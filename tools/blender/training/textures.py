"""Original painted floor and test labels. Fonts are the project's OFL fonts."""
from pathlib import Path
import math
import numpy as np
from PIL import Image, ImageDraw, ImageFont
import palette as P
from layout import SPEC, bounds

FONTS = Path(__file__).resolve().parents[3] / 'client/Unity/Assets/Fonts'
CELLS = {
    'hero': (0, 0, 1792, 256),
    'ko': (0, 256, 1792, 512),
    'height': (1792, 256, 256, 768),
    'depth4': (0, 800, 512, 256),
    'depth8': (512, 800, 512, 256),
    'camera': (0, 1100, 1024, 256),
    'parts': (1024, 1100, 768, 256),
    'height3': (1792, 1100, 256, 768),
    'plate15': (0, 1400, 1024, 128),
    'plate3': (0, 1540, 1024, 128),
    'shelf': (1024, 1400, 512, 128),
}


def rgb(c):
    return tuple(round(255 * v) for v in c[:3])


def fit(draw, text, rect, color, mono=False):
    x0, y0, x1, y1 = rect
    font_path = FONTS / ('SpaceMono-Regular.ttf' if mono else 'ArchivoBlack-Regular.ttf')
    f = ImageFont.truetype(str(font_path), 100)
    bounds = f.getbbox(text)
    size = max(6, int(100 * min((x1-x0)/max(1,bounds[2]-bounds[0]), (y1-y0)/max(1,bounds[3]-bounds[1]))))
    f = ImageFont.truetype(str(font_path), size)
    draw.text(((x0+x1)/2, (y0+y1)/2), text, font=f, fill=color, anchor='mm')


def uv(name):
    x,y,w,h = CELLS[name]
    return ((x+2)/2048, 1-(y+h-2)/2048), ((x+w-2)/2048, 1-(y+2)/2048)


def atlas(path):
    im = Image.new('RGB', (2048,2048), 'white')
    d = ImageDraw.Draw(im)
    for name, (x,y,w,h) in CELLS.items():
        d.rectangle((x,y,x+w-1,y+h-1), fill=rgb(P.DARK))
        if name == 'hero':
            fit(d,'SLOP LABS · IMPACT TESTING',(x+35,y+35,x+w-35,y+h-35),rgb(P.WHITE))
        elif name == 'ko':
            fit(d,'DAYS WITHOUT A KO:',(x+35,y+65,x+1450,y+h-65),rgb(P.WHITE))
            tile=(x+1500,y+18,x+w-30,y+h-18)
            d.rounded_rectangle(tile,radius=12,fill=rgb(P.WHITE))
            fit(d,'0',(tile[0]+30,tile[1]+10,tile[2]-30,tile[3]-10),rgb(P.BLACK))
            d.line((tile[0]+3,y+h/2,tile[2]-3,y+h/2),fill=rgb(P.DARK),width=3)
        elif name in ('height','height3'):
            count=4
            for i in range(count):
                yy=y+h-25-i*(h-50)/(count-1)
                d.line((x+15,yy,x+75,yy),fill=rgb(P.YELLOW),width=8)
                fit(d, ('0' if i==0 and name=='height' else f'{i*0.5+(1.5 if name=="height3" else 0):g}'), (x+90,yy-28,x+w-15,yy+28),rgb(P.WHITE),True)
        else:
            text={'depth4':'−4 m', 'depth8':'−8 m','camera':'1000 FPS', 'parts':'SPARE PARTS','plate15':'1.5 m · ONE JUMP','plate3':'3 m · DOUBLE JUMP','shelf':'−1.5 m'}[name]
            fit(d,text,(x+25,y+25,x+w-25,y+h-25),rgb(P.WHITE),True)
    im.save(path)


def floor_uv(footprint):
    x0,x1,z0,z1=bounds()
    a,c,d,e=footprint
    return [((a-x0)/(x1-x0),(c-z0)/(z1-z0)),((d-x0)/(x1-x0),(c-z0)/(z1-z0)),
            ((d-x0)/(x1-x0),(e-z0)/(z1-z0)),((a-x0)/(x1-x0),(e-z0)/(z1-z0))]


