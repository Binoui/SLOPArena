"""Unity-ready salamander from the owner's Mixamo rig, with fixed weights, a tail chain and his costume pieces.

blender -b --factory-startup --python-exit-code 1 --python build_rig.py -- \
    <mixamo.fbx> <hat.blend> <poncho.blend> <texture.png> <out_dir> [key=value ...]

The Mixamo download's bone rest is the upload A-pose and its mesh is the upload mesh; its take is a T-pose.
1. Weights, in the A-pose:
   - head: the jaw, snout and skull follow Head (Mixamo gave the lower half of the head to Neck);
   - arms: hand, forearm and upper-arm weights only on skin that is really on that arm, measured as surface
     (geodesic) distance from the wrist (Mixamo spilled the right hand onto his waist and the straw);
   - tail: a Tail1..TailN chain under Hips from the tail's own geometry (Mixamo tied it to the thighs);
   - mustache and straw: rigid on new bones Mustache and Straw under Head.
2. Pieces: the hat (crown and brim) rigid on a new bone Hat under Head; the draped poncho skinned from the body's
   nearest surface (no head, forearm, hand or tail weights; thighs damped), smoothed.
3. At most 4 influences per vertex.
4. Rest pose: the take's T-pose becomes the rest, and every mesh is solved back (inverse skinning) so that posing
   the skeleton in the A-pose reproduces the A-pose model exactly. A forward bake of the T-pose instead pulls blended
   skin (armpits, the poncho over the arms) toward the joints when the arms come back down.
Writes <out_dir>/<name>.fbx (no embedded media), textures/<name>_Body.png, <name>_rig.blend and <name>_report.json.

Options: name=Salamander tail_bones=5 tex=4096
         poncho_torso=1 (poncho on spine, neck, shoulders and hips only, for Unity cloth)
         poncho_tex=<png> (separate Salamander_Poncho material and textures/<name>_Poncho.png, original size)
         jeans=<fitted.blend from fit_jeans.py> jeans_tex=<its jeans_basecolor.png>: the jeans are skinned from the
         body's torso and leg skin (tail weights go to Hips), and the body skin they hide is removed
"""
import bpy, bmesh, sys, os, json, math, heapq, shutil
import numpy as np
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree
from mathutils.geometry import intersect_point_line

argv = sys.argv[sys.argv.index("--") + 1:]
SRC, HAT_BLEND, PONCHO_BLEND, TEX, OUT = (os.path.abspath(a) for a in argv[:5])
opt = dict(a.split("=", 1) for a in argv[5:])
NAME = opt.get("name", "Salamander")
NTAIL = int(opt.get("tail_bones", 5))
TEX_SIZE = int(opt.get("tex", 4096))
# poncho_torso=1: the poncho follows only the spine, neck and hips; Unity cloth moves the rest, so a raised
# arm lifts it instead of stretching it like a wing (owner, 2026-10-27).
PONCHO_TORSO = opt.get("poncho_torso", "0") == "1"
P = "mixamorig:"
os.makedirs(os.path.join(OUT, "textures"), exist_ok=True)
report = {"source": SRC, "options": opt}

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
scene = bpy.context.scene
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
body = next(o for o in bpy.data.objects if o.type == "MESH")
act = arm.animation_data.action if arm.animation_data else None
take_frame = int(act.frame_range[0]) if act else None
if act:
    arm.animation_data.action = None
for pb in arm.pose.bones:
    pb.matrix_basis.identity()
bpy.context.view_layer.update()
AW = arm.matrix_world.copy()
AWi = AW.inverted()


def joint(n, end="head"):
    b = arm.data.bones[n if n in arm.data.bones else P + n]
    return AW @ (b.head_local if end == "head" else b.tail_local)


