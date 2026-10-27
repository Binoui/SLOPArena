"""Build a rectangular, quad-grid blanket poncho and cloth-drape it on the dressed A-pose.

blender -b --factory-startup --python-exit-code 1 --python tools/blender/salamander_rig/build_poncho.py -- \
    <dressed.blend> <out_dir> ['<json options>']

Writes poncho.blend (object `poncho`, in the dressed model's metre/Z-up frame),
textures/Salamander_Poncho.png (1024 square), review/*.png and poncho.json.
The single-sided cloth has one planar UV island, an oval neck opening and no modifiers after baking.
Options: width, length, cell (metres), frames, bending, clearance, quality and renders (boolean).
The initial smooth folds put the rectangle over the shoulders before gravity settles it.
"""
import bpy, bmesh, json, math, os, sys
import numpy as np
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT = (os.path.abspath(a) for a in args[:2])
O = {"width": 0.44, "length": 0.84, "cell": 0.02, "frames": 60,
     "bending": 3.0, "clearance": 0.004, "quality": 12, "renders": True}
if len(args) > 2:
    O.update(json.loads(args[2]))
os.makedirs(os.path.join(OUT, "textures"), exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=SRC)
scene = bpy.context.scene
scene.render.threads_mode = "FIXED"; scene.render.threads = 4
scene.render.image_settings.file_format = "PNG"
scene.render.use_file_extension = True
scene.render.film_transparent = False
scene.render.resolution_percentage = 100
body = bpy.data.objects["body"]
bpy.data.objects.remove(bpy.data.objects["poncho"], do_unlink=True)

# Integer multiples of the cell size keep the hole and outer hem on the same even grid.
nx, ny = (2 * round(O[k] / O["cell"] / 2) for k in ("width", "length"))
hx, hy = nx * O["cell"] / 2, ny * O["cell"] / 2
neck_x, neck_s = 0.06, 0.08
verts, uvcoords, faces, ids, collar = [], [], [], {}, []
for j in range(ny + 1):
    s = -hy + j * O["cell"]
    for i in range(nx + 1):
        x = -hx + i * O["cell"]
        if abs(x) < neck_x - 1e-6 and abs(s) < neck_s - 1e-6:
            continue
        # Round the rectangular hole into an oval; only its immediate neighbours move.
        r = max(abs(x) / neck_x, abs(s) / neck_s)
        blend = max(0.0, min(1.0, (1.6 - r) / 0.6))
        radial = math.sqrt((x / neck_x)**2 + (s / neck_s)**2)
        f = 1 + blend * (r / radial - 1) if radial else 1
        xx, ss = x * f, s * f
        radius = 0.125
        theta = min(abs(ss) / radius, math.pi / 2)
        y = math.copysign(radius * math.sin(theta), ss) + 0.009
        z = 0.775 - radius * (1 - math.cos(theta)) - max(0, abs(ss) - radius * math.pi / 2)
        # Fold the outer shoulder strips down too. Starting as a horizontal shelf makes the
        # second simulation choose between stiff shoulder pads and a crumpled front panel.
        excess = max(0, abs(xx) - 0.10)
        theta_x = min(excess / 0.07, math.pi / 2)
        shoulder = math.exp(-(abs(ss) / 0.13)**4)
        z -= shoulder * (0.07 * (1 - math.cos(theta_x)) + max(0, excess - 0.07 * math.pi / 2))
        folded_x = math.copysign(min(abs(xx), 0.10) + 0.07 * math.sin(theta_x), xx)
        xx += shoulder * (folded_x - xx)
        # Deterministic, barely visible symmetry breaking lets gravity form broad folds.
        y += 0.0015 * math.sin(xx * 34) * min(1, max(0, (r - 1) / 2))
        ids[i, j] = len(verts)
        if abs(r - 1) < 1e-5:
            collar.append(len(verts))
        verts.append((xx, y, z))
        uvcoords.append((i / nx, j / ny))
for j in range(ny):
    for i in range(nx):
        keys = [(i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1)]
        # Four hole corners exist; do not accidentally cap a hole cell.
        x, s = -hx + (i + 0.5) * O["cell"], -hy + (j + 0.5) * O["cell"]
        if abs(x) < neck_x and abs(s) < neck_s:
            continue
        if all(k in ids for k in keys):
            faces.append(tuple(ids[k] for k in keys))
me = bpy.data.meshes.new("poncho")
me.from_pydata(verts, [], faces); me.update()
assert min(p.area for p in me.polygons) > 1e-6, "Initial folds collapsed a cloth cell"
po = bpy.data.objects.new("poncho", me); scene.collection.objects.link(po)
uv = me.uv_layers.new(name="Blanket")
for p in me.polygons:
    p.use_smooth = True
    for li in p.loop_indices:
        uv.data[li].uv = uvcoords[me.loops[li].vertex_index]

# Pixel-space design follows distance to either short end of the unfolded rectangle.
# It therefore produces identical horizontal hem bands on the front and back after folding.
n = 1024
rng = np.random.default_rng(6)
yy, xx = np.mgrid[0:n, 0:n]
hem = np.minimum(yy + 0.5, n - yy - 0.5) / n
rgb = np.empty((n, n, 3), dtype=np.float32); rgb[:] = np.array([217, 85, 42]) / 255
stripe = ((hem > 0.037) & (hem < 0.062)) | ((hem > 0.087) & (hem < 0.112))
rgb[stripe] = np.array([67, 43, 31]) / 255
border = (hem < 0.009) | (xx < 5) | (xx > n - 6)
rgb[border] *= 0.68
weave = (0.007 * np.sin(xx * math.pi) + 0.010 * np.cos(yy * math.pi / 2)
         + rng.normal(0, 0.009, (n, n)) + rng.normal(0, 0.006, (n, 1))
         + rng.normal(0, 0.004, (1, n)))
