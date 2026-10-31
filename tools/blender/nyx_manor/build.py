"""Two-Faced Manor presentation. The baked shell is read, never written."""
from __future__ import annotations
import argparse,json,sys,math,random
from pathlib import Path
HERE=Path(__file__).resolve().parent
sys.path[:0]=[str(HERE),str(HERE.parent)]
import bpy
from stagekit import blender as kit,geo,checks
from stagekit.presentation import write
import layout
import parts as K
import surfaces as S
import dressing
import world
from presentation import PRESENTATION
TRIANGLE_BUDGET=180000

def clip(poly,front):
    out=[]
    for a,b in zip(poly,poly[1:]+poly[:1]):
        ina=a[2]<=12 if front else a[2]>=12; inb=b[2]<=12 if front else b[2]>=12
        if ina:out.append(a)
        if ina!=inb:
            t=(12-a[2])/(b[2]-a[2]);out.append(tuple(a[i]+t*(b[i]-a[i]) for i in range(3)))
    return out

def shell(data):
    m=K.mesh('Baked Shell Skin');rng=random.Random(194)
    for name,tris in data['collision'].items():
        cliff_name=name in ('Graveyard','GraveyardGateW','GraveyardGateE','CliffStairs')
        if name=='CliffStairs':S.cliff(m,(8,41.5,12,49,9),-12,rng,lambda x,z:5+4*(z-41.5)/7.5)
        elif cliff_name:S.cliff(m,layout.SPEC['route'][name],layout.base(name),rng)
        for t in tris:
            top=layout._normal(t)[1]>.1
            if cliff_name and not top:continue
            if name=='Roof' and abs(layout._normal(t)[2])>.9:continue
            if name=='Turret':
                for front,color in ((True,K.PALE if top else K.PINK),(False,(.19,.18,.25) if top else K.INK)):
                    p=clip(list(t),front)
                    if len(p)>2:m.poly(p,color)
            else:
                gothic=name in ('Roof','ChimneyW','ChimneyE','B1','CliffStairs') or name.startswith(('Aqueduct','SeaRock','Grave','Mausoleum','Crypt'))
                color=((.33,.34,.38) if top else (.14,.15,.20)) if gothic else ((.18,.29,.16) if top else (.075,.12,.10)) if 'Lawn' in name and 'Ramp' not in name else K.PALE if top else K.PINK
                if name=='Roof':color=(.23,.23,.31) if top else (.075,.065,.11)
                if name.startswith(('SeaRock','Aqueduct','Gravestone')):
                    S.fractured_triangle(m,t,color,rng,2 if name.startswith('SeaRock') else 1)
                else:m.poly(t,color)
    return m

def risers(detail):
    m=K.mesh('Front Stucco Relief')
    for z,y in ((-18,3.6),(-10,7.2),(-2,10.8)):
        # Trim is flush with the baked lip and has no rail or upright on it.
        K.box(m,(0,y-.13,z+.015),(32,.22,.055),K.WHITE)
        K.box(m,(0,y-3.45,z+.015),(32,.12,.055),(.38,.10,.22))
        for x in (-14,-10,-6,0,6,10,14):
            if detail:
                K.heart(m,x,y-1.65,z-.025,.68,(.72,.32,.12))
                K.heart(m,x,y-1.65,z-.04,.80,K.WHITE,True)
                for side in (-1,1):
                    K.box(m,(x+side*1.03,y-1.6,z-.03),(.36,1.6,.04),(.32,.45,.41) if x%3 else (.40,.28,.49))
                    for j in range(5):K.box(m,(x+side*1.03,y-2.18+j*.27,z-.06),(.33,.035,.025),(.55,.58,.52))
        if detail:
            for x in (-15.6,-7.7,7.7,15.6):
                pts=[(x+.12*math.sin(i),y-3.3+i*.19,z-.065) for i in range(16)]
                geo.tube(m,pts,.035,5,K.GREEN)
                for i,p in enumerate(pts[::2]):
                    K.ball(m,(p[0]+(-1)**i*.18,p[1],p[2]),(.18,.07,.04),K.GREEN)
                    if i%2==0:K.ball(m,(p[0],p[1],p[2]-.04),(.12,.12,.075),K.HOT)
    # Marble veining and stair nosings lie on the exact continuous ramp plane.
    for name,(axis,x0,z0,x1,z1,a,b,_) in layout.SPEC['ramps'].items():
        if name=='CliffStairs':continue
        for i in range(1,19):
            t=i/19; u=t+.012
            if axis=='x':pts=[(x0+(x1-x0)*t,a+(b-a)*t+.008,z0),(x0+(x1-x0)*u,a+(b-a)*u+.008,z0),(x0+(x1-x0)*u,a+(b-a)*u+.008,z1),(x0+(x1-x0)*t,a+(b-a)*t+.008,z1)]
            else:pts=[(x0,a+(b-a)*t+.008,z0+(z1-z0)*t),(x0,a+(b-a)*u+.008,z0+(z1-z0)*u),(x1,a+(b-a)*u+.008,z0+(z1-z0)*u),(x1,a+(b-a)*t+.008,z0+(z1-z0)*t)]
            geo.oriented(m,pts,(0,1,0),(.81,.56,.61))
    if detail:
        for z,y in ((-18,3.6),(-10,7.2),(-2,10.8)):
            for x in range(-14,16,4):
                pts=[(x+i*.65,y+.012,z+.3+i*.06+.10*math.sin(i)) for i in range(5)]
                geo.tube(m,pts,.012,4,(.50,.29,.34))
    return m

