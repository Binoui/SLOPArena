"""The flooded pit: turquoise quarry water with lapping foam round every piece, the shore
and Mount Manki's chin, floating junk, and a rubber duck in an eyepatch bobbing right under
the dynamite.

The water is the declared visual exception inside the corridor (flat at WATER_Y, just above
the KO plane). Flotsam floats no higher than FLOTSAM_HIGH above it and keeps FLOTSAM_CLEAR
from every piece; the duck is the one taller exception (DUCK_HIGH, DUCK_CLEAR).
"""
from __future__ import annotations

import math

from stagekit import geo
import palette as P
import parts as K
import quarry as Q

WATER_Y = -0.05
STEP = 1.5
FLOTSAM_HIGH = 0.42
FLOTSAM_CLEAR = 3.0
DUCK = (-9.5, -22.0)
DUCK_HIGH = 1.6
DUCK_CLEAR = 4.0
DEEP = (0.06, 0.31, 0.36)
SHALLOW = (0.18, 0.56, 0.52)


def rect_distance(x, z, box):
    x0, z0, x1, z1 = box[:4]
    return math.hypot(max(x0 - x, 0.0, x - x1), max(z0 - z, 0.0, z - z1))


class Water:
    def __init__(self, b):
        self.b = b
        self.C = b.C
        self.rng = b.rng
        self.pieces = list(b.pieces.values())
        # Where the shore and Mount Manki's chin meet the water (for foam and shallows).
        hx, hy = Q.HEAD
        reach = math.sqrt(max(0.0, 8.7 ** 2 - (hy - WATER_Y) ** 2))
        self.chin = (hx - reach, Q.HEAD_FRONT - 0.2, hx + reach, Q.HEAD_FRONT + 0.2)

    def shore_distance(self, x, z):
        C = self.C
        g = max(C["minX"] - x, x - C["maxX"], C["minZ"] - z, z - C["maxZ"])
        if g < 0.0:
            g = -min(x - C["minX"], C["maxX"] - x, z - C["minZ"], C["maxZ"] - z)
        if Q.NICHE[0] < x < Q.NICHE[1] and z > C["maxZ"]:
            return rect_distance(x, z, self.chin)
        return abs(g - Q.TIERS[0][0])

    def build(self):
        self.surface()
        self.flotsam()
        self.duck()

    def surface(self):
        C = self.C
        x0, x1 = C["minX"] - 3.0, C["maxX"] + 3.0
        z0, z1 = C["minZ"] - 3.0, C["maxZ"] + 3.0
        rects = [(x0, z0, x1, z1), (Q.NICHE[0], z1, Q.NICHE[1], C["maxZ"] + Q.TIERS[2][0] + 0.5)]

        def color_at(x, z):
            d_piece = min(rect_distance(x, z, p) for p in self.pieces)
            d_shore = self.shore_distance(x, z)
            near = min(d_piece, d_shore)
            shallow = max(0.0, 1.0 - near / 7.0)
            c = geo.mix(DEEP, SHALLOW, shallow ** 1.5)
            foam = max(max(0.0, 1.0 - d_piece / 1.0) * 0.6, max(0.0, 1.0 - d_shore / 1.5) * 0.5)
            return geo.rgba(c, round(foam, 3))

        def build(m):
            for (a0, b0, a1, b1) in rects:
                nx = max(1, int(round((a1 - a0) / STEP)))
                nz = max(1, int(round((b1 - b0) / STEP)))
                cache = {}

                def col(i, j):
                    if (i, j) not in cache:
                        cache[(i, j)] = color_at(a0 + (a1 - a0) * i / nx, b0 + (b1 - b0) * j / nz)
                    return cache[(i, j)]
                for i in range(nx):
                    for j in range(nz):
                        xa, xb = a0 + (a1 - a0) * i / nx, a0 + (a1 - a0) * (i + 1) / nx
                        za, zb = b0 + (b1 - b0) * j / nz, b0 + (b1 - b0) * (j + 1) / nz
                        geo.oriented(m, [(xa, WATER_Y, za), (xb, WATER_Y, za), (xb, WATER_Y, zb), (xa, WATER_Y, zb)], (0, 1, 0),
                                     [col(i, j), col(i + 1, j), col(i + 1, j + 1), col(i, j + 1)], K.WATER)
        self.b.part("water", "Water", "Water", build)

    def clear(self, x, z, r, extra=()):
        C = self.C
        if not (C["minX"] + 1.0 < x < C["maxX"] - 1.0 and C["minZ"] + 1.0 < z < C["maxZ"] - 1.0):
            return False
        if any(rect_distance(x, z, p) < FLOTSAM_CLEAR + r for p in self.pieces):
            return False
        return all(math.hypot(x - ex, z - ez) > er + r for ex, ez, er in extra)

    def flotsam(self):
        taken = [(DUCK[0], DUCK[1], 3.0)]

        def build(m):
            kinds = ["drum"] * 14 + ["tyre"] * 5 + ["plank"] * 7 + ["hat"] * 2
            for kind in kinds:
                for _ in range(200):
                    x = self.rng.uniform(self.C["minX"], self.C["maxX"])
                    z = self.rng.uniform(self.C["minZ"], self.C["maxZ"])
                    if self.clear(x, z, 1.0, taken):
                        break
                else:
                    continue
                taken.append((x, z, 1.2))
                yaw = self.rng.uniform(0, math.pi)
                with geo.detail(m, 0.3):
                    if kind == "drum":
                        color = geo.jitter(self.rng.choice(P.DRUM_COLORS), self.rng, 0.06)
                        K.lying_drum(m, (x, WATER_Y + 0.08, z), 0.33, 0.95, yaw, color)
                    elif kind == "tyre":
                        K.tyre(m, (x, WATER_Y + 0.04, z), radius=0.45, width=0.2)
                    elif kind == "plank":
                        geo.box(m, (x, WATER_Y + 0.03, z), (2.2, 0.08, 0.3), geo.jitter(P.WOOD, self.rng, 0.08), K.LIT, yaw=yaw,
                                skip=("bottom",))
                    else:
                        # A hard hat, upside down, going round in circles.
                        geo.lathe(m, (x, WATER_Y - 0.1, z), [(0.0, 0.0), (0.2, 0.04), (0.27, 0.16), (0.36, 0.2), (0.36, 0.24)], 10,
                                  P.HAZARD, K.LIT, cap_top=False)
        self.b.part("flotsam", "Flotsam", "Flotsam", build)

    def duck(self):
        x, z = DUCK
        y = WATER_Y

        def build(m):
            with geo.detail(m, 0.1):
                # Facing south-west, towards the start.
                yaw = math.radians(200.0)
                fx, fz = math.sin(yaw), math.cos(yaw)
                rx, rz = fz, -fx

                def W(f, up, r):
                    return (x + fx * f + rx * r, y + up, z + fz * f + rz * r)
                geo.ellipsoid(m, W(0.0, 0.18, 0.0), (0.95, 0.62, 0.95), P.DUCK, K.LIT, rings=7, segments=14)
                geo.ellipsoid(m, W(-0.95, 0.5, 0.0), (0.3, 0.3, 0.3), P.DUCK, K.LIT, rings=4, segments=8)
                geo.ellipsoid(m, W(0.55, 1.02, 0.0), (0.52, 0.5, 0.52), P.DUCK, K.LIT, rings=7, segments=14)
                geo.ellipsoid(m, W(1.06, 0.92, 0.0), (0.26, 0.09, 0.2), P.MANKI_ORANGE, K.LIT, rings=4, segments=10)
                # One googly eye, one eyepatch (Manki's influence) with its strap.
                geo.ellipsoid(m, W(0.88, 1.16, -0.2), (0.07, 0.13, 0.13), P.EYE_WHITE, K.LIT, rings=4, segments=8)
                geo.ellipsoid(m, W(0.94, 1.13, -0.22), (0.04, 0.07, 0.07), P.INK, K.LIT, rings=3, segments=6)
                geo.ellipsoid(m, W(0.88, 1.16, 0.2), (0.07, 0.15, 0.15), P.INK, K.LIT, rings=4, segments=8)
                strap = [W(0.55 + 0.53 * math.cos(a), 1.2 + 0.12 * math.sin(a), 0.53 * math.sin(a))
                         for a in [k * math.pi / 8 for k in range(17)]]
                geo.tube(m, strap, 0.025, 4, P.INK, K.LIT)
        self.b.part("duck", "Duck", "Duck", build)
