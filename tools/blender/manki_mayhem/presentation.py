"""Warm venue backlight and recovery fill; the neutral deck keeps its original lighting."""
from stagekit.presentation import CULL_BACK, LIT, NEON

SHADOWED = ('Concrete Deck', 'Deck Hoarding', 'Deck Scaffold Tower')


def lit(asset, texture, detail, bounce=0):
    return {'asset':asset,'shader':LIT,'baseMap':texture,'baseColor':[1,1,1,1],
            'textures':{'_DetailMap':'MankiMayhemDetail.png'},
            'floats':{'_DetailScale':.5,'_DetailStrength':detail,'_Wrap':.30,'_Specular':.07,
                      '_Gloss':18,'_Emission':bounce,'_Wind':0,'_Cull':CULL_BACK,'_ReceiveShadowsOff':0},
            'colors':{'_ShadowTint':[.62,.57,.52]},'keywords':{'_RECEIVE_SHADOWS_OFF':False}}


def night(asset, base, glow, ambient=.24):
    return {'asset':asset,'shader':NEON,'baseMap':'MankiMayhemAtlas.png','baseColor':[1,1,1,1],
            'floats':{'_Base':base,'_Ambient':ambient,'_Shade':.24,'_Glow':glow,'_FogAmount':.55,
                      '_TimeScale':1,'_Cull':CULL_BACK},
            'vectors':{'_ShadeDirection':[.15,.85,-.3,0]}}


CAMERAS = [
    # South-south-east, looking towards the host booth: positive X / negative Z.
    {'name':'thumbnail','position':[19,14,-29],'target':[0,1.5,8],'fov':46},
    {'name':'show-wide','position':[0,10,-29],'target':[0,5.5,10],'fov':48},
    {'name':'host-booth','position':[0,12,23],'target':[0,8,42],'fov':64},
    {'name':'broadcast-desk','position':[8,5,-26],'target':[0,1,-40],'fov':61},
]
# All eight low recovery poses look down, never up. Inspect the actual edge/KO walls.
for name,pos,target in [
    ('recovery-north',[0,-2,24],[0,-5,10]),('recovery-east',[26,-2,0],[12,-5,0]),
    ('recovery-south',[0,-2,-24],[0,-5,-10]),('recovery-west',[-26,-2,0],[-12,-5,0]),
    ('recovery-ne',[24,-2,22],[12,-5,10]),('recovery-nw',[-24,-2,22],[-12,-5,10]),
    ('recovery-se',[24,-2,-22],[12,-5,-10]),('recovery-sw',[-24,-2,-22],[-12,-5,-10]),
]:
    CAMERAS.append({'name':name,'position':pos,'target':target,'fov':55})
for i,(x,z,yaw) in enumerate(((-5,-4,0),(5,4,180),(-5,4,135),(5,-4,315))):
    import math
    a=math.radians(yaw)
    CAMERAS.append({'name':'spawn-%d'%(i+1),'position':[x-11*math.sin(a),5.3,z-11*math.cos(a)],
                    'target':[x,1.3,z],'fov':40})
CAMERAS.append({'name':'warehouse-below','position':[20,-10,-23],'target':[0,-25,6],'fov':62})

