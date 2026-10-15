"""Small Tibetan monastery and training props (all presentation-only)."""
from __future__ import annotations

import math

import geo
import palette as P


def gyaltsen(m, base, height=1.5, radius=0.22):
    """Gilded victory-banner cylinder used on roof corners."""
    x, y, z = base
    prof = [(radius * 0.55, 0), (radius * 0.55, height * 0.08), (radius * 1.15, height * 0.1), (radius * 1.0, height * 0.16),
            (radius * 1.0, height * 0.62), (radius * 1.2, height * 0.66), (radius * 0.85, height * 0.72),
            (radius * 0.9, height * 0.8), (radius * 0.45, height * 0.86), (radius * 0.25, height * 0.94), (0.02, height)]
    cols = [P.GOLD_DARK, P.GOLD, P.GOLD_DARK, P.GOLD, P.GOLD_DARK, P.GOLD, P.GOLD_DARK, P.GOLD, P.GOLD, P.GOLD]
    geo.lathe(m, (x, y, z), prof, 12, cols, mat="SA_Gloss")
    # Black yak-hair skirt band
    geo.lathe(m, (x, y, z), [(radius * 1.04, height * 0.30), (radius * 1.04, height * 0.46)], 12, [P.BLACK], cap_top=False)


def ganjira(m, base, height=2.4):
    """Bulbous gilded roof finial (vase, lotus, spire)."""
    h = height
    prof = [(0.55, 0), (0.62, h * 0.05), (0.38, h * 0.12), (0.52, h * 0.22), (0.62, h * 0.32), (0.55, h * 0.42),
            (0.22, h * 0.52), (0.30, h * 0.58), (0.16, h * 0.66), (0.20, h * 0.74), (0.10, h * 0.84), (0.13, h * 0.9), (0.01, h)]
    cols = [P.GOLD_DARK, P.GOLD, P.GOLD, P.GOLD, P.GOLD_DARK, P.GOLD, P.GOLD_DARK, P.GOLD, P.GOLD_DARK, P.GOLD, P.GOLD_DARK, P.GOLD]
    geo.lathe(m, base, prof, 16, cols, mat="SA_Gloss")


def dharma_wheel(m, center, facing_z=-1, radius=0.55):
    """Eight-spoked wheel standing upright facing south (-z) with two kneeling deer."""
    cx, cy, cz = center
    th = 0.07
    seg = 20
    for r0, r1, col in ((radius * 0.82, radius, P.GOLD), (radius * 0.16, radius * 0.3, P.GOLD_DARK)):
        for k in range(seg):
            a0, a1 = 2 * math.pi * k / seg, 2 * math.pi * (k + 1) / seg
            for side in (-1, 1):
                zz = cz + side * th
                pts = [(cx + r0 * math.cos(a0), cy + r0 * math.sin(a0), zz), (cx + r1 * math.cos(a0), cy + r1 * math.sin(a0), zz),
                       (cx + r1 * math.cos(a1), cy + r1 * math.sin(a1), zz), (cx + r0 * math.cos(a1), cy + r0 * math.sin(a1), zz)]
                geo.oriented(m, pts, (0, 0, side), col, "SA_Gloss")
            o0, o1 = (cx + r1 * math.cos(a0), cy + r1 * math.sin(a0)), (cx + r1 * math.cos(a1), cy + r1 * math.sin(a1))
            am = (a0 + a1) / 2
            geo.oriented(m, [(o0[0], o0[1], cz - th), (o0[0], o0[1], cz + th), (o1[0], o1[1], cz + th), (o1[0], o1[1], cz - th)],
                         (math.cos(am), math.sin(am), 0), P.GOLD_DARK, "SA_Gloss")
    for k in range(8):
        a = 2 * math.pi * k / 8 + math.pi / 8
        p0 = (cx + radius * 0.25 * math.cos(a), cy + radius * 0.25 * math.sin(a), cz)
        p1 = (cx + radius * 0.85 * math.cos(a), cy + radius * 0.85 * math.sin(a), cz)
        geo.cylinder_between(m, p0, p1, 0.035, 5, P.GOLD, "SA_Gloss")
    geo.box(m, (cx, cy - radius - 0.1, cz), (0.5, 0.2, 0.32), P.GOLD_DARK, "SA_Gloss")
    for side in (-1, 1):
        deer(m, (cx + side * (radius + 0.45), cy - radius - 0.2, cz), side)


