# DawnKit — engine + data client + dev sandbox (M1a layout)

BepInEx plugins for Dawncaster (Steam, Unity 2022.3.62f2, Mono). The former
`Dawncaster.Sandbox` 0.4.0 monolith is split per `../SPEC.md` §3 into:

| Assembly | Plugin GUID | Role |
|---|---|---|
| `DawnKit.dll` | `dcmods.dawnkit` | **Engine.** Owns ALL Harmony patches (SPEC §6, 16 targets + 4 private members, each logged "Target found: X" at boot). Lifecycle (two-phase load, re-injection after `ForceReloadAssets`), ref resolver, SO factories, embedded command vocabularies, art loading/placeholders, set-screen/Codex/class integration. Public API: `DawnKit.Cards` / `Sets` / `Weapons` / `WeaponPowers` builders validating at `Register()`, clean-spelled enum mirrors (`Rarity`, `Suffix.Physical`, …) mapped to the game's typo'd enums. Ships zero content. |
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
- **Set rows / bonuses / Sunforge / Codex**: identical patch set, now applied
  individually by the engine's PatchManager with per-target
  `Target found:` / `Target MISSING:` boot logging; a missing target disables
  only that integration.

## Verification results

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
