"""Original procedural textures for Splash Deck.

deck (2048 x 1280, RGB): the fight surface, planar-mapped over x in [DECK_X0, DECK_X1]
    and z in [DECK_Z0, DECK_Z1] (v grows north). Bleached teak planks run bow to stern
    with caulked seams and staggered butts, nibbed into a painted margin board that
    follows the layout outline. Weathering, salt, wet patches, flush rope coils, a
    flattened net pile, a few fish and soft contact shadows around both raised spots.
hull (8192 x 1024, RGB): the painted side strip. u = arc length from the stem round
    the CCW outline / perimeter; v = (y - HULL_BOTTOM) / -HULL_BOTTOM (v = 1 at the
    deck edge). Gunwale, yellow sheer strake, rub rail, cobalt planking, portholes,
    scuppers, the bow eyes, SPLASH DECK on both bows and the transom, boot-top and
    antifouling at the waterline.
atlas (2048 x 2048, RGBA; alpha = StageNeon glow mask): see REGIONS.
detail (512 x 512, grayscale, tileable): grain for StageVertexLit's triplanar detail.
ripples (512 x 512, RGB, tileable, linear): water normals for SLOPArena/StageWater.

Everything is drawn from code; text uses the shared stroke font.
"""
from __future__ import annotations

import math
import random

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

from stagekit import font
from stagekit.noise import fbm, mix as _mix, smooth as _smooth
import layout as LAYOUT
import palette as P

DECK_W, DECK_H = 2048, 1280
DECK_X0, DECK_X1 = -15.75, 20.75
DECK_Z0, DECK_Z1 = -11.4, 11.4
HULL_W, HULL_H = 8192, 1024
HULL_BOTTOM = -12.5
WATER_Y = -11.4
ATLAS = 2048
REGIONS = {
    "sign": (0, 0, 1024, 384),
    "window": (1024, 0, 256, 256),
    "wheel": (1280, 0, 256, 256),
    "roofnum": (1536, 0, 384, 192),
    "white": (1984, 0, 64, 64),
    "market": (0, 384, 1024, 256),
    "clock": (1024, 256, 256, 256),
    "lifering": (1280, 256, 256, 256),
    "door": (1536, 256, 256, 512),
    "hatchtop": (0, 640, 1024, 853),
    "porthole": (1024, 512, 256, 256),
    "shutter": (1280, 512, 128, 256),
    "window_house": (1408, 512, 128, 256),
}


def deck_uv(x, z):
    return ((x - DECK_X0) / (DECK_X1 - DECK_X0), (z - DECK_Z0) / (DECK_Z1 - DECK_Z0))


def region_uv(name, inset=2):
    """((u0, v0), (u1, v1)) of an atlas region with a small texel inset."""
    x0, y0, w, h = REGIONS[name]
    u0, u1 = (x0 + inset) / ATLAS, (x0 + w - inset) / ATLAS
    v1, v0 = 1 - (y0 + inset) / ATLAS, 1 - (y0 + h - inset) / ATLAS
    return (u0, v0), (u1, v1)


def _c(rgb):
    return np.array(rgb, dtype=np.float64)


def _hash1(i, seed):
    """Deterministic per-integer random in [0, 1) (arrays)."""
    h = (np.asarray(i, dtype=np.int64) * 2654435761 + seed * 97531) & 0xFFFFFFFF
    h = ((h ^ (h >> 15)) * 2246822519) & 0xFFFFFFFF
    return ((h ^ (h >> 13)) & 0xFFFF) / 65536.0


def _segment_distance(px, pz, a, b):
    ax, az = a
    bx, bz = b
    ex, ez = bx - ax, bz - az
    L2 = ex * ex + ez * ez
    t = np.clip(((px - ax) * ex + (pz - az) * ez) / L2, 0.0, 1.0)
    dx, dz = px - (ax + ex * t), pz - (az + ez * t)
    return np.sqrt(dx * dx + dz * dz)


def inside_distance(outline, X, Z):
    """Signed distance to a convex CCW outline: positive inside, negative outside."""
    d = np.full(X.shape, 1e9)
    inside = np.ones(X.shape, dtype=bool)
    n = len(outline)
    for i in range(n):
        a, b = outline[i], outline[(i + 1) % n]
        d = np.minimum(d, _segment_distance(X, Z, a, b))
        ex, ez = b[0] - a[0], b[1] - a[1]
        inside &= (X - a[0]) * ez - (Z - a[1]) * ex <= 0  # left of the CCW edge
    return np.where(inside, d, -d)


def _stroke_text(draw, text, height, center, width, color, tracking=1.05, wobble=None, align="center"):
    strokes, w = font.text_strokes(text, height, origin=center, tracking=tracking, align=align)
    for line in strokes:
        pts = [(x, y) for x, y in line]
        if wobble is not None:
            pts = [(x + wobble.uniform(-0.5, 0.5), y + wobble.uniform(-0.5, 0.5)) for x, y in pts]
        draw.line(pts, fill=color, width=max(1, int(width)), joint="curve")
        r = width / 2
        for x, y in (pts[0], pts[-1]):
            draw.ellipse((x - r, y - r, x + r, y + r), fill=color)
    return w


def _rgb255(c, a=255):
    return tuple(int(round(min(1.0, max(0.0, v)) * 255)) for v in c[:3]) + (a,)


# ---------------------------------------------------------------- deck

RAISED_PAD = 0.04  # shadow and wear start just outside each raised footprint


