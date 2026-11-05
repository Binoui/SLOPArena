"""Pose the imported salamander FBX armature with a GenRA clip USD (shared by the flip poncho build and review).

Each bone takes the USD skeleton's world-space change from rest, the transfer clip_review.py uses: the FBX and USD rests
share joint positions, not bone axes.
"""
import bpy
from mathutils import Matrix

P = "mixamorig:"


def import_fbx(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path, ignore_leaf_bones=False)
    return next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")


def load_clip(path):
    """Import a clip USD; returns its armature and every other new object (meshes are deleted)."""
    before = set(bpy.data.objects)
    bpy.ops.wm.usd_import(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    ua = next(o for o in new if o.type == "ARMATURE")
    for o in new:
        if o.type == "MESH":
            bpy.data.objects.remove(o)
    rest = [o for o in bpy.data.objects if o in new and o != ua]
    ua.hide_render = True  # hiding it in the viewport too would stop its animation evaluating
    return ua, rest


def deltas(fa, ua, frame):
    bpy.context.scene.frame_set(frame)
    Aw = ua.matrix_world
    return {pb.name.replace("mixamorig_", P): Aw @ pb.matrix @ pb.bone.matrix_local.inverted() @ Aw.inverted()
            for pb in ua.pose.bones if pb.name.replace("mixamorig_", P) in fa.data.bones}


def apply(fa, D):
    """Pose fa from world-space deltas D (bone name -> matrix); returns each bone's armature-space pose matrix."""
    Af = fa.matrix_world
    M = {}
    for b in fa.data.bones:  # parents precede children
        R = b.matrix_local
        if b.name not in D:
            M[b.name] = (M[b.parent.name] @ b.parent.matrix_local.inverted() @ R) if b.parent else R.copy()
            fa.pose.bones[b.name].matrix_basis = Matrix()
            continue
        M[b.name] = Af.inverted() @ D[b.name] @ Af @ R
        rel = (b.parent.matrix_local.inverted() @ R) if b.parent else R
        par = M[b.parent.name] if b.parent else Matrix()
        fa.pose.bones[b.name].matrix_basis = rel.inverted() @ par.inverted() @ M[b.name]
    bpy.context.view_layer.update()
    return M


def rest(fa):
    for pb in fa.pose.bones:
        pb.matrix_basis = Matrix()
    bpy.context.view_layer.update()


def pose_clip(fa, usd, frame):
    """Pose fa at one clip frame and drop the clip; returns the bone pose matrices."""
    ua, extra = load_clip(usd)
    M = apply(fa, deltas(fa, ua, frame))
    for o in [ua] + extra:
        bpy.data.objects.remove(o)
    bpy.context.view_layer.update()
    return M
