"""Polish on the playable props: all flush with (or inside) the collision faces, art only."""
from __future__ import annotations
import math
import random
from stagekit import font
import layout as L
import palette as P

MAT = 'HH_Lit'
EPS = 0.015   # decals sit this far off their face, inside the wall check's 2.5 cm tolerance


def _ribbon(m, a, b, width, plane, color):
    """A flat stroke from a to b (2D u, v) drawn by `plane(u, v)` -> 3D point."""
    du, dv = b[0] - a[0], b[1] - a[1]
    n = math.hypot(du, dv) or 1.0
    pu, pv = -dv / n * width / 2, du / n * width / 2
    m.poly([plane(a[0] - pu, a[1] - pv), plane(b[0] - pu, b[1] - pv),
            plane(b[0] + pu, b[1] + pv), plane(a[0] + pu, a[1] + pv)], color, MAT)


def tape_label(m, text, centre_x, centre_y, face_z, width, height=1.5):
    """Masking tape with the kid's marker writing on a south-facing bin wall (reads left to right)."""
    z = face_z - EPS
    hw, hh = width / 2, height * 0.95
    m.poly([(centre_x - hw, centre_y - hh, z), (centre_x + hw, centre_y - hh, z),
            (centre_x + hw + 0.2, centre_y + hh, z), (centre_x - hw - 0.15, centre_y + hh, z)], P.TAPE, MAT)
    strokes, _ = font.text_strokes(text, height, origin=(centre_x, centre_y - height / 2), tracking=1.15)
    plane = lambda u, v: (u, v, z - EPS)
    for line in strokes:
        for a, b in zip(line, line[1:]):
            _ribbon(m, a, b, height * 0.14, plane, P.MARKER)


