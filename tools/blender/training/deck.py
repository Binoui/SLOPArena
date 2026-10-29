"""Caps, plated faces and braced supports follow the same solid rows as collision."""
import math
from stagekit import geo
import palette as P
import textures
from parts import bar, label


def build(b, detailed=False):
    support=b.mesh('Braced Lattice','below','', 'TL_Hall')
    for solid in b.data['solids']:
        name=solid['name']; top=solid['top']; bottom=solid['bottom']
        x0,z0,x1,z1=solid['footprint']; poly=b.outlines[name]
        floor=b.mesh(name+' Cap','surface',name)
        geo.oriented(floor,[(x0,top,z0),(x1,top,z0),(x1,top,z1),(x0,top,z1)],
                     (0,1,0),(1,1,1),'TL_Floor',textures.floor_uv(solid['footprint']))
        faces=b.mesh(name+' Plating','wall',name,'TL_Hall')
        deep=bottom==b.data['killY']
        for a,c,n,length in geo.edges(poly):
            tangent=((c[0]-a[0])/length,(c[1]-a[1])/length)
            bays=max(1,math.ceil(length/2))
            levels=max(1,math.ceil((top-bottom)/2))
            for row in range(levels):
                low=bottom+(top-bottom)*row/levels; high=bottom+(top-bottom)*(row+1)/levels
                for k in range(bays):
                    aa=tuple(a[i]+(c[i]-a[i])*k/bays for i in range(2))
                    cc=tuple(a[i]+(c[i]-a[i])*(k+1)/bays for i in range(2))
                    geo.facade_quad(faces,aa,cc,low,high,n,geo.scale((.52,.57,.60),1 if (k+row)%3 else .94))
                    if detailed:
                        geo.facade_quad(faces,aa,cc,low,low+.035,n,P.DARK,off=.008)
                        for t in (.06,.94):
                            x=aa[0]+(cc[0]-aa[0])*t; z=aa[1]+(cc[1]-aa[1])*t
                            geo.box(faces,(x+n[0]*.009,low+.13,z+n[1]*.009),(.04,.06,.04),P.DARK)
            if deep:
                if detailed:
                    def point(t,y): return (a[0]+tangent[0]*t+n[0]*.027,y,a[1]+tangent[1]*t+n[1]*.027)
                    for k in range(max(1,round(length/4))):
                        count=max(1,round(length/4)); lo=k*length/count+.18; hi=(k+1)*length/count-.18
                        for low,high in ((-11.5,-6.3),(-5.7,-.3)):
                            for ya,yb in ((low,high),(high,low)):
                                geo.oriented(faces,[point(lo,ya),point(hi,yb),point(hi,yb+.13),point(lo,ya+.13)],
                                             (n[0],0,n[1]),(.68,.71,.72))
                geo.facade_quad(faces,a,c,bottom,bottom+.24,n,P.RED,off=.012)
                cx=(a[0]+c[0])/2; cz=(a[1]+c[1])/2
                for depth in range(2,12,2):
                    aa=(cx-tangent[0]*.5,cz-tangent[1]*.5)
                    cc=(cx+tangent[0]*.5,cz+tangent[1]*.5)
                    geo.facade_quad(faces,aa,cc,-depth-.035,-depth+.035,n,P.WHITE,off=.018)
                for text,depth in [('depth4',4),('depth8',8)]:
                    width=min(2.6,length-.4)/2
                    aa=(cx-tangent[0]*width+n[0]*.034,cz-tangent[1]*width+n[1]*.034)
                    cc=(cx+tangent[0]*width+n[0]*.034,cz+tangent[1]*width+n[1]*.034)
                    label(faces,text,aa,cc,-depth+.15,-depth+.95,n,'TL_Lit')
            else:
                geo.facade_quad(faces,a,c,bottom,top,n,P.YELLOW,off=.002)
                for k in range(math.ceil(length*2)):
                    lo=(k+.1)/2; hi=min(length,(k+.55)/2)
                    aa=tuple(a[i]+tangent[i]*lo for i in range(2)); cc=tuple(a[i]+tangent[i]*hi for i in range(2))
                    geo.facade_quad(faces,aa,cc,bottom,top,n,P.BLACK,off=.006)
                if top>0:
                    aa=(a[0]+tangent[0]*.15+n[0]*.015,a[1]+tangent[1]*.15+n[1]*.015)
                    cc=(a[0]+tangent[0]*.62+n[0]*.015,a[1]+tangent[1]*.62+n[1]*.015)
                    label(faces,'height3' if top==3 else 'height',aa,cc,bottom,top,n,'TL_Lit')
                    cx=(a[0]+c[0])/2; cz=(a[1]+c[1])/2; half=min(length-.9,4)/2
                    aa=(cx-tangent[0]*half+n[0]*.02,cz-tangent[1]*half+n[1]*.02)
                    cc=(cx+tangent[0]*half+n[0]*.02,cz+tangent[1]*half+n[1]*.02)
                    label(faces,'plate3' if top==3 else 'plate15',aa,cc,bottom+.3,top-.3,n,'TL_Lit')
            if top<0:
                cx=(a[0]+c[0])/2; cz=(a[1]+c[1])/2
                aa=(cx-tangent[0]*1.5+n[0]*.02,cz-tangent[1]*1.5+n[1]*.02)
                cc=(cx+tangent[0]*1.5+n[0]*.02,cz+tangent[1]*1.5+n[1]*.02)
                label(faces,'shelf',aa,cc,bottom+.05,top-.05,n,'TL_Lit')
        if top<0:
            geo.oriented(faces,[(x0,bottom,z0),(x1,bottom,z0),(x1,bottom,z1),(x0,bottom,z1)],(0,-1,0),P.STEEL)
        if deep:
            xs=[x0+.3+(x1-x0-.6)*i/max(1,math.ceil((x1-x0)/7)) for i in range(math.ceil((x1-x0)/7)+1)]
            zs=[z0+.3,z1-.3]
            for x in xs:
                for z in zs: geo.box(support,(x,-15.3,z),(.45,6.4,.45),(.43,.49,.53))
            for z in zs:
                for a,c in zip(xs,xs[1:]):
                    bar(support,(a,-17.9,z),(c,-12.4,z),.15)
                    bar(support,(a,-12.4,z),(c,-17.9,z),.15)
            geo.box(support,((x0+x1)/2,-12.25,(z0+z1)/2),(x1-x0-.2,.3,z1-z0-.2),P.STEEL)
