"""Dress the bare Tripo body with its extracted parts (mustache, hat, poncho, boots, straw) and render checks.

blender -b --python-exit-code 1 <parts.blend> --python dress.py -- <out_dir> ['<json overrides>']
Frame: the body faces -Y, his left is +X, the feet centre is at the origin, Z up (metres as imported).
Writes dressed.blend (one object per part), fit.json and renders in <out_dir>/renders/.
"""
import bpy, bmesh, sys, os, math, json
import numpy as np
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

argv = sys.argv[sys.argv.index("--") + 1:]
OUT = os.path.abspath(argv[0])
CFG = {
    "mustache": {"gap": 0.0015, "dz": 0.0, "scale": 1.0},
    "hat": {"scale": 1.22, "tilt_back": 6.0, "yaw": 90.0, "dx": 0.0, "dy": 0.0, "dz": 0.0, "cap_keep": 0.0},
    "poncho": {"yaw": 270.0, "scale": 1.5, "dx": 0.0, "dy": 0.0, "dz": 0.0, "clearance": 0.006, "push": True, "neck_z": 0.745},
    "boot": {"scale": 1.0, "splay": 8.0, "dy": 0.0, "kit_side": 1},
    "straw": {"scale": 0.9, "dir": [0.62, -0.7, -0.35], "inset": 0.006, "dy": 0.0, "dz": 0.0},
    "delete_feet": True,
    "render": True,
}
if len(argv) > 1:
    for k, v in json.loads(argv[1]).items():
        if isinstance(v, dict):
            CFG[k].update(v)
        else:
            CFG[k] = v
os.makedirs(os.path.join(OUT, "renders"), exist_ok=True)
report = {"cfg": CFG}
scene = bpy.context.scene
OB = {o.name: o for o in scene.objects if o.type == "MESH"}

# --- frame: kit body faces -X -> rotate +90 deg about Z; feet centre to the origin ---
Rz90 = Matrix.Rotation(math.radians(90), 4, "Z")
for o in OB.values():
    o.data.transform(Rz90)
B = np.array([v.co[:] for v in OB["body"].data.vertices])
feet = B[B[:, 2] < 0.03]
T = Vector((-feet[:, 0].mean(), -feet[:, 1].mean(), 0.0))
for o in OB.values():
    o.data.transform(Matrix.Translation(T))
    o.data.update()
report["frame"] = {"rotate_z_deg": 90, "translate": list(T)}

def verts(o):
    return np.array([v.co[:] for v in o.data.vertices])

def set_verts(o, P):
    for v, p in zip(o.data.vertices, P):
        v.co = Vector(p)
    o.data.update()

def xform(o, M):
    o.data.transform(M); o.data.update()

B = verts(OB["body"])
body_faces = [tuple(p.vertices) for p in OB["body"].data.polygons]
body_bvh = BVHTree.FromPolygons([Vector(p) for p in B], body_faces)
low_faces = [f for f in body_faces if max(B[i][2] for i in f) < 0.77]
low_bvh = BVHTree.FromPolygons([Vector(p) for p in B], low_faces)

def signed(tree, p):
    loc, nrm, idx, d = tree.find_nearest(Vector(p))
    if loc is None:
        return 1e9
    return d if (Vector(p) - loc).dot(nrm) >= 0 else -d

def open_loops(o):
    bm = bmesh.new(); bm.from_mesh(o.data); bm.edges.ensure_lookup_table()
    adj = {}
    for e in bm.edges:
        if len(e.link_faces) == 1:
            a, b = e.verts
            adj.setdefault(a.index, []).append(b.index); adj.setdefault(b.index, []).append(a.index)
    bm.free()
    seen, loops = set(), []
    for st in adj:
        if st in seen:
            continue
        comp, stack = [], [st]; seen.add(st)
        while stack:
            v = stack.pop(); comp.append(v)
            for w in adj[v]:
                if w not in seen:
                    seen.add(w); stack.append(w)
        loops.append(comp)
    return loops

