"""Raise the arms of the dressed Sir Bonk into an A-pose for Mixamo, carrying the arm pieces with them.

blender -b --python-exit-code 1 <dressed.blend> --python apose.py -- <out_dir> ['<json overrides>']
The body's arms hang ~1 cm from his sides; Mixamo's auto-rigger wants them clear. Each arm turns about its
shoulder joint (axis +Y) by `deg`, blended by an arm weight: a hard cut along his side below the armpit, a soft
blend over the deltoid. Pieces follow by mode: "arm" (rigid, full turn), "nearest" (per vertex, the body's
weight; armpit cloth reads only shoulder-and-torso skin), "mean" (rigid, the mean of the nearest weights),
anything else stays put. Writes apose.blend and renders.
"""
import bpy, bmesh, sys, os, math, json
import numpy as np
from mathutils import Vector, Matrix
from mathutils.kdtree import KDTree

argv = sys.argv[sys.argv.index("--") + 1:]
OUT = os.path.abspath(argv[0]); os.makedirs(os.path.join(OUT, "renders"), exist_ok=True)
CFG = {
    "deg": 35.0,
    "joint": [0.105, 0.025, 0.585],  # left shoulder joint; the right one is mirrored in x
    "cut_x": 0.106,  # his side below the armpit, |x|
    "waist_z": 0.47, "waist_x": 0.085,
    "arm_pin_z": [0.5, 0.56, 0.62], "arm_pin_x": [0.108, 0.11, 0.105],
    "torso_pin_z": [0.5, 0.55, 0.6, 0.63], "torso_pin_x": [0.098, 0.095, 0.08, 0.06],
    "shoulder_z": [0.55, 0.64], "shoulder_x": [0.106, 0.075], "blend": [0.003, 0.025],
    "modes": {"wrap_a": "arm-R", "colander_a": "arm-R", "wrap_b": "arm-L", "pauldron_a": "mean", "pauldron_b": "mean",
              "tabard": "nearest"},
    "render": True,
}
if len(argv) > 1:
    CFG.update(json.loads(argv[1]))
OB = {o.name: o for o in bpy.context.scene.objects if o.type == "MESH"}


def verts(o):
    a = np.empty(len(o.data.vertices) * 3); o.data.vertices.foreach_get("co", a); return a.reshape(-1, 3)


def set_verts(o, P):
    o.data.vertices.foreach_set("co", np.ascontiguousarray(P, dtype=np.float64).ravel()); o.data.update()


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / np.maximum(e1 - e0, 1e-9), 0, 1); return t * t * (3 - 2 * t)


body = OB["body"]; B = verts(body)
# the upper-body island (torso, arms, head) holds the arms; legs, shorts and eyes are their own islands
n = len(B); par = np.arange(n)
def find(a):
    while par[a] != a:
        par[a] = par[par[a]]; a = par[a]
    return a
for p in body.data.polygons:
    vs = list(p.vertices); r = find(vs[0])
    for v in vs[1:]:
        s = find(v)
        if s != r: par[s] = r
roots = np.array([find(i) for i in range(n)])
upper = roots == roots[np.argmax(B[:, 2])]


def arm_weight(P, side):
    ax = P[:, 0] * side; z = P[:, 2]
    z0, z1 = CFG["shoulder_z"]; t = np.clip((z - z0) / (z1 - z0), 0, 1)
    b = CFG["shoulder_x"][0] + (CFG["shoulder_x"][1] - CFG["shoulder_x"][0]) * t
    b = np.where(z < CFG["waist_z"], CFG["waist_x"], b)  # below the lats the torso is narrow: the fists curl in past |x| 0.106
    h = CFG["blend"][0] + (CFG["blend"][1] - CFG["blend"][0]) * np.clip((z - 0.53) / 0.07, 0, 1)
    return smoothstep(b - h, b + h, ax)


def turn(P, w, side):
    J = np.array(CFG["joint"]) * [side, 1, 1]
    phi = -side * math.radians(CFG["deg"]) * w
    c, s = np.cos(phi), np.sin(phi); Q = P - J
    return J + np.stack([Q[:, 0] * c + Q[:, 2] * s, Q[:, 1], -Q[:, 0] * s + Q[:, 2] * c], 1)


