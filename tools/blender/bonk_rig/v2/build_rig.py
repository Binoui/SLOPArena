"""Unity-ready Sir Bonk v2 rig: the owner's Mixamo rig of the body plus the dressed kit parts.

blender -b --factory-startup --python-exit-code 1 --python build_rig.py -- \
    <rigged.blend> <apose.blend> <old bonk.fbx> <texture.png> <out_dir> [name=SirBonk] [height_ratio=0.9]

<rigged.blend>: swap_mesh.py output (Mixamo skeleton and weights on the smoothed A-pose body + boots).
<apose.blend>:  apose.py output (every kit part fitted and posed with the body; same world frame).
1. Weights for each part, then everything is joined into one mesh with one material:
   - bucket, crest, eyes and wire handle: Head
   - pot lids: a fixed blend of that side's Shoulder and Arm (the nearest body weights)
   - wraps: ForeArm; colander: 65% ForeArm, 35% Arm (an elbow cap)
   - knee guards: a fixed blend of that side's UpLeg and Leg; barrel plate: a fixed blend of the spine
   - tabard, sash ring, pants: per-vertex body weights (torso skin for the tabard and sash, legs for the pants)
   - sash tail: hips at the knot, half the left thigh at the end
   - skirt flaps: hips, sharing up to 75% with the thighs toward the hem (55% let kicks through, see v1)
2. Bakes a T-pose rest (arms, forearms and hands along +-X): Humanoid takes the rest as its T-pose.
3. Adds three cape chains (Cape_L/C/R 1..4) under Spine2 for BoneChainSpring.
4. Puts HeadTop_End on the bucket top and scales bones and mesh so the bucket top is height_ratio x the old
   Bonk's standing height (the live Sir Bonk's rule), baked into bones and vertices.
Writes <out_dir>/<name>.fbx (no embedded media), textures/<name>_BaseColor.png, <name>_rig.blend, <name>_report.json.
"""
import bpy, bmesh, sys, os, json, math
from collections import defaultdict
import numpy as np
from mathutils import Vector, Matrix
from mathutils.kdtree import KDTree

argv = sys.argv[sys.argv.index("--") + 1:]
rigged, parts_blend, old_src, tex, out_dir = (os.path.abspath(a) for a in argv[:5])
name = argv[5] if len(argv) > 5 else "SirBonk"
HEIGHT_RATIO = float(argv[6]) if len(argv) > 6 else 0.9
os.makedirs(os.path.join(out_dir, "textures"), exist_ok=True)
P = "mixamorig:"
SIDES = {"Left": 1, "Right": -1}  # his left is +X
PARTS = ("helm", "helm_handle", "pauldron_a", "pauldron_b", "chestplate", "wrap_a", "wrap_b", "colander_a", "tabard",
         "sash", "skirt", "knee_a", "knee_b", "pants_a", "pants_b", "cape")
report = {"rigged": rigged, "parts": parts_blend, "old_rig": old_src, "height_ratio": HEIGHT_RATIO}


def smooth01(x):
    x = max(0.0, min(1.0, x)); return x * x * (3 - 2 * x)


# --- 0. Old Bonk: standing height ----------------------------------------------------------------
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=old_src)
old_body = max((o for o in bpy.data.objects if o.type == "MESH"), key=lambda o: len(o.data.vertices))
bpy.context.view_layer.update()
ev = old_body.evaluated_get(bpy.context.evaluated_depsgraph_get())
zs = [(ev.matrix_world @ v.co).z for v in ev.data.vertices]
old_height = max(zs) - min(zs)
report["old_standing_height"] = old_height

# --- 1. Rig and parts ------------------------------------------------------------------------------
bpy.ops.wm.open_mainfile(filepath=rigged)
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
body = next(o for o in bpy.data.objects if o.type == "MESH")
if arm.animation_data:
    arm.animation_data_clear()
for pb in arm.pose.bones:
    pb.rotation_mode = "QUATERNION"; pb.rotation_quaternion = (1, 0, 0, 0); pb.location = (0, 0, 0); pb.scale = (1, 1, 1)
