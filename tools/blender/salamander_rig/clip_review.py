"""Drive the salamander FBX with GenRA clips (USD) and measure and render how his torso skin follows his arms.

blender -b --factory-startup --python-exit-code 1 --python clip_review.py -- <salamander.fbx> <textures_dir> <out_dir> <spec.json> \
  [clips=<name>,...] [regions=<regions.json>] [scale=<ortho width in m, default 1.0>] [res=<px, default 480>] [clay=1] [paint=Right|Left]

spec.json: {"clips": [{"name": "haymaker", "usd": "<run>/usd/<clip>.usda", "frames": [31, 38] | "auto", "render": true}]}
Each bone takes the USD skeleton's world-space change from rest, so the FBX and USD rests only need to share joint
positions (they do; their bone axes differ). Per frame and side it measures
  rib_cm: how far rib skin (more than 4 cm below the shoulder joint, inside the chest) moves when only that arm's
          pose is applied, i.e. rib skin carried by the arm;
  stretch: the longest armpit/torso-side edge's posed length over its rest length;
  folds: armpit edges whose two faces meet at more than 120 degrees (skin folded over itself).
With regions.json (written by the v1 rebuild_rest.py, removed 2026-11-05; see git history) the rib vertices and armpit edges come from the authored A-pose and stretch
is measured against authored edge lengths; the FBX rest alone misses rib skin a bad rest has flung out with the arm.
Writes metrics.json, or metrics-<clips>.json with clips= (one clip per process keeps Blender's memory low): each
clip's maximum per side, its worst frames, and every number for the frames picked, so the file is reusable as the next
run's spec. Renders <clip>-f<frame>-<view>-<poncho|bare>.png for those frames: views "game" (behind and above) and
"front" (3/4 from his right). "auto" picks each side's worst rib_cm, stretch and folds frames.
"""
import bpy, sys, os, json, math
import numpy as np
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:]
FBX, TEX, OUT = (os.path.abspath(a) for a in argv[:3])
SPEC = json.load(open(argv[3]))
OPT = dict(a.split("=", 1) for a in argv[4:])
if "clips" in OPT:
    SPEC["clips"] = [c for c in SPEC["clips"] if c["name"] in OPT["clips"].split(",")]
REGIONS = json.load(open(OPT["regions"])) if "regions" in OPT else None
os.makedirs(OUT, exist_ok=True)
P = "mixamorig:"

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX, ignore_leaf_bones=False)
scene = bpy.context.scene
fa = next(o for o in scene.objects if o.type == "ARMATURE")
meshes = [o for o in scene.objects if o.type == "MESH"]
body = bpy.data.objects["Salamander_Body"]
poncho = bpy.data.objects.get("Salamander_Poncho")
Af = fa.matrix_world.copy()
order = [b.name for b in fa.data.bones]  # parents precede children in Blender's bone list
assert all(fa.data.bones[n].parent is None or order.index(fa.data.bones[n].parent.name) < order.index(n) for n in order)


def arm_subtree(side):
    root = fa.data.bones[P + side + "Arm"]
    return {root.name} | {c.name for c in root.children_recursive}


ARM = {s: arm_subtree(s) for s in ("Left", "Right")}


def body_world():
    dg = bpy.context.evaluated_depsgraph_get()
    ev = body.evaluated_get(dg)
    me = ev.to_mesh()
    co = np.empty(len(me.vertices) * 3, np.float32)
    me.vertices.foreach_get("co", co)
    ev.to_mesh_clear()
    co = co.reshape(-1, 3).astype(np.float64)
    M = np.array(body.matrix_world)
    return co @ M[:3, :3].T + M[:3, 3]


def set_rest():
    for pb in fa.pose.bones:
        pb.matrix_basis = Matrix()
    bpy.context.view_layer.update()


set_rest()
REST = body_world()
EDGES = np.array([e.vertices[:] for e in body.data.edges])
EDGE_INDEX = {tuple(sorted(e.vertices)): i for i, e in enumerate(body.data.edges)}
EDGE_FACES = {}
TRI, TRI_FACE = [], []
for p in body.data.polygons:
    for k in p.edge_keys:
        EDGE_FACES.setdefault(EDGE_INDEX[k], []).append(p.index)
    v = p.vertices
    for k in range(1, len(v) - 1):
        TRI.append((v[0], v[k], v[k + 1])); TRI_FACE.append(p.index)
TRI, TRI_FACE = np.array(TRI), np.array(TRI_FACE)
ADJ = np.array([(e, f[0], f[1]) for e, f in EDGE_FACES.items() if len(f) == 2])


