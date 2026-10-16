"""Surface treatments that exactly cover each authoritative slab.

Walkable tops stay at the collision top height. Side treatments may stand proud
by at most a few centimetres; anything taller is registered as a prop.
"""
from __future__ import annotations

import math

from stagekit import geo
import palette as P


def sides(fp):
    """(a, b, outward) for each footprint side, CCW seen from above (+x east, +z north)."""
    x0, z0, x1, z1 = fp
    return [((x0, z0), (x1, z0), (0, -1), "s"), ((x1, z0), (x1, z1), (1, 0), "e"),
            ((x1, z1), (x0, z1), (0, 1), "n"), ((x0, z1), (x0, z0), (-1, 0), "w")]


def side_len(a, b):
    return math.hypot(b[0] - a[0], b[1] - a[1])


def wall_point(a, b, n, u, y, off=0.0):
    return (a[0] + (b[0] - a[0]) * u + n[0] * off, y, a[1] + (b[1] - a[1]) * u + n[1] * off)


def wall_quad(m, a, b, n, u0, u1, y0, y1, color, off=0.0, mat="SA_Stone", uvs=None):
    m.poly([wall_point(a, b, n, u0, y0, off), wall_point(a, b, n, u0, y1, off),
            wall_point(a, b, n, u1, y1, off), wall_point(a, b, n, u1, y0, off)], color, mat, uvs)


def wall_box(m, a, b, n, s_center, y_center, width, height, depth, color, off=0.0, mat="SA_Stone", skip=("back",)):
    """Box standing on a wall: s_center metres along a->b, protruding `depth` from off."""
    L = side_len(a, b)
    u = s_center / L
    c = wall_point(a, b, n, u, y_center, off + depth / 2)
    along_x = abs(b[0] - a[0]) > abs(b[1] - a[1])
    size = (width, height, depth) if along_x else (depth, height, width)
    back = {(0, -1): "n", (0, 1): "s", (1, 0): "w", (-1, 0): "e"}[tuple(n)]
    geo.box(m, c, size, color, mat, skip=tuple(back if s == "back" else s for s in skip))


def bottom(m, fp, y, color=P.ROCK_DEEP):
    x0, z0, x1, z1 = fp
    m.poly([(x0, y, z0), (x1, y, z0), (x1, y, z1), (x0, y, z1)], color)  # CCW from above -> down


# ---------------------------------------------------------------- tops

def subtract(rect, hole):
    """Rectangles covering rect minus hole (axis-aligned x0, z0, x1, z1)."""
    x0, z0, x1, z1 = rect
    hx0, hz0, hx1, hz1 = max(x0, hole[0]), max(z0, hole[1]), min(x1, hole[2]), min(z1, hole[3])
    if hx0 >= hx1 or hz0 >= hz1:
        return [rect]
    out = [(x0, z0, x1, hz0), (x0, hz1, x1, z1), (x0, hz0, hx0, hz1), (hx1, hz0, x1, hz1)]
    return [r for r in out if r[2] - r[0] > 1e-4 and r[3] - r[1] > 1e-4]


def paving(m, rng, fp, yt, colors, along="x", row=(0.85, 1.25), length=(0.9, 1.9), gap=0.024,
           border=0.34, border_color=P.MASONRY_DARK, grout=P.GROUT, skip_rect=None, border_sides="nsew"):
    """Flagstones in running bond. Stone tops sit exactly at yt; grout 3 cm below."""
    x0, z0, x1, z1 = fp
    depth = 0.03
    with geo.detail(m, 0.45):
        _paving(m, rng, fp, yt, colors, along, row, length, gap, border, border_color, grout, skip_rect, border_sides)


