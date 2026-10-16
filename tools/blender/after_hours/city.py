"""The living city around the club: streets, traffic, buildings, rail, bridge, sky.

Everything here is background. Build.background() checks each piece against the
blast corridor and the gameplay camera envelope: within ENVELOPE metres of the
roof bounds nothing may rise above the kill plane minus a margin.
"""
from __future__ import annotations

import math

from stagekit import geo
import palette as P
import signs

NIGHT = "AH_Night"
SKY = "AH_Sky"
STREET_Y = -36.0
FLOOR_H = 3.3
EXTENT = 450.0          # streets and traffic loops run from -EXTENT to EXTENT
STREET_X = [-190.0, -115.0, -40.0, 40.0, 115.0, 190.0]
STREET_Z = [-185.0, -110.0, -37.0, 38.0, 110.0, 185.0]
STREET_W = 15.0
SIDEWALK = 3.2
RIVER_Z = -205.0        # water south of this line
EXPRESSWAY_X = -78.0
TRAIN_X = 40.0


def block_rects():
    """Blocks between the street grid, as (x0, z0, x1, z1) inner sidewalk rectangles."""
    xs = [-EXTENT] + STREET_X + [EXTENT]
    zs = [-EXTENT] + STREET_Z + [EXTENT]
    out = []
    for i in range(len(xs) - 1):
        for j in range(len(zs) - 1):
            x0 = xs[i] + (STREET_W / 2 if i > 0 else 0)
            x1 = xs[i + 1] - (STREET_W / 2 if i + 1 < len(xs) - 1 else 0)
            z0 = zs[j] + (STREET_W / 2 if j > 0 else 0)
            z1 = zs[j + 1] - (STREET_W / 2 if j + 1 < len(zs) - 1 else 0)
            out.append((x0, z0, x1, z1))
    return out


