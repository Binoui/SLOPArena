#!/usr/bin/env python3
"""Retime Nyx's authored move clocks. Run with --write to apply; default is a preview.

retime-state.json journals original timing leaves before publication, so interrupted
and repeated runs converge without overwriting unrelated package edits.
"""
import argparse
import copy
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'client/Unity/Assets/CharacterPackages/nyx/character.json'
STATE = Path(__file__).with_name('retime-state.json')
FACTORS = {
    (form, f'{stance}.{button}'): 1.35 if (stance, button) == ('ground', '1') else 1.25
    for form in ('real', 'fake')
    for stance in ('ground', 'air')
    for button in ('1', '3', '4', 'A', 'E', 'R', 'F')
}
STAGE_CLOCKS = {'durationTicks', 'iasaTicks', 'autoCancelBeforeTicks', 'autoCancelAfterTicks'}
CAPABILITY_CLOCKS = {
    'slop.ability.thrust.v1': {'thrustTicks'},
    'slop.ability.aimed-recovery.v1': {'startupTicks'},
    'slop.ability.tether.v1': {'throwTick', 'maxReelTicks', 'missRecoveryTicks'},
    'slop.ability.command-grab.v1': {'lungeTicks', 'captureTick', 'releaseTick'},
}


def rounded(value, factor):
    return (value * round(factor * 100) + 50) // 100


def retime_slot(slot, factor):
    slot = copy.deepcopy(slot)
    for stage in slot['timeline']['stages']:
        for key in STAGE_CLOCKS:
            if key in stage:
                stage[key] = rounded(stage[key], factor)
        previous = -1
        for op in stage['operations']:
            op['tick'] = rounded(op['tick'], factor)
            assert previous <= op['tick'] < stage['durationTicks']
            previous = op['tick']
            for key in ('endTick', 'durationTicks'):
                if key in op:
                    op[key] = rounded(op[key], factor)
            for key in ('hitbox', 'placement'):
                if key in op and 'durationTicks' in op[key]:
                    op[key]['durationTicks'] = rounded(op[key]['durationTicks'], factor)
            if op['kind'] == 'startCapability':
                params = op['parameters']
                for key in CAPABILITY_CLOCKS[op['capabilityId']]:
                    params[key] = rounded(params[key], factor)
                if 'atTarget' in params:
                    for key in ('warningTicks', 'catchTicks'):
                        params['atTarget'][key] = rounded(params['atTarget'][key], factor)
                if 'capture' in params:
                    params['capture']['durationTicks'] = rounded(params['capture']['durationTicks'], factor)
    return slot


def leaves(before, after, path=()):
    if isinstance(before, dict):
        for key in before:
            yield from leaves(before[key], after[key], path + (key,))
    elif isinstance(before, list):
        for i, value in enumerate(before):
            yield from leaves(value, after[i], path + (i,))
    elif before != after:
        yield {'path': list(path), 'before': before, 'after': after}


def summary(slot):
    windows = []
    offset = 0
    iasa = 0
    for stage in slot['timeline']['stages']:
        if stage['iasaTicks']:
            iasa = offset + stage['iasaTicks']
        for op in stage['operations']:
            tick = offset + op['tick']
            if op['kind'] == 'spawnHitbox':
                windows.append([tick, tick + op['hitbox']['durationTicks'] - 1])
            elif op['kind'] == 'spawnProjectile':
                windows.append([tick, tick])
            elif op['kind'] == 'startCapability':
                p = op['parameters']
                if 'atTarget' in p:
                    first = tick + p['throwTick'] + p['atTarget']['warningTicks']
                    windows.append([first, first + p['atTarget']['catchTicks'] - 1])
                if 'captureTick' in p:
                    first = tick + p['captureTick']
                    windows.append([first, first + p['capture']['durationTicks'] - 1])
        offset += stage['durationTicks']
    windows = sorted(set(tuple(w) for w in windows))
    return {'first': windows[0][0] if windows else None, 'active': windows, 'iasa': iasa, 'total': offset}


