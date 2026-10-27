"""Fit Tripo's jeans onto the salamander's body in the Mixamo A-pose frame.

blender -b --factory-startup --python-exit-code 1 --python fit_jeans.py -- \
    <jeans.fbx> <jeans_tex.png> <mixamo.fbx> <body_tex.png> <out_dir> '<json options>'

The jeans come alone, 1 m tall, front to -Y, legs straight. The fit is a per-height box map: at each height the
jeans' cross-section (per leg below the crotch, the whole ring above) is mapped onto the body's cross-section at the
matching height, with some ease, so the jeans keep their own folds:
  heights: jeans hem / crotch / waist -> body hem (inside the boot) / crotch / waist;
  legs below the crotch follow each body leg's section; the seat and waist follow the pelvis ring; a band above the
  crotch blends the two;
  near the boot top the legs are held inside the boot shaft (tucked in).
Then any jeans point inside the body or closer than `clear` is pushed out along the body normal (smoothed), and the
seat faces over the tail root are cut away (the tail comes out through a hole).
Writes fitted.blend (objects: body, jeans) and review renders.
"""
import bpy, bmesh, sys, os, math, json, heapq
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index("--") + 1:]
JF, JTEX, MF, BTEX, OUT = (os.path.abspath(a) for a in argv[:5])
O = {"hem": 0.125, "crotch": 0.352, "waist": 0.495, "leg_k": 1.12, "leg_a": 0.008, "hip_k": 1.06, "hip_a": 0.006,
     "blend": 0.05, "hem_skip": 0.05, "waist_skip": 0.04, "waist_k": 1.0, "waist_a": 0.003, "clear": 0.005, "boot_in": 0.006, "hand_x": 0.145, "tail_cut": 0.006, "tail_grow": 0.0, "smooth": 6, "arm_r": 0.045, "tube_margin": 0.0, "patch_inset": 1.0, "patch_pad": 0.015, "patch_turn": 8, "patch_lift": 0.003, "sub_r": 0.12, "sub_cuts": 2, "patch_lut": 360, "patch_ext": 0.7, "patch_half": 0.07, "patch_up": 0.012, "rim_from": 0.9, "rim_dark": 0.35, "patch_grid": 28, "patch_gap": 0.0, "patch_steep": -0.2, "patch_blur": 4.0, "stitch_in": 0.88, "stitch_scale": 0.6}
if len(argv) > 5:
    O.update(json.loads(argv[5]))
os.makedirs(OUT, exist_ok=True)
P = "mixamorig:"

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=JF)
jeans = next(o for o in bpy.data.objects if o.type == "MESH")
jeans.data.transform(jeans.matrix_world); jeans.matrix_world.identity()
bpy.ops.import_scene.fbx(filepath=MF)
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
if arm.animation_data:
    arm.animation_data.action = None
for pb in arm.pose.bones:
    pb.matrix_basis.identity()
bpy.context.view_layer.update()
body = next(o for o in bpy.data.objects if o.type == "MESH" and o is not jeans)
AW = arm.matrix_world.copy()
def joint(n):
    return AW @ arm.data.bones[P + n].head_local
BV = [body.matrix_world @ v.co for v in body.data.vertices]


def islands(me):
    bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
    seen, out = set(), []
    for v in bm.verts:
        if v.index in seen:
            continue
        comp, st = [], [v]; seen.add(v.index)
        while st:
            a = st.pop(); comp.append(a.index)
            for e in a.link_edges:
                b = e.other_vert(a)
                if b.index not in seen:
                    seen.add(b.index); st.append(b)
        out.append(comp)
    bm.free()
    return sorted(out, key=len, reverse=True)

B_isl = islands(body.data)
BMAIN = set(B_isl[0])
BOOTS = [s for s in B_isl[1:] if len(s) > 150 and max(BV[i].z for i in s) < 0.2]
boot_top = max(BV[i].z for s in BOOTS for i in s)
JV = [v.co.copy() for v in jeans.data.vertices]
J_isl = islands(jeans.data)
JMAIN = set(J_isl[0])


def section(V, edges, keep, z):
    """Points where mesh edges (both ends in keep) cross the plane at height z."""
    pts = []
    for a, b in edges:
        if a in keep and b in keep:
            za, zb = V[a].z, V[b].z
            if (za - z) * (zb - z) < 0:
                t = (z - za) / (zb - za)
                pts.append(V[a].lerp(V[b], t))
    return pts

B_edges = [tuple(e.vertices) for e in body.data.edges]
J_edges = [tuple(e.vertices) for e in jeans.data.edges]

# body sets: the pelvis ring without hands; each leg without the tail (distance to the leg bones)
from mathutils.geometry import intersect_point_line
def seg_dist(p, a, b):
    _, f = intersect_point_line(p, a, b)
    return (p - a.lerp(b, max(0.0, min(1.0, f)))).length