def _paving(m, rng, fp, yt, colors, along, row, length, gap, border, border_color, grout, skip_rect, border_sides):
    x0, z0, x1, z1 = fp
    depth = 0.03
    m.poly([(x0, yt - depth, z0), (x0, yt - depth, z1), (x1, yt - depth, z1), (x1, yt - depth, z0)], grout)
    if border > 0 and border_sides:
        bs, bn = border * ("s" in border_sides), border * ("n" in border_sides)
        bw, be = border * ("w" in border_sides), border * ("e" in border_sides)
        bx = []
        if bs:
            bx.append((x0, z0, x1, z0 + bs))
        if bn:
            bx.append((x0, z1 - bn, x1, z1))
        if bw:
            bx.append((x0, z0 + bs, x0 + bw, z1 - bn))
        if be:
            bx.append((x1 - be, z0 + bs, x1, z1 - bn))
        for (a0, b0, a1, b1) in bx:
            _stones_line(m, rng, (a0, b0, a1, b1), yt, depth, gap, border_color, along="x" if (a1 - a0) > (b1 - b0) else "z")
        x0, z0, x1, z1 = x0 + bw, z0 + bs, x1 - be, z1 - bn
    if along == "x":
        v = z0
        while v < z1 - 0.05:
            h = min(rng.uniform(*row), z1 - v)
            if z1 - (v + h) < 0.35:
                h = z1 - v
            _stones_row(m, rng, x0, x1, v, v + h, yt, depth, gap, length, colors, "x", skip_rect)
            v += h
    else:
        v = x0
        while v < x1 - 0.05:
            h = min(rng.uniform(*row), x1 - v)
            if x1 - (v + h) < 0.35:
                h = x1 - v
            _stones_row(m, rng, z0, z1, v, v + h, yt, depth, gap, length, colors, "z", skip_rect)
            v += h


def _stones_line(m, rng, rect, yt, depth, gap, color, along):
    a0, b0, a1, b1 = rect
    if along == "x":
        _stones_row(m, rng, a0, a1, b0, b1, yt, depth, gap, (0.7, 1.3), [color], "x", None)
    else:
        _stones_row(m, rng, b0, b1, a0, a1, yt, depth, gap, (0.7, 1.3), [color], "z", None)


def _stones_row(m, rng, s0, s1, t0, t1, yt, depth, gap, length, colors, along, skip_rect):
    s = s0
    while s < s1 - 0.05:
        l = min(rng.uniform(*length), s1 - s)
        if s1 - (s + l) < 0.3:
            l = s1 - s
        col = geo.jitter(colors[rng.randrange(len(colors))], rng, 0.05)
        if along == "x":
            r = (s + gap / 2, t0 + gap / 2, s + l - gap / 2, t1 - gap / 2)
        else:
            r = (t0 + gap / 2, s + gap / 2, t1 - gap / 2, s + l - gap / 2)
        if skip_rect and not (r[2] <= skip_rect[0] or r[0] >= skip_rect[2] or r[3] <= skip_rect[1] or r[1] >= skip_rect[3]):
            s += l
            continue
        geo.box(m, ((r[0] + r[2]) / 2, yt - depth / 2, (r[1] + r[3]) / 2), (r[2] - r[0], depth, r[3] - r[1]), col, skip=("bottom",))
        s += l


def planks(m, rng, fp, yt, along, width=(0.26, 0.34), gap=0.016, colors=(P.WOOD, P.WOOD_LIGHT, P.WOOD), grout=P.WOOD_DARK):
    with geo.detail(m, 0.55):
        _planks(m, rng, fp, yt, along, width, gap, colors, grout)


def _planks(m, rng, fp, yt, along, width, gap, colors, grout):
    x0, z0, x1, z1 = fp
    depth = 0.04
    m.poly([(x0, yt - depth, z0), (x0, yt - depth, z1), (x1, yt - depth, z1), (x1, yt - depth, z0)], grout)
    if along == "z":
        v = x0
        while v < x1 - 0.02:
            w = min(rng.uniform(*width), x1 - v)
            if x1 - (v + w) < 0.12:
                w = x1 - v
            _plank_run(m, rng, (v + gap / 2, v + w - gap / 2), (z0, z1), yt, depth, gap, colors, "z")
            v += w
    else:
        v = z0
        while v < z1 - 0.02:
            w = min(rng.uniform(*width), z1 - v)
            if z1 - (v + w) < 0.12:
                w = z1 - v
            _plank_run(m, rng, (v + gap / 2, v + w - gap / 2), (x0, x1), yt, depth, gap, colors, "x")
            v += w


