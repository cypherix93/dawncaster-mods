"""Build engine tests: incremental state, determinism, per-card error isolation."""

import json

import pytest
from PIL import Image

import artmutate as am
from tests.synthart import png_bytes, synth_sprite


@pytest.fixture
def corpus(tmp_path):
    """Tmp sprite corpus + recipe file, no real game assets."""
    sprites = tmp_path / "out" / "sprites"
    sprites.mkdir(parents=True)
    (sprites / "A.png").write_bytes(png_bytes(synth_sprite(512, 0)))
    (sprites / "B.png").write_bytes(png_bytes(synth_sprite(512, 3)))
    index = {
        "SRC_A": {"file": "sprites/A.png", "w": 512, "h": 512, "path_id": 1},
        "SRC_B": {"file": "sprites/B.png", "w": 512, "h": 512, "path_id": 2},
    }
    recipes = {
        "pack": "TestPack",
        "finisher": [{"op": "vignette", "strength": 0.25}],
        "cards": {
            "Alpha": {
                "source": "SRC_A", "seed": 700000001,
                "ops": [{"op": "mirror"}, {"op": "hue_rotate", "degrees": -30}],
            },
            "Beta": {
                "source": ["SRC_A", "SRC_B"], "seed": 700000002,
                "ops": [{"op": "composite", "mask": "noise", "scale": 8},
                        {"op": "grain", "opacity": 0.1}],
            },
        },
    }
    recipe_path = tmp_path / "art-recipes.json"
    recipe_path.write_text(json.dumps(recipes), encoding="utf-8")
    return {"tmp": tmp_path, "recipes": recipes, "recipe_path": recipe_path,
            "index": index, "base": tmp_path / "out", "out": tmp_path / "art"}


def build(c, force=False):
    return am.build_pack(c["recipe_path"], c["out"], c["index"],
                         sprites_base=c["base"], force=force)


def test_build_writes_512_rgba_pngs(corpus):
    built, skipped, errors = build(corpus)
    assert (built, skipped, errors) == (2, 0, [])
    for name in ("Alpha", "Beta"):
        img = Image.open(corpus["out"] / f"{name}.png")
        assert img.size == (512, 512) and img.mode == "RGBA"
    assert (corpus["tmp"] / am.STATE_FILENAME).is_file()


def test_incremental_skip_then_force_rebuild_identical(corpus):
    build(corpus)
    first = {n: (corpus["out"] / f"{n}.png").read_bytes() for n in ("Alpha", "Beta")}

    built, skipped, errors = build(corpus)
    assert (built, skipped, errors) == (0, 2, [])

    built, skipped, errors = build(corpus, force=True)
    assert (built, skipped, errors) == (2, 0, [])
    second = {n: (corpus["out"] / f"{n}.png").read_bytes() for n in ("Alpha", "Beta")}
    assert first == second  # determinism: same inputs -> byte-identical outputs


def test_recipe_change_rebuilds_only_that_card(corpus):
    build(corpus)
    recipes = corpus["recipes"]
    recipes["cards"]["Alpha"]["ops"][1]["degrees"] = 45
    corpus["recipe_path"].write_text(json.dumps(recipes), encoding="utf-8")
    built, skipped, errors = build(corpus)
    assert (built, skipped, errors) == (1, 1, [])


def test_source_change_invalidates_fingerprint(corpus):
    build(corpus)
    (corpus["base"] / "sprites" / "A.png").write_bytes(
        png_bytes(synth_sprite(512, 7)))
    built, skipped, errors = build(corpus)
    assert built == 2 and skipped == 0 and errors == []  # both cards use SRC_A


def test_missing_sprite_key_errors_but_others_build(corpus):
    recipes = corpus["recipes"]
    recipes["cards"]["Alpha"]["source"] = "NOPE_KEY"
    corpus["recipe_path"].write_text(json.dumps(recipes), encoding="utf-8")
    built, skipped, errors = build(corpus)
    assert built == 1  # Beta still builds
    assert len(errors) == 1 and "NOPE_KEY" in errors[0] and "Alpha" in errors[0]