def smoothstep(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


# --- body mesh in world space (A-pose bind), islands, weights ---------------------------------------
me = body.data
MW = body.matrix_world.copy()
V = [MW @ v.co for v in me.vertices]
gname = {g.index: g.name for g in body.vertex_groups}
WT = [{gname[g.group]: g.weight for g in v.groups if g.weight > 1e-5} for v in me.vertices]
bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table(); bm.faces.ensure_lookup_table()
seen, isl = set(), []
for f in bm.faces:
    if f.index in seen:
        continue
    vs, stack = set(), [f]; seen.add(f.index)
    while stack:
        g = stack.pop(); vs.update(v.index for v in g.verts)
        for e in g.edges:
            for h in e.link_faces:
                if h.index not in seen:
                    seen.add(h.index); stack.append(h)
    isl.append(vs)
isl.sort(key=len, reverse=True)
BODY = isl[0]
def centroid(vs):
    return sum((V[i] for i in vs), Vector()) / len(vs)
MUSTACHE = next(s for s in isl[1:] if centroid(s).y < -0.12 and centroid(s).z > 0.75)
STRAW = next(s for s in isl[1:] if centroid(s).z > 0.7 and abs(centroid(s).x) > 0.08)
report["islands"] = {"body": len(BODY), "mustache": len(MUSTACHE), "straw": len(STRAW), "count": len(isl)}
ARM_CHAIN = lambda side: {P + side + n for n in ("Arm", "ForeArm", "Hand")} | {g for g in gname.values() if g.startswith(P + side + "Hand")}
TORSO_LEG = {P + n for n in ("Hips", "Spine", "Spine1", "Spine2", "LeftUpLeg", "RightUpLeg", "LeftLeg", "RightLeg")}


def norm(w):
    s = sum(w.values())
    return {k: v / s for k, v in w.items() if v > 1e-5} if s > 0 else {}


def nearest_bone(p, names):
    best, bd = None, 9.0
    for n in names:
        _, f = intersect_point_line(p, joint(n), joint(n, "tail"))
        q = joint(n).lerp(joint(n, "tail"), max(0.0, min(1.0, f)))
        if (p - q).length < bd:
            best, bd = n, (p - q).length
    return best


# --- 1a. head: the jaw, snout and skull follow Head ---------------------------------------------------
neck, headj = joint("Neck"), joint("Head")
head_fixed = 0
for i in BODY:
    p = V[i]
    if p.z < 0.70 or abs(p.x) > 0.17:
        continue
    t_z = smoothstep(headj.z - 0.045, headj.z - 0.005, p.z)          # up the neck column
    t_f = smoothstep(neck.y - 0.035, neck.y - 0.065, p.y) * smoothstep(0.715, 0.745, p.z)  # jaw in front of the neck
    t = max(t_z, t_f)
    w = {k: v for k, v in WT[i].items() if k not in ARM_CHAIN("Left") | ARM_CHAIN("Right")} if p.z > 0.735 and abs(p.x) < 0.13 else dict(WT[i])
    if t > 0:
        w = norm(w) if w else {P + "Neck": 1.0}
        w = {k: v * (1 - t) for k, v in w.items()}
        w[P + "Head"] = w.get(P + "Head", 0) + t
        head_fixed += 1
    WT[i] = norm(w) if w else {P + "Neck": 1.0}
report["head_fixed_verts"] = head_fixed

# --- 1b. arms: geodesic distance from the wrist decides who may hold arm weights -----------------------
def geodesic(seeds, region):
    dist = {i: 0.0 for i in seeds}
    heap = [(0.0, i) for i in seeds]
    while heap:
        d, i = heapq.heappop(heap)
        if d > dist.get(i, 9.0):
            continue
        for e in bm.verts[i].link_edges:
            o = e.other_vert(bm.verts[i]).index
            if o not in region:
                continue
            nd = d + (V[i] - V[o]).length
            if nd < dist.get(o, 9.0):
                dist[o] = nd; heapq.heappush(heap, (nd, o))
    return dist

arm_report = {}
for side in ("Left", "Right"):
    sgn = 1 if side == "Left" else -1
    hand, fore, upper = joint(side + "Hand"), joint(side + "ForeArm"), joint(side + "Arm")
    l_fore, l_up = (hand - fore).length, (fore - upper).length
    # Seed at the fingertip: the lowest point out beyond the hips on that side. Seeds at the wrist caught hip skin
    # right next to the hanging hand.
    tipv = min((i for i in BODY if V[i].x * sgn > 0.15), key=lambda i: V[i].z)
    l_hand = (V[tipv] - hand).length
    G = geodesic([tipv], BODY)
    lower = {P + side + "ForeArm", P + side + "Hand"} | {g for g in gname.values() if g.startswith(P + side + "Hand")}
    removed = cleaned = 0
    for i in BODY:
        g = G.get(i, 9.0)
        w = dict(WT[i])
        changed = False
        for k in list(w):
            if (k in lower and g > l_hand + l_fore + 0.06) or (k == P + side + "Arm" and g > l_hand + l_fore + l_up + 0.08
                                                                and (V[i] - upper).length > 0.07):
                del w[k]; changed = True; removed += 1
        if g < l_hand + 0.5 * l_fore:   # hand and forearm skin never follows the torso or legs
            for k in list(w):
                if k in TORSO_LEG:
                    del w[k]; changed = True; cleaned += 1
        if changed:
            if not w:
                w = {nearest_bone(V[i], [P + n for n in ("Hips", "Spine", "Spine1", "Spine2", "LeftUpLeg", "RightUpLeg")]): 1.0}
            WT[i] = norm(w)
    left_over = sum(1 for i in BODY if G.get(i, 9.0) > l_hand + l_fore + 0.06 and any(k in lower for k in WT[i]))
    arm_report[side] = {"tip": [round(c, 3) for c in V[tipv]], "weights_removed": removed, "torso_leg_removed_from_hand": cleaned,
                        "hand_len": round(l_hand, 4), "forearm_len": round(l_fore, 4), "upperarm_len": round(l_up, 4),
                        "left_over": left_over}
report["arms"] = arm_report

# --- 1c. tail chain -------------------------------------------------------------------------------------
LEGB = {P + n for n in ("LeftUpLeg", "RightUpLeg", "LeftLeg", "RightLeg")}
leg_segs = [(joint(s + "UpLeg"), joint(s + "Leg")) for s in ("Left", "Right")] + \
           [(joint(s + "Leg"), joint(s + "Foot")) for s in ("Left", "Right")]
def leg_dist(p):
    best = 9.0
    for h, t in leg_segs:
        _, f = intersect_point_line(p, h, t)
        best = min(best, (p - h.lerp(t, max(0.0, min(1.0, f)))).length)
    return best
hips = joint("Hips")
spine_c = min(BODY, key=lambda i: (V[i] - joint("Spine")).length)
from_spine = geodesic([spine_c], BODY)
cands = [i for i in BODY if max(WT[i], key=WT[i].get) in LEGB and leg_dist(V[i]) > 0.1 and V[i].y > hips.y]
# The tail curls up at its end; the end is the backmost point, while the farthest point from the spine can land on
# the underside of the curl.
tip = max(cands, key=lambda i: V[i].y)
g_tip = geodesic([tip], BODY)
# The root: the ring through two points on his midline where the tail leaves the body (from the side profile:
# the back starts sloping into the tail at the dorsal point; the ventral point is the crotch).
dors = Vector([float(c) for c in opt.get("tail_root_dorsal", "0,0.10,0.45").split(",")])
vent = Vector([float(c) for c in opt.get("tail_root_ventral", "0,0.06,0.35").split(",")])
def midline_near(q):
    return min((i for i in BODY if abs(V[i].x) < 0.015), key=lambda i: (V[i] - q).length)
s_root = 0.5 * (g_tip[midline_near(dors)] + g_tip[midline_near(vent)])
TAIL = {i for i, d in g_tip.items() if d < s_root}
u = {i: 1 - g_tip[i] / s_root for i in TAIL}   # 0 at the root, 1 at the tip
samples, k = [], 0.0
while k <= 1.0001:
    pts = [V[i] for i in TAIL if abs(u[i] - k) < 0.06]
    if pts:
        samples.append(sum(pts, Vector()) / len(pts))
    k += 0.04
samples[-1] = V[tip].copy()
sm = [samples[0]] + [(samples[i - 1] + samples[i] * 2 + samples[i + 1]) / 4 for i in range(1, len(samples) - 1)] + [samples[-1]]
seg = [0.0]
for a, b in zip(sm, sm[1:]):
    seg.append(seg[-1] + (b - a).length)
def along(t):
    L = t * seg[-1]
    for i in range(1, len(seg)):
        if seg[i] >= L:
            f = (L - seg[i - 1]) / max(1e-9, seg[i] - seg[i - 1]); return sm[i - 1].lerp(sm[i], f)
    return sm[-1]
tail_joints = [along(j / NTAIL) for j in range(NTAIL + 1)]
report["tail"] = {"verts": len(TAIL), "surface_len": round(s_root, 3), "chain_len": round(seg[-1], 3),
                  "joints": [[round(c, 3) for c in p] for p in tail_joints]}

# --- bones: tail chain, Mustache, Straw, Hat -------------------------------------------------------------
hat_objs = []
with bpy.data.libraries.load(HAT_BLEND) as (src, dst):
    dst.objects = [n for n in src.objects if n in ("crown", "brim")]
for o in dst.objects:
    scene.collection.objects.link(o); hat_objs.append(o)
with bpy.data.libraries.load(PONCHO_BLEND) as (src, dst):
    dst.objects = [n for n in src.objects if n == "poncho"]
poncho = dst.objects[0]
scene.collection.objects.link(poncho)
jeans = None
if opt.get("jeans"):
    with bpy.data.libraries.load(os.path.abspath(opt["jeans"])) as (src, dst):
        dst.objects = [n for n in src.objects if n == "jeans"]
    jeans = dst.objects[0]
    scene.collection.objects.link(jeans)
for o in hat_objs + [poncho] + ([jeans] if jeans else []):
    o.data = o.data.copy()
    o.data.transform(o.matrix_world); o.matrix_world = Matrix.Identity(4)
    for m in list(o.modifiers):
        o.modifiers.remove(m)
    o.vertex_groups.clear()
bpy.ops.object.select_all(action="DESELECT")
for o in hat_objs:
    o.select_set(True)
bpy.context.view_layer.objects.active = hat_objs[0]
bpy.ops.object.join()
hat = bpy.context.view_layer.objects.active
hat.name = hat.data.name = f"{NAME}_Hat"
poncho.name = poncho.data.name = f"{NAME}_Poncho"
if jeans:
    jeans.name = jeans.data.name = f"{NAME}_Jeans"
HP = [v.co.copy() for v in hat.data.vertices]
hat_base = Vector((sum(p.x for p in HP) / len(HP), sum(p.y for p in HP) / len(HP), min(p.z for p in HP)))
# the hat's opening sits on the skull: put the pivot at the head top's height, under the crown's centre
hat_pivot = Vector((hat_base.x, hat_base.y, max(V[i].z for i in BODY) - 0.03))
mus_c = centroid(MUSTACHE)
mus_back = Vector((mus_c.x, max(V[i].y for i in MUSTACHE), mus_c.z))
straw_pts = [V[i] for i in STRAW]
straw_end = min(straw_pts, key=lambda p: (p - Vector((0.09, -0.05, 0.84))).length)
straw_far = max(straw_pts, key=lambda p: (p - straw_end).length)

bpy.ops.object.select_all(action="DESELECT")
bpy.context.view_layer.objects.active = arm; arm.select_set(True)
bpy.ops.object.mode_set(mode="EDIT")
eb = arm.data.edit_bones
def new_bone(name, head, tail, parent, connect=False):
    b = eb.new(name)
    b.head, b.tail = AWi @ head, AWi @ tail
    b.parent = eb[parent]; b.use_connect = connect; b.use_deform = True
    return b
prev = P + "Hips"
tail_names = []
for j in range(NTAIL):
    b = new_bone(f"Tail{j + 1}", tail_joints[j], tail_joints[j + 1], prev, connect=j > 0)
    b.align_roll(AWi.to_3x3() @ Vector((0, 0, 1)))
    prev = b.name; tail_names.append(b.name)
new_bone("Mustache", mus_back, mus_back + Vector((0, -0.05, 0)), P + "Head")
new_bone("Straw", straw_end, straw_end + (straw_far - straw_end).normalized() * 0.08, P + "Head")
new_bone("Hat", hat_pivot, hat_pivot + Vector((0, 0, 0.12)), P + "Head")
bpy.ops.object.mode_set(mode="OBJECT")

# tail weights: linear blend between bone mid-points; the root eases in from Hips
mids = [(j + 0.5) / NTAIL for j in range(NTAIL)]
for i in TAIL:
    t = u[i]
    new = {}
    if t <= mids[0]:
        f = t / mids[0]; new[P + "Hips"] = 1 - f; new[tail_names[0]] = f
    elif t >= mids[-1]:
        new[tail_names[-1]] = 1.0
    else:
        j = min(NTAIL - 2, int((t - mids[0]) * NTAIL)); f = (t - mids[j]) * NTAIL
        new[tail_names[j]] = 1 - f; new[tail_names[j + 1]] = f
    a = min(1.0, t / 0.15)   # the first 15% blends from the body's own (leg-free) skin
    old = {k: v for k, v in WT[i].items() if k not in LEGB}
    old = norm(old) if old else {P + "Hips": 1.0}
    WT[i] = norm({k: (1 - a) * old.get(k, 0) + a * new.get(k, 0) for k in set(old) | set(new)})
for i in MUSTACHE:
    WT[i] = {"Mustache": 1.0}
for i in STRAW:
    WT[i] = {"Straw": 1.0}


def limit4(w):
    top = sorted(w.items(), key=lambda t: -t[1])[:4]
    return norm(dict(top))


def write_weights(ob, weights):
    ob.vertex_groups.clear()
    groups = {}
    for i, w in enumerate(weights):
        for k, x in limit4(w).items():
            if k not in groups:
                groups[k] = ob.vertex_groups.new(name=k)
            groups[k].add([i], x, "REPLACE")


write_weights(body, WT)

# --- 2. pieces: hat rigid, poncho from the body's nearest surface -----------------------------------------
write_weights(hat, [{"Hat": 1.0} for _ in hat.data.vertices])
body_tris = [t for t in (tuple(p.vertices) for p in me.polygons) if all(i in BODY for i in t)]
tri_list = []
for p in me.polygons:
    if all(i in BODY for i in p.vertices):
        vs = list(p.vertices)
        for k in range(1, len(vs) - 1):
            tri_list.append((vs[0], vs[k], vs[k + 1]))
bvh = BVHTree.FromPolygons(V, tri_list)
PV = [v.co.copy() for v in poncho.data.vertices]
pw = []
for p in PV:
    loc, nrm, ti, d = bvh.find_nearest(p)
    a, b, c = tri_list[ti]
    from mathutils.geometry import barycentric_transform
    bc = barycentric_transform(loc, V[a], V[b], V[c], Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1)))
    w = {}
    for vi, f in zip((a, b, c), bc):
        for k, x in WT[vi].items():
            w[k] = w.get(k, 0) + max(0.0, f) * x
    out = {}
    for k, x in w.items():
        if k in (P + "Head", "Hat", "Mustache", "Straw"):
            k = P + "Neck"
        elif PONCHO_TORSO and any(k.startswith(P + side + a) for side in ("Left", "Right")
                                  for a in ("Shoulder", "Arm", "ForeArm", "Hand")):
            # not the shoulders either: Unity's Humanoid lifts the collarbones with a raised arm, and the poncho's back
            # then bulged up like a hump (2026-10-27); the arm comes out from under it instead
            k = P + "Spine2"
        elif k.startswith(P + "LeftForeArm") or k.startswith(P + "LeftHand"):
            k = P + "LeftArm"
        elif k.startswith(P + "RightForeArm") or k.startswith(P + "RightHand"):
            k = P + "RightArm"
        elif k in tail_names:
            k = P + "Hips"
        elif PONCHO_TORSO and k in LEGB:
            k = P + "Hips"
        elif k in LEGB:
            out[P + "Hips"] = out.get(P + "Hips", 0) + 0.7 * x
            x *= 0.3
        out[k] = out.get(k, 0) + x
    pw.append(norm(out))
