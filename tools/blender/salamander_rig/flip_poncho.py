"""Build Dusty's gunslinger-flip poncho: a wide blanket draped in his idle with its right side thrown back over his right shoulder.

systemd-run --user --scope -q -p MemoryMax=3G -p MemorySwapMax=1G blender -b --factory-startup --python-exit-code 1 \
  --python tools/blender/salamander_rig/flip_poncho.py -- <live salamander.fbx> <idle clip .usda> <out_dir> ['<json options>']

The pattern is one quad grid: columns run across his shoulders (his right to his left), rows from the front hem to the
back hem, an oval neck hole on the shoulder line (off centre toward his left, so the left side ends at his elbow), soft
points at the front and back hem centres and rounded corners. A slit runs up his right front, from the front hem at his
right chest to the shoulder line, and the block right of it (the fabric in front of his right arm) is trimmed shorter.
Its planar `Blanket` UV is the unfolded pattern, which is also the cloth's rest shape (shape key `Flat`).
1. Drop: the flat blanket starts as a shallow cone around his neck, above his shoulders, and falls onto his body posed at
   the idle frame (arms down, head and props not colliding), with no pins.
2. Flip: the block right of the slit folds back over his shoulder like paper. Each of its vertices moves to where its
   mirror image across the shoulder line lies on the drape, lifted off it, so the flap lies over his right shoulder and
   down his upper back on top of the fabric it covers, and his right arm comes free. A cloth settle with every vertex held
   softly to its folded place then rounds the crease and resolves contact; the collar stays pinned.
   The slit is what makes the flip possible. Without it, thrown corners or edges (hooks, pin goals carried over the
   shoulder) bunched behind his arm or lifted the front panel, and paper folds laid a lapel across his chest and left his
   arm covered: fabric that stays flat cannot keep a straight front hem and also reach his back. The trim lands the
   flap's striped hem on his upper back, clear of the back hem's stripes.
The result goes back to his T-pose rest through `Spine2`, the bone it is skinned to here (a rigid preview skin; spring
chains are a later pass), so posing the rig in the idle frame reproduces the drape exactly.
Writes poncho_flip.blend (the FBX's rig, body, jeans and hat, the new `Salamander_Poncho`, the live one hidden as
`Salamander_Poncho_Tabard`, posed at the idle frame), textures/Salamander_Poncho_Flip.png and report.json.
"""
import bpy, bmesh, json, math, os, sys
import numpy as np
from mathutils import Vector, geometry
from mathutils.bvhtree import BVHTree

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import clip_pose as cp

argv = sys.argv[sys.argv.index("--") + 1:]
FBX, IDLE, OUT = (os.path.abspath(a) for a in argv[:3])
O = {"frame": 0, "width": 0.68, "front": 0.255, "back": 0.29, "point": 0.04, "corner": 0.10, "cell": 0.02,
     # Neck hole half-axes and its centre's offset toward his left.
     "hole": [0.085, 0.075], "hole_x": 0.02, "cone": [0.14, 35], "drop_gap": 0.02, "drop_frames": 80,
     # The slit's pattern x from the hole centre, the fold row's s (both snap to the grid) and how much shorter the flap
     # block is. The flap lifts `fold_gap` off the fabric it covers; the settle holds the flap and the rest this softly.
     "slit": -0.13, "fold_s": 0.0, "flap_trim": 0.05, "fold_gap": 0.012, "settle_frames": 60, "hold": [0.2, 0.5],
     "bending": 8.0, "mass": 0.3, "quality": 10, "clearance": 0.005, "friction": 2.0,
     "stripes": [[0.026, 0.044], [0.060, 0.078]], "texture": 1024}
if len(argv) > 3:
    O.update(json.loads(argv[3]))
os.makedirs(os.path.join(OUT, "textures"), exist_ok=True)
TEX = os.path.join(os.path.dirname(FBX), "..", "Textures")
P = cp.P

