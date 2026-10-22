"""Fit a Tripo coat onto Wibou's bind pose, skin it to his skeleton and export it for Unity.

    blender -b --factory-startup --python-exit-code 1 --python tools/blender/wibou_coat/build_coat.py -- \
        <tripo_coat.fbx> <wibou.fbx> <out_dir> [--pick R] [--scale 1.65] [--dy 0.095] [--dz -0.085]

The Tripo export of 2026-10-21 holds a turnaround: three coats side by side (front, side and
back view). `--pick` selects one by its order along X (L, M, R); R is the back view, the only
one with a closed back and a vent. Fit values are in Blender's Wibou frame (metres, Z up,
front -Y, bind pose).

Writes `<out_dir>/WibouCoat.fbx` (Wibou's skeleton plus four coat sway chains under Hips,
and the skinned coat), `textures/WibouCoat_BaseColor.png`, `build-report.json` and
`preview/*.png`. The body mesh is not exported; `unity_import.eval.cs` binds the coat to
the existing Wibou prefab.
"""
import bpy
import bmesh
import json
import math
import os
import shutil
import sys

import mathutils
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

argv = sys.argv[sys.argv.index("--") + 1:]
COAT_FBX, WIBOU_FBX, OUT = argv[0], argv[1], argv[2]
opts = {"pick": "R", "scale": 1.65, "dy": 0.095, "dz": -0.085, "previews": "1"}
for i in range(3, len(argv) - 1, 2):
    opts[argv[i].lstrip("-")] = argv[i + 1]
PICK = opts["pick"]
SCALE, DY, DZ = float(opts["scale"]), float(opts["dy"]), float(opts["dz"])
os.makedirs(os.path.join(OUT, "textures"), exist_ok=True)
os.makedirs(os.path.join(OUT, "preview"), exist_ok=True)
report = {"coatFbx": os.path.abspath(COAT_FBX), "wibouFbx": os.path.abspath(WIBOU_FBX),
          "pick": PICK, "scale": SCALE, "offset": [0.0, DY, DZ]}

CLEARANCE = 0.012      # cloth kept this far off the skin
RELAX_RADIUS = 0.04    # neighbourhood that spreads each push, so both cloth layers move together
RELAX_ROUNDS = 8
NECK_SKIN_R = 0.075    # above the neck, only surfaces this close to the neck axis are skin; the rest is hair
CHAIN_AZIMUTHS = {"FL": 45.0, "BL": 135.0, "BR": 225.0, "FR": 315.0}  # 0 = front, 90 = his left (+X)
CHAIN_ROOT_Z, CHAIN_MID_Z = 0.16, 0.04
SKIRT_TOP, SKIRT_FULL = 0.20, 0.10   # skirt weights fade in between these heights
LEG_SHARE = 0.25                     # part of the skirt that follows the thigh on its side
MAX_INFLUENCES = 4


def smoothstep(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


# --- Wibou in bind pose -------------------------------------------------------------------
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=WIBOU_FBX)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
body = next(o for o in bpy.data.objects if o.type == 'MESH')
# wibou.fbx carries a Mixamo take whose first frame lifts the hips ~1 m; work and export in the bind pose.
if arm.animation_data:
    arm.animation_data.action = None
arm.data.pose_position = 'REST'
for pb in arm.pose.bones:
    pb.matrix_basis.identity()
bpy.context.view_layer.update()
bme = body.evaluated_get(bpy.context.evaluated_depsgraph_get()).to_mesh()
BV = [body.matrix_world @ v.co for v in bme.vertices]
BF = [tuple(p.vertices) for p in bme.polygons]
tree = BVHTree.FromPolygons(BV, BF)
body_groups = {g.index: g.name for g in body.vertex_groups}
body_w = [{body_groups[g.group]: g.weight for g in v.groups if g.weight > 0} for v in body.data.vertices]


