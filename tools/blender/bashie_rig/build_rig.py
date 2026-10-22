"""Unity-ready Bashie from her Mixamo rig, with her shell, whistle and cord as separate pieces on bones.
blender -b --factory-startup --python-exit-code 1 --python tools/blender/bashie_rig/build_rig.py -- \
    <mixamo.fbx> <out_dir> [shell=<shell.fbx>] [whistle=<whistle.fbx>] [key=value ...]

1. Deletes flat backdrop cards: islands of at most 8 faces, flatter than 0.1 mm, bigger than 20% of the
   model's tallest side squared (a generator turns an opaque image background into such a quad).
2. Bakes the Mixamo take into the rest pose. Blender reads Mixamo's bone rest as the upload pose; only the
   take's pose (a T-pose) lines the bones up with the mesh. Then deletes the small tail the generator gave her
   (tail=keep keeps it).
3. Limits every vertex to 4 influences (Unity's default) and normalises.
4. Adds the separate pieces, each with its own material, skinned rigidly to bones of their own, so they never bend
   and the GenRA rig pack sees them like her body:
   - shell: the Tripo shell (standing 1.0 tall, opening toward -Y), scaled and placed by shell_fit, on a new bone
     Shell under shell_bone.
   - whistle: the Tripo whistle, its ring at the top of her chest, on a new bone Whistle under Spine2.
   - cord: a red tube from the whistle's ring up and around her neck, on Spine2, handing over to Whistle near the
     ring so the strands follow the whistle when a move lifts it.
Writes <out_dir>/<name>.fbx (no embedded media), <out_dir>/textures/<material>.png (one per material),
<out_dir>/<name>_rig.blend and <out_dir>/<name>_report.json.

Options (lengths are in units of her rest height):
  name=Bashie
  shell_fit=0.38,1.1,0.75,0.25,0.11,-4  height, width factor, depth factor, bottom, back offset, pitch (degrees,
                                        negative tips the top backward). Chosen 2026-10-22: it hugs her back with
                                        its rim at her sides; a shell set further back clipped her arms less but
                                        read as a backpack.
  shell_bone=Spine1
  whistle_size=0.09  longest side      whistle_z=0.49  height of its ring      whistle_tilt=20  mouthpiece down
  whistle_hang=0.4   0 keeps the model upright (barrel in front of its ring), 1 hangs the barrel straight under
                     the ring; either way the barrel swings out until it clears her chest. Fully hung, her belly
                     pushed the chunky barrel out flat, which looked worse.
  cord_r=0.007       tube radius       cord_rgb=0.86,0.2,0.18
"""
import bpy, bmesh, sys, os, json, math
import numpy as np
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index("--") + 1:]
src, out_dir = (os.path.abspath(a) for a in argv[:2])
opt = dict(a.split("=", 1) for a in argv[2:])
name = opt.get("name", "Bashie")
SHELL_FIT = [float(v) for v in opt.get("shell_fit", "0.38,1.1,0.75,0.25,0.11,-4").split(",")]
SHELL_BONE = opt.get("shell_bone", "Spine1")
WHISTLE_SIZE = float(opt.get("whistle_size", 0.09))
WHISTLE_Z = float(opt.get("whistle_z", 0.49))
WHISTLE_TILT = float(opt.get("whistle_tilt", 20))
WHISTLE_HANG = float(opt.get("whistle_hang", 0.4))
CORD_R = float(opt.get("cord_r", 0.007))
CORD_RGB = [float(v) for v in opt.get("cord_rgb", "0.86,0.2,0.18").split(",")]
TEX_SIZE = {"Body": 4096, "Shell": 2048, "Whistle": 1024}
os.makedirs(os.path.join(out_dir, "textures"), exist_ok=True)
P = "mixamorig:"
report = {"source": src, "options": opt}

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
body = max((o for o in bpy.data.objects if o.type == "MESH"), key=lambda o: len(o.data.vertices))
me = body.data


