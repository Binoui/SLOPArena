#!/usr/bin/env python3
"""Build Nyx's volumetric comic OBJ meshes and band palette without operating Unity.

Run from any cwd. Unity's NyxPresentationBuilder assembles these into separate prefabs.
All geometry is in metres about the capsule centre unless its name denotes a floor mark.
"""
from pathlib import Path
from collections import Counter
import json, math, random
from PIL import Image

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'client/Unity/Assets/CharacterPackages/nyx/Presentation/Source'
INK='#160D20'; DARK='#2C143E'; VIOLET='#5B2A86'; PALE='#D9C8F0'
COLORS=[INK,DARK,VIOLET,PALE,INK,'#92234F','#FF4FA3','#FFFFFF']

class Mesh:
    def __init__(self): self.v=[];self.f=[];self.uv=[]
    def tri(self,a,b,c,band):
        n=len(self.v);self.v += [a,b,c];self.uv += [((band+.5)/8,.5)]*3;self.f.append((n+1,n+2,n+3))
    def poly(self, points, band):
        # Ear clipping preserves concave claws, hands and feathers.
        points=list(points); n=len(points)
        if n<3:return
        def cross(a,b,c):return (b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0])
        area=sum(points[i][0]*points[(i+1)%n][1]-points[(i+1)%n][0]*points[i][1] for i in range(n))
        idx=list(range(n)) if area>0 else list(reversed(range(n)))
        while len(idx)>3:
            for k,b in enumerate(idx):
                a,c=idx[k-1],idx[(k+1)%len(idx)]
                if cross(points[a],points[b],points[c])<=1e-9:continue
                if any(all(z>=-1e-9 for z in (cross(points[a],points[b],points[j]),cross(points[b],points[c],points[j]),cross(points[c],points[a],points[j]))) for j in idx if j not in (a,b,c)):continue
                self.tri(points[a],points[b],points[c],band);idx.pop(k);break
            else: raise ValueError('Non-simple polygon')
        self.tri(*(points[i] for i in idx),band)
    def shape(self,xy,at=(0,0,0),size=(1,1),fake=False,rotation=0,depth=None,fold=0,hollow=False,bands=(0,1,2,3)):
        """Closed beveled silhouette: palette bands are facets, never stacked cards.

        Both faces carry the same bands. The waist has thickness, so a side view
        still shows a coloured solid. Fold bends wings/fingers out of the XY plane.
        """
        ca,sa=math.cos(rotation),math.sin(rotation)
        depth=depth if depth is not None else min(size)*.65
        area=sum(xy[i][0]*xy[(i+1)%len(xy)][1]-xy[(i+1)%len(xy)][0]*xy[i][1] for i in range(len(xy)))
        xy=list(xy) if area>0 else list(reversed(xy))
        def ring(scale,z):
            return [(at[0]+(x*size[0]*ca-y*size[1]*sa)*scale,
                     at[1]+(x*size[0]*sa+y*size[1]*ca)*scale,
                     at[2]+z+fold*abs(x*size[0]*scale)+.08*depth*y*scale) for x,y in xy]
        def quad(a,b,c,d,band):
            self.tri(a,b,c,band);self.tri(a,c,d,band)
        offset=4 if fake else 0
        # The side wall carries the accent, framed by ink on each bevel.
        back,front=ring(1,-depth*.18),ring(1,depth*.18)
        for j in range(len(xy)):
            k=(j+1)%len(xy);quad(back[j],back[k],front[k],front[j],offset+2)
        for sign in (-1,1):
            rings=[ring(scale,sign*depth*z) for scale,z in ((1,.18),(.88,.32),(.78 if hollow else .71,.44),(.64 if hollow else .40,.50))]
            for band in range(3):
                outer,inner=rings[band:band+2]
                for j in range(len(xy)):
                    k=(j+1)%len(xy)
                    points=[outer[j],outer[k],inner[k],inner[j]]
                    if sign<0:points.reverse()
                    quad(*points,offset+bands[band])
            # Ear clipping uses XY; reversing only triangle winding preserves concavity.
            if not hollow:
                first=len(self.f);self.poly(rings[-1],offset+bands[3])
                if sign<0:
                    self.f[first:]=[tuple(reversed(f)) for f in self.f[first:]]
        if hollow:
            # A closed inner wall leaves genuine negative space through the palm
            # or fist. Outer dimensions, placement and depth remain unchanged.
            back,front=ring(.64,-depth*.5),ring(.64,depth*.5)
            for j in range(len(xy)):
                k=(j+1)%len(xy);quad(front[j],front[k],back[k],back[j],offset+3)
    def ribbon(self,points,width,fake=False,side=(0,1,0),taper=True,depth_ratio=.7):
        """Sweep a closed 16-facet band around a 3D path with a real cross section."""
        def norm(v):
            d=math.sqrt(sum(x*x for x in v));return tuple(x/d for x in v)
        def cross(a,b):return (a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0])
        rings=[];n=len(points);segments=16
        for j,p in enumerate(points):
            before,after=points[max(0,j-1)],points[min(n-1,j+1)]
            tangent=norm(tuple(after[k]-before[k] for k in range(3)))
            dot=sum(side[k]*tangent[k] for k in range(3))
            across=norm(tuple(side[k]-dot*tangent[k] for k in range(3)))
            deep=norm(cross(tangent,across));t=j/(n-1)
            w=width*(max(.06,max(0,math.sin(math.pi*t))**.4) if taper else 1)
            if not fake:w*=1+.16*math.sin(j*8.2)
            rings.append([tuple(p[k]+w*(across[k]*math.cos(a)+deep[k]*math.sin(a)*depth_ratio) for k in range(3)) for a in (i*math.tau/segments for i in range(segments))])
        for j in range(n-1):
            for i in range(segments):
                k=(i+1)%segments
                # Pale crest on both broad faces; saturated flank, narrow ink edges.
                c=abs(math.cos((i+.5)*math.tau/segments))
                band=0 if c>.96 else 1 if c>.82 else 2 if c>.42 else 3
                band+=4 if fake else 0
                a,b,c,d=rings[j][i],rings[j][k],rings[j+1][k],rings[j+1][i]
                self.tri(a,b,c,band);self.tri(a,c,d,band)
        for index,reverse in ((0,True),(n-1,False)):
            for i in range(segments):
                pts=[points[index],rings[index][i],rings[index][(i+1)%segments]]
                if reverse:pts.reverse()
                self.tri(*pts,4 if fake else 0)
    def stroke(self,points,widths,deep=(0,0,1),fake=False,thickness=.05,torn=None,fractions=(1,.86,.68,.32),teeth=0):
        """A flat comic stroke along a 3D path: ink edges, dark, accent and a pale core line.

        Bands run across the width, not toward a centre, so crescents and wedges keep a
        clean core. `deep` is the stroke's face normal (a constant or a function of t);
        `widths` gives the half-width per point. The volume is closed by end fans.
        """
        def norm(v):
            d=math.sqrt(sum(x*x for x in v)) or 1e-9;return tuple(x/d for x in v)
        def cross(a,b):return (a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0])
        n=len(points);torn=(not fake) if torn is None else torn
        offset=4 if fake else 0
        loops=[]
        for j,p in enumerate(points):
            t=j/(n-1)
            before,after=points[max(0,j-1)],points[min(n-1,j+1)]
            tangent=norm(tuple(after[k]-before[k] for k in range(3)))
            d=deep(t) if callable(deep) else deep
            across=norm(cross(d,tangent));face=norm(cross(tangent,across))
            w=max(.004,widths[j])*(1+.14*math.sin(j*8.2) if torn else 1)
            # Torn flame edge: every few points the outer edge spikes out, like the concepts' ripped shadow.
            spike=1+(teeth if teeth and j%3==1 and 0<j<n-1 else 0)
            h=thickness*min(1,.3+w/(max(widths) or 1))
            zs=(.36,.62,.86,1)
            def at(f,z):return tuple(p[k]+w*(f if f<1 else spike)*across[k]+h*z*face[k] for k in range(3))
            def bt(f,z):return tuple(p[k]-w*f*across[k]+h*z*face[k] for k in range(3))
            fr=fractions
            loop=[at(fr[0],zs[0]),at(fr[1],zs[1]),at(fr[2],zs[2]),at(fr[3],zs[3]),
                  bt(fr[3],zs[3]),bt(fr[2],zs[2]),bt(fr[1],zs[1]),bt(fr[0],zs[0]),
                  bt(fr[0],-zs[0]),bt(fr[1],-zs[1]),bt(fr[2],-zs[2]),bt(fr[3],-zs[3]),
                  at(fr[3],-zs[3]),at(fr[2],-zs[2]),at(fr[1],-zs[1]),at(fr[0],-zs[0])]
            loops.append(loop)
        # Segment bands around the loop: both faces ink->dark->accent->core, ink side walls.
        seg=[0,1,2,3,2,1,0,0,1,2,3,2,1,0,0,0]
        for j in range(n-1):
            for i in range(16):
                k=(i+1)%16;band=offset+seg[i]
                a,b,c,d=loops[j][i],loops[j][k],loops[j+1][k],loops[j+1][i]
                self.tri(a,c,b,band);self.tri(a,d,c,band)
        for index,reverse in ((0,False),(n-1,True)):
            centre=points[index]
            for i in range(16):
                pts=[centre,loops[index][i],loops[index][(i+1)%16]]
                if reverse:pts.reverse()
                self.tri(*pts,offset)
    def save(self,name):
        size=[max(v[k] for v in self.v)-min(v[k] for v in self.v) for k in range(3)]
        if min(size)<.02:raise ValueError(f'{name}: effect collapsed to a card: {size}')
        # Compare coordinates as written to OBJ, since the mesh intentionally splits
        # vertices at palette bands and hard normals. Every component must be closed.
        edges=Counter()
        for face in self.f:
            for i in range(3):
                a,b=(tuple(round(x,7) for x in self.v[face[j]-1]) for j in (i,(i+1)%3))
                edges[tuple(sorted((a,b)))]+=1
        if any(count==1 for count in edges.values()):raise ValueError(f'{name}: open volume')
        p=OUT/(name+'.obj')
        lines=['# Generated by tools/nyx/build_paper_assets.py. Metres.','o '+name]
        lines += ['v '+' '.join(f'{x:.7f}' for x in v) for v in self.v]
        lines += ['vt '+' '.join(f'{x:.7f}' for x in v) for v in self.uv]
        normals=[]
        for face in self.f:
            a,b,c=[self.v[j-1] for j in face];u=[b[k]-a[k] for k in range(3)];v=[c[k]-a[k] for k in range(3)]
            n=(u[1]*v[2]-u[2]*v[1],u[2]*v[0]-u[0]*v[2],u[0]*v[1]-u[1]*v[0]);length=math.sqrt(sum(x*x for x in n))
            n=tuple(x/length for x in n) if length>1e-12 else (0,0,1);normals += [n]*3
        lines += ['vn '+' '.join(f'{x:.7f}' for x in n) for n in normals]
        lines += ['f '+' '.join(f'{j}/{j}/{j}' for j in f) for f in self.f]
        p.write_text('\n'.join(lines)+'\n');return {'asset':str(p.relative_to(ROOT/'client/Unity')),'vertices':len(self.v),'triangles':len(self.f),'sizeMeters':[round(x,7) for x in size]}

