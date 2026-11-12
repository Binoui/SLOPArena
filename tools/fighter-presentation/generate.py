#!/usr/bin/env python3
"""Generate UI-only bindings without changing any character cook input."""
import argparse
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / 'client/Unity/Assets/Resources/Generated/FighterUi.json'


def generate():
    assets = ROOT / 'client/Unity/Assets'
    entries = json.loads((ROOT / 'content-cooked/roster/manifest.json').read_text())['entries']
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
        definition = json.loads((ROOT / 'content-cooked' / package / 'character.runtime.json').read_text())['character']
        forms = [definition] + ([definition['alternateForm']] if definition.get('alternateForm') else [])
        portraits, icons = [], []
        for form, model in enumerate(forms):
            portrait = f'UI/Portraits/{alias}' + ('_Fake' if form else '')
            portraits.append(portrait if (assets / 'Resources' / (portrait + '.png')).exists() else '')
            paths = {}
            card_folder = f'UI/AbilityCards/{alias}/' + (('Fake/' if form else 'Real/') if len(forms) > 1 else '')
            for folder in [f'Icons/{alias}/', card_folder]:
                directory = assets / 'Resources' / folder
                if directory.exists():
                    for file in sorted(directory.glob('*.png')):
                        paths.setdefault(file.stem, {})['card' if folder == card_folder else 'icon'] = folder + file.stem
            icons.append(paths)
        fighters.append(dict(packageId=package, alias=alias, names=[f['displayName'] for f in forms],
                             portraits=portraits, icons=icons, legacyWeapon=f'WeaponConfigs/{alias}' if (assets / 'Resources/WeaponConfigs' / (alias + '.asset')).exists() else '', hasSplash=f'fighter-panel--{alias} .fighter-panel__art' in (assets / 'UI/FighterPanels.uss').read_text(), card=card))
    return json.dumps(dict(fighters=fighters), indent=2, ensure_ascii=False) + '\n'


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    generated = generate()
    if args.check:
        if not OUTPUT.exists() or OUTPUT.read_text() != generated:
            raise SystemExit('Fighter UI bindings are stale. Run tools/fighter-presentation/generate.py')
    else:
        OUTPUT.parent.mkdir(parents=True, exist_ok=True)
        OUTPUT.write_text(generated)
