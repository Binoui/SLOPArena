"""Small shared shapes for Fight Night Pier (Unity coordinates, geo.Mesh): planes, panels,
atlas plates and boards, pier piles and bracing, rails, lamp posts, bulb strings, bunting,
beacons, stars and puffs."""
from __future__ import annotations

import math

from stagekit import geo
import palette as P
import textures as T

GROUND = "FNP_Ground"   # the planar ground texture on every walkable top
LIT = "FNP_Lit"         # vertex colour x atlas (geo.WHITE_UV for plain faces)
GLOW = "FNP_Glow"       # StageNeon on the atlas: vertex alpha is the glow mask, animation channels
WATER = "FNP_Water"
SKY = "FNP_Sky"         # StageNeon, unlit and two-sided: the sky dome, clouds and the sun
MATERIALS = [LIT, GROUND, GLOW, WATER, SKY]

STEADY = (0.0, 0.0)     # StageNeon Anim: always on
BLINK = (1.2, 0.0)      # 1.2 Hz blink
SLOW_BLINK = (0.5, 0.0)
PULSE = (51.5, 0.0)     # 1.5 Hz pulse
FLICKER = (-1.0, 0.0)   # faulty-bulb flicker

WATER_FLOOR = -0.2      # every standing solid's base (the KO plane)


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


def box_planes(x0, z0, x1, z1):
    """The four outward planes of a footprint, each with its u range: [(key, plane, u0, u1)]."""
    return [
        ("s", Plane.facing((0.0, 0.0, z0), (0.0, -1.0)), x0, x1),   # south: u runs west -> east
        ("n", Plane.facing((0.0, 0.0, z1), (0.0, 1.0)), -x1, -x0),  # north: u runs east -> west
        ("w", Plane.facing((x0, 0.0, 0.0), (-1.0, 0.0)), -z1, -z0),  # west: u runs north -> south
        ("e", Plane.facing((x1, 0.0, 0.0), (1.0, 0.0)), z0, z1),     # east: u runs south -> north
    ]


def panel(m, plane, u0, v0, u1, v1, color, off=0.0, mat=LIT, uvs=None):
    """Rectangle on the plane (u right, v up) `off` metres proud."""
    quv = None
    if uvs is not None:
        (a0, b0), (a1, b1) = uvs
        quv = [(a0, b0), (a1, b0), (a1, b1), (a0, b1)]
    geo.oriented(m, [plane.at(u0, v0, off), plane.at(u1, v0, off), plane.at(u1, v1, off), plane.at(u0, v1, off)],
                 plane.n, color, mat, quv)


def plate(m, plane, cell, u_center, v_center, width, off=0.012, height=None, color=(1.0, 1.0, 1.0), mat=LIT):
    """An atlas cell on the plane at its own aspect (or `height`)."""
    x, y, w, h = T.CELLS[cell]
    height = height or width * h / w
    panel(m, plane, u_center - width / 2, v_center - height / 2, u_center + width / 2, v_center + height / 2,
          color, off=off, mat=mat, uvs=T.uv(cell))
    return height


def slab(m, plane, u0, v0, u1, v1, depth, color, side=None, mat=LIT, bottom=True):
    """A box standing `depth` proud of the plane over (u0..u1, v0..v1)."""
    sc = side or geo.scale(color, 0.8)
    panel(m, plane, u0, v0, u1, v1, color, off=depth, mat=mat)
    r = plane.r
    for u, d in ((u0, (-r[0], 0.0, -r[2])), (u1, r)):
        geo.oriented(m, [plane.at(u, v0, 0), plane.at(u, v0, depth), plane.at(u, v1, depth), plane.at(u, v1, 0)], d, sc, mat)
    geo.oriented(m, [plane.at(u0, v1, 0), plane.at(u1, v1, 0), plane.at(u1, v1, depth), plane.at(u0, v1, depth)], (0, 1, 0), sc, mat)
    if bottom:
        geo.oriented(m, [plane.at(u0, v0, 0), plane.at(u1, v0, 0), plane.at(u1, v0, depth), plane.at(u0, v0, depth)], (0, -1, 0), sc, mat)