def deck(path, spec=LAYOUT.SPEC, seed=11):
    rng = random.Random(seed)
    W, H = DECK_W, DECK_H
    xs = DECK_X0 + (np.arange(W) + 0.5) / W * (DECK_X1 - DECK_X0)
    zs = DECK_Z1 - (np.arange(H) + 0.5) / H * (DECK_Z1 - DECK_Z0)  # row 0 is north (v = 1)
    X, Z = np.meshgrid(xs, zs)
    px = (DECK_X1 - DECK_X0) / W  # metres per pixel

    # Planks along X: a 0.30 m king plank on the centreline, 0.15 m strakes either side.
    az = np.abs(Z)
    king = az < 0.15
    strake = np.where(king, 0, np.floor((az - 0.15) / 0.15) + 1).astype(np.int64)
    strake_id = np.where(Z >= 0, strake, -strake)
    across = np.where(king, (Z + 0.15) / 0.30, ((az - 0.15) % 0.15) / 0.15)
    # Butt joints every 3.5-6.5 m, staggered by strake.
    length = 3.5 + 3.0 * _hash1(strake_id, seed)
    offset = _hash1(strake_id, seed + 1) * length
    run = (X + 40.0 + offset) / length
    piece = np.floor(run).astype(np.int64)
    along = (run - piece) * length
    pid = strake_id * 131 + piece
    tone = 0.94 + 0.12 * _hash1(pid, seed + 2)
    warm = _hash1(pid, seed + 3)
    base = _mix(_c(P.TEAK) * np.ones(X.shape + (3,)), _c(P.TEAK_LIGHT), warm * 0.55)
    base = _mix(base, _c(P.TEAK_DARK), (1 - warm) * 0.22 * _hash1(pid, seed + 4))
    grain = fbm(X * 0.9 + pid * 3.1, Z * 26.0, seed + 5, 4)
    streak = fbm(X * 0.25 + pid * 1.7, Z * 9.0, seed + 6, 3)
    col = base * (tone * (0.86 + 0.24 * grain) * (0.94 + 0.12 * streak))[..., None]

    # Caulked seams between strakes and at butts (about 1.6 px wide, anti-aliased).
    edge = np.minimum(across, 1 - across) * np.where(king, 0.30, 0.15)
    seam = 1 - _smooth(0.4 * px, 1.4 * px, edge)
    butt = 1 - _smooth(0.4 * px, 1.3 * px, np.minimum(along, length - along))
    col = _mix(col, _c(P.CAULK), np.maximum(seam, butt) * 0.85)
    # Fastening plugs across every plank at the deck beams (0.6 m).
    beam = np.abs(((X + 40.0) % 0.6) - 0.3)
    plug = (beam < 0.022) & (np.abs(across - 0.5) < 0.16)
    col[plug] = col[plug] * 0.9

    # Margin board along the authoritative outline: planks nib into it at the bow.
    outline = LAYOUT.outline(spec)
    d_in = inside_distance(outline, X, Z)
    wobble = 0.02 * (fbm(X * 3.0, Z * 3.0, seed + 7, 2) - 0.5)
    margin = 1 - _smooth(0.34 - px, 0.34 + px, d_in + wobble)
    paint = _c(P.MARGIN_PAINT) * (0.86 + 0.24 * fbm(X * 2.0, Z * 2.0, seed + 8, 3))[..., None]
    chip = _smooth(0.66, 0.74, fbm(X * 5.0, Z * 5.0, seed + 9, 3))
    paint = _mix(paint, col, chip * 0.8)
    col = _mix(col, paint, margin)
    rim_line = (1 - _smooth(0.4 * px, 1.4 * px, np.abs(d_in - 0.34)))
    col = _mix(col, _c(P.CAULK), rim_line * 0.8)
    # Scuppers and fairleads: small dark plates on the margin.
    perim = []
    for i in range(len(outline)):
        a, b = outline[i], outline[(i + 1) % len(outline)]
        perim.append((a, b))
    for x0 in (-11.0, -6.0, -1.0, 4.0):
        for zs_ in (1, -1):
            cx, cz = x0, zs_ * (LAYOUT.half_beam(x0, spec) - 0.17)
            m = (np.abs(X - cx) < 0.26) & (np.abs(Z - cz) < 0.11)
            col[m] = _c(P.STEEL_DARK) * 0.9

    # Bleaching, salt bloom near the edges and wear along the walkways.
    bleach = _smooth(0.5, 0.78, fbm(X * 0.22, Z * 0.22, seed + 10, 4))
    col = _mix(col, col * _c(P.TEAK_GREY) / _c(P.TEAK), bleach * 0.3)
    salt = _smooth(0.62, 0.8, fbm(X * 1.1, Z * 1.1, seed + 11, 4)) * _smooth(2.2, 0.5, d_in)
    col = _mix(col, _c(P.SALT), salt * 0.35)
    walk = np.exp(-(Z / 3.2) ** 2) * _smooth(-14.0, -6.0, X) * _smooth(18.0, 12.0, X)
    col *= (1 + 0.06 * walk * fbm(X * 0.5, Z * 0.5, seed + 12, 2))[..., None]

    # Wet patches with darker cores and paler dried rims.
    for (cx, cz, r) in ((14.8, -4.5, 1.3), (-14.4, 4.2, 1.0)):  # fish wash by the hatch, a puddle aft
        dist = np.hypot((X - cx) / 1.4, Z - cz) + 0.6 * (fbm(X * 1.4, Z * 1.4, seed + 13, 3) - 0.5)
        wet = _smooth(r, r * 0.6, dist)
        col = _mix(col, col * _c(P.DECK_WET) / _c(P.TEAK), wet * 0.32)
        ring = _smooth(r * 0.2, 0.0, np.abs(dist - r * 1.02))
        col = _mix(col, _c(P.SALT), ring * 0.1)

    # Contact shadows and scuffs around both raised spots.
    for r in spec["raised"].values():
        x0, z0, x1, z1 = r["footprint"]
        dx = np.maximum(np.maximum(x0 - X, X - x1), 0.0)
        dz = np.maximum(np.maximum(z0 - Z, Z - z1), 0.0)
        dd = np.hypot(dx, dz)
        shadow = np.exp(-dd / 0.28) * (dd > 0)
        col *= (1 - 0.42 * shadow)[..., None]
        scuff = _smooth(0.6, 0.72, fbm(X * 4.0, Z * 4.0, seed + 14, 3)) * np.exp(-dd / 1.4) * (dd > 0)
        col = _mix(col, col * 0.8, scuff * 0.5)

    img = Image.fromarray(np.clip(col * 255.0, 0, 255).astype(np.uint8), "RGB")
    _deck_decals(img, rng)
    img.save(path)
    return path


def _deck_px(x, z):
    u, v = deck_uv(x, z)
    return u * DECK_W, (1 - v) * DECK_H


