"""Derive the game, Steam and website art from the generated key art originals.

python3 tools/key_art/key_art.py build tools/key_art/set-2026-10-18.json [--only game,steam,web]
python3 tools/key_art/key_art.py namemark <art.png> <out.png> --centre X Y --size PX [--angle DEG] [--bomb] [--tagline]

The set file names the originals and every output: a byte copy, a crop and resize, the name mark
typeset on a plate, the plate logo or an icon. The name mark is SLOPArena on one line in the bundled
Archivo Black, never generated and never stacked as SLOP / ARENA (that reads as two words). On large
marks the bomb from the game icon stands in for the O (`bomb`). See README.md.
"""
import argparse
import hashlib
import json
import os
import shutil
from pathlib import Path

from PIL import Image, ImageChops, ImageCms, ImageDraw, ImageFilter, ImageFont

REPO = Path(__file__).resolve().parents[2]
FONTS = REPO / "client/Unity/Assets/Fonts"
BOMB = REPO / "client/Unity/Assets/UI/SlopArenaIcon.png"
# The bomb's round body in the 256 px icon, without its cap, fuse and spark: it takes the O's place.
BOMB_BODY = (30, 75, 222, 253)
INK, ORANGE, PAPER, YELLOW = (23, 24, 20, 255), (240, 91, 53, 255), (222, 216, 201, 255), (255, 204, 34, 255)
# SlopArena-web's torn plate (scripts/social-preview.html): clip-path polygon in percent of the box.
TORN = [(0, 3), (6, 0), (19, 2.4), (33, .4), (47, 2.2), (62, 0), (78, 2), (91, .2), (100, 1.8), (98.6, 19), (100, 37),
        (98.4, 56), (100, 76), (98.8, 100), (84, 97.6), (70, 100), (54, 97.8), (39, 100), (23, 98), (9, 100), (0, 97.2),
        (1.4, 79), (0, 58), (1.2, 37), (.2, 18)]


def font(name, size):
    return ImageFont.truetype(str(FONTS / name), size)


def bomb_icon():
    """The game icon and its dark silhouette (body, cap and fuse; not the spark) as a white mask."""
    image = Image.open(BOMB).convert("RGBA")
    dark = image.convert("L").point(lambda v: 255 if v < 90 else 0)
    solid = image.getchannel("A").point(lambda v: 255 if v > 40 else 0)
    silhouette = Image.new("RGBA", image.size, (255, 255, 255, 255))
    silhouette.putalpha(ImageChops.multiply(dark, solid))
    return image, silhouette


def lettering(lines, size, tracking=-0.04, leading=0.89, bomb=False):
    """White letter masks: (letters, bomb colour, bomb silhouette) on one canvas cropped to their ink.
    With `bomb`, each O is left out and the bomb icon takes its place at 1.04x the O's height, its body
    centred on the O and its fuse and spark rising above the line. Without it the bomb layers are None."""
    face = font("ArchivoBlack-Regular.ttf", size)
    widths = [sum(face.getlength(c) for c in line) + tracking * size * (len(line) - 1) for line in lines]
    ascent, descent = face.getmetrics()
    pad = size if bomb else 0
    canvas = (int(max(widths)) + 4 + 2 * pad, int(size * leading) * (len(lines) - 1) + ascent + descent + 2 * pad)
    layer = Image.new("RGBA", canvas)
    colour = silhouette = None
    if bomb:
        icon, icon_silhouette = bomb_icon()
        l, t, r, b = face.getbbox("O")
        scale = 1.04 * (b - t) / (BOMB_BODY[3] - BOMB_BODY[1])
        scaled = (round(icon.size[0] * scale), round(icon.size[1] * scale))
        icon, icon_silhouette = icon.resize(scaled, Image.LANCZOS), icon_silhouette.resize(scaled, Image.LANCZOS)
        body_x, body_y = (BOMB_BODY[0] + BOMB_BODY[2]) / 2 * scale, (BOMB_BODY[1] + BOMB_BODY[3]) / 2 * scale
        colour, silhouette = Image.new("RGBA", canvas), Image.new("RGBA", canvas)
    draw = ImageDraw.Draw(layer)
    for row, line in enumerate(lines):
        x, y = (canvas[0] - widths[row]) / 2, pad + row * int(size * leading)
        for c in line:
            if bomb and c == "O":
                at = (round(x + (l + r) / 2 - body_x), round(y + (t + b) / 2 - body_y))
                colour.alpha_composite(icon, at)
                silhouette.alpha_composite(icon_silhouette, at)
            else:
                draw.text((x, y), c, font=face, fill=(255, 255, 255, 255))
            x += face.getlength(c) + tracking * size
    box = layer.getbbox() if colour is None else ImageChops.lighter(layer, colour).getbbox()
    return tuple(None if part is None else part.crop(box) for part in (layer, colour, silhouette))