def _plank_run(m, rng, across, span, yt, depth, gap, colors, along):
    s = span[0]
    while s < span[1] - 0.02:
        l = min(rng.uniform(2.2, 4.2), span[1] - s)
        if span[1] - (s + l) < 0.8:
            l = span[1] - s
        col = geo.jitter(colors[rng.randrange(len(colors))], rng, 0.07)
        if along == "z":
            c = ((across[0] + across[1]) / 2, yt - depth / 2, s + l / 2)
            size = (across[1] - across[0], depth, l - gap)
        else:
            c = (s + l / 2, yt - depth / 2, (across[0] + across[1]) / 2)
            size = (l - gap, depth, across[1] - across[0])
        geo.box(m, c, size, col, skip=("bottom",))
        s += l


def earth_roof(m, rng, fp, yt, border=0.4):
    """Tibetan arga roof: smooth tamped earth with a darker raised-look border (flush)."""
    x0, z0, x1, z1 = fp
    nx, nz = max(2, int((x1 - x0) / 0.7)), max(2, int((z1 - z0) / 0.7))

    def col(i, j):
        u, v = (i + 0.5) / nx * (x1 - x0), (j + 0.5) / nz * (z1 - z0)
        edge = min(u, v, (x1 - x0) - u, (z1 - z0) - v)
        base = P.EARTH_ROOF if edge > border else geo.mix(P.EARTH_ROOF, P.MASONRY_DARK, 0.45)
        return geo.jitter(base, rng, 0.035, alpha=0.3)
    geo.top_grid(m, x0, z0, x1, z1, yt, nx, nz, col)


def snow_rock_color(p, seed=0.0):
    """Shared per-vertex snow-on-rock colouring for natural island tops and sides."""
    from mathutils import Vector, noise
    x, y, z = p
    n = noise.noise(Vector((x * 0.5 + seed, y * 0.35, z * 0.5))) + 0.45 * noise.noise(Vector((x * 1.3, y * 1.1 + 2.0, z * 1.3)))
    rock = geo.mix(P.ROCK_WARM, P.ROCK, 0.5 + 0.5 * noise.noise(Vector((x * 0.3, y * 0.3 + 5.0, z * 0.3))))
    snow = geo.mix(P.SNOW_SHADE, P.SNOW, min(1.0, max(0.0, 0.5 + n)))
    t = min(1.0, max(0.0, (n + 0.12) * 4.0))
    return geo.rgba(geo.mix(rock, snow, t), 1.0 - 0.85 * t)


def rock_top(m, rng, fp, yt, snow=0.35):
    """Snow-capped flat rock: drifts cover most of the top, wind-scoured stone shows through."""
    x0, z0, x1, z1 = fp
    nx, nz = max(6, int((x1 - x0) / 0.3)), max(6, int((z1 - z0) / 0.3))
    geo.vertex_grid(m, (x0, yt, z0), (0, 0, z1 - z0), (x1 - x0, 0, 0), nz, nx, snow_rock_color)


# ---------------------------------------------------------------- sides