def deer(m, base, side):
    x, y, z = base
    gold = P.GOLD
    geo.ellipsoid(m, (x, y + 0.28, z), (0.32, 0.2, 0.16), gold, "SA_Gloss", rings=5, segments=8)
    hx = x - side * 0.22
    geo.cylinder_between(m, (hx, y + 0.36, z), (hx - side * 0.08, y + 0.62, z), 0.07, 6, gold, "SA_Gloss")
    geo.ellipsoid(m, (hx - side * 0.12, y + 0.68, z), (0.12, 0.08, 0.07), gold, "SA_Gloss", rings=4, segments=8)
    for ear in (-0.04, 0.04):
        geo.cylinder_between(m, (hx - side * 0.1, y + 0.74, z + ear), (hx - side * 0.04, y + 0.88, z + ear * 2.5), 0.015, 4, P.GOLD_DARK, "SA_Gloss")
    geo.box(m, (x, y + 0.05, z), (0.62, 0.1, 0.3), P.GOLD_DARK, "SA_Gloss")


def prayer_wheel(m, base, radius=0.21, height=0.6, frame=True):
    x, y, z = base
    prof = [(radius * 0.92, 0.08), (radius, 0.12), (radius, height * 0.36), (radius * 1.02, height * 0.42),
            (radius * 1.02, height * 0.58), (radius, height * 0.64), (radius, height - 0.04), (radius * 0.85, height), (0.03, height + 0.06)]
    cols = [P.GOLD_DARK, P.RED, P.GOLD, P.GOLD_DARK, P.GOLD, P.RED, P.GOLD_DARK, P.GOLD]
    geo.lathe(m, (x, y, z), prof, 12, cols, mat="SA_Stone", mats=["SA_Gloss", "SA_Stone", "SA_Gloss", "SA_Gloss", "SA_Gloss", "SA_Stone", "SA_Gloss", "SA_Gloss"])
    if frame:
        geo.cylinder_between(m, (x, y, z), (x, y + 0.1, z), 0.04, 5, P.WOOD_DARK)


def wheel_gallery(m, a, b, n, y0, count, depth=0.62, height=1.45, wheel_r=0.2):
    """Row of prayer wheels in a recessed niche of a vertical face (a->b, outward n)."""
    import dressing as D
    L = D.side_len(a, b)
    margin = (L - count * 0.62) / 2
    u0, u1 = margin / L, 1 - margin / L
    # niche back wall, floor and ceiling (recessed `depth` into the slab face)
    D.wall_quad(m, a, b, n, u0, u1, y0, y0 + height, P.MAROON, off=-depth)
    for i in range(count):
        s = margin + (i + 0.5) * 0.62
        c = D.wall_point(a, b, n, s / L, y0 + 0.08, -depth * 0.45)
        prayer_wheel(m, c, wheel_r, height - 0.4)
    p = lambda u, y, o: D.wall_point(a, b, n, u, y, o)
    geo.oriented(m, [p(u0, y0, -depth), p(u0, y0, 0.0), p(u1, y0, 0.0), p(u1, y0, -depth)], (0, 1, 0), P.MASONRY_DARK)
    geo.oriented(m, [p(u0, y0 + height, -depth), p(u0, y0 + height, 0.0), p(u1, y0 + height, 0.0), p(u1, y0 + height, -depth)], (0, -1, 0), P.WOOD_DARK)
    along = ((b[0] - a[0]) / L, 0, (b[1] - a[1]) / L)
    for u, sgn in ((u0, 1), (u1, -1)):
        geo.oriented(m, [p(u, y0, -depth), p(u, y0 + height, -depth), p(u, y0 + height, 0.0), p(u, y0, 0.0)],
                     (along[0] * sgn, 0, along[2] * sgn), P.MAROON)
    # painted lintel over the niche
    D.wall_quad(m, a, b, n, u0, u1, y0 + height, y0 + height + 0.12, P.OCHRE, off=0.01)


