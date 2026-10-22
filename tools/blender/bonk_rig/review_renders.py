"""Review renders of a built Sir Bonk rig (Blender approximation; Unity's look is checked separately).
blender -b --factory-startup --python-exit-code 1 --python tools/blender/bonk_rig/review_renders.py -- \
    <build_dir>/SirBonk_rig.blend <out_dir>
Writes rest_*.png (turnaround), pose_*.png (deformation tests: arms raised, overhead swing, kick, squat,
twist, nod, fists) and sheet_*.png contact sheets."""
import bpy, sys, os, math
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:]
blend, out_dir = (os.path.abspath(a) for a in argv[:2])
os.makedirs(out_dir, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=blend)
scene = bpy.context.scene
arm = bpy.data.objects["Armature"]
body = next(o for o in bpy.data.objects if o.type == "MESH")
P = "mixamorig:"

world = bpy.data.worlds.new("review")
scene.world = world
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.72, 0.72, 0.75, 1)
world.node_tree.nodes["Background"].inputs[1].default_value = 0.9
for rot, e in (((math.radians(50), 0, math.radians(30)), 2.6), ((math.radians(65), 0, math.radians(200)), 1.4)):
    d = bpy.data.lights.new("sun", "SUN")
    d.energy = e
    o = bpy.data.objects.new("sun", d)
    scene.collection.objects.link(o)
    o.rotation_euler = rot
scene.render.engine = "BLENDER_EEVEE"
scene.view_settings.view_transform = "Standard"
cam_d = bpy.data.cameras.new("cam")
cam_d.type = "ORTHO"
cam_d.sensor_fit = "AUTO"
cam = bpy.data.objects.new("cam", cam_d)
scene.collection.objects.link(cam)
scene.camera = cam
bpy.context.view_layer.update()
dg = bpy.context.evaluated_depsgraph_get()
ev = body.evaluated_get(dg)
zs = [(ev.matrix_world @ v.co).z for v in ev.data.vertices]
H = max(zs) - min(zs)
centre = Vector((0, 0, min(zs) + H / 2))


def shoot(name, target, yaw_deg, pitch_deg, ortho, res=(600, 800)):
    yaw, pitch = math.radians(yaw_deg), math.radians(pitch_deg)
    direction = Vector((math.sin(yaw) * math.cos(pitch), -math.cos(yaw) * math.cos(pitch), math.sin(pitch)))
    cam.location = target + direction * 6
    cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
    cam_d.ortho_scale = ortho
    cam_d.clip_end = 20
    scene.render.resolution_x, scene.render.resolution_y = res
    scene.render.filepath = os.path.join(out_dir, name + ".png")
    bpy.ops.render.render(write_still=True)


def bone_world(n):
    return arm.matrix_world @ arm.pose.bones[P + n].head


def rot_world(bone, axis_world, deg):
    bpy.context.view_layer.update()
    pb = arm.pose.bones[P + bone]
    axis = (arm.matrix_world.to_3x3().inverted() @ Vector(axis_world)).normalized()
    R = Matrix.Rotation(math.radians(deg), 4, axis)
    pb.matrix = Matrix.Translation(pb.head) @ R @ Matrix.Translation(-pb.head) @ pb.matrix
    bpy.context.view_layer.update()


def move_hips(dz):
    bpy.context.view_layer.update()
    pb = arm.pose.bones[P + "Hips"]
    pb.matrix = Matrix.Translation(arm.matrix_world.to_3x3().inverted() @ Vector((0, 0, dz))) @ pb.matrix
    bpy.context.view_layer.update()


def reset():
    for pb in arm.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()


S = 1.25 * H
for name, yaw in (("rest_front", 0), ("rest_34", 35), ("rest_side", 90), ("rest_back", 180), ("rest_34back", 215)):
    shoot(name, centre, yaw, 5, S)
