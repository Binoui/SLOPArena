"""Build the Log Off campsite presentation in headless Blender.

    blender -b --factory-startup --python-exit-code 1 --python tools/blender/log_off/build.py -- \
        [--out /tmp/log-off-generated] [--report .stage-authoring-cache/log_off/campsite] \
        [--preview] [--views a,b] [--no-bake] [--no-export] [--spec-only]

The baked arena supplies the authoritative shell (see layout.py); --spec-only
previews the layout spec before it has been baked and refuses to export.
Output is presentation only: an FBX with vertex-coloured meshes and animation UV
channels, the stump-top texture, an atlas, a detail texture, a source stamp and a
clearance report. Run with a fresh factory scene; it never opens or saves a .blend.
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

import camp as CAMP  # noqa: E402
import forest as FOREST  # noqa: E402
import layout as LAYOUT  # noqa: E402
import stump as STUMP  # noqa: E402
import textures  # noqa: E402

REPO = LAYOUT.REPO
DEFAULT_OUT = "/tmp/log-off-generated"
DEFAULT_REPORT = REPO / ".stage-authoring-cache/log_off/campsite"
MATERIALS = ["LO_Wood", "LO_Stump", "LO_Foliage", "LO_Cloth", "LO_Glow", "LO_Sky"]
GLOWING = ("LO_Glow", "LO_Sky")
ENVELOPE = 25.0         # orbit radius 10 x 1.5 zoom plus the 10 m side blast margin
ENVELOPE_CEILING = 2.0  # metres below the kill plane
NO_AO = {"Sky", "Fireflies", "Far Forest", "Camp Glow", "Canopy"}
SEED = 20261016
TRIANGLE_BUDGET = 300_000  # planning target, reported only (performance is measured in Unity)
STEM = "LogOffCampsite"


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


def _inside(poly, x, z):
    inside = False
    n = len(poly)
    for i in range(n):
        (ax, az), (bx, bz) = poly[i], poly[(i + 1) % n]
        if (az > z) != (bz > z):
            t = (z - az) / (bz - az)
            if x < ax + t * (bx - ax):
                inside = not inside
    return inside


def _outside_distance(poly, x, z):
    """0 inside the polygon, otherwise the distance to its boundary."""
    if _inside(poly, x, z):
        return 0.0
    best = 1e9
    n = len(poly)
    for i in range(n):
        (ax, az), (bx, bz) = poly[i], poly[(i + 1) % n]
        ex, ez = bx - ax, bz - az
        L2 = ex * ex + ez * ez
        t = max(0.0, min(1.0, ((x - ax) * ex + (z - az) * ez) / L2)) if L2 else 0.0
        best = min(best, math.hypot(x - (ax + ex * t), z - (az + ez * t)))
    return best


class Build:
    def __init__(self, data):
        self.data = data
        self.C = data["corridor"]
        self.seed = SEED
        self.rng = random.Random(SEED)
        self.groups = {}
        self.checks = []
        xs = [p[0] for p in data["outline"]] + [p[0] for s in data["shelves"].values() for p in s["outline"]]
        zs = [p[1] for p in data["outline"]] + [p[1] for s in data["shelves"].values() for p in s["outline"]]
        self.box = ((min(xs), min(zs)), (max(xs), max(zs)))

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
        C = self.C
        outline = self.data["outline"]
        top = self.data["top"]
        report = {"top": {}, "wall": {}, "shelf": {}, "below": {}, "background": {}}
        problems = []
        ceiling = C["killY"] - ENVELOPE_CEILING
        (lx, lz), (hx, hz) = self.box
        solids = LAYOUT.collision(self.data["spec"])
        for role, owner, m in self.checks:
            if not m.verts:
                continue
            entry = report[role].setdefault(owner, {})
            if role == "top":
                off = max(abs(y - top) for x, y, z in m.verts)
                outside = max(_outside_distance(outline, x, z) for x, y, z in m.verts)
                entry.update({"offTop": round(off, 5), "outside": round(outside, 4)})
                if off > 1e-4 or outside > 1e-3:
                    problems.append("top %s leaves the fight surface (off %.4f, outside %.4f)" % (owner, off, outside))
            elif role == "wall":
                proud = max([0.0] + [_outside_distance(outline, x, z) for x, y, z in m.verts if y >= C["killY"]])
                above = max(y - top for x, y, z in m.verts)
                entry.update({"proudInCorridor": round(proud, 4), "above": round(above, 5)})
                if proud > STUMP.WALL_PROUD + 1e-4:
                    problems.append("wall %s stands %.3f m proud inside the corridor" % (owner, proud))
                if above > 1e-4:
                    problems.append("wall %s rises above the stump top" % owner)
            elif role == "shelf":
                shelf = self.data["shelves"][owner]
                verts, tris = solids[owner]
                bottoms = [(verts[a], verts[b], verts[c]) for a, b, c in tris
                           if geo._cross([verts[b][i] - verts[a][i] for i in range(3)],
                                         [verts[c][i] - verts[a][i] for i in range(3)])[1] < -1e-9]
                outside = max(_outside_distance(shelf["outline"], x, z) for x, y, z in m.verts)
                above = max(y - shelf["top"] for x, y, z in m.verts)
                below = 0.0
                for x, y, z in m.verts:
                    b = STUMP.Stump.surface_y(bottoms, x, z)
                    if b is not None:
                        below = max(below, b - y)
                entry.update({"outside": round(outside, 4), "above": round(above, 5), "belowSolid": round(below, 4)})
                if outside > 0.03 or above > 1e-4 or below > 0.03:
                    problems.append("shelf %s leaves its collision solid (outside %.3f, above %.4f, below %.3f)"
                                    % (owner, outside, above, below))
            elif role == "below":
                highest = max(y for x, y, z in m.verts)
                entry["highest"] = round(highest, 3)
                if highest > ceiling + 1e-4:
                    problems.append("below-plane %s reaches %.2f (limit %.2f)" % (owner, highest, ceiling))
            elif role == "background":
                crossing = 0
                near_high = 0
                nearest = 1e9
                for face in m.faces:
                    pts = [m.verts[i] for i in face]
                    xs = [p[0] for p in pts]
                    ys = [p[1] for p in pts]
                    zs = [p[2] for p in pts]
                    if (max(xs) > C["minX"] and min(xs) < C["maxX"] and max(ys) > C["killY"] and min(ys) < C["topY"]
                            and max(zs) > C["minZ"] and min(zs) < C["maxZ"]):
                        if checks.intersects_corridor(pts, C):
                            crossing += 1
                for x, y, z in m.verts:
                    if ceiling < y < C["topY"] + 30.0:
                        d = math.hypot(max(lx - x, x - hx, 0.0), max(lz - z, z - hz, 0.0))
                        nearest = min(nearest, d)
                        if d < ENVELOPE:
                            near_high += 1
                entry.update({"facesInsideCorridor": crossing, "verticesInEnvelope": near_high,
                              "nearestAboveCeiling": round(nearest, 2) if nearest < 1e9 else None,
                              "triangles": m.triangle_count})
                if crossing:
                    problems.append("background %s has %d faces intersecting the blast corridor" % (owner, crossing))
                if near_high:
                    problems.append("background %s has %d vertices above %.1f within %.0f m of the stage" % (owner, near_high, ceiling, ENVELOPE))
        return report, problems


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
    STUMP.Stump(b).build_all()
    FOREST.Forest(b).build_all()
    CAMP.Camp(b).build_all()
    report, problems = b.verify()
    os.makedirs(a.report, exist_ok=True)
    os.makedirs(a.out, exist_ok=True)
    if problems and not a.no_export:
        print("LOG_OFF_PROBLEMS", json.dumps(problems[:40]))
        raise SystemExit("Clearance problems found; fix them before exporting presentation art.")
    top_path = os.path.join(a.out, "LogOffStumpTop.png")
    atlas_path = os.path.join(a.out, "LogOffAtlas.png")
    textures.stump_top(top_path, data["spec"])
    textures.atlas(atlas_path)
    textures.detail(os.path.join(a.out, "LogOffDetail.png"))
    kit.reset_scene(MATERIALS)
    objects = kit.build_objects(b.groups, channels=True)
    bake_seconds = None if a.no_bake else kit.bake_ao(objects, a.samples, ao_distance=4.0, floor=0.3, gamma=1.0,
                                                      skip=NO_AO, keep=glowing)
    digest, object_digests = kit.digest(objects)
    if not a.no_export:
        kit.export_fbx(objects, os.path.join(a.out, STEM + ".fbx"))
        with open(os.path.join(a.out, STEM + ".layout.json"), "w") as fh:
            json.dump({"generator": "tools/blender/log_off/build.py", "seed": SEED,
                       "source": data["source"], "sourceSha256": data["sourceSha256"],
                       "arena": data.get("arena"), "arenaSha256": data["arenaSha256"],
                       "blender": bpy.app.version_string, "materials": MATERIALS,
                       "lights": {k: list(v) for k, v in CAMP.LIGHTS.items()},
                       "objects": sorted(objects), "geometryDigest": digest, "problems": len(problems)}, fh, indent=1)
            fh.write("\n")
    triangles = sum(g.triangle_count for g in b.groups.values())
    summary = {
        "source": data["source"], "sourceSha256": data["sourceSha256"], "arenaSha256": data["arenaSha256"],
        "specOnly": data["specOnly"], "corridor": data["corridor"], "stageBox": b.box,
        "objects": {n: {"triangles": b.groups[n].triangle_count, "vertices": len(b.groups[n].verts)} for n in objects},
        "triangles": triangles, "budget": checks.budget(triangles, TRIANGLE_BUDGET),
        "geometryDigest": digest, "objectDigests": object_digests,
        "meshHealth": kit.mesh_health(objects),
        "problems": problems, "checks": report, "aoBakeSeconds": bake_seconds,
        "groundContacts": b.ground_contacts,
        "buildSeconds": round(time.time() - t0, 1),
    }
    with open(os.path.join(a.report, "build-report.json"), "w") as fh:
        json.dump(summary, fh, indent=1)
    print("LOG_OFF_BUILD", json.dumps({k: summary[k] for k in ("triangles", "aoBakeSeconds", "buildSeconds", "geometryDigest")}),
          "problems", len(problems))
    if summary["budget"]["over"]:
        print("  OVER BUDGET: %d triangles planned, %d built" % (TRIANGLE_BUDGET, triangles))
    if summary["meshHealth"]["objects"]:
        print("  MESH HEALTH", json.dumps(summary["meshHealth"]))
    for p in problems[:25]:
        print("  PROBLEM", p)
    if a.preview:
        import preview as PV
        PV.render(b, objects, os.path.join(a.report, "preview"), atlas_path, top_path, a.views)


if __name__ == "__main__":
    main()
