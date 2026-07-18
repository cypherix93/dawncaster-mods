# Card Art Pipeline

Static-image pipeline for custom Dawncaster card art, modeled on the `ftk2-mods` tooling
pattern (offline authoring tools → validated pack → runtime loader; game dir read-only).
Art facts below are verified from the extracted assets — see
`../docs/research/asset-extraction-notes.md`.

## Verified art specs (ground truth)

| Surface | Size | Format | Notes |
|---|---|---|---|
| Player card art | **512×512** | RGBA PNG | Full-bleed square, **no frame** — the game UI draws the frame/border on top (frame color from `Card.GetColor()` / `colorCard`). Measured from extraction: CARDART_4_15 (Fireball), CARDART_5_41 (Cleave), ABILITYART_2_55 (Poison Dart) all 512×512 |
| Starting-weapon art (char-creation weapon card face) | **512×873** | RGBA PNG | **Portrait** full-bleed — square art letterboxes in the tall weapon frame. Measured from extraction via `Card.artwork.m_PathID`: LONGSWORD, DAGGERS, FORCEWAND, HATCHETS, KNUCKLES, WARMACE, GREATSWORD are all 512×873 |
| Monster/creature ability art | **256×256** | RGBA PNG | Same full-bleed convention |
| Talent/power art (`Talent.powerImage`) | **512×512** | RGBA PNG | Square — measured from extraction across 10 tier-0 talent `powerImage` path_ids (MASKS_OF_MISERY_18/8, CARD_UNLOCKS_56, DEIFIC-LARCENY, ARTIFICERS-INSIGHT, CARDART_3_62/38, WILL_OF_THE_JUNGLE, TELEVAN_INSIGHT_ECLYPSE, MOM_PHANTOMCARD: all 512×512) |
| Event art (`AreaEvent.eventImage`) | varies | RGBA PNG | Not size-locked; measure a reference before shipping |

In the shipped game these are sprites cut from 4096² spritesheets (`cardart_1..5`,
expansion sheets) in `resources.assets` — but that's an authoring detail of theirs, not a
constraint on us: `Card.artwork` is just a `UnityEngine.Sprite` reference, and a
standalone runtime-created sprite works identically.

## Runtime side (plugin)

Loading a PNG into a card at inject time (APIs verified against the game's shipped
`UnityEngine.ImageConversionModule.dll`, already referenced by `Dawncaster.Sandbox`):

```csharp
static Sprite LoadCardArt(string pngPath)
{
    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
    tex.LoadImage(File.ReadAllBytes(pngPath));   // resizes to actual dimensions
    tex.wrapMode = TextureWrapMode.Clamp;
    var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                               new Vector2(0.5f, 0.5f), 100f);  // 100 PPU = Unity default
    sprite.hideFlags = HideFlags.HideAndDontSave;               // survive scene loads
    tex.hideFlags = HideFlags.HideAndDontSave;
    return sprite;
}
// card.artwork = LoadCardArt(Path.Combine(modDir, "art", card.name + ".png"));
```

Convention: plugin looks for `BepInEx/plugins/DawncasterMods/art/<CardName>.png`
(exact ScriptableObject name → filename). Missing art → fall back to a designated
placeholder PNG, never null (Codex/display code dereferences `artwork`).

## Offline authoring pipeline (tools to build)

Mirrors `ftk2-mods/tools` (`extract → index → transform → contact sheet → validate`).
Everything runs outside the game; `tools/out/` is machine-artifact space (gitignored).

```
art sources (AI-gen / hand-made, any size)
        │
        ▼
tools/artforge.py      — normalize: center-crop to square, Lanczos-resize to 512×512,
        │                strip metadata, RGBA, optional grade presets (see below)
        ▼
mods/<pack>/art/<CardName>.png     — shipping art, committed
        │
        ▼
tools/validate_art.py  — gate: exact per-type size (cards/powers 512×512, weapons
        │                512×873 portrait — measured from extraction; 256×256 for
        │                monster cards), RGBA, file budget scaled by pixel area
        │                (~600 KB at 512², ~1023 KB at 512×873), name matches a
        │                card in the pack manifest
        ▼
tools/contact_sheet.py — HTML review sheet: art rendered at game display size next to
                         name/cost/rarity, like ftk2's *-contact-sheet.html
```

Status: **art sourcing is recipe-driven sprite mutation — see `ART-MUTATION-SPEC.md`.**
`tools/artmutate.py` (build/preview from `DC.*/art-recipes.json`; recipes may set
`"size": [w, h]` — starting weapons build at 512×873 portrait),
`tools/validate_art.py` (per-type size RGBA gate + `--distinctness` perceptual-hash check)
and `tools/contact_sheet.py` (source→result pair sheets) exist and are deterministic
(same inputs → same bytes) per the ftk2 iconforge convention. `artforge.py`
(normalizing external/original art) remains unwritten — only needed if this mod is
ever distributed, when mutated art must be replaced by original art.

### Style reference workflow

- `tools/out/sprites/cardart_*.png` (278 core-set arts) are the style reference corpus —
  painterly fantasy, strong central subject, dark vignetted edges (they sit inside a
  frame), readable at ~150 px display size.
- Use them as *references* for prompting/grading only. **Never redistribute extracted
  game art** in a mod pack; only original/generated art ships.
- Correlating a card to its art for reference: `Card.artwork.m_PathID` →
  `tools/out/sprite-index.json` `path_id` (names do NOT match card names).

### AI-generation guidance

- Generate at ≥768² and downscale to 512 (crisper result than native 512 gen).
- Full-bleed square composition, subject centered; assume the outer ~10% may be
  covered by the frame overlay.
- The game UI renders art at small sizes — favor high contrast, single focal point,
  minimal fine text/detail.
- Keep per-card prompts + seeds in the pack dir (`art-manifest.json`) so packs are
  regenerable — same discipline as ftk2's itemforge profiles.

## In-game verification loop

1. Drop PNG in the plugin art dir, relaunch game (BepInEx console + `Player.log` show
   injection).
2. Check the card in the Codex (art at both thumbnail and inspect size).
3. Screenshot review — contact sheet ≠ in-game render (frame overlay, color grading).