W = {side: np.where(upper, arm_weight(B, side), 0.0) for side in (-1, 1)}
if CFG.get("harmonic", True):
    # Smooth the shoulder: pin the arm below the armpit to 1 and the torso, neck and head to 0, then relax the
    # rest over the mesh (a harmonic fill, like bone heat). The old piecewise blend left a ledge under the biceps.
    E = np.array([e.vertices[:] for e in body.data.edges])
    for side in (-1, 1):
        ax = B[:, 0] * side; z = B[:, 2]
        # arm, deltoid included, stays rigid; torso, traps and neck stay put; only the band between them blends
        ab = np.interp(z, CFG["arm_pin_z"], CFG["arm_pin_x"]); tb = np.interp(z, CFG["torso_pin_z"], CFG["torso_pin_x"])
        arm_lo = upper & (ax > ab) & (W[side] > 0.5) | upper & (ax > ab) & (z >= CFG["shoulder_z"][0])
        torso = upper & ((ax < tb) | (z > CFG["shoulder_z"][1]))
        w = W[side].copy(); w[arm_lo] = 1.0; w[torso] = 0.0
        free = upper & ~arm_lo & ~torso
        deg = np.zeros(len(B)); np.add.at(deg, E[:, 0], 1); np.add.at(deg, E[:, 1], 1)
        for _ in range(CFG.get("relax_iters", 800)):
            s = np.zeros(len(B)); np.add.at(s, E[:, 0], w[E[:, 1]]); np.add.at(s, E[:, 1], w[E[:, 0]])
            w = np.where(free & (deg > 0), s / np.maximum(deg, 1), w)
        W[side] = np.where(upper, w, 0.0)
report = {"cfg": CFG, "parts": {}}
# parts first (they read the body's rest pose), then the body
kd_all = KDTree(n)
for i, p in enumerate(B): kd_all.insert(Vector(p), i)
kd_all.balance()
# armpit cloth reads only shoulder and torso skin: no arm or fist below the shoulder blend
skin = np.where(upper & ((B[:, 2] > CFG["shoulder_z"][0]) | (np.maximum(W[-1], W[1]) < 0.05)))[0]
kd_skin = KDTree(len(skin))
for j, i in enumerate(skin): kd_skin.insert(Vector(B[i]), j)
kd_skin.balance()
for name, o in OB.items():
    if name == "body": continue
    mode = CFG["modes"].get(name)
    if mode is None: continue
    P = verts(o)
    if mode.startswith("arm-"):
        side = -1 if mode == "arm-R" else 1
        set_verts(o, turn(P, np.ones(len(P)), side)); report["parts"][name] = {"mode": mode}; continue
    tree, ids = (kd_skin, skin) if mode == "nearest" else (kd_all, np.arange(n))
    nn = np.array([ids[tree.find(Vector(p))[1]] for p in P])
    Q = P.copy()
    for side in (-1, 1):
        w = W[side][nn]
        if mode == "mean":
            if w.mean() < 1e-3: continue
            w = np.full(len(P), w.mean())
        if w.max() < 1e-3: continue
        Q = turn(Q, w, side)
        report["parts"][name] = {"mode": mode, "side": side, "w_mean": round(float(w.mean()), 3), "w_max": round(float(w.max()), 3)}
    set_verts(o, Q)
Pb = B.copy()
for side in (-1, 1):
    Pb = turn(Pb, W[side], side)
set_verts(body, Pb)
report["body_arm_verts"] = {str(s): int((W[s] > 0.5).sum()) for s in (-1, 1)}
print("APOSE", json.dumps(report["parts"]), report["body_arm_verts"])
json.dump(report, open(os.path.join(OUT, "apose.json"), "w"), indent=1)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "apose.blend"))

if CFG["render"]:
    sc = bpy.context.scene
    w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.8, 0.8, 0.84, 1)
    for rr, e in (((math.radians(50), 0, math.radians(30)), 2.6), ((math.radians(65), 0, math.radians(200)), 1.4)):
        d = bpy.data.lights.new("s", "SUN"); d.energy = e; so = bpy.data.objects.new("s", d); sc.collection.objects.link(so); so.rotation_euler = rr
    sc.render.engine = "BLENDER_EEVEE"; sc.view_settings.view_transform = "Standard"
    cd = bpy.data.cameras.new("c"); cd.type = "ORTHO"; cam = bpy.data.objects.new("c", cd); sc.collection.objects.link(cam); sc.camera = cam
    cd.clip_end = 40
    for o in sc.objects:
        if o.type == "MESH" and o.name in ("sword", "colander_b"): o.hide_render = True
    for name, t, yaw, pitch, span, res in (("front", (0, 0.01, 0.42), 0, 2, 1.0, (1100, 1000)), ("back", (0, 0.01, 0.42), 180, 2, 1.0, (1100, 1000)),
                                           ("q34", (0, 0.01, 0.42), -30, 6, 1.0, (1100, 1000)),
                                           ("cu_shoulder_R", (-0.12, 0.02, 0.56), -20, 10, 0.26, (800, 800)),
                                           ("cu_shoulder_L", (0.12, 0.02, 0.56), 20, 10, 0.26, (800, 800)),
                                           ("cu_armpit_R", (-0.12, 0.02, 0.5), -100, 0, 0.26, (800, 800))):
        y, p = math.radians(yaw), math.radians(pitch)
        d = Vector((math.sin(y) * math.cos(p), -math.cos(y) * math.cos(p), math.sin(p)))
        cd.ortho_scale = span; tv = Vector(t); cam.location = tv + d * 5
        cam.rotation_euler = (tv - cam.location).to_track_quat("-Z", "Y").to_euler()
        sc.render.resolution_x, sc.render.resolution_y = res
        sc.render.filepath = os.path.join(OUT, "renders", name + ".png"); bpy.ops.render.render(write_still=True)
print("DONE", OUT)
