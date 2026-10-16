"""Build the After Hours neon-city presentation in headless Blender.

    blender -b --factory-startup --python tools/blender/after_hours/build.py -- \
        [--out /tmp/after-hours-generated] [--report .stage-authoring-cache/after_hours/neon] \
        [--preview] [--views a,b] [--no-bake] [--no-export] [--spec-only]

The baked arena supplies the authoritative shell (see layout.py); --spec-only
previews the layout spec before it has been baked and refuses to export.
Output is presentation only: an FBX with vertex-coloured meshes and animation UV
channels, an atlas, a detail texture, a source stamp and a clearance report.
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

import bpy  # noqa: E402

from stagekit import blender as kit  # noqa: E402
from stagekit import checks, geo  # noqa: E402

import city as CITY  # noqa: E402
import layout as LAYOUT  # noqa: E402
import roof as ROOF  # noqa: E402
import textures  # noqa: E402

REPO = LAYOUT.REPO
DEFAULT_OUT = "/tmp/after-hours-generated"
DEFAULT_REPORT = REPO / ".stage-authoring-cache/after_hours/neon"
MATERIALS = ["AH_Build", "AH_Night", "AH_Far", "AH_Sky"]
ENVELOPE = 25.0         # orbit radius 10 x 1.5 zoom plus the 10 m side blast margin
ENVELOPE_CEILING = 2.0  # metres below the kill plane
NO_AO = {"Sky", "City Traffic", "City Far"}
NIGHT = ("AH_Night", "AH_Far", "AH_Sky")
SEED = 20261016
TRIANGLE_BUDGET = 300_000  # planning target, reported only (performance is measured in Unity)


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
        lo = (min(p[0] for p in data["roof"]), min(p[1] for p in data["roof"]))
        hi = (max(p[0] for p in data["roof"]), max(p[1] for p in data["roof"]))
        self.roof_box = (lo, hi)

    def group(self, name):
        if name not in self.groups:
            self.groups[name] = geo.Mesh(name, MATERIALS)
        return self.groups[name]

    # ------------------------------------------------------------ recording
    def surface(self, group, owner, build):
        m, g = geo.Mesh(owner, MATERIALS), geo.Mesh(owner + " Glow", MATERIALS)
        build(m, g)
        for mesh in (m, g):
            self.checks.append(("surface", owner, mesh))
            self.group(group).extend(mesh)

    def wall(self, group, owner, build):
        m, g = geo.Mesh(owner, MATERIALS), geo.Mesh(owner + " Glow", MATERIALS)
        build(m, g)
        for mesh in (m, g):
            self.checks.append(("wall", owner, mesh))
            self.group(group).extend(mesh)

    def below(self, group, owner, build):
        m, g = geo.Mesh(owner, MATERIALS), geo.Mesh(owner + " Glow", MATERIALS)
        build(m, g)
        for mesh in (m, g):
            self.checks.append(("below", owner, mesh))
            self.group(group).extend(mesh)

    def background(self, group, label, build, far=False):
        m = geo.Mesh(label, MATERIALS)
        mover = geo.Mesh(label + " Movers", MATERIALS)
        self.mover = mover
        build(m)
        self.mover = None
        if far:
            # Distant pieces keep their lights through the haze (less fog on AH_Far).
            for mesh in (m, mover):
                mesh.materials = ["AH_Far" if n == "AH_Night" else n for n in mesh.materials]
        self.checks.append(("background", label, m))
        self.group(group).extend(m)
        if mover.faces:
            self.checks.append(("background", label + " Movers", mover))
            self.group("City Traffic").extend(mover)

    # ------------------------------------------------------------ checks
    def verify(self):
        C = self.C
        roof = self.data["roof"]
        top = self.data["roofTop"]
        report = {"surface": {}, "wall": {}, "below": {}, "background": {}}
        problems = []
        ceiling = C["killY"] - ENVELOPE_CEILING
        (lx, lz), (hx, hz) = self.roof_box
        for role, owner, m in self.checks:
            if not m.verts:
                continue
            entry = report[role].setdefault(owner, {})
            if role == "surface":
                if owner in self.data["risers"]:
                    x0, z0, x1, z1, rtop = self.data["risers"][owner]
                    outside = max(max(x0 - x, x - x1, z0 - z, z - z1, 0.0) for x, y, z in m.verts)
                    above = max(y - rtop for x, y, z in m.verts)
                    below = min(y for x, y, z in m.verts) - top
                    tol = ROOF.WALL_PROUD + 1e-4
                else:
                    outside = max(0.0, max(max((x - a[0]) * n[0] + (z - a[1]) * n[1] for a, b, n, L in geo.edges(roof)) for x, y, z in m.verts))
                    above = max(y - top for x, y, z in m.verts)
                    below = min(y for x, y, z in m.verts) - top
                    tol = 1e-4
                entry.update({"outside": round(outside, 4), "above": round(above, 5), "lowest": round(below, 3)})
                if outside > tol:
                    problems.append("surface %s extends %.3f m outside its footprint" % (owner, outside))
                if above > 1e-4:
                    problems.append("surface %s rises %.4f m above its walkable top" % (owner, above))
                if below < -0.1 and owner not in self.data["risers"]:
                    problems.append("surface %s reaches %.2f m below the roof" % (owner, below))
            elif role == "wall":
                outside = max(0.0, max(max((x - a[0]) * n[0] + (z - a[1]) * n[1] for a, b, n, L in geo.edges(roof)) for x, y, z in m.verts))
                above = max(y - top for x, y, z in m.verts)
                corridor_proud = max([0.0] + [max((x - a[0]) * n[0] + (z - a[1]) * n[1] for a, b, n, L in geo.edges(roof))
                                              for x, y, z in m.verts if y >= C["killY"]])
                entry.update({"outside": round(outside, 4), "proudInCorridor": round(corridor_proud, 4), "above": round(above, 5)})
                if corridor_proud > ROOF.WALL_PROUD + 1e-4:
                    problems.append("wall %s stands %.3f m proud inside the corridor" % (owner, corridor_proud))
                if above > 1e-4:
                    problems.append("wall %s rises above the roof" % owner)
            elif role == "below":
                highest = max(y for x, y, z in m.verts)
                entry["highest"] = round(highest, 3)
                if highest > ceiling:
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
                    # The orbit camera stays within ~8 m above the top blast plane.
                    if ceiling < y < C["topY"] + 30.0:
                        dx = max(lx - x, x - hx, 0.0)
                        dz = max(lz - z, z - hz, 0.0)
                        d = math.hypot(dx, dz)
                        nearest = min(nearest, d)
                        if d < ENVELOPE:
                            near_high += 1
                entry.update({"facesInsideCorridor": crossing, "verticesInEnvelope": near_high,
                              "nearestAboveCeiling": round(nearest, 2) if nearest < 1e9 else None,
                              "triangles": m.triangle_count})
                if crossing:
                    problems.append("background %s has %d faces intersecting the blast corridor" % (owner, crossing))
                if near_high:
                    problems.append("background %s has %d vertices above %.1f within %.0f m of the roof" % (owner, near_high, ceiling, ENVELOPE))
        return report, problems


# -------------------------------------------------------------- Blender side

def glowing(me, rgba):
    """Glowing StageNeon corners (vertex alpha marks glow) keep their colour unshaded by AO."""
    import numpy as np
    return np.isin(kit.loop_materials(me), NIGHT) & (rgba[:, 3] > 0.5)


def main():
    a = args()
    t0 = time.time()
    data = LAYOUT.load(spec_only=a.spec_only)
    if a.spec_only and not a.no_export:
        raise SystemExit("--spec-only previews an unbaked layout; add --no-export.")
    b = Build(data)
    ROOF.ClubTower(b).build_all()
    CITY.City(b).build_all()
    report, problems = b.verify()
    os.makedirs(a.report, exist_ok=True)
    os.makedirs(a.out, exist_ok=True)
    if problems and not a.no_export:
        print("AFTER_HOURS_PROBLEMS", json.dumps(problems[:40]))
        raise SystemExit("Clearance problems found; fix them before exporting presentation art.")
    atlas_path = os.path.join(a.out, "AfterHoursAtlas.png")
    textures.atlas(atlas_path)
    textures.detail(os.path.join(a.out, "AfterHoursDetail.png"))
    kit.reset_scene(MATERIALS)
    objects = kit.build_objects(b.groups, channels=True)
    bake_seconds = None if a.no_bake else kit.bake_ao(objects, a.samples, ao_distance=3.0, floor=0.28, gamma=1.1,
                                                      skip=NO_AO, keep=glowing)
    digest, object_digests = kit.digest(objects)
    if not a.no_export:
        kit.export_fbx(objects, os.path.join(a.out, "AfterHoursNeon.fbx"))
        with open(os.path.join(a.out, "AfterHoursNeon.layout.json"), "w") as fh:
            json.dump({"generator": "tools/blender/after_hours/build.py", "seed": SEED,
                       "source": data["source"], "sourceSha256": data["sourceSha256"],
                       "arena": data.get("arena"), "arenaSha256": data["arenaSha256"],
                       "blender": bpy.app.version_string, "materials": MATERIALS,
                       "objects": sorted(objects), "geometryDigest": digest, "problems": len(problems)}, fh, indent=1)
            fh.write("\n")
    triangles = sum(g.triangle_count for g in b.groups.values())
    summary = {
        "source": data["source"], "sourceSha256": data["sourceSha256"], "arenaSha256": data["arenaSha256"],
        "specOnly": data["specOnly"], "corridor": data["corridor"],
        "objects": {n: {"triangles": b.groups[n].triangle_count, "vertices": len(b.groups[n].verts)} for n in objects},
        "triangles": triangles, "budget": checks.budget(triangles, TRIANGLE_BUDGET),
        "geometryDigest": digest, "objectDigests": object_digests,
        "problems": problems, "checks": report, "aoBakeSeconds": bake_seconds,
        "buildSeconds": round(time.time() - t0, 1),
    }
    with open(os.path.join(a.report, "build-report.json"), "w") as fh:
        json.dump(summary, fh, indent=1)
    print("AFTER_HOURS_BUILD", json.dumps({k: summary[k] for k in ("triangles", "aoBakeSeconds", "buildSeconds", "geometryDigest")}),
          "problems", len(problems))
    if summary["budget"]["over"]:
        print("  OVER BUDGET: %d triangles planned, %d built" % (TRIANGLE_BUDGET, triangles))
    for p in problems[:25]:
        print("  PROBLEM", p)
    if a.preview:
        import preview as PV
        PV.render(b, objects, os.path.join(a.report, "preview"), atlas_path, a.views)


if __name__ == "__main__":
    main()