def masonry(m, rng, fp, y0, y1, colors, course=(0.32, 0.5), block=(0.55, 1.35), mortar=P.GROUT,
            protrude=0.012, skip_sides=(), cap=None, cap_h=0.14, band=None):
    """Ashlar courses. cap: coping colour for the top course; band: (y0, y1, color) painted band."""
    for a, b, n, key in sides(fp):
        if key in skip_sides:
            continue
        L = side_len(a, b)
        wall_quad(m, a, b, n, 0, 1, y0, y1, mortar)
        y = y1
        if cap:
            wall_quad(m, a, b, n, 0, 1, y1 - cap_h, y1, cap, off=protrude)
            y = y1 - cap_h
        if band:
            by0, by1, bcol = band
            wall_quad(m, a, b, n, 0, 1, by0, by1, bcol, off=protrude * 1.5)
        k = 0
        while y > y0 + 0.02:
            h = min(rng.uniform(*course), y - y0)
            if y - h - y0 < 0.15:
                h = y - y0
            s = -rng.uniform(0, 0.6) if k % 2 else 0.0
            while s < L - 0.01:
                w = rng.uniform(*block)
                s0, s1 = max(0.0, s), min(L, s + w)
                if L - s1 < 0.2:
                    s1 = L
                if s1 - s0 > 0.06:
                    yy0, yy1 = y - h + 0.018, y - 0.018
                    if band and not (yy1 <= band[0] or yy0 >= band[1]):
                        s = s1
                        continue
                    col = geo.jitter(colors[rng.randrange(len(colors))], rng, 0.06)
                    wall_quad(m, a, b, n, (s0 + 0.018) / L, (s1 - 0.018) / L, yy0, yy1, col, off=protrude)
                s = s1
            y -= h
            k += 1


def timber_edge(m, rng, fp, y0, y1, paint=P.RED_DARK, joist_colors=((0.14, 0.27, 0.52), (0.14, 0.42, 0.26), (0.74, 0.52, 0.17), (0.6, 0.13, 0.09)), protrude=0.015):
    """Boardwalk edge: painted fascia above a row of colourful joist ends over dark timber."""
    for a, b, n, key in sides(fp):
        L = side_len(a, b)
        wall_quad(m, a, b, n, 0, 1, y0, y1, P.WOOD_DARK)
        top = y1
        wall_quad(m, a, b, n, 0, 1, top - 0.24, top, paint, off=protrude)
        wall_quad(m, a, b, n, 0, 1, top - 0.29, top - 0.24, P.BLACK, off=protrude)
        count = max(2, int(L / 0.42))
        for i in range(count):
            s = (i + 0.5) * L / count
            wall_box(m, a, b, n, s, top - 0.43, 0.13, 0.13, 0.04, joist_colors[i % len(joist_colors)], off=0.0)
        if y1 - y0 > 0.8:
            wall_quad(m, a, b, n, 0, 1, y0, top - 0.6, P.WOOD, off=protrude * 0.5)


def penbe(m, rng, fp, y0, y1, protrude=0.03, medallion_every=1.6, medallions=True):
    """Maroon brushwood frieze (stippled) with gilded medallions, black/white trims."""
    h = y1 - y0
    for a, b, n, key in sides(fp):
        L = side_len(a, b)
        cols = max(4, int(L / 0.09))
        rows = max(2, int(h / 0.11))
        for i in range(cols):
            for j in range(rows):
                col = geo.mix(P.PENBE, P.MAROON, rng.random() * 0.6)
                col = geo.jitter(col, rng, 0.12)
                wall_quad(m, a, b, n, i / cols, (i + 1) / cols, y0 + j * h / rows, y0 + (j + 1) * h / rows, col, off=protrude)
        wall_quad(m, a, b, n, 0, 1, y1, y1 + 0.07, P.BLACK, off=protrude)
        wall_quad(m, a, b, n, 0, 1, y0 - 0.07, y0, P.WHITE, off=protrude)
        wall_quad(m, a, b, n, 0, 1, y0 - 0.12, y0 - 0.07, P.BLACK, off=protrude)
        if medallions and h > 0.35:
            count = max(1, int(L / medallion_every))
            for i in range(count):
                s = (i + 0.5) * L / count
                c = wall_point(a, b, n, s / L, (y0 + y1) / 2, protrude + 0.012)
                _disc(m, c, n, min(0.2, h * 0.36), P.GOLD, "SA_Gloss")