def _rope_coil(draw, cx, cz, radius, turns, rng):
    """Flat spiral of twisted rope (deck metres)."""
    pts = []
    steps = int(turns * 64)
    for i in range(steps + 1):
        t = i / steps
        a = t * turns * 2 * math.pi + rng.uniform(-0.01, 0.01)
        r = radius * (0.22 + 0.78 * t)
        pts.append(_deck_px(cx + r * math.cos(a), cz + r * math.sin(a)))
    w = max(3, int(0.055 * DECK_W / (DECK_X1 - DECK_X0)))
    draw.line(pts, fill=_rgb255(P.ROPE_DARK), width=w + 2, joint="curve")
    draw.line(pts, fill=_rgb255(P.ROPE), width=w, joint="curve")
    for i in range(0, len(pts) - 1, 3):  # strand twist
        x, y = pts[i]
        draw.line((x - 1, y - 1, x + 1, y + 1), fill=_rgb255(P.ROPE_DARK), width=1)
    tail = [pts[-1], _deck_px(cx + radius * 1.6, cz + radius * 0.4), _deck_px(cx + radius * 2.4, cz - radius * 0.2)]
    draw.line(tail, fill=_rgb255(P.ROPE_DARK), width=w + 2, joint="curve")
    draw.line(tail, fill=_rgb255(P.ROPE), width=w, joint="curve")


def _fish(draw, cx, cz, length, angle, rng):
    ca, sa = math.cos(angle), math.sin(angle)

    def P2(u, v):
        return _deck_px(cx + u * ca - v * sa, cz + u * sa + v * ca)

    body = [P2(length * (t - 0.5), length * 0.13 * math.sin(math.pi * t) ** 0.8) for t in np.linspace(0, 1, 14)]
    body += [P2(length * (t - 0.5), -length * 0.13 * math.sin(math.pi * t) ** 0.8) for t in np.linspace(1, 0, 14)]
    draw.polygon(body, fill=_rgb255(P.FISH))
    back = [P2(length * (t - 0.5), length * 0.07 * math.sin(math.pi * t)) for t in np.linspace(0.05, 0.95, 10)]
    draw.line(back, fill=_rgb255(P.FISH_DARK), width=3)
    tail = [P2(-length * 0.5, 0), P2(-length * 0.68, length * 0.12), P2(-length * 0.66, -length * 0.12)]
    draw.polygon(tail, fill=_rgb255(P.FISH_DARK))
    ex, ey = P2(length * 0.36, length * 0.03)
    draw.ellipse((ex - 2, ey - 2, ex + 2, ey + 2), fill=(20, 20, 24, 255))


def _deck_decals(img, rng):
    draw = ImageDraw.Draw(img, "RGBA")
    # Rope coils near the bow, the stern quarters and beside the fish hatch.
    for cx, cz, r, turns in ((17.2, 1.2, 0.62, 5), (-14.4, 6.8, 0.55, 4), (-14.3, -6.9, 0.5, 4), (5.6, -8.4, 0.7, 6)):
        _rope_coil(draw, cx, cz, r, turns, rng)
    # A flattened net with orange floats on the starboard quarter: mostly mesh, little fill.
    cx, cz = -10.6, -7.6
    blob = []
    for k in range(48):
        a = 2 * math.pi * k / 48
        rr = 1.0 + 0.22 * math.sin(3 * a + 0.5) + 0.12 * math.sin(7 * a) + 0.06 * math.sin(13 * a)
        blob.append(_deck_px(cx + 1.35 * rr * math.cos(a), cz + 0.85 * rr * math.sin(a)))
    mask = Image.new("L", img.size, 0)
    ImageDraw.Draw(mask).polygon(blob, fill=255)
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    dl = ImageDraw.Draw(layer)
    dl.polygon(blob, fill=_rgb255(P.NET, 45))
    for k in range(-40, 41):
        for sign in (1, -1):
            a0 = _deck_px(cx - 1.8, cz + k * 0.06)
            a1 = _deck_px(cx + 1.8, cz + k * 0.06 + sign * 1.1)
            dl.line((a0, a1), fill=_rgb255(geo_scale(P.NET, 0.8), 200), width=2)
    img.paste(layer, (0, 0), Image.fromarray((np.asarray(layer)[..., 3] * (np.asarray(mask) / 255.0)).astype(np.uint8), "L"))
    draw = ImageDraw.Draw(img, "RGBA")
    # The float line snakes across the heap with its orange corks in a row.
    line = [_deck_px(cx - 1.15 + 2.3 * t, cz + 0.45 * math.sin(t * 5.0 + 0.6) * (1 - abs(t - 0.5))) for t in np.linspace(0, 1, 40)]
    draw.line(line, fill=_rgb255(P.ROPE_DARK), width=4, joint="curve")
    for i in range(3, 40, 5):
        x, y = line[i]
        draw.ellipse((x - 6, y - 5, x + 6, y + 5), fill=_rgb255(P.NET_FLOAT), outline=_rgb255(geo_scale(P.NET_FLOAT, 0.6)))
    # A few fish that missed the hold, beside the hatch.
    for cx, cz, L, a in ((6.6, 3.6, 0.46, 0.4), (6.9, 4.4, 0.38, 2.6), (14.6, -3.2, 0.5, -0.8), (11.0, 3.4, 0.42, 1.9)):
        _fish(draw, cx, cz, L, a, rng)
    # Scales glinting in the wet patch by the hatch.
    for _ in range(140):
        x, y = _deck_px(rng.uniform(5.8, 15.4), rng.uniform(-4.6, 4.6))
        draw.point((x, y), fill=(225, 232, 236, 160))


def geo_scale(color, k):
    return tuple(min(1.0, max(0.0, c * k)) for c in color[:3])


# ---------------------------------------------------------------- hull strip

def hull_perimeter(spec=LAYOUT.SPEC):
    """Arc-length table round the CCW outline from the stem: (points, cumulative s, total)."""
    pts = LAYOUT.outline(spec)
    s = [0.0]
    for i in range(len(pts)):
        a, b = pts[i], pts[(i + 1) % len(pts)]
        s.append(s[-1] + math.dist(a, b))
    return pts, s, s[-1]


SHEER = 0.62      # how far the painted strakes dip at midships (m)
BOOT_RISE = 0.5   # how far the boot-top rises at the bow (m)


