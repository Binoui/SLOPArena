"""Give Dusty's poncho spring chains on a candidate FBX; the live FBX is only read.

systemd-run --user --scope -q -p MemoryMax=3G -p MemorySwapMax=1G blender -b --factory-startup --python-exit-code 1 \
  --python tools/blender/salamander_rig/spring_poncho.py -- <live salamander.fbx> <out_dir> ['<json options>']

Adds ten leaf chains of four bones (`Salamander_Poncho_<chain>_<bone>`) under `mixamorig:Spine2`, five down the front
panel and five down the back, in ring order: front from his right to his left, then back from his left to his right.
Their joints sit on the fabric at flat-blanket positions (the poncho's planar `Blanket` UV is its unfolded
44 x 84 cm rectangle), from just outside the collar to the hem. Only the poncho is reweighted: the collar keeps its
spine and neck weights, the fabric over each shoulder top shifts part of that onto the shoulder bone, and below the
collar the weights ramp onto the chains (two neighbouring chains x two bone levels, at most four influences).
The export uses build_rig.py's FBX call. It is re-imported and compared with the live file: existing bone names,
parents and rest frames (what Unity imports: heads within 1e-5 m, orientations within 1e-4), every other mesh exact, the poncho's
vertices, faces, UVs and materials exact. FBX stores no bone tails, so Blender's guessed tail length for a bone that
gained children (Spine2) is reported, not compared. Any difference renames the export to *.REJECTED.fbx and fails.
Writes salamander_spring_poncho.fbx, chains.json (ring order, joint positions, suggested collision capsules) and
report.json (the comparison).
"""
import bpy, sys, os, json
import numpy as np
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
LIVE, OUT = os.path.abspath(argv[0]), os.path.abspath(argv[1])
OPT = {"columns": [-0.2, -0.1, 0.0, 0.1, 0.2], "root": 0.10, "segment": 0.08, "bones": 4,
       "ramp": [0.09, 0.16], "shoulder": 0.5, "shoulder_x": [0.08, 0.16], "shoulder_d": [0.04, 0.12],
       "width": 0.44, "length": 0.84, **(json.loads(argv[2]) if len(argv) > 2 else {})}
os.makedirs(OUT, exist_ok=True)
P = "mixamorig:"
PONCHO = "Salamander_Poncho"
MAX_INFLUENCES = 4


def snapshot(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path, ignore_leaf_bones=False)
    a = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
    bpy.context.view_layer.objects.active = a
    bpy.ops.object.mode_set(mode="EDIT")
    A = a.matrix_world
    bones = {b.name: (b.parent.name if b.parent else None, np.array(A @ b.head), np.array(A @ b.tail), b.roll) for b in a.data.edit_bones}
    bpy.ops.object.mode_set(mode="OBJECT")
    # What Unity imports: each node's rest frame. FBX stores no tails; Blender guesses tail length from children.
    frames = {}
    for b in a.data.bones:
        m = np.array(A @ b.matrix_local)
        m[:3, :3] /= np.linalg.norm(m[:3, :3], axis=0)
        frames[b.name] = m
    meshes = {}
    for o in bpy.context.scene.objects:
        if o.type != "MESH":
            continue
        me = o.data
        w = np.zeros((len(me.vertices), len(o.vertex_groups)))
        for v in me.vertices:
            for g in v.groups:
                w[v.index, g.group] = g.weight
        meshes[o.name] = {"matrix": np.array(o.matrix_world), "parent": o.parent.name if o.parent else None,
                          "co": np.array([v.co for v in me.vertices]), "polys": [tuple(p.vertices) for p in me.polygons],
                          "materials": [m.name if m else None for m in me.materials],
                          "mat_index": [p.material_index for p in me.polygons],
                          "uv": {l.name: np.array([d.uv for d in l.data]) for l in me.uv_layers},
                          "weights": {g.name: w[:, g.index] for g in o.vertex_groups}}
    return {"arm": np.array(a.matrix_world), "bones": bones, "frames": frames, "meshes": meshes}


