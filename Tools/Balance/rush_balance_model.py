#!/usr/bin/env python3
"""DEPRECATED historical prototype for the campaign/Rush loop.

Do not use this file for formal balance conclusions: its routing probabilities
are hand-authored assumptions. Use ``balance_pipeline.py`` with the Unity-generated
``Docs/Balance/physical_profile.json`` instead. The implementation is retained only
to reproduce the earlier Rush-specific report.
"""

from __future__ import annotations

import argparse
import heapq
import json
import math
import random
import re
import statistics
from dataclasses import dataclass
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]

# Physics-routing assumptions. Update these from telemetry once Play Mode data is
# available; keeping them explicit is more useful than hiding them in a formula.
HIGH_SCORE_CHANCE = 0.30
BASE_SCORE_CHANCE = 0.50
RANDOM_SCORE_CHANCE = 0.10
DEVICE_HIT_CHANCE = 0.55
CLOVER_HIT_CHANCE = 0.45
LENS_HIT_CHANCE = 0.30
FLIGHT_SECONDS = (1.8, 3.2)

# Actual-play calibration (2026-09-30): a 150-point opening milestone was
# playable while the abstract model's 1000-point scale was not.  Keep the
# simulator's relative build deltas, but map its absolute scores to the scene.
OBSERVED_SCORE_SCALE = 0.15

@dataclass(frozen=True)
class Build:
    name: str
    multi_dings: int = 0
    rush_dings: int = 0
    clovers: int = 0
    lenses: int = 0
    adrenaline: int = 0
    rush_fruit: int = 0
    rush_duration: int = 0
    timed: bool = False
    early_drugs: bool = False


@dataclass
class Ball:
    settle_at: float
    value: float
    machine_multiplier: float
    rush_add: float
    clover: bool
    rush_fruit: bool
    rush_duration: float


def percentile(sorted_values: list[float], quantile: float) -> float:
    index = (len(sorted_values) - 1) * quantile
    lower = math.floor(index)
    upper = math.ceil(index)
    if lower == upper:
        return sorted_values[lower]
    return sorted_values[lower] * (upper - index) + sorted_values[upper] * (index - lower)


def load_balance() -> tuple[dict, dict]:
    balance = json.loads((ROOT / "Assets/Resources/GameBalance.json").read_text(encoding="utf-8-sig"))
    content = json.loads((ROOT / "Assets/Resources/GameContent.json").read_text(encoding="utf-8-sig"))
    scene = (ROOT / "Assets/Scenes/TestScene.unity").read_text(encoding="utf-8")
    if not re.search(r"BaseRushChance: ", scene):
        raise SystemExit(
            "This historical probability model does not support cumulative RUSH. "
            "Use a historical checkout or migrate the simulator before analyzing the current game."
        )
    attributes = {row["Id"]: row for row in content["Attributes"]}
    devices = {row["Id"]: row for row in content["Devices"]}
    drugs = {row["Kind"]: row for row in content["Drugs"]}
    nonzero_rush = [float(value) for value in re.findall(r"BaseRushChance: ([0-9.]+)", scene) if float(value) > 0]
    return balance, {
        "raw": content,
        "attributes": attributes,
        "devices": devices,
        "drugs": drugs,
        "base_rush_chance": nonzero_rush[0],
    }


def effect_multiplier(drug: dict, attribute: int, default: float = 1.0) -> float:
    result = default
    for effect in drug.get("Effects", []):
        if effect["Attribute"] == attribute and effect["Operation"] == 1:
            result *= float(effect["Value"])
    return result


