"""Original procedural textures for Slop Pit (drawn with Pillow; run inside Blender or not).

canvas (2048 x 2048): the ring top, planar over its 16 x 16 m footprint (north up).
floor  (2048 x 2048): the ringside floor, planar over the 32 x 32 m floor box (north up).
atlas  (2048 x 2048, RGBA; alpha = StageNeon glow mask, ignored by StageVertexLit):
  posters, cloth banners, ring-skirt strips, plywood hoarding, hazard stripes, light
  boxes, the odds board and the solid white block that geo.WHITE_UV samples.
detail (512 x 512): tileable grayscale grit for StageVertexLit's triplanar detail.

Lettering uses the project's Archivo Black and Space Mono (OFL, client/Unity/Assets/Fonts).
Poster and banner art crops the roster portraits in client/Unity/Assets/Resources/UI/Portraits;
everything else is drawn here.
"""
from __future__ import annotations

import math
import random
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageEnhance, ImageFilter, ImageFont, ImageOps

REPO = Path(__file__).resolve().parents[3]
FONTS = REPO / "client/Unity/Assets/Fonts"
PORTRAITS = REPO / "client/Unity/Assets/Resources/UI/Portraits"
DISPLAY = FONTS / "ArchivoBlack-Regular.ttf"
MONO = FONTS / "SpaceMono-Regular.ttf"

SIZE = 2048
SS = 2  # supersampling for cells

INK = (27, 26, 24)
PAPER = (240, 238, 232)
OLD_PAPER = (226, 214, 186)
YELLOW = (255, 204, 34)
ORANGE = (242, 107, 26)
RED = (168, 34, 28)
BLUE = (42, 70, 158)
SLOP = (120, 170, 40)
BLACK_CLOTH = (24, 22, 21)

# name -> (x, y, w, h) in atlas pixels (y down). UVs flip v (see uv()).
CELLS = {}
POSTERS = ["fight_night", "manki_bonk", "wibou", "bonk", "no_refunds", "get_in", "bet_fightguy"]
for _i, _n in enumerate(POSTERS):
    CELLS["poster_" + _n] = (_i * 256, 0, 256, 384)
CELLS["white"] = (1984, 0, 64, 64)
CELLS["exit"] = (1792, 64, 192, 72)
CELLS["bets"] = (1792, 144, 256, 96)
CELLS["tickets"] = (1792, 248, 256, 96)
BANNERS = ["fightguy", "manki", "wibou", "bonk", "crown", "crown_slop"]
for _i, _n in enumerate(BANNERS):
    CELLS["banner_" + _n] = (_i * 192, 384, 192, 480)
HOARDING = ["ply_raw", "ply_black_tag", "ply_red_crown", "ply_raw_ko", "ply_grey_pit", "ply_black_scribble"]
for _i, _n in enumerate(HOARDING):
    CELLS["hoard_" + _n] = (1152 + _i * 144, 384, 144, 288)
CELLS["hazard"] = (1152, 672, 512, 64)
CELLS["stencil_refunds"] = (1152, 736, 512, 96)
CELLS["stencil_fighters"] = (1152, 832, 512, 64)
CELLS["crown"] = (1664, 672, 192, 192)
SKIRTS = ["slop_pit", "sponsors_a", "no_rules", "sponsors_b"]
for _i, _n in enumerate(SKIRTS):
    CELLS["skirt_" + _n] = (0, 896 + _i * 144, 2048, 144)
CELLS["odds"] = (0, 1472, 512, 384)
CELLS["fighters_box"] = (512, 1472, 512, 128)
CELLS["slop_cola"] = (512, 1600, 512, 128)
CELLS["bonk_burger"] = (512, 1728, 512, 128)
CELLS["chalk_rules"] = (1024, 1472, 512, 384)


def uv(name, inset=2):
    """((u0, v0), (u1, v1)) of an atlas cell with a small texel inset."""
    x, y, w, h = CELLS[name]
    u0, u1 = (x + inset) / SIZE, (x + w - inset) / SIZE
    v1, v0 = 1 - (y + inset) / SIZE, 1 - (y + h - inset) / SIZE
    return (u0, v0), (u1, v1)


# ---------------------------------------------------------------- drawing kit

def font(size, mono=False):
    return ImageFont.truetype(str(MONO if mono else DISPLAY), max(6, int(size)))


def fit_font(draw, text, box_w, box_h, mono=False, start=400):
    size = start
    while size > 6:
        f = font(size, mono)
        l, t, r, b = draw.textbbox((0, 0), text, font=f)
        if r - l <= box_w and b - t <= box_h:
            return f
        size = int(size * 0.92)
    return font(6, mono)


