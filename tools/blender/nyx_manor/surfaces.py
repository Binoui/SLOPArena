"""Worn stone and cliff skins derived from the frozen collision triangles."""
import math
import random
from stagekit import geo
import parts as K
import layout

STONE=(.29,.29,.32)
CLIFF=(.14,.16,.21)

def fractured_triangle(m, tri, color, rng, depth=1):
    if depth:
        a,b,c=tri
        ab=tuple((a[i]+b[i])/2 for i in range(3))
        bc=tuple((b[i]+c[i])/2 for i in range(3))
        ca=tuple((c[i]+a[i])/2 for i in range(3))
        for t in ((a,ab,ca),(ab,b,bc),(ca,bc,c),(ab,bc,ca)):
            fractured_triangle(m,t,color,rng,depth-1)
    else:
        c=tuple(sum(p[i] for p in tri)/3 for i in range(3))
        tone=1+.10*math.sin(c[0]*1.1+c[1]*.7)*math.sin(c[2]*.9-c[1]*.6)+rng.uniform(-.025,.025)
        m.poly(tri,geo.scale(color,tone))

def cliff(m, bounds, bottom, rng, top_at=None):
    x0,z0,x1,z1,top=bounds
    perimeter=[]
    for a,b in (((x0,z0),(x1,z0)),((x1,z0),(x1,z1)),((x1,z1),(x0,z1)),((x0,z1),(x0,z0))):
        n=max(1,math.ceil(math.dist(a,b)/2.2))
        perimeter.extend([(a[0]+(b[0]-a[0])*i/n,a[1]+(b[1]-a[1])*i/n) for i in range(n)])
    cx,cz=(x0+x1)/2,(z0+z1)/2
    rings=[]
    for k in range(9):
        t=k/8
        ring=[]
        for x,z in perimeter:
            inset=0 if k==8 else rng.uniform(.04,.75)*(1-.5*t)
            dx,dz=cx-x,cz-z; d=max(abs(dx),abs(dz),.01)
            local_top=top if top_at is None else top_at(x,z)
            y=bottom+(local_top-bottom)*t
            if k not in (0,8):y+=rng.uniform(-.45,.45)
            ring.append((x+dx/d*inset,y,z+dz/d*inset))
        rings.append(ring)
    for k in range(8):
        for i in range(len(perimeter)):
            j=(i+1)%len(perimeter)
            pts=(rings[k][i],rings[k+1][i],rings[k+1][j],rings[k][j])
            normal=(pts[0][0]-cx,0,pts[0][2]-cz)
            col=geo.scale(CLIFF,rng.uniform(.78,1.5)*(1+.18*k/8))
            for a,b,c in ((0,1,2),(0,2,3)):
                geo.oriented(m,[pts[a],pts[b],pts[c]],normal,geo.jitter(col,rng,.12))

def paving(m,x0,z0,x1,z1,y,rng):
    z=z0
    row=0
    while z<z1-.05:
        d=min(rng.uniform(1.0,1.9),z1-z)
        x=x0
        while x<x1-.05:
            w=min(rng.uniform(1.1,2.6),x1-x)
            cut=min(.16,w*.15,d*.15)
            outline=[(x+cut,z+.025),(x+w-.04,z+.025),(x+w-.025,z+d-cut),(x+w-cut,z+d-.025),(x+.025,z+d-.025),(x+.025,z+cut)]
            col=geo.jitter(STONE,rng,.18)
            geo.oriented(m,[(a,y+.009,b) for a,b in outline],(0,1,0),col)
            if rng.random()<.22:
                geo.oriented(m,[(x+cut,y+.011,z+.03),(x+w-.05,y+.011,z+.03),(x+w-.15,y+.011,z+.09),(x+cut+.08,y+.011,z+.085)],(0,1,0),geo.scale(col,1.26))
            if rng.random()<.13:
                pts=[(x+w*.22,y+.014,z+d*.15),(x+w*.48,y+.014,z+d*.44),(x+w*.42,y+.014,z+d*.7)]
                geo.tube(m,pts,.009,3,(.10,.12,.16))
            x+=w
        z+=d;row+=1

def masonry(m,a,b,y0,y1,rng):
    length=math.dist(a,b)
    nx,nz=(b[1]-a[1])/length,-(b[0]-a[0])/length
    rows=max(1,math.ceil((y1-y0)/.64))
    for j in range(rows):
        lo=y0+(y1-y0)*j/rows+.015; hi=y0+(y1-y0)*(j+1)/rows-.025
        u=0
        while u<length-.05:
            w=min(rng.uniform(.65,1.65),length-u)
            t0,t1=(u+.025)/length,(u+w-.025)/length
            p=(a[0]+(b[0]-a[0])*t0,a[1]+(b[1]-a[1])*t0)
            q=(a[0]+(b[0]-a[0])*t1,a[1]+(b[1]-a[1])*t1)
            col=geo.jitter((.22,.21,.27),rng,.27)
            geo.facade_quad(m,p,q,lo,hi,(nx,nz),col,off=.025+rng.random()*.018)
            if rng.random()<.4:
                geo.facade_quad(m,p,q,hi-.035,hi,(nx,nz),geo.scale(col,1.32),off=.045)
            u+=w
