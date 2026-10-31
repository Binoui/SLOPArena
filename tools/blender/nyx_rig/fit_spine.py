"""Fit Nyx's real anatomy and waistband to her immutable shared skeleton.

Run with Blender: -- <waist-candidate-directory> <output-directory>.
This postprocess preserves every bone and all fake/prop surfaces exactly.
"""
import bpy
import json
import sys
import shutil
from pathlib import Path
import numpy as np
from mathutils import Vector
sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import islands, select

P = 'mixamorig:'
AXIAL = ['Hips', 'Spine', 'Spine1', 'Spine2', 'Neck', 'Head']
ROOT = Path(__file__).resolve().parents[3]
source, out = [Path(p).resolve() for p in sys.argv[sys.argv.index('--')+1:]]
out.mkdir(parents=True, exist_ok=True)
assert source != out
bpy.ops.wm.open_mainfile(filepath=str(source/'Nyx_rig.blend'))
arm = bpy.data.objects['Armature']
body = bpy.data.objects['Nyx_Real']
dress = bpy.data.objects['Nyx_Real_Dress']
joints = {r['bone']: r for r in json.loads((source/'Nyx_report.json').read_text())['joint_displacement_metres']}
# Canonical T-rest adds the same root frame offset to the axial fake joints.
frame = np.mean([np.array(arm.data.bones[P+n].head_local)-joints[P+n]['fake_target']
                 for n in AXIAL[1:]], axis=0)
source_z = np.array([joints[P+n]['real_source'][2]+frame[2] for n in AXIAL])
deltas = np.array([np.array(joints[P+n]['fake_target'])-joints[P+n]['real_source'] for n in AXIAL])

def smooth(v):
    t = np.clip(v, 0, 1)
    return t*t*(3-2*t)

def offset(z):
    if z <= source_z[0]:
        return deltas[0]
    if z >= source_z[-1]:
        return deltas[-1]
    j = int(np.searchsorted(source_z, z)-1)
    t = smooth((z-source_z[j])/(source_z[j+1]-source_z[j]))
    return deltas[j]*(1-t)+deltas[j+1]*t

# Skin ownership spans each segment, with continuous first derivatives at the joins.
centres = np.array([.968, 1.068, 1.184, 1.316, 1.415, 1.478])

def axial_weights(z):
    if z <= centres[0]:
        return {P+AXIAL[0]: 1.0}
    if z >= centres[-1]:
        return {P+AXIAL[-1]: 1.0}
    j = int(np.searchsorted(centres, z)-1)
    t = float(smooth((z-centres[j])/(centres[j+1]-centres[j])))
    return {P+AXIAL[j]: 1-t, P+AXIAL[j+1]: t}

def read_weights(ob):
    names = {g.index:g.name for g in ob.vertex_groups}
    result = []
    for v in ob.data.vertices:
        w = {names[g.group]:g.weight for g in v.groups if g.weight>1e-7}
        total = sum(w.values())
        assert total > 0
        result.append({n:value/total for n,value in w.items()})
    return result

def write_weights(ob, weights):
    ob.vertex_groups.clear()
    groups = {n:ob.vertex_groups.new(name=n) for n in sorted({n for w in weights for n in w})}
    for i,w in enumerate(weights):
        ordered = sorted(((n,v) for n,v in w.items() if v>1e-6), key=lambda p:-p[1])[:4]
        total = sum(v for n,v in ordered)
        assert total > 0
        for n,v in ordered:
            groups[n].add([i], v/total, 'REPLACE')

weights = read_weights(body)
old = np.array([list(v.co) for v in body.data.vertices])
new = old.copy()
changed_weights = set()
processed = set()
for fs, vs in islands(body):
    centre = old[vs].mean(axis=0)
    group_names = {n for i in vs for n in weights[i]}
    rigid_head = group_names == {P+'Head'}
    rigid_axial = len(group_names) == 1 and next(iter(group_names)) in {P+n for n in AXIAL}
    for vi in vs:
        if vi in processed:
            continue
        processed.add(vi)
        p = old[vi]
        w = weights[vi]
        is_head = rigid_head or w.get(P+'Head', 0) > .99999
        axial = sum(v for n,v in w.items() if n in {P+n for n in AXIAL})
        if axial < 1e-6:
            continue
        # Translate the entire face/hair as one piece, including hair below the neck.
        d = deltas[-1] if is_head or p[2]>=source_z[-1] else offset(centre[2] if rigid_axial else p[2])
        new[vi] += d*axial
        if is_head:
            continue
        if p[2] >= source_z[-1] and w.get(P+'Head', 0) > .5:
            weights[vi] = {P+'Head': 1.0}
            continue
        z = new[vi,2] if not rigid_axial else float(centre[2]+offset(centre[2])[2])
        if .99 < z < 1.52:
            replacement = {n:v for n,v in w.items() if n not in {P+k for k in AXIAL}}
            for n,v in axial_weights(z).items():
                replacement[n] = replacement.get(n,0)+axial*v
            if replacement != w:
                weights[vi] = replacement
                changed_weights.add(vi)
