"""Compose verified Blender renders and annotated Mixamo marker guides (Python + Pillow, no Blender)."""
import sys,json
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
out=Path(sys.argv[1]).resolve();renders=out/'verification'
font_path='/usr/share/fonts/TTF/DejaVuSans.ttf'
def font(n):
    try:return ImageFont.truetype(font_path,n)
    except OSError:return ImageFont.load_default(size=n)
def sheet(path,panels,subtitle):
    w=800;h=850;im=Image.new('RGB',(2400,1825),'#e9ebef');d=ImageDraw.Draw(im)
    d.text((28,16),'NYX / MIXAMO PREPARATION v1',font=font(32),fill='#17202e')
    d.text((28,59),subtitle,font=font(22),fill='#354152')
    for i,(key,view,label) in enumerate(panels):
        x=(i%3)*w;y=105+(i//3)*h
        panel=Image.open(renders/(key+'-'+view+'.png')).convert('RGB').resize((800,800),Image.Resampling.LANCZOS)
        im.paste(panel,(x,y+38));d.text((x+20,y+4),label,font=font(24),fill='#17202e')
        floor=y+38+int((.95+1.05)/2.1*800)
        d.line((x+30,floor,x+770,floor),fill='#516171',width=2)
    d.text((25,1800),'Shared camera scale in every panel. Prop retains its body-relative position.',font=font(18),fill='#354152')
    im.save(path,quality=94,subsampling=0)
sheet(out/'preview.jpg',[
 ('real','front','REAL / front / upload body'),('fake','front','FAKE / front / upload body'),('backpack','front','Backpack + pole + ring / front'),
 ('real','side','REAL / side / upload body'),('fake','side','FAKE / side / upload body'),('backpack','side','Backpack + pole + ring / side')],
 'Hair crowns at 1.70 m. Textured renders of the actual exported FBXs, re-imported in a fresh Blender process.')
sheet(out/'split-verification.jpg',[
 ('fake','front','FAKE body / front'),('fake','back','FAKE body / back'),('fake','side','FAKE body / side'),
 ('backpack','front','Rigid prop / front'),('backpack','back','Rigid prop / back'),('backpack','side','Rigid prop / side')],
 'Body and complete rigid prop separately. Upload the two body FBXs; retain the prop for attachment after rigging.')
m=json.loads((out/'measurements.json').read_text())
for form in ['real','fake']:
    im=Image.open(renders/(form+'-front.png')).convert('RGB');d=ImageDraw.Draw(im)
    d.rectangle((0,0,1200,95),fill='#e9ebef');d.text((25,12),'NYX '+form.upper()+' / Mixamo marker guide',font=font(32),fill='#17202e')
    d.text((25,56),'Estimates in current A pose; knee/groin markers go over the dress.',font=font(20),fill='#354152')
    for name,color in [('chin','#f6db4c'),('elbow','#42dcee'),('wrist','#f076c5'),('knee','#70ed7e'),('crotch','#ffa552')]:
        p=m['forms'][form]['landmarks'][name];z=p['height_cm']/100
        half=p.get('span_cm',0)/200
        y=(.95+1.05-z)/2.1*1200
        for sign in ([1,-1] if half else [1]):
            x=600+sign*half/2.1*1200
            d.ellipse((x-11,y-11,x+11,y+11),fill=color,outline='black',width=2)
            label=('groin' if name=='crotch' else name)+f' {z*100:.1f} cm'
            tx=20 if sign<0 else 950
            if half==0:tx=780
            d.line((x,y,tx+(210 if sign<0 else 0),y),fill=color,width=3)
            d.rectangle((tx-4,y-23,tx+235,y-2),fill='#202735')
            d.text((tx,y-23),label,font=font(18),fill=color)
    im.save(out/('markers-'+form+'.jpg'),quality=95,subsampling=0)
print('Wrote preview.jpg, split-verification.jpg, markers-real.jpg, markers-fake.jpg')