def board(m, plane, cell, u, v, width, frame=P.INK, off=0.012, rim=0.08, depth=0.05):
    """An atlas cell mounted as a board: a frame slab with the cell on its face. Returns its height."""
    x, y, w, h = T.CELLS[cell]
    height = width * h / w
    slab(m, plane, u - width / 2 - rim, v - height / 2 - rim, u + width / 2 + rim, v + height / 2 + rim, depth, frame)
    plate(m, plane, cell, u, v, width, off=depth + off)
    return height


def disc(m, plane, u, v, r, color, off=0.0, n=16, ry=None, mat=LIT):
    """A flat filled ellipse on the plane."""
    ry = r if ry is None else ry
    c = plane.at(u, v, off)
    pts = [plane.at(u + r * math.cos(2 * math.pi * k / n), v + ry * math.sin(2 * math.pi * k / n), off) for k in range(n)]
    for k in range(n):
        geo.oriented(m, [c, pts[k], pts[(k + 1) % n]], plane.n, color, mat)


def textured_disc(m, plane, u, v, r, cell, off=0.0, n=32, mat=LIT):
    """A disc showing a cell's inscribed circle (the wheel hub portrait): no rectangle shows."""
    c = plane.at(u, v, off)
    cu = T.circle_uv(cell, 0.0, 0.0)
    for k in range(n):
        a0, a1 = 2 * math.pi * k / n, 2 * math.pi * (k + 1) / n
        p0 = plane.at(u + r * math.cos(a0), v + r * math.sin(a0), off)
        p1 = plane.at(u + r * math.cos(a1), v + r * math.sin(a1), off)
        geo.oriented(m, [c, p0, p1], plane.n, (1.0, 1.0, 1.0), mat, [cu, T.circle_uv(cell, a0), T.circle_uv(cell, a1)])


def post(m, x, z, y0, y1, w, color, mat=LIT):
    geo.box(m, (x, (y0 + y1) / 2, z), (w, y1 - y0, w), color, mat, skip=("bottom",))


def round_post(m, x, z, y0, y1, r, color, segments=8, mat=LIT, lean=(0.0, 0.0)):
    geo.cylinder_between(m, (x, y0, z), (x + lean[0], y1, z + lean[1]), r, segments, color, mat)


def beam(m, a, b, size, color, mat=LIT):
    """A square bar from a to b."""
    geo.cylinder_between(m, a, b, size * 0.7071, 4, color, mat)


def lattice(m, x, z, half, y0, y1, color, bay=2.0, chord=0.12, brace=0.06):
    """A square lattice column: four chords and zig-zag braces on each face."""
    corners = [(x - half, z - half), (x + half, z - half), (x + half, z + half), (x - half, z + half)]
    for cx, cz in corners:
        geo.box(m, (cx, (y0 + y1) / 2, cz), (chord, y1 - y0, chord), color, LIT)
    n = max(1, int((y1 - y0) / bay))
    for i in range(n):
        ya, yb = y0 + (y1 - y0) * i / n, y0 + (y1 - y0) * (i + 1) / n
        for k in range(4):
            (ax, az), (bx, bz) = corners[k], corners[(k + 1) % 4]
            if i % 2 == 0:
                beam(m, (ax, ya, az), (bx, yb, bz), brace, color)
            else:
                beam(m, (bx, ya, bz), (ax, yb, az), brace, color)
            beam(m, (ax, yb, az), (bx, yb, bz), brace, color)


