# Dawncaster.Sandbox — BepInEx content-injection plugin

BepInEx plugin for Dawncaster (Steam, Unity 2022.3.62f2, Mono). Features:

1. **Card-pack loader** (`PackLoader.cs` + `PackManifest.cs`) — loads every
   `<PacksPath>/<Pack>/pack.json` manifest (schema: `CARD-PACK-SPEC.md` §2) into
   `AssetManager.allCards`/`playercards` at asset-load time. This is how the four
   repo packs (47 cards) get into the game.
1b. **Weapons & weapon powers** (manifest **v1.1**, `WEAPON-SPEC.md` §2/§5, plugin
   v0.4.0) — optional `weapons` (starting-weapon cards, category `BasicAttack`,
   offered at character creation via `Profession.weapons`) and `weaponPowers`
   (tier-0 `Talent`s with an `ActivateWeapon` payload, offered via
   `Profession.talents`) arrays in the same pack.json. See "How weapons load"
   below.
2. **Per-pack card sets** (`SetScreenPatches.cs`, v0.3.0) — every pack shows up as its
   own toggleable row in the run-settings "card sets" screen (and the Sunforge set
   screen), wired to the same `excludedsets` logic as the official Core/Extended/…
   rows. Set value formula: `1000 + (idBlock.start − 700,000,000)/100`
   (CARD-PACK-SPEC.md §3).
3. **Codex auto-discovery** — mod cards render face-up in the Codex instead of as
   undiscovered silhouettes (config `AutoDiscoverModCards`, default on).
4. **SandboxStrike hello-card** — the original proof-of-concept injection, now behind
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
dotnet build "D:\src\mods\dawncaster-mods\src\Dawncaster.Sandbox\Dawncaster.Sandbox.csproj" -c Release
```

Output: `src\Dawncaster.Sandbox\bin\Release\Dawncaster.Sandbox.dll` (references to
BepInEx/Harmony/Assembly-CSharp/UnityEngine/Newtonsoft.Json are `Private=false`, so only
this one DLL ships; `reference\effect-commands.txt` and `reference\talent-commands.txt`
are embedded into it for runtime codeLine validation).

## Install

```powershell
Copy-Item "D:\src\mods\dawncaster-mods\src\Dawncaster.Sandbox\bin\Release\Dawncaster.Sandbox.dll" `
          "E:\Games\Steam\steamapps\common\Dawncaster\BepInEx\plugins\" -Force
```

Launch the game normally (Steam must be running).

## Configuration

`BepInEx\config\com.dawncastermods.sandbox.cfg` (created on first run):

