"""Side-by-side review of two salamander FBXs (the live model and a build_v2.py result) under his GenRA clip USDs.

systemd-run --user --scope -q -p MemoryMax=3G -p MemorySwapMax=1G blender -b --factory-startup --python-exit-code 1 \
  --python tools/blender/salamander_rig/review_v2.py -- <live.fbx> <live_textures_dir> <new.fbx> <new_texture.png> <out_dir> \
  <mode> [clip=<usd>] [name=<label>] [frames=a,b,..|a:b:step] [views=front,game,side,back,three] [res=W] [clay=1] [stretch=1]

mode: stills (both models in one image per frame and view), metrics (stretch only, no renders), hands (close-ups of each model's hands: views rhand, bothhands),
seq (every frame in range, for video), rest (no clip: idle-free rest pose).
The live model is driven exactly as clip_review.py drives it (each bone takes the USD skeleton's world-space change from
rest; the USD rig pack is his live rig). The new model has other proportions, so it emulates Unity instead: Humanoid bones
take the world-space rotation change and keep their own joint offsets (the Hips also move with the clip); Hat and
Mustache copy the live bones' local change (same rest rotations), Straw and Tail1-5 take the live bones' rotation change
relative to Head or Hips (tailmode=root, how retarget_clips.eval.cs rewrites the clips' generic curves; tailmode=own
re-applies each live bone's local rotation change in its own axes instead), and each Pinky bone follows its Ring bone (the
prefab's RotationConstraints). stretch=1 also writes stretch-<name>.json: per frame, the new body's longest edge over its rest
length and where it is.
"""
import bpy, sys, os, json, math
import numpy as np
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:]
LIVE, LIVE_TEX, NEW, NEW_TEX, OUT, MODE = argv[:6]
OPT = dict(a.split("=", 1) for a in argv[6:])
OUT = os.path.abspath(OUT)
os.makedirs(OUT, exist_ok=True)
P = "mixamorig:"
GENERIC_COPY = ("Hat", "Mustache")
GENERIC_ROT = ("Straw",) + tuple(f"Tail{k}" for k in range(1, 6))

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene


def load(path, tag, dx):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=os.path.abspath(path), ignore_leaf_bones=False)
    new = [o for o in bpy.data.objects if o not in before]
    a = next(o for o in new if o.type == "ARMATURE")
    a.location.x += dx
    a.name = tag
    return a, [o for o in new if o.type == "MESH"]


GAP = float(OPT.get("gap", 0.42))
la, lmeshes = load(LIVE, "live", -GAP)
na, nmeshes = load(NEW, "new", GAP)
bpy.context.view_layer.update()
for a in (la, na):
    a.data.pose_position = "POSE"
    for pb in a.pose.bones:
        pb.matrix_basis = Matrix()


def texture(meshes, lookup):
    for o in meshes:
        for m in o.data.materials:
            if not m:
                continue
            m.use_nodes = True
            png = lookup(m.name)
            node = next((n for n in m.node_tree.nodes if n.type == "TEX_IMAGE"), None) or m.node_tree.nodes.new("ShaderNodeTexImage")
            node.image = bpy.data.images.load(png, check_existing=True)
            m.node_tree.nodes.active = node


def live_png(name):
    base = name.split(".")[0]
    p = os.path.join(LIVE_TEX, base + ".png")
    return p if os.path.exists(p) else os.path.join(LIVE_TEX, "Salamander_Body.png")


texture(lmeshes, live_png)
for o in nmeshes:  # the new model's material name collides with the live one; give it its own
    o.data.materials[0] = o.data.materials[0].copy()
texture(nmeshes, lambda n: os.path.abspath(NEW_TEX))

LORDER = [b.name for b in la.data.bones]
NORDER = [b.name for b in na.data.bones]


