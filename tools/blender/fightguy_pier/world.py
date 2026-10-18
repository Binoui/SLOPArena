"""The world round the pier at dusk: the sea (foam lapping every pile and piece), the sky dome
with the sun setting in the west, long pink clouds, the seafront town behind the promenade
(lit windows, HOTEL SLOP and ARCADE neon, promenade lamps), headlands with a lighthouse and
the lights of other piers.

The sea is the declared visual exception inside the corridor (flat at WATER_Y, just above the
KO plane). Everything else is background and stays outside the blast corridor; build.py
clips every background face against it.
"""
from __future__ import annotations

import math

from stagekit import geo
import palette as P
import parts as K
import textures as T

WATER_Y = -0.05
SKY_R = 900.0
SUN_AZ = 262.0      # compass degrees: low in the west, behind the old pier
SUN_EL = 3.5
STEP = 1.6


def rect_distance(x, z, box):
    x0, z0, x1, z1 = box[:4]
    return math.hypot(max(x0 - x, 0.0, x - x1), max(z0 - z, 0.0, z - z1))


class World:
    def __init__(self, b):
        self.b = b
        self.C = b.C
        self.rng = b.rng
        self.boxes = list(b.pieces.values())

    def build(self):
        self.sea()
        self.b.part("background", "Sky", "Sky", self.sky)
        self.b.part("background", "Clouds", "Clouds", self.clouds)
        self.b.part("background", "Town", "Town", self.town)
        self.b.part("background", "Town Lights", "Town Lights", self.town_lights)
        self.b.part("background", "Headlands", "Headlands", self.headlands)

    # ------------------------------------------------------------ the sea
    def sea(self):
        C = self.C
        x0, x1 = C["minX"] - 40.0, C["maxX"] + 40.0
        z0, z1 = C["minZ"] - 6.0, C["maxZ"] + 40.0
        sun = math.radians(SUN_AZ)
        sx, sz = math.sin(sun), math.cos(sun)

        def color_at(x, z):
            d = min(rect_distance(x, z, p) for p in self.boxes)
            near = max(0.0, 1.0 - d / 6.0)
            c = geo.mix(P.SEA_DEEP, P.SEA_SHALLOW, near ** 1.6)
            # the sun's path on the water, west of the pier
            along = (x * sx + z * sz)
            across = abs(-x * sz + z * sx)
            path = max(0.0, min(1.0, (along + 20.0) / 80.0)) * math.exp(-(across / 18.0) ** 2)
            c = geo.mix(c, P.SEA_GLOW, 0.55 * path)
            foam = max(0.0, 1.0 - d / 0.9) * 0.55
            return geo.rgba(c, round(foam, 3))

        def build(m):
            nx = max(1, int(round((x1 - x0) / STEP)))
            nz = max(1, int(round((z1 - z0) / STEP)))
            cache = {}

            def col(i, j):
                if (i, j) not in cache:
                    cache[(i, j)] = color_at(x0 + (x1 - x0) * i / nx, z0 + (z1 - z0) * j / nz)
                return cache[(i, j)]
            for i in range(nx):
                for j in range(nz):
                    xa, xb = x0 + (x1 - x0) * i / nx, x0 + (x1 - x0) * (i + 1) / nx
                    za, zb = z0 + (z1 - z0) * j / nz, z0 + (z1 - z0) * (j + 1) / nz
                    geo.oriented(m, [(xa, WATER_Y, za), (xb, WATER_Y, za), (xb, WATER_Y, zb), (xa, WATER_Y, zb)], (0, 1, 0),
                                 [col(i, j), col(i + 1, j), col(i + 1, j + 1), col(i, j + 1)], K.WATER)
            # The open sea beyond, in coarse rings out to the horizon (outside the course, no foam).
            R0, R1 = 160.0, SKY_R * 0.92
            seg = 48
            cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
            inner = [(x0, z0), (x1, z0), (x1, z1), (x0, z1)]
            rings = [R0, 260.0, 420.0, R1]
            for k in range(4):
                a0 = math.atan2(inner[k][1] - cz, inner[k][0] - cx)
                a1 = math.atan2(inner[(k + 1) % 4][1] - cz, inner[(k + 1) % 4][0] - cx)
                if a1 < a0:
                    a1 += 2 * math.pi
                steps = 12
                for s in range(steps):
                    t0, t1 = s / steps, (s + 1) / steps
                    pa = (inner[k][0] + (inner[(k + 1) % 4][0] - inner[k][0]) * t0, inner[k][1] + (inner[(k + 1) % 4][1] - inner[k][1]) * t0)
                    pb = (inner[k][0] + (inner[(k + 1) % 4][0] - inner[k][0]) * t1, inner[k][1] + (inner[(k + 1) % 4][1] - inner[k][1]) * t1)
                    prev = (pa, pb)
                    for R in rings:
                        qa = (cx + (pa[0] - cx) / max(1e-6, math.hypot(pa[0] - cx, pa[1] - cz)) * R,
                              cz + (pa[1] - cz) / max(1e-6, math.hypot(pa[0] - cx, pa[1] - cz)) * R)
                        qb = (cx + (pb[0] - cx) / max(1e-6, math.hypot(pb[0] - cx, pb[1] - cz)) * R,
                              cz + (pb[1] - cz) / max(1e-6, math.hypot(pb[0] - cx, pb[1] - cz)) * R)
                        cols = [self.open_sea(prev[0]), self.open_sea(prev[1]), self.open_sea(qb), self.open_sea(qa)]
                        geo.oriented(m, [(prev[0][0], WATER_Y, prev[0][1]), (prev[1][0], WATER_Y, prev[1][1]),
                                         (qb[0], WATER_Y, qb[1]), (qa[0], WATER_Y, qa[1])], (0, 1, 0), cols, K.WATER)
                        prev = (qa, qb)
        self.b.part("water", "Sea", "Sea", build)

    def open_sea(self, p):
        x, z = p
        sun = math.radians(SUN_AZ)
        along = x * math.sin(sun) + z * math.cos(sun)
        across = abs(-x * math.cos(sun) + z * math.sin(sun))
        path = max(0.0, min(1.0, (along + 20.0) / 200.0)) * math.exp(-(across / max(20.0, along * 0.18 + 20.0)) ** 2)
        c = geo.mix(P.SEA_DEEP, P.SEA_MID, min(1.0, math.hypot(x, z) / 600.0))
        return geo.rgba(geo.mix(c, P.SEA_GLOW, 0.6 * path), 0.0)

    # ------------------------------------------------------------ the sky
    SKY_RINGS = [(-10, P.SKY_HORIZON), (0, P.SKY_HORIZON), (2.5, P.SKY_LOW), (7, P.SKY_LOW), (14, P.SKY_MID),
                 (26, P.SKY_HIGH), (45, P.SKY_ZENITH), (90, P.SKY_ZENITH)]

    def sky_color(self, el, az):
        rings = self.SKY_RINGS
        base = rings[-1][1]
        for (e0, c0), (e1, c1) in zip(rings, rings[1:]):
            if e0 <= el <= e1:
                base = geo.mix(c0, c1, (el - e0) / (e1 - e0))
                break
        sun = math.radians(SUN_AZ)
        toward = max(0.0, math.cos(az - sun))
        glow = toward ** 3 * math.exp(-max(0.0, el) / 12.0)
        away = max(0.0, -math.cos(az - sun)) * math.exp(-max(0.0, el) / 18.0)
        c = geo.mix(base, P.SKY_EAST_LOW, 0.6 * away)
        return geo.mix(c, P.SUN_HALO, 0.75 * glow)

    def sky(self, m):
        rings = self.SKY_RINGS
        seg = 64
        verts = []
        for el, _ in rings:
            row = []
            e = math.radians(el)
            for s in range(seg):
                az = 2 * math.pi * s / seg
                p = (SKY_R * math.cos(e) * math.sin(az), SKY_R * math.sin(e) - 20.0, SKY_R * math.cos(e) * math.cos(az))
                row.append((p, self.sky_color(el, az)))
            verts.append(row)
        with geo.state(m, alpha=0.0):
            for i in range(len(rings) - 1):
                for s in range(seg):
                    s2 = (s + 1) % seg
                    q = [verts[i][s], verts[i][s2], verts[i + 1][s2], verts[i + 1][s]]
                    pts = [p for p, _ in q]
                    c = [sum(p[k] for p in pts) / 4 for k in range(3)]
                    geo.oriented(m, pts, (-c[0], -c[1] - 20.0, -c[2]), [c_ for _, c_ in q], K.SKY)
        # The setting sun: a soft halo, then the disc sinking into the horizon.
        az, el = math.radians(SUN_AZ), math.radians(SUN_EL)
        for r_scale, radius, color, alpha in ((0.9, 140.0, P.SUN_HALO, 0.0), (0.88, 30.0, P.SUN, 1.0)):
            r = SKY_R * r_scale
            c = (r * math.cos(el) * math.sin(az), r * math.sin(el) - 20.0, r * math.cos(el) * math.cos(az))
            n = geo._norm((-c[0], -c[1] - 20.0, -c[2]))
            u = geo._norm(geo._cross(n, (0, 1, 0)))
            v = geo._cross(u, n)
            ring = [tuple(c[i] + (u[i] * math.cos(a) + v[i] * math.sin(a)) * radius for i in range(3))
                    for a in [k * math.pi / 18 for k in range(36)]]
            with geo.state(m, alpha=alpha):
                for k in range(36):
                    rim = self.sky_color(math.degrees(math.asin(max(-1, min(1, (ring[k][1] + 20.0) / r)))),
                                         math.atan2(ring[k][0], ring[k][2])) if alpha == 0.0 else color
                    rim2 = self.sky_color(math.degrees(math.asin(max(-1, min(1, (ring[(k + 1) % 36][1] + 20.0) / r)))),
                                          math.atan2(ring[(k + 1) % 36][0], ring[(k + 1) % 36][2])) if alpha == 0.0 else color
                    geo.oriented(m, [c, ring[k], ring[(k + 1) % 36]], n, [color, rim, rim2], K.SKY)

    def clouds(self, m):
        rng = self.rng
        with geo.state(m, alpha=0.0):
            for k in range(14):
                az = math.radians(SUN_AZ + rng.uniform(-110, 110))
                el = math.radians(rng.uniform(5, 18))
                r = SKY_R * 0.8
                cx = r * math.cos(el) * math.sin(az)
                cy = r * math.sin(el) - 20.0
                cz = r * math.cos(el) * math.cos(az)
                length = rng.uniform(90, 220)
                thick = rng.uniform(6, 14)
                tx, tz = math.cos(az), -math.sin(az)
                toward = max(0.0, math.cos(az - math.radians(SUN_AZ)))
                c = geo.mix(P.CLOUD_DARK, P.CLOUD, 0.25 + 0.75 * toward)
                pts = [(cx - tx * length / 2, cy - thick / 2, cz - tz * length / 2), (cx + tx * length / 2, cy - thick / 3, cz + tz * length / 2),
                       (cx + tx * length * 0.4, cy + thick / 2, cz + tz * length * 0.4), (cx - tx * length * 0.45, cy + thick / 3, cz - tz * length * 0.45)]
                n = geo._norm((-cx, 0.0, -cz))
                geo.oriented(m, pts, n, [geo.scale(c, 0.8), c, geo.mix(c, P.SUN, 0.3 * toward), c], K.SKY)

    # ------------------------------------------------------------ the seafront town
    def town(self, m):
        """Buildings along the seafront south of the promenade, outside the corridor."""
        rng = self.rng
        z_front = self.C["minZ"] - 14.0
        x = -110.0
        with geo.detail(m, 0.2):
            # the seafront road and its seawall
            geo.box(m, (0.0, 1.0, z_front - 3.0), (240.0, 2.4, 10.0), (0.30, 0.26, 0.28), K.LIT, skip=("bottom",))
            while x < 110.0:
                w = rng.uniform(8.0, 16.0)
                h = rng.uniform(9.0, 24.0) if abs(x) > 18 else rng.uniform(14.0, 30.0)
                d = rng.uniform(10.0, 16.0)
                c = geo.jitter(P.TOWN_LIGHT if rng.random() < 0.5 else P.TOWN, rng, 0.12)
                cx, cz = x + w / 2, z_front - 8.0 - d / 2
                geo.box(m, (cx, 2.2 + h / 2, cz), (w - 0.6, h, d), c, K.LIT, skip=("bottom",))
                # a roof cap
                geo.box(m, (cx, 2.2 + h + 0.3, cz), (w - 0.2, 0.6, d + 0.4), geo.scale(c, 0.8), K.LIT, skip=("bottom",))
                x += w
            # hills behind the town
            for k in range(9):
                cx = -160 + k * 40 + rng.uniform(-10, 10)
                geo.ellipsoid(m, (cx, -6.0, z_front - 110.0 + rng.uniform(-20, 10)), (rng.uniform(45, 70), rng.uniform(22, 40), 40),
                              P.HILL, K.LIT, rings=5, segments=10)

    def town_lights(self, m):
        """Lit windows, HOTEL SLOP and ARCADE neon and the promenade lamps along the seafront."""
        rng = random_from(self.rng)
        z_front = self.C["minZ"] - 14.0
        front_z = z_front - 8.0 + 0.05
        plane = K.Plane.facing((0.0, 0.0, front_z), (0.0, 1.0))
        (a0, b0), (a1, b1) = T.uv("windows")
        with geo.state(m, alpha=1.0):
            x = -100.0
            while x < 100.0:
                w = rng.uniform(5.0, 9.0)
                h = rng.uniform(4.0, 12.0)
                # windows panel: the cell's lit panes glow (atlas alpha), the rest stays dark
                pts = [plane.at(-x, 3.0), plane.at(-(x + w), 3.0), plane.at(-(x + w), 3.0 + h), plane.at(-x, 3.0 + h)]
                geo.oriented(m, [(p[0], p[1], p[2]) for p in pts], (0.0, 0.0, 1.0), (1.0, 1.0, 1.0), K.GLOW,
                             [(a0, b0), (a1, b0), (a1, b0 + (b1 - b0) * min(1.0, h / 8.0)), (a0, b0 + (b1 - b0) * min(1.0, h / 8.0))])
                x += w + rng.uniform(4.0, 10.0)
            for cell, cx, y, w in (("hotel", -28.0, 20.0, 18.0), ("arcade", 24.0, 12.0, 14.0)):
                x_, y_, cw, ch = T.CELLS[cell]
                hgt = w * ch / cw
                K.panel(m, plane, -cx - w / 2, y, -cx + w / 2, y + hgt, (1.0, 1.0, 1.0), off=0.4, mat=K.GLOW, uvs=T.uv(cell))
        for x in range(-100, 101, 9):
            K.lamp_post(m, float(x), z_front + 1.5, 2.2, 4.0, P.GI_BLUE_DARK, P.BULB_WARM)

    def headlands(self, m):
        rng = self.rng
        with geo.detail(m, 0.1):
            for side, cx in ((-1, -420.0), (1, 380.0)):
                for k in range(6):
                    geo.ellipsoid(m, (cx + side * k * 40.0 + rng.uniform(-10, 10), -12.0, 160.0 + k * 70.0 + rng.uniform(-20, 20)),
                                  (rng.uniform(80, 130), rng.uniform(16, 32), rng.uniform(60, 90)), P.HEADLAND, K.LIT, rings=4, segments=12)
        # the lighthouse on the east headland, blinking
        x, z = 372.0, 230.0
        geo.lathe(m, (x, 10.0, z), [(4.0, 0.0), (3.2, 22.0), (3.4, 24.0)], 12, [P.WHITE, P.RED], K.LIT)
        K.beacon(m, (x, 36.0, z), 2.4, P.BULB, K.SLOW_BLINK, 0.0)
        # the lights of another pier far to the north-west
        for i in range(30):
            K.bulb(m, (-260.0 + i * 4.0, 4.0, 380.0), 0.9, P.BULB_WARM, (5.0, 0.0), i / 30)


def random_from(rng):
    import random
    return random.Random(rng.random())
