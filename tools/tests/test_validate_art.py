"""validate_art tests: hash math, shipping checks, §2 source budget, distinctness gate.

All fixtures are synthetic (tmp sprite corpus + tmp packs dir) — no extracted
game assets required.
"""

import json
import random

import pytest
from PIL import Image

import artmutate_ops as ops
import validate_art as va
from tests.synthart import png_bytes, synth_sprite

MIRROR_RECOLOR = [
    {"op": "mirror"},
    {"op": "hue_rotate", "degrees": 60},
    {"op": "gradient_map", "palette": ["#1a1210", "#c2571f", "#f5c469"],
     "strength": 0.5},
]
PURE_HUE = [{"op": "hue_rotate", "degrees": 120}]


# ------------------------------------------------------------------ hash math

def test_hamming_known_bits():
    assert va.hamming(0b1010, 0b0101) == 4
    assert va.hamming(0, 0) == 0
    assert va.hamming((1 << 256) - 1, 0) == 256


def test_hashes_zero_distance_for_identical_images():
    img = synth_sprite(512, 1)
    assert va.hash_distance(va.art_hashes(img), va.art_hashes(img.copy())) == (0, 0)


def test_dhash_large_for_mirror_of_asymmetric_image():
    img = synth_sprite(512, 0)
    mirrored = ops.apply_ops(img, [], [{"op": "mirror"}], 1)
    d, _ = va.hash_distance(va.art_hashes(img), va.art_hashes(mirrored))
    assert d >= va.SOURCE_MIN_DHASH


def test_hashes_ignore_pure_hue_rotation():
    img = synth_sprite(512, 0)
    hued = ops.apply_ops(img, [], PURE_HUE, 1)
    d, a = va.hash_distance(va.art_hashes(img), va.art_hashes(hued))
    assert d + a < va.SOURCE_MIN_SUM  # the whole point of V-plane hashing


# ------------------------------------------------------------------ fixtures

def make_env(tmp_path, cards, recipes=None, arts=None, extra_packs=()):
    """Build a tmp packs/ + sprite corpus. cards: list of names for PackA.

    arts: {cardname: png bytes} written to PackA/art/.
    extra_packs: [(packname, cards, recipes, arts)] for cross-pack checks.
    """
    out = tmp_path / "out"
    (out / "sprites").mkdir(parents=True)
    (out / "sprites" / "A.png").write_bytes(png_bytes(synth_sprite(512, 0)))
    (out / "sprites" / "B.png").write_bytes(png_bytes(synth_sprite(512, 3)))
    index = {"SRC_A": {"file": "sprites/A.png"}, "SRC_B": {"file": "sprites/B.png"}}
    index_path = out / "sprite-index.json"
    index_path.write_text(json.dumps(index), encoding="utf-8")

    packs = tmp_path / "packs"
    for pack_name, pcards, precipes, parts in (
            [("PackA", cards, recipes, arts)] + list(extra_packs)):
        pd = packs / pack_name
        pd.mkdir(parents=True)
        manifest = {"pack": pack_name,
                    "cards": [{"name": n, "cost": {}, "rarity": "Common",
                               "type": "Magic", "description": "d"} for n in pcards]}
        (pd / "pack.json").write_text(json.dumps(manifest), encoding="utf-8")
        if precipes is not None:
            (pd / "art-recipes.json").write_text(json.dumps(precipes),
                                                 encoding="utf-8")
        for name, data in (parts or {}).items():
            (pd / "art").mkdir(exist_ok=True)
            (pd / "art" / f"{name}.png").write_bytes(data)
    return {"packs": packs, "index_path": index_path, "out": out}


def run_validate(env, scope=("PackA",), distinctness=False):
    return va.validate([env["packs"] / s for s in scope],
                       packs_dir=env["packs"],
                       sprite_index_path=env["index_path"],
                       sprites_base=env["out"],
                       distinctness=distinctness)


def errors_of(findings, check=None):
    return [f for f in findings if f["level"] == "ERROR"
            and (check is None or f["check"] == check)]


def mutate(variant, chain, seed=1):
    return png_bytes(ops.apply_ops(synth_sprite(512, variant), [], chain, seed))


# ------------------------------------------------------------------ shipping checks

def test_good_pack_passes_including_distinctness(tmp_path):
    recipes = {"pack": "PackA", "cards": {
        "Good": {"source": "SRC_A", "seed": 1, "ops": MIRROR_RECOLOR}}}
    env = make_env(tmp_path, ["Good"], recipes,
                   {"Good": mutate(0, MIRROR_RECOLOR)})
    findings = run_validate(env, distinctness=True)
    assert errors_of(findings) == []


def test_missing_art_reported(tmp_path):
    env = make_env(tmp_path, ["Ghost"], recipes={"pack": "PackA", "cards": {}})
    errs = errors_of(run_validate(env), "art_missing")
    assert len(errs) == 1 and errs[0]["card"] == "Ghost"


def test_stray_file_reported(tmp_path):
    env = make_env(tmp_path, ["Good"], None,
                   {"Good": mutate(0, MIRROR_RECOLOR), "Impostor": b"x"})
    errs = errors_of(run_validate(env), "art_stray")
    assert len(errs) == 1 and errs[0]["card"] == "Impostor.png"