legs = {s: [(joint(n + "UpLeg"), joint(n + "Leg")), (joint(n + "Leg"), joint(n + "Foot"))]
        for s, n in (("L", "Left"), ("R", "Right"))}
B_leg = {s: {i for i in BMAIN if min(seg_dist(BV[i], a, b) for a, b in legs[s]) < 0.07 and (BV[i].x > 0) == (s == "L")
             and BV[i].y < 0.11} for s in legs}
arm_segs = []
for n in ("Left", "Right"):
    a, f, h = joint(n + "Arm"), joint(n + "ForeArm"), joint(n + "Hand")
    arm_segs += [(a, f), (f, h), (h, h + (h - f) * 0.9)]
def near_arm(p):
    return min(seg_dist(p, a, b) for a, b in arm_segs) < O["arm_r"]
ARMV = {i for i in BMAIN if near_arm(BV[i])}
B_ring = {i for i in BMAIN if abs(BV[i].x) < O["hand_x"] and i not in ARMV}


def ext(pts, back_lateral=False):
    if not pts:
        return None
    xs = [p.x for p in pts]; ys = [p.y for p in pts]
    ymax = max(ys)
    if back_lateral:   # the seat's back: from the buttocks beside the tail, not the tail root itself
        lat = [p.y for p in pts if abs(p.x) > 0.045]
        if lat:
            ymax = max(lat)
    return np.array([min(xs), max(xs), min(ys), ymax])


def table(V, edges, keep, z0, z1, n, **kw):
    zs = np.linspace(z0, z1, n)
    rows = [ext(section(V, edges, keep, z), **kw) for z in zs]
    good = [i for i, r in enumerate(rows) if r is not None]
    arr = np.array([rows[i] if rows[i] is not None else rows[min(good, key=lambda g: abs(g - i))] for i in range(n)])
    # smooth along z
    sm = arr.copy()
    for i in range(n):
        lo, hi = max(0, i - 2), min(n, i + 3)
        sm[i] = arr[lo:hi].mean(0)
    return zs, sm


def lookup(tab, z):
    zs, arr = tab
    return np.array([np.interp(z, zs, arr[:, k]) for k in range(4)])

# jeans landmarks: crotch = lowest main-island point near the midline
jz = [JV[i].z for i in JMAIN]
j_hem, j_waist = min(jz), max(jz)
j_crotch = min(JV[i].z for i in JMAIN if abs(JV[i].x) < 0.012 and abs(JV[i].y) < 0.12 and JV[i].z > 0.3)
# the waist band's top edge rises at the back label: take the front waist as the reference
j_waist = sorted(JV[i].z for i in JMAIN if JV[i].y < -0.05)[-5]
print("JEANS hem", round(j_hem, 3), "crotch", round(j_crotch, 3), "waist", round(j_waist, 3))
J_leg = {s: {i for i in JMAIN if (JV[i].x > 0) == (s == "L")} for s in ("L", "R")}
JT_leg = {s: table(JV, J_edges, J_leg[s], j_hem + 0.005, j_crotch - 0.01, 40) for s in J_leg}
JT_ring = table(JV, J_edges, JMAIN, j_crotch + 0.01, j_waist, 24)
BT_leg = {s: table(BV, B_edges, B_leg[s], O["hem"] - 0.03, O["crotch"] - 0.005, 40) for s in B_leg}
BT_ring = table(BV, B_edges, B_ring, O["crotch"] + 0.005, O["waist"] + 0.03, 24, back_lateral=True)
BOOT = {s: table(BV, B_edges, {i for b in BOOTS for i in b if (BV[i].x > 0) == (s == "L")}, boot_top - 0.06, boot_top - 0.003, 8)
        for s in ("L", "R")}

print("BODY ring", [[round(float(x), 3) for x in BT_ring[1][k]] + [round(float(BT_ring[0][k]), 3)] for k in range(0, 24, 3)])


def zmap(z):
    if z < j_crotch:
        return O["hem"] + (z - j_hem) / (j_crotch - j_hem) * (O["crotch"] - O["hem"])
    return O["crotch"] + (z - j_crotch) / (j_waist - j_crotch) * (O["waist"] - O["crotch"])


def box(p, je, be, k, a):
    """Map p's x, y from the jeans box je onto the body box be widened by k and a."""
    out = []
    for ax, (i0, i1) in enumerate(((0, 1), (2, 3))):
        jc, jh = (je[i0] + je[i1]) / 2, max(1e-4, (je[i1] - je[i0]) / 2)
        bc, bh = (be[i0] + be[i1]) / 2, (be[i1] - be[i0]) / 2 * k + a
        out.append(bc + (p[ax] - jc) / jh * bh)
    return out