def lhatho(m, base, flag_seed=0):
    """Whitewashed roof-corner shrine with a juniper/flag bundle."""
    x, y, z = base
    geo.box(m, (x, y + 0.25, z), (0.46, 0.5, 0.46), P.WHITEWASH, skip=("bottom",))
    geo.box(m, (x, y + 0.53, z), (0.52, 0.06, 0.52), P.MAROON, skip=("bottom",))
    for k in range(5):
        a = 2 * math.pi * k / 5 + flag_seed
        tip = (x + 0.18 * math.cos(a), y + 1.25 + 0.1 * (k % 2), z + 0.18 * math.sin(a))
        geo.cylinder_between(m, (x, y + 0.56, z), tip, 0.018, 4, P.WOOD_DARK)
        cloth_flag(m, tip, (math.cos(a), 0, math.sin(a)), P.FLAG_COLORS[k], 0.22, 0.32, 0.0)


def cloth_flag(m, top, direction, color, w, h, lift):
    """Small vertical flag hanging from `top`, extending along `direction` (horizontal)."""
    d = geo._norm((direction[0], 0, direction[2]))
    p0 = top
    p1 = (top[0] + d[0] * w, top[1] + lift, top[2] + d[2] * w)
    q0 = (p0[0], p0[1] - h, p0[2])
    q1 = (p1[0], p1[1] - h + lift * 0.5, p1[2])
    c = geo.rgba(color)
    m.poly([q0, p0, p1, q1], [geo.rgba(color, 0.0), geo.rgba(color, 0.0), geo.rgba(color, 0.9), geo.rgba(color, 1.0)], "SA_Cloth")


def gong(m, base, facing_x=-1):
    """Red training-hall gong frame; the bronze gong faces along x."""
    x, y, z = base
    for dz in (-1.15, 1.15):
        geo.box(m, (x, y + 1.35, z + dz), (0.22, 2.7, 0.22), P.RED)
        geo.box(m, (x, y + 0.08, z + dz), (0.5, 0.16, 0.5), P.MASONRY_DARK)
    geo.box(m, (x, y + 2.78, z), (0.3, 0.22, 2.9), P.RED_DARK)
    for dz in (-1.5, 1.5):
        geo.box(m, (x, y + 2.86, z + dz * 1.0), (0.3, 0.18, 0.3), P.GOLD, "SA_Gloss")
    geo.box(m, (x, y + 2.6, z), (0.24, 0.12, 2.5), P.OCHRE)
    r = 0.8
    # disc in the y/z plane facing -x: build as a fan
    seg = 24
    cx, cy = x + facing_x * 0.05, y + 1.62
    rings = [(r, 0.0, P.BRONZE), (r * 0.9, 0.02, P.GOLD_DARK), (r * 0.55, 0.035, P.BRONZE), (r * 0.22, 0.06, P.GOLD)]
    for i in range(len(rings) - 1):
        r0, d0, c0 = rings[i]
        r1, d1, _ = rings[i + 1]
        for k in range(seg):
            a0, a1 = 2 * math.pi * k / seg, 2 * math.pi * (k + 1) / seg
            pts = [(cx + facing_x * d0, cy + r0 * math.sin(a0), z + r0 * math.cos(a0)), (cx + facing_x * d0, cy + r0 * math.sin(a1), z + r0 * math.cos(a1)),
                   (cx + facing_x * d1, cy + r1 * math.sin(a1), z + r1 * math.cos(a1)), (cx + facing_x * d1, cy + r1 * math.sin(a0), z + r1 * math.cos(a0))]
            if geo.normal(pts)[0] * facing_x < 0:
                pts.reverse()
            m.poly(pts, c0, "SA_Gloss")
    for k in range(seg):
        a0, a1 = 2 * math.pi * k / seg, 2 * math.pi * (k + 1) / seg
        pts = [(cx - facing_x * 0.06, cy + r * math.sin(a0), z + r * math.cos(a0)), (cx - facing_x * 0.06, cy + r * math.sin(a1), z + r * math.cos(a1)),
               (cx - facing_x * 0.06, cy, z)]
        if geo.normal(pts)[0] * facing_x > 0:
            pts.reverse()
        m.poly(pts, P.BRONZE, "SA_Gloss")
    for dz in (-0.45, 0.45):
        geo.cylinder_between(m, (x, y + 2.6, z + dz), (cx, cy + r * 0.85, z + dz * 0.4), 0.012, 3, P.BLACK)
    # beater resting against the post
    geo.cylinder_between(m, (x + 0.3, y + 0.05, z + 1.4), (x + 0.15, y + 1.3, z + 1.3), 0.03, 5, P.WOOD)
    geo.ellipsoid(m, (x + 0.15, y + 1.32, z + 1.3), (0.11, 0.11, 0.11), P.RED_DARK, rings=4, segments=8)


