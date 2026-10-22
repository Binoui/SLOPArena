"""When each foot lands: the ticks footstep sounds, dust and other cues key off (python -m genra contacts).

A foot is down while its foot or toe joint is within a tolerance of that joint's lowest point in the clip, and it lands
on the first tick of each run of down ticks. Briefs record no floor, so in an air move "down" only marks each foot's
lowest reach. Read landings from the clip itself rather than from the brief: its beats
are the intent, and Sir Bonk's Joust gallop landed one tick after its "contact 0" beats (2026-10-21), which put the
clip-clop knocks a frame early until they were measured.
"""
from __future__ import annotations

import numpy as np

FEET = (('LeftFoot', 'LeftToeBase'), ('RightFoot', 'RightToeBase'))


def foot_contacts(clip, tolerance: float = 0.02, loop_from: int | None = None) -> dict:
    """Per foot, its runs of down ticks and its landing ticks, plus every landing in tick order. With `loop_from`, ticks
    loop_from..end-1 repeat in the game (the clip's last tick equals loop_from), so a run can wrap round the loop; its
    last tick is then listed before its first."""
    names = set(clip.rig.names)
    count = len(clip)
    first = 0
    if loop_from is not None:
        first, count = loop_from, count - 1 - loop_from
    feet, landings = {}, []
    for foot, toe in FEET:
        if foot not in names:
            continue
        down = np.zeros(count, bool)
        for joint in (j for j in (foot, toe) if j in names):
            height = clip.positions(joint)[first:first + count, 2]  # char frame: right, front, up
            down |= height <= height.min() + tolerance
        runs = [(first + a, first + b) for a, b in _runs(down, wrap=loop_from is not None)]
        feet[foot] = {'landings': [a for a, _ in runs], 'down': [list(run) for run in runs]}
        landings += [(a, foot) for a, _ in runs]
    return {'tolerance': tolerance, 'loopFrom': loop_from, 'ticks': len(clip), 'feet': feet,
            'landings': [list(x) for x in sorted(landings)]}


def _runs(down, wrap: bool) -> list[tuple[int, int]]:
    """(first, last) index of each run of True values, the runs of a loop continuing across its end."""
    n = len(down)
    if down.all():
        return [(0, n - 1)] if n else []
    runs = []
    for i in range(n):
        before = down[i - 1] if (wrap or i > 0) else False
        if not down[i] or before:
            continue
        last = i
        while down[(last + 1) % n] and (wrap or last + 1 < n):
            last = (last + 1) % n
        runs.append((i, last))
    return runs


def format_contacts(report: dict) -> str:
    loop = report['loopFrom']
    head = f"Foot contacts: down within {report['tolerance'] * 100:.0f} cm of the joint's lowest point"
    if loop is not None:
        head += f"; ticks {loop}-{report['ticks'] - 2} loop"
    lines = [head]
    for foot, data in report['feet'].items():
        runs = ', '.join(f'{a}-{b}' if a != b else str(a) for a, b in data['down'])
        lines.append(f"  {foot}: lands on {', '.join(map(str, data['landings'])) or 'no tick'} (down {runs or 'never'})")
    lines.append('Landings in order: ' + (', '.join(f'{tick} {foot}' for tick, foot in report['landings']) or 'none'))
    return '\n'.join(lines)
