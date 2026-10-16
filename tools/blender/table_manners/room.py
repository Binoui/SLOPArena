"""The kids' bedroom around the craft table, and the garden outside its window.

Scale: the table is a 1 m kids' table at 30x, so a real 30 cm toy is 9 m here. The room
is 132 x 144 m (4.4 x 4.8 m) with the floor at FLOOR_Y, 1 m under the kill plane.
Everything within the camera envelope (25 m of the table edge) stays under the kill
plane; furniture starts 40 m from the centre and hugs the walls.

Regions (from the default camera: north = window, east = bunk bed and toy box,
south = play corner and door, west = art corner):
  floor     boards, rainbow rug, low craft clutter
  walls     wainscot, chair rail, sky-blue upper walls with clouds, window, door, ceiling
  corners   bunk bed, toy box, desk and chair, easel, bookshelf, brick castle, train, teepee
  party     bunting along the walls, balloon bunches, presents, chairs pushed back
  garden    lawn, sandbox, swing set, slide, fence, trees, houses, hills, sky, sun, clouds
"""
from __future__ import annotations

import math

from stagekit import geo
import palette as P
import textures

BUILD = "TM_Build"
CLOTH = "TM_Cloth"   # StageVertexLit with wind (alpha = sway weight), double sided
GLOW = "TM_Glow"     # StageNeon: glow stars, lamp, toy train (travel)
SKY = "TM_Sky"       # StageNeon unlit: sky, sun, drifting clouds

FLOOR_Y = -16.0
CEIL_Y = 59.0
X0, X1 = -66.0, 66.0
Z0, Z1 = -72.0, 72.0
WAINSCOT_TOP = 11.0
WIN = (-24.0, 24.0, -1.0, 41.0)   # window opening on the north wall: x0, x1, y0, y1
WALL_T = 6.0
GARDEN_Y = -25.0
DOOR = (28.0, 52.0, 46.0)        # south wall: x0, x1, top


# ---------------------------------------------------------------- small kit

class Plane:
    """A vertical plane: origin, horizontal unit `right`, outward unit normal (map vector)."""

    def __init__(self, origin, normal):
        nx, nz = normal
        L = math.hypot(nx, nz)
        self.n = (nx / L, 0.0, nz / L)
        self.r = (-nz / L, 0.0, nx / L)
        self.o = origin

    def at(self, u, v, off=0.0):
        o, r, n = self.o, self.r, self.n
        return (o[0] + r[0] * u + n[0] * off, v, o[2] + r[2] * u + n[2] * off)

    def rect(self, m, u0, v0, u1, v1, color, off=0.0, mat=BUILD, uvs=None):
        pts = [self.at(u0, v0, off), self.at(u1, v0, off), self.at(u1, v1, off), self.at(u0, v1, off)]
        quv = None
        if uvs is not None:
            (a, b), (c, d) = uvs
            quv = [(a, b), (c, b), (c, d), (a, d)]
        geo.oriented(m, pts, self.n, color, mat, quv)

    def slab(self, m, u0, v0, u1, v1, depth, color, side=None, off=0.0, mat=BUILD, uvs=None):
        """A flat board standing `depth` proud of the plane (front plus four edges)."""
        self.rect(m, u0, v0, u1, v1, color, off + depth, mat, uvs)
        sc = side or geo.scale(color, 0.86)
        for (a, b, c, d, nrm) in (((u0, v0), (u1, v0), None, None, (0, -1)), ((u0, v1), (u1, v1), None, None, (0, 1)),
                                  ((u0, v0), (u0, v1), None, None, (-1, 0)), ((u1, v0), (u1, v1), None, None, (1, 0))):
            p0, p1 = self.at(a[0], a[1], off), self.at(b[0], b[1], off)
            p2, p3 = self.at(b[0], b[1], off + depth), self.at(a[0], a[1], off + depth)
            desired = (self.r[0] * nrm[0], float(nrm[1]), self.r[2] * nrm[0])
            geo.oriented(m, [p0, p1, p2, p3], desired, sc, mat)


def cyl(m, base, r, h, color, mat=BUILD, segs=12, top=True, bottom=False, colors=None):
    geo.lathe(m, base, [(r, 0.0), (r, h)], segs, colors or color, mat, cap_top=top, cap_bottom=bottom)


def ball(m, c, r, color, mat=BUILD, rings=7, segs=12, stretch=1.0, color_fn=None):
    geo.ellipsoid(m, c, (r, r * stretch, r), color, mat, rings=rings, segments=segs, color_fn=color_fn)


def crayon(m, a, b, r, color, mat=BUILD):
    """Hexagonal crayon from a (blunt end) to b (tip) with a darker paper wrapper."""
    d = [b[i] - a[i] for i in range(3)]
    L = math.sqrt(sum(x * x for x in d))
    u = [x / L for x in d]
    p1 = tuple(a[i] + u[i] * L * 0.12 for i in range(3))
    p2 = tuple(a[i] + u[i] * L * 0.78 for i in range(3))
    p3 = tuple(a[i] + u[i] * L * 0.88 for i in range(3))
    wrap = geo.scale(color, P.CRAYON_WRAP_DARK)
    geo.cylinder_between(m, a, p1, r, 6, color, mat)
    geo.cylinder_between(m, p1, p2, r * 1.02, 6, wrap, mat)
    geo.cylinder_between(m, p2, p3, r, 6, color, mat)
    geo.cylinder_between(m, p3, b, r, 6, color, mat, radius_b=r * 0.25)


def catenary(p0, p1, sag, n):
    return geo.catenary(p0, p1, sag, n)


def wall_grid(m, plane, u0, u1, v0, v1, cu, cv, color_at, mat=BUILD, off=0.0, skip=None):
    """A subdivided wall rectangle (AO needs vertices); `skip(u, v)` cuts openings."""
    nu = max(1, round((u1 - u0) / cu))
    nv = max(1, round((v1 - v0) / cv))
    for i in range(nu):
        for j in range(nv):
            a0, a1 = u0 + (u1 - u0) * i / nu, u0 + (u1 - u0) * (i + 1) / nu
            b0, b1 = v0 + (v1 - v0) * j / nv, v0 + (v1 - v0) * (j + 1) / nv
            if skip and skip((a0 + a1) / 2, (b0 + b1) / 2):
                continue
            plane.rect(m, a0, b0, a1, b1, color_at(a0, b0, a1, b1), off, mat)


# ---------------------------------------------------------------- the room

