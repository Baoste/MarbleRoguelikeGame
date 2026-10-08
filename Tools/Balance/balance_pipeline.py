#!/usr/bin/env python3
"""Reproducible hybrid balance pipeline for Blood Pachinko.

The Unity editor produces Docs/Balance/physical_profile.json from the authored
scene and the gameplay PhysX bridge.  This program consumes that empirical
distribution, runs campaign-level Monte Carlo, exports raw rows/aggregates and
builds the balance reports.  It refuses to silently invent a physical profile.
"""

from __future__ import annotations

import argparse
import copy
import csv
import hashlib
import heapq
import json
import math
import random
import statistics
from collections import Counter, defaultdict
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Iterable


STRATEGY_NAMES = ("Conservative", "Balanced", "Aggressive")
CONFIG_NAMES = ("Baseline", "Conservative", "Recommended", "Experimental")
PRICE_CONFIG_NAMES = ("Current", "PriceConservative", "PriceRecommended", "PriceExperimental")
ITEM_CONFIG_NAMES = ("Current", "ItemConservative", "ItemRecommended", "ItemExperimental")
SPECIAL_BALL_NOTE = "N/A: the current runtime has no SpecialBall catalog or persistent special-ball inventory."


@dataclass(frozen=True)
class Strategy:
    name: str
    normal_flow: float
    rush_flow: float
    cashout_blood_fraction: float
    score_margin: float
    spend_fraction: float
    max_refreshes: int
    utility_threshold: float
    timing_skill: float
    duplicate_exposure: float


STRATEGIES = {
    "Conservative": Strategy("Conservative", 0.25, 0.25, 1.01, 1.00, 0.45, 0, 0.015, 0.55, 0.65),
    "Balanced": Strategy("Balanced", 0.0, 1.0, 0.55, 1.15, 0.72, 1, 0.020, 0.80, 0.75),
    "Aggressive": Strategy("Aggressive", 0.5, 1.0, 0.15, 1.45, 0.95, 2, 0.012, 0.95, 0.82),
}


@dataclass
class Outcome:
    kind: str
    target_id: int
    multiplier: float
    rush_chance: float
    rush_enabled: bool
    count: int
    flight_seconds: float


@dataclass
class RouteProfile:
    lane: str
    flow: float
    samples: int
    outcomes: list[Outcome]
    cumulative: list[int]
    hotspots: dict[float, float]
    mean_flight: float

    def draw(self, rng: random.Random) -> Outcome:
        pick = rng.randrange(self.cumulative[-1])
        lo, hi = 0, len(self.cumulative)
        while lo < hi:
            mid = (lo + hi) // 2
            if pick < self.cumulative[mid]:
                hi = mid
            else:
                lo = mid + 1
        return self.outcomes[lo]

    def hotspot(self, radius: float) -> float:
        key = min(self.hotspots, key=lambda value: abs(value - radius))
        return self.hotspots[key]


@dataclass
class BuildState:
    devices: Counter[int]
    drugs: Counter[int]
    purchased_devices: Counter[int] = field(default_factory=Counter)
    purchased_drugs: Counter[int] = field(default_factory=Counter)
    coins_spent: int = 0
    purchase_spend: int = 0
    refresh_spend: int = 0
    purchases: int = 0
    refreshes: int = 0


@dataclass
class Ball:
    settle_at: float
    outcome: Outcome
    value: float
    cost: float
    rush_add: float
    rush_duration: float
    rush_score_multiplier: float
    clover_probability: float
    revive: bool
    banked_coins: int
    consumed: bool


@dataclass
class RoundResult:
    score: int
    pending_score: int
    gambling_multiplier: int
    balls: int
    duration: float
    blood_remaining: float
    cashout: bool
    cashout_coins: int
    bank_coins: int
    rush_seconds: float
    dead_run: bool
    route: str


@dataclass(frozen=True)
class ShopAffordabilitySnapshot:
    stage: int
    coins_before_shop: int
    required_purchases: int
    required_refreshes: int
    bundle_cost: int
    affordable: bool


@dataclass
class RunResult:
    config: str
    strategy: str
    run_index: int
    seed: int
    won: bool
    failed_stage: int
    rounds_played: int
    stages_cleared: int
    total_score: int
    score_variance: float
    coins_income: int
    cashout_income: int
    bank_income: int
    stage_reward_income: int
    overkill_income: int
    coins_spent: int
    purchase_spend: int
    refresh_spend: int
    ending_coins: int
    balls_launched: int
    duration: float
    blood_remaining: float
    cashouts: int
    purchases: int
    refreshes: int
    device_count: int
    drug_count: int
    dead_run: bool
    purchase_devices: Counter[int]
    purchase_drugs: Counter[int]
    build_snapshots: list[tuple[int, float, int, int]]
    shop_affordability_snapshots: list[ShopAffordabilitySnapshot]


def read_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def stable_fingerprint(*paths: Path) -> str:
    digest = hashlib.sha256()
    for path in paths:
        digest.update(path.as_posix().encode("utf-8"))
        digest.update(path.read_bytes())
    return digest.hexdigest()


def load_physics(path: Path) -> tuple[dict[tuple[str, float], RouteProfile], dict[str, Any]]:
    if not path.exists():
        raise SystemExit(
            f"Missing empirical physical profile: {path}. Run Tools > Pachinko > Balance > Run Physical Sampling first."
        )
    raw = read_json(path)
    profiles: dict[tuple[str, float], RouteProfile] = {}
    for row in raw.get("Profiles", []):
        outcomes: list[Outcome] = []
        cumulative: list[int] = []
        running = 0
        for item in row.get("Outcomes", []):
            count = int(item["Count"])
            if count <= 0:
                continue
            outcomes.append(
                Outcome(
                    kind=item["Kind"],
                    target_id=int(item["TargetId"]),
                    multiplier=float(item["Multiplier"]),
                    rush_chance=float(item["BaseRushChance"]),
                    rush_enabled=bool(item["RushEnabled"]),
                    count=count,
                    flight_seconds=float(item["MeanFlightSeconds"]),
                )
            )
            running += count
            cumulative.append(running)
        if not outcomes:
            raise SystemExit(f"Physical profile {row.get('Lane')} / {row.get('Flow')} has no outcomes.")
        hotspots = {float(item["DeviceRadius"]): float(item["HitProbability"]) for item in row.get("Hotspots", [])}
        if not hotspots:
            hotspots = {0.12: 0.0}
        profile = RouteProfile(
            lane=row["Lane"], flow=float(row["Flow"]), samples=int(row["Samples"]),
            outcomes=outcomes, cumulative=cumulative, hotspots=hotspots,
            mean_flight=float(row["MeanFlightSeconds"]),
        )
        profiles[(profile.lane, profile.flow)] = profile
    required = {(lane, flow) for lane in ("Left", "Center", "Right") for flow in (0.0, 0.5, 1.0)}
    missing = required - profiles.keys()
    if missing:
        raise SystemExit(f"Physical profile is incomplete; missing {sorted(missing)}")
    return profiles, raw


def apply_overrides(balance: dict[str, Any], content: dict[str, Any], proposal: dict[str, Any]) -> None:
    for field_name, value in proposal.get("Marble", {}).items():
        balance["Marble"][field_name] = value
    for field_name, value in proposal.get("Campaign", {}).items():
        balance["Campaign"][field_name] = value
    stage_fields = proposal.get("Stages", {})
    for field_name, values in stage_fields.items():
        if len(values) != len(balance["Stages"]):
            raise ValueError(f"Proposal Stages.{field_name} length must match runtime stages")
        for stage, value in zip(balance["Stages"], values):
            stage[field_name] = value
    attributes = {row["Id"]: row for row in content["Attributes"]}
    for attribute, values in proposal.get("Attributes", {}).items():
        if attribute not in attributes:
            raise ValueError(f"Unknown proposal attribute {attribute}")
        if isinstance(values, dict):
            attributes[attribute].update(values)
        else:
            attributes[attribute]["DefaultValue"] = values
    devices = {str(row["Id"]): row for row in content["Devices"]}
    for device_id, values in proposal.get("Devices", {}).items():
        if str(device_id) not in devices:
            raise ValueError(f"Unknown proposal device {device_id}")
        devices[str(device_id)].update(values)
    drugs = {str(row["Id"]): row for row in content["Drugs"]}
    for drug_id, values in proposal.get("Drugs", {}).items():
        if str(drug_id) not in drugs:
            raise ValueError(f"Unknown proposal drug {drug_id}")
        drugs[str(drug_id)].update(values)
    for drug_id, effect_values in proposal.get("DrugEffects", {}).items():
        if str(drug_id) not in drugs:
            raise ValueError(f"Unknown proposal drug {drug_id}")
        effects = {str(row["Attribute"]): row for row in drugs[str(drug_id)].get("Effects", [])}
        for attribute, value in effect_values.items():
            if str(attribute) not in effects:
                raise ValueError(f"Drug {drug_id} has no effect for attribute {attribute}")
            effects[str(attribute)]["Value"] = value
    for field_name, value in proposal.get("Content", {}).items():
        content[field_name] = value


def apply_physics_overrides(physics: dict[tuple[str, float], RouteProfile], proposal: dict[str, Any]) -> None:
    """Change terminal rewards without inventing new path probabilities.

    ZoneMultipliers edits the authored score reward for a sampled TargetId.  The
    empirical landing counts, flight times, contact paths and void rates stay
    unchanged, which is valid while only the terminal reward changes.
    """
    overrides = {int(key): float(value) for key, value in proposal.get("ZoneMultipliers", {}).items()}
    if not overrides:
        return
    seen: set[int] = set()
    for profile in physics.values():
        for outcome in profile.outcomes:
            if outcome.target_id in overrides and outcome.kind == "Score":
                outcome.multiplier = overrides[outcome.target_id]
                seen.add(outcome.target_id)
    missing = set(overrides) - seen
    if missing:
        raise ValueError(f"Proposal ZoneMultipliers target(s) not present in physical sample: {sorted(missing)}")


