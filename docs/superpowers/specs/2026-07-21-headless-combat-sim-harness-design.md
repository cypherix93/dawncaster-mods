# Headless Combat Sim Harness — Design

**Date:** 2026-07-21
**Status:** Approved design, pre-implementation
**Author:** cypherix93 (with Claude)

## Problem

We author modded card packs for Dawncaster and need to balance/optimize them by
running **many fast simulations** of the game's card mechanics. The existing
`tools/sim` Python harness is a statistical *design screen*: single-combat,
card-level power scoring against **3 hand-approximated enemy profiles**, with
only ~9.9% of shipped cards fully simulable (`tools/sim/README.md`). It cannot
answer the two questions we actually care about:

1. **Win-rate / survival curves** for a deck against a **real** monster deck.
2. **Degenerate-combo detection** inside a pack (infinite loops, runaway synergies).

Both require **exact** game semantics. An approximation misses exactly the
combos we want to catch, because a combo usually rides on a command the
approximation never modeled.

## Goal

A harness that runs Dawncaster **combat** as pure card-math — the real game
logic, minus UI and animation delays — fast enough to run many thousands of
fights, to test and optimize modded packs.

**v1 scope: combat only.** Deck vs. a real `Battle`/monster deck, full turn
loop, statuses/conditions/keywords/combos, producing win-rate + survival +
combo-detection outputs. No map/rewards/economy/events yet (those are later
iterations and are explicitly out of scope for v1).

## Key insight that makes this feasible

Dawncaster is **Unity Mono** (not IL2CPP), so `Assembly-CSharp.dll` is ordinary
managed C# and *is* the real combat logic. The decompile (`decompiled/`, verified
2026-07-17) shows:

- Combat **state** is `static` and separable: `SpellEffects.CombatStat`,
  `SpellEffects.TurnStat`, `PlayerHandler.thePlayerData.combatData`
  (`decompiled/CombatHandler.cs:21,50,82`).
- The real **math** is UI-free and directly callable:
  `DamageCalculations.CalculateDamage(...)` — `public static int`, 363 lines, 0
  coroutines (`decompiled/DamageCalculations.cs:20`); `ConditionChecker` — 0 UI
  refs, 0 coroutines (`decompiled/ConditionChecker.cs`); `NumberParser`.
- The UI/animation coupling is concentrated in the **orchestrator**
  (`SpellEffects.cs` — 361 UI/animation refs, 15 coroutines;
  `CombatHandler.cs` — a `MonoBehaviour` phase machine driven by
  `StartCoroutine`/`WaitForSeconds`).
- **Both sides of combat are card decks running the same DSL.** The enemy has no
  separate AI brain: `MonsterHandler` draws `Monster`-category cards from a deck
  and plays their `codeLine` effects through the same `SpellEffects`
  (`decompiled/MonsterHandler.cs:403 StartMonsterTurn`, `:457 PlayAbilities`).
- Content normally loads via `Resources.LoadAll` into `AssetManager` static lists
  (`allCards`, `allBattles`, `allStatusEffects`, …), but the repo **already
  extracts all of that** to `tools/out/data/*.json` via `tools/extract_data.py`
  (2525 cards, 49 statuses, Battles), and `tools/sim/dsl.py` already reconstructs
  card models from it — so we can bypass Addressables entirely.

Therefore we can run the **real** combat logic headless by stubbing the
presentation layer, without re-deriving any semantics.

## Approach (chosen: B — Headless real engine)

Alternatives considered:

- **A — Extend the Python port.** Rejected: structurally cannot deliver combo
  detection (fails on any unmodeled command) or real-monster win-rates, and every
  game patch is a hand-re-derivation treadmill.
- **C — Hybrid** (our orchestrator + real leaf-math classes, hand-ported
  dispatch). Rejected as v1: still hand-ports the 565-command dispatch → same
  combo-detection gap as A for anything unwired, for most of B's effort.
- **B — Headless real engine.** Chosen: exact fidelity (all 565 commands, real
  monster decks, real combos), and cheap to maintain across patches (re-copy DLLs,
  fix shim breaks — never re-derive semantics).

### The core bet: a "shadow UnityEngine"

We do **not** run inside a live Unity player. We compile our own stub
`UnityEngine.dll` exposing the same type shapes the game references
(`MonoBehaviour`, `ScriptableObject`, `Debug`, `GameObject`, `Coroutine`,
`LeanTween`, …) with **no-op managed bodies**, and bind the real
`Assembly-CSharp.dll` against *that*. The game's combat code then links and runs
as ordinary managed C#: the 361 UI refs in `SpellEffects` become no-op stub
calls; the real damage/status/condition math runs untouched.

Supporting pieces that make this bounded:

- **Coroutine pump:** a trivial driver that runs any `IEnumerator` to completion,
  treating every `WaitForSeconds`/`yield` as "advance immediately" — literally
  "no UI delays."
- **Asset bypass:** reconstruct real `Card`/`Battle`/`StatusEffect` instances from
  `tools/out/data/*.json` (+ the target `pack.json`) and populate the
  `AssetManager` static lists + caches directly, skipping `Resources.LoadAll` and
  Addressables.
- **Incremental shim growth:** run combat, hit a missing/failing Unity member,
  stub it, repeat — guided by the decompile. The surface is only what combat
  actually touches.

## Architecture

New C# project `DC.SimHarness/` (a .NET/Mono toolchain alongside the Python
`tools/`). Components:

1. **Shadow-Unity shim** — stub `UnityEngine.dll` (type shapes + no-op bodies),
   the synchronous coroutine pump, and a seeded RNG bound into the game's `RNG`
   namespace.
