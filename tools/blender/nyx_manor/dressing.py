"""Gothic relief and planting, kept on solid hosts and inside the side plots."""
import math
import random
from stagekit import geo
import parts as K
import layout
import surfaces as S


def neon_heart(m,x,y,z,s):
    points=[]
    for i in range(33):
        a=i*math.tau/32
        points.append((x+s*16*math.sin(a)**3/17,y+s*(13*math.cos(a)-5*math.cos(2*a)-2*math.cos(3*a)-math.cos(4*a))/17))
    for a,b in zip(points,points[1:]):
        dx,dy=b[0]-a[0],b[1]-a[1];d=max(.001,math.hypot(dx,dy));u,v=-dy/d*.025,dx/d*.025
        geo.oriented(m,[(a[0]-u,a[1]-v,z),(a[0]+u,a[1]+v,z),(b[0]+u,b[1]+v,z),(b[0]-u,b[1]-v,z)],(0,0,-1),K.HOT,'ManorGlow')


def roof(m,x,z,y,w,d,h):
    rings=[[(x+a*w*t,y+b,z+c*d*t) for a,c in ((-.5,-.5),(.5,-.5),(.5,.5),(-.5,.5))] for b,t in ((0,1),(h*.78,.62),(h,.53))]
    for k in range(2):
        for i in range(4):
            j=(i+1)%4
            pts=[rings[k][i],rings[k+1][i],rings[k+1][j],rings[k][j]]
            geo.oriented(m,pts,(pts[0][0]-x,.2,pts[0][2]-z),(.075+.025*i,.085+.022*i,.13+.025*i))
            for row in range(1,5):
                t=row/5
                a=tuple(pts[0][n]+(pts[1][n]-pts[0][n])*t for n in range(3))
                b=tuple(pts[3][n]+(pts[2][n]-pts[3][n])*t for n in range(3))
                geo.cylinder_between(m,a,b,.022,4,(.21,.23,.29))
    K.box(m,(x,y+h+.025,z),(w*.53,.06,d*.53),(.16,.17,.23))
    for i in range(4):
        a,b=rings[0][i],rings[1][i]
        geo.cylinder_between(m,a,b,.035,5,(.24,.24,.31))


def candle(m,x,y,z,s=1):
    K.box(m,(x,y+.12*s,z),(.11*s,.24*s,.11*s),(.63,.52,.33))
    geo.ellipsoid(m,(x,y+.31*s,z),(.045*s,.10*s,.045*s),(.90,.39,.055),'CandleGlow',rings=3,segments=6)


def lantern(m,x,y,z,s=1):
    K.rod(m,(x,y,z),(x,y+1.4*s,z),.045*s,K.INK)
    K.box(m,(x,y+1.55*s,z),(.4*s,.08*s,.4*s),(.22,.21,.27))
    K.box(m,(x,y+1.87*s,z),(.27*s,.55*s,.27*s),(.64,.26,.045),mat='CandleGlow')
    for a in (-1,1):
        for b in (-1,1):K.rod(m,(x+a*.17*s,y+1.6*s,z+b*.17*s),(x+a*.17*s,y+2.15*s,z+b*.17*s),.025*s,K.INK)
    roof(m,x,z,y+2.15*s,.46*s,.46*s,.32*s)


def dead_tree(m,x,y,z,h,seed):
    rng=random.Random(seed)
    trunk=[(x,y,z),(x-.12*h,y+h*.5,z+.09*h),(x+.06*h,y+h,z)]
    for i in range(2):geo.cylinder_between(m,trunk[i],trunk[i+1],h*(.065-.022*i),6,(.065,.062,.087),radius_b=h*(.043-.023*i))
    for j in range(7):
        t=.35+j*.08;a=rng.uniform(0,math.tau)
        start=(x-.10*h,y+h*t,z+.04*h)
        elbow=(start[0]+math.cos(a)*h*.30,start[1]+h*.12,start[2]+math.sin(a)*h*.30)
        end=(elbow[0]+math.cos(a+.3)*h*.11,elbow[1]+h*.24,elbow[2]+math.sin(a+.3)*h*.11)
        geo.cylinder_between(m,start,elbow,h*.022,5,K.INK,radius_b=h*.012)
        geo.cylinder_between(m,elbow,end,h*.012,5,K.INK,radius_b=.018)
        tip=(elbow[0]+math.cos(a-1)*h*.15,elbow[1]+h*.06,elbow[2]+math.sin(a-1)*h*.15)
        geo.cylinder_between(m,elbow,tip,h*.009,4,K.INK,radius_b=.009)


