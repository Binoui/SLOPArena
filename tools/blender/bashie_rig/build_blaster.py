"""Bashie's toy water blaster (Hose Down, slot A) as a Unity-ready held prop.
blender -b --factory-startup --python-exit-code 1 --python tools/blender/bashie_rig/build_blaster.py -- \
    <blaster.fbx> <out_dir> [key=value ...]

The Tripo model lies 1.0 long along +X with its muzzle at +X, its tank on top (+Z) and its grip under the rear.
1. Bakes the import transform, deletes backdrop cards, decimates to `faces` and scales it to `length` (metres, at
   her rig's scale 1).
2. Turns the muzzle to -Y, Blender's forward, which the FBX export makes Unity's +Z (her own model faces -Y here and
   +Z in Unity). The tank stays up.
3. Puts the origin in her fist: the middle of the upper half of the grip, where her right hand closes. Her weapon
   config then places this point in the hand.
Writes <out_dir>/BashieBlaster.fbx (no embedded media), <out_dir>/textures/Bashie_Blaster.png, a review render
with the grip marked and <out_dir>/BashieBlaster_report.json.

Options:
  length=0.44   barrel length in metres: about as wide as her body, a toy next to Manki's bazooka
  faces=4000    face budget after decimation (the Tripo mesh has about 11,500)
  tex=1024      colour map size
"""
import bpy, bmesh, sys, os, json, math
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:]
src, out_dir = (os.path.abspath(a) for a in argv[:2])
opts = dict(a.split("=", 1) for a in argv[2:])
LENGTH = float(opts.get("length", 0.44))
FACES = int(opts.get("faces", 4000))
TEX = int(opts.get("tex", 1024))
NAME = "BashieBlaster"
os.makedirs(os.path.join(out_dir, "textures"), exist_ok=True)
report = {"source": src, "length": LENGTH}

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
meshes = [o for o in bpy.data.objects if o.type == "MESH"]
ob = max(meshes, key=lambda o: len(o.data.vertices))
ob.data.transform(ob.matrix_world)
ob.parent = None
ob.matrix_world = Matrix.Identity(4)
for o in list(bpy.data.objects):
    if o is not ob:
        bpy.data.objects.remove(o)
ob.name = ob.data.name = NAME
report["facesIn"] = len(ob.data.polygons)

# Backdrop cards: flat islands of at most 8 faces bigger than 20% of the longest side squared.
bm = bmesh.new()
bm.from_mesh(ob.data)
dims = ob.dimensions
limit = 0.2 * max(dims) ** 2
seen, cards = set(), []
for f in bm.faces:
    if f.index in seen:
        continue
    island, stack = [], [f]
    seen.add(f.index)
    while stack:
        g = stack.pop()
        island.append(g)
        for e in g.edges:
            for h in e.link_faces:
                if h.index not in seen:
                    seen.add(h.index)
                    stack.append(h)
    if len(island) <= 8:
        area = sum(g.calc_area() for g in island)
        ns = island[0].normal
        if area > limit and all(abs(g.normal.dot(ns)) > 0.999 for g in island):
            cards.extend(island)
bmesh.ops.delete(bm, geom=list({v for g in cards for v in g.verts}), context="VERTS")
bm.to_mesh(ob.data)
bm.free()
report["cardsDeleted"] = len(cards)

# Decimate to the face budget; collapse keeps the UVs.
if len(ob.data.polygons) > FACES:
    mod = ob.modifiers.new("decimate", "DECIMATE")
    mod.ratio = FACES / len(ob.data.polygons)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.modifier_apply(modifier=mod.name)
report["facesOut"] = len(ob.data.polygons)

# Muzzle to -Y, scaled to LENGTH.
me = ob.data
xs = [v.co.x for v in me.vertices]
scale = LENGTH / (max(xs) - min(xs))
me.transform(Matrix.Rotation(-math.pi / 2, 4, "Z") @ Matrix.Scale(scale, 4))

# Origin in her fist: the grip is the part under the body at the rear; she closes her hand on its upper half.
co = [v.co.copy() for v in me.vertices]
y0, y1 = min(p.y for p in co), max(p.y for p in co)  # muzzle at y0, rear at y1
z0, z1 = min(p.z for p in co), max(p.z for p in co)
grip = [p for p in co if p.y > y1 - 0.35 * (y1 - y0) and p.z < z0 + 0.30 * (z1 - z0)]
gz0, gz1 = min(p.z for p in grip), max(p.z for p in grip)
upper = [p for p in grip if p.z > (gz0 + gz1) / 2]
fist = Vector((sum(p.x for p in upper) / len(upper), sum(p.y for p in upper) / len(upper),
               sum(p.z for p in upper) / len(upper)))
me.transform(Matrix.Translation(-fist))
co = [v.co for v in me.vertices]
report["gripVertices"] = len(grip)
report["boundsMetres"] = [[round(min(p[i] for p in co), 4) for i in range(3)], [round(max(p[i] for p in co), 4) for i in range(3)]]
report["muzzleFromFist"] = round(-min(p.y for p in co), 4)

# One material, its colour map downsized.
mat = me.materials[0]
mat.name = "Bashie_Blaster"
while len(me.materials) > 1:
    me.materials.pop(index=len(me.materials) - 1)
for poly in me.polygons:
    poly.material_index = 0
node = max((n for n in mat.node_tree.nodes if n.type == "TEX_IMAGE" and n.image), key=lambda n: n.image.size[0])
img = node.image
if img.size[0] > TEX:
    img.scale(TEX, TEX)
img.filepath_raw = os.path.join(out_dir, "textures", "Bashie_Blaster.png")
img.file_format = "PNG"
img.save()
report["texture"] = {"file": img.filepath_raw, "size": list(img.size)}

# Review: side and three-quarter views, the fist marked.
scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = scene.render.resolution_y = 640
scene.world = bpy.data.worlds.new("w")
scene.world.use_nodes = True
scene.world.node_tree.nodes["Background"].inputs[0].default_value = (0.8, 0.8, 0.82, 1)
bpy.ops.mesh.primitive_uv_sphere_add(radius=0.025, location=(0, 0, 0))
dot = bpy.context.active_object
dmat = bpy.data.materials.new("dot")
dmat.diffuse_color = (0.1, 0.9, 0.2, 1)
dot.data.materials.append(dmat)
dot.show_in_front = True
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
scene.collection.objects.link(cam)
scene.camera = cam
cam.data.type = "ORTHO"
cam.data.ortho_scale = LENGTH * 1.4
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
scene.collection.objects.link(sun)
sun.rotation_euler = (math.radians(50), 0, math.radians(30))
sun.data.energy = 3
centre = Vector((0, -LENGTH * 0.35, 0.05))
for view, d in {"side": (1, 0, 0), "q34": (0.7, -0.6, 0.4)}.items():
    d = Vector(d).normalized()
    cam.location = centre + d * 3
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = os.path.join(out_dir, f"review_{view}.png")
    bpy.ops.render.render(write_still=True)
bpy.data.objects.remove(dot)

bpy.ops.object.select_all(action="DESELECT")
ob.select_set(True)
fbx = os.path.join(out_dir, NAME + ".fbx")
bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True, object_types={"MESH"}, bake_anim=False,
                         path_mode="STRIP", embed_textures=False, mesh_smooth_type="OFF")
report["fbx"] = fbx
json.dump(report, open(os.path.join(out_dir, NAME + "_report.json"), "w"), indent=1)
print("REPORT", json.dumps(report))
