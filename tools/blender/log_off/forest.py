"""The forest around the stump at fighter scale (about 32 units to the metre).

The fighters are a few centimetres tall, so the forest floor 24 units below the
rim is a landscape: leaf litter, twigs, pinecones and mushrooms near the stump's
foot; ferns and grass in the clearing; tree trunks as wide as buildings beyond,
a fallen log, canopy masses, a far forest wall, a sunset sky and fireflies.
Everything here is background: below the kill plane near the stump and outside
the blast corridor and camera envelope elsewhere.
"""
from __future__ import annotations

import math

from stagekit import geo
import palette as P
import shapes as S
import textures as T

WOOD = "LO_Wood"
FOLIAGE = "LO_Foliage"
GLOW = "LO_Glow"
SKY = "LO_Sky"
GROUND_Y = -24.0
SUN_AZ = 252.0     # compass degrees: 0 north (+Z), 90 east (+X)
SUN_EL = 7.0
SUN_WINDOW = (212.0, 292.0)  # keep near trunks and canopy out of the sunset view
SKY_R = 1800.0


def polar(r, az_deg):
    a = math.radians(az_deg)
    return (r * math.sin(a), r * math.cos(a))


def azimuth(x, z):
    return math.degrees(math.atan2(x, z)) % 360.0


def in_window(az, pad=0.0):
    return SUN_WINDOW[0] - pad <= az <= SUN_WINDOW[1] + pad


def _h(ix, iz, seed):
    n = (ix * 374761393 + iz * 668265263 + seed * 1442695041) & 0xFFFFFFFF
    n = ((n ^ (n >> 13)) * 1274126177) & 0xFFFFFFFF
    return ((n ^ (n >> 16)) & 0xFFFF) / 65535.0


def vnoise(x, z, seed=0):
    x0, z0 = math.floor(x), math.floor(z)
    tx, tz = x - x0, z - z0
    tx, tz = tx * tx * (3 - 2 * tx), tz * tz * (3 - 2 * tz)
    a = _h(x0, z0, seed) + (_h(x0 + 1, z0, seed) - _h(x0, z0, seed)) * tx
    b = _h(x0, z0 + 1, seed) + (_h(x0 + 1, z0 + 1, seed) - _h(x0, z0 + 1, seed)) * tx
    return a + (b - a) * tz


