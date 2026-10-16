"""Sea, sky, clouds and gulls around the boat.

The sea is one polar grid centred on the boat, fine near the hull (where its vertex
alpha marks lapping foam for SLOPArena/StageWater) and coarse towards the horizon.
Vertex colour carries depth: cobalt in the bay, turquoise over the shelves near the
coast. It is visual-only water at WATER_Y, 0.6 m above the KO plane; the shader's
swell never lifts it more than a few centimetres. The sky dome, sun, drifting clouds
and gliding gulls use StageNeon; gull lanes keep 25 m clear of the stage box.
"""
from __future__ import annotations

import math
import random

import numpy as np

from stagekit import geo
from stagekit.noise import fbm, smooth
import bay as BAY
import layout as LAYOUT
import palette as P
import textures as T
import village as VILLAGE

WATER = "SD_Water"
SKY = "SD_Sky"
GLOW = "SD_Glow"
WOOD = "SD_Wood"
CLOTH = "SD_Cloth"
LAND = "SD_Land"
CENTER = (2.5, 0.0)
SKY_R = 1800.0
SEA_R = 1750.0
SEGMENTS = 180


def _hull_distance(x, z, outline):
    """Signed 2D distance to the hull outline: positive outside, negative inside."""
    return -T.inside_distance(outline, x, z)


