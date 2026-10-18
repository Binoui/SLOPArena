"""Fight Night Pier's bonus targets, the course's eight-hit targets, in four damage stages each,
for PierCoursePresentationBuilder (client/Unity/Assets/Scripts/Editor):

  Car     the contender's two-tone sedan in the pen by the gate (the car bash, pier edition)
  Pinata  a bull piñata hung from the burnt pier's derrick (肉 means meat)
  Punch   the PUNCH-O-METER on the VIP tower, WIMP to FIGHTGUY (the builder adds its meter)
  Bag     the champ's own heavy bag by the ring, 肉 patch and all

    blender -b --factory-startup --python-exit-code 1 --python tools/blender/fightguy_pier/bonus.py -- \\
        [--out client/Unity/Assets/Stages/fightguy_pier/Presentation/Course] [--no-bake]

Prop space: the origin is the Shared target centre, +Y up, and the side a player meets faces
+Z; the builder turns each prefab to its spot (CharacterCoursePresentation keeps a bonus
target's authored rotation). Objects are "<Prop> <stage>", 0 untouched to 3 nearly wrecked.
Faces use the pier's FNP_Lit and FNP_Glow on FightNightPierAtlas.png, so a change to the atlas
cells needs the stage art rebuilt and applied too (build.py, then sloparena.stage.art-apply).
Each stage is baked apart, standing on its own floor, then moved back to the origin.
"""
from __future__ import annotations

import hashlib
import json
import math
import os
import sys
from contextlib import contextmanager

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.dirname(HERE))  # tools/blender: the shared stagekit

from stagekit import geo  # noqa: E402

import palette as P  # noqa: E402
import parts as K  # noqa: E402
import textures as T  # noqa: E402

STEM = "FightNightPierBonus"
DEFAULT_OUT = os.path.join(T.REPO, "client/Unity/Assets/Stages/fightguy_pier/Presentation/Course")
STAGES = 4
# Where each prop's floor or hook sits relative to its target centre (layout.py's targets and
# the derrick and bag frame in rides.py).
CAR_DECK = -0.9
PINATA_HOOK = 1.1
PUNCH_DECK = -1.0
BAG_HOOK, BAG_DECK = 1.0, -1.4
# The punch machine's meter column (prop space): the builder lays its glowing bar over it.
METER = {"x0": 0.07, "x1": 0.21, "y0": 0.42, "y1": 1.02, "z": -0.045}

CHROME = (0.84, 0.85, 0.88)
TEAL_LIGHT = geo.mix(P.TEAL, P.WHITE, 0.25)
TEAL_DARK = geo.scale(P.TEAL, 0.62)
INTERIOR = (0.09, 0.08, 0.09)
GLASS = (0.20, 0.33, 0.42)
GLASS_SHINE = (0.55, 0.72, 0.80)
TAPE = (0.70, 0.71, 0.72)
SAND = (0.80, 0.68, 0.48)
CANDY = [P.PINK, P.GOLD, P.TEAL, P.ORANGE, P.GI_BLUE, P.WHITE]
FRINGE = [P.PINK, P.GOLD, P.TEAL, P.ORANGE]


# ------------------------------------------------------------------ helpers

@contextmanager
def moved(m, fn):
    """Vertices added inside are mapped through fn (a rigid move keeps the faces outward)."""
    start = len(m.verts)
    yield
    for i in range(start, len(m.verts)):
        m.verts[i] = fn(m.verts[i])


def turn(axis, degrees, pivot=(0.0, 0.0, 0.0), then=(0.0, 0.0, 0.0)):
    """A rotation about `axis` through `pivot`, then a shift."""
    L = math.sqrt(sum(a * a for a in axis))
    k = [a / L for a in axis]
    c, s = math.cos(math.radians(degrees)), math.sin(math.radians(degrees))

    def fn(p):
        v = [p[i] - pivot[i] for i in range(3)]
        cross = (k[1] * v[2] - k[2] * v[1], k[2] * v[0] - k[0] * v[2], k[0] * v[1] - k[1] * v[0])
        dot = sum(k[i] * v[i] for i in range(3))
        return tuple(v[i] * c + cross[i] * s + k[i] * dot * (1 - c) + pivot[i] + then[i] for i in range(3))
    return fn


def squash(y0, k):
    """Scale heights about y0 (a burst tyre)."""
    return lambda p: (p[0], y0 + (p[1] - y0) * k, p[2])


def extrude_z(m, outline, z0, z1, color, side_colors=None, mat=K.LIT):
    """A side-profile prism: a convex CCW outline in the XY plane between z0 and z1."""
    n = len(outline)
    for i in range(n):
        (ax, ay), (bx, by) = outline[i], outline[(i + 1) % n]
        col = side_colors[i] if side_colors else color
        geo.oriented(m, [(ax, ay, z0), (bx, by, z0), (bx, by, z1), (ax, ay, z1)], (by - ay, ax - bx, 0.0), col, mat)
    geo.oriented(m, [(x, y, z1) for x, y in outline], (0.0, 0.0, 1.0), color, mat)
    geo.oriented(m, [(x, y, z0) for x, y in outline], (0.0, 0.0, -1.0), color, mat)


def face_poly(m, pts, color, off, normal, mat=K.LIT):
    """A convex polygon pushed `off` along `normal`, facing it."""
    geo.oriented(m, [tuple(p[i] + normal[i] * off for i in range(3)) for p in pts], normal, color, mat)


