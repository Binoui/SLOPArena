"""Lay out actual build renders without modifying their model content."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

root = Path(__file__).resolve().parents[3]
out = root/'tmp/nyx-model/props'
review = out/'review'
font_path = '/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf'
font = ImageFont.truetype(font_path, 23)
small = ImageFont.truetype(font_path, 18)


def sheet(name, panels, columns):
    width, height = 560, 630
    rows = (len(panels)+columns-1)//columns
    canvas = Image.new('RGB', (columns*width, rows*(height+82)), '#22252b')
    draw = ImageDraw.Draw(canvas)
    for i, (file, title, subtitle) in enumerate(panels):
        image = Image.open(review/file).convert('RGB')
        image.thumbnail((width, height))
        x, y = i%columns*width, i//columns*(height+82)
        canvas.paste(image, (x+(width-image.width)//2, y))
        draw.text((x+16, y+height+12), title, font=font, fill='white')
        draw.text((x+16, y+height+47), subtitle, font=small, fill='#c6cad2')
    canvas.save(out/name)


sheet('backpack-before-after.png', [
    ('before-backpack-held.png', 'Before: held copy and stowed pole', 'Original v3 build. Two ring lights are visible.'),
    ('after-backpack-stowed.png', 'After: backpack pole visible', 'Bag and pole are now separate renderers.'),
    ('after-backpack-held.png', 'After: held copy only', 'Backpack and straps remain. Stowed pole is hidden.')
], 3)
sheet('phone-before-after.png', [
    ('before-real-phone.png', 'Real form before', 'The right hand has no separate phone.'),
    ('after-real-phone.png', 'Real form with phone', 'Going Live exposes the shared right-hand prop.'),
    ('before-fake-phone.png', 'Fake form before', 'The supplied v3 right hand is open.'),
    ('after-fake-phone.png', 'Fake form with phone', 'Pink glitter case. Existing fingers need an authored grip.')
], 2)
print(out/'backpack-before-after.png')
print(out/'phone-before-after.png')
