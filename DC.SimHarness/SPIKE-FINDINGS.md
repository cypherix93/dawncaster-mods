# M0 Spike Findings — headless combat sim

**Date:** 2026-07-21
**Probe:** `DC.SimHarness/probe/` (net472 console, references real `Assembly-CSharp.dll` +
`UnityEngine*.dll`, resolves the dependency web from the game's `Managed/` folder at
runtime via an `AssemblyResolve` hook).

## Verdict: Strategy 1 is NOT viable. The real engine is reachable, but only via
## Strategy 2 (replacement UnityEngine shim) **on a modern-.NET host** — a bigger
## build than the spec assumed. **Needs a user decision before proceeding.**

## Probe results (verbatim)

| Check | Result |
|---|---|
| Load `Assembly-CSharp` + reflect `Card` | **PASS** — assembly binds; dependency web resolves from `Managed/` |
| `FormatterServices.GetUninitializedObject(Card)` + set field | **PASS** — game data objects construct as pure managed objects headless |
| `ScriptableObject.CreateInstance<Card>()` | **THROW** — `SecurityException: ECall methods must be packaged into a system module` |
| `UnityEngine.Debug.Log(...)` | **THROW** — same `SecurityException` (Unity native ECall, no player to bind it) |
| `DamageCalculations.CalculateDamage(6, <uninit cc>, …)` | **THROW** — `MissingMethodException: System.String[] System.String.Split(Char, System.StringSplitOptions)` |
| `RunClassConstructor(SpellEffects)` (static init) | **PASS** — no Addressables/Steamworks detonation at type load |

## What this means

Two **independent** blockers to running the real game code as-is:

1. **Unity native ECalls are unbound without a Unity player.** `ScriptableObject.CreateInstance`,
   `Debug.Log`, and every other Unity internal call throw `SecurityException` in a bare
   process. `Debug.Log` is pervasive throughout `SpellEffects` (the decompile shows 361
   UI/animation refs), so the real interpreter cannot execute against the real
   `UnityEngine.dll`. → **A replacement (shadow) `UnityEngine` assembly with managed no-op
   bodies is REQUIRED, not an optional fallback.**

2. **BCL/runtime mismatch.** The game (Unity Mono, ~netstandard2.1 BCL) uses framework APIs
   absent from .NET Framework 4.7.2's `mscorlib` — e.g. `String.Split(char, StringSplitOptions)`.
   So even a *pure-math* method (`DamageCalculations.CalculateDamage`) fails on net472. →
   **The host must run on a runtime whose BCL matches (modern .NET has these overloads), not
   net472.** This also means the whole `DC.SimHarness` should target modern .NET, not the
   net472 the plan assumed (net472 was chosen to mirror the DawnKit *plugin*, which runs
   inside Unity — a different context).

## What still works (the good news)

- The assembly **binds and its dependency graph resolves** headless.
- **`GetUninitializedObject` constructs game data objects** (Card/Battle/StatusEffect) as
  plain managed objects — the content-loader premise holds.
- **`SpellEffects` static init is clean** — no hidden Addressables/Steamworks dependency at
  type load.
- Conclusion: the game's data model and logic ARE reachable headless. The obstacles are the
  Unity **ECall surface** (fix: shadow assembly) and the **BCL host** (fix: modern .NET) —
  both solvable, but together they are a real sub-project, not a few stubs.

## Impact on the plan

- Task 0.1 target framework **net472 → a modern .NET** (e.g. net8/net9).
- Strategy 2 (replacement UnityEngine shim) moves from "fallback" to "the required core of
  M0." Its cost is dominated by **how much of the UnityEngine surface the combat path
  references** — this is now the key unknown to size before committing.

## Recommended next step (cheap, before committing to a big build)

Enumerate the UnityEngine members that `Assembly-CSharp`'s combat classes
(`SpellEffects`, `CombatHandler`, `MonsterHandler`, `DamageCalculations`, `ConditionChecker`,
`CardHandler`, `Card`, `StatusEffect`, `Battle`) actually reference. That count bounds the
shadow-assembly effort and tells us whether Strategy 2 is a day or a week — and whether
UniTask/DOTween/TextMeshPro ECalls also sit on the combat path (which would enlarge it).

---

## Shim sizing (Mono.Cecil IL walk of the combat classes) — `DC.SimHarness/sizer/`

Seed set (combat + card-math classes): SpellEffects, CombatHandler, MonsterHandler,
DamageCalculations, ConditionChecker, CardHandler, PlayerHandler, PlayerData, DeckHandler,
EventHandler, SpellManager, Card, CardContainer, StatusEffect, Status, Battle, CardEffect,
Condition, NumberParser, Enchantment, LastingEffect(Container), GameTextTranslator
(1117 methods with bodies).

**DIRECT (combat classes only) — the surface v1 truly needs:**

| Assembly | types | members |
|---|---|---|
| UnityEngine.CoreModule | 20 | 69 |
| UnityEngine.UI | 3 | 3 |
| Unity.TextMeshPro | 1 | 2 |
| Unity.Localization | 2 | 2 |

≈ **74 members / 26 types** — small.

**TRANSITIVE (full reachable closure, 4367 methods) — pessimistic upper bound:**
CoreModule 46t/247m, UI 18t/62m, then a long presentation/platform tail:
Localization, Steamworks, UIEffect, ParticleSystem, Audio, InputSystem, TextMeshPro,
UIModule, SQLite, UniTask, ResourceManager, JSON, Ink, Postprocessing, Addressables.
This tail is all UI / feedback / achievements / save / input — reachable from combat via
branches we will not execute, but the shim types must still *exist* so JIT can resolve them.

### What this means for effort

