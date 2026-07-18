"""Scaffold a new content pack: DC.<CollapsedName>/ with a schema-wired
pack.json (AutoId-derived idBlock, one on-curve starter card), an empty
art-recipes.json (finisher stub), and a DESIGN-NOTES.md skeleton.

The idBlock is the deterministic AutoId block (SPEC.md §4.3, same formula the
engine derives at runtime):

    block = 700,000,000 + (FNV1a32(lower(author + "/" + pack)) % 1,000,000) x 100

so the manifest's IDs and the engine's `.AutoId()` agree forever. The
scaffolder refuses block/name collisions against every existing manifest and
self-checks its output through validate_pack.py before declaring success. It
registers nothing anywhere else — docs/ID-REGISTRY.md registration (for repo
packs) is a deliberate human step, printed as a next step.

Usage:
    python tools/new_pack.py "<Pack Name>" --author "<name>"
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import gamedata as gd    # noqa: E402
import validate_pack     # noqa: E402

EXAMPLE_PACK_JSON = (gd.REPO_DIR / "DC.DawnKit" / "examples" / "ExamplePack"
                     / "pack.json")


def collapse(name: str) -> str:
    """'Frost Reverie' -> 'FrostReverie' (package dirs have no spaces)."""
    return re.sub(r"[^A-Za-z0-9]", "", name)


def known_manifests() -> list[tuple[Path, dict]]:
    """Every manifest whose identity claims are already spoken for: the
    top-level DC.*/pack.json packages plus the nested ExamplePack (its AutoId
    block is a pinned reference vector)."""
    out = gd.other_pack_manifests()
    if EXAMPLE_PACK_JSON.is_file():
        try:
            out.append((EXAMPLE_PACK_JSON,
                        json.loads(EXAMPLE_PACK_JSON.read_text(encoding="utf-8"))))
        except (OSError, json.JSONDecodeError):
            pass
    return out


def starter_card_name(pack_name: str, taken_lower: set[str]) -> str | None:
    for suffix in ("Strike", "Opening Strike", "First Strike"):
        candidate = f"{pack_name} {suffix}"
        if candidate.lower() not in taken_lower:
            return candidate
    return None


def starter_card(name: str, card_id: int) -> dict:
    """A valid, on-curve starter: 1-energy common melee for 4 damage —
    CARD-PACK-SPEC §4's cost-1 common median. Meant to be edited, not shipped."""
    return {
        "name": name,
        "cardID": card_id,
        "expansion": "Extended",
        "type": "Melee",
        "category": "Action",
        "suffix": "None",
        "rarity": "Common",
        "cost": {"STR": 1},
        "description": "Deal 4 damage.",
        "keywords": [],
        "cardKeywords": [],
        "utilityNumber": "",
        "charges": 0,
        "flags": [],
        "playConditions": [],
        "effects": [
            {
                "trigger": "PlayAction",
                "codeLine": "damage:4",
                "forecast": True,
                "referenceStatus": None,
                "referenceCards": [],
                "hideReferenceCards": False,
                "conditions": [],
            }
        ],
        "enchantment": None,
        "art": "",
        "meta": {
            "archetype": "starter scaffold (replace me)",
            "nearestExisting": "Bash.json",
            "whyDifferent": "Deliberately not different yet - this is the "
                            "scaffolder's on-curve hello card (cost-1 common, "
                            "4 damage, the pool median). Reshape it into your "
                            "pack's first real card and write the uniqueness "
                            "argument here (CARD-PACK-SPEC §5).",
            "intendedDeck": "None yet - describe how the finished card plays.",
        },
    }


DESIGN_NOTES_TEMPLATE = """# {pack} — design notes

**Cluster:** <archetype cluster — see GAME-MECHANICS Part VI for the synergy web>.
**ID block:** {low}–{high} (AutoId; cards bottom-up from {low}; weapons and
starting cards share one top-down counter from {high}; weapon-power talentIDs
reuse the top-down numbers in the separate Talent namespace). **Colors:** <cost pools = frame colors; keep the archetype's
color identity coherent (DEX green, INT blue, STR red, HOLY gold, hybrids)>.

**Pack thesis.** <2-3 paragraphs: which verified mechanics (GAME-MECHANICS
Parts I-VII) this pack treats as design material, and what gap in the shipped
pool it fills. Cite timing facts, not vibes.>

Rarity mix target ≈ 3-4 C / 2-3 U / 2-3 R / 1 L per 10 cards; cost curve skews
low (pool is 72% cost 0-1) — CARD-PACK-SPEC §4.

---

## Card-by-card

### {low} {starter} — C, STR 1, Melee/Action
- **Role:** <what job the card does in a deck>.
- **Deck:** <which shell wants it; setup/payoff partners>.
- **Budget:** <math vs the §4 baselines: ~4 dmg per energy at common; each
  rarity step buys ~+1 damage-equivalent or one rider>.
- **Nearest:** <closest shipped card, from the nearest-neighbor audit>.
  **Difference:** <the new decision it introduces — CARD-PACK-SPEC §5>.
- **DSL:** <commands/triggers/conditions used, each verified against a shipped
  card that does the same>.

## Weapons, weapon powers & starting cards (optional — WEAPON-SPEC.md)

<Starting weapons (BasicAttack cards, IDs top-down), weapon powers (tier-0
talents) and starting cards (normal cards + classes, IDs continuing the
top-down counter) if the pack ships any. Same per-item structure as above.>

## Open questions

- <decisions deferred to review>
"""


