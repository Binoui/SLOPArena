"""Build Dusty's live model from the owner's Tripo v2 FBX (2026-11-05): the salamander rig contract on the new mesh.

systemd-run --user --scope -q -p MemoryMax=3G -p MemorySwapMax=1G blender -b --factory-startup --python-exit-code 1 \
  --python tools/blender/salamander_rig/build_v2.py -- <tripo_v2.fbx> <poncho salamander.fbx> <out_dir> [key=value ...]

The second FBX is the poncho model the v2 replaced (artifacts/salamander-model-v2/backup/Models/salamander.fbx), read
only: its hip height, bone names and parents, and the rest frames of Head, Hips, Hat and Mustache. Do not pass the
installed v2 FBX: its hips already carry the size factor, so a rebuild would scale twice.
Writes <out_dir>/salamander.fbx (no embedded media), textures/Salamander_Body.png, salamander_rig.blend, report.json and
pieces.json. See README.md ("Tripo v2 model") for why each step exists. Options: hat_subdiv=0|1, tail_bones=5,
lasso_hips=0.75 (the lasso's Hips share, the rest RightUpLeg), knuckle_back=0.008 (MCP joints behind the web, m),
size=1.25 (uniform scale on top of the hip match, applied last), spurs=1 (a steel spur on each boot heel).
"""
import bpy, bmesh, sys, os, json, math, heapq
import numpy as np
from mathutils import Vector, Matrix
from mathutils.kdtree import KDTree

argv = sys.argv[sys.argv.index("--") + 1:]
TRIPO, LIVE, OUT = (os.path.abspath(a) for a in argv[:3])
opt = dict(a.split("=", 1) for a in argv[3:])
NTAIL = int(opt.get("tail_bones", 5))
HAT_SUBDIV = int(opt.get("hat_subdiv", 0))
LASSO_HIPS = float(opt.get("lasso_hips", 0.75))
KNUCKLE_BACK = float(opt.get("knuckle_back", 0.008))
# His hips match the poncho rig's (0.434 m) during the build, so every fitting length below keeps its meaning. That left
# him short: idle head 1.063 m in game against FightGuy's 1.328 m (cooked idle frame 0). The owner asked for his old
# height (2026-11-05); 1.25 puts the idle head at 1.329 m. The whole result is scaled once, at the end.
SIZE = float(opt.get("size", 1.25))
SPURS = int(opt.get("spurs", 1))
P = "mixamorig:"
os.makedirs(os.path.join(OUT, "textures"), exist_ok=True)
report = {"tripo": TRIPO, "live": LIVE, "options": opt}


def smoothstep(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a))) if b != a else float(x >= a)
    return t * t * (3 - 2 * t)


def norm(w):
    w = {k: v for k, v in w.items() if v > 1e-6}
    s = sum(w.values())
    return {k: v / s for k, v in w.items()} if s > 0 else w


# --- 0. the live rig: hip height, bone contract, rest frames -------------------------------------------------
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=LIVE, ignore_leaf_bones=False)
la = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
LAW = la.matrix_world.copy()
LIVE_BONES = {b.name: (b.parent.name if b.parent else None) for b in la.data.bones}
LIVE_ROT = {b.name: (LAW @ b.matrix_local).to_3x3().normalized() for b in la.data.bones}
LIVE_HEAD = {b.name: LAW @ b.head_local for b in la.data.bones}
live_meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
dg = bpy.context.evaluated_depsgraph_get()
lpts = np.array([list(o.matrix_world @ v.co) for o in live_meshes for v in o.data.vertices])
lbody = np.array([list(o.matrix_world @ v.co) for o in live_meshes if o.name.endswith("_Body") for v in o.data.vertices])
report["live_heights"] = {"hips": LIVE_HEAD[P + "Hips"].z, "head_joint": LIVE_HEAD[P + "Head"].z,
                          "body_top": float(lbody[:, 2].max()), "with_hat": float(lpts[:, 2].max()), "bones": len(LIVE_BONES)}

# --- 1. Tripo: Z-up, the live hip height, no take --------------------------------------------------------------
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=TRIPO, ignore_leaf_bones=False)
scene = bpy.context.scene
arm = next(o for o in scene.objects if o.type == "ARMATURE")
body = next(o for o in scene.objects if o.type == "MESH")
arm.animation_data_clear()
for a in list(bpy.data.actions):
    bpy.data.actions.remove(a)
for pb in arm.pose.bones:
    pb.matrix_basis = Matrix()
arm.data.pose_position = "REST"
AW = arm.matrix_world.copy()
AWi = AW.inverted()
# Tripo writes its Z-up data into a Y-up file: in Blender his height runs along -Y. Rotate it upright and scale it so the
# Hips joint sits at the live rig's height (clips, reach and hurtboxes stay comparable).
hips_y = (AW @ arm.data.bones[P + "Hips"].head_local).y
S = report["live_heights"]["hips"] / -hips_y
W = Matrix.Scale(S, 4) @ Matrix.Rotation(-math.pi / 2, 4, "X")
arm.data.transform(AWi @ W @ AW)
body.data.transform(W @ body.matrix_world)
body.matrix_world = Matrix.Identity(4)  # the live meshes sit at identity under the armature
me = body.data
with bpy.context.temp_override(object=body, active_object=body, selected_objects=[body]):
    bpy.ops.mesh.customdata_custom_splitnormals_clear()  # Tripo's custom normals equal the smooth vertex normals
for e in me.edges:
    e.use_edge_sharp = False
for p in me.polygons:
    p.use_smooth = True
bpy.context.view_layer.update()
report["scale"] = S


def bone_head(n):
    return AW @ arm.data.bones[n].head_local


# --- 2. pieces -------------------------------------------------------------------------------------------------
def pieces_of(mesh):
    bm = bmesh.new(); bm.from_mesh(mesh); bm.verts.ensure_lookup_table()
    comp = np.full(len(bm.verts), -1)
    out = []
    for v in bm.verts:
        if comp[v.index] >= 0:
            continue
        st = [v]; comp[v.index] = len(out); cl = []
        while st:
            x = st.pop(); cl.append(x.index)
            for e in x.link_edges:
                y = e.other_vert(x)
                if comp[y.index] < 0:
                    comp[y.index] = len(out); st.append(y)
        out.append(np.array(cl))
    bm.free()
    return out


def group_shares(idx):
    gn = {g.index: g.name for g in body.vertex_groups}
    tot = {}
    for i in idx:
        for g in me.vertices[i].groups:
            tot[gn[g.group]] = tot.get(gn[g.group], 0) + g.weight
    s = sum(tot.values()) or 1
    return {k: v / s for k, v in tot.items()}


