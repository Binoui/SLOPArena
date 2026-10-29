"""Panelled test hall, a foam pit, safety curtains and the four wall set pieces."""
import math
from stagekit import geo
import palette as P
from parts import bar, dummy, label, foam


def build(b, detailed=False):
    m=b.mesh('Test Hall','background','', 'TL_Hall')
    C=b.data['corridor']
    west_x=C['minX']-13; east_x=C['maxX']+13
    south_z=C['minZ']-13; north_z=C['maxZ']+13
    centre=(west_x+east_x)/2; width=east_x-west_x
    outline=[(west_x,south_z),(east_x,south_z),(east_x,north_z),(west_x,north_z)]
    for a,c,n,length in geo.edges(outline):
        inward=(-n[0],-n[1])
        geo.facade_quad(m,a,c,-18,-5,inward,(.38,.43,.46))
        geo.facade_quad(m,a,c,-5,28,inward,(.77,.79,.79))
        bays=round(length/12)
        for k in range(bays+1):
            t=k/bays; x=a[0]+(c[0]-a[0])*t; z=a[1]+(c[1]-a[1])*t
            geo.box(m,(x,5,z),(.20,46,.20),(.53,.57,.59))
        for y in (-5,4,13,22):
            aa=(a[0]-n[0]*.03,a[1]-n[1]*.03); cc=(c[0]-n[0]*.03,c[1]-n[1]*.03)
            geo.facade_quad(m,aa,cc,y,y+.045,inward,(.58,.62,.63))
        geo.facade_quad(m,a,c,-17,-15.5,inward,P.YELLOW,off=.04)
        for k in range(int(length)):
            aa=tuple(a[i]+(c[i]-a[i])*(k+.1)/length for i in range(2))
            cc=tuple(a[i]+(c[i]-a[i])*(k+.6)/length for i in range(2))
            geo.facade_quad(m,aa,cc,-17,-15.5,inward,P.BLACK,off=.05)
    geo.box(m,(centre,28.5,0),(width+1,1,north_z-south_z+1),P.DARK,skip=('top',))
    pit=b.mesh('Foam Pit','below','', 'TL_Hall')
    geo.box(pit,(centre,-18.7,0),(width,.5,north_z-south_z),P.DARK)
    for x in range(math.ceil(west_x+2),math.floor(east_x-1),3):
        for z in range(-34,35,3):
            y=-17.1+b.rng.uniform(-.35,.35)
            color=b.rng.choice([P.FOAM]*17+[P.YELLOW,(.63,.36,.17)])
            foam(pit,(x+b.rng.uniform(-.15,.15),y,z+b.rng.uniform(-.15,.15)),
                 b.rng.uniform(2.65,2.95),b.rng.uniform(1.4,2.0),geo.jitter(color,b.rng,.12),b.rng.uniform(-.14,.14))
    curtains=b.mesh('Light Curtains','curtain','', 'TL_Glow')
    posts=b.mesh('Curtain Emitters','curtain','', 'TL_Hall')
    heights=(-8,-10/3,4/3,6)
    for a,c,n,length in geo.edges([(C['minX']-.2,C['minZ']-.2),(C['maxX']+.2,C['minZ']-.2),
                                      (C['maxX']+.2,C['maxZ']+.2),(C['minX']-.2,C['maxZ']+.2)]):
        for y in heights:
            bar(curtains,(a[0],y,a[1]),(c[0],y,c[1]),.018,(.85,.055,.025))
        for t in (0,.25,.75):
            x=a[0]+(c[0]-a[0])*t; z=a[1]+(c[1]-a[1])*t
            geo.box(posts,(x,-3.5,z),(.20,20,.20),P.YELLOW)
            for y in range(-13,7):
                geo.box(posts,(x,y,z),(.215,.35,.215),P.BLACK)
            for y in heights:
                geo.box(posts,(x,y,z),(.36,.34,.36),P.DARK)
                geo.box(curtains,(x,y,z),(.38,.10,.38),(.85,.055,.025))
    truss=b.mesh('Overhead Trusses','background','', 'TL_Hall')
    lamps=b.mesh('Panel Lights','background','', 'TL_Glow')
    for z in (-27,-9,9,27):
        for y in (24.2,26):
            bar(truss,(west_x+1,y,z),(east_x-1,y,z),.15,P.DARK)
        for x in range(math.ceil(west_x+1),math.floor(east_x-3),4):
            bar(truss,(x,24.2,z),(x+4,26,z),.075)
            bar(truss,(x,26,z),(x+4,24.2,z),.075)
        for x in range(math.ceil(west_x+7),math.floor(east_x-6),9):
            geo.box(truss,(x,24.2,z),(6,.3,1.05),P.DARK)
            geo.box(lamps,(x,23.95,z),(5.7,.12,.8),(.78,.81,.82))
    if detailed:
        for x in (west_x+2,east_x-2):
            for z in (-26,-10,10,26):
                bar(m,(x,-14,z),(x,2,z+7),.14,P.STEEL)
        for x in (-26,26):
            geo.box(m,(x,-3,35.3),(7,14,.45),geo.scale(P.WALL,.8))
            for y in (-7,-3,1):
                geo.box(m,(x,y,35),(6,.10,.10),P.STEEL)
    north(b,detailed)
    west(b,detailed)
    east(b,detailed)
    south(b,detailed)