def build_pack_dir(pack_dir: Path, pack_name: str, collapsed: str,
                   block: int, card_name: str) -> Path:
    pack_dir.mkdir(parents=True)
    (pack_dir / "art").mkdir()

    schema_rel = Path("../schemas/pack.schema.json")
    try:
        import os
        schema_rel = Path(os.path.relpath(
            gd.REPO_DIR / "schemas" / "pack.schema.json", pack_dir))
    except ValueError:
        pass  # different drive — keep the repo-layout default
    manifest = {
        "$schema": schema_rel.as_posix(),
        "schemaVersion": 1,
        "pack": pack_name,
        "idBlock": [block, block + gd.AUTOID_BLOCK_SIZE - 1],
        "cards": [starter_card(card_name, block)],
    }
    pack_json = pack_dir / "pack.json"
    pack_json.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")

    recipes = {
        "pack": collapsed,
        "finisher": [{"op": "vignette", "strength": 0.25}],
        "cards": {},
    }
    (pack_dir / "art-recipes.json").write_text(
        json.dumps(recipes, indent=2) + "\n", encoding="utf-8")

    (pack_dir / "DESIGN-NOTES.md").write_text(
        DESIGN_NOTES_TEMPLATE.format(pack=pack_name, low=block,
                                     high=block + gd.AUTOID_BLOCK_SIZE - 1,
                                     starter=card_name),
        encoding="utf-8")
    return pack_json


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("name", help='pack display name, e.g. "Frost Reverie" '
                                 "(spaces allowed; names the in-game set row)")
    ap.add_argument("--author", required=True,
                    help="author name — half of the AutoId owner pair; "
                         "changing it later changes the ID block")
    ap.add_argument("--root", default=str(gd.PACKS_DIR),
                    help="where to create DC.<Name>/ (default: repo root; "
                         "mainly for tests)")
    args = ap.parse_args(argv)

    pack_name = args.name.strip()
    collapsed = collapse(pack_name)
    if not pack_name or not collapsed:
        print(f"[ERROR] pack name {args.name!r} has no usable characters")
        return 1
    if not args.author.strip():
        print("[ERROR] --author must not be empty")
        return 1

    pack_dir = Path(args.root) / f"DC.{collapsed}"
    if pack_dir.exists():
        print(f"[ERROR] {pack_dir} already exists — refusing to touch it")
        return 1

    # --- AutoId block + provenance
    owner = gd.autoid_owner_string(args.author, pack_name)
    block = gd.autoid_block(args.author, pack_name)
    high = block + gd.AUTOID_BLOCK_SIZE - 1
    print(f"AutoId block: [{block}, {high}]")
    print(f"  formula:  700,000,000 + (FNV1a32(owner) % 1,000,000) x 100   "
          "(SPEC.md §4.3; python twin: tools/gamedata.py autoid_block, "
          "pinned by tools/tests/test_autoid.py + the engine boot self-check)")
    print(f"  owner:    {owner!r}  (lower(author + '/' + pack))")
    print(f"  FNV1a32:  {gd.fnv1a32(owner)}")
    print(f"  set value: {gd.autoid_set_value(block)}  "
          "(synthetic CardExpansions: 1000 + (block - 700,000,000) / 100)")

    # --- collision refusal vs every known manifest
    taken_names: set[str] = set(gd.pool_card_names_lower())
    for path, sib in known_manifests():
        sib_label = sib.get("pack", path.parent.name)
        if isinstance(sib.get("pack"), str) and sib["pack"].lower() == pack_name.lower():
            print(f"[ERROR] pack name {pack_name!r} is already taken by {path}")
            return 1
        sib_block = sib.get("idBlock")
        if (isinstance(sib_block, list) and len(sib_block) == 2
                and all(isinstance(x, int) for x in sib_block)
                and block <= sib_block[1] and sib_block[0] <= high):
            print(f"[ERROR] AutoId block [{block}, {high}] collides with pack "
                  f"{sib_label!r} ({path}, idBlock {sib_block}).\n"
                  "        Pick a different pack name or --author (the block is "
                  "derived from that pair), or allocate an explicit block in "
                  "docs/ID-REGISTRY.md and edit pack.json by hand.")
            return 1
        for c in (list(sib.get("cards") or []) + list(sib.get("weapons") or [])
                  + list(sib.get("startingCards") or [])):
            if isinstance(c, dict) and isinstance(c.get("name"), str):
                taken_names.add(c["name"].lower())

    card_name = starter_card_name(pack_name, taken_names)
    if card_name is None:
        print(f"[ERROR] could not derive a collision-free starter card name "
              f"from {pack_name!r} — every candidate is taken")
        return 1

    # --- write the skeleton
    pack_json = build_pack_dir(pack_dir, pack_name, collapsed, block, card_name)
    print(f"\ncreated {pack_dir}")
    for f in ("pack.json", "art-recipes.json", "DESIGN-NOTES.md", "art/"):
        print(f"  {f}")

    # --- self-check: the scaffold must pass gate 1 immediately
    print("\nself-check (tools/validate_pack.py):")
    rc = validate_pack.run_file(pack_json, strict=False)
    if rc != 0:
        print("[ERROR] scaffold failed its own validation — this is a "
              "new_pack.py bug; files left in place for inspection")
        return 1

    print(f"""
next steps:
  1. edit {pack_json} — reshape {card_name!r} into your first real card
     (docs/TUTORIAL.md walks through it; your editor autocompletes via $schema)
  2. python tools/dmk.py validate {pack_json}
  3. python tools/dmk.py sim {pack_json}
  4. repo packs only: allocate the block in docs/ID-REGISTRY.md (nothing is
     registered automatically)""")
    return 0


if __name__ == "__main__":
    sys.exit(main())