def training_dummy(m, base, yaw=0.0):
    """Wooden wing-chun style dummy: post, three arms and one leg."""
    x, y, z = base
    geo.box(m, (x, y + 0.06, z), (0.7, 0.12, 0.7), P.WOOD_DARK)
    geo.cylinder_between(m, (x, y + 0.1, z), (x, y + 1.75, z), 0.15, 10, P.WOOD)
    geo.lathe(m, (x, y + 1.75, z), [(0.15, 0), (0.12, 0.06), (0.0, 0.08)], 10, [P.WOOD_LIGHT, P.WOOD_LIGHT], cap_top=False)
    c, s = math.cos(yaw), math.sin(yaw)
    def fw(d, side=0.0, up=0.0):
        return (x + d * s + side * c, y + up, z + d * c - side * s)
    for side, up, pitch in ((-0.1, 1.42, 0.05), (0.1, 1.42, 0.05), (0.0, 1.12, 0.0)):
        geo.cylinder_between(m, fw(0.1, side, up), fw(0.62, side * 1.8, up + pitch), 0.035, 6, P.WOOD_LIGHT)
    geo.cylinder_between(m, fw(0.1, 0, 0.62), fw(0.42, 0.12, 0.42), 0.045, 6, P.WOOD_LIGHT)
    geo.cylinder_between(m, fw(0.42, 0.12, 0.42), fw(0.42, 0.12, 0.12), 0.045, 6, P.WOOD_LIGHT)
    # rope wraps and a red cloth tied around the head
    for h in (0.5, 0.95, 1.3):
        geo.lathe(m, (x, y + h, z), [(0.155, 0), (0.155, 0.05)], 10, [(0.78, 0.68, 0.45)], cap_top=False)
    geo.lathe(m, (x, y + 1.58, z), [(0.16, 0), (0.16, 0.07)], 10, [P.RED], cap_top=False)


def makiwara_row(m, start, step, count, yaw=0.0):
    for i in range(count):
        p = (start[0] + step[0] * i, start[1], start[2] + step[2] * i)
        training_dummy(m, p, yaw)


def cairn(m, rng, base, height=1.2):
    x, y, z = base
    h = 0.0
    r = 0.5
    while h < height:
        ry = r * rng.uniform(0.35, 0.5)
        geo.ellipsoid(m, (x + rng.uniform(-0.05, 0.05), y + h + ry, z + rng.uniform(-0.05, 0.05)), (r, ry, r * rng.uniform(0.8, 1.0)),
                      geo.mix(P.ROCK, P.WHITE, 0.3 if h + 2 * ry < height else 0.9), rings=4, segments=8)
        h += ry * 1.7
        r *= 0.8
    geo.cylinder_between(m, (x, y + h * 0.6, z), (x + 0.1, y + h + 1.2, z), 0.02, 4, P.WOOD_DARK)
    for k in range(5):
        cloth_flag(m, (x + 0.1, y + h + 1.15 - k * 0.2, z), (1, 0, 0.3 * (k - 2)), P.FLAG_COLORS[k], 0.35, 0.18, 0.02)


def sangkang(m, base):
    """Whitewashed incense (juniper) burner shaped like a small chorten."""
    x, y, z = base
    geo.box(m, (x, y + 0.3, z), (0.9, 0.6, 0.9), P.WHITEWASH, skip=("bottom",))
    geo.box(m, (x, y + 0.64, z), (1.0, 0.08, 1.0), P.MAROON, skip=("bottom",))
    geo.lathe(m, (x, y + 0.68, z), [(0.38, 0), (0.42, 0.2), (0.36, 0.5), (0.2, 0.62), (0.12, 0.7), (0.12, 0.9), (0.18, 0.95), (0.0, 1.0)], 12,
              [P.WHITEWASH, P.WHITEWASH, P.WHITEWASH, P.WHITEWASH_SHADE, P.BLACK, P.GOLD, P.GOLD], cap_top=False,
              mats=["SA_Stone"] * 5 + ["SA_Gloss", "SA_Gloss"])
    geo.box(m, (x, y + 1.05, z - 0.36), (0.22, 0.2, 0.05), P.BLACK)


