"""Build the Himalayan FightGuy Sanctuary presentation in headless Blender.

    blender -b --factory-startup --python-exit-code 1 --python tools/blender/fightguy_sanctuary/build.py -- \
        [--out client/Unity/Assets/Stages/fightguy_sanctuary/Presentation/Himalaya] \
        [--report .stage-authoring-cache/fightguy_sanctuary/himalaya] [--preview] [--no-bake] [--no-export]

The authoritative source scene supplies every surface and marker (see layout.py).
Output is presentation only: an FBX with vertex-coloured meshes, an atlas, a detail
texture and a clearance report. Run with a fresh factory scene; it never opens or
saves a .blend file.
"""
from __future__ import annotations

import argparse
import json
import math
import os
import random
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.dirname(HERE))  # tools/blender: the shared stagekit

from stagekit import blender as kit  # noqa: E402
from stagekit import checks, geo  # noqa: E402

import architecture as A  # noqa: E402
import dressing as D  # noqa: E402
import flags  # noqa: E402
import layout as LAYOUT  # noqa: E402
import palette as P  # noqa: E402
import props  # noqa: E402
import rocks  # noqa: E402
import textures  # noqa: E402

REPO = LAYOUT.REPO
DEFAULT_OUT = REPO / "client/Unity/Assets/Stages/fightguy_sanctuary/Presentation/Himalaya"
DEFAULT_REPORT = REPO / ".stage-authoring-cache/fightguy_sanctuary/himalaya"
MATERIALS = ["SA_Stone", "SA_Gloss", "SA_Cloth", "SA_Foliage", "SA_Glow", "SA_Cloud"]
SMOOTH_GROUPS = {"Mountain Floor", "Cloud Sea"}
CAMERA_ENVELOPE = 30.0  # max orbit radius (20 m x 1.5 radial zoom)
NO_AO = ("Cloud Sea", "Backdrop Peaks", "Prayer Flags", "Mountain Floor")
TRIANGLE_BUDGET = 300_000  # planning target, reported only (performance is measured in Unity)


def args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--out", default=str(DEFAULT_OUT))
    p.add_argument("--report", default=str(DEFAULT_REPORT))
    p.add_argument("--preview", action="store_true")
    p.add_argument("--views", default="")
    p.add_argument("--no-bake", action="store_true")
    p.add_argument("--no-export", action="store_true")
    p.add_argument("--samples", type=int, default=48)
    return p.parse_args(argv)


