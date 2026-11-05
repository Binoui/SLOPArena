"""Rebuild Dusty's T-pose rest mesh by forward-skinning his authored A-pose, with his rib skin taken off his arms.

blender -b --factory-startup --python-exit-code 1 --python rebuild_rest.py -- <salamander.fbx> <salamander-mixamo.fbx> <out_dir> \
  ['{"arm_t0": -0.1, "arm_t1": 0.25, "arm_seam": 0.03, "arm_r": 0.08, "arm_smooth": 4,
       "clav_full": 0.06, "clav_zero": 0.17, "clav_mid": 0.04, "clav_max": 0.9, "wsmooth": 8, "wsmooth_r": 0.15,
       "smooth_iter": 30, "smooth_factor": 0.5, "mask_rings": 4, ...}']

build_rig.py made the T-pose rest by inverse linear-blend skinning from his Mixamo upload's A-pose (arms about 76
degrees down). Where a vertex blends bones that the A-to-T change turns far apart (the shoulder and armpit), the
inverted blend matrix is near singular, so that rest came out malformed: rib shards flung off the right shoulder, and
once the weights were fixed, a dented, lumpy upper arm. Every clip deforms from that rest. This script:
  1. poses the input FBX into the authored A-pose (the Mixamo download's bone rest); his body lands on the Mixamo mesh;
  2. fixes Mixamo's arm weights per side, in that A-pose: rib skin comes off the arm (torso skin is nearer the chest
     midline over the surface than the forearm or the outer half of the upper-arm tube), arm weight ramps across the
     armpit seam, the whole upper arm becomes arm a few cm past the joint (Mixamo left 30-40% Spine2 on it), Spine2
     weight round the joint moves onto the Shoulder bone (which carried almost none, so shrugs left the shoulder
     behind), every weight round the joint is smoothed, and at most 4 influences are kept, as in Unity;
  3. forward-skins that A-pose into the existing T-pose bone rest with those weights, and runs Blender's Corrective
     Smooth bound at the A-pose over the shoulder and armpit, so the raised shoulder keeps its authored volume;
  4. takes the result as the new rest of every mesh whose inverse solve was damaged (forward vs current rest differ
     by more than `damage_mm`); hand-only skin (the rebuilt finger tubes) keeps its rest.
Forward skinning does not invert exactly: posing the new rest back into the A-pose misses the authored body near the
shoulder. fix_report.json records by how much and where. Bones, materials and UVs are untouched: the export is
re-imported and compared with the input, and a bone difference rejects it (salamander.REJECTED.fbx).
Writes salamander.fbx, fix_report.json and regions.json (for clip_review.py).
"""
import bpy, sys, os, json, heapq
import numpy as np
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
LIVE, MIXAMO, OUT = (os.path.abspath(a) for a in argv[:3])
OPT = {"full": 0.02, "zero": 0.05, "band_torso": 0.04, "band_arm": 0.065, "smooth": 10,
       "arm_t0": -0.1, "arm_t1": 0.25, "arm_seam": 0.03, "arm_r": 0.08, "arm_smooth": 4,
       "clav_full": 0.06, "clav_zero": 0.17, "clav_mid": 0.04, "clav_max": 0.9, "wsmooth": 8, "wsmooth_r": 0.15,
       "smooth_iter": 30, "smooth_factor": 0.5, "mask_rings": 4, "mask_soft": 4, "damage_mm": 5.0,
       **(json.loads(argv[3]) if len(argv) > 3 else {})}
os.makedirs(OUT, exist_ok=True)
P = "mixamorig:"
SIDES = {"Left": 1.0, "Right": -1.0}
MAX_INFLUENCES = 4


def bone_mats(arm):
    out = {}
    for b in arm.data.bones:
        m = np.array(arm.matrix_world @ b.matrix_local)
        m[:3, :3] /= np.linalg.norm(m[:3, :3], axis=0)
        out[b.name] = m
    return out