PRESENTATION = {
    'stage':'manki_mayhem','folder':'Assets/Stages/manki_mayhem/Presentation/PirateBroadcast',
    'model':'MankiMayhemArena.fbx','stamp':'MankiMayhemArena.layout.json',
    'root':'Manki Mayhem Pirate Broadcast','supersededRoots':[],
    'surfaces':[
        {'name':'Mayhem CRT Left','position':[-22.5,7.4,37.51],'rotation':[0,0,0],'size':[10.4,6.3]},
        {'name':'Mayhem CRT Right','position':[22.5,7.4,37.51],'rotation':[0,0,0],'size':[10.4,6.3]},
    ],
    'textures':[
        {'file':'MankiMayhemAtlas.png','srgb':True,'wrap':'clamp','maxSize':2048,'aniso':8},
        {'file':'MankiMayhemFloor.png','srgb':True,'wrap':'clamp','maxSize':2048,'aniso':8},
        {'file':'MankiMayhemDetail.png','srgb':False,'wrap':'repeat','maxSize':256,'aniso':8},
    ],
    'materials':[
        {'slot':'MM_Night',**night('MankiMayhemNight',.78,2.2)},
        {'slot':'MM_Lit',**lit('MankiMayhemStructure','MankiMayhemAtlas.png',.22,.70)},
        {'slot':'MM_Floor',**lit('MankiMayhemConcrete','MankiMayhemFloor.png',.12)},
        {'slot':'MM_Screen',**night('MankiMayhemCRT',.35,1.05,.05)},
        {'slot':'MM_Beam','asset':'MankiMayhemWarmBeams','shader':'Universal Render Pipeline/Particles/Unlit',
         'baseColor':[1,1,1,1],
         'floats':{'_Surface':1,'_Blend':2,'_SrcBlend':5,'_DstBlend':1,'_ZWrite':0,'_Cull':0,'_ColorMode':0,'_SrcBlendAlpha':1,'_DstBlendAlpha':1,'_QueueOffset':9},
         'keywords':{'_SURFACE_TYPE_TRANSPARENT':True}},
    ],
    'shadows':{'cast':False,'receive':False,'objects':{n:{'cast':True,'receive':True} for n in SHADOWED}},
    'lights':[
        {'name':'Broadcast Key','type':'directional','rotation':[64,22,0],'color':[1,.93,.83],
         'intensity':1.10,'shadows':'soft','shadowStrength':.60},
        {'name':'Deck Tungsten Fill','type':'point','position':[0,10,0],'color':[1,.85,.65],
         'intensity':20,'range':27},
        {'name':'Host Work Lamp','type':'point','position':[0,12,40],'color':[1,.69,.36],
         'intensity':22,'range':17},
    ],
    'atmosphere':{'background':[.016,.011,.008],'ambientSky':[.31,.27,.23],
                  'ambientGround':[.08,.065,.05],'ambientIntensity':1,
                  'fog':True,'fogColor':[.095,.060,.034],'fogStart':35,'fogEnd':155},
    'cameras':CAMERAS,
}

# The stage's look (owner 2026-11-11: every stage gets one). Fields: stagekit/presentation.py.
# The broadcast deck under its own lamp, as in the Slop Pit: the warehouse and the audience sit back.
# The CRT walls and the host booth stay sharp and untinted, because the show reads from them.
QUIET = [0.72, 0.70, 0.68]
PRESENTATION["look"] = {
    "grade": {"tonemap": "neutral", "bloomThreshold": 0.9, "bloomIntensity": 0.9, "bloomScatter": 0.65, "contrast": 12,
              "saturation": 6, "filter": [1.0, 0.97, 0.93], "splitShadows": [0.50, 0.47, 0.44],
              "splitHighlights": [0.56, 0.52, 0.46], "splitBalance": 0, "vignette": 0.26, "vignetteColor": [0.03, 0.02, 0.01]},
    "farBlur": {"start": 58, "end": 140, "radius": 0.9},
    "tints": {"Audience": QUIET, "Audience Fences": QUIET, "Scaffold Stands": QUIET, "Warehouse": QUIET,
              "Side Identities": [0.85, 0.84, 0.83], "Venue Backlight": [0.9, 0.9, 0.9], "Warehouse Light Pools": [0.9, 0.9, 0.9]},
    "pools": [{"position": [0, 20, 0], "direction": [0, -1, 0], "color": [1.0, 0.9, 0.72],
               "intensity": 300, "range": 40, "angle": 52, "innerAngle": 28}],
    "dust": {"count": 300, "center": [0, 6, 0], "size": [14, 10, 14], "color": [1.0, 0.92, 0.75, 0.6]},
    # Tungsten from above, the cold light of the screens from behind.
    "fighters": {"kickA": {"color": [1.0, 0.84, 0.58], "direction": [-0.4, 1, -0.3], "strength": 1.5},
                 "kickB": {"color": [0.55, 0.85, 1.0], "direction": [0.6, 0.1, 0.8], "strength": 1.0},
                 "kickPower": 2.6, "lift": 0.06, "lampCap": 1.0, "contactShadow": 0.45, "contactShadowColor": [0.08, 0.06, 0.04]},
}