def text(draw, xy, s, f, fill, anchor="mm", shadow=None, offset=(0, 0), stroke=0, stroke_fill=None):
    """Lettering with an optional print-offset shadow (brand: physical print offsets)."""
    if shadow is not None:
        draw.text((xy[0] + offset[0], xy[1] + offset[1]), s, font=f, fill=shadow, anchor=anchor)
    draw.text(xy, s, font=f, fill=fill, anchor=anchor, stroke_width=stroke, stroke_fill=stroke_fill)


def fitted(draw, box, s, fill, shadow=None, offset_frac=0.05, mono=False, anchor="mm"):
    x0, y0, x1, y1 = box
    f = fit_font(draw, s, x1 - x0, y1 - y0, mono=mono)
    size = f.size
    off = (max(1, int(size * offset_frac)), max(1, int(size * offset_frac)))
    text(draw, ((x0 + x1) / 2, (y0 + y1) / 2), s, f, fill, anchor=anchor, shadow=shadow, offset=off)


def crown(draw, cx, cy, w, fill, outline=None):
    """The SLOPArena crown mark: band plus three points with ball tips."""
    h = w * 0.62
    x0, x1 = cx - w / 2, cx + w / 2
    yb = cy + h / 2
    band = h * 0.22
    pts = [(x0, yb), (x0, cy - h * 0.18), (x0 + w * 0.25, cy + h * 0.05), (cx, cy - h / 2 + w * 0.06),
           (x1 - w * 0.25, cy + h * 0.05), (x1, cy - h * 0.18), (x1, yb)]
    draw.polygon(pts, fill=fill, outline=outline)
    r = w * 0.065
    for px, py in ((x0, cy - h * 0.18), (cx, cy - h / 2 + w * 0.06), (x1, cy - h * 0.18)):
        draw.ellipse([px - r, py - r * 1.6, px + r, py + r * 0.4], fill=fill)
    draw.rectangle([x0, yb - band, x1, yb], fill=fill)


def grunge(img, rng, amount=0.08, dark=(0, 0, 0), light=None, rmax=None):
    """Speckles, scratches and worn ink: honest photocopy texture. Density scales with area."""
    d = ImageDraw.Draw(img)
    w, h = img.size
    rmax = rmax or max(1.2, min(w, h) / 300.0)
    area = w * h / 10000.0
    for _ in range(int(amount * 40 * area)):
        x, y = rng.uniform(0, w), rng.uniform(0, h)
        r = rng.uniform(0.25, 1.0) * rmax
        col = dark if rng.random() < 0.6 or light is None else light
        d.ellipse([x - r, y - r, x + r, y + r], fill=col)
    for _ in range(int(amount * 4 * area)):
        x, y = rng.uniform(0, w), rng.uniform(0, h)
        L = rng.uniform(8, 40) * min(w, h) / 512.0
        a = rng.uniform(0, math.pi)
        d.line([(x, y), (x + math.cos(a) * L, y + math.sin(a) * L)], fill=dark if light is None else light, width=1)


def portrait(name, w, h, contrast=1.25, saturation=0.9):
    im = Image.open(PORTRAITS / (name + ".png")).convert("RGB")
    im = ImageOps.fit(im, (int(w), int(h)), method=Image.LANCZOS, centering=(0.5, 0.35))
    im = ImageEnhance.Contrast(im).enhance(contrast)
    im = ImageEnhance.Color(im).enhance(saturation)
    return im


def noise_layer(w, h, rng, scale, lo, hi):
    """Smooth value noise in [lo, hi] (bilinear upscale of a small random grid)."""
    gw, gh = max(2, int(w / scale)), max(2, int(h / scale))
    small = Image.new("L", (gw, gh))
    small.putdata([int(rng.uniform(lo, hi)) for _ in range(gw * gh)])
    return small.resize((w, h), Image.BICUBIC)


# ---------------------------------------------------------------- posters