def _disc(m, center, n, radius, color, mat, segments=12):
    t = (-n[1], 0, n[0])  # tangent along the wall (x,z) -> 3d
    pts = []
    for k in range(segments):
        a = 2 * math.pi * k / segments
        pts.append((center[0] + t[0] * math.cos(a) * radius, center[1] + math.sin(a) * radius, center[2] + t[2] * math.cos(a) * radius))
    nn = (n[0], 0, n[1])
    if geo.normal(pts)[0] * nn[0] + geo.normal(pts)[2] * nn[2] < 0:
        pts.reverse()
    m.poly(pts, color, mat)


def whitewash_wall(m, rng, fp, y0, y1, inset=0.1, windows=True, window_rows=None, door_side=None, seed=0, door_clear=2.6,
                   skip_sides_windows=None):
    """Tibetan whitewashed walls with black trapezoid windows, set back `inset` from fp."""
    x0, z0, x1, z1 = fp
    wfp = (x0 + inset, z0 + inset, x1 - inset, z1 - inset)
    h = y1 - y0
    for a, b, n, key in sides(wfp):
        L = side_len(a, b)
        rows = max(2, int(h / 0.6))
        cols = max(2, int(L / 0.9))
        for i in range(cols):
            for j in range(rows):
                t = j / rows
                col = geo.mix(P.WHITEWASH_SHADE, P.WHITEWASH, 0.55 + 0.45 * t)
                wall_quad(m, a, b, n, i / cols, (i + 1) / cols, y0 + j * h / rows, y0 + (j + 1) * h / rows, geo.jitter(col, rng, 0.02, alpha=0.4))
        if not windows or L < 1.6:
            continue
        rws = window_rows or ([y0 + h * 0.55] if h < 4.5 else [y0 + h * 0.32, y0 + h * 0.70])
        count = max(1, int(L / 2.4))
        for ry in rws:
            for i in range(count):
                s = (i + 0.5) * L / count
                if door_side == key and abs(s - L / 2) < door_clear:
                    continue
                if skip_sides_windows and key in skip_sides_windows and abs(s - L / 2) < skip_sides_windows[key]:
                    continue
                window(m, a, b, n, s, ry, 0.62, 0.8)


def window(m, a, b, n, s, yc, w, h, off=0.0):
    """Black trapezoid surround, red frame, dark lattice, painted lintel and a white valance."""
    L = side_len(a, b)
    def P2(ds, y, o):
        return wall_point(a, b, n, (s + ds) / L, y, off + o)
    bw, tw = w / 2 + 0.34, w / 2 + 0.16
    yb, yt = yc - h / 2 - 0.22, yc + h / 2 + 0.06
    m.poly([P2(-bw, yb, 0.012), P2(-tw, yt, 0.012), P2(tw, yt, 0.012), P2(bw, yb, 0.012)], P.BLACK)
    m.poly([P2(-w / 2, yc - h / 2, 0.02), P2(-w / 2, yc + h / 2, 0.02), P2(w / 2, yc + h / 2, 0.02), P2(w / 2, yc - h / 2, 0.02)], P.RED_DARK)
    iw, ih = w / 2 - 0.07, h / 2 - 0.07
    m.poly([P2(-iw, yc - ih, 0.026), P2(-iw, yc + ih, 0.026), P2(iw, yc + ih, 0.026), P2(iw, yc - ih, 0.026)], (0.16, 0.13, 0.12))
    for k in (-1, 0, 1):
        x = k * iw * 0.5
        m.poly([P2(x - 0.018, yc - ih, 0.03), P2(x - 0.018, yc + ih, 0.03), P2(x + 0.018, yc + ih, 0.03), P2(x + 0.018, yc - ih, 0.03)], P.RED)
    m.poly([P2(-iw, yc - 0.018, 0.03), P2(-iw, yc + 0.018, 0.03), P2(iw, yc + 0.018, 0.03), P2(iw, yc - 0.018, 0.03)], P.RED)
    # Two-step painted lintel above and a short striped valance (shambu).
    for k, col in enumerate((P.GREEN, P.OCHRE)):
        y0 = yt + 0.02 + k * 0.08
        wall_box(m, a, b, n, s, y0 + 0.04, w + 0.5 - k * 0.1, 0.08, 0.10 + k * 0.05, col, off=off)
    stripes = [P.WHITE, P.BLUE, P.WHITE, P.RED, P.WHITE]
    vy1 = yt
    for k, col in enumerate(stripes):
        y0 = vy1 - 0.05 * (k + 1)
        m.poly([P2(-w / 2 - 0.12, y0, 0.055), P2(-w / 2 - 0.12, y0 + 0.05, 0.055), P2(w / 2 + 0.12, y0 + 0.05, 0.055), P2(w / 2 + 0.12, y0, 0.055)], col)


