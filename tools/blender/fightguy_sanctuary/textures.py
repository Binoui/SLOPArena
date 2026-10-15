"""Original procedural textures for the Himalayan Sanctuary.

atlas: thangka (u 0..0.5, v 0..0.75), prayer-flag prints (u 0.5..1, v 0..0.5),
       solid white (u 0.75..1, v 0.75..1) for untextured faces.
detail: tileable grayscale grit for the stage shader's triplanar detail.
"""
from __future__ import annotations

import math
import random

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

SIZE = 2048


def _fist(draw, cx, cy, s, fill, outline, width):
    """FightGuy's rising fist (original blocky emblem), centred at cx, cy, scale s."""
    def rr(x0, y0, x1, y1, r):
        draw.rounded_rectangle([cx + x0 * s, cy + y0 * s, cx + x1 * s, cy + y1 * s], r * s, fill=fill, outline=outline, width=width)
    # fingers (back of the fist, knuckles up)
    rr(-0.62, -1.02, -0.22, -0.40, 0.12)
    rr(-0.26, -1.18, 0.14, -0.40, 0.12)
    rr(0.10, -1.08, 0.50, -0.40, 0.12)
    rr(0.46, -0.92, 0.84, -0.36, 0.12)
    rr(-0.70, -0.62, 0.86, 0.38, 0.18)       # palm block
    draw.polygon([(cx - 0.70 * s, cy - 0.30 * s), (cx - 1.05 * s, cy - 0.62 * s), (cx - 1.22 * s, cy - 0.30 * s),
                  (cx - 0.94 * s, cy + 0.18 * s), (cx - 0.62 * s, cy + 0.30 * s)], fill=fill, outline=outline)
    rr(-0.46, 0.30, 0.60, 1.05, 0.08)        # wrist
    # FightGuy's red headband, tied around the wrist with two tails in the wind
    red, dark = (206, 34, 28), (120, 16, 14)
    draw.rectangle([cx - 0.50 * s, cy + 0.52 * s, cx + 0.64 * s, cy + 0.74 * s], fill=red, outline=dark, width=width)
    draw.polygon([(cx + 0.60 * s, cy + 0.56 * s), (cx + 1.55 * s, cy + 0.20 * s), (cx + 1.70 * s, cy + 0.42 * s), (cx + 0.64 * s, cy + 0.72 * s)], fill=red, outline=dark)
    draw.polygon([(cx + 0.60 * s, cy + 0.66 * s), (cx + 1.38 * s, cy + 0.86 * s), (cx + 1.30 * s, cy + 1.06 * s), (cx + 0.58 * s, cy + 0.76 * s)], fill=red, outline=dark)
    for x0, x1, y1 in ((-0.80, -0.52, 2.15), (0.0, 0.18, 2.45), (0.70, 0.98, 2.05)):
        draw.polygon([(cx + x0 * s, cy + 1.15 * s), (cx + x1 * s, cy + 1.15 * s), (cx + (x0 + x1) / 2 * s, cy + y1 * s)], fill=fill, outline=outline)


def _flame(draw, x, y, angle, length, width, fill):
    pts = []
    for k in range(13):
        t = k / 12
        w = width * math.sin(math.pi * t) * (1 - t * 0.55)
        pts.append((t, w))
    poly = []
    for t, w in pts:
        px = x + math.cos(angle) * length * t - math.sin(angle) * w
        py = y + math.sin(angle) * length * t + math.cos(angle) * w
        poly.append((px, py))
    for t, w in reversed(pts):
        px = x + math.cos(angle) * length * t + math.sin(angle) * w * 0.6
        py = y + math.sin(angle) * length * t - math.cos(angle) * w * 0.6
        poly.append((px, py))
    draw.polygon(poly, fill=fill)


