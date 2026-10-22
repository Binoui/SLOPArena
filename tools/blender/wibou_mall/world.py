"""Neo Mall's world below and above the route: the mopped ground floor (the KO plane is 0.2 m
under it) with the Grand Court fountain, benches, palms, wet floor signs and the mall cop's
Segway; slender columns under the floating galleries; the glass barrel vault over the atrium,
its ribs, the moon and the stars; and the landmark, a giant katana sign hanging over the Grand
Court with its BLADES OF THE EAST banner. Nothing here is walkable or within any deck's reach.
"""
from __future__ import annotations

import math

from stagekit import geo
import palette as P
import parts as K
import textures as T

FLOOR_RECTS = [(-16.0, -50.0, 16.0, -8.0), (-18.0, -8.0, 16.0, 33.0), (-8.0, 33.0, 16.0, 45.0)]
VAULT_X = (-18.0, 16.0)
VAULT_Z = (-50.0, 45.0)
VAULT_RISE = 6.5
DOME = 24.0
KATANA_Z = -1.5
KATANA_Y = 19.5


class World:
    def __init__(self, b):
        self.b = b
        self.P = b.pieces
        self.spec = b.data["spec"]
        self.solids = b.data["solids"]
        self.rng = b.rng

    def build(self):
        self.floor()
        self.fountain()
        self.props()
        self.columns()
        self.vault()
        self.katana()

    # ------------------------------------------------------------ the mopped ground floor
    def _solid_over(self, x, z):
        for name in ("Tower", "Podium"):
            _, s = self.solids[name]
            if s[0] - 1e-3 <= x <= s[3] + 1e-3 and s[2] - 1e-3 <= z <= s[5] + 1e-3:
                return True
        return False

    def floor(self):
        rng = self.rng

        def build(m):
            for x0, z0, x1, z1 in FLOOR_RECTS:
                nx, nz = int(round(x1 - x0)), int(round(z1 - z0))
                for i in range(nx):
                    for k in range(nz):
                        a, c = x0 + i * (x1 - x0) / nx, x0 + (i + 1) * (x1 - x0) / nx
                        b, d = z0 + k * (z1 - z0) / nz, z0 + (k + 1) * (z1 - z0) / nz
                        if self._solid_over((a + c) / 2, (b + d) / 2):
                            continue
                        tone = 0.92 if (i + k) % 2 else 1.0
                        wet = 0.82 if math.sin(a * 0.37 + b * 0.21) * math.cos(b * 0.13 - a * 0.09) > 0.55 else 1.0
                        col = geo.jitter(geo.scale(P.FLOOR_L1, tone * wet), rng, 0.02)
                        with geo.detail(m, 0.25):
                            geo.oriented(m, [(a, K.FLOOR_Y, b), (a, K.FLOOR_Y, d), (c, K.FLOOR_Y, d), (c, K.FLOOR_Y, b)],
                                         (0, 1, 0), col, K.GLASS)
        self.b.part("world", "Floor", "Floor", build)

    # ------------------------------------------------------------ the Grand Court fountain
    def fountain(self):
        cx, cz = 0.0, 0.0

        def build(m):
            y = K.FLOOR_Y
            geo.lathe(m, (cx, y, cz), [(3.3, 0.0), (3.3, 0.55), (3.0, 0.62), (2.8, 0.55), (2.8, 0.35)], 32,
                      [P.CREAM, P.COLUMN, P.COLUMN, P.WALL_DARK], cap_top=False)
            with geo.state(m, alpha=0.55):
                K.hdisc(m, cx, y + 0.4, cz, 2.8, P.WATER, n=32, mat=K.GLOW)
            geo.lathe(m, (cx, y + 0.35, cz), [(0.45, 0.0), (0.35, 1.3), (1.3, 1.5), (1.4, 1.7), (0.3, 1.75)], 20,
                      [P.CREAM, P.COLUMN, P.COLUMN, P.CREAM])
            geo.lathe(m, (cx, y + 2.05, cz), [(0.25, 0.0), (0.18, 0.9), (0.6, 1.05), (0.1, 1.15)], 16, P.CREAM)
            with geo.state(m, alpha=0.7):
                for k in range(8):
                    a = 2 * math.pi * k / 8
                    p0 = (cx + 1.3 * math.cos(a), y + 1.75, cz + 1.3 * math.sin(a))
                    p1 = (cx + 2.2 * math.cos(a), y + 0.45, cz + 2.2 * math.sin(a))
                    geo.cylinder_between(m, p0, p1, 0.05, 5, P.WATER, K.GLOW, caps=False)
                geo.cylinder_between(m, (cx, y + 3.2, cz), (cx, y + 4.4, cz), 0.06, 5, P.WATER, K.GLOW, caps=False)
            rng = self.rng
            for _ in range(24):
                r, a = rng.uniform(0.6, 2.5), rng.uniform(0, 2 * math.pi)
                with geo.state(m, alpha=0.4):
                    K.hdisc(m, cx + r * math.cos(a), y + 0.41, cz + r * math.sin(a), 0.04, P.YELLOW, n=6, mat=K.GLOW)
        self.b.part("world", "Fountain", "Fountain", build)

    # ------------------------------------------------------------ ground floor props
    def props(self):
        rng = self.rng

        def build(m):
            y = K.FLOOR_Y
            # benches round the fountain
            for a in (0.0, math.pi / 2, math.pi, 3 * math.pi / 2):
                x, z = 5.0 * math.cos(a), 5.0 * math.sin(a)
                geo.box(m, (x, y + 0.45, z), (2.2, 0.1, 0.6), P.WOOD, yaw=a + math.pi / 2)
                for s in (-0.9, 0.9):
                    geo.box(m, (x + s * math.cos(a + math.pi / 2), y + 0.22, z - s * math.sin(a + math.pi / 2)), (0.12, 0.44, 0.5),
                            P.STEEL_DARK, yaw=a + math.pi / 2)
            # potted palms
            for x, z in ((-6.0, -6.0), (6.0, 6.0), (-6.0, 6.0), (6.0, -6.0), (-4.0, -46.0), (10.0, -46.0), (0.0, 40.0)):
                geo.lathe(m, (x, y, z), [(0.45, 0.0), (0.55, 0.7), (0.5, 0.75)], 12, P.POT)
                geo.cylinder_between(m, (x, y + 0.7, z), (x + 0.1, y + 2.6, z), 0.08, 6, P.WOOD)
                for k in range(7):
                    a = 2 * math.pi * k / 7
                    tip = (x + 1.3 * math.cos(a), y + 2.1, z + 1.3 * math.sin(a))
                    base = (x + 0.1, y + 2.6, z)
                    side = (-math.sin(a) * 0.3, 0.0, math.cos(a) * 0.3)
                    m.poly([base, (tip[0] + side[0], tip[1], tip[2] + side[2]), tip], geo.jitter(P.PLANT, rng, 0.1))
                    m.poly([base, tip, (tip[0] + side[0], tip[1], tip[2] + side[2])], geo.jitter(P.PLANT, rng, 0.1))
            # wet floor signs, everywhere: the floor really is wet
            for x, z, yaw in ((3.4, -4.0, 0.3), (-4.2, 3.6, 1.2), (8.0, -32.0, 0.0), (-10.0, 20.0, 0.7), (4.0, 30.0, 2.0),
                              (-2.0, -44.0, 1.0), (12.0, 10.0, 0.4)):
                self.wet_sign(m, x, y, z, yaw)
            # the janitor's cart beside the fountain, mop in the bucket
            geo.box_minmax(m, (3.6, y + 0.1, 2.6), (4.8, y + 1.0, 3.3), P.HAZARD_YELLOW)
            geo.cylinder_between(m, (4.2, y + 1.0, 2.95), (4.6, y + 2.3, 2.7), 0.025, 5, P.WOOD)
            for x, z in ((3.7, 2.7), (4.7, 2.7), (3.7, 3.2), (4.7, 3.2)):
                K.hdisc(m, x, y + 0.08, z, 0.08, P.INK, n=6)
            # the mall cop's Segway, parked by the cinema
            sx, sz = 2.0, 42.0
            geo.box_minmax(m, (sx - 0.3, y + 0.15, sz - 0.25), (sx + 0.3, y + 0.25, sz + 0.25), P.INK)
            for dz in (-0.32, 0.32):
                geo.cylinder_between(m, (sx, y + 0.22, sz + dz), (sx, y + 0.22, sz + dz + 0.08 * (1 if dz > 0 else -1)), 0.22, 10, P.HANDRAIL)
            geo.cylinder_between(m, (sx, y + 0.25, sz), (sx - 0.1, y + 1.15, sz), 0.03, 5, P.STEEL)
            geo.box_minmax(m, (sx - 0.14, y + 1.12, sz - 0.3), (sx - 0.06, y + 1.18, sz + 0.3), P.INK)
            # closed kiosk carts on the ground floor
            for x, z in ((-6.0, -28.0), (6.0, 18.0)):
                geo.box_minmax(m, (x - 1.0, y, z - 1.6), (x + 1.0, y + 1.1, z + 1.6), P.WOOD)
                geo.box_minmax(m, (x - 1.1, y + 1.1, z - 1.7), (x + 1.1, y + 1.2, z + 1.7), P.CREAM)
                geo.box_minmax(m, (x - 0.05, y + 1.2, z - 1.5), (x + 0.05, y + 2.4, z - 1.4), P.STEEL)
                geo.box_minmax(m, (x - 1.2, y + 2.4, z - 1.8), (x + 1.2, y + 2.5, z + 1.8), P.RED)
        self.b.part("world", "Props", "GroundProps", build)

    def wet_sign(self, m, x, y, z, yaw):
        c, s = math.cos(yaw), math.sin(yaw)
        for side in (-1.0, 1.0):
            nx, nz = -s * side, c * side
            px, pz = x + nx * 0.12, z + nz * 0.12
            plane = K.Plane.facing((px, y, pz), (nx, nz))
            u = (x - plane.o[0]) * plane.r[0] + (z - plane.o[2]) * plane.r[2]
            K.panel(m, plane, u - 0.22, 0.0, u + 0.22, 0.62, (1.0, 1.0, 1.0), uvs=T.uv("wet_floor"))

    # ------------------------------------------------------------ columns under the galleries
    def columns(self):
        edges = {
            "WestL2": [("x", -8.6, -48.0, -10.0)],
            "EastL2": [("x", 8.6, -18.0, 2.0)],
            "LandingL3": [("z", 13.6, 6.6, 15.0)],
            "FoodCourt": [("x", -8.6, 9.0, 31.0), ("z", 8.6, -17.0, -9.0)],
            "EastL4": [("x", 8.6, 21.0, 44.0), ("z", 20.6, 9.0, 15.0)],
        }

        def build(m):
            for name, runs in edges.items():
                base = self.spec["floating"][name]
                for axis, fixed, a, b in runs:
                    n = max(1, int(round((b - a) / 8.0)))
                    for i in range(n + 1):
                        t = a + (b - a) * i / n
                        x, z = (fixed, t) if axis == "x" else (t, fixed)
                        if self._solid_over(x, z):
                            continue
                        K.round_post(m, x, z, K.FLOOR_Y, base - 0.02, 0.28, P.COLUMN, 12)
                        geo.lathe(m, (x, base - 0.5, z), [(0.28, 0.0), (0.42, 0.48)], 12, P.BRASS, cap_top=False)
                        geo.lathe(m, (x, K.FLOOR_Y, z), [(0.45, 0.0), (0.45, 0.25), (0.28, 0.35)], 12, P.WALL_DARK, cap_top=False)
        self.b.part("world", "Columns", "Columns", build)

    # ------------------------------------------------------------ the glass vault
    def vault(self):
        x0, x1 = VAULT_X
        z0, z1 = VAULT_Z
        span = x1 - x0
        half = span / 2
        R = (half * half + VAULT_RISE * VAULT_RISE) / (2 * VAULT_RISE)
        cx = (x0 + x1) / 2
        cy = DOME + VAULT_RISE - R
        a0 = math.asin(half / R)
        segs = 16
        bays = int(round((z1 - z0) / 3.0))
        rng = self.rng

        def arc(t):
            a = -a0 + 2 * a0 * t
            return cx + R * math.sin(a), cy + R * math.cos(a)

        def build(m):
            for i in range(segs):
                xa, ya = arc(i / segs)
                xb, yb = arc((i + 1) / segs)
                for k in range(bays):
                    za = z0 + (z1 - z0) * k / bays
                    zb = z0 + (z1 - z0) * (k + 1) / bays
                    depth = 1.0 - 0.35 * abs(i / segs - 0.5) * 2
                    col = geo.mix(P.NIGHT_LOW, P.NIGHT, depth)
                    geo.oriented(m, [(xa, ya, za), (xb, yb, za), (xb, yb, zb), (xa, ya, zb)], (cx - (xa + xb) / 2, cy - (ya + yb) / 2, 0),
                                 col, K.SKY)
            # gables
            for zz, nz in ((z0, 1.0), (z1, -1.0)):
                for i in range(segs):
                    xa, ya = arc(i / segs)
                    xb, yb = arc((i + 1) / segs)
                    geo.oriented(m, [(xa, DOME, zz), (xb, DOME, zz), (xb, yb, zz), (xa, ya, zz)], (0, 0, nz), P.NIGHT, K.SKY)
            # stars and the moon, just inside the glass
            with geo.state(m, alpha=1.0):
                for _ in range(140):
                    t, z = rng.uniform(0.08, 0.92), rng.uniform(z0 + 1, z1 - 1)
                    x, y = arc(t)
                    s = rng.uniform(0.04, 0.09)
                    geo.oriented(m, [(x - s, y - 0.05, z - s), (x + s, y - 0.05, z - s), (x + s, y - 0.05, z + s), (x - s, y - 0.05, z + s)],
                                 (0, -1, 0), P.STAR, K.GLOW, [T.LAMP_UV] * 4)
                mx, my = arc(0.62)
                n = 24
                mz = -18.0
                c = (mx, my - 0.12, mz)
                pts = [(mx + 2.2 * math.cos(2 * math.pi * k / n), my - 0.12, mz + 2.2 * math.sin(2 * math.pi * k / n)) for k in range(n)]
                for k in range(n):
                    geo.oriented(m, [c, pts[(k + 1) % n], pts[k]], (0, -1, 0), P.MOON, K.GLOW, [T.LAMP_UV] * 3)
        self.b.part("dome", "Vault", "Vault", build)

        def ribs(m):
            for k in range(bays + 1):
                z = z0 + (z1 - z0) * k / bays
                for i in range(segs):
                    xa, ya = arc(i / segs)
                    xb, yb = arc((i + 1) / segs)
                    geo.oriented(m, [(xa, ya - 0.02, z - 0.07), (xb, yb - 0.02, z - 0.07), (xb, yb - 0.25, z - 0.07), (xa, ya - 0.25, z - 0.07)],
                                 (0, 0, -1), P.STEEL_DARK)
                    geo.oriented(m, [(xa, ya - 0.25, z - 0.07), (xb, yb - 0.25, z - 0.07), (xb, yb - 0.25, z + 0.07), (xa, ya - 0.25, z + 0.07)],
                                 (0, -1, 0), P.STEEL)
                    geo.oriented(m, [(xa, ya - 0.02, z + 0.07), (xa, ya - 0.25, z + 0.07), (xb, yb - 0.25, z + 0.07), (xb, yb - 0.02, z + 0.07)],
                                 (0, 0, 1), P.STEEL_DARK)
            for i in range(1, segs):
                x, y = arc(i / segs)
                geo.oriented(m, [(x - 0.05, y - 0.04, z0), (x + 0.05, y - 0.04, z0), (x + 0.05, y - 0.04, z1), (x - 0.05, y - 0.04, z1)],
                             (0, -1, 0), P.STEEL_DARK)
            # two pendant globes over the south atrium, far from every deck
            for z in (-42.0, -24.0):
                top = arc(0.5)[1]
                K.cable(m, (0.0, 18.8, z), (0.0, top, z), P.STEEL_DARK, 0.03)
                with geo.state(m, alpha=1.0):
                    geo.ellipsoid(m, (0.0, 18.4, z), (0.7, 0.55, 0.7), P.LAMP, K.GLOW, rings=6, segments=12)
        self.b.part("dome", "Vault", "Ribs", ribs)

    # ------------------------------------------------------------ the giant katana
    def katana(self):
        z = KATANA_Z
        y = KATANA_Y
        guard_x, tip_x, pommel_x = -5.5, 9.5, -9.5

        def build(m):
            n = 24
            pts = []
            for i in range(n + 1):
                t = i / n
                x = guard_x + (tip_x - guard_x) * t
                curve = 0.55 * math.sin(math.pi * t * 0.9) * t
                width = 0.6 * (1 - t) ** 0.35 if t < 0.97 else 0.0
                pts.append((x, y + curve, width))
            for zz, nz in ((z - 0.05, -1.0), (z + 0.05, 1.0)):
                for (xa, ya, wa), (xb, yb, wb) in zip(pts, pts[1:]):
                    geo.oriented(m, [(xa, ya - wa * 0.15, zz), (xb, yb - wb * 0.15, zz), (xb, yb + wb * 0.85, zz), (xa, ya + wa * 0.85, zz)],
                                 (0, 0, nz), P.KATANA_STEEL, K.GLASS)
                    with geo.state(m, alpha=0.7):
                        geo.oriented(m, [(xa, ya - wa * 0.15 - 0.002, zz + nz * 0.004), (xb, yb - wb * 0.15 - 0.002, zz + nz * 0.004),
                                         (xb, yb - wb * 0.15 + 0.07, zz + nz * 0.004), (xa, ya - wa * 0.15 + 0.07, zz + nz * 0.004)],
                                     (0, 0, nz), P.KATANA_EDGE, K.GLOW, [T.LAMP_UV] * 4)
            for (xa, ya, wa), (xb, yb, wb) in zip(pts, pts[1:]):
                geo.oriented(m, [(xa, ya + wa * 0.85, z - 0.05), (xa, ya + wa * 0.85, z + 0.05), (xb, yb + wb * 0.85, z + 0.05), (xb, yb + wb * 0.85, z - 0.05)],
                             (0, 1, 0), P.KATANA_STEEL)
                geo.oriented(m, [(xa, ya - wa * 0.15, z - 0.05), (xb, yb - wb * 0.15, z - 0.05), (xb, yb - wb * 0.15, z + 0.05), (xa, ya - wa * 0.15, z + 0.05)],
                             (0, -1, 0), P.KATANA_EDGE)
            # the guard, the hilt with its diamond wrap and the pommel
            plane = K.Plane.facing((guard_x, y, z), (1.0, 0.0))
            geo.cylinder_between(m, (guard_x - 0.1, y + 0.2, z), (guard_x + 0.08, y + 0.2, z), 0.7, 16, P.KATANA_GUARD)
            geo.box_minmax(m, (pommel_x, y - 0.05, z - 0.2), (guard_x - 0.1, y + 0.42, z + 0.2), P.KATANA_HILT)
            for k in range(9):
                x = pommel_x + 0.3 + k * 0.42
                for zz, nz in ((z - 0.21, -1.0), (z + 0.21, 1.0)):
                    geo.oriented(m, [(x, y + 0.18, zz), (x + 0.16, y + 0.32, zz), (x + 0.32, y + 0.18, zz), (x + 0.16, y + 0.04, zz)], (0, 0, nz), P.RED)
            geo.box_minmax(m, (pommel_x - 0.25, y - 0.08, z - 0.24), (pommel_x, y + 0.45, z + 0.24), P.KATANA_GUARD)
            # the banner below, on two short cables, and the cables up to the vault
            top = DOME + VAULT_RISE - 0.3
            for x in (-6.5, 6.0):
                K.cable(m, (x, y + 0.5, z), (x, top, z), P.STEEL_DARK, 0.035)
                K.cable(m, (x, y - 0.1, z), (x, y - 0.55, z), P.STEEL_DARK, 0.025)
            bw = 13.0
            bh = bw * T.CELLS["katana_banner"][3] / T.CELLS["katana_banner"][2]
            (u0, v0), (u1, v1) = T.uv("katana_banner")
            xa, xb = (-6.5 - 0.3, 6.0 + 0.3)
            for zz, nz in ((z - 0.02, -1.0), (z + 0.02, 1.0)):
                # read from the south, u grows east; read from the north, u grows west
                uvs = [(u0, v0), (u1, v0), (u1, v1), (u0, v1)] if nz < 0 else [(u1, v0), (u0, v0), (u0, v1), (u1, v1)]
                geo.oriented(m, [(xa, y - 0.55 - bh, zz), (xb, y - 0.55 - bh, zz), (xb, y - 0.55, zz), (xa, y - 0.55, zz)],
                             (0, 0, nz), (1.0, 1.0, 1.0), K.LIT, uvs)
        self.b.part("landmark", "Katana", "Katana", build)
