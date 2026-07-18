"""Recipe-driven card-art mutation engine (ART-MUTATION-SPEC.md §3, §5, §7).

Reads `packs/<Pack>/art-recipes.json`, resolves each card's `source` sprite key(s)
via `tools/out/sprite-index.json`, applies the op chain (tools/artmutate_ops.py)
plus the pack-level `finisher`, and writes `packs/<Pack>/art/<CardName>.png`
(512×512 RGBA, full bleed).

Deterministic: same recipe + same source bytes ⇒ byte-identical PNGs (fixed
encoder settings, no timestamps, seeded ops derive RNG from the card's `seed`).

Incremental: a `.build-state.json` next to each pack's art/ dir records a
fingerprint (engine version + card recipe + finisher + source bytes) per card;
unchanged cards are skipped unless --force.

Usage:
    python tools/artmutate.py build --pack EmberweaveGrove [--force]
    python tools/artmutate.py build --all [--force]
    python tools/artmutate.py preview --pack EmberweaveGrove --card Ashfeast
    python tools/artmutate.py preview --recipes some-recipes.json --card X [--out p.png]

Per-card failures (missing sprite key, bad op) are reported and the build
continues; the process exits nonzero if anything failed.
"""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import sys
import tempfile
from pathlib import Path

from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))
from artmutate_ops import OpError, apply_ops  # noqa: E402

TOOLS_DIR = Path(__file__).resolve().parent
REPO_DIR = TOOLS_DIR.parent
PACKS_DIR = REPO_DIR / "packs"
SPRITE_INDEX = TOOLS_DIR / "out" / "sprite-index.json"
SPRITES_BASE = TOOLS_DIR / "out"  # index 'file' entries are relative to this

ART_SIZE = (512, 512)
ENGINE_VERSION = 1  # bump to force-invalidate all build state
STATE_FILENAME = ".build-state.json"


class BuildError(RuntimeError):
    """Per-card build failure with a human-readable message."""


# --------------------------------------------------------------------------- IO

def load_sprite_index(path: Path = SPRITE_INDEX) -> dict:
    if not path.is_file():
        raise BuildError(
            f"sprite index {path} not found — run tools/extract_sprites.py first")
    return json.loads(path.read_text(encoding="utf-8"))


def resolve_source_paths(source, index: dict, base: Path = SPRITES_BASE) -> list[Path]:
    """Recipe `source` (key or list of keys) -> sprite file paths."""
    keys = source if isinstance(source, list) else [source]
    if not keys or not all(isinstance(k, str) for k in keys) or len(keys) > 2:
        raise BuildError(f"source must be a sprite key or a list of 2 keys, got {source!r}")
    paths = []
    for key in keys:
        entry = index.get(key)
        if entry is None:
            raise BuildError(f"sprite key {key!r} not in sprite-index.json")
        p = base / entry["file"]
        if not p.is_file():
            raise BuildError(f"sprite file missing for {key!r}: {p}")
        paths.append(p)
    return paths


def load_source_image(data: bytes) -> Image.Image:
    """Decode sprite bytes -> 512×512 RGBA canvas (center-square-crop + Lanczos)."""
    img = Image.open(io.BytesIO(data))
    img.load()
    img = img.convert("RGBA")
    if img.size != ART_SIZE:
        w, h = img.size
        side = min(w, h)
        left, top = (w - side) // 2, (h - side) // 2
        img = img.crop((left, top, left + side, top + side)).resize(
            ART_SIZE, Image.Resampling.LANCZOS)
    return img


def encode_png(img: Image.Image) -> bytes:
    """Fixed encoder settings so identical pixels ⇒ identical bytes."""
    buf = io.BytesIO()
    img.save(buf, format="PNG", optimize=False, compress_level=6)
    return buf.getvalue()


# --------------------------------------------------------------------------- recipes

def load_recipes(path: Path) -> dict:
    if not path.is_file():
        raise BuildError(f"recipe file not found: {path}")
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as e:
        raise BuildError(f"recipe file {path} is not valid JSON: {e}") from None
    if not isinstance(data, dict) or not isinstance(data.get("cards"), dict):
        raise BuildError(f"recipe file {path} must be an object with a 'cards' object")
    return data


def card_fingerprint(card_recipe: dict, finisher: list, source_bytes: list[bytes]) -> str:
    h = hashlib.sha256()
    h.update(f"artmutate-v{ENGINE_VERSION}".encode())
    h.update(json.dumps(card_recipe, sort_keys=True).encode())
    h.update(json.dumps(finisher, sort_keys=True).encode())
    for b in source_bytes:
        h.update(hashlib.sha256(b).digest())
    return h.hexdigest()


def build_card(card_recipe: dict, finisher: list, source_bytes: list[bytes]) -> bytes:
    """Recipe card + raw sprite bytes -> deterministic PNG bytes."""
    seed = card_recipe.get("seed")
    if not isinstance(seed, int):
        raise BuildError("recipe needs an integer 'seed' (convention: the cardID)")
    ops = card_recipe.get("ops")
    if not isinstance(ops, list):
        raise BuildError("recipe needs an 'ops' list")
    images = [load_source_image(b) for b in source_bytes]
    try:
        out = apply_ops(images[0], images[1:], ops + list(finisher or []), seed)
    except OpError as e:
        raise BuildError(str(e)) from None
    if out.size != ART_SIZE:
        raise BuildError(f"op chain produced {out.size}, expected {ART_SIZE}")
    return encode_png(out.convert("RGBA"))


# --------------------------------------------------------------------------- build

def _load_state(path: Path) -> dict:
    if path.is_file():
        try:
            state = json.loads(path.read_text(encoding="utf-8"))
            if isinstance(state, dict):
                return state
        except json.JSONDecodeError:
            pass
    return {}