for v,p in zip(body.data.vertices,new):
    v.co = p
assert np.linalg.norm(new-old, axis=1).max() < .057
write_weights(body, weights)
body.data.update()

# The lower-body rebuild reset the skirt to raw Tripo coordinates, omitting the
# canonical root offset already applied to the bodice. Undo the cap workaround
# and put the whole approved surface in that same frame before anatomy fitting.
reference = ROOT/'tmp/nyx-model/spring-dress-v3/Nyx_rig.blend'
with bpy.data.libraries.load(str(reference),link=False) as (available,loaded):
    loaded.meshes = ['Nyx_Real_Dress']
raw_mesh = loaded.meshes[0]
assert len(raw_mesh.vertices) == len(dress.data.vertices)
assert [tuple(f.vertices) for f in raw_mesh.polygons] == [tuple(f.vertices) for f in dress.data.polygons]
raw = np.array([list(v.co) for v in raw_mesh.vertices])
points = raw+frame
# The frame's 8 cm backward Y belongs to the bodice join only. Applied to the
# whole bell it left 15 cm of empty fabric behind the hips (a bustle) and 5 cm
# in front. Lean the bell forward linearly from the waist to the hem so it
# returns over the legs; a short fade bends the straight A-line into an S.
points[:,1] -= frame[1]*np.clip((1.0-points[:,2])/.7, 0, 1)
for i,p in enumerate(points):
    # Keep the original bell and folds; fade the small vertical hip correction
    # toward the hem so its established height stays close to the real boots.
    d = offset(p[2]).copy()
    d[2] *= smooth((p[2]-.60)/.35)
    points[i] += d
for v,p in zip(dress.data.vertices,points):
    v.co = p
bpy.data.meshes.remove(raw_mesh)
dw = read_weights(dress)
waist_vertices = 0
for i,p in enumerate(points):
    t = float(smooth((p[2]-.97)/.105))
    if t <= 0:
        continue
    # Share the bodice's skin field at the join, fading into the unchanged chains.
    chain = {n:w*(1-t) for n,w in dw[i].items() if n.startswith('Nyx_Skirt_')}
    for n,w in axial_weights(p[2]).items():
        chain[n] = chain.get(n,0)+t*w
    dw[i] = chain
    waist_vertices += 1
write_weights(dress,dw)
dress.data.update()

shutil.copytree(source/'textures',out/'textures',dirs_exist_ok=True)
for image in bpy.data.images:
    if image.filepath:
        name = Path(image.filepath).name
        if (out/'textures'/name).exists():
            image.filepath = str(out/'textures'/name)
# Leave tights at the final waist candidate's already trimmed/tapered geometry.
# No upper fake hips remain to drive the silhouette; Blender has no cloth solver.
meshes = [o for o in bpy.context.scene.objects if o.type=='MESH']
for b in arm.pose.bones:
    b.matrix_basis.identity()
bpy.context.view_layer.update()
select([arm]+meshes)
bpy.ops.export_scene.fbx(filepath=str(out/'Nyx.fbx'), use_selection=True, object_types={'ARMATURE','MESH'},
    add_leaf_bones=False,bake_anim=False,path_mode='STRIP',embed_textures=False,mesh_smooth_type='OFF',
    use_armature_deform_only=False,armature_nodetype='NULL')
for o in meshes:
    hide = not o.name.startswith('Nyx_Real')
    o.hide_render = hide
    o.hide_set(hide)
bpy.ops.wm.save_as_mainfile(filepath=str(out/'Nyx_rig.blend'))
report = {'source':str(source),'canonical_frame_offset_m':frame.tolist(),
          'axial_joints': [{'bone':P+n,'source_z_in_canonical_frame':float(z),'mesh_delta_m':d.tolist()}
                           for n,z,d in zip(AXIAL,source_z,deltas)],
          'skin_centres_z':centres.tolist(),'body_changed_vertices':int(np.sum(np.linalg.norm(new-old,axis=1)>1e-7)),
          'body_max_displacement_m':float(np.linalg.norm(new-old,axis=1).max()),
          'body_reweighted_vertices':len(changed_weights),'dress_source':str(reference),
          'dress_waist_reweighted_vertices':waist_vertices,'tights':'final waist candidate retained exactly',
          'renderers':[o.name for o in meshes]}
(out/'fit-report.json').write_text(json.dumps(report,indent=2))
print('NYX_SPINE_FIT_COMPLETE',json.dumps(report))
