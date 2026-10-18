"""Small shared shapes for Kaboom Quarry (Unity coordinates, geo.Mesh): wall planes, flush
panels, atlas plates, rock faces, drums, TNT crates, tyres, cones, beacons, sparks and
cartoon smoke."""
from __future__ import annotations

import math

from stagekit import geo
import palette as P
import textures as T

GROUND = "MQ_Ground"   # the planar ground texture on every walkable top
LIT = "MQ_Lit"         # vertex colour x atlas (geo.WHITE_UV for plain faces)
ROCK = "MQ_Rock"       # tiling strata, UVs in metres / ROCK_TILE
GLOW = "MQ_Glow"       # StageNeon on the atlas: vertex alpha is the glow mask, animation channels
WATER = "MQ_Water"
ROCK_TILE = 7.0
MATERIALS = [LIT, GROUND, ROCK, GLOW, WATER]

STEADY = (0.0, 0.0)     # StageNeon Anim: always on
BLINK = (1.2, 0.0)      # StageNeon Anim: 1.2 Hz blink
PULSE = (51.5, 0.0)     # StageNeon Anim: 1.5 Hz pulse
FLICKER = (-1.0, 0.0)   # StageNeon Anim: faulty-tube flicker


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


def rock_face(m, plane, u0, v0, u1, v1, color, off=0.0, step=3.0, u_base=0.0, jitter=0.08, seed=0):
    """A rock wall on the plane, tiled with the strata texture and broken into a grid whose
    vertex colours wander a little (never displaced: the face stays flush)."""
    nu = max(1, int(math.ceil((u1 - u0) / step)))
    nv = max(1, int(math.ceil((v1 - v0) / step)))
    for i in range(nu):
        for j in range(nv):
            a0, a1 = u0 + (u1 - u0) * i / nu, u0 + (u1 - u0) * (i + 1) / nu
            b0, b1 = v0 + (v1 - v0) * j / nv, v0 + (v1 - v0) * (j + 1) / nv
            k = 1.0 + jitter * math.sin(1.7 * (i + seed) + 2.3 * j + seed * 0.31) * math.cos(0.9 * i - 1.3 * j)
            uvs = [((u_base + a) / ROCK_TILE, b / ROCK_TILE) for a, b in ((a0, b0), (a1, b0), (a1, b1), (a0, b1))]
            geo.oriented(m, [plane.at(a0, b0, off), plane.at(a1, b0, off), plane.at(a1, b1, off), plane.at(a0, b1, off)],
                         plane.n, geo.scale(color, k), ROCK, uvs)


def box_planes(x0, z0, x1, z1):
    """The four outward planes of a footprint, each with its u range: [(plane, u0, u1)]."""
    return [
        (Plane.facing((0.0, 0.0, z0), (0.0, -1.0)), x0, x1),   # south: u runs west -> east
        (Plane.facing((0.0, 0.0, z1), (0.0, 1.0)), -x1, -x0),  # north: u runs east -> west
        (Plane.facing((x0, 0.0, 0.0), (-1.0, 0.0)), -z1, -z0),  # west: u runs north -> south
        (Plane.facing((x1, 0.0, 0.0), (1.0, 0.0)), z0, z1),     # east: u runs south -> north
    ]


def drum(m, base, radius=0.42, height=1.0, color=P.DRUM_COLORS[0], mat=LIT, cap=True):
    """An upright oil drum with two rolled rims."""
    dark = geo.scale(color, 0.7)
    profile = [(radius, 0.0), (radius, height * 0.30), (radius * 1.03, height * 0.31), (radius * 1.03, height * 0.34),
               (radius, height * 0.35), (radius, height * 0.65), (radius * 1.03, height * 0.66), (radius * 1.03, height * 0.69),
               (radius, height * 0.70), (radius, height)]
    colors = [color, dark, dark, dark, color, dark, dark, dark, color]
    geo.lathe(m, base, profile, 12, colors, mat, cap_top=cap)


