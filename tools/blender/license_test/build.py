"""Build the Fighter's License rooftop presentation in headless Blender.

    blender -b --factory-startup --python-exit-code 1 --python tools/blender/license_test/build.py -- \
        [--out /tmp/license-test-generated] [--report .stage-authoring-cache/license_test/roof] \
        [--no-bake] [--no-export] [--spec-only]

The baked arena supplies the authoritative shell (see layout.py); --spec-only previews
the layout spec before it has been baked and refuses to export. Output is presentation
only: an FBX with vertex-coloured meshes and animation UV channels, the deck texture, the
sign atlas, brick, concrete and detail maps, a source stamp, the presentation document
and a clearance report. Run with a fresh factory scene; it never opens or saves a .blend.
"""
from __future__ import annotations

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
from stagekit.presentation import write as write_presentation  # noqa: E402

import city as CITY  # noqa: E402
import course as COURSE  # noqa: E402
import deck as DECK  # noqa: E402
import layout as LAYOUT  # noqa: E402
import presentation as PRESENTATION  # noqa: E402
import textures  # noqa: E402
import walls as WALLS  # noqa: E402

REPO = LAYOUT.REPO
DEFAULT_OUT = "/tmp/license-test-generated"
DEFAULT_REPORT = REPO / ".stage-authoring-cache/license_test/roof"
MATERIALS = ["LT_Lit", "LT_Deck", "LT_Glass", "LT_Concrete", "LT_Brick", "LT_Far", "LT_Sky"]
GLOWING = ("LT_Sky",)
# Walls, course and facades carry painted shade: a vertex AO bake across their few large
# faces would grey whole walls (stagekit README pitfall). Background is unshaded haze.
NO_AO = {"Deck", "Facade", "Test Center Base", "Party Wall", "Safety Fence", "Course", "Street", "Traffic",
         "Test Center Tower", "Neighbour", "City Far", "Sky", "Clouds"}
SMOOTH = ("Clouds",)
SEED = 20261018
TRIANGLE_BUDGET = 200_000  # planning target, reported only (performance is measured in Unity)
STEM = "LicenseTestRoof"
TEXTURES = {"deck": "LicenseTestDeck.png", "atlas": "LicenseTestAtlas.png", "brick": "LicenseTestBrick.png",
            "concrete": "LicenseTestConcrete.png", "detail": "LicenseTestDetail.png"}
CAMERA_REACH = checks.CAMERA_REACH  # the orbit camera behind a fighter beyond the open edge
LANE_CLEAR = 30.0
# The broken guardrail is the one declared exception to the clearance rules: its torn ends
# stay beside the north and south walls (|z| >= BARRIER_Z), out over the drop by at most
# BARRIER_OUT and no higher than BARRIER_HIGH above the deck.
BARRIER_Z = 12.2
BARRIER_OUT = 0.45
BARRIER_HIGH = 0.95
BARRIER_LOW = -2.4


def args():
    return checks.build_args(DEFAULT_OUT, DEFAULT_REPORT)


def _box_reach(x, z, box):
    x0, x1, z0, z1 = box
    return math.hypot(max(x0 - x, x - x1, 0.0), max(z0 - z, z - z1, 0.0))


