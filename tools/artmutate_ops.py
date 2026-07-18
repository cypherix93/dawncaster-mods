"""Deterministic Pillow image ops for the art-mutation engine (ART-MUTATION-SPEC.md §3).

Every op is a pure function `(img RGBA, params dict, ctx dict) -> img RGBA`:
  - no wall-clock, no global RNG — seeded ops draw from `ctx["rng"]`, a
    `random.Random` derived from the recipe card's `seed` + op index + op name,
    so the same recipe + same source bytes always produce identical pixels.
  - pure Pillow (no numpy) so results don't depend on optional deps.

Ops compose in recipe order via `apply_ops`. Color ops preserve the alpha
channel; geometry ops (mirror / rotate_crop / zoom_crop) transform it.

Smooth spatial masks (vignette / glow_spot / composite) are sampled on a
128×128 grid in pure Python and Lanczos-upscaled — deterministic and fast.
"""

from __future__ import annotations

import math
import random

from PIL import Image, ImageChops, ImageOps

LANCZOS = Image.Resampling.LANCZOS
BICUBIC = Image.Resampling.BICUBIC
NEAREST = Image.Resampling.NEAREST

_MASK_RES = 128  # sample resolution for smooth analytic masks


class OpError(ValueError):
    """A recipe op is unknown or mis-parameterized."""


# --------------------------------------------------------------------------- helpers

def rng_for(seed: int, op_index: int, op_name: str) -> random.Random:
    """Per-op deterministic RNG: stable even if sibling ops are re-parameterized."""
    return random.Random(f"artmutate:{seed}:{op_index}:{op_name}")


def _hex_rgb(color: str) -> tuple[int, int, int]:
    if not isinstance(color, str) or not color.startswith("#") or len(color) != 7:
        raise OpError(f"color must be '#rrggbb', got {color!r}")
    try:
        return tuple(int(color[i:i + 2], 16) for i in (1, 3, 5))  # type: ignore[return-value]
    except ValueError:
        raise OpError(f"color must be '#rrggbb', got {color!r}") from None


def _num(params: dict, key: str, default=None, lo=None, hi=None) -> float:
    if key in params:
        v = params[key]
        if not isinstance(v, (int, float)) or isinstance(v, bool):
            raise OpError(f"param {key!r} must be a number, got {v!r}")
    elif default is None:
        raise OpError(f"missing required param {key!r}")
    else:
        v = default
    v = float(v)
    if lo is not None and v < lo or hi is not None and v > hi:
        raise OpError(f"param {key!r}={v} outside [{lo}, {hi}]")
    return v


def _rgb_op(img: Image.Image, fn) -> Image.Image:
    """Run fn on the RGB planes, keep the original alpha."""
    alpha = img.getchannel("A")
    out = fn(img.convert("RGB")).convert("RGBA")
    out.putalpha(alpha)
    return out


def _clamp01(x: float) -> float:
    return 0.0 if x < 0.0 else 1.0 if x > 1.0 else x


def _smoothstep(t: float) -> float:
    t = _clamp01(t)
    return t * t * (3.0 - 2.0 * t)


def _field_mask(size: tuple[int, int], fn) -> Image.Image:
    """L-mode mask from fn(u, v) -> 0..1 over the unit square (128² samples)."""
    res = _MASK_RES
    buf = bytearray(res * res)
    for j in range(res):
        v = (j + 0.5) / res
        row = j * res
        for i in range(res):
            u = (i + 0.5) / res
            x = fn(u, v)
            buf[row + i] = 0 if x <= 0.0 else 255 if x >= 1.0 else int(round(x * 255))
    return Image.frombytes("L", (res, res), bytes(buf)).resize(size, LANCZOS)


def _center(params: dict, key: str = "center") -> tuple[float, float]:
    c = params.get(key, [0.5, 0.5])
    if (not isinstance(c, (list, tuple)) or len(c) != 2
            or not all(isinstance(x, (int, float)) and not isinstance(x, bool) for x in c)):
        raise OpError(f"param {key!r} must be [x, y] fractions, got {c!r}")
    return float(c[0]), float(c[1])


# --------------------------------------------------------------------------- geometry

def op_mirror(img, params, ctx):
    return ImageOps.mirror(img)


