"""Three scaffold galleries and side identities inside a deep, dark warehouse."""
import math
from stagekit import geo
import palette as P
from parts import Side, box, beam, plate, scaffold, truss, string, lamp, glow, speaker, crate, crt, chain_link, fan, paint_text, disc, NIGHT, rect, shaft


def stands(b,detail):
    m=b.group('Scaffold Stands','background')
    crowd=b.group('Audience','background')
    fence=b.group('Audience Fences','background')
    rng=b.rng; count=0
    for k in (1,2,3):
        s=Side(k); front=39 if k%2 else 37
        for w in (front,front+9): scaffold(m,s,-29,29,w,-28,9,bay=4)
        for row in range(3):
            yy=-1.3+row*3.3; ww=front+row*3.1
            box(m,s,(0,yy-.15,ww+1.8),(58,.3,3.6),P.PLYWOOD)
            # Keep the central television desk/entrance clear of the audience.
            for line in range(2 if detail else 1):
                for col in range(55):
                    u=-27+col+line*.35
                    if k in (2,3) and abs(u)<8: continue
                    count+=1
                    fan(crowd,s,u+rng.uniform(-.16,.16),ww+.85+line*1.35,yy,rng,P)
                    if detail and line==0 and col%13==0:
                        with glow(crowd,(51.1,rng.random()),.4):
                            plate(crowd,s,'fan',u,yy+2.4,ww+.5,1.35,.67)
            for u in range(-28,29,4):
                box(fence,s,(u,yy+1.1,ww-.10),(.10,2.4,.10),P.STEEL)
            for rail_y in (yy+.15,yy+1.65):
                box(fence,s,(0,rail_y,ww-.10),(58,.065,.065),P.SCAFFOLD)
            if detail:
                chain_link(fence,s,-28,28,yy+.15,yy+1.65,ww-.10,.38,.012,P.STEEL)
                for u in range(-26,27,7): lamp(m,s,u,yy+.35,ww-.25,.09,anim=(50.45,(u+26)/52))
        if detail:
            for i,u in enumerate((-23,-12,12,23)):
                plate(m,s,('poster_manki','poster_fightguy','poster_wibou','poster_bonk')[i],u,2.4,front-.12,1.3,1.95)
    return count


def identities(b,detail):
    m=b.group('Side Identities','background')
    east=Side(1); west=Side(3); south=Side(2)
    box(m,east,(0,6.9,39),(18,2.8,1),P.INK)
    with glow(m,(1.1,.1),.65): plate(m,east,'applause',0,6.9,38.48,16.8,2.1)
    # West: low entrance, theatrical folds, chalk board (not another hero structure).
    box(m,west,(0,1.5,39.5),(11,6,1),P.INK)
    for i in range(20):
        u=-4.5+i*.47
        box(m,west,(u,.1,38.85),(.48,4.1,.38 if i%2 else .18),geo.scale(P.CURTAIN,.8+.3*(i%2)))
    box(m,west,(0,3.3,39),(11.5,1.5,.7),P.STEEL)
    with glow(m,(0,0),.35): plate(m,west,'contestants',0,3.3,38.63,10.8,1.25)
    box(m,west,(-9.2,1.8,39),(4.6,4.6,.25),P.PLYWOOD)
    plate(m,west,'scoreboard',-9.2,1.8,38.86,4.3,4.3)
    # South: a small production island with real table thickness, TV dolly and monitors.
    box(m,south,(0,-.85,40),(15,.4,6),P.STEEL)
    box(m,south,(0,.65,39.9),(10,.23,1.7),P.PLYWOOD)
    for u in (-4.6,4.6): box(m,south,(u,-.06,40),(.17,1.5,1.45),P.STEEL)
    for i in range(4): crt(m,south,-3.6+i*2.4,1.55,40,1.7,1.1,'static' if i%2 else 'manki',i*.21)
    plate(m,south,'on-air',4.9,3.2,39.6,3, .75)
    with glow(m,(0,0),.8): lamp(m,south,6.8,3.1,40,.23,P.RED)
    # Large old TV camera: pedestal, tripod dolly, lens facing the deck and reel/tally.
    u,w=-7.7,38.8
    box(m,south,(u,.6,w),(.18,2.8,.18),P.STEEL)
    for du,dw in ((-.8,-.6),(.8,-.6),(0,.8)):
        beam(m,south.P(u,.2,w),south.P(u+du,-.7,w+dw),.12,P.STEEL)
        geo.ellipsoid(m,south.P(u+du,-.69,w+dw),(.19,.19,.19),P.INK,rings=3,segments=8)
    box(m,south,(u,2.1,w),(1.5,.9,1.9),P.STEEL)
    geo.cylinder_between(m,south.P(u,2.1,w-.8),south.P(u,2.1,w-1.8),.39,12,P.INK)
    disc(m,south.P(u,2.1,w-1.81),south.inward,.30,(.10,.14,.15))
    lamp(m,south,u,2.76,w,.09,P.RED)
    if detail:
        for i in range(9): box(m,south,(-2.9+i*.7,.80,39.65),(.35,.05,.3),P.INK)
        paint_text(m,south,'BROADCAST CONTROL',0,-.25,36.98,.55,P.PAPER)
        for s in (east,west,south):
            for u in (-25,25): speaker(m,s,u,-1.3,39.4,1.8,3)


