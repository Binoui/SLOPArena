"""Unity-ready Sir Bonk rig from a Tripo auto-rig.
blender -b --factory-startup --python-exit-code 1 --python tools/blender/bonk_rig/build_rig.py -- \
    <tripo.fbx> <old bonk.fbx> <out_dir> [name=SirBonk] [arm_length=1.15] [height_ratio=0.9]

1. Deletes the stick across his back (a hallucinated frying-pan handle).
2. Weights: hard pieces (bucket, crest, handle, pauldrons, vambraces, knee guards, spurs, book) move
   rigidly with one bone or a fixed blend; the tabard skirt and sash tail hang from the hips and follow
   the thighs only toward the hem; Neck_Twist_A and the eye bones are folded into the head.
3. Lengthens the upper arms and forearms by arm_length. Skin stretches; vambraces, pauldrons and the
   arm knot keep their size and slide with the limb.
4. Bakes a T-pose rest (upper arm, forearm and hand along +-X); Tripo's rest is an A-pose.
5. Adds three cape chains (Cape_L/C/R 1..4) under the upper chest for BoneChainSpring.
6. Renames bones to Mixamo names (BonkWeaponAttachConfig and old clip paths use them). Bone frames differ
   from the old rig's, so per-bone offsets (the sword attach) are converted in Unity, not here.
7. Scales bones and mesh so the bucket top is height_ratio of the old Bonk's standing height.
Writes <out_dir>/<name>.fbx (no embedded media), <out_dir>/textures/<name>_BaseColor.png,
<out_dir>/<name>_rig.blend and <out_dir>/<name>_report.json."""
import bpy, bmesh, sys, os, json, math
from collections import defaultdict
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:]
src, old_src, out_dir = (os.path.abspath(a) for a in argv[:3])
name = argv[3] if len(argv) > 3 else "SirBonk"
ARM_LENGTH = float(argv[4]) if len(argv) > 4 else 1.15
HEIGHT_RATIO = float(argv[5]) if len(argv) > 5 else 0.9
os.makedirs(os.path.join(out_dir, "textures"), exist_ok=True)
SIDES = ("Left", "Right")
P = "mixamorig:"
report = {"source": src, "old_rig": old_src, "arm_length": ARM_LENGTH, "height_ratio": HEIGHT_RATIO}

FINGERS = ("Thumb", "Index", "Middle", "Ring", "Pinky")
SEGMENTS = ("Proximal", "Intermediate", "Distal", "DistalEnd")
RENAME = {"Hips": "Hips", "Spine": "Spine", "Chest": "Spine1", "UpperChest": "Spine2", "Neck": "Neck", "Head": "Head"}
for s in SIDES:
    RENAME.update({f"{s}_Shoulder": f"{s}Shoulder", f"{s}_UpperArm": f"{s}Arm", f"{s}_LowerArm": f"{s}ForeArm",
                   f"{s}_Hand": f"{s}Hand", f"{s}_UpperLeg": f"{s}UpLeg", f"{s}_LowerLeg": f"{s}Leg",
                   f"{s}_Foot": f"{s}Foot", f"{s}_Toes": f"{s}ToeBase", f"{s}_ToesEnd": f"{s}Toe_End"})
    for f in FINGERS:
        for k, seg in enumerate(SEGMENTS):
            RENAME[f"{s}_{f}{seg}"] = f"{s}Hand{f}{k + 1}"
FOLD_INTO_HEAD = ("Neck_Twist_A", "Left_Eye", "Right_Eye")


