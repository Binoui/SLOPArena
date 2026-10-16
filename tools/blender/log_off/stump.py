"""The Log Off chopping stump: fight top, bark, axe split, fungus shelves, foot and roots.

The playable parts follow the authoritative shell exactly: the textured top is the
collision outline at y = 0, bark furrows are recessed into the collision walls
(never proud of them), the split walls are the collision split, and each fungus
shelf's visible top, lip and underside stay inside its collision solid. Everything
that bulges outward (the flared foot, roots and small fungi) sits below the kill
plane, under the camera-envelope ceiling.
"""
from __future__ import annotations

import math

from stagekit import geo
import layout as LAYOUT
import palette as P
import shapes as S
import textures as T

WOOD = "LO_Wood"
STUMP = "LO_Stump"
GROUND_Y = -24.0
FOOT_TOP = -12.0
FLARE = 3.4
WALL_PROUD = 0.025
SHELF_INSET = 0.04


def _hash(*values):
    h = 2166136261
    for v in values:
        h = ((h ^ (int(v * 1000) & 0xFFFFFFFF)) * 16777619) & 0xFFFFFFFF
    return (h & 0xFFFF) / 65535.0


def subdivide(points, per_segment):
    """Columns along an open outline chain: [(x, z), outward normal (nx, nz), segment index]."""
    out = []
    n = len(points)
    normals = []
    for i in range(n - 1):
        (ax, az), (bx, bz) = points[i], points[i + 1]
        ex, ez = bx - ax, bz - az
        L = math.hypot(ex, ez)
        normals.append((ez / L, -ex / L))
    for i in range(n - 1):
        k = per_segment if isinstance(per_segment, int) else max(1, int(round(math.dist(points[i], points[i + 1]) / per_segment)))
        for j in range(k):
            t = j / k
            p = (points[i][0] + (points[i + 1][0] - points[i][0]) * t, points[i][1] + (points[i + 1][1] - points[i][1]) * t)
            if j == 0 and i > 0:
                a, b = normals[i - 1], normals[i]
                nx, nz = a[0] + b[0], a[1] + b[1]
                L = math.hypot(nx, nz)
                nrm = (nx / L, nz / L)
            else:
                nrm = normals[i]
            out.append((p, nrm, i))
    out.append((points[-1], normals[-1], n - 2))
    return out