def world_co(o):
    M = np.array(o.matrix_world)
    co = np.empty(len(o.data.vertices) * 3)
    o.data.vertices.foreach_get("co", co)
    return co.reshape(-1, 3) @ M[:3, :3].T + M[:3, 3]


def snapshot(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path, ignore_leaf_bones=False)
    a = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
    bpy.context.view_layer.objects.active = a
    bpy.ops.object.mode_set(mode="EDIT")
    A = a.matrix_world
    bones = {b.name: (b.parent.name if b.parent else None, np.array(A @ b.head), np.array(A @ b.tail), b.roll) for b in a.data.edit_bones}
    bpy.ops.object.mode_set(mode="OBJECT")
    meshes = {}
    for o in bpy.context.scene.objects:
        if o.type != "MESH":
            continue
        me = o.data
        w = np.zeros((len(me.vertices), len(o.vertex_groups)))
        for v in me.vertices:
            for g in v.groups:
                w[v.index, g.group] = g.weight
        meshes[o.name] = {
            "matrix": np.array(o.matrix_world), "parent": o.parent.name if o.parent else None,
            "mods": [(m.type, m.object.name if getattr(m, "object", None) else None) for m in o.modifiers],
            "co": np.array([v.co for v in me.vertices]), "polys": [tuple(p.vertices) for p in me.polygons],
            "materials": [m.name if m else None for m in me.materials], "mat_index": [p.material_index for p in me.polygons],
            "uv": {l.name: np.array([d.uv for d in l.data]) for l in me.uv_layers},
            "weights": {g.name: w[:, g.index] for g in o.vertex_groups}}
    return {"arm": np.array(a.matrix_world), "bones": bones, "meshes": meshes}


def compare(old, new, allowed):
    """allowed: mesh name -> vertex indices that may move or change weights."""
    ob, nb = old["bones"], new["bones"]
    same = list(ob) == list(nb) and all(ob[n][0] == nb[n][0] for n in ob)
    dev = {k: max(float(np.abs(ob[n][i] - nb[n][i]).max()) for n in ob) for k, i in (("head", 1), ("tail", 2), ("roll", 3))} if same else {}
    bones_ok = bool(same and max(dev.values()) <= 1e-5 and np.abs(old["arm"] - new["arm"]).max() <= 1e-6)
    rep = {"bones": {"count": len(nb), "names_and_hierarchy_equal": same,
                     "max_dev_m_rad": {k: float(f"{v:.1e}") for k, v in dev.items()},
                     "ok": bones_ok}, "meshes": {}}
    ok = bones_ok and sorted(old["meshes"]) == sorted(new["meshes"])
    for name, a in old["meshes"].items():
        b = new["meshes"][name]
        moved = set(np.where(np.linalg.norm(a["co"] - b["co"], axis=1) > 1e-6)[0])
        wdev = np.zeros(len(a["co"]))
        for k in set(a["weights"]) | set(b["weights"]):
            wdev = np.maximum(wdev, np.abs(a["weights"].get(k, 0) - b["weights"].get(k, 0)))
        reweighed = set(np.where(wdev > 1e-5)[0])
        m = {"verts": len(b["co"]), "topology_equal": a["polys"] == b["polys"],
             "materials_equal": a["materials"] == b["materials"] and a["mat_index"] == b["mat_index"],
             "uv_max_dev": max((float(np.abs(a["uv"][k] - b["uv"][k]).max()) for k in a["uv"]), default=0.0)
             if a["uv"].keys() == b["uv"].keys() else None,
             "object_equal": bool(np.abs(a["matrix"] - b["matrix"]).max() <= 1e-6 and a["parent"] == b["parent"] and a["mods"] == b["mods"]),
             "vertex_groups_equal": sorted(a["weights"]) == sorted(b["weights"]),
             "moved_verts": len(moved), "reweighed_verts": len(reweighed),
             "only_rebuilt_verts_changed": moved | reweighed <= set(allowed.get(name, ()))}
        m["ok"] = bool(m["topology_equal"] and m["materials_equal"] and m["uv_max_dev"] is not None and m["uv_max_dev"] <= 1e-6
                       and m["object_equal"] and m["vertex_groups_equal"] and m["only_rebuilt_verts_changed"])
        ok = ok and m["ok"]
        rep["meshes"][name] = m
    rep["verified"] = bool(ok)
    return rep


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=MIXAMO, ignore_leaf_bones=False)
MIX = bone_mats(next(o for o in bpy.context.scene.objects if o.type == "ARMATURE"))
MIX_MESH = np.vstack([world_co(o) for o in bpy.context.scene.objects if o.type == "MESH"])

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=LIVE, ignore_leaf_bones=False)
arm = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
MESHES = [o for o in bpy.context.scene.objects if o.type == "MESH"]
body = bpy.data.objects["Salamander_Body"]
LIVE_B = bone_mats(arm)
BONES = [b.name for b in arm.data.bones]
BI = {n: i for i, n in enumerate(BONES)}
HAND_SUBTREE = {BI[c.name] for s in SIDES for c in arm.data.bones[P + s + "Hand"].children_recursive} | {BI[P + s + "Hand"] for s in SIDES}