def face_quad(m, a, b, c, d, color, off, normal, mat=K.LIT):
    face_poly(m, (a, b, c, d), color, off, normal, mat)


def half_disc(m, plane, u, v, r, color, off=0.0, n=10):
    """The upper half of a disc on a vertical plane: a wheel well."""
    c = plane.at(u, v, off)
    pts = [plane.at(u + r * math.cos(math.pi * k / n), v + r * math.sin(math.pi * k / n), off) for k in range(n + 1)]
    for k in range(n):
        geo.oriented(m, [c, pts[k], pts[k + 1]], plane.n, color, K.LIT)


def curved_plate(m, cell, radius, half_angle, y0, y1, n=6):
    """An atlas cell wrapped on a vertical cylinder about +Y, centred on +Z (the bag's patch)."""
    (u0, v0), (u1, v1) = T.uv(cell)
    cols = []
    for i in range(n + 1):
        a = half_angle - 2 * half_angle * i / n   # +X (the viewer's left) to -X
        cols.append((radius * math.sin(a), radius * math.cos(a), u0 + (u1 - u0) * i / n))
    for i in range(n):
        (xa, za, ua), (xb, zb, ub) = cols[i], cols[i + 1]
        mid = ((xa + xb) / 2, 0.0, (za + zb) / 2)
        geo.oriented(m, [(xa, y0, za), (xb, y0, zb), (xb, y1, zb), (xa, y1, za)], mid, (1.0, 1.0, 1.0), K.LIT,
                     [(ua, v0), (ub, v0), (ub, v1), (ua, v1)])


def strip_on_cylinder(m, radius, a0, a1, y0, y1, color, n=4):
    """A band of the cylinder about +Y between angles a0..a1 (from +Z toward +X): tape, a gash."""
    for i in range(n):
        ta, tb = a0 + (a1 - a0) * i / n, a0 + (a1 - a0) * (i + 1) / n
        pa = (radius * math.sin(ta), radius * math.cos(ta))
        pb = (radius * math.sin(tb), radius * math.cos(tb))
        out = ((pa[0] + pb[0]) / 2, 0.0, (pa[1] + pb[1]) / 2)
        geo.oriented(m, [(pa[0], y0, pa[1]), (pb[0], y0, pb[1]), (pb[0], y1, pb[1]), (pa[0], y1, pa[1])], out, color, K.LIT)


def tilted_strip(m, radius, angle, y, length, width, tilt, color):
    """A short strip of tape on the cylinder about +Y at `angle`, tilted `tilt` degrees."""
    with moved(m, turn((math.sin(angle), 0.0, math.cos(angle)), tilt, (radius * math.sin(angle), y, radius * math.cos(angle)))):
        a = length / 2 / radius
        strip_on_cylinder(m, radius, angle - a, angle + a, y - width / 2, y + width / 2, color)


def glowing_disc(m, plane, u, v, r, color, off=0.0, n=12):
    with K.glow(m):
        first = len(m.faces)
        K.disc(m, plane, u, v, r, color, off=off, n=n, mat=K.GLOW)
        for fi in range(first, len(m.faces)):
            m.uvs[fi] = K.lamp_uvs(len(m.faces[fi]))


# ------------------------------------------------------------------ the car

