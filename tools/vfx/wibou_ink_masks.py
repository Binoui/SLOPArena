#!/usr/bin/env python3
"""Rebuild Wibou R's approved ink-slash masks. Requires Pillow and Noto Serif CJK Black.

Run from the repository root. The presentation builder supplies the ink, green edge,
pale keyline and stamp colours. These seeded brush masks retain dry bristles and drops.
"""
from __future__ import annotations

import argparse
import math
import random
import sys
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from glyph_mask import DEFAULT_FONT, render

ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / 'client/Unity/Assets/CharacterPackages/wibou/Presentation/Source'


def brush(seed: int, bristles: int, bend: float) -> tuple[Image.Image, Image.Image]:
    rng = random.Random(seed)
    size = (1024, 384)
    mask = Image.new('L', size, 0)
    draw = ImageDraw.Draw(mask)

    def centre(u: float) -> tuple[float, float]:
        return 70 + 840 * u, 205 - bend * math.sin(math.pi * u)

    def half(u: float) -> float:
        ragged = 1 + .07 * math.sin(u * 61 + seed) + .05 * math.sin(u * 137 + 2 * seed)
        return 91 * min(1, u / .1) ** .7 * (1 - u) ** .9 * ragged

    top, bottom = [], []
    for i in range(161):
        u = i / 160
        x, y = centre(u)
        wobble = 1 + .06 * math.sin(u * 23 + seed)
        top.append((x, y - half(u) * wobble))
        bottom.append((x, y + half(u) * (2 - wobble)))
    draw.polygon(top + bottom[::-1], fill=255)
    for _ in range(9):
        u = rng.uniform(.08, .6)
        x, y = centre(u)
        y += rng.choice((-1, 1)) * (half(u) + rng.uniform(12, 48))
        r = rng.uniform(3, 9)
        draw.ellipse((x - r, y - r, x + r, y + r), fill=255)
    for _ in range(6):
        off = rng.uniform(-1, 1) * 20
        x, y = centre(.87)
        draw.line([(x, y + off), (rng.uniform(940, 990), 205 + off * 1.8)], fill=255, width=rng.randint(2, 4))
    silhouette = mask.filter(ImageFilter.MaxFilter(13)).filter(ImageFilter.MinFilter(13))
    gaps = Image.new('L', size, 0)
    for k in range(bristles):
        offset = (k + .5) / bristles * 2 - 1
        u0 = rng.uniform(.42, .65)
        points = []
        for i in range(60):
            u = u0 + (1 - u0) * i / 59
            x, y = centre(u)
            points.append((x, y + offset * half(u) * .88))
        ImageDraw.Draw(gaps).line(points, fill=255, width=rng.randint(2, 4))
    return ImageChops.subtract(mask, gaps), silhouette


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out', type=Path, default=OUTPUT)
    parser.add_argument('--font', default=DEFAULT_FONT)
    args = parser.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)
    for name, seed, bristles, bend in [('Tip', 9, 7, 25), ('Light', 4, 3, 10), ('Draw', 7, 5, 36)]:
        mask, silhouette = brush(seed, bristles, bend)
        for suffix, image in [('', mask), ('Outline', silhouette)]:
            image.save(args.out / f'QuickInk{name}{suffix}.png', optimize=True)
    render('斬', args.font, 256, .7, 0).save(args.out / 'QuickInkZan.png', optimize=True)
    chin = render('チン', args.font, 512, .83, 0)
    chin.crop((0, 128, 512, 384)).save(args.out / 'QuickInkChin.png', optimize=True)
    print(args.out)


if __name__ == '__main__':
    main()