def classify():
    V = np.array([list(v.co) for v in me.vertices])
    H, N, HIP = bone_head(P + "Head"), bone_head(P + "Neck"), bone_head(P + "Hips")
    info = []
    for k, idx in enumerate(pieces_of(me)):
        p = V[idx]
        sh = group_shares(idx)
        info.append({"k": k, "idx": idx, "n": len(idx), "lo": p.min(0), "hi": p.max(0), "c": p.mean(0), "sh": sh})
    role = {}
    headish = [d for d in info if d["sh"].get(P + "Head", 0) > 0.85 and d["c"][2] > N.z]
    hat = max(headish, key=lambda d: d["hi"][2] - (0 if d["hi"][0] - d["lo"][0] > 0.25 else 9))
    role[hat["k"]] = "hat"
    for d in headish:
        if d is hat:
            continue
        if d["hi"][0] - d["lo"][0] > 0.1 and d["lo"][2] > H.z + 0.05 and abs(d["c"][0]) < 0.03:
            role[d["k"]] = "hat_band"
    rest = [d for d in headish if d["k"] not in role]
    straw = max(rest, key=lambda d: d["n"])
    assert abs(straw["c"][0]) > 0.04, "straw should stick out sideways"
    role[straw["k"]] = "straw"
    rest = [d for d in rest if d is not straw]
    mus = min((d for d in rest if d["lo"][0] < -0.02 and d["hi"][0] > 0.02), key=lambda d: d["lo"][1])
    role[mus["k"]] = "mustache"
    for d in rest:
        if d["k"] not in role:
            role[d["k"]] = "face"
    for d in info:
        if d["k"] in role:
            continue
        hand = {s: sum(v for g, v in d["sh"].items() if g.startswith(P + s + "Hand")) for s in ("Left", "Right")}
        if max(hand.values()) > 0.3:
            role[d["k"]] = "arm_" + max(hand, key=hand.get)
    low = [d for d in info if d["k"] not in role and d["c"][2] < HIP.z]
    tail = max(low, key=lambda d: d["hi"][1])
    assert tail["hi"][1] > HIP.y + 0.1, "the tail should reach well behind him"
    role[tail["k"]] = "tail"
    body_skin = max((d for d in info if d["k"] not in role), key=lambda d: d["sh"].get(P + "Head", 0) * (d["hi"][2] - d["lo"][2]))
    role[body_skin["k"]] = "body"
    jeans = max((d for d in info if d["k"] not in role and d["c"][2] < HIP.z),
                key=lambda d: (d["hi"][2] - d["lo"][2]) * (d["hi"][0] - d["lo"][0]))
    role[jeans["k"]] = "jeans"
    lasso = max((d for d in info if d["k"] not in role and abs(d["c"][0]) > 0.03 and d["c"][2] < HIP.z), key=lambda d: d["n"])
    assert lasso["n"] > 1000, "the lasso coil is a big piece on his hip"
    role[lasso["k"]] = "lasso"
    for d in info:
        if d["k"] in role:
            continue
        sp = d["sh"]
        legs = sum(v for g, v in sp.items() if "Leg" in g or "Foot" in g or "Toe" in g)
        armw = sum(v for g, v in sp.items() if "Arm" in g)
        if d["c"][2] < 0.25 and legs > 0.5:
            role[d["k"]] = "boot"
        elif armw > 0.6 and d["n"] < 200:
            role[d["k"]] = "cuff"
        elif d["hi"][2] < HIP.z + 0.01 and d["n"] < 400:
            role[d["k"]] = "belt_part"  # belt, loops, buckle, keeper, knee patch: follow the jeans under them
        else:
            role[d["k"]] = "torso_cloth"  # jacket, shirt collar, neckerchief
    return info, role


info, ROLE = classify()
PIECE_OF = np.zeros(len(me.vertices), int)
for d in info:
    PIECE_OF[d["idx"]] = d["k"]


def verts(*roles):
    return np.concatenate([d["idx"] for d in info if ROLE[d["k"]] in roles]) if any(ROLE[d["k"]] in roles for d in info) else np.array([], int)


report["pieces"] = [{"k": d["k"], "role": ROLE[d["k"]], "verts": int(d["n"]), "centre": [round(float(x), 3) for x in d["c"]],
                     "lo": [round(float(x), 3) for x in d["lo"]], "hi": [round(float(x), 3) for x in d["hi"]]} for d in info]
json.dump(report["pieces"], open(os.path.join(OUT, "pieces.json"), "w"), indent=1)
print("ROLES", sorted((ROLE[d["k"]], d["n"]) for d in info))

# --- 3. optional hat smoothing (low-poly brim facets) ------------------------------------------------------------
if HAT_SUBDIV > 0:
    # Smooth subdivision keeps every original vertex and lays the new ones on a smooth surface, so nothing shrinks.
    hat_set = set(verts("hat", "hat_band").tolist())
    bm = bmesh.new(); bm.from_mesh(me)
    for _ in range(HAT_SUBDIV):
        bm.verts.index_update(); bm.verts.ensure_lookup_table()
        edges = [e for e in bm.edges if e.verts[0].index in hat_set and e.verts[1].index in hat_set]
        res = bmesh.ops.subdivide_edges(bm, edges=edges, cuts=1, use_grid_fill=True, smooth=1.0)
        bm.verts.index_update()
        hat_set |= {g.index for g in res["geom_inner"] + res["geom_split"] if isinstance(g, bmesh.types.BMVert)}
    bm.to_mesh(me); bm.free(); me.update()
    info, ROLE = classify()
    PIECE_OF = np.zeros(len(me.vertices), int)
    for d in info:
        PIECE_OF[d["idx"]] = d["k"]

V = [Vector(v.co) for v in me.vertices]
Vn = np.array([list(v) for v in V])
GN = {g.index: g.name for g in body.vertex_groups}
WT = [{GN[g.group]: g.weight for g in v.groups if g.weight > 1e-5} for v in me.vertices]
report["tripo_weights_over_0.2"] = {n: int(sum(1 for w in WT if w.get(n, 0) > 0.2)) for n in (P + "Spine", P + "Spine1", P + "Spine2", P + "Neck", P + "Head")}

# --- 4. pronate the hands: Tripo's T-pose has thumbs up and palms forward ---------------------------------------
# Unity Humanoid takes the rest pose as every muscle's zero, so his live clips (authored on a palms-down rest) would roll
# both hands about 90 degrees: in a Unity test on the oriented Tripo rig the palm normals were 83-161 degrees off the live
# rig's in idle, Finger Guns, Big Iron and the Haymaker. Each forearm is twisted gradually (none at the elbow, all of it at
# the wrist) and the hand turned rigidly, so the rest has palms down and thumbs forward like the live rig. This edits
# the rest mesh directly; nothing is solved back through the skinning.
EDIT = {}


def edit_mode():
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = arm; arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    return arm.data.edit_bones