def car(stage):
    """A two-tone 50s sedan, nose to +X, the driver's side to +Z: dents and a cracked
    windshield, then the door, glass and a hubcap gone and the hood up, then the roof
    caved in on burst tyres."""
    m = geo.Mesh("Car %d" % stage, K.MATERIALS)
    d = CAR_DECK
    r = 0.31
    burst = stage >= 3
    sink = 0.08 if burst else 0.0
    lift = d + 0.36 - sink           # the body's underside
    waist = lift + 0.5               # hood, trunk and door tops
    roof = waist + (0.24 if burst else 0.46)

    # Whitewall tyres with chrome hubcaps on their outer faces.
    for x in (-1.1, 1.1):
        for side in (-1, 1):
            cy = d + r
            with moved(m, squash(d, 0.72) if burst else (lambda p: p)):
                geo.cylinder_between(m, (x, cy, side * 0.56), (x, cy, side * 0.82), r, 14, P.INK, K.LIT)
                plane = K.Plane.facing((0.0, 0.0, side * 0.825), (0.0, side))
                u = -x * side
                K.disc(m, plane, u, cy, 0.23, P.CREAM, n=14)
                K.disc(m, plane, u, cy, 0.17, P.INK, off=0.003, n=14)
                if not (stage >= 2 and x > 0 and side > 0):  # one hubcap rolled off
                    K.disc(m, plane, u, cy, 0.12, CHROME, off=0.006, n=12)
                    K.disc(m, plane, u, cy, 0.04, P.RED, off=0.009, n=8)

    # The lower body: teal, a lighter hood and trunk, the underside in shadow.
    outline = [(-1.8, lift), (1.82, lift), (1.9, lift + 0.08), (1.9, waist - 0.1), (1.8, waist), (-1.8, waist),
               (-1.9, waist - 0.12), (-1.9, lift + 0.1)]
    extrude_z(m, outline, -0.8, 0.8, P.TEAL, [P.UNDER, TEAL_DARK, P.TEAL, P.TEAL, TEAL_LIGHT, P.TEAL, P.TEAL, TEAL_DARK])
    for side in (-1, 1):
        plane = K.Plane.facing((0.0, 0.0, side * 0.8), (0.0, side))
        for x in (-1.1, 1.1):
            half_disc(m, plane, -x * side, d + r - sink, 0.37, P.UNDER, off=0.004)
        # the cream side spear and the chrome rocker
        K.slab(m, plane, -1.6, waist - 0.2, 1.6, waist - 0.12, 0.012, P.CREAM)
        K.slab(m, plane, -1.75, lift + 0.03, 1.75, lift + 0.07, 0.012, CHROME)
        # dents on the side the champ works on
        if stage >= 1 and side > 0:
            for x, y, rx in ((0.3, lift + 0.28, 0.2), (-0.5, lift + 0.22, 0.14)) + (((1.2, lift + 0.3, 0.18),) if stage >= 2 else ()):
                K.disc(m, plane, -x, y, rx, TEAL_DARK, off=0.016, n=10, ry=rx * 0.55)
    # The driver's door: gone from stage 2, showing the dark cab and a red bench.
    plane = K.Plane.facing((0.0, 0.0, 0.8), (0.0, 1.0))
    if stage >= 2:
        K.panel(m, plane, -0.42, lift + 0.08, 0.55, waist - 0.02, INTERIOR, off=0.02)
        K.panel(m, plane, -0.32, lift + 0.12, 0.45, lift + 0.36, P.RED_DARK, off=0.024)
    else:
        for u in (-0.42, 0.55):
            K.panel(m, plane, u - 0.008, lift + 0.08, u + 0.008, waist - 0.01, TEAL_DARK, off=0.016)
        K.panel(m, plane, 0.22, waist - 0.3, 0.38, waist - 0.27, CHROME, off=0.016)

    # Bumpers, grille, headlights and tail lights, plates.
    for sx in (-1, 1):
        x = sx * 1.93
        drop = 9 if (stage == 2 and sx > 0) else 0
        if not (stage >= 3 and sx > 0):
            with moved(m, turn((1, 0, 0), drop, (x, lift + 0.14, -0.82))):
                geo.box(m, (x, lift + 0.14, 0.0), (0.14, 0.13, 1.66), CHROME, K.LIT)
                end = K.Plane.facing((x + sx * 0.071, 0.0, 0.0), (sx, 0.0))
                K.plate(m, end, "plate", 0.0, lift + 0.14, 0.4, off=0.004)
        face = K.Plane.facing((sx * 1.9, 0.0, 0.0), (sx, 0.0))
        if sx > 0:
            K.panel(m, face, -0.36, lift + 0.22, 0.36, lift + 0.34, CHROME, off=0.02)
            for k in range(5):
                K.panel(m, face, -0.3 + k * 0.14, lift + 0.235, -0.26 + k * 0.14, lift + 0.325, P.INK, off=0.024)
            for z, alive in ((-0.55, stage < 2), (0.55, stage < 1)):
                K.disc(m, face, z, lift + 0.33, 0.12, CHROME, off=0.01, n=12)
                if alive:
                    glowing_disc(m, face, z, lift + 0.33, 0.09, P.BULB, off=0.016)
                else:
                    K.disc(m, face, z, lift + 0.33, 0.09, INTERIOR, off=0.016, n=12)
        else:
            for z in (-0.6, 0.6):
                glowing_disc(m, face, z, lift + 0.36, 0.07, P.RED, off=0.02, n=10)

    # The cab: cream, with glass all round until the champ gets to it.
    top_front, top_back = (0.5, -0.75) if not burst else (0.62, -0.88)
    cab = [(-1.05, waist), (0.85, waist), (top_front, roof), (top_back, roof)]
    with moved(m, turn((1, 0, 0), -6, (0.0, waist, 0.0)) if burst else (lambda p: p)):
        extrude_z(m, cab, -0.7, 0.7, P.CREAM, [P.CREAM, P.CREAM, geo.scale(P.CREAM, 1.05), P.CREAM])
        smashed = stage >= 2
        inset = 0.06
        for side in (-1, 1):
            z = side * 0.7
            pts = [(-0.95, waist + inset), (0.74, waist + inset), (top_front - 0.06, roof - inset), (top_back + 0.04, roof - inset)]
            if burst:
                pts = [(-0.98, waist + 0.04), (0.78, waist + 0.04), (top_front - 0.04, roof - 0.04), (top_back + 0.03, roof - 0.04)]
            # the B pillar splits the glass in two
            for xa, xb in ((pts[0][0], -0.12), (-0.04, pts[1][0])):
                window = clip_window(pts, xa, xb)
                face_poly(m, [(x, y, z) for x, y in window], INTERIOR if smashed else GLASS, 0.006, (0.0, 0.0, side))
            if not smashed:
                face_quad(m, (-0.7, waist + 0.1, z), (-0.55, waist + 0.1, z), (-0.35, roof - 0.08, z), (-0.5, roof - 0.08, z),
                          GLASS_SHINE, 0.01, (0.0, 0.0, side))
        # windshield and rear window on the slopes
        for (ax, ay), (bx, by), front in (((0.85, waist), (top_front, roof), True), ((top_back, roof), (-1.05, waist), False)):
            nx, ny = by - ay, ax - bx
            L = math.hypot(nx, ny)
            normal = (nx / L, ny / L, 0.0)
            a = (ax + (bx - ax) * 0.08, ay + (by - ay) * 0.08)
            b = (ax + (bx - ax) * 0.92, ay + (by - ay) * 0.92)
            face_quad(m, (a[0], a[1], -0.6), (a[0], a[1], 0.6), (b[0], b[1], 0.6), (b[0], b[1], -0.6),
                      INTERIOR if (stage >= 2 or (burst and front)) else GLASS, 0.006, normal)
            if front and stage == 1:
                crack_star(m, a, b, 0.15, normal)

    # The hood springs open on stage 2 and the trunk on stage 3.
    if stage >= 2:
        with moved(m, turn((0, 0, 1), 28, (0.9, waist, 0.0))):
            geo.box(m, (1.33, waist + 0.03, 0.0), (0.86, 0.05, 1.5), TEAL_LIGHT, K.LIT, bottom_color=INTERIOR)
        geo.box(m, (1.33, waist + 0.005, 0.0), (0.8, 0.01, 1.4), INTERIOR, K.LIT, skip=("bottom",))
    if stage >= 3:
        with moved(m, turn((0, 0, 1), -35, (-1.1, waist, 0.0))):
            geo.box(m, (-1.45, waist + 0.03, 0.0), (0.7, 0.05, 1.5), TEAL_LIGHT, K.LIT, bottom_color=INTERIOR)
    return m


