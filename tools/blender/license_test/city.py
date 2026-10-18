"""The city around the Department of Fighting, all background.

The streets lie 60 m below the deck with looping traffic, sidewalks, trees and a coned-off
lane. Beside the deck: the Test Center tower to the north with the FIGHTER'S LICENSE sign
on its roof edge, the neighbour's brick block to the west, a row of buildings south beyond
the fence. East of the open edge the city falls away: low blocks whose roofs the deck
overlooks, mid-rise further out and a hazy skyline, hills and sky at the horizon.

Nothing here enters the blast corridor, and within CAMERA_REACH of the open side (where a
fighter can be outside the walls) everything stays below the kill plane's envelope ceiling.
"""
from __future__ import annotations

import math
import random

from stagekit import geo
import palette as P
import parts
from parts import GLASS, LIT, Plane
import textures as T

FAR = "LT_Far"
SKY = "LT_Sky"
STREET_Y = -60.0
SKY_R = 1800.0
SUN_AZIMUTH = 100.0   # degrees clockwise from north (+Z): east-south-east over the open edge
SUN_ELEVATION = 58.0
AVENUE = (26.0, 42.0)          # x range of the north-south avenue below the open edge
SOUTH_STREET = (-42.0, -28.0)  # z range of the east-west street south of the deck
NORTH_STREET = (58.0, 72.0)
WEST_STREET = (-74.0, -62.0)


def sun_direction():
    az, el = math.radians(SUN_AZIMUTH), math.radians(SUN_ELEVATION)
    return (math.cos(el) * math.sin(az), math.sin(el), math.cos(el) * math.cos(az))


