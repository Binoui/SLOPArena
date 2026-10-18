"""Build Kaboom Quarry's presentation in headless Blender.

    blender -b --factory-startup --python-exit-code 1 --python tools/blender/manki_quarry/build.py -- \
        [--out /tmp/manki-quarry-generated] [--report .stage-authoring-cache/manki_quarry/quarry] \
        [--no-bake] [--no-export] [--spec-only]

The baked arena supplies the authoritative shell (layout.py refuses a different bake). Manki
Demolition's flooded quarry: dressed route pieces and islands on their collision boxes
(pieces.py), the crane with its lit dynamite and the detonator (landmark.py), the terraced
bowl with Mount Manki and the shore-bench yard (quarry.py), and the water with its foam,
junk and duck (water.py). Crates are the course presentation's targets, not part of this FBX.
Output is presentation only: an FBX with vertex-coloured meshes and animation UV channels,
the ground, atlas, strata, detail and ripple textures, a source stamp, the presentation
document and a clearance report. Run with a fresh factory scene; it never opens a .blend.
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

import landmark as LANDMARK  # noqa: E402
import layout as LAYOUT  # noqa: E402
import parts as K  # noqa: E402
import pieces as PIECES  # noqa: E402
import presentation as PRESENTATION  # noqa: E402
import quarry as QUARRY  # noqa: E402
import textures  # noqa: E402
import water as WATER  # noqa: E402

REPO = LAYOUT.REPO
DEFAULT_OUT = "/tmp/manki-quarry-generated"
DEFAULT_REPORT = REPO / ".stage-authoring-cache/manki_quarry/quarry"
MATERIALS = K.MATERIALS
GLOWING = (K.GLOW,)
# Painted tops, the big terrace faces and Mount Manki carry painted shade: a vertex AO bake
# across their large faces would grey them (stagekit README pitfall). Water and smoke too.
NO_AO = {"Tops", "Water", "Quarry Walls", "Mount Manki", "Smoke"}
SMOOTH = ("Smoke", "Duck")
SEED = 20261018
TRIANGLE_BUDGET = 170_000  # planning target, reported only (performance is measured in Unity)
STEM = "KaboomQuarry"
TEXTURES = {"ground": "KaboomQuarryGround.png", "atlas": "KaboomQuarryAtlas.png", "strata": "KaboomQuarryStrata.png",
            "detail": "KaboomQuarryDetail.png", "ripples": "KaboomQuarryRipples.png"}


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
        self.pieces = data["pieces"]

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
        roles = ("top", "side", "sideprop", "topprop", "detonator", "crane", "water", "flotsam", "duck", "background")
        report = {k: {} for k in roles}
        problems = []
        floor = K.WATER_FLOOR - 1e-3
        for role, owner, m in self.checks:
            if not m.verts:
                continue
            V = m.verts
            entry = report[role].setdefault(owner, {"vertices": 0})
            entry["vertices"] += len(V)
            if role in ("top", "side", "sideprop", "topprop", "detonator"):
                x0, z0, x1, z1, top = self.pieces[owner]
                out = max(_outside(x, z, (x0, z0, x1, z1)) for x, y, z in V)
                hi = max(y for x, y, z in V) - top
                lo = min(y for x, y, z in V)
                entry["outside"] = round(max(out, entry.get("outside", 0.0)), 4)
                entry["above"] = round(max(hi, entry.get("above", -99.0)), 4)
                if role == "top":
                    off = max(abs(y - top) for x, y, z in V)
                    if off > 1e-4 or out > 1e-3:
                        problems.append("top %s leaves its surface (off %.4f, outside %.4f)" % (owner, off, out))
                elif role == "side":
                    if out > PIECES.PROUD + 1e-3 or hi > 1e-3 or lo < floor:
                        problems.append("side %s leaves its box (outside %.3f, above %.3f, low %.3f)" % (owner, out, hi, lo))
                elif role == "sideprop":
                    if out > PIECES.STICK_PROUD + 1e-3 or hi > -PIECES.STICK_DROP + 1e-3 or lo < floor:
                        problems.append("side prop %s leaves its band (outside %.3f, above %.3f)" % (owner, out, hi))
                elif role == "topprop":
                    if out > 1e-3 or hi > PIECES.TOP_PROP_HIGH + 1e-3 or lo < top - 0.35:
                        problems.append("top prop %s leaves its top (outside %.3f, above %.3f, low %.3f)" % (owner, out, hi, lo))
                else:
                    if out > 0.1 or hi > PIECES.DETONATOR_HIGH or lo < floor:
                        problems.append("detonator leaves the dock (outside %.3f, above %.3f)" % (out, hi))
            elif role == "crane":
                x0, z0, x1, z1, top = self.pieces[LANDMARK.PILLAR]
                bx, bz = LANDMARK.BUNDLE
                stray = 0
                for x, y, z in V:
                    on_pillar = _outside(x, z, (x0, z0, x1, z1)) < 1e-3 and y >= top - 1e-3
                    in_bundle = math.hypot(x - bx, z - bz) <= LANDMARK.BUNDLE_RADIUS and y >= LANDMARK.BUNDLE_FLOOR
                    if not (on_pillar or in_bundle or y >= LANDMARK.CRANE_CLEAR):
                        stray += 1
                entry.update({"stray": stray, "declaredException": True})
                if stray:
                    problems.append("crane %s has %d vertices outside its declared region" % (owner, stray))
            elif role == "water":
                off = max(abs(y - WATER.WATER_Y) for x, y, z in V)
                entry.update({"offLevel": round(off, 6), "aboveKill": round(WATER.WATER_Y - C["killY"], 3)})
                if off > 1e-6 or not 0 < WATER.WATER_Y - C["killY"] < 0.2:
                    problems.append("water %s is not flat just above the KO plane" % owner)
            elif role in ("flotsam", "duck"):
                high = WATER.FLOTSAM_HIGH if role == "flotsam" else WATER.DUCK_HIGH
                near = min(_outside(x, z, p) for x, y, z in V for p in self.pieces.values())
                top_y = max(y for x, y, z in V) - WATER.WATER_Y
                entry.update({"nearestPiece": round(near, 2), "aboveWater": round(top_y, 3)})
                if top_y > high + 1e-3 or near < 2.5:
                    problems.append("%s %s is too high or too near a piece (%.2f m up, %.2f m away)" % (role, owner, top_y, near))
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
    PIECES.Pieces(b).build()
    LANDMARK.Landmark(b).build()
    QUARRY.Quarry(b).build()
    WATER.Water(b).build()
    report, problems = b.verify()
    os.makedirs(a.report, exist_ok=True)
    os.makedirs(a.out, exist_ok=True)
    if problems and not a.no_export:
        print("MANKI_QUARRY_PROBLEMS", json.dumps(problems[:40]))
        raise SystemExit("Clearance problems found; fix them before exporting presentation art.")
    paths = {k: os.path.join(a.out, v) for k, v in TEXTURES.items()}
    t_tex = time.time()
    textures.ground(paths["ground"], data)
    textures.atlas(paths["atlas"])
    textures.strata(paths["strata"])
    textures.detail(paths["detail"])
    textures.ripples(paths["ripples"])
    texture_seconds = round(time.time() - t_tex, 1)
    kit.reset_scene(MATERIALS)
    objects = kit.build_objects(b.groups, smooth=SMOOTH, channels=True)
    bake_seconds = None if a.no_bake else kit.bake_ao(objects, a.samples, ao_distance=2.2, floor=0.5, gamma=1.0,
                                                      skip=NO_AO, keep=glowing)
    digest, object_digests = kit.digest(objects)
    if not a.no_export:
        kit.export_fbx(objects, os.path.join(a.out, STEM + ".fbx"))
        with open(os.path.join(a.out, STEM + ".layout.json"), "w") as fh:
            json.dump({"generator": "tools/blender/manki_quarry/build.py", "seed": SEED,
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
    print("MANKI_QUARRY_BUILD", json.dumps({k: summary[k] for k in ("triangles", "aoBakeSeconds", "textureSeconds", "buildSeconds",
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
