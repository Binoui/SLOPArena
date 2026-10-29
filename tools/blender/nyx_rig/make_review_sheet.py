"""Label Blender renders without resampling the individual PNG evidence."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import sys
out=Path(sys.argv[1]).resolve();review=out/'review'
font=ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf',23)
small=ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf',18)
poses=['arms-raised','arms-forward','wide-stance','deep-squat','high-front-kick','run-stride','fist','open-hand']
s=Image.new('RGB',(1600,1900),'#e2e5eb');d=ImageDraw.Draw(s)
d.text((25,12),'NYX / ONE SKELETON / MATCHED POSES',font=font,fill='#172333')
d.text((25,46),'Real then fake in each pair. Dress shown with skin only; Unity adds waist-pinned cloth.',font=small,fill='#172333')
for i,name in enumerate(poses):
 x=i%4*400;y=80+i//4*900
 d.text((x+8,y),name.replace('-',' '),font=font,fill='#172333')
 for j,form in enumerate(['real','fake']):
  im=Image.open(review/f'{name}-{form}.png').convert('RGB');im.thumbnail((392,400))
  s.paste(im,(x+(400-im.width)//2,y+38+j*425));d.text((x+8,y+38+j*425),form.upper(),font=small,fill='white')
s.save(review/'pose-check.jpg',quality=94)
s=Image.new('RGB',(1800,1320),'#e2e5eb');d=ImageDraw.Draw(s)
d.text((20,12),'NYX REAL / BEFORE AND AFTER SKELETON FIT',font=font,fill='#172333')
for row,phase in enumerate(['before','after']):
 for col,view in enumerate(['front','side','face']):
  im=Image.open(review/f'fit-{phase}-{view}.png').convert('RGB');im=im.resize((590,590))
  x=col*600;y=60+row*630;s.paste(im,(x,y+30));d.text((x+10,y),f'{phase.upper()} / {view}',font=font,fill='#172333')
s.save(review/'fit-before-after.jpg',quality=95)
s=Image.new('RGB',(1600,880),'#e2e5eb');d=ImageDraw.Draw(s)
for i,(pose,form) in enumerate((p,f) for p in ['fist','open-hand'] for f in ['real','fake']):
 im=Image.open(review/f'{pose}-{form}-hand.png').convert('RGB');im.thumbnail((780,400));x=i%2*800;y=i//2*440;s.paste(im,(x,y+30));d.text((x+10,y),pose+' / '+form,font=font,fill='#172333')
s.save(review/'hands.jpg',quality=95)
