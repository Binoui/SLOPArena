"""Weld + triangulate + smooth + tail chain + 4-influence weights + fur normals, then export.
blender -b --factory-startup --python-exit-code 1 --python tools/blender/manki_rig/fix_rig.py -- <in.fbx> <out.fbx> [bones=5] [smooth_angle_deg=40|0] [fur_normal_iterations=40|0]"""
import bpy, bmesh, sys, os, math, json
import numpy as np
sys.path.insert(0, os.path.dirname(__file__))
from tail_segment import find_tail_geo
from mathutils import Matrix, Vector

argv = sys.argv[sys.argv.index("--") + 1:]
src, dst = argv[:2]
N = int(argv[2]) if len(argv) > 2 else 5
SMOOTH = float(argv[3]) if len(argv) > 3 else 40.0
FUR_ITERATIONS = int(argv[4]) if len(argv) > 4 else 40
P = "mixamorig:"
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
body = max((o for o in bpy.data.objects if o.type == "MESH"), key=lambda o: len(o.data.vertices))
report = {"source": src, "verts_before": len(body.data.vertices)}

# 0. Manki v4's Mixamo FBX binds a centimetre, upside-down mesh to a rest pose lying along -Y; only the clip stands it
#    up. Bake the visible T-pose into the mesh and the rest pose, in metres, so it matches v3's layout (mesh world =
#    identity, upright rest). v3 and Manki 2 already match and skip this.
bpy.context.scene.frame_set(1)
upright = arm.data.bones[P + "Head"].head_local - arm.data.bones[P + "Hips"].head_local
upright = (arm.matrix_world.to_3x3() @ upright).normalized().z > 0.9
if not upright or abs(body.matrix_world.to_scale().x - 1) > 1e-3:
    bpy.context.view_layer.objects.active = body
    mod = next(m for m in body.modifiers if m.type == "ARMATURE")
    keep = body.modifiers.new("Armature.keep", "ARMATURE"); keep.object = arm
    bpy.ops.object.modifier_apply(modifier=mod.name)
    keep.name = "Armature"
    body.data.transform(body.matrix_world); body.matrix_world = Matrix.Identity(4)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="POSE"); bpy.ops.pose.armature_apply(selected=False); bpy.ops.object.mode_set(mode="OBJECT")
    report["baked_pose_as_rest"] = True
if arm.animation_data: arm.animation_data.action = None  # keep node pose, drop the 2-frame clip
for a in list(bpy.data.actions): bpy.data.actions.remove(a)

# 1. weld the triangle soup (coincident verts carry identical weights; UVs are per corner)
bm = bmesh.new(); bm.from_mesh(body.data)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
bmesh.ops.triangulate(bm, faces=bm.faces)  # v4 arrives with quads; fur_normals and Unity want triangles
bm.to_mesh(body.data); body.data.update()
report["verts_welded"] = len(body.data.vertices)
bpy.context.view_layer.objects.active = body
body.select_set(True)
if SMOOTH > 0:
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(SMOOTH))
else:
    bpy.ops.object.shade_flat()

# 2. find the tail on the welded rest mesh
Mi = body.matrix_world.inverted() @ arm.matrix_world
bones = {b.name.replace(P, ""): (Mi @ b.head_local, Mi @ b.tail_local) for b in arm.data.bones}
bm = bmesh.new(); bm.from_mesh(body.data); bm.verts.ensure_lookup_table()
groups = {g.index: g.name.replace(P, "") for g in body.vertex_groups}
tail, g, s_root, tip = find_tail_geo(bm, bm.verts.layers.deform.active, groups, bones)
u = {i: 1 - g[i] / s_root for i in tail}  # 0 at the root, 1 at the tip
detached = all(e.other_vert(bm.verts[i]).index in tail for i in tail for e in bm.verts[i].link_edges)
report.update(tail_verts=len(tail), tail_surface_length=round(s_root, 3), tail_detached=detached)

# 3. centreline from windowed ring centroids (root -> tip), resampled to N equal segments
samples = []
k = 0.0
while k <= 1.0001:
    pts = [bm.verts[i].co for i in tail if abs(u[i] - k) < 0.07]
    if pts: samples.append(sum(pts, Vector()) / len(pts))
    k += 0.04
