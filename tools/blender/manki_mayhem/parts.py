"""Stage motifs built on stagekit; Slop Pit supplies the proven crowd/fence shapes."""
from __future__ import annotations
import math
from stagekit import geo, font
from slop_pit.parts import Side, rect, flat, end, box, beam, ribbon, chain_link, fan
import palette as P
import textures as T

NIGHT, LIT, FLOOR, SCREEN = 'MM_Night', 'MM_Lit', 'MM_Floor', 'MM_Screen'
MATERIALS = [NIGHT, LIT, FLOOR, SCREEN, "MM_Beam"]


def plate(m, side, cell, u, y, w, width, height=None, mat=NIGHT, color=(1,1,1), facing=-1):
    _,_,tw,th=T.CELLS[cell]
    height=height or width*th/tw
    rect(m,side,u-width/2,u+width/2,y-height/2,y+height/2,w,color,mat,facing,T.uv(cell))


def glow(m, anim=(0,0), alpha=1):
    return geo.state(m,alpha=alpha,anim=anim)


def lamp(m, side, u,y,w, radius=.13, color=P.LAMP, anim=(0,0)):
    with glow(m,anim):
        geo.ellipsoid(m,side.P(u,y,w),(radius,radius*1.2,radius),color,NIGHT,rings=4,segments=6)


def scaffold(m, side, u0,u1,w,y0,y1,bay=3.0):
    n=max(1,math.ceil((u1-u0)/bay))
    rows=max(1,math.ceil((y1-y0)/3.2))
    for i in range(n+1):
        u=u0+(u1-u0)*i/n
        box(m,side,(u,(y0+y1)/2,w),(.12,y1-y0,.12),P.SCAFFOLD)
        for j in range(rows+1):
            y=y0+(y1-y0)*j/rows
            box(m,side,(u,y,w),(.22,.14,.22),P.STEEL)
    for j in range(rows+1):
        y=y0+(y1-y0)*j/rows
        box(m,side,((u0+u1)/2,y,w),(u1-u0,.12,.12),P.SCAFFOLD)
    for i in range(n):
        ua,ub=u0+(u1-u0)*i/n,u0+(u1-u0)*(i+1)/n
        for j in range(rows):
            ya,yb=y0+(y1-y0)*j/rows,y0+(y1-y0)*(j+1)/rows
            beam(m,side.P(ua,ya,w),side.P(ub,yb,w),.07,P.STEEL)
            if (i+j)%2==0: beam(m,side.P(ub,ya,w),side.P(ua,yb,w),.065,P.STEEL)


def truss(m,side,u0,u1,y,w):
    for yy in (y-.32,y+.32):
        for ww in (w-.32,w+.32): beam(m,side.P(u0,yy,ww),side.P(u1,yy,ww),.10,P.STEEL)
    count=max(1,math.ceil((u1-u0)/1.5))
    for i in range(count):
        a,b=u0+(u1-u0)*i/count,u0+(u1-u0)*(i+1)/count
        for ww in (w-.32,w+.32):
            beam(m,side.P(a,y-.32,ww),side.P(b,y+.32,ww),.055,P.SCAFFOLD)
            beam(m,side.P(a,y+.32,ww),side.P(b,y-.32,ww),.055,P.SCAFFOLD)
        beam(m,side.P(a,y-.32,w-.32),side.P(a,y-.32,w+.32),.055,P.SCAFFOLD)


def string(m,side,u0,u1,y,w,bulbs=28,sag=1,radius=.16):
    last=None
    for k in range(bulbs*2+1):
        t=k/(bulbs*2); u=u0+(u1-u0)*t; yy=y-sag*4*t*(1-t)
        p=side.P(u,yy,w)
        if last: beam(m,last,p,.022,P.INK)
        last=p
        if k%2==0:
            beam(m,p,side.P(u,yy-.19,w),.025,P.INK)
            # Mostly steady bulbs keep the string luminous throughout its chase.
            lamp(m,side,u,yy-.25,w,radius,anim=(50.3,k/(bulbs*2)) if k%6==0 else (0,0))


def crate(m,side,u,y,w,size=1.5):
    box(m,side,(u,y+size/2,w),(size,size,size),P.CRATE)
    for yy in (y+.12,y+size-.12): box(m,side,(u,yy,w-size/2-.025),(size,.15,.05),P.PLYWOOD)
    beam(m,side.P(u-size*.42,y+.16,w-size/2-.06),side.P(u+size*.42,y+size-.16,w-size/2-.06),.15,P.PLYWOOD)