bpy.context.view_layer.update()
me = body.data
to_world = body.matrix_world.copy()
with bpy.data.libraries.load(parts_blend) as (src, dst):
    dst.objects = [n for n in src.objects if n in PARTS]
parts = {o.name: o for o in dst.objects}
for o in parts.values():
    bpy.context.scene.collection.objects.link(o)
missing = [n for n in PARTS if n not in parts]
assert not missing, f"parts missing from {parts_blend}: {missing}"

gname = {g.index: g.name for g in body.vertex_groups}
BW = [{gname[g.group]: g.weight for g in v.groups if g.weight > 1e-4} for v in me.vertices]
BP = np.array([(to_world @ v.co)[:] for v in me.vertices])


def dominant(w):
    return max(w, key=w.get) if w else ""


LEG_BONES = ("UpLeg", "Leg", "Foot", "ToeBase", "Toe_End")
ARM_BONES = ("Arm", "ForeArm", "Hand")
dom = [dominant(w).replace(P, "") for w in BW]
is_leg = np.array([d.endswith(LEG_BONES) or (d == "Hips" and z < 0.42) for d, z in zip(dom, BP[:, 2])])
is_armish = np.array([any(d.startswith(s) and d[len(s):].startswith(ARM_BONES) for s in SIDES) or "Hand" in d for d in dom])
is_torso = ~is_leg & ~is_armish


def tree(mask):
    idx = np.nonzero(mask)[0]; t = KDTree(len(idx))
    for j, i in enumerate(idx): t.insert(Vector(BP[i]), j)
    t.balance(); return t, idx


TREES = {"all": tree(np.ones(len(BP), bool)), "legs": tree(is_leg), "torso": tree(is_torso)}


def nearest_w(p, which, k=4):
    t, idx = TREES[which]
    found = t.find_n(Vector(p), k)
    acc = defaultdict(float); tot = 0.0
    for co, j, d in found:
        wt = 1.0 / max(d, 1e-4); tot += wt
        for n, x in BW[idx[j]].items(): acc[n] += wt * x
    return {n: x / tot for n, x in acc.items()}


def keep(w, names):
    out = {n: x for n, x in w.items() if n in names}
    s = sum(out.values()) or 1.0
    return {n: x / s for n, x in out.items()}


def accumulate(ws):
    acc = defaultdict(float)
    for w in ws:
        for n, x in w.items(): acc[n] += x
    s = sum(acc.values()) or 1.0
    return {n: x / s for n, x in acc.items()}


def pts(o):
    return np.array([(o.matrix_world @ v.co)[:] for v in o.data.vertices])


def side_of(o):
    return "Left" if pts(o)[:, 0].mean() > 0 else "Right"


weights, roles = {}, {}
for n, o in parts.items():
    X = pts(o); s = side_of(o)
    if n in ("helm", "helm_handle"):
        W = [{P + "Head": 1.0}] * len(X); roles[n] = "Head"
    elif n.startswith("pauldron"):
        rule = keep(accumulate(nearest_w(p, "all") for p in X), {P + s + "Shoulder", P + s + "Arm"})
        W = [rule] * len(X); roles[n] = rule
    elif n.startswith("wrap"):
        W = [{P + s + "ForeArm": 1.0}] * len(X); roles[n] = s + "ForeArm"
    elif n.startswith("colander"):
        rule = {P + s + "ForeArm": 0.65, P + s + "Arm": 0.35}; W = [rule] * len(X); roles[n] = rule
    elif n.startswith("knee"):
        rule = keep(accumulate(nearest_w(p, "legs") for p in X), {P + s + "UpLeg", P + s + "Leg"})
        W = [rule] * len(X); roles[n] = rule
    elif n == "chestplate":
        rule = keep(accumulate(nearest_w(p, "torso") for p in X), {P + "Spine", P + "Spine1", P + "Spine2"})
        W = [rule] * len(X); roles[n] = rule
    elif n == "tabard":
        W = [nearest_w(p, "torso") for p in X]; roles[n] = "torso skin per vertex"
    elif n.startswith("pants"):
        W = [nearest_w(p, "legs") for p in X]; roles[n] = "leg skin per vertex"
    elif n == "sash":
        ring_low = 0.43
        tail = X[:, 2] < ring_low
        top, zmin = ring_low, X[:, 2].min()
        W = []
        for p, t in zip(X, tail):
            if t:
                share = 0.5 * smooth01((top - p[2]) / (top - zmin))
                W.append({P + "Hips": 1 - share, P + "LeftUpLeg": share})
            else:
                W.append(nearest_w(p, "torso"))
        roles[n] = f"ring: torso skin; tail ({int(tail.sum())} verts): hips + up to 50% LeftUpLeg"
    elif n == "skirt":
        ztop, zhem = X[:, 2].max(), X[:, 2].min()
        W = []
        for p in X:
            share = 0.75 * smooth01((ztop - 0.01 - p[2]) / (ztop - 0.01 - zhem))
            left = smooth01((p[0] + 0.06) / 0.12)
            W.append({P + "Hips": 1 - share, P + "LeftUpLeg": share * left, P + "RightUpLeg": share * (1 - left)})
        roles[n] = "hips, up to 75% thighs at the hem"
    elif n == "cape":
        W = [{P + "Spine2": 1.0}] * len(X); roles[n] = "Spine2 (cape chains in step 3)"
    weights[n] = W
