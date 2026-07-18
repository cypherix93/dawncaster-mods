# Sim harness (validation gate 3)

Balance evaluation for card packs BEFORE in-game testing. Two commands:

```
python tools/validate_pack.py packs/<Pack>/pack.json [--all] [--strict]   # gate 1
python tools/sim/report.py    packs/<Pack>/pack.json [--all]              # gate 3
python tools/sim/report.py    --self-check [--rebuild-envelopes]          # calibration
python -m pytest tools/tests                                              # 68 tests
```

`report.py` writes `packs/<Pack>/BALANCE-REPORT.md` and exits non-zero if any card
is OVER or DEGENERATE-RISK. Envelopes are cached in `tools/sim/out/envelopes.json`
(`--rebuild-envelopes` after changing the engine or the extraction).

## How it works

1. **Corpus** (`envelopes.py`): all 2,525 extracted cards are normalized
   (`dsl.card_from_pool_json` — enum ints decoded, `referenceStatus` PPtrs resolved
   via `data-index.json` path_ids). Player-legal cards in action-routed categories
   (Action/Item/BasicAttack/Conjuration) whose behavior is 100% inside the
   simulable subset become the reference corpus.
2. **Metric** (`engine.py`): each card is added (1 copy) to a fixed baseline deck —
   6x Strike (`damage:4`, cost 1: the ~4 dmg/energy common rate from
   CARD-PACK-SPEC §4) + 4x Defend (`bless:4` Armor, cost 1) — and run through a
   scripted 16-turn combat against 3 enemy profiles derived from the extracted
   Battle/monster-card stats (fast 42 HP / 3x2 hits, standard 65 HP / 2x3, tank
   150 HP / 1x7; Battle HP quartiles p25/p50/p75+, monster `damage:` median 3,
   p90 7). The **power score** = Δ(damage dealt) + Δ(player HP kept) +
   1.0 x Δ(net cards gained) vs the SAME deck without the card, averaged over
   3 profiles x 5 seeds, scored on the first 8 turns. Because the card competes
   for the same energy as the strikes it displaces, **energy opportunity cost is
   priced in automatically** — power measures value above replacement.
3. **Envelopes**: per rarity, the corpus power distribution gives P5/P25/P50/P75/P95.
   Inside P5–P95 = ON-CURVE (band shown); above P95 = OVER; below P5 = UNDER.
4. **Degeneracy**: static sniff (replay/copy/conjure commands without
   OneUse/Conjured/charges bounds; triggers that re-fire themselves) plus a dynamic
   probe (late-window vs early-window marginal damage ratio >= 1.5 => still scaling
   at the turn cap).

Determinism: fixed seed list (`engine.SEEDS`), `random.Random` only, no wall-clock
or dict-order dependence. Two runs produce identical reports.

## Calibration (2026-07-18 extraction)

- Reference corpus: **91 fully-simulable shipped cards** out of 916 player-legal
  action-routed cards (9.9%; 283/916 are >=50% simulable but only fully-simulable
  cards are admitted to the envelopes — partial simulation would systematically
  underscore them). The subset is deliberately conservative: we never guess
  semantics, and most shipped cards touch enchantments, delves, or tokens/
  conditions outside the verified subset.
- Envelope sizes: Common n=50, Uncommon n=20, Rare n=13, Legendary n=6 (below the
  n>=10 floor, so Legendary pack cards are scored against the Rare envelope and the
  report marks the fallback).
- **Self-check: PASS — 17/20 seeded-random shipped commons land ON-CURVE**
  (threshold 80%; the 3 misses are 1 OVER / 2 UNDER, all within noise of the P5/P95
  edges). Rebuild + recheck with `python tools/sim/report.py --self-check`.
  If this ever FAILs, the engine or envelopes are wrong — fix before trusting any
  pack verdict.

## What is simulated

- **Commands** (semantics verified in decompiled SpellEffects.cs, cited in
  `dsl.py`): `damage` / `piercingdamage` / `directdamage` / `statusdamage` (typed,
  multi-attack, self/other), `draw`, `heal`, `inflict`/`bless` via
  `referenceStatus`, `bury`, `energy`/`energyDEX..HOLY`, `conjure` (as an inert
  token add — see limits), `addstack` (tracked counter, payload never runs),
  `adddamage`/`multiplydamage` (x2 = the crit channel, refuses stacking, cancelled
  by Weakness), `caustic`/`reaping`, `removestatus`, `discard` (random self),
  `addtempvalue`/`addpermavalue`, bare status-name commands for modeled statuses.
