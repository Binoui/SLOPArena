"""Fight Night Pier's playable pieces, dressed as what they are: the promenade's seawall,
plank decks on piles with white fascia, the burnt old pier and its pilings, buoys and rafts,
the gondola pods stacked on crates, the wheel's tower, the coaster's platforms on lattice
and the ring's apron.

Every walkable top is one face on the planar ground texture (textures.ground paints it).
Side dressing stays on its collision box (at most PROUD out of it, never above its top);
small props on tops stay inside the footprint, at its edges, and no higher than
TOP_PROP_HIGH unless declared (lamp posts). build.py checks it per role.
"""
from __future__ import annotations

import math

from stagekit import geo
import palette as P
import parts as K
import textures as T

PROUD = 0.26
TOP_PROP_HIGH = 1.3
LAMP_HIGH = 4.6
FASCIA = 0.42


def sides(pieces, bases, name):
    """Exposed side spans of a piece: [(key, plane, u0, u1, y0)], y0 being the height a
    touching neighbour (or the piece's own base) hides the side up to."""
    x0, z0, x1, z1, top = pieces[name]
    base = bases[name]
    out = []
    for key, plane, u0, u1 in K.box_planes(x0, z0, x1, z1):
        covers = []
        for other, (a0, b0, a1, b1, t) in pieces.items():
            if other == name:
                continue
            if key == "s" and abs(b1 - z0) < 1e-3:
                lo, hi = a0, a1
            elif key == "n" and abs(b0 - z1) < 1e-3:
                lo, hi = -a1, -a0
            elif key == "w" and abs(a1 - x0) < 1e-3:
                lo, hi = -b1, -b0
            elif key == "e" and abs(a0 - x1) < 1e-3:
                lo, hi = b0, b1
            else:
                continue
            lo, hi = max(lo, u0), min(hi, u1)
            if hi - lo > 1e-3:
                covers.append((lo, hi, min(t, top)))
        cuts = sorted({u0, u1, *[c[0] for c in covers], *[c[1] for c in covers]})
        for a, b in zip(cuts, cuts[1:]):
            mid = (a + b) / 2
            hidden = max([base] + [h for lo, hi, h in covers if lo <= mid <= hi])
            if top - hidden > 1e-3 and b - a > 1e-3:
                out.append((key, plane, a, b, hidden))
    return out