def load(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    body = max((o for o in bpy.data.objects if o.type == "MESH"), key=lambda o: len(o.data.vertices))
    bpy.context.view_layer.update()
    return arm, body


def evaluated_world(body):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = body.evaluated_get(dg)
    return [ev.matrix_world @ v.co for v in ev.data.vertices]


def frame(m3):
    return Matrix([m3.col[i].normalized() for i in range(3)]).transposed()


# --- 0. Old Bonk: standing height and Unity bone frames (his Mixamo T-pose take) ---------------------
arm, body = load(old_src)
pts = evaluated_world(body)
old_height = max(p.z for p in pts) - min(p.z for p in pts)
report["old_standing_height"] = old_height

# --- 1. Tripo model ----------------------------------------------------------------------------
arm, body = load(src)
if arm.animation_data:
    arm.animation_data_clear()
me = body.data
to_world = body.matrix_world.copy()


def islands():
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    seen = [False] * len(bm.verts)
    out = []
    for v0 in bm.verts:
        if seen[v0.index]:
            continue
        stack, comp = [v0], []
        seen[v0.index] = True
        while stack:
            v = stack.pop()
            comp.append(v.index)
            for e in v.link_edges:
                w = e.other_vert(v)
                if not seen[w.index]:
                    seen[w.index] = True
                    stack.append(w)
        out.append(comp)
    bm.free()
    out.sort(key=len, reverse=True)
    return out


def world_of(comp):
    return [to_world @ me.vertices[k].co for k in comp]


def centroid(ws):
    return sum(ws, Vector()) / len(ws)


drop, stick_segments = [], []
for comp in islands():
    ws = world_of(comp)
    c = centroid(ws)
    if len(comp) < 1000 and c.y > 0.06 and c.z > 0.55:
        drop.extend(comp)
        # its axis and radius: the generator painted the stick onto the cape behind it (step 8 cleans it)
        import numpy as np
        X = np.array([p[:] for p in ws])
        c0 = X.mean(0)
        axis = np.linalg.svd(X - c0)[2][0]
        t = (X - c0) @ axis
        radial = np.linalg.norm((X - c0) - np.outer(t, axis), axis=1)
        stick_segments.append((c0 + t.min() * axis, c0 + t.max() * axis, float(np.percentile(radial, 90))))
bm = bmesh.new()
bm.from_mesh(me)
bm.verts.ensure_lookup_table()
bmesh.ops.delete(bm, geom=[bm.verts[k] for k in drop], context="VERTS")
bm.to_mesh(me)
bm.free()
report["deleted_back_stick_verts"] = len(drop)
orig_pos = [(to_world @ v.co)[:] for v in me.vertices]  # for the paint cleanup in step 8

# --- 2. Weights --------------------------------------------------------------------------------
groups = {g.name: g for g in body.vertex_groups}


def group(n):
    if n not in groups:
        groups[n] = body.vertex_groups.new(name=n)
    return groups[n]


def get_w(k):
    return {body.vertex_groups[g.group].name: g.weight for g in me.vertices[k].groups if g.weight > 0}


def set_w(k, w):
    for g in list(me.vertices[k].groups):
        body.vertex_groups[g.group].remove([k])
    tot = sum(w.values())
    if tot <= 0:
        return
    for n, x in w.items():
        if x / tot > 1e-4:
            group(n).add([k], x / tot, "REPLACE")


bone_parent = {b.name: (b.parent.name if b.parent else None) for b in arm.data.bones}


def ancestors(n):
    out = []
    while bone_parent.get(n):
        n = bone_parent[n]
        out.append(n)
    return out


def folded(w, main_island):
    out = defaultdict(float)
    for n, x in w.items():
        if n in FOLD_INTO_HEAD:
            if main_island and n == "Neck_Twist_A":
                out["Neck"] += x / 2
                out["Head"] += x / 2
            else:
                out["Head"] += x
        else:
            out[n] += x
    return out


def smooth01(x):
    x = max(0.0, min(1.0, x))
    return x * x * (3 - 2 * x)


comps = islands()
main, legs = comps[0], comps[1]
roles = defaultdict(int)
cape = None
for comp in comps:
    ws = world_of(comp)
    c = centroid(ws)
    if comp is legs:
        roles["legs (Tripo weights)"] += 1
        continue
    if comp is main:
        for k, p in zip(comp, ws):
            w = folded(get_w(k), True)
            if p.z < 0.51:  # tabard skirt below the sash
                t = smooth01((0.49 - p.z) / (0.49 - 0.284))
                share = 0.75 * t
                left = smooth01((p.x + 0.06) / 0.12)
                mine = {"Hips": 1 - share, "Left_UpperLeg": share * left, "Right_UpperLeg": share * (1 - left)}
                blend = smooth01((0.51 - p.z) / 0.04)
                w2 = defaultdict(float)
                for n, x in w.items():
                    w2[n] += x * (1 - blend)
                for n, x in mine.items():
                    w2[n] += x * blend
                w = w2
            set_w(k, w)
        roles["main (skirt re-weighted)"] += 1
        continue
    if len(comp) > 1000 and max(p.y for p in ws) > 0.15 and 0.4 < c.z < 0.7:
        cape = comp
        for k in comp:
            set_w(k, {"UpperChest": 1.0})  # final cape weights come with the cape bones (step 5)
        roles["cape"] += 1
        continue
    acc = defaultdict(float)
    for k in comp:
        for n, x in folded(get_w(k), False).items():
            acc[n] += x
    dom = max(acc, key=acc.get)
    zmin = min(p.z for p in ws)
    if "UpperLeg" in dom and c.y < -0.06 and c.z < 0.6 and abs(c.x) < 0.12 and zmin > 0.3:
        # sash tail hanging in front: hips at the knot, the near thigh toward the end
        side = "Left" if c.x > 0 else "Right"
        top = max(p.z for p in ws)
        for k, p in zip(comp, ws):
            share = 0.5 * smooth01((top - p.z) / (top - zmin))
            set_w(k, {"Hips": 1 - share, f"{side}_UpperLeg": share})
        roles["sash tail"] += 1
        continue
    if dom in ("Head", "Neck"):
        rule = {"Head": 1.0}
    elif dom.endswith(("_Foot", "_Toes", "_ToesEnd")):
        rule = {dom.split("_")[0] + "_Foot": 1.0}
    elif dom.endswith(("_LowerArm", "_Hand")) or any(f in dom for f in FINGERS):
        rule = {dom.split("_")[0] + "_LowerArm": 1.0}
    elif dom.endswith(("_UpperArm", "_Shoulder")):
        side = dom.split("_")[0]
        keep = {n: x for n, x in acc.items() if n in (f"{side}_UpperArm", f"{side}_Shoulder")}
        rule = keep
    elif dom.endswith("_LowerLeg"):
        side = dom.split("_")[0]
        rule = {n: x for n, x in acc.items() if n in (f"{side}_UpperLeg", f"{side}_LowerLeg")}
    elif dom.endswith("_UpperLeg"):
        rule = {"Hips": 0.6, dom: 0.4}  # book and its strap ride the belt, partly the thigh
    else:
        rule = dict(acc)
    for k in comp:
        set_w(k, rule)
    roles["rigid: " + "+".join(sorted(rule))] += 1
report["island_roles"] = dict(roles)

# Tripo smeared thigh weights up the inside of the tabard to the chest and neck (up to 78% on a thigh at 0.2 m above
# the hips): a big stride then pulled a shard out of his chest. Above the hip joints only the legs island (trousers,
# boots) may follow the legs; elsewhere leg weight moves to the spine joint just below the vertex.
legs_set = set(legs)
spine_heads = sorted(((arm.matrix_world @ arm.data.bones[n].head_local).z, n) for n in ("Hips", "Spine", "Chest", "UpperChest"))
hip_line = max((arm.matrix_world @ arm.data.bones[n].head_local).z for n in ("Left_UpperLeg", "Right_UpperLeg")) + 0.035
moved = 0
for v in me.vertices:
    if v.index in legs_set:
        continue
    z = (to_world @ v.co).z
    if z <= hip_line:
        continue
    w = get_w(v.index)
    leg = {n: x for n, x in w.items() if n.endswith(("_UpperLeg", "_LowerLeg", "_Foot", "_Toes", "_ToesEnd"))}
    if not leg:
        continue
    target = [n for hz, n in spine_heads if hz <= z][-1]
    for n in leg:
        w.pop(n)
    w[target] = w.get(target, 0.0) + sum(leg.values())
    set_w(v.index, w)
    moved += 1
report["leg_weight_moved_to_spine_verts"] = moved
for n in FOLD_INTO_HEAD:
    if n in groups:
        body.vertex_groups.remove(groups.pop(n))
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="EDIT")
for n in FOLD_INTO_HEAD:
    if n in arm.data.edit_bones:
        arm.data.edit_bones.remove(arm.data.edit_bones[n])