def slab(z0, z1, cond=None):
    s = B[(B[:, 2] >= z0) & (B[:, 2] < z1)]
    return s if cond is None else s[cond(s)]

# --- landmarks ---
head = B[B[:, 2] > 0.74]
neck = slab(0.745, 0.76)
neck_c = neck[:, :2].mean(0)
skull = slab(0.895, 1.0)
skull_c = skull[:, :2].mean(0)
report["landmarks"] = {"neck_c": neck_c.tolist(), "skull_c": skull_c.tolist(),
                       "head_y": [float(head[:, 1].min()), float(head[:, 1].max())]}
for side in (-1, 1):
    for z in (0.04, 0.08, 0.11, 0.14, 0.17):
        s = slab(z, z + 0.012, lambda s: (s[:, 0] * side > 0.015) & (s[:, 1] < 0.2))
        if len(s):
            print(f"LEG side {side} z={z:.2f} centre ({s[:,0].mean():.3f},{s[:,1].mean():.3f}) "
                  f"x {s[:,0].min():.3f}..{s[:,0].max():.3f} y {s[:,1].min():.3f}..{s[:,1].max():.3f} n {len(s)}")
tailish = slab(0.0, 0.3, lambda s: np.abs(s[:, 0]) < 0.03)
print("TAILISH (|x|<0.03, z<0.3) y range", tailish[:, 1].min().round(3), tailish[:, 1].max().round(3))

# --- mustache: similarity ICP of the extra head onto the body head, then push back onto the lip ---
src = verts(OB["head2"])
c0 = src.mean(0)
yaw = math.radians(-90)
Rc = np.array([[math.cos(yaw), -math.sin(yaw), 0], [math.sin(yaw), math.cos(yaw), 0], [0, 0, 1]])
X = (src - c0) @ Rc.T
tc = np.array([head[:, 0].mean() - X[:, 0].mean(), head[:, 1].min() - X[:, 1].min(), head[:, 2].max() - X[:, 2].max()])
sc = 1.0
kd = KDTree(len(head))
for i, p in enumerate(head):
    kd.insert(Vector(p), i)
kd.balance()
for it in range(50):
    Y = sc * ((src - c0) @ Rc.T) + tc
    pairs = [kd.find(Vector(p)) for p in Y]
    tgt = np.array([q[0][:] for q in pairs]); dist = np.array([q[2] for q in pairs])
    keep = dist < max(3 * np.median(dist), 0.004)
    A, Bm = (src - c0)[keep], tgt[keep]
    ma, mb = A.mean(0), Bm.mean(0)
    U, S, Vt = np.linalg.svd((Bm - mb).T @ (A - ma))
    D = np.eye(3); D[2, 2] = np.sign(np.linalg.det(U @ Vt))
    Rc = U @ D @ Vt
    sc = np.trace(np.diag(S) @ D) / ((A - ma) ** 2).sum()
    tc = mb - sc * (ma @ Rc.T)
mus = sc * ((verts(OB["mustache"]) - c0) @ Rc.T) + tc
ms = CFG["mustache"]["scale"]
mc = mus.mean(0)
mus = mc + ms * (mus - mc)
mus[:, 2] += CFG["mustache"]["dz"]
def mus_min(dy):
    return min(signed(body_bvh, p + np.array([0, dy, 0])) for p in mus)
lo, hi = -0.02, 0.06
for _ in range(30):
    mid = (lo + hi) / 2
    if mus_min(mid) > CFG["mustache"]["gap"]:
        lo = mid
    else:
        hi = mid