class Sea:
    def __init__(self, build):
        self.b = build
        self.outline = build.data["outline"]
        self.rng = random.Random(build.seed + 300)

    def build_all(self):
        b = self.b
        b.part("water", "Sea", "Sea", self.water)
        b.part("background", "Sky", "Sky", self.sky)
        b.part("background", "Clouds", "Clouds", self.clouds)
        b.part("background", "Gulls", "Gulls", self.gulls)
        b.part("background", "Sailboats", "Sailboats", self.sailboats)
        b.part("background", "Far Islands", "Far Islands", self.islands)

    # ------------------------------------------------------------ water
    def rings(self):
        r = list(np.arange(9.0, 30.0, 0.5))
        x = 30.0
        while x < SEA_R:
            r.append(x)
            x *= 1.06
        r.append(SEA_R)
        return np.array(r)

    def water(self, m):
        radii = self.rings()
        ang = np.linspace(0.0, 2 * math.pi, SEGMENTS, endpoint=False)
        R, A = np.meshgrid(radii, ang, indexing="ij")
        X = CENTER[0] + R * np.cos(A)
        Z = CENTER[1] + R * np.sin(A)
        d_hull = _hull_distance(X, Z, self.outline)
        d_shore = BAY.shore_distance(X, Z)
        # Colour: bay cobalt, deeper offshore, turquoise shelves near the coast, slow slicks.
        base = np.ones(X.shape + (3,)) * np.array(P.SEA_BAY)
        far = smooth(220.0, 900.0, R)
        base = base * (1 - far[..., None]) + np.array(P.SEA_FAR) * far[..., None]
        shelf = smooth(-38.0, -4.0, d_shore)
        base = base * (1 - shelf[..., None] * 0.85) + np.array(P.SEA_SHALLOW) * (shelf[..., None] * 0.85)
        reef = smooth(-8.0, -1.0, d_shore) * smooth(0.45, 0.62, fbm(X / 14.0, Z / 14.0, 311, 3))
        base = base * (1 - reef[..., None] * 0.6) + np.array(P.SEA_REEF) * (reef[..., None] * 0.6)
        slick = fbm(X / 45.0, Z / 45.0, 312, 3)
        base *= (0.94 + 0.12 * slick)[..., None]
        deep = smooth(30.0, 6.0, d_hull) * 0.12  # the hull's shadowed water reads a touch deeper
        base *= (1 - deep)[..., None]
        # Foam (vertex alpha): lapping at the hull, broken surf along the shore.
        foam_hull = smooth(2.4, 0.0, d_hull) * (0.7 + 0.3 * fbm(X * 0.8, Z * 0.8, 313, 2))
        foam_shore = smooth(-7.0, -0.5, d_shore) * 0.8
        alpha = np.clip(np.maximum(foam_hull, foam_shore), 0.0, 1.0)
        idx = {}

        def vertex(i, k):
            if (i, k) not in idx:
                idx[(i, k)] = m.add((X[i, k], BAY.WATER_Y, Z[i, k]))
            return idx[(i, k)]

        for i in range(X.shape[0] - 1):
            for k in range(X.shape[1]):
                k2 = (k + 1) % X.shape[1]
                quad = [(i, k), (i + 1, k), (i + 1, k2), (i, k2)]
                if all(d_hull[a, c] < -0.6 for a, c in quad):
                    continue  # entirely under the boat
                if all(d_shore[a, c] > 3.0 for a, c in quad):
                    continue  # entirely under the land
                # Ring i is inside ring i+1; reversed, this order faces up (+Y) for CCW angles.
                order = list(reversed(quad))
                cols = [(float(base[a, c][0]), float(base[a, c][1]), float(base[a, c][2]), float(alpha[a, c])) for a, c in order]
                m.face([vertex(a, c) for a, c in order], cols, WATER)

    # ------------------------------------------------------------ sky
    SKY_RINGS = [(-14, P.HAZE), (-1, P.HAZE), (0, P.SKY_HORIZON), (3, P.SKY_LOW), (9, P.SKY_MID),
                 (20, P.SKY_HIGH), (38, P.SKY_ZENITH), (60, P.SKY_ZENITH), (90, P.SKY_ZENITH)]

    def sky_color(self, el, az):
        rings = self.SKY_RINGS
        for (e0, c0), (e1, c1) in zip(rings[:-1], rings[1:]):
            if e0 <= el <= e1:
                base = geo.mix(c0, c1, (el - e0) / (e1 - e0) if e1 > e0 else 0.0)
                break
        else:
            base = rings[-1][1]
        sun = BAY.sun_direction()
        e, a = math.radians(el), math.radians(az)
        v = (math.cos(e) * math.sin(a), math.sin(e), math.cos(e) * math.cos(a))
        cosang = sum(v[i] * sun[i] for i in range(3))
        glow = max(0.0, cosang) ** 6 * (1.0 - min(1.0, max(0.0, el) / 70.0))
        base = geo.mix(base, P.SKY_SUN_SIDE, 0.55 * glow)
        return base

    def sky(self, m):
        rings = [e for e, _ in self.SKY_RINGS]
        n_az = 72
        rows = []
        for el in rings:
            row = []
            e = math.radians(el)
            for k in range(n_az):
                az = 360.0 * k / n_az
                a = math.radians(az)
                p = (SKY_R * math.cos(e) * math.sin(a), SKY_R * math.sin(e) - 40.0, SKY_R * math.cos(e) * math.cos(a))
                row.append((p, self.sky_color(el, az)))
            rows.append(row)
        with geo.state(m, alpha=0.0):
            for j in range(len(rows) - 1):
                for k in range(n_az):
                    k2 = (k + 1) % n_az
                    q = [rows[j][k], rows[j][k2], rows[j + 1][k2], rows[j + 1][k]]
                    pts = [p for p, _ in q]
                    c = [p for p in pts]
                    centre = (sum(p[0] for p in c) / 4, sum(p[1] for p in c) / 4 + 40.0, sum(p[2] for p in c) / 4)
                    geo.oriented(m, pts, (-centre[0], -centre[1], -centre[2]), [col for _, col in q], SKY)
        # The sun: a glowing disc inside a soft halo on the light's bearing. Every halo vertex
        # takes the dome's own colour for its direction, so the halo fades into the sky.
        sun = BAY.sun_direction()
        r = SKY_R * 0.97
        fwd = (-sun[0], -sun[1], -sun[2])
        right = geo._norm(geo._cross((0.0, 1.0, 0.0), fwd))
        up = geo._cross(fwd, right)
        n = 40
        # (angular radius in degrees, blend towards the halo colour, glow amount)
        rings = [(1.7, None, 1.0), (2.6, 0.8, 0.3), (5.0, 0.35, 0.1), (10.0, 0.0, 0.0)]

        def vertex(deg, k):
            t = math.tan(math.radians(deg))
            a = 2 * math.pi * k / n
            d = [sun[i] + (right[i] * math.cos(a) + up[i] * math.sin(a)) * t for i in range(3)]
            length = math.sqrt(sum(x * x for x in d))
            d = [x / length for x in d]
            return tuple(d[i] * r for i in range(3)), d

        def sky_at(d):
            el = math.degrees(math.asin(max(-1.0, min(1.0, d[1]))))
            az = math.degrees(math.atan2(d[0], d[2])) % 360.0
            return self.sky_color(el, az)

        centre = (sun[0] * r, sun[1] * r - 40.0, sun[2] * r)
        ring_pts = []
        for deg, blend, glow in rings:
            row = []
            for k in range(n):
                pos, d = vertex(deg, k)
                pos = (pos[0], pos[1] - 40.0, pos[2])
                if blend is None:
                    col = geo.rgba(P.SUN, glow)
                else:
                    col = geo.rgba(geo.mix(sky_at(d), P.SUN_HALO, blend), glow)
                row.append((pos, col))
            ring_pts.append(row)
        for k in range(n):
            k2 = (k + 1) % n
            geo.oriented(m, [centre, ring_pts[0][k][0], ring_pts[0][k2][0]], fwd, geo.rgba(P.SUN, 1.0), SKY)
            for j in range(len(ring_pts) - 1):
                a, b2 = ring_pts[j], ring_pts[j + 1]
                geo.oriented(m, [a[k][0], a[k2][0], b2[k2][0], b2[k][0]], fwd, [a[k][1], a[k2][1], b2[k2][1], b2[k][1]], SKY)

    # ------------------------------------------------------------ clouds
    def clouds(self, m):
        """Fair-weather cumulus low on the horizon (the match camera rarely looks up), drifting
        slowly inland. Over the land they stay above the far hills; over the sea they sit lower."""
        rng = random.Random(self.b.seed + 301)
        loop = 2 * (SKY_R + 150.0)
        for i in range(12):
            az = math.radians(360.0 * (i + rng.uniform(0.15, 0.85)) / 12)
            dist = rng.uniform(950, 1550)
            cx, cz = dist * math.sin(az), dist * math.cos(az)
            over_sea = BAY.shore_distance(cx, cz) < 0
            cy = rng.uniform(150, 210) if over_sea else rng.uniform(230, 300)
            speed = rng.uniform(1.5, 3.0)
            # Westward drift: along-coordinate of the rest position is -cx.
            with geo.state(m, alpha=0.0, travel=(speed, -cx), direction=(-loop, 0.0)):
                for j in range(rng.randint(5, 8)):
                    ox, oz = rng.uniform(-130, 130), rng.uniform(-50, 50)
                    rx, ry, rz = rng.uniform(70, 140), rng.uniform(26, 48), rng.uniform(45, 85)

                    def shade(r, s, ry=ry):
                        t = r / 6.0
                        return geo.mix(P.CLOUD_SHADE, P.CLOUD, min(1.0, t * 1.3))
                    geo.ellipsoid(m, (cx + ox, cy + ry * 0.35, cz + oz), (rx, ry, rz), P.CLOUD, SKY,
                                  rings=6, segments=12, color_fn=shade)

    # ------------------------------------------------------------ gulls
    def gulls(self, m):
        """Gulls gliding on long looping lanes over the bay, clear of the stage box."""
        rng = random.Random(self.b.seed + 302)
        # (direction, sideways offset of the lane from the origin, height). Lanes nearer
        # than ENVELOPE to the stage box fly above its ceiling (topY + 30 = 51.5).
        lanes = [((1.0, 0.0), 64.0, 34.0), ((-1.0, 0.0), -58.0, 41.0), ((0.0, 1.0), 72.0, 37.0),
                 ((0.0, -1.0), -66.0, 45.0), ((0.8, 0.6), 0.0, 60.0), ((-0.6, 0.8), 0.0, 66.0)]
        loop = 900.0
        for (dx, dz), offset, y in lanes:
            for g in range(3):
                along0 = rng.uniform(-loop / 2, loop / 2)
                # Rest position: on the lane line (offset sideways from the origin), at along0.
                px, pz = -dz * offset, dx * offset
                x, z = px + dx * along0, pz + dz * along0
                yaw = math.atan2(dx, dz)
                speed = rng.uniform(7.0, 10.0)
                gy = y + rng.uniform(-3, 3)
                with geo.state(m, alpha=0.0, travel=(speed, along0), direction=(dx * loop, dz * loop)):
                    self._gull(m, (x, gy, z), yaw, rng.uniform(1.6, 2.0))  # stylised: readable at 60 m

    @staticmethod
    def _gull(m, pos, yaw, s):
        c, si = math.cos(yaw), math.sin(yaw)

        def P2(u, y, v):  # u right, v forward
            return (pos[0] + (u * c + v * si) * s, pos[1] + y * s, pos[2] + (-u * si + v * c) * s)

        body = [P2(0, 0.05, 0.45), P2(0.09, 0.0, 0.0), P2(0, 0.06, -0.38), P2(-0.09, 0.0, 0.0)]
        geo.oriented(m, body, (0, 1, 0), P.GULL, GLOW)
        for side in (1, -1):
            wing = [P2(side * 0.06, 0.03, 0.12), P2(side * 0.55, 0.16, 0.08), P2(side * 0.95, 0.06, -0.1),
                    P2(side * 0.5, 0.1, -0.16), P2(side * 0.06, 0.03, -0.1)]
            geo.oriented(m, wing, (0, 1, 0), P.GULL_WING, GLOW)
            geo.oriented(m, wing, (0, -1, 0), P.GULL, GLOW)
            tip = [P2(side * 0.78, 0.09, -0.02), P2(side * 0.95, 0.06, -0.1), P2(side * 0.8, 0.08, -0.12)]
            geo.oriented(m, tip, (0, 1, 0), P.GULL_TIP, GLOW)

    # ------------------------------------------------------------ the bay mouth
    def sailboats(self, m):
        """Yachts at anchor in the mouth and two more under sail farther out."""
        rng = random.Random(self.b.seed + 303)
        for (x, z, yaw, L) in ((205.0, 28.0, 70.0, 11.0), (262.0, -38.0, 95.0, 9.5), (330.0, 62.0, 50.0, 12.5),
                               (520.0, -150.0, 130.0, 13.0), (700.0, 120.0, 20.0, 14.0)):
            hull = rng.choice((P.CABIN_WHITE, P.BOAT_HULLS[0], P.CABIN_WHITE))
            VILLAGE.Village.small_boat(m, (x, BAY.WATER_Y, z), math.radians(yaw), L, L * 0.3, hull,
                                       rng.choice(P.BOAT_TRIM), rng, cabin=True, mast=L * 1.25)
            self._sails(m, (x, BAY.WATER_Y, z), math.radians(yaw), L, rng)

    @staticmethod
    def _sails(m, pos, yaw, L, rng):
        c, s = math.cos(yaw), math.sin(yaw)

        def P2(u, y, v):
            return (pos[0] + u * c + v * s, pos[1] + y, pos[2] - u * s + v * c)

        mast_v = 0.18 * L
        top = L * 1.25
        trim = rng.uniform(-0.25, 0.25)  # sails eased slightly off the centre line
        with geo.state(m, alpha=0.25):
            main = [P2(0.0, 1.6, mast_v), P2(0.0, top * 0.97, mast_v), P2(trim * L * 0.4, 1.8, mast_v - 0.42 * L)]
            geo.oriented(m, main, (c, 0.0, -s), P.SAIL, CLOTH)
            geo.oriented(m, main, (-c, 0.0, s), geo.scale(P.SAIL, 0.85), CLOTH)
            jib = [P2(0.0, top * 0.9, mast_v), P2(0.0, 1.2, 0.48 * L), P2(-trim * L * 0.3, 1.4, mast_v + 0.08 * L)]
            geo.oriented(m, jib, (c, 0.0, -s), P.SAIL, CLOTH)
            geo.oriented(m, jib, (-c, 0.0, s), geo.scale(P.SAIL, 0.85), CLOTH)

    def islands(self, m):
        """Low hazy islands on the eastern horizon (fog fades them into the sky)."""
        rng = random.Random(self.b.seed + 304)
        for (cx, cz, R, H) in ((1250.0, 160.0, 210.0, 55.0), (1020.0, -420.0, 110.0, 26.0), (1480.0, 430.0, 150.0, 38.0)):
            rings, segs = 7, 28
            seed = rng.randint(0, 9999)
            grid = []
            for i in range(rings + 1):
                t = i / rings
                row = []
                for k in range(segs):
                    a = 2 * math.pi * k / segs
                    wob = 1.0 + 0.22 * float(fbm(np.float64(math.cos(a) * 1.6 + seed), np.float64(math.sin(a) * 1.6), seed, 3) - 0.5) * 2
                    r = R * t * wob
                    h = H * max(0.0, 1.0 - t * t) ** 0.8 * (0.8 + 0.4 * float(fbm(np.float64(r * math.cos(a) / 60.0 + seed), np.float64(r * math.sin(a) / 60.0), seed + 1, 3)))
                    y = BAY.WATER_Y - 3.0 + h if i < rings else BAY.WATER_Y - 4.0
                    row.append(((cx + r * math.cos(a), y, cz + r * math.sin(a)), geo.mix(P.SCRUB, P.LIMESTONE, 0.6 * t)))
                grid.append(row)
            with geo.detail(m, 0.2):
                for i in range(rings):
                    for k in range(segs):
                        k2 = (k + 1) % segs
                        q = [grid[i][k], grid[i][k2], grid[i + 1][k2], grid[i + 1][k]]
                        pts = [p_ for p_, _ in q]
                        if len({tuple(round(v, 3) for v in p_) for p_ in pts}) < 3:
                            continue
                        geo.oriented(m, pts, (0, 1, 0), [c_ for _, c_ in q], LAND)
