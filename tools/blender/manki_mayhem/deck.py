"""Functional silhouette and the DIY scaffold/hoarding sides; all top paint is flush."""
import math
from stagekit import geo
import palette as P
from parts import Side, box, rect, plate, beam, ribbon, LIT, FLOOR


def build(b, detail=True):
    m=b.group('Concrete Deck','surface')
    # One perfectly flat UV-mapped surface: no decals floating above the deck.
    geo.oriented(m,[(-12,0,-10),(12,0,-10),(12,0,10),(-12,0,10)],(0,1,0),(1,1,1),FLOOR,
                 [(0,0),(1,0),(1,1),(0,1)])
    m=b.group('Deck Hoarding','wall')
    for k in range(4):
        s=Side(k); half=12 if k%2==0 else 10; w=10 if k%2==0 else 12
        # Match the closed authoritative silhouette exactly, including its calm top band.
        rect(m,s,-half,half,-11,0,w,P.DARK_BAND,LIT,1)
        rect(m,s,-half,half,-.85,-.02,w+.006,P.DARK_BAND,LIT,1)
        n=round(half*2/2)
        for i in range(n):
            a=-half+i*half*2/n; c=a+half*2/n
            rect(m,s,a+.018,c-.018,-10.9,-.95,w+.008,P.PLYWOOD,LIT,1)
            if detail:
                plate(m,s,'plywood',(a+c)/2,-5.9,w+.010,c-a-.06,9.6,LIT,facing=1)
                box(m,s,((a+c)/2,-.32,w-.01),(c-a-.03,.50,.035),P.STEEL,LIT)
        # Dark band remains quiet. Sparse hazard segments describe the actual lip.
        for u in range(-half+1,half,4):
            plate(m,s,'hazard',u,-.29,w+.021,2.0,.32,LIT,facing=1)
        if detail:
            normal=tuple(-v for v in s.inward)
            # Framing reads on top of the panels while remaining inside the same
            # 2.5 cm cosmetic tolerance as Slop Pit's recovery walls.
            us=[-half+.15+i*(2*half-.30)/6 for i in range(7)]
            for u in us:
                rect(m,s,u-.075,u+.075,-10.9,-.95,w+.023,P.STEEL,LIT,1)
            for yy in (-1.1,-5.9,-10.7):
                rect(m,s,-half+.08,half-.08,yy-.07,yy+.07,w+.023,P.STEEL,LIT,1)
            for a,c in zip(us,us[1:]):
                for ya,yb in ((-1.25,-5.75),(-6.05,-10.55)):
                    ribbon(m,s.P(a+.1,ya,w+.024),s.P(c-.1,yb,w+.024),.10,normal,P.STEEL,LIT)
                    if int(a)%2==0:
                        ribbon(m,s.P(c-.1,ya,w+.024),s.P(a+.1,yb,w+.024),.075,normal,P.STEEL,LIT)
            for i,u in enumerate((-half*.53,half*.48)):
                cell=('poster_manki','poster_fightguy','poster_wibou','poster_bonk')[(i+k)%4]
                plate(m,s,cell,u,-5.4,w+.017,1.0,1.5,LIT,facing=1)
            # Flat chain silhouettes and cable bundles remain within the 2.5 cm tolerance.
            for u in (-half*.83,half*.85):
                for j in range(22):
                    yy=-1.4-j*.31
                    rect(m,s,u-.035,u+.035,yy-.20,yy,w+.019,P.STEEL,LIT,1)
                for off in (-.10,0,.10):
                    pts=[s.P(u+off+.15*math.sin(j*.4),-1.2-j*.38,w+.018) for j in range(23)]
                    for a,c in zip(pts,pts[1:]): ribbon(m,a,c,.024,tuple(-v for v in s.inward),P.INK,LIT)
    # Scaffold tower continues below KO to an actual warehouse floor, with no bright junk
    # under the recovery edge. Keep every strut inset from the actual collision sides.
    m=b.group('Deck Scaffold Tower','wall')
    for k in range(4):
        s=Side(k); half=11.5 if k%2==0 else 9.5; w=9.7 if k%2==0 else 11.7
        for u in (-half,0,half):
            box(m,s,(u,-19.5,w),(.30,17,.30),P.STEEL,LIT)
        for yy in (-12,-18,-24,-27.8):
            box(m,s,(0,yy,w),(half*2,.20,.20),P.STEEL,LIT)
        for sign in (-1,1):
            beam(m,s.P(0,-12,w),s.P(sign*half,-24,w),.16,P.STEEL,LIT)