head = bone_world("Head")
shoot("close_helm_front", head + Vector((0, 0, 0.03)), 0, 0, 0.3 * H, (600, 600))
shoot("close_helm_back", head + Vector((0, 0, 0.03)), 180, 10, 0.3 * H, (600, 600))

DOWN = [("LeftArm", (0, 1, 0), 75), ("RightArm", (0, 1, 0), -75)]
POSES = {
    "arms_down": DOWN,
    "arms_up": [("LeftArm", (0, 1, 0), -80), ("RightArm", (0, 1, 0), 80)],
    "overhead": [("LeftArm", (0, 1, 0), -60), ("LeftArm", (0, 0, 1), -60), ("RightArm", (0, 1, 0), 60),
                 ("RightArm", (0, 0, 1), 60), ("LeftForeArm", (0, 0, 1), -60), ("RightForeArm", (0, 0, 1), 60),
                 ("Spine2", (1, 0, 0), 12)],
    "kick": DOWN + [("LeftUpLeg", (1, 0, 0), -80), ("LeftLeg", (1, 0, 0), 45)],
    "run_back_leg": DOWN + [("LeftUpLeg", (1, 0, 0), 40), ("LeftLeg", (1, 0, 0), 70), ("RightUpLeg", (1, 0, 0), -35)],
    "squat": DOWN + [("LeftUpLeg", (1, 0, 0), -75), ("RightUpLeg", (1, 0, 0), -75), ("LeftLeg", (1, 0, 0), 110),
                     ("RightLeg", (1, 0, 0), 110), ("LeftFoot", (1, 0, 0), -35), ("RightFoot", (1, 0, 0), -35)],
    "twist_look": DOWN + [("Spine2", (0, 0, 1), 35), ("Spine1", (0, 0, 1), 15), ("Head", (0, 0, 1), 40)],
    "nod": DOWN + [("Head", (1, 0, 0), -30), ("Neck", (1, 0, 0), -10)],
}
for name, ops in POSES.items():
    reset()
    for bone, axis, deg in ops:
        rot_world(bone, axis, deg)
    if name == "squat":
        move_hips(-0.3 * H)
    shoot("pose_" + name + "_front", centre, 0, 5, S * 1.05)
    shoot("pose_" + name + "_34", centre, 40, 10, S * 1.05)
    if name in ("kick", "run_back_leg", "arms_up"):
        shoot("pose_" + name + "_side", centre, 90, 5, S * 1.05)
reset()
for side, sgn in (("Left", 1), ("Right", -1)):
    for f in ("Index", "Middle", "Ring", "Pinky"):
        for k, deg in ((1, 55), (2, 65), (3, 45)):
            rot_world(f"{side}Hand{f}{k}", (0, 1, 0), deg * sgn)
shoot("pose_fists_R", bone_world("RightHand"), 0, 30, 0.25 * H, (600, 600))
shoot("pose_fists_L", bone_world("LeftHand"), 0, 30, 0.25 * H, (600, 600))
reset()

try:
    from PIL import Image

    def sheet(names, out, h=520):
        ims = [Image.open(os.path.join(out_dir, n + ".png")).convert("RGB") for n in names]
        ims = [im.resize((int(im.width * h / im.height), h)) for im in ims]
        s = Image.new("RGB", (sum(i.width for i in ims), h))
        x = 0
        for i in ims:
            s.paste(i, (x, 0))
            x += i.width
        s.save(os.path.join(out_dir, out))
    sheet(["rest_front", "rest_34", "rest_side", "rest_back", "rest_34back"], "sheet_rest.png")
    sheet(["pose_arms_down_front", "pose_arms_up_front", "pose_arms_up_side", "pose_overhead_front", "pose_overhead_34"], "sheet_arms.png")
    sheet(["pose_kick_front", "pose_kick_side", "pose_run_back_leg_side", "pose_squat_front", "pose_squat_34"], "sheet_legs.png")
    sheet(["pose_twist_look_front", "pose_nod_34", "close_helm_front", "close_helm_back", "pose_fists_R"], "sheet_misc.png")
except ImportError:
    pass