def rig(b,detail):
    m=b.group('Lighting Rig','background')
    beams=b.group('Warm Light Shafts','background')
    for k in range(4):
        s=Side(k); w=38 if k%2 else 36.6
        # No rig crosses the open air above the gameplay deck.
        truss(m,s,-31,31,16.8 if k==0 else 14.6,w)
        for u in (-30,30): scaffold(m,s,u-.55,u+.55,w,-28,14.8,bay=2)
        # Keep front wires off the giant host's face/body in the north bay.
        for u0,u1 in ((-30,-11),(11,30)) if k==0 else ((-30,30),):
            string(m,s,u0,u1,12.6,w-.25,round((u1-u0)*1.1),1.0)
            string(m,s,u0,u1,8.9,w+.25,round((u1-u0)*.95),.7)
        # Another string in depth turns the far bulbs into small soft bloom dots.
        string(m,s,-32,32,10.4,49.5,48,.85,.14)
        for u in (-25,-15,15,25):
            # Physical par can, angled down and inward; the separate Unity rig lights the deck.
            a=s.P(u,13.7,w); c=s.P(u,13.15,w-.45)
            geo.cylinder_between(m,a,c,.30,10,P.INK)
            with glow(m,(0,0),1): disc(m,c,tuple(c[i]-a[i] for i in range(3)),.28,P.LAMP)
            # Light is carried by the shafts and painted venue pools, not a deck exposure boost.
            shaft(beams,s,u,13.15,w-.45,u,-2.0,w+1,1.5)
            beam(m,s.P(u,14.3,w),a,.065,P.STEEL)
    if detail:
        north=Side(0)
        for u in (-8,8):
            beam(m,north.P(u,12.7,39),north.P(u,12.7,38),.10,P.STEEL)
            box(m,north,(u,12.3,38.3),(1,.55,.35),P.INK)
            with glow(m,alpha=1): plate(m,north,'hazard',u,12.3,38.1,.8,.3)
        for u in (-9,9):
            box(m,north,(u,15.5,38.2),(1.8,.7,.8),P.INK)
            with glow(m):
                for du in (-.53,0,.53): disc(m,north.P(u+du,15.35,37.75),north.inward,.22,P.LAMP)
            shaft(beams,north,u,15.25,37.7,u*.4,6.7,40.5,1.8)


