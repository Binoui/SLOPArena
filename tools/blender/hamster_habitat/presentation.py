"""Night bedroom presentation (concept C): the desk lamp left on is the key, moonlight and a deep
blue ambient fill the room. Glass tubes and wheel; glow stars, moon and bulb in StageNeon."""
from stagekit.presentation import CULL_BACK, CULL_OFF, LIT, NEON
import room

GLASS = 'SLOPArena/StageGlass'
# Glass casts no shadow; its hoops do, which keeps the tubes readable on the floors below.
SHADOWED = ('Desk Bin Shavings', 'Shelf Bin Shavings', 'Bins', 'House', 'Food Bowl', 'Wheel Deck', 'Wheel Rims',
            'Wheel Post', 'TP Roll', 'Junction Step', 'Low Tube Frame', 'High Tube Frame', 'Hamster')


def lit(asset, texture, wrap=.35):
    return {'asset':asset,'shader':LIT,'baseMap':texture,'baseColor':[1,1,1,1],
            'floats':{'_DetailStrength':0,'_Wrap':wrap,'_Specular':.06,'_Gloss':18,'_Emission':0,'_Wind':0,
                      '_Cull':CULL_OFF,'_ReceiveShadowsOff':0},
            'colors':{'_ShadowTint':[.42,.48,.78]},'keywords':{'_RECEIVE_SHADOWS_OFF':False}}


CAMERAS = [
    # Over the gap from the south, looking north at the bins, the lamp and the hamster.
    {'name':'thumbnail','position':[10,15,-44],'target':[-8,2,12],'fov':50},
    {'name':'gap-south','position':[0,5,-39],'target':[0,0,4],'fov':55},
    {'name':'gap-north','position':[0,7,39],'target':[0,0,0],'fov':55},
    {'name':'hamster','position':[-16,6,-22],'target':[-22,6,40],'fov':45},
    {'name':'room-east','position':[20,6,-40],'target':[45,-15,60],'fov':60},
    {'name':'drop','position':[0,-6,-30],'target':[0,-40,5],'fov':55},
    {'name':'west','position':[-6,8,2],'target':[-120,-25,-5],'fov':60},
    {'name':'south','position':[-12,8,4],'target':[-20,-20,-150],'fov':60},
]

PRESENTATION = {
    'stage':'hamster_habitat','folder':'Assets/Stages/hamster_habitat/Presentation/Bedroom',
    'model':'HamsterHabitatArena.fbx','stamp':'HamsterHabitatArena.layout.json',
    'root':'Hamster Habitat Night','supersededRoots':['Hamster Habitat Blockout'],
    'textures':[{'file':'HamsterHabitatWhite.png','srgb':True,'wrap':'clamp','maxSize':32,'aniso':1},
                {'file':'HamsterHabitatShavings.png','srgb':True,'wrap':'repeat','maxSize':256,'aniso':8},
                {'file':'HamsterHabitatPosters.png','srgb':True,'wrap':'clamp','maxSize':2048,'aniso':8}],
    'materials':[
        {'slot':'HH_Lit',**lit('HamsterHabitatLit','HamsterHabitatWhite.png')},
        {'slot':'HH_Shavings',**lit('HamsterHabitatShavings','HamsterHabitatShavings.png',wrap=.25)},
        {'slot':'HH_Posters',**lit('HamsterHabitatPosters','HamsterHabitatPosters.png')},
        {'slot':'HH_Glass','asset':'HamsterHabitatGlass','shader':GLASS,'baseMap':'HamsterHabitatWhite.png',
         'baseColor':[1,1,1,.10],'floats':{'_Rim':.45,'_RimPower':2.5,'_Specular':.7,'_Gloss':64,'_Cull':CULL_BACK}},
        {'slot':'HH_Glow','asset':'HamsterHabitatGlow','shader':NEON,'baseMap':'HamsterHabitatWhite.png',
         'baseColor':[1,1,1,1],'floats':{'_Base':.9,'_Ambient':.2,'_Shade':0,'_Glow':2.2,'_FogAmount':.2,
                                          '_TimeScale':0,'_Cull':CULL_OFF},
         'vectors':{'_ShadeDirection':[.3,.8,.4,0]}},
        # The hamster: lit from the lamp side in front of it, readable at every quality level.
        {'slot':'HH_Toon','asset':'HamsterHabitatToon','shader':NEON,'baseMap':'HamsterHabitatWhite.png',
         'baseColor':[1,1,1,1],'floats':{'_Base':.55,'_Ambient':.45,'_Shade':.5,'_Glow':0,'_FogAmount':.2,
                                          '_TimeScale':0,'_Cull':CULL_OFF},
         'vectors':{'_ShadeDirection':[-.35,.55,-.75,0]}},
    ],
    'shadows':{'cast':False,'receive':False,'objects':{n:{'cast':True,'receive':True} for n in SHADOWED}},
    'lights':[
        # The lamp at the back of the desk, aimed over the bins.
        {'name':'Lamp Key','type':'directional','rotation':[62,150,0],'color':[1,.78,.52],
         'intensity':.72,'shadows':'soft','shadowStrength':.65},
        {'name':'Lamp Bulb','type':'point','position':list(room.LAMP_HEAD),'color':[1,.74,.45],
         'intensity':7,'range':95},
        {'name':'Moonlight','type':'directional','rotation':[28,200,0],'color':[.55,.65,1],'intensity':.28},
    ],
    'atmosphere':{'background':[.02,.025,.06],'ambientSky':[.34,.38,.62],'ambientGround':[.07,.06,.10],
                  'ambientIntensity':1,'fog':True,'fogColor':[.03,.04,.10],'fogStart':50,'fogEnd':260},
    'cameras':CAMERAS,
}
