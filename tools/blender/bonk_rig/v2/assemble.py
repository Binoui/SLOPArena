"""Dress the squared Sir Bonk v2 body with the kit parts and render review views.

blender -b --python-exit-code 1 <framed.blend> --python assemble.py -- <out_dir> ['<json overrides>']
Frame: the body faces -Y, his left is +X, hips centre at x=y=0, soles at z=0 (kit units, ~0.72 tall).
Per part, in this order (kit coordinates for caps/rigid/anchor, placed coordinates after that):
  caps:   delete the faces that close a tube's ends (Tripo caps every opening)
  place:  centre on the anchor bbox, scale (scalar or xyz), optional align (part PCA axis -> direction),
          rotate (XYZ euler deg), move to "at"; "follow": copy another part's whole transform instead
  clear:  slide along an axis until the test verts (the rigid subset if given) are `gap` outside the layers below
  grow:   scale about the anchor until every vertex is `gap` outside the layers below
  cloth:  clamp each movable vertex's distance to the layers below into [min, max], smoothed (max None = push only);
          the rigid subset never moves in this step
Writes dressed.blend (one object per part), fit.json and renders in <out_dir>/renders/.
"""
import bpy, bmesh, sys, os, math, json
import numpy as np
from mathutils import Vector, Matrix, Euler
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index("--") + 1:]
OUT = os.path.abspath(argv[0])
os.makedirs(os.path.join(OUT, "renders"), exist_ok=True)

