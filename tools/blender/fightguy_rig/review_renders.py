"""Review renders of the built FightGuy rig with a given base-color texture (Blender approximation only;
Unity's URP Lit look and real clips are verified separately).
blender -b --factory-startup --python-exit-code 1 --python tools/blender/fightguy_rig/review_renders.py -- \
    <build_dir>/FightGuy2_rig.blend <texture.png> <out_dir>"""
import bpy, sys, os, math
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
blend, texture, out_dir = (os.path.abspath(a) for a in argv[:3])
os.makedirs(out_dir, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=blend)
scene = bpy.context.scene
arm = bpy.data.objects["Armature"]
body = next(o for o in bpy.data.objects if o.type == "MESH")
img = bpy.data.images.load(texture, check_existing=False)
for slot in body.material_slots:
    for n in slot.material.node_tree.nodes:
        if n.type == "TEX_IMAGE":
            n.image = img
world = bpy.data.worlds.new("review")
scene.world = world
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.72, 0.72, 0.75, 1)
for rot, e in (((math.radians(50), 0, math.radians(30)), 2.5), ((math.radians(60), 0, math.radians(210)), 1.6)):
    d = bpy.data.lights.new("sun", "SUN")
    d.energy = e
    o = bpy.data.objects.new("sun", d)
    scene.collection.objects.link(o)
    o.rotation_euler = rot
scene.render.engine = "BLENDER_EEVEE"
scene.view_settings.view_transform = "Standard"
cam_d = bpy.data.cameras.new("cam")
cam_d.type = "ORTHO"
cam = bpy.data.objects.new("cam", cam_d)
scene.collection.objects.link(cam)
scene.camera = cam
bpy.context.view_layer.update()
mw = body.matrix_world
zs = [(mw @ v.co).z for v in body.data.vertices]
lo, hi = min(zs), max(zs)
height = hi - lo


def bone_pos(n):
    return arm.matrix_world @ arm.data.bones["mixamorig:" + n].head_local


def shoot(name, target, yaw_deg, pitch_deg, ortho, res=(900, 1200)):
    yaw, pitch = math.radians(yaw_deg), math.radians(pitch_deg)
    direction = Vector((math.sin(yaw) * math.cos(pitch), -math.cos(yaw) * math.cos(pitch), math.sin(pitch)))
    cam.location = target + direction * 6
    cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
    cam_d.ortho_scale = ortho
    cam_d.clip_end = 20
    scene.render.resolution_x, scene.render.resolution_y = res
    scene.render.filepath = os.path.join(out_dir, name + ".png")
    bpy.ops.render.render(write_still=True)


centre = Vector((0, 0, lo + height / 2))
shoot("front", centre, 0, 0, height * 1.1)
shoot("back", centre, 180, 0, height * 1.1)
shoot("back_34", centre, 145, 12, height * 1.1)
head = bone_pos("Head") + Vector((0, 0, height * 0.05))
shoot("face", head, 0, 0, height * 0.17, (800, 800))
shoot("face_34", head, 35, 0, height * 0.17, (800, 800))
elbow, wrist = bone_pos("RightForeArm"), bone_pos("RightHand")
shoot("right_arm_front", (elbow + wrist) / 2, 0, 15, height * 0.25, (900, 600))
shoot("right_arm_back", (elbow + wrist) / 2, 180, 15, height * 0.25, (900, 600))
shoot("back_upper", Vector((0, 0, lo + height * 0.70)), 180, 0, height * 0.45, (800, 800))
shoot("distance_back", centre, 160, 20, height * 1.1, (90, 120))
print("RENDERED", out_dir)
