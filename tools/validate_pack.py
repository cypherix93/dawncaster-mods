"""Gate 1: static validation of a card-pack manifest against CARD-PACK-SPEC.md §2.

Checks (all vocabulary comes from the decompiled enums hardcoded in tools/gamedata.py
and the extracted pool in tools/out/data/):
  - manifest + per-card JSON shape (required fields, list fields non-null)
  - enum spellings: type/category/suffix/rarity/expansion/keywords/flags,
    effect triggers, condition values + operators (exact, incl. canonical typos)
  - every codeLine command exists in reference/effect-commands.txt
  - referenceStatus resolves to one of the 49 shipped StatusEffect assets;
    referenceCards resolve to pool cards or cards in this pack
  - cardID inside the declared idBlock, block inside the mod range
    700,000,000-799,999,999, collision-free vs the 2,525 extracted cards AND
    vs every other packs/*/pack.json
  - name collision-free (case-insensitive) vs pool and other packs
  - player-pool legality (expansion, rarity, suffix — ProcessCard filter)
  - meta.nearestExisting references a real extracted card
  - cost/color coherence + power-shape warnings; art presence/512x512 RGBA check

Exit code 1 on any ERROR (warnings alone exit 0; --strict makes warnings fatal).

Usage:
    python tools/validate_pack.py packs/<Pack>/pack.json
    python tools/validate_pack.py --all
"""

from __future__ import annotations

import argparse
import json
import struct
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import gamedata as gd  # noqa: E402


def _finding(level: str, card: str, check: str, msg: str) -> dict:
    return {"level": level, "card": card, "check": check, "msg": msg}


def _png_info(path: Path) -> tuple[int, int, int] | None:
    """(width, height, colortype) from a PNG's IHDR, or None if not a PNG."""
    try:
        with open(path, "rb") as f:
            head = f.read(33)
    except OSError:
        return None
    if len(head) < 33 or head[:8] != b"\x89PNG\r\n\x1a\n" or head[12:16] != b"IHDR":
        return None
    w, h = struct.unpack(">II", head[16:24])
    return w, h, head[25]


def _validate_conditions(conds, where: str, err, warn) -> None:
    if not isinstance(conds, list):
        err("shape", f"{where} must be a list")
        return
    for c in conds:
        if not isinstance(c, dict):
            err("shape", f"{where}: each condition must be an object")
            continue
        val = c.get("value")
        if val not in gd.CONDITION_VALUES:
            err("bad_condition", f"{where}: unknown ConditionValue {val!r}")
        op = c.get("op")
        if op not in gd.CONDITION_OPERATORS:
            err("bad_condition", f"{where}: unknown ConditionOperator {op!r}")
        if "target" in c and not isinstance(c["target"], str):
            err("bad_condition", f"{where}: condition target must be a string")


def _validate_effect(eff, where: str, pack_names_lower: set[str], err, warn) -> None:
    commands = gd.effect_commands()
    statuses = gd.status_names()
    if not isinstance(eff, dict):
        err("shape", f"{where}: effect must be an object")
        return
    trig = eff.get("trigger")
    if trig not in gd.GAME_TRIGGERS:
        err("bad_trigger", f"{where}: unknown GameTriggers member {trig!r}")
    code = eff.get("codeLine")
    if not isinstance(code, str) or not code.strip():
        err("empty_codeline", f"{where}: codeLine missing or empty")
    else:
        for stmt in gd.parse_statements(code):
            cmd = gd.statement_command(stmt)
            if cmd in commands:
                continue
            hint = ""
            lower_hits = [c for c in commands if c.lower() == cmd.lower()]
            if lower_hits:
                hint = f" (did you mean {lower_hits[0]!r}? commands are case-sensitive)"
            err("unknown_command",
                f"{where}: command {cmd!r} not in reference/effect-commands.txt{hint}")
    ref_status = eff.get("referenceStatus")
    if ref_status is not None and ref_status not in statuses:
        err("bad_reference_status",
            f"{where}: referenceStatus {ref_status!r} is not a shipped StatusEffect")
    if ref_status is None and isinstance(code, str):
        cmds = {gd.statement_command(s) for s in gd.parse_statements(code)}
        if cmds & {"inflict", "bless"}:
            warn("missing_reference_status",
                 f"{where}: inflict/bless without referenceStatus applies nothing "
                 "(shipped idiom wires the status via referenceStatus)")
    for rc in eff.get("referenceCards") or []:
        if not isinstance(rc, str) or (
                rc.lower() not in gd.pool_card_names_lower()
                and rc.lower() not in pack_names_lower):
            err("bad_reference_card",
                f"{where}: referenceCards entry {rc!r} is not a pool card or a card in this pack")
    _validate_conditions(eff.get("conditions", []), f"{where}.conditions", err, warn)