def headstone(m,x,y,z,kind,lean,seed):
    rng=random.Random(seed);s=rng.uniform(.7,1.15)
    stone=K.mesh('Carved grave')
    color=geo.jitter((.30,.32,.36),rng,.18)
    K.box(stone,(0,.09,0),(.78,.18,.47),geo.scale(color,.75))
    if kind==0:
        K.box(stone,(0,.63,0),(.24,1.13,.24),color)
        K.box(stone,(0,.85,0),(.81,.22,.24),color)
        K.box(stone,(0,.85,-.126),(.12,.12,.02),geo.scale(color,.55))
    elif kind==1:
        geo.prism(stone,[(-.31,-.13),(.31,-.13),(.31,.13),(-.31,.13)],.18,.94,color)
        geo.ellipsoid(stone,(0,.94,0),(.31,.18,.13),color,rings=3,segments=8)
        K.box(stone,(0,.62,-.141),(.38,.36,.025),geo.scale(color,.7))
        K.box(stone,(0,.66,-.16),(.05,.28,.012),geo.scale(color,1.3))
        K.box(stone,(0,.68,-.16),(.22,.05,.012),geo.scale(color,1.3))
    else:
        geo.lathe(stone,(0,.17,0),[(.29,0),(.24,.12),(.14,1.02),(.01,1.35)],4,color,yaw=math.pi/4)
    stone.verts=[(x+s*(a+lean*b),y+s*b,z+s*c) for a,b,c in stone.verts]
    m.extend(stone)


def patch(m,x,z,y,rx,rz,rng,grass=True):
    pts=[]
    for i in range(9):
        a=i*math.tau/9;r=rng.uniform(.7,1)
        pts.append((x+rx*r*math.cos(a),y,z+rz*r*math.sin(a)))
    col=(.16,.19,.15) if grass else (.20,.17,.18)
    geo.oriented(m,pts,(0,1,0),col)
    if grass:
        for _ in range(18):
            xx=x+rng.uniform(-rx*.65,rx*.65);zz=z+rng.uniform(-rz*.65,rz*.65)
            h=rng.uniform(.07,.18)
            for dx,dz in ((.07,0),(0,.07)):
                geo.oriented(m,[(xx-dx,y,zz-dz),(xx+.035,y+h,zz),(xx+dx,y,zz+dz)],(0,1,0),geo.jitter((.22,.26,.18),rng,.2))