class Build:
    def __init__(self, data):
        self.data = data
        self.S = {s["name"]: s for s in data["surfaces"]}
        self.C = data["corridor"]
        self.rng = random.Random(20261015)
        self.groups = {}
        self.checks = []
        self.props = []
        self.anchors = {}

    def group(self, name):
        if name not in self.groups:
            self.groups[name] = geo.Mesh(name, MATERIALS)
        return self.groups[name]

    def fp(self, name):
        return A.fp_of(self.S[name])

    # ---------------------------------------------------------- recording
    def surface_dressing(self, group, name, build):
        m = geo.Mesh(name, MATERIALS)
        build(m)
        self.checks.append(("surface", name, m))
        self.group(group).extend(m)

    def pillar(self, group, name, **kw):
        s = self.S[name]
        m = geo.Mesh(name + " Rock", MATERIALS)
        fp = A.fp_of(s)
        kw.setdefault("seed", self.rng.random() * 50)
        rocks.pillar(m, self.rng, fp, s["min"][1] - 0.001, self.C["killY"], **kw)
        self.checks.append(("pillar", name, m))
        self.group(group).extend(m)

    def prop(self, group, owner, label, build):
        m = geo.Mesh(label, MATERIALS)
        build(m)
        self.checks.append(("prop", owner, m))
        self.props.append((owner, label, m))
        self.group(group).extend(m)

    def background(self, group, label, build):
        m = geo.Mesh(label, MATERIALS)
        build(m)
        self.checks.append(("background", label, m))
        self.group(group).extend(m)

    def overhead(self, label, build):
        m = geo.Mesh(label, MATERIALS)
        build(m)
        self.checks.append(("overhead", label, m))
        self.group("Prayer Flags").extend(m)

    # ---------------------------------------------------------- course
    def courtyard(self):
        name = "Training Courtyard"
        s = self.S[name]
        fp = self.fp(name)
        yt, yb = s["top"], s["min"][1]
        spawn = self.data["markers"]["Spawn_01"]
        R, Sq = 4.6, 4.95
        square = (spawn[0] - Sq, spawn[2] - Sq, spawn[0] + Sq, spawn[2] + Sq)
        takeoff = self.fp("Broken Bridge Takeoff")
        overlap = (max(fp[0], takeoff[0]), max(fp[1], takeoff[1]), min(fp[2], takeoff[2]), min(fp[3], takeoff[3]))

        def build(m):
            rects = [fp]
            for hole in (square, overlap):
                rects = [r for rect in rects for r in D.subtract(rect, hole)]
            for r in rects:
                borders = "".join(k for k, edge in (("s", r[1] == fp[1]), ("n", r[3] == fp[3]), ("w", r[0] == fp[0]), ("e", r[2] == fp[2])) if edge)
                D.paving(m, self.rng, r, yt, [P.PAVING, P.PAVING_LIGHT, P.PAVING_WARM], border=0.38, border_sides=borders,
                         row=(0.9, 1.3), length=(1.0, 2.1))
            with geo.detail(m, 0.45):
                D.mandala(m, self.rng, (spawn[0], spawn[2]), R, Sq, yt)
            D.masonry(m, self.rng, fp, yb, yt, [P.MASONRY, P.RUBBLE, geo.mix(P.MASONRY, P.PAVING_WARM, 0.4)], cap=P.MASONRY_DARK, cap_h=0.16)
            D.bottom(m, fp, yb)
            n_a, n_b = (fp[2], fp[3]), (fp[0], fp[3])  # north face, CCW: east -> west
            sub_a = (min(9.0, fp[2]), fp[3])
            sub_b = (max(-21.0, overlap[2] + 2.0), fp[3])
            props.wheel_gallery(m, sub_a, sub_b, (0, 1), yb + 0.22, int((sub_a[0] - sub_b[0]) / 0.62) - 1, height=1.42)
        self.surface_dressing("Courtyard", name, build)
        self.pillar("Courtyard", name, inset=0.35, taper=0.03, warm=0.2)

        finish = self.data["markers"]["Finish"]
        self.prop("Courtyard", name, "Finish Gate", lambda m: self.anchors.__setitem__("gate", A.gate(m, self.rng, finish, None)))
        masts = {"sw": (fp[0] + 0.7, fp[1] + 0.7), "se": (fp[2] - 0.7, fp[1] + 0.7), "ne": (fp[2] - 0.7, fp[3] - 0.7)}
        for key, (x, z) in masts.items():
            self.prop("Courtyard", name, "Mast " + key, lambda m, x=x, z=z, key=key: self.anchors.__setitem__("court_" + key, props.darchor(m, (x, yt, z), 11.0)))
        self.prop("Courtyard", name, "Training Dummies", lambda m: props.makiwara_row(m, (fp[0] + 6.5, yt, fp[1] + 1.15), (1.55, 0, 0), 3, 0.0))
        self.prop("Courtyard", name, "Juniper Burner", lambda m: props.sangkang(m, (fp[0] + 2.2, yt, fp[1] + 1.9)))

    def stone_terrace(self, group, name, colors=None, sides="masonry", band=None, border=0.3, pillar_kw=None, along="x", medallions=False):
        s = self.S[name]
        fp = self.fp(name)
        yt, yb = s["top"], s["min"][1]

        def build(m):
            D.paving(m, self.rng, fp, yt, colors or [P.PAVING, P.PAVING_LIGHT, P.PAVING_WARM], border=border, along=along)
            if sides == "masonry":
                D.masonry(m, self.rng, fp, yb, yt, [P.MASONRY, geo.mix(P.MASONRY, P.PAVING_WARM, 0.4), P.RUBBLE], cap=P.MASONRY_DARK, cap_h=0.12, band=band)
            elif sides == "whitewash":
                for a, b, n, key in D.sides(fp):
                    D.wall_quad(m, a, b, n, 0, 1, yb, yt, P.WHITEWASH)
                D.penbe(m, self.rng, fp, yt - (0.62 if medallions else 0.42), yt - 0.08, protrude=0.025, medallions=medallions, medallion_every=1.25)
                for a, b, n, key in D.sides(fp):
                    D.wall_quad(m, a, b, n, 0, 1, yt - 0.08, yt, P.BLACK, off=0.025)
            D.bottom(m, fp, yb)
        self.surface_dressing(group, name, build)
        self.pillar(group, name, **(pillar_kw or {}))

    def boardwalk(self, group, name, along):
        s = self.S[name]
        fp = self.fp(name)
        yt, yb = s["top"], s["min"][1]

        def build(m):
            D.planks(m, self.rng, fp, yt, along)
            D.timber_edge(m, self.rng, fp, yb, yt)
            D.bottom(m, fp, yb, P.WOOD_DARK)
        self.surface_dressing(group, name, build)
        self.pillar(group, name, inset=0.45)

    def training_post(self, name, knot_side):
        """Monastery training column: square walkable cap and painted capital over an
        octagonal cedar shaft (corners recede inside the authoritative box)."""
        s = self.S[name]
        fp = self.fp(name)
        yt, yb = s["top"], s["min"][1]
        x0, z0, x1, z1 = fp
        cx, cz, h = (x0 + x1) / 2, (z0 + z1) / 2, (x1 - x0) / 2
        k = h * math.tan(math.pi / 8)
        octagon = [(cx + h, cz - k), (cx + h, cz + k), (cx + k, cz + h), (cx - k, cz + h),
                   (cx - h, cz + k), (cx - h, cz - k), (cx - k, cz - h), (cx + k, cz - h)]
        cap_bottom = yt - 1.05

        def build(m):
            with geo.detail(m, 0.6):
                D.planks(m, self.rng, fp, yt, "z", width=(0.42, 0.6), colors=(P.WOOD_LIGHT, P.WOOD, P.WOOD_LIGHT))
                for a, b, n, key in D.sides(fp):
                    L = D.side_len(a, b)
                    D.wall_quad(m, a, b, n, 0, 1, yt - 0.16, yt, P.RED_DARK, off=0.0)
                    D.wall_quad(m, a, b, n, 0, 1, yt - 0.3, yt - 0.16, P.GOLD, off=0.0, mat="SA_Gloss")
                    for y0, y1, col in ((yt - 0.62, yt - 0.3, P.BLUE), (yt - 0.7, yt - 0.62, P.WHITE), (yt - 0.95, yt - 0.7, P.GREEN), (cap_bottom, yt - 0.95, P.RED)):
                        D.wall_quad(m, a, b, n, 0, 1, y0, y1, col)
                    blocks = max(3, int(L / 0.42))
                    for i in range(blocks):
                        D.wall_box(m, a, b, n, (i + 0.5) * L / blocks, yt - 0.46, 0.2, 0.2, 0.04, [P.OCHRE, P.RED, P.GREEN][i % 3])
                m.poly([(x0, cap_bottom, z0), (x1, cap_bottom, z0), (x1, cap_bottom, z1), (x0, cap_bottom, z1)], P.WOOD_DARK)
                # octagonal shaft: vertical grain strips, gilded rings
                for i in range(8):
                    (ax, az), (bx, bz) = octagon[i], octagon[(i + 1) % 8]
                    for t in range(3):
                        u0, u1 = t / 3, (t + 1) / 3
                        p0 = (ax + (bx - ax) * u0, az + (bz - az) * u0)
                        p1 = (ax + (bx - ax) * u1, az + (bz - az) * u1)
                        col = geo.jitter(geo.mix(P.WOOD, P.WOOD_LIGHT, 0.35 + 0.3 * ((i + t) % 2)), self.rng, 0.04)
                        m.poly([(p0[0], yb, p0[1]), (p0[0], cap_bottom, p0[1]), (p1[0], cap_bottom, p1[1]), (p1[0], yb, p1[1])], col)
                for y in (cap_bottom - 0.35, yb + 0.6):
                    ring = [(px + (px - cx) * 0.012, pz + (pz - cz) * 0.012) for px, pz in octagon]
                    geo.prism(m, ring, y - 0.09, y + 0.09, P.GOLD, "SA_Gloss", top=True, bottom=True)
            D.bottom(m, fp, yb, P.WOOD_DARK)
        self.surface_dressing("West Crossing", name, build)
        knot = {"e": 0, "n": 2, "w": 4, "s": 6}[knot_side]
        self.prop("West Crossing", name, "Red Training Ribbon", lambda m: flags.ribbon_ring(m, self.rng, octagon, cap_bottom - 0.9, knot_index=knot))
        self.pillar("West Crossing", name, inset=0.55, taper=0.02, flute=0.3)

    def broken_bridge_posts(self, name, edge_z, outward):
        s = self.S[name]
        fp = self.fp(name)
        yt = s["top"]
        for x in (fp[0] + 1.2, fp[2] - 1.2):
            def build(m, x=x):
                z = edge_z - outward * 0.3
                geo.box(m, (x, yt + 0.55, z), (0.34, 1.1, 0.34), P.MASONRY, skip=("bottom",), top_color=P.MASONRY_DARK)
                geo.lathe(m, (x, yt + 1.1, z), [(0.2, 0), (0.24, 0.08), (0.14, 0.22), (0.0, 0.3)], 8, [P.MASONRY_DARK, P.MASONRY, P.MASONRY], cap_top=False)
                # snapped rope and a dangling plank hanging down the gap face
                top = (x, yt + 0.85, z)
                hang = (x + 0.2, yt - 1.6, edge_z + outward * 0.06)
                geo.cylinder_between(m, top, (x, yt - 0.02, edge_z + outward * 0.05), 0.03, 4, (0.62, 0.52, 0.34))
                geo.cylinder_between(m, (x, yt - 0.02, edge_z + outward * 0.05), hang, 0.03, 4, (0.62, 0.52, 0.34))
                geo.box(m, (hang[0], hang[1] - 0.55, hang[2] + outward * 0.03), (0.28, 1.1, 0.05), P.WOOD_DARK)
                for k in range(3):
                    props.cloth_flag(m, (x + 0.05, yt - 0.3 - k * 0.32, edge_z + outward * 0.08), (0.4, 0, outward * 0.6), P.FLAG_COLORS[(k * 2 + int(x)) % 5], 0.24, 0.2, -0.02)
            self.prop("West Crossing", name, "Broken Bridge Posts", build)

    def roof(self, name, wall, rich, door, lhatho=(0, 2)):
        s = self.S[name]
        holder = {}

        def build(m):
            holder["base"] = A.roof_building(m, self.rng, s, wall, rich=rich, lhatho_corners=lhatho, door_side=door)
        # The building walls hang below the slab inside its footprint; check as surface dressing
        self.surface_dressing("Descent Houses", name, build)
        fp = self.fp(name)
        m = geo.Mesh(name + " Rock", MATERIALS)
        rocks.pillar(m, self.rng, (fp[0] + 0.14, fp[1] + 0.14, fp[2] - 0.14, fp[3] - 0.14), holder["base"] - 0.001,
                     self.C["killY"], inset=0.1, seed=self.rng.random() * 50)
        self.checks.append(("pillar", name, m))
        self.group("Descent Houses").extend(m)

    def island(self, name, snow):
        s = self.S[name]
        fp = self.fp(name)
        yt, yb = s["top"], s["min"][1]

        def build(m):
            D.rock_top(m, self.rng, fp, yt, snow=0.45)
            for a, b, n, key in D.sides(fp):
                L = D.side_len(a, b)
                # natural rock sides: snow only lingers along the upper lip
                geo.vertex_grid(m, (a[0], yb, a[1]), (0, yt - yb, 0), (b[0] - a[0], 0, b[1] - a[1]), 4, max(4, int(L / 0.4)),
                                lambda p: D.snow_rock_color(p) if p[1] > yt - 0.25 else geo.rgba(rocks.rock_color(self.rng, p, 0.0, None, 0.2, 0.25), 1.0))
            D.bottom(m, fp, yb)
        self.surface_dressing("Summit", name, build)
        self.pillar("Summit", name, inset=0.03, snow_level=snow, taper=0.04, flute=0.3)

    def course(self):
        S = self.S
        self.courtyard()
        # West crossing: forgiving R gap between two stone landings, cedar training posts on the left.
        for name in ("Broken Bridge Takeoff", "Broken Bridge Landing"):
            self.stone_terrace("West Crossing", name, colors=[P.PAVING_LIGHT, P.PAVING, (0.80, 0.74, 0.62)],
                               band=(S[name]["top"] - 0.42, S[name]["top"] - 0.24, P.TURQUOISE), border=0.36,
                               pillar_kw={"inset": 0.3, "warm": 0.15})
        self.broken_bridge_posts("Broken Bridge Takeoff", self.fp("Broken Bridge Takeoff")[3], 1)
        self.broken_bridge_posts("Broken Bridge Landing", self.fp("Broken Bridge Landing")[1], -1)
        for name, knot in (("Bridge Detour Pole 1", "e"), ("Bridge Detour Pole 2", "s"), ("Bridge Detour Pole 3", "e")):
            self.training_post(name, knot)
        self.boardwalk("West Crossing", "West Walk", "z")
        self.boardwalk("West Crossing", "West Walk Turn", "x")
        # North: stepped terraces climbing to the checkpoint plaza.
        self.stone_terrace("North Terraces", "Terrace Takeoff")
        for name in ("Rising Terrace 1", "Rising Terrace 2", "Rising Terrace 3"):
            self.stone_terrace("North Terraces", name, sides="whitewash", border=0.22, pillar_kw={"inset": 0.12})
        self.stone_terrace("North Terraces", "Upper Terrace Landing", sides="whitewash", border=0.34, medallions=True)
        self.boardwalk("North Terraces", "North Gallery", "x")
        self.boardwalk("North Terraces", "North Gallery Turn", "x")
        # Summit: snow appears with altitude.
        self.stone_terrace("Summit", "Summit Takeoff", band=(S["Summit Takeoff"]["top"] - 0.4, S["Summit Takeoff"]["top"] - 0.22, P.TURQUOISE),
                           pillar_kw={"snow_level": -6.0})
        for name in ("Summit Detour Island 1", "Summit Detour Island 2", "Summit Detour Island 3"):
            self.island(name, -8.0)
        sl = S["Summit Landing"]
        self.stone_terrace("Summit", "Summit Landing", colors=[P.PAVING_LIGHT, (0.86, 0.86, 0.86), P.PAVING], band=(sl["top"] - 0.4, sl["top"] - 0.22, P.TURQUOISE),
                           pillar_kw={"snow_level": -4.0, "inset": 0.2})
        fp = self.fp("Summit Landing")
        self.prop("Summit", "Summit Landing", "Summit Darchor", lambda m: self.anchors.__setitem__("summit_mast", props.darchor(m, (fp[2] - 0.55, sl["top"], fp[3] - 0.55), 9.0)))
        self.prop("Summit", "Summit Landing", "Summit Cairn", lambda m: props.cairn(m, self.rng, (fp[0] + 0.6, sl["top"], fp[3] - 0.6), 1.1))
        self.prop("Summit", "Summit Landing", "Summit Gong", lambda m: props.gong(m, (fp[0] + 0.32, sl["top"], (fp[1] + fp[3]) / 2 - 0.2), facing_x=1))
        # Descent: monastery houses stepping down to the courtyard.
        self.roof("Descent Roof 1", 5.5, True, "e", (0, 1, 2, 3))
        self.roof("Descent Roof 2", 4.2, False, "w", (1, 3))
        self.roof("Descent Roof 3", 4.6, False, "e", (0, 2))
        self.roof("Descent Roof 4", 3.4, True, "w", (0, 1))
        # Sanctuary temple: the central landmark.
        temple = geo.Mesh("Sanctuary", MATERIALS)
        cloth = geo.Mesh("Sanctuary Cloth", MATERIALS)
        self.anchors["temple"] = A.temple(temple, self.rng, S, cloth)
        self.checks.append(("temple", "Sanctuary", temple))
        self.group("Sanctuary Temple").extend(temple)
        self.checks.append(("overhead", "Sanctuary Cloth", cloth))
        self.group("Prayer Flags").extend(cloth)
        self.pillar("Sanctuary Temple", "Sanctuary Foundation", inset=0.4, taper=0.04, warm=0.1)

    def course_flags(self):
        a = self.anchors
        t = a["temple"]
        lines = [
            (a["court_ne"], t["roof_corners"][1], 2.2), (a["court_ne"], a["gate"], 1.0),
            (a["court_se"], a["gate"], 1.0), (a["court_sw"], a["court_se"], 2.2),
            (a["summit_mast"], t["ganjira_tip"], 2.6),
        ]
        for k, (p0, p1, sag) in enumerate(lines):
            self.overhead("Flag Line %d" % k, lambda m, p0=p0, p1=p1, sag=sag, k=k: flags.string(m, self.rng, p0, p1, sag, color_offset=k))
        # Terrace masts with a line along the southern edge of the climb.
        S = self.S
        tt, ut = self.fp("Terrace Takeoff"), self.fp("Upper Terrace Landing")
        self.prop("North Terraces", "Terrace Takeoff", "Terrace Mast", lambda m: a.__setitem__("terrace_mast", props.darchor(m, (tt[0] + 0.6, S["Terrace Takeoff"]["top"], tt[3] - 0.6), 8.0)))
        self.prop("North Terraces", "Upper Terrace Landing", "Plaza Mast", lambda m: a.__setitem__("plaza_mast", props.darchor(m, (ut[2] - 0.6, S["Upper Terrace Landing"]["top"], ut[3] - 0.6), 8.5)))
        self.overhead("Terrace Line", lambda m: flags.string(m, self.rng, a["terrace_mast"], a["plaza_mast"], 2.4))

    # ---------------------------------------------------------- world
    def backdrop(self):
        rng = self.rng
        spires = [
            ((-88, 84), 8.0, 7.5, 4.6, "stupa"), ((-34, 100), 21.0, 6.5, 0.0, None), ((24, 106), 14.0, 6.0, 3.2, "chorten"),
            ((90, 80), 2.0, 9.0, 6.5, "hermitage"), ((104, -8), 25.0, 5.5, 0.0, None), ((84, -96), 4.0, 7.0, 4.5, "chorten"),
            ((4, -108), 15.0, 6.5, 0.0, None), ((-96, -86), 12.0, 7.0, 0.0, None), ((-124, -36), -2.0, 9.0, 5.5, "hermitage"),
            ((-112, 30), 30.0, 7.0, 0.0, None), ((132, 46), 10.0, 8.0, 0.0, None), ((-60, -128), 6.0, 8.0, 0.0, None),
        ]
        tops = []
        for k, ((x, z), top, radius, flat, kind) in enumerate(spires):
            def build(m, x=x, z=z, top=top, radius=radius, flat=flat, kind=kind, k=k):
                rocks.spire(m, rng, (x, z), top, radius, seed=k * 3.7, flat_top=flat, snow_level=-6.0, pines=m)
                if kind in ("stupa", "chorten"):
                    props.chorten(m, (x, top, z), 1.25 if kind == "stupa" else 0.8)
                elif kind == "hermitage":
                    fp = (x - 3.0, z - 2.4, x + 3.0, z + 2.4)
                    house = {"min": [fp[0], top + 3.6, fp[1]], "max": [fp[2], top + 4.6, fp[3]], "top": top + 4.6}
                    A.roof_building(m, rng, house, 3.6, rich=True, lhatho_corners=(0, 1, 2, 3), door_side="s")
            self.background("Backdrop Spires", "Spire %d" % k, build)
            tops.append((x, top + (6 if kind else 0.5), z))
        for i, j, sag in ((1, 2, 6.0), (0, 1, 9.0), (3, 4, 7.0), (7, 8, 6.0), (9, 0, 8.0)):
            self.background("Backdrop Spires", "Spire Flags %d" % i, lambda m, i=i, j=j, sag=sag: flags.string(m, rng, tops[i], tops[j], sag, spacing=0.7, fw=0.55, fh=0.5))
        peaks = [((30, 300), 150, 190), ((-130, 250), 112, 150), ((175, 235), 120, 160), ((300, 30), 96, 140), ((240, -175), 104, 150),
                 ((50, -320), 132, 170), ((-205, -225), 108, 150), ((-305, -40), 118, 160), ((-265, 125), 92, 130), ((110, 380), 128, 170),
                 ((-60, 410), 140, 200)]
        for k, ((x, z), h, r) in enumerate(peaks):
            self.background("Backdrop Peaks", "Peak %d" % k, lambda m, x=x, z=z, h=h, r=r, k=k: rocks.peak(m, rng, (x, z), h, r, h - 55, seed=k * 1.9))
        self.background("Cloud Sea", "Cloud Sea", lambda m: rocks.cloud_sea(m, rng, extent=460, spacing=64, top=-27.0))
        self.background("Mountain Floor", "Mountain Floor", lambda m: rocks.massif(m, rng, self.C))
        for k, ((x, z), h, r) in enumerate(peaks[:8]):
            self.background("Cloud Sea", "Peak Clouds %d" % k, lambda m, x=x, z=z, h=h, r=r: rocks.cloud_bank(m, rng, (x * 0.82, z * 0.82), 3, r * 0.5, r * 0.35, h * 0.12 - 8))

    # ---------------------------------------------------------- checks
    def verify(self):
        C = self.C
        S = self.data["surfaces"]
        report = {"surface": {}, "pillar": {}, "prop": {}, "background": {}, "overhead": {}, "temple": {}}
        problems = []
        course_lo = (min(s["min"][0] for s in S), min(s["min"][2] for s in S))
        course_hi = (max(s["max"][0] for s in S), max(s["max"][2] for s in S))
        for role, owner, m in self.checks:
            if not m.verts:
                continue
            if role in ("surface", "pillar", "prop"):
                s = self.S[owner]
                x0, z0, x1, z1 = A.fp_of(s)
                tol = {"surface": 0.065, "pillar": 0.0, "prop": 0.0}[role]
                worst = 0.0
                above = -1e9
                for x, y, z in m.verts:
                    if role == "pillar" and y < C["killY"]:
                        continue
                    out = max(x0 - x, x - x1, z0 - z, z - z1, 0.0)
                    worst = max(worst, out)
                    above = max(above, y - s["top"])
                entry = report[role].setdefault(owner, {"outside": 0.0, "above": -1e9})
                entry["outside"] = max(entry["outside"], round(worst, 4))
                entry["above"] = max(entry["above"], round(above, 4))
                if role != "prop" and worst > tol + 1e-6:
                    problems.append("%s %s extends %.3f m outside its footprint" % (role, owner, worst))
                if role == "surface" and above > 0.0005 and owner not in ("Descent Roof 1", "Descent Roof 2", "Descent Roof 3", "Descent Roof 4"):
                    problems.append("surface %s rises %.3f m above its walkable top" % (owner, above))
                if role == "pillar" and above > 0.0005 - (s["top"] - s["min"][1]):
                    problems.append("pillar %s rises into its slab" % owner)
            elif role == "background":
                inside = 0
                near = 1e9
                for x, y, z in m.verts:
                    if C["minX"] < x < C["maxX"] and C["minZ"] < z < C["maxZ"] and C["killY"] < y < C["topY"]:
                        inside += 1
                    if y > C["killY"] - 15:
                        dx = max(course_lo[0] - x, x - course_hi[0], 0.0)
                        dz = max(course_lo[1] - z, z - course_hi[1], 0.0)
                        near = min(near, math.hypot(dx, dz))
                crossing_faces = sum(checks.intersects_corridor([m.verts[i] for i in face], C) for face in m.faces)
                report["background"][owner] = {"insideCorridor": inside, "facesInsideCorridor": crossing_faces,
                                               "nearestCourseDistance": round(near, 2)}
                if inside:
                    problems.append("background %s has %d vertices inside the blast corridor" % (owner, inside))
                if crossing_faces:
                    problems.append("background %s has %d faces intersecting the blast corridor" % (owner, crossing_faces))
                if near < CAMERA_ENVELOPE and owner not in ("Cloud Sea", "Mountain Floor"):
                    problems.append("background %s is %.1f m from the course (camera envelope %.0f m)" % (owner, near, CAMERA_ENVELOPE))
            elif role == "overhead":
                lowest = 1e9
                for x, y, z in m.verts:
                    for s in S:
                        if s["min"][0] <= x <= s["max"][0] and s["min"][2] <= z <= s["max"][2] and y >= s["top"] - 0.05:
                            lowest = min(lowest, y - s["top"])
                report["overhead"][owner] = {"lowestAboveWalkable": round(lowest, 2) if lowest < 1e9 else None}
            elif role == "temple":
                boxes = [self.S[k] for k in ("Sanctuary Foundation", "Sanctuary Hall", "Sanctuary Roof", "Sanctuary Crown")]
                worst = 0.0
                for x, y, z in m.verts:
                    d = min(max(b["min"][0] - x, x - b["max"][0], b["min"][2] - z, z - b["max"][2], 0.0) for b in boxes)
                    worst = max(worst, d)
                report["temple"][owner] = {"outsideFootprints": round(worst, 3)}
        return report, problems


