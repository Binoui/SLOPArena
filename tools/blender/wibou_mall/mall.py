"""Neo Mall's playable pieces and its envelope, dressed as what they are: terrazzo balconies with
cream fascia, LED strips and downlit soffits; steel skybridges; glass railings over every
collision railing; the stopped escalator; the glass elevator tower; hanging planters and menu
signs; the fashion podium; and the shopfronts on the envelope walls, level by level.

Every walkable top is one face on the planar ground texture (textures.ground paints it). Side
dressing stays on its collision solid (at most PROUD out of it, never above a walkable top);
fixtures on tops stay at their edges unless declared. build.py checks it per role.
"""
from __future__ import annotations

import math

from stagekit import geo
import palette as P
import parts as K
import textures as T

PROUD = 0.3
LED = 0.06            # LED strip height on a fascia
FLOOR_LINES = (4.0, 9.0, 15.0)
DOME = 24.0
SHOP_H = 3.4          # a shopfront's height above its floor
SIGN_H = 0.55         # a shop sign band


def _spans(solids, name):
    """Exposed rectangles of a box solid's four sides: [(key, plane, u0, u1, y0, y1)], with every
    span hidden by a touching box solid removed."""
    kind, s = solids[name]
    x0, y0, z0, x1, y1, z1 = s
    out = []
    for key, plane, u0, u1 in K.box_planes(x0, z0, x1, z1):
        hides = []
        for other, (k2, b) in solids.items():
            if other == name or k2 != "box":
                continue
            bx0, by0, bz0, bx1, by1, bz1 = b
            if key == "s" and abs(bz1 - z0) < 1e-3:
                lo, hi = bx0, bx1
            elif key == "n" and abs(bz0 - z1) < 1e-3:
                lo, hi = -bx1, -bx0
            elif key == "w" and abs(bx1 - x0) < 1e-3:
                lo, hi = -bz1, -bz0
            elif key == "e" and abs(bx0 - x1) < 1e-3:
                lo, hi = bz0, bz1
            else:
                continue
            lo, hi = max(lo, u0), min(hi, u1)
            ylo, yhi = max(y0, by0), min(y1, by1)
            if hi - lo > 1e-3 and yhi - ylo > 1e-3:
                hides.append((lo, hi, ylo, yhi))
        cuts = sorted({u0, u1, *[h[0] for h in hides], *[h[1] for h in hides]})
        for a, b in zip(cuts, cuts[1:]):
            if b - a < 1e-3:
                continue
            mid = (a + b) / 2
            ys = [(h[2], h[3]) for h in hides if h[0] <= mid <= h[1]]
            free = [(y0, y1)]
            for ylo, yhi in ys:
                nxt = []
                for f0, f1 in free:
                    if yhi <= f0 or ylo >= f1:
                        nxt.append((f0, f1))
                        continue
                    if ylo > f0:
                        nxt.append((f0, ylo))
                    if yhi < f1:
                        nxt.append((yhi, f1))
                free = nxt
            for f0, f1 in free:
                if f1 - f0 > 1e-3:
                    out.append((key, plane, a, b, f0, f1))
    return out


def _merge(spans):
    """Join neighbouring spans on the same plane and height into longer runs."""
    spans = sorted(spans, key=lambda s: (s[0], round(s[4], 3), round(s[5], 3), s[2]))
    out = []
    for s in spans:
        if out and out[-1][0] == s[0] and abs(out[-1][3] - s[2]) < 1e-3 and abs(out[-1][4] - s[4]) < 1e-3 and abs(out[-1][5] - s[5]) < 1e-3:
            out[-1] = (s[0], s[1], out[-1][2], s[3], s[4], s[5])
        else:
            out.append(s)
    return out