pre_kit = jeans.data.copy()      # the kit jeans as they came (positions = JV), for the knee patch
new = []
for i, p in enumerate(JV):
    zb = zmap(p.z)
    s = "L" if p.x > 0 else "R"
    jl = lookup(JT_leg[s], max(j_hem + O["hem_skip"], min(p.z, j_crotch - 0.011)))
    bl = lookup(BT_leg[s], min(zb, O["crotch"] - 0.006))
    leg_xy = box(p, jl, bl, O["leg_k"], O["leg_a"])
    if zb < boot_top + 0.03:
        # tuck into the boot: no wider than the shaft's inner box, centred in it
        bo = lookup(BOOT[s], min(zb, boot_top - 0.004))
        inner = np.array([bo[0] + O["boot_in"], bo[1] - O["boot_in"], bo[2] + O["boot_in"], bo[3] - O["boot_in"]])
        tuck = box(p, jl, inner, 1.0, 0.0)
        t = max(0.0, min(1.0, (zb - boot_top) / 0.03))
        t = t * t * (3 - 2 * t)
        # keep whichever is narrower near the boot (legs thinner than the shaft stay as they are)
        lw = (bl[1] - bl[0]) / 2 * O["leg_k"] + O["leg_a"]; iw = (inner[1] - inner[0]) / 2
        if iw < lw:
            leg_xy = [tuck[k] * (1 - t) + leg_xy[k] * t for k in range(2)]
    jr = lookup(JT_ring, min(j_waist - O["waist_skip"], max(p.z, j_crotch + 0.011)))
    br = lookup(BT_ring, max(zb, O["crotch"] + 0.006))
    w = max(0.0, min(1.0, (zb - O["crotch"]) / (O["waist"] - O["crotch"])))
    ring_xy = box(p, jr, br, O["hip_k"] + (O["waist_k"] - O["hip_k"]) * w, O["hip_a"] + (O["waist_a"] - O["hip_a"]) * w)
    t = (p.z - j_crotch) / O["blend"]
    t = max(0.0, min(1.0, t)); t = t * t * (3 - 2 * t)
    xy = [leg_xy[k] * (1 - t) + ring_xy[k] * t for k in range(2)]
    new.append(Vector((xy[0], xy[1], zb)))

# stitch islands (the knee patch's X stitches) ride on the main surface: move each by its nearest main point's shift
main_idx = sorted(JMAIN)
from mathutils.kdtree import KDTree
kd = KDTree(len(main_idx))
for k, i in enumerate(main_idx):
    kd.insert(JV[i], k)
kd.balance()
for comp in J_isl[1:]:
    c = sum((JV[i] for i in comp), Vector()) / len(comp)
    _, k, _ = kd.find(c)
    m = main_idx[k]
    # local scale at that point: approximate with the neighbouring main points' mapping
    pts = kd.find_n(c, 12)
    A = np.array([[*JV[main_idx[q]], 1.0] for _, q, _ in pts]); Bm = np.array([new[main_idx[q]][:] for _, q, _ in pts])
    M, *_ = np.linalg.lstsq(A, Bm, rcond=None)
    for i in comp:
        new[i] = Vector(np.array([*JV[i], 1.0]) @ M)

# push out of the body (no hands), smoothed over the jeans' edges
tris = []
for poly in body.data.polygons:
    vs = list(poly.vertices)
    if all(i in BMAIN and abs(BV[i].x) < O["hand_x"] for i in vs):
        for k in range(1, len(vs) - 1):
            tris.append((vs[0], vs[k], vs[k + 1]))
bvh = BVHTree.FromPolygons(BV, tris)
nbrs = [[] for _ in JV]
for a, b in J_edges:
    nbrs[a].append(b); nbrs[b].append(a)
disp = [Vector() for _ in JV]
for it in range(3):
    pushed = 0
    for i in range(len(JV)):
        p = new[i] + disp[i]
        loc, nrm, _, d = bvh.find_nearest(p)
        if loc is None:
            continue
        sd = (p - loc).dot(nrm)
        if sd < O["clear"]:
            disp[i] += nrm * (O["clear"] - sd); pushed += 1
    for _ in range(O["smooth"]):
        nd = []
        for i in range(len(JV)):
            if nbrs[i]:
                avg = sum((disp[j] for j in nbrs[i]), Vector()) / len(nbrs[i])
                nd.append(disp[i] * 0.5 + avg * 0.5 if avg.length > disp[i].length else disp[i])
            else:
                nd.append(disp[i])
        disp = nd
    print("PUSH iter", it, "pushed", pushed)
for i in range(len(JV)):
    jeans.data.vertices[i].co = new[i] + disp[i]