def back(detail):
    m=K.mesh('Gothic Masonry');rng=random.Random(18)
    for name in ('B1','Turret','ChimneyW','ChimneyE','Crypt','MausoleumW','MausoleumE'):
        x0,z0,x1,z1,y=layout.SPEC['route'][name];bottom=layout.base(name)
        for h in range(math.ceil(bottom),math.ceil(y)):
            for x in range(math.floor(x0),math.ceil(x1),2):
                xx=min(x1-.10,x+1+(h%2)*.6)
                if xx<=x0:continue
                w=min(1.9,2*(x1-xx)-.02,2*(xx-x0)-.02)
                row0=max(bottom,h+.02);row1=min(y-.02,h+.90)
                if row1<=row0:continue
                K.box(m,(xx,(row0+row1)/2,z1+.012),(w,row1-row0,.03),geo.scale(K.STONE,rng.uniform(.55,.8)))
        if detail and name in ('Turret','Crypt'):
            K.gothic(m,(x0+x1)/2,y-2.7,z1+.05,1.5,2.25,False)
    if detail:
        for x in (-13,-6,6,13):K.gothic(m,x,11.05,18.04,1.4,2.95,False)
        for x in (-14,-10,-6,0,6,10,14):K.gothic(m,x,11.15,5.98,.92,2.65)
        for y in (11.15,14.15):K.box(m,(0,y,5.985),(32,.13,.025),(.23,.21,.29))
        for x in (-19.5,19.5):K.rod(m,(x,10.8,21),(x,11.12,21),.035,K.INK)
        for x in (-19,19):K.crow(m,(x,10.8,21))
    return m

def graveyard(detail):
    m=K.mesh('Ravine and Graveyard');rng=random.Random(77)
    for name in ('Aqueduct1','Aqueduct2','Aqueduct3'):
        x0,z0,x1,z1,y=layout.SPEC['route'][name]
        for x in (x0-.025,x1+.025):
            pts=[]
            for i in range(17):
                t=i/16;pts.append((x,y-6+3*math.sqrt(max(0,1-(2*t-1)**2)),z0+.75+(z1-z0-1.5)*t))
            geo.tube(m,pts,.13,5,(.28,.25,.36))
            for z in (z0+.3,z1-.3):
                for h in (-10,-7,-4,-1,2,5):K.box(m,(x,h,z),(.04,.06,.58),(.20,.17,.27))
        for x,d in ((x0-.025,-1),(x1+.025,1)):
            for i in range(10):
                t0=i/10;t1=(i+.88)/10
                a=z0+.75+(z1-z0-1.5)*t0;b=z0+.75+(z1-z0-1.5)*t1
                h0=y-6+3*math.sqrt(max(0,1-(2*t0-1)**2));h1=y-6+3*math.sqrt(max(0,1-(2*t1-1)**2))
                geo.oriented(m,[(x,h0,a),(x,h0+.43,a),(x,h1+.43,b),(x,h1,b)],(d,0,0),geo.jitter((.32,.31,.37),rng,.22))
            for z in (z0+.35,z1-.35):
                for h in range(-11,8):
                    geo.oriented(m,[(x,h,z-.31),(x,h+.88,z-.31),(x,h+.88,z+.31),(x,h,z+.31)],(d,0,0),geo.jitter((.20,.21,.27),rng,.3))
        for z in range(math.ceil(z0),math.floor(z1)):
            K.box(m,(0,y+.008,z),(4.8,.016,.025),(.19,.18,.25))
    for name in ('SeaRock1','SeaRock2','SeaRock3','SeaRock4'):
        x0,z0,x1,z1,y=layout.SPEC['route'][name]
        for i in range(7):
            x=rng.uniform(x0+.15,x1-.3);z=rng.uniform(z0+.15,z1-.3)
            geo.oriented(m,[(x,y+.01,z),(x+.22,y+.01,z+.15),(x+.15,y+.01,z+.34)],(0,1,0),(.21,.23,.27))
    for i in range(1,24):
        z=41.5+7.5*i/24;y=5+4*i/24
        K.box(m,(10,y+.008,z),(3.96,.016,.055),(.42,.43,.46))
        for x in (8.10,11.90):K.box(m,(x,y-.08,z),(.14,.14,.18),(.25,.27,.31))
    for name in ('B1','Aqueduct1','Aqueduct2','Aqueduct3','Graveyard','GraveyardGateW','GraveyardGateE','MausoleumW','MausoleumE','Crypt'):
        x0,z0,x1,z1,y=layout.SPEC['route'][name]
        S.paving(m,x0,z0,x1,z1,y,rng)
    if not detail:return m
    K.box(m,(0,10.35,61.965),(2.3,2.7,.04),K.INK)
    for x in (-.95,-.63,-.31,0,.31,.63,.95):K.rod(m,(x,9.1,61.92),(x,11.57,61.92),.035,(.35,.29,.36))
    K.gothic(m,0,9.08,61.89,2.45,2.75)
    # Iron bars sit in front of the candle pane, keeping the door visibly closed.
    for x in (-.95,-.63,-.31,0,.31,.63,.95):K.rod(m,(x,9.1,61.70),(x,11.57,61.70),.04,K.INK)
    for x,z,y in ((-2.48,25,8.7),(2.48,33,8.7),(-3,65.7,11.8)):
        pts=[(x+.06*math.sin(i),y-i*.18,z+.04) for i in range(11)]
        geo.tube(m,pts,.025,5,K.INK)
    return m