def bin_ribs(m, footprint, top, support):
    """Moulded vertical ribs on the outside of a bin, a shade darker than the plastic."""
    x0, x1, z0, z1 = footprint
    for (ax, az), (bx, bz), (nx, nz) in (((x0, z0), (x1, z0), (0, -1)), ((x1, z0), (x1, z1), (1, 0)),
                                         ((x1, z1), (x0, z1), (0, 1)), ((x0, z1), (x0, z0), (-1, 0))):
        length = math.hypot(bx - ax, bz - az)
        count = int(length // 3.6)
        for i in range(1, count):
            t = i / count
            cx, cz = ax + (bx - ax) * t, az + (bz - az) * t
            ux, uz = (bx - ax) / length * 0.28, (bz - az) / length * 0.28
            ox, oz = nx * EPS, nz * EPS
            m.poly([(cx - ux + ox, support, cz - uz + oz), (cx + ux + ox, support, cz + uz + oz),
                    (cx + ux + ox, top - 1.3, cz + uz + oz), (cx - ux + ox, top - 1.3, cz - uz + oz)],
                   [P.BIN_DARK, P.BIN_DARK, P.BIN_RIB, P.BIN_RIB], MAT)


def house(m):
    """Plank walls, a framed arch door to the gap, a round window and a roof trim."""
    x0, x1, _, top, z0, z1 = L.SPEC['house']
    faces = [((x0, z0), (x1, z0), (0, -1)), ((x1, z0), (x1, z1), (1, 0)),
             ((x1, z1), (x0, z1), (0, 1)), ((x0, z1), (x0, z0), (-1, 0))]
    for (ax, az), (bx, bz), (nx, nz) in faces:
        length = math.hypot(bx - ax, bz - az)
        count = max(2, int(length // 1.1))
        for i in range(count):
            t0, t1 = i / count, (i + 1) / count
            pa = (ax + (bx - ax) * t0 + nx * EPS, az + (bz - az) * t0 + nz * EPS)
            pb = (ax + (bx - ax) * t1 + nx * EPS, az + (bz - az) * t1 + nz * EPS)
            shade = (P.HOUSE, P.HOUSE_PLANK)[i % 2]
            m.poly([(pa[0], 0.0, pa[1]), (pb[0], 0.0, pb[1]), (pb[0], top, pb[1]), (pa[0], top, pa[1])], shade, MAT)
    # Roof trim: a darker band round the top edge, flush on the roof.
    y = top + EPS
    inset = 0.5
    for (ax, az, bx, bz) in ((x0, z0, x1, z0 + inset), (x0, z1 - inset, x1, z1),
                             (x0, z0, x0 + inset, z1), (x1 - inset, z0, x1, z1)):
        m.poly([(ax, y, az), (ax, y, bz), (bx, y, bz), (bx, y, az)], P.HOUSE_TRIM, MAT)
    # Door on the east face, towards the gap: a light frame round a dark arch.
    zc = (z0 + z1) / 2
    for w, h, color, off in ((1.85, 3.35, P.HOUSE_TRIM, 2 * EPS), (1.5, 3.0, P.HOUSE_DOOR, 3 * EPS)):
        arc = [(x1 + off, h - w + w * math.sin(a), zc - w * math.cos(a)) for a in [k * math.pi / 12 for k in range(13)]]
        m.poly([(x1 + off, 0.0, zc - w)] + arc + [(x1 + off, 0.0, zc + w)], color, MAT)
    # Round window on the south face with a cross frame.
    cx, cy = (x0 + x1) / 2 - 1.5, 2.3
    for r, color, off in ((1.35, P.HOUSE_TRIM, 2 * EPS), (1.05, P.HOUSE_DOOR, 3 * EPS)):
        m.poly([(cx + r * math.cos(a), cy + r * math.sin(a), z0 - off)
                for a in [k * 2 * math.pi / 16 for k in range(16)]], color, MAT)
    for a, b in (((cx - 1.05, cy), (cx + 1.05, cy)), ((cx, cy - 1.05), (cx, cy + 1.05))):
        _ribbon(m, a, b, 0.18, lambda u, v: (u, v, z0 - 4 * EPS), P.HOUSE_TRIM)


def bowl(m):
    """A lighter ceramic rim and a heaped mound of kibble in the middle."""
    (bx, bz), r, top = L.SPEC['bowl']
    y = top + EPS
    sides = 24
    for k in range(sides):
        a0, a1 = 2 * math.pi * k / sides, 2 * math.pi * (k + 1) / sides
        m.poly([(bx + r * math.cos(a0), y, bz + r * math.sin(a0)), (bx + r * math.cos(a1), y, bz + r * math.sin(a1)),
                (bx + (r - 0.45) * math.cos(a1), y, bz + (r - 0.45) * math.sin(a1)),
                (bx + (r - 0.45) * math.cos(a0), y, bz + (r - 0.45) * math.sin(a0))], P.BOWL_RIM, MAT)
    rng = random.Random(5)
    for _ in range(70):
        a, d = rng.uniform(0, 2 * math.pi), rng.uniform(0, r - 0.7)
        h = 0.12 * (1 - d / r)
        x, z = bx + d * math.cos(a), bz + d * math.sin(a)
        s = rng.uniform(0.10, 0.16)
        color = rng.choice(P.KIBBLES)
        pts = [(x + s * math.cos(a + k * 2.1), y + h + 0.05, z + s * math.sin(a + k * 2.1)) for k in range(3)]
        apex = (x, y + h + 0.10, z)
        for i in range(3):
            m.poly([pts[i], pts[(i + 1) % 3], apex], color, MAT)


def deck(m):
    """A lighter inset panel on the wheel deck's top."""
    x0, x1, _, top, z0, z1 = L.SPEC['deck']
    y, i = top + EPS, 0.7
    m.poly([(x0 + i, y, z0 + i), (x0 + i, y, z1 - i), (x1 - i, y, z1 - i), (x1 - i, y, z0 + i)], P.DECK_PANEL, MAT)


def wheel_rungs(m):
    """Rungs across the inside of the clear wheel."""
    (wx, wy), (wz0, wz1) = L.SPEC['wheel']
    r = L.WHEEL_INNER * math.cos(math.pi / L.WHEEL_SIDES) - 0.03
    for k in range(L.WHEEL_SIDES):
        a = 2 * math.pi * (k + 0.5) / L.WHEEL_SIDES
        c, s = math.cos(a), math.sin(a)
        t = (-s * 0.14, c * 0.14)
        m.poly([(wx + r * c - t[0], wy + r * s - t[1], wz0 + 0.05), (wx + r * c + t[0], wy + r * s + t[1], wz0 + 0.05),
                (wx + r * c + t[0], wy + r * s + t[1], wz1 - 0.05), (wx + r * c - t[0], wy + r * s - t[1], wz1 - 0.05)],
               P.WHEEL_RIM, MAT)


def tp_roll(m):
    """Cardboard with a spiral seam outside and a darker inside (finer faces than the collision)."""
    (tx, ty), (tz0, tz1), tr = L.SPEC['tp_roll']
    for t in L.annulus(L.straight((tx, ty, tz0), (tx, ty, tz1), 12), tr, 0.5):
        cx = sum(p[0] for p in t) / 3
        cy = sum(p[1] for p in t) / 3
        cz = sum(p[2] for p in t) / 3
        radius = math.hypot(cx - tx, cy - ty)
        if radius < tr + 0.25:
            color = P.CARDBOARD_INSIDE
        else:
            turn = (math.atan2(cy - ty, cx - tx) / (2 * math.pi)) * 2.2 + (cz - tz0) / 2.2
            color = P.CARDBOARD_SEAM if turn % 1.0 < 0.16 else P.CARDBOARD
        m.poly(list(t), color, MAT)