REQUIRED_CARD_FIELDS = ["name", "cardID", "expansion", "type", "category", "suffix",
                        "rarity", "cost", "description", "effects", "art", "meta"]
LIST_DEFAULT_FIELDS = ["keywords", "cardKeywords", "flags", "playConditions"]


def validate_card(card: dict, idx: int, pack_dir: Path, id_block, pack_names_lower,
                  findings: list[dict]) -> None:
    label = card.get("name") or f"cards[{idx}]"
    err = lambda check, msg: findings.append(_finding("ERROR", label, check, msg))   # noqa: E731
    warn = lambda check, msg: findings.append(_finding("WARN", label, check, msg))   # noqa: E731

    for f in REQUIRED_CARD_FIELDS:
        if f not in card:
            err("missing_field", f"required field {f!r} missing")
    for f in LIST_DEFAULT_FIELDS:
        if f in card and not isinstance(card[f], list):
            err("shape", f"{f} must be a list (spec: list fields ship non-null)")

    # --- enums
    if card.get("type") not in gd.CARD_TYPES:
        err("bad_enum", f"type {card.get('type')!r} not a CardType member")
    if card.get("category") not in gd.CARD_CATEGORIES:
        err("bad_enum", f"category {card.get('category')!r} not a CardCategory member")
    if card.get("suffix") not in gd.CARD_SUFFIXES:
        err("bad_enum", f"suffix {card.get('suffix')!r} not a Suffix member "
                        "(note the canonical spelling 'Phyisical')")
    if card.get("rarity") not in gd.CARD_RARITIES:
        err("bad_enum", f"rarity {card.get('rarity')!r} not a CardRariry member")
    if card.get("expansion") not in gd.CARD_EXPANSIONS:
        err("bad_enum", f"expansion {card.get('expansion')!r} not a CardExpansions member")

    # --- player-pool legality (AssetManager.ProcessCard filter)
    if card.get("expansion") in ("None", "Metaprogress"):
        err("pool_illegal", "expansion None/Metaprogress is filtered from the player pool")
    if card.get("rarity") == "Monster":
        err("pool_illegal", "rarity Monster is never legal for player cards")
    if card.get("suffix") == "Companion":
        err("pool_illegal", "suffix Companion routes to the companion deck, not the player pool")
    if card.get("rarity") in ("Epic", "Mythic", "Ultra"):
        warn("odd_rarity", f"rarity {card['rarity']} is outside the reward ladder — "
                           "reachable only by forced placement (GAME-MECHANICS Part VIII)")

    for kw in card.get("keywords") or []:
        if kw not in gd.CARD_PROPERTIES:
            err("bad_keyword", f"keyword {kw!r} not a CardProperties member")
    for fl in card.get("flags") or []:
        if fl not in gd.CARD_FLAGS:
            err("bad_flag", f"flag {fl!r} not in the spec flag set")
    for ck in card.get("cardKeywords") or []:
        if not isinstance(ck, str):
            err("shape", "cardKeywords entries must be strings")
        elif ck != ck.lower():
            warn("cardkeyword_case", f"cardKeywords {ck!r}: shipped tags are lowercase")

    # --- cost
    cost = card.get("cost")
    total_cost = 0
    colored_pools = 0
    if not isinstance(cost, dict):
        err("shape", "cost must be an object of pool -> amount")
    else:
        for k, v in cost.items():
            if k not in gd.COST_KEYS:
                err("bad_cost_key", f"cost key {k!r} not one of {gd.COST_KEYS}")
            elif not isinstance(v, int) or v < 0:
                err("bad_cost_value", f"cost[{k}] must be a non-negative integer")
        total_cost = sum(v for k, v in cost.items()
                         if k in gd.COST_KEYS and k != "Life" and isinstance(v, int))
        colored_pools = sum(1 for k in ("DEX", "INT", "STR", "HOLY", "DEXINT",
                                        "DEXSTR", "INTSTR")
                            if isinstance(cost.get(k), int) and cost[k] > 0)
        if colored_pools >= 3:
            warn("color_muddy", f"{colored_pools} colored cost pools — frame color/"
                                "identity gets incoherent (Card.GetColor rules)")
        if total_cost > 4:
            warn("cost_outlier", f"total energy cost {total_cost} exceeds the shipped "
                                 "pool maximum of 4")
        if total_cost == 0 and cost.get("Life", 0) == 0 and card.get("rarity") == "Common":
            pass  # 0-cost commons exist (15% of pool); no finding

    # --- identity
    cid = card.get("cardID")
    if not isinstance(cid, int):
        err("bad_id", "cardID must be an integer")
    else:
        if id_block and not (id_block[0] <= cid <= id_block[1]):
            err("id_outside_block", f"cardID {cid} outside declared idBlock {id_block}")
        if not (gd.MOD_ID_RANGE[0] <= cid <= gd.MOD_ID_RANGE[1]):
            err("id_outside_mod_range",
                f"cardID {cid} outside the mod range {gd.MOD_ID_RANGE}")
        if cid in gd.pool_card_ids():
            err("id_collision", f"cardID {cid} collides with a shipped card")

    name = card.get("name")
    if isinstance(name, str):
        if name.lower() in gd.pool_card_names_lower():
            err("name_collision",
                f"name {name!r} collides (case-insensitive) with a shipped card")
    else:
        err("shape", "name must be a string")

    if not isinstance(card.get("description"), str) or not card.get("description", "").strip():
        err("empty_description", "description missing or empty")

    # --- effects & conditions
    effects = card.get("effects")
    if not isinstance(effects, list) or not effects:
        err("no_effects", "effects must be a non-empty list")
    else:
        for i, eff in enumerate(effects):
            _validate_effect(eff, f"effects[{i}]", pack_names_lower,
                             err, warn)
    _validate_conditions(card.get("playConditions", []), "playConditions", err, warn)

    ench = card.get("enchantment")
    if ench is not None:
        if not isinstance(ench, dict):
            err("shape", "enchantment must be null or an object")
        else:
            if ench.get("type") not in gd.ENCHANT_TYPES:
                warn("bad_enchant_type",
                     f"enchantment.type {ench.get('type')!r} not a known EffectType name")
            for i, eff in enumerate(ench.get("effects") or []):
                _validate_effect(eff, f"enchantment.effects[{i}]",
                                 pack_names_lower, err, warn)

    # --- meta (review-gate payload)
    meta = card.get("meta")
    if isinstance(meta, dict):
        nearest = meta.get("nearestExisting")
        if not nearest:
            err("meta_missing", "meta.nearestExisting is required (uniqueness gate)")
        elif not gd.resolve_pool_reference(nearest):
            err("meta_bad_reference",
                f"meta.nearestExisting {nearest!r} does not resolve to an extracted card")
        for f in ("archetype", "whyDifferent"):
            if not meta.get(f):
                warn("meta_incomplete", f"meta.{f} empty — reviewers need it")
    elif "meta" in card:
        err("shape", "meta must be an object")

    # --- art
    art = card.get("art")
    if isinstance(art, str) and art:
        art_path = pack_dir / art
        if not art_path.is_file():
            warn("art_missing", f"art file {art} not found (required before ship)")
        else:
            info = _png_info(art_path)
            if info is None:
                warn("art_not_png", f"art file {art} is not a PNG")
            elif (info[0], info[1]) != (512, 512):
                warn("art_dimensions", f"art is {info[0]}x{info[1]}, spec wants 512x512")
            elif info[2] != 6:
                warn("art_not_rgba", f"art PNG colortype {info[2]}, spec wants RGBA (6)")


