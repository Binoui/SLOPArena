"""The underground venue: the slop pit, four terraced stands packed with fans, the
lighting rig and the corner towers.

Everything above two metres under the kill plane stays at least ENVELOPE (15 m) outside
the blast corridor, so the orbit camera can never clip into it: the stands start at the
pit wall (w = PIT_W). Inside that envelope only the slop and its junk exist, far below.
Stands are authored once in a `parts.Side` frame (u along, w outward) and turned by
quarter turns; side 0 faces the default camera's north.
"""
from __future__ import annotations

import math
import random

from stagekit import font, geo
import palette as P
import parts
import textures as T
from parts import Side

LIT, FLOOR, CANVAS, VENUE, HAZE = "SP_Lit", "SP_Floor", "SP_Canvas", "SP_Venue", "SP_Haze"
SLOP_Y = -24.0
RIM_Y = -6.0
PIT_W = 41.5            # pit wall and front of the stands
SPAN = 41.5             # stands run u in [-SPAN, SPAN]; corner towers fill the rest
GALLERIES = ((-1.0, 45.0), (4.0, 48.5), (9.0, 52.0))  # (deck y, front w)
DEPTH = 3.5
BACK_W = 55.5
CEILING_Y = 18.0
FASCIA_Y = 16.4
TRUSS_W, TRUSS_Y = 43.2, 15.0
LANTERN_TOP = 30.0
BAY = 2.5
TARGET = (0.0, 1.2, 0.0)  # the lights aim at the ring
NAMES = ("North", "East", "South", "West")
BANNERS = {0: [(-24.0, "crown"), (24.0, "crown_slop")],
           1: [(-24.0, "fightguy"), (-8.0, "manki"), (8.0, "crown"), (24.0, "bonk")],
           2: [(-24.0, "crown_slop"), (-8.0, "wibou"), (8.0, "bonk"), (24.0, "crown")],
           3: [(-24.0, "manki"), (-8.0, "crown"), (8.0, "crown_slop"), (24.0, "fightguy")]}
HERO = (-11.5, 11.5)   # north gallery 2: the SLOP PIT neon sign replaces the crowd here


def disc(m, center, normal, radius, segments, color, mat=VENUE):
    n = geo._norm(normal)
    ref = (0.0, 1.0, 0.0) if abs(n[1]) < 0.9 else (1.0, 0.0, 0.0)
    a = geo._norm(geo._cross(n, ref))
    b = geo._cross(n, a)
    pts = [tuple(center[i] + (a[i] * math.cos(t) + b[i] * math.sin(t)) * radius for i in range(3))
           for t in (2 * math.pi * k / segments for k in range(segments))]
    geo.oriented(m, pts, n, color, mat)


def toward(p, q):
    d = [q[i] - p[i] for i in range(3)]
    L = math.sqrt(sum(x * x for x in d)) or 1.0
    return tuple(x / L for x in d)


