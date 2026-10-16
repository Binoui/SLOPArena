"""The After Hours club tower: fight roof, performance risers and facade.

Walkable tops stay exactly at their authoritative heights. Inside the blast
corridor (y >= kill plane) wall dressing stands proud by at most WALL_PROUD; the
marquee, blade sign and podium only exist below the kill plane.
"""
from __future__ import annotations

import math

from stagekit import geo
import palette as P
import signs

BUILD = "AH_Build"      # StageVertexLit: lit by the stage's own lights
NIGHT = "AH_Night"      # StageNeon: unlit base + glow
WALL_PROUD = 0.025
STREET_Y = -36.0
PODIUM_TOP = -26.0
COPING = 0.5
FLOOR_H = 3.4
DANCE = (-5.0, -4.0, 5.0, 4.0)


def clip_convex(poly, outline):
    """Sutherland-Hodgman: clip polygon (map points) by a convex CCW outline."""
    out = list(poly)
    for a, b, (nx, nz), L in geo.edges(outline):
        if not out:
            break
        src, out = out, []
        for i in range(len(src)):
            p, q = src[i - 1], src[i]
            dp = (p[0] - a[0]) * nx + (p[1] - a[1]) * nz
            dq = (q[0] - a[0]) * nx + (q[1] - a[1]) * nz
            if dq <= 0:
                if dp > 0:
                    t = dp / (dp - dq)
                    out.append((p[0] + (q[0] - p[0]) * t, p[1] + (q[1] - p[1]) * t))
                out.append(q)
            elif dp <= 0:
                t = dp / (dp - dq)
                out.append((p[0] + (q[0] - p[0]) * t, p[1] + (q[1] - p[1]) * t))
    return out


def subtract_rect(rect, hole):
    x0, z0, x1, z1 = rect
    hx0, hz0, hx1, hz1 = max(x0, hole[0]), max(z0, hole[1]), min(x1, hole[2]), min(z1, hole[3])
    if hx0 >= hx1 or hz0 >= hz1:
        return [rect]
    out = [(x0, z0, x1, hz0), (x0, hz1, x1, z1), (x0, hz0, hx0, hz1), (hx1, hz0, x1, hz1)]
    return [r for r in out if r[2] - r[0] > 1e-4 and r[3] - r[1] > 1e-4]


def shrink(poly, d):
    cx = sum(p[0] for p in poly) / len(poly)
    cz = sum(p[1] for p in poly) / len(poly)
    out = []
    for x, z in poly:
        vx, vz = x - cx, z - cz
        L = math.hypot(vx, vz) or 1.0
        k = max(0.0, (L - d * 1.2) / L)
        out.append((cx + vx * k, cz + vz * k))
    return out


def top_poly(m, poly, y, color, mat=BUILD):
    """Upward-facing polygon from map points."""
    geo.oriented(m, [(x, y, z) for x, z in poly], (0, 1, 0), color, mat)


def rect_poly(r):
    x0, z0, x1, z1 = r
    return [(x0, z0), (x1, z0), (x1, z1), (x0, z1)]


