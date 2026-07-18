# Dawncaster.Sandbox — BepInEx content-injection plugin

BepInEx plugin for Dawncaster (Steam, Unity 2022.3.62f2, Mono). Two features:

1. **Card-pack loader** (`PackLoader.cs` + `PackManifest.cs`) — loads every
   `<PacksPath>/<Pack>/pack.json` manifest (schema: `CARD-PACK-SPEC.md` §2) into
   `AssetManager.allCards`/`playercards` at asset-load time. This is how the four
   repo packs (47 cards) get into the game.
2. **SandboxStrike hello-card** — the original proof-of-concept injection, now behind
   the `InjectSandboxCard` config flag (default **off**).

No game files are modified; everything is runtime injection via Harmony.

## Prerequisites

- BepInEx **5.4.23.2** (win_x64) extracted into the game root
  `E:\Games\Steam\steamapps\common\Dawncaster` (adds `BepInEx\`, `winhttp.dll`,
  `doorstop_config.ini` next to `Dawncaster.exe`). Zip archived at
  `D:\temp\claude\dawncaster-mods\tools\bin\BepInEx_win_x64_5.4.23.2.zip`.
- .NET SDK (any recent; builds `net472` via `Microsoft.NETFramework.ReferenceAssemblies`).

## Build

```powershell
dotnet build "D:\temp\claude\dawncaster-mods\src\Dawncaster.Sandbox\Dawncaster.Sandbox.csproj" -c Release
```

Output: `src\Dawncaster.Sandbox\bin\Release\Dawncaster.Sandbox.dll` (references to
BepInEx/Harmony/Assembly-CSharp/UnityEngine/Newtonsoft.Json are `Private=false`, so only
this one DLL ships; `reference\effect-commands.txt` is embedded into it for runtime
codeLine validation).

## Install

```powershell
Copy-Item "D:\temp\claude\dawncaster-mods\src\Dawncaster.Sandbox\bin\Release\Dawncaster.Sandbox.dll" `
          "E:\Games\Steam\steamapps\common\Dawncaster\BepInEx\plugins\" -Force
```

Launch the game normally (Steam must be running).

## Configuration

`BepInEx\config\com.dawncastermods.sandbox.cfg` (created on first run):

| Section.Key | Default | Meaning |
|---|---|---|
| `Packs.PacksPath` | `<plugin dir>\DawncasterPacks` | Directory scanned for `<Pack>/pack.json` manifests. **Dev setup**: point it at the repo — `D:\temp\claude\dawncaster-mods\packs` — so the checked-in manifests are the live source. **Non-dev deployment**: copy each `packs\<Pack>\` folder (with its `pack.json` + optional `art\`) into `BepInEx\plugins\DawncasterPacks\`. |
| `Packs.ExpansionOverride` | `Core` | When non-empty, every loaded card's manifest `expansion` is overridden with this `AssetManager.CardExpansions` member at load. The manifests say `Extended`; if you disable all non-core sets in-game, `CreateRunLists()` would filter `Extended` cards out via `excludedsets`. With the default `Core` override the mod cards stay in the run pool alongside the base set. Set to empty to respect the manifest values (then keep the Extended set enabled in-game). |
| `Sandbox.InjectSandboxCard` | `false` | Inject the SandboxStrike test card (id 900001). |

## How the pack loader works

- **Hooks** (Harmony postfixes, all idempotent and re-entrant):
  - `AssetManager.SetPlayerAssetsLoaded()` + safety net `LoadPlayerAssets()` → **phase 1**:
    parse manifests, construct `Card` ScriptableObjects, register them.
  - `AssetManager.SetWorldAssetsLoaded()` + safety net `LoadWorldAssets()` → **phase 2**:
    resolve `referenceStatus` / `referenceCards` by asset name.
- **Why two phases**: statuses only exist after `CreateStatusCollections()` in the world
  phase, and calling `AssetManager.GetStatus` before that would hit its lazy
  `LoadAllAssets()` fallback and reenter the loading pipeline. Phase 1 therefore leaves
  refs unresolved (an opportunistic pass runs only when the collections are already
  populated — which is in fact the case on the async boot path, where the game sets both
  flags after all loading finishes). Phase 2 is the authoritative pass: every ref still
  unresolved after it is logged as an error with card + ref name.
- **Validation**: enums are parsed by exact member name (case-insensitive fallback logs a
  warning), cost keys / flags are whitelisted, and every `codeLine` command is checked
  against the embedded 565-command vocabulary. Any unknown enum/command/field ⇒ that card
  is logged and skipped; the load never crashes. (Manifests are also gated offline by
  `tools/validate_pack.py`.)
- **Registration** mirrors `AssetManager.ProcessCard`: `allCards` always (Metaprogress →
  `metacards`), `playercards` when player-legal (`expansion != None/Metaprogress`,
  `rarity != Monster`, `suffix != Companion`). After each pack batch:
  `RefreshCaches()` + `CreateRunLists()` (when a player is loaded). Collisions with
  existing card IDs/names ⇒ skip with error.
- **Re-injection**: `ForceReloadAssets()` (game version change) clears all collections;
  the hooks re-fire on the next load pass, wiped cards are detected (tracked instance no
  longer in `allCards`) and rebuilt from the manifests.
- **Art**: if `packs\<Pack>\art\<CardName>.png` exists it is loaded via
  `Texture2D.LoadImage` (512×512 RGBA per `ART-PIPELINE.md`); otherwise a generated
  512×512 placeholder is used — flat color from the card's cost-color identity
  (`Card.GetColor()`: green DEX, blue INT, red STR, gold HOLY, aqua/orange/purple hybrids,
  black life/corruption, brown neutral) with a darker lower half so it reads as
  placeholder art.
- **audioClip** stays `null` — verified safe: every usage site null-checks it
  (`CodexUI.cs:1229`, `CombatUIHandler.cs:1224`, `SpellEffects.cs:260`, `SpellManager.cs:630`).

## Verification results

### Pack loader (2026-07-18, plugin 0.2.0)

Game launched with `PacksPath = D:\temp\claude\dawncaster-mods\packs`;
`BepInEx\LogOutput.log`:

```
[Info   :   BepInEx] Loading [Dawncaster Sandbox 0.2.0]
[Info   :PackLoader] [PackLoader] Configured. PacksPath=D:\temp\claude\dawncaster-mods\packs, ExpansionOverride=Core, command vocabulary: 565.
[Info   :Dawncaster Sandbox] [Sandbox] Dawncaster Sandbox 0.2.0 loaded, hooks installed.
[Info   :PackLoader] [PackLoader] Clockwork Cadence: 12 cards injected, 0 skipped (hook: SetPlayerAssetsLoaded)
[Info   :PackLoader] [PackLoader] CrimsonLedger: 11 cards injected, 0 skipped (hook: SetPlayerAssetsLoaded)
[Info   :PackLoader] [PackLoader] EmberweaveGrove: 12 cards injected, 0 skipped (hook: SetPlayerAssetsLoaded)
[Info   :PackLoader] [PackLoader] VenomousLegacy: 12 cards injected, 0 skipped (hook: SetPlayerAssetsLoaded)
[Info   :PackLoader] [PackLoader] Reference resolution: 40 resolved, 0 unresolved (hook: SetPlayerAssetsLoaded/phase1)
[Info   :PackLoader] [PackLoader] Reference resolution: 0 resolved, 0 unresolved (hook: SetWorldAssetsLoaded)
```

- All 4 packs load: **47 cards injected, 0 skipped**.
- All **40 references resolved, 0 unresolved** (37 `referenceStatus` + 3 intra-pack
  `referenceCards`; matches the manifest count exactly).
- No exceptions/NREs in `LogOutput.log` or `Player.log`; boot finished with
  "Game loading completed successfully".

### Hello-card (2026-07-17, plugin 0.1.0)

```
[Info   :Dawncaster Sandbox] [Sandbox] Injected SandboxStrike (id 900001), allCards=2478 (hook: SetPlayerAssetsLoaded)
```

Injection fired exactly once via the `SetPlayerAssetsLoaded` postfix; `allCards` = 2477
baseline + 1. (SandboxStrike is now off by default — enable `Sandbox.InjectSandboxCard`
to bring it back.)