mus[:, 1] += lo
if CFG["mustache"].get("wrap", 1.0) > 0:
    # Column by column (in x), slide the mustache back so its back surface follows the snout's curve.
    edges = np.linspace(mus[:, 0].min() - 1e-6, mus[:, 0].max() + 1e-6, 15)
    shift = np.zeros(len(edges) - 1); centres = (edges[:-1] + edges[1:]) / 2
    for k in range(len(edges) - 1):
        col = mus[(mus[:, 0] >= edges[k]) & (mus[:, 0] < edges[k + 1])]
        if not len(col):
            continue
        back = col[:, 1].max(); zc = col[:, 2].mean()
        hit = body_bvh.ray_cast(Vector((centres[k], -0.6, zc)), Vector((0, 1, 0)))
        if hit[0] is not None:
            shift[k] = max(0.0, hit[0].y - CFG["mustache"]["gap"] - back) * CFG["mustache"].get("wrap", 1.0)
    shift = np.convolve(np.pad(shift, 1, mode="edge"), [0.25, 0.5, 0.25], mode="valid")
    mus[:, 1] += np.interp(mus[:, 0], centres, shift)
    report["mustache_wrap_shift"] = shift.round(4).tolist()
set_verts(OB["mustache"], mus)
sd = np.array([signed(body_bvh, p) for p in mus])
report["mustache"] = {"icp_scale": float(sc), "push_dy": lo, "bounds": [mus.min(0).tolist(), mus.max(0).tolist()],
                      "signed_min_med_max": [float(sd.min()), float(np.median(sd)), float(sd.max())]}
print("MUSTACHE", json.dumps(report["mustache"]))

# --- hat: crown + brim as one piece, levelled, tipped back, scaled, set on the skull ---
hat_obs = [OB["crown"], OB["brim"]]
loop = max(open_loops(OB["crown"]), key=len)
H = verts(OB["crown"])
ring = H[loop]
rc = ring.mean(0)
w, v = np.linalg.eigh(np.cov((ring - rc).T))
n = Vector(v[:, 0]).normalized()
if n.z < 0:
    n = -n
lev = n.rotation_difference(Vector((0, 0, 1))).to_matrix().to_4x4()
tilt = Matrix.Rotation(math.radians(-CFG["hat"]["tilt_back"]), 4, "X")
S = Matrix.Scale(CFG["hat"]["scale"], 4)
hyaw = Matrix.Rotation(math.radians(CFG["hat"]["yaw"]), 4, "Z")
for o in hat_obs:
    xform(o, hyaw @ lev @ Matrix.Translation(-Vector(rc)))
# The brim closes its hole with a small cone (the "spike"): drop faces inside the crown's ring so the head fits.
ring_l = verts(OB["crown"])[loop]
ring_r = float(np.median(np.hypot(ring_l[:, 0], ring_l[:, 1])))
bm = bmesh.new(); bm.from_mesh(OB["brim"].data); bm.faces.ensure_lookup_table()
cap = [f for f in bm.faces if math.hypot(f.calc_center_median().x, f.calc_center_median().y) < ring_r * (0.97 - CFG["hat"]["cap_keep"])]
cap_z = [f.calc_center_median().z for f in cap]
bmesh.ops.delete(bm, geom=cap, context="FACES")
bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
bm.to_mesh(OB["brim"].data); bm.free(); OB["brim"].data.update()
print("HATCAP removed faces", len(cap), "ring_r", round(ring_r, 4), "cap z range", (round(min(cap_z), 4), round(max(cap_z), 4)) if cap_z else None)
for o in hat_obs:
    xform(o, tilt @ S)
target = Vector((skull_c[0] + CFG["hat"]["dx"], skull_c[1] + CFG["hat"]["dy"], 0))
for o in hat_obs:
    xform(o, Matrix.Translation(target))
HP = np.vstack([verts(o) for o in hat_obs])
def hat_inside(z):
    return sum(1 for p in HP if signed(body_bvh, p + np.array([0, 0, z])) < -0.002)
lo, hi = 0.6, 1.3
for _ in range(20):
    mid = (lo + hi) / 2
    if hat_inside(mid) > 2:
        lo = mid
    else:
        hi = mid