def object_mode():
    bpy.ops.object.mode_set(mode="OBJECT")


def scan_fingers(side, idx):
    """Finger pieces beyond the web: the hand vertices past an axial threshold split into 4 islands (plus the thumb's)."""
    E, Wr = bone_head(P + side + "ForeArm"), bone_head(P + side + "Hand")
    a = (Wr - E).normalized()
    sel_all = [i for i in idx if (V[i] - Wr).dot(a) > 0]
    adj = {}
    for e in me.edges:
        i, j = e.vertices
        adj.setdefault(i, []).append(j); adj.setdefault(j, []).append(i)
    thumb = {i for i in sel_all if sum(w for g, w in WT[i].items() if "Thumb" in g) > 0.5}
    best = None
    for k in range(100):
        thr = 0.04 + 0.0025 * k
        sel = {i for i in sel_all if (V[i] - Wr).dot(a) > thr and i not in thumb}
        seen, comps = set(), []
        for i in sel:
            if i in seen:
                continue
            st, cl = [i], []
            seen.add(i)
            while st:
                x = st.pop(); cl.append(x)
                for y in adj.get(x, []):
                    if y in sel and y not in seen:
                        seen.add(y); st.append(y)
            comps.append(cl)
        big = [c for c in comps if len(c) > 25]
        if len(big) == 4:
            best = (thr, big)
            break
    assert best, side + ": the hand never splits into four fingers"
    return a, Wr, E, best[0], best[1], thumb


def pronate(side):
    idx = verts("arm_" + side)
    a, Wr, E, thr, fingers, _ = scan_fingers(side, idx)
    cents = [sum((V[i] for i in f), Vector()) / len(f) for f in fingers]
    cm = sum(cents, Vector()) / 4
    C = np.array([list(c - cm) for c in cents])
    _, _, vt = np.linalg.svd(C)
    s = Vector(vt[0]); s = (s - a * s.dot(a)).normalized()
    thumb_c = bone_head(P + side + "HandThumb2")
    index_first = sorted(range(4), key=lambda k: (cents[k] - thumb_c).length)
    if s.dot(cents[index_first[-1]] - cents[index_first[0]]) < 0:
        s = -s  # index -> little
    t = Vector((0, 1, 0)); t = (t - a * t.dot(a)).normalized()  # the live rig: index forward, the others behind it
    ang = math.atan2(a.dot(s.cross(t)), s.dot(t))
    lf = (Wr - E).length
    for i in idx:
        tau = (V[i] - E).dot(a) / lf
        f = smoothstep(0.1, 0.95, tau)
        if f <= 0:
            continue
        R = Matrix.Rotation(ang * f, 3, a)
        V[i] = E + R @ (V[i] - E)
    RW = Matrix.Translation(E) @ Matrix.Rotation(ang, 4, a) @ Matrix.Translation(-E)
    RA = AWi @ RW @ AW
    eb = edit_mode()
    hand = eb[P + side + "Hand"]
    for b in [hand] + list(hand.children_recursive):
        b.use_connect = False
    for b in [hand] + list(hand.children_recursive):
        b.matrix = RA @ b.matrix
    object_mode()
    return {"angle_deg": round(math.degrees(ang), 2), "split_at_m": round(thr, 4)}


report["pronation"] = {s: pronate(s) for s in ("Left", "Right")}
for i, v in enumerate(me.vertices):
    v.co = V[i]
me.update()
print("PRONATION", report["pronation"])

# --- 5. finger chains: index, middle, ring and a new pinky from the four finger pieces ---------------------------
FINGERS = ("Index", "Middle", "Ring", "Pinky")


def finger_step(side):
    idx = verts("arm_" + side)
    a, Wr, E, thr, comps, thumb = scan_fingers(side, idx)
    cents = [sum((V[i] for i in c), Vector()) / len(c) for c in comps]
    thumb_c = bone_head(P + side + "HandThumb2")
    order = sorted(range(4), key=lambda k: (cents[k] - thumb_c).length)
    order = [order[0]] + sorted(order[1:], key=lambda k: (cents[k] - cents[order[0]]).length)
    lines = {}
    for name, k in zip(FINGERS, order):
        pts = np.array([list(V[i]) for i in comps[k] if (V[i] - Wr).dot(a) > thr + 0.004])
        # the finger's centreline: centroids of 4 mm slices across the hand axis (a short, flat finger's PCA axis can
        # lie across it)
        sa = (pts - np.array(list(Wr))) @ np.array(list(a))
        cents_s = [pts[(sa >= b0) & (sa < b0 + 0.004)].mean(0) for b0 in np.arange(sa.min(), sa.max(), 0.004)
                   if ((sa >= b0) & (sa < b0 + 0.004)).sum() >= 6]
        cs = np.array(cents_s)
        c = cs.mean(0)
        _, _, vt = np.linalg.svd(cs - c)
        d = Vector(vt[0]); d = d if d.dot(a) > 0 else -d
        if len(cs) < 3 or d.dot(a) < 0.9:
            d = a.copy()
        c = Vector(c)
        proj = [(Vector(p) - c).dot(d) for p in pts]
        # MCP: where the finger's line crosses the web plane, moved back into the palm
        t0 = (Wr + a * (thr - KNUCKLE_BACK) - c).dot(a) / d.dot(a)
        mcp = c + d * t0
        tip = c + d * max(proj)
        lines[name] = {"verts": comps[k], "mcp": mcp, "tip": tip, "dir": d, "len": (tip - mcp).length}
    # The pinky is unmapped in Humanoid; the prefab gives each Pinky bone a RotationConstraint on its Ring bone. With the
    # same rest frames the constraint needs no offset, so the pinky chain runs along the ring finger's direction (the two
    # fingers are parallel; the joints move under 3 mm).
    lines["Pinky"]["dir"] = lines["Ring"]["dir"]
    eb = edit_mode()
    for name, L in lines.items():
        fr = [0.0, 0.42, 0.72, 1.0, 1.15]
        pts = [L["mcp"] + L["dir"] * (L["len"] * r) for r in fr]
        prev = eb[P + side + "Hand"]
        for k in range(4):
            n = P + side + f"Hand{name}{k + 1}"
            b = eb[n] if n in eb else eb.new(n)
            b.head, b.tail = AWi @ pts[k], AWi @ pts[k + 1]
            b.parent = prev; b.use_connect = k > 0; b.use_deform = k < 3
            b.align_roll(AWi.to_3x3() @ Vector((0, -1, 0)))
            prev = b
    for b in eb:
        if b.name.startswith(P + side + "HandThumb"):
            b.align_roll(AWi.to_3x3() @ Vector((0, -1, 0)))
    object_mode()
    # weights: the hand's share of every hand vertex goes to the palm or one finger chain; the thumb keeps Tripo's
    member = {}
    for name in FINGERS:
        for i in lines[name]["verts"]:
            member[i] = {name: 1.0}
    stack = Vector((0, 1, 0))
    ys = {name: (lines[name]["mcp"] - Wr).dot(stack) for name in FINGERS}
    assert all(ys[FINGERS[k]] < ys[FINGERS[k + 1]] for k in range(3)), side + ": fingers should run index to pinky front to back"
    edges = [(FINGERS[k], FINGERS[k + 1], 0.5 * (ys[FINGERS[k]] + ys[FINGERS[k + 1]]),
              0.35 * abs(ys[FINGERS[k + 1]] - ys[FINGERS[k]])) for k in range(3)]
    hand_bones = {P + side + "Hand"} | {P + side + f"Hand{f}{k}" for f in FINGERS for k in range(1, 5)}
    moved = 0
    for i in idx:
        sa = (V[i] - Wr).dot(a)
        if sa <= 0:
            continue
        w = WT[i]
        share = sum(v for g, v in w.items() if g in hand_bones)
        if share <= 0:
            continue
        if i in member:
            m = member[i]
        else:
            y = (V[i] - Wr).dot(stack)
            m = {}
            lo = 1.0
            for f0, f1, mid, half in edges:
                b = smoothstep(mid - half, mid + half, y)
                m[f0] = m.get(f0, 0) + lo * (1 - b)
                lo *= b
            m[FINGERS[-1]] = m.get(FINGERS[-1], 0) + lo
        new = {}
        for f, mf in m.items():
            if mf <= 1e-4:
                continue
            L = lines[f]
            s = (V[i] - L["mcp"]).dot(L["dir"])
            fin = smoothstep(-0.006, 0.006, s) * mf
            u = s / L["len"]
            segs = [1 - smoothstep(0.36, 0.48, u), smoothstep(0.36, 0.48, u) * (1 - smoothstep(0.66, 0.78, u)), smoothstep(0.66, 0.78, u)]
            for k, x in enumerate(segs):
                if x * fin > 1e-4:
                    n = P + side + f"Hand{f}{k + 1}"
                    new[n] = new.get(n, 0) + x * fin
        tot = sum(new.values())
        new[P + side + "Hand"] = max(0.0, 1 - tot)
        for g in list(w):
            if g in hand_bones:
                del w[g]
        for g, x in new.items():
            w[g] = w.get(g, 0) + x * share
        moved += 1
    return {"split_at_m": round(thr, 4), "verts": moved,
            "fingers": {f: {"len": round(L["len"], 4), "mcp": [round(c, 4) for c in L["mcp"]], "verts": len(L["verts"])} for f, L in lines.items()}}


