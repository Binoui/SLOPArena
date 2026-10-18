"""Compose the per-fighter model sheets from render_views.py output.

python3 tools/blender/model_sheet/compose.py <renders_dir> docs/design/references/key-art

<renders_dir> holds one render_views.py output folder per name used below
(fightguy, manki, manki_bazooka, ...). Fonts come from the Unity project.
"""
import os
import sys

from PIL import Image, ImageChops, ImageDraw, ImageFont

RENDERS, OUT = sys.argv[1], sys.argv[2]
FONTS = os.path.join(os.path.dirname(__file__), "../../../client/Unity/Assets/Fonts/")
TITLE = ImageFont.truetype(FONTS + "ArchivoBlack-Regular.ttf", 64)
LABEL = ImageFont.truetype(FONTS + "SpaceMono-Regular.ttf", 26)
SMALL = ImageFont.truetype(FONTS + "SpaceMono-Regular.ttf", 22)
PAPER, INK, YELLOW = (222, 216, 201), (23, 24, 20), (255, 204, 34)

# name -> (title, size note, full-body height in px, [(prop render, caption)])
SHEETS = {
    "fightguy": ("FIGHTGUY", "roster height reference (1.0)", 760, []),
    "manki": ("MANKI", "about 0.75x FightGuy height", 760,
              [("manki_bazooka/q3.png", "BAZOOKA (R)"), ("manki_bomb/front.png", "ROUND BOMB (A)"),
               ("manki_aerosol/front.png", "AEROSOL CAN (F)")]),
    "wibou": ("WIBOU", "about 1.05x FightGuy height", 900,
              [("wibou_katana/q3.png", "KATANA (BLACK HILT, STEEL BLADE)"), ("wibou_kunai/q3.png", "KUNAI")]),
    "bonk": ("BONK", "about 1.7x FightGuy height; huge torso, tiny head", 700,
             [("bonk_sword/q3.png", "RIVETED PLANK GREATSWORD")]),
}


def trimmed(path, pad=24):
    image = Image.open(path).convert("RGB")
    mask = ImageChops.difference(image, Image.new("RGB", image.size, image.getpixel((2, 2)))).convert("L")
    box = mask.point(lambda x: 255 if x > 12 else 0).getbbox() or (0, 0) + image.size
    return image.crop((max(box[0] - pad, 0), max(box[1] - pad, 0),
                       min(box[2] + pad, image.size[0]), min(box[3] + pad, image.size[1])))


def to_height(image, height):
    return image.resize((max(1, round(image.size[0] * height / image.size[1])), height), Image.LANCZOS)


for key, (title, note, height, props) in SHEETS.items():
    folder = os.path.join(RENDERS, key)
    bodies = [(v, to_height(trimmed(os.path.join(folder, v + ".png")), height)) for v in ("front", "q3", "back")]
    face_height = 440
    faces = [(v, to_height(trimmed(os.path.join(folder, v + ".png"), 0), face_height)) for v in ("face", "face_q3")]
    extras = []
    for path, caption in props:
        image = trimmed(os.path.join(RENDERS, path), 16)
        image.thumbnail((760 if "SWORD" in caption or "KATANA" in caption else 420, face_height - 60), Image.LANCZOS)
        extras.append((caption, image))
    gap, margin, top = 32, 48, 130
    row1 = sum(i.size[0] for _, i in bodies) + gap * 2
    row2 = sum(i.size[0] for _, i in faces + extras) + gap * (len(faces) + len(extras) - 1)
    width = margin * 2 + max(row1, row2, 1500)
    total = top + height + 50 + face_height + 110
    sheet = Image.new("RGB", (width, total), PAPER)
    draw = ImageDraw.Draw(sheet)
    draw.rectangle((0, 0, width, 100), fill=INK)
    draw.text((margin, 14), title, font=TITLE, fill=YELLOW)
    draw.text((margin + draw.textlength(title, font=TITLE) + 30, 46),
              "MODEL REFERENCE // LIVE IN-GAME MODEL // " + note.upper(), font=LABEL, fill=PAPER)
    x = margin
    for view, image in bodies:
        sheet.paste(image, (x, top))
        draw.text((x, top + height + 8), {"q3": "3/4"}.get(view, view).upper(), font=SMALL, fill=INK)
        x += image.size[0] + gap
    y, x = top + height + 50, margin
    for view, image in faces:
        sheet.paste(image, (x, y))
        draw.text((x, y + face_height + 8), "FACE 3/4" if "q3" in view else "FACE", font=SMALL, fill=INK)
        x += image.size[0] + gap
    for caption, image in extras:
        sheet.paste(image, (x, y + (face_height - 60 - image.size[1]) // 2))
        draw.text((x, y + face_height + 8), caption, font=SMALL, fill=INK)
        x += image.size[0] + gap
    draw.text((margin, total - 44), "Blender render of the live Unity model, neutral light, rest pose (T/A) - "
              "not an action pose. Props rendered separately.", font=SMALL, fill=(90, 88, 80))
    sheet.save(os.path.join(OUT, key + "-model-ref.jpg"), quality=90, optimize=True)
    print(key, sheet.size)