# The build recentred the model; Hips carries the offset.
T = np.eye(4)
T[:3, 3] = LIVE_B[P + "Hips"][:3, 3] - MIX[P + "Hips"][:3, 3]


def mix_bone(n):
    while n not in MIX:
        n = arm.data.bones[n].parent.name
    return n


D = np.stack([T @ MIX[mix_bone(n)] @ np.linalg.inv(LIVE_B[mix_bone(n)]) for n in BONES])  # world: T-rest -> A-pose
D_INV = np.linalg.inv(D)


def weights(o):
    gi = {g.index: BI[g.name] for g in o.vertex_groups if g.name in BI}
    W = np.zeros((len(o.data.vertices), len(BONES)))
    for v in o.data.vertices:
        for g in v.groups:
            if g.group in gi:
                W[v.index, gi[g.group]] = g.weight
    return W / W.sum(1, keepdims=True)


def skin(Wm, mats, X):
    return np.einsum("vb,bij,vj->vi", Wm, mats, np.c_[X, np.ones(len(X))])[:, :3]


def cap_influences(Wm):
    out = Wm.copy()
    drop = np.argsort(-out, axis=1)[:, MAX_INFLUENCES:]
    np.put_along_axis(out, drop, 0, axis=1)
    out[out < 1e-4] = 0
    return out / out.sum(1, keepdims=True)


def edges_of(o):
    return np.array([e.vertices[:] for e in o.data.edges])


def neighbour_mean(values, E, n):
    total, count = np.zeros(n), np.zeros(n)
    np.add.at(total, E[:, 0], values[E[:, 1]]); np.add.at(total, E[:, 1], values[E[:, 0]])
    np.add.at(count, E.ravel(), 1)
    return total / np.maximum(count, 1)


def corrective_smooth(o, base, deformed, mask):
    """Blender's Corrective Smooth bound at `base` (the authored A-pose), evaluated on `deformed`."""
    me = o.data.copy()
    tmp = bpy.data.objects.new("cs_tmp", me)
    bpy.context.scene.collection.objects.link(tmp)
    me.vertices.foreach_set("co", base.ravel())
    tmp.shape_key_add(name="Basis")
    key = tmp.shape_key_add(name="T")
    key.data.foreach_set("co", deformed.ravel())
    vg = tmp.vertex_groups.new(name="cs_mask")
    for i in np.nonzero(mask)[0]:
        vg.add([int(i)], float(mask[i]), "REPLACE")
    cs = tmp.modifiers.new("cs", "CORRECTIVE_SMOOTH")
    cs.rest_source, cs.smooth_type, cs.use_pin_boundary = "BIND", "LENGTH_WEIGHTED", True
    cs.iterations, cs.factor, cs.vertex_group = int(OPT["smooth_iter"]), float(OPT["smooth_factor"]), "cs_mask"
    key.value = 0.0
    bpy.context.view_layer.objects.active = tmp
    bpy.ops.object.correctivesmooth_bind(modifier="cs")
    key.value = 1.0
    bpy.context.view_layer.update()
    ev = tmp.evaluated_get(bpy.context.evaluated_depsgraph_get())
    out = np.empty(len(base) * 3)
    ev.data.vertices.foreach_get("co", out)
    bpy.data.objects.remove(tmp)
    bpy.data.meshes.remove(me)
    return out.reshape(-1, 3)