| Section.Key | Default | Meaning |
|---|---|---|
| `Packs.PacksPath` | `<plugin dir>\DawncasterPacks` | Directory scanned for `<Pack>/pack.json` manifests. **Dev setup**: point it at the repo — `D:\src\mods\dawncaster-mods\packs` — so the checked-in manifests are the live source. **Non-dev deployment**: copy each `packs\<Pack>\` folder (with its `pack.json` + optional `art\`) into `BepInEx\plugins\DawncasterPacks\`. |
| `Packs.ExpansionOverride` | *(empty)* | **Emergency override.** When non-empty, every loaded card's expansion is forced to this `AssetManager.CardExpansions` member and per-pack synthetic sets are disabled (no set rows). Default empty = each pack becomes its own card set (see below). *(v0.2.0 defaulted this to `Core`; if your cfg predates 0.3.0, blank the line or delete the cfg.)* |
| `Packs.AutoDiscoverModCards` | `true` | Add all loaded mod card IDs to the in-memory Codex list so they render as discovered. In-memory only — the plugin never writes `Codex.dtt` itself (the game persists the list on its own saves; stale mod IDs are harmless, see save-compatibility below). |
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

## How weapons & weapon powers load (v0.4.0, manifest v1.1 — WEAPON-SPEC.md §5)

Both arrays ride the same phase-1 hooks (after `AllClasses()` has populated
`AssetManager.allClasses`) and the same phase-2 reference resolver.

- **Weapons are Cards** built by the existing card factory (full card schema +
  a `classes` list). Category **must** parse to `BasicAttack` — anything else is
  logged and skipped. `excludeFromRewards` is forced `true`; weapons register in
  `allCards` **only** (never `playercards` — they enter play via character
  creation, not rewards). Same cardID/name collision policy as cards.
- **Weapon powers are tier-0 Talents** (`ScriptableObject.CreateInstance<Talent>`):
  `ID = talentID`, `tier = 0`, `cooldown`, description/flavortext inline,
  `unique = true`, all exclusion flags false, `requiredTalents`/
  `requiredProfessions` empty (class gating is purely `Profession.talents`
  membership). Registered in `AssetManager.allTalents` + `RefreshCaches()`;
  talentID/name collision vs `allTalents` ⇒ skip with error. `powerImage` uses
  `packs\<Pack>\art\<Name>.png` when present, else the two-band placeholder
  (white). `audioClip` stays null (only usage site null-checks,
  PlayerUIHandler.cs:1631).
- **codeLine validation**: talent effects check against the embedded
  `effect-commands.txt` ∪ `talent-commands.txt` (the `RunTalentEffect` switch
  falls through to SpellEffects, TalentHandler.cs:510) — 660 commands; card
  effects keep the 565-command effect vocabulary.
- **Class attachment**: for each name in `classes` (exact Profession asset name,
  or `"all"`), the live `Profession` in `AssetManager.allClasses` gets the card/
  talent appended to `profession.weapons` / `profession.talents`. The
  character-creation UI reads those lists live (`CharacterBuilder.LoadWeapons`) —
  no UI patching. Unknown class name ⇒ error logged, that attachment alone
  skipped. Attachment is idempotent **by cardID/talentID + name**, not object
  identity, and replaces stale instances — so re-injection after
  `ForceReloadAssets()` converges (spec §5.5).
- **Log lines**:
  `[PackLoader] <Pack>: N weapons, M weapon powers injected (classes: ...)` per
  pack, plus a per-class debug dump after every injection pass:
  `[PackLoader] Class weapon/talent counts: Knight=2w/10t, ...` (weapon/talent
  list sizes on each live Profession — verify the lists actually grew).
- **Set rows**: weapons inherit the pack's synthetic expansion and appear in the
  pack's set row/preview, but set exclusion only filters the reward pool —
  weapons/powers stay offered at character creation even when the pack's card
  set is toggled off (v1 scope).

## How the set / Codex integration works (v0.3.0, `SetScreenPatches.cs`)

**Synthetic sets.** Each pack's cards get `cardexpansion = (CardExpansions)(1000 +
(idBlock.start − 700,000,000)/100)` — EmberweaveGrove→1000, VenomousLegacy→1001,
Clockwork Cadence→1002, CrimsonLedger→1003. Undefined enum members are legal in C#;
every pipeline touchpoint (`CreateRunLists` excludedsets filter, save serialization via
`JsonUtility`, PlayerPrefs `lastExcludedSets` round-trip through `Enum.TryParse`, which
accepts numeric strings) treats the enum as an int, verified against the decompiled
source. Packs without a valid `idBlock` fall back to their manifest `expansion` and get
no set row (warning logged).

**Run-settings rows** — the native screen (`NameSelectorDisplay.SetSettings`,
decompiled:498) builds one row per enum value that has a `Resources/Sets/<name>`
`SetConfig`; synthetic values have neither, so a postfix appends one row per pack using
the same prefab (`setSelectionSetting`), a runtime-built `SetConfig`
(name/colors/preview cards/promo banner generated from the pack), and the game's own
private `ToggleSet(SetConfig, SunforgeSettingButton)` for the toggle — so
`excludedsets`, sounds, accessibility lines and `SetExcludedSetsModified()` all behave
exactly like native rows. The eye icon is fully supported: it opens the native
`SetPreviewPanel` showing the pack's actual cards. Cosmetic patches fix every spot that
would render the raw enum int ("1002"): row label (`SunforgeSettingButton.SetDisplay`),
preview title (`SetPreviewPanel.SetDisplay`), set description
(`SetConfig.GetDescription`), and the "(x/y)" sets counter
(`NameSelectorDisplay.ExpansionInfo`).

**Balance guards.** `GetBonusTalent`/`GetBonusTransmute` grant run bonuses keyed on
`excludedsets.Count`; prefixes make them count only *native* exclusions so disabling a
mod pack never eats a bonus. The Sunforge settings screen gets the same rows
(`SunforgeSettings.SetSettings` postfix), and its reroll baseline (+1 per disableable
set) is raised by one per mod pack so mod sets follow the native reroll economy.

**Codex.** Discovery = `CodexHandler.codex.cardList.Contains(cardID)`
(decompiled `CodexUI.cs:1353,685`). A `LoadCodex` postfix (plus a call after each
injection pass) adds all mod IDs to that list **in memory**; the plugin never calls
`SaveCodex`. `CodexUI.Start` seeds its expansion filter from `Enum.GetValues`, which
misses synthetic values — a postfix appends them to the private `shownExpansions` list
so mod cards survive any filter pass.

**Save compatibility if the mod is removed.** Everything degrades to no-ops:
- `excludedsets` with synthetic ints (save file / `lastExcludedSets` PlayerPrefs /
  `LastCharacterConfig.json`): `JsonUtility` and `Enum.TryParse("1002")` both
  deserialize undefined members fine; a stale `(CardExpansions)1002` in `excludedsets`
  simply excludes nothing because no card carries that expansion. The base game's set
  screen never renders it (rows come from `Enum.GetValues` + `Resources/Sets`), so at
  worst the vanilla "(x/y)" counter under-counts until the next run reset clears it.
  One vanilla quirk returns: stale exclusions count toward `GetBonusTalent/Transmute`
  thresholds (fewer bonus talents/transmutes) until cleared — cleared automatically on
  the next non-run-it-back character screen visit if the player ever toggles sets.
- Mod card IDs persisted into `Codex.dtt` by the game's own saves: harmless — all reads
  are `Contains()`, and the Codex cleanup pass (`CodexUI.Start`, decompiled:251-279)
  only removes IDs whose card still exists but is no longer collectible; unknown IDs
  (`GetCard` returns null) are left untouched.
- In-run save with mod cards in the deck: **not** covered by set machinery — that was
  already true in 0.2.0 (cards vanish from `AssetManager` and the deck load logs
  missing cards). Finish runs before uninstalling.

## Verification results

### Weapons & weapon powers (2026-07-18, plugin 0.4.0, manifest v1.1)

Game launched with the real pack content (5 weapons + 11 weapon powers across the
four repo packs, authored against WEAPON-SPEC §2); `BepInEx\LogOutput.log`:

```
[Info   :   BepInEx] Loading [Dawncaster Sandbox 0.4.0]
[Info   :PackLoader] [PackLoader] Configured. PacksPath=D:\src\mods\dawncaster-mods\packs, ExpansionOverride=(none — per-pack synthetic sets), AutoDiscoverModCards=True, command vocabulary: 565 effect / 660 talent-union.
[Info   :PackLoader] [PackLoader] Clockwork Cadence: 12 cards injected, 0 skipped (hook: SetPlayerAssetsLoaded)
[Info   :PackLoader] [PackLoader] Clockwork Cadence: 1 weapons, 3 weapon powers injected (classes: Arcanist, Seeker)
[Info   :PackLoader] [PackLoader] CrimsonLedger: 1 weapons, 3 weapon powers injected (classes: Knight, Warrior)
[Info   :PackLoader] [PackLoader] EmberweaveGrove: 1 weapons, 3 weapon powers injected (classes: Arcanist, Knight)
[Info   :PackLoader] [PackLoader] VenomousLegacy: 2 weapons, 2 weapon powers injected (classes: Hunter, Rogue, Warrior)
[Info   :PackLoader] [PackLoader] Class weapon/talent counts: Arcanist=3w/6t, Hunter=2w/3t, Knight=3w/6t, Rogue=2w/2t, Seeker=2w/4t, Warrior=2w/5t, Scion=0w/0t
[Info   :PackLoader] [PackLoader] Reference resolution: 56 resolved, 0 unresolved (hook: SetPlayerAssetsLoaded/phase1)
[Info   :PackLoader] [PackLoader] Reference resolution: 0 resolved, 0 unresolved (hook: SetWorldAssetsLoaded)
```

- All **5 weapons + 11 weapon powers injected, 0 skipped** (plus the 47 cards).
- **Profession lists actually grew**: every class shows exactly base + mod counts
  (e.g. Knight 3w = Longsword + Bloodprice Falchion + Cinderbough Wand, 6t =
  1 shipped tier-0 + 5 mod powers; Scion untouched at 0w/0t — no pack targets it).
- All **56 references resolved, 0 unresolved** (40 card refs + 16 weapon/power refs).
- Zero errors/exceptions in the log (`grep -ciE "error|exception"` = 0).
- Synthetic set counts include the weapons (13/14/13/12 cards); the reward pool
  does not (weapons are `allCards`-only + `excludeFromRewards`).
- In-game char-creation UI walkthrough (weapon offered/locked rendering, power
  activation in exploration, cooldown tick) is the WEAPON-SPEC §7 gate-3 QA pass
  and still needs a manual session.

### Per-pack card sets + Codex discovery (2026-07-18, plugin 0.3.0)

Game launched with the new build (`ExpansionOverride` blanked in the cfg);
`BepInEx\LogOutput.log`:

```
[Info   :   BepInEx] Loading [Dawncaster Sandbox 0.3.0]
[Info   :PackLoader] [PackLoader] Configured. PacksPath=D:\temp\claude\dawncaster-mods\packs, ExpansionOverride=(none — per-pack synthetic sets), AutoDiscoverModCards=True, command vocabulary: 565.
[Info   :PackLoader] [PackLoader] Clockwork Cadence: 12 cards injected, 0 skipped (hook: SetPlayerAssetsLoaded)
[Info   :PackLoader] [PackLoader] CrimsonLedger: 11 cards injected, 0 skipped (hook: SetPlayerAssetsLoaded)
[Info   :PackLoader] [PackLoader] EmberweaveGrove: 12 cards injected, 0 skipped (hook: SetPlayerAssetsLoaded)
[Info   :PackLoader] [PackLoader] VenomousLegacy: 12 cards injected, 0 skipped (hook: SetPlayerAssetsLoaded)
[Info   :PackLoader] [PackLoader] Synthetic card sets: EmberweaveGrove=(CardExpansions)1000 [12 cards], VenomousLegacy=(CardExpansions)1001 [12 cards], Clockwork Cadence=(CardExpansions)1002 [12 cards], CrimsonLedger=(CardExpansions)1003 [11 cards]
[Info   :PackLoader] [PackLoader] Reference resolution: 40 resolved, 0 unresolved (hook: SetPlayerAssetsLoaded/phase1)
[Info   :PackLoader.UI] [PackLoader] Run-settings set row added: EmberweaveGrove -> (CardExpansions)1000, 12 cards
[Info   :PackLoader.UI] [PackLoader] Run-settings set row added: VenomousLegacy -> (CardExpansions)1001, 12 cards
[Info   :PackLoader.UI] [PackLoader] Run-settings set row added: Clockwork Cadence -> (CardExpansions)1002, 12 cards
[Info   :PackLoader.UI] [PackLoader] Run-settings set row added: CrimsonLedger -> (CardExpansions)1003, 11 cards
[Info   :PackLoader.UI] [PackLoader] Set toggle: EmberweaveGrove ((CardExpansions)1000) excluded=True
[Info   :PackLoader.UI] [PackLoader] Set toggle: EmberweaveGrove ((CardExpansions)1000) excluded=False
```

- All 47 cards injected with their per-pack synthetic expansions; formula values match
  the ID registry blocks exactly.
- 4 set rows appended to the run-settings screen, with pack display names and preview
  wiring.
- Clicking a mod row round-trips `excludedsets` (add on first click, remove on second)
  through the game's own `ToggleSet`.
- No errors/exceptions in the log.
- `[PackLoader] CreateRunLists: runcards=N [...] excludedsets=[...]` probe lines fire on
  every run-pool rebuild and show per-pack counts — check these after toggling a set and
  starting a run to confirm exclusion end-to-end.
- Codex: on any codex load the log shows
  `[PackLoader] Codex: marked N mod cards as discovered (in-memory; hook: LoadCodex)`.

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
