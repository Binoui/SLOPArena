"""Independent candidate preservation, facial shape, mesh health and export checks."""
import bpy,bmesh,json,sys,hashlib
from pathlib import Path
import numpy as np
sys.path.insert(0,str(Path(__file__).resolve().parent))
from common import islands
before,after=[Path(p).resolve() for p in sys.argv[sys.argv.index('--')+1:]]

def snapshot(path):
 bpy.ops.wm.open_mainfile(filepath=str(path/'Nyx_rig.blend'));arm=bpy.data.objects['Armature'];meshes={}
 for o in bpy.context.scene.objects:
  if o.type!='MESH':continue
  points = np.array([list(v.co) for v in o.data.vertices])
  o.data.calc_loop_triangles()
  triangles = np.array([tuple(t.vertices) for t in o.data.loop_triangles])
  area = np.linalg.norm(np.cross(points[triangles[:,1]]-points[triangles[:,0]], points[triangles[:,2]]-points[triangles[:,0]]), axis=1)*.5
  face_area = np.bincount([t.polygon_index for t in o.data.loop_triangles], weights=area, minlength=len(o.data.polygons))
  bm=bmesh.new();bm.from_mesh(o.data)
  health={'zero_area':int(np.sum(face_area<1e-10)),'nonmanifold':sum(len(e.link_faces)>2 for e in bm.edges),'opposite_winding':sum(e.is_manifold and not e.is_contiguous for e in bm.edges),'loose':sum(not v.link_faces for v in bm.verts)};bm.free()
  weights=[sorted((o.vertex_groups[g.group].name,g.weight) for g in v.groups if g.weight>1e-6) for v in o.data.vertices]
  meshes[o.name]={'points':np.array([list(v.co) for v in o.data.vertices]),'faces':[tuple(f.vertices) for f in o.data.polygons],'uvs':np.array([tuple(u.uv) for u in o.data.uv_layers.active.data]),'weights':weights,'health':health,'matrix':np.array(o.matrix_world),'triangles':sum(len(f.vertices)-2 for f in o.data.polygons)}
 return {'bones':{b.name:(b.parent.name if b.parent else None,np.array(b.matrix_local),list(b.head_local),list(b.tail_local),b.use_deform,b.use_connect) for b in arm.data.bones},'arm_world':np.array(arm.matrix_world),'meshes':meshes}
old,new=snapshot(before),snapshot(after)
assert old['bones'].keys()==new['bones'].keys()
for n,b in old['bones'].items():
 a=new['bones'][n];assert b[0]==a[0] and np.array_equal(b[1],a[1]) and b[2:]==a[2:],n
assert np.array_equal(old['arm_world'],new['arm_world'])
assert old['meshes'].keys()==new['meshes'].keys()
unchanged=[]
for n,m in old['meshes'].items():
 a=new['meshes'][n]
 for k in ['faces','weights','health'] if n not in ['Nyx_Real','Nyx_Real_Dress'] else ['faces']:
  assert m[k]==a[k],(n,k)
 assert np.array_equal(m['uvs'],a['uvs']),(n,'uvs')
 assert np.array_equal(m['matrix'],a['matrix']),(n,'world matrix')
 if n not in ['Nyx_Real','Nyx_Real_Dress']:
  assert np.array_equal(m['points'],a['points']),n;unchanged.append(n)
 for k,v in a['health'].items():assert v<=m['health'][k],(n,k,v,m['health'][k])
 for w in a['weights']:
  assert 1<=len(w)<=4 and abs(sum(v for _,v in w)-1)<1e-5
m=old['meshes']['Nyx_Real'];a=new['meshes']['Nyx_Real'];p=m['points'];delta=a['points']-p
head=np.array([len(w)==1 and w[0][0]=='mixamorig:Head' for w in m['weights']])
expected=np.array(json.loads((after/'fit-report.json').read_text())['axial_joints'][-1]['mesh_delta_m'])
head_error=float(np.linalg.norm(delta[head]-expected,axis=1).max());assert head_error<2e-7,head_error
boots=p[:,2]<.5;assert np.array_equal(p[boots],a['points'][boots])
texture_hashes={}
for f in (after/'textures').glob('*.png'):
 h=hashlib.sha256(f.read_bytes()).hexdigest();assert h==hashlib.sha256((before/'textures'/f.name).read_bytes()).hexdigest();texture_hashes[f.name]=h
report={'success':True,'baseline':str(before),'bones_exact':len(old['bones']),'unrelated_meshes_exact':unchanged,'all_topology_uvs_object_transforms_exact':True,'rigid_head_vertices':int(head.sum()),'rigid_head_translation_max_error_m':head_error,'boots_exact_vertices':int(boots.sum()),'mesh_health':{n:{'before':m['health'],'after':new['meshes'][n]['health']} for n,m in old['meshes'].items()},'mesh_counts':{n:{'vertices':len(m['points']),'triangles':m['triangles']} for n,m in new['meshes'].items()},'texture_hashes':texture_hashes,'fbx_sha256':hashlib.sha256((after/'Nyx.fbx').read_bytes()).hexdigest()}
(after/'spine-preservation.json').write_text(json.dumps(report,indent=2));print('NYX_SPINE_PRESERVED',json.dumps(report))