def smooth(e0, e1, x):
    t = max(0.0, min(1.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


def ground_y(x, z):
    r = math.hypot(x, z)
    rise = smooth(70.0, 700.0, r) * (14.0 + 12.0 * vnoise(x * 0.004, z * 0.004, 3))
    bumps = 1.4 * (vnoise(x * 0.03, z * 0.03, 5) - 0.5) * smooth(45.0, 90.0, r)
    near = 0.1 * (vnoise(x * 0.12, z * 0.12, 9) - 0.5)
    return GROUND_Y + rise + bumps + near


class Forest:
    def __init__(self, build):
        self.b = build
        self.rng = build.rng

    def build_all(self):
        b = self.b
        b.part("background", "Forest Floor", "Ground", self.ground)
        b.part("background", "Forest Floor", "Leaf Litter", self.litter)
        b.part("background", "Forest Floor", "Twigs And Cones", self.twigs_and_cones)
        b.part("background", "Forest Floor", "Mushrooms", self.mushrooms)
        b.part("background", "Forest Floor", "Moss", self.moss)
        b.part("background", "Undergrowth", "Ferns", self.ferns)
        b.part("background", "Undergrowth", "Grass", self.grass)
        b.part("background", "Trees", "Trunks", self.trunks)
        b.part("background", "Trees", "Fallen Log", self.fallen_log)
        b.part("background", "Canopy", "Canopy", self.canopy)
        b.part("background", "Far Forest", "Far Forest", self.far_forest)
        b.part("background", "Sky", "Sky", self.sky)
        b.part("background", "Fireflies", "Fireflies", self.fireflies)

    # ------------------------------------------------------------ floor
    def _floor_color(self, x, z):
        """Large, smooth patches only: fine grain comes from leaves and the detail map."""
        r = math.hypot(x, z)
        n1 = vnoise(x * 0.035, z * 0.035, 21)
        n2 = vnoise(x * 0.012, z * 0.012, 23)
        n3 = vnoise(x * 0.05, z * 0.05, 25)
        litter = geo.mix(P.LITTER[0], P.LITTER[1], n3)
        col = geo.mix(P.SOIL, litter, 0.3 + 0.4 * n1)
        col = geo.mix(col, P.NEEDLES, 0.2 * vnoise(x * 0.02, z * 0.02, 27))
        moss = smooth(0.55, 0.72, n2) * smooth(25.0, 120.0, r)
        col = geo.mix(col, geo.mix(P.MOSS_DARK, P.MOSS, n1), moss * 0.8)
        far = smooth(250.0, 900.0, r)
        return geo.mix(col, geo.mix(P.MOSS_DARK, P.SOIL_DARK, 0.5), far * 0.6)

    def ground(self, m):
        radii = [11.0]
        while radii[-1] < 1100.0:
            radii.append(radii[-1] + max(2.2, radii[-1] * 0.11))
        seg = 160
        rows = []
        for r in radii:
            row = []
            for k in range(seg):
                a = 2 * math.pi * k / seg
                x, z = r * math.sin(a), r * math.cos(a)
                row.append(((x, ground_y(x, z), z), self._floor_color(x, z)))
            rows.append(row)
        with geo.state(m, alpha=0.7):
            for i in range(len(radii) - 1):
                for k in range(seg):
                    k2 = (k + 1) % seg
                    q = [rows[i][k], rows[i][k2], rows[i + 1][k2], rows[i + 1][k]]
                    geo.oriented(m, [p for p, _ in q], (0, 1, 0), [c for _, c in q], WOOD)

    def litter(self, m):
        rng = self.rng
        with geo.state(m, alpha=0.35):
            for _ in range(700):
                r = 15.5 + rng.random() ** 1.7 * 150
                x, z = polar(r, rng.uniform(0, 360))
                length = rng.uniform(1.6, 3.4)
                col = geo.jitter(rng.choice(P.LITTER), rng, 0.08)
                y = ground_y(x, z) + 0.04
                S.leaf(m, (x, y, z), length, length * rng.uniform(0.38, 0.62), rng.uniform(0, 2 * math.pi), col,
                       curl=rng.uniform(0.08, 0.4), lobes=rng.choice([0, 0, 3, 5]), mat=WOOD, steps=6)

    def twigs_and_cones(self, m):
        rng = self.rng
        with geo.state(m, alpha=0.4):
            for _ in range(22):
                x, z = polar(rng.uniform(20, 150), rng.uniform(0, 360))
                yaw = rng.uniform(0, math.pi)
                length = rng.uniform(4, 15)
                radius = rng.uniform(0.18, 0.5)
                dx, dz = math.sin(yaw), math.cos(yaw)
                pts = []
                for k in range(5):
                    t = k / 4 - 0.5
                    px, pz = x + dx * length * t, z + dz * length * t + 0.4 * math.sin(k * 1.7)
                    pts.append((px, ground_y(px, pz) + radius * 0.8, pz))
                col = geo.jitter(P.TWIG, rng, 0.1)
                S.tube(m, pts, radius, 6, lambda i, k, n, c=col: geo.scale(c, 0.85 + 0.25 * max(0.0, n[1])), WOOD,
                       cap_start=True)
                if rng.random() < 0.6:
                    mid = pts[2]
                    fx, fz = mid[0] + dz * length * 0.25, mid[2] - dx * length * 0.25
                    S.tube(m, [mid, (fx, ground_y(fx, fz) + radius * 0.5, fz)], radius * 0.6, 5,
                           lambda i, k, n, c=col: c, WOOD)
            for _ in range(11):
                x, z = polar(rng.uniform(21, 95), rng.uniform(0, 360))
                yaw = rng.uniform(0, 2 * math.pi)
                dx, dz = math.sin(yaw), math.cos(yaw)
                L = rng.uniform(2.8, 3.6)
                base = ground_y(x, z) + 1.0
                pts = [(x + dx * L * (k / 6 - 0.5), base + 0.15 * math.sin(k), z + dz * L * (k / 6 - 0.5)) for k in range(7)]
                radii = [0.25, 0.85, 1.2, 1.25, 1.05, 0.7, 0.2]
                S.tube(m, pts, radii, 10,
                       lambda i, k, n: P.PINECONE_TIP if (i + k) % 2 else geo.scale(P.PINECONE, 0.9), WOOD,
                       cap_start=True)
            for _ in range(14):
                x, z = polar(rng.uniform(18, 60), rng.uniform(0, 360))
                y = ground_y(x, z)
                S.blob(m, (x, y + 0.42, z), (0.38, 0.45, 0.38), lambda n, p: P.ACORN, None, rings=4, segments=7, mat=WOOD)
                S.blob(m, (x, y + 0.75, z), (0.42, 0.18, 0.42), lambda n, p: P.ACORN_CAP, None, rings=3, segments=7, mat=WOOD)

    # ------------------------------------------------------------ fungi and moss
    def amanita(self, m, x, z, h, cap, rng):
        y = ground_y(x, z)
        S.lathe(m, (x, y - 0.2, z), [(0.55, 0.0), (0.42, h * 0.35), (0.36, h * 0.78), (0.62, h * 0.8), (0.36, h * 0.84),
                                     (0.34, h)], 10, lambda b, k: P.STEM, WOOD, cap_top=False)
        prof = [(0.34, h - 0.05), (cap * 0.98, h + 0.05), (cap, h + 0.32), (cap * 0.82, h + 0.95 * cap * 0.55),
                (cap * 0.5, h + 1.35 * cap * 0.55), (0.02, h + 1.5 * cap * 0.55)]
        S.lathe(m, (x, y, z), prof, 14,
                lambda b, k: P.STEM if b == 0 else geo.mix(P.AMANITA, P.AMANITA_DARK, 0.3 * ((k * 7) % 3) / 2),
                WOOD, cap_top=False)
        for _ in range(int(cap * 4)):
            b = rng.uniform(1.6, 4.4)
            a = rng.uniform(0, 2 * math.pi)
            i = int(b)
            t = b - i
            r = prof[i][0] + (prof[i + 1][0] - prof[i][0]) * t
            yy = prof[i][1] + (prof[i + 1][1] - prof[i][1]) * t
            s = cap * rng.uniform(0.09, 0.16)
            S.blob(m, (x + r * math.cos(a), y + yy, z + r * math.sin(a)), (s, s * 0.45, s), lambda n, p: P.SPOT, None,
                   rings=3, segments=6, mat=WOOD)

    def mushrooms(self, m):
        rng = self.rng
        with geo.state(m, alpha=0.2):
            for (x, z, h, cap) in ((-23.0, -11.0, 6.0, 2.4), (-26.5, -8.0, 3.8, 1.6), (-21.0, -15.5, 2.6, 1.2),
                                   (68.0, 32.0, 8.5, 3.4), (73.0, 27.0, 5.5, 2.2)):
                self.amanita(m, x, z, h, cap, rng)
            # Fairy bonnets on the east roots and by the fallen log.
            for cx, cz, count in ((21.0, 4.0, 7), (19.0, -9.0, 5), (-6.0, -21.0, 6), (102.0, -95.0, 9)):
                for _ in range(count):
                    x, z = cx + rng.uniform(-2.5, 2.5), cz + rng.uniform(-2.5, 2.5)
                    h = rng.uniform(1.6, 3.8)
                    y = ground_y(x, z)
                    if math.hypot(x, z) < 22.5:
                        y = max(y, -21.5)  # perched on a root
                    lean = rng.uniform(-0.3, 0.3)
                    top = (x + lean, y + h, z + lean * 0.5)
                    S.tube(m, [(x, y, z), S.lerp3((x, y, z), top, 0.5), top], 0.12, 5, lambda i, k, n: P.STEM, WOOD)
                    S.lathe(m, top, [(0.08, -0.1), (0.55, -0.05), (0.42, 0.35), (0.02, 0.75)], 9,
                            lambda b, k: geo.mix(P.BONNET, P.STEM, 0.4 if b == 0 else 0.0), WOOD, cap_top=False)
            # A big bolete further out (outside the camera envelope).
            x, z = 46.0, -60.0
            y = ground_y(x, z)
            S.lathe(m, (x, y - 0.3, z), [(1.9, 0.0), (2.3, 2.0), (1.9, 5.0), (1.5, 8.0)], 14,
                    lambda b, k: geo.mix(P.STEM, P.BOLETE_PORE, 0.25), WOOD, cap_top=False)
            S.lathe(m, (x, y, z), [(1.4, 7.6), (5.4, 7.8), (6.0, 8.8), (5.2, 10.6), (3.0, 11.8), (0.02, 12.2)], 16,
                    lambda b, k: P.BOLETE_PORE if b == 0 else geo.scale(P.BOLETE, 0.95 + 0.1 * (k % 2)), WOOD,
                    cap_top=False)

    def moss(self, m):
        rng = self.rng
        with geo.state(m, alpha=0.8):
            for _ in range(24):
                r = rng.uniform(22, 170)
                x, z = polar(r, rng.uniform(0, 360))
                size = rng.uniform(1.8, 5.0) * (1.0 if r < 45 else 1.5)
                y = ground_y(x, z)
                S.blob(m, (x, y - size * 0.05, z), (size, size * 0.5, size * rng.uniform(0.75, 1.1)),
                       lambda n, p: geo.mix(P.MOSS_DARK, P.MOSS_LIGHT, max(0.0, n[1]) ** 1.5 * 0.9),
                       rng, rings=6, segments=12, bump=0.05, floor=y - 0.2, mat=WOOD)

    # ------------------------------------------------------------ undergrowth
    def ferns(self, m):
        rng = self.rng
        clumps = [(92, 28), (112, 58), (96, 138), (124, 160), (88, 196), (134, 214), (104, 300), (118, 322),
                  (146, 12), (162, 96), (152, 184), (174, 124), (196, 40), (210, 340), (188, 230), (98, 266)]
        for r, az in clumps:
            cx, cz = polar(r, az)
            base_y = ground_y(cx, cz)
            for k in range(rng.randint(7, 10)):
                yaw = rng.uniform(0, 2 * math.pi)
                length = rng.uniform(18, 30) * (0.85 if r < 110 else 1.0)
                tone = rng.random()

                def col(t, tone=tone):
                    c = geo.mix(geo.mix(P.FERN_DARK, P.FERN, 0.4 + 0.6 * t), P.FERN_LIGHT, 0.35 * tone * t)
                    return tuple(c) + (0.15 + 0.85 * t,)

                S.frond(m, (cx + rng.uniform(-1, 1), base_y, cz + rng.uniform(-1, 1)), yaw, length,
                        rise=length * rng.uniform(0.55, 0.8), droop=length * rng.uniform(0.25, 0.5), rng=rng,
                        color_fn=col, mat=FOLIAGE)

    def grass(self, m):
        rng = self.rng
        for _ in range(46):
            r = rng.uniform(56, 230)
            cx, cz = polar(r, rng.uniform(0, 360))
            y = ground_y(cx, cz)
            for _ in range(rng.randint(12, 20)):
                x, z = cx + rng.uniform(-2.5, 2.5), cz + rng.uniform(-2.5, 2.5)
                h = rng.uniform(7, 16)
                S.blade(m, (x, y - 0.2, z), h, rng.uniform(0.5, 0.9), rng.uniform(0, 2 * math.pi), h * rng.uniform(0.1, 0.4),
                        tuple(geo.jitter(P.GRASS, rng, 0.08)) + (0.0,), tuple(P.GRASS_DRY) + (1.0,), FOLIAGE)

    # ------------------------------------------------------------ trees
    TRUNKS = [(185, 25, 15), (250, 48, 22), (205, 75, 14), (275, 103, 24), (215, 128, 16), (310, 146, 21),
              (190, 168, 12), (345, 178, 19), (240, 196, 15), (300, 302, 22), (205, 312, 16), (265, 326, 25),
              (185, 345, 13), (330, 2, 21), (390, 30, 27), (460, 80, 30), (500, 128, 29), (480, 310, 28),
              (420, 352, 25), (380, 160, 24), (430, 212, 22), (520, 248, 30)]

    def trunk(self, m, x, z, R, rng, top=340.0):
        y0 = ground_y(x, z)
        seed = rng.random() * 100
        prof = [(R * 1.75, -3.0), (R * 1.45, 1.0), (R * 1.18, 7.0), (R * 1.02, 16.0), (R * 0.97, 40.0),
                (R * 0.93, 80.0), (R * 0.89, 130.0), (R * 0.85, 190.0), (R * 0.8, 260.0), (R * 0.76, top - y0)]
        seg = 20

        def wobble(b, k):
            a = 2 * math.pi * k / seg
            fade = max(0.0, 1.0 - b / 3.0)
            return 1.0 + 0.28 * fade * max(0.0, math.cos(5 * a + seed)) ** 4 + 0.03 * math.sin(a * 7 + b)

        def color(b, k):
            stripe = (k * 5 + int(seed)) % 4
            col = [P.TRUNK_DARK, P.TRUNK, P.TRUNK_LIGHT, P.TRUNK][stripe]
            if b <= 2 and math.cos(2 * math.pi * k / seg) > -0.2 and vnoise(k * 0.7, b * 1.3, int(seed)) > 0.45:
                col = geo.mix(col, P.MOSS, 0.6)
            return col

        S.lathe(m, (x, y0, z), prof, seg, color, WOOD, cap_top=False, wobble=wobble)

    def trunks(self, m):
        rng = self.rng
        with geo.state(m, alpha=0.6):
            for r, az, R in self.TRUNKS:
                x, z = polar(r, az)
                self.trunk(m, x, z, R, rng)

    def fallen_log(self, m):
        rng = self.rng
        a = (64.0, 0.0, -128.0)
        b = (176.0, 0.0, -66.0)
        R = 11.0
        pts = []
        for k in range(9):
            t = k / 8
            x, z = a[0] + (b[0] - a[0]) * t, a[2] + (b[2] - a[2]) * t
            pts.append((x, ground_y(x, z) + R * 0.82, z))

        def color(i, k, n):
            if n[1] > 0.35 and vnoise(i * 0.9, k * 0.7, 61) > 0.32:
                return geo.mix(P.MOSS_DARK, P.MOSS_LIGHT, vnoise(i, k, 63))
            return geo.mix(P.BARK, P.BARK_DARK, 0.25 + 0.5 * ((k * 3 + i) % 4) / 3)

        with geo.state(m, alpha=0.6):
            S.tube(m, pts, R, 18, color, WOOD, cap_start=False, cap_end=False)
            # The sawn end shows its rings (atlas end grain); the far end is broken off.
            fr = S.frames(pts)
            t0, u0, w0 = fr[0]
            (gu0, gv0), (gu1, gv1) = T.region_uv("endgrain")
            ring, uvs = [], []
            for k in range(18):
                ang = 2 * math.pi * k / 18
                c, s = math.cos(ang), math.sin(ang)
                ring.append(tuple(pts[0][j] + (u0[j] * c + w0[j] * s) * R for j in range(3)))
                uvs.append((gu0 + (gu1 - gu0) * (0.5 + 0.47 * c), gv0 + (gv1 - gv0) * (0.5 + 0.47 * s)))
            centre = pts[0]
            cuv = ((gu0 + gu1) / 2, (gv0 + gv1) / 2)
            for k in range(18):
                k2 = (k + 1) % 18
                geo.oriented(m, [centre, ring[k], ring[k2]], S.mul(t0, -1), (1.0, 1.0, 1.0), WOOD, [cuv, uvs[k], uvs[k2]])
            t1, u1, w1 = fr[-1]
            jag = []
            for k in range(18):
                ang = 2 * math.pi * k / 18
                c, s = math.cos(ang), math.sin(ang)
                jag.append(tuple(pts[-1][j] + (u1[j] * c + w1[j] * s) * R + t1[j] * rng.uniform(-1.5, 3.5) for j in range(3)))
            S.fan(m, S.add(pts[-1], S.mul(t1, 1.0)), jag, P.SPLIT_WOOD, P.SPLIT_DARK, t1, WOOD)
        # Brackets and bonnets along its side.
        with geo.state(m, alpha=0.3):
            for t in (0.3, 0.38, 0.62):
                i = int(t * 8)
                p = pts[i]
                side = (-(b[2] - a[2]), 0.0, b[0] - a[0])
                L = math.hypot(side[0], side[2])
                nx, nz = side[0] / L, side[2] / L
                cx, cz = p[0] - nx * (R - 0.3), p[2] - nz * (R - 0.3)
                y = p[1] + rng.uniform(-2, 2)
                ring = []
                for k in range(9):
                    th = math.pi * k / 8
                    ring.append((cx - nx * math.sin(th) * 3.2 + nz * math.cos(th) * 3.6, y, cz - nz * math.sin(th) * 3.2 - nx * math.cos(th) * 3.6))
                S.fan(m, (cx, y, cz), ring, P.FUNGUS_BANDS[2], P.FUNGUS_BANDS[5], (0, 1, 0), WOOD, closed=False)
                S.fan(m, (cx, y - 2.0, cz), list(reversed(ring)), P.FUNGUS_PORE_DARK, P.FUNGUS_PORE, (-nx, -1.0, -nz), WOOD,
                      closed=False)

    def canopy(self, m):
        rng = self.rng
        with geo.state(m, alpha=0.08):
            for r, az, R in self.TRUNKS:
                x, z = polar(r, az)
                for _ in range(rng.randint(3, 5)):
                    ox, oz = rng.uniform(-3, 3) * R, rng.uniform(-3, 3) * R
                    size = rng.uniform(45, 85)
                    S.blob(m, (x + ox, rng.uniform(230, 340), z + oz), (size, size * 0.55, size * rng.uniform(0.8, 1.2)),
                           lambda n, p: geo.mix(P.CANOPY_DARK, P.CANOPY_LIGHT, max(0.0, n[1]) * 0.6 + 0.2 * vnoise(p[0] * 0.02, p[2] * 0.02, 71)),
                           rng, rings=5, segments=9, bump=0.12, mat=FOLIAGE)
            for _ in range(60):
                az = rng.uniform(0, 360)
                if in_window(az, -12):
                    continue
                r = rng.uniform(420, 1000)
                x, z = polar(r, az)
                size = rng.uniform(70, 150)
                S.blob(m, (x, rng.uniform(150, 380), z), (size, size * 0.5, size),
                       lambda n, p: geo.mix(P.CANOPY_DARK, P.CANOPY, max(0.0, n[1]) * 0.7),
                       rng, rings=4, segments=8, bump=0.1, mat=FOLIAGE)

    def far_forest(self, m):
        rng = self.rng
        with geo.state(m, alpha=0.0):
            for _ in range(110):
                az = rng.uniform(0, 360)
                r = rng.uniform(560, 1050)
                if in_window(az, -20) and rng.random() < 0.6:
                    continue
                x, z = polar(r, az)
                R = rng.uniform(9, 26)
                y0 = ground_y(x, z) - 4.0
                tone = geo.mix(P.TRUNK_DARK, P.TRUNK, rng.random() * 0.6)
                S.lathe(m, (x, y0, z), [(R * 1.4, 0.0), (R, 14.0), (R * 0.8, 460.0)], 8, lambda b, k, c=tone: c, WOOD,
                        cap_top=False)

    # ------------------------------------------------------------ sky
    SKY_RINGS = [(-12, P.SKY_HORIZON), (0, P.SKY_HORIZON), (3, P.SKY_LOW), (8, P.SKY_LOW), (16, P.SKY_MID),
                 (28, P.SKY_HIGH), (45, P.SKY_ZENITH), (70, P.SKY_ZENITH), (90, P.SKY_ZENITH)]

    def sky_color(self, el, az):
        """Sunset gradient at elevation `el` (degrees) and compass azimuth `az` (radians)."""
        rings = self.SKY_RINGS
        base = rings[-1][1]
        for (e0, c0), (e1, c1) in zip(rings, rings[1:]):
            if e0 <= el <= e1:
                base = geo.mix(c0, c1, (el - e0) / (e1 - e0))
                break
        else:
            if el < rings[0][0]:
                base = rings[0][1]
        sun = math.radians(SUN_AZ)
        toward = max(0.0, math.cos(az - sun))
        glow = toward ** 3 * math.exp(-max(0.0, el) / 14.0)
        away = max(0.0, -math.cos(az - sun)) * math.exp(-max(0.0, el) / 20.0)
        c = geo.mix(base, P.SKY_EAST_LOW, 0.55 * away)
        return geo.mix(c, P.SUN_HALO, 0.7 * glow)

    def sky(self, m):
        rings = self.SKY_RINGS
        seg = 64
        verts = []
        for el, _ in rings:
            row = []
            e = math.radians(el)
            for s in range(seg):
                az = 2 * math.pi * s / seg
                p = (SKY_R * math.cos(e) * math.sin(az), SKY_R * math.sin(e) - 30.0, SKY_R * math.cos(e) * math.cos(az))
                row.append((p, self.sky_color(el, az)))
            verts.append(row)
        with geo.state(m, alpha=0.0):
            for i in range(len(rings) - 1):
                for s in range(seg):
                    s2 = (s + 1) % seg
                    q = [verts[i][s], verts[i][s2], verts[i + 1][s2], verts[i + 1][s]]
                    pts = [p for p, _ in q]
                    c = [sum(p[k] for p in pts) / 4 for k in range(3)]
                    geo.oriented(m, pts, (-c[0], -c[1] - 30.0, -c[2]), [c_ for _, c_ in q], SKY)
        # The low sun: a soft halo fading into the sky colour, then a glowing disc.
        az, el = math.radians(SUN_AZ), math.radians(SUN_EL)
        for r_scale, radius, alpha in ((0.9, 320.0, None), (0.88, 46.0, 1.0)):
            r = SKY_R * r_scale
            c = (r * math.cos(el) * math.sin(az), r * math.sin(el) - 30.0, r * math.cos(el) * math.cos(az))
            n = geo._norm((-c[0], -c[1] - 30.0, -c[2]))
            u = geo._norm(geo._cross(n, (0, 1, 0)))
            v = geo._cross(u, n)
            ring = [tuple(c[i] + (u[i] * math.cos(a) + v[i] * math.sin(a)) * radius for i in range(3))
                    for a in [k * math.pi / 18 for k in range(36)]]
            if alpha is None:
                rim = []
                for p in ring:
                    pe = math.degrees(math.asin(max(-1.0, min(1.0, (p[1] + 30.0) / r))))
                    pa = math.atan2(p[0], p[2])
                    rim.append(tuple(self.sky_color(pe, pa)) + (0.0,))
                with geo.state(m, alpha=0.0):
                    S.fan(m, c, ring, tuple(P.SUN_HALO) + (0.3,), lambda i, rim=rim: rim[i], n, SKY)
            else:
                with geo.state(m, alpha=alpha):
                    S.fan(m, c, ring, P.SUN, P.SUN, n, SKY)

    # ------------------------------------------------------------ fireflies
    def fireflies(self, m):
        rng = self.rng
        for k in range(96):
            if k < 36:
                r, y = rng.uniform(19, 46), rng.uniform(-23.0, -13.5)
            else:
                r = rng.uniform(52, 230)
                y = None
            x, z = polar(r, rng.uniform(0, 360))
            if y is None:
                y = ground_y(x, z) + rng.uniform(1.5, 26.0)
            rate = rng.uniform(0.28, 0.6)
            loop = rng.uniform(1.5, 4.5)
            heading = rng.uniform(0, 2 * math.pi)
            dx, dz = math.sin(heading), math.cos(heading)
            rest = rng.uniform(-loop / 2, loop / 2)
            # Travel wraps while the light is off: see the StageNeon blink and travel encodings.
            phase = (0.25 + rest / loop) % 1.0
            s = rng.uniform(0.22, 0.34)
            with geo.state(m, alpha=1.0, anim=(rate, phase), travel=(loop * rate, rest), direction=(dx * loop, dz * loop)):
                tip = [(x, y + s * 1.3, z), (x, y - s * 1.3, z)]
                ring = [(x + s, y, z), (x, y, z + s), (x - s, y, z), (x, y, z - s)]
                for i in range(4):
                    a_, b_ = ring[i], ring[(i + 1) % 4]
                    geo.oriented(m, [tip[0], a_, b_], ((a_[0] + b_[0]) / 2 - x, 0.6, (a_[2] + b_[2]) / 2 - z), P.FIREFLY, GLOW)
                    geo.oriented(m, [tip[1], b_, a_], ((a_[0] + b_[0]) / 2 - x, -0.6, (a_[2] + b_[2]) / 2 - z), P.FIREFLY, GLOW)