def lying_drum(m, center, radius, length, yaw, color, mat=LIT):
    cx, cy, cz = center
    dx, dz = math.cos(yaw) * length / 2, math.sin(yaw) * length / 2
    geo.cylinder_between(m, (cx - dx, cy, cz - dz), (cx + dx, cy, cz + dz), radius, 10, color, mat)


def tnt_crate(m, center, size=1.0, yaw=0.0):
    """A wooden TNT crate: atlas faces on every side and the lid."""
    cx, cy, cz = center
    h = size / 2
    c, s = math.cos(yaw), math.sin(yaw)

    def P_(x, y, z):
        return (cx + x * c + z * s, cy + y, cz - x * s + z * c)
    (a0, b0), (a1, b1) = T.uv("tnt")
    quv = [(a0, b0), (a1, b0), (a1, b1), (a0, b1)]
    faces = [
        ([P_(-h, -h, -h), P_(h, -h, -h), P_(h, h, -h), P_(-h, h, -h)], (0, 0, -1)),
        ([P_(h, -h, h), P_(-h, -h, h), P_(-h, h, h), P_(h, h, h)], (0, 0, 1)),
        ([P_(-h, -h, h), P_(-h, -h, -h), P_(-h, h, -h), P_(-h, h, h)], (-1, 0, 0)),
        ([P_(h, -h, -h), P_(h, -h, h), P_(h, h, h), P_(h, h, -h)], (1, 0, 0)),
        ([P_(-h, h, -h), P_(h, h, -h), P_(h, h, h), P_(-h, h, h)], (0, 1, 0)),
    ]
    for pts, n in faces:
        nn = (n[0] * c + n[2] * s, n[1], -n[0] * s + n[2] * c)
        geo.oriented(m, pts, nn, (1.0, 1.0, 1.0), LIT, quv)


def dynamite(m, a, b, radius=0.07, color=P.DYNAMITE, mat=LIT):
    """One stick of dynamite from a to b with a pale label band in the middle."""
    geo.cylinder_between(m, a, b, radius, 8, color, mat)
    mid = [(a[k] + b[k]) / 2 for k in range(3)]
    d = [(b[k] - a[k]) * 0.12 for k in range(3)]
    geo.cylinder_between(m, tuple(mid[k] - d[k] for k in range(3)), tuple(mid[k] + d[k] for k in range(3)),
                         radius * 1.04, 8, P.LABEL, mat, caps=False)


def tyre(m, center, radius=0.45, width=0.28, yaw=0.0, upright=False):
    """A tyre lying flat (or upright, rolling along yaw)."""
    cx, cy, cz = center
    inner = radius * 0.55
    if not upright:
        profile = [(inner, -width / 2), (radius, -width / 2), (radius * 1.04, 0.0), (radius, width / 2), (inner, width / 2)]
        geo.lathe(m, (cx, cy, cz), profile, 14, P.TIRE, LIT, cap_top=False)
        # the hole's inside wall
        geo.lathe(m, (cx, cy, cz), [(inner, width / 2), (inner, -width / 2)], 14, geo.scale(P.TIRE, 0.7), LIT, cap_top=False)
    else:
        ax, az = math.cos(yaw), math.sin(yaw)
        a = (cx - ax * width / 2, cy, cz - az * width / 2)
        b = (cx + ax * width / 2, cy, cz + az * width / 2)
        geo.cylinder_between(m, a, b, radius, 14, P.TIRE, LIT)


def cone(m, base, height=0.7, radius=0.19, yaw=0.0):
    """A traffic cone: square base, orange body with two white bands."""
    x, y, z = base
    geo.box(m, (x, y + 0.02, z), (radius * 2.3, 0.04, radius * 2.3), geo.scale(P.MANKI_ORANGE, 0.75), LIT, yaw=yaw, skip=("bottom",))
    profile = [(radius, 0.04), (radius * 0.86, height * 0.30), (radius * 0.72, height * 0.47), (radius * 0.58, height * 0.66),
               (radius * 0.44, height * 0.84), (radius * 0.16, height)]
    colors = [P.MANKI_ORANGE, P.EYE_WHITE, P.MANKI_ORANGE, P.EYE_WHITE, P.MANKI_ORANGE]
    geo.lathe(m, (x, y, z), profile, 10, colors, LIT, yaw=yaw)