jeans.data.update()

# tucked into the boots: below the boot top, pull each point in along the ray from the shin axis until it is
# boot_in inside the boot's wall
boot_tris, boot_vs = [], set(i for b in BOOTS for i in b)
for poly in body.data.polygons:
    vs = list(poly.vertices)
    if vs[0] in boot_vs:
        for k in range(1, len(vs) - 1):
            boot_tris.append((vs[0], vs[k], vs[k + 1]))
boot_bvh = BVHTree.FromPolygons(BV, boot_tris)
pulled = 0
for i in range(len(JV)):
    p = jeans.data.vertices[i].co
    if p.z > boot_top + 0.004:
        continue
    s_ = "L" if p.x > 0 else "R"
    a, b = joint(("Left" if s_ == "L" else "Right") + "Leg"), joint(("Left" if s_ == "L" else "Right") + "Foot")
    f = (p.z - b.z) / (a.z - b.z)
    axis = b.lerp(a, f)
    d = Vector((p.x - axis.x, p.y - axis.y, 0))
    if d.length < 1e-5:
        continue
    hit, _, _, dist = boot_bvh.ray_cast(Vector((axis.x, axis.y, p.z)), d.normalized(), d.length + 0.01)
    if hit is not None and dist - O["boot_in"] < d.length:
        jeans.data.vertices[i].co = Vector((axis.x, axis.y, p.z)) + d.normalized() * max(0.0, dist - O["boot_in"])
        pulled += 1
jeans.data.update()
print("PUSH boot pulled", pulled)

# tail hole: the tail as in build_rig (surface distance from its tip up to the root ring through two midline points)
bmb = bmesh.new(); bmb.from_mesh(body.data); bmb.verts.ensure_lookup_table()
def geodesic(seed):
    dist = {seed: 0.0}; heap = [(0.0, seed)]
    while heap:
        d, i = heapq.heappop(heap)
        if d > dist[i]:
            continue
        for e in bmb.verts[i].link_edges:
            o = e.other_vert(bmb.verts[i]).index
            nd = d + (BV[i] - BV[o]).length
            if nd < dist.get(o, 9.0):
                dist[o] = nd; heapq.heappush(heap, (nd, o))
    return dist
tip = max((i for i in BMAIN if BV[i].z < 0.3), key=lambda i: BV[i].y)
g = geodesic(tip)
def midline_near(q):
    return min((i for i in BMAIN if abs(BV[i].x) < 0.015), key=lambda i: (BV[i] - q).length)
s_root = 0.5 * (g[midline_near(Vector((0, 0.10, 0.45)))] + g[midline_near(Vector((0, 0.06, 0.35)))])
TAIL = {i for i, d in g.items() if d < s_root + O["tail_grow"]}
# the tail as a tube: ring centroids and radii by surface distance from the tip, extended 3 cm into the body
levels = []
for k in range(0, 40):
    lo_, hi_ = s_root * k / 40, s_root * (k + 1) / 40
    ring = [BV[i] for i, d in g.items() if lo_ <= d < hi_]
    if len(ring) >= 4:
        c = sum(ring, Vector()) / len(ring)
        levels.append((c, sum((p - c).length for p in ring) / len(ring)))
print("BODY tube radii tip->root", [round(r, 3) for _, r in levels])
rc, rr = levels[-1]
dirn = (rc - levels[-4][0]).normalized()
levels += [(rc + dirn * 0.015, rr), (rc + dirn * 0.03, rr)]
TUBE = levels
tail_tris = [t for t in tris if all(i in TAIL for i in t)]
tail_bvh = BVHTree.FromPolygons(BV, tail_tris)
axis_pts = [c for c, _ in levels]
def axis_dir(p):
    k = min(range(len(axis_pts) - 1), key=lambda k: seg_dist(p, axis_pts[k], axis_pts[k + 1]))
    return (axis_pts[k + 1] - axis_pts[k]).normalized()
def in_tail(p, margin=0.0):
    """Inside the tail: rays across the tail's axis hit its skin all round (margin > 0 grows it)."""
    a = axis_dir(p)
    e1 = a.orthogonal().normalized(); e2 = a.cross(e1)
    hits = 0
    for k in range(8):
        t = 2 * math.pi * k / 8
        d = e1 * math.cos(t) + e2 * math.sin(t)
        h = tail_bvh.ray_cast(p, d, 0.15)
        if h[0] is not None:
            hits += 1
    if hits >= 7:
        return True
    if margin > 0:
        loc = tail_bvh.find_nearest(p, margin)[0]
        return loc is not None and hits >= 4
    return False