def mandala(m, rng, center, R, S, y, segments=96):
    """Coplanar stone-inlay mandala filling the square [c - S, c + S] at height y.

    Polar rings out to radius R, then two rings that follow the square so the
    inlay meets the surrounding flagstones exactly.
    """
    cx, cz = center
    radii = [0.0, 0.07, 0.16, 0.24, 0.42, 0.6, 0.62, 0.71, 0.8, 0.84, 0.92, 1.0]

    def square_r(a, half):
        return half / max(abs(math.cos(a)), abs(math.sin(a)))

    def ring_r(k, a):
        if k < len(radii):
            return radii[k] * R
        return square_r(a, S - 0.32) if k == len(radii) else square_r(a, S)

    nrings = len(radii) + 2
    grid = []
    for k in range(nrings):
        row = []
        for s in range(segments):
            a = 2 * math.pi * s / segments
            r = ring_r(k, a)
            row.append(m.add((cx + r * math.cos(a), y, cz + r * math.sin(a))))
        grid.append(row)
    dirs = {0: P.WHITE, 1: P.OCHRE, 2: P.RED, 3: P.GREEN}  # east, north(+z), west, south in Unity +z north

    def color(k, s):
        a = 2 * math.pi * (s + 0.5) / segments
        rmid = (radii[k] + radii[k + 1]) / 2 if k + 1 < len(radii) else 1.1
        quadrant = int(((a + math.pi / 4) % (2 * math.pi)) // (math.pi / 2))
        if k + 1 >= len(radii) + 1:
            return P.MASONRY_DARK                                  # square border band
        if k + 1 == len(radii):
            return geo.mix(P.TURQUOISE, P.JADE, 0.4)               # corners outside the circle
        if rmid > 0.92:
            return P.RED if (s // 3) % 2 else P.SAFFRON            # ring of fire
        if rmid > 0.84:
            return P.GOLD if s % 4 == 0 else P.BLUE                # vajra ring
        if rmid > 0.8:
            return P.BLACK
        if rmid > 0.62:
            return (0.96, 0.84, 0.80) if (s // 3) % 2 else P.WHITE  # lotus petals
        if rmid > 0.6:
            return P.GOLD
        x, z = math.cos(a) * rmid, math.sin(a) * rmid
        if max(abs(x), abs(z)) > 0.46:
            return P.BLUE                                           # sky between palace and lotus
        if rmid > 0.24:
            return geo.mix(dirs[quadrant], P.WHITE, 0.15)           # palace quarters
        if rmid > 0.16:
            return P.GOLD
        return P.RED if rmid > 0.07 else P.GOLD
    for k in range(nrings - 1):
        for s in range(segments):
            s2 = (s + 1) % segments
            idx = [grid[k][s], grid[k + 1][s], grid[k + 1][s2], grid[k][s2]]
            col = color(k, s)
            mat = "SA_Gloss" if col == P.GOLD else "SA_Stone"
            # CCW (seen from above) polar cell -> reverse for an upward normal
            if mat == "SA_Stone":
                col = geo.mix(geo.mix(col, P.PAVING, 0.5), P.GROUT, 0.12)
            m.face(list(reversed(idx)), geo.jitter(col, rng, 0.03), mat)