def clip_window(pts, xa, xb):
    """A window's trapezoid (bottom-left, bottom-right, top-right, top-left) cut to xa..xb:
    a convex polygon, counter-clockwise seen from +Z."""
    (bl, br, tr, tl) = pts

    def along(p, q, x):
        t = (x - p[0]) / (q[0] - p[0]) if abs(q[0] - p[0]) > 1e-6 else 0.0
        return (x, p[1] + (q[1] - p[1]) * t)

    def hi(x):
        if x <= tl[0]:
            return along(bl, tl, x)
        if x >= tr[0]:
            return along(br, tr, x)
        return along(tl, tr, x)
    xa, xb = max(xa, bl[0]), min(xb, br[0])
    poly = [along(bl, br, xa), along(bl, br, xb), hi(xb)]
    poly += [c for c in (tr, tl) if xa < c[0] < xb]
    poly.append(hi(xa))
    out = []
    for p in poly:
        if not out or math.dist(p, out[-1]) > 1e-4:
            out.append(p)
    if math.dist(out[0], out[-1]) <= 1e-4:
        out.pop()
    return out


def crack_star(m, a, b, z, normal, width=0.009):
    """White cracks radiating on a slope from a to b (side profile) around z."""
    L = math.hypot(b[0] - a[0], b[1] - a[1])

    def at(s, zz):
        t = s / L
        return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, zz)
    cs, cz = L * 0.5, z
    for k in range(7):
        t = k * 2 * math.pi / 7 + 0.4
        du, dz = math.cos(t), math.sin(t)
        length = 0.16 + 0.09 * (k % 3) / 2
        pu, pz = -dz * width, du * width
        tip = (cs + du * length, cz + dz * length)
        face_poly(m, [at(cs - pu, cz - pz), at(tip[0], tip[1]), at(cs + pu, cz + pz)], P.WHITE, 0.012, normal)


# ------------------------------------------------------------------ the piñata

