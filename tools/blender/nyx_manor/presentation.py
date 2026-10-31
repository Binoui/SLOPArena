"""Moonlit manor, candle windows, and a restrained pink bounce on the fake front."""
from stagekit.presentation import LIT,NEON,WATER
PRESENTATION={
 'stage':'nyx_manor','folder':'Assets/Stages/nyx_manor/Presentation/Manor',
 'model':'NyxManor.fbx','stamp':'NyxManor.layout.json','root':'Two-Faced Manor Art','supersededRoots':['Two-Faced Manor Graybox'],
 'textures':[{'file':'MoonHalo.png','wrap':'clamp','srgb':True,'maxSize':256,'aniso':0},{'file':'ManorGrit.png','wrap':'repeat','srgb':False,'maxSize':256,'aniso':4},{'file':'ManorRipples.png','wrap':'repeat','srgb':False,'maxSize':256,'aniso':4}],
 'materials':[
  {'slot':'ManorLit','asset':'ManorLit','shader':LIT,'baseColor':[1,1,1,1],
   'textures':{'_DetailMap':'ManorGrit.png'},'floats':{'_DetailStrength':.23,'_DetailScale':1.8,'_Wrap':.38,'_Specular':.12,'_Gloss':30,'_Emission':0,'_Cull':2},'colors':{'_ShadowTint':[.62,.67,.79]}},
  {'slot':'ManorGlow','asset':'ManorGlow','shader':NEON,'baseColor':[1,1,1,1],
   'floats':{'_Base':1,'_Ambient':.15,'_Shade':0,'_Glow':1.25,'_TimeScale':0,'_FogAmount':.35,'_Cull':0}},
  {'slot':'MoonSurface','asset':'MoonSurface','shader':NEON,'baseColor':[1,1,1,1],
   'floats':{'_Base':1,'_Ambient':0,'_Shade':0,'_Glow':.35,'_TimeScale':0,'_FogAmount':0,'_Cull':2}},
  {'slot':'NightSky','asset':'NightSky','shader':NEON,'baseColor':[1,1,1,1],
   'floats':{'_Base':1,'_Ambient':0,'_Shade':0,'_Glow':0,'_TimeScale':0,'_FogAmount':0,'_Cull':0}},
  {'slot':'CandleGlow','asset':'CandleGlow','shader':NEON,'baseColor':[1,1,1,1],
   'floats':{'_Base':1,'_Ambient':0,'_Shade':0,'_Glow':1.7,'_TimeScale':1,'_FogAmount':.25,'_Cull':0}},
  {'slot':'ManorWater','asset':'ManorWater','shader':WATER,'baseColor':[1,1,1,1],
   'textures':{'_RippleMap':'ManorRipples.png'},
   'floats':{'_RippleScale':.09,'_RippleStrength':.38,'_RippleSpeed':.28,'_SwellHeight':.15,'_SwellLength':13,'_SwellFade':320,'_Diffuse':.8,'_Fresnel':.65,'_Specular':.3,'_Gloss':280,'_FoamStrength':.15},
   'colors':{'_SkyColor':[.10,.17,.27],'_HorizonColor':[.27,.34,.48],'_FoamColor':[.29,.35,.47]}},
  {'slot':'MoonHalo','asset':'MoonHalo','shader':'Universal Render Pipeline/Unlit','baseMap':'MoonHalo.png','baseColor':[1,1,1,1],
   'floats':{'_Surface':1,'_Blend':0,'_SrcBlend':5,'_DstBlend':10,'_ZWrite':0,'_Cull':0},'keywords':{'_SURFACE_TYPE_TRANSPARENT':True}}],
 'shadows':{'cast':True,'receive':True,'objects':{'Outside Blast Scenery':{'cast':False,'receive':False},'Starry Gradient Sky':{'cast':False,'receive':False},'Moonlit Sea':{'cast':False,'receive':False}}},
 'lights':[
  {'name':'Moonlight','type':'directional','sun':{'azimuth':210,'elevation':55},'color':[.75,.83,1],'intensity':1.05,'shadows':'soft','shadowStrength':.4},
  {'name':'Night Fill','type':'directional','rotation':[30,30,0],'color':[.70,.75,.85],'intensity':.5,'shadows':'none'},
  {'name':'Pink Front Bounce','type':'point','position':[0,13,-12],'color':[1,.18,.42],'intensity':7,'range':28},
  {'name':'Crypt Candles','type':'point','position':[0,11,61.5],'color':[1,.48,.16],'intensity':5,'range':8},
  {'name':'Gate Candles','type':'point','position':[0,10.7,47],'color':[1,.46,.12],'intensity':4,'range':8},
  {'name':'West Plot Lantern','type':'point','position':[-12,10.7,54],'color':[1,.45,.12],'intensity':4,'range':7},
  {'name':'East Plot Lantern','type':'point','position':[12,10.7,64],'color':[1,.45,.12],'intensity':4,'range':7}],
 'atmosphere':{'background':[.025,.04,.08],'ambientSky':[.42,.46,.58],'ambientGround':[.21,.22,.29],'ambientIntensity':1,'fog':True,'fogColor':[.065,.085,.14],'fogStart':95,'fogEnd':360},
 'cameras':[
  {'name':'thumbnail','position':[45,42,-54],'target':[0,5,-4],'fov':55},
  {'name':'front','position':[0,17,-45],'target':[0,8,-7],'fov':60},
  {'name':'back','position':[37,35,88],'target':[0,7,40],'fov':58},
  {'name':'low','position':[28,1,-28],'target':[0,5,-3],'fov':60},
  {'name':'recovery','position':[22,1,43],'target':[0,5,35],'fov':65},
  {'name':'crossing','position':[24,22,30],'target':[2,7,35],'fov':65},
  {'name':'graveyard','position':[22,23,75],'target':[0,10,55],'fov':58},
  {'name':'moon','position':[0,20,50],'target':[-27,46,115],'fov':42}]}

# Intermediate-yaw sweeps share the exact match-camera render path with the hero views.
import math
for label,step,rx,rz,height,fov in (('orbit',15,100,100,48,65),('rim',45,39,82,-1,85)):
    for yaw in range(0,360,step):
        a=math.radians(yaw)
        camera_y=68 if label=='orbit' and yaw==345 else height
        PRESENTATION['cameras'].append({'name':f'{label}-{yaw:03d}','position':[math.sin(a)*rx,camera_y,11+math.cos(a)*rz],'target':[0,6 if label=='orbit' else 10,11],'fov':fov})

for yaw in range(0,360,45):
    a=math.radians(yaw)
    camera_x=-8 if yaw in (0,180) else math.sin(a)*27
    PRESENTATION['cameras'].append({'name':f'ravine-{yaw:03d}','position':[camera_x,2,35+math.cos(a)*10],'target':[3,6,35],'fov':68})