def _poster(name, rng):
    W, H = 256 * SS, 384 * SS
    img = Image.new("RGB", (W, H), OLD_PAPER)
    d = ImageDraw.Draw(img)
    m = int(W * 0.05)
    if name in ("fight_night", "manki_bonk"):
        a, b = ("FightGuy", "Manki") if name == "fight_night" else ("Manki", "Bonk")
        top = int(H * 0.16)
        bottom = int(H * 0.74)
        pa = portrait(a, W - 2 * m, bottom - top)
        pb = portrait(b, W - 2 * m, bottom - top)
        mask = Image.new("L", pa.size, 0)
        ImageDraw.Draw(mask).polygon([(0, 0), (pa.size[0] * 0.62, 0), (pa.size[0] * 0.38, pa.size[1]), (0, pa.size[1])], fill=255)
        art = Image.composite(pa, pb, mask)
        img.paste(art, (m, top))
        sd = ImageDraw.Draw(img)
        sd.line([(m + art.size[0] * 0.62, top), (m + art.size[0] * 0.38, bottom)], fill=YELLOW, width=int(W * 0.035))
        fitted(sd, (m, int(H * 0.02), W - m, top - int(H * 0.015)), "SLOP PIT", INK, shadow=YELLOW)
        cx, cy = W / 2, (top + bottom) / 2
        sd.regular_polygon((cx, cy, W * 0.13), 12, rotation=7, fill=YELLOW, outline=INK)
        fitted(sd, (cx - W * 0.09, cy - W * 0.07, cx + W * 0.09, cy + W * 0.07), "VS", INK)
        fitted(sd, (m, bottom + int(H * 0.02), W - m, int(H * 0.88)), "FRIDAY" if name == "fight_night" else "SATURDAY", INK, shadow=ORANGE)
        fitted(sd, (m, int(H * 0.89), W - m, H - m), "%s VS %s" % (a.upper(), b.upper()), INK)
    elif name in ("wibou", "bonk", "bet_fightguy"):
        who = {"wibou": "Wibou", "bonk": "Bonk", "bet_fightguy": "FightGuy"}[name]
        top = int(H * 0.04)
        bottom = int(H * 0.70)
        img.paste(portrait(who, W - 2 * m, bottom - top), (m, top))
        sd = ImageDraw.Draw(img)
        if name == "bet_fightguy":
            fitted(sd, (m, bottom + int(H * 0.02), W - m, int(H * 0.84)), "BET ON", INK, shadow=YELLOW)
            fitted(sd, (m, int(H * 0.84), W - m, H - m), "FIGHTGUY 2-1", RED)
        else:
            fitted(sd, (m, bottom + int(H * 0.02), W - m, int(H * 0.86)), who.upper(), INK, shadow=ORANGE)
            fitted(sd, (m, int(H * 0.87), W - m, H - m), "NO MERCY" if name == "wibou" else "HEAVY HITTER", INK)
    elif name == "no_refunds":
        img = Image.new("RGB", (W, H), YELLOW)
        sd = ImageDraw.Draw(img)
        crown(sd, W / 2, H * 0.16, W * 0.42, INK)
        for k, word in enumerate(("NO", "RULES", "NO", "REFUNDS")):
            y0 = H * (0.30 + k * 0.165)
            fitted(sd, (m, y0, W - m, y0 + H * 0.15), word, INK, shadow=ORANGE)
    elif name == "get_in":
        img = Image.new("RGB", (W, H), INK)
        sd = ImageDraw.Draw(img)
        crown(sd, W / 2, H * 0.18, W * 0.5, YELLOW)
        for k, word in enumerate(("GET", "IN THE", "SLOP")):
            y0 = H * (0.36 + k * 0.17)
            fitted(sd, (m, y0, W - m, y0 + H * 0.16), word, YELLOW if word != "SLOP" else SLOP, shadow=ORANGE)
        for k in range(7):  # slop drips off the bottom edge
            x = rng.uniform(m, W - m)
            L = rng.uniform(H * 0.03, H * 0.09)
            sd.rounded_rectangle([x - W * 0.018, H - L - m * 0.4, x + W * 0.018, H], radius=W * 0.018, fill=SLOP)
    img = img.convert("RGB")
    grunge(img, rng, 0.06, dark=(60, 50, 40), light=(250, 245, 230))
    # torn, slightly darkened paper border
    edge = Image.new("L", img.size, 0)
    ImageDraw.Draw(edge).rectangle([2, 2, W - 3, H - 3], outline=255, width=int(W * 0.012))
    img = Image.composite(Image.new("RGB", img.size, (120, 108, 90)), img, edge.filter(ImageFilter.GaussianBlur(3)))
    return img.resize((256, 384), Image.LANCZOS)


# ---------------------------------------------------------------- banners and skirts

def _cloth(w, h, base, rng, folds=True):
    img = Image.new("RGB", (w, h), base)
    if folds:
        shade = noise_layer(w, 4, rng, max(1, w / 9), 70, 130).resize((w, h))
        img = ImageChops.multiply(img, Image.merge("RGB", [shade.point(lambda v: min(255, v + 140))] * 3))
    return img


