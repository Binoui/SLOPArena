"""Original procedural textures for the Slopball Night Court (Pillow + numpy).

atlas  (4096 x 4096, RGBA; alpha = StageNeon glow mask): the hero lettering band, the
       mascot mural, the side mural, sponsor dasher boards, the SLOP CLUB neon, the
       combo board's digit cards, flood LED panels, speaker cones, the vending machine,
       EXIT sign, concrete wall patches, the roll-up door and the solid white block that
       geo.WHITE_UV samples. Murals carry their concrete background: StageNeon is opaque.
floor  (2048 x 2048, RGB): the court, planar over the 40 x 40 m room (north up).
detail (512 x 512, gray): tileable grit for StageVertexLit's triplanar detail.

The art follows the Slopball poster (client/Unity/Assets/UI/Arcade/SlopballPoster.png):
halftone dots, ink splatter, speed streaks, a teal/terracotta court and the club's
smiling-ball mascot. Lettering uses the project's Archivo Black, Baloo 2 and Space Mono
(OFL, client/Unity/Assets/Fonts). Everything is drawn here.
"""
from __future__ import annotations

import math
import random
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))  # tools/blender: the shared stagekit
from stagekit import noise  # noqa: E402

REPO = Path(__file__).resolve().parents[3]
FONTS = REPO / "client/Unity/Assets/Fonts"
DISPLAY = str(FONTS / "ArchivoBlack-Regular.ttf")
ROUND = str(FONTS / "Baloo2-Variable.ttf")
MONO = str(FONTS / "SpaceMono-Regular.ttf")

SIZE = 4096
TEAL = (43, 179, 163)
TEAL_DARK = (20, 92, 96)
CORAL = (255, 107, 74)
YELLOW = (255, 210, 63)
CREAM = (244, 236, 216)
INK = (16, 24, 26)
MINT = (92, 190, 150)
PINK = (255, 79, 154)
CYAN = (60, 220, 255)
LIME = (190, 255, 70)

# name -> (x, y, w, h) in atlas pixels (y down). UVs flip v (see uv()).
CELLS = {
    "mural": (0, 0, 2048, 1024),          # mascot mural, 31 x 15.5 m
    "side": (2048, 0, 2048, 512),         # KEEP IT MOVING, 28 x 7 m
    "neon": (2048, 512, 1024, 512),       # SLOP CLUB / OPEN LATE, 12 x 6 m
    "boards": (3072, 512, 1024, 1024),    # eight 1024 x 128 dasher-board ads
    "band": (0, 1024, 3072, 512),         # SLOPBALL lettering band, 42 x 5.25 m
    "digits": (0, 1536, 11 * 128, 192),   # 0-9 and a blank card, 128 x 192 each
    "combo": (1408, 1536, 512, 128),      # COMBO header plate
    "flood": (1920, 1536, 256, 256),      # LED flood panel face
    "speaker": (2176, 1536, 256, 256),    # speaker cone
    "vending": (2432, 1536, 256, 512),    # vending machine front
    "exit": (2688, 1536, 256, 128),       # EXIT sign
    "concrete_a": (0, 2048, 1024, 1024),  # wall patches, mapped 3 m square
    "concrete_b": (1024, 2048, 1024, 1024),
    "concrete_c": (2048, 2048, 1024, 1024),
    "door": (3072, 2048, 1024, 1024),     # roll-up door with a tag
    "white": (3968, 3968, 128, 128),
}
BOARD_ROWS = 8
DIGIT_CELL = 128  # pixels; the runtime steps _BaseMap_ST.z by DIGIT_CELL / SIZE per digit


def uv(name, inset=2):
    """(u0, v0, u1, v1) of a cell, inset by `inset` pixels against bleeding."""
    x, y, w, h = CELLS[name]
    return ((x + inset) / SIZE, 1 - (y + h - inset) / SIZE, (x + w - inset) / SIZE, 1 - (y + inset) / SIZE)


def sub_uv(name, fu0, fv0, fu1, fv1, inset=2):
    """A fractional sub-rectangle of a cell (fv measured upwards from the cell bottom)."""
    u0, v0, u1, v1 = uv(name, inset)
    return (u0 + (u1 - u0) * fu0, v0 + (v1 - v0) * fv0, u0 + (u1 - u0) * fu1, v0 + (v1 - v0) * fv1)


