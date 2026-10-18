"""Neutral front / 3-4 / back / face renders of a character or prop, for model sheets.

blender -b --factory-startup --python-exit-code 1 --python tools/blender/model_sheet/render_views.py -- \
    <model.fbx|.glb> <basecolor.png|-|grey> <out_dir>

`-` keeps the imported materials; `grey` replaces a missing texture with flat grey (shape only).
Blender approximation of the model, not Unity's URP look.
"""
import bpy, sys, os, math
from mathutils import Vector

model, texture, out_dir = sys.argv[sys.argv.index("--") + 1:][:3]
os.makedirs(out_dir, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
importer = bpy.ops.import_scene.gltf if model.lower().endswith((".glb", ".gltf")) else bpy.ops.import_scene.fbx
importer(filepath=os.path.abspath(model))
scene = bpy.context.scene
meshes = [o for o in scene.objects if o.type == "MESH"]
image = bpy.data.images.load(os.path.abspath(texture)) if texture not in ("-", "grey") else None
for o in meshes:
    for slot in o.material_slots:
        if slot.material is None:
            continue
        slot.material.use_nodes = True
        nodes = slot.material.node_tree
        bsdf = next((n for n in nodes.nodes if n.type == "BSDF_PRINCIPLED"), None)
        if bsdf is None:
            continue
        if image is not None:
            tex = next((n for n in nodes.nodes if n.type == "TEX_IMAGE"), None) or nodes.nodes.new("ShaderNodeTexImage")
            tex.image = image
            nodes.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        if texture == "grey":
            for link in list(bsdf.inputs["Base Color"].links):
                nodes.links.remove(link)
            bsdf.inputs["Base Color"].default_value = (0.42, 0.43, 0.45, 1)
        bsdf.inputs["Roughness"].default_value = 0.85
        bsdf.inputs["Metallic"].default_value = 0.0

world = bpy.data.worlds.new("sheet")
scene.world = world
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.72, 0.72, 0.75, 1)
for rotation, energy in (((math.radians(50), 0, math.radians(30)), 2.5), ((math.radians(60), 0, math.radians(210)), 1.6)):
    light = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    light.data.energy = energy
    light.rotation_euler = rotation
    scene.collection.objects.link(light)
scene.render.engine = "BLENDER_EEVEE"
scene.view_settings.view_transform = "Standard"
camera = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
camera.data.type = "ORTHO"
scene.collection.objects.link(camera)
scene.camera = camera
bpy.context.view_layer.update()

# Bounds of the evaluated (rest-pose, skinned) meshes.
depsgraph = bpy.context.evaluated_depsgraph_get()
points = []
for o in meshes:
    evaluated = o.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh()
    points += [evaluated.matrix_world @ v.co for v in mesh.vertices]
    evaluated.to_mesh_clear()
lo, hi = min(p.z for p in points), max(p.z for p in points)
height = hi - lo
cx = (min(p.x for p in points) + max(p.x for p in points)) / 2
cy = (min(p.y for p in points) + max(p.y for p in points)) / 2
width = max(max(p.x for p in points) - min(p.x for p in points), max(p.y for p in points) - min(p.y for p in points))

head = None
for armature in (o for o in scene.objects if o.type == "ARMATURE"):
    for bone in armature.data.bones:
        if bone.name.lower().split(":")[-1] == "head":
            head = armature.matrix_world @ bone.head_local
if head is None or not lo + height * 0.7 < head.z < hi:
    head = Vector((cx, cy, hi - height * 0.11))


def shoot(name, target, yaw_deg, pitch_deg, ortho, resolution):
    yaw, pitch = math.radians(yaw_deg), math.radians(pitch_deg)
    direction = Vector((math.sin(yaw) * math.cos(pitch), -math.cos(yaw) * math.cos(pitch), math.sin(pitch)))
    camera.location = target + direction * (height * 4 + 6)
    camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera.data.ortho_scale = ortho
    camera.data.clip_end = height * 10 + 20
    scene.render.resolution_x, scene.render.resolution_y = resolution
    scene.render.filepath = os.path.join(out_dir, name + ".png")
    bpy.ops.render.render(write_still=True)


centre = Vector((cx, cy, lo + height / 2))
span = max(height, width) * 1.08
body = (900, 1200) if width < height * 0.9 else (1200, 1200)
shoot("front", centre, 0, 0, span, body)
shoot("q3", centre, 35, 8, span, body)
shoot("back", centre, 180, 0, span, body)
face = head + Vector((0, 0, height * 0.04))
shoot("face", face, 0, 0, height * 0.22, (800, 800))
shoot("face_q3", face, 35, 0, height * 0.22, (800, 800))
print("RENDERED", out_dir)
