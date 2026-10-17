"""Automated animation checks, usable during authoring and after export.

check_clip(clip, spec) returns {'passed', 'failures', 'warnings', 'metrics'}; format_report
turns it into short text. Every failure names the tick and joint so it can be fixed.
"""
from __future__ import annotations

import numpy as np

from .clip import Clip, Pose

DEFAULT_SPEC = {
    'flexion_min_degrees': 15.0,       # hinge direction is judged above this flexion
    'snap_degrees_per_tick': 35.0,     # local rotation change limit outside fast windows
    'snap_ceiling_degrees_per_tick': 90.0,  # hard limit everywhere: more is a one-frame jump, not speed
    'fast_windows': [],                # [[first_tick, last_tick], ...] where fast motion is intended
    'clearance_metres': 0.01,          # region pairs closer than this are reported
    'pose_tolerance_metres': 0.001,
    'humanoid_degrees': 5.0,           # finger/thumb/toe rotation Unity's Humanoid cannot play
    'grip_metres': 0.02,               # two-handed ticks: left hand distance from the grip
    'grip_degrees': 15.0,
    'face_min_visible': 0.25,          # face visible fraction from the front below which it counts as hidden
    'face_hidden_ticks': 4,            # consecutive hidden ticks that are reported
}


def check_clip(clip: Clip, spec: dict | None = None, skin=None) -> dict:
    s = {**DEFAULT_SPEC, **(spec or {})}
    rig, world = clip.rig, clip.world()
    n = len(clip)
    failures, warnings, metrics = [], [], {}

    def fail(kind, message, **detail):
        failures.append({'check': kind, 'message': message, **detail})

    def warn(kind, message, **detail):
        warnings.append({'check': kind, 'message': message, **detail})

    # Clock: ticks 0..duration inclusive.
    if 'duration_ticks' in s and n != s['duration_ticks'] + 1:
        fail('clock', f"{n} samples; expected {s['duration_ticks'] + 1} (ticks 0..{s['duration_ticks']})")

    # Entry and exit poses.
    positions = world[:, :, 3, :3]
    tol = s['pose_tolerance_metres']
    entry: Pose | None = s.get('entry')
    if entry is not None:
        err = float(np.linalg.norm(positions[0] - entry.world()[:, 3, :3], axis=1).max())
        metrics['entryErrorMetres'] = err
        if err > tol:
            fail('entry', f'tick 0 differs from the entry pose by {err * 1000:.1f} mm')
    exit_from = s.get('exit_from_tick')
    if exit_from is not None:
        target = (s.get('exit') or entry).world()[:, 3, :3]
        errs = np.linalg.norm(positions[exit_from:] - target, axis=2).max(axis=1)
        metrics['exitErrorMetres'] = float(errs.max())
        bad = [exit_from + i for i, e in enumerate(errs) if e > tol]
        if bad:
            fail('exit', f'ticks {bad[0]}..{bad[-1]} differ from the exit pose by up to {errs.max() * 1000:.1f} mm', ticks=bad)

    # Frozen channels and fixed root. The hips may translate (crouch, lunge, weight shift): Unity bakes that into
    # the pose, and the fighter itself never moves. Bone lengths, scales and any joint above the hips stay fixed.
    hips = rig.index('Hips')
    if s.get('freeze_translations', True):
        moved = np.abs(clip.t - clip.t[0]).max(axis=(0, 2))
        moved[hips] = 0.0
        if moved.max() > 1e-4:  # skeleton units; float32 storage noise is ~1e-5
            fail('translations', f'{rig.names[int(np.argmax(moved))]} translation changes by up to {moved.max():.2e}; '
                                 'only the hips may translate')
    if s.get('freeze_scales', True):
        d = float(np.abs(clip.s - clip.s[0]).max())
        if d > 1e-3:  # USD stores scales as half floats
            fail('scales', f'joint scales change by up to {d:.2e}')
    h = positions[:, hips]
    metrics['hipTravelMetres'] = float(np.linalg.norm(h - h[0], axis=1).max())
    if s.get('root_fixed', True):
        above, j = [], rig.parents[hips]
        while j >= 0:
            above.append(int(j))
            j = rig.parents[j]
        for j in above:
            d = float(np.abs(world[:, j] - world[0, j]).max())
            if d > 1e-5:
                fail('root', f'{rig.names[j]} (above the hips) moves; only the hips and below may animate')

    # Planted joints (grounded moves): feet must not slide while the body moves.
    planted = s.get('planted') or []
    if planted:
        tol = s.get('plant_tolerance_metres', 0.01)
        for joint in planted:
            p = positions[:, rig.index(joint)]
            drift = np.linalg.norm(p - p[0], axis=1)
            metrics[f'{joint} drift (m)'] = float(drift.max())
            if drift.max() > tol:
                bad = np.where(drift > tol)[0]
                fail('planted', f'{joint} drifts {drift.max() * 100:.1f} cm (ticks {bad[0]}..{bad[-1]}); keep it planted '
                     f'within {tol * 100:.0f} cm with two_bone leg IK', joint=joint, ticks=bad.tolist())

    # Anatomical hinge direction. Knees/elbows: reversed above 15 degrees, sideways above 30 (a nearly
    # straight limb has no reliable bend plane). Fingers: reversed above 30 (knuckles hyperextend naturally;
    # sideways knuckle motion is splay, not an error).
    bad_hinges = {}
    for name, (kind_of, *_rest) in rig.hinges.items():
        finger = kind_of == 'finger'
        reversed_min = max(s.get('finger_reversed_min_degrees', 30.0) if finger else s['flexion_min_degrees'],
                           rig.reversed_allowance(name))
        sideways_min = None if finger else s.get('sideways_min_degrees', 30.0)
        for tick in range(n):
            flex, align = rig.hinge_state(world[tick], name)
            if align <= -0.5 and flex > reversed_min:
                kind = 'reversed'
            elif sideways_min is not None and abs(align) < 0.5 and flex > sideways_min:
                kind = 'sideways'
            else:
                continue
            bad_hinges.setdefault((name, kind), []).append((tick, round(flex, 1), round(align, 2)))
    for (name, kind), rows in bad_hinges.items():
        fail('hinge', f'{name} bends {kind} at ticks {rows[0][0]}..{rows[-1][0]} ({len(rows)} ticks; worst flexion '
             f'{max(r[1] for r in rows)} deg)', joint=name, bend=kind, ticks=[r[0] for r in rows])

    # Unity's Humanoid plays finger, thumb and toe rotations only about each joint's own muscle axes; anything
    # else is lost in the game (rig packs with humanoid.json).
    if rig.humanoid_digits and s.get('humanoid_digits', True):
        off = np.array([[rig.humanoid_split(j, clip.q[tick, j])[1] for j in rig.humanoid_digits] for tick in range(n)])
        metrics['humanoid digit off-axis (deg)'] = float(off.max())
        bad = [(rig.names[j], float(off[:, c].max()), int(off[:, c].argmax())) for c, j in enumerate(rig.humanoid_digits)
               if off[:, c].max() > s['humanoid_degrees']]
        if bad:
            name, degrees, tick = max(bad, key=lambda row: row[1])
            fail('humanoid', f'{len(bad)} finger/thumb/toe joints turn about axes Unity cannot play, worst {name} '
                             f'{degrees:.0f} deg at tick {tick}; use fist()/set_hinge() or pose.snap_to_humanoid()',
                 joints=[row[0] for row in bad], tick=tick)

    # Snaps: per-tick local rotation change.
    speed = clip.angular_speed()  # (n-1, joints); row i is tick i -> i+1
    fast = set()
    for a, b in s['fast_windows']:
        fast.update(range(a, b + 1))
    limit = s['snap_degrees_per_tick']
    snaps = []
    ceiling = s['snap_ceiling_degrees_per_tick']
    for i, j in zip(*np.where(speed > limit)):
        tick = int(i) + 1
        if tick not in fast or speed[i, j] > ceiling:
            snaps.append((tick, rig.names[j], float(speed[i, j])))
    metrics['maxDegreesPerTick'] = float(speed.max()) if len(speed) else 0.0
    if snaps:
        snaps.sort(key=lambda x: -x[2])
        worst = ', '.join(f'{name} {deg:.0f} deg at tick {tick}' for tick, name, deg in snaps[:5])
        fail('snap', f'{len(snaps)} joint-ticks exceed {limit:.0f} deg/tick outside fast windows or {ceiling:.0f} inside: {worst}',
             joints=sorted({x[1] for x in snaps}), ticks=sorted({x[0] for x in snaps}))

    # Region clearances (vertex proximity).
    if s.get('clearance', True):
        from .skin import Skin
        skin = skin or Skin(rig)
        ticks = s.get('clearance_ticks') or list(range(0, n, 2)) + [n - 1]
        closest = {}
        for tick in ticks:
            for pair, d in skin.clearances(world[tick]).items():
                if pair not in closest or d < closest[pair][0]:
                    closest[pair] = (d, tick)
        metrics['closestMetres'] = {k: [round(v[0], 4), v[1]] for k, v in closest.items()}
        start = {k: v for k, v in skin.clearances(world[0]).items()}
        for pair, (d, tick) in closest.items():
            if d < s['clearance_metres'] and start.get(pair, 1) >= s['clearance_metres']:
                warn('clearance', f'{pair} within {d * 100:.1f} cm at tick {tick} (touching or intersecting)', pair=pair, tick=tick)

    # Two-handed ticks: the left hand stays on the character's own grip (rig pack grip.json).
    span = s.get('two_hands')
    if span and rig.grip:
        from .solve import grip_report
        misses = [(tick, grip_report(clip.pose(tick))) for tick in range(span[0], min(span[1], n - 1) + 1)]
        off = [(t, r) for t, r in misses if r['leftHandMissMetres'] > s['grip_metres'] or r['leftHandMissDegrees'] > s['grip_degrees']]
        metrics['left hand off the grip (m)'] = max(r['leftHandMissMetres'] for _, r in misses)
        if off:
            tick, worst = max(off, key=lambda row: row[1]['leftHandMissMetres'])
            fail('grip', f'the left hand leaves the grip on {len(off)} of ticks {span[0]}..{span[1]}, worst at tick {tick} '
                         f'({worst["leftHandMissMetres"] * 100:.1f} cm, {worst["leftHandMissDegrees"]:.0f} deg); '
                         'place it with two_hand_grip() and keep it with hold_grip()', ticks=[t for t, _ in off])

    # The face reads the action: report stretches where the character's own arms or weapon hide it from the
    # front. A warning, because approved clips do it briefly (Bonk's jump start hides it for 17 ticks).
    if s.get('face', True) and clip.frame is not None:
        from .render import Renderer, visibility
        from .skin import Skin
        skin = skin or Skin(rig)
        if 'Head' in skin.region_names:
            renderer = Renderer(skin, clip.frame)
            seen = np.array([visibility(renderer, world[tick], ['Head'], 'front', size=160) for tick in range(n)])
            metrics['face visible from the front (min)'] = float(seen.min())
            hidden = seen < s['face_min_visible']
            runs, start = [], None
            for tick, flag in enumerate(list(hidden) + [False]):
                if flag and start is None:
                    start = tick
                elif not flag and start is not None:
                    runs.append((start, tick - 1))
                    start = None
            for a, b in runs:
                if b - a + 1 >= s['face_hidden_ticks']:
                    warn('face', f'arms or weapon hide the face from the front at ticks {a}..{b} (as little as '
                                 f'{seen[a:b + 1].min() * 100:.0f}% visible)', ticks=[a, b])

    # Brief-specific numeric targets.
    for t in s.get('targets', []):
        value = measure(clip, t)
        metrics[t['name']] = value
        lo, hi = t.get('min'), t.get('max')
        if (lo is not None and value < lo) or (hi is not None and value > hi):
            bound = ' and '.join(x for x in [f'>= {lo}' if lo is not None else '', f'<= {hi}' if hi is not None else ''] if x)
            fail('target', f"{t['name']} = {value:.3f}; wanted {bound}", target=t['name'])

    return {'passed': not failures, 'failures': failures, 'warnings': warnings, 'metrics': metrics}


