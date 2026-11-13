"""Tumbleweed's own clips on the raw Tripo Dusty rig, keyed as a few poses per clip, exported at 60 fps.

blender -b --python make_clips.py -- <dusty.glb> <out.glb> <clips.json>

Directions are in the armature's own rest space: up = +Z, left = +X, forward = -Y. clips.json lists every clip's
frame count and loop flag for add_extras.py; a looping clip keys its last frame equal to its first.
"""
import bpy, sys, math, json
from mathutils import Vector, Quaternion

argv = sys.argv[sys.argv.index('--') + 1:]
src, dst, manifest = argv[0], argv[1], argv[2]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
scene = bpy.context.scene
scene.render.fps = 60
for o in list(bpy.data.objects):
    if o.type == 'MESH' and o.parent is None:
        bpy.data.objects.remove(o)
for a in list(bpy.data.actions):
    bpy.data.actions.remove(a)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
U, L, F = Vector((0, 0, 1)), Vector((1, 0, 0)), Vector((0, -1, 0))
D, R, B = -U, -L, -F
P = 'mixamorig:'


def d(*terms):
    v = Vector((0, 0, 0))
    for t in terms:
        v += t
    return v.normalized()


STANCE = {
    'Spine': d(U, 0.08 * F),
    'LeftArm': d(0.45 * L, 0.85 * D, 0.25 * F), 'LeftForeArm': d(0.15 * L, 0.35 * D, 0.95 * F),
    'RightArm': d(0.45 * R, 0.85 * D, 0.25 * F), 'RightForeArm': d(0.15 * R, 0.35 * D, 0.95 * F),
    'LeftUpLeg': d(0.12 * L, D, 0.58 * F), 'LeftLeg': d(0.06 * L, D, -0.58 * F),
    'RightUpLeg': d(0.12 * R, D, 0.58 * F), 'RightLeg': d(0.06 * R, D, -0.58 * F),
}


def pose(**over):
    p = dict(STANCE)
    p.update(over)
    return p


def apply(frame, targets, turn=None):
    """Key every bone so its final rest-space direction is the target; parents first. turn = (axis, degrees) on the hips."""
    acc = {}
    for b in arm.data.bones:
        pb = arm.pose.bones[b.name]
        parent = acc.get(b.parent.name, Quaternion()) if b.parent else Quaternion()
        rest = b.matrix_local.to_quaternion()
        local = Quaternion()
        short = b.name[len(P):] if b.name.startswith(P) else b.name
        if short == 'Hips' and turn:
            local = Quaternion(turn[0], math.radians(turn[1]))
        elif short in targets:
            r = (b.matrix_local.to_3x3() @ Vector((0, 1, 0))).normalized()
            t = parent.inverted() @ targets[short]
            local = r.rotation_difference(t)
        acc[b.name] = parent @ local
        pb.rotation_mode = 'QUATERNION'
        pb.rotation_quaternion = rest.inverted() @ local @ rest
        pb.location = (0, 0, 0)
        pb.keyframe_insert('rotation_quaternion', frame=frame)


CLIPS = []


def clip(name, frames, keys, loop=False):
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    arm.animation_data_create()
    arm.animation_data.action = act
    for frame, targets, turn in keys:
        apply(frame, targets, turn)
    arm.animation_data.action = None
    last = max(k[0] for k in keys)
    assert last == (frames if loop else frames - 1), (name, last, frames)
    CLIPS.append({'name': name, 'frames': frames, 'loop': loop})


# Locomotion and reactions.
breath = pose(Spine=d(U, 0.14 * F), LeftArm=d(0.5 * L, 0.8 * D, 0.3 * F), RightArm=d(0.5 * R, 0.8 * D, 0.3 * F))
clip('tw.idle', 60, [(0, pose(), None), (30, breath, None), (60, pose(), None)], loop=True)
stride_a = pose(Spine=d(U, 0.3 * F),
                LeftUpLeg=d(0.1 * L, D, 1.0 * F), LeftLeg=d(D, -0.2 * F), RightUpLeg=d(0.1 * R, D, -0.7 * F), RightLeg=d(D, -1.0 * F),
                LeftArm=d(0.3 * L, 0.8 * D, -0.6 * F), RightArm=d(0.3 * R, 0.6 * D, 0.8 * F), RightForeArm=d(0.1 * R, 0.2 * U, F))
stride_b = pose(Spine=d(U, 0.3 * F),
                RightUpLeg=d(0.1 * R, D, 1.0 * F), RightLeg=d(D, -0.2 * F), LeftUpLeg=d(0.1 * L, D, -0.7 * F), LeftLeg=d(D, -1.0 * F),
                RightArm=d(0.3 * R, 0.8 * D, -0.6 * F), LeftArm=d(0.3 * L, 0.6 * D, 0.8 * F), LeftForeArm=d(0.1 * L, 0.2 * U, F))
