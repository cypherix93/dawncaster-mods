"""Art gate: shipping checks + perceptual distinctness (ART-MUTATION-SPEC.md §6).

Shipping checks (always):
  - every card in packs/<Pack>/pack.json has packs/<Pack>/art/<CardName>.png
    (missing-art report), exactly 512×512 RGBA, file < 600 KB
  - no stray files in art/ that don't match a card name
  - §2 source budget from packs/*/art-recipes.json: a sprite key backing more
    than 2 mod cards across all packs, or 2 in one pack, is an ERROR

--distinctness (perceptual-hash gate, Pillow-only):
  Hashes are 16×16 dHash + aHash computed on the **HSV value plane**
  (V = max(R,G,B)) of each image, NOT on luminance. Rationale: our `hue_rotate`
  op preserves V exactly, so a pure recolor collapses to ~0 hash distance —
  which is precisely what the gate must reject — while composition changes
  (mirror / zoom / rotate / composite) move V structure and score high.
  Luminance-based hashes were tried first and could NOT separate the classes
  (hue-rotate shifts luminance: fail-class max 118/512 overlapped pass-class
  min 12/512).

  Calibration (2026-07, Pillow 12.2, five real card sprites CARDART_4_15,
  CARDART_1_0, CARDART_2_10, CARDART_3_33, CARDART_5_7; see git history for
  the harness):
    - pure recolors (hue_rotate ±30..180, +sat_value, +vignette, +color_wash):
      dHash ≤ 12, dHash+aHash ≤ 28   → must FAIL
    - mirror/zoom/rotate + recolor combos on asymmetric art:
      dHash ≥ 33, dHash+aHash ≥ 38   → must PASS
    - unrelated sprite pairs: sum ≥ 141
    - same source, two different composition recipes: sum ≥ 64
  Chosen thresholds (mid-gap):
    vs source:   PASS iff dHash ≥ 16 AND dHash+aHash ≥ 33
    vs siblings: PASS iff dHash+aHash ≥ 40   (each pair of mod-card outputs)
  Note: on near-mirror-symmetric sources a bare `mirror` scores ~0 — the gate
  intentionally fails those recipes; use zoom/rotate/composite instead.

Usage:
    python tools/validate_art.py --pack EmberweaveGrove [--distinctness]
    python tools/validate_art.py --all --distinctness

Exit code 1 on any ERROR (warnings alone exit 0).
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from PIL import Image

TOOLS_DIR = Path(__file__).resolve().parent
REPO_DIR = TOOLS_DIR.parent
PACKS_DIR = REPO_DIR / "packs"
SPRITE_INDEX = TOOLS_DIR / "out" / "sprite-index.json"
SPRITES_BASE = TOOLS_DIR / "out"

ART_SIZE = (512, 512)
MAX_BYTES = 600 * 1024
HASH_SIZE = 16

# calibrated thresholds — see module docstring before changing
SOURCE_MIN_DHASH = 16
SOURCE_MIN_SUM = 33
SIBLING_MIN_SUM = 40

# §2 source budget
MAX_SOURCE_USES_GLOBAL = 2
MAX_SOURCE_USES_PER_PACK = 1


# --------------------------------------------------------------------------- hashes

def _vplane(img: Image.Image) -> Image.Image:
    """HSV value plane (max of RGB) — invariant under pure hue rotation."""
    return img.convert("RGB").convert("HSV").split()[2]


def _pixels(img: Image.Image) -> list[int]:
    # Pillow 12 deprecates getdata; keep compat with older Pillow
    if hasattr(img, "get_flattened_data"):
        return list(img.get_flattened_data())
    return list(img.getdata())


def ahash(img: Image.Image, size: int = HASH_SIZE) -> int:
    """Average hash of the V plane, size×size bits."""
    g = _vplane(img).resize((size, size), Image.Resampling.LANCZOS)
    px = _pixels(g)
    avg = sum(px) / len(px)
    bits = 0
    for p in px:
        bits = (bits << 1) | (1 if p > avg else 0)
    return bits


def dhash(img: Image.Image, size: int = HASH_SIZE) -> int:
    """Horizontal-gradient hash of the V plane, size×size bits."""
    g = _vplane(img).resize((size + 1, size), Image.Resampling.LANCZOS)
    px = _pixels(g)
    bits = 0
    for row in range(size):
        base = row * (size + 1)
        for col in range(size):
            bits = (bits << 1) | (1 if px[base + col] > px[base + col + 1] else 0)
    return bits


def hamming(a: int, b: int) -> int:
    return (a ^ b).bit_count()


def art_hashes(img: Image.Image) -> tuple[int, int]:
    return dhash(img), ahash(img)


def hash_distance(ha: tuple[int, int], hb: tuple[int, int]) -> tuple[int, int]:
    """(dHash distance, aHash distance) between two art_hashes() tuples."""
    return hamming(ha[0], hb[0]), hamming(ha[1], hb[1])


# --------------------------------------------------------------------------- checks

def _finding(level: str, pack: str, card: str, check: str, msg: str) -> dict:
    return {"level": level, "pack": pack, "card": card, "check": check, "msg": msg}


def _load_json(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))


def _card_names(pack_json: dict) -> list[str]:
    return [c["name"] for c in pack_json.get("cards", [])
            if isinstance(c, dict) and isinstance(c.get("name"), str)]


def _open_image(path: Path) -> Image.Image:
    img = Image.open(path)
    img.load()
    return img


def check_pack_files(pack_dir: Path, pack_json: dict, findings: list[dict]) -> None:
    """Dimensions/format/size/missing/stray checks for one pack."""
    pack = pack_dir.name
    art_dir = pack_dir / "art"
    names = _card_names(pack_json)

    for name in names:
        p = art_dir / f"{name}.png"
        if not p.is_file():
            findings.append(_finding("ERROR", pack, name, "art_missing",
                                     f"no art file {p.name} in {pack}/art/"))
            continue
        size = p.stat().st_size
        if size >= MAX_BYTES:
            findings.append(_finding("ERROR", pack, name, "art_too_big",
                                     f"{size} bytes ≥ {MAX_BYTES} limit"))
        try:
            img = _open_image(p)
        except OSError as e:
            findings.append(_finding("ERROR", pack, name, "art_unreadable",
                                     f"cannot decode {p.name}: {e}"))
            continue
        if img.format != "PNG":
            findings.append(_finding("ERROR", pack, name, "art_not_png",
                                     f"format is {img.format}, must be PNG"))
        if img.size != ART_SIZE:
            findings.append(_finding("ERROR", pack, name, "art_dimensions",
                                     f"{img.size[0]}x{img.size[1]}, must be 512x512"))
        if img.mode != "RGBA":
            findings.append(_finding("ERROR", pack, name, "art_not_rgba",
                                     f"mode is {img.mode}, must be RGBA"))

    if art_dir.is_dir():
        expected = {f"{n}.png" for n in names}
        for f in sorted(art_dir.iterdir()):
            if f.name not in expected:
                findings.append(_finding("ERROR", pack, f.name, "art_stray",
                                         f"file in {pack}/art/ matches no card name"))


def check_source_budget(recipes_by_pack: dict[str, dict],
                        findings: list[dict]) -> None:
    """§2: a sprite may back ≤2 mod cards globally and ≤1 per pack."""
    usage: dict[str, list[tuple[str, str]]] = {}
    for pack, recipes in recipes_by_pack.items():
        for card, rec in sorted((recipes.get("cards") or {}).items()):
            if not isinstance(rec, dict):
                continue
            src = rec.get("source")
            keys = src if isinstance(src, list) else [src]
            for k in keys:
                if isinstance(k, str):
                    usage.setdefault(k, []).append((pack, card))
    for key, users in sorted(usage.items()):
        if len(users) > MAX_SOURCE_USES_GLOBAL:
            listing = ", ".join(f"{p}/{c}" for p, c in users)
            findings.append(_finding("ERROR", "<all>", key, "source_overuse",
                                     f"sprite backs {len(users)} mod cards "
                                     f"(max {MAX_SOURCE_USES_GLOBAL}): {listing}"))
        by_pack: dict[str, list[str]] = {}
        for p, c in users:
            by_pack.setdefault(p, []).append(c)
        for p, cards in sorted(by_pack.items()):
            if len(cards) > MAX_SOURCE_USES_PER_PACK:
                findings.append(_finding("ERROR", p, key, "source_overuse_pack",
                                         f"sprite backs {len(cards)} cards in one "
                                         f"pack: {', '.join(cards)}"))


def check_distinctness(scope_packs: list[Path], packs_dir: Path,
                       recipes_by_pack: dict[str, dict], sprite_index: dict,
                       sprites_base: Path, findings: list[dict]) -> None:
    """dHash+aHash gate: each output vs its source sprite(s) + vs every other
    mod-card output (across ALL packs, not just the validated scope)."""
    hash_cache: dict[Path, tuple[int, int]] = {}

    def hashes_of(path: Path) -> tuple[int, int] | None:
        if path not in hash_cache:
            try:
                hash_cache[path] = art_hashes(_open_image(path))
            except OSError:
                return None
        return hash_cache[path]

    scope_names = {p.name for p in scope_packs}

    # gather every existing mod-card output for the sibling sweep
    outputs: list[tuple[str, str, Path]] = []
    for pack_dir in sorted(p for p in packs_dir.iterdir() if p.is_dir()):
        pj = pack_dir / "pack.json"
        if not pj.is_file():
            continue
        try:
            names = _card_names(_load_json(pj))
        except (OSError, json.JSONDecodeError):
            continue
        for name in names:
            p = pack_dir / "art" / f"{name}.png"
            if p.is_file():
                outputs.append((pack_dir.name, name, p))

    # (a) output vs its recipe source(s)
    for pack, name, path in outputs:
        if pack not in scope_names:
            continue
        recipes = recipes_by_pack.get(pack)
        rec = (recipes or {}).get("cards", {}).get(name)
        if not isinstance(rec, dict):
            if recipes is not None:
                findings.append(_finding("WARN", pack, name, "no_recipe",
                                         "art exists but no recipe — distinctness "
                                         "vs source not checkable"))
            continue
        out_h = hashes_of(path)
        if out_h is None:
            continue  # unreadable already reported by file checks
        src = rec.get("source")
        keys = src if isinstance(src, list) else [src]
        for key in keys:
            entry = sprite_index.get(key) if isinstance(key, str) else None
            if entry is None:
                findings.append(_finding("ERROR", pack, name, "bad_source_key",
                                         f"recipe source {key!r} not in sprite index"))
                continue
            sp = sprites_base / entry["file"]
            src_h = hashes_of(sp)
            if src_h is None:
                findings.append(_finding("ERROR", pack, name, "source_unreadable",
                                         f"cannot read source sprite {sp}"))
                continue
            d, a = hash_distance(out_h, src_h)
            if d < SOURCE_MIN_DHASH or d + a < SOURCE_MIN_SUM:
                findings.append(_finding(
                    "ERROR", pack, name, "not_distinct_from_source",
                    f"vs {key}: dHash {d} (need ≥{SOURCE_MIN_DHASH}), "
                    f"dHash+aHash {d + a} (need ≥{SOURCE_MIN_SUM}) — "
                    "pure recolor territory; add composition ops "
                    "(mirror/zoom/rotate/composite)"))

    # (b) pairwise siblings — report a pair once, when either side is in scope
    for i in range(len(outputs)):
        for j in range(i + 1, len(outputs)):
            pa, ca, fa = outputs[i]
            pb, cb, fb = outputs[j]
            if pa not in scope_names and pb not in scope_names:
                continue
            ha, hb = hashes_of(fa), hashes_of(fb)
            if ha is None or hb is None:
                continue
            d, a = hash_distance(ha, hb)
            if d + a < SIBLING_MIN_SUM:
                findings.append(_finding(
                    "ERROR", pa, ca, "not_distinct_from_sibling",
                    f"vs {pb}/{cb}: dHash+aHash {d + a} "
                    f"(need ≥{SIBLING_MIN_SUM}) — the two arts read as twins"))


# --------------------------------------------------------------------------- driver

def validate(scope_packs: list[Path], packs_dir: Path = PACKS_DIR,
             sprite_index_path: Path = SPRITE_INDEX,
             sprites_base: Path = SPRITES_BASE,
             distinctness: bool = False) -> list[dict]:
    findings: list[dict] = []

    # recipes across ALL packs (source budget is a global constraint)
    recipes_by_pack: dict[str, dict] = {}
    for pack_dir in sorted(p for p in packs_dir.iterdir() if p.is_dir()):
        rp = pack_dir / "art-recipes.json"
        if not rp.is_file():
            continue
        try:
            recipes_by_pack[pack_dir.name] = _load_json(rp)
        except (OSError, json.JSONDecodeError) as e:
            findings.append(_finding("ERROR", pack_dir.name, "<recipes>",
                                     "recipes_unreadable", f"{rp.name}: {e}"))

    for pack_dir in scope_packs:
        pj = pack_dir / "pack.json"
        if not pj.is_file():
            findings.append(_finding("ERROR", pack_dir.name, "<pack>",
                                     "no_manifest", f"missing {pj}"))
            continue
        try:
            pack_json = _load_json(pj)
        except (OSError, json.JSONDecodeError) as e:
            findings.append(_finding("ERROR", pack_dir.name, "<pack>",
                                     "bad_manifest", f"{pj.name}: {e}"))
            continue
        check_pack_files(pack_dir, pack_json, findings)
        if pack_dir.name not in recipes_by_pack:
            findings.append(_finding("WARN", pack_dir.name, "<pack>", "no_recipes",
                                     "no art-recipes.json — distinctness and "
                                     "source-budget checks skipped for this pack"))

    check_source_budget(recipes_by_pack, findings)

    if distinctness:
        if not sprite_index_path.is_file():
            findings.append(_finding("ERROR", "<all>", "<sprites>", "no_sprite_index",
                                     f"{sprite_index_path} not found — run "
                                     "tools/extract_sprites.py"))
        else:
            sprite_index = _load_json(sprite_index_path)
            check_distinctness(scope_packs, packs_dir, recipes_by_pack,
                               sprite_index, sprites_base, findings)
    return findings


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--pack", help="pack dir name under packs/")
    ap.add_argument("--all", action="store_true", help="validate every pack")
    ap.add_argument("--distinctness", action="store_true",
                    help="run the perceptual-hash distinctness gate (§6)")
    args = ap.parse_args(argv)

    if args.all:
        scope = sorted(p for p in PACKS_DIR.iterdir()
                       if p.is_dir() and (p / "pack.json").is_file())
    elif args.pack:
        scope = [PACKS_DIR / args.pack]
    else:
        ap.error("give --pack <Name> or --all")

    findings = validate(scope, distinctness=args.distinctness)
    errors = [f for f in findings if f["level"] == "ERROR"]
    warns = [f for f in findings if f["level"] == "WARN"]
    for f in findings:
        print(f"[{f['level']}] {f['pack']}/{f['card']} :: {f['check']} :: {f['msg']}")
    print(f"== validate_art: {len(errors)} error(s), {len(warns)} warning(s)")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
