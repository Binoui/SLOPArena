"""Steam "About This Game" section banners in the SLOPArena paper/ink style.

    python3 tools/store_page/store_page.py banners tools/store_page/sections.json --out tmp/store-page
    python3 tools/store_page/store_page.py board tools/store_page/sections.json --out tmp/store-page

`shoot` films one section's clip through the Unity Editor gateway: it reshoots the section's photo-mode
spec at twice the clip size and the clip's lens, then prints KO ticks and writes a contact sheet so you can
pick the window. `encode` cuts every shot section's window into an H.264 loop named NN-id.mp4.

    python3 tools/store_page/store_page.py shoot tools/store_page/sections.json brawl --runs tmp/store-page/runs
    python3 tools/store_page/store_page.py encode tools/store_page/sections.json --runs tmp/store-page/runs --out tmp/store-page

`banners` writes one PNG per section, named as the store text's {STEAM_APP_IMAGE}/extras/ uploads, in the
spec's style (tape, picked 2026-11-05) unless --style overrides it. `board` writes every style plus
board.html to compare them.
"""

import argparse
import json
import random
import subprocess
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageEnhance, ImageFilter

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "tools/key_art"))
from key_art import INK, ORANGE, PAPER, YELLOW, font  # noqa: E402


def load(spec_path):
    spec = json.loads(Path(spec_path).read_text())
    spec["shots"] = Path(spec["shots"]).expanduser()
    spec["paper"] = Path(spec["paper"]).expanduser()
    return spec


def cover(image, size, focus, zoom=1.0, anchor=(0.5, 0.5)):
    """Scale `image` to cover `size` (times `zoom`), then crop so the focus point (fractions of the source)
    lands at `anchor` (fractions of the output), as far as the image edges allow."""
    w, h = size
    scale = max(w / image.width, h / image.height) * zoom
    scaled = image.resize((round(image.width * scale), round(image.height * scale)), Image.LANCZOS)
    left = min(max(round(focus[0] * scaled.width - anchor[0] * w), 0), scaled.width - w)
    top = min(max(round(focus[1] * scaled.height - anchor[1] * h), 0), scaled.height - h)
    return scaled.crop((left, top, left + w, top + h))


def torn_edge_mask(size, x, seed, depth=10, step=7, side="left"):
    """Opaque on one side of a ragged vertical line at `x`."""
    rng = random.Random(seed)
    w, h = size
    edge = [(x + rng.uniform(-depth, depth) * (0.4 if i % 2 else 1), y) for i, y in enumerate(range(-step, h + step, step))]
    far = w + 1 if side == "left" else -1
    mask = Image.new("L", size, 0)
    ImageDraw.Draw(mask).polygon(edge + [(far, h + step), (far, -step)], fill=255)
    return mask


def headline_lines(text, size, max_width):
    """`text` at a fixed Archivo Black size: one line if it fits, else the most balanced two-line split."""
    face = font("ArchivoBlack-Regular.ttf", size)
    if face.getlength(text) <= max_width:
        return [text]
    words = text.split()
    splits = [(" ".join(words[:i]), " ".join(words[i:])) for i in range(1, len(words))]
    return list(min(splits, key=lambda s: max(face.getlength(s[0]), face.getlength(s[1]))))