def measure(clip: Clip, t: dict) -> float:
    """Evaluate one target. Kinds (char-frame metres, ticks):
    distance: |a - b| at tick           ahead: a ahead of b (front axis) at tick
    below: b above a (up axis) at tick  char: component ('right'|'front'|'up') of a at tick
    fastest_drop: tick ending a's fastest downward step within [first, last]
    """
    kind, tick = t['kind'], t.get('tick')
    a = t.get('a')
    b = t.get('b')
    if kind == 'fastest_drop':
        first, last = t.get('window', [0, len(clip) - 1])
        z = clip.positions(a)[:, 2]
        step = np.diff(z)[first:last]
        return float(first + int(np.argmin(step)) + 1)
    pa = clip.positions(a)[tick]
    pb = clip.positions(b)[tick] if b else None
    if kind == 'distance':
        return float(np.linalg.norm(pa - pb))
    if kind == 'ahead':
        return float(pa[1] - pb[1])
    if kind == 'below':
        return float(pb[2] - pa[2])
    if kind == 'char':
        return float(pa[{'right': 0, 'front': 1, 'up': 2}[t['axis']]])
    raise ValueError(f'Unknown target kind {kind}')


def format_report(report: dict) -> str:
    lines = ['PASS' if report['passed'] else f"FAIL ({len(report['failures'])})"]
    lines += [f"  x {f['check']}: {f['message']}" for f in report['failures']]
    lines += [f"  ! {w['check']}: {w['message']}" for w in report['warnings']]
    m = report['metrics']
    lines.append(f"  max {m.get('maxDegreesPerTick', 0):.0f} deg/tick; hip travel {m.get('hipTravelMetres', 0) * 1000:.2f} mm")
    for k, v in m.items():
        if k not in {'maxDegreesPerTick', 'hipTravelMetres', 'closestMetres', 'entryErrorMetres', 'exitErrorMetres'}:
            lines.append(f'  {k}: {v:.3f}')
    return '\n'.join(lines)
