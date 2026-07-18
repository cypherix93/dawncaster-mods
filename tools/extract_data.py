"""Extract Dawncaster ScriptableObject data + Ink stories (READ-ONLY on the game dir).

Dumps every MonoBehaviour whose script class is one of the game's content types
(Card, Talent, StatusEffect, Dialogue, ...) from `resources.assets` (+ level/sharedassets
files) to `tools/out/data/<Class>/<name>.json`, plus all TextAssets (compiled Ink stories)
to `tools/out/data/TextAsset/<name>.txt`, and writes `tools/out/data-index.json`.

Mono backend => MonoBehaviour typetrees are generated from the game's Managed DLLs
via UnityPy's TypeTreeGenerator (TypeTreeGeneratorAPI).

Implementation notes (hard-won):
- All .assets/level files are loaded into ONE UnityPy environment so cross-file
  m_Script PPtrs resolve.
- The script class name is read via `obj.parse_monobehaviour_head()` +
  `m_Script.deref_parse_as_object()` — this parses only the MonoBehaviour base
  fields with the stock typetree and is ~instant. Do NOT call `obj.read()` while a
  typetree generator is attached just to peek at the class: that performs a full
  generated-tree *wrapped* read for every MonoBehaviour (UI widgets included),
  which is orders of magnitude slower and can raise EOFError on some objects.
- Full data is read with `obj.read_typetree()` (dict mode) only for wanted classes.
- TypeTreeGeneratorAPI 0.0.10 names List<T> field nodes after their *element* type.
  For `List<string>` fields (e.g. Card.cardKeywords) the node therefore gets
  m_Type == "string", which UnityPy's reader special-cases as a plain string and
  misparses whenever the list is non-empty (EOFError / garbage). FixedTypeTreeGenerator
  renames such nodes to "vector" (detected by their Array child holding non-char data).
- Even with that rename the UnityPyBoost C reader still fails on these trees, so the
  module forces the pure-Python typetree reader (read_typetree_boost = None).

Usage:
    python tools/extract_data.py [--game <Dawncaster_Data dir>] [--out tools/out]
                                 [--classes Card,Talent,...]
"""

from __future__ import annotations

import argparse
import json
import re
import time
from pathlib import Path

import UnityPy
from UnityPy.helpers import TypeTreeHelper
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator

# The UnityPyBoost C reader mis-handles the generator's List<T> nodes even after the
# "vector" rename below (EOFError on non-empty List<string>). The pure-Python reader
# handles them fine and is plenty fast for these small objects, so force it.
TypeTreeHelper.read_typetree_boost = None

DEFAULT_GAME_DATA = Path(r"E:\Games\Steam\steamapps\common\Dawncaster\Dawncaster_Data")
DEFAULT_OUT = Path(__file__).resolve().parent / "out"

CONTENT_CLASSES = [
    "Card", "Talent", "StatusEffect", "Dialogue", "AreaEvent", "NPC",
    "Profession", "Battle", "Area", "Affix", "Bounty", "Invasion",
    "Companion", "Keystone", "Score", "DawncasterProduct", "ExpansionData",
]

SAFE_NAME = re.compile(r"[^A-Za-z0-9_\-\.]")


def asset_files(game_data: Path) -> list[Path]:
    files = [game_data / "resources.assets", game_data / "globalgamemanagers.assets"]
    files += sorted(game_data.glob("sharedassets*.assets"))
    files += sorted(p for p in game_data.glob("level*") if not p.suffix == ".resS")
    return [f for f in files if f.is_file()]


def _fix_string_list_nodes(node) -> None:
    """Rename List<string> nodes mis-typed as 'string' to 'vector' (see module doc)."""
    for child in node.m_Children:
        if child.m_Type == "string" and child.m_Children:
            arr = child.m_Children[0]
            if arr.m_Type == "Array" and len(arr.m_Children) > 1 and arr.m_Children[1].m_Type != "char":
                child.m_Type = "vector"
        _fix_string_list_nodes(child)


class FixedTypeTreeGenerator(TypeTreeGenerator):
    def get_nodes_up(self, assembly: str, fullname: str):
        node = super().get_nodes_up(assembly, fullname)
        _fix_string_list_nodes(node)
        return node