def north(b,detailed):
    m=b.mesh('Control Room','background','', 'TL_Hall')
    geo.box(m,(0,2.3,34.5),(31,4,2),P.DARK)
    for x in range(-14,15,4):
        geo.box(m,(x,2.6,33.43),(3.6,2.6,.10),P.GLASS)
        # A desk and crooked monitor occupy each deep window bay.
        geo.box(m,(x,1.6,33.3),(2.8,.18,.35),P.STEEL)
        if detailed:
            geo.box(m,(x+.5,2.1,33.27),(.6,.45,.08),P.DARK)
            geo.box(m,(x+.5,2.1,33.21),(.48,.33,.025),(.32,.46,.49))
    geo.box(m,(0,5.6,34),(26,3.2,.6),P.DARK)
    label(m,'ko',(-12.6,33.68),(12.6,33.68),4.05,7.15,(0,-1))
    geo.box(m,(0,8.1,34),(32,1.35,.6),P.DARK)
    label(m,'hero',(-15.7,33.68),(15.7,33.68),7.5,8.7,(0,-1))
    for x in (-14,14):
        geo.box(m,(x,5.5,33.62),(.15,3.2,.1),P.YELLOW)


def west(b,detailed):
    m=b.mesh('Dummy Mezzanine','background','', 'TL_Hall')
    geo.box(m,(-32,1.2,0),(7,.6,28),P.STEEL)
    for z in (-12,-4,4,12):
        geo.box(m,(-32,-7.5,z),(.6,17,.6),P.DARK)
    geo.box(m,(-32,2.5,0),(2,.35,23),P.DARK)
    # The bench faces east; rotate a local group so every spectator looks at the fight.
    people=geo.Mesh('Watching dummies',['TL_Hall']+[m for m in b.materials if m!='TL_Hall'])
    people.alpha=0
    for x in (-9,-6,-3,0,3,6,9):
        dummy(people,x,0,0,1.45 if x%2 else 1.6)
    people.verts=[(-32-z,y+2.55,x) for x,y,z in people.verts]
    m.extend(people)
    for z in range(-12,13,3):
        geo.box(m,(-28.6,1.40,z),(.20,.18,1.4),P.YELLOW)

    shift=b.data['corridor']['minX']-8+32
    m.verts=[(x+shift,y,z) for x,y,z in m.verts]