fa = cp.import_fbx(FBX)
scene = bpy.context.scene
scene.render.fps = 24
body = bpy.data.objects["Salamander_Body"]
old = bpy.data.objects["Salamander_Poncho"]
old.name, old.data.name = "Salamander_Poncho_Tabard", "Salamander_Poncho_Tabard"
old.hide_render = old.hide_viewport = True
POSE = cp.pose_clip(fa, IDLE, O["frame"])
Af = fa.matrix_world


def world_mesh(o):
    ev = o.evaluated_get(bpy.context.evaluated_depsgraph_get())
    me = ev.to_mesh()
    co = np.array([v.co for v in me.vertices]) @ np.array(o.matrix_world)[:3, :3].T + np.array(o.matrix_world)[:3, 3]
    polys = [tuple(p.vertices) for p in me.polygons]
    ev.to_mesh_clear()
    return co, polys


def bone_weight(o, names):
    gi = {g.index for g in o.vertex_groups if g.name in names}
    return np.array([sum(g.weight for g in v.groups if g.group in gi) for v in o.data.vertices])


def joint(name):
    return Af @ POSE[P + name].translation


BODY, BODY_POLYS = world_mesh(body)
HEAD = bone_weight(body, {P + "Head", P + "HeadTop_End", "Mustache", "Straw"}) > 0.5
keep = np.where(~HEAD)[0]


def make_collider(drop):
    """His posed body; for the drop without the head and its props, so the blanket can start around the neck."""
    k = keep if drop else np.arange(len(BODY))
    remap = -np.ones(len(BODY), int); remap[k] = np.arange(len(k))
    cme = bpy.data.meshes.new("collider")
    cme.from_pydata(BODY[k].tolist(), [], [tuple(remap[list(p)]) for p in BODY_POLYS if (remap[list(p)] >= 0).all()])
    ob = bpy.data.objects.new("collider", cme); scene.collection.objects.link(ob)
    ob.modifiers.new("collision", "COLLISION")
    ob.collision.thickness_outer = O["clearance"]
    ob.collision.thickness_inner = 0.02
    ob.collision.cloth_friction = O["friction"]
    return ob


collider = make_collider(drop=True)

# Pattern: column i across his shoulders (his right, -x, first), row j from the front hem (-s) to the back hem.
W, cell = O["width"], O["cell"]
nx = 2 * round(W / cell / 2)
XS = np.linspace(-W / 2, W / 2, nx + 1)
ic = nx // 2 + round(O["hole_x"] / cell)
HX = XS[ic]


def length(x, L0):
    """Flat distance from the shoulder line to the hem: a soft point below the neck, rounded corners at the sides."""
    L = L0 + O["point"] * math.cos(math.pi * (x - HX) / W) ** 2
    rc, e = O["corner"], abs(x) - (W / 2 - O["corner"])
    return L - (rc - math.sqrt(max(rc * rc - e * e, 0.0))) if e > 0 else L


SF, SB = length(HX, O["front"]), length(HX, O["back"])
jf, jb = round(SF / cell), round(SB / cell)
hx, hs = O["hole"]
ny = jf + jb
kx, ksf, ksb = round(hx / cell), round(hs / (SF / jf)), round(hs / (SB / jb))


def hole_r(i, j):
    return max(abs(i - ic) / kx, (jf - j) / ksf if j <= jf else (j - jf) / ksb)


ids, flat = {}, []
for j in range(ny + 1):
    for i in range(nx + 1):
        r = hole_r(i, j)
        if r < 1 - 1e-9:
            continue
        x = XS[i]
        s = -length(x, O["front"]) * (1 - j / jf) if j <= jf else length(x, O["back"]) * (j - jf) / jb
        # Round the hole's grid rectangle into an oval; only its near neighbours move.
        radial = math.hypot((x - HX) / hx, s / hs)
        f = 1 + max(0.0, min(1.0, (1.6 - r) / 0.6)) * (r / radial - 1)
        ids[i, j] = len(flat)
        flat.append((HX + (x - HX) * f, s * f))
