import copy
import json
import socket
import threading
import unittest

from game_ai.environment import UnityEnvironment
from game_ai.cli import summarize


def state(digest='start'):
    return dict(contentHash='content', reward=0, ticks=0, steps=0, terminated=False, truncated=False,
                observation=dict(protocol=1, globals=[0.] * 26, entities=[], actions=[], phase='Build', digest=digest))


class ProtocolTests(unittest.TestCase):
    def test_json_lines_transport_and_remote_errors(self):
        listener = socket.socket()
        listener.bind(('127.0.0.1', 0))
        listener.listen()
        port = listener.getsockname()[1]
        def server():
            connection, _ = listener.accept()
            with connection, connection.makefile('rwb') as stream:
                for _ in range(3):
                    request = json.loads(stream.readline())
                    response = state()
                    response['requestId'] = request['requestId']
                    if request['command'] == 'step':
                        response['error'] = 'illegal action'
                    stream.write((json.dumps(response) + '\n').encode())
                    stream.flush()
            listener.close()
        thread = threading.Thread(target=server)
        thread.start()
        try:
            with UnityEnvironment(port=port, timeout=5) as environment:
                environment.reset(1)
                with self.assertRaisesRegex(RuntimeError, 'illegal action'):
                    environment.step('forbidden')
        finally:
            thread.join(timeout=5)
        self.assertFalse(thread.is_alive())

    def test_replay_rejects_a_different_root(self):
        class Worker(UnityEnvironment):
            def __init__(self):
                self.search_worker = True
            def reset(self, seed, config):
                self.state = state('different')
                return self.state
        class Source:
            seed = 1
            config = {}
            initial = state()
            trace = []
            state = copy.deepcopy(initial)
        with self.assertRaisesRegex(RuntimeError, 'mismatch'):
            Worker().restore_prefix(Source(), 99)

    def test_truncations_are_reported_separately_from_completed_score(self):
        report = summarize([dict(score=100, terminated=True, truncated=False, reason='Won', ticks=60, seconds=1),
                            dict(score=99999, terminated=False, truncated=True, reason='budget', ticks=60, seconds=1)])
        self.assertEqual(report['completed_mean'], 100)
        self.assertEqual(report['truncated'], 1)
        self.assertEqual(report['completed'], 1)


if __name__ == '__main__':
    unittest.main()