def compare(old, new, added):
    ob, nb = old["bones"], new["bones"]
    kept = [n for n in nb if n in ob]
    same = sorted(kept) == sorted(ob) and all(ob[n][0] == nb[n][0] for n in ob)
    dev = {k: max(float(np.abs(ob[n][i] - nb[n][i]).max()) for n in ob) for k, i in (("head", 1), ("roll", 3))} if same else {}
    if same:
        frame_dev = {n: (float(np.abs(old["frames"][n][:3, 3] - new["frames"][n][:3, 3]).max()),
                         float(np.abs(old["frames"][n][:3, :3] - new["frames"][n][:3, :3]).max())) for n in ob}
        dev["rest_frame_position"] = max(v[0] for v in frame_dev.values())
        dev["rest_frame_rotation"] = max(v[1] for v in frame_dev.values())
        direction = lambda b: (b[2] - b[1]) / np.linalg.norm(b[2] - b[1])
        dev["tail_direction"] = max(float(np.abs(direction(ob[n]) - direction(nb[n])).max()) for n in ob)
    tails = {n: round(float(np.linalg.norm(nb[n][2] - nb[n][1]) - np.linalg.norm(ob[n][2] - ob[n][1])), 4)
             for n in ob if same and np.abs(ob[n][2] - nb[n][2]).max() > 1e-5}
    extra = sorted(set(nb) - set(ob))
    leaves = all(nb[n][0] == P + "Spine2" or nb[n][0] in added for n in extra) and not any(nb[n][0] in added for n in ob)
    # Positions to 1e-5 m. Edit mode re-derives every bone matrix in float32, so orientations get 1e-4 (0.006 deg).
    bones_ok = bool(same and max(dev["head"], dev["rest_frame_position"]) <= 1e-5
                    and max(dev["roll"], dev["rest_frame_rotation"], dev["tail_direction"]) <= 1e-4
                    and np.abs(old["arm"] - new["arm"]).max() <= 1e-6 and extra == sorted(added) and leaves)
    rep = {"bones": {"live": len(ob), "candidate": len(nb), "added": len(extra),
                     "existing_names_parents_equal": same, "added_are_poncho_leaves_under_spine2": leaves,
                     "max_dev_m_rad": {k: float(f"{v:.1e}") for k, v in dev.items()},
                     "blender_tail_length_change_m": tails,
                     "worst_rest_frames": dict(sorted(frame_dev.items(), key=lambda kv: -max(kv[1]))[:4]) if same else {},
                     "ok": bones_ok}, "meshes": {}}
    ok = bones_ok and sorted(old["meshes"]) == sorted(new["meshes"])
    for name, a in old["meshes"].items():
        b = new["meshes"][name]
        m = {"verts": len(b["co"]), "co_max_dev": float(np.abs(a["co"] - b["co"]).max()) if a["co"].shape == b["co"].shape else None,
             "topology_equal": a["polys"] == b["polys"],
             "materials_equal": a["materials"] == b["materials"] and a["mat_index"] == b["mat_index"],
             "uv_max_dev": max((float(np.abs(a["uv"][k] - b["uv"][k]).max()) for k in a["uv"]), default=0.0)
             if a["uv"].keys() == b["uv"].keys() else None,
             "object_equal": bool(np.abs(a["matrix"] - b["matrix"]).max() <= 1e-6 and a["parent"] == b["parent"])}
        wdev = np.zeros(len(a["co"]))
        for k in set(a["weights"]) | set(b["weights"]):
            wdev = np.maximum(wdev, np.abs(a["weights"].get(k, 0) - b["weights"].get(k, 0)))
        m["reweighed_verts"] = int((wdev > 1e-5).sum())
        m["ok"] = bool(m["co_max_dev"] is not None and m["co_max_dev"] <= 1e-6 and m["topology_equal"] and m["materials_equal"]
                       and m["uv_max_dev"] is not None and m["uv_max_dev"] <= 1e-6 and m["object_equal"]
                       and (name == PONCHO or m["reweighed_verts"] == 0))
        ok = ok and m["ok"]
        rep["meshes"][name] = m
    rep["verified"] = bool(ok)
    return rep


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def world(o):
    M = np.array(o.matrix_world)
    co = np.array([v.co for v in o.data.vertices])
    return co @ M[:3, :3].T + M[:3, 3]


def weights(o):
    w = np.zeros((len(o.data.vertices), len(o.vertex_groups)))
    for v in o.data.vertices:
        for g in v.groups:
            w[v.index, g.group] = g.weight
    return w


before = snapshot(LIVE)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=LIVE, ignore_leaf_bones=False)
arm = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
poncho = bpy.data.objects[PONCHO]
me = poncho.data
X = world(poncho)
uv = np.zeros((len(me.vertices), 2))
for loop in me.loops:
    uv[loop.vertex_index] = me.uv_layers["Blanket"].data[loop.index].uv
