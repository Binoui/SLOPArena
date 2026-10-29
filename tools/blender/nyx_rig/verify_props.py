"""Compare all v3 bones and retained surfaces, and prove the backpack split is lossless."""
import bpy
import json
import sys
from pathlib import Path
import numpy as np


def surface(ob):
    vertices = [(tuple(v.co), tuple((ob.vertex_groups[g.group].name, g.weight) for g in v.groups)) for v in ob.data.vertices]
    faces = []
    for p in ob.data.polygons:
        faces.append(tuple((tuple(ob.data.vertices[ob.data.loops[i].vertex_index].co), tuple(ob.data.uv_layers.active.data[i].uv)) for i in p.loop_indices))
    return sorted(vertices), sorted(faces)


def read(path):
    bpy.ops.wm.open_mainfile(filepath=str(path/'Nyx_rig.blend'))
    arm = bpy.data.objects['Armature']
    return {b.name: (b.parent.name if b.parent else None, np.array(b.matrix_local)) for b in arm.data.bones}, {o.name: surface(o) for o in bpy.data.objects if o.type == 'MESH'}


before_path, after_path = map(lambda p: Path(p).resolve(), sys.argv[sys.argv.index('--')+1:])
before_bones, before_meshes = read(before_path)
after_bones, after_meshes = read(after_path)
assert after_bones.keys() - before_bones.keys() == {'Nyx_PhoneBone'}
for name, (parent, matrix) in before_bones.items():
    assert after_bones[name][0] == parent, name
    assert np.array_equal(matrix, after_bones[name][1]), name
retained = [name for name in before_meshes if name != 'Nyx_Backpack']
assert all(before_meshes[n] == after_meshes[n] for n in retained), 'Existing body, spring dress, tights, fake skin or held pole changed'
for i in range(2):
    assert before_meshes['Nyx_Backpack'][i] == sorted(after_meshes['Nyx_Backpack'][i] + after_meshes['Nyx_BackpackPole'][i]), 'Split lost geometry, UVs or weights'
report = {'success': True, 'existing_bones_exact': len(before_bones), 'new_leaf_bones': ['Nyx_PhoneBone'],
          'retained_surfaces_exact': retained, 'backpack_split_lossless': True, 'baseline': str(before_path)}
(after_path/'props-preservation.json').write_text(json.dumps(report, indent=2))
print('NYX_PROPS_PRESERVED', json.dumps(report))