def _banner(name, rng):
    W, H = 192 * SS, 480 * SS
    img = _cloth(W, H, BLACK_CLOTH, rng)
    d = ImageDraw.Draw(img)
    m = int(W * 0.08)
    d.rectangle([m * 0.5, m * 0.5, W - m * 0.5, H - m * 0.5], outline=YELLOW, width=int(W * 0.025))
    if name in ("fightguy", "manki", "wibou", "bonk"):
        who = {"fightguy": "FightGuy", "manki": "Manki", "wibou": "Wibou", "bonk": "Bonk"}[name]
        side = W - 2 * m
        p = portrait(who, side, side * 1.2)
        img.paste(p, (m, int(H * 0.08)))
        d = ImageDraw.Draw(img)
        d.rectangle([m, H * 0.08, m + side, H * 0.08 + side * 1.2], outline=YELLOW, width=int(W * 0.02))
        crown(d, W / 2, H * 0.62, W * 0.34, YELLOW)
        fitted(d, (m, H * 0.70, W - m, H * 0.84), who.upper(), YELLOW, shadow=ORANGE)
        fitted(d, (m, H * 0.86, W - m, H * 0.93), "SLOP PIT", PAPER)
    else:
        crown(d, W / 2, H * 0.17, W * 0.64, YELLOW)
        letters = "SLOP" if name == "crown" else "PIT"
        span = 0.66 if name == "crown" else 0.5
        for k, ch in enumerate(letters):
            y0 = H * (0.30 + k * span / len(letters))
            fitted(d, (m, y0, W - m, y0 + H * span / len(letters) * 0.92), ch, YELLOW, shadow=ORANGE)
    grunge(img, rng, 0.04, dark=(10, 10, 10), light=(70, 66, 60))
    return img.resize((192, 480), Image.LANCZOS)


def _skirt(name, rng):
    W, H = 2048 * SS, 144 * SS
    img = _cloth(W, H, (30, 28, 27), rng)
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, W, H * 0.07], fill=YELLOW)          # apron trim along the top
    d.rectangle([0, H * 0.07, W, H * 0.10], fill=(12, 11, 10))
    dirt = Image.linear_gradient("L").resize((W, H)).point(lambda v: int(255 - max(0, v - 150) * 0.9))
    img = ImageChops.multiply(img, Image.merge("RGB", [dirt] * 3))
    d = ImageDraw.Draw(img)
    y0, y1 = H * 0.2, H * 0.92

    def logo(x0, x1):
        crown(d, x0 + (x1 - x0) * 0.09, (y0 + y1) / 2, (y1 - y0) * 0.9, YELLOW)
        fitted(d, (x0 + (x1 - x0) * 0.2, y0, x1, y1), "SLOP PIT", YELLOW, shadow=ORANGE)

    def patch(x0, x1, words, bg, fg):
        d.rounded_rectangle([x0, y0 + H * 0.05, x1, y1 - H * 0.05], radius=H * 0.08, fill=bg)
        fitted(d, (x0 + H * 0.12, y0 + H * 0.1, x1 - H * 0.12, y1 - H * 0.1), words, fg)

    if name == "slop_pit":
        logo(W * 0.04, W * 0.46)
        fitted(d, (W * 0.52, y0 + H * 0.12, W * 0.96, y1 - H * 0.12), "GET IN THE SLOP", PAPER)
    elif name == "sponsors_a":
        patch(W * 0.03, W * 0.30, "SLOP COLA", RED, PAPER)
        logo(W * 0.35, W * 0.70)
        patch(W * 0.74, W * 0.97, "BONK BURGER", YELLOW, INK)
    elif name == "no_rules":
        fitted(d, (W * 0.03, y0 + H * 0.1, W * 0.45, y1 - H * 0.1), "NO RULES", PAPER, shadow=RED)
        logo(W * 0.50, W * 0.97)
    else:
        patch(W * 0.03, W * 0.30, "MANKI MOTORS", ORANGE, INK)
        logo(W * 0.35, W * 0.70)
        patch(W * 0.74, W * 0.97, "WIBOU WATCHES", PAPER, INK)
    grunge(img, rng, 0.05, dark=(8, 8, 8), light=(60, 56, 50))
    return img.resize((2048, 144), Image.LANCZOS)


# ---------------------------------------------------------------- hoarding, signs, boards

def _spray(d, pts, color, width, rng, drips=True):
    d.line(pts, fill=color, width=int(width), joint="curve")
    for p in pts:
        r = width / 2
        d.ellipse([p[0] - r, p[1] - r, p[0] + r, p[1] + r], fill=color)
    if drips:
        for p in rng.sample(pts, k=min(3, len(pts))):
            L = rng.uniform(width * 1.5, width * 5)
            d.line([(p[0], p[1]), (p[0], p[1] + L)], fill=color, width=max(1, int(width * 0.35)))


