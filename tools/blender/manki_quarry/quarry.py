"""Kaboom Quarry's bowl: rounded terraces stepping up outside the blast corridor, Mount Manki
(Manki's own face, cut into the north wall and painted by him, grinning out of the water) and
Manki Demolition's yard on the low shore bench, where the gameplay camera actually looks:
signs with chasing bulbs, the excavator, the TNT water tower, drums, tyres, cones, beacons,
a fresh blast and smoke over the rim. Everything here is background: build.py rejects any
face inside the corridor.

The gameplay camera pitches down and never looks up, so from the route it sees the walls up
to about its own height: the shore bench (y 2.5) and the first face carry what must read.
"""
from __future__ import annotations

import math

from stagekit import geo
import palette as P
import parts as K
import textures as T

CORNER_IN = 3.2          # every tier's corner arcs share centres this far inside the corridor's corners
# Tier faces: (gap from the corridor box, foot y, top y, colour). The first tier is low so the
# orbit camera behind a fighter on an outer island passes over it.
TIERS = [(1.5, -1.0, 2.5, P.OCHRE_DARK), (7.5, 2.5, 9.5, P.OCHRE), (11.5, 9.5, 16.5, P.SANDSTONE),
         (15.5, 16.5, 23.5, P.OCHRE_LIGHT), (19.5, 23.5, 31.0, P.OCHRE)]
BENCHES = [P.SCREE, P.OCHRE_LIGHT, P.SCREE, P.SANDSTONE, (0.80, 0.70, 0.55)]
# Sunlit ochre clipped in the north-facing captures (pass 04): faces and benches sit a step darker.
WALL_TONE, BENCH_TONE = 0.86, 0.88
RIM = 60.0               # the rim plateau's outer gap
SHORE = TIERS[0][2]      # the shore bench's height
NICHE = (-2.0, 26.0)     # Mount Manki's cut through the first two tiers of the north wall (x range)
HEAD = (12.0, 7.0)       # the head's centre on its front plane (x, y)
HEAD_FRONT, HEAD_BACK = 54.0, 60.0


def outline(C, g, step=4.0, arc=7):
    """Points (x, z, nx, nz) CCW round the tier at gap g from the south-east corner, and the
    tag of the segment each point starts. Every tier shares the corner centres and the
    straight-edge samples (the niche's ends included), so index i lines up across tiers."""
    I = CORNER_IN
    cx0, cx1, cz0, cz1 = C["minX"] + I, C["maxX"] - I, C["minZ"] + I, C["maxZ"] - I
    r = g + I

    def samples(a, b, extra=()):
        n = max(1, round(abs(b - a) / step))
        vals = [a + (b - a) * k / n for k in range(1, n)]
        for v in extra:
            vals = [w for w in vals if abs(w - v) > 1.0] + [v]
        return sorted(vals, reverse=b < a)
    pts, tags = [], []
    for tag, ox, oz, a0, edge in (("se", cx1, cz0, -90.0, "e"), ("ne", cx1, cz1, 0.0, "n"),
                                  ("nw", cx0, cz1, 90.0, "w"), ("sw", cx0, cz0, 180.0, "s")):
        for k in range(arc + 1):
            a = math.radians(a0 + 90.0 * k / arc)
            pts.append((ox + r * math.cos(a), oz + r * math.sin(a), math.cos(a), math.sin(a)))
            tags.append(tag if k < arc else edge)
        if edge == "e":
            pts += [(cx1 + r, z, 1.0, 0.0) for z in samples(cz0, cz1)]
        elif edge == "n":
            pts += [(x, cz1 + r, 0.0, 1.0) for x in samples(cx1, cx0, NICHE)]
        elif edge == "w":
            pts += [(cx0 - r, z, -1.0, 0.0) for z in samples(cz1, cz0)]
        else:
            pts += [(x, cz0 - r, 0.0, -1.0) for x in samples(cx0, cx1)]
        tags += [edge] * (len(pts) - len(tags))
    return pts, tags


def in_niche(pts, tags, i):
    a, b = pts[i], pts[(i + 1) % len(pts)]
    return tags[i] == "n" and min(a[0], b[0]) >= NICHE[0] - 1e-6 and max(a[0], b[0]) <= NICHE[1] + 1e-6


