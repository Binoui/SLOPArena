"""Prove the waist correction preserves every bone and unrelated surface."""
import json
import sys
from pathlib import Path
import bpy
import bmesh
import numpy as np
from mathutils import Vector
from mathutils.kdtree import KDTree


def snapshot(path):
    bpy.ops.wm.open_mainfile(filepath=str(path/'Nyx_rig.blend'))
    arm = bpy.data.objects['Armature']
    bones = {b.name: (b.parent.name if b.parent else None, np.array(b.matrix_local)) for b in arm.data.bones}
    meshes = {}
    for ob in bpy.data.objects:
        if ob.type != 'MESH':
            continue
        bm = bmesh.new()
        bm.from_mesh(ob.data)
        health = {'zero_area': sum(f.calc_area() < 1e-10 for f in bm.faces),
                  'nonmanifold': sum(len(e.link_faces) > 2 for e in bm.edges),
                  'opposite_winding': sum(e.is_manifold and not e.is_contiguous for e in bm.edges),
                  'loose': sum(not v.link_faces for v in bm.verts)}
        bm.free()
        meshes[ob.name] = {
            'points': np.array([list(v.co) for v in ob.data.vertices]),
            'weights': [sorted((ob.vertex_groups[g.group].name, g.weight) for g in v.groups) for v in ob.data.vertices],
            'faces': [tuple(p.vertices) for p in ob.data.polygons],
            'uvs': [tuple(uv.uv) for uv in ob.data.uv_layers.active.data],
            'health': health,
        }
    return bones, meshes


before, after = [Path(p).resolve() for p in sys.argv[sys.argv.index('--')+1:]]
bb, bm = snapshot(before)
ab, am = snapshot(after)
assert bb.keys() == ab.keys()
for name in bb:
    assert bb[name][0] == ab[name][0] and np.array_equal(bb[name][1], ab[name][1]), name
assert bm.keys() == am.keys()
unchanged = [n for n in bm if n not in {'Nyx_Real_Tights', 'Nyx_Real_Dress'}]
for name in unchanged:
    assert np.array_equal(bm[name]['points'], am[name]['points']), name
    for field in ['weights', 'faces', 'uvs', 'health']:
        assert bm[name][field] == am[name][field], (name, field)
old, new = bm['Nyx_Real_Dress'], am['Nyx_Real_Dress']
for field in ['weights', 'faces', 'uvs', 'health']:
    assert old[field] == new[field], ('dress', field)
assert np.array_equal(old['points'][:, 0], new['points'][:, 0])
lower = old['points'][:, 2] <= 1.00
assert np.array_equal(old['points'][lower], new['points'][lower])
assert np.all(new['points'][:, 2] >= old['points'][:, 2])
assert np.max(new['points'][:, 2] - old['points'][:, 2]) <= .014001
old, new = bm['Nyx_Real_Tights'], am['Nyx_Real_Tights']
assert new['points'][:, 2].max() <= .84
# Recover source vertices from their undeformed position, then compare actual retained skin weights and UVs.
tree = KDTree(len(old['points']))
for i, p in enumerate(old['points']):
    tree.insert(Vector(p), i)
tree.balance()
indices = []
for p in new['points']:
    t = min(1, max(0, (p[2]-.70)/.14))
    scale = 1-.28*t*t*(3-2*t)
    original = Vector((p[0]/scale, -.01+(p[1]+.01)/scale, p[2]))
    _, index, distance = tree.find(original)
    assert distance < 1e-7
    indices.append(index)
assert len(set(indices)) == len(indices)
assert new['weights'] == [old['weights'][i] for i in indices]
retained = set(indices)
faces = [f for f in old['faces'] if set(f) <= retained]
assert [tuple(indices[i] for i in f) for f in new['faces']] == faces
old_uvs = []
offset = 0
for f in old['faces']:
    if set(f) <= retained:
        old_uvs.extend(old['uvs'][offset:offset+len(f)])
    offset += len(f)
assert old_uvs == new['uvs']
assert new['health']['zero_area'] <= old['health']['zero_area']
assert new['health']['nonmanifold'] <= old['health']['nonmanifold']
report = {'success': True, 'baseline': str(before), 'bones_exact': len(bb), 'unchanged_surfaces': unchanged,
          'dress': {'topology_uvs_weights_exact': True, 'unchanged_below_z': 1.0, 'waist_raise_max_m': .014, 'waist_y_scale': 1.33, 'waist_y_offset_m': .12},
          'tights': {'before': len(old['points']), 'after': len(new['points']), 'surviving_weights_uvs_exact': True,
                     'top_z': float(new['points'][:, 2].max()), 'health_before': old['health'], 'health_after': new['health']}}
(after/'waist-preservation.json').write_text(json.dumps(report, indent=2))
print('NYX_WAIST_PRESERVED', json.dumps(report))