def turret(detail):
    m=K.mesh('Split Turret Details')
    # Roof tiles painted on the upper turret faces preserve its approved rectangular shell.
    for side in (-1,1):
        for y in (19.4,20,20.6,21.2,21.8):
            for z in (9.3,10,10.7,11.4,12.1,12.8,13.5,14.2):K.box(m,(side*3.015,y,z),(.025,.51,.63),K.INK)
    K.box(m,(0,21,8.985),(6,2,.03),K.INK)
    # Slate roof bands follow the flat approved roof and its back fascia.
    for z in (6.3,7.2,8.1,9,9.9,10.8,11.7,12.6,13.5,14.4,15.3,16.2,17.1,17.7):
        K.box(m,(0,14.409,z),(32,.012,.035),(.10,.09,.16))
    for x in range(-16,17,2):
        K.box(m,(x,14.41,12),(.025,.01,12),(.12,.10,.18))
    for z in (9.4,10.2,11,11.8,12.6,13.4,14.2):
        K.box(m,(0,22.008,z),(6,.016,.045),K.INK)
    K.ring(m,(0,18.5,8.88),1.35,(.95,.63,.57),.11)
    K.rod(m,(0,14.4,8.88),(0,17.1,8.88),.055,K.INK)
    if detail:
        # Gable relief and gargoyles are confined to existing solid chimney footprints.
        for x in (-9.2,9.2):
            geo.oriented(m,[(x-.7,15.0,11.17),(x,16.05,11.17),(x+.7,15.0,11.17)],(0,0,-1),(.16,.14,.22))
            K.box(m,(x,16.08,12),(1.3,.14,1.3),(.14,.12,.19))
        K.rod(m,(0,22,12),(0,23.2,12),.035,K.INK)
        K.rod(m,(-.7,22.6,12),(.7,22.6,12),.025,K.INK)
        K.crow(m,(0,23.1,12),.65)
        for x in (-9.2,9.2):
            K.gothic(m,x,14.55,11.16,.65,1.15)
            K.bat(m,(x,16.48,12),.32)
    return m

