"""Build the Table Manners kids' craft-room presentation in headless Blender.

    blender -b --factory-startup --python-exit-code 1 --python tools/blender/table_manners/build.py -- \
        [--out /tmp/table-manners-generated] [--report .stage-authoring-cache/table_manners/craft] \
        [--preview] [--views a,b] [--no-bake] [--no-export] [--spec-only]

The baked arena supplies the authoritative shell (see layout.py); --spec-only
previews the layout spec before it has been baked and refuses to export.
Output is presentation only: an FBX with vertex-coloured meshes (plus StageNeon
animation UV channels), an atlas, a detail texture, a source stamp and a clearance
report. Run with a fresh factory scene; it never opens or saves a .blend file.
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

import layout as LAYOUT  # noqa: E402
import presentation as PRESENTATION  # noqa: E402
import room as ROOM  # noqa: E402
import table as TABLE  # noqa: E402
import textures  # noqa: E402

REPO = LAYOUT.REPO
DEFAULT_OUT = "/tmp/table-manners-generated"
DEFAULT_REPORT = REPO / ".stage-authoring-cache/table_manners/craft"
MATERIALS = ["TM_Build", "TM_Cloth", "TM_Glow", "TM_Sky"]
# Camera clearance: the orbit camera reaches 15 m from a fighter who can be 10 m past
# the table edge, never below the fighter (pitch 0-45 degrees) and at most ~11 m above
# the top blast line. Nothing within ENVELOPE of the edge may sit between the kill
# plane and ENVELOPE_TOP.
ENVELOPE = checks.CAMERA_REACH_FROM_OUTLINE
ENVELOPE_TOP = 35.0
NO_AO = {"Sky", "Garden Far", "Toy Train"}
# Flush layers stacked millimetres above another surface. They are hidden while the rest
# bakes (so the surface below is not occluded) and keep their unshaded colours.
DECALS = {"Table Decals", "Wall Decals"}
SMOOTH = {"Play-Dough Cake"}
SEED = 20261016
TRIANGLE_BUDGET = 300_000  # planning target, reported only (performance is measured in Unity)


def args():
    return checks.build_args(DEFAULT_OUT, DEFAULT_REPORT)


def outside_distance(outline, x, z):
    """Signed-ish clearance: max distance past any edge (0 or less when inside)."""
    return max((x - a[0]) * n[0] + (z - a[1]) * n[1] for a, b, n, L in geo.edges(outline))


class Build:
    def __init__(self, data):
        self.data = data
        self.C = data["corridor"]
        self.rng = random.Random(SEED)
        self.groups = {}
        self.checks = []
        self.mover = None

    def group(self, name):
        if name not in self.groups:
            self.groups[name] = geo.Mesh(name, MATERIALS)
        return self.groups[name]

    def _record(self, role, group, owner, build, **extra):
        m, g = geo.Mesh(owner, MATERIALS), geo.Mesh(owner + " Glow", MATERIALS)
        build(m, g)
        for mesh in (m, g):
            self.checks.append((role, mesh.name, mesh, extra))
            self.group(group).extend(mesh)

    def surface(self, group, owner, build):
        """Dressing on the tabletop: inside the outline, never above y = top."""
        self._record("surface", group, owner, build)

    def wall(self, group, owner, build):
        """The drape: within WALL_PROUD of the outline inside the corridor."""
        self._record("wall", group, owner, build)

    def cake(self, group, owner, build, top):
        self._record("cake-top" if top else "cake-side", group, owner, build)

    def below(self, group, owner, build):
        """Near-table dressing that must stay under the kill plane."""
        self._record("below", group, owner, build)

    def background(self, group, label, build, movers="Toy Train"):
        m = geo.Mesh(label, MATERIALS)
        mover = geo.Mesh(label + " Movers", MATERIALS)
        self.mover = mover
        build(m)
        self.mover = None
        self.checks.append(("background", label, m, {}))
        self.group(group).extend(m)
        if mover.faces:
            self.checks.append(("background", label + " Movers", mover, {}))
            self.group(movers).extend(mover)

    # ------------------------------------------------------------ checks
    def verify(self):
        C = self.C
        d = self.data
        top, kill = d["top"], d["killY"]
        table, cake = d["tableOutline"], d["cakeOutline"]
        R = d["table"]["radius"]
        cake_top = d["cake"]["top"]
        report = {}
        problems = []
        for role, owner, m, extra in self.checks:
            if not m.verts:
                continue
            entry = report.setdefault(role, {}).setdefault(owner, {})
            if role == "surface":
                outside = max(outside_distance(table, x, z) for x, y, z in m.verts)
                above = max(y for x, y, z in m.verts) - top
                lowest = min(y for x, y, z in m.verts) - top
                entry.update({"outside": round(outside, 4), "above": round(above, 5), "lowest": round(lowest, 3)})
                if outside > 1e-4:
                    problems.append("surface %s extends %.3f m outside the table" % (owner, outside))
                if above > 1e-4:
                    problems.append("surface %s rises %.4f m above the tabletop" % (owner, above))
                if lowest < TABLE.LIP_DROP - 0.01:
                    problems.append("surface %s reaches %.2f m below the tabletop" % (owner, lowest))
            elif role == "wall":
                proud = max([-1.0] + [outside_distance(table, x, z) for x, y, z in m.verts if y >= kill])
                above = max(y for x, y, z in m.verts) - top
                entry.update({"proudInCorridor": round(proud, 4), "above": round(above, 5)})
                if proud > TABLE.WALL_PROUD + 1e-4:
                    problems.append("wall %s stands %.3f m proud inside the corridor" % (owner, proud))
                if above > 1e-4:
                    problems.append("wall %s rises above the tabletop" % owner)
            elif role == "cake-top":
                outside = max(outside_distance(cake, x, z) for x, y, z in m.verts)
                above = max(y for x, y, z in m.verts) - cake_top
                lowest = min(y for x, y, z in m.verts) - cake_top
                entry.update({"outside": round(outside, 4), "above": round(above, 5), "lowest": round(lowest, 3)})
                if outside > 1e-4:
                    problems.append("cake top %s extends %.3f m outside the cake" % (owner, outside))
                if above > 1e-4:
                    problems.append("cake top %s rises %.4f m above the cake" % (owner, above))
                if lowest < -0.35:
                    problems.append("cake top %s sags %.2f m below the cake top" % (owner, lowest))
            elif role == "cake-side":
                proud = max(outside_distance(cake, x, z) for x, y, z in m.verts)
                above = max(y for x, y, z in m.verts) - cake_top
                lowest = min(y for x, y, z in m.verts) - top
                entry.update({"proud": round(proud, 4), "above": round(above, 5), "lowest": round(lowest, 4)})
                if proud > TABLE.WALL_PROUD + 1e-4:
                    problems.append("cake side %s stands %.3f m proud of the cake" % (owner, proud))
                if above > 1e-4 or lowest < -1e-4:
                    problems.append("cake side %s leaves the cake's height range" % owner)
            elif role == "below":
                highest = max(y for x, y, z in m.verts)
                entry["highest"] = round(highest, 3)
                if highest > kill:
                    problems.append("below-plane %s reaches %.2f (kill plane %.2f)" % (owner, highest, kill))
            elif role == "background":
                crossing = 0
                in_envelope = 0
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
                    if kill < y < ENVELOPE_TOP:
                        dist = max(0.0, math.hypot(x, z) - R)
                        nearest = min(nearest, dist)
                        if dist < ENVELOPE:
                            in_envelope += 1
                entry.update({"facesInsideCorridor": crossing, "verticesInEnvelope": in_envelope,
                              "nearestInCameraBand": round(nearest, 2) if nearest < 1e9 else None,
                              "triangles": m.triangle_count})
                if crossing:
                    problems.append("background %s has %d faces intersecting the blast corridor" % (owner, crossing))
                if in_envelope:
                    problems.append("background %s has %d vertices within %.0f m of the table edge between %.0f and %.0f"
                                    % (owner, in_envelope, ENVELOPE, kill, ENVELOPE_TOP))
        return report, problems


# -------------------------------------------------------------- Blender side

def glowing(me, rgba):
    """Glowing StageNeon corners (vertex alpha marks glow) keep their colour unshaded by AO."""
    import numpy as np
    return np.isin(kit.loop_materials(me), ("TM_Glow", "TM_Sky")) & (rgba[:, 3] > 0.5)


def main():
    a = args()
    t0 = time.time()
    data = LAYOUT.load(spec_only=a.spec_only)
    if a.spec_only and not a.no_export:
        raise SystemExit("--spec-only previews an unbaked layout; add --no-export.")
    b = Build(data)
    TABLE.CraftTable(b).build_all()
    ROOM.KidsRoom(b).build_all()
    report, problems = b.verify()
    os.makedirs(a.report, exist_ok=True)
    os.makedirs(a.out, exist_ok=True)
    if problems and not a.no_export:
        print("TABLE_MANNERS_PROBLEMS", json.dumps(problems[:40]))
        raise SystemExit("Clearance problems found; fix them before exporting presentation art.")
    atlas_path = os.path.join(a.out, "TableMannersAtlas.png")
    textures.atlas(atlas_path)
    textures.detail(os.path.join(a.out, "TableMannersDetail.png"))
    kit.reset_scene(MATERIALS)
    objects = kit.build_objects(b.groups, smooth=SMOOTH, channels=True)
    bake_seconds = None
    if not a.no_bake:
        decals = [o for n, o in objects.items() if n in DECALS]
        for o in decals:
            o.hide_render = True
        bake_seconds = kit.bake_ao(objects, a.samples, ao_distance=4.0, floor=0.32, gamma=1.0,
                                   skip=NO_AO | DECALS, keep=glowing)
        for o in decals:
            o.hide_render = False
    digest, object_digests = kit.digest(objects)
    if not a.no_export:
        kit.export_fbx(objects, os.path.join(a.out, "TableMannersCraftRoom.fbx"))
        with open(os.path.join(a.out, "TableMannersCraftRoom.layout.json"), "w") as fh:
            json.dump({"generator": "tools/blender/table_manners/build.py", "seed": SEED,
                       "source": data["source"], "sourceSha256": data["sourceSha256"],
                       "arena": data.get("arena"), "arenaSha256": data["arenaSha256"],
                       "blender": bpy.app.version_string, "materials": MATERIALS,
                       "objects": sorted(objects), "geometryDigest": digest, "problems": len(problems)}, fh, indent=1)
            fh.write("\n")
        write_presentation(PRESENTATION.PRESENTATION, a.out)
    triangles = sum(g.triangle_count for g in b.groups.values())
    summary = {
        "source": data["source"], "sourceSha256": data["sourceSha256"], "arenaSha256": data["arenaSha256"],
        "specOnly": data["specOnly"], "corridor": data["corridor"],
        "objects": {n: {"triangles": b.groups[n].triangle_count, "vertices": len(b.groups[n].verts)} for n in objects},
        "triangles": triangles, "budget": checks.budget(triangles, TRIANGLE_BUDGET),
        "geometryDigest": digest, "objectDigests": object_digests,
        "meshHealth": kit.mesh_health(objects),
        "problems": problems, "checks": report, "aoBakeSeconds": bake_seconds,
        "buildSeconds": round(time.time() - t0, 1),
    }
    with open(os.path.join(a.report, "build-report.json"), "w") as fh:
        json.dump(summary, fh, indent=1)
    print("TABLE_MANNERS_BUILD", json.dumps({k: summary[k] for k in ("triangles", "aoBakeSeconds", "buildSeconds", "geometryDigest")}),
          "problems", len(problems))
    if summary["budget"]["over"]:
        print("  OVER BUDGET: %d triangles planned, %d built" % (TRIANGLE_BUDGET, triangles))
    if summary["meshHealth"]["objects"]:
        print("  MESH HEALTH", json.dumps(summary["meshHealth"]))
    for p in problems[:25]:
        print("  PROBLEM", p)
    if a.preview:
        import preview as PV
        PV.render(b, objects, os.path.join(a.report, "preview"), atlas_path, a.views)


if __name__ == "__main__":
    main()