class City:
    def __init__(self, build):
        self.b = build
        self.rng = random.Random(build.seed + 101)
        self.ground_contacts = 0

    # ------------------------------------------------------------ helpers
    @staticmethod
    def visible(center, normal, margin=40.0):
        """Can a camera over the deck see this face's front?"""
        return normal[0] * -center[0] + normal[1] * -center[1] + margin > 0

    def building(self, m, fp, top, style, rng, base=STREET_Y, roof=True, lod=0, mat=LIT, windows=True):
        """A box building on the street: walls, windows on faces that can be seen, parapet and roof clutter."""
        x0, z0, x1, z1 = fp
        wall = {"brick": P.CITY_BRICK, "concrete": P.CITY_CONCRETE, "glass": P.CITY_GLASS, "teal": P.CITY_GLASS_TEAL,
                "sand": P.CITY_SAND, "white": P.CITY_WHITE, "terracotta": P.CITY_TERRACOTTA}[style]
        wall = geo.jitter(wall, rng, 0.07)
        geo.box_minmax(m, (x0, base, z0), (x1, top, z1), wall, mat, skip=("bottom",), top_color=P.CITY_ROOF)
        if windows:
            self._windows(m, fp, base, top, style, wall, rng, lod, mat)
        if roof:
            self._roof(m, fp, top, wall, rng, lod, mat)
        self.ground_contacts += 1

    def _windows(self, m, fp, y0, y1, style, wall, rng, lod, mat):
        x0, z0, x1, z1 = fp
        faces = [((x0, z0), (x1, z0), (0, -1)), ((x1, z0), (x1, z1), (1, 0)), ((x1, z1), (x0, z1), (0, 1)), ((x0, z1), (x0, z0), (-1, 0))]
        floor = 3.4 if style in ("brick", "sand", "terracotta", "white") else 3.9
        for a, b, n in faces:
            center = ((a[0] + b[0]) / 2, (a[1] + b[1]) / 2)
            if not self.visible(center, n):
                continue
            L = math.dist(a, b)
            plane = Plane.facing((center[0], 0.0, center[1]), n)
            if style in ("glass", "teal"):
                # Curtain wall: glass bands with spandrels and a few mullions.
                y = y0 + 4.5
                while y + floor < y1 - 0.8:
                    parts.panel(m, plane, -L / 2 + 0.4, y + 0.6, L / 2 - 0.4, y + floor - 0.3, geo.scale(P.CITY_GLASS, rng.uniform(0.85, 1.1)),
                                off=0.05, mat=GLASS if lod == 0 else mat, uvs=T.uv("glass") if lod == 0 else None)
                    y += floor
                if lod == 0:
                    u = -L / 2 + 0.4
                    while u < L / 2:
                        parts.panel(m, plane, u - 0.08, y0 + 4.5, u + 0.08, y1 - 0.8, geo.scale(wall, 0.9), off=0.08, mat=mat)
                        u += 3.0
                continue
            if lod >= 1:
                # Ribbon windows: one strip per storey.
                y = y0 + 4.2
                tone = P.CITY_WINDOW
                while y + floor < y1 - 0.8:
                    parts.panel(m, plane, -L / 2 + 0.8, y + 1.0, L / 2 - 0.8, y + floor - 0.7,
                                geo.mix(tone, P.CITY_WINDOW_LIT, rng.random() * 0.35), off=0.05, mat=mat)
                    y += floor * (2 if lod == 2 else 1)
                continue
            cell = "window_office" if style == "concrete" else "window_apt"
            bays = max(1, int(L / 3.0))
            bw = L / bays
            y = y0 + 4.4
            while y + floor < y1 - 0.8:
                for i in range(bays):
                    u0 = -L / 2 + i * bw + bw * 0.2
                    u1 = u0 + bw * 0.6
                    c = cell if cell != "window_apt" or rng.random() > 0.3 else "window_apt_lit"
                    parts.panel(m, plane, u0, y + 0.8, u1, y + floor - 0.6, (1, 1, 1), off=0.05, mat=GLASS, uvs=T.uv(c))
                y += floor
            # Street level: shop windows under a band.
            if y0 <= STREET_Y + 0.01:
                u = -L / 2 + 1.0
                while u < L / 2 - 4.0:
                    w = min(rng.uniform(4.0, 6.5), L / 2 - 1.0 - u)
                    parts.panel(m, plane, u, y0 + 0.4, u + w, y0 + 3.4, (1, 1, 1), off=0.06, mat=GLASS, uvs=T.uv("window_shop"))
                    u += w + rng.uniform(1.0, 2.0)

    def _roof(self, m, fp, y, wall, rng, lod, mat):
        x0, z0, x1, z1 = fp
        par = geo.scale(wall, 0.88)
        for lo, hi in (((x0, y, z0), (x1, y + 0.9, z0 + 0.3)), ((x0, y, z1 - 0.3), (x1, y + 0.9, z1)),
                       ((x0, y, z0), (x0 + 0.3, y + 0.9, z1)), ((x1 - 0.3, y, z0), (x1, y + 0.9, z1))):
            geo.box_minmax(m, lo, hi, par, mat, skip=("bottom",))
        if lod > 0:
            return
        w, d = x1 - x0, z1 - z0
        kinds = ["ac", "ac", "ac", "tank", "stair", "solar", "garden", "skylight"]
        for _ in range(max(2, int(w * d / 160))):
            kind = rng.choice(kinds)
            cx, cz = rng.uniform(x0 + 2.5, x1 - 2.5), rng.uniform(z0 + 2.5, z1 - 2.5)
            if kind == "ac":
                geo.box(m, (cx, y + 0.6, cz), (rng.uniform(1.4, 2.6), 1.2, rng.uniform(1.0, 1.8)), (0.74, 0.75, 0.76), mat, skip=("bottom",))
                geo.box(m, (cx, y + 1.25, cz), (0.9, 0.1, 0.9), (0.30, 0.31, 0.32), mat, skip=("bottom",))
            elif kind == "tank":
                geo.lathe(m, (cx, y, cz), [(0.15, 0.0), (0.15, 2.4), (1.5, 2.4), (1.5, 5.2), (1.6, 5.2), (0.0, 6.4)], 10,
                          [(0.32, 0.30, 0.28), (0.50, 0.36, 0.24), (0.50, 0.36, 0.24), (0.40, 0.28, 0.20), (0.36, 0.26, 0.20)], mat)
            elif kind == "stair":
                geo.box(m, (cx, y + 1.5, cz), (3.0, 3.0, 4.0), geo.scale(wall, 0.95), mat, skip=("bottom",), top_color=P.CITY_ROOF)
            elif kind == "solar":
                for k in range(3):
                    geo.box(m, (cx, y + 0.5, cz - 1.6 + k * 1.6), (4.0, 0.08, 1.2), (0.16, 0.22, 0.38), mat)
            elif kind == "garden":
                geo.box(m, (cx, y + 0.15, cz), (rng.uniform(3, 6), 0.3, rng.uniform(3, 6)), P.TREE, mat, skip=("bottom",))
            else:
                geo.box(m, (cx, y + 0.35, cz), (2.4, 0.7, 2.4), (0.62, 0.74, 0.82), mat, skip=("bottom",))

    # ------------------------------------------------------------ ground and streets
    def streets(self, m):
        y = STREET_Y
        # Base ground (sidewalk colour) and the roads over it.
        geo.oriented(m, [(-700, y - 0.02, -700), (900, y - 0.02, -700), (900, y - 0.02, 700), (-700, y - 0.02, 700)],
                     (0, 1, 0), P.SIDEWALK, LIT)
        roads_x = [AVENUE, WEST_STREET, (122.0, 136.0), (222.0, 236.0), (-170.0, -158.0)]
        roads_z = [SOUTH_STREET, NORTH_STREET, (-140.0, -126.0), (148.0, 162.0)]
        for a, b in roads_x:
            geo.oriented(m, [(a, y, -700), (b, y, -700), (b, y, 700), (a, y, 700)], (0, 1, 0), P.STREET, LIT)
            self._dashes(m, (a + b) / 2, -700, 700, axis="z")
        for a, b in roads_z:
            geo.oriented(m, [(-700, y + 0.004, a), (900, y + 0.004, a), (900, y + 0.004, b), (-700, y + 0.004, b)], (0, 1, 0), P.STREET, LIT)
            self._dashes(m, (a + b) / 2, -700, 900, axis="x")
        # Crosswalks where the avenue meets the south and north streets.
        for zc in SOUTH_STREET + NORTH_STREET:
            for k in range(8):
                x = AVENUE[0] + 1.0 + k * 1.9
                geo.oriented(m, [(x, y + 0.012, zc - 1.5), (x + 0.9, y + 0.012, zc - 1.5), (x + 0.9, y + 0.012, zc + 1.5), (x, y + 0.012, zc + 1.5)],
                             (0, 1, 0), P.PAINT_WHITE, LIT)
        # A coned-off lane on the avenue below the open edge, with a road-works sign.
        lane_x = AVENUE[0] + 2.0
        for k in range(14):
            z = -12.0 + k * 2.2
            parts.cone(m, (lane_x + 1.8 + (0.3 if k in (0, 13) else 0.0), y, z), height=0.75, radius=0.22, segments=8)
        geo.oriented(m, [(lane_x - 1.0, y + 0.01, -12.0), (lane_x + 1.6, y + 0.01, -12.0), (lane_x + 1.6, y + 0.01, 17.0), (lane_x - 1.0, y + 0.01, 17.0)],
                     (0, 1, 0), geo.scale(P.STREET, 0.8), LIT)
        geo.box(m, (lane_x, y + 0.5, 5.0), (1.6, 1.0, 2.6), (0.85, 0.55, 0.10), LIT, skip=("bottom",))  # a works trailer
        # Street trees along the avenue's far sidewalk and the south street.
        rng = self.rng
        for k in range(16):
            self._tree(m, (AVENUE[1] + 2.4, y, -70 + k * 10 + rng.uniform(-1, 1)), rng)
        for k in range(10):
            self._tree(m, (-60 + k * 12 + rng.uniform(-1, 1), y, SOUTH_STREET[0] - 2.4), rng)

    def _dashes(self, m, c, a, b, axis):
        y = STREET_Y + 0.008
        s = a
        while s < b:
            if axis == "z":
                pts = [(c - 0.1, y, s), (c + 0.1, y, s), (c + 0.1, y, s + 3.0), (c - 0.1, y, s + 3.0)]
            else:
                pts = [(s, y, c - 0.1), (s + 3.0, y, c - 0.1), (s + 3.0, y, c + 0.1), (s, y, c + 0.1)]
            geo.oriented(m, pts, (0, 1, 0), P.PAINT_WHITE, LIT)
            s += 9.0

    def _tree(self, m, base, rng):
        x, y, z = base
        h = rng.uniform(5.0, 7.5)
        geo.cylinder_between(m, (x, y, z), (x, y + h * 0.45, z), 0.18, 6, (0.36, 0.28, 0.20), LIT)
        geo.ellipsoid(m, (x, y + h * 0.65, z), (rng.uniform(1.8, 2.6), h * 0.32, rng.uniform(1.8, 2.6)),
                      geo.jitter(P.TREE, rng, 0.12), LIT, rings=4, segments=7,
                      color_fn=lambda r, s: geo.mix(P.TREE_DARK, P.TREE, r / 4.0))

    def traffic(self, m):
        """Cars looping along the avenue and the south street (StageNeon travel channels)."""
        rng = self.rng
        loop = 1400.0
        lanes = [((AVENUE[0] + 6.0, 0.0), (0.0, 1.0)), ((AVENUE[0] + 9.5, 0.0), (0.0, 1.0)),
                 ((AVENUE[1] - 2.4, 0.0), (0.0, -1.0)), ((AVENUE[1] - 5.6, 0.0), (0.0, -1.0)),
                 ((0.0, SOUTH_STREET[0] + 2.6), (1.0, 0.0)), ((0.0, SOUTH_STREET[1] - 2.6), (-1.0, 0.0)),
                 ((0.0, NORTH_STREET[0] + 2.6), (1.0, 0.0)), ((0.0, NORTH_STREET[1] - 2.6), (-1.0, 0.0))]
        for (ox, oz), (dx, dz) in lanes:
            s = -loop / 2 + rng.uniform(0, 30)
            while s < loop / 2:
                pos = (ox + dx * s if dx else ox, oz + dz * s if dz else oz)
                self.car(m, pos, (dx, dz), rng.uniform(9.0, 14.0), loop, rng)
                s += rng.uniform(18.0, 46.0)

    def car(self, m, pos, direction, speed, loop, rng):
        dx, dz = direction
        along = pos[0] * dx + pos[1] * dz
        yaw = math.atan2(dx, dz)
        kind = "taxi" if rng.random() < 0.2 else "bus" if rng.random() < 0.06 else "car"
        length, width, height = (11.5, 2.5, 2.9) if kind == "bus" else (4.4, 1.8, 0.8)
        body = P.TAXI if kind == "taxi" else (0.86, 0.20, 0.14) if kind == "bus" else rng.choice(P.CAR_COLORS)
        y = STREET_Y
        with geo.state(m, alpha=0.0, travel=(speed, along), direction=(dx * loop, dz * loop)):
            geo.box(m, (pos[0], y + 0.35 + height / 2, pos[1]), (width, height, length), body, FAR, yaw=yaw, skip=("bottom",))
            if kind != "bus":
                geo.box(m, (pos[0] - dx * 0.3, y + 0.35 + height + 0.28, pos[1] - dz * 0.3), (width * 0.86, 0.56, 2.3),
                        (0.20, 0.26, 0.32), FAR, yaw=yaw, skip=("bottom",))
            else:
                geo.box(m, (pos[0], y + 0.35 + height + 0.02, pos[1]), (width * 0.9, 0.04, length * 0.9), (0.9, 0.9, 0.88), FAR, yaw=yaw)

    # ------------------------------------------------------------ the near buildings
    def test_center(self, m):
        """The Department's tower north of the deck, with the rooftop FIGHTER'S LICENSE sign."""
        rng = random.Random(self.b.seed + 7)
        x0, z0, x1, z1 = -26.0, 30.0, 4.0, 52.0
        top = 44.0
        geo.box_minmax(m, (x0, STREET_Y, z0), (x1, top, z1), P.CONCRETE, LIT, skip=("bottom",), top_color=P.CITY_ROOF)
        south = Plane.facing(((x0 + x1) / 2, 0.0, z0), (0.0, -1.0))
        east = Plane.facing((x1, 0.0, (z0 + z1) / 2), (1.0, 0.0))
        west = Plane.facing((x0, 0.0, (z0 + z1) / 2), (-1.0, 0.0))
        hw, hd = (x1 - x0) / 2, (z1 - z0) / 2
        for plane, half in ((south, hw), (east, hd), (west, hd)):
            # Blue glass bands between pale concrete floor slabs, with corner piers.
            y = STREET_Y + 6.0
            while y + 4.0 < top - 1.0:
                parts.panel(m, plane, -half + 1.6, y + 0.9, half - 1.6, y + 3.7, (1, 1, 1), off=0.05, mat=GLASS, uvs=T.uv("glass"))
                y += 4.0
            for u in (-half, half - 1.6):
                parts.panel(m, plane, u, STREET_Y, u + 1.6, top, geo.scale(P.CONCRETE, 0.9), off=0.1, depth=0.1)
        parts.plate(m, south, "sign_dept", 0.0, 2.0 + 12.0, 8.0, off=0.12)  # over the light well, above the deck walls
        # Roof: parapet, plant and the sign frame along the south edge, facing the deck.
        self._roof(m, (x0, z0, x1, z1), top, P.CONCRETE, rng, 0, LIT)
        sign_w = 26.0
        sx0, sx1 = (x0 + x1) / 2 - sign_w / 2, (x0 + x1) / 2 + sign_w / 2
        sh = sign_w * T.CELLS["big_sign"][3] / T.CELLS["big_sign"][2]
        sy0 = top + 2.2
        face = Plane.facing(((x0 + x1) / 2, 0.0, z0 + 1.2), (0.0, -1.0))
        parts.panel(m, face, -sign_w / 2 - 0.3, sy0 - 0.3, sign_w / 2 + 0.3, sy0 + sh + 0.3, P.STEEL_DARK, off=0.0, depth=0.5)
        parts.panel(m, face, -sign_w / 2, sy0, sign_w / 2, sy0 + sh, (1, 1, 1), off=0.02, uvs=T.uv("big_sign"))
        for u in range(-12, 13, 4):
            geo.box(m, ((x0 + x1) / 2 + u, (top + sy0) / 2, z0 + 1.5), (0.3, sy0 - top, 0.3), P.STEEL_DARK, LIT, skip=("bottom",))
            geo.cylinder_between(m, ((x0 + x1) / 2 + u, top, z0 + 3.5), ((x0 + x1) / 2 + u, sy0 + sh * 0.6, z0 + 1.6), 0.1, 5, P.STEEL, LIT)
        # A flag on the roof.
        fx, fz = x0 + 4.0, z0 + 6.0
        geo.cylinder_between(m, (fx, top, fz), (fx, top + 9.0, fz), 0.08, 6, P.STEEL, LIT)
        geo.oriented(m, [(fx, top + 6.4, fz), (fx + 3.2, top + 6.6, fz), (fx + 3.2, top + 8.6, fz), (fx, top + 8.8, fz)], (0, 0, -1), P.SIGN_YELLOW, LIT)
        geo.oriented(m, [(fx, top + 6.4, fz), (fx + 3.2, top + 6.6, fz), (fx + 3.2, top + 8.6, fz), (fx, top + 8.8, fz)], (0, 0, 1), P.SIGN_YELLOW, LIT)
        # The light well between the deck and the tower: a low podium roof well below the kill plane.
        geo.box_minmax(m, (-26.0, STREET_Y, 16.0), (20.0, -15.0, 30.0), geo.scale(P.CONCRETE, 0.8), LIT, skip=("bottom",), top_color=P.CITY_ROOF)
        for k in range(6):
            geo.box(m, (-20.0 + k * 7.0, -14.4, 23.0), (2.4, 1.2, 1.8), (0.72, 0.73, 0.74), LIT, skip=("bottom",))

    def neighbour(self, m):
        """The neighbour's brick block west of the party wall, with fire escapes facing the deck."""
        rng = random.Random(self.b.seed + 9)
        fp = (-62.0, -24.0, -34.0, 24.0)
        top = 22.0
        self.building(m, fp, top, "brick", rng)
        x = fp[2]
        east = Plane.facing((x, 0.0, 0.0), (1.0, 0.0))
        for zc in (-12.0, 8.0):
            y = STREET_Y + 7.4
            flip = 1.0
            while y < top - 3.0:
                # Landing with a railing, and a stair flight up to the next landing, alternating.
                geo.box(m, (x + 0.75, y, zc), (1.5, 0.08, 4.4), P.STEEL_DARK, LIT)
                for dz in (-2.2, 2.2):
                    geo.box(m, (x + 1.47, y + 0.5, zc + dz), (0.05, 1.0, 0.05), P.STEEL_DARK, LIT, skip=("bottom",))
                geo.box(m, (x + 1.47, y + 1.0, zc), (0.05, 0.05, 4.45), P.STEEL_DARK, LIT)
                geo.box(m, (x + 1.47, y + 0.55, zc), (0.03, 0.03, 4.45), P.STEEL_DARK, LIT)
                if y + 3.4 < top - 3.0:
                    za, zb = zc - flip * 1.7, zc + flip * 1.7
                    flight = [(x + 0.4, y + 0.08, za), (x + 1.1, y + 0.08, za), (x + 1.1, y + 3.4, zb), (x + 0.4, y + 3.4, zb)]
                    geo.oriented(m, flight, (0.0, 1.0, -flip * 0.9), P.STEEL_DARK, LIT)
                    geo.oriented(m, flight, (0.0, -1.0, flip * 0.9), geo.scale(P.STEEL_DARK, 0.7), LIT)
                    geo.box(m, (x + 1.12, y + 1.74 + 0.5, zc), (0.04, 0.04, 3.4), P.STEEL_DARK, LIT)
                flip = -flip
                y += 3.4
        # A water tower on the roof.
        geo.lathe(m, (-48.0, top, 6.0), [(0.2, 0.0), (0.2, 3.0), (2.4, 3.0), (2.4, 7.4), (2.5, 7.4), (0.0, 9.2)], 12,
                  [(0.30, 0.28, 0.26), (0.48, 0.34, 0.22), (0.48, 0.34, 0.22), (0.36, 0.25, 0.18), (0.30, 0.22, 0.16)], LIT)
        for dx, dz in ((-1.6, -1.6), (1.6, -1.6), (-1.6, 1.6), (1.6, 1.6)):
            geo.cylinder_between(m, (-48.0 + dx, top, 6.0 + dz), (-48.0 + dx, top + 3.0, 6.0 + dz), 0.08, 4, P.STEEL_DARK, LIT)
        # The gap between the party wall and the neighbour: a low roof well below the kill plane.
        geo.box_minmax(m, (-34.0, STREET_Y, -24.0), (-21.0, -16.0, 24.0), geo.scale(P.CITY_BRICK, 0.9), LIT, skip=("bottom",), top_color=P.CITY_ROOF)

    def surroundings(self, m):
        """Blocks around the Department: south beyond the fence, east below the edge, north-east."""
        rng = self.rng
        # South row, seen through the fence bars.
        x = -96.0
        while x < 96.0:
            w = rng.uniform(14.0, 24.0)
            top = rng.uniform(-24.0, 16.0) if abs(x) < 50 else rng.uniform(-14.0, 26.0)
            self.building(m, (x, -76.0, x + w - 1.5, -46.0), top, rng.choice(["brick", "concrete", "sand", "white", "terracotta"]), rng)
            x += w
        # East of the avenue: low roofs the deck looks down onto, then taller blocks further out.
        for zr0, zr1 in ((-120.0, -46.0), (-24.0, 52.0), (76.0, 140.0)):
            z = zr0
            while z < zr1 - 6.0:
                d = min(rng.uniform(16.0, 26.0), zr1 - z)
                x = 48.0
                while x < 118.0:
                    w = rng.uniform(16.0, 30.0)
                    near = x < 80.0 and -60 < z < 60
                    top = rng.uniform(-44.0, -26.0) if near else rng.uniform(-40.0, -18.0)
                    style = rng.choice(["brick", "concrete", "sand", "white", "terracotta", "glass"] if not near
                                       else ["brick", "concrete", "sand", "white", "terracotta"])
                    self.building(m, (x, z, min(x + w - 2.0, 118.0), z + d - 2.0), top, style, rng)
                    x += w
                z += d
        # Mid-rise belt east of the next avenue.
        for zr0, zr1 in ((-200.0, -144.0), (-122.0, 144.0), (166.0, 230.0)):
            z = zr0
            while z < zr1 - 8.0:
                d = min(rng.uniform(20.0, 34.0), zr1 - z)
                for xr0, xr1 in ((140.0, 218.0), (240.0, 330.0)):
                    x = xr0
                    while x < xr1 - 8.0:
                        w = min(rng.uniform(18.0, 34.0), xr1 - x)
                        # Below the deck, so the open edge looks down over roofs to the skyline.
                        top = rng.uniform(-36.0, -6.0) if rng.random() < 0.88 else rng.uniform(-6.0, 14.0)
                        self.building(m, (x, z, x + w - 3.0, z + d - 3.0), top,
                                      rng.choice(["concrete", "glass", "teal", "sand", "white", "brick"]), rng, lod=1, roof=True)
                        x += w
                z += d
        # North of the north street.
        z = 78.0
        while z < 150.0:
            x = -120.0
            d = rng.uniform(22.0, 34.0)
            while x < 20.0:
                w = rng.uniform(18.0, 30.0)
                self.building(m, (x, z, x + w - 2.0, z + d - 2.0), rng.uniform(-5.0, 40.0),
                              rng.choice(["concrete", "glass", "brick", "sand", "white"]), rng, lod=1)
                x += w
            z += d
        # West beyond the neighbour.
        z = -90.0
        while z < 90.0:
            d = rng.uniform(20.0, 32.0)
            x = -150.0
            while x < -78.0:
                w = rng.uniform(18.0, 28.0)
                self.building(m, (x, z, x + w - 2.0, z + d - 2.0), rng.uniform(-10.0, 38.0),
                              rng.choice(["brick", "sand", "concrete", "terracotta"]), rng, lod=1)
                x += w
            z += d

    # ------------------------------------------------------------ far city, hills and sky
    def skyline(self, m):
        """Hazy towers on a ring 380-900 m out, densest east beyond the open edge."""
        rng = random.Random(self.b.seed + 41)
        for _ in range(170):
            az = rng.uniform(0, 2 * math.pi)
            east = math.cos(az - math.radians(90))
            if rng.random() > 0.35 + 0.65 * max(0.0, east):
                continue
            dist = rng.uniform(380.0, 900.0)
            cx, cz = dist * math.sin(az), dist * math.cos(az)
            w, d = rng.uniform(18, 42), rng.uniform(18, 42)
            top = rng.uniform(-10, 50) if rng.random() < 0.75 else rng.uniform(50, 150)
            haze = min(1.0, (dist - 380.0) / 520.0)
            base_col = geo.mix(rng.choice([P.CITY_CONCRETE, P.CITY_GLASS, P.CITY_SAND, P.CITY_GLASS_TEAL, P.CITY_BRICK,
                                           P.CITY_TERRACOTTA]), P.FAR_CITY, 0.3 + 0.45 * haze)
            base_col = geo.scale(base_col, rng.uniform(0.78, 1.0))
            fp = (cx - w / 2, cz - d / 2, cx + w / 2, cz + d / 2)
            geo.box_minmax(m, (fp[0], STREET_Y, fp[1]), (fp[2], top, fp[3]), base_col, FAR, skip=("bottom",),
                           top_color=geo.scale(base_col, 0.9))
            # Window bands as darker stripes on the faces that face the deck.
            band = geo.mix(base_col, P.CITY_WINDOW, 0.35 * (1 - haze) + 0.1)
            for a, b, n in (((fp[0], fp[1]), (fp[2], fp[1]), (0, -1)), ((fp[2], fp[1]), (fp[2], fp[3]), (1, 0)),
                            ((fp[2], fp[3]), (fp[0], fp[3]), (0, 1)), ((fp[0], fp[3]), (fp[0], fp[1]), (-1, 0))):
                c = ((a[0] + b[0]) / 2, (a[1] + b[1]) / 2)
                if not self.visible(c, n, 0.0):
                    continue
                plane = Plane.facing((c[0], 0.0, c[1]), n)
                L = math.dist(a, b)
                y = STREET_Y + 8.0
                step = 7.0 if top > 60 else 5.0
                while y + step < top - 2.0:
                    parts.panel(m, plane, -L / 2 + 1.2, y, L / 2 - 1.2, y + step * 0.45, band, off=0.2, mat=FAR)
                    y += step
            if top > 120 and rng.random() < 0.6:
                geo.cylinder_between(m, (cx, top, cz), (cx, top + rng.uniform(15, 35), cz), 0.6, 5, base_col, FAR, radius_b=0.2)

    def hills(self, m):
        """Low blue hills on the horizon beyond the city, east to north and south."""
        rng = random.Random(self.b.seed + 43)
        n = 64
        radius = 1450.0
        prev = None
        for k in range(n + 1):
            az = math.radians(15 + 150 * k / n)
            h = 40 + 70 * (0.5 + 0.5 * math.sin(k * 0.6 + 1.0)) * (0.6 + 0.4 * math.sin(k * 0.23)) + rng.uniform(-8, 8)
            p_low = (radius * math.sin(az), STREET_Y - 5.0, radius * math.cos(az))
            p_high = (radius * 0.98 * math.sin(az), STREET_Y + h, radius * 0.98 * math.cos(az))
            if prev is not None:
                quad = [prev[0], p_low, p_high, prev[1]]
                col = [P.HILLS, P.HILLS, geo.mix(P.HILLS, P.HAZE, 0.25), geo.mix(P.HILLS, P.HAZE, 0.25)]
                geo.oriented(m, quad, (-p_low[0], 0.0, -p_low[2]), col, FAR)
            prev = (p_low, p_high)

    SKY_RINGS = [(-12, P.HAZE), (0, P.SKY_HORIZON), (4, P.SKY_HORIZON), (12, P.SKY_MID), (28, P.SKY_MID),
                 (50, P.SKY_ZENITH), (90, P.SKY_ZENITH)]

    def sky_color(self, el, az):
        rings = self.SKY_RINGS
        base = rings[-1][1]
        for (e0, c0), (e1, c1) in zip(rings[:-1], rings[1:]):
            if e0 <= el <= e1:
                base = geo.mix(c0, c1, (el - e0) / (e1 - e0) if e1 > e0 else 0.0)
                break
        sun = sun_direction()
        e, a = math.radians(el), math.radians(az)
        v = (math.cos(e) * math.sin(a), math.sin(e), math.cos(e) * math.cos(a))
        glow = max(0.0, sum(v[i] * sun[i] for i in range(3))) ** 8
        return geo.mix(base, (0.96, 0.95, 0.90), 0.45 * glow)

    def sky(self, m):
        rings = [e for e, _ in self.SKY_RINGS]
        n_az = 72
        rows = []
        for el in rings:
            e = math.radians(el)
            rows.append([((SKY_R * math.cos(e) * math.sin(math.radians(360.0 * k / n_az)), SKY_R * math.sin(e) - 60.0,
                           SKY_R * math.cos(e) * math.cos(math.radians(360.0 * k / n_az))), self.sky_color(el, 360.0 * k / n_az))
                         for k in range(n_az)])
        with geo.state(m, alpha=0.0):
            for j in range(len(rows) - 1):
                for k in range(n_az):
                    k2 = (k + 1) % n_az
                    q = [rows[j][k], rows[j][k2], rows[j + 1][k2], rows[j + 1][k]]
                    pts = [p for p, _ in q]
                    centre = tuple(sum(p[i] for p in pts) / 4 for i in range(3))
                    geo.oriented(m, pts, (-centre[0], -(centre[1] + 60.0), -centre[2]), [c for _, c in q], SKY)
        # The sun disc on the light's bearing (above the gameplay camera's frame, seen in high views).
        sun = sun_direction()
        r = SKY_R * 0.96
        fwd = (-sun[0], -sun[1], -sun[2])
        right = geo._norm(geo._cross((0.0, 1.0, 0.0), fwd))
        up = geo._cross(fwd, right)
        centre = (sun[0] * r, sun[1] * r - 60.0, sun[2] * r)
        size = math.tan(math.radians(1.6)) * r
        ring = [tuple(centre[i] + (right[i] * math.cos(2 * math.pi * k / 24) + up[i] * math.sin(2 * math.pi * k / 24)) * size for i in range(3))
                for k in range(24)]
        with geo.state(m, alpha=1.0):
            for k in range(24):
                geo.oriented(m, [centre, ring[k], ring[(k + 1) % 24]], fwd, P.SUN_DISC, SKY)

    def clouds(self, m):
        """Fair-weather cumulus low around the horizon, drifting slowly west."""
        rng = random.Random(self.b.seed + 47)
        loop = 2 * (SKY_R + 200.0)
        for i in range(16):
            az = math.radians(360.0 * (i + rng.uniform(0.15, 0.85)) / 16)
            dist = rng.uniform(1000, 1500)
            cx, cz = dist * math.sin(az), dist * math.cos(az)
            cy = rng.uniform(120, 260)
            with geo.state(m, alpha=0.0, travel=(rng.uniform(1.5, 3.0), -cx), direction=(-loop, 0.0)):
                for _ in range(rng.randint(4, 7)):
                    ox, oz = rng.uniform(-120, 120), rng.uniform(-50, 50)
                    rx, ry, rz = rng.uniform(60, 120), rng.uniform(22, 40), rng.uniform(40, 80)
                    geo.ellipsoid(m, (cx + ox, cy + ry * 0.35, cz + oz), (rx, ry, rz), P.CLOUD, SKY, rings=5, segments=10,
                                  color_fn=lambda r, s: geo.mix(P.CLOUD_SHADE, P.CLOUD, min(1.0, r / 5.0 * 1.4)))


def build_all(b):
    c = City(b)
    b.part("background", "Street", "Streets", c.streets)
    b.part("background", "Traffic", "Traffic", c.traffic)
    b.part("background", "Test Center Tower", "Test Center", c.test_center)
    b.part("background", "Neighbour", "Neighbour", c.neighbour)
    b.part("background", "City Near", "Surroundings", c.surroundings)
    b.part("background", "City Far", "Skyline", c.skyline)
    b.part("background", "City Far", "Hills", c.hills)
    b.part("background", "Sky", "Sky", c.sky)
    b.part("background", "Clouds", "Clouds", c.clouds)
    return c
