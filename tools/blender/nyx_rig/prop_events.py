"""Write exact timeline-operation handoff entries without modifying gameplay source."""
import json
from pathlib import Path

root = Path(__file__).resolve().parents[3]
source = root/'client/Unity/Assets/CharacterPackages/nyx/character.json'
character = json.loads(source.read_text())
ids = ['presentation.nyx.props.pole-held', 'presentation.nyx.props.phone-raise', 'presentation.nyx.props.phone-drop']
result = {'presentationIdsToAdd': ids, 'moves': [], 'notes': [
    'Insert each operation at the start of the listed stage operations. Preserve existing gameplay operations.',
    'air.F aliases ground.F in both forms and needs no duplicate operation.',
    'Bone attachment and AttackBoundPresentation bind visibility to this activation. Interruption, end, replacement and retraction restore defaults.',
    'Nyx_Phone is inactive in the saved real-form prefab and visible in fake-form defaults. Going Live exposes it on real form before swap. Logging Off hides it at tick 0.',
    'Durations reflect the current source. Rerun this script if the engine owner retimes those stages or swap ticks.'
]}
for form, slot, semantic, swap in [('fake', 'ground.2', ids[0], False), ('fake', 'air.2', ids[0], False), ('real', 'ground.F', ids[1], True), ('fake', 'ground.F', ids[2], True)]:
    definition = character if form == 'real' else character['alternateForm']
    stage = next(s for s in definition['slots'] if s['id'] == slot)['timeline']['stages'][0]
    duration = next(o['tick'] for o in stage['operations'] if o['kind'] == 'swapForm') if swap else stage['durationTicks']
    operation = {'kind': 'emitPresentation', 'tick': 0, 'unit': 'ticks', 'presentationId': semantic,
                 'placement': {'attachmentMode': 'bone', 'boneId': 'bone.hips', 'durationTicks': duration}}
    result['moves'].append({'form': form, 'slot': slot, 'stageIndex': 0, 'operationsToAdd': [operation]})
out = root/'tmp/nyx-model/props/events.json'
out.parent.mkdir(exist_ok=True)
out.write_text(json.dumps(result, indent=2)+'\n')
print(out)