def disc(m,center,normal,r,color,mat=NIGHT,segments=12):
    n=geo._norm(normal); a=geo._norm(geo._cross(n,(0,1,0) if abs(n[1])<.9 else (1,0,0))); b=geo._cross(n,a)
    pts=[tuple(center[j]+r*(a[j]*math.cos(i*math.tau/segments)+b[j]*math.sin(i*math.tau/segments)) for j in range(3)) for i in range(segments)]
    geo.oriented(m,pts,n,color,mat)


def speaker(m,side,u,y,w,width=1.5,height=3):
    box(m,side,(u,y+height/2,w),(width,height,1.25),P.INK)
    for yy,r in ((y+height*.29,width*.35),(y+height*.73,width*.29)):
        disc(m,side.P(u,yy,w-.64),side.inward,r,P.STEEL)
        disc(m,side.P(u,yy,w-.65),side.inward,r*.76,P.DARK_BAND)
        disc(m,side.P(u,yy,w-.66),side.inward,r*.28,P.STEEL)
    for du in (-width*.4,width*.4):
        for yy in (y+.13,y+height-.13): box(m,side,(u+du,yy,w-.64),(.13,.13,.03),P.STEEL)


def crt(m,side,u,y,w,width,height,cell,phase=0,tilt=0):
    first=len(m.verts)
    box(m,side,(u,y,w+.55),(width+.5,height+.45,1.25),P.INK)
    box(m,side,(u,y,w-.09),(width+.2,height+.18,.12),P.STEEL)
    # Front pane is shader-lit; only its glow flickers. Paint carries dark scanlines.
    with glow(m,(-1.1,phase),.72): plate(m,side,cell,u,y,w-.165,width,height,SCREEN)
    with glow(m,(0,0),.2):
        for i in range(34):
            yy=y-height/2+(i+.5)*height/34
            with geo.state(m,anim=(102.0,(i/34+phase)%1)):
                rect(m,side,u-width*.493,u+width*.493,yy,yy+height*.003,w-.17,P.SCREEN,SCREEN)
    for du in (-width*.36,width*.36):
        disc(m,side.P(u+du,y-height/2-.12,w-.18),side.inward,.055,P.SCAFFOLD,segments=8)
    with glow(m,(.7,phase)):
        disc(m,side.P(u+width*.45,y-height/2-.12,w-.18),side.inward,.035,P.RED,segments=8)

    if tilt:
        # Roll the complete cabinet, pane and controls in its own side frame.
        center=side.P(u,y,w); angle=math.radians(tilt); cs,sn=math.cos(angle),math.sin(angle)
        right=side.P(1,0,0)
        for i in range(first,len(m.verts)):
            p=m.verts[i]; du=sum((p[k]-center[k])*right[k] for k in range(3)); dy=p[1]-y
            nu,ny=du*cs-dy*sn,du*sn+dy*cs
            m.verts[i]=(p[0]+right[0]*(nu-du),y+ny,p[2]+right[2]*(nu-du))


def paint_text(m,side,text,u,y,w,height,color):
    strokes,_=font.text_strokes(text,height,origin=(u,y-height/2),tracking=1.1)
    for stroke in strokes:
        for a,b in zip(stroke,stroke[1:]): ribbon(m,side.P(a[0],a[1],w),side.P(b[0],b[1],w),height*.12,side.inward,color)


def shaft(m,side,u,y,w,end_u,end_y,end_w,width=1.8):
    """Soft additive fins, confined to the venue outside the complete camera envelope."""
    top=side.P(u,y,w); bottom=side.P(end_u,end_y,end_w)
    axis=geo._norm(tuple(bottom[i]-top[i] for i in range(3)))
    across=geo._norm(geo._cross(axis,(0,1,0)))
    other=geo._cross(axis,across)
    cols=(-1,-.6,-.2,.2,.6,1)
    for angle in (0,math.pi/3,2*math.pi/3):
        direction=tuple(across[i]*math.cos(angle)+other[i]*math.sin(angle) for i in range(3))
        rows=[]
        for j in range(7):
            t=j/6; center=tuple(top[i]+(bottom[i]-top[i])*t for i in range(3))
            radius=.18+width*t
            rows.append([(tuple(center[i]+direction[i]*radius*c for i in range(3)),
                          .12*(1-t)**1.2*(1-c*c)**2) for c in cols])
        for j in range(6):
            for k in range(len(cols)-1):
                q=[rows[j][k],rows[j][k+1],rows[j+1][k+1],rows[j+1][k]]
                m.poly([p for p,a in q],[(*P.LAMP,a) for p,a in q],'MM_Beam')
