"""Preview Bashie's shell tuck (Shell Slide, R) on her rig: what ShellTuckRig does in Unity, rendered in Blender.
blender -b --factory-startup --python-exit-code 1 --python tools/blender/bashie_rig/tuck_preview.py -- \
    <Bashie_rig.blend> <out_dir> '<spec json>'

The bone changes are the ones ShellTuckRig (client/Unity/Assets/Scripts/Runtime/Entities/ShellTuckRig.cs) makes after
her clip: the body pitched prone with the shell on top, the shell grown on its bone, the upper arms and legs shrunk
into it, the whistle hidden, and the head held upright where it peeks out of the shell's front. Try a look here before
touching Unity. <Bashie_rig.blend> is build_rig.py's output.

The spec names variants, each fully tucked at t = 1:
  {"variants": {"A": {"shell": 1.7, "arm": 0.15, "leg": 0.3, "headScale": 0.8, "peek": [0, -0.33, -0.16],
                      "whistle": 0.01}}}
`peek` is the head's offset from the shell bone, in metres in the prone frame (-Y forward, +Z up). That variant is
look A, which the owner picked on 2026-10-22; "armsDown": 62 lowers her T-pose arms for a standing start.
Modes:
  default                       each variant at "ts" (default [1.0]) from "views" (front34, front, side, game)
  "frames": [[t, roll], ...]    one variant along a timeline, fixed camera, <out_dir>/<view>/NNNN.png per frame
  "report": true                the tucked bone positions on Unity's model root (x flipped, y up, z forward): the
                                numbers in ShellTuckPose.Bashie, whose hips sit 0.42 m further back so the shell is
                                over her position
"""
import bpy, sys, math, os, json
from mathutils import Vector, Matrix, Quaternion
argv = sys.argv[sys.argv.index("--") + 1:]
blend, out, spec = argv[0], argv[1], json.loads(argv[2])
bpy.ops.wm.open_mainfile(filepath=blend)
scene = bpy.context.scene
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
P = arm.pose.bones
mw = arm.matrix_world.copy()
pivot = bpy.data.objects.new('Pivot', None); scene.collection.objects.link(pivot)
arm.parent = pivot
arm.matrix_parent_inverse = Matrix.Identity(4)
arm.matrix_world = mw

def upd():
    bpy.context.view_layer.update()

def world_head(name):
    return arm.matrix_world @ P[name].head

def rot_bone_world(name, axis, deg):
    """Rotate a pose bone about its head around a world axis (armature X == world X here)."""
    pb = P[name]
    a = arm.matrix_world.inverted().to_3x3() @ Vector(axis)
    a.normalize()
    head = pb.head.copy()
    R = Matrix.Translation(head) @ Matrix.Rotation(math.radians(deg), 4, a) @ Matrix.Translation(-head)
    pb.matrix = R @ pb.matrix
    upd()

def scale_bone(name, s):
    P[name].scale = (s, s, s); upd()

def move_bone_world(name, d):
    """Offset a pose bone by a world-space vector (metres)."""
    pb = P[name]
    m = pb.matrix.copy()
    m.translation = m.translation + arm.matrix_world.inverted().to_3x3() @ Vector(d)
    pb.matrix = m; upd()

def reset():
    for pb in P:
        pb.matrix_basis = Matrix.Identity(4)
    pivot.rotation_euler = (0, 0, 0); pivot.location = (0, 0, 0)
    upd()

def lerp(a, b, t):
    return a + (b - a) * t

def lowest_z():
    zs = []
    dg = bpy.context.evaluated_depsgraph_get()
    for o in bpy.data.objects:
        if o.type == 'MESH' and not o.hide_render and o.name.startswith('Bashie'):
            e = o.evaluated_get(dg)
            me = e.to_mesh()
            zs += [(e.matrix_world @ v.co).z for v in list(me.vertices)[::7]]
            e.to_mesh_clear()
    return min(zs)

def bounds():
    dg = bpy.context.evaluated_depsgraph_get()
    pts = []
    for o in bpy.data.objects:
        if o.type == 'MESH' and not o.hide_render and o.name.startswith('Bashie'):
            e = o.evaluated_get(dg); me = e.to_mesh()
            pts += [e.matrix_world @ v.co for v in list(me.vertices)[::5]]
            e.to_mesh_clear()
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return lo, hi

HEAD = 'mixamorig:Head'
reset()
HEAD_REST = (arm.matrix_world @ P[HEAD].matrix).copy()      # upright, facing -Y: how the peeking head should face
SHELL_REST = arm.matrix_world @ P['Shell'].head