- **Auto-generate the shim, don't hand-write it.** Read each real dependency DLL's public
  metadata with Cecil and emit an assembly with identical public signatures but
  `return default` bodies. This covers the entire surface of an assembly at once, so the
  247-vs-69 gap stops mattering — mechanical, ~1–2 days, not a week.
- **Tiny "must be REAL" carve-out** (combat reads these values back, so no-op is wrong):
  `Mathf.RoundToInt/Clamp/Min/Max` and `Random.Range` — a handful of one-liners; `Random.Range`
  routes to the seeded RNG. Everything else (Debug/UI/TMP/audio/particles/Steamworks/SQLite/
  input/localization/LeanTween) is safely no-op.
- **Coroutine model confirmed:** the combat classes use **classic `IEnumerator` +
  `StartCoroutine` + `WaitForSeconds`, zero UniTask/async** — the synchronous coroutine pump
  design is correct. (UniTask appears only in the transitive periphery.)
- **Host:** modern .NET (net8) — the sizer runs on it fine; fixes the net472 BCL gap.

### Verdict: **GO.** Strategy 2 is bounded and largely auto-generatable, the pump model
holds, and the real-math carve-out is trivial. Residual risk is now low and concrete:
stub-default correctness where combat reads a Unity value back (mitigated by the Mathf/Random
carve-out) and confirming no other value-returning native call sits on the executed path
(surfaces incrementally as fights run).

### Revised M0/M1 shape
1. Modern-.NET (net8) host; drop net472.
2. Cecil-based **shim generator**: real Unity/dep DLL → no-op stub assembly; hand-override the
   Mathf/Random carve-out with real bodies. Emit stubs for CoreModule + UI + the transitive
   tail assemblies.
3. Bind Assembly-CSharp against the generated stubs; construct content via
   `GetUninitializedObject`; drive the real `CombatHandler` phase machine through the pump.
4. Proceed into M1 (content loader → single combat) as originally planned.

---

## M0 GATE: GREEN ✅ (2026-07-21) — shim approach proven end-to-end

`DC.SimHarness/src/ShimGen/` rewrites the real Unity DLLs (neutralized 7970 native
methods across 28 assemblies; CoreModule 3479). `DC.SimHarness/probe2/` binds the real
`Assembly-CSharp` against the shimmed UnityEngine on **net8** and passes every check:

| Check | net472 (probe 1) | net8 + shim (probe 2) |
|---|---|---|
| Load Assembly-CSharp + reflect Card | PASS | **PASS** |
| GetUninitializedObject(Card) | PASS | **PASS** |
| `Debug.Log` | THROW (SecurityException) | **PASS (no-op)** |
| `ScriptableObject.CreateInstance` | THROW | **PASS (tolerated)** |
| `Mathf.RoundToInt/Clamp/Min` real math | n/a | **PASS (3 / 5 / 3)** |
| `DamageCalculations.CalculateDamage(6,…)` | THROW (BCL gap) | **PASS → returned 6** |
| `SpellEffects` static init | PASS | **PASS** |

The real game's damage calculation runs headless and returns a correct value. Bet confirmed;
proceeding to M1 (content loader → single combat) on this foundation.

---

## Combat-driver progress (2026-07-24) — coroutine pump wired; damage-context grind remains

**Keystone landed: the coroutine pump lives INSIDE the shim.** Combat resolution is
coroutine-driven (`DamageRoutineIsRunning`, ability sequences). The game starts these
via `MonoBehaviour.StartCoroutine` *internally*, so an external pump can't reach them.
Fix: `src/SimRuntime/` (`SimCoroutine.Run`, netstandard2.0) is a synchronous recursive
drain, and `ShimGen` rewrites `MonoBehaviour.StartCoroutine(IEnumerator)` to call it (with
`StopCoroutine*` no-op'd). Result: real internal combat coroutines now run to completion
headless with no frame waits. `StartCoroutine` no longer throws; `RunEffect` executes.

**Fourth carve-out confirmed:** `MonoBehaviour.StartCoroutine → SimCoroutine.Run`
(plus a `MaxSteps` cap so a never-satisfied `WaitWhile` becomes a detectable
non-termination rather than a hang; the pump also records `LastError`).

**Where it stops:** `RunEffect("damage:6", cc)` runs without throwing but monster
`E_tempHealth` is unchanged. Root cause identified: the damage path reaches
`MonsterHandler.ChangeMonsterHealth`, which dereferences three still-null singletons —
`PlayerHandler.thePlayerData.trackingData`, `StatManager.Instance`, and the static
`combatUI` — and the resulting NRE is swallowed by the coroutine drain (now recorded in
`SimCoroutine.LastError`). This is ordinary incremental wiring, not a design problem.

### Exact next steps for the combat driver
1. Wire `thePlayerData.trackingData` (a `PlayerTrackingData`), `StatManager.Instance`, and a
   tolerable `combatUI`/`playerUI` (the `*UIHandler` methods deref their own null managed
   fields — likely need selective no-op of the `*UIHandler` classes, OR guard by only
   calling state sub-methods). Re-run the vertical slice until monster HP drops on `damage:6`.
2. Monster setup via `MonsterHandler.ResetMonster` + `GenerateMonsterDeck` (state only; skip
   the UI/animation tail of `LoadMonster`).
3. Own thin turn orchestrator (player play-phase via `ExecuteEffect`; enemy via monster-deck
   effects; call real `EventHandler.FindTriggeredEvents` at phase boundaries for status ticks)
   → `FightResult`. Autoplay = fixed greedy policy.
4. `Random.Range` → route to the seeded RNG (currently returns default).

### Verified state at this checkpoint
- M0 (shim + net8) — proven. Content loader — 6/6 tests green.
- Coroutine pump — wired and functioning (RunEffect executes headless).
- Combat driver — foundation in place; damage application blocked only on the three
  singleton wirings above.