# smooth over the poncho's own edges so neighbouring cloth moves together
pbm = bmesh.new(); pbm.from_mesh(poncho.data); pbm.verts.ensure_lookup_table()
nbrs = [[e.other_vert(v).index for e in v.link_edges] for v in pbm.verts]
pbm.free()
for _ in range(int(opt.get("poncho_smooth", 4))):
    nw = []
    for i, w in enumerate(pw):
        acc = {k: 0.5 * x for k, x in w.items()}
        if nbrs[i]:
            for j in nbrs[i]:
                for k, x in pw[j].items():
                    acc[k] = acc.get(k, 0) + 0.5 * x / len(nbrs[i])
        nw.append(norm(acc))
    pw = nw
write_weights(poncho, pw)
covered = set()
if jeans:
    # the jeans: weights from the nearest body skin that is torso or leg (never arm, hand, head or tail)
    ARMW = ARM_CHAIN("Left") | ARM_CHAIN("Right")
    leg_tris = [t for t in tri_list if all(not (set(WT[i]) & ARMW) and V[i].z < 0.62 and i not in TAIL for i in t)]
    lbvh = BVHTree.FromPolygons(V, leg_tris)
    JW = [v.co.copy() for v in jeans.data.vertices]
    jw = []
    for p in JW:
        loc, nrm, ti, d = lbvh.find_nearest(p)
        a, b, c = leg_tris[ti]
        bc = barycentric_transform(loc, V[a], V[b], V[c], Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1)))
        w = {}
        for vi, f in zip((a, b, c), bc):
            for k, x in WT[vi].items():
                k = P + "Hips" if k in tail_names else k
                w[k] = w.get(k, 0) + max(0.0, f) * x
        jw.append(norm(w))
    jbm = bmesh.new(); jbm.from_mesh(jeans.data); jbm.verts.ensure_lookup_table()
    jn = [[e.other_vert(v).index for e in v.link_edges] for v in jbm.verts]
    jbm.free()
    for _ in range(int(opt.get("jeans_smooth", 2))):
        nw = []
        for i, w in enumerate(jw):
            acc = {k: 0.5 * x for k, x in w.items()}
            for j in jn[i]:
                for k, x in jw[j].items():
                    acc[k] = acc.get(k, 0) + 0.5 * x / len(jn[i])
            nw.append(norm(acc) if jn[i] else w)
        jw = nw
    write_weights(jeans, jw)
    # body skin the jeans hide: a ray out along the skin normal meets the jeans (not the tail, arms or boots)
    jt = []
    for q in jeans.data.polygons:
        vs = list(q.vertices)
        for k in range(1, len(vs) - 1):
            jt.append((vs[0], vs[k], vs[k + 1]))
    jbvh = BVHTree.FromPolygons(JW, jt)
    MN = MW.to_3x3().inverted().transposed()
    for i in BODY:
        if i in TAIL or set(WT[i]) & ARMW or V[i].z > 0.6:
            continue
        n = (MN @ me.vertices[i].normal).normalized()
        if jbvh.ray_cast(V[i] + n * 0.001, n, 0.06)[0] is not None:
            covered.add(i)
    report["jeans"] = {"verts": len(JW), "bones": sorted({k for w in jw for k in w}), "covered_body_verts": len(covered)}