class KidsRoom:
    def __init__(self, build):
        self.b = build
        self.rng = build.rng
        self.R = build.data["table"]["radius"]
        self.kill = build.data["killY"]

    # ------------------------------------------------------------ floor
    def floor(self, m):
        rng = self.rng
        with geo.detail(m, 0.8):
            geo.top_grid(m, X0, Z0, X1, Z1, FLOOR_Y - 0.03, 8, 8, lambda i, j: P.FLOOR_GAP, BUILD)
            plank = 3.6
            z = Z0
            row = 0
            while z < Z1 - 1e-6:
                z1 = min(Z1, z + plank)
                x = X0 - rng.uniform(0, 30)
                while x < X1:
                    length = rng.uniform(28, 46)
                    xa, xb = max(X0, x), min(X1, x + length)
                    tone = rng.random()
                    base = P.FLOOR_LIGHT if tone > 0.75 else P.FLOOR_DARK if tone < 0.22 else P.FLOOR
                    base = geo.jitter(base, rng, 0.05)
                    n = max(1, round((xb - xa) / 6.0))
                    for k in range(n):
                        sa = xa + (xb - xa) * k / n + (0.06 if k == 0 else 0.0)
                        sb = xa + (xb - xa) * (k + 1) / n - (0.06 if k == n - 1 else 0.0)
                        cxm, czm = (sa + sb) / 2, (z + z1) / 2
                        if math.hypot(cxm, czm) < self.R + 7.5 and math.hypot(sa, czm) < self.R + 9 and math.hypot(sb, czm) < self.R + 9:
                            continue  # hidden under the rug
                        geo.box_minmax(m, (sa, FLOOR_Y - 0.03, z + 0.07), (sb, FLOOR_Y, z1 - 0.07), base, BUILD, skip=("bottom",))
                    x += length
                z = z1
                row += 1

    def rug(self, m):
        """Round rainbow rug framing the table: rings from the cloth puddle outwards."""
        n = 64
        radii = [self.R + 0.2, 17.0, 18.8, 20.6, 22.4, 24.2]
        y = FLOOR_Y + 0.12
        with geo.detail(m, 0.9):
            for k in range(len(radii) - 1):
                col = P.RUG_RINGS[k % len(P.RUG_RINGS)]
                for i in range(n):
                    a0, a1 = 2 * math.pi * i / n, 2 * math.pi * (i + 1) / n
                    r0, r1 = radii[k], radii[k + 1]
                    q = [(r0 * math.cos(a0), y, r0 * math.sin(a0)), (r1 * math.cos(a0), y, r1 * math.sin(a0)),
                         (r1 * math.cos(a1), y, r1 * math.sin(a1)), (r0 * math.cos(a1), y, r0 * math.sin(a1))]
                    geo.oriented(m, q, (0, 1, 0), geo.jitter(col, self.rng, 0.03), BUILD)
            R = radii[-1]
            for i in range(n):
                a0, a1 = 2 * math.pi * i / n, 2 * math.pi * (i + 1) / n
                geo.oriented(m, [(R * math.cos(a0), y, R * math.sin(a0)), (R * math.cos(a1), y, R * math.sin(a1)),
                                 (R * math.cos(a1), FLOOR_Y, R * math.sin(a1)), (R * math.cos(a0), FLOOR_Y, R * math.sin(a0))],
                             (math.cos(a0), 0, math.sin(a0)), P.RUG_RINGS[-1], BUILD)
            # fringe: short flat strands
            for i in range(120):
                a = 2 * math.pi * i / 120
                c, s = math.cos(a), math.sin(a)
                r0, r1 = R, R + 0.9
                w = 0.12
                q = [(r0 * c - s * w, FLOOR_Y + 0.02, r0 * s + c * w), (r1 * c - s * w, FLOOR_Y + 0.02, r1 * s + c * w),
                     (r1 * c + s * w, FLOOR_Y + 0.02, r1 * s - c * w), (r0 * c + s * w, FLOOR_Y + 0.02, r0 * s - c * w)]
                geo.oriented(m, q, (0, 1, 0), P.RUG_FRINGE, BUILD)

    def floor_clutter(self, m):
        """Low craft clutter on the rug and floor near the table, all under the kill plane."""
        rng = self.rng
        colors = list(P.CRAYON.values())
        y0 = FLOOR_Y + 0.12
        # crayons that rolled off the table
        for k, (r, deg, rot) in enumerate(((19.5, 18, 70), (21.0, 140, 10), (18.2, 205, 120), (23.0, 290, 40), (20.0, 330, 160),
                                           (26.5, 95, 30), (30.0, 250, 80), (28.0, 168, 140))):
            a = math.radians(deg)
            cx, cz = r * math.cos(a), r * math.sin(a)
            yaw = math.radians(rot)
            dx, dz = math.cos(yaw) * 1.4, math.sin(yaw) * 1.4
            yy = y0 + 0.3 if r < 24.2 else FLOOR_Y + 0.3
            crayon(m, (cx - dx, yy, cz - dz), (cx + dx, yy, cz + dz), 0.3, colors[k % len(colors)])
        # pom-poms
        for k in range(10):
            r = rng.uniform(16.5, 34.0)
            a = rng.uniform(0, 2 * math.pi)
            base = y0 if r < 24.2 else FLOOR_Y
            rad = rng.uniform(0.3, 0.4)
            ball(m, (r * math.cos(a), base + rad, r * math.sin(a)), rad, rng.choice(P.BALLOONS), rings=5, segs=8)
        # a dropped drawing and a paper aeroplane
        (u0, v0), (u1, v1) = textures.drawing_uv("dino")
        cx, cz, ang = 27.0, -14.0, 0.5
        c, s = math.cos(ang), math.sin(ang)
        rect = [(cx + u * c - v * s, cz + u * s + v * c) for u, v in ((-4.45, -3.15), (4.45, -3.15), (4.45, 3.15), (-4.45, 3.15))]
        geo.oriented(m, [(x, FLOOR_Y + 0.05, z) for x, z in rect], (0, 1, 0), P.PAPER, BUILD, [(u0, v0), (u1, v0), (u1, v1), (u0, v1)])
        tip, tail_l, tail_r, keel = (-22.0, -15.4, 24.0), (-27.0, -15.6, 20.5), (-24.2, -15.6, 19.0), (-25.4, -15.95, 20.0)
        for tri in ((tip, tail_l, keel), (tip, keel, tail_r)):
            geo.oriented(m, list(tri), (0, 1, 0), P.PAPER, BUILD)
        # confetti
        for k in range(160):
            r = rng.uniform(16.0, 36.0)
            a = rng.uniform(0, 2 * math.pi)
            x, z = r * math.cos(a), r * math.sin(a)
            y = (y0 if r < 24.2 else FLOOR_Y) + 0.03 + (k % 4) * 0.004
            sz = rng.uniform(0.25, 0.5)
            yaw = rng.uniform(0, math.pi)
            c, s = math.cos(yaw), math.sin(yaw)
            q = [(x + u * c - v * s, y, z + u * s + v * c) for u, v in ((-sz, -sz * 0.6), (sz, -sz * 0.6), (sz, sz * 0.6), (-sz, sz * 0.6))]
            geo.oriented(m, q, (0, 1, 0), rng.choice(P.BUNTING), BUILD)

    # ------------------------------------------------------------ walls
    def _wall_color(self, v0, v1):
        mid = (v0 + v1) / 2
        if mid < WAINSCOT_TOP:
            return P.WAINSCOT
        return P.WALL_SKY

    @staticmethod
    def _walls():
        """Inward-facing wall planes and their world extents along the wall."""
        return {
            "north": (Plane((0.0, 0.0, Z1), (0, -1)), X1, X0),
            "south": (Plane((0.0, 0.0, Z0), (0, 1)), X0, X1),
            "east": (Plane((X1, 0.0, 0.0), (-1, 0)), Z0, Z1),
            "west": (Plane((X0, 0.0, 0.0), (1, 0)), Z1, Z0),
        }

    @staticmethod
    def _skip(name, pl):
        """Openings in a wall: the north window and the south door."""
        def skip(u, v):
            if name == "north":
                x = pl.at(u, v)[0]
                return WIN[0] < x < WIN[1] and WIN[2] < v < WIN[3]
            if name == "south":
                x = pl.at(u, v)[0]
                return DOOR[0] < x < DOOR[1] and v < DOOR[2]
            return False
        return skip

    def walls(self, m):
        rng = self.rng
        for name, (pl, a, b) in self._walls().items():
            # plane u coordinate: the plane's right vector. Work in u space with explicit ends.
            ua, ub = self._u(pl, a, name), self._u(pl, b, name)
            u0, u1 = min(ua, ub), max(ua, ub)
            skip = self._skip(name, pl)

            with geo.detail(m, 0.5):
                wall_grid(m, pl, u0, u1, FLOOR_Y, WAINSCOT_TOP, 4.4, 4.5,
                          lambda a0, b0, a1, b1: geo.jitter(P.WAINSCOT, rng, 0.01), skip=skip)
                wall_grid(m, pl, u0, u1, WAINSCOT_TOP, CEIL_Y, 6.0, 6.0,
                          lambda a0, b0, a1, b1: geo.jitter(P.WALL_SKY, rng, 0.012), skip=skip)
            # chair rail, skirting and crown moulding
            with geo.detail(m, 0.3):
                for seg in self._segments(pl, name, u0, u1, WAINSCOT_TOP - 0.6, WAINSCOT_TOP + 1.2, skip):
                    pl.slab(m, seg[0], WAINSCOT_TOP - 0.6, seg[1], WAINSCOT_TOP + 1.2, 0.7, P.CHAIR_RAIL)
                for seg in self._segments(pl, name, u0, u1, FLOOR_Y, FLOOR_Y + 3.2, skip):
                    pl.slab(m, seg[0], FLOOR_Y, seg[1], FLOOR_Y + 3.2, 0.5, P.SKIRTING)
                pl.slab(m, u0, CEIL_Y - 2.2, u1, CEIL_Y, 0.9, P.TRIM_WHITE)

    def wall_decals(self, m):
        """Beadboard grooves and painted clouds: flush layers on the walls."""
        rng = self.rng
        for name, (pl, a, b) in self._walls().items():
            ua, ub = self._u(pl, a, name), self._u(pl, b, name)
            u0, u1 = min(ua, ub), max(ua, ub)
            skip = self._skip(name, pl)
            with geo.detail(m, 0.3):
                u = u0 + 3.0
                while u < u1 - 1.0:
                    if not (skip(u, 0.0) or skip(u, FLOOR_Y + 4)):
                        pl.rect(m, u - 0.12, FLOOR_Y + 3.0, u + 0.12, WAINSCOT_TOP - 0.6, P.WAINSCOT_SHADE, off=0.03)
                    u += 3.0
            with geo.detail(m, 0.1):
                for k in range(6):
                    u = u0 + (u1 - u0) * (k + 0.5) / 6 + rng.uniform(-5, 5)
                    v = rng.uniform(16, 34) if name != "north" else rng.uniform(46, 54)
                    if skip(u, v):
                        continue
                    for j in range(4):
                        du, dv, rr = (j - 1.5) * 2.6, (0.8 if j in (1, 2) else 0.0), 2.2 + (0.9 if j in (1, 2) else 0.0)
                        pts = [pl.at(u + du + rr * math.cos(t) * 1.25, v + dv + rr * math.sin(t) * 0.8, 0.05)
                               for t in (2 * math.pi * s / 14 for s in range(14))]
                        geo.oriented(m, pts, pl.n, P.WALL_CLOUD, BUILD)

    @staticmethod
    def _u(pl, coord, name):
        # convert a world coordinate along the wall to plane u
        if name in ("north", "south"):
            return (coord - pl.o[0]) / pl.r[0]
        return (coord - pl.o[2]) / pl.r[2]

    @staticmethod
    def _segments(pl, name, u0, u1, v0, v1, skip):
        """Split [u0, u1] around openings that cut the band v0..v1."""
        if name == "north":
            cut = sorted(((WIN[0] - pl.o[0]) / pl.r[0], (WIN[1] - pl.o[0]) / pl.r[0]))
            if v1 > WIN[2] and v0 < WIN[3]:
                return [(u0, cut[0]), (cut[1], u1)]
        if name == "south":
            cut = sorted(((DOOR[0] - pl.o[0]) / pl.r[0], (DOOR[1] - pl.o[0]) / pl.r[0]))
            if v0 < DOOR[2]:
                return [(u0, cut[0] - 1.6), (cut[1] + 1.6, u1)]
        return [(u0, u1)]

    def ceiling(self, m, g):
        with geo.detail(m, 0.2):
            nx, nz = 11, 12
            for i in range(nx):
                for j in range(nz):
                    xa, xb = X0 + (X1 - X0) * i / nx, X0 + (X1 - X0) * (i + 1) / nx
                    za, zb = Z0 + (Z1 - Z0) * j / nz, Z0 + (Z1 - Z0) * (j + 1) / nz
                    geo.oriented(m, [(xa, CEIL_Y, za), (xb, CEIL_Y, za), (xb, CEIL_Y, zb), (xa, CEIL_Y, zb)], (0, -1, 0), P.CEILING, BUILD)
        rng = self.rng
        for k in range(40):
            x, z = rng.uniform(X0 + 6, X1 - 6), rng.uniform(Z0 + 6, Z1 - 6)
            r = rng.uniform(1.2, 2.4)
            pts = []
            for s in range(10):
                a = math.pi / 2 + s * math.pi / 5
                rr = r if s % 2 == 0 else r * 0.45
                pts.append((x + rr * math.cos(a), CEIL_Y - 0.06, z + rr * math.sin(a)))
            with geo.state(g, alpha=0.6, anim=(50.0 + 0.15, rng.random())):
                geo.oriented(g, pts, (0, -1, 0), P.GLOW_STAR, GLOW)
        # paper lantern over the table
        cyl(m, (0.0, CEIL_Y - 9.0, 0.0), 0.15, 9.0, P.STRING, segs=5)
        with geo.state(g, alpha=0.9):
            ball(g, (0.0, CEIL_Y - 13.0, 0.0), 5.0, (1.0, 0.92, 0.78), GLOW, rings=8, segs=16, stretch=0.9)

    def window_wall(self, m, g):
        """Window frame, mullions, reveal, window seat and curtains (north wall)."""
        x0, x1, y0, y1 = WIN
        z = Z1
        with geo.detail(m, 0.3):
            # reveal (wall thickness)
            for a, b, nrm in (((x0, y0), (x1, y0), (0, 1, 0)), ((x1, y1), (x0, y1), (0, -1, 0))):
                geo.oriented(m, [(a[0], a[1], z), (b[0], b[1], z), (b[0], b[1], z + WALL_T), (a[0], a[1], z + WALL_T)], nrm, P.TRIM_WHITE, BUILD)
            for x, nrm in ((x0, (1, 0, 0)), (x1, (-1, 0, 0))):
                geo.oriented(m, [(x, y0, z), (x, y1, z), (x, y1, z + WALL_T), (x, y0, z + WALL_T)], nrm, P.TRIM_WHITE, BUILD)
            # frame boards around the opening, on the room face
            pl = Plane((0.0, 0.0, Z1), (0, -1))
            for (a0, a1, b0, b1) in ((x0 - 2.4, x1 + 2.4, y1, y1 + 2.4), (x0 - 2.4, x1 + 2.4, y0 - 2.0, y0),
                                     (x0 - 2.4, x0, y0, y1), (x1, x1 + 2.4, y0, y1)):
                u0, u1 = sorted(((a0 - pl.o[0]) / pl.r[0], (a1 - pl.o[0]) / pl.r[0]))
                pl.slab(m, u0, b0, u1, b1, 0.6, P.TRIM_WHITE)
            # mullions (set back in the reveal)
            zc = z + WALL_T * 0.55
            geo.box_minmax(m, (-0.8, y0, zc - 0.6), (0.8, y1, zc + 0.6), P.TRIM_WHITE, BUILD)
            geo.box_minmax(m, (x0, (y0 + y1) / 2 - 0.8, zc - 0.6), (x1, (y0 + y1) / 2 + 0.8, zc + 0.6), P.TRIM_WHITE, BUILD)
            # window seat: white bench with a blue cushion
            geo.box_minmax(m, (-27.0, FLOOR_Y, 62.5), (27.0, y0 - 0.4, Z1), P.PAINT_WHITE, BUILD, skip=("bottom",))
            geo.box_minmax(m, (-26.0, y0 - 0.4, 63.0), (26.0, y0 + 2.0, Z1 - 0.2), P.BEDDING_BLUE, BUILD, skip=("bottom",))
            for k, cx in enumerate((-19.0, 17.0)):
                geo.box(m, (cx, y0 + 4.2, 69.0), (8.0, 4.4, 2.6), P.BALLOONS[(k + 3) % 7], BUILD, yaw=0.2 * (1 if k else -1))
        # curtains on a rod
        geo.cylinder_between(m, (-42.0, 45.5, 69.0), (42.0, 45.5, 69.0), 0.55, 8, P.PINE_DARK, BUILD)
        for side in (-1, 1):
            xa, xb = (side * 23.0, side * 41.0)
            if side < 0:
                xa, xb = xb, xa
            nx, ny = 18, 12
            top, bottom = 44.8, 2.8
            grid = []
            for j in range(ny + 1):
                y = top + (bottom - top) * j / ny
                row = []
                for i in range(nx + 1):
                    x = xa + (xb - xa) * i / nx
                    zz = 68.6 + 0.9 * math.sin(i * math.pi / 2)
                    row.append((x, y, zz))
                grid.append(row)
            for j in range(ny):
                for i in range(nx):
                    q = [grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i]]
                    hem = j == ny - 1
                    col = P.CURTAIN_DOT if hem else (P.CURTAIN_DOT if (i + j) % 5 == 0 else P.CURTAIN)
                    weight = (j + 0.5) / ny
                    with geo.state(m, alpha=0.15 + 0.85 * weight):
                        geo.oriented(m, q, (0, 0, -1), col, CLOTH)

    def door(self, m):
        x0, x1, top = DOOR
        pl = Plane((0.0, 0.0, Z0), (0, 1))
        U = lambda x: (x - pl.o[0]) / pl.r[0]

        def slab(xa, ya, xb, yb, depth, color, uvs=None):
            ua, ub = sorted((U(xa), U(xb)))
            pl.slab(m, ua, ya, ub, yb, depth, color, uvs=uvs)

        with geo.detail(m, 0.3):
            slab(x0 - 1.6, FLOOR_Y, x0, top + 1.6, 0.6, P.TRIM_WHITE)
            slab(x1, FLOOR_Y, x1 + 1.6, top + 1.6, 0.6, P.TRIM_WHITE)
            slab(x0 - 1.6, top, x1 + 1.6, top + 1.6, 0.6, P.TRIM_WHITE)
            slab(x0, FLOOR_Y, x1, top, 0.35, (0.95, 0.93, 0.88))
            for (a, b) in ((FLOOR_Y + 4, FLOOR_Y + 26), (FLOOR_Y + 32, top - 4)):
                slab(x0 + 3, a, x1 - 3, b, 0.55, (0.92, 0.90, 0.85))
            ball(m, pl.at(U(x1 - 3.2), FLOOR_Y + 30, 1.4), 0.9, (0.85, 0.70, 0.30), rings=5, segs=10)
            # growth chart beside the door (u runs west here, so flip the strip's u)
            (u0, v0), (u1, v1) = textures.poster_uv("growth")
            slab(x0 - 9.0, FLOOR_Y + 4, x0 - 4.0, FLOOR_Y + 46, 0.12, P.PAPER, uvs=((u1, v0), (u0, v1)))

    def posters(self, m):
        """Posters and the kids' framed paintings on the walls (inside the camera band)."""
        items = [
            # wall, centre u (world x or z), v centre, width, height, slot
            ("north", 42.0, 20.0, 12.0, 15.0, "abc"),
            ("north", -44.0, 20.0, 12.0, 15.5, "space"),
            ("east", -50.0, 17.0, 18.0, 13.0, "map"),
            ("west", 32.0, 18.0, 12.0, 16.0, "fightguy"),
            ("west", 4.0, 18.0, 20.0, 12.0, "corkboard"),
            ("south", -28.0, 20.0, 26.0, 13.0, "rainbow"),
            ("south", 4.0, 19.0, 11.0, 14.0, "manki"),
        ]
        planes = {"north": Plane((0.0, 0.0, Z1), (0, -1)), "south": Plane((0.0, 0.0, Z0), (0, 1)),
                  "east": Plane((X1, 0.0, 0.0), (-1, 0)), "west": Plane((X0, 0.0, 0.0), (1, 0))}
        for wall, c, v, w, h, slot in items:
            pl = planes[wall]
            u = self._u(pl, c, wall)
            (u0, v0), (u1, v1) = textures.poster_uv(slot)
            with geo.detail(m, 0.15):
                frame = slot in ("fightguy", "manki")
                if frame:
                    pl.slab(m, u - w / 2 - 0.9, v - h / 2 - 0.9, u + w / 2 + 0.9, v + h / 2 + 0.9, 0.4, P.BALLOONS[len(slot) % 7])
                pl.slab(m, u - w / 2, v - h / 2, u + w / 2, v + h / 2, 0.5 if frame else 0.12, P.PAPER, uvs=((u0, v0), (u1, v1)))

    # ------------------------------------------------------------ corners
    def bunk_bed(self, m, cl):
        x0, x1, z0, z1 = 41.0, 65.5, 6.0, 63.0
        post = 2.2
        wood, dark = P.PINE, P.PINE_DARK
        with geo.detail(m, 0.6):
            for x in (x0, x1 - post):
                for z in (z0, z1 - post):
                    geo.box_minmax(m, (x, FLOOR_Y, z), (x + post, 36.0, z + post), wood, BUILD, skip=("bottom",))
                    ball(m, (x + post / 2, 36.6, z + post / 2), 1.3, wood, rings=5, segs=10)
            for (yb, yt) in ((-12.0, -7.0), (11.0, 15.0)):
                geo.box_minmax(m, (x0, yb, z0 + post), (x0 + 1.4, yt, z1 - post), dark, BUILD)
                geo.box_minmax(m, (x1 - 1.4, yb, z0 + post), (x1, yt, z1 - post), dark, BUILD)
                geo.box_minmax(m, (x0 + post, yb, z0), (x1 - post, yt + 6.0, z0 + 1.2), wood, BUILD)
                geo.box_minmax(m, (x0 + post, yb, z1 - 1.2), (x1 - post, yt + 6.0, z1), wood, BUILD)
            # mattresses and bedding
            for (ytop, cover, stripe) in ((-3.6, P.BEDDING_BLUE, P.BEDDING_STAR), (18.6, P.BEDDING_GREEN, P.PILLOW)):
                geo.box_minmax(m, (x0 + 1.0, ytop - 3.6, z0 + 1.2), (x1 - 1.0, ytop - 1.2, z1 - 1.2), P.PILLOW, BUILD)
                geo.box_minmax(m, (x0 + 0.6, ytop - 1.4, z0 + 1.0), (x1 - 1.2, ytop, z1 - 13.0), cover, BUILD,
                               side_color=geo.scale(cover, 0.9))
                for zz in range(int(z0 + 6), int(z1 - 16), 7):
                    geo.box_minmax(m, (x0 + 0.5, ytop - 1.0, zz), (x1 - 1.3, ytop + 0.02, zz + 1.6), stripe, BUILD)
                geo.box(m, ((x0 + x1) / 2 + 2.0, ytop + 1.4, z1 - 7.5), (14.0, 3.2, 8.0), P.PILLOW, BUILD)
            # guard rail on the top bunk (facing the room), leaving the ladder gap
            for zz in range(int(z0 + post), int(z1 - post) + 1, 6):
                if 9.0 < zz < 20.0:
                    continue
                geo.box_minmax(m, (x0, 15.0, zz - 0.5), (x0 + 1.0, 27.0, zz + 0.5), wood, BUILD)
            geo.box_minmax(m, (x0 - 0.2, 26.0, 20.0), (x0 + 1.2, 27.6, z1 - post), dark, BUILD)
            # ladder
            for zz in (10.0, 18.0):
                geo.box_minmax(m, (x0 - 1.6, FLOOR_Y, zz), (x0 - 0.2, 27.0, zz + 1.2), wood, BUILD, skip=("bottom",))
            for k in range(8):
                y = FLOOR_Y + 5.0 + k * 5.2
                geo.cylinder_between(m, (x0 - 0.9, y, 10.6), (x0 - 0.9, y, 18.6), 0.45, 6, dark, BUILD)
        # blanket hanging over the top bunk's side
        nx, ny = 10, 8
        for j in range(ny):
            for i in range(nx):
                za, zb = 30.0 + i * 2.6, 30.0 + (i + 1) * 2.6
                ya, yb = 18.4 - j * 2.2, 18.4 - (j + 1) * 2.2
                sway = lambda y: 0.6 * math.sin(y * 0.4)
                q = [(x0 - 0.5 - sway(ya), ya, za), (x0 - 0.5 - sway(ya), ya, zb), (x0 - 0.5 - sway(yb), yb, zb), (x0 - 0.5 - sway(yb), yb, za)]
                col = P.BEDDING_GREEN if (i + j) % 2 else P.PILLOW
                with geo.state(cl, alpha=(j + 1) / ny):
                    geo.oriented(cl, q, (-1, 0, 0), col, CLOTH)

    def toy_box(self, m):
        x0, x1, z0, z1, top = 50.0, 65.5, -38.0, -14.0, -2.0
        with geo.detail(m, 0.5):
            geo.box_minmax(m, (x0, FLOOR_Y, z0), (x1, top, z1), P.TOYBOX_RED, BUILD, skip=("bottom", "top"),
                           side_color=P.TOYBOX_RED)
            geo.box_minmax(m, (x0 + 0.8, top - 6.0, z0 + 0.8), (x1 - 0.8, top - 5.6, z1 - 0.8), (0.45, 0.14, 0.12), BUILD)
            for x, z in ((x0, z0), (x0, z1 - 0.8), (x1 - 0.8, z0), (x1 - 0.8, z1 - 0.8)):
                geo.box_minmax(m, (x, top - 6, z), (x + 0.8, top, z + 0.8), P.TOYBOX_RED, BUILD)
            for x0b, x1b, z0b, z1b in ((x0, x0 + 0.8, z0, z1), (x1 - 0.8, x1, z0, z1), (x0, x1, z0, z0 + 0.8), (x0, x1, z1 - 0.8, z1)):
                geo.box_minmax(m, (x0b, top - 6, z0b), (x1b, top, z1b), P.TOYBOX_RED, BUILD, skip=("bottom",))
            # lid propped open against the wall
            lid = [(x1 - 1.0, top, z0), (x1 - 1.0, top, z1), (x1 + 0.0, top + 15.0, z1), (x1 + 0.0, top + 15.0, z0)]
            geo.oriented(m, lid, (-1, 0.1, 0), P.TOYBOX_LID, BUILD)
            geo.oriented(m, [(p[0] - 0.8, p[1], p[2]) for p in lid], (1, 0, 0), P.TOYBOX_LID, BUILD)
            # star stickers on the front
            pl = Plane((x0, 0.0, 0.0), (-1, 0))
            for k, (zc, yc) in enumerate(((-32.0, -9.0), (-24.0, -6.0), (-18.0, -11.0))):
                u = (zc - pl.o[2]) / pl.r[2]
                pts = []
                for s in range(10):
                    a = math.pi / 2 + s * math.pi / 5
                    rr = 2.0 if s % 2 == 0 else 0.9
                    pts.append(pl.at(u + rr * math.cos(a), yc + rr * math.sin(a), 0.05))
                geo.oriented(m, pts, pl.n, (P.TOY_YELLOW, P.TOY_WHITE, P.TOY_BLUE)[k], BUILD)
            # toys spilling out
            ball(m, (56.5, top + 1.5, -29.0), 4.2, P.TOY_BLUE, rings=8, segs=14,
                 color_fn=lambda r, s: P.TOY_BLUE if (s // 3) % 2 else P.TOY_YELLOW)
            for k, (x, y, z, c) in enumerate(((53.5, top - 1.0, -20.0, P.TOY_GREEN), (60.0, top - 0.5, -18.5, P.TOY_RED),
                                              (47.0, FLOOR_Y + 1.5, -24.0, P.TOY_YELLOW), (46.0, FLOOR_Y + 1.5, -30.0, P.TOY_PURPLE))):
                geo.box(m, (x, y, z), (3.0, 3.0, 3.0), c, BUILD, yaw=0.4 * k)
            self.robot(m, (58.5, top - 2.0, -21.0), 0.5)

    def robot(self, m, base, yaw):
        x, y, z = base
        with geo.detail(m, 0.3):
            geo.box(m, (x, y + 3.0, z), (3.6, 4.0, 2.6), (0.70, 0.74, 0.80), BUILD, yaw=yaw)
            geo.box(m, (x, y + 6.6, z), (3.0, 2.6, 2.4), (0.78, 0.82, 0.88), BUILD, yaw=yaw)
            c, s = math.cos(yaw), math.sin(yaw)
            for ex in (-0.7, 0.7):
                ball(m, (x + ex * c - 1.25 * s, y + 6.9, z - ex * s - 1.25 * c), 0.42, (0.95, 0.25, 0.25), rings=4, segs=8)
            geo.cylinder_between(m, (x, y + 7.9, z), (x, y + 9.6, z), 0.15, 5, (0.4, 0.4, 0.45), BUILD)
            ball(m, (x, y + 9.8, z), 0.45, P.TOY_YELLOW, rings=4, segs=8)

    def desk(self, m, g):
        x0, x1, z0, z1, top = -65.5, -48.0, -15.0, 17.0, 2.0
        with geo.detail(m, 0.5):
            geo.box_minmax(m, (x0, top - 1.2, z0), (x1, top, z1), P.PAINT_WHITE, BUILD)
            for x, z in ((x1 - 1.6, z0 + 0.4), (x1 - 1.6, z1 - 2.0)):
                geo.box_minmax(m, (x, FLOOR_Y, z), (x + 1.6, top - 1.2, z + 1.6), P.PAINT_WHITE, BUILD, skip=("bottom",))
            geo.box_minmax(m, (x0, FLOOR_Y, z0 + 0.4), (x0 + 12.0, top - 1.2, z0 + 11.0), P.PAINT_WHITE, BUILD, skip=("bottom",))
            for k in range(3):
                y = top - 2.4 - k * 5.4
                geo.box_minmax(m, (x0 + 12.0, y - 4.6, z0 + 0.9), (x0 + 12.4, y, z0 + 10.5), P.BALLOONS[k * 2 % 7], BUILD)
                ball(m, (x0 + 12.7, y - 2.3, z0 + 5.7), 0.45, P.TRIM_WHITE, rings=4, segs=8)
            # crayon cup with crayons and pencils
            cyl(m, (-58.0, top, 8.0), 2.0, 4.0, P.TOY_BLUE, segs=14, top=False)
            cols = list(P.CRAYON.values())
            for k in range(9):
                a = 2 * math.pi * k / 9
                crayon(m, (-58.0 + 0.9 * math.cos(a), top + 0.8, 8.0 + 0.9 * math.sin(a)),
                       (-58.0 + 1.6 * math.cos(a), top + 7.5 + 0.6 * math.sin(k), 8.0 + 1.6 * math.sin(a)), 0.32, cols[k % len(cols)])
            # glue bottle, paint jars, paper stack, scissors
            cyl(m, (-55.0, top, -3.0), 1.3, 4.6, P.TOY_WHITE, segs=12)
            geo.lathe(m, (-55.0, top + 4.6, -3.0), [(1.3, 0.0), (0.4, 1.2), (0.25, 2.2)], 10, P.TOY_ORANGE, BUILD)
            for k, (zz, col) in enumerate(((1.0, P.TOY_RED), (3.2, P.TOY_YELLOW), (5.4, P.TOY_BLUE))):
                cyl(m, (-62.0, top, zz), 1.0, 1.6, (0.9, 0.92, 0.95), segs=10)
                cyl(m, (-62.0, top + 1.6, zz), 1.05, 0.6, col, segs=10)
            for k in range(6):
                geo.box(m, (-56.0, top + 0.12 + k * 0.12, -10.0), (8.9, 0.12, 6.3), P.PAPER if k % 2 else P.CARD_LEMON, BUILD, yaw=0.05 * k)
            # desk lamp
            cyl(m, (-62.0, top, 13.0), 2.0, 0.6, P.TOY_RED, segs=14)
            geo.cylinder_between(m, (-62.0, top + 0.6, 13.0), (-60.0, top + 9.0, 11.0), 0.35, 6, P.TOY_RED, BUILD)
            geo.cylinder_between(m, (-60.0, top + 9.0, 11.0), (-56.0, top + 8.0, 9.5), 0.35, 6, P.TOY_RED, BUILD)
            geo.lathe(m, (-56.0, top + 5.0, 9.5), [(2.6, 0.0), (1.2, 2.8), (0.6, 3.2)], 12, P.TOY_RED, BUILD, cap_top=True)
        with geo.state(g, alpha=1.0):
            ball(g, (-56.0, top + 5.5, 9.5), 1.0, (1.0, 0.92, 0.70), GLOW, rings=5, segs=10)
        # the kids' chair, pushed back
        self.chair(m, (-45.5, FLOOR_Y, 2.0), math.pi / 2, P.TOY_YELLOW)

    def chair(self, m, base, yaw, color):
        x, y, z = base
        c, s = math.cos(yaw), math.sin(yaw)

        def P_(u, w, h):
            return (x + u * c + w * s, y + h, z - u * s + w * c)

        with geo.detail(m, 0.5):
            for u in (-3.6, 3.6):
                for w in (-3.6, 3.6):
                    a, b = P_(u, w, 0.0), P_(u, w, 9.0)
                    geo.cylinder_between(m, a, b, 0.5, 6, color, BUILD)
            geo.box(m, P_(0.0, 0.0, 9.4), (8.6, 0.9, 8.6), color, BUILD, yaw=yaw)
            for u in (-3.6, 3.6):
                geo.cylinder_between(m, P_(u, -3.6, 9.8), P_(u, -3.6, 19.0), 0.5, 6, color, BUILD)
            geo.box(m, P_(0.0, -3.6, 17.2), (8.4, 3.4, 0.8), color, BUILD, yaw=yaw)

    def easel(self, m):
        cx, cz = -47.0, 52.0
        face = math.atan2(-cz, -cx)   # canvas faces the table
        nx, nz = math.cos(face), math.sin(face)
        rx, rz = -nz, nx
        with geo.detail(m, 0.5):
            for side in (-1, 1):
                geo.cylinder_between(m, (cx + rx * side * 6.0 + nx * 2.0, FLOOR_Y, cz + rz * side * 6.0 + nz * 2.0),
                                     (cx + rx * side * 2.0, 22.0, cz + rz * side * 2.0), 0.6, 6, P.PINE, BUILD)
            geo.cylinder_between(m, (cx - nx * 7.0, FLOOR_Y, cz - nz * 7.0), (cx, 20.0, cz), 0.6, 6, P.PINE_DARK, BUILD)
            pl = Plane((cx + nx * 0.8, 0.0, cz + nz * 0.8), (nx, nz))
            (u0, v0), (u1, v1) = textures.poster_uv("painting")
            pl.slab(m, -7.0, 2.0, 7.0, 20.0, 0.5, P.PAPER, uvs=((u0, v0), (u1, v1)))
            pl.slab(m, -8.0, 0.4, 8.0, 1.6, 2.4, P.PINE_DARK)
            for k, col in enumerate((P.TOY_RED, P.TOY_BLUE, P.TOY_YELLOW)):
                p = pl.at(-4.0 + k * 3.5, 1.6, 1.4)
                cyl(m, p, 0.8, 1.4, col, segs=8)

    def bookshelf(self, m):
        x0, x1, z0, z1, top = -65.5, -56.0, -58.0, -26.0, 22.0
        rng = self.rng
        with geo.detail(m, 0.5):
            geo.box_minmax(m, (x0, FLOOR_Y, z0), (x1, top, z0 + 1.0), P.PAINT_WHITE, BUILD, skip=("bottom",))
            geo.box_minmax(m, (x0, FLOOR_Y, z1 - 1.0), (x1, top, z1), P.PAINT_WHITE, BUILD, skip=("bottom",))
            geo.box_minmax(m, (x0, FLOOR_Y, z0), (x0 + 0.6, top, z1), (0.86, 0.90, 0.96), BUILD)
            shelves = [FLOOR_Y + 1.0, -3.0, 6.0, 15.0, top - 1.0]
            for y in shelves:
                geo.box_minmax(m, (x0, y, z0), (x1, y + 1.0, z1), P.PAINT_WHITE, BUILD)
            for k in range(len(shelves) - 1):
                y = shelves[k] + 1.0
                z = z0 + 1.2
                while z < z1 - 2.0:
                    if rng.random() < 0.12:
                        z += rng.uniform(1.5, 3.0)
                        continue
                    w = rng.uniform(0.8, 1.6)
                    h = rng.uniform(5.0, 7.8)
                    d = rng.uniform(5.5, 7.5)
                    col = geo.jitter(rng.choice(P.BALLOONS + P.BUNTING), rng, 0.1)
                    geo.box_minmax(m, (x0 + 0.7, y, z), (x0 + 0.7 + d, y + h, min(z1 - 1.1, z + w)), col, BUILD, skip=("bottom",))
                    z += w + 0.05
            # a globe and a rocket on top
            ball(m, (-61.0, top + 4.8, -48.0), 3.6, (0.30, 0.55, 0.88), rings=7, segs=12,
                 color_fn=lambda r, s: (0.35, 0.70, 0.35) if (r * 3 + s) % 5 == 0 else (0.30, 0.55, 0.88))
            cyl(m, (-61.0, top, -48.0), 1.2, 1.0, P.PINE_DARK, segs=8)
            geo.lathe(m, (-61.0, top, -34.0), [(1.8, 0.0), (1.8, 7.0), (1.2, 9.0), (0.0, 11.0)], 10,
                      [P.TOY_WHITE, P.TOY_RED, P.TOY_RED], BUILD)

    def brick(self, m, x, y, z, w, d, color, studs=True, yaw=0.0):
        """Chunky toy brick (2 x 1 x 0.6 m per unit stud pair) with studs on top."""
        h = 1.15
        geo.box(m, (x, y + h / 2, z), (w, h, d), color, BUILD, yaw=yaw, side_color=geo.scale(color, 0.9))
        if studs:
            c, s = math.cos(yaw), math.sin(yaw)
            nu, nv = max(1, round(w / 1.0)), max(1, round(d / 1.0))
            for i in range(nu):
                for j in range(nv):
                    u = -w / 2 + (i + 0.5) * w / nu
                    v = -d / 2 + (j + 0.5) * d / nv
                    cyl(m, (x + u * c + v * s, y + h, z - u * s + v * c), 0.32, 0.25, color, segs=8)

    def castle(self, m):
        """A toy-brick castle on a green baseplate in the south play corner."""
        bx0, bx1, bz0, bz1 = -22.0, 6.0, -68.0, -50.0
        rng = self.rng
        with geo.detail(m, 0.3):
            geo.box_minmax(m, (bx0, FLOOR_Y, bz0), (bx1, FLOOR_Y + 0.4, bz1), P.TOY_GREEN, BUILD, skip=("bottom",))
            for i in range(int(bx1 - bx0)):
                for j in range(int(bz1 - bz0)):
                    if (i + j) % 2 == 0:
                        cyl(m, (bx0 + i + 0.5, FLOOR_Y + 0.4, bz0 + j + 0.5), 0.3, 0.2, P.TOY_GREEN, segs=6, top=True)
            # walls: front (north, facing the room) and sides, two towers
            courses = 6
            for c_ in range(courses):
                y = FLOOR_Y + 0.4 + c_ * 1.15
                off = 1.0 if c_ % 2 else 0.0
                x = bx0 + 3.0 + off
                while x < bx1 - 4.0:
                    gate = abs(x - (bx0 + bx1) / 2) < 3.0 and c_ < 4
                    if not gate:
                        self.brick(m, x + 1.0, y, bz1 - 3.0, 2.0, 1.0, P.BRICKS[(c_ + int(x)) % 4], studs=c_ == courses - 1)
                    x += 2.0
                for z in range(int(bz0 + 3), int(bz1 - 3), 2):
                    for xx in (bx0 + 3.5, bx1 - 4.5):
                        self.brick(m, xx, y, z + 1.0, 1.0, 2.0, P.BRICKS[(c_ + z) % 4], studs=c_ == courses - 1)
            for tx in (bx0 + 3.5, bx1 - 4.5):
                for c_ in range(10):
                    y = FLOOR_Y + 0.4 + c_ * 1.15
                    self.brick(m, tx, y, bz1 - 3.0, 3.0, 3.0, (P.TOY_RED, P.TOY_YELLOW)[c_ % 2], studs=c_ == 9)
                geo.lathe(m, (tx, FLOOR_Y + 0.4 + 11.5, bz1 - 3.0), [(2.4, 0.0), (0.0, 4.5)], 8, P.TOY_BLUE, BUILD, cap_top=False)
            # flag
            geo.cylinder_between(m, (bx0 + 3.5, FLOOR_Y + 16.4, bz1 - 3.0), (bx0 + 3.5, FLOOR_Y + 20.5, bz1 - 3.0), 0.12, 5, P.TOY_WHITE, BUILD)

    def block_tower(self, m):
        """Alphabet blocks stacked by the castle (letters from the atlas)."""
        blocks = [(17.0, -56.0, 0.1, "A"), (17.4, -56.2, -0.15, "B"), (17.1, -55.9, 0.3, "C"), (24.0, -60.0, 0.6, "D")]
        size = 4.0
        heights = [0, 1, 2, 0]
        for k, (x, z, yaw, letter) in enumerate(blocks):
            y = FLOOR_Y + heights[k] * size + size / 2
            col = (P.TOY_RED, P.TOY_BLUE, P.TOY_GREEN, P.TOY_YELLOW)[k]
            with geo.detail(m, 0.4):
                geo.box(m, (x, y, z), (size, size, size), P.TOY_WHITE, BUILD, yaw=yaw, side_color=P.TOY_WHITE)
                c, s = math.cos(yaw), math.sin(yaw)
                (u0, v0), (u1, v1) = textures.letter_uv(letter)
                for (nx, nz) in ((0, 1), (1, 0), (0, -1), (-1, 0)):
                    wx, wz = nx * c + nz * s, -nx * s + nz * c
                    pl = Plane((x + wx * size / 2, 0.0, z + wz * size / 2), (wx, wz))
                    pl.rect(m, -size / 2 + 0.4, y - size / 2 + 0.4, size / 2 - 0.4, y + size / 2 - 0.4, col, off=0.02, uvs=((u0, v0), (u1, v1)))

    def train(self, m):
        """Wooden track across the play corner; the train itself loops via StageNeon travel."""
        z = -45.0
        xa, xb = -40.0, 40.0
        with geo.detail(m, 0.4):
            geo.box_minmax(m, (xa, FLOOR_Y, z - 1.6), (xb, FLOOR_Y + 0.5, z + 1.6), P.TRACK_WOOD, BUILD, skip=("bottom",))
            for side in (-0.8, 0.8):
                geo.box_minmax(m, (xa, FLOOR_Y + 0.5, z + side - 0.15), (xb, FLOOR_Y + 0.62, z + side + 0.15), P.PINE_DARK, BUILD)
            for x0 in (xa - 1.0, xb - 7.0):
                # tunnels hide the loop's wrap
                geo.box_minmax(m, (x0, FLOOR_Y, z - 3.2), (x0 + 8.0, FLOOR_Y + 1.0, z - 2.4), P.TOY_GREEN, BUILD)
                geo.box_minmax(m, (x0, FLOOR_Y, z + 2.4), (x0 + 8.0, FLOOR_Y + 1.0, z + 3.2), P.TOY_GREEN, BUILD)
                for k in range(9):
                    a0, a1 = math.pi * k / 9, math.pi * (k + 1) / 9
                    r0 = 3.2
                    q_out = [(x0, FLOOR_Y + 1.0 + r0 * math.sin(a0), z + r0 * math.cos(a0)), (x0 + 8.0, FLOOR_Y + 1.0 + r0 * math.sin(a0), z + r0 * math.cos(a0)),
                             (x0 + 8.0, FLOOR_Y + 1.0 + r0 * math.sin(a1), z + r0 * math.cos(a1)), (x0, FLOOR_Y + 1.0 + r0 * math.sin(a1), z + r0 * math.cos(a1))]
                    geo.oriented(m, q_out, (0, math.sin((a0 + a1) / 2), math.cos((a0 + a1) / 2)), P.TOY_GREEN, BUILD)
        mover = self.b.mover
        if mover is None:
            return
        # The rigid cars wrap at +-loop/2 = +-37 m, fully inside the tunnels (|x| 33..41).
        loop = 74.0
        speed = 4.5
        cars = [("engine", P.TOY_RED, 0.0), ("car", P.TOY_BLUE, -6.4), ("car", P.TOY_YELLOW, -12.4)]
        for kind, col, offset in cars:
            x = offset
            with geo.state(mover, alpha=0.0, travel=(speed, x), direction=(loop, 0.0)):
                geo.box(mover, (x, FLOOR_Y + 2.2, z), (5.2, 2.4, 2.6), col, GLOW, side_color=geo.scale(col, 0.88))
                if kind == "engine":
                    geo.box(mover, (x - 1.2, FLOOR_Y + 4.6, z), (2.4, 2.4, 2.6), col, GLOW)
                    geo.box(mover, (x - 1.2, FLOOR_Y + 6.0, z), (2.8, 0.4, 3.0), P.TOY_YELLOW, GLOW)
                    cyl(mover, (x + 1.6, FLOOR_Y + 3.4, z), 0.6, 2.2, (0.15, 0.15, 0.18), GLOW, segs=8)
                for wx in (-1.6, 1.6):
                    for wz in (-1.35, 1.35):
                        geo.cylinder_between(mover, (x + wx, FLOOR_Y + 1.1, z + wz), (x + wx, FLOOR_Y + 1.1, z + wz * 1.25), 0.9, 10,
                                             (0.20, 0.20, 0.22), GLOW)

    def teepee(self, m):
        cx, cz = -50.0, -58.0
        with geo.detail(m, 0.4):
            cols = [P.CURTAIN, P.TRIM_WHITE, P.TOY_BLUE, P.TRIM_WHITE]
            geo.lathe(m, (cx, FLOOR_Y, cz), [(9.0, 0.0), (6.0, 7.0), (3.0, 14.0), (0.6, 20.0)], 8, cols[:3], BUILD, cap_top=False)
            for k in range(5):
                a = 2 * math.pi * k / 5 + 0.3
                geo.cylinder_between(m, (cx + 0.6 * math.cos(a), 19.0 + FLOOR_Y + 1.0, cz + 0.6 * math.sin(a)),
                                     (cx + 1.6 * math.cos(a), FLOOR_Y + 25.0, cz + 1.6 * math.sin(a)), 0.3, 5, P.PINE, BUILD)

    # ------------------------------------------------------------ party
    def bunting(self, cl):
        """Pennant strings along the walls, inside the visible band above the furniture."""
        runs = [((-64.0, 34.0, 70.5), (-28.0, 35.0, 70.5)), ((28.0, 35.0, 70.5), (64.0, 34.0, 70.5)),
                ((-26.0, 43.0, 70.5), (26.0, 43.0, 70.5)),
                ((64.5, 42.0, 66.0), (64.5, 42.0, 2.0)), ((64.5, 30.0, -6.0), (64.5, 30.0, -68.0)),
                ((-64.5, 30.0, 66.0), (-64.5, 30.0, 22.0)), ((-64.5, 37.0, -20.0), (-64.5, 37.0, -68.0)),
                ((-62.0, 33.0, -70.5), (24.0, 33.0, -70.5)), ((56.0, 33.0, -70.5), (64.0, 33.0, -70.5))]
        for k, (a, b) in enumerate(runs):
            L = math.dist(a, b)
            n = max(2, int(L / 3.2))
            pts = geo.catenary(a, b, min(5.0, L * 0.08), n)
            with geo.state(cl, alpha=0.0):
                for i in range(len(pts) - 1):
                    geo.cylinder_between(cl, pts[i], pts[i + 1], 0.12, 4, P.STRING, CLOTH, caps=False)
            # outward normal = towards the room centre
            mx, mz = (a[0] + b[0]) / 2, (a[2] + b[2]) / 2
            nrm = (-mx, 0.0, -mz)
            for i in range(len(pts) - 1):
                p, q = pts[i], pts[i + 1]
                tip = ((p[0] + q[0]) / 2, (p[1] + q[1]) / 2 - 3.4, (p[2] + q[2]) / 2)
                col = P.BUNTING[(i + k) % len(P.BUNTING)]
                # alpha is the wind weight: the string corners hold still, the tip sways
                geo.oriented(cl, [p, q, tip], nrm, [geo.rgba(col, 0.0), geo.rgba(col, 0.0), geo.rgba(col, 1.0)], CLOTH)

    def balloons(self, cl):
        bunches = [((42.1, 27.0, 61.9), (45.0, 30.0, 57.0), 4), ((-48.5, 2.0, 16.5), (-54.0, 18.0, 16.0), 3),
                   ((22.0, -9.0, 46.0), (22.0, 6.0, 52.0), 3), ((-60.0, 22.0, -28.0), (-56.0, 30.0, -31.0), 3)]
        rng = self.rng
        for anchor, centre, n in bunches:
            for k in range(n):
                off = (rng.uniform(-3.5, 3.5), rng.uniform(-1.5, 3.5), rng.uniform(-3.5, 3.5))
                c = (centre[0] + off[0], centre[1] + off[1] + k * 1.2, centre[2] + off[2])
                col = rng.choice(P.BALLOONS)
                with geo.state(cl, alpha=1.0):
                    geo.ellipsoid(cl, c, (3.4, 4.0, 3.4), col, CLOTH, rings=7, segments=12,
                                  color_fn=lambda r, s, col=col: geo.mix(col, (1, 1, 1), 0.45) if (r == 5 and s in (2, 3)) else col)
                    geo.lathe(cl, (c[0], c[1] - 4.6, c[2]), [(0.0, 0.0), (0.55, 0.45), (0.2, 0.7)], 6, col, CLOTH, cap_top=False)
                pts = geo.catenary(anchor, (c[0], c[1] - 4.6, c[2]), 1.0, 8)
                for i in range(len(pts) - 1):
                    t = i / (len(pts) - 1)
                    with geo.state(cl, alpha=t * 0.9):
                        geo.cylinder_between(cl, pts[i], pts[i + 1], 0.07, 4, P.STRING, CLOTH, caps=False)

    def presents(self, m):
        items = [((22.0, 46.0), 8.0, 7.0, 0.3, 0), ((31.0, 52.0), 6.0, 5.0, -0.4, 1), ((14.0, 52.0), 5.0, 9.0, 0.1, 2),
                 ((-36.0, -40.0), 7.0, 6.0, 0.6, 3)]
        with geo.detail(m, 0.3):
            for (x, z), w, h, yaw, k in items:
                paper, ribbon = P.GIFT[k % len(P.GIFT)]
                geo.box(m, (x, FLOOR_Y + h / 2, z), (w, h, w), paper, BUILD, yaw=yaw)
                c, s = math.cos(yaw), math.sin(yaw)
                geo.box(m, (x, FLOOR_Y + h / 2, z), (w + 0.1, h + 0.1, 1.0), ribbon, BUILD, yaw=yaw)
                geo.box(m, (x, FLOOR_Y + h / 2, z), (1.0, h + 0.1, w + 0.1), ribbon, BUILD, yaw=yaw)
                for sgn in (-1, 1):
                    geo.ellipsoid(m, (x + sgn * 0.9 * c, FLOOR_Y + h + 0.6, z - sgn * 0.9 * s), (1.0, 0.6, 0.5), ribbon, BUILD, rings=4, segments=8)
        # a party hat on its side and the second chair
        geo.lathe(m, (-27.0, FLOOR_Y, 44.0), [(2.6, 0.0), (0.0, 7.0)], 12, P.BALLOONS[4], BUILD, cap_bottom=False)
        ball(m, (-27.0, FLOOR_Y + 7.3, 44.0), 0.7, P.TOY_WHITE, rings=4, segs=8)
        self.chair(m, (34.0, FLOOR_Y, 33.0), -2.4, P.TOY_BLUE)
        self.chair(m, (-27.0, FLOOR_Y, -47.0), 0.8, P.TOY_RED)

    # ------------------------------------------------------------ garden
    def garden(self, m, cl):
        rng = self.rng
        gy = GARDEN_Y
        with geo.detail(m, 0.6):
            # lawn with mowing stripes
            for i in range(-12, 12):
                for j in range(0, 14):
                    xa, xb = i * 50.0, (i + 1) * 50.0
                    za, zb = Z1 + WALL_T + j * 40.0, Z1 + WALL_T + (j + 1) * 40.0
                    col = P.GRASS if i % 2 else P.GRASS_DARK
                    geo.oriented(m, [(xa, gy, za), (xb, gy, za), (xb, gy, zb), (xa, gy, zb)], (0, 1, 0), geo.jitter(col, rng, 0.03), BUILD)
            # flower beds along the lawn, far enough out to show over the window sill
            for bx0, bx1 in ((-260.0, -70.0), (70.0, 260.0)):
                geo.box_minmax(m, (bx0, gy, 250.0), (bx1, gy + 3.0, 272.0), (0.42, 0.28, 0.18), BUILD, skip=("bottom",))
                for k in range(34):
                    x = rng.uniform(bx0 + 4, bx1 - 4)
                    z = rng.uniform(254.0, 268.0)
                    h = rng.uniform(14, 24)
                    geo.cylinder_between(m, (x, gy + 3.0, z), (x, gy + 3.0 + h, z), 0.7, 4, P.TREE, BUILD)
                    ball(m, (x, gy + 3.0 + h, z), rng.uniform(3.5, 5.5), rng.choice(P.FLOWERS), rings=4, segs=7)
            # sandbox
            geo.box_minmax(m, (-40.0, gy, 150.0), (40.0, gy + 6.0, 160.0), P.PINE, BUILD, skip=("bottom",))
            geo.box_minmax(m, (-40.0, gy, 220.0), (40.0, gy + 6.0, 230.0), P.PINE, BUILD, skip=("bottom",))
            geo.box_minmax(m, (-40.0, gy, 150.0), (-30.0, gy + 6.0, 230.0), P.PINE, BUILD, skip=("bottom",))
            geo.box_minmax(m, (30.0, gy, 150.0), (40.0, gy + 6.0, 230.0), P.PINE, BUILD, skip=("bottom",))
            geo.box_minmax(m, (-30.0, gy, 160.0), (30.0, gy + 4.5, 220.0), P.SAND, BUILD, skip=("bottom",))
            geo.lathe(m, (8.0, gy + 4.5, 185.0), [(6.0, 0.0), (7.5, 9.0)], 12, P.TOY_RED, BUILD, cap_top=False, cap_bottom=True)
            # swing set
            sx, sz = -150.0, 290.0
            for side in (-1, 1):
                for lean in (-1, 1):
                    geo.cylinder_between(m, (sx + side * 70.0, gy, sz + lean * 22.0), (sx + side * 64.0, gy + 78.0, sz), 2.2, 8, P.SWING_POST, BUILD)
            geo.cylinder_between(m, (sx - 66.0, gy + 78.0, sz), (sx + 66.0, gy + 78.0, sz), 2.4, 8, P.SWING_SEAT, BUILD)
            for k, x in enumerate((sx - 28.0, sx + 28.0)):
                for dx in (-9.0, 9.0):
                    geo.cylinder_between(m, (x + dx, gy + 77.0, sz), (x + dx, gy + 22.0, sz), 0.5, 4, (0.85, 0.82, 0.70), BUILD)
                geo.box(m, (x, gy + 21.0, sz), (22.0, 2.0, 8.0), (P.TOY_YELLOW, P.TOY_GREEN)[k], BUILD)
            # slide
            lx, lz = 170.0, 330.0
            for dx in (-10.0, 10.0):
                geo.cylinder_between(m, (lx + dx, gy, lz + 30.0), (lx + dx, gy + 60.0, lz + 30.0), 1.8, 6, P.SWING_SEAT, BUILD)
            geo.box(m, (lx, gy + 58.0, lz + 30.0), (24.0, 3.0, 16.0), P.SWING_SEAT, BUILD)
            chute = [(lx - 9.0, gy + 59.0, lz + 22.0), (lx + 9.0, gy + 59.0, lz + 22.0), (lx + 9.0, gy + 6.0, lz - 70.0), (lx - 9.0, gy + 6.0, lz - 70.0)]
            geo.oriented(m, chute, (0, 1, -0.4), P.SLIDE, BUILD)
            geo.oriented(m, [(p[0], p[1] - 1.5, p[2]) for p in chute], (0, -1, 0.4), geo.scale(P.SLIDE, 0.8), BUILD)
            # picket fence
            fz = 520.0
            x = -700.0
            while x < 700.0:
                geo.box_minmax(m, (x, gy, fz), (x + 9.0, gy + 34.0, fz + 3.0), P.FENCE, BUILD, skip=("bottom",))
                geo.lathe(m, (x + 4.5, gy + 34.0, fz + 1.5), [(6.4, 0.0), (0.0, 5.0)], 4, P.FENCE, BUILD, yaw=math.pi / 4, cap_top=False)
                x += 14.0
            for y in (gy + 9.0, gy + 25.0):
                geo.box_minmax(m, (-700.0, y, fz + 3.0), (700.0, y + 4.0, fz + 5.0), P.FENCE, BUILD)
        # trees (canopies sway gently)
        for k, (tx, tz, s) in enumerate(((-330.0, 640.0, 1.2), (300.0, 600.0, 1.0), (40.0, 760.0, 1.4),
                                         (-640.0, 820.0, 1.1), (620.0, 880.0, 1.3), (-90.0, 420.0, 0.7))):
            with geo.detail(m, 0.6):
                geo.cylinder_between(m, (tx, gy, tz), (tx, gy + 110.0 * s, tz), 12.0 * s, 10, P.TRUNK, BUILD, radius_b=8.0 * s)
            with geo.state(cl, alpha=0.25):
                for j in range(6):
                    a = 2 * math.pi * j / 6 + k
                    c = (tx + 45.0 * s * math.cos(a), gy + (130.0 + 25.0 * (j % 2)) * s, tz + 35.0 * s * math.sin(a))
                    geo.ellipsoid(cl, c, (70.0 * s, 58.0 * s, 70.0 * s), P.TREE if j % 2 else P.TREE_LIGHT, CLOTH, rings=6, segments=10)
                geo.ellipsoid(cl, (tx, gy + 175.0 * s, tz), (75.0 * s, 62.0 * s, 75.0 * s), P.TREE_LIGHT, CLOTH, rings=6, segments=10)

    def garden_far(self, m):
        """Neighbouring houses and hills, hazed towards the sky colour (StageNeon unlit)."""
        rng = self.rng

        def haze(col, dist):
            t = min(0.75, max(0.0, (dist - 600.0) / 2600.0))
            return geo.mix(col, P.SKY_HORIZON, t)

        with geo.state(m, alpha=0.0):
            for k in range(9):
                x = -1000.0 + k * 250.0 + rng.uniform(-40, 40)
                z = 1050.0 + rng.uniform(0, 160)
                w, d, h = rng.uniform(150, 210), 140.0, rng.uniform(120, 170)
                wall = haze(rng.choice(P.HOUSE_WALLS), z)
                roof = haze(rng.choice(P.ROOF_TILES), z)
                geo.box_minmax(m, (x - w / 2, GARDEN_Y, z), (x + w / 2, GARDEN_Y + h, z + d), wall, SKY, skip=("bottom",))
                ridge = GARDEN_Y + h + 70.0
                for sgn in (-1, 1):
                    geo.oriented(m, [(x - w / 2 - 8, GARDEN_Y + h, z + d / 2 + sgn * (d / 2 + 8)), (x + w / 2 + 8, GARDEN_Y + h, z + d / 2 + sgn * (d / 2 + 8)),
                                     (x + w / 2 + 8, ridge, z + d / 2), (x - w / 2 - 8, ridge, z + d / 2)], (0, 0.6, sgn), roof, SKY)
                for sx in (x - w / 2 - 8, x + w / 2 + 8):
                    geo.oriented(m, [(sx, GARDEN_Y + h, z - 8), (sx, GARDEN_Y + h, z + d + 8), (sx, ridge, z + d / 2)], (1 if sx > x else -1, 0, 0), wall, SKY)
                for wx in (-0.25, 0.25):
                    geo.box(m, (x + wx * w, GARDEN_Y + h * 0.55, z - 0.5), (28.0, 32.0, 1.0), haze((0.55, 0.70, 0.90), z), SKY)
            for layer, (z, amp, col, half) in enumerate(((1800.0, 220.0, P.HILL_FAR, 2400.0), (2400.0, 380.0, P.HILL_FARTHER, 2000.0))):
                n = 40
                for i in range(n):
                    x0 = -half + 2 * half * i / n
                    x1 = -half + 2 * half * (i + 1) / n
                    h0 = amp * (0.55 + 0.45 * math.sin(x0 * 0.0021 + layer * 2.0) * math.cos(x0 * 0.0013))
                    h1 = amp * (0.55 + 0.45 * math.sin(x1 * 0.0021 + layer * 2.0) * math.cos(x1 * 0.0013))
                    geo.oriented(m, [(x0, GARDEN_Y - 10, z), (x1, GARDEN_Y - 10, z), (x1, GARDEN_Y + h1, z), (x0, GARDEN_Y + h0, z)], (0, 0, -1), haze(col, z), SKY)
            # far lawn to the hills
            geo.oriented(m, [(-2400, GARDEN_Y - 0.5, 600.0), (2400, GARDEN_Y - 0.5, 600.0), (2000, GARDEN_Y - 0.5, 2400.0), (-2000, GARDEN_Y - 0.5, 2400.0)],
                         (0, 1, 0), haze(P.GRASS, 1400.0), SKY)

    def sky(self, m):
        """Sky dome over the window view, sun disc and drifting clouds (StageNeon unlit)."""
        R = 3200.0
        rings = [(-4, P.SKY_HORIZON), (6, P.SKY_HORIZON), (16, geo.mix(P.SKY_HORIZON, P.SKY_TOP, 0.4)), (32, P.SKY_TOP), (70, geo.scale(P.SKY_TOP, 0.92))]
        seg = 24
        with geo.state(m, alpha=0.0):
            for i in range(len(rings) - 1):
                for s in range(seg):
                    az0 = -math.pi / 2 + math.pi * s / seg
                    az1 = -math.pi / 2 + math.pi * (s + 1) / seg
                    e0, e1 = math.radians(rings[i][0]), math.radians(rings[i + 1][0])

                    def p(az, e):
                        return (R * math.cos(e) * math.sin(az), R * math.sin(e) + GARDEN_Y, R * math.cos(e) * math.cos(az))
                    q = [p(az0, e0), p(az1, e0), p(az1, e1), p(az0, e1)]
                    cols = [rings[i][1], rings[i][1], rings[i + 1][1], rings[i + 1][1]]
                    geo.oriented(m, q, (-q[0][0], 0, -q[0][2]), cols, SKY)
        # sun with a soft halo
        sun_dir = (math.sin(math.radians(-14)) * math.cos(math.radians(17)), math.sin(math.radians(17)), math.cos(math.radians(-14)) * math.cos(math.radians(17)))
        c = tuple(sun_dir[i] * (R - 60.0) for i in range(3))
        c = (c[0], c[1] + GARDEN_Y, c[2])
        right = (math.cos(math.radians(-14)), 0.0, -math.sin(math.radians(-14)))
        up = (-sun_dir[0] * sun_dir[1], 1 - sun_dir[1] ** 2, -sun_dir[2] * sun_dir[1])
        L = math.sqrt(sum(v * v for v in up))
        up = tuple(v / L for v in up)
        for radius, col, push, alpha in ((210.0, geo.mix(P.SUN, P.SKY_HORIZON, 0.55), 0.0, 0.3), (120.0, P.SUN, 6.0, 1.0)):
            pts = []
            for k in range(20):
                t = 2 * math.pi * k / 20
                pts.append(tuple(c[i] + (right[i] * math.cos(t) + up[i] * math.sin(t)) * radius - sun_dir[i] * push for i in range(3)))
            with geo.state(m, alpha=alpha):
                geo.oriented(m, pts, tuple(-v for v in sun_dir), col, SKY)
        # clouds: puffy clusters drifting east; the loop wraps far outside the window view
        mover = self.b.mover
        rng = self.rng
        if mover is None:
            return
        for k in range(9):
            cx = -2400.0 + k * 560.0 + rng.uniform(-120, 120)
            cz = 2000.0 + rng.uniform(-150, 150)
            cy = GARDEN_Y + rng.uniform(380, 900)
            with geo.state(mover, alpha=0.0, travel=(9.0, cx), direction=(5600.0, 0.0)):
                for j in range(5):
                    rr = rng.uniform(90, 160)
                    pts = []
                    ox, oy = (j - 2) * 120.0, (60.0 if j in (1, 2, 3) else 0.0)
                    for s in range(16):
                        t = 2 * math.pi * s / 16
                        pts.append((cx + ox + rr * 1.4 * math.cos(t), cy + oy + rr * 0.75 * math.sin(t), cz))
                    geo.oriented(mover, pts, (0, 0, -1), P.CLOUD if j % 2 == 0 else P.CLOUD_SHADE, SKY)

    # ------------------------------------------------------------ assembly
    def build_all(self):
        b = self.b
        b.below("Room Floor", "Rug", lambda m, g: self.rug(m))
        b.below("Room Floor", "Floor Clutter", lambda m, g: self.floor_clutter(m))
        b.background("Room Floor", "Floor Boards", self.floor)
        b.background("Room Walls", "Walls", self.walls)
        b.background("Wall Decals", "Wall Decals", self.wall_decals)
        b.background("Wall Decals", "Posters", self.posters)
        b.background("Room Walls", "Door", self.door)
        b.background("Room Walls", "Window Wall", lambda m: self.window_wall(m, m))
        b.background("Ceiling", "Ceiling", lambda m: self.ceiling(m, m))
        b.background("Bunk Corner", "Bunk Bed", lambda m: self.bunk_bed(m, m))
        b.background("Bunk Corner", "Toy Box", self.toy_box)
        b.background("Art Corner", "Desk", lambda m: self.desk(m, m))
        b.background("Art Corner", "Easel", self.easel)
        b.background("Art Corner", "Bookshelf", self.bookshelf)
        b.background("Play Corner", "Castle", self.castle)
        b.background("Play Corner", "Blocks", self.block_tower)
        b.background("Play Corner", "Train", self.train)
        b.background("Play Corner", "Teepee", self.teepee)
        b.background("Party", "Bunting", self.bunting)
        b.background("Party", "Balloons", self.balloons)
        b.background("Party", "Presents", self.presents)
        b.background("Garden", "Garden", lambda m: self.garden(m, m))
        b.background("Garden Far", "Garden Far", self.garden_far)
        b.background("Sky", "Sky", self.sky, movers="Sky")
