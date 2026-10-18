"""The landmark: Manki's tower crane on the Last Pillar, dangling a comically large bundle of
dynamite over the pit with its fuse already lit, and the detonator at the finish, wired into
the water. Both are presentation only.

The crane is the one declared exception to the corridor rule. build.py checks its region:
the pillar's footprint above its top, the bundle's column (BUNDLE, BUNDLE_RADIUS, above
BUNDLE_FLOOR) and the sky above CRANE_CLEAR. The gameplay camera looks down, so what must
read hangs low: the bundle sits between 4.6 and 9.8 m, the jib and cab are for the far views.
"""
from __future__ import annotations

import math

from stagekit import geo
import palette as P
import parts as K
import textures as T

PILLAR = "LastPillar"
MAST = 16.0                 # mast top
BUNDLE = (-8.0, -19.0)      # the dynamite's column over open water, left of the start's view of Mount Manki
BUNDLE_BOTTOM, BUNDLE_TOP = 4.6, 9.8
BUNDLE_RADIUS = 3.4         # declared column radius (sign, fuse and smoke included)
BUNDLE_FLOOR = 4.2          # nothing of the bundle hangs lower
CRANE_CLEAR = 15.0          # above this the crane may reach anywhere
STICK_R = 0.8
ROPE = (0.80, 0.66, 0.44)


def truss(m, a, b, width, height, color, bay=1.6, chord=0.11, brace=0.05):
    """A triangular truss from a to b: two bottom chords, a top chord and zig-zag braces."""
    dx, dz = b[0] - a[0], b[2] - a[2]
    L = math.hypot(dx, dz)
    px, pz = -dz / L * width / 2, dx / L * width / 2

    def bl(p):
        return (p[0] + px, p[1], p[2] + pz)

    def br(p):
        return (p[0] - px, p[1], p[2] - pz)

    def tp(p):
        return (p[0], p[1] + height, p[2])
    for f in (bl, br, tp):
        K.beam(m, f(a), f(b), chord, color)
    n = max(1, int(L / bay))
    pts = [tuple(a[k] + (b[k] - a[k]) * i / n for k in range(3)) for i in range(n + 1)]
    for i in range(n):
        p, q = pts[i], pts[i + 1]
        if i % 2 == 0:
            K.beam(m, bl(p), tp(q), brace, color)
            K.beam(m, br(p), tp(q), brace, color)
        else:
            K.beam(m, tp(p), bl(q), brace, color)
            K.beam(m, tp(p), br(q), brace, color)
        K.beam(m, bl(q), br(q), brace, color)


def labelled_stick(m, base, radius, y0, y1, segments=14):
    """An upright dynamite stick: red body with the atlas label wrapped round its middle."""
    x, _, z = base
    mid0, mid1 = y0 + (y1 - y0) * 0.36, y0 + (y1 - y0) * 0.64
    geo.lathe(m, (x, 0.0, z), [(radius, y0), (radius, mid0)], segments, P.DYNAMITE, K.LIT, cap_top=False)
    geo.lathe(m, (x, 0.0, z), [(radius, mid1), (radius, y1), (radius * 0.86, y1 + 0.06), (0.0, y1 + 0.08)], segments,
              [P.DYNAMITE, P.DYNAMITE_DARK, P.DYNAMITE_DARK], K.LIT, cap_top=False)
    geo.lathe(m, (x, 0.0, z), [(0.0, y0 - 0.08), (radius * 0.86, y0 - 0.06), (radius, y0)], segments,
              P.DYNAMITE_DARK, K.LIT, cap_top=False)
    # The label: two wraps of the atlas cell round the circumference.
    (a0, b0), (a1, b1) = T.uv("dynamite")
    r = radius * 1.01
    for k in range(segments):
        t0, t1 = k / segments, (k + 1) / segments
        p = [(x + r * math.cos(2 * math.pi * t), y, z + r * math.sin(2 * math.pi * t)) for t, y in
             ((t0, mid0), (t1, mid0), (t1, mid1), (t0, mid1))]
        f0, f1 = (t0 * 2) % 1.0, (t0 * 2) % 1.0 + 2.0 / segments
        uvs = [(a0 + (a1 - a0) * (1 - f0), b0), (a0 + (a1 - a0) * (1 - f1), b0), (a0 + (a1 - a0) * (1 - f1), b1),
               (a0 + (a1 - a0) * (1 - f0), b1)]
        ang = 2 * math.pi * (t0 + t1) / 2
        geo.oriented(m, p, (math.cos(ang), 0.0, math.sin(ang)), (1.0, 1.0, 1.0), K.LIT, uvs)