def pinata(stage):
    """A bull piñata, nose to +X, flank to +Z, hung from the hook: a leg off and a hole,
    then the head drooping and candy showing, then only the front half left on the rope."""
    m = geo.Mesh("Pinata %d" % stage, K.MATERIALS)
    hook = (0.0, PINATA_HOOK, 0.0)
    swing = turn((1, 0, 0), 14, hook) if stage >= 3 else (lambda p: p)
    with moved(m, swing):
        geo.tube(m, [hook, (0.02, 0.62, 0.0), (0.0, 0.33, 0.0)], 0.022, 5, P.CREAM, K.LIT)
        # The body: a barrel of tissue fringe along X, each band lipped over the next.
        profile, colors = [], []
        bands = 7
        for b in range(bands):
            t0, t1 = b / bands, (b + 1) / bands
            h0, h1 = -0.55 + 1.1 * t0, -0.55 + 1.1 * t1
            r0 = 0.34 * math.sin(math.pi * (0.12 + 0.76 * t0)) ** 0.5
            r1 = 0.34 * math.sin(math.pi * (0.12 + 0.76 * t1)) ** 0.5
            profile += [(r0 + 0.05, h0), (r1, h1)]
            colors += [FRINGE[b % len(FRINGE)], geo.scale(FRINGE[b % len(FRINGE)], 0.75)]
        profile = [(0.0, -0.56)] + profile + [(0.0, 0.56)]
        colors = [FRINGE[0]] + colors
        if stage >= 3:   # the back half is gone: keep the bands from the middle forward
            keep = len(profile) // 2
            profile = [(0.0, profile[keep][1] - 0.02)] + profile[keep:]
            colors = [INTERIOR] + colors[keep:]
        with moved(m, turn((0, 0, 1), -90)):
            geo.lathe(m, (0.0, 0.0, 0.0), profile, 12, colors[:len(profile) - 1], K.LIT, cap_top=False)
        if stage >= 3:
            # loose fringe hanging off the torn end
            for k in range(6):
                z = -0.24 + k * 0.1
                face_quad(m, (-0.02, 0.1, z), (-0.02, 0.1, z + 0.06), (-0.12, -0.32 - 0.05 * (k % 3), z + 0.06),
                          (-0.12, -0.32 - 0.05 * (k % 3), z), FRINGE[k % 4], 0.0, (-1.0, 0.0, 0.0))
                face_quad(m, (-0.02, 0.1, z), (-0.02, 0.1, z + 0.06), (-0.12, -0.32 - 0.05 * (k % 3), z + 0.06),
                          (-0.12, -0.32 - 0.05 * (k % 3), z), FRINGE[k % 4], 0.0, (1.0, 0.0, 0.0))

        # Legs, striped, with dark hooves: the front pair falls off first.
        for x, z in ((0.3, 0.17), (0.3, -0.17), (-0.32, 0.17), (-0.32, -0.17)):
            gone = (stage >= 1 and x > 0 and z > 0) or (stage >= 2 and x > 0) or (stage >= 3 and x < 0)
            if gone:
                if not (stage >= 3 and x < 0):
                    geo.cylinder_between(m, (x, -0.18, z), (x, -0.26, z), 0.075, 8, INTERIOR, K.LIT)
                continue
            geo.cylinder_between(m, (x, -0.18, z), (x, -0.42, z), 0.075, 8, P.GOLD, K.LIT)
            geo.cylinder_between(m, (x, -0.42, z), (x, -0.62, z), 0.072, 8, P.PINK, K.LIT)
            geo.cylinder_between(m, (x, -0.62, z), (x, -0.7, z), 0.078, 8, P.INK, K.LIT)

        # The head on a short neck: horns, snout, eyes and ears. It droops from stage 2.
        droop = turn((0, 0, 1), -32, (0.5, 0.1, 0.0)) if stage >= 2 else (lambda p: p)
        with moved(m, droop):
            geo.ellipsoid(m, (0.66, 0.16, 0.0), (0.24, 0.22, 0.22), P.PINK, K.LIT, rings=6, segments=12)
            geo.ellipsoid(m, (0.86, 0.08, 0.0), (0.13, 0.12, 0.17), P.CREAM, K.LIT, rings=5, segments=10)
            snout = K.Plane.facing((0.985, 0.0, 0.0), (1.0, 0.0))
            for z in (-0.07, 0.07):
                K.disc(m, snout, z, 0.09, 0.03, INTERIOR, n=8)
            for side in (-1, 1):
                geo.cylinder_between(m, (0.62, 0.32, side * 0.12), (0.68, 0.56, side * 0.34), 0.05, 8, P.CREAM, K.LIT,
                                     radius_b=0.012)
                geo.ellipsoid(m, (0.56, 0.3, side * 0.24), (0.06, 0.035, 0.1), P.GOLD, K.LIT, rings=3, segments=8)
                eye = K.Plane.facing((0.0, 0.0, side * 0.205), (0.0, side))
                K.disc(m, eye, -0.74 * side, 0.22, 0.055, P.WHITE, n=10)
                K.disc(m, eye, -0.75 * side, 0.215, 0.032, P.INK, off=0.003, n=8)
                K.disc(m, eye, -0.76 * side, 0.228, 0.011, P.WHITE, off=0.006, n=6)
            # a red ribbon at the neck
            with moved(m, turn((0, 0, 1), -90, (0.0, 0.0, 0.0))):
                geo.lathe(m, (0.0, 0.43, 0.0), [(0.27, 0.0), (0.28, 0.07)], 12, P.RED, K.LIT, cap_top=False)

        # The tail and its gold tuft.
        if stage < 3:
            geo.tube(m, [(-0.55, 0.12, 0.0), (-0.68, 0.0, 0.0), (-0.74, -0.18, 0.0)], 0.02, 5, P.PINK, K.LIT)
            geo.ellipsoid(m, (-0.75, -0.24, 0.0), (0.05, 0.08, 0.05), P.GOLD, K.LIT, rings=3, segments=6)

        # Holes in the flank the champ is working on, candy showing from stage 2.
        if stage in (1, 2):
            r = 0.1 if stage == 1 else 0.16
            K.disc(m, K.Plane.facing((0.0, 0.0, 0.335), (0.0, 1.0)), 0.05, -0.02, r, INTERIOR, n=10, ry=r * 0.8)
            if stage == 2:
                for k in range(6):
                    a = k * 2.39996
                    geo.ellipsoid(m, (-0.05 + math.cos(a) * 0.08, -0.02 + math.sin(a) * 0.06, 0.33),
                                  (0.035, 0.025, 0.02), CANDY[k % len(CANDY)], K.LIT, rings=3, segments=6)
    return m


# ------------------------------------------------------------------ the punch machine

