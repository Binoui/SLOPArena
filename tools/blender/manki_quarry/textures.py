"""Kaboom Quarry textures (numpy + Pillow). Everything is drawn here; lettering uses the
project's Archivo Black, Baloo 2 and Space Mono (OFL, client/Unity/Assets/Fonts) and the
billboard reuses Manki's own portrait (Resources/UI/Portraits/Manki.png).

ground  (4096 x 4096, RGB): every walkable top, planar over GROUND (north up). Concrete
        dock and haul road, steel pontoons, blasted granite benches, corrugated roofs,
        gravel, timber rafts and rock islands, plus Manki Demolition's paint: hazard
        stripes on edges that drop into the pit, route arrows, the jetpack pad, the
        detonator box, crate spots on the islands, scorch marks and graffiti.
atlas   (4096 x 4096, RGBA; alpha = StageNeon glow): signs, the billboard, tags, TNT and
        dynamite labels, hazard tape, drum bands, corrugated metal, planks, doors, the
        white block that geo.WHITE_UV samples (alpha 0) and the lamp block glowing faces
        sample (LAMP_UV, alpha 255).
strata  (1024 x 1024, RGB, tiling): layered blasted rock, tinted by vertex colour.
detail  (512 x 512, gray): tileable grain for StageVertexLit's triplanar detail.
ripples (512 x 512, RGB): water normals for SLOPArena/StageWater.
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
PORTRAIT = REPO / "client/Unity/Assets/Resources/UI/Portraits/Manki.png"

# The planar ground: world rectangle (x0, z0, x1, z1) and its pixels per metre.
GROUND = (-58.0, -56.0, 40.0, 42.0)
GROUND_SIZE = 4096
SIZE = 4096


def rgb(c, a=255):
    return tuple(int(round(min(1.0, max(0.0, v)) * 255)) for v in c[:3]) + (a,)


def font(path, size):
    return ImageFont.truetype(path, int(size))


# ------------------------------------------------------------------ atlas layout

# name -> (x, y, w, h) in atlas pixels (y down). UVs flip v (see uv()).
CELLS = {
    "billboard": (0, 0, 2048, 1024),        # MANKI DEMOLITION CO. with the portrait, 2:1
    "quarry_sign": (2048, 0, 1920, 512),    # KABOOM QUARRY - EST. YESTERDAY, 3.75:1
    "days": (2048, 512, 1024, 512),         # DAYS WITHOUT KABOOM: 0
    "safety": (3072, 512, 1024, 512),       # SAFETY THIRD
    "radius": (0, 1024, 1024, 512),         # BLAST RADIUS: YES
    "smoking": (1024, 1024, 1024, 512),     # NO SMOKING* *EXCEPT FUSES
    "wasnt": (2048, 1024, 1024, 512),       # IT WASN'T ME (graffiti on rock)
    "monkey": (3072, 1024, 512, 512),       # CAUTION: MONKEY AT WORK diamond
    "tag": (3584, 1024, 512, 512),          # Manki face tag on rock
    "bullseye": (0, 1536, 1024, 1024),      # the Blast Face target with JETPACK UP
    "kaboom": (1024, 1536, 1024, 1024),     # comic KA-BOOM! burst
    "tnt": (2048, 1536, 512, 512),          # TNT crate side
    "door": (2560, 1536, 1024, 1024),       # crusher shed door with the logo
    "crusher": (3584, 1536, 512, 256),      # BIG CRUSHER plate
    "dynamite": (3584, 1792, 512, 256),     # dynamite stick label band
    "hazard": (0, 2560, 1024, 256),         # hazard tape
    "drum": (1024, 2560, 1024, 256),        # oil drum band
    "corrugated": (2048, 2560, 512, 512),   # corrugated sheet (vertical ribs)
    "planks": (2560, 2560, 512, 512),       # timber planks
    "window": (3072, 2560, 512, 512),       # shed window, dusty
    "duck": (3584, 2560, 512, 512),         # the duck's eyepatch face
    "water_tower": (0, 2816, 2048, 512),    # TNT band for the dynamite water tower
    "grin": (2048, 3072, 1024, 512),        # Mount Manki's grin and teeth
    "eye": (3072, 3072, 512, 512),          # googly eye
    "wink": (3584, 3072, 512, 512),         # winking eye with the mask
    "lamp": (0, 3584, 256, 256),            # solid white, alpha 255: StageNeon faces that glow sample LAMP_UV
    "white": (3968, 0, 128, 128),           # top-right: contains geo.WHITE_UV (0.985, 0.985)
}


def uv(name, inset=2):
    """((u0, v0), (u1, v1)) of an atlas cell, inset a few pixels against bleeding."""
    x, y, w, h = CELLS[name]
    return ((x + inset) / SIZE, 1 - (y + h - inset) / SIZE), ((x + w - inset) / SIZE, 1 - (y + inset) / SIZE)


LAMP_UV = ((CELLS["lamp"][0] + CELLS["lamp"][2] / 2) / SIZE, 1 - (CELLS["lamp"][1] + CELLS["lamp"][3] / 2) / SIZE)


def sub_uv(name, fu0, fv0, fu1, fv1, inset=2):
    """A fractional window of a cell (fractions measured from the cell's bottom-left)."""
    (a0, b0), (a1, b1) = uv(name, inset)
    return (a0 + (a1 - a0) * fu0, b0 + (b1 - b0) * fv0), (a0 + (a1 - a0) * fu1, b0 + (b1 - b0) * fv1)


# ------------------------------------------------------------------ drawing helpers

def outlined(d, xy, text, f, fill, outline, width, anchor="mm"):
    d.text(xy, text, font=f, fill=fill, anchor=anchor, stroke_width=width, stroke_fill=outline)


def fit_font(path, text, max_w, max_h, start=400):
    size = start
    while size > 8:
        f = font(path, size)
        x0, y0, x1, y1 = f.getbbox(text)
        if x1 - x0 <= max_w and y1 - y0 <= max_h:
            return f
        size = int(size * 0.92)
    return font(path, 8)


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


def spray_text(img, xy, text, size, color, rng, angle=0.0, drips=6, outline=None, path=DISPLAY):
    """Graffiti lettering: slightly rotated, optional outline, paint drips under it."""
    f = font(path, size)
    x0, y0, x1, y1 = f.getbbox(text)
    w, h = x1 - x0 + size, y1 - y0 + size * 2
    layer = Image.new("RGBA", (int(w), int(h)), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    cx, cy = w / 2, size * 0.9
    if outline:
        d.text((cx, cy), text, font=f, fill=outline, anchor="mm", stroke_width=max(3, size // 10), stroke_fill=outline)
    d.text((cx, cy), text, font=f, fill=color, anchor="mm")
    for _ in range(drips):
        x = rng.uniform(cx - (x1 - x0) / 2, cx + (x1 - x0) / 2)
        L = rng.uniform(size * 0.2, size * 0.9)
        wd = rng.uniform(size * 0.04, size * 0.08)
        d.rounded_rectangle((x - wd / 2, cy, x + wd / 2, cy + (y1 - y0) / 2 + L), radius=wd / 2, fill=color)
    layer = layer.rotate(angle, resample=Image.BICUBIC, expand=True)
    img.alpha_composite(layer, (int(xy[0] - layer.width / 2), int(xy[1] - layer.height / 2)))


def manki_face(d, cx, cy, r, ink=True):
    """Manki's face as a tag: orange head, horns, red mask, a googly eye, a wink, a grin and the tongue."""
    o = rgb(P.MANKI_ORANGE)
    k = rgb(P.INK)
    # horns and ears behind the head
    for s in (-1, 1):
        d.polygon([(cx + s * r * 0.55, cy - r * 0.7), (cx + s * r * 1.05, cy - r * 1.25), (cx + s * r * 0.95, cy - r * 0.55)], fill=rgb(P.HORN))
        d.ellipse((cx + s * r * 0.95 - r * 0.25, cy - r * 0.15, cx + s * r * 0.95 + r * 0.25, cy + r * 0.35), fill=rgb(P.SKIN), outline=k, width=max(2, int(r * 0.05)))
    # spiky fur outline
    pts = []
    for i in range(36):
        a = i / 36 * math.tau
        rr = r * (1.0 + (0.12 if i % 2 == 0 else 0.0))
        pts.append((cx + math.cos(a) * rr, cy + math.sin(a) * rr * 0.95))
    d.polygon(pts, fill=o, outline=k)
    # face plate
    d.ellipse((cx - r * 0.72, cy - r * 0.55, cx + r * 0.72, cy + r * 0.78), fill=rgb(P.SKIN), outline=k, width=max(2, int(r * 0.04)))
    # red mask band
    d.rounded_rectangle((cx - r * 0.78, cy - r * 0.42, cx + r * 0.78, cy + r * 0.05), radius=r * 0.2, fill=rgb(P.MASK_RED), outline=k, width=max(2, int(r * 0.04)))
    # googly eye (viewer's left) and the wink (viewer's right)
    d.ellipse((cx - r * 0.62, cy - r * 0.5, cx - r * 0.12, cy + r * 0.02), fill=rgb(P.EYE_WHITE), outline=k, width=max(2, int(r * 0.05)))
    d.ellipse((cx - r * 0.42, cy - r * 0.32, cx - r * 0.22, cy - r * 0.12), fill=k)
    d.arc((cx + r * 0.12, cy - r * 0.35, cx + r * 0.62, cy + r * 0.05), 200, 340, fill=k, width=max(3, int(r * 0.09)))
    # grin with teeth
    d.chord((cx - r * 0.55, cy + r * 0.05, cx + r * 0.55, cy + r * 0.72), 0, 180, fill=rgb(P.TOOTH), outline=k, width=max(2, int(r * 0.05)))
    for i in range(1, 7):
        x = cx - r * 0.55 + r * 1.1 * i / 7
        d.line((x, cy + r * 0.38, x, cy + r * 0.62), fill=k, width=max(1, int(r * 0.025)))
    # tongue out
    d.ellipse((cx + r * 0.0, cy + r * 0.5, cx + r * 0.32, cy + r * 1.05), fill=rgb(P.TONGUE), outline=k, width=max(2, int(r * 0.04)))


def hazard_stripes(d, box, period, a=P.HAZARD, b=P.INK, slant=1.0):
    x0, y0, x1, y1 = box
    d.rectangle(box, fill=rgb(a))
    h = y1 - y0
    s = x0 - h * slant
    while s < x1:
        d.polygon([(s, y1), (s + period / 2, y1), (s + period / 2 + h * slant, y0), (s + h * slant, y0)], fill=rgb(b))
        s += period


def rock_patch(w, h, seed, base=P.OCHRE, dark=P.OCHRE_DARK):
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float64)
    m = fbm(xs / 90.0, ys / 90.0, seed, octaves=5)
    bands = 0.5 + 0.5 * np.sin(ys / 23.0 + 3.0 * fbm(xs / 160.0, ys / 160.0, seed + 1, 3))
    t = np.clip(0.55 * m + 0.35 * bands, 0, 1)[..., None]
    col = np.array(dark, np.float32) * (1 - t) + np.array(base, np.float32) * t
    img = Image.fromarray(np.clip(col * 255, 0, 255).astype(np.uint8), "RGB").convert("RGBA")
    return img


def board(w, h, bg, border=None, bw=10, radius=18):
    im = Image.new("RGBA", (w, h), rgb(bg))
    d = ImageDraw.Draw(im)
    if border:
        d.rounded_rectangle((bw // 2, bw // 2, w - bw // 2, h - bw // 2), radius=radius, outline=rgb(border), width=bw)
    return im, d


def grime(img, rng, amount=0.14, seed=1):
    a = np.asarray(img).astype(np.float32)
    h, w = a.shape[:2]
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float64)
    n = fbm(xs / 60.0, ys / 60.0, seed, octaves=4)
    streak = fbm(xs / 8.0, ys / 160.0, seed + 3, 3)
    k = 1 - amount * np.clip(n * 1.3 - 0.3, 0, 1) - amount * 0.6 * np.clip(streak - 0.55, 0, 1) * 2
    a[..., :3] *= k[..., None]
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA")


# ------------------------------------------------------------------ atlas cells

def _billboard(rng):
    w, h = CELLS["billboard"][2:]
    im, d = board(w, h, P.HAZARD, P.INK, bw=24, radius=10)
    hazard_stripes(d, (0, h - 90, w, h), 120)
    hazard_stripes(d, (0, 0, w, 60), 120)
    port = Image.open(PORTRAIT).convert("RGBA").resize((760, 760), Image.LANCZOS)
    mask = Image.new("L", port.size, 0)
    ImageDraw.Draw(mask).ellipse((0, 0, 760, 760), fill=255)
    d.ellipse((50, 92, 850, 892), fill=rgb(P.INK))
    im.paste(port, (70, 112), mask)
    outlined(d, (1440, 260), "MANKI", font(DISPLAY, 230), rgb(P.MASK_RED), rgb(P.INK), 12)
    outlined(d, (1440, 470), "DEMOLITION CO.", font(DISPLAY, 120), rgb(P.INK), rgb(P.HAZARD), 2)
    d.rounded_rectangle((900, 580, 1990, 860), radius=30, fill=rgb(P.INK))
    d.text((1445, 660), "WE BLOW IT UP.", font=font(ROUND, 110), fill=rgb(P.HAZARD), anchor="mm")
    d.text((1445, 785), "YOU STOW IT. *", font=font(ROUND, 110), fill=rgb(P.EYE_WHITE), anchor="mm")
    d.text((1960, 912), "*NO REFUNDS", font=font(MONO, 44), fill=rgb(P.INK), anchor="rm")
    return grime(im, rng, 0.10, 5)


def _quarry_sign(rng):
    w, h = CELLS["quarry_sign"][2:]
    im, d = board(w, h, P.INK, P.HAZARD, bw=20)
    outlined(d, (w * 0.47, h * 0.42), "KABOOM QUARRY", fit_font(DISPLAY, "KABOOM QUARRY", w * 0.82, h * 0.6), rgb(P.MANKI_ORANGE), rgb(P.MASK_DARK), 10)
    d.text((w * 0.47, h * 0.82), "EST. YESTERDAY  -  CLOSED TOMORROW", font=font(MONO, 64), fill=rgb(P.HAZARD), anchor="mm")
    manki_face(d, w * 0.93, h * 0.48, h * 0.32)
    return grime(im, rng, 0.12, 6)


def _days(rng):
    w, h = CELLS["days"][2:]
    im, d = board(w, h, (0.10, 0.36, 0.20), P.EYE_WHITE, bw=14)
    d.text((w / 2, 92), "DAYS WITHOUT", font=font(DISPLAY, 82), fill=rgb(P.EYE_WHITE), anchor="mm")
    d.text((w / 2, 196), "A KABOOM", font=font(DISPLAY, 82), fill=rgb(P.EYE_WHITE), anchor="mm")
    d.rounded_rectangle((w / 2 - 120, 260, w / 2 + 120, 470), radius=16, fill=rgb(P.EYE_WHITE))
    d.text((w / 2, 368), "0", font=font(DISPLAY, 200), fill=rgb(P.MASK_RED), anchor="mm")
    spray_text(im, (w * 0.82, h * 0.78), "LOL", 90, rgb(P.MANKI_ORANGE), rng, angle=18, drips=4)
    return grime(im, rng, 0.12, 7)


def _safety(rng):
    w, h = CELLS["safety"][2:]
    im, d = board(w, h, P.EYE_WHITE, P.INK, bw=16)
    d.rectangle((0, 0, w, 150), fill=rgb(P.MASK_RED))
    d.text((w / 2, 78), "SAFETY", font=font(DISPLAY, 110), fill=rgb(P.EYE_WHITE), anchor="mm")
    d.text((w / 2, 300), "FIRST", font=font(DISPLAY, 150), fill=rgb(P.INK), anchor="mm")
    # FIRST crossed out, THIRD sprayed over it
    d.line((150, 300, w - 150, 290), fill=rgb(P.MASK_RED), width=24)
    spray_text(im, (w * 0.55, h * 0.82), "THIRD", 120, rgb(P.MANKI_ORANGE), rng, angle=-8, drips=5, outline=rgb(P.INK))
    return grime(im, rng, 0.10, 8)


def _radius(rng):
    w, h = CELLS["radius"][2:]
    im, d = board(w, h, P.HAZARD, P.INK, bw=16)
    d.text((w / 2, 140), "BLAST RADIUS:", font=font(DISPLAY, 112), fill=rgb(P.INK), anchor="mm")
    d.text((w / 2, 340), "YES", font=font(DISPLAY, 210), fill=rgb(P.MASK_RED), anchor="mm")
    return grime(im, rng, 0.12, 9)


def _smoking(rng):
    w, h = CELLS["smoking"][2:]
    im, d = board(w, h, P.EYE_WHITE, P.MASK_RED, bw=20)
    cx, cy, r = 150, h / 2, 115
    d.ellipse((cx - r, cy - r, cx + r, cy + r), outline=rgb(P.MASK_RED), width=28)
    d.rounded_rectangle((cx - 80, cy - 14, cx + 70, cy + 14), radius=6, fill=rgb(P.INK))
    d.line((cx - r * 0.7, cy + r * 0.7, cx + r * 0.7, cy - r * 0.7), fill=rgb(P.MASK_RED), width=28)
    d.text((620, 190), "NO SMOKING*", font=font(DISPLAY, 96), fill=rgb(P.INK), anchor="mm")
    d.text((620, 330), "*EXCEPT FUSES", font=font(ROUND, 88), fill=rgb(P.MASK_RED), anchor="mm")
    return grime(im, rng, 0.10, 10)


def _wasnt(rng):
    w, h = CELLS["wasnt"][2:]
    im = rock_patch(w, h, 31)
    spray_text(im, (w * 0.48, h * 0.42), "IT WASN'T ME", 120, rgb(P.EYE_WHITE), rng, angle=6, drips=8, outline=rgb(P.INK))
    d = ImageDraw.Draw(im)
    splatter(d, w * 0.86, h * 0.75, 26, rgb(P.MANKI_ORANGE), rng, drips=3, drip_len=60)
    return im


def _monkey(rng):
    w, h = CELLS["monkey"][2:]
    im = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.polygon([(w / 2, 6), (w - 6, h / 2), (w / 2, h - 6), (6, h / 2)], fill=rgb(P.INK))
    d.polygon([(w / 2, 26), (w - 26, h / 2), (w / 2, h - 26), (26, h / 2)], fill=rgb(P.HAZARD))
    manki_face(d, w / 2, h * 0.44, 78)
    d.text((w / 2, h * 0.73), "MONKEY", font=font(DISPLAY, 46), fill=rgb(P.INK), anchor="mm")
    d.text((w / 2, h * 0.81), "AT WORK", font=font(DISPLAY, 34), fill=rgb(P.INK), anchor="mm")
    bg = Image.new("RGBA", (w, h), rgb(P.STEEL_DARK))
    bg.alpha_composite(im)
    return bg


def _tag(rng):
    w, h = CELLS["tag"][2:]
    im = rock_patch(w, h, 37)
    d = ImageDraw.Draw(im)
    manki_face(d, w / 2, h * 0.46, 150)
    spray_text(im, (w / 2, h * 0.88), "MANKI WAS HERE", 46, rgb(P.EYE_WHITE), rng, angle=-4, drips=3)
    return im


def _bullseye(rng):
    w, h = CELLS["bullseye"][2:]
    im = rock_patch(w, h, 41, base=P.GRANITE, dark=P.GRANITE_DARK)
    d = ImageDraw.Draw(im)
    cx, cy = w / 2, h * 0.58
    for i, r in enumerate((300, 240, 180, 120, 60)):
        d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=rgb(P.MASK_RED if i % 2 == 0 else P.EYE_WHITE))
    for s in (-1, 1):
        d.line((cx + s * 330, cy, cx + s * 380, cy), fill=rgb(P.INK), width=12)
    spray_text(im, (w / 2, h * 0.12), "JETPACK", 150, rgb(P.HAZARD), rng, angle=-3, drips=6, outline=rgb(P.INK))
    # three chevrons pointing up
    for k in range(3):
        y = h * 0.33 - k * 40
        d.line((cx - 90, y + 40, cx, y - 20, cx + 90, y + 40), fill=rgb(P.HAZARD), width=22, joint="curve")
    splatter(d, w * 0.18, h * 0.86, 30, rgb(P.MANKI_ORANGE), rng, drips=4, drip_len=70)
    return im


def _kaboom(rng):
    w, h = CELLS["kaboom"][2:]
    im = rock_patch(w, h, 43)
    d = ImageDraw.Draw(im)
    cx, cy, R = w / 2, h / 2, w * 0.46
    star = []
    for k in range(28):
        r = R if k % 2 == 0 else R * 0.6
        a = k / 28 * math.tau - math.pi / 2
        star.append((cx + math.cos(a) * r, cy + math.sin(a) * r))
    d.polygon(star, fill=rgb(P.HAZARD), outline=rgb(P.INK), width=12)
    inner = [(cx + (x - cx) * 0.78, cy + (y - cy) * 0.78) for x, y in star]
    d.polygon(inner, fill=rgb(P.MANKI_ORANGE))
    outlined(d, (cx, cy - 50), "KA-", font(DISPLAY, 190), rgb(P.EYE_WHITE), rgb(P.INK), 12)
    outlined(d, (cx, cy + 140), "BOOM!", font(DISPLAY, 210), rgb(P.EYE_WHITE), rgb(P.INK), 12)
    return im


def _tnt(rng):
    w, h = CELLS["tnt"][2:]
    im = _planks_image(w, h, rng, vertical=False)
    d = ImageDraw.Draw(im)
    d.rectangle((0, 0, w - 1, h - 1), outline=rgb(P.WOOD_DARK), width=22)
    d.line((0, 0, w, h), fill=rgb(P.WOOD_DARK), width=26)
    outlined(d, (w / 2, h / 2), "TNT", font(DISPLAY, 150), rgb(P.DYNAMITE), rgb(P.INK), 6)
    return grime(im, rng, 0.14, 12)


def _door(rng):
    w, h = CELLS["door"][2:]
    im = _corrugated_image(w, h, rng, (0.72, 0.42, 0.16))
    d = ImageDraw.Draw(im)
    d.rectangle((0, 0, w, 70), fill=rgb(P.STEEL_DARK))
    manki_face(d, w / 2, h * 0.45, 190)
    spray_text(im, (w / 2, h * 0.86), "KEEP OUT (OR DON'T)", 64, rgb(P.EYE_WHITE), rng, angle=-3, drips=5, outline=rgb(P.INK))
    return grime(im, rng, 0.16, 13)


def _crusher(rng):
    w, h = CELLS["crusher"][2:]
    im, d = board(w, h, P.MASK_RED, P.EYE_WHITE, bw=10)
    d.text((w / 2, h * 0.38), "BIG CRUSHER", font=font(DISPLAY, 70), fill=rgb(P.EYE_WHITE), anchor="mm")
    d.text((w / 2, h * 0.76), "(DO NOT HUG)", font=font(ROUND, 56), fill=rgb(P.HAZARD), anchor="mm")
    return im


def _dynamite(rng):
    w, h = CELLS["dynamite"][2:]
    im, d = board(w, h, P.DYNAMITE, None)
    d.rectangle((0, h * 0.3, w, h * 0.7), fill=rgb(P.LABEL))
    d.text((w / 2, h / 2), "DANGER  TNT  DANGER", font=font(DISPLAY, 46), fill=rgb(P.INK), anchor="mm")
    return im


def _hazard(rng):
    w, h = CELLS["hazard"][2:]
    im = Image.new("RGBA", (w, h))
    hazard_stripes(ImageDraw.Draw(im), (0, 0, w, h), 128)
    return grime(im, rng, 0.10, 14)


def _drum(rng):
    w, h = CELLS["drum"][2:]
    im = Image.new("RGBA", (w, h), (255, 255, 255, 255))
    d = ImageDraw.Draw(im)
    for y in (h * 0.22, h * 0.78):
        d.rectangle((0, y - 10, w, y + 10), fill=(205, 205, 205, 255))
    d.text((w * 0.25, h / 2), "OIL", font=font(DISPLAY, 70), fill=(170, 170, 170, 255), anchor="mm")
    d.text((w * 0.75, h / 2), "PROBABLY", font=font(ROUND, 60), fill=(170, 170, 170, 255), anchor="mm")
    return grime(im, rng, 0.18, 15)


def _corrugated_image(w, h, rng, color):
    xs = np.arange(w, dtype=np.float32)
    ribs = 0.80 + 0.20 * np.sin(xs / w * math.tau * 12)[None, :]
    base = np.ones((h, w, 3), np.float32) * np.array(color, np.float32)
    ys, xx = np.mgrid[0:h, 0:w].astype(np.float64)
    rust = np.clip(fbm(xx / 50.0, ys / 120.0, 77, 4) * 1.6 - 0.6, 0, 1)[..., None]
    col = base * ribs[..., None] * (1 - 0.4 * rust) + np.array(P.RUST, np.float32) * 0.5 * rust
    return Image.fromarray(np.clip(col * 255, 0, 255).astype(np.uint8), "RGB").convert("RGBA")


def _planks_image(w, h, rng, vertical=True):
    im = Image.new("RGBA", (w, h), rgb(P.WOOD))
    d = ImageDraw.Draw(im)
    n = 6
    for i in range(n):
        tone = rng.uniform(0.85, 1.12)
        c = rgb(tuple(min(1, v * tone) for v in P.WOOD))
        if vertical:
            x0 = i * w / n
            d.rectangle((x0, 0, x0 + w / n - 6, h), fill=c)
        else:
            y0 = i * h / n
            d.rectangle((0, y0, w, y0 + h / n - 6), fill=c)
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float64)
    a = np.asarray(im).astype(np.float32)
    grain = fbm(xs / (6.0 if vertical else 90.0), ys / (90.0 if vertical else 6.0), 5, 3)
    a[..., :3] *= (0.85 + 0.25 * grain)[..., None]
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA")


def _window(rng):
    w, h = CELLS["window"][2:]
    im, d = board(w, h, P.STEEL_DARK, None)
    for i in range(2):
        for j in range(2):
            x0, y0 = 30 + i * (w - 60) / 2, 30 + j * (h - 60) / 2
            d.rectangle((x0 + 8, y0 + 8, x0 + (w - 60) / 2 - 8, y0 + (h - 60) / 2 - 8), fill=(120, 140, 140, 255))
    d.line((40, 60, w - 60, h - 40), fill=(220, 225, 220, 255), width=6)
    d.line((w - 60, 40, 60, h - 80), fill=(220, 225, 220, 255), width=4)
    return grime(im, rng, 0.25, 16)


def _duck(rng):
    w, h = CELLS["duck"][2:]
    im, d = board(w, h, P.DUCK, None)
    d.ellipse((w * 0.18, h * 0.3, w * 0.38, h * 0.5), fill=rgb(P.INK))
    d.ellipse((w * 0.6, h * 0.28, w * 0.84, h * 0.52), fill=rgb(P.INK))           # eyepatch
    d.line((w * 0.0, h * 0.2, w * 1.0, h * 0.42), fill=rgb(P.INK), width=14)       # strap
    return im


def _water_tower(rng):
    w, h = CELLS["water_tower"][2:]
    im, d = board(w, h, P.DYNAMITE, None)
    d.rectangle((0, h * 0.3, w, h * 0.7), fill=rgb(P.LABEL))
    for k, x in enumerate(np.linspace(w * 0.12, w * 0.88, 3)):
        d.text((x, h / 2), ("TNT", "KABOOM", "TNT")[k], font=font(DISPLAY, 120), fill=rgb(P.INK), anchor="mm")
    return grime(im, rng, 0.12, 17)


def _grin(rng):
    w, h = CELLS["grin"][2:]
    im, d = board(w, h, P.INK, None)
    teeth = 9
    for i in range(teeth):
        x0 = 30 + i * (w - 60) / teeth
        d.rounded_rectangle((x0 + 6, 30, x0 + (w - 60) / teeth - 6, h * 0.62), radius=26, fill=rgb(P.TOOTH))
    d.rectangle((0, h * 0.62, w, h), fill=rgb(P.MASK_DARK))
    return grime(im, rng, 0.12, 18)


def _eye(rng):
    w, h = CELLS["eye"][2:]
    im, d = board(w, h, P.EYE_WHITE, None)
    d.ellipse((150, 120, 360, 330), fill=rgb(P.INK))
    d.ellipse((190, 150, 240, 200), fill=rgb(P.EYE_WHITE))
    for _ in range(12):
        a = rng.uniform(0, math.tau)
        d.line((w / 2 + math.cos(a) * 220, h / 2 + math.sin(a) * 220, w / 2 + math.cos(a) * 250, h / 2 + math.sin(a) * 250),
               fill=rgb(P.MASK_RED), width=5)
    return im


def _wink(rng):
    w, h = CELLS["wink"][2:]
    im, d = board(w, h, P.MASK_RED, None)
    d.arc((60, 160, w - 60, h - 40), 200, 340, fill=rgb(P.INK), width=40)
    for k in range(4):
        x = 120 + k * 90
        d.line((x, 200, x - 20, 140), fill=rgb(P.INK), width=16)
    return im


def atlas(path):
    rng = random.Random(1018)
    im = Image.new("RGBA", (SIZE, SIZE), (128, 128, 128, 0))
    cells = {
        "billboard": _billboard, "quarry_sign": _quarry_sign, "days": _days, "safety": _safety, "radius": _radius,
        "smoking": _smoking, "wasnt": _wasnt, "monkey": _monkey, "tag": _tag, "bullseye": _bullseye, "kaboom": _kaboom,
        "tnt": _tnt, "door": _door, "crusher": _crusher, "dynamite": _dynamite, "hazard": _hazard, "drum": _drum,
        "window": _window, "duck": _duck, "water_tower": _water_tower, "grin": _grin, "eye": _eye, "wink": _wink,
    }
    for name, fn in cells.items():
        x, y, w, h = CELLS[name]
        cell = fn(rng).convert("RGBA").resize((w, h))
        cell.putalpha(0)  # lit cells: no glow
        im.paste(cell, (x, y))
    for name, img in (("corrugated", _corrugated_image(*CELLS["corrugated"][2:], rng, P.ROOF)),
                      ("planks", _planks_image(*CELLS["planks"][2:], rng))):
        x, y, w, h = CELLS[name]
        img = img.convert("RGBA")
        img.putalpha(0)
        im.paste(img, (x, y))
    # Glowing faces (StageNeon: glow = vertex alpha x atlas alpha) sample this block. The shader
    # is opaque, so glow shapes are geometry, never sprites with clear corners.
    x, y, w, h = CELLS["lamp"]
    im.paste(Image.new("RGBA", (w, h), (255, 255, 255, 255)), (x, y))
    x, y, w, h = CELLS["white"]
    im.paste(Image.new("RGBA", (w, h), (255, 255, 255, 0)), (x, y))
    im.save(path, optimize=True)
    return path


# ------------------------------------------------------------------ tiling rock and detail

def strata(path, size=1024, seed=23):
    """Layered blasted rock around light neutral, tiling on both axes (vertex colour tints it)."""
    c = np.arange(size) / size
    X, Y = np.meshgrid(c, c)
    warp = fbm(X * 4, Y * 4, seed, 4, period=4)
    bands = 0.5 + 0.5 * np.sin((Y * 9 + warp * 0.9) * math.tau)
    fine = fbm(X * 24, Y * 48, seed + 1, 3, period=(24, 48))
    blotch = fbm(X * 6, Y * 6, seed + 2, 4, period=6)
    v = 0.72 + 0.08 * bands + 0.12 * (fine - 0.5) + 0.14 * (blotch - 0.5)
    # Drill-hole rows: dark dots on a vertical lattice.
    holes = np.zeros_like(v)
    for k in range(6):
        cx = (k + 0.5) / 6
        d = np.hypot((X - cx) * size, ((Y * 8) % 1 - 0.5) * size / 8)
        holes = np.maximum(holes, np.clip(1 - d / 7.0, 0, 1))
    v *= 1 - 0.3 * holes
    # Cracks: thin dark lines from thresholded noise.
    crack = np.abs(fbm(X * 10, Y * 10, seed + 3, 4, period=10) - 0.5)
    v *= np.where(crack < 0.012, 0.75, 1.0)
    img = np.clip(np.stack([v * 1.02, v, v * 0.96], -1) * 255, 0, 255).astype(np.uint8)
    Image.fromarray(img, "RGB").save(path, optimize=True)
    return path


def detail(path, size=512, seed=17):
    """Tileable rock grain around mid grey (the shader multiplies albedo by detail * 2)."""
    c = np.arange(size) / size
    X, Z = np.meshgrid(c, c)
    g = (0.55 * fbm(X * 8, Z * 8, seed, 4, period=8) + 0.3 * fbm(X * 32, Z * 32, seed + 1, 3, period=32)
         + 0.15 * fbm(X * 96, Z * 96, seed + 2, 2, period=96))
    g = 0.5 + (g - g.mean()) * 0.9
    Image.fromarray((np.clip(g, 0, 1) * 255).astype(np.uint8), "L").save(path)
    return path


def ripples(path, size=512, seed=61):
    """Tileable water normals (RGB = xyz * 0.5 + 0.5, linear) from wind-stretched noise."""
    c = np.arange(size) / size
    X, Z = np.meshgrid(c, c)
    h = (0.55 * fbm(X * 6, Z * 10, seed, 4, period=(6, 10))
         + 0.3 * fbm(X * 16, Z * 22, seed + 3, 3, period=(16, 22))
         + 0.15 * fbm(X * 40, Z * 40, seed + 5, 2, period=40))
    dx = (np.roll(h, -1, axis=1) - np.roll(h, 1, axis=1)) * size * 0.5
    dz = (np.roll(h, -1, axis=0) - np.roll(h, 1, axis=0)) * size * 0.5
    strength = 0.03
    nx, nz = -dx * strength, -dz * strength
    ny = np.ones_like(nx)
    n = np.sqrt(nx * nx + ny * ny + nz * nz)
    rgb_ = np.stack([nx / n, nz / n, ny / n], axis=-1) * 0.5 + 0.5  # tangent-style: R=x, G=z, B=up
    Image.fromarray((np.clip(rgb_, 0, 1) * 255).astype(np.uint8), "RGB").save(path)
    return path


# ------------------------------------------------------------------ the ground

def ground_uv(x, z):
    x0, z0, x1, z1 = GROUND
    return ((x - x0) / (x1 - x0), (z - z0) / (z1 - z0))


class Ground:
    """Paints the planar ground. World metres in, pixels out (north up)."""

    def __init__(self, data):
        self.data = data
        self.S = GROUND_SIZE / (GROUND[2] - GROUND[0])
        self.W = self.H = GROUND_SIZE
        self.rng = random.Random(4242)

    def px(self, x, z):
        return ((x - GROUND[0]) * self.S, (GROUND[3] - z) * self.S)

    def rect_px(self, x0, z0, x1, z1):
        a, b = self.px(x0, z1)
        c, d = self.px(x1, z0)
        return (a, b, c, d)

    def grid(self, q=1):
        ys, xs = np.mgrid[0:self.H // q, 0:self.W // q].astype(np.float32) * q
        X = GROUND[0] + xs / self.S
        Z = GROUND[3] - ys / self.S
        return X, Z


KIND = {
    "LoadingDock": "concrete", "HaulRoad": "concrete", "WestLanding": "concrete", "NorthBench": "granite",
    "CliffBase": "granite", "UpperBench": "granite", "Bench1": "granite", "Bench2": "granite", "Bench3": "granite",
    "Pontoon1": "steel", "Pontoon2": "steel", "Pontoon3": "steel", "Pontoon4": "steel",
    "CrusherRoof": "roof", "ConveyorGantry": "grate", "Hopper": "gravel", "DumpTruck": "truck", "GravelPile": "gravel",
    "LowRaft": "wood", "TwinRaft": "wood", "FarBuoy": "buoy", "LastPillar": "rock",
}
# Walkable tops sit a little darker than the side palette: the sun lights them square-on and
# white concrete clipped a quarter of every capture (pass 03).
BASE = {
    "concrete": (0.50, 0.50, 0.51), "granite": (0.50, 0.49, 0.47), "steel": P.STEEL, "roof": P.ROOF, "grate": P.STEEL_DARK,
    "gravel": P.GRAVEL, "truck": P.HAZARD, "wood": P.WOOD, "buoy": P.MASK_RED, "rock": P.ROCK,
}


def kind(name):
    return KIND.get(name, "rock")


def drop_edges(pieces, route, name):
    """Edge segments of a route piece's top that end in a drop: [(axis, fixed, a, b)], axis 'x' runs along x."""
    x0, z0, x1, z1, top = pieces[name]
    others = [p for n, p in pieces.items() if n != name and n in route and abs(p[4] - top) < 0.01]

    def subtract(interval, cuts):
        parts = [interval]
        for c0, c1 in cuts:
            nxt = []
            for a, b in parts:
                if c1 <= a or c0 >= b:
                    nxt.append((a, b))
                    continue
                if c0 > a:
                    nxt.append((a, c0))
                if c1 < b:
                    nxt.append((c1, b))
            parts = nxt
        return [(a, b) for a, b in parts if b - a > 0.05]

    out = []
    for zf in (z0, z1):
        cuts = [(max(p[0], x0), min(p[2], x1)) for p in others if (abs(p[1] - zf) < 0.01 or abs(p[3] - zf) < 0.01) and p[0] < x1 and p[2] > x0]
        out += [("x", zf, a, b) for a, b in subtract((x0, x1), cuts)]
    for xf in (x0, x1):
        cuts = [(max(p[1], z0), min(p[3], z1)) for p in others if (abs(p[0] - xf) < 0.01 or abs(p[2] - xf) < 0.01) and p[1] < z1 and p[3] > z0]
        out += [("z", xf, a, b) for a, b in subtract((z0, z1), cuts)]
    return out


def ground(path, data):
    g = Ground(data)
    pieces, route = data["pieces"], set(data["route"])
    X, Z = g.grid(4)
    seed = 99
    big = fbm(X * 0.08, Z * 0.08, seed, 4).astype(np.float32)
    mid = fbm(X * 0.6, Z * 0.6, seed + 1, 3).astype(np.float32)
    fine = fbm(X * 3.0, Z * 3.0, seed + 2, 2).astype(np.float32)
    col = np.ones(X.shape + (3,), np.float32) * np.array(P.CONCRETE_DARK, np.float32)
    for name, (x0, z0, x1, z1, top) in pieces.items():
        k = kind(name)
        inside = (X >= x0) & (X <= x1) & (Z >= z0) & (Z <= z1)
        base = np.array(BASE[k], np.float32)
        tone = 0.9 + 0.16 * big + 0.08 * (mid - 0.5)
        if k == "concrete":
            # 4 m slabs, joints and a little staining
            jx = np.abs(((X - x0) % 4.0) - 2.0) > 1.97
            jz = np.abs(((Z - z0) % 4.0) - 2.0) > 1.97
            shade = np.where(jx | jz, 0.72, 1.0) * tone
        elif k == "granite":
            blocks = 0.92 + 0.12 * fbm(np.floor(X / 1.6) * 3.1, np.floor(Z / 1.2) * 2.7, seed + 9, 1)
            shade = tone * blocks * (0.9 + 0.2 * fine)
        elif k == "steel":
            diamond = ((np.floor(X / 0.18) + np.floor(Z / 0.18)) % 2) * 0.06
            shade = (0.9 + diamond) * tone
        elif k in ("roof",):
            ribs = 0.84 + 0.16 * np.sin((X - x0) * math.tau / 0.45)
            shade = ribs * tone
        elif k == "grate":
            g1 = (np.abs(((X - x0) % 0.3) - 0.15) < 0.03) | (np.abs(((Z - z0) % 0.3) - 0.15) < 0.03)
            shade = np.where(g1, 1.25, 0.65) * tone
        elif k == "gravel":
            shade = (0.75 + 0.45 * fine) * tone
        elif k == "truck":
            # Dump bed ribs; the cab roof at the south end (pieces.TRUCK_CAB) is Manki orange.
            cab = Z < z0 + 1.4
            ribs = np.where(cab, 1.0, 0.86 + 0.14 * np.sin((Z - z0) * math.tau / 0.6))
            base = np.where(cab[..., None], np.array(P.MANKI_ORANGE, np.float32), np.array(P.HAZARD, np.float32))
            shade = ribs * tone
        elif k == "wood":
            planks = np.where(np.abs(((X - x0) % 0.5) - 0.25) > 0.235, 0.6, 1.0)
            shade = planks * (0.88 + 0.22 * fbm(X * 0.6, Z * 9.0, seed + 5, 2)) * tone
        elif k == "buoy":
            stripes = np.where(((X - x0) % 1.2) < 0.6, 1.0, 0.0)
            base = np.where(stripes[..., None] > 0.5, np.array(P.MASK_RED, np.float32), np.array(P.EYE_WHITE, np.float32))
            shade = tone
        else:
            shade = (0.82 + 0.3 * mid + 0.12 * (fine - 0.5)) * tone
        layer = (base * shade[..., None]) if np.ndim(base) == 1 else base * shade[..., None]
        col = np.where(inside[..., None], layer, col)
    img = Image.fromarray(np.clip(col * 255, 0, 255).astype(np.uint8), "RGB").resize((g.W, g.H), Image.BILINEAR)
    img = img.convert("RGBA")
    paint = Image.new("RGBA", (g.W, g.H), (0, 0, 0, 0))
    d = ImageDraw.Draw(paint)
    _paint(g, d, paint, data)
    # Paint wear: blotchy loss.
    pa = np.asarray(paint).astype(np.float32)
    Xf, Zf = g.grid(4)
    wear = fbm(Xf * 0.9, Zf * 0.9, 321, 4).astype(np.float32)
    wear = np.asarray(Image.fromarray((wear * 255).astype(np.uint8)).resize((g.W, g.H), Image.BILINEAR)).astype(np.float32) / 255
    pa[..., 3] *= np.clip(1.25 - 0.9 * wear, 0.35, 1.0)
    img.alpha_composite(Image.fromarray(pa.astype(np.uint8), "RGBA"))
    _scorch(g, img, data)
    img.convert("RGB").save(path, optimize=True)
    return path


def _stripe_band(g, img, x0, z0, x1, z1, along_x):
    """Hazard stripes clipped to a world rectangle; stripes run across the band's long axis."""
    a, b = g.px(x0, z1)
    c, e = g.px(x1, z0)
    w, h = max(1, int(round(c - a))), max(1, int(round(e - b)))
    long_, short = (w, h) if along_x else (h, w)
    band = Image.new("RGBA", (long_, short))
    hazard_stripes(ImageDraw.Draw(band), (0, 0, long_, short), 0.9 * g.S)
    if not along_x:
        band = band.rotate(90, expand=True)
    img.alpha_composite(band, (int(round(a)), int(round(b))))


def _text(g, img, text, x, z, height, color, angle=0.0, path=DISPLAY, outline=None):
    """Lettering lying on the ground; `angle` (degrees, counter-clockwise) turns its up from north."""
    px_h = height * g.S
    f = font(path, px_h * 1.35)
    x0, y0, x1, y1 = f.getbbox(text)
    w, h = int(x1 - x0 + px_h), int(y1 - y0 + px_h)
    layer = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    ld = ImageDraw.Draw(layer)
    ld.text((w / 2, h / 2), text, font=f, fill=color, anchor="mm",
            stroke_width=int(px_h * 0.08) if outline else 0, stroke_fill=outline)
    layer = layer.rotate(angle, resample=Image.BICUBIC, expand=True)
    cx, cy = g.px(x, z)
    img.alpha_composite(layer, (int(cx - layer.width / 2), int(cy - layer.height / 2)))


def _arrow(g, d, x, z, length, width, angle, color):
    """A painted arrow pointing along `angle` (degrees from +x, counter-clockwise, world)."""
    a = math.radians(angle)
    ux, uz = math.cos(a), math.sin(a)
    vx, vz = -uz, ux
    shaft = length * 0.6
    pts = [(x - ux * length / 2 + vx * width / 4, z - uz * length / 2 + vz * width / 4),
           (x - ux * length / 2 + ux * shaft + vx * width / 4, z - uz * length / 2 + uz * shaft + vz * width / 4),
           (x - ux * length / 2 + ux * shaft + vx * width / 2, z - uz * length / 2 + uz * shaft + vz * width / 2),
           (x + ux * length / 2, z + uz * length / 2),
           (x - ux * length / 2 + ux * shaft - vx * width / 2, z - uz * length / 2 + uz * shaft - vz * width / 2),
           (x - ux * length / 2 + ux * shaft - vx * width / 4, z - uz * length / 2 + uz * shaft - vz * width / 4),
           (x - ux * length / 2 - vx * width / 4, z - uz * length / 2 - vz * width / 4)]
    d.polygon([g.px(px_, pz_) for px_, pz_ in pts], fill=color)


PAINT_WHITE = (0.84, 0.84, 0.80)  # ground lettering and arrows: off-white so the sun does not clip it


def _paint(g, d, img, data):
    pieces, route = data["pieces"], set(data["route"])
    spec = data["spec"]
    rng = g.rng
    hz = rgb(P.HAZARD)
    ink = rgb(P.INK)
    # Hazard stripes along every route edge that drops into the pit.
    for name in route:
        for axis, fixed, a, b in drop_edges(pieces, data["route"], name):
            x0, z0, x1, z1, top = pieces[name]
            band = 0.34
            if axis == "x":
                inward = -1 if abs(fixed - z1) < 0.01 else 1
                za, zb = sorted((fixed, fixed + inward * band))
                _stripe_band(g, img, a, za, b, zb, along_x=True)
            else:
                inward = -1 if abs(fixed - x1) < 0.01 else 1
                xa, xb = sorted((fixed, fixed + inward * band))
                _stripe_band(g, img, xa, a, xb, b, along_x=False)
    # Tyre tracks along the haul road and onto the dock.
    for zoff in (-34.9, -33.1):
        for k in range(3):
            z = zoff + (k - 1) * 0.12
            d.line([g.px(-37.5, z + 0.1 * math.sin(k)), g.px(-12, z), g.px(8, z - 0.3)], fill=(30, 30, 32, 70), width=int(0.16 * g.S))
    # Start box, the title and route arrows.
    sx, sz, _ = spec["start"]
    box = g.rect_px(sx - 1.6, sz - 1.6, sx + 1.6, sz + 1.6)
    d.rectangle(box, outline=hz, width=int(0.18 * g.S))
    _text(g, img, "START", sx, sz - 2.3, 0.55, rgb(P.HAZARD))
    _text(g, img, "KABOOM QUARRY", 1.5, -38.6, 0.95, rgb(P.MANKI_ORANGE), outline=ink)
    _text(g, img, "MANKI DEMOLITION CO.", 1.5, -29.6, 0.5, rgb(PAINT_WHITE), path=MONO)
    for x in (-15.0, -23.0, -31.0):
        _arrow(g, d, x, -34.0, 2.6, 1.3, 180, rgb(PAINT_WHITE, 200))
    _text(g, img, "BLAST RANGE", -26.0, -32.4, 0.55, rgb(PAINT_WHITE), angle=0, path=MONO)
    _arrow(g, d, -42.0, -32.5, 2.4, 1.2, 90, rgb(PAINT_WHITE, 200))
    # Pontoon numbers, read from the south.
    for i, n in enumerate(("Pontoon1", "Pontoon2", "Pontoon3", "Pontoon4")):
        x0, z0, x1, z1, top = pieces[n]
        _text(g, img, str(i + 1), (x0 + x1) / 2, (z0 + z1) / 2, 1.3, rgb(P.HAZARD), outline=ink)
    # North Bench to the Blast Face.
    for x in (-38.0, -30.0, -22.0):
        _arrow(g, d, x, 7.0, 2.4, 1.2, 0, rgb(PAINT_WHITE, 200))
    _text(g, img, "UP THE BENCHES", -21.5, 11.6, 0.42, rgb(PAINT_WHITE), angle=90, path=MONO)
    # The jetpack pad: scorch, flame ring and the stencil.
    px_, pz_ = -10.0, 18.3
    for r, c in ((1.9, (25, 20, 18, 230)), (1.45, rgb(P.FLAME, 240)), (1.12, (20, 16, 14, 255))):
        a, b = g.px(px_ - r, pz_ + r)
        c2, d2 = g.px(px_ + r, pz_ - r)
        d.ellipse((a, b, c2, d2), fill=c)
    for k in range(12):
        ang = k / 12 * math.tau
        x1_, z1_ = px_ + math.cos(ang) * 1.5, pz_ + math.sin(ang) * 1.5
        x2_, z2_ = px_ + math.cos(ang + 0.13) * 2.35, pz_ + math.sin(ang + 0.13) * 2.35
        x3_, z3_ = px_ + math.cos(ang + 0.26) * 1.5, pz_ + math.sin(ang + 0.26) * 1.5
        d.polygon([g.px(x1_, z1_), g.px(x2_, z2_), g.px(x3_, z3_)], fill=rgb(P.FLAME, 230))
    _text(g, img, "JUMP + E", px_, pz_ - 2.9, 0.5, rgb(P.HAZARD), path=DISPLAY)
    # Upper Bench: the way down.
    _arrow(g, d, -1.0, 29.0, 2.2, 1.1, 0, rgb(PAINT_WHITE, 200))
    _text(g, img, "NO RAILINGS. GOOD LUCK.", -7.0, 22.0, 0.42, rgb(PAINT_WHITE), path=MONO)
    # Crate spots: a red cross on every target island.
    for tid, island, tx, tz in spec["targets"]:
        r = 0.75
        for s in (-1, 1):
            d.line([g.px(tx - r, tz - s * r), g.px(tx + r, tz + s * r)], fill=rgb(P.MASK_RED, 230), width=int(0.22 * g.S))
    # The finish: the detonator box with tape and PUSH.
    fx, fz = spec["finish"][1]
    hx, hz_ = spec["finish"][2][0], spec["finish"][2][2]
    _stripe_band(g, img, fx - hx, fz + hz_ - 0.4, fx + hx, fz + hz_, along_x=True)
    box = g.rect_px(fx - hx, fz - hz_, fx + hx, fz + hz_)
    d.rectangle(box, outline=hz, width=int(0.25 * g.S))
    _text(g, img, "FINISH", fx, fz - 0.4, 0.7, rgb(P.HAZARD), outline=ink)
    _text(g, img, "PUSH THE BIG RED THING", fx, fz - 1.6, 0.36, rgb(PAINT_WHITE), path=MONO)
    # A chalk outline with a stick of dynamite: some days go worse than others.
    _chalk_outline(g, d, 12.0, -30.4, 30)
    _text(g, img, "OOPS", 13.6, -29.3, 0.38, (235, 235, 230, 200), angle=20, path=ROUND)
    # Graffiti and junk.
    _text(g, img, "BOOM?", -6.0, 9.0, 0.6, rgb(P.MANKI_ORANGE), angle=-12, path=ROUND)
    _text(g, img, "BOOM.", -40.0, 9.8, 0.6, rgb(P.MANKI_ORANGE), angle=8, path=ROUND)
    for (x, z) in ((-19.0, -35.5), (-33.0, -33.0), (-43.0, -36.0), (-29.0, 4.5), (-12.0, 12.0), (5.0, -37.0)):
        r = rng.uniform(0.25, 0.5) * g.S
        cx, cy = g.px(x, z)
        splatter(d, cx, cy, r, rgb(rng.choice((P.MANKI_ORANGE, P.HAZARD, P.MASK_RED)), 220), rng)
    for (x, z, s) in ((-8.0, -36.4, 1.0), (-35.0, 8.4, 0.8)):
        cx, cy = g.px(x, z)
        manki_face(d, cx, cy, 0.8 * s * g.S)


def _chalk_outline(g, d, x, z, angle):
    """A cartoon body outline, arms up, in chalk; a dynamite stick by the hand."""
    c = (236, 236, 230, 210)
    w = int(0.07 * g.S)
    pts = [(0, 1.0), (0.18, 0.92), (0.22, 0.75), (0.55, 1.1), (0.65, 1.02), (0.28, 0.62), (0.3, 0.2), (0.5, -0.45),
           (0.36, -0.52), (0.12, -0.05), (-0.12, -0.05), (-0.36, -0.52), (-0.5, -0.45), (-0.3, 0.2), (-0.28, 0.62),
           (-0.65, 1.02), (-0.55, 1.1), (-0.22, 0.75), (-0.18, 0.92), (0, 1.0)]
    a = math.radians(angle)
    S = 1.25
    pts = [(x + (px_ * math.cos(a) - pz_ * math.sin(a)) * S, z + (px_ * math.sin(a) + pz_ * math.cos(a)) * S) for px_, pz_ in pts]
    d.line([g.px(*p) for p in pts], fill=c, width=w, joint="curve")
    hx, hz = x + 0.8, z + 1.45
    d.rectangle(g.rect_px(hx - 0.06, hz - 0.3, hx + 0.06, hz + 0.3), fill=rgb(P.DYNAMITE, 230))


def _scorch(g, img, data):
    """Soft blast marks: dark radial stains where Manki practised."""
    d = ImageDraw.Draw(img)
    rng = random.Random(77)
    spots = [(-24.0, -33.0, 1.6), (-44.0, -30.0, 1.2), (-26.0, 8.0, 1.4), (-14.0, 24.0, 1.8), (13.0, -2.0, 1.0),
             (3.0, -33.0, 1.3), (-36.0, 5.0, 1.0), (16.0, 10.0, 1.2)]
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ld = ImageDraw.Draw(layer)
    for x, z, r in spots:
        cx, cy = g.px(x, z)
        R = r * g.S
        ld.ellipse((cx - R, cy - R, cx + R, cy + R), fill=(18, 14, 12, 150))
        for _ in range(9):
            a = rng.uniform(0, math.tau)
            L = R * rng.uniform(1.2, 2.0)
            ld.line((cx, cy, cx + math.cos(a) * L, cy + math.sin(a) * L), fill=(18, 14, 12, 110), width=int(R * 0.18))
    layer = layer.filter(ImageFilter.GaussianBlur(radius=max(2, int(0.18 * g.S))))
    img.alpha_composite(layer)