bpy.ops.object.mode_set(mode="OBJECT")


# --- rest-pose changes: bake the current pose into mesh and bones --------------------------------
def bake_rest(fix=None):
    """Applies the armature modifier, lets `fix(mesh)` adjust baked positions, makes the pose the rest."""
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = body
    body.select_set(True)
    for m in [m for m in body.modifiers if m.type == "ARMATURE"]:
        bpy.ops.object.modifier_apply(modifier=m.name)
    if fix:
        fix()
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="POSE")
    bpy.ops.pose.armature_apply(selected=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    mod = body.modifiers.new("Armature", "ARMATURE")
    mod.object = arm
    bpy.context.view_layer.update()


def bone_world_matrix(n):
    aw = arm.matrix_world
    return aw @ arm.pose.bones[n].matrix @ arm.data.bones[n].matrix_local.inverted() @ aw.inverted()


# --- 3. Longer arms; hard pieces on the arms slide instead of stretching -----------------------------
ARM_BONES = ("Shoulder", "UpperArm", "LowerArm", "Hand") + FINGERS
cand = []
for comp in islands():
    if comp is None or len(comp) > 5000:
        continue
    acc = defaultdict(float)
    for k in comp:
        for n, x in get_w(k).items():
            acc[n] += x
    tot = sum(acc.values()) or 1
    if sum(x for n, x in acc.items() if any(a in n for a in ARM_BONES)) / tot < 0.6:
        continue
    ws = world_of(comp)
    dom = max(acc, key=acc.get)
    seg = "upper" if dom.endswith(("_UpperArm", "_Shoulder")) else "lower"
    mn = Vector([min(p[i] for p in ws) for i in range(3)])
    mx = Vector([max(p[i] for p in ws) for i in range(3)])
    cand.append((comp, mn, mx, seg))
parent = list(range(len(cand)))


def find(i):
    while parent[i] != i:
        parent[i] = parent[parent[i]]
        i = parent[i]
    return i


for i in range(len(cand)):
    for j in range(i + 1, len(cand)):
        a, b = cand[i], cand[j]
        if a[3] == b[3] and all(a[1][q] - 0.005 <= b[2][q] and b[1][q] - 0.005 <= a[2][q] for q in range(3)):
            parent[find(i)] = find(j)
clusters = defaultdict(list)
for i, c in enumerate(cand):
    clusters[find(i)].extend(c[0])
clusters = list(clusters.values())
report["rigid_arm_clusters"] = sorted(len(c) for c in clusters)
rest_world = [to_world @ v.co for v in me.vertices]
for side in SIDES:
    arm.data.bones[f"{side}_LowerArm"].inherit_scale = "ALIGNED"
    arm.data.bones[f"{side}_Hand"].inherit_scale = "ALIGNED"
    arm.pose.bones[f"{side}_UpperArm"].scale = (1, ARM_LENGTH, 1)
    arm.pose.bones[f"{side}_Hand"].scale = (1, 1 / ARM_LENGTH, 1)
bpy.context.view_layer.update()
mats = {}
moves = []
for comp in clusters:
    acc = defaultdict(float)
    for k in comp:
        for n, x in get_w(k).items():
            acc[n] += x
    tot = sum(acc.values())
    c = centroid([rest_world[k] for k in comp])
    moved = Vector()
    for n, x in acc.items():
        if n not in mats:
            mats[n] = bone_world_matrix(n)
        moved += (x / tot) * (mats[n] @ c)
    moves.append((comp, moved - c))


def slide_clusters():
    inv = to_world.inverted()
    for comp, d in moves:
        for k in comp:
            me.vertices[k].co = inv @ (rest_world[k] + d)


bake_rest(slide_clusters)
for side in SIDES:
    arm.data.bones[f"{side}_LowerArm"].inherit_scale = "FULL"
    arm.data.bones[f"{side}_Hand"].inherit_scale = "FULL"
report["upper_arm_length"] = arm.data.bones["Left_UpperArm"].length * arm.scale.x
report["forearm_length"] = arm.data.bones["Left_LowerArm"].length * arm.scale.x

# --- 4. T-pose rest ----------------------------------------------------------------------------
for side, sgn in (("Left", 1), ("Right", -1)):
    for n in (f"{side}_UpperArm", f"{side}_LowerArm", f"{side}_Hand"):
        bpy.context.view_layer.update()
        pb = arm.pose.bones[n]
        d = (pb.tail - pb.head).normalized()
        R = d.rotation_difference(Vector((sgn, 0, 0))).to_matrix().to_4x4()
        pb.matrix = Matrix.Translation(pb.head) @ R @ Matrix.Translation(-pb.head) @ pb.matrix
bpy.context.view_layer.update()
bake_rest()

# --- 5. Cape chains ----------------------------------------------------------------------------
cape_idx = None
for comp in islands():
    ws = world_of(comp)
    c = centroid(ws)
    if len(comp) > 1000 and max(p.y for p in ws) > 0.15 and 0.4 < c.z < 0.7:
        cape_idx = comp
cape_pts = {k: to_world @ me.vertices[k].co for k in cape_idx}
half_w = max(abs(p.x) for p in cape_pts.values())
cols = {"L": 0.62 * half_w, "C": 0.0, "R": -0.62 * half_w}
z_top, z_end = 0.80, min(p.z for p in cape_pts.values()) - 0.02
heights = [z_top - k * (z_top - z_end) / 4 for k in range(5)]


def surface_y(x, z):
    near = [p.y for p in cape_pts.values() if abs(p.x - x) < 0.04 and abs(p.z - z) < 0.03]
    if not near:
        near = [p.y for p in cape_pts.values() if abs(p.x - x) < 0.08 and abs(p.z - z) < 0.06]
    if not near:
        near = [min(cape_pts.values(), key=lambda p: (p.x - x) ** 2 + (p.z - z) ** 2).y]
    return sum(near) / len(near) - 0.008


aw_inv = arm.matrix_world.inverted()
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="EDIT")
eb = arm.data.edit_bones
for col, x in cols.items():
    prev = eb["UpperChest"]
    for k in range(4):
        b = eb.new(f"Cape_{col}{k + 1}")
        b.head = aw_inv @ Vector((x, surface_y(x, heights[k]), heights[k]))
        b.tail = aw_inv @ Vector((x, surface_y(x, heights[k + 1]) if k < 3 else surface_y(x, z_end + 0.02) + 0.01, heights[k + 1]))
        b.align_roll(arm.matrix_world.to_3x3().inverted() @ Vector((0, 1, 0)))
        b.parent = prev
        b.use_connect = k > 0
        prev = b