def load_clip(path):
    before = set(bpy.data.objects)
    bpy.ops.wm.usd_import(filepath=os.path.abspath(path))
    new = [o for o in bpy.data.objects if o not in before]
    ua = next(o for o in new if o.type == "ARMATURE")
    for o in new:
        if o.type == "MESH":
            bpy.data.objects.remove(o)
    ua.hide_render = True
    return ua


def deltas(ua, frame):
    scene.frame_set(frame)
    Aw = ua.matrix_world
    return {pb.name.replace("mixamorig_", P): Aw @ pb.matrix @ pb.bone.matrix_local.inverted() @ Aw.inverted()
            for pb in ua.pose.bones}


def set_basis(a, n, M, Mparent):
    b = a.data.bones[n]
    rel = (b.parent.matrix_local.inverted() @ b.matrix_local) if b.parent else b.matrix_local
    par = Mparent if b.parent else Matrix()
    a.pose.bones[n].matrix_basis = rel.inverted() @ par.inverted() @ M


def rot_only(o):
    """The object's rotation and scale: the clip's world deltas act about the origin, where the USD skeleton stands."""
    M = o.matrix_world.copy()
    M.translation = Vector()
    return M


def pose_live(D):
    Af = rot_only(la)
    M = {}
    for n in LORDER:
        b = la.data.bones[n]
        R = b.matrix_local
        if n not in D:
            M[n] = (M[b.parent.name] @ b.parent.matrix_local.inverted() @ R) if b.parent else R.copy()
            la.pose.bones[n].matrix_basis = Matrix()
            continue
        M[n] = Af.inverted() @ D[n] @ Af @ R
        set_basis(la, n, M[n], M[b.parent.name] if b.parent else None)
    bpy.context.view_layer.update()
    return M


def pose_new(D):
    Af = rot_only(na)
    A3 = Af.to_3x3().normalized()
    M = {}
    for n in NORDER:
        b = na.data.bones[n]
        R = b.matrix_local
        fk = (M[b.parent.name] @ b.parent.matrix_local.inverted() @ R) if b.parent else R.copy()
        if n in GENERIC_COPY or (n in GENERIC_ROT and OPT.get("tailmode", "root") == "own"):
            basis = la.pose.bones[n].matrix_basis.copy()
            if n in GENERIC_ROT:
                basis = basis.to_quaternion().to_matrix().to_4x4()
            na.pose.bones[n].matrix_basis = basis
            M[n] = fk @ basis
            continue
        if "Pinky" in n:
            ring = n.replace("Pinky", "Ring")
            if ring in M:
                Rr = na.data.bones[ring].matrix_local
                rot = (M[ring] @ Rr.inverted() @ R).to_3x3()
                M[n] = Matrix.Translation(fk.translation) @ rot.to_4x4()
                set_basis(na, n, M[n], M[b.parent.name])
                continue
        if n not in D:
            M[n] = fk
            na.pose.bones[n].matrix_basis = Matrix()
            continue
        rot_w = D[n].to_3x3()
        rot = A3.inverted() @ rot_w @ A3 @ R.to_3x3()
        if b.parent is None:
            pos = (Af.inverted() @ D[n] @ Af @ R).translation
        else:
            pos = fk.translation
        M[n] = Matrix.Translation(pos) @ rot.normalized().to_4x4()
        set_basis(na, n, M[n], M[b.parent.name] if b.parent else None)
    bpy.context.view_layer.update()
    return M


# --- render setup ------------------------------------------------------------------------------------------------
scene.render.engine = "BLENDER_WORKBENCH"
sh = scene.display.shading
sh.light, sh.color_type, sh.show_cavity = "STUDIO", "SINGLE" if OPT.get("clay") else "TEXTURE", False
sh.single_color = (0.75, 0.72, 0.66)
sh.show_shadows = False
scene.world = bpy.data.worlds.new("w")
scene.world.color = (0.62, 0.64, 0.68)
cd = bpy.data.cameras.new("cam"); cd.type = "ORTHO"; cd.clip_end = 50
cam = bpy.data.objects.new("cam", cd); scene.collection.objects.link(cam); scene.camera = cam
VIEWS = {"front": Vector((0, -1, 0.08)), "three": Vector((-0.55, -1, 0.18)), "side": Vector((-1, 0, 0.05)),
         "back": Vector((0, 1, 0.08)), "game": Vector((0.0, 1.0, 0.75))}
