"""Original paint/CRT atlas and flush concrete deck. Uses existing roster portraits.

No external art, vendor assets or real brands. Archivo Black and Space Mono are
repository-bundled OFL fonts. Atlas white block matches stagekit.geo.WHITE_UV.
"""
from __future__ import annotations
import math
import random
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont, ImageOps, ImageEnhance, ImageFilter

REPO = Path(__file__).resolve().parents[3]
FONTS = REPO / 'client/Unity/Assets/Fonts'
PORTRAITS = REPO / 'client/Unity/Assets/Resources/UI/Portraits'
SIZE = 2048
CELLS = {
    'logo': (0, 0, 1024, 512), 'manki': (1024, 0, 512, 512), 'static': (1536, 0, 512, 512),
    'banner': (0, 512, 1024, 384), 'applause': (0, 896, 1024, 192),
    'contestants': (0, 1088, 1024, 192), 'hazard': (0, 1280, 1024, 128),
    'scoreboard': (1024, 512, 512, 512), 'plywood': (1536, 512, 512, 512),
    'off-air': (1024, 1024, 512, 128), 'on-air': (1536, 1024, 512, 128),
    'exit': (1024, 1152, 256, 128), 'fan': (1280, 1152, 256, 128),
    **{'poster_' + n.lower(): (i * 256, 1408, 256, 384)
       for i, n in enumerate(('Manki', 'FightGuy', 'Wibou', 'Bonk'))},
}
INK = (24, 20, 16)
YELLOW = (220, 165, 45)
ORANGE = (210, 66, 18)
PAPER = (213, 192, 144)


def uv(name, inset=3):
    x, y, w, h = CELLS[name]
    return ((x + inset) / SIZE, 1 - (y + h - inset) / SIZE), ((x + w - inset) / SIZE, 1 - (y + inset) / SIZE)


def font(size, mono=False):
    return ImageFont.truetype(str(FONTS / ('SpaceMono-Regular.ttf' if mono else 'ArchivoBlack-Regular.ttf')), max(6, int(size)))


def fitted(draw, box, text, color, mono=False, tilted=False):
    x0, y0, x1, y1 = box
    size = y1 - y0
    while size > 6:
        f = font(size, mono)
        b = draw.textbbox((0, 0), text, font=f)
        if b[2] - b[0] <= x1 - x0 and b[3] - b[1] <= y1 - y0:
            break
        size *= 0.94
    draw.text(((x0+x1)/2, (y0+y1)/2), text, font=f, anchor='mm', fill=color,
              stroke_width=0)


def crown(draw, cx, cy, w, color):
    h = w * .48
    draw.polygon([(cx-w/2, cy+h/2), (cx-w*.58, cy-h*.45), (cx-w*.23, cy-h*.05),
                  (cx, cy-h*.7), (cx+w*.23, cy-h*.05), (cx+w*.58, cy-h*.45), (cx+w/2, cy+h/2)], fill=color)


def burst(draw, w, h, rng, color):
    pts=[]
    for i in range(36):
        a = i*math.tau/36
        r = (1 if i%2==0 else .67)*rng.uniform(.91,1.05)
        pts.append((w/2+math.cos(a)*w*.47*r, h/2+math.sin(a)*h*.47*r))
    draw.polygon(pts, fill=color)


def portrait(name, size):
    im = Image.open(PORTRAITS / (name + '.png')).convert('RGB')
    im = ImageOps.fit(im, size, centering=(.5,.32))
    im = ImageEnhance.Color(im).enhance(.52)
    return im


