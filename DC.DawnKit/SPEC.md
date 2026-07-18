# DawnKit Engine Spec v0 — the M1 implementation contract

The content-injection engine for Dawncaster mods. This spec is the refactor contract for
splitting today's working `Dawncaster.Sandbox` monolith (src/Dawncaster.Sandbox/*.cs,
plugin 0.4.0) into a stable engine + data client. Every behavior below exists in that
code unless marked **NEW**. Companions: `CARD-PACK-SPEC.md` (card manifest contract),
`WEAPON-SPEC.md` (weapons/powers contract), `MOD-TOOLKIT.md` §2 (direction + pain ledger
— rows cited as P1–P21).

## 1. Purpose & scope

DawnKit owns everything hard about putting content into Dawncaster: load-phase lifecycle,
two-phase reference resolution, re-injection after asset wipes, safe ScriptableObject
construction, set-screen/Codex/class integration, collision safety, degradation. Client
mods declare content through a typed API (or pure data via `DawnKit.Packs`) and never
touch Harmony. The engine ships **zero content and zero tuning numbers** (docs/CONVENTIONS
rule); our four `DC.*` packs are its permanent dogfood. Out of scope: game-behavior
rebalancing hooks, new DSL commands, asset redistribution (MOD-TOOLKIT §2.4).

## 2. Player-facing behavior

- Installed alone: nothing changes. With content mods: new cards/weapons/powers appear
  in the run pool, character creation, Sunforge, and Codex as if shipped.
- Each pack surfaces as its **own card set** — a named, colored, toggleable row with
  eye-icon preview in the run-settings screen and Sunforge settings, exactly like
  official sets (SetScreenPatches). Disabling a mod set never farms run bonuses (P10)
  or breaks Sunforge reroll math (P11).
- Mod cards render face-up in the Codex (auto-discovery, config-gated) and survive the
  expansion filter (P12).