def sheer_dy(x):
    t = np.clip((np.asarray(x, dtype=np.float64) - 2.5) / 18.0, -1.0, 1.0)
    return -SHEER * (1.0 - t * t)


def boot_dy(x):
    return BOOT_RISE * _smooth(4.0, 20.0, np.asarray(x, dtype=np.float64))


def _x_along(pts, s_tab, S):
    """Hull x at arc length S (vectorised, closed loop)."""
    xs = [p[0] for p in pts] + [pts[0][0]]
    return np.interp(S, s_tab, xs)


def hull_u(s, total):
    return s / total


def hull_v(y):
    return (y - HULL_BOTTOM) / -HULL_BOTTOM


def _s_at_x(pts, s, x_target, side):
    """Arc length where the hull side (port +1 / starboard -1) reaches x_target."""
    n = len(pts)
    for i in range(n):
        a, b = pts[i], pts[(i + 1) % n]
        if (a[1] >= 0 if side > 0 else a[1] <= 0) and (b[1] >= 0 if side > 0 else b[1] <= 0):
            if (a[0] - x_target) * (b[0] - x_target) <= 0 and a[0] != b[0]:
                t = (x_target - a[0]) / (b[0] - a[0])
                return s[i] + t * (s[i + 1] - s[i])
    raise ValueError("no hull point at x=%.2f" % x_target)


HULL_BANDS = [  # (top y, bottom y, colour) from the deck edge down
    (0.0, -0.30, P.GUNWALE), (-0.30, -0.36, P.SHEER_RED), (-0.36, -1.10, P.SHEER_YELLOW),
    (-1.10, -1.18, P.SHEER_RED), (-1.18, -1.45, P.RUB_RAIL), (-1.45, -1.62, P.HULL_STRIPE),
    (-1.62, -10.95, P.HULL_BLUE), (-10.95, -11.22, P.BOOT_TOP), (-11.22, HULL_BOTTOM - 1, P.ANTIFOUL),
]


def hull(path, spec=LAYOUT.SPEC, seed=23):
    rng = random.Random(seed)
    pts, s_tab, total = hull_perimeter(spec)
    W, H = HULL_W, HULL_H
    S = (np.arange(W) + 0.5) / W * total
    Y = -((np.arange(H) + 0.5) / H) * -HULL_BOTTOM  # row 0 is the deck edge
    SS, YY = np.meshgrid(S, Y)
    XX = _x_along(pts, s_tab, SS)
    # Painted sheer: the strakes dip towards midships and the boot-top rises at the bow,
    # so the straight deck edge still reads as a curved boat. YB is the "band" height.
    YB = YY - sheer_dy(XX) * _smooth(-8.5, -3.5, YY) - boot_dy(XX) * _smooth(-7.0, -9.5, YY)
    col = np.zeros(SS.shape + (3,))
    for top, bottom, c in HULL_BANDS:
        band = (YB <= top) & (YB > bottom)
        col[band] = c
    col[YY > -0.30] = P.GUNWALE
    bulwark = (YY <= -0.30) & (YB > -0.30)
    col[bulwark] = P.HULL_STRIPE
    # Soften band edges by a pixel.
    col = np.array(Image.fromarray((col * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.6)), dtype=np.float64) / 255

    # Planking: horizontal seams every 0.34 m in the body, staggered butts.
    body = (YB < -1.62) & (YB > -10.95)
    plank = np.floor((YB + 1.62) / -0.34).astype(np.int64)
    py = ((YB + 1.62) / -0.34) % 1.0
    seam = (1 - _smooth(0.004, 0.012, np.minimum(py, 1 - py) * 0.34)) * body
    blen = 4.0 + 2.5 * _hash1(plank, seed)
    boff = _hash1(plank, seed + 1) * blen
    bpos = ((SS + boff) % blen)
    butt = (1 - _smooth(0.006, 0.016, np.minimum(bpos, blen - bpos))) * body
    col *= (1 - 0.35 * np.maximum(seam, butt))[..., None]
    tone = 0.95 + 0.08 * _hash1(plank * 7 + np.floor((SS + boff) / blen).astype(np.int64), seed + 2)
    col = np.where(body[..., None], col * tone[..., None], col)
    grain = fbm(SS * 0.7, YY * 18.0, seed + 3, 4)
    col *= (0.93 + 0.12 * grain)[..., None]
    # Gunwale and rub rail wood grain.
    wood = (YY > -0.30) | ((YB < -1.18) & (YB > -1.45))
    col = np.where(wood[..., None], col * (0.85 + 0.25 * fbm(SS * 1.6, YY * 40, seed + 4, 3))[..., None], col)

    # Paint wear: chips near the gunwale and the rub rail, grime above the waterline.
    chips = _smooth(0.68, 0.76, fbm(SS * 3.2, YY * 3.2, seed + 5, 4)) * (_smooth(-3.0, -0.4, YB) * (YY < -0.36))
    col = _mix(col, _c(P.GUNWALE) * 0.8, chips * 0.75)
    grime = _smooth(-8.0, -10.9, YB) * (YB > -10.95) * (0.5 + 0.5 * fbm(SS * 0.8, YY * 2, seed + 6, 3))
    col = _mix(col, col * _c((0.55, 0.62, 0.48)), grime * 0.45)
    algae = _smooth(-11.0, -11.6, YY) * (0.5 + 0.5 * fbm(SS * 2.0, YY * 6.0, seed + 7, 3))
    col = _mix(col, _c((0.22, 0.30, 0.14)), algae * 0.6)

    img = Image.fromarray(np.clip(col * 255, 0, 255).astype(np.uint8), "RGB")
    _hull_decals(img, pts, s_tab, total, spec, rng)
    img.save(path)
    return path


def _hull_px(s, y, total):
    return s / total * HULL_W, (y / HULL_BOTTOM) * HULL_H