bpy.ops.object.mode_set(mode="OBJECT")
for k, p in cape_pts.items():
    s = (z_top - p.z) / (z_top - z_end) * 4
    vert = {"parent": max(0.0, 1 - abs(s + 0.5)) if s > -0.5 else 1.0}
    for b in range(4):
        vert[b] = max(0.0, 1 - abs(s - (b + 0.5)))
    if s >= 3.5:
        vert[3] = 1.0
    u = p.x / cols["L"]
    lat = {"C": max(0.0, 1 - abs(u)), "L": min(1.0, max(0.0, u)), "R": min(1.0, max(0.0, -u))}
    w = defaultdict(float)
    pw = vert.pop("parent")
    side = "Left" if p.x > 0 else "Right"
    sh = 0.4 * smooth01((abs(p.x) - 0.06) / 0.1)
    w["UpperChest"] += pw * (1 - sh)
    w[f"{side}_Shoulder"] += pw * sh
    for b, vb in vert.items():
        for col, lc in lat.items():
            if vb * lc > 0:
                w[f"Cape_{col}{b + 1}"] += vb * lc
    top4 = dict(sorted(w.items(), key=lambda kv: -kv[1])[:4])
    set_w(k, top4)
report["cape"] = {"verts": len(cape_idx), "columns_x": cols, "joint_heights": heights}

