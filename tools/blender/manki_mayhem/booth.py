"""Raised scaffold/crate host tower, with an unobstructed future giant-host bay."""
import math
from stagekit import geo
import palette as P
from parts import Side, box, beam, plate, scaffold, speaker, crate, crt, lamp, glow, string

# Unity coordinates; feet sit on the platform, not on the banner or a crate.
HOST_ANCHOR = {'MankiHostAnchor': (0.0, 6.5, 41.5)}
PLATFORM = {'height': 6.5, 'footprint': [20.0, 10.0],
            'boundsXZ': [-10.0, 10.0, 37.5, 47.5],
            'clearHostBoundsXZ': [-4.5, 4.5, 38.5, 45.0],
            'clearHostHeight': 8.0}


def build(b,detail=True):
    s=Side(0)
    m=b.group('Host Booth','background')
    # Front braces stop at the platform: the 8 m tall host window stays open.
    for w in (37.8,47.2): scaffold(m,s,-9.5,9.5,w,-28,6.3,bay=4.75)
    for u in (-9.5,9.5):
        scaffold(m,s,u-.45,u+.45,37.8,6.3,16.8,bay=1)
        scaffold(m,s,u-.45,u+.45,47.2,6.3,16.8,bay=1)
        for yy in (-25,-15,-5,2,6.2,16.5):
            beam(m,s.P(u,yy,37.8),s.P(u,yy,47.2),.16,P.SCAFFOLD)
    box(m,s,(0,16.6,38.0),(20,.24,.24),P.SCAFFOLD)
    box(m,s,(0,6.25,42.5),(20,.5,10),P.PLYWOOD)
    # Crate-filled plinth reads as a tower instead of an unsupported desk/banner.
    for u in (-7.8,-4.7,4.7,7.8):
        for yy in (-2.8,.2,3.2): crate(m,s,u,yy,39.4,2.9)
    box(m,s,(0,3.9,37.65),(18,4.4,.22),P.CRATE)
    with glow(m,alpha=.10): plate(m,s,'banner',0,3.9,37.52,17.5,4.4)
    # Storage at the far corners leaves a 9 x 6.5 m clear host footprint.
    for u in (-8,8):
        for yy in (6.5,8.5,10.5): crate(m,s,u,yy,46.1,2.0)
    # Rear drape frames the host; no rail, cross-brace or sign crosses his body.
    box(m,s,(0,10.4,47.3),(12,7.8,.18),P.CURTAIN)
    plate(m,s,'off-air',0,14.9,47.18,5.0,.75)
    for u in (-11.6,11.6):
        for yy in (-.5,3.7,7.9): speaker(m,s,u,yy,39.3,2.5,4.1)
    for u in (-9.2,9.2): lamp(m,s,u,10.8,37.65,.27)
    string(m,s,-9,9,6.15,37.38,26,.25)
    if detail:
        # Giant-host mic at the left of the clear bay; a 3–4x fighter host can reach it.
        x,y,z=-6.0,6.5,39.1
        for k in range(3):
            a=k*math.tau/3
            beam(m,(x,y+.1,z),(x+math.cos(a)*.6,y+.035,z+math.sin(a)*.6),.07,P.INK)
        beam(m,(x,y+.1,z),(x,y+4.2,z),.095,P.STEEL)
        beam(m,(x,y+4.2,z),(x+1.0,y+4.6,z-.35),.075,P.STEEL)
        geo.cylinder_between(m,(x+.85,y+4.54,z-.30),(x+1.3,y+4.75,z-.48),.23,12,P.STEEL)
        for k in range(10):
            bx=5.7+(k%3)*.92; by=6.5+.55+(k//3)*.80; bz=39.5+(k%2)*.65
            geo.ellipsoid(m,(bx,by,bz),(.57,.57,.57),P.INK,rings=5,segments=10)
            geo.cylinder_between(m,(bx,by+.52,bz),(bx+.15,by+.88,bz),.06,6,P.PAPER)
        for u in (-8.8,8.8):
            for v in (2,5.5): box(m,s,(u,v,37.39),(.18,.18,.08),P.STEEL)
    m=b.group('CRT Walls','background')
    for sign in (-1,1):
        x=sign*22.5
        for w in (38.0,42.5): scaffold(m,s,x-8.4,x+8.4,w,-28,14.7,bay=4.2)
        box(m,s,(x,.85,40),(17.5,.4,5),P.STEEL)
        crt(m,s,x,7.4,37.7,10.4,6.3,'logo',phase=.21 if sign<0 else .68)
        # Five irregular side stacks and two packed rows: 26 small sets per wall.
        for side in (-1,1):
            for j in range(5):
                width=(1.9,2.25,1.7,2.1,1.8)[j]
                crt(m,s,x+side*(6.75+.12*(j%2)),2.15+j*2.45,37.7+.15*(j%3),width,1.75+.15*(j%2),
                    'static' if (j+side+sign)%3==0 else 'manki',phase=(j*.17+side*.11)%1,
                    tilt=side*(4 if j%2 else -3))
        if detail:
            for row,yy in enumerate((2.55,12.6)):
                for j in range(6):
                    crt(m,s,x-5.0+j*2.0,yy+.16*(j%2),38.0+.2*(j%3),
                        1.3+.18*(j%3),1.45+.12*((j+row)%2),
                        'manki' if (j+row)%2 else 'static',phase=j*.13+row*.19,tilt=(-1)**j*4)
            for j in range(4):
                crt(m,s,x-4.3+j*2.9,.75,37.8,2.1,1.0,'static' if j%2 else 'manki',phase=j*.21,tilt=2-j)
            string(m,s,x-8,x+8,.30,37.25,22,.12)
