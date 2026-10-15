"""Loopback protocol and deterministic-prefix replay. No game rules are simulated in Python."""
from __future__ import annotations

import copy
import contextlib
import json
import os
from pathlib import Path
import socket
import subprocess
import time

ROOT = Path(__file__).resolve().parents[3]
DEFAULT_CONFIG = dict(decisionTicks=6, gridColumns=7, gridRows=9,
                      maxActions=3000, maxTicks=108000, rewardScale=1000.0)


class UnityEnvironment:
    def __init__(self, executable=None, port=0, search_worker=False, visible=False, timeout=180):
        self.process = None
        self.search_worker = search_worker
        self.request_id = 0
        self.trace = []
        self.seed = None
        self.initial = None
        self.state = None
        self.config = dict(DEFAULT_CONFIG)
        self.manifest = None
        if executable:
            executable = Path(executable).resolve()
            if not executable.is_file():
                raise FileNotFoundError(f"Build the Unity training player first: {executable}")
            if not port:
                with socket.socket() as probe:
                    probe.bind(('127.0.0.1', 0))
                    port = probe.getsockname()[1]
            logs = ROOT / '.utmp/game-ai/logs'
            logs.mkdir(parents=True, exist_ok=True)
            command = [str(executable), '--game-ai-port', str(port), '-logFile', str(logs / f'player-{port}.log')]
            if search_worker:
                command.append('--game-ai-search-worker')
            if not visible:
                command.extend(['-batchmode', '-nographics'])
            flags = subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0
            self.process = subprocess.Popen(command, cwd=str(ROOT), creationflags=flags,
                                            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            manifest = executable.parent / 'manifest.json'
            if manifest.exists():
                self.manifest = json.loads(manifest.read_text(encoding='utf-8-sig'))
        elif not port:
            raise ValueError('Provide an executable or a listening port.')
        deadline = time.monotonic() + timeout
        try:
            while True:
                try:
                    self.socket = socket.create_connection(('127.0.0.1', port), timeout=2)
                    break
                except OSError:
                    if self.process and self.process.poll() is not None:
                        raise RuntimeError(f'Unity exited with {self.process.returncode}; see .utmp/game-ai/logs.')
                    if time.monotonic() >= deadline:
                        raise TimeoutError('Unity connection timed out; inspect the player log.')
                    time.sleep(.1)
            self.socket.settimeout(timeout)
            self.stream = self.socket.makefile('rwb')
            self.rpc('hello')
        except Exception:
            self.close()
            raise

    def rpc(self, command, **kwargs):
        self.request_id += 1
        request = dict(requestId=self.request_id, command=command, **kwargs)
        self.stream.write((json.dumps(request, allow_nan=False) + '\n').encode('utf-8'))
        self.stream.flush()
        line = self.stream.readline(32 * 1024 * 1024)
        if not line or not line.endswith(b'\n'):
            raise ConnectionError('Missing or oversized Unity response.')
        response = json.loads(line)
        if response.get('requestId') != self.request_id:
            raise RuntimeError('Protocol request/response mismatch.')
        if response.get('error'):
            raise RuntimeError(response['error'])
        return response

    def reset(self, seed, config=None):
        if not 0 < int(seed) < 2**32:
            raise ValueError('Episode seeds must be uint32 values greater than zero.')
        self.seed = int(seed)
        self.config = dict(DEFAULT_CONFIG, **(config or {}))
        self.state = self.rpc('reset', seed=self.seed, config=self.config)
        self.initial = copy.deepcopy(self.state)
        self.trace = []
        validate_observation(self.state['observation'])
        return self.state

    def step(self, action_key):
        self.state = self.rpc('step', actionKey=action_key)
        validate_observation(self.state['observation'])
        self.trace.append(dict(action=action_key, digest=self.state['observation']['digest'],
                               reward=self.state['reward'], ticks=self.state['ticks']))
        return self.state

    def restore_prefix(self, source, future_seed=None):
        """Recreate the *observed past*, verify every step, then sample an independent future."""
        if source is self:
            raise ValueError('Search needs a separate worker.')
        response = self.reset(source.seed, source.config)
        assert_same_state(response, source.initial, 'reset')
        for index, item in enumerate(source.trace):
            response = self.step(item['action'])
            if (response['observation']['digest'] != item['digest'] or
                    response['ticks'] != item['ticks'] or abs(response['reward'] - item['reward']) > 1e-5):
                raise RuntimeError(f'Replay diverged at action {index}; search is disabled for this environment.')
        assert_same_state(response, source.state, 'root')
        if future_seed is not None:
            if not self.search_worker:
                raise ValueError('Independent future sampling requires a search worker.')
            if response['observation']['phase'] not in ('Shop', 'Build'):
                raise ValueError('Protocol v1 searches only Shop/Build boundaries.')
            self.state = self.rpc('sample_future', seed=int(future_seed))
            assert_same_state(self.state, response, 'future randomization changed public state')
        return self.state

    def save_trace(self, path, metadata=None):
        Path(path).write_text(json.dumps(dict(protocol=1, seed=self.seed, config=self.config,
            initial=self.initial, trace=self.trace, final=self.state, manifest=self.manifest,
            metadata=metadata or {}), ensure_ascii=False, indent=2), encoding='utf-8')

    def close(self):
        if hasattr(self, 'stream'):
            with contextlib.suppress(OSError):
                self.stream.close()
        if hasattr(self, 'socket'):
            with contextlib.suppress(OSError):
                self.socket.close()
        if self.process and self.process.poll() is None:
            self.process.terminate()
            try:
                self.process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                self.process.kill()
                self.process.wait(timeout=10)

    def __enter__(self):
        return self

    def __exit__(self, *_):
        self.close()


def validate_observation(observation):
    if observation.get('protocol') != 1 or len(observation['globals']) != 26:
        raise ValueError('Unsupported observation schema.')
    keys = [x['key'] for x in observation['actions']]
    if len(keys) != len(set(keys)):
        raise ValueError('Unity returned duplicate action keys.')
    if any(len(x['features']) != 20 for x in observation['actions']):
        raise ValueError('Invalid action feature width.')
    if any(len(x['values']) != 16 for x in observation['entities']):
        raise ValueError('Invalid entity feature width.')


def assert_same_state(a, b, location):
    if a['contentHash'] != b['contentHash'] or a['observation']['digest'] != b['observation']['digest']:
        raise RuntimeError(f'Environment replay mismatch at {location}. Do not trust search results.')
