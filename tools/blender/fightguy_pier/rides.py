"""The fair on the pier: the Big Wheel at the end of the pier behind the ring (FightGuy's grin on
the hub, the gondolas he borrowed for his staircase missing), the carousel house, the High Striker, the shooting gallery, the dunk tank,
the car bash pen, the entrance arch, the ring with its posts and ropes, the cardboard crowd,
the light towers, the old pier's derrick, the bag frame, the coaster's track between its
platforms, string lights and bunting.

These are fixtures without collision. Anything standing on a walkable top keeps to its
edges or is declared below (DECLARED); the wheel, the coaster track and the crowd stand over
the sea beside the route. build.py checks every fixture's footprint against the route
pieces and the target positions.
"""
from __future__ import annotations

import math

from stagekit import geo
import palette as P
import parts as K
import textures as T

# The Big Wheel stands in the sea past the pier head, the backdrop to the ring; the gameplay
# camera never looks up, so it is low enough to read behind the ring from the coaster and the
# tower top.
WHEEL_Z = 60.0
HUB = (5.0, 13.5, WHEEL_Z)
WHEEL_R = 7.0
GONDOLAS = 12
BORROWED = (0, 2, 9, 11)   # gondola slots counted from the bottom, now the pods by the tower
RING_POSTS = 1.4           # corner post height above the ring canvas
# Fixtures that stand on a walkable top away from its edges, on purpose.
DECLARED = {"striker pole", "ring posts", "bag frame", "carousel finger", "derrick", "wheel", "car pen", "booths"}