def _hoarding(name, rng):
    W, H = 144 * SS, 288 * SS
    bases = {"ply_raw": (168, 134, 92), "ply_black_tag": (30, 29, 28), "ply_red_crown": (110, 30, 26),
             "ply_raw_ko": (160, 128, 88), "ply_grey_pit": (88, 88, 86), "ply_black_scribble": (26, 25, 24)}
    base = bases[name]
    img = Image.new("RGB", (W, H), base)
    if name.startswith("ply_raw"):
        grain = noise_layer(W, H, rng, 6, 90, 140).resize((W // 8, H)).resize((W, H))
        img = ImageChops.multiply(img, Image.merge("RGB", [grain.point(lambda v: v + 115)] * 3))
    d = ImageDraw.Draw(img)
    if name == "ply_raw":
        pass
    elif name == "ply_black_tag":
        fitted(d, (W * 0.08, H * 0.32, W * 0.92, H * 0.52), "SLOP", YELLOW, shadow=ORANGE)
        _spray(d, [(W * 0.15, H * 0.62), (W * 0.5, H * 0.58), (W * 0.85, H * 0.64)], YELLOW, W * 0.04, rng)
    elif name == "ply_red_crown":
        crown(d, W / 2, H * 0.36, W * 0.66, PAPER)
        fitted(d, (W * 0.1, H * 0.56, W * 0.9, H * 0.7), "PIT", PAPER)
    elif name == "ply_raw_ko":
        f = font(W * 0.6)
        text(d, (W / 2, H * 0.45), "KO", f, INK)
        _spray(d, [(W * 0.1, H * 0.7), (W * 0.9, H * 0.66)], RED, W * 0.05, rng)
    elif name == "ply_grey_pit":
        fitted(d, (W * 0.1, H * 0.2, W * 0.9, H * 0.34), "NO", INK, mono=True)
        fitted(d, (W * 0.1, H * 0.36, W * 0.9, H * 0.5), "REFUNDS", INK, mono=True)
        d.rectangle([W * 0.08, H * 0.18, W * 0.92, H * 0.52], outline=INK, width=4)
    else:
        for k in range(6):
            pts = [(rng.uniform(0, W), rng.uniform(H * 0.2, H * 0.8)) for _ in range(4)]
            _spray(d, pts, rng.choice([ORANGE, YELLOW, PAPER]), W * 0.03, rng, drips=k < 3)
    grunge(img, rng, 0.07, dark=(20, 18, 16), light=(200, 190, 170))
    return img.resize((144, 288), Image.LANCZOS)


def _hazard():
    W, H = 512 * SS, 64 * SS
    img = Image.new("RGB", (W, H), INK)
    d = ImageDraw.Draw(img)
    step = H * 1.0
    x = -H
    while x < W + H:
        d.polygon([(x, H), (x + step * 0.5, H), (x + step * 0.5 + H, 0), (x + H, 0)], fill=YELLOW)
        x += step
    grunge(img, random.Random(7), 0.05, dark=(40, 36, 30), light=(220, 200, 150))
    return img.resize((512, 64), Image.LANCZOS)


def _sign(w, h, words, color, box=False, bg=(20, 18, 16)):
    """Glowing sign: RGBA where alpha marks lit pixels (StageNeon glow mask)."""
    W, H = w * SS, h * SS
    rgb = Image.new("RGB", (W, H), bg if not box else (250, 240, 220))
    alpha = Image.new("L", (W, H), 255 if box else 0)
    d, a = ImageDraw.Draw(rgb), ImageDraw.Draw(alpha)
    m = H * 0.14
    f = fit_font(d, words, W - 2 * m, H - 2 * m)
    d.text((W / 2, H / 2), words, font=f, fill=color, anchor="mm")
    a.text((W / 2, H / 2), words, font=f, fill=255, anchor="mm")
    if box:
        d.rectangle([0, 0, W - 1, H - 1], outline=(40, 36, 30), width=int(H * 0.06))
    out = rgb.convert("RGBA")
    out.putalpha(alpha.filter(ImageFilter.GaussianBlur(1)))
    return out.resize((w, h), Image.LANCZOS)


def _odds(rng):
    W, H = 512 * SS, 384 * SS
    img = Image.new("RGB", (W, H), (30, 38, 32))
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, W - 1, H - 1], outline=(120, 86, 50), width=int(W * 0.03))
    chalk = (226, 228, 216)
    fitted(d, (W * 0.08, H * 0.06, W * 0.92, H * 0.22), "TONIGHT'S ODDS", YELLOW, mono=True)
    rows = [("FIGHTGUY", "2-1"), ("MANKI", "5-2"), ("WIBOU", "3-1"), ("BONK", "EVENS")]
    for k, (who, odds) in enumerate(rows):
        y0 = H * (0.28 + k * 0.15)
        fitted(d, (W * 0.08, y0, W * 0.58, y0 + H * 0.12), who, chalk, mono=True)
        fitted(d, (W * 0.64, y0, W * 0.92, y0 + H * 0.12), odds, chalk, mono=True)
    fitted(d, (W * 0.08, H * 0.88, W * 0.92, H * 0.96), "CASH ONLY - NO REFUNDS", (200, 120, 110), mono=True)
    grunge(img, rng, 0.05, dark=(20, 26, 22), light=(160, 168, 150))
    return img.resize((512, 384), Image.LANCZOS)