def graveyard():
    m=K.mesh('Graveyard Carving and Planting');rng=random.Random(302)
    for side in (-1,1):
        for i,z in enumerate((50.5,53.4,55.5,63,65.8)):
            x=side*(14.6 if z<56 else 8)
            patch(m,x,z,9.025,1.85,1.1,rng,False)
            headstone(m,x,9,z,i%3,side*(.13+.03*i),i+int(x*10))
            patch(m,x+.65,z+.32,9.03,.5,.45,rng)
        for i in range(25):
            x=side*rng.uniform(10,16.6);z=rng.uniform(49.7,66.5)
            if (side<0 and z>59) or (side>0 and 55<z<62):continue
            patch(m,x,z,9.03,rng.uniform(.4,.9),rng.uniform(.35,.6),rng)
    for i,z in enumerate((52,57)):
        for j,x in enumerate(range(-8,9,2)):
            face=z-.026
            col=geo.jitter((.32,.33,.37),rng,.2)
            K.box(m,(x,9.13,face),(1.04,.22,.025),geo.scale(col,.7))
            if (i+j)%3==0:
                K.box(m,(x,9.51,face-.02),(.16,.65,.025),col)
                K.box(m,(x,9.61,face-.02),(.67,.15,.025),col)
            elif (i+j)%3==1:
                K.box(m,(x,9.48,face-.02),(.71,.55,.025),col)
                K.box(m,(x,9.48,face-.04),(.53,.33,.015),geo.scale(col,.67))
            else:
                geo.oriented(m,[(x-.32,9.22,face-.02),(x-.20,9.70,face-.02),(x,9.86,face-.02),(x+.20,9.70,face-.02),(x+.32,9.22,face-.02)],(0,0,-1),col)
    for x,z,y in ((-2,26,9),(2,34,9),(-14,62,11.4),(14,58,11.4),(2,65,12)):
        K.crow(m,(x,y,z),.48)
    for name in ('MausoleumW','MausoleumE','Crypt'):
        x0,z0,x1,z1,y=layout.SPEC['route'][name];x=(x0+x1)/2
        for a,b in (((x0,z0),(x1,z0)),((x1,z0),(x1,z1)),((x1,z1),(x0,z1)),((x0,z1),(x0,z0))):
            S.masonry(m,a,b,9,y-.3,rng)
        # The roofs are usable shortcuts. Slate and gable relief stay below their exact tops.
        for zface,d in ((z0-.055,-1),(z1+.055,1)):
            geo.oriented(m,[(x0+.1,y-.28,zface),(x,y-.015,zface),(x1-.1,y-.28,zface),(x1-.1,y-.65,zface),(x0+.1,y-.65,zface)],(0,0,d),(.12,.14,.20))
            geo.tube(m,[(x0+.15,y-.60,zface+d*.01),(x,y-.05,zface+d*.01),(x1-.15,y-.60,zface+d*.01)],.035,4,(.40,.39,.44))
        for xx in (x0+.15,x1-.15):
            K.box(m,(xx,10,z0-.05),(.22,1.95,.12),(.33,.32,.39))
        if name!='Crypt':
            K.box(m,(x,9.90,z0-.20),(1.12,1.75,.04),(.075,.067,.095))
            K.gothic(m,x,9.1,z0-.19,1.0,1.85)
            for dx in (-.36,-.12,.12,.36):K.rod(m,(x+dx,9.15,z0-.39),(x+dx,10.45,z0-.39),.025,K.INK)
        for side in (-1,1):
            xx=x+side*(1.9 if name!='Crypt' else 2.2)
            for j in range(3):candle(m,xx+j*.14,9.04,z0-.15,1+j*.25)
    for x,z in ((-3.8,47),(3.8,47),(-2,61.7),(2,61.7),(-12,54),(12,64)):
        lantern(m,x,9,z,.78)
    dead_tree(m,-16.1,9,66,5.4,72)
    dead_tree(m,16.2,9,65.8,4.5,91)
    for p in ((-17,12,66),(-14.9,12.9,65.9),(-15.8,14.45,66),(15.8,13.5,66)):
        K.crow(m,p,.45)
    return m


