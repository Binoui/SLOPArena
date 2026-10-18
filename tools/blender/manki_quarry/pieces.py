"""Kaboom Quarry's playable pieces, dressed as what they are in Manki's yard: the concrete
dock and haul road, oil-drum pontoons, blasted granite benches with the wired Blast Face,
the crusher shed, the conveyor tower, the hopper, the dump truck sunk to its axles, the
gravel crib, timber rafts, the buoy and the rock islands.

Every walkable top is one face on the planar ground texture (textures.ground paints it).
Sides stay on their collision box; side dressing may stand PROUD out of it, the dynamite
sticks in the drill holes STICK_PROUD (and STICK_DROP below the top). Props on tops are
small, inside the footprint and no higher than TOP_PROP_HIGH, except the declared
detonator at the finish. build.py checks all of it per role.
"""
from __future__ import annotations

import math

from stagekit import geo
import palette as P
import parts as K
import textures as T

PROUD = 0.08
STICK_PROUD = 0.34
STICK_DROP = 0.2
TOP_PROP_HIGH = 1.25
DETONATOR_HIGH = 1.9
TRUCK_CAB = 1.4  # the cab's depth at the dump truck's south end (textures.ground paints its roof)


def sides(pieces, name):
    """Exposed side spans of a piece: [(key, plane, u0, u1, y0)], y0 being the height a
    touching neighbour hides the side up to (K.WATER_FLOOR where nothing touches)."""
    x0, z0, x1, z1, top = pieces[name]
    out = []
    for key, (plane, u0, u1) in zip(("s", "n", "w", "e"), K.box_planes(x0, z0, x1, z1)):
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
            hidden = max([K.WATER_FLOOR] + [h for lo, hi, h in covers if lo <= mid <= hi])
            if top - hidden > 1e-3 and b - a > 1e-3:
                out.append((key, plane, a, b, hidden))
    return out


def face(pieces, name, key):
    """The plane and full u range of one side of a piece."""
    x0, z0, x1, z1, top = pieces[name]
    return dict(zip(("s", "n", "w", "e"), K.box_planes(x0, z0, x1, z1)))[key]


