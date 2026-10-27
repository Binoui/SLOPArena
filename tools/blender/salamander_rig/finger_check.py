"""Finger checks on the built rig: curls in the T-rest, then a finger gun with the arm forward.

blender -b --python-exit-code 1 <rig.blend> --python finger_check.py -- <texture.png> <out_dir>
"""
import bpy, sys, os, math
from mathutils import Vector, Matrix

tex, out = (os.path.abspath(a) for a in sys.argv[sys.argv.index("--") + 1:][:2])
os.makedirs(out, exist_ok=True)
scene = bpy.context.scene
arm = bpy.data.objects["Armature"]
img = bpy.data.images.load(tex)
for m in bpy.data.materials:
    if m.node_tree and "Jeans" not in m.name:
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

def shoot(name, yaw, pitch, centre, size, res=(520, 520)):
    yw, pt = math.radians(yaw), math.radians(pitch)
    d = Vector((math.sin(yw) * math.cos(pt), -math.cos(yw) * math.cos(pt), math.sin(pt)))
    cam.location = centre + d * 6; cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
    cd.ortho_scale = size; cd.clip_end = 20
    scene.render.resolution_x, scene.render.resolution_y = res
    scene.render.filepath = os.path.join(out, name + ".png")
    bpy.ops.render.render(write_still=True)

def pb(n):
    return arm.pose.bones[n if n in arm.pose.bones else P + n]

def rot_local(bone, axis_world, deg):
    """Rotate a bone about a world axis through its head (keeps children)."""
    bpy.context.view_layer.update()
    b = pb(bone)
    ax = (arm.matrix_world.to_3x3().inverted() @ Vector(axis_world)).normalized()
    R = Matrix.Rotation(math.radians(deg), 4, ax)
    b.matrix = Matrix.Translation(b.head) @ R @ Matrix.Translation(-b.head) @ b.matrix
    bpy.context.view_layer.update()

def reset():
    for p in arm.pose.bones:
        p.matrix_basis.identity()
    bpy.context.view_layer.update()

def curl(side, finger, degs):
    sgn = 1 if side == "Left" else -1
    for k, d in enumerate(degs):
        # each joint about its own bone's side axis (world Y in the T-rest), applied in the bone's current frame
        b = pb(f"{side}Hand{finger}{k + 1}")
        bpy.context.view_layer.update()
        ax_world = (arm.matrix_world.to_3x3() @ (b.matrix.to_3x3() @ Vector((0, 0, 1)))).normalized()
        rot_local(f"{side}Hand{finger}{k + 1}", ax_world, -d * sgn)

def handc(side):
    bpy.context.view_layer.update()
    b = pb(f"{side}HandMiddle2")
    return arm.matrix_world @ b.head

# 1) T-rest: middle + ring curled, index straight, thumb raised (left hand, palm down)
reset()
curl("Left", "Middle", (75, 85, 60)); curl("Left", "Ring", (75, 85, 60))
rot_local("LeftHandThumb1", (1, 0, 0), 35)
c = handc("Left")
shoot("t_fingergun_top", 0, 80, c, 0.24)
shoot("t_fingergun_front", 0, 5, c, 0.24)
shoot("t_fingergun_back", 180, 5, c, 0.24)
# 2) full curl (fist) and open
reset()
for f in ("Index", "Middle", "Ring"):
    curl("Left", f, (80, 90, 70))
rot_local("LeftHandThumb1", (0, 0, 1), -40)
c = handc("Left")
shoot("t_fist_front", 0, 5, c, 0.24)
shoot("t_fist_below", 0, -60, c, 0.24)
# 3) finger gun with the right arm forward, hand rolled thumb-up
reset()
rot_local("LeftArm", (0, 1, 0), 72)
rot_local("RightArm", (0, 0, 1), 85)
bpy.context.view_layer.update()
fa = pb("RightForeArm"); axis = (arm.matrix_world.to_3x3() @ (fa.tail - fa.head)).normalized()
rot_local("RightHand", axis, -90)
curl("Right", "Middle", (75, 85, 60)); curl("Right", "Ring", (75, 85, 60))
bpy.context.view_layer.update()
c = arm.matrix_world @ pb("RightHand").head
shoot("fingergun_q34", 35, 10, c + Vector((0, 0, 0)), 0.32)
shoot("fingergun_side", 90, 5, c, 0.32)
shoot("fingergun_full", 30, 8, Vector((0, -0.05, 0.56)), 1.3, res=(560, 640))
print("DONE", out)