def islands(mesh):
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.faces.ensure_lookup_table()
    seen, out = set(), []
    for f in bm.faces:
        if f.index in seen:
            continue
        faces, verts, stack = [], set(), [f]
        seen.add(f.index)
        while stack:
            g = stack.pop()
            faces.append(g.index)
            verts.update(v.index for v in g.verts)
            for e in g.edges:
                for h in e.link_faces:
                    if h.index not in seen:
                        seen.add(h.index)
                        stack.append(h)
        out.append((faces, verts))
    bm.free()
    return out


def world():
    dg = bpy.context.evaluated_depsgraph_get()
    ev = body.evaluated_get(dg)
    return [ev.matrix_world @ v.co for v in ev.data.vertices]


# --- 1. Backdrop cards -------------------------------------------------------------------------------
pts = world()
size = max(max(p[i] for p in pts) - min(p[i] for p in pts) for i in range(3))
cards = []
for faces, verts in islands(me):
    ext = [max(pts[v][i] for v in verts) - min(pts[v][i] for v in verts) for i in range(3)]
    srt = sorted(ext)
    if len(faces) <= 8 and srt[0] < 1e-4 and srt[1] * srt[2] > 0.2 * size * size:
        cards.append(verts)
if cards:
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    doomed = {i for c in cards for i in c}
    bmesh.ops.delete(bm, geom=[bm.verts[i] for i in doomed], context="VERTS")
    bm.to_mesh(me)
    bm.free()
    me.update()
report["cards_deleted"] = len(cards)

# --- 2. Mixamo take -> rest pose -------------------------------------------------------------------
if arm.animation_data and arm.animation_data.action:
    act = arm.animation_data.action
    bpy.context.scene.frame_set(int(act.frame_range[0]))
    bpy.context.view_layer.update()
    base = {pb.name: pb.matrix_basis.copy() for pb in arm.pose.bones}
    arm.animation_data_clear()
    for pb in arm.pose.bones:
        pb.matrix_basis = base[pb.name]
    bpy.context.view_layer.update()
    report["take"] = act.name
bpy.ops.object.select_all(action="DESELECT")
bpy.context.view_layer.objects.active = body
body.select_set(True)
for m in [m for m in body.modifiers if m.type == "ARMATURE"]:
    bpy.ops.object.modifier_apply(modifier=m.name)
bpy.ops.object.select_all(action="DESELECT")
bpy.context.view_layer.objects.active = arm
arm.select_set(True)
bpy.ops.object.mode_set(mode="POSE")
bpy.ops.pose.armature_apply(selected=False)
bpy.ops.object.mode_set(mode="OBJECT")
mod = body.modifiers.new("Armature", "ARMATURE")
mod.object = arm
bpy.context.view_layer.update()
for side in ("Left", "Right"):
    b = arm.data.bones[P + side + "Arm"]
    d = (arm.matrix_world.to_3x3() @ (b.tail_local - b.head_local)).normalized()
    report[f"{side.lower()}_arm_rest_z"] = round(d.z, 4)

