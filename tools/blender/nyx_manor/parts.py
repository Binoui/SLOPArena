"""Original manor motifs, in Unity coordinates. Small relief never changes the route."""
import math
from stagekit import geo
MATERIALS=('ManorLit','ManorGlow')
PINK=(.66,.21,.29); PALE=(.66,.44,.45); WHITE=(.70,.66,.62)
INK=(.035,.028,.065); STONE=(.29,.235,.34); GREEN=(.065,.18,.11)
GOLD=(.85,.38,.10); HOT=(.95,.07,.33)

def mesh(name): return geo.Mesh(name,MATERIALS)
def box(m,p,s,c,**kw): geo.box(m,p,s,c,**kw)
def ball(m,p,s,c,mat=None): geo.ellipsoid(m,p,s,c,mat,rings=5,segments=10)
def rod(m,a,b,r,c,mat=None): geo.cylinder_between(m,a,b,r,7,c,mat)
def heart(m,x,y,z,s,c,outline=False):
    pts=[]
    for i in range(40):
        a=i*math.tau/40
        pts.append((x+s*16*math.sin(a)**3/17,y+s*(13*math.cos(a)-5*math.cos(2*a)-2*math.cos(3*a)-math.cos(4*a))/17,z))
    if outline: geo.tube(m,pts+[pts[0]],.045*s,6,c,'ManorGlow')
    else:
        # A fan keeps the concave notch out of Blender's n-gon tessellation.
        for a,b in zip(pts,pts[1:]+pts[:1]): geo.oriented(m,[(x,y,z),a,b],(0,0,-1),c,'ManorGlow')

def ring(m,p,r,c=HOT,tube=.07,plane='xy',mat='ManorGlow'):
    x,y,z=p
    pts=[(x+r*math.cos(a),y+r*math.sin(a),z) if plane=='xy' else (x+r*math.cos(a),y,z+r*math.sin(a)) for a in [i*math.tau/32 for i in range(33)]]
    geo.tube(m,pts,tube,7,c,mat)

def skeleton(m,base=(0,0,0),s=1):
    x,y,z=base
    def p(a,b,c):return(x+a*s,y+b*s,z+c*s)
    bone=(.70,.65,.50)
    ball(m,p(0,1.65,0),(.17*s,.21*s,.13*s),bone)
    for a in (-.07,.07):ball(m,p(a,1.7,-.12),(.045*s,.055*s,.035*s),INK)
    box(m,p(0,1.54,-.11),(.15*s,.06*s,.03*s),INK)
    rod(m,p(0,.75,0),p(0,1.43,0),.045*s,bone)
    for b in (1.06,1.18,1.30):
        for a in (-1,1):rod(m,p(0,b,0),p(a*.23,b-.07,0),.025*s,bone)
    box(m,p(0,.8,0),(.32*s,.12*s,.15*s),bone)
    for a in (-1,1):
        pts=[p(a*.1,.8,0),p(a*.14,.4,0),p(a*.2,.06,-.03)]
        for q,r in zip(pts,pts[1:]):rod(m,q,r,.035*s,bone)
        pts=[p(a*.24,1.37,0),p(a*.34,1.0,0),p(a*.4,.76,-.03)]
        for q,r in zip(pts,pts[1:]):rod(m,q,r,.03*s,bone)
        ball(m,p(a*.21,.05,-.09),(.09*s,.05*s,.14*s),bone)

def bat(m,base=(0,0,0),s=1):
    x,y,z=base
    ball(m,(x,y,z),(.38*s,.46*s,.25*s),INK)
    for a in (-1,1):
        pts=[(x+a*u*s,y+v*s,z) for u,v in ((.2,.2),(.52,.58),(1.15,.77),(.96,.24),(.8,.32),(.65,-.06),(.48,.05),(.27,-.25))]
        geo.oriented(m,pts,(0,0,-1),(.07,.045,.12))
        geo.oriented(m,[(px,py,pz+.1*s) for px,py,pz in pts],(0,0,1),INK)
        geo.tube(m,pts+[pts[0]],.045*s,6,(.13,.09,.2))
        geo.oriented(m,[(x+a*.17*s,y+.32*s,z-.08*s),(x+a*.29*s,y+.75*s,z),(x+a*.38*s,y+.34*s,z)],(0,0,-1),INK)
        heart(m,x+a*.15*s,y+.12*s,z-.253*s,.12*s,HOT)
    for a in (-.07,.07):
        geo.oriented(m,[(x+a*s-.025*s,y-.11*s,z-.26*s),(x+a*s+.025*s,y-.11*s,z-.26*s),(x+a*s,y-.21*s,z-.26*s)],(0,0,-1),WHITE)

def crow(m,p,s=.45):
    x,y,z=p
    ball(m,(x,y+s*.4,z),(s*.4,s*.5,s*.25),INK)
    ball(m,(x,y+s*.8,z-s*.1),(s*.25,s*.25,s*.23),INK)
    rod(m,(x,y+s*.8,z-s*.2),(x,y+s*.77,z-s*.55),s*.1,(.12,.10,.14))
    for a in (-1,1):rod(m,(x+a*s*.14,y,z),(x+a*s*.14,y+s*.2,z),.025,INK)

def gothic(m,x,y,z,w,h,front=True):
    d=-1 if front else 1
    # The pane stands proud of masonry relief; inset glazing was hidden by the stone skin.
    pane=z+d*.13
    geo.box(m,(x,y+h*.34,pane),(w,.68*h,.025),(.69,.31,.10),'ManorGlow')
    geo.oriented(m,[(x-w/2,y+h*.67,pane),(x,y+h,pane),(x+w/2,y+h*.67,pane)],(0,0,d),(.69,.31,.10),'ManorGlow')
    pts=[(x-w/2,y,pane+d*.025),(x-w/2,y+h*.68,pane+d*.025),(x,y+h,pane+d*.025),(x+w/2,y+h*.68,pane+d*.025),(x+w/2,y,pane+d*.025)]
    geo.tube(m,pts+[pts[0]],.085,6,INK)
    rod(m,(x,y,pane+d*.04),(x,y+h-.08,pane+d*.04),.045,INK)
    rod(m,(x-w/2,y+h*.35,pane+d*.04),(x+w/2,y+h*.35,pane+d*.04),.045,INK)
