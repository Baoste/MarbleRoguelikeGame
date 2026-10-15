from __future__ import annotations

import argparse
import contextlib
from datetime import datetime
import hashlib
import json
from pathlib import Path
import random
import statistics
import subprocess
import time

from .environment import ROOT, UnityEnvironment, assert_same_state


def project_path(value):
    path = (ROOT / value).resolve()
    if path != ROOT and ROOT not in path.parents:
        raise ValueError('Output paths must stay inside the current project.')
    return path


def parser():
    result = argparse.ArgumentParser(description='Learn to play the real Unity marble game.')
    result.add_argument('command', choices=['train', 'evaluate', 'replay', 'verify-replay'])
    result.add_argument('--exe', default='.utmp/game-ai/player/GameAI.exe')
    result.add_argument('--device', choices=['cpu', 'cuda'], default='cpu')
    result.add_argument('--checkpoint')
    result.add_argument('--output', help='New experiment directory; defaults to a timestamped directory.')
    result.add_argument('--trace')
    result.add_argument('--episodes', type=int, default=10)
    result.add_argument('--seed', type=int, default=1)
    result.add_argument('--search', type=int, default=0, help='MCTS simulations per Shop/Build decision; use 0 to warm up PPO.')
    result.add_argument('--rollout-steps', type=int, default=64)
    result.add_argument('--max-actions', type=int, default=3000)
    result.add_argument('--max-ticks', type=int, default=108000)
    result.add_argument('--decision-ticks', type=int, default=6)
    result.add_argument('--reward-scale', type=float, default=1000)
    result.add_argument('--threads', type=int, default=2)
    result.add_argument('--learning-rate', type=float, default=.0003)
    result.add_argument('--visible', action='store_true')
    result.add_argument('--greedy', action='store_true', help='Evaluate argmax actions instead of sampling the learned policy.')
    result.add_argument('--random', action='store_true', help='Evaluate a legal random baseline (uniform operation, then candidate).')
    return result


def run_metadata(args, environment):
    commit = subprocess.run(['git', 'rev-parse', 'HEAD'], cwd=ROOT, capture_output=True, text=True)
    diff = subprocess.run(['git', '-c', f'lfs.storage={ROOT / ".utmp/game-ai/lfs"}', 'diff', '--binary'],
                          cwd=ROOT, capture_output=True)
    trainer_sources = {path.relative_to(ROOT).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest()
                       for path in sorted((ROOT / 'Tools/GameAI').rglob('*'))
                       if path.is_file() and path.suffix in ('.py', '.ps1', '.txt')}
    return dict(arguments=vars(args), commit=commit.stdout.strip(),
                working_diff_sha256=hashlib.sha256(diff.stdout).hexdigest(),
                trainer_sources=trainer_sources, build_manifest=environment.manifest,
                content_hash=environment.state['contentHash'],
                unity_version=environment.state['unityVersion'])


