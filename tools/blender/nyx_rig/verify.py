"""Isolate and re-import FBX files; assert geometry, UVs, embedded 4K textures; render upload evidence.

Run in a NEW Blender process, after prepare.py, with -- <out>.
"""
import sys,json,hashlib,shutil
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
from common import *
checks_only='--checks-only' in sys.argv
out=Path(sys.argv[sys.argv.index('--')+1]).resolve();report=json.loads((out/'measurements.json').read_text())
verification={'blender':bpy.app.version_string,'fresh_process':True,'files':{}}
render_dir=out/'verification';render_dir.mkdir(exist_ok=True)
for key,filename in [('real','nyx-real-mixamo.fbx'),('fake','nyx-fake-mixamo.fbx'),('backpack','nyx-fake-backpack.fbx')]:
    isolated=render_dir/'isolated'/key;isolated.mkdir(parents=True,exist_ok=True)
    fbx=isolated/filename;shutil.copy2(out/filename,fbx)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(fbx),use_image_search=False)
    obs=list(bpy.context.scene.objects)
    assert len(obs)==1 and obs[0].type=='MESH', [(o.name,o.type) for o in obs]
    ob=obs[0];bpy.context.view_layer.update()
    v=np.array([(ob.matrix_world@p.co)[:] for p in ob.data.vertices])
    expected=np.load(out/(key+'-expected.npz'))
    assert v.shape==expected['vertices'].shape
    error=float(np.abs(v-expected['vertices']).max());assert error<1e-5,error
    assert np.isfinite(v).all()
    uv=np.empty(len(ob.data.uv_layers.active.data)*2,dtype=np.float64);ob.data.uv_layers.active.data.foreach_get('uv',uv)
    uv_error=float(np.abs(uv-expected['uv']).max());assert uv_error<1e-6,uv_error
    assert len(ob.data.materials)==1
    mat=ob.data.materials[0];bs=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    link=bs.inputs['Base Color'].links;assert len(link)==1 and link[0].from_node.type=='TEX_IMAGE'
    img=link[0].from_node.image
    assert list(img.size)==[4096,4096]
    texture=Path(bpy.path.abspath(img.filepath)).resolve()
    assert texture.is_relative_to(isolated), ('External dependency leaked into embedded-media test',str(texture))
    form='fake' if key=='backpack' else key
    media=bytes(img.packed_file.data) if img.packed_file else texture.read_bytes()
    digest=hashlib.sha256(media).hexdigest()
    assert digest==report['forms'][form]['texture_sha256']
    pix=np.empty(4096*4096*4,dtype=np.float32);img.pixels.foreach_get(pix)
    assert float(pix.reshape(-1,4)[:,:3].std())>.03,'Blank texture'
    bm=bmesh.new();bm.from_mesh(ob.data)
    stats={'one_mesh_only':True,'vertices':len(v),'faces':len(ob.data.polygons),'triangles':sum(len(p.vertices)-2 for p in ob.data.polygons),
       'max_world_coordinate_error_m':error,'max_uv_error':uv_error,'finite_geometry':True,'bounds_m':[v.min(0).tolist(),v.max(0).tolist()],
       'texture_loaded_from_embedded_media':True,'texture_storage':'packed' if img.packed_file else 'extracted','texture_sha256_matches_4k_source':True,'texture_extracted_path':str(texture),
       'boundary_edges_including_source_seams':sum(e.is_boundary for e in bm.edges),'zero_area_faces':sum(f.calc_area()<1e-14 for f in bm.faces),
       'texture_nonconstant':True,'file_bytes':fbx.stat().st_size}
    bm.free();verification['files'][key]=stats
    expected_faces=report['prop_split']['output']['faces'] if key=='backpack' else report['forms'][key]['output']['faces']
    assert stats['faces']==expected_faces
    if key!='backpack':assert abs(v[:,2].min())<1e-6
    if not checks_only:
        setup_render(1200)
        for view,yaw in [('front',0),('back',180),('side',90)]:
            shoot(render_dir/(key+'-'+view+'.png'),(0,0,.95),2.1,yaw)
# Verify the prop-only .blend is a usable complete scene with packed texture.
bpy.ops.wm.open_mainfile(filepath=str(out/'nyx-fake-backpack.blend'))
obs=list(bpy.context.scene.objects);assert len(obs)==1 and obs[0].type=='MESH'
assert len(obs[0].data.polygons)==report['prop_split']['output']['faces']
verification['prop_blend_complete_scene']=True
# GLB structure: embedded buffers/images, one body mesh for REAL, body+prop for FAKE.
import struct
verification['glb']={}
for form,count in [('real',1),('fake',2)]:
    data=(out/('nyx-'+form+'-dressed.glb')).read_bytes();assert data[:4]==b'glTF'
    n,typ=struct.unpack_from('<II',data,12);assert typ==0x4e4f534a
    gltf=json.loads(data[20:20+n]);assert len(gltf['meshes'])==count
    assert all('uri' not in img for img in gltf['images'])
    assert all('uri' not in buf for buf in gltf['buffers'])
    verification['glb'][form]={'meshes':count,'self_contained':True}
# Audit source seams and degenerate polygons without silently changing Tripo's resolution.
verification['source_geometry']={}
for form in ['real','fake']:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    ob=load_mesh(report['forms'][form]['source_fbx'],'Source_'+form)
    cfg=report['forms'][form]['output'];s=cfg['uniform_scale_from_source'];origin=Vector(cfg['ground_origin_raw'])
    for v in ob.data.vertices:v.co=(v.co-origin)*s
    bm=bmesh.new();bm.from_mesh(ob.data)
    base={'vertices':len(ob.data.vertices),'faces':len(ob.data.polygons),'triangles':sum(len(f.vertices)-2 for f in ob.data.polygons),
      'boundary_edges_including_source_seams':sum(e.is_boundary for e in bm.edges),'zero_area_faces_at_target_scale':sum(f.calc_area()<1e-14 for f in bm.faces)}
    bm.free();verification['source_geometry'][form]=base
    output=[verification['files'][form]]+([verification['files']['backpack']] if form=='fake' else [])
    for metric in ['vertices','faces','triangles','boundary_edges_including_source_seams']:
        assert base[metric]==sum(o[metric] for o in output),(form,metric)
    assert base['zero_area_faces_at_target_scale']==sum(o['zero_area_faces'] for o in output),(form,'degenerate count')
verification['original_mesh_resolution_and_seams_preserved']=True
json.dump(verification,open(out/'verification.json','w'),indent=2)
print('PASS',json.dumps(verification),flush=True)
