"""Op vocabulary tests: basic correctness of all 13 ops + determinism (spec §3)."""

import pytest
from PIL import Image

import artmutate_ops as ops
from artmutate import encode_png
from tests.synthart import synth_sprite


@pytest.fixture(scope="module")
def src():
    return synth_sprite(512, variant=0)


@pytest.fixture(scope="module")
def src_b():
    return synth_sprite(512, variant=3)


def run(img, op, extras=(), seed=700000001):
    return ops.apply_ops(img, list(extras), [op], seed)


# ------------------------------------------------------------------ geometry

def test_mirror_flips_pixels(src):
    out = run(src, {"op": "mirror"})
    w = src.width
    for x, y in ((3, 10), (100, 200), (400, 480)):
        assert out.getpixel((x, y)) == src.getpixel((w - 1 - x, y))


def test_rotate_crop_keeps_size_and_changes_image(src):
    out = run(src, {"op": "rotate_crop", "degrees": 6})
    assert out.size == src.size and out.mode == "RGBA"
    assert out.tobytes() != src.tobytes()


def test_rotate_crop_zero_is_identity(src):
    assert run(src, {"op": "rotate_crop", "degrees": 0}).tobytes() == src.tobytes()


def test_rotate_crop_preserves_nonsquare_canvas():
    # portrait canvas (weapon art is 512×873): the re-crop must keep the
    # original aspect, not collapse to the inscribed square
    tall = synth_sprite(512, variant=1).resize((256, 436))
    out = run(tall, {"op": "rotate_crop", "degrees": 6})
    assert out.size == (256, 436) and out.mode == "RGBA"


def test_zoom_crop_top_left_magnifies_that_corner(src):
    out = run(src, {"op": "zoom_crop", "factor": 2.0, "gravity": "top_left"})
    assert out.size == src.size
    # pixel at (100,100) of output ≈ source pixel at (50,50)
    op_px = out.getpixel((100, 100))
    sp_px = src.getpixel((50, 50))
    assert all(abs(a - b) <= 12 for a, b in zip(op_px[:3], sp_px[:3]))


def test_zoom_crop_bad_gravity_raises(src):
    with pytest.raises(ops.OpError, match="gravity"):
        run(src, {"op": "zoom_crop", "factor": 1.2, "gravity": "sideways"})


# ------------------------------------------------------------------ color

def test_hue_rotate_red_to_green():
    red = Image.new("RGBA", (32, 32), (255, 0, 0, 255))
    out = run(red, {"op": "hue_rotate", "degrees": 120})
    r, g, b, a = out.getpixel((16, 16))
    assert g > 200 and r < 60 and b < 60 and a == 255


def test_hue_rotate_zero_identity(src):
    assert run(src, {"op": "hue_rotate", "degrees": 0}).tobytes() == src.tobytes()


def test_hue_rotate_preserves_value_plane(src):
    out = run(src, {"op": "hue_rotate", "degrees": 137})
    v0 = src.convert("RGB").convert("HSV").split()[2]
    v1 = out.convert("RGB").convert("HSV").split()[2]
    assert v0.tobytes() == v1.tobytes()  # distinctness gate relies on this


def test_sat_value_desaturates_to_gray(src):
    out = run(src, {"op": "sat_value", "sat": 0.0})
    for xy in ((10, 10), (256, 300), (500, 470)):
        r, g, b, _ = out.getpixel(xy)
        assert r == g == b


def test_gradient_map_two_stop_black_white_is_monochrome(src):
    out = run(src, {"op": "gradient_map", "palette": ["#000000", "#ffffff"],
                    "strength": 1.0})
    for xy in ((10, 10), (256, 300), (480, 100)):
        r, g, b, _ = out.getpixel(xy)
        assert r == g == b


def test_gradient_map_bad_palette_raises(src):
    with pytest.raises(ops.OpError, match="palette"):
        run(src, {"op": "gradient_map", "palette": ["#000000"]})
    with pytest.raises(ops.OpError, match="rrggbb"):
        run(src, {"op": "gradient_map", "palette": ["black", "white"]})


def test_channel_mix_identity(src):
    out = run(src, {"op": "channel_mix",
                    "matrix": [[1, 0, 0], [0, 1, 0], [0, 0, 1]]})
    assert out.tobytes() == src.tobytes()


def test_channel_mix_swaps_channels():
    img = Image.new("RGBA", (8, 8), (200, 40, 90, 255))
    out = run(img, {"op": "channel_mix",
                    "matrix": [[0, 1, 0], [1, 0, 0], [0, 0, 1]]})
    assert out.getpixel((4, 4)) == (40, 200, 90, 255)


def test_color_wash_full_opacity_normal_is_solid(src):
    out = run(src, {"op": "color_wash", "color": "#c2571f", "opacity": 1.0})
    assert out.getpixel((100, 100)) == (0xC2, 0x57, 0x1F, 255)
    assert out.getpixel((400, 400)) == (0xC2, 0x57, 0x1F, 255)


def test_color_wash_multiply_darkens(src):
    out = run(src, {"op": "color_wash", "color": "#404040", "opacity": 1.0,
                    "blend": "multiply"})
    for xy in ((100, 100), (300, 400)):
        assert sum(out.getpixel(xy)[:3]) <= sum(src.getpixel(xy)[:3])