samples[-1] = tip.co.copy()
sm = [samples[0]] + [(samples[i - 1] + samples[i] * 2 + samples[i + 1]) / 4 for i in range(1, len(samples) - 1)] + [samples[-1]]
seg = [0.0]
for a, b in zip(sm, sm[1:]): seg.append(seg[-1] + (b - a).length)
def at(t):
    L = t * seg[-1]
    for i in range(1, len(seg)):
        if seg[i] >= L:
            f = (L - seg[i - 1]) / max(1e-9, seg[i] - seg[i - 1]); return sm[i - 1].lerp(sm[i], f)
    return sm[-1]
joints = [at(j / N) for j in range(N + 1)]
report["tail_chain_length"] = round(seg[-1], 3)

# 4. tail bones parented to the hips (rest/bind space)
W = body.matrix_world; Ai = arm.matrix_world.inverted()
bpy.ops.object.select_all(action="DESELECT")
bpy.context.view_layer.objects.active = arm; arm.select_set(True)
bpy.ops.object.mode_set(mode="EDIT")
eb = arm.data.edit_bones
prev = eb[P + "Hips"]
names = []
for j in range(N):
    b = eb.new(f"tail:{j + 1}")
    b.head = Ai @ (W @ joints[j]); b.tail = Ai @ (W @ joints[j + 1])
    b.parent = prev; b.use_connect = j > 0; b.use_deform = True
    b.align_roll(Ai.to_3x3() @ Vector((0, 0, 1)))
    prev = b; names.append(b.name)
bpy.ops.object.mode_set(mode="OBJECT")

# 5. tail weights: linear blend between bone mid-points; root ring eases from the original weights
for n in names: body.vertex_groups.new(name=n)
vg = {g.name: g for g in body.vertex_groups}
mids = [(j + 0.5) / N for j in range(N)]
for i in tail:
    t = u[i]
    new = {}
    if t <= mids[0]:
        f = t / mids[0]; new[P + "Hips"] = 1 - f; new[names[0]] = f
    elif t >= mids[-1]:
        new[names[-1]] = 1.0
    else:
        j = int((t - mids[0]) * N); f = (t - mids[j]) * N
        new[names[j]] = 1 - f; new[names[j + 1]] = f
    v = body.data.vertices[i]
    old = {body.vertex_groups[x.group].name: x.weight for x in v.groups}
    # first 8% of the tail keeps some of the body skin for a seamless join; a detached tail (v4) has no join and its
    # generated skin follows a thigh, so it takes the new weights outright
    a = 1.0 if detached else min(1.0, t / 0.08)
    mix = {k_: (1 - a) * old.get(k_, 0) + a * new.get(k_, 0) for k_ in set(old) | set(new)}
    for x in list(v.groups): body.vertex_groups[x.group].remove([i])
    for k_, w in mix.items():
        if w > 1e-4: vg[k_].add([i], w, "REPLACE")

# 6. Unity keeps 4 influences: do it here so Blender previews match the game, then normalise
over = 0
for v in body.data.vertices:
    ws = sorted(((x.group, x.weight) for x in v.groups), key=lambda x: -x[1])
    if len(ws) > 4: over += 1
    keep = ws[:4]; s = sum(w for _, w in keep) or 1
    for gi, _ in ws[4:]: body.vertex_groups[gi].remove([v.index])
    for gi, w in keep: body.vertex_groups[gi].add([v.index], w / s, "REPLACE")
report["trimmed_over4"] = over

# 7. fur tufts get body-like normals so they do not read as dark speckles under the arena key light
if FUR_ITERATIONS > 0:
    from fur_normals import apply as fur_normals
    img = next(n.image for sl in body.material_slots if sl.material and sl.material.use_nodes
               for n in sl.material.node_tree.nodes if n.type == "TEX_IMAGE" and n.image)
    iw, ih = img.size
    px = np.empty(iw * ih * 4, np.float32)
    img.pixels.foreach_get(px)
    report["fur_normals"] = fur_normals(body.data, px.reshape(ih, iw, 4)[::-1, :, :3].astype(np.float64), FUR_ITERATIONS)

# 8. export
bpy.ops.object.select_all(action="SELECT")
bpy.ops.export_scene.fbx(filepath=dst, use_selection=True, object_types={"ARMATURE", "MESH"}, add_leaf_bones=False,
                         bake_anim=False, path_mode="COPY", embed_textures=True, mesh_smooth_type="OFF",
                         use_armature_deform_only=False, armature_nodetype="NULL")
report["joints"] = [[round(x, 3) for x in (W @ p)] for p in joints]
report["output"] = dst
print("REPORT", json.dumps(report))
