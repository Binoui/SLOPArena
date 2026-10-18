"""Build the Slopball Night Court presentation in headless Blender.

    blender -b --factory-startup --python-exit-code 1 --python tools/blender/slopball/build.py -- \
        [--out /tmp/slopball-generated] [--report .stage-authoring-cache/slopball/night-court] \
        [--preview] [--views a,b] [--no-bake] [--no-export]

The baked arena supplies the authoritative room (see layout.py). Output is presentation
only: an FBX of vertex-coloured meshes (StageNeon animation channels included), the
atlas, court and detail textures, a source stamp and a clearance report. Run with a
fresh factory scene; it never opens or saves a .blend file.
"""
from __future__ import annotations

import argparse
import json
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.dirname(HERE))  # tools/blender: the shared stagekit

from stagekit import checks, geo  # noqa: E402

import cage as CAGE  # noqa: E402
import court as COURT  # noqa: E402
import hall as HALL  # noqa: E402
import layout as LAYOUT  # noqa: E402
import textures  # noqa: E402

REPO = LAYOUT.REPO
DEFAULT_OUT = "/tmp/slopball-generated"
DEFAULT_REPORT = REPO / ".stage-authoring-cache/slopball/night-court"
MATERIALS = ["NC_Court", "NC_Night", "NC_Beam"]
# The court keeps its analytic flood pools only: a vertex AO bake leaves blotchy 1 m squares on flat paint.
NO_AO = {"Court Floor", "Fence", "Glow", "Flood Panels", "Flood Beams", "Combo Board", *CAGE.CARD_NAMES}
FLUSH = 0.025            # dressing may stand this far proud of a bounce plane
FLOOR_PAINT = 0.012      # court surface tolerance above y = 0
TRIANGLE_BUDGET = 160_000  # planning target, reported only (performance is measured in Unity)
FILES = ("SlopballNightCourt.fbx", "SlopballNightCourt.layout.json", "SlopballNightAtlas.png",
         "SlopballNightFloor.png", "SlopballNightDetail.png")


def args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--out", default=DEFAULT_OUT)
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
        self.spec = data["spec"]
        self.groups = {}
        self.roles = {}
        self.centred = []
        self.rig = HALL.rig(self.spec)
        self.floods_aim = [(a[0], a[2]) for a in (HALL.flood_aim(x, z) for x, z in HALL.FLOODS)]

    def group(self, name, role, centre=False):
        """One Unity object per group. Roles decide the clearance rule (see verify)."""
        if name not in self.groups:
            m = geo.Mesh(name, MATERIALS)
            m.alpha = 0.0  # StageNeon: no glow unless a part asks for it
            self.groups[name] = m
            self.roles[name] = role
            if centre:
                self.centred.append(name)
        return self.groups[name]

    def verify(self):
        """Nothing may stand inside the room except the floor paint, the net within its baked
        slab, light beams, and dressing within FLUSH of a wall or the ceiling plane."""
        half, ceil = self.spec["half"], self.spec["ceiling"]
        net_half, net_top = self.spec["net_half"], self.spec["net_top"]
        report, problems = {}, []

        def inside(p):
            return abs(p[0]) < half - FLUSH and abs(p[2]) < half - FLUSH and FLOOR_PAINT < p[1] < ceil - FLUSH

        for name, m in self.groups.items():
            role = self.roles[name]
            entry = {"role": role, "triangles": m.triangle_count, "vertices": len(m.verts)}
            bad = 0
            if role == "beam":
                report[name] = entry
                continue
            points = list(m.verts) + [tuple(sum(m.verts[i][k] for i in f) / len(f) for k in range(3)) for f in m.faces]
            for p in points:
                if role == "floor":
                    if p[1] > FLOOR_PAINT or abs(p[0]) > half + 1e-6 or abs(p[2]) > half + 1e-6:
                        bad += 1
                elif role == "net":
                    if inside(p) and (abs(p[2]) > net_half + 0.005 or p[1] > net_top + 0.005):
                        bad += 1
                elif inside(p):
                    bad += 1
            entry["violations"] = bad
            if bad:
                problems.append("%s (%s) has %d points standing inside the court" % (name, role, bad))
            report[name] = entry
        return report, problems