2. **Content loader** — reconstructs `Card`/`Battle`/`StatusEffect` instances from
   `tools/out/data/*.json` and a `pack.json`; populates `AssetManager.allCards` /
   `allBattles` / `allStatusEffects` and rebuilds the lookup caches. Ports the
   reconstruction semantics already in `tools/sim/dsl.py`.
3. **Combat driver** — sets up a player (deck, HP, energy, class stats) and a
   `Battle`, then drives the real `CombatHandler` phase machine
   (`StartCombat` → `GoToPhase(...)`) through the pump to victory/defeat.
4. **Autoplay policy** *(new — not in the game)* — the game waits for human
   card-choice; headless needs a policy to decide plays each turn. v1 = a
   configurable **greedy heuristic** (play affordable value, respect play
   conditions, then end turn). Pluggable interface so smarter policies can swap in
   later.
5. **Batch runner + metrics** — runs N fights over seeds × decks × enemy sets;
   aggregates **win-rate, avg turns-to-kill, HP kept, damage curves**, and flags
   **degenerate combos**. Emits metrics JSON + a `BALANCE-REPORT`-style doc; wired
   into `dmk` as a new gate.

### Data flow

```
tools/out/data/*.json  ─┐
target pack.json       ─┤→ Content loader → AssetManager static lists (+caches)
                                                   │
player setup (deck/HP/energy/stats) + Battle ──────┤
                                                   ▼
                        Combat driver → real CombatHandler phase machine
                                       (via coroutine pump; autoplay policy
                                        chooses plays) → real SpellEffects /
                                        DamageCalculations / ConditionChecker /
                                        MonsterHandler mutate static combat state
                                                   │
                                                   ▼
                        Fight result (transcript + outcome)
                                                   │
                        Batch runner → aggregate metrics + combo flags
                                                   │
                                                   ▼
                        metrics JSON + BALANCE-REPORT-style doc  (dmk gate)
```

### Degenerate-combo detection

A fight is flagged degenerate when any of: the turn/ability loop fails to
terminate; an internal iteration cap is hit (cf. `MonsterHandler.cs:472`'s own
`iterationCount < 20` guard); a tracked stack/counter grows without bound; or
marginal damage is still accelerating at the turn cap (the dynamic scaling probe
the Python sim already uses, but now against real semantics).

## Throughput & determinism

- Combat state is `static` (`SpellEffects.CombatStat`,
  `PlayerHandler.thePlayerData`), so **parallel fights cannot share one process**.
  The game already resets all statics between combats
  (`CombatHandler.ResetCombatMetrics` → `SpellEffects.CombatStat = new CombatStat()`,
  `decompiled/CombatHandler.cs:156`), so we **run fights sequentially per process
  and parallelize across worker processes**. "Many many fast" comes from
  worker-level parallelism × per-fight CPU speed (no rendering, no waits).
- **Determinism:** seeded RNG; same seed → identical fight transcript. Enforced by
  test.

## Risks & how we handle them

1. **Shadow-Unity feasibility (load-bearing).** The stub surface is the main
   unknown; a static initializer might demand engine state, or a member might
   resist stubbing. **Mitigation: M0 is a narrow go/no-go spike** — one `damage:`
   card vs. one monster resolving headless through the real `SpellEffects` —
   *before* building the full harness. If it fails, fall back to approach C.
2. **RNG parity.** Exact fight-for-fight reproduction of an in-game battle needs
   replicating the game's RNG stream. For win-rate/survival **curves this is
   unnecessary** — any fair seeded RNG over many runs gives the correct
   *distribution*. We validate **distributionally** (aggregate win-rate vs.
   in-game) and only chase exact-transcript parity if a specific golden test needs
   it.
3. **Autoplay strength.** Win-rate depends on the policy. For balance work,
   *consistency* matters more than optimality; the policy is fixed + documented so
   pack-vs-pack comparisons stay apples-to-apples.
4. **Patch drift.** On a game update: re-run `extract_data.py`, re-copy DLLs, fix
   any shim breaks. No semantics are re-derived — that is the whole point of B
   over A/C.

## Milestones

- **M0 — Feasibility spike (go/no-go gate).** One `damage:` card vs. one monster
  resolves end-to-end through the real `SpellEffects` headless. Proves or kills
  the shadow-Unity bet.
- **M1 — Full single combat.** Real phase machine + autoplay policy + content
  loader; one deck vs. one `Battle` runs to victory/defeat.
- **M2 — Content coverage.** All statuses/keywords/conditions load; cross-check
  power ordering against the Python sim on its 91-card simulable subset as a
  sanity check; expand to the full pool.
- **M3 — Batch runner + metrics + combo detection + `dmk` integration.**
- **M4 — Validation.** Golden fights: capture a few real in-game outcomes and
  assert a distributional match.

## Testing strategy

- **Determinism:** same seed → byte-identical fight transcript.
- **Shim unit tests:** coroutine pump runs an `IEnumerator` to completion; RNG
  determinism.
- **Cross-check:** agreement (at least in ordering) with `tools/sim` on the
  simulable subset.
- **Golden-fight oracle (M4):** capture real in-game combat outcomes (via BepInEx
  `LogOutput.log` and/or manual play) for a handful of fixed
  (deck, enemy, seed) and assert the headless engine matches — distributionally by
  default, exact-transcript only where RNG parity is established.

## Relationship to existing tooling

- `tools/sim` (Python) **stays** as the fast pre-screen / design gate. No forced
  retirement in v1.
- The new harness is the **high-fidelity gate**, exposed through `dmk` (e.g. a new
  subcommand) and emitting a report in the established `BALANCE-REPORT.md` shape.

## Out of scope for v1

Rewards/economy, XP, map/`AreaHandler`, Ink events/`DialogueActionHandler`,
talents/affixes/companions, upgrades economy, multi-stage run loops. Each is a
later iteration that grows the shim surface deliberately.