- **Statuses** (16, each with the asset-verified timing and a dedicated test in
  `tools/tests/test_sim_statuses.py`): Armor (fades StartPhase), Barrier
  (persistent), Resilience (-1/hit), Evasion (Melee-only dodge, fades StartPhase),
  Weakness (halves outgoing, -1 per damage inflicted, cancels crits), Vulnerable
  (+10%/stack, cap 10), Anger (+stacks, Rampage doubles), Chain (+stacks, wiped by
  non-Chain plays, fades EndTurn), Zeal (Divine bonus + StartPhase self-burn),
  Frozen (outgoing -stacks, -1 per play, melts EndTurn), Poison (ticks flat 1 on
  holder's card plays, duration stacks, eats healing 1:1), Burning (EndTurn tick,
  never decays), Bleeding (ticks on damage taken, -1), Regenerate, Doom (-1 per
  play, detonates EndTurn), Stagger.
- **Keywords**: Chain, Echo, OneUse, Piercing, Persistent, Lifedrain, Reaping,
  Heavy, Rampage. Inert-by-construction here: Valuable, Sinister, Unique,
  Reliable, Memorized, Firecast, Psionic, Grounded, Cursed, Untempered.
- **Conditions** (ConditionChecker-verified): AnyValue (resolvable expressions),
  Chance, Ambush, Frenzy, StatusOnSelf/StatusOnOpponent
  (any/blessing/affliction/named-modeled-status). `CheckAllConditions` is AND.
- **Engine truths**: energy persists across turns (cap 8), hand cap 10, stack cap
  9999, reshuffle keeps Heavy in the graveyard, life costs are self-damage and
  count for Frenzy, Persistent survives the discard phase.
- **Value tokens**: `[[lastDamage]]`, `[[tempValue]]`, `[[permaValue]]`,
  `[[damageBonus]]` (0 — cards are scored unupgraded), `[[cardsInHand]]`,
  `[[my(status)X]]`/`[[other(status)X]]` for modeled statuses; NumberParser
  left-to-right integer arithmetic and `a%b` random ranges.

Anything else marks the statement non-simulable. **We never guess semantics** —
a card is scored only on what the engine actually models, and the report shows
exactly what was skipped.

## Verdicts

| Verdict | Meaning |
|---|---|
| ON-CURVE | Power inside the shipped pool's P5–P95 for the rarity. Band column shows where; P25–P75 is the comfortable middle. |
| OVER / UNDER | Outside P95 / P5. Check `meta.powerNotes` — conditional setup cost can justify some OVER, but verify. |
| DEGENERATE-RISK | Unbounded loop pattern (static) or damage still accelerating at turn 16 (dynamic). Human review mandatory. |
| INSUFFICIENT-SIM | <80% of the card's behavior is simulable. **Not a balance judgement** — the card simply needs gate-4 in-game QA. Partial metrics are still shown. |

## Known limits (read before arguing with a verdict)

- **The sim sees a 10-card vanilla deck, not the card's archetype.** Synergy
  payoffs (bury enablers, chain webs, enchantment engines, cross-card combos)
  score near zero here by construction. UNDER on a payoff card usually means
  "needs its deck", not "weak".
- **Conditions outside the supported six are not evaluated** — the affected
  statements are simply not scored (and the card is usually INSUFFICIENT-SIM).
- **Conjured tokens are inert**: `conjure` counts as card-advantage only; the
  token's own text is never executed. Conjure-engine value is underestimated.
- **Enchantment payloads never run** (no `enchant` support), so Enchantment-
  category cards and enchant-rider Actions are out of scope for scoring.
- **The enemy is a scripted profile**: no AI reactions, no enemy statuses/
  cleanses, no Deadly/interrupt dance, no multi-stage battles; it also never
  dies (damage accumulates), which makes never-decaying DoTs (Burning) look
  stronger late — one reason the scaling probe flags heavy Burning stackers;
  treat those DEGENERATE-RISK flags as "verify by hand".
- **Energy is untyped** (3/turn): color-fixing friction and HOLY scarcity are
  invisible; Life costs are modeled as self-damage only.
- **No fatigue, no rewards/economy, no talents/affixes/companions, no tide**
  (Ebb/Flow cards keep their manifest side).
- Upgrades are ignored (`damageBonus` = 0): cards are scored at base power.

## Files

- `dsl.py` — card model, codeLine parser, value grammar, simulability analysis,
  static degeneracy sniff.
- `engine.py` — fighters, verified status timing, damage pipeline, turn loop,
  enemy profiles, `score_card`.
- `envelopes.py` — corpus selection, percentile envelopes, calibration self-check.
- `report.py` — CLI + BALANCE-REPORT.md renderer.
- `fixtures/demo-pack.json` — 3-card fixture (on-curve / overpowered / degenerate);
  `report.py` on it yields ON-CURVE / OVER / DEGENERATE-RISK respectively
  (`fixtures/DEMO-BALANCE-REPORT.md`).
- Tests live in `tools/tests/` (`test_sim_statuses.py` has one test per status
  timing rule; `test_dsl.py`, `test_envelopes.py`, `test_validate_pack.py`).
