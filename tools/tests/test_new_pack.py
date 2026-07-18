"""Scaffolder tests: creates a valid pack that passes gate 1 immediately,
refuses collisions (existing dir, block overlap, taken pack name), and
registers nothing outside its own directory."""

import json

import pytest
from jsonschema import Draft7Validator

import gamedata as gd
import new_pack
import validate_pack as vp

AUTHOR = "zz-test-author"
NAME = "Zz Scaffold Probe"


@pytest.fixture()
def scaffold(tmp_path, capsys):
    rc = new_pack.main([NAME, "--author", AUTHOR, "--root", str(tmp_path)])
    out = capsys.readouterr().out
    pack_dir = tmp_path / "DC.ZzScaffoldProbe"
    return rc, out, pack_dir


def test_scaffold_creates_and_validates(scaffold):
    rc, out, pack_dir = scaffold
    assert rc == 0
    for f in ("pack.json", "art-recipes.json", "DESIGN-NOTES.md"):
        assert (pack_dir / f).is_file(), f
    assert (pack_dir / "art").is_dir()

    manifest = json.loads((pack_dir / "pack.json").read_text(encoding="utf-8"))
    block = gd.autoid_block(AUTHOR, NAME)
    assert manifest["schemaVersion"] == 1
    assert manifest["pack"] == NAME
    assert manifest["idBlock"] == [block, block + 99]
    assert manifest["cards"][0]["cardID"] == block

    # provenance is printed: block, formula, owner string, self-check verdict
    assert str(block) in out
    assert "FNV1a32" in out
    assert f"{AUTHOR}/{NAME}".lower() in out
    assert "0 error(s)" in out

    # gate 1 passes from a fresh process-state too
    assert vp.run_file(pack_dir / "pack.json", strict=False) == 0

    # the $schema pointer targets the repo schema (exact relative path when the
    # roots share a drive; the repo-layout default "../schemas/..." otherwise,
    # e.g. pytest tmp on C: vs repo on D:) and the manifest passes the schema
    schema_path = (gd.REPO_DIR / "schemas" / "pack.schema.json").resolve()
    pointer = (pack_dir / manifest["$schema"]).resolve()
    assert (pointer == schema_path
            or manifest["$schema"] == "../schemas/pack.schema.json")
    v = Draft7Validator(json.loads(schema_path.read_text(encoding="utf-8")))
    assert list(v.iter_errors(manifest)) == []


def test_scaffold_registers_nothing_elsewhere(scaffold, tmp_path):
    rc, _, pack_dir = scaffold
    assert rc == 0
    # nothing outside DC.ZzScaffoldProbe/ was created in the target root,
    # and the repo's ID registry was not touched
    assert [p.name for p in tmp_path.iterdir()] == [pack_dir.name]
    registry = (gd.REPO_DIR / "docs" / "ID-REGISTRY.md").read_text(encoding="utf-8")
    assert "ZzScaffoldProbe" not in registry


def test_art_recipes_stub(scaffold):
    _, _, pack_dir = scaffold
    recipes = json.loads((pack_dir / "art-recipes.json").read_text(encoding="utf-8"))
    assert recipes["pack"] == "ZzScaffoldProbe"
    assert recipes["cards"] == {}
    assert recipes["finisher"] == [{"op": "vignette", "strength": 0.25}]


def test_design_notes_skeleton(scaffold):
    _, _, pack_dir = scaffold
    notes = (pack_dir / "DESIGN-NOTES.md").read_text(encoding="utf-8")
    block = gd.autoid_block(AUTHOR, NAME)
    for fragment in (NAME, str(block), "Pack thesis", "Card-by-card",
                  "Weapons & weapon powers", "Open questions"):
        assert fragment in notes, fragment


def test_refuses_existing_dir(scaffold, capsys):
    rc, _, pack_dir = scaffold
    assert rc == 0
    rc2 = new_pack.main([NAME, "--author", AUTHOR,
                         "--root", str(pack_dir.parent)])
    assert rc2 == 1
    assert "already exists" in capsys.readouterr().out


def test_refuses_block_collision(tmp_path, monkeypatch, capsys):
    # plant a sibling that already claims this owner pair's AutoId block
    block = gd.autoid_block(AUTHOR, NAME)
    sib = tmp_path / "siblings" / "DC.Squatter"
    sib.mkdir(parents=True)
    (sib / "pack.json").write_text(json.dumps(
        {"pack": "Squatter", "idBlock": [block, block + 99], "cards": []}),
        encoding="utf-8")
    monkeypatch.setattr(gd, "PACKS_DIR", tmp_path / "siblings")
    rc = new_pack.main([NAME, "--author", AUTHOR,
                        "--root", str(tmp_path / "target")])
    out = capsys.readouterr().out
    assert rc == 1
    assert "collides" in out and "Squatter" in out
    assert not (tmp_path / "target" / "DC.ZzScaffoldProbe").exists()


def test_refuses_taken_pack_name(tmp_path, monkeypatch, capsys):
    sib = tmp_path / "siblings" / "DC.Whatever"
    sib.mkdir(parents=True)
    (sib / "pack.json").write_text(json.dumps(
        {"pack": NAME, "idBlock": [700999900, 700999999], "cards": []}),
        encoding="utf-8")
    monkeypatch.setattr(gd, "PACKS_DIR", tmp_path / "siblings")
    rc = new_pack.main([NAME, "--author", AUTHOR,
                        "--root", str(tmp_path / "target")])
    assert rc == 1
    assert "already taken" in capsys.readouterr().out


def test_collapse():
    assert new_pack.collapse("Frost Reverie") == "FrostReverie"
    assert new_pack.collapse("O'Malley's 2nd Deck!") == "OMalleys2ndDeck"