def atomic_json(path, value):
    text = json.dumps(value, indent=2) + '\n'
    if path.exists() and path.read_text() == text:
        return
    temporary = path.with_suffix(path.suffix + '.tmp')
    temporary.write_text(text)
    temporary.replace(path)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--write', action='store_true')
    parser.add_argument('--timing', type=Path, default=ROOT / 'tmp/nyx-model/genra/timing.json')
    args = parser.parse_args()
    current = json.loads(SOURCE.read_text())
    baseline = copy.deepcopy(current)
    if STATE.exists():
        state = json.loads(STATE.read_text())
        for change in state['changes']:
            obj = baseline
            for key in change['path'][:-1]:
                obj = obj[key]
            key = change['path'][-1]
            if obj[key] not in (change['before'], change['after']):
                raise SystemExit(f"Timing edited since retime at {change['path']}; reconcile the journal first.")
            obj[key] = change['before']
    after = copy.deepcopy(baseline)
    rows = []
    for form, before_form, after_form in [('real', baseline, after), ('fake', baseline['alternateForm'], after['alternateForm'])]:
        authored = {s['id']: s for s in before_form['slots']}
        aliases = {a['from']: a['to'] for a in before_form['aliases']}
        for slot in after_form['slots']:
            slot.update(retime_slot(slot, FACTORS[form, slot['id']]))
        updated = {s['id']: s for s in after_form['slots']}
        for (key_form, address), factor in FACTORS.items():
            if key_form != form:
                continue
            target = aliases.get(address, address)
            assert factor == FACTORS[form, target], 'An alias cannot have a different factor.'
            rows.append({'form': form, 'slot': address, 'factor': factor,
                         'before': summary(authored[target]), 'after': summary(updated[target])})
    state = {'appliedFactors': {f'{f}/{s}': v for (f, s), v in FACTORS.items()},
             'changes': list(leaves(baseline, after)), 'moves': rows}
    print('| Form | Slot | Factor | First hit | Active windows, inclusive | IASA | Total |')
    print('| --- | --- | --- | --- | --- | --- | --- |')
    for row in rows:
        def pair(key):
            return f"{row['before'][key]} → {row['after'][key]}"
        print(f"| {row['form']} | {row['slot']} | {row['factor']} | {pair('first')} | {pair('active')} | {pair('iasa')} | {pair('total')} |")
    if args.write:
        atomic_json(STATE, state)
        atomic_json(SOURCE, after)
        if args.timing.exists():
            timing = json.loads(args.timing.read_text())
            for move in timing['moves'].values():
                row = next((r for r in rows if r['form'] == move['form'] and r['slot'] == move['slot']), None)
                if row:
                    game = move['game']
                    data = row['after']
                    game.update(total=data['total'], iasa=data['iasa'], contact=data['first'],
                                active=[data['active'][0][0], data['active'][-1][1]] if data['active'] else [],
                                activeWindows=data['active'], timeFactor=row['factor'])
                    if 'hits' in game:
                        game['hits'] = [w[0] for w in data['active']]
                    game['activeDefinition'] = 'Inclusive hitbox or capture window; projectile rows mark launch only.'
                    form = after if row['form'] == 'real' else after['alternateForm']
                    slot = next(s for s in form['slots'] if s['id'] == row['slot'])
                    capability = next((op for st in slot['timeline']['stages'] for op in st['operations']
                                       if op['kind'] == 'startCapability'), None)
                    if capability:
                        p = capability['parameters']
                        game['phases'] = {key: value for key, value in p.items()
                                          if key in CAPABILITY_CLOCKS[capability['capabilityId']]}
                        if 'atTarget' in p:
                            game['phases'].update({key: p['atTarget'][key] for key in ('warningTicks', 'catchTicks')})
                        if 'capture' in p:
                            game['phases']['captureDurationTicks'] = p['capture']['durationTicks']
                    move.pop('playbackScale', None)
            for form, ticks in [('real', 24), ('fake', 26)]:
                timing['moves'][form + '-run']['game']['total'] = ticks
                timing['moves'][form + '-run']['retimedClipTicks'] = ticks
            atomic_json(args.timing, timing)
        print('Rebuild sloparena.presentation.nyx.build, then run sloparena.presentation.nyx.verify-timing: '
              'paper effect layers are keyed to these ticks.')


if __name__ == '__main__':
    main()
