# Dawncaster Mods

Multi-mod monorepo for **Dawncaster: The RPG Cardventure** (Steam app 3966890; Unity Mono,
BepInEx 5 + HarmonyX). One directory per shippable package (`DC.*`), shared docs and Python
toolchain at the root. Direction doc: [MOD-TOOLKIT.md](MOD-TOOLKIT.md).

## Packages

| Package | What it is | Status |
|---|---|---|
| [DC.DawnKit](DC.DawnKit/SPEC.md) | The engine: BepInEx plugin + data-driven pack loader (cards, weapons, weapon powers, synthetic card sets, Codex integration). Owns the data contracts ([CARD-PACK-SPEC](DC.DawnKit/CARD-PACK-SPEC.md), [WEAPON-SPEC](DC.DawnKit/WEAPON-SPEC.md), [ART-PIPELINE](DC.DawnKit/ART-PIPELINE.md), [ART-MUTATION-SPEC](DC.DawnKit/ART-MUTATION-SPEC.md)) | Working monolith (`Dawncaster.Sandbox` 0.4.0); M1 = split into DawnKit + DawnKit.Packs |
| [DC.VenomousLegacy](DC.VenomousLegacy/DESIGN-NOTES.md) | Content pack: poison/bleed/Infected attrition (12 cards + weapons/powers) | Injects in-game; art built |
| [DC.EmberweaveGrove](DC.EmberweaveGrove/DESIGN-NOTES.md) | Content pack: burn/graveyard (12 cards + weapons/powers) | Injects in-game; art built |
| [DC.ClockworkCadence](DC.ClockworkCadence/DESIGN-NOTES.md) | Content pack: chain/echo/conjure tempo (12 cards + weapons/powers) | Injects in-game; art built |
| [DC.CrimsonLedger](DC.CrimsonLedger/DESIGN-NOTES.md) | Content pack: blood/souls/artifice (11 cards + weapons/powers) | Injects in-game; art built |

A top-level `DC.*/` dir with a `pack.json` is a content package (tools and the loader
discover packs by that convention); each pack ships `pack.json` + `DESIGN-NOTES.md` +
`BALANCE-REPORT.md` + `art-recipes.json` (art PNGs are gitignored derivative works,
rebuilt locally).

## Docs

- [docs/CONVENTIONS.md](docs/CONVENTIONS.md) — per-package rules + SPEC template
- [docs/ID-REGISTRY.md](docs/ID-REGISTRY.md) — cardID/talentID block allocations (mods own 700,000,000+)
- [docs/research/GROUND-TRUTH.md](docs/research/GROUND-TRUTH.md) — verified architecture findings from the decompiled game. Start here.
- [docs/research/GAME-MECHANICS.md](docs/research/GAME-MECHANICS.md) — canonical mechanics + synergy web (decompile-verified). Required reading before designing content.
- [docs/research/asset-extraction-notes.md](docs/research/asset-extraction-notes.md) — extractor how-to, asset counts, card-art location/naming
- `docs/research/reference/` — extracted command vocabularies: `effect-commands.txt` (565 card-effect DSL commands), `talent-commands.txt` (115 talent commands), `dialogue-action-commands.txt` (99 Ink event actions)

## Tools (`tools/`, Python; game dir is READ-ONLY)

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