hz = hi + CFG["hat"]["dz"]
for o in hat_obs:
    xform(o, Matrix.Translation(Vector((0, 0, hz))))
HP = np.vstack([verts(o) for o in hat_obs])
report["hat"] = {"ring_normal_kit": list(n), "rest_z": hz, "bounds": [HP.min(0).tolist(), HP.max(0).tolist()],
                 "brim_width": float(HP[:, 0].max() - HP[:, 0].min())}
print("HAT", json.dumps(report["hat"]))

# --- poncho: yaw, scale about the neck hole, neck hole over the neck, lowered onto the shoulders ---
po = OB["poncho"]
PP = verts(po)
loops = [l for l in open_loops(po) if len(l) > 15]
nl = max(loops, key=lambda l: PP[l][:, 2].mean())
pc = PP[nl].mean(0)
a = math.radians(CFG["poncho"]["yaw"])
Ry = np.array([[math.cos(a), -math.sin(a), 0], [math.sin(a), math.cos(a), 0], [0, 0, 1]])
PP = CFG["poncho"]["scale"] * ((PP - pc) @ Ry.T)
PP[:, 0] += neck_c[0] + CFG["poncho"]["dx"]
PP[:, 1] += neck_c[1] + CFG["poncho"]["dy"]
def pon_inside(z):
    return sum(1 for p in PP if p[2] + z < 0.765 and signed(body_bvh, p + np.array([0, 0, z])) < -0.003)
if CFG["poncho"].get("neck_z"):
    hi = CFG["poncho"]["neck_z"]
else:
    lo, hi = 0.4, 1.2
    for _ in range(20):
        mid = (lo + hi) / 2
        if pon_inside(mid) > 3:
            lo = mid
        else:
            hi = mid
PP[:, 2] += hi + CFG["poncho"]["dz"]
inside_before = sum(1 for p in PP if signed(body_bvh, p) < 0)
if CFG["poncho"]["push"]:
    CLR, RAD = CFG["poncho"]["clearance"], 0.03
    kdp = KDTree(len(PP))
    for i, p in enumerate(PP):
        kdp.insert(Vector(p), i)
    kdp.balance()
    nbr = [kdp.find_range(Vector(p), RAD) for p in PP]
    for _ in range(8):
        need = []
        for p in PP:
            loc, nrm, idx, d = body_bvh.find_nearest(Vector(p))
            sdv = (Vector(p) - loc).dot(nrm)
            need.append(nrm * (CLR - sdv) if sdv < CLR else Vector())
        new = PP.copy()
        for i in range(len(PP)):
            acc, ws = Vector(), 0.0
            for (_, j, dist) in nbr[i]:
                ww = math.exp(-(dist / (RAD * 0.5)) ** 2)
                acc += need[j] * ww; ws += ww
            dd = acc / ws if ws else need[i]
            if need[i].length > dd.length:
                dd = dd.normalized() * need[i].length if dd.length > 1e-9 else need[i]
            new[i] = PP[i] + np.array(dd[:])
        PP = new
set_verts(po, PP)
inside_after = sum(1 for p in PP if signed(body_bvh, p) < 0)
report["poncho"] = {"neck_loop_n": len(nl), "rest_z": hi, "inside_before_push": inside_before, "inside_after": inside_after,
                    "bounds": [PP.min(0).tolist(), PP.max(0).tolist()]}
print("PONCHO", json.dumps(report["poncho"]))