def _hull_decals(img, pts, s_tab, total, spec, rng):
    draw = ImageDraw.Draw(img, "RGBA")
    k = HULL_W / total  # pixels per metre (both axes, ~82)
    hb = spec["hull"]
    port_end = _s_at_x(pts, s_tab, hb["stern_x"] + 0.0001, 1)  # transom port corner
    star_start = total - port_end
    # Portholes along the parallel midbody, both sides.
    for side in (1, -1):
        for x in np.arange(-11.0, 4.1, 2.6):
            s = _s_at_x(pts, s_tab, float(x), side)
            cx, cy = _hull_px(s, -2.9 + float(sheer_dy(x)), total)
            r = 0.30 * k
            draw.ellipse((cx - r - 0.07 * k, cy - r - 0.07 * k, cx + r + 0.07 * k, cy + r + 0.07 * k), fill=_rgb255(P.BRASS))
            draw.ellipse((cx - r, cy - r, cx + r, cy + r), fill=_rgb255(P.PORTHOLE_GLASS))
            draw.ellipse((cx - r * 0.55, cy - r * 0.7, cx - r * 0.05, cy - r * 0.2), fill=(150, 185, 205, 150))
            for b in range(8):
                a = 2 * math.pi * b / 8
                bx, by = cx + (r + 0.035 * k) * math.cos(a), cy + (r + 0.035 * k) * math.sin(a)
                draw.ellipse((bx - 2, by - 2, bx + 2, by + 2), fill=_rgb255(geo_scale(P.BRASS, 0.6)))
            # A faint rust run beneath.
            for t in range(int(0.9 * k)):
                a = 70 * (1 - t / (0.9 * k))
                draw.point((cx + rng.uniform(-2, 2), cy + r + 0.07 * k + t), fill=_rgb255(P.RUST, int(a)))
        # Scuppers at the deck edge with drip stains.
        for x in (-11.0, -6.0, -1.0, 4.0):
            s = _s_at_x(pts, s_tab, x, side)
            cx, cy = _hull_px(s, -0.62, total)
            draw.rectangle((cx - 0.2 * k, cy - 0.08 * k, cx + 0.2 * k, cy + 0.08 * k), fill=_rgb255(P.STEEL_DARK))
            for t in range(int(2.2 * k)):
                a = 60 * (1 - t / (2.2 * k))
                draw.line((cx - 0.12 * k, cy + 0.08 * k + t, cx + 0.12 * k, cy + 0.08 * k + t), fill=_rgb255((0.30, 0.34, 0.30), int(a)))
    # Bow eyes (luzzu style) and hawse pipes near the stem.
    for side in (1, -1):
        s = _s_at_x(pts, s_tab, 18.2, side)
        cx, cy = _hull_px(s, -2.15, total)
        w, h = 0.95 * k, 0.48 * k
        flip = 1 if side > 0 else -1  # the eye's corner points towards the stem
        lid = []
        for t in np.linspace(0, 1, 24):
            lid.append((cx + flip * (t - 0.5) * 2 * w, cy - h * math.sin(math.pi * t)))
        for t in np.linspace(1, 0, 24):
            lid.append((cx + flip * (t - 0.5) * 2 * w, cy + 0.65 * h * math.sin(math.pi * t)))
        draw.polygon(lid, fill=_rgb255(P.EYE_WHITE), outline=_rgb255(P.EYE_BLACK))
        draw.ellipse((cx - 0.34 * k, cy - 0.36 * k, cx + 0.34 * k, cy + 0.3 * k), fill=_rgb255(P.CABIN_TRIM))
        draw.ellipse((cx - 0.17 * k, cy - 0.2 * k, cx + 0.17 * k, cy + 0.14 * k), fill=_rgb255(P.EYE_BLACK))
        brow = [(cx + flip * (t - 0.5) * 2.3 * w, cy - h * 1.35 * math.sin(math.pi * t) - 0.08 * k) for t in np.linspace(0, 1, 20)]
        draw.line(brow, fill=_rgb255(P.EYE_BLACK), width=int(0.09 * k))
        lash = [(cx + flip * (0.5) * 2 * w, cy), (cx + flip * (0.5 * 2 * w + 0.35 * k), cy + 0.12 * k)]
        draw.line(lash, fill=_rgb255(P.EYE_RED), width=int(0.08 * k))
        # Hawse pipe with a stowed anchor and rust streak.
        s2 = _s_at_x(pts, s_tab, 19.3, side)
        hx, hy = _hull_px(s2, -1.0, total)
        draw.ellipse((hx - 0.28 * k, hy - 0.2 * k, hx + 0.28 * k, hy + 0.2 * k), fill=_rgb255(P.STEEL))
        draw.ellipse((hx - 0.2 * k, hy - 0.13 * k, hx + 0.2 * k, hy + 0.13 * k), fill=_rgb255(P.STEEL_DARK))
        for t in range(int(3.0 * k)):
            a = 90 * (1 - t / (3.0 * k))
            draw.line((hx - 0.1 * k, hy + 0.2 * k + t, hx + 0.1 * k, hy + 0.2 * k + t), fill=_rgb255(P.RUST, int(a)))
    # SPLASH DECK on both bows (letters follow the hull strip, readable from outside).
    for side in (1, -1):
        s = _s_at_x(pts, s_tab, 10.8, side)
        cx, cy = _hull_px(s, -3.65 + float(sheer_dy(10.8)), total)
        _strip_text(img, "SPLASH DECK", cx, cy, 0.78 * k, P.NAME_PAINT, P.HULL_BLUE_DARK, stroke=0.13 * k)
    # Transom: name and port of registry.
    mid = (port_end + star_start) / 2
    cx, cy = _hull_px(mid, -2.9, total)
    _strip_text(img, "SPLASH DECK", cx, cy, 0.72 * k, P.NAME_PAINT, P.HULL_BLUE_DARK, stroke=0.12 * k)
    cx, cy = _hull_px(mid, -4.05, total)
    _strip_text(img, "PORTO SLOP", cx, cy, 0.44 * k, P.SHEER_YELLOW, P.HULL_BLUE_DARK, stroke=0.08 * k)
    # Draft marks at the stem and stern.
    for s in (6.0, total - 6.0, port_end - 0.6, star_start + 0.6):
        for i, y in enumerate((-8.0, -9.0, -10.0)):
            cx, cy = _hull_px(s % total, y, total)
            _strip_text(img, str(8 + i), cx, cy, 0.3 * k, P.HULL_STRIPE, None, stroke=0.05 * k)