# --- limit to 4 influences everywhere ------------------------------------------------------------
over = 0
for v in me.vertices:
    w = get_w(v.index)
    if len(w) > 4:
        over += 1
        set_w(v.index, dict(sorted(w.items(), key=lambda kv: -kv[1])[:4]))
report["verts_trimmed_to_4"] = over

# --- 6. Mixamo names, HeadTop_End ------------------------------------------------------------------
for n in list(arm.data.bones.keys()):
    if n in RENAME:
        arm.data.bones[n].name = P + RENAME[n]
# bucket top: highest point outside the crest (islands that start above 0.9 of the normalised height)
crest = set()
for comp in islands():
    if min((to_world @ me.vertices[k].co).z for k in comp) > 0.9:
        crest.update(comp)
bucket_top = max((to_world @ v.co).z for v in me.vertices if v.index not in crest)
report["crest_verts"] = len(crest)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="EDIT")
eb = arm.data.edit_bones
head = eb[P + "Head"]
top = eb.new(P + "HeadTop_End")
top.head = head.tail.copy()
top_w = arm.matrix_world @ head.head
top.head = aw_inv @ Vector((top_w.x, top_w.y, bucket_top))
top.tail = top.head + (head.tail - head.head).normalized() * (head.length * 0.5)
top.parent = head
top.use_deform = False
bpy.ops.object.mode_set(mode="OBJECT")
missing_groups = [g.name for g in body.vertex_groups if g.name not in arm.data.bones]
report["vertex_groups_without_bone"] = missing_groups

