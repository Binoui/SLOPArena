"""Prayer-flag strings, the giant thangka and FightGuy's red training ribbons.

Cloth faces use the SA_Cloth material. Vertex alpha is the wind weight: 0 where
the cloth is attached, 1 at its free edge.
"""
from __future__ import annotations

import math

from stagekit import geo
import palette as P

# Atlas regions (UV, origin bottom-left), see textures.py
FLAG_CELLS = 4            # 4 x 4 print cells in u 0.5..1, v 0..0.5
THANGKA_UV = (0.0, 0.0, 0.5, 1344 / 2048)


def flag_cell_uv(index):
    i, j = index % FLAG_CELLS, (index // FLAG_CELLS) % FLAG_CELLS
    size = 0.5 / FLAG_CELLS
    u0, v0 = 0.5 + i * size, j * size
    pad = size * 0.04
    return (u0 + pad, v0 + pad, u0 + size - pad, v0 + size - pad)


def string(m, rng, p0, p1, sag, spacing=0.4, fw=0.3, fh=0.27, wind=(0.35, 0.0, 0.15), color_offset=0):
    """A sagging cord with five-colour flags hanging from it."""
    length = math.dist(p0, p1)
    segs = max(4, int(length / 0.8))
    pts = geo.catenary(p0, p1, sag, segs)
    for a, b in zip(pts, pts[1:]):
        geo.cylinder_between(m, a, b, 0.011, 3, P.WOOD_DARK, caps=False)
    # resample by arc length
    arc = [0.0]
    for a, b in zip(pts, pts[1:]):
        arc.append(arc[-1] + math.dist(a, b))
    total = arc[-1]
    count = int(total / spacing)
    seg = 0
    for k in range(1, count):
        s = k * spacing + rng.uniform(-0.03, 0.03)
        while seg < len(arc) - 2 and arc[seg + 1] < s:
            seg += 1
        t = (s - arc[seg]) / max(1e-6, arc[seg + 1] - arc[seg])
        a, b = pts[seg], pts[seg + 1]
        p = tuple(a[i] + (b[i] - a[i]) * t for i in range(3))
        d = geo._norm(tuple(b[i] - a[i] for i in range(3)))
        color = P.FLAG_COLORS[(k + color_offset) % 5]
        sway = rng.uniform(0.6, 1.2)
        w = (wind[0] * sway, -1.0, wind[2] * sway)
        tl = tuple(p[i] - d[i] * fw / 2 for i in range(3))
        tr = tuple(p[i] + d[i] * fw / 2 for i in range(3))
        bl = tuple(tl[i] + w[i] * fh for i in range(3))
        br = tuple(tr[i] + w[i] * fh for i in range(3))
        u0, v0, u1, v1 = flag_cell_uv(rng.randrange(16))
        m.poly([bl, tl, tr, br], [geo.rgba(color, 1.0), geo.rgba(color, 0.0), geo.rgba(color, 0.0), geo.rgba(color, 1.0)],
               "SA_Cloth", [(u0, v0), (u0, v1), (u1, v1), (u1, v0)])
    return pts


def thangka(m, top_left, top_right, height, cols=6, rows=10, sway=0.25):
    """Giant festival thangka hanging flat against a wall (face direction from the corners)."""
    a, b = top_left, top_right
    u0, v0, u1, v1 = THANGKA_UV
    for j in range(rows):
        for i in range(cols):
            def corner(ii, jj):
                t = ii / cols
                y = a[1] - height * jj / rows
                p = (a[0] + (b[0] - a[0]) * t, y, a[2] + (b[2] - a[2]) * t)
                return p
            q = [corner(i, j + 1), corner(i, j), corner(i + 1, j), corner(i + 1, j + 1)]
            alpha = [sway * (j + 1) / rows, sway * j / rows, sway * j / rows, sway * (j + 1) / rows]
            uv = [(u0 + (u1 - u0) * i / cols, v1 - (v1 - v0) * (j + 1) / rows), (u0 + (u1 - u0) * i / cols, v1 - (v1 - v0) * j / rows),
                  (u0 + (u1 - u0) * (i + 1) / cols, v1 - (v1 - v0) * j / rows), (u0 + (u1 - u0) * (i + 1) / cols, v1 - (v1 - v0) * (j + 1) / rows)]
            m.poly(q, [geo.rgba(P.WHITE, al) for al in alpha], "SA_Cloth", uv)


def ribbon_band(m, rng, fp, y, height=0.13, off=0.03, knot_side="s", tail_dir=(1.0, 0.0, -0.4)):
    """FightGuy's red headband tied around a training post, two tails streaming downwind."""
    import dressing as D
    for a, b, n, key in D.sides(fp):
        D.wall_quad(m, a, b, n, -off / D.side_len(a, b), 1 + off / D.side_len(a, b), y - height / 2, y + height / 2, P.RED, off=off)
        if key == knot_side:
            L = D.side_len(a, b)
            c = D.wall_point(a, b, n, 0.72, y, off + 0.05)
            geo.box(m, c, (0.16 if abs(n[1]) else 0.1, 0.17, 0.1 if abs(n[1]) else 0.16), P.RED_DARK)
            for k, spread in enumerate((-0.25, 0.3)):
                _tail(m, c, n, tail_dir, spread, 1.15 + 0.2 * k, rng)


def _tail(m, start, n, wind, spread, length, rng, width=0.11, segs=8):
    pts = []
    for i in range(segs + 1):
        t = i / segs
        x = start[0] + (n[0] * 0.35 + wind[0]) * length * t + spread * t * 0.4
        z = start[2] + (n[1] * 0.35 + wind[2]) * length * t
        y = start[1] - (0.15 + 0.55 * t) * length * t + math.sin(t * 6 + spread * 5) * 0.06
        pts.append((x, y, z))
    for i in range(segs):
        a, b = pts[i], pts[i + 1]
        d = geo._norm(tuple(b[k] - a[k] for k in range(3)))
        side = geo._norm(geo._cross(d, (0, 1, 0))) if abs(d[1]) < 0.95 else (1, 0, 0)
        w0, w1 = width * (1 - 0.3 * i / segs), width * (1 - 0.3 * (i + 1) / segs)
        q = [tuple(a[k] - side[k] * w0 / 2 for k in range(3)), tuple(a[k] + side[k] * w0 / 2 for k in range(3)),
             tuple(b[k] + side[k] * w1 / 2 for k in range(3)), tuple(b[k] - side[k] * w1 / 2 for k in range(3))]
        al0, al1 = 0.15 + 0.85 * i / segs, 0.15 + 0.85 * (i + 1) / segs
        m.poly(q, [geo.rgba(P.RED, al0), geo.rgba(P.RED, al0), geo.rgba(P.RED, al1), geo.rgba(P.RED, al1)], "SA_Cloth")


def ribbon_ring(m, rng, outline, y, height=0.14, off=0.03, knot_index=0, tail_dir=(1.0, 0.0, -0.4)):
    """Red headband tied around a polygonal post (outline CCW seen from above)."""
    cx = sum(p[0] for p in outline) / len(outline)
    cz = sum(p[1] for p in outline) / len(outline)
    ring = []
    for px, pz in outline:
        d = math.hypot(px - cx, pz - cz) or 1.0
        ring.append((px + (px - cx) / d * off, pz + (pz - cz) / d * off))
    n = len(ring)
    for i in range(n):
        (ax, az), (bx, bz) = ring[i], ring[(i + 1) % n]
        m.poly([(ax, y - height / 2, az), (ax, y + height / 2, az), (bx, y + height / 2, bz), (bx, y - height / 2, bz)], P.RED)
    (ax, az), (bx, bz) = ring[knot_index], ring[(knot_index + 1) % n]
    mx, mz = (ax + bx) / 2, (az + bz) / 2
    nx, nz = mx - cx, mz - cz
    d = math.hypot(nx, nz) or 1.0
    nx, nz = nx / d, nz / d
    c = (mx + nx * 0.06, y, mz + nz * 0.06)
    geo.box(m, c, (0.14, 0.18, 0.14), P.RED_DARK)
    for k, spread in enumerate((-0.25, 0.3)):
        _tail(m, c, (nx, nz), tail_dir, spread, 1.2 + 0.25 * k, rng)