def _strip_text(img, text, cx, cy, height, color, shadow, stroke):
    """Text centred at image pixel (cx, cy); image rows grow downwards."""
    pad = int(height * 0.6)
    width = int(len(text) * height * 1.05) + 2 * pad
    hgt = int(height * 1.6) + 2 * pad
    canvas = Image.new("RGBA", (width, hgt), (0, 0, 0, 0))
    d = ImageDraw.Draw(canvas)
    base = (width / 2, (hgt - height) / 2)
    if shadow is not None:
        _stroke_text(d, text, height, (base[0] + stroke * 0.4, base[1] - stroke * 0.4), stroke * 1.25, _rgb255(shadow))
    _stroke_text(d, text, height, base, stroke, _rgb255(color))
    canvas = canvas.transpose(Image.Transpose.FLIP_TOP_BOTTOM)
    img.paste(canvas, (int(cx - width / 2), int(cy - hgt / 2)), canvas)


# ---------------------------------------------------------------- atlas

def _board(w, h, color, border, seed, ss=2):
    W, H = w * ss, h * ss
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float64)
    grain = fbm(xx / W * 4.0, yy / H * 30.0, seed, 3)
    base = _c(color) * (0.9 + 0.12 * grain)[..., None]
    edge = np.minimum.reduce([xx, W - xx, yy, H - yy])
    rim = edge < 22 * ss
    base[rim] = _c(border)
    inner = (edge >= 22 * ss) & (edge < 28 * ss)
    base[inner] = _c(color) * 0.82
    return Image.fromarray((np.clip(base, 0, 1) * 255).astype(np.uint8), "RGB").convert("RGBA")


def _sign(w=1024, h=384, seed=41):
    rng = random.Random(seed)
    ss = 2
    img = _board(w, h, P.SIGN_BOARD, P.CABIN_TRIM, seed, ss)
    W, H = w * ss, h * ss
    canvas = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(canvas)
    _stroke_text(d, "NO FIGHTING", 92 * ss, (W / 2, 206 * ss), 21 * ss, _rgb255(P.SIGN_PAINT), wobble=rng)
    _stroke_text(d, "ON DECK", 78 * ss, (W / 2, 72 * ss), 18 * ss, _rgb255(P.SIGN_PAINT_DARK), tracking=1.2, wobble=rng)
    canvas = canvas.transpose(Image.Transpose.FLIP_TOP_BOTTOM)
    img.alpha_composite(canvas)
    d2 = ImageDraw.Draw(img)
    for nx, ny in ((40, 40), (w - 40, 40), (40, h - 40), (w - 40, h - 40)):
        cx, cy = nx * ss, ny * ss
        d2.ellipse((cx - 9 * ss, cy - 9 * ss, cx + 9 * ss, cy + 9 * ss), fill=(150, 150, 150, 255))
    img = img.resize((w, h), Image.Resampling.LANCZOS)
    img.putalpha(Image.new("L", (w, h), 0))
    return img


def _glass(w, h, wheel=False, seed=5):
    ss = 2
    W, H = w * ss, h * ss
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float64)
    t = yy / H
    col = _mix(_c(P.WINDOW_SKY) * np.ones((H, W, 3)), _c(P.WINDOW_GLASS), _smooth(0.0, 0.55, t))
    streak = np.abs((xx / W - 0.25) + (yy / H) * 0.6 - 0.45) < 0.07
    col[streak] = col[streak] * 0.6 + 0.4
    img = Image.fromarray((np.clip(col, 0, 1) * 255).astype(np.uint8), "RGB").convert("RGBA")
    d = ImageDraw.Draw(img)
    if wheel:
        cx, cy, r = W / 2, H * 0.78, H * 0.36
        dark = _rgb255(P.WHEEL_WOOD)
        d.ellipse((cx - r, cy - r, cx + r, cy + r), outline=dark, width=int(9 * ss))
        d.ellipse((cx - r * 0.18, cy - r * 0.18, cx + r * 0.18, cy + r * 0.18), fill=dark)
        for k in range(8):
            a = 2 * math.pi * k / 8
            x1, y1 = cx + r * 1.25 * math.cos(a), cy + r * 1.25 * math.sin(a)
            d.line((cx, cy, x1, y1), fill=dark, width=int(6 * ss))
            d.ellipse((x1 - 6 * ss, y1 - 6 * ss, x1 + 6 * ss, y1 + 6 * ss), fill=dark)
    frame = int(12 * ss)
    d.rectangle((0, 0, W - 1, H - 1), outline=_rgb255(P.CABIN_WHITE), width=frame)
    d.line((W / 2, 0, W / 2, H), fill=_rgb255(P.CABIN_WHITE), width=int(5 * ss)) if not wheel else None
    img = img.resize((w, h), Image.Resampling.LANCZOS)
    img.putalpha(Image.new("L", (w, h), 0))
    return img


def _roof_number(w=384, h=192):
    ss = 2
    img = Image.new("RGBA", (w * ss, h * ss), _rgb255(P.CABIN_ROOF))
    d = ImageDraw.Draw(img)
    canvas = Image.new("RGBA", (w * ss, h * ss), (0, 0, 0, 0))
    dc = ImageDraw.Draw(canvas)
    _stroke_text(dc, "SLOP 04", 52 * ss, (w * ss / 2, 70 * ss), 11 * ss, _rgb255(P.ROOF_NUMBER), tracking=1.1)
    canvas = canvas.transpose(Image.Transpose.FLIP_TOP_BOTTOM)
    img.alpha_composite(canvas)
    img = img.resize((w, h), Image.Resampling.LANCZOS)
    img.putalpha(Image.new("L", (w, h), 0))
    return img


def _market(w=1024, h=256, seed=43):
    ss = 2
    img = _board(w, h, P.CABIN_TRIM, P.SIGN_BOARD, seed, ss)
    canvas = Image.new("RGBA", (w * ss, h * ss), (0, 0, 0, 0))
    d = ImageDraw.Draw(canvas)
    _stroke_text(d, "PESCHERIA", 92 * ss, (w * ss / 2, 82 * ss), 18 * ss, _rgb255(P.SIGN_BOARD), tracking=1.1)
    canvas = canvas.transpose(Image.Transpose.FLIP_TOP_BOTTOM)
    img.alpha_composite(canvas)
    img = img.resize((w, h), Image.Resampling.LANCZOS)
    img.putalpha(Image.new("L", (w, h), 0))
    return img