def geodesic(sources, nbr):
    dist = np.full(len(nbr), np.inf)
    dist[list(sources)] = 0
    heap = [(0.0, int(s)) for s in sources]
    heapq.heapify(heap)
    while heap:
        d, v = heapq.heappop(heap)
        if d > dist[v]:
            continue
        for u, l in nbr[v]:
            if d + l < dist[u]:
                dist[u] = d + l
                heapq.heappush(heap, (d + l, u))
    return dist


def nearest(Pq, Q):
    best = np.full(len(Pq), np.inf)
    for a in range(0, len(Q), 512):
        best = np.minimum(best, np.linalg.norm(Pq[:, None] - Q[None, a:a + 512], axis=2).min(1))
    return best


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


# --- body: rib/arm split in the authored A-pose --------------------------------------------------------------------
W = weights(body)
V = len(W)
REST = world_co(body)
EDGES = edges_of(body)
APOSE = skin(W, D, REST)
EDGE_LEN = np.linalg.norm(APOSE[EDGES[:, 0]] - APOSE[EDGES[:, 1]], axis=1)
NBR = [[] for _ in range(V)]
for (a, b), l in zip(EDGES, EDGE_LEN):
    NBR[a].append((b, l)); NBR[b].append((a, l))

SPINE2 = BI[P + "Spine2"]
DOM = W.argmax(1)
NEW_W = W.copy()
ARM_CHAIN = np.zeros(len(BONES), bool)
report = {"input": LIVE, "options": OPT, "sides": {}}
regions = {"vertex_count": V, "rib": {}, "armpit_edges": {}, "apose_edge_len": EDGE_LEN.round(6).tolist()}
for side, sgn in SIDES.items():
    chain = np.array([BI[n] for n in BONES if n.startswith(P + side) and ("Arm" in n or "Hand" in n)])
    ARM_CHAIN[chain] = True
    lower = [BI[n] for n in BONES if n.startswith(P + side) and ("ForeArm" in n or "Hand" in n)]
    upper = BI[P + side + "Arm"]
    J, E = ((D[BI[P + side + b]] @ np.r_[LIVE_B[P + side + b][:3, 3], 1])[:3] for b in ("Arm", "ForeArm"))
    x, z = APOSE[:, 0] * sgn, APOSE[:, 2]
    depth = J[2] - z
    g_arm = geodesic(np.where(np.isin(DOM, lower))[0], NBR)
    g_torso = geodesic(np.where((np.abs(APOSE[:, 0]) < 0.05) & (z > 0.45) & (depth > 0.03))[0], NBR)
    zone = (x > 0.02) & (z > 0.40) & (depth > 0) & np.isfinite(g_arm) & np.isfinite(g_torso)
    t = (APOSE - J) @ (E - J) / ((E - J) @ (E - J))
    medial = (APOSE[:, 0] - (J[0] + t * (E[0] - J[0]))) * sgn < 0
    # The arm hangs 1 cm from the ribs, so nearness to the forearm puts the inner upper arm on the torso. Seed the arm
    # with the outer half of the upper-arm tube too: inner-arm skin is a quarter turn round the tube from it, while rib
    # skin only reaches it through the armpit.
    radial = np.linalg.norm(APOSE - (J + np.outer(np.clip(t, 0, 1), E - J)), axis=1)
    outer_tube = (t > 0.15) & (t < 1) & ~medial & (radial < OPT["arm_r"])
    g_tube = geodesic(np.where(np.isin(DOM, lower) | outer_tube)[0], NBR)
    torso = zone & (g_torso < g_tube) & medial
    seam = EDGES[(torso[EDGES[:, 0]] != torso[EDGES[:, 1]]) & zone[EDGES[:, 0]] & zone[EDGES[:, 1]]]
    g_seam = geodesic(np.unique(seam), NBR)
    share = smoothstep(-OPT["band_torso"], OPT["band_arm"], np.where(torso, -g_seam, g_seam))
    band = zone & (share > 0) & (share < 1)
    for _ in range(OPT["smooth"]):
        share = np.where(band, neighbour_mean(share, EDGES, V), share)
    below = smoothstep(OPT["full"], OPT["zero"], depth)

    aw = W[:, chain].sum(1)
    new_aw = np.where(zone, aw + (share - aw) * below, aw)
    # Mixamo left 30-40% Spine2 weight down the whole upper arm, nearly to the elbow, so every swing blended the upper
    # arm halfway with the chest (the noodle and the ribbon). Arm skin becomes all arm a few cm past the joint.
    arm_side = ~torso & (g_tube < g_torso)
    lift = np.where(arm_side, smoothstep(OPT["arm_t0"], OPT["arm_t1"], t) * smoothstep(0, OPT["arm_seam"], g_seam), 0)
    new_aw = np.maximum(new_aw, lift)
    blend = (arm_side | zone) & (t > OPT["arm_t0"] - 0.2) & (new_aw > 0) & (new_aw < 1)
    for _ in range(int(OPT["arm_smooth"])):
        new_aw = np.where(blend, neighbour_mean(new_aw, EDGES, V), new_aw)
    report.setdefault("upper_arm", {})[side] = {"verts_lifted": int((lift > aw + 1e-4).sum())}
    for i in np.where(np.abs(new_aw - aw) > 1e-4)[0]:
        arm_part = W[i, chain] / aw[i] if aw[i] > 1e-6 else (chain == upper).astype(float)
        other = W[i].copy()
        other[chain] = 0
        other = other / other.sum() if other.sum() > 1e-6 else (np.arange(len(BONES)) == SPINE2).astype(float)
        NEW_W[i] = other * (1 - new_aw[i])
        NEW_W[i, chain] = arm_part * new_aw[i]

    ribs = torso & (depth > 0.04) & (z > 0.45) & (g_seam > 0.03)
    mid_z = (z[EDGES[:, 0]] + z[EDGES[:, 1]]) / 2
    regions["rib"][side] = np.where(ribs)[0].tolist()
    regions["armpit_edges"][side] = np.where(zone[EDGES[:, 0]] & zone[EDGES[:, 1]] & (mid_z > J[2] - 0.17) & (mid_z < J[2] + 0.05))[0].tolist()
    gained, lost = np.clip(new_aw - aw, 0, None), np.clip(aw - new_aw, 0, None)
    report["sides"][side] = {
        "shoulder_joint_apose": J.round(4).tolist(), "torso_skin": int(torso.sum()), "arm_skin": int((zone & ~torso).sum()),
        "rib_verts": int(ribs.sum()), "rib_arm_weight_before_max": round(float(aw[ribs].max()), 3),
        "rib_arm_weight_after_max": round(float(new_aw[ribs].max()), 3),
        "verts_less_arm": int((lost > 1e-4).sum()), "verts_more_arm": int((gained > 1e-4).sum())}

