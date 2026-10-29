"""Crash-Test Lab materials and lighting consumed by the shared art-apply command."""
from stagekit.presentation import LIT, NEON, CULL_BACK
from layout import SPEC

SHADOWED=tuple(s['name']+suffix for s in SPEC['solids'] for suffix in (' Cap',' Plating'))+('Braced Lattice',)


def lit(slot, base):
    return {'slot':slot,'asset':slot,'shader':LIT,'baseMap':base,'baseColor':[1,1,1,1],
            'textures':{'_DetailMap':'TrainingDetail.png'},
            'floats':{'_DetailScale':.7,'_DetailStrength':.10,'_Wrap':.35,'_Specular':.06,'_Gloss':18,
                      '_Emission':0,'_Wind':0,'_Cull':CULL_BACK,'_ReceiveShadowsOff':0},
            'colors':{'_ShadowTint':[.64,.66,.68]},'keywords':{'_RECEIVE_SHADOWS_OFF':False}}


def hall(slot,glow=0):
    return {'slot':slot,'asset':slot,'shader':NEON,'baseMap':'TrainingAtlas.png','baseColor':[1,1,1,1],
            'floats':{'_Base':.72,'_Ambient':.18,'_Shade':.22,'_Glow':glow,'_FogAmount':1,'_TimeScale':1,'_Cull':CULL_BACK},
            'vectors':{'_ShadeDirection':[.1,.95,-.25,0]}}


PRESENTATION={
    'stage':'training','folder':'Assets/Stages/training/Presentation/CrashTestLab',
    'model':'TrainingLab.fbx','stamp':'TrainingLab.layout.json','root':'Crash-Test Lab',
    'supersededRoots':['Neutral Lab Fill','Practice Lab Finish','Measurement Surfaces','Room and Platforms'],
    'removeRootLights':True,
    'textures':[
        {'file':'TrainingAtlas.png','srgb':True,'wrap':'clamp','maxSize':2048,'aniso':8},
        {'file':'TrainingFloor.png','srgb':True,'wrap':'clamp','maxSize':4096,'aniso':8},
        {'file':'TrainingDetail.png','srgb':False,'wrap':'repeat','maxSize':256,'aniso':8},
    ],
    'materials':[lit('TL_Lit','TrainingAtlas.png'),lit('TL_Floor','TrainingFloor.png'),hall('TL_Hall'),hall('TL_Glow',1.05)],
    'shadows':{'cast':False,'receive':False,'objects':{n:{'cast':True,'receive':True} for n in SHADOWED}},
    'lights':[
        {'name':'Overhead Lab Key','type':'directional','rotation':[78,0,0],'color':[.96,.98,1],
         'intensity':1.0,'shadows':'soft','shadowStrength':.50},
    ],
    'atmosphere':{'background':[.30,.34,.38],'ambientSky':[.60,.62,.65],'ambientGround':[.30,.32,.34],
                  'ambientIntensity':1,'fog':True,'fogColor':[.36,.40,.44],'fogStart':45,'fogEnd':160},
    'cameras':[
        {'name':'start','position':[0,5,-17],'target':[0,2,5],'fov':45},
        {'name':'dummy','position':[0,5,17],'target':[0,1,-6],'fov':45},
        {'name':'yard','position':[-12,14,-6],'target':[-26,.2,2],'fov':70},
        {'name':'range','position':[9,7,-7],'target':[23,-.7,0],'fov':82},
        {'name':'trench','position':[-15.5,-2,-6],'target':[-15.5,1,6],'fov':75},
        {'name':'south-recovery','position':[5,-2.5,-25],'target':[0,-5,-13],'fov':60},
        {'name':'thumbnail','position':[15,21,-30],'target':[-3,-2,0],'fov':84},
    ],
}