report["fingers"] = {s: finger_step(s) for s in ("Left", "Right")}

# --- 6. tail chain along the tail piece's own centreline ---------------------------------------------------------
tail_idx = verts("tail")
TS = set(tail_idx.tolist())
adj = {}
for e in me.edges:
    i, j = e.vertices
    if i in TS and j in TS:
        L = (V[i] - V[j]).length
        adj.setdefault(i, []).append((j, L)); adj.setdefault(j, []).append((i, L))
bmt = bmesh.new(); bmt.from_mesh(me); bmt.verts.ensure_lookup_table()
root_ring = [v.index for v in bmt.verts if v.index in TS and v.is_boundary]
bmt.free()
assert root_ring, "the tail piece should be open where it enters the jeans"
dist = {i: 0.0 for i in root_ring}
pq = [(0.0, i) for i in root_ring]
while pq:
    d, i = heapq.heappop(pq)
    if d > dist.get(i, 9):
        continue
    for j, L in adj.get(i, []):
        if d + L < dist.get(j, 9):
            dist[j] = d + L; heapq.heappush(pq, (d + L, j))
tip = max(dist, key=dist.get)
dmax = dist[tip]
u_tail = {i: dist[i] / dmax for i in TS}
samples = []
for k in range(41):
    t = k / 40
    pts = [V[i] for i in TS if abs(u_tail[i] - t) < 0.03]
    if pts:
        samples.append(sum(pts, Vector()) / len(pts))
root_c = sum((V[i] for i in root_ring), Vector()) / len(root_ring)
samples[0] = root_c
samples[-1] = V[tip].copy()
for _ in range(2):
    samples = [samples[0]] + [(samples[i - 1] + samples[i] * 2 + samples[i + 1]) / 4 for i in range(1, len(samples) - 1)] + [samples[-1]]
seg = [0.0]
for p0, p1 in zip(samples, samples[1:]):
    seg.append(seg[-1] + (p1 - p0).length)


def along(t):
    Lh = t * seg[-1]
    for i in range(1, len(seg)):
        if seg[i] >= Lh:
            f = (Lh - seg[i - 1]) / max(1e-9, seg[i] - seg[i - 1])
            return samples[i - 1].lerp(samples[i], f)
    return samples[-1]


tail_joints = [along(j / NTAIL) for j in range(NTAIL + 1)]
# arc-length parameter of each tail vertex: its nearest centreline sample
kd = KDTree(len(samples))
for k, p in enumerate(samples):
    kd.insert(p, k)
kd.balance()
arc = {}
for i in TS:
    _, k, _ = kd.find(V[i])
    arc[i] = seg[k] / seg[-1]
assert root_c.y > bone_head(P + "Hips").y, "the tail root should be behind the hips"
report["tail"] = {"verts": len(TS), "root_ring": len(root_ring), "chain_len": round(seg[-1], 4),
                  "joints": [[round(c, 4) for c in p] for p in tail_joints]}

# --- 7. props and bone frames the clips rely on ------------------------------------------------------------------
# His clips write Hat, Mustache, Straw and Tail1-5 as local transforms (generic curves). Head and Hips keep the live
# rig's rest frames, and Hat and Mustache the live rest rotations, so those curves keep their meaning; the install moves
# each prop's position curves by its new pivot and re-expresses the Straw and tail rotations from the new rest.
body_idx = verts("body")
skull_top = max(V[i].z for i in body_idx)
hat_idx = verts("hat")
band_idx = verts("hat_band")
band_top = max(V[i].z for i in band_idx) if len(band_idx) else min(V[i].z for i in hat_idx)
crown = [V[i] for i in hat_idx if V[i].z > band_top]
crown_c = sum(crown, Vector()) / len(crown)
hat_pivot = Vector((crown_c.x, crown_c.y, skull_top - 0.03))
mus_idx = verts("mustache")
mus_c = sum((V[i] for i in mus_idx), Vector()) / len(mus_idx)
mus_back = Vector((mus_c.x, max(V[i].y for i in mus_idx), mus_c.z))
straw_idx = verts("straw")
sp = np.array([list(V[i]) for i in straw_idx])
inner = sp[np.argmin(np.abs(sp[:, 0]))]
end_pts = sp[np.linalg.norm(sp - inner, axis=1) < 0.006]
straw_end = Vector(end_pts.mean(0))
straw_far = Vector(sp[np.argmax(np.linalg.norm(sp - inner, axis=1))])

