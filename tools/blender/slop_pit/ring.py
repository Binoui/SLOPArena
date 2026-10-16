"""The fight: ringside floor, the raised ring and the tower they stand on.

Walkable tops stay exactly at their authoritative heights and inside their footprints.
Inside the blast corridor (y >= kill plane) wall dressing stands proud by at most
WALL_PROUD; below it the tower sinks into the slop, where its base, I-beams and the
fighters' walkout bridge stay at least two metres under the kill plane.
"""
from __future__ import annotations

import math
import random

from stagekit import geo
import palette as P
import textures as T

LIT, FLOOR, CANVAS, VENUE, HAZE = "SP_Lit", "SP_Floor", "SP_Canvas", "SP_Venue", "SP_Haze"
WALL_PROUD = 0.025
SLOP_Y = -24.0
HOARD_TOP = -2.6
PANEL_W, PANEL_H = 1.22, 2.44
HOARD_ROWS = 3
PLINTH_TOP = -16.0
BRIDGE_Y = -14.2
BRIDGE_HALF = 1.2
SKIRT_BY_NORMAL = {(0, -1): "skirt_slop_pit", (1, 0): "skirt_sponsors_a",
                   (0, 1): "skirt_no_rules", (-1, 0): "skirt_sponsors_b"}
WHITE = (1.0, 1.0, 1.0)


def clip_convex(poly, outline):
    """Sutherland-Hodgman: the part of `poly` inside the convex CCW `outline` (map view)."""
    for a, b, n, L in geo.edges(outline):
        out = []
        for i in range(len(poly)):
            p, q = poly[i], poly[(i + 1) % len(poly)]
            dp = (p[0] - a[0]) * n[0] + (p[1] - a[1]) * n[1]
            dq = (q[0] - a[0]) * n[0] + (q[1] - a[1]) * n[1]
            if dp <= 1e-9:
                out.append(p)
            if (dp <= 1e-9) != (dq <= 1e-9):
                t = dp / (dp - dq)
                out.append((p[0] + (q[0] - p[0]) * t, p[1] + (q[1] - p[1]) * t))
        poly = out
        if len(poly) < 3:
            return []
    return poly


class WallPlane:
    """A vertical plane on an outline edge: s along the edge (readable from outside), y up,
    `off` outward along the normal."""

    def __init__(self, a, b, n, L):
        self.mid = ((a[0] + b[0]) / 2, (a[1] + b[1]) / 2)
        self.n = n
        self.r = (-n[1], n[0])
        self.L = L
        self.yaw = math.atan2(n[0], n[1])

    def P(self, s, y, off=0.0):
        return (self.mid[0] + self.r[0] * s + self.n[0] * off, y, self.mid[1] + self.r[1] * s + self.n[1] * off)

    def quad(self, m, s0, s1, y0, y1, off, color, mat=LIT, uvs=None, tilt=0.0):
        pts = [(s0, y0), (s1, y0), (s1, y1), (s0, y1)]
        if tilt:
            cs, cy = (s0 + s1) / 2, (y0 + y1) / 2
            c, s = math.cos(tilt), math.sin(tilt)
            pts = [(cs + (p - cs) * c - (q - cy) * s, cy + (p - cs) * s + (q - cy) * c) for p, q in pts]
        quv = None
        if uvs is not None:
            (u0, v0), (u1, v1) = uvs
            quv = [(u0, v0), (u1, v0), (u1, v1), (u0, v1)]
        geo.oriented(m, [self.P(p, q, off) for p, q in pts], (self.n[0], 0.0, self.n[1]), color, mat, quv)


def sub_uv(uvs, f0, f1):
    """Horizontal fraction [f0, f1] of an atlas cell's UV rectangle."""
    (u0, v0), (u1, v1) = uvs
    return (u0 + (u1 - u0) * f0, v0), (u0 + (u1 - u0) * f1, v1)