RES = int(OPT.get("res", 900))


def look(centre, d, size, w, h):
    d = d.normalized()
    cam.location = centre + d * 8
    cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
    cd.ortho_scale = size
    scene.render.resolution_x, scene.render.resolution_y = w, h


def shoot(path):
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def render_pair(stem, D=None, views=None):
    only = OPT.get("only")  # live|new: one model, framed on its own (centre=z, size=m)
    for vn in (views or OPT.get("views", "three,game")).split(","):
        place(vn)
        if D is not None:
            pose_live(D); pose_new(D)
        if only:
            a = la if only == "live" else na
            for o in lmeshes + nmeshes:
                o.hide_render = o not in (lmeshes if only == "live" else nmeshes)
            c = a.matrix_world.translation + Vector((0, 0, float(OPT.get("centre", 0.5))))
            look(c, VIEWS[vn], float(OPT.get("size", 0.6)), RES, RES)
        else:
            c = Vector((0, 0, 0.56))
            look(c, VIEWS[vn], 2.0 * GAP + 1.25, int(RES * 1.6), RES)
        shoot(os.path.join(OUT, f"{stem}-{vn}{'-' + only if only else ''}.png"))
    for o in lmeshes + nmeshes:
        o.hide_render = False
    place("front")


def place(vn):
    if vn in ("back", "game"):
        la.location, na.location = Vector((GAP, 0, 0)), Vector((-GAP, 0, 0))
    elif vn == "side":
        la.location, na.location = Vector((0, GAP, 0)), Vector((0, -GAP, 0))
    else:
        la.location, na.location = Vector((-GAP, 0, 0)), Vector((GAP, 0, 0))
    bpy.context.view_layer.update()


def frames_of(lo, hi):
    f = OPT.get("frames")
    if not f:
        return list(range(lo, hi + 1))
    if ":" in f:
        a, b, s = (int(x) for x in f.split(":"))
        return list(range(a, min(b, hi) + 1, s))
    return [int(x) for x in f.split(",")]


def body_world(meshes):
    dg = bpy.context.evaluated_depsgraph_get()
    out = []
    for o in meshes:
        ev = o.evaluated_get(dg); m = ev.to_mesh()
        co = np.empty(len(m.vertices) * 3, np.float32); m.vertices.foreach_get("co", co); ev.to_mesh_clear()
        Mw = np.array(o.matrix_world)
        out.append(co.reshape(-1, 3) @ Mw[:3, :3].T + Mw[:3, 3])
    return np.concatenate(out)


def bone_world(a, n):
    return a.matrix_world @ a.pose.bones[n].head


name = OPT.get("name", "clip")
if MODE == "rest":
    render_pair(f"{name}-rest", None, OPT.get("views", "front,three,side,back,game"))
    raise SystemExit(0)

ua = load_clip(OPT["clip"])
lo, hi = int(scene.frame_start), int(scene.frame_end)
FR = frames_of(lo, hi)
nb = nmeshes[0]
EDGES = np.array([e.vertices[:] for e in nb.data.edges])
place("front")
REST = body_world(nmeshes)
RL = np.linalg.norm(REST[EDGES[:, 0]] - REST[EDGES[:, 1]], axis=1)
# piece of every vertex (connected components), for per-piece stretch
parent = list(range(len(nb.data.vertices)))
def find(x):
    while parent[x] != x:
        parent[x] = parent[parent[x]]; x = parent[x]
    return x
for i, j in EDGES:
    ri, rj = find(int(i)), find(int(j))
    if ri != rj:
        parent[ri] = rj