class Build:
    def __init__(self, data):
        self.data = data
        self.C = data["corridor"]
        self.seed = SEED
        self.rng = random.Random(SEED)
        self.groups = {}
        self.checks = []
        x0, z0, x1, z1 = data["deck"]
        # Where a fighter can be outside the walls: beyond the open east edge, inside the corridor.
        self.open_side = (x1, self.C["maxX"], self.C["minZ"], self.C["maxZ"])

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
    def verify(self):
        C, d = self.C, self.data
        top = d["top"]
        x0, z0, x1, z1 = d["deck"]
        report = {k: {} for k in ("top", "facade", "wall", "raised", "barrier", "background")}
        problems = []
        ceiling = C["killY"] - checks.CEILING_CLEARANCE
        for role, owner, m in self.checks:
            if not m.verts:
                continue
            entry = report[role].setdefault(owner, {})
            V = m.verts
            if role == "top":
                off = max(abs(y - top) for x, y, z in V)
                outside = max(max(x0 - x, x - x1, z0 - z, z - z1, 0.0) for x, y, z in V)
                entry.update({"offTop": round(off, 6), "outside": round(outside, 4)})
                if off > 1e-4 or outside > 1e-3:
                    problems.append("top %s leaves the deck surface (off %.4f, outside %.4f)" % (owner, off, outside))
            elif role == "facade":
                upper = [(x, y, z) for x, y, z in V if y >= C["killY"]]
                proud = max([0.0] + [max(x0 - x, x - x1, z0 - z, z - z1, 0.0) for x, y, z in upper])
                above = max(y - top for x, y, z in V)
                entry.update({"proudAboveKill": round(proud, 4), "above": round(above, 5)})
                if proud > DECK.FACADE_PROUD + 1e-4:
                    problems.append("facade %s stands %.3f m proud above the kill plane" % (owner, proud))
                if above > 1e-4:
                    problems.append("facade %s rises above the deck" % owner)
            elif role == "wall":
                bx0, bz0, bx1, bz1 = d["walls"][owner]
                p = WALLS.WALL_PROUD
                proud = max(max(bx0 - x, x - bx1, bz0 - z, z - bz1, 0.0) for x, y, z in V)
                above = max(y - d["wallTop"] for x, y, z in V)
                below = max(top - y for x, y, z in V)
                entry.update({"proud": round(proud, 4), "above": round(above, 5), "belowDeck": round(below, 5)})
                if proud > p + 1e-4 or above > 1e-4 or below > 1e-4:
                    problems.append("wall %s leaves its collision box (proud %.4f, above %.4f, below %.4f)" % (owner, proud, above, below))
            elif role == "raised":
                r = d["raised"][owner]
                fx0, fz0, fx1, fz1 = r["footprint"]
                proud = max(max(fx0 - x, x - fx1, fz0 - z, z - fz1, 0.0) for x, y, z in V)
                above = max(y - r["top"] for x, y, z in V)
                below = max(top - y for x, y, z in V)
                entry.update({"proud": round(proud, 4), "above": round(above, 5), "belowDeck": round(below, 5)})
                if proud > COURSE.RAISED_PROUD + 1e-4 or above > 1e-4 or below > 1e-4:
                    problems.append("raised %s leaves its collision box (proud %.4f, above %.4f, below %.4f)" % (owner, proud, above, below))
            elif role == "barrier":
                near_walls = min(abs(z) for x, y, z in V)
                out = max(x - x1 for x, y, z in V)
                inward = max(x1 - x for x, y, z in V)
                high = max(y - top for x, y, z in V)
                low = min(y - top for x, y, z in V)
                entry.update({"minAbsZ": round(near_walls, 3), "outOverDrop": round(out, 3), "inward": round(inward, 3),
                              "high": round(high, 3), "low": round(low, 3), "declaredException": True})
                if near_walls < BARRIER_Z or out > BARRIER_OUT or inward > 0.4 or high > BARRIER_HIGH or low < BARRIER_LOW:
                    problems.append("barrier %s leaves its declared region" % owner)
            elif role == "background":
                crossing = near_high = lanes = 0
                nearest = 1e9
                for fi, face in enumerate(m.faces):
                    pts = [V[i] for i in face]
                    xs, ys, zs = zip(*pts)
                    if (max(xs) > C["minX"] and min(xs) < C["maxX"] and max(ys) > C["killY"] and min(ys) < C["topY"]
                            and max(zs) > C["minZ"] and min(zs) < C["maxZ"] and checks.intersects_corridor(pts, C)):
                        crossing += 1
                    speed, _along = m.travels[fi]
                    if speed:
                        lanes += 1
                        if not (max(ys) < ceiling or min(ys) > C["topY"] + LANE_CLEAR):
                            crossing += 1
                for x, y, z in V:
                    if y > ceiling:
                        dd = _box_reach(x, z, self.open_side)
                        nearest = min(nearest, dd)
                        if dd < CAMERA_REACH:
                            near_high += 1
                entry.update({"facesInsideCorridor": crossing, "verticesInCameraReach": near_high,
                              "nearestAboveCeiling": round(nearest, 2) if nearest < 1e9 else None,
                              "travellingFaces": lanes, "triangles": m.triangle_count})
                if crossing:
                    problems.append("background %s has %d faces in the blast corridor or off its lanes" % (owner, crossing))
                if near_high:
                    problems.append("background %s has %d vertices above %.1f within %.0f m of the open side" % (owner, near_high, ceiling, CAMERA_REACH))
        return report, problems


