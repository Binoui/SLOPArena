"""Small shared shapes for the Fighter's License generator: wall planes, flush panels,
atlas plates, cones, guardrail and window grids (Unity coordinates, geo.Mesh)."""
from __future__ import annotations

import math

from stagekit import geo
import palette as P
import textures as T

LIT = "LT_Lit"           # vertex colour x atlas (geo.WHITE_UV for plain faces)
GLASS = "LT_Glass"       # the same atlas with a glossy highlight
CONCRETE = "LT_Concrete"  # tiling board-formed concrete, UVs in metres / 2.4
BRICK = "LT_Brick"       # tiling brick, UVs in metres / 1.8
CONCRETE_TILE = 2.4
BRICK_TILE = 1.8


class Plane:
    """A vertical plane: origin, horizontal unit `right` and outward unit `normal`.
    `facing` keeps text readable for a viewer looking against the normal."""

    def __init__(self, origin, right, normal):
        self.o, self.r, self.n = origin, right, normal

    @staticmethod
    def facing(origin, normal):
        nx, nz = normal
        L = math.hypot(nx, nz)
        nx, nz = nx / L, nz / L
        return Plane(origin, (-nz, 0.0, nx), (nx, 0.0, nz))

    def at(self, u, v, off=0.0):
        o, r, n = self.o, self.r, self.n
        return (o[0] + r[0] * u + n[0] * off, o[1] + v, o[2] + r[2] * u + n[2] * off)


def panel(m, plane, u0, v0, u1, v1, color, off=0.0, mat=LIT, uvs=None, depth=0.0, side_color=None):
    """Rectangle on the plane (u right, v up) `off` metres proud; optional `depth` sides behind it."""
    quv = None
    if uvs is not None:
        (a0, b0), (a1, b1) = uvs
        quv = [(a0, b0), (a1, b0), (a1, b1), (a0, b1)]
    geo.oriented(m, [plane.at(u0, v0, off), plane.at(u1, v0, off), plane.at(u1, v1, off), plane.at(u0, v1, off)],
                 plane.n, color, mat, quv)
    if depth > 0:
        sc = side_color or geo.scale(color[:3], 0.75)
        for (a, b) in (((u0, v0), (u1, v0)), ((u1, v0), (u1, v1)), ((u1, v1), (u0, v1)), ((u0, v1), (u0, v0))):
            pts = [plane.at(a[0], a[1], off), plane.at(b[0], b[1], off), plane.at(b[0], b[1], off - depth),
                   plane.at(a[0], a[1], off - depth)]
            mid = ((a[0] + b[0]) / 2 - (u0 + u1) / 2, (a[1] + b[1]) / 2 - (v0 + v1) / 2)
            out = (plane.r[0] * mid[0], mid[1], plane.r[2] * mid[0])
            geo.oriented(m, pts, out, sc, mat)


def plate(m, plane, cell, u_center, v_center, width, off=0.01, height=None, color=(1.0, 1.0, 1.0), frame=None, mat=LIT):
    """An atlas cell on the plane at its own aspect (or `height`), optionally framed."""
    x, y, w, h = T.CELLS[cell]
    height = height or width * h / w
    u0, u1 = u_center - width / 2, u_center + width / 2
    v0, v1 = v_center - height / 2, v_center + height / 2
    if frame:
        b = 0.05
        panel(m, plane, u0 - b, v0 - b, u1 + b, v1 + b, frame, off=off - 0.004, mat=LIT)
    panel(m, plane, u0, v0, u1, v1, color, off=off, mat=mat, uvs=T.uv(cell))
    return u0, v0, u1, v1


def tiled_panel(m, plane, u0, v0, u1, v1, color, mat, tile, off=0.0, u_base=0.0):
    """Panel whose UVs are metres / tile (for the tiling brick and concrete materials)."""
    uvs = ((u_base + u0) / tile, v0 / tile), ((u_base + u1) / tile, v1 / tile)
    panel(m, plane, u0, v0, u1, v1, color, off=off, mat=mat, uvs=uvs)


def stripes(m, plane, u0, v0, u1, v1, a, b, period=0.4, off=0.006, slant=1.0, mat=LIT):
    """Diagonal hazard stripes (colours a and b) clipped to the rectangle."""
    panel(m, plane, u0, v0, u1, v1, a, off=off, mat=mat)
    h = v1 - v0
    s = u0 - h * slant
    while s < u1:
        pts = []
        for uu, vv in ((s, v0), (s + period / 2, v0), (s + period / 2 + h * slant, v1), (s + h * slant, v1)):
            pts.append((uu, vv))
        clipped = _clip_u(pts, u0, u1)
        if len(clipped) >= 3:
            geo.oriented(m, [plane.at(uu, vv, off + 0.002) for uu, vv in clipped], plane.n, b, mat)
        s += period


