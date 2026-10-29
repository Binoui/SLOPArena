"""Independent FBX re-import, skin/scale/media checks and texture dimensions."""
import bpy, json, sys
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
assert len(arm.data.bones)==67
assert all(abs(s-1)<1e-5 for s in arm.scale)
assert len([b for b in arm.data.bones if b.name.startswith('mixamorig:')])==65
report={'armatures':1,'bones':67,'armature_scale':list(arm.scale),'meshes':{}}
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
  assert all(names[g.group]=='mixamorig:Hips' for v in mesh.data.vertices for g in v.groups if g.weight>1e-6)
 if mesh.name in ['Nyx_Backpack','Nyx_PoleHeld']:
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
report['embedded_media_bytes']=0
for path in (out/'textures').glob('*.png'):
 image=bpy.data.images.load(str(path),check_existing=False)
 assert list(image.size)==[2048,2048]
report['success']=True
(out/'verification.json').write_text(json.dumps(report,indent=2))
print('NYX_FBX_VERIFIED',json.dumps(report))