class CampaignModel:
    def __init__(self, balance: dict[str, Any], content: dict[str, Any], physics: dict[tuple[str, float], RouteProfile]):
        self.balance = balance
        self.content = content
        self.physics = physics
        self.attrs = {row["Id"]: row for row in content["Attributes"]}
        self.devices = {int(row["Id"]): row for row in content["Devices"]}
        self.drugs = {int(row["Id"]): row for row in content["Drugs"]}
        self.rush_devices = [row for row in content["Devices"] if int(row.get("Kind", 0)) == 5 or float(row.get("RushChanceAdd", 0)) > 0]
        self.rush_drugs = [row for row in content["Drugs"] if int(row.get("Kind", 0)) in (1, 2, 3)]
        self.all_items = [("Device", int(row["Id"])) for row in content["Devices"]] + [
            ("Drug", int(row["Id"])) for row in content["Drugs"]
        ]
        self.route_lane = {name: self._select_lane(name) for name in STRATEGY_NAMES}
        self.route_ev = self._route_values()

    def _profile(self, lane: str, flow: float) -> RouteProfile:
        chosen = min((0.0, 0.5, 1.0), key=lambda item: abs(item - flow))
        return self.physics[(lane, chosen)]

    def _route_values(self) -> dict[tuple[str, float], tuple[float, float, float]]:
        result = {}
        for key, profile in self.physics.items():
            values = []
            risky = 0
            voids = 0
            for outcome in profile.outcomes:
                probability = outcome.count / profile.samples
                score = outcome.multiplier if outcome.kind in ("Score", "RandomScore") else 0
                values.extend([score] * outcome.count)
                if outcome.kind == "RandomScore" or outcome.target_id == 2:
                    risky += outcome.count
                if outcome.kind in ("Void", "Drain"):
                    voids += outcome.count
            result[key] = (
                statistics.fmean(values) if values else 0.0,
                risky / profile.samples,
                voids / profile.samples,
            )
        return result

    def _select_lane(self, strategy: str) -> str:
        flow = 0.0 if strategy != "Aggressive" else 0.5
        rows = [(lane, self._route_values_single(self.physics[(lane, flow)])) for lane in ("Left", "Center", "Right")]
        if strategy == "Conservative":
            return min(rows, key=lambda row: (row[1][2], -row[1][0]))[0]
        if strategy == "Aggressive":
            return max(rows, key=lambda row: (row[1][1], row[1][0]))[0]
        return max(rows, key=lambda row: row[1][0])[0]

    @staticmethod
    def _route_values_single(profile: RouteProfile) -> tuple[float, float, float]:
        mean, risky, voids = 0.0, 0.0, 0.0
        for outcome in profile.outcomes:
            probability = outcome.count / profile.samples
            if outcome.kind in ("Score", "RandomScore"):
                mean += probability * outcome.multiplier
            if outcome.kind == "RandomScore" or outcome.target_id == 2:
                risky += probability
            if outcome.kind in ("Void", "Drain"):
                voids += probability
        return mean, risky, voids

    def new_build(self) -> BuildState:
        devices = Counter()
        drugs = Counter()
        for definition in self.devices.values():
            devices[int(definition["Id"])] += int(definition.get("StartingCount", 0))
        for definition in self.drugs.values():
            drugs[int(definition["Id"])] += int(definition.get("StartingCount", 0))
        return BuildState(devices=devices, drugs=drugs)

    def simulate_run(self, config: str, strategy: Strategy, run_index: int, seed: int) -> RunResult:
        rng = random.Random(seed)
        build = self.new_build()
        coins = int(self.balance["Campaign"]["StartingCoins"])
        total_score = 0
        stages_cleared = 0
        rounds = 0
        income = 0
        cashout_income = 0
        bank_income = 0
        stage_reward_income = 0
        overkill_income = 0
        total_balls = 0
        total_duration = 0.0
        total_blood = 0.0
        cashouts = 0
        dead = False
        round_scores: list[int] = []
        build_snapshots: list[tuple[int, float, int, int]] = []
        shop_affordability_snapshots: list[ShopAffordabilitySnapshot] = []
        failed_stage = 0
        won = False
        stages = self.balance["Stages"]

        while rounds < len(stages):
            stage_index = stages_cleared
            if stage_index >= len(stages):
                won = True
                break
            stage = stages[stage_index]
            rounds += 1
            build_snapshots.append((
                int(stage["Id"]), self.build_strength_index(strategy, build),
                sum(build.devices.values()), sum(build.drugs.values()),
            ))
            result = self.simulate_round(rng, strategy, build, total_score, stage)
            total_balls += result.balls
            total_duration += result.duration
            total_blood += result.blood_remaining
            cashouts += int(result.cashout)
            dead |= result.dead_run
            round_scores.append(result.score)
            before = total_score
            total_score += result.score
            round_income = result.cashout_coins + result.bank_coins
            cashout_income += result.cashout_coins
            bank_income += result.bank_coins
            if total_score < int(stage["TargetScore"]):
                failed_stage = int(stage["Id"])
                coins += round_income
                income += round_income
                break

            first = stage_index
            next_stage = first
            stage_reward_raw = 0
            while next_stage < len(stages) and total_score >= int(stages[next_stage]["TargetScore"]):
                crossed = stages[next_stage]
                stage_reward = int(crossed["RewardCoins"])
                if next_stage > first:
                    stage_reward += int(crossed["SkipRewardCoins"])
                stage_reward_raw += stage_reward
                next_stage += 1
            excess = max(0, total_score - max(before, int(stage["TargetScore"])))
            overkill_raw = math.floor(
                excess * float(self.content.get("OverkillCoinsPerScore", 0.01)) *
                float(self.attrs["SCORE_OVERKILL_RATE"]["DefaultValue"])
            )
            coin_gain = float(self.attrs["COIN_GAIN_RATE"]["DefaultValue"])
            reward = math.floor((stage_reward_raw + overkill_raw) * coin_gain)
            stage_reward = math.floor(stage_reward_raw * coin_gain)
            overkill_reward = reward - stage_reward
            stage_reward_income += stage_reward
            overkill_income += overkill_reward
            round_income += reward
            coins += round_income
            income += round_income
            stages_cleared = next_stage
            shop_affordability_snapshots.append(self.shop_affordability_snapshot(
                seed, rounds, stages_cleared, strategy, build, coins,
            ))
            if stages_cleared >= len(stages):
                won = True
                break
            coins = self.shop(rng, strategy, build, coins)

        variance = statistics.pvariance(round_scores) if len(round_scores) > 1 else 0.0
        return RunResult(
            config=config, strategy=strategy.name, run_index=run_index, seed=seed, won=won,
            failed_stage=failed_stage, rounds_played=rounds, stages_cleared=stages_cleared,
            total_score=total_score, score_variance=variance, coins_income=income,
            cashout_income=cashout_income, bank_income=bank_income,
            stage_reward_income=stage_reward_income, overkill_income=overkill_income,
            coins_spent=build.coins_spent, purchase_spend=build.purchase_spend,
            refresh_spend=build.refresh_spend, ending_coins=coins, balls_launched=total_balls,
            duration=total_duration, blood_remaining=total_blood / max(1, rounds), cashouts=cashouts,
            purchases=build.purchases, refreshes=build.refreshes,
            device_count=sum(build.devices.values()), drug_count=sum(build.drugs.values()), dead_run=dead,
            purchase_devices=build.purchased_devices.copy(), purchase_drugs=build.purchased_drugs.copy(),
            build_snapshots=build_snapshots,
            shop_affordability_snapshots=shop_affordability_snapshots,
        )

    def simulate_round(self, rng: random.Random, strategy: Strategy, build: BuildState,
                       total_score: int, stage: dict[str, Any]) -> RoundResult:
        blood_capacity = float(self.balance["Marble"]["BloodCapacity"])
        min_invest = float(self.balance["Marble"]["MinInvestment"])
        max_invest = float(self.balance["Marble"]["MaxInvestment"])
        interval = float(self.balance["Marble"]["FireIntervalSeconds"])
        blood = blood_capacity
        time = 0.0
        last_time = 0.0
        next_launch = 0.0
        rush_remaining = 0.0
        rush_meter = 0.0
        rush_seconds = 0.0
        score = 0
        pending = 0
        balls = 0
        bank_coins = 0
        cashout = False
        cashout_coins = 0
        settlement_blood = 0.0
        event_id = 0
        events: list[tuple[float, int, Ball]] = []
        used_drugs: set[int] = set()
        active_until: dict[int, float] = {}
        dead_at_ball: int | None = None
        settled_balls = 0
        projected_points = 0.0
        lane = self.route_lane[strategy.name]
        target = int(stage["TargetScore"])
        gamble_weights = [
            float(self.attrs["GAMBLE_LOSS_CHANCE"]["DefaultValue"]),
            float(self.attrs["GAMBLE_RETURN_CHANCE"]["DefaultValue"]),
            float(self.attrs["GAMBLE_WIN_CHANCE"]["DefaultValue"]),
            float(self.attrs["GAMBLE_QUADRUPLE_CHANCE"]["DefaultValue"]),
        ]
        gamble_weight_total = max(1e-9, sum(gamble_weights))
        gamble_ev = sum(value * weight for value, weight in zip((0, 1, 2, 4), gamble_weights)) / gamble_weight_total

        def advance(to_time: float) -> None:
            nonlocal last_time, rush_remaining, rush_meter, rush_seconds
            delta = max(0.0, to_time - last_time)
            if rush_remaining > 0:
                active = min(delta, rush_remaining)
                rush_seconds += active
                rush_remaining = max(0.0, rush_remaining - delta * float(self.attrs["RUSH_DECAY"]["DefaultValue"]))
            else:
                rush_meter = max(0.0, rush_meter - delta * float(self.attrs["RUSH_DECAY"]["DefaultValue"]))
            last_time = to_time

        def use_drug(drug_id: int) -> None:
            if build.drugs[drug_id] <= 0 or drug_id in used_drugs:
                return
            definition = self.drugs[drug_id]
            build.drugs[drug_id] -= 1
            used_drugs.add(drug_id)
            active_until[drug_id] = time + float(definition["DurationSeconds"])

        while (blood >= min_invest * float(self.attrs["BLOOD_COST"]["DefaultValue"]) and not cashout) or events:
            next_settle = events[0][0] if events else math.inf
            can_launch = not cashout and blood >= min_invest * float(self.attrs["BLOOD_COST"]["DefaultValue"])
            if can_launch and next_launch <= next_settle:
                time = next_launch
                advance(time)
                if strategy.name == "Aggressive" and balls == 0:
                    use_drug(100)
                    use_drug(102)
                if strategy.name == "Conservative" and balls == 0:
                    use_drug(100)
                if strategy.name == "Balanced" and rush_remaining <= 0 and rush_meter >= float(self.content["RushThreshold"]) * 0.6:
                    if rng.random() < strategy.timing_skill:
                        use_drug(100)
                        use_drug(102)
                if strategy.name == "Conservative" and rush_remaining <= 0 and rush_meter >= float(self.content["RushThreshold"]) * 0.8:
                    if rng.random() < strategy.timing_skill:
                        use_drug(102)
                if rush_remaining > 0 and rng.random() < strategy.timing_skill:
                    use_drug(101)

                flow = strategy.rush_flow if rush_remaining > 0 else strategy.normal_flow
                investment = min_invest + (max_invest - min_invest) * flow
                cost = investment * float(self.attrs["BLOOD_COST"]["DefaultValue"])
                if active_until.get(100, -1) > time:
                    cost *= self.drug_effect_value(100, 0, 1.0)
                if active_until.get(102, -1) > time:
                    cost *= self.drug_effect_value(102, 0, 1.0)
                if cost > blood + 1e-9:
                    flow = 0.0
                    investment = min_invest
                    cost = investment * float(self.attrs["BLOOD_COST"]["DefaultValue"])
                if cost > blood + 1e-9:
                    next_launch = math.inf
                    continue
                profile = self._profile(lane, flow)
                outcome = profile.draw(rng)
                base_value = float(self.attrs["BALL_BASE_VALUE"]["DefaultValue"]) * math.pow(
                    investment / min_invest, float(self.content["FlowValueExponent"])
                )
                rush_add = float(self.drugs[100].get("RushChanceAdd", 0)) if active_until.get(100, -1) > time else 0.0
                rush_duration = float(self.attrs["RUSH_DURATION"]["DefaultValue"])
                if active_until.get(102, -1) > time:
                    rush_duration *= self.drug_effect_value(102, 22, 1.0)
                rush_duration = min(rush_duration, float(self.attrs["RUSH_DURATION"]["MaxValue"]))
                fruit = active_until.get(101, -1) > time
                if fruit:
                    base_value *= self.drug_effect_value(101, 3, 1.0)
                value, rush_add, clover_probability, revive, banked, consumed = self.apply_devices(
                    rng, strategy, build, profile, base_value, rush_add
                )
                blood -= cost
                balls += 1
                event_id += 1
                flight = max(0.05, rng.gauss(outcome.flight_seconds, max(0.02, outcome.flight_seconds * 0.08)))
                heapq.heappush(events, (time + flight, event_id, Ball(
                    settle_at=time + flight, outcome=outcome, value=value, cost=cost,
                    rush_add=rush_add, rush_duration=rush_duration,
                    rush_score_multiplier=float(self.drugs[101].get("RushScoreMultiplier", 1)) if fruit else 1.0,
                    clover_probability=clover_probability, revive=revive, banked_coins=banked, consumed=consumed,
                )))
                next_launch += interval
                continue

            time, _, ball = heapq.heappop(events)
            advance(time)
            settled_balls += 1
            bank_coins += ball.banked_coins
            if ball.revive:
                blood = min(blood_capacity, blood + ball.cost)
            if not ball.consumed and ball.outcome.kind in ("Score", "RandomScore"):
                in_rush = rush_remaining > 0
                value = ball.value * (ball.rush_score_multiplier if in_rush else 1.0)
                machine = ball.outcome.multiplier
                global_mult = float(self.attrs["RUSH_MULT"]["DefaultValue"]) if in_rush else 1.0
                awarded = math.floor(value * machine * global_mult)
                if ball.outcome.kind == "RandomScore":
                    pending += awarded
                    projected_points += awarded * gamble_ev
                else:
                    score += awarded
                    projected_points += awarded
                if ball.outcome.rush_enabled:
                    probability = min(1.0, (ball.clover_probability if ball.clover_probability > 0 else ball.outcome.rush_chance) + ball.rush_add)
                    triggered = rng.random() < probability
                    rush_meter += float(self.attrs["BALL_RUSH_GAIN"]["DefaultValue"])
                    if rush_meter >= float(self.content["RushThreshold"]):
                        triggered = True
                        rush_meter %= float(self.content["RushThreshold"])
                    if triggered:
                        rush_remaining = min(
                            float(self.attrs["RUSH_DURATION"]["MaxValue"]),
                            max(rush_remaining, ball.rush_duration),
                        )

            confirmed = total_score + score
            if not cashout and confirmed >= target and blood > 0:
                should_cash = blood / blood_capacity <= strategy.cashout_blood_fraction or confirmed >= target * strategy.score_margin
                if should_cash:
                    cashout = True
                    settlement_blood = blood
                    dividend = float(self.attrs["BLOOD_DIVIDEND_RATE"]["DefaultValue"])
                    dividend *= float(self.attrs["COIN_GAIN_RATE"]["DefaultValue"])
                    cashout_coins = math.floor(blood * float(stage["CashoutRate"]) * dividend)
                    blood = 0.0

            if dead_at_ball is None and balls >= 12 and confirmed < target and blood > blood_capacity * 0.2:
                flow = strategy.normal_flow
                profile = self._profile(lane, flow)
                mean_zone = self.route_ev[(lane, min((0.0, 0.5, 1.0), key=lambda item: abs(item - flow)))][0]
                investment = min_invest + (max_invest - min_invest) * flow
                base_value = float(self.attrs["BALL_BASE_VALUE"]["DefaultValue"]) * math.pow(
                    investment / min_invest, float(self.content["FlowValueExponent"])
                )
                modeled_mean = base_value * mean_zone
                observed_mean = projected_points / settled_balls if settled_balls >= 6 else 0.0
                per_ball = max(modeled_mean, observed_mean)
                zone_second = sum(
                    (outcome.count / profile.samples) *
                    (outcome.multiplier if outcome.kind in ("Score", "RandomScore") else 0.0) ** 2
                    for outcome in profile.outcomes
                )
                zone_sigma = math.sqrt(max(0.0, zone_second - mean_zone * mean_zone))
                effect_scale = max(1.0, per_ball / max(1e-9, modeled_mean))
                per_ball_sigma = base_value * zone_sigma * effect_scale
                remaining_balls = blood / max(0.001, investment * float(self.attrs["BLOOD_COST"]["DefaultValue"]))
                # One-sided normal approximation: mean + 1.645*sqrt(n)*sigma.
                # Pending random score is included at the configured gamble EV.
                optimistic = (
                    confirmed + pending * gamble_ev + remaining_balls * per_ball +
                    1.645 * math.sqrt(max(0.0, remaining_balls)) * per_ball_sigma
                )
                if optimistic < target:
                    dead_at_ball = balls

        draw = rng.random() * gamble_weight_total
        cumulative = 0.0
        multiplier = 4
        for value, weight in zip((0, 1, 2, 4), gamble_weights):
            cumulative += weight
            if draw < cumulative:
                multiplier = value
                break
        payout = pending * multiplier
        score += payout
        # A dead run must actually finish below the target.  The prospective
        # warning alone is not enough because Rush/device variance can still
        # turn a low-EV position into a clear.
        dead_run = (
            dead_at_ball is not None and
            balls - dead_at_ball >= 15 and
            total_score + score < target
        )
        return RoundResult(
            score=score, pending_score=pending, gambling_multiplier=multiplier, balls=balls,
            duration=time, blood_remaining=settlement_blood if cashout else blood,
            cashout=cashout, cashout_coins=cashout_coins,
            bank_coins=bank_coins, rush_seconds=rush_seconds, dead_run=dead_run, route=lane,
        )

    def apply_devices(self, rng: random.Random, strategy: Strategy, build: BuildState,
                      profile: RouteProfile, value: float, rush_add: float) -> tuple[float, float, float, bool, int, bool]:
        clover_probability = 0.0
        revive = False
        banked = 0
        consumed = False
        for device_id, count in sorted(build.devices.items()):
            if count <= 0:
                continue
            definition = self.devices[device_id]
            radius = float(definition.get("Radius", 0.12))
            base_probability = profile.hotspot(radius)
            for copy_index in range(count):
                probability = min(1.0, base_probability * math.pow(strategy.duplicate_exposure, copy_index))
                if rng.random() >= probability:
                    continue
                kind = int(definition.get("Kind", 0))
                if device_id == 113:
                    value *= min(100.0, float(definition.get("ScoreMultiplier", 2.0)))
                elif device_id == 114:
                    rush_add += float(definition.get("RushChanceAdd", 0.0))
                elif kind == 2 and count >= 2:
                    value = min(10000.0, value * float(definition.get("Value", 1.2)))
                elif kind == 5:
                    clover_probability = max(clover_probability, float(definition.get("Value", 0.75)))
                elif kind == 8:
                    value = min(10000.0, value * float(definition.get("Value", 2.0)))
                elif kind == 9:
                    if value < 6:
                        consumed = True
                    else:
                        value = min(10000.0, value * float(definition.get("Value", 3.0)))
                elif kind == 12:
                    revive = True
                elif kind == 1:
                    banked += math.floor(value * float(definition.get("Value", 0.12)) *
                                         float(self.attrs["COIN_GAIN_RATE"]["DefaultValue"]))
                    consumed = True
        return value, rush_add, clover_probability, revive, banked, consumed

    def drug_effect_value(self, drug_id: int, attribute: int, default: float) -> float:
        for effect in self.drugs[drug_id].get("Effects", []):
            if int(effect.get("Attribute", -1)) == attribute:
                return float(effect.get("Value", default))
        return default

    def item_utility(self, strategy: Strategy, item: tuple[str, int], build: BuildState) -> float:
        kind, item_id = item
        lane = self.route_lane[strategy.name]
        profile = self._profile(lane, strategy.normal_flow)
        if kind == "Device":
            definition = self.devices[item_id]
            price = max(1, int(definition["Price"]))
            p = profile.hotspot(float(definition.get("Radius", 0.12)))
            device_kind = int(definition.get("Kind", 0))
            if item_id == 113:
                benefit = p * max(0.0, float(definition.get("ScoreMultiplier", 1.0)) - 1.0)
            elif item_id == 114:
                benefit = p * float(definition.get("RushChanceAdd", 0)) * 3.0
            elif device_kind == 8:
                benefit = p * (float(definition.get("Value", 2)) - 1)
            elif device_kind == 5:
                benefit = p * float(definition.get("Value", 0.75))
            elif device_kind == 2:
                benefit = p * max(0.0, float(definition.get("Value", 1.2)) - 1.0) * (1.0 if build.devices[item_id] % 2 else 0.35)
            elif device_kind == 9:
                gate = 0.45 if strategy.name == "Aggressive" else 0.125
                benefit = p * max(0.0, float(definition.get("Value", 3.0)) - 1.0) * gate
            elif device_kind == 12:
                benefit = p * 0.45
            elif device_kind == 1:
                bank_weight = 2.083333 if strategy.name == "Conservative" else 1.0
                benefit = p * float(definition.get("Value", 0.12)) * bank_weight
            elif device_kind in (3, 4, 7, 11, 13):
                benefit = p * 0.08  # route effect not calibrated; intentionally below modeled score items
            elif device_kind == 6:
                benefit = p * 0.12
            else:
                benefit = p * 0.05
            if strategy.name == "Aggressive" and item_id in (104, 107, 108, 114):
                benefit *= 1.35
            return benefit / price
        definition = self.drugs[item_id]
        price = max(1, int(definition["Price"]))
        drug_kind = int(definition.get("Kind", 0))
        if drug_kind == 1:
            benefit = float(definition.get("RushChanceAdd", 0.25)) * 2.4
            benefit *= {"Conservative": 1.15, "Balanced": 0.85, "Aggressive": 1.0}[strategy.name]
            benefit -= max(0.0, self.drug_effect_value(item_id, 0, 1.0) - 1.0) * 0.35
        elif drug_kind == 2:
            benefit = max(0.0, float(definition.get("RushScoreMultiplier", 1.75)) - 1) * strategy.timing_skill
            benefit -= max(0.0, 1.0 - self.drug_effect_value(item_id, 3, 1.0)) * (1.0 - strategy.timing_skill)
        elif drug_kind == 3:
            benefit = max(0.0, self.drug_effect_value(item_id, 22, 1.0) - 1.0) * 0.9 * strategy.timing_skill
            benefit -= max(0.0, self.drug_effect_value(item_id, 0, 1.0) - 1.0) * 0.25
        elif drug_kind == 5:
            benefit = 0.083
        elif drug_kind in (6, 7):
            benefit = 0.11
        elif drug_kind == 8:
            benefit = 0.55 if strategy.name == "Aggressive" else 0.08
        else:
            benefit = 0.02
        return benefit / price

    def build_strength_index(self, strategy: Strategy, build: BuildState) -> float:
        """Normalized modeled benefit available at the start of a stage; 1.0 is the naked build."""
        strength = 1.0
        for category, item_id in self.all_items:
            definition = self.devices[item_id] if category == "Device" else self.drugs[item_id]
            modeled = category == "Device" and (
                item_id in (113, 114) or int(definition.get("Kind", 0)) in (1, 2, 5, 8, 9, 12)
            )
            modeled |= category == "Drug" and int(definition.get("Kind", 0)) in (1, 2, 3)
            if not modeled:
                continue
            copies = build.devices[item_id] if category == "Device" else build.drugs[item_id]
            if copies <= 0:
                continue
            price = max(1, int(definition["Price"]))
            strength += self.item_utility(strategy, (category, item_id), build) * price * copies
        return strength

    def shop(self, rng: random.Random, strategy: Strategy, build: BuildState, coins: int) -> int:
        refreshes = 0
        while True:
            offers = self.generate_offers(rng)
            budget_floor = coins * (1 - strategy.spend_fraction)
            ranked = sorted(offers, key=lambda item: self.item_utility(strategy, item, build), reverse=True)
            bought = False
            for item in ranked:
                utility = self.item_utility(strategy, item, build)
                kind, item_id = item
                definition = self.devices[item_id] if kind == "Device" else self.drugs[item_id]
                price = max(1, math.floor(int(definition["Price"]) * float(self.attrs["SHOP_PRICE_MOD"]["DefaultValue"])))
                if utility < strategy.utility_threshold or price > coins or coins - price < budget_floor:
                    continue
                coins -= price
                build.coins_spent += price
                build.purchase_spend += price
                build.purchases += 1
                bought = True
                if kind == "Device":
                    quantity = 2 if int(definition.get("Kind", 0)) == 4 else 1
                    build.devices[item_id] += quantity
                    build.purchased_devices[item_id] += 1
                else:
                    build.drugs[item_id] += 1
                    build.purchased_drugs[item_id] += 1
            if bought or refreshes >= strategy.max_refreshes:
                return coins
            refresh_cost = max(1, math.floor(int(self.balance["Campaign"]["ShopRefreshCost"]) *
                                             float(self.attrs["SHOP_PRICE_MOD"]["DefaultValue"])))
            if coins - refresh_cost < budget_floor:
                return coins
            coins -= refresh_cost
            build.coins_spent += refresh_cost
            build.refresh_spend += refresh_cost
            build.refreshes += 1
            refreshes += 1

    def shop_affordability_snapshot(self, run_seed: int, round_number: int, stage: int,
                                    strategy: Strategy, build: BuildState,
                                    coins: int) -> ShopAffordabilitySnapshot:
        required_purchases, required_refreshes = ((2, 1) if stage <= 4 else (3, 3))
        probe_seed = run_seed ^ (round_number * 0x9E3779B1) ^ (stage * 0x85EBCA77) ^ 0xC2B2AE3D
        probe_rng = random.Random(probe_seed)
        offered: list[tuple[str, int]] = []
        for _ in range(required_refreshes + 1):
            offered.extend(self.generate_offers(probe_rng))
        ranked = sorted(
            offered,
            key=lambda item: (
                self.item_utility(strategy, item, build),
                -int((self.devices[item[1]] if item[0] == "Device" else self.drugs[item[1]])["Price"]),
            ),
            reverse=True,
        )
        chosen = ranked[:required_purchases]
        price_mod = float(self.attrs["SHOP_PRICE_MOD"]["DefaultValue"])
        item_cost = sum(max(1, math.floor(int(
            (self.devices[item_id] if kind == "Device" else self.drugs[item_id])["Price"]
        ) * price_mod)) for kind, item_id in chosen)
        refresh_cost = max(1, math.floor(int(self.balance["Campaign"]["ShopRefreshCost"]) * price_mod))
        bundle_cost = item_cost + required_refreshes * refresh_cost
        return ShopAffordabilitySnapshot(
            stage=stage,
            coins_before_shop=coins,
            required_purchases=required_purchases,
            required_refreshes=required_refreshes,
            bundle_cost=bundle_cost,
            affordable=coins >= bundle_cost,
        )

    def generate_offers(self, rng: random.Random) -> list[tuple[str, int]]:
        count = int(self.balance["Campaign"]["ShopOfferCount"])
        rush_devices = [("Device", int(row["Id"])) for row in self.rush_devices]
        rush_drugs = [("Drug", int(row["Id"])) for row in self.rush_drugs]
        offers = [rng.choice(rush_devices), rng.choice(rush_drugs)]
        candidates = [item for item in self.all_items if item not in offers]
        rng.shuffle(candidates)
        offers.extend(candidates[: max(0, count - len(offers))])
        return offers[:count]