eb = edit_mode()
saved = {b.name: (b.head.copy(), b.tail.copy(), b.roll) for b in eb}
for b in eb:
    b.use_connect = False


def set_frame(b, head_world, rot_world, length):
    """Bone b at head_world with the world-space rest rotation rot_world (Y along the bone) and the given length."""
    R = AWi.to_3x3().normalized() @ rot_world
    b.head = AWi @ head_world
    b.tail = AWi @ (head_world + rot_world.col[1] * length)
    b.matrix = Matrix.Translation(AWi @ head_world) @ R.to_4x4()


for n in (P + "Hips", P + "Head"):
    b = eb[n]
    set_frame(b, AW @ b.head, LIVE_ROT[n], (AW.to_3x3() @ (b.tail - b.head)).length)
for b in eb:  # nothing else moves
    if b.name not in (P + "Hips", P + "Head"):
        b.head, b.tail, b.roll = saved[b.name]


def new_bone(name, head, tail, parent, connect=False):
    b = eb.new(name)
    b.head, b.tail = AWi @ head, AWi @ tail
    b.parent = eb[parent]; b.use_connect = connect; b.use_deform = True
    return b


prev = P + "Hips"
tail_names = []
for j in range(NTAIL):
    b = new_bone(f"Tail{j + 1}", tail_joints[j], tail_joints[j + 1], prev, connect=j > 0)
    d = (tail_joints[j + 1] - tail_joints[j]).normalized()
    b.align_roll(AWi.to_3x3() @ (Vector((0, 0, 1)) if abs(d.z) < 0.9 else Vector((0, 1, 0))))
    prev = b.name; tail_names.append(b.name)
b = new_bone("Hat", hat_pivot, hat_pivot + Vector((0, 0, 0.12)), P + "Head")
set_frame(b, hat_pivot, LIVE_ROT["Hat"], 0.12)
b = new_bone("Mustache", mus_back, mus_back + Vector((0, -0.05, 0)), P + "Head")
set_frame(b, mus_back, LIVE_ROT["Mustache"], 0.05)
b = new_bone("Straw", straw_end, straw_end + (straw_far - straw_end).normalized() * 0.08, P + "Head")
b.align_roll(AWi.to_3x3() @ Vector((0, 0, 1)))
# connections as Tripo had them, where the head still sits on the parent's tail
for b in eb:
    if b.parent and (b.head - b.parent.tail).length < 1e-6 and b.name in saved:
        b.use_connect = True
object_mode()
report["props_before_size"] = {"hat_pivot": [round(c, 4) for c in hat_pivot], "mustache": [round(c, 4) for c in mus_back],
                   "straw_end": [round(c, 4) for c in straw_end], "skull_top": round(skull_top, 4)}

# --- 8. weights --------------------------------------------------------------------------------------------------
# 8a. Torso and neck: Tripo jumps from Hips to Spine2 in a thin band at the jacket hem, and gives the snout and face to
# Neck (the eyes, all Head, would slide off his face). The spine family's share of each vertex is re-split by a smooth
# function of height, the same for the skin and every layer over it; shoulder, arm and leg shares stay Tripo's.
SPINE = [P + n for n in ("Hips", "Spine", "Spine1", "Spine2", "Neck", "Head")]
z = {n: bone_head(n).z for n in SPINE}
mids = [(z[SPINE[k]] + z[SPINE[k + 1]]) / 2 for k in range(4)]  # Hips..Spine2 segment centres
zn, zh = z[P + "Neck"], z[P + "Head"]
Ln = zh - zn
# the skin goes from Neck to Head across the jaw line (the chin's underside is about 1.5 cm below the Head joint)
n1, n2, n3 = zn + 0.2 * Ln, zn + float(opt.get("jaw_lo", 0.52)) * Ln, zn + float(opt.get("jaw_hi", 0.74)) * Ln
neck_pts = np.array([list(V[i]) for i in body_idx if zn - 0.005 < V[i].z < n2])
neck_axis = bone_head(P + "Neck")
neck_r = float(np.percentile(np.hypot(neck_pts[:, 0] - neck_axis.x, neck_pts[:, 1] - neck_axis.y), 90)) if len(neck_pts) else 0.04
R0, R1 = neck_r * 1.15, neck_r * 1.9


def spine_split(p, cloth=False):
    zz = p.z
    r = math.hypot(p.x - neck_axis.x, p.y - neck_axis.y)
    w = {}
    if zz <= mids[0]:
        return {SPINE[0]: 1.0}
    for k in range(3):
        if zz <= mids[k + 1]:
            b = smoothstep(mids[k], mids[k + 1], zz)
            return {SPINE[k]: 1 - b, SPINE[k + 1]: b}
    head = 0.0 if cloth else smoothstep(n2, n3, zz)  # the neckerchief and collar are tied round the neck, not the jaw
    neck = smoothstep(mids[3], n1, zz) * (1 - head) * (1 - smoothstep(R0, R1, r))
    return {P + "Spine2": max(0.0, 1 - head - neck), P + "Neck": neck, P + "Head": head}


smoothed = 0
CLOTH = set(verts("torso_cloth").tolist())
for i in verts("body", "torso_cloth", "jeans"):
    w = WT[i]
    sh = sum(w.get(n, 0) for n in SPINE)
    if sh <= 0:
        continue
    for n in SPINE:
        w.pop(n, None)
    for n, x in spine_split(V[i], i in CLOTH).items():
        if x * sh > 1e-5:
            w[n] = w.get(n, 0) + x * sh
    WT[i] = norm(w)
    smoothed += 1
report["torso"] = {"verts": smoothed, "neck_radius": round(neck_r, 4), "bands": [round(m, 4) for m in mids] + [round(n1, 4), round(n2, 4), round(n3, 4)]}

# 8b. Layers that ride on others copy the weights of the nearest vertex beneath them.
def copy_from(dst_roles, src_roles):
    src = verts(*src_roles)
    kd = KDTree(len(src))
    for k, i in enumerate(src):
        kd.insert(V[i], k)
    kd.balance()
    for i in verts(*dst_roles):
        _, k, _ = kd.find(V[i])
        WT[i] = dict(WT[src[k]])


copy_from(("belt_part",), ("jeans",))
copy_from(("cuff",), ("arm_Left", "arm_Right", "torso_cloth"))
# Small rigid parts (loops, buckle, keeper, knee patch, cuffs) take one weight set, their vertices' average: per-vertex
# copies straddled the jeans' left/right leg split at the fly and stretched the buckle keeper 9.5x in the idle.
EDGE_ADJ = {}
for e in me.edges:
    i, j = e.vertices
    EDGE_ADJ.setdefault(i, []).append(j); EDGE_ADJ.setdefault(j, []).append(i)
