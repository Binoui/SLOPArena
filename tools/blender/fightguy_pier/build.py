"""Build Fight Night Pier's presentation in headless Blender.

    blender -b --factory-startup --python-exit-code 1 --python tools/blender/fightguy_pier/build.py -- \
        [--out /tmp/fightguy-pier-generated] [--report .stage-authoring-cache/fightguy_pier/pier] \
        [--no-bake] [--no-export] [--spec-only]

The baked arena supplies the authoritative shell (layout.py refuses a different bake). The
fight club's pier at dusk: dressed decks, the burnt old pier, buoys, pods, tower and coaster
platforms on their collision boxes (pier.py); the Big Wheel, carousel, High Striker, booths,
ring, crowd and the coaster's track (rides.py); the sea, sunset sky, town and headlands
(world.py). Balloons and bonus targets are the course presentation's, not part of this FBX.
Output is presentation only: an FBX with vertex-coloured meshes and animation UV channels,
the ground, atlas, detail and ripple textures, a source stamp, the presentation document and
a clearance report. Run with a fresh factory scene; it never opens a .blend.
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
import parts as K  # noqa: E402
import pier as PIER  # noqa: E402
import presentation as PRESENTATION  # noqa: E402
import rides as RIDES  # noqa: E402
import textures  # noqa: E402
import world as WORLD  # noqa: E402

REPO = LAYOUT.REPO
DEFAULT_OUT = "/tmp/fightguy-pier-generated"
DEFAULT_REPORT = REPO / ".stage-authoring-cache/fightguy_pier/pier"
MATERIALS = K.MATERIALS
GLOWING = (K.GLOW, K.SKY)
# Painted tops and big flat paint carry their own shade; the sea and sky are unlit.
NO_AO = {"Tops", "Sea", "Sky", "Clouds", "Town Lights", "Big Wheel Lights", "Lights"}
SMOOTH = ()
SEED = 20261018
TRIANGLE_BUDGET = 240_000  # planning target, reported only (performance is measured in Unity)
STEM = "FightNightPier"
TEXTURES = {"ground": "FightNightPierGround.png", "atlas": "FightNightPierAtlas.png",
            "detail": "FightNightPierDetail.png", "ripples": "FightNightPierRipples.png"}
BODY = 2.0          # the fighter's height band above a walkable top
EDGE = 1.0          # fixtures on a walkable top keep within this of its edge, unless declared


def args():
    return checks.build_args(DEFAULT_OUT, DEFAULT_REPORT)


def _outside(x, z, box):
    x0, z0, x1, z1 = box[:4]
    return max(x0 - x, x - x1, z0 - z, z - z1, 0.0)


class Build:
    def __init__(self, data):
        self.data = data
        self.C = data["corridor"]
        self.rng = random.Random(SEED)
        self.groups = {}
        self.checks = []
        self.fixtures = []      # (name, footprint (x0, z0, x1, z1), declared)
        self.pieces = data["pieces"]
        self.targets = [(t[1], t[2], t[3], LAYOUT.target_radius(t)) for t in data["spec"]["targets"]]

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
        roles = ("top", "side", "topprop", "lamps", "fixture", "water", "background")
        report = {k: {} for k in roles}
        problems = []
        floor = K.WATER_FLOOR - 1e-3
        declared = {name for name, _, d in self.fixtures if d}
        for role, owner, m in self.checks:
            if not m.verts:
                continue
            V = m.verts
            entry = report[role].setdefault(owner, {"vertices": 0})
            entry["vertices"] += len(V)
            if role in ("top", "side", "topprop"):
                x0, z0, x1, z1, top = self.pieces[owner]
                out = max(_outside(x, z, (x0, z0, x1, z1)) for x, y, z in V)
                hi = max(y for x, y, z in V) - top
                lo = min(y for x, y, z in V)
                entry.update({"outside": round(out, 4), "above": round(hi, 4)})
                if role == "top":
                    off = max(abs(y - top) for x, y, z in V)
                    if off > 1e-4 or out > 1e-3:
                        problems.append("top %s leaves its surface (off %.4f, outside %.4f)" % (owner, off, out))
                elif role == "side":
                    if out > PIER.PROUD + 1e-3 or hi > 1e-3 or lo < floor:
                        problems.append("side %s leaves its box (outside %.3f, above %.3f, low %.3f)" % (owner, out, hi, lo))
                else:
                    if out > PIER.PROUD + 1e-3 or hi > 2.0 or lo < floor:
                        problems.append("top prop %s leaves its top (outside %.3f, above %.3f)" % (owner, out, hi))
            elif role == "lamps":
                stray = 0
                for x, y, z in V:
                    on = [p for p in self.pieces.values() if _outside(x, z, p) < 0.05 and p[4] - 0.05 <= y <= p[4] + PIER.LAMP_HIGH + 0.4]
                    if not on:
                        stray += 1
                entry["stray"] = stray
                if stray:
                    problems.append("%s: %d vertices off any deck" % (owner, stray))
            elif role == "fixture":
                buried = sum(1 for x, y, z in V for tx, ty, tz, r in self.targets
                             if (x - tx) ** 2 + (y - ty) ** 2 + (z - tz) ** 2 < (r + 0.12) ** 2)
                inner = 0
                for x, y, z in V:
                    for name, (x0, z0, x1, z1, top) in self.pieces.items():
                        if top - 0.05 <= y <= top + BODY and x0 + EDGE < x < x1 - EDGE and z0 + EDGE < z < z1 - EDGE:
                            inner += 1
                            break
                entry.update({"buriedTargetVertices": buried, "innerDeckVertices": inner, "declared": owner in declared})
                if buried:
                    problems.append("fixture %s has %d vertices inside a target" % (owner, buried))
                if inner and owner not in declared:
                    problems.append("fixture %s stands %d vertices inside a walkable deck" % (owner, inner))
            elif role == "water":
                off = max(abs(y - WORLD.WATER_Y) for x, y, z in V)
                entry.update({"offLevel": round(off, 6), "aboveKill": round(WORLD.WATER_Y - C["killY"], 3)})
                if off > 1e-6 or not 0 < WORLD.WATER_Y - C["killY"] < 0.2:
                    problems.append("water %s is not flat just above the KO plane" % owner)
            elif role == "background":
                crossing = 0
                for face in m.faces:
                    pts = [V[i] for i in face]
                    xs, ys, zs = zip(*pts)
                    if (max(xs) > C["minX"] and min(xs) < C["maxX"] and max(ys) > C["killY"] and min(ys) < C["topY"]
                            and max(zs) > C["minZ"] and min(zs) < C["maxZ"] and checks.intersects_corridor(pts, C)):
                        crossing += 1
                entry.update({"facesInsideCorridor": crossing, "triangles": m.triangle_count})
                if crossing:
                    problems.append("background %s has %d faces in the blast corridor" % (owner, crossing))
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
    PIER.Pieces(b).build()
    RIDES.Rides(b).build()
    WORLD.World(b).build()
    report, problems = b.verify()
    os.makedirs(a.report, exist_ok=True)
    os.makedirs(a.out, exist_ok=True)
    if problems and not a.no_export:
        print("FIGHTGUY_PIER_PROBLEMS", json.dumps(problems[:40]))
        raise SystemExit("Clearance problems found; fix them before exporting presentation art.")
    paths = {k: os.path.join(a.out, v) for k, v in TEXTURES.items()}
    t_tex = time.time()
    textures.ground(paths["ground"], data)
    textures.atlas(paths["atlas"])
    textures.detail(paths["detail"])
    textures.ripples(paths["ripples"])
    texture_seconds = round(time.time() - t_tex, 1)
    kit.reset_scene(MATERIALS)
    objects = kit.build_objects(b.groups, smooth=SMOOTH, channels=True)
    bake_seconds = None if a.no_bake else kit.bake_ao(objects, a.samples, ao_distance=2.0, floor=0.5, gamma=1.0,
                                                      skip=NO_AO, keep=glowing)
    digest, object_digests = kit.digest(objects)
    if not a.no_export:
        kit.export_fbx(objects, os.path.join(a.out, STEM + ".fbx"))
        with open(os.path.join(a.out, STEM + ".layout.json"), "w") as fh:
            json.dump({"generator": "tools/blender/fightguy_pier/build.py", "seed": SEED,
                       "source": data["source"], "sourceSha256": data["sourceSha256"],
                       "arena": data.get("arena"), "arenaSha256": data["arenaSha256"],
                       "blender": bpy.app.version_string, "materials": MATERIALS, "textures": TEXTURES,
                       "sun": {"azimuth": PRESENTATION.SUN_AZIMUTH, "elevation": PRESENTATION.SUN_ELEVATION},
                       "objects": sorted(objects), "geometryDigest": digest, "problems": len(problems)}, fh, indent=1)
            fh.write("\n")
        write_presentation(PRESENTATION.PRESENTATION, a.out)
    triangles = sum(g.triangle_count for g in b.groups.values())
    summary = {
        "source": data["source"], "sourceSha256": data["sourceSha256"], "arenaSha256": data["arenaSha256"],
        "specOnly": data["specOnly"], "corridor": data["corridor"],
        "objects": {n: {"triangles": b.groups[n].triangle_count, "vertices": len(b.groups[n].verts)} for n in objects},
        "triangles": triangles, "budget": checks.budget(triangles, TRIANGLE_BUDGET),
        "geometryDigest": digest, "objectDigests": object_digests, "meshHealth": kit.mesh_health(objects),
        "problems": problems, "checks": report, "aoBakeSeconds": bake_seconds, "textureSeconds": texture_seconds,
        "buildSeconds": round(time.time() - t0, 1),
    }
    with open(os.path.join(a.report, "build-report.json"), "w") as fh:
        json.dump(summary, fh, indent=1)
    print("FIGHTGUY_PIER_BUILD", json.dumps({k: summary[k] for k in ("triangles", "aoBakeSeconds", "textureSeconds", "buildSeconds",
                                                                     "geometryDigest")}), "problems", len(problems))
    print("  OBJECTS", json.dumps({n: v["triangles"] for n, v in summary["objects"].items()}))
    if summary["budget"]["over"]:
        print("  OVER BUDGET: %d triangles planned, %d built" % (TRIANGLE_BUDGET, triangles))
    if summary["meshHealth"]["objects"]:
        print("  MESH HEALTH", json.dumps(summary["meshHealth"]))
    for p in problems[:25]:
        print("  PROBLEM", p)


if __name__ == "__main__":
    main()
