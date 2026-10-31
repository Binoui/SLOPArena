"""Moonlit sea, distant cliffs, forest and sky, outside the playable death space."""
import math
import random
from stagekit import geo
import parts as K
from dressing import dead_tree


def pinnacle(m,x,z,base,h,r,seed):
    rng=random.Random(seed);rings=[];n=7
    for y,s,shift in ((base,1,0),(base+h*.3,.94,.1),(base+h*.68,.74,.23),(base+h,.28,.38)):
        ring=[]
        for i in range(n):
            a=i*math.tau/n;rr=r*s*rng.uniform(.74,1.1)
            ring.append((x+rr*math.cos(a)+shift*r,y+(0 if s==.28 else rng.uniform(-.08,.08)*h),z+rr*math.sin(a)))
        rings.append(ring)
    for k in range(3):
        for i in range(n):
            j=(i+1)%n
            pts=[rings[k][i],rings[k+1][i],rings[k+1][j],rings[k][j]]
            geo.oriented(m,pts,(pts[0][0]-x,.1,pts[0][2]-z),geo.jitter((.12,.16,.23),rng,.28))
    geo.oriented(m,rings[-1],(0,1,0),(.20,.23,.29))


def scenery(detail):
    m=K.mesh('Outside Blast Scenery');rng=random.Random(44)
    for side in (-1,1):
        for i,z in enumerate(range(-58,94,14)):
            x=side*rng.uniform(42,57)
            h,r=rng.uniform(11,21),rng.uniform(4.8,8.0)
            pinnacle(m,x,z,-18,h,r,i+int(x*10))
            if detail and i%2==0:dead_tree(m,x+.38*r,-18+h-.08,z,rng.uniform(7,12),i+5)
        for i in range(13):
            x=side*rng.uniform(70,140);z=rng.uniform(95,195)
            pinnacle(m,x,z,-21,rng.uniform(20,47),rng.uniform(6,14),i+300)
        x0,x1=(38,128) if side>0 else (-128,-38)
        z0,z1=-143,-68
        nx,nz=15,15
        def height(ix,iz):
            return -13+4*max(0,1-((ix-7.5)/8)**2-((iz-7.5)/8)**2)+.35*math.sin(ix*.8+iz*.6)
        for iz in range(nz):
            for ix in range(nx):
                pts=[(x0+(x1-x0)*i/nx,height(i,j),z0+(z1-z0)*j/nz) for i,j in ((ix,iz),(ix+1,iz),(ix+1,iz+1),(ix,iz+1))]
                for ids in ((0,1,2),(0,2,3)):geo.oriented(m,[pts[k] for k in ids],(0,1,0),(.045,.070,.085))
        def support(x,z):
            u=(x-x0)*nx/(x1-x0);v=(z-z0)*nz/(z1-z0)
            ix,iz=math.floor(u),math.floor(v);a,b=u-ix,v-iz
            if a>=b:return height(ix,iz)*(1-a)+height(ix+1,iz)*(a-b)+height(ix+1,iz+1)*b
            return height(ix,iz)*(1-b)+height(ix+1,iz+1)*a+height(ix,iz+1)*(b-a)
        for i in range(36):
            x=side*rng.uniform(43,115);z=rng.uniform(-136,-76);y=support(x,z)
            h=rng.uniform(9,18)
            geo.cylinder_between(m,(x,y-.1,z),(x,y+h,z),.23,5,K.INK)
            for t in range(3):
                yy=y+h*(.38+t*.22)
                geo.lathe(m,(x,yy,z),[(h*(.22-.04*t),0),(.01,h*.47)],7,(.035,.075,.09))
        for i in range(12):
            x=side*rng.uniform(48,110);z=rng.uniform(-135,-104);bottom=support(x,z)-.4;h=rng.uniform(2,5)
            K.box(m,(x,bottom+h/2,z),(rng.uniform(2,5),h,3),(.045,.065,.10))
            K.box(m,(x,bottom+h*.5,z+1.51),(.20,.30,.02),(.34,.20,.08),mat='CandleGlow')
    # Every near jagged rock is below killY, so none suggests a recovery foothold.
    for i in range(36):
        x=rng.uniform(-33,33);z=rng.uniform(17,93)
        pinnacle(m,x,z,-20,rng.uniform(3,6.4),rng.uniform(.4,2.2),i+500)
    moon(m)
    m.poly([(-44,29,115),(-44,63,115),(-10,63,115),(-10,29,115)],(1,1,1),'MoonHalo',uvs=[(0,0),(0,1),(1,1),(1,0)])
    return m