# The slit: column i_slit below the fold row gets a second copy of its vertices for the quads on his right of it,
# and the block right of the slit is rescaled to its trimmed hem.
i_slit = ic + round(O["slit"] / cell)
j_top = round(jf * (1 + O["fold_s"] / length(XS[i_slit], O["front"])))
SLIT = {}
for j in range(j_top):
    SLIT[j] = len(flat)
    flat.append(flat[ids[i_slit, j]])
for (i, j), v in list(ids.items()) + [((i_slit, j), v) for j, v in SLIT.items()]:
    if j < j_top and (i < i_slit or v in SLIT.values()):
        x, top = XS[i], flat[ids[i, j_top]][1]
        hem = -length(x, O["front"]) + O["flap_trim"]
        flat[v] = (x, hem + (top - hem) * j / j_top)
FLAT = np.array(flat)
faces = []
for j in range(ny):
    for i in range(nx):
        if hole_r(i + 0.5, j + 0.5) < 1:
            continue
        k = [(i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1)]
        if all(q in ids for q in k):
            faces.append(tuple(SLIT[q[1]] if q[0] == i_slit and i < i_slit and q[1] in SLIT else ids[q] for q in k))
GRID = {v: k for k, v in ids.items()} | {v: (i_slit, j) for j, v in SLIT.items()}
RING = [v for v, (i, j) in GRID.items() if abs(hole_r(i, j) - 1) < 1e-9]

# Start: a shallow cone around the neck, flat over the shoulders, above every collider point outside the hole.
neck = (joint("Neck") + joint("Spine2")) / 2
cx, cy = neck.x, neck.y
cxy = BODY[keep][:, :2] - (cx, cy)
outside = (cxy[:, 0] / hx) ** 2 + (cxy[:, 1] / hs) ** 2 > 1.0
under = outside & (np.abs(cxy[:, 0]) < W / 2 + abs(HX)) & (np.abs(cxy[:, 1]) < max(SF, SB))
drop_z = float(BODY[keep][under, 2].max()) + O["drop_gap"]
r0, alpha = O["cone"][0], math.radians(O["cone"][1])
start = []
for x, s in FLAT:
    x -= HX
    d = math.hypot(x, s)
    out = max(0.0, d - r0)
    dh = (d - out) + out * math.cos(alpha)
    start.append((cx + x / d * dh, cy + s / d * dh, drop_z - out * math.sin(alpha)))
me = bpy.data.meshes.new("Salamander_Poncho")
me.from_pydata(start, [], faces); me.update()
assert len(me.polygons) == len(faces) and all(len(p.vertices) == 4 for p in me.polygons)
bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
assert all(len(e.link_faces) <= 2 for e in bm.edges), "non-manifold edge"
assert all(v.link_faces for v in bm.verts), "loose vertex"
seen, todo = set(), [bm.faces[0]]
while todo:
    f = todo.pop()
    if f.index not in seen:
        seen.add(f.index)
        todo += [g for e in f.edges for g in e.link_faces]
assert len(seen) == len(bm.faces), "the blanket is not one piece"
border = {e for e in bm.edges if e.is_boundary}
loops = 0
while border:
    loops += 1
    todo = [border.pop()]
    while todo:
        e = todo.pop()
        for v in e.verts:
            for f in [f for f in v.link_edges if f in border]:
                border.discard(f); todo.append(f)
assert loops == 2, f"expected the outer hem (slit included) and the neck hole, got {loops} boundary loops"
bm.free()
po = bpy.data.objects.new("Salamander_Poncho", me); scene.collection.objects.link(po)
uv = me.uv_layers.new(name="Blanket")
UV = np.c_[(FLAT[:, 0] + W / 2) / W, (FLAT[:, 1] + SF) / (SF + SB)]
for p in me.polygons:
    p.use_smooth = True
    for li in p.loop_indices:
        uv.data[li].uv = UV[me.loops[li].vertex_index]
po.shape_key_add(name="Basis")
key = po.shape_key_add(name="Flat"); key.value = 0
for v, (x, s) in enumerate(FLAT):
    key.data[v].co = (x, s, 0)