def bone_head(name):
    return arm.matrix_world @ arm.data.bones["mixamorig:" + name].head_local


def bone_tail(name):
    return arm.matrix_world @ arm.data.bones["mixamorig:" + name].tail_local


neck = bone_head("Neck")
hips = bone_head("Hips")
body_center = mathutils.Vector((0.0, hips.y + 0.012, 0.0))

# --- the coat -----------------------------------------------------------------------------
before = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=COAT_FBX)
src = next(o for o in bpy.data.objects if o not in before and o.type == 'MESH')
for o in bpy.data.objects:
    o.select_set(False)
bpy.context.view_layer.objects.active = src
src.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.mesh.separate(type='LOOSE')
bpy.ops.object.mode_set(mode='OBJECT')
parts = [o for o in bpy.data.objects if o not in before and o.type == 'MESH']
parts.sort(key=lambda p: sum(v.co.x for v in p.data.vertices) / len(p.data.vertices))
report["parts"] = [{"verts": len(p.data.vertices), "tris": sum(len(f.vertices) - 2 for f in p.data.polygons)} for p in parts]
if len(parts) == 3:
    coat = parts["LMR".index(PICK)]
else:
    coat = max(parts, key=lambda p: len(p.data.vertices))
for p in parts:
    if p is not coat:
        bpy.data.objects.remove(p, do_unlink=True)
for o in list(bpy.data.objects):
    if o.type not in ('MESH', 'ARMATURE'):
        bpy.data.objects.remove(o, do_unlink=True)
coat.name = coat.data.name = "WibouCoat"
color_src = None
for m in coat.data.materials:
    for n in m.node_tree.nodes:
        if n.type == 'TEX_IMAGE' and n.image and os.path.basename(n.image.filepath).lower() == "color.png":
            color_src = bpy.path.abspath(n.image.filepath)
if color_src is None:
    color_src = os.path.join(os.path.splitext(COAT_FBX)[0] + ".fbm", "Color.png")
report["colorSource"] = color_src

raw = [v.co.copy() for v in coat.data.vertices]
mn = mathutils.Vector([min(c[i] for c in raw) for i in range(3)])
mx = mathutils.Vector([max(c[i] for c in raw) for i in range(3)])
base = mathutils.Vector(((mn.x + mx.x) / 2, (mn.y + mx.y) / 2, mn.z))
raw = [c - base for c in raw]
# The back view (R) faces +Y; turn it to face -Y like Wibou.
ROT = mathutils.Matrix.Rotation(math.pi if PICK == "R" else 0.0, 3, 'Z')
P = [ROT @ (c * SCALE) + mathutils.Vector((0.0, DY, DZ)) for c in raw]


def penetration(points, step=2):
    inside = total = 0
    for i in range(0, len(points), step):
        hit = tree.find_nearest(points[i])
        if hit[0] is None:
            continue
        total += 1
        if (points[i] - hit[0]).dot(hit[1]) < -0.003:
            inside += 1
    return inside / max(1, total)


report["penetrationPlaced"] = round(penetration(P), 4)

# --- sleeves: turn each sleeve about the shoulder joint onto the upper arm -----------------
sleeve_raw = [abs(c.x) > 0.115 and 0.28 < c.z < 0.50 for c in raw]
report["sleeves"] = {}
for side, (upper, lower) in ((1, ("LeftArm", "LeftForeArm")), (-1, ("RightArm", "RightForeArm"))):
    pts = [P[i] for i in range(len(P)) if sleeve_raw[i] and P[i].x * side > 0]
    centre = sum(pts, mathutils.Vector()) / len(pts)
    sh, el = bone_head(upper), bone_head(lower)
    a_arm = math.atan2(el.z - sh.z, (el.x - sh.x) * side)
    a_sleeve = math.atan2(centre.z - sh.z, (centre.x - sh.x) * side)
    theta = a_arm - a_sleeve
    rot = mathutils.Matrix.Rotation(-theta * side, 3, 'Y')
    x0, x1 = abs(sh.x) - 0.06, abs(sh.x) + 0.01
    for i, p in enumerate(P):
        if p.x * side <= 0:
            continue
        w = smoothstep(x0, x1, abs(p.x)) * smoothstep(sh.z - 0.30, sh.z - 0.22, p.z)
        if w > 0:
            P[i] = p.lerp(sh + rot @ (p - sh), w)
    report["sleeves"]["left" if side > 0 else "right"] = round(math.degrees(theta), 2)