def folds(co, edges):
    """Edges whose two faces meet at more than 120 degrees: skin folded over on itself."""
    n = np.zeros((len(body.data.polygons), 3))
    np.add.at(n, TRI_FACE, np.cross(co[TRI[:, 1]] - co[TRI[:, 0]], co[TRI[:, 2]] - co[TRI[:, 0]]))
    n /= np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-12)
    a = ADJ[np.isin(ADJ[:, 0], edges)]
    return int(((n[a[:, 1]] * n[a[:, 2]]).sum(1) < -0.5).sum())
REST_LEN = np.linalg.norm(REST[EDGES[:, 0]] - REST[EDGES[:, 1]], axis=1)
J = {s: Af @ fa.data.bones[P + s + "Arm"].head_local for s in ("Left", "Right")}
SGN = {"Left": 1.0, "Right": -1.0}
x, z = REST[:, 0], REST[:, 2]
RIB = {s: np.where((x * SGN[s] > 0) & (x * SGN[s] < abs(J[s].x) + 0.035) & (z < J[s].z - 0.04) & (z > 0.45))[0]
       for s in SGN}
mid = (REST[EDGES[:, 0]] + REST[EDGES[:, 1]]) / 2
ARMPIT = {s: np.where((mid[:, 0] * SGN[s] > 0.03) & (mid[:, 0] * SGN[s] < abs(J[s].x) + 0.06)
                      & (mid[:, 2] > J[s].z - 0.17) & (mid[:, 2] < J[s].z + 0.05))[0] for s in SGN}
if REGIONS:
    assert REGIONS["vertex_count"] == len(REST), "regions.json is for another topology"
    RIB = {s: np.array(REGIONS["rib"][s]) for s in SGN}
    ARMPIT = {s: np.array(REGIONS["armpit_edges"][s]) for s in SGN}
    REST_LEN = np.array(REGIONS["apose_edge_len"])


def pose_from_deltas(D, skip=frozenset()):
    """D: bone name -> world-space pose delta. Bones in skip keep their rest relative to their parent."""
    M = {}
    for n in order:
        b = fa.data.bones[n]
        R = b.matrix_local
        if n in skip or n not in D:
            M[n] = (M[b.parent.name] @ b.parent.matrix_local.inverted() @ R) if b.parent else R.copy()
            fa.pose.bones[n].matrix_basis = Matrix()
            continue
        M[n] = Af.inverted() @ D[n] @ Af @ R
        rel = (b.parent.matrix_local.inverted() @ R) if b.parent else R
        par = M[b.parent.name] if b.parent else Matrix()
        fa.pose.bones[n].matrix_basis = rel.inverted() @ par.inverted() @ M[n]
    bpy.context.view_layer.update()
    return M