def backlights(b):
    """Slop Pit's lit risers: fans and fencing silhouette against warm scalloped bays."""
    m=b.group('Venue Backlight','background')
    for k in range(4):
        s=Side(k); front=39 if k%2 else 37
        # North is a distant backstage wall behind the booth/screens. The other
        # three sides have a separate luminous wall behind each gallery row.
        levels=[(-5,17,53)] if k==0 else [(-1.3+r*3.3,2.4+r*3.3,front+r*3.1+2.95) for r in range(3)]
        for row,(bottom,top,w) in enumerate(levels):
            for bay in range(10):
                u0=-28+bay*5.6
                hot=.65+.24*math.sin(bay*2.3+k+row)**2
                for ix in range(4):
                    for iy in range(4):
                        pts=[(u0+ix*1.4,bottom+(top-bottom)*iy/4),
                             (u0+(ix+1)*1.4,bottom+(top-bottom)*iy/4),
                             (u0+(ix+1)*1.4,bottom+(top-bottom)*(iy+1)/4),
                             (u0+ix*1.4,bottom+(top-bottom)*(iy+1)/4)]
                        def col(u,y):
                            fy=(y-bottom)/(top-bottom); fu=max(0,1-abs(u-(u0+3))/4)
                            return geo.mix((.12,.065,.035),(.64,.32,.13),hot*(.25+.75*fy)*(.35+.65*fu))
                        with glow(m,alpha=.65):
                            geo.oriented(m,[s.P(u,y,w) for u,y in pts],s.inward,[col(u,y) for u,y in pts],NIGHT)
                lamp(m,s,u0+2.8,top-.12,w-.10,.16)
                # Panel joints ground the glow on real metal, rather than a light card.
                for du in (0,2.8):
                    box(m,s,(u0+du,(bottom+top)/2,w-.025),(.045,top-bottom,.035),(.18,.105,.05))
    # Far walls are quieter and cooler than the tungsten risers, behind columns.
    m=b.group('Warehouse Light Pools','background')
    for k in range(4):
        s=Side(k)
        for bay in range(8):
            u0=-56+bay*14
            for iy in range(6):
                y0=-28+iy*9.7; y1=y0+9.7
                color0=geo.mix((.085,.073,.065),(.19,.155,.12),max(0,1-abs(y0-5)/22))
                color1=geo.mix((.085,.073,.065),(.19,.155,.12),max(0,1-abs(y1-5)/22))
                with glow(m,alpha=.22):
                    geo.oriented(m,[s.P(u0,y0,60.45),s.P(u0+14,y0,60.45),s.P(u0+14,y1,60.45),s.P(u0,y1,60.45)],s.inward,
                                 [color0,color0,color1,color1],NIGHT)
        # Low, broad warehouse pools preserve depth under the KO plane.
        for u in (-37,0,37):
            with glow(m,alpha=.25):
                rect(m,s,u-4.7,u+4.7,-27,-7,59.6,(.19,.155,.12),NIGHT)
            for j in range(20): box(m,s,(u,-27+j,59.5),(9.5,.06,.08),(.17,.14,.11))


def warehouse(b,detail):
    m=b.group('Warehouse','background')
    # Far floor below KO, anchored columns and roll-up door recesses. No water.
    geo.box(m,(0,-28.5,0),(160,1,160),P.WAREHOUSE)
    for k in range(4):
        s=Side(k)
        box(m,s,(0,2,61),(123,60,1),P.WAREHOUSE)
        for u in range(-56,57,14):
            box(m,s,(u,1.5,59.9),(1.4,59,1.5),P.CONCRETE)
            box(m,s,(u,30,54),(1.4,1,12),P.STEEL)
        # A ceiling only over the stands; open dark void over the deck/camera envelope.
        box(m,s,(0,25,51),(116,.5,18),P.INK)
        box(m,s,(0,24.3,45),(116,.7,.5),P.STEEL)
        for u in (-37,0,37):
            box(m,s,(u,-17.5,60),(10,21,.5),P.INK)
            for j in range(20): box(m,s,(u,-27+j,59.65),(9.5,.10,.11),P.STEEL)
            with glow(m,(0,0),.35): plate(m,s,'exit',u,-6.5,59.5,1.65,.82)
        if detail:
            for i in range(12):
                u=b.rng.uniform(-48,48); w=b.rng.uniform(22,54)
                # All ground clutter far below the KO line, never a recovery obstruction.
                crate(m,s,u,-28,w,b.rng.uniform(1,2.4))
            for u in (-21,21):
                # Cable spool with two flanges, standing on the floor.
                p=s.P(u,-26.8,27)
                geo.cylinder_between(m,(p[0],-27.7,p[2]),(p[0],-25.9,p[2]),.55,10,P.CRATE)
                for yy in (-27.8,-25.8): geo.cylinder_between(m,(p[0],yy-.1,p[2]),(p[0],yy+.1,p[2]),1.1,12,P.PLYWOOD)
                lamp(m,s,u,-25.4,30,.14,anim=(0,0))


def build(b,detail=True):
    warehouse(b,detail)
    backlights(b)
    count=stands(b,detail)
    identities(b,detail)
    rig(b,detail)
    return count