# --- boots: the kit boot (and its strap and spur) on one foot, a mirrored copy on the other ---
boot, bits = OB["boot"], OB["bootbits"]
BT = verts(boot)
sole = BT[BT[:, 2] < BT[:, 2].min() + 0.012]
sc_ = sole[:, :2].mean(0)
w, v = np.linalg.eigh(np.cov((sole[:, :2] - sc_).T))
major = v[:, 1]
top = BT[BT[:, 2] > BT[:, 2].max() - 0.03]
shaft_c = top[:, :2].mean(0)
toe = major if np.dot(major, sc_ - shaft_c) > 0 else -major
toe_ang = math.atan2(toe[1], toe[0])
report["boot_kit"] = {"toe_dir": toe.tolist(), "sole_len": float(np.ptp(sole[:, :2] @ toe)),
                      "sole_width": float(np.ptp(sole[:, :2] @ np.array([-toe[1], toe[0]]))),
                      "height": float(np.ptp(BT[:, 2]))}
print("BOOTKIT", json.dumps(report["boot_kit"]))
boots = {}
for side in (1, -1):
    want = math.radians(-90 + side * CFG["boot"]["splay"])
    rot = Matrix.Rotation(want - toe_ang, 4, "Z")
    s_b = CFG["boot"]["scale"]
    pivot = Vector((shaft_c[0], shaft_c[1], BT[:, 2].min()))
    M = rot @ Matrix.Scale(s_b, 4) @ Matrix.Translation(-pivot)
    if side != CFG["boot"]["kit_side"]:
        M = Matrix.Scale(-1, 4, Vector((1, 0, 0))) @ M
    leg = slab(0.09, 0.17, lambda s: (s[:, 0] * side > 0.015) & (s[:, 1] < 0.2))
    target = Vector((leg[:, 0].mean(), leg[:, 1].mean() + CFG["boot"]["dy"], 0.0))
    M = Matrix.Translation(target) @ M
    for src_o, nm in ((boot, "boot"), (bits, "bootbits")):
        o = src_o.copy(); o.data = src_o.data.copy()
        o.name = o.data.name = f"{nm}_{'L' if side > 0 else 'R'}"
        scene.collection.objects.link(o)
        xform(o, M)
        if side != CFG["boot"]["kit_side"]:
            bm = bmesh.new(); bm.from_mesh(o.data)
            bmesh.ops.reverse_faces(bm, faces=bm.faces[:])
            bm.to_mesh(o.data); bm.free(); o.data.update()
        boots.setdefault(side, []).append(o)
    bv = verts(boots[side][0])
    report[f"boot_{side}"] = {"bounds": [bv.min(0).tolist(), bv.max(0).tolist()], "shin_target": list(target)}
bpy.data.objects.remove(boot); bpy.data.objects.remove(bits)

# --- straw: stem end in his left mouth corner, ear out, forward and down ---
st = OB["straw"]
SP = verts(st)
end = SP[SP[:, 2].argmin()]
far = SP[np.argmax(((SP - end) ** 2).sum(1))]
axis = Vector(far - end).normalized()
d = Vector(CFG["straw"]["dir"]).normalized()
corner_slab = slab(0.842, 0.86, lambda s: (s[:, 0] > 0.0) & (s[:, 1] < 0.02))
corner = corner_slab[corner_slab[:, 0].argmax()]
corner = Vector(corner) + Vector((0, CFG["straw"]["dy"], CFG["straw"]["dz"]))
M = (Matrix.Translation(corner - d * CFG["straw"]["inset"]) @ axis.rotation_difference(d).to_matrix().to_4x4()
     @ Matrix.Scale(CFG["straw"]["scale"], 4) @ Matrix.Translation(-Vector(end)))
xform(st, M)
report["straw"] = {"corner": list(corner)}
print("STRAW corner", [round(c, 3) for c in corner])

# --- remove the card and the extra head ---
for nm in ("card", "head2"):
    bpy.data.objects.remove(OB[nm])

