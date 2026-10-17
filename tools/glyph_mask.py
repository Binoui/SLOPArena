#!/usr/bin/env python3
"""Render text from a font into a square grayscale mask PNG for presentation builders.

Builders turn the mask into a signed distance field (ToonShapes.MaskField) and bake
toon-banded textures from it at any size, so glyph art matches the generated shapes.
Requires Pillow. The default font matches FightGuy's painted 肉 (paint_texture.py).

  python3 tools/glyph_mask.py 肉 client/Unity/Assets/CharacterPackages/fightguy/Presentation/Source/Niku.png
  python3 tools/glyph_mask.py 'BONK!' client/Unity/Assets/CharacterPackages/bonk/Presentation/Source/Bonk.png \
      --font client/Unity/Assets/Fonts/Baloo2-Variable.ttf --weight 800 --fill 0.86 --bounce 7
"""
from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

DEFAULT_FONT = "/usr/share/fonts/noto-cjk/NotoSerifCJK-Black.ttc"


def load(font_path: str, size: int, index: int, weight: float | None) -> ImageFont.FreeTypeFont:
    font = ImageFont.truetype(font_path, size, index=index)
    if weight is not None:
        # Variable fonts (Baloo 2): pick the weight axis value, e.g. 800 for ExtraBold.
        font.set_variation_by_axes([weight])
    return font


def bounced(text: str, font: ImageFont.FreeTypeFont, degrees: float) -> Image.Image:
    """Comic lettering: each letter tilted the other way from the last and bobbed up or down."""
    height = font.size
    tiles, advance = [], 0.0
    for i, char in enumerate(text):
        sign = -1 if i % 2 == 0 else 1
        tile = Image.new("L", (height * 2, height * 2), 0)
        ImageDraw.Draw(tile).text((height // 2, height // 4), char, fill=255, font=font)
        tile = tile.rotate(sign * degrees, resample=Image.BICUBIC, center=(height, height))
        tiles.append((tile, advance, sign * degrees * 0.006 * height))
        advance += font.getlength(char)
    canvas = Image.new("L", (int(advance + height * 2), height * 3), 0)
    for tile, x, y in tiles:
        canvas.paste(tile, (int(x), int(height * 0.5 + y)), tile)
    return canvas.crop(canvas.getbbox())


def render(text: str, font_path: str, size: int, fill: float, index: int, weight: float | None = None,
           bounce: float = 0.0) -> Image.Image:
    """White text on black, scaled so its ink box spans `fill` of the square and centred."""
    if bounce:
        ink = bounced(text, load(font_path, size, index, weight), bounce)
        scale = fill * size / max(ink.size)
        ink = ink.resize((max(1, round(ink.width * scale)), max(1, round(ink.height * scale))), Image.LANCZOS)
        image = Image.new("L", (size, size), 0)
        image.paste(ink, ((size - ink.width) // 2, (size - ink.height) // 2))
        return image
    probe = load(font_path, size, index, weight)
    left, top, right, bottom = ImageDraw.Draw(Image.new("L", (1, 1))).textbbox((0, 0), text, font=probe)
    scale = fill * size / max(right - left, bottom - top)
    font = load(font_path, max(1, round(size * scale)), index, weight)
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
    parser.add_argument("--weight", type=float, help="weight axis value for a variable font, e.g. 800")
    parser.add_argument("--bounce", type=float, default=0.0, help="tilt alternate letters by this many degrees")
    args = parser.parse_args(argv)
    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    render(args.text, args.font, args.size, args.fill, args.index, args.weight, args.bounce).save(out, optimize=True)
    print(out)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
