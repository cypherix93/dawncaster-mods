# DawnKit — engine + data client + dev sandbox (M1)

BepInEx plugins for Dawncaster (Steam, Unity 2022.3.62f2, Mono). **Public API
reference: [`../API.md`](../API.md)**; tutorials: `../examples/MyFirstMod`
(C#) and `../examples/ExamplePack` (data-only). The former
`Dawncaster.Sandbox` 0.4.0 monolith is split per `../SPEC.md` §3 into:

| Assembly | Plugin GUID | Role |
|---|---|---|
| `DawnKit.dll` | `dcmods.dawnkit` | **Engine.** Owns ALL Harmony patches (SPEC §6, 16 targets + 4 private members, each logged "Target found: X" at boot). Lifecycle (two-phase load, re-injection after `ForceReloadAssets`), ref resolver, SO factories, embedded command vocabularies, art loading/placeholders, set-screen/Codex/class integration. Public API: `DawnKit.Cards` / `Sets` / `Weapons` / `WeaponPowers` builders validating at `Register()` (with did-you-mean hints), clean-spelled enum mirrors (`Rarity`, `Suffix.Physical`, …) mapped to the game's typo'd enums. M1b: ownership registry with cross-mod collision refusal at `Register()`, `.AutoId()` / `Sets.Register(name, author)` deterministic ID blocks (SPEC §4.3), consolidated boot conflict report, in-game status row, `DiagnosticsDump`. Ships zero content. |
| `DawnKit.Packs.dll` | `dcmods.dawnkit.packs` | **Data client.** `[BepInDependency("dcmods.dawnkit")]`. Scans `<PacksPath>/<Pack>/pack.json` (manifest v1.1: cards + weapons + weaponPowers), registers everything through the public API. Runs zero patches; its csproj references **no Assembly-CSharp and no Harmony** — the compiler enforces the "public API only" acceptance guard. |
| `Dawncaster.Sandbox.dll` | `com.dawncastermods.sandbox` | **Thin dev sandbox** (never shipped): the SandboxStrike hello-card behind `[Sandbox] InjectSandboxCard` (default off), registered via the typed clean-enum builder API. |

Source layout (namespaces per SPEC §3.2): `DawnKit/` (engine — `Api/`,
`Core/` = Lifecycle/RefResolver/Ownership stub/Status probe/PatchManager,
`Content/` = Factories/Vocabulary/Art, `Integration/` = Sets/Codex/Classes),
`DawnKit.Packs/`, `Dawncaster.Sandbox/`. `DawnKit.slnx` builds all three.

## Prerequisites

- BepInEx **5.4.23.2** (win_x64) extracted into the game root
  `E:\Games\Steam\steamapps\common\Dawncaster` (adds `BepInEx\`, `winhttp.dll`,
  `doorstop_config.ini` next to `Dawncaster.exe`). Zip archived at
  `D:\temp\claude\dawncaster-mods\tools\bin\BepInEx_win_x64_5.4.23.2.zip`.
- .NET SDK (any recent; builds `net472` via `Microsoft.NETFramework.ReferenceAssemblies`).

## Build

```powershell
dotnet build "D:\src\mods\dawncaster-mods\DC.DawnKit\src\DawnKit.slnx" -c Release
```

Outputs (each in `<project>\bin\Release\`): `DawnKit.dll`, `DawnKit.Packs.dll`,
`Dawncaster.Sandbox.dll`. Game/BepInEx references are `Private=false`, so only
those DLLs ship. `docs\research\reference\effect-commands.txt` /
`talent-commands.txt` are embedded into **DawnKit.dll** (the engine owns
codeLine validation).

## Install

```powershell
$plugins = "E:\Games\Steam\steamapps\common\Dawncaster\BepInEx\plugins"
$src = "D:\src\mods\dawncaster-mods\DC.DawnKit\src"
Copy-Item "$src\DawnKit\bin\Release\DawnKit.dll" $plugins -Force
Copy-Item "$src\DawnKit.Packs\bin\Release\DawnKit.Packs.dll" $plugins -Force
Copy-Item "$src\Dawncaster.Sandbox\bin\Release\Dawncaster.Sandbox.dll" $plugins -Force  # dev only
```

**Upgrading from the 0.4.0 monolith:** the old `Dawncaster.Sandbox.dll` must be
replaced/removed (it contained the whole loader — leaving a stale copy alongside
DawnKit would double-inject). Same filename, so the copy above overwrites it.

## Configuration (one cfg per plugin GUID)

`BepInEx\config\dcmods.dawnkit.cfg` (engine):

| Section.Key | Default | Meaning |
|---|---|---|
| `Engine.Enabled` | `true` | Master switch (fail-safe rule). `false` = no patches applied, no injection — completely vanilla. |
| `Engine.VerboseLogging` | `false` | Decision-level debug logging. |
| `Engine.DiagnosticsDump` | `false` | Write `BepInEx\DawnKit-diagnostics.txt` at boot completion (overwritten each boot): per-mod items with IDs/names/sets/classes and applied/FAILED status, the conflict list, unresolved references. The bug-report channel (P19). |

`BepInEx\config\dcmods.dawnkit.packs.cfg` (data client):

| Section.Key | Default | Meaning |
|---|---|---|
| `Packs.PacksPath` | `<plugin dir>\DawncasterPacks` | Directory whose subdirs are scanned for `pack.json` (subdirs without one are skipped). **Dev setup**: the repo root `D:\src\mods\dawncaster-mods`, so the checked-in `DC.<Pack>` packages are the live source. |
| `Packs.ExpansionOverride` | *(empty)* | Emergency override: force every card's expansion to a native `CardExpansions` member; disables per-pack synthetic sets (no set rows). |
| `Packs.AutoDiscoverModCards` | `true` | Mark loaded mod cards discovered in the Codex (in-memory only; the engine never writes `Codex.dtt`). |

`BepInEx\config\com.dawncastermods.sandbox.cfg` keeps only
`Sandbox.InjectSandboxCard` (default `false`).

**Config migration:** on first run, `DawnKit.Packs` reads the legacy
`com.dawncastermods.sandbox.cfg` `[Packs]` keys (PacksPath / ExpansionOverride /
AutoDiscoverModCards), seeds its own cfg with them, saves, and logs a
`Migrated [Packs] settings from legacy …` hint — the game keeps working without
user edits. The legacy `[Packs]` keys are not read again afterwards.

## Runtime behavior

The injection/lifecycle/set/Codex behavior is unchanged from the monolith
(that was the M1a acceptance bar) — the mechanics documentation lives in
`../SPEC.md` §3/§6 and the data contracts in `../CARD-PACK-SPEC.md` /
`../WEAPON-SPEC.md`. In short:

- **Lifecycle** (engine): phase 1 on `SetPlayerAssetsLoaded`/`LoadPlayerAssets`
  (construct + register cards/weapons/talents, per-owner `RefreshCaches` +
  `CreateRunLists`, class attachment), phase 2 on
  `SetWorldAssetsLoaded`/`LoadWorldAssets` (authoritative name→ref resolution).
  Registrations are durable: `ForceReloadAssets` wipes are pruned by list
  membership and rebuilt idempotently by ID/name.
- **Validation at Register()** (engine): enum membership
  (exact-then-case-insensitive with warning), codeLine commands vs the embedded
  565-effect / 660-talent-union vocabularies, cost-key/flag whitelists, weapon
  category forced `BasicAttack`. One bad item is skipped with a named error and
  a `RegisterResult.Failed`; collisions vs the live pools are checked at
  injection time. Failures also count into the per-pack "skipped" log line.
- **schemaVersion handshake (M2)**: `pack.json` may declare an optional
  top-level integer `"schemaVersion"` (absent = 1). `DawnKit.Packs` supports
  schemaVersion **1**; a pack declaring a higher version is refused entirely
  (one log error naming the pack, its declared version, the supported version,
  and the remedy: update DawnKit.Packs) and reported via the public
  `Mods.ReportFailedMod` API so the boot report shows it as a failed mod and
  the status row's error count includes it. Equal/lower/absent loads normally.
  The gate's decision table is pinned by a boot self-check log line
  (`Schema gate self-check: 5/5 reference cases OK`).
- **Set rows / bonuses / Sunforge / Codex**: identical patch set, now applied
  individually by the engine's PatchManager with per-target
  `Target found:` / `Target MISSING:` boot logging; a missing target disables
  only that integration.
- **Ownership & conflicts (M1b, SPEC §3.5)**: every `Register()` records
  `(owner, kind, id, name)` in the registration ledger (owner = explicit
  `.Owner(...)`, else the calling plugin's BepInEx GUID). Duplicate IDs or
  (case-insensitive) names across registered mods are refused at `Register()`
  with BOTH claimants named; shipped-pool collisions are refused at injection
  the same way. After each injection pass the engine logs one consolidated
  **boot report** (per-mod registered/applied/failed + every conflict), e.g.:

  ```
  [DawnKit] ── Boot report ──────────────────────────────
  [DawnKit] 5 mods · 65 items registered, 63 applied, 2 failed · 2 conflicts
  [DawnKit]   VenomousLegacy: 16 registered, 16 applied, 0 failed
  [DawnKit]   ConflictTest: 2 registered, 0 applied, 2 failed
  [DawnKit] Conflicts (both claimants named):
  [DawnKit]   ✗ ConflictTest/Glacial Lance: cardID 700000100 already owned by VenomousLegacy (card "Weeping Blade")
  [DawnKit] ── end boot report ──────────────────────────
  ```

- **AutoId (M1b, SPEC §4.3)**: `Sets.Register(name, author)` derives the mod's
  100-ID block as `700,000,000 + (FNV1a32(lower(author + "/" + name)) %
  1,000,000) * 100`; builders use `.InSet(set).AutoId()` — cards allocate
  bottom-up, weapons top-down from the block end, talent IDs top-down in their
  own space. A block owned by a different mod is a **hard refusal** (both owners
  named, remedy = explicit block) — never probing to adjacent blocks. The
  Python twin (`tools/gamedata.py`) and the engine pin the same 5 reference
  vectors (`tools/tests/test_autoid.py`; boot logs `AutoId self-check: 5/5`).
- **Status row (M1b)**: with any mod content registered, the run-settings
  set screen shows one non-interactive appended row —
  `DawnKit: 4 mods, 63 items loaded`, plus
  `— K errors, see BepInEx/LogOutput.log` when anything failed. Fail-safe:
  if the row can't render it logs and skips.
- **Validation hints (M1b)**: unknown codeLine commands and enum members get
  did-you-mean suggestions (case fix > prefix > Levenshtein ≤ 2, top 3),
  mirrored in `tools/validate_pack.py`.

## Verification results

### M1b ownership/AutoId/status (2026-07-18, DawnKit 0.6.0)

Three live boots against the four repo packs:

1. **Clean boot (DiagnosticsDump=true)** — baseline unchanged vs M1a: 4 packs /
   47 cards / 5 weapons / 11 powers / 0 skipped, sets 1000–1003 [13/14/13/12],
   class counts byte-for-byte equal, 56 refs resolved / 0 unresolved,
   `grep -ciE "error|exception"` = 0. New lines:
   `AutoId self-check: 5/5 reference vectors OK.`, one boot-report block
   (`4 mods · 63 items registered, 63 applied, 0 failed · 0 conflicts`,
   `Conflicts: none`), and
   `Diagnostics dump written: …\BepInEx\DawnKit-diagnostics.txt` (full per-mod
   ledger with IDs/names/sets/classes, `Status: DawnKit: 4 mods, 63 items loaded`).
2. **Intentional conflict** — throwaway `ZZ.ConflictTest` pack (deleted after)
   colliding with VenomousLegacy by ID and by name. Both refused at Register()
   with both claimants named (log lines quoted in the boot-report example
   above); boot report showed `5 mods · 65 items registered, 63 applied,
   2 failed · 2 conflicts`; diagnostics `Status:` line appended
   `— 2 errors, see BepInEx/LogOutput.log`; the four real packs loaded
   untouched.
3. **Steady state (DiagnosticsDump back to false)** — single boot-report block,
   0 conflicts, 0 errors, no diagnostics file.

The status row itself renders inside `NameSelectorDisplay.SetSettings`
(run-settings set screen) — log line `Status row added: DawnKit: …` appears
when that screen opens; the visual check is part of the manual test script.

### M1a extraction (2026-07-18, DawnKit 0.5.0 — behavior-identical vs 0.4.0)

Live launch with the four repo packs via the migrated config;
`BepInEx\LogOutput.log` (abridged — all 20 `Target found` lines present):

```
[Info   :   BepInEx] Loading [DawnKit 0.5.0]
[Info   :   DawnKit] [DawnKit] Command vocabulary: 565 effect / 660 talent-union.
[Info   :   DawnKit] [DawnKit] Target found: AssetManager.SetPlayerAssetsLoaded
...14 more patch targets...
[Info   :   DawnKit] [DawnKit] Target found: AssetManager.CreateRunLists
[Info   :   DawnKit] [DawnKit] Target found: NameSelectorDisplay.ToggleSet(SetConfig, SunforgeSettingButton) (member)
[Info   :   DawnKit] [DawnKit] Target found: SunforgeSettings.ToggleSet(SetConfig, SunforgeSettingButton) (member)
[Info   :   DawnKit] [DawnKit] Target found: CodexUI.shownExpansions (member)
[Info   :   DawnKit] [DawnKit] Target found: AssetManager._runcards (member)
[Info   :   DawnKit] [DawnKit] DawnKit 0.5.0 loaded — 16/16 patch targets applied.
[Info   :   BepInEx] Loading [Dawncaster Sandbox 0.5.0]
[Info   :Dawncaster Sandbox] [Sandbox] Dawncaster Sandbox 0.5.0 loaded (InjectSandboxCard=false — nothing registered).
[Info   :   BepInEx] Loading [DawnKit.Packs 0.5.0]
[Warning:DawnKit.Packs] [DawnKit.Packs] Migrated [Packs] settings from legacy com.dawncastermods.sandbox.cfg (PacksPath=D:\src\mods\dawncaster-mods). Future changes go in dcmods.dawnkit.packs.cfg — the legacy [Packs] keys are no longer read.
[Info   :DawnKit.Packs] [DawnKit.Packs] Configured. PacksPath=D:\src\mods\dawncaster-mods, ExpansionOverride=(none — per-pack synthetic sets), AutoDiscoverModCards=True.
[Info   :DawnKit.Packs] [DawnKit.Packs] Clockwork Cadence: registered 12 cards, 1 weapons, 3 weapon powers (applied at asset load).
[Info   :DawnKit.Packs] [DawnKit.Packs] CrimsonLedger: registered 11 cards, 1 weapons, 3 weapon powers (applied at asset load).
[Info   :DawnKit.Packs] [DawnKit.Packs] EmberweaveGrove: registered 12 cards, 1 weapons, 3 weapon powers (applied at asset load).
[Info   :DawnKit.Packs] [DawnKit.Packs] VenomousLegacy: registered 12 cards, 2 weapons, 2 weapon powers (applied at asset load).
[Info   :   DawnKit] [DawnKit] Clockwork Cadence: 12 cards injected, 0 skipped (hook: SetPlayerAssetsLoaded)
[Info   :   DawnKit] [DawnKit] Clockwork Cadence: 1 weapons, 3 weapon powers injected (classes: Arcanist, Seeker)
[Info   :   DawnKit] [DawnKit] CrimsonLedger: 11 cards injected, 0 skipped (hook: SetPlayerAssetsLoaded)
[Info   :   DawnKit] [DawnKit] CrimsonLedger: 1 weapons, 3 weapon powers injected (classes: Knight, Warrior)
[Info   :   DawnKit] [DawnKit] EmberweaveGrove: 12 cards injected, 0 skipped (hook: SetPlayerAssetsLoaded)
[Info   :   DawnKit] [DawnKit] EmberweaveGrove: 1 weapons, 3 weapon powers injected (classes: Arcanist, Knight)
[Info   :   DawnKit] [DawnKit] VenomousLegacy: 12 cards injected, 0 skipped (hook: SetPlayerAssetsLoaded)
[Info   :   DawnKit] [DawnKit] VenomousLegacy: 2 weapons, 2 weapon powers injected (classes: Hunter, Rogue, Warrior)
[Info   :   DawnKit] [DawnKit] Class weapon/talent counts: Arcanist=3w/6t, Hunter=2w/3t, Knight=3w/6t, Rogue=2w/2t, Seeker=2w/4t, Warrior=2w/5t, Scion=0w/0t
[Info   :   DawnKit] [DawnKit] Synthetic card sets: EmberweaveGrove=(CardExpansions)1000 [13 cards], VenomousLegacy=(CardExpansions)1001 [14 cards], Clockwork Cadence=(CardExpansions)1002 [13 cards], CrimsonLedger=(CardExpansions)1003 [12 cards]
[Info   :   DawnKit] [DawnKit] Reference resolution: 56 resolved, 0 unresolved (hook: SetPlayerAssetsLoaded/phase1)
[Info   :   DawnKit] [DawnKit] Reference resolution: 0 resolved, 0 unresolved (hook: SetWorldAssetsLoaded)
```

Identical to the 0.4.0 baseline (below): 4 packs / 47 cards / 5 weapons /
11 weapon powers / 0 skipped; class counts byte-for-byte equal; sets 1000–1003
with 13/14/13/12 cards (weapons included); 56 refs resolved, 0 unresolved;
`grep -ciE "error|exception"` = **0**. A second launch (post-migration steady
state) reproduced the same counts with no migration line and 0 errors.
Acceptance guard: `DawnKit.Packs.csproj` references no Assembly-CSharp and no
Harmony — the client compiles against the public API namespace alone.

## Pre-split baseline (Dawncaster.Sandbox 0.4.0) — kept as the regression reference

### Weapons & weapon powers (2026-07-18, plugin 0.4.0, manifest v1.1)

```
[Info   :   BepInEx] Loading [Dawncaster Sandbox 0.4.0]
[Info   :PackLoader] [PackLoader] Configured. PacksPath=D:\src\mods\dawncaster-mods, ExpansionOverride=(none — per-pack synthetic sets), AutoDiscoverModCards=True, command vocabulary: 565 effect / 660 talent-union.
[Info   :PackLoader] [PackLoader] Clockwork Cadence: 12 cards injected, 0 skipped (hook: SetPlayerAssetsLoaded)
[Info   :PackLoader] [PackLoader] Clockwork Cadence: 1 weapons, 3 weapon powers injected (classes: Arcanist, Seeker)
[Info   :PackLoader] [PackLoader] CrimsonLedger: 1 weapons, 3 weapon powers injected (classes: Knight, Warrior)
[Info   :PackLoader] [PackLoader] EmberweaveGrove: 1 weapons, 3 weapon powers injected (classes: Arcanist, Knight)
[Info   :PackLoader] [PackLoader] VenomousLegacy: 2 weapons, 2 weapon powers injected (classes: Hunter, Rogue, Warrior)
[Info   :PackLoader] [PackLoader] Class weapon/talent counts: Arcanist=3w/6t, Hunter=2w/3t, Knight=3w/6t, Rogue=2w/2t, Seeker=2w/4t, Warrior=2w/5t, Scion=0w/0t
[Info   :PackLoader] [PackLoader] Reference resolution: 56 resolved, 0 unresolved (hook: SetPlayerAssetsLoaded/phase1)
[Info   :PackLoader] [PackLoader] Reference resolution: 0 resolved, 0 unresolved (hook: SetWorldAssetsLoaded)
```

- All 5 weapons + 11 weapon powers injected, 0 skipped (plus the 47 cards);
  every Profession list grew to base + mod counts; 56/56 refs resolved; zero
  errors. Synthetic set counts include the weapons (13/14/13/12); the reward
  pool does not (weapons are `allCards`-only + `excludeFromRewards`).
- The in-game char-creation walkthrough (weapon offered/locked rendering, power
  activation, cooldown tick) is the WEAPON-SPEC §7 gate-3 QA pass and still
  needs a manual session.

### Per-pack card sets + Codex discovery (2026-07-18, plugin 0.3.0)

```
[Info   :PackLoader] [PackLoader] Synthetic card sets: EmberweaveGrove=(CardExpansions)1000 [12 cards], VenomousLegacy=(CardExpansions)1001 [12 cards], Clockwork Cadence=(CardExpansions)1002 [12 cards], CrimsonLedger=(CardExpansions)1003 [11 cards]
[Info   :PackLoader.UI] [PackLoader] Run-settings set row added: EmberweaveGrove -> (CardExpansions)1000, 12 cards
[Info   :PackLoader.UI] [PackLoader] Set toggle: EmberweaveGrove ((CardExpansions)1000) excluded=True
```

- 47 cards with per-pack synthetic expansions matching the ID-registry blocks;
  4 set rows with pack display names, eye-icon preview, native `ToggleSet`
  round-trip of `excludedsets`; `CreateRunLists` probe lines on every run-pool
  rebuild; Codex `marked N mod cards as discovered` on codex load. (v0.5.0
  reproduces all of these — same messages under the `DawnKit` log sources.)

### Pack loader (2026-07-18, plugin 0.2.0)

- All 4 packs / 47 cards injected, 0 skipped; 40 refs resolved (37
  `referenceStatus` + 3 intra-pack `referenceCards`); clean boot.

### Hello-card (2026-07-17, plugin 0.1.0)

```
[Info   :Dawncaster Sandbox] [Sandbox] Injected SandboxStrike (id 900001), allCards=2478 (hook: SetPlayerAssetsLoaded)
```
