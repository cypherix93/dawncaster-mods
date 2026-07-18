"""dmk dispatch smoke tests: help map, forwarding to the underlying tools,
pack resolution, and the `all` gate chain (out-of-tree pack -> artcheck SKIP)."""

import json

import pytest

import dmk
import gamedata as gd

EXAMPLE = gd.REPO_DIR / "DC.DawnKit" / "examples" / "ExamplePack" / "pack.json"


# ------------------------------------------------------------------- help map

def test_help_lists_every_command(capsys):
    assert dmk.main([]) == 0
    out = capsys.readouterr().out
    for cmd in dmk.COMMANDS:
        assert cmd in out, cmd
    for anchor in ("gate 1", "gate 2", "gate 3", "TUTORIAL", "CONVENTIONS"):
        assert anchor in out, anchor
    assert dmk.main(["-h"]) == 0


def test_unknown_command_did_you_mean(capsys):
    assert dmk.main(["validte"]) == 2
    assert "did you mean 'validate'" in capsys.readouterr().out


# ----------------------------------------------------------------- forwarding

def test_validate_forwards(capsys):
    assert dmk.main(["validate", str(EXAMPLE)]) == 0
    assert "0 error(s)" in capsys.readouterr().out


def test_validate_error_propagates(tmp_path, capsys):
    bad = tmp_path / "pack.json"
    bad.write_text(json.dumps({"pack": "Zz Bad", "idBlock": [1, 2],
                               "cards": []}), encoding="utf-8")
    assert dmk.main(["validate", str(bad)]) == 1


@pytest.mark.parametrize("argv", [["validate", "-h"], ["sim", "-h"],
                                  ["art", "-h"], ["artcheck", "-h"],
                                  ["sheet", "-h"], ["new", "-h"],
                                  ["stats", "-h"], ["all", "-h"]])
def test_subcommand_help_exits_zero(argv, capsys):
    assert dmk.main(argv) == 0
    assert capsys.readouterr().out.strip()


def test_stats_rejects_args(capsys):
    assert dmk.main(["stats", "bogus"]) == 2


def test_new_forwards(tmp_path, capsys):
    assert dmk.main(["new", "Zz Dmk Probe", "--author", "zz-test",
                     "--root", str(tmp_path)]) == 0
    assert (tmp_path / "DC.ZzDmkProbe" / "pack.json").is_file()


# ------------------------------------------------------------ pack resolution

def test_resolve_pack_variants():
    pj, pd = dmk._resolve_pack(str(EXAMPLE))
    assert pj == EXAMPLE and pd == EXAMPLE.parent
    pj, pd = dmk._resolve_pack(str(EXAMPLE.parent))
    assert pj == EXAMPLE
    expect = (gd.PACKS_DIR / "DC.VenomousLegacy" / "pack.json").resolve()
    pj, _ = dmk._resolve_pack("DC.VenomousLegacy")
    assert pj.resolve() == expect
    pj, _ = dmk._resolve_pack("VenomousLegacy")     # DC. prefix optional
    assert pj.resolve() == expect
    assert dmk._resolve_pack("DC.DoesNotExist") == (None, None)


# -------------------------------------------------------------- the `all` chain

def test_all_unknown_pack(capsys):
    assert dmk.main(["all", "DC.DoesNotExist"]) == 1
    assert "not a pack" in capsys.readouterr().out


def test_all_gate_chain_out_of_tree(tmp_path, capsys):
    # a fresh scaffold outside the repo root: validate + sim PASS,
    # artcheck is skipped (validate_art only scopes repo-root packs)
    assert dmk.main(["new", "Zz Dmk Chain Probe", "--author", "zz-test",
                     "--root", str(tmp_path)]) == 0
    capsys.readouterr()
    rc = dmk.main(["all", str(tmp_path / "DC.ZzDmkChainProbe")])
    out = capsys.readouterr().out
    assert "gate summary" in out
    assert "gate 1  validate   PASS" in out
    assert "SKIP" in out
    assert "gate 3  sim        PASS" in out
    assert "gate 4  in-game    manual" in out
    assert rc == 0


def test_all_fails_when_a_gate_fails(tmp_path, capsys):
    pack_dir = tmp_path / "DC.ZzBroken"
    pack_dir.mkdir()
    (pack_dir / "pack.json").write_text(json.dumps({
        "pack": "Zz Broken", "idBlock": [700999900, 700999999],
        "cards": [{"name": "Zz Broken Card"}]}), encoding="utf-8")
    rc = dmk.main(["all", str(pack_dir)])
    out = capsys.readouterr().out
    assert rc == 1
    assert "FAIL" in out
