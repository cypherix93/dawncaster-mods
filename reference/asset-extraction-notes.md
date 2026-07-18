# Asset extraction notes (data + sprites)

Verified 2026-07-18 against the Steam PC build (Unity 2022.3.62f2, Mono backend).
Game dir is treated READ-ONLY; all outputs land in `tools/out/` (large — gitignore it).

## How to re-run

```
# ScriptableObject data + Ink TextAssets  ->  tools/out/data/<Class>/*.json, tools/out/data-index.json
python tools/extract_data.py                     # all content classes, ~7s
python tools/extract_data.py --classes Card      # subset

# Sprites  ->  tools/out/sprites/<NAME>.png, tools/out/sprite-index.json
python tools/extract_sprites.py                  # defaults --min 64 --max 2048, ~15 min, 727 MB
```

Requirements: Python 3.14 (`python`), UnityPy 1.25.2, TypeTreeGeneratorAPI 0.0.10.
Both scripts default `--game` to `E:\Games\Steam\steamapps\common\Dawncaster\Dawncaster_Data`.

### Extractor gotchas baked into `extract_data.py` (do not "simplify" these away)

1. **Class-name peek must be cheap.** Use `obj.parse_monobehaviour_head()` +
   `m_Script.deref_parse_as_object()`. Calling `obj.read()` while an
   `env.typetree_generator` is attached does a full generated-tree read of *every*
   MonoBehaviour (thousands of UI widgets) — extremely slow and error-prone.
