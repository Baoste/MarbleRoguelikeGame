"""PUCT with sampled chance outcomes, public-observation branches and independent futures."""
from __future__ import annotations

from dataclasses import dataclass, field
import math
import random

import torch


@dataclass
class Edge:
    prior: float
    visits: int = 0
    total: float = 0.0
    outcomes: dict = field(default_factory=dict)


class Node:
    def __init__(self, observation, priors):
        self.observation = observation
        self.edges = {a['key']: Edge(float(p)) for a, p in zip(observation['actions'], priors)}

    def select(self, rng, exploration=1.5, widening=3):
        visits = sum(e.visits for e in self.edges.values())
        count = min(len(self.edges), max(1, int(widening * math.sqrt(visits + 1))))
        ranked = sorted(self.edges, key=lambda a: self.edges[a].prior, reverse=True)
        candidates = set(ranked[:count])
        candidates.update(a for a, e in self.edges.items() if e.visits)
        # Persistent nonzero probability of exploring an action beyond the top policy proposals.
        if rng.random() < .15:
            candidates.add(rng.choice(ranked))
        def score(key):
            edge = self.edges[key]
            q = edge.total / edge.visits if edge.visits else 0.0
            # Bounded selection scale only; stored returns and the training objective stay linear.
            bounded_q = q / (1 + abs(q))
            return bounded_q + exploration * edge.prior * math.sqrt(visits + 1) / (1 + edge.visits)
        return max(sorted(candidates), key=score)


class TreeSearch:
    def __init__(self, model, worker, simulations=16, depth=8, rollout_steps=64, seed=123, root_noise=True):
        if simulations < 1 or depth < 1 or rollout_steps < 1:
            raise ValueError('Search budgets must be positive.')
        self.model, self.worker = model, worker
        self.simulations, self.depth, self.rollout_steps = simulations, depth, rollout_steps
        self.rng = random.Random(seed)
        self.root_noise = root_noise

    @torch.no_grad()
    def expand(self, observation):
        logits, value = self.model(observation)
        priors = torch.softmax(logits, 0).cpu().tolist()
        return Node(observation, priors), float(value)

    @torch.no_grad()
    def rollout(self, response):
        reward = 0.0
        for _ in range(self.rollout_steps):
            if response['terminated']:
                return reward
            if response['truncated']:
                break
            observation = response['observation']
            index, _, _ = self.model.decide(observation)
            response = self.worker.step(observation['actions'][index]['key'])
            reward += response['reward']
        if not response['terminated']:
            _, value = self.model(response['observation'])
            reward += float(value)
        return reward

    @torch.no_grad()
    def run(self, source):
        observation = source.state['observation']
        if observation['phase'] not in ('Shop', 'Build'):
            raise ValueError('Search only supports Shop/Build boundaries.')
        root, _ = self.expand(observation)
        # Root exploration noise trains discovery without handcrafted purchase/layout rules.
        keys = list(root.edges)
        if self.root_noise:
            noise = [self.rng.gammavariate(.3, 1) for _ in keys]
            total = sum(noise)
            for key, value in zip(keys, noise):
                root.edges[key].prior = .75 * root.edges[key].prior + .25 * value / total
        for _ in range(self.simulations):
            response = self.worker.restore_prefix(source, self.rng.randrange(1, 2**32))
            node = root
            path = []
            leaf_value = 0.0
            for _ in range(self.depth):
                key = node.select(self.rng)
                edge = node.edges[key]
                response = self.worker.step(key)
                path.append((edge, response['reward']))
                if response['terminated']:
                    break
                next_observation = response['observation']
                if response['truncated'] or next_observation['phase'] not in ('Shop', 'Build'):
                    leaf_value = self.rollout(response)
                    break
                digest = next_observation['digest']
                if digest not in edge.outcomes:
                    child, _ = self.expand(next_observation)
                    edge.outcomes[digest] = child
                    leaf_value = self.rollout(response)
                    break
                node = edge.outcomes[digest]
            else:
                leaf_value = self.rollout(response)
            for edge, reward in reversed(path):
                leaf_value += reward
                edge.visits += 1
                edge.total += leaf_value  # single player: no negation
        counts = [root.edges[a['key']].visits for a in observation['actions']]
        total = sum(counts)
        return [n / total for n in counts]
