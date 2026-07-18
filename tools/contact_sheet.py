"""Per-pack HTML review sheets: SOURCE sprite → RESULT art pairs (spec §6/§7).

Self-contained HTML (images embedded as base64 data URIs, no external assets,
no timestamps — deterministic output for identical inputs). One row per card
in pack.json order: source sprite(s) from art-recipes.json, the built art from
DC.<Pack>/art/, and name/cost/rarity/type/description. Missing art renders
as a red placeholder tile; missing recipe as a grey tile.

Outputs are gitignored (they embed copyrighted game art — local review only).

Usage:
    python tools/contact_sheet.py --pack DC.EmberweaveGrove
    python tools/contact_sheet.py --all      # all packs + repo-root contact-sheets.html index
"""

from __future__ import annotations

import argparse
import base64
import html
import json
import sys
import urllib.parse
from pathlib import Path

TOOLS_DIR = Path(__file__).resolve().parent
REPO_DIR = TOOLS_DIR.parent
# Content packages are top-level DC.<Name>/ dirs; pack.json presence is the filter.
PACKS_DIR = REPO_DIR
SPRITE_INDEX = TOOLS_DIR / "out" / "sprite-index.json"
SPRITES_BASE = TOOLS_DIR / "out"

CSS = """
body { background: #17141f; color: #d8d3e6; font: 14px/1.45 'Segoe UI', sans-serif;
       margin: 0; padding: 24px; }
h1 { font-size: 20px; margin: 0 0 4px; color: #f0ead6; }
.sub { color: #8d86a3; margin-bottom: 20px; }
table { border-collapse: collapse; width: 100%; }
td, th { padding: 10px 12px; border-bottom: 1px solid #2b2638; vertical-align: top;
         text-align: left; }
th { color: #8d86a3; font-weight: 600; font-size: 12px; text-transform: uppercase; }
img.art { width: 192px; height: 192px; display: block; border-radius: 6px;
          background: #000; }
.tile { width: 192px; height: 192px; display: flex; align-items: center;
        justify-content: center; border-radius: 6px; font-size: 12px;
        text-align: center; }
.tile.missing { background: #5c1a1a; color: #ffb3b3; border: 2px solid #a33; }
.tile.norecipe { background: #2b2638; color: #8d86a3; border: 2px dashed #453d5c; }
.arrow { font-size: 28px; color: #6f6787; vertical-align: middle; }
.name { font-size: 16px; font-weight: 600; color: #f0ead6; }
.tags { color: #8d86a3; font-size: 12px; margin: 2px 0 8px; }
.desc { white-space: pre-line; max-width: 420px; }
.srckey { color: #6f6787; font-size: 11px; margin-top: 4px; font-family: monospace; }
a { color: #9ecbff; }
ul.index { font-size: 16px; line-height: 2; }
"""


def _b64_img(path: Path) -> str:
    data = base64.b64encode(path.read_bytes()).decode("ascii")
    return f'<img class="art" src="data:image/png;base64,{data}" alt="">'


def _cost_str(cost: dict) -> str:
    if not isinstance(cost, dict):
        return "Free"
    parts = [f"{v} {k}" for k, v in sorted(cost.items()) if v]
    return " + ".join(parts) if parts else "Free"


def _source_cell(rec, sprite_index: dict, sprites_base: Path) -> str:
    if not isinstance(rec, dict):
        return '<div class="tile norecipe">no recipe</div>'
    src = rec.get("source")
    keys = src if isinstance(src, list) else [src]
    parts = []
    for key in keys:
        entry = sprite_index.get(key) if isinstance(key, str) else None
        p = (sprites_base / entry["file"]) if entry else None
        if p is not None and p.is_file():
            parts.append(_b64_img(p))
        else:
            parts.append(f'<div class="tile missing">source<br>{html.escape(str(key))}'
                         "<br>not found</div>")
        parts.append(f'<div class="srckey">{html.escape(str(key))}'
                     f'{" ← " + html.escape(rec.get("sourceCard", "")) if rec.get("sourceCard") else ""}</div>')
    return "\n".join(parts)


