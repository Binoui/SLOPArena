"""Render a .blend keeping its own materials: named views (yaw, pitch, centre, size, res, hide).

blender -b --python-exit-code 1 <file.blend> --python render_mats.py -- <out_dir> '<json views>'
"""
import bpy, sys, os, math, json
from mathutils import Vector
argv = sys.argv[sys.argv.index("--") + 1:]
out_dir, views = os.path.abspath(argv[0]), json.loads(argv[1])
os.makedirs(out_dir, exist_ok=True)
scene = bpy.context.scene
for o in list(scene.objects):
    if o.type in ("CAMERA", "LIGHT"):
        bpy.data.objects.remove(o)
world = bpy.data.worlds.new("review"); scene.world = world; world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.72, 0.72, 0.75, 1)
for r, e in (((math.radians(50), 0, math.radians(30)), 2.6), ((math.radians(65), 0, math.radians(200)), 1.4)):
    dl = bpy.data.lights.new("sun", "SUN"); dl.energy = e
    so = bpy.data.objects.new("sun", dl); scene.collection.objects.link(so); so.rotation_euler = r
scene.render.engine = "BLENDER_EEVEE"; scene.view_settings.view_transform = "Standard"
cd = bpy.data.cameras.new("cam"); cd.type = "ORTHO"
cam = bpy.data.objects.new("cam", cd); scene.collection.objects.link(cam); scene.camera = cam
meshes = [o for o in scene.objects if o.type == "MESH"]
for v in views:
    for o in meshes:
        o.hide_render = o.name in v.get("hide", [])
    yw, pt = math.radians(v["yaw"]), math.radians(v["pitch"])
    c = Vector(v["centre"])
    d = Vector((math.sin(yw) * math.cos(pt), -math.cos(yw) * math.cos(pt), math.sin(pt)))
    cam.location = c + d * 6; cam.rotation_euler = (c - cam.location).to_track_quat("-Z", "Y").to_euler()
    cd.ortho_scale = v["size"]; cd.clip_end = 20
    scene.render.resolution_x, scene.render.resolution_y = v.get("res", [600, 600])
    scene.render.filepath = os.path.join(out_dir, v["name"] + ".png")
    bpy.ops.render.render(write_still=True)
print("DONE", out_dir)