# Mixamo gave his Shoulder bones almost no skin, so a shrug (the clips raise them 50-77 degrees) left the shoulder top on
# the chest. Spine2 weight round each shoulder joint moves onto that side's Shoulder bone, fading out with distance.
for side, sgn in SIDES.items():
    J = (D[BI[P + side + "Arm"]] @ np.r_[LIVE_B[P + side + "Arm"][:3, 3], 1])[:3]
    near = smoothstep(OPT["clav_zero"], OPT["clav_full"], np.linalg.norm(APOSE - J, axis=1))
    lateral = smoothstep(OPT["clav_mid"], OPT["clav_mid"] + 0.04, APOSE[:, 0] * sgn)
    moved = NEW_W[:, SPINE2] * near * lateral * OPT["clav_max"]
    NEW_W[:, SPINE2] -= moved
    NEW_W[:, BI[P + side + "Shoulder"]] += moved
    report["sides"][side]["spine2_to_shoulder"] = {"verts": int((moved > 1e-3).sum()), "weight": round(float(moved.sum()), 2)}

# Mixamo's shoulder weights step from bone to bone in a few rows; those steps fold the skin in big swings. Smooth every
# weight round each shoulder joint over the surface (hand skin excluded).
deg = np.zeros(V); np.add.at(deg, EDGES.ravel(), 1)
for side, sgn in SIDES.items():
    J = (D[BI[P + side + "Arm"]] @ np.r_[LIVE_B[P + side + "Arm"][:3, 3], 1])[:3]
    region = (np.linalg.norm(APOSE - J, axis=1) < OPT["wsmooth_r"]) & (APOSE[:, 0] * sgn > 0) & (NEW_W[:, sorted(HAND_SUBTREE)].sum(1) < 1e-6)
    for _ in range(int(OPT["wsmooth"])):
        total = np.zeros_like(NEW_W)
        np.add.at(total, EDGES[:, 0], NEW_W[EDGES[:, 1]]); np.add.at(total, EDGES[:, 1], NEW_W[EDGES[:, 0]])
        NEW_W[region] = 0.5 * NEW_W[region] + 0.5 * total[region] / deg[region, None]