2. **TypeTreeGeneratorAPI 0.0.10 mis-types `List<T>` fields**: the field node's
   `m_Type` is the *element* type name. For `List<string>` (e.g. `Card.cardKeywords`)
   the node reads `string`, which UnityPy special-cases and misparses whenever the list
   is non-empty (EOFError; empty lists happen to parse identically to an empty string,
   which is why a naive run "works" for ~14% of cards). `FixedTypeTreeGenerator`
   renames such nodes to `vector` (detected: Array child whose data child isn't `char`).
3. **The UnityPyBoost C reader still chokes on these generated trees** even after the
   rename, so the script forces the pure-Python reader
   (`TypeTreeHelper.read_typetree_boost = None`). It's fast anyway (full run 6.7s).
4. All `.assets`/`level*` files are loaded into **one** `UnityPy.load(...)` environment
   so cross-file `m_Script` PPtrs resolve.

## Per-class asset counts (full run)

| Class | Count | | Class | Count |
|---|---|---|---|---|
| Card | 2525 | | Bounty | 35 |
| Keystone | 261 | | NPC | 33 |
| Talent | 383 | | Area | 27 |
| Battle | 170 | | DawncasterProduct | 13 |
| Dialogue | 148 | | Profession | 7 |
| Affix | 59 | | Score | 5 |
| StatusEffect | 49 | | Invasion | 3 |
| TextAsset | 192 | | Companion | 1 |

Notes: `AreaEvent` yields 0 because every event asset is the `Dialogue` subclass;
`ExpansionData` yields 0 (no such assets serialized). The 192 TextAssets are the
compiled **Ink** stories for events (plus a few config files: `IAPProductCatalog`,
`BillingMode`, TMP line-breaking tables). Card categories in the dump: 1536 Action,
509 Enchantment, 110 BasicAttack, 69 Item, 59 Equipment, 31 Artifact, 28 Form,
23 Summon, 21 Affix, 21 Path, 20 Hymn, 19 Conjuration, 18 Revelation, 13 Performance,
12 Adaptation, 10 Code, 9 Ingredient, 6 Location, 6 Mantra, 3 Attunement, 2 Offering.

## Card artwork: where it lives and how to find it

- **Everything is in `resources.assets`** (with pixel data in `resources.assets.resS`).
  The `StreamingAssets\aa` Addressables bundles contain ONLY localization tables and
  CJK fonts — no art. `sharedassets*/level*` hold UI/scene textures.
- `Card.artwork` is a PPtr: `{"m_FileID": 0, "m_PathID": N}`. `m_FileID: 0` = same file
  (resources.assets). Resolve `N` against `sprite-index.json`'s `path_id` field, or
  directly with UnityPy. **Sprite names do NOT match card names** — you must go
  through the pathID (e.g. Fireball -> pathID 1733 -> sprite `cardart_4_15`).
- **Atlases vs standalone**: card art sprites are plain `Sprite` objects cut from
  multi-sprite spritesheet `Texture2D`s ("Multiple" sprite mode) — **no Unity
  `SpriteAtlas` objects are involved** (`m_SpriteAtlas` is null). Several sprites share
  one big texture via `m_RD.texture` (e.g. `cardart_4` and `Smite`'s art both point at
  texture pathID 163). UnityPy's `sprite.image` crops the sheet for you.

### Sheets / naming / dimensions

| Sheet texture (in resources.assets) | Sheet size | Sprite naming | Sprite size | Used for |
|---|---|---|---|---|
| `cardart_1` .. `cardart_5` | 4096x4096 | `cardart_<sheet>_<idx>` (278 total) | **512x512** | player card art (core) |
| `creature_abilties_1` .. `_4` (sic) | 2048x2048 | `creature_abilties_<sheet>_<idx>` | **256x256** | monster/creature ability card art |
| `cards_metamorphosis`, `cards_metamorphosis_2` | 4096x4096 | `cards_metamorphosis[_2]_<idx>` | 512x512 | Metamorphosis expansion card art |
| `abilityart_1`, `abilityart_2` | — | `abilityart_<sheet>_<idx>` | 512x512 | talent/power art |
| `Masks of Misery`, `Death_of_Divinity`, `MoM`/`DoD`/`SoS`/`ToT`* | — | sheet-name + `_<idx>` | 512x512 | expansion card art |
| `card_unlocks` | — | `card_unlocks_<idx>` | 512x512 | unlockable card art |
| `status_icons_sheet` (sharedassets1), `milestone_icons` | — | `<sheet>_<idx>` | mixed (170-256 px) | status/milestone icons |

Standard card art format: **512x512 RGBA sprite** (256x256 for creature abilities),
full-bleed square, no frame — the card frame is drawn by the game UI on top.
Verified examples: Fireball -> `cardart_4_15` 512x512, Cleave -> `cardart_5_41`
512x512, Smite -> `cardart_4_60` 512x512, Abolish -> `creature_abilties_2_40` 256x256.

For modding: this confirms GROUND-TRUTH's claim that a 512x512 PNG ->
`Sprite.Create` is a drop-in replacement for `Card.artwork`.

## Sprite extraction results

`tools/out/sprite-index.json` maps `KEY -> {file, w, h, source, path_id}`, where KEY is
the uppercased object name, `source` is `<assetfile>:<Sprite|Texture2D>` and `path_id`
enables the card-artwork correlation described above. Dedupe: Sprite preferred over
same-named Texture2D, then largest area wins.

Full-run totals: **3,774 images kept** (64-2048 px band), 727 MB. By source:
resources.assets 3,177 Sprites + 101 Texture2Ds (all card art is here, incl. the 278
`cardart_*` sprites), sharedassets1/2 ~350 UI sprites, `defaultlocalgroup` bundle 47
localization sprites, the rest scattered UI/scene textures. The one aa-bundle with
sprites is `defaultlocalgroup_assets_all_*.bundle`; localization/font bundles carry none.

## 5 example card JSONs worth reading (`tools/out/data/Card/`)

| File | What it demonstrates |
|---|---|
| `Adaptive_Edge.json` | Melee attack (`cardType` 0 Melee, `cardCategory` 0 Action): `damage:5`, On-Hit trigger + `adapt` DSL, `adaptationSlots` |
| `Fireball.json` | Spell (Magic/Action, costINT 2): `damage:6:fire`, value token `inflict:[[lastDamage]]/2`, `referenceStatus` PPtr to Burning, `cardKeywords: ["firecast"]` |
| `Sharpen_Steel.json` | Enchantment (`cardCategory` 4): `enchant:self` + populated `CardEnchantments` payload (own `CardEffectList`), `shout:` DSL |
| `Falcon.json` | Summon (`cardCategory` 6, `cardType` 7): multi-line effect (`directdamage:...:self; bless:1; discard:1:this`), self-managing summon pattern |
| `Infernal_Racket.json` | Monster card (`cardType` 7 Monster, played by enemies): `draw:1`, `bless:[[costLastDrawnCard]]` — enemy-side DSL usage |

Also instructive: `Abolish.json` (conditions: `effectConditions` with
`valueToCheck`/`conditonOperator`/`targetValue "[[otherBlessings]]:0"`). Duplicate
asset names get `__N` filename suffixes (64 cases, e.g. `Abyssal_Bulwark__1.json`) —
these are distinct assets sharing one name (different `cardID`s, e.g. cosmetic/alt-art
copies from the `Cosmetics` Resources folder).

## Enum decoding cheat-sheet (ints in the JSON)

From `decompiled/Card.cs`: `cardType`: 0 Melee, 1 Magic, 2 Ranged, 3 Utility, 4 Divine,
5 Move, 6 Corruption, 7 Monster. `cardCategory`: 0 Action, 1 Item, 2 Artifact,
3 Conjuration, 4 Enchantment, 5 BasicAttack, 6 Summon, 7 Performance, 8 Form, 9 Hymn,
10 Revelation, 11 Affix, 12 Attunement, 13 Equipment, 14 Code, 15 Ingredient, 16 Path,
17 Location, 18 Offering, 19 Mantra, 20 Adaptation. `cardRarity`: 0 Common, 1 Uncommon,
2 Rare, 3 Legendary, 4 Monster, 5 Epic, 6 Mythic, 7 Ultra. `cardexpansion`: 0 None,
1 Core, 2 Metaprogress, 3 Metamorphosis, 4 Extended, 5 Infinitum, 6 Catalyst,
7 Eclypse, 8 Synthesis. Booleans serialize as 0/1 ints.