def truss(m, a, b, depth, color, bays=None, chord=0.09, brace=0.05):
    """A flat two-chord truss from a to b, `depth` deep along +y, zig-zag braced."""
    L = math.dist(a, b)
    n = bays or max(1, int(L / max(0.6, depth * 1.2)))
    top = [(a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t + depth, a[2] + (b[2] - a[2]) * t) for t in [i / n for i in range(n + 1)]]
    bot = [(a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t) for t in [i / n for i in range(n + 1)]]
    beam(m, top[0], top[-1], chord, color)
    beam(m, bot[0], bot[-1], chord, color)
    for i in range(n):
        beam(m, bot[i], top[i + 1] if i % 2 == 0 else top[i], brace, color)
        beam(m, top[i], bot[i + 1] if i % 2 == 0 else bot[i], brace, color)


# ------------------------------------------------------------------ glow

def glow(m, anim=STEADY, phase=0.0):
    """Faces added inside glow on the StageNeon material (sample LAMP_UV with them)."""
    return geo.state(m, alpha=1.0, anim=(anim[0], phase))


def lamp_uvs(n):
    return [T.LAMP_UV] * n


def glow_ellipsoid(m, center, radii, color, rings=4, segments=8):
    """An ellipsoid that glows: every corner samples the lamp block."""
    first = len(m.faces)
    geo.ellipsoid(m, center, radii, color, GLOW, rings=rings, segments=segments)
    for fi in range(first, len(m.faces)):
        m.uvs[fi] = lamp_uvs(len(m.faces[fi]))


def bulb(m, center, r=0.07, color=P.BULB, anim=STEADY, phase=0.0):
    with glow(m, anim, phase):
        glow_ellipsoid(m, center, (r, r, r), color, rings=3, segments=6)


def bulb_line(m, a, b, spacing, r=0.07, color=P.BULB, sag=0.0, wire=P.INK, chase=None, phase=0.0):
    """Bulbs along a (sagging) wire from a to b. `chase` (Hz) makes them chase along the line."""
    L = math.dist(a, b)
    n = max(2, int(L / spacing))
    pts = geo.catenary(a, b, sag, n)
    if wire is not None and L > 0.2:
        geo.tube(m, pts, 0.012, 3, wire, LIT)
    for i, p in enumerate(pts):
        anim = (chase, 0.0) if chase else STEADY
        bulb(m, p, r, color, anim, (phase + i / max(1, n)) % 1.0)


def star(m, center, normal, r_out, r_in, points, color, spin=0.0, mat=GLOW):
    """A flat star facing `normal` (nx, nz) or up when normal is None (two-sided)."""
    cx, cy, cz = center
    pts = []
    for k in range(points * 2):
        a = spin + math.pi * k / points
        r = r_out if k % 2 == 0 else r_in
        if normal is None:
            pts.append((cx + math.cos(a) * r, cy, cz + math.sin(a) * r))
        else:
            nx, nz = normal
            rx, rz = -nz, nx
            pts.append((cx + rx * math.cos(a) * r, cy + math.sin(a) * r, cz + rz * math.cos(a) * r))
    n = (0.0, 1.0, 0.0) if normal is None else (normal[0], 0.0, normal[1])
    uv = lamp_uvs(3) if mat == GLOW else None
    for k in range(len(pts)):
        tri = [center, pts[k], pts[(k + 1) % len(pts)]]
        geo.oriented(m, tri, n, color, mat, uv)
        geo.oriented(m, tri, tuple(-c for c in n), color, mat, uv)


def beacon(m, center, radius=0.2, color=P.BUOY_RED, anim=BLINK, phase=0.0):
    """A warning lamp: a glowing (blinking) dome on a dark cap."""
    cx, cy, cz = center
    geo.box(m, (cx, cy - radius * 0.9, cz), (radius * 1.7, radius * 0.5, radius * 1.7), P.INK, LIT, skip=("bottom",))
    with glow(m, anim, phase):
        glow_ellipsoid(m, center, (radius, radius * 0.9, radius), color)


