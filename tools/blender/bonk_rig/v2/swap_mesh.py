"""Swap a corrected rest shape into the Mixamo-rigged FBX, keeping Mixamo's skeleton and weights.

blender -b --factory-startup --python-exit-code 1 --python swap_mesh.py -- <rigged.fbx> <sent_apose.blend> <new_apose.blend> <out_dir>
The upload was body + boot_a + boot_b joined in that order, so vertices match by index. The sent shape checks the
match (it must equal the rigged mesh); the new shape replaces it.
"""
import bpy, sys, os, json
import numpy as np
from mathutils import Matrix

rig_fbx, sent, new, out = (os.path.abspath(a) for a in sys.argv[sys.argv.index("--") + 1:][:4])
os.makedirs(out, exist_ok=True)


def joined(path):
    with bpy.data.libraries.load(path) as (src, dst):
        dst.objects = [n for n in src.objects if n in ("body", "boot_a", "boot_b")]
    obs = {o.name: o for o in dst.objects}
    P = np.vstack([np.array([(obs[n].matrix_world @ v.co)[:] for v in obs[n].data.vertices]) for n in ("body", "boot_a", "boot_b")])
    for o in dst.objects: bpy.data.objects.remove(o)
    return P


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=rig_fbx)
arm = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
me = next(o for o in bpy.context.scene.objects if o.type == "MESH")
if arm.animation_data: arm.animation_data.action = None
for b in arm.pose.bones: b.rotation_quaternion = (1, 0, 0, 0); b.location = (0, 0, 0)
Wr = np.array([(me.matrix_world @ v.co)[:] for v in me.data.vertices])
A, Bn = joined(sent), joined(new)
err = np.linalg.norm(Wr - A, axis=1)
print("MATCH max", err.max().round(6), "mean", err.mean().round(6), "n", len(Wr), len(A), len(Bn))
assert len(Wr) == len(A) == len(Bn) and err.max() < 1e-3, "vertex order does not match the upload"
inv = me.matrix_world.inverted()
for v, p in zip(me.data.vertices, Bn):
    v.co = inv @ __import__("mathutils").Vector(p)
me.data.update()
moved = np.linalg.norm(Bn - A, axis=1)
print("MOVED verts >1mm", int((moved > 1e-3).sum()), "max", moved.max().round(4))
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out, "sir-bonk-v2-rigged.blend"))
bpy.ops.object.select_all(action="SELECT")
bpy.ops.export_scene.fbx(filepath=os.path.join(out, "sir-bonk-v2-rigged.fbx"), use_selection=True, path_mode="COPY",
                         embed_textures=True, add_leaf_bones=False, bake_anim=False)
print("DONE", out)
