#!/usr/bin/env python3
"""HUD move icons in the glyph style, from the live models (see the AbilityCards README).

decks.json holds, per package, the HUD folder, accent and fighter description, and per canonical slot the
frame to draw from, the move's subject and direction, plus optional clip, ticks and rig pack overrides.
Work happens in tmp/hud-icons/<package>/.

    hud_icons.py render manki [--slots 13A]   # Unity: render each move on magenta, write sheet.png
    hud_icons.py refs manki                   # cut each move's decks.json frame into refs/<slot>.png
    hud_icons.py job manki                    # write job.md: the filled prompt per slot for the image generator
    hud_icons.py prep manki                   # clean gen/<slot>.png into prepped/<slot>.png (hud_icon_prep.py)
    hud_icons.py board manki wibou            # review deck at real size and 3x: board.png
    hud_icons.py install manki                # copy prepped icons over the HUD sprites, keeping .meta files

`render` needs the GenRA venv and the rig packs in artifacts/genra-rigpacks (tools/genra/README.md), and runs
under your own Unity gateway lease. `install` prints the Assets paths to import afterwards.
"""
from __future__ import annotations

import argparse
import json
import re
import shutil
import subprocess
import sys
import uuid
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
sys.path.insert(0, str(REPO / 'tools'))
import hud_icon_prep  # noqa: E402

DECKS = json.loads((HERE / 'decks.json').read_text())
WORK = REPO / 'tmp/hud-icons'
CARDS = REPO / 'client/Unity/Assets/Resources/UI/AbilityCards'
GENRA_PY = REPO / 'artifacts/genra-20261014/.venv/bin/python'
SLOTS = '134AERF'
NORMALS = '134'
INK, PAPER, KEY_PLATE, TAB_PLATE = (27, 26, 24), (240, 238, 232), (74, 144, 226), (224, 92, 42)
STAGE = REPO / 'tools/hud_icons/stage.png'
FONT = ImageFont.truetype('/usr/share/fonts/TTF/DejaVuSans-Bold.ttf', 18)

TEMPLATE = """Game ability icon for a HUD key in SLOPArena, a goofy party platform fighter. Square 1024x1024 canvas, transparent background. The icon sits on {bg}.
Fighter: {fighter}
Subject: {subject}
Image 1 is the in-game render of this move: use it for the pose, proportions and prop shapes; simplify everything. Show whatever explains the move: the whole fighter when the body performs it (a lunge, kick, leap, spin, roll or slam); crop to the hand and prop only when a thrown object is the move.
Direction: the motion clearly travels {direction}.
Style: a bold flat pictogram, like a road sign or a mobile-game ability glyph: the fighter and props as one {shape} silhouette built from big simple shapes, with no inner detail, no outlines and no texture; a few thin gaps may separate overlapping limbs. A single {accent_name} {accent} motion arc, speed lines or impact burst carries the movement. Only those two colours. Image 2 shows the required glyph style only; do not copy its subject.
Composition: one dominant shape group filling about 85% of the canvas, centred by visual weight, readable as a 30-pixel silhouette. Prefer a wide, side-on pose over a tall one when the move allows.
No text, letters, numbers, symbols on clothing, frames, borders, badges, backing shapes or sticker borders, background scenery or drop shadows."""

JOB_RULES = """# HUD glyph icons: {pkg}

For each slot below, make one image with the image generator, square, transparent background.
Pass two reference images: Image 1 is the slot's `refs/<slot>.png` in this folder, Image 2 is the style anchor
`{anchor}`. Regenerate only for text or letters, a frame, a backing shape or background scenery.
Save each result as `gen/<slot>.png` in this folder, with the exact prompt used as `gen/<slot>.prompt.txt`.

"""
STYLE_ANCHOR = CARDS / 'FightGuy/icon.r.png'


def deck(pkg):
    if pkg not in DECKS:
        raise SystemExit(f'{pkg} is not in decks.json ({", ".join(DECKS)})')
    return DECKS[pkg]