def lamp_post(m, x, z, y0, height=4.2, color=P.GI_BLUE, globe=P.BULB, twin=False, phase=0.0, axis="x"):
    """A Victorian pier lamp: fluted post, a crossbar (along `axis`) and glowing globes."""
    geo.lathe(m, (x, y0, z), [(0.16, 0.0), (0.16, 0.25), (0.09, 0.4), (0.07, height * 0.9), (0.1, height * 0.94)], 8,
              color, LIT, cap_top=True)
    top = y0 + height
    if twin:
        ax, az = (0.55, 0.0) if axis == "x" else (0.0, 0.55)
        beam(m, (x - ax, top - 0.25, z - az), (x + ax, top - 0.25, z + az), 0.06, color)
        for s in (-1.0, 1.0):
            bulb(m, (x + s * ax, top - 0.02, z + s * az), 0.2, globe, STEADY, phase)
    else:
        bulb(m, (x, top + 0.12, z), 0.22, globe, STEADY, phase)


def pennants(m, a, b, size, colors, sag=0.25, spacing=0.55):
    """Triangle bunting along a sagging line from a to b, two-sided."""
    L = math.dist(a, b)
    n = max(2, int(L / spacing))
    pts = geo.catenary(a, b, sag, n)
    geo.tube(m, pts, 0.01, 3, P.INK, LIT)
    for i in range(n):
        p, q = pts[i], pts[i + 1]
        mid = ((p[0] + q[0]) / 2, (p[1] + q[1]) / 2 - size, (p[2] + q[2]) / 2)
        c = colors[i % len(colors)]
        d = (q[0] - p[0], q[2] - p[2])
        nrm = (-d[1], 0.0, d[0])
        tri = [p, q, mid]
        geo.oriented(m, tri, nrm, c, LIT)
        geo.oriented(m, tri, tuple(-v for v in nrm), c, LIT)


def puff(m, center, r, color=(0.6, 0.6, 0.62), seed=0, lumps=5, mat=LIT):
    """A cartoon cloud: a few overlapping squashed spheres."""
    cx, cy, cz = center
    for k in range(lumps):
        a = seed * 1.7 + k * 2.39996
        rr = r * (0.55 + 0.35 * ((k * 37 + seed * 11) % 7) / 6)
        off = r * 0.55 * (k / max(1, lumps - 1))
        c = geo.scale(color, 0.92 + 0.16 * ((k * 13 + seed) % 5) / 4)
        geo.ellipsoid(m, (cx + math.cos(a) * off, cy + rr * 0.35 + k * r * 0.12, cz + math.sin(a) * off),
                      (rr, rr * 0.82, rr), c, mat, rings=5, segments=9)


def awning(m, x0, x1, z, y, depth, drop, cell="awning", facing=-1.0):
    """A striped awning along x from x0 to x1 at height y, sloping `depth` out towards
    `facing` (-1 south, +1 north) and dropping `drop`, with a scalloped valance."""
    zf = z + facing * depth
    (a0, b0), (a1, b1) = T.uv(cell)
    pts = [(x0, y, z), (x1, y, z), (x1, y - drop, zf), (x0, y - drop, zf)]
    geo.oriented(m, pts, (0.0, 1.0, facing * 0.6), (1.0, 1.0, 1.0), LIT, [(a0, b1), (a1, b1), (a1, b0), (a0, b0)])
    geo.oriented(m, list(reversed(pts)), (0.0, -1.0, -facing * 0.6), (0.8, 0.8, 0.8), LIT,
                 [(a0, b0), (a1, b0), (a1, b1), (a0, b1)])
    n = max(2, int((x1 - x0) / 0.5))
    for i in range(n):
        xa, xb = x0 + (x1 - x0) * i / n, x0 + (x1 - x0) * (i + 1) / n
        c = P.AWNING_RED if i % 2 == 0 else P.AWNING_WHITE
        if cell == "awning_blue":
            c = P.GI_BLUE if i % 2 == 0 else P.GOLD
        tri = [(xa, y - drop, zf), (xb, y - drop, zf), ((xa + xb) / 2, y - drop - 0.22, zf)]
        geo.oriented(m, tri, (0.0, 0.0, facing), c, LIT)
        geo.oriented(m, tri, (0.0, 0.0, -facing), c, LIT)