def op_rotate_crop(img, params, ctx):
    deg = _num(params, "degrees", lo=-30, hi=30)
    if deg == 0:
        return img
    w, h = img.size
    rot = img.rotate(deg, resample=BICUBIC, expand=False)
    rad = abs(math.radians(deg))
    sin, cos = math.sin(rad), math.cos(rad)
    # largest centered rect of the ORIGINAL aspect inside the rotated canvas:
    # s*w*cos + s*h*sin <= w and s*w*sin + s*h*cos <= h (square: 1/(cos+sin))
    s = min(w / (w * cos + h * sin), h / (w * sin + h * cos))
    inner_w = max(1, int(math.floor(w * s)))
    inner_h = max(1, int(math.floor(h * s)))
    left = (w - inner_w) // 2
    top = (h - inner_h) // 2
    return rot.crop((left, top, left + inner_w, top + inner_h)).resize((w, h), LANCZOS)


_GRAVITY = {
    "center": (0.5, 0.5), "top": (0.5, 0.0), "bottom": (0.5, 1.0),
    "left": (0.0, 0.5), "right": (1.0, 0.5),
    "top_left": (0.0, 0.0), "top_right": (1.0, 0.0),
    "bottom_left": (0.0, 1.0), "bottom_right": (1.0, 1.0),
}


def op_zoom_crop(img, params, ctx):
    factor = _num(params, "factor", lo=1.0, hi=2.0)
    grav = params.get("gravity", "center")
    if isinstance(grav, (list, tuple)):
        fx, fy = _center({"gravity": grav}, "gravity")
    elif grav in _GRAVITY:
        fx, fy = _GRAVITY[grav]
    else:
        raise OpError(f"unknown gravity {grav!r} (use {sorted(_GRAVITY)} or [fx, fy])")
    w, h = img.size
    win_w = max(1, int(round(w / factor)))
    win_h = max(1, int(round(h / factor)))
    left = int(round((w - win_w) * _clamp01(fx)))
    top = int(round((h - win_h) * _clamp01(fy)))
    return img.crop((left, top, left + win_w, top + win_h)).resize((w, h), LANCZOS)


# --------------------------------------------------------------------------- color

def op_hue_rotate(img, params, ctx):
    deg = _num(params, "degrees", lo=-360, hi=360)
    shift = int(round((deg % 360) / 360.0 * 256.0)) % 256
    if shift == 0:
        return img

    def fn(rgb):
        h, s, v = rgb.convert("HSV").split()
        h = h.point([(p + shift) % 256 for p in range(256)])
        return Image.merge("HSV", (h, s, v)).convert("RGB")

    return _rgb_op(img, fn)


def op_sat_value(img, params, ctx):
    sat = _num(params, "sat", default=1.0, lo=0.0, hi=4.0)
    val = _num(params, "value", default=1.0, lo=0.0, hi=4.0)
    gamma = _num(params, "gamma", default=1.0, lo=0.1, hi=4.0)
    s_lut = [min(255, int(round(p * sat))) for p in range(256)]
    v_lut = [min(255, int(round(((p / 255.0) ** gamma) * val * 255.0))) for p in range(256)]

    def fn(rgb):
        h, s, v = rgb.convert("HSV").split()
        return Image.merge("HSV", (h, s.point(s_lut), v.point(v_lut))).convert("RGB")

    return _rgb_op(img, fn)


def op_gradient_map(img, params, ctx):
    palette = params.get("palette")
    if not isinstance(palette, list) or not 2 <= len(palette) <= 3:
        raise OpError(f"gradient_map needs a palette of 2-3 '#rrggbb' stops, got {palette!r}")
    stops = [_hex_rgb(c) for c in palette]
    strength = _num(params, "strength", default=1.0, lo=0.0, hi=1.0)
    n = len(stops) - 1
    luts: list[list[int]] = [[], [], []]
    for i in range(256):
        t = i / 255.0 * n
        lo = min(int(t), n - 1)
        frac = t - lo
        for ch in range(3):
            a, b = stops[lo][ch], stops[lo + 1][ch]
            luts[ch].append(int(round(a + (b - a) * frac)))

    def fn(rgb):
        gray = rgb.convert("L")
        mapped = Image.merge("RGB", tuple(gray.point(luts[ch]) for ch in range(3)))
        return Image.blend(rgb, mapped, strength)

    return _rgb_op(img, fn)


