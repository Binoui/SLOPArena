"""Split the Tripo kit into named part objects, report their sizes and render each part alone.

blender -b --factory-startup --python-exit-code 1 --python split_parts.py -- <model.fbx> <texture.png> <out_dir>
Writes parts.blend (one object per part, transforms applied), parts.json and <part>_<view>.png.
"""
import bpy, bmesh, sys, os, math, json
from mathutils import Vector

fbx, tex, out_dir = (os.path.abspath(a) for a in sys.argv[sys.argv.index("--") + 1:][:3])
os.makedirs(out_dir, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx)
scene = bpy.context.scene
src = next(o for o in scene.objects if o.type == "MESH")
bpy.context.view_layer.objects.active = src
src.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

# Islands by face connectivity.
bm = bmesh.new(); bm.from_mesh(src.data); bm.faces.ensure_lookup_table(); bm.verts.ensure_lookup_table()
seen, islands = set(), []
for f in bm.faces:
    if f.index in seen:
        continue
    comp, stack = [], [f]
    seen.add(f.index)
    while stack:
        g = stack.pop(); comp.append(g.index)
        for e in g.edges:
            for h in e.link_faces:
                if h.index not in seen:
                    seen.add(h.index); stack.append(h)
    vs = {v.index for i in comp for v in bm.faces[i].verts}
    islands.append((comp, vs))

def classify(vs):
    pts = [bm.verts[i].co for i in vs]
    c = sum(pts, Vector()) / len(pts)
    lo = Vector([min(p[i] for p in pts) for i in range(3)]); hi = Vector([max(p[i] for p in pts) for i in range(3)])
    n = len(vs)
    if n > 2000:
        return "body"
    if n == 4 and len(vs) == 4:
        return "card"
    if c.z > 0.8 and n > 400:
        return "head2"
    if c.z > 0.8 and (hi.y - lo.y) < 0.05 and (hi.x - lo.x) > 0.1:
        return "mustache"
    if c.x > 0.44:
        return "straw"
    if 0.5 < c.z < 0.62 and n > 200:
        return "brim"
    if 0.62 <= c.z < 0.76:
        return "crown"
    if 0.2 < c.z < 0.5:
        return "poncho"
    if c.z < 0.12 and n > 150:
        return "boot"
    if c.z < 0.12:
        return "bootbits"
    return "unknown"

groups = {}
for comp, vs in islands:
    groups.setdefault(classify(vs), []).append(comp)
print("GROUPS", {k: [len(c) for c in v] for k, v in groups.items()})
bm.free()

# One object per group.
parts = {}
for name, comps in groups.items():
    keep = {i for c in comps for i in c}
    ob = src.copy(); ob.data = src.data.copy(); ob.name = ob.data.name = name
    scene.collection.objects.link(ob)
    b = bmesh.new(); b.from_mesh(ob.data); b.faces.ensure_lookup_table()
    bmesh.ops.delete(b, geom=[f for f in b.faces if f.index not in keep], context="FACES")
    loose = [v for v in b.verts if not v.link_faces]
    bmesh.ops.delete(b, geom=loose, context="VERTS")
    b.to_mesh(ob.data); b.free()
    parts[name] = ob
bpy.data.objects.remove(src)

report = {}
for name, ob in parts.items():
    pts = [v.co for v in ob.data.vertices]
    lo = [min(p[i] for p in pts) for i in range(3)]; hi = [max(p[i] for p in pts) for i in range(3)]
    report[name] = {"verts": len(pts), "faces": len(ob.data.polygons), "lo": [round(x, 4) for x in lo],
                    "hi": [round(x, 4) for x in hi], "size": [round(hi[i] - lo[i], 4) for i in range(3)]}
    print("PART", name, json.dumps(report[name]))
json.dump(report, open(os.path.join(out_dir, "parts.json"), "w"), indent=1)

img = bpy.data.images.load(tex, check_existing=True)
mat = next(iter(parts.values())).material_slots[0].material
mat.use_nodes = True
nt = mat.node_tree
bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
tn = nt.nodes.new("ShaderNodeTexImage"); tn.image = img
nt.links.new(tn.outputs["Color"], bsdf.inputs["Base Color"])
bsdf.inputs["Roughness"].default_value = 0.85
bsdf.inputs["Metallic"].default_value = 0.0
for l in list(bsdf.inputs["Alpha"].links):
    nt.links.remove(l)
bsdf.inputs["Alpha"].default_value = 1.0
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out_dir, "parts.blend"))

world = bpy.data.worlds.new("review"); scene.world = world; world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.72, 0.72, 0.75, 1)
for rot, e in (((math.radians(50), 0, math.radians(30)), 2.6), ((math.radians(65), 0, math.radians(200)), 1.4)):
    d = bpy.data.lights.new("sun", "SUN"); d.energy = e
    so = bpy.data.objects.new("sun", d); scene.collection.objects.link(so); so.rotation_euler = rot
scene.render.engine = "BLENDER_EEVEE"
scene.view_settings.view_transform = "Standard"
cam_d = bpy.data.cameras.new("cam"); cam_d.type = "ORTHO"
cam = bpy.data.objects.new("cam", cam_d); scene.collection.objects.link(cam); scene.camera = cam

def shoot(path, centre, size, yaw_deg, pitch_deg, res=500):
    yaw, pitch = math.radians(yaw_deg), math.radians(pitch_deg)
    direction = Vector((math.sin(yaw) * math.cos(pitch), -math.cos(yaw) * math.cos(pitch), math.sin(pitch)))
    cam.location = centre + direction * 10
    cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
    cam_d.ortho_scale = size; cam_d.clip_end = 30
    scene.render.resolution_x = scene.render.resolution_y = res
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)

for name, ob in parts.items():
    if name == "body":
        continue
    for other in parts.values():
        other.hide_render = other is not ob
    r = report[name]
    centre = Vector([(r["lo"][i] + r["hi"][i]) / 2 for i in range(3)])
    size = max(r["size"]) * 1.3 + 0.01
    for view, yaw, pitch in (("mY", 0, 0), ("pX", 90, 0), ("pY", 180, 0), ("mX", 270, 0), ("top", 0, 89.5), ("under", 0, -89.5)):
        shoot(os.path.join(out_dir, f"{name}_{view}.png"), centre, size, yaw, pitch)
print("DONE", out_dir)