def load_clip(path):
    before = set(bpy.data.objects)
    bpy.ops.wm.usd_import(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    ua = next(o for o in new if o.type == "ARMATURE")
    extra = [o for o in new if o.type != "MESH" and o != ua]
    for o in new:
        if o.type == "MESH":
            bpy.data.objects.remove(o)
    ua.hide_render = True
    return ua, extra


def deltas(ua, frame):
    scene.frame_set(frame)
    Aw = ua.matrix_world
    return {pb.name.replace("mixamorig_", P): Aw @ pb.matrix @ pb.bone.matrix_local.inverted() @ Aw.inverted()
            for pb in ua.pose.bones if pb.name.replace("mixamorig_", P) in fa.data.bones}


def measure(D):
    M = pose_from_deltas(D)
    full = body_world()
    err = max((Af @ M[n].translation - (D[n] @ Af @ fa.data.bones[n].head_local)).length for n in D)
    out = {"transfer_err_mm": round(err * 1000, 4)}
    edge = np.linalg.norm(full[EDGES[:, 0]] - full[EDGES[:, 1]], axis=1) / np.maximum(REST_LEN, 1e-6)
    for s in SGN:
        pose_from_deltas(D, skip=ARM[s])
        d = np.linalg.norm(full[RIB[s]] - body_world()[RIB[s]], axis=1) * 100
        out[s] = {"rib_cm": round(float(d.max()), 2), "rib_cm_p95": round(float(np.percentile(d, 95)), 2),
                  "stretch": round(float(edge[ARMPIT[s]].max()), 2), "folds": folds(full, ARMPIT[s]),
                  "rib_vert": int(RIB[s][d.argmax()]), "stretch_edge": int(ARMPIT[s][edge[ARMPIT[s]].argmax()])}
    pose_from_deltas(D)
    return out


def setup_render():
    scene.render.engine = "BLENDER_WORKBENCH"
    sh = scene.display.shading
    sh.light, sh.color_type, sh.show_cavity = "STUDIO", "SINGLE" if "clay" in OPT else "TEXTURE", True
    sh.single_color = (0.75, 0.72, 0.66)
    if "paint" in OPT:  # red: that side's arm chain, green: its Shoulder, blue: Spine2
        gi = {g.index: g.name for g in body.vertex_groups}
        side = OPT["paint"]
        col = body.data.color_attributes.new("paint", "FLOAT_COLOR", "POINT")
        for v in body.data.vertices:
            w = {gi[g.group]: g.weight for g in v.groups}
            arm = sum(x for n, x in w.items() if n.startswith(P + side) and ("Arm" in n or "Hand" in n))
            col.data[v.index].color = (arm, w.get(P + side + "Shoulder", 0), w.get(P + "Spine2", 0), 1)
        body.data.color_attributes.active_color = col
        sh.color_type = "VERTEX"
    scene.render.resolution_x = scene.render.resolution_y = int(OPT.get("res", 480))
    scene.render.film_transparent = False
    scene.world = bpy.data.worlds.new("w")
    scene.world.color = (0.16, 0.17, 0.19)
    for o in meshes:
        for m in o.data.materials:
            if not m:
                continue
            m.use_nodes = True
            png = os.path.join(TEX, m.name + ".png")
            if not os.path.exists(png):
                png = os.path.join(TEX, "Salamander_Body.png")
            node = next((n for n in m.node_tree.nodes if n.type == "TEX_IMAGE"), None) or m.node_tree.nodes.new("ShaderNodeTexImage")
            node.image = bpy.data.images.load(png, check_existing=True)
            m.node_tree.nodes.active = node
    cd = bpy.data.cameras.new("cam"); cd.type = "ORTHO"; cd.ortho_scale = float(OPT.get("scale", 1.0))
    cam = bpy.data.objects.new("cam", cd); scene.collection.objects.link(cam); scene.camera = cam
    return cam


VIEWS = {"game": Vector((0.0, 1.0, 0.75)).normalized(), "front": Vector((-0.64, -0.77, 0.15)).normalized()}


def render(cam, M, stem):
    centre = Af @ M[P + "Spine1"].translation + Vector((0, 0, 0.12 if float(OPT.get("scale", 1.0)) >= 1 else 0.06))
    for vn, d in VIEWS.items():
        cam.location = centre + d * 5
        cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
        for tag, show in (("poncho", True), ("bare", False)):
            if poncho:
                poncho.hide_render = not show
            scene.render.filepath = os.path.join(OUT, f"{stem}-{vn}-{tag}.png")
            bpy.ops.render.render(write_still=True)


cam = setup_render()
result = {"fbx": FBX, "regions": OPT.get("regions"), "rib_verts": {s: len(v) for s, v in RIB.items()}, "armpit_edges": {s: len(v) for s, v in ARMPIT.items()},
          "clips": []}
for c in SPEC["clips"]:
    ua, extra = load_clip(c["usd"])
    lo, hi = int(scene.frame_start), int(scene.frame_end)
    frames = {}
    for f in range(lo, hi + 1):
        frames[f] = measure(deltas(ua, f))
    worst = {k: {s: max(frames, key=lambda f: frames[f][s][k]) for s in SGN} for k in ("rib_cm", "stretch", "folds")}
    pick = c.get("frames", "auto")
    if pick == "auto":
        pick = sorted({f for w in worst.values() for f in w.values()})
    if c.get("render", True):
        for f in pick:
            M = pose_from_deltas(deltas(ua, f))
            render(cam, M, f"{c['name']}-f{f:03d}")
    summary = {s: {k: max(frames[f][s][k] for f in frames) for k in ("rib_cm", "rib_cm_p95", "stretch", "folds")} for s in SGN}
    result["clips"].append({"name": c["name"], "usd": c["usd"], "frames": pick, "render": c.get("render", True),
                            "range": [lo, hi], "worst_frame": worst, "max": summary,
                            "transfer_err_mm": max(v["transfer_err_mm"] for v in frames.values()),
                            "per_frame": {str(f): {s: frames[f][s] for s in SGN} for f in pick}})
    print("CLIP", c["name"], json.dumps(summary), "worst", worst)
    for o in [ua] + extra:
        bpy.data.objects.remove(o)
name = "metrics.json" if "clips" not in OPT else f"metrics-{OPT['clips'].replace(',', '+')}.json"
json.dump(result, open(os.path.join(OUT, name), "w"), indent=1)