def pose(v, t):
    """v: variant dict; t: 0 (standing) .. 1 (fully tucked)."""
    reset()
    for side, sgn in (('Left', 1), ('Right', -1)):
        down = v.get('armsDown', 0)
        rot_bone_world(f'mixamorig:{side}Arm', (0, 1, 0), sgn * (down + (70 - down) * t))   # arms down along her sides
        rot_bone_world(f'mixamorig:{side}UpLeg', (1, 0, 0), -60 * t)       # knees up under her
        rot_bone_world(f'mixamorig:{side}Leg', (1, 0, 0), 90 * t)
        scale_bone(f'mixamorig:{side}Arm', lerp(1, v['arm'], t))
        scale_bone(f'mixamorig:{side}UpLeg', lerp(1, v['leg'], t))
    scale_bone('Shell', lerp(1, v['shell'], t))
    if 'shellFwd' in v:
        move_bone_world('Shell', (0, -v['shellFwd'] * t, v.get('shellUp', 0.0) * t))
    scale_bone('Whistle', lerp(1, v.get('whistle', 1), t))
    pivot.rotation_euler = (math.radians(v.get('pitch', 90) * t), math.radians(v.get('_roll', 0)), 0)
    upd()
    # The head: pinned upright in the shell's front opening (offsets in metres from the shell bone, prone frame:
    # -Y forward, +Z up), blended in with t.
    shell = arm.matrix_world @ P['Shell'].head
    peek = Vector(v['peek'])
    target = shell + peek
    animated = (arm.matrix_world @ P[HEAD].matrix).copy()
    loc = animated.to_translation().lerp(target, t)
    rot = animated.to_quaternion().slerp(HEAD_REST.to_quaternion(), min(1.0, max(0.0, t)))
    s = lerp(1, v['headScale'], t) * animated.to_scale()[0]
    W = Matrix.LocRotScale(loc, rot, Vector((s, s, s)))
    P[HEAD].matrix = arm.matrix_world.inverted() @ W
    upd()
    pivot.location = (0, 0, -lowest_z()); upd()

scene.render.engine = 'BLENDER_EEVEE'
scene.render.resolution_x = scene.render.resolution_y = 480
if scene.world is None:
    scene.world = bpy.data.worlds.new('w')
scene.world.use_nodes = True
scene.world.node_tree.nodes['Background'].inputs[0].default_value = (0.72, 0.8, 0.86, 1)
scene.world.node_tree.nodes['Background'].inputs[1].default_value = 1.0
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); scene.collection.objects.link(cam); scene.camera = cam
cam.data.type = 'ORTHO'
sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); scene.collection.objects.link(sun)
sun.rotation_euler = (math.radians(45), 0, math.radians(-35)); sun.data.energy = 3.2
# floor
bpy.ops.mesh.primitive_plane_add(size=6, location=(0, 0, 0))
floor = bpy.context.active_object
mat = bpy.data.materials.new('floor'); mat.use_nodes = True
mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.55, 0.75, 0.85, 1)
floor.data.materials.append(mat)

VIEWS = {  # direction from the target to the camera; her front is -Y
    'front34': (-0.75, -1.0, 0.45),
    'side': (1.0, 0.0, 0.12),
    'game': (0.25, 1.0, 0.65),      # behind and above, like the match camera
    'front': (0.0, -1.0, 0.25),
}
if 'report' in spec:
    (name, v), = spec['variants'].items()
    pose(v, 1.0)
    def wpos(n):
        return arm.matrix_world @ P[n].head
    for n in ['mixamorig:Hips', 'mixamorig:Spine1', 'Shell', 'mixamorig:Head', 'mixamorig:LeftUpLeg', 'mixamorig:LeftArm']:
        w = wpos(n)
        print('REPORT', n, 'blender', tuple(round(c, 4) for c in w), 'unity_rootlocal', (round(-w.x, 4), round(w.z, 4), round(-w.y, 4)))
    lo, hi = bounds()
    print('REPORT bounds', tuple(round(c, 3) for c in lo), tuple(round(c, 3) for c in hi))
    pose(v, 0.0)
    for n in ['mixamorig:Hips', 'Shell', 'mixamorig:Head']:
        w = wpos(n)
        print('REPORT rest', n, 'unity_rootlocal', (round(-w.x, 4), round(w.z, 4), round(-w.y, 4)))
    raise SystemExit(0)
if 'frames' in spec:
    (name, v), = spec['variants'].items()
    pose(v, 0.0); lo0, hi0 = bounds()
    pose(v, 1.0); lo1, hi1 = bounds()
    c = Vector(((min(lo0.x, lo1.x) + max(hi0.x, hi1.x)) / 2, (min(lo0.y, lo1.y) + max(hi0.y, hi1.y)) / 2, 0.42))
    for i, (t, roll) in enumerate(spec['frames']):
        v['_roll'] = roll
        pose(v, t)
        for view in spec.get('views', ['front34']):
            d = Vector(VIEWS[view]).normalized()
            cam.location = c + d * 6
            cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
            cam.data.ortho_scale = spec.get('ortho', 1.5)
            scene.render.filepath = os.path.join(out, view, f'{i:04d}.png')
            bpy.ops.render.render(write_still=True)
    print('done motion', len(spec['frames']))
    raise SystemExit(0)
for name, v in spec['variants'].items():
    for t in spec.get('ts', [1.0]):
        pose(v, t)
        lo, hi = bounds()
        c = (lo + hi) / 2
        for view in spec.get('views', list(VIEWS)):
            d = Vector(VIEWS[view]).normalized()
            cam.location = c + d * 6
            cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
            cam.data.ortho_scale = spec.get('ortho', 1.25)
            scene.render.filepath = os.path.join(out, f'{name}-t{int(t * 100):03d}-{view}.png')
            bpy.ops.render.render(write_still=True)
        print('done', name, t, 'bounds', tuple(round(x, 2) for x in lo), tuple(round(x, 2) for x in hi))