def character_slots(pkg) -> dict:
    """Canonical grounded slot -> {clip, ticks, iconId} from the package's character.json."""
    data = json.loads((REPO / f'client/Unity/Assets/CharacterPackages/{pkg}/character.json').read_text())
    slots = {}
    for spec in data['slots']:
        if not spec['id'].startswith('ground.'):
            continue
        stages = spec['timeline'].get('stages', [])
        clips = [a for s in stages for a in s.get('animationIds', [])]
        ticks = spec['timeline'].get('durationTicks') or sum(s.get('durationTicks', 0) for s in stages)
        slots[spec['id'].split('.')[1]] = {'clip': clips[0] if clips else None, 'ticks': ticks, 'iconId': spec.get('iconId')}
    return slots


def chosen(pkg, slots):
    moves = deck(pkg)['moves']
    picked = [s for s in (slots or SLOTS) if s in moves]
    if not picked:
        raise SystemExit(f'no slots of {slots!r} in decks.json for {pkg}')
    return picked


def keyed(path) -> Image.Image:
    a = np.array(Image.open(path).convert('RGBA'))
    a[(a[..., 0] > 200) & (a[..., 1] < 80) & (a[..., 2] > 200), 3] = 0
    return Image.fromarray(a)


def render(pkg, slots):
    spec, live, work = deck(pkg), character_slots(pkg), WORK / pkg
    (work / 'briefs').mkdir(parents=True, exist_ok=True)
    runs_path = work / 'runs.json'
    runs = json.loads(runs_path.read_text()) if runs_path.exists() else {}
    rows = []
    for slot in chosen(pkg, slots):
        move = spec['moves'][slot]
        clip = move.get('clip') or live[slot]['clip']
        ticks = move.get('ticks') or min(max(live[slot]['ticks'], 20), 150)
        pack = move.get('pack') or spec.get('pack') or f'{pkg}-unity'
        brief = work / 'briefs' / f'{slot}.json'
        brief.write_text(json.dumps({'name': f'hud-icon-{pkg}-{slot}', 'rigpack': f'artifacts/genra-rigpacks/{pack}',
                                     'references': {'idle': 'references/idle.usda'}, 'entry': {'reference': 'idle', 'tick': 0},
                                     'clock': {'duration_ticks': ticks}, 'views': ['profile']}))
        out = subprocess.run([str(GENRA_PY), 'tools/genra/unity_clip.py', 'render', '--brief', str(brief), '--clip', f'x={clip}',
                              '--views', 'profile,front-three-quarter-right', '--size', '512', '--background', '255,0,255',
                              '--name', f'hud-icon-{pkg}-{slot}', '--margin', '1.9'], cwd=REPO, capture_output=True, text=True)
        if out.returncode:
            raise SystemExit(f'{pkg} {slot}: render failed\n{out.stdout[-800:]}\n{out.stderr[-800:]}')
        runs[slot] = json.loads(out.stdout[out.stdout.index('{'):])['evidence']
        runs_path.write_text(json.dumps(runs, indent=1))
        for view in ('profile', 'front-three-quarter-right'):
            frames = sorted((REPO / runs[slot] / 'frames/x' / view).glob('*.png'))
            row = Image.new('RGB', (2200, 220), (68, 68, 68))
            for i in range(10):
                tick = i * (len(frames) - 1) // 9
                image = keyed(frames[tick])
                image.thumbnail((220, 220))
                row.paste(image, (220 * i, 0), image)
                ImageDraw.Draw(row).text((220 * i + 6, 4), f'{slot} {view}:{tick}', fill='white', font=FONT)
            rows.append(row)
        print(f'{pkg} {slot}: {clip} over {ticks} ticks on {pack}')
    sheet = Image.new('RGB', (2200, 220 * len(rows)))
    for i, row in enumerate(rows):
        sheet.paste(row, (0, 220 * i))
    sheet.save(work / 'sheet.png')
    print(f'contact sheet: {work / "sheet.png"}; set each move\'s "frame" (view:tick) in decks.json, then run refs')