flat_x = (uv[:, 0] - 0.5) * OPT["width"]
flat_s = (uv[:, 1] - 0.5) * OPT["length"]
assert np.abs(flat_x).max() <= OPT["width"] / 2 + 1e-4 and np.abs(flat_s).max() <= OPT["length"] / 2 + 1e-4, "Blanket UV is not the unfolded rectangle"
# Front is -Y; the planar UV's low-v half is the front panel.
assert X[flat_s < -0.3, 1].mean() < X[flat_s > 0.3, 1].mean(), "Expected the low-v half of the blanket in front"

cols, nb = OPT["columns"], OPT["bones"]
levels = [OPT["root"] + j * OPT["segment"] for j in range(nb + 1)]
assert abs(levels[-1] - OPT["length"] / 2) < 1e-6, "The last joint must be the hem"
# Ring order: front from his right (-X) to his left, then back from his left to his right.
ring = [("front", x) for x in cols] + [("back", x) for x in reversed(cols)]


def joint(panel, x, d):
    sign = -1.0 if panel == "front" else 1.0
    du = np.hypot(flat_x - x, flat_s - sign * d)
    near = np.argsort(du)[:4]
    assert du[near[0]] < 0.012, f"No fabric at {panel} x={x} d={d}"
    w = 1.0 / (du[near] + 1e-3)
    return (X[near] * w[:, None]).sum(0) / w.sum()


joints = [[joint(panel, x, d) for d in levels] for panel, x in ring]
names = [[f"Salamander_Poncho_{c:02d}_{j:02d}" for j in range(nb)] for c in range(len(ring))]
inv = arm.matrix_world.inverted()
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="EDIT")
eb = arm.data.edit_bones
for c in range(len(ring)):
    parent = eb[P + "Spine2"]
    for j in range(nb):
        b = eb.new(names[c][j])
        b.head = inv @ Vector(joints[c][j])
        b.tail = inv @ Vector(joints[c][j + 1])
        b.roll = 0.0
        b.parent = parent
        b.use_connect = False
        b.use_deform = True
        parent = b
bpy.ops.object.mode_set(mode="OBJECT")

# Weights, in the flat blanket frame.
W0 = weights(poncho)
groups = [g.name for g in poncho.vertex_groups]
for side in ("Left", "Right"):
    if P + side + "Shoulder" not in groups:
        poncho.vertex_groups.new(name=P + side + "Shoulder")
        groups.append(P + side + "Shoulder")
for row in names:
    for n in row:
        poncho.vertex_groups.new(name=n)
        groups.append(n)
W = np.zeros((len(X), len(groups)))
W[:, :W0.shape[1]] = W0
gi = {n: i for i, n in enumerate(groups)}
d = np.abs(flat_s)
body = W.copy()
# The fabric over each shoulder top rides the shoulder bone, so a shrug or raised arm lifts it instead of cutting it.
spine = body[:, gi[P + "Spine2"]] + body[:, gi[P + "Neck"]]
shoulder = OPT["shoulder"] * smoothstep(*OPT["shoulder_x"], np.abs(flat_x)) * (1 - smoothstep(*OPT["shoulder_d"], d))
for side, mask in (("Left", flat_x > 0), ("Right", flat_x < 0)):
    moved = np.where(mask, shoulder, 0.0)
    for bone in (P + "Spine2", P + "Neck"):
        body[:, gi[bone]] *= 1 - moved
    body[:, gi[P + side + "Shoulder"]] += moved * spine
chain = np.zeros_like(W)
step = cols[1] - cols[0]
xc = np.clip(flat_x, cols[0], cols[-1])
ci = np.minimum(((xc - cols[0]) / step).astype(int), len(cols) - 2)
cf = (xc - (cols[0] + ci * step)) / step
centres = [levels[j] + OPT["segment"] / 2 for j in range(nb)]
dc = np.clip(d, centres[0], centres[-1])
li = np.minimum(((dc - centres[0]) / OPT["segment"]).astype(int), nb - 2)
lf = (dc - (centres[0] + li * OPT["segment"])) / OPT["segment"]
ring_index = {(panel, x): c for c, (panel, x) in enumerate(ring)}
for v in range(len(X)):
    panel = "front" if flat_s[v] < 0 else "back"
    for col, wc in ((ci[v], 1 - cf[v]), (ci[v] + 1, cf[v])):
        c = ring_index[(panel, cols[col])]
        for lev, wl in ((li[v], 1 - lf[v]), (li[v] + 1, lf[v])):
            chain[v, gi[names[c][lev]]] += wc * wl