# --- 2b. Tail ----------------------------------------------------------------------------------------
# The generator gave her a small tail: its own island, behind her hips. The owner had it removed (2026-10-22).
if opt.get("tail", "remove") == "remove":
    pts = world()
    zs = [p.z for p in pts]
    h = max(zs) - min(zs)
    hips = arm.matrix_world @ arm.data.bones[P + "Hips"].head_local
    tails = []
    for faces, verts in islands(me):
        c = sum((pts[i] for i in verts), Vector()) / len(verts)
        if (len(verts) < 0.03 * len(pts) and c.y > hips.y + 0.08 * h and abs(c.x) < 0.05 * h
                and abs(c.z - hips.z) < 0.12 * h):
            tails.append(verts)
    if tails:
        bm = bmesh.new()
        bm.from_mesh(me)
        bm.verts.ensure_lookup_table()
        bmesh.ops.delete(bm, geom=[bm.verts[i] for t in tails for i in t], context="VERTS")
        bm.to_mesh(me)
        bm.free()
        me.update()
        # The skin under the tail is painted red (it was never meant to show) and the shell's rim can bare it when
        # she bends. Point those faces' UVs at the green skin nearest to them.
        node = max((n for n in body.data.materials[0].node_tree.nodes if n.type == "TEX_IMAGE" and n.image),
                   key=lambda n: n.image.size[0] * n.image.size[1])
        tw, th = node.image.size
        texels = np.empty(tw * th * 4, dtype=np.float32)
        node.image.pixels.foreach_get(texels)
        texels = texels.reshape(th, tw, 4)
        uv = me.uv_layers.active.data
        pts = world()

        def red(u, v):
            r, g, b = texels[min(th - 1, max(0, int(v * th))), min(tw - 1, max(0, int(u * tw))), :3]
            return r > 0.3 and r > 2 * g and r > 2 * b

        near_hips = [f for f in me.polygons
                     if (lambda c: c.y > hips.y + 0.03 * h and abs(c.x) < 0.1 * h and abs(c.z - hips.z) < 0.12 * h)(
                         sum((pts[i] for i in f.vertices), Vector()) / len(f.vertices))]
        patch, skin = [], []
        for f in near_hips:
            uvs = [uv[li].uv for li in f.loop_indices]
            mid = sum(uvs, Vector((0, 0))) / len(uvs)
            (patch if any(red(*q) for q in uvs + [mid]) else skin).append(f)
        if patch and skin:
            centre = sum((sum((pts[i] for i in f.vertices), Vector()) / len(f.vertices) for f in patch), Vector()) / len(patch)
            donor = min(skin, key=lambda f: (sum((pts[i] for i in f.vertices), Vector()) / len(f.vertices) - centre).length)
            green = sum((uv[li].uv for li in donor.loop_indices), Vector((0, 0))) / len(donor.loop_indices)
            for f in patch:
                for li in f.loop_indices:
                    uv[li].uv = green
        report["tail_patch_faces"] = len(patch)
    report["tail_verts_deleted"] = sum(len(t) for t in tails)

# --- 3. At most 4 influences, normalised ---------------------------------------------------------
trimmed = 0
for v in me.vertices:
    ws = sorted(((g.group, g.weight) for g in v.groups if g.weight > 1e-6), key=lambda t: -t[1])
    keep = ws[:4]
    if len(ws) > 4:
        trimmed += 1
        for gi, _ in ws[4:]:
            body.vertex_groups[gi].remove([v.index])
    total = sum(w for _, w in keep)
    if total > 0:
        for gi, w in keep:
            body.vertex_groups[gi].add([v.index], w / total, "REPLACE")
report["verts_trimmed_to_4"] = trimmed
report["unweighted_verts"] = sum(1 for v in me.vertices if not any(g.weight > 1e-6 for g in v.groups))

# --- 4. Separate pieces on bones -------------------------------------------------------------------
pts = world()
floor = min(p.z for p in pts)
H = max(p.z for p in pts) - floor
report["height"] = round(H, 4)
surface = BVHTree.FromPolygons(pts, [tuple(p.vertices) for p in me.polygons])
aw = arm.matrix_world


def joint(n):
    return aw @ arm.data.bones[P + n].head_local


def front_y(x, z):
    """Her front surface along a ray from the front (she faces -Y)."""
    hit = surface.ray_cast(Vector((x, -2 * H, z)), Vector((0, 1, 0)))
    return hit[0].y if hit[0] else None


