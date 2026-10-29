"""Colour raw FAKE face islands to distinguish torso straps from removable backpack surfaces."""
import sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
from common import *
out=Path(sys.argv[sys.argv.index('--')+1]).resolve();bpy.ops.wm.open_mainfile(filepath=str(out/'inspection.blend'))
fake=bpy.data.objects['Nyx_fake'];gs=islands(fake)
fake.data.materials.clear()
colors=[(.65,.45,.3,1),(.8,.08,.08,1),(.08,.2,.8,1),(.03,.8,.25,1),(.8,.65,.05,1),(.45,.45,.45,1)]
for i,c in enumerate(colors):
 m=bpy.data.materials.new('Region'+str(i));m.use_nodes=True;m.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=c;fake.data.materials.append(m)
attr=fake.data.attributes.new('island_id','INT','FACE')
for i,(fs,vs) in enumerate(gs):
 mi={0:0,1:1,2:2,4:3,9:4,10:4}.get(i,5)
 for fi in fs:
  fake.data.polygons[fi].material_index=mi
  attr.data[fi].value=i
ids=__import__('json').load(open(out/'split-selection.json'))['prop_components']
body=subset(fake,[f for i,g in enumerate(gs) if i not in ids for f in g[0]],'region_body')
for ob in bpy.context.scene.objects:
 if ob.type=='MESH':ob.hide_render=ob!=body
bpy.context.view_layer.update()
for z in [.57,.60,.63,.66,.69]:
 for x in [0,.025,.045]:
  ok,loc,norm,fi=body.ray_cast(Vector((x,.4,z)),Vector((0,-1,0)))
  print('BACK RAY',x,z,'island',body.data.attributes['island_id'].data[fi].value if ok else None,'point',list(loc) if ok else None,flush=True)
setup_render(1200)
for view,yaw in [('back',180),('side',90)]:shoot(out/('regions-'+view+'.png'),(0,0,.7),.55,yaw)