report["part_weights"] = {k: (v if isinstance(v, str) else {a.replace(P, ""): round(b, 3) for a, b in v.items()}) for k, v in roles.items()}

# vertex groups on each part, then join everything into the body (groups merge by name)
for n, o in parts.items():
    o.vertex_groups.clear()
    groups = {}
    for i, w in enumerate(weights[n]):
        s = sum(w.values()) or 1.0
        for g, x in w.items():
            if x / s < 1e-4: continue
            if g not in groups: groups[g] = o.vertex_groups.new(name=g)
            groups[g].add([i], x / s, "REPLACE")
    o.vertex_groups.new(name="_part_" + n).add(list(range(len(o.data.vertices))), 1.0, "REPLACE")  # survives the join
    for m in list(o.modifiers): o.modifiers.remove(m)
    o.parent = None
bpy.ops.object.select_all(action="DESELECT")
order = list(PARTS)
for n in order: parts[n].select_set(True)
body.select_set(True)
bpy.context.view_layer.objects.active = body
counts = {n: len(parts[n].data.vertices) for n in order}
n_body = len(me.vertices)
bpy.ops.object.join()
me = body.data
report["verts"] = {"body+boots": n_body, **counts, "total": len(me.vertices)}
TAG = {}
for g in list(body.vertex_groups):
    if g.name.startswith("_part_"):
        TAG[g.name[6:]] = [v.index for v in me.vertices if any(x.group == g.index for x in v.groups)]
        body.vertex_groups.remove(g)
assert all(len(TAG[n]) == counts[n] for n in order), "part tags lost in the join"

# one material with the kit texture
img = bpy.data.images.load(tex)
mat = bpy.data.materials.new(f"{name}_Body"); mat.use_nodes = True
bsdf = mat.node_tree.nodes["Principled BSDF"]
tn = mat.node_tree.nodes.new("ShaderNodeTexImage"); tn.image = img
mat.node_tree.links.new(tn.outputs["Color"], bsdf.inputs["Base Color"]); bsdf.inputs["Roughness"].default_value = 0.85
me.materials.clear(); me.materials.append(mat)
for poly in me.polygons: poly.material_index = 0


def get_w(k):
    return {body.vertex_groups[g.group].name: g.weight for g in me.vertices[k].groups if g.weight > 0}


def set_w(k, w):
    for g in list(me.vertices[k].groups):
        body.vertex_groups[g.group].remove([k])
    tot = sum(w.values())
    if tot <= 0: return
    for gname_, x in w.items():
        if x / tot > 1e-4:
            vg = body.vertex_groups.get(gname_) or body.vertex_groups.new(name=gname_)
            vg.add([k], x / tot, "REPLACE")


# --- 2. T-pose rest -------------------------------------------------------------------------------
def bake_rest():
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = body; body.select_set(True)
    for m in [m for m in body.modifiers if m.type == "ARMATURE"]:
        bpy.ops.object.modifier_apply(modifier=m.name)
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = arm; arm.select_set(True)
    bpy.ops.object.mode_set(mode="POSE"); bpy.ops.pose.armature_apply(selected=False); bpy.ops.object.mode_set(mode="OBJECT")
    mod = body.modifiers.new("Armature", "ARMATURE"); mod.object = arm
    bpy.context.view_layer.update()


