"""Neo Mall textures (numpy + Pillow). Everything is drawn here; lettering uses the project's
Archivo Black, Baloo 2 and Space Mono (OFL, client/Unity/Assets/Fonts).

ground  (4096 x 4096, RGB): every walkable top, planar over GROUND (north up): cream terrazzo
        galleries with brass strips, the food court's diner tiles, the skybridges' steel plate,
        the fashion podium's runway, planter soil, the elevator tower's tread plate and the
        escalator's grooved steps, plus the route paint: START, arrows, the RED PILL / BLUE PILL
        fork hints, hazard edges, OUT OF ORDER stripes and FINISH.
atlas   (4096 x 4096, RGBA; alpha = StageNeon glow): shop signs, the department store, the
        cinema marquee, posters and banners, the pharmacy's pills, menus, shutters, windows,
        the night outside the entrance, the white block geo.WHITE_UV samples (alpha 0) and the
        lamp block glowing faces sample (LAMP_UV, alpha 255).
detail  (512 x 512, gray): tileable grain for StageVertexLit's triplanar detail.
"""
from __future__ import annotations

import math
import random
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

from stagekit.noise import fbm

import palette as P

REPO = Path(__file__).resolve().parents[3]
FONTS = REPO / "client/Unity/Assets/Fonts"
DISPLAY = str(FONTS / "ArchivoBlack-Regular.ttf")
ROUND = str(FONTS / "Baloo2-Variable.ttf")
MONO = str(FONTS / "SpaceMono-Regular.ttf")

# The planar ground: world rectangle (x0, z0, x1, z1), square, and its size in pixels.
GROUND = (-52.0, -54.0, 48.0, 46.0)
GROUND_SIZE = 4096
SIZE = 4096


def rgb(c, a=255):
    return tuple(int(round(min(1.0, max(0.0, v)) * 255)) for v in c[:3]) + (int(a),)


def font(path, size):
    return ImageFont.truetype(path, max(6, int(size)))


def fit_font(path, text, max_w, max_h, start=420):
    size = start
    while size > 8:
        f = font(path, size)
        x0, y0, x1, y1 = f.getbbox(text)
        if x1 - x0 <= max_w and y1 - y0 <= max_h:
            return f
        size = int(size * 0.93)
    return font(path, 8)


# ------------------------------------------------------------------ atlas layout

# (name, w, h) packed into shelves; the top-right 128 x 128 block stays white for geo.WHITE_UV.
SHOPS = [
    # name, text, colour, kind (neon letters on a dark box, or a lit box with dark letters)
    ("blades", "BLADES OF THE EAST", P.RED, "neon"),
    ("case_closed", "CASE CLOSED", P.CYAN, "neon"),
    ("shade", "SHADE STATION", P.YELLOW, "neon"),
    ("otaku", "OTAKU PARADISE", P.PINK, "neon"),
    ("edgelord", "EDGELORD", P.PURPLE, "neon"),
    ("sole", "SOLE MATES", P.ORANGE, "box"),
    ("candle", "CANDLE IN THE WIND", P.YELLOW, "box"),
    ("books", "BOOKS & CRANNIES", P.CREAM, "box"),
    ("tea", "TEA-RRIFIC", P.PINK, "box"),
    ("spooky", "SPOOKY SEASON", P.ORANGE, "neon"),
    ("karate", "KARATE KIDZ", P.RED, "box"),
    ("pharmacy", "PHARMACY", P.EXIT_GREEN, "neon"),
    ("pretzel", "PRETZEL LOGIC", P.YELLOW, "box"),
    ("wok", "WOK THIS WAY", P.RED, "neon"),
    ("hotdog", "HOT DOG HEAVEN", P.ORANGE, "neon"),
    ("slop_cola", "SLOP COLA", P.RED, "box"),
    ("pixel", "PIXEL PALACE", P.CYAN, "neon"),
    ("massage", "MASSAGE CHAIRS: TRY ME", P.BLUE, "box"),
    ("lucky", "LUCKY CAT GIFTS", P.YELLOW, "neon"),
    ("vhs", "REWIND VIDEO", P.BLUE, "neon"),
]
_CELL_SIZES = (
    [(name, 1024, 160) for name, *_ in SHOPS]
    + [
        ("dice_dojo", 1024, 384), ("grand_bazaar", 1536, 288), ("neo_mall", 2048, 384), ("marquee", 2048, 384),
        ("katana_banner", 2048, 192), ("fnd", 1024, 256), ("closed_cleaning", 1024, 256), ("out_of_order", 1024, 256),
        ("no_swords", 1024, 256), ("out_of_service", 1024, 256), ("exit", 512, 192),
        ("banner_sale", 256, 1024), ("banner_open", 256, 1024), ("banner_ninja", 256, 1024), ("banner_pills", 256, 1024),
        ("red_pill", 512, 512), ("blue_pill", 512, 512),
        ("menu_wok", 768, 384), ("menu_pretzel", 768, 384), ("menu_hotdog", 768, 384),
        ("shutter", 512, 512), ("window", 512, 512), ("display", 512, 512), ("dept_window", 768, 384),
        ("game_window", 768, 384), ("claw", 512, 512), ("night", 1024, 512), ("speaker", 256, 256),
        ("wet_floor", 256, 384), ("you_are_here", 512, 768), ("fashion", 1024, 256), ("led", 256, 64),
        ("doug", 512, 1024), ("lamp", 256, 256),
    ]
)


def _pack():
    cells = {"white": (SIZE - 128, 0, 128, 128)}
    x = y = shelf = 0
    for name, w, h in sorted(_CELL_SIZES, key=lambda c: (-c[2], -c[1], c[0])):
        if x + w > SIZE - (128 if y < 128 else 0):
            x, y, shelf = 0, y + shelf, 0
        if x + w > SIZE - (128 if y < 128 else 0):
            raise ValueError("atlas cell %s does not fit" % name)
        cells[name] = (x, y, w, h)
        x += w
        shelf = max(shelf, h)
    if y + shelf > SIZE:
        raise ValueError("atlas overflow")
    return cells


CELLS = _pack()


def uv(name, inset=2):
    """((u0, v0), (u1, v1)) of an atlas cell, inset a few pixels against bleeding."""
    x, y, w, h = CELLS[name]
    return ((x + inset) / SIZE, 1 - (y + h - inset) / SIZE), ((x + w - inset) / SIZE, 1 - (y + inset) / SIZE)