- **NEW (M1b)** — load status surfaced in-game: "N mods, M items, K errors" with per-mod
  error names, so nobody spelunks BepInEx logs to learn a card was skipped (P19).
  Placement: v1 renders as an appended status row on the run-settings set screen
  (reachable from the already-patched `NameSelectorDisplay.SetSettings`); a true
  main-menu badge needs a target-research pass (open question #2).
- Uninstall/removal degrades, never corrupts (§9).

## 3. Architecture

### 3.1 Assembly split (MOD-TOOLKIT §2.1)

| Assembly | Plugin GUID | Role |
|---|---|---|
| `DawnKit.dll` | `dcmods.dawnkit` | Engine. Owns ALL Harmony patches (§6). Public registries + builders. |
| `DawnKit.Packs.dll` | `dcmods.dawnkit.packs` | Data client. `[BepInDependency("dcmods.dawnkit")]`. Scans pack folders, parses `pack.json` (PackManifest DTOs), calls the public API. Runs zero patches. |
| `Dawncaster.Sandbox.dll` | `com.dawncastermods.sandbox` | Thin dev sandbox: the SandboxStrike hello-world card via the public API. Experiments only; never shipped. |

### 3.2 Namespace layout (MOD-TOOLKIT §2.3 — what moves where)

| Namespace | Today's source | Contents |
|---|---|---|
| `DawnKit.Core.Lifecycle` | SandboxPlugin.AssetLoadHooks + PackLoader.InjectPacks pruning | Load hooks, idempotency, re-injection after `ForceReloadAssets` (P1, P2, P3, P21) |
| `DawnKit.Core.RefResolver` | PackLoader.ResolveReferences | Two-phase referenceStatus/referenceCard resolution by name (P4) |
| `DawnKit.Content.Factories` | PackLoader.BuildCard/BuildTalent/RegisterCard | Non-null lists, `HideAndDontSave`, non-null `CardEnchantments`, ProcessCard-mirroring pool routing (P5, P17) |
| `DawnKit.Content.Vocabulary` | PackLoader.LoadCommandFile + embedded effect-commands.txt / talent-commands.txt | Effect DSL + talent-union command sets, enum tables (P14) |
| `DawnKit.Content.Art` | PackLoader.LoadSpriteFile/CreatePlaceholderSprite/PlaceholderColor | PNG→Sprite (100 PPU, Clamp, HideAndDontSave), cost-color two-band placeholder (P16) |
| `DawnKit.Integration.Sets` | PackSetInfo + SetScreenPatches (rows, names, bonuses, Sunforge) | Synthetic CardExpansions, set rows, bonus/reroll corrections (P8–P11) |
| `DawnKit.Integration.Codex` | PackLoader.MarkModCardsDiscovered + Codex patches | Discovery + shownExpansions filter (P12) |
| `DawnKit.Integration.Classes` | PackLoader.AttachPackToClasses/ResolveClasses | Profession.weapons/talents attachment, stale-instance replacement (P18) |
| `DawnKit.Packs` | PackLoader manifest scan + PackManifest.cs | pack.json parsing → builder calls. Client assembly, not engine. |

**NEW** engine-only namespaces: `DawnKit.Core.Ownership` (registry + conflict report,
§3.5), `DawnKit.Core.Status` (player-facing status + diagnostics dump).

### 3.3 Lifecycle contract as seen by clients

Clients register in `Awake()` (any time before the first player-asset load completes).
Registration is **declarative and durable**: the engine records it and

1. constructs + injects at phase 1 (player assets loaded), refreshing caches and run
   lists (`AssetManager.RefreshCaches`, `CreateRunLists` when a player exists);
2. resolves name-declared refs at phase 2 (world assets loaded — statuses only exist
   after `CreateStatusCollections`; early `GetStatus` would re-enter loading, P4);
3. re-applies automatically after `ForceReloadAssets` wipes collections (tracked
   entries pruned by list membership, rebuilt idempotently by ID/name, P3, P21);
4. re-attaches to freshly reloaded Profession assets (keyed on ID/name, never object
   identity — WEAPON-SPEC §5.5).

Clients never observe phases, hooks, or reloads. Registering after boot is applied on
the next injection pass (next asset reload) — supported, not recommended.

### 3.4 Error model

- **Per-item isolation** (exists — `ManifestError`): one bad card/weapon/power is
  skipped with a rich, named error; the pack, the mod, and the game load on. One bad
  pack manifest skips the pack. Engine-level hook failures are caught per hook.
- Every UI/integration patch is skip-don't-crash: failure logs and leaves native
  behavior intact (SetScreenPatches convention; the fail-safe rule).
- **NEW**: `Register()` returns a result object (`Ok`/`Failed(reason)`); all failures
  are retained per-mod in the registry so the status UI and diagnostics dump can name
  them (today they are log-only).

### 3.5 Ownership registry & conflict report — NEW (M1b)

- Every registration records `(owner: author/modName, kind, id, name)`. IDs and
  names (case-insensitive — names are identity, P7) are validated at `Register()`
  against the shipped pool **and** all other registered mods (today's collision checks
  in PackLoader, centralized).
- At boot completion the engine emits one **conflict report**: per-mod counts plus
  every refused item with both claimants named ("`FrostPack` card 700000102 refused:
  ID owned by `VenomWorks`"). Feeds the status UI and log.

## 4. Data formats & public API surface

Pack-folder JSON contracts are **not duplicated here**: `CARD-PACK-SPEC.md` §2–3 (cards,
identity, synthetic sets) and `WEAPON-SPEC.md` §2–5 (weapons, weaponPowers, class
attachment) are the data contracts `DawnKit.Packs` implements. The C# surface below is
the same capability set, typed.

### 4.1 Registries

`DawnKit.Cards`, `DawnKit.Sets`, `DawnKit.Weapons`, `DawnKit.WeaponPowers` — each with
`.Build(name)` → builder, and `.All` (inspectable registrations + ownership metadata).
`DawnKit.Sets.Register("Name", author:)` is one line (P8); packs get sets implicitly
from their ID block (CARD-PACK-SPEC §3 formula).

### 4.2 Builders — validation at Register()

Builder setters mirror the manifest fields (PackManifest.cs is the field inventory);
`Register()` runs every check the current loader runs, then hands the item to Lifecycle:

| Validation (exists in PackLoader) | Kills |
|---|---|
| Enum membership w/ exact-then-case-insensitive parse + warning (`ParseEnum`) | P15 |
| codeLine command vocabulary, `;`/`:` split, per-statement (`ValidateCodeLine`; talent union for powers) | P14 |
| cardID/talentID + name collision vs allCards/metacards/allTalents (`InjectPacksInner`, `InjectWeapons`, `InjectWeaponPowers`) — **NEW**: vs other registered mods too, via ownership registry | P6, P7 |
| Cost keys (`ApplyCosts` whitelist), flags (`ApplyFlag` whitelist), missing name/ID | P14, P15 |
| Weapon category forced `BasicAttack`, `excludeFromRewards` forced true, allCards-only registration (`InjectWeapons`) | P17, P18 |
| Weapon power: tier forced 0, `unique`, empty `requiredTalents`/`requiredProfessions`, gating by list membership (`BuildTalent`) | P18 |
| Class names resolved against live Professions; `"all"` supported; unknown class skips that attachment only (`ResolveClasses`) | P18 |
| Factory guarantees: `HideAndDontSave`, every list non-null, non-null `CardEnchantments` | P5 |
| **NEW**: did-you-mean suggestions on vocabulary/enum misses | P14 |

**Clean-spelled enums — NEW**: the public API exposes `DawnKit.Rarity` (→
`Card.CardRariry`), `DawnKit.Suffix.Physical` (→ `Card.Suffix.Phyisical`), etc. — thin
mirror enums mapped internally; humans never type the typo (P15). Data packs keep the
game's exact spellings (CARD-PACK-SPEC §2 rule) for 1:1 traceability.

### 4.3 AutoId — NEW (M1b)

`.AutoId()` replaces manual ID bookkeeping (P6):

- `block = 700_000_000 + (FNV1a32(utf8(lower(author + "/" + modName))) % 1_000_000) * 100`
  — FNV-1a 32-bit, offset 2166136261, prime 16777619. 1,000,000 blocks of 100 inside
  the verified-free mod range 700,000,000–799,999,999 (CARD-PACK-SPEC §3).
- IDs allocate sequentially within the block in registration order; weapons top-down
  from block end (WEAPON-SPEC §3). Talent IDs reuse the same numbers (separate space).
- The synthetic set value follows automatically: `1000 + (block − 700M)/100`.
- **Collision refusal**: if the block is already claimed by a *different* owner string
  (ownership registry, or a `DC.*` block registered in `ID-REGISTRY.md` and shipped as
  an explicit `idBlock`), `AutoId` registration is refused entirely with both owners
  named — no probing to adjacent blocks (silent drift would break the set-value and
  save-stability guarantees). Remedy in the error text: set an explicit block.
- Determinism requirement: same owner string → same block on every machine, forever
  (set values and save data depend on it).

## 5. Knobs

Current (SandboxPlugin.Awake — migrate sections `[Packs]` → DawnKit.Packs config,
`[Sandbox]` stays in the sandbox):

- `[Packs] PacksPath` (string, default `BepInEx/plugins/DawncasterPacks`) — scanned for
  `<Pack>/pack.json`. Post-migration default becomes the `DC.*`-aware scan (MOD-TOOLKIT §6.2).
- `[Packs] ExpansionOverride` (string, `""`) — emergency: force every card's expansion
  to a native `CardExpansions` member; disables synthetic sets entirely.
- `[Packs] AutoDiscoverModCards` (bool, `true`) — Codex in-memory discovery.
- `[Sandbox] InjectSandboxCard` (bool, `false`) — SandboxStrike test card.

**NEW** engine knobs (fail-safe rules, MOD-TOOLKIT §6.1):

- `[Engine] Enabled` (bool, `true`) — master switch; false = no patches applied, vanilla.
- `[Engine] VerboseLogging` (bool, `false`) — decision-level Debug logs (per-item
  routing, ref resolution detail, class counts).
- `[Engine] DiagnosticsDump` (bool, `false`) — write `BepInEx/DawnKit-diagnostics.txt`
  at boot completion: per-mod registered content, ownership table, conflict report,
  unresolved refs (P19 bug-report channel).

## 6. Patch targets & integration points

All Harmony patches live in `DawnKit.dll`. **Boot requirement (NEW, ftk2/EOR practice):
every target logs "Target found: `<Class.Method>`" at patch time; a missing target logs
an error and disables only that integration.** Inventory — enumerated from
SandboxPlugin.cs + SetScreenPatches.cs, the complete current set:

| # | Target | Patch | Why (engine home) |
|---|---|---|---|
| 1 | `AssetManager.SetPlayerAssetsLoaded` | postfix | Phase-1 injection, async boot path (Lifecycle) |
| 2 | `AssetManager.LoadPlayerAssets` | postfix | Phase-1 safety net — sync path never calls #1 (P2) |
| 3 | `AssetManager.SetWorldAssetsLoaded` | postfix | Phase-2 ref resolution, final pass (RefResolver) |
| 4 | `AssetManager.LoadWorldAssets` | postfix | Phase-2 safety net, sync path |
| 5 | `NameSelectorDisplay.SetSettings` (private) | postfix | Clone native set rows for mod sets (Sets) |
| 6 | `SunforgeSettingButton.SetDisplay(SetConfig)` | postfix | Row label: pack name, not raw enum int |
| 7 | `SetPreviewPanel.SetDisplay(SetConfig)` | postfix | Preview panel title, same fix |
| 8 | `SetConfig.GetDescription` | prefix | Synthetic-set description; native sets pass through |
| 9 | `NameSelectorDisplay.ExpansionInfo` | postfix | "x/y sets" recap including mod sets |
| 10 | `CreateCharacterFunctions.GetBonusTalent` | prefix | Bonus counts native exclusions only (P10) |
| 11 | `CreateCharacterFunctions.GetBonusTransmute` | prefix | Same (P10) |
| 12 | `SunforgeSettings.SetSettings` (private) | postfix | Sunforge set rows (Sets) |
| 13 | `SunforgeSettings.InitializeSunforgeSettings` | postfix | Reroll baseline +1 per mod set (P11) |
| 14 | `CodexHandler.LoadCodex` | postfix | Mark mod cards discovered on codex (re)load (Codex) |
| 15 | `CodexUI.Start` (private) | postfix | Add mod expansions to `shownExpansions` filter (P12) |
| 16 | `AssetManager.CreateRunLists` | postfix | Verification probe: per-set run-pool counts (Status) |

Non-patch integration: `AssetManager.RefreshCaches`/`CreateRunLists` calls after
injection; private-member access via `AccessTools` (`NameSelectorDisplay.ToggleSet`,
`SunforgeSettings.ToggleSet`, `CodexUI.shownExpansions`, `AssetManager._runcards`) —
these get the same found/missing boot log. **NEW (M1b)**: one status-row render target
on the set screen (inside existing patch #5, no new target).

## 7. Example dataset (`examples/`) — NEW (M1c)

- `examples/MyFirstMod/` — the 20-line C# mod (MOD-TOOLKIT §2.2 sketch, compiling):
  one `Sets.Register`, one `Cards.Build(...).AutoId()...Register()`. THE acceptance
  demo for API ergonomics; doubles as the M2 tutorial seed.
- `examples/ExamplePack/` — minimal data pack: `pack.json` with one card + one weapon +
  one weapon power, one PNG, one placeholder-fallback card (no art), `DESIGN-NOTES.md`.
  Exercises both manifest arrays, class attachment, and art fallback.
- Engine ships neither (CONVENTIONS: no content in the engine); examples are docs.

## 8. Testing plan

**Offline (pytest, exists — `tools/tests/`)**: `test_validate_pack.py`, `test_dsl.py`,
`test_envelopes.py` etc. keep gating pack data. **NEW additions**: AutoId reference
vectors (owner string → expected block; the C# and Python implementations must agree),
`examples/ExamplePack` passes `validate_pack.py`.

**Live-launch (cannot be offline)**: injection, set rows, Codex render, class
attachment, re-injection after version-change reload, status UI.

Human test script (<15 min, shipped example data + our packs):
1. Launch; log shows 16 "Target found" lines, per-pack "N cards injected, 0 skipped",
   synthetic-set summary. (~3 min)
2. New run → run settings: one named colored row per pack; eye preview shows cards;
   toggle a set off/on → `CreateRunLists` probe log shows its cards leave/enter the
   pool; bonus text unchanged by mod toggles. (~4 min)
3. Character creation: example weapon offered for its classes; pick it → in starting
   deck; activate weapon power in exploration → effect fires, cooldown set. (~4 min)
4. Codex: mod cards face-up, present under filters. (~2 min)
5. Break a manifest enum on purpose → relaunch: card skipped, error named in status
   surface/log, game loads clean. (~2 min)

## 9. Save considerations (verified guarantees — cited, not re-derived)

Single-player; no multiplayer section. The engine never writes saves; all persistence
is the game's own, and removal degrades safely:

- Mod weapon missing on load → **Longsword fallback** with error log
  (PlayerHandler.cs:344-359; WEAPON-SPEC §6). Run continues.
- Mod weapon power missing → `talents.FindLast(tier == 0)` null → graceful activation
  error (PlayerUIHandler.cs:1636-1640; WEAPON-SPEC §6).
- Stale synthetic-set ints in saves/PlayerPrefs are harmless: `CardExpansions`
  round-trips as int everywhere it persists, undefined members survive
  (PackSetInfo doc header; CARD-PACK-SPEC §3).
- Codex residue: mod IDs in `Codex.dtt` are inert — base-game checks are pure
  `Contains()`, cleanup skips unknown IDs; we add discovery **in-memory only** and
  never call SaveCodex (PackLoader.MarkModCardsDiscovered doc).
- `LastCharacterConfig.json` stores weapon/power **names**; stale names just fail
  lookup on "run it back" (WEAPON-SPEC §6).
- AutoId determinism (§4.3) keeps set values save-stable across reinstalls.

## 10. Milestones

- **M1a — extraction (acceptance: behavior-identical)**: `DawnKit.dll` compiles with
  the §3.2 layout and owns all §6 patches; `DawnKit.Packs.dll` ports the manifest scan
  as a pure public-API consumer; sandbox shrinks to the hello-world card via the API.
  Test: all four `DC.*` packs load through the public API with zero private hooks and
  log-diff-clean output vs the 0.4.0 monolith.
  **Status: DONE (2026-07-18, DawnKit 0.5.0).** Live-verified: 16/16 targets found
  (+4 members), 4 packs / 47 cards / 5 weapons / 11 powers / 56 refs / sets
  1000–1003 [13/14/13/12] / class counts identical to the 0.4.0 baseline, 0 errors
  (`src/README.md`). `DawnKit.Packs.csproj` references no Assembly-CSharp and no
  Harmony (compiler-enforced public-API-only guard). Config migrated to per-GUID
  cfgs with a legacy-cfg fallback + hint. Pulled forward from M1b: "Target found"
  logging, `[Engine] Enabled`/`VerboseLogging`, `RegisterResult` objects; M1b seams
  stubbed (`Core.Ownership.RegistrationLedger`, `Core.Status`).
- **M1b — the NEW parts**: ownership registry + boot conflict report (§3.5), AutoId
  (§4.3), player-facing status row + `DiagnosticsDump` (§2, §5), `Enabled`/
  `VerboseLogging` knobs, "Target found" logging, Register() result objects.
  **Status: DONE (2026-07-18, DawnKit 0.6.0).** Ownership ledger refuses cross-mod
  ID/name collisions at Register() with both claimants named; AutoId blocks
  (FNV-1a, hard refusal, C#/Python twins pinned on 5 shared vectors); one
  deduplicated boot-report block; set-screen status row; did-you-mean hints;
  `DiagnosticsDump` → `BepInEx/DawnKit-diagnostics.txt`. Live-verified clean +
  intentional-conflict boots (`src/README.md`); pytest 155 green.
- **M1c — examples + docs**: `examples/` (§7), public API reference doc, pytest AutoId
  vectors. Exit = MOD-TOOLKIT M1 success test (20-line example mod registers a card).
  **Status: DONE (2026-07-18).** `examples/MyFirstMod/` (complete compiling BepInEx
  project; the plugin is ~20 lines — one `Sets.Register(name, author)` + one
  `Cards.Build(...).InSet(set).AutoId()...Register()` with the result-check idiom;
  line-by-line README) and `examples/ExamplePack/` (the same set + card as a data-only
  `pack.json` on the AutoId-derived block 737,673,400 — a pinned reference vector —
  passing `validate_pack.py` with 0 errors/0 warnings); `API.md` public reference
  (registries, builders, validation/error model, lifecycle, AutoId, ownership, knobs,
  log vocabulary, Harmony-porting note). §7's fuller dataset (weapon + power + art in
  the data example) folded down to the one-set-one-card twins — the richer versions
  are the four live `DC.*` packs themselves. Live-verified: with MyFirstMod.dll
  deployed the boot report reads `5 mods · 64 items registered, 64 applied, 0 failed`
  with set 377734 [1 cards]; after removal, clean 4-mod baseline again. pytest green;
  AutoId vectors were already pinned in M1b (`tools/tests/test_autoid.py`).

## 11. Open questions

1. ~~**Builders vs attributes**~~ — **decided (2026-07-18): fluent builders** are the
   C# API (implemented in M1a); attributes may still be evaluated as an *addition*
   in M1c, but builders are the contract.
2. ~~**Status surface placement**~~ — **decided: run-settings set screen for v1**
   (inside existing patch #5); main-menu badge deferred to M2 research.
3. ~~**AutoId block size**~~ — **decided: 100 IDs per author/modName block** (§4.3
   as specced); big mods use explicit blocks.
4. ~~**Clean-enum migration**~~ — **decided: the C# API stays clean-spelled only**;
   the data-pack path warn-and-accepts the game's typo'd spellings (string setters
   parse exact-then-case-insensitive with a warning — M1a behavior).
5. **Sim-gate provenance** (MOD-TOOLKIT #7): should `DawnKit.Packs` surface a status
   warning for packs without a sim/balance stamp?
6. ~~**Semver/capability handshake**~~ — **resolved (2026-07-18, M2, DawnKit 0.7.0):
   implemented pack-side with the public release.** `pack.json` gains an OPTIONAL
   top-level `"schemaVersion": 1` (integer; absent = 1). `DawnKit.Packs` supports
   schemaVersion 1 (`SchemaGate.SupportedSchemaVersion`); a pack declaring a HIGHER
   version is refused **entirely** — fail-safe, since a newer schema may carry content
   types the loader cannot see — with one log error naming the pack, its declared
   version, the supported version, and the remedy (update DawnKit.Packs). The refusal
   is reported through the new public `Mods.ReportFailedMod(owner, error)` API, so the
   boot report shows the pack as a failed mod and the status row's error count includes
   it. Equal/lower/absent loads normally. Engine-version semver policy:
   `src/README.md` VERSIONING.
