# Dawncaster Mods

Multi-mod monorepo for **Dawncaster: The RPG Cardventure** (Steam app 3966890; Unity Mono,
BepInEx 5 + HarmonyX). One directory per shippable package (`DC.*`), shared docs and Python
toolchain at the root. Direction doc: [MOD-TOOLKIT.md](MOD-TOOLKIT.md).

## Start here

- **I want to play with mod packs** → install BepInEx + the DawnKit DLLs
  ([DC.DawnKit/src/README.md](DC.DawnKit/src/README.md)), then drop packs into
  `BepInEx\plugins\DawncasterPacks\`
  ([ExamplePack/README.md](DC.DawnKit/examples/ExamplePack/README.md) shows it) —
  the packages table below is the menu.
- **I want to make a pack (no code)** → [docs/TUTORIAL.md](docs/TUTORIAL.md)
  ("your first card in 15 minutes": `dmk new` → edit → gates → install), then
  [CARD-PACK-SPEC](DC.DawnKit/CARD-PACK-SPEC.md) for the contract,
  [docs/design/METHODOLOGY.md](docs/design/METHODOLOGY.md) for the design bar your
  content is reviewed against, and
  [GAME-MECHANICS](docs/research/GAME-MECHANICS.md) to design well.
- **I'm an AI agent working in this repo** → [AGENTS.md](AGENTS.md) (task routing,
  binding rules, commands) — read it before anything else.
- **I want to write a C# mod** → the public builder API in
  [DC.DawnKit/API.md](DC.DawnKit/API.md), starting from the ~20-line
  [MyFirstMod](DC.DawnKit/examples/MyFirstMod/README.md); engine internals in
  [DC.DawnKit/SPEC.md](DC.DawnKit/SPEC.md).
- **I want to work on the toolkit itself** → direction in
  [MOD-TOOLKIT.md](MOD-TOOLKIT.md), the rules in
  [docs/CONVENTIONS.md](docs/CONVENTIONS.md), verified engine knowledge in
  [docs/research/GROUND-TRUTH.md](docs/research/GROUND-TRUTH.md).

## Packages

| Package | What it is | Status |
|---|---|---|
| [DC.DawnKit](DC.DawnKit/SPEC.md) | The engine: `DawnKit.dll` (all patches, lifecycle, [public builder API](DC.DawnKit/API.md)) + `DawnKit.Packs.dll` (data-driven pack loader: cards, weapons, weapon powers, synthetic card sets, Codex integration). Owns the data contracts ([CARD-PACK-SPEC](DC.DawnKit/CARD-PACK-SPEC.md), [WEAPON-SPEC](DC.DawnKit/WEAPON-SPEC.md), [ART-PIPELINE](DC.DawnKit/ART-PIPELINE.md), [ART-MUTATION-SPEC](DC.DawnKit/ART-MUTATION-SPEC.md)) | **M1 complete** (0.6.0): engine + data client + ownership/AutoId/conflict report + status row + examples/docs |
| [DC.DawnKit/examples](DC.DawnKit/examples/MyFirstMod/README.md) | The two flagship examples: [MyFirstMod](DC.DawnKit/examples/MyFirstMod/README.md) (the ~20-line C# mod — one set + one card via the builder API) and [ExamplePack](DC.DawnKit/examples/ExamplePack/README.md) (the same content as a data-only `pack.json`) | Docs/tutorial seeds — compile + validate clean; never shipped |
| [DC.VenomousLegacy](DC.VenomousLegacy/DESIGN-NOTES.md) | Content pack: poison/bleed/Infected attrition (12 cards + weapons/powers) | Injects in-game; art built |
| [DC.EmberweaveGrove](DC.EmberweaveGrove/DESIGN-NOTES.md) | Content pack: burn/graveyard (12 cards + weapons/powers) | Injects in-game; art built |
| [DC.ClockworkCadence](DC.ClockworkCadence/DESIGN-NOTES.md) | Content pack: chain/echo/conjure tempo (12 cards + weapons/powers) | Injects in-game; art built |
| [DC.CrimsonLedger](DC.CrimsonLedger/DESIGN-NOTES.md) | Content pack: blood/souls/artifice (11 cards + weapons/powers) | Injects in-game; art built |

A top-level `DC.*/` dir with a `pack.json` is a content package (tools and the loader
discover packs by that convention); each pack ships `pack.json` + `DESIGN-NOTES.md` +
`BALANCE-REPORT.md` + `art-recipes.json` (art PNGs are gitignored derivative works,
rebuilt locally).

## Docs

- [AGENTS.md](AGENTS.md) — operating manual for AI agents (task routing + binding rules)
- [docs/TUTORIAL.md](docs/TUTORIAL.md) — "your first card in 15 minutes" (the data-path walkthrough)
- [docs/design/METHODOLOGY.md](docs/design/METHODOLOGY.md) — the content-design methodology: design loop, the four review bars (Budget/Uniqueness/Pull-Equity/Flavor), per-type addenda. **Binding for all content drafting.**
- [docs/design/CORPUS-STATS.md](docs/design/CORPUS-STATS.md) — measured shipped-pool curves (card pool, weapons, powers, starting cards) that budget arguments anchor to
- [docs/CONVENTIONS.md](docs/CONVENTIONS.md) — per-package rules + SPEC template
- [docs/ID-REGISTRY.md](docs/ID-REGISTRY.md) — cardID/talentID block allocations (mods own 700,000,000+)
- [docs/research/GROUND-TRUTH.md](docs/research/GROUND-TRUTH.md) — verified architecture findings from the decompiled game. Start here.
- [docs/research/GAME-MECHANICS.md](docs/research/GAME-MECHANICS.md) — canonical mechanics + synergy web (decompile-verified). Required reading before designing content.
- [docs/research/asset-extraction-notes.md](docs/research/asset-extraction-notes.md) — extractor how-to, asset counts, card-art location/naming
- `docs/research/reference/` — extracted command vocabularies: `effect-commands.txt` (565 card-effect DSL commands), `talent-commands.txt` (115 talent commands), `dialogue-action-commands.txt` (99 Ink event actions)

## Tools (`tools/`, Python; game dir is READ-ONLY)

Deps: `pip install --user -r tools/requirements.txt`. One entry point for the
whole chain: **`python tools/dmk.py -h`** (`new` / `validate` / `sim` / `art` /
`artcheck` / `sheet` / `stats`, plus `dmk all <pack>` for the full gate chain);
the standalone CLIs below keep working unchanged.

- `new_pack.py "<Name>" --author "<you>"` — scaffold a valid pack (AutoId block, starter card, recipes/notes skeletons); `schemas/pack.schema.json` gives editors autocomplete + inline docs for `pack.json`
- `extract_data.py` — content ScriptableObjects → `tools/out/data/` (2525 cards, 383 talents, 49 statuses, ...)
- `extract_sprites.py` — sprites → `tools/out/sprites/` + `sprite-index.json` (3,774 images)
- `validate_pack.py --all` — gate 1: schema/enums/vocabulary/collisions (exit 1 on errors)
- `sim/report.py --all` — gate 3: balance sim vs pool-derived envelopes → `DC.<Pack>/BALANCE-REPORT.md`
- Art build ([ART-MUTATION-SPEC](DC.DawnKit/ART-MUTATION-SPEC.md) — outputs gitignored, local use only):
  - `python tools/artmutate.py build --all` — recipes → `DC.<Pack>/art/*.png` (512×512 RGBA, incremental)
  - `python tools/validate_art.py --all --distinctness` — shipping + perceptual-hash gate
  - `python tools/contact_sheet.py --all` — per-pack review sheets + root `contact-sheets.html` index
- `python -m pytest tools/tests` — full suite (sim, validators, art tooling)
- `decompiled/` — gitignored local ilspycmd decompile of `Assembly-CSharp.dll`; regenerate after game updates

## Environment

- Game: `E:\Games\Steam\steamapps\common\Dawncaster` (Unity 2022.3.62f2, **Mono** backend)
- Loader: BepInEx 5.4.23.2 (installed in game dir) + HarmonyX
- Extraction: Python 3.14, UnityPy 1.25.2, TypeTreeGeneratorAPI 0.0.10
- Plugin build: `dotnet build -c Release` (net472) — see [DC.DawnKit/src/README.md](DC.DawnKit/src/README.md) for install/config
- Dev config: `BepInEx\config\com.dawncastermods.sandbox.cfg` sets `PacksPath = D:\src\mods\dawncaster-mods` (loader scans `<PacksPath>\*\pack.json`)

## Status (2026-07-18)

- Live in-game: 4 packs load as their own card sets — 47 cards + 5 weapons + 11 weapon powers, all references resolved
- Full pipeline proven: extraction → design gates (validate/sim) → recipe-driven art → runtime injection
- Next: M1 engine extraction (`DC.DawnKit/SPEC.md`), hello-event (Ink) spike
