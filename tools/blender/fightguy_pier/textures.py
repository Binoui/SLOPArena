"""Fight Night Pier textures (numpy + Pillow). Everything is drawn here; lettering uses the
project's Archivo Black and Baloo 2 (OFL, client/Unity/Assets/Fonts) and the wheel hub and
poster reuse FightGuy's own portrait (Resources/UI/Portraits/FightGuy.png).

ground  (4096 x 4096, RGB): every walkable top, planar over GROUND (north up): bleached
        pier planks, the burnt old pier, gondola roofs, the coaster's grating, the buoys,
        the carousel canopy and the ring's canvas, plus the route paint: START, arrows,
        the JUMP + R and JUMP + E hints, 肉 stamps and the champ's chalky bare footprints.
atlas   (4096 x 4096, RGBA; alpha = StageNeon glow): signs, posters, banners, the hub
        portrait, awnings, cardboard fans, the white block geo.WHITE_UV samples (alpha 0)
        and the lamp block glowing faces sample (LAMP_UV, alpha 255).
detail  (512 x 512, gray): tileable grain for StageVertexLit's triplanar detail.
ripples (512 x 512, RGB): water normals for SLOPArena/StageWater.
"""
from __future__ import annotations

import math
import random
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

from stagekit.noise import fbm

import palette as P

REPO = Path(__file__).resolve().parents[3]
FONTS = REPO / "client/Unity/Assets/Fonts"
DISPLAY = str(FONTS / "ArchivoBlack-Regular.ttf")
ROUND = str(FONTS / "Baloo2-Variable.ttf")
PORTRAIT = REPO / "client/Unity/Assets/Resources/UI/Portraits/FightGuy.png"
# 肉: the brand he stamps on everything (Noto Sans CJK if present, else a drawn glyph).
CJK_CANDIDATES = ["/usr/share/fonts/noto-cjk/NotoSansCJK-Black.ttc", "/usr/share/fonts/noto-cjk/NotoSansCJK-Bold.ttc",
                  "/usr/share/fonts/opentype/noto/NotoSansCJK-Black.ttc", "/usr/share/fonts/opentype/noto/NotoSansCJK-Bold.ttc",
                  "/usr/share/fonts/noto-cjk/NotoSansCJKjp-Black.otf", "/usr/share/fonts/TTF/NotoSansCJK-Bold.ttc"]
NIKU_MASK = REPO / "client/Unity/Assets/CharacterPackages/fightguy/Presentation/Source/Niku.png"

# The planar ground: world rectangle (x0, z0, x1, z1), square, and its size in pixels.
GROUND = (-56.0, -50.0, 52.0, 58.0)
GROUND_SIZE = 4096
SIZE = 4096


def rgb(c, a=255):
    return tuple(int(round(min(1.0, max(0.0, v)) * 255)) for v in c[:3]) + (a,)


def font(path, size):
    return ImageFont.truetype(path, int(size))


def fit_font(path, text, max_w, max_h, start=420):
    size = start
    while size > 8:
        f = font(path, size)
        x0, y0, x1, y1 = f.getbbox(text)
        if x1 - x0 <= max_w and y1 - y0 <= max_h:
            return f
        size = int(size * 0.93)
    return font(path, 8)


def outlined(d, xy, text, f, fill, outline, width, anchor="mm"):
    d.text(xy, text, font=f, fill=fill, anchor=anchor, stroke_width=width, stroke_fill=outline)


# ------------------------------------------------------------------ atlas layout

# (name, w, h) packed into shelves left to right, top to bottom; the top-right 128 x 128
# block stays white for geo.WHITE_UV (0.985, 0.985).
_CELL_SIZES = [
    ("arch", 2048, 512), ("title", 768, 1024), ("ring_banner", 1152, 288),
    ("hub", 1024, 1024), ("striker", 256, 1024), ("welcome", 1280, 256), ("no_fighting", 1280, 288),
    ("gallery", 1536, 256), ("car_bash", 1024, 384), ("dunk", 768, 384), ("niku_dogs", 768, 384),
    ("mirrors", 1024, 384), ("photo", 768, 384), ("prizes", 768, 384), ("collapsed", 1024, 384),
    ("keep_off", 768, 384), ("tickets", 512, 256), ("vip", 1024, 256), ("coaster", 1024, 384),
    ("apron", 1024, 256), ("awning", 512, 512), ("awning_blue", 512, 512), ("planks", 512, 512),
    ("fan_0", 256, 512), ("fan_1", 256, 512), ("fan_2", 256, 512), ("fan_3", 256, 512),
    ("mirror", 256, 512), ("windows", 512, 512), ("hotel", 1024, 256), ("arcade", 1024, 256),
    ("tally", 512, 256), ("niku", 256, 256), ("lamp", 256, 256),
    # the bonus targets' own (bonus.py)
    ("plate", 512, 256), ("punch_marquee", 1024, 256), ("punch_scale", 512, 1024),
]


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


LAMP_UV = ((CELLS["lamp"][0] + CELLS["lamp"][2] / 2) / SIZE, 1 - (CELLS["lamp"][1] + CELLS["lamp"][3] / 2) / SIZE)


def sub_uv(name, fu0, fv0, fu1, fv1, inset=2):
    """A fractional window of a cell (fractions measured from the cell's bottom-left)."""
    (a0, b0), (a1, b1) = uv(name, inset)
    return (a0 + (a1 - a0) * fu0, b0 + (b1 - b0) * fv0), (a0 + (a1 - a0) * fu1, b0 + (b1 - b0) * fv1)