def op_channel_mix(img, params, ctx):
    m = params.get("matrix")
    if (not isinstance(m, list) or len(m) != 3
            or not all(isinstance(r, list) and len(r) == 3 for r in m)):
        raise OpError(f"channel_mix needs a 3x3 matrix (list of 3 rows of 3), got {m!r}")
    flat = []
    for row in m:
        for x in row:
            if not isinstance(x, (int, float)) or isinstance(x, bool):
                raise OpError(f"channel_mix matrix entries must be numbers, got {x!r}")
            flat.append(float(x))
        flat.append(0.0)  # Pillow wants a 4x3 affine (offset column)
    return _rgb_op(img, lambda rgb: rgb.convert("RGB", tuple(flat)))


_BLEND_MODES = {
    "multiply": ImageChops.multiply,
    "screen": ImageChops.screen,
    "overlay": ImageChops.overlay,
    "soft_light": ImageChops.soft_light,
}


def op_color_wash(img, params, ctx):
    color = _hex_rgb(params.get("color", ""))
    opacity = _num(params, "opacity", lo=0.0, hi=1.0)
    mode = params.get("blend", "normal")
    if mode != "normal" and mode not in _BLEND_MODES:
        raise OpError(f"unknown blend mode {mode!r} "
                      f"(use normal/{'/'.join(sorted(_BLEND_MODES))})")

    def fn(rgb):
        solid = Image.new("RGB", rgb.size, color)
        blended = solid if mode == "normal" else _BLEND_MODES[mode](rgb, solid)
        return Image.blend(rgb, blended, opacity)

    return _rgb_op(img, fn)


# --------------------------------------------------------------------------- light

def op_vignette(img, params, ctx):
    strength = _num(params, "strength", lo=0.0, hi=1.0)
    color = _hex_rgb(params.get("color", "#000000"))
    start = _num(params, "start", default=0.45, lo=0.0, hi=0.95)
    power = _num(params, "power", default=2.0, lo=0.5, hi=6.0)
    half_diag = math.hypot(0.5, 0.5)

    def field(u, v):
        r = math.hypot(u - 0.5, v - 0.5) / half_diag  # corner == 1.0
        return (_smoothstep((r - start) / (1.0 - start)) ** power) * strength

    mask = _field_mask(img.size, field)
    return _rgb_op(img, lambda rgb: Image.composite(
        Image.new("RGB", rgb.size, color), rgb, mask))


def op_glow_spot(img, params, ctx):
    cx, cy = _center(params)
    radius = _num(params, "radius", lo=0.02, hi=1.5)
    color = _hex_rgb(params.get("color", "#ffffff"))
    opacity = _num(params, "opacity", lo=0.0, hi=1.0)

    def field(u, v):
        d = math.hypot(u - cx, v - cy) / radius
        if d >= 1.0:
            return 0.0
        return ((1.0 - d) ** 2) * opacity

    mask = _field_mask(img.size, field)

    def fn(rgb):
        lit = ImageChops.screen(rgb, Image.new("RGB", rgb.size, color))
        return Image.composite(lit, rgb, mask)

    return _rgb_op(img, fn)


# --------------------------------------------------------------------------- blend / texture

def _noise_mask(size, rng, cells: int, threshold: float, softness: float) -> Image.Image:
    cells = max(2, min(64, cells))
    raw = bytes(rng.randrange(256) for _ in range(cells * cells))
    low = Image.frombytes("L", (cells, cells), raw)
    smooth = low.resize(size, BICUBIC)
    # contrast: smoothstep window around threshold
    t0 = (threshold - softness) * 255.0
    t1 = (threshold + softness) * 255.0
    span = max(1.0, t1 - t0)
    lut = [int(round(_smoothstep((p - t0) / span) * 255)) for p in range(256)]
    return smooth.point(lut)