def _chalk_rules(rng):
    W, H = 512 * SS, 384 * SS
    img = Image.new("RGB", (W, H), (34, 30, 28))
    d = ImageDraw.Draw(img)
    fitted(d, (W * 0.08, H * 0.05, W * 0.92, H * 0.2), "HOUSE RULES", YELLOW)
    lines = ["1. NO WEAPONS*", "2. NO BITING", "3. LOSER MOPS", "4. GET IN THE SLOP", "*BONK EXEMPT"]
    for k, line in enumerate(lines):
        y0 = H * (0.26 + k * 0.14)
        fitted(d, (W * 0.08, y0, W * 0.92, y0 + H * 0.11), line, (230, 226, 214), mono=True)
    grunge(img, rng, 0.05, dark=(18, 16, 14), light=(150, 140, 130))
    return img.resize((512, 384), Image.LANCZOS)


def _crown_cell():
    W = 192 * SS
    rgb = Image.new("RGB", (W, W), (0, 0, 0))
    alpha = Image.new("L", (W, W), 0)
    crown(ImageDraw.Draw(rgb), W / 2, W / 2, W * 0.8, YELLOW)
    crown(ImageDraw.Draw(alpha), W / 2, W / 2, W * 0.8, 255)
    out = rgb.convert("RGBA")
    out.putalpha(alpha)
    return out.resize((192, 192), Image.LANCZOS)


def atlas(path):
    rng = random.Random(20261016)
    img = Image.new("RGBA", (SIZE, SIZE), (128, 128, 128, 255))

    def put(name, cell):
        x, y, w, h = CELLS[name]
        if cell.size != (w, h):
            raise ValueError("%s is %s, cell %s" % (name, cell.size, (w, h)))
        img.paste(cell.convert("RGBA") if cell.mode != "RGBA" else cell, (x, y))

    for n in POSTERS:
        put("poster_" + n, _poster(n, rng))
    for n in BANNERS:
        put("banner_" + n, _banner(n, rng))
    for n in HOARDING:
        put("hoard_" + n, _hoarding(n, rng))
    for n in SKIRTS:
        put("skirt_" + n, _skirt(n, rng))
    put("hazard", _hazard())
    put("stencil_refunds", _sign(512, 96, "NO REFUNDS", PAPER, bg=(30, 28, 26)))
    put("stencil_fighters", _sign(512, 64, "FIGHTERS ONLY", PAPER, bg=(30, 28, 26)))
    put("exit", _sign(192, 72, "EXIT", (255, 60, 40)))
    put("bets", _sign(256, 96, "BETS", YELLOW))
    put("tickets", _sign(256, 96, "TICKETS", (255, 150, 60)))
    put("fighters_box", _sign(512, 128, "FIGHTERS", RED, box=True))
    put("slop_cola", _sign(512, 128, "SLOP COLA", RED, box=True))
    put("bonk_burger", _sign(512, 128, "BONK BURGER", (200, 90, 20), box=True))
    put("odds", _odds(rng))
    put("chalk_rules", _chalk_rules(rng))
    put("crown", _crown_cell())
    put("white", Image.new("RGBA", (64, 64), (255, 255, 255, 255)))
    img.save(path)
    return path


# ---------------------------------------------------------------- canvas and floor