def glow(m, color, anim=STEADY, phase=0.0):
    """Faces added inside glow on the StageNeon material (sample LAMP_UV with them)."""
    return geo.state(m, alpha=1.0, anim=(anim[0], phase))


def lamp_uvs(n):
    return [T.LAMP_UV] * n


def glow_poly(m, pts, color):
    m.poly(pts, color, GLOW, lamp_uvs(len(pts)))


def glow_ellipsoid(m, center, radii, color, rings=5, segments=8):
    """An ellipsoid that glows: every corner samples the lamp block."""
    first = len(m.faces)
    geo.ellipsoid(m, center, radii, color, GLOW, rings=rings, segments=segments)
    for fi in range(first, len(m.faces)):
        m.uvs[fi] = lamp_uvs(len(m.faces[fi]))


def beacon(m, center, radius=0.22, color=P.BEACON, anim=BLINK, phase=0.0):
    """A warning lamp: a glowing (blinking) dome on a dark cap."""
    cx, cy, cz = center
    geo.box(m, (cx, cy - radius * 0.9, cz), (radius * 1.7, radius * 0.5, radius * 1.7), P.INK, LIT, skip=("bottom",))
    with glow(m, color, anim, phase):
        glow_ellipsoid(m, center, (radius, radius * 0.9, radius), color)


def star(m, center, normal, r_out, r_in, points, color, spin=0.0):
    """A flat glowing star facing `normal` (nx, nz) or up when normal is None."""
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
    # Fan from the centre so the concave outline triangulates cleanly.
    n = (0.0, 1.0, 0.0) if normal is None else (normal[0], 0.0, normal[1])
    for k in range(len(pts)):
        tri = [center, pts[k], pts[(k + 1) % len(pts)]]
        geo.oriented(m, tri, n, color, GLOW, lamp_uvs(3))
        geo.oriented(m, tri, tuple(-c for c in n), color, GLOW, lamp_uvs(3))


def spark(m, center, size=0.35, phase=0.0):
    """A fuse spark: three crossed glowing stars that flicker."""
    with glow(m, P.SPARK, FLICKER, phase):
        for k, yaw in enumerate((0.0, math.pi / 3, 2 * math.pi / 3)):
            star(m, center, (math.cos(yaw), math.sin(yaw)), size, size * 0.35, 6, P.SPARK, spin=0.3 * k)
        glow_ellipsoid(m, center, (size * 0.3,) * 3, P.FLAME, rings=3, segments=6)


def puff(m, center, r, color=P.SMOKE, seed=0, lumps=5):
    """A cartoon smoke cloud: a few overlapping squashed spheres."""
    cx, cy, cz = center
    for k in range(lumps):
        a = seed * 1.7 + k * 2.39996
        rr = r * (0.55 + 0.35 * ((k * 37 + seed * 11) % 7) / 6)
        off = r * 0.55 * (k / max(1, lumps - 1))
        c = geo.scale(color, 0.92 + 0.16 * ((k * 13 + seed) % 5) / 4)
        geo.ellipsoid(m, (cx + math.cos(a) * off, cy + rr * 0.35 + k * r * 0.12, cz + math.sin(a) * off),
                      (rr, rr * 0.82, rr), c, LIT, rings=5, segments=9)