def op_composite(img, params, ctx):
    extras = ctx.get("extras") or []
    if not extras:
        raise OpError("composite needs the recipe 'source' to be a list of 2 sprites")
    overlay = extras[0].convert("RGBA")
    if overlay.size != img.size:
        overlay = overlay.resize(img.size, LANCZOS)
    mask_kind = params.get("mask", "radial")
    invert = bool(params.get("invert", False))
    opacity = _num(params, "opacity", default=1.0, lo=0.0, hi=1.0)

    if mask_kind == "radial":
        cx, cy = _center(params)
        radius = _num(params, "radius", default=0.5, lo=0.05, hi=1.5)
        softness = _num(params, "softness", default=0.4, lo=0.02, hi=1.0)
        r_in = radius * (1.0 - softness)

        def field(u, v):
            d = math.hypot(u - cx, v - cy)
            return 1.0 - _smoothstep((d - r_in) / max(1e-6, radius - r_in))

        mask = _field_mask(img.size, field)
    elif mask_kind == "linear":
        angle = math.radians(_num(params, "angle", default=0.0, lo=-360, hi=360))
        start = _num(params, "start", default=0.25, lo=0.0, hi=1.0)
        end = _num(params, "end", default=0.75, lo=0.0, hi=1.0)
        if end <= start:
            raise OpError("linear mask needs end > start")
        dx, dy = math.cos(angle), math.sin(angle)

        def field(u, v):
            # project onto the gradient axis, normalized so [0,1] spans the image
            p = ((u - 0.5) * dx + (v - 0.5) * dy) + 0.5
            return _smoothstep((p - start) / (end - start))

        mask = _field_mask(img.size, field)
    elif mask_kind == "noise":
        cells = int(_num(params, "scale", default=8, lo=2, hi=64))
        threshold = _num(params, "threshold", default=0.5, lo=0.0, hi=1.0)
        softness = _num(params, "softness", default=0.25, lo=0.02, hi=1.0)
        mask = _noise_mask(img.size, ctx["rng"], cells, threshold, softness)
    else:
        raise OpError(f"unknown composite mask {mask_kind!r} (radial/linear/noise)")

    if invert:
        mask = ImageChops.invert(mask)
    if opacity < 1.0:
        mask = mask.point([int(round(p * opacity)) for p in range(256)])
    return Image.composite(overlay, img, mask)


def op_grain(img, params, ctx):
    opacity = _num(params, "opacity", lo=0.0, hi=1.0)
    size = int(_num(params, "size", default=1, lo=1, hi=8))
    rng = ctx["rng"]
    w, h = img.size
    gw, gh = max(1, w // size), max(1, h // size)
    noise = Image.frombytes("L", (gw, gh), rng.randbytes(gw * gh))
    if (gw, gh) != (w, h):
        noise = noise.resize((w, h), NEAREST)
    noise_rgb = Image.merge("RGB", (noise, noise, noise))

    def fn(rgb):
        grained = ImageChops.overlay(rgb, noise_rgb)
        return Image.blend(rgb, grained, opacity)

    return _rgb_op(img, fn)


def op_posterize_soft(img, params, ctx):
    levels = int(_num(params, "levels", lo=4, hi=64))
    strength = _num(params, "strength", default=1.0, lo=0.0, hi=1.0)
    n = levels - 1
    lut = [int(round(round(p / 255.0 * n) / n * 255.0)) for p in range(256)]

    def fn(rgb):
        post = rgb.point(lut * 3)
        return post if strength >= 1.0 else Image.blend(rgb, post, strength)

    return _rgb_op(img, fn)


# --------------------------------------------------------------------------- registry

OPS = {
    "mirror": op_mirror,
    "rotate_crop": op_rotate_crop,
    "zoom_crop": op_zoom_crop,
    "hue_rotate": op_hue_rotate,
    "sat_value": op_sat_value,
    "gradient_map": op_gradient_map,
    "channel_mix": op_channel_mix,
    "color_wash": op_color_wash,
    "vignette": op_vignette,
    "glow_spot": op_glow_spot,
    "composite": op_composite,
    "grain": op_grain,
    "posterize_soft": op_posterize_soft,
}

# ops that consume ctx["rng"] — documented for recipe authors; all ops get one anyway
SEEDED_OPS = {"composite", "grain"}


def apply_ops(base: Image.Image, extra_sources: list[Image.Image],
              ops: list[dict], seed: int) -> Image.Image:
    """Apply a recipe op chain to `base` (RGBA). Deterministic for fixed inputs."""
    img = base.convert("RGBA")
    for i, op in enumerate(ops):
        if not isinstance(op, dict) or "op" not in op:
            raise OpError(f"ops[{i}] must be an object with an 'op' key, got {op!r}")
        name = op["op"]
        fn = OPS.get(name)
        if fn is None:
            raise OpError(f"ops[{i}]: unknown op {name!r} (known: {', '.join(sorted(OPS))})")
        ctx = {"seed": seed, "op_index": i, "extras": extra_sources,
               "rng": rng_for(seed, i, name)}
        try:
            img = fn(img, op, ctx)
        except OpError as e:
            raise OpError(f"ops[{i}] ({name}): {e}") from None
    return img