def simulate_round(rng: random.Random, build: Build, balance: dict, content: dict) -> dict[str, float]:
    attrs = content["attributes"]
    blood = float(balance["Marble"]["BloodCapacity"])
    min_investment = float(balance["Marble"]["MinInvestment"])
    max_investment = float(balance["Marble"]["MaxInvestment"])
    interval = float(balance["Marble"]["FireIntervalSeconds"])
    base_cost = float(attrs["BLOOD_COST"]["DefaultValue"])
    base_value = float(attrs["BALL_BASE_VALUE"]["DefaultValue"])
    rush_multiplier = float(attrs["RUSH_MULT"]["DefaultValue"])
    rush_duration_base = float(attrs["RUSH_DURATION"]["DefaultValue"])
    rush_duration_cap = float(attrs["RUSH_DURATION"]["MaxValue"])
    threshold = float(content["raw"]["RushThreshold"])
    flow_value_exponent = float(content["raw"]["FlowValueExponent"])
    rush_ding_add = float(content["devices"][114]["RushChanceAdd"])
    adrenaline = content["drugs"][1]
    rush_fruit = content["drugs"][2]
    duration_drug = content["drugs"][3]
    adrenaline_rush_add = float(adrenaline["RushChanceAdd"])
    rush_fruit_multiplier = float(rush_fruit["RushScoreMultiplier"])
    adrenaline_cost = effect_multiplier(adrenaline, 0)
    duration_cost = effect_multiplier(duration_drug, 0)
    duration_multiplier = effect_multiplier(duration_drug, 22)
    drug_durations = {
        "adrenaline": float(adrenaline["DurationSeconds"]),
        "rush_fruit": float(rush_fruit["DurationSeconds"]),
        "rush_duration": float(duration_drug["DurationSeconds"]),
    }

    time = 0.0
    last_time = 0.0
    next_launch = 0.0
    rush_remaining = 0.0
    rush_meter = 0.0
    score = 0.0
    rush_score = 0.0
    pending_score = 0.0
    pending_rush_score = 0.0
    rush_triggers = 0
    rush_seconds = 0.0
    launched = 0
    events: list[tuple[float, int, Ball]] = []
    event_id = 0

    inventory = {
        "adrenaline": build.adrenaline,
        "rush_fruit": build.rush_fruit,
        "rush_duration": build.rush_duration,
    }
    drug_ends = {key: -1.0 for key in inventory}

    def advance(to_time: float) -> None:
        nonlocal last_time, rush_remaining, rush_seconds
        delta = max(0.0, to_time - last_time)
        if rush_remaining > 0:
            active = min(delta, rush_remaining)
            rush_seconds += active
            rush_remaining = max(0.0, rush_remaining - delta)
        last_time = to_time

    def use_drug(name: str) -> None:
        if inventory[name] <= 0 or drug_ends[name] > time:
            return
        inventory[name] -= 1
        drug_ends[name] = time + drug_durations[name]

    while blood + 1e-9 >= min_investment * base_cost or events:
        next_settle = events[0][0] if events else math.inf
        can_launch = blood + 1e-9 >= min_investment * base_cost
        if can_launch and next_launch <= next_settle:
            time = next_launch
            advance(time)

            if build.early_drugs and launched == 0:
                use_drug("adrenaline")
                use_drug("rush_duration")
                use_drug("rush_fruit")
            if build.timed and rush_meter >= threshold * 0.625 and rush_remaining <= 0:
                use_drug("adrenaline")
                use_drug("rush_duration")
            if build.timed and rush_remaining > 0:
                use_drug("rush_fruit")

            bursting = (build.timed or build.early_drugs) and rush_remaining > 0
            investment = max_investment if bursting else min_investment
            adrenaline_active = drug_ends["adrenaline"] > time
            duration_active = drug_ends["rush_duration"] > time
            fruit_active = drug_ends["rush_fruit"] > time
            cost_multiplier = (adrenaline_cost if adrenaline_active else 1.0) * (duration_cost if duration_active else 1.0)
            cost = investment * base_cost * cost_multiplier
            if cost > blood + 1e-9:
                # Expensive drug windows must not prevent the player from using
                # the remaining blood at minimum flow.
                investment = min_investment
                cost = investment * base_cost * cost_multiplier
            if cost > blood + 1e-9:
                next_launch = math.inf
                continue

            blood -= cost
            launched += 1
            value = base_value * investment**flow_value_exponent
            if fruit_active:
                value *= 0.90
            if build.lenses and rng.random() < 1 - (1 - LENS_HIT_CHANCE) ** build.lenses:
                value *= 2.0
            machine = 1.0
            for _ in range(build.multi_dings):
                if rng.random() < DEVICE_HIT_CHANCE:
                    machine *= 2.0
            rush_add = adrenaline_rush_add if adrenaline_active else 0.0
            for _ in range(build.rush_dings):
                if rng.random() < DEVICE_HIT_CHANCE:
                    rush_add += rush_ding_add
            clover = bool(build.clovers and rng.random() < 1 - (1 - CLOVER_HIT_CHANCE) ** build.clovers)
            duration = rush_duration_base * (duration_multiplier if duration_active else 1.0)
            duration = min(rush_duration_cap, duration)
            event_id += 1
            ball = Ball(
                settle_at=time + rng.uniform(*FLIGHT_SECONDS),
                value=value,
                machine_multiplier=machine,
                rush_add=rush_add,
                clover=clover,
                rush_fruit=fruit_active,
                rush_duration=duration,
            )
            heapq.heappush(events, (ball.settle_at, event_id, ball))
            next_launch += interval
            continue

        time, _, ball = heapq.heappop(events)
        advance(time)
        draw = rng.random()
        rush_enabled = draw < HIGH_SCORE_CHANCE
        random_zone = False
        if rush_enabled:
            zone_multiplier = 10.0
        elif draw < HIGH_SCORE_CHANCE + BASE_SCORE_CHANCE:
            zone_multiplier = 1.0
        elif draw < HIGH_SCORE_CHANCE + BASE_SCORE_CHANCE + RANDOM_SCORE_CHANCE:
            zone_multiplier = 1.0
            random_zone = True
        else:
            zone_multiplier = 0.0

        in_rush = rush_remaining > 0
        value = ball.value * (rush_fruit_multiplier if in_rush and ball.rush_fruit else 1.0)
        raw = value * ball.machine_multiplier * zone_multiplier * (rush_multiplier if in_rush else 1.0)
        if random_zone:
            pending_score += raw
            if in_rush:
                pending_rush_score += raw
        else:
            awarded = math.floor(raw)
            score += awarded
            if in_rush:
                rush_score += awarded

        if rush_enabled:
            probability = min(1.0, (0.75 if ball.clover else content["base_rush_chance"]) + ball.rush_add)
            triggered = rng.random() < probability
            rush_meter += float(attrs["BALL_RUSH_GAIN"]["DefaultValue"])
            if rush_meter >= threshold:
                triggered = True
                rush_meter %= threshold
            if triggered:
                rush_triggers += 1
                rush_remaining = max(rush_remaining, ball.rush_duration)

    # The real game resolves every RandomScoreZone contribution as one shared
    # pool and one draw after the board drains.
    gamble_multiplier = rng.choice((0.0, 1.0, 2.0, 4.0))
    score += math.floor(pending_score) * gamble_multiplier
    rush_score += math.floor(pending_rush_score) * gamble_multiplier
    calibrated_score = score * OBSERVED_SCORE_SCALE
    calibrated_rush_score = rush_score * OBSERVED_SCORE_SCALE
    return {
        "score": calibrated_score,
        "rush_share": calibrated_rush_score / calibrated_score if calibrated_score else 0.0,
        "rush_triggers": float(rush_triggers),
        "rush_seconds": rush_seconds,
        "launched": float(launched),
    }


