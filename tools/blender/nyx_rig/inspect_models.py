"""Inspect raw FBX geometry and render coordinate-calibrated views before choosing landmarks."""
import sys, json
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
from common import *
args=sys.argv[sys.argv.index('--')+1:]; root=Path(args[0]).resolve(); out=Path(args[1]).resolve(); out.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
report={}; objects=[]
for form in ['real','fake']:
    path=next((root/form).glob('tripo_convert_*.fbx'))
    ob=load_mesh(path,'Nyx_'+form); objects.append(ob)
    tex=next(path.with_suffix('.fbm').glob('*.png')); material(ob,tex,form)
    v=vertices(ob); gs=islands(ob)
    report[form]={'source':str(path),'vertices':len(v),'faces':len(ob.data.polygons),'bounds':[v.min(0).tolist(),v.max(0).tolist()],'components':[]}
    for i,(fs,vs) in enumerate(gs):
        w=v[vs]
        report[form]['components'].append({'id':i,'vertices':len(vs),'faces':len(fs),'min':w.min(0).tolist(),'max':w.max(0).tolist(),'centroid':w.mean(0).tolist()})
    np.savez_compressed(out/(form+'-raw.npz'),vertices=v)
    print(form,'vertices',len(v),'faces',len(ob.data.polygons),'components',len(gs),flush=True)
json.dump(report,open(out/'raw-components.json','w'),indent=2)
bpy.ops.wm.save_as_mainfile(filepath=str(out/'inspection.blend'))
setup_render(1500)
for form,ob in zip(['real','fake'],objects):
    for o in objects:o.hide_render=o!=ob
    # Frame exactly -0.6..+0.6 horizontally and -0.1..1.1 vertically.
    for view,yaw in [('front',0),('back',180),('side',90)]:
        shoot(out/(form+'-'+view+'.png'),(0,0,.5),1.2,yaw)