# --- hidden feet: body faces inside a boot's footprint below its rim ---
if CFG["delete_feet"]:
    # Flood from the toes through body vertices below the boot rim; the tail only joins the feet through the hips.
    bodyo = OB["body"]
    rim = min(verts(bo).max(0)[2] for bo, _ in boots.values())
    limit = rim - CFG.get("feet_margin", 0.02)
    bm = bmesh.new(); bm.from_mesh(bodyo.data); bm.verts.ensure_lookup_table(); bm.faces.ensure_lookup_table()
    cand = {v.index for v in bm.verts if v.co.z < limit and v.co.y < 0.16}
    seeds = [v for v in bm.verts if v.co.z < 0.03 and abs(v.co.x) > 0.05 and v.co.y < 0.0]
    feet_set = {v.index for v in seeds}
    stack = list(seeds)
    while stack:
        v = stack.pop()
        for e in v.link_edges:
            o = e.other_vert(v)
            if o.index in cand and o.index not in feet_set:
                feet_set.add(o.index); stack.append(o)
    doomed = [f for f in bm.faces if all(v.index in feet_set for v in f.verts)]
    tailish = [bm.verts[i].co for i in cand - feet_set]
    print("FEET rim", round(rim, 4), "limit", round(limit, 4), "flooded verts", len(feet_set), "doomed faces", len(doomed),
          "candidates left (tail etc.)", len(tailish))
    bmesh.ops.delete(bm, geom=doomed, context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.to_mesh(bodyo.data); bm.free(); bodyo.data.update()
    report["deleted_foot_faces"] = len(doomed)

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "dressed.blend"))
json.dump(report, open(os.path.join(OUT, "fit.json"), "w"), indent=1, default=float)

# --- renders ---
if CFG["render"]:
    world = bpy.data.worlds.new("review"); scene.world = world; world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.72, 0.72, 0.75, 1)
    for r, e in (((math.radians(50), 0, math.radians(30)), 2.6), ((math.radians(65), 0, math.radians(200)), 1.4)):
        dl = bpy.data.lights.new("sun", "SUN"); dl.energy = e
        so = bpy.data.objects.new("sun", dl); scene.collection.objects.link(so); so.rotation_euler = r
    scene.render.engine = "BLENDER_EEVEE"
    scene.view_settings.view_transform = "Standard"
    cam_d = bpy.data.cameras.new("cam"); cam_d.type = "ORTHO"
    cam = bpy.data.objects.new("cam", cam_d); scene.collection.objects.link(cam); scene.camera = cam
    allp = np.vstack([verts(o) for o in scene.objects if o.type == "MESH"])
    lo3, hi3 = allp.min(0), allp.max(0)
    ctr = Vector((0, 0.05, (lo3[2] + hi3[2]) / 2))
    Hh = hi3[2] - lo3[2]
    def shoot(name, centre, size, yaw_d, pitch_d, res=(700, 900)):
        yw, pt = math.radians(yaw_d), math.radians(pitch_d)
        dirv = Vector((math.sin(yw) * math.cos(pt), -math.cos(yw) * math.cos(pt), math.sin(pt)))
        cam.location = centre + dirv * 6
        cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
        cam_d.ortho_scale = size; cam_d.clip_end = 20
        scene.render.resolution_x, scene.render.resolution_y = res
        scene.render.filepath = os.path.join(OUT, "renders", name + ".png")
        bpy.ops.render.render(write_still=True)
    S = 1.12 * Hh
    for name, yw, pt in (("front", 0, 3), ("q34", 35, 8), ("side", 90, 3), ("q34back", 215, 8), ("back", 180, 3),
                         ("game", 20, 35)):
        shoot(name, ctr, S, yw, pt)
    hc = Vector((0, -0.03, 0.88))
    for name, yw, pt in (("head_front", 0, 3), ("head_q34", 40, 8), ("head_side", 90, 3), ("head_top", 0, 70)):
        shoot(name, hc, 0.5, yw, pt, res=(700, 700))
    fc = Vector((0, 0.02, 0.1))
    for name, yw, pt in (("feet_front", 0, 5), ("feet_q34", 40, 15), ("feet_side", 90, 3), ("feet_back", 180, 10)):
        shoot(name, fc, 0.42, yw, pt, res=(700, 700))
print("DONE", OUT)
