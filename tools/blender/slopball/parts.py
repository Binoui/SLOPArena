"""Building blocks for the Night Court: wall frames, shaded panels, ribbons and chain-link.

Coordinates are Unity world space (+X east, +Y up, +Z north). A `Side` maps a local
frame (u to the right of a viewer inside the court facing that wall, y up, w outward
from the court centre) onto one of the four walls, so each wall is authored once.
Side and chain_link follow tools/blender/slop_pit/parts.py.
"""
from __future__ import annotations

import math

from stagekit import geo


class Side:
    """Wall k (0 north/+Z, 1 east/+X, 2 south/-Z, 3 west/-X); +w points away from the court."""

    NAMES = ("N", "E", "S", "W")

    def __init__(self, k):
        self.k = k
        self.name = self.NAMES[k]
        self.yaw = k * math.pi / 2

    def xz(self, u, w):
        return ((u, w), (w, -u), (-u, -w), (-w, u))[self.k]

    def P(self, u, y, w):
        x, z = self.xz(u, w)
        return (x, y, z)

    def vec(self, du, dy, dw):
        x, z = self.xz(du, dw)
        return (x, dy, z)

    @property
    def inward(self):
        return self.vec(0.0, 0.0, -1.0)


SIDES = [Side(k) for k in range(4)]


def add(a, b, k=1.0):
    return tuple(a[i] + b[i] * k for i in range(3))


def panel(m, bl, right, up, nu, nv, uvrect, color_at, mat=None):
    """Grid of nu x nv quads from bottom-left `bl` spanning `right` and `up` (Unity vectors),
    facing the viewer for whom `right` points right. UVs interpolate across uvrect
    (u0, v0, u1, v1); color_at(point, normal) gives each vertex an rgb/rgba colour."""
    n = geo._norm(geo._cross(up, right))
    rows, cols = [], []
    u0, v0, u1, v1 = uvrect
    for i in range(nu + 1):
        row, crow = [], []
        for j in range(nv + 1):
            p = add(add(bl, right, i / nu), up, j / nv)
            row.append(p)
            c = color_at(p, n)
            crow.append(geo.rgba(c, m.alpha) if len(c) == 3 else tuple(c))
        rows.append(row)
        cols.append(crow)
    for i in range(nu):
        for j in range(nv):
            q = [(i, j), (i, j + 1), (i + 1, j + 1), (i + 1, j)]  # BL, TL, TR, BR: front faces the viewer
            uvs = [(u0 + (u1 - u0) * a / nu, v0 + (v1 - v0) * b / nv) for a, b in q]
            m.poly([rows[a][b] for a, b in q], [cols[a][b] for a, b in q], mat, uvs)


def box(m, center, size, color, mat=None, **kw):
    geo.box(m, center, size, color, mat, **kw)


def ribbon(m, a, b, width, normal, color, mat=None):
    """Flat strip from a to b, `width` wide, facing `normal` (wires, ropes, cables)."""
    d = [b[i] - a[i] for i in range(3)]
    L = math.sqrt(sum(x * x for x in d)) or 1e-6
    d = [x / L for x in d]
    s = geo._norm(geo._cross(normal, d))
    h = width / 2
    pts = [tuple(a[i] - s[i] * h for i in range(3)), tuple(b[i] - s[i] * h for i in range(3)),
           tuple(b[i] + s[i] * h for i in range(3)), tuple(a[i] + s[i] * h for i in range(3))]
    geo.oriented(m, pts, normal, color, mat)


def beam(m, a, b, size, color, mat=None, caps=False):
    """Square-section bar between two points (4-sided tube)."""
    geo.cylinder_between(m, a, b, size * 0.7071, 4, color, mat, caps=caps)


def _clip_line(p, d, u0, u1, y0, y1):
    t0, t1 = -1e9, 1e9
    for axis, lo, hi in ((0, u0, u1), (1, y0, y1)):
        if abs(d[axis]) < 1e-12:
            if p[axis] < lo or p[axis] > hi:
                return None
            continue
        a, b = (lo - p[axis]) / d[axis], (hi - p[axis]) / d[axis]
        t0, t1 = max(t0, min(a, b)), min(t1, max(a, b))
    if t1 - t0 < 1e-3:
        return None
    return (p[0] + d[0] * t0, p[1] + d[1] * t0), (p[0] + d[0] * t1, p[1] + d[1] * t1)


def chain_link(m, side, u0, u1, y0, y1, w, spacing, wire, color_at, mat=None, pieces=1.5):
    """Diamond mesh at depth w facing the court: two families of 45-degree wires, each cut
    into ~`pieces`-metre ribbons so baked light can vary along them."""
    n = side.inward
    for d in ((1.0, 1.0), (1.0, -1.0)):
        L = math.sqrt(2.0)
        dd = (d[0] / L, d[1] / L)
        perp = (-dd[1], dd[0])
        corners = [(u0, y0), (u1, y0), (u1, y1), (u0, y1)]
        cs = [c[0] * perp[0] + c[1] * perp[1] for c in corners]
        k0, k1 = math.floor(min(cs) / spacing), math.ceil(max(cs) / spacing)
        for k in range(k0, k1 + 1):
            c = k * spacing
            seg = _clip_line((perp[0] * c, perp[1] * c), dd, u0, u1, y0, y1)
            if seg is None:
                continue
            (a0, b0), (a1, b1) = seg
            length = math.hypot(a1 - a0, b1 - b0)
            steps = max(1, int(round(length / pieces)))
            for s in range(steps):
                t0, t1 = s / steps, (s + 1) / steps
                pa = side.P(a0 + (a1 - a0) * t0, b0 + (b1 - b0) * t0, w)
                pb = side.P(a0 + (a1 - a0) * t1, b0 + (b1 - b0) * t1, w)
                mid = tuple((pa[i] + pb[i]) / 2 for i in range(3))
                ribbon(m, pa, pb, wire, n, color_at(mid, n), mat)
