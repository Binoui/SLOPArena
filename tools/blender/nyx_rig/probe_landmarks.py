"""Print surface samples and expose FAKE's legs for anatomical landmark selection."""
import sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
from common import *
out=Path(sys.argv[sys.argv.index('--')+1]).resolve()
bpy.ops.wm.open_mainfile(filepath=str(out/'inspection.blend'))
for form,cid in [('real',4),('fake',3)]:
 ob=bpy.data.objects['Nyx_'+form];gs=islands(ob);v=vertices(ob)[gs[cid][1]]
 for x in [.003,.008,.015]:
  w=v[np.abs(v[:,0])<x];print(form,'crotch near centre',x,w[w[:,2].argmin()].tolist(),flush=True)
 leg=subset(ob,gs[cid][0],form+'_leg_probe')
 if form=='fake':fake=leg
# Front silhouette intervals give arm/leg cross-section centres instead of using sleeve edges.
sections={}
for form,zs in [('real',[.792,.682,.568,.553,.560,.310,.090]),('fake',[.727,.622,.527,.510,.499,.310,.100])]:
 ob=bpy.data.objects['Nyx_'+form];rows=[]
 for z in zs:
  hits=[]
  for x in np.arange(0,.34,.0005):
   probe=bpy.data.objects[form+'_leg_probe'] if z==.310 else ob
   ok,loc,norm,fi=probe.ray_cast(Vector((float(x),-.5,z)),Vector((0,1,0)))
   hits.append(bool(ok))
  runs=[];start=None
  for j,hit in enumerate(hits+[False]):
   if hit and start is None:start=j
   if not hit and start is not None:
    if j-start>2:runs.append([start*.0005,(j-1)*.0005,(start+j-1)*.00025])
    start=None
  rows.append({'z':z,'intervals_min_max_mid_x':runs});print('SECTION',form,z,runs,flush=True)
 sections[form]=rows
import json
json.dump(sections,open(out/'landmark-sections.json','w'),indent=2)
if '--checks-only' not in sys.argv:
 for ob in bpy.context.scene.objects:
  if ob.type=='MESH':ob.hide_render=ob!=fake
 setup_render(1500)
 shoot(out/'fake-legs-front.png',(0,0,.5),1.2,0)
 shoot(out/'fake-legs-side.png',(0,0,.5),1.2,90)