def garden():
    m=K.mesh('Nyxxie Garden')
    # Side planting is well away from every target and route. No tier-front props.
    for side in (-1,1):
        for z in range(-43,-19,3):
            x=side*22.8
            K.ball(m,(x,.5,z),(.6,.55,1),K.GREEN)
            for a in (-.3,.25):K.ball(m,(x+a,.92,z),(.16,.16,.16),K.HOT)
        for z in (-42,-33,-24):
            x=side*23.4
            K.rod(m,(x,0,z),(x,2.6,z),.055,K.INK)
            K.box(m,(x,2.4,z),(.35,.55,.35),(.53,.26,.08))
        for z in range(-44,-20,2):K.rod(m,(side*23.8,0,z),(side*23.8,.85,z),.025,K.INK)
        for h in (.3,.65):K.rod(m,(side*23.8,h,-44),(side*23.8,h,-20),.025,K.INK)
    # The small side-garden arch stays outside the start camera and on the lawn.
    for side in (-1,1):
        for i in range(13):
            a=(i/12)*math.pi/2
            x=-20+side*(1.35*math.cos(a)); y=.2+1.6*math.sin(a)
            K.ball(m,(x,y,-43),(.14,.15,.12),K.PINK if i%2 else K.PALE)
    K.heart(m,-20,1.8,-43.02,.22,K.HOT)
    # Tiny paddling pool has a low soft rim, clearly a prop rather than a platform.
    K.ring(m,(19,.14,-36),1.25,K.HOT,.14,'xz',None)
    K.box(m,(19,.06,-36),(1.7,.03,1.7),(.13,.30,.39))
    for side in (-1,1):
        x=side*20;z=-28
        K.box(m,(x,.32,z),(.75,.07,1.5),K.PINK)
        K.box(m,(x,.72,z+.55),(.75,.85,.08),K.PINK)
        lounger=K.mesh('Lounge Skeleton')
        K.skeleton(lounger,s=.65)
        # Recline the toy against the chair, not a standing duplicate of a live target.
        lounger.verts=[(x+a,.38+b*.6,z-.50+b*.65+c) for a,b,c in lounger.verts]
        m.extend(lounger)
    # Dress existing quick blocks with ribbons or planter face motifs, never grow their tops.
    for name in ('BoxW','BoxE','EndBoxW','EndBoxE','LedgeW','LedgeE','DormerW','DormerE'):
        x0,z0,x1,z1,y=layout.SPEC['route'][name]; x=(x0+x1)/2
        if name.startswith(('EndBox','Ledge')):
            # Low clipped hedge crowns stay on supported quick-step tops.
            for dx in (-.35,0,.35):K.ball(m,(x+dx,y-.08,(z0+z1)/2),(.30,.13,.42),K.GREEN)
            K.box(m,(x,y-.25,z0-.025),(x1-x0,.14,.03),K.WHITE)
        K.box(m,(x,y-.8,z0-.025),(.14,1.5,.025),K.WHITE)
        K.heart(m,x,y-.45,z0-.05,.25,K.HOT,True)
        for side in (-1,1):
            K.ring(m,(x+side*.18,y-.23,z0-.05),.17,K.WHITE,.035,mat=None)
    return m

def main():
    ap=argparse.ArgumentParser();ap.add_argument('--out',required=True);ap.add_argument('--report',required=True);ap.add_argument('--rough',action='store_true')
    args=ap.parse_args(sys.argv[sys.argv.index('--')+1:]);data=layout.load();detail=not args.rough
    groups={m.name:m for m in (shell(data),risers(detail),back(detail),graveyard(detail),turret(detail),world.scenery(detail),world.sea(),world.sky())}
    if detail:
        for m in (garden(),dressing.graveyard(),dressing.manor(),dressing.front()):groups[m.name]=m
    bg=groups['Outside Blast Scenery']; violations=[i for i,f in enumerate(bg.faces) if checks.intersects_corridor([bg.verts[j] for j in f],data['corridor'])]
    if violations:raise ValueError(f'Background in blast corridor: {violations[:8]}')
    triangles=sum(m.triangle_count for m in groups.values())
    if triangles>TRIANGLE_BUDGET:raise ValueError(f'Triangle budget exceeded: {triangles}')
    materials=K.MATERIALS+('MoonHalo','MoonSurface','ManorWater','NightSky','CandleGlow')
    kit.reset_scene(materials); objects=kit.build_objects(groups,smooth=(),channels=True)
    digest,digests=kit.digest(objects);out=Path(args.out);out.mkdir(parents=True,exist_ok=True)
    kit.export_fbx(objects,str(out/'NyxManor.fbx'))
    world.textures(out)
    halo=bpy.data.images.new('MoonHalo',width=256,height=256,alpha=True)
    pixels=[]
    for y in range(256):
        for x in range(256):
            r=math.hypot((x-127.5)/127.5,(y-127.5)/127.5)
            alpha=.55*math.exp(-4*r*r)*max(0,min(1,(1-r)*12))
            pixels.extend((.45,.52,.75,alpha))
    halo.pixels.foreach_set(pixels);halo.filepath_raw=str(out/'MoonHalo.png');halo.file_format='PNG';halo.save()
    stamp={k:data[k] for k in ('source','sourceSha256','arena','arenaSha256')}
    stamp.update(generator='tools/blender/nyx_manor/build.py',geometryDigest=digest,blender=bpy.app.version_string,objects=sorted(objects),materials=materials,problems=0)
    (out/'NyxManor.layout.json').write_text(json.dumps(stamp,indent=2)+'\n');write(PRESENTATION,str(out))
    report=Path(args.report);report.mkdir(parents=True,exist_ok=True)
    (report/'build-report.json').write_text(json.dumps(dict(stamp,triangles=triangles,budget=TRIANGLE_BUDGET,backgroundCorridorViolations=len(violations),objectTriangles={n:m.triangle_count for n,m in groups.items()},objectDigests=digests,meshHealth=kit.mesh_health(objects)),indent=2)+'\n')
    print('NYX_MANOR_ART',triangles,'triangles',digest)
if __name__=='__main__':main()