def sign_on_posts(m, x, z, ground_y, yaw, cell, width, clearance=1.2, post=P.STEEL_DARK):
    """An atlas sign on two posts, facing yaw (radians, 0 = facing -z / south)."""
    x_, y_, w_, h_ = T.CELLS[cell]
    height = width * h_ / w_
    nx, nz = -math.sin(yaw), -math.cos(yaw)
    plane = Plane.facing((x, ground_y, z), (nx, nz))
    panel(m, plane, -width / 2, clearance, width / 2, clearance + height, (1.0, 1.0, 1.0), off=0.0, uvs=T.uv(cell))
    back = Plane.facing((x, ground_y, z), (-nx, -nz))
    panel(m, back, -width / 2, clearance, width / 2, clearance + height, geo.scale(P.STEEL, 0.8), off=0.04)
    post_w = 0.12 + 0.02 * width
    for u in (-width * 0.35, width * 0.35):
        p = plane.at(u, 0.0, -0.05 - post_w / 2)
        geo.box(m, (p[0], ground_y + (clearance + height) / 2, p[2]), (post_w, clearance + height, post_w), post, LIT,
                skip=("bottom",), yaw=math.atan2(nx, nz))
    return height


def beam(m, a, b, size, color, mat=LIT):
    """A square bar from a to b."""
    geo.cylinder_between(m, a, b, size * 0.7071, 4, color, mat)


def lattice(m, x, z, half, y0, y1, color, bay=2.0, chord=0.12, brace=0.06):
    """A square lattice column (crane mast): four chords and zig-zag braces on each face."""
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


# ------------------------------------------------------------------ walls and grids

WATER_FLOOR = -0.2  # every solid stands on the KO plane
WET = 0.35          # top of the wet band at the foot of every side


def mottle(x, y, z, seed=0.0):
    """Cheap smooth variation in [0, 1] for vertex colours (a few metres across)."""
    return 0.5 + 0.25 * (math.sin(x * 0.61 + seed) * math.cos(z * 0.53 - seed * 0.7)
                         + math.sin(y * 1.3 + x * 0.27 + z * 0.19 + seed * 1.9))


def wet(v):
    """Colour factor of the wet band: dark at the water, full above WET."""
    if v <= WET:
        return 0.55 + 0.25 * max(0.0, (v - WATER_FLOOR) / (WET - WATER_FLOOR))
    return 1.0


def rows(y0, y1, step=1.6):
    """Vertex rows from y0 to y1, with one at the wet line."""
    vs = {y0, y1}
    if y0 + 0.05 < WET < y1 - 0.05:
        vs.add(WET)
    k = math.floor(y0 / step) + 1
    while k * step < y1 - 0.3:
        if k * step > y0 + 0.3:
            vs.add(k * step)
        k += 1
    return sorted(vs)


def grid(m, plane, u0, u1, vs, color_at, mat=LIT, uv_at=None, step_u=2.0, off=0.0):
    """A wall on the plane over u0..u1 and the rows vs, coloured (and mapped) per corner."""
    nu = max(1, int(math.ceil((u1 - u0) / step_u - 1e-6)))
    us = [u0 + (u1 - u0) * i / nu for i in range(nu + 1)]
    for i in range(nu):
        for j in range(len(vs) - 1):
            corners = [(us[i], vs[j]), (us[i + 1], vs[j]), (us[i + 1], vs[j + 1]), (us[i], vs[j + 1])]
            geo.oriented(m, [plane.at(u, v, off) for u, v in corners], plane.n, [color_at(u, v) for u, v in corners], mat,
                         [uv_at(u, v) for u, v in corners] if uv_at else None)


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


def splat(m, plane, u, v, r, color, seed, off=0.01, points=9):
    """A paint splat: a jagged blob flush on the plane."""
    pts = []
    for k in range(points * 2):
        a = math.pi * k / points
        rr = r * (1.0 if k % 2 == 0 else 0.55 + 0.25 * math.sin(seed * 3.1 + k * 1.7))
        pts.append((u + math.cos(a) * rr, v + math.sin(a) * rr))
    c = plane.at(u, v, off)
    for k in range(len(pts)):
        a, b = pts[k], pts[(k + 1) % len(pts)]
        geo.oriented(m, [c, plane.at(a[0], a[1], off), plane.at(b[0], b[1], off)], plane.n, color, LIT)