k = smoothstep(*OPT["ramp"], d)[:, None]
W = (1 - k) * body + k * chain
order = np.argsort(-W, axis=1)
W[np.arange(len(W))[:, None], order[:, MAX_INFLUENCES:]] = 0
W[W < 1e-3] = 0
W /= W.sum(1, keepdims=True)
for g in poncho.vertex_groups:
    g.remove(list(range(len(X))))
for v in range(len(X)):
    for b in np.nonzero(W[v])[0]:
        poncho.vertex_groups[groups[b]].add([v], float(W[v, b]), "REPLACE")
me.update()

# Suggested collision capsules: limb skin around each segment, measured in this rest.
def capsule(mesh, a, b):
    o = bpy.data.objects[mesh]
    ow = weights(o)
    og = {g.name: g.index for g in o.vertex_groups}
    pa = np.array(arm.matrix_world @ arm.data.bones[a].head_local)
    pb = np.array(arm.matrix_world @ arm.data.bones[b].head_local)
    axis = pb - pa
    pts = world(o)
    t = (pts - pa) @ axis / (axis @ axis)
    own = ow[:, og[a]] > 0.5 if a in og else np.zeros(len(pts), bool)
    sel = own & (t > 0.2) & (t < 0.8)
    r = np.linalg.norm(pts - (pa + t[:, None] * axis), axis=1)
    return {"start": a, "end": b, "mesh": mesh, "samples": int(sel.sum()),
            "radius_median": float(np.median(r[sel])) if sel.any() else None,
            "radius_p90": float(np.percentile(r[sel], 90)) if sel.any() else None}


capsules = [capsule("Salamander_Jeans", P + s + "UpLeg", P + s + "Leg") for s in ("Left", "Right")]
capsules += [capsule("Salamander_Jeans", P + s + "Leg", P + s + "Foot") for s in ("Left", "Right")]
capsules += [capsule("Salamander_Body", P + s + a, P + s + b) for s in ("Left", "Right") for a, b in (("Arm", "ForeArm"), ("ForeArm", "Hand"))]
capsules += [capsule("Salamander_Body", "Tail1", "Tail2"), capsule("Salamander_Body", "Tail2", "Tail3")]

out_fbx = os.path.join(OUT, "salamander_spring_poncho.fbx")
bpy.ops.object.select_all(action="DESELECT")
for o in bpy.context.scene.objects:
    o.select_set(o.type in {"ARMATURE", "MESH"})
bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.fbx(filepath=out_fbx, use_selection=True, object_types={"ARMATURE", "MESH"}, add_leaf_bones=False,
                         bake_anim=False, path_mode="STRIP", embed_textures=False, mesh_smooth_type="OFF",
                         use_armature_deform_only=False, armature_nodetype="NULL")
influences = (W > 0).sum(1)
chains = {"source": LIVE, "options": OPT, "parent": P + "Spine2",
          "ring": [{"panel": panel, "flat_x": x, "bones": names[c], "joints": [j.round(5).tolist() for j in joints[c]],
                    "segment_lengths": [float(np.linalg.norm(joints[c][j + 1] - joints[c][j])) for j in range(nb)]}
                   for c, (panel, x) in enumerate(ring)],
          "capsules": capsules,
          "weights": {"max_influences": int(influences.max()), "chain_weighted_verts": int((W[:, [gi[n] for r in names for n in r]].sum(1) > 0.5).sum()),
                      "shoulder_weighted_verts": int((W[:, [gi[P + "LeftShoulder"], gi[P + "RightShoulder"]]].sum(1) > 0.05).sum())}}
json.dump(chains, open(os.path.join(OUT, "chains.json"), "w"), indent=1)
report = compare(before, snapshot(out_fbx), [n for r in names for n in r])
report["live"] = LIVE
report["candidate"] = out_fbx
json.dump(report, open(os.path.join(OUT, "report.json"), "w"), indent=1)
print("REPORT", json.dumps(report))
if not report["verified"]:
    os.replace(out_fbx, os.path.join(OUT, "salamander_spring_poncho.REJECTED.fbx"))
    sys.exit("candidate changed something besides the poncho weights and the added chains")
