"""Compare two packed rig builds. Run with Blender and -- <baseline> <candidate>."""
import bpy
import json
import sys
from pathlib import Path
import numpy as np


def snapshot(directory):
    bpy.ops.wm.open_mainfile(filepath=str(directory/'Nyx_rig.blend'))
    arm = bpy.data.objects['Armature']
    data = {'bones': {b.name: np.array(b.matrix_local) for b in arm.data.bones},
            'parents': {b.name: b.parent.name if b.parent else None for b in arm.data.bones}, 'meshes': {}}
    for name in ['Nyx_Fake', 'Nyx_Backpack', 'Nyx_PoleHeld', 'Nyx_Real', 'Nyx_Real_Tights']:
        ob = bpy.data.objects[name]
        data['meshes'][name] = {
            'coordinates': [list(v.co) for v in ob.data.vertices],
            'faces': [list(p.vertices) for p in ob.data.polygons],
            'weights': [[[ob.vertex_groups[g.group].name, g.weight] for g in v.groups] for v in ob.data.vertices],
            'uv': [list(d.uv) for d in ob.data.uv_layers.active.data],
        }
    return data


baseline, candidate = [Path(p).resolve() for p in sys.argv[sys.argv.index('--')+1:]]
before, after = snapshot(baseline), snapshot(candidate)
assert all(after['parents'].get(n) == parent for n,parent in before['parents'].items()), 'Existing skeleton hierarchy changed'
assert all(n.startswith('Nyx_Skirt_') for n in after['parents'].keys()-before['parents'].keys())
errors = {name: float(np.max(np.abs(matrix-after['bones'][name]))) for name,matrix in before['bones'].items()}
assert max(errors.values()) == 0, errors
assert before['meshes'] == after['meshes'], 'Retained body, tights, fake form or props changed'
report = {'bone_count': len(errors), 'max_bone_matrix_difference': max(errors.values()),
          'added_bones': len(after['bones'])-len(before['bones']), 'retained_meshes_exactly_equal': True, 'baseline': str(baseline), 'candidate': str(candidate)}
(candidate/'preservation.json').write_text(json.dumps(report,indent=2))
print('NYX_PRESERVATION_OK', json.dumps(report))
