from __future__ import annotations

from dataclasses import dataclass
import random

import torch
from torch.distributions import Categorical


@dataclass
class Transition:
    observation: dict
    action: int
    old_log_probability: float
    old_value: float
    reward: float
    search_target: list = None


def returns(rewards, bootstrap=0.0):
    """Undiscounted remaining score; bootstrap only when a rollout was truncated."""
    result = [0.0] * len(rewards)
    value = bootstrap
    for i in range(len(rewards) - 1, -1, -1):
        value = rewards[i] + value
        result[i] = value
    return result


@torch.no_grad()
def collect_episode(environment, model, search=None, callback=None):
    trajectory = []
    while not environment.state['terminated'] and not environment.state['truncated']:
        observation = environment.state['observation']
        target = None
        index, logp, value = model.decide(observation)
        if search and observation['phase'] in ('Shop', 'Build'):
            target = search.run(environment)
            index = int(Categorical(probs=torch.tensor(target)).sample())
            # This action is trained by search distillation, never by an on-policy PPO ratio.
            logp = 0.0
        response = environment.step(observation['actions'][index]['key'])
        trajectory.append(Transition(observation, index, logp, value, response['reward'], target))
        if callback:
            callback(len(trajectory), response)
    bootstrap = 0.0
    if environment.state['truncated']:
        _, bootstrap = model(environment.state['observation'])
        bootstrap = float(bootstrap)
    targets = returns([item.reward for item in trajectory], bootstrap)
    return trajectory, targets


def update(model, optimizer, trajectory, targets, epochs=3, batch_size=32, clip=.2, entropy_weight=.01):
    if not trajectory:
        raise ValueError('No transitions to train on.')
    device = model.device
    advantages = torch.tensor([target - item.old_value for item, target in zip(trajectory, targets)], device=device)
    on_policy = torch.tensor([item.search_target is None for item in trajectory], device=device)
    if int(on_policy.sum()) > 1:
        selected = advantages[on_policy]
        advantages[on_policy] = (selected - selected.mean()) / (selected.std(unbiased=False) + 1e-8)
    indices = list(range(len(trajectory)))
    losses = []
    for _ in range(epochs):
        random.shuffle(indices)
        for start in range(0, len(indices), batch_size):
            optimizer.zero_grad(set_to_none=True)
            batch = indices[start:start + batch_size]
            batch_loss = 0.0
            for i in batch:
                item = trajectory[i]
                logits, value = model(item.observation)
                distribution = Categorical(logits=logits)
                if item.search_target is not None:
                    target = torch.tensor(item.search_target, dtype=torch.float32, device=device)
                    actor_loss = -(target * torch.log_softmax(logits, 0)).sum()
                else:
                    logp = distribution.log_prob(torch.tensor(item.action, device=device))
                    ratio = torch.exp(logp - item.old_log_probability)
                    actor_loss = -torch.minimum(ratio * advantages[i], ratio.clamp(1 - clip, 1 + clip) * advantages[i])
                value_loss = (value - targets[i]).square()
                loss = actor_loss + .5 * value_loss - entropy_weight * distribution.entropy()
                if not bool(torch.isfinite(loss)):
                    raise FloatingPointError('Non-finite training loss; inspect reward scale and traces.')
                (loss / len(batch)).backward()
                batch_loss += float(loss.detach()) / len(batch)
            torch.nn.utils.clip_grad_norm_(model.parameters(), 1.0, error_if_nonfinite=True)
            optimizer.step()
            losses.append(batch_loss)
    return sum(losses) / len(losses)