class City:
    def __init__(self, build):
        self.b = build
        self.rng = build.rng
        self.C = build.C
        self.roof_lo = (min(p[0] for p in build.data["roof"]), min(p[1] for p in build.data["roof"]))
        self.roof_hi = (max(p[0] for p in build.data["roof"]), max(p[1] for p in build.data["roof"]))
        self.ceiling_near = self.C["killY"] - 2.0

    def near_distance(self, fp):
        """Distance from the roof bounds to a footprint (x0, z0, x1, z1)."""
        dx = max(self.roof_lo[0] - fp[2], fp[0] - self.roof_hi[0], 0.0)
        dz = max(self.roof_lo[1] - fp[3], fp[1] - self.roof_hi[1], 0.0)
        return math.hypot(dx, dz)

    # ------------------------------------------------------------ ground
    def ground(self, m):
        y = STREET_Y
        with geo.state(m, alpha=0.0):
            # asphalt everywhere north of the river
            geo.oriented(m, [(-EXTENT - 300, y - 0.02, RIVER_Z), (EXTENT + 300, y - 0.02, RIVER_Z),
                             (EXTENT + 300, y - 0.02, EXTENT + 300), (-EXTENT - 300, y - 0.02, EXTENT + 300)], (0, 1, 0), P.ASPHALT, NIGHT)
            for x0, z0, x1, z1 in block_rects():
                if z1 <= RIVER_Z:
                    continue
                z0 = max(z0, RIVER_Z + 6)
                # sidewalk ring and curb
                geo.box_minmax(m, (x0, y - 0.02, z0), (x1, y + 0.16, z1), P.SIDEWALK, NIGHT, skip=("bottom",))
        # lane markings and crosswalks (faint, unlit paint)
        with geo.state(m, alpha=0.0):
            for x in STREET_X:
                self._dashes(m, (x, -EXTENT), (x, EXTENT), P.PAINT_YELLOW)
            for z in STREET_Z:
                self._dashes(m, (-EXTENT, z), (EXTENT, z), P.PAINT_YELLOW)
            for x in STREET_X:
                for z in STREET_Z:
                    if z < RIVER_Z:
                        continue
                    self._crosswalk(m, x, z)

    def _dashes(self, m, a, b, color):
        L = math.dist(a, b)
        dx, dz = (b[0] - a[0]) / L, (b[1] - a[1]) / L
        s = 0.0
        while s < L:
            if any(abs((a[0] + dx * s) - x) < STREET_W / 2 + 1 and abs((a[1] + dz * s) - z) < STREET_W / 2 + 1
                   for x in STREET_X for z in STREET_Z):
                s += 6.0
                continue
            p0 = (a[0] + dx * s, a[1] + dz * s)
            p1 = (a[0] + dx * (s + 3.0), a[1] + dz * (s + 3.0))
            w = 0.12
            q = [(p0[0] - dz * w, p0[1] + dx * w), (p1[0] - dz * w, p1[1] + dx * w), (p1[0] + dz * w, p1[1] - dx * w), (p0[0] + dz * w, p0[1] - dx * w)]
            geo.oriented(m, [(x, STREET_Y + 0.005, z) for x, z in q], (0, 1, 0), geo.scale(color, 0.6), NIGHT)
            s += 6.0

    def _crosswalk(self, m, x, z):
        h = STREET_W / 2
        for k in range(7):
            o = -h + 1.2 + k * 2.0
            for (cx, cz, ax) in ((x + o, z - h - 1.6, "z"), (x + o, z + h + 1.6, "z"), (x - h - 1.6, z + o, "x"), (x + h + 1.6, z + o, "x")):
                sx, sz = (0.5, 2.6) if ax == "z" else (2.6, 0.5)
                geo.oriented(m, [(cx - sx / 2, STREET_Y + 0.006, cz - sz / 2), (cx + sx / 2, STREET_Y + 0.006, cz - sz / 2),
                                 (cx + sx / 2, STREET_Y + 0.006, cz + sz / 2), (cx - sx / 2, STREET_Y + 0.006, cz + sz / 2)],
                             (0, 1, 0), geo.scale(P.PAINT_WHITE, 0.55), NIGHT)

    def street_lights(self, m):
        """Lamp posts along both curbs; sodium on side streets, white LEDs on the avenues."""
        for x in STREET_X:
            led = abs(x) < 60
            for side in (-1, 1):
                cx = x + side * (STREET_W / 2 + 0.6)
                z = -EXTENT + 12
                while z < EXTENT:
                    if z > RIVER_Z + 8 and not any(abs(z - zz) < STREET_W for zz in STREET_Z):
                        self._lamp(m, (cx, z), (-side, 0), P.STREET_LED if led else P.SODIUM)
                    z += 34.0
        for z in STREET_Z:
            if z < RIVER_Z:
                continue
            led = abs(z) < 60
            for side in (-1, 1):
                cz = z + side * (STREET_W / 2 + 0.6)
                x = -EXTENT + 26
                while x < EXTENT:
                    if not any(abs(x - xx) < STREET_W for xx in STREET_X):
                        self._lamp(m, (x, cz), (0, -side), P.STREET_LED if led else P.SODIUM)
                    x += 34.0

    def _lamp(self, m, base, arm, color):
        x, z = base
        top = STREET_Y + 7.5
        with geo.state(m, alpha=0.0):
            geo.box(m, (x, STREET_Y + 3.75, z), (0.18, 7.5, 0.18), P.STEEL_DARK, NIGHT)
            geo.box(m, (x + arm[0] * 1.0, top, z + arm[1] * 1.0), (0.12 + abs(arm[0]) * 2.0, 0.12, 0.12 + abs(arm[1]) * 2.0), P.STEEL_DARK, NIGHT)
        with geo.state(m, alpha=1.0):
            geo.box(m, (x + arm[0] * 1.9, top - 0.12, z + arm[1] * 1.9), (0.5, 0.1, 0.5), color, NIGHT)
            # pool of light on the road
            cx, cz = x + arm[0] * 2.4, z + arm[1] * 2.4
            pts = [(cx + 3.2 * math.cos(a * math.pi / 6), STREET_Y + 0.01, cz + 3.2 * math.sin(a * math.pi / 6)) for a in range(12)]
            geo.oriented(m, pts, (0, 1, 0), geo.scale(color, 0.10), NIGHT)

    # ------------------------------------------------------------ traffic
    def traffic(self, m):
        rng = self.rng
        lanes = []
        for x in STREET_X:
            for off, d in ((-5.2, 1), (-1.9, 1), (1.9, -1), (5.2, -1)):
                lanes.append(((x + off, 0.0), (0.0, d), 2 * EXTENT, (8.0, 13.0), 40.0))
        for z in STREET_Z:
            if z < RIVER_Z:
                continue
            for off, d in ((-5.2, -1), (-1.9, -1), (1.9, 1), (5.2, 1)):
                lanes.append(((0.0, z + off), (d, 0.0), 2 * EXTENT, (8.0, 13.0), 40.0))
        for (ox, oz), (dx, dz), loop, (v0, v1), gap in lanes:
            s = -EXTENT + rng.uniform(0, gap)
            while s < EXTENT:
                pos = (ox + dx * s if dx else ox, oz + dz * s if dz else oz)
                if pos[1] > RIVER_Z + 6:
                    self.car(m, pos, (dx, dz), rng.uniform(v0, v1), loop, rng)
                s += gap * rng.uniform(0.6, 1.6)

    def car(self, m, pos, direction, speed, loop, rng, y=STREET_Y, kind=None):
        m = self.b.mover if getattr(self.b, "mover", None) is not None else m
        dx, dz = direction
        along = pos[0] * dx + pos[1] * dz
        yaw = math.atan2(dx, dz)
        kind = kind or ("taxi" if rng.random() < 0.22 else "bus" if rng.random() < 0.05 else "car")
        length, width, height = (11.0, 2.5, 2.9) if kind == "bus" else (4.4, 1.8, 0.75)
        body = P.CAB_YELLOW if kind == "taxi" else (0.20, 0.30, 0.55) if kind == "bus" else rng.choice(P.CAR_COLORS)
        with geo.state(m, alpha=0.0, travel=(speed, along), direction=(dx * loop, dz * loop)):
            geo.box(m, (pos[0], y + 0.3 + height / 2, pos[1]), (width, height, length), body, NIGHT, yaw=yaw, skip=("bottom",))
            if kind != "bus":
                geo.box(m, (pos[0] - dx * 0.3, y + 0.3 + height + 0.27, pos[1] - dz * 0.3), (width * 0.86, 0.55, 2.3),
                        geo.scale(body, 0.45), NIGHT, yaw=yaw, skip=("bottom",))
        hx, hz = -dz, dx  # right vector
        front = (pos[0] + dx * (length / 2 + 0.01), pos[1] + dz * (length / 2 + 0.01))
        back = (pos[0] - dx * (length / 2 + 0.01), pos[1] - dz * (length / 2 + 0.01))
        ly = y + 0.3 + (0.45 if kind != "bus" else 0.5)
        with geo.state(m, alpha=1.0, travel=(speed, along), direction=(dx * loop, dz * loop)):
            for side in (-1, 1):
                for (c, col, n) in ((front, P.HEADLIGHT, (dx, 0, dz)), (back, P.TAILLIGHT, (-dx, 0, -dz))):
                    px, pz = c[0] + hx * side * width * 0.36, c[1] + hz * side * width * 0.36
                    w = 0.22
                    pts = [(px - hx * w, ly - 0.08, pz - hz * w), (px + hx * w, ly - 0.08, pz + hz * w),
                           (px + hx * w, ly + 0.08, pz + hz * w), (px - hx * w, ly + 0.08, pz - hz * w)]
                    geo.oriented(m, pts, n, col, NIGHT)
            if kind == "taxi":
                geo.box(m, (pos[0] - dx * 0.3, y + 0.3 + height + 0.62, pos[1] - dz * 0.3), (0.7, 0.18, 0.25), P.GOLD, NIGHT, yaw=yaw)
            if kind == "bus":
                for side in (-1, 1):
                    for k in range(5):
                        c = (pos[0] + dx * (-4.0 + k * 2.0) + hx * side * (width / 2 + 0.01), pos[1] + dz * (-4.0 + k * 2.0) + hz * side * (width / 2 + 0.01))
                        pts = [(c[0] - dx * 0.8, y + 1.6, c[1] - dz * 0.8), (c[0] + dx * 0.8, y + 1.6, c[1] + dz * 0.8),
                               (c[0] + dx * 0.8, y + 2.6, c[1] + dz * 0.8), (c[0] - dx * 0.8, y + 2.6, c[1] - dz * 0.8)]
                        geo.oriented(m, pts, (hx * side, 0, hz * side), geo.scale(P.WINDOW_COOL, 0.7), NIGHT)

    # ------------------------------------------------------------ buildings
    def building(self, m, fp, height, style, rng, signs_spec=None, roof_items=True, base_y=STREET_Y):
        x0, z0, x1, z1 = fp
        top = base_y + height
        tiers = [(fp, base_y, top)]
        if style == "deco" and height > 55:
            # Art Deco setbacks
            inset1 = min(x1 - x0, z1 - z0) * 0.14
            t1 = base_y + height * 0.7
            t2 = base_y + height * 0.88
            tiers = [(fp, base_y, t1), ((x0 + inset1, z0 + inset1, x1 - inset1, z1 - inset1), t1, t2),
                     ((x0 + inset1 * 2, z0 + inset1 * 2, x1 - inset1 * 2, z1 - inset1 * 2), t2, top)]
        wall = {"brick": P.BRICK, "brown": P.BROWNSTONE, "office": P.GLASS, "teal": P.GLASS_TEAL,
                "concrete": P.CONCRETE, "deco": P.CONCRETE_WARM}[style]
        wall = geo.jitter(wall, rng, 0.08)
        lit_rate = {"brick": 0.36, "brown": 0.38, "office": 0.42, "teal": 0.42, "concrete": 0.32, "deco": 0.34}[style] * rng.uniform(0.55, 1.2)
        for k, ((a0, b0, a1, b1), ya, yb) in enumerate(tiers):
            with geo.state(m, alpha=0.0):
                geo.box_minmax(m, (a0, ya, b0), (a1, yb, b1), wall, NIGHT, skip=("bottom",), top_color=P.TAR)
            self._windows(m, (a0, b0, a1, b1), ya, yb, style, lit_rate, rng, ground_floor=(k == 0 and base_y == STREET_Y))
            self._parapet(m, (a0, b0, a1, b1), yb, wall, rng, crown=(style == "deco" and k == len(tiers) - 1) or (height > 70 and k == len(tiers) - 1))
        tx0, tz0, tx1, tz1 = tiers[-1][0]
        if roof_items:
            self._roof_items(m, tiers[-1][0], top, style, height, rng)
        if style == "deco" and height > 55:
            # spire with aviation light
            cx, cz = (tx0 + tx1) / 2, (tz0 + tz1) / 2
            with geo.state(m, alpha=0.0):
                geo.cylinder_between(m, (cx, top, cz), (cx, top + height * 0.18, cz), 0.5, 6, P.STEEL, NIGHT, radius_b=0.08)
            with geo.state(m, alpha=1.0, anim=(0.5, rng.random())):
                geo.box(m, (cx, top + height * 0.18 + 0.3, cz), (0.6, 0.6, 0.6), P.AVIATION, NIGHT)
        for spec in signs_spec or []:
            self._sign(m, fp, base_y, top, spec, rng)
        return top

    WINDOW_COLORS = [(P.WINDOW_WARM, 46), (P.WINDOW_AMBER, 26), (P.WINDOW_COOL, 12), (P.WINDOW_TV, 6), (P.WINDOW_PINK, 3)]

    def _window_color(self, rng, cool_bias=0.0):
        weights = [(c, w * (1 + cool_bias * 3) if c is P.WINDOW_COOL else w) for c, w in self.WINDOW_COLORS]
        r = rng.random() * sum(w for _, w in weights)
        for c, w in weights:
            r -= w
            if r <= 0:
                return c
        return weights[0][0]

    @staticmethod
    def face_visible(center, normal, margin=45.0):
        """Can any gameplay camera (within ~40 m of the arena) see this face's front?"""
        return normal[0] * -center[0] + normal[1] * -center[1] + margin > 0

    def _windows(self, m, fp, y0, y1, style, lit_rate, rng, ground_floor):
        x0, z0, x1, z1 = fp
        faces = [((x0, z0), (x1, z0), (0, -1)), ((x1, z0), (x1, z1), (1, 0)), ((x1, z1), (x0, z1), (0, 1)), ((x0, z1), (x0, z0), (-1, 0))]
        floor_h = FLOOR_H if style not in ("office", "teal") else 3.8
        start = y0 + (5.0 if ground_floor else 0.6)
        cool = 1.0 if style in ("office", "teal") else 0.0
        for a, b, n in faces:
            center = ((a[0] + b[0]) / 2, (a[1] + b[1]) / 2)
            if not self.face_visible(center, n):
                continue
            d = math.hypot(*center)
            lod = 0 if d < 110 else 1 if d < 240 else 2
            L = math.dist(a, b)
            plane = signs.Plane.facing((center[0], 0.0, center[1]), n)
            floor_step = floor_h * (2 if lod == 2 else 1)
            ribbon = style in ("office", "teal") or lod > 0
            bays = max(1, int(L / 3.2))
            bw = L / bays
            y = start
            while y + floor_h < y1 - 0.5:
                if ribbon:
                    # merged runs: lit stretches of several windows share one quad
                    k = 0
                    while k < bays:
                        run = min(bays - k, rng.randint(1, 4 if lod < 2 else 6))
                        lit = rng.random() < lit_rate
                        if lit:
                            u0 = -L / 2 + k * bw + bw * 0.18
                            u1 = -L / 2 + (k + run) * bw - bw * 0.18
                            col = self._window_color(rng, cool)
                            anim = (150.0 + rng.uniform(0.01, 0.03), rng.random()) if rng.random() < 0.15 else None
                            signs.panel(m, plane, u0, y + 0.85, u1, y + floor_h - 0.75, geo.scale(col, 0.5), off=0.03, glow=True, anim=anim)
                        k += run
                else:
                    k = 0
                    while k < bays:
                        run = min(bays - k, rng.randint(1, 3))
                        lit = rng.random() < lit_rate
                        col = self._window_color(rng, cool) if lit else P.WINDOW_OFF
                        anim = (150.0 + rng.uniform(0.01, 0.03), rng.random()) if lit and rng.random() < 0.15 else None
                        for j in range(run):
                            u0 = -L / 2 + (k + j) * bw + bw * 0.29
                            u1 = u0 + bw * 0.42
                            v0, v1 = y + 0.95, y + floor_h - 0.8
                            shade = geo.scale(col, rng.uniform(0.38, 0.55)) if lit else col
                            signs.panel(m, plane, u0, v0, u1, v1, shade, off=0.03, glow=lit, anim=anim)
                            if lit and lod == 0:
                                # mullion, transom and a drawn blind keep close windows from reading as flat tiles
                                frame = geo.scale(P.STEEL_DARK, 0.6)
                                signs.panel(m, plane, (u0 + u1) / 2 - 0.05, v0, (u0 + u1) / 2 + 0.05, v1, frame, off=0.045)
                                signs.panel(m, plane, u0, v0 + (v1 - v0) * 0.68, u1, v0 + (v1 - v0) * 0.68 + 0.08, frame, off=0.045)
                                if rng.random() < 0.35:
                                    signs.panel(m, plane, u0, v1 - (v1 - v0) * rng.uniform(0.2, 0.5), u1, v1,
                                                geo.scale(col, 0.22), off=0.04, glow=True)
                        k += run
                y += floor_step
            if ground_floor and lod == 0:
                # shopfronts with awnings and neon words
                u = -L / 2 + 1.0
                while u < L / 2 - 4.0:
                    w = min(rng.uniform(3.5, 6.0), L / 2 - 1.0 - u)
                    col = rng.choice([P.WINDOW_WARM, P.WINDOW_COOL, P.WINDOW_AMBER, P.WINDOW_PINK])
                    signs.panel(m, plane, u, y0 + 0.5, u + w, y0 + 3.6, geo.scale(col, 0.6), off=0.04, glow=True)
                    if rng.random() < 0.5:
                        awning = rng.choice([P.RED, P.TERRACOTTA, P.BLUE, (0.15, 0.4, 0.25)])
                        with geo.state(m, alpha=0.0):
                            p0 = plane.at(u, y0 + 3.9, 0.04)
                            p1 = plane.at(u + w, y0 + 3.9, 0.04)
                            p2 = plane.at(u + w, y0 + 3.3, 1.4)
                            p3 = plane.at(u, y0 + 3.3, 1.4)
                            geo.oriented(m, [p0, p1, p2, p3], (n[0], 1.0, n[1]), geo.scale(awning, 0.8), NIGHT)
                            geo.oriented(m, [p0, p1, p2, p3], (-n[0], -1.0, -n[1]), geo.scale(awning, 0.5), NIGHT)
                    if rng.random() < 0.6:
                        word = rng.choice(["OPEN", "BAR", "PIZZA", "RAMEN", "24H", "LIVE", "TATTOO", "DELI", "HOTEL", "ARCADE", "NOODLE", "SUSHI"])
                        signs.text(m, plane, word, 0.55, center=(u + w / 2, y0 + 4.35), color=rng.choice(P.NEON_SET), off=0.06,
                                   anim=(rng.choice([0.0, 0.0, 0.8, 1.1]), rng.random()))
                    u += w + rng.uniform(0.8, 2.0)

    def _parapet(self, m, fp, y, wall, rng, crown):
        x0, z0, x1, z1 = fp
        with geo.state(m, alpha=0.0):
            for lo, hi in (((x0, y, z0), (x1, y + 0.9, z0 + 0.3)), ((x0, y, z1 - 0.3), (x1, y + 0.9, z1)),
                           ((x0, y, z0), (x0 + 0.3, y + 0.9, z1)), ((x1 - 0.3, y, z0), (x1, y + 0.9, z1))):
                geo.box_minmax(m, lo, hi, geo.scale(wall, 0.85), NIGHT, skip=("bottom",))
        if crown:
            col = rng.choice([P.CYAN, P.MAGENTA, P.GOLD, P.VIOLET, P.BLUE])
            for a, b, n in (((x0, z0), (x1, z0), (0, -1)), ((x1, z0), (x1, z1), (1, 0)), ((x1, z1), (x0, z1), (0, 1)), ((x0, z1), (x0, z0), (-1, 0))):
                L = math.dist(a, b)
                plane = signs.Plane.facing(((a[0] + b[0]) / 2, 0.0, (a[1] + b[1]) / 2), n)
                signs.panel(m, plane, -L / 2, y + 0.5, L / 2, y + 0.75, col, off=0.04, glow=True)
                signs.panel(m, plane, -L / 2, y - 1.6, L / 2, y - 1.45, geo.scale(col, 0.8), off=0.04, glow=True)

    def _roof_items(self, m, fp, y, style, height, rng):
        x0, z0, x1, z1 = fp
        w, d = x1 - x0, z1 - z0
        seen = y < 24.0 and math.hypot((x0 + x1) / 2, (z0 + z1) / 2) < 260
        if not seen:
            self._antenna(m, fp, y, height, rng)
            return
        with geo.state(m, alpha=0.0):
            # stair / elevator bulkhead
            bx = rng.uniform(x0 + 2, max(x0 + 2.1, x1 - 6))
            bz = rng.uniform(z0 + 2, max(z0 + 2.1, z1 - 5))
            geo.box_minmax(m, (bx, y, bz), (bx + 4.0, y + 3.2, bz + 3.0), P.CONCRETE_DARK, NIGHT, skip=("bottom",), top_color=P.TAR)
            for _ in range(rng.randint(1, 4)):
                cx = rng.uniform(x0 + 2, x1 - 2)
                cz = rng.uniform(z0 + 2, z1 - 2)
                geo.box(m, (cx, y + 0.6, cz), (rng.uniform(1.2, 2.6), 1.2, rng.uniform(1.0, 2.0)), P.STEEL, NIGHT, skip=("bottom",))
        if style in ("brick", "brown") and rng.random() < 0.75:
            self.water_tower(m, (rng.uniform(x0 + 3, x1 - 3), rng.uniform(z0 + 3, z1 - 3)), y, rng)
        self._antenna(m, fp, y, height, rng)

    def _antenna(self, m, fp, y, height, rng):
        x0, z0, x1, z1 = fp
        if height > 45 and rng.random() < 0.6:
            cx, cz = rng.uniform(x0 + 3, x1 - 3), rng.uniform(z0 + 3, z1 - 3)
            mast = rng.uniform(6, 14)
            with geo.state(m, alpha=0.0):
                geo.cylinder_between(m, (cx, y, cz), (cx, y + mast, cz), 0.15, 4, P.STEEL_DARK, NIGHT)
            with geo.state(m, alpha=1.0, anim=(0.5, rng.random())):
                geo.box(m, (cx, y + mast + 0.2, cz), (0.45, 0.45, 0.45), P.AVIATION, NIGHT)

    def rooftop_party(self, m, fp, y, rng):
        """A lit rooftop hangout: deck, string lights, a glowing bar and a few tables."""
        x0, z0, x1, z1 = fp
        ix0, iz0, ix1, iz1 = x0 + 1.2, z0 + 1.2, x1 - 1.2, z1 - 1.2
        with geo.state(m, alpha=0.0):
            geo.box_minmax(m, (ix0, y, iz0), (ix1, y + 0.12, iz1), P.BAR_WOOD if hasattr(P, "BAR_WOOD") else P.BROWNSTONE, NIGHT, skip=("bottom",))
            posts = [(ix0, iz0), (ix1, iz0), (ix1, iz1), (ix0, iz1)]
            for px, pz in posts:
                geo.box(m, (px, y + 1.6, pz), (0.12, 3.2, 0.12), P.STEEL_DARK, NIGHT)
            for _ in range(max(2, int((ix1 - ix0) * (iz1 - iz0) / 30))):
                tx, tz = rng.uniform(ix0 + 1, ix1 - 1), rng.uniform(iz0 + 1, iz1 - 1)
                geo.box(m, (tx, y + 0.5, tz), (0.9, 0.08, 0.9), P.STEEL, NIGHT)
                geo.box(m, (tx, y + 0.25, tz), (0.1, 0.5, 0.1), P.STEEL_DARK, NIGHT)
        bar_z = iz0 + 0.6
        with geo.state(m, alpha=0.0):
            geo.box_minmax(m, (ix0 + 1.0, y, bar_z - 0.4), (min(ix1 - 1.0, ix0 + 6.0), y + 1.1, bar_z + 0.4), P.STEEL_DARK, NIGHT, skip=("bottom",))
        with geo.state(m, alpha=1.0):
            geo.box_minmax(m, (ix0 + 1.0, y + 1.1, bar_z - 0.42), (min(ix1 - 1.0, ix0 + 6.0), y + 1.16, bar_z + 0.42), geo.scale(P.HOT_PINK, 0.8), NIGHT)
        bulb_colors = [P.GOLD, P.NEON_WHITE, P.AMBER]
        posts3 = [(px, y + 3.2, pz) for px, pz in posts]
        for a, b in ((0, 2), (1, 3), (0, 1), (2, 3)):
            pts = geo.catenary(posts3[a], posts3[b], 0.7, 14)
            with geo.state(m, alpha=0.0):
                for p, q in zip(pts, pts[1:]):
                    geo.cylinder_between(m, p, q, 0.015, 3, P.STEEL_DARK, NIGHT, caps=False)
            for k, p in enumerate(pts[1:-1]):
                with geo.state(m, alpha=1.0, anim=(-0.6, rng.random()) if rng.random() < 0.08 else (0.0, 0.0)):
                    geo.box(m, (p[0], p[1] - 0.08, p[2]), (0.14, 0.14, 0.14), bulb_colors[k % 3], NIGHT)

    def water_tower(self, m, base, y, rng):
        cx, cz = base
        r = rng.uniform(1.6, 2.2)
        legs = 3.2
        with geo.state(m, alpha=0.0):
            for k in range(4):
                a = k * math.pi / 2 + math.pi / 4
                geo.cylinder_between(m, (cx + math.cos(a) * r * 0.8, y, cz + math.sin(a) * r * 0.8),
                                     (cx + math.cos(a) * r * 0.7, y + legs, cz + math.sin(a) * r * 0.7), 0.1, 4, P.STEEL_DARK, NIGHT)
            geo.lathe(m, (cx, y + legs, cz), [(r, 0.0), (r, 3.4), (r * 1.04, 3.5), (r * 0.5, 4.6), (0.08, 5.0)], 10,
                      [P.WATER_TANK, geo.scale(P.WATER_TANK, 0.8), geo.scale(P.WATER_TANK, 0.6), P.STEEL_DARK], NIGHT, cap_top=False)

    def _sign(self, m, fp, base_y, top, spec, rng):
        """spec: dict(kind, face 'n'/'s'/'e'/'w', ...)."""
        x0, z0, x1, z1 = fp
        face = spec["face"]
        a, b, n = {"s": ((x0, z0), (x1, z0), (0, -1)), "e": ((x1, z0), (x1, z1), (1, 0)),
                   "n": ((x1, z1), (x0, z1), (0, 1)), "w": ((x0, z1), (x0, z0), (-1, 0))}[face]
        L = math.dist(a, b)
        plane = signs.Plane.facing(((a[0] + b[0]) / 2, 0.0, (a[1] + b[1]) / 2), n)
        kind = spec["kind"]
        if kind == "billboard_wall":
            w, h = spec.get("size", (16.0, 8.0))
            u, v = spec.get("at", (0.0, top - h / 2 - 3.0))
            signs.panel(m, plane, u - w / 2 - 0.3, v - h / 2 - 0.3, u + w / 2 + 0.3, v + h / 2 + 0.3, P.STEEL_DARK, off=0.05, depth=0.5)
            import textures
            signs.panel(m, plane, u - w / 2, v - h / 2, u + w / 2, v + h / 2, P.NEON_WHITE, off=0.08,
                        uvs=textures.billboard_uv(spec["image"]), glow=True)
        elif kind == "screen":
            w, h = spec.get("size", (10.0, 10.0))
            u, v = spec.get("at", (0.0, top - h / 2 - 4.0))
            signs.screen(m, plane, w, h, spec["image"], (u, v), off=0.06)
        elif kind == "roof_billboard":
            w, h = spec.get("size", (14.0, 7.0))
            bp = signs.Plane(plane.at(spec.get("u", 0.0), top, -spec.get("inset", 3.0)), plane.r, plane.n)
            signs.billboard(m, bp, w, h, spec["image"], bottom=spec.get("lift", 2.0))
        elif kind == "blade":
            u = spec.get("u", L / 2 - 1.0)
            p = plane.at(u, 0.0, 0.0)
            ytop = spec.get("top", top - 3.0)
            signs.blade(m, (p[0], p[2]), n, spec.get("depth", 2.6), 0.5, ytop, ytop - spec.get("height", 14.0),
                        spec["text"], spec.get("color", P.MAGENTA), bulb_color=spec.get("bulbs", P.GOLD), outline=spec.get("outline"))
        elif kind == "text":
            u, v = spec.get("at", (0.0, top - 3.0))
            if spec.get("backing", True):
                width = len(spec["text"]) * spec["height"] * 0.95
                signs.panel(m, plane, u - width / 2, v - spec["height"] * 0.85, u + width / 2, v + spec["height"] * 0.85, P.STEEL_DARK, off=0.05, depth=0.3)
            signs.text(m, plane, spec["text"], spec["height"], center=(u, v), color=spec.get("color", P.CYAN), off=0.09,
                       style=spec.get("style", "flat"), anim=spec.get("anim"), per_letter_anim=spec.get("letters"))
        elif kind == "arrow":
            u, v = spec.get("at", (0.0, top - 6.0))
            s = spec.get("scale", 6.0)
            # chasing arrow bulbs pointing along -right (towards the club when placed accordingly)
            pts = []
            for k in range(12):
                pts.append(plane.at(u + s - k * s / 6, v, 0.1))
            for k in range(1, 5):
                pts.append(plane.at(u - s + k * s / 6, v + k * s / 6, 0.1))
                pts.append(plane.at(u - s + k * s / 6, v - k * s / 6, 0.1))
            signs.bulbs(m, pts, spec.get("color", P.GOLD), size=0.45, rate=104.0)

    # ------------------------------------------------------------ districts
    def club_block(self, m):
        """Low neighbours inside the club's own block: well below the kill plane."""
        rng = self.rng
        lots = [((-32.0, -29.5, -22.0, -19.0), 10.0, "brick"), ((22.0, -29.5, 32.5, -18.5), 12.0, "brown"),
                ((-32.5, 18.0, -21.5, 30.5), 9.0, "concrete"), ((21.0, 18.5, 32.5, 30.5), 11.0, "brick"),
                ((-32.5, -16.0, -24.0, 16.0), 7.0, "brown"), ((24.0, -16.0, 32.5, 16.0), 8.0, "concrete")]
        for k, (fp, h, style) in enumerate(lots):
            self.building(m, fp, h, style, rng, roof_items=k % 2 == 1)
            if k % 2 == 0:
                self.rooftop_party(m, fp, STREET_Y + h, rng)

    def near_ring(self, m):
        """Hand-placed neighbours across the streets; the north faces the club with Neon Row."""
        rng = self.rng
        self.occupied = []
        building = self.building

        def placed(m_, fp, *args, **kw):
            self.occupied.append(fp)
            return building(m_, fp, *args, **kw)
        self.building = placed
        try:
            self._near_ring(m, rng)
        finally:
            self.building = building

    def _near_ring(self, m, rng):
        # Neon Row, north side (the busiest view)
        self.building(m, (-46.0, 46.0, -14.0, 74.0), 58.0, "office", rng, [
            {"kind": "billboard_wall", "face": "s", "image": "slop_cola", "size": (20.0, 10.0), "at": (0.0, -8.0)},
            {"kind": "blade", "face": "s", "u": -14.0, "text": "HOTEL", "top": 2.0, "height": 13.0, "color": P.RED, "outline": P.GOLD},
        ])
        self.building(m, (-12.0, 46.0, 18.0, 70.0), 46.0, "deco", rng, [
            {"kind": "screen", "face": "s", "image": "arena_tonight", "size": (14.0, 14.0), "at": (0.0, -4.0)},
            {"kind": "text", "face": "s", "text": "AFTER HOURS", "height": 3.4, "at": (0.0, -16.5), "color": P.MAGENTA,
             "style": "tube", "letters": (lambda k, ch: (-1.0, 0.61) if k == 7 else None)},
            {"kind": "arrow", "face": "s", "at": (0.0, -21.5), "scale": 5.0, "color": P.GOLD},
        ])
        self.building(m, (20.0, 46.0, 33.0, 64.0), 22.0, "brick", rng, [
            {"kind": "roof_billboard", "face": "s", "image": "karaoke", "size": (12.0, 6.0), "inset": 3.0},
            {"kind": "text", "face": "s", "text": "KARAOKE", "height": 1.6, "at": (0.0, -26.0), "color": P.PINK},
        ])
        # Times-Square-like corner at Neon Row x East Ave
        self.building(m, (47.5, 45.5, 72.0, 70.0), 64.0, "teal", rng, [
            {"kind": "billboard_wall", "face": "w", "image": "manki_motors", "size": (20.0, 10.0), "at": (0.0, -6.0)},
            {"kind": "billboard_wall", "face": "s", "image": "wibou_watches", "size": (18.0, 9.0), "at": (0.0, 2.0)},
            {"kind": "screen", "face": "s", "image": "dj_set", "size": (9.0, 9.0), "at": (0.0, -12.0)},
            {"kind": "billboard_wall", "face": "w", "image": "bonk_burger", "size": (16.0, 8.0), "at": (0.0, -20.0)},
        ])
        self.building(m, (-72.0, 45.5, -48.0, 66.0), 38.0, "brown", rng, [
            {"kind": "blade", "face": "e", "u": 6.0, "text": "BAR", "top": -8.0, "height": 8.0, "color": P.CYAN, "outline": P.MAGENTA},
            {"kind": "roof_billboard", "face": "s", "image": "ramen", "size": (12.0, 6.0)},
        ])
        # East Ave, across the train line
        self.building(m, (47.5, -28.0, 70.0, -4.0), 22.0, "brick", rng, [
            {"kind": "roof_billboard", "face": "w", "image": "fightguy_gym", "size": (14.0, 7.0), "inset": 3.0},
        ])
        self.building(m, (47.5, -2.0, 66.0, 30.5), 30.0, "concrete", rng, [
            {"kind": "blade", "face": "w", "u": -9.0, "text": "LIVE", "top": -8.0, "height": 10.0, "color": P.VIOLET, "outline": P.CYAN},
            {"kind": "text", "face": "w", "text": "♪ JAZZ ♪", "height": 1.8, "at": (6.0, -26.0), "color": P.GOLD},
        ])
        self.rooftop_party(m, (47.5, -2.0, 66.0, 30.5), STREET_Y + 30.0, rng)
        # West Ave
        self.building(m, (-70.0, -26.0, -47.5, 6.0), 21.0, "brick", rng, [
            {"kind": "roof_billboard", "face": "e", "image": "after_hours", "size": (16.0, 8.0), "inset": 3.0, "lift": 1.5},
        ])
        self.rooftop_party(m, (-70.0, -26.0, -47.5, 6.0), STREET_Y + 21.0, rng)
        self.building(m, (-66.0, 8.0, -47.5, 30.5), 18.0, "brown", rng, [
            {"kind": "screen", "face": "e", "image": "heart", "size": (6.0, 6.0), "at": (0.0, -26.0)},
        ])
        # South St (quieter residential; their roofs read from the fight roof)
        self.building(m, (-30.0, -72.0, -6.0, -44.5), 24.0, "brick", rng)
        self.building(m, (-4.0, -70.0, 14.0, -44.5), 30.0, "brown", rng, [
            {"kind": "text", "face": "n", "text": "HOTEL", "height": 2.0, "at": (0.0, -18.0), "color": P.GREEN},
        ])
        self.building(m, (16.0, -74.0, 33.0, -44.5), 18.0, "concrete", rng, [
            {"kind": "roof_billboard", "face": "n", "image": "bonk_burger", "size": (12.0, 6.0), "inset": 2.0},
        ])
        self.building(m, (-66.0, -72.0, -47.5, -44.5), 34.0, "brown", rng)
        self.building(m, (47.5, -72.0, 70.0, -44.5), 26.0, "brick", rng, [
            {"kind": "screen", "face": "n", "image": "sale", "size": (7.0, 7.0), "at": (0.0, -8.0)},
        ])

    def mid_ring(self, m):
        """Procedural lots on the remaining blocks; height by district."""
        rng = self.rng
        for x0, z0, x1, z1 in block_rects():
            cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
            if abs(cx) < 40 and abs(cz) < 40:
                continue  # the club block holds the tower and its low neighbours
            if z1 <= RIVER_Z + 6:
                continue
            z0 = max(z0, RIVER_Z + 8)
            if x0 < EXPRESSWAY_X + 8 < x1:
                # keep a corridor for the expressway viaduct
                parts = [(x0, z0, EXPRESSWAY_X - 7, z1), (EXPRESSWAY_X + 9, z0, x1, z1)]
            else:
                parts = [(x0, z0, x1, z1)]
            for px0, pz0, px1, pz1 in parts:
                if px1 - px0 < 10:
                    continue
                self._fill_block(m, (px0, pz0, px1, pz1), rng)

    def _fill_block(self, m, fp, rng):
        x0, z0, x1, z1 = fp
        cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
        dist = math.hypot(cx, cz)
        downtown = math.exp(-((cx - 160) ** 2 + (cz - 200) ** 2) / (2 * 140.0 ** 2))
        nx = max(1, int((x1 - x0) / rng.uniform(18, 30)))
        nz = max(1, int((z1 - z0) / rng.uniform(18, 30)))
        for i in range(nx):
            for j in range(nz):
                lx0 = x0 + (x1 - x0) * i / nx + 0.8
                lx1 = x0 + (x1 - x0) * (i + 1) / nx - 0.8
                lz0 = z0 + (z1 - z0) * j / nz + 0.8
                lz1 = z0 + (z1 - z0) * (j + 1) / nz - 0.8
                if lx1 - lx0 < 6 or lz1 - lz0 < 6:
                    continue
                if any(lx0 < o[2] + 1.5 and o[0] - 1.5 < lx1 and lz0 < o[3] + 1.5 and o[1] - 1.5 < lz1 for o in getattr(self, "occupied", [])):
                    continue
                if dist > 300:
                    h = rng.uniform(20, 60) + 140 * downtown * rng.random()
                else:
                    h = rng.uniform(16, 50) + 120 * downtown * rng.random()
                if dist < 125 and cz < 40:
                    h = min(h, rng.uniform(18, 34))
                style = rng.choice(["brick", "brown", "concrete", "office", "teal", "deco"] if h > 50 else ["brick", "brown", "concrete", "brick"])
                spec = []
                if rng.random() < 0.18:
                    spec.append({"kind": "roof_billboard", "face": rng.choice("nsew"), "image": rng.choice(
                        ["slop_cola", "bonk_burger", "manki_motors", "wibou_watches", "fightguy_gym", "ramen", "karaoke", "after_hours"]),
                        "size": (12.0, 6.0), "inset": 2.5})
                elif rng.random() < 0.18 and h > 30:
                    spec.append({"kind": "blade", "face": rng.choice("nsew"), "u": rng.uniform(-3, 3),
                                 "text": rng.choice(["HOTEL", "BAR", "CLUB", "DINER", "LIVE", "JAZZ", "MOTEL"]),
                                 "top": STREET_Y + min(h - 3, 26.0), "height": 10.0, "color": rng.choice(P.NEON_SET), "outline": rng.choice(P.NEON_SET)})
                self.building(m, (lx0, lz0, lx1, lz1), h, style, rng, spec)

    def skyline(self, m):
        """Far silhouettes beyond the street grid: dark boxes with sparse window bands."""
        rng = self.rng
        for k in range(150):
            a = rng.uniform(0, 2 * math.pi)
            r = rng.uniform(EXTENT + 40, EXTENT + 380)
            cx, cz = math.cos(a) * r, math.sin(a) * r
            if cz < -EXTENT + 60 and abs(cx) < 380:
                continue  # leave the river mouth open
            downtown = math.exp(-((cx - 300) ** 2 + (cz - 420) ** 2) / (2 * 260.0 ** 2))
            w = rng.uniform(20, 46)
            d = rng.uniform(20, 46)
            h = rng.uniform(40, 110) + 200 * downtown * rng.random()
            col = geo.jitter(P.CONCRETE_DARK, rng, 0.15)
            with geo.state(m, alpha=0.0):
                geo.box_minmax(m, (cx - w / 2, STREET_Y, cz - d / 2), (cx + w / 2, STREET_Y + h, cz + d / 2), col, NIGHT, skip=("bottom",))
            # sparse lit bands on all faces
            for (a0, b0, n) in (((cx - w / 2, cz - d / 2), (cx + w / 2, cz - d / 2), (0, -1)), ((cx + w / 2, cz - d / 2), (cx + w / 2, cz + d / 2), (1, 0)),
                                ((cx + w / 2, cz + d / 2), (cx - w / 2, cz + d / 2), (0, 1)), ((cx - w / 2, cz + d / 2), (cx - w / 2, cz - d / 2), (-1, 0))):
                L = math.dist(a0, b0)
                plane = signs.Plane.facing(((a0[0] + b0[0]) / 2, 0.0, (a0[1] + b0[1]) / 2), n)
                y = STREET_Y + 6
                while y < STREET_Y + h - 4:
                    if rng.random() < 0.55:
                        u = rng.uniform(-L / 2, L / 2 - 3)
                        run = rng.uniform(2, min(14, L / 2 - u))
                        col2 = rng.choice([P.WINDOW_WARM, P.WINDOW_WARM, P.WINDOW_COOL, P.WINDOW_AMBER])
                        signs.panel(m, plane, u, y, u + run, y + 1.5, geo.scale(col2, 0.5), off=0.05, glow=True)
                    y += rng.choice([3.3, 6.6])
            if h > 120:
                with geo.state(m, alpha=1.0, anim=(0.45, rng.random())):
                    geo.box(m, (cx, STREET_Y + h + 1.5, cz), (1.2, 1.2, 1.2), P.AVIATION, NIGHT)

    # ------------------------------------------------------------ transit
    def elevated_train(self, m):
        rng = self.rng
        deck = -19.0
        x = TRAIN_X
        with geo.state(m, alpha=0.0):
            z = -EXTENT
            while z < EXTENT:
                if z > RIVER_Z + 6:
                    for side in (-4.2, 4.2):
                        geo.box(m, (x + side, (STREET_Y + deck) / 2, z), (0.6, deck - STREET_Y, 0.6), P.STEEL_DARK, NIGHT, skip=("bottom",))
                    geo.box(m, (x, deck - 1.1, z), (9.2, 0.6, 0.8), P.STEEL_DARK, NIGHT)
                z += 15.0
            geo.box_minmax(m, (x - 4.6, deck - 0.8, RIVER_Z + 6), (x + 4.6, deck, EXTENT), P.STEEL, NIGHT)
            for side in (-4.6, 4.3):
                geo.box_minmax(m, (x + side, deck, RIVER_Z + 6), (x + side + 0.3, deck + 1.2, EXTENT), P.STEEL_DARK, NIGHT)
        # signal lamps along the deck edge
        z = RIVER_Z + 10
        while z < EXTENT:
            with geo.state(m, alpha=1.0, anim=(0.0, 0.0)):
                geo.box(m, (x + 4.75, deck + 1.4, z), (0.2, 0.2, 0.2), P.GREEN if int(z) % 2 else P.RED, NIGHT)
            z += 60.0
        loop = 1600.0
        for track, direction, offset in ((-2.0, 1, -300.0), (2.0, -1, 420.0)):
            speed = 17.0
            for car in range(5):
                cz = offset - direction * car * 14.6
                self._train_car(m, (x + track, cz), direction, deck, speed, loop)

    def _train_car(self, m, pos, direction, deck, speed, loop):
        m = self.b.mover if getattr(self.b, "mover", None) is not None else m
        cx, cz = pos
        along = cz * direction
        with geo.state(m, alpha=0.0, travel=(speed, along), direction=(0.0, direction * loop)):
            geo.box(m, (cx, deck + 2.0, cz), (3.0, 3.2, 14.0), P.TRAIN, NIGHT, skip=("bottom",))
            geo.box(m, (cx, deck + 1.0, cz), (3.04, 0.35, 14.0), P.TRAIN_STRIPE, NIGHT, skip=("bottom", "top"))
        with geo.state(m, alpha=1.0, travel=(speed, along), direction=(0.0, direction * loop)):
            for side in (-1, 1):
                for k in range(6):
                    wz = cz - 5.6 + k * 2.25
                    pts = [(cx + side * 1.52, deck + 1.9, wz - 0.8), (cx + side * 1.52, deck + 1.9, wz + 0.8),
                           (cx + side * 1.52, deck + 3.0, wz + 0.8), (cx + side * 1.52, deck + 3.0, wz - 0.8)]
                    geo.oriented(m, pts, (side, 0, 0), geo.scale(P.WINDOW_COOL, 0.85), NIGHT)
            for end in (-1, 1):
                pts = [(cx - 1.0, deck + 1.4, cz + end * 7.01), (cx + 1.0, deck + 1.4, cz + end * 7.01),
                       (cx + 1.0, deck + 2.0, cz + end * 7.01), (cx - 1.0, deck + 2.0, cz + end * 7.01)]
                geo.oriented(m, pts, (0, 0, end), P.HEADLIGHT if end == direction else P.TAILLIGHT, NIGHT)

    def expressway(self, m):
        rng = self.rng
        deck = -9.0
        x = EXPRESSWAY_X
        z0 = -EXTENT
        with geo.state(m, alpha=0.0):
            z = z0
            while z < EXTENT:
                geo.box(m, (x, (STREET_Y + deck) / 2 - 0.6, z), (2.2, deck - STREET_Y - 1.2, 2.2), P.CONCRETE_DARK, NIGHT, skip=("bottom",))
                z += 32.0
            geo.box_minmax(m, (x - 8.5, deck - 1.4, z0), (x + 8.5, deck, EXTENT), P.CONCRETE, NIGHT)
            for side in (-8.5, 8.2):
                geo.box_minmax(m, (x + side, deck, z0), (x + side + 0.3, deck + 0.9, EXTENT), P.CONCRETE_DARK, NIGHT)
        # lamp line
        z = z0 + 10
        while z < EXTENT:
            with geo.state(m, alpha=0.0):
                geo.box(m, (x, deck + 4.0, z), (0.2, 8.0, 0.2), P.STEEL_DARK, NIGHT)
            with geo.state(m, alpha=1.0):
                geo.box(m, (x, deck + 8.1, z), (2.4, 0.15, 0.4), P.SODIUM, NIGHT)
            z += 36.0
        for off, d in ((-6.2, 1), (-2.4, 1), (2.4, -1), (6.2, -1)):
            s = z0 + rng.uniform(0, 12)
            while s < EXTENT:
                self.car(m, (x + off, s), (0.0, d), rng.uniform(18.0, 26.0), 2 * EXTENT, rng, y=deck, kind="car")
                s += rng.uniform(9.0, 22.0)

    def river_and_bridge(self, m):
        rng = self.rng
        water_y = STREET_Y - 2.5
        with geo.state(m, alpha=0.0):
            geo.oriented(m, [(-EXTENT - 500, water_y, -EXTENT - 500), (EXTENT + 500, water_y, -EXTENT - 500),
                             (EXTENT + 500, water_y, RIVER_Z), (-EXTENT - 500, water_y, RIVER_Z)], (0, 1, 0), (0.03, 0.035, 0.07), NIGHT)
            geo.box_minmax(m, (-EXTENT - 300, water_y, RIVER_Z - 0.6), (EXTENT + 300, STREET_Y + 0.2, RIVER_Z + 6), P.CONCRETE_DARK, NIGHT, skip=("bottom",))
        # embankment promenade lights
        x = -EXTENT
        while x < EXTENT:
            with geo.state(m, alpha=1.0):
                geo.box(m, (x, STREET_Y + 3.2, RIVER_Z + 1.5), (0.35, 0.35, 0.35), P.SODIUM, NIGHT)
            with geo.state(m, alpha=0.0):
                geo.box(m, (x, STREET_Y + 1.6, RIVER_Z + 1.5), (0.12, 3.2, 0.12), P.STEEL_DARK, NIGHT)
            x += 18.0
        # suspension bridge
        bz = RIVER_Z - 130.0
        deck = STREET_Y + 16.0
        span = 900.0
        with geo.state(m, alpha=0.0):
            geo.box_minmax(m, (-span / 2, deck - 2.0, bz - 9.0), (span / 2, deck, bz + 9.0), P.STEEL_DARK, NIGHT)
            for tx in (-170.0, 170.0):
                for side in (-8.0, 8.0):
                    geo.box(m, (tx, (water_y + deck + 60) / 2, bz + side), (4.0, deck + 60 - water_y, 3.0), P.CONCRETE_DARK, NIGHT, skip=("bottom",))
                geo.box(m, (tx, deck + 48, bz), (4.0, 3.0, 19.0), P.CONCRETE_DARK, NIGHT)
                geo.box(m, (tx, deck + 20, bz), (4.0, 2.5, 19.0), P.CONCRETE_DARK, NIGHT)
            for px in range(-420, 421, 60):
                geo.box(m, (px, (water_y + deck) / 2, bz), (6.0, deck - water_y, 14.0), P.CONCRETE_DARK, NIGHT, skip=("bottom",))
        # necklace lights on the main cables
        for side in (-8.5, 8.5):
            spans = [((-450.0, deck + 2.0), (-170.0, deck + 60.0), 18.0), ((-170.0, deck + 60.0), (170.0, deck + 60.0), 52.0),
                     ((170.0, deck + 60.0), (450.0, deck + 2.0), 18.0)]
            for (ax, ay), (bx, by), sag in spans:
                pts = geo.catenary((ax, ay, bz + side), (bx, by, bz + side), sag, 48)
                with geo.state(m, alpha=0.0):
                    for p, q in zip(pts, pts[1:]):
                        geo.cylinder_between(m, p, q, 0.25, 4, P.STEEL, NIGHT, caps=False)
                for k, p in enumerate(pts):
                    with geo.state(m, alpha=1.0, anim=(0.0, 0.0)):
                        geo.box(m, (p[0], p[1] + 0.35, p[2]), (0.9, 0.9, 0.9), P.NEON_WHITE if k % 2 else P.GOLD, NIGHT)
            # aviation lights on tower tops
            for tx in (-170.0, 170.0):
                with geo.state(m, alpha=1.0, anim=(0.5, 0.25 if tx > 0 else 0.0)):
                    geo.box(m, (tx, deck + 60.6, bz + side), (1.0, 1.0, 1.0), P.AVIATION, NIGHT)
        # bridge traffic
        for off, d in ((-5.5, 1), (-2.0, 1), (2.0, -1), (5.5, -1)):
            s = -span / 2 + rng.uniform(0, 20)
            while s < span / 2:
                self.car(m, (s, bz + off), (d, 0.0), rng.uniform(15.0, 22.0), span, rng, y=deck, kind="car")
                s += rng.uniform(14.0, 34.0)
        # light reflections on the water
        with geo.state(m, alpha=1.0, anim=(50.0 + 0.3, 0.0)):
            for k in range(120):
                x = rng.uniform(-EXTENT, EXTENT)
                z = rng.uniform(bz + 12, RIVER_Z - 4)
                col = rng.choice([P.SODIUM, P.GOLD, P.MAGENTA, P.CYAN, P.NEON_WHITE])
                w, l = rng.uniform(0.6, 2.0), rng.uniform(8.0, 30.0)
                geo.oriented(m, [(x - w, water_y + 0.02, z - l / 2), (x + w, water_y + 0.02, z - l / 2),
                                 (x + w, water_y + 0.02, z + l / 2), (x - w, water_y + 0.02, z + l / 2)], (0, 1, 0), geo.scale(col, 0.18), NIGHT)

    # ------------------------------------------------------------ sky
    def sky(self, m):
        rng = self.rng
        R = 1600.0
        rings = [(-8, P.SKY_HORIZON), (0, P.SKY_HORIZON), (4, P.SKY_GLOW), (12, P.SKY_MID), (28, P.SKY_HIGH), (55, P.SKY_ZENITH), (90, P.SKY_ZENITH)]
        seg = 48

        def color(el_i, az):
            el, base = rings[el_i]
            # brighter city glow towards downtown (north-east) and the bridge (south)
            boost = 0.35 * max(0.0, math.cos(az - math.radians(52))) + 0.2 * max(0.0, math.cos(az + math.radians(90)))
            fade = max(0.0, 1.0 - el / 30.0)
            return tuple(min(1.0, c * (1.0 + boost * fade)) for c in base)

        verts = []
        for i, (el, _) in enumerate(rings):
            row = []
            for s in range(seg):
                az = 2 * math.pi * s / seg
                e = math.radians(el)
                p = (R * math.cos(e) * math.sin(az), R * math.sin(e) - 40.0, R * math.cos(e) * math.cos(az))
                row.append((p, color(i, az)))
            verts.append(row)
        with geo.state(m, alpha=0.0):
            for i in range(len(rings) - 1):
                for s in range(seg):
                    s2 = (s + 1) % seg
                    q = [verts[i][s], verts[i][s2], verts[i + 1][s2], verts[i + 1][s]]
                    pts = [p for p, _ in q]
                    cols = [c for _, c in q]
                    centre = [sum(p[k] for p in pts) / 4 for k in range(3)]
                    geo.oriented(m, pts, (-centre[0], -centre[1] - 40.0, -centre[2]), cols, SKY)
        # stars
        for k in range(420):
            az = rng.uniform(0, 2 * math.pi)
            el = math.radians(rng.uniform(14, 85))
            r = R * 0.96
            c = (r * math.cos(el) * math.sin(az), r * math.sin(el) - 40.0, r * math.cos(el) * math.cos(az))
            size = rng.uniform(1.6, 4.2)
            n = (-c[0], -c[1] - 40.0, -c[2])
            L = math.sqrt(sum(x * x for x in n))
            n = tuple(x / L for x in n)
            u = geo._norm(geo._cross(n, (0, 1, 0)))
            v = geo._cross(u, n)
            pts = [tuple(c[i] + (u[i] * sx + v[i] * sy) * size for i in range(3)) for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
            with geo.state(m, alpha=1.0, anim=(rng.choice([0.0, 0.0, 0.0, 0.13, 0.21]), rng.random())):
                geo.oriented(m, pts, n, geo.scale(P.STAR, rng.uniform(0.3, 0.9)), SKY)
        # moon and halo
        az, el = math.radians(155), math.radians(30)
        r = R * 0.9
        c = (r * math.cos(el) * math.sin(az), r * math.sin(el) - 40.0, r * math.cos(el) * math.cos(az))
        n = geo._norm((-c[0], -c[1] - 40.0, -c[2]))
        u = geo._norm(geo._cross(n, (0, 1, 0)))
        v = geo._cross(u, n)
        for radius, col, alpha in ((95.0, geo.scale(P.SKY_GLOW, 0.8), 1.0), (60.0, geo.scale(P.MOON, 0.25), 1.0), (38.0, P.MOON, 1.0)):
            pts = [tuple(c[i] + (u[i] * math.cos(a) + v[i] * math.sin(a)) * radius + n[i] * (100 - radius) * 0.25 for i in range(3))
                   for a in [k * math.pi / 16 for k in range(32)]]
            with geo.state(m, alpha=alpha):
                geo.oriented(m, pts, n, col, SKY)

    def build_all(self):
        b = self.b
        b.background("City Ground", "Streets", lambda m: (self.ground(m), self.street_lights(m)))
        b.background("City Traffic", "Traffic", self.traffic)
        b.background("City Near", "Club Block", self.club_block)
        b.background("City Near", "Near Ring", self.near_ring)
        b.background("City Mid", "Mid Ring", self.mid_ring)
        b.background("City Far", "Skyline", self.skyline, far=True)
        b.background("City Transit", "Elevated Train", self.elevated_train)
        b.background("City Transit", "Expressway", self.expressway)
        b.background("City Far", "River And Bridge", self.river_and_bridge, far=True)
        b.background("Sky", "Sky", self.sky)
