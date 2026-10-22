"""Render the rig's joints over a see-through body (front and side), to judge joint placement.

blender -b --factory-startup --python-exit-code 1 --python tools/blender/bashie_rig/joint_map.py -- \
    <rig.fbx> <basecolor.png> <out_dir>
"""
import bpy, sys, os, math
from mathutils import Vector

fbx, tex, out_dir = (os.path.abspath(a) for a in sys.argv[sys.argv.index("--") + 1:][:3])
os.makedirs(out_dir, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx)
scene = bpy.context.scene
arm = next(o for o in scene.objects if o.type == "ARMATURE")
body = next(o for o in scene.objects if o.type == "MESH")
if arm.animation_data and arm.animation_data.action:
    bpy.context.scene.frame_set(int(arm.animation_data.action.frame_range[0]))
    bpy.context.view_layer.update()
img = bpy.data.images.load(tex)
for s in body.material_slots:
    m = s.material; m.use_nodes = True; nt = m.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    tn = nt.nodes.new("ShaderNodeTexImage"); tn.image = img
    nt.links.new(tn.outputs["Color"], bsdf.inputs["Base Color"])
    for l in list(bsdf.inputs["Alpha"].links): nt.links.remove(l)
    bsdf.inputs["Alpha"].default_value = 0.28
    bsdf.inputs["Roughness"].default_value = 0.9
    if hasattr(m, "surface_render_method"): m.surface_render_method = "BLENDED"
    if hasattr(m, "blend_method"): m.blend_method = "BLEND"
    m.use_backface_culling = False

def mat(name, rgb):
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*rgb, 1)
    b.inputs["Emission Color"].default_value = (*rgb, 1); b.inputs["Emission Strength"].default_value = 0.6
    return m
ok, bad = mat("joint", (1.0, 0.8, 0.13)), mat("joint_bad", (0.94, 0.25, 0.15))
focus = {"LeftForeArm", "RightForeArm", "LeftHand", "RightHand", "LeftLeg", "RightLeg", "LeftFoot", "RightFoot"}
mw = arm.matrix_world
for pb in arm.pose.bones:
    b = pb.bone
    short = b.name.replace("mixamorig:", "")
    h, t = mw @ pb.head, mw @ pb.tail
    bpy.ops.mesh.primitive_uv_sphere_add(radius=0.009 if short in focus else 0.006, location=h)
    sph = bpy.context.object; sph.data.materials.append(bad if short in focus else ok)
    d = t - h
    if d.length > 1e-4 and not b.name.endswith("_End") and "4" not in short[-1:]:
        bpy.ops.mesh.primitive_cylinder_add(radius=0.0025, depth=d.length, location=(h + t) / 2)
        cyl = bpy.context.object
        cyl.rotation_euler = d.to_track_quat("Z", "Y").to_euler()
        cyl.data.materials.append(ok)

world = bpy.data.worlds.new("w"); scene.world = world; world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.86, 0.86, 0.88, 1)
for rot, e in (((math.radians(50), 0, math.radians(30)), 2.4), ((math.radians(65), 0, math.radians(200)), 1.2)):
    d = bpy.data.lights.new("sun", "SUN"); d.energy = e
    o = bpy.data.objects.new("sun", d); scene.collection.objects.link(o); o.rotation_euler = rot
scene.render.engine = "BLENDER_EEVEE"; scene.view_settings.view_transform = "Standard"
cam_d = bpy.data.cameras.new("cam"); cam_d.type = "ORTHO"
cam = bpy.data.objects.new("cam", cam_d); scene.collection.objects.link(cam); scene.camera = cam
zs = [(body.matrix_world @ v.co).z for v in body.data.vertices]
H = max(zs) - min(zs); centre = Vector((0, 0, min(zs) + H / 2))
for name, yaw in (("joints_front", 0), ("joints_side", 90)):
    y = math.radians(yaw)
    cam.location = centre + Vector((math.sin(y), -math.cos(y), 0)) * 6
    cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
    cam_d.ortho_scale = 1.1 * H; cam_d.clip_end = 20
    scene.render.resolution_x, scene.render.resolution_y = 700, 800
    scene.render.filepath = os.path.join(out_dir, name + ".png")
    bpy.ops.render.render(write_still=True)
for pb in arm.pose.bones:
    b = pb.bone
    if b.name.replace("mixamorig:", "") in focus | {"LeftArm", "RightArm", "LeftUpLeg", "RightUpLeg", "LeftToeBase"}:
        h = mw @ pb.head
        print(f"JOINT {b.name.replace('mixamorig:', ''):12s} x={h.x:+.3f} y={h.y:+.3f} z={h.z:.3f}")
print("RENDERED", out_dir)
