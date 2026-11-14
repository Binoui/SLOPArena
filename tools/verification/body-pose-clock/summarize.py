#!/usr/bin/env python3
"""Summarize the diagnostic player CSV. Values are joint distances in millimetres."""
import argparse
import collections
import csv
import json
import math
import statistics

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('csv')
parser.add_argument('--expect-full-roster', action='store_true')
parser.add_argument('--assert-attack-clock', action='store_true')
args = parser.parse_args()
groups = collections.defaultdict(list)
with open(args.csv) as source:
    for row in csv.DictReader(source):
        phase = row['phase']
        expected = {'idle': 'Idle', 'run': 'Run', 'hitstun': 'Hitstun', 'attack': 'Attacking'}[phase]
        if row['state'] != expected or float(row['weight']) < .999:
            continue
        if phase == 'run' and (not row['anim'].endswith('.run') or row['anim'] != row['authAnim']):
            continue
        groups[(row['fighter'], row['form'], phase)].append(row)

def stats(values):
    values = sorted(v for v in values if math.isfinite(v))
    if not values:
        return {'n': 0}
    return {'n': len(values), 'median': round(statistics.median(values), 3),
            'p95': round(values[min(len(values)-1, int(.95 * len(values)))], 3),
            'max': round(values[-1], 3)}

report = []
for (fighter, form, phase), rows in sorted(groups.items()):
    report.append({'fighter': fighter, 'form': int(form), 'phase': phase,
                   'currentMm': stats(float(r['currentMm']) for r in rows),
                   'forcedSharedTimeMm': stats(float(r['proposedMm']) for r in rows),
                   'renderMinusSharedFrames': stats(float(r['renderFrame'])-float(r['authFrame'])
                       for r in rows if r['anim'] == r['authAnim']),
                   'otherAnimationSamples': sum(r['anim'] != r['authAnim'] for r in rows)})
if args.expect_full_roster:
    expected = {(fighter, form, phase) for fighter in ['fightguy', 'manki', 'wibou', 'bonk', 'bashie', 'salamander', 'nyx']
                for form in (['0', '1'] if fighter == 'nyx' else ['0'])
                for phase in ['idle', 'run', 'hitstun', 'attack']}
    if set(groups) != expected:
        raise SystemExit(f'Missing or unexpected groups: {expected.symmetric_difference(groups)}')
    if any(not row['currentMm']['n'] or not row['forcedSharedTimeMm']['n'] for row in report):
        raise SystemExit('A measurement group has no finite joint samples')
if args.assert_attack_clock:
    attacks = [row for row in report if row['phase'] == 'attack' and row['renderMinusSharedFrames']['n']]
    if not attacks:
        raise SystemExit('FAIL: no full-weight attack clock samples')
    worst = max(abs(row['renderMinusSharedFrames']['median']) for row in attacks)
    if worst > .001:
        raise SystemExit(f'FAIL: median attack clock gap {worst:.3f} baked frames exceeds 0.001')
    print(f'PASS: median attack clock gap {worst:.3f} baked frames is within 0.001')
else:
    print(json.dumps(report, indent=2))
