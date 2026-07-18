"""Synthetic test sprites for the art-mutation test suite.

Deterministic, structured, ASYMMETRIC images so perceptual hashes behave like
they do on real card art — no extracted game assets needed to run the tests.
"""

from __future__ import annotations

import io

from PIL import Image, ImageDraw


def synth_sprite(size: int = 512, variant: int = 0) -> Image.Image:
    """Painterly-ish deterministic test image: gradient sky, off-center shapes.

    Different `variant` values move/recolor the shapes so two synthetic
    sprites are as structurally unrelated as two real card arts.
    """
    img = Image.new("RGBA", (size, size))
    dr = ImageDraw.Draw(img)
    for y in range(size):
        t = y / size
        base = (
            int(30 + 60 * t + 25 * variant) % 256,
            int(24 + 90 * t + 60 * variant) % 256,
            int(70 + 120 * t + 110 * variant) % 256,
        )
        dr.line([(0, y), (size, y)], fill=base + (255,))
    s = size / 512.0
    dx = (variant * 97) % 120 * s
    # big warm blob, left of center
    dr.ellipse([50 * s + dx, 150 * s, 260 * s + dx, 360 * s],
               fill=(230 - 40 * (variant % 3), 150 + 30 * (variant % 2), 40, 255))
    # cool rectangle, upper right
    dr.rectangle([330 * s - dx, 40 * s, 470 * s - dx, 280 * s],
                 fill=(30, 170 + 20 * (variant % 3), 90 + 50 * (variant % 2), 255))
    # dark diagonal slash for gradient structure
    dr.polygon([(0, size * 0.85), (size * 0.6, size * 0.55),
                (size * 0.7, size * 0.7), (size * 0.1, size)],
               fill=(20 + 10 * variant % 40, 18, 26, 255))
    return img


def png_bytes(img: Image.Image) -> bytes:
    buf = io.BytesIO()
    img.save(buf, format="PNG", optimize=False, compress_level=6)
    return buf.getvalue()