def in_tube(p, margin):
    for (a, ra), (b, rb) in zip(TUBE, TUBE[1:]):
        _, f = intersect_point_line(p, a, b)
        if 0 <= f <= 1 and (p - a.lerp(b, f)).length < ra + (rb - ra) * f + margin:
            return True
    return False
# the seat around the tail: subdivided so the hole's edge follows the tail closely
def poly_centres(me):
    return [q.center.copy() for q in me.polygons]
first = [i for i, c in enumerate(poly_centres(jeans.data)) if c.y > 0.0 and in_tail(c, O["tube_margin"])]
hc0 = sum((jeans.data.polygons[i].center for i in first), Vector()) / len(first)
bm = bmesh.new(); bm.from_mesh(jeans.data); bm.faces.ensure_lookup_table()
near = [f for f in bm.faces if (f.calc_center_median() - hc0).length < O["sub_r"] and f.calc_center_median().y > -0.02]
bmesh.ops.subdivide_edges(bm, edges=list({e for f in near for e in f.edges}), cuts=O["sub_cuts"], use_grid_fill=True)
bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
bm.to_mesh(jeans.data); bm.free(); jeans.data.update()
cut = {i for i, c in enumerate(poly_centres(jeans.data)) if c.y > 0.0 and in_tail(c, O["tube_margin"])}
hole = [jeans.data.polygons[i].center.copy() for i in cut]
hc = sum(hole, Vector()) / len(hole)

# --- the tail patch, painted into the jeans' texture over the seat: each texel takes the colour of the matching point
# of the knee patch (frame from the knee's X stitches); the stitches themselves are copied to the patch's corners
def frame(n):
    up = (Vector((0, 0, 1)) - n * n.z).normalized()
    return up.cross(n), up, n
stitches = [c for c in J_isl[1:] if 0.35 < sum(JV[i].z for i in c) / len(c) < 0.62 and sum(JV[i].x for i in c) > 0]
st_c = [sum((JV[i] for i in c), Vector()) / len(c) for c in stitches]
# group the stitch pieces into X's
xs_groups = []
for k, c in enumerate(st_c):
    for gk in xs_groups:
        if (st_c[gk[0]] - c).length < 0.065:
            gk.append(k); break
    else:
        xs_groups.append([k])
xc = [sum((st_c[k] for k in gk), Vector()) / len(gk) for gk in xs_groups]
kc = sum(xc, Vector()) / len(xc)
kit_tris, kit_loops = [], []
for q in pre_kit.polygons:
    if all(v in JMAIN for v in q.vertices):
        vs, ls = list(q.vertices), list(q.loop_indices)
        for k in range(1, len(vs) - 1):
            kit_tris.append((vs[0], vs[k], vs[k + 1])); kit_loops.append((ls[0], ls[k], ls[k + 1]))
knee_bvh = BVHTree.FromPolygons(JV, kit_tris)
_, kn, _, _ = knee_bvh.find_nearest(kc)
if kn.y > 0:
    kn = -kn
kr, ku, _ = frame(kn)
hk = (max(abs((c - kc).dot(kr)) for c in xc), max(abs((c - kc).dot(ku)) for c in xc))
kuv = pre_kit.uv_layers.active.data
_src = bpy.data.images.load(JTEX)
TS = _src.size[0]
TEX = np.empty(TS * TS * 4, np.float32); _src.pixels.foreach_get(TEX)
TEX = TEX.reshape(TS, TS, 4)
from mathutils.geometry import barycentric_transform
def tex_at(uv):
    x = min(TS - 1, max(0, int(uv.x * TS))); y = min(TS - 1, max(0, int(uv.y * TS)))
    return TEX[y, x]
NL = O["patch_lut"]
LUT = np.zeros((NL, NL, 4), np.float32)
ext_k = O["patch_ext"]
for i in range(NL):
    for j in range(NL):
        u, v = (i / (NL - 1) * 2 - 1) * hk[0] * ext_k, (j / (NL - 1) * 2 - 1) * hk[1] * ext_k
        o = kc + kr * u + ku * v
        hit, _, ti, _ = knee_bvh.ray_cast(o + kn * 0.04, -kn, 0.08)
        if hit is None:
            hit, _, ti, _ = knee_bvh.find_nearest(o)
        a_, b_, c_ = kit_tris[ti]
        w = barycentric_transform(hit, JV[a_], JV[b_], JV[c_], Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1)))
        la, lb, lc = kit_loops[ti]
        LUT[j, i] = tex_at(kuv[la].uv * w.x + kuv[lb].uv * w.y + kuv[lc].uv * w.z)
