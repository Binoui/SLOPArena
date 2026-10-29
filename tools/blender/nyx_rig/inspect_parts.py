"""Render candidate rear-only prop islands and the hidden leg surfaces without changing source files."""
import sys,json
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
from common import *
out=Path(sys.argv[sys.argv.index('--')+1]).resolve()
bpy.ops.wm.open_mainfile(filepath=str(out/'inspection.blend'))
real=bpy.data.objects['Nyx_real'];fake=bpy.data.objects['Nyx_fake']
gs=islands(fake);v=vertices(fake)
ids=[i for i,(fs,vs) in enumerate(gs) if v[vs,1].min()>.012]
propfs=[f for i in ids for f in gs[i][0]];bodyfs=[f for i,g in enumerate(gs) if i not in ids for f in g[0]]
prop=subset(fake,propfs,'candidate_prop');body=subset(fake,bodyfs,'candidate_body')
rg=islands(real);legs=subset(real,rg[4][0],'real_hidden_legs')
print('PROP IDS',ids,flush=True)
json.dump({'prop_components':ids},open(out/'split-selection.json','w'),indent=2)
setup_render(1200)
meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
for ob,name in [(prop,'prop'),(body,'body'),(legs,'real-legs')]:
 for o in meshes:o.hide_render=o!=ob
 for view,yaw in [('front',0),('back',180),('side',90)]:
  shoot(out/(name+'-'+view+'.png'),(0,0,.5),1.2,yaw)