def punch(stage):
    """The PUNCH-O-METER: a red cabinet, the marquee, the ranks beside the meter column and
    the red pad on its arm. The marquee cracks, tilts and hangs; the pad is pushed in,
    torn and finally dangles; the bulbs give out."""
    m = geo.Mesh("Punch %d" % stage, K.MATERIALS)
    d = PUNCH_DECK
    top = 1.18
    front = -0.08
    geo.box(m, (0.0, d + 0.06, -0.46), (1.02, 0.12, 0.96), P.IRON, K.LIT, skip=("bottom",))
    geo.box(m, (0.0, (d + 0.12 + top) / 2, -0.47), (0.86, top - d - 0.12, 0.78), P.RED, K.LIT, skip=("bottom",),
            side_color=P.RED_DARK, top_color=P.GOLD_DARK)
    plane = K.Plane.facing((0.0, 0.0, front), (0.0, 1.0))
    # gold trim down the front edges and round the scale
    for u in (-0.41, 0.41):
        K.slab(m, plane, u - 0.025, d + 0.14, u + 0.025, top - 0.02, 0.02, P.GOLD)
    for side in (-1, 1):
        sp = K.Plane.facing((side * 0.43, 0.0, 0.0), (side, 0.0))
        # u runs along +Z on the east face and along -Z on the west one
        K.slab(m, sp, min(-0.2 * side, -0.12 * side), d + 0.2, max(-0.2 * side, -0.12 * side), top - 0.1, 0.012, P.GOLD)
        if stage >= 2 and side > 0:
            K.disc(m, sp, -0.5, 0.35, 0.16, P.RED_DARK, off=0.02, n=10, ry=0.1)
    # The ranks on the viewer's right, the meter column on their left (u = -x on this face).
    K.board(m, plane, "punch_scale", 0.2, 0.72, 0.3, frame=P.GOLD_DARK, depth=0.02, rim=0.025)
    K.slab(m, plane, -METER["x1"] - 0.03, METER["y0"] - 0.03, -METER["x0"] + 0.03, METER["y1"] + 0.03, 0.02, P.GOLD_DARK)
    K.panel(m, plane, -METER["x1"], METER["y0"], -METER["x0"], METER["y1"], INTERIOR, off=0.03)
    # the coin door under the pad, hanging open from stage 2
    door = turn((0, 1, 0), -60, (0.18, -0.5, front + 0.02)) if stage >= 2 else (lambda p: p)
    K.slab(m, plane, -0.24, -0.72, 0.24, -0.34, 0.015, INTERIOR)
    with moved(m, door):
        K.slab(m, plane, -0.18, -0.66, 0.18, -0.4, 0.035, P.GOLD_DARK)
        K.panel(m, plane, -0.06, -0.48, 0.06, -0.46, P.INK, off=0.037)
    # The marquee on top: cracked, then tilted, then hanging by one corner.
    hang = {0: 0.0, 1: 0.0, 2: -9.0, 3: -26.0}[stage]
    with moved(m, turn((0, 0, 1), hang, (-0.5, top, front))):
        mplane = K.Plane.facing((0.0, 0.0, front - 0.06), (0.0, 1.0))
        K.board(m, mplane, "punch_marquee", 0.0, top + 0.17, 1.0, frame=P.GOLD, depth=0.06, rim=0.04)
        if stage >= 1:
            for (ua, va), (ub, vb) in (((0.18, top + 0.3), (0.3, top + 0.14)), ((0.3, top + 0.14), (0.27, top + 0.05)),
                                       ((0.3, top + 0.14), (0.42, top + 0.18))):
                pa, pb = mplane.at(ua, va, 0.09), mplane.at(ub, vb, 0.09)
                geo.cylinder_between(m, pa, pb, 0.008, 4, P.INK, K.LIT)
        for k in range(5):
            x = -0.4 + k * 0.2
            lit = stage < 2 or (stage == 2 and k % 2 == 0)
            if lit:
                K.bulb(m, (x, top + 0.37, front - 0.04), 0.045, P.BULB, K.BLINK, (k * 0.2) % 1.0)
            else:
                geo.ellipsoid(m, (x, top + 0.37, front - 0.04), (0.045, 0.045, 0.045), INTERIOR, K.LIT, rings=3, segments=6)
    # The pad: a red cushion on a chrome arm, its face where the target sits.
    pad = {0: (0.0, 0.0), 1: (-0.04, 6.0), 2: (-0.06, 12.0), 3: (0.0, 48.0)}[stage]
    geo.cylinder_between(m, (0.0, 0.0, front), (0.0, 0.0, front + 0.06), 0.05, 8, CHROME, K.LIT)
    with moved(m, turn((1, 0, 0), pad[1], (0.0, 0.0, front + 0.06), (0.0, 0.0, pad[0]))):
        geo.ellipsoid(m, (0.0, 0.0, 0.08), (0.27, 0.29, 0.13), P.RED, K.LIT, rings=6, segments=12)
        geo.ellipsoid(m, (0.0, 0.0, 0.14), (0.15, 0.16, 0.08), geo.mix(P.RED, P.WHITE, 0.25), K.LIT, rings=4, segments=10)
        if stage >= 2:
            geo.ellipsoid(m, (0.1, 0.12, 0.19), (0.07, 0.05, 0.04), P.CREAM, K.LIT, rings=3, segments=6)
    return m


# ------------------------------------------------------------------ the heavy bag