def set_coords(co):
    for v, c in enumerate(co):
        me.vertices[v].co = c
        me.shape_keys.key_blocks["Basis"].data[v].co = c


def simulate(frames, pin=None, self_collision=True):
    cl = po.modifiers.new("cloth", "CLOTH")
    s = cl.settings
    s.quality, s.mass, s.air_damping = O["quality"], O["mass"], 2.0
    s.tension_stiffness = s.compression_stiffness = 40
    s.shear_stiffness, s.bending_stiffness = 8, O["bending"]
    s.rest_shape_key = me.shape_keys.key_blocks["Flat"]
    if pin:
        s.vertex_group_mass, s.pin_stiffness = pin, 2.0
    c = cl.collision_settings
    c.use_collision, c.distance_min, c.collision_quality = True, O["clearance"], 4
    c.use_self_collision, c.self_distance_min, c.self_friction = self_collision, 0.004, 3.0
    cl.point_cache.frame_start, cl.point_cache.frame_end = 1, frames
    scene.frame_start, scene.frame_end = 1, frames
    for f in range(1, frames + 1):
        scene.frame_set(f)
        if f % 20 == 0:
            print("PONCHO frame", f, flush=True)
    ev = po.evaluated_get(bpy.context.evaluated_depsgraph_get())
    m = ev.to_mesh()
    co = np.array([v.co for v in m.vertices])
    ev.to_mesh_clear()
    assert np.isfinite(co).all(), "cloth exploded"
    po.modifiers.clear()
    scene.frame_set(1)
    set_coords(co)
    return co


DROPPED = simulate(O["drop_frames"], self_collision=False)  # the slit's two edges start on top of each other
bpy.data.objects.remove(collider)
collider = make_collider(drop=False)
# Flip: fold the flap block over the shoulder line onto the drape behind it, then settle.
tris = [(f[0], f[1], f[2]) for f in faces] + [(f[0], f[2], f[3]) for f in faces]
F3, D3 = [Vector((x, s, 0)) for x, s in FLAT], [Vector(c) for c in DROPPED]
bvh_flat = BVHTree.FromPolygons([tuple(v) for v in F3], tris)
bvh_body = BVHTree.FromPolygons(BODY.tolist(), BODY_POLYS)
flap = np.array([j < j_top and (i < i_slit or v in SLIT.values()) for v, (i, j) in sorted(GRID.items())])
fold_s = np.array([FLAT[ids[min(i, i_slit), j_top], 1] for v, (i, j) in sorted(GRID.items())])
from_fold = np.where(np.array([i <= i_slit for v, (i, j) in sorted(GRID.items())]), np.abs(FLAT[:, 1] - fold_s), 1.0)


def drape_at(q):
    """The dropped drape at pattern point q (affine past the pattern's edge) and its outward normal there."""
    q3 = Vector((q[0], q[1], 0))
    i0, i1, i2 = tris[bvh_flat.find_nearest(q3)[2]]
    p = geometry.barycentric_transform(q3, F3[i0], F3[i1], F3[i2], D3[i0], D3[i1], D3[i2])
    n = (D3[i1] - D3[i0]).cross(D3[i2] - D3[i0]).normalized()
    return p, (n if n.dot(p - bvh_body.find_nearest(p)[0]) > 0 else -n)


folded = DROPPED.copy()
for v in np.where(flap)[0]:
    p, n = drape_at((FLAT[v, 0], 2 * fold_s[v] - FLAT[v, 1]))
    folded[v] = p + n * O["fold_gap"] * min(1.0, from_fold[v] / 0.03)
set_coords(folded)
pin = po.vertex_groups.new(name="pin")
ring = FLAT[RING]
for v in range(len(FLAT)):
    d = np.min(np.linalg.norm(ring - FLAT[v], axis=1))
    w = max(O["hold"][0 if flap[v] else 1] * min(1.0, from_fold[v] / 0.04),
            1.0 if d < 0.005 else 0.6 * (1 - d / 0.03) if d < 0.03 else 0.0)
    if w > 0:
        pin.add([v], w, "REPLACE")