PIECE = np.array([find(i) for i in range(len(parent))])
EP = PIECE[EDGES[:, 0]]
PIECE_C = {p: (REST[PIECE == p].mean(0) - np.array(list(na.location))).round(3).tolist() for p in set(PIECE.tolist())}
stretch = {}
for f in FR:
    D = deltas(ua, f)
    if MODE == "stills":
        render_pair(f"{name}-f{f:03d}", D)
    elif MODE == "seq":
        render_pair(f"seq-{name}-{f:04d}", D)
    elif MODE == "hands":
        place("front")
        pose_live(D); pose_new(D)
        for a, meshes, tag in ((la, lmeshes, "live"), (na, nmeshes, "new")):
            for o in lmeshes + nmeshes:
                o.hide_render = o not in meshes
            for vn in OPT.get("views", "rhand").split(","):
                if vn == "rhand":
                    h = bone_world(a, P + "RightHand")
                    tipn = P + "RightHandIndex3"
                    c = h.lerp(bone_world(a, tipn), 0.5)
                    fwd = (bone_world(a, tipn) - h).normalized()
                    for k, d in enumerate((Vector((-fwd.y, fwd.x, 0.35)), Vector((fwd.y, -fwd.x, 0.35)), Vector((-0.2, -0.3, 1)))):
                        look(c, d, 0.2, RES, RES)
                        shoot(os.path.join(OUT, f"hands-{name}-f{f:03d}-{tag}-r{k}.png"))
                else:
                    c = bone_world(a, P + "RightHand").lerp(bone_world(a, P + "LeftHand"), 0.5)
                    for k, d in enumerate((Vector((-0.6, -1, 0.3)), Vector((0.3, -0.4, 1)))):
                        look(c, d, 0.34, RES, RES)
                        shoot(os.path.join(OUT, f"hands-{name}-f{f:03d}-{tag}-b{k}.png"))
        for o in lmeshes + nmeshes:
            o.hide_render = False
    if OPT.get("debug"):
        place("front"); pose_live(D); pose_new(D)
        Bw = body_world(nmeshes) - np.array(list(na.location))
        far = np.where(Bw[:, 2] > 0.95)[0]
        gi = {g.index: g.name for g in nb.vertex_groups}
        import collections
        cnt = collections.Counter(gi[max(nb.data.vertices[i].groups, key=lambda g: g.weight).group] for i in far)
        print("DEBUG far verts", len(far), cnt.most_common(8))
        for n in ("mixamorig:RightHand", "mixamorig:RightHandIndex1", "mixamorig:RightHandIndex3", "mixamorig:RightHandPinky1", "mixamorig:RightHandThumb2", "mixamorig:RightForeArm"):
            print("DEBUG bone", n, [round(x, 3) for x in (bone_world(na, n) - na.location)], "in D", n in D)
    if OPT.get("stretch") or MODE == "metrics":
        place("front"); pose_live(D); pose_new(D)
        B = body_world(nmeshes)
        r = np.linalg.norm(B[EDGES[:, 0]] - B[EDGES[:, 1]], axis=1) / np.maximum(RL, 1e-6)
        k = int(np.argmax(r))
        j = int(np.argmin(r))
        per = {}
        for p in PIECE_C:
            m = EP == p
            if m.any():
                per[str(PIECE_C[p])] = [round(float(r[m].max()), 2), round(float(r[m].min()), 2)]
        stretch[f] = {"pieces": per, "max": round(float(r[k]), 3), "at": [round(float(x), 3) for x in REST[EDGES[k, 0]] - np.array(list(na.location))],
                      "min": round(float(r[j]), 3), "min_at": [round(float(x), 3) for x in REST[EDGES[j, 0]] - np.array(list(na.location))]}
if stretch:
    json.dump(stretch, open(os.path.join(OUT, f"stretch-{name}.json"), "w"), indent=1)
    worst = max(stretch, key=lambda f: stretch[f]["max"])
    print("STRETCH", name, "worst frame", worst, stretch[worst])
print("DONE", name, len(FR))