clip('tw.run', 30, [(0, stride_a, None), (15, stride_b, None), (30, stride_a, None)], loop=True)
lean = pose(Spine=d(U, 0.9 * F), LeftArm=d(0.4 * L, 0.5 * D, -0.8 * F), RightArm=d(0.4 * R, 0.5 * D, -0.8 * F),
            LeftUpLeg=d(0.1 * L, D, 0.9 * F), RightUpLeg=d(0.1 * R, D, -0.5 * F), RightLeg=d(D, -0.9 * F))
clip('tw.dash', 36, [(0, pose(), None), (6, lean, None), (28, lean, None), (35, pose(), None)])
tuck = pose(LeftUpLeg=d(0.15 * L, 0.3 * D, F), LeftLeg=d(0.1 * L, D, -0.4 * F), RightUpLeg=d(0.15 * R, 0.3 * D, F), RightLeg=d(0.1 * R, D, -0.4 * F),
            LeftArm=d(0.6 * L, 0.3 * U, 0.2 * F), RightArm=d(0.6 * R, 0.3 * U, 0.2 * F))
clip('tw.jump', 28, [(0, pose(), None), (8, tuck, None), (20, tuck, None), (27, pose(Spine=d(U, 0.05 * B)), None)])
float_a = pose(LeftArm=d(0.7 * L, 0.5 * U), RightArm=d(0.7 * R, 0.4 * U), LeftUpLeg=d(0.15 * L, D, 0.3 * F), RightUpLeg=d(0.1 * R, D, 0.5 * F))
float_b = pose(LeftArm=d(0.7 * L, 0.4 * U), RightArm=d(0.7 * R, 0.5 * U), LeftUpLeg=d(0.1 * L, D, 0.5 * F), RightUpLeg=d(0.15 * R, D, 0.3 * F))
clip('tw.fall', 20, [(0, float_a, None), (10, float_b, None), (20, float_a, None)], loop=True)
recoil = pose(Spine=d(U, 0.6 * B), LeftArm=d(0.8 * L, 0.3 * U, 0.4 * F), RightArm=d(0.8 * R, 0.2 * U, 0.4 * F),
              LeftForeArm=d(0.3 * L, U), RightForeArm=d(0.3 * R, U))
clip('tw.hit', 40, [(0, pose(), None), (4, recoil, None), (14, recoil, None), (39, pose(), None)])
spread = pose(LeftArm=d(L, 0.3 * U), RightArm=d(R, 0.3 * U), LeftUpLeg=d(0.6 * L, D), RightUpLeg=d(0.6 * R, D), Spine=d(U, 0.2 * B))
clip('tw.tumble', 40, [(f, spread, (L, a)) for f, a in [(0, 0), (10, 90), (20, 180), (30, 270), (40, 360)]], loop=True)

# Moves, keyed so the active pose covers each hitbox window of the kit.
punch = pose(Spine=d(U, 0.25 * F), RightArm=d(0.15 * R, 0.1 * D, F), RightForeArm=d(0.05 * R, F), LeftArm=d(0.4 * L, 0.6 * D, 0.5 * F))
clip('tw.jab', 28, [(0, pose(), None), (4, punch, None), (12, punch, None), (27, pose(), None)])
clip('tw.upswipe', 36, [
    (0, pose(), None),
    (6, pose(RightArm=d(0.3 * R, 0.8 * D, -0.5 * F), RightForeArm=d(0.3 * R, 0.8 * D, -0.5 * F), Spine=d(U, 0.2 * F)), None),
    (11, pose(RightArm=d(0.1 * R, U, 0.35 * F), RightForeArm=d(0.05 * R, U, 0.2 * F), Spine=d(U, -0.12 * F)), None),
    (18, pose(RightArm=d(0.15 * R, U, 0.2 * F), RightForeArm=d(0.1 * R, U, 0.1 * F), Spine=d(U, -0.1 * F)), None),
    (35, pose(), None),
])
kick = pose(Spine=d(U, 0.35 * B), RightUpLeg=d(0.15 * R, 0.1 * D, F), RightLeg=d(0.05 * R, F), LeftArm=d(0.7 * L, 0.2 * D), RightArm=d(0.7 * R, 0.4 * D, -0.3 * F))
clip('tw.bigkick', 52, [(0, pose(), None), (6, kick, None), (17, kick, None), (51, pose(), None)])
clip('tw.toss', 40, [
    (0, pose(), None),
    (8, pose(RightArm=d(0.6 * R, 0.5 * U, -0.6 * F), RightForeArm=d(0.2 * R, U), Spine=d(U, -0.15 * F)), None),
    (14, pose(RightArm=d(0.15 * R, 0.25 * U, F), RightForeArm=d(0.05 * R, 0.1 * U, F), Spine=d(U, 0.35 * F)), None),
    (22, pose(RightArm=d(0.1 * R, 0.6 * D, 0.8 * F), RightForeArm=d(0.1 * R, 0.5 * D, 0.85 * F), Spine=d(U, 0.3 * F)), None),
    (39, pose(), None),
])
ball = dict(LeftUpLeg=d(0.15 * L, 0.3 * D, F), LeftLeg=d(0.1 * L, D, -0.6 * F), RightUpLeg=d(0.15 * R, 0.3 * D, F), RightLeg=d(0.1 * R, D, -0.6 * F),
            LeftArm=d(0.3 * L, 0.6 * D, 0.7 * F), LeftForeArm=d(-0.5 * L, 0.2 * D, 0.8 * F), RightArm=d(0.3 * R, 0.6 * D, 0.7 * F),
            RightForeArm=d(-0.5 * R, 0.2 * D, 0.8 * F), Spine=d(U, 0.5 * F))