for d in info:
    if ROLE[d["k"]] in ("belt_part", "cuff"):
        if d["n"] < 200:
            avg = {}
            for i in d["idx"]:
                for k, x in WT[i].items():
                    avg[k] = avg.get(k, 0) + x / d["n"]
            avg = norm(dict(sorted(avg.items(), key=lambda t: -t[1])[:4]))
            for i in d["idx"]:
                WT[i] = dict(avg)
        else:  # the belt: smooth its copied weights along itself
            for _ in range(int(opt.get("belt_smooth", 12))):
                new = {}
                for i in d["idx"]:
                    acc = dict(WT[i])
                    nb = EDGE_ADJ.get(i, [])
                    for j in nb:
                        for k, x in WT[j].items():
                            acc[k] = acc.get(k, 0) + x
                    new[i] = norm(acc)
                for i, w in new.items():
                    WT[i] = w
# 8c. The lasso hangs from the belt on his right hip and keeps its coil: one weight set for all of it.
for i in verts("lasso"):
    WT[i] = {P + "Hips": LASSO_HIPS, P + "RightUpLeg": 1 - LASSO_HIPS}
# 8d. Rigid props, and the face pieces on Head.
for i in verts("hat", "hat_band"):
    WT[i] = {"Hat": 1.0}
for i in verts("mustache"):
    WT[i] = {"Mustache": 1.0}
for i in verts("straw"):
    WT[i] = {"Straw": 1.0}
for i in verts("face"):
    WT[i] = {P + "Head": 1.0}
# 8e. Tail: smooth blends between bone centres; the root eases in from Hips.
cent = [(j + 0.5) / NTAIL for j in range(NTAIL)]
for i in TS:
    t = arc[i]
    new = {}
    if t <= cent[0]:
        new[tail_names[0]] = 1.0
    elif t >= cent[-1]:
        new[tail_names[-1]] = 1.0
    else:
        j = min(NTAIL - 2, int((t - cent[0]) * NTAIL))
        b = smoothstep(cent[j], cent[j + 1], t)
        new[tail_names[j]] = 1 - b; new[tail_names[j + 1]] = b
    hip = 1 - smoothstep(0.0, 0.12, t)
    WT[i] = norm({**{k: v * (1 - hip) for k, v in new.items()}, P + "Hips": hip})


# 8f. at most 4 influences, normalised
def limit4(w):
    return norm(dict(sorted(w.items(), key=lambda t: -t[1])[:4]))


WT = [limit4(w) for w in WT]
assert all(WT), "every vertex keeps a weight"
body.vertex_groups.clear()
groups = {}
for i, w in enumerate(WT):
    for k, x in w.items():
        if k not in groups:
            groups[k] = body.vertex_groups.new(name=k)
        groups[k].add([i], x, "REPLACE")
report["weights_over_0.2"] = {n: int(sum(1 for w in WT if w.get(n, 0) > 0.2)) for n in SPINE}

# --- 8g. spurs: one on the back of each boot heel, rigid on the foot bone ------------------------------------------
# The Tripo boots have none, and two of his moves are named after them (Spur Kick, Spur Stomp). Each spur is a band
# round the back of the heel, a shank and a star rowel, flat shaded so the facets read like the rest of the low-poly
# model. Each is weighted fully to its own bone, LeftSpur / RightSpur, a child of the foot bone that no clip animates,
# so it stays rigid on the foot; the bone's name keeps the spur out of the hurtbox fit (hurtbox_fit.eval.cs treats spur
# bones as cosmetic, like the hat): weighted to the foot itself, it grew the foot capsules 1.61x instead of 1.25x. They keep the one body material: their UVs sit on a steel patch painted into the largest square of the atlas
# that no UV triangle touches. Each part's UVs collapse onto one point of its own tone, so mipmaps never bleed the
# neighbouring atlas into the steel. Lengths are metres before `size`.
SPUR_Z = float(opt.get("spur_z", 0.052))  # band centre above the floor
SPUR_SHANK = float(opt.get("spur_shank", 0.031))  # band to rowel centre
SPUR_ROWEL = float(opt.get("spur_rowel", 0.022))  # rowel outer radius (points); the inner radius is half
SPUR_THICK = float(opt.get("spur_thick", 0.0065))  # rowel thickness: it is seen edge-on from the gameplay camera
SPUR_POINTS = int(opt.get("spur_points", 6))
SPUR_DROP = math.radians(float(opt.get("spur_drop_deg", 15)))  # shank tilt below horizontal
STEEL = {"rowel": (0.74, 0.76, 0.79), "shank": (0.58, 0.60, 0.63), "band": (0.44, 0.46, 0.49)}  # sRGB
IMG = next(i for i in bpy.data.images if i.size[0] > 0)


def free_uv_square(n=1024):
    """The largest square of the atlas that no UV triangle's bounding box touches: (u0, v0, size)."""
    me.calc_loop_triangles()
    uv = np.empty(len(me.loops) * 2, np.float32)
    me.uv_layers.active.data.foreach_get("uv", uv)
    tl = np.empty(len(me.loop_triangles) * 3, np.int32)
    me.loop_triangles.foreach_get("loops", tl)
    T = uv.reshape(-1, 2)[tl.reshape(-1, 3)]
    lo = np.floor(T.min(1) * n).astype(int).clip(0, n - 1)
    hi = np.floor(T.max(1) * n).astype(int).clip(0, n - 1)
    cover = np.zeros((n + 1, n + 1), np.int32)  # bounding boxes as a 2D difference array
    np.add.at(cover, (lo[:, 1], lo[:, 0]), 1); np.add.at(cover, (lo[:, 1], hi[:, 0] + 1), -1)
    np.add.at(cover, (hi[:, 1] + 1, lo[:, 0]), -1); np.add.at(cover, (hi[:, 1] + 1, hi[:, 0] + 1), 1)
    free = cover.cumsum(0).cumsum(1)[:n, :n] == 0
    dp = np.zeros((n + 1, n + 1), np.int32)  # dp[y+1, x+1]: side of the largest free square ending at cell (y, x)
    for y in range(n):
        prev, cur = dp[y], dp[y + 1]
        for x in np.nonzero(free[y])[0]:
            cur[x + 1] = 1 + min(prev[x + 1], cur[x], prev[x])
    s = int(dp.max())
    y, x = np.unravel_index(int(dp.argmax()), dp.shape)
    return (x - s) / n, (y - s) / n, s / n