# --- relax off the body (hair is allowed to fall over the collar) --------------------------
kd = KDTree(len(P))
for i, p in enumerate(P):
    kd.insert(p, i)
kd.balance()
neighbours = [kd.find_range(p, RELAX_RADIUS) for p in P]
for _ in range(RELAX_ROUNDS):
    need = []
    for p in P:
        hit = tree.find_nearest(p)
        if hit[0] is None or (p.z > neck.z + 0.005 and math.hypot(hit[0].x, hit[0].y - neck.y) > NECK_SKIN_R):
            need.append(mathutils.Vector())
            continue
        sd = (p - hit[0]).dot(hit[1])
        need.append(hit[1] * (CLEARANCE - sd) if sd < CLEARANCE else mathutils.Vector())
    for i in range(len(P)):
        acc, ws = mathutils.Vector(), 0.0
        for (_, j, dist) in neighbours[i]:
            w = math.exp(-(dist / (RELAX_RADIUS * 0.5)) ** 2)
            acc += need[j] * w
            ws += w
        d = acc / ws if ws else need[i]
        if need[i].length > d.length:
            d = d.normalized() * need[i].length if d.length > 1e-9 else need[i]
        P[i] = P[i] + d
report["penetrationFitted"] = round(penetration(P), 4)
for v, p in zip(coat.data.vertices, P):
    v.co = p
coat.data.update()
coat_min_z = min(p.z for p in P)