def run_config(config_name: str, balance: dict[str, Any], content: dict[str, Any],
               physics: dict[tuple[str, float], RouteProfile], runs: int, seed: int) -> list[RunResult]:
    model = CampaignModel(balance, content, physics)
    rows: list[RunResult] = []
    for strategy_index, strategy_name in enumerate(STRATEGY_NAMES):
        strategy = STRATEGIES[strategy_name]
        for run_index in range(runs):
            run_seed = seed + strategy_index * 100_000_019 + run_index * 7919
            rows.append(model.simulate_run(config_name, strategy, run_index, run_seed))
    return rows


def mean(rows: Iterable[RunResult], field_name: str) -> float:
    values = [float(getattr(row, field_name)) for row in rows]
    return statistics.fmean(values) if values else 0.0


def variance(rows: Iterable[RunResult], field_name: str) -> float:
    values = [float(getattr(row, field_name)) for row in rows]
    return statistics.pvariance(values) if len(values) > 1 else 0.0


def nearest_rank_percentile(values: Iterable[float], percentile: float) -> float | None:
    ordered = sorted(float(value) for value in values)
    if not ordered:
        return None
    index = max(0, min(len(ordered) - 1, math.ceil(percentile * len(ordered)) - 1))
    return ordered[index]


def summarize(rows: list[RunResult], model: CampaignModel) -> dict[str, Any]:
    output: dict[str, Any] = {}
    for strategy_name in STRATEGY_NAMES:
        selected = [row for row in rows if row.strategy == strategy_name]
        total_rounds = sum(row.rounds_played for row in selected)
        total_clears = sum(row.stages_cleared for row in selected)
        devices = Counter()
        drugs = Counter()
        device_runs = Counter()
        drug_runs = Counter()
        for row in selected:
            devices.update(row.purchase_devices)
            drugs.update(row.purchase_drugs)
            device_runs.update(row.purchase_devices.keys())
            drug_runs.update(row.purchase_drugs.keys())
        pick_rows = []
        for item_id, count in devices.items():
            pick_rows.append((
                "Device", item_id,
                device_runs[item_id] / max(1, len(selected)),
                count / max(1, len(selected)),
                model.devices[item_id]["Name"],
            ))
        for item_id, count in drugs.items():
            pick_rows.append((
                "Medicine", item_id,
                drug_runs[item_id] / max(1, len(selected)),
                count / max(1, len(selected)),
                model.drugs[item_id]["Name"],
            ))
        pick_rows.sort(key=lambda row: row[2], reverse=True)
        build_curve = []
        affordability_curve = []
        for stage in model.balance["Stages"]:
            stage_id = int(stage["Id"])
            snapshots = [
                snapshot for row in selected for snapshot in row.build_snapshots
                if snapshot[0] == stage_id
            ]
            build_curve.append({
                "stage": stage_id,
                "runs_reaching": len(snapshots),
                "average_strength_index": statistics.fmean(value[1] for value in snapshots) if snapshots else None,
                "average_devices": statistics.fmean(value[2] for value in snapshots) if snapshots else None,
                "average_medicines": statistics.fmean(value[3] for value in snapshots) if snapshots else None,
            })
            affordability = [
                snapshot for row in selected for snapshot in row.shop_affordability_snapshots
                if snapshot.stage == stage_id
            ]
            affordability_curve.append({
                "stage": stage_id,
                "observations": len(affordability),
                "required_purchases": 2 if stage_id <= 4 else 3,
                "required_refreshes": 1 if stage_id <= 4 else 3,
                "affordability_rate": (
                    sum(snapshot.affordable for snapshot in affordability) / len(affordability)
                    if affordability else None
                ),
                "coins_p25": nearest_rank_percentile(
                    (snapshot.coins_before_shop for snapshot in affordability), 0.25
                ),
                "coins_p50": nearest_rank_percentile(
                    (snapshot.coins_before_shop for snapshot in affordability), 0.50
                ),
                "bundle_cost_p50": nearest_rank_percentile(
                    (snapshot.bundle_cost for snapshot in affordability), 0.50
                ),
                "bundle_cost_p75": nearest_rank_percentile(
                    (snapshot.bundle_cost for snapshot in affordability), 0.75
                ),
            })
        output[strategy_name] = {
            "runs": len(selected),
            "win_rate": sum(row.won for row in selected) / max(1, len(selected)),
            "round_clear_rate": (
                sum(row.rounds_played - (1 if row.failed_stage else 0) for row in selected) /
                max(1, total_rounds)
            ),
            "round_failure_rate": sum(not row.won for row in selected) / max(1, total_rounds),
            "cashout_rate": sum(row.cashouts for row in selected) / max(1, total_rounds),
            "average_run_duration": mean(selected, "duration"),
            "average_balls_launched": mean(selected, "balls_launched"),
            "average_blood_remaining": mean(selected, "blood_remaining"),
            "average_score": mean(selected, "total_score"),
            "average_gold_income": mean(selected, "coins_income"),
            "average_cashout_income": mean(selected, "cashout_income"),
            "average_bank_income": mean(selected, "bank_income"),
            "average_stage_reward_income": mean(selected, "stage_reward_income"),
            "average_overkill_income": mean(selected, "overkill_income"),
            "average_gold_spend": mean(selected, "coins_spent"),
            "average_purchase_spend": mean(selected, "purchase_spend"),
            "average_refresh_spend": mean(selected, "refresh_spend"),
            "average_gold_stock": mean(selected, "ending_coins"),
            "average_purchases": mean(selected, "purchases"),
            "average_refreshes": mean(selected, "refreshes"),
            "average_devices": mean(selected, "device_count"),
            "average_medicines": mean(selected, "drug_count"),
            "average_special_balls": None,
            "score_variance": variance(selected, "total_score"),
            "gold_variance": variance(selected, "ending_coins"),
            "run_variance": variance(selected, "duration"),
            "dead_run_rate": sum(row.dead_run for row in selected) / max(1, len(selected)),
            "failure_stage_counts": dict(sorted(Counter(row.failed_stage for row in selected if row.failed_stage).items())),
            "stage_clear_rates": [
                {
                    "stage": int(stage["Id"]),
                    "clear_rate": sum(row.stages_cleared >= index for row in selected) / max(1, len(selected)),
                }
                for index, stage in enumerate(model.balance["Stages"], start=1)
            ],
            "build_strength_curve": build_curve,
            "shop_affordability_curve": affordability_curve,
            "item_pick_rates": [
                {
                    "category": kind, "id": item_id, "name": name,
                    "pick_rate": rate, "average_copies": average_copies,
                }
                for kind, item_id, rate, average_copies, name in pick_rows
            ],
        }
    return output