def thangka(w=1024, h=1344, ss=2):
    W, H = w * ss, h * ss
    img = Image.new("RGB", (W, H), (28, 42, 88))
    d = ImageDraw.Draw(img)
    rng = random.Random(7)
    # outer brocade pattern
    for y in range(0, H, 28 * ss):
        for x in range(0, W, 28 * ss):
            o = 14 * ss if (y // (28 * ss)) % 2 else 0
            cx, cy = x + o, y
            d.polygon([(cx, cy - 7 * ss), (cx + 7 * ss, cy), (cx, cy + 7 * ss), (cx - 7 * ss, cy)], fill=(176, 132, 52))
    B = 64 * ss
    # rainbow borders
    for k, col in enumerate([(214, 170, 60), (180, 32, 26), (226, 196, 80), (32, 120, 90)]):
        inset = B + k * 7 * ss
        d.rectangle([inset, inset + 150 * ss, W - inset, H - inset - 120 * ss], outline=col, width=7 * ss)
    fx0, fy0, fx1, fy1 = B + 28 * ss, B + 178 * ss, W - B - 28 * ss, H - B - 148 * ss
    # main field: deep red with a soft vignette
    field = Image.new("RGB", (fx1 - fx0, fy1 - fy0), (140, 26, 20))
    fd = ImageDraw.Draw(field)
    for k in range(40):
        a = k / 40
        fd.rectangle([int(a * 60 * ss), int(a * 60 * ss), field.width - int(a * 60 * ss), field.height - int(a * 60 * ss)],
                     outline=(int(110 + 30 * a), int(18 + 8 * a), int(16 + 4 * a)), width=2 * ss)
    img.paste(field, (fx0, fy0))
    # canopy valance across the top
    stripes = [(36, 90, 170), (230, 190, 60), (190, 36, 28), (40, 140, 80), (240, 236, 220)]
    sy = B
    for k, col in enumerate(stripes):
        d.rectangle([B, sy, W - B, sy + 24 * ss], fill=col)
        sy += 24 * ss
    for x in range(B, W - B, 48 * ss):
        d.pieslice([x, sy - 26 * ss, x + 48 * ss, sy + 26 * ss], 0, 180, fill=stripes[(x // (48 * ss)) % 5])
    cx, cy = W // 2, int(H * 0.47)
    R = int(min(W, H) * 0.36)
    # halo flames
    for k in range(36):
        a = 2 * math.pi * k / 36
        _flame(d, cx + math.cos(a) * R * 0.92, cy + math.sin(a) * R * 0.92, a + 0.25, R * 0.36, R * 0.07,
               (230, 150, 40) if k % 2 else (250, 210, 80))
    d.ellipse([cx - R, cy - R, cx + R, cy + R], fill=(214, 166, 52))
    d.ellipse([cx - R * 0.94, cy - R * 0.94, cx + R * 0.94, cy + R * 0.94], fill=(24, 54, 118))
    d.ellipse([cx - R * 0.90, cy - R * 0.90, cx + R * 0.90, cy + R * 0.90], fill=(34, 132, 100))
    # lotus petals
    for k in range(16):
        a = 2 * math.pi * k / 16
        px, py = cx + math.cos(a) * R * 0.72, cy + math.sin(a) * R * 0.72
        d.ellipse([px - R * 0.16, py - R * 0.16, px + R * 0.16, py + R * 0.16], fill=(236, 186, 170) if k % 2 else (250, 226, 200))
    d.ellipse([cx - R * 0.66, cy - R * 0.66, cx + R * 0.66, cy + R * 0.66], fill=(14, 28, 70))
    # blue ki flames swirling around the fist
    for k in range(22):
        a = 2 * math.pi * k / 22
        r0 = R * (0.28 + 0.05 * (k % 3))
        _flame(d, cx + math.cos(a) * r0, cy + math.sin(a) * r0, a + 0.9, R * 0.34, R * 0.06,
               (90, 200, 255) if k % 2 else (190, 240, 255))
    d.ellipse([cx - R * 0.42, cy - R * 0.42, cx + R * 0.42, cy + R * 0.42], fill=(20, 40, 96))
    _fist(d, cx, cy - R * 0.08, R * 0.25, (246, 196, 66), (90, 50, 10), 3 * ss)
    # lotus throne and waves under the mandala
    ty = cy + R + 20 * ss
    for k in range(9):
        x = cx + (k - 4) * R * 0.2
        d.ellipse([x - R * 0.14, ty - R * 0.1, x + R * 0.14, ty + R * 0.12], fill=(240, 200, 190) if k % 2 else (220, 120, 120))
    for row in range(4):
        y = fy1 - (row + 1) * 46 * ss
        col = [(30, 90, 150), (40, 120, 170), (60, 150, 190), (110, 190, 210)][row]
        for x in range(fx0 - 40 * ss, fx1 + 40 * ss, 80 * ss):
            d.pieslice([x, y, x + 80 * ss, y + 80 * ss], 180, 360, fill=col)
    # small white clouds in the upper corners
    for (x, y) in ((fx0 + 110 * ss, fy0 + 120 * ss), (fx1 - 110 * ss, fy0 + 120 * ss), (fx0 + 120 * ss, cy + R * 0.2), (fx1 - 120 * ss, cy + R * 0.2)):
        for k in range(5):
            r = (34 - k * 4) * ss
            d.ellipse([x + (k - 2) * 26 * ss - r, y - r + (k % 2) * 10 * ss, x + (k - 2) * 26 * ss + r, y + r + (k % 2) * 10 * ss], fill=(236, 240, 246))
    img = img.resize((w, h), Image.LANCZOS)
    return img


def flag_print(seed, size=256):
    rng = random.Random(seed)
    img = Image.new("RGB", (size * 2, size * 2), (255, 255, 255))
    d = ImageDraw.Draw(img)
    s = size * 2
    ink = (84, 80, 78)
    d.rectangle([18, 18, s - 18, s - 18], outline=ink, width=6)
    kind = seed % 3
    if kind == 0:  # wind horse carrying the jewel
        cx, cy = s * 0.5, s * 0.55
        d.ellipse([cx - 90, cy - 40, cx + 90, cy + 40], fill=ink)
        d.polygon([(cx + 60, cy - 20), (cx + 120, cy - 110), (cx + 150, cy - 95), (cx + 100, cy - 10)], fill=ink)
        d.ellipse([cx + 118, cy - 140, cx + 178, cy - 92], fill=ink)
        for lx in (-70, -40, 40, 70):
            d.line([(cx + lx, cy + 20), (cx + lx + rng.randint(-15, 15), cy + 120)], fill=ink, width=14)
        d.line([(cx - 90, cy - 10), (cx - 160, cy + 40)], fill=ink, width=12)
        d.ellipse([cx - 30, cy - 130, cx + 30, cy - 70], outline=ink, width=8)
        for k in range(6):
            a = k * math.pi / 3
            d.line([(cx + 34 * math.cos(a), cy - 100 + 34 * math.sin(a)), (cx + 56 * math.cos(a), cy - 100 + 56 * math.sin(a))], fill=ink, width=6)
    elif kind == 1:  # flaming jewel
        cx, cy = s * 0.5, s * 0.5
        for k in range(10):
            a = k * math.pi / 5
            _flame(d, cx + 60 * math.cos(a), cy + 60 * math.sin(a), a, 90, 22, ink)
        d.ellipse([cx - 64, cy - 64, cx + 64, cy + 64], fill=(255, 255, 255), outline=ink, width=10)
        d.ellipse([cx - 26, cy - 26, cx + 26, cy + 26], fill=ink)
    rows = 5 if kind == 2 else 3
    y0 = 60 if kind == 2 else s - 150
    for r in range(rows):
        y = y0 + r * (76 if kind == 2 else 40)
        x = 48
        while x < s - 70:
            l = rng.randint(18, 64)
            d.line([(x, y), (min(s - 48, x + l), y)], fill=ink, width=8)
            d.line([(x, y), (x, y + 18)], fill=ink, width=6)
            x += l + rng.randint(12, 26)
    return img.resize((size, size), Image.LANCZOS).filter(ImageFilter.GaussianBlur(0.6))


def atlas(path):
    img = Image.new("RGB", (SIZE, SIZE), (255, 255, 255))
    img.paste(thangka(), (0, SIZE - 1344))  # u 0..0.5, v 0..0.65625
    for k in range(16):
        i, j = k % 4, k // 4
        img.paste(flag_print(k), (1024 + i * 256, SIZE - (j + 1) * 256))
    img.save(path)
    return path


def detail(path, size=512, seed=3):
    rng = np.random.default_rng(seed)
    noise = rng.standard_normal((size, size))
    f = np.fft.fft2(noise)
    fy = np.fft.fftfreq(size)[:, None]
    fx = np.fft.fftfreq(size)[None, :]
    r = np.sqrt(fx * fx + fy * fy) + 1.0 / size
    field = np.real(np.fft.ifft2(f / r ** 1.1))
    field = (field - field.mean()) / (field.std() + 1e-9)
    speck = rng.standard_normal((size, size)) * 0.35
    v = 0.5 + 0.16 * field + 0.05 * speck
    v = np.clip(v, 0, 1)
    Image.fromarray((v * 255).astype(np.uint8), "L").save(path)
    return path


if __name__ == "__main__":
    import sys
    out = sys.argv[1] if len(sys.argv) > 1 else "."
    atlas(out + "/SanctuaryAtlas.png")
    detail(out + "/SanctuaryDetail.png")