def _clock(size=256):
    ss = 2
    S = size * ss
    img = Image.new("RGBA", (S, S), _rgb255(P.CHURCH))
    d = ImageDraw.Draw(img)
    c = S / 2
    d.ellipse((12 * ss, 12 * ss, S - 12 * ss, S - 12 * ss), fill=(250, 248, 240, 255), outline=(40, 40, 44, 255), width=8 * ss)
    for k in range(12):
        a = 2 * math.pi * k / 12
        r0, r1 = S * 0.36, S * 0.42
        d.line((c + r0 * math.sin(a), c - r0 * math.cos(a), c + r1 * math.sin(a), c - r1 * math.cos(a)), fill=(40, 40, 44, 255), width=6 * ss)
    for ang, length, width in ((math.radians(290), 0.22, 9), (math.radians(60), 0.33, 6)):  # about 9:50
        d.line((c, c, c + S * length * math.sin(ang), c - S * length * math.cos(ang)), fill=(30, 30, 34, 255), width=width * ss)
    img = img.resize((size, size), Image.Resampling.LANCZOS)
    img.putalpha(Image.new("L", (size, size), 0))
    return img


def _lifering(size=256):
    ss = 2
    S = size * ss
    img = Image.new("RGBA", (S, S), _rgb255(P.CABIN_WHITE))
    d = ImageDraw.Draw(img)
    c = S / 2
    ro, ri = S * 0.46, S * 0.27
    for k in range(4):
        start = k * 90 + 45
        col = P.NET_FLOAT if k % 2 == 0 else P.CABIN_WHITE
        d.pieslice((c - ro, c - ro, c + ro, c + ro), start, start + 90, fill=_rgb255(col))
    d.ellipse((c - ro, c - ro, c + ro, c + ro), outline=(60, 60, 60, 255), width=3 * ss)
    d.ellipse((c - ri, c - ri, c + ri, c + ri), fill=_rgb255(P.CABIN_WHITE), outline=(60, 60, 60, 255), width=3 * ss)
    for k in range(4):
        a = math.radians(k * 90)
        d.line((c + ri * math.cos(a), c + ri * math.sin(a), c + ro * math.cos(a), c + ro * math.sin(a)), fill=_rgb255(P.ROPE), width=6 * ss)
    img = img.resize((size, size), Image.Resampling.LANCZOS)
    img.putalpha(Image.new("L", (size, size), 0))
    return img


def _door(w=256, h=512, seed=47):
    ss = 2
    W, H = w * ss, h * ss
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float64)
    grain = fbm(xx / W * 3.0, yy / H * 24.0, seed, 3)
    col = _c(P.CABIN_TRIM) * (0.9 + 0.15 * grain)[..., None]
    img = Image.fromarray((np.clip(col, 0, 1) * 255).astype(np.uint8), "RGB").convert("RGBA")
    d = ImageDraw.Draw(img)
    d.rectangle((0, 0, W - 1, H - 1), outline=_rgb255(P.CABIN_WHITE), width=10 * ss)
    cx, cy, r = W / 2, H * 0.3, W * 0.22
    d.ellipse((cx - r - 6 * ss, cy - r - 6 * ss, cx + r + 6 * ss, cy + r + 6 * ss), fill=_rgb255(P.BRASS))
    d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=_rgb255(P.WINDOW_GLASS))
    d.rectangle((W * 0.74, H * 0.55, W * 0.84, H * 0.6), fill=_rgb255(P.BRASS))
    for y in (0.48, 0.75):
        d.rectangle((14 * ss, H * y, W - 14 * ss, H * y + 6 * ss), fill=_rgb255(geo_scale(P.CABIN_TRIM, 0.7)))
    img = img.resize((w, h), Image.Resampling.LANCZOS)
    img.putalpha(Image.new("L", (w, h), 0))
    return img


def _hatch_top(w=1024, h=853, seed=53):
    """Top of the fish hatch (6 x 5 m): weathered hatch boards, iron straps, a heaped net
    with orange floats and a coiled line. u runs along x (aft to fore), v along z."""
    rng = random.Random(seed)
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float64)
    u, v = xx / w, yy / h
    board = np.floor(u * 10.0)
    tone = 0.86 + 0.16 * _hash1(board.astype(np.int64), seed)
    grain = fbm(u * 12.0 + board * 3.0, v * 2.0, seed + 1, 4)
    col = _c(P.TEAK_DARK) * (tone * (0.82 + 0.3 * grain))[..., None]
    seam = np.abs((u * 10.0) % 1.0 - 0.5) > 0.47
    col[seam] = _c(P.CAULK)
    for sv in (0.2, 0.5, 0.8):
        strap = np.abs(v - sv) < 0.022
        col[strap] = _c(P.STEEL_DARK) * (0.9 + 0.2 * fbm(u[strap] * 30, v[strap] * 30, seed + 2, 2))[..., None]
    edge = np.minimum.reduce([u, 1 - u, v, 1 - v])
    col *= (0.7 + 0.3 * _smooth(0.0, 0.05, edge))[..., None]
    img = Image.fromarray((np.clip(col, 0, 1) * 255).astype(np.uint8), "RGB").convert("RGBA")
    d = ImageDraw.Draw(img, "RGBA")
    # A heaped net on the forward half: layered blobs, a mesh masked to the heap, floats.
    cx, cy = w * 0.68, h * 0.55
    heap = Image.new("L", (w, h), 0)
    dh = ImageDraw.Draw(heap)
    for layer in range(5):
        pts = []
        for k in range(36):
            a = 2 * math.pi * k / 36
            r = (0.27 - layer * 0.035) * (1 + 0.18 * math.sin(3 * a + layer) + 0.1 * math.sin(7 * a + 2 * layer))
            pts.append((cx + r * w * math.cos(a) * 1.05, cy + r * h * math.sin(a) * 1.2))
        d.polygon(pts, fill=_rgb255(geo_scale(P.NET, 0.6 + 0.1 * layer)))
        if layer == 0:
            dh.polygon(pts, fill=255)
    mesh = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    dm = ImageDraw.Draw(mesh)
    for k in range(-60, 61):
        dm.line((0, cy + k * 7, w, cy + k * 7 + 0.4 * h), fill=_rgb255(geo_scale(P.NET, 0.42), 150), width=2)
        dm.line((0, cy + k * 7 + 0.4 * h, w, cy + k * 7), fill=_rgb255(geo_scale(P.NET, 0.42), 150), width=2)
    img.paste(mesh, (0, 0), Image.fromarray((np.asarray(mesh)[..., 3] * (np.asarray(heap) / 255.0)).astype(np.uint8), "L"))
    d = ImageDraw.Draw(img, "RGBA")
    for k in range(16):
        a = rng.uniform(0, 2 * math.pi)
        r = rng.uniform(0.04, 0.22)
        fx, fy = cx + r * w * math.cos(a), cy + r * h * math.sin(a) * 1.1
        d.ellipse((fx - 14, fy - 10, fx + 14, fy + 10), fill=_rgb255(P.NET_FLOAT), outline=_rgb255(geo_scale(P.NET_FLOAT, 0.55)), width=2)
    # A coiled mooring line on the aft half.
    ccx, ccy = w * 0.22, h * 0.32
    for k in range(260):
        t = k / 259
        a = t * 5 * 2 * math.pi
        r = (0.03 + 0.1 * t) * w
        x0, y0 = ccx + r * math.cos(a), ccy + r * math.sin(a)
        d.ellipse((x0 - 7, y0 - 7, x0 + 7, y0 + 7), fill=_rgb255(P.ROPE))
    img.putalpha(Image.new("L", (w, h), 0))
    return img