# --- 7. Size: bucket top = height_ratio x old Bonk -------------------------------------------------
ws = [to_world @ v.co for v in me.vertices]
min_z = min(p.z for p in ws)
crest_free_top = bucket_top - min_z
scale = HEIGHT_RATIO * old_height / crest_free_top
# Baked into bones and vertices (not the object scale): Unity bones keep unit scale like the old rig, so
# per-bone offsets such as the sword's PositionOffset (bone.TransformPoint) keep their length.
# Connected bones share a joint (setting a child's head moves its parent's tail), so release every
# connection, scale from the recorded joints, then restore the flags.
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="EDIT")
joints = {b.name: (b.head.copy(), b.tail.copy(), b.use_connect) for b in arm.data.edit_bones}
for b in arm.data.edit_bones:
    b.use_connect = False
for b in arm.data.edit_bones:
    head, tail, _ = joints[b.name]
    b.head = head * scale
    b.tail = tail * scale
for b in arm.data.edit_bones:
    b.use_connect = joints[b.name][2]
bpy.ops.object.mode_set(mode="OBJECT")
for v in me.vertices:
    v.co = v.co * scale
bpy.context.view_layer.update()
ws = evaluated_world(body)
report.update({"scale": scale, "bucket_top_m": crest_free_top * scale,
               "height_with_crest_m": max(p.z for p in ws) - min(p.z for p in ws),
               "hips_height_m": (arm.matrix_world @ arm.data.bones[P + "Hips"].head_local).z - min(p.z for p in ws)})