def moon(m):
    rng=random.Random(912)
    maria=[(geo._norm((rng.uniform(-1,1),rng.uniform(-1,1),rng.uniform(-1,1))),rng.uniform(.10,.4),rng.uniform(.025,.08)) for _ in range(18)]
    def vertex(r,s):
        phi=math.pi*r/48-math.pi/2;theta=math.tau*s/112
        n=(math.cos(phi)*math.cos(theta),math.sin(phi),math.cos(phi)*math.sin(theta))
        point=(-27+n[0]*10,46+n[1]*10,115+n[2]*10)
        shade=1
        for direction,radius,depth in maria:
            d=sum((n[i]-direction[i])**2 for i in range(3))
            shade-=depth*math.exp(-d/radius)
        shade+=.012*math.sin(n[0]*39+n[2]*16)*math.sin(n[1]*34-n[2]*27)
        return point,geo.scale((.57,.63,.75),shade)
    for r in range(48):
        for s in range(112):
            corners=[vertex(a,b) for a,b in ((r,s),(r+1,s),(r+1,s+1),(r,s+1))]
            m.poly([v[0] for v in corners],[v[1] for v in corners],'MoonSurface')


def sea():
    m=K.mesh('Moonlit Sea');rng=random.Random(83)
    # The complete animated swell remains at least 1.7 m below the kill plane.
    for x in range(-420,420,10):
        for z in range(-420,420,10):
            c=geo.jitter((.025,.050,.085),rng,.10)+(0,)
            geo.oriented(m,[(x,-14,z),(x+10,-14,z),(x+10,-14,z+10),(x,-14,z+10)],(0,1,0),c,'ManorWater')
    return m


def sky():
    m=K.mesh('Starry Gradient Sky');rng=random.Random(919)
    n=64
    for j in range(10):
        a0=-.12+(math.pi/2+.12)*j/10;a1=-.12+(math.pi/2+.12)*(j+1)/10
        for i in range(n):
            t0,t1=i*math.tau/n,(i+1)*math.tau/n
            pts=[(430*math.cos(a)*math.sin(t),430*math.sin(a),11+430*math.cos(a)*math.cos(t)) for a,t in ((a0,t0),(a1,t0),(a1,t1),(a0,t1))]
            cols=[geo.mix((.065,.085,.14),(.012,.025,.065),max(0,math.sin(a))) for a in (a0,a1,a1,a0)]
            m.poly(pts,cols,'NightSky')
    for i in range(230):
        a=rng.uniform(.12,1.38);t=rng.uniform(0,math.tau)
        x,y,z=425*math.cos(a)*math.sin(t),425*math.sin(a),11+425*math.cos(a)*math.cos(t)
        s=rng.uniform(.18,.58);v=rng.uniform(.35,.7)
        geo.ellipsoid(m,(x,y,z),(s,s,s),(v*.8,v*.88,v),'NightSky',rings=2,segments=4)
    return m


def textures(out):
    import bpy
    rng=random.Random(190)
    for name in ('ManorGrit','ManorRipples'):
        im=bpy.data.images.new(name,width=256,height=256,alpha=False)
        pixels=[]
        for y in range(256):
            for x in range(256):
                u,v=x*math.tau/256,y*math.tau/256
                if name=='ManorGrit':
                    a=.49+rng.uniform(-.16,.16)+.055*math.sin(u*7+math.sin(v*3))+.035*math.sin(v*19)
                    pixels.extend((a,a,a,1))
                else:
                    dx=.24*math.cos(u*9+v*4)+.13*math.cos(v*13-u*3)
                    dz=.24*math.sin(v*11+u*6)+.12*math.sin(u*15-v*5)
                    pixels.extend((dx*.5+.5,dz*.5+.5,math.sqrt(1-dx*dx-dz*dz)*.5+.5,1))
        im.pixels.foreach_set(pixels);im.filepath_raw=str(out/(name+'.png'));im.file_format='PNG';im.save()