# -------------------------------------------------------------- Blender side

def glowing(me, rgba):
    """Glowing corners (vertex alpha on StageNeon) keep their colour unshaded by AO."""
    import numpy as np
    return np.isin(kit.loop_materials(me), GLOWING) & (rgba[:, 3] > 0.5)


def main():
    a = args()
    t0 = time.time()
    data = LAYOUT.load(spec_only=a.spec_only)
    if a.spec_only and not a.no_export:
        raise SystemExit("--spec-only previews an unbaked layout; add --no-export.")
    b = Build(data)
    DECK.build_all(b)
    WALLS.build_all(b)
    COURSE.build_all(b)
    city = CITY.build_all(b)
    report, problems = b.verify()
    os.makedirs(a.report, exist_ok=True)
    os.makedirs(a.out, exist_ok=True)
    if problems and not a.no_export:
        print("LICENSE_TEST_PROBLEMS", json.dumps(problems[:40]))
        raise SystemExit("Clearance problems found; fix them before exporting presentation art.")
    paths = {k: os.path.join(a.out, v) for k, v in TEXTURES.items()}
    t_tex = time.time()
    textures.deck(paths["deck"], data["spec"])
    textures.atlas(paths["atlas"])
    textures.brick(paths["brick"])
    textures.concrete(paths["concrete"])
    textures.detail(paths["detail"])
    texture_seconds = round(time.time() - t_tex, 1)
    kit.reset_scene(MATERIALS)
    objects = kit.build_objects(b.groups, smooth=SMOOTH, channels=True)
    bake_seconds = None if a.no_bake else kit.bake_ao(objects, a.samples, ao_distance=2.5, floor=0.45, gamma=1.0,
                                                      skip=NO_AO, keep=glowing)
    digest, object_digests = kit.digest(objects)
    if not a.no_export:
        kit.export_fbx(objects, os.path.join(a.out, STEM + ".fbx"))
        with open(os.path.join(a.out, STEM + ".layout.json"), "w") as fh:
            json.dump({"generator": "tools/blender/license_test/build.py", "seed": SEED,
                       "source": data["source"], "sourceSha256": data["sourceSha256"],
                       "arena": data.get("arena"), "arenaSha256": data["arenaSha256"],
                       "blender": bpy.app.version_string, "materials": MATERIALS, "textures": TEXTURES,
                       "sun": {"azimuth": CITY.SUN_AZIMUTH, "elevation": CITY.SUN_ELEVATION},
                       "objects": sorted(objects), "geometryDigest": digest, "problems": len(problems)}, fh, indent=1)
            fh.write("\n")
        write_presentation(PRESENTATION.PRESENTATION, a.out)
    triangles = sum(g.triangle_count for g in b.groups.values())
    summary = {
        "source": data["source"], "sourceSha256": data["sourceSha256"], "arenaSha256": data["arenaSha256"],
        "specOnly": data["specOnly"], "corridor": data["corridor"], "openSide": b.open_side,
        "objects": {n: {"triangles": b.groups[n].triangle_count, "vertices": len(b.groups[n].verts)} for n in objects},
        "triangles": triangles, "budget": checks.budget(triangles, TRIANGLE_BUDGET),
        "geometryDigest": digest, "objectDigests": object_digests,
        "meshHealth": kit.mesh_health(objects),
        "problems": problems, "checks": report, "aoBakeSeconds": bake_seconds, "textureSeconds": texture_seconds,
        "buildings": city.ground_contacts,
        "buildSeconds": round(time.time() - t0, 1),
    }
    with open(os.path.join(a.report, "build-report.json"), "w") as fh:
        json.dump(summary, fh, indent=1)
    print("LICENSE_TEST_BUILD", json.dumps({k: summary[k] for k in ("triangles", "aoBakeSeconds", "textureSeconds", "buildSeconds",
                                                                    "geometryDigest", "buildings")}), "problems", len(problems))
    print("  OBJECTS", json.dumps({n: v["triangles"] for n, v in summary["objects"].items()}))
    if summary["budget"]["over"]:
        print("  OVER BUDGET: %d triangles planned, %d built" % (TRIANGLE_BUDGET, triangles))
    if summary["meshHealth"]["objects"]:
        print("  MESH HEALTH", json.dumps(summary["meshHealth"]))
    for p in problems[:25]:
        print("  PROBLEM", p)


if __name__ == "__main__":
    main()