_l = bpy.data.images.new("lut", NL, NL); _l.pixels.foreach_set(LUT.ravel()); _l.filepath_raw = os.path.join(OUT, "knee_lut.png")
_l.file_format = "PNG"; _l.save()
print("PATCH X centres (knee u,v)", [(round((c - kc).dot(kr), 3), round((c - kc).dot(ku), 3), len(g)) for c, g in zip(xc, xs_groups)])
# seat frame at the hole
sn = Vector()
for q in jeans.data.polygons:
    if (q.center - hc).length < 0.08:
        sn += q.normal * q.area
sn.normalize()
if sn.y < 0:
    sn = -sn
sr, su, _ = frame(sn)
ang = math.radians(O["patch_turn"])
sr, su = sr * math.cos(ang) + su * math.sin(ang), -sr * math.sin(ang) + su * math.cos(ang)
hx = max(abs((h - hc).dot(sr)) for h in hole); hy = max(abs((h - hc).dot(su)) for h in hole)
half = (O["patch_half"], O["patch_half"])
hc = hc + su * O["patch_up"]
# the seat before the hole: the patch grid is laid on it
pre_tris = []
for q in jeans.data.polygons:
    if (q.center - hc).length < max(half) * 1.8:
        vs = list(q.vertices)
        for k in range(1, len(vs) - 1):
            pre_tris.append((vs[0], vs[k], vs[k + 1]))
pre_bvh = BVHTree.FromPolygons([v.co.copy() for v in jeans.data.vertices], pre_tris)
bm = bmesh.new(); bm.from_mesh(jeans.data); bm.faces.ensure_lookup_table()
bmesh.ops.delete(bm, geom=[bm.faces[i] for i in cut], context="FACES")
bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
bm.to_mesh(jeans.data); bm.free(); jeans.data.update()
print("TAIL cut faces", len(cut))
# stitches: each X keeps its place on the patch (corner), lands on the seat and turns with the local surface
seat_tris = []
for q in jeans.data.polygons:
    if (q.center - hc).length < max(half) * 1.6:
        vs = list(q.vertices)
        for k in range(1, len(vs) - 1):
            seat_tris.append((vs[0], vs[k], vs[k + 1]))
SV = [v.co.copy() for v in jeans.data.vertices]
seat_bvh = BVHTree.FromPolygons(SV, seat_tris)
sbm = bmesh.new(); sbm.from_mesh(pre_kit); sbm.verts.ensure_lookup_table()
keep = {}
corners = [(1, 1), (-1, 1), (-1, -1), (1, -1)]
by_angle = sorted(zip(xs_groups, xc), key=lambda t: math.atan2((t[1] - kc).dot(ku), (t[1] - kc).dot(kr)))
placed = 0
for (gk, c), (cu, cv) in zip(by_angle, sorted(corners, key=lambda t: math.atan2(t[1], t[0]))):
    u = cu * half[0] * O["stitch_in"]; v = cv * half[1] * O["stitch_in"]
    ok = False
    for _step in range(12):
        target = hc + sr * u + su * v
        hit, n, _, _ = seat_bvh.ray_cast(target + sn * 0.06, -sn, 0.12)
        if hit is None:
            hit, n, _, _ = seat_bvh.find_nearest(target)
        if not (in_tail(hit, 0.0) or tail_bvh.ray_cast(hit + sn * 0.004, sn, 0.04)[0] is not None):
            ok = True; break
        v += 0.008                                # the tail covers this corner: move up beside it
    if not ok:
        continue
    if n.dot(sn) < 0:
        n = -n
    lr, lu, _ = frame(n)
    lr, lu = lr * math.cos(ang) + lu * math.sin(ang), -lr * math.sin(ang) + lu * math.cos(ang)
    placed += 1
    for kk in gk:
        for i in stitches[kk]:
            d = JV[i] - c
            keep[i] = hit + (lr * d.dot(kr) + lu * d.dot(ku) + n * d.dot(kn)) * O["stitch_scale"] + n * O["patch_lift"]
print("PATCH stitches placed", placed)
for v in sbm.verts:
    if v.index in keep:
        v.co = keep[v.index]
bmesh.ops.delete(sbm, geom=[v for v in sbm.verts if v.index not in keep], context="VERTS")
sme = bpy.data.meshes.new("stitches"); sbm.to_mesh(sme); sbm.free()
jb = bmesh.new(); jb.from_mesh(jeans.data); jb.from_mesh(sme); jb.to_mesh(jeans.data); jb.free(); jeans.data.update()
# soften the knee weave so the patch reads flat (and hides stretch on the steep underside of the seat)
if O["patch_blur"] > 0:
    r = int(O["patch_blur"] * 3); xk = np.arange(-r, r + 1); kern = np.exp(-xk ** 2 / (2 * O["patch_blur"] ** 2)); kern /= kern.sum()
    pad = np.pad(LUT, ((r, r), (r, r), (0, 0)), mode="edge")
    tmp = sum(pad[:, r + k: r + k + NL] * kern[k + r] for k in xk)
    LUT = sum(tmp[r + k: r + k + NL] * kern[k + r] for k in xk)
