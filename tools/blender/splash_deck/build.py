"""Build the Splash Deck fishing-harbour presentation in headless Blender.

    blender -b --factory-startup --python-exit-code 1 --python tools/blender/splash_deck/build.py -- \
        [--out /tmp/splash-deck-generated] [--report .stage-authoring-cache/splash_deck/harbour] \
        [--preview] [--views a,b] [--no-bake] [--no-export] [--spec-only]

The baked arena supplies the authoritative shell (see layout.py); --spec-only
previews the layout spec before it has been baked and refuses to export.
Output is presentation only: an FBX with vertex-coloured meshes and animation UV
channels, the deck and hull textures, an atlas, detail and ripple maps, a source
stamp and a clearance report. Run with a fresh factory scene; it never opens or
saves a .blend.
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

import bpy  # noqa: E402

from stagekit import blender as kit  # noqa: E402
from stagekit import checks, geo  # noqa: E402

import bay as BAY  # noqa: E402
import boat as BOAT  # noqa: E402
import coast as COAST  # noqa: E402
import layout as LAYOUT  # noqa: E402
import sea as SEA  # noqa: E402
import textures  # noqa: E402
import village as VILLAGE  # noqa: E402

REPO = LAYOUT.REPO
DEFAULT_OUT = "/tmp/splash-deck-generated"
DEFAULT_REPORT = REPO / ".stage-authoring-cache/splash_deck/harbour"
MATERIALS = ["SD_Wood", "SD_Deck", "SD_Hull", "SD_Land", "SD_Foliage", "SD_Cloth", "SD_Water", "SD_Glow", "SD_Sky"]
GLOWING = ("SD_Glow", "SD_Sky")
ENVELOPE = 25.0         # orbit radius 10 x 1.5 zoom plus the 10 m side blast margin
ENVELOPE_CEILING = 2.0  # metres below the kill plane
LANE_CLEAR = 30.0       # travelling objects stay above topY + LANE_CLEAR inside the envelope
# The deck, hull and raised spots carry painted contact shading instead of a vertex AO bake:
# their few large faces would turn the whole wall grey (a wall beside the deck sees half the sky).
NO_AO = {"Deck", "Hull", "Wheelhouse", "FishHatch", "Sea", "Sky", "Clouds", "Gulls", "Lighthouse Lamp", "Laundry", "Awnings",
         "Sailboats", "Far Islands"}
SMOOTH = ("Hull", "Hills", "Trees", "Clouds")
SEED = 20261016
TRIANGLE_BUDGET = 260_000  # planning target, reported only (performance is measured in Unity)
STEM = "SplashDeckHarbour"
TEXTURES = {"deck": "SplashDeckDeck.png", "hull": "SplashDeckHull.png", "atlas": "SplashDeckAtlas.png",
            "detail": "SplashDeckDetail.png", "ripples": "SplashDeckRipples.png"}


def args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--out", default=DEFAULT_OUT)
    p.add_argument("--report", default=str(DEFAULT_REPORT))
    p.add_argument("--preview", action="store_true")
    p.add_argument("--views", default="")
    p.add_argument("--no-bake", action="store_true")
    p.add_argument("--no-export", action="store_true")
    p.add_argument("--spec-only", action="store_true")
    p.add_argument("--samples", type=int, default=48)
    return p.parse_args(argv)


def _outside_distance(poly, x, z):
    """0 inside the convex CCW polygon, otherwise the distance to its boundary."""
    inside = True
    best = 1e9
    n = len(poly)
    for i in range(n):
        (ax, az), (bx, bz) = poly[i], poly[(i + 1) % n]
        ex, ez = bx - ax, bz - az
        if (x - ax) * ez - (z - az) * ex > 0:
            inside = False
        L2 = ex * ex + ez * ez
        t = max(0.0, min(1.0, ((x - ax) * ex + (z - az) * ez) / L2)) if L2 else 0.0
        best = min(best, math.hypot(x - (ax + ex * t), z - (az + ez * t)))
    return 0.0 if inside else best


class Build:
    def __init__(self, data):
        self.data = data
        self.C = data["corridor"]
        self.seed = SEED
        self.rng = random.Random(SEED)
        self.groups = {}
        self.checks = []
        x0, x1, z0, z1 = LAYOUT.bounds(data["spec"])
        self.box = ((x0, z0), (x1, z1))

    def group(self, name):
        if name not in self.groups:
            self.groups[name] = geo.Mesh(name, MATERIALS)
        return self.groups[name]

    def part(self, role, group, owner, build):
        m = geo.Mesh(owner, MATERIALS)
        build(m)
        self.checks.append((role, owner, m))
        self.group(group).extend(m)

    # ------------------------------------------------------------ checks
    def _envelope_distance(self, x, z):
        (lx, lz), (hx, hz) = self.box
        return math.hypot(max(lx - x, x - hx, 0.0), max(lz - z, z - hz, 0.0))

    def verify(self):
        C = self.C
        outline = self.data["outline"]
        top = self.data["top"]
        report = {"top": {}, "wall": {}, "raised": {}, "water": {}, "background": {}}
        problems = []
        ceiling = C["killY"] - ENVELOPE_CEILING
        for role, owner, m in self.checks:
            if not m.verts:
                continue
            entry = report[role].setdefault(owner, {})
            if role == "top":
                off = max(max(y - top, top - BOAT.CHAMFER - y, 0.0) for x, y, z in m.verts)
                outside = max(_outside_distance(outline, x, z) for x, y, z in m.verts)
                entry.update({"offTop": round(off, 5), "outside": round(outside, 4)})
                if off > 1e-4 or outside > 1e-3:
                    problems.append("top %s leaves the fight surface (off %.4f, outside %.4f)" % (owner, off, outside))
            elif role == "wall":
                proud = max([0.0] + [_outside_distance(outline, x, z) for x, y, z in m.verts if y >= C["killY"]])
                above = max(y - top for x, y, z in m.verts)
                entry.update({"proudInCorridor": round(proud, 4), "above": round(above, 5)})
                if proud > BOAT.WALL_PROUD + 1e-4:
                    problems.append("wall %s stands %.3f m proud inside the corridor" % (owner, proud))
                if above > 1e-4:
                    problems.append("wall %s rises above the deck" % owner)
            elif role == "raised":
                r = self.data["spec"]["raised"][owner]
                proud = max(BOAT.Boat.proud_from_box(r["footprint"], x, z) for x, y, z in m.verts)
                above = max(y - r["top"] for x, y, z in m.verts)
                below = max(top - y for x, y, z in m.verts)
                entry.update({"proud": round(proud, 4), "above": round(above, 5), "belowDeck": round(below, 5)})
                if proud > BOAT.WALL_PROUD + 1e-4 or above > 1e-4 or below > 1e-4:
                    problems.append("raised %s leaves its collision box (proud %.4f, above %.4f, below %.4f)"
                                    % (owner, proud, above, below))
            elif role == "water":
                off = max(abs(y - BAY.WATER_Y) for x, y, z in m.verts)
                entry.update({"offLevel": round(off, 6), "level": BAY.WATER_Y, "aboveKillPlane": round(BAY.WATER_Y - C["killY"], 3),
                              "triangles": m.triangle_count})
                if off > 1e-5:
                    problems.append("water %s is not flat at the sea level" % owner)
                if BAY.WATER_Y <= C["killY"] or BAY.WATER_Y >= top - 1.0:
                    problems.append("water level %.2f must sit just above the KO plane %.2f" % (BAY.WATER_Y, C["killY"]))
            elif role == "background":
                crossing = 0
                near_high = 0
                nearest = 1e9
                lanes = 0
                for fi, face in enumerate(m.faces):
                    pts = [m.verts[i] for i in face]
                    xs = [p[0] for p in pts]
                    ys = [p[1] for p in pts]
                    zs = [p[2] for p in pts]
                    if (max(xs) > C["minX"] and min(xs) < C["maxX"] and max(ys) > C["killY"] and min(ys) < C["topY"]
                            and max(zs) > C["minZ"] and min(zs) < C["maxZ"]):
                        if checks.intersects_corridor(pts, C):
                            crossing += 1
                    speed, along = m.travels[fi]
                    if speed:
                        lanes += 1
                        crossing += self._lane_problem(pts, m.dirs[fi], along, C)
                for x, y, z in m.verts:
                    if ceiling < y < C["topY"] + 30.0:
                        d = self._envelope_distance(x, z)
                        nearest = min(nearest, d)
                        if d < ENVELOPE:
                            near_high += 1
                entry.update({"facesInsideCorridor": crossing, "verticesInEnvelope": near_high,
                              "nearestAboveCeiling": round(nearest, 2) if nearest < 1e9 else None,
                              "travellingFaces": lanes, "triangles": m.triangle_count})
                if crossing:
                    problems.append("background %s has %d faces intersecting the blast corridor" % (owner, crossing))
                if near_high:
                    problems.append("background %s has %d vertices above %.1f within %.0f m of the stage" % (owner, near_high, ceiling, ENVELOPE))
        return report, problems

    def _lane_problem(self, pts, dir_loop, along, C):
        """1 if a travelling face ever enters the corridor or the envelope below the lane ceiling."""
        loop = math.hypot(*dir_loop)
        if loop < 1e-3:
            return 0
        dx, dz = dir_loop[0] / loop, dir_loop[1] / loop
        for k in range(64):
            wrapped = -loop / 2 + loop * k / 63
            shift = wrapped - along
            for x, y, z in pts:
                px, pz = x + dx * shift, z + dz * shift
                if y < C["topY"] + LANE_CLEAR and self._envelope_distance(px, pz) < ENVELOPE:
                    return 1
        return 0


# -------------------------------------------------------------- Blender side

def glowing(me, rgba):
    """Glowing corners (vertex alpha marks glow on StageNeon) keep their colour unshaded by AO."""
    import numpy as np
    return np.isin(kit.loop_materials(me), GLOWING) & (rgba[:, 3] > 0.5)


def main():
    a = args()
    t0 = time.time()
    data = LAYOUT.load(spec_only=a.spec_only)
    if a.spec_only and not a.no_export:
        raise SystemExit("--spec-only previews an unbaked layout; add --no-export.")
    b = Build(data)
    BOAT.Boat(b).build_all()
    SEA.Sea(b).build_all()
    coast = COAST.Coast(b)
    coast.build_all()
    village = VILLAGE.Village(b)
    village.build_all()
    report, problems = b.verify()
    os.makedirs(a.report, exist_ok=True)
    os.makedirs(a.out, exist_ok=True)
    if problems and not a.no_export:
        print("SPLASH_DECK_PROBLEMS", json.dumps(problems[:40]))
        raise SystemExit("Clearance problems found; fix them before exporting presentation art.")
    paths = {k: os.path.join(a.out, v) for k, v in TEXTURES.items()}
    textures.deck(paths["deck"], data["spec"])
    textures.hull(paths["hull"], data["spec"])
    textures.atlas(paths["atlas"])
    textures.detail(paths["detail"])
    textures.ripples(paths["ripples"])
    kit.reset_scene(MATERIALS)
    objects = kit.build_objects(b.groups, smooth=SMOOTH, channels=True)
    bake_seconds = None if a.no_bake else kit.bake_ao(objects, a.samples, ao_distance=4.0, floor=0.35, gamma=1.0,
                                                      skip=NO_AO, keep=glowing)
    digest, object_digests = kit.digest(objects)
    if not a.no_export:
        kit.export_fbx(objects, os.path.join(a.out, STEM + ".fbx"))
        with open(os.path.join(a.out, STEM + ".layout.json"), "w") as fh:
            json.dump({"generator": "tools/blender/splash_deck/build.py", "seed": SEED,
                       "source": data["source"], "sourceSha256": data["sourceSha256"],
                       "arena": data.get("arena"), "arenaSha256": data["arenaSha256"],
                       "blender": bpy.app.version_string, "materials": MATERIALS,
                       "textures": TEXTURES, "waterY": BAY.WATER_Y,
                       "sun": {"azimuth": BAY.SUN_AZIMUTH, "elevation": BAY.SUN_ELEVATION},
                       "objects": sorted(objects), "geometryDigest": digest, "problems": len(problems)}, fh, indent=1)
            fh.write("\n")
    triangles = sum(g.triangle_count for g in b.groups.values())
    summary = {
        "source": data["source"], "sourceSha256": data["sourceSha256"], "arenaSha256": data["arenaSha256"],
        "specOnly": data["specOnly"], "corridor": data["corridor"], "stageBox": b.box, "waterY": BAY.WATER_Y,
        "objects": {n: {"triangles": b.groups[n].triangle_count, "vertices": len(b.groups[n].verts)} for n in objects},
        "triangles": triangles, "budget": checks.budget(triangles, TRIANGLE_BUDGET),
        "geometryDigest": digest, "objectDigests": object_digests,
        "meshHealth": kit.mesh_health(objects),
        "problems": problems, "checks": report, "aoBakeSeconds": bake_seconds,
        "groundContacts": coast.ground_contacts + village.ground_contacts,
        "houses": len(village.houses),
        "buildSeconds": round(time.time() - t0, 1),
    }
    with open(os.path.join(a.report, "build-report.json"), "w") as fh:
        json.dump(summary, fh, indent=1)
    print("SPLASH_DECK_BUILD", json.dumps({k: summary[k] for k in ("triangles", "aoBakeSeconds", "buildSeconds", "geometryDigest", "houses")}),
          "problems", len(problems))
    if summary["budget"]["over"]:
        print("  OVER BUDGET: %d triangles planned, %d built" % (TRIANGLE_BUDGET, triangles))
    if summary["meshHealth"]["objects"]:
        print("  MESH HEALTH", json.dumps(summary["meshHealth"]))
    for p in problems[:25]:
        print("  PROBLEM", p)
    if a.preview:
        import preview as PV
        PV.render(b, objects, os.path.join(a.report, "preview"), paths, a.views)


if __name__ == "__main__":
    main()
