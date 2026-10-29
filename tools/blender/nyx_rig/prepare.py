"""Prepare full-resolution Nyx Tripo v1 meshes, split rigid prop, measure and export.

blender -b --factory-startup --python-exit-code 1 --python prepare.py -- <tripo-v1> <out>
"""
import sys,json,hashlib,shutil
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
from common import *
args=sys.argv[sys.argv.index('--')+1:]
root=Path(args[0]).resolve();out=Path(args[1]).resolve();out.mkdir(parents=True,exist_ok=True)
config=json.loads(Path(__file__).with_name('landmarks.json').read_text())
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.unit_settings.system='METRIC';bpy.context.scene.unit_settings.scale_length=1
bpy.context.preferences.filepaths.save_version=0
report={'blender':bpy.app.version_string,'coordinates':'metres; X lateral, Z up, front -Y; origin on floor below estimated ankle midpoint',
 'measurement_method':'Uniform scale based on outer hair crown, excluding FAKE cat ears, headband and ring. Joint heights are visual anatomical estimates on calibrated front/side orthographic views, with hidden trouser/shorts meshes isolated for leg landmarks. Elbow, wrist, knee and ankle lateral coordinates are checked against ray-sampled cross-section midpoints. They are not fitted bones. Sole includes footwear. Heights and transverse spans in current A poses are pose dependent.',
 'forms':{},'rows_cm':[], 'sources_preserved':True}
objects={};raw={};groups={};sole={};crown={};source_vertices={}
for form in ['real','fake']:
    cfg=config[form];matches=list((root/form).glob('tripo_convert_*.fbx'));assert len(matches)==1
    path=matches[0];ob=load_mesh(path,'Nyx_'+form.title());raw[form]=ob
    v=vertices(ob);source_vertices[form]=v.copy();groups[form]=islands(ob)
    assert len(v)==cfg['expected_vertices'] and len(ob.data.polygons)==cfg['expected_faces'], 'New input: inspect and update landmarks/selection first'
    sole[form]=float(v[:,2].min());crown[form]=float(v[groups[form][cfg['crown_component']][1],2].max())
    tex=next(path.with_suffix('.fbm').glob('*.png'))
    texout=out/'textures'/('nyx-'+form+'-basecolor.png');texout.parent.mkdir(exist_ok=True);shutil.copy2(tex,texout)
    img=material(ob,texout,'Nyx_'+form.title()+'_Material');img.pack()
    report['forms'][form]={'source_fbx':str(path),'source_sha256':hashlib.sha256(path.read_bytes()).hexdigest(),
      'source_texture':str(tex),'texture_sha256':hashlib.sha256(tex.read_bytes()).hexdigest(),'texture_size':list(img.size),
      'input_vertices':len(v),'input_faces':len(ob.data.polygons),'input_triangles':sum(len(p.vertices)-2 for p in ob.data.polygons),
      'raw_sole':sole[form],'raw_crown':crown[form],'crown_component':cfg['crown_component']}
relative=(crown['real']-sole['real'])/(crown['fake']-sole['fake'])
scale=1.7/(crown['real']-sole['real']);scales={'real':scale,'fake':scale*relative}
report['fake_relative_body_scale']=relative
report['scale_formula']='real_world=raw_real*real_scale; fake_world=raw_fake*real_scale*fake_relative_body_scale; first subtract each ground origin'
# Face partition; retain UVs and all original polygons. No cuts, welds, decimation or filling.
fake=raw['fake'];fg=groups['fake'];ids=config['fake']['prop_components'];fv=vertices(fake)
assert set(ids)=={i for i,(_,vs) in enumerate(fg) if fv[vs,1].min()>.012}, 'Rear component selection changed'
propfaces=[f for i in ids for f in fg[i][0]];bodyfaces=[f for i,g in enumerate(fg) if i not in ids for f in g[0]]
assert not set(propfaces)&set(bodyfaces)
assert set(propfaces)|set(bodyfaces)==set(range(len(fake.data.polygons)))
body=subset(fake,bodyfaces,'Nyx_Fake');prop=subset(fake,propfaces,'Nyx_Fake_Backpack')
assert len(body.data.polygons)+len(prop.data.polygons)==len(fake.data.polygons)
objects={'real':raw['real'],'fake':body,'backpack':prop}
bpy.data.objects.remove(fake,do_unlink=True)
body.name=body.data.name='Nyx_Fake'
report['prop_split']={'method':'complete edge-connected face islands wholly behind Y=0.012 in raw v1 coordinates, visually verified front/back/side; torso shoulder straps retained',
 'component_ids':ids,'component_count':len(ids),'input_face_partition_complete':True,'new_cut_boundaries':0}