# texture: the jeans' atlas squeezed into the top 7/8, the patch image in the strip below (one material)
PS = TS // 8
squeezed = bpy.data.images.load(JTEX); squeezed.scale(TS, TS - PS)
SQ = np.empty(TS * (TS - PS) * 4, np.float32); squeezed.pixels.foreach_get(SQ)
OUT_TEX = np.zeros((TS, TS, 4), np.float32); OUT_TEX[..., 3] = 1
OUT_TEX[PS:] = SQ.reshape(TS - PS, TS, 4)
g = (np.arange(PS) + 0.5) / PS * 2 - 1                  # -1..1 across the patch
pu, pv = np.meshgrid(g, g)
fi = np.clip((pu / 1.0 + 1) / 2 * (NL - 1), 0, NL - 1.001); fj = np.clip((pv + 1) / 2 * (NL - 1), 0, NL - 1.001)
i0, j0 = fi.astype(int), fj.astype(int); ti_, tj_ = (fi - i0)[..., None], (fj - j0)[..., None]
col = (LUT[j0, i0] * (1 - ti_) * (1 - tj_) + LUT[j0, i0 + 1] * ti_ * (1 - tj_) + LUT[j0 + 1, i0] * (1 - ti_) * tj_ +
       LUT[j0 + 1, i0 + 1] * ti_ * tj_)
edge = np.maximum(np.abs(pu), np.abs(pv))
rim = np.clip((edge - O["rim_from"]) / (1 - O["rim_from"]), 0, 1)[..., None]
col[..., :3] *= (1 - O["rim_dark"] * rim)
OUT_TEX[:PS, :PS] = col
for l in jeans.data.uv_layers.active.data:
    l.uv = (l.uv.x, (TS - PS) / TS * l.uv.y + PS / TS)
# the patch: a copy of the (holed) seat around the tail, lifted off it, trimmed square, UVs flat in the patch frame
pbm = bmesh.new(); pbm.from_mesh(jeans.data); pbm.faces.ensure_lookup_table()
def in_box(p, k=1.0):
    d = p - hc
    return abs(d.dot(sr)) < half[0] * k and abs(d.dot(su)) < half[1] * k
keep_f = {f for f in pbm.faces if f.normal.dot(sn) > O["patch_steep"] and any(in_box(v.co, 1.25) for v in f.verts)}
bmesh.ops.delete(pbm, geom=[f for f in pbm.faces if f not in keep_f], context="FACES")
bmesh.ops.delete(pbm, geom=[v for v in pbm.verts if not v.link_faces], context="VERTS")
pbm.normal_update()
for v in pbm.verts:
    n = v.normal if v.normal.dot(sn) > 0 else -v.normal
    v.co += n * O["patch_lift"]
for axis, sign in ((sr, 1), (sr, -1), (su, 1), (su, -1)):
    co = hc + axis * sign * half[0 if axis is sr else 1]
    geom = pbm.verts[:] + pbm.edges[:] + pbm.faces[:]
    bmesh.ops.bisect_plane(pbm, geom=geom, plane_co=co, plane_no=axis * sign, clear_outer=True)
bmesh.ops.delete(pbm, geom=[v for v in pbm.verts if not v.link_faces], context="VERTS")
# keep only the piece(s) joined to the patch's middle band (drop slivers cut off on far sides of the seat)
puv = pbm.loops.layers.uv[jeans.data.uv_layers.active.name]
for f in pbm.faces:
    for lp in f.loops:
        d = lp.vert.co - hc
        u = (d.dot(sr) / half[0] + 1) / 2; v = (d.dot(su) / half[1] + 1) / 2
        lp[puv].uv = (min(1, max(0, u)) * PS / TS, min(1, max(0, v)) * PS / TS)
quads = len(pbm.faces)
pme = bpy.data.meshes.new("patch"); pbm.to_mesh(pme); pbm.free()
jb = bmesh.new(); jb.from_mesh(jeans.data); jb.from_mesh(pme); jb.to_mesh(jeans.data); jb.free(); jeans.data.update()
painted_tex = os.path.join(OUT, "jeans_basecolor.png")
pimg = bpy.data.images.new("jeans_basecolor", TS, TS)
pimg.pixels.foreach_set(OUT_TEX.ravel())
pimg.filepath_raw = painted_tex; pimg.file_format = "PNG"; pimg.save()
JTEX = painted_tex
painted = quads
print("PATCH texels", painted, "half", [round(h, 3) for h in half], "centre", [round(c, 3) for c in hc], "normal",
      [round(c, 3) for c in sn], "knee normal", [round(c, 3) for c in kn], "X groups", len(xs_groups))