def _porthole(size=256):
    ss = 2
    S = size * ss
    img = Image.new("RGBA", (S, S), _rgb255(P.CABIN_WHITE))
    d = ImageDraw.Draw(img)
    c = S / 2
    d.ellipse((c - S * 0.44, c - S * 0.44, c + S * 0.44, c + S * 0.44), fill=_rgb255(P.BRASS))
    d.ellipse((c - S * 0.34, c - S * 0.34, c + S * 0.34, c + S * 0.34), fill=_rgb255(P.WINDOW_GLASS))
    d.ellipse((c - S * 0.24, c - S * 0.26, c - S * 0.02, c - S * 0.06), fill=(160, 190, 210, 255))
    img = img.resize((size, size), Image.Resampling.LANCZOS)
    img.putalpha(Image.new("L", (size, size), 0))
    return img


def _house_window(w=128, h=256):
    img = Image.new("RGBA", (w, h), _rgb255(P.WINDOW_DARK))
    d = ImageDraw.Draw(img)
    d.rectangle((0, 0, w - 1, h - 1), outline=(235, 230, 220, 255), width=8)
    d.line((w / 2, 0, w / 2, h), fill=(235, 230, 220, 255), width=5)
    d.line((0, h * 0.45, w, h * 0.45), fill=(235, 230, 220, 255), width=5)
    img.putalpha(Image.new("L", (w, h), 0))
    return img


def _shutter(w=128, h=256):
    img = Image.new("RGBA", (w, h), (255, 255, 255, 255))
    d = ImageDraw.Draw(img)
    for y in range(10, h - 10, 14):
        d.rectangle((10, y, w - 10, y + 7), fill=(190, 190, 190, 255))
    d.rectangle((0, 0, w - 1, h - 1), outline=(150, 150, 150, 255), width=8)
    img.putalpha(Image.new("L", (w, h), 0))
    return img


def atlas(path):
    img = Image.new("RGBA", (ATLAS, ATLAS), (255, 255, 255, 0))
    img.paste(_sign(), REGIONS["sign"][:2])
    img.paste(_glass(256, 256), REGIONS["window"][:2])
    img.paste(_glass(256, 256, wheel=True), REGIONS["wheel"][:2])
    img.paste(_roof_number(), REGIONS["roofnum"][:2])
    img.paste(_market(), REGIONS["market"][:2])
    img.paste(_clock(), REGIONS["clock"][:2])
    img.paste(_lifering(), REGIONS["lifering"][:2])
    img.paste(_door(), REGIONS["door"][:2])
    img.paste(_hatch_top(), REGIONS["hatchtop"][:2])
    img.paste(_porthole(), REGIONS["porthole"][:2])
    img.paste(_shutter(), REGIONS["shutter"][:2])
    img.paste(_house_window(), REGIONS["window_house"][:2])
    x0, y0, w, h = REGIONS["white"]
    img.paste(Image.new("RGBA", (w, h), (255, 255, 255, 255)), (x0, y0))
    img.save(path)
    return path


# ---------------------------------------------------------------- tiling maps

def detail(path, size=512, seed=59):
    """Tileable grain: fibres plus soft grit, mean near 0.5 (neutral for the shader)."""
    c = np.arange(size) / size
    X, Z = np.meshgrid(c, c)
    period = 8
    grit = fbm(X * period, Z * period, seed, 4, period=period)
    fibre = fbm(X * 4, Z * 48, seed + 7, 3, period=(4, 48))
    v = 0.5 + 0.55 * (grit - 0.5) + 0.3 * (fibre - 0.5)
    img = Image.fromarray((np.clip(v, 0, 1) * 255).astype(np.uint8), "L")
    img = img.filter(ImageFilter.GaussianBlur(0.6))
    img.save(path)
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
    strength = 0.035
    nx, nz = -dx * strength, -dz * strength
    ny = np.ones_like(nx)
    n = np.sqrt(nx * nx + ny * ny + nz * nz)
    rgb = np.stack([nx / n, nz / n, ny / n], axis=-1) * 0.5 + 0.5  # tangent-style: R=x, G=z, B=up
    Image.fromarray((np.clip(rgb, 0, 1) * 255).astype(np.uint8), "RGB").save(path)
    return path


if __name__ == "__main__":
    import os
    import sys
    out = sys.argv[1] if len(sys.argv) > 1 else "/tmp/splash-deck-textures"
    os.makedirs(out, exist_ok=True)
    deck(os.path.join(out, "SplashDeckDeck.png"))
    hull(os.path.join(out, "SplashDeckHull.png"))
    atlas(os.path.join(out, "SplashDeckAtlas.png"))
    detail(os.path.join(out, "SplashDeckDetail.png"))
    ripples(os.path.join(out, "SplashDeckRipples.png"))
    print("textures written to", out)