def make_generator(game_data: Path) -> TypeTreeGenerator:
    gen = FixedTypeTreeGenerator(unity_version="2022.3.62f2")
    gen.load_local_dll_folder(str(game_data / "Managed"))
    return gen


def script_class(obj) -> str | None:
    """Cheap class-name peek: base MonoBehaviour fields only, no generated tree."""
    try:
        head = obj.parse_monobehaviour_head()
        script = head.m_Script.deref_parse_as_object()
        return script.m_ClassName
    except Exception:
        return None


def unique_path(cls_dir: Path, name: str, ext: str) -> Path:
    out_path = cls_dir / f"{name}{ext}"
    i = 1
    while out_path.exists():
        out_path = cls_dir / f"{name}__{i}{ext}"
        i += 1
    return out_path


def dump(game_data: Path, out_dir: Path, classes: list[str]) -> None:
    t_start = time.time()
    data_dir = Path(out_dir) / "data"
    data_dir.mkdir(parents=True, exist_ok=True)
    wanted = set(classes)
    index: dict[str, dict] = {}
    counts: dict[str, int] = {}
    gen = make_generator(game_data)

    files = asset_files(game_data)
    # One environment for all files => cross-file PPtr (m_Script) resolution works.
    env = UnityPy.load(*[str(p) for p in files])
    env.typetree_generator = gen

    for obj in env.objects:
        kind = obj.type.name
        src = Path(obj.assets_file.name if hasattr(obj.assets_file, "name") else "?").name
        if kind == "TextAsset":
            data = obj.read()
            name = SAFE_NAME.sub("_", data.m_Name) or f"pid_{obj.path_id}"
            cls_dir = data_dir / "TextAsset"
            cls_dir.mkdir(exist_ok=True)
            text = data.m_Script
            if isinstance(text, bytes):
                text = text.decode("utf-8", errors="replace")
            out_path = unique_path(cls_dir, name, ".txt")
            out_path.write_text(text, encoding="utf-8", errors="replace")
            counts["TextAsset"] = counts.get("TextAsset", 0) + 1
            index[f"TextAsset/{out_path.stem}"] = {
                "class": "TextAsset", "file": f"data/TextAsset/{out_path.name}",
                "source": src, "path_id": obj.path_id,
            }
            continue
        if kind != "MonoBehaviour":
            continue
        cls = script_class(obj)
        if cls is None or cls not in wanted:
            continue
        try:
            tree = obj.read_typetree()  # dict mode; generator supplies full tree
        except Exception:
            counts[f"{cls}:FAILED"] = counts.get(f"{cls}:FAILED", 0) + 1
            continue
        name = SAFE_NAME.sub("_", tree.get("m_Name") or "") or f"pid_{obj.path_id}"
        cls_dir = data_dir / cls
        cls_dir.mkdir(exist_ok=True)
        out_path = unique_path(cls_dir, name, ".json")
        with open(out_path, "w", encoding="utf-8", newline="\n") as f:
            json.dump(tree, f, indent=2, ensure_ascii=False, default=str)
        counts[cls] = counts.get(cls, 0) + 1
        index[f"{cls}/{out_path.stem}"] = {
            "class": cls, "file": f"data/{cls}/{out_path.name}",
            "source": src, "path_id": obj.path_id,
        }

    with open(Path(out_dir) / "data-index.json", "w", encoding="utf-8", newline="\n") as f:
        json.dump(dict(sorted(index.items())), f, indent=2)
    print(f"elapsed: {time.time() - t_start:.1f}s")
    print("counts:", json.dumps(dict(sorted(counts.items())), indent=2))


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--game", type=Path, default=DEFAULT_GAME_DATA)
    ap.add_argument("--out", type=Path, default=DEFAULT_OUT)
    ap.add_argument("--classes", type=str, default=",".join(CONTENT_CLASSES))
    args = ap.parse_args()
    dump(args.game, args.out, [c.strip() for c in args.classes.split(",") if c.strip()])


if __name__ == "__main__":
    main()
