# Art Mutation Spec v1 — repurposed in-game art (non-AI)

Card art for mod packs is produced by **deterministic, recipe-driven mutation of extracted
in-game sprites** — no AI generation. Goal: art that keeps Dawncaster's painterly look and
reads as native, while being *visibly distinct* from its source card at a glance (source
and mutant can appear in the same run pool / Codex page).

This spec covers **sourcing + mutation**. The shipping contract (512×512 RGBA full-bleed,
runtime loading, `validate_art` gate, contact sheets) is unchanged — see `ART-PIPELINE.md`.

## 1. Legal/repo policy (non-negotiable)

- Mutated outputs are **derivative works of Wanderlost's copyrighted art**. They are for
  personal, local use only.
- `DC.*/art/*.png` outputs are **gitignored**. What's committed: the recipe files and
  tooling, which rebuild identical PNGs from the locally-extracted sprite corpus
  (`tools/out/sprites/`, itself gitignored and rebuilt by `tools/extract_sprites.py`).
- If this mod is ever distributed publicly, mutated art must be replaced with original
  art first. The recipe layer makes that swap non-disruptive (same filenames).

## 2. Source selection

- Source corpus: player card art only — `cardart_*`, `cards_metamorphosis*`, and expansion
  sheet sprites, 512×512 (see `../docs/research/asset-extraction-notes.md`). Do NOT use
  `creature_abilties_*` (256×256) for player cards, or UI/status icons.
- Correlate shipped card → its art via `Card.artwork.m_PathID` ↔ `sprite-index.json`
  `path_id` (sprite names do not match card names).
- Pick per mod card a **thematically-nearest source** (mod poison card ← shipped poison
  card art), recorded in the recipe. Constraints:
  - Never source from the card named as `meta.nearestExisting` (those two will be
    compared side-by-side forever) unless the mutation includes composition-level ops
    (mirror + composite), not just recolor.
  - A source sprite may back at most **2** mod cards across all packs, and never 2 in
    the same pack.
- **Same-menu adjacency rule (weapons & weapon powers)**: mod content that appears in a
  *selection menu alongside shipped content* must never source art from items in that
  same menu. Concretely: a mod weapon offered to class X must not source from any weapon
  offered to class X (shipped or mod) — source from *other classes'* weapons or from
  non-weapon art with weapon-like composition, then mutate. Same for weapon powers: a
  power offered to class X must not source from tier-0 talent art available to class X.
  Rationale: char-creation renders source and mutant side by side; recolors read as
  duplicates there even when they pass the hash gate.

## 3. Mutation vocabulary (Pillow, deterministic)

Every op is parameterized and seeded; same recipe + same source bytes ⇒ byte-identical
output. Ops compose in recipe order:

| Op | Params | Notes |
|---|---|---|
| `mirror` | — | horizontal flip; cheapest composition change |
| `rotate_crop` | degrees (±3–8) | slight rotation, re-crop to full bleed |
| `zoom_crop` | factor 1.05–1.35, gravity | reframe the composition |
| `hue_rotate` | degrees | HSV hue shift — the workhorse |
| `sat_value` | sat ×, value × / curve | mood shifts |
| `gradient_map` | 2–3 stop palette | duotone/tritone toward pack palette |
| `channel_mix` | 3×3 matrix | subtle color-cast changes |
| `color_wash` | color, opacity, blend mode | pack-tint overlay |
| `vignette` | strength, color | matches native art's dark edges |
| `glow_spot` | center, radius, color, opacity | relight the focal point |
| `composite` | second source, mask (radial/linear/noise), seed | blend two sources — strongest distinctness tool |
| `grain` | seeded noise, opacity | texture unification after composite |
| `posterize_soft` | levels (high) | use sparingly; painterly flattening |

Guardrails: no text/logo stamping; keep ops within ranges that preserve the painterly
read (no extreme posterize/pixelation); every card ends with the shared `vignette`
finisher so pack art feels uniform.

## 4. Pack palettes (recolor targets)

Mutations pull toward the pack's identity so each pack also gains internal cohesion:

| Pack | Palette pull | Feel |
|---|---|---|
| EmberweaveGrove | ember orange / ash grey / charcoal | heat over dead ground |
| VenomousLegacy | toxin green / bruise purple / bone | sickly, wet |
| Clockwork Cadence | electric blue / brass / storm grey | crackling machinery |
| CrimsonLedger | arterial red / black / candle gold | blood-lit ledger room |

## 5. Recipe format

`DC.<Pack>/art-recipes.json` (committed):

```jsonc
{
  "pack": "EmberweaveGrove",
  "finisher": [ { "op": "vignette", "strength": 0.25 } ],   // appended to every card
  "cards": {
    "Ashfeast": {
      "source": "CARDART_4_15",              // sprite-index key; list of 2 for composite
      "sourceCard": "Fireball",              // human note: which shipped card this art belongs to
      "seed": 700000001,                     // = cardID; feeds all seeded ops
      "ops": [
        { "op": "mirror" },
        { "op": "hue_rotate", "degrees": -18 },
        { "op": "gradient_map", "palette": ["#1a1210", "#c2571f", "#f5c469"], "strength": 0.5 },
        { "op": "glow_spot", "center": [0.5, 0.42], "radius": 0.35, "color": "#ff9c3d", "opacity": 0.3 }
      ]
    }
  }
}
```

## 6. Distinctness gate (automated)

`tools/validate_art.py` grows a `--distinctness` check:
- Perceptual hash (dHash + aHash computed with Pillow, 16×16) distance between each
  output and (a) its source sprite(s), (b) every other mod-card art: must exceed
  thresholds (calibrate so that plain `hue_rotate` alone FAILS vs its source — a pure
  recolor is not distinct enough; mirror+recolor should pass).
- Hard fail on identical-source-pair violations from §2.
- The contact sheet renders **source → result pairs** per card for the human eyeball pass.

## 7. Pipeline & workflow

```
tools/out/sprites/  (local, extracted)          DC.<Pack>/art-recipes.json  (committed)
        └──────────────┬─────────────────────────────────┘
                tools/artmutate.py build [--pack X | --all]
                       │   deterministic Pillow ops
                       ▼
        DC.<Pack>/art/<CardName>.png   (gitignored, 512×512 RGBA)
                       ▼
        tools/validate_art.py --all --distinctness      (gate)
                       ▼
        tools/contact_sheet.py --all --pairs            (review: source → result)
                       ▼
        launch game — loader already prefers art/<CardName>.png over placeholders
```

- `artmutate.py build` is idempotent and incremental (rebuilds only cards whose recipe
  or source changed; `--force` for full rebuild).
- Recipe authoring is a design task: pick source via the path_id correlation, choose ops
  per card flavor (the card's `meta`/description drives the art intent), verify on the
  pair sheet.

## 8. Acceptance criteria (v1 done =)

1. All 47 cards have recipes; `artmutate build --all` produces 47 PNGs locally.
2. `validate_art --all --distinctness` passes: dimensions/format + every output distinct
   from source and siblings per §6 thresholds.
3. Contact sheets show source→result pairs; human review signs off per pack.
4. In-game: placeholders gone, mutated art renders in hand/Codex/reward screens, no
   visual confusion with the source cards in a mixed pool.
5. `git status` clean of PNGs (gitignore covers `DC.*/art/`).
