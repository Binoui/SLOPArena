"""Small test-lab parts. Coordinates and dimensions are Unity metres."""
import math
from stagekit import geo
import palette as P
import textures


def bar(m, a, b, radius=.06, color=P.STEEL):
    geo.cylinder_between(m,a,b,radius,6,color)


def label(m, name, a, b, y0, y1, normal, material='TL_Hall'):
    geo.facade_quad(m,a,b,y0,y1,normal,(1,1,1),material,uvs=textures.uv(name))


def foam(m,center,width,height,color,yaw):
    x,y,z=center; r=width/2; cut=.20
    outline=[(-r+cut,-r),(r-cut,-r),(r,-r+cut),(r,r-cut),
             (r-cut,r),(-r+cut,r),(-r,r-cut),(-r,-r+cut)]
    c,s=math.cos(yaw),math.sin(yaw)
    def point(a,h,b): return (x+a*c+b*s,h,z-a*s+b*c)
    top=y+height/2; bottom=y-height/2
    for i,(a,b) in enumerate(outline):
        aa,bb=outline[(i+1)%8]
        normal=(bb-b,0,a-aa)
        geo.oriented(m,[point(a,bottom,b),point(a,top-.20,b),point(aa,top-.20,bb),point(aa,bottom,bb)],normal,color)
        geo.oriented(m,[point(a,top-.20,b),point(a*.90,top,b*.90),point(aa*.90,top,bb*.90),point(aa,top-.20,bb)],
                     (0,1,0),geo.scale(color,1.07))
    geo.oriented(m,[point(a*.90,top,b*.90) for a,b in outline],(0,1,0),geo.scale(color,1.04))


def dummy(m,x,y,z,scale=1.0):
    def point(dx,dy,dz): return (x+dx*scale,y+dy*scale,z+dz*scale)
    def profile(values): return [(r*scale,h*scale) for r,h in values]
    geo.lathe(m,point(0,1.05,0),profile([(.30,0),(.48,.35),(.40,.88)]),10,P.YELLOW)
    geo.lathe(m,point(0,2.02,0),profile([(.32,0),(.45,.12),(.46,.65),(.32,.76)]),12,P.YELLOW,cap_bottom=True)
    for quadrant in range(4):
        vertices=[point(0,2.40,-.465)]
        for i in range(7):
            angle=(quadrant+i/6)*math.pi/2
            vertices.append(point(math.cos(angle)*.35,2.40+math.sin(angle)*.35,-.465))
        geo.oriented(m,vertices,(0,0,-1),P.BLACK if quadrant%2==0 else P.YELLOW)
    for side in (-1,1):
        bar(m,point(side*.49,1.80,0),point(side*.67,1.20,-.16),.17*scale,P.YELLOW)
        bar(m,point(side*.67,1.20,-.16),point(side*.43,.95,-.43),.15*scale,P.YELLOW)
        bar(m,point(side*.23,1.1,0),point(side*.25,.72,-.56),.21*scale,P.YELLOW)
        bar(m,point(side*.25,.72,-.56),point(side*.25,.12,-.56),.18*scale,P.YELLOW)
        geo.box(m,point(side*.25,.1,-.70),(.38*scale,.22*scale,.60*scale),P.BLACK)