clip('tw.roll', 34, [(f, pose(**ball) if 4 <= f <= 28 else pose(), (L, a))
                     for f, a in [(0, 0), (4, 0), (10, 90), (16, 180), (22, 270), (28, 360), (33, 360)]])
reach = pose(Spine=d(U, 0.7 * F), LeftArm=d(0.3 * L, F), RightArm=d(0.3 * R, F), LeftForeArm=d(0.2 * L, F), RightForeArm=d(0.2 * R, F))
clip('tw.lunge', 12, [(0, pose(), None), (4, reach, None), (11, reach, None)])
hug = pose(Spine=d(U, 0.4 * F), LeftArm=d(0.2 * L, 0.2 * D, F), RightArm=d(0.2 * R, 0.2 * D, F), LeftForeArm=d(0.9 * R, 0.4 * F), RightForeArm=d(0.9 * L, 0.4 * F))
squeeze = pose(Spine=d(U, 0.2 * B), LeftArm=d(0.2 * L, 0.3 * D, F), RightArm=d(0.2 * R, 0.3 * D, F), LeftForeArm=d(R, 0.2 * F), RightForeArm=d(L, 0.2 * F))
heave = pose(Spine=d(U, 0.6 * B), LeftArm=d(0.3 * L, U, 0.3 * F), RightArm=d(0.3 * R, U, 0.3 * F), LeftForeArm=d(U, 0.4 * B), RightForeArm=d(U, 0.4 * B))
clip('tw.hugthrow', 56, [(0, hug, None), (8, squeeze, None), (18, hug, None), (24, squeeze, None), (34, heave, None), (44, heave, None), (55, pose(), None)])
kick_r = dict(RightUpLeg=d(0.6 * R, 0.1 * D, 0.8 * F), RightLeg=d(0.6 * R, 0.8 * F), LeftArm=d(L, 0.2 * U), RightArm=d(R, 0.2 * U))
kick_l = dict(LeftUpLeg=d(0.6 * L, 0.1 * D, 0.8 * F), LeftLeg=d(0.6 * L, 0.8 * F), LeftArm=d(L, 0.2 * U), RightArm=d(R, 0.2 * U))
devil = [(0, pose(), (U, 0))]
for i, (frame, k) in enumerate([(12, kick_r), (20, kick_l), (28, kick_r), (36, kick_l), (44, kick_r)]):
    devil.append((frame - 3, pose(**k), (U, -frame * 9)))
    devil.append((frame + 3, pose(**k), (U, -(frame + 3) * 9)))
devil += [(56, pose(LeftArm=d(0.3 * L, U), RightArm=d(0.3 * R, U), LeftUpLeg=d(0.5 * L, D), RightUpLeg=d(0.5 * R, D)), (U, -540)),
          (62, pose(LeftArm=d(0.3 * L, U), RightArm=d(0.3 * R, U)), (U, -540)), (89, pose(), (U, -720))]
clip('tw.devil', 90, devil)

for act in bpy.data.actions:
    for fc in act.fcurves if hasattr(act, 'fcurves') else []:
        for kp in fc.keyframe_points:
            kp.interpolation = 'BEZIER'

arm.animation_data.action = None
bpy.ops.export_scene.gltf(filepath=dst, export_format='GLB', export_animations=True,
                          export_animation_mode='ACTIONS', export_force_sampling=True, export_frame_step=1,
                          export_optimize_animation_size=False, export_anim_slide_to_zero=True,
                          export_reset_pose_bones=True)
with open(manifest, 'w') as out:
    json.dump(CLIPS, out, indent=1)
print('EXPORTED', dst, len(CLIPS), 'clips')
