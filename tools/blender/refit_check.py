"""Prove that a re-exported rig FBX kept the original's rest pose and mesh.

blender -b --factory-startup --python-exit-code 1 --python refit_check.py -- <original.fbx> <refit.fbx> [moved=0] [skip=<prefix>]

Fails unless every bone the two files share has the same rest matrix (in world space, object transform included) and
every vertex of the refit lies on a vertex of the original. `moved=1` skips the vertex test for a refit that reshapes
the mesh on purpose (the Wibou coat taper), and `skip=Coat_` skips bones the refit moves on purpose. Prints bones added
and vertices removed.
"""
import bpy, sys, json
from mathutils.kdtree import KDTree

argv = sys.argv[sys.argv.index("--") + 1:]
extra = dict(a.split("=", 1) for a in argv[2:])
moved = extra.get("moved") == "1"
skip = extra.get("skip")


def load(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    mesh = next(o for o in bpy.data.objects if o.type == "MESH")
    bones = {b.name: (arm.matrix_world @ b.matrix_local, b.parent.name if b.parent else None) for b in arm.data.bones}
    verts = [mesh.matrix_world @ v.co for v in mesh.data.vertices]
    node = [list(r) for r in mesh.matrix_world], [list(r) for r in arm.matrix_world]
    return bones, verts, node, mesh.name, [m.name for m in mesh.data.materials]


a_bones, a_verts, a_node, a_name, a_mats = load(argv[0])
b_bones, b_verts, b_node, b_name, b_mats = load(argv[1])
size = max(max(abs(c) for c in v) for v in a_verts)
worst_bone, worst_name = 0.0, None
for n, (m, parent) in a_bones.items():
    assert n in b_bones, f"bone {n} is missing from the refit"
    m2, parent2 = b_bones[n]
    if skip and n.startswith(skip): continue
    assert parent == parent2, f"bone {n} changed parent: {parent} -> {parent2}"
    d = max(abs(m[i][j] - m2[i][j]) for i in range(4) for j in range(4))
    if d > worst_bone: worst_bone, worst_name = d, n
node_dev = max(abs(x - y) for p, q in zip(a_node, b_node) for r, s in zip(p, q) for x, y in zip(r, s))
worst_vert = 0.0
if not moved:
    kd = KDTree(len(a_verts))
    for i, v in enumerate(a_verts): kd.insert(v, i)
    kd.balance()
    worst_vert = max(kd.find(v)[2] for v in b_verts)
out = {"bones_shared": len(a_bones), "bones_added": sorted(set(b_bones) - set(a_bones)),
       "worst_bone_matrix_deviation": worst_bone, "worst_bone": worst_name, "node_transform_deviation": node_dev,
       "verts": [len(a_verts), len(b_verts)], "worst_vertex_distance": worst_vert, "model_size": size,
       "mesh_names": [a_name, b_name], "materials": [a_mats, b_mats]}
print("CHECK", json.dumps(out))
assert worst_bone < 1e-4 * max(1.0, size), f"rest pose moved: {worst_name} by {worst_bone}"
assert node_dev < 1e-5, "the mesh or armature node transform changed"
assert worst_vert < 1e-5 * max(1.0, size), f"a vertex moved by {worst_vert}"
assert a_name == b_name and a_mats == b_mats, "mesh or material names changed"