CFG = {
    "tabard": {"scale": 1.38, "at": [0, 0.0, 0.527], "over": ["body"], "cloth": {"min": 0.004, "max": 0.022}},
    "sash": {"scale": 1.25, "anchor_zmin": 0.52, "at": [0, -0.018, 0.452], "over": ["body", "tabard"],
             "cloth": {"min": 0.003, "max": 0.012, "zmin": 0.43}},
    "skirt": {"scale": [0.75, 0.75, 1.12], "at": [0, -0.01, 0.375], "over": ["body", "tabard"],
              "cloth": {"min": 0.004, "max": 0.03}},
    "pants_a": {"caps": {"ends": ["top"]}, "scale": [1.25, 1.25, 2.3], "at": [-0.058, 0.0, 0.222], "over": ["legs"],
                "bend": {"z0": 0.27, "dx": -0.19, "dy": 0.25},
                "cloth": {"min": 0.003, "max": 0.012}},
    "pants_b": {"caps": {"ends": ["top"]}, "scale": [1.25, 1.25, 2.3], "at": [0.058, 0.0, 0.222], "over": ["legs"],
                "bend": {"z0": 0.27, "dx": 0.19, "dy": 0.25},
                "cloth": {"min": 0.003, "max": 0.012}},
    "boot_a": {"caps": {"ends": ["top"], "band": 0.03}, "scale": [1.15, 1.15, 1.45], "boot": "R", "splay": 8, "shaft_at": [-0.076, 0.036]},
    "boot_b": {"caps": {"ends": ["top"], "band": 0.03}, "scale": [1.15, 1.15, 1.45], "boot": "L", "splay": 8, "shaft_at": [0.076, 0.036]},
    "knee_a": {"scale": 0.8, "at": [-0.062, -0.012, 0.222], "over": ["legs", "pants_a"], "rigid": [[1, "<", -0.06]], "drop": [[1, ">", 0.004]],
               "clear": {"axis": [0, -1, 0], "gap": 0.002, "tmax": 0.05}, "cloth": {"min": 0.002, "max": 0.006}},
    "knee_b": {"scale": 0.8, "at": [0.062, -0.012, 0.222], "over": ["legs", "pants_b"], "rigid": [[1, "<", -0.08]], "drop": [[1, ">", -0.012]],
               "clear": {"axis": [0, -1, 0], "gap": 0.002, "tmax": 0.05}, "cloth": {"min": 0.002, "max": 0.006}},
    "wrap_a": {"caps": {"ends": ["top", "bottom"], "axis": "pca"}, "scale": 1.0, "align": [0.0, -0.39, -0.92],
               "at": [-0.143, 0.022, 0.425], "over": ["arms"], "cloth": {"min": 0.002, "max": 0.007}},
    "wrap_b": {"caps": {"ends": ["top", "bottom"], "axis": "pca"}, "scale": 1.0, "align": [0.0, -0.39, -0.92],
               "at": [0.143, 0.022, 0.425], "over": ["arms"], "cloth": {"min": 0.002, "max": 0.007}},
    "colander_a": {"scale": 0.75, "at": [-0.185, 0.035, 0.452], "over": ["body"],
                   "clear": {"axis": [-1, 0, 0], "gap": 0.003, "tmax": 0.05}},
    "chestplate": {"scale": 0.9, "rot": [-12, 0, 0], "at": [0, -0.03, 0.56], "over": ["torso"], "test": [[1, "<", -0.02]],
                   "clear": {"axis": [0, -1, 0], "gap": 0.004, "tmax": 0.08, "quantile": 0.2},
                   "cloth": {"min": 0.008, "max": None, "smooth": 8}},
    "cape": {"scale": [1.1, 1.0, 0.78], "at": [0, 0.102, 0.507], "arc": {"k": 3.0},
             "over": ["body", "tabard", "chestplate"], "cloth": {"min": 0.006, "max": 0.012, "zmin": 0.625, "smooth": 6}},
    "pauldron_a": {"scale": 1.0, "rot": [0, -30, -15], "at": [-0.138, 0.03, 0.62], "over": ["body"],
                   "clear": {"axis": [-0.42, 0, 0.9], "gap": 0.01, "tmax": 0.08}},
    "pauldron_b": {"scale": 1.0, "rot": [0, 30, 15], "at": [0.138, 0.03, 0.62], "over": ["body"],
                   "clear": {"axis": [0.42, 0, 0.9], "gap": 0.01, "tmax": 0.08}},
    "helm": {"scale": 1.0, "anchor_zmax": 0.876, "at": [-0.003, -0.012, 0.67], "over": ["body"],
             "clear": {"axis": [0, 0, 1], "gap": 0.003, "tmax": 0.06, "min_island": 300}},
    "helm_handle": {"follow": "helm", "over": ["body"], "cloth": {"min": 0.004, "max": None}},
    "sword": {"scale": 1.0, "at": [0.27, -0.02, None], "floor": True},
    "hide": ["colander_b"],
    "trim_body": {"zmax": 0.19},  # feet and shins are inside the boots (cuff top ~0.19): delete them so nothing pokes through
    "render": True,
}
if len(argv) > 1:
    for k, v in json.loads(argv[1]).items():
        if isinstance(v, dict) and isinstance(CFG.get(k), dict):
            CFG[k].update(v)
        else:
            CFG[k] = v
report = {"cfg": CFG, "parts": {}}
OB = {o.name: o for o in bpy.context.scene.objects if o.type == "MESH"}
XF = {}  # part -> (ctr, s, R, at, t_clear)


def verts(o):
    a = np.empty(len(o.data.vertices) * 3); o.data.vertices.foreach_get("co", a); return a.reshape(-1, 3)


def set_verts(o, P):
    o.data.vertices.foreach_set("co", np.ascontiguousarray(P, dtype=np.float64).ravel()); o.data.update()


def faces(o):
    return [tuple(p.vertices) for p in o.data.polygons]


REGIONS = {  # body subsets (face centre tests) used as fitting layers
    "legs": lambda c: abs(c[0]) < 0.105 and c[2] < 0.42,
    "arms": lambda c: abs(c[0]) > 0.1 and 0.3 < c[2] < 0.62,
    "torso": lambda c: abs(c[0]) < 0.108 and c[2] > 0.42,
}


def tree_of(names):
    allP, allF, off = [], [], 0
    for n in names:
        if n in REGIONS:
            o = OB["body"]; P = verts(o)
            F = [f for f in faces(o) if REGIONS[n](P[list(f)].mean(0))]
        else:
            o = OB[n]; P = verts(o); F = faces(o)
        allP.append(P); allF += [tuple(i + off for i in f) for f in F]; off += len(P)
    P = np.vstack(allP)
    return BVHTree.FromPolygons([Vector(p) for p in P], allF)


