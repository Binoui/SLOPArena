"""The logged-off campsite north of the stump, at fighter scale (about 32 units/m).

Somebody came to the woods to log off: a campfire with marshmallow sticks, a dome
tent, a folding chair, a log seat with a lantern and an enamel mug, boots, a
backpack, a woodpile with the axe left in its chopping round, a NO WI-FI sign and,
at the stump's foot, the phone face up in the leaves showing NO SERVICE. The fire,
lantern, tent doorway and phone screen glow on StageNeon; all of it is background
beyond the camera envelope.
"""
from __future__ import annotations

import math

from stagekit import geo
import forest as F
import palette as P
import shapes as S
from stump import Stump
import textures as T

WOOD = "LO_Wood"
CLOTH = "LO_Cloth"
GLOW = "LO_Glow"

FIRE = (6.0, 92.0)
TENT = (-58.0, 150.0)
CHAIR = (40.0, 112.0)
SEAT = (-24.0, 82.0)
WOODPILE = (88.0, 52.0)
CHOPPING = (106.0, 36.0)
SIGN = (-34.0, 62.0)
PHONE = (12.0, -42.0)
LIGHTS = {
    "campfire": (FIRE[0], F.ground_y(*FIRE) + 9.0, FIRE[1]),
    "lantern": (SEAT[0] + 1.0, F.ground_y(*SEAT) + 13.0 + 5.0, SEAT[1] - 1.0),
    "tent": (TENT[0] + 10.0, F.ground_y(*TENT) + 14.0, TENT[1] - 12.0),
}


def facing(src, dst):
    """Yaw (radians, 0 = +Z) from src towards dst."""
    return math.atan2(dst[0] - src[0], dst[1] - src[1])


def local(origin, yaw):
    """Map local (x right, y up, z forward) to world for an object facing `yaw`."""
    c, s = math.cos(yaw), math.sin(yaw)
    right = (c, 0.0, -s)
    fwd = (s, 0.0, c)

    def to(p):
        return (origin[0] + right[0] * p[0] + fwd[0] * p[2], origin[1] + p[1], origin[2] + right[2] * p[0] + fwd[2] * p[2])
    return to, right, fwd