FINAL = simulate(O["settle_frames"], pin="pin")
bpy.data.objects.remove(collider)

# Checks in the idle pose: clearance from the whole body, hem heights, how much of each arm the front view sees.
signed = []
for c in FINAL:
    loc, n, _, d = bvh_body.find_nearest(Vector(c))
    signed.append(d if (Vector(c) - loc).dot(n) >= 0 else -d)
signed = np.array(signed)
bvh_poncho = BVHTree.FromPolygons(FINAL.tolist(), faces)


def seen_from_front(bones):
    """Share of that skin the front view (looking along +y) sees past the poncho; arm skin facing away still counts."""
    sel = np.where(bone_weight(body, bones) > 0.5)[0]
    hit = [bvh_poncho.ray_cast(Vector(BODY[v]) + Vector((0, -0.003, 0)), Vector((0, -1, 0)))[0] is not None for v in sel]
    return round(1 - float(np.mean(hit)), 3)


arm = lambda side, *b: {P + side + n for n in b}
col_z = lambda i: {j: float(FINAL[ids[i, j], 2]) for j in (0, ny) if (i, j) in ids}
report = {"fbx": FBX, "idle": IDLE, "options": O, "vertices": len(FLAT), "quads": len(faces),
          "grid": [nx, ny], "pattern_m": {"width": W, "front_max": SF, "back_max": SB}, "drop_z": round(drop_z, 4),
          "clearance_m": {"min_signed": round(float(signed.min()), 4), "inside": int((signed < 0).sum()),
                          "under_3mm": int((signed < 0.003).sum())},
          "hem_z": {"front_centre": round(col_z(ic)[0], 3), "back_centre": round(col_z(ic)[ny], 3),
                    "left_side_mid": round(float(FINAL[ids[nx, jf], 2]), 3), "belt_ref_hips": round(joint("Hips").z, 3),
                    "left_elbow": round(joint("LeftForeArm").z, 3)},
          "seen_from_front": {"right_upper_arm": seen_from_front(arm("Right", "Arm")),
                              "right_forearm": seen_from_front(arm("Right", "ForeArm")),
                              "left_upper_arm": seen_from_front(arm("Left", "Arm")),
                              "left_forearm": seen_from_front(arm("Left", "ForeArm"))},
          "flap": {"vertices": int(flap.sum()), "slit_x": round(float(XS[i_slit] - HX), 3), "fold_row": j_top,
                   "corner_final": FINAL[ids[0, 0]].round(3).tolist()},
          "jaw_gap_m": round(float(min(bvh_poncho.find_nearest(Vector(BODY[v]))[3] for v in np.where(HEAD)[0])), 4)}
print("PONCHO", json.dumps(report))

# Back to his T-pose rest through Spine2, skinned rigidly to it for preview.
S_pose = Af @ fa.pose.bones[P + "Spine2"].matrix
S_rest = Af @ fa.data.bones[P + "Spine2"].matrix_local
to_rest = np.array(S_rest @ S_pose.inverted())
po.shape_key_clear()
for v, c in enumerate(FINAL @ to_rest[:3, :3].T + to_rest[:3, 3]):
    me.vertices[v].co = c
for g in list(po.vertex_groups):
    po.vertex_groups.remove(g)
po.vertex_groups.new(name=P + "Spine2").add(list(range(len(FLAT))), 1.0, "REPLACE")
po.parent = fa
po.matrix_parent_inverse = Af.inverted()
po.modifiers.new("Armature", "ARMATURE").object = fa
bpy.context.view_layer.update()
posed, _ = world_mesh(po)
report["rest_roundtrip_m"] = float(np.abs(posed - FINAL).max())
assert report["rest_roundtrip_m"] < 1e-4, report["rest_roundtrip_m"]

