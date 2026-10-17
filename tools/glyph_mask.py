#!/usr/bin/env python3
"""Render text from a font into a square grayscale mask PNG for presentation builders.

Builders turn the mask into a signed distance field (ToonShapes.MaskField) and bake
toon-banded textures from it at any size, so glyph art matches the generated shapes.
Requires Pillow. The default font matches FightGuy's painted 肉 (paint_texture.py).

  python3 tools/glyph_mask.py 肉 client/Unity/Assets/CharacterPackages/fightguy/Presentation/Source/Niku.png
"""
from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

DEFAULT_FONT = "/usr/share/fonts/noto-cjk/NotoSerifCJK-Black.ttc"


def render(text: str, font_path: str, size: int, fill: float, index: int) -> Image.Image:
    """White text on black, scaled so its ink box spans `fill` of the square and centred."""
    probe = ImageFont.truetype(font_path, size, index=index)
    left, top, right, bottom = ImageDraw.Draw(Image.new("L", (1, 1))).textbbox((0, 0), text, font=probe)
    scale = fill * size / max(right - left, bottom - top)
    font = ImageFont.truetype(font_path, max(1, round(size * scale)), index=index)
    image = Image.new("L", (size, size), 0)
    draw = ImageDraw.Draw(image)
    left, top, right, bottom = draw.textbbox((0, 0), text, font=font)
    draw.text(((size - (right - left)) / 2 - left, (size - (bottom - top)) / 2 - top), text, fill=255, font=font)
    return image


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("text", help="the glyph or short text to render")
    parser.add_argument("out", help="output PNG path")
    parser.add_argument("--font", default=DEFAULT_FONT, help="TrueType/OpenType font or collection")
    parser.add_argument("--index", type=int, default=0, help="face index inside a font collection")
    parser.add_argument("--size", type=int, default=512, help="square mask size in pixels")
    parser.add_argument("--fill", type=float, default=0.8, help="share of the square the ink box spans")
    args = parser.parse_args(argv)
    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    render(args.text, args.font, args.size, args.fill, args.index).save(out, optimize=True)
    print(out)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
