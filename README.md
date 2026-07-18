# dawncaster-mods

Modding sandbox for **Dawncaster: The RPG Cardventure** (Steam app 3966890).
Goal: custom content — cards first, then opportunity events and talents/passives.

## Layout

- `GROUND-TRUTH.md` — verified architecture findings from the decompiled game code. Start here.
- `GAME-MECHANICS.md` — canonical game mechanics + synergy web (decompile-verified, claim-grounded).
  Required reading before designing any card/event/talent.
- `ART-PIPELINE.md` — card art specs (512×512 full-bleed) + authoring/runtime pipeline.
- `decompiled/` — full ilspycmd decompilation of `Assembly-CSharp.dll` (562 files). Read-only
  reference; regenerate after game updates with:
  `ilspycmd -p -o decompiled --nested-directories -r "<game>\Dawncaster_Data\Managed" "<game>\Dawncaster_Data\Managed\Assembly-CSharp.dll"`
- `reference/effect-commands.txt` — all 565 card-effect DSL commands (from `SpellEffects.cs`).
- `reference/dialogue-action-commands.txt` — all 99 Ink event action commands
  (from `DialogueActionHandler.cs`).
- `reference/asset-extraction-notes.md` — how to re-run extractors; asset counts; card-art
  location/naming/dimensions.
- `tools/` — offline extraction tooling (UnityPy; game dir is READ-ONLY):
  - `extract_data.py` — all content ScriptableObjects → `tools/out/data/<Class>/*.json`
    (2525 cards with codeLines, 383 talents, 49 statuses, 148 events, 192 Ink stories...)
  - `extract_sprites.py` — sprites → `tools/out/sprites/` + `sprite-index.json` (3,774 images)
  - `tools/out/`, `tools/bin/` — machine artifacts, gitignore when repo-ified
- `src/Dawncaster.Sandbox/` — BepInEx 5 plugin (working): injects test card `SandboxStrike`
  at asset-load time. See `src/README.md` for build/install.

## Environment

- Game: `E:\Games\Steam\steamapps\common\Dawncaster` (Unity 2022.3.62f2, **Mono** backend)
- Loader: BepInEx 5.4.23.2 (installed in game dir) + HarmonyX
- Extraction: Python 3.14, UnityPy 1.25.2, TypeTreeGeneratorAPI 0.0.10
- Plugin build: `dotnet build -c Release` (net472)

## Card packs (design wave 1)

- `CARD-PACK-SPEC.md` — pack manifest schema, ID policy (mods own 700,000,000+),
  pool-derived power budgets, uniqueness bar, validation gates.
- `packs/` — four designed packs (47 cards): `VenomousLegacy` (poison/bleed/Infected),
  `EmberweaveGrove` (burn/graveyard), `Clockwork Cadence` (chain/echo/conjure),
  `CrimsonLedger` (blood/souls/artifice). Each: `pack.json` + `DESIGN-NOTES.md` +
  `BALANCE-REPORT.md`. `packs/ID-REGISTRY.md` allocates ID blocks.
- `tools/validate_pack.py` — static gate (schema/enums/vocabulary/collisions/art).
- `tools/sim/` — balance harness: DSL-subset combat sim with verified status timings,
  envelopes calibrated on the shipped pool (self-check 17/20 shipped commons ON-CURVE).
  68 pytest tests in `tools/tests/`.

## Status (2026-07-18)

- ✅ Grounding: architecture + mechanics docs, full data/sprite extraction
- ✅ BepInEx bootstrap + hello-card verified live in-game (`SandboxStrike`, `damage:6`)
- ✅ Wave-1 pack designs + balance reports (3 cards flagged, see BALANCE-REPORTs)
- Next: fix flagged cards; JSON pack loader in the plugin; art generation
  (artforge/contact-sheet scripts); in-game QA pass (gate 4); hello-event (Ink) spike