def spot(C, side, along, g):
    """(x, z, yaw) on a straight bench g metres out from the corridor box, facing the pit
    (yaw as parts.sign_on_posts: 0 faces south)."""
    if side == "n":
        return along, C["maxZ"] + g, 0.0
    if side == "s":
        return along, C["minZ"] - g, math.pi
    if side == "w":
        return C["minX"] - g, along, -math.pi / 2
    return C["maxX"] + g, along, math.pi / 2


class Quarry:
    def __init__(self, b):
        self.b = b
        self.C = b.C
        self.rng = b.rng
        self.outlines = [outline(self.C, g) for g, _, _, _ in TIERS] + [outline(self.C, RIM)]

    def build(self):
        self.walls()
        self.niche()
        self.head()
        self.yard()
        self.smoke()

    # ------------------------------------------------------------ terraces
    def displaced(self, t, i, y, row, pts):
        """Point i of tier t's face at height y; interior rows bulge (the shore tier only recedes)."""
        x, z, nx, nz = pts[i]
        if row in (0, 3):
            return (x, y, z)
        w = K.mottle(x * 1.7, y * 2.0 + t * 5.0, z * 1.7, 2.0 + t)
        if t == 0:
            d = -0.35 * w                       # recede only: the corridor is 1.5 m away
        elif row == 1:
            d = 1.1 * w                         # lean out over the scree at the foot
        else:
            d = 2.4 * (w - 0.5)
        return (x - nx * d, y, z - nz * d)

    def walls(self):
        def build(m):
            for t, (g, y0, y1, color) in enumerate(TIERS):
                pts, tags = self.outlines[t]
                n = len(pts)
                s = [0.0]
                for i in range(n):
                    a, b = pts[i], pts[(i + 1) % n]
                    s.append(s[-1] + math.hypot(b[0] - a[0], b[1] - a[1]))
                for i in range(n):
                    if t in (0, 1) and in_niche(pts, tags, i):
                        continue
                    foot = -1.0 if t == 2 and in_niche(pts, tags, i) else y0
                    h = y1 - foot
                    ys = [foot, foot + 0.3 * h, foot + 0.7 * h, y1]
                    j2 = (i + 1) % n
                    na = pts[i][2:]
                    nb = pts[j2][2:]
                    inward = (-(na[0] + nb[0]) / 2, 0.0, -(na[1] + nb[1]) / 2)
                    for r in range(3):
                        quad = [self.displaced(t, i, ys[r], r, pts), self.displaced(t, j2, ys[r], r, pts),
                                self.displaced(t, j2, ys[r + 1], r + 1, pts), self.displaced(t, i, ys[r + 1], r + 1, pts)]
                        rows_ = (r, r, r + 1, r + 1)
                        cols = []
                        for (x, y, z), rr in zip(quad, rows_):
                            k = (0.78, 0.96, 1.0, 1.1)[rr] * (0.86 + 0.26 * K.mottle(x * 0.5, y * 0.8, z * 0.5, t * 1.3))
                            if t == 0:
                                k *= K.wet(y) if y < 0.6 else 1.0
                            cols.append(geo.scale(color, k * WALL_TONE))
                        uvs = [(s[i] / K.ROCK_TILE, quad[0][1] / K.ROCK_TILE), (s[i + 1] / K.ROCK_TILE, quad[1][1] / K.ROCK_TILE),
                               (s[i + 1] / K.ROCK_TILE, quad[2][1] / K.ROCK_TILE), (s[i] / K.ROCK_TILE, quad[3][1] / K.ROCK_TILE)]
                        with geo.detail(m, 0.6):
                            geo.oriented(m, quad, inward, cols, K.ROCK, uvs)
                # The bench on top of this tier, out to the next tier's face (or the rim).
                outer, _ = self.outlines[t + 1]
                bench = geo.scale(BENCHES[t], BENCH_TONE)
                for i in range(n):
                    if t in (0, 1) and in_niche(pts, tags, i):
                        continue
                    j2 = (i + 1) % n
                    quad = [(pts[i][0], y1, pts[i][1]), (pts[j2][0], y1, pts[j2][1]),
                            (outer[j2][0], y1, outer[j2][1]), (outer[i][0], y1, outer[i][1])]
                    cols = [geo.scale(bench, (0.9 if k < 2 else 1.0) * (0.88 + 0.22 * K.mottle(x * 0.4, t, z * 0.4, 9.0 + t)))
                            for k, (x, y, z) in enumerate(quad)]
                    with geo.detail(m, 0.9):
                        geo.oriented(m, quad, (0, 1, 0), cols, K.LIT)
                    # Scree against the next face's foot.
                    if t < len(TIERS) - 1 and self.rng.random() < 0.3:
                        self.heap(m, outer[i], outer[j2], y1, bench)
        self.b.part("background", "Quarry Walls", "Quarry Walls", build)

    def heap(self, m, a, b, y, color):
        """A scree cone leaning on a face: base along a..b on the bench, apex sunk into the face."""
        t0, t1 = sorted((self.rng.uniform(0.0, 0.6), self.rng.uniform(0.4, 1.0)))
        if t1 - t0 < 0.25:
            return
        p0 = (a[0] + (b[0] - a[0]) * t0, a[1] + (b[1] - a[1]) * t0)
        p1 = (a[0] + (b[0] - a[0]) * t1, a[1] + (b[1] - a[1]) * t1)
        nx, nz = -(a[2] + b[2]) / 2, -(a[3] + b[3]) / 2   # into the pit
        mid = ((p0[0] + p1[0]) / 2, (p0[1] + p1[1]) / 2)
        width = math.hypot(p1[0] - p0[0], p1[1] - p0[1])
        h, depth = min(1.1, 0.28 * width + 0.3), min(2.6, 0.6 * width + 0.7)
        apex = (mid[0] - nx * 0.4, y + h, mid[1] - nz * 0.4)
        toe = (mid[0] + nx * depth, y, mid[1] + nz * depth)
        c = geo.scale(color, 0.92)
        with geo.detail(m, 1.0):
            geo.oriented(m, [(p0[0], y, p0[1]), toe, apex], (nx, 0.6, nz), [c, c, geo.scale(c, 1.08)], K.LIT)
            geo.oriented(m, [toe, (p1[0], y, p1[1]), apex], (nx, 0.6, nz), [c, c, geo.scale(c, 1.08)], K.LIT)

    def niche(self):
        """The cut ends of the first two tiers either side of Mount Manki."""
        C = self.C
        z0, z1, z2 = C["maxZ"] + TIERS[0][0], C["maxZ"] + TIERS[1][0], C["maxZ"] + TIERS[2][0]

        def build(m):
            for x, facing in ((NICHE[0], 1.0), (NICHE[1], -1.0)):
                for (za, zb, top) in ((z0, z1, SHORE), (z1, z2, TIERS[1][2])):
                    quad = [(x, -1.0, za), (x, -1.0, zb), (x, top, zb), (x, top, za)]
                    cols = [geo.scale(P.OCHRE_DARK, K.wet(y) * 0.95) for _, y, _ in quad]
                    uvs = [(z / K.ROCK_TILE, y / K.ROCK_TILE) for _, y, z in quad]
                    with geo.detail(m, 0.6):
                        geo.oriented(m, quad, (facing, 0.0, 0.0), cols, K.ROCK, uvs)
        self.b.part("background", "Quarry Walls", "Niche", build)

    # ------------------------------------------------------------ Mount Manki
    def head(self):
        hx, hy = HEAD
        zf, zb = HEAD_FRONT, HEAD_BACK
        front = K.Plane.facing((hx, 0.0, zf), (0.0, -1.0))   # u runs east, v is world y

        def at(u, v, off=0.0):
            return front.at(u - hx, v, off)

        # The silhouette: spiky fur round the top and sides, a smooth chin in the water.
        rim = []
        n = 48
        for k in range(n):
            a = 2 * math.pi * k / n
            s = math.sin(a)
            if s > -0.3:
                r = 9.0 + (2.5 * (0.7 + 0.3 * math.sin(3 * a + 0.4)) if k % 2 == 0 else 0.2)
            else:
                r = 8.7
            rim.append((hx + r * math.cos(a), hy + r * math.sin(a)))

        def build(m):
            centre = at(hx, hy)
            with geo.detail(m, 0.35):
                for k in range(n):
                    a, b = rim[k], rim[(k + 1) % n]
                    geo.oriented(m, [centre, at(*a), at(*b)], front.n,
                                 [geo.scale(P.MANKI_ORANGE, 1.05), P.MANKI_ORANGE, P.MANKI_ORANGE], K.LIT)
            with geo.detail(m, 0.6):
                u = 0.0
                for k in range(n):
                    a, b = rim[k], rim[(k + 1) % n]
                    quad = [(a[0], a[1], zf), (b[0], b[1], zf), (b[0], b[1], zb), (a[0], a[1], zb)]
                    mid = ((a[0] + b[0]) / 2 - hx, (a[1] + b[1]) / 2 - hy)
                    L = math.hypot(*mid) or 1.0
                    seg = math.hypot(b[0] - a[0], b[1] - a[1])
                    uvs = [(u / K.ROCK_TILE, zf / K.ROCK_TILE), ((u + seg) / K.ROCK_TILE, zf / K.ROCK_TILE),
                           ((u + seg) / K.ROCK_TILE, zb / K.ROCK_TILE), (u / K.ROCK_TILE, zb / K.ROCK_TILE)]
                    u += seg
                    geo.oriented(m, quad, (mid[0] / L, mid[1] / L, 0.0), geo.scale(P.MANKI_ORANGE, 0.8), K.ROCK, uvs)
            with geo.detail(m, 0.2):
                # The pale muzzle patch, the red mask and its fresh drips.
                ring = [(hx + 6.9 * math.cos(2 * math.pi * k / 28), hy - 1.9 + 5.4 * math.sin(2 * math.pi * k / 28)) for k in range(28)]
                c0 = at(hx, hy - 1.9, 0.03)
                for k in range(28):
                    geo.oriented(m, [c0, at(*ring[k], 0.03), at(*ring[(k + 1) % 28], 0.03)], front.n, P.FUR_LIGHT, K.LIT)
                mask = [(-9.0, 2.2), (-6.6, 4.2), (-2.2, 4.3), (0.0, 3.5), (2.2, 4.3), (6.6, 4.2), (9.0, 2.2),
                        (6.3, 0.7), (2.4, 0.9), (0.0, 1.7), (-2.4, 0.9), (-6.3, 0.7)]
                c1 = at(hx, hy + 2.5, 0.06)
                for k in range(len(mask)):
                    a, b = mask[k], mask[(k + 1) % len(mask)]
                    geo.oriented(m, [c1, at(hx + a[0], hy + a[1], 0.06), at(hx + b[0], hy + b[1], 0.06)], front.n, P.MASK_RED, K.LIT)
                for dx, length in ((-6.9, 1.6), (-4.4, 2.6), (3.6, 1.2), (5.9, 2.2), (7.6, 0.9)):
                    K.panel(m, front, dx - 0.16, hy + 1.3 - length, dx + 0.16, hy + 1.3, P.MASK_RED, off=0.065)
                # One googly eye rolled up and out, one wink (the wink cell's red is the mask's).
                K.googly(m, front, -4.1, hy + 2.5, 1.75, look=(0.55, 0.5), off=0.09)
                K.plate(m, front, "wink", 4.1, hy + 2.6, 3.2, off=0.09)
                self.mouth(m, front, hy)
            with geo.detail(m, 0.3):
                geo.ellipsoid(m, (hx, hy - 0.4, zf), (1.6, 1.05, 1.15), P.INK, K.LIT, rings=6, segments=12)
                # Horns, curling up and out of the fur.
                for side in (-1, 1):
                    pts = [(hx + side * 4.8, hy + 7.6, zf + 2.5), (hx + side * 6.2, hy + 10.2, zf + 2.9),
                           (hx + side * 8.6, hy + 12.3, zf + 3.3), (hx + side * 11.4, hy + 12.9, zf + 3.6)]
                    radii = [1.35, 0.95, 0.55, 0.12]
                    for k in range(3):
                        geo.cylinder_between(m, pts[k], pts[k + 1], radii[k], 10, P.HORN, K.LIT, radius_b=radii[k + 1])
                    # Ears out of the sides.
                    geo.ellipsoid(m, (hx + side * 10.4, hy + 2.6, zf + 2.6), (2.5, 3.1, 1.3), geo.scale(P.MANKI_ORANGE, 0.95), K.LIT,
                                  rings=6, segments=12)
                    geo.ellipsoid(m, (hx + side * 10.6, hy + 2.6, zf + 1.45), (1.5, 2.0, 0.35), P.SKIN, K.LIT, rings=5, segments=10)
                # The giant roller he did it with, and his bucket.
                roller = (hx + 11.6, hy + 5.4, zf + 1.1)
                geo.cylinder_between(m, (roller[0] - 2.0, roller[1], roller[2]), (roller[0] + 2.0, roller[1], roller[2]), 0.75, 12,
                                     P.MANKI_ORANGE, K.LIT)
                geo.cylinder_between(m, (roller[0] + 2.05, roller[1], roller[2]), (roller[0] + 2.6, roller[1] - 0.4, roller[2] - 0.8),
                                     0.12, 6, P.STEEL_DARK, K.LIT)
                geo.cylinder_between(m, (roller[0] + 2.6, roller[1] - 0.4, roller[2] - 0.8), (hx + 13.2, -0.6, HEAD_FRONT - 3.0),
                                     0.22, 8, P.WOOD, K.LIT)
                bx, bz = hx + 16.8, self.C["maxZ"] + 4.2
                geo.lathe(m, (bx, SHORE, bz), [(0.85, 0.0), (1.0, 1.3), (0.9, 1.3), (0.0, 1.22)], 14,
                          [P.STEEL, P.STEEL, P.MANKI_ORANGE], K.LIT, cap_top=False)
                K.panel(m, K.Plane.facing((bx, SHORE, bz - 0.95), (0.0, -1.0)), -0.12, 0.4, 0.12, 1.25, P.MANKI_ORANGE, off=0.04)
        self.b.part("background", "Mount Manki", "Mount Manki", build)

    def mouth(self, m, front, hy):
        """The grin: a wide dark smile with a row of teeth and the tongue hanging out."""
        half = 5.6

        def top(u):
            return hy - 2.7 + 0.055 * u * u

        def bottom(u):
            return hy - 2.7 - 2.9 * (1.0 - (u / half) ** 2) - 0.05
        n = 16
        us = [-half + 2 * half * k / n for k in range(n + 1)]
        for a, b in zip(us, us[1:]):
            quad = [front.at(a, bottom(a), 0.05), front.at(b, bottom(b), 0.05), front.at(b, top(b), 0.05), front.at(a, top(a), 0.05)]
            geo.oriented(m, quad, front.n, P.MASK_DARK, K.LIT)
        teeth = 9
        for k in range(teeth):
            a = -half * 0.86 + 2 * half * 0.86 * k / teeth + 0.07
            b = -half * 0.86 + 2 * half * 0.86 * (k + 1) / teeth - 0.07
            length = 1.25 if k in (1, teeth - 2) else 0.95
            mid = (a + b) / 2
            quad = [front.at(a, top(a) - length, 0.06), front.at(b, top(b) - length, 0.06), front.at(b, top(b) + 0.02, 0.06),
                    front.at(a, top(a) + 0.02, 0.06)]
            geo.oriented(m, quad, front.n, P.TOOTH, K.LIT)
            K.disc(m, front, mid, top(mid) - length, (b - a) / 2, P.TOOTH, off=0.06, n=8)
        # The tongue, out and a little to one side.
        K.disc(m, front, 1.5, hy - 5.4, 1.45, P.TONGUE, off=0.07, n=18, ry=1.9)
        K.panel(m, front, 1.43, hy - 6.6, 1.57, hy - 4.6, geo.scale(P.TONGUE, 0.7), off=0.075)

    # ------------------------------------------------------------ the yard on the shore bench
    def marquee(self, m, plane, u0, v0, u1, v1, spacing=0.7, off=0.1):
        """Chasing bulbs round a sign (StageNeon chase: one bulb in three lit)."""
        per = 2 * ((u1 - u0) + (v1 - v0))
        n = max(8, int(per / spacing))
        for k in range(n):
            d = per * k / n
            if d < u1 - u0:
                u, v = u0 + d, v0
            elif d < (u1 - u0) + (v1 - v0):
                u, v = u1, v0 + d - (u1 - u0)
            elif d < 2 * (u1 - u0) + (v1 - v0):
                u, v = u1 - (d - (u1 - u0) - (v1 - v0)), v1
            else:
                u, v = u0, v1 - (d - 2 * (u1 - u0) - (v1 - v0))
            with K.glow(m, P.SPARK, (101.4, 0.0), (k % 3) / 3.0):
                K.glow_ellipsoid(m, plane.at(u, v, off), (0.11, 0.11, 0.11), P.SPARK, rings=3, segments=6)

    def big_sign(self, m, side, along, g, cell, width, clearance=0.9, bulbs=True):
        x, z, yaw = spot(self.C, side, along, g)
        height = K.sign_on_posts(m, x, z, SHORE, yaw, cell, width, clearance=clearance)
        nx, nz = -math.sin(yaw), -math.cos(yaw)
        plane = K.Plane.facing((x, SHORE, z), (nx, nz))
        frame = 0.16
        for (u0, v0, u1, v1) in ((-width / 2 - frame, clearance - frame, width / 2 + frame, clearance),
                                 (-width / 2 - frame, clearance + height, width / 2 + frame, clearance + height + frame),
                                 (-width / 2 - frame, clearance, -width / 2, clearance + height),
                                 (width / 2, clearance, width / 2 + frame, clearance + height)):
            K.slab(m, plane, u0, v0, u1, v1, 0.06, P.INK)
        if bulbs:
            self.marquee(m, plane, -width / 2 - frame / 2, clearance - frame / 2, width / 2 + frame / 2,
                         clearance + height + frame / 2)

    def excavator(self, m, x, z, yaw):
        """Manki's excavator, bucket up, holding one stick of dynamite like a trophy."""
        c, s = math.cos(yaw), math.sin(yaw)

        def W(px, py, pz):  # local: +z forward (towards the pit), +x right
            return (x + px * c + pz * s, SHORE + py, z - px * s + pz * c)
        for side in (-1.15, 1.15):
            geo.box(m, W(side, 0.45, 0.0), (0.75, 0.9, 3.7), P.TIRE, K.LIT, yaw=yaw, skip=("bottom",))
            for k in range(4):
                geo.box(m, W(side, 0.92, -1.35 + 0.9 * k), (0.8, 0.06, 0.25), P.STEEL_DARK, K.LIT, yaw=yaw, skip=("bottom",))
        geo.box(m, W(0.0, 1.55, -0.2), (2.7, 1.2, 2.6), P.MANKI_ORANGE, K.LIT, yaw=yaw, skip=("bottom",))
        geo.box(m, W(0.0, 1.55, -1.7), (2.5, 1.0, 0.6), P.STEEL_DARK, K.LIT, yaw=yaw, skip=("bottom",))
        geo.box(m, W(-0.65, 2.9, 0.35), (1.15, 1.5, 1.25), P.MANKI_ORANGE, K.LIT, yaw=yaw, skip=("bottom",))
        cab = K.Plane.facing(W(-0.65, 2.15, 0.98), (s, c))
        K.panel(m, cab, -0.48, 0.2, 0.48, 1.25, (0.14, 0.2, 0.26), off=0.01)
        K.googly(m, cab, -0.2, 0.78, 0.19, look=(-0.5, 0.4), off=0.02)
        K.googly(m, cab, 0.22, 0.82, 0.16, look=(0.6, -0.3), off=0.02)
        side_p = K.Plane.facing(W(1.36, 1.55, -0.2), (c, -s))
        K.sticker(m, side_p, "tag", 0.0, 0.0, 0.95, off=0.01)
        shoulder, elbow, wrist = W(0.55, 2.2, 0.9), W(0.55, 5.6, 2.6), W(0.55, 4.6, 4.6)
        geo.cylinder_between(m, shoulder, elbow, 0.26, 6, P.MANKI_ORANGE, K.LIT)
        geo.cylinder_between(m, elbow, wrist, 0.2, 6, P.MANKI_ORANGE, K.LIT)
        geo.cylinder_between(m, W(0.55, 2.0, 0.6), W(0.55, 4.6, 2.0), 0.1, 6, P.STEEL, K.LIT)
        geo.box(m, (wrist[0], wrist[1] - 0.35, wrist[2]), (0.9, 0.7, 0.8), P.STEEL_DARK, K.LIT, yaw=yaw)
        K.dynamite(m, (wrist[0], wrist[1] + 0.0, wrist[2]), (wrist[0] + 0.15, wrist[1] + 1.3, wrist[2] + 0.2), radius=0.12)
        K.spark(m, (wrist[0] + 0.16, wrist[1] + 1.42, wrist[2] + 0.22), size=0.3, phase=0.12)

    def water_tower(self, m, x, z):
        """A water tower painted as a stick of TNT, fuse lit. Nobody knows what is inside."""
        legs = 3.2
        r = 2.1
        for k in range(4):
            a = math.pi / 4 + k * math.pi / 2
            foot = (x + math.cos(a) * 1.9, SHORE, z + math.sin(a) * 1.9)
            head = (x + math.cos(a) * 1.4, SHORE + legs, z + math.sin(a) * 1.4)
            K.beam(m, foot, head, 0.22, P.STEEL_DARK)
            b = k + 1
            a2 = math.pi / 4 + b * math.pi / 2
            K.beam(m, foot, (x + math.cos(a2) * 1.4, SHORE + legs, z + math.sin(a2) * 1.4), 0.08, P.STEEL_DARK)
        y0, y1 = SHORE + legs, SHORE + legs + 3.0
        geo.lathe(m, (x, y0, z), [(0.0, -0.1), (r, 0.0)], 16, P.DYNAMITE_DARK, K.LIT, cap_top=False)
        (a0, b0), (a1, b1) = T.uv("water_tower")
        seg = 16
        for k in range(seg):
            t0, t1 = k / seg, (k + 1) / seg
            p = [(x + r * math.cos(2 * math.pi * t), y, z + r * math.sin(2 * math.pi * t)) for t, y in ((t0, y0), (t1, y0), (t1, y1), (t0, y1))]
            uvs = [(a0 + (a1 - a0) * (1 - t0), b0), (a0 + (a1 - a0) * (1 - t1), b0), (a0 + (a1 - a0) * (1 - t1), b1),
                   (a0 + (a1 - a0) * (1 - t0), b1)]
            ang = 2 * math.pi * (t0 + t1) / 2
            geo.oriented(m, p, (math.cos(ang), 0.0, math.sin(ang)), (1.0, 1.0, 1.0), K.LIT, uvs)
        geo.lathe(m, (x, y1, z), [(r, 0.0), (r * 0.9, 0.25), (0.0, 0.4)], 16, P.DYNAMITE_DARK, K.LIT, cap_top=False)
        fuse = [(x, y1 + 0.35, z), (x + 0.2, y1 + 1.0, z + 0.1), (x + 0.7, y1 + 1.5, z - 0.1), (x + 1.1, y1 + 1.3, z - 0.3)]
        geo.tube(m, fuse, 0.12, 6, (0.32, 0.26, 0.2), K.LIT)
        K.spark(m, (x + 1.15, y1 + 1.25, z - 0.32), size=0.55, phase=0.66)

    def drums(self, m, x, z, count):
        for k in range(count):
            a = self.rng.uniform(0, 2 * math.pi)
            d = 0.0 if k == 0 else self.rng.uniform(0.75, 1.0)
            px, pz = x + math.cos(a) * d, z + math.sin(a) * d
            color = geo.jitter(self.rng.choice(P.DRUM_COLORS), self.rng, 0.06)
            if self.rng.random() < 0.3:
                K.lying_drum(m, (px, SHORE + 0.42, pz), 0.42, 1.0, self.rng.uniform(0, math.pi), color)
            else:
                K.drum(m, (px, SHORE, pz), color=color)

    def boulder(self, m, x, z, r, color):
        geo.ellipsoid(m, (x, SHORE + r * 0.35, z), (r, r * 0.7, r * 0.9), color, K.LIT, rings=3, segments=6)

    def yard(self):
        C = self.C

        def build(m):
            with geo.detail(m, 0.25):
                self.big_sign(m, "w", -33.5, 3.2, "quarry_sign", 13.0, clearance=0.45)
                self.big_sign(m, "s", -5.0, 3.6, "billboard", 11.0, clearance=0.7)
                self.big_sign(m, "s", 21.0, 2.8, "days", 5.6, clearance=0.6, bulbs=False)
                self.big_sign(m, "w", 6.0, 2.8, "safety", 5.2, clearance=0.6, bulbs=False)
                self.big_sign(m, "e", 21.0, 2.8, "radius", 5.2, clearance=0.6, bulbs=False)
                self.big_sign(m, "n", -34.0, 2.8, "wasnt", 6.0, clearance=0.5, bulbs=False)
                for side, along in (("n", -48.0), ("e", -26.0), ("w", -48.0), ("s", 33.0), ("n", 34.0)):
                    x, z, yaw = spot(C, side, along, 2.5)
                    K.sign_on_posts(m, x, z, SHORE, yaw + 0.5, "monkey", 1.9, clearance=0.9)
            with geo.detail(m, 0.35):
                x, z, _ = spot(C, "e", 4.0, 4.2)
                self.excavator(m, x, z, -0.26)   # along the wall, bucket held up towards the pit
                x, z, _ = spot(C, "e", -47.0, 4.0)
                self.water_tower(m, x, z)
                for side, along, count in (("n", -42.0, 4), ("w", -16.0, 5), ("s", -40.0, 3), ("e", -14.0, 4), ("n", 32.0, 3),
                                           ("s", 9.0, 2), ("w", 28.0, 3), ("e", 36.0, 3)):
                    x, z, _ = spot(C, side, along, self.rng.uniform(3.2, 4.8))
                    self.drums(m, x, z, count)
                for side, along in (("s", 3.0), ("w", 18.0), ("e", -36.0), ("n", -20.0)):
                    x, z, _ = spot(C, side, along, 4.0)
                    for k in range(self.rng.randint(2, 4)):
                        K.tyre(m, (x + self.rng.uniform(-0.1, 0.1), SHORE + 0.14 + 0.28 * k, z + self.rng.uniform(-0.1, 0.1)), 0.5, 0.28)
                for side, along in (("s", 14.0), ("w", -24.0), ("n", -8.0), ("e", 12.0)):
                    for k in range(4):
                        x, z, yaw = spot(C, side, along + 1.6 * k, 2.1)
                        K.cone(m, (x, SHORE, z), yaw=self.rng.uniform(0, 3))
                for k in range(70):
                    side = self.rng.choice("nswe")
                    span = (C["minX"] + 4, C["maxX"] - 4) if side in "ns" else (C["minZ"] + 4, C["maxZ"] - 4)
                    along = self.rng.uniform(*span)
                    if side == "n" and NICHE[0] - 1.5 < along < NICHE[1] + 1.5:
                        continue
                    x, z, _ = spot(C, side, along, self.rng.uniform(2.2, 6.6))
                    self.boulder(m, x, z, self.rng.uniform(0.25, 0.8), geo.jitter(P.OCHRE_DARK, self.rng, 0.12))
            # Beacons along the shore bench's lip, blinking out of step.
            for k, (side, along) in enumerate([("s", -50.0), ("s", -26.0), ("s", 12.0), ("s", 36.0), ("w", -52.0), ("w", -14.0),
                                               ("w", 20.0), ("w", 40.0), ("n", -54.0), ("n", -24.0), ("n", 31.0), ("e", -50.0),
                                               ("e", -20.0), ("e", 10.0), ("e", 40.0)]):
                x, z, _ = spot(C, side, along, 2.0)
                geo.box(m, (x, SHORE + 0.6, z), (0.14, 1.2, 0.14), P.STEEL_DARK, K.LIT, skip=("bottom",))
                K.beacon(m, (x, SHORE + 1.4, z), radius=0.2, phase=(k * 0.37) % 1.0)
        self.b.part("background", "Quarry Yard", "Quarry Yard", build)

        # A fresh blast on the north bench: scorch, rubble and smoke still rising.
        def blast(m):
            x, z, _ = spot(C, "n", -44.0, 4.2)
            pts = []
            for k in range(18):
                a = math.pi * k / 9
                r = 3.0 if k % 2 == 0 else 1.9
                pts.append((x + math.cos(a) * r, SHORE + 0.02, z + math.sin(a) * r * 0.7))
            for k in range(len(pts)):
                geo.oriented(m, [(x, SHORE + 0.02, z), pts[k], pts[(k + 1) % len(pts)]], (0, 1, 0),
                             [(0.12, 0.1, 0.09), (0.3, 0.24, 0.18), (0.3, 0.24, 0.18)], K.LIT)
            for k in range(14):
                a = self.rng.uniform(0, 2 * math.pi)
                d = self.rng.uniform(1.0, 3.4)
                self.boulder(m, x + math.cos(a) * d, z + math.sin(a) * d * 0.6, self.rng.uniform(0.2, 0.55), geo.jitter(P.ROCK_DARK, self.rng, 0.1))
            K.puff(m, (x, SHORE + 1.6, z), 1.6, P.SMOKE_DARK, seed=5, lumps=6)
            K.puff(m, (x + 0.8, SHORE + 3.8, z + 0.6), 2.0, P.SMOKE, seed=9, lumps=6)
            K.puff(m, (x + 2.0, SHORE + 6.6, z + 1.2), 2.3, P.SMOKE, seed=13, lumps=5)
        self.b.part("background", "Smoke", "Fresh Blast", blast)

    def smoke(self):
        """Blast smoke over the rim: only the far views and the thumbnail see it."""
        C = self.C

        def build(m):
            for k, (side, along) in enumerate((("n", -40.0), ("w", -20.0), ("e", 18.0), ("s", -30.0), ("n", 40.0))):
                x, z, _ = spot(C, side, along, 24.0 + 3.0 * (k % 2))
                for j in range(3):
                    K.puff(m, (x + j * 2.0, TIERS[-1][2] + 2.0 + j * 4.5, z + j * 1.2), 4.0 + j * 1.2, P.SMOKE, seed=k * 7 + j, lumps=6)
        self.b.part("background", "Smoke", "Rim Smoke", build)