class Mall:
    def __init__(self, b):
        self.b = b
        self.P = b.pieces
        self.spec = b.data["spec"]
        self.solids = b.data["solids"]
        self.rng = b.rng
        self.floating = set(self.spec["floating"])

    def build(self):
        for name in self.P:
            self.top(name)
        self.ramp_tops()
        for name in self.P:
            kind = T.kind(name)
            if kind in ("gallery", "food", "bridge"):
                self.balcony(name)
            elif kind == "planter":
                self.planter(name)
            elif kind in ("sign", "speaker"):
                self.hanging_sign(name)
        self.podium()
        self.tower()
        self.escalator()
        self.rails()
        self.walls()
        self.fixtures()

    # ------------------------------------------------------------ tops
    def top(self, name):
        x0, z0, x1, z1, top = self.P[name]

        def build(m):
            pts = [(x0, top, z0), (x0, top, z1), (x1, top, z1), (x1, top, z0)]
            with geo.detail(m, 0.35):
                geo.oriented(m, pts, (0, 1, 0), (1.0, 1.0, 1.0), K.GROUND, [T.ground_uv(x, z) for x, y, z in pts])
        self.b.part("top", "Tops", name, build)

    def ramp_tops(self):
        for name, (kind, s) in self.solids.items():
            if kind != "ramp" or name.startswith("Balustrade"):
                continue
            x0, z0, x1, z1, bs, bn, ys, yn = s

            def build(m, x0=x0, z0=z0, x1=x1, z1=z1, ys=ys, yn=yn):
                pts = [(x0, ys, z0), (x0, yn, z1), (x1, yn, z1), (x1, ys, z0)]
                n = (0.0, z1 - z0, -(yn - ys))
                geo.oriented(m, pts, n, (1.0, 1.0, 1.0), K.GROUND, [T.ground_uv(x, z) for x, y, z in pts])
            self.b.part("ramp", "Tops", name, build)

    # ------------------------------------------------------------ balconies and bridges
    def balcony(self, name):
        x0, z0, x1, z1, top = self.P[name]
        base = self.spec["floating"].get(name)
        steel = T.kind(name) == "bridge"
        face = P.STEEL if steel else P.FASCIA

        def build(m):
            for key, plane, u0, u1, y0, y1 in _merge(_spans(self.solids, name)):
                K.panel(m, plane, u0, y0, u1, y1, face, off=0.02)
                with geo.state(m, alpha=1.0):
                    K.panel(m, plane, u0, y1 - 0.14, u1, y1 - 0.14 + LED, P.LED, off=0.04, mat=K.GLOW, uvs=T.uv("led"))
                K.panel(m, plane, u0, y0, u1, y0 + 0.06, geo.scale(face, 0.5), off=0.03)
            if base is not None:
                # The soffit and its downlights.
                geo.oriented(m, [(x0, base - 0.01, z0), (x1, base - 0.01, z0), (x1, base - 0.01, z1), (x0, base - 0.01, z1)],
                             (0, -1, 0), P.SOFFIT)
                step = 3.0
                nx, nz = max(1, int((x1 - x0) / step)), max(1, int((z1 - z0) / step))
                for i in range(nx):
                    for k in range(nz):
                        cx = x0 + (i + 0.5) * (x1 - x0) / nx
                        cz = z0 + (k + 0.5) * (z1 - z0) / nz
                        with geo.state(m, alpha=1.0):
                            K.hdisc(m, cx, base - 0.02, cz, 0.16, P.LAMP, n=10, mat=K.GLOW, down=True)
        self.b.part("side", "Balconies", name, build)

    # ------------------------------------------------------------ hanging planters and signs
    def planter(self, name):
        x0, z0, x1, z1, top = self.P[name]
        base = self.spec["floating"][name]

        def build(m):
            for key, plane, u0, u1 in K.box_planes(x0, z0, x1, z1):
                K.panel(m, plane, u0, base, u1, top, P.POT, off=0.02)
                K.panel(m, plane, u0, top - 0.08, u1, top, geo.scale(P.POT, 0.8), off=0.04)
            geo.oriented(m, [(x0, base - 0.01, z0), (x1, base - 0.01, z0), (x1, base - 0.01, z1), (x0, base - 0.01, z1)],
                         (0, -1, 0), geo.scale(P.POT, 0.6))
            # trailing vines over the sides
            rng = self.rng
            for key, plane, u0, u1 in K.box_planes(x0, z0, x1, z1):
                for k in range(3):
                    u = u0 + (u1 - u0) * (k + 0.5) / 3 + rng.uniform(-0.15, 0.15)
                    length = rng.uniform(0.4, 0.9)
                    K.panel(m, plane, u - 0.08, top - length, u + 0.08, top - 0.02, P.PLANT, off=0.05)
        self.b.part("side", "Hanging", name, build)

        def chains(m):
            for x, z in ((x0 + 0.1, z0 + 0.1), (x1 - 0.1, z0 + 0.1), (x1 - 0.1, z1 - 0.1), (x0 + 0.1, z1 - 0.1)):
                K.cable(m, (x, top, z), ((x0 + x1) / 2, top + 2.2, (z0 + z1) / 2), P.STEEL_DARK, 0.025)
            K.cable(m, ((x0 + x1) / 2, top + 2.2, (z0 + z1) / 2), ((x0 + x1) / 2, DOME + 4.0, (z0 + z1) / 2), P.STEEL_DARK, 0.03)
        self.b.part("cable", "Cables", name + "Chains", chains)

    def hanging_sign(self, name):
        x0, z0, x1, z1, top = self.P[name]
        base = self.spec["floating"][name]
        speaker = T.kind(name) == "speaker"
        menu = {"Sign1": "menu_wok", "Sign3": "menu_pretzel"}.get(name, "speaker")

        def build(m):
            for key, plane, u0, u1 in K.box_planes(x0, z0, x1, z1):
                K.panel(m, plane, u0, base, u1, top, P.INK, off=0.02)
                w = u1 - u0 - 0.08
                if speaker:
                    K.plate(m, plane, "speaker", (u0 + u1) / 2, (base + top) / 2, min(w, top - base - 0.04), off=0.03)
                else:
                    with geo.state(m, alpha=1.0):
                        K.panel(m, plane, u0 + 0.04, base + 0.03, u1 - 0.04, top - 0.03, (1.0, 1.0, 1.0), off=0.03,
                                mat=K.GLOW, uvs=T.sub_uv(menu, 0.0, 0.3, 1.0, 1.0))
            geo.oriented(m, [(x0, base - 0.01, z0), (x1, base - 0.01, z0), (x1, base - 0.01, z1), (x0, base - 0.01, z1)],
                         (0, -1, 0), P.INK)
        self.b.part("side", "Hanging", name, build)

        def cables(m):
            for x, z in ((x0 + 0.1, z0 + 0.1), (x1 - 0.1, z1 - 0.1)):
                K.cable(m, (x, top, z), (x, DOME + 4.0, z), P.STEEL_DARK, 0.025)
        self.b.part("cable", "Cables", name + "Cables", cables)

    # ------------------------------------------------------------ the fashion podium
    def podium(self):
        x0, z0, x1, z1, top = self.P["Podium"]

        def build(m):
            for key, plane, u0, u1, y0, y1 in _merge(_spans(self.solids, "Podium")):
                K.panel(m, plane, u0, y0, u1, y0 + 0.6, P.INK, off=0.02)
                K.panel(m, plane, u0, y0 + 0.6, u1, y1 - 0.5, P.CREAM, off=0.02)
                K.panel(m, plane, u0, y1 - 0.5, u1, y1, P.CARPET, off=0.03)
                with geo.state(m, alpha=1.0):
                    K.panel(m, plane, u0, y1 - 0.62, u1, y1 - 0.56, P.LED, off=0.04, mat=K.GLOW, uvs=T.uv("led"))
                if u1 - u0 > 6.0:
                    for u in (u0 + (u1 - u0) * 0.3, u0 + (u1 - u0) * 0.7):
                        K.plate(m, plane, "fashion", u, (y0 + y1) / 2 + 0.2, 3.2, off=0.04)
                else:
                    K.plate(m, plane, "banner_open", (u0 + u1) / 2, (y0 + y1) / 2, 0.7, off=0.04)
        self.b.part("side", "Podium", "Podium", build)

    # ------------------------------------------------------------ the glass elevator tower
    def tower(self):
        x0, z0, x1, z1, top = self.P["Tower"]
        y0 = self.spec["water_y"]

        def build(m):
            for key, plane, u0, u1, a, b in _merge(_spans(self.solids, "Tower")):
                with geo.detail(m, 0.0):
                    K.panel(m, plane, u0, a, u1, b, P.RAIL_GLASS, off=0.02, mat=K.GLASS)
                # mullions and floor bands
                n = max(1, int(round((u1 - u0) / 1.4)))
                for i in range(n + 1):
                    u = u0 + (u1 - u0) * i / n
                    K.panel(m, plane, u - 0.06, a, u + 0.06, b, P.STEEL_DARK, off=0.05)
                for y in (0.3, 4.0, 9.0, b - 0.25):
                    if a <= y <= b:
                        K.panel(m, plane, u0, max(a, y - 0.12), u1, min(b, y + 0.12), P.STEEL, off=0.06)
                # the stuck car, lit, at level 2
                if key in ("e", "w"):
                    with geo.state(m, alpha=0.8):
                        K.panel(m, plane, u0 + 0.5, 4.3, u1 - 0.5, 6.6, P.LAMP, off=0.03, mat=K.GLOW, uvs=[T.LAMP_UV, T.LAMP_UV])
                if key == "s":
                    K.plate(m, plane, "out_of_service", (u0 + u1) / 2, 11.5, 3.2, off=0.07)
                if key == "w":
                    K.lit_sign(m, plane, "red_pill", u0 + 0.9, 13.4, 1.3, depth=0.08)
                with geo.state(m, alpha=1.0):
                    K.panel(m, plane, u0, b - 0.16, u1, b - 0.1, P.LED, off=0.07, mat=K.GLOW, uvs=T.uv("led"))
        self.b.part("side", "Tower", "Tower", build)

    # ------------------------------------------------------------ the stopped escalator
    def escalator(self):
        for name, (kind, s) in self.solids.items():
            if kind != "ramp":
                continue
            x0, z0, x1, z1, bs, bn, ys, yn = s
            balustrade = name.startswith("Balustrade")

            def build(m, x0=x0, z0=z0, x1=x1, z1=z1, bs=bs, bn=bn, ys=ys, yn=yn, balustrade=balustrade):
                d = 0.02
                for xx, nx in ((x0 - d, -1.0), (x1 + d, 1.0)):
                    pts = [(xx, bs, z0), (xx, bn, z1), (xx, yn, z1), (xx, ys, z0)]
                    if balustrade:
                        geo.oriented(m, [(xx, ys - 0.95, z0), (xx, yn - 0.95, z1), (xx, yn - 0.06, z1), (xx, ys - 0.06, z0)],
                                     (nx, 0, 0), P.RAIL_GLASS, K.GLASS)
                        geo.oriented(m, [(xx, bs, z0), (xx, bn, z1), (xx, yn - 0.95, z1), (xx, ys - 0.95, z0)],
                                     (nx, 0, 0), P.STEEL)
                    else:
                        geo.oriented(m, pts, (nx, 0, 0), P.STEEL)
                # the underside
                geo.oriented(m, [(x0, bs - 0.01, z0), (x1, bs - 0.01, z0), (x1, bn - 0.01, z1), (x0, bn - 0.01, z1)],
                             (0, -(z1 - z0), -(bn - bs)), P.STEEL_DARK)
                # end faces
                for zz, nz, yb, yt in ((z0 - d, -1.0, bs, ys), (z1 + d, 1.0, bn, yn)):
                    geo.oriented(m, [(x0, yb, zz), (x1, yb, zz), (x1, yt, zz), (x0, yt, zz)], (0, 0, nz), P.STEEL_DARK)
                if balustrade:
                    # the black moving handrail along the top
                    for xx in (x0 - 0.03, x1 + 0.03):
                        pass
                    geo.oriented(m, [(x0 - 0.04, ys + 0.01, z0), (x0 - 0.04, yn + 0.01, z1), (x1 + 0.04, yn + 0.01, z1), (x1 + 0.04, ys + 0.01, z0)],
                                 (0, z1 - z0, -(yn - ys)), P.HANDRAIL)
            self.b.part("ramp", "Escalator", name, build)
        # OUT OF ORDER on the balustrades at the missing steps, facing into the corridor.
        _, (bx0, bz0, bx1, bz1, bbs, bbn, bys, byn) = self.solids["BalustradeW"]

        def signs(m):
            slope = (byn - bys) / (bz1 - bz0)
            for z in (7.2, 10.4):
                y = bys + (z - bz0) * slope - 0.45
                plane = K.Plane.facing((bx1 + 0.03, 0.0, 0.0), (1.0, 0.0))
                K.plate(m, plane, "out_of_order", -z, y, 1.4, off=0.0)
        self.b.part("ramp", "Escalator", "OutOfOrder", signs)

    # ------------------------------------------------------------ railings
    def rails(self):
        for name, (x0, z0, x1, z1, host) in self.spec["rails"].items():
            top = self.P[host][4]

            def build(m, x0=x0, z0=z0, x1=x1, z1=z1, top=top):
                along_x = (x1 - x0) >= (z1 - z0)
                cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
                glass_lo, glass_hi = top + 0.08, top + 0.9
                if along_x:
                    for zz, nz in ((z0 - 0.01, -1.0), (z1 + 0.01, 1.0)):
                        geo.oriented(m, [(x0, glass_lo, zz), (x1, glass_lo, zz), (x1, glass_hi, zz), (x0, glass_hi, zz)], (0, 0, nz),
                                     P.RAIL_GLASS, K.GLASS)
                    geo.box_minmax(m, (x0, top + 0.92, cz - 0.07), (x1, top + 1.0, cz + 0.07), P.HANDRAIL)
                    n = max(1, int((x1 - x0) / 1.5))
                    for i in range(n + 1):
                        x = x0 + (x1 - x0) * i / n
                        geo.box_minmax(m, (x - 0.03, top, cz - 0.07), (x + 0.03, top + 0.92, cz + 0.07), P.STEEL)
                else:
                    for xx, nx in ((x0 - 0.01, -1.0), (x1 + 0.01, 1.0)):
                        geo.oriented(m, [(xx, glass_lo, z0), (xx, glass_lo, z1), (xx, glass_hi, z1), (xx, glass_hi, z0)], (nx, 0, 0),
                                     P.RAIL_GLASS, K.GLASS)
                    geo.box_minmax(m, (cx - 0.07, top + 0.92, z0), (cx + 0.07, top + 1.0, z1), P.HANDRAIL)
                    n = max(1, int((z1 - z0) / 1.5))
                    for i in range(n + 1):
                        z = z0 + (z1 - z0) * i / n
                        geo.box_minmax(m, (cx - 0.07, top, z - 0.03), (cx + 0.07, top + 0.92, z + 0.03), P.STEEL)
            self.b.part("rail", "Railings", name, build)

    # ------------------------------------------------------------ the envelope
    def walls(self):
        """Each wall's exposed faces, dressed band by band: shopfronts where a gallery stands in
        front, blank facade with banners and pilasters elsewhere, cornices at the floor lines,
        the clerestory under the dome, and the named facades (department store, food court,
        entrance, cinema, arcade, game store)."""
        galleries = {name: self.P[name] for name in self.P if T.kind(name) in ("gallery", "food")}
        for name in self.spec["walls"]:
            spans = _merge(_spans(self.solids, name))

            def build(m, name=name, spans=spans):
                for key, plane, u0, u1, y0, y1 in spans:
                    self.facade(m, name, key, plane, u0, u1, y0, y1, galleries)
            self.b.part("wall", "Walls", name, build)

    def _world_at(self, plane, u):
        p = plane.at(u, 0.0)
        return p[0], p[2]

    def _gallery_at(self, plane, u0, u1, galleries):
        """Galleries whose back edge meets this facade span: [(top, ua, ub)]."""
        out = []
        for g, (gx0, gz0, gx1, gz1, top) in galleries.items():
            # sample along the span
            ua = ub = None
            for i in range(41):
                u = u0 + (u1 - u0) * i / 40
                x, z = self._world_at(plane, u)
                nx, nz = plane.n[0], plane.n[2]
                px, pz = x + nx * 0.5, z + nz * 0.5   # half a metre in front of the wall
                if gx0 - 1e-3 <= px <= gx1 + 1e-3 and gz0 - 1e-3 <= pz <= gz1 + 1e-3:
                    ua = u if ua is None else ua
                    ub = u
            if ua is not None and ub - ua > 0.5:
                out.append((top, ua, ub))
        return out

    def facade(self, m, name, key, plane, u0, u1, y0, y1, galleries):
        rng = self.rng
        y0 = max(y0, K.FLOOR_Y)
        # Base: the wall itself, cream with a dark plinth.
        K.panel(m, plane, u0, y0, u1, y1, P.WALL, off=0.02)
        K.panel(m, plane, u0, y0, u1, y0 + 0.25, P.WALL_DARK, off=0.03)
        on = self._gallery_at(plane, u0, u1, galleries)
        levels = [(0.0, None)] + [(t, (a, b)) for t, a, b in on]
        special = self.special(name, key)
        # Shopfronts on the ground floor everywhere, and on each gallery level where one stands.
        for floor, span in levels:
            a, b = (u0, u1) if span is None else span
            if floor + SHOP_H > y1:
                continue
            if special and floor in special:
                special[floor](m, plane, a, b, floor)
            else:
                self.shopfronts(m, plane, a, b, floor, height=3.0 if floor == 0.0 else SHOP_H)
        # Cornices at the floor lines where no gallery stands, pilasters on the blank facade.
        gallery_floors = {t for t, *_ in on}
        for line in FLOOR_LINES:
            if y0 < line < y1 and line not in gallery_floors:
                K.slab(m, plane, u0, line - 0.2, u1, line + 0.1, 0.18, P.COLUMN)
        n = max(1, int((u1 - u0) / 7.0))
        for i in range(n + 1):
            u = u0 + (u1 - u0) * i / n
            ua, ub = max(u0, u - 0.35), min(u1, u + 0.35)
            K.slab(m, plane, ua, 4.2 if gallery_floors else 0.25, ub, y1 - 1.0, 0.12, P.COLUMN, bottom=False)
        # Banners on the tall blank wall above the highest gallery.
        high = max([3.6] + [t + SHOP_H + 0.6 for t in gallery_floors])
        if y1 - high > 6.0 and u1 - u0 > 5.0:
            cells = ["banner_sale", "banner_open", "banner_ninja", "banner_pills"]
            k = abs(hash(name + key)) % 4
            count = max(1, int((u1 - u0) / 9.0))
            for i in range(count):
                u = u0 + (u1 - u0) * (i + 0.5) / count
                K.plate(m, plane, cells[(k + i) % 4], u, min(high + 4.2, y1 - 4.5), 1.6, off=0.16)
        # The clerestory under the dome: a band of night windows and a cornice.
        if y1 > DOME - 0.5:
            K.panel(m, plane, u0, DOME - 3.6, u1, DOME - 1.0, P.NIGHT, off=0.04, mat=K.SKY,
                    uvs=T.sub_uv("night", 0.0, 0.55, 1.0, 0.95))
            K.slab(m, plane, u0, DOME - 0.6, u1, DOME, 0.25, P.COLUMN)
        if special and "upper" in special:
            special["upper"](m, plane, u0, u1, y0, y1)

    def shopfronts(self, m, plane, u0, u1, floor, width=4.6, height=SHOP_H):
        """A row of shops on one floor: lit sign band, glass or shutter, a display now and then."""
        rng = self.rng
        SH = height
        n = max(1, int((u1 - u0) / width))
        w = (u1 - u0) / n
        for i in range(n):
            a, b = u0 + i * w + 0.15, u0 + (i + 1) * w - 0.15
            mid = (a + b) / 2
            shop = rng.choice(T.SHOP_NAMES)
            K.slab(m, plane, a - 0.15, floor + 0.05, a, floor + SH, 0.1, P.COLUMN)
            look = rng.random()
            cell = "shutter" if look < 0.4 else ("display" if look < 0.6 else "window")
            if cell == "shutter":
                K.panel(m, plane, a, floor + 0.05, b, floor + SH - SIGN_H - 0.1, (1.0, 1.0, 1.0), off=0.03,
                        uvs=T.sub_uv("shutter", 0.0, 0.0, 1.0, 1.0))
            else:
                with geo.state(m, alpha=1.0):
                    K.panel(m, plane, a, floor + 0.05, b, floor + SH - SIGN_H - 0.1, (1.0, 1.0, 1.0), off=0.03,
                            mat=K.GLOW, uvs=T.uv(cell))
            # the sign band
            K.panel(m, plane, a - 0.15, floor + SH - SIGN_H - 0.1, b + 0.15, floor + SH, P.INK, off=0.05)
            sw = min(b - a - 0.2, 3.2)
            with geo.state(m, alpha=1.0):
                K.plate(m, plane, shop, mid, floor + SH - SIGN_H / 2 - 0.05, sw, off=0.07, height=SIGN_H - 0.12, mat=K.GLOW)

    # ------------------------------------------------------------ named facades
    def special(self, name, key):
        """Per-wall facade overrides: floor -> builder, plus "upper" for big signs."""
        if name == "WallEast" and key == "w":
            return {0.0: self.east_ground, 4.0: self.east_l2, 9.0: self.east_l3, 15.0: self.east_l4, "upper": self.east_upper}
        if name == "WallNorth" and key == "s":
            return {15.0: self.game_store, "upper": self.north_upper}
        if name == "WallGrandWest" and key == "e":
            return {0.0: self.entrance, "upper": self.entrance_upper}
        if name == "WallFoodNorth" and key == "s":
            return {9.0: self.food_stalls_north}
        if name == "WallFoodWest" and key == "e":
            return {9.0: self.food_stalls_west}
        if name == "WallWestSouth" and key == "e":
            return {4.0: self.kiosk_row}
        return None

    def _z_of(self, plane, u):
        return self._world_at(plane, u)[1]

    @staticmethod
    def _u(plane, x, z):
        """The facade coordinate of a world point: its offset along the plane's right vector."""
        return (x - plane.o[0]) * plane.r[0] + (z - plane.o[2]) * plane.r[2]

    def east_ground(self, m, plane, u0, u1, floor):
        # The department store's ground floor south of the east gallery, shops elsewhere.
        cut = self._u(plane, 16.0, -20.0)
        lo, hi = sorted((u0, u1))
        if lo < cut < hi:
            south_lo = cut if self._z_of(plane, hi) < -20.0 else lo
            south_hi = hi if self._z_of(plane, hi) < -20.0 else cut
            self.dept_windows(m, plane, south_lo, south_hi, floor)
            other = (lo, cut) if south_lo == cut else (cut, hi)
            self.shopfronts(m, plane, other[0], other[1], floor, height=3.0)
        else:
            self.shopfronts(m, plane, u0, u1, floor, height=3.0)

    def dept_windows(self, m, plane, u0, u1, floor):
        n = max(1, int((u1 - u0) / 6.0))
        w = (u1 - u0) / n
        for i in range(n):
            a, b = u0 + i * w + 0.3, u0 + (i + 1) * w - 0.3
            K.slab(m, plane, a - 0.3, floor + 0.05, a, floor + 4.0, 0.16, P.COLUMN)
            with geo.state(m, alpha=1.0):
                K.panel(m, plane, a, floor + 0.3, b, floor + 3.6, (1.0, 1.0, 1.0), off=0.03, mat=K.GLOW, uvs=T.uv("dept_window"))

    def east_l2(self, m, plane, u0, u1, floor):
        # The east gallery: the pharmacy opposite the skybridge, its pill signs facing the stub.
        u_mid = self._u(plane, 16.0, -12.0)
        a, b = u_mid - 2.6, u_mid + 2.6
        if u0 < a:
            self.shopfronts(m, plane, u0, a, floor)
        with geo.state(m, alpha=1.0):
            K.panel(m, plane, a, floor + 0.05, b, floor + 2.8, (1.0, 1.0, 1.0), off=0.03, mat=K.GLOW, uvs=T.uv("window"))
        K.panel(m, plane, a - 0.15, floor + 2.8, b + 0.15, floor + SHOP_H, P.INK, off=0.05)
        with geo.state(m, alpha=1.0):
            K.plate(m, plane, "pharmacy", (a + b) / 2, floor + SHOP_H - 0.3, 3.4, off=0.07, height=0.45, mat=K.GLOW)
        K.lit_sign(m, plane, "red_pill", a + 0.9, floor + 4.6, 1.5, depth=0.1)
        K.lit_sign(m, plane, "blue_pill", b - 0.9, floor + 4.6, 1.5, depth=0.1)
        if b < u1:
            self.shopfronts(m, plane, b, u1, floor)

    def east_l3(self, m, plane, u0, u1, floor):
        # The landing: the department store's level-3 doors (THE AGENT stands guard).
        with geo.state(m, alpha=1.0):
            K.panel(m, plane, u0 + 0.4, floor + 0.05, u1 - 0.4, floor + 3.0, (1.0, 1.0, 1.0), off=0.03, mat=K.GLOW,
                    uvs=T.uv("dept_window"))
        K.lit_sign(m, plane, "grand_bazaar", (u0 + u1) / 2, floor + 3.6, 3.4, depth=0.1)

    def east_l4(self, m, plane, u0, u1, floor):
        # Level 4: the arcade and the cinema lobby; the game store is on the north wall.
        a = u0
        while a < u1 - 1.0:
            b = min(u1, a + 9.0)
            z = self._z_of(plane, (a + b) / 2)
            if z < 33.0:
                for k in range(int((b - a) / 1.6)):
                    u = a + 0.8 + k * 1.6
                    if u + 0.7 > b:
                        break
                    K.slab(m, plane, u - 0.7, floor, u + 0.7, floor + 2.0, 0.9, (0.18, 0.08, 0.30))
                    with geo.state(m, alpha=1.0):
                        K.plate(m, plane, "claw", u, floor + 1.25, 1.2, off=0.92, mat=K.GLOW)
                K.lit_sign(m, plane, "pixel", (a + b) / 2, floor + 3.1, 3.6, depth=0.1)
            else:
                self.shopfronts(m, plane, a, b, floor)
            a = b

    def east_upper(self, m, plane, u0, u1, y0, y1):
        # GRAND BAZAAR's big letters over the south-east, the NEO MALL banner over the Grand Court.
        for z, cell, width, y in ((-35.0, "grand_bazaar", 12.0, 11.5), (8.5, "neo_mall", 9.0, 14.0)):
            u = self._u(plane, 16.0, z)
            if u0 < u < u1:
                K.lit_sign(m, plane, cell, u, y, width, depth=0.15)

    def game_store(self, m, plane, u0, u1, floor):
        # North wall, level 4: DICE DOJO, straight ahead up the east gallery.
        a, b = sorted((self._u(plane, 8.0, 45.0), self._u(plane, 16.0, 45.0)))
        a, b = max(a, u0), min(b, u1)
        if b - a < 1.0:
            self.shopfronts(m, plane, u0, u1, floor)
            return
        with geo.state(m, alpha=1.0):
            K.panel(m, plane, a + 0.4, floor + 0.05, b - 0.4, floor + 2.9, (1.0, 1.0, 1.0), off=0.03, mat=K.GLOW, uvs=T.uv("game_window"))
        K.lit_sign(m, plane, "dice_dojo", (a + b) / 2, floor + 3.7, 4.2, depth=0.12)
        K.plate(m, plane, "fnd", (a + b) / 2, floor + 5.5, 3.6, off=0.05)
        if u0 < a:
            self.shopfronts(m, plane, u0, a, floor)

    def north_upper(self, m, plane, u0, u1, y0, y1):
        # The cinema over the north atrium and the mall's name above it.
        K.lit_sign(m, plane, "marquee", 0.0, 16.6, 12.0, depth=0.2)
        K.lit_sign(m, plane, "neo_mall", 0.0, 20.6, 10.0, depth=0.15)
        for u in (-5.0, 5.0):
            K.plate(m, plane, "banner_ninja", u, 9.0, 1.8, off=0.1)

    def entrance(self, m, plane, u0, u1, floor):
        # The Grand Court's glass entrance: the night outside through a mullioned curtain wall.
        K.panel(m, plane, u0, floor, u1, 19.0, P.NIGHT, off=0.03, mat=K.SKY, uvs=T.sub_uv("night", 0.0, 0.0, 1.0, 1.0))
        n = int((u1 - u0) / 2.0)
        for i in range(n + 1):
            u = u0 + (u1 - u0) * i / n
            K.slab(m, plane, u - 0.08, floor, u + 0.08, 19.0, 0.12, P.STEEL_DARK)
        for y in (2.8, 6.5, 10.5, 14.5, 19.0):
            K.slab(m, plane, u0, y - 0.08, u1, y + 0.08, 0.12, P.STEEL_DARK)
        with geo.state(m, alpha=1.0):
            K.plate(m, plane, "exit", (u0 + u1) / 2, 3.2, 1.2, off=0.16, mat=K.GLOW)

    def entrance_upper(self, m, plane, u0, u1, y0, y1):
        K.lit_sign(m, plane, "neo_mall", (u0 + u1) / 2, 21.2, 9.0, depth=0.15)

    def food_stalls_north(self, m, plane, u0, u1, floor):
        stalls = [("wok", "menu_wok"), ("pretzel", "menu_pretzel"), ("tea", None)]
        self._stalls(m, plane, u0, u1, floor, stalls)

    def food_stalls_west(self, m, plane, u0, u1, floor):
        stalls = [("slop_cola", None), ("lucky", None), ("hotdog", "menu_hotdog"), ("wok", "menu_wok")]
        self._stalls(m, plane, u0, u1, floor, stalls)

    def _stalls(self, m, plane, u0, u1, floor, stalls):
        n = len(stalls)
        w = (u1 - u0) / n
        for i, (sign, menu) in enumerate(stalls):
            a, b = u0 + i * w + 0.25, u0 + (i + 1) * w - 0.25
            mid = (a + b) / 2
            K.panel(m, plane, a, floor + 0.05, b, floor + 3.2, (0.20, 0.16, 0.14), off=0.03)
            # the counter, a metre deep
            K.slab(m, plane, a + 0.1, floor, b - 0.1, floor + 1.05, 0.7, P.STEEL, side=P.STEEL_DARK)
            K.lit_sign(m, plane, sign, mid, floor + 3.6, min(3.4, b - a - 0.2), depth=0.12)
            if menu:
                with geo.state(m, alpha=1.0):
                    K.plate(m, plane, menu, mid, floor + 2.2, min(2.4, b - a - 0.4), off=0.05, mat=K.GLOW)

    def kiosk_row(self, m, plane, u0, u1, floor):
        # The west gallery's shops, with BLADES OF THE EAST behind the start.
        u_start = self._u(plane, -16.0, self.spec["start"][1])
        a, b = u_start - 3.0, u_start + 3.0
        if u0 < a:
            self.shopfronts(m, plane, u0, a, floor)
        with geo.state(m, alpha=1.0):
            K.panel(m, plane, a, floor + 0.05, b, floor + 2.8, (1.0, 1.0, 1.0), off=0.03, mat=K.GLOW, uvs=T.uv("display"))
        K.panel(m, plane, a - 0.15, floor + 2.8, b + 0.15, floor + SHOP_H, P.INK, off=0.05)
        K.lit_sign(m, plane, "blades", (a + b) / 2, floor + SHOP_H - 0.25, 4.6, depth=0.08)
        if b < u1:
            self.shopfronts(m, plane, b, u1, floor)

    # ------------------------------------------------------------ fixtures on the decks
    def fixtures(self):
        b = self.b
        # The katana kiosk beside the start: a cart against the shopfronts, swords on a rack.
        sx, sz, _ = self.spec["start"]

        def kiosk(m):
            x0, x1 = -16.0, -14.8
            z0, z1 = sz - 2.0, sz + 2.0
            top = 4.0
            geo.box_minmax(m, (x0, top, z0), (x1, top + 1.0, z1), P.WOOD)
            geo.box_minmax(m, (x0 + 0.05, top + 1.0, z0 + 0.05), (x1 - 0.05, top + 1.08, z1 - 0.05), P.INK)
            for k in range(6):
                z = z0 + 0.4 + k * 0.6
                geo.cylinder_between(m, (x0 + 0.3, top + 1.1, z), (x0 + 0.3, top + 2.2, z), 0.025, 4, P.KATANA_STEEL)
                geo.box_minmax(m, (x0 + 0.27, top + 1.08, z - 0.05), (x0 + 0.33, top + 1.35, z + 0.05), P.KATANA_HILT)
        b.fixtures.append(("Kiosk", (-16.0, sz - 2.0, -14.8, sz + 2.0), True))
        b.part("fixture", "Fixtures", "Kiosk", kiosk)

        # Wet floor signs and a mop bucket on the skybridge stubs, at their outer corners.
        def cones(m):
            for x, z in ((-7.7, -13.7), (7.7, -10.3)):
                self.wet_floor_sign(m, x, 4.0, z)
            geo.box_minmax(m, (7.3, 4.0, -13.8), (7.8, 4.45, -13.3), P.HAZARD_YELLOW)
            geo.cylinder_between(m, (7.55, 4.45, -13.55), (7.9, 5.6, -13.2), 0.02, 4, P.WOOD)
            K.plate(m, K.Plane.facing((0.0, 0.0, -10.12 - 0.08), (0.0, -1.0)), "closed_cleaning", -7.0, 4.62, 1.6, off=0.0)
            K.plate(m, K.Plane.facing((0.0, 0.0, -13.88 + 0.08), (0.0, 1.0)), "closed_cleaning", -7.0, 4.62, 1.6, off=0.0)
        b.fixtures.append(("Cones", (-8.0, -14.0, 8.0, -10.0), True))
        b.part("fixture", "Fixtures", "Cones", cones)

        # Food court tables in the west half, clear of the route, the row and the boss.
        def tables(m):
            for x, z in ((-15.6, 11.5), (-15.6, 15.5), (-15.6, 19.5), (-15.6, 23.5), (-13.4, 13.5), (-13.4, 17.5), (-13.4, 21.5)):
                geo.cylinder_between(m, (x, 9.0, z), (x, 9.72, z), 0.06, 6, P.STEEL_DARK)
                geo.lathe(m, (x, 9.72, z), [(0.45, 0.0), (0.45, 0.04)], 12, P.CREAM, cap_top=True, cap_bottom=True)
                for dx, dz in ((0.75, 0.0), (-0.75, 0.0)):
                    geo.box_minmax(m, (x + dx - 0.2, 9.0, z + dz - 0.2), (x + dx + 0.2, 9.45, z + dz + 0.2), P.RED)
        b.fixtures.append(("Tables", (-16.6, 10.8, -12.4, 24.2), True))
        b.part("fixture", "Fixtures", "Tables", tables)

        # The blue pill over the menu signs, on the food court's north wall.
        def blue(m):
            plane = K.Plane.facing((0.0, 0.0, 32.0 - 0.02), (0.0, -1.0))
            K.lit_sign(m, plane, "blue_pill", -9.4, 12.2, 1.3, depth=0.06)
        b.part("wall", "Walls", "BluePill", blue)

    def wet_floor_sign(self, m, x, y, z):
        plane_a = K.Plane.facing((x, y, z - 0.12), (0.0, -1.0))
        plane_b = K.Plane.facing((x, y, z + 0.12), (0.0, 1.0))
        for plane in (plane_a, plane_b):
            K.panel(m, plane, -x - 0.22 if plane is plane_b else x - 0.22, 0.0, -x + 0.22 if plane is plane_b else x + 0.22, 0.62,
                    (1.0, 1.0, 1.0), uvs=T.uv("wet_floor"))