def atlas(path):
    rng = random.Random(20261027)
    out = Image.new('RGBA', (SIZE, SIZE), (255,255,255,255))
    for name,(x,y,w,h) in CELLS.items():
        im=Image.new('RGB',(w,h),INK)
        d=ImageDraw.Draw(im)
        if name in ('logo','banner'):
            im.paste(ORANGE,(0,0,w,h)); d=ImageDraw.Draw(im)
            burst(d,w,h,rng,YELLOW)
            for i,line in enumerate(('MANKI','MAYHEM')):
                # Small alternating offsets give the painted banner an imperfect baseline.
                fitted(d,(w*.09,h*(.09+.41*i),w*.91,h*(.45+.41*i)),line,INK)
            if name=='logo':
                fitted(d,(w*.21,h*.91,w*.79,h*.98),'PIRATE BROADCAST  //  CH. 00',PAPER,True)
            else:
                d.line([(0,10),(w,14)], fill=(114,57,19),width=5)
                d.line([(0,h-13),(w,h-8)], fill=(114,57,19),width=5)
                for px in range(16,w,90): d.ellipse((px,15,px+8,23),fill=INK)
        elif name=='manki':
            art=portrait('Manki',(w,h))
            im=ImageOps.colorize(ImageOps.grayscale(art), (30,17,9), (225,113,36)); d=ImageDraw.Draw(im)
            fitted(d,(w*.07,h*.83,w*.93,h*.96),'YOUR HOST: MANKI',PAPER)
        elif name=='static':
            small=Image.new('RGB',(128,128))
            small.putdata([(v,int(v*.58),int(v*.32)) for v in [rng.randrange(35,155) for _ in range(128*128)]])
            im=small.resize((w,h),Image.Resampling.NEAREST); d=ImageDraw.Draw(im)
            fitted(d,(w*.2,h*.41,w*.8,h*.58),'NO SIGNAL',PAPER,True)
        elif name.startswith('poster_'):
            who={'manki':'Manki','fightguy':'FightGuy','wibou':'Wibou','bonk':'Bonk'}[name[7:]]
            im.paste(PAPER,(0,0,w,h)); im.paste(portrait(who,(w-22,int(h*.67))),(11,34)); d=ImageDraw.Draw(im)
            fitted(d,(10,5,w-10,30),'MANKI MAYHEM',INK)
            fitted(d,(10,h*.78,w-10,h*.90),who.upper(),INK)
            fitted(d,(10,h*.91,w-10,h*.98),'AUDIENCE WANTED',ORANGE,True)
        elif name=='scoreboard':
            im.paste((29,36,29),(0,0,w,h)); d=ImageDraw.Draw(im)
            d.rectangle((9,9,w-9,h-9),outline=(101,78,42),width=15)
            fitted(d,(25,24,w-25,80),'TONIGHT\'S CONTESTANTS',PAPER)
            for i,n in enumerate(('FIGHTGUY   00','MANKI      ??','WIBOU      00','BONK       00')):
                fitted(d,(38,110+i*76,w-38,158+i*76),n,(177,181,148),True)
                d.line([(36,169+i*76),(w-36,171+i*76)],fill=(81,88,70),width=2)
            fitted(d,(28,h-57,w-28,h-22),'SURVIVE.  TRY AGAIN.',PAPER,True)
        elif name=='plywood':
            im.paste((101,72,39),(0,0,w,h)); d=ImageDraw.Draw(im)
            for i in range(170):
                xx=rng.randrange(w); yy=rng.randrange(h)
                d.line([(xx,yy),(xx+rng.randrange(-15,16),min(h,yy+rng.randrange(15,140)))],fill=(91+rng.randrange(20),62+rng.randrange(15),31),width=1)
            d.line([(0,h*.5),(w,h*.5)],fill=(61,43,24),width=4)
        elif name=='hazard':
            im.paste(YELLOW,(0,0,w,h)); d=ImageDraw.Draw(im)
            for k in range(-h,w+h,140): d.polygon([(k,0),(k+68,0),(k+68+h,h),(k+h,h)],fill=INK)
        else:
            label={'applause':'APPLAUSE','contestants':'CONTESTANTS','off-air':'OFF AIR  //  STAND BY',
                   'on-air':'ON AIR','exit':'EXIT  >','fan':'GO MANKI!'}[name]
            if name=='on-air': im.paste((90,11,4),(0,0,w,h)); d=ImageDraw.Draw(im)
            color=(227,79,30) if name=='on-air' else YELLOW
            fitted(d,(w*.04,h*.12,w*.96,h*.88),label,color,mono=name in ('off-air','exit'))
        # Screen scanlines live in paint; separate shader-driven raster strips animate.
        if name in ('logo','manki','static'):
            for yy in range(0,h,5): d.line([(0,yy),(w,yy)],fill=(39,17,7),width=1)
        if name!='static':
            for _ in range(int(w*h/450)):
                px,py=rng.randrange(w),rng.randrange(h)
                d.line([(px,py),(min(w,px+rng.randrange(1,5)),py)],fill=INK,width=1)
        out.paste(im,(x,y))
    ImageDraw.Draw(out).rectangle((1984,0,2047,63),fill=(255,255,255,255))
    out.save(path)


def floor(path):
    rng=random.Random(2701)
    n=2048
    noise=Image.new('L',(128,128)); noise.putdata([rng.randrange(115,121) for _ in range(128*128)])
    im=noise.resize((n,n),Image.Resampling.BICUBIC).convert('RGB'); d=ImageDraw.Draw(im)
    # Concrete saw cuts; physically flush, low contrast.
    for i in range(1,6):
        xx=round(n*i/6); d.line([(xx,0),(xx,n)],fill=(85,84,80),width=2)
    for j in range(1,5):
        yy=round(n*j/5); d.line([(0,yy),(n,yy)],fill=(85,84,80),width=2)
    # Bare deck: only a worn, very low-contrast centre print and little edge ticks.
    faint=(111,110,104)
    crown(d,n*.5,n*.465,n*.10,faint)
    fitted(d,(n*.39,n*.505,n*.61,n*.535),'MANKI MAYHEM',faint)
    for x in (65,n-65):
        for y in (180,640,1408,n-180):
            d.line([(x-10,y),(x+10,y)],fill=(132,127,110),width=4)
    for y in (65,n-65):
        for x in (200,700,1348,n-200):
            d.line([(x,y-8),(x,y+8)],fill=(132,127,110),width=4)
    for _ in range(9500):
        x,y=rng.randrange(n),rng.randrange(n)
        if 52<x<n-52 and 48<y<n-48:
            value=rng.randrange(96,125); d.point((x,y),fill=(value,value-1,value-5))
    im.save(path)


def detail(path):
    rng=random.Random(2002)
    im=Image.new('L',(256,256)); im.putdata([rng.randrange(92,164) for _ in range(256*256)])
    im.filter(ImageFilter.GaussianBlur(.55)).save(path)