def arc(m,r,y,z=0,fake=False,start=0,end=math.tau,width=.16):
    m.ribbon([(r*math.sin(a),y+(.11 if fake else .14)*math.sin(a*(3 if fake else 9)),z+r*math.cos(a)) for a in [start+(end-start)*j/72 for j in range(73)]],width,fake,side=(0,1,0),taper=False)
def crescent(n,peak,power=.8):
    """Half-widths for a comic slash: pointed tips, fullest mid-stroke."""
    return [peak*max(0,math.sin(math.pi*k/(n-1)))**power for k in range(n)]
def burst(n=9,seed=1,inner=.42,jag=.32,twist=.1):
    """An uneven starburst outline, like the concepts' inked flashes."""
    rnd=random.Random(seed);pts=[]
    for i in range(n*2):
        a=i*math.pi/n+rnd.uniform(-twist,twist)
        r=rnd.uniform(1-jag,1.0) if i%2==0 else rnd.uniform(inner*.85,inner*1.15)
        pts.append((math.cos(a)*r,math.sin(a)*r))
    return pts
def floor(m,first):
    """Lay the vertices added since `first` from the XY plane onto the floor (XZ)."""
    for j in range(first,len(m.v)):
        x,y,z=m.v[j];m.v[j]=(x,z,y)
    m.f[first//3:]=[tuple(reversed(f)) for f in m.f[first//3:]]
def teeth(m,count,radius,height,seed,fake=False,y=0):
    """Upright torn spikes around a floor mark, readable from the low gameplay camera."""
    rnd=random.Random(seed)
    for i in range(count):
        a=i*math.tau/count+rnd.uniform(-.15,.15);r=radius*rnd.uniform(.9,1.05);h=height*rnd.uniform(.6,1.15)
        first=len(m.v)
        m.shape([(-.11,0),(-.04,.25),(.01,1),(.05,.22),(.12,0)],(0,0,0),(1,h),fake=fake,depth=.09)
        for j in range(first,len(m.v)):
            x,yy,z=m.v[j];m.v[j]=(r*math.cos(a)+x*math.sin(a)+z*math.cos(a),y+yy,r*math.sin(a)-x*math.cos(a)+z*math.sin(a))
def wisps(poly,below=.45,amount=.16,seed=4):
    """Shadow-flame edges: split each edge below `below` and push its midpoint outward into a spike."""
    rnd=random.Random(seed);out=[];cx=sum(x for x,_ in poly)/len(poly);cy=sum(y for _,y in poly)/len(poly)
    for i,(x,y) in enumerate(poly):
        out.append((x,y));nx,ny=poly[(i+1)%len(poly)]
        if (y+ny)/2<below:
            mx,my=(x+nx)/2,(y+ny)/2;dx,dy=mx-cx,my-cy;d=math.hypot(dx,dy) or 1
            k=amount*rnd.uniform(.6,1.3);out.append((mx+dx/d*k,my+dy/d*k-k*.6))
    return out
def star(n=10):return [(math.cos(i*math.pi/n)*(.95 if i%2==0 else .35),math.sin(i*math.pi/n)*(.95 if i%2==0 else .35)) for i in range(n*2)]
HAND=[(-.32,-.5),(-.43,-.1),(-.7,.22),(-.68,.45),(-.48,.39),(-.28,.17),(-.34,.72),(-.24,.97),(-.13,.85),(-.09,.38),(-.05,1.12),(.07,1.22),(.17,1.06),(.12,.42),(.26,1.02),(.4,1.05),(.43,.85),(.29,.26),(.48,.71),(.63,.75),(.67,.55),(.43,-.12),(.27,-.5)]
FIST=[(-.38,-.4),(-.54,-.15),(-.58,.22),(-.46,.5),(-.22,.53),(-.12,.62),(.09,.59),(.25,.54),(.45,.48),(.56,.25),(.5,-.14),(.29,-.4)]
# Knuckles down: wrist on top, four knuckle bumps along the bottom, thumb across the front.
PUNCH=[(-.36,.62),(.36,.62),(.42,.25),(.56,.1),(.58,-.2),(.5,-.42),(.38,-.52),(.27,-.43),(.14,-.56),(0,-.47),(-.14,-.57),(-.27,-.45),(-.4,-.54),(-.53,-.36),(-.58,-.08),(-.46,.2),(-.42,.3)]
FEATHER=[(-.1,-.9),(-.22,-.2),(-.38,.2),(-.2,.12),(-.35,.5),(-.12,.4),(-.22,.7),(.03,1),(.24,.65),(.18,.15),(.05,-.35)]
BAT=[(-.9,.15),(-.58,.12),(-.78,.55),(-.35,.36),(-.16,.08),(-.13,.3),(0,.16),(.13,.3),(.16,.08),(.35,.36),(.78,.55),(.58,.12),(.9,.15),(.64,-.19),(.41,-.04),(.21,-.26),(0,-.13),(-.21,-.26),(-.41,-.04),(-.64,-.19)]
HEART=[(0,-.9),(-.76,-.12),(-.85,.37),(-.64,.73),(-.22,.78),(0,.5),(.22,.78),(.64,.73),(.85,.37),(.76,-.12)]
WING=[(0,0),(-.45,.02),(-.9,-.12),(-1,-.01),(-.77,.19),(-1,.14),(-.63,.45),(-.84,.43),(-.36,.68),(.08,.28)]

def build():
    OUT.mkdir(parents=True,exist_ok=True)
    palette=Image.new('RGBA',(8,1));palette.putdata([tuple(bytes.fromhex(c[1:]))+(255,) for c in COLORS]);palette.save(OUT/'NyxPaperPalette.png')
    meshes={}
    def emit(name,fn):
        m=Mesh();fn(m);meshes[name]=m.save(name)
    def claws(m):
        # Three parallel claw marks raking down across the target, drawn face-on.
        n=25
        for j,off in enumerate((-.25,0,.25)):
            pts=[(-.72+1.44*t+off*.25,.32-.64*t+off+.24*math.sin(math.pi*t),0) for t in (k/(n-1) for k in range(n))]
            m.stroke(pts,crescent(n,.1 if j==1 else .085,.75),thickness=.05,teeth=.9)
    emit('NyxShadowClaws',claws)
    # The builder places two independently yaw-facing palms at the original centres.
    emit('NyxRisingHands',lambda m:m.shape(wisps(HAND),(0,.55,.55),(.45,.56),depth=.34,fold=.3))
    def lash(m):
        n=61
        m.stroke([(.2*math.sin(t*math.pi*2.5),-.76+.04*math.sin(t*math.pi*4),.7+3.3*t) for t in (k/(n-1) for k in range(n))],
                 [.18*min(1,t*7)*(1-t)**.55+.004 for t in (k/(n-1) for k in range(n))],deep=(0,1,0),thickness=.05,teeth=.8)
        # Upright torn edges read above the floor from behind while staying inside the capsule.
        for i in range(7):
            z=1+i*.46;x=.2*math.sin((z-.7)/3.3*math.pi*2.5);first=len(m.v)
            m.shape([(-.55,0),(-.18,.3),(.05,1),(.14,.26),(.55,0)],(0,0,0),(.22,.42*(1-i*.08)),depth=.07)
            turn=math.radians(45 if i%2 else -45);c,sn=math.cos(turn),math.sin(turn)
            for j in range(first,len(m.v)):
                vx,vy,vz=m.v[j];m.v[j]=(x+vx*c+vz*sn,-.78+vy,z-vx*sn+vz*c)
    emit('NyxFloorLash',lash)
    def ring(m):
        n=73;a0,a1=.5,math.tau-.1
        m.stroke([(1.2*math.sin(a),.1*math.sin(a*3),1.2*math.cos(a)) for a in (a0+(a1-a0)*k/(n-1) for k in range(n))],
                 crescent(n,.22,.55),deep=(0,1,0),thickness=.06,teeth=.8)
    emit('NyxShadowRing',ring)
    def upclaw(m):
        n=25
        for j,off in enumerate((-.3,0,.3)):
            m.stroke([(off*(1+.7*t)+.14*math.sin(math.pi*t),.45+1.45*t,0) for t in (k/(n-1) for k in range(n))],
                     crescent(n,.1 if j==1 else .085,.75),thickness=.05,teeth=.9)
    emit('NyxUpClaw',upclaw)
    def fist(m):
        m.shape(PUNCH,(0,-.8,.25),(.62,.62),depth=.3)
        n=13
        for x in (-.3,0,.3):
            m.stroke([(x*(1-.55*t),-1.2-.65*t,.25) for t in (k/(n-1) for k in range(n))],
                     [.004+.1*(1-t)**.8 for t in (k/(n-1) for k in range(n))],thickness=.05)
    emit('NyxSpikeFist',fist)
    emit('NyxGraspHand',lambda m:m.shape(wisps(HAND,seed=6),(0,.72,0),(.85,1.12),depth=.6,fold=.32))
    def warning(m):
        arc(m,.8,.015,width=.08)
        for i in range(6):
            a=i*math.tau/6
            # Upright torn crown teeth keep the warning legible at the 0-degree limit.
            first=len(m.v);m.shape([(-.13,0),(.0,.34),(.13,0)],(.0,.08,0),depth=.14)
            for j in range(first,len(m.v)):
                x,y,z=m.v[j];m.v[j]=(.7*math.cos(a)+x*math.sin(a)+z*math.cos(a),y,.7*math.sin(a)-x*math.cos(a)+z*math.sin(a))
            p=[(.15*math.cos(a),.022,.15*math.sin(a)),(.72*math.cos(a+.16),.022,.72*math.sin(a+.16))];m.ribbon(p,.027,side=(0,1,0),taper=False)
    emit('NyxGraspWarning',warning)
    def tendril(m):
        # Shadow tendrils from the caught fighter back to her hand; the presenter stretches z to the distance.
        n=25
        for j,(x,w,sag) in enumerate(((0,.075,.18),(-.22,.04,.1),(.22,.04,.26))):
            m.ribbon([(x*(1-t)+.05*math.sin(t*math.pi*3+j),.12-sag*math.sin(math.pi*t),t) for t in (k/(n-1) for k in range(n))],w,side=(0,1,0))
    emit('NyxGraspDrag',tendril)
    emit('NyxBat',lambda m:m.shape(BAT,size=(.24,.24),depth=.12,fold=.65,bands=(0,0,1,2)))
    emit('NyxFeather',lambda m:m.shape(FEATHER,size=(.18,.26),depth=.1,fold=.32))
    emit('NyxHeart',lambda m:m.shape(HEART,size=(.12,.12),fake=True,depth=.13))
    emit('NyxSparkle',lambda m:m.shape([(0,1),(.15,.18),(.7,0),(.15,-.18),(0,-1),(-.15,-.18),(-.7,0),(-.15,.18)],size=(.13,.13),fake=True,depth=.17))
    emit('NyxShadowShard',lambda m:m.shape([(-.14,-1),(-.46,.21),(.15,1),(.34,.13),(.1,-.16)],size=(.22,.52),depth=.18,fold=.5))
    def upper(m):
        n=49
        # Faced halfway between her side and the camera behind her, so neither view sees it edge-on.
        m.stroke([(0,-.1+1.3*t,.75+.45*math.sin(math.pi*t)) for t in (i/(n-1) for i in range(n))],crescent(n,.3,.7),deep=(.7,0,-.7),fake=True,thickness=.06)
    emit('NyxPoleUpperArc',upper)
    def overhead(m):
        n=49
        m.stroke([(-.95+1.9*t,.9+.3*math.sin(math.pi*t),.25+.38*math.sin(math.pi*t)) for t in (i/(n-1) for i in range(n))],
                 crescent(n,.24,.7),deep=(0,.6,-.8),fake=True,thickness=.06)
    emit('NyxPoleOverheadArc',overhead)
    def spin(m):
        n=61;a0,a1=.4,math.tau-.2
        m.stroke([(.95*math.sin(a),.1+.08*math.sin(a*3),.95*math.cos(a)) for a in (a0+(a1-a0)*k/(n-1) for k in range(n))],
                 crescent(n,.15,.55),deep=(0,1,0),fake=True,thickness=.05)
    emit('NyxSpinArc',spin)
    def drop(m):
        for x in (-.2,.2):m.shape([(0,-1),(-.2,.25),(.1,.65),(.26,.28)],(x,-.64,.48),(.4,.65),True)
    emit('NyxDropkickSpike',drop)
    def cone(m):
        # The ring light's hoop and nine wedge rays on a narrow cone. Gaps between the
        # rays keep the dazzled target visible from behind.
        m.stroke([(.2*math.cos(a),.1+.2*math.sin(a),.62) for a in (i*math.tau/48 for i in range(49))],[.04]*49,
                 deep=(0,0,1),fake=True,thickness=.04)
        n=13
        for i in range(12):
            a=i*math.tau/12+.3;long=i%2==0
            r0,r1=.22,.5 if long else .42;z1=2.05 if long else 1.7
            # A light spike: thin at the hoop, fullest two thirds out, pointed at the target.
            m.stroke([((r0+(r1-r0)*t)*math.cos(a),.1+(r0+(r1-r0)*t)*math.sin(a),.72+(z1-.72)*t) for t in (k/(n-1) for k in range(n))],
                     [.006+(.11 if long else .08)*1.7*t**.9*(1-t)**.45 for t in (k/(n-1) for k in range(n))],
                     deep=(math.cos(a),math.sin(a),0),fake=True,thickness=.015)
    emit('NyxFlashCone',cone)
    def rotor(m):
        n=31
        for j in range(3):
            a0=j*math.tau/3
            m.stroke([(.78*math.sin(a),1.28+.03*j,.78*math.cos(a)) for a in (a0+1.7*k/(n-1) for k in range(n))],
                     crescent(n,.16,.6),deep=(0,1,0),fake=True,thickness=.05)
    emit('NyxRotorArcs',rotor)
    emit('NyxShadowBurst',lambda m:m.shape(burst(9,seed=3,inner=.46,jag=.3),size=(.5,.5),depth=.12))
    emit('NyxPinkBurst',lambda m:m.shape(burst(11,seed=5,inner=.42,jag=.36),size=(.5,.5),fake=True,depth=.12))
    def rays(m):
        rnd=random.Random(11);n=9
        for i in range(14):
            a=i*math.tau/14+rnd.uniform(-.1,.1);r0=.62+rnd.uniform(0,.18);r1=1.15+rnd.uniform(0,.4)
            m.stroke([(math.cos(a)*(r0+(r1-r0)*t),math.sin(a)*(r0+(r1-r0)*t),0) for t in (k/(n-1) for k in range(n))],
                     [.004+.07*t**.8 for t in (k/(n-1) for k in range(n))],fake=True,thickness=.03)
    emit('NyxPinkRays',rays)
    def splash(m):
        # A torn shadow pool under the target: ink rim, violet ring, dark centre, upright teeth.
        first=len(m.v);m.shape(burst(14,seed=7,inner=.66,jag=.36),(0,0,0),(.95,.95),depth=.06,bands=(0,2,1,1));floor(m,first)
        teeth(m,8,.8,.32,seed=8)
    emit('NyxGraspSplash',splash)
    def crown(m):
        # Logging Off's jagged floor ring, open in the middle.
        first=len(m.v);m.shape(burst(18,seed=9,inner=.8,jag=.32),(0,0,0),(1,1),depth=.06,hollow=True);floor(m,first)
        teeth(m,12,.95,.55,seed=10)
    emit('NyxFloorCrown',crown)
    def crow(m):
        # Side profile drawn with x forward, then turned so the beak points along +Z.
        first=len(m.v)
        m.shape([(.34,.1),(.2,.14),(.15,.22),(.06,.24),(-.02,.16),(-.14,.1),(-.3,.06),(-.42,.12),(-.46,.04),(-.4,-.02),(-.46,-.08),(-.3,-.06),(-.12,-.12),(.04,-.1),(.14,-.02),(.2,.06)],
                size=(1,1),depth=.16)
        for j in range(first,len(m.v)):
            x,y,z=m.v[j];m.v[j]=(z,y,x)
        m.f[first//3:]=[tuple(reversed(f)) for f in m.f[first//3:]]
    emit('NyxCrowBody',crow)
    emit('NyxCrowWingLeft',lambda m:m.shape(WING,size=(.50,.5),depth=.13,fold=.65))
    emit('NyxCrowWingRight',lambda m:m.shape([(-x,y) for x,y in reversed(WING)],size=(.50,.5),depth=.13,fold=.65))
    (OUT/'NyxPaperMeshes.json').write_text(json.dumps(meshes,indent=2)+'\n')
    print(f'{len(meshes)} meshes, {sum(v["triangles"] for v in meshes.values())} triangles, eight-colour palette')

if __name__=='__main__':build()