def board_uv(row, inset=3):
    x, y, w, h = CELLS["boards"]
    ry = y + row * (h // BOARD_ROWS)
    rh = h // BOARD_ROWS
    return ((x + inset) / SIZE, 1 - (ry + rh - inset) / SIZE, (x + w - inset) / SIZE, 1 - (ry + inset) / SIZE)


def digit_uv(k=0, inset=3):
    x, y, w, h = CELLS["digits"]
    cx = x + k * DIGIT_CELL
    return ((cx + inset) / SIZE, 1 - (y + h - inset) / SIZE, (cx + DIGIT_CELL - inset) / SIZE, 1 - (y + inset) / SIZE)


def font(path, size):
    return ImageFont.truetype(path, size)


def outlined(d, xy, text, f, fill, outline, width, anchor="mm"):
    d.text(xy, text, font=f, fill=fill, anchor=anchor, stroke_width=width, stroke_fill=outline)


# ------------------------------------------------------------------ motifs

def mascot(size=512, body=MINT, wink=True):
    """The club's lopsided smiling ball: cream rim, mint body, ink wink and grin."""
    im = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    s = size / 512
    pts = []
    for k in range(64):
        a = k / 64 * math.tau
        r = 225 + 9 * math.sin(a * 3 + 0.6) + 5 * math.sin(a * 5 + 2.0)
        pts.append((256 * s + math.cos(a) * r * s, 262 * s + math.sin(a) * r * 0.95 * s))
    d.polygon(pts, fill=CREAM)
    d.polygon([(256 * s + (x - 256 * s) * 0.9, 262 * s + (y - 262 * s) * 0.9) for x, y in pts], fill=body)
    d.arc((60 * s, 40 * s, 470 * s, 300 * s), 200, 330, fill=tuple(int(c * 0.8) for c in body), width=int(16 * s))
    if wink:
        d.arc((120 * s, 190 * s, 220 * s, 260 * s), 200, 340, fill=INK, width=int(18 * s))
    else:
        d.ellipse((150 * s, 185 * s, 200 * s, 265 * s), fill=INK)
    d.ellipse((290 * s, 170 * s, 345 * s, 265 * s), fill=INK)
    d.ellipse((305 * s, 185 * s, 322 * s, 205 * s), fill=CREAM)
    d.chord((150 * s, 250 * s, 370 * s, 400 * s), 10, 170, fill=INK)
    d.chord((205 * s, 330 * s, 315 * s, 400 * s), 20, 160, fill=CORAL)
    d.ellipse((100 * s, 280 * s, 160 * s, 312 * s), fill=CORAL)
    d.ellipse((370 * s, 275 * s, 430 * s, 307 * s), fill=CORAL)
    return im


def halftone(d, box, color, step, rmax, direction=(1, 0)):
    x0, y0, x1, y1 = box
    for y in range(int(y0), int(y1), step):
        for x in range(int(x0), int(x1), step):
            t = ((x - x0) / max(1, x1 - x0)) * direction[0] + ((y - y0) / max(1, y1 - y0)) * direction[1]
            r = rmax * max(0.0, min(1.0, t))
            if r > 0.6:
                off = step / 2 if (y // step) % 2 else 0
                d.ellipse((x + off - r, y - r, x + off + r, y + r), fill=color)


def splatter(d, cx, cy, r, color, rng, drips=0, drip_len=80):
    d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=color)
    for _ in range(int(r / 3) + 6):
        a = rng.uniform(0, math.tau)
        dist = rng.uniform(r * 0.8, r * 2.1)
        rr = rng.uniform(r * 0.05, r * 0.28)
        d.ellipse((cx + math.cos(a) * dist - rr, cy + math.sin(a) * dist - rr,
                   cx + math.cos(a) * dist + rr, cy + math.sin(a) * dist + rr), fill=color)
    for _ in range(drips):
        x = cx + rng.uniform(-r * 0.7, r * 0.7)
        w = rng.uniform(4, 10)
        L = rng.uniform(drip_len * 0.4, drip_len)
        d.rounded_rectangle((x - w / 2, cy, x + w / 2, cy + r * 0.5 + L), radius=w / 2, fill=color)


def speed_lines(d, box, color, rng, n=18, width=(6, 22)):
    x0, y0, x1, y1 = box
    for _ in range(n):
        y = rng.uniform(y0, y1)
        L = rng.uniform((x1 - x0) * 0.25, (x1 - x0) * 0.8)
        x = rng.uniform(x0, x1 - L)
        w = rng.uniform(*width)
        d.polygon([(x, y), (x + L, y - w / 2), (x + L, y + w / 2)], fill=color)


def burst(d, cx, cy, R, fill, word, size):
    star = []
    for k in range(24):
        r = R if k % 2 == 0 else R * 0.55
        a = k / 24 * math.tau - math.pi / 2
        star.append((cx + math.cos(a) * r, cy + math.sin(a) * r))
    d.polygon(star, fill=fill, outline=INK)
    outlined(d, (cx, cy), word, font(DISPLAY, size), CREAM, INK, max(4, size // 14))


def concrete(w, h, seed, base=(118, 118, 120), stains=True):
    """Weathered cast concrete: soft mottling, form-tie dots, vertical water streaks."""
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float64)
    m = noise.fbm(xs / 140.0, ys / 140.0, seed, octaves=5)
    fine = noise.fbm(xs / 9.0, ys / 9.0, seed + 7, octaves=2)
    v = 0.86 + 0.22 * (m - 0.5) + 0.06 * (fine - 0.5)
    if stains:
        streak = noise.fbm(xs / 38.0, ys / 900.0, seed + 31, octaves=3)
        v -= 0.12 * np.clip((streak - 0.55) * 3.0, 0, 1) * (0.4 + 0.6 * ys / h)
    rgb = np.clip(np.stack([v * base[0], v * base[1], v * base[2]], -1), 0, 255).astype(np.uint8)
    im = Image.fromarray(rgb, "RGB")
    d = ImageDraw.Draw(im)
    for gx in (w * 0.25, w * 0.75):
        for gy in (h * 0.25, h * 0.75):
            d.ellipse((gx - 6, gy - 6, gx + 6, gy + 6), fill=tuple(int(c * 0.62) for c in base))
    return im


def _paste(atlas, name, im):
    x, y, w, h = CELLS[name]
    atlas.paste(im.resize((w, h), Image.LANCZOS) if im.size != (w, h) else im, (x, y))


def _with_concrete(w, h, seed, art, fade=0.0):
    """Composite RGBA art over a concrete patch; optionally fade the paint at the edges."""
    base = concrete(w, h, seed).convert("RGBA")
    if fade > 0:
        a = np.asarray(art).astype(np.float64)
        ys, xs = np.mgrid[0:h, 0:w]
        edge = np.minimum(np.minimum(xs, w - 1 - xs) / (w * fade), np.minimum(ys, h - 1 - ys) / (h * fade))
        a[..., 3] *= np.clip(edge, 0, 1)
        art = Image.fromarray(a.astype(np.uint8), "RGBA")
    # weathered paint: knock a little concrete grain back through the art
    grain = np.asarray(concrete(w, h, seed + 99, base=(255, 255, 255), stains=False).convert("L")).astype(np.float64) / 255.0
    a = np.asarray(art).astype(np.float64)
    a[..., 3] *= np.clip(0.55 + 0.5 * grain, 0, 1) ** 0.6
    base.alpha_composite(Image.fromarray(a.astype(np.uint8), "RGBA"))
    out = base.convert("RGB").convert("RGBA")
    out.putalpha(0)  # murals never glow
    return out


# ------------------------------------------------------------------ cells

def _band():
    w, h = CELLS["band"][2], CELLS["band"][3]
    art = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(art)
    rng = random.Random(23)
    halftone(d, (0, 0, w, h), (*TEAL_DARK, 255), 22, 9, direction=(0, 1))
    speed_lines(d, (0, 40, w, h - 40), (*INK, 220), rng, n=10, width=(8, 26))
    f = font(DISPLAY, 300)
    tx, ty = w * 0.34, 236
    outlined(d, (tx + 20, ty + 22), "SLOPBALL", f, (*INK, 255), (*INK, 255), 30)
    outlined(d, (tx, ty), "SLOPBALL", f, (*YELLOW, 255), (*INK, 255), 18)
    for k in range(11):
        x = tx - 980 + k * 196 + rng.uniform(-36, 36)
        dw = rng.uniform(12, 22)
        L = rng.uniform(30, 100)
        d.rounded_rectangle((x - dw / 2, ty + 110, x + dw / 2, ty + 110 + L), radius=dw / 2, fill=(*YELLOW, 255))
    # the combo board hangs in front of the band's right half; a mascot and a burst sign it off
    m = mascot(330).rotate(-10, resample=Image.BICUBIC)
    art.alpha_composite(m, (int(w * 0.905) - 165, 70))
    burst(d, int(w * 0.965), 110, 80, (*CORAL, 255), "GO!", 46)
    for _ in range(4):
        splatter(d, rng.uniform(120, w - 120), rng.uniform(60, h - 120), rng.uniform(16, 30),
                 (*rng.choice([CORAL, CREAM, TEAL]), 255), rng, drips=2, drip_len=60)
    return _with_concrete(w, h, 5, art, fade=0.02)


def _mural():
    w, h = CELLS["mural"][2], CELLS["mural"][3]
    art = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(art)
    rng = random.Random(17)
    speed_lines(d, (0, 200, w, 860), (*INK, 235), rng, n=20, width=(8, 30))
    halftone(d, (0, 0, int(w * 0.45), h), (*TEAL, 255), 24, 10, direction=(-1, 0.4))
    halftone(d, (int(w * 0.55), 0, w, h), (*CORAL, 255), 24, 10, direction=(1, -0.2))
    for _ in range(9):
        splatter(d, rng.uniform(140, w - 140), rng.uniform(100, h - 200), rng.uniform(20, 52),
                 (*rng.choice([YELLOW, CORAL, TEAL, CREAM]), 255), rng, drips=rng.randint(1, 4), drip_len=90)
    m = mascot(900).rotate(6, resample=Image.BICUBIC)
    art.alpha_composite(m, (w // 2 - 450, 60))
    burst(d, 1680, 220, 140, (*CORAL, 255), "GO!", 80)
    burst(d, 370, 280, 112, (*YELLOW, 255), "WOW", 62)
    return _with_concrete(w, h, 11, art, fade=0.035)


def _side():
    w, h = CELLS["side"][2], CELLS["side"][3]
    art = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(art)
    rng = random.Random(11)
    speed_lines(d, (0, 80, w, 430), (*TEAL, 230), rng, n=12, width=(8, 24))
    for _ in range(5):
        splatter(d, rng.uniform(80, w - 80), rng.uniform(100, 330), rng.uniform(14, 36),
                 (*rng.choice([YELLOW, CORAL, CREAM]), 255), rng, drips=2, drip_len=70)
    f = font(DISPLAY, 138)
    outlined(d, (w * 0.38 + 12, 262), "KEEP IT MOVING", f, (*INK, 255), (*INK, 255), 18)
    outlined(d, (w * 0.38, 250), "KEEP IT MOVING", f, (*CORAL, 255), (*INK, 255), 11)
    m = mascot(380).rotate(-12, resample=Image.BICUBIC)
    art.alpha_composite(m, (w - 520, 60))
    return _with_concrete(w, h, 13, art, fade=0.03)


def _boards():
    rows = [
        ("SLOPBALL", INK, YELLOW, DISPLAY), ("HIT BALL - FEEL GOOD", TEAL_DARK, CREAM, DISPLAY),
        ("SLOP CO. SPORTS DRINK", CORAL, INK, DISPLAY), ("KEEP IT MOVING", INK, TEAL, DISPLAY),
        ("NO REFUNDS", YELLOW, INK, DISPLAY), ("BALL IS LIFE", (40, 26, 60), PINK, DISPLAY),
        ("MASCOT", INK, None, None), ("EST. 2026", TEAL, INK, MONO),
    ]
    w, h = CELLS["boards"][2], CELLS["boards"][3]
    rh = h // BOARD_ROWS
    im = Image.new("RGB", (w, h))
    d = ImageDraw.Draw(im)
    for i, (text, bg, fg, path) in enumerate(rows):
        y0 = i * rh
        d.rectangle((0, y0, w, y0 + rh), fill=bg)
        d.rectangle((0, y0, w, y0 + 7), fill=CORAL if i % 2 == 0 else TEAL)
        d.rectangle((0, y0 + rh - 9, w, y0 + rh), fill=(14, 14, 15))
        if text == "MASCOT":
            for k in range(5):
                m = mascot(104, wink=k % 2 == 0)
                im.paste(m, (40 + k * 200, y0 + 10), m)
            continue
        size = 74 if len(text) < 14 else 54
        d.text((w / 2, y0 + rh / 2 + 1), text, font=font(path, size), fill=fg, anchor="mm")
    rgba = im.convert("RGBA")
    rgba.putalpha(0)
    return rgba


def _neon():
    w, h = CELLS["neon"][2], CELLS["neon"][3]
    glow = Image.new("L", (w, h), 0)
    col = Image.new("RGB", (w, h), (22, 18, 30))
    dc = ImageDraw.Draw(col)
    dg = ImageDraw.Draw(glow)
    dc.rounded_rectangle((20, 20, w - 20, h - 20), radius=40, fill=(18, 14, 26), outline=(40, 34, 52), width=8)
    for text, y, size, color in (("SLOP CLUB", 200, 190, PINK), ("OPEN LATE", 380, 110, CYAN)):
        f = font(ROUND, size)
        dc.text((w / 2, y), text, font=f, fill=color, anchor="mm", stroke_width=3, stroke_fill=color)
        dg.text((w / 2, y), text, font=f, fill=255, anchor="mm", stroke_width=3, stroke_fill=255)
    out = col.convert("RGBA")
    out.putalpha(glow)
    return out


def _digits():
    w, h = CELLS["digits"][2], CELLS["digits"][3]
    im = Image.new("RGB", (w, h), (20, 22, 24))
    d = ImageDraw.Draw(im)
    f = font(DISPLAY, 150)
    for k in range(11):
        x0 = k * DIGIT_CELL
        blank = k == 10  # an empty hook: a dark slot rather than a white card
        d.rounded_rectangle((x0 + 6, 6, x0 + DIGIT_CELL - 6, h - 6), radius=12, fill=(30, 33, 37) if blank else CREAM)
        if not blank:
            d.text((x0 + DIGIT_CELL / 2, h / 2 + 4), str(k), font=f, fill=INK, anchor="mm")
            d.line((x0 + 6, h / 2, x0 + DIGIT_CELL - 6, h / 2), fill=(120, 116, 104), width=4)
    rgba = im.convert("RGBA")
    rgba.putalpha(0)
    return rgba


def _combo():
    w, h = CELLS["combo"][2], CELLS["combo"][3]
    im = Image.new("RGB", (w, h), CORAL)
    d = ImageDraw.Draw(im)
    d.text((w / 2, h / 2 + 2), "COMBO", font=font(DISPLAY, 84), fill=INK, anchor="mm")
    rgba = im.convert("RGBA")
    rgba.putalpha(0)
    return rgba


def _flood():
    s = CELLS["flood"][2]
    im = Image.new("RGB", (s, s), (30, 32, 36))
    glow = Image.new("L", (s, s), 0)
    d, g = ImageDraw.Draw(im), ImageDraw.Draw(glow)
    n = 6
    for i in range(n):
        for j in range(n):
            x0, y0 = 14 + i * (s - 28) / n, 14 + j * (s - 28) / n
            box = (x0 + 4, y0 + 4, x0 + (s - 28) / n - 4, y0 + (s - 28) / n - 4)
            d.rounded_rectangle(box, radius=6, fill=(250, 252, 255))
            g.rounded_rectangle(box, radius=6, fill=255)
    out = im.convert("RGBA")
    out.putalpha(glow)
    return out


def _speaker():
    s = CELLS["speaker"][2]
    im = Image.new("RGB", (s, s), (14, 14, 16))
    glow = Image.new("L", (s, s), 0)
    d, g = ImageDraw.Draw(im), ImageDraw.Draw(glow)
    c = s / 2
    d.ellipse((c - 118, c - 118, c + 118, c + 118), fill=(40, 40, 44))
    d.ellipse((c - 112, c - 112, c + 112, c + 112), outline=(60, 220, 255), width=6)
    g.ellipse((c - 112, c - 112, c + 112, c + 112), outline=255, width=6)
    for r, shade in ((100, 26), (78, 34), (50, 22), (22, 52)):
        d.ellipse((c - r, c - r, c + r, c + r), fill=(shade, shade, shade + 4))
    out = im.convert("RGBA")
    out.putalpha(glow)
    return out


def _vending():
    w, h = CELLS["vending"][2], CELLS["vending"][3]
    im = Image.new("RGB", (w, h), (150, 18, 30))
    glow = Image.new("L", (w, h), 0)
    d, g = ImageDraw.Draw(im), ImageDraw.Draw(glow)
    d.rectangle((16, 16, w - 16, 330), fill=(225, 235, 245))
    g.rectangle((16, 16, w - 16, 330), fill=200)
    rng = random.Random(4)
    for row in range(5):
        for k in range(5):
            x0, y0 = 28 + k * 42, 30 + row * 60
            d.rounded_rectangle((x0, y0, x0 + 30, y0 + 48), radius=6,
                                fill=rng.choice([CORAL, YELLOW, TEAL, LIME, PINK]))
    d.text((w / 2, 380), "SLOP CO.", font=font(DISPLAY, 34), fill=CREAM, anchor="mm")
    g.text((w / 2, 380), "SLOP CO.", font=font(DISPLAY, 34), fill=255, anchor="mm")
    d.rectangle((60, 430, w - 60, 480), fill=(30, 10, 14))
    out = im.convert("RGBA")
    out.putalpha(glow)
    return out


def _exit():
    w, h = CELLS["exit"][2], CELLS["exit"][3]
    im = Image.new("RGB", (w, h), (20, 70, 30))
    glow = Image.new("L", (w, h), 160)
    d, g = ImageDraw.Draw(im), ImageDraw.Draw(glow)
    d.text((w / 2, h / 2 + 2), "EXIT", font=font(DISPLAY, 70), fill=(220, 255, 220), anchor="mm")
    g.text((w / 2, h / 2 + 2), "EXIT", font=font(DISPLAY, 70), fill=255, anchor="mm")
    out = im.convert("RGBA")
    out.putalpha(glow)
    return out


def _door():
    s = CELLS["door"][2]
    base = np.zeros((s, s, 3), dtype=np.float64)
    ys = np.arange(s)[:, None]
    ribs = 0.78 + 0.22 * np.cos(ys / s * math.tau * 24) ** 2
    rust = noise.fbm(np.arange(s)[None, :] / 60.0, ys / 60.0, 41, octaves=4)
    for i, c in enumerate((112, 118, 124)):
        base[..., i] = c * ribs * (0.85 + 0.3 * (rust - 0.5))
    im = Image.fromarray(np.clip(base, 0, 255).astype(np.uint8), "RGB").convert("RGBA")
    d = ImageDraw.Draw(im)
    rng = random.Random(9)
    splatter(d, s * 0.32, s * 0.45, 40, (*PINK, 255), rng, drips=3, drip_len=90)
    outlined(d, (s * 0.55, s * 0.5), "SLOP!", font(DISPLAY, 150), (*YELLOW, 255), (*INK, 255), 10)
    im.putalpha(0)
    return im


def atlas(path):
    im = Image.new("RGBA", (SIZE, SIZE), (128, 128, 128, 0))
    _paste(im, "band", _band())
    _paste(im, "mural", _mural())
    _paste(im, "side", _side())
    _paste(im, "boards", _boards())
    _paste(im, "neon", _neon())
    _paste(im, "digits", _digits())
    _paste(im, "combo", _combo())
    _paste(im, "flood", _flood())
    _paste(im, "speaker", _speaker())
    _paste(im, "vending", _vending())
    _paste(im, "exit", _exit())
    for i, name in enumerate(("concrete_a", "concrete_b", "concrete_c")):
        c = concrete(1024, 1024, 60 + i).convert("RGBA")
        c.putalpha(0)
        _paste(im, name, c)
    _paste(im, "door", _door())
    x, y, w, h = CELLS["white"]
    im.paste((255, 255, 255, 255), (x, y, x + w, y + h))
    im.save(path)


# ------------------------------------------------------------------ court floor

def floor(path, half=20.0, size=2048):
    """The court on sealed concrete, planar over x, z in [-half, half] (north/sender up)."""
    px = size / (2 * half)

    def P(x, z):
        return ((x + half) * px, (half - z) * px)

    ys, xs = np.mgrid[0:size, 0:size].astype(np.float64)
    wx, wz = xs / px - half, half - ys / px
    teal = np.array((26, 96, 102), np.float64)
    terra = np.array((142, 58, 36), np.float64)
    border = np.array((22, 23, 27), np.float64)
    rgb = np.where((wz < 0)[..., None], teal, terra)
    outside = (np.abs(wx) > 18.7) | (np.abs(wz) > 18.7)
    rgb = np.where(outside[..., None], border, rgb)
    # sealed concrete under the paint: soft mottling, quieter inside the receiving area
    mott = noise.fbm(xs / 90.0, ys / 90.0, 3, octaves=4) - 0.5
    grain = noise.fbm(xs / 6.0, ys / 6.0, 8, octaves=2) - 0.5
    receive = np.exp(-((wx / 7.5) ** 2 + ((wz + 8.0) / 6.5) ** 2))
    amount = 1.0 - 0.6 * receive
    rgb *= (1.0 + (0.10 * mott + 0.035 * grain) * amount)[..., None]
    # scuffs on the border band only
    scuff = noise.fbm(xs / 25.0, ys / 25.0, 19, octaves=3)
    rgb = np.where((outside & (scuff > 0.62))[..., None], rgb * 1.25, rgb)
    im = Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8), "RGB")
    d = ImageDraw.Draw(im)
    line = (228, 223, 204)

    def seg(a, b, width):
        d.line([P(*a), P(*b)], fill=line, width=max(1, int(round(width * px))))

    r = 18.7
    for a, b in (((-r, -r), (r, -r)), ((r, -r), (r, r)), ((r, r), (-r, r)), ((-r, r), (-r, -r))):
        seg(a, b, 0.12)
    for a, b in (((-12, -16), (12, -16)), ((12, -16), (12, 16)), ((12, 16), (-12, 16)), ((-12, 16), (-12, -16))):
        seg(a, b, 0.09)
    seg((-12, -7), (12, -7), 0.09)
    seg((-12, 7), (12, 7), 0.09)
    seg((0, -16), (0, -7), 0.07)
    seg((0, 7), (0, 12 - 3.6), 0.07)
    seg((0, 12 + 3.6), (0, 16), 0.07)
    # sender-half logo: a painted mascot roundel, toned down for paint on concrete
    cx, cz, R = 0.0, 12.0, 3.6
    x0, y0 = P(cx - R, cz + R)
    x1, y1 = P(cx + R, cz - R)
    d.ellipse((x0, y0, x1, y1), outline=line, width=int(0.12 * px))
    m = mascot(int(2 * R * 0.62 * px))
    faded = Image.new("RGBA", m.size, (0, 0, 0, 0))
    faded = Image.blend(faded, m, 0.82)
    im.paste(faded, (int(P(cx, cz)[0] - m.size[0] / 2), int(P(cx, cz + 0.5)[1] - m.size[1] / 2)), faded)
    d.text(P(cx, cz - 2.45), "SLOPBALL", font=font(DISPLAY, int(0.62 * px)), fill=line, anchor="mm")
    im = im.filter(ImageFilter.GaussianBlur(0.5))
    im.save(path)


def detail(path, size=512):
    ys, xs = np.mgrid[0:size, 0:size].astype(np.float64)
    g = noise.fbm(xs / 16.0, ys / 16.0, 5, octaves=4, period=size // 16)
    v = np.clip(128 + 70 * (g - 0.5), 0, 255).astype(np.uint8)
    Image.fromarray(v, "L").save(path)


if __name__ == "__main__":
    out = Path(sys.argv[1] if len(sys.argv) > 1 else "/tmp/slopball-textures")
    out.mkdir(parents=True, exist_ok=True)
    atlas(out / "SlopballNightAtlas.png")
    floor(out / "SlopballNightFloor.png")
    detail(out / "SlopballNightDetail.png")
    print("textures", out)