def paint_steel(u0, v0, s):
    """Three steel tones side by side inside the free square (12% margin); returns each tone's UV centre."""
    w, h = IMG.size
    px = np.empty(w * h * 4, np.float32)
    IMG.pixels.foreach_get(px)
    px = px.reshape(h, w, 4)
    m = 0.12 * s
    x0, x1 = int(math.ceil((u0 + m) * w)), int(math.floor((u0 + s - m) * w))
    y0, y1 = int(math.ceil((v0 + m) * h)), int(math.floor((v0 + s - m) * h))
    cols = np.linspace(x0, x1, 4).round().astype(int)
    centres = {}
    for k, part in enumerate(("rowel", "shank", "band")):
        px[y0:y1, cols[k]:cols[k + 1], :3] = STEEL[part]
        px[y0:y1, cols[k]:cols[k + 1], 3] = 1.0
        centres[part] = ((cols[k] + cols[k + 1]) / 2 / w, (y0 + y1) / 2 / h)
    IMG.pixels.foreach_set(px.ravel())
    IMG.update()
    return centres, [x0, y0, x1, y1]


def hull2(pts):
    pts = sorted(set(map(tuple, np.round(pts, 6))))

    def cross(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])
    lo, hi = [], []
    for p in pts:
        while len(lo) >= 2 and cross(lo[-2], lo[-1], p) <= 0:
            lo.pop()
        lo.append(p)
    for p in reversed(pts):
        while len(hi) >= 2 and cross(hi[-2], hi[-1], p) <= 0:
            hi.pop()
        hi.append(p)
    return np.array(lo[:-1] + hi[:-1])


def ray_hull(H, c, r):
    """Distance from c along r to the convex polygon H's boundary."""
    best = None
    for k in range(len(H)):
        p, e = H[k], H[(k + 1) % len(H)] - H[k]
        den = -r[0] * e[1] + r[1] * e[0]
        if abs(den) < 1e-12:
            continue
        w = p - c
        t = (-w[0] * e[1] + w[1] * e[0]) / den
        s = (r[0] * w[1] - r[1] * w[0]) / den
        if t > 0 and -1e-9 <= s <= 1 + 1e-9:
            best = t if best is None else max(best, t)
    return best


def add_part(bm, coords, faces, part, group, centres, dl, uvl, made):
    vs = [bm.verts.new(c) for c in coords]
    for v in vs:
        v[dl][group] = 1.0
    new = []
    for f in faces:
        face = bm.faces.new([vs[i] for i in f])
        face.smooth = False
        face.material_index = 0
        for loop in face.loops:
            loop[uvl].uv = centres[part]
        new.append(face)
    bmesh.ops.recalc_face_normals(bm, faces=new)  # closed parts: outward, so back-face culling keeps them
    made[part] = made.get(part, 0) + len(vs)


def prism(ring0, ring1):
    """Faces of a closed prism between two rings of equal size (caps included)."""
    n = len(ring0)
    faces = [(k, (k + 1) % n, n + (k + 1) % n, n + k) for k in range(n)]
    return faces + [tuple(range(n - 1, -1, -1)), tuple(range(n, 2 * n))]


def spur(side, bm, dl, uvl, centres):
    sign = 1 if side == "Left" else -1
    Vb = np.empty(len(me.vertices) * 3, np.float32)
    me.vertices.foreach_get("co", Vb)
    Vb = Vb.reshape(-1, 3)
    boot = set(int(i) for i in verts("boot") if np.sign(Vb[i][0]) == sign)
    foot, toe = bone_head(P + side + "Foot"), bone_head(P + side + "ToeBase")
    back = -Vector((toe.x - foot.x, toe.y - foot.y, 0)).normalized()
    lat = Vector((0, 0, 1)).cross(back).normalized()
    up = Vector((0, 0, 1))
    sec = []  # the boot's outline where its edges cross z = SPUR_Z
    for e in me.edges:
        i, j = e.vertices
        if i in boot and j in boot and (Vb[i][2] - SPUR_Z) * (Vb[j][2] - SPUR_Z) < 0:
            t = (SPUR_Z - Vb[i][2]) / (Vb[j][2] - Vb[i][2])
            sec.append(Vb[i][:2] + t * (Vb[j][:2] - Vb[i][:2]))
    H = hull2(np.array(sec))
    b2, l2 = np.array([back.x, back.y]), np.array([lat.x, lat.y])
    along, across = H @ b2, H @ l2
    heel = (along > along.max() - 0.04)
    width = across[heel].max() - across[heel].min()
    c = b2 * (along.max() - width / 2) + l2 * (across[heel].max() + across[heel].min()) / 2
    gap, thick, tall = 0.001, 0.0035, 0.010  # the band: 1 mm off the boot
    t_back = ray_hull(H, c, b2)
    root = Vector((c[0], c[1], SPUR_Z)) + back * (t_back + gap + thick * 0.5)
    d = (back * math.cos(SPUR_DROP) - up * math.sin(SPUR_DROP)).normalized()
    ebs = edit_mode()
    bone = ebs.new(side + "Spur")
    bone.head, bone.tail = AWi @ root, AWi @ (root + d * SPUR_SHANK)
    bone.parent = ebs[P + side + "Foot"]; bone.use_connect = False; bone.use_deform = True
    bone.align_roll(AWi.to_3x3() @ up)
    object_mode()
    group = body.vertex_groups.new(name=side + "Spur").index
    made = {}
    # band: a flat strap round the back of the heel
    rings = []
    for th in np.radians(np.linspace(-80, 80, 9)):
        r = b2 * math.cos(th) + l2 * math.sin(th)
        t = ray_hull(H, c, r)
        p = Vector((c[0] + r[0] * t, c[1] + r[1] * t, SPUR_Z))
        n = Vector((r[0], r[1], 0))
        rings.append([p + n * gap - up * tall / 2, p + n * (gap + thick) - up * tall / 2,
                      p + n * (gap + thick) + up * tall / 2, p + n * gap + up * tall / 2])
    coords = [q for ring in rings for q in ring]
    faces = [(4 * s + k, 4 * s + (k + 1) % 4, 4 * (s + 1) + (k + 1) % 4, 4 * (s + 1) + k) for s in range(len(rings) - 1) for k in range(4)]
    faces += [(3, 2, 1, 0), tuple(4 * (len(rings) - 1) + k for k in range(4))]
    add_part(bm, coords, faces, "band", group, centres, dl, uvl, made)
    # shank: a tapered hexagonal bar from the band's back, tilted down
    a1, a2 = lat, d.cross(lat).normalized()
    hexa = [a1 * math.cos(math.pi * k / 3) + a2 * math.sin(math.pi * k / 3) for k in range(6)]
    end = root + d * SPUR_SHANK
    coords = [root + h * 0.0056 for h in hexa] + [end + h * 0.0042 for h in hexa]
    add_part(bm, coords, prism(hexa, hexa), "shank", group, centres, dl, uvl, made)
    # hub: the rowel's axle, wider than the rowel
    u = lat.cross(d).normalized()
    ring = [d * math.cos(math.pi * k / 3) + u * math.sin(math.pi * k / 3) for k in range(6)]
    hub_r, hub_half = 0.0048, SPUR_THICK / 2 + 0.0015
    coords = [end - lat * hub_half + q * hub_r for q in ring] + [end + lat * hub_half + q * hub_r for q in ring]
    add_part(bm, coords, prism(ring, ring), "shank", group, centres, dl, uvl, made)
    # rowel: a star with a low pyramid on each face, so the facets catch the light
    n_pts, thick_r, bulge = 2 * SPUR_POINTS, SPUR_THICK, 0.0025
    star = [(d * math.cos(math.pi * k / SPUR_POINTS) + u * math.sin(math.pi * k / SPUR_POINTS))
            * (SPUR_ROWEL if k % 2 == 0 else SPUR_ROWEL * 0.5) for k in range(n_pts)]
    coords = [end + lat * thick_r / 2 + q for q in star] + [end - lat * thick_r / 2 + q for q in star]
    coords += [end + lat * (thick_r / 2 + bulge), end - lat * (thick_r / 2 + bulge)]
    fa, ba = 2 * n_pts, 2 * n_pts + 1
    faces = [(k, (k + 1) % n_pts, n_pts + (k + 1) % n_pts, n_pts + k) for k in range(n_pts)]
    faces += [(fa, k, (k + 1) % n_pts) for k in range(n_pts)] + [(ba, n_pts + (k + 1) % n_pts, n_pts + k) for k in range(n_pts)]
    add_part(bm, coords, faces, "rowel", group, centres, dl, uvl, made)
    near = [i for i in boot if (Vector(Vb[i]) - root).length < 0.02]
    share = {}
    for i in near:
        for g in me.vertices[i].groups:
            nm = body.vertex_groups[g.group].name
            share[nm] = share.get(nm, 0) + g.weight / len(near)
    return {"bone": side + "Spur", "verts": made, "band_z": SPUR_Z, "shank_root": [round(x, 4) for x in root], "rowel_centre": [round(x, 4) for x in end],
            "heel_width": round(float(width), 4), "boot_weights_near_root": {k: round(v, 3) for k, v in sorted(share.items(), key=lambda t: -t[1])}}