def chorten(m, base, scale=1.0):
    """White Kadampa-style stupa: stepped base, dome, harmika, 13 rings and a gold finial."""
    x, y, z = base
    s = scale
    steps = [(2.4, 0.35), (2.1, 0.3), (1.8, 0.28), (1.55, 0.25)]
    h = 0.0
    for w, t in steps:
        geo.box(m, (x, y + (h + t / 2) * s, z), (w * s, t * s, w * s), P.WHITEWASH, skip=("bottom",), top_color=P.WHITEWASH_SHADE)
        h += t
    geo.box(m, (x, y + (h + 0.06) * s, z), (1.6 * s, 0.12 * s, 1.6 * s), P.MAROON, skip=("bottom",))
    h += 0.12
    dome = [(0.62, 0), (0.75, 0.12), (0.78, 0.45), (0.7, 0.75), (0.48, 0.98), (0.25, 1.08)]
    geo.lathe(m, (x, y + h * s, z), [(r * s, hh * s) for r, hh in dome], 16, P.WHITEWASH, cap_top=True)
    h += 1.02
    geo.box(m, (x, y + (h + 0.16) * s, z), (0.5 * s, 0.32 * s, 0.5 * s), P.WHITEWASH, skip=("bottom",))
    geo.box(m, (x, y + (h + 0.36) * s, z), (0.6 * s, 0.08 * s, 0.6 * s), P.MAROON, skip=("bottom",))
    h += 0.4
    rings = []
    for k in range(13):
        r = 0.24 - k * 0.012
        rings += [(r, k * 0.11), (r * 0.82, k * 0.11 + 0.08)]
    geo.lathe(m, (x, y + h * s, z), [(r * s, hh * s) for r, hh in rings], 12, P.GOLD, mat="SA_Gloss")
    h += 13 * 0.11
    geo.lathe(m, (x, y + h * s, z), [(0.34 * s, 0), (0.36 * s, 0.05 * s), (0.08 * s, 0.14 * s)], 12, [P.GOLD, P.GOLD_DARK], mat="SA_Gloss", cap_top=False)
    geo.ellipsoid(m, (x, y + (h + 0.32) * s, z), (0.08 * s, 0.1 * s, 0.08 * s), P.GOLD, "SA_Gloss", rings=4, segments=8)
    geo.cylinder_between(m, (x, y + (h + 0.4) * s, z), (x, y + (h + 0.7) * s, z), 0.02 * s, 4, P.GOLD, "SA_Gloss")
    return y + (h + 0.7) * s


def darchor(m, base, height=9.0, banner_colors=None, seed=0):
    """Tall prayer-flag pole: painted mast, gold finial, a long vertical banner."""
    x, y, z = base
    geo.box(m, (x, y + 0.25, z), (0.9, 0.5, 0.9), P.WHITEWASH, skip=("bottom",))
    geo.cylinder_between(m, (x, y + 0.5, z), (x, y + height, z), 0.09, 8, P.WOOD, radius_b=0.06)
    ganjira(m, (x, y + height, z), 0.55)
    cols = banner_colors or P.FLAG_COLORS
    top = y + height - 0.4
    length = height * 0.62
    n = 10
    w = 0.55
    for k in range(n):
        y0, y1 = top - length * k / n, top - length * (k + 1) / n
        col = cols[k * len(cols) // n]
        a0, a1 = 0.0, 1.0
        m.poly([(x + 0.07, y1, z), (x + 0.07, y0, z), (x + 0.07 + w, y0 - 0.05, z), (x + 0.07 + w, y1 - 0.05, z)],
               [geo.rgba(col, 0.0), geo.rgba(col, 0.0), geo.rgba(col, 1.0), geo.rgba(col, 1.0)], "SA_Cloth")
    return (x, y + height - 0.15, z)
