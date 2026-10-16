"""The harbour village west of the boat: quay, fish market, houses, church and moored boats.

Rows of pastel houses climb the western slope above a straight stone quay. Every
house is a complete box sunk into the hill (no floating foundations), its front facing
the bay with shuttered windows, balconies and either a terracotta roof or a flat roof
terrace. The bell tower is the village landmark; the striped fish-market awning and
small boats moored bow-in along the quay explain the working harbour.
"""
from __future__ import annotations

import math
import random

from stagekit import geo
import bay as BAY
import palette as P
import textures as T

LAND = "SD_Land"
WOOD = "SD_Wood"
CLOTH = "SD_Cloth"
W = BAY.WATER_Y
FLOOR = 3.1
CHURCH_BOX = (-194.0, 2.0, -162.0, 30.0)  # x0, z0, x1, z1 kept clear of houses


class Village:
    def __init__(self, build):
        self.b = build
        self.rng = random.Random(build.seed + 500)
        self.houses = []
        self.ground_contacts = []

    def build_all(self):
        b = self.b
        b.part("background", "Quay", "Quay", self.quay)
        b.part("background", "Village", "Houses", self.village)
        b.part("background", "Church", "Church", self.church)
        b.part("background", "Market", "Fish Market", self.market)
        b.part("background", "Awnings", "Awnings", self.awnings)
        b.part("background", "Harbour Boats", "Quay Boats", self.quay_boats)
        b.part("background", "Harbour Boats", "Moorings", self.moorings)
        b.part("background", "Laundry", "Laundry", self.laundry)

    # ------------------------------------------------------------ quay
    def quay(self, m):
        rng = random.Random(self.b.seed + 501)
        x0, x1 = BAY.QUAY_BACK, BAY.QUAY_X
        z0, z1 = BAY.QUAY_Z
        top = BAY.QUAY_TOP + 0.02
        with geo.detail(m, 0.55):
            # Paving in strips so the stone colour varies.
            z = z0
            while z < z1:
                dz = min(4.0, z1 - z)
                geo.oriented(m, [(x0, top, z), (x1, top, z), (x1, top, z + dz), (x0, top, z + dz)], (0, 1, 0),
                             geo.jitter(P.STONE_QUAY, rng, 0.05), LAND)
                z += dz
            # Wall face towards the bay: block courses, darker and weedy at the waterline.
            courses = [(W - 2.0, W + 0.2, P.WET_ROCK), (W + 0.2, W + 0.6, P.STONE_QUAY_DARK)]
            y = W + 0.6
            while y < top - 0.05:
                y2 = min(top - 0.05, y + 0.55)
                courses.append((y, y2, geo.jitter(P.STONE_QUAY, rng, 0.06)))
                y = y2
            courses.append((top - 0.05, top, geo.scale(P.STONE_QUAY, 1.08)))
            for ya, yb, c in courses:
                geo.facade_quad(m, (x1, z0), (x1, z1), ya, yb, (1.0, 0.0), c, LAND)
            # Coping ends at both quay ends.
            for zz, n in ((z0, -1.0), (z1, 1.0)):
                geo.facade_quad(m, (x0, zz), (x1, zz), W - 2.0, top, (0.0, n), P.STONE_QUAY_DARK, LAND)
            # Bollards and lamp posts along the edge.
            z = z0 + 4.0
            k = 0
            while z < z1 - 2:
                geo.cylinder_between(m, (x1 - 0.7, top, z), (x1 - 0.7, top + 0.7, z), 0.24, 8, P.BOLLARD, LAND)
                geo.lathe(m, (x1 - 0.7, top + 0.7, z), [(0.32, 0.0), (0.32, 0.12), (0.0, 0.16)], 8, P.BOLLARD, LAND)
                if k % 3 == 1:
                    lx = x0 + 1.2
                    geo.cylinder_between(m, (lx, top, z), (lx, top + 4.2, z), 0.08, 6, P.BOLLARD, LAND)
                    geo.box(m, (lx, top + 4.45, z), (0.4, 0.5, 0.4), P.LANTERN_ROOF, LAND)
                z += 9.0
                k += 1
            # Crates, net piles and barrels left on the quay.
            for _ in range(22):
                cx = rng.uniform(x0 + 2.0, x1 - 2.0)
                cz = rng.uniform(z0 + 4, z1 - 4)
                if -34 < cz < -8:
                    continue  # the market stands here
                kind = rng.random()
                if kind < 0.45:
                    for s in range(rng.randint(1, 3)):
                        geo.box(m, (cx, top + 0.22 + s * 0.44, cz), (0.75, 0.44, 0.5), geo.jitter(P.CABIN_TRIM if s % 2 else P.ROPE_DARK, rng, 0.1),
                                LAND, yaw=rng.uniform(0, math.pi), skip=("bottom",))
                elif kind < 0.75:
                    geo.ellipsoid(m, (cx, top + 0.15, cz), (1.0, 0.35, 0.8), geo.jitter(P.NET, rng, 0.1), LAND, rings=3, segments=8)
                    for f in range(3):
                        geo.ellipsoid(m, (cx + rng.uniform(-0.7, 0.7), top + 0.42, cz + rng.uniform(-0.5, 0.5)), (0.12, 0.08, 0.12), P.NET_FLOAT, LAND, rings=2, segments=5)
                else:
                    geo.lathe(m, (cx, top, cz), [(0.3, 0.0), (0.34, 0.45), (0.3, 0.9)], 8, geo.jitter(P.RUST, rng, 0.1), LAND)

    # ------------------------------------------------------------ houses
    def village(self, m):
        rng = random.Random(self.b.seed + 510)
        rows = 9
        for r in range(rows):
            front = BAY.QUAY_BACK - 0.5 - r * 14.5
            z = BAY.QUAY_Z[0] - 6 + rng.uniform(0, 6) + (r % 2) * 3.0
            z_end = BAY.QUAY_Z[1] + 6 - r * 4.0
            k = 0
            while z < z_end:
                w = rng.uniform(5.5, 10.0)
                if k % rng.randint(3, 5) == 3:
                    z += rng.uniform(3.0, 4.5)  # alley or stair
                    k += 1
                    continue
                k += 1
                depth = rng.uniform(8.0, 11.5)
                cx = front - depth / 2
                if self._in_box(CHURCH_BOX, front, z, front - depth, z + w):
                    z += w
                    continue
                floors = rng.choice((2, 3, 3, 4)) if r < 3 else rng.choice((2, 2, 3))
                base = min(BAY.height(front, z), BAY.height(front, z + w))
                if base < BAY.QUAY_TOP - 0.01:
                    base = BAY.QUAY_TOP  # the frontage sits on the quay
                self.house(m, front, z, w, depth, base, floors, rng)
                z += w + rng.uniform(0.0, 0.4)

    @staticmethod
    def _in_box(box, xa, za, xb, zb):
        x0, z0, x1, z1 = box
        return not (max(xa, xb) < x0 or min(xa, xb) > x1 or max(za, zb) < z0 or min(za, zb) > z1)

    def house(self, m, front, z0, w, depth, base, floors, rng):
        """Front face at x = front, facing +X (the bay); spans z0..z0 + w."""
        x0, x1 = front - depth, front
        z1 = z0 + w
        height = floors * FLOOR
        bottom = base - 4.5
        top = base + height
        plaster = rng.choice(P.PLASTER)
        shutter = rng.choice(P.SHUTTER)
        with geo.detail(m, 0.3):
            geo.box_minmax(m, (x0, bottom, z0), (x1, top, z1), geo.jitter(plaster, rng, 0.04), LAND, skip=("bottom",),
                           top_color=geo.scale(plaster, 0.92))
            # Base course in stone.
            geo.facade_quad(m, (x1, z0), (x1, z1), bottom, base + 0.5, (1.0, 0.0), P.STONE_QUAY_DARK, LAND, off=0.02)
            # Roof: terracotta gable along z, or a flat roof terrace with a parapet.
            if rng.random() < 0.55:
                ridge = top + rng.uniform(1.5, 2.2)
                o = 0.35
                geo.oriented(m, [(x1 + o, top - 0.15, z0 - o), (x1 + o, top - 0.15, z1 + o), ((x0 + x1) / 2, ridge, z1 + o),
                                 ((x0 + x1) / 2, ridge, z0 - o)], (1, 1, 0), geo.jitter(P.TERRACOTTA, rng, 0.06), LAND)
                geo.oriented(m, [(x0 - o, top - 0.15, z1 + o), (x0 - o, top - 0.15, z0 - o), ((x0 + x1) / 2, ridge, z0 - o),
                                 ((x0 + x1) / 2, ridge, z1 + o)], (-1, 1, 0), geo.jitter(P.TERRACOTTA_DARK, rng, 0.06), LAND)
                for zz, n in ((z0, -1.0), (z1, 1.0)):
                    geo.oriented(m, [(x1, top, zz), (x0, top, zz), ((x0 + x1) / 2, ridge - 0.1, zz)], (0, 0, n), plaster, LAND)
                # Tile courses as darker lines on the front slope.
                for t in (0.33, 0.66):
                    xa = x1 + o + ((x0 + x1) / 2 - x1 - o) * t
                    ya = top - 0.15 + (ridge - top + 0.15) * t
                    geo.oriented(m, [(xa, ya + 0.02, z0 - o), (xa, ya + 0.02, z1 + o), (xa - 0.12, ya + 0.12, z1 + o),
                                     (xa - 0.12, ya + 0.12, z0 - o)], (1, 1, 0), P.TERRACOTTA_DARK, LAND)
            else:
                pt = top + 0.6
                for (a, b2, n) in (((x1, z0), (x1, z1), (1.0, 0.0)), ((x0, z1), (x0, z0), (-1.0, 0.0)),
                                   ((x1, z1), (x0, z1), (0.0, 1.0)), ((x0, z0), (x1, z0), (0.0, -1.0))):
                    geo.facade_quad(m, a, b2, top, pt, n, geo.scale(plaster, 0.97), LAND)
                geo.oriented(m, [(x1, pt, z0), (x1, pt, z1), (x1 - 0.25, pt, z1), (x1 - 0.25, pt, z0)], (0, 1, 0), P.CABIN_WHITE, LAND)
                if rng.random() < 0.5:  # stair hut on the roof terrace
                    sx = x0 + 1.6
                    geo.box_minmax(m, (sx - 1.2, top, z0 + 0.6), (sx + 1.2, top + 2.3, z0 + 2.8), plaster, LAND, skip=("bottom",))
        # Windows, shutters, doors and balconies on the bay front.
        n = max(1, int((w - 1.2) / 2.3))
        step = w / n
        for f in range(floors):
            y0 = base + f * FLOOR
            balcony = f > 0 and rng.random() < 0.35
            for i in range(n):
                cz = z0 + step * (i + 0.5)
                if f == 0 and i == n // 2:
                    geo.facade_quad(m, (x1, cz - 0.6), (x1, cz + 0.6), y0, y0 + 2.3, (1.0, 0.0), P.DOOR, LAND, off=0.03)
                    continue
                wy0, wy1 = y0 + 0.9, y0 + 2.25
                geo.facade_quad(m, (x1, cz - 0.42), (x1, cz + 0.42), wy0, wy1, (1.0, 0.0), P.WINDOW_DARK, LAND, off=0.03)
                if rng.random() < 0.8:
                    open_ = rng.random() < 0.6
                    for s in (-1, 1):
                        za = cz + s * (0.44 if open_ else 0.0)
                        zb = cz + s * (0.88 if open_ else 0.42)
                        geo.facade_quad(m, (x1, min(za, zb)), (x1, max(za, zb)), wy0, wy1, (1.0, 0.0), shutter, LAND, off=0.05)
                geo.facade_quad(m, (x1, cz - 0.55), (x1, cz + 0.55), wy0 - 0.12, wy0, (1.0, 0.0), P.CABIN_WHITE, LAND, off=0.06)
                if balcony:
                    geo.box_minmax(m, (x1, y0 + 0.75, cz - 0.85), (x1 + 0.8, y0 + 0.9, cz + 0.85), P.STONE_QUAY, LAND)
                    geo.facade_quad(m, (x1 + 0.8, cz - 0.85), (x1 + 0.8, cz + 0.85), y0 + 0.9, y0 + 1.75, (1.0, 0.0), P.BOLLARD, LAND)
                    if rng.random() < 0.6:
                        geo.box_minmax(m, (x1 + 0.55, y0 + 0.9, cz - 0.7), (x1 + 0.78, y0 + 1.15, cz + 0.7), rng.choice(P.FLOWER), LAND)
            # A few windows on the side walls.
            for zz, nrm in ((z0, -1.0), (z1, 1.0)):
                if rng.random() < 0.5:
                    cx = x1 - depth * rng.uniform(0.25, 0.6)
                    geo.facade_quad(m, (cx - 0.42, zz), (cx + 0.42, zz), y0 + 0.9, y0 + 2.25, (0.0, nrm), P.WINDOW_DARK, LAND, off=0.03)
        self.houses.append({"front": front, "z0": z0, "z1": z1, "base": base, "top": top})

    # ------------------------------------------------------------ church
    def church(self, m):
        x1 = -166.0          # facade facing the bay
        x0 = x1 - 22.0
        z0, z1 = 8.0, 20.0
        base = min(BAY.height(x1, z0), BAY.height(x1, z1)) - 0.2
        top = base + 11.0
        with geo.detail(m, 0.25):
            geo.box_minmax(m, (x0, base - 4.0, z0), (x1, top, z1), P.CHURCH, LAND, skip=("bottom",))
            ridge = top + 3.4
            for (zs, ze, n) in ((z0 - 0.4, (z0 + z1) / 2, -1.0), (z1 + 0.4, (z0 + z1) / 2, 1.0)):
                geo.oriented(m, [(x0 - 0.4, top, zs), (x1 + 0.4, top, zs), (x1 + 0.4, ridge, ze), (x0 - 0.4, ridge, ze)],
                             (0, 1, n), P.TERRACOTTA, LAND)
            geo.oriented(m, [(x1, top, z0), (x1, top, z1), (x1, ridge, (z0 + z1) / 2)], (1, 0, 0), P.CHURCH, LAND)
            geo.oriented(m, [(x0, top, z1), (x0, top, z0), (x0, ridge, (z0 + z1) / 2)], (-1, 0, 0), P.CHURCH, LAND)
            # Facade: door, rose window and pilasters.
            geo.facade_quad(m, (x1, 12.6), (x1, 15.4), base, base + 4.6, (1.0, 0.0), P.DOOR, LAND, off=0.04)
            geo.facade_quad(m, (x1, 13.2), (x1, 14.8), base + 6.4, base + 8.0, (1.0, 0.0), P.WINDOW_DARK, LAND, off=0.04)
            for zz in (z0 + 0.4, z1 - 1.0):
                geo.facade_quad(m, (x1, zz), (x1, zz + 0.6), base, top, (1.0, 0.0), geo.scale(P.CHURCH, 0.9), LAND, off=0.08)
            # Bell tower at the north-east corner: shaft, belfry, clock and a tiled cap.
            tx0, tx1, tz0, tz1 = x1 - 5.5, x1 + 0.5, z1, z1 + 6.0
            t_top = base + 24.0
            geo.box_minmax(m, (tx0, base - 4.0, tz0), (tx1, t_top, tz1), geo.scale(P.CHURCH, 0.97), LAND, skip=("bottom",))
            for face, (a, b2, n) in enumerate((((tx1, tz0), (tx1, tz1), (1.0, 0.0)), ((tx0, tz1), (tx0, tz0), (-1.0, 0.0)),
                                               ((tx1, tz1), (tx0, tz1), (0.0, 1.0)), ((tx0, tz0), (tx1, tz0), (0.0, -1.0)))):
                mx, mz = (a[0] + b2[0]) / 2, (a[1] + b2[1]) / 2
                ux, uz = (b2[0] - a[0]) / 6.0, (b2[1] - a[1]) / 6.0
                geo.facade_quad(m, (mx - ux * 1.6, mz - uz * 1.6), (mx + ux * 1.6, mz + uz * 1.6), t_top - 5.2, t_top - 1.2, n, P.WINDOW_DARK, LAND, off=0.03)
                (u0, v0), (u1, v1) = T.region_uv("clock")
                geo.facade_quad(m, (mx - ux * 1.3, mz - uz * 1.3), (mx + ux * 1.3, mz + uz * 1.3), t_top - 8.8, t_top - 6.2, n,
                                (1.0, 1.0, 1.0), WOOD, off=0.03, uvs=((u0, v0), (u1, v1)))
                geo.facade_quad(m, a, b2, t_top - 0.6, t_top, n, geo.scale(P.CHURCH, 0.85), LAND, off=0.06)
            cx, cz = (tx0 + tx1) / 2, (tz0 + tz1) / 2
            geo.box(m, (cx, t_top - 3.4, cz), (1.2, 1.4, 1.2), P.BELL, LAND)
            geo.oriented(m, [(tx0 - 0.3, t_top, tz0 - 0.3), (tx1 + 0.3, t_top, tz0 - 0.3), (cx, t_top + 4.2, cz)], (0, 0.5, -1), P.TERRACOTTA, LAND)
            geo.oriented(m, [(tx1 + 0.3, t_top, tz0 - 0.3), (tx1 + 0.3, t_top, tz1 + 0.3), (cx, t_top + 4.2, cz)], (1, 0.5, 0), P.TERRACOTTA_DARK, LAND)
            geo.oriented(m, [(tx1 + 0.3, t_top, tz1 + 0.3), (tx0 - 0.3, t_top, tz1 + 0.3), (cx, t_top + 4.2, cz)], (0, 0.5, 1), P.TERRACOTTA, LAND)
            geo.oriented(m, [(tx0 - 0.3, t_top, tz1 + 0.3), (tx0 - 0.3, t_top, tz0 - 0.3), (cx, t_top + 4.2, cz)], (-1, 0.5, 0), P.TERRACOTTA_DARK, LAND)
            geo.cylinder_between(m, (cx, t_top + 4.1, cz), (cx, t_top + 5.6, cz), 0.06, 6, P.LANTERN_ROOF, LAND)
            geo.box(m, (cx, t_top + 5.1, cz), (0.9, 0.08, 0.08), P.LANTERN_ROOF, LAND)
        self.ground_contacts.append({"name": "church", "base": round(base, 2)})

    # ------------------------------------------------------------ fish market
    def market(self, m):
        x0, x1 = BAY.QUAY_BACK + 1.5, BAY.QUAY_X - 1.6
        z0, z1 = -30.0, -12.0
        top = BAY.QUAY_TOP + 0.02
        roof = top + 3.4
        with geo.detail(m, 0.25):
            for x in (x0, x1):
                for z in (z0, (z0 + z1) / 2, z1):
                    geo.cylinder_between(m, (x, top, z), (x, roof, z), 0.09, 6, P.CABIN_WHITE, LAND)
            # Counter with crates of fish.
            geo.box_minmax(m, (x1 - 1.4, top, z0 + 1.0), (x1 - 0.4, top + 1.0, z1 - 1.0), P.CABIN_TRIM, LAND, skip=("bottom",))
            z = z0 + 1.3
            while z < z1 - 1.4:
                geo.box_minmax(m, (x1 - 1.35, top + 1.0, z), (x1 - 0.45, top + 1.25, z + 0.7), P.ROPE_DARK, LAND, skip=("bottom",))
                geo.oriented(m, [(x1 - 1.3, top + 1.24, z + 0.05), (x1 - 0.5, top + 1.24, z + 0.05), (x1 - 0.5, top + 1.24, z + 0.65),
                                 (x1 - 1.3, top + 1.24, z + 0.65)], (0, 1, 0), P.FISH, LAND)
                z += 0.85
        # The PESCHERIA board above the counter, facing the bay.
        (u0, v0), (u1, v1) = T.region_uv("market")
        # Seen from the bay (facing -X) the viewer's left is -Z, so u runs from z0 to z1.
        geo.facade_quad(m, (x1 + 0.1, z0 + 3.0), (x1 + 0.1, z1 - 3.0), roof + 0.1, roof + 1.5, (1.0, 0.0), (1.0, 1.0, 1.0), WOOD,
                        uvs=((u0, v0), (u1, v1)))
        geo.facade_quad(m, (x1 + 0.09, z1 - 3.0), (x1 + 0.09, z0 + 3.0), roof + 0.1, roof + 1.5, (-1.0, 0.0), P.CABIN_TRIM, WOOD)

    def awnings(self, m):
        """Striped canvas over the market and the quayside shops (cloth sways in the breeze)."""
        top = BAY.QUAY_TOP + 0.02
        x0, x1 = BAY.QUAY_BACK + 1.2, BAY.QUAY_X - 1.2
        self._awning(m, x0, x1, -30.4, -11.6, top + 3.4, top + 2.6, P.AWNING[1])
        rng = random.Random(self.b.seed + 520)
        for h in self.houses:
            if abs(h["front"] - (BAY.QUAY_BACK - 0.5)) > 0.01 or rng.random() > 0.35:
                continue
            if -34 < h["z0"] < -8 or -34 < h["z1"] < -8:
                continue
            fx = h["front"]
            self._awning(m, fx, fx + 2.2, h["z0"] + 0.4, h["z1"] - 0.4, h["base"] + 2.9, h["base"] + 2.3,
                         rng.choice((P.AWNING[1], P.AWNING_RED, P.HULL_GREEN)))

    @staticmethod
    def _awning(m, xa, xb, z0, z1, y_back, y_front, stripe):
        stripes = max(2, int((z1 - z0) / 0.7))
        with geo.state(m, alpha=0.5):
            for i in range(stripes):
                za = z0 + (z1 - z0) * i / stripes
                zb = z0 + (z1 - z0) * (i + 1) / stripes
                c = stripe if i % 2 == 0 else P.AWNING[0]
                geo.oriented(m, [(xa, y_back, za), (xb, y_front, za), (xb, y_front, zb), (xa, y_back, zb)], (0.3, 1, 0), c, CLOTH)
                geo.oriented(m, [(xb, y_front, za), (xb, y_front - 0.35, za), (xb, y_front - 0.35, zb), (xb, y_front, zb)], (1, 0, 0), c, CLOTH)

    # ------------------------------------------------------------ boats
    @staticmethod
    def small_boat(m, pos, yaw, length, beam, hull, trim, rng, cabin=False, mast=0.0, mat=WOOD):
        """A gozzo-style open boat floating at the waterline: lofted hull, gunwale, thwarts.
        pos is the waterline centre; yaw 0 points the bow along +Z."""
        cx, wy, cz = pos
        c, s = math.cos(yaw), math.sin(yaw)
        free = 0.55 + 0.05 * length  # freeboard
        draft = 0.35

        def P2(u, y, v):  # u starboard, v forward
            return (cx + u * c + v * s, wy + y, cz - u * s + v * c)

        stations = [-0.5, -0.42, -0.25, 0.0, 0.25, 0.4, 0.5]
        widths = [0.62, 0.86, 0.98, 1.0, 0.9, 0.62, 0.02]
        sheer = [0.12, 0.05, 0.0, 0.0, 0.05, 0.16, 0.32]
        rows, inner = [], []
        for t, wfac, sh in zip(stations, widths, sheer):
            v = t * length
            hw = beam / 2 * wfac
            # Port gunwale, port chine, keel, starboard chine, starboard gunwale.
            rows.append([P2(-hw, free + sh, v), P2(-hw * 0.8, 0.0, v), P2(0.0, -draft, v), P2(hw * 0.8, 0.0, v), P2(hw, free + sh, v)])
            inner.append((P2(-hw * 0.84, 0.18, v), P2(hw * 0.84, 0.18, v)))
        with geo.detail(m, 0.2):
            for i in range(len(rows) - 1):
                for j in range(4):
                    q = [rows[i][j], rows[i + 1][j], rows[i + 1][j + 1], rows[i][j + 1]]
                    mid = [sum(p[k] for p in q) / 4 for k in range(3)]
                    out = (mid[0] - cx, -1.0 if j in (1, 2) else 0.3, mid[2] - cz)
                    geo.oriented(m, q, out, hull, mat)
                    if j in (0, 3):  # the inside of the topsides, seen from above
                        geo.oriented(m, q, (-out[0], 0.2, -out[2]), geo.scale(hull, 0.6), mat)
                # Gunwale bands.
                for k in (0, 4):
                    p, q2 = rows[i][k], rows[i + 1][k]
                    band = [p, q2, (q2[0], q2[1] - 0.16, q2[2]), (p[0], p[1] - 0.16, p[2])]
                    mid = [sum(pp[c2] for pp in band) / 4 for c2 in range(3)]
                    geo.oriented(m, band, (mid[0] - cx, 0.0, mid[2] - cz), trim, mat)
                # Floorboards between the topsides.
                floor = [inner[i][0], inner[i + 1][0], inner[i + 1][1], inner[i][1]]
                geo.oriented(m, floor, (0, 1, 0), geo.scale(P.TEAK, 0.8), mat)
            # Transom.
            geo.oriented(m, rows[0], (-s, 0.0, -c), trim, mat)
            # Thwarts.
            for v in (-0.15, 0.12):
                p = P2(0, 0.0, v * length)
                geo.box(m, (p[0], wy + free * 0.7, p[2]), (beam * 0.82, 0.06, 0.3), P.TEAK, mat, yaw=yaw, skip=("bottom",))
            if cabin:
                p = P2(0, 0.0, 0.05 * length)
                geo.box(m, (p[0], wy + free + 0.45, p[2]), (beam * 0.6, 0.9, length * 0.22), P.CABIN_WHITE, mat, yaw=yaw, skip=("bottom",),
                        top_color=P.CABIN_ROOF)
            if mast > 0:
                p = P2(0, 0.0, 0.18 * length)
                geo.cylinder_between(m, (p[0], wy, p[2]), (p[0], wy + mast, p[2]), 0.06, 5, P.MAST, mat)

    def quay_boats(self, m):
        """Small boats moored bow-in along the quay (bows west, sterns towards the bay)."""
        rng = random.Random(self.b.seed + 530)
        z = BAY.QUAY_Z[0] + 8.0
        while z < BAY.QUAY_Z[1] - 8.0:
            if -36 < z < -6 or rng.random() < 0.25:
                z += rng.uniform(4.0, 7.0)
                continue
            L = rng.uniform(5.0, 7.5)
            B = L * rng.uniform(0.34, 0.4)
            x = BAY.QUAY_X + 0.6 + L / 2
            hull = rng.choice(P.BOAT_HULLS)
            trim = rng.choice(P.BOAT_TRIM)
            self.small_boat(m, (x, W, z), math.radians(-90 + rng.uniform(-6, 6)), L, B, hull, trim, rng,
                            cabin=rng.random() < 0.25, mast=rng.choice((0.0, 0.0, 3.5)))
            # Mooring line from the bow to a bollard.
            geo.cylinder_between(m, (BAY.QUAY_X - 0.7, BAY.QUAY_TOP + 0.5, z), (BAY.QUAY_X + 0.6, W + 0.9, z), 0.025, 4, P.ROPE, WOOD, caps=False)
            z += B + rng.uniform(1.2, 2.4)

    def moorings(self, m):
        """Boats on swinging moorings in the inner harbour and a few in the bay."""
        rng = random.Random(self.b.seed + 540)
        spots = []
        for _ in range(400):
            x, z = rng.uniform(-110, 70), rng.uniform(70, 160)
            if float(BAY.shore_distance(x, z)) > -9 or BAY.mole_distance(x, z) < 20:
                continue
            if any(math.hypot(x - a, z - b2) < 11 for a, b2 in spots):
                continue
            spots.append((x, z))
            if len(spots) >= 16:
                break
        # A handful farther out in the bay, well beyond the camera envelope.
        for x, z in ((-62.0, -64.0), (-78.0, 34.0), (58.0, -82.0), (96.0, 38.0), (-40.0, -96.0)):
            spots.append((x, z))
        for i, (x, z) in enumerate(spots):
            L = rng.uniform(5.5, 9.0)
            yaw = math.radians(80 + rng.uniform(-14, 14))  # bows into the sea breeze (east)
            sail = i % 3 == 0
            self.small_boat(m, (x, W, z), yaw, L, L * rng.uniform(0.32, 0.38), rng.choice(P.BOAT_HULLS), rng.choice(P.BOAT_TRIM), rng,
                            cabin=sail or rng.random() < 0.4, mast=rng.uniform(7.0, 10.0) if sail else 0.0)
            # Mooring buoy just ahead of the bow.
            bx, bz = x + math.sin(yaw) * (L / 2 + 1.5), z + math.cos(yaw) * (L / 2 + 1.5)
            geo.ellipsoid(m, (bx, W + 0.1, bz), (0.35, 0.35, 0.35), rng.choice(P.BUOY), WOOD, rings=3, segments=6)

    # ------------------------------------------------------------ laundry
    def laundry(self, m):
        """Washing lines strung across a few alleys of the lower rows."""
        rng = random.Random(self.b.seed + 550)
        fronts = sorted({round(h["front"], 2) for h in self.houses}, reverse=True)[:3]
        lines = 0
        for front in fronts:
            row = sorted((h for h in self.houses if abs(h["front"] - front) < 0.01), key=lambda h: h["z0"])
            for a, b2 in zip(row[:-1], row[1:]):
                gap = b2["z0"] - a["z1"]
                if not (2.5 < gap < 5.0) or rng.random() < 0.4:
                    continue
                y = min(a["top"], b2["top"]) - 1.8
                x = front - 0.6
                p0, p1 = (x, y, a["z1"]), (x, y, b2["z0"])
                pts = geo.catenary(p0, p1, 0.3, 6)
                with geo.state(m, alpha=0.0):
                    for q0, q1 in zip(pts[:-1], pts[1:]):
                        geo.cylinder_between(m, q0, q1, 0.015, 3, P.ROPE_DARK, CLOTH, caps=False)
                with geo.state(m, alpha=0.8):
                    for k in range(1, 6):
                        p = pts[k]
                        c = rng.choice(P.LAUNDRY)
                        hgt = rng.uniform(0.5, 0.9)
                        wid = rng.uniform(0.4, 0.7)
                        geo.oriented(m, [(p[0], p[1], p[2] - wid / 2), (p[0], p[1], p[2] + wid / 2), (p[0], p[1] - hgt, p[2] + wid / 2),
                                         (p[0], p[1] - hgt, p[2] - wid / 2)], (1, 0, 0), c, CLOTH)
                lines += 1
        return lines