def deck(path):
    x0,x1,z0,z1=bounds(); ppm=60
    w,h=round((x1-x0)*ppm),round((z1-z0)*ppm)
    im=Image.new('RGB',(w,h),(115,115,115)); d=ImageDraw.Draw(im)
    def px(x,z): return ((x-x0)*ppm,(z1-z)*ppm)
    for x in range(math.ceil(x0),math.floor(x1)+1):
        d.line([px(x,z0),px(x,z1)],fill=(88,90,91) if x%5==0 else (104,105,105),width=3 if x%5==0 else 1)
    for z in range(math.ceil(z0),math.floor(z1)+1):
        d.line([px(x0,z),px(x1,z)],fill=(88,90,91) if z%5==0 else (104,105,105),width=3 if z%5==0 else 1)
    stripe=Image.new('RGB',(w,h),rgb(P.YELLOW)); sd=ImageDraw.Draw(stripe)
    for k in range(-h,w+h,48):
        sd.polygon([(k,0),(k+24,0),(k+24+h,h),(k+h,h)],fill=rgb(P.BLACK))
    mask=Image.new('L',(w,h),0); md=ImageDraw.Draw(mask)
    for solid in SPEC['solids']:
        a,c,b,e=solid['footprint']; p=px(a,e); q=px(b,c)
        md.rectangle((*p,*q),outline=255,width=14)
    im.paste(stripe,(0,0),mask); d=ImageDraw.Draw(im)
    for index,(x,z,yaw) in enumerate(SPEC['spawns']):
        cx,cy=px(x,z)
        if index==0:
            d.rectangle((cx-ppm,cy-1.1*ppm,cx+ppm,cy+1.1*ppm),outline=rgb(P.WHITE),width=4)
            continue
        rr=(1.25 if index==1 else .72)*ppm
        d.ellipse((cx-rr,cy-rr,cx+rr,cy+rr),fill=rgb(P.YELLOW),outline=rgb(P.BLACK),width=5)
        for angle in (0,180): d.pieslice((cx-rr,cy-rr,cx+rr,cy+rr),angle,angle+90,fill=rgb(P.BLACK))
        d.ellipse((cx-rr*.3,cy-rr*.3,cx+rr*.3,cy+rr*.3),fill=rgb(P.YELLOW),outline=rgb(P.BLACK),width=3)
        d.line((cx-rr-10,cy,cx+rr+10,cy),fill=rgb(P.BLACK),width=2)
        d.line((cx,cy-rr-10,cx,cy+rr+10),fill=rgb(P.BLACK),width=2)
    for distance in range(1,18):
        z=4-distance; half=.60 if distance%5==0 else .28
        d.line([px(-half,z),px(half,z)],fill=rgb(P.WHITE),width=3 if distance%5==0 else 2)
    for distance in (5,10,15):
        cx,cy=px(1.25,4-distance)
        fit(d,str(distance),(cx-27,cy-20,cx+27,cy+20),rgb(P.WHITE),True)
    cx,cy=px(0,-12)
    d.rectangle((cx-222,cy-21,cx+222,cy+21),fill=rgb(P.TEAL))
    fit(d,'BEELINE 16 m',(cx-207,cy-17,cx+207,cy+17),rgb(P.WHITE),True)
    def stencil(text,x,z,width,height,color=P.WHITE,rotation=0):
        cx,cy=px(x,z)
        if rotation:
            tile=Image.new('RGBA',(round(width*ppm),round(height*ppm)))
            fit(ImageDraw.Draw(tile),text,(0,0,tile.width,tile.height),(*rgb(color),255),True)
            tile=tile.rotate(rotation,expand=True)
            im.paste(tile,(round(cx-tile.width/2),round(cy-tile.height/2)),tile)
        else:
            fit(d,text,(cx-width*ppm/2,cy-height*ppm/2,cx+width*ppm/2,cy+height*ppm/2),rgb(color),True)
    stencil('DRILL YARD',-24,-10.5,10,1.6)
    stencil('TRENCH 3 m',-13.1,-4,5,.8,P.YELLOW,90)
    for solid in SPEC['solids']:
        if 'gap' not in solid: continue
        a,c,b,e=solid['footprint']; gap=solid['gap']; z=(c+e)/2
        stencil(f'{gap:g} m',(a+b)/2,z+(.95 if solid['name']=='MidIsland' else 0),3.2,.95)
        stencil(f'{gap:g} m',13.1,z+1.5,2.6,.65,P.YELLOW,90)
        arrow=[px(13.65,z),px(13.05,z+.30),px(13.05,z+.12),px(12.75,z+.12),px(12.75,z-.12),px(13.05,z-.12),px(13.05,z-.30)]
        d.polygon(arrow,fill=rgb(P.YELLOW))
    im.save(path)


def detail(path):
    rng=np.random.default_rng(20261029)
    a=np.clip(rng.normal(128,9,(256,256)),0,255).astype(np.uint8)
    Image.fromarray(a,'L').save(path)


def write(out):
    atlas(out/'TrainingAtlas.png')
    deck(out/'TrainingFloor.png')
    detail(out/'TrainingDetail.png')