class RingTower:
    def __init__(self, build):
        self.b = build
        d = build.data
        self.outline = d["floor"]
        self.top = d["top"]
        self.ring = d["ring"]["footprint"]
        self.ring_top = d["ring"]["top"]
        self.kill = build.C["killY"]
        self.rng = random.Random(20261016 + 1)

    # ------------------------------------------------------------ walkable tops
    def floor_top(self, m):
        """Stained ringside concrete: a 0.5 m grid clipped to the octagon, skipping the ring."""
        x0, z0, x1, z1 = self.ring
        step = 0.5
        n = int(round(32 / step))
        with geo.detail(m, 0.6):
            for i in range(n):
                for j in range(n):
                    cx0, cz0 = -16 + i * step, -16 + j * step
                    cx1, cz1 = cx0 + step, cz0 + step
                    if cx0 >= x0 - 1e-6 and cx1 <= x1 + 1e-6 and cz0 >= z0 - 1e-6 and cz1 <= z1 + 1e-6:
                        continue
                    poly = clip_convex([(cx0, cz0), (cx1, cz0), (cx1, cz1), (cx0, cz1)], self.outline)
                    if len(poly) < 3 or abs(geo.polygon_area(poly)) < 1e-5:
                        continue
                    pts = [(x, self.top, z) for x, z in poly]
                    uvs = [((x + 16) / 32, (z + 16) / 32) for x, z in poly]
                    geo.oriented(m, pts, (0, 1, 0), WHITE, FLOOR, uvs)

    def ring_top_surface(self, m):
        x0, z0, x1, z1 = self.ring
        n = 32
        with geo.detail(m, 0.25):
            for i in range(n):
                for j in range(n):
                    ax, bx = x0 + (x1 - x0) * i / n, x0 + (x1 - x0) * (i + 1) / n
                    az, bz = z0 + (z1 - z0) * j / n, z0 + (z1 - z0) * (j + 1) / n
                    poly = [(ax, az), (bx, az), (bx, bz), (ax, bz)]
                    uvs = [((x - x0) / (x1 - x0), (z - z0) / (z1 - z0)) for x, z in poly]
                    geo.oriented(m, [(x, self.ring_top, z) for x, z in poly], (0, 1, 0), WHITE, CANVAS, uvs)

    # ------------------------------------------------------------ ring skirt
    def ring_skirt(self, m):
        x0, z0, x1, z1 = self.ring
        square = [(x1, z0), (x1, z1), (x0, z1), (x0, z0)]
        h = self.ring_top - self.top
        for a, b, n, L in geo.edges(square):
            wp = WallPlane(a, b, n, L)
            cell = T.uv(SKIRT_BY_NORMAL[(round(n[0]), round(n[1]))])
            (u0, v0), (u1, v1) = cell
            cols, rows = 16, 2
            with geo.detail(m, 0.3):
                for c in range(cols):
                    for r in range(rows):
                        s0, s1 = -L / 2 + L * c / cols, -L / 2 + L * (c + 1) / cols
                        y0, y1 = self.top + h * r / rows, self.top + h * (r + 1) / rows
                        uvs = ((u0 + (u1 - u0) * c / cols, v0 + (v1 - v0) * r / rows),
                               (u0 + (u1 - u0) * (c + 1) / cols, v0 + (v1 - v0) * (r + 1) / rows))
                        wp.quad(m, s0, s1, y0, y1, 0.004, WHITE, LIT, uvs)

    # ------------------------------------------------------------ tower walls
    def tower_walls(self, m):
        """Kick plate with hazard stripes, a calm band under the edge, three rows of plywood
        hoarding with fight posters, then dark concrete down into the slop."""
        rng = self.rng
        hoard_bottom = HOARD_TOP - PANEL_H * HOARD_ROWS
        for i, (a, b, n, L) in enumerate(geo.edges(self.outline)):
            wp = WallPlane(a, b, n, L)
            cols = max(1, int(round(L)))
            bands = [(-0.42, 0.0, P.KICK_PLATE), (HOARD_TOP, -0.42, P.TOWER_DARK),
                     (hoard_bottom, HOARD_TOP, geo.scale(P.TOWER_DARK, 0.75)),
                     (self.kill - 0.6, hoard_bottom, P.TOWER_STEEL), (-17.0, self.kill - 0.6, P.TOWER_CONCRETE),
                     (SLOP_Y - 1.0, -17.0, geo.mix(P.TOWER_CONCRETE, P.SLOP_DARK, 0.45))]
            # backing wall, subdivided for AO and colour variation
            with geo.detail(m, 0.55):
                for y0, y1, color in bands:
                    rows = max(1, int(round((y1 - y0) / 2.2)))
                    for c in range(cols):
                        for r in range(rows):
                            s0, s1 = -L / 2 + L * c / cols, -L / 2 + L * (c + 1) / cols
                            ya, yb = y0 + (y1 - y0) * r / rows, y0 + (y1 - y0) * (r + 1) / rows
                            wp.quad(m, s0, s1, ya, yb, 0.003, geo.jitter(color, rng, 0.05), LIT)
            # hazard-striped kick plate (one 8:1 stripe cell per ~3.4 m) and a steel lip
            segs = max(1, int(round(L / 3.36)))
            with geo.detail(m, 0.3):
                for k in range(segs):
                    wp.quad(m, -L / 2 + L * k / segs, -L / 2 + L * (k + 1) / segs, -0.40, -0.04, 0.008, WHITE, LIT, T.uv("hazard"))
                wp.quad(m, -L / 2, L / 2, -0.04, 0.0, 0.010, P.STEEL_DARK, LIT)
            # plywood hoarding: mostly plain dark sheets (a calm backdrop for recovery), some
            # raw ply, a few tagged sheets and pasted fight posters
            tagged = ["hoard_" + h for h in T.HOARDING if h != "ply_raw"]
            for r in range(HOARD_ROWS):
                ytop = HOARD_TOP - r * PANEL_H
                s = -L / 2
                while s < L / 2 - 0.05:
                    s1 = min(L / 2, s + PANEL_W)
                    frac = (s1 - s) / PANEL_W
                    roll = rng.random()
                    if roll < 0.58:
                        cell, tint = T.uv("hoard_ply_raw"), geo.jitter((0.2, 0.19, 0.18), rng, 0.15)
                    elif roll < 0.76:
                        cell, tint = T.uv("hoard_ply_raw"), geo.jitter((0.7, 0.68, 0.64), rng, 0.08)
                    else:
                        cell, tint = T.uv(rng.choice(tagged)), geo.jitter((0.85, 0.85, 0.85), rng, 0.06)
                    with geo.detail(m, 0.5):
                        wp.quad(m, s + 0.012, s1 - 0.012, ytop - PANEL_H + 0.012, ytop - 0.012, 0.009,
                                tint, LIT, sub_uv(cell, 0.0, frac))
                    if frac > 0.8 and rng.random() < 0.12:
                        poster = T.uv("poster_" + rng.choice(T.POSTERS))
                        cs = (s + s1) / 2 + rng.uniform(-0.12, 0.12)
                        cy = ytop - PANEL_H / 2 + rng.uniform(-0.25, 0.25)
                        with geo.detail(m, 0.2):
                            wp.quad(m, cs - 0.4, cs + 0.4, cy - 0.6, cy + 0.6, 0.015, geo.jitter((0.9, 0.9, 0.9), rng, 0.05), LIT,
                                    poster, tilt=math.radians(rng.uniform(-4, 4)))
                    s = s1
            # flat steel strips continue each I-beam up through the corridor
            pil = max(1, int(L / 3.6))
            for k in range(1, pil + 1):
                ps = -L / 2 + L * k / (pil + 1)
                wp.quad(m, ps - 0.16, ps + 0.16, self.kill - 2.05, hoard_bottom, 0.014, P.STEEL_DARK, LIT)
            # stencilled warnings on the long walls
            if L > 15:
                cell = T.uv("stencil_refunds" if i % 4 == 0 else "stencil_fighters")
                wp.quad(m, -3.2, 3.2, -15.6, -14.4 if i % 4 == 0 else -14.8, 0.012, WHITE, LIT, cell)

    def tower_door(self, m):
        """The fighters' door on the west wall, where the walkout bridge arrives."""
        for a, b, n, L in geo.edges(self.outline):
            if round(n[0]) == -1 and abs(n[1]) < 1e-6:
                wp = WallPlane(a, b, n, L)
                wp.quad(m, -BRIDGE_HALF, BRIDGE_HALF, BRIDGE_Y, -12.0, 0.016, (0.03, 0.03, 0.03), LIT)
                for s0, s1, y0, y1 in ((-BRIDGE_HALF - 0.12, -BRIDGE_HALF, BRIDGE_Y, -11.95),
                                       (BRIDGE_HALF, BRIDGE_HALF + 0.12, BRIDGE_Y, -11.95),
                                       (-BRIDGE_HALF - 0.12, BRIDGE_HALF + 0.12, -12.0, -11.88)):
                    wp.quad(m, s0, s1, y0, y1, 0.02, P.STEEL, LIT)
                with geo.state(m, alpha=1.0):
                    wp.quad(m, -0.9, 0.9, -11.84, -11.36, 0.022, WHITE, VENUE, T.uv("fighters_box"))

    # ------------------------------------------------------------ below the kill plane
    def tower_base(self, m, glow):
        """Battered concrete footing, I-beams sinking into the slop and the walkout bridge."""
        rng = self.rng
        out = 4.0
        with geo.state(m, alpha=0.0):
            for a, b, n, L in geo.edges(self.outline):
                wp = WallPlane(a, b, n, L)
                # sloped footing from the wall at PLINTH_TOP down past the slop
                pts = [wp.P(-L / 2, PLINTH_TOP, 0.0), wp.P(L / 2, PLINTH_TOP, 0.0),
                       wp.P(L / 2 + out * 0.42, SLOP_Y - 1.0, out), wp.P(-L / 2 - out * 0.42, SLOP_Y - 1.0, out)]
                up = (n[0] * 0.92, 0.4, n[1] * 0.92)
                geo.oriented(m, pts, up, geo.mix(P.CONCRETE_DARK, P.SLOP_DARK, 0.25), VENUE)
                # I-beams protruding below the kill plane (minus the two-metre margin)
                pil = max(1, int(L / 3.6))
                for k in range(1, pil + 1):
                    ps = -L / 2 + L * k / (pil + 1)
                    top = self.kill - 2.05
                    c = wp.P(ps, (top + PLINTH_TOP - 1.0) / 2, 0.19)
                    geo.box(m, c, (0.34, top - PLINTH_TOP + 1.0, 0.34), P.STEEL_DARK, VENUE, skip=("bottom",), yaw=wp.yaw)
                    geo.box(m, wp.P(ps, top - 0.1, 0.24), (0.5, 0.2, 0.46), geo.scale(P.STEEL, 0.8), VENUE, yaw=wp.yaw)
        self.bridge(m, glow)

    def bridge(self, m, glow):
        """Steel gangway from the west pit wall to the tower door (all below kill - 2)."""
        x0, x1 = -41.5, -16.06
        y = BRIDGE_Y
        z0, z1 = -BRIDGE_HALF, BRIDGE_HALF
        with geo.state(m, alpha=0.0):
            geo.box_minmax(m, (x0, y - 0.25, z0), (x1, y, z1), P.STEEL, VENUE, skip=("bottom",), top_color=P.DECK_BOARD)
            for z in (z0 + 0.15, z1 - 0.15):  # girders
                geo.box_minmax(m, (x0, y - 1.1, z - 0.12), (x1, y - 0.25, z + 0.12), P.STEEL_DARK, VENUE)
            n = int((x1 - x0) / 2.0)
            for k in range(n + 1):
                x = x0 + (x1 - x0) * k / n
                for z in (z0 + 0.05, z1 - 0.05):
                    geo.box(m, (x, y + 0.5, z), (0.06, 1.0, 0.06), P.SCAFFOLD, VENUE)
            for z in (z0 + 0.05, z1 - 0.05):
                geo.box_minmax(m, (x0, y + 0.97, z - 0.04), (x1, y + 1.03, z + 0.04), P.SCAFFOLD, VENUE)
                geo.box_minmax(m, (x0, y + 0.47, z - 0.03), (x1, y + 0.51, z + 0.03), P.SCAFFOLD, VENUE)
        # warm bulbs along both rails
        for z in (z0 + 0.05, z1 - 0.05):
            pts = geo.catenary((x0, y + 0.98, z), (x1, y + 0.98, z), 0.0, int((x1 - x0) / 0.8))
            for k, p in enumerate(pts):
                with geo.state(glow, alpha=1.0, anim=(0.0, 0.0)):
                    geo.box(glow, (p[0], p[1] - 0.06, p[2]), (0.07, 0.09, 0.07), P.BULB, VENUE)

    def build_all(self):
        b = self.b
        b.surface("Ringside Floor", "Ringside Floor", lambda m, g: self.floor_top(m))
        b.surface("Ring", "Ring", lambda m, g: self.ring_top_surface(m))
        b.wall("Ring", "Ring Skirt", "ring", lambda m, g: self.ring_skirt(m))
        b.wall("Ring Tower", "Tower Walls", "floor", lambda m, g: (self.tower_walls(m), self.tower_door(m)))
        b.below("Pit", "Tower Base", lambda m, g: self.tower_base(m, g))