def validate_pack(manifest: dict, pack_path: Path) -> list[dict]:
    findings: list[dict] = []
    perr = lambda check, msg: findings.append(_finding("ERROR", "<pack>", check, msg))  # noqa: E731
    pwarn = lambda check, msg: findings.append(_finding("WARN", "<pack>", check, msg))  # noqa: E731
    pack_dir = pack_path.parent

    pack_name = manifest.get("pack")
    if not isinstance(pack_name, str) or not pack_name:
        perr("missing_field", "pack name missing")

    id_block = manifest.get("idBlock")
    if (not isinstance(id_block, list) or len(id_block) != 2
            or not all(isinstance(x, int) for x in id_block) or id_block[0] > id_block[1]):
        perr("bad_id_block", "idBlock must be [low, high] integers")
        id_block = None
    elif not (gd.MOD_ID_RANGE[0] <= id_block[0] and id_block[1] <= gd.MOD_ID_RANGE[1]):
        perr("bad_id_block", f"idBlock {id_block} outside mod range {gd.MOD_ID_RANGE}")

    cards = manifest.get("cards")
    if not isinstance(cards, list) or not cards:
        perr("no_cards", "cards must be a non-empty list")
        return findings

    # cross-pack collision surfaces
    sibling_ids: dict[int, str] = {}
    sibling_names: dict[str, str] = {}
    for sib_path, sib in gd.other_pack_manifests(exclude=pack_path):
        sib_name = sib.get("pack", sib_path.parent.name)
        for c in sib.get("cards", []):
            if isinstance(c.get("cardID"), int):
                sibling_ids.setdefault(c["cardID"], sib_name)
            if isinstance(c.get("name"), str):
                sibling_names.setdefault(c["name"].lower(), sib_name)

    pack_names_lower = {c["name"].lower() for c in cards
                        if isinstance(c, dict) and isinstance(c.get("name"), str)}

    seen_ids: set[int] = set()
    seen_names: set[str] = set()
    rarity_counts: dict[str, int] = {}
    cost_counts: dict[int, int] = {}
    for i, card in enumerate(cards):
        if not isinstance(card, dict):
            perr("shape", f"cards[{i}] is not an object")
            continue
        label = card.get("name") or f"cards[{i}]"
        validate_card(card, i, pack_dir, id_block, pack_names_lower, findings)

        cid = card.get("cardID")
        if isinstance(cid, int):
            if cid in seen_ids:
                findings.append(_finding("ERROR", label, "id_collision",
                                         f"cardID {cid} duplicated inside the pack"))
            if cid in sibling_ids:
                findings.append(_finding("ERROR", label, "id_collision",
                                         f"cardID {cid} collides with pack {sibling_ids[cid]!r}"))
            seen_ids.add(cid)
        nm = card.get("name")
        if isinstance(nm, str):
            if nm.lower() in seen_names:
                findings.append(_finding("ERROR", label, "name_collision",
                                         "name duplicated inside the pack"))
            if nm.lower() in sibling_names:
                findings.append(_finding("ERROR", label, "name_collision",
                                         f"name collides with pack {sibling_names[nm.lower()]!r}"))
            seen_names.add(nm.lower())

        rarity_counts[card.get("rarity", "?")] = rarity_counts.get(card.get("rarity", "?"), 0) + 1
        cost = card.get("cost") or {}
        if isinstance(cost, dict):
            tc = sum(v for k, v in cost.items()
                     if k in gd.COST_KEYS and k != "Life" and isinstance(v, int))
            cost_counts[tc] = cost_counts.get(tc, 0) + 1

    # pack-shape advisories (CARD-PACK-SPEC §4: 15/57/16/11/2 cost skew, 35/22/25/19 rarity)
    n = len(cards)
    if n:
        low_cost = sum(v for k, v in cost_counts.items() if k <= 1)
        if low_cost / n < 0.5:
            pwarn("cost_skew", f"only {low_cost}/{n} cards at cost 0-1; the pool is 72% "
                               "cost 0-1 — this is a low-cost, high-tempo game")
        commons = rarity_counts.get("Common", 0)
        if n >= 8 and commons / n < 0.2:
            pwarn("rarity_skew", f"only {commons}/{n} commons; pool rarity mix is "
                                 "~35/22/25/19 C/U/R/L")
    return findings