if SPURS:
    u0, v0, s = free_uv_square()
    centres, rect = paint_steel(u0, v0, s)
    bm = bmesh.new()
    bm.from_mesh(me)
    dl = bm.verts.layers.deform.verify()
    uvl = bm.loops.layers.uv.active
    report["spurs"] = {"free_square_uv": [round(u0, 5), round(v0, 5), round(s, 5)], "patch_px": rect,
                       "sides": {sd: spur(sd, bm, dl, uvl, centres) for sd in ("Left", "Right")}}
    bm.to_mesh(me)
    bm.free()
    me.update()
    print("SPURS", report["spurs"])

# --- 8h. size: one uniform scale of the finished rig and mesh ------------------------------------------------------
if SIZE != 1.0:
    K = Matrix.Scale(SIZE, 4)
    arm.data.transform(AWi @ K @ AW)
    me.transform(K)
    me.update()
report["hip_match_scale"] = S
report["size"] = SIZE
report["scale"] = S * SIZE

# --- 9. material, texture, export ----------------------------------------------------------------------------------
img = IMG
tex = os.path.join(OUT, "textures", "Salamander_Body.png")
img.save(filepath=tex)
mat = me.materials[0]
mat.name = "Salamander_Body"
body.name = body.data.name = "Salamander_Body"
arm.name = "Armature"
arm.data.pose_position = "POSE"
bpy.ops.object.select_all(action="DESELECT")
arm.select_set(True); body.select_set(True)
bpy.context.view_layer.objects.active = arm
fbx = os.path.join(OUT, "salamander.fbx")
bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True, object_types={"ARMATURE", "MESH"}, add_leaf_bones=False,
                         bake_anim=False, path_mode="STRIP", embed_textures=False, mesh_smooth_type="OFF",
                         use_armature_deform_only=False, armature_nodetype="NULL")
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "salamander_rig.blend"))
BUILT_HEAD = {b.name: AW @ b.head_local for b in arm.data.bones}
BUILT_V = np.array([list(v.co) for v in me.vertices])
pts = BUILT_V
report["new_heights"] = {"hips": BUILT_HEAD[P + "Hips"].z, "head_joint": BUILT_HEAD[P + "Head"].z,
                         "body_top": float(skull_top) * SIZE, "with_hat": float(pts[:, 2].max())}
report["verts"] = len(me.vertices)
report["bones"] = len(arm.data.bones)

# --- 10. re-import the export and check the contract -------------------------------------------------------------
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx, ignore_leaf_bones=False)
ra = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
rb = next(o for o in bpy.context.scene.objects if o.type == "MESH")
names = {b.name: (b.parent.name if b.parent else None) for b in ra.data.bones}
missing = [n for n in LIVE_BONES if n not in names]
reparented = [n for n in LIVE_BONES if n in names and names[n] != LIVE_BONES[n]]
added = sorted(n for n in names if n not in LIVE_BONES)
moved = max((ra.matrix_world @ ra.data.bones[n].head_local - BUILT_HEAD[n]).length for n in names)
RV = np.array([list(rb.matrix_world @ v.co) for v in rb.data.vertices])
infl = max(len(v.groups) for v in rb.data.vertices)
unweighted = sum(1 for v in rb.data.vertices if not v.groups)
report["check"] = {"missing_live_bones": missing, "reparented": reparented, "added_bones": added,
                   "bone_heads_moved_m": moved, "vertex_roundtrip_m": float(np.abs(RV - BUILT_V).max()) if len(RV) == len(BUILT_V) else None,
                   "max_influences": infl, "unweighted": unweighted, "armature_matrix_world": [list(r) for r in ra.matrix_world],
                   "rest_frame_err_deg": {n: math.degrees((((ra.matrix_world @ ra.data.bones[n].matrix_local).to_3x3().normalized()).transposed() @ LIVE_ROT[n]).to_quaternion().angle)
                                          for n in (P + "Hips", P + "Head", "Hat", "Mustache")}}
json.dump(report, open(os.path.join(OUT, "report.json"), "w"), indent=1, default=str)
print("REPORT", json.dumps({k: report[k] for k in ("scale", "size", "live_heights", "new_heights", "pronation", "check")}, default=str))
ok = not missing and not reparented and infl <= 4 and unweighted == 0 and moved < 1e-4
if not ok:
    os.replace(fbx, fbx.replace(".fbx", ".REJECTED.fbx"))
    raise SystemExit("build check failed; see report.json")
