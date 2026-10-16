"""Unity-ready FightGuy rig from a Mixamo auto-rig with index-only fingers.
blender -b --factory-startup --python tools/blender/fightguy_rig/build_rig.py -- \
    <mixamo.fbx> <old_rig.fbx> <out_dir> [name=FightGuy2]

1. Bakes the Mixamo T-pose take into the rest/bind pose (Unity Humanoid expects a T-pose rest).
2. Adds mixamorig:<Side>HandThumb1..4. Joints follow the old rig's thumb layout, expressed in the
   hand bone's frame and scaled by hand length, then snapped to the actual thumb geometry.
3. Moves the thumb skin from the index chain (where the auto-rig put it) to the new chain, blending
   into the hand around the thumb's base.
4. Scales the armature object so the T-pose hip height matches the old rig. Different
   proportions still change Unity humanScale; verify retargeted poses and foot alignment in Unity.
Writes <out_dir>/<name>.fbx (no embedded media), <out_dir>/<name>_paint.npz (geometry for
paint_texture.py), <out_dir>/textures/<name>_BaseColor_src.png and <out_dir>/<name>_report.json."""
import bpy, sys, os, json, heapq
import numpy as np
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:]
src, old_src, out_dir = (os.path.abspath(a) for a in argv[:3])
name = argv[3] if len(argv) > 3 else "FightGuy2"
os.makedirs(os.path.join(out_dir, "textures"), exist_ok=True)
P = "mixamorig:"
SIDES = ("Left", "Right")
report = {"source": src, "old_rig": old_src}