def refs(pkg, slots):
    work = WORK / pkg
    runs = json.loads((work / 'runs.json').read_text())
    (work / 'refs').mkdir(exist_ok=True)
    for slot in chosen(pkg, slots):
        view, tick = deck(pkg)['moves'][slot]['frame'].rsplit(':', 1)
        image = keyed(REPO / runs[slot] / 'frames/x' / view / f'{int(tick):04d}.png')
        image.crop(image.getbbox()).save(work / 'refs' / f'{slot}.png')
    print(f'refs: {work / "refs"}')


def prompt(pkg, slot) -> str:
    spec, move = deck(pkg), deck(pkg)['moves'][slot]
    normal = slot in NORMALS
    return TEMPLATE.format(bg='a light paper tab' if normal else 'a dark ink key', fighter=spec['fighter'],
                           subject=move['subject'], direction=move['direction'],
                           shape='solid dark ink #1B1A18' if normal else 'solid off-white #F0EEE8',
                           accent_name=spec['accentName'], accent=spec['accent'])


def job(pkg, slots):
    work = WORK / pkg
    (work / 'gen').mkdir(parents=True, exist_ok=True)
    text = JOB_RULES.format(pkg=pkg, anchor=STYLE_ANCHOR)
    for slot in chosen(pkg, slots):
        text += f'## Slot {slot}\n\nImage 1: `{work / "refs" / f"{slot}.png"}`\n\n```text\n{prompt(pkg, slot)}\n```\n\n'
    (work / 'job.md').write_text(text)
    print(f'job: {work / "job.md"}')


def prep(pkg, slots):
    work, accent = WORK / pkg, deck(pkg)['accent']
    (work / 'prepped').mkdir(exist_ok=True)
    for slot in chosen(pkg, slots):
        src = work / 'gen' / f'{slot}.png'
        if not src.exists():
            print(f'{pkg} {slot}: no gen/{slot}.png yet')
            continue
        used = src.with_suffix('.prompt.txt')
        hud_icon_prep.prepare(src, work / 'prepped' / f'{slot}.png', accent, 0.88, 256,
                              used.read_text().strip() if used.exists() else prompt(pkg, slot), solid=slot in NORMALS)
    print(f'prepped: {work / "prepped"}')