def filled(mask, color):
    out = Image.new("RGBA", mask.size, color)
    out.putalpha(mask.split()[3])
    return out


def name_mark(size, lines=("SLOPArena",), tagline=False, bomb=False):
    """Ink lettering over an off-register Orange print, as printed on the key art's yellow plates."""
    mask, colour, silhouette = lettering(list(lines), size, bomb=bomb)
    printed = mask if silhouette is None else ImageChops.lighter(mask, silhouette)
    offset = max(4, size // 22)
    mark = Image.new("RGBA", (mask.size[0] + offset + 2, mask.size[1] + offset + 2))
    mark.alpha_composite(filled(printed, ORANGE), (offset, offset))
    mark.alpha_composite(filled(mask, INK), (0, 0))
    if colour is not None:
        mark.alpha_composite(colour)
    if not tagline:
        return mark
    font_size = max(14, round(size * 0.26))
    mono, text = font("SpaceMono-Regular.ttf", font_size), "GET IN THE SLOP."
    strip = Image.new("RGBA", (int(mono.getlength(text)) + font_size, int(font_size * 1.7)), INK)
    ImageDraw.Draw(strip).text((font_size // 2, strip.size[1] / 2 - font_size * 0.62), text, font=mono,
                               fill=PAPER, stroke_width=1, stroke_fill=PAPER)
    gap = size // 7
    block = Image.new("RGBA", (max(mark.size[0], strip.size[0]), mark.size[1] + gap + strip.size[1]))
    block.alpha_composite(mark, ((block.size[0] - mark.size[0]) // 2, 0))
    block.alpha_composite(strip, ((block.size[0] - strip.size[0]) // 2, mark.size[1] + gap))
    return block


def stamp(art, centre, size, angle=0.0, lines=("SLOPArena",), tagline=False, bomb=False):
    mark = name_mark(size, lines, tagline, bomb).rotate(angle, resample=Image.BICUBIC, expand=True)
    out = art.convert("RGBA")
    out.alpha_composite(mark, (int(centre[0] - mark.size[0] / 2), int(centre[1] - mark.size[1] / 2)))
    return out.convert("RGB")


def plate_logo(width, shadow=0.035, scale=4, bomb=False):
    """The website's name mark: SLOPArena on its torn, dotted Action Yellow plate, rotated -1.6 degrees,
    with a hard Ink offset shadow (`shadow` 0 for none), on transparency. Drawn at `scale` and reduced."""
    letters, colour, _ = lettering(["SLOPArena"], 148 * scale, tracking=-0.055, bomb=bomb)
    pad_t, pad_r, pad_b, pad_l = (v * scale for v in (22, 34, 14, 26))
    box = (letters.size[0] + pad_l + pad_r, letters.size[1] + pad_t + pad_b)
    plate = Image.new("RGBA", box)
    draw = ImageDraw.Draw(plate)
    polygon = [(x / 100 * box[0], y / 100 * box[1]) for x, y in TORN]
    draw.polygon(polygon, fill=YELLOW)
    dots = Image.new("RGBA", box)
    dot_draw, step, radius = ImageDraw.Draw(dots), 8 * scale, 1.3 * scale
    for y in range(0, box[1], int(step)):
        for x in range(0, box[0], int(step)):
            dot_draw.ellipse((x - radius, y - radius, x + radius, y + radius), fill=(23, 24, 20, 36))
    clip = Image.new("L", box)
    ImageDraw.Draw(clip).polygon(polygon, fill=255)
    dots.putalpha(Image.composite(dots.split()[3], Image.new("L", box), clip))
    plate.alpha_composite(dots)
    plate.alpha_composite(filled(letters, INK), (pad_l, pad_t))
    if colour is not None:
        plate.alpha_composite(colour, (pad_l, pad_t))
    plate = plate.rotate(1.6, resample=Image.BICUBIC, expand=True)
    offset = round(shadow * plate.size[1])
    out = Image.new("RGBA", (plate.size[0] + offset, plate.size[1] + offset))
    if offset:
        out.alpha_composite(filled(plate, INK), (offset, offset))
    out.alpha_composite(plate)
    out = out.crop(out.getbbox())
    return out.resize((width, round(out.size[1] * width / out.size[0])), Image.LANCZOS)


def icon(image, background, margin):
    side = round(max(image.size) / (1 - 2 * margin))
    out = Image.new("RGBA", (side, side), background)
    out.alpha_composite(image.convert("RGBA"), ((side - image.size[0]) // 2, (side - image.size[1]) // 2))
    return out


# littlecms stamps the creation time into the ICC header (bytes 24-35); pin it so rebuilds are byte-identical.
_SRGB = bytearray(ImageCms.ImageCmsProfile(ImageCms.createProfile("sRGB")).tobytes())
_SRGB[24:36] = (2026).to_bytes(2, "big") + (10).to_bytes(2, "big") + (18).to_bytes(2, "big") + bytes(6)
SRGB = bytes(_SRGB)


def save(image, dest, out, embed_srgb):
    dest.parent.mkdir(parents=True, exist_ok=True)
    extra = {"icc_profile": SRGB} if embed_srgb else {}
    suffix = dest.suffix.lower()
    if suffix == ".ico":
        image.convert("RGBA").save(dest, sizes=[(s, s) for s in out["ico"]])
    elif suffix in (".jpg", ".jpeg"):
        image.convert("RGB").save(dest, quality=out.get("quality", 92), optimize=True, **extra)
    else:
        image.save(dest, optimize=True, **extra)


def build(set_path, only, overrides):
    spec = json.loads(Path(set_path).read_text())
    roots = {}
    for name, value in {**spec["roots"], **overrides}.items():
        path = Path(os.path.expanduser(value))
        roots[name] = path if path.is_absolute() else (REPO / path).resolve()

    def resolve(ref):
        root, _, rel = ref.partition(":")
        return roots[root] / rel if rel else roots["originals"] / ref

    sources = {key: resolve(ref) for key, ref in spec["sources"].items()}
    masters = {}
    written = []
    for out in spec["outputs"]:
        root, _, rel = out["to"].partition(":")
        if only and root not in only:
            continue
        dest = roots[root] / rel
        if "logo" in out:
            image = plate_logo(**out["logo"])
        else:
            source = sources.get(out["from"]) or resolve(out["from"])
            if not set(out) - {"to", "from"}:
                dest.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(source, dest)
                written.append(dest)
                continue
            mark = out.get("mark")
            key = (str(source), json.dumps(mark, sort_keys=True))
            if key not in masters:
                image = Image.open(source)
                if mark:
                    m = spec["marks"][mark] if isinstance(mark, str) else mark
                    image = stamp(image, m["centre"], m["size"], m.get("angle", 0.0), tagline=m.get("tagline", False),
                                  bomb=m.get("bomb", False))
                masters[key] = image
            image = masters[key]
            if "icon" in out:
                image = icon(image, out["icon"]["background"], out["icon"]["margin"])
            if "crop" in out:
                image = image.crop(tuple(out["crop"]))
            if "size" in out:
                w, h = out["size"]
                if abs(image.size[0] / image.size[1] - w / h) > 0.005 * w / h:
                    raise SystemExit(f"{out['to']}: crop {image.size} does not match the {w}x{h} aspect")
                image = image.resize((w, h), Image.LANCZOS)
            if out.get("sharpen"):
                image = image.filter(ImageFilter.UnsharpMask(radius=1.4, percent=55, threshold=2))
        save(image, dest, out, root == "steam")
        written.append(dest)
    for path in written:
        print(path)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    b = sub.add_parser("build", help="Write every output of a key art set")
    b.add_argument("set", type=Path)
    b.add_argument("--only", help="Comma list of roots to write: game, steam, web")
    b.add_argument("--root", nargs=2, action="append", default=[], metavar=("NAME", "DIR"),
                   help="Override a root from the set file, e.g. --root steam /tmp/steam")
    n = sub.add_parser("namemark", help="Typeset the name mark on any image")
    n.add_argument("art", type=Path)
    n.add_argument("out", type=Path)
    n.add_argument("--centre", type=float, nargs=2, metavar=("X", "Y"), required=True)
    n.add_argument("--size", type=int, required=True, help="Lettering size in image pixels")
    n.add_argument("--angle", type=float, default=0.0, help="Counter-clockwise degrees")
    n.add_argument("--bomb", action="store_true", help="The game icon's bomb stands in for the O")
    n.add_argument("--tagline", action="store_true", help="Add a GET IN THE SLOP. strip")
    args = parser.parse_args()
    if args.command == "build":
        build(args.set, set(args.only.split(",")) if args.only else None, dict(args.root))
    else:
        stamp(Image.open(args.art), args.centre, args.size, args.angle, tagline=args.tagline, bomb=args.bomb) \
            .save(args.out, optimize=True)


if __name__ == "__main__":
    main()