class ClubTower:
    def __init__(self, build):
        self.b = build
        self.rng = build.rng
        self.outline = build.data["roof"]
        self.top = build.data["roofTop"]
        self.risers = build.data["risers"]
        self.kill = build.C["killY"]

    # ------------------------------------------------------------ fight roof
    def deck(self, m):
        inner = geo.inset_polygon(self.outline, COPING)
        y = self.top
        holes = [DANCE] + [(r[0], r[1], r[2], r[3]) for r in self.risers.values()]
        with geo.state(m, alpha=0.55):
            top_poly(m, inner, y - 0.025, P.SEAM)
            xs = [p[0] for p in inner]
            zs = [p[1] for p in inner]
            x = math.floor(min(xs))
            while x < max(xs):
                z = math.floor(min(zs))
                while z < max(zs):
                    rects = [(x, z, x + 1.0, z + 1.0)]
                    for h in holes:
                        rects = [piece for r in rects for piece in subtract_rect(r, h)]
                    for r in rects:
                        poly = clip_convex(rect_poly(r), inner)
                        if len(poly) >= 3 and abs(geo.polygon_area(poly)) > 0.02:
                            tone = self.rng.random()
                            base = P.DECK_LIGHT if tone > 0.9 else P.DECK_DARK if tone < 0.12 else P.DECK
                            top_poly(m, shrink(poly, 0.02), y, geo.jitter(base, self.rng, 0.05))
                    z += 1.0
                x += 1.0

    def coping(self, m, glow):
        """Light coping band with a flush neon inlay along every edge."""
        y = self.top
        rings = [0.0, 0.07, 0.15, COPING]
        outlines = [geo.inset_polygon(self.outline, d) if d else list(self.outline) for d in rings]
        n = len(self.outline)
        for band in range(3):
            outer, inner = outlines[band], outlines[band + 1]
            for i in range(n):
                j = (i + 1) % n
                chamfer = i % 2 == 1
                if band == 1:
                    color = P.VIOLET if chamfer else P.MAGENTA
                    with geo.state(glow, alpha=1.0):
                        geo.oriented(glow, [(outer[i][0], y, outer[i][1]), (outer[j][0], y, outer[j][1]),
                                            (inner[j][0], y, inner[j][1]), (inner[i][0], y, inner[i][1])], (0, 1, 0), color, NIGHT)
                    continue
                # coping stones: split long edges into ~1.2 m pieces
                L = math.dist(outer[i], outer[j])
                pieces = max(1, round(L / 1.2))
                for k in range(pieces):
                    t0, t1 = k / pieces, (k + 1) / pieces
                    lerp = lambda a, b, t: (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t)
                    q = [lerp(outer[i], outer[j], t0), lerp(outer[i], outer[j], t1), lerp(inner[i], inner[j], t1), lerp(inner[i], inner[j], t0)]
                    with geo.state(m, alpha=0.8):
                        top_poly(m, q, y, geo.jitter(P.COPING if band == 2 else P.COPING_DARK, self.rng, 0.04))

    def dance_floor(self, glow):
        x0, z0, x1, z1 = DANCE
        y = self.top
        with geo.state(glow, alpha=0.0):
            top_poly(glow, rect_poly(DANCE), y - 0.02, P.STAGE_BLACK, NIGHT)
        colors = [P.MAGENTA, P.VIOLET, P.BLUE, P.PINK, P.PURPLE]
        nx, nz = int(x1 - x0), int(z1 - z0)
        for i in range(nx):
            for j in range(nz):
                cx, cz = x0 + i + 0.5, z0 + j + 0.5
                ring = int(max(abs(cx) / 1.0, abs(cz) / 1.0))
                col = colors[(ring + (i + j) % 2) % len(colors)]
                phase = (math.hypot(cx, cz) / 9.0) % 1.0
                with geo.state(glow, alpha=1.0, anim=(50.0 + 0.45, phase)):
                    top_poly(glow, shrink(rect_poly((x0 + i, z0 + j, x0 + i + 1, z0 + j + 1)), 0.03), y,
                             geo.scale(col, 0.28 if (i + j) % 2 else 0.2), NIGHT)
        # bright frame around the floor
        frame = [(x0 - 0.12, z0 - 0.12), (x1 + 0.12, z0 - 0.12), (x1 + 0.12, z1 + 0.12), (x0 - 0.12, z1 + 0.12)]
        inner = [(x0, z0), (x1, z0), (x1, z1), (x0, z1)]
        with geo.state(glow, alpha=1.0):
            geo.ring_quads(glow, frame, inner, y, geo.scale(P.PINK, 0.55), NIGHT)

    def riser(self, m, glow, name):
        x0, z0, x1, z1, top = self.risers[name]
        y0 = self.top
        dj = name.startswith("East")
        fp = rect_poly((x0, z0, x1, z1))
        # walkable top: stage boards (DJ) or warm bar deck (VIP), with a flush LED inlay
        with geo.state(m, alpha=0.5):
            top_poly(m, fp, top - 0.02, P.SEAM)
            if dj:
                w = 0.3
                z = z0
                while z < z1 - 1e-6:
                    zz = min(z1, z + w)
                    top_poly(m, rect_poly((x0 + 0.24, z + 0.012, x1 - 0.24, zz - 0.012)), top, geo.jitter(P.STAGE_BOARD, self.rng, 0.08))
                    z = zz
            else:
                w = 0.28
                x = x0
                while x < x1 - 1e-6:
                    xx = min(x1, x + w)
                    top_poly(m, rect_poly((x + 0.012, z0 + 0.24, xx - 0.012, z1 - 0.24)), top, geo.jitter(P.BAR_WOOD, self.rng, 0.1))
                    x = xx
        trim = rect_poly((x0, z0, x1, z1))
        mid = rect_poly((x0 + 0.1, z0 + 0.1, x1 - 0.1, z1 - 0.1))
        inner = rect_poly((x0 + 0.24, z0 + 0.24, x1 - 0.24, z1 - 0.24))
        with geo.state(m, alpha=0.6):
            geo.ring_quads(m, trim, mid, top, P.BRASS if not dj else P.STEEL_DARK, BUILD)
        with geo.state(glow, alpha=1.0):
            geo.ring_quads(glow, mid, inner, top, P.VIOLET if dj else P.HOT_PINK, NIGHT)
        # walls
        sides = [((x0, z0), (x1, z0), (0, -1)), ((x1, z0), (x1, z1), (1, 0)), ((x1, z1), (x0, z1), (0, 1)), ((x0, z1), (x0, z0), (-1, 0))]
        for a, b, n in sides:
            plane = signs.Plane.facing(((a[0] + b[0]) / 2, y0, (a[1] + b[1]) / 2), n)
            L = math.dist(a, b)
            h = top - y0
            with geo.state(m, alpha=0.4):
                signs.panel(m, plane, -L / 2, 0.0, L / 2, h, P.STAGE_BLACK if dj else P.BAR_WOOD_DARK, off=0.0, mat=BUILD)
            with geo.state(glow, alpha=1.0):
                signs.panel(glow, plane, -L / 2, h - 0.16, L / 2, h - 0.1, P.VIOLET if dj else P.HOT_PINK, off=0.012, glow=True)
                signs.panel(glow, plane, -L / 2, 0.04, L / 2, 0.09, P.PURPLE if dj else P.AMBER, off=0.012, glow=True)
            faces_floor = (n[0] * -math.copysign(1, x0 + x1)) > 0.5
            if dj:
                self._speakers(m, glow, plane, L, h, eq=faces_floor)
            else:
                self._bar_front(m, glow, plane, L, h, sign=faces_floor)

    def _speakers(self, m, glow, plane, L, h, eq):
        if eq:
            # LED equaliser facing the dance floor: bars pulse like a level meter
            bars = 14
            for k in range(bars):
                u = -L / 2 + 0.5 + k * (L - 1.0) / (bars - 1)
                height = 0.25 + 0.75 * abs(math.sin(k * 0.8 + 0.3))
                for s in range(int(height * 6)):
                    v = 0.25 + s * 0.15
                    col = P.CYAN if s < 3 else P.VIOLET if s < 5 else P.MAGENTA
                    with geo.state(glow, alpha=1.0, anim=(50.0 + 1.6 + (k % 3) * 0.3, (k * 0.13 + s * 0.05) % 1.0)):
                        signs.panel(glow, plane, u - 0.13, v, u + 0.13, v + 0.1, col, off=0.015, glow=True,
                                    anim=(50.0 + 1.6 + (k % 3) * 0.3, (k * 0.13 + s * 0.05) % 1.0))
            return
        for u in (-L / 4, L / 4):
            for r, col in ((0.52, P.STEEL_DARK), (0.44, (0.06, 0.06, 0.07)), (0.2, P.STEEL), (0.1, (0.04, 0.04, 0.05))):
                ring = [(u + r * math.cos(a * math.pi / 8), h * 0.5 + r * math.sin(a * math.pi / 8)) for a in range(16)]
                with geo.state(m, alpha=0.2):
                    geo.oriented(m, [plane.at(p[0], p[1], 0.006 + (0.52 - r) * 0.02) for p in ring], plane.n, col, BUILD)

    def _bar_front(self, m, glow, plane, L, h, sign):
        with geo.state(m, alpha=0.5):
            u = -L / 2 + 0.1
            while u < L / 2 - 0.2:
                signs.panel(m, plane, u, 0.14, u + 0.16, h - 0.24, geo.jitter(P.BAR_WOOD, self.rng, 0.12), off=0.008, mat=BUILD)
                u += 0.22
            signs.panel(m, plane, -L / 2, 0.24, L / 2, 0.3, P.BRASS, off=0.018, mat=BUILD)
        if sign:
            with geo.state(glow, alpha=0.0):
                signs.panel(glow, plane, -1.6, 0.36, 1.6, 1.18, P.STAGE_BLACK, off=0.014)
            signs.text(glow, plane, "BAR", 0.56, center=(-0.25, 0.77), color=P.HOT_PINK, off=0.02)
            signs.text(glow, plane, "🍸", 0.56, center=(1.0, 0.77), color=P.CYAN, off=0.02)

    # ------------------------------------------------------------ tower
    def facade(self, m, glow):
        """Club walls from the roof edge to the podium (y PODIUM_TOP)."""
        y_top = self.top
        n = len(self.outline)
        for i, (a, b, normal, L) in enumerate(geo.edges(self.outline)):
            plane = signs.Plane.facing(((a[0] + b[0]) / 2, 0.0, (a[1] + b[1]) / 2), normal)
            chamfer = i % 2 == 1
            # base wall
            with geo.state(m, alpha=0.7):
                signs.panel(m, plane, -L / 2, PODIUM_TOP, L / 2, y_top, P.CLUB_STONE, off=0.0, mat=BUILD)
            # roof flashing and neon line just below the edge
            with geo.state(m, alpha=0.3):
                signs.panel(m, plane, -L / 2, y_top - 0.36, L / 2, y_top, P.STEEL_DARK, off=0.004, mat=BUILD)
            signs.panel(glow, plane, -L / 2, y_top - 0.24, L / 2, y_top - 0.17, P.VIOLET if chamfer else P.MAGENTA, off=0.012, glow=True)
            # Keep the wall just below the fight edge calm: edge play happens right above it.
            with geo.state(m, alpha=0.3):
                signs.panel(m, plane, -L / 2, y_top - 2.4, L / 2, y_top - 0.36, P.CLUB_STONE_DARK, off=0.004, mat=BUILD)
            # piers and window bays
            bay = 3.0 if not chamfer else L / 2
            bays = max(1, round(L / bay))
            bw = L / bays
            first_floor_top = y_top - 2.6
            with geo.state(m, alpha=0.6):
                for k in range(bays + 1):
                    u = -L / 2 + k * bw
                    signs.panel(m, plane, max(-L / 2, u - 0.28), PODIUM_TOP, min(L / 2, u + 0.28), y_top - 0.36, P.CLUB_STONE_DARK, off=0.01, mat=BUILD)
            floors = int((first_floor_top - PODIUM_TOP) / FLOOR_H)
            for f in range(floors):
                ytop = first_floor_top - f * FLOOR_H
                with geo.state(m, alpha=0.6):
                    signs.panel(m, plane, -L / 2, ytop - 0.55, L / 2, ytop, P.TERRACOTTA_DARK, off=0.006, mat=BUILD)
                for k in range(bays):
                    u0 = -L / 2 + k * bw + 0.45
                    u1 = u0 + bw - 0.9
                    lit = self.rng.random()
                    col = (P.WINDOW_WARM if lit < 0.3 else P.WINDOW_AMBER if lit < 0.4 else P.WINDOW_PINK if lit < 0.45 else P.WINDOW_OFF)
                    glowing = col != P.WINDOW_OFF
                    anim = (150.0 + 0.02, self.rng.random()) if glowing and self.rng.random() < 0.3 else None
                    target = glow if glowing else m
                    with geo.state(target, alpha=1.0 if glowing else 0.25):
                        signs.panel(target, plane, u0, ytop - FLOOR_H + 0.5, u1, ytop - 0.85, geo.scale(col, 0.5) if glowing else col,
                                    off=0.012, glow=glowing, anim=anim, mat=NIGHT if glowing else BUILD)
                    # mullion
                    with geo.state(m, alpha=0.2):
                        signs.panel(m, plane, (u0 + u1) / 2 - 0.05, ytop - FLOOR_H + 0.5, (u0 + u1) / 2 + 0.05, ytop - 0.85, P.STEEL_DARK, off=0.018, mat=BUILD)

    def podium(self, m, glow):
        """Club name, podium, entrance marquee and corner blade sign (all below the kill plane)."""
        name_y = self.kill - 5.0
        for i, (a, b, normal, L) in enumerate(geo.edges(self.outline)):
            if i % 2 == 1 or L < 20:
                continue
            plane = signs.Plane.facing(((a[0] + b[0]) / 2, 0.0, (a[1] + b[1]) / 2), normal)
            with geo.state(m, alpha=0.3):
                signs.panel(m, plane, -L / 2 + 0.6, name_y - 1.9, L / 2 - 0.6, name_y + 1.9, P.STAGE_BLACK, off=0.12, depth=0.12, mat=BUILD)
            signs.text(glow, plane, "AFTER HOURS", 2.4, center=(0.0, name_y), color=P.MAGENTA if normal[1] > 0 else P.HOT_PINK,
                       style="tube", off=0.16, per_letter_anim=(lambda k, ch: (-1.0, 0.37) if (k == 3 and normal[1] > 0) else None))
        x0, z0, x1, z1 = -19.0, -16.0, 19.0, 16.0
        with geo.state(m, alpha=0.6):
            geo.box_minmax(m, (x0, STREET_Y, z0), (x1, PODIUM_TOP, z1), P.CLUB_STONE_DARK, BUILD, skip=("bottom",))
            geo.box_minmax(m, (x0 - 0.3, PODIUM_TOP - 0.6, z0 - 0.3), (x1 + 0.3, PODIUM_TOP, z1 + 0.3), P.TERRACOTTA, BUILD, skip=("bottom",))
        # roof clutter on the podium
        with geo.state(m, alpha=0.6):
            for cx, cz in ((-17, -14), (16.5, -13.5), (-16.5, 13.8), (17, 13.6)):
                geo.box(m, (cx, PODIUM_TOP + 0.7, cz), (2.2, 1.4, 1.6), P.STEEL, BUILD)
        # ground-floor shop windows all round
        for (a, b, normal) in (((x0, z0), (x1, z0), (0, -1)), ((x1, z0), (x1, z1), (1, 0)), ((x1, z1), (x0, z1), (0, 1)), ((x0, z1), (x0, z0), (-1, 0))):
            L = math.dist(a, b)
            plane = signs.Plane.facing(((a[0] + b[0]) / 2, 0.0, (a[1] + b[1]) / 2), normal)
            k = 0
            u = -L / 2 + 1.5
            while u < L / 2 - 4.0:
                entrance = normal == (0, 1) and abs(u + 2.0) < 7.0
                if not entrance:
                    signs.panel(glow, plane, u, STREET_Y + 0.6, u + 3.6, STREET_Y + 4.0, geo.scale(P.WINDOW_WARM, 0.65), off=0.03, glow=True)
                    signs.panel(glow, plane, u, STREET_Y + 5.6, u + 3.6, STREET_Y + 8.4,
                                geo.scale(P.WINDOW_PINK if k % 3 == 0 else P.WINDOW_WARM, 0.55), off=0.03, glow=True)
                u += 4.6
                k += 1
            if normal == (0, 1):
                signs.panel(glow, plane, -6.0, STREET_Y + 0.0, 6.0, STREET_Y + 4.6, geo.scale(P.WINDOW_AMBER, 0.9), off=0.03, glow=True)
                signs.marquee(glow, plane, 15.0, 3.6, STREET_Y + 6.2, 0.9, "AFTER HOURS", P.HOT_PINK, letter_height=0.62)
        # blade sign on the north-east chamfer, hanging below the kill plane
        corner = self.outline[4]
        prev = self.outline[3]
        base = ((corner[0] + prev[0]) / 2, (corner[1] + prev[1]) / 2)
        top = min(self.kill - 2.4, -14.5)
        signs.blade(glow, base, (1, 1), 2.8, 0.55, top, top - 11.0, "ROOFTOP", P.CYAN, bulb_color=P.GOLD, outline=P.MAGENTA)
        # entrance queue rope posts and red carpet
        with geo.state(m, alpha=0.4):
            geo.box_minmax(m, (-3.0, STREET_Y, z1), (3.0, STREET_Y + 0.03, z1 + 6.0), (0.55, 0.06, 0.10), BUILD)
        for k in range(6):
            for side in (-3.4, 3.4):
                with geo.state(glow, alpha=1.0):
                    geo.box(glow, (side, STREET_Y + 0.5, z1 + 0.6 + k * 1.0), (0.1, 1.0, 0.1), P.GOLD, NIGHT)

    def build_all(self):
        b = self.b
        b.surface("Club Roof", "Fight Roof", lambda m, g: (self.deck(m), self.coping(m, g), self.dance_floor(g)))
        for name in self.risers:
            b.surface("Club Roof", name, lambda m, g, name=name: self.riser(m, g, name))
        b.wall("Club Tower", "Club Facade", lambda m, g: self.facade(m, g))
        b.below("Club Tower", "Club Podium", lambda m, g: self.podium(m, g))