class Camp:
    def __init__(self, build):
        self.b = build
        self.rng = build.rng
        ground = next(m for _, name, m in build.checks if name == "Ground")
        self.ground = [(ground.verts[f[0]], ground.verts[f[i]], ground.verts[f[i + 1]])
                       for f in ground.faces for i in range(1, len(f) - 1)]
        self.contacts = {}

    def grounded_y(self, name, x, z, yaw, footprint):
        """Seat a rigid prop using its feet on the actual rendered ground triangles."""
        to, _, _ = local((x, 0.0, z), yaw)
        points = [to((u, 0.0, v)) for u, v in footprint]
        heights = [Stump.surface_y(self.ground, p[0], p[2]) for p in points]
        if any(h is None for h in heights):
            raise ValueError("No forest ground under " + name)
        y = min(heights) - 0.1
        self.contacts[name] = {"samples": len(heights), "maxGap": max(y - h for h in heights),
                               "maxEmbed": max(h - y for h in heights)}
        return y

    def build_all(self):
        b = self.b
        b.part("background", "Camp", "Campfire", self.campfire)
        b.part("background", "Camp Glow", "Flames", self.flames)
        b.part("background", "Camp", "Tent", self.tent)
        b.part("background", "Camp", "Chair", self.chair)
        b.part("background", "Camp", "Log Seat", self.log_seat)
        b.part("background", "Camp", "Boots And Pack", self.boots_and_pack)
        b.part("background", "Camp", "Woodpile", self.woodpile)
        b.part("background", "Camp", "Sign", self.sign)
        b.part("background", "Camp", "Phone", self.phone)
        b.ground_contacts = self.contacts

    # ------------------------------------------------------------ campfire
    def campfire(self, m):
        rng = self.rng
        fx, fz = FIRE
        gy = F.ground_y(fx, fz)
        with geo.state(m, alpha=0.5):
            ring = [(fx + 30 * math.sin(a), F.ground_y(fx + 30 * math.sin(a), fz + 30 * math.cos(a)) + 0.08,
                     fz + 30 * math.cos(a)) for a in [k * math.pi / 12 for k in range(24)]]
            S.fan(m, (fx, gy + 0.08, fz), ring, geo.mix(P.SOIL_DARK, P.ASH, 0.3), P.SOIL, (0, 1, 0), WOOD)
            ash = [(fx + 10.5 * math.sin(a), gy + 0.25, fz + 10.5 * math.cos(a)) for a in [k * math.pi / 10 for k in range(20)]]
            S.fan(m, (fx, gy + 0.35, fz), ash, P.CHAR, P.ASH, (0, 1, 0), WOOD)
            for k in range(14):
                a = 2 * math.pi * k / 14 + rng.uniform(-0.08, 0.08)
                sx, sy, sz = rng.uniform(3.4, 4.8), rng.uniform(2.4, 3.6), rng.uniform(3.0, 4.2)
                cx, cz = fx + 13.0 * math.sin(a), fz + 13.0 * math.cos(a)
                S.blob(m, (cx, gy + sy * 0.55, cz), (sx, sy, sz),
                       lambda n, p, cx=cx, cz=cz: geo.mix(P.STONE, P.STONE_DARK,
                                                          0.25 + 0.6 * max(0.0, -(n[0] * (cx - fx) + n[2] * (cz - fz)) / 13.0)),
                       rng, rings=4, segments=8, bump=0.12, floor=gy - 0.3, mat=WOOD)
            # Teepee logs, charred where they meet.
            apex = (fx + 0.4, gy + 9.5, fz - 0.3)
            for k in range(6):
                a = 2 * math.pi * k / 6 + 0.3
                foot = (fx + 7.0 * math.sin(a), gy + 1.0, fz + 7.0 * math.cos(a))
                top = S.lerp3(apex, foot, 0.12)
                pts = [foot, S.lerp3(foot, top, 0.5), top]
                S.tube(m, pts, rng.uniform(1.2, 1.6), 8,
                       lambda i, k_, n: P.CHAR if i >= 1 and n[1] > -0.2 else geo.mix(P.BARK, P.CHAR, 0.35 * i), WOOD,
                       cap_start=True)
            # Two marshmallow sticks resting on the stones.
            for a, toasted in ((2.2, False), (2.75, True)):
                base = (fx + 22 * math.sin(a), gy + 0.3, fz + 22 * math.cos(a))
                tip = (fx + 4.5 * math.sin(a), gy + 9.5, fz + 4.5 * math.cos(a))
                S.tube(m, [base, S.lerp3(base, tip, 0.5), tip], 0.2, 5, lambda i, k_, n: P.TWIG, WOOD, cap_start=True)
                d = geo._norm(S.sub(tip, base))
                mb = S.add(tip, S.mul(d, -1.6))
                col = geo.mix(P.MARSHMALLOW, (0.80, 0.55, 0.25), 0.7 if toasted else 0.0)
                S.tube(m, [S.add(mb, S.mul(d, -0.75)), S.add(mb, S.mul(d, 0.75))], 0.8, 8, lambda i, k_, n, c=col: c, WOOD,
                       cap_start=True)
            # A log bench between the fire and the tent.
            a0, a1 = (-26.0, 118.0), (8.0, 128.0)
            pts = [(x, F.ground_y(x, z) + 3.2, z) for x, z in (a0, S.lerp3((a0[0], 0, a0[1]), (a1[0], 0, a1[1]), 0.5)[::2], a1)]
            S.tube(m, pts, 3.6, 12, lambda i, k_, n: geo.mix(P.BARK, P.BARK_DARK, 0.3 * (k_ % 3) / 2), WOOD, cap_start=True)

    def flames(self, m):
        rng = self.rng
        fx, fz = FIRE
        gy = F.ground_y(fx, fz)
        for k in range(11):
            a = 2 * math.pi * k / 11 + rng.uniform(-0.3, 0.3)
            off = rng.uniform(0.0, 3.2) if k else 0.0
            base = (fx + off * math.sin(a), gy + 0.8, fz + off * math.cos(a))
            h = rng.uniform(11.0, 21.0) * (1.0 - off / 7.0) * (1.25 if k == 0 else 1.0)
            w = rng.uniform(2.4, 3.8) * (1.3 if k == 0 else 1.0)
            lean = rng.uniform(-0.12, 0.12)
            prof = [(w * 0.7, 0.0), (w, h * 0.18), (w * 0.85, h * 0.42), (w * 0.5, h * 0.68), (w * 0.2, h * 0.88), (0.02, h)]
            cols = [P.FLAME_CORE, P.FLAME_CORE, P.FLAME, P.FLAME, P.FLAME_TIP, P.FLAME_TIP]
            with geo.state(m, alpha=1.0, anim=(50.0 + rng.uniform(1.8, 3.4), rng.random())):
                S.lathe(m, base, prof, 8, lambda b, k_, cols=cols: cols[b], GLOW, yaw=rng.uniform(0, 1),
                        cap_top=False, cap_bottom=True,
                        wobble=lambda b, k_, lean=lean: 1.0 + lean * b * math.cos(k_ * 0.8))
        for _ in range(14):
            x, z = fx + rng.uniform(-7, 7), fz + rng.uniform(-7, 7)
            if math.hypot(x - fx, z - fz) > 8.5:
                continue
            s = rng.uniform(0.5, 1.1)
            with geo.state(m, alpha=1.0, anim=(50.0 + rng.uniform(0.3, 0.9), rng.random())):
                S.blob(m, (x, gy + 0.45, z), (s, s * 0.5, s * 0.8), lambda n, p: P.EMBER, None, rings=3, segments=6, mat=GLOW)

    # ------------------------------------------------------------ tent
    def tent(self, m):
        rng = self.rng
        tx, tz = TENT
        yaw = facing(TENT, FIRE)
        W, H, D = 34.0, 40.0, 30.0
        footprint = [(W * math.sin(k * math.pi / 16), D * math.cos(k * math.pi / 16)) for k in range(32)]
        gy = self.grounded_y("Tent hem", tx, tz, yaw, footprint)
        to, right, fwd = local((tx, gy, tz), yaw)
        rings, seg = 9, 32
        grid = []
        for r in range(rings + 1):
            phi = (math.pi / 2) * r / rings
            row = []
            for s in range(seg):
                th = 2 * math.pi * s / seg
                sag = 1.0 - 0.07 * math.sin(2 * th) ** 2 * math.sin(phi * 2)
                rr = (math.cos(phi) ** 0.75) * sag
                p = to((W * rr * math.sin(th), H * math.sin(phi) ** 1.2, D * rr * math.cos(th)))
                row.append(p)
            grid.append(row)

        def door(s, r):
            th = 2 * math.pi * s / seg
            return math.cos(th) > 0.82 and r <= 5

        with geo.state(m, alpha=0.2):
            for r in range(rings):
                for s in range(seg):
                    s2 = (s + 1) % seg
                    q = [grid[r][s], grid[r][s2], grid[r + 1][s2], grid[r + 1][s]]
                    centre = [sum(p[i] for p in q) / 4 for i in range(3)]
                    out = (centre[0] - tx, centre[1] - gy + 4.0, centre[2] - tz)
                    if door(s, r) and door(s2, r):
                        continue
                    col = P.TENT_DARK if r == 0 else (P.TENT if (s // 4) % 2 == 0 else geo.scale(P.TENT, 0.9))
                    geo.oriented(m, q, out, col, CLOTH)
            # Poles: two crossing arcs just outside the fabric.
            for th0 in (math.pi / 4, 3 * math.pi / 4):
                pts = []
                for r in range(rings * 2 + 1):
                    phi = math.pi * r / (rings * 2)
                    side = 1 if phi <= math.pi / 2 else -1
                    ph = phi if side > 0 else math.pi - phi
                    rr = math.cos(ph) ** 0.75 * 1.015
                    th = th0 if side > 0 else th0 + math.pi
                    pts.append(to((W * rr * math.sin(th), H * math.sin(ph) ** 1.2 + 0.3, D * rr * math.cos(th))))
                S.tube(m, pts, 0.35, 5, lambda i, k, n: P.TENT_POLE, WOOD, cap_start=True)
            # Guy lines to stakes.
            for th in (math.pi / 4, 3 * math.pi / 4, 5 * math.pi / 4, 7 * math.pi / 4):
                top = to((W * 0.62 * math.sin(th), H * 0.62, D * 0.62 * math.cos(th)))
                stake = to((W * 1.45 * math.sin(th), 0.6, D * 1.45 * math.cos(th)))
                stake = (stake[0], Stump.surface_y(self.ground, stake[0], stake[2]) + 0.3, stake[2])
                S.tube(m, [top, stake], 0.09, 4, lambda i, k, n: P.GUY_LINE, WOOD)
                S.tube(m, [S.add(stake, (0, -0.6, 0)), S.add(stake, (0, 1.2, 0))], 0.25, 5, lambda i, k, n: P.TENT_POLE, WOOD)
            # Rolled-up door flap above the opening.
            roll = [to((W * x * 0.3, H * 0.66, D * 0.66)) for x in (-1.0, -0.5, 0.0, 0.5, 1.0)]
            S.tube(m, roll, 1.4, 8, lambda i, k, n: P.TENT_DARK, CLOTH, cap_start=True)
        # The doorway glows warm from the lantern inside.
        door_pts = []
        for s in range(seg):
            if door(s, 0):
                door_pts.append(s)
        with geo.state(m, alpha=0.55, anim=(50.0 + 0.35, 0.2)):
            arch = []
            for k in range(13):
                t = k / 12
                th = -0.6 + 1.2 * t
                phi = (math.pi / 2) * 5 / rings * math.sin(math.pi * t) ** 0.5
                rr = math.cos(phi) ** 0.75 * 0.985
                arch.append(to((W * rr * math.sin(th), H * math.sin(phi) ** 1.2 + 0.05, D * rr * math.cos(th))))
            centre = to((0.0, 4.0, D * 0.9))
            S.fan(m, centre, arch, P.TENT_INSIDE, geo.scale(P.TENT_INSIDE, 0.7), fwd, GLOW, closed=True)

    # ------------------------------------------------------------ chair, seat, gear
    def chair(self, m):
        cx, cz = CHAIR
        yaw = facing(CHAIR, FIRE)
        gy = self.grounded_y("Chair feet", cx, cz, yaw, [(x, z) for x in (-8.5, 8.5) for z in (-7.0, 6.5)])
        to, right, fwd = local((cx, gy, cz), yaw)
        tube = lambda pts, r=0.36: S.tube(m, [to(p) for p in pts], r, 6, lambda i, k, n: P.CHAIR_FRAME, WOOD, cap_start=True)
        with geo.state(m, alpha=0.2):
            for sx in (-1, 1):
                tube([(sx * 8.5, 0.0, -7.0), (sx * 8.5, 14.0, 6.5)])
                tube([(sx * 8.5, 0.0, 6.5), (sx * 8.5, 14.0, -7.0)])
                tube([(sx * 8.5, 14.0, -7.5), (sx * 9.2, 27.5, -10.0)])
                tube([(sx * 9.0, 20.5, -8.5), (sx * 9.0, 20.5, 6.0)], 0.5)
            tube([(-8.5, 0.0, -7.0), (8.5, 0.0, -7.0)])
            tube([(-8.5, 0.0, 6.5), (8.5, 0.0, 6.5)])
        with geo.state(m, alpha=0.25):
            seat = []
            for j in range(5):
                v = j / 4
                z = -7.0 + 13.5 * v
                row = []
                for i in range(5):
                    u = i / 4
                    x = -8.4 + 16.8 * u
                    sag = 1.6 * math.sin(math.pi * u) * math.sin(math.pi * v)
                    row.append(to((x, 14.0 - sag, z)))
                seat.append(row)
            for j in range(4):
                for i in range(4):
                    q = [seat[j][i], seat[j][i + 1], seat[j + 1][i + 1], seat[j + 1][i]]
                    geo.oriented(m, q, (0, 1, 0), P.CHAIR_FABRIC, CLOTH)
            back = [[to((-8.4 + 16.8 * i / 4, 14.5 + 12.5 * j / 3, -7.6 - 2.2 * j / 3 + 0.9 * math.sin(math.pi * i / 4))) for i in range(5)]
                    for j in range(4)]
            for j in range(3):
                for i in range(4):
                    q = [back[j][i], back[j][i + 1], back[j + 1][i + 1], back[j + 1][i]]
                    geo.oriented(m, q, fwd, geo.scale(P.CHAIR_FABRIC, 0.92), CLOTH)
            # Cup holder on the right armrest.
            S.lathe(m, to((10.6, 18.5, 2.0)), [(1.4, 0.0), (1.5, 2.2)], 8, lambda b, k: geo.scale(P.CHAIR_FABRIC, 0.7), CLOTH,
                    cap_top=False, cap_bottom=True)

    def mug(self, m, base):
        x, y, z = base
        S.lathe(m, base, [(1.25, 0.0), (1.32, 2.9), (1.36, 3.05), (1.18, 3.05), (1.16, 0.4), (0.01, 0.4)], 12,
                lambda b, k: [P.MUG, P.MUG_RIM, P.MUG_RIM, P.MUG, (0.18, 0.10, 0.05)][min(b, 4)], WOOD,
                cap_top=False, cap_bottom=True)
        S.tube(m, [(x + 1.3, y + 2.4, z), (x + 2.2, y + 2.1, z), (x + 2.3, y + 1.3, z), (x + 1.3, y + 0.8, z)], 0.24, 6,
               lambda i, k, n: P.MUG, WOOD, cap_start=True)

    def log_seat(self, m):
        sx, sz = SEAT
        gy = F.ground_y(sx, sz)
        top = gy + 13.0
        with geo.state(m, alpha=0.5):
            S.lathe(m, (sx, gy - 0.5, sz), [(6.4, 0.0), (6.0, 2.0), (6.0, 13.5)], 18,
                    lambda b, k: geo.mix(P.BARK, P.BARK_DARK, 0.3 * (k % 3) / 2), WOOD, cap_top=False)
            (u0, v0), (u1, v1) = T.region_uv("endgrain")
            ring, uvs = [], []
            for k in range(18):
                a = 2 * math.pi * k / 18
                ring.append((sx + 6.0 * math.cos(a), top + 0.5, sz + 6.0 * math.sin(a)))
                uvs.append((u0 + (u1 - u0) * (0.5 + 0.47 * math.cos(a)), v0 + (v1 - v0) * (0.5 + 0.47 * math.sin(a))))
            cuv = ((u0 + u1) / 2, (v0 + v1) / 2)
            for k in range(18):
                k2 = (k + 1) % 18
                geo.oriented(m, [(sx, top + 0.5, sz), ring[k], ring[k2]], (0, 1, 0), (1.0, 1.0, 1.0), WOOD, [cuv, uvs[k], uvs[k2]])
            # Lantern: base, glass, guard wires, cap and bail.
            lx, ly, lz = sx + 1.0, top + 0.5, sz - 1.0
            S.lathe(m, (lx, ly, lz), [(2.1, 0.0), (2.1, 1.1), (1.5, 1.4)], 12, lambda b, k: P.LANTERN, WOOD, cap_top=True)
            for k in range(4):
                a = 2 * math.pi * k / 4 + 0.4
                S.tube(m, [(lx + 1.75 * math.cos(a), ly + 1.2, lz + 1.75 * math.sin(a)),
                           (lx + 1.85 * math.cos(a), ly + 3.4, lz + 1.85 * math.sin(a)),
                           (lx + 1.6 * math.cos(a), ly + 5.6, lz + 1.6 * math.sin(a))], 0.09, 4, lambda i, k_, n: P.LANTERN, WOOD)
            S.lathe(m, (lx, ly + 5.5, lz), [(2.0, 0.0), (1.8, 0.6), (0.6, 1.6), (0.5, 2.2)], 12, lambda b, k: P.LANTERN, WOOD,
                    cap_top=True, cap_bottom=True)
            bail = [(lx - 1.9, ly + 5.8, lz), (lx - 1.6, ly + 8.0, lz), (lx, ly + 9.0, lz), (lx + 1.6, ly + 8.0, lz), (lx + 1.9, ly + 5.8, lz)]
            S.tube(m, bail, 0.12, 4, lambda i, k_, n: P.BRASS, WOOD)
            self.mug(m, (sx - 3.2, top + 0.5, sz + 2.6))
        with geo.state(m, alpha=1.0, anim=(50.0 + 0.25, 0.6)):
            S.blob(m, (lx, ly + 3.4, lz), (1.45, 2.05, 1.45), lambda n, p: P.LANTERN_GLOW, None, rings=6, segments=10, mat=GLOW)

    def boot(self, m, origin, yaw):
        to, right, fwd = local(origin, yaw)
        S.frame_box(m, to((0.0, 0.35, 0.6)), (right, (0, 1, 0), fwd), (1.9, 0.35, 5.0), P.SOLE, WOOD)
        S.blob(m, to((0.0, 1.9, 1.6)), (1.8, 1.5, 3.6), lambda n, p: P.BOOT, None, rings=5, segments=9, floor=origin[1] + 0.6, mat=WOOD)
        S.lathe(m, to((0.0, 0.7, -2.0)), [(2.0, 0.0), (2.0, 3.0), (1.85, 6.2), (1.95, 6.6)], 10,
                lambda b, k: P.BOOT_DARK if b == 3 else P.BOOT, WOOD, cap_top=True)
        for k in range(4):
            z = 0.2 + 1.2 * k
            S.tube(m, [to((-1.2, 2.9 + 0.35 * k - 0.0, z - 1.0)), to((1.2, 2.9 + 0.35 * k, z - 1.0))], 0.13, 4,
                   lambda i, k_, n: P.LACE, WOOD)

    def boots_and_pack(self, m):
        cx, cz = CHAIR
        with geo.state(m, alpha=0.35):
            for (dx, dz, dyaw) in ((10.0, -10.0, 0.4), (14.5, -5.0, -0.2)):
                x, z = cx + dx, cz + dz
                yaw = facing(CHAIR, FIRE) + dyaw
                gy = self.grounded_y("Boot %.1f" % dx, x, z, yaw, [(u, v) for u in (-1.9, 1.9) for v in (-4.4, 5.6)])
                self.boot(m, (x, gy, z), yaw)
            px, pz = -30.0, 128.0
            gy = F.ground_y(px, pz)
            to, right, fwd = local((px, gy, pz), facing((px, pz), FIRE))
            S.blob(m, to((0.0, 8.5, 0.0)), (5.6, 8.8, 3.8), lambda n, p: P.PACK if n[1] > -0.6 else P.PACK_DARK, None,
                   rings=6, segments=10, floor=gy, mat=WOOD)
            S.blob(m, to((0.0, 16.5, -0.4)), (5.2, 2.2, 3.9), lambda n, p: P.PACK_DARK, None, rings=4, segments=10, mat=WOOD)
            S.blob(m, to((0.0, 6.0, 3.6)), (3.8, 3.6, 1.6), lambda n, p: P.PACK_DARK, None, rings=4, segments=8, mat=WOOD)
            S.tube(m, [to((-6.5, 19.5, -0.6)), to((6.5, 19.5, -0.6))], 2.4, 10, lambda i, k, n: P.CHAIR_FABRIC, WOOD, cap_start=True)
            for sx in (-2.4, 2.4):
                S.tube(m, [to((sx, 15.0, -3.6)), to((sx * 1.1, 9.0, -4.6)), to((sx, 3.0, -3.8))], 0.5, 5, lambda i, k, n: P.STRAP, WOOD)

    # ------------------------------------------------------------ woodpile and axe
    def billet(self, m, origin, U, V, W, R, L, rng):
        arc = [(R * math.cos(a), R * math.sin(a)) for a in [k * math.pi / 2 / 5 for k in range(6)]]
        outline = [(0.0, 0.0)] + arc

        def side(i):
            if 1 <= i <= 4:
                return geo.mix(P.BARK, P.BARK_DARK, rng.random() * 0.5)
            return geo.jitter(P.SPLIT_WOOD, rng, 0.06)

        S.extrude(m, outline, origin, U, V, W, L, side, geo.mix(P.EARLYWOOD, P.LATEWOOD, 0.3), WOOD)

    def woodpile(self, m):
        rng = self.rng
        wx, wz = WOODPILE
        gy = F.ground_y(wx, wz)
        yaw = facing(WOODPILE, (0.0, 0.0)) + math.pi / 2
        W = (math.sin(yaw), 0.0, math.cos(yaw))
        side_axis = (math.cos(yaw), 0.0, -math.sin(yaw))
        with geo.state(m, alpha=0.4):
            for row, count in enumerate((4, 3, 2)):
                for i in range(count):
                    off = (i - (count - 1) / 2) * 7.6
                    base = (wx + side_axis[0] * off, gy + 0.2 + row * 5.4, wz + side_axis[2] * off)
                    a = rng.uniform(0, 2 * math.pi)
                    U = (side_axis[0] * math.cos(a), math.sin(a), side_axis[2] * math.cos(a))
                    V = geo._cross(W, U)
                    start = S.add(base, S.mul(W, -6.5 + rng.uniform(-0.8, 0.8)))
                    centre = S.add(start, S.add(S.mul(U, -1.8), S.mul(V, -1.8)))
                    centre = (centre[0], max(centre[1], gy + 0.1), centre[2])
                    self.billet(m, centre, U, V, W, 5.0, 13.0, rng)
            # The chopping round, the axe left in it, and chips around.
            cx, cz = CHOPPING
            cy = F.ground_y(cx, cz)
            top = cy + 12.0
            S.lathe(m, (cx, cy - 0.5, cz), [(7.4, 0.0), (7.0, 1.8), (7.0, 12.5)], 20,
                    lambda b, k: geo.mix(P.BARK, P.BARK_DARK, 0.3 * (k % 3) / 2), WOOD, cap_top=False)
            (u0, v0), (u1, v1) = T.region_uv("endgrain")
            ring, uvs = [], []
            for k in range(20):
                a = 2 * math.pi * k / 20
                ring.append((cx + 7.0 * math.cos(a), top, cz + 7.0 * math.sin(a)))
                uvs.append((u0 + (u1 - u0) * (0.5 + 0.47 * math.cos(a)), v0 + (v1 - v0) * (0.5 + 0.47 * math.sin(a))))
            cuv = ((u0 + u1) / 2, (v0 + v1) / 2)
            for k in range(20):
                k2 = (k + 1) % 20
                geo.oriented(m, [(cx, top, cz), ring[k], ring[k2]], (0, 1, 0), (1.0, 1.0, 1.0), WOOD, [cuv, uvs[k], uvs[k2]])
            # Axe: handle rises away from the stump at 32 degrees; the blade bites the round.
            out = geo._norm((cx, 0.0, cz))
            pitch = math.radians(32)
            hdir = (out[0] * math.cos(pitch), math.sin(pitch), out[2] * math.cos(pitch))
            blade = geo._norm(S.sub((0.0, -1.0, 0.0), S.mul(hdir, -math.sin(pitch))))
            blade = geo._norm(S.sub(blade, S.mul(hdir, S.dot(blade, hdir))))
            side = geo._cross(hdir, blade)
            eye = (cx - out[0] * 1.0, top + 1.6, cz - out[2] * 1.0)
            head_centre = S.add(eye, S.mul(blade, 1.6))
            S.frame_box(m, head_centre, (blade, hdir, side), (2.6, 1.7, 0.55),
                        {"+x": P.AXE_EDGE, "-x": P.AXE_HEAD, "+y": P.AXE_HEAD, "-y": P.AXE_HEAD, "+z": P.AXE_HEAD, "-z": P.AXE_HEAD},
                        WOOD)
            S.frame_box(m, S.add(eye, S.mul(blade, -1.7)), (blade, hdir, side), (0.9, 1.5, 0.75), P.AXE_HEAD, WOOD)
            handle = [S.add(eye, S.mul(hdir, t)) for t in (-1.0, 4.0, 10.0, 16.0, 20.0, 21.5)]
            S.tube(m, handle, [0.55, 0.55, 0.52, 0.56, 0.62, 0.7], 8,
                   lambda i, k, n: P.AXE_PAINT if i >= 4 else P.AXE_HANDLE, WOOD, cap_start=True, squash=0.75)
            for _ in range(26):
                a = rng.uniform(0, 2 * math.pi)
                r = rng.uniform(8.0, 16.0)
                x, z = cx + r * math.cos(a), cz + r * math.sin(a)
                S.leaf(m, (x, F.ground_y(x, z) + 0.05, z), rng.uniform(0.8, 1.8), rng.uniform(0.5, 0.9), rng.uniform(0, 6.3),
                       geo.jitter(P.CHIP, rng, 0.06), curl=0.06, mat=WOOD, steps=3)

    # ------------------------------------------------------------ sign and phone
    def sign(self, m):
        sx, sz = SIGN
        gy = F.ground_y(sx, sz)
        yaw = facing(SIGN, (0.0, 0.0))
        to, right, fwd = local((sx, gy, sz), yaw)
        with geo.state(m, alpha=0.45):
            S.frame_box(m, to((0.0, 16.0, -0.9)), (right, (0, 1, 0), fwd), (0.9, 17.5, 0.9), P.SIGN_POST, WOOD)
            S.frame_box(m, to((0.0, 27.5, 0.0)), (right, (0, 1, 0), fwd), (8.0, 4.0, 0.3), P.SIGN_BOARD, WOOD, skip=("+z",))
            (u0, v0), (u1, v1) = T.region_uv("sign")
            quad = [to((-8.0, 23.5, 0.31)), to((8.0, 23.5, 0.31)), to((8.0, 31.5, 0.31)), to((-8.0, 31.5, 0.31))]
            # Seen from the front, the board's local +x is on the viewer's left: mirror u.
            geo.oriented(m, quad, fwd, (1.0, 1.0, 1.0), WOOD, [(u1, v0), (u0, v0), (u0, v1), (u1, v1)])

    def phone(self, m):
        px, pz = PHONE
        yaw = facing((0.0, 0.0), PHONE) + 0.3  # the screen's top points away from the stump: it reads upright from the rim
        gy = self.grounded_y("Phone corners", px, pz, yaw, [(u, v) for u in (-1.2, 1.2) for v in (-2.5, 2.5)])
        to, right, fwd = local((px, gy, pz), yaw)
        with geo.state(m, alpha=0.1):
            S.frame_box(m, to((0.0, 0.2, 0.0)), (right, (0, 1, 0), fwd), (1.2, 0.16, 2.5),
                        {"+y": P.PHONE, "-y": P.PHONE, "+x": P.PHONE_EDGE, "-x": P.PHONE_EDGE, "+z": P.PHONE_EDGE, "-z": P.PHONE_EDGE},
                        WOOD)
        (u0, v0), (u1, v1) = T.region_uv("phone")
        with geo.state(m, alpha=1.0):
            quad = [to((-1.05, 0.37, -2.3)), to((1.05, 0.37, -2.3)), to((1.05, 0.37, 2.3)), to((-1.05, 0.37, 2.3))]
            geo.oriented(m, quad, (0, 1, 0), (1.0, 1.0, 1.0), GLOW, [(u0, v0), (u1, v0), (u1, v1), (u0, v1)])
