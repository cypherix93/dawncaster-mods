"""dmk — the Dawncaster mod toolchain behind one command.

Thin dispatch: each subcommand imports the underlying tool and calls its
main() with your args unchanged, so `python tools/dmk.py validate -h` shows
that tool's own help and every standalone CLI keeps working. `dmk all <pack>`
runs the shipping gate chain in order and prints a summary table.

Usage:
    python tools/dmk.py <command> [args...]
    python tools/dmk.py all DC.<Pack>
"""

from __future__ import annotations

import sys
from pathlib import Path

TOOLS_DIR = Path(__file__).resolve().parent
sys.path.insert(0, str(TOOLS_DIR))

HELP = """\
dmk — the Dawncaster mod toolchain, one entry point
usage: python tools/dmk.py <command> [args...]        (command -h for details)

make & check content ("your first card in 15 minutes": docs/TUTORIAL.md)
  new       scaffold a new pack: valid pack.json + starter card + AutoId block
            (new_pack.py "<Pack Name>" --author "<you>")
  validate  gate 1 — schema shape, exact enum spellings, effect-DSL vocabulary,
            ID/name collisions vs the shipped pool and sibling packs
            (validate_pack.py <pack.json> | --all)
  sim       gate 3 — balance sim vs pool-derived envelopes; writes the pack's
            BALANCE-REPORT.md (sim/report.py <pack.json> | --all)

art (recipes -> PNGs; outputs stay local, never redistributed)
  art       build art from art-recipes.json, incremental
            (artmutate.py build --pack <DC.Name> | --all)
  artcheck  gate 2 — art presence/512x512/RGBA + perceptual-hash distinctness
            (validate_art.py --pack <DC.Name> [--distinctness] | --all)
  sheet     per-pack HTML contact sheets for review
            (contact_sheet.py --pack <DC.Name> | --all)

reference data
  stats     rebuild tools/out/card-stats.json pool statistics — the source of
            the power budgets (card_stats.py)

everything
  all       run the full gate chain on one pack, in order
            (validate -> artcheck --distinctness -> sim), then a summary table.
            Gate 4 (in-game QA) stays manual: install the pack and play it.

The gates and their order are the repo law (docs/CONVENTIONS.md 'Testing').
Docs: CARD-PACK-SPEC / WEAPON-SPEC (contracts), GAME-MECHANICS (design),
ART-MUTATION-SPEC (art), DC.DawnKit/API.md (C# mods).
"""


def _run_validate(rest):
    import validate_pack
    return validate_pack.main(rest)


def _run_sim(rest):
    from sim import report
    return report.main(rest)


def _run_art(rest):
    import artmutate
    if rest and rest[0] in ("-h", "--help"):
        return artmutate.main(["build", "--help"])
    return artmutate.main(["build"] + rest)


def _run_sheet(rest):
    import contact_sheet
    return contact_sheet.main(rest)


def _run_artcheck(rest):
    import validate_art
    return validate_art.main(rest)


def _run_new(rest):
    import new_pack
    return new_pack.main(rest)


def _run_stats(rest):
    if rest:
        print("dmk stats takes no arguments — rebuilds tools/out/card-stats.json "
              "from the extracted pool (card_stats.py)")
        return 0 if rest[0] in ("-h", "--help") else 2
    import card_stats
    card_stats.main()
    return 0


def _resolve_pack(arg: str):
    """Accept a DC.<Name> dir name, a pack dir path, or a pack.json path.
    Returns (pack_json, pack_dir) or (None, None)."""
    p = Path(arg)
    if p.name == "pack.json" and p.is_file():
        return p, p.parent
    if p.is_dir() and (p / "pack.json").is_file():
        return p / "pack.json", p
    import gamedata as gd
    for name in (arg, f"DC.{arg}"):
        cand = gd.PACKS_DIR / name / "pack.json"
        if cand.is_file():
            return cand, cand.parent
    return None, None


def _run_all(rest):
    args = [a for a in rest if not a.startswith("-")]
    if len(args) != 1 or any(a in ("-h", "--help") for a in rest):
        print("usage: python tools/dmk.py all <DC.Pack | path\\to\\pack.json>\n"
              "runs: validate (gate 1) -> artcheck --distinctness (gate 2) -> "
              "sim (gate 3); gate 4 is in-game QA")
        return 0 if any(a in ("-h", "--help") for a in rest) else 2
    pack_json, pack_dir = _resolve_pack(args[0])
    if pack_json is None:
        print(f"[ERROR] {args[0]!r} is not a pack (no pack.json found)")
        return 1

    import gamedata as gd
    in_tree = pack_dir.parent.resolve() == Path(gd.PACKS_DIR).resolve()
    gates = [
        ("gate 1  validate", lambda: _run_validate([str(pack_json)])),
        ("gate 2  artcheck",
         (lambda: _run_artcheck(["--pack", pack_dir.name, "--distinctness"]))
         if in_tree else None),
        ("gate 3  sim", lambda: _run_sim([str(pack_json)])),
    ]

    results = []
    for label, fn in gates:
        if fn is None:
            results.append((label, "SKIP (pack outside the repo root — run "
                                   "validate_art.py by hand)"))
            continue
        print(f"\n=== {label} — {pack_dir.name} " + "=" * 20)
        try:
            rc = fn()
        except SystemExit as e:   # underlying argparse error
            rc = e.code or 0
        results.append((label, "PASS" if rc == 0 else f"FAIL (exit {rc})"))

    print(f"\n{'=' * 56}\n{pack_dir.name} — gate summary")
    for label, verdict in results:
        print(f"  {label:<18} {verdict}")
    print("  gate 4  in-game    manual: install to DawncasterPacks and play "
          "(docs/TUTORIAL.md)")
    return 0 if all(v.startswith(("PASS", "SKIP")) for _, v in results) else 1


COMMANDS = {
    "new": _run_new,
    "validate": _run_validate,
    "sim": _run_sim,
    "art": _run_art,
    "artcheck": _run_artcheck,
    "sheet": _run_sheet,
    "stats": _run_stats,
    "all": _run_all,
}


def main(argv=None) -> int:
    argv = list(sys.argv[1:] if argv is None else argv)
    if not argv or argv[0] in ("-h", "--help", "help"):
        print(HELP)
        return 0
    cmd, rest = argv[0], argv[1:]
    fn = COMMANDS.get(cmd)
    if fn is None:
        import gamedata as gd
        close = gd.did_you_mean(cmd, COMMANDS)
        hint = f" — did you mean {', '.join(map(repr, close))}?" if close else ""
        print(f"dmk: unknown command {cmd!r}{hint}\n")
        print(HELP)
        return 2
    try:
        return fn(rest) or 0
    except SystemExit as e:       # let underlying argparse -h/errors exit clean
        return e.code or 0


if __name__ == "__main__":
    sys.exit(main())