class Pieces:
    def __init__(self, b):
        self.b = b
        self.P = b.pieces
        self.rng = b.rng
        self.route = set(b.data["route"])
        self.targets = {island for _, island, _, _ in b.data["spec"]["targets"]}

    def group_of(self, name):
        return "Route" if name in self.route else "Islands"

    def build(self):
        for name in self.P:
            self.top(name)
            getattr(self, "dress_" + T.kind(name))(name)
        self.blast_face()
        self.decals()
        self.top_props()

    # ------------------------------------------------------------ tops
    def top(self, name):
        x0, z0, x1, z1, top = self.P[name]

        def build(m):
            pts = [(x0, top, z0), (x0, top, z1), (x1, top, z1), (x1, top, z0)]
            with geo.detail(m, 0.45):
                geo.oriented(m, pts, (0, 1, 0), (1.0, 1.0, 1.0), K.GROUND, [T.ground_uv(x, z) for x, y, z in pts])
        self.b.part("top", "Tops", name, build)

    # ------------------------------------------------------------ rock and concrete walls
    def rock_sides(self, m, name, color, seed, step_u=2.2):
        x0, z0, x1, z1, top = self.P[name]
        u_base = 0.0
        for key, plane, u0, u1, y0 in sides(self.P, name):
            def col(u, v, plane=plane):
                x, y, z = plane.at(u, v)
                k = K.wet(v) * (0.86 + 0.24 * K.mottle(x, y, z, seed))
                return geo.scale(color, k)

            def uv(u, v, base=u_base):
                return ((base + u) / K.ROCK_TILE, v / K.ROCK_TILE)
            with geo.detail(m, 0.55):
                K.grid(m, plane, u0, u1, K.rows(y0, top), col, K.ROCK, uv, step_u=step_u)
            u_base += u1 - u0 + 1.7

    def dress_granite(self, name):
        self.b.part("side", self.group_of(name), name, lambda m: self.rock_sides(m, name, P.GRANITE, 1.3))
        x0, z0, x1, z1, top = self.P[name]
        if name in ("NorthBench", "Bench2", "UpperBench"):
            # A few sticks already in the drill holes: this rock is next.
            def sticks(m):
                for key, plane, u0, u1, y0 in sides(self.P, name):
                    if key == "s" and name == "UpperBench":
                        continue  # the Blast Face has its own wired row
                    n = max(1, int((u1 - u0) / 6.0))
                    for i in range(n):
                        if self.rng.random() < 0.45:
                            continue
                        u = u0 + (u1 - u0) * (i + 0.5) / n + self.rng.uniform(-0.8, 0.8)
                        v = top - STICK_DROP - self.rng.uniform(0.15, 0.6)
                        if v < max(y0, 0.4) + 0.2:
                            continue
                        self.stick(m, plane, u, v, self.rng.uniform(0.18, 0.3))
            self.b.part("sideprop", "Props", name, sticks)

    def dress_rock(self, name):
        x0, z0, x1, z1, top = self.P[name]
        tint = P.ROCK if name in self.targets or name == "LastPillar" else P.SCREE
        tint = geo.jitter(tint, self.rng, 0.04)
        self.b.part("side", self.group_of(name), name, lambda m: self.rock_sides(m, name, tint, (x0 + z0) * 0.37, step_u=1.6))

    def dress_concrete(self, name):
        x0, z0, x1, z1, top = self.P[name]

        def build(m):
            for key, plane, u0, u1, y0 in sides(self.P, name):
                def col(u, v, plane=plane):
                    x, y, z = plane.at(u, v)
                    return geo.scale(P.CONCRETE, K.wet(v) * (0.9 + 0.14 * K.mottle(x, y, z, 3.0)))
                with geo.detail(m, 0.7):
                    K.grid(m, plane, u0, u1, K.rows(y0, top - 0.14), col, step_u=2.0)
                    K.panel(m, plane, u0, top - 0.14, u1, top, geo.scale(P.CONCRETE, 1.06))
                if y0 > 0.0:
                    continue
                with geo.detail(m, 0.25):
                    # Rubber bumpers and the rust they bleed.
                    n = max(1, int((u1 - u0) / 3.2))
                    for i in range(n):
                        u = u0 + (u1 - u0) * (i + 0.5) / n
                        K.slab(m, plane, u - 0.15, 0.05, u + 0.15, top - 0.2, 0.07, P.TIRE)
                        streak = self.rng.uniform(0.25, 0.6)
                        K.panel(m, plane, u + 0.22, top - 0.2 - streak, u + 0.27, top - 0.18,
                                [P.CONCRETE, P.CONCRETE, P.RUST, P.RUST], off=0.004)
        self.b.part("side", self.group_of(name), name, build)

    # ------------------------------------------------------------ steel, timber and machines
    def dress_steel(self, name):
        """A pontoon: a steel deck plate on stacked oil drums and steel pallets."""
        x0, z0, x1, z1, top = self.P[name]
        rim = 0.16

        def build(m):
            with geo.detail(m, 0.35):
                for key, plane, u0, u1, y0 in sides(self.P, name):
                    K.panel(m, plane, u0, top - rim, u1, top, P.STEEL_DARK)
                geo.oriented(m, [(x0, top - rim, z0), (x1, top - rim, z0), (x1, top - rim, z1), (x0, top - rim, z1)],
                             (0, -1, 0), P.STEEL_DARK, K.LIT)
                height = top - rim - K.WATER_FLOOR
                tiers = max(1, round(height / 0.9))
                h = height / tiers
                pallet = 0.07
                r = min(x1 - x0, z1 - z0) / 6 - 0.02
                for t in range(tiers):
                    ya = K.WATER_FLOOR + t * h
                    yb = ya + h - (pallet if t < tiers - 1 else 0.0)
                    for i in range(3):
                        for j in range(3):
                            if i == 1 and j == 1:
                                continue
                            cx = x0 + (x1 - x0) * (i + 0.5) / 3
                            cz = z0 + (z1 - z0) * (j + 0.5) / 3
                            color = geo.jitter(self.rng.choice(P.DRUM_COLORS), self.rng, 0.06)
                            K.drum(m, (cx, ya, cz), radius=r / 1.04, height=yb - ya, color=color, cap=False)
                    if t < tiers - 1:
                        geo.box_minmax(m, (x0 + 0.04, yb, z0 + 0.04), (x1 - 0.04, yb + pallet, z1 - 0.04), P.STEEL_DARK, K.LIT)
        self.b.part("side", "Route", name, build)

    def dress_wood(self, name):
        """A raft: a plank deck on lashed drums, half sunk."""
        x0, z0, x1, z1, top = self.P[name]
        deck = 0.2
        (a0, b0), (a1, b1) = T.uv("planks")

        def build(m):
            with geo.detail(m, 0.5):
                for key, plane, u0, u1, y0 in sides(self.P, name):
                    n = max(1, int(round((u1 - u0) / 1.2)))
                    for i in range(n):
                        ua, ub = u0 + (u1 - u0) * i / n, u0 + (u1 - u0) * (i + 1) / n
                        K.panel(m, plane, ua, top - deck, ub, top, geo.jitter(P.WOOD, self.rng, 0.06),
                                uvs=((a0, b0), (a1, b0 + (b1 - b0) * 0.25)))
                geo.oriented(m, [(x0, top - deck, z0), (x1, top - deck, z0), (x1, top - deck, z1), (x0, top - deck, z1)],
                             (0, -1, 0), P.WOOD_DARK, K.LIT)
                r = min(0.3, (top - deck + 0.25) / 1.6)
                cy = max(K.WATER_FLOOR + r, top - deck - r * 0.8)
                rows_ = max(2, int((z1 - z0) / 1.0))
                for j in range(rows_):
                    cz = z0 + (z1 - z0) * (j + 0.5) / rows_
                    half = (x1 - x0) / 2 - 0.05
                    for side in (-1, 1):
                        cx = (x0 + x1) / 2 + side * half / 2
                        color = geo.jitter(self.rng.choice(P.DRUM_COLORS), self.rng, 0.06)
                        K.lying_drum(m, (cx, cy, cz), r, half - 0.04, 0.0, color)
                # Rope lashings over the deck edge.
                for key, plane, u0, u1, y0 in sides(self.P, name):
                    for u in (u0 + 0.35, u1 - 0.35):
                        K.panel(m, plane, u - 0.04, top - deck - 0.12, u + 0.04, top, P.LABEL, off=0.012)
        self.b.part("side", "Route" if name in self.route else "Islands", name, build)

    def dress_buoy(self, name):
        """A square float in red and white stripes."""
        x0, z0, x1, z1, top = self.P[name]

        def build(m):
            with geo.detail(m, 0.3):
                for key, plane, u0, u1, y0 in sides(self.P, name):
                    n = max(2, int(round((u1 - u0) / 0.7)))
                    for i in range(n):
                        ua, ub = u0 + (u1 - u0) * i / n, u0 + (u1 - u0) * (i + 1) / n
                        c = P.MASK_RED if i % 2 == 0 else P.EYE_WHITE
                        K.grid(m, plane, ua, ub, [K.WATER_FLOOR, K.WET, top - 0.1],
                               lambda u, v, c=c: geo.scale(c, K.wet(v)), step_u=ub - ua + 1)
                        K.panel(m, plane, ua, top - 0.1, ub, top, P.INK)
        self.b.part("side", "Islands", name, build)

    def dress_roof(self, name):
        """The crusher shed: concrete plinth, patched corrugated walls, the door and its plate."""
        x0, z0, x1, z1, top = self.P[name]
        plinth, trim = 0.6, 0.25
        (a0, b0), (a1, b1) = T.uv("corrugated")

        def build(m):
            with geo.detail(m, 0.4):
                for key, plane, u0, u1, y0 in sides(self.P, name):
                    K.grid(m, plane, u0, u1, [K.WATER_FLOOR, K.WET, plinth],
                           lambda u, v: geo.scale(P.CONCRETE_DARK, K.wet(v)), step_u=3.0)
                    K.panel(m, plane, u0, top - trim, u1, top, P.STEEL_DARK)
                    n = max(1, int(round((u1 - u0) / 1.5)))
                    for i in range(n):
                        ua, ub = u0 + (u1 - u0) * i / n, u0 + (u1 - u0) * (i + 1) / n
                        roll = self.rng.random()
                        c = (0.95, 0.60, 0.36) if roll < 0.62 else P.ROOF if roll < 0.86 else P.RUST
                        K.panel(m, plane, ua, plinth, ub, top - trim, geo.jitter(c, self.rng, 0.05), uvs=T.uv("corrugated"))
            with geo.detail(m, 0.15):
                plane, u0, u1 = face(self.P, name, "s")
                mid = (u0 + u1) / 2
                K.slab(m, plane, mid - 1.45, plinth, mid + 1.45, plinth + 2.9, 0.05, P.STEEL_DARK, bottom=False)
                K.plate(m, plane, "door", mid, plinth + 1.45, 2.7, off=0.062)
                K.plate(m, plane, "crusher", mid, plinth + 3.55, 2.6, off=0.02)
                plane, u0, u1 = face(self.P, name, "w")
                for u in (u0 + 1.6, u1 - 1.6):
                    K.slab(m, plane, u - 0.75, 2.4, u + 0.75, 3.9, 0.04, P.STEEL_DARK, bottom=False)
                    K.plate(m, plane, "window", u, 3.15, 1.35, off=0.05)
                plane, u0, u1 = face(self.P, name, "e")
                K.plate(m, plane, "smoking", (u0 + u1) / 2, 3.0, 2.4, off=0.02)
                plane, u0, u1 = face(self.P, name, "n")
                K.sticker(m, plane, "tag", u0 + 1.6, 2.6, 1.7, off=0.02)
        self.b.part("side", "Route", name, build)

    def dress_grate(self, name):
        """The conveyor tower: steel cladding between corner columns, braced and girted."""
        x0, z0, x1, z1, top = self.P[name]

        def build(m):
            with geo.detail(m, 0.3):
                for key, plane, u0, u1, y0 in sides(self.P, name):
                    K.grid(m, plane, u0, u1, K.rows(y0, top - 0.2, 1.4),
                           lambda u, v, plane=plane: geo.scale(P.STEEL, K.wet(v) * (0.9 + 0.16 * K.mottle(*plane.at(u, v), 5.0))),
                           step_u=1.0)
                    K.panel(m, plane, u0, top - 0.2, u1, top, P.CRANE)
                    for v in (1.4, 2.8):
                        K.slab(m, plane, u0, v - 0.08, u1, v + 0.08, 0.05, P.STEEL_DARK)
                    for u in (u0, u1 - 0.3):
                        K.slab(m, plane, u, K.WATER_FLOOR, u + 0.3, top - 0.2, 0.06, P.STEEL_DARK, bottom=False)
                    # X bracing in each bay, painted dark on the cladding.
                    bays = [(K.WET, 1.32), (1.48, 2.72), (2.88, top - 0.2)]
                    n = max(1, int(round((u1 - u0 - 0.6) / 2.0)))
                    for i in range(n):
                        ua = u0 + 0.3 + (u1 - u0 - 0.6) * i / n
                        ub = u0 + 0.3 + (u1 - u0 - 0.6) * (i + 1) / n
                        for va, vb in bays:
                            for (p0, p1) in (((ua, va), (ub, vb)), ((ua, vb), (ub, va))):
                                dx, dy = p1[0] - p0[0], p1[1] - p0[1]
                                L = math.hypot(dx, dy)
                                nx_, ny_ = -dy / L * 0.05, dx / L * 0.05
                                quad = [(p0[0] + nx_, p0[1] + ny_), (p1[0] + nx_, p1[1] + ny_),
                                        (p1[0] - nx_, p1[1] - ny_), (p0[0] - nx_, p0[1] - ny_)]
                                geo.oriented(m, [plane.at(u, v, 0.02) for u, v in quad], plane.n, P.STEEL_DARK, K.LIT)
            plane, u0, u1 = face(self.P, name, "w")
            K.board(m, plane, "monkey", (u0 + u1) / 2, 2.1, 1.2, off=0.012, depth=0.06)
        self.b.part("side", "Route", name, build)

    def dress_gravel(self, name):
        x0, z0, x1, z1, top = self.P[name]
        if name == "Hopper":
            def build(m):
                with geo.detail(m, 0.35):
                    for key, plane, u0, u1, y0 in sides(self.P, name):
                        K.grid(m, plane, u0, u1, K.rows(y0, top - 0.6),
                               lambda u, v, plane=plane: geo.scale(P.MANKI_ORANGE, K.wet(v) * (0.85 + 0.2 * K.mottle(*plane.at(u, v), 7.0))),
                               step_u=1.0)
                        n = max(1, int(round((u1 - u0) / 1.0)))
                        for i in range(n):
                            ua, ub = u0 + (u1 - u0) * i / n, u0 + (u1 - u0) * (i + 1) / n
                            K.panel(m, plane, ua, top - 0.6, ub, top - 0.12, (1, 1, 1), uvs=T.uv("hazard"))
                        K.panel(m, plane, u0, top - 0.12, u1, top, P.STEEL_DARK)
                        for i in range(1, n):
                            u = u0 + (u1 - u0) * i / n
                            K.slab(m, plane, u - 0.05, K.WET, u + 0.05, top - 0.62, 0.05, P.RUST_DARK)
                    plane, u0, u1 = face(self.P, name, "w")
                    K.board(m, plane, "safety", (u0 + u1) / 2, 1.5, 2.3, off=0.012, depth=0.06)
            self.b.part("side", "Route", name, build)
            return

        (a0, b0), (a1, b1) = T.uv("planks")

        def crib(m):
            with geo.detail(m, 0.5):
                for key, plane, u0, u1, y0 in sides(self.P, name):
                    K.grid(m, plane, u0, u1, [K.WATER_FLOOR, K.WET], lambda u, v: geo.scale(P.WOOD_DARK, K.wet(v)), step_u=4)
                    n = max(1, int(round((u1 - u0) / 1.2)))
                    for i in range(n):
                        ua, ub = u0 + (u1 - u0) * i / n, u0 + (u1 - u0) * (i + 1) / n
                        K.panel(m, plane, ua, K.WET, ub, top, geo.jitter(P.WOOD, self.rng, 0.07),
                                uvs=((a0, b0), (a0 + (a1 - a0) * 0.6, b1)))
                    for u in (u0, u1 - 0.18):
                        K.slab(m, plane, u, K.WATER_FLOOR, u + 0.18, top, 0.06, P.WOOD_DARK, bottom=False)
        self.b.part("side", "Route", name, crib)

    def dress_truck(self, name):
        """The dump truck, sunk to its axles, cab to the south with googly eyes in the windscreen."""
        x0, z0, x1, z1, top = self.P[name]
        cab_z = z0 + TRUCK_CAB
        chassis = 0.9

        def build(m):
            with geo.detail(m, 0.25):
                # The chassis, narrower than the body so the wheels sit outboard of it.
                geo.box_minmax(m, (x0 + 0.45, K.WATER_FLOOR, z0 + 0.1), (x1 - 0.45, chassis, z1 - 0.1), P.STEEL_DARK, K.LIT,
                               skip=("bottom",))
                # Bed and cab sides: east and west split at the cab.
                for key in ("w", "e"):
                    plane, u0, u1 = face(self.P, name, key)
                    cab_u = (-cab_z) if key == "w" else cab_z
                    cab = (cab_u, u1) if key == "w" else (u0, cab_u)
                    bed = (u0, cab_u) if key == "w" else (cab_u, u1)
                    K.panel(m, plane, bed[0], chassis, bed[1], top, P.HAZARD)
                    for i in range(1, 5):
                        u = bed[0] + (bed[1] - bed[0]) * i / 5
                        K.slab(m, plane, u - 0.07, chassis + 0.05, u + 0.07, top - 0.1, 0.05, P.CRANE_DARK)
                    K.panel(m, plane, cab[0], chassis, cab[1], top, P.MANKI_ORANGE)
                    K.slab(m, plane, cab[0] + 0.15, chassis + 0.55, cab[1] - 0.15, top - 0.12, 0.02, P.INK)  # side window
                    K.sticker(m, plane, "tag", (cab[0] + cab[1]) / 2, chassis + 0.32, 0.46, off=0.012)
                    # Wheels: axles at the cab and the bed's rear, half under the water.
                    for wu in ((cab[0] + cab[1]) / 2, bed[0] + (bed[1] - bed[0]) * (0.7 if key == "e" else 0.3)):
                        K.tyre(m, plane.at(wu, 0.42, -0.2), radius=0.62, width=0.4, yaw=0.0, upright=True)  # axle along x
                        geo.cylinder_between(m, plane.at(wu, 0.42, -0.02), plane.at(wu, 0.42, 0.01), 0.28, 8, P.CRANE, K.LIT)
                # The front: windscreen with eyes, grille, lamps, bumper.
                plane, u0, u1 = face(self.P, name, "s")
                K.panel(m, plane, u0, chassis, u1, top, P.MANKI_ORANGE)
                K.panel(m, plane, u0 + 0.25, 1.45, u1 - 0.25, top - 0.12, (0.14, 0.2, 0.26), off=0.01)
                K.googly(m, plane, u0 + 1.2, 1.76, 0.36, look=(0.55, -0.35), off=0.02)
                K.googly(m, plane, u1 - 1.2, 1.76, 0.36, look=(-0.6, 0.55), off=0.02)
                K.slab(m, plane, u0 + 1.3, chassis + 0.05, u1 - 1.3, 1.35, 0.03, P.INK)
                for u in (u0 + 0.6, u1 - 0.6):
                    K.slab(m, plane, u - 0.25, 1.0, u + 0.25, 1.3, 0.04, P.STEEL_DARK)
                K.slab(m, plane, u0 + 0.05, 0.55, u1 - 0.05, chassis, 0.07, P.STEEL_DARK)
                # The tailgate.
                plane, u0, u1 = face(self.P, name, "n")
                K.panel(m, plane, u0, chassis, u1, top, P.HAZARD)
                K.slab(m, plane, u0 + 0.2, top - 0.35, u1 - 0.2, top - 0.15, 0.05, P.CRANE_DARK)
            with K.glow(m, P.FLAME):
                plane, u0, u1 = face(self.P, name, "s")
                for u in (u0 + 0.6, u1 - 0.6):
                    K.glow_poly(m, [plane.at(u - 0.18, 1.06, 0.045), plane.at(u + 0.18, 1.06, 0.045),
                                    plane.at(u + 0.18, 1.24, 0.045), plane.at(u - 0.18, 1.24, 0.045)], P.SPARK)
        self.b.part("side", "Route", name, build)

    # ------------------------------------------------------------ the Blast Face and decals
    def stick(self, m, plane, u, v, out, color=P.DYNAMITE):
        """A dynamite stick poking `out` metres from a drill hole, tipped slightly down."""
        a = plane.at(u, v, -0.05)
        b = plane.at(u + self.rng.uniform(-0.05, 0.05), v - out * 0.18, out)
        K.dynamite(m, a, b, radius=0.06, color=color)
        return b

    def blast_face(self):
        """The Upper Bench's south face over the jetpack pad: the bullseye, a KA-BOOM and a
        wired row of dynamite along its lip."""
        name = "UpperBench"
        x0, z0, x1, z1, top = self.P[name]
        plane, u0, u1 = face(self.P, name, "s")
        pad_x = -10.0

        def decals(m):
            with geo.detail(m, 0.2):
                K.board(m, plane, "bullseye", pad_x, 3.75, 4.0, frame=P.STEEL_DARK, rim=0.12)
                K.board(m, plane, "kaboom", -0.6, 2.9, 3.5, frame=P.INK)
                for u, v, r, c in ((-14.2, 2.4, 0.5, P.MANKI_ORANGE), (-6.2, 1.9, 0.35, P.HAZARD), (-12.6, 5.7, 0.3, P.MASK_RED),
                                   (1.2, 5.8, 0.4, P.MANKI_ORANGE), (-3.4, 0.9, 0.45, P.MASK_RED)):
                    K.splat(m, plane, u, v, r, c, seed=u * 3.0, off=0.01)
        self.b.part("side", "Route", name, decals)

        def wired(m):
            ends = []
            u = -15.4
            while u < 1.6:
                ends.append(self.stick(m, plane, u, top - STICK_DROP - 0.28, 0.26))
                u += 0.95
            pts = []
            for i in range(len(ends) - 1):
                a, b = ends[i], ends[i + 1]
                for p in geo.catenary(a, b, 0.12, 4)[:-1]:
                    pts.append(p)
            pts.append(ends[-1])
            geo.tube(m, pts, 0.018, 4, P.INK, K.LIT)
        self.b.part("sideprop", "Props", name, wired)

    def decals(self):
        """Graffiti sprayed on the rocks and signs bolted to them, where the route looks."""
        sprayed = [
            ("LastPillar", "s", "IT WASN'T ME", 1.6, 0.42, P.EYE_WHITE),   # read from the start
            ("SeaStack", "n", "KABOOM", 2.0, 0.45, P.MANKI_ORANGE),        # from the haul road
            ("NorthPillar", "s", "MANKI", 2.8, 0.5, P.HAZARD),             # from the Upper Bench
        ]
        for name, key, text, v, height, color in sprayed:
            plane, u0, u1 = face(self.P, name, key)

            def build(m, plane=plane, u=(u0 + u1) / 2, text=text, v=v, height=height, color=color):
                with geo.detail(m, 0.2):
                    K.spray(m, plane, text, u, v, height, color, rng=self.rng, slant=0.08)
            self.b.part("side", self.group_of(name), name, build)
        bolted = [
            ("HighStack", "w", "monkey", 0.0, 2.4, 1.5),         # from the pontoons
            ("EastRock", "w", "radius", 0.0, 3.0, 2.4),          # from the Upper Bench's east edge
            ("FarStack", "w", "smoking", 0.0, 1.8, 2.4),         # from the hopper
            ("NorthBench", "s", "safety", -36.0, 0.45, 0.96),   # low, between pontoon 4 and the high stack
        ]
        for name, key, cell, at, v, width in bolted:
            plane, u0, u1 = face(self.P, name, key)
            u = (u0 + u1) / 2 if at == 0.0 else at  # `at` is a u on the face (a world x on a south face)

            def build(m, plane=plane, cell=cell, u=u, v=v, width=width):
                with geo.detail(m, 0.25):
                    K.board(m, plane, cell, u, v, width, rim=0.06, depth=0.04)
            self.b.part("side", self.group_of(name), name, build)
        # Paint splats on a few rocks: Manki tests his colours here.
        for name in ("HopIslet", "TwinIslet", "TrioBlock", "BaseIslet", "LowIslet", "EastStone"):
            x0, z0, x1, z1, top = self.P[name]

            def build(m, name=name, top=top):
                for key, plane, u0, u1, y0 in sides(self.P, name):
                    if self.rng.random() < 0.5:
                        continue
                    u = self.rng.uniform(u0 + 0.4, u1 - 0.4)
                    r = min(0.28, max(0.1, (top + 0.05) / 3))
                    K.splat(m, plane, u, min(top - r - 0.06, self.rng.uniform(0.5, 0.9)), r,
                            self.rng.choice((P.MANKI_ORANGE, P.HAZARD, P.MASK_RED)), seed=u, off=0.01)
            self.b.part("side", "Islands", name, build)

    # ------------------------------------------------------------ props on tops
    def top_props(self):
        P_ = self.P
        dock = P_["LoadingDock"]

        def dock_props(m):
            y = dock[4]
            with geo.detail(m, 0.3):
                for x in (-8.0, -2.0, 4.0):   # mooring bollards on the north edge
                    geo.lathe(m, (x, y, dock[3] - 0.4), [(0.2, 0.0), (0.16, 0.32), (0.24, 0.36), (0.24, 0.44), (0.0, 0.46)],
                              10, [P.CRANE, P.CRANE_DARK, P.CRANE, P.CRANE], K.LIT, cap_top=False)
                K.cone(m, (-10.6, y, -39.4), yaw=0.3)
                K.cone(m, (-9.7, y, -39.5), yaw=1.1)
                # One cone has had a day.
                geo.cylinder_between(m, (-8.7, y + 0.17, -39.45), (-8.05, y + 0.12, -39.2), 0.17, 8, P.MANKI_ORANGE, K.LIT, radius_b=0.05)
                for k in range(2):
                    K.tyre(m, (15.2, y + 0.14 + k * 0.28, -39.2), radius=0.5, width=0.28)
        self.b.part("topprop", "Props", "LoadingDock", dock_props)

        bench = P_["NorthBench"]

        def barrow(m):
            # A wheelbarrow of dynamite, parked facing the Blast Face.
            x, z, y = -41.0, 10.0, bench[4]
            with geo.detail(m, 0.3):
                geo.box_minmax(m, (x - 0.55, y + 0.35, z - 0.35), (x + 0.45, y + 0.72, z + 0.35), P.MANKI_ORANGE, K.LIT)
                for dz in (-0.3, 0.3):
                    geo.cylinder_between(m, (x - 0.55, y + 0.36, z + dz), (x - 1.2, y + 0.62, z + dz * 1.2), 0.03, 6, P.WOOD_DARK, K.LIT)
                    geo.cylinder_between(m, (x - 0.3, y + 0.36, z + dz), (x - 0.35, y, z + dz), 0.03, 6, P.STEEL_DARK, K.LIT)
                K.tyre(m, (x + 0.6, y + 0.2, z), radius=0.2, width=0.1, yaw=math.pi / 2, upright=True)
                for i in range(7):
                    a = (x - 0.4 + 0.12 * i, y + 0.74, z - 0.25 + 0.08 * (i % 3))
                    b = (a[0] + 0.05, a[1] + 0.06, a[2] + 0.45)
                    K.dynamite(m, a, b, radius=0.05)
        self.b.part("topprop", "Props", "NorthBench", barrow)

        gantry = P_["ConveyorGantry"]
        for name, spots in (("ConveyorGantry", ((gantry[0] + 0.3, gantry[3] - 0.3), (gantry[2] - 0.3, gantry[3] - 0.3))),
                            ("CrusherRoof", ((P_["CrusherRoof"][2] - 0.35, P_["CrusherRoof"][3] - 0.35),)),
                            ("DumpTruck", (((P_["DumpTruck"][0] + P_["DumpTruck"][2]) / 2, P_["DumpTruck"][1] + 0.7),))):
            top = P_[name][4]

            def lamps(m, spots=spots, top=top):
                for i, (x, z) in enumerate(spots):
                    K.beacon(m, (x, top + 0.3, z), radius=0.16, phase=0.37 * i)
            self.b.part("topprop", "Props", name, lamps)

        crusher = P_["CrusherRoof"]

        def vent(m):
            x, z, y = crusher[0] + 1.2, crusher[1] + 4.6, crusher[4]
            geo.box(m, (x, y + 0.3, z), (0.7, 0.6, 0.7), P.STEEL_DARK, K.LIT, skip=("bottom",))
            geo.box(m, (x, y + 0.66, z), (0.9, 0.12, 0.9), P.STEEL, K.LIT)
        self.b.part("topprop", "Props", "CrusherRoof", vent)

        pile = P_["GravelPile"]

        def shovel(m):
            x, z, y = pile[2] - 0.6, pile[3] - 0.6, pile[4]
            geo.cylinder_between(m, (x, y - 0.05, z), (x - 0.25, y + 1.05, z + 0.12), 0.03, 6, P.WOOD, K.LIT)
            geo.box(m, (x + 0.02, y + 0.05, z), (0.26, 0.3, 0.03), P.STEEL_DARK, K.LIT, yaw=0.4)
            geo.cylinder_between(m, (x - 0.33, y + 1.05, z + 0.08), (x - 0.17, y + 1.05, z + 0.16), 0.03, 6, P.WOOD_DARK, K.LIT)
        self.b.part("topprop", "Props", "GravelPile", shovel)

        buoy = P_["FarBuoy"]

        def lamp_post(m):
            x, z, y = buoy[2] - 0.3, buoy[3] - 0.3, buoy[4]
            geo.box(m, (x, y + 0.45, z), (0.1, 0.9, 0.1), P.STEEL_DARK, K.LIT, skip=("bottom",))
            K.beacon(m, (x, y + 1.05, z), radius=0.15, color=P.MASK_RED, anim=K.BLINK, phase=0.5)
        self.b.part("topprop", "Props", "FarBuoy", lamp_post)