def load(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    body = max((o for o in bpy.data.objects if o.type == "MESH"), key=lambda o: len(o.data.vertices))
    bpy.context.view_layer.update()
    return arm, body


def evaluated_world_verts(body):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = body.evaluated_get(dg)
    me = ev.to_mesh()
    mw = body.matrix_world
    pts = np.array([tuple(mw @ v.co) for v in me.vertices])
    ev.to_mesh_clear()
    return pts


def frame3(m3):
    """Orthonormal 3x3 from a (possibly scaled) bone/world matrix."""
    c = [m3.col[i].normalized() for i in range(3)]
    return Matrix((c[0], c[1], c[2])).transposed()


# --- 1. Old rig reference (posed T-pose): thumb layout in hand frame, hip height ------------------
arm, body = load(old_src)
mw3 = arm.matrix_world.to_3x3()
old_min_z = evaluated_world_verts(body)[:, 2].min()
pb = arm.pose.bones
old_hips = (arm.matrix_world @ pb[P + "Hips"].head).z - old_min_z
old = {}
for side in SIDES:
    # World-relative layout: both rigs' T-pose hands point along +-X with palms down, while their hand
    # bone rolls differ, so world offsets (scaled by hand length) transfer better than hand-frame ones.
    h = arm.matrix_world @ pb[P + side + "Hand"].head
    L = ((arm.matrix_world @ pb[P + side + "HandIndex1"].head) - h).length
    joints, zaxes = [], []
    for k in range(1, 5):
        b = pb[P + side + "HandThumb%d" % k]
        joints.append(((arm.matrix_world @ b.head) - h) / L)
        zaxes.append(frame3(mw3 @ b.matrix.to_3x3()).col[2])
    old[side] = {"joints_rel": joints, "z_world": zaxes}
report["old_hips_height"] = old_hips

# --- 2. New rig: bake the T-pose take into the rest pose -----------------------------------------
arm, body = load(src)
posed_ref = evaluated_world_verts(body)
bpy.ops.object.select_all(action="DESELECT")
bpy.context.view_layer.objects.active = body
body.select_set(True)
for m in [m for m in body.modifiers if m.type == "ARMATURE"]:
    bpy.ops.object.modifier_apply(modifier=m.name)
bpy.ops.object.select_all(action="DESELECT")
bpy.context.view_layer.objects.active = arm
arm.select_set(True)
bpy.ops.object.mode_set(mode="POSE")
bpy.ops.pose.select_all(action="SELECT")
bpy.ops.pose.armature_apply(selected=False)
bpy.ops.object.mode_set(mode="OBJECT")
if arm.animation_data:
    arm.animation_data.action = None
for a in list(bpy.data.actions):
    bpy.data.actions.remove(a)
mod = body.modifiers.new("Armature", "ARMATURE")
mod.object = arm
# Faceted look: flat faces, no imported custom normals (the mesh is a triangle soup anyway).
bpy.context.view_layer.objects.active = body
try:
    bpy.ops.mesh.customdata_custom_splitnormals_clear()
except RuntimeError:
    pass
for poly in body.data.polygons:
    poly.use_smooth = False
bpy.context.view_layer.update()
rest_world = evaluated_world_verts(body)
report["bake_max_error_m"] = float(np.abs(rest_world - posed_ref).max())

mesh = body.data
mw = body.matrix_world
X_all = np.array([tuple(mw @ v.co) for v in mesh.vertices])
tris = np.array([tuple(p.vertices) for p in mesh.polygons])
keys = np.round(X_all / 1e-5).astype(np.int64)
_, wid = np.unique(keys, axis=0, return_inverse=True)
wid = wid.ravel()
nw = wid.max() + 1
X = np.zeros((nw, 3))
X[wid] = X_all
adj = [set() for _ in range(nw)]
for t in tris:
    a, b, c = wid[t]
    adj[a].update((b, c)); adj[b].update((a, c)); adj[c].update((a, b))


def geodesic(seed, limit):
    dist = {seed: 0.0}
    pq = [(0.0, seed)]
    while pq:
        d, u = heapq.heappop(pq)
        if d > dist.get(u, 1e9) or d > limit:
            continue
        for v in adj[u]:
            nd = d + float(np.linalg.norm(X[u] - X[v]))
            if nd < dist.get(v, 1e9):
                dist[v] = nd
                heapq.heappush(pq, (nd, v))
    D = np.full(nw, np.inf)
    for k, v in dist.items():
        D[k] = v
    return D


def closest_on_chain(pts, J):
    """Chain parameter t (0 at J[0], 1 at J[1], ...) and distance to the polyline."""
    best_t = np.zeros(len(pts)); best_r = np.full(len(pts), np.inf)
    for i in range(len(J) - 1):
        a, b = J[i], J[i + 1]
        ab = b - a
        u = np.clip(((pts - a) @ ab) / (ab @ ab), 0, 1)
        r = np.linalg.norm(pts - (a + np.outer(u, ab)), axis=1)
        better = r < best_r
        best_r[better] = r[better]
        best_t[better] = i + u[better]
    return best_t, best_r


def smooth01(x):
    x = np.clip(x, 0, 1)
    return x * x * (3 - 2 * x)


# --- 3. Thumb chains ---------------------------------------------------------------------------
amw = arm.matrix_world
amw_inv = amw.inverted()
amw3 = amw.to_3x3()
amw3_inv = amw3.inverted()
chains = {}
for side in SIDES:
    hb = arm.data.bones[P + side + "Hand"]
    h = np.array(amw @ hb.head_local)
    i1 = np.array(amw @ arm.data.bones[P + side + "HandIndex1"].head_local)
    L = float(np.linalg.norm(i1 - h))
    pred = [np.array(j * L) + h for j in old[side]["joints_rel"]]
    zax = list(old[side]["z_world"])
    sgn = np.sign(h[0])
    region = np.where(X[:, 0] * sgn > abs(h[0]) - 0.01)[0]
    tip = region[np.argmin(np.linalg.norm(X[region] - pred[3], axis=1))]
    D = geodesic(tip, 0.14)
    # Free thumb = geodesically close to the tip and near the predicted chain.
    free = np.where(D < 0.075)[0]
    axis = pred[3] - pred[1]
    axis /= np.linalg.norm(axis)
    proj = (X[free] - pred[1]) @ axis
    J = [p.copy() for p in pred]
    for k in (1, 2):  # MCP, IP joints: centroid of the thumb slab around the predicted joint
        pk = (pred[k] - pred[1]) @ axis
        slab = free[np.abs(proj - pk) < 0.005]
        if len(slab) >= 6:
            c = X[slab].mean(0)
            J[k] = c
    # Tip joint: centre of the last 5 mm of the thumb along its axis.
    pmax = proj.max()
    J[3] = X[free[proj > pmax - 0.005]].mean(0)
    # CMC keeps the old offset from the MCP joint (it sits inside the palm, no geometry to snap to).
    J[0] = J[1] + (pred[0] - pred[1])
    chains[side] = {"joints": J, "z": zax, "tip_index": int(tip), "D": D, "hand_len": L,
                    "pred": pred}
    report.setdefault("thumb_joints", {})[side] = [list(np.round(j, 4)) for j in J]
    report.setdefault("thumb_snap_shift_m", {})[side] = [float(np.linalg.norm(J[k] - pred[k])) for k in range(4)]

bpy.ops.object.select_all(action="DESELECT")
bpy.context.view_layer.objects.active = arm
arm.select_set(True)
bpy.ops.object.mode_set(mode="EDIT")
eb = arm.data.edit_bones
for side in SIDES:
    J, Z = chains[side]["joints"], chains[side]["z"]
    parent = eb[P + side + "Hand"]
    tail_end = J[3] + (J[3] - J[2])  # leaf: same direction, same length as the distal segment
    for k in range(4):
        b = eb.new(P + side + "HandThumb%d" % (k + 1))
        b.head = amw_inv @ Vector(J[k])
        b.tail = amw_inv @ Vector(J[k + 1] if k < 3 else tail_end)
        b.align_roll(amw3_inv @ Z[k])
        b.parent = parent
        b.use_connect = False
        b.use_deform = k < 3
        parent = b
bpy.ops.object.mode_set(mode="OBJECT")

# --- 4. Thumb weights ---------------------------------------------------------------------------
groups = {g.name: g for g in body.vertex_groups}
for side in SIDES:
    for k in range(1, 4):
        n = P + side + "HandThumb%d" % k
        if n not in groups:
            groups[n] = body.vertex_groups.new(name=n)
names = [g.name for g in body.vertex_groups]
Wold = np.zeros((len(mesh.vertices), len(names)), dtype=np.float64)
for v in mesh.vertices:
    for g in v.groups:
        Wold[v.index, g.group] = g.weight
Wnew = Wold.copy()
weight_report = {}
for side in SIDES:
    J = np.array(chains[side]["joints"])
    D = chains[side]["D"][wid]                       # per mesh vertex
    t, r = closest_on_chain(X_all, J)
    # membership: whole free thumb, fading into the palm over 2.5 cm of surface; only near the chain
    m = smooth01((0.085 - D) / 0.025) * smooth01((0.024 - r) / 0.008)
    m[~np.isfinite(D)] = 0.0
    # chain weights along t with blends around the MCP (t=1) and IP (t=2) joints
    w1 = 1 - smooth01((t - 0.8) / 0.4)
    w3 = smooth01((t - 1.8) / 0.4)
    w2 = 1 - w1 - w3
    cols = [names.index(P + side + "HandThumb%d" % k) for k in (1, 2, 3)]
    sel = m > 0
    Wnew[sel] *= (1 - m[sel])[:, None]
    for c, wk in zip(cols, (w1, w2, w3)):
        Wnew[sel, c] += m[sel] * wk[sel]
    idx_cols = [names.index(P + side + "HandIndex%d" % k) for k in (1, 2, 3)]
    full = m > 0.99
    weight_report[side] = {"thumb_verts": int(sel.sum()), "full_thumb_verts": int(full.sum()),
                           "index_weight_left_on_full_thumb": float(Wnew[full][:, idx_cols].sum(1).max() if full.any() else 0)}
# keep 4 strongest influences, normalize
for i in range(len(Wnew)):
    row = Wnew[i]
    nz = np.nonzero(row > 1e-4)[0]
    if len(nz) > 4:
        keep = nz[np.argsort(row[nz])[-4:]]
        drop = np.setdiff1d(nz, keep)
        row[drop] = 0
    s = row.sum()
    if s > 0:
        row /= s
changed = np.where(np.abs(Wnew - Wold).max(1) > 1e-6)[0]
for i in changed:
    for gi, g in enumerate(body.vertex_groups):
        if Wnew[i, gi] > 1e-4:
            g.add([int(i)], float(Wnew[i, gi]), "REPLACE")
        elif Wold[i, gi] > 0:
            g.remove([int(i)])
weight_report["changed_verts"] = int(len(changed))
report["weights"] = weight_report

# Bone positions in the same (unscaled T-pose) space as the paint geometry.
bone_world = {b.name: (list(amw @ b.head_local), list(amw @ b.tail_local)) for b in arm.data.bones}

# --- 5. Scale to the old rig's hip height --------------------------------------------------------
min_z = X_all[:, 2].min()
new_hips = (amw @ arm.data.bones[P + "Hips"].head_local).z - min_z
scale = old_hips / new_hips
arm.scale = arm.scale * scale
bpy.context.view_layer.update()
report.update({"new_hips_height_before": new_hips, "scale": scale,
               "height_after_m": float((X_all[:, 2].max() - min_z) * scale)})

# --- 6. Names, texture, paint geometry, export ---------------------------------------------------
arm.name = "Armature"
body.name = f"{name}_Body"
body.data.name = f"{name}_Body"
for slot in body.material_slots:
    if not slot.material:
        continue
    mat = slot.material
    mat.name = f"{name}_Body"
    for n in mat.node_tree.nodes if mat.use_nodes else []:
        if n.type == "TEX_IMAGE" and n.image:
            img = n.image
            dst = os.path.join(out_dir, "textures", f"{name}_BaseColor_src.png")
            if img.packed_file:
                open(dst, "wb").write(img.packed_file.data)
                img.unpack(method="REMOVE")
            else:
                img.filepath_raw = dst
                img.save()
            img.filepath = dst
            report["texture_src"] = dst
            report["texture_size"] = list(img.size)

uv = mesh.uv_layers.active.data
loop_uv = np.array([tuple(uv[i].uv) for i in range(len(mesh.loops))])
tri_loops = np.array([tuple(p.loop_indices) for p in mesh.polygons])
np.savez(os.path.join(out_dir, f"{name}_paint.npz"), X=X_all, tris=tris, tri_uv=loop_uv[tri_loops],
         W=Wnew, names=np.array(names))
json.dump(bone_world, open(os.path.join(out_dir, f"{name}_bones_world_unscaled.json"), "w"), indent=1)

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
report.update({"fbx": fbx, "blend": blend, "bones": len(arm.data.bones), "verts": len(mesh.vertices),
               "tris": len(tris)})
json.dump(report, open(os.path.join(out_dir, f"{name}_report.json"), "w"), indent=1, default=float)
print("REPORT", json.dumps(report, default=float))