# --- 8. Names, texture, export ---------------------------------------------------------------------
def clean_stick_paint(img):
    """Repaints the stick's image off his back: the generator painted the stick onto the tabard back and
    the cape behind it. Dark texels behind the removed stick (seen from the back) take the colour of clean
    texels beside it (offset across the stick), so the cloth texture continues."""
    import numpy as np
    from mathutils.kdtree import KDTree
    W, H = img.size
    px = np.array(img.pixels[:], dtype=np.float32).reshape(H, W, 4)
    uvl = me.uv_layers.active.data
    cloth = set(cape_idx) | set(main)
    vco = orig_pos
    xy_parts, pos_parts = [], []
    for poly in me.polygons:
        if poly.vertices[0] not in cloth or sum(vco[k][1] for k in poly.vertices) / len(poly.vertices) < 0.03:
            continue
        loops = list(poly.loop_indices)
        for i in range(1, len(loops) - 1):
            tri = (loops[0], loops[i], loops[i + 1])
            uv = np.array([uvl[l].uv[:] for l in tri]) * (W, H)
            P3 = np.array([vco[me.loops[l].vertex_index] for l in tri])
            (x0, y0), (x1, y1) = np.floor(uv.min(0)).astype(int), np.ceil(uv.max(0)).astype(int)
            xs, ys = np.meshgrid(np.arange(x0, x1 + 1), np.arange(y0, y1 + 1))
            pts = np.stack([xs.ravel() + 0.5, ys.ravel() + 0.5], 1)
            a, b, c = uv
            v0, v1 = b - a, c - a
            d = v0[0] * v1[1] - v1[0] * v0[1]
            if abs(d) < 1e-12:
                continue
            vp = pts - a
            l1 = (vp[:, 0] * v1[1] - v1[0] * vp[:, 1]) / d
            l2 = (v0[0] * vp[:, 1] - vp[:, 0] * v0[1]) / d
            l0 = 1 - l1 - l2
            inside = (l0 >= -0.03) & (l1 >= -0.03) & (l2 >= -0.03)
            if inside.any():
                xy_parts.append(np.stack([xs.ravel()[inside], ys.ravel()[inside]], 1))
                pos_parts.append(np.stack([l0, l1, l2], 1)[inside] @ P3)
    xy = np.concatenate(xy_parts)
    pos = np.concatenate(pos_parts)
    xy = np.clip(xy, 0, (W - 1, H - 1))
    _, first = np.unique(xy[:, 1] * W + xy[:, 0], return_index=True)
    xy, pos = xy[first], pos[first]
    col = px[xy[:, 1], xy[:, 0], :3]
    near = np.zeros(len(pos), bool)
    far = np.ones(len(pos), bool)
    perp = []
    flat = pos * (1, 0, 1)  # the paint is the stick seen from behind: measure across the back (x, z)
    for a, b, r in stick_segments:
        a, b = a * (1, 0, 1), b * (1, 0, 1)
        ab = b - a
        u = np.clip(((flat - a) @ ab) / (ab @ ab), 0, 1)
        dist = np.linalg.norm(flat - (a + np.outer(u, ab)), axis=1)
        near |= dist < r + 0.016
        far &= dist > r + 0.05
        n = np.cross(ab, (0, 1, 0))
        perp.append((n / np.linalg.norm(n), r))
    cream = np.median(col[far], 0)
    blue = col[:, 2] >= col[:, 0] - 0.01  # the painted crown and its dark navy edges stay
    dirty = near & ~blue & (np.linalg.norm(col - cream, axis=1) > 0.07)
    clean = ~near
    tree = KDTree(int(clean.sum()))
    clean_idx = np.nonzero(clean)[0]
    for i, k in enumerate(clean_idx):
        tree.insert(pos[k], i)
    tree.balance()
    for k in np.nonzero(dirty)[0]:
        best = None
        for n, r in perp:
            for sgn in (1, -1):
                q = pos[k] + sgn * n * (r + 0.035)
                found = tree.find_n(q, 4)
                if found and (best is None or found[0][2] < best[0][2]):
                    best = found
        src = np.mean([col[clean_idx[f[1]]] for f in best], 0)
        px[xy[k, 1], xy[k, 0], :3] = src
    img.pixels[:] = px.ravel()
    return {"back_texels": int(len(pos)), "repainted": int(dirty.sum()), "cream": [round(float(v), 3) for v in cream]}



arm.name = "Armature"
body.name = f"{name}_Body"
body.data.name = f"{name}_Body"
# Tripo's material holds the colour map twice (two image nodes, two datablocks on one file): keep one.
base = None
for slot in body.material_slots:
    if not slot.material:
        continue
    mat = slot.material
    mat.name = f"{name}_Body"
    for n in mat.node_tree.nodes if mat.use_nodes else []:
        if n.type == "TEX_IMAGE" and n.image:
            base = base or n.image
            n.image = base
if base is not None:
    if stick_segments:
        report["stick_paint"] = clean_stick_paint(base)
    dst = os.path.join(out_dir, "textures", f"{name}_BaseColor.png")
    if base.packed_file:
        base.unpack(method="USE_ORIGINAL")
    base.filepath_raw = dst
    base.file_format = "PNG"
    base.save()
    base.filepath = dst
    report["texture"] = dst
    report["texture_size"] = list(base.size)
bpy.ops.object.select_all(action="DESELECT")
arm.select_set(True)
body.select_set(True)
bpy.context.view_layer.objects.active = arm
fbx = os.path.join(out_dir, f"{name}.fbx")
bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True, object_types={"ARMATURE", "MESH"},
                         add_leaf_bones=False, bake_anim=False, path_mode="STRIP", embed_textures=False,
                         mesh_smooth_type="OFF", use_armature_deform_only=False, armature_nodetype="NULL")
blend = os.path.join(out_dir, f"{name}_rig.blend")
bpy.ops.wm.save_as_mainfile(filepath=blend)
report.update({"fbx": fbx, "blend": blend, "bones": len(arm.data.bones), "verts": len(me.vertices)})
json.dump(report, open(os.path.join(out_dir, f"{name}_report.json"), "w"), indent=1, default=str)
print("REPORT", json.dumps(report, default=str))