def centre_origins(objects, names):
    """Card objects pivot on their own centre so the runtime can flip them in place."""
    import bpy
    for o in bpy.context.scene.objects:
        o.select_set(False)
    for name in names:
        ob = objects.get(name)
        if ob is None:
            continue
        ob.select_set(True)
        bpy.context.view_layer.objects.active = ob
        bpy.ops.object.origin_set(type="ORIGIN_GEOMETRY", center="BOUNDS")
        ob.select_set(False)


def main():
    a = args()
    t0 = time.time()
    data = LAYOUT.load()
    b = Build(data)
    strands = COURT.build(b)
    CAGE.build(b)
    HALL.build(b)
    report, problems = b.verify()
    os.makedirs(a.report, exist_ok=True)
    os.makedirs(a.out, exist_ok=True)
    if problems and not a.no_export:
        print("SLOPBALL_PROBLEMS", json.dumps(problems[:40]))
        raise SystemExit("Clearance problems found; fix them before exporting presentation art.")
    import bpy
    from stagekit import blender as kit
    atlas_path = os.path.join(a.out, "SlopballNightAtlas.png")
    floor_path = os.path.join(a.out, "SlopballNightFloor.png")
    textures.atlas(atlas_path)
    textures.floor(floor_path, half=b.spec["half"])
    textures.detail(os.path.join(a.out, "SlopballNightDetail.png"))
    kit.reset_scene(MATERIALS)
    objects = kit.build_objects(b.groups, channels=True)
    centre_origins(objects, b.centred)
    bake_seconds = None if a.no_bake else kit.bake_ao(objects, a.samples, ao_distance=2.0, floor=0.35, gamma=1.0,
                                                      skip=NO_AO)
    digest, object_digests = kit.digest(objects)
    if not a.no_export:
        kit.export_fbx(objects, os.path.join(a.out, "SlopballNightCourt.fbx"))
        with open(os.path.join(a.out, "SlopballNightCourt.layout.json"), "w") as fh:
            json.dump({"generator": "tools/blender/slopball/build.py",
                       "source": data["source"], "sourceSha256": data["sourceSha256"],
                       "arena": data["arena"], "arenaSha256": data["arenaSha256"],
                       "blender": bpy.app.version_string, "materials": MATERIALS,
                       "objects": sorted(objects), "centred": b.centred,
                       "digitCell": textures.DIGIT_CELL, "atlasSize": textures.SIZE,
                       "geometryDigest": digest, "problems": len(problems)}, fh, indent=1)
            fh.write("\n")
    triangles = sum(g.triangle_count for g in b.groups.values())
    summary = {
        "source": data["source"], "sourceSha256": data["sourceSha256"], "arenaSha256": data["arenaSha256"],
        "netStrands": strands,
        "objects": {n: {"triangles": b.groups[n].triangle_count, "vertices": len(b.groups[n].verts)} for n in objects},
        "triangles": triangles, "budget": checks.budget(triangles, TRIANGLE_BUDGET),
        "geometryDigest": digest, "objectDigests": object_digests,
        "meshHealth": kit.mesh_health(objects),
        "problems": problems, "checks": report, "aoBakeSeconds": bake_seconds,
        "buildSeconds": round(time.time() - t0, 1),
    }
    with open(os.path.join(a.report, "build-report.json"), "w") as fh:
        json.dump(summary, fh, indent=1)
    print("SLOPBALL_BUILD", json.dumps({k: summary[k] for k in ("triangles", "aoBakeSeconds", "buildSeconds", "geometryDigest")}),
          "problems", len(problems))
    if summary["budget"]["over"]:
        print("  OVER BUDGET: %d triangles planned, %d built" % (TRIANGLE_BUDGET, triangles))
    for p in problems[:25]:
        print("  PROBLEM", p)
    if a.preview:
        import preview as PV
        PV.render(objects, os.path.join(a.report, "preview"), atlas_path, floor_path, a.views)


def dry_run():
    """Geometry and clearance only, without Blender (python3 build.py --dry)."""
    data = LAYOUT.load()
    b = Build(data)
    COURT.build(b)
    CAGE.build(b)
    HALL.build(b)
    report, problems = b.verify()
    tri = {n: g.triangle_count for n, g in b.groups.items()}
    print(json.dumps({"triangles": sum(tri.values()), "groups": tri, "problems": problems[:30]}, indent=1))


if __name__ == "__main__":
    if "--dry" in sys.argv and "--" not in sys.argv:
        dry_run()
    else:
        main()