aw = arm.matrix_world
for side, sgn in SIDES.items():
    for b in (f"{side}Arm", f"{side}ForeArm", f"{side}Hand"):
        bpy.context.view_layer.update()
        pb = arm.pose.bones[P + b]
        head_w, tail_w = aw @ pb.head, aw @ pb.tail
        d_local = (pb.tail - pb.head).normalized()
        target_local = (aw.inverted().to_3x3() @ Vector((sgn, 0, 0))).normalized()
        R = d_local.rotation_difference(target_local).to_matrix().to_4x4()
        pb.matrix = Matrix.Translation(pb.head) @ R @ Matrix.Translation(-pb.head) @ pb.matrix
bpy.context.view_layer.update()
bake_rest()
to_world = body.matrix_world.copy()

# --- 3. Cape chains --------------------------------------------------------------------------------
cape_idx = TAG["cape"]
cape_pts = {k: to_world @ me.vertices[k].co for k in cape_idx}
half_w = max(abs(p.x) for p in cape_pts.values())
cols = {"L": 0.62 * half_w, "C": 0.0, "R": -0.62 * half_w}
z_top = max(p.z for p in cape_pts.values()) - 0.005
z_end = min(p.z for p in cape_pts.values()) - 0.015
heights = [z_top - k * (z_top - z_end) / 4 for k in range(5)]


def surface_y(x, z):
    for rx, rz in ((0.03, 0.02), (0.06, 0.045)):
        near = [p.y for p in cape_pts.values() if abs(p.x - x) < rx and abs(p.z - z) < rz]
        if near: return sum(near) / len(near) - 0.006
    return min(cape_pts.values(), key=lambda p: (p.x - x) ** 2 + (p.z - z) ** 2).y - 0.006


aw_inv = arm.matrix_world.inverted()
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="EDIT")
eb = arm.data.edit_bones
for col, x in cols.items():
    prev = eb[P + "Spine2"]
    for k in range(4):
        b = eb.new(f"Cape_{col}{k + 1}")
        b.head = aw_inv @ Vector((x, surface_y(x, heights[k]), heights[k]))
        b.tail = aw_inv @ Vector((x, surface_y(x, heights[k + 1]) if k < 3 else surface_y(x, z_end + 0.015) + 0.008, heights[k + 1]))
        b.align_roll(arm.matrix_world.to_3x3().inverted() @ Vector((0, 1, 0)))
        b.parent = prev; b.use_connect = k > 0; prev = b
bpy.ops.object.mode_set(mode="OBJECT")
for k, p in cape_pts.items():
    s = (z_top - p.z) / (z_top - z_end) * 4
    vert = {"parent": max(0.0, 1 - abs(s + 0.5)) if s > -0.5 else 1.0}
    for b in range(4): vert[b] = max(0.0, 1 - abs(s - (b + 0.5)))
    if s >= 3.5: vert[3] = 1.0
    u = p.x / cols["L"]
    lat = {"C": max(0.0, 1 - abs(u)), "L": min(1.0, max(0.0, u)), "R": min(1.0, max(0.0, -u))}
    w = defaultdict(float)
    pw = vert.pop("parent")
    side = "Left" if p.x > 0 else "Right"
    sh = 0.4 * smooth01((abs(p.x) - 0.045) / 0.07)
    w[P + "Spine2"] += pw * (1 - sh); w[P + side + "Shoulder"] += pw * sh
    for b, vb in vert.items():
        for col, lc in lat.items():
            if vb * lc > 0: w[f"Cape_{col}{b + 1}"] += vb * lc
    set_w(k, dict(sorted(w.items(), key=lambda kv: -kv[1])[:4]))
report["cape"] = {"verts": len(cape_idx), "columns_x": cols, "joint_heights": heights}

over = 0
for v in me.vertices:
    w = get_w(v.index)
    if len(w) > 4:
        over += 1; set_w(v.index, dict(sorted(w.items(), key=lambda kv: -kv[1])[:4]))