rgb = np.clip(rgb * (1 + weave[:, :, None]), 0, 1)
rgba = np.ones((n, n, 4), dtype=np.float32); rgba[:, :, :3] = rgb
img = bpy.data.images.new("Salamander_Poncho", width=n, height=n, alpha=False)
img.pixels.foreach_set(rgba.ravel())
img.filepath_raw = os.path.join(OUT, "textures", "Salamander_Poncho.png")
img.file_format = "PNG"; img.save()
mat = bpy.data.materials.new("Salamander_Poncho"); mat.use_nodes = True
mat.use_backface_culling = False
bs = mat.node_tree.nodes.get("Principled BSDF")
bs.inputs["Roughness"].default_value = 0.92
bs.inputs["Specular IOR Level"].default_value = 0.18
tn = mat.node_tree.nodes.new("ShaderNodeTexImage"); tn.image = img
mat.node_tree.links.new(tn.outputs["Color"], bs.inputs["Base Color"])
me.materials.append(mat)

pin = po.vertex_groups.new(name="pin")
ring = [Vector(verts[i]) for i in collar]
for v in me.vertices:
    d = min((v.co - p).length for p in ring)
    if d < 0.027:
        pin.add([v.index], 1.0 if d < 0.006 else 0.5 * (1 - (d - 0.006) / 0.021), "REPLACE")
col = body.modifiers.new("Poncho collider", "COLLISION")
body.collision.thickness_outer = O["clearance"]
body.collision.thickness_inner = 0.01
body.collision.cloth_friction = 2.0
cl = po.modifiers.new("Blanket drape", "CLOTH")
s = cl.settings
s.quality = O["quality"]; s.mass = 0.20
s.tension_stiffness = s.compression_stiffness = 60
s.shear_stiffness = 20; s.bending_stiffness = O["bending"]
s.air_damping = 3; s.vertex_group_mass = "pin"; s.pin_stiffness = 1
cs = cl.collision_settings
cs.use_collision = True; cs.distance_min = O["clearance"]; cs.collision_quality = 6
cs.use_self_collision = True; cs.self_distance_min = 0.003; cs.self_friction = 2
cl.point_cache.frame_start = 1; cl.point_cache.frame_end = O["frames"]
scene.frame_start, scene.frame_end = 1, O["frames"]
for f in range(1, O["frames"] + 1):
    scene.frame_set(f)
    if f % 15 == 0:
        print("PONCHO frame", f, flush=True)
ev = po.evaluated_get(bpy.context.evaluated_depsgraph_get())
baked = bpy.data.meshes.new_from_object(ev)
po.modifiers.clear(); po.data = baked; baked.name = "poncho"
body.modifiers.remove(col)

# Keep just the authoritative single surface; no render-only subdivision in the delivered mesh.
bm = bmesh.new(); bm.from_mesh(baked)
assert all(len(f.verts) == 4 for f in bm.faces), "Expected an all-quad blanket"
assert not [f for f in bm.faces if f.calc_area() < 1e-10], "Degenerate cloth faces"
assert not [v for v in bm.verts if not v.link_faces], "Loose cloth vertices"
assert not [e for e in bm.edges if len(e.link_faces) > 2], "Non-manifold cloth"
edges = [e.calc_length() for e in bm.edges]
P = np.array([v.co[:] for v in baked.vertices]); assert np.isfinite(P).all()
report = {"source": SRC, "options": O, "vertices": len(baked.vertices), "quads": len(baked.polygons),
          "collarVertices": len(collar), "boundaryEdges": sum(e.is_boundary for e in bm.edges),
          "edgeMetresMinMedianMax": [min(edges), float(np.median(edges)), max(edges)],
          "bounds": [P.min(0).tolist(), P.max(0).tolist()]}
bm.free()
json.dump(report, open(os.path.join(OUT, "poncho.json"), "w"), indent=2)
# Save before adding the review-only lighting and cameras. The body remains as a placement reference.
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "poncho.blend"))
if O["renders"]:
    for o in list(scene.objects):
        if o.type in {"CAMERA", "LIGHT"}:
            bpy.data.objects.remove(o, do_unlink=True)
    scene.render.engine = "BLENDER_EEVEE"
    scene.view_settings.view_transform = "Standard"
    scene.world = bpy.data.worlds.new("Poncho review")
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs[0].default_value = (0.65, 0.65, 0.68, 1)
    for deg, energy in [(30, 2.4), (200, 1.2)]:
        light = bpy.data.lights.new("Review sun", "SUN"); light.energy = energy
        ob = bpy.data.objects.new(light.name, light); scene.collection.objects.link(ob)
        ob.rotation_euler = (math.radians(45), 0, math.radians(deg))
    cd = bpy.data.cameras.new("Review camera"); cd.type = "ORTHO"; cd.ortho_scale = 1.18
    cam = bpy.data.objects.new(cd.name, cd); scene.collection.objects.link(cam); scene.camera = cam
    scene.render.resolution_x = scene.render.resolution_y = 720
    os.makedirs(os.path.join(OUT, "review"), exist_ok=True)
    for name, yaw in [("front", 0), ("q34", 40), ("side", 90), ("back", 180)]:
        centre = Vector((0, 0.02, 0.56)); a = math.radians(yaw)
        cam.location = centre + Vector((3 * math.sin(a), -3 * math.cos(a), 0.4))
        cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(OUT, "review", name + ".png")
        bpy.ops.render.render(write_still=True)
print("PONCHO", json.dumps(report))