def bag(stage):
    """The champ's red heavy bag on four chains, a cream 肉 patch on its front: taped, then
    split and leaking sand and sagging, then gashed and hanging off one chain."""
    m = geo.Mesh("Bag %d" % stage, K.MATERIALS)
    hook = (0.0, BAG_HOOK, 0.0)
    sag = {0: 0.0, 1: 0.0, 2: 0.04, 3: 0.08}[stage]
    tilt = turn((0, 0, 1), 11, hook) if stage >= 3 else (lambda p: p)
    with moved(m, tilt):
        geo.tube(m, [hook, (0.0, 0.92, 0.0)], 0.02, 5, P.IRON, K.LIT)
        geo.lathe(m, (0.0, 0.9, 0.0), [(0.05, 0.0), (0.06, 0.02), (0.05, 0.04)], 8, CHROME, K.LIT)
        for k in range(4):
            if stage >= 3 and k == 1:
                continue  # a chain snapped
            a = math.pi / 4 + k * math.pi / 2
            geo.tube(m, [(0.0, 0.9, 0.0), (math.cos(a) * 0.24, 0.72, math.sin(a) * 0.24)], 0.011, 4, P.IRON, K.LIT)
        profile = [(0.0, -0.8 + sag * 0.5), (0.22 + sag, -0.79 + sag * 0.5), (0.34 + sag, -0.7), (0.36 + sag * 0.8, -0.6),
                   (0.36, -0.45), (0.36, 0.3), (0.355 - sag * 0.8, 0.55), (0.32 - sag, 0.68), (0.2 - sag * 0.5, 0.74), (0.0, 0.75)]
        colors = [P.RED_DARK, P.RED_DARK, P.RED, P.RED, P.RED, P.RED, P.RED_DARK, P.RED_DARK, P.RED_DARK]
        geo.lathe(m, (0.0, 0.0, 0.0), profile, 16, colors, K.LIT, cap_top=False)
        for y0, y1 in ((-0.6, -0.5), (0.46, 0.56)):
            geo.lathe(m, (0.0, y0, 0.0), [(radius_at(profile, y0) + 0.014, 0.0), (radius_at(profile, y1) + 0.014, y1 - y0)],
                      16, P.GOLD, K.LIT, cap_top=False)
        curved_plate(m, "niku", 0.372, 0.55, -0.15, 0.27)
        if stage >= 1:
            tilted_strip(m, 0.376, -0.75, -0.25, 0.34, 0.07, 35, TAPE)
            tilted_strip(m, 0.377, -0.75, -0.25, 0.34, 0.07, -35, TAPE)
        if stage >= 2:
            strip_on_cylinder(m, 0.372, 0.95, 1.05, -0.42, 0.12, INTERIOR, n=2)
            geo.cylinder_between(m, (0.36 * math.sin(1.0), -0.42, 0.36 * math.cos(1.0)),
                                 (0.4 * math.sin(1.0), -0.78, 0.4 * math.cos(1.0)), 0.025, 6, SAND, K.LIT, radius_b=0.012)
        if stage >= 3:
            strip_on_cylinder(m, 0.373, -1.4, -0.8, 0.05, 0.18, INTERIOR, n=4)
            tilted_strip(m, 0.378, 0.2, 0.05, 0.4, 0.07, 80, TAPE)
    return m


def radius_at(profile, y):
    """A lathe profile's radius at height y."""
    for (r0, y0), (r1, y1) in zip(profile, profile[1:]):
        if y0 <= y <= y1 and y1 > y0:
            return r0 + (r1 - r0) * (y - y0) / (y1 - y0)
    return profile[-1][0]


# (builder, the floor below the target centre)
PROPS = {"Car": (car, CAR_DECK), "Pinata": (pinata, -1.7), "Punch": (punch, PUNCH_DECK), "Bag": (bag, BAG_DECK)}


def groups():
    """{"<Prop> <stage>": geo.Mesh} in prop space."""
    out = {}
    for name, (build, _) in PROPS.items():
        for stage in range(STAGES):
            out["%s %d" % (name, stage)] = build(stage)
    return out


# ------------------------------------------------------------------ Blender

ATLAS_PREVIEW = None
# (frame width in metres, look-at height) per prop, for the preview renders.
FRAMING = {"Car": (4.8, -0.35), "Pinata": (2.6, 0.15), "Punch": (3.0, 0.25), "Bag": (2.4, 0.05)}


