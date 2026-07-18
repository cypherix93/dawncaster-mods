"""Contact sheet tests: deterministic HTML, embedded pairs, placeholder tiles."""

import json

import contact_sheet as cs
from tests.synthart import png_bytes, synth_sprite


def make_pack(tmp_path, with_art=True, with_recipe=True):
    out = tmp_path / "out"
    (out / "sprites").mkdir(parents=True)
    (out / "sprites" / "A.png").write_bytes(png_bytes(synth_sprite(512, 0)))
    index = {"SRC_A": {"file": "sprites/A.png"}}

    pd = tmp_path / "packs" / "PackA"
    pd.mkdir(parents=True)
    manifest = {"pack": "PackA", "cards": [{
        "name": "Alpha", "cost": {"STR": 1, "Generic": 1}, "rarity": "Rare",
        "type": "Melee", "description": "Hit <b>hard</b>.\nDraw a card."}]}
    (pd / "pack.json").write_text(json.dumps(manifest), encoding="utf-8")
    if with_recipe:
        recipes = {"pack": "PackA", "cards": {
            "Alpha": {"source": "SRC_A", "sourceCard": "Fireball", "seed": 1,
                      "ops": [{"op": "mirror"}]}}}
        (pd / "art-recipes.json").write_text(json.dumps(recipes), encoding="utf-8")
    if with_art:
        (pd / "art").mkdir()
        (pd / "art" / "Alpha.png").write_bytes(png_bytes(synth_sprite(512, 2)))
    return pd, index, out


def test_sheet_embeds_source_and_result(tmp_path):
    pd, index, out = make_pack(tmp_path)
    html = cs.build_sheet(pd, index, sprites_base=out)
    assert html.count("data:image/png;base64,") == 2   # source + result
    assert "Alpha" in html and "Rare" in html and "Melee" in html
    assert "1 Generic + 1 STR" in html
    assert "&lt;b&gt;hard&lt;/b&gt;" in html            # description escaped
    assert "SRC_A" in html and "Fireball" in html
    assert "1/1 cards with art" in html


def test_sheet_is_deterministic(tmp_path):
    pd, index, out = make_pack(tmp_path)
    assert cs.build_sheet(pd, index, sprites_base=out) == \
        cs.build_sheet(pd, index, sprites_base=out)


def test_missing_art_gets_red_placeholder(tmp_path):
    pd, index, out = make_pack(tmp_path, with_art=False)
    html = cs.build_sheet(pd, index, sprites_base=out)
    assert '<div class="tile missing">missing art</div>' in html
    assert "0/1 cards with art" in html


def test_missing_recipe_gets_grey_placeholder(tmp_path):
    pd, index, out = make_pack(tmp_path, with_recipe=False)
    html = cs.build_sheet(pd, index, sprites_base=out)
    assert '<div class="tile norecipe">no recipe</div>' in html


def test_index_links_and_counts(tmp_path):
    pd, index, out = make_pack(tmp_path)
    html = cs.build_index([pd])
    assert 'href="PackA/contact-sheet.html"' in html
    assert "1/1 arts" in html
