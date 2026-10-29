"""Render the built rig, same eight poses per form, and an exact A-pose fit comparison."""
import bpy, sys, math, json
import numpy as np
from pathlib import Path
from mathutils import Matrix, Vector
sys.path.insert(0,str(Path(__file__).resolve().parent))
from common import setup_render, shoot, material
ROOT=Path(__file__).resolve().parents[3]
OUT=Path(sys.argv[sys.argv.index('--')+1]).resolve()
review=OUT/'review';review.mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(OUT/'Nyx_rig.blend'))
arm=bpy.data.objects['Armature'];P='mixamorig:'
for o in bpy.context.scene.objects:
 o.hide_set(False)
meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
setup_render(640)
scene=bpy.context.scene;scene.render.engine='BLENDER_EEVEE'
scene.render.resolution_x=600;scene.render.resolution_y=700
scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.30,.33,.38,1)

def show(form):
 for o in meshes:
  o.hide_render=not (o.name.startswith('Nyx_'+form) or form=='Fake' and o.name=='Nyx_Backpack')

def reset():
 for b in arm.pose.bones:b.matrix_basis.identity()
 bpy.context.view_layer.update()

def rot(n,axis,deg):
 b=arm.pose.bones[n if n in arm.pose.bones else P+n]
 r=Matrix.Rotation(math.radians(deg),4,Vector(axis))
 b.matrix=Matrix.Translation(b.head)@r@Matrix.Translation(-b.head)@b.matrix
 bpy.context.view_layer.update()

def down():
 rot('LeftArm',(0,1,0),75);rot('RightArm',(0,1,0),-75)

def fist():
 down()
 for side,sgn in [('Left',1),('Right',-1)]:
  for f in ['Index','Middle','Ring','Pinky']:
   for k in [1,2,3]:rot(f'{side}Hand{f}{k}',(0,1,0),sgn*65)
  for k in [1,2,3]:rot(f'{side}HandThumb{k}',(0,1,0),sgn*25)

def squat():
 down();b=arm.pose.bones[P+'Hips'];b.matrix=Matrix.Translation((0,0,-.34))@b.matrix
 bpy.context.view_layer.update()
 for side in ['Left','Right']:
  rot(side+'UpLeg',(1,0,0),-65);rot(side+'Leg',(1,0,0),115);rot(side+'Foot',(1,0,0),-50)

poses={
 'arms-raised':lambda:(rot('LeftArm',(0,1,0),-65),rot('RightArm',(0,1,0),65)),
 'arms-forward':lambda:(rot('LeftArm',(0,0,1),-85),rot('RightArm',(0,0,1),85)),
 'wide-stance':lambda:(down(),rot('LeftUpLeg',(0,1,0),-32),rot('RightUpLeg',(0,1,0),32)),
 'deep-squat':squat,
 'high-front-kick':lambda:(down(),rot('LeftUpLeg',(1,0,0),-90),rot('LeftLeg',(1,0,0),12)),
 'run-stride':lambda:(down(),rot('LeftUpLeg',(1,0,0),-55),rot('LeftLeg',(1,0,0),55),rot('RightUpLeg',(1,0,0),30),rot('RightLeg',(1,0,0),55),rot('LeftArm',(1,0,0),30),rot('RightArm',(1,0,0),-40)),
 'fist':fist,
 'open-hand':down,
}
metrics={}
for name,pose in poses.items():
 reset();pose()
 for form in ['Real','Fake']:
  show(form)
  yaw=75 if name=='high-front-kick' else 30 if name in ['run-stride','arms-forward','fist','open-hand'] else 0
  shoot(review/f'{name}-{form.lower()}.png',(0,-.05,.90),2.15,yaw)
  if name in ['fist','open-hand']:
   h=(arm.pose.bones[P+'RightHand'].head+arm.pose.bones[P+'RightHandMiddle3'].tail)/2
   shoot(review/f'{name}-{form.lower()}-hand.png',h,.40,35,res=640)
   scene.render.resolution_x=600;scene.render.resolution_y=700
reset()
a=np.load(OUT/'a-pose.npz')
for b in sorted(arm.pose.bones,key=lambda b:len(b.parent_recursive)):
 b.matrix=Matrix(a[b.name].tolist());bpy.context.view_layer.update()
show('Real')
# Before is the supplied upload A pose, with animation and skin evaluation disabled.
before=set(bpy.context.scene.objects)
bpy.ops.import_scene.fbx(filepath=str(ROOT/'tmp/nyx-model/mixamo-rig-v1/nyx-real-mixamo-rig.fbx'),use_image_search=False)
added=set(bpy.context.scene.objects)-before
src=next(o for o in added if o.type=='MESH');src.modifiers.clear()
material(src,OUT/'textures/Nyx_Real.png','Nyx_Real_FitReference')
for ob in added:
 if ob.type=='ARMATURE':ob.animation_data_clear()
for phase in ['before','after']:
 src.hide_render=phase!='before'
 for ob in meshes:ob.hide_render=phase=='before' or not ob.name.startswith('Nyx_Real')
 for view,yaw,center,scale in [('front',0,(0,-.03,.85),1.90),('side',90,(0,-.03,.85),1.90),('face',0,(0,-.09,1.55),.40)]:
  shoot(review/f'fit-{phase}-{view}.png',center,scale,yaw,res=900)
print('NYX_REVIEW_OK',str(review))
