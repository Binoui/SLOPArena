#!/usr/bin/env python3
"""Export Unity's newcomer UI art at twice its web display size."""
from pathlib import Path
import re
from PIL import Image

ROOT = Path(__file__).resolve().parents[3]
WEB = ROOT / 'client/Web'
UI = ROOT / 'client/Unity/Assets/Resources/UI'
OUT = WEB / 'assets/ui'
FIGHTERS = {
    'fightguy': 'FightGuy', 'manki': 'Manki', 'wibou': 'Wibou', 'bonk': 'Bonk',
    'bashie': 'Bashie', 'salamander': 'Salamander', 'nyx': 'Nyx',
}
written = []


def export(source, target, size):
    target = OUT / target
    target.parent.mkdir(parents=True, exist_ok=True)
    with Image.open(source) as image:
        image = image.convert('RGBA')
        image.thumbnail(size, Image.Resampling.LANCZOS)
        image.save(target, 'WEBP', quality=82, method=6)
    written.append(target)


for package, character in FIGHTERS.items():
    for source in sorted((UI / 'AbilityCards' / character).rglob('*.png')):
        relative = source.relative_to(UI / 'AbilityCards' / character)
        export(source, Path('abilities') / package / relative.with_suffix('.webp'), (144, 144))
    source = UI / 'Portraits' / f'{character}.png'
    if package == 'salamander':
        source = ROOT.parent / 'SlopArena-web/src/assets/fighters/salamander-portrait.png'
    export(source, Path('portraits') / f'{package}.webp', (256, 320))
export(UI / 'Portraits/Nyx_Fake.png', Path('portraits/nyx_fake.webp'), (256, 320))
main = (WEB / 'app/main.js').read_text()
stages = main.split('const STAGES = [', 1)[1].split('];', 1)[0]
for stage in re.findall(r"\['([a-z0-9_]+)'", stages):
    source = UI / 'Stages' / f'{stage}.png'
    # Mode-only courts have no Unity picker thumbnail.
    if stage not in ('slopball', 'license_test'):
        export(source, Path('stages') / f'{stage}.webp', (360, 202))
for group in ('abilities', 'portraits', 'stages'):
    files = [p for p in written if p.relative_to(OUT).parts[0] == group]
    print(f'{group}: {len(files)} files, {sum(p.stat().st_size for p in files):,} bytes')
print(f'Total: {len(written)} files, {sum(p.stat().st_size for p in written):,} bytes')
