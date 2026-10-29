"""Compose the approved/rest comparison and actual Unity spring pose contact sheets."""
from pathlib import Path
import sys
from PIL import Image, ImageDraw

out = Path(sys.argv[1]).resolve()
review = out/'review'
sheet = Image.new('RGB',(1200,1260),'#e4e6eb')
draw = ImageDraw.Draw(sheet)
for row,(phase,label) in enumerate([('before','APPROVED MODEL / original Tripo'),('after','V3 / rest skin, arms lowered for comparison')]):
    draw.text((15,row*630+10),label,fill='black',font_size=24)
    for column,view in enumerate(['front','side']):
        image = Image.open(review/f'fit-{phase}-{view}.png').convert('RGB')
        image.thumbnail((596,590))
        sheet.paste(image,(column*600+(600-image.width)//2,row*630+40))
sheet.save(review/'approved-v3-rest.jpg',quality=95)
poses = ['arms-raised','arms-forward','wide-stance','deep-squat','high-front-kick','run-stride','fist','open-hand']
sheet = Image.new('RGB',(1600,1080),'#e4e6eb')
draw = ImageDraw.Draw(sheet)
for index,name in enumerate(poses):
    column,row = index%4,index//4
    image = Image.open(review/'unity-poses'/(name+'.png')).convert('RGB')
    image.thumbnail((396,500))
    sheet.paste(image,(column*400+(400-image.width)//2,row*540+35))
    draw.text((column*400+12,row*540+8),name,fill='black',font_size=20)
sheet.save(review/'spring-eight-poses.jpg',quality=95)
if all((review/'unity-poses'/(name+'-hand.png')).exists() for name in ['fist','open-hand']):
    sheet=Image.new('RGB',(1200,650),'#e4e6eb');draw=ImageDraw.Draw(sheet)
    for index,name in enumerate(['fist','open-hand']):
        image=Image.open(review/'unity-poses'/(name+'-hand.png')).convert('RGB');image.thumbnail((596,600))
        sheet.paste(image,(index*600+(600-image.width)//2,40));draw.text((index*600+12,10),name,fill='black',font_size=24)
    sheet.save(review/'spring-hands.jpg',quality=95)
print(review/'approved-v3-rest.jpg')
print(review/'spring-eight-poses.jpg')