def signed(tree, P):
    out = np.empty(len(P)); loc = np.empty_like(P); nrm = np.empty_like(P)
    for i, p in enumerate(P):
        l, n, _, d = tree.find_nearest(Vector(p))
        if l is None:
            out[i] = 1e9; loc[i] = p; nrm[i] = (0, 0, 1); continue
        s = 1.0 if (Vector(p) - l).dot(n) >= 0 else -1.0
        out[i] = s * d; loc[i] = l; nrm[i] = n
    return out, loc, nrm


def neighbours(o):
    nb = [set() for _ in o.data.vertices]
    for e in o.data.edges:
        a, b = e.vertices; nb[a].add(b); nb[b].add(a)
    return [np.array(sorted(s), dtype=int) for s in nb]


def smooth_field(D, nb, it, fixed):
    for _ in range(it):
        D = np.array([D[n].mean(0) if len(n) else D[i] for i, n in enumerate(nb)]) * 0.5 + D * 0.5
        D[fixed] = 0
    return D


def pca_axis(P):
    Q = P - P.mean(0); w, v = np.linalg.eigh(Q.T @ Q); a = v[:, -1]
    return a if a[2] >= 0 else -a


def cut_caps(o, c):
    """Delete faces closing a tube end: normal along the axis, near the axis, inside the end band."""
    P = verts(o)
    ax = pca_axis(P) if c.get("axis") == "pca" else np.array([0, 0, 1.0])
    t = (P - P.mean(0)) @ ax; t0, t1 = t.min(), t.max(); band = c.get("band", 0.025)
    bm = bmesh.new(); bm.from_mesh(o.data); bm.faces.ensure_lookup_table(); bm.verts.ensure_lookup_table()
    kill = []
    for end in c["ends"]:
        sel = t > t1 - band if end == "top" else t < t0 + band
        ring = P[sel]; cen = ring.mean(0)
        radial = lambda p: np.linalg.norm((p - cen) - ((p - cen) @ ax) * ax)
        R = max(radial(p) for p in ring)
        for f in bm.faces:
            m = np.array(f.calc_center_median()[:]); tm = (m - P.mean(0)) @ ax
            inside = tm > t1 - band if end == "top" else tm < t0 + band
            if inside and abs(np.dot(np.array(f.normal[:]), ax)) > c.get("ndot", 0.6) and radial(m) < c.get("frac", 0.8) * R:
                kill.append(f)
    kill = list(set(kill))
    bmesh.ops.delete(bm, geom=kill, context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.to_mesh(o.data); bm.free(); o.data.update()
    return len(kill)


def drop_islands(o, rules):
    bm = bmesh.new(); bm.from_mesh(o.data); bm.faces.ensure_lookup_table()
    seen, kill = set(), []
    for f in bm.faces:
        if f.index in seen: continue
        comp, st = [], [f]; seen.add(f.index)
        while st:
            g = st.pop(); comp.append(g)
            for v in g.verts:
                for h in v.link_faces:
                    if h.index not in seen: seen.add(h.index); st.append(h)
        cen = np.array([v.co[:] for g in comp for v in g.verts]).mean(0)
        if all((cen[ax] < val) if op == "<" else (cen[ax] > val) for ax, op, val in rules):
            kill += comp
    bmesh.ops.delete(bm, geom=kill, context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.to_mesh(o.data); bm.free(); o.data.update()
    return len(kill)


def rigid_mask(P, rules):
    m = np.ones(len(P), bool)
    for ax, op, val in rules:
        m &= (P[:, ax] < val) if op == "<" else (P[:, ax] > val)
    return m


def place(o, c):
    P = verts(o)
    if "follow" in c:
        ctr, s, R, at, tc = XF[c["follow"]]
        set_verts(o, ((P - ctr) * s) @ R.T + at + tc)
        return at
    A = P
    if c.get("anchor_zmax") is not None: A = A[A[:, 2] <= c["anchor_zmax"]]
    if c.get("anchor_zmin") is not None: A = A[A[:, 2] >= c["anchor_zmin"]]
    ctr = (A.min(0) + A.max(0)) / 2
    s = np.array(c["scale"] if isinstance(c["scale"], list) else [c["scale"]] * 3, float)
    R = np.array(Euler([math.radians(a) for a in c.get("rot", [0, 0, 0])], "XYZ").to_matrix())
    if c.get("align") is not None:
        a = pca_axis(P); b = np.array(c["align"], float); b /= np.linalg.norm(b)
        if np.dot(a, b) < 0: a = -a
        R = R @ np.array(Vector(a.tolist()).rotation_difference(Vector(b.tolist())).to_matrix())
    Q = ((P - ctr) * s) @ R.T
    at = np.array([0.0 if v is None else v for v in c["at"]])
    if c.get("floor"):
        at[2] = -(Q[:, 2].min())
    set_verts(o, Q + at)
    XF[o.name] = [ctr, s, R, at, np.zeros(3)]
    return at


def bisect(ok, lo, hi, want_low):
    for _ in range(14):
        m = (lo + hi) / 2
        if ok(m) == want_low: hi = m
        else: lo = m
    return hi if want_low else lo


def islands_mask(o, min_verts):
    """Vertices that belong to face islands with at least min_verts vertices."""
    n = len(o.data.vertices); par = np.arange(n)
    def find(a):
        while par[a] != a:
            par[a] = par[par[a]]; a = par[a]
        return a
    for p in o.data.polygons:
        vs = list(p.vertices); r = find(vs[0])
        for v in vs[1:]:
            s = find(v)
            if s != r: par[s] = r
    roots = np.array([find(i) for i in range(n)])
    u, cnt = np.unique(roots, return_counts=True)
    big = set(u[cnt >= min_verts])
    return np.array([r in big for r in roots])


def clear(o, tree, c, sub):
    P = verts(o); ax = np.array(c["axis"], float); ax /= np.linalg.norm(ax)
    if c.get("min_island"): sub = sub & islands_mask(o, c["min_island"])
    T = P[sub]; q = c.get("quantile", 0.0)
    ok = lambda t: np.quantile(signed(tree, T + ax * t)[0], q) >= c["gap"]
    step = 0.002  # scan, then bisect: the test is not monotonic near ears, fingers and straps
    if ok(0.0):  # already clear: slide back in until it touches
        u = 0.0
        while u + step <= c["tmax"] and ok(-(u + step)): u += step
        t = -(u if u + step > c["tmax"] else bisect(lambda w: ok(-w), u, u + step, False))
    else:
        u = step
        while u <= c["tmax"] and not ok(u): u += step
        t = c["tmax"] if u > c["tmax"] else bisect(ok, u - step, u, True)
    set_verts(o, P + ax * t)
    if o.name in XF: XF[o.name][4] = ax * t
    return float(t)


def grow(o, tree, c, at):
    P = verts(o); ctr = np.array(at)
    ok = lambda k: signed(tree, ctr + (P - ctr) * k)[0].min() >= c["gap"]
    if ok(1.0):
        k = 0.6 if ok(0.6) else bisect(ok, 0.6, 1.0, True)
    else:
        k = c["kmax"] if not ok(c["kmax"]) else bisect(ok, 1.0, c["kmax"], True)
    set_verts(o, ctr + (P - ctr) * k)
    return float(k)


def fit_cloth(o, tree, c, fixed):
    P = verts(o); nb = neighbours(o)
    cmin, cmax = c["min"], c.get("max")
    mask = np.ones(len(P), bool) if c.get("zmin") is None else P[:, 2] >= c["zmin"]
    for it in range(c.get("iters", 6)):
        sd, loc, nrm = signed(tree, P)
        D = np.zeros_like(P)
        low = sd < cmin
        D[low] = loc[low] + nrm[low] * cmin - P[low]
        if cmax is not None:
            hi = (sd > cmax) & mask
            dirs = P[hi] - loc[hi]; dirs /= np.linalg.norm(dirs, axis=1, keepdims=True) + 1e-12
            D[hi] = loc[hi] + dirs * cmax - P[hi]
        D[fixed] = 0
        D = smooth_field(D, nb, c.get("smooth", 3), fixed)
        P = P + D
    sd, loc, nrm = signed(tree, P)
    low = (sd < cmin * 0.5) & ~fixed
    P[low] = loc[low] + nrm[low] * cmin
    set_verts(o, P)
    sd, _, _ = signed(tree, P)
    return {"min_sd": float(sd.min()), "inside": int((sd < 0).sum())}


def toe_dir(P):
    s = P[P[:, 2] < P[:, 2].min() + 0.012]
    Q = s[:, :2] - s[:, :2].mean(0); w, v = np.linalg.eigh(Q.T @ Q); a = v[:, -1]
    top = P[P[:, 2] > P[:, 2].max() - 0.02][:, :2].mean(0)  # the shaft sits over the heel
    proj = (s[:, :2] - top) @ a
    if abs(proj.max()) < abs(proj.min()): a = -a
    return a, s[:, :2].mean(0)


def fit_boot(o, c):
    side = -1 if c["boot"] == "R" else 1
    B = verts(OB["body"]); foot = B[(B[:, 0] * side > 0.03) & (B[:, 2] < 0.13)]
    fa, fc = toe_dir(foot)
    if c.get("splay") is not None:
        a = math.radians(-90 + side * c["splay"]); fa = np.array([math.cos(a), math.sin(a)])
    P = verts(o); ba, bc = toe_dir(P)
    ang = math.atan2(fa[1], fa[0]) - math.atan2(ba[1], ba[0])
    R = np.array(Matrix.Rotation(ang, 3, "Z"))
    s = np.array(c["scale"] if isinstance(c["scale"], list) else [c["scale"]] * 3, float)
    Q = ((P - np.array([bc[0], bc[1], P[:, 2].min()])) * s) @ R.T
    fproj = (foot[:, :2] - fc) @ fa; qproj = Q[:, :2] @ fa
    if c.get("shaft_at") is not None:  # centre of the shaft opening onto the given leg point
        top = Q[Q[:, 2] > Q[:, 2].max() - 0.025][:, :2]
        Q[:, :2] += np.array(c["shaft_at"]) - (top.min(0) + top.max(0)) / 2
    else:  # heel to heel
        Q[:, :2] += (fc + fa * fproj.min()) - fa * qproj.min() - fa * c.get("heel_back", 0.008)
    set_verts(o, Q)
    return {"rot_deg": round(math.degrees(ang), 2), "foot_toe_yaw": round(math.degrees(math.atan2(fa[1], fa[0])), 2)}


ORDER = ["tabard", "sash", "skirt", "pants_a", "pants_b", "boot_a", "boot_b", "knee_a", "knee_b", "wrap_a", "wrap_b",
         "colander_a", "chestplate", "cape", "pauldron_a", "pauldron_b", "helm", "helm_handle", "sword"]
for n in ORDER:
    c = CFG[n]; o = OB[n]; r = {}
    if "drop" in c: r["dropped_faces"] = drop_islands(o, c["drop"])
    if "caps" in c: r["caps_cut"] = cut_caps(o, c["caps"])
    K = verts(o)  # kit coordinates, for the rigid subset
    fixed = rigid_mask(K, c["rigid"]) if "rigid" in c else np.zeros(len(K), bool)
    if "boot" in c:
        r.update(fit_boot(o, c)); at = None
    else:
        at = place(o, c)
    if "arc" in c:  # wrap a flat sheet round the back: its sides come forward (towards -Y)
        P = verts(o); cx = (P[:, 0].min() + P[:, 0].max()) / 2
        P[:, 1] -= c["arc"]["k"] * (P[:, 0] - cx) ** 2; set_verts(o, P)
    if "bend" in c:
        P = verts(o); b = c["bend"]; drop = np.clip(b["z0"] - P[:, 2], 0, None)
        P[:, 0] += b.get("dx", 0) * drop; P[:, 1] += b.get("dy", 0) * drop; set_verts(o, P)
    tree = tree_of(c["over"]) if c.get("over") else None
    test = rigid_mask(K, c["test"]) if "test" in c else (fixed if fixed.any() else np.ones(len(K), bool))
    if "clear" in c: r["clear_t"] = clear(o, tree, c["clear"], test)
    if "grow" in c: r["grow_k"] = grow(o, tree, c["grow"], at)
    if "cloth" in c: r.update(fit_cloth(o, tree, c["cloth"], fixed))
    if tree is not None and "cloth" not in c:
        sd, _, _ = signed(tree, verts(o)); r["min_sd"] = round(float(sd.min()), 4); r["inside"] = int((sd < 0).sum())
    P = verts(o); r["lo"] = P.min(0).round(4).tolist(); r["hi"] = P.max(0).round(4).tolist()
    report["parts"][n] = r
    print("FIT", n, json.dumps(r))
if CFG.get("trim_body"):
    o = OB["body"]; bm = bmesh.new(); bm.from_mesh(o.data)
    kill = [f for f in bm.faces if max(v.co.z for v in f.verts) < CFG["trim_body"]["zmax"]]
    bmesh.ops.delete(bm, geom=kill, context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.to_mesh(o.data); bm.free(); o.data.update()
    report["trim_body_faces"] = len(kill); print("TRIM body faces", len(kill))
for n in CFG.get("hide", []):
    OB[n].hide_render = True; OB[n].hide_viewport = True
json.dump(report, open(os.path.join(OUT, "fit.json"), "w"), indent=1, default=lambda x: x.tolist() if hasattr(x, "tolist") else str(x))
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "dressed.blend"))

if CFG["render"]:
    sc = bpy.context.scene
    w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.8, 0.8, 0.84, 1)
    for rr, e in (((math.radians(50), 0, math.radians(30)), 2.6), ((math.radians(65), 0, math.radians(200)), 1.4)):
        d = bpy.data.lights.new("s", "SUN"); d.energy = e; so = bpy.data.objects.new("s", d); sc.collection.objects.link(so); so.rotation_euler = rr
    sc.render.engine = "BLENDER_EEVEE"; sc.view_settings.view_transform = "Standard"
    cd = bpy.data.cameras.new("c"); cam = bpy.data.objects.new("c", cd); sc.collection.objects.link(cam); sc.camera = cam
    cd.clip_end = 40
    views = {"front": (0, 2), "back": (180, 2), "side_r": (-90, 2), "side_l": (90, 2), "q34_r": (-30, 6), "q34_l": (30, 6),
             "concept": (-18, -4), "q34back": (150, 8)}
    tgt = Vector((0, 0.01, 0.42))
    for name, (yaw, pitch) in views.items():
        y, p = math.radians(yaw), math.radians(pitch)
        d = Vector((math.sin(y) * math.cos(p), -math.cos(y) * math.cos(p), math.sin(p)))
        if name == "concept":
            cd.type = "PERSP"; cd.lens = 50; cam.location = tgt + d * 1.45
        else:
            cd.type = "ORTHO"; cd.ortho_scale = 0.95; cam.location = tgt + d * 5
        cam.rotation_euler = (tgt - cam.location).to_track_quat("-Z", "Y").to_euler()
        sc.render.resolution_x, sc.render.resolution_y = 900, 1100
        sc.render.filepath = os.path.join(OUT, "renders", name + ".png")
        bpy.ops.render.render(write_still=True)
    for name, at, yaw, pitch, span in (("cu_helm", (0, -0.01, 0.66), -25, 8, 0.25), ("cu_legs", (0, 0, 0.2), -25, 5, 0.42),
                                        ("cu_torso", (0, 0, 0.52), -25, 5, 0.38), ("cu_back", (0, 0, 0.5), 160, 8, 0.45)):
        y, p = math.radians(yaw), math.radians(pitch)
        d = Vector((math.sin(y) * math.cos(p), -math.cos(y) * math.cos(p), math.sin(p)))
        t = Vector(at); cd.type = "ORTHO"; cd.ortho_scale = span; cam.location = t + d * 5
        cam.rotation_euler = (t - cam.location).to_track_quat("-Z", "Y").to_euler()
        sc.render.resolution_x = sc.render.resolution_y = 900
        sc.render.filepath = os.path.join(OUT, "renders", name + ".png")
        bpy.ops.render.render(write_still=True)
print("DONE", OUT)
