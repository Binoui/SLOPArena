"""Independent FBX re-import, skin/scale/media checks and texture dimensions."""
import bpy, bmesh, json, sys
from pathlib import Path
import numpy as np
sys.path.insert(0,str(Path(__file__).resolve().parent))
from common import islands
out=Path(sys.argv[sys.argv.index('--')+1]).resolve()
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(out/'Nyx.fbx'), use_image_search=False)
arms=[o for o in bpy.context.scene.objects if o.type=='ARMATURE']
assert len(arms)==1
arm=arms[0]
assert len(arm.data.bones)==116
assert all(abs(s-1)<1e-5 for s in arm.scale)
assert len([b for b in arm.data.bones if b.name.startswith('mixamorig:')])==65
report={'armatures':1,'bones':116,'armature_scale':list(arm.scale),'meshes':{}}
for mesh in [o for o in bpy.context.scene.objects if o.type=='MESH']:
 names={g.index:g.name for g in mesh.vertex_groups}
 count=0;err=0
 for v in mesh.data.vertices:
  w={names[g.group]:g.weight for g in v.groups if g.weight>1e-6}
  assert w and len(w)<=4
  assert all(n in arm.data.bones for n in w)
  err=max(err,abs(sum(w.values())-1));count=max(count,len(w))
 assert err<1e-5
 if mesh.name=='Nyx_Real_Dress':
  assert len(mesh.data.vertices) > 5000
  bm = bmesh.new(); bm.from_mesh(mesh.data)
  assert all(f.calc_area() > 1e-10 for f in bm.faces)
  assert not any(len(e.link_faces)>2 for e in bm.edges)
  bm.free()
  for v in mesh.data.vertices:
   for g in v.groups:
    if g.weight <= 1e-6: continue
    name = names[g.group]
    assert name == 'mixamorig:Hips' or name.startswith('Nyx_Skirt_') or (
        v.co.z > .96 and name in {'mixamorig:Spine', 'mixamorig:Spine1', 'mixamorig:Spine2'}), (name, tuple(v.co))
 if mesh.name in ['Nyx_Backpack','Nyx_BackpackPole','Nyx_PoleHeld','Nyx_Phone']:
  assert all(len(v.groups)==1 for v in mesh.data.vertices)
 coords=np.array([list(mesh.matrix_world@v.co) for v in mesh.data.vertices]);assert np.isfinite(coords).all()
 report['meshes'][mesh.name]={'vertices':len(mesh.data.vertices),'max_influences':count,'weight_sum_max_error':err,
                             'bounds':[coords.min(0).tolist(),coords.max(0).tolist()]}
for side in ['Left','Right']:
 a=arm.matrix_world@arm.data.bones['mixamorig:'+side+'Arm'].head_local
 b=arm.matrix_world@arm.data.bones['mixamorig:'+side+'ForeArm'].head_local
 assert abs((b-a).normalized().z)<.03
# FBX binary media Content values must all be empty.
from io_scene_fbx.parse_fbx import parse
root,version=parse(str(out/'Nyx.fbx'))
def walk(e):
 yield e
 for child in e.elems:yield from walk(child)
assert not any(e.id==b'Content' and any(isinstance(p,bytes) and len(p)>0 for p in e.props) for e in walk(root))
assert 'Nyx_Real_Dress' in report['meshes'] and 'Nyx_Real_Tights' in report['meshes']
report['embedded_media_bytes']=0
for path in (out/'textures').glob('*.png'):
 image=bpy.data.images.load(str(path),check_existing=False)
 assert list(image.size)==({'Nyx_Dress.png':[1024,1024], 'Nyx_Tights.png':[256,256], 'Nyx_Phone.png':[512,512]}.get(path.name, [2048,2048]))
for c in range(12):
 for j in range(4):
  b = arm.data.bones[f'Nyx_Skirt_{c:02d}_{j:02d}']
  assert b.parent.name == ('mixamorig:Hips' if j == 0 else f'Nyx_Skirt_{c:02d}_{j-1:02d}')
assert arm.data.bones['Nyx_PhoneBone'].parent.name == 'mixamorig:RightHand'
assert not arm.data.bones['Nyx_PhoneBone'].children
assert all(n in report['meshes'] for n in ['Nyx_BackpackPole', 'Nyx_Phone'])
report['success']=True
(out/'verification.json').write_text(json.dumps(report,indent=2))
print('NYX_FBX_VERIFIED',json.dumps(report))