jeans.name = jeans.data.name = "jeans"
body.name = "body"
jimg = bpy.data.images.load(JTEX)
jm = bpy.data.materials.new("jeans"); jm.use_nodes = True
tn = jm.node_tree.nodes.new("ShaderNodeTexImage"); tn.image = jimg
bsdf = jm.node_tree.nodes["Principled BSDF"]
jm.node_tree.links.new(tn.outputs["Color"], bsdf.inputs["Base Color"]); bsdf.inputs["Roughness"].default_value = 0.9
jeans.data.materials.clear(); jeans.data.materials.append(jm)
bimg = bpy.data.images.load(BTEX)
bmat = bpy.data.materials.new("body"); bmat.use_nodes = True
tn = bmat.node_tree.nodes.new("ShaderNodeTexImage"); tn.image = bimg
b2 = bmat.node_tree.nodes["Principled BSDF"]
bmat.node_tree.links.new(tn.outputs["Color"], b2.inputs["Base Color"]); b2.inputs["Roughness"].default_value = 0.85
body.data.materials.clear(); body.data.materials.append(bmat)
for o in list(bpy.data.objects):
    if o.type not in ("MESH",) or o not in (body, jeans):
        if o is not arm:
            bpy.data.objects.remove(o)
json.dump({"options": O, "jeans": {"hem": j_hem, "crotch": j_crotch, "waist": j_waist}, "boot_top": boot_top,
           "tail_cut": len(cut)}, open(os.path.join(OUT, "fit.json"), "w"), indent=1)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "fitted.blend"))

# preview only: drop the body skin the jeans hide (ray out along the skin normal meets the jeans), as the build will
jv = [v.co.copy() for v in jeans.data.vertices]
jt = []
for poly in jeans.data.polygons:
    vs = list(poly.vertices)
    for k in range(1, len(vs) - 1):
        jt.append((vs[0], vs[k], vs[k + 1]))
jbvh = BVHTree.FromPolygons(jv, jt)
covered = set()
for i in BMAIN:
    v = body.data.vertices[i]
    if i in TAIL or abs(BV[i].x) >= O["hand_x"] or not (O["hem"] - 0.02 < BV[i].z < O["waist"] - 0.01):
        continue
    if jbvh.ray_cast(BV[i] + v.normal * 0.001, v.normal, 0.06)[0] is not None:
        covered.add(i)
bm = bmesh.new(); bm.from_mesh(body.data); bm.faces.ensure_lookup_table()
hidden = [f for f in bm.faces if all(v.index in covered for v in f.verts)]
bmesh.ops.delete(bm, geom=hidden, context="FACES_ONLY")
bm.to_mesh(body.data); bm.free()
print("COVERED body verts", len(covered), "faces dropped", len(hidden))

scene = bpy.context.scene
world = bpy.data.worlds.new("w"); scene.world = world; world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.72, 0.72, 0.75, 1)
for r, e in (((math.radians(50), 0, math.radians(30)), 2.6), ((math.radians(65), 0, math.radians(200)), 1.4)):
    dl = bpy.data.lights.new("sun", "SUN"); dl.energy = e
    so = bpy.data.objects.new("sun", dl); scene.collection.objects.link(so); so.rotation_euler = r
scene.render.engine = "BLENDER_EEVEE"; scene.view_settings.view_transform = "Standard"
cd = bpy.data.cameras.new("c"); cd.type = "ORTHO"; cam = bpy.data.objects.new("c", cd); scene.collection.objects.link(cam); scene.camera = cam
for name, yaw, pitch, c, size in (("front", 0, 3, (0, 0.05, 0.48), 1.0), ("side", 90, 3, (0, 0.1, 0.48), 1.0),
                                  ("back", 180, 8, (0, 0.05, 0.48), 1.0), ("q34", 35, 10, (0, 0.05, 0.48), 1.0),
                                  ("back_close", 160, 15, (0, 0.12, 0.38), 0.45), ("side_close", 270, 5, (0.0, 0.05, 0.3), 0.6)):
    yw, pt = math.radians(yaw), math.radians(pitch)
    d = Vector((math.sin(yw) * math.cos(pt), -math.cos(yw) * math.cos(pt), math.sin(pt)))
    cam.location = Vector(c) + d * 6; cam.rotation_euler = (Vector(c) - cam.location).to_track_quat("-Z", "Y").to_euler()
    cd.ortho_scale = size; cd.clip_end = 20
    scene.render.resolution_x, scene.render.resolution_y = 600, 600
    scene.render.filepath = os.path.join(OUT, name + ".png")
    bpy.ops.render.render(write_still=True)
print("DONE", OUT)