# -------------------------------------------------------------- Blender side

def preview(build, objects, out_dir, atlas_path, views_filter):
    import preview as PV
    PV.render(build, objects, out_dir, atlas_path, views_filter)


def main():
    a = args()
    t0 = time.time()
    data = LAYOUT.load()
    b = Build(data)
    b.course()
    b.course_flags()
    b.backdrop()
    report, problems = b.verify()
    os.makedirs(a.report, exist_ok=True)
    os.makedirs(a.out, exist_ok=True)
    if problems and not a.no_export:
        print("SANCTUARY_PROBLEMS", json.dumps(problems))
        raise SystemExit("Clearance problems found; fix them before exporting presentation art.")
    atlas_path = os.path.join(a.out, "SanctuaryAtlas.png")
    textures.atlas(atlas_path)
    textures.detail(os.path.join(a.out, "SanctuaryDetail.png"))
    kit.reset_scene(MATERIALS)
    objects = kit.build_objects(b.groups, smooth=SMOOTH_GROUPS)
    bake_seconds = None if a.no_bake else kit.bake_ao(objects, a.samples, ao_distance=2.2, floor=0.30, gamma=1.15, skip=NO_AO)
    digest, object_digests = kit.digest(objects)
    if not a.no_export:
        kit.export_fbx(objects, os.path.join(a.out, "SanctuaryHimalaya.fbx"))
        with open(os.path.join(a.out, "SanctuaryHimalaya.layout.json"), "w") as fh:
            json.dump({"generator": "tools/blender/fightguy_sanctuary/build.py", "source": data["source"],
                       "sourceSha256": data["sourceSha256"], "geometryDigest": digest, "problems": len(problems)}, fh, indent=1)
            fh.write("\n")
    triangles = sum(g.triangle_count for g in b.groups.values())
    summary = {
        "source": data["source"], "sourceSha256": data["sourceSha256"], "corridor": data["corridor"],
        "objects": {n: {"triangles": b.groups[n].triangle_count, "vertices": len(b.groups[n].verts)} for n in objects},
        "triangles": triangles, "budget": checks.budget(triangles, TRIANGLE_BUDGET),
        "geometryDigest": digest, "objectDigests": object_digests,
        "meshHealth": kit.mesh_health(objects),
        "problems": problems, "checks": report, "aoBakeSeconds": bake_seconds,
        "buildSeconds": round(time.time() - t0, 1),
    }
    with open(os.path.join(a.report, "build-report.json"), "w") as fh:
        json.dump(summary, fh, indent=1)
    print("SANCTUARY_BUILD", json.dumps({k: summary[k] for k in ("triangles", "problems", "aoBakeSeconds", "buildSeconds", "geometryDigest")}))
    if summary["budget"]["over"]:
        print("  OVER BUDGET: %d triangles planned, %d built" % (TRIANGLE_BUDGET, triangles))
    if summary["meshHealth"]["objects"]:
        print("  MESH HEALTH", json.dumps(summary["meshHealth"]))
    if a.preview:
        preview(b, objects, os.path.join(a.report, "preview"), atlas_path, a.views)


if __name__ == "__main__":
    main()