def _clip_u(pts, u0, u1):
    for bound, sign in ((u0, 1), (u1, -1)):
        out = []
        n = len(pts)
        for i in range(n):
            p, q = pts[i], pts[(i + 1) % n]
            dp, dq = sign * (p[0] - bound), sign * (q[0] - bound)
            if dp >= 0:
                out.append(p)
            if (dp >= 0) != (dq >= 0):
                t = dp / (dp - dq)
                out.append((p[0] + (q[0] - p[0]) * t, p[1] + (q[1] - p[1]) * t))
        pts = out
        if not pts:
            break
    return pts


def cone(m, base, height=0.7, radius=0.19, segments=10, yaw=0.0, tilt=None, mat=LIT):
    """A traffic cone: square rubber base, orange body with two reflective white bands."""
    x, y, z = base
    geo.box(m, (x, y + 0.02, z), (radius * 2.3, 0.04, radius * 2.3), geo.scale(P.CONE, 0.75), mat, yaw=yaw, skip=("bottom",))
    profile = [(radius, 0.04), (radius * 0.86, height * 0.30), (radius * 0.72, height * 0.47), (radius * 0.58, height * 0.66),
               (radius * 0.44, height * 0.84), (radius * 0.16, height)]
    colors = [P.CONE, P.CONE_BAND, P.CONE, P.CONE_BAND, P.CONE]
    first = len(m.faces)
    geo.lathe(m, (x, y, z), profile, segments, colors, mat, yaw=yaw)
    if tilt:
        _tilt(m, first, (x, y, z), tilt)


def _tilt(m, first_face, pivot, tilt):
    """Rotate the vertices of faces added since first_face about `pivot` (axis-angle in the XZ plane)."""
    (ax, az), angle = tilt
    L = math.hypot(ax, az) or 1.0
    ax, az = ax / L, az / L
    c, s = math.cos(angle), math.sin(angle)
    seen = set()
    for f in m.faces[first_face:]:
        for i in f:
            if i in seen:
                continue
            seen.add(i)
            px, py, pz = (m.verts[i][k] - pivot[k] for k in range(3))
            # Rodrigues rotation about the horizontal axis (ax, 0, az).
            dot = px * ax + pz * az
            cx, cy, cz = (0 * pz - az * py, az * px - ax * pz, ax * py - 0 * px)
            nx = px * c + cx * s + ax * dot * (1 - c)
            ny = py * c + cy * s
            nz = pz * c + cz * s + az * dot * (1 - c)
            m.verts[i] = (nx + pivot[0], ny + pivot[1], nz + pivot[2])


def ribbon(m, pts, half_width, up, color, mat=LIT, back_color=None):
    """A flat strip through `pts` (3D), widened along `up` (3D unit) at every point; two-sided."""
    tops = [tuple(p[k] + up[k] * half_width for k in range(3)) for p in pts]
    bots = [tuple(p[k] - up[k] * half_width for k in range(3)) for p in pts]
    for i in range(len(pts) - 1):
        quad = [bots[i], bots[i + 1], tops[i + 1], tops[i]]
        n = geo.normal(quad)
        m.poly(quad, color, mat)
        m.poly(list(reversed(quad)), back_color or geo.scale(color, 0.7), mat)
    return n


def window_grid(m, plane, u0, u1, v0, v1, bay, floor, cell, color, off=0.01, sill=0.9, head=0.6, frame=None,
                mat=GLASS, skip=None):
    """Repeat an atlas window cell over a facade area: bays `bay` wide, storeys `floor` high."""
    n_u = max(1, int((u1 - u0) / bay))
    n_v = max(1, int((v1 - v0) / floor))
    bw = (u1 - u0) / n_u
    count = 0
    for j in range(n_v):
        for i in range(n_u):
            if skip and skip(i, j):
                continue
            a0, a1 = u0 + i * bw + bw * 0.12, u0 + (i + 1) * bw - bw * 0.12
            b0, b1 = v0 + j * floor + sill, v0 + (j + 1) * floor - head
            if frame:
                panel(m, plane, a0 - 0.06, b0 - 0.06, a1 + 0.06, b1 + 0.06, frame, off=off - 0.006, mat=LIT)
            panel(m, plane, a0, b0, a1, b1, color, off=off, mat=mat, uvs=T.uv(cell))
            count += 1
    return count