report["body_verts_over_4_influences"] = {"input": int(((W > 0).sum(1) > MAX_INFLUENCES).sum()),
                                          "before_cap": int(((NEW_W > 0).sum(1) > MAX_INFLUENCES).sum())}
NEW_W = cap_influences(NEW_W)


def rebuild(o, Wm, A, E, rest):
    """Forward-skin A (the authored A-pose) into the T-pose rest, then corrective-smooth where the blend bends."""
    fwd = skin(Wm, D_INV, A)
    aw = Wm[:, ARM_CHAIN].sum(1)
    mask = ((aw > 1e-3) & (aw < 1 - 1e-3)).astype(float)
    for _ in range(int(OPT["mask_rings"])):
        hit = np.zeros(len(A), bool)
        hit[E[mask[E[:, 0]] > 0, 1]] = True; hit[E[mask[E[:, 1]] > 0, 0]] = True
        mask = np.maximum(mask, hit.astype(float))
    for _ in range(int(OPT["mask_soft"])):
        mask = np.maximum(mask * 0.5, neighbour_mean(mask, E, len(A)))
    hand_only = Wm[:, sorted(HAND_SUBTREE)].sum(1) > 1 - 1e-6
    mask[hand_only] = 0
    smoothed = corrective_smooth(o, A, fwd, mask) if mask.any() else fwd
    new = np.where(hand_only[:, None], rest, smoothed)
    return new, {"cs_mask_verts": int((mask > 0).sum()), "hand_only_kept": int(hand_only.sum())}


def deviation(new_rest, Wm, A):
    back = skin(Wm, D, new_rest)
    err = np.linalg.norm(back - A, axis=1)
    worst = np.argsort(-err)[:200]
    dom = Wm.argmax(1)
    by_bone = {}
    for i in np.where(err > 0.002)[0]:
        n = BONES[dom[i]].replace(P, "")
        by_bone[n] = max(by_bone.get(n, 0.0), round(float(err[i] * 100), 2))
    return {"max_cm": round(float(err.max() * 100), 3), "p99_cm": round(float(np.percentile(err, 99) * 100), 3),
            "median_mm": round(float(np.median(err) * 1000), 4), "verts_over_5mm": int((err > 0.005).sum()),
            "verts_over_1cm": int((err > 0.01).sum()), "worst_by_dominant_bone_cm": dict(sorted(by_bone.items(), key=lambda t: -t[1])),
            "worst_vertex_apose": A[worst[0]].round(4).tolist()}


