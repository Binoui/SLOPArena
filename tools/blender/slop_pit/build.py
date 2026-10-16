"""Build the Slop Pit underground fight-ring presentation in headless Blender.

    blender -b --factory-startup --python-exit-code 1 --python tools/blender/slop_pit/build.py -- \
        [--out /tmp/slop-pit-generated] [--report .stage-authoring-cache/slop_pit/ring] \
        [--preview] [--views a,b] [--no-bake] [--no-export] [--spec-only]

The baked arena supplies the authoritative shell (see layout.py); --spec-only previews
the layout spec before it has been baked and refuses to export. Output is presentation
only: an FBX of vertex-coloured meshes with StageNeon animation channels, the ring
canvas, floor and atlas textures, a detail texture, a source stamp and a clearance report.
Run with a fresh factory scene; it never opens or saves a .blend file.
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

from stagekit import checks, geo  # noqa: E402

import layout as LAYOUT  # noqa: E402
import ring as RING  # noqa: E402
import textures  # noqa: E402
import venue as VENUE  # noqa: E402

REPO = LAYOUT.REPO
DEFAULT_OUT = "/tmp/slop-pit-generated"
DEFAULT_REPORT = REPO / ".stage-authoring-cache/slop_pit/ring"
MATERIALS = ["SP_Lit", "SP_Floor", "SP_Canvas", "SP_Venue", "SP_Haze"]
NIGHT = ("SP_Venue", "SP_Haze")
ENVELOPE = 15.0          # orbit camera reach (radius 10 x 1.5 zoom) beyond the blast corridor
ENVELOPE_CEILING = 2.0   # metres below the kill plane
ENVELOPE_TOP = 16.0      # metres above the top blast plane the camera can still reach
NO_AO = {"Haze", "Crowd", "Fences", "Banners", "Venue Glow"}
SHADOWED = ("Ring", "Ringside Floor", "Ring Tower")
SEED = 20261016
TRIANGLE_BUDGET = 320_000  # planning target, reported only (performance is measured in Unity)
FILES = ("SlopPitArena.fbx", "SlopPitArena.layout.json", "SlopPitAtlas.png", "SlopPitCanvas.png",
         "SlopPitFloor.png", "SlopPitDetail.png")


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


class Build:
    def __init__(self, data):
        self.data = data
        self.C = data["corridor"]
        self.rng = random.Random(SEED)
        self.groups = {}
        self.checks = []
        x0, z0, x1, z1 = data["ring"]["footprint"]
        self.outlines = {"floor": data["floor"], "ring": [(x1, z0), (x1, z1), (x0, z1), (x0, z0)]}
        self.tops = {"floor": data["top"], "ring": data["ring"]["top"]}

    def group(self, name):
        if name not in self.groups:
            self.groups[name] = geo.Mesh(name, MATERIALS)
        return self.groups[name]

    # ------------------------------------------------------------ recording
    def record(self, role, group, owner, build, outline=None):
        """Run build(m, g) on a fresh mesh and glow mesh; both are checked under `role`."""
        m, g = geo.Mesh(owner, MATERIALS), geo.Mesh(owner + " Glow", MATERIALS)
        m.alpha = 1.0 if role in ("surface", "wall") else 0.0  # StageVertexLit detail / StageNeon no glow
        g.alpha = 1.0
        result = build(m, g)
        for mesh, target in ((m, group), (g, "Venue Glow")):
            if mesh.faces:
                self.checks.append((role, owner, mesh, outline))
                self.group(target).extend(mesh)
        return result

    def surface(self, group, owner, build):
        return self.record("surface", group, owner, build, "ring" if owner == "Ring" else "floor")

    def wall(self, group, owner, outline, build):
        return self.record("wall", group, owner, build, outline)

    def below(self, group, owner, build):
        return self.record("below", group, owner, build)

    def background(self, group, owner, build):
        return self.record("background", group, owner, build)

    # ------------------------------------------------------------ checks
    def envelope_distance(self, x, z):
        C = self.C
        dx = max(C["minX"] - x, x - C["maxX"], 0.0)
        dz = max(C["minZ"] - z, z - C["maxZ"], 0.0)
        return math.hypot(dx, dz)

    def verify(self):
        C = self.C
        report = {"surface": {}, "wall": {}, "below": {}, "background": {}}
        problems = []
        ceiling = C["killY"] - ENVELOPE_CEILING
        top_reach = C["topY"] + ENVELOPE_TOP
        for role, owner, m, outline in self.checks:
            if not m.verts:
                continue
            entry = report[role].setdefault(owner, {})
            if role == "surface":
                poly = self.outlines[outline]
                top = self.tops[outline]
                outside = max(0.0, max(max((x - a[0]) * n[0] + (z - a[1]) * n[1] for a, b, n, L in geo.edges(poly))
                                       for x, y, z in m.verts))
                above = max(y - top for x, y, z in m.verts)
                below = min(y for x, y, z in m.verts) - top
                entry.update({"outside": round(outside, 5), "above": round(above, 5), "lowest": round(below, 4)})
                if outside > 1e-4:
                    problems.append("surface %s extends %.4f m outside its footprint" % (owner, outside))
                if above > 1e-4:
                    problems.append("surface %s rises %.4f m above its walkable top" % (owner, above))
                if below < -1e-4:
                    problems.append("surface %s sinks %.4f m below its walkable top" % (owner, -below))
            elif role == "wall":
                poly = self.outlines[outline]
                top = self.tops[outline]
                proud = max([0.0] + [max((x - a[0]) * n[0] + (z - a[1]) * n[1] for a, b, n, L in geo.edges(poly))
                                     for x, y, z in m.verts if y >= C["killY"]])
                above = max(y - top for x, y, z in m.verts)
                entry.update({"proudInCorridor": round(proud, 4), "above": round(above, 5)})
                if proud > RING.WALL_PROUD + 1e-4:
                    problems.append("wall %s stands %.3f m proud inside the corridor" % (owner, proud))
                if above > 1e-4:
                    problems.append("wall %s rises %.4f m above its top" % (owner, above))
            else:
                crossing = 0
                for face in m.faces:
                    pts = [m.verts[i] for i in face]
                    xs = [p[0] for p in pts]
                    ys = [p[1] for p in pts]
                    zs = [p[2] for p in pts]
                    if (max(xs) > C["minX"] and min(xs) < C["maxX"] and max(ys) > C["killY"] and min(ys) < C["topY"]
                            and max(zs) > C["minZ"] and min(zs) < C["maxZ"]):
                        if checks.intersects_corridor(pts, C):
                            crossing += 1
                inside = 0
                nearest = 1e9
                highest_near = -1e9
                for x, y, z in m.verts:
                    d = self.envelope_distance(x, z)
                    if ceiling < y < top_reach:
                        nearest = min(nearest, d)
                        if d < ENVELOPE - 1e-6:
                            inside += 1
                    if d < ENVELOPE:
                        highest_near = max(highest_near, y)
                entry.update({"facesInsideCorridor": crossing, "verticesInCameraEnvelope": inside,
                              "nearestVisibleToCorridor": round(nearest, 2) if nearest < 1e9 else None,
                              "highestWithinEnvelope": round(highest_near, 2) if highest_near > -1e9 else None,
                              "triangles": m.triangle_count})
                if crossing:
                    problems.append("%s %s has %d faces intersecting the blast corridor" % (role, owner, crossing))
                if inside:
                    problems.append("%s %s has %d vertices inside the camera envelope (%.1f < y < %.1f, within %.0f m)"
                                    % (role, owner, inside, ceiling, top_reach, ENVELOPE))
        return report, problems


# -------------------------------------------------------------- Blender side

def glowing(me, rgba):
    """Glowing StageNeon corners (vertex alpha marks glow) keep their colour unshaded by AO."""
    import numpy as np
    from stagekit import blender as kit
    return np.isin(kit.loop_materials(me), NIGHT) & (rgba[:, 3] > 0.5)


def main():
    a = args()
    t0 = time.time()
    data = LAYOUT.load(spec_only=a.spec_only)
    if a.spec_only and not a.no_export:
        raise SystemExit("--spec-only previews an unbaked layout; add --no-export.")
    b = Build(data)
    RING.RingTower(b).build_all()
    crowd = VENUE.Venue(b).build_all()
    report, problems = b.verify()
    os.makedirs(a.report, exist_ok=True)
    os.makedirs(a.out, exist_ok=True)
    if problems and not a.no_export:
        print("SLOP_PIT_PROBLEMS", json.dumps(problems[:40]))
        raise SystemExit("Clearance problems found; fix them before exporting presentation art.")
    import bpy
    from stagekit import blender as kit
    atlas_path = os.path.join(a.out, "SlopPitAtlas.png")
    canvas_path = os.path.join(a.out, "SlopPitCanvas.png")
    floor_path = os.path.join(a.out, "SlopPitFloor.png")
    textures.atlas(atlas_path)
    textures.canvas(canvas_path, half=(data["ring"]["footprint"][2] - data["ring"]["footprint"][0]) / 2)
    textures.floor(floor_path, data["floor"], ring_half=data["ring"]["footprint"][2])
    textures.detail(os.path.join(a.out, "SlopPitDetail.png"))
    kit.reset_scene(MATERIALS)
    objects = kit.build_objects(b.groups, channels=True)
    bake_seconds = None if a.no_bake else kit.bake_ao(objects, a.samples, ao_distance=3.0, floor=0.3, gamma=1.1,
                                                      skip=NO_AO, keep=glowing)
    digest, object_digests = kit.digest(objects)
    if not a.no_export:
        kit.export_fbx(objects, os.path.join(a.out, "SlopPitArena.fbx"))
        with open(os.path.join(a.out, "SlopPitArena.layout.json"), "w") as fh:
            json.dump({"generator": "tools/blender/slop_pit/build.py", "seed": SEED,
                       "source": data["source"], "sourceSha256": data["sourceSha256"],
                       "arena": data.get("arena"), "arenaSha256": data["arenaSha256"],
                       "blender": bpy.app.version_string, "materials": MATERIALS, "shadowed": list(SHADOWED),
                       "objects": sorted(objects), "geometryDigest": digest, "problems": len(problems)}, fh, indent=1)
            fh.write("\n")
    triangles = sum(g.triangle_count for g in b.groups.values())
    summary = {
        "source": data["source"], "sourceSha256": data["sourceSha256"], "arenaSha256": data["arenaSha256"],
        "specOnly": data["specOnly"], "corridor": data["corridor"], "crowd": crowd,
        "objects": {n: {"triangles": b.groups[n].triangle_count, "vertices": len(b.groups[n].verts)} for n in objects},
        "triangles": triangles, "budget": checks.budget(triangles, TRIANGLE_BUDGET),
        "geometryDigest": digest, "objectDigests": object_digests,
        "meshHealth": kit.mesh_health(objects),
        "problems": problems, "checks": report, "aoBakeSeconds": bake_seconds,
        "buildSeconds": round(time.time() - t0, 1),
    }
    with open(os.path.join(a.report, "build-report.json"), "w") as fh:
        json.dump(summary, fh, indent=1)
    print("SLOP_PIT_BUILD", json.dumps({k: summary[k] for k in ("triangles", "crowd", "aoBakeSeconds", "buildSeconds", "geometryDigest")}),
          "problems", len(problems))
    if summary["budget"]["over"]:
        print("  OVER BUDGET: %d triangles planned, %d built" % (TRIANGLE_BUDGET, triangles))
    if summary["meshHealth"]["objects"]:
        print("  MESH HEALTH", json.dumps(summary["meshHealth"]))
    for p in problems[:25]:
        print("  PROBLEM", p)
    if a.preview:
        import preview as PV
        PV.render(b, objects, os.path.join(a.report, "preview"), {"SP_Venue": atlas_path, "SP_Haze": atlas_path,
                                                                    "SP_Lit": atlas_path, "SP_Canvas": canvas_path,
                                                                    "SP_Floor": floor_path}, a.views)


def dry_run():
    """Geometry and clearance only, without Blender (python3 build.py --dry)."""
    data = LAYOUT.load()
    b = Build(data)
    RING.RingTower(b).build_all()
    crowd = VENUE.Venue(b).build_all()
    report, problems = b.verify()
    tri = {n: g.triangle_count for n, g in b.groups.items()}
    print(json.dumps({"triangles": sum(tri.values()), "groups": tri, "crowd": crowd, "problems": problems[:30]}, indent=1))


if __name__ == "__main__":
    if "--dry" in sys.argv and "--" not in sys.argv:
        dry_run()
    else:
        main()