# --- sway chains ----------------------------------------------------------------------------
def cloth_radius(az, z):
    rs = []
    for p in P:
        if abs(p.z - z) > 0.015:
            continue
        d = p - body_center
        a = math.degrees(math.atan2(d.x, -d.y)) % 360
        if min(abs(a - az), 360 - abs(a - az)) < 12:
            rs.append(math.hypot(d.x, d.y))
    rs.sort()
    return rs[len(rs) // 2] if rs else None


chains = {}
heights = [CHAIN_ROOT_Z, CHAIN_MID_Z, coat_min_z + 0.01]
for key, az in CHAIN_AZIMUTHS.items():
    pts = []
    for z in heights:
        r = cloth_radius(az, z) or 0.2
        a = math.radians(az)
        pts.append(mathutils.Vector((body_center.x + r * math.sin(a), body_center.y - r * math.cos(a), z)))
    chains[key] = pts
report["chains"] = {k: [[round(c, 4) for c in p] for p in v] for k, v in chains.items()}

bpy.context.view_layer.objects.active = arm
for o in bpy.data.objects:
    o.select_set(o is arm)
bpy.ops.object.mode_set(mode='EDIT')
inv = arm.matrix_world.inverted()
hips_eb = arm.data.edit_bones["mixamorig:Hips"]
for key, pts in chains.items():
    parent = hips_eb
    names = [f"Coat_{key}_1", f"Coat_{key}_2", f"Coat_{key}_Tip"]
    for k, name in enumerate(names):
        eb = arm.data.edit_bones.new(name)
        head = inv @ pts[min(k, 2)]
        if k < 2:
            tail = inv @ pts[k + 1]
        else:  # short tip bone continuing the chain: BoneChainSpring aims the last bone at it
            prev = pts[2] - pts[1]
            tail = inv @ (pts[2] + prev.normalized() * 0.03)
        eb.head, eb.tail = head, tail
        eb.parent = parent
        eb.use_connect = k > 0
        eb.use_deform = k < 2
        parent = eb
bpy.ops.object.mode_set(mode='OBJECT')


def chain_seg(key, k):
    return chains[key][k], chains[key][k + 1]


# --- weights ----------------------------------------------------------------------------------
def transfer(p):
    hit = tree.find_nearest(p)
    face = BF[hit[2]]
    a, b, c = (BV[i] for i in face[:3])
    bary = mathutils.geometry.barycentric_transform(hit[0], a, b, c,
                                                     mathutils.Vector((1, 0, 0)), mathutils.Vector((0, 1, 0)),
                                                     mathutils.Vector((0, 0, 1)))
    w = {}
    for f, vi in zip((bary.x, bary.y, bary.z), face[:3]):
        f = max(0.0, f)
        for g, gw in body_w[vi].items():
            w[g] = w.get(g, 0.0) + f * gw
    return w


M = "mixamorig:"
ARM_BONES = {M + n for s in ("Left", "Right") for n in (s + "Arm", s + "ForeArm", s + "Hand")} | \
    {g for g in body_groups.values() if "Hand" in g}
HEAD_BONES = {M + "Head", M + "HeadTop_End"}
weights = []
sleeve_count = 0
for i, p in enumerate(P):
    w = transfer(p)
    for g in list(w):
        if g in HEAD_BONES:
            w.pop(g)
    # Sleeves are classified in the coat's own (unfitted) frame: past the body's side at sleeve height.
    sleeve = smoothstep(0.11, 0.135, abs(raw[i].x)) * smoothstep(0.26, 0.29, raw[i].z) * smoothstep(0.51, 0.48, raw[i].z)
    own_arm = M + ("LeftArm" if p.x > 0 else "RightArm")
    if sleeve > 0.5:
        sleeve_count += 1
    for g in list(w):  # the upper arm only moves sleeves; forearms and hands never move the coat
        if g == own_arm:
            w[g] *= sleeve
        elif g in ARM_BONES:
            w.pop(g)
    if sleeve > 0 and w.get(own_arm, 0.0) < sleeve * 0.5:
        w[own_arm] = w.get(own_arm, 0.0) + sleeve * 0.5  # sleeves whose nearest skin is the torso still follow the arm
    # collar: chest and neck only, so head turns do not drag it
    tc = smoothstep(neck.z - 0.04, neck.z + 0.02, p.z) * smoothstep(0.15, 0.11, math.hypot(p.x, p.y - neck.y))
    if tc > 0:
        w = {g: v * (1 - tc) for g, v in w.items()}
        w[M + "Spine2"] = w.get(M + "Spine2", 0.0) + 0.55 * tc
        w[M + "Neck"] = w.get(M + "Neck", 0.0) + 0.45 * tc
    # skirt: hang from the sway chains around the hips, partly following the thigh on its side
    ts = smoothstep(SKIRT_TOP, SKIRT_FULL, p.z)
    if ts > 0:
        d = p - body_center
        az = math.degrees(math.atan2(d.x, -d.y)) % 360
        keys = sorted(CHAIN_AZIMUTHS, key=lambda k: CHAIN_AZIMUTHS[k])
        az_list = [CHAIN_AZIMUTHS[k] for k in keys]
        sk = {}
        for idx, k in enumerate(keys):
            a0, a1 = az_list[idx], az_list[(idx + 1) % len(keys)]
            span = (a1 - a0) % 360
            off = (az - a0) % 360
            if off <= span:
                f = off / span
                for kk, share in ((k, 1 - f), (keys[(idx + 1) % len(keys)], f)):
                    lower = smoothstep(CHAIN_MID_Z + 0.04, CHAIN_MID_Z - 0.04, p.z)
                    sk[f"Coat_{kk}_1"] = sk.get(f"Coat_{kk}_1", 0.0) + share * (1 - lower)
                    sk[f"Coat_{kk}_2"] = sk.get(f"Coat_{kk}_2", 0.0) + share * lower
                break
        left = smoothstep(-0.05, 0.05, p.x)
        legs = {M + "LeftUpLeg": left, M + "RightUpLeg": 1 - left}
        w = {g: v * (1 - ts) for g, v in w.items()}
        for g, v in sk.items():
            w[g] = w.get(g, 0.0) + v * ts * (1 - LEG_SHARE)
        for g, v in legs.items():
            w[g] = w.get(g, 0.0) + v * ts * LEG_SHARE
    for g in list(w):  # the shins and feet never carry the coat
        if any(k in g for k in ("LeftLeg", "RightLeg", "Foot", "Toe")) and "UpLeg" not in g:
            w.pop(g)
    s = sum(w.values())
    weights.append({g: v / s for g, v in w.items()} if s > 0 else {M + "Spine1": 1.0})
report["sleeveVertices"] = sleeve_count

# smooth over the mesh topology, then keep the four largest influences
bm = bmesh.new()
bm.from_mesh(coat.data)
bm.verts.ensure_lookup_table()
adj = [[e.other_vert(v).index for e in v.link_edges] for v in bm.verts]
bm.free()
for _ in range(3):
    smoothed = []
    for i, w in enumerate(weights):
        acc = dict(w)
        for j in adj[i]:
            for g, v in weights[j].items():
                acc[g] = acc.get(g, 0.0) + v
        n = 1 + len(adj[i])
        smoothed.append({g: v / n for g, v in acc.items()})
    weights = smoothed
final = []
for w in weights:
    top = sorted(w.items(), key=lambda kv: -kv[1])[:MAX_INFLUENCES]
    top = [(g, v) for g, v in top if v >= 0.01]
    s = sum(v for _, v in top)
    final.append({g: v / s for g, v in top})
coat.vertex_groups.clear()
groups = {}
for i, w in enumerate(final):
    for g, v in w.items():
        if g not in groups:
            groups[g] = coat.vertex_groups.new(name=g)
        groups[g].add([i], v, 'REPLACE')
report["weightGroups"] = sorted(groups)

# --- parent like the body mesh, so the FBX mesh node matches Wibou's -------------------------
world = [coat.matrix_world @ v.co for v in coat.data.vertices]
coat.parent = arm
coat.matrix_parent_inverse = body.matrix_parent_inverse.copy()
coat.location, coat.rotation_euler, coat.scale = body.location.copy(), body.rotation_euler.copy(), body.scale.copy()
bpy.context.view_layer.update()
to_local = coat.matrix_world.inverted()
for v, p in zip(coat.data.vertices, world):
    v.co = to_local @ p
coat.data.update()
mod = coat.modifiers.new("Armature", 'ARMATURE')
mod.object = arm

# material with the colour map, for the FBX reference and the previews
tex_out = os.path.join(OUT, "textures", "WibouCoat_BaseColor.png")
shutil.copyfile(color_src, tex_out)
mat = bpy.data.materials.new("WibouCoat")
mat.use_nodes = True
bsdf = mat.node_tree.nodes["Principled BSDF"]
tex = mat.node_tree.nodes.new("ShaderNodeTexImage")
tex.image = bpy.data.images.load(tex_out)
mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
bsdf.inputs["Roughness"].default_value = 0.85
coat.data.materials.clear()
coat.data.materials.append(mat)
report["tris"] = sum(len(f.vertices) - 2 for f in coat.data.polygons)
report["verts"] = len(coat.data.vertices)

# landmarks for the Unity-side alignment check (metres, measured from bone heads)
centroid = sum(world, mathutils.Vector()) / len(world)
report["landmarks"] = {
    "centroidToHips": round((centroid - hips).length, 4),
    "centroidToNeck": round((centroid - neck).length, 4),
    "centroidToLeftArm": round((centroid - bone_head("LeftArm")).length, 4),
    "minZFromHips": round(min(p.z for p in world) - hips.z, 4),
    "maxZFromHips": round(max(p.z for p in world) - hips.z, 4),
    "widthX": round(max(p.x for p in world) - min(p.x for p in world), 4),
    "depthY": round(max(p.y for p in world) - min(p.y for p in world), 4),
}

# --- export -------------------------------------------------------------------------------------
fbx_out = os.path.join(OUT, "WibouCoat.fbx")
for o in bpy.data.objects:
    o.select_set(o in (arm, coat))
bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.fbx(filepath=fbx_out, use_selection=True, object_types={'ARMATURE', 'MESH'},
                         add_leaf_bones=False, bake_anim=False, use_armature_deform_only=False,
                         path_mode='STRIP', mesh_smooth_type='FACE')

# --- previews: bind pose, then a few controlled poses (not real clips) ------------------------
if opts["previews"] != "0":
    body_tex = os.path.join(os.path.dirname(os.path.dirname(WIBOU_FBX)), "Textures", "Baked_BaseColor.png")
    if os.path.exists(body_tex):
        img = bpy.data.images.load(os.path.abspath(body_tex))
        for m in body.data.materials:
            for n in m.node_tree.nodes:
                if n.type == 'TEX_IMAGE':
                    n.image = img
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_EEVEE'
    sc.render.resolution_x, sc.render.resolution_y = 560, 860
    wd = bpy.data.worlds.new("w")
    sc.world = wd
    wd.use_nodes = True
    wd.node_tree.nodes['Background'].inputs[0].default_value = (0.7, 0.74, 0.8, 1)
    for rot, e in (((50, 0, 30), 2.5), ((110, 0, 210), 1.2)):
        sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", 'SUN'))
        sc.collection.objects.link(sun)
        sun.data.energy = e
        sun.rotation_euler = tuple(math.radians(r) for r in rot)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    sc.collection.objects.link(cam)
    sc.camera = cam
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = 2.2

    def shoot(tag, views, ctr=(0, 0.06, 0.0)):
        c = mathutils.Vector(ctr)
        for name, d in views.items():
            d = mathutils.Vector(d).normalized()
            cam.location = c + d * 5
            cam.rotation_euler = (c - cam.location).to_track_quat('-Z', 'Y').to_euler()
            sc.render.filepath = os.path.join(OUT, "preview", f"{tag}_{name}.png")
            bpy.ops.render.render(write_still=True)

    shoot("bind", {"front": (0, -1, 0.05), "q34": (-0.7, -0.7, 0.15), "side": (1, 0, 0.05),
                   "q34back": (0.7, 0.7, 0.25), "back": (0, 1, 0.05)})
    arm.data.pose_position = 'POSE'

    def pose(rotations):
        for pb in arm.pose.bones:
            pb.matrix_basis.identity()
        for name, (axis, deg) in rotations.items():
            pb = arm.pose.bones[M + name]
            pb.rotation_mode = 'XYZ'
            setattr(pb.rotation_euler, "xyz"[axis], math.radians(deg))
        bpy.context.view_layer.update()

    # Local bone axes differ per bone; these angles were chosen by looking at the renders.
    pose({"LeftArm": (2, 70), "RightArm": (2, -70)})
    shoot("arms_up", {"front": (0, -1, 0.05), "q34back": (0.7, 0.7, 0.25)})
    pose({"LeftUpLeg": (0, -70), "Spine1": (1, 25)})
    shoot("kick_twist", {"side": (1, 0, 0.05), "q34": (-0.7, -0.7, 0.15)})
    for pb in arm.pose.bones:
        pb.matrix_basis.identity()
    arm.data.pose_position = 'REST'

with open(os.path.join(OUT, "build-report.json"), "w") as f:
    json.dump(report, f, indent=2)
print("REPORT", json.dumps(report))
