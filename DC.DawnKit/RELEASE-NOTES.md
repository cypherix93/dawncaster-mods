# DawnKit v0.7.0 — release notes

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