report["verts_trimmed_to_4"] = over

# --- 4. HeadTop_End on the bucket top, size ---------------------------------------------------------
helm_idx = TAG["helm"]
bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
seen, best = set(), None
for k in helm_idx:
    if k in seen: continue
    comp, st = [], [k]; seen.add(k)
    while st:
        a = st.pop(); comp.append(a)
        for e in bm.verts[a].link_edges:
            b = e.other_vert(bm.verts[a]).index
            if b not in seen: seen.add(b); st.append(b)
    if len(comp) >= 300:
        zz = [(to_world @ me.vertices[q].co).z for q in comp]
        if best is None or min(zz) < best[0]: best = (min(zz), max(zz))
bm.free()
bucket_top = best[1]
ws = [to_world @ v.co for v in me.vertices]
min_z = min(p.z for p in ws)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="EDIT")
eb = arm.data.edit_bones
head = eb[P + "Head"]
top = eb.get(P + "HeadTop_End") or eb.new(P + "HeadTop_End")
hw = arm.matrix_world @ head.head
top.head = aw_inv @ Vector((hw.x, hw.y, bucket_top))
top.tail = top.head + (head.tail - head.head).normalized() * (head.length * 0.5)
top.parent = head; top.use_connect = False; top.use_deform = False
bpy.ops.object.mode_set(mode="OBJECT")

scale = HEIGHT_RATIO * old_height / (bucket_top - min_z)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="EDIT")
joints = {b.name: (b.head.copy(), b.tail.copy(), b.use_connect) for b in arm.data.edit_bones}
# bones are in armature space; scale about the armature-space point under the world origin so the soles stay on z=0
origin = aw_inv @ Vector((0, 0, 0))
for b in arm.data.edit_bones: b.use_connect = False
for b in arm.data.edit_bones:
    h, t, _ = joints[b.name]; b.head = origin + (h - origin) * scale; b.tail = origin + (t - origin) * scale
for b in arm.data.edit_bones: b.use_connect = joints[b.name][2]
bpy.ops.object.mode_set(mode="OBJECT")
inv = to_world.inverted()
for v in me.vertices:
    wpt = to_world @ v.co
    v.co = inv @ (wpt * scale)
bpy.context.view_layer.update()
ev = body.evaluated_get(bpy.context.evaluated_depsgraph_get())
ws = [ev.matrix_world @ v.co for v in ev.data.vertices]
report.update({"scale": scale, "bucket_top_m": (bucket_top - min_z) * scale,
               "height_with_crest_m": max(p.z for p in ws) - min(p.z for p in ws), "lowest_m": min(p.z for p in ws),
               "hips_height_m": (arm.matrix_world @ arm.data.bones[P + "Hips"].head_local).z})
report["vertex_groups_without_bone"] = [g.name for g in body.vertex_groups if g.name not in arm.data.bones]

# --- 5. Names, texture, export ---------------------------------------------------------------------
arm.name = "Armature"
body.name = body.data.name = f"{name}_Body"
dst = os.path.join(out_dir, "textures", f"{name}_BaseColor.png")
import shutil; shutil.copyfile(tex, dst); img.filepath = dst; img.reload()
report["texture"] = dst; report["texture_size"] = list(img.size)
bpy.ops.object.select_all(action="DESELECT")
arm.select_set(True); body.select_set(True); bpy.context.view_layer.objects.active = arm
fbx = os.path.join(out_dir, f"{name}.fbx")
bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True, object_types={"ARMATURE", "MESH"},
                         add_leaf_bones=False, bake_anim=False, path_mode="STRIP", embed_textures=False,
                         mesh_smooth_type="OFF", use_armature_deform_only=False, armature_nodetype="NULL")
blend = os.path.join(out_dir, f"{name}_rig.blend")
bpy.ops.wm.save_as_mainfile(filepath=blend)
report.update({"fbx": fbx, "blend": blend, "bones": len(arm.data.bones)})
json.dump(report, open(os.path.join(out_dir, f"{name}_report.json"), "w"), indent=1, default=str)
print("REPORT", json.dumps(report, default=str))