def write_run_csv(path: Path, rows: list[RunResult]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    fields = [
        "config", "strategy", "run_index", "seed", "won", "failed_stage", "rounds_played",
        "stages_cleared", "total_score", "score_variance", "coins_income", "cashout_income",
        "bank_income", "stage_reward_income", "overkill_income", "coins_spent",
        "purchase_spend", "refresh_spend",
        "ending_coins", "balls_launched", "duration", "blood_remaining", "cashouts",
        "purchases", "refreshes", "device_count", "drug_count", "special_ball_count", "dead_run",
        "purchased_devices", "purchased_medicines", "build_snapshots",
        "shop_affordability_snapshots",
    ]
    with path.open("w", newline="", encoding="utf-8-sig") as handle:
        writer = csv.DictWriter(handle, fieldnames=fields)
        writer.writeheader()
        for row in rows:
            writer.writerow({
                "config": row.config, "strategy": row.strategy, "run_index": row.run_index, "seed": row.seed,
                "won": int(row.won), "failed_stage": row.failed_stage, "rounds_played": row.rounds_played,
                "stages_cleared": row.stages_cleared, "total_score": row.total_score,
                "score_variance": f"{row.score_variance:.6f}", "coins_income": row.coins_income,
                "cashout_income": row.cashout_income, "bank_income": row.bank_income,
                "stage_reward_income": row.stage_reward_income, "overkill_income": row.overkill_income,
                "coins_spent": row.coins_spent, "purchase_spend": row.purchase_spend,
                "refresh_spend": row.refresh_spend, "ending_coins": row.ending_coins,
                "balls_launched": row.balls_launched, "duration": f"{row.duration:.6f}",
                "blood_remaining": f"{row.blood_remaining:.6f}", "cashouts": row.cashouts,
                "purchases": row.purchases, "refreshes": row.refreshes, "device_count": row.device_count,
                "drug_count": row.drug_count, "special_ball_count": "N/A", "dead_run": int(row.dead_run),
                "purchased_devices": json.dumps(dict(row.purchase_devices), ensure_ascii=False, sort_keys=True),
                "purchased_medicines": json.dumps(dict(row.purchase_drugs), ensure_ascii=False, sort_keys=True),
                "build_snapshots": json.dumps([
                    {"stage": stage, "strength_index": strength, "devices": devices, "medicines": medicines}
                    for stage, strength, devices, medicines in row.build_snapshots
                ], ensure_ascii=False, separators=(",", ":")),
                "shop_affordability_snapshots": json.dumps([
                    {
                        "stage": snapshot.stage,
                        "coins_before_shop": snapshot.coins_before_shop,
                        "required_purchases": snapshot.required_purchases,
                        "required_refreshes": snapshot.required_refreshes,
                        "bundle_cost": snapshot.bundle_cost,
                        "affordable": snapshot.affordable,
                    }
                    for snapshot in row.shop_affordability_snapshots
                ], ensure_ascii=False, separators=(",", ":")),
            })


def write_shop_affordability_csv(path: Path, summaries: dict[str, dict[str, Any]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    fields = [
        "config", "strategy", "stage", "observations", "required_purchases",
        "required_refreshes", "affordability_rate", "coins_p25", "coins_p50",
        "bundle_cost_p50", "bundle_cost_p75",
    ]
    with path.open("w", newline="", encoding="utf-8-sig") as handle:
        writer = csv.DictWriter(handle, fieldnames=fields)
        writer.writeheader()
        for config in summaries:
            for strategy in STRATEGY_NAMES:
                for row in summaries[config][strategy]["shop_affordability_curve"]:
                    writer.writerow({
                        "config": config,
                        "strategy": strategy,
                        **row,
                        "affordability_rate": (
                            "" if row["affordability_rate"] is None
                            else f"{row['affordability_rate']:.8f}"
                        ),
                    })


def write_item_csv(path: Path, item_tables: dict[str, list[dict[str, Any]]],
                   config_order: Iterable[str] = CONFIG_NAMES) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    fields = [
        "config", "category", "id", "name", "price", "ev_ratio", "variance_class",
        "trigger_frequency", "modeled", "pick_rate",
    ]
    with path.open("w", newline="", encoding="utf-8-sig") as handle:
        writer = csv.DictWriter(handle, fieldnames=fields)
        writer.writeheader()
        for config_name in config_order:
            for row in item_tables.get(config_name, []):
                writer.writerow({
                    "config": config_name, "category": row["category"], "id": row["id"],
                    "name": row["name"], "price": row["price"],
                    "ev_ratio": f"{row['ev_ratio']:.8f}", "variance_class": row["variance_class"],
                    "trigger_frequency": row["trigger_frequency"], "modeled": int(row["modeled"]),
                    "pick_rate": f"{row['pick_rate']:.8f}",
                })


def percent(value: float) -> str:
    return f"{value * 100:.1f}%"


def metric_table(summary: dict[str, Any]) -> str:
    lines = [
        "| KPI | Conservative | Balanced | Aggressive |",
        "| --- | ---: | ---: | ---: |",
    ]
    metrics = [
        ("Run Win Rate", "win_rate", percent),
        ("Round Clear Rate", "round_clear_rate", percent),
        ("Round Failure Rate", "round_failure_rate", percent),
        ("Cashout Rate", "cashout_rate", percent),
        ("Dead Run Rate", "dead_run_rate", percent),
        ("平均 Run 时长（秒）", "average_run_duration", lambda x: f"{x:.1f}"),
        ("平均发射弹珠", "average_balls_launched", lambda x: f"{x:.1f}"),
        ("平均剩余血量", "average_blood_remaining", lambda x: f"{x:.1f}"),
        ("平均得分", "average_score", lambda x: f"{x:.1f}"),
        ("平均金币收入", "average_gold_income", lambda x: f"{x:.1f}"),
        ("平均金币支出", "average_gold_spend", lambda x: f"{x:.1f}"),
        ("平均金币存量", "average_gold_stock", lambda x: f"{x:.1f}"),
        ("平均购买数", "average_purchases", lambda x: f"{x:.2f}"),
        ("平均刷新数", "average_refreshes", lambda x: f"{x:.2f}"),
        ("平均 Device 数", "average_devices", lambda x: f"{x:.2f}"),
        ("平均 Medicine 数", "average_medicines", lambda x: f"{x:.2f}"),
        ("平均 Special Ball 数", "average_special_balls", lambda x: "N/A" if x is None else f"{x:.2f}"),
        ("Score Variance", "score_variance", lambda x: f"{x:,.1f}"),
        ("Gold Variance", "gold_variance", lambda x: f"{x:,.1f}"),
        ("Run Variance（时长秒²）", "run_variance", lambda x: f"{x:,.1f}"),
    ]
    for label, key, formatter in metrics:
        lines.append("| " + label + " | " + " | ".join(formatter(summary[name][key]) for name in STRATEGY_NAMES) + " |")
    return "\n".join(lines)


def affordability_table(summary: dict[str, Any]) -> str:
    lines = [
        "| 阶段 | 目标购买力 | Conservative | Balanced | Aggressive |",
        "| ---: | --- | ---: | ---: | ---: |",
    ]
    for stage_index in range(len(summary[STRATEGY_NAMES[0]]["shop_affordability_curve"])):
        first = summary[STRATEGY_NAMES[0]]["shop_affordability_curve"][stage_index]
        target = f"{first['required_purchases']} 件 + {first['required_refreshes']} 刷"
        cells = []
        for strategy in STRATEGY_NAMES:
            row = summary[strategy]["shop_affordability_curve"][stage_index]
            if row["affordability_rate"] is None:
                cells.append("N/A")
            else:
                cells.append(
                    f"{percent(row['affordability_rate'])} "
                    f"(金币P50 {row['coins_p50']:.0f} / 成本P50 {row['bundle_cost_p50']:.0f})"
                )
        lines.append(f"| {first['stage']} | {target} | " + " | ".join(cells) + " |")
    return "\n".join(lines)


def build_item_rows(model: CampaignModel, summary: dict[str, Any]) -> list[dict[str, Any]]:
    rows = []
    for category, item_id in model.all_items:
        definition = model.devices[item_id] if category == "Device" else model.drugs[item_id]
        utilities = [model.item_utility(STRATEGIES[name], (category, item_id), model.new_build()) for name in STRATEGY_NAMES]
        pick_rates = []
        lookup_category = "Device" if category == "Device" else "Medicine"
        for strategy in STRATEGY_NAMES:
            matched = next((item for item in summary[strategy]["item_pick_rates"]
                            if item["category"] == lookup_category and item["id"] == item_id), None)
            pick_rates.append(matched["pick_rate"] if matched else 0.0)
        modeled = category == "Device" and (item_id in (113, 114) or int(definition.get("Kind", 0)) in (1, 2, 5, 8, 9, 12))
        modeled |= category == "Drug" and int(definition.get("Kind", 0)) in (1, 2, 3)
        rows.append({
            "category": category, "id": item_id, "name": definition["Name"], "price": int(definition["Price"]),
            "ev_ratio": statistics.fmean(utilities), "variance_class": "High" if item_id in (107, 108, 111) or int(definition.get("Kind", 0)) == 8 else "Medium",
            "trigger_frequency": "Empirical hotspot" if category == "Device" else "Use-window",
            "modeled": modeled, "pick_rate": statistics.fmean(pick_rates),
        })
    return sorted(rows, key=lambda row: row["ev_ratio"], reverse=True)


def baseline_report(root: Path, balance: dict[str, Any], content: dict[str, Any], physical_raw: dict[str, Any],
                    summary: dict[str, Any], item_rows: list[dict[str, Any]], runs: int, seed: int) -> None:
    docs = root / "Docs/Balance"
    top = [row for row in item_rows if row["modeled"]][:5]
    low = [row for row in reversed(item_rows) if row["modeled"]][:5]
    failure_lines = []
    for strategy in STRATEGY_NAMES:
        failure_lines.append(f"- {strategy}: {summary[strategy]['failure_stage_counts'] or '无失败'}")
    pick_lines = []
    for strategy in STRATEGY_NAMES:
        picks = summary[strategy]["item_pick_rates"][:5]
        pick_lines.append(f"- {strategy}: " + (", ".join(f"{p['name']} {percent(p['pick_rate'])}" for p in picks) or "无购买"))
    gold_lines = []
    for label, key in (
        ("提现收入", "average_cashout_income"),
        ("银行收入", "average_bank_income"),
        ("阶段奖励", "average_stage_reward_income"),
        ("超额分奖励", "average_overkill_income"),
        ("商品购买支出", "average_purchase_spend"),
        ("刷新支出", "average_refresh_spend"),
    ):
        gold_lines.append("| " + label + " | " + " | ".join(
            f"{summary[strategy][key]:.1f}" for strategy in STRATEGY_NAMES
        ) + " |")
    route_lines = []
    for profile in physical_raw.get("Profiles", []):
        samples = max(1, int(profile.get("Samples", 0)))
        terminal_ev = 0.0
        void_rate = 0.0
        random_rate = 0.0
        target_two_rate = 0.0
        for outcome in profile.get("Outcomes", []):
            probability = int(outcome.get("Count", 0)) / samples
            if outcome.get("Kind") in ("Score", "RandomScore"):
                terminal_ev += probability * float(outcome.get("Multiplier", 0))
            if outcome.get("Kind") in ("Void", "Drain"):
                void_rate += probability
            if outcome.get("Kind") == "RandomScore":
                random_rate += probability
            if int(outcome.get("TargetId", -1)) == 2:
                target_two_rate += probability
        route_lines.append(
            f"| {profile.get('Lane')} | {float(profile.get('Flow', 0)):.1f} | {terminal_ev:.2f} | "
            f"{percent(void_rate)} | {percent(random_rate)} | {percent(target_two_rate)} |"
        )
    stage_lines = []
    for stage_index, stage in enumerate(balance["Stages"]):
        rates = []
        for strategy in STRATEGY_NAMES:
            rate = summary[strategy]["stage_clear_rates"][stage_index]["clear_rate"]
            rates.append(percent(rate))
        stage_lines.append(f"| {stage['Id']} | " + " | ".join(rates) + " |")
    strength_lines = []
    for stage_index, stage in enumerate(balance["Stages"]):
        values = []
        for strategy in STRATEGY_NAMES:
            point = summary[strategy]["build_strength_curve"][stage_index]
            strength = point["average_strength_index"]
            devices = point["average_devices"]
            values.append("N/A" if strength is None else f"{strength:.2f} ({devices:.2f}D)")
        strength_lines.append(f"| {stage['Id']} | " + " | ".join(values) + " |")
    report = f"""# Baseline Report

## 口径

- 配置：当前 `GameBalance.json` + `GameContent.json`，未应用 Proposal。
- 物理：`{physical_raw.get('SampleCount', 0):,}` 次真实 Unity PhysX 采样，场景 `{physical_raw.get('Scene', 'UNKNOWN')}`，物理指纹 `{physical_raw.get('PhysicsFingerprint', 'UNKNOWN')}`。
- 经济：每种策略 {runs:,} Runs，Seed={seed}；每个 Run 的实际派生 Seed 已写入 `baseline_results.csv`。
- 装置触发：使用每个尺寸在真实轨迹中“最佳观察网格”的经验暴露率。它代表标准化理想布置，不代表所有玩家布置。
- 特殊弹珠：{SPECIAL_BALL_NOTE}

## KPI

{metric_table(summary)}

## 实测路线 EV

终点倍率 EV 只包含实际落点与终点倍率，不含 Ball Value、装置、Rush 和赌博开奖；它用来检测盘面路线支配。

当前三种策略都会选择 Left：Right 三档均为 100% Void，Center 的收益/风险也被 Left 支配。因此现状不存在有意义的路线选择；Recommended 只下调 TargetId 2 奖励，没有把几何问题伪装成已解决。

当前三种策略都会选择 Left：Right 三档均为 100% Void，Center 的收益/风险也被 Left 支配。因此现状不存在有意义的路线选择；Recommended 只下调 TargetId 2 奖励，没有把几何问题伪装成已解决。

| 路线 | Flow | 终点倍率 EV | Void | 赌博区 | TargetId 2 区 |
| --- | ---: | ---: | ---: | ---: | ---: |
{chr(10).join(route_lines)}

## 各阶段到达率

| Stage | Conservative | Balanced | Aggressive |
| ---: | ---: | ---: | ---: |
{chr(10).join(stage_lines)}

## Build 强度增长曲线

`Strength Index` 是已建模 Device/Medicine 在阶段开始时的标准化期望效用，裸构筑为 `1.00`；括号为平均 Device 数。它用于比较成长斜率，不等同于分数倍率，未采样的路线型内容不会被伪装成确定收益。

| Stage | Conservative | Balanced | Aggressive |
| ---: | ---: | ---: | ---: |
{chr(10).join(strength_lines)}

## 失败位置

{chr(10).join(failure_lines)}

## 经济与构筑

金币收入包括阶段奖励、提现、银行和超额分奖励；支出包括购买与刷新。

| 每 Run 平均金币来源/去向 | Conservative | Balanced | Aggressive |
| --- | ---: | ---: | ---: |
{chr(10).join(gold_lines)}

Baseline 的主要收入来自提现与阶段奖励，商店支出远低于收入；因此现金库存持续膨胀。完整逐 Run 来源字段保存在 CSV，可重放异常局。

购买率最高项目：

{chr(10).join(pick_lines)}

## Cashout 行为

Baseline 每回合 Cashout Rate 为 {percent(summary['Conservative']['cashout_rate'])} / {percent(summary['Balanced']['cashout_rate'])} / {percent(summary['Aggressive']['cashout_rate'])}。在当前线性兑换与高金币产出下，“达到目标后尽快提现”几乎支配三种启发式策略。模拟器没有用动态规划求全局最优阶段，因此不能把该结果解释为精确的唯一最优 Round；它能确认的是继续消耗剩余血量通常没有足够的边际经济回报。

## 标准化 Item EV 排名

EV Ratio 是“模型效用 / 金币价格”的筛选指标，用于同类相对比较，不等同于金币或分数。未建立物理变体的路线型药物和装置不进入强弱结论。完整 Device/Medicine 目录（包括 0 Pick 项）及四配置结果见 `item_ev.csv`；Special Ball 因运行时不存在而为 N/A。

| 项目 | 类型 | 价格 | EV Ratio | Pick Rate | 状态 |
| --- | --- | ---: | ---: | ---: | --- |
{chr(10).join(f"| {row['name']} | {row['category']} | {row['price']} | {row['ev_ratio']:.4f} | {percent(row['pick_rate'])} | {'Modeled' if row['modeled'] else 'UNKNOWN: route effect'} |" for row in top + low)}

## Baseline 结论边界

1. 终点命中率来自真实盘面；商店和结算公式来自当前代码。
2. 装置布局没有唯一真值。报告采用标准化理想布置来比较，不把它解释为普通玩家必然表现。
3. 润流剂、弹性/穿透/磁力/定向等会改变路径的药物需要独立 PhysX 变体采样。当前主模拟不把这些未采样收益算作确定收益。
4. Dead Run 定义为：在仍有至少 20% 血量时，按已结算球的在线收益、实测路线均值/方差与单侧 `mean + 1.645σ` 近似后，翻盘概率仍低于约 5%；之后继续至少发射 15 球并最终失败。该指标是回溯分类，不声称是精确动态规划胜率。
"""
    (docs / "baseline_report.md").write_text(report, encoding="utf-8")


def proposal_report(root: Path, summaries: dict[str, dict[str, Any]], proposals: dict[str, Any]) -> None:
    lines = [
        "# Balance Proposal", "",
        "三套方案均使用相同 PhysX 样本、Run 数和 Seed 序列。Target Range 是设计护栏，不是要求所有策略命中同一个胜率。", "",
        "## Target Range", "",
        "| 指标 | 目标区间 | 设计意图 |", "| --- | ---: | --- |",
        "| Conservative Win Rate | 70%–90% | 安全策略可完成，但牺牲速度与成长上限 |",
        "| Balanced Win Rate | 80%–97% | 易学且可靠，但仍保留尾部失败 |",
        "| Aggressive Win Rate | 60%–95% | 高方差换速度、构筑与收益 |",
        "| Dead Run Rate | 单策略 <20%，理想 <10% | 避免早已难翻盘却继续大量操作 |",
        "| 终局金币库存 | 约 2–8 个中价商品 | 既能做选择，也不形成无用途囤积 |",
        "| Gold Spend / Income | 30%–80% | 商店既不失去意义，也不强迫清空 |",
        "| 商店购买力达标率 | 每策略每节点 ≥75% | Round 1–4 可买 2 件+1 刷；Round 5+ 可买 3 件+3 刷 |",
        "| 单项 Pick Rate | 通常 <90% | 保底槽除外，避免唯一答案 |",
        "| 路线终点 EV 比 | <3:1（长期目标） | 当前右侧 0 EV 需要后续盘面物理迭代 |", "",
    ]
    for config in CONFIG_NAMES:
        if config not in summaries:
            continue
        lines.extend([
            f"## {config}", "", metric_table(summaries[config]), "",
            "商店购买力（独立 Offer 探针；终局阶段为假想购买力检查）：", "",
            affordability_table(summaries[config]), "",
        ])
        if config != "Baseline":
            lines.extend(["参数覆盖：", "", "```json", json.dumps(proposals.get(config, {}), ensure_ascii=False, indent=2), "```", ""])
    lines.extend([
        "## Build Growth 对比", "",
        "数值为达到对应阶段的 Run 在 Stage 1 → Stage 6 的 `Strength Index`（括号为变化量）。它是条件均值；低存活方案的 Stage 6 只代表幸存构筑。", "",
        "| Config | Conservative | Balanced | Aggressive |",
        "| --- | ---: | ---: | ---: |",
    ])
    for config in CONFIG_NAMES:
        if config not in summaries:
            continue
        cells = []
        for strategy in STRATEGY_NAMES:
            curve = summaries[config][strategy]["build_strength_curve"]
            available = [point["average_strength_index"] for point in curve if point["average_strength_index"] is not None]
            if not available:
                cells.append("N/A")
                continue
            start, end = available[0], available[-1]
            cells.append(f"{start:.2f}→{end:.2f} ({end - start:+.2f})")
        lines.append(f"| {config} | " + " | ".join(cells) + " |")
    lines.append("")
    recommended = summaries.get("Recommended")
    if recommended:
        affordability_nodes = [
            (point["affordability_rate"], strategy, point["stage"])
            for strategy in STRATEGY_NAMES
            for point in recommended[strategy]["shop_affordability_curve"]
            if point["affordability_rate"] is not None
        ]
        passing_nodes = sum(rate >= 0.75 for rate, _, _ in affordability_nodes)
        minimum_rate, minimum_strategy, minimum_stage = min(affordability_nodes)
        item_pick_nodes = [
            (item["pick_rate"], item["name"], strategy)
            for strategy in STRATEGY_NAMES
            for item in recommended[strategy]["item_pick_rates"]
        ]
        highest_pick_rate, highest_pick_item, highest_pick_strategy = max(item_pick_nodes)
        balanced_win = recommended["Balanced"]["win_rate"]
        affordability_conclusion = (
            f"- Recommended 的 {passing_nodes}/{len(affordability_nodes)} 个策略×阶段购买力节点达到 75% 护栏；"
            f"最低为 {minimum_strategy} Stage {minimum_stage} 的 {percent(minimum_rate)}。"
        )
        pick_conclusion = (
            f"- 最高单项 Pick Rate 为 {highest_pick_item}（{highest_pick_strategy}）的 "
            f"{percent(highest_pick_rate)}；90% 以上视为支配策略预警。"
        )
        win_conclusion = (
            f"- Recommended 的 Balanced Win Rate 为 {percent(balanced_win)}。"
            "本轮主动降低整条目标曲线，因此高于原护栏时应优先通过真人测试确认节奏，而不是立即反向抬高目标。"
        )
    else:
        affordability_conclusion = "- Recommended 未生成，无法评估商店购买力节点。"
        pick_conclusion = "- Recommended 未生成，无法评估单项 Pick Rate。"
        win_conclusion = "- Recommended 未生成，无法评估 Balanced Win Rate。"
    lines.extend([
        "## 选择结论", "",
        "- Conservative 只做小幅调整，仍接近 Baseline 的高胜率与金币冗余，用作低风险回退方案。",
        "- Recommended 同时约束高倍区、阶段曲线、Rush/赌博 EV 与金币产出，并用价格 V3 保障商店购买力；当前正式目标曲线按最新提案生成。",
        affordability_conclusion,
        pick_conclusion,
        win_conclusion,
        "- Experimental 强化风险与稀缺性，但 Conservative 的 Dead Run 过高，因此不适合作为默认正式数值。",
        "- 路线型内容没有采样完成时，不因模型低估而直接删改玩法；优先改基础值、概率、价格、倍率、阈值和成长曲线。",
    ])
    (root / "Docs/Balance/balance_proposal.md").write_text("\n".join(lines) + "\n", encoding="utf-8")


def price_tuning_report(root: Path, summaries: dict[str, dict[str, Any]], proposals: dict[str, Any],
                        runs: int, seed: int) -> None:
    lines = [
        "# Shop Price Tuning", "",
        f"经济模拟：{len(summaries)} 配置 × 3 策略 × {runs:,} Runs；主 Seed `{seed}`。物理、目标、奖励和倍率均保持不变，仅比较商品价与刷新价。", "",
        "## KPI 定义", "",
        "- Round 1–4：进入对应商店时，金币可覆盖策略最想买的 2 件商品与 1 次刷新。",
        "- Round 5+：金币可覆盖策略最想买的 3 件商品与 3 次刷新。",
        "- `大概率` 的验收线为每种策略、每个有样本节点达标率至少 75%。",
        "- Offer 探针使用独立派生 Seed，不改变正式购买或 Run 结果；Stage 6 已无下一商店，只做终局假想购买力检查。", "",
    ]
    for config in PRICE_CONFIG_NAMES:
        if config not in summaries:
            continue
        summary = summaries[config]
        checked = [
            row["affordability_rate"]
            for strategy in STRATEGY_NAMES
            for row in summary[strategy]["shop_affordability_curve"]
            if row["affordability_rate"] is not None
        ]
        passes = sum(value >= 0.75 for value in checked)
        lines.extend([
            f"## {config}", "",
            f"购买力节点通过：`{passes}/{len(checked)}`；最低达标率：`{percent(min(checked)) if checked else 'N/A'}`。", "",
            metric_table(summary), "",
            affordability_table(summary), "",
        ])
        if config != "Current":
            lines.extend(["价格覆盖：", "", "```json", json.dumps(proposals[config], ensure_ascii=False, indent=2), "```", ""])
    eligible = []
    for config in PRICE_CONFIG_NAMES[1:]:
        if config not in summaries:
            continue
        rates = [
            row["affordability_rate"]
            for strategy in STRATEGY_NAMES
            for row in summaries[config][strategy]["shop_affordability_curve"]
            if row["affordability_rate"] is not None
        ]
        if rates and all(value >= 0.75 for value in rates):
            eligible.append(config)
    lines.extend([
        "## 选择规则", "",
        "优先采用满足全部购买力节点的最小降价方案；若多个方案都满足，则比较购买数、金币存量、Pick Rate 与 Combo 风险，不以更高购买力为单一目标。", "",
        f"正式选择：`{eligible[0] if eligible else '无方案通过'}`。它是按候选顺序第一个通过全部节点的方案。", "",
        "逐 Run 原始结果见 `price_tuning_results.csv`，聚合购买力见 `price_affordability.csv`，机器可读汇总见 `price_tuning_summary.json`。", "",
    ])
    (root / "Docs/Balance/price_tuning_report.md").write_text("\n".join(lines), encoding="utf-8")


def item_tuning_report(root: Path, summaries: dict[str, dict[str, Any]],
                       item_tables: dict[str, list[dict[str, Any]]], proposals: dict[str, Any],
                       runs: int, seed: int) -> None:
    lines = [
        "# Item & Medicine Effect Tuning", "",
        f"经济模拟：{len(summaries)} 配置 × 3 策略 × {runs:,} Runs；固定 Seed `{seed}`。阶段目标、奖励、物理分布和正式价格保持不变，仅比较效果强度与必要的单项价格修正。", "",
        "## 护栏", "",
        "- 已建模内容的平均 Pick Rate 不应由单个选项长期垄断；90% 为支配策略红线，75% 为观察线。",
        "- 降低最高 EV 集群时，同时保留至少三种可购买的 Rush 路线，避免把唯一强解换成唯一弱解。",
        "- 路线/物理型装置和药物缺少变体物理样本，除修复无限递归外不凭启发式 EV 大幅改强度。",
        "- 胜率只作回归护栏；效果方案不会为了追一个固定胜率而反向抬高阶段目标。", "",
    ]
    for config in ITEM_CONFIG_NAMES:
        if config not in summaries:
            continue
        modeled = [row for row in item_tables[config] if row["modeled"]]
        top_ev = modeled[0]
        low_ev = modeled[-1]
        max_pick = max(
            (item["pick_rate"], item["name"], strategy)
            for strategy in STRATEGY_NAMES
            for item in summaries[config][strategy]["item_pick_rates"]
        )
        viable = sum(row["pick_rate"] >= 0.05 for row in modeled)
        lines.extend([
            f"## {config}", "",
            metric_table(summaries[config]), "",
            f"- 最高单策略 Pick Rate：{max_pick[1]} / {max_pick[2]} = {percent(max_pick[0])}。",
            f"- 已建模且平均 Pick Rate ≥5%：{viable}/{len(modeled)}。",
            f"- 最高/最低建模 EV÷价格：{top_ev['name']} {top_ev['ev_ratio']:.4f} / {low_ev['name']} {low_ev['ev_ratio']:.4f}。", "",
        ])
        if config != "Current":
            lines.extend(["效果覆盖：", "", "```json", json.dumps(proposals[config], ensure_ascii=False, indent=2), "```", ""])
    lines.extend([
        "## 采用结论", "",
        "正式采用 `ItemRecommended`：压低当前购买率最高的三种 Rush 药物与乘法装置，保留各自定位；四叶草与肾上腺素不再组成 100% 确定点火。", "",
        "`无限增值` 无法仅靠价格平衡：复制体继承复制能力会产生 2^n 球数。运行时改为复用 `MaxSplitGeneration` 的复制代数上限，默认最多复制 2 代；这是本轮唯一必要的规则级安全修复。", "",
        "原始逐 Run 结果见 `item_tuning_results.csv`，效果 EV 与 Pick Rate 见 `item_tuning_ev.csv`，聚合结果见 `item_tuning_summary.json`。", "",
    ])
    (root / "Docs/Balance/item_tuning_report.md").write_text("\n".join(lines), encoding="utf-8")


def combo_report(root: Path, content: dict[str, Any]) -> None:
    attrs = {row["Id"]: row for row in content["Attributes"]}
    devices = {int(row["Id"]): row for row in content["Devices"]}
    drugs = {int(row["Id"]): row for row in content["Drugs"]}
    gamble_ev = (
        attrs["GAMBLE_RETURN_CHANCE"]["DefaultValue"] +
        2 * attrs["GAMBLE_WIN_CHANCE"]["DefaultValue"] +
        4 * attrs["GAMBLE_QUADRUPLE_CHANCE"]["DefaultValue"]
    )
    replication_cap = 2 ** int(content["MaxSplitGeneration"])
    clover_combo = float(devices[104]["Value"]) + float(drugs[100].get("RushChanceAdd", 0))
    lens_cap_hits = math.ceil(math.log(attrs["BALL_BASE_VALUE"]["MaxValue"], devices[107]["Value"]))
    ding_cap_hits = math.ceil(math.log(attrs["MACHINE_MULT"]["MaxValue"], devices[113]["ScoreMultiplier"]))
    chain_multiplier = float(devices[107]["Value"]) * float(devices[108]["Value"])
    rows = [
        ("Watch", "无限增值 × 多个可触发对象", f"复制链复用 MaxSplitGeneration={content['MaxSplitGeneration']} 的独立复制代数上限；单颗原球最多扩展到 {replication_cap} 球，不再无界递归。"),
        ("Watch", "增值透镜 × 回流路线", f"每次重新进入 ×{devices[107]['Value']}; BALL_BASE_VALUE 从 1 到 {attrs['BALL_BASE_VALUE']['MaxValue']} 约需 {lens_cap_hits} 次，仍需回流盘压测。"),
        ("Watch", "资本主义 × 增值透镜", f"单次串联倍率为 ×{chain_multiplier:.3f}；资本主义每装置每球最多触发 2 次，仍需关注多实例回流。"),
        ("Watch", "Multi Ding × Multi Ding", f"机器乘区 ×{devices[113]['ScoreMultiplier']}^n，但 MACHINE_MULT 上限 {attrs['MACHINE_MULT']['MaxValue']}；约 {ding_cap_hits} 次有效触发触顶。"),
        ("Watch", "增幅塔 × 回流路线", f"每次过线 ×{devices[101]['Value']} 且无总次数上限；达到 10,000 约需 {math.ceil(math.log(attrs['BALL_BASE_VALUE']['MaxValue'], devices[101]['Value']))} 次。"),
        ("Watch", "黑色四叶草 × 肾上腺素", f"下一次有效 Rush 概率 {percent(devices[104]['Value'])} + {percent(drugs[100].get('RushChanceAdd', 0))} = {percent(clover_combo)}；同家族只检定一次，不再确定点火。"),
        ("Safe", "分流器 × 分流器", f"每代总价值 2×0.5=1 倍且 MaxSplitGeneration={content['MaxSplitGeneration']}，不会指数增值。"),
        ("Safe", "复活 × 复制", "RefunedFamilies 按 Family 去重，整族只返一次原始血耗，不能用克隆重复返血。"),
        ("Safe", "赌博四档", f"当前基础 EV={gamble_ev:.2f}；四档概率合计为1且单局只开奖一次。"),
    ]
    matrix: list[dict[str, Any]] = []

    def add_combo(category_a: str, id_a: Any, name_a: str, category_b: str, id_b: Any,
                  name_b: str, status: str, reason: str) -> None:
        matrix.append({
            "category_a": category_a, "id_a": id_a, "name_a": name_a,
            "category_b": category_b, "id_b": id_b, "name_b": name_b,
            "status": status, "reason": reason,
        })

    device_rows = sorted(content["Devices"], key=lambda value: int(value["Id"]))
    drug_rows = sorted(content["Drugs"], key=lambda value: int(value["Id"]))
    for index, left in enumerate(device_rows):
        for right in device_rows[index:]:
            pair = tuple(sorted((int(left["Id"]), int(right["Id"]))))
            if pair == (107, 108):
                status, reason = "Watch", f"基础价值串联为 ×{chain_multiplier:.3f}；重复路线仍会复利，但较原 ×6 收敛。"
            elif pair in ((101, 101), (113, 113)):
                status, reason = "Watch", "同类乘法可重复，虽有属性上限，仍会在少量触发后接近上限。"
            elif pair == (105, 105):
                status, reason = "Safe", "分裂代数上限为2且每代总价值守恒。"
            elif all(item in {101, 107, 108, 113} for item in pair):
                status, reason = "Watch", "至少两个倍率环节相乘；有硬上限，但需要在回流盘面压测触顶速度。"
            else:
                status, reason = "UNKNOWN", "未发现已编码的递归/乘法规则；仍需实际布局或物理变体证据。"
            add_combo("Device", left["Id"], left["Name"], "Device", right["Id"], right["Name"], status, reason)

    rush_device_ids = {104, 114}
    rush_drug_ids = {100, 101, 102}
    for drug in drug_rows:
        for device in device_rows:
            drug_id, device_id = int(drug["Id"]), int(device["Id"])
            if drug_id == 107:
                status, reason = "Watch", f"复制体继承能力，但独立复制代数封顶 {content['MaxSplitGeneration']}；单颗原球最多 {replication_cap} 球。"
            elif drug_id == 100 and device_id == 104:
                status, reason = "Watch", f"SET 与 ADD 合计 {percent(clover_combo)}，不再是确定触发。"
            elif drug_id in rush_drug_ids and device_id in rush_device_ids:
                status, reason = "Watch", "Rush 点火、倍率或窗口叠加，提高爆发覆盖率；受时长/倍率上限约束。"
            else:
                status, reason = "UNKNOWN", "路线或临时窗口效果未建立配对物理样本，不能宣称安全。"
            add_combo("Medicine", drug["Id"], drug["Name"], "Device", device["Id"], device["Name"], status, reason)

    for index, left in enumerate(drug_rows):
        for right in drug_rows[index:]:
            pair = {int(left["Id"]), int(right["Id"])}
            if 107 in pair:
                status, reason = "Watch", f"包含复制药物，但独立复制代数封顶 {content['MaxSplitGeneration']}，不再只依赖全局球数上限。"
            elif pair <= rush_drug_ids:
                status, reason = "Watch", "Rush 概率、倍率与持续时间可在同一窗口协同。"
            else:
                status, reason = "UNKNOWN", "缺少该双药窗口的物理变体采样。"
            add_combo("Medicine", left["Id"], left["Name"], "Medicine", right["Id"], right["Name"], status, reason)

    add_combo("Device", "*", "全部装置", "SpecialBall", "N/A", "运行时无目录", "N/A", "当前项目没有 SpecialBall 数据或库存系统。")
    add_combo("SpecialBall", "N/A", "运行时无目录", "SpecialBall", "N/A", "运行时无目录", "N/A", "当前项目没有 SpecialBall 数据或库存系统。")
    matrix_path = root / "Docs/Balance/combo_matrix.csv"
    with matrix_path.open("w", newline="", encoding="utf-8-sig") as handle:
        writer = csv.DictWriter(handle, fieldnames=[
            "category_a", "id_a", "name_a", "category_b", "id_b", "name_b", "status", "reason",
        ])
        writer.writeheader()
        writer.writerows(matrix)
    text = "# Combo Risk Report\n\n| 级别 | 组合 | 数学原因 |\n| --- | --- | --- |\n" + "\n".join(
        f"| {level} | {name} | {reason} |" for level, name, reason in rows
    ) + "\n\n完整 Device×Device、Medicine×Device、Medicine×Medicine 扫描见 `combo_matrix.csv`。没有证据的组合标为 `UNKNOWN`，不会伪装为 Safe；Device×SpecialBall 与 SpecialBall×SpecialBall 因运行时无目录标为 N/A。\n\n所有分数、球数和金币都有硬上限，因此没有字面意义上的无限数值；`Broken` 指在正常 Run 尺度内会迅速撞上上限或性能容量。\n"
    (root / "Docs/Balance/combo_risk_report.md").write_text(text, encoding="utf-8")


def overview_report(root: Path, balance: dict[str, Any], content: dict[str, Any], physical_raw: dict[str, Any]) -> None:
    attrs = {row["Id"]: row for row in content["Attributes"]}
    stages = "\n".join(
        f"| {stage['Id']} | {stage['TargetScore']} | {stage['RewardCoins']} | {stage['SkipRewardCoins']} | {stage['CashoutRate']} |"
        for stage in balance["Stages"]
    )
    text = f"""# Balance Overview

## Source of Truth

1. `Assets/Config/GameBalance.xlsx` → 自动导入 `Assets/Resources/GameBalance.json`：血量、发射、容量、战役和阶段。
2. `Assets/Resources/GameContent.json`：34 个属性、15 个装置、12 个药物的运行时唯一目录。
3. `Assets/Scenes/TestScene.unity`：盘面几何、Collider、PhysicMaterial、Launcher、Zone Inspector 值。
4. 代码：结算顺序、叠加、上限、商店保底、Cashout 条件和失败条件。

## 实际 Gameplay Loop

`Build（必须放完未布置装置） → Playing（每回合补满 BloodCapacity） → Draining → Shop → Build`。确认总分达到当前累计目标且仍有血量时可 Cashout；提现把剩余血量清零并换金币，场上球继续结算。未达到目标则 Lost，跨过最后阶段则 Won。

当前没有独立 SpecialBall 目录。药物只影响生效窗口内新发射的球；装置、库存、金币、累计分和布局跨回合保留。

## 统一核心数值

- 初始/每回合血量：{balance['Marble']['BloodCapacity']}
- 抽血档位：{balance['Marble']['MinInvestment']}–{balance['Marble']['MaxInvestment']}
- 单球基础价值：{attrs['BALL_BASE_VALUE']['DefaultValue']}
- 同时活动球上限：{balance['Marble']['MaxActiveMarbles']}
- Rush：阈值 {content['RushThreshold']}，倍率 ×{attrs['RUSH_MULT']['DefaultValue']}，持续 {attrs['RUSH_DURATION']['DefaultValue']} 秒，上限 {attrs['RUSH_DURATION']['MaxValue']} 秒
- 赌博：0/1/2/4 倍，权重分别为 {percent(attrs['GAMBLE_LOSS_CHANCE']['DefaultValue'])} / {percent(attrs['GAMBLE_RETURN_CHANCE']['DefaultValue'])} / {percent(attrs['GAMBLE_WIN_CHANCE']['DefaultValue'])} / {percent(attrs['GAMBLE_QUADRUPLE_CHANCE']['DefaultValue'])}
- 初始金币：{balance['Campaign']['StartingCoins']}；刷新：{balance['Campaign']['ShopRefreshCost']}；商品槽：{balance['Campaign']['ShopOfferCount']}
- 经验物理样本：{physical_raw.get('SampleCount', 0)}；场景依赖指纹：{physical_raw.get('PhysicsFingerprint', 'UNKNOWN')}

## Round Progression

| Round | 累计目标 | 基础奖励 | 越级奖励 | Cashout Rate |
| ---: | ---: | ---: | ---: | ---: |
{stages}

## Hard-coded / 分散参数

- `AttributeRuntime.RadiusToWorld = 0.16`。
- 稀血剂穿透 30%、传送锁 0.5 秒、分流角度/保留率等行为值仍在代码中；四叶草概率已迁移到 `DeviceDefinition.Value`。
- `ContentSettlementSystem` 与 `DeviceEffectSystem` 包含若干行为阈值（资本主义价值 6、分流价值 2）。
- 这些值已列入冲突/债务报告；本次不为统一配置而重写玩法系统。
"""
    (root / "Docs/Balance/balance_overview.md").write_text(text, encoding="utf-8")


def methodology_report(root: Path, physical_raw: dict[str, Any], runs: int, seed: int) -> None:
    text = f"""# Simulation Methodology

## Hybrid Simulation

第一层由 `BalancePhysicalSampler` 保存当前 Editor Scene Setup，在临时 additive Scene 中克隆 `TestScene/PhysicsRoot`，暂时禁用原根对象并手动推进 Unity Physics；采样结束后恢复原 Scene Setup。采样使用实际 Collider、Rigidbody、PhysicMaterial、Gravity、Launcher 旋转和 `MarblePhysicsBridge.Simulate`。对 Left/Center/Right 三条发射带与低/中/高三档流量分层，共采样 {physical_raw.get('SampleCount', 0)} 球。

每个样本记录 Seed、发射位置、流量、终点、倍率、Rush 概率、飞行时间、Pin/Device 接触。原始结果见 `physical_samples.csv`，汇总见 `physical_profile.json`。

第二层从经验终点分布有放回抽样，执行战役、Rush、共享赌博池、累计阶段目标、Cashout、金币、商店保底、购买和刷新。每个策略 {runs:,} Runs，主 Seed={seed}。

商店购买力探针使用独立派生 Seed，不改变主模拟的 Offer 或购买 RNG。Round 1–4 检查“策略最想买的 2 件 + 1 次刷新”，Round 5+ 检查“策略最想买的 3 件 + 3 次刷新”；达标率目标为每种策略、每个有样本节点至少 75%。Stage 6 已无后续商店，仅作为终局假想购买力检查。

## 策略

- Conservative：选择最低 Void 路线，使用 25% 流量档，达到条件即提现，偏好稳定 Rush Chance，最多花 45% 现金，不刷新。
- Balanced：选择最高平均区倍率路线，平时低流量、Rush 高流量，保留 28% 现金，最多刷新一次。
- Aggressive：选择高倍/赌博区暴露最高路线，中流量起步、Rush 高流量，最多花 95% 现金并刷新两次。

## 可复现性

- 经济 PRNG 完全由每行 `seed` 重放。
- PhysX 的 Seed 固定发射选择，但 Unity PhysX 不承诺跨平台位级确定性；报告保存 Unity 版本和场景依赖 Hash。
- Proposal 使用同一物理 Profile 与同一组经济 Seed（common random numbers），降低方案差值噪声。

## Dead Run 判定

从已结算弹珠在线估计单球收益，并结合实测路线的终点均值/方差、剩余可发射球数和单侧 `mean + 1.645σ` 上界。若在血量仍高于 20% 时估计翻盘概率低于约 5%，之后又继续至少 15 球且最终失败，则记为 Dead Run。该指标是可复现近似，不是完整 MDP 求解。

## UNKNOWN / ASSUMPTION

- 装置可自由布置，没有唯一“真实命中率”。模拟使用各尺寸在采样轨迹中最佳观察网格的经验暴露率，并对重复装置使用显式递减系数。
- 改变摩擦、弹性、穿透、磁力、定向和复制的药物尚无独立物理变体采样；其路线收益不作为确定结论。
- 人类选择不是数学最优。三种策略使用带阈值的启发式购买与用药。
- {SPECIAL_BALL_NOTE}
"""
    (root / "Docs/Balance/simulation_methodology.md").write_text(text, encoding="utf-8")


def dashboard_summary(config: str, summary: dict[str, Any], item_rows: list[dict[str, Any]], runs: int) -> dict[str, Any]:
    top = ", ".join(row["name"] for row in item_rows[:3]) or "N/A"
    low = ", ".join(row["name"] for row in item_rows[-3:]) or "N/A"
    return {
        "Config": config,
        "Runs": runs,
        "Strategies": [
            {
                "Strategy": name,
                "WinRate": summary[name]["win_rate"],
                "RoundClearRate": summary[name]["round_clear_rate"],
                "AverageScore": summary[name]["average_score"],
                "AverageGold": summary[name]["average_gold_stock"],
                "AverageBloodRemaining": summary[name]["average_blood_remaining"],
                "CashoutRate": summary[name]["cashout_rate"],
                "DeadRunRate": summary[name]["dead_run_rate"],
                "ShopAffordability": summary[name]["shop_affordability_curve"],
                "TopItems": top,
                "LowItems": low,
            }
            for name in STRATEGY_NAMES
        ],
    }


def simulate(root: Path, runs: int, seed: int, balance_source_path: Path | None = None,
             content_source_path: Path | None = None) -> None:
    docs = root / "Docs/Balance"
    docs.mkdir(parents=True, exist_ok=True)
    balance_path = (balance_source_path or (root / "Assets/Resources/GameBalance.json")).resolve()
    content_path = (content_source_path or (root / "Assets/Resources/GameContent.json")).resolve()
    physical_path = docs / "physical_profile.json"
    proposal_path = root / "Tools/Balance/proposals.json"
    balance_source = read_json(balance_path)
    content_source = read_json(content_path)
    physics, physical_raw = load_physics(physical_path)
    proposals = read_json(proposal_path) if proposal_path.exists() else {}

    snapshots = docs / "config_snapshots"
    snapshots.mkdir(parents=True, exist_ok=True)
    write_json(snapshots / "baseline_GameBalance.json", balance_source)
    write_json(snapshots / "baseline_GameContent.json", content_source)
    write_json(snapshots / "baseline_manifest.json", {
        "fingerprint": stable_fingerprint(balance_path, content_path, physical_path),
        "seed": seed, "runs_per_strategy": runs,
        "physical_profile": physical_raw.get("PhysicsFingerprint"),
    })

    all_rows: list[RunResult] = []
    summaries: dict[str, dict[str, Any]] = {}
    item_tables: dict[str, list[dict[str, Any]]] = {}
    overview_balance = copy.deepcopy(balance_source)
    overview_content = copy.deepcopy(content_source)
    for config_name in CONFIG_NAMES:
        if config_name != "Baseline" and config_name not in proposals:
            continue
        balance = copy.deepcopy(balance_source)
        content = copy.deepcopy(content_source)
        if config_name != "Baseline":
            apply_overrides(balance, content, proposals[config_name])
        physics_config = copy.deepcopy(physics)
        if config_name != "Baseline":
            apply_physics_overrides(physics_config, proposals[config_name])
        if config_name == "Recommended":
            overview_balance = copy.deepcopy(balance)
            overview_content = copy.deepcopy(content)
        slug = config_name.lower()
        write_json(snapshots / f"{slug}_GameBalance.json", balance)
        write_json(snapshots / f"{slug}_GameContent.json", content)
        write_json(snapshots / f"{slug}_manifest.json", {
            "fingerprint": stable_fingerprint(balance_path, content_path, physical_path),
            "seed": seed,
            "runs_per_strategy": runs,
            "physical_profile": physical_raw.get("PhysicsFingerprint"),
            "zone_multipliers": proposals.get(config_name, {}).get("ZoneMultipliers", {}),
        })
        model = CampaignModel(balance, content, physics_config)
        rows = run_config(config_name, balance, content, physics_config, runs, seed)
        all_rows.extend(rows)
        summaries[config_name] = summarize(rows, model)
        item_tables[config_name] = build_item_rows(model, summaries[config_name])
        if config_name == "Baseline":
            write_run_csv(docs / "baseline_results.csv", rows)
            baseline_report(root, balance, content, physical_raw, summaries[config_name], item_tables[config_name], runs, seed)

    write_run_csv(docs / "proposal_results.csv", all_rows)
    write_item_csv(docs / "item_ev.csv", item_tables)
    write_shop_affordability_csv(docs / "shop_affordability.csv", summaries)
    write_json(docs / "simulation_summary.json", summaries)
    proposal_report(root, summaries, proposals)
    combo_report(root, overview_content)
    overview_report(root, overview_balance, overview_content, physical_raw)
    methodology_report(root, physical_raw, runs, seed)
    latest = "Recommended" if "Recommended" in summaries else "Baseline"
    write_json(docs / "latest_summary.json", dashboard_summary(latest, summaries[latest], item_tables[latest], runs))
    write_json(docs / "dashboard_configs.json", {
        "Configs": [
            dashboard_summary(name, summaries[name], item_tables[name], runs)
            for name in CONFIG_NAMES if name in summaries
        ]
    })
    print(f"Completed {len(all_rows):,} campaign simulations across {len(summaries)} config(s).")
    for config_name, summary in summaries.items():
        values = ", ".join(f"{name} win={percent(summary[name]['win_rate'])}" for name in STRATEGY_NAMES)
        print(f"{config_name}: {values}")


def simulate_prices(root: Path, runs: int, seed: int) -> None:
    docs = root / "Docs/Balance"
    docs.mkdir(parents=True, exist_ok=True)
    balance = read_json(root / "Assets/Resources/GameBalance.json")
    content = read_json(root / "Assets/Resources/GameContent.json")
    physics, _ = load_physics(docs / "physical_profile.json")
    proposals = read_json(root / "Tools/Balance/proposals.json")
    current_physics = copy.deepcopy(physics)
    apply_physics_overrides(current_physics, proposals["Recommended"])
    summaries: dict[str, dict[str, Any]] = {}
    all_rows: list[RunResult] = []
    for config in PRICE_CONFIG_NAMES:
        candidate_balance = copy.deepcopy(balance)
        candidate_content = copy.deepcopy(content)
        if config != "Current":
            if config not in proposals:
                raise SystemExit(f"Missing price proposal: {config}")
            apply_overrides(candidate_balance, candidate_content, proposals[config])
        rows = run_config(config, candidate_balance, candidate_content, current_physics, runs, seed)
        model = CampaignModel(candidate_balance, candidate_content, current_physics)
        summaries[config] = summarize(rows, model)
        all_rows.extend(rows)
    write_run_csv(docs / "price_tuning_results.csv", all_rows)
    write_shop_affordability_csv(docs / "price_affordability.csv", summaries)
    write_json(docs / "price_tuning_summary.json", summaries)
    price_tuning_report(root, summaries, proposals, runs, seed)
    print(f"Completed {len(all_rows):,} price-tuning simulations across {len(summaries)} config(s).")
    for config, summary in summaries.items():
        rates = [
            row["affordability_rate"]
            for strategy in STRATEGY_NAMES
            for row in summary[strategy]["shop_affordability_curve"]
            if row["affordability_rate"] is not None
        ]
        passes = sum(value >= 0.75 for value in rates)
        print(f"{config}: affordability={passes}/{len(rates)}, minimum={percent(min(rates))}")


def simulate_items(root: Path, runs: int, seed: int, content_source_path: Path | None = None) -> None:
    docs = root / "Docs/Balance"
    docs.mkdir(parents=True, exist_ok=True)
    balance = read_json(root / "Assets/Resources/GameBalance.json")
    content = read_json(content_source_path or (root / "Assets/Resources/GameContent.json"))
    physics, _ = load_physics(docs / "physical_profile.json")
    proposals = read_json(root / "Tools/Balance/proposals.json")
    current_physics = copy.deepcopy(physics)
    apply_physics_overrides(current_physics, proposals["Recommended"])
    summaries: dict[str, dict[str, Any]] = {}
    item_tables: dict[str, list[dict[str, Any]]] = {}
    all_rows: list[RunResult] = []
    for config in ITEM_CONFIG_NAMES:
        candidate_balance = copy.deepcopy(balance)
        candidate_content = copy.deepcopy(content)
        if config != "Current":
            if config not in proposals:
                raise SystemExit(f"Missing item proposal: {config}")
            apply_overrides(candidate_balance, candidate_content, proposals[config])
        rows = run_config(config, candidate_balance, candidate_content, current_physics, runs, seed)
        model = CampaignModel(candidate_balance, candidate_content, current_physics)
        summaries[config] = summarize(rows, model)
        item_tables[config] = build_item_rows(model, summaries[config])
        all_rows.extend(rows)
    write_run_csv(docs / "item_tuning_results.csv", all_rows)
    write_item_csv(docs / "item_tuning_ev.csv", item_tables, ITEM_CONFIG_NAMES)
    write_json(docs / "item_tuning_summary.json", summaries)
    item_tuning_report(root, summaries, item_tables, proposals, runs, seed)
    print(f"Completed {len(all_rows):,} item-tuning simulations across {len(summaries)} config(s).")
    for config in ITEM_CONFIG_NAMES:
        summary = summaries[config]
        max_pick = max(
            (item["pick_rate"], item["name"], strategy)
            for strategy in STRATEGY_NAMES
            for item in summary[strategy]["item_pick_rates"]
        )
        print(f"{config}: max_pick={max_pick[1]}/{max_pick[2]} {percent(max_pick[0])}")


def main() -> None:
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest="command", required=True)
    simulate_parser = sub.add_parser("simulate")
    simulate_parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[2])
    simulate_parser.add_argument("--runs", type=int, default=10000)
    simulate_parser.add_argument("--seed", type=int, default=20260930)
    simulate_parser.add_argument("--balance-source", type=Path)
    simulate_parser.add_argument("--content-source", type=Path)
    price_parser = sub.add_parser("simulate-prices")
    price_parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[2])
    price_parser.add_argument("--runs", type=int, default=10000)
    price_parser.add_argument("--seed", type=int, default=20260930)
    item_parser = sub.add_parser("simulate-items")
    item_parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[2])
    item_parser.add_argument("--runs", type=int, default=10000)
    item_parser.add_argument("--seed", type=int, default=20260930)
    item_parser.add_argument("--content-source", type=Path)
    args = parser.parse_args()
    if args.command == "simulate":
        if args.runs < 100:
            raise SystemExit("Use at least 100 runs per strategy.")
        root = args.root.resolve()
        balance_source = (root / args.balance_source).resolve() if args.balance_source and not args.balance_source.is_absolute() else args.balance_source
        content_source = (root / args.content_source).resolve() if args.content_source and not args.content_source.is_absolute() else args.content_source
        simulate(root, args.runs, args.seed, balance_source, content_source)
    elif args.command == "simulate-prices":
        if args.runs < 100:
            raise SystemExit("Use at least 100 runs per strategy.")
        simulate_prices(args.root.resolve(), args.runs, args.seed)
    elif args.command == "simulate-items":
        if args.runs < 100:
            raise SystemExit("Use at least 100 runs per strategy.")
        root = args.root.resolve()
        content_source = (root / args.content_source).resolve() if args.content_source and not args.content_source.is_absolute() else args.content_source
        simulate_items(root, args.runs, args.seed, content_source)


if __name__ == "__main__":
    main()