class Venue:
    def __init__(self, build):
        self.b = build
        self.rng = random.Random(20261016 + 2)
        self.sides = [Side(k) for k in range(4)]

    # ------------------------------------------------------------ the pit
    @staticmethod
    def murk(x, z):
        """0..1 swirl pattern of the slop surface (slow overlapping waves)."""
        v = (math.sin(x * 0.21 + z * 0.13) + math.sin(x * 0.07 - z * 0.19 + 1.3)
             + math.sin((x + z) * 0.11 + 0.4) + 0.5 * math.sin(x * 0.43 - z * 0.37))
        return min(1.0, max(0.0, 0.5 + v / 7.0))

    def slop_color(self, x, z):
        r = math.hypot(x, z)
        c = geo.mix(P.SLOP_DARK, P.SLOP, self.murk(x, z))
        return geo.mix(c, P.SLOP_DARK, min(1.0, max(0.0, (r - 30.0) / 14.0)) * 0.6)

    def slop(self, m, g):
        """Murky swirled slop, soft glowing sheen, small domed bubbles and junk (all far below
        the kill plane)."""
        rng = self.rng
        n = 30
        step = 2 * PIT_W / n
        for i in range(n):
            for j in range(n):
                x0, z0 = -PIT_W + i * step, -PIT_W + j * step
                geo.vertex_grid(m, (x0, SLOP_Y, z0), (0, 0, step), (step, 0, 0), 1, 1,
                                lambda p: self.slop_color(p[0], p[2]), VENUE)
        for _ in range(46):  # soft sheen: glowing centres fading into the surrounding slop
            r, a = rng.uniform(20, 39.5), rng.uniform(0, 2 * math.pi)
            cx, cz = r * math.cos(a), r * math.sin(a)
            rad = rng.uniform(1.2, 4.0)
            centre = (cx, SLOP_Y + 0.02, cz)
            rim = []
            for k in range(12):
                t = 2 * math.pi * k / 12
                rr = rad * rng.uniform(0.65, 1.0)
                rim.append((cx + rr * math.cos(t), SLOP_Y + 0.02, cz + rr * math.sin(t)))
            hot = geo.rgba(geo.mix(self.slop_color(cx, cz), P.SLOP_GLOW, 0.35), rng.uniform(0.3, 0.5))
            with geo.state(g, anim=(50.0 + rng.uniform(0.1, 0.3), rng.random())):
                for k in range(12):
                    a2, b2 = rim[k], rim[(k + 1) % 12]
                    cols = [hot, geo.rgba(self.slop_color(a2[0], a2[2]), 0.0), geo.rgba(self.slop_color(b2[0], b2[2]), 0.0)]
                    geo.oriented(g, [centre, a2, b2], (0, 1, 0), cols, VENUE)
        for _ in range(60):  # bubbles: small domes that blink as they pop
            r, a = rng.uniform(20, 40), rng.uniform(0, 2 * math.pi)
            s = rng.uniform(0.12, 0.38)
            x, z = r * math.cos(a), r * math.sin(a)
            with geo.state(g, alpha=0.4, anim=(rng.uniform(0.2, 0.55), rng.random())):
                geo.ellipsoid(g, (x, SLOP_Y, z), (s, s * 0.7, s), geo.mix(self.slop_color(x, z), P.SLOP_FOAM, 0.6), VENUE,
                              rings=4, segments=7)
        with geo.state(m, alpha=0.0):
            for _ in range(26):  # half-sunk junk
                r, a = rng.uniform(22, 39), rng.uniform(0, 2 * math.pi)
                x, z = r * math.cos(a), r * math.sin(a)
                kind = rng.random()
                if kind < 0.45:
                    geo.lathe(m, (x, SLOP_Y - 0.45, z), [(0.3, 0.0), (0.31, 0.5), (0.3, 0.95)], 10,
                              [rng.choice((P.RUST, P.ORANGE, P.STEEL, P.BLUE)), P.RUST], VENUE, yaw=rng.random())
                elif kind < 0.7:
                    geo.lathe(m, (x, SLOP_Y - 0.12, z), [(0.42, 0.0), (0.46, 0.12), (0.42, 0.24)], 12, P.STEEL_DARK, VENUE)
                    geo.lathe(m, (x, SLOP_Y + 0.01, z), [(0.22, 0.12), (0.22, 0.13)], 12, P.SLOP_DARK, VENUE)
                elif kind < 0.92:
                    s = rng.uniform(0.6, 1.1)
                    geo.box(m, (x, SLOP_Y + s * 0.2, z), (s, s * 0.7, s), P.DECK_BOARD, VENUE, yaw=rng.random() * 3, skip=("bottom",))
                else:  # a sunken car roof
                    geo.box(m, (x, SLOP_Y + 0.15, z), (1.7, 0.5, 2.4), (0.45, 0.12, 0.10), VENUE, yaw=rng.random() * 3, skip=("bottom",))

    def pit_walls(self, side, m, g):
        rng = self.rng
        with geo.state(m, alpha=0.0):
            u = -SPAN
            while u < SPAN - 1e-6:
                u1 = min(SPAN, u + 3.0)
                streak = rng.uniform(0.75, 1.1)
                for y0, y1 in ((SLOP_Y - 0.5, -21.0), (-21.0, -16.0), (-16.0, -11.0), (-11.0, RIM_Y)):
                    def shade(y, streak=streak):
                        f = (y - SLOP_Y) / (RIM_Y - SLOP_Y)
                        c = geo.scale(P.CONCRETE_DARK, streak * (0.55 + 0.5 * f))
                        return geo.mix(P.SLOP_DARK, c, min(1.0, max(0.0, (y - SLOP_Y - 0.6) / 2.5)))
                    cols = [shade(y0), shade(y0), shade(y1), shade(y1)]
                    pts = [side.P(u, y0, PIT_W), side.P(u1, y0, PIT_W), side.P(u1, y1, PIT_W), side.P(u, y1, PIT_W)]
                    geo.oriented(m, pts, side.inward, cols, VENUE)
                u = u1
            # rim curb
            parts.box(m, side, (0.0, RIM_Y + 0.12, PIT_W + 0.2), (2 * SPAN, 0.24, 0.4), P.CONCRETE, VENUE, skip=("bottom",))
            # outfall pipes pouring slop
            for u in (-22.0 + rng.uniform(-4, 4), 18.0 + rng.uniform(-4, 4)):
                mouth = side.P(u, -19.0, PIT_W - 1.0)
                geo.cylinder_between(m, side.P(u, -19.0, PIT_W + 0.1), mouth, 0.55, 12, P.RUST, VENUE)
                geo.lathe(m, mouth, [(0.6, -0.08), (0.6, 0.08)], 12, P.STEEL_DARK, VENUE)
                fall = [(-0.18, -19.25), (0.18, -19.25), (0.42, SLOP_Y), (-0.42, SLOP_Y)]
                with geo.state(g, alpha=0.3, anim=(50.7, rng.random())):
                    geo.oriented(g, [side.P(u + du, y, PIT_W - 1.2) for du, y in fall], side.inward,
                                 geo.mix(P.SLOP, P.SLOP_GLOW, 0.25), VENUE)
                cx, _, cz = side.P(u, SLOP_Y, PIT_W - 1.4)
                for k in range(9):
                    t = 2 * math.pi * k / 9
                    with geo.state(g, alpha=0.15):
                        geo.box(g, (cx + 0.75 * math.cos(t), SLOP_Y + 0.04, cz + 0.75 * math.sin(t)), (0.35, 0.08, 0.35),
                                P.SLOP_FOAM, VENUE, yaw=t, skip=("bottom",))
        if side.k == 3:  # the fighters' tunnel behind the walkout bridge
            with geo.state(m, alpha=0.0):
                parts.rect(m, side, -1.6, 1.6, -14.5, -11.4, PIT_W - 0.02, (0.02, 0.02, 0.02), VENUE)
            with geo.state(g, alpha=1.0):
                parts.rect(g, side, -1.4, 1.4, -11.1, -10.4, PIT_W - 0.04, (1, 1, 1), VENUE, uvs=T.uv("fighters_box"))

    # ------------------------------------------------------------ stands
    def structure(self, side, m, g):
        """Rim floor, fence posts, terraced galleries with railings, scaffold frames, back
        wall, ceiling, fascia and the lighting truss of one stand."""
        rng = self.rng
        bays = int(round(2 * SPAN / BAY))
        with geo.state(m, alpha=0.0):
            # rim floor
            for k in range(14):
                u0, u1 = -SPAN + 2 * SPAN * k / 14, -SPAN + 2 * SPAN * (k + 1) / 14
                parts.flat(m, side, u0, u1, PIT_W, GALLERIES[0][1], RIM_Y, geo.jitter(P.CONCRETE_DARK, rng, 0.08), VENUE)
            # rim fence posts, rails and barbed wire
            fw = PIT_W + 0.5
            posts = int(round(2 * SPAN / 3.0))
            for k in range(posts + 1):
                u = -SPAN + 2 * SPAN * k / posts
                parts.box(m, side, (u, RIM_Y + 2.0, fw), (0.09, 4.0, 0.09), P.GALV, VENUE)
                parts.beam(m, side.P(u, RIM_Y + 3.95, fw), side.P(u, RIM_Y + 4.5, fw - 0.45), 0.05, P.GALV, VENUE)
            for y, dw in ((RIM_Y + 0.1, 0.0), (RIM_Y + 3.95, 0.0), (RIM_Y + 4.15, -0.17), (RIM_Y + 4.32, -0.31), (RIM_Y + 4.48, -0.44)):
                parts.box(m, side, (0.0, y, fw + dw), (2 * SPAN, 0.05 if dw == 0 else 0.02, 0.05 if dw == 0 else 0.02), P.GALV, VENUE)
            lower = RIM_Y
            for gi, (y, w) in enumerate(GALLERIES):
                # riser wall (the back of the level below), deck and its steel edge
                for k in range(bays):
                    u0, u1 = -SPAN + BAY * k, -SPAN + BAY * (k + 1)
                    parts.rect(m, side, u0, u1, lower, y, w, geo.jitter(P.CONCRETE_DARK, rng, 0.08), VENUE)
                for k in range(14):
                    u0, u1 = -SPAN + 2 * SPAN * k / 14, -SPAN + 2 * SPAN * (k + 1) / 14
                    parts.flat(m, side, u0, u1, w, w + DEPTH, y, geo.jitter(P.DECK_BOARD, rng, 0.1), VENUE)
                parts.box(m, side, (0.0, y - 0.16, w + 0.03), (2 * SPAN, 0.32, 0.06), P.STEEL_DARK, VENUE, skip=("bottom", "top"))
                # scaffold frame in front of the riser: standards, ledgers and braces
                fw_ = w - 0.22
                for k in range(bays + 1):
                    u = -SPAN + BAY * k
                    parts.box(m, side, (u, (lower + y) / 2, fw_), (0.07, y - lower, 0.07), P.SCAFFOLD, VENUE, skip=("top", "bottom"))
                for ly in (lower + 0.15, (lower + y) / 2, y - 0.2):
                    parts.box(m, side, (0.0, ly, fw_ - 0.05), (2 * SPAN, 0.05, 0.05), P.SCAFFOLD, VENUE)
                for k in range(bays):
                    u = -SPAN + BAY * k
                    a, b = (u, lower + 0.15), (u + BAY, y - 0.2)
                    if k % 2:
                        a, b = (u, y - 0.2), (u + BAY, lower + 0.15)
                    parts.beam(m, side.P(a[0], a[1], fw_ - 0.09), side.P(b[0], b[1], fw_ - 0.09), 0.04, P.SCAFFOLD, VENUE)
                # railing: posts, rails (chain-link panels are in the fence mesh)
                rw = w + 0.12
                for k in range(bays + 1):
                    parts.box(m, side, (-SPAN + BAY * k, y + 0.55, rw), (0.05, 1.1, 0.05), P.GALV, VENUE, skip=("bottom",))
                parts.box(m, side, (0.0, y + 1.09, rw), (2 * SPAN, 0.05, 0.06), P.GALV, VENUE)
                parts.box(m, side, (0.0, y + 0.55, rw), (2 * SPAN, 0.035, 0.035), P.GALV, VENUE)
                lower = y
            # back wall, ceiling, ceiling beams, fascia and the wall above the fascia
            for k in range(bays):
                u0, u1 = -SPAN + BAY * k, -SPAN + BAY * (k + 1)
                parts.rect(m, side, u0, u1, lower, CEILING_Y, BACK_W, geo.jitter(P.CONCRETE_DARK, rng, 0.08), VENUE)
            for k in range(7):
                u0, u1 = -SPAN + 2 * SPAN * k / 7, -SPAN + 2 * SPAN * (k + 1) / 7
                parts.flat(m, side, u0, u1, PIT_W, BACK_W, CEILING_Y, geo.jitter(P.CONCRETE_WET, rng, 0.08), VENUE, up=False)
            for k in range(1, 14):
                u = -SPAN + 2 * SPAN * k / 14
                parts.box(m, side, (u, CEILING_Y - 0.45, (PIT_W + BACK_W) / 2), (0.45, 0.9, BACK_W - PIT_W), P.CONCRETE_DARK, VENUE, skip=("top",))
            parts.box(m, side, (0.0, (FASCIA_Y + CEILING_Y) / 2, PIT_W + 0.55), (2 * SPAN, CEILING_Y - FASCIA_Y, 1.1),
                      P.CONCRETE_DARK, VENUE, skip=("top", "n"))
            parts.rect(m, side, -SPAN, SPAN, CEILING_Y, LANTERN_TOP, PIT_W, geo.scale(P.CONCRETE_DARK, 0.7), VENUE)
            # lighting truss: four chords, a zigzag face and hangers
            tw, ty, ts = TRUSS_W, TRUSS_Y, 0.42
            for dy in (0.0, ts):
                for dw in (-ts / 2, ts / 2):
                    parts.box(m, side, (0.0, ty + dy, tw + dw), (2 * SPAN, 0.05, 0.05), P.TRUSS, VENUE)
            n = int(2 * SPAN / 0.5)
            for k in range(n):
                u0, u1 = -SPAN + 0.5 * k, -SPAN + 0.5 * (k + 1)
                ya, yb = (ty, ty + ts) if k % 2 == 0 else (ty + ts, ty)
                parts.beam(m, side.P(u0, ya, tw - ts / 2), side.P(u1, yb, tw - ts / 2), 0.03, P.TRUSS, VENUE)
            for k in range(-6, 7):
                u = 6.0 * k
                parts.beam(m, side.P(u, ty + ts, tw), side.P(u, CEILING_Y, tw), 0.04, P.TRUSS, VENUE)
        # par cans under the truss, aimed at the ring
        k = -13
        while k <= 13:
            u = 3.0 * k + 1.5 * (k % 2)
            if abs(u) <= SPAN - 1.0:
                mount = side.P(u, TRUSS_Y - 0.06, TRUSS_W)
                d = toward(mount, TARGET)
                tip = tuple(mount[i] + d[i] * 0.42 for i in range(3))
                with geo.state(m, alpha=0.0):
                    geo.cylinder_between(m, mount, tip, 0.17, 8, P.STEEL_DARK, VENUE)
                with geo.state(g, alpha=1.0):
                    disc(g, tuple(tip[i] + d[i] * 0.01 for i in range(3)), d, 0.15, 8,
                         P.LAMP_WARM if (k + side.k) % 3 else P.LAMP_AMBER)
            k += 1
        # work lamps under each deck edge, backlighting the fans in front of each riser
        lower = RIM_Y
        for gi, (y, w) in enumerate(GALLERIES):
            for k in range(int(2 * SPAN / 5.0)):
                u = -SPAN + 2.5 + 5.0 * k
                with geo.state(g, alpha=1.0):
                    parts.box(g, side, (u, y - 0.42, w - 0.35), (0.42, 0.16, 0.28), P.LAMP_WARM, VENUE, skip=("top",))
            lower = y

    def haze(self, side, m):
        """Glowing wash behind every row of fans: the poster's orange backlight. Each bay is
        hottest under its work lamp and falls off sideways and downwards; a few bays are dark."""
        rng = self.rng
        levels = [(RIM_Y, GALLERIES[0][1])] + [(GALLERIES[i][0], GALLERIES[i + 1][1]) for i in range(2)] + [(GALLERIES[2][0], BACK_W)]
        for li, (y0, w) in enumerate(levels):
            top = (GALLERIES[li][0] if li < 3 else CEILING_Y - 2.5) - 0.3
            bottom = y0 + 0.4
            bays = int(round(2 * SPAN / BAY))
            for k in range(bays):
                u0 = -SPAN + BAY * k
                hot = rng.uniform(0.7, 1.0) if rng.random() > 0.14 else rng.uniform(0.08, 0.2)
                hc = u0 + BAY / 2 + rng.uniform(-0.35, 0.35)

                def col(u, y, hot=hot, hc=hc):
                    fy = (y - bottom) / (top - bottom)
                    fu = 1.0 - min(1.0, abs(u - hc) / (BAY * 0.8))
                    i = hot * (0.1 + 0.9 * fy ** 1.8) * (0.3 + 0.7 * fu)
                    return geo.mix(geo.scale(P.HAZE_DEEP, 0.35), P.HAZE_HOT, min(1.0, i))
                us = (u0, u0 + BAY / 2, u0 + BAY)
                ys = (bottom, bottom + (top - bottom) * 0.55, top)
                for a in range(2):
                    for b in range(2):
                        corners = [(us[a], ys[b]), (us[a + 1], ys[b]), (us[a + 1], ys[b + 1]), (us[a], ys[b + 1])]
                        with geo.state(m, alpha=1.0):
                            geo.oriented(m, [side.P(u, y, w - 0.04) for u, y in corners], side.inward,
                                         [col(u, y) for u, y in corners], HAZE)

    def fences(self, side, m):
        """Chain-link: the tall rim fence and every gallery railing."""
        with geo.state(m, alpha=0.0):
            posts = int(round(2 * SPAN / 3.0))
            for k in range(posts):
                u0, u1 = -SPAN + 2 * SPAN * k / posts, -SPAN + 2 * SPAN * (k + 1) / posts
                parts.chain_link(m, side, u0 + 0.05, u1 - 0.05, RIM_Y + 0.12, RIM_Y + 3.9, PIT_W + 0.48, 0.34, 0.05,
                                 geo.scale(P.GALV, 0.62), VENUE)
            for y, w in GALLERIES:
                bays = int(round(2 * SPAN / BAY))
                for k in range(bays):
                    u0 = -SPAN + BAY * k
                    if side.k == 0 and y == GALLERIES[1][0] and u0 + BAY > HERO[0] and u0 < HERO[1]:
                        continue  # behind the hero sign
                    parts.chain_link(m, side, u0 + 0.04, u0 + BAY - 0.04, y + 0.06, y + 1.05, w + 0.1, 0.3, 0.04,
                                     geo.scale(P.GALV, 0.62), VENUE)

    def crowd(self, side, m):
        rng = self.rng
        rows = [(RIM_Y, PIT_W + 1.1), (RIM_Y, PIT_W + 2.05)]
        for gi, (y, w) in enumerate(GALLERIES):
            rows.append((y, w + 0.8))
            rows.append((y, w + 1.85))
        count = 0
        for ri, (fy, w) in enumerate(rows):
            sparse = ri >= 6 and ri % 2 == 1
            u = -SPAN + 0.5 + rng.uniform(0, 0.4)
            while u < SPAN - 0.5:
                skip = rng.random() < (0.45 if sparse else 0.07)
                if side.k == 0 and fy == GALLERIES[1][0] and HERO[0] - 0.4 < u < HERO[1] + 0.4:
                    skip = True
                if not skip:
                    parts.fan(m, side, u, w + rng.uniform(-0.15, 0.15), fy, rng, P)
                    count += 1
                u += rng.uniform(0.62, 0.86)
        return count

    def barrels(self, side, g, m):
        rng = self.rng
        u = -SPAN + rng.uniform(4, 8)
        while u < SPAN - 4:
            x, y, z = side.P(u, RIM_Y, GALLERIES[0][1] - 0.6)
            with geo.state(m, alpha=0.0):
                geo.lathe(m, (x, y, z), [(0.3, 0.0), (0.31, 0.45), (0.3, 0.9)], 10, P.RUST, VENUE)
            for f in range(3):
                ox, oz = rng.uniform(-0.1, 0.1), rng.uniform(-0.1, 0.1)
                h = rng.uniform(0.5, 0.85)
                with geo.state(g, alpha=1.0, anim=(-rng.uniform(0.8, 1.4), rng.random())):
                    geo.lathe(g, (x + ox, y + 0.88, z + oz), [(0.2, 0.0), (0.0, h)], 5, P.FIRE, VENUE, yaw=rng.random())
            with geo.state(g, alpha=1.0, anim=(-1.0, rng.random())):
                geo.lathe(g, (x, y + 0.88, z), [(0.13, 0.0), (0.0, 0.4)], 5, P.FIRE_CORE, VENUE)
            u += rng.uniform(10.0, 16.0)

    def string_lights(self, side, g):
        rng = self.rng
        for y, w in GALLERIES[:2]:
            span = 5.0
            k = 0
            while -SPAN + span * (k + 1) <= SPAN + 1e-6:
                u0, u1 = -SPAN + span * k, -SPAN + span * (k + 1)
                k += 1
                if side.k == 0 and y == GALLERIES[1][0] and u1 > HERO[0] and u0 < HERO[1]:
                    continue
                pts = geo.catenary(side.P(u0, y + 1.12, w + 0.08), side.P(u1, y + 1.12, w + 0.08), 0.22, 9)
                for i, p in enumerate(pts[1:-1]):
                    anim = (rng.uniform(0.2, 0.5), rng.random()) if rng.random() < 0.15 else (0.0, 0.0)
                    with geo.state(g, alpha=1.0, anim=anim):
                        geo.box(g, (p[0], p[1] - 0.05, p[2]), (0.08, 0.1, 0.08), P.BULB, VENUE, skip=("top",))

    def banners(self, side, m, g):
        y_top = GALLERIES[2][0] + 1.12
        w = GALLERIES[2][1] - 0.36  # hanging in front of the third riser's scaffold
        for u, name in BANNERS[side.k]:
            with geo.state(m, alpha=0.22):
                parts.rect(m, side, u - 1.0, u + 1.0, y_top - 5.0, y_top, w, (1, 1, 1), VENUE, uvs=T.uv("banner_" + name))
            with geo.state(m, alpha=0.0):
                parts.box(m, side, (u, y_top + 0.04, w - 0.03), (2.2, 0.08, 0.08), P.STEEL_DARK, VENUE)
        # chains with hooks hanging from the fascia
        for u in ((-14.0, 14.0) if side.k == 0 else (-16.0, 0.0, 16.0)):
            u += self.rng.uniform(-1.5, 1.5)
            with geo.state(m, alpha=0.0):
                end = parts.chain(m, side.P(u, FASCIA_Y, PIT_W + 0.6), self.rng.uniform(9.0, 11.5), 0.17, P.STEEL, VENUE, self.rng)
                parts.hook(m, end, P.STEEL, VENUE)

    def signs(self, side, m, g):
        """Side-specific dressing: the hero neon (north), the bookie (east), the bar (south),
        exit signs everywhere."""
        k = side.k
        for u in (-34.0, 34.0):
            with geo.state(g, alpha=1.0):
                parts.rect(g, side, u - 0.9, u + 0.9, CEILING_Y - 4.6, CEILING_Y - 3.92, BACK_W - 0.05, (1, 1, 1), VENUE, uvs=T.uv("exit"))
        r2y, r2w = GALLERIES[0][0], GALLERIES[1][1] - 0.3  # riser 2, in front of its scaffold frame
        if k == 0:
            self.hero(side, m, g)
        elif k == 1:
            with geo.state(m, alpha=0.08):
                parts.rect(m, side, -2.6, 2.6, r2y + 2.0, r2y + 4.4, r2w - 0.06, (1, 1, 1), VENUE, uvs=T.uv("odds"))
            with geo.state(g, alpha=1.0):
                parts.rect(g, side, -1.4, 1.4, r2y + 4.45, r2y + 4.95, r2w - 0.06, (1, 1, 1), VENUE, uvs=T.uv("bets"))
                parts.rect(g, side, 9.0, 11.6, r2y + 4.2, r2y + 4.95, r2w - 0.06, (1, 1, 1), VENUE, uvs=T.uv("tickets"))
        elif k == 2:
            with geo.state(m, alpha=0.08):
                parts.rect(m, side, -2.6, 2.6, r2y + 2.0, r2y + 4.4, r2w - 0.06, (1, 1, 1), VENUE, uvs=T.uv("chalk_rules"))
            with geo.state(g, alpha=1.0):
                parts.rect(g, side, -11.6, -8.4, r2y + 3.9, r2y + 4.7, r2w - 0.06, (1, 1, 1), VENUE, uvs=T.uv("slop_cola"))
                parts.rect(g, side, 8.4, 11.6, r2y + 3.9, r2y + 4.7, r2w - 0.06, (1, 1, 1), VENUE, uvs=T.uv("bonk_burger"))
        else:
            with geo.state(g, alpha=1.0):
                parts.rect(g, side, -1.6, 1.6, r2y + 4.0, r2y + 4.8, r2w - 0.06, (1, 1, 1), VENUE, uvs=T.uv("fighters_box"))

    def hero(self, side, m, g):
        """SLOP PIT in yellow neon with an orange crown on a black board over the north
        stand's second gallery, framed by chasing marquee bulbs; one letter is faulty."""
        y, w = GALLERIES[1]
        bw = w - 0.12
        y0, y1 = y + 0.15, y + 3.55
        with geo.state(m, alpha=0.0):
            parts.box(m, side, (0.0, (y0 + y1) / 2, bw), (HERO[1] - HERO[0], y1 - y0, 0.16), P.CLOTH_BLACK, VENUE)
        face = bw - 0.08
        height = 2.2
        strokes, width = font.text_strokes("SLOP PIT", height, origin=(1.3, y0 + 0.55), tracking=1.02)
        glyph_counts = [len(font.GLYPHS[ch]) if ch in font.GLYPHS else 0 for ch in "SLOP PIT"]
        idx = 0
        for li, cnt in enumerate(glyph_counts):
            anim = (-1.3, 0.41) if li == 2 else (0.0, 0.0)  # the O buzzes
            for s in strokes[idx:idx + cnt]:
                with geo.state(g, alpha=1.0, anim=anim):
                    geo.tube(g, [side.P(u, v, face - 0.07) for u, v in s], 0.07, 6, P.NEON_YELLOW, VENUE)
            idx += cnt
        cx = 1.3 - width / 2 - 1.9
        cy = y0 + 0.55
        crown = [(-1.25, 0.0), (-1.25, 1.35), (-0.62, 0.55), (0.0, 1.85), (0.62, 0.55), (1.25, 1.35), (1.25, 0.0), (-1.25, 0.0)]
        with geo.state(g, alpha=1.0, anim=(50.8, 0.0)):
            geo.tube(g, [side.P(cx + u, cy + v, face - 0.07) for u, v in crown], 0.08, 6, P.NEON_ORANGE, VENUE)
        bulbs = []
        for t in range(0, 98):
            f = t / 98.0
            per = 2 * ((HERO[1] - HERO[0]) + (y1 - y0))
            d = f * per
            L, H = HERO[1] - HERO[0], y1 - y0
            if d < L:
                p = (HERO[0] + d, y0 + 0.12)
            elif d < L + H:
                p = (HERO[1] - 0.12, y0 + (d - L))
            elif d < 2 * L + H:
                p = (HERO[1] - (d - L - H), y1 - 0.12)
            else:
                p = (HERO[0] + 0.12, y1 - (d - 2 * L - H))
            bulbs.append(side.P(p[0], p[1], face - 0.06))
        for i, p in enumerate(bulbs):
            with geo.state(g, alpha=1.0, anim=(101.5, (i % 3) / 3.0)):
                geo.box(g, p, (0.11, 0.11, 0.11), P.BULB, VENUE, yaw=side.yaw)

    # ------------------------------------------------------------ corner towers
    def corner(self, sx, sz, m, g):
        rng = self.rng
        with geo.state(m, alpha=0.0):
            # the two faces that look at the pit, and the ceiling above the corner
            for y0, y1 in ((SLOP_Y - 0.5, RIM_Y), (RIM_Y, 6.0), (6.0, CEILING_Y)):
                c = geo.jitter(P.CONCRETE_DARK, rng, 0.06)
                geo.oriented(m, [(sx * PIT_W, y0, sz * PIT_W), (sx * PIT_W, y0, sz * BACK_W), (sx * PIT_W, y1, sz * BACK_W),
                                 (sx * PIT_W, y1, sz * PIT_W)], (-sx, 0, 0), c, VENUE)
                geo.oriented(m, [(sx * PIT_W, y0, sz * PIT_W), (sx * BACK_W, y0, sz * PIT_W), (sx * BACK_W, y1, sz * PIT_W),
                                 (sx * PIT_W, y1, sz * PIT_W)], (0, 0, -sz), c, VENUE)
            geo.oriented(m, [(sx * PIT_W, CEILING_Y, sz * PIT_W), (sx * BACK_W, CEILING_Y, sz * PIT_W),
                             (sx * BACK_W, CEILING_Y, sz * BACK_W), (sx * PIT_W, CEILING_Y, sz * BACK_W)], (0, -1, 0), P.CONCRETE_WET, VENUE)
            # a four-leg truss column standing in the pit just inside the corner
            c0 = (sx * (PIT_W - 0.9), sz * (PIT_W - 0.9))
            legs = [(c0[0] + sx * dx, c0[1] + sz * dz) for dx, dz in ((-0.4, -0.4), (0.4, -0.4), (0.4, 0.4), (-0.4, 0.4))]
            for px, pz in legs:
                geo.box(m, (px, (SLOP_Y + CEILING_Y) / 2, pz), (0.08, CEILING_Y - SLOP_Y, 0.08), P.SCAFFOLD, VENUE, skip=("top", "bottom"))
            y = SLOP_Y + 1.0
            while y < CEILING_Y - 0.5:
                for i in range(4):
                    (ax, az), (bx, bz) = legs[i], legs[(i + 1) % 4]
                    parts.beam(m, (ax, y, az), (bx, y, bz), 0.04, P.SCAFFOLD, VENUE)
                    if i < 2:
                        parts.beam(m, (ax, y, az), (bx, y + 1.2, bz), 0.03, P.SCAFFOLD, VENUE)
                y += 1.2
            # speaker stack on a platform at the first gallery
            base = (sx * (PIT_W - 1.9), GALLERIES[0][0], sz * (PIT_W - 1.9))
            geo.box(m, (base[0], base[1] - 0.1, base[2]), (2.6, 0.2, 2.6), P.STEEL_DARK, VENUE)
            yaw = math.atan2(-sx, -sz)
            for row in range(3):
                for col in (-0.62, 0.62):
                    cx = base[0] + math.cos(yaw) * col
                    cz = base[2] - math.sin(yaw) * col
                    cy = base[1] + 0.55 + row * 1.1
                    geo.box(m, (cx, cy, cz), (1.2, 1.08, 0.9), (0.05, 0.05, 0.055), VENUE, yaw=yaw)
                    fwd = (-sx * 0.7071, 0.0, -sz * 0.7071)
                    disc(m, (cx + fwd[0] * 0.46, cy, cz + fwd[2] * 0.46), fwd, 0.36, 10, (0.12, 0.12, 0.13))
        # warm wash on both corner faces so the corners read as lit walls, not dead concrete
        for face in (0, 1):
            for y0, y1, hot in ((RIM_Y + 0.5, 4.5, 0.45), (5.0, 15.5, 0.7)):
                c_top, c_bot = geo.scale(P.HAZE_HOT, hot), geo.scale(P.HAZE_DEEP, hot * 0.25)
                if face == 0:
                    pts = [(sx * (PIT_W - 0.04), y0, sz * (PIT_W + 0.4)), (sx * (PIT_W - 0.04), y0, sz * BACK_W),
                           (sx * (PIT_W - 0.04), y1, sz * BACK_W), (sx * (PIT_W - 0.04), y1, sz * (PIT_W + 0.4))]
                    n = (-sx, 0.0, 0.0)
                else:
                    pts = [(sx * (PIT_W + 0.4), y0, sz * (PIT_W - 0.04)), (sx * BACK_W, y0, sz * (PIT_W - 0.04)),
                           (sx * BACK_W, y1, sz * (PIT_W - 0.04)), (sx * (PIT_W + 0.4), y1, sz * (PIT_W - 0.04))]
                    n = (0.0, 0.0, -sz)
                with geo.state(g, alpha=1.0):
                    geo.oriented(g, pts, n, [c_bot, c_bot, c_top, c_top], HAZE)
        # a tall crown banner hanging from the corner column, facing the ring
        d = toward((sx * (PIT_W - 1.4), 0.0, sz * (PIT_W - 1.4)), (0.0, 0.0, 0.0))
        right = (-d[2], 0.0, d[0])
        c = (sx * (PIT_W - 1.45), 0.0, sz * (PIT_W - 1.45))
        top, bottom = 12.5, 3.0
        pts = [(c[0] - right[0] * 1.3, bottom, c[2] - right[2] * 1.3), (c[0] + right[0] * 1.3, bottom, c[2] + right[2] * 1.3),
               (c[0] + right[0] * 1.3, top, c[2] + right[2] * 1.3), (c[0] - right[0] * 1.3, top, c[2] - right[2] * 1.3)]
        (u0, v0), (u1, v1) = T.uv("banner_crown_slop" if sx * sz > 0 else "banner_crown")
        with geo.state(m, alpha=0.22):
            geo.oriented(m, pts, (d[0], 0.0, d[2]), (1.0, 1.0, 1.0), VENUE, [(u0, v0), (u1, v0), (u1, v1), (u0, v1)])
        # floodlight bank on top of the tower, aimed at the ring
        centre = (sx * (PIT_W - 1.6), 15.2, sz * (PIT_W - 1.6))
        d = toward(centre, TARGET)
        right = geo._norm(geo._cross((0.0, 1.0, 0.0), d))
        up = geo._cross(d, right)
        with geo.state(m, alpha=0.0):
            geo.box(m, (centre[0], centre[1] - 1.8, centre[2]), (0.2, 3.6, 0.2), P.SCAFFOLD, VENUE)
        for i in (-1, 0, 1):
            for j in (-1, 0, 1):
                p = tuple(centre[k] + right[k] * i * 0.62 + up[k] * j * 0.55 for k in range(3))
                with geo.state(m, alpha=0.0):
                    geo.cylinder_between(m, p, tuple(p[k] + d[k] * 0.3 for k in range(3)), 0.26, 4, P.STEEL_DARK, VENUE)
                with geo.state(g, alpha=1.0):
                    disc(g, tuple(p[k] + d[k] * 0.31 for k in range(3)), d, 0.23, 4, P.LAMP_WHITE)

    def build_all(self):
        b = self.b
        b.below("Pit", "Slop", lambda m, g: self.slop(m, g))
        stats = {}
        for side in self.sides:
            name = NAMES[side.k]
            b.background("Pit", "%s Pit Wall" % name, lambda m, g, side=side: self.pit_walls(side, m, g))
            b.background("Stands", "%s Stand" % name, lambda m, g, side=side: self.structure(side, m, g))
            b.background("Haze", "%s Haze" % name, lambda m, g, side=side: self.haze(side, m))
            b.background("Fences", "%s Fences" % name, lambda m, g, side=side: self.fences(side, m))
            b.background("Crowd", "%s Crowd" % name, lambda m, g, side=side: stats.__setitem__(name, self.crowd(side, m)))
            b.background("Stands", "%s Fire" % name, lambda m, g, side=side: self.barrels(side, g, m))
            b.background("Stands", "%s Lights" % name, lambda m, g, side=side: self.string_lights(side, g))
            b.background("Banners", "%s Banners" % name, lambda m, g, side=side: self.banners(side, m, g))
            b.background("Stands", "%s Signs" % name, lambda m, g, side=side: self.signs(side, m, g))
        for sx in (-1, 1):
            for sz in (-1, 1):
                b.background("Stands", "Corner %+d%+d" % (sx, sz), lambda m, g, sx=sx, sz=sz: self.corner(sx, sz, m, g))
        return stats
