#!/usr/bin/env python3
"""Generate UI-only bindings without changing any character cook input."""
import argparse
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / 'client/Unity/Assets/Resources/Generated/FighterUi.json'


def generate(root=ROOT, admission=None):
    assets = root / 'client/Unity/Assets'
    entries = json.loads((root / 'content-cooked/roster/manifest.json').read_text())['entries']
    if admission is not None and not any(e['packageId'] == admission['packageId'] for e in entries):
        entries.append(admission)
    resources = assets / 'Resources'
    textures = {}
    for file in resources.rglob('*'):
        if not file.is_file() or file.suffix.lower() in {'.meta', '.md', '.txt', '.json'}:
            continue
        relative = file.relative_to(resources).as_posix()
        if not relative.lower().startswith(('ui/portraits/', 'ui/abilitycards/', 'icons/')):
            continue
        if file.suffix != '.png':
            raise ValueError(f'Fighter UI art requires .png files with canonical case: {relative}')
        key = relative[:-4].casefold()
        if key in textures:
            raise ValueError(f'Ambiguous Resources path: {relative}')
        textures[key] = relative[:-4]

    def texture(path):
        actual = textures.get(path.casefold())
        if actual is not None and actual != path:
            raise ValueError(f'Fighter UI art requires canonical case {path}.png, found {actual}.png')
        return path if actual is not None else ''

    fighters = []
    for entry in entries:
        package, alias = entry['packageId'], entry['selector']
        card = json.loads((assets / 'CharacterPackages' / package / 'card.json').read_text())
        if set(card) != {'description', 'workInProgress', 'tutorialHint'}:
            raise ValueError(f'Invalid card fields for {package}')
        if not isinstance(card['description'], str) or not card['description'].strip() or type(card['workInProgress']) is not bool:
            raise ValueError(f'Invalid card text for {package}')
        hint = card['tutorialHint']
        if hint is not None and (set(hint) != {'starter', 'followUp', 'jump'} or type(hint['jump']) is not bool
                                 or hint['starter'] not in ['1', '2', '3', 'A', 'E', 'R', 'F']
                                 or hint['followUp'] not in ['1', '2', '3', 'A', 'E', 'R', 'F']):
            raise ValueError(f'Invalid Tutorial hint for {package}')
        definition = json.loads((root / 'content-cooked' / package / 'character.runtime.json').read_text())['character']
        forms = [definition] + ([definition['alternateForm']] if definition.get('alternateForm') else [])
        portraits, icons = [], []
        for form, model in enumerate(forms):
            portrait = f'UI/Portraits/{alias}' + ('_Fake' if form else '')
            portraits.append(texture(portrait))
            paths = {}
            card_folder = f'UI/AbilityCards/{alias}/' + (('Fake/' if form else 'Real/') if len(forms) > 1 else '')
            for folder in [f'Icons/{alias}/', card_folder]:
                for actual in sorted(textures.values()):
                    if actual.casefold().startswith(folder.casefold()) and '/' not in actual[len(folder):]:
                        canonical = folder + actual[len(folder):]
                        paths.setdefault(actual[len(folder):], {})['card' if folder == card_folder else 'icon'] = texture(canonical)
            icons.append(paths)
        fighters.append(dict(packageId=package, alias=alias, names=[f['displayName'] for f in forms],
                             portraits=portraits, icons=icons, legacyWeapon=f'WeaponConfigs/{alias}' if (assets / 'Resources/WeaponConfigs' / (alias + '.asset')).exists() else '', hasSplash=f'fighter-panel--{alias} .fighter-panel__art' in (assets / 'UI/FighterPanels.uss').read_text(), card=card))
    return json.dumps(dict(fighters=fighters), indent=2, ensure_ascii=False) + '\n'


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true')
    parser.add_argument('--root', type=Path, default=ROOT, help=argparse.SUPPRESS)
    parser.add_argument('--prepare-admission', metavar='PACKAGE_ID')
    parser.add_argument('--selector')
    args = parser.parse_args()
    admission = None
    if args.prepare_admission:
        if args.check or not args.selector:
            parser.error('--prepare-admission requires --selector and cannot use --check')
        package = args.prepare_admission
        if not package or Path(package).name != package or '/' in package or '\\' in package:
            parser.error('Package ID must be a single folder name')
        card_path = args.root / 'client/Unity/Assets/CharacterPackages' / package / 'card.json'
        if not card_path.parent.is_dir():
            parser.error('Package source directory is missing')
        if not card_path.exists():
            card_path.write_text(json.dumps(dict(description='Admitted fighter with a style all its own.', workInProgress=True, tutorialHint=None), indent=2) + '\n')
        admission = dict(packageId=package, selector=args.selector)
    generated = generate(args.root, admission)
    output = args.root / OUTPUT.relative_to(ROOT)
    if args.check:
        if not output.exists() or output.read_text() != generated:
            raise SystemExit('Fighter UI bindings are stale. Run tools/fighter-presentation/generate.py')
    else:
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(generated)
