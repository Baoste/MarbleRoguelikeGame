import math
import random
import tempfile
from pathlib import Path
import unittest

import torch

from game_ai.network import PolicyValue
from game_ai.search import TreeSearch
from game_ai.training import Transition, returns, update
from game_ai.environment import validate_observation


def action(key, operation, definition=0):
    features = [0.] * 20
    features[operation] = 1.
    return dict(key=key, kind=key, definitionId=definition, features=features)


def observation(actions, phase='Shop'):
    return dict(protocol=1, globals=[0.] * 26, entities=[], actions=actions, phase=phase, digest=phase)


class LearningTests(unittest.TestCase):
    def setUp(self):
        torch.manual_seed(3)
        random.seed(3)
        torch.set_num_threads(1)

    def test_operation_probability_is_not_multiplied_by_number_of_cells(self):
        model = PolicyValue([])
        for p in model.parameters():
            torch.nn.init.zeros_(p)
        obs = observation([action('begin', 10)] + [action(f'place{i}', 6) for i in range(200)])
        logits, _ = model(obs)
        probabilities = logits.softmax(0)
        self.assertAlmostEqual(float(probabilities[0]), .5, places=5)
        self.assertAlmostEqual(float(probabilities[1:].sum()), .5, places=5)

    def test_policy_learns_better_reward_without_handwritten_strategy(self):
        model = PolicyValue([], width=32)
        optimizer = torch.optim.Adam(model.parameters(), lr=.01)
        obs = observation([action('a', 0), action('b', 1)])
        with torch.no_grad():
            initial = float(model(obs)[0].softmax(0)[1])
        for _ in range(25):
            trajectories, targets = [], []
            for _ in range(32):
                index, logp, value = model.decide(obs)
                reward = float(index == 1)
                trajectories.append(Transition(obs, index, logp, value, reward))
                targets.append(reward)
            update(model, optimizer, trajectories, targets, epochs=1, entropy_weight=0)
        with torch.no_grad():
            learned = float(model(obs)[0].softmax(0)[1])
        self.assertGreater(learned, .95)
        self.assertGreater(learned, initial)

    def test_search_targets_do_not_use_ppo_likelihood_ratio(self):
        model = PolicyValue([], width=32)
        optimizer = torch.optim.Adam(model.parameters(), lr=.01)
        obs = observation([action('a', 0), action('b', 1)])
        samples = [Transition(obs, 1, float('nan'), 0, 1, [0., 1.])]
        loss = update(model, optimizer, samples, [1.], epochs=1)
        self.assertTrue(math.isfinite(loss))

    def test_terminal_and_truncated_return_targets(self):
        self.assertEqual(returns([1, 2, 3]), [6, 5, 3])
        self.assertEqual(returns([1, 2, 3], bootstrap=4), [10, 9, 7])

    def test_variable_entity_list_and_unknown_content(self):
        model = PolicyValue([9020], width=32)
        obs = observation([action('activate', 13, 9020)])
        obs['entities'] = [dict(kind=0, definitionId=9020, values=[0.] * 16, status='充能 2/3')]
        logits, value = model(obs)
        (logits.sum() + value).backward()
        self.assertEqual(logits.shape, (1,))
        obs['actions'][0]['definitionId'] = 99
        with self.assertRaises(ValueError):
            model(obs)

    def test_checkpoint_roundtrip(self):
        model = PolicyValue([9020], width=32)
        obs = observation([action('a', 0)])
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'model.pt'
            model.save(path, dict(test=True))
            other, data = PolicyValue.load(path)
            self.assertTrue(data['metadata']['test'])
            self.assertTrue(torch.equal(model(obs)[1], other(obs)[1]))

    def test_duplicate_action_keys_are_rejected(self):
        obs = observation([action('a', 0), action('a', 1)])
        with self.assertRaises(ValueError):
            validate_observation(obs)

    def test_stochastic_search_averages_outcomes_and_samples_new_futures(self):
        class Model:
            def __call__(self, obs):
                return torch.zeros(len(obs['actions'])), torch.tensor(0.)
        class Source:
            state = dict(observation=observation([action('safe', 0), action('risky', 1)]))
        class Worker:
            seeds = []
            def restore_prefix(self, source, seed):
                self.seeds.append(seed)
                self.rng = random.Random(seed)
                return source.state
            def step(self, key):
                reward = 1. if key == 'safe' else (20. if self.rng.random() < .02 else 0.)
                return dict(reward=reward, terminated=True, truncated=False,
                            observation=observation([], 'Won'))
        worker = Worker()
        search = TreeSearch(Model(), worker, simulations=1200, seed=8)
        probabilities = search.run(Source())
        self.assertAlmostEqual(sum(probabilities), 1)
        self.assertGreater(probabilities[0], .8)
        self.assertEqual(len(worker.seeds), 1200)
        self.assertEqual(len(set(worker.seeds)), 1200)


if __name__ == '__main__':
    unittest.main()
