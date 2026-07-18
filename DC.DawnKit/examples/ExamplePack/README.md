# ExamplePack — the data-only twin of MyFirstMod

The same one set + one card as [`../MyFirstMod/`](../MyFirstMod/README.md)
("My First Mod" / "Practice Strike"), expressed as a pure-data `pack.json`
instead of C#. No compiler needed: the bundled `DawnKit.Packs.dll` client scans
pack folders, parses the manifest and registers everything through the same
public API the C# example calls — same validation, same set row, same boot
report. Pick one consumption mode per mod; **don't install both examples at
once** (they claim the same set and card, so the second is refused with both
claimants named — a live demo of the conflict report).

## Install

DawnKit + DawnKit.Packs must be installed
([`../../src/README.md`](../../src/README.md)). Then either:

- **Drop it in the default location** — copy this folder to
  `BepInEx\plugins\DawncasterPacks\ExamplePack\` (the `Packs.PacksPath`
  default is `<plugin dir>\DawncasterPacks`; every subfolder with a
  `pack.json` is a pack), or
- **Point `PacksPath` at it** — set `Packs.PacksPath` in
  `BepInEx\config\dcmods.dawnkit.packs.cfg` to any directory whose subdirs
  hold your packs. (This repo's dev config points it at the repo root, which
  is why the `DC.*` packs load from checkout — and why this nested example
  does NOT auto-load: discovery is one level deep, `<PacksPath>\<Pack>\pack.json`.)

Boot log: `[DawnKit.Packs] My First Mod: registered 1 cards, 0 weapons,
0 weapon powers (applied at asset load)`, then the same injection/boot-report
lines as the C# twin.

## Editor autocomplete (the schema)

The manifest's first line points at the repo's JSON Schema:

```json
"$schema": "../../../schemas/pack.schema.json"
```

Any schema-aware editor (VS Code out of the box; JetBrains, others) resolves
that path relative to the `pack.json` file and gives you autocomplete for
every field, the exact enum spellings (including the game's canonical typos —
type `"suffix": "` and you'll be offered `Phyisical`, which is correct),
inline docs on hover, and red squiggles for typos, missing required fields or
unknown keys — *while you type*, before you ever run the validator. When you
copy this file to start your own pack, fix the relative path for your pack's
location (a top-level `DC.<Name>/pack.json` uses `"../schemas/pack.schema.json"`).
The `$schema` key itself is ignored by the loader. The schema is the editor's
half of gate 1; `validate_pack.py` remains the shipping gate (it additionally
checks the command vocabulary, pool/sibling collisions, art files and budgets).

## Validate before shipping (gate 1)

```powershell
cd D:\src\mods\dawncaster-mods
python tools\validate_pack.py DC.DawnKit\examples\ExamplePack\pack.json
```

Exit 0 / `0 error(s), 0 warning(s)` for this pack. The validator checks the
schema, every enum spelling, the effect-DSL command vocabulary, ID/name
collisions vs the 2,525 shipped cards and the repo's other packs, pool
legality and the `meta` review fields. (`--all` only discovers top-level
`DC.*/pack.json` packages; nested packs like this one are validated by
explicit path, as above.)

## Why these IDs can never collide

```json
"idBlock": [737673400, 737673499]
```

This is exactly the AutoId block the C# twin derives at runtime from its
owner pair, computed offline with the Python twin of the engine's formula:

```powershell
python -c "import sys; sys.path.insert(0,'tools'); import gamedata; print(gamedata.autoid_block('dcmods.example', 'My First Mod'))"
# -> 737673400
```

(`block = 700,000,000 + (FNV1a32("dcmods.example/my first mod") % 1,000,000) × 100`
— SPEC.md §4.3; this exact pair is one of the five reference vectors pinned by
`tools/tests/test_autoid.py` and the engine's boot self-check.) It cannot
collide with the real repo packs — their blocks are allocated at
700,000,000–700,000,399 in `docs/ID-REGISTRY.md` — and any third-party AutoId
mod that hashed onto this block would be refused loudly at boot with both
owners named, never silently merged. Starting your own pack: pick your own
`author`/`pack` names, compute your block the same way (or register an
explicit block in ID-REGISTRY.md), and put `cardID`s inside it — cards
bottom-up from the block start, weapons top-down from the end.

## Manifest notes (v1.1 — what's real today)

- Top-level fields the loader reads: `pack` (display name, spaces allowed —
  it names the set row), `idBlock` (`[low, high]`, drives the synthetic set
  value: `1000 + (low − 700,000,000) / 100`, here `377734`), the content
  arrays `cards` / `weapons` / `weaponPowers`, and the optional integer
  `schemaVersion` (absent = 1; the loader refuses manifests newer than it
  supports). There is no `author` manifest field yet (the set's author
  defaults to the pack name), and unknown JSON fields are ignored.
- Full card schema: [`../../CARD-PACK-SPEC.md`](../../CARD-PACK-SPEC.md) §2
  (weapons/powers: [`../../WEAPON-SPEC.md`](../../WEAPON-SPEC.md) §2). Data
  packs use the game's exact enum spellings (including its canonical typos,
  e.g. suffix `Phyisical`) for 1:1 traceability — only the C# API gets the
  clean-spelled mirrors.
- `"art": ""` means no art file: the engine renders its generated
  cost-colored placeholder. Ship real art as `art/<CardName>.png`
  (512×512 RGBA) and put that path here.
- `meta` is review-gate data for humans and the validator (uniqueness
  argument, nearest shipped card); it is stripped before load and never
  reaches the game.