def sub_uv(name, fu0, fv0, fu1, fv1, inset=2):
    """A fractional window of a cell (fractions measured from the cell's bottom-left)."""
    (a0, b0), (a1, b1) = uv(name, inset)
    return (a0 + (a1 - a0) * fu0, b0 + (b1 - b0) * fv0), (a0 + (a1 - a0) * fu1, b0 + (b1 - b0) * fv1)


LAMP_UV = ((CELLS["lamp"][0] + CELLS["lamp"][2] / 2) / SIZE, 1 - (CELLS["lamp"][1] + CELLS["lamp"][3] / 2) / SIZE)
SALE_UV = sub_uv("banner_sale", 0.08, 0.76, 0.92, 0.85)
SHOP_NAMES = [name for name, *_ in SHOPS]


# ------------------------------------------------------------------ cell painters

def _cell(w, h, color, alpha=0):
    im = Image.new("RGBA", (w, h), rgb(color, alpha))
    return im, ImageDraw.Draw(im)


def _glow_text(im, text, box, color, path=DISPLAY, glow=255, halo=True, outline=None):
    """Lettering filling `box`; its alpha is the StageNeon glow (a soft halo glows faintly)."""
    x0, y0, x1, y1 = box
    f = fit_font(path, text, x1 - x0, y1 - y0)
    layer = Image.new("RGBA", im.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    kw = {"stroke_width": max(2, f.size // 18), "stroke_fill": rgb(outline, glow)} if outline else {}
    d.text(((x0 + x1) / 2, (y0 + y1) / 2), text, font=f, fill=rgb(color, glow), anchor="mm", **kw)
    if halo:
        blur = layer.filter(ImageFilter.GaussianBlur(max(4, f.size // 6)))
        a = np.asarray(blur).astype(np.float32)
        a[..., 3] *= 0.55
        im.alpha_composite(Image.fromarray(a.astype(np.uint8), "RGBA"))
    im.alpha_composite(layer)


def _shop(text, color, kind):
    w, h = 1024, 192
    if kind == "neon":
        # Neon letters on a dark box: the letters glow, the box does not.
        im, d = _cell(w, h, (0.06, 0.06, 0.08), 0)
        d.rectangle((0, 0, w - 1, h - 1), outline=rgb((0.25, 0.25, 0.28), 0), width=10)
        _glow_text(im, text, (40, 26, w - 40, h - 26), color, glow=255)
    else:
        # A lit box with dark letters: the whole face glows softly.
        im, d = _cell(w, h, color, 150)
        d.rectangle((0, 0, w - 1, h - 1), outline=rgb(P.INK, 160), width=12)
        _glow_text(im, text, (44, 30, w - 44, h - 30), P.INK, glow=150, halo=False)
    return im


def _logo(text, sub, color, w, h, bg=(0.05, 0.05, 0.07)):
    im, d = _cell(w, h, bg, 0)
    _glow_text(im, text, (60, 24, w - 60, int(h * 0.72)), color, glow=255)
    if sub:
        _glow_text(im, sub, (200, int(h * 0.72), w - 200, h - 18), P.CREAM, path=MONO, glow=200, halo=False)
    return im


def _banner(lines, bg, fg, w, h, accent=None, path=DISPLAY):
    """A printed banner: no glow."""
    im, d = _cell(w, h, bg, 0)
    if accent:
        d.rectangle((14, 14, w - 15, h - 15), outline=rgb(accent, 0), width=12)
    n = len(lines)
    top, bottom = 40, h - 40
    step = (bottom - top) / n
    for i, line in enumerate(lines):
        f = fit_font(path, line, w - 80, step * 0.82)
        d.text((w / 2, top + step * (i + 0.5)), line, font=f, fill=rgb(fg, 0), anchor="mm")
    return im


def _vertical(lines, bg, fg, accent):
    w, h = 384, 1536
    im, d = _cell(w, h, bg, 0)
    d.rectangle((18, 18, w - 19, h - 19), outline=rgb(accent, 0), width=10)
    y = 90
    for line, size in lines:
        f = font(DISPLAY, size)
        bbox = f.getbbox(line)
        if bbox[2] - bbox[0] > w - 60:
            f = fit_font(DISPLAY, line, w - 60, size)
        d.text((w / 2, y), line, font=f, fill=rgb(fg, 0), anchor="mt")
        y += f.size * 1.25
    return im, d


def _pill(color, text):
    w = h = 512
    im, d = _cell(w, h, (0.06, 0.06, 0.08), 0)
    # A giant capsule, glowing, tilted like a pharmacy sign.
    layer = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    ld = ImageDraw.Draw(layer)
    ld.rounded_rectangle((70, 150, 442, 330), radius=90, fill=rgb(color, 255), outline=rgb(P.WHITE, 255), width=10)
    ld.rectangle((256, 158, 262, 322), fill=rgb(P.WHITE, 255))
    ld.ellipse((110, 180, 200, 220), fill=rgb(P.WHITE, 200))
    layer = layer.rotate(18, resample=Image.BICUBIC)
    im.alpha_composite(layer)
    _glow_text(im, text, (40, 380, w - 40, 480), color, glow=255, outline=P.WHITE)
    return im


def _shutter():
    w = h = 512
    a = np.zeros((h, w, 3), np.float32)
    ys = np.arange(h)[:, None].astype(np.float32)
    slat = 0.82 + 0.18 * np.cos(ys / 16.0 * 2 * math.pi)
    a[:] = np.array(P.SHUTTER, np.float32) * slat[..., None]
    im = Image.fromarray(np.clip(a * 255, 0, 255).astype(np.uint8), "RGB").convert("RGBA")
    d = ImageDraw.Draw(im)
    d.rectangle((0, h - 40, w, h), fill=rgb(P.STEEL_DARK, 0))
    d.rectangle((w // 2 - 50, h - 70, w // 2 + 50, h - 54), fill=rgb(P.STEEL_DARK, 0))
    a = np.asarray(im).copy()
    a[..., 3] = 0
    return Image.fromarray(a, "RGBA")


def _window(lit=True):
    """A shop window at night: dark glass, a lit display inside (alpha = glow)."""
    w = h = 512
    im, d = _cell(w, h, P.GLASS_DARK, 0)
    if lit:
        d.rectangle((24, 60, w - 24, h - 40), fill=rgb((0.85, 0.72, 0.52), 120))
        # shelves and goods
        rng = random.Random(7)
        for row in range(3):
            y = 150 + row * 110
            d.rectangle((40, y, w - 40, y + 10), fill=rgb(P.WOOD, 120))
            for k in range(8):
                x = 60 + k * 52 + rng.randint(-6, 6)
                c = rng.choice([P.PINK, P.CYAN, P.YELLOW, P.RED, P.BLUE, P.CREAM])
                d.rectangle((x, y - rng.randint(40, 80), x + 34, y), fill=rgb(c, 140))
    # glass glint
    d.polygon([(w * 0.55, 0), (w * 0.72, 0), (w * 0.35, h), (w * 0.18, h)], fill=rgb((0.8, 0.9, 1.0), 40))
    d.rectangle((0, 0, w - 1, h - 1), outline=rgb(P.STEEL_DARK, 0), width=14)
    return im


def _mannequin(d, x, y, s, color, pose=0):
    """A mannequin silhouette standing at (x, y) feet, `s` pixels tall."""
    head = s * 0.13
    d.ellipse((x - head / 2, y - s, x + head / 2, y - s + head), fill=color)
    d.polygon([(x - s * 0.13, y - s * 0.84), (x + s * 0.13, y - s * 0.84), (x + s * 0.09, y - s * 0.45), (x - s * 0.09, y - s * 0.45)], fill=color)
    d.polygon([(x - s * 0.09, y - s * 0.45), (x + s * 0.09, y - s * 0.45), (x + s * 0.06 + pose * 4, y), (x - s * 0.08, y)], fill=color)
    d.line((x - s * 0.13, y - s * 0.82, x - s * 0.2, y - s * 0.5 - pose * 10), fill=color, width=max(3, int(s * 0.05)))
    d.line((x + s * 0.13, y - s * 0.82, x + s * 0.22, y - s * 0.55 + pose * 8), fill=color, width=max(3, int(s * 0.05)))


def _display():
    """A boutique window: three posed mannequins in suits and shades under a spotlight."""
    w = h = 512
    im, d = _cell(w, h, P.GLASS_DARK, 0)
    d.rectangle((20, 40, w - 20, h - 30), fill=rgb((0.30, 0.18, 0.30), 90))
    d.ellipse((60, 380, 452, 470), fill=rgb((0.95, 0.85, 0.70), 150))
    for k, x in enumerate((140, 256, 372)):
        _mannequin(d, x, 440, 300, rgb((0.95, 0.93, 0.90), 170), pose=k - 1)
        d.rectangle((x - 30, 440 - 300 * 0.84, x + 30, 440 - 300 * 0.48), fill=rgb(P.INK, 120))   # suit
        d.rectangle((x - 18, 440 - 300 * 0.95, x + 18, 440 - 300 * 0.92), fill=rgb(P.INK, 160))   # shades
    d.polygon([(w * 0.6, 0), (w * 0.75, 0), (w * 0.4, h), (w * 0.25, h)], fill=rgb((0.8, 0.9, 1.0), 30))
    d.rectangle((0, 0, w - 1, h - 1), outline=rgb(P.STEEL_DARK, 0), width=14)
    return im


def _dept_window():
    w, h = 1024, 512
    im, d = _cell(w, h, P.GLASS_DARK, 0)
    d.rectangle((16, 30, w - 16, h - 24), fill=rgb((0.55, 0.42, 0.30), 100))
    for k, x in enumerate((150, 330, 520, 700, 880)):
        _mannequin(d, x, 450, 340, rgb((0.96, 0.95, 0.92), 170), pose=(k % 3) - 1)
        if k in (1, 3):
            d.rectangle((x - 34, 450 - 340 * 0.84, x + 34, 450 - 340 * 0.48), fill=rgb(P.INK, 120))
            d.rectangle((x - 20, 450 - 340 * 0.95, x + 20, 450 - 340 * 0.92), fill=rgb(P.INK, 160))
    f = font(DISPLAY, 46)
    d.text((w / 2, 60), "THE AGENT COLLECTION", font=f, fill=rgb(P.CREAM, 160), anchor="mm")
    d.polygon([(w * 0.62, 0), (w * 0.7, 0), (w * 0.5, h), (w * 0.42, h)], fill=rgb((0.8, 0.9, 1.0), 30))
    return im


def _game_window():
    """DICE DOJO's window: dice, card boxes, a painted dragon and FRIDAY NIGHT DUNGEONS."""
    w, h = 1024, 512
    im, d = _cell(w, h, P.GLASS_DARK, 0)
    d.rectangle((16, 30, w - 16, h - 24), fill=rgb((0.22, 0.16, 0.36), 110))
    rng = random.Random(11)
    for k in range(14):
        x, y, s = rng.randint(60, w - 120), rng.randint(300, 440), rng.randint(36, 60)
        c = rng.choice([P.RED, P.BLUE, P.PURPLE, P.YELLOW, P.CREAM])
        pts = [(x + s * math.cos(a), y + s * math.sin(a)) for a in [math.pi / 2 + i * math.pi / 3 for i in range(6)]]
        d.polygon(pts, fill=rgb(c, 150), outline=rgb(P.INK, 150))
    d.polygon([(620, 300), (700, 160), (760, 240), (840, 120), (880, 300)], fill=rgb(P.RED, 140))   # the dragon mini
    f = font(DISPLAY, 54)
    d.text((300, 90), "FRIDAY NIGHT", font=f, fill=rgb(P.YELLOW, 200), anchor="mm")
    d.text((300, 160), "DUNGEONS 9PM", font=f, fill=rgb(P.YELLOW, 200), anchor="mm")
    f2 = font(MONO, 30)
    d.text((300, 220), "ninjas: ask the DM first", font=f2, fill=rgb(P.CREAM, 150), anchor="mm")
    return im


def _claw():
    w = h = 512
    im, d = _cell(w, h, (0.10, 0.06, 0.18), 0)
    d.rectangle((40, 40, w - 40, h - 160), fill=rgb((0.55, 0.85, 1.0), 90))
    rng = random.Random(3)
    for k in range(18):
        x, y = rng.randint(70, w - 70), rng.randint(220, h - 180)
        d.ellipse((x - 26, y - 26, x + 26, y + 26), fill=rgb(rng.choice([P.PINK, P.YELLOW, P.HACKER, P.CYAN]), 150))
    d.line((w / 2, 40, w / 2, 160), fill=rgb(P.STEEL, 200), width=6)
    d.polygon([(w / 2 - 30, 160), (w / 2 + 30, 160), (w / 2 + 40, 200), (w / 2 - 40, 200)], fill=rgb(P.STEEL, 200))
    _glow_text(im, "WIN A NINJA!", (60, h - 140, w - 60, h - 50), P.PINK, glow=255)
    return im


def _night():
    """Outside the entrance: the parking lot at night, lamp posts and the city beyond."""
    w, h = 1024, 512
    a = np.zeros((h, w, 3), np.float32)
    ys = np.arange(h)[:, None].astype(np.float32) / h
    sky = np.array(P.NIGHT, np.float32) * (1 - ys) + np.array(P.NIGHT_LOW, np.float32) * ys
    a[:] = sky[:, None, :] if sky.ndim == 2 else sky
    im = Image.fromarray(np.clip(a * 255, 0, 255).astype(np.uint8), "RGB").convert("RGBA")
    d = ImageDraw.Draw(im)
    rng = random.Random(5)
    # skyline
    x = 0
    while x < w:
        bw, bh = rng.randint(40, 110), rng.randint(60, 220)
        d.rectangle((x, 300 - bh, x + bw, 300), fill=rgb((0.08, 0.09, 0.15), 0))
        for _ in range(bw * bh // 900):
            wx, wy = rng.randint(x + 4, x + bw - 8), rng.randint(300 - bh + 6, 296)
            d.rectangle((wx, wy, wx + 5, wy + 7), fill=rgb(P.LAMP, 200))
        x += bw + rng.randint(2, 12)
    d.rectangle((0, 300, w, h), fill=rgb((0.12, 0.12, 0.14), 0))
    for k in range(9):
        lx = 50 + k * 115
        d.line((lx, 300, lx, 220), fill=rgb((0.3, 0.3, 0.32), 0), width=5)
        d.ellipse((lx - 16, 210, lx + 16, 230), fill=rgb(P.LAMP, 255))
        d.ellipse((lx - 60, 300, lx + 60, 330), fill=rgb((0.5, 0.42, 0.28), 60))
    for k in range(7):
        cx = rng.randint(20, w - 80)
        d.rounded_rectangle((cx, 380, cx + 70, 410), radius=8, fill=rgb(rng.choice([P.RED, P.BLUE, P.CREAM, P.STEEL]), 0))
    return im


def _you_are_here():
    w, h = 512, 768
    im, d = _cell(w, h, (0.10, 0.12, 0.18), 120)
    d.rectangle((0, 0, w - 1, h - 1), outline=rgb(P.STEEL, 160), width=16)
    _glow_text(im, "NEO MALL", (40, 30, w - 40, 120), P.CYAN, glow=230)
    d.rectangle((50, 140, w - 50, h - 150), fill=rgb((0.86, 0.84, 0.78), 160))
    rng = random.Random(2)
    for k in range(14):
        x, y = rng.randint(70, w - 160), rng.randint(160, h - 230)
        d.rectangle((x, y, x + rng.randint(50, 110), y + rng.randint(30, 70)), fill=rgb(rng.choice([P.PINK, P.CYAN, P.YELLOW, P.ORANGE]), 160))
    d.ellipse((w / 2 - 34, h / 2 - 34, w / 2 + 34, h / 2 + 34), fill=rgb(P.RED, 255))
    f = font(DISPLAY, 58)
    d.text((w / 2, h - 90), "YOU ARE HERE", font=f, fill=rgb(P.RED, 255), anchor="mm")
    return im


def _wet_floor():
    w, h = 256, 384
    im, d = _cell(w, h, P.HAZARD_YELLOW, 0)
    d.polygon([(128, 40), (200, 170), (56, 170)], fill=rgb(P.INK, 0))
    d.polygon([(128, 70), (178, 158), (78, 158)], fill=rgb(P.HAZARD_YELLOW, 0))
    d.text((128, 140), "!", font=font(DISPLAY, 60), fill=rgb(P.INK, 0), anchor="mm")
    f = font(DISPLAY, 52)
    d.text((128, 240), "WET", font=f, fill=rgb(P.INK, 0), anchor="mm")
    d.text((128, 300), "FLOOR", font=f, fill=rgb(P.INK, 0), anchor="mm")
    return im


def _speaker():
    w = h = 256
    im, d = _cell(w, h, (0.10, 0.10, 0.11), 0)
    for r in (100, 70, 40):
        d.ellipse((128 - r, 128 - r, 128 + r, 128 + r), outline=rgb((0.25, 0.25, 0.27), 0), width=8)
    for y in range(20, 240, 14):
        for x in range(20, 240, 14):
            if (x - 128) ** 2 + (y - 128) ** 2 < 110 ** 2:
                d.ellipse((x - 3, y - 3, x + 3, y + 3), fill=rgb((0.04, 0.04, 0.05), 0))
    return im


def _menu(title, items, color):
    w, h = 1024, 512
    im, d = _cell(w, h, (0.08, 0.08, 0.10), 110)
    _glow_text(im, title, (40, 20, w - 40, 130), color, glow=255)
    f = font(MONO, 40)
    for k, (item, price) in enumerate(items):
        y = 180 + k * 70
        d.text((70, y), item, font=f, fill=rgb(P.CREAM, 220), anchor="lm")
        d.text((w - 70, y), price, font=f, fill=rgb(P.YELLOW, 220), anchor="rm")
    return im


def _marquee():
    w, h = 2048, 512
    im, d = _cell(w, h, (0.96, 0.94, 0.88), 160)
    for x in range(30, w, 60):
        for y in (22, h - 22):
            d.ellipse((x - 12, y - 12, x + 12, y + 12), fill=rgb(P.LAMP, 255))
    _glow_text(im, "NOW SHOWING", (200, 50, w - 200, 150), P.RED, glow=200, halo=False)
    f = font(DISPLAY, 86)
    for k, line in enumerate(("THE CHOSEN ONE 7: REBOOTED AGAIN", "NINJA SUMMER  ·  SPOON 2: NO SPOON")):
        d.text((w / 2, 230 + k * 120), line, font=fit_font(DISPLAY, line, w - 160, 110), fill=rgb(P.INK, 170), anchor="mm")
    return im


def _katana_banner():
    w, h = 2048, 256
    im, d = _cell(w, h, P.RED, 0)
    d.rectangle((14, 14, w - 15, h - 15), outline=rgb(P.KATANA_GUARD, 0), width=10)
    f = fit_font(DISPLAY, "BLADES OF THE EAST  ·  ALL SALES FINAL", w - 120, h - 80)
    d.text((w / 2, h / 2), "BLADES OF THE EAST  ·  ALL SALES FINAL", font=f, fill=rgb(P.CREAM, 0), anchor="mm")
    return im


def _doug():
    """OFFICER DOUG's cardboard cutout: cap, moustache, thumbs up, SEE SOMETHING? SAY SOMETHING!"""
    w, h = 512, 1024
    im, d = _cell(w, h, (0.72, 0.56, 0.38), 0)                       # cardboard
    navy, skin, gold = (0.12, 0.16, 0.32), (0.96, 0.76, 0.60), (0.92, 0.74, 0.22)
    # speech bubble
    d.rounded_rectangle((30, 20, 482, 170), radius=40, fill=rgb(P.WHITE, 0), outline=rgb(P.INK, 0), width=6)
    d.polygon([(330, 165), (380, 165), (300, 230)], fill=rgb(P.WHITE, 0), outline=rgb(P.INK, 0))
    f = font(DISPLAY, 46)
    d.text((256, 70), "SEE SOMETHING?", font=f, fill=rgb(P.INK, 0), anchor="mm")
    d.text((256, 125), "SAY SOMETHING!", font=f, fill=rgb(P.RED, 0), anchor="mm")
    # body: uniform, belt, legs
    d.rounded_rectangle((120, 470, 392, 820), radius=40, fill=rgb(navy, 0))
    d.rectangle((120, 760, 392, 790), fill=rgb(P.INK, 0))
    d.rectangle((240, 762, 272, 788), fill=rgb(gold, 0))
    d.rectangle((150, 820, 240, 1000), fill=rgb(navy, 0))
    d.rectangle((272, 820, 362, 1000), fill=rgb(navy, 0))
    d.rounded_rectangle((136, 980, 246, 1016), radius=12, fill=rgb(P.INK, 0))
    d.rounded_rectangle((266, 980, 376, 1016), radius=12, fill=rgb(P.INK, 0))
    # badge and name tag
    d.polygon([(170, 540), (200, 525), (230, 540), (225, 585), (200, 600), (175, 585)], fill=rgb(gold, 0), outline=rgb(P.INK, 0))
    d.rectangle((280, 540, 370, 575), fill=rgb(P.WHITE, 0), outline=rgb(P.INK, 0))
    d.text((325, 558), "DOUG", font=font(DISPLAY, 24), fill=rgb(P.INK, 0), anchor="mm")
    # arm with thumbs up
    d.rounded_rectangle((360, 480, 430, 640), radius=30, fill=rgb(navy, 0))
    d.ellipse((370, 420, 450, 500), fill=rgb(skin, 0))
    d.rounded_rectangle((398, 370, 426, 440), radius=12, fill=rgb(skin, 0))
    d.rounded_rectangle((82, 480, 152, 700), radius=30, fill=rgb(navy, 0))
    d.ellipse((86, 680, 150, 740), fill=rgb(skin, 0))
    # head, cap, moustache
    d.ellipse((166, 270, 346, 470), fill=rgb(skin, 0))
    d.chord((150, 230, 362, 380), 180, 360, fill=rgb(navy, 0))
    d.rectangle((150, 300, 380, 322), fill=rgb(P.INK, 0))
    d.polygon([(236, 250), (256, 236), (276, 250), (272, 280), (240, 280)], fill=rgb(gold, 0))
    d.ellipse((206, 345, 236, 375), fill=rgb(P.INK, 0))
    d.ellipse((276, 345, 306, 375), fill=rgb(P.INK, 0))
    d.chord((196, 395, 316, 450), 180, 360, fill=rgb((0.30, 0.20, 0.12), 0))
    d.arc((226, 420, 286, 455), 20, 160, fill=rgb(P.INK, 0), width=5)
    return im


def _led():
    w, h = 256, 64
    im, d = _cell(w, h, P.LED, 255)
    return im


def atlas(path):
    im = Image.new("RGBA", (SIZE, SIZE), (255, 255, 255, 0))

    def put(name, cell):
        x, y, w, h = CELLS[name]
        if cell.size != (w, h):
            # Resize colour and glow apart: Pillow premultiplies RGBA, which would blacken
            # every unlit (alpha 0) pixel.
            colour = cell.convert("RGB").resize((w, h), Image.LANCZOS)
            glow = cell.getchannel("A").resize((w, h), Image.LANCZOS)
            cell = Image.merge("RGBA", (*colour.split(), glow))
        im.paste(cell, (x, y))

    for name, text, color, kind in SHOPS:
        put(name, _shop(text, color, kind))
    put("dice_dojo", _logo("DICE DOJO", "GAMES · CARDS · MINIATURES", P.PURPLE, 1024, 384))
    put("grand_bazaar", _logo("GRAND BAZAAR", "DEPARTMENT STORE · EST. 1987", P.CREAM, 2048, 384, bg=(0.10, 0.06, 0.08)))
    put("neo_mall", _logo("NEO MALL", "OPEN 10 – 9 · ALL WELCOME", P.CYAN, 2048, 512))
    put("marquee", _marquee())
    put("katana_banner", _katana_banner())
    put("fnd", _banner(["FRIDAY NIGHT DUNGEONS · 9PM", "DON'T BE LATE, WIBOU"], P.PURPLE, P.CREAM, 1024, 256, P.YELLOW))
    put("closed_cleaning", _banner(["CLOSED FOR CLEANING", "SORRY FOR THE INCONVENIENCE"], P.HAZARD_YELLOW, P.INK, 1024, 256, P.INK))
    put("out_of_order", _banner(["OUT OF ORDER", "PLEASE USE THE STAIRS"], P.RED, P.CREAM, 1024, 256, P.CREAM))
    put("no_swords", _banner(["NO SWORDS IN THE FOOD COURT", "(THIS MEANS YOU)"], P.CREAM, P.RED, 1024, 256, P.RED))
    put("out_of_service", _banner(["OUT OF SERVICE", "ELEVATOR STUCK SINCE 9:00"], P.CREAM, P.INK, 1024, 256, P.STEEL_DARK))
    ex = _logo("EXIT", None, P.EXIT_GREEN, 512, 192)
    put("exit", ex)
    im_v, d_v = _vertical([("FALL", 120), ("SALE", 120), ("EVERY", 64), ("THING", 64), ("MUST", 64), ("GO", 120)], P.RED, P.CREAM, P.YELLOW)
    put("banner_sale", im_v)
    im_v, d_v = _vertical([("NEO", 120), ("MALL", 110), ("OPEN", 70), ("10-9", 90), ("DAILY", 60)], P.BLUE, P.CREAM, P.CYAN)
    put("banner_open", im_v)
    im_v, d_v = _vertical([("NINJA", 96), ("SUMMER", 70), ("IN", 60), ("CINEMAS", 54), ("NOW", 70)], (0.06, 0.06, 0.08), P.HACKER, P.HACKER)
    d_v.rectangle((110, 900, 274, 1400), fill=rgb((0.16, 0.36, 0.12), 0))
    d_v.ellipse((150, 830, 234, 914), fill=rgb((0.16, 0.36, 0.12), 0))
    d_v.line((80, 1100, 330, 860), fill=rgb(P.KATANA_STEEL, 0), width=10)
    put("banner_ninja", im_v)
    im_v, d_v = _vertical([("RED", 110), ("OR", 80), ("BLUE?", 100), ("ASK", 64), ("YOUR", 64), ("PHARM-", 54), ("ACIST", 54)], P.CREAM, P.INK, P.RED)
    put("banner_pills", im_v)
    put("red_pill", _pill(P.RED, "RED PILL"))
    put("blue_pill", _pill(P.BLUE, "BLUE PILL"))
    put("menu_wok", _menu("WOK THIS WAY", [("ORANGE CHICKEN", "6.99"), ("LO MEIN", "5.49"), ("FORTUNE COOKIE", "FREE")], P.RED))
    put("menu_pretzel", _menu("PRETZEL LOGIC", [("SALTED", "3.49"), ("CINNAMON", "3.99"), ("PRETZEL DOG", "4.99")], P.YELLOW))
    put("menu_hotdog", _menu("HOT DOG HEAVEN", [("THE FRANK", "3.99"), ("CHILI DOG", "4.49"), ("SLOP COLA", "1.99")], P.ORANGE))
    put("shutter", _shutter())
    put("window", _window())
    put("display", _display())
    put("dept_window", _dept_window())
    put("game_window", _game_window())
    put("claw", _claw())
    put("night", _night())
    put("speaker", _speaker())
    put("wet_floor", _wet_floor())
    put("you_are_here", _you_are_here())
    put("fashion", _banner(["FALL FASHION WEEK", "TONIGHT ON THE PODIUM"], P.CARPET, P.CREAM, 1024, 256, P.CREAM))
    put("led", _led())
    put("doug", _doug())
    put("lamp", Image.new("RGBA", (256, 256), (255, 255, 255, 255)))
    put("white", Image.new("RGBA", (128, 128), (255, 255, 255, 0)))
    im.save(path)


# ------------------------------------------------------------------ tiling maps

def detail(path, size=512, seed=23):
    ys, xs = np.mgrid[0:size, 0:size].astype(np.float64)
    a = 0.5 + 0.5 * (fbm(xs / 19.0, ys / 19.0, seed, 4) - 0.5) + 0.2 * (fbm(xs / 3.0, ys / 3.0, seed + 1, 2) - 0.5)
    Image.fromarray(np.clip(a * 255, 0, 255).astype(np.uint8), "L").save(path)


# ------------------------------------------------------------------ the ground

def ground_uv(x, z):
    x0, z0, x1, z1 = GROUND
    return ((x - x0) / (x1 - x0), (z - z0) / (z1 - z0))


KIND = {
    "WestL2": "gallery", "EastL2": "gallery", "LandingL3": "gallery", "EastL4": "gallery",
    "FoodCourt": "food", "Podium": "runway", "PlanterW": "planter", "PlanterE": "planter",
    "BridgeW": "bridge", "BridgeE": "bridge", "BridgeL3": "bridge", "BridgeL4": "bridge",
    "Sign1": "sign", "Sign2": "speaker", "Sign3": "sign", "Tower": "tower",
    "SaleBalloon1": "balloon", "SaleBalloon2": "balloon",
    "ServiceSign1": "service_sign", "ServiceSign2": "service_sign",
}


def kind(name):
    return KIND.get(name, "gallery")


class Ground:
    """Paints the planar ground. World metres in, pixels out (north up)."""

    def __init__(self):
        self.S = GROUND_SIZE / (GROUND[2] - GROUND[0])
        self.N = GROUND_SIZE

    def px(self, x, z):
        return ((x - GROUND[0]) * self.S, (GROUND[3] - z) * self.S)

    def rect_px(self, x0, z0, x1, z1):
        a, b = self.px(x0, z1)
        c, d = self.px(x1, z0)
        return (int(math.floor(a)), int(math.floor(b)), int(math.ceil(c)), int(math.ceil(d)))


def _terrazzo(X, Z, seed):
    base = np.array(P.TERRAZZO, np.float32)
    n = fbm(X * 0.35, Z * 0.35, seed, 3)
    col = base * (0.94 + 0.10 * n)[..., None]
    # chips: three sizes of speckle from hashed cells
    for scale, colour, amount in ((0.05, P.CHIP_A, 0.18), (0.03, P.CHIP_B, 0.22), (0.08, P.CHIP_C, 0.06)):
        cx = np.floor(X / scale)
        cz = np.floor(Z / scale)
        h = np.sin(cx * 12.9898 + cz * 78.233 + seed) * 43758.5453
        h = h - np.floor(h)
        chip = h < amount
        col = np.where(chip[..., None], np.array(colour, np.float32), col)
    # brass divider strips every 3 m
    strip = (np.abs(((X + 0.0) % 3.0) - 1.5) > 1.47) | (np.abs(((Z + 0.0) % 3.0) - 1.5) > 1.47)
    return np.where(strip[..., None], np.array(P.BRASS, np.float32), col)


def ground(path, data):
    g = Ground()
    pieces = data["pieces"]
    img = np.zeros((g.N, g.N, 3), np.float32) + np.array(P.STEEL_DARK, np.float32)
    areas = [(name, p[:4], kind(name)) for name, p in pieces.items()]
    for name, (x0, z0, x1, z1, y0, y1) in data["spec"]["ramps"].items():
        if not name.startswith("Balustrade"):
            areas.append((name, (x0, z0, x1, z1), "escalator"))
    for name, (x0, z0, x1, z1), k in areas:
        a, b, c, d = g.rect_px(x0, z0, x1, z1)
        a, b = max(0, a - 2), max(0, b - 2)
        c, d = min(g.N, c + 2), min(g.N, d + 2)
        ys, xs = np.mgrid[b:d, a:c].astype(np.float32)
        X = GROUND[0] + (xs + 0.5) / g.S
        Z = GROUND[3] - (ys + 0.5) / g.S
        seed = sum(ord(ch) for ch in name) % 97
        if k == "gallery":
            col = _terrazzo(X, Z, seed)
        elif k == "food":
            check = ((np.floor(X / 0.6) + np.floor(Z / 0.6)) % 2).astype(bool)
            col = np.where(check[..., None], np.array(P.TILE_RED, np.float32), np.array(P.TILE_CREAM, np.float32))
            col = col * (0.95 + 0.08 * fbm(X * 0.5, Z * 0.5, seed, 3))[..., None]
        elif k == "bridge":
            plate = 0.88 + 0.06 * ((np.floor(X / 1.0) + np.floor(Z / 1.0)) % 2)
            col = np.array(P.STEEL, np.float32) * plate[..., None]
            seam = (np.abs((X % 1.0) - 0.5) > 0.48) | (np.abs((Z % 1.0) - 0.5) > 0.48)
            col = np.where(seam[..., None], np.array(P.STEEL_DARK, np.float32), col)
        elif k == "runway":
            col = np.array(P.RUNWAY, np.float32) * (0.96 + 0.04 * fbm(X, Z, seed, 2))[..., None]
            cx = (x0 + x1) / 2
            carpet = np.abs(X - cx) < 1.0
            col = np.where(carpet[..., None], np.array(P.CARPET, np.float32), col)
        elif k == "planter":
            n = fbm(X * 3.0, Z * 3.0, seed, 3)
            col = np.array(P.SOIL, np.float32) * (0.8 + 0.4 * n)[..., None]
            leaf = n > 0.55
            col = np.where(leaf[..., None], np.array(P.PLANT_LIGHT, np.float32), col)
            rim = np.minimum.reduce([X - x0, x1 - X, Z - z0, z1 - Z]) < 0.12
            col = np.where(rim[..., None], np.array(P.POT, np.float32), col)
        elif k == "balloon":
            col = np.array(P.CREAM, np.float32) + 0 * X[..., None]
            rim = np.minimum.reduce([X - x0, x1 - X, Z - z0, z1 - Z]) < 0.12
            col = np.where(rim[..., None], np.array(P.PINK if name.endswith("1") else P.YELLOW, np.float32), col)
        elif k in ("sign", "speaker", "service_sign"):
            col = np.array(P.STEEL_DARK, np.float32) * (0.9 + 0.2 * fbm(X * 2, Z * 2, seed, 2))[..., None]
            rim = np.minimum.reduce([X - x0, x1 - X, Z - z0, z1 - Z]) < 0.1
            col = np.where(rim[..., None], np.array(P.HAZARD_YELLOW, np.float32), col)
        elif k == "tower":
            dia = (np.abs(((X * 4 + Z * 4) % 1.0) - 0.5) < 0.12) | (np.abs(((X * 4 - Z * 4) % 1.0) - 0.5) < 0.12)
            col = np.array(P.STEEL, np.float32) * np.where(dia, 1.08, 0.9)[..., None]
        elif k == "escalator":
            groove = ((Z * 25.0) % 1.0) < 0.45
            col = np.where(groove[..., None], np.array(P.GROOVE, np.float32), np.array(P.STEEL, np.float32))
            step = ((Z - z0) % 0.4) < 0.05
            col = np.where(step[..., None], np.array(P.HAZARD_YELLOW, np.float32), col)
            edge = (X - x0 < 0.08) | (x1 - X < 0.08)
            col = np.where(edge[..., None], np.array(P.HAZARD_YELLOW, np.float32), col)
        else:
            col = np.array(P.TERRAZZO, np.float32) + 0 * X[..., None]
        img[b:d, a:c] = col
    out = Image.fromarray(np.clip(img * 255, 0, 255).astype(np.uint8), "RGB").convert("RGBA")
    _paint(g, out, data)
    out.convert("RGB").save(path)


# ------------------------------------------------------------------ route paint

PAINT = (0.98, 0.97, 0.93)


def _text(g, img, text, x, z, height, color, angle=0.0, path=DISPLAY, outline=None, alpha=235):
    """Lettering on the ground: `height` metres tall, reading for a viewer facing `angle`
    (degrees, 0 = facing north, so the text's top points north)."""
    px = height * g.S
    f = font(path, px)
    x0, y0, x1, y1 = f.getbbox(text)
    w, h = x1 - x0 + px, y1 - y0 + px
    layer = Image.new("RGBA", (int(w), int(h)), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    kw = {"stroke_width": max(2, int(px * 0.08)), "stroke_fill": rgb(outline, alpha)} if outline else {}
    d.text((w / 2, h / 2), text, font=f, fill=rgb(color, alpha), anchor="mm", **kw)
    layer = layer.rotate(-angle, expand=True, resample=Image.BICUBIC)
    cx, cy = g.px(x, z)
    img.alpha_composite(layer, (int(cx - layer.width / 2), int(cy - layer.height / 2)))


def _arrow(g, d, x, z, length, width, angle, color, alpha=225):
    """A painted arrow pointing along `angle` (degrees, 0 = north)."""
    a = math.radians(angle)
    fx, fz = math.sin(a), math.cos(a)
    rx, rz = fz, -fx

    def W(f, r):
        return g.px(x + fx * f + rx * r, z + fz * f + rz * r)
    shaft = [W(-length / 2, -width / 4), W(length * 0.15, -width / 4), W(length * 0.15, width / 4), W(-length / 2, width / 4)]
    head = [W(length * 0.15, -width / 2), W(length / 2, 0), W(length * 0.15, width / 2)]
    d.polygon(shaft, fill=rgb(color, alpha))
    d.polygon(head, fill=rgb(color, alpha))


def _hazard(g, img, x0, z0, x1, z1, along_x=True):
    """Diagonal hazard stripes filling a strip along an edge (clipped to the strip)."""
    a, b, c, e = g.rect_px(x0, z0, x1, z1)
    w, h = max(1, c - a), max(1, e - b)
    strip = Image.new("RGBA", (w, h), rgb(P.HAZARD_YELLOW, 235))
    sd = ImageDraw.Draw(strip)
    step = 0.3 * g.S
    k = -float(w + h)
    while k < w + h:
        sd.polygon([(k, 0), (k + step / 2, 0), (k + step / 2 + h, h), (k + h, h)], fill=rgb(P.HAZARD_BLACK, 235))
        k += step
    img.alpha_composite(strip, (a, b))


def _pill_hint(g, img, d, x, z, angle, red, text):
    """A painted pharmacy capsule pointing the way, red or blue, with a short hint."""
    color = P.RED if red else P.BLUE
    a = math.radians(angle)
    fx, fz = math.sin(a), math.cos(a)
    rx, rz = fz, -fx

    def W(f, r):
        return g.px(x + fx * f + rx * r, z + fz * f + rz * r)
    n = 12
    half = [W(0.55 + 0.35 * math.cos(math.pi / 2 - math.pi * k / n), 0.35 * math.sin(math.pi / 2 - math.pi * k / n)) for k in range(n + 1)]
    back = [W(-0.55 - 0.35 * math.cos(math.pi / 2 - math.pi * k / n), -0.35 * math.sin(math.pi / 2 - math.pi * k / n)) for k in range(n + 1)]
    d.polygon(half + back, fill=rgb(color, 235), outline=rgb(P.WHITE, 235))
    d.line((W(0, -0.35), W(0, 0.35)), fill=rgb(P.WHITE, 235), width=max(2, int(0.06 * g.S)))
    _text(g, img, text, x - fx * 1.25, z - fz * 1.25, 0.42, color, angle=angle, outline=P.WHITE)


def _paint(g, img, data):
    d = ImageDraw.Draw(img)
    spec = data["spec"]
    sx, sz, _ = spec["start"]
    # START by the katana kiosk, the row ahead and the turn to the skybridge.
    x0, y0 = g.px(sx - 1.5, sz + 1.5)
    x1, y1 = g.px(sx + 1.5, sz - 1.5)
    d.rectangle((x0, y0, x1, y1), outline=rgb(P.HACKER, 235), width=int(0.16 * g.S))
    _text(g, img, "START", sx, sz - 2.3, 0.9, P.HACKER, outline=P.INK)
    _text(g, img, "NEO MALL · LEVEL 2", sx, sz + 2.4, 0.42, P.INK, alpha=200)
    for z in (-39.5, -24.5):
        _arrow(g, d, -12.5, z, 1.8, 0.8, 0.0, P.INK, 150)
    _text(g, img, "R  ->", -12.4, -35.6, 0.5, P.RED, angle=0.0, outline=P.WHITE)
    _arrow(g, d, -11.5, -12.0, 1.6, 0.8, 90.0, P.INK, 150)
    _text(g, img, "NO RUNNING", -14.4, -28.0, 0.4, P.INK, angle=90.0, alpha=160)
    # The skybridge stub: red pill (E across) and blue pill (the planters).
    _pill_hint(g, img, d, -7.0, -11.0, 90.0, True, "E")
    _pill_hint(g, img, d, -7.0, -13.1, 90.0, False, "HOP")
    _hazard(g, img, -6.3, -14.0, -6.0, -10.0, along_x=False)
    _hazard(g, img, 6.0, -14.0, 6.3, -10.0, along_x=False)
    # East gallery: up to the escalator.
    _arrow(g, d, 9.0, -4.0, 1.6, 0.8, 0.0, P.INK, 150)
    _arrow(g, d, 9.0, 1.8, 1.6, 0.8, 0.0, P.INK, 150)
    _text(g, img, "ESCALATOR", 12.0, 1.2, 0.5, P.INK, alpha=170)
    # The escalator's missing steps: hazard bands at both edges.
    _hazard(g, img, 8.2, 7.3, 9.8, 7.6, along_x=True)
    _hazard(g, img, 8.2, 10.0, 9.8, 10.3, along_x=True)
    # Landing and level-3 bridge.
    _arrow(g, d, 7.0, 15.0, 1.6, 0.8, 270.0, P.INK, 150)
    _text(g, img, "R  ->", 5.0, 16.3, 0.5, P.RED, angle=270.0, outline=P.WHITE)
    _text(g, img, "LEVEL 3", 11.0, 15.0, 0.6, P.INK, angle=270.0, alpha=180)
    # The food court: north, then the tower fork.
    _text(g, img, "NO SWORDS IN THE FOOD COURT", -13.5, 12.0, 0.5, P.RED, alpha=220)
    _text(g, img, "(THIS MEANS YOU)", -13.5, 11.2, 0.36, P.RED, alpha=200)
    _arrow(g, d, -11.0, 24.0, 1.6, 0.8, 0.0, P.INK, 140)
    _pill_hint(g, img, d, -9.6, 22.4, 90.0, True, "JUMP JUMP E")
    _pill_hint(g, img, d, -9.6, 27.2, 90.0, False, "SIGNS")
    _hazard(g, img, -8.0, 21.0, -7.7, 25.0, along_x=False)
    # Level 4: the top bridge, the gallery and the finish at the game store.
    _text(g, img, "LEVEL 4", -6.0, 24.3, 0.5, P.INK, angle=90.0, alpha=180)
    _arrow(g, d, -2.0, 23.0, 1.6, 0.8, 90.0, P.INK, 150)
    _text(g, img, "R  ->", -0.2, 21.7, 0.5, P.RED, angle=90.0, outline=P.WHITE)
    for z in (27.0, 33.5):
        _arrow(g, d, 11.0, z, 1.8, 0.8, 0.0, P.INK, 150)
    fx, fz = 12.0, 42.0
    a, b = g.px(fx - 3.0, fz + 1.5)
    c, e = g.px(fx + 3.0, fz - 1.5)
    d.rectangle((a, b, c, e), outline=rgb(P.PURPLE, 235), width=int(0.16 * g.S))
    _text(g, img, "FINISH", fx, fz - 2.2, 0.8, P.PURPLE, outline=P.WHITE)
    _text(g, img, "DICE DOJO · FRIDAY NIGHT DUNGEONS", fx, fz + 1.0, 0.36, P.INK, alpha=200)
    # The podium's catwalk marks.
    _text(g, img, "FALL FASHION", -1.0, -37.0, 0.45, P.WHITE, alpha=220)