class Pieces:
    def __init__(self, b):
        self.b = b
        self.P = b.pieces
        self.bases = {n: s[1] for n, s in b.data["solids"].items()}
        self.rng = b.rng

    def build(self):
        for name in self.P:
            self.top(name)
            getattr(self, "dress_" + T.kind(name))(name)
        self.top_props()

    # ------------------------------------------------------------ tops
    def top(self, name):
        x0, z0, x1, z1, top = self.P[name]

        def build(m):
            pts = [(x0, top, z0), (x0, top, z1), (x1, top, z1), (x1, top, z0)]
            with geo.detail(m, 0.35):
                geo.oriented(m, pts, (0, 1, 0), (1.0, 1.0, 1.0), K.GROUND, [T.ground_uv(x, z) for x, y, z in pts])
        self.b.part("top", "Tops", name, build)

    # ------------------------------------------------------------ pier decks on piles
    def pier_sides(self, m, name, fascia=P.TRIM, pile=P.PILE, under=P.UNDER, burnt=False, spacing=2.8):
        x0, z0, x1, z1, top = self.P[name]
        rng = self.rng
        for key, plane, u0, u1, y0 in sides(self.P, self.bases, name):
            f_lo = max(y0, top - FASCIA)
            # The fascia: a painted beam along the top of every exposed side.
            if top - f_lo > 1e-3:
                K.panel(m, plane, u0, f_lo, u1, top, fascia, off=0.06)
                K.panel(m, plane, u0, f_lo - 0.04, u1, f_lo, geo.scale(fascia, 0.55), off=0.07)
            if f_lo - y0 <= 0.05:
                continue
            # Darkness under the deck, set back into the box.
            K.panel(m, plane, u0, y0, u1, f_lo, under, off=-0.35)
            # Piles in front of it, weed and wet at the waterline.
            n = max(1, int(round((u1 - u0) / spacing)))
            us = [u0 + 0.3 + (u1 - u0 - 0.6) * i / n for i in range(n + 1)]
            for i, u in enumerate(us):
                p0 = plane.at(u, y0, 0.0)
                lean = (0.0, 0.0)
                y_top = f_lo
                y_bot = y0 + 0.05
                if burnt:
                    # burnt piles lean back under the deck, never out of the box
                    k = rng.uniform(0.0, 0.18)
                    lean = (-plane.n[0] * k, -plane.n[2] * k)
                    if rng.random() < 0.18:
                        y_top = y0 + (f_lo - y0) * rng.uniform(0.3, 0.7)
                tone = geo.jitter(pile, rng, 0.08)
                K.round_post(m, p0[0], p0[2], y_bot, y_top, 0.17, tone, 8, lean=lean)
                if y0 < 0.3:
                    K.round_post(m, p0[0], p0[2], y_bot, min(0.35, y_top), 0.19, P.WEED, 8)
            # X-bracing between pile pairs (some missing on the burnt pier).
            for a, b in zip(us, us[1:]):
                if burnt and rng.random() < 0.45:
                    continue
                lo, hi = y0 + 0.4, f_lo - 0.15
                if hi - lo < 0.6:
                    continue
                c = geo.scale(pile, 0.85)
                K.beam(m, plane.at(a, lo, 0.05), plane.at(b, hi, 0.05), 0.09, c)
                K.beam(m, plane.at(a, hi, 0.05), plane.at(b, lo, 0.05), 0.09, c)

    def dress_deck(self, name):
        if name == "Promenade":
            self.b.part("side", "Pier", name, lambda m: self.seawall(m, name))
            return
        self.b.part("side", "Pier", name, lambda m: self.pier_sides(m, name))

    def seawall(self, m, name):
        x0, z0, x1, z1, top = self.P[name]
        for key, plane, u0, u1, y0 in sides(self.P, self.bases, name):
            vs = [y0, 0.35, 1.2, 2.1, top - 0.3, top]
            for j, (va, vb) in enumerate(zip(vs, vs[1:])):
                if vb <= va:
                    continue
                step = 1.6
                n = max(1, int((u1 - u0) / step))
                for i in range(n):
                    ua, ub = u0 + (u1 - u0) * i / n, u0 + (u1 - u0) * (i + 1) / n
                    if j % 2:
                        ua, ub = ua + step / 2, ub + step / 2
                        if ub > u1:
                            ub = u1
                        if ua >= u1:
                            continue
                    c = P.TRIM if vb == top else geo.jitter((0.46, 0.43, 0.42), self.rng, 0.06)
                    if va < 0.35:
                        c = geo.scale(c, 0.6)
                    K.panel(m, plane, ua + 0.02, va + 0.02, ub - 0.02, vb - 0.02, c, off=0.02)
                K.panel(m, plane, u0, va, u1, vb, (0.22, 0.20, 0.21), off=0.0)

    def dress_old(self, name):
        self.b.part("side", "Old Pier", name, lambda m: self.pier_sides(m, name, P.CHAR_LIGHT, P.CHAR, burnt=True))

    def dress_raft(self, name):
        def build(m):
            x0, z0, x1, z1, top = self.P[name]
            for key, plane, u0, u1, y0 in sides(self.P, self.bases, name):
                K.panel(m, plane, u0, top - 0.3, u1, top, P.PLANK_DARK, off=0.05)
                K.panel(m, plane, u0, y0, u1, top - 0.3, P.UNDER, off=-0.3)
            # Floating drums under the deck, at the corners and middles.
            for fx in (0.18, 0.5, 0.82):
                for fz in (0.18, 0.82):
                    cx, cz = x0 + (x1 - x0) * fx, z0 + (z1 - z0) * fz
                    geo.cylinder_between(m, (cx - 0.38, top - 0.75, cz), (cx + 0.38, top - 0.75, cz), 0.42, 10,
                                         geo.jitter(P.GI_BLUE if fx == 0.5 else P.RED, self.rng, 0.08), K.LIT)
        self.b.part("side", "Rafts", name, build)

    def dress_piling(self, name):
        def build(m):
            x0, z0, x1, z1, top = self.P[name]
            cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
            r = (x1 - x0) / 2 - 0.05
            # A cluster of burnt piles bound with iron, flush with the top.
            for k in range(3):
                a = k * 2 * math.pi / 3 + 0.4
                px, pz = cx + math.cos(a) * r * 0.42, cz + math.sin(a) * r * 0.42
                geo.lathe(m, (px, K.WATER_FLOOR, pz), [(r * 0.55, 0.0), (r * 0.55, top - K.WATER_FLOOR - 0.02)], 9,
                          geo.jitter(P.CHAR, self.rng, 0.1), K.LIT, cap_top=True)
            for y in (top - 0.25, top - 1.4):
                geo.lathe(m, (cx, y, cz), [(r * 1.0, 0.0), (r * 1.0, 0.12)], 12, P.IRON, K.LIT, cap_top=False)
            geo.lathe(m, (cx, K.WATER_FLOOR, cz), [(r * 1.02, 0.0), (r * 1.02, 0.5)], 12, P.WEED, K.LIT, cap_top=False)
        self.b.part("side", "Old Pier", name, build)

    def dress_buoy(self, name):
        def build(m):
            x0, z0, x1, z1, top = self.P[name]
            cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
            r = (x1 - x0) / 2 - 0.02
            body = P.BUOY_ORANGE if name == "SteppingBuoy" else P.BUOY_RED
            geo.lathe(m, (cx, K.WATER_FLOOR, cz), [(r * 0.75, 0.0), (r, 0.4), (r, top - K.WATER_FLOOR - 0.35),
                                                   (r * 0.96, top - K.WATER_FLOOR - 0.02)], 16,
                      [geo.scale(body, 0.6), body, geo.scale(body, 1.05)], K.LIT, cap_top=False)
            geo.lathe(m, (cx, top - 0.9, cz), [(r * 1.02, 0.0), (r * 1.02, 0.25)], 16, P.WHITE, K.LIT, cap_top=False)
        self.b.part("side", "Buoys", name, build)

    def dress_bell(self, name):
        def build(m):
            self.dress_buoy_body(m, name)
            x0, z0, x1, z1, top = self.P[name]
            # The bell cage stands on the float's back edge, out of the way of the balloon.
            cx, cz = x0 + 0.45, (z0 + z1) / 2
            for dx, dz in ((-0.25, -0.25), (0.25, -0.25), (0.25, 0.25), (-0.25, 0.25)):
                K.beam(m, (cx + dx, top, cz + dz), (cx + dx * 0.4, top + 1.6, cz + dz * 0.4), 0.07, P.BUOY_RED)
            geo.lathe(m, (cx, top + 0.95, cz), [(0.22, 0.0), (0.2, 0.15), (0.12, 0.42), (0.05, 0.5)], 10, P.GOLD_DARK, K.LIT)
            K.beacon(m, (cx, top + 1.8, cz), 0.16, P.BUOY_RED, K.SLOW_BLINK, 0.3)
        self.b.part("topprop", "Buoys", name, build)

    def dress_buoy_body(self, m, name):
        x0, z0, x1, z1, top = self.P[name]
        cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
        r = (x1 - x0) / 2 - 0.02
        geo.lathe(m, (cx, K.WATER_FLOOR, cz), [(r * 0.75, 0.0), (r, 0.4), (r, top - K.WATER_FLOOR - 0.02)], 16,
                  [geo.scale(P.BUOY_RED, 0.6), P.BUOY_RED], K.LIT, cap_top=False)

    def dress_pod(self, name):
        """A gondola borrowed off the wheel, sitting on a stack of crates and drums."""
        def build(m):
            x0, z0, x1, z1, top = self.P[name]
            base = self.bases[name]
            cab = 1.1
            colors = {"Pod1": P.RED, "Pod2": P.GI_BLUE, "Pod3": P.GOLD_DARK, "Pod4": P.TEAL}
            body = colors[name]
            for key, plane, u0, u1, y0 in sides(self.P, self.bases, name):
                # the cabin: coloured walls with a window band
                K.panel(m, plane, u0, top - cab, u1, top, body, off=0.02)
                K.panel(m, plane, u0 + 0.2, top - cab + 0.35, u1 - 0.2, top - 0.18, P.GLASS, off=0.04)
                K.panel(m, plane, u0, top - cab - 0.08, u1, top - cab, P.STEEL_DARK, off=0.05)
                # the stack below: crates
                y = y0
                k = 0
                while y < top - cab - 0.1:
                    h = min(1.2, top - cab - y)
                    c = geo.jitter(P.PLANK_DARK if k % 2 == 0 else P.CARDBOARD_DARK, self.rng, 0.08)
                    K.panel(m, plane, u0 + 0.05, y, u1 - 0.05, y + h - 0.04, c, off=0.01)
                    K.beam(m, plane.at(u0 + 0.05, y, 0.03), plane.at(u1 - 0.05, y + h - 0.04, 0.03), 0.06, geo.scale(c, 0.7))
                    y += h
                    k += 1
        self.b.part("side", "Pods", name, build)

        def arm(m):
            # the hanger arm on the roof, now holding nothing
            x0, z0, x1, z1, top = self.P[name]
            cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
            K.beam(m, (cx, top, cz), (cx, top + 0.5, cz), 0.08, P.STEEL_DARK)
        self.b.part("topprop", "Pods", name, arm)

    def dress_vip(self, name):
        """The wheel's tower: red and gold boarding tower with a marquee, posters and the VIP sign."""
        def build(m):
            x0, z0, x1, z1, top = self.P[name]
            for key, plane, u0, u1, y0 in sides(self.P, self.bases, name):
                K.panel(m, plane, u0, y0, u1, top, P.RED_DARK, off=0.0)
                # pilasters and bands
                for u in (u0 + 0.25, (u0 + u1) / 2, u1 - 0.25):
                    K.slab(m, plane, u - 0.18, y0, u + 0.18, top - 0.5, 0.08, P.GOLD_DARK)
                K.slab(m, plane, u0, top - 0.5, u1, top, 0.12, P.GOLD)
                for i in range(int((u1 - u0) / 0.5)):
                    p = plane.at(u0 + 0.25 + i * 0.5, top - 0.25, 0.16)
                    K.bulb(m, p, 0.07, P.BULB, (2.2, 0.0), (i % 2) * 0.5)
                mid = (u0 + u1) / 2
                if key == "s":
                    K.board(m, plane, "vip", mid, y0 + 3.7, 4.2, frame=P.GOLD, off=0.012, depth=0.1)
                    K.board(m, plane, "title", u0 + 1.1, y0 + 1.6, 1.3, frame=P.INK, depth=0.1)
                elif key == "e":
                    K.board(m, plane, "title", mid, y0 + 2.3, 1.6, frame=P.INK, depth=0.1)
                elif key == "w":
                    # The sheer west face Rising Dragon goes up: a gold arrow up it.
                    for i in range(3):
                        v = y0 + 1.4 + i * 1.6
                        tri = [plane.at(mid - 0.55, v, 0.12), plane.at(mid + 0.55, v, 0.12), plane.at(mid, v + 0.7, 0.12)]
                        geo.oriented(m, tri, plane.n, P.GOLD, K.LIT)
        self.b.part("side", "Wheel Tower", name, build)

    def dress_coaster(self, name):
        """A coaster platform: red steel fascia on white lattice towers."""
        def build(m):
            x0, z0, x1, z1, top = self.P[name]
            base = self.bases[name]
            for key, plane, u0, u1, y0 in sides(self.P, self.bases, name):
                K.panel(m, plane, u0, top - 0.5, u1, top, P.COASTER, off=0.05)
                K.panel(m, plane, u0, y0, u1, top - 0.5, P.UNDER, off=-0.4)
            # lattice towers at the corners, standing in the sea (or on the deck)
            for cx in (x0 + 0.4, x1 - 0.4):
                for cz in (z0 + 0.4, z1 - 0.4) if z1 - z0 < 8 else [z0 + 0.4 + (z1 - z0 - 0.8) * t for t in (0, 0.33, 0.66, 1.0)]:
                    K.lattice(m, cx, cz, 0.26, base + 0.05, top - 0.5, P.SUPPORT, bay=1.6, chord=0.08, brace=0.04)
        self.b.part("side", "Coaster", name, build)

    def dress_canopy(self, name):
        pass  # the carousel is dressed by rides.Carousel (its drum is the box's sides)

    def dress_ring(self, name):
        def build(m):
            x0, z0, x1, z1, top = self.P[name]
            for key, plane, u0, u1, y0 in sides(self.P, self.bases, name):
                K.panel(m, plane, u0, y0, u1, top, P.GI_BLUE_DARK, off=0.02)
                n = max(1, int((u1 - u0) / 3.2))
                w = (u1 - u0) / n
                for i in range(n):
                    K.plate(m, plane, "apron", u0 + w * (i + 0.5), (y0 + top) / 2, w * 0.92, off=0.03,
                            height=(top - y0) * 0.86)
        self.b.part("side", "Ring", name, build)

    # ------------------------------------------------------------ small props on tops
    def top_props(self):
        """Bollards, life rings, crates and lamp posts at deck edges."""
        def build(m):
            P_ = self.P
            # Lamp posts along the midway's edges and round the pier head.
            # (none at z -30 on the west edge: that is the dunk buoy's takeoff)
            for x, zs in ((-5.6, (-35.5, -26.0, -20.0, -16.8)), (5.6, (-35.5, -29.0, -23.0, -16.8))):
                for z in zs:
                    K.lamp_post(m, x, z, 3.0, 4.2, P.GI_BLUE, twin=(z == -29.0), phase=0.1 * (z % 3), axis="z")
            for x, z in ((-9.3, 38.6), (15.3, 38.6), (-9.3, 53.4), (15.3, 53.4), (-11.3, 16.6), (7.3, 16.6)):
                K.lamp_post(m, x, z, 3.0, 4.2, P.GOLD_DARK, twin=True)
            # Bollards with rope on the promenade's sea edge and the plaza.
            for x in (-20.5, -14.0, -9.0, 9.0, 14.0, 20.5):
                geo.lathe(m, (x, 3.0, -38.35), [(0.16, 0.0), (0.16, 0.45), (0.22, 0.5), (0.18, 0.62)], 8, P.IRON, K.LIT)
            # Life rings on posts.
            for x, z, yaw in ((-21.4, -39.0, 0.0), (21.4, -39.0, 0.0), (-9.6, -15.4, 0.0), (15.4, 53.4, 0.0)):
                K.post(m, x, z, 3.0, 4.0, 0.08, P.TRIM)
                geo.lathe(m, (x, 3.7, z - 0.06), [(0.18, -0.05), (0.32, -0.05), (0.32, 0.05), (0.18, 0.05)], 12,
                          P.BUOY_RED, K.LIT, cap_top=False)
            # A few crates and drums on the old pier's edges.
            for x, z in ((-33.2, -13.2), (-21.0, -4.8), (-39.2, 36.6), (-24.8, 26.2)):
                geo.box(m, (x, 3.3, z), (0.6, 0.6, 0.6), geo.jitter(P.CHAR_LIGHT, self.rng, 0.1), K.LIT, skip=("bottom",))
        self.b.part("lamps", "Lamps", "Props", build)