class Stump:
    def __init__(self, build):
        self.b = build
        self.rng = build.rng
        d = build.data
        self.spec = d["spec"]
        self.outline = d["outline"]
        self.top = d["top"]
        self.kill = build.C["killY"]
        self.rim_count = self.spec["rim_segments"] + 1
        self.rim = self.outline[:self.rim_count]
        self.chain = [self.outline[self.rim_count - 1]] + self.outline[self.rim_count:] + [self.outline[0]]
        self.columns = subdivide(self.rim, 3)
        self.furrow, self.plate = self._plates()
        self.solids = LAYOUT.collision(self.spec)

    def build_all(self):
        b = self.b
        b.part("top", "Stump", "Stump Top", self.top_surface)
        b.part("wall", "Stump", "Bark", self.bark)
        b.part("wall", "Stump", "Axe Split", self.split_walls)
        for name, shelf in b.data["shelves"].items():
            b.part("shelf", "Stump", name, lambda m, s=shelf, n=name: self.shelf(m, n, s))
        b.part("below", "Stump", "Stump Foot", self.foot)
        b.part("below", "Stump", "Split Floor", self.split_floor)
        b.part("below", "Stump", "Roots", self.roots)
        b.part("below", "Stump", "Foot Fungi", self.foot_fungi)

    # ------------------------------------------------------------ fight top
    def top_surface(self, m):
        pts = self.outline
        with geo.state(m, alpha=0.3):
            for i, j, k in LAYOUT.ear_clip(pts):
                tri = [pts[i], pts[j], pts[k]]
                geo.oriented(m, [(x, self.top, z) for x, z in tri], (0, 1, 0), (1.0, 1.0, 1.0), STUMP,
                             [T.top_uv(x, z) for x, z in tri])

    # ------------------------------------------------------------ bark
    def _plates(self):
        """Irregular bark plates (2-5 columns wide) separated by V furrows, plus a few
        shallow sub-furrows. Depths are recesses into the collision wall."""
        n = len(self.columns)
        rng = __import__("random").Random(911)
        depth, plate = [0.0] * n, [0] * n
        i, p = 1, 0
        while i < n - 1:
            width = rng.choice((2, 3, 3, 4, 5))
            for k in range(width):
                if i + k < n - 1:
                    plate[i + k] = p
                    if 0 < k < width - 1 and rng.random() < 0.18:
                        depth[i + k] = rng.uniform(0.05, 0.1)
            i += width
            if i < n - 1:
                depth[i] = rng.uniform(0.17, 0.3)
                plate[i] = -1
                i += 1
            p += 1
        return depth, plate

    def _bark_color(self, i, y, depth, nrm):
        h = _hash(i, round(y, 2))
        p = self.plate[i] if 0 <= i < len(self.plate) else -1
        if depth > 0.15 or p < 0:
            col = geo.mix(P.BARK_DARK, P.BARK, 0.2 * h)
        else:
            tone = _hash(p, 17)
            col = geo.mix(P.BARK, P.BARK_LIGHT, 0.15 + 0.6 * tone)
            col = geo.mix(col, (0.52, 0.48, 0.44), 0.25 * _hash(p, 23))  # weathered grey plates
            band = math.floor((y + 40.0) / (1.6 + 2.4 * _hash(p, 29)))
            if _hash(p, band, 31) > 0.82:
                col = geo.scale(col, 0.62)  # horizontal break across a plate
            if _hash(p, round(y / 1.5), 37) > 0.72 and y > -7.0:
                col = geo.mix(col, P.LICHEN, 0.6)
            if depth > 0.03:
                col = geo.scale(col, 0.8)
        # Moss creeps up the shady north side and the base.
        north = max(0.0, nrm[1])
        mossy = max(0.0, min(1.0, (-y - 5.0) / 7.0)) * (0.3 + 0.7 * north)
        if _hash(i // 3, round(y / 1.3), 41) < mossy * 0.85:
            col = geo.mix(col, P.MOSS_DARK if h < 0.5 else P.MOSS, 0.75)
        return col

    def _ramp(self, y):
        t = max(0.0, min(1.0, -y / 0.45))
        return t * t * (3 - 2 * t)

    def bark(self, m):
        rows = [0.0, -0.15, -0.45, -0.9, -1.6, -2.5, -3.6, -4.9, -6.3, -7.8, -9.2, -10.0, -11.0, -12.0]
        grid = []
        for i, ((x, z), (nx, nz), _) in enumerate(self.columns):
            col = []
            for y in rows:
                d = self.furrow[i] * self._ramp(y)
                col.append(((x - nx * d, y, z - nz * d), self._bark_color(i, y, d, (nx, nz))))
            grid.append(col)
        with geo.state(m, alpha=0.65):
            for i in range(len(grid) - 1):
                (_, (n0x, n0z), _), (_, (n1x, n1z), _) = self.columns[i], self.columns[i + 1]
                out = (n0x + n1x, 0.0, n0z + n1z)
                for j in range(len(rows) - 1):
                    q = [grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1]]
                    geo.oriented(m, [p for p, _ in q], out, [c for _, c in q], WOOD)

    # ------------------------------------------------------------ axe split
    def split_walls(self, m):
        cols = subdivide(self.chain, 0.6)
        rows = [0.0, -0.3, -0.9, -1.8, -3.0, -4.5, -6.2, -8.0, -10.0, -12.0, -15.0, -18.0, -21.0, GROUND_Y - 0.5]
        with geo.state(m, alpha=0.5):
            for i in range(len(cols) - 1):
                (a, na, _), (b, nb, _) = cols[i], cols[i + 1]
                out = (na[0] + nb[0], 0.0, na[1] + nb[1])
                for j in range(len(rows) - 1):
                    y0, y1 = rows[j], rows[j + 1]
                    def c(k, y):
                        stripe = _hash(i + k, 7)
                        base = geo.mix(P.SPLIT_WOOD, P.SPLIT_DARK, 0.15 + 0.45 * stripe)
                        depth = max(0.25, 1.0 - 0.032 * -y)
                        return geo.scale(base, depth)
                    geo.oriented(m, [(a[0], y0, a[1]), (b[0], y0, b[1]), (b[0], y1, b[1]), (a[0], y1, a[1])], out,
                                 [c(0, y0), c(1, y0), c(1, y1), c(0, y1)], WOOD)

    def split_floor(self, m):
        poly = list(reversed(self.chain))  # the chain runs clockwise; the floor polygon must be CCW
        with geo.state(m, alpha=0.7):
            for i, j, k in LAYOUT.ear_clip(poly):
                tri = [poly[i], poly[j], poly[k]]
                cols = [geo.mix(P.SOIL, P.LITTER[int(_hash(x, z) * 5)], 0.4) for x, z in tri]
                geo.oriented(m, [(x, GROUND_Y + 0.05, z) for x, z in tri], (0, 1, 0), cols, WOOD)

    # ------------------------------------------------------------ fungus shelves
    def _shelf_solid(self, name):
        verts, tris = self.solids[name]
        bottoms = []
        for a, b, c in tris:
            pa, pb, pc = verts[a], verts[b], verts[c]
            n = geo._cross(S.sub(pb, pa), S.sub(pc, pa))
            if n[1] < -1e-9:
                bottoms.append((pa, pb, pc))
        return bottoms

    @staticmethod
    def surface_y(tris, x, z):
        for a, b, c in tris:
            d = (b[2] - c[2]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[2] - c[2])
            if abs(d) < 1e-12:
                continue
            u = ((b[2] - c[2]) * (x - c[0]) + (c[0] - b[0]) * (z - c[2])) / d
            v = ((c[2] - a[2]) * (x - c[0]) + (a[0] - c[0]) * (z - c[2])) / d
            w = 1 - u - v
            if u >= -1e-6 and v >= -1e-6 and w >= -1e-6:
                return u * a[1] + v * b[1] + w * c[1]
        return None

    def shelf(self, m, name, shelf):
        """Bracket fungus: banded flat top, chamfered lip and pored underside, all inside
        the shelf's collision solid (the top is the walkable surface itself)."""
        s, top, n = shelf["side"], shelf["top"], shelf["segments"]
        L, D, pw = shelf["half_length"], shelf["depth"], shelf["power"]
        bottoms = self._shelf_solid(name)
        outer, inner, depth = [], [], []
        for i in range(n + 1):
            u = -1.0 + 2.0 * i / n
            z = L * u
            d = D * (1.0 - abs(u) ** pw) ** (1.0 / pw)
            bx = LAYOUT.bark_x(z, s, self.spec)
            outer.append((bx + s * d, z))
            inner.append((bx - s * 0.6, z))
            depth.append(d)
        # Three columns per collision segment, linear along its chords.
        cols = []
        for i in range(n):
            for k in range(3):
                t = k / 3
                o = (outer[i][0] + (outer[i + 1][0] - outer[i][0]) * t, outer[i][1] + (outer[i + 1][1] - outer[i][1]) * t)
                q = (inner[i][0] + (inner[i + 1][0] - inner[i][0]) * t, inner[i][1] + (inner[i + 1][1] - inner[i][1]) * t)
                cols.append((o, q, depth[i] + (depth[i + 1] - depth[i]) * t))
        cols.append((outer[n], inner[n], depth[n]))
        rows = [0.0, 0.2] + [0.2 + 0.8 * (k / 14) ** 0.8 for k in range(1, 14)] + [0.975]
        rows = sorted(set(round(r, 4) for r in rows if r <= 0.975))
        zones = (0.0, 0.16, 0.3, 0.42, 0.55, 0.67, 0.79, 0.9)

        def at(c, t):
            o, i_, _ = cols[c]
            return (i_[0] + (o[0] - i_[0]) * t, i_[1] + (o[1] - i_[1]) * t)

        def band_color(c, t):
            d = cols[c][2]
            q = 1.0 if d < 0.05 else max(0.0, (t * (0.6 + d) - 0.6) / d)
            q += 0.03 * math.sin(c * 1.9 + 2.0 * q)  # slightly wavy zones
            k = sum(1 for zq in zones if q >= zq) - 1
            base = P.FUNGUS_BANDS[max(0, min(len(P.FUNGUS_BANDS) - 1, k))]
            return geo.scale(base, 0.94 + 0.1 * _hash(c, k, 13))

        def bottom(x, z, q=1.0):
            """Underside inside the collision solid, bulging up slightly mid-way (a concave hoof)."""
            y = self.surface_y(bottoms, x, z)
            return (top - shelf["root"] if y is None else y) + SHELF_INSET + 0.3 * math.sin(math.pi * q) ** 0.8

        lip_top = top - 0.12
        with geo.state(m, alpha=0.25):
            for c in range(len(cols) - 1):
                for j in range(len(rows) - 1):
                    q = [at(c, rows[j]), at(c + 1, rows[j]), at(c + 1, rows[j + 1]), at(c, rows[j + 1])]
                    colors = [band_color(c, rows[j]), band_color(c + 1, rows[j]), band_color(c + 1, rows[j + 1]),
                              band_color(c, rows[j + 1])]
                    geo.oriented(m, [(x, top, z) for x, z in q], (0, 1, 0), colors, WOOD)
                a, b = at(c, rows[-1]), at(c + 1, rows[-1])
                oa, ob = at(c, 1.0), at(c + 1, 1.0)
                pale = P.FUNGUS_BANDS[-1]
                geo.oriented(m, [(a[0], top, a[1]), (b[0], top, b[1]), (ob[0], lip_top, ob[1]), (oa[0], lip_top, oa[1])],
                             (s * 0.7, 0.7, 0.0), pale, WOOD)
                ya, yb = bottom(*oa), bottom(*ob)
                if max(lip_top - ya, lip_top - yb) > 0.02:
                    geo.oriented(m, [(oa[0], lip_top, oa[1]), (ob[0], lip_top, ob[1]), (ob[0], yb, ob[1]), (oa[0], ya, oa[1])],
                                 (float(s), 0.0, 0.0), geo.scale(pale, 0.82), WOOD)
            under = [0.0, 0.3, 0.55, 0.75, 0.88, 0.96, 1.0]
            for c in range(len(cols) - 1):
                for j in range(len(under) - 1):
                    q = [at(c, under[j]), at(c + 1, under[j]), at(c + 1, under[j + 1]), at(c, under[j + 1])]
                    ts = (under[j], under[j], under[j + 1], under[j + 1])
                    pts = [(x, bottom(x, z, t), z) for (x, z), t in zip(q, ts)]
                    tints = [geo.scale(geo.mix(P.FUNGUS_PORE_DARK, P.FUNGUS_PORE, t ** 0.7), 0.82) for t in ts]
                    geo.oriented(m, pts, (s * 0.4, -1.0, 0.0), tints, WOOD)

    # ------------------------------------------------------------ below the kill plane
    def _flare(self, y):
        t = max(0.0, (FOOT_TOP - y) / (FOOT_TOP - GROUND_Y))
        return FLARE * t * t

    def foot(self, m):
        rows = [FOOT_TOP, -13.5, -15.0, -16.5, -18.0, -19.5, -21.0, -22.5, GROUND_Y, GROUND_Y - 1.5]
        grid = []
        for i, ((x, z), (nx, nz), _) in enumerate(self.columns):
            ang = math.atan2(z, x)
            buttress = 0.5 + 0.5 * math.cos(8 * ang + 0.9) ** 3
            col = []
            for y in rows:
                f = self._flare(y) * (0.65 + 0.7 * buttress)
                if i == 0 or i == len(self.columns) - 1:
                    f *= 0.6
                d = self.furrow[i]
                p = (x + nx * (f - d), y, z + nz * (f - d))
                col.append((p, self._bark_color(i, y, d, (nx, nz))))
            grid.append(col)
        with geo.state(m, alpha=0.65):
            for i in range(len(grid) - 1):
                (_, (n0x, n0z), _), (_, (n1x, n1z), _) = self.columns[i], self.columns[i + 1]
                out = (n0x + n1x, 0.0, n0z + n1z)
                for j in range(len(rows) - 1):
                    q = [grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1]]
                    geo.oriented(m, [p for p, _ in q], out, [c for _, c in q], WOOD)
            # Close the flared bark against the straight split walls at both lips.
            for lip, wall_normal in ((0, None), (len(grid) - 1, None)):
                (lx, lz), _, _ = self.columns[lip]
                for j in range(len(rows) - 1):
                    a = (lx, rows[j], lz)
                    b = (lx, rows[j + 1], lz)
                    c = grid[lip][j + 1][0]
                    d = grid[lip][j][0]
                    if math.dist(a, d) + math.dist(b, c) < 1e-4:
                        continue
                    mid = ((lx + c[0]) / 2, 0.0, (lz + c[2]) / 2)
                    side = (-1.0 if lx > 0 else 1.0, 0.0, 0.0) if lip == len(grid) - 1 else (1.0 if lx < 0 else -1.0, 0.0, 0.0)
                    geo.oriented(m, [a, b, c, d], side, P.BARK_DARK, WOOD)

    def roots(self, m):
        rng = self.rng
        with geo.state(m, alpha=0.6):
            for deg in (14, 50, 128, 166, 204, 246, 290, 334):
                a = math.radians(deg + rng.uniform(-6, 6))
                r0 = LAYOUT.rim_radius(a, self.spec) + 0.8
                length = rng.uniform(13, 24)
                wiggle = rng.uniform(-0.12, 0.12)
                pts, radii = [], []
                for k in range(11):
                    t = k / 10
                    ang = a + wiggle * math.sin(t * 3.1) + 0.04 * t
                    r = r0 + length * t
                    sink = t * t * 2.8
                    pts.append((r * math.cos(ang), -19.0 - 5.0 * min(1.0, t * 1.6) - sink * 0.4, r * math.sin(ang)))
                    radii.append(2.5 * (1 - t) ** 0.75 + 0.35)

                def color(i, k, n, deg=deg):
                    if n[1] > 0.45 and _hash(deg, i, k) > 0.35:
                        return geo.mix(P.MOSS, P.MOSS_LIGHT, _hash(i, k, deg))
                    return geo.mix(P.BARK, P.BARK_DARK, 0.3 + 0.4 * _hash(k, i, 5))

                S.tube(m, pts, radii, 9, color, WOOD)

    def foot_fungi(self, m):
        """Small cosmetic brackets and moss cushions on the flared foot."""
        rng = self.rng
        with geo.state(m, alpha=0.3):
            for deg, y, size in ((32, -15.5, 2.2), (36, -17.0, 1.6), (188, -16.0, 2.6), (232, -19.0, 1.8),
                                 (312, -14.5, 2.0), (318, -15.8, 1.4)):
                a = math.radians(deg)
                r = LAYOUT.rim_radius(a, self.spec) + self._flare(y) * 0.9
                cx, cz = r * math.cos(a), r * math.sin(a)
                nx, nz = math.cos(a), math.sin(a)
                tx, tz = -nz, nx
                ring = []
                for k in range(9):
                    th = math.pi * k / 8
                    rr = size * (1 - 0.12 * math.sin(th * 5) ** 2)
                    ring.append((cx + nx * math.sin(th) * rr - tx * math.cos(th) * size * 1.2 + nx * -0.4,
                                 y, cz + nz * math.sin(th) * rr - tz * math.cos(th) * size * 1.2 + nz * -0.4))
                center = (cx - nx * 0.4, y, cz - nz * 0.4)
                S.fan(m, center, ring, P.FUNGUS_BANDS[1], P.FUNGUS_BANDS[5], (0, 1, 0), WOOD, closed=False)
                under = (cx - nx * 0.4, y - size * 0.7, cz - nz * 0.4)
                S.fan(m, under, list(reversed(ring)), P.FUNGUS_PORE_DARK, P.FUNGUS_PORE, (nx, -1.0, nz), WOOD, closed=False)
        with geo.state(m, alpha=0.8):
            for _ in range(14):
                a = rng.uniform(0, 2 * math.pi)
                if abs(math.degrees(a) - 90) < 14:
                    continue
                r = LAYOUT.rim_radius(a, self.spec) + FLARE * rng.uniform(0.7, 1.4)
                size = rng.uniform(1.4, 3.2)
                S.blob(m, (r * math.cos(a), GROUND_Y + size * 0.15, r * math.sin(a)), (size, size * 0.45, size * 0.9),
                       lambda n, p: geo.mix(P.MOSS_DARK, P.MOSS_LIGHT, max(0.0, n[1]) * 0.8),
                       rng, rings=4, segments=9, bump=0.12, floor=GROUND_Y - 0.3, mat=WOOD)