class Landmark:
    def __init__(self, b):
        self.b = b
        self.P = b.pieces
        self.rng = b.rng

    def build(self):
        self.crane()
        self.bundle()
        self.detonator()

    def crane(self):
        x0, z0, x1, z1, top = self.P[PILLAR]
        cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
        bx, bz = BUNDLE
        dx, dz = bx - cx, bz - cz
        L = math.hypot(dx, dz)
        ux, uz = dx / L, dz / L
        jib_y = MAST + 1.0

        def build(m):
            with geo.detail(m, 0.3):
                # Footing: a striped concrete block on the pillar.
                geo.box(m, (cx, top + 0.25, cz), (2.6, 0.5, 2.6), P.CONCRETE, K.LIT, skip=("bottom",))
                for k in range(4):
                    a = k * math.pi / 2
                    plane = K.Plane.facing((cx, top, cz), (math.cos(a), math.sin(a)))
                    for i in range(4):
                        u = -1.3 + 0.65 * i
                        geo.oriented(m, [plane.at(u, 0.0, 1.31), plane.at(u + 0.32, 0.0, 1.31), plane.at(u + 0.62, 0.5, 1.31),
                                         plane.at(u + 0.3, 0.5, 1.31)], plane.n, P.HAZARD, K.LIT)
                K.lattice(m, cx, cz, 0.7, top + 0.5, MAST, P.CRANE, bay=1.8, chord=0.16, brace=0.07)
                # Slewing ring and the cab facing along the jib.
                geo.lathe(m, (cx, MAST, cz), [(1.0, 0.0), (1.05, 0.25), (0.9, 0.5)], 12, P.CRANE_DARK, K.LIT)
                cab = (cx + ux * 1.3 - uz * 1.0, MAST + 1.15, cz + uz * 1.3 + ux * 1.0)
                yaw = math.atan2(ux, uz)
                geo.box(m, cab, (1.6, 1.5, 1.9), P.CRANE, K.LIT, yaw=yaw, top_color=P.CRANE_DARK)
                front = K.Plane.facing((cab[0] + ux * 0.96, cab[1] - 0.75, cab[2] + uz * 0.96), (ux, uz))
                K.panel(m, front, -0.65, 0.55, 0.65, 1.35, (0.14, 0.2, 0.26), off=0.01)
                K.googly(m, front, -0.28, 0.95, 0.27, look=(0.6, 0.2), off=0.02)
                K.googly(m, front, 0.33, 1.0, 0.21, look=(-0.5, 0.6), off=0.02)
                # Jib, counter-jib, cat head and ties.
                tip = (cx + ux * (L + 1.2), jib_y, cz + uz * (L + 1.2))
                tail = (cx - ux * 6.5, jib_y, cz - uz * 6.5)
                root = (cx, jib_y, cz)
                truss(m, root, tip, 1.1, 1.2, P.CRANE)
                truss(m, root, tail, 1.3, 0.9, P.CRANE)
                apex = (cx, MAST + 5.2, cz)
                for leg in ((0.6, 0.6), (-0.6, 0.6), (0.6, -0.6), (-0.6, -0.6)):
                    K.beam(m, (cx + leg[0], jib_y, cz + leg[1]), apex, 0.12, P.CRANE)
                for end in (tip, tail):
                    geo.cylinder_between(m, apex, (end[0], end[1] + 1.0, end[2]), 0.035, 4, P.INK, K.LIT)
                # Counterweight: TNT crates (of course).
                for i, (o, h) in enumerate(((-5.2, 0.0), (-5.2, 1.0), (-6.2, 0.0))):
                    K.tnt_crate(m, (cx + ux * o, jib_y + 0.5 + h, cz + uz * o), 1.0, yaw=yaw + 0.05 * i)
                # Trolley and hook block over the bundle.
                trolley = (bx, jib_y - 0.2, bz)
                geo.box(m, trolley, (1.0, 0.4, 0.8), P.STEEL_DARK, K.LIT, yaw=yaw)
                hook = (bx, BUNDLE_TOP + 1.9, bz)
                for off in (-0.2, 0.2):
                    geo.cylinder_between(m, (bx + off * uz, jib_y - 0.4, bz - off * ux), (hook[0] + off * uz, hook[1] + 0.3, hook[2] - off * ux),
                                         0.03, 4, P.INK, K.LIT)
                geo.box(m, hook, (0.7, 0.6, 0.5), P.HAZARD, K.LIT, yaw=yaw)
                for k in range(3):
                    a = 2 * math.pi * k / 3 + 0.3
                    geo.cylinder_between(m, (hook[0], hook[1] - 0.3, hook[2]),
                                         (bx + math.cos(a) * 1.5, BUNDLE_TOP - 0.55, bz + math.sin(a) * 1.5), 0.035, 4, ROPE, K.LIT)
            with K.glow(m, P.MASK_RED, K.BLINK, 0.2):
                K.glow_ellipsoid(m, (apex[0], apex[1] + 0.25, apex[2]), (0.22, 0.22, 0.22), P.MASK_RED)
        self.b.part("crane", "Landmark", "Crane", build)

    def bundle(self):
        bx, bz = BUNDLE
        x0, z0, x1, z1, top = self.P[PILLAR]

        def build(m):
            with geo.detail(m, 0.25):
                for k in range(3):
                    a = 2 * math.pi * k / 3 + 0.3
                    d = STICK_R * 1.16
                    labelled_stick(m, (bx + math.cos(a) * d, 0.0, bz + math.sin(a) * d), STICK_R, BUNDLE_BOTTOM, BUNDLE_TOP)
                for y in (BUNDLE_BOTTOM + 0.9, BUNDLE_TOP - 0.55):
                    geo.lathe(m, (bx, y, bz), [(1.75, -0.12), (1.78, 0.0), (1.75, 0.12)], 16, ROPE, K.LIT, cap_top=False)
                # The fuse, curling up out of the middle.
                fuse = [(bx, BUNDLE_TOP - 0.2, bz), (bx + 0.1, BUNDLE_TOP + 0.5, bz - 0.15), (bx + 0.55, BUNDLE_TOP + 1.0, bz - 0.4),
                        (bx + 1.05, BUNDLE_TOP + 0.9, bz - 0.75), (bx + 1.25, BUNDLE_TOP + 0.45, bz - 0.95)]
                geo.tube(m, fuse, 0.07, 6, (0.32, 0.26, 0.2), K.LIT)
                # NO SMOKING *EXCEPT FUSES, hung on the bundle where the start can read it.
                plane = K.Plane.facing((bx, 0.0, bz - 1.72), (0.0, -1.0))
                K.plate(m, plane, "smoking", 0.0, 7.0, 2.2, off=0.0)
                back = K.Plane.facing((bx, 0.0, bz - 1.72), (0.0, 1.0))
                K.panel(m, back, -1.1, 6.45, 1.1, 7.55, P.WOOD_DARK)
                for u in (-0.8, 0.8):
                    geo.cylinder_between(m, plane.at(u, 7.55), (bx + u, BUNDLE_TOP - 0.55, bz - 1.5), 0.02, 4, ROPE, K.LIT)
            tip = (bx + 1.27, BUNDLE_TOP + 0.4, bz - 0.98)
            K.spark(m, tip, size=0.42, phase=0.3)
            K.spark(m, (tip[0] + 0.05, tip[1] + 0.12, tip[2] - 0.05), size=0.28, phase=0.71)
            K.puff(m, (tip[0] + 0.5, tip[1] + 0.5, tip[2] - 0.3), 0.45, P.SMOKE, seed=3, lumps=4)
            K.puff(m, (tip[0] + 0.7, tip[1] + 1.4, tip[2] - 0.2), 0.6, P.SMOKE, seed=7, lumps=4)
        self.b.part("crane", "Landmark", "Bundle", build)

    def detonator(self):
        """The finish: a big red plunger box east of the finish box, its wire off the dock."""
        piece, (fx, fz), half = self.b.data["spec"]["finish"]
        x0, z0, x1, z1, top = self.P[piece]
        x, z = fx + half[0] + 1.35, fz

        def build(m):
            with geo.detail(m, 0.25):
                geo.box(m, (x, top + 0.38, z), (0.95, 0.76, 0.8), P.DYNAMITE, K.LIT, skip=("bottom",), top_color=P.DYNAMITE_DARK)
                plane = K.Plane.facing((x - 0.476, top, z), (-1.0, 0.0))
                K.plate(m, plane, "kaboom", 0.0, 0.4, 0.6, off=0.005)
                for band in (0.1, 0.62):
                    K.panel(m, plane, -0.4, band, 0.4, band + 0.06, P.HAZARD, off=0.004)
                geo.cylinder_between(m, (x, top + 0.76, z), (x, top + 1.45, z), 0.05, 8, P.STEEL, K.LIT)
                geo.cylinder_between(m, (x, top + 1.45, z - 0.45), (x, top + 1.45, z + 0.45), 0.075, 8, P.WOOD_DARK, K.LIT)
                # The wire: along the deck to the north edge, then over it into the water.
                wire = [(x + 0.3, top + 0.1, z + 0.4), (x + 0.6, top + 0.03, z + 1.2), (x + 0.4, top + 0.03, z + 3.0),
                        (x + 0.9, top + 0.03, z1 - 0.6), (x + 1.0, top + 0.03, z1 - 0.02),
                        (x + 1.0, top - 0.06, z1 + 0.04), (x + 1.05, -0.12, z1 + 0.06)]
                geo.tube(m, wire, 0.03, 4, P.INK, K.LIT)
            with K.glow(m, P.MASK_RED, K.BLINK, 0.6):
                K.glow_ellipsoid(m, (x + 0.32, top + 0.86, z - 0.26), (0.1, 0.1, 0.1), P.MASK_RED)
        self.b.part("detonator", "Props", piece, build)