# ------------------------------------------------------------------ paint without backgrounds
# StageVertexLit is opaque, so an atlas plate always shows its whole rectangle. Paint that
# must sit straight on rock or glass (eyes, mouths, graffiti) is geometry instead; atlas
# cells go on boards, posters and stickers whose rectangle is part of the joke.

def disc(m, plane, u, v, r, color, off=0.0, n=16, ry=None, mat=LIT):
    """A flat filled ellipse on the plane."""
    ry = r if ry is None else ry
    c = plane.at(u, v, off)
    pts = [plane.at(u + r * math.cos(2 * math.pi * k / n), v + ry * math.sin(2 * math.pi * k / n), off) for k in range(n)]
    for k in range(n):
        geo.oriented(m, [c, pts[k], pts[(k + 1) % n]], plane.n, color, mat)


def googly(m, plane, u, v, r, look=(0.35, 0.2), off=0.02):
    """A googly eye: dark rim, white, a pupil rolled towards `look` and a glint."""
    disc(m, plane, u, v, r * 1.08, P.INK, off)
    disc(m, plane, u, v, r, P.EYE_WHITE, off + 0.006, n=20)
    pu, pv = u + look[0] * r * 0.45, v + look[1] * r * 0.45
    disc(m, plane, pu, pv, r * 0.46, P.INK, off + 0.012)
    disc(m, plane, pu + r * 0.14, pv + r * 0.16, r * 0.11, P.EYE_WHITE, off + 0.018, n=8)


def strip(m, plane, a, b, width, color, off):
    """A painted stroke from a to b (plane coordinates), square-ended and slightly overlong."""
    du, dv = b[0] - a[0], b[1] - a[1]
    L = math.hypot(du, dv) or 1e-6
    tu, tv = du / L * width * 0.5, dv / L * width * 0.5
    nu, nv = -dv / L * width * 0.5, du / L * width * 0.5
    quad = [(a[0] - tu + nu, a[1] - tv + nv), (b[0] + tu + nu, b[1] + tv + nv),
            (b[0] + tu - nu, b[1] + tv - nv), (a[0] - tu - nu, a[1] - tv - nv)]
    geo.oriented(m, [plane.at(p[0], p[1], off) for p in quad], plane.n, color, LIT)


def spray(m, plane, text, u, v, height, color, off=0.012, rng=None, drips=4, slant=0.0):
    """Spray-painted capitals (stagekit's monoline font) centred at (u, v), with drips."""
    from stagekit import font
    strokes, width = font.text_strokes(text, height, origin=(u, v - height / 2), tracking=1.1)
    w = height * 0.16
    lows = []
    for line in strokes:
        line = [(p[0] + (p[1] - v) * slant, p[1]) for p in line]
        for a, b in zip(line, line[1:]):
            strip(m, plane, a, b, w, color, off)
        lows.append(min(line, key=lambda p: p[1]))
    if rng is not None:
        for p in rng.sample(lows, min(drips, len(lows))):
            length = height * rng.uniform(0.35, 0.9)
            strip(m, plane, (p[0], p[1]), (p[0], p[1] - length), w * 0.45, color, off)
    return width


def board(m, plane, cell, u, v, width, frame=P.INK, off=0.012, rim=0.08, depth=0.05):
    """An atlas cell mounted as a board: a dark frame slab with the cell on its face."""
    x, y, w, h = T.CELLS[cell]
    height = width * h / w
    slab(m, plane, u - width / 2 - rim, v - height / 2 - rim, u + width / 2 + rim, v + height / 2 + rim, depth, frame)
    plate(m, plane, cell, u, v, width, off=depth + off)
    return height


def sticker(m, plane, cell, u, v, width, off=0.012):
    """An atlas cell as a sticker with a white margin."""
    x, y, w, h = T.CELLS[cell]
    height = width * h / w
    b = width * 0.06
    panel(m, plane, u - width / 2 - b, v - height / 2 - b, u + width / 2 + b, v + height / 2 + b, P.EYE_WHITE, off=off)
    plate(m, plane, cell, u, v, width, off=off + 0.004)