def preview(objects, names, spacing, out_dir):
    """Eevee renders of each stage from the player's side, three-quarter and slightly above,
    with the atlas times vertex colour and a low warm sun: review aids, not the Unity look."""
    import bpy
    from mathutils import Vector

    from stagekit.preview import B

    sc = bpy.context.scene
    sc.render.engine = "BLENDER_EEVEE"
    sc.render.resolution_x = sc.render.resolution_y = 640
    sc.render.film_transparent = False
    sc.world.color = (0.36, 0.32, 0.42)
    image = bpy.data.images.load(ATLAS_PREVIEW) if ATLAS_PREVIEW and os.path.exists(ATLAS_PREVIEW) else None
    if image is not None:
        image.alpha_mode = "NONE"   # the white block has alpha 0 (StageNeon's glow mask), not black
    for mat in bpy.data.materials:
        mat.use_nodes = True
        nt = mat.node_tree
        nt.nodes.clear()
        out = nt.nodes.new("ShaderNodeOutputMaterial")
        col = nt.nodes.new("ShaderNodeVertexColor")
        col.layer_name = "Col"
        mix = nt.nodes.new("ShaderNodeMix")
        mix.data_type = "RGBA"
        mix.blend_type = "MULTIPLY"
        mix.inputs["Factor"].default_value = 1.0
        nt.links.new(col.outputs["Color"], mix.inputs["A"])
        if image is not None:
            tex = nt.nodes.new("ShaderNodeTexImage")
            tex.image = image
            uv = nt.nodes.new("ShaderNodeUVMap")
            uv.uv_map = "UVMap"
            nt.links.new(uv.outputs["UV"], tex.inputs["Vector"])
            nt.links.new(tex.outputs["Color"], mix.inputs["B"])
        else:
            mix.inputs["B"].default_value = (1, 1, 1, 1)
        if mat.name == K.GLOW:
            shader = nt.nodes.new("ShaderNodeEmission")
            shader.inputs["Strength"].default_value = 2.0
            nt.links.new(mix.outputs["Result"], shader.inputs["Color"])
        else:
            shader = nt.nodes.new("ShaderNodeBsdfDiffuse")
            nt.links.new(mix.outputs["Result"], shader.inputs["Color"])
        nt.links.new(shader.outputs[0], out.inputs["Surface"])
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 3.2
    sun.data.color = (1.0, 0.82, 0.66)
    sc.collection.objects.link(sun)
    # light travelling +X, down and -Z: onto the player's side from their left
    sun.rotation_euler = B((0.4, -0.8, -0.6)).to_track_quat("-Z", "Y").to_euler()
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    cam.data.type = "ORTHO"
    sc.collection.objects.link(cam)
    sc.camera = cam
    os.makedirs(out_dir, exist_ok=True)
    for i, name in enumerate(names):
        width, height = FRAMING[name.split()[0]]
        target = Vector((i * spacing, height, 0.0))
        yaw, pitch = math.radians(200.0), math.radians(14.0)
        fwd = (math.sin(yaw) * math.cos(pitch), -math.sin(pitch), math.cos(yaw) * math.cos(pitch))
        pos = tuple(target[k] - fwd[k] * 12.0 for k in range(3))
        cam.location = B(pos)
        cam.rotation_euler = (B(tuple(target)) - B(pos)).to_track_quat("-Z", "Y").to_euler()
        cam.data.ortho_scale = width
        sc.render.filepath = os.path.join(out_dir, name.replace(" ", "_") + ".png")
        bpy.ops.render.render(write_still=True)


def main():
    import argparse

    import bpy
    from mathutils import Matrix

    from stagekit import blender as kit

    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=DEFAULT_OUT)
    ap.add_argument("--no-bake", action="store_true")
    ap.add_argument("--samples", type=int, default=96)
    ap.add_argument("--preview", help="also render every stage to <dir>/<object>.png (review aid; uses --atlas)")
    ap.add_argument("--atlas", default="/tmp/fightguy-pier-generated/FightNightPierAtlas.png")
    a = ap.parse_args(argv)
    global ATLAS_PREVIEW
    ATLAS_PREVIEW = a.atlas

    meshes = groups()
    # Bake each stage apart on its own floor (6 m apart along X), then move it home.
    spacing = 6.0
    floors = {}
    for i, (name, m) in enumerate(meshes.items()):
        ox = i * spacing
        m.verts = [(x + ox, y, z) for x, y, z in m.verts]
        floor_y = PROPS[name.split()[0]][1]
        f = geo.Mesh("Floor %d" % i, K.MATERIALS)
        geo.box(f, (ox, floor_y - 0.05, 0.0), (4.5, 0.1, 4.5), P.PLANK, K.LIT)
        floors[f.name] = f
    kit.reset_scene(K.MATERIALS)
    objects = kit.build_objects({**meshes, **floors}, channels=True)
    glowing = lambda me, c: kit.loop_materials(me) == K.GLOW  # noqa: E731
    bake_seconds = None if a.no_bake else kit.bake_ao(objects, a.samples, ao_distance=0.8, floor=0.55, gamma=1.0,
                                                      skip=tuple(floors), keep=glowing)
    if a.preview:
        preview(objects, list(meshes), spacing, a.preview)
    for name, ob in list(objects.items()):
        if name in floors:
            bpy.data.objects.remove(ob, do_unlink=True)
            del objects[name]
    for i, name in enumerate(meshes):
        # Unity +X is Blender -X (geo.to_object): undo the bake offset.
        objects[name].data.transform(Matrix.Translation((i * spacing, 0.0, 0.0)))
    digest, object_digests = kit.digest(objects)
    path = os.path.join(a.out, STEM + ".fbx")
    kit.export_fbx(objects, path)
    cells = {k: T.CELLS[k] for k in ("plate", "punch_marquee", "punch_scale", "niku", "white", "lamp")}
    stamp = {"generator": "tools/blender/fightguy_pier/bonus.py", "blender": bpy.app.version_string,
             "materials": K.MATERIALS, "atlas": "FightNightPierAtlas.png",
             "atlasCells": hashlib.sha256(json.dumps(T.CELLS, sort_keys=True).encode()).hexdigest(),
             "cells": cells, "meter": METER, "objects": sorted(objects), "geometryDigest": digest}
    with open(os.path.join(a.out, STEM + ".layout.json"), "w") as fh:
        json.dump(stamp, fh, indent=1)
        fh.write("\n")
    tris = {n: meshes[n].triangle_count for n in meshes}
    print("FIGHTGUY_PIER_BONUS", json.dumps({"triangles": sum(tris.values()), "aoBakeSeconds": bake_seconds,
                                             "geometryDigest": digest}))
    print("  OBJECTS", json.dumps(tris))
    print("  HEALTH", json.dumps(kit.mesh_health(objects)))


if __name__ == "__main__":
    main()
