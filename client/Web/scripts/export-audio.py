#!/usr/bin/env python3
"""Export Unity's audio and licensed UI assets. Run from any directory; needs ffmpeg.

No Unity import or cook is involved. Generated files live in ignored assets/ and
are included by package.mjs. manifest.json records every source and adaptation.
"""
from pathlib import Path
import json
import re
import shutil
import subprocess

WEB = Path(__file__).resolve().parents[1]
REPO = WEB.parents[1]
UNITY = REPO / 'client/Unity/Assets'
OUT = WEB / 'assets/audio'
UI = WEB / 'assets/ui'


def convert(source, name, music=False):
    target = OUT / (name + '.mp3')
    subprocess.run(['ffmpeg', '-hide_banner', '-loglevel', 'error', '-nostdin', '-y',
                    '-i', str(source), '-map_metadata', '-1', '-vn',
                    '-ac', '2' if music else '1', '-ar', '44100' if music else '32000',
                    '-codec:a', 'libmp3lame', '-b:a', '96k' if music else '56k',
                    str(target)], check=True)
    return target.name


def main():
    if not shutil.which('ffmpeg'):
        raise SystemExit('ffmpeg is required; install it separately before exporting.')
    OUT.mkdir(parents=True, exist_ok=True)
    UI.mkdir(parents=True, exist_ok=True)
    clips = {}
    records = {}

    def add(key, source, note='', music=False):
        path = UNITY / source
        if not path.is_file():
            raise SystemExit(f'Missing Unity source: {path}')
        clips[key] = convert(path, key, music)
        records[key] = {'source': str(path.relative_to(REPO)), 'note': note}

    add('MainTheme', 'Resources/Audio/Music/MainTheme.mp3',
        'Owner-selected menu and match music. Lyria-generated; see CREDITS.md.', True)
    add('click', 'Resources/Audio/SFX/UI/Click/UI_Button_Click_1.wav', 'UISFX menu/countdown source; source-pack licence not recorded in repo.')
    add('jump', 'Resources/Audio/SFX/jump.mp3', 'PlayerRenderer jump source; source licence not recorded in repo.')
    add('block', 'Resources/Audio/SFX/Bonk/bonk_lid_tink.ogg',
        'Original tools/sfx/sir_bonk_clips.py; adapted for blocks (Unity block ripple is silent).')
    add('ko', 'Resources/Audio/SFX/Bonk/impact_heavy.ogg',
        'Original tools/sfx/bonk_foley_clips.py; adapted at lower pitch for KO and finish, like UISFX.PlaySoloFinish.')
    add('land', 'Resources/Audio/SFX/Bonk/bonk_thoom.ogg',
        'BonkSlamLanding prefab source, Kenney CC0 + synthesis; adapted quietly for ordinary landings.')
    for name in ['pop', 'splat', 'note', 'miss', 'beep', 'heat']:
        add('slopball_' + name, f'Resources/Audio/SFX/Slopball/slopball_{name}.ogg',
            'Original tools/sfx/slopball_clips.py; SlopballPresentation source.')

    # Resolve the same serialized GUID pools as CombatSFX, without loading Unity.
    sources = {}
    for path in (UNITY / 'Resources/Audio').rglob('*.meta'):
        source = path.with_suffix('')
        if source.suffix not in ('.wav', '.mp3', '.ogg'):
            continue
        match = re.search(r'^guid: (\w+)', path.read_text(), re.M)
        if match:
            sources[match[1]] = source
    pools = {}
    exported = {}
    for catalog in sorted((UNITY / 'CharacterPackages').glob('*/CharacterAssetCatalog.asset')):
        text = catalog.read_text()
        tiers = {}
        for tier in ['light', 'medium', 'heavy']:
            match = re.search(r'_' + tier + r'Impact:\n(.*?)(?=\n    _|\Z)', text, re.S)
            if not match:
                continue
            keys = []
            for guid in re.findall(r'guid: (\w+)', match[1]):
                source = sources.get(guid)
                if source is None:
                    raise SystemExit(f'Unresolved impact audio {guid} in {catalog}')
                if guid not in exported:
                    key = 'impact_' + guid[:12]
                    add(key, str(source.relative_to(UNITY)),
                        'CharacterPresentationProfile impact pool. Bonk is original synthesis; punch/Wibou source licences not recorded in repo.')
                    exported[guid] = key
                keys.append(exported[guid])
            if keys:
                tiers[tier] = keys
        if tiers:
            pools[catalog.parent.name] = tiers
    (OUT / 'manifest.json').write_text(json.dumps({'clips': clips, 'pools': pools, 'sources': records}, indent=2) + '\n')

    for name in ['ArchivoBlack-Regular.ttf', 'ArchivoBlack-OFL.txt', 'SpaceMono-Regular.ttf', 'SpaceMono-OFL.txt']:
        shutil.copyfile(UNITY / 'Fonts' / name, UI / name)
    shutil.copyfile(UNITY / 'UI/SlopArenaIcon.png', UI / 'SlopArenaIcon.png')
    subprocess.run(['ffmpeg', '-hide_banner', '-loglevel', 'error', '-nostdin', '-y',
                    '-i', str(UNITY / 'UI/FrontendPaperBackground.png'), '-vf', 'scale=1280:-1',
                    '-frames:v', '1', '-quality', '76', str(UI / 'paper.webp')], check=True)
    (UI / 'CREDITS.txt').write_text(
        'Archivo Black: Omnibus-Type / Archivo Black Project Authors, SIL OFL 1.1 (included).\n'
        'Space Mono: Colophon Foundry / Space Mono Project Authors, SIL OFL 1.1 (included).\n'
        'SLOPArena wordmark: typeset in Archivo Black, as in FrontendShell.uxml.\n'
        'Bomb icon and paper wallpaper: owner-generated SLOPArena UI art, 2026-10-18; '
        'see docs/assets/credits-and-licenses.md and CREDITS.md.\n')
    total = sum(p.stat().st_size for folder in [OUT, UI] for p in folder.iterdir() if p.is_file())
    print(f'Exported {len(clips)} clips, {len(pools)} fighter pools, fonts and UI: {total / 1048576:.2f} MiB')


if __name__ == '__main__':
    main()