for key,ob in objects.items():
    form='fake' if key=='backpack' else key;cfg=config[form];s=scales[form]
    for vert in ob.data.vertices:vert.co=(vert.co-Vector((0,cfg['ground_origin_y'],sole[form])))*s
    ob.data.update();ob.location=(0,0,0);ob.rotation_euler=(0,0,0);ob.scale=(1,1,1)
    v=vertices(ob)
    stats={'vertices':len(v),'faces':len(ob.data.polygons),'triangles':sum(len(p.vertices)-2 for p in ob.data.polygons),
      'bounds_m':[v.min(0).tolist(),v.max(0).tolist()],'uniform_scale_from_source':s,'ground_origin_raw':[0,cfg['ground_origin_y'],sole[form]]}
    if key=='backpack':report['prop_split']['output']=stats
    else:report['forms'][form]['output']=stats
    select([ob]);dest=out/('nyx-fake-backpack.fbx' if key=='backpack' else 'nyx-'+key+'-mixamo.fbx')
    bpy.ops.export_scene.fbx(filepath=str(dest),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',
      global_scale=1.0,apply_unit_scale=True,path_mode='COPY',embed_textures=True,add_leaf_bones=False,bake_anim=False)
    stats['fbx_bytes']=dest.stat().st_size;stats['fbx_sha256']=hashlib.sha256(dest.read_bytes()).hexdigest()
    # Exact expected coordinates and UVs for the fresh-process roundtrip checks.
    uv=np.empty(len(ob.data.uv_layers.active.data)*2,dtype=np.float64);ob.data.uv_layers.active.data.foreach_get('uv',uv)
    np.savez_compressed(out/(key+'-expected.npz'),vertices=v,uv=uv)
# Both bodies remain centred in the authoring file; enable one at a time. No lineup offset is baked.
for key,obs in [('real',[objects['real']]),('fake',[body,prop])]:
    select(obs)
    bpy.ops.export_scene.gltf(filepath=str(out/('nyx-'+key+'-dressed.glb')),export_format='GLB',use_selection=True,export_animations=False)
for key,ob in objects.items():ob.hide_set(key=='fake' or key=='backpack')
bpy.data.orphans_purge(do_recursive=True)
select([objects['real']]);bpy.ops.wm.save_as_mainfile(filepath=str(out/'nyx-prepared.blend'),compress=True)
# A prop-only editable file, at exactly its body-relative coordinates, ready for a future Spine2 child bone.
# Saved below as a complete scene after measurements have consumed the body data.
# Measurements include raw/target coordinates, explicit estimated versus surface-sampled status, and uncertainty.
for form in ['real','fake']:
    cfg=config[form];s=scales[form];r=report['forms'][form]
    points=dict(cfg['landmarks'])
    points['head_top']={'z':crown[form],'uncertainty_cm':.5,'method':'maximum Z of scalp hair component, excluding cat ears, ring and headband'}
    source_v=source_vertices[form]
    legv=source_v[groups[form][cfg['legs_component']][1]]
    central=legv[np.abs(legv[:,0])<.003];cz=float(central[:,2].min())
    points['crotch']={'z':cz,'uncertainty_cm':.7,'method':'lowest trouser/shorts surface vertex within |X|<0.003 on isolated leg component; garment groin proxy, not hip joint'}
    points['sole']={'z':sole[form],'uncertainty_cm':0,'method':'minimum body mesh Z, includes shoe platform'}
    r['landmarks']={}
    for name,p in points.items():
        value=dict(p);value['raw_z']=p['z'];value['height_cm']=(p['z']-sole[form])*s*100
        if 'x' in p:
            value['raw_half_span']=p['x'];value['span_cm']=p['x']*s*200
            value['left_front_xz_m']=[p['x']*s,(p['z']-sole[form])*s]
            value['right_front_xz_m']=[-p['x']*s,(p['z']-sole[form])*s]
        r['landmarks'][name]=value
for name in ['head_top','eye','chin','shoulder','elbow','wrist','hip','crotch','knee','ankle','sole']:
    a=report['forms']['real']['landmarks'][name];b=report['forms']['fake']['landmarks'][name]
    for metric in ['height_cm','span_cm']:
        if metric not in a:continue
        report['rows_cm'].append({'landmark':name,'metric':metric,'real':a[metric],'fake':b[metric],'fake_minus_real':b[metric]-a[metric],
          'uncertainty_each_cm':[(2 if metric=='span_cm' else 1)*p['uncertainty_cm'] for p in [a,b]]})
report['upload_limits']={'checked':'2026-10-29','official_docs':'https://helpx.adobe.com/creative-cloud/faq/mixamo-faq.html',
 'numeric_limit_status':'Adobe help and public Mixamo client expose no authoritative numerical file-size or polygon cap. No claim of server acceptance before owner upload.',
 'local_practical_budget_bytes':30000000,'all_uploads_below_budget':all(report['forms'][f]['output']['fbx_bytes']<30000000 for f in ['real','fake']),
 'mesh_decimation':False,'texture_downsampling':False}
json.dump(report,open(out/'measurements.json','w'),indent=2)
for ob in [objects['real'],body]:bpy.data.objects.remove(ob,do_unlink=True)
prop.hide_set(False);prop.hide_render=False;select([prop])
bpy.data.orphans_purge(do_recursive=True)
bpy.ops.wm.save_as_mainfile(filepath=str(out/'nyx-fake-backpack.blend'),compress=True)
print('SCALE',scales,'FAKE_RELATIVE',relative)
for r in report['rows_cm']:print(r['landmark'],r['metric'],*[round(r[k],2) for k in ['real','fake','fake_minus_real']])
print('DONE',out)