def test_color_wash_unknown_blend_raises(src):
    with pytest.raises(ops.OpError, match="blend"):
        run(src, {"op": "color_wash", "color": "#ffffff", "opacity": 0.5,
                  "blend": "dodge"})


# ------------------------------------------------------------------ light

def test_vignette_darkens_corners_not_center(src):
    out = run(src, {"op": "vignette", "strength": 0.8})
    corner_before = sum(src.getpixel((5, 5))[:3])
    corner_after = sum(out.getpixel((5, 5))[:3])
    assert corner_after < corner_before
    assert out.getpixel((256, 256)) == src.getpixel((256, 256))


def test_glow_spot_brightens_center_only(src):
    op = {"op": "glow_spot", "center": [0.5, 0.5], "radius": 0.2,
          "color": "#ffffff", "opacity": 0.8}
    out = run(src, op)
    assert sum(out.getpixel((256, 256))[:3]) > sum(src.getpixel((256, 256))[:3])
    assert out.getpixel((5, 5)) == src.getpixel((5, 5))


# ------------------------------------------------------------------ blend / texture

def test_composite_radial_center_shows_overlay(src):
    overlay = Image.new("RGBA", (512, 512), (10, 250, 10, 255))
    op = {"op": "composite", "mask": "radial", "center": [0.5, 0.5],
          "radius": 0.4, "softness": 0.2}
    out = run(src, op, extras=[overlay])
    assert out.getpixel((256, 256)) == (10, 250, 10, 255)
    assert out.getpixel((5, 5)) == src.getpixel((5, 5))


def test_composite_linear_blends_across(src):
    overlay = Image.new("RGBA", (512, 512), (250, 10, 10, 255))
    op = {"op": "composite", "mask": "linear", "angle": 0,
          "start": 0.3, "end": 0.7}
    out = run(src, op, extras=[overlay])
    assert out.getpixel((5, 256)) == src.getpixel((5, 256))       # before ramp
    assert out.getpixel((506, 256)) == (250, 10, 10, 255)          # after ramp


def test_composite_noise_is_seeded(src, src_b):
    op = {"op": "composite", "mask": "noise", "scale": 6}
    a = ops.apply_ops(src, [src_b], [op], 111)
    b = ops.apply_ops(src, [src_b], [op], 111)
    c = ops.apply_ops(src, [src_b], [op], 222)
    assert a.tobytes() == b.tobytes()
    assert a.tobytes() != c.tobytes()


def test_composite_without_second_source_raises(src):
    with pytest.raises(ops.OpError, match="source"):
        run(src, {"op": "composite", "mask": "radial"})


def test_grain_seeded_and_visible(src):
    op = {"op": "grain", "opacity": 0.3}
    a = ops.apply_ops(src, [], [op], 111)
    b = ops.apply_ops(src, [], [op], 111)
    c = ops.apply_ops(src, [], [op], 222)
    assert a.tobytes() == b.tobytes()
    assert a.tobytes() != c.tobytes()
    assert a.tobytes() != src.tobytes()


def test_posterize_soft_reduces_levels(src):
    out = run(src, {"op": "posterize_soft", "levels": 8})
    reds = {px for px in out.getchannel("R").tobytes()}
    assert len(reds) <= 8


# ------------------------------------------------------------------ chain plumbing

def test_unknown_op_raises_with_index(src):
    with pytest.raises(ops.OpError, match=r"ops\[1\].*sparkle"):
        ops.apply_ops(src, [], [{"op": "mirror"}, {"op": "sparkle"}], 1)


def test_missing_param_raises(src):
    with pytest.raises(ops.OpError, match="degrees"):
        run(src, {"op": "hue_rotate"})


def test_alpha_preserved_by_color_ops():
    img = synth_sprite(64)
    img.putalpha(Image.new("L", (64, 64), 130))
    out = ops.apply_ops(img, [], [
        {"op": "hue_rotate", "degrees": 90},
        {"op": "vignette", "strength": 0.4},
        {"op": "gradient_map", "palette": ["#102030", "#f5c469"], "strength": 0.5},
    ], 1)
    assert out.getchannel("A").tobytes() == img.getchannel("A").tobytes()


def test_full_chain_byte_identical_across_runs(src, src_b):
    chain = [
        {"op": "mirror"},
        {"op": "rotate_crop", "degrees": -5},
        {"op": "zoom_crop", "factor": 1.15, "gravity": "top"},
        {"op": "hue_rotate", "degrees": -18},
        {"op": "sat_value", "sat": 1.1, "value": 0.95},
        {"op": "gradient_map", "palette": ["#1a1210", "#c2571f", "#f5c469"],
         "strength": 0.5},
        {"op": "channel_mix", "matrix": [[0.9, 0.1, 0], [0, 1, 0], [0.05, 0, 0.95]]},
        {"op": "color_wash", "color": "#2f4f4f", "opacity": 0.15, "blend": "overlay"},
        {"op": "composite", "mask": "noise", "scale": 8, "threshold": 0.5},
        {"op": "glow_spot", "center": [0.5, 0.42], "radius": 0.35,
         "color": "#ff9c3d", "opacity": 0.3},
        {"op": "grain", "opacity": 0.12},
        {"op": "posterize_soft", "levels": 32, "strength": 0.6},
        {"op": "vignette", "strength": 0.25},
    ]
    one = encode_png(ops.apply_ops(src, [src_b], chain, 700000042))
    two = encode_png(ops.apply_ops(src, [src_b], chain, 700000042))
    assert one == two