class Rides:
    def __init__(self, b):
        self.b = b
        self.P = b.pieces
        self.rng = b.rng

    def build(self):
        self.wheel()
        self.carousel()
        self.striker()
        self.gallery()
        self.dunk_tank()
        self.car_pen()
        self.arch()
        self.ring()
        self.crowd()
        self.light_towers()
        self.derrick()
        self.bag_frame()
        self.coaster_track()
        self.collapse()
        self.booths()
        self.strings()

    def fixture(self, name, group, build, footprint=None, declared=False):
        self.b.part("fixture", group, name, build)
        self.b.fixtures.append((name, footprint, declared or name in DECLARED))

    # ------------------------------------------------------------ the Big Wheel
    def wheel(self):
        hx, hy, hz = HUB

        def rim_point(a, r=WHEEL_R, dz=0.0):
            return (hx + r * math.sin(a), hy - r * math.cos(a), hz + dz)

        def build(m):
            seg = 48
            for dz in (-0.45, 0.45):
                pts = [rim_point(2 * math.pi * k / seg, WHEEL_R, dz) for k in range(seg + 1)]
                geo.tube(m, pts, 0.11, 6, P.WHITE, K.LIT)
                pts = [rim_point(2 * math.pi * k / seg, WHEEL_R - 0.7, dz) for k in range(seg + 1)]
                geo.tube(m, pts, 0.07, 5, P.GOLD_DARK, K.LIT)
            for k in range(16):
                a = 2 * math.pi * k / 16
                for dz in (-0.45, 0.45):
                    K.beam(m, (hx, hy, hz + dz * 0.6), rim_point(a, WHEEL_R, dz), 0.07, P.WHITE)
                K.beam(m, rim_point(a, WHEEL_R, -0.45), rim_point(a, WHEEL_R, 0.45), 0.06, P.STEEL)
            # The axle drum and the hub: FightGuy's grin facing the start.
            geo.cylinder_between(m, (hx, hy, hz - 0.7), (hx, hy, hz + 0.7), 1.0, 16, P.GOLD_DARK, K.LIT)
            front = K.Plane.facing((hx, 0.0, hz - 0.72), (0.0, -1.0))
            K.disc(m, front, 0.0, hy, 2.55, P.GOLD, off=0.0, n=32)
            K.textured_disc(m, front, 0.0, hy, 2.25, "hub", off=0.02)
            back = K.Plane.facing((hx, 0.0, hz + 0.72), (0.0, 1.0))
            K.disc(m, back, 0.0, hy, 2.4, P.GOLD, off=0.0, n=24)
            # The A-frame legs, down into the sea on their own piles.
            for sx in (-1, 1):
                for dz in (-0.8, 0.8):
                    K.beam(m, (hx, hy, hz + dz * 0.5), (hx + sx * 6.2, K.WATER_FLOOR + 0.1, hz + dz), 0.36, P.WHITE)
                K.beam(m, (hx + sx * 3.6, 5.6, hz - 0.8), (hx + sx * 3.6, 5.6, hz + 0.8), 0.18, P.WHITE)
                geo.box(m, (hx + sx * 6.2, 0.4, hz), (1.2, 1.2, 2.4), P.PILE_DARK, K.LIT)
            # Gondolas on the rim, three missing (the champ borrowed them: the pods).
            for k in range(GONDOLAS):
                if k in BORROWED:
                    continue
                a = 2 * math.pi * k / GONDOLAS
                px, py, pz = rim_point(a)
                c = [P.RED, P.GI_BLUE, P.GOLD_DARK, P.TEAL][k % 4]
                K.beam(m, (px, py, pz), (px, py - 0.55, pz), 0.05, P.STEEL_DARK)
                geo.box(m, (px, py - 1.05, pz), (1.3, 0.9, 1.0), c, K.LIT, top_color=geo.scale(c, 0.8))
                geo.box(m, (px, py - 1.0, pz), (1.32, 0.35, 0.8), P.GLASS, K.LIT, skip=("top", "bottom"))
        self.fixture("wheel", "Big Wheel", build)

        def lights(m):
            seg = 64
            for i in range(seg):
                a = 2 * math.pi * i / seg
                K.bulb(m, rim_point(a, WHEEL_R + 0.18, -0.5), 0.09, P.BULB if i % 2 else P.NEON_GOLD, (6.0, 0.0), i / seg)
            for k in range(16):
                a = 2 * math.pi * k / 16
                for j in range(1, 7):
                    r = 2.6 + (WHEEL_R - 2.6) * j / 7
                    K.bulb(m, rim_point(a, r, -0.5), 0.06, P.NEON_PINK if k % 2 else P.NEON_BLUE, (3.0, 0.0), (j / 7 + k / 16) % 1.0)
        self.fixture("wheel lights", "Big Wheel Lights", lights)

    # ------------------------------------------------------------ the carousel house
    def carousel(self):
        x0, z0, x1, z1, top = self.P["Carousel"]
        base = 3.0

        def build(m):
            for key, plane, u0, u1 in K.box_planes(x0, z0, x1, z1):
                K.panel(m, plane, u0, base, u1, top - 0.7, P.CREAM, off=0.0)
                n = 3
                w = (u1 - u0) / n
                for i in range(n):
                    uc = u0 + w * (i + 0.5)
                    # an arched niche with a funhouse mirror of the champ
                    K.slab(m, plane, uc - w * 0.4, base + 0.15, uc + w * 0.4, top - 1.0, 0.04, P.GI_BLUE)
                    K.plate(m, plane, "mirror", uc, base + 1.55, w * 0.55, off=0.06)
                for u in [u0 + w * i for i in range(n + 1)]:
                    K.slab(m, plane, u - 0.14, base, u + 0.14, top - 0.7, 0.1, P.GOLD)
                # the valance: a red band with bulbs and a scalloped edge
                K.panel(m, plane, u0, top - 0.7, u1, top, P.AWNING_RED, off=0.08)
                for i in range(int((u1 - u0) / 0.45)):
                    uc = u0 + 0.22 + i * 0.45
                    K.bulb(m, plane.at(uc, top - 0.32, 0.14), 0.06, P.BULB, (4.0, 0.0), (i % 3) / 3)
                    tri = [plane.at(uc - 0.22, top - 0.7, 0.09), plane.at(uc + 0.22, top - 0.7, 0.09), plane.at(uc, top - 0.95, 0.09)]
                    geo.oriented(m, tri, plane.n, P.AWNING_WHITE if i % 2 else P.AWNING_RED, K.LIT)
        self.fixture("carousel house", "Carousel", build, (x0, z0, x1, z1), declared=True)

        def finger(m):
            # A gold foam #1 finger on the roof: the champ's weathervane.
            cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
            K.round_post(m, cx, cz, top, top + 0.7, 0.08, P.GOLD_DARK, 8)
            geo.box(m, (cx, top + 1.0, cz), (0.6, 0.6, 0.25), P.GOLD, K.LIT)
            geo.box(m, (cx, top + 1.65, cz), (0.18, 0.75, 0.2), P.GOLD, K.LIT)
            plane = K.Plane.facing((cx, 0.0, cz - 0.13), (0.0, -1.0))
            K.disc(m, plane, 0.0, top + 1.0, 0.16, P.INK, off=0.0, n=10)
        self.fixture("carousel finger", "Carousel", finger, ((x0 + x1) / 2 - 0.4, (z0 + z1) / 2 - 0.4, (x0 + x1) / 2 + 0.4, (z0 + z1) / 2 + 0.4))

    # ------------------------------------------------------------ the High Striker
    def striker(self):
        x, z = -4.9, -24.0

        def build(m):
            K.post(m, x, z, 3.0, 11.0, 0.22, P.CREAM)
            plane = K.Plane.facing((x + 0.12, 0.0, z), (1.0, 0.0))
            K.plate(m, plane, "striker", 0.0, 7.0, 0.62, off=0.02, height=6.6)
            # the bell, the puck at the bottom, the pad and the mallet
            geo.lathe(m, (x + 0.05, 11.05, z), [(0.42, 0.0), (0.38, 0.12), (0.22, 0.42), (0.06, 0.5)], 12, P.GOLD, K.LIT)
            K.beacon(m, (x + 0.05, 11.75, z), 0.14, P.BULB, K.PULSE, 0.0)
            geo.box(m, (x + 0.22, 3.75, z), (0.22, 0.28, 0.3), P.RED, K.LIT)
            geo.box(m, (x + 0.4, 3.12, z), (0.7, 0.24, 0.5), P.STEEL_DARK, K.LIT, skip=("bottom",))
            K.beam(m, (x - 0.5, 3.02, z + 0.6), (x - 0.42, 4.1, z + 0.4), 0.06, P.PLANK_DARK)
            geo.box(m, (x - 0.42, 4.18, z + 0.4), (0.24, 0.26, 0.42), P.RED_DARK, K.LIT)
            # guy wires
            for dz in (-1.2, 1.2):
                geo.tube(m, [(x, 10.4, z), (x - 0.9, 3.0, z + dz)], 0.012, 3, P.INK, K.LIT)
        self.fixture("striker pole", "Midway", build, (x - 0.6, z - 1.3, x + 0.8, z + 1.3))

    # ------------------------------------------------------------ the shooting gallery
    def gallery(self):
        """An open-air gallery on stilts over the sea beside the midway: a low counter on the deck
        edge and the duck wall well out, so the camera clears it from across the midway."""
        x0, x1, z0, z1 = 6.05, 11.0, -35.0, -24.6

        def build(m):
            deck = 3.0
            for z in (z0 + 0.2, (z0 + z1) / 2, z1 - 0.2):
                for x in (x0 + 0.25, (x0 + x1) / 2, x1 - 0.2):
                    K.round_post(m, x, z, K.WATER_FLOOR, deck - 0.05, 0.13, P.PILE, 6)
            geo.box(m, ((x0 + x1) / 2, deck - 0.12, (z0 + z1) / 2), (x1 - x0, 0.24, z1 - z0), P.PLANK_DARK, K.LIT)
            # the counter, striped, knee high
            geo.box(m, (x0 + 0.2, deck + 0.4, (z0 + z1) / 2), (0.4, 0.8, z1 - z0), P.AWNING_RED, K.LIT, skip=("bottom",))
            for i in range(int(z1 - z0)):
                geo.box(m, (x0 + 0.01, deck + 0.4, z0 + 0.5 + i), (0.02, 0.8, 0.5), P.AWNING_WHITE, K.LIT, skip=("bottom",))
            # the duck wall at the back, with its banner on top
            back = K.Plane.facing((x1, 0.0, 0.0), (-1.0, 0.0))
            K.panel(m, back, -z1, deck, -z0, deck + 2.8, P.GI_BLUE_DARK, off=0.0)
            for row in range(2):
                for i in range(10):
                    u = -z1 + 0.6 + i * (z1 - z0 - 1.2) / 9
                    v = deck + 1.0 + row * 1.0
                    K.disc(m, back, u, v, 0.22, P.GOLD, off=0.03, n=10)
                    K.disc(m, back, u + 0.18, v + 0.12, 0.1, P.GOLD, off=0.03, n=8)
            K.plate(m, back, "gallery", -(z0 + z1) / 2, deck + 3.3, z1 - z0, off=0.02, height=0.9)
            for z in (z0, z1):
                K.post(m, x1 - 0.1, z, deck, deck + 3.8, 0.14, P.GOLD_DARK)
        self.fixture("shooting gallery", "Midway", build)

    # ------------------------------------------------------------ the dunk tank
    def dunk_tank(self):
        cx, cz = -19.85, -30.0

        def build(m):
            geo.lathe(m, (cx, K.WATER_FLOOR, cz), [(0.75, 0.0), (0.75, 3.1)], 14, P.PILE_DARK, K.LIT, cap_top=False)
            geo.lathe(m, (cx, 2.9, cz), [(0.78, 0.0), (0.78, 1.5)], 14, P.GLASS, K.LIT, cap_top=False)
            geo.lathe(m, (cx, 3.6, cz), [(0.74, 0.0), (0.74, 0.02)], 14, P.WATER_TANK, K.LIT)
            # the empty seat above it
            geo.box(m, (cx, 4.55, cz), (0.9, 0.08, 0.5), P.PLANK_LIGHT, K.LIT)
            K.beam(m, (cx - 0.5, 2.9, cz - 0.6), (cx - 0.5, 4.6, cz - 0.6), 0.07, P.STEEL)
            K.beam(m, (cx - 0.5, 2.9, cz + 0.6), (cx - 0.5, 4.6, cz + 0.6), 0.07, P.STEEL)
            # the sign on the back frame, facing the midway
            plane = K.Plane.facing((cx - 0.6, 0.0, cz), (1.0, 0.0))
            K.post(m, cx - 0.62, cz - 0.9, 2.9, 5.6, 0.08, P.STEEL)
            K.post(m, cx - 0.62, cz + 0.9, 2.9, 5.6, 0.08, P.STEEL)
            K.board(m, plane, "dunk", 0.0, 5.55, 2.0, frame=P.GOLD, depth=0.05)
        self.fixture("dunk tank", "Midway", build)

    # ------------------------------------------------------------ the car bash pen
    def car_pen(self):
        cx, cz = 12.0, -43.0

        def build(m):
            # a low barrier on three sides, open towards the start
            for (ax, az), (bx, bz) in (((cx - 2.2, cz - 2.6), (cx + 2.6, cz - 2.6)), ((cx + 2.6, cz - 2.6), (cx + 2.6, cz + 2.6)),
                                       ((cx + 2.6, cz + 2.6), (cx - 2.2, cz + 2.6))):
                L = math.dist((ax, az), (bx, bz))
                n = max(1, int(L / 1.2))
                for i in range(n):
                    t0, t1 = i / n, (i + 1) / n
                    c = P.RED if i % 2 == 0 else P.WHITE
                    p0 = (ax + (bx - ax) * t0, az + (bz - az) * t0)
                    p1 = (ax + (bx - ax) * t1, az + (bz - az) * t1)
                    yaw = math.atan2(p1[0] - p0[0], p1[1] - p0[1])
                    geo.box(m, ((p0[0] + p1[0]) / 2, 3.45, (p0[1] + p1[1]) / 2), (0.12, 0.9, L / n - 0.05), c, K.LIT,
                            yaw=yaw, skip=("bottom",))
            plane = K.Plane.facing((cx + 2.9, 0.0, cz), (-1.0, 0.0))
            K.post(m, cx + 2.95, cz - 1.3, 3.0, 6.2, 0.12, P.STEEL)
            K.post(m, cx + 2.95, cz + 1.3, 3.0, 6.2, 0.12, P.STEEL)
            K.board(m, plane, "car_bash", 0.0, 5.3, 2.6, frame=P.INK, depth=0.06)
            # tyres stacked in the corner
            for k in range(3):
                geo.lathe(m, (cx + 2.0, 3.0 + k * 0.26, cz + 2.0), [(0.25, 0.0), (0.42, 0.0), (0.44, 0.13), (0.42, 0.26), (0.25, 0.26)],
                          12, P.INK, K.LIT, cap_top=False)
        self.fixture("car pen", "Promenade", build, (cx - 2.3, cz - 2.7, cx + 3.1, cz + 2.7))

    # ------------------------------------------------------------ the entrance arch
    def arch(self):
        z = -38.4

        def build(m):
            for sx in (-1, 1):
                x = sx * 6.6
                K.lattice(m, x, z, 0.35, 3.0, 14.4, P.GOLD_DARK, bay=1.5, chord=0.12, brace=0.06)
                K.beacon(m, (x, 14.8, z), 0.25, P.BULB, K.PULSE, 0.25 * (sx + 1))
            # High enough that the orbit camera passes under the sign up and down the midway.
            plane = K.Plane.facing((0.0, 0.0, z - 0.06), (0.0, -1.0))
            K.board(m, plane, "arch", 0.0, 13.0, 10.4, frame=P.GOLD, depth=0.18, rim=0.12)
            back = K.Plane.facing((0.0, 0.0, z + 0.3), (0.0, 1.0))
            K.board(m, back, "welcome", 0.0, 13.0, 8.0, frame=P.GI_BLUE, depth=0.1)
            K.pennants(m, (-6.6, 11.6, z), (6.6, 11.6, z), 0.32, [P.GI_BLUE, P.GOLD, P.RED, P.WHITE], sag=0.5)
        self.fixture("arch", "Arch", build)

    # ------------------------------------------------------------ the ring
    def ring(self):
        x0, z0, x1, z1, top = self.P["Ring"]
        inset = 0.25
        corners = [(x0 + inset, z0 + inset), (x1 - inset, z0 + inset), (x1 - inset, z1 - inset), (x0 + inset, z1 - inset)]

        def build(m):
            for i, (cx, cz) in enumerate(corners):
                K.round_post(m, cx, cz, top, top + RING_POSTS, 0.09, P.STEEL, 8)
                geo.box(m, (cx, top + RING_POSTS - 0.35, cz), (0.32, 0.7, 0.32), P.RED if i % 2 == 0 else P.GI_BLUE, K.LIT)
            for k, (h, c) in enumerate(((0.45, P.CREAM), (0.85, P.RED), (1.25, P.GI_BLUE))):
                for i in range(4):
                    a, b = corners[i], corners[(i + 1) % 4]
                    geo.tube(m, [(a[0], top + h, a[1]), (b[0], top + h, b[1])], 0.035, 6, c, K.LIT)
            # The banner on two poles behind the ring.
            for x in (x0 + 0.6, x1 - 0.6):
                K.post(m, x, z1 + 1.2, 3.0, 9.4, 0.14, P.STEEL_DARK)
            plane = K.Plane.facing((0.0, 0.0, z1 + 1.1), (0.0, -1.0))
            K.board(m, plane, "ring_banner", (x0 + x1) / 2, 8.5, x1 - x0 - 1.0,
                    frame=P.GOLD, depth=0.08)
        self.fixture("ring posts", "Ring", build, (x0, z0, x1, z1))

    def crowd(self):
        """Bleachers on stilts over the sea west of the pier head, packed with cardboard fans."""
        def build(m):
            xs = (-16.2, -14.2, -12.2)
            for t, x in enumerate(xs):
                y = 3.2 + t * 0.6
                geo.box(m, (x + 1.0, y, 46.0), (2.0, 0.16, 12.0), P.PLANK_DARK, K.LIT)
                for z in (40.4, 46.0, 51.6):
                    K.round_post(m, x + 1.0, z, K.WATER_FLOOR, y - 0.08, 0.1, P.PILE, 6)
                for i in range(7):
                    z = 40.8 + i * 1.75 + (0.4 if t % 2 else 0.0)
                    cell = "fan_%d" % ((i + t) % 4)
                    plane = K.Plane.facing((x + 1.2, 0.0, z), (1.0, 0.0))
                    K.plate(m, plane, cell, 0.0, y + 1.0, 0.8, off=0.0, height=1.6)
                    back = K.Plane.facing((x + 1.18, 0.0, z), (-1.0, 0.0))
                    K.panel(m, back, -0.4, y + 0.2, 0.4, y + 1.8, P.CARDBOARD_DARK)
        self.fixture("crowd", "Crowd", build)

    def light_towers(self):
        def build(m):
            for x, z in ((-11.0, 55.0), (17.0, 55.0)):
                K.lattice(m, x, z, 0.4, K.WATER_FLOOR, 11.0, P.STEEL_DARK, bay=1.8)
                geo.box(m, (x, 11.3, z), (1.8, 0.6, 0.5), P.INK, K.LIT)
                for dx in (-0.55, 0.0, 0.55):
                    with K.glow(m):
                        K.glow_ellipsoid(m, (x + dx, 11.3, z - 0.3), (0.22, 0.22, 0.08), P.BEAM)
        self.fixture("light towers", "Ring", build)

    # ------------------------------------------------------------ the old pier
    def derrick(self):
        bx, bz = -32.2, -11.6

        def build(m):
            top = (-30.8, 7.2, -9.8)
            K.beam(m, (bx - 0.6, 3.0, bz), (bx + 0.4, 7.0, bz + 0.6), 0.2, P.CHAR)
            K.beam(m, (bx + 0.6, 3.0, bz - 0.4), (bx + 0.4, 7.0, bz + 0.6), 0.2, P.CHAR)
            K.beam(m, (bx + 0.4, 7.0, bz + 0.6), top, 0.16, P.CHAR_LIGHT)
            geo.tube(m, [top, (-30.0, 5.8, -9.5)], 0.03, 4, P.CREAM, K.LIT)
            geo.box(m, (-30.0, 5.86, -9.5), (0.12, 0.12, 0.12), P.IRON, K.LIT)
            plane = K.Plane.facing((-20.3, 0.0, -12.0), (1.0, 0.0))
            K.post(m, -20.45, -12.6, 3.0, 4.9, 0.1, P.CHAR)
            K.post(m, -20.45, -11.4, 3.0, 4.9, 0.1, P.CHAR)
            K.board(m, plane, "keep_off", 0.0, 4.4, 1.5, frame=P.CHAR, depth=0.05)
        self.fixture("derrick", "Old Pier", build, (bx - 0.8, bz - 0.6, bx + 0.8, bz + 0.8))

    def bag_frame(self):
        cx, cz = 13.0, 42.0

        def build(m):
            for dx in (-1.25, 1.25):
                K.post(m, cx + dx, cz, 3.0, 6.6, 0.16, P.STEEL_DARK)
            K.beam(m, (cx - 1.3, 6.55, cz), (cx + 1.3, 6.55, cz), 0.16, P.STEEL_DARK)
            geo.tube(m, [(cx, 6.5, cz), (cx, 5.4, cz)], 0.025, 4, P.IRON, K.LIT)
            plane = K.Plane.facing((cx, 0.0, cz + 0.12), (0.0, 1.0))
            K.board(m, plane, "photo", 0.0, 7.4, 2.0, frame=P.GI_BLUE, depth=0.05)
        self.fixture("bag frame", "Pier Head", build, (cx - 1.4, cz - 0.2, cx + 1.4, cz + 0.2))

    # ------------------------------------------------------------ the coaster's track
    def coaster_track(self):
        P_ = self.P

        def rails(m, pts, gauge=1.1):
            """Two red rails along a polyline with ties, raised on the given points."""
            left, right = [], []
            for i, p in enumerate(pts):
                q = pts[min(i + 1, len(pts) - 1)] if i < len(pts) - 1 else pts[i]
                pp = pts[max(i - 1, 0)]
                dx, dz = q[0] - pp[0], q[2] - pp[2]
                L = math.hypot(dx, dz) or 1.0
                nx, nz = -dz / L * gauge / 2, dx / L * gauge / 2
                left.append((p[0] + nx, p[1], p[2] + nz))
                right.append((p[0] - nx, p[1], p[2] - nz))
            geo.tube(m, left, 0.07, 6, P.COASTER, K.LIT)
            geo.tube(m, right, 0.07, 6, P.COASTER, K.LIT)
            for a, b in zip(left, right):
                K.beam(m, (a[0], a[1] - 0.08, a[2]), (b[0], b[1] - 0.08, b[2]), 0.08, P.STEEL_DARK)

        def bez(a, b, c, d, n=14):
            return [tuple((1 - t) ** 3 * a[k] + 3 * (1 - t) ** 2 * t * b[k] + 3 * (1 - t) * t * t * c[k] + t ** 3 * d[k] for k in range(3))
                    for t in [i / n for i in range(n + 1)]]

        def build(m):
            lift, drop, straight, end = (P_[n] for n in ("LiftHill", "CoasterDrop", "CoasterStraight", "CoasterEnd"))
            # Rails along every platform's long edges, low, so they read as track, not walls.
            for x0, z0, x1, z1, top in (lift, drop, end):
                rails(m, [(x0, top + 0.12, (z0 + z1) / 2), (x1, top + 0.12, (z0 + z1) / 2)])
            x0, z0, x1, z1, top = straight
            rails(m, [((x0 + x1) / 2, top + 0.12, z0), ((x0 + x1) / 2, top + 0.12, z1)])
            # The track between platforms dips below their tops (no walkway in the gaps).
            rails(m, bez((lift[2], lift[4] - 0.4, 27.0), (10.5, lift[4] - 2.5, 27.0), (11.0, drop[4] - 2.0, 26.0), (drop[0], drop[4] - 0.4, 26.0)))
            rails(m, bez((drop[2], drop[4] - 0.4, 26.0), (18.0, drop[4] - 3.0, 26.0), (19.0, straight[4] - 2.5, 22.0), (straight[0] + 0.5, straight[4] - 0.4, 21.0)))
            rails(m, bez((22.0, straight[4] - 0.4, straight[3]), (22.0, straight[4] - 2.5, 41.5), (22.0, end[4] - 2.0, 42.5), (22.0, end[4] - 0.4, end[1])))
            # The loop out over the sea east of the straight, on white towers.
            loop = [(29.0 + 4.6 * math.sin(a), 9.0 - 4.6 * math.cos(a), 30.0 + 0.9 * a / math.pi) for a in [i * 2 * math.pi / 28 for i in range(29)]]
            rails(m, loop)
            rails(m, bez((24.5, straight[4] - 0.6, 24.0), (27.0, 3.8, 26.0), (28.0, 4.4, 29.0), loop[0]))
            rails(m, bez(loop[-1], (30.0, 4.4, 33.0), (27.5, 3.6, 35.5), (24.5, straight[4] - 0.6, 37.0)))
            for z in (27.0, 31.0, 35.0):
                K.lattice(m, 29.0, z, 0.3, K.WATER_FLOOR, 4.0, P.SUPPORT, bay=1.6, chord=0.08, brace=0.04)
            plane = K.Plane.facing((24.1, 0.0, 30.0), (1.0, 0.0))
            K.board(m, plane, "coaster", 0.0, 7.3, 2.6, frame=P.CREAM, depth=0.05)
        self.fixture("coaster track", "Coaster", build)

    # ------------------------------------------------------------ the collapsed middle
    def collapse(self):
        """Broken pile stubs in the gap, clearly too low and splintered to stand on."""
        def build(m):
            rng = self.rng
            for i in range(14):
                x = rng.uniform(-6.0, 6.0)
                z = rng.uniform(-1.5, 13.5)
                h = rng.uniform(0.2, 0.9)
                lean = (rng.uniform(-0.6, 0.6), rng.uniform(-0.6, 0.6))
                K.round_post(m, x, z, K.WATER_FLOOR, h, 0.15, P.PILE_DARK, 6, lean=lean)
            for i in range(5):
                x = rng.uniform(-5.0, 5.0)
                z = rng.uniform(0.0, 12.0)
                geo.box(m, (x, 0.0, z), (2.4, 0.08, 0.25), P.PLANK_DARK, K.LIT, yaw=rng.uniform(0, math.pi), skip=("bottom",))
            # PIER CLOSED barrier at the plaza's north edge.
            for x in (-3.4, 3.4):
                K.post(m, x, -4.25, 3.0, 4.2, 0.1, P.WHITE)
            for k, y in enumerate((3.55, 3.95)):
                geo.box(m, (0.0, y, -4.25), (6.8, 0.18, 0.08), P.RED if k == 0 else P.WHITE, K.LIT)
            plane = K.Plane.facing((0.0, 0.0, -4.32), (0.0, -1.0))
            K.board(m, plane, "collapsed", 0.0, 4.9, 2.4, frame=P.RED, depth=0.05)
        self.fixture("collapse", "Plaza", build, (-3.6, -4.4, 3.6, -4.1))

    # ------------------------------------------------------------ booths and signs off the route
    def booths(self):
        def build(m):
            # 肉 DOGS on the promenade's west end.
            x, z = -16.5, -44.0
            geo.box(m, (x, 4.1, z), (3.0, 2.2, 2.0), P.CREAM, K.LIT, skip=("bottom",))
            front = K.Plane.facing((x, 0.0, z + 1.0), (0.0, 1.0))
            K.board(m, front, "niku_dogs", 0.0, 5.75, 2.6, frame=P.RED, depth=0.05)
            K.awning(m, x - 1.5, x + 1.5, z + 1.0, 4.9, 0.8, 0.3, "awning", facing=1.0)
            # NO FIGHTING sign on the promenade.
            plane = K.Plane.facing((-9.0, 0.0, -38.6), (0.0, -1.0))
            K.post(m, -9.0, -38.5, 3.0, 4.4, 0.08, P.STEEL)
            K.board(m, plane, "no_fighting", 0.0, 4.9, 2.0, frame=P.INK, depth=0.04)
            # HALL OF MIRRORS on the carousel's back (the champ's favourite).
            x0, z0, x1, z1, top = self.P["Carousel"]
            plane = K.Plane.facing((x1 + 0.12, 0.0, 0.0), (1.0, 0.0))
            K.board(m, plane, "mirrors", (z0 + z1) / 2, top + 0.7, 3.4, frame=P.GOLD, depth=0.06)
            # TICKETS on the ticket raft's corner.
            plane = K.Plane.facing((-18.8, 0.0, 30.8), (0.0, -1.0))
            K.post(m, -18.7, 30.75, 3.4, 5.0, 0.07, P.STEEL)
            K.board(m, plane, "tickets", 0.0, 4.8, 1.0, frame=P.GOLD, depth=0.04)
            # The prize stall's shelf on the prize raft's back edge, stacked with plush champs.
            for k in range(4):
                geo.box(m, (22.9, 2.6 + 0.35 + 0.0 * k, 51.4 + k * 0.75), (0.25, 0.7, 0.55), [P.GI_BLUE, P.RED, P.GOLD, P.GI_BLUE][k], K.LIT)
            plane = K.Plane.facing((23.1, 0.0, 52.5), (-1.0, 0.0))
            K.post(m, 23.05, 51.2, 2.6, 4.6, 0.07, P.STEEL)
            K.post(m, 23.05, 53.8, 2.6, 4.6, 0.07, P.STEEL)
            K.board(m, plane, "prizes", 0.0, 4.2, 2.2, frame=P.GOLD, depth=0.04)
        self.fixture("booths", "Booths", build)

    # ------------------------------------------------------------ lights over the route
    def strings(self):
        def build(m):
            # Zig-zag bulb strings across the midway between the lamp posts, high over the route.
            for (za, zb) in ((-35.5, -29.0), (-26.0, -23.0), (-20.0, -16.8), (-35.5, -35.5), (-16.8, -16.8)):
                K.bulb_line(m, (-5.6, 7.4, za), (5.6, 7.4, zb), 0.6, 0.075, P.BULB, sag=0.6, chase=2.0)
            # Across the pier head round the ring, and along the arch.
            K.bulb_line(m, (-9.6, 7.4, 38.6), (15.6, 7.4, 38.6), 0.6, 0.075, P.BULB_WARM, sag=0.8, chase=1.5)
            K.bulb_line(m, (-9.6, 7.4, 53.4), (15.6, 7.4, 53.4), 0.6, 0.075, P.BULB_WARM, sag=0.8, chase=1.5)
            K.bulb_line(m, (-11.6, 7.4, 16.6), (7.6, 7.4, 16.6), 0.6, 0.075, P.BULB, sag=0.8, chase=2.0)
            for a, b in (((-9.6, 7.2, 38.6), (-9.6, 7.2, 53.4)), ((15.6, 7.2, 38.6), (15.6, 7.2, 53.4))):
                K.pennants(m, a, b, 0.3, [P.GI_BLUE, P.GOLD, P.RED], sag=0.7)
        self.fixture("strings", "Lights", build)
