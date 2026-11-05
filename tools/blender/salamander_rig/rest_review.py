"""Render Dusty's bare body around one shoulder, in clay, at its bone rest or posed into the authored A-pose.

blender -b --factory-startup --python-exit-code 1 --python rest_review.py -- <model.fbx> <out_dir> <label> \
  [side=Right] [scale=0.42] [res=900] [apose=<salamander-mixamo.fbx> [ref=<other model.fbx>]]

Writes <label>-front.png, <label>-side.png (from that side) and <label>-below.png (up into the armpit), centred on the
shoulder joint. The hat and poncho are hidden. Run it on the Mixamo download too: its rest is the authored A-pose.
apose= poses the model into the Mixamo download's bone rest (the authored A-pose). With ref=, the body is coloured by
how far each vertex lands from the same vertex of ref posed the same way: grey at 0, full red at 2 cm. Use the pre-fix
FBX as ref to see where a rebuilt rest no longer reproduces the authored body.
"""
import bpy, sys, os
import numpy as np
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:]
FBX, OUT, LABEL = os.path.abspath(argv[0]), os.path.abspath(argv[1]), argv[2]
OPT = dict(a.split("=", 1) for a in argv[3:])
SIDE = OPT.get("side", "Right")
P = "mixamorig:"
os.makedirs(OUT, exist_ok=True)


def load(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path, ignore_leaf_bones=False)
    arm = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
    arm.data.pose_position = "REST"  # the Mixamo download carries a T-pose take
    return arm


def world_bones(arm):
    out = {}
    for b in arm.data.bones:
        m = np.array(arm.matrix_world @ b.matrix_local)
        m[:3, :3] /= np.linalg.norm(m[:3, :3], axis=0)
        out[b.name] = Matrix(m.tolist())
    return out


def pose_apose(arm, mix):
    """Pose each bone by its world change from this rest to the Mixamo rest (bones Mixamo lacks follow their parent)."""
    arm.data.pose_position = "POSE"
    live = world_bones(arm)
    shift = Matrix.Translation(live[P + "Hips"].translation - mix[P + "Hips"].translation)
    Af = arm.matrix_world
    M = {}
    for b in sorted(arm.data.bones, key=lambda b: len(b.parent_recursive)):
        m = b
        while m.name not in mix:
            m = m.parent
        D = shift @ mix[m.name] @ live[m.name].inverted()
        M[b.name] = Af.inverted() @ D @ Af @ b.matrix_local
        rel = (b.parent.matrix_local.inverted() @ b.matrix_local) if b.parent else b.matrix_local
        par = M[b.parent.name] if b.parent else Matrix()
        arm.pose.bones[b.name].matrix_basis = rel.inverted() @ par.inverted() @ M[b.name]
    bpy.context.view_layer.update()


def body_world():
    body = bpy.data.objects["Salamander_Body"]
    ev = body.evaluated_get(bpy.context.evaluated_depsgraph_get())
    co = np.array([body.matrix_world @ v.co for v in ev.data.vertices])
    return body, co


MIX = world_bones(load(os.path.abspath(OPT["apose"]))) if "apose" in OPT else None
REF = None
if MIX and "ref" in OPT:
    pose_apose(load(os.path.abspath(OPT["ref"])), MIX)
    REF = body_world()[1]

arm = load(FBX)
if MIX:
    pose_apose(arm, MIX)
scene = bpy.context.scene
for o in scene.objects:
    if o.type == "MESH" and any(k in o.name for k in ("Hat", "Poncho")):
        o.hide_render = True
scene.render.engine = "BLENDER_WORKBENCH"
sh = scene.display.shading
sh.light, sh.color_type, sh.show_cavity = "STUDIO", "SINGLE", True
sh.single_color = (0.75, 0.72, 0.66)
if REF is not None:
    body, co = body_world()
    err = np.linalg.norm(co - REF, axis=1)
    k = np.clip(err / 0.02, 0, 1)
    col = body.data.color_attributes.new("deviation", "FLOAT_COLOR", "POINT")
    for i, a in enumerate(k):
        col.data[i].color = (0.62 + 0.33 * a, 0.6 * (1 - a) + 0.08, 0.55 * (1 - a) + 0.08, 1)
    body.data.color_attributes.active_color = col
    sh.color_type = "VERTEX"
    print("DEVIATION", f"max {err.max() * 100:.2f} cm, over 5 mm {(err > 0.005).sum()}, over 1 cm {(err > 0.01).sum()}")
scene.render.resolution_x = scene.render.resolution_y = int(OPT.get("res", 900))
scene.world = bpy.data.worlds.new("w")
scene.world.color = (0.02, 0.02, 0.025)
cd = bpy.data.cameras.new("cam"); cd.type = "ORTHO"; cd.ortho_scale = float(OPT.get("scale", 0.42))
cam = bpy.data.objects.new("cam", cd); scene.collection.objects.link(cam); scene.camera = cam

pb = arm.pose.bones[P + SIDE + "Arm"]
joint = arm.matrix_world @ pb.head
sgn = -1.0 if SIDE == "Right" else 1.0
centre = joint + Vector((sgn * 0.04, 0, -0.05))
VIEWS = {"front": Vector((0, -1, 0)), "side": Vector((sgn, 0, 0)), "below": Vector((sgn * 0.35, -0.55, -0.75)).normalized()}
for name, d in VIEWS.items():
    cam.location = centre + d * 3
    cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = os.path.join(OUT, f"{LABEL}-{name}.png")
    bpy.ops.render.render(write_still=True)