def test_bad_op_errors_but_others_build(corpus):
    recipes = corpus["recipes"]
    recipes["cards"]["Beta"]["ops"].insert(0, {"op": "sparkle"})
    corpus["recipe_path"].write_text(json.dumps(recipes), encoding="utf-8")
    built, skipped, errors = build(corpus)
    assert built == 1
    assert len(errors) == 1 and "sparkle" in errors[0]


def test_missing_recipe_file_is_error(tmp_path):
    built, skipped, errors = am.build_pack(
        tmp_path / "art-recipes.json", tmp_path / "art", {},
        sprites_base=tmp_path)
    assert built == 0 and len(errors) == 1
    assert "not found" in errors[0]


def test_invalid_recipe_json_is_error(tmp_path):
    rp = tmp_path / "art-recipes.json"
    rp.write_text("{not json", encoding="utf-8")
    built, _, errors = am.build_pack(rp, tmp_path / "art", {},
                                     sprites_base=tmp_path)
    assert built == 0 and len(errors) == 1 and "JSON" in errors[0]


def test_missing_seed_is_clear_error(corpus):
    recipes = corpus["recipes"]
    del recipes["cards"]["Alpha"]["seed"]
    corpus["recipe_path"].write_text(json.dumps(recipes), encoding="utf-8")
    _, _, errors = build(corpus)
    assert len(errors) == 1 and "seed" in errors[0]


def test_non_512_source_is_normalized(corpus):
    (corpus["base"] / "sprites" / "A.png").write_bytes(
        png_bytes(synth_sprite(256, 0)))
    built, _, errors = build(corpus)
    assert errors == []
    img = Image.open(corpus["out"] / "Alpha.png")
    assert img.size == (512, 512)


def test_recipe_size_builds_portrait_output(corpus):
    """Weapon-style recipes: "size": [w, h] drives the output canvas."""
    recipes = corpus["recipes"]
    recipes["cards"]["Alpha"]["size"] = [256, 436]
    corpus["recipe_path"].write_text(json.dumps(recipes), encoding="utf-8")
    built, _, errors = build(corpus)
    assert errors == []
    img = Image.open(corpus["out"] / "Alpha.png")
    assert img.size == (256, 436) and img.mode == "RGBA"
    # Beta (no size) keeps the 512×512 default
    assert Image.open(corpus["out"] / "Beta.png").size == (512, 512)


def test_portrait_source_kept_whole_at_matching_size(corpus):
    """A source already at the target size must pass through uncropped
    (the 512×873 weapon sprites keep their full height — no letterbox,
    no blade-tip clipping)."""
    portrait = synth_sprite(512, 0).resize((256, 436))
    (corpus["base"] / "sprites" / "A.png").write_bytes(png_bytes(portrait))
    loaded = am.load_source_image(png_bytes(portrait), (256, 436))
    assert loaded.size == (256, 436)
    assert loaded.tobytes() == portrait.convert("RGBA").tobytes()


def test_composite_extras_are_cover_cropped_to_target_size(corpus):
    """Secondary (square) sources of a portrait recipe are cover-cropped to
    the target aspect, never stretched."""
    recipes = corpus["recipes"]
    recipes["cards"]["Beta"]["size"] = [256, 436]
    corpus["recipe_path"].write_text(json.dumps(recipes), encoding="utf-8")
    built, _, errors = build(corpus)
    assert errors == []
    assert Image.open(corpus["out"] / "Beta.png").size == (256, 436)


@pytest.mark.parametrize("bad", [[512], [512, 512, 512], ["512", "873"],
                                 [512, 0], [512, 4096], "512x873"])
def test_bad_size_is_clear_error(corpus, bad):
    recipes = corpus["recipes"]
    recipes["cards"]["Alpha"]["size"] = bad
    corpus["recipe_path"].write_text(json.dumps(recipes), encoding="utf-8")
    built, _, errors = build(corpus)
    assert built == 1  # Beta still builds
    assert len(errors) == 1 and "size" in errors[0] and "Alpha" in errors[0]


def test_encode_png_fixed_settings_stable():
    img = synth_sprite(128, 2)
    assert am.encode_png(img) == am.encode_png(img.copy())