def build_sheet(pack_dir: Path, sprite_index: dict,
                sprites_base: Path = SPRITES_BASE) -> str:
    pack_json = json.loads((pack_dir / "pack.json").read_text(encoding="utf-8"))
    recipes_path = pack_dir / "art-recipes.json"
    recipes: dict = {}
    if recipes_path.is_file():
        recipes = json.loads(recipes_path.read_text(encoding="utf-8"))
    recipe_cards = recipes.get("cards") or {}

    pack_name = pack_json.get("pack", pack_dir.name)
    cards = [c for c in pack_json.get("cards", []) if isinstance(c, dict)]
    have_art = 0

    rows = []
    for card in cards:
        name = card.get("name", "?")
        art_path = pack_dir / "art" / f"{name}.png"
        if art_path.is_file():
            result_cell = _b64_img(art_path)
            have_art += 1
        else:
            result_cell = '<div class="tile missing">missing art</div>'
        info = (
            f'<div class="name">{html.escape(name)}</div>'
            f'<div class="tags">{html.escape(_cost_str(card.get("cost")))} · '
            f'{html.escape(str(card.get("rarity", "?")))} · '
            f'{html.escape(str(card.get("type", "?")))}</div>'
            f'<div class="desc">{html.escape(str(card.get("description", "")))}</div>'
        )
        rows.append(
            "<tr>"
            f"<td>{_source_cell(recipe_cards.get(name), sprite_index, sprites_base)}</td>"
            '<td class="arrow">→</td>'
            f"<td>{result_cell}</td>"
            f"<td>{info}</td>"
            "</tr>"
        )

    return (
        "<!DOCTYPE html>\n<html><head><meta charset='utf-8'>"
        f"<title>{html.escape(pack_name)} — art contact sheet</title>"
        f"<style>{CSS}</style></head><body>"
        f"<h1>{html.escape(pack_name)}</h1>"
        f'<div class="sub">{have_art}/{len(cards)} cards with art · '
        "source sprite → mutated result (local review only — do not distribute)</div>"
        "<table><tr><th>Source</th><th></th><th>Result</th><th>Card</th></tr>"
        + "\n".join(rows) +
        "</table></body></html>\n"
    )


def build_index(pack_dirs: list[Path]) -> str:
    items = []
    for pd in pack_dirs:
        pack_json = json.loads((pd / "pack.json").read_text(encoding="utf-8"))
        cards = [c for c in pack_json.get("cards", []) if isinstance(c, dict)]
        n_art = sum(1 for c in cards
                    if (pd / "art" / f"{c.get('name', '?')}.png").is_file())
        href = urllib.parse.quote(pd.name) + "/contact-sheet.html"
        items.append(f'<li><a href="{href}">{html.escape(pack_json.get("pack", pd.name))}'
                     f"</a> — {n_art}/{len(cards)} arts</li>")
    return (
        "<!DOCTYPE html>\n<html><head><meta charset='utf-8'>"
        "<title>Pack art contact sheets</title>"
        f"<style>{CSS}</style></head><body>"
        "<h1>Pack art contact sheets</h1>"
        '<ul class="index">' + "\n".join(items) + "</ul></body></html>\n"
    )


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--pack", help="package dir name (e.g. DC.EmberweaveGrove)")
    ap.add_argument("--all", action="store_true",
                    help="all packs + repo-root contact-sheets.html index")
    args = ap.parse_args(argv)

    if args.all:
        pack_dirs = sorted(p for p in PACKS_DIR.iterdir()
                           if p.is_dir() and (p / "pack.json").is_file())
    elif args.pack:
        pd = PACKS_DIR / args.pack
        if not (pd / "pack.json").is_file():
            print(f"[ERROR] no pack.json in {pd}")
            return 1
        pack_dirs = [pd]
    else:
        ap.error("give --pack <Name> or --all")

    sprite_index = {}
    if SPRITE_INDEX.is_file():
        sprite_index = json.loads(SPRITE_INDEX.read_text(encoding="utf-8"))
    else:
        print(f"[WARN] {SPRITE_INDEX} not found — source tiles will be placeholders")

    for pd in pack_dirs:
        out = pd / "contact-sheet.html"
        out.write_text(build_sheet(pd, sprite_index), encoding="utf-8")
        print(out)
    if args.all:
        out = PACKS_DIR / "contact-sheets.html"
        out.write_text(build_index(pack_dirs), encoding="utf-8")
        print(out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