def canvas(path, half=8.0):
    """Ring top: worn canvas, apron border, coloured corners and the centre logo."""
    rng = random.Random(4242)
    W = SIZE
    px = W / (2 * half)  # pixels per metre
    base = Image.new("RGB", (W, W), (172, 165, 150))
    weave = noise_layer(W, W, rng, 3, 120, 136)
    base = ImageChops.multiply(base, Image.merge("RGB", [weave.point(lambda v: v + 120)] * 3))
    stains = noise_layer(W, W, rng, 160, 0, 255).point(lambda v: int(255 - max(0, v - 175) * 0.45))
    base = ImageChops.multiply(base, Image.merge("RGB", [stains] * 3))
    d = ImageDraw.Draw(base)
    border = 0.45 * px
    corner = 2.3 * px
    colors = {"sw": RED, "ne": BLUE, "nw": (206, 200, 186), "se": (206, 200, 186)}
    b = border
    tri = {"nw": [(b, b), (b + corner, b), (b, b + corner)],
           "ne": [(W - b, b), (W - b - corner, b), (W - b, b + corner)],
           "sw": [(b, W - b), (b + corner, W - b), (b, W - b - corner)],
           "se": [(W - b, W - b), (W - b - corner, W - b), (W - b, W - b - corner)]}
    for k, pts in tri.items():
        d.polygon(pts, fill=colors[k], outline=INK)
        d.line(pts + [pts[0]], fill=INK, width=int(0.05 * px))
    # centre logo: a faded print of the crown and the stage name
    logo = Image.new("L", (W, W), 0)
    ld = ImageDraw.Draw(logo)
    ld.ellipse([W / 2 - 4.6 * px, W / 2 - 4.6 * px, W / 2 + 4.6 * px, W / 2 + 4.6 * px], outline=255, width=int(0.16 * px))
    ld.ellipse([W / 2 - 4.2 * px, W / 2 - 4.2 * px, W / 2 + 4.2 * px, W / 2 + 4.2 * px], outline=255, width=int(0.05 * px))
    crown(ld, W / 2, W / 2 - 1.75 * px, 3.0 * px, 255)
    fitted(ld, (W / 2 - 3.9 * px, W / 2 - 0.55 * px, W / 2 + 3.9 * px, W / 2 + 1.35 * px), "SLOP PIT", 255)
    fitted(ld, (W / 2 - 2.6 * px, W / 2 + 1.75 * px, W / 2 + 2.6 * px, W / 2 + 2.35 * px), "UNDERGROUND", 255, mono=True)
    wear = noise_layer(W, W, rng, 24, 120, 255)
    logo = ImageChops.multiply(logo, wear)
    ink = Image.new("RGB", (W, W), (48, 44, 42))
    base = Image.composite(ink, base, logo.point(lambda v: int(v * 0.78)))
    yellow_mask = Image.new("L", (W, W), 0)
    crown(ImageDraw.Draw(yellow_mask), W / 2, W / 2 - 1.75 * px, 2.55 * px, 255)
    base = Image.composite(Image.new("RGB", (W, W), (186, 138, 44)), base, ImageChops.multiply(yellow_mask, wear).point(lambda v: int(v * 0.7)))
    d = ImageDraw.Draw(base)
    # slop splats and scuffs
    for _ in range(12):
        x, y = rng.uniform(border, W - border), rng.uniform(border, W - border)
        r = rng.uniform(0.08, 0.3) * px
        col = rng.choice([(112, 108, 72), (96, 86, 62), (122, 112, 92)])
        for _k in range(rng.randint(3, 8)):
            ox, oy = rng.gauss(0, r), rng.gauss(0, r)
            rr = r * rng.uniform(0.2, 0.6)
            d.ellipse([x + ox - rr, y + oy - rr, x + ox + rr, y + oy + rr], fill=col)
    for _ in range(60):
        x, y = rng.uniform(border, W - border), rng.uniform(border, W - border)
        a = rng.uniform(0, math.pi)
        L = rng.uniform(0.2, 0.9) * px
        d.line([(x, y), (x + math.cos(a) * L, y + math.sin(a) * L)], fill=(150, 142, 128), width=int(0.03 * px))
    base = base.filter(ImageFilter.GaussianBlur(0.6))
    d = ImageDraw.Draw(base)
    # apron border: black vinyl with a yellow inner trim
    d.rectangle([0, 0, W - 1, border], fill=(30, 28, 27))
    d.rectangle([0, W - border, W - 1, W - 1], fill=(30, 28, 27))
    d.rectangle([0, 0, border, W - 1], fill=(30, 28, 27))
    d.rectangle([W - border, 0, W - 1, W - 1], fill=(30, 28, 27))
    t = 0.06 * px
    d.rectangle([border - t, border - t, W - border + t, W - border + t], outline=YELLOW, width=int(t))
    f = font(0.22 * px)
    for k in range(4):
        side = Image.new("RGBA", (W, int(border)), (0, 0, 0, 0))
        sd = ImageDraw.Draw(side)
        for j in range(5):
            sd.text((W * (0.1 + j * 0.2), border / 2), "SLOP PIT", font=f, fill=(170, 140, 60, 255), anchor="mm")
        side = side.rotate(90 * k, expand=True)
        pos = [(0, 0), (0, 0), (0, int(W - border)), (int(W - border), 0)][k]
        if k == 1:
            pos = (0, 0)
        elif k == 3:
            pos = (int(W - border), 0)
        base.paste(side, pos, side)
    grunge(base, rng, 0.015, dark=(110, 104, 92), rmax=2.5)
    # Headroom: Training's arena adds three directional lights on top of the stage's own,
    # so the print stays mid-toned instead of clipping to white there.
    base = base.point(lambda v: int(v * 0.66))
    base.save(path)
    return path