minv = lambda o: np.array(o.matrix_world.inverted())
allowed, NEW_REST, MESH_REPORT = {}, {}, {}
for o in MESHES:
    Wm = NEW_W if o is body else weights(o)
    rest = REST if o is body else world_co(o)
    A = APOSE if o is body else skin(Wm, D, rest)
    damage = np.linalg.norm(skin(Wm, D_INV, A) - rest, axis=1)
    hand_only = Wm[:, sorted(HAND_SUBTREE)].sum(1) > 1 - 1e-6
    rec = {"forward_vs_current_rest_mm": {"max": round(float(damage[~hand_only].max(initial=0) * 1000), 3),
                                          "verts_over_damage_mm": int((damage[~hand_only] > OPT["damage_mm"] / 1000).sum())}}
    if o is body or rec["forward_vs_current_rest_mm"]["verts_over_damage_mm"]:
        new, info = rebuild(o, Wm, A, edges_of(o), rest)
        rec.update(info, rebuilt=True)
        rec["apose_deviation"] = deviation(new, Wm, A)
        if o is body:
            rec["apose_off_mixamo_mesh_mm_before"] = round(float(nearest(A[~hand_only], MIX_MESH + T[:3, 3]).max() * 1000), 4)
        NEW_REST[o.name] = (new, Wm)
        moved = np.where(np.linalg.norm(new - rest, axis=1) > 1e-7)[0]
        rec["rest_move_cm"] = {"max": round(float(np.linalg.norm(new - rest, axis=1).max() * 100), 2),
                               "over_1cm": int((np.linalg.norm(new - rest, axis=1) > 0.01).sum())}
        allowed[o.name] = sorted(set(moved.tolist()) | (set(np.where(np.abs(Wm - weights(o)).max(1) > 1e-6)[0].tolist()) if o is body else set()))
    else:
        rec["rebuilt"] = False
    MESH_REPORT[o.name] = rec
report["meshes"] = MESH_REPORT

for o in MESHES:
    if o.name not in NEW_REST:
        continue
    new, Wm = NEW_REST[o.name]
    Mi = minv(o)
    local = new @ Mi[:3, :3].T + Mi[:3, 3]
    o.data.vertices.foreach_set("co", local.ravel())
    if o is body:
        changed = np.where(np.abs(Wm - weights(o)).max(1) > 1e-6)[0]
        for i in changed:
            for g in o.vertex_groups:
                g.remove([int(i)])
            for b in np.nonzero(Wm[i])[0]:
                (o.vertex_groups.get(BONES[b]) or o.vertex_groups.new(name=BONES[b])).add([int(i)], float(Wm[i, b]), "REPLACE")
        report["body_reweighed_verts"] = int(len(changed))
    o.data.update()

out_fbx = os.path.join(OUT, "salamander.fbx")
bpy.ops.object.select_all(action="DESELECT")
for o in bpy.context.scene.objects:
    o.select_set(o.type in {"ARMATURE", "MESH"})
bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.fbx(filepath=out_fbx, use_selection=True, object_types={"ARMATURE", "MESH"}, add_leaf_bones=False,
                         bake_anim=False, path_mode="STRIP", embed_textures=False, mesh_smooth_type="OFF",
                         use_armature_deform_only=False, armature_nodetype="NULL")

report["step3"] = compare(snapshot(LIVE), snapshot(out_fbx), allowed)
json.dump(regions, open(os.path.join(OUT, "regions.json"), "w"))
json.dump(report, open(os.path.join(OUT, "fix_report.json"), "w"), indent=1)
print("REPORT", json.dumps(report))
if not report["step3"]["bones"]["ok"]:
    os.replace(out_fbx, os.path.join(OUT, "salamander.REJECTED.fbx"))
    sys.exit("bone rest changed: Humanoid avatar and bound clips depend on it")
