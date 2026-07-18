"""Empirical balance envelopes calibrated against the shipped pool.

Method (sim-harness brief):
  1. Parse all extracted cards; keep the player-legal, fully-simulable subset.
  2. Score each with engine.score_card (delta vs a fixed baseline deck across
     3 enemy profiles x 3 seeds, 8-turn scoring window).
  3. Per rarity tier, the power-score distribution gives the envelope:
     P25-P75 = "on curve", outside P5/P95 = flagged.
  4. Self-check: 20 seeded-random shipped commons must mostly land inside P5-P95;
     if they don't, the sim or the envelopes are wrong — do not trust pack verdicts.

Envelope cache: tools/sim/out/envelopes.json (delete or --rebuild to recompute).
"""

from __future__ import annotations

import json
import random
import sys
from dataclasses import dataclass
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import gamedata as gd                      # noqa: E402
from sim import dsl, engine                # noqa: E402

OUT_DIR = Path(__file__).resolve().parent / "out"
CACHE = OUT_DIR / "envelopes.json"

ENVELOPE_RARITIES = ["Common", "Uncommon", "Rare", "Legendary"]
# Epic/Mythic/Ultra are outside the reward ladder; pack cards with those rarities
# are compared against the Legendary envelope (noted in the report).
RARITY_FALLBACK = {"Epic": "Legendary", "Mythic": "Legendary", "Ultra": "Legendary"}

SELF_CHECK_SEED = 20260718
SELF_CHECK_N = 20

# Categories with play-routing the engine actually models (played from hand as an
# action). Enchantment/Form/Performance/etc. route differently (Part IV).
SIMPLE_CATEGORIES = {"Action", "Item", "BasicAttack", "Conjuration"}


def percentile(sorted_vals: list[float], p: float) -> float:
    """Linear-interpolation percentile (deterministic, no numpy)."""
    if not sorted_vals:
        raise ValueError("empty distribution")
    if len(sorted_vals) == 1:
        return sorted_vals[0]
    k = (len(sorted_vals) - 1) * p / 100.0
    lo = int(k)
    hi = min(lo + 1, len(sorted_vals) - 1)
    frac = k - lo
    return sorted_vals[lo] * (1 - frac) + sorted_vals[hi] * frac


@dataclass
class Envelope:
    rarity: str
    n: int
    p5: float
    p25: float
    p50: float
    p75: float
    p95: float

    def position(self, power: float) -> str:
        if power > self.p95:
            return "OVER"
        if power < self.p5:
            return "UNDER"
        return "ON-CURVE"

    def band(self, power: float) -> str:
        for label, hi in (("<P5", self.p5), ("P5-P25", self.p25), ("P25-P50", self.p50),
                          ("P50-P75", self.p75), ("P75-P95", self.p95)):
            if power <= hi:
                return label
        return ">P95"


def player_legal(d: dict) -> bool:
    """AssetManager.ProcessCard filter + categories the engine can route."""
    return (d.get("cardexpansion") not in (0, 2)
            and d.get("cardRarity") in (0, 1, 2, 3)
            and d.get("cardSuffix") != 8
            and gd.CARD_CATEGORIES[d.get("cardCategory", 0)] in SIMPLE_CATEGORIES)


def reference_corpus() -> tuple[list[dsl.CardModel], dict]:
    """(fully-simulable player-legal cards, coverage stats)."""
    pool = gd.pool_cards()
    legal = [d for d in pool if player_legal(d)]
    models = [dsl.card_from_pool_json(d) for d in legal]
    simulable = [m for m in models if m.fully_simulable() and "Unplayable" not in m.keywords]
    stats = {
        "total_extracted": len(pool),
        "player_legal_simple_category": len(legal),
        "fully_simulable": len(simulable),
        "coverage_pct": round(100.0 * len(simulable) / len(legal), 1) if legal else 0.0,
    }
    return simulable, stats


def build_envelopes(rebuild: bool = False) -> dict:
    """Compute (or load cached) envelopes + per-card corpus scores."""
    if CACHE.is_file() and not rebuild:
        return json.loads(CACHE.read_text(encoding="utf-8"))

    corpus, stats = reference_corpus()
    scores: dict[str, dict] = {}
    for m in corpus:
        sc = engine.score_card(m)
        scores[f"{m.name}#{m.card_id}"] = {
            "name": m.name, "rarity": m.rarity, "cost": m.cost,
            "power": round(sc.power, 2), "dmg": round(sc.dmg_delta, 2),
            "sustain": round(sc.sustain_delta, 2), "cards": round(sc.cards_delta, 2),
            "scaling": round(sc.scaling_ratio, 3),
        }

    envelopes = {}
    for rarity in ENVELOPE_RARITIES:
        vals = sorted(s["power"] for s in scores.values() if s["rarity"] == rarity)
        if len(vals) < 10:
            continue
        envelopes[rarity] = {
            "n": len(vals),
            "p5": percentile(vals, 5), "p25": percentile(vals, 25),
            "p50": percentile(vals, 50), "p75": percentile(vals, 75),
            "p95": percentile(vals, 95),
        }

    data = {"stats": stats, "envelopes": envelopes, "corpus_scores": scores,
            "self_check": self_check(scores, envelopes)}
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    CACHE.write_text(json.dumps(data, indent=1), encoding="utf-8")
    return data


def envelope_for(rarity: str, data: dict) -> Envelope | None:
    """Envelope for a rarity; if that tier has too few simulable shipped cards
    (n<10), fall back down the ladder (noted via the returned Envelope.rarity)."""
    rarity = RARITY_FALLBACK.get(rarity, rarity)
    ladder = ["Legendary", "Rare", "Uncommon", "Common"]
    start = ladder.index(rarity) if rarity in ladder else 0
    for tier in ladder[start:]:
        e = data["envelopes"].get(tier)
        if e is not None:
            return Envelope(tier, e["n"], e["p5"], e["p25"], e["p50"], e["p75"], e["p95"])
    return None


def self_check(scores: dict, envelopes: dict) -> dict:
    """Harness sanity gate: 20 seeded-random shipped commons scored against the
    common envelope. Most must land ON-CURVE (P5-P95) or the harness is wrong."""
    commons = sorted(k for k, s in scores.items() if s["rarity"] == "Common")
    rng = random.Random(SELF_CHECK_SEED)
    sample = rng.sample(commons, min(SELF_CHECK_N, len(commons)))
    env = envelopes.get("Common")
    results = []
    on_curve = 0
    for key in sample:
        s = scores[key]
        verdict = "ON-CURVE"
        if env is not None:
            if s["power"] > env["p95"]:
                verdict = "OVER"
            elif s["power"] < env["p5"]:
                verdict = "UNDER"
        if verdict == "ON-CURVE":
            on_curve += 1
        results.append({"name": s["name"], "power": s["power"], "verdict": verdict})
    n = len(sample)
    return {
        "sample_size": n,
        "on_curve": on_curve,
        "pass": bool(n and on_curve / n >= 0.8),
        "results": results,
    }