def fit(path, w, h, fill=0.92):
    art = Image.open(path).convert('RGBA')
    art = art.crop(art.getbbox())
    scale = min(w * fill / art.width, h * fill / art.height)
    art = art.resize((max(1, round(art.width * scale)), max(1, round(art.height * scale))), Image.LANCZOS)
    canvas = Image.new('RGBA', (w, h))
    canvas.alpha_composite(art, ((w - art.width) // 2, (h - art.height) // 2))
    return canvas


def button(path, w, h, face, edge, plate, depth):
    tile = Image.new('RGBA', (w + depth, h + depth))
    draw = ImageDraw.Draw(tile)
    draw.rectangle((depth, depth, w + depth - 1, h + depth - 1), fill=plate)
    draw.rectangle((0, 0, w - 1, h - 1), fill=edge)
    draw.rectangle((2, 2, w - 3, h - 3), fill=face)
    if path and path.exists():
        tile.alpha_composite(fit(path, w - 4, h - 4), (2, 2))
    return tile


def deck_image(icons: dict) -> Image.Image:
    """The grounded deck as DESIGN.md sizes it: 64x34 paper normal tabs over 64x64 ink special keys."""
    bg = Image.open(STAGE).convert('RGB').resize((340, 140)) if STAGE.exists() else Image.new('RGB', (340, 140), (200, 170, 120))
    for i, slot in enumerate(NORMALS):
        tab = button(icons.get(slot), 64, 34, PAPER, INK, TAB_PLATE, 3)
        bg.paste(tab, (98 + i * 80, 10), tab)
    for i, slot in enumerate('AERF'):
        key = button(icons.get(slot), 64, 64, INK, PAPER, KEY_PLATE, 4)
        bg.paste(key, (18 + i * 80, 58), key)
    return bg


def installed(pkg) -> dict:
    live = character_slots(pkg)
    return {slot: CARDS / deck(pkg)['folder'] / f'{live[slot]["iconId"]}.png' for slot in SLOTS if slot in live}


def board(pkgs, source, out):
    rowh = 40 + 420 + 16
    image = Image.new('RGB', (12 + 340 + 24 + 1020 + 12, 10 + rowh * len(pkgs)), (40, 40, 40))
    draw = ImageDraw.Draw(image)
    for r, pkg in enumerate(pkgs):
        icons = installed(pkg) if source == 'installed' else {s: WORK / pkg / 'prepped' / f'{s}.png' for s in SLOTS}
        y = 10 + r * rowh
        draw.text((12, y), f'{deck(pkg)["folder"]} ({source})', fill=PAPER, font=FONT)
        strip = deck_image(icons)
        image.paste(strip, (12, y + 40))
        draw.text((12, y + 184), 'real size', fill=(170, 170, 170), font=FONT)
        image.paste(strip.resize((1020, 420), Image.NEAREST), (12 + 340 + 24, y + 40))
    out = Path(out or WORK / ('board.png' if len(pkgs) > 1 else f'{pkgs[0]}/board.png'))
    out.parent.mkdir(parents=True, exist_ok=True)
    image.save(out)
    print(f'board: {out}')


def new_meta(template: Path, name: str) -> str:
    """A sprite importer for a new icon, copied from an installed one with a fresh GUID and the new sprite name."""
    text = template.read_text()
    old = template.name[:-len('.png.meta')]
    text = re.sub(r'^guid: [0-9a-f]{32}$', f'guid: {uuid.uuid4().hex}', text, count=1, flags=re.M)
    return re.sub(rf'(?<![\w.]){re.escape(old)}(?=[:\s])', name, text)


def install(pkg, slots):
    work, targets = WORK / pkg, installed(pkg)
    template = CARDS / 'FightGuy/icon.a.png.meta'
    paths = []
    for slot in chosen(pkg, slots):
        src, dst = work / 'prepped' / f'{slot}.png', targets[slot]
        if not src.exists():
            print(f'{pkg} {slot}: no prepped/{slot}.png, skipped')
            continue
        if Image.open(src).size != (256, 256):
            raise SystemExit(f'{src} is not 256x256: existing sprite slices cover exactly 256x256')
        dst.parent.mkdir(parents=True, exist_ok=True)
        meta = dst.with_name(dst.name + '.meta')
        if not meta.exists():
            meta.write_text(new_meta(template, dst.stem))
        shutil.copyfile(src, dst)
        paths.append(str(dst.relative_to(REPO / 'client/Unity')))
    print('\n'.join(paths))
    print('import exactly these under your own lease (AssetDatabase.ImportAsset), then check that '
          'Resources.LoadAll<Sprite>("UI/AbilityCards/<Folder>/<iconId>") returns one sprite each')


def main():
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    parser.add_argument('command', choices=['render', 'refs', 'job', 'prep', 'board', 'install'])
    parser.add_argument('packages', nargs='+')
    parser.add_argument('--slots', help='canonical slots, e.g. 13AF (default: all seven)')
    parser.add_argument('--source', choices=['prepped', 'installed'], default='prepped', help='board: which icons to show')
    parser.add_argument('--out', help='board: output PNG')
    args = parser.parse_args()
    if args.command == 'board':
        return board(args.packages, args.source, args.out)
    for pkg in args.packages:
        {'render': render, 'refs': refs, 'job': job, 'prep': prep, 'install': install}[args.command](pkg, args.slots)


if __name__ == '__main__':
    main()