def run_trials(build: Build, trials: int, seed: int, balance: dict, content: dict) -> dict[str, float]:
    rows = [simulate_round(random.Random(seed + index * 7919), build, balance, content) for index in range(trials)]
    scores = sorted(row["score"] for row in rows)
    return {
        "mean": statistics.fmean(scores),
        "p10": percentile(scores, 0.10),
        "p50": percentile(scores, 0.50),
        "p90": percentile(scores, 0.90),
        "rush_share": statistics.fmean(row["rush_share"] for row in rows),
        "rush_triggers": statistics.fmean(row["rush_triggers"] for row in rows),
        "rush_seconds": statistics.fmean(row["rush_seconds"] for row in rows),
        "launched": statistics.fmean(row["launched"] for row in rows),
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--trials", type=int, default=3000)
    parser.add_argument("--seed", type=int, default=20260929)
    args = parser.parse_args()
    balance, content = load_balance()
    builds = [
        Build("无构筑", timed=False),
        Build("开局Multi", multi_dings=1, timed=False),
        Build("开局+肾上腺素", multi_dings=1, adrenaline=1, timed=True),
        Build("Rush起步-肾上腺", multi_dings=1, rush_dings=1, adrenaline=1, timed=True),
        Build("Rush起步-果实", multi_dings=1, rush_dings=1, rush_fruit=1, timed=True),
        Build("Rush起步-延长", multi_dings=1, rush_dings=1, rush_duration=1, timed=True),
        Build("Rush核心-开局乱吃", multi_dings=1, rush_dings=1, adrenaline=1, rush_fruit=1,
              rush_duration=1, early_drugs=True),
        Build("Rush核心", multi_dings=1, rush_dings=1, adrenaline=1, rush_fruit=1, rush_duration=1, timed=True),
        Build("Rush强化", multi_dings=2, rush_dings=2, adrenaline=2, rush_fruit=2, rush_duration=2, timed=True),
        Build("Rush成型", multi_dings=2, rush_dings=2, clovers=1, lenses=1,
              adrenaline=2, rush_fruit=2, rush_duration=2, timed=True),
        Build("Rush豪华", multi_dings=2, rush_dings=2, clovers=1, lenses=2,
              adrenaline=3, rush_fruit=3, rush_duration=3, timed=True),
        Build("Rush超载", multi_dings=3, rush_dings=3, clovers=2, lenses=2,
              adrenaline=3, rush_fruit=3, rush_duration=3, timed=True),
    ]
    print("MODEL_ASSUMPTIONS", json.dumps({
        "high_score": HIGH_SCORE_CHANCE,
        "base_score": BASE_SCORE_CHANCE,
        "random_score": RANDOM_SCORE_CHANCE,
        "drain": 1 - HIGH_SCORE_CHANCE - BASE_SCORE_CHANCE - RANDOM_SCORE_CHANCE,
        "device_hit": DEVICE_HIT_CHANCE,
        "clover_hit": CLOVER_HIT_CHANCE,
        "lens_hit": LENS_HIT_CHANCE,
        "observed_score_scale": OBSERVED_SCORE_SCALE,
    }, ensure_ascii=False))
    results = {}
    for build in builds:
        result = run_trials(build, args.trials, args.seed, balance, content)
        results[build.name] = result
        print(build.name, json.dumps({key: round(value, 3) for key, value in result.items()}, ensure_ascii=False))

    stages = balance["Stages"]
    intended = ["开局+肾上腺素", "Rush起步-延长", "Rush强化", "Rush成型", "Rush豪华", "Rush超载"]
    print("MILESTONE_PRESSURE")
    expected_total = 0.0
    pressures = []
    for index, stage in enumerate(stages):
        profile = intended[min(index, len(intended) - 1)]
        median = results[profile]["p50"]
        required = max(0.0, stage["TargetScore"] - expected_total)
        expected_total += median
        pressure = required / median if median else math.inf
        pressures.append(pressure)
        print(json.dumps({
            "stage": stage["Id"],
            "target": stage["TargetScore"],
            "required_from_expected_start": round(required, 1),
            "profile": profile,
            "median": round(median, 1),
            "pressure": round(pressure, 3) if median else None,
            "expected_margin": round(expected_total - stage["TargetScore"], 1),
        }, ensure_ascii=False))

    raw_content = content["raw"]
    rush_devices = [row for row in raw_content["Devices"] if row["Kind"] == 5 or row["RushChanceAdd"] > 0]
    rush_drugs = [row for row in raw_content["Drugs"] if row["Kind"] in (1, 2, 3)]
    first_shop_budget = balance["Campaign"]["StartingCoins"] + stages[0]["RewardCoins"]
    gates = {
        "内容目录完整且ID唯一": len(raw_content["Attributes"]) == 34 and len(raw_content["Devices"]) == 15 and
            len(raw_content["Drugs"]) == 12 and len({row["Id"] for row in raw_content["Devices"]}) == 15 and
            len({row["Id"] for row in raw_content["Drugs"]}) == 12,
        "首轮奖励可买保底核心组合": max(row["Price"] for row in rush_devices) +
            max(row["Price"] for row in rush_drugs) <= first_shop_budget,
        "延长药剂不会立刻撞时长上限": float(content["attributes"]["RUSH_DURATION"]["DefaultValue"]) *
            effect_multiplier(content["drugs"][3], 22) <= float(content["attributes"]["RUSH_DURATION"]["MaxValue"]),
        "开局中等难度": results["开局Multi"]["p10"] < stages[0]["TargetScore"] < results["开局Multi"]["p50"],
        "看准时机提高均值": results["Rush核心"]["mean"] >= results["Rush核心-开局乱吃"]["mean"] * 1.08,
        "看准时机提高爆发上限": results["Rush核心"]["p90"] >= results["Rush核心-开局乱吃"]["p90"] * 1.15,
        "核心构筑明显增益": results["Rush核心"]["p50"] >= results["开局Multi"]["p50"] * 1.60,
        "成型构筑产生爽感": results["Rush成型"]["p50"] >= results["开局Multi"]["p50"] * 4.0,
        "构筑期形成难度峰值": pressures[1] > pressures[0] and pressures[2] >= pressures[1] * 0.95,
        "成型后压力连续下降": pressures[3] < pressures[2] and pressures[4] < pressures[3] and pressures[5] < pressures[4],
    }
    failed = [name for name, passed in gates.items() if not passed]
    print("BALANCE_GATES", json.dumps(gates, ensure_ascii=False))
    if failed:
        raise SystemExit("FAILED: " + ", ".join(failed))


if __name__ == "__main__":
    main()
