"""Variable-size set encoder and candidate-action policy. No hardcoded device rankings."""
from __future__ import annotations

import torch
from torch import nn
from torch.distributions import Categorical


class PolicyValue(nn.Module):
    def __init__(self, catalog_ids, width=96):
        super().__init__()
        self.catalog_ids = sorted(set(int(x) for x in catalog_ids))
        self.id_to_index = {x: i + 1 for i, x in enumerate(self.catalog_ids)}
        self.width = width
        self.identity = nn.Embedding(len(self.catalog_ids) + 1, 16)
        self.kind = nn.Embedding(5, 8)
        self.byte = nn.Embedding(256, 8)
        self.entity = nn.Sequential(nn.Linear(48, width), nn.Tanh(), nn.Linear(width, width), nn.Tanh())
        self.attention = nn.MultiheadAttention(width, 4, batch_first=True)
        self.global_encoder = nn.Sequential(nn.Linear(26, width), nn.Tanh())
        self.state_encoder = nn.Sequential(nn.Linear(width * 3, width), nn.Tanh())
        self.policy = nn.Sequential(nn.Linear(width + 36, width), nn.Tanh(), nn.Linear(width, 1))
        self.operation = nn.Linear(width, 14)
        self.value = nn.Sequential(nn.Linear(width, width), nn.Tanh(), nn.Linear(width, 1))

    @property
    def device(self):
        return next(self.parameters()).device

    def ids(self, values):
        result = []
        for value in values:
            value = int(value)
            if value and value not in self.id_to_index:
                raise ValueError(f'Unknown definition {value}; rebuild the model vocabulary for this content.')
            result.append(self.id_to_index.get(value, 0))
        return torch.tensor(result, dtype=torch.long, device=self.device)

    def forward(self, observation):
        entities = observation['entities']
        if entities:
            numeric = torch.tensor([e['values'] for e in entities], dtype=torch.float32, device=self.device)
            kinds = torch.tensor([e['kind'] for e in entities], dtype=torch.long, device=self.device)
            identities = self.identity(self.ids([e['definitionId'] for e in entities]))
            statuses = []
            for entity in entities:
                data = list((entity.get('status') or '').encode('utf-8'))
                statuses.append(self.byte(torch.tensor(data, device=self.device, dtype=torch.long)).mean(0)
                                if data else torch.zeros(8, device=self.device))
            tokens = self.entity(torch.cat((numeric, self.kind(kinds), identities, torch.stack(statuses)), -1))
            attended, _ = self.attention(tokens[None], tokens[None], tokens[None], need_weights=False)
            tokens = tokens + attended[0]
            pooled = torch.cat((tokens.mean(0), tokens.max(0).values))
        else:
            pooled = torch.zeros(self.width * 2, device=self.device)
        globals_ = torch.tensor(observation['globals'], dtype=torch.float32, device=self.device)
        state = self.state_encoder(torch.cat((self.global_encoder(globals_), pooled)))
        value = self.value(state).squeeze(-1)
        actions = observation['actions']
        if not actions:
            return torch.empty(0, device=self.device), value
        features = torch.tensor([a['features'] for a in actions], dtype=torch.float32, device=self.device)
        ids = self.identity(self.ids([a['definitionId'] for a in actions]))
        scores = self.policy(torch.cat((state.expand(len(actions), -1), features, ids), -1)).squeeze(-1)
        # Choose an operation first, then a legal parameterized candidate. A thousand placement
        # cells must not give placement a thousand times the initial mass of BeginRound.
        kinds = features[:, :14].argmax(-1)
        operation_scores = self.operation(state)
        present = torch.unique(kinds)
        operation_log_probs = torch.log_softmax(operation_scores[present], 0)
        logits = torch.empty_like(scores)
        for i, kind in enumerate(present):
            mask = kinds == kind
            logits[mask] = operation_log_probs[i] + torch.log_softmax(scores[mask], 0)
        return logits, value

    @torch.no_grad()
    def decide(self, observation, greedy=False):
        logits, value = self(observation)
        if not len(logits):
            raise ValueError('Cannot choose an action in a terminal/truncated state.')
        distribution = Categorical(logits=logits)
        index = logits.argmax() if greedy else distribution.sample()
        return int(index), float(distribution.log_prob(index)), float(value)

    def save(self, path, metadata=None, optimizer=None):
        torch.save(dict(schema=1, catalog_ids=self.catalog_ids, width=self.width,
                        model=self.state_dict(), metadata=metadata or {},
                        optimizer=optimizer.state_dict() if optimizer else None), path)

    @classmethod
    def load(cls, path, device='cpu'):
        data = torch.load(path, map_location=device, weights_only=True)
        if data.get('schema') != 1:
            raise ValueError('Unsupported checkpoint schema.')
        model = cls(data['catalog_ids'], data['width']).to(device)
        model.load_state_dict(data['model'])
        return model, data
