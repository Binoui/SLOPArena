#!/usr/bin/env python3
"""Build Nyx's volumetric comic OBJ meshes and band palette without operating Unity.

Run from any cwd. Unity's NyxPresentationBuilder assembles these into separate prefabs.
All geometry is in metres about the capsule centre unless its name denotes a floor mark.
"""
from pathlib import Path
from collections import Counter
import json, math
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
    def shape(self,xy,at=(0,0,0),size=(1,1),fake=False,rotation=0,depth=None,fold=0,hollow=False):
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
                    quad(*points,offset+band)
            # Ear clipping uses XY; reversing only triangle winding preserves concavity.
            if not hollow:
                first=len(self.f);self.poly(rings[-1],offset+3)
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
def star(n=10):return [(math.cos(i*math.pi/n)*(.95 if i%2==0 else .35),math.sin(i*math.pi/n)*(.95 if i%2==0 else .35)) for i in range(n*2)]
HAND=[(-.32,-.5),(-.43,-.1),(-.7,.22),(-.68,.45),(-.48,.39),(-.28,.17),(-.34,.72),(-.24,.97),(-.13,.85),(-.09,.38),(-.05,1.12),(.07,1.22),(.17,1.06),(.12,.42),(.26,1.02),(.4,1.05),(.43,.85),(.29,.26),(.48,.71),(.63,.75),(.67,.55),(.43,-.12),(.27,-.5)]
FIST=[(-.38,-.4),(-.54,-.15),(-.58,.22),(-.46,.5),(-.22,.53),(-.12,.62),(.09,.59),(.25,.54),(.45,.48),(.56,.25),(.5,-.14),(.29,-.4)]
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
        # A triangular bundle around the sweep axis, rather than three coplanar
        # translations: an oblique view cannot stack all three blades into a line.
        for j in range(3):
            a=j*math.tau/3
            m.ribbon([(-.48+t+.30*math.sin(t*math.pi)+.42*math.cos(a)*.862,
                       .05+.42*math.sin(a)+.22*math.sin(t*math.tau),
                       .3+1.7*t+.4*math.sin(t*math.pi)-.42*math.cos(a)*.507)
                      for t in (k/30 for k in range(31))],.14)
    emit('NyxShadowClaws',claws)
    # The builder places two independently yaw-facing palms at the original centres.
    emit('NyxRisingHands',lambda m:m.shape(HAND,(0,.55,.55),(.45,.56),depth=.34,fold=.3))
    def lash(m):
        m.ribbon([(.10*math.sin(t*math.pi*4),-.55+.1*math.sin(t*math.pi*3),.8+3.2*t) for t in [k/80 for k in range(81)]],.24,side=(1,0,0))
        # Upright torn edges read above the floor from behind while staying inside the capsule.
        for i in range(7):
            m.shape([(-.8,-.4),(-.35,.18),(.15,1),(.08,.14),(.7,-.28)],
                at=(.08*math.sin(i*2),-.52,1+i*.46),size=(.26,.3))
    emit('NyxFloorLash',lash)
    emit('NyxShadowRing',lambda m:arc(m,1.20,0,width=.14))
    emit('NyxUpClaw',lambda m:m.shape(HAND,(0,.94,.3),(.65,.65),depth=.46,fold=.4,hollow=True))
    def fist(m):
        m.shape(FIST,(0,-.85,.25),(.88,.82),rotation=math.pi,depth=.58,hollow=True)
        for x in (-.27,0,.27):m.ribbon([(x,-.4,.25),(x*1.3,-.95,.25),(x*.2,-1.56,.25)],.09,side=(1,0,0))
    emit('NyxSpikeFist',fist)
    emit('NyxGraspHand',lambda m:m.shape(HAND,(0,.72,0),(.85,1.12),depth=.6,fold=.32))
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
    emit('NyxGraspDrag',lambda m:[m.ribbon([(x,.15,0),(x*.7,.23,.4),(x*.4,.1,1)],.095,side=(1,0,0)) for x in (-.5,0,.5)])
    emit('NyxBat',lambda m:m.shape(BAT,size=(.24,.24),depth=.12,fold=.65))
    emit('NyxFeather',lambda m:m.shape(FEATHER,size=(.18,.26),depth=.1,fold=.32))
    emit('NyxHeart',lambda m:m.shape(HEART,size=(.12,.12),fake=True,depth=.13))
    emit('NyxSparkle',lambda m:m.shape([(0,1),(.15,.18),(.7,0),(.15,-.18),(0,-1),(-.15,-.18),(-.7,0),(-.15,.18)],size=(.13,.13),fake=True,depth=.17))
    emit('NyxShadowShard',lambda m:m.shape([(-.14,-1),(-.46,.21),(.15,1),(.34,.13),(.1,-.16)],size=(.22,.52),depth=.18,fold=.5))
    def impact(m,fake=False):
        # Three intersecting closed blade fans make a spike burst from every axis.
        # A shallow primary face preserves the approved front-view star at pitch 35.
        m.shape(star(),size=(.36,.36) if fake else (.42,.42),fake=fake,depth=.13)
        for axis in (0,1):
            first=len(m.v)
            m.shape(star(6),size=(.27,.27),fake=fake,depth=.10)
            for j in range(first,len(m.v)):
                x,y,z=m.v[j];m.v[j]=(z,y,x) if axis==0 else (x,z,y)
            # Each axis swap is a reflection: retain outward triangle winding.
            first_face=first//3
            m.f[first_face:]=[tuple(reversed(f)) for f in m.f[first_face:]]
    emit('NyxShadowImpact',impact)
    emit('NyxPinkImpact',lambda m:impact(m,True))
    def upper(m):
        m.ribbon([(0,-.1+1.25*t,.75+.42*math.sin(math.pi*t)) for t in [i/48 for i in range(49)]],.32,True,side=(1,0,0))
    emit('NyxPoleUpperArc',upper)
    def overhead(m):
        m.ribbon([(-.9+1.8*t,.9+.28*math.sin(math.pi*t),.25+.35*math.sin(math.pi*t)) for t in [i/48 for i in range(49)]],.2,True)
    emit('NyxPoleOverheadArc',overhead)
    emit('NyxSpinArc',lambda m:arc(m,.94,.1,fake=True,width=.055))
    def drop(m):
        for x in (-.2,.2):m.shape([(0,-1),(-.2,.25),(.1,.65),(.26,.28)],(x,-.64,.48),(.4,.65),True)
    emit('NyxDropkickSpike',drop)
    def cone(m):
        # A phone-sized ring light and six independent light streaks. No end cap,
        # filled fan or centre ray: the target remains visible through the gaps.
        m.ribbon([(.19*math.cos(a),.1+.19*math.sin(a),.62)
                  for a in (i*math.tau/48 for i in range(49))],.028,True,
                 side=(0,0,1),taper=False)
        for i in range(6):
            a=i*math.tau/6
            m.ribbon([((.23+.29*t)*math.cos(a),.1+(.19+.16*t)*math.sin(a),.76+.9*t)
                      for t in (k/12 for k in range(13))],.018,True,
                     side=(0,1,0))
    emit('NyxFlashCone',cone)
    emit('NyxRotorArcs',lambda m:[arc(m,.62,1.25+j*.08,fake=True,start=j*.9,end=j*.9+math.pi*1.45,width=.055) for j in range(2)])
    emit('NyxCrowBody',lambda m:m.shape([(-.12,-.1),(-.17,.07),(-.11,.22),(.04,.27),(.19,.16),(.25,.12),(.12,.08),(.16,-.1),(.06,-.22),(-.04,-.19)],size=(1,1),depth=.3))
    emit('NyxCrowWingLeft',lambda m:m.shape(WING,size=(.50,.5),depth=.13,fold=.65))
    emit('NyxCrowWingRight',lambda m:m.shape([(-x,y) for x,y in reversed(WING)],size=(.50,.5),depth=.13,fold=.65))
    (OUT/'NyxPaperMeshes.json').write_text(json.dumps(meshes,indent=2)+'\n')
    print(f'{len(meshes)} meshes, {sum(v["triangles"] for v in meshes.values())} triangles, eight-colour palette')

if __name__=='__main__':build()
