"""Taper the flare of Wibou's fitted coat and move its sway chains onto the new cloth.

    blender -b --factory-startup --python-exit-code 1 --python tools/blender/wibou_coat/refit_coat.py -- \
        <wibou.fbx> <wibou_coat.fbx> <out_dir> [--hem 0.8] [--from 0.25]

Works on build_coat.py's output (the shipped wibou_coat.fbx), because the Tripo source it was built from is gone.
Scaling the Tripo coat 1.65x to fit his shoulders left it a bell: by the hem the cloth stood 6-15 cm off his hips, so
it never read as hanging and his hands sat inside it with the arms down. Below `--from` (metres above the origin, in
Blender's bind frame) every vertex moves toward the body axis; the radius scale eases from 1 to `--hem` at the hem.
No vertex comes closer than CLEARANCE to the body, and a vertex that would keeps part of its old gap, so the inner and
outer cloth layers stay in order. Arms are not counted as body. Weights, UVs and Wibou's bones are unchanged;
the four `Coat_*` chains move to the tapered cloth. Writes <out_dir>/WibouCoat.fbx and refit-report.json.

Tried and dropped (2026-11-11): giving the front of the skirt 60% of the weights of the body under it, so it would
move like the pants. Against his clips it cleared the thighs no better than the spring's leg lift alone and took
most of the hang away.
"""
import bpy
import json
import math
import os
import sys

import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index("--") + 1:]
BODY_FBX, COAT_FBX, OUT = (os.path.abspath(a) for a in argv[:3])
opts = {"hem": 0.8, "from": 0.25}
for i in range(3, len(argv) - 1, 2):
    opts[argv[i].lstrip("-")] = float(argv[i + 1])
HEM_SCALE, TAPER_FROM = opts["hem"], opts["from"]
CLEARANCE = 0.015      # cloth kept this far off the body
KEEP_GAP = 0.35        # share of its old gap a vertex keeps when the scale alone would bring it inside CLEARANCE
CHAIN_AZIMUTHS = {"FL": 45.0, "BL": 135.0, "BR": 225.0, "FR": 315.0}  # as build_coat.py: 0 = front, 90 = his left
CHAIN_ROOT_Z, CHAIN_MID_Z = 0.16, 0.04
os.makedirs(OUT, exist_ok=True)
M = "mixamorig:"

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=BODY_FBX)
body_arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
body = next(o for o in bpy.data.objects if o.type == 'MESH')
if body_arm.animation_data:  # wibou.fbx carries a Mixamo take; the coat was fitted in the bind pose
    body_arm.animation_data_clear()
body_arm.data.pose_position = 'REST'
before = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=COAT_FBX)
arm = next(o for o in bpy.data.objects if o not in before and o.type == 'ARMATURE')
coat = next(o for o in bpy.data.objects if o not in before and o.type == 'MESH')
bpy.context.view_layer.update()

# The body without its arms: in the A-pose they hang beside the hips and would count as body there.
groups = {g.index: g.name for g in body.vertex_groups}
ARM_WORDS = ("Shoulder", "Arm", "Hand")


def is_arm(v):
    best = max(v.groups, key=lambda g: g.weight, default=None)
    return best is not None and any(w in groups[best.group] for w in ARM_WORDS)


arm_vert = [is_arm(v) for v in body.data.vertices]
BV = [body.matrix_world @ v.co for v in body.data.vertices]
BF = [tuple(p.vertices) for p in body.data.polygons if not any(arm_vert[i] for i in p.vertices)]
tree = BVHTree.FromPolygons(BV, BF)
hips = body_arm.matrix_world @ body_arm.data.bones[M + "Hips"].head_local
AXIS = Vector((0.0, hips.y + 0.012, 0.0))  # build_coat.py's body centre

P = [coat.matrix_world @ v.co for v in coat.data.vertices]
z_hem = min(p.z for p in P)