def floor(path, outline, ring_half=8.0, half=16.0):
    """Ringside floor: stained concrete, joints, a hazard band on the edge, the ringside line."""
    rng = random.Random(1717)
    W = SIZE
    px = W / (2 * half)

    def P(x, z):
        return ((x + half) * px, (half - z) * px)

    base = Image.new("RGB", (W, W), (92, 86, 79))
    tone = noise_layer(W, W, rng, 64, 150, 255)
    fine = noise_layer(W, W, rng, 2, 200, 255)
    base = ImageChops.multiply(base, Image.merge("RGB", [tone.point(lambda v: v + 40)] * 3))
    base = ImageChops.multiply(base, Image.merge("RGB", [fine.point(lambda v: v + 20)] * 3))
    d = ImageDraw.Draw(base)
    # oil stains
    for _ in range(22):
        x, z = rng.uniform(-15, 15), rng.uniform(-15, 15)
        r = rng.uniform(0.3, 1.3)
        cx, cy = P(x, z)
        d.ellipse([cx - r * px, cy - r * px * 0.7, cx + r * px, cy + r * px * 0.7], fill=(74, 69, 63))
    base = base.filter(ImageFilter.GaussianBlur(3))
    d = ImageDraw.Draw(base)
    # saw-cut joints every 4 m and a few cracks
    for k in range(-16, 17, 4):
        d.line([P(k, -16), P(k, 16)], fill=(58, 54, 50), width=3)
        d.line([P(-16, k), P(16, k)], fill=(58, 54, 50), width=3)
    for _ in range(14):
        x, z = rng.uniform(-15, 15), rng.uniform(-15, 15)
        pts = [P(x, z)]
        for _k in range(6):
            x += rng.uniform(-0.6, 0.6)
            z += rng.uniform(-0.6, 0.6)
            pts.append(P(x, z))
        d.line(pts, fill=(50, 46, 42), width=2)
    # ringside line, 1 m outside the ring, and a stencil in each band
    r = ring_half + 1.0
    d.rectangle([P(-r, r), P(r, -r)], outline=(220, 175, 50), width=int(0.12 * px))
    f = font(0.42 * px, mono=True)
    for k, (x, z, rot) in enumerate(((0, -11.6, 0), (11.6, 0, 90), (0, 11.6, 180), (-11.6, 0, 270))):
        label = Image.new("RGBA", (int(9 * px), int(0.7 * px)), (0, 0, 0, 0))
        ImageDraw.Draw(label).text((label.size[0] / 2, label.size[1] / 2), "STAY BEHIND THE LINE" if k % 2 == 0 else "FIGHTERS ONLY",
                                   font=f, fill=(205, 200, 185, 150), anchor="mm")
        label = label.rotate(rot, expand=True)
        cx, cy = P(x, z)
        base.paste(label, (int(cx - label.size[0] / 2), int(cy - label.size[1] / 2)), label)
    # cable runs taped to the floor
    for start in ((-8.5, -3.0), (8.5, 5.0)):
        x, z = start
        pts = [P(x, z)]
        sign = -1 if x < 0 else 1
        while abs(x) < 15.2:
            x += sign * rng.uniform(0.8, 1.4)
            z += rng.uniform(-0.5, 0.5)
            pts.append(P(x, z))
        d.line(pts, fill=(20, 20, 20), width=int(0.05 * px))
        for p in pts[1::2]:
            d.rectangle([p[0] - 0.14 * px, p[1] - 0.06 * px, p[0] + 0.14 * px, p[1] + 0.06 * px], fill=(70, 70, 72))
    # hazard band along the floor edge (0.5 m wide)
    band = Image.new("L", (W, W), 0)
    bd = ImageDraw.Draw(band)
    bd.polygon([P(x, z) for x, z in outline], fill=255)
    inner = [(x * (1 - 0.5 / 16.0), z * (1 - 0.5 / 16.0)) for x, z in outline]
    bd.polygon([P(x, z) for x, z in inner], fill=0)
    stripes = Image.new("RGB", (W, W), (24, 23, 22))
    sd = ImageDraw.Draw(stripes)
    step = 0.7 * px
    k = -W
    while k < 2 * W:
        sd.polygon([(k, 0), (k + step / 2, 0), (k + step / 2 + W, W), (k + W, W)], fill=(230, 180, 40))
        k += step
    base = Image.composite(stripes, base, band)
    grunge(base, rng, 0.02, dark=(56, 52, 47), rmax=3.0)
    base.save(path)
    return path


def detail(path):
    rng = random.Random(99)
    W = 512
    g = noise_layer(W, W, rng, 3, 100, 156)
    g2 = noise_layer(W, W, rng, 24, 110, 146)
    out = ImageChops.add(g, g2, scale=2.0, offset=0)
    out = out.point(lambda v: int(min(255, max(0, v))))
    # make it tile: blend with a half-offset copy
    shifted = ImageChops.offset(out, W // 2, W // 2)
    mask = Image.radial_gradient("L").resize((W, W))
    out = Image.composite(out, shifted, mask)
    out.save(path)
    return path