def manor():
    m=K.mesh('Manor Slate Roofs');rng=random.Random(313)
    roof(m,0,12,22.005,5.95,5.95,3.1)
    for side in (-1,1):
        for i in range(8):
            xa,xb=-16+i*4,-12+i*4
            z0,z1=(6.005,6.72) if side<0 else (17.995,17.28)
            pts=[(xa,10.8,z0),(xa,14.15,z1),(xb,14.15,z1),(xb,10.8,z0)]
            geo.oriented(m,pts,(0,.2,side),geo.jitter((.14,.15,.21),rng,.12))
            for j in range(1,9):
                t=j/9;y=10.8+3.35*t;z=z0+(z1-z0)*t+side*.012
                geo.cylinder_between(m,(xa,y,z),(xb,y,z),.014,4,(.25,.26,.32))
    for x in (-14,-10,-6,0,6,10,14):
        for z,d in ((6.01,-1),(17.99,1)):
            K.box(m,(x,12.3,z+d*.02),(1.45,2.6,.05),(.105,.095,.15))
            geo.oriented(m,[(x-.8,13.55,z+d*.045),(x,14.3,z+d*.045),(x+.8,13.55,z+d*.045)],(0,0,d),(.14,.14,.20))
    for side in (-1,1):
        x=side*9.2
        for z in (11.19,12.81):
            geo.oriented(m,[(x-.7,15.7,z),(x,16.16,z),(x+.7,15.7,z)],(0,0,-1 if z<12 else 1),(.22,.20,.28))
        K.box(m,(x,16.08,12),(1.58,.15,1.58),(.32,.29,.34))
        K.bat(m,(x,16.42,12),.32)
    for d in (-1,1):
        z=12+d*2.13
        K.box(m,(0,23.1,z),(1.25,1.45,.2),(.11,.10,.17))
        K.gothic(m,0,22.45,z+d*.12,.76,1.38,d<0)
        geo.oriented(m,[(-.85,23.7,z+d*.16),(0,24.45,z+d*.16),(.85,23.7,z+d*.16)],(0,0,d),(.14,.14,.21))
        geo.tube(m,[(-.85,23.7,z+d*.18),(0,24.45,z+d*.18),(.85,23.7,z+d*.18)],.045,5,(.34,.31,.39))
    K.rod(m,(0,25,12),(0,26,12),.04,K.INK)
    K.crow(m,(0,25.9,12),.7)
    # Side dormer trim and small gables sit on the existing vertical roof-riser faces.
    for x in (-14,-10,-6,6,10,14):
        geo.oriented(m,[(x-.7,13.65,5.94),(x,14.33,5.94),(x+.7,13.65,5.94)],(0,0,-1),(.13,.12,.19))
        geo.tube(m,[(x-.7,13.65,5.91),(x,14.33,5.91),(x+.7,13.65,5.91)],.035,4,(.31,.28,.34))
    for side in (-1,1):
        for j in range(9):
            t=j/8
            # White feathers form a relief on the turret, with no new deck obstacles.
            x=side*(.5+1.9*t);y=16.1+1.5*t;z=8.925
            pts=[(side*.45,15.8,z),(x,y+.62,z),(x+side*.25,y+.48,z),(side*(.65+1.5*t),15.92+.54*t,z)]
            geo.oriented(m,pts,(0,0,-1),geo.mix((.64,.57,.56),(.91,.85,.79),t))
        neon_heart(m,side*2.1,19.8,8.92,.48)
    return m


def front():
    m=K.mesh('Dreamhouse Trim and Roses');rng=random.Random(202)
    for z,y in ((-18,3.6),(-10,7.2),(-2,10.8)):
        for h,w,c in ((.30,.08,K.WHITE),(.42,.05,(.88,.59,.58)),(3.35,.06,K.WHITE)):
            K.box(m,(0,y-h,z-.045),(32,w,.05),c)
        for x in (-15.7,-12,-8,-4,4,8,12,15.7):
            K.box(m,(x,y-1.8,z-.03),(.16,3.0,.04),(.81,.42,.46))
            K.box(m,(x,y-.55,z-.045),(.34,.15,.045),K.WHITE)
        for side in (-1,1):
            x=side*15.5
            for j in range(9):
                xx=x+math.sin(j*2)*.20;yy=y-3.1+j*.30
                K.ball(m,(xx,yy,z-.10),(.16,.14,.06),geo.jitter(K.GREEN,rng,.2))
                if j%2==0:
                    for k in range(5):
                        a=k*math.tau/5
                        geo.ellipsoid(m,(xx+.09*math.cos(a),yy+.09*math.sin(a),z-.16),(.09,.08,.05),geo.jitter(K.HOT,rng,.25),rings=3,segments=5)
                    K.ball(m,(xx,yy,z-.20),(.05,.05,.03),(.89,.37,.42))
        for x in (-9,9):neon_heart(m,x,y-1.6,z-.08,.36)
    for side in (-1,1):
        for z in (-42,-33,-24):lantern(m,side*23.4,0,z,1.05)
    return m