def import_piece(path, piece):
    """Import a single-mesh model, bake its import transform into the mesh, name it <name>_<piece>."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    ob = max((o for o in new if o.type == "MESH"), key=lambda o: len(o.data.vertices))
    ob.data.transform(ob.matrix_world)
    ob.parent = None
    ob.matrix_world = Matrix.Identity(4)
    for o in new:
        if o is not ob:
            bpy.data.objects.remove(o)
    ob.name = ob.data.name = f"{name}_{piece}"
    return ob


def add_bone(bone, head, tail, parent):
    """A new bone, outside the Humanoid mapping, in her rest pose under `parent`."""
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    inv = arm.matrix_world.inverted()
    eb = arm.data.edit_bones.new(bone)
    eb.head, eb.tail = inv @ head, inv @ tail
    eb.parent = arm.data.edit_bones[P + parent]
    eb.use_connect = False
    bpy.ops.object.mode_set(mode="OBJECT")


def skin(ob, weights):
    """Bind a placed piece to the skeleton without moving it; weights(world position) -> {bone: weight}. Skinned
    rather than parented to a bone, so the rig pack and Unity see it like her body."""
    groups = {}
    for v in ob.data.vertices:
        for bone, w in weights(ob.matrix_world @ v.co).items():
            if w > 1e-4:
                if bone not in groups:
                    groups[bone] = ob.vertex_groups.new(name=bone)
                groups[bone].add([v.index], w, "REPLACE")
    saved = ob.matrix_world.copy()
    ob.parent = arm
    ob.matrix_world = saved
    ob.modifiers.new("Armature", "ARMATURE").object = arm


def keep_texture(ob, piece):
    """One material per piece, named <name>_<piece>, its colour map saved as textures/<name>_<piece>.png."""
    mat = ob.data.materials[0]
    mat.name = f"{name}_{piece}"
    while len(ob.data.materials) > 1:
        ob.data.materials.pop(index=len(ob.data.materials) - 1)
    for poly in ob.data.polygons:
        poly.material_index = 0
    node = max((n for n in mat.node_tree.nodes if n.type == "TEX_IMAGE" and n.image),
               key=lambda n: n.image.size[0] * n.image.size[1])
    img = node.image
    dst = os.path.join(out_dir, "textures", f"{name}_{piece}.png")
    side = TEX_SIZE[piece]
    if img.size[0] > side:
        img.scale(side, side)
    img.filepath_raw = dst
    img.file_format = "PNG"
    img.save()
    report.setdefault("textures", {})[piece] = {"file": dst, "size": list(img.size)}


keep_texture(body, "Body")
body.name = body.data.name = f"{name}_Body"

pieces = {}
if "shell" in opt:
    shell = import_piece(opt["shell"], "Shell")
    sz, wx, dd, zb, ty, pitch = SHELL_FIT
    sp = [v.co for v in shell.data.vertices]
    lo = Vector([min(p[i] for p in sp) for i in range(3)])
    hi = Vector([max(p[i] for p in sp) for i in range(3)])
    # Normalise to 1.0 tall with its bottom centre at the origin, then scale, pitch about its centre and place.
    norm = Matrix.Scale(1 / (hi.z - lo.z), 4) @ Matrix.Translation(-Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z)))
    S = Matrix.Diagonal((sz * wx * H, sz * dd * H, sz * H, 1))
    shell.data.transform(Matrix.Translation((0, ty * H, floor + zb * H + sz * H / 2)) @ Matrix.Rotation(math.radians(pitch), 4, "X")
                         @ S @ Matrix.Translation((0, 0, -0.5)) @ norm)
    keep_texture(shell, "Shell")
    # Its own bone under SHELL_BONE, pivoting on her back at that bone's height, so a clip can bounce it.
    z = joint(SHELL_BONE).z
    hit = surface.ray_cast(Vector((0, 2 * H, z)), Vector((0, -1, 0)))
    pivot = Vector((0, hit[0].y if hit[0] else joint(SHELL_BONE).y, z))
    add_bone("Shell", pivot, pivot + Vector((0, 0, 0.1 * H)), SHELL_BONE)
    skin(shell, lambda p: {"Shell": 1.0})
    pieces["shell"] = {"bone": "Shell", "parent": SHELL_BONE, "pivot": [round(v, 4) for v in pivot],
                       "verts": len(shell.data.vertices), "fit": SHELL_FIT}

if "whistle" in opt:
    whistle = import_piece(opt["whistle"], "Whistle")
    wm = whistle.data
    wp = [v.co.copy() for v in wm.vertices]
    lo = Vector([min(p[i] for p in wp) for i in range(3)])
    hi = Vector([max(p[i] for p in wp) for i in range(3)])
    # Its ring is the island on top.
    ring = max(islands(wm), key=lambda t: sum(wp[i].z for i in t[1]) / len(t[1]))[1]
    rlo = Vector([min(wp[i][k] for i in ring) for k in range(3)])
    rhi = Vector([max(wp[i][k] for i in ring) for k in range(3)])
    ring_c = (rlo + rhi) / 2
    s = WHISTLE_SIZE * H / max(hi - lo)
    # Native pose: barrel face toward -Y (her front), ring on top but behind the barrel, mouthpiece out to +X.
    # Hang it part of the way toward its centre sitting straight under the ring, then tilt the mouthpiece down.
    centre = sum(wp, Vector()) / len(wp) - ring_c
    hang_x = math.atan2(-centre.y, -centre.z)
    target = Vector((0, 0, floor + WHISTLE_Z * H))
    clear = 0.002 * H

    def placed(swing):
        """Hung under its ring with the ring's back on her chest, the barrel swung out by `swing` (radians).
        Returns the transform and how far the rest still sinks into her (<= 0: clear)."""
        R = (Matrix.Rotation(math.radians(WHISTLE_TILT), 4, "Y") @ Matrix.Rotation(-swing, 4, "X")
             @ Matrix.Rotation(hang_x * WHISTLE_HANG, 4, "X") @ Matrix.Scale(s, 4) @ Matrix.Translation(-ring_c))
        q = [R @ p for p in wp]
        ring_back = max(q[i].y for i in ring)
        M = Matrix.Translation((0, front_y(0, target.z) - clear - ring_back, target.z)) @ R
        sink = -1.0
        for p in rest:
            q = M @ p
            fy = front_y(q.x, q.z)
            if fy is not None:
                sink = max(sink, q.y - (fy - clear))
        return M, sink

    rest = [p for i, p in enumerate(wp) if i not in ring]

    # Gravity hangs the barrel straight under the ring; her chest and belly push it out. Swing it out until clear.
    for k in range(0, 46):
        M, sink = placed(math.radians(2 * k))
        if sink <= 0:
            break
    swing = 2 * k
    wm.transform(M)
    ring_pos = M @ ring_c
    keep_texture(whistle, "Whistle")
    # Its own bone under Spine2, pivoting on the ring, so a move can take it to her beak.
    add_bone("Whistle", ring_pos, ring_pos + Vector((0, 0, -0.05 * H)), "Spine2")
    skin(whistle, lambda p: {"Whistle": 1.0})
    pieces["whistle"] = {"bone": "Whistle", "parent": "Spine2", "ring": [round(v, 4) for v in ring_pos],
                         "scale": round(s, 4), "swing_deg": swing, "sink": round(sink, 4)}

    # Cord: around the neck at its narrowest, then down both sides of her chest to the whistle's ring.
    neck_lo, neck_hi = joint("Neck").z, joint("Head").z + 0.03 * H
    neck_axis = Vector((joint("Neck").x, joint("Neck").y, 0))

    def radius(theta, z):
        dvec = Vector((math.sin(theta), -math.cos(theta), 0))
        o = neck_axis + Vector((0, 0, z)) + dvec * 0.6 * H
        hit = surface.ray_cast(o, -dvec)
        return (hit[0] - (neck_axis + Vector((0, 0, z)))).length if hit[0] else None

    ring_z = []
    for k in range(13):
        z = neck_lo + (neck_hi - neck_lo) * k / 12
        rs = [radius(math.radians(a), z) for a in range(0, 360, 30) if a not in (90, 270)]
        if None not in rs:
            ring_z.append((sum(rs) / len(rs), z))
    neck_z = min(ring_z)[1]
    off = CORD_R + 0.003 * H
    TH0 = 55
    loop = []
    for a in range(TH0, 360 - TH0 + 1, 10):
        t = math.radians(a)
        r = radius(t, neck_z)
        dvec = Vector((math.sin(t), -math.cos(t), 0))
        loop.append(neck_axis + Vector((0, 0, neck_z)) + dvec * (r + off))

    def strand(a, b, n=10):
        """From a to b, lying on her chest; it leaves the chest only over the last stretch to the ring."""
        out = []
        for j in range(1, n):
            t = j / n
            p = a.lerp(b, t)
            fy = front_y(p.x, p.z)
            if fy is not None:
                lying = fy - off
                free = min(p.y, lying)
                w = max(0.0, (max(t, 1 - t) - 0.7) / 0.3)  # 0 in the middle, 1 at the ends
                p.y = lying + (free - lying) * w
            out.append(p)
        return out

    path = [ring_pos] + strand(ring_pos, loop[0]) + loop + strand(loop[-1], ring_pos)
    curve = bpy.data.curves.new(f"{name}_Cord", "CURVE")
    curve.dimensions = "3D"
    curve.bevel_depth = CORD_R
    curve.bevel_resolution = 2
    spline = curve.splines.new("BEZIER")
    spline.bezier_points.add(len(path) - 1)
    for bp, p in zip(spline.bezier_points, path):
        bp.co = p
        bp.handle_left_type = bp.handle_right_type = "AUTO"
    spline.use_cyclic_u = True
    cord_curve = bpy.data.objects.new(f"{name}_Cord", curve)
    bpy.context.scene.collection.objects.link(cord_curve)
    bpy.context.view_layer.update()
    cord_mesh = bpy.data.meshes.new_from_object(cord_curve.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    bpy.data.objects.remove(cord_curve)
    cord = bpy.data.objects.new(f"{name}_Cord", cord_mesh)
    bpy.context.scene.collection.objects.link(cord)
    if not cord_mesh.uv_layers:
        cord_mesh.uv_layers.new(name="UVMap")
    mat = bpy.data.materials.new(f"{name}_Cord")
    mat.use_nodes = True
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    img = bpy.data.images.new(f"{name}_Cord", 8, 8)
    img.pixels = [c for _ in range(64) for c in (*CORD_RGB, 1.0)]
    tn = mat.node_tree.nodes.new("ShaderNodeTexImage")
    tn.image = img
    mat.node_tree.links.new(tn.outputs["Color"], bsdf.inputs["Base Color"])
    cord_mesh.materials.append(mat)
    dst = os.path.join(out_dir, "textures", f"{name}_Cord.png")
    img.filepath_raw = dst
    img.file_format = "PNG"
    img.save()
    report.setdefault("textures", {})["Cord"] = {"file": dst, "size": [8, 8]}
    # The neck loop rides on Spine2; the strands hand over to the whistle's bone near the ring, so they follow it.
    def cord_weights(p, reach=0.06 * H):
        t = max(0.0, min(1.0, 1 - (p - ring_pos).length / reach))
        w = t * t * (3 - 2 * t)
        return {P + "Spine2": 1 - w, "Whistle": w}

    skin(cord, cord_weights)
    pieces["cord"] = {"bones": ["Spine2", "Whistle"], "neck_z": round(neck_z, 4), "points": len(path),
                      "verts": len(cord_mesh.vertices)}
report["pieces"] = pieces

# --- 5. Export ---------------------------------------------------------------------------------------
arm.name = "Armature"
bpy.ops.object.select_all(action="DESELECT")
arm.select_set(True)
for o in bpy.data.objects:
    if o.type == "MESH":
        o.select_set(True)
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