# Wool texture: two dark brown stripes at fixed distances from each region's own hem. The flap (right of the slit, in
# front of the fold) follows its trimmed front hem and the right side edge, the rest the front panel's hem, the back hem
# and the side edges. The slit is a plain edge.
n = O["texture"]
yy, xx = np.mgrid[0:n, 0:n]
pix = np.c_[((xx.ravel() + 0.5) / n) * W - W / 2, ((yy.ravel() + 0.5) / n) * (SF + SB) - SF].astype(np.float32)


def hem_distance(points):
    A = np.array(points[:-1], np.float32); AB = np.array(points[1:], np.float32) - A
    out = np.empty(len(pix), np.float32)
    for k in range(0, len(pix), 4096):
        q = pix[k:k + 4096, None, :] - A[None]
        t = np.clip((q * AB).sum(2) / (AB * AB).sum(1), 0, 1)
        out[k:k + 4096] = np.linalg.norm(q - t[:, :, None] * AB, axis=2).min(1)
    return out


x_slit, top = XS[i_slit], FLAT[ids[i_slit, j_top], 1]
flap_hem = [(x, -length(x, O["front"]) + O["flap_trim"]) for x in np.linspace(x_slit, -W / 2, 81)]
rest = [(x, -length(x, O["front"])) for x in np.linspace(x_slit, W / 2, 161)]
rest += [(x, length(x, O["back"])) for x in np.linspace(W / 2, -W / 2, 241)] + [(-W / 2, top)]
in_flap = (pix[:, 0] < x_slit) & (pix[:, 1] < top)
dist = np.where(in_flap, hem_distance(flap_hem + [(-W / 2, top)]), hem_distance(rest))
dist = dist.reshape(n, n)
rgb = np.empty((n, n, 3), np.float32); rgb[:] = np.array([217, 85, 42]) / 255
for a, b in O["stripes"]:
    rgb[(dist > a) & (dist < b)] = np.array([67, 43, 31]) / 255
rgb[dist < 0.008] *= 0.68
rng = np.random.default_rng(6)
weave = (0.007 * np.sin(xx * math.pi) + 0.010 * np.cos(yy * math.pi / 2) + rng.normal(0, 0.009, (n, n))
         + rng.normal(0, 0.006, (n, 1)) + rng.normal(0, 0.004, (1, n)))
rgba = np.ones((n, n, 4), np.float32); rgba[:, :, :3] = np.clip(rgb * (1 + weave[:, :, None]), 0, 1)
img = bpy.data.images.new("Salamander_Poncho_Flip", width=n, height=n, alpha=False)
img.pixels.foreach_set(rgba.ravel())
img.filepath_raw = os.path.join(OUT, "textures", "Salamander_Poncho_Flip.png")
img.file_format = "PNG"; img.save()


def textured(mat, image):
    """One image into Base Color, no normal map: the FBX's texture paths point at missing files."""
    mat.use_nodes = True
    nt = mat.node_tree
    for nd in [nd for nd in nt.nodes if nd.type in {"TEX_IMAGE", "NORMAL_MAP"}]:
        nt.nodes.remove(nd)
    bs = next(nd for nd in nt.nodes if nd.type == "BSDF_PRINCIPLED")
    bs.inputs["Roughness"].default_value = 0.9
    bs.inputs["Specular IOR Level"].default_value = 0.2
    tn = nt.nodes.new("ShaderNodeTexImage"); tn.image = image
    nt.links.new(tn.outputs["Color"], bs.inputs["Base Color"])


for m in bpy.data.materials:
    png = os.path.abspath(os.path.join(TEX, m.name + ".png"))
    if os.path.exists(png):
        textured(m, bpy.data.images.load(png, check_existing=True))
mat = bpy.data.materials.new("Salamander_Poncho_Flip")
mat.use_backface_culling = False
textured(mat, img)
me.materials.append(mat)
json.dump(report, open(os.path.join(OUT, "report.json"), "w"), indent=1)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "poncho_flip.blend"))
print("PONCHO saved", os.path.join(OUT, "poncho_flip.blend"))