def smoothstep(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def body_radius(direction, z):
    """Distance from the axis to the body's outside along `direction` at height z; None where there is no body."""
    centre = Vector((AXIS.x, AXIS.y, z))
    hit = tree.ray_cast(centre + direction * 2.0, -direction)
    return None if hit[0] is None else (hit[0] - centre).length


moved = held = 0
worst = 0.0
for i, p in enumerate(P):
    scale = 1.0 - (1.0 - HEM_SCALE) * smoothstep(TAPER_FROM, z_hem, p.z)
    if scale >= 1.0:
        continue
    radial = Vector((p.x - AXIS.x, p.y - AXIS.y, 0.0))
    r = radial.length
    if r < 1e-6:
        continue
    direction = radial / r
    target = r * scale
    rb = body_radius(direction, p.z)
    if rb is not None:
        gap = r - (rb + CLEARANCE)
        floor = rb + CLEARANCE + KEEP_GAP * gap if gap > 0 else r  # already inside the clearance: leave it
        if floor > target:
            target = floor
            held += 1
    new = Vector((AXIS.x + direction.x * target, AXIS.y + direction.y * target, p.z))
    worst = max(worst, (new - p).length)
    P[i] = new
    moved += 1
to_local = coat.matrix_world.inverted()
for v, p in zip(coat.data.vertices, P):
    v.co = to_local @ p
coat.data.update()


# --- sway chains onto the tapered cloth (build_coat.py's placement) ---------------------------------------
def cloth_radius(az, z):
    rs = []
    for p in P:
        if abs(p.z - z) > 0.015:
            continue
        d = p - AXIS
        a = math.degrees(math.atan2(d.x, -d.y)) % 360
        if min(abs(a - az), 360 - abs(a - az)) < 12:
            rs.append(math.hypot(d.x, d.y))
    rs.sort()
    return rs[len(rs) // 2] if rs else None


chains = {}
for key, az in CHAIN_AZIMUTHS.items():
    pts = []
    for z in (CHAIN_ROOT_Z, CHAIN_MID_Z, z_hem + 0.01):
        r = cloth_radius(az, z) or 0.2
        a = math.radians(az)
        pts.append(Vector((AXIS.x + r * math.sin(a), AXIS.y - r * math.cos(a), z)))
    chains[key] = pts
bpy.context.view_layer.objects.active = arm
for o in bpy.data.objects:
    o.select_set(o is arm)
bpy.ops.object.mode_set(mode='EDIT')
inv = arm.matrix_world.inverted()
for key, pts in chains.items():
    for k, name in enumerate((f"Coat_{key}_1", f"Coat_{key}_2", f"Coat_{key}_Tip")):
        eb = arm.data.edit_bones[name]
        eb.use_connect = False
        eb.head = inv @ pts[min(k, 2)]
        eb.tail = inv @ (pts[k + 1] if k < 2 else pts[2] + (pts[2] - pts[1]).normalized() * 0.03)
for key in chains:
    for name in (f"Coat_{key}_2", f"Coat_{key}_Tip"):
        arm.data.edit_bones[name].use_connect = True
bpy.ops.object.mode_set(mode='OBJECT')

report = {"body": BODY_FBX, "coat": COAT_FBX, "hemScale": HEM_SCALE, "taperFrom": TAPER_FROM, "hemZ": round(z_hem, 4),
          "vertsMoved": moved, "vertsHeldOffTheBody": held, "largestMove": round(worst, 4),
          "chains": {k: [[round(c, 4) for c in p] for p in v] for k, v in chains.items()},
          "widthX": round(max(p.x for p in P) - min(p.x for p in P), 4),
          "hemRadius": {str(int(az)): round(cloth_radius(az, z_hem + 0.02) or 0.0, 4) for az in range(0, 360, 45)}}

fbx_out = os.path.join(OUT, "WibouCoat.fbx")
for o in bpy.data.objects:
    o.select_set(o in (arm, coat))
bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.fbx(filepath=fbx_out, use_selection=True, object_types={'ARMATURE', 'MESH'},
                         add_leaf_bones=False, bake_anim=False, use_armature_deform_only=False,
                         path_mode='STRIP', mesh_smooth_type='FACE')
report["fbx"] = fbx_out
with open(os.path.join(OUT, "refit-report.json"), "w") as f:
    json.dump(report, f, indent=2)
print("REPORT", json.dumps(report))