def circle_uv(name, angle, r=1.0, inset=6):
    """UV on the cell's inscribed circle at `angle` (radians, 0 = right, CCW) and radius fraction r."""
    (a0, b0), (a1, b1) = uv(name, inset)
    cu, cv = (a0 + a1) / 2, (b0 + b1) / 2
    return (cu + (a1 - a0) / 2 * r * math.cos(angle), cv + (b1 - b0) / 2 * r * math.sin(angle))


# ------------------------------------------------------------------ drawing helpers

def board(w, h, bg, border=None, bw=10, radius=18):
    im = Image.new("RGBA", (w, h), rgb(bg))
    d = ImageDraw.Draw(im)
    if border:
        d.rounded_rectangle((bw // 2, bw // 2, w - bw // 2, h - bw // 2), radius=radius, outline=rgb(border), width=bw)
    return im, d


def bulbs(d, box, step, r, color=P.BULB, rim=P.GOLD_DARK):
    """Marquee bulbs round a rectangle."""
    x0, y0, x1, y1 = box
    n = max(2, int((x1 - x0) / step))
    m = max(2, int((y1 - y0) / step))
    pts = [(x0 + (x1 - x0) * i / n, y0) for i in range(n)] + [(x1, y0 + (y1 - y0) * j / m) for j in range(m)]
    pts += [(x1 - (x1 - x0) * i / n, y1) for i in range(n)] + [(x0, y1 - (y1 - y0) * j / m) for j in range(m)]
    for x, y in pts:
        d.ellipse((x - r - 3, y - r - 3, x + r + 3, y + r + 3), fill=rgb(rim))
        d.ellipse((x - r, y - r, x + r, y + r), fill=rgb(color))


def niku(size, color=P.CREAM, stroke=None):
    """The 肉 glyph as an RGBA image (FightGuy's own mask if present, else a font, else drawn)."""
    if NIKU_MASK.exists():
        src = Image.open(NIKU_MASK).convert("L").resize((size, size), Image.LANCZOS)
        im = Image.new("RGBA", (size, size), rgb(color))
        im.putalpha(src)
        return im
    for path in CJK_CANDIDATES:
        if Path(path).exists():
            im = Image.new("RGBA", (size, size), (0, 0, 0, 0))
            d = ImageDraw.Draw(im)
            d.text((size / 2, size / 2), "肉", font=font(path, size * 0.9), fill=rgb(color), anchor="mm")
            return im
    im = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    w = max(3, size // 9)
    c = rgb(color)
    d.rectangle((size * 0.12, size * 0.18, size * 0.88, size * 0.92), outline=c, width=w)
    d.line((size * 0.5, size * 0.06, size * 0.5, size * 0.55), fill=c, width=w)
    for s in (-1, 1):
        d.line((size * 0.5, size * 0.3, size * (0.5 + s * 0.28), size * 0.55), fill=c, width=w)
        d.line((size * 0.5, size * 0.55, size * (0.5 + s * 0.28), size * 0.82), fill=c, width=w)
    return im


def portrait(size, circle=True):
    """FightGuy's portrait (the key-art crop), optionally cut to a circle."""
    im = Image.open(PORTRAIT).convert("RGBA").resize((size, size), Image.LANCZOS)
    if circle:
        mask = Image.new("L", (size, size), 0)
        ImageDraw.Draw(mask).ellipse((0, 0, size - 1, size - 1), fill=255)
        im.putalpha(mask)
    return im


def stripes(w, h, a, b, n, vertical=True):
    im = Image.new("RGBA", (w, h), rgb(a))
    d = ImageDraw.Draw(im)
    for i in range(n):
        if i % 2:
            if vertical:
                d.rectangle((w * i / n, 0, w * (i + 1) / n, h), fill=rgb(b))
            else:
                d.rectangle((0, h * i / n, w, h * (i + 1) / n), fill=rgb(b))
    return im


def grime(img, amount=0.12, seed=1):
    a = np.asarray(img).astype(np.float32)
    h, w = a.shape[:2]
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float64)
    n = fbm(xs / 70.0, ys / 70.0, seed, octaves=4)
    k = 1 - amount * np.clip(n * 1.3 - 0.3, 0, 1)
    a[..., :3] *= k[..., None]
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA")


def stack_text(d, w, lines, top, bottom, color, outline=None, ow=0, path=DISPLAY, gap=0.12):
    """Lines of text filling the band [top, bottom], each fitted to the width."""
    n = len(lines)
    band = (bottom - top) / n
    for i, line in enumerate(lines):
        f = fit_font(path, line, w * 0.9, band * (1 - gap))
        y = top + band * (i + 0.5)
        if outline:
            outlined(d, (w / 2, y), line, f, rgb(color), rgb(outline), ow)
        else:
            d.text((w / 2, y), line, font=f, fill=rgb(color), anchor="mm")


# ------------------------------------------------------------------ atlas cells

def _arch():
    w, h = CELLS["arch"][2:]
    im, d = board(w, h, P.RED_DARK, P.GOLD, bw=26, radius=40)
    bulbs(d, (34, 34, w - 34, h - 34), 64, 13)
    f = fit_font(DISPLAY, "FIGHT NIGHT PIER", w * 0.86, h * 0.62)
    outlined(d, (w / 2, h * 0.5), "FIGHT NIGHT PIER", f, rgb(P.GOLD), rgb(P.INK), 12)
    return im


def _title():
    w, h = CELLS["title"][2:]
    im, d = board(w, h, P.CREAM, P.INK, bw=14, radius=6)
    d.rectangle((14, 14, w - 14, h * 0.2), fill=rgb(P.RED))
    stack_text(d, w, ["TITLE DEFENSE #47"], 20, h * 0.2, P.CREAM, P.INK, 6)
    pic = portrait(int(w * 0.62), circle=False)
    im.alpha_composite(pic, (int(w * 0.19), int(h * 0.23)))
    d.rectangle((w * 0.19, h * 0.23, w * 0.81, h * 0.23 + w * 0.62), outline=rgb(P.INK), width=8)
    stack_text(d, w, ["FIGHTGUY", "VS", "??? (TBA)", "TONIGHT!"], h * 0.63, h * 0.97, P.INK, gap=0.22)
    return grime(im, 0.10, 3)


def _ring_banner():
    w, h = CELLS["ring_banner"][2:]
    im, d = board(w, h, P.GI_BLUE, P.GOLD, bw=16, radius=12)
    f = fit_font(DISPLAY, "TITLE DEFENSE #47", w * 0.84, h * 0.6)
    outlined(d, (w / 2, h / 2), "TITLE DEFENSE #47", f, rgb(P.GOLD), rgb(P.INK), 8)
    return im


def _hub():
    w, h = CELLS["hub"][2:]
    im = Image.new("RGBA", (w, h), rgb(P.GOLD))
    d = ImageDraw.Draw(im)
    d.ellipse((0, 0, w - 1, h - 1), fill=rgb(P.GOLD_DARK))
    d.ellipse((26, 26, w - 27, h - 27), fill=rgb(P.GOLD))
    pic = portrait(int(w * 0.84))
    im.alpha_composite(pic, (int(w * 0.08), int(w * 0.08)))
    d.ellipse((int(w * 0.08), int(w * 0.08), int(w * 0.92), int(w * 0.92)), outline=rgb(P.INK), width=14)
    return im


def _striker():
    w, h = CELLS["striker"][2:]
    im, d = board(w, h, P.CREAM, P.RED, bw=10, radius=8)
    levels = ["FIGHTGUY", "CHAMP", "STRONG", "OK", "WEAK"]
    colors = [P.GOLD, P.RED, P.ORANGE, P.GI_BLUE, P.STEEL]
    band = (h - 40) / len(levels)
    for i, (text, col) in enumerate(zip(levels, colors)):
        y0 = 20 + band * i
        d.rectangle((16, y0 + 4, w - 16, y0 + band - 4), fill=rgb(col))
        f = fit_font(DISPLAY, text, w * 0.8, band * 0.42)
        outlined(d, (w / 2, y0 + band / 2), text, f, rgb(P.CREAM), rgb(P.INK), 4)
    return im


def _banner(name, lines, bg, fg, border, outline=None, ow=0, path=DISPLAY):
    w, h = CELLS[name][2:]
    im, d = board(w, h, bg, border, bw=12, radius=14)
    stack_text(d, w, lines, 16, h - 16, fg, outline, ow, path)
    return im


def _gallery():
    w, h = CELLS["gallery"][2:]
    im = stripes(w, h, P.AWNING_RED, P.AWNING_WHITE, 16)
    d = ImageDraw.Draw(im)
    d.rounded_rectangle((w * 0.18, 18, w * 0.82, h - 18), radius=20, fill=rgb(P.INK))
    f = fit_font(DISPLAY, "SHOOTING GALLERY", w * 0.58, h * 0.6)
    d.text((w / 2, h / 2), "SHOOTING GALLERY", font=f, fill=rgb(P.GOLD), anchor="mm")
    return im


def _niku_dogs():
    w, h = CELLS["niku_dogs"][2:]
    im, d = board(w, h, P.GOLD, P.RED, bw=12, radius=16)
    glyph = niku(int(h * 0.62), P.RED)
    im.alpha_composite(glyph, (int(w * 0.06), int(h * 0.1)))
    f = fit_font(DISPLAY, "DOGS", w * 0.5, h * 0.5)
    outlined(d, (w * 0.62, h * 0.42), "DOGS", f, rgb(P.RED), rgb(P.INK), 6)
    f2 = fit_font(ROUND, "WHAT DOES IT MEAN? - THE CHAMP", w * 0.9, h * 0.16)
    d.text((w / 2, h * 0.86), "WHAT DOES IT MEAN? - THE CHAMP", font=f2, fill=rgb(P.INK), anchor="mm")
    return im


def _mirror():
    """A funhouse mirror: FightGuy stretched tall and grinning."""
    w, h = CELLS["mirror"][2:]
    im, d = board(w, h, P.GLASS, P.GOLD, bw=14, radius=100)
    pic = Image.open(PORTRAIT).convert("RGBA").resize((int(w * 0.8), int(h * 0.82)), Image.LANCZOS)
    mask = Image.new("L", pic.size, 0)
    ImageDraw.Draw(mask).ellipse((0, 0, pic.size[0] - 1, pic.size[1] - 1), fill=255)
    pic.putalpha(mask)
    im.alpha_composite(pic, (int(w * 0.1), int(h * 0.09)))
    return im


def _fan(k):
    """A cardboard fan cut-out: a cheering silhouette holding a sign (the crowd he brought)."""
    w, h = CELLS["fan_%d" % k][2:]
    im = Image.new("RGBA", (w, h), rgb(P.CARDBOARD_DARK))
    d = ImageDraw.Draw(im)
    rng = random.Random(70 + k)
    shirt = [P.GI_BLUE, P.RED, P.GOLD, P.TEAL][k]
    skin = [P.SKIN, (0.62, 0.42, 0.30), (0.95, 0.78, 0.62), (0.45, 0.30, 0.22)][k]
    d.rectangle((0, 0, w, h), fill=rgb(P.CARDBOARD))
    cx = w / 2
    d.ellipse((cx - 46, 120, cx + 46, 212), fill=rgb(skin), outline=rgb(P.INK), width=5)
    d.rounded_rectangle((cx - 70, 205, cx + 70, 420), radius=30, fill=rgb(shirt), outline=rgb(P.INK), width=5)
    for s in (-1, 1):
        d.line((cx + s * 60, 230, cx + s * 100, 120 - rng.randint(0, 40)), fill=rgb(skin), width=26)
    d.arc((cx - 24, 160, cx + 24, 196), 10, 170, fill=rgb(P.INK), width=6)
    for s in (-1, 1):
        d.ellipse((cx + s * 18 - 6, 150, cx + s * 18 + 6, 162), fill=rgb(P.INK))
    sign = ["#1", "CHAMP!", "肉", "FIGHT GUY"][k]
    d.rectangle((14, 14, w - 14, 112), fill=rgb(P.CREAM), outline=rgb(P.INK), width=5)
    if sign == "肉":
        g = niku(84, P.RED)
        im.alpha_composite(g, (int(cx - 42), 20))
    else:
        f = fit_font(DISPLAY, sign, w - 50, 80)
        d.text((cx, 63), sign, font=f, fill=rgb(P.RED), anchor="mm")
    d.rectangle((cx - 40, 420, cx + 40, h), fill=rgb(P.CARDBOARD_DARK))
    return grime(im, 0.14, 11 + k)


def _windows():
    """Town windows: a grid of lit and dark panes (alpha = glow on lit panes)."""
    w, h = CELLS["windows"][2:]
    im = Image.new("RGBA", (w, h), rgb(P.TOWN, 0))
    d = ImageDraw.Draw(im)
    rng = random.Random(5)
    for i in range(8):
        for j in range(8):
            x0, y0 = i * w / 8 + 10, j * h / 8 + 12
            lit = rng.random() < 0.55
            col = P.WINDOW if lit else P.TOWN_LIGHT
            if lit and rng.random() < 0.25:
                col = (0.70, 0.86, 1.0)
            d.rectangle((x0, y0, x0 + w / 8 - 20, y0 + h / 8 - 24), fill=rgb(col, 255 if lit else 0))
    return im


def _neon(name, text, color):
    w, h = CELLS[name][2:]
    im = Image.new("RGBA", (w, h), rgb(P.INK, 0))
    d = ImageDraw.Draw(im)
    f = fit_font(DISPLAY, text, w * 0.9, h * 0.72)
    d.text((w / 2, h / 2), text, font=f, fill=rgb(color, 255), anchor="mm", stroke_width=6, stroke_fill=rgb(geo_scale(color, 0.5), 160))
    return im


def geo_scale(color, k):
    return tuple(min(1.0, max(0.0, c * k)) for c in color[:3])


def _tally():
    w, h = CELLS["tally"][2:]
    im, d = board(w, h, P.CHAR, None)
    f = fit_font(ROUND, "TITLE DEFENSES", w * 0.9, h * 0.3)
    d.text((w / 2, h * 0.2), "TITLE DEFENSES", font=f, fill=rgb(P.WHITE), anchor="mm")
    x = 26
    for k in range(46):
        if k % 5 == 4:
            d.line((x - 4 * 22 - 6, h * 0.75, x + 4, h * 0.42), fill=rgb(P.WHITE), width=5)
        else:
            d.line((x, h * 0.42, x, h * 0.8), fill=rgb(P.WHITE), width=5)
        x += 22 if k % 5 != 4 else 34
        if x > w - 20:
            break
    return im


def _plate():
    """The car's licence plate."""
    w, h = CELLS["plate"][2:]
    im, d = board(w, h, P.CREAM, P.GI_BLUE_DARK, bw=14, radius=26)
    for x in (w * 0.1, w * 0.9):
        d.ellipse((x - 11, h * 0.2 - 11, x + 11, h * 0.2 + 11), fill=rgb(P.STEEL_DARK))
    f = fit_font(ROUND, "PIER STATE", w * 0.5, h * 0.2)
    d.text((w / 2, h * 0.21), "PIER STATE", font=f, fill=rgb(P.RED), anchor="mm")
    f = fit_font(DISPLAY, "BONUS", w * 0.8, h * 0.5)
    d.text((w / 2, h * 0.62), "BONUS", font=f, fill=rgb(P.GI_BLUE_DARK), anchor="mm")
    return grime(im, 0.12, 21)


def _punch_marquee():
    w, h = CELLS["punch_marquee"][2:]
    im, d = board(w, h, P.RED_DARK, P.GOLD, bw=16, radius=24)
    bulbs(d, (30, 30, w - 30, h - 30), 58, 10)
    f = fit_font(DISPLAY, "PUNCH-O-METER", w * 0.8, h * 0.48)
    outlined(d, (w / 2, h / 2), "PUNCH-O-METER", f, rgb(P.GOLD), rgb(P.INK), 8)
    return im


def _punch_scale():
    """The punch machine's ranks, read against its meter on the viewer's left (the builder's)."""
    w, h = CELLS["punch_scale"][2:]
    im, d = board(w, h, P.INK, P.GOLD, bw=12, radius=10)
    levels = ["FIGHTGUY", "CHAMP", "CONTENDER", "BUM", "WIMP"]
    colors = [P.GOLD, P.RED, P.ORANGE, P.GI_BLUE, P.STEEL]
    band = (h - 40) / len(levels)
    for i, (text, col) in enumerate(zip(levels, colors)):
        y = 20 + band * (i + 0.5)
        d.line((14, y, w * 0.2, y), fill=rgb(col), width=10)
        f = fit_font(DISPLAY, text, w * 0.7, band * 0.34)
        outlined(d, (w * 0.58, y), text, f, rgb(col), rgb(P.CREAM if col == P.STEEL else P.INK), 3)
    return im


def _planks():
    w, h = CELLS["planks"][2:]
    a = np.zeros((h, w, 3), np.float32)
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float64)
    board_i = np.floor(xs / 64.0)
    tone = 0.85 + 0.15 * np.sin(board_i * 12.9898) ** 2
    grain = 0.92 + 0.08 * fbm(xs / 6.0, ys / 80.0, 3, 3)
    gap = (xs % 64.0) < 3
    k = np.where(gap, 0.35, tone * grain)
    a[...] = np.array(P.PLANK_LIGHT, np.float32) * k[..., None]
    return Image.fromarray(np.clip(a * 255, 0, 255).astype(np.uint8), "RGB").convert("RGBA")


def atlas(path):
    im = Image.new("RGBA", (SIZE, SIZE), (255, 255, 255, 0))

    def put(name, cell):
        x, y, w, h = CELLS[name]
        if cell.size != (w, h):
            cell = cell.resize((w, h), Image.LANCZOS)
        im.paste(cell, (x, y))
    put("arch", _arch())
    put("title", _title())
    put("ring_banner", _ring_banner())
    put("hub", _hub())
    put("striker", _striker())
    put("welcome", _banner("welcome", ["WELCOME, CHAMP!"], P.CREAM, P.RED, P.GI_BLUE, P.INK, 4))
    put("no_fighting", _banner("no_fighting", ["NO FIGHTING ON THE PIER", "(EXCEPT TONIGHT)"], P.WHITE, P.INK, P.RED))
    put("gallery", _gallery())
    put("car_bash", _banner("car_bash", ["CAR BASH!", "8 HITS AND IT'S GONE"], P.GOLD, P.RED, P.INK, P.INK, 5))
    put("dunk", _banner("dunk", ["DUNK THE CHAMP $1", "(CHAMP UNAVAILABLE)"], P.GI_BLUE, P.CREAM, P.GOLD))
    put("niku_dogs", _niku_dogs())
    put("mirrors", _banner("mirrors", ["HALL OF MIRRORS", "SEE MORE FIGHTGUY"], P.TEAL, P.GOLD, P.CREAM, P.INK, 5))
    put("photo", _banner("photo", ["PHOTO WITH THE CHAMP", "FREE (MANDATORY)"], P.CREAM, P.GI_BLUE, P.RED))
    put("prizes", _banner("prizes", ["EVERYBODY WINS", "(A FIGHTGUY)"], P.PINK, P.CREAM, P.GOLD, P.INK, 4))
    put("collapsed", _banner("collapsed", ["PIER CLOSED", "(NOT HIS FAULT)"], P.WHITE, P.RED, P.RED))
    put("keep_off", grime(_banner("keep_off", ["OLD PIER", "KEEP OFF"], P.CHAR_LIGHT, P.CREAM, P.EMBER), 0.3, 9))
    put("tickets", _banner("tickets", ["TICKETS ->"], P.RED, P.CREAM, P.GOLD))
    put("vip", _banner("vip", ["THE CHAMP'S VIP DECK"], P.INK, P.GOLD, P.GOLD))
    put("coaster", _banner("coaster", ["THE KNOCKOUT", "KEEP HANDS INSIDE"], P.COASTER, P.CREAM, P.CREAM, P.INK, 4))
    put("apron", _banner("apron", ["FIGHT NIGHT"], P.GI_BLUE, P.GOLD, P.RED, P.INK, 5))
    put("awning", stripes(512, 512, P.AWNING_RED, P.AWNING_WHITE, 8))
    put("awning_blue", stripes(512, 512, P.GI_BLUE, P.GOLD, 8))
    put("planks", _planks())
    for k in range(4):
        put("fan_%d" % k, _fan(k))
    put("mirror", _mirror())
    put("windows", _windows())
    put("hotel", _neon("hotel", "HOTEL SLOP", P.NEON_PINK))
    put("arcade", _neon("arcade", "ARCADE", P.NEON_BLUE))
    put("tally", _tally())
    glyph = niku(256, P.RED)
    cell = Image.new("RGBA", (256, 256), rgb(P.CREAM))
    cell.alpha_composite(glyph)
    put("niku", cell)
    put("lamp", Image.new("RGBA", (256, 256), (255, 255, 255, 255)))
    put("plate", _plate())
    put("punch_marquee", _punch_marquee())
    put("punch_scale", _punch_scale())
    put("white", Image.new("RGBA", (128, 128), (255, 255, 255, 0)))
    im.save(path)


# ------------------------------------------------------------------ tiling maps

def detail(path, size=512, seed=17):
    ys, xs = np.mgrid[0:size, 0:size].astype(np.float64)
    a = 0.5 + 0.5 * (fbm(xs / 23.0, ys / 23.0, seed, 4) - 0.5) + 0.18 * (fbm(xs / 4.0, ys / 4.0, seed + 1, 2) - 0.5)
    img = Image.fromarray(np.clip(a * 255, 0, 255).astype(np.uint8), "L")
    img.save(path)


def ripples(path, size=512, seed=61):
    ys, xs = np.mgrid[0:size, 0:size].astype(np.float64)
    hgt = fbm(xs / 32.0, ys / 32.0, seed, 4) + 0.4 * fbm(xs / 9.0, ys / 9.0, seed + 1, 3)
    gy, gx = np.gradient(hgt)
    nx, nz = -gx * 6.0, -gy * 6.0
    ny = np.ones_like(nx)
    n = np.sqrt(nx * nx + ny * ny + nz * nz)
    out = np.stack([(nx / n) * 0.5 + 0.5, (nz / n) * 0.5 + 0.5, ny / n], -1)
    Image.fromarray(np.clip(out * 255, 0, 255).astype(np.uint8), "RGB").save(path)


# ------------------------------------------------------------------ the ground

def ground_uv(x, z):
    x0, z0, x1, z1 = GROUND
    return ((x - x0) / (x1 - x0), (z - z0) / (z1 - z0))


KIND = {
    "Promenade": "deck", "Midway": "deck", "Plaza": "deck", "WheelDeck": "deck", "PierHead": "deck", "TicketBooth": "raft",
    "Carousel": "canopy", "OldLanding": "old", "OldDeckB": "old", "OldDeckC": "old", "OldDeckD": "old",
    "OldPierHead": "old", "Piling1": "piling", "Piling2": "piling", "Piling3": "piling",
    "DunkBuoy": "buoy", "SteppingBuoy": "buoy", "BellBuoy": "bell", "PrizeBuoy": "raft", "DunkTank": "deck",
    "Pod1": "pod", "Pod2": "pod", "Pod3": "pod", "Pod4": "pod", "WheelTower": "vip",
    "LiftHill": "coaster", "CoasterDrop": "coaster", "CoasterStraight": "coaster", "CoasterEnd": "coaster",
    "Ring": "ring",
}


def kind(name):
    return KIND.get(name, "deck")


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


def _planks_into(X, Z, base, rng_seed, board=0.22, along_x=True, scorch=0.0, run=3.2):
    """Deck boards: rows `board` metres wide, butt joints every `run` m staggered per row."""
    across = Z if along_x else X
    along = X if along_x else Z
    row = np.floor(across / board)
    h = np.sin(row * 12.9898 + rng_seed * 78.233) * 43758.5453
    hrow = h - np.floor(h)
    tone = 0.84 + 0.22 * hrow
    joint_off = hrow * run
    seg = np.floor((along + joint_off) / run)
    hseg = np.sin(seg * 3.7 + row * 1.31 + rng_seed) * 9871.3
    hseg = hseg - np.floor(hseg)
    tone = tone * (0.93 + 0.12 * hseg)
    grain = 0.94 + 0.08 * np.sin(along * 7.0 + hrow * 40.0 + np.sin(along * 1.3 + row) * 2.0)
    frac_across = across / board - row
    gap = frac_across < 0.09
    frac_along = ((along + joint_off) / run) - seg
    butt = (frac_along < 0.012) | (frac_along > 0.988)
    nail = (np.abs(frac_along - 0.5) < 0.004) & (np.abs(frac_across - 0.5) > 0.25) & (np.abs(frac_across - 0.5) < 0.31)
    k = np.where(gap | butt, 0.32, tone * grain)
    k = np.where(nail, 0.45, k)
    c = np.array(base, np.float32)[None, None, :] * k[..., None]
    if scorch > 0:
        burn = fbm(X * 0.35, Z * 0.35, rng_seed + 5, 4)
        heavy = np.clip((burn - 0.45) * 3.0, 0, 1) * scorch
        c = c * (1 - heavy[..., None] * 0.75) + np.array(P.ASH, np.float32) * (heavy[..., None] * 0.15)
    return c


def ground(path, data):
    g = Ground()
    pieces = data["pieces"]
    img = np.zeros((g.N, g.N, 3), np.float32) + np.array(P.SEA_DEEP, np.float32)
    for name, (x0, z0, x1, z1, top) in pieces.items():
        a, b, c, d = g.rect_px(x0, z0, x1, z1)
        a, b = max(0, a - 2), max(0, b - 2)
        c, d = min(g.N, c + 2), min(g.N, d + 2)
        ys, xs = np.mgrid[b:d, a:c].astype(np.float32)
        X = GROUND[0] + (xs + 0.5) / g.S
        Z = GROUND[3] - (ys + 0.5) / g.S
        k = kind(name)
        seed = sum(ord(ch) for ch in name) % 97
        if k == "deck":
            col = _planks_into(X, Z, P.PLANK, seed)
            stain = fbm(X * 0.12, Z * 0.12, seed + 3, 4)
            col = col * (0.9 + 0.2 * stain[..., None])
        elif k == "old":
            col = _planks_into(X, Z, P.CHAR_LIGHT, seed, scorch=1.0, run=2.4)
        elif k == "raft":
            col = _planks_into(X, Z, P.PLANK_DARK, seed, board=0.3, along_x=False, run=9.0)
        elif k == "piling":
            cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
            r = np.hypot(X - cx, Z - cz)
            rings = 0.82 + 0.18 * np.sin(r * 38.0) ** 2
            col = np.array(P.CHAR_LIGHT, np.float32) * rings[..., None]
            edge = np.maximum(np.abs(X - cx) - (x1 - x0) / 2 + 0.12, np.abs(Z - cz) - (z1 - z0) / 2 + 0.12) > 0
            col = np.where(edge[..., None], np.array(P.CHAR, np.float32), col)
        elif k in ("buoy", "bell"):
            cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
            ang = np.arctan2(Z - cz, X - cx)
            seg = (np.floor((ang + math.pi) / (math.pi / 4)) % 2).astype(bool)
            base = P.BUOY_RED if k == "bell" or name != "SteppingBuoy" else P.BUOY_ORANGE
            col = np.where(seg[..., None], np.array(base, np.float32), np.array(P.WHITE, np.float32) * 0.82)
            r = np.hypot(X - cx, Z - cz)
            col = np.where((r < 0.35)[..., None], np.array(P.IRON, np.float32), col)
        elif k == "pod":
            colors = {"Pod1": P.RED, "Pod2": P.GI_BLUE, "Pod3": P.GOLD_DARK, "Pod4": P.TEAL}
            base = np.array(colors[name], np.float32) * 0.85
            ridge = 0.9 + 0.1 * (np.sin((X - x0) * 18.0) > 0.6)
            col = base * ridge[..., None]
            rim = np.minimum.reduce([X - x0, x1 - X, Z - z0, z1 - Z]) < 0.12
            col = np.where(rim[..., None], np.array(P.STEEL_DARK, np.float32), col)
        elif k == "vip":
            col = _planks_into(X, Z, P.RED_DARK, seed, board=0.3, run=6.0) * 1.3
        elif k == "coaster":
            grid = ((np.floor(X / 0.12) + np.floor(Z / 0.12)) % 2) * 0.08 + 0.86
            col = np.array(P.STEEL, np.float32) * grid[..., None]
            ties = ((Z - z0) % 1.1) < 0.22 if name == "CoasterStraight" else ((X - x0) % 1.1) < 0.22
            col = np.where(ties[..., None], np.array(P.PLANK_DARK, np.float32), col)
        elif k == "canopy":
            cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
            ang = np.arctan2(Z - cz, X - cx)
            r = np.hypot(X - cx, Z - cz)
            seg = (np.floor((ang + math.pi) / (math.pi / 8)) % 2).astype(bool)
            col = np.where(seg[..., None], np.array(P.AWNING_RED, np.float32), np.array(P.AWNING_WHITE, np.float32) * 0.86)
            col = np.where((r > 3.9)[..., None], np.array(P.PLANK_DARK, np.float32), col)
            col = np.where((r < 0.6)[..., None], np.array(P.GOLD, np.float32), col)
        elif k == "ring":
            col = np.array(P.CANVAS, np.float32) * (0.92 + 0.08 * fbm(X * 0.8, Z * 0.8, 4, 3))[..., None]
        else:
            col = np.array(P.PLANK, np.float32) + 0 * X[..., None]
        img[b:d, a:c] = col
    out = Image.fromarray(np.clip(img * 255, 0, 255).astype(np.uint8), "RGB").convert("RGBA")
    _paint(g, out, data)
    out.convert("RGB").save(path)


# ------------------------------------------------------------------ route paint

PAINT = (0.86, 0.84, 0.78)   # off-white so the low sun never clips it
CHALK = (0.84, 0.84, 0.80)


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


def _arrow(g, d, x, z, length, width, angle, color):
    """A painted arrow pointing along `angle` (degrees, 0 = north)."""
    a = math.radians(angle)
    fx, fz = math.sin(a), math.cos(a)
    rx, rz = fz, -fx

    def W(f, r):
        return g.px(x + fx * f + rx * r, z + fz * f + rz * r)
    shaft = [W(-length / 2, -width / 4), W(length * 0.15, -width / 4), W(length * 0.15, width / 4), W(-length / 2, width / 4)]
    head = [W(length * 0.15, -width / 2), W(length / 2, 0), W(length * 0.15, width / 2)]
    d.polygon(shaft, fill=rgb(PAINT, 225))
    d.polygon(head, fill=rgb(PAINT, 225))


def _stamp(g, img, x, z, size, angle=0.0, color=P.RED, alpha=200):
    glyph = niku(int(size * g.S), color)
    a = np.asarray(glyph).astype(np.float32)
    a[..., 3] *= alpha / 255.0
    glyph = Image.fromarray(a.astype(np.uint8), "RGBA").rotate(-angle, expand=True, resample=Image.BICUBIC)
    cx, cy = g.px(x, z)
    img.alpha_composite(glyph, (int(cx - glyph.width / 2), int(cy - glyph.height / 2)))


def _foot(g, d, x, z, angle, left):
    """One chalky bare footprint (he trains barefoot)."""
    a = math.radians(angle)
    fx, fz = math.sin(a), math.cos(a)
    rx, rz = fz, -fx
    side = -0.09 if left else 0.09
    cx, cz = x + rx * side, z + rz * side

    def W(f, r):
        return g.px(cx + fx * f + rx * r, cz + fz * f + rz * r)
    sole = [W(-0.12, -0.04), W(-0.06, -0.055), W(0.05, -0.05), W(0.1, -0.03), W(0.1, 0.035), W(0.03, 0.05), W(-0.08, 0.04), W(-0.13, 0.0)]
    d.polygon(sole, fill=rgb(CHALK, 150))
    for k, t in enumerate((-0.045, -0.02, 0.005, 0.03, 0.05)):
        px, py = W(0.135 - abs(t) * 0.4, t * (1 if left else -1))
        r = (0.022 if k == 0 else 0.016) * g.S
        d.ellipse((px - r, py - r, px + r, py + r), fill=rgb(CHALK, 150))


def _footsteps(g, d, pts, stride=0.75):
    """Footprints along a polyline of (x, z) points."""
    left = True
    for (x0, z0), (x1, z1) in zip(pts, pts[1:]):
        L = math.hypot(x1 - x0, z1 - z0)
        ang = math.degrees(math.atan2(x1 - x0, z1 - z0))
        n = max(1, int(L / stride))
        for i in range(n):
            t = (i + 0.5) / n
            _foot(g, d, x0 + (x1 - x0) * t, z0 + (z1 - z0) * t, ang, left)
            left = not left


def _paint(g, img, data):
    d = ImageDraw.Draw(img)
    spec = data["spec"]
    sx, sz, _ = spec["start"]
    # START box on the promenade, the car bash to its right, then up the midway.
    x0, y0 = g.px(sx - 1.4, sz + 1.4)
    x1, y1 = g.px(sx + 1.4, sz - 1.4)
    d.rectangle((x0, y0, x1, y1), outline=rgb(P.GOLD, 230), width=int(0.16 * g.S))
    _text(g, img, "START", sx, sz - 2.2, 0.9, P.GOLD, outline=P.INK)
    _arrow(g, d, sx, sz + 3.4, 2.4, 1.0, 0.0, PAINT)
    _text(g, img, "CAR BASH ->", 6.5, -45.6, 0.55, PAINT)
    _text(g, img, "FIGHT NIGHT", 0.0, -40.0, 1.1, P.GOLD, outline=P.RED_DARK)
    for z in (-34.0, -20.0):
        _arrow(g, d, -0.6, z, 1.8, 0.8, 0.0, PAINT)
    _text(g, img, "THE RING IS THAT WAY", -0.6, -18.2, 0.42, PAINT)
    # The plaza: the old pier crossing hint, painted at the west edge.
    _text(g, img, "JUMP + R", -8.2, -11.6, 0.7, P.GOLD, angle=-90.0, outline=P.INK)
    for k in range(4):
        z = -8.0 + k * 0.35
        x0_, y0_ = g.px(-9.9, z)
        x1_, y1_ = g.px(-7.4 + k * 0.4, z)
        d.line((x0_, y0_, x1_, y1_), fill=rgb(P.GOLD, 200), width=int(0.08 * g.S))
    _text(g, img, "NO RAILINGS. GOOD LUCK, CHAMP.", -7.0, -5.0, 0.32, PAINT)
    # The old pier: scorch, a tally of title defenses scratched in chalk, KEEP OFF.
    _text(g, img, "KEEP OFF", -27.0, -12.6, 0.6, P.ASH)
    _text(g, img, "TITLE DEFENSES: 46", -32.0, 35.4, 0.45, CHALK, alpha=170)
    _text(g, img, "||||| ||||| ||||| |||||", -32.0, 34.6, 0.42, CHALK, alpha=170)
    # The wheel deck: the Rising Dragon hint at the tower's foot.
    _text(g, img, "JUMP + E", -6.4, 27.0, 0.55, P.GOLD, angle=90.0, outline=P.INK)
    _arrow(g, d, -5.0, 27.0, 1.2, 0.6, 90.0, P.GOLD)
    _text(g, img, "THE CHAMP'S VIP DECK", 0.0, 24.6, 0.38, P.GOLD)
    _text(g, img, "WHEEL CLOSED: GONDOLAS BORROWED", -1.0, 36.6, 0.36, PAINT)
    # The coaster straight and the pier head.
    _text(g, img, "R ->", 22.0, 23.0, 0.6, P.GOLD, angle=0.0, outline=P.INK)
    _text(g, img, "RINGSIDE", 5.0, 41.8, 0.6, P.GOLD, outline=P.RED_DARK)
    # The ring canvas: FIGHT NIGHT and a big 肉 in the centre circle.
    rx0, rz0, rx1, rz1, _ = data["pieces"]["Ring"]
    cx, cz = (rx0 + rx1) / 2, (rz0 + rz1) / 2
    a_, b_ = g.px(cx - 2.4, cz + 2.4)
    c_, d_ = g.px(cx + 2.4, cz - 2.4)
    d.ellipse((a_, b_, c_, d_), fill=rgb(P.GI_BLUE, 235), outline=rgb(P.GOLD, 255), width=int(0.14 * g.S))
    _stamp(g, img, cx, cz, 3.2, color=P.GOLD, alpha=255)
    _text(g, img, "FIGHT NIGHT", cx, rz0 + 0.9, 0.55, P.GI_BLUE)
    _text(g, img, "TITLE DEFENSE #47", cx, rz1 - 0.9, 0.45, P.RED, angle=180.0)
    # 肉 stamps where he has been.
    for (x, z, s, ang) in ((-3.5, -41.0, 1.0, 12), (3.8, -19.0, 0.8, -20), (-29.0, -6.0, 0.9, 25), (-6.0, 36.0, 1.1, -8),
                           (12.0, 50.0, 1.0, 15), (8.0, -12.0, 0.8, 30)):
        _stamp(g, img, x, z, s, ang)
    # The champ's chalky bare footprints along the route.
    _footsteps(g, d, [(0.4, -42.0), (0.6, -38.5), (2.0, -36.5)])
    _footsteps(g, d, [(-2.0, -20.0), (-6.0, -13.5), (-7.5, -10.0)])
    _footsteps(g, d, [(-24.0, -8.6), (-26.5, -6.0), (-29.0, -4.4)])
    _footsteps(g, d, [(-30.0, 28.5), (-26.0, 29.0)])
    _footsteps(g, d, [(11.5, 46.0), (10.4, 47.6)])
