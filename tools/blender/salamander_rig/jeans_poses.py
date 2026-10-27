"""Jeans checks on a built rig, poncho hidden: walk, high kick, crouch, tail swing.

blender -b --python-exit-code 1 <rig.blend> --python jeans_poses.py -- <out_dir>
"""
import bpy, sys, os, math
from mathutils import Vector, Matrix
out = os.path.abspath(sys.argv[sys.argv.index("--") + 1]); os.makedirs(out, exist_ok=True)
scene = bpy.context.scene
arm = bpy.data.objects["Armature"]
for o in scene.objects:
    if o.type == "MESH" and "Poncho" in o.name:
        o.hide_render = True
for o in list(scene.objects):
    if o.type in ("CAMERA", "LIGHT"):
        bpy.data.objects.remove(o)
world = bpy.data.worlds.new("w"); scene.world = world; world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.72, 0.72, 0.75, 1)
for r, e in (((math.radians(50), 0, math.radians(30)), 2.6), ((math.radians(65), 0, math.radians(200)), 1.4)):
    dl = bpy.data.lights.new("sun", "SUN"); dl.energy = e
    so = bpy.data.objects.new("sun", dl); scene.collection.objects.link(so); so.rotation_euler = r
scene.render.engine = "BLENDER_EEVEE"; scene.view_settings.view_transform = "Standard"
cd = bpy.data.cameras.new("c"); cd.type = "ORTHO"; cam = bpy.data.objects.new("c", cd); scene.collection.objects.link(cam); scene.camera = cam
P = "mixamorig:"
def shoot(name, yaw, pitch, centre=Vector((0, 0.05, 0.4)), size=0.9):
    yw, pt = math.radians(yaw), math.radians(pitch)
    d = Vector((math.sin(yw) * math.cos(pt), -math.cos(yw) * math.cos(pt), math.sin(pt)))
    cam.location = centre + d * 6; cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
    cd.ortho_scale = size; cd.clip_end = 20
    scene.render.resolution_x, scene.render.resolution_y = 500, 500
    scene.render.filepath = os.path.join(out, name + ".png")
    bpy.ops.render.render(write_still=True)
def rot(bone, axis, deg):
    bpy.context.view_layer.update()
    pb = arm.pose.bones[bone if bone in arm.pose.bones else P + bone]
    ax = (arm.matrix_world.to_3x3().inverted() @ Vector(axis)).normalized()
    R = Matrix.Rotation(math.radians(deg), 4, ax)
    pb.matrix = Matrix.Translation(pb.head) @ R @ Matrix.Translation(-pb.head) @ pb.matrix
    bpy.context.view_layer.update()
def reset():
    for pb in arm.pose.bones:
        pb.matrix_basis.identity()
    bpy.context.view_layer.update()
def arms_down():
    rot("LeftArm", (0, 1, 0), 72); rot("RightArm", (0, 1, 0), -72)
POSES = {
    "walk": lambda: (arms_down(), rot("LeftUpLeg", (1, 0, 0), -35), rot("LeftLeg", (1, 0, 0), 25), rot("RightUpLeg", (1, 0, 0), 25)),
    "kick": lambda: (arms_down(), rot("RightUpLeg", (1, 0, 0), -80), rot("RightLeg", (1, 0, 0), 20)),
    "crouch": lambda: (arms_down(), rot("LeftUpLeg", (1, 0, 0), -70), rot("LeftLeg", (1, 0, 0), 110), rot("RightUpLeg", (1, 0, 0), -70),
                       rot("RightLeg", (1, 0, 0), 110)),
    "tail": lambda: (arms_down(), rot("Tail1", (0, 0, 1), 30), rot("Tail2", (0, 0, 1), 20), rot("Tail1", (1, 0, 0), -25)),
}
for name, f in POSES.items():
    reset(); f()
    shoot(name + "_q34", 40, 8)
    shoot(name + "_back", 160, 12)
print("DONE", out)
