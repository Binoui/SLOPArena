"""Build Neo Mall's presentation in headless Blender.

    blender -b --factory-startup --python-exit-code 1 --python tools/blender/wibou_mall/build.py -- \
        [--out /tmp/wibou-mall-generated] [--report .stage-authoring-cache/wibou_mall/mall] \
        [--no-bake] [--no-export] [--spec-only]

The baked arena supplies the authoritative shell (layout.py refuses a different bake). The mall
after closing: dressed balconies, bridges, railings, the escalator, the elevator tower, hanging
planters and signs, the podium and the shopfront envelope (mall.py); the ground floor, columns,
glass vault and the giant katana (world.py). Mannequin targets, bosses, checkpoint maps and the
finish are the course presentation's, not part of this FBX. Output is presentation only: an FBX
with vertex-coloured meshes and animation UV channels, the ground, atlas and detail textures, a
source stamp, the presentation document and a clearance report. Run with a fresh factory scene;
it never opens a .blend.
"""
from __future__ import annotations

import json
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
import mall as MALL  # noqa: E402
import parts as K  # noqa: E402
import presentation as PRESENTATION  # noqa: E402
import textures  # noqa: E402
import world as WORLD  # noqa: E402

REPO = LAYOUT.REPO
DEFAULT_OUT = "/tmp/wibou-mall-generated"
DEFAULT_REPORT = REPO / ".stage-authoring-cache/wibou_mall/mall"
MATERIALS = K.MATERIALS
GLOWING = (K.GLOW, K.SKY)
NO_AO = {"Tops", "Vault", "Cables"}
SMOOTH = ()
SEED = 20261020
TRIANGLE_BUDGET = 260_000  # planning target, reported only (performance is measured in Unity)
STEM = "NeoMall"
TEXTURES = {"ground": "NeoMallGround.png", "atlas": "NeoMallAtlas.png", "detail": "NeoMallDetail.png"}
BODY = 2.2          # the fighter's height band above a walkable top
EDGE = 1.0          # dressing within this of a deck's edge never blocks its walkable middle
REACH = 8.0         # Jump, jump, E climbs 7.5 m: decoration nearer than this above a deck must not hang there
WALL_PROUD = 1.0    # food counters stand a metre out of the wall; everything else less


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
        spec = self.data["spec"]
        solids = self.data["solids"]
        roles = ("top", "ramp", "side", "rail", "wall", "fixture", "cable", "world", "dome", "landmark")
        report = {k: {} for k in roles}
        problems = []
        declared = {name for name, _, d in self.fixtures if d}
        walls = {n: s for n, (k, s) in solids.items() if n in spec["walls"]}
        air = [(n, (x0 + EDGE, z0 + EDGE, x1 - EDGE, z1 - EDGE), top) for n, (x0, z0, x1, z1, top) in self.pieces.items()
               if x1 - x0 > 2 * EDGE and z1 - z0 > 2 * EDGE]
        for role, owner, m in self.checks:
            if not m.verts:
                continue
            V = m.verts
            entry = report[role].setdefault(owner, {"vertices": 0})
            entry["vertices"] += len(V)
            if role == "top":
                x0, z0, x1, z1, top = self.pieces[owner]
                off = max(abs(y - top) for x, y, z in V)
                out = max(_outside(x, z, (x0, z0, x1, z1)) for x, y, z in V)
                if off > 1e-4 or out > 1e-3:
                    problems.append("top %s leaves its surface (off %.4f, outside %.4f)" % (owner, off, out))
            elif role == "side":
                x0, z0, x1, z1, top = self.pieces[owner]
                out = max(_outside(x, z, (x0, z0, x1, z1)) for x, y, z in V)
                hi = max(y for x, y, z in V) - top
                entry.update({"outside": round(out, 4), "above": round(hi, 4)})
                if out > MALL.PROUD + 1e-3 or hi > 1e-3:
                    problems.append("side %s leaves its deck (outside %.3f, above %.3f)" % (owner, out, hi))
            elif role == "rail":
                x0, z0, x1, z1, host = spec["rails"][owner]
                top = self.pieces[host][4]
                out = max(_outside(x, z, (x0, z0, x1, z1)) for x, y, z in V)
                lo, hi = min(y for x, y, z in V), max(y for x, y, z in V)
                if out > 0.1 or lo < top - 1e-3 or hi > top + 1.0 + 1e-3:
                    problems.append("rail %s leaves its railing (outside %.3f, %.2f..%.2f)" % (owner, out, lo, hi))
            elif role == "wall":
                if owner in walls:
                    s = walls[owner]
                    out = max(_outside(x, z, (s[0], s[2], s[3], s[5])) for x, y, z in V)
                    entry["outside"] = round(out, 4)
                    if out > WALL_PROUD + 1e-3:
                        problems.append("wall %s dressing stands %.2f m out" % (owner, out))
            elif role == "fixture":
                buried = sum(1 for x, y, z in V for tx, ty, tz, r in self.targets
                             if (x - tx) ** 2 + (y - ty) ** 2 + (z - tz) ** 2 < (r + 0.12) ** 2)
                entry.update({"buriedTargetVertices": buried, "declared": owner in declared})
                if buried:
                    problems.append("fixture %s has %d vertices inside a target" % (owner, buried))
            elif role in ("dome", "landmark"):
                low = 0
                for x, y, z in V:
                    for name, (x0, z0, x1, z1, top) in self.pieces.items():
                        if _outside(x, z, (x0, z0, x1, z1)) < 12.0 and y < top + REACH + 0.5:
                            low += 1
                            break
                entry["withinReach"] = low
                if low:
                    problems.append("%s %s has %d vertices within a jump of a deck" % (role, owner, low))
            # Nothing but declared fixtures stands in a deck's walkable middle.
            if role not in ("top", "ramp", "rail", "dome", "landmark") and owner not in declared:
                inner = 0
                for x, y, z in V:
                    for name, (ax0, az0, ax1, az1), top in air:
                        if ax0 < x < ax1 and az0 < z < az1 and top + 0.05 < y < top + BODY:
                            inner += 1
                            break
                if inner:
                    entry["innerDeckVertices"] = inner
                    problems.append("%s %s stands %d vertices inside a walkable deck" % (role, owner, inner))
            # No dressing inside a target (the mannequins stand on their own).
            if role in ("side", "rail", "wall", "cable", "world"):
                buried = sum(1 for x, y, z in V for tx, ty, tz, r in self.targets
                             if (x - tx) ** 2 + (y - ty) ** 2 + (z - tz) ** 2 < (r + 0.05) ** 2)
                if buried:
                    problems.append("%s %s has %d vertices inside a target" % (role, owner, buried))
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
    MALL.Mall(b).build()
    WORLD.World(b).build()
    report, problems = b.verify()
    os.makedirs(a.report, exist_ok=True)
    os.makedirs(a.out, exist_ok=True)
    if problems and not a.no_export:
        print("WIBOU_MALL_PROBLEMS", json.dumps(problems[:40]))
        raise SystemExit("Clearance problems found; fix them before exporting presentation art.")
    paths = {k: os.path.join(a.out, v) for k, v in TEXTURES.items()}
    t_tex = time.time()
    textures.ground(paths["ground"], data)
    textures.atlas(paths["atlas"])
    textures.detail(paths["detail"])
    texture_seconds = round(time.time() - t_tex, 1)
    kit.reset_scene(MATERIALS)
    objects = kit.build_objects(b.groups, smooth=SMOOTH, channels=True)
    bake_seconds = None if a.no_bake else kit.bake_ao(objects, a.samples, ao_distance=2.0, floor=0.55, gamma=1.0,
                                                      skip=NO_AO, keep=glowing)
    digest, object_digests = kit.digest(objects)
    if not a.no_export:
        kit.export_fbx(objects, os.path.join(a.out, STEM + ".fbx"))
        with open(os.path.join(a.out, STEM + ".layout.json"), "w") as fh:
            json.dump({"generator": "tools/blender/wibou_mall/build.py", "seed": SEED,
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
    print("WIBOU_MALL_BUILD", json.dumps({k: summary[k] for k in ("triangles", "aoBakeSeconds", "textureSeconds", "buildSeconds",
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
