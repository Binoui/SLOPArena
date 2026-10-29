"""Prepare Nyx's approved FightGuy stand-in kit, preserving an engine-authored alternateForm."""
from pathlib import Path
import json
import sys
ROOT = Path(__file__).resolve().parents[3]
source = ROOT/'client/Unity/Assets/CharacterPackages/fightguy/character.json'
target = ROOT/'client/Unity/Assets/CharacterPackages/nyx/character.json'

def own_id(value):
    if value.startswith('anim.fightguy.'):
        return value.replace('anim.fightguy.', 'anim.nyx.', 1)
    return value.replace('anim.', 'anim.nyx.', 1) if value.startswith('anim.') else value

def own(value):
    if isinstance(value, str):
        return own_id(value)
    if isinstance(value, list):
        return [own(v) for v in value if not isinstance(v, dict) or v.get('kind') != 'emitPresentation']
    if isinstance(value, dict):
        return {k: ('' if k == 'explosionPresentationId' else own(v)) for k, v in value.items()
                if k not in {'presentationIds', 'hitPresentationId'} and not (k == 'type' and v == 'emitPresentation')}
    return value

j = own(json.loads(source.read_text()))
j['displayName'] = 'Nyx'
j['presentationIds'] = []
j['presentation'].update(visualScale=1.0375, modelYOffset=-0.85, modelSoleOffset=0, autoModelYOffset=False,
                         modelResourcePath='', hurtboxBoneScale=1)
j['hipHeight'] = 0.832
for slot in j['slots']:
    slot['description'] = 'FightGuy placeholder. '+slot.get('description','')
if target.exists():
    current = json.loads(target.read_text())
    if 'alternateForm' in current:
        raise SystemExit('Engine alternateForm already exists. Refusing to overwrite current Nyx source.')
if '--output' in sys.argv:
    target = Path(sys.argv[sys.argv.index('--output')+1])
target.parent.mkdir(parents=True, exist_ok=True)
target.write_text(json.dumps(j, indent=2)+'\n')
print(target)