def main():
    args = parser().parse_args()
    if args.episodes < 1 or args.search < 0 or args.threads < 1:
        raise ValueError('Invalid episode, search or CPU thread count.')
    executable = project_path(args.exe)
    if args.command in ('replay', 'verify-replay'):
        if not args.trace:
            raise ValueError('Provide --trace for replay.')
        replay(args, executable)
        return
    if args.output is None:
        args.output = '.utmp/game-ai/runs/' + datetime.now().strftime('%Y%m%d-%H%M%S-%f')
    output = project_path(args.output)
    if output.exists() and any(output.iterdir()):
        raise ValueError('Output directory is not empty. Use a new directory; resume with --checkpoint.')
    output.mkdir(parents=True, exist_ok=True)
    if args.command == 'evaluate' and not args.random and not args.checkpoint:
        raise ValueError('Evaluation requires --checkpoint or --random.')
    import torch
    from .network import PolicyValue
    from .search import TreeSearch
    from .training import collect_episode, update
    torch.set_num_threads(args.threads)
    if args.device == 'cuda' and not torch.cuda.is_available():
        raise RuntimeError('CUDA is unavailable; use --device cpu or install a compatible GPU wheel.')
    torch.manual_seed(args.seed)
    random.seed(args.seed)
    config = dict(maxActions=args.max_actions, maxTicks=args.max_ticks,
                  decisionTicks=args.decision_ticks, rewardScale=args.reward_scale)
    # Evaluation uses a separate default seed interval; exact episode seeds are always recorded.
    first_seed = args.seed if args.command == 'train' else args.seed + 1_000_000
    records = []
    with contextlib.ExitStack() as stack:
        environment = stack.enter_context(UnityEnvironment(executable, visible=args.visible))
        environment.reset(first_seed, config)
        if args.checkpoint:
            model, checkpoint = PolicyValue.load(project_path(args.checkpoint), args.device)
            if model.catalog_ids != sorted(environment.state['catalogIds']):
                raise ValueError('Checkpoint vocabulary differs from the game catalog.')
            expected_hash = checkpoint['metadata'].get('content_hash')
            if expected_hash and expected_hash != environment.state['contentHash']:
                raise ValueError('Checkpoint content differs from this game; create a separate experiment.')
            expected_scale = checkpoint['metadata'].get('arguments', {}).get('reward_scale', args.reward_scale)
            if expected_scale != args.reward_scale:
                raise ValueError('Reward scale differs from the checkpoint.')
        else:
            model = PolicyValue(environment.state['catalogIds']).to(args.device)
            checkpoint = {}
        optimizer = torch.optim.Adam(model.parameters(), lr=args.learning_rate)
        if checkpoint.get('optimizer') and args.command == 'train':
            optimizer.load_state_dict(checkpoint['optimizer'])
        search = None
        if args.search:
            if args.random:
                raise ValueError('Random baseline does not use learned search.')
            worker = stack.enter_context(UnityEnvironment(executable, search_worker=True))
            search = TreeSearch(model, worker, args.search, rollout_steps=args.rollout_steps,
                                seed=args.seed + 7, root_noise=args.command == 'train')
        metadata = run_metadata(args, environment)
        (output / 'manifest.json').write_text(json.dumps(metadata, indent=2, ensure_ascii=False), encoding='utf-8')
        for episode in range(args.episodes):
            if episode:
                environment.reset(first_seed + episode, config)
            started = time.monotonic()
            model.eval()
            if args.command == 'train':
                trajectory, targets = collect_episode(environment, model, search,
                    lambda n, r: print(f'episode={episode + 1} actions={n} score={r["finalScore"]}', flush=True) if n % 100 == 0 else None)
                model.train()
                loss = update(model, optimizer, trajectory, targets)
                model.save(output / 'latest.pt', dict(metadata, episode=episode + 1), optimizer)
            else:
                loss = None
                while not environment.state['terminated'] and not environment.state['truncated']:
                    obs = environment.state['observation']
                    if args.random:
                        kind = random.choice(sorted({a['kind'] for a in obs['actions']}))
                        action = random.choice([a for a in obs['actions'] if a['kind'] == kind])
                    elif search and obs['phase'] in ('Shop', 'Build'):
                        distribution = search.run(environment)
                        action = obs['actions'][max(range(len(distribution)), key=distribution.__getitem__)]
                    else:
                        index, _, _ = model.decide(obs, greedy=args.greedy)
                        action = obs['actions'][index]
                    environment.step(action['key'])
            record = dict(episode=episode + 1, seed=first_seed + episode, score=environment.state['finalScore'],
                          terminated=environment.state['terminated'], truncated=environment.state['truncated'],
                          reason=environment.state['reason'], actions=environment.state['steps'], ticks=environment.state['ticks'],
                          seconds=time.monotonic() - started, loss=loss)
            records.append(record)
            environment.save_trace(output / f'episode-{episode + 1:05}.json', record)
            with (output / 'metrics.jsonl').open('a', encoding='utf-8') as stream:
                stream.write(json.dumps(record, allow_nan=False) + '\n')
            print(json.dumps(record), flush=True)
        summary = summarize(records)
        (output / 'summary.json').write_text(json.dumps(summary, indent=2), encoding='utf-8')
        print(json.dumps(summary), flush=True)


def replay(args, executable):
    data = json.loads(project_path(args.trace).read_text(encoding='utf-8'))
    with UnityEnvironment(executable, visible=args.visible) as environment:
        response = environment.reset(data['seed'], data['config'])
        assert_same_state(response, data['initial'], 'reset')
        for index, item in enumerate(data['trace']):
            response = environment.step(item['action'])
            if (response['observation']['digest'] != item['digest'] or response['ticks'] != item['ticks'] or
                    abs(response['reward'] - item['reward']) > 1e-5):
                raise RuntimeError(f'Replay mismatch at action {index}.')
        assert_same_state(response, data['final'], 'final')
        print(json.dumps(dict(replay='passed', actions=len(data['trace']), score=response['finalScore'])))


def summarize(records):
    completed = [r['score'] for r in records if r['terminated']]
    result = dict(episodes=len(records), completed=len(completed),
                  truncated=sum(r['truncated'] for r in records),
                  won=sum(r['reason'] == 'Won' for r in records),
                  ticks_per_second=sum(r['ticks'] for r in records) / max(.001, sum(r['seconds'] for r in records)))
    if completed:
        rng = random.Random(42)
        means = sorted(statistics.mean(rng.choices(completed, k=len(completed))) for _ in range(1000))
        result.update(completed_mean=statistics.mean(completed), completed_median=statistics.median(completed),
                      completed_min=min(completed), completed_max=max(completed),
                      completed_mean_bootstrap_95=[means[24], means[974]])
    return result