def east(b,detailed):
    m=b.mesh('Crash Sled','background','', 'TL_Hall')
    geo.box(m,(31,-14.15,0),(9,.5,48),P.STEEL)
    for x in (29.5,32.5):
        geo.box(m,(x,-13.75,-1),(.18,.3,40),P.DARK)
    for z in range(-20,21,2):
        geo.box(m,(31,-13.95,z),(4,.12,.18),P.DARK)
    geo.box(m,(31,-11.3,20),(7,5.2,1.0),P.DARK)
    for x in range(28,35):
        geo.box(m,(x,-11.3,19.45),(.4,5.1,.05),P.YELLOW)
    # A cartoon wreck on its sled. The crushed nose folds up against the barrier.
    geo.box(m,(31,-12.7,15.4),(4.2,.7,7),P.DARK)
    geo.box(m,(31,-11.9,15.3),(3.5,1.1,5.8),P.YELLOW)
    geo.prism(m,[(29.4,14),(32.6,14),(32.3,16.5),(29.8,16.7)],-11.5,-9.8,P.YELLOW)
    geo.box(m,(31,-10.5,14.0),(2.8,1.3,.10),P.GLASS)
    geo.box(m,(31,-10.7,18),(3.2,1.0,.5),P.YELLOW,yaw=.18)
    for x in (29,33):
        for z in (13.2,17):
            geo.cylinder_between(m,(x-.18,-12,z),(x+.18,-12,z),.65,10,P.BLACK)
    if detailed:
        # The folded bonnet and dangling bumper make this a crash result, not a parked car.
        geo.oriented(m,[(29.3,-11.4,17),(32.7,-11.4,17),(32.1,-9.0,18.7),(29.7,-9.4,18.4)],
                     (0,1,-.4),P.YELLOW)
        bar(m,(29.2,-12.2,18.5),(32.7,-12.7,18.1),.10,P.STEEL)
        geo.cylinder_between(m,(29.18,-11.5,15.2),(29.15,-11.5,15.2),.55,16,P.BLACK)
        geo.cylinder_between(m,(29.14,-11.5,15.2),(29.12,-11.5,15.2),.36,16,P.YELLOW)
        geo.cylinder_between(m,(28.6,-13.5,17.6),(29.0,-13.15,17.9),.65,10,P.BLACK)
        bar(m,(29.8,-10.9,13.91),(31.1,-9.9,13.91),.035,P.WHITE)
        bar(m,(31.1,-9.9,13.91),(30.7,-10.8,13.91),.035,P.WHITE)
        geo.lathe(m,(31,-10.8,14.2),[(.4,0),(.65,.25),(.4,.6)],10,P.WHITE,cap_bottom=True)
    m.verts=[(x,y+14,z-10) for x,y,z in m.verts]
    rack=b.mesh('Dummy Parts Rack','background','', 'TL_Hall')
    geo.box(rack,(33,-3.4,-6),(3,.4,14),P.DARK)
    for z in (-12,0):
        for x in (31.5,34.5):
            geo.box(rack,(x,-7.6,z),(.20,12.6,.20),P.STEEL)
    for y in (-10,-7,-4):
        geo.box(rack,(33,y,-6),(3,.18,14),P.STEEL)
        for z in (-10,-7,-4,-1):
            geo.lathe(rack,(32.6,y+.10,z),[(.32,0),(.46,.20),(.44,.65),(.25,.8)],8,P.YELLOW)
            if detailed:
                bar(rack,(33.5,y+.25,z-.5),(33.8,y+.7,z+.4),.15,P.YELLOW)
    label(rack,'parts',(31.42,0),(31.42,-12),-3.2,-1.4,(-1,0))
    rack.verts=[(x,y+7,z) for x,y,z in rack.verts]
    apron=b.mesh('East Service Apron','background','', 'TL_Hall')
    geo.box(apron,(32,-7.4,-5),(10,14.2,48),P.DARK)
    if detailed:
        for z in range(-28,20,4):
            geo.box(apron,(27.1,-.22,z),(.12,.18,1.4),P.YELLOW)

    shift=b.data['corridor']['maxX']+8-31
    for part in (m,rack,apron):
        part.verts=[(x+shift,y,z) for x,y,z in part.verts]


def south(b,detailed):
    m=b.mesh('High Speed Camera','background','', 'TL_Hall')
    for x in (-12,12):
        geo.box(m,(x,-3,-30),(.65,27,.65),P.DARK)
        for y in range(-14,10,4):
            bar(m,(x-.8,y,-30),(x+.8,y+4,-30),.07)
    geo.box(m,(0,6.2,-30),(25,.6,2),P.STEEL)
    geo.box(m,(0,4.6,-29),(3,2,2.4),P.DARK)
    geo.cylinder_between(m,(0,4.6,-27.8),(0,4.6,-26.5),.70,12,P.BLACK)
    geo.cylinder_between(m,(0,4.6,-26.48),(0,4.6,-26.45),.53,12,P.GLASS)
    label(m,'camera',(1.8,-27.75),(-1.8,-27.75),6.55,7.4,(0,1))
    door=b.mesh('Roller Door','background','', 'TL_Hall')
    geo.box(door,(0,-2,-35.5),(21,25,.6),P.DARK)
    for y in range(-14,11):
        geo.box(door,(0,y,-35.1),(18,.85,.15),geo.scale(P.WALL,.8))
    for x in (-10,10):
        geo.box(door,(x,-2,-35),(1.2,25,.3),P.YELLOW)
        for y in range(-14,11,2):
            geo.box(door,(x,y,-34.8),(1.22,.70,.10),P.BLACK)
    lights=b.mesh('Light Towers','background','', 'TL_Hall')
    lamps=b.mesh('Tower Lamps','background','', 'TL_Glow')
    for x in (-22,22):
        geo.box(lights,(x,-3,-31),(.35,26,.35),P.STEEL)
        geo.box(lights,(x,6,-31),(3.4,2.2,.65),P.DARK)
        for y in (5.5,6.5):
            for dx in (-.9,.9):
                geo.cylinder_between(lights,(x+dx,y,-30.65),(x+dx,y,-30.35),.44,12,P.BLACK)
                geo.cylinder_between(lamps,(x+dx,y,-30.34),(x+dx,y,-30.30),.34,12,P.WHITE)