def run_file(pack_path: Path, strict: bool) -> int:
    try:
        manifest = json.loads(pack_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as e:
        print(f"[ERROR] <pack> load_failure: cannot parse {pack_path}: {e}")
        return 1
    findings = validate_pack(manifest, pack_path)
    errors = [f for f in findings if f["level"] == "ERROR"]
    warns = [f for f in findings if f["level"] == "WARN"]
    print(f"== {pack_path} — {len(errors)} error(s), {len(warns)} warning(s)")
    for f in findings:
        print(f"[{f['level']}] {f['card']} :: {f['check']} :: {f['msg']}")
    if errors or (strict and warns):
        return 1
    return 0


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("pack", nargs="?", help="path to packs/<Pack>/pack.json")
    ap.add_argument("--all", action="store_true", help="validate every packs/*/pack.json")
    ap.add_argument("--strict", action="store_true", help="warnings are fatal")
    args = ap.parse_args(argv)

    if args.all:
        paths = sorted(gd.PACKS_DIR.glob("*/pack.json"))
        if not paths:
            print("no packs/*/pack.json found")
            return 0
    elif args.pack:
        paths = [Path(args.pack)]
    else:
        ap.error("give a pack.json path or --all")

    rc = 0
    for p in paths:
        rc |= run_file(p, args.strict)
    return rc


if __name__ == "__main__":
    sys.exit(main())