def build_pack(recipe_path: Path, out_dir: Path, sprite_index: dict,
               sprites_base: Path = SPRITES_BASE, force: bool = False,
               only_card: str | None = None) -> tuple[int, int, list[str]]:
    """Build one pack's art. Returns (built, skipped, errors)."""
    errors: list[str] = []
    try:
        recipes = load_recipes(recipe_path)
    except BuildError as e:
        return 0, 0, [str(e)]
    finisher = recipes.get("finisher") or []
    pack_label = recipes.get("pack", recipe_path.parent.name)

    state_path = out_dir.parent / STATE_FILENAME
    state = {} if force else _load_state(state_path)
    new_state: dict[str, str] = {}
    built = skipped = 0

    cards = recipes["cards"]
    names = [only_card] if only_card else sorted(cards)
    if only_card and only_card not in cards:
        return 0, 0, [f"{pack_label}/{only_card}: no recipe for this card"]

    for name in names:
        card_recipe = cards[name]
        out_path = out_dir / f"{name}.png"
        try:
            if not isinstance(card_recipe, dict):
                raise BuildError("recipe entry must be an object")
            paths = resolve_source_paths(card_recipe.get("source"), sprite_index,
                                         sprites_base)
            source_bytes = [p.read_bytes() for p in paths]
            fp = card_fingerprint(card_recipe, finisher, source_bytes)
            if state.get(name) == fp and out_path.is_file():
                new_state[name] = fp
                skipped += 1
                continue
            png = build_card(card_recipe, finisher, source_bytes)
            out_dir.mkdir(parents=True, exist_ok=True)
            out_path.write_bytes(png)
            new_state[name] = fp
            built += 1
        except BuildError as e:
            errors.append(f"{pack_label}/{name}: {e}")

    if not only_card:
        state_path.parent.mkdir(parents=True, exist_ok=True)
        state_path.write_text(json.dumps(new_state, indent=2, sort_keys=True) + "\n",
                              encoding="utf-8")
    return built, skipped, errors


def cmd_build(args) -> int:
    if args.recipes:
        pairs = [(Path(args.recipes),
                  Path(args.out_dir) if args.out_dir else Path(args.recipes).parent / "art")]
    elif args.all:
        pack_dirs = sorted(p for p in PACKS_DIR.iterdir()
                           if p.is_dir() and (p / "pack.json").is_file())
        pairs = [(p / "art-recipes.json", p / "art") for p in pack_dirs]
    elif args.pack:
        pack_dir = PACKS_DIR / args.pack
        if not pack_dir.is_dir():
            print(f"[ERROR] no such pack dir: {pack_dir}")
            return 1
        pairs = [(pack_dir / "art-recipes.json", pack_dir / "art")]
    else:
        print("give --pack <Name>, --all, or --recipes <file>")
        return 1

    try:
        index = load_sprite_index()
    except BuildError as e:
        print(f"[ERROR] {e}")
        return 1

    total_built = total_skipped = 0
    all_errors: list[str] = []
    for recipe_path, out_dir in pairs:
        built, skipped, errors = build_pack(recipe_path, out_dir, index,
                                            force=args.force)
        label = recipe_path.parent.name
        print(f"== {label}: built {built}, skipped {skipped}, errors {len(errors)}")
        total_built += built
        total_skipped += skipped
        all_errors.extend(errors)
    for e in all_errors:
        print(f"[ERROR] {e}")
    print(f"total: built {total_built}, skipped {total_skipped}, "
          f"errors {len(all_errors)}")
    return 1 if all_errors else 0


def cmd_preview(args) -> int:
    if args.recipes:
        recipe_path = Path(args.recipes)
    elif args.pack:
        recipe_path = PACKS_DIR / args.pack / "art-recipes.json"
    else:
        print("give --pack <Name> or --recipes <file>")
        return 1
    try:
        index = load_sprite_index()
        recipes = load_recipes(recipe_path)
    except BuildError as e:
        print(f"[ERROR] {e}")
        return 1
    card_recipe = recipes["cards"].get(args.card)
    if card_recipe is None:
        print(f"[ERROR] no recipe for card {args.card!r} in {recipe_path}")
        return 1
    try:
        paths = resolve_source_paths(card_recipe.get("source"), index)
        source_bytes = [p.read_bytes() for p in paths]
        png = build_card(card_recipe, recipes.get("finisher") or [], source_bytes)
    except BuildError as e:
        print(f"[ERROR] {args.card}: {e}")
        return 1
    out = Path(args.out) if args.out else (
        Path(tempfile.gettempdir()) / f"artmutate-preview-{args.card}.png")
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_bytes(png)
    print(out)
    return 0


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = ap.add_subparsers(dest="cmd", required=True)

    b = sub.add_parser("build", help="build pack art from recipes (incremental)")
    b.add_argument("--pack", help="pack dir name under packs/")
    b.add_argument("--all", action="store_true", help="build every pack")
    b.add_argument("--force", action="store_true", help="ignore build state, rebuild all")
    b.add_argument("--recipes", help="explicit recipe file (testing/off-tree builds)")
    b.add_argument("--out-dir", help="explicit output dir (with --recipes)")
    b.set_defaults(fn=cmd_build)

    p = sub.add_parser("preview", help="build one card to a temp path and print it")
    p.add_argument("--pack", help="pack dir name under packs/")
    p.add_argument("--recipes", help="explicit recipe file")
    p.add_argument("--card", required=True, help="card name (recipe key)")
    p.add_argument("--out", help="output path (default: system temp)")
    p.set_defaults(fn=cmd_preview)

    args = ap.parse_args(argv)
    return args.fn(args)


if __name__ == "__main__":
    sys.exit(main())