for o in (hat, poncho) + ((jeans,) if jeans else ()):
    saved = o.matrix_world.copy()
    o.parent = arm; o.matrix_world = saved
    o.modifiers.new("Armature", "ARMATURE").object = arm
report["poncho"] = {"verts": len(PV), "bones": sorted({k for w in pw for k in w})}

# --- 3/4. T-pose rest by inverse skinning ----------------------------------------------------------------
meshes = [body, hat, poncho] + ([jeans] if jeans else [])
A_pos = {o.name: [o.matrix_world @ v.co for v in o.data.vertices] for o in meshes}
if act:
    arm.animation_data.action = act
    scene.frame_set(take_frame)
    bpy.context.view_layer.update()
    basis = {pb.name: pb.matrix_basis.copy() for pb in arm.pose.bones}
    arm.animation_data.action = None
    for pb in arm.pose.bones:
        pb.matrix_basis = basis[pb.name]
    bpy.context.view_layer.update()
    RA = {b.name: b.matrix_local.copy() for b in arm.data.bones}
    PT = {pb.name: pb.matrix.copy() for pb in arm.pose.bones}
    N = {n: RA[n] @ PT[n].inverted() for n in RA}     # armature space: T-rest -> A-pose
    for o in meshes:
        for m in [m for m in o.modifiers if m.type == "ARMATURE"]:
            o.modifiers.remove(m)
        names = {g.index: g.name for g in o.vertex_groups}
        Wi = o.matrix_world.inverted()
        for v, pa in zip(o.data.vertices, A_pos[o.name]):
            M = np.zeros((4, 4))
            for g in v.groups:
                M += g.weight * np.array(N[names[g.group]])
            va = AWi @ pa
            vt = np.linalg.solve(M, np.array([va.x, va.y, va.z, 1.0]))
            v.co = Wi @ (AW @ Vector(vt[:3] / vt[3]))
        o.data.update()
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = arm; arm.select_set(True)
    bpy.ops.object.mode_set(mode="POSE")
    bpy.ops.pose.armature_apply(selected=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    for o in meshes:
        o.modifiers.new("Armature", "ARMATURE").object = arm
    bpy.context.view_layer.update()
    # check: posing the new rest back into the A-pose gives the A-pose model
    order = sorted(arm.pose.bones, key=lambda pb: len(pb.parent_recursive))
    for pb in order:
        pb.matrix = RA[pb.name]
        bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    err = 0.0
    for o in meshes:
        ev = o.evaluated_get(dg)
        for v, pa in zip(ev.data.vertices, A_pos[o.name]):
            err = max(err, ((o.matrix_world @ v.co) - pa).length)
    report["a_pose_roundtrip_max_err_m"] = err
    for pb in arm.pose.bones:
        pb.matrix_basis.identity()
    bpy.context.view_layer.update()
    for side in ("Left", "Right"):
        d = (joint(side + "ForeArm") - joint(side + "Arm")).normalized()
        report[f"{side.lower()}_arm_rest_dir_z"] = round(d.z, 4)

# --- 5. fingers (T-rest): Mixamo gave one chain down the middle finger; split it into thumb, index, middle, ring --
# His hand: a thumb pointing forward and three fingers side by side front to back. In the T-rest the hands point
# along +-X, palms down, thumbs toward -Y. Finger Guns needs the index finger straight while the others curl.
def finger_step():
    rep = {}
    for side, sgn in (("Left", 1), ("Right", -1)):
        hand = joint(side + "Hand")
        hb = [P + side + "Hand"] + [P + side + f"HandIndex{k}" for k in range(1, 5)]
        cand = [i for i in range(len(me.vertices)) if any(g.weight > 0 and body.vertex_groups[g.group].name in hb
                                                          for g in me.vertices[i].groups)]
        Vt = [MWb @ me.vertices[i].co for i in cand]
        xs = [(p.x - hand.x) * sgn for p in Vt]
        # The knuckles sit where his fingers part from the palm, 6.5-6.7 cm out from the wrist joint. At 9.5 cm (until
        # 2026-10-27) the first 3 cm of each finger stayed on the palm, so curled fingers still reached 11 cm along the
        # hand against the index's 15 and the finger gun read as an open hand.
        x_k = float(opt.get("knuckle", 0.065))
        # three finger bands by y, beyond the knuckles; the thumb is the forward-most part of the palm
        bands = {"Index": (-0.032, -0.0005), "Middle": (-0.0005, 0.025), "Ring": (0.025, 0.07)}
        tips, axes = {}, {}
        for f, (y0, y1) in bands.items():
            pts = [p for p, x in zip(Vt, xs) if x > x_k and y0 <= p.y - hand.y < y1]
            yc = sum(p.y for p in pts) / len(pts); zc = sum(p.z for p in pts) / len(pts)
            tipx = max((p.x - hand.x) * sgn for p in pts)
            axes[f] = (yc, zc); tips[f] = tipx
        th = [p for p, x in zip(Vt, xs) if p.y - hand.y < -0.045 and x > 0.02]
        tc = sum(th, Vector()) / len(th)
        C = np.cov(np.array([p[:] for p in th]).T); w, v = np.linalg.eigh(C)
        td = Vector(v[:, 2]); td = td if (td.x * sgn - td.y) > 0 else -td      # outward and forward
        proj = [(p - tc).dot(td) for p in th]
        t_tip = tc + td * max(proj)
        t_base = tc + td * (min(proj) - 0.012)
        # bones
        bpy.ops.object.select_all(action="DESELECT")
        bpy.context.view_layer.objects.active = arm; arm.select_set(True)
        bpy.ops.object.mode_set(mode="EDIT")
        eb = arm.data.edit_bones
        def chain(names, pts, parent):
            prev = eb[parent]
            for k, n in enumerate(names):
                b = eb[n] if n in eb else eb.new(n)
                b.head, b.tail = AWi @ pts[k], AWi @ pts[k + 1]
                b.parent = prev; b.use_connect = k > 0; b.use_deform = k < len(names) - 1
                b.align_roll(AWi.to_3x3() @ Vector((0, -1, 0)))
                prev = b
        for f in ("Index", "Middle", "Ring"):
            yc, zc = axes[f]
            L = tips[f] - x_k
            fr = [0.0, 0.38, 0.69, 1.0, 1.18]
            pts = [Vector((hand.x + sgn * (x_k + L * r), yc, zc)) for r in fr]
            chain([P + side + f"Hand{f}{k}" for k in range(1, 5)], pts, P + side + "Hand")
        L = (t_tip - t_base).length
        pts = [t_base + (t_tip - t_base) * r for r in (0.0, 0.4, 0.72, 1.0, 1.18)]
        chain([P + side + f"HandThumb{k}" for k in range(1, 5)], pts, P + side + "Hand")
        bpy.ops.object.mode_set(mode="OBJECT")
        # weights: the hand's share of each vertex goes to the palm, a finger or the thumb by position
        names = {g.index: g.name for g in body.vertex_groups}
        def grp(n):
            return body.vertex_groups.get(n) or body.vertex_groups.new(name=n)
        moved = 0
        for i, p, x in zip(cand, Vt, xs):
            v = me.vertices[i]
            wv = {names[g.group]: g.weight for g in v.groups}
            share = sum(wv.get(n, 0) for n in hb)
            new = {}
            dy = p.y - hand.y
            thumb_m = smoothstep(-0.036, -0.046, dy) * smoothstep(0.015, 0.03, x)
            if thumb_m > 0:
                s_ = max(0.0, min(1.0, (p - t_base).dot((t_tip - t_base).normalized()) / L))
                seg = 0 if s_ < 0.4 else (1 if s_ < 0.72 else 2)
                new[P + side + f"HandThumb{seg + 1}"] = thumb_m
            rest = 1 - thumb_m
            fing = smoothstep(x_k - 0.008, x_k + 0.006, x)
            if fing > 0 and rest > 0:
                mem = {"Index": 1 - smoothstep(-0.004, 0.003, dy),
                       "Middle": smoothstep(-0.004, 0.003, dy) * (1 - smoothstep(0.021, 0.029, dy)),
                       "Ring": smoothstep(0.021, 0.029, dy)}
                for f, m in mem.items():
                    if m <= 0:
                        continue
                    s_ = max(0.0, (x - x_k) / (tips[f] - x_k))
                    for k, (a, b) in enumerate(((-1, 0.38), (0.38, 0.69), (0.69, 9))):
                        lo_, hi_ = smoothstep(a - 0.06, a + 0.06, s_) if a > 0 else 1.0, 1 - smoothstep(b - 0.06, b + 0.06, s_) if b < 9 else 1.0
                        ww = lo_ * hi_
                        if ww > 0:
                            n = P + side + f"Hand{f}{k + 1}"
                            new[n] = new.get(n, 0) + rest * fing * m * ww
            tot = sum(new.values())
            new[P + side + "Hand"] = max(0.0, 1 - tot)
            for n in hb:
                if n in wv:
                    body.vertex_groups[n].remove([i])
            for n, x_ in new.items():
                if x_ * share > 1e-4:
                    grp(n).add([i], x_ * share, "ADD")
            moved += 1
        rep[side] = {"verts": moved, "tips": {k: round(t, 4) for k, t in tips.items()}, "thumb_len": round(L, 4)}
    # keep 4 influences, normalised
    for v in me.vertices:
        ws = sorted(((g.group, g.weight) for g in v.groups), key=lambda t: -t[1])
        keep = ws[:4]; tot = sum(w for _, w in keep) or 1
        for gi, _ in ws[4:]:
            body.vertex_groups[gi].remove([v.index])
        for gi, w in keep:
            body.vertex_groups[gi].add([v.index], w / tot, "REPLACE")
    return rep

# --- 5b. part the index (off by default; part_index=1): Tripo's hand is a mitten (index, middle and ring joined by
# grooves from the knuckles to the tips), so a finger gun stretches the skin between the straight index and the curled
# middle (worst triangle 19x in finger_check's pose). This cuts the index-middle groove, moves each cut side's weights
# to its own finger and caps each side with a fan: the stretch drops to 2.4x, but in Unity the flat caps showed as
# light torn-looking plates under the index (rig-build-v14, 2026-10-27), worse than the stretch. Real fix: a model
# whose fingers are separate.
def part_index_step():
    gname = {g.index: g.name for g in body.vertex_groups}
    bm = bmesh.new(); bm.from_mesh(me)
    deform = bm.verts.layers.deform.active
    uv = bm.loops.layers.uv.active
    def label(v):
        d = v[deform]
        if not d:
            return None
        n = gname[max(d.items(), key=lambda t: t[1])[0]].split(":")[-1]
        for side in ("Left", "Right"):
            for f in ("Index", "Middle"):
                if n.startswith(side + "Hand" + f):
                    return side, f
        return None
    vl = {v: label(v) for v in bm.verts}
    def face_label(f):
        ls = [vl[v] for v in f.verts if vl[v]]
        return max(set(ls), key=ls.count) if ls else None
    fl = {f: face_label(f) for f in bm.faces}
    rep = {}
    for side in ("Left", "Right"):
        seam = [e for e in bm.edges if len(e.link_faces) == 2
                and {fl[e.link_faces[0]], fl[e.link_faces[1]]} == {(side, "Index"), (side, "Middle")}]
        if not seam:
            continue
        bmesh.ops.split_edges(bm, edges=seam)
        bm.verts.ensure_lookup_table()
        # A cut side keeps vertices weighted to the other finger (a groove triangle has corners on both); move their
        # weights to the same joint of their side's finger, or the cut skin still stretches across (19x before).
        gidx = {n: body.vertex_groups[n].index for n in (P + side + f"Hand{f}{k}" for f in ("Index", "Middle")
                                                          for k in (1, 2, 3)) if n in body.vertex_groups}
        swapped = 0
        for v in bm.verts:
            sides = {fl.get(f) for f in v.link_faces} - {None}
            if len(sides) != 1:
                continue
            (s_, mine), = sides
            if s_ != side:
                continue
            theirs = "Middle" if mine == "Index" else "Index"
            d = v[deform]
            moved_any = False
            for k in (1, 2, 3):
                a, b = gidx.get(P + side + f"Hand{theirs}{k}"), gidx.get(P + side + f"Hand{mine}{k}")
                if a is not None and b is not None and a in d:
                    d[b] = d.get(b, 0.0) + d[a]
                    del d[a]
                    moved_any = True
            swapped += moved_any
        borders, centres = {}, {}
        for f in ("Index", "Middle"):
            borders[f] = [e for e in bm.edges if e.is_boundary and fl.get(e.link_faces[0]) == (side, f)]
            verts = {v for e in borders[f] for v in e.verts}
            centres[f] = sum((v.co for v in verts), Vector()) / len(verts)
            rep.setdefault(side, {})[f] = len(borders[f])
        rep[side]["reweighted"] = swapped
        for f, other in (("Index", "Middle"), ("Middle", "Index")):
            border = borders[f]
            verts = {v for e in border for v in e.verts}
            c = bm.verts.new(centres[f])
            # the cap vertex follows the cut side: its weights are the side's mean
            acc = {}
            for v in verts:
                for g, w in v[deform].items():
                    acc[g] = acc.get(g, 0.0) + w / len(verts)
            for g, w in acc.items():
                c[deform][g] = w
            outward = (centres[other] - centres[f]).normalized()
            for e in border:
                a, b = e.verts
                face = bm.faces.new((a, b, c))
                if face.normal.dot(outward) < 0:
                    face.normal_flip()
                # UVs: each corner copies a UV the vertex already has (the cap sits between the fingers, out of view)
                donor = e.link_faces[0]
                for loop in face.loops:
                    src = next((l for l in loop.vert.link_loops if l.face is not face), None) or donor.loops[0]
                    loop[uv].uv = src[uv].uv
                fl[face] = (side, f)
    bm.to_mesh(me); bm.free(); me.update()
    return rep

# --- 5c. separate fingers (owner, 2026-10-27): Tripo's hand is a mitten, so the index, middle and ring fingers beyond
# the knuckles are rebuilt as three rounded tubes along their bone chains, each fitted slice by slice to the finger it
# replaces and narrowed so a gap opens between them. Each tube vertex takes the texture of the nearest point of its old
# finger, and the weights follow the chain the way finger_step weighs the old ones. The tube bases sink into the palm,
# whose front is closed where the fingers left it. The thumb was already separate.
def rebuild_fingers_step():
    from mathutils.geometry import barycentric_transform
    gname = {g.index: g.name for g in body.vertex_groups}
    gidx = {g.name: g.index for g in body.vertex_groups}
    narrow = float(opt.get("finger_narrow", 0.92))
    bm = bmesh.new(); bm.from_mesh(me)
    deform = bm.verts.layers.deform.verify()
    uv = bm.loops.layers.uv.active
    MWbi = MWb.inverted()
    world = {v: MWb @ v.co for v in bm.verts}
    def dominant(v):
        d = v[deform]
        return gname[max(d.items(), key=lambda t: t[1])[0]].split(":")[-1] if d else ""
    dom = {v: dominant(v) for v in bm.verts}
    rep = {}
    new_faces = []
    material = 0
    for side, sgn in (("Left", 1), ("Right", -1)):
        hand = joint(side + "Hand")
        x_k = float(opt.get("knuckle", 0.065))
        fingers = {}
        for f in ("Index", "Middle", "Ring"):
            names = [side + f"Hand{f}{k}" for k in (1, 2, 3)]
            faces = [fc for fc in bm.faces if all(dom.get(v, "") in names for v in fc.verts)]
            if not faces:
                continue
            fingers[f] = faces
        # the old fingers: faces whose corners all belong to the three finger chains, beyond the knuckles
        chain = {side + f"Hand{f}{k}" for f in ("Index", "Middle", "Ring") for k in (1, 2, 3)}
        old = [fc for fc in bm.faces if all(dom.get(v, "") in chain for v in fc.verts)
                and all((world[v].x - hand.x) * sgn > x_k - 0.004 for v in fc.verts)]
        if not old:
            continue
        material = old[0].material_index
        tubes = {}
        for f, faces in fingers.items():
            k1, tip = joint(side + f"Hand{f}1"), joint(side + f"Hand{f}4")
            axis = (tip - k1); L = axis.length; axis.normalize()
            across = Vector((0, 1, 0)); across = (across - axis * across.dot(axis)).normalized()
            up = axis.cross(across).normalized()
            verts = {v for fc in faces for v in fc.verts}
            pts = [(((world[v] - k1).dot(axis)) / L, (world[v] - k1).dot(across), (world[v] - k1).dot(up)) for v in verts]
            # slice the old finger: centre and half extents across and up, smoothed along the length
            bins = np.linspace(0.0, 1.0, 9)
            rows = []
            for a, b in zip(bins[:-1], bins[1:]):
                sl = [p for p in pts if a <= p[0] < b]
                if len(sl) >= 3:
                    u = [p[1] for p in sl]; w_ = [p[2] for p in sl]
                    rows.append(((a + b) / 2, (max(u) + min(u)) / 2, (max(w_) + min(w_)) / 2,
                                 (max(u) - min(u)) / 2, (max(w_) - min(w_)) / 2))
            rows = np.array(rows)
            # the triangles of this old finger, for the texture lookup
            tris = []  # (corner positions, corner UVs), cached: the faces are deleted below
            for fc in faces:
                ls = list(fc.loops)
                for i in range(1, len(ls) - 1):
                    t = (ls[0], ls[i], ls[i + 1])
                    tris.append(([world[l.vert].copy() for l in t], [l[uv].uv.copy() for l in t]))
            if len(rows) < 2 or not tris:
                continue
            tree = BVHTree.FromPolygons([p for t in tris for p in t[0]], [(3 * i, 3 * i + 1, 3 * i + 2) for i in range(len(tris))])
            tubes[f] = (k1, axis, across, up, L, rows, tris, tree)
        # remove the old fingers and close the palm front
        bmesh.ops.delete(bm, geom=old, context="FACES")
        loose = [v for v in bm.verts if not v.link_faces]
        bmesh.ops.delete(bm, geom=loose, context="VERTS")
        border = [e for e in bm.edges if e.is_boundary and all(abs(world.get(v, MWb @ v.co).x - hand.x) < 0.2 for v in e.verts)
                  and all((world.get(v, MWb @ v.co).x - hand.x) * sgn > x_k - 0.03 for v in e.verts)]
        filled = bmesh.ops.holes_fill(bm, edges=border, sides=0)["faces"]
        filled = bmesh.ops.triangulate(bm, faces=filled)["faces"]
        for fc in filled:
            fc.material_index = material; fc.smooth = True
        front = list({v for fc in filled for v in fc.verts})
        for _ in range(4):  # round the patched palm front a little
            bmesh.ops.smooth_vert(bm, verts=front, factor=0.5, use_axis_x=True, use_axis_y=True, use_axis_z=True)
        # build the tubes
        ring_s = [-0.32, -0.16, 0.0, 0.1, 0.2, 0.3, 0.38, 0.46, 0.54, 0.62, 0.69, 0.76, 0.83, 0.89, 0.94, 0.975]
        k1s = {f: tubes[f][0] for f in tubes}
        order = [f for f in ("Index", "Middle", "Ring") if f in k1s]
        mean_spacing = sum((k1s[a] - k1s[b]).length for a, b in zip(order, order[1:])) / max(1, len(order) - 1)
        M = 12
        for f, (k1, axis, across, up, L, rows, tris, tree) in tubes.items():
            # a smooth tube on the bone chain: its thickness is the old finger's median, its width the hand's mean
            # finger spacing, so all three keep the mitten's chunky look (slice-by-slice fits of the sparse mesh came
            # out sawtoothed; widths from each finger's own spacing left the middle and ring 1.6 cm thin, 2026-10-27)
            b0 = float(np.median(rows[:, 4]))
            a0 = min(narrow * mean_spacing / 2, 1.25 * b0)
            def shape(s):
                taper = 1.0 - 0.12 * max(0.0, s)
                r_u, r_w = a0 * taper, b0 * taper
                if s > 0.8:  # round the tip
                    k = math.sqrt(max(0.0, 1 - ((s - 0.8) / 0.2) ** 2))
                    r_u, r_w = r_u * k, r_w * k
                return 0.0, 0.0, r_u, r_w
            def weights(s):
                w = {side + "Hand": 1 - smoothstep(-0.12, 0.02, s)}
                rest = 1 - w[side + "Hand"]
                for k, (a, b) in enumerate(((-1, 0.38), (0.38, 0.69), (0.69, 9))):
                    lo = smoothstep(a - 0.06, a + 0.06, s) if a > 0 else 1.0
                    hi = 1 - smoothstep(b - 0.06, b + 0.06, s) if b < 9 else 1.0
                    if lo * hi > 0:
                        w[side + f"Hand{f}{k + 1}"] = rest * lo * hi
                t = sum(w.values())
                return {n: x / t for n, x in w.items() if x / t > 1e-3}
            rings = []
            for s in ring_s:
                c_u, c_w, r_u, r_w = shape(s)
                ring = []
                for j in range(M):
                    a = 2 * math.pi * j / M
                    p = k1 + axis * (s * L) + across * (c_u + r_u * math.cos(a)) + up * (c_w + r_w * math.sin(a))
                    v = bm.verts.new(MWbi @ p); world[v] = p
                    for n, x in weights(s).items():
                        v[deform][gidx[P + n]] = x
                    ring.append(v)
                rings.append(ring)
            c_u, c_w, _, _ = shape(1.0)
            tip_v = bm.verts.new(MWbi @ (k1 + axis * L + across * c_u + up * c_w)); world[tip_v] = k1 + axis * L + across * c_u + up * c_w
            for n, x in weights(1.0).items():
                tip_v[deform][gidx[P + n]] = x
            faces = []
            for r0, r1 in zip(rings[:-1], rings[1:]):
                for j in range(M):
                    faces.append(bm.faces.new((r0[j], r0[(j + 1) % M], r1[(j + 1) % M], r1[j])))
            for j in range(M):
                faces.append(bm.faces.new((rings[-1][j], rings[-1][(j + 1) % M], tip_v)))
            # outward: the tube's faces point away from its axis
            bm.normal_update()
            probe = faces[len(faces) // 2]
            centre = k1 + axis * (axis.dot(MWb @ probe.calc_center_median() - k1))
            if (MWb.to_3x3() @ probe.normal).dot((MWb @ probe.calc_center_median()) - centre) < 0:
                for fc in faces:
                    fc.normal_flip()
            # texture: each face maps onto the old triangle nearest its centre, all corners through that one triangle,
            # so a face stays inside one texture island (corners mapped one by one could land on different islands
            # and smear the atlas across the face)
            for fc in faces:
                fc.material_index = material; fc.smooth = True
                centre = sum((world[v] for v in fc.verts), Vector()) / len(fc.verts)
                _, _, i, _ = tree.find_nearest(centre)
                corners, uvs = tris[i]
                uv3 = [Vector((u.x, u.y, 0.0)) for u in uvs]
                for l in fc.loops:
                    q = barycentric_transform(world[l.vert], *corners, *uv3)
                    l[uv].uv = (q.x, q.y)
            new_faces += faces
            rep.setdefault(side, {})[f] = {"length_m": round(L, 4), "width_m": round(2 * a0, 4), "thickness_m": round(2 * b0, 4)}
        rep.setdefault(side, {})["removed_faces"] = len(old)
        rep[side]["palm_fill_faces"] = len(filled)
    bm.to_mesh(me); bm.free(); me.update()
    return rep

if opt.get("fingers", "1") == "1":
    MWb = body.matrix_world.copy()
    report["fingers"] = finger_step()
    if opt.get("part_index", "0") == "1":
        report["part_index"] = part_index_step()
    if opt.get("rebuild_fingers", "1") == "1":
        report["rebuild_fingers"] = rebuild_fingers_step()

# --- body skin under the jeans goes (it would only poke through) -------------------------------------------
if covered:
    bmc = bmesh.new(); bmc.from_mesh(me); bmc.faces.ensure_lookup_table()
    gone = [f for f in bmc.faces if all(v.index in covered for v in f.verts)]
    bmesh.ops.delete(bmc, geom=gone, context="FACES")
    bmesh.ops.delete(bmc, geom=[v for v in bmc.verts if not v.link_faces], context="VERTS")
    bmc.to_mesh(me); bmc.free(); me.update()
    report["jeans"]["body_faces_removed"] = len(gone)

# --- material, texture, export ----------------------------------------------------------------------
img = bpy.data.images.load(TEX)
if img.size[0] != TEX_SIZE:
    img.scale(TEX_SIZE, TEX_SIZE)
dst_tex = os.path.join(OUT, "textures", f"{NAME}_Body.png")
img.filepath_raw = dst_tex; img.file_format = "PNG"; img.save()
mat = bpy.data.materials.new(f"{NAME}_Body"); mat.use_nodes = True
tn = mat.node_tree.nodes.new("ShaderNodeTexImage"); tn.image = img
mat.node_tree.links.new(tn.outputs["Color"], mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
for o in meshes:
    o.data.materials.clear(); o.data.materials.append(mat)
if opt.get("poncho_tex"):
    pdst = os.path.join(OUT, "textures", f"{NAME}_Poncho.png")
    psrc = os.path.abspath(opt["poncho_tex"])
    if psrc != pdst:
        shutil.copy2(psrc, pdst)
    pimg = bpy.data.images.load(pdst, check_existing=False)
    pmat = bpy.data.materials.get("Salamander_Poncho") or bpy.data.materials.new("Salamander_Poncho")
    pmat.use_nodes = True
    pmat.use_backface_culling = False
    pbs = pmat.node_tree.nodes["Principled BSDF"]
    pbs.inputs["Roughness"].default_value = 0.92
    pbs.inputs["Specular IOR Level"].default_value = 0.18
    tn = next((n for n in pmat.node_tree.nodes if n.type == "TEX_IMAGE"), None)
    tn = tn or pmat.node_tree.nodes.new("ShaderNodeTexImage"); tn.image = pimg
    pmat.node_tree.links.new(tn.outputs["Color"], pbs.inputs["Base Color"])
    poncho.data.materials.clear(); poncho.data.materials.append(pmat)
    report["poncho"]["texture"] = pdst
if jeans:
    jimg = bpy.data.images.load(os.path.abspath(opt["jeans_tex"]))
    if jimg.size[0] != TEX_SIZE:
        jimg.scale(TEX_SIZE, TEX_SIZE)
    jdst = os.path.join(OUT, "textures", f"{NAME}_Jeans.png")
    jimg.filepath_raw = jdst; jimg.file_format = "PNG"; jimg.save()
    jmat = bpy.data.materials.new(f"{NAME}_Jeans"); jmat.use_nodes = True
    tn = jmat.node_tree.nodes.new("ShaderNodeTexImage"); tn.image = jimg
    jmat.node_tree.links.new(tn.outputs["Color"], jmat.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
    jeans.data.materials.clear(); jeans.data.materials.append(jmat)
    report["jeans"]["texture"] = jdst
body.name = body.data.name = f"{NAME}_Body"
arm.name = "Armature"
bpy.ops.object.select_all(action="DESELECT")
arm.select_set(True)
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = arm
fbx = os.path.join(OUT, f"{NAME}.fbx")
bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True, object_types={"ARMATURE", "MESH"}, add_leaf_bones=False,
                         bake_anim=False, path_mode="STRIP", embed_textures=False, mesh_smooth_type="OFF",
                         use_armature_deform_only=False, armature_nodetype="NULL")
blend = os.path.join(OUT, f"{NAME}_rig.blend")
bpy.ops.wm.save_as_mainfile(filepath=blend)
report.update({"fbx": fbx, "blend": blend, "texture": dst_tex, "bones": len(arm.data.bones),
               "verts": {o.name: len(o.data.vertices) for o in meshes}})
json.dump(report, open(os.path.join(OUT, f"{NAME}_report.json"), "w"), indent=1, default=str)
print("REPORT", json.dumps(report, default=str))
