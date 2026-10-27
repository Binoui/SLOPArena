"""Pose checks on a built rig (T-pose rest): arms down idle, punch, two-hand point, head turn, tail swing, walk.

blender -b --python-exit-code 1 <rig.blend> --python pose_check.py -- <texture.png> <out_dir>
Writes the pose PNGs and sheet.jpg (idle front/three-quarter/side, punch, two-hand point, rear walk).
"""
import bpy, sys, os, math
import numpy as np
from mathutils import Vector, Matrix

tex, out = (os.path.abspath(a) for a in sys.argv[sys.argv.index("--") + 1:][:2])
os.makedirs(out, exist_ok=True)
scene = bpy.context.scene
arm = bpy.data.objects["Armature"]
img = bpy.data.images.load(tex)
for m in bpy.data.materials:
    if m.node_tree and "Jeans" not in m.name and "Poncho" not in m.name:
        for n in m.node_tree.nodes:
            if n.type == "TEX_IMAGE":
                n.image = img
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

def shoot(name, yaw, pitch, centre=Vector((0, 0.05, 0.56)), size=1.3):
    yw, pt = math.radians(yaw), math.radians(pitch)
    d = Vector((math.sin(yw) * math.cos(pt), -math.cos(yw) * math.cos(pt), math.sin(pt)))
    cam.location = centre + d * 6; cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
    cd.ortho_scale = size; cd.clip_end = 20
    scene.render.resolution_x, scene.render.resolution_y = 560, 640
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

def arms_down(deg=72):
    rot("LeftArm", (0, 1, 0), deg)     # left arm (+X) swings down
    rot("RightArm", (0, 1, 0), -deg)

POSES = {
    "idle": lambda: arms_down(),
    "punch_r": lambda: (rot("LeftArm", (0, 1, 0), 72), rot("RightArm", (0, 0, 1), 85), rot("RightArm", (1, 0, 0), 5)),
    "two_hand_point": lambda: (rot("LeftArm", (0, 0, 1), -70), rot("RightArm", (0, 0, 1), 70),
                               rot("LeftArm", (1, 0, 0), -10), rot("RightArm", (1, 0, 0), -10)),
    "look_nod": lambda: (arms_down(), rot("Head", (0, 0, 1), 40), rot("Head", (1, 0, 0), -20)),
    "tail_swing": lambda: (arms_down(), rot("Tail1", (0, 0, 1), 25), rot("Tail2", (0, 0, 1), 20), rot("Tail3", (0, 0, 1), 15),
                           rot("Tail4", (0, 0, 1), 10)),
    "walk": lambda: (arms_down(), rot("LeftUpLeg", (1, 0, 0), -35), rot("LeftLeg", (1, 0, 0), 25),
                     rot("RightUpLeg", (1, 0, 0), 25), rot("RightArm", (1, 0, 0), -25), rot("LeftArm", (1, 0, 0), 25)),
}
for name, f in POSES.items():
    reset(); f()
    shoot(name + "_front", 0, 4)
    shoot(name + "_q34", 40, 10)
    shoot(name + "_side", 90, 4)
    if name in ("tail_swing", "walk"):
        shoot(name + "_back", 180, 15)
tiles = ["idle_front", "idle_q34", "idle_side", "punch_r_q34", "two_hand_point_front", "walk_back"]
pixels = np.ones((960, 1260, 4), dtype=np.float32)
for i, name in enumerate(tiles):
    tile = bpy.data.images.load(os.path.join(out, name + ".png"), check_existing=False)
    tile.scale(420, 480)
    data = np.empty(420 * 480 * 4, dtype=np.float32); tile.pixels.foreach_get(data)
    x, y = (i % 3) * 420, (1 - i // 3) * 480
    pixels[y:y + 480, x:x + 420] = data.reshape(480, 420, 4)
    bpy.data.images.remove(tile)
sheet = bpy.data.images.new("Pose contact sheet", width=1260, height=960)
sheet.pixels.foreach_set(pixels.ravel())
sheet.filepath_raw = os.path.join(out, "sheet.jpg"); sheet.file_format = "JPEG"; sheet.save()
print("DONE", out)
