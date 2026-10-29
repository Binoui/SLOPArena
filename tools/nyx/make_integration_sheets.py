#!/usr/bin/env python3
"""One labeled contact sheet per form, with full gameplay frames and enlarged actor insets."""
from pathlib import Path
import json
from PIL import Image,ImageDraw,ImageOps
ROOT=Path(__file__).resolve().parents[2];E=ROOT/'tmp/nyx-model/integration';P=E/'training-final'
labels={'idle':'Idle','run':'Run','g1':'Shadow Flick','g3':'Grasping Hands','g4':'Shadow Lash','a1':'Shadow Ring','a3':'Up Claw','a4':'Shadow Fist','q':'Hex Crow','e':'Bat Scatter','r':'Grasp','swap':'Going Live'}
fake={'g1':'Phone Slaps','g3':'Ring Light Uppercut','g4':'Platform Stomp-Kick','a1':'Spinning Slap','a3':'Ring Light Overhead','a4':'Double Dropkick','q':'Ring Light','e':'Ring Light Rise','r':'Selfie Grab','swap':'Logging Off'}
manifest={}
for form in ('real','fake'):
    canvas=Image.new('RGB',(2160,1600),'#18151e');draw=ImageDraw.Draw(canvas);draw.text((16,12),f'NYX / {form.upper()} FORM / Native Training gameplay camera, enlarged actor insets',fill='white');records=[]
    for i,key in enumerate(labels):
        p=E/'training-delivery'/f'{form}-{key}'
        if not (p/'states.jsonl').exists():p=P/f'{form}-{key}'
        if key=='idle':p=P/f'{form}-idle-full'
        if key=='g4' and form=='real' and not str(p).startswith(str(E/'training-delivery')):p=P/'real-g4-final'
        rows=[json.loads(l) for l in (p/'states.jsonl').read_text().splitlines()];hits=[r for r in rows if r.get('hits')];attacks=[r for r in rows if r['slot']]
        start=attacks[0]['tick'] if attacks else 0
        if key=='idle':tick=360
        elif key=='run':tick=22
        elif key=='q' and form=='real':tick=start+44
        elif key=='e':tick=start+(12 if form=='real' else 24)
        elif key=='r' and form=='fake':tick=start+44
        elif key=='r':tick=hits[0]['tick']+1 if hits else start+25
        elif key=='g4' and form=='real':tick=hits[0]['tick']+7
        else:tick=hits[0]['tick']+1 if hits else start+12
        image=Image.open(p/f'{tick:04d}.jpg').convert('RGB');w,h=image.size
        panel=Image.new('RGB',(708,370),'#23202a');small=ImageOps.contain(image,(708,333));panel.paste(small,(0,28))
        # An inset magnifies existing gameplay pixels; it is not a different camera or simulation.
        crop=image.crop((int(w*.38),int(h*.19),int(w*.65),int(h*.64)))
        inset=ImageOps.fit(crop,(230,220));panel.paste(inset,(475,148));pd=ImageDraw.Draw(panel);pd.rectangle((474,147,706,369),outline='#e3dbe9',width=2)
        text=labels[key] if form=='real' else fake.get(key,labels[key]);pd.text((8,7),f'{key}  {text}  | frame {tick}',fill='white')
        x=i%3*720;y=40+i//3*390;canvas.paste(panel,(x,y))
        records.append({'move':key,'name':text,'frame':tick,'source':str((p/f'{tick:04d}.jpg').relative_to(ROOT))})
    out=E/f'{form}-contact-sheet.jpg';canvas.save(out,quality=94);manifest[form]=records;print(out.relative_to(ROOT))
(E/'contact-sheet-manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