def printed_text(lines, size, fill, offset_fill=ORANGE, leading=0.98):
    """Archivo Black lines with an off-register second print, like the key art's name mark."""
    face = font("ArchivoBlack-Regular.ttf", size)
    ascent, descent = face.getmetrics()
    line_h = round(size * leading)
    offset = max(3, size // 16)
    w = int(max(face.getlength(line) for line in lines)) + offset + 4
    h = line_h * (len(lines) - 1) + ascent + descent + offset
    out = Image.new("RGBA", (w, h))
    draw = ImageDraw.Draw(out)
    for colour, dx in ((offset_fill, offset), (fill, 0)):
        for row, line in enumerate(lines):
            if colour is not None:
                draw.text((dx, dx + row * line_h), line, font=face, fill=colour)
    return out.crop(out.getbbox())


def label_chip(text, size, fg, bg, pad=(10, 5)):
    face = font("SpaceMono-Regular.ttf", size)
    l, t, r, b = face.getbbox(text)
    chip = Image.new("RGBA", (r - l + 2 * pad[0], b - t + 2 * pad[1]), bg)
    ImageDraw.Draw(chip).text((pad[0] - l, pad[1] - t), text, font=face, fill=fg)
    return chip


def halftone(size, colour, spacing=9, fade="right"):
    """Orange print dots that shrink toward one side."""
    w, h = size
    layer = Image.new("RGBA", size)
    draw = ImageDraw.Draw(layer)
    for y in range(0, h + spacing, spacing):
        for x in range(0, w + spacing, spacing):
            t = x / w if fade == "right" else 1 - x / w
            r = (1 - t) * spacing * 0.42
            if r > 0.6:
                cx = x + (spacing / 2 if (y // spacing) % 2 else 0)
                draw.ellipse((cx - r, y - r, cx + r, y + r), fill=colour)
    return layer


def tape(width, height, angle):
    strip = Image.new("RGBA", (width, height), (238, 228, 196, 190))
    ImageDraw.Draw(strip).rectangle((0, 0, width - 1, height - 1), outline=(200, 190, 160, 120))
    return strip.rotate(angle, resample=Image.BICUBIC, expand=True)


def paper_ground(spec, size, seed):
    paper = Image.open(spec["paper"]).convert("RGB")
    rng = random.Random(seed)
    crop_w, crop_h = paper.width // 3, round(paper.width // 3 * size[1] / size[0])
    left = rng.randint(paper.width // 4, paper.width // 2 - crop_w // 3)
    top = rng.randint(paper.height // 3, paper.height // 2)
    return paper.crop((left, top, left + crop_w, top + crop_h)).resize(size, Image.LANCZOS).convert("RGBA")


def style_flyer(spec, section, size, seed):
    """Paper ground, yellow torn plate with ink headline, the shot pasted on the right with a torn edge and tape."""
    w, h = size
    out = paper_ground(spec, size, seed)
    out.alpha_composite(halftone((w // 3, h // 2), ORANGE[:3] + (120,)), (0, h - h // 2))
    shot_x = round(w * 0.5)
    shot = cover(Image.open(spec["shots"] / section["shot"]).convert("RGB"), (w - shot_x + 30, h), section["focus"])
    shot_layer = Image.new("RGBA", size)
    shot_layer.paste(shot, (shot_x - 30, 0))
    mask = torn_edge_mask(size, shot_x, seed)
    shadow = Image.new("RGBA", size, INK)
    shadow.putalpha(mask.point(lambda v: v * 0.55).filter(ImageFilter.GaussianBlur(3)))
    out.alpha_composite(shadow, (-5, 4))
    shot_layer.putalpha(mask)
    out.alpha_composite(shot_layer)
    out.alpha_composite(tape(110, 26, 8), (shot_x - 40, -6))

    text = printed_text(headline_lines(section["headline"].upper(), 40, shot_x - 120), 40, INK)
    plate_w, plate_h = text.width + 52, text.height + 34
    plate = Image.new("RGBA", (plate_w, plate_h))
    rng = random.Random(seed + 1)
    jag = lambda a, b, n: [a + (b - a) * i / n for i in range(n + 1)]
    top = [(x, rng.uniform(0, 6)) for x in jag(0, plate_w, 18)]
    bottom = [(x, plate_h - rng.uniform(0, 6)) for x in reversed(jag(0, plate_w, 18))]
    ImageDraw.Draw(plate).polygon(top + bottom, fill=YELLOW)
    plate.alpha_composite(text, (26, 17))
    plate = plate.rotate(-1.5, resample=Image.BICUBIC, expand=True)
    plate_y = (h - plate.height) // 2 + 16
    out.alpha_composite(plate, (34, plate_y))
    chip = label_chip(section["label"], 18, PAPER, INK).rotate(2, resample=Image.BICUBIC, expand=True)
    out.alpha_composite(chip, (44, max(10, plate_y - chip.height + 6)))
    return out


def style_poster(spec, section, size, seed):
    """Full-bleed shot under an ink fade, yellow headline with an orange print, paper label chip."""
    w, h = size
    shot = cover(Image.open(spec["shots"] / section["shot"]).convert("RGB"), size, (min(section["focus"][0] + 0.1, 1), section["focus"][1]))
    out = shot.convert("RGBA")
    fade = Image.new("L", (w, 1))
    for x in range(w):
        t = min(max((x - w * 0.3) / (w * 0.35), 0.0), 1.0)
        fade.putpixel((x, 0), round(245 * (1 - t * t * (3 - 2 * t))))
    ink = Image.new("RGBA", size, INK)
    ink.putalpha(fade.resize(size))
    out.alpha_composite(ink)
    out.alpha_composite(halftone((w // 4, h // 2), ORANGE[:3] + (150,)), (0, h - h // 2))
    text = printed_text(headline_lines(section["headline"].upper(), 42, round(w * 0.4)), 42, YELLOW)
    chip = label_chip(section["label"], 18, INK, PAPER)
    block_h = chip.height + 12 + text.height
    y = (h - block_h) // 2
    out.alpha_composite(chip, (40, y))
    out.alpha_composite(text, (40, y + chip.height + 12))
    ImageDraw.Draw(out).rectangle((0, 0, w - 1, h - 1), outline=INK, width=4)
    return out


def style_tape(spec, section, size, seed):
    """Full-bleed shot, the headline on a strip of yellow tape across it, torn paper top and bottom."""
    w, h = size
    shot = cover(Image.open(spec["shots"] / section["shot"]).convert("RGB"), size, section["focus"],
                 section.get("zoom", 1.0), anchor=(0.75, 0.5))
    shot = ImageEnhance.Color(shot).enhance(0.85)
    out = shot.convert("RGBA")
    text = printed_text(headline_lines(section["headline"].upper(), 38, round(w * 0.55)), 38, INK)
    strip = Image.new("RGBA", (text.width + 70, text.height + 30), YELLOW)
    strip.alpha_composite(text, (35, 15))
    rng = random.Random(seed)
    edge = Image.new("L", strip.size, 255)
    draw = ImageDraw.Draw(edge)
    for side in (0, strip.width):
        for y in range(0, strip.height, 5):
            draw.polygon([(side, y), (side + (-1 if side else 1) * rng.uniform(3, 9), y + 2.5), (side, y + 5)], fill=0)
    strip.putalpha(edge)
    strip = strip.rotate(2.5, resample=Image.BICUBIC, expand=True)
    shadow = Image.new("RGBA", strip.size, INK[:3] + (0,))
    shadow.putalpha(strip.getchannel("A").point(lambda v: v * 0.5).filter(ImageFilter.GaussianBlur(4)))
    x, y = 30, (h - strip.height) // 2 + 14
    out.alpha_composite(shadow, (x + 6, y + 6))
    out.alpha_composite(strip, (x, y))
    chip = label_chip(section["label"], 18, PAPER, INK).rotate(-2, resample=Image.BICUBIC, expand=True)
    out.alpha_composite(chip, (x + 24, max(8, y - chip.height + 4)))
    paper = paper_ground(spec, size, seed)
    for side in ("top", "bottom"):
        band = Image.new("L", size, 0)
        pts, rng2 = [], random.Random(seed + (1 if side == "top" else 2))
        base = 7 if side == "top" else h - 7
        for i, xx in enumerate(range(-8, w + 16, 8)):
            pts.append((xx, base + rng2.uniform(-4, 4) * (1 if i % 2 else 0.5)))
        corner = -1 if side == "top" else h + 1
        ImageDraw.Draw(band).polygon(pts + [(w + 16, corner), (-8, corner)], fill=255)
        layer = paper.copy()
        layer.putalpha(band)
        out.alpha_composite(layer)
    return out


STYLES = {"flyer": style_flyer, "poster": style_poster, "tape": style_tape}


def render(spec, style, out_dir):
    out_dir.mkdir(parents=True, exist_ok=True)
    paths = []
    for index, section in enumerate(spec["sections"]):
        image = STYLES[style](spec, section, tuple(spec["size"]), seed=index * 17 + 3)
        path = out_dir / f"{index + 1:02d}-{section['id']}.png"
        image.convert("RGB").save(path, optimize=True)
        paths.append(path)
    return paths


def wide_spec(clip, size):
    """The section's photo-mode spec, reshot at 2x the clip size with the clip's lens. `presses` maps move
    labels for specs written before the 2026-11-01 normals renumbering (old 3 and 4 are now 2 and 3)."""
    spec = json.loads((REPO / clip["spec"]).read_text())
    spec["capture"].update({"from": clip["capture"][0], "to": clip["capture"][1], "step": 1,
                            "width": 2 * size[0], "height": 2 * size[1]})
    for camera in spec["cameras"]:
        if camera["name"] == clip["camera"]:
            camera["fov"] = clip["fov"]
    spec["cameras"] = [c for c in spec["cameras"] if c["name"] == clip["camera"]]
    for fighter in spec["fighters"]:
        for step in fighter.get("input", []):
            if "press" in step:
                step["press"] = clip.get("presses", {}).get(step["press"], step["press"])
    return spec


def takes(runs, section_id):
    """Numbered shoot folders for a section, oldest first. Photo mode never overwrites a run."""
    found = [p for p in runs.glob(f"{section_id}-*") if p.is_dir() and p.name.rsplit("-", 1)[1].isdigit()]
    return sorted(found, key=lambda p: int(p.name.rsplit("-", 1)[1]))


def shoot(spec, section_id, runs):
    section = next(s for s in spec["sections"] if s["id"] == section_id)
    clip = section["clip"]
    runs.mkdir(parents=True, exist_ok=True)
    previous = takes(runs, section_id)
    take = f"{section_id}-{int(previous[-1].name.rsplit('-', 1)[1]) + 1 if previous else 1}"
    run = runs / take
    spec_path = runs / f"{take}.spec.json"
    spec_path.write_text(json.dumps(wide_spec(clip, spec["clipSize"]), indent=1))
    relative = lambda path: str(path.relative_to(REPO))
    batch = subprocess.run([sys.executable, "tools/photo_shoot.py", "batch", relative(spec_path), "--output", relative(run)],
                           cwd=REPO, check=True, capture_output=True, text=True).stdout
    batch_path = runs / f"{take}.batch.json"
    batch_path.write_text(batch)
    gateway = subprocess.run(["bun", "scripts/unity-editor-gateway.ts", "--project-path", "client/Unity",
                              "--batch", str(batch_path)], cwd=REPO, capture_output=True, text=True)
    (runs / f"{take}.gateway.log").write_text(gateway.stdout + gateway.stderr)
    if gateway.returncode:
        sys.exit(f"gateway failed ({gateway.returncode}); see {runs / f'{take}.gateway.log'}")
    deaths = None
    for line in (run / "states.jsonl").read_text().splitlines():
        state = json.loads(line)
        now = [f.get("deaths") for f in state.get("fighters", [])]
        if deaths is not None and now != deaths:
            print(f"tick {state.get('tick')}: deaths {now}")
        deaths = now
    first, last = clip["capture"]
    sheet = runs / f"{take}.sheet.jpg"
    subprocess.run([sys.executable, "tools/photo_shoot.py", "sheet", str(run), "--camera", clip["camera"],
                    "--frames", f"{first}-{last}:{max(1, (last - first) // 15)}", "--out", str(sheet)], cwd=REPO, check=True)
    print(sheet)


def encode(spec, runs, out_dir):
    out_dir.mkdir(parents=True, exist_ok=True)
    width, height = spec["clipSize"]
    for index, section in enumerate(spec["sections"]):
        clip = section.get("clip")
        if not clip:
            continue
        path = out_dir / f"{index + 1:02d}-{section['id']}.mp4"
        subprocess.run([sys.executable, "tools/photo_shoot.py", "clip", str(takes(runs, section["id"])[-1]), "--camera", clip["camera"],
                        "--from", str(clip["window"][0]), "--to", str(clip["window"][1]), "--width", str(width),
                        "--height", str(height), "--crf", "28", "--out", str(path)], cwd=REPO, check=True, capture_output=True)
        print(path, f"{path.stat().st_size // 1024} KB")


BOARD = """<!doctype html><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>SLOPArena store banners</title>
<style>
body{{margin:0;background:#1b2838;color:#c7d5e0;font:15px/1.45 Arial,sans-serif}}
main{{max-width:640px;margin:0 auto;padding:16px 12px 40px}}
h1{{font-size:20px;color:#fff;margin:4px 0 2px}}
h2{{font-size:17px;color:#fff;margin:26px 0 8px;padding-top:12px;border-top:1px solid #3a4d63}}
p.note{{color:#8f98a0;font-size:13px;margin:0 0 6px}}
img{{width:100%;display:block;margin:10px 0}}
</style><main>
<h1>Store banners, three looks</h1>
<p class="note">Steam-like column at its real width. Same six sections, same shots. Pick a look; headlines and shots are edits in sections.json.</p>
{body}
</main>"""


def board(spec, out_dir):
    body = []
    for style in STYLES:
        paths = render(spec, style, out_dir / style)
        body.append(f"<h2>{style.title()}</h2><p class='note'>{STYLES[style].__doc__}</p>")
        body.extend(f"<img src='{p.relative_to(out_dir)}' alt=''>" for p in paths)
    (out_dir / "board.html").write_text(BOARD.format(body="\n".join(body)))
    return out_dir / "board.html"


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    one = sub.add_parser("banners")
    one.add_argument("spec")
    one.add_argument("--style", choices=STYLES, help="defaults to the spec's style")
    one.add_argument("--out", type=Path, required=True)
    many = sub.add_parser("board")
    many.add_argument("spec")
    many.add_argument("--out", type=Path, required=True)
    film = sub.add_parser("shoot")
    film.add_argument("spec")
    film.add_argument("section")
    film.add_argument("--runs", type=Path, required=True)
    cut = sub.add_parser("encode")
    cut.add_argument("spec")
    cut.add_argument("--runs", type=Path, required=True)
    cut.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    spec = load(args.spec)
    if args.command == "banners":
        for path in render(spec, args.style or spec["style"], args.out):
            print(path)
    elif args.command == "board":
        print(board(spec, args.out))
    elif args.command == "shoot":
        shoot(spec, args.section, args.runs.resolve())
    else:
        encode(spec, args.runs.resolve(), args.out)


if __name__ == "__main__":
    main()
