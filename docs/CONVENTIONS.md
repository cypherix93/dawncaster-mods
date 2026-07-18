# Conventions — how every package in this repo is built

Adapted from `ftk2-mods/docs/CONVENTIONS.md` (the pattern this monorepo adopts —
MOD-TOOLKIT.md §6), minus multiplayer: Dawncaster is single-player.

## Package = shippable unit

One top-level directory per shippable package. Two kinds:

- **Engine packages** ship DLLs from a `src/` tree (today: `DC.DawnKit`, whose data
  contracts — CARD-PACK-SPEC, WEAPON-SPEC, ART-PIPELINE, ART-MUTATION-SPEC — live with it,
  because those contracts ARE its extensibility surface).
- **Content packages** are data-only: any top-level `DC.*/` dir containing a `pack.json`
  is a content pack. **`pack.json` presence is the discovery filter** — the Python tools
  and the runtime loader both scan `<root>/*/pack.json`; DC.DawnKit has none and is
  naturally excluded. Content packs ship `pack.json` + `DESIGN-NOTES.md` +
  `BALANCE-REPORT.md` + `art-recipes.json` (art PNGs are gitignored derivative works,
  rebuilt locally from recipes).

Shared assets live at the root: `docs/research/` (decompile-verified knowledge +
`reference/` command vocabularies), `docs/ID-REGISTRY.md` (ID block allocations),
`tools/` (Python authoring/CI toolchain), `MOD-TOOLKIT.md` (direction).

## The engine philosophy (non-negotiable design rule)

Every engine package is split into two layers:

1. **Engine (C# plugin)** — mechanics only: Harmony patches, lifecycle, factories,
   registries. The engine hardcodes **no content and no tuning numbers**. DawnKit ships
   zero cards; even its example content lives in `examples/`.
2. **Data surface (pack.json + BepInEx config)** — everything a designer would tune:
   content definitions, feature toggles, paths. Changing behavior must not require
   recompiling.

Rules of thumb:
- If a value could plausibly be tuned during balancing, it is a **knob** (BepInEx config
  entry or manifest field).
- If a concept could plausibly have more instances later (a card, a pack, a status, an
  event), it is a **data-driven registry** loaded from pack folders, with new
  files/entries picked up automatically.
- Follow the game's own patterns where they exist — including its **canonical enum typos**
  (`CardRariry`, `Phyisical`): the enums are API surface, never "fix" spellings in data.
  (The future public C# API exposes corrected spellings and maps internally.)

## SPEC.md template (per package; all specs follow this structure)

1. **Purpose & scope** — one paragraph; what it does, what it deliberately does not do.
2. **Player-facing behavior** — what changes in-game, as a player would see it.
3. **Architecture** — engine vs data split; runtime flow; state lifecycle
   (per-combat / per-run / persistent).
4. **Data file formats** — full schemas of every file the engine loads, with a commented
   example of each. This is the extensibility contract.
5. **Knobs** — BepInEx config entries: `[Section] Key (type, default) — description`.
6. **Patch targets & integration points** — exact Assembly-CSharp classes/methods
   (verbatim, verified against `decompiled/`), patch kind (prefix/postfix), and why.
7. **Example starting dataset** — the files shipped as examples and what each
   demonstrates/tests.
8. **Testing plan** — concrete in-game verification steps; what to log; edge cases.
9. **Save considerations** — persistence strategy, uninstall degradation, game-update
   (asset-wipe/reload) behavior.
10. **Milestones** — M1 (MVP) → M2 → M3, each independently shippable.
11. **Open questions** — decisions deferred to review.

(ftk2's multiplayer section is dropped — single-player game.)

## Naming

- Plugin GUIDs: `dcmods.<package>` (e.g. `dcmods.dawnkit`). (The pre-split sandbox
  monolith keeps its legacy `com.dawncastermods.sandbox` GUID until the M1 extraction.)
- Package dirs: `DC.<PascalCase>`, no spaces (`DC.ClockworkCadence`). The in-game display
  name comes from `pack.json`'s `pack` field, which may contain spaces
  ("Clockwork Cadence").
- Content IDs: numeric blocks per pack from the mod range 700,000,000–799,999,999,
  allocated permanently in `docs/ID-REGISTRY.md` (cardIDs bottom-up; weapon and
  starting-card cardIDs share one top-down counter; talentIDs their own namespace —
  WEAPON-SPEC §3).
- Art files: `art/<CardName>.png`, exactly matching the card's manifest name.

## Technical baseline

- Target: BepInEx 5.4.23.2, .NET Framework 4.7.2 (Mono), HarmonyX, game assembly
  `Assembly-CSharp.dll` (reference
  `E:\Games\Steam\steamapps\common\Dawncaster\Dawncaster_Data\Managed\`).
- JSON parsing: Newtonsoft.Json (shipped in Managed).
- **"Target found: X" logging**: every Harmony patch target is verified and logged at
  boot, so breakage after game updates is diagnosable from logs alone (ftk2/EOR
  practice).
- **Fail-safe rule**: every engine feature has a master `Enabled` knob and fails safe —
  parse/validation failures log loudly, skip the failing item (never the whole load), and
  leave vanilla behavior untouched.
- Injected content must survive `ForceReloadAssets()` (game-version-change asset wipe):
  registrations are durable and re-applied by the lifecycle hooks, never one-shot.

## Testing

- Gates, in order: `tools/validate_pack.py` (static, gate 1) →
  `tools/validate_art.py --distinctness` (gate 2) → `tools/sim/report.py` (balance,
  gate 3) → in-game QA (gate 4: injection log lines, set screen, Codex, a live run).
- `python -m pytest tools/tests` must pass before any toolchain change lands.
- Each spec's testing plan must be executable by a human in < 15 minutes with the shipped
  example data.
- The only runtime feedback channel today is `BepInEx\LogOutput.log` — engines log every
  injection/skip/resolution count there; player-facing status is an M1 engine goal.