def test_wrong_dimensions_and_mode(tmp_path):
    small = png_bytes(synth_sprite(256, 0))
    rgb = png_bytes(synth_sprite(512, 0).convert("RGB"))
    env = make_env(tmp_path, ["Small", "NoAlpha"], None,
                   {"Small": small, "NoAlpha": rgb})
    findings = run_validate(env)
    assert errors_of(findings, "art_dimensions")[0]["card"] == "Small"
    assert errors_of(findings, "art_not_rgba")[0]["card"] == "NoAlpha"


def test_oversized_art_reported(tmp_path):
    rng = random.Random(1)
    noisy = Image.frombytes("RGBA", (512, 512), rng.randbytes(512 * 512 * 4))
    env = make_env(tmp_path, ["Fat"], None, {"Fat": png_bytes(noisy)})
    assert errors_of(run_validate(env), "art_too_big")


def test_missing_recipes_file_warns_not_errors(tmp_path):
    env = make_env(tmp_path, ["Good"], None, {"Good": mutate(0, MIRROR_RECOLOR)})
    findings = run_validate(env, distinctness=True)
    assert errors_of(findings) == []
    assert any(f["check"] == "no_recipes" for f in findings)


# ------------------------------------------------------------------ §2 source budget

def test_source_twice_in_one_pack_is_error(tmp_path):
    recipes = {"pack": "PackA", "cards": {
        "One": {"source": "SRC_A", "seed": 1, "ops": []},
        "Two": {"source": "SRC_A", "seed": 2, "ops": []}}}
    env = make_env(tmp_path, ["One", "Two"], recipes)
    assert errors_of(run_validate(env), "source_overuse_pack")


def test_source_thrice_globally_is_error(tmp_path):
    ra = {"pack": "PackA", "cards": {
        "One": {"source": "SRC_A", "seed": 1, "ops": []}}}
    rb = {"pack": "PackB", "cards": {
        "Two": {"source": "SRC_A", "seed": 2, "ops": []},
        "Three": {"source": ["SRC_A", "SRC_B"], "seed": 3, "ops": []}}}
    env = make_env(tmp_path, ["One"], ra,
                   extra_packs=[("PackB", ["Two", "Three"], rb, None)])
    findings = run_validate(env)
    assert errors_of(findings, "source_overuse")          # SRC_A used 3× globally
    assert errors_of(findings, "source_overuse_pack")     # and 2× inside PackB


def test_source_twice_across_packs_is_allowed(tmp_path):
    ra = {"pack": "PackA", "cards": {
        "One": {"source": "SRC_A", "seed": 1, "ops": []}}}
    rb = {"pack": "PackB", "cards": {
        "Two": {"source": "SRC_A", "seed": 2, "ops": []}}}
    env = make_env(tmp_path, ["One"], ra,
                   extra_packs=[("PackB", ["Two"], rb, None)])
    findings = run_validate(env)
    assert not errors_of(findings, "source_overuse")
    assert not errors_of(findings, "source_overuse_pack")


# ------------------------------------------------------------------ distinctness

def test_pure_hue_rotate_fails_vs_source(tmp_path):
    recipes = {"pack": "PackA", "cards": {
        "Lazy": {"source": "SRC_A", "seed": 1, "ops": PURE_HUE}}}
    env = make_env(tmp_path, ["Lazy"], recipes, {"Lazy": mutate(0, PURE_HUE)})
    errs = errors_of(run_validate(env, distinctness=True),
                     "not_distinct_from_source")
    assert len(errs) == 1 and errs[0]["card"] == "Lazy"


def test_mirror_recolor_passes_vs_source(tmp_path):
    recipes = {"pack": "PackA", "cards": {
        "Fine": {"source": "SRC_A", "seed": 1, "ops": MIRROR_RECOLOR}}}
    env = make_env(tmp_path, ["Fine"], recipes,
                   {"Fine": mutate(0, MIRROR_RECOLOR)})
    assert not errors_of(run_validate(env, distinctness=True),
                         "not_distinct_from_source")


def test_twin_outputs_fail_sibling_gate_across_packs(tmp_path):
    art = mutate(0, MIRROR_RECOLOR)
    ra = {"pack": "PackA", "cards": {
        "One": {"source": "SRC_A", "seed": 1, "ops": MIRROR_RECOLOR}}}
    env = make_env(tmp_path, ["One"], ra, {"One": art},
                   extra_packs=[("PackB", ["Twin"], None, {"Twin": art})])
    errs = errors_of(run_validate(env, distinctness=True),
                     "not_distinct_from_sibling")
    assert len(errs) == 1
    assert "Twin" in errs[0]["msg"]


def test_bad_source_key_reported_in_distinctness(tmp_path):
    recipes = {"pack": "PackA", "cards": {
        "Odd": {"source": "SRC_MISSING", "seed": 1, "ops": MIRROR_RECOLOR}}}
    env = make_env(tmp_path, ["Odd"], recipes, {"Odd": mutate(0, MIRROR_RECOLOR)})
    assert errors_of(run_validate(env, distinctness=True), "bad_source_key")
