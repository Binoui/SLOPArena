"""The coast around the bay: terrain, shore boulders, breakwater, lighthouse, trees,
the hilltop chapel and the beach cove.

Terrain is a polar grid round the bay centre with heights from bay.land_height;
faces wholly under the sea are skipped and the quay platform is left to village.py.
Vertex colours follow region, height and slope: limestone cliffs and scrub in the
north, ochre olive terraces and a chapel ridge in the south, dry village ground in
the west and hazy hills far away, with a pale surf line where land meets the water.
"""
from __future__ import annotations

import math
import random

import numpy as np

from stagekit import geo
from stagekit.noise import fbm, smooth
import bay as BAY
import palette as P

LAND = "SD_Land"
FOLIAGE = "SD_Foliage"
GLOW = "SD_Glow"
W = BAY.WATER_Y
CENTER = (-10.0, 0.0)
TERRAIN_R = (92.0, 1750.0)
SEGMENTS = 288


class Coast:
    def __init__(self, build):
        self.b = build
        self.rng = random.Random(build.seed + 400)
        self.ground_contacts = []

    def build_all(self):
        b = self.b
        b.part("background", "Hills", "Terrain", self.terrain)
        b.part("background", "Rocks", "Shore Rocks", self.rocks)
        b.part("background", "Rocks", "Outcrops", self.outcrops)
        b.part("background", "Breakwater", "Breakwater", self.breakwater)
        b.part("background", "Lighthouse", "Lighthouse", self.lighthouse)
        b.part("background", "Lighthouse Lamp", "Lighthouse Lamp", self.lamp)
        b.part("background", "Trees", "Olive Groves", self.olives)
        b.part("background", "Trees", "Pines", self.pines)
        b.part("background", "Trees", "Cypresses", self.cypresses)
        b.part("background", "Trees", "Maquis", self.maquis)
        b.part("background", "Chapel", "Chapel", self.chapel)
        b.part("background", "Beach", "Beach", self.beach)

    # ------------------------------------------------------------ terrain
    def terrain(self, m):
        radii = []
        r = TERRAIN_R[0]
        while r < TERRAIN_R[1]:
            radii.append(r)
            r *= 1.028 if r < 600 else 1.06
        radii.append(TERRAIN_R[1])
        ang = np.linspace(0.0, 2 * math.pi, SEGMENTS, endpoint=False)
        R, A = np.meshgrid(np.array(radii), ang, indexing="ij")
        X = CENTER[0] + R * np.cos(A)
        Z = CENTER[1] + R * np.sin(A)
        D = BAY.shore_distance(X, Z)
        H = BAY.land_height(X, Z, D)
        hh = H - W
        # Slope from neighbouring grid points.
        dr = np.gradient(H, axis=0) / np.maximum(np.gradient(R, axis=0), 1e-6)
        dt = np.gradient(H, axis=1) / np.maximum(R * (2 * math.pi / SEGMENTS), 1e-6)
        slope = np.sqrt(dr * dr + dt * dt)
        cols_ = self.terrain_colors(X, Z, D, hh, slope)
        quay = (X > BAY.QUAY_BACK - 1.0) & (X <= BAY.QUAY_X + 0.5) & (Z > BAY.QUAY_Z[0]) & (Z < BAY.QUAY_Z[1])
        idx = {}

        def vertex(i, k):
            if (i, k) not in idx:
                idx[(i, k)] = m.add((X[i, k], H[i, k], Z[i, k]))
            return idx[(i, k)]

        for i in range(X.shape[0] - 1):
            for k in range(X.shape[1]):
                k2 = (k + 1) % X.shape[1]
                quad = [(i, k), (i + 1, k), (i + 1, k2), (i, k2)]
                if all(hh[a, c] < -0.8 for a, c in quad):
                    continue
                if all(quay[a, c] for a, c in quad):
                    continue
                order = list(reversed(quad))
                cols = [(float(cols_[a, c][0]), float(cols_[a, c][1]), float(cols_[a, c][2]), 0.45) for a, c in order]
                m.face([vertex(a, c) for a, c in order], cols, LAND)

    def terrain_colors(self, X, Z, D, hh, slope):
        shape = X.shape + (3,)
        n1 = fbm(X / 30.0, Z / 30.0, 451, 3)
        n2 = fbm(X / 9.0, Z / 9.0, 452, 2)
        ang = np.degrees(np.arctan2(Z, X + 10.0)) % 360.0
        west = np.clip(1.0 - np.abs(ang - 180.0) / 50.0, 0.0, 1.0)
        south = np.clip(1.0 - np.abs(ang - 262.0) / 55.0, 0.0, 1.0)
        # Base ground: dark garrigue scrub with dry ochre patches; ochre earth on the olive terraces.
        col = np.ones(shape) * np.array(P.SCRUB)
        col = geo_mix(col, np.array(P.MAQUIS), smooth(0.45, 0.75, n1) * 0.7)
        col = geo_mix(col, np.array(P.SCRUB_DRY), smooth(0.5, 0.8, fbm(X / 80.0, Z / 80.0, 453, 3)) * 0.65)
        col = geo_mix(col, np.array(P.EARTH), south * smooth(0.3, 0.6, n1) * 0.55)
        # Paved lanes and terraces through the village.
        village = ((X < BAY.QUAY_X) & (X > -330.0) & (Z > -95.0) & (Z < 142.0)).astype(np.float64)
        col = geo_mix(col, np.array(P.STONE_QUAY_DARK) * (0.9 + 0.2 * n1)[..., None], village * 0.8)
        # Grey-ochre rock only where the ground is genuinely steep (low-frequency variation:
        # vertices are metres apart, so fine streaks would alias into smears).
        rock = smooth(0.85, 1.4, slope + 0.2 * (n1 - 0.5)) * (1 - village * 0.7)
        rock_col = geo_mix(np.ones(shape) * np.array(P.ROCK), np.array(P.LIMESTONE_DARK), smooth(0.35, 0.65, n1))
        col = geo_mix(col, rock_col, rock * 0.85)
        # Shore: dark wet rock, a pale surf line, sand in the cove.
        near = smooth(3.5, 0.6, hh)
        col = geo_mix(col, np.array(P.ROCK_DARK), near * 0.8)
        beach = smooth(26.0, 12.0, np.hypot(X - BAY.BEACH[0], Z - BAY.BEACH[1]))
        col = geo_mix(col, np.array(P.SAND), beach * smooth(6.0, 1.0, hh))
        surf = smooth(0.55, 0.15, np.abs(hh - 0.12))
        col = geo_mix(col, np.array(P.FOAM), surf * 0.7)
        wet = smooth(0.0, -0.5, hh)
        col = geo_mix(col, np.array(P.SAND_WET) * 0.75, wet)
        col *= (0.9 + 0.18 * n2)[..., None]
        return col

    # ------------------------------------------------------------ shore rocks
    def rocks(self, m):
        rng = random.Random(self.b.seed + 401)
        pts = BAY.WATER
        for (ax, az), (bx, bz) in zip(pts, pts[1:] + pts[:1]):
            L = math.hypot(bx - ax, bz - az)
            steps = int(L / 6.0)
            for s in range(steps):
                t = (s + rng.random()) / max(steps, 1)
                x, z = ax + (bx - ax) * t, az + (bz - az) * t
                if math.hypot(x, z) > 380:
                    continue
                if BAY.QUAY_BACK - 2 < x < BAY.QUAY_X + 4 and BAY.QUAY_Z[0] - 4 < z < BAY.QUAY_Z[1] + 4:
                    continue  # the quay wall
                if math.hypot(x - BAY.BEACH[0], z - BAY.BEACH[1]) < 24:
                    continue  # the sandy cove
                if BAY.mole_distance(x, z) < 18:
                    continue
                nx, nz = (bz - az) / L, -(bx - ax) / L  # outward (towards land) normal of the water polygon
                for _ in range(rng.randint(1, 2)):
                    off = rng.uniform(-2.5, 3.5)
                    cx, cz = x + nx * off + rng.uniform(-2, 2), z + nz * off + rng.uniform(-2, 2)
                    r = rng.uniform(1.4, 3.6)
                    cy = W + rng.uniform(-0.8, 0.6)
                    self.boulder(m, (cx, cy, cz), (r * rng.uniform(0.9, 1.4), r * rng.uniform(0.5, 0.8), r), rng)

    def outcrops(self, m):
        """Rock outcrops half-buried in the steep coastal slopes, so cliffs read as rock."""
        rng = random.Random(self.b.seed + 403)

        def accept(x, z, d, hh, r):
            if BAY.QUAY_X > x > -330 and -95 < z < 142:
                return False
            return 2.0 < hh < 60.0 and self._slope(x, z) > 0.75 and r.random() < 0.55

        for i, region in enumerate((((-140.0, 150.0), (340.0, 300.0)), ((-180.0, -330.0), (260.0, -120.0)),
                                    ((150.0, -320.0), (400.0, -70.0)))):
            for x, z in self._scatter(region, 9.0, accept, self.b.seed + 404 + i):
                y = BAY.height(x, z)
                r = rng.uniform(2.0, 4.5)
                self.boulder(m, (x, y - r * 0.35, z), (r * rng.uniform(1.0, 1.6), r * rng.uniform(0.5, 0.8), r), rng,
                             color=geo.mix(P.ROCK, P.LIMESTONE_DARK, rng.random()))

    @staticmethod
    def boulder(m, center, radii, rng, mat=LAND, color=None):
        rings, segs = 4, 8
        seed = rng.randint(0, 10_000)
        base = color or geo.mix(P.ROCK, P.LIMESTONE, rng.random() * 0.6)
        rows = []
        for r in range(rings + 1):
            phi = math.pi * r / rings - math.pi / 2
            row = []
            for s in range(segs):
                th = 2 * math.pi * s / segs
                k = 0.78 + 0.4 * float(fbm(np.float64(math.cos(th) * 1.7 + seed), np.float64(math.sin(phi) * 1.7), seed, 2))
                p = (center[0] + radii[0] * k * math.cos(phi) * math.cos(th),
                     center[1] + radii[1] * k * math.sin(phi),
                     center[2] + radii[2] * k * math.cos(phi) * math.sin(th))
                row.append(m.add(p))
            rows.append(row)
        with geo.detail(m, 0.6):
            for r in range(rings):
                shade = 0.7 + 0.3 * r / rings
                for s in range(segs):
                    s2 = (s + 1) % segs
                    m.face([rows[r][s], rows[r + 1][s], rows[r + 1][s2], rows[r][s2]], geo.scale(base, shade), mat)

    # ------------------------------------------------------------ breakwater
    MOLE_PROFILE = [(-15.0, -2.4), (-11.0, -0.3), (-7.0, 2.0), (-5.0, 3.0), (-4.5, 3.4), (4.5, 3.4),
                    (4.5, 4.7), (5.7, 4.7), (6.2, 3.1), (9.5, 0.6), (14.5, -2.4)]

    def _mole_frames(self, step=3.0):
        frames = []
        pts = BAY.MOLE
        for (ax, az), (bx, bz) in zip(pts[:-1], pts[1:]):
            L = math.hypot(bx - ax, bz - az)
            n = max(1, int(L / step))
            for k in range(n):
                t = k / n
                frames.append(((ax + (bx - ax) * t, az + (bz - az) * t), ((bx - ax) / L, (bz - az) / L)))
        (ax, az), (bx, bz) = pts[-2], pts[-1]
        L = math.hypot(bx - ax, bz - az)
        frames.append(((bx, bz), ((bx - ax) / L, (bz - az) / L)))
        return frames

    def breakwater(self, m):
        rng = random.Random(self.b.seed + 402)
        frames = self._mole_frames()
        rows = []
        for (cx, cz), (dx, dz) in frames:
            nx, nz = -dz, dx  # left of the direction: the open-sea side (north-east)
            row = []
            for j, (off, y) in enumerate(self.MOLE_PROFILE):
                jitter = 0.0 if 3 <= j <= 7 else rng.uniform(-0.35, 0.35)
                row.append((cx + nx * off, W + y + jitter, cz + nz * off))
            rows.append(row)
        prof_cols = [P.BREAKWATER_DARK, P.BREAKWATER, P.BREAKWATER, P.CONCRETE, P.CONCRETE, P.CONCRETE,
                     geo.scale(P.CONCRETE, 0.9), P.CONCRETE, P.BREAKWATER, P.BREAKWATER_DARK]
        with geo.detail(m, 0.55):
            for i in range(len(rows) - 1):
                (dx, dz) = frames[i][1]
                nx, nz = -dz, dx
                for j in range(len(self.MOLE_PROFILE) - 1):
                    q = [rows[i][j], rows[i + 1][j], rows[i + 1][j + 1], rows[i][j + 1]]
                    c = geo.jitter(prof_cols[j], rng, 0.06)
                    # The profile runs from the bay side to the sea side with the solid below it,
                    # so each segment's outward normal is its left-hand perpendicular.
                    (o0, y0), (o1, y1) = self.MOLE_PROFILE[j], self.MOLE_PROFILE[j + 1]
                    n_off, n_y = -(y1 - y0), (o1 - o0)
                    geo.oriented(m, q, (nx * n_off, n_y, nz * n_off), c, LAND)
        # Armour blocks tumbled on the inner (bay) slope.
        for (cx, cz), (dx, dz) in frames[::2]:
            nx, nz = -dz, dx
            for _ in range(2):
                off = -rng.uniform(6.5, 12.0)
                px, pz = cx + nx * off + dx * rng.uniform(-1, 1), cz + nz * off + dz * rng.uniform(-1, 1)
                py = W + 3.0 + (off + 6.5) * 0.5 + rng.uniform(-0.3, 0.3)
                s = rng.uniform(1.4, 2.2)
                geo.box(m, (px, py, pz), (s, s * 0.8, s), geo.jitter(P.CONCRETE, rng, 0.08), LAND, yaw=rng.uniform(0, math.pi),
                        side_color=geo.jitter(P.BREAKWATER, rng, 0.08), skip=("bottom",))
        # Bollards and a lamp post along the walk.
        for (cx, cz), (dx, dz) in frames[2::6]:
            nx, nz = -dz, dx
            geo.cylinder_between(m, (cx - nx * 3.8, W + 3.4, cz - nz * 3.8), (cx - nx * 3.8, W + 4.0, cz - nz * 3.8), 0.22, 8, P.BOLLARD, LAND)

    # ------------------------------------------------------------ lighthouse
    def lighthouse(self, m):
        x, z = BAY.LIGHTHOUSE
        base = BAY.MOLE_TOP
        with geo.detail(m, 0.35):
            geo.lathe(m, (x, W - 2.5, z), [(8.4, 0.0), (8.2, base - W + 2.3), (7.8, base - W + 2.5)], 20,
                      [P.BREAKWATER, P.CONCRETE], LAND)
            geo.lathe(m, (x, base, z), [(3.3, 0.0), (3.3, 1.3), (2.9, 1.5)], 16, [P.CONCRETE, P.CONCRETE], LAND)
            # Tower with red and white bands.
            prof = [(2.6, 1.5), (2.45, 4.6), (2.3, 7.7), (2.15, 10.8), (2.0, 13.9), (1.9, 16.0)]
            cols = [P.LIGHTHOUSE_WHITE, P.LIGHTHOUSE_RED, P.LIGHTHOUSE_WHITE, P.LIGHTHOUSE_RED, P.LIGHTHOUSE_WHITE]
            geo.lathe(m, (x, base, z), prof, 16, cols, LAND, cap_top=False)
            # Gallery deck, railing, lantern room and roof.
            geo.lathe(m, (x, base, z), [(1.9, 16.0), (2.9, 16.0), (2.9, 16.35), (1.8, 16.35)], 16, P.LANTERN_ROOF, LAND, cap_top=False)
            geo.lathe(m, (x, base, z), [(2.85, 16.35), (2.85, 17.3)], 16, geo.scale(P.LANTERN_ROOF, 1.2), LAND, cap_top=False)
            geo.lathe(m, (x, base, z), [(1.55, 16.35), (1.55, 18.6)], 12, P.LANTERN_GLASS, LAND, cap_top=False)
            geo.lathe(m, (x, base, z), [(1.8, 18.6), (1.2, 19.4), (0.5, 20.0), (0.12, 20.6)], 12, P.LIGHTHOUSE_RED, LAND)
            geo.cylinder_between(m, (x, base + 20.5, z), (x, base + 21.4, z), 0.06, 6, P.LANTERN_ROOF, LAND)
            # Door and two small windows facing the bay.
            for (h0, h1, w, col) in ((1.5, 3.6, 1.1, P.DOOR), (7.0, 8.2, 0.6, P.WINDOW_DARK), (12.0, 13.2, 0.6, P.WINDOW_DARK)):
                r = 2.62 - (h0 / 16.0) * 0.7
                ux, uz = -math.cos(math.radians(30)), -math.sin(math.radians(30))  # towards the bay
                px, pz = x + ux * (r + 0.02), z + uz * (r + 0.02)
                tx, tz = -uz, ux
                geo.facade_quad(m, (px - tx * w / 2, pz - tz * w / 2), (px + tx * w / 2, pz + tz * w / 2),
                                base + h0, base + h1, (ux, uz), col, LAND)

    def lamp(self, m):
        x, z = BAY.LIGHTHOUSE
        base = BAY.MOLE_TOP
        with geo.state(m, alpha=0.35):
            geo.lathe(m, (x, base, z), [(0.6, 16.9), (0.6, 17.9)], 10, P.LAMP, GLOW)

    # ------------------------------------------------------------ trees
    def _ground(self, x, z):
        return BAY.height(x, z)

    def _olive(self, m, x, z, s, rng):
        y = self._ground(x, z) - 0.3
        lean = rng.uniform(-0.4, 0.4)
        top = (x + lean, y + 2.3 * s, z + rng.uniform(-0.3, 0.3))
        with geo.state(m, alpha=0.0):
            geo.cylinder_between(m, (x, y, z), top, 0.28 * s, 5, P.OLIVE_TRUNK, FOLIAGE, radius_b=0.2 * s, caps=False)
        with geo.state(m, alpha=0.35):
            for k in range(2):
                ox, oz = rng.uniform(-0.9, 0.9) * s, rng.uniform(-0.9, 0.9) * s
                c = geo.jitter(P.OLIVE, rng, 0.08)
                geo.ellipsoid(m, (top[0] + ox, top[1] + 0.9 * s + k * 0.5 * s, top[2] + oz),
                              (2.0 * s, 1.35 * s, 2.0 * s), c, FOLIAGE, rings=3, segments=6,
                              color_fn=lambda r, si, c=c: geo.mix(P.OLIVE_DARK, c, r / 3.0))

    def _pine(self, m, x, z, s, rng):
        """Stone pine: a short leaning trunk under a broad, flat two-lobed crown."""
        y = self._ground(x, z) - 0.3
        lean = (rng.uniform(-1.2, 1.2), rng.uniform(-1.2, 1.2))
        h = rng.uniform(5.2, 7.6) * s
        top = (x + lean[0], y + h, z + lean[1])
        with geo.state(m, alpha=0.0):
            geo.cylinder_between(m, (x, y, z), top, 0.4 * s, 6, P.PINE_TRUNK, FOLIAGE, radius_b=0.25 * s, caps=False)
        with geo.state(m, alpha=0.3):
            c = geo.jitter(P.PINE, rng, 0.08)
            shade = lambda r, si, c=c: geo.mix(geo.scale(c, 0.6), P.PINE_LIGHT, r / 4.0)
            geo.ellipsoid(m, (top[0], top[1] + 0.9 * s, top[2]), (5.2 * s, 1.9 * s, 4.8 * s), c, FOLIAGE,
                          rings=4, segments=9, color_fn=shade)
            a = rng.uniform(0, 2 * math.pi)
            geo.ellipsoid(m, (top[0] + 2.6 * s * math.cos(a), top[1] + 0.3 * s, top[2] + 2.6 * s * math.sin(a)),
                          (3.4 * s, 1.4 * s, 3.2 * s), c, FOLIAGE, rings=3, segments=7, color_fn=shade)

    def _cypress(self, m, x, z, s, rng):
        y = self._ground(x, z) - 0.4
        h = rng.uniform(9.0, 13.0) * s
        c = geo.jitter(P.CYPRESS, rng, 0.06)
        with geo.state(m, alpha=0.2):
            geo.lathe(m, (x, y, z), [(0.25 * s, 0.0), (0.9 * s, 0.9), (1.05 * s, h * 0.3), (0.85 * s, h * 0.65),
                                     (0.4 * s, h * 0.9), (0.05 * s, h)], 7,
                      [P.PINE_TRUNK, c, c, geo.mix(c, P.CYPRESS_LIGHT, 0.5), P.CYPRESS_LIGHT], FOLIAGE, cap_top=False)

    def _scatter(self, region, spacing, accept, seed):
        rng = random.Random(seed)
        (x0, z0), (x1, z1) = region
        out = []
        x = x0
        while x < x1:
            z = z0
            while z < z1:
                px, pz = x + rng.uniform(-0.4, 0.4) * spacing, z + rng.uniform(-0.4, 0.4) * spacing
                d = float(BAY.shore_distance(np.float64(px), np.float64(pz)))
                if d > 0:
                    hh = BAY.height(px, pz) - W
                    if accept(px, pz, d, hh, rng):
                        out.append((px, pz))
                z += spacing
            x += spacing
        return out

    def _slope(self, x, z):
        e = 2.0
        hx = BAY.height(x + e, z) - BAY.height(x - e, z)
        hz = BAY.height(x, z + e) - BAY.height(x, z - e)
        return math.hypot(hx, hz) / (2 * e)

    def olives(self, m):
        rng = random.Random(self.b.seed + 410)

        def accept(x, z, d, hh, r):
            if not (6.0 < d and 3.0 < hh < 78.0):
                return False
            if math.hypot(x - BAY.CHAPEL[0], z - BAY.CHAPEL[1]) < 16 or math.hypot(x - BAY.BEACH[0], z - BAY.BEACH[1]) < 30:
                return False
            return self._slope(x, z) < 0.7 and r.random() < 0.7

        for x, z in self._scatter(((-170.0, -420.0), (230.0, -150.0)), 12.5, accept, self.b.seed + 411):
            self._olive(m, x, z, rng.uniform(0.85, 1.2), rng)
        # A few olives on the village's southern edge.
        for x, z in self._scatter(((-330.0, -170.0), (-140.0, -95.0)), 13.0, accept, self.b.seed + 412):
            self._olive(m, x, z, rng.uniform(0.85, 1.15), rng)

    def pines(self, m):
        rng = random.Random(self.b.seed + 420)

        def accept(x, z, d, hh, r):
            grove = float(fbm(np.float64(x / 70.0), np.float64(z / 70.0), 422, 2))  # pines grow in stands
            return (4.0 < d and 2.0 < hh < 70.0 and grove > 0.5 and r.random() < 0.6
                    and BAY.mole_distance(x, z) > 20 and self._slope(x, z) < 0.9)

        for region, spacing in ((((-110.0, 178.0), (330.0, 330.0)), 14.0), (((160.0, -330.0), (380.0, -95.0)), 15.0)):
            for x, z in self._scatter(region, spacing, accept, self.b.seed + 421 + int(region[0][0])):
                self._pine(m, x, z, rng.uniform(0.85, 1.2), rng)

    def cypresses(self, m):
        rng = random.Random(self.b.seed + 430)
        cx, cz = BAY.CHAPEL
        # An avenue up to the chapel and a few round it.
        for k in range(7):
            t = k / 6
            px = cx + 12.0 + 4.0 * math.sin(t * 2.0)
            pz = cz + 18.0 + t * 46.0
            for side in (-1, 1):
                self._cypress(m, px + side * 3.2, pz, rng.uniform(0.9, 1.1), rng)
        for a in (40, 120, 200, 300):
            self._cypress(m, cx + 13 * math.cos(math.radians(a)), cz + 11 * math.sin(math.radians(a)), rng.uniform(0.95, 1.2), rng)
        # Scattered on the western and northern skylines.
        def accept(x, z, d, hh, r):
            return hh > 25 and r.random() < 0.25 and self._slope(x, z) < 0.8
        for x, z in self._scatter(((-420.0, -260.0), (-240.0, 260.0)), 24.0, accept, self.b.seed + 431):
            self._cypress(m, x, z, rng.uniform(0.9, 1.25), rng)

    def maquis(self, m):
        """Low dark scrub (mastic, myrtle) breaking up the open slopes."""
        rng = random.Random(self.b.seed + 440)

        def accept(x, z, d, hh, r):
            if BAY.QUAY_X > x > -330 and -95 < z < 142:
                return False  # the village
            if math.hypot(x - BAY.CHAPEL[0], z - BAY.CHAPEL[1]) < 12 or BAY.mole_distance(x, z) < 18:
                return False
            return d > 3.0 and hh > 1.5 and r.random() < 0.24 and self._slope(x, z) < 1.1

        regions = [((-130.0, 176.0), (330.0, 330.0)), ((-170.0, -420.0), (230.0, -142.0)),
                   ((160.0, -330.0), (380.0, -90.0)), ((-460.0, -280.0), (-300.0, 280.0))]
        for i, region in enumerate(regions):
            for x, z in self._scatter(region, 12.0, accept, self.b.seed + 441 + i):
                y = self._ground(x, z)
                r = rng.uniform(1.0, 2.2)
                c = geo.jitter(rng.choice((P.MAQUIS_LIGHT, P.OLIVE_DARK, P.OLIVE)), rng, 0.08)
                with geo.state(m, alpha=0.15):
                    geo.ellipsoid(m, (x, y - r * 0.1, z), (r * 1.3, r * 0.75, r), c, FOLIAGE, rings=3, segments=6,
                                  color_fn=lambda rr, si, c=c: geo.mix(geo.scale(c, 0.65), c, rr / 3.0))

    # ------------------------------------------------------------ chapel
    def chapel(self, m):
        cx, cz = BAY.CHAPEL
        y = min(BAY.height(cx + dx, cz + dz) for dx in (-6, 6) for dz in (-5, 5)) - 0.6
        top = y + 6.6
        with geo.detail(m, 0.25):
            geo.box_minmax(m, (cx - 6.0, y, cz - 4.5), (cx + 6.0, top, cz + 4.5), P.CHURCH, LAND, skip=("bottom",))
            # Drum and dome over the crossing.
            geo.lathe(m, (cx, top, cz), [(3.0, 0.0), (3.0, 1.4)], 16, P.CHURCH, LAND, cap_top=False)
            prof = [(3.15, 1.4)] + [(3.15 * math.cos(math.radians(a)), 1.4 + 3.0 * math.sin(math.radians(a))) for a in (25, 50, 70, 85)] + [(0.2, 4.5)]
            geo.lathe(m, (cx, top, cz), prof, 16, P.CHURCH_DOME, LAND)
            geo.cylinder_between(m, (cx, top + 4.4, cz), (cx, top + 5.6, cz), 0.07, 6, P.LANTERN_ROOF, LAND)
            # Bell gable on the bay-facing (north) front, with door and round window.
            geo.box_minmax(m, (cx - 1.6, top - 0.2, cz + 3.5), (cx + 1.6, top + 3.0, cz + 4.5), P.CHURCH, LAND, skip=("bottom",))
            geo.facade_quad(m, (cx - 0.5, cz + 4.52), (cx + 0.5, cz + 4.52), top + 0.9, top + 2.2, (0.0, 1.0), P.WINDOW_DARK, LAND)
            geo.box(m, (cx, top + 1.4, cz + 4.0), (0.5, 0.6, 0.5), P.BELL, LAND)
            geo.facade_quad(m, (cx - 1.0, cz + 4.52), (cx + 1.0, cz + 4.52), y + 0.6, y + 3.4, (0.0, 1.0), P.DOOR, LAND)
            geo.facade_quad(m, (cx - 0.6, cz + 4.52), (cx + 0.6, cz + 4.52), y + 4.2, y + 5.4, (0.0, 1.0), P.WINDOW_DARK, LAND)
        self.ground_contacts.append({"name": "chapel", "base": round(y, 2)})

    # ------------------------------------------------------------ beach
    def beach(self, m):
        rng = random.Random(self.b.seed + 440)
        bx, bz = BAY.BEACH
        spots = []
        for k in range(30):
            x, z = bx + rng.uniform(-18, 18), bz + rng.uniform(-14, 6)
            hh = BAY.height(x, z) - W
            if 0.6 < hh < 2.4 and all(math.hypot(x - a, z - b2) > 4.5 for a, b2 in spots):
                spots.append((x, z))
            if len(spots) >= 7:
                break
        for i, (x, z) in enumerate(spots):
            y = BAY.height(x, z)
            pole_top = y + 2.3
            geo.cylinder_between(m, (x, y - 0.3, z), (x, pole_top, z), 0.05, 5, P.CABIN_WHITE, LAND, caps=False)
            colors = [P.AWNING_RED, P.CABIN_WHITE] if i % 2 else [P.SHEER_YELLOW, P.HULL_BLUE]
            for seg in range(8):
                a0, a1 = 2 * math.pi * seg / 8, 2 * math.pi * (seg + 1) / 8
                p0 = (x + 1.5 * math.cos(a0), pole_top - 0.45, z + 1.5 * math.sin(a0))
                p1 = (x + 1.5 * math.cos(a1), pole_top - 0.45, z + 1.5 * math.sin(a1))
                geo.oriented(m, [(x, pole_top, z), p0, p1], (0, 1, 0), colors[seg % 2], LAND)
                geo.oriented(m, [(x, pole_top - 0.02, z), p0, p1], (0, -1, 0), geo.scale(colors[seg % 2], 0.7), LAND)
            # Towel beside the parasol.
            tx, tz = x + 1.8, z - 0.5
            ty = BAY.height(tx, tz) + 0.03
            geo.oriented(m, [(tx - 0.5, ty, tz - 0.9), (tx + 0.5, ty, tz - 0.9), (tx + 0.5, ty, tz + 0.9), (tx - 0.5, ty, tz + 0.9)],
                         (0, 1, 0), P.LAUNDRY[i % len(P.LAUNDRY)], LAND)


def geo_mix(a, b, t):
    t = np.clip(t, 0.0, 1.0)
    if np.ndim(t) == 0:
        return a * (1 - t) + b * t
    return a * (1 - t)[..., None] + b * t[..., None]
