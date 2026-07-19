# DawnKit v0.9.0 — release notes

> **DRAFT — NOT FOR PUBLICATION.** Internal draft for the M2 "first
> public-ready cut". Do not publish, upload, or link this release anywhere
> without the maintainer's explicit go — licensing is still TBD
> (MOD-TOOLKIT #5) and Wanderlost has not yet been contacted (MOD-TOOLKIT #4).

## What DawnKit is

DawnKit is a modding toolkit for Dawncaster built for human modders —
explicitly including not-so-technical ones: describe the card, don't program
the injection. The engine is a clean library that sits on top of the game's
own code and handles content injection properly — lifecycle, registration,
collision safety, UI integration, degradation — so modders install it as a
BepInEx plugin and add content either from C# against a typed API or purely
as data (pack folders). All the hard-won engine knowledge from building four
real content packs — load phases, two-phase reference resolution,
re-injection after asset wipes, set-screen integration, Codex discovery — is
encoded once, in the engine, instead of living in any single mod.

## What's new in 0.9.0 — opportunity events (manifest v2)

The fourth content family lands (EVENT-SPEC.md E1): packs and C# mods can ship
**opportunity events** — Ink-scripted map encounters that join the same global
fill pool as vanilla roadside events (native frequency, no odds skew).

- **Manifest**: optional top-level `events` array — `name`, `storyFile`
  (compiled Ink JSON, pack-relative), optional `minLevel`/`maxLevel`/`unique`.
  Presence requires `"schemaVersion": 2`; older loaders refuse the whole pack
  instead of silently dropping events. Events are name-keyed — no numeric IDs,
  no idBlock needed for an events-only pack.
- **API**: `DawnKit.Events.Build(name).StoryFile(path)` (or `.StoryJson(text)`)
  `.Levels(min, max).Unique().Register()` — validated at Register(): JSON
  parses, `inkVersion` ∈ [18, 20], every `>>>` action command in the game's
  99-command dialogue vocabulary, name collisions vs shipped events (both the
  Dialogue-name and TextAsset-name namespaces) and other mods.
- **Engine**: events inject at the world-asset phase and re-inject after game
  updates like all content; a new `DialogueManagerINK.StartDialogue` prefix
  serves registered stories by name (vanilla names pass through untouched).
  Fail-safe: if the patch target or any tracked member is missing, the whole
  Events integration disables itself — an unservable story never reaches the
  map. New `[Events] Enabled` config knob (default true).
- **Authoring pin**: compile stories with **inklecate v1.0.0** (emits
  inkVersion 20). Inky releases bundling ink ≥ 1.1 emit 21, which the game's
  runtime rejects.
- **Validator**: `dmk validate` covers the `events` section (ink version pin,
  dialogue-action vocabulary with did-you-mean, dual-namespace collisions,
  `goto` knot checks; `STORYFUNCTION` is reserved for now).
- **Example**: `examples/ExampleEventPack/` — the "Hello Wayfarer" event
  (two choices, one `>>>>gold:50` action) with committed `.ink` source.
- **Honest list**: uninstalling a pack mid-run leaves already-dealt event nodes
  on the map; picking one aborts cleanly (the dialogue closes, the run
  continues). Picked `unique` events leave inert name strings in the save's
  `doneEvents` — harmless, human-readable, stable across reinstalls.

## What's new in 0.8.0 — starting cards (manifest v1.2)

The third character-creation loadout slot lands: packs (and C# mods) can now
ship **starting cards** alongside weapons and weapon powers.

- **Manifest**: optional top-level `startingCards` array — full card schema
  (any legal card shape, no category pinning) plus `classes`
  (WEAPON-SPEC.md §2). JSON schema + `validate_pack.py` cover it (budget
  lints, top-down ID advisory, shared collision namespaces).
- **API**: `DawnKit.StartingCards.Build(name)...ForClasses(...).Register()`
  mirrors `Weapons`; ledger kind `"startingCard"`.
- **Behavior**: the card registers in the normal pools (playercards-eligible,
  NOT reward-excluded — 62/63 shipped starting cards are ordinary reward-pool
  cards) and is appended to the live `Profession.startingCards` list, which
  the character-creation UI (phase 5) reads directly; the pick enters the
  starting deck ×1 and round-trips in the save like any other deck card — no
  fallback plumbing needed.
- **AutoId**: starting cards share the block's top-down loadout cursor with
  weapons (WEAPON-SPEC.md §3).

## What's in 0.7.0 — first public-ready cut

Two shippable DLLs (`DawnKit.dll` engine + `DawnKit.Packs.dll` data loader);
full API reference in `API.md`, data contracts in `CARD-PACK-SPEC.md` /
`WEAPON-SPEC.md`.

- **Declarative, lifecycle-safe registration.** Clients register in
  `Awake()`; the engine injects at the right load phases (sync and async
  boot paths), resolves name-declared references in phase 2, survives
  `ForceReloadAssets` wipes (game updates) by re-applying automatically, and
  refreshes caches/run lists. Client mods run zero Harmony patches.
- **Typed C# builder API** — `DawnKit.Cards` / `Sets` / `Weapons` /
  `WeaponPowers`, validating everything at `Register()`: enum membership,
  effect-DSL command vocabulary (565 effect / 660 talent-union commands
  embedded), cost/flag whitelists, ID and name collisions — with
  did-you-mean hints and per-item failure isolation (one bad item never
  kills a load). Clean-spelled enum mirrors (`Rarity`, `Suffix.Physical`)
  map internally to the game's canonical typos.
- **Data-only packs.** Drop a folder with `pack.json` (+ PNG art) under
  `BepInEx/plugins/DawncasterPacks/` — cards, starting weapons, and weapon
  powers with class attachment, no code. Art loads from PNG with a
  placeholder fallback.
- **Per-pack card sets.** Each pack surfaces as its own named, toggleable
  set row with eye-icon preview in run settings and Sunforge, exactly like
  official sets — without farming run bonuses or breaking Sunforge reroll
  math. Codex auto-discovery renders mod cards face-up (config-gated).
- **Ownership registry + conflict safety.** Deterministic AutoId blocks
  (hash of author/mod name; identical C# and Python implementations),
  hard refusal on block/ID/name collisions with both claimants named, one
  consolidated boot report, and an in-game status row
  (`DawnKit: N mods, M items loaded — K errors`). `DiagnosticsDump` writes a
  full per-mod report file for bug reports.
- **Schema-version handshake (new in 0.7.0).** `pack.json` may declare
  `"schemaVersion": 1` (absent = 1); packs declaring a newer schema than the
  installed loader supports are refused entirely with a clear log error and
  counted as a failed mod in the boot report. Semver policy for the engine:
  `src/README.md` VERSIONING.
- **Fail-safe by rule.** Master `Engine.Enabled` kill-switch; every patch
  target logged (`Target found:`) at boot so game-update breakage is
  diagnosable from the log; a missing target disables only that integration.
  Uninstall degrades safely (verified guarantees — Longsword fallback,
  inert save/Codex residue; `SPEC.md` §9).

## Install

See `INSTALL.md` inside the release zip (built by
`tools/package_release.py`): BepInEx 5.4.23 x64 into the game root, the two
DLLs into `BepInEx/plugins/`, packs under
`BepInEx/plugins/DawncasterPacks/<Pack>/pack.json`.

## Known limitations (the honest list)

- **One content-type family so far**: cards, card sets, starting weapons,
  and weapon powers. That's the whole surface.
- **Opportunity events and statuses are not yet supported** — mods can
  *reference* existing statuses by name, but cannot add new ones (M3).
- **The balance simulator covers a subset of the effect DSL**; sim reports
  on packs using commands outside that subset are partial, and the loader
  does not (yet) surface sim provenance in-game.
- **Some verification is still manual QA**: the in-game walkthrough (set
  toggles, weapon offer/pick, power activation, Codex render) is a
  documented <15-minute human script, not automated; this 0.7.0 build's
  schemaVersion refusal path and status-row error count still need one live
  boot check.
- Localization is English-inline only; no loc-table support for packs yet.

## Credits

DawnKit is an **unaffiliated fan project** for
[Dawncaster](https://dawncaster.wanderlost.games/) by Wanderlost Games —
all respect to the original creators. It distributes **no game assets**:
packs travel as manifests, recipes, and original art only.

License: **TBD** (see `LICENSES.md` in the release zip).
