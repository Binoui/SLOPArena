"""Automated animation checks, usable during authoring and after export.

check_clip(clip, spec) runs full checks. mode='fast' skips mesh clearance, face
visibility and the gait's mesh arm gap for iteration. Reports name their mode and
skipped checks; a fast pass is not a delivery pass.
"""
from __future__ import annotations

from pathlib import Path

import numpy as np

from .clip import Clip, Pose

DEFAULT_SPEC = {
    'flexion_min_degrees': 15.0,       # hinge direction is judged above this flexion
    'snap_degrees_per_tick': 35.0,     # local rotation change limit outside fast windows
    'snap_ceiling_degrees_per_tick': 90.0,  # hard limit everywhere: more is a one-frame jump, not speed
    'fast_windows': [],                # [[first_tick, last_tick], ...] where fast motion is intended
    'clearance_metres': 0.01,          # region pairs closer than this are reported
    'pose_tolerance_metres': 0.001,
    'humanoid_degrees': 5.0,           # rotation Unity's Humanoid cannot play (digits off-axis, limits exceeded)
    'grip_metres': 0.02,               # two-handed ticks: left hand distance from the grip
    'grip_degrees': 15.0,
    'grip_roll_degrees': 90.0,         # how far the held hand may turn about the handle from the measured grip
    'flip_degrees_per_tick': 60.0,     # a limb bone rolling about its own length faster than this is an IK flip
    'flip_degrees_per_3_ticks': 120.0,  # ... or this much over three ticks (a flip spread out by a retime)
    'keys_metres': 0.02,               # approved key poses (run.py --keys): how far a joint may move from them
    'keys_ticks': 2,                   # ... within this many ticks of the key (overlap and drag), the weapon on its tick
    'flow_stops_per_second': 1.0,      # one-shot clips: joint stops per joint per second (hand-made p90 0.62, max 1.16)
    'flow_together_share': 0.35,       # ... share of the body stopping within the same three ticks (p90 0.27)
    'flow_frozen_share': 0.14,         # ... share of frozen joint-ticks outside the exit hold (corpus max 0.12)
    'face_min_visible': 0.25,          # face visible fraction from the front below which it counts as hidden
    'face_hidden_ticks': 4,            # consecutive hidden ticks that are reported
}


def check_clip(clip: Clip, spec: dict | None = None, skin=None, *, mode: str = 'full') -> dict:
    if mode not in ('full', 'fast'):
        raise ValueError(f'check mode must be full or fast, not {mode!r}')
    s = {**DEFAULT_SPEC, **(spec or {})}
    if s.get('stage') == 'keys':
        # Judge actual poses independently of their provisional interpolation. A worst
        # Humanoid sample between keys must not hide a smaller violation on a key.
        ticks = sorted(set(s['key_ticks']))
        risks = check_clip(clip, s | {'stage': 'full'}, mode='fast')
        poses = s | {'stage': 'full', 'pose_ticks': ticks, 'motion': False,
                     'clearance_ticks': ticks, 'face': False, 'life': False, 'flow': False,
                     'targets': [t for t in s.get('targets', []) if t.get('tick') in ticks]}
        report = check_clip(clip, poses, skin, mode=mode)
        report.update(stage='keys', keyTicks=ticks, transitionRisks=risks['failures'])
        return report
    skipped = []
    if mode == 'fast':
        skipped = ['clearance', 'face', 'arm_gap']
        s.update({name: False for name in skipped})
    rig, world = clip.rig, clip.world()
    n = len(clip)
    checked = np.array([t for t in s.get('pose_ticks', range(n)) if 0 <= t < n], dtype=int)
    motion = s.get('motion', True)
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
        exit_ticks = checked[checked >= exit_from]
        errs = np.linalg.norm(positions[exit_ticks] - target, axis=2).max(axis=1)
        if not len(errs):
            errs = np.array([0.0])
        metrics['exitErrorMetres'] = float(errs.max())
        bad = [int(t) for t, e in zip(exit_ticks, errs) if e > tol]
        if bad:
            fail('exit', f'ticks {bad[0]}..{bad[-1]} differ from the exit pose by up to {errs.max() * 1000:.1f} mm', ticks=bad)

    # Loops: the game wraps from the last tick back to loop_from_tick, so those poses must match and the motion
    # must keep its speed across the seam (no stop, no kink). The seam may not jerk harder than the loop itself.
    loop_from = s.get('loop_from_tick')
    if loop_from is not None and n - 1 - loop_from >= 3:
        last = n - 1
        seam = np.linalg.norm(positions[last] - positions[loop_from], axis=1)
        metrics['loopSeamMetres'] = float(seam.max())
        if seam.max() > tol:
            fail('loop', f'tick {last} differs from tick {loop_from} by up to {seam.max() * 1000:.1f} mm '
                         f'({rig.names[int(seam.argmax())]}); the loop must close exactly', ticks=[loop_from, last])
        elif motion:
            jerk = np.linalg.norm(positions[loop_from + 1] - 2 * positions[last] + positions[last - 1], axis=1)
            inner = positions[loop_from:last + 1]
            inside = np.linalg.norm(inner[2:] - 2 * inner[1:-1] + inner[:-2], axis=2).max()
            metrics['loopSeamJerkMetres'] = float(jerk.max())
            if jerk.max() > max(1.5 * inside, 0.002):
                fail('loop', f'{rig.names[int(jerk.argmax())]} changes speed by {jerk.max() * 100:.1f} cm/tick across '
                             f'the seam (tick {last} -> {loop_from}); at most {inside * 100:.1f} inside the loop. Key it '
                             f'with Clip.from_keys(..., cycle=({loop_from}, {last}))', ticks=[loop_from, last])

    # Frozen channels and fixed root. The hips may translate (crouch, lunge, weight shift): Unity bakes that into
    # the pose, and the fighter itself never moves. Bone lengths, scales and any joint above the hips stay fixed.
    hips = rig.index('Hips')
    if s.get('freeze_translations', True):
        moved = np.abs(clip.t[checked] - clip.t[0]).max(axis=(0, 2))
        moved[hips] = 0.0
        if moved.max() > 1e-4:  # skeleton units; float32 storage noise is ~1e-5
            fail('translations', f'{rig.names[int(np.argmax(moved))]} translation changes by up to {moved.max():.2e}; '
                                 'only the hips may translate')
    if s.get('freeze_scales', True):
        d = float(np.abs(clip.s[checked] - clip.s[0]).max())
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
            d = float(np.abs(world[checked, j] - world[0, j]).max())
            if d > 1e-5:
                fail('root', f'{rig.names[j]} (above the hips) moves; only the hips and below may animate')

    # Planted joints (grounded moves): feet must not slide while the body moves.
    planted = s.get('planted') or []
    if planted:
        tol = s.get('plant_tolerance_metres', 0.01)
        for joint in planted:
            p = positions[checked, rig.index(joint)]
            drift = np.linalg.norm(p - p[0], axis=1)
            metrics[f'{joint} drift (m)'] = float(drift.max())
            if drift.max() > tol:
                bad = checked[drift > tol]
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
        for tick in checked:
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
        off = np.array([[rig.humanoid_split(j, clip.q[tick, j])[1] for j in rig.humanoid_digits] for tick in checked])
        metrics['humanoid digit off-axis (deg)'] = float(off.max())
        bad = [(rig.names[j], float(off[:, c].max()), int(checked[off[:, c].argmax()])) for c, j in enumerate(rig.humanoid_digits)
               if off[:, c].max() > s['humanoid_degrees']]
        if bad:
            name, degrees, tick = max(bad, key=lambda row: row[1])
            fail('humanoid', f'{len(bad)} finger/thumb/toe joints turn about axes Unity cannot play, worst {name} '
                             f'{degrees:.0f} deg at tick {tick}; use fist()/set_hinge() or pose.snap_to_humanoid()',
                 joints=[row[0] for row in bad], tick=tick)

    # Spine, neck, head, collarbones, upper arms and thighs play only through their muscles: past the limits the
    # game shows a different pose (rig.humanoid_bounds; a collarbone cannot roll at all).
    limited = rig.humanoid_limited
    if limited and s.get('humanoid_limits', True):
        excess = np.array([[rig.humanoid_excess(j, clip.q[tick, j])[0] for j in limited] for tick in checked])
        metrics['humanoid limits exceeded by (deg)'] = float(excess.max())
        bad = [c for c in range(len(limited)) if excess[:, c].max() > s['humanoid_degrees']]
        if bad:
            c = max(bad, key=lambda col: excess[:, col].max())
            tick = int(checked[excess[:, c].argmax()])
            ticks = checked[excess[:, c] > s['humanoid_degrees']]
            degrees, muscle = rig.humanoid_excess(limited[c], clip.q[tick, limited[c]])
            fail('humanoid', f"{len(bad)} joints turn past what Unity's Humanoid plays, worst {rig.names[limited[c]]} "
                             f'{degrees:.0f} deg past {muscle} at tick {tick} (ticks {ticks[0]}..{ticks[-1]}); '
                             'solve() and two_hand_grip() keep to the limits, rotate() does not',
                 joints=[rig.names[limited[col]] for col in bad], tick=tick)

    # Snaps: per-tick local rotation change.
    speed = clip.angular_speed() if motion else np.empty((0, len(rig.names)))
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

    # Flips: a limb bone that rolls about its own length faster than any arm or leg turns, fast windows included. It is
    # an IK solution flip, not speed: two neighbouring keys (or ticks) were solved separately and landed on different
    # elbow and roll solutions (Sir Bonk's E1 slam: LeftArm 158 deg in one tick, 153 of them a roll; a review read it as
    # a rotation taking the long way round). Slowing it down only shows the roll for longer: the retimed flight still
    # rolled 153 deg in 3 ticks. Approved clips roll at most 37 deg in a tick and 80 in three.
    flips, rolling = [], twist_speed(clip) if motion else {}
    for span, limit in ((1, s['flip_degrees_per_tick']), (3, s['flip_degrees_per_3_ticks'])):
        for joint, rolls in (rolling if span == 1 or not motion else twist_speed(clip, span)).items():
            flips += [(int(i) + span, joint, float(rolls[i]), span) for i in np.where(rolls > limit)[0]]
    metrics['max roll about a limb bone (deg/tick)'] = max((float(r.max()) for r in rolling.values() if len(r)), default=0.0)
    if flips:
        flips.sort(key=lambda x: -x[2])
        worst = ', '.join(f"{name} {deg:.0f} deg {'at tick' if span == 1 else f'over the {span} ticks to'} {tick}"
                          for tick, name, deg, span in flips[:4])
        fail('flip', f'a limb bone rolls about its own length by more than {s["flip_degrees_per_tick"]:.0f} deg in a tick '
                     f'or {s["flip_degrees_per_3_ticks"]:.0f} in three ({worst}): an IK flip between keys solved separately, '
                     'not speed, and slowing it down only shows the roll longer. Solve each key from its neighbour '
                     '(solve(..., seed=previous_key)) and keep contacts between keys with track()/pin()/hold_grip()',
             joints=sorted({x[1] for x in flips}), ticks=sorted({x[0] for x in flips}))

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

    # Two-handed ticks: the left hand stays on the character's own grip (rig pack grip.json), turned about the handle
    # at most grip_roll_degrees. Without a measured grip it must at least be around the handle, as close to its axis as
    # the weapon hand: a hand-to-hand distance passes a hand floating beside it (Sir Bonk's E1 aim and leap held the
    # plank 10-13 cm off the handle with "both hands on the handle" targets of 28 cm between the hands).
    span = s.get('two_hands')
    grip_ticks = [int(t) for t in checked if span and span[0] <= t <= span[1]]
    if not grip_ticks:
        span = None
    if span and rig.grip and s.get('two_hands_hold', 'exact') == 'loose':
        # A loose hold (solve.handle_target): the fist on the handle beside the weapon hand, any turn about it, within
        # the hold's own slack, tilt and place plus grip_metres / grip_degrees.
        from .solve import _handle_error, handle_target
        target = handle_target(rig)
        lo, hi = target['along']
        rows = [(tick, _handle_error(clip.pose(tick), target)[1]) for tick in grip_ticks]
        off = [(t, r) for t, r in rows if r['offHandleMetres'] > target['slack'] + s['grip_metres']
               or r['tiltDegrees'] > target['tilt'] + s['grip_degrees']
               or abs(r['gapMetres']) < target['gap'] - s['grip_metres']
               or not lo - s['grip_metres'] <= r['pastHiltMetres'] <= hi + s['grip_metres']]
        metrics['left hand off the handle (m)'] = max(r['offHandleMetres'] for _, r in rows)
        metrics['left hand tilt on the handle (deg)'] = max(r['tiltDegrees'] for _, r in rows)
        if off:
            tick, worst = max(off, key=lambda row: row[1]['offHandleMetres'])
            fail('grip', f'the left hand leaves the handle on {len(off)} of ticks {span[0]}..{span[1]}, worst at tick {tick} '
                         f'({worst["offHandleMetres"] * 100:.1f} cm off its line, tilted {worst["tiltDegrees"]:.0f} deg, '
                         f'{worst["pastHiltMetres"] * 100:.0f} cm past the hilt, {abs(worst["gapMetres"]) * 100:.0f} cm along '
                         "it from the right hand); place it with two_hand_grip(..., hold='loose') and keep it with "
                         "carry(..., hold='loose') or hold_grip(..., hold='loose')", ticks=[t for t, _ in off])
    elif span and rig.grip:
        from .solve import grip_report
        misses = [(tick, grip_report(clip.pose(tick))) for tick in grip_ticks]
        off = [(t, r) for t, r in misses if r['leftHandMissMetres'] > s['grip_metres'] or r['leftHandMissDegrees'] > s['grip_degrees']
               or abs(r['leftHandRollDegrees']) > s['grip_roll_degrees']]
        metrics['left hand off the grip (m)'] = max(r['leftHandMissMetres'] for _, r in misses)
        metrics['left hand turned about the handle (deg)'] = max(abs(r['leftHandRollDegrees']) for _, r in misses)
        if off:
            tick, worst = max(off, key=lambda row: row[1]['leftHandMissMetres'])
            fail('grip', f'the left hand leaves the grip on {len(off)} of ticks {span[0]}..{span[1]}, worst at tick {tick} '
                         f'({worst["leftHandMissMetres"] * 100:.1f} cm, {worst["leftHandMissDegrees"]:.0f} deg, turned '
                         f'{worst["leftHandRollDegrees"]:.0f} deg about the handle); place it with two_hand_grip() and keep '
                         'it with hold_grip()', ticks=[t for t, _ in off])
    elif span and {'bladeHilt', 'bladeEnd'} <= set(rig.names):
        from .rig import handle_offsets
        ticks = np.array(grip_ticks)
        weapon_side = rig.side('bladeEnd')
        other = 'Left' if weapon_side == 'Right' else 'Right'
        own = float(np.median(handle_offsets(rig, world[ticks], weapon_side)[0]))
        dist, along = handle_offsets(rig, world[ticks], other)
        metrics[f'{other.lower()} hand off the handle (m)'] = float(dist.max())
        bad = ticks[(dist > own + s['grip_metres']) | (along < 0)]
        if len(bad):
            fail('grip', f"the {other.lower()} hand is up to {dist.max() * 100:.0f} cm off the handle on {len(bad)} of ticks "
                         f'{span[0]}..{span[1]} (the {weapon_side.lower()} hand holds it {own * 100:.1f} cm off its axis): '
                         'close it around the handle beyond the hilt, not beside it', ticks=bad.tolist())

    # Approved key poses (run.py --stage keys, then --keys): the owner signed them off; the motion goes between them.
    keys = s.get('keys')
    if keys:
        approved = keys['clip'].world()
        digits = set(rig.humanoid_digits)
        body = [j for j in range(len(rig.names)) if j not in digits]  # fingers may still close
        # The body may meet its key pose up to keys_ticks early or late, so overlap and drag can offset joints in time;
        # a weapon keeps its tick (its timing is the hit's).
        exact = [j for j in body if rig.names[j].startswith(('Weapon', 'blade'))]
        k = int(s.get('keys_ticks', 0))

        def off(t):
            near = positions[max(0, t - k):min(n, t + k + 1)]
            gap = np.linalg.norm(near[:, body] - approved[t, body, 3, :3][None], axis=2).min(axis=0)
            if exact:
                gap[[body.index(j) for j in exact]] = np.linalg.norm(positions[t, exact] - approved[t, exact, 3, :3], axis=1)
            return float(gap.max())

        moved = {t: off(t) for t in keys['ticks'] if t < n and t < len(approved)}
        metrics['approved keys moved (m)'] = max(moved.values(), default=0.0)
        bad = [t for t, d in moved.items() if d > s['keys_metres']]
        if bad:
            fail('keys', f"ticks {', '.join(map(str, bad))} moved up to {max(moved[t] for t in bad) * 100:.1f} cm from the "
                         'approved key poses (keys/result.json); keep those poses and change the motion between them',
                 ticks=bad)

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

    # Form: anatomy and staging defects the owner caught by eye on Sir Bonk's runs (2026-10-21), with thresholds
    # calibrated on the roster's approved clips (curves.py): a wrist folded past what wrists do, a held weapon
    # outside its hand, forearms glued to the torso while running, knees that never straighten, legs that are
    # mirror copies of each other, and curves that are plain sine waves.
    if s.get('form', True):
        form(clip, fail, warn, metrics, s, skin, loop=motion and s.get('loop_from_tick') is not None)

    # Life: the motion reads robotic when the limbs work but the body around them stays rigid or rides the hips in
    # one piece (Sir Bonk's first run, 2026-10-20: spine and neck within 2 degrees, head bobbing exactly with the
    # hips, a frozen collarbone, flat feet, the plank arm locked). Warnings, so loops and big moves get their
    # secondary motion; see the guide's "Life" section.
    if s.get('life', True) and n >= 12:
        life(clip, warn, metrics, s.get('loop_from_tick') is not None)

    # Flow: a one-shot move should never stop much of the body at once or freeze it in place (pose-to-pose
    # interpolation stops every joint that turns back at a key, on the key's tick). Thresholds sit above the
    # hand-made liveliness corpus (44 attacks on Sir Bonk, 2026-10-21; README "Flow"), so they need no reference.
    if s.get('flow', True) and s.get('loop_from_tick') is None and n >= 30:
        flow_checks(clip, warn, metrics, s)

    if motion and s.get('chatter', True) and n >= 8:
        from .curves import chatter
        for row in chatter(clip, s['fast_windows']):
            warn('chatter', f"{row['channel']} repeatedly reverses every 1-2 ticks over ticks "
                 f"{row['ticks'][0]}..{row['ticks'][-1]} (largest step {row['maxStepDegrees']:.1f} deg); "
                 'inspect for IK jitter at game speed. Intentional shaking is allowed; do not smooth the whole move',
                 **row)

    # Brief-specific numeric targets.
    for t in s.get('targets', []):
        value = measure(clip, t)
        metrics[t['name']] = value
        lo, hi = t.get('min'), t.get('max')
        if (lo is not None and value < lo) or (hi is not None and value > hi):
            bound = ' and '.join(x for x in [f'>= {lo}' if lo is not None else '', f'<= {hi}' if hi is not None else ''] if x)
            fail('target', f"{t['name']} = {value:.3f}; wanted {bound}", target=t['name'])

    return {'mode': mode, 'skipped': skipped, 'passed': not failures,
            'failures': failures, 'warnings': warnings, 'metrics': metrics}


def _runs(ticks) -> str:
    ticks = sorted(int(t) for t in ticks)
    return f'{ticks[0]}..{ticks[-1]}' if ticks else ''


ROLL_BONES = (('Arm', ('ForeArm',)), ('ForeArm', ('Hand',)), ('Hand', ('HandMiddle1', 'HandIndex1')),
              ('UpLeg', ('Leg',)), ('Leg', ('Foot',)))


def twist_speed(clip: Clip, span: int = 1) -> dict:
    """Per limb joint, the degrees it rolls about its own bone (the line to its child) from each tick to `span` ticks
    later (row i: tick i -> i + span): the twist part of that local rotation change."""
    from scipy.spatial.transform import Rotation
    rig, out = clip.rig, {}
    for side in ('Left', 'Right'):
        for bone, children in ROLL_BONES:
            child = next((side + c for c in children if side + c in rig.names), None)
            if side + bone not in rig.names or child is None or len(clip) <= span:
                continue
            axis = clip.t[0, rig.index(child)]
            axis = axis / np.linalg.norm(axis)
            q = Rotation.from_quat(clip.q[:, rig.index(side + bone)])
            d = (q[:-span].inv() * q[span:]).as_quat()
            out[side + bone] = np.degrees(2 * np.arctan2(np.abs(d[:, :3] @ axis), np.abs(d[:, 3])))
    return out


def stepping(clip: Clip) -> bool:
    """A gait: both feet travel more than 0.3 m front to back, in opposite phase (runs and walks; not spins, flips
    or flails, whose feet also travel)."""
    if not {'LeftFoot', 'RightFoot'} <= set(clip.rig.names):
        return False
    left, right = (clip.positions(f)[:, 1] for f in ('LeftFoot', 'RightFoot'))
    return min(float(np.ptp(left)), float(np.ptp(right))) > 0.3 and float(np.corrcoef(left, right)[0, 1]) < -0.5


def form(clip: Clip, fail, warn, metrics: dict, spec: dict, skin=None, loop: bool = False) -> None:
    """Anatomy and staging checks (see check_clip). Approved roster clips: wrists at most 95 degrees off the
    forearm, runs straighten their knees to 6-36 degrees, legs differ by 4-8 degrees half a cycle apart, no curve
    is a plain sine, and a forearm stays within 2 cm of the torso on at most 18% of a run. The gait checks apply to
    looping locomotion only (see stepping)."""
    from .curves import channels, harmonic_share, mirror_error
    rig, world, n = clip.rig, clip.world(), len(clip)
    names = set(rig.names)
    locomotion = loop and stepping(clip)

    # Wrists: the angle between the forearm and the hand (wrist to middle knuckle).
    for side in ('Left', 'Right'):
        knuckle = next((side + 'Hand' + f + '1' for f in ('Middle', 'Index') if side + 'Hand' + f + '1' in names), None)
        if knuckle is None or side + 'ForeArm' not in names:
            continue
        e, h, k = (world[:, rig.index(j), 3, :3] for j in (side + 'ForeArm', side + 'Hand', knuckle))
        f, g = h - e, k - h
        angle = np.degrees(np.arccos(np.clip(np.sum(f * g, 1) / (np.linalg.norm(f, axis=1) * np.linalg.norm(g, axis=1) + 1e-12), -1, 1)))
        from .brief import exempt_ranges
        for first, last in exempt_ranges(spec.get('wrist_exempt_ticks')):  # a matched entry's or exit's own fold
            angle[first:last + 1] = 0.0
        if 'pose_ticks' in spec:
            angle[np.setdiff1d(np.arange(n), spec['pose_ticks'])] = 0.0
        metrics[f'{side.lower()} wrist bend max (deg)'] = float(angle.max())
        over = np.where(angle > spec.get('wrist_fail_degrees', 105.0))[0]
        if len(over):
            fail('wrist', f'the {side.lower()} wrist folds {angle.max():.0f} deg off the forearm at ticks {_runs(over)} '
                          '(approved clips stay under 95); keep the hand in line with the forearm and turn the forearm instead',
                 joint=side + 'Hand', ticks=over.tolist())
        elif loop and angle.max() > spec.get('wrist_loop_warn_degrees', 85.0):
            warn('wrist', f'the {side.lower()} wrist bends {angle.max():.0f} deg in a loop (approved loops stay under 45)',
                 joint=side + 'Hand')

    if not spec.get('motion', True):
        return

    # Forearms glued to the torso while running (vertex gap between the forearm and hand and the spine's skin).
    if locomotion and spec.get('arm_gap', True):
        from scipy.spatial import cKDTree
        from .skin import Skin
        skin = skin or Skin(rig)
        spine = [rig.index(j) for j in ('Hips', 'Spine', 'Spine1', 'Spine2', 'Chest', 'UpperChest', 'Neck') if j in names]
        for side in ('Left', 'Right'):
            limb_joints = [rig.index(j) for j in (side + 'ForeArm', side + 'Hand') if j in names]
            gaps = []
            for tick in range(0, n, 2):
                pts = skin.points(world[tick])
                torso = np.concatenate([p[np.isin(m['dominant'], spine)] for p, m in zip(pts, skin.meshes)])
                limb = np.concatenate([p[np.isin(m['dominant'], limb_joints)] for p, m in zip(pts, skin.meshes)])
                if len(torso) and len(limb):
                    gaps.append(float(cKDTree(torso).query(limb[::3])[0].min()))
            if not gaps:
                continue
            gaps = np.array(gaps)
            share = float(np.mean(gaps < spec.get('arm_gap_metres', 0.02)))
            metrics[f'{side.lower()} forearm on the torso (share of ticks)'] = share
            if share > spec.get('arm_gap_share', 0.5):
                warn('arms', f'the {side.lower()} forearm stays within 2 cm of the torso on {share * 100:.0f}% of the '
                             f'cycle (median gap {np.median(gaps) * 100:.1f} cm; roster runs at most 18%); hold it away '
                             'from the body', joint=side + 'ForeArm')

    ch = channels(clip)
    knees = [ch[k] for k in ('Left knee bend', 'Right knee bend') if k in ch]
    if locomotion and knees:
        least = float(min(k.min() for k in knees))
        metrics['knee bend min (deg)'] = least
        if least > spec.get('knee_straighten_degrees', 40.0):
            warn('legs', f'the knees never straighten (least bend {least:.0f} deg; the roster runs reach 6-36): push off '
                         'and reach into each landing on a nearly straight leg')
    if loop and locomotion:
        pairs = [mirror_error(ch['Left ' + c], ch['Right ' + c]) for c in ('knee bend', 'thigh swing')
                 if 'Left ' + c in ch and 'Right ' + c in ch]
        if pairs:
            metrics['legs mirror error (deg)'] = float(np.mean(pairs))
            if np.mean(pairs) < spec.get('mirror_degrees', 2.5):
                warn('life', f'the legs are mirror copies (within {np.mean(pairs):.1f} deg half a cycle apart; the roster '
                             'runs differ by 4-8): give the two steps different timing, height or weight')
    swings = [float(np.ptp(v)) for name, v in ch.items() if 'swing' in name]
    if loop and (locomotion or (swings and max(swings) > 20)):  # a breathing idle is allowed its sine
        body = ('knee', 'thigh', 'elbow', 'upper-arm', 'Pelvis', 'Chest', 'Spine')  # wrists and feet may ride a sine
        sines = [name for name, v in ch.items() if any(b in name for b in body) and np.ptp(v) > 4 and harmonic_share(v) < 0.002]
        metrics['plain-sine curves'] = float(len(sines))
        if len(sines) >= 2:
            warn('life', f'{", ".join(sines)} move as plain sine waves: vary the timing and spacing (sharper landings, '
                         'slower recoveries, a hold) instead of one smooth wave per joint', joints=sines)


def _swing(clip: Clip, joint: str) -> float:
    """Largest local rotation away from the joint's mean rotation over the clip, in degrees."""
    from scipy.spatial.transform import Rotation
    rots = Rotation.from_quat(clip.q[:, clip.rig.index(joint)])
    return float(np.degrees((rots.mean().inv() * rots).magnitude()).max())


def life(clip: Clip, warn, metrics: dict, loop: bool) -> None:
    """Secondary-motion warnings: a rigid torso under working limbs, a head that rides the hips' bob, a frozen
    collarbone under a swinging arm, a stepping foot that never rolls, and an arm frozen while the body bounces."""
    names = clip.rig.names
    gait = loop and stepping(clip)
    have = [j for j in ('Spine', 'Spine1', 'Spine2', 'Chest', 'UpperChest', 'Neck') if j in names]
    limbs = {j: _swing(clip, j) for j in ('LeftArm', 'RightArm', 'LeftUpLeg', 'RightUpLeg') if j in names}
    torso = {j: _swing(clip, j) for j in have}
    if torso:
        metrics['life: torso swing (deg)'] = max(torso.values())
        if limbs and max(limbs.values()) > 30 and max(torso.values()) < 2.5:
            warn('life', f'rigid torso: the spine and neck turn at most {max(torso.values()):.1f} deg while the limbs swing '
                         f'up to {max(limbs.values()):.0f}; let the pelvis twist and tilt with the steps and the chest '
                         'counter-rotate (3-10 deg)', joints=list(torso))
    if 'Hips' in names and 'Head' in names:
        hips, head = clip.positions('Hips')[:, 2], clip.positions('Head')[:, 2]
        if np.ptp(hips) > 0.04:  # the roster's runs move the head 41-81% of the hips' bob against them
            ratio = float(np.ptp(head - hips) / np.ptp(hips))
            metrics['life: head bob against the hips / hips bob'] = ratio
            if ratio < 0.25:
                warn('life', f'rigid bob: the head rides the hips, rising and falling {np.ptp(head) * 100:.1f} cm with '
                             f'their {np.ptp(hips) * 100:.1f} cm and only {np.ptp(head - hips) * 100:.1f} cm against '
                             'them; let the spine flex on the landings so the head moves less and a few ticks later')
    for side in ('Left', 'Right'):
        arm, shoulder = side + 'Arm', side + 'Shoulder'
        if arm in limbs and shoulder in names:
            moved = _swing(clip, shoulder)
            if limbs[arm] > 25 and moved < 1.5:
                warn('life', f'frozen collarbone: {shoulder} turns {moved:.1f} deg while {arm} swings {limbs[arm]:.0f}; '
                             'let the shoulder lift and roll with the arm (3-8 deg)', joint=shoulder)
        foot, toe = side + 'Foot', side + 'ToeBase'
        if foot in names and toe in names:
            travel = float(np.ptp(clip.positions(foot)[:, 1]))
            roll = _swing(clip, toe)
            if gait and roll < 4:
                warn('life', f'flat foot: {foot} travels {travel:.2f} m but {toe} bends {roll:.1f} deg; roll the foot '
                             'heel to toe and bend the toes at push-off (20-40 deg)', joint=toe)
        chain = [j for j in (side + 'Arm', side + 'ForeArm', side + 'Hand') if j in names]
        if chain and 'Hips' in names and np.ptp(clip.positions('Hips')[:, 2]) > 0.05:
            most = max(_swing(clip, j) for j in chain)
            if most < 5:
                warn('life', f'frozen arm: the {side.lower()} arm turns at most {most:.1f} deg while the body bounces; '
                             'let it (and anything it holds) lag and settle a few ticks behind the body (Clip.overlap)',
                     joints=chain)


def flow_checks(clip: Clip, warn, metrics: dict, spec: dict) -> None:
    """Stops, the body stopping together and frozen joints (curves.flow), between a matched entry and the exit hold."""
    from .curves import flow, flow_window
    window = flow_window(len(clip), hold_from=spec.get('exit_from_tick'))
    stats = flow(clip, window)
    metrics.update({f'flow: {k}': v for k, v in stats.items()})
    stops, together = stats['stops per joint per second'], stats['most of the body stopping together (share)']
    frozen = stats['frozen joint-ticks (share)']
    if stops > spec['flow_stops_per_second']:
        warn('flow', f'pose-to-pose stops: joints halt {stops:.2f} times a second each (hand-made attacks 0.05-1.16, nine '
                     'in ten under 0.62); stagger the arrivals (each body part reaches a big pose 1-3 ticks apart) and '
                     'key holds as moving holds',
             window=list(window))
    if together > spec['flow_together_share']:
        warn('flow', f'the body stops together: {together * 100:.0f}% of the joints halt within the same three ticks '
                     '(hand-made attacks at most 27% in nine clips out of ten); the hips arrive first, the chest a tick '
                     'later, the arms and head trail (key them at their own ticks or Clip.overlap the joints that carry no '
                     'contact)', window=list(window))
    if frozen > spec['flow_frozen_share']:
        warn('flow', f'frozen body: {frozen * 100:.0f}% of the joint-ticks barely move (hand-made attacks at most 12%); '
                     'make holds moving holds (key the end of a hold 2-5 degrees on from its start, never the same pose '
                     'twice) and let the settle give', window=list(window))


def measure(clip: Clip, t: dict) -> float:
    """Evaluate one target. Kinds (char-frame metres, ticks):
    distance: |a - b| at tick           ahead: a ahead of b (front axis) at tick
    below: b above a (up axis) at tick  char: component ('right'|'front'|'up') of a at tick
    fastest_drop: tick ending a's fastest downward step within [first, last]
    range: how far a travels along an axis ('right'|'front'|'up') within window [first, last]
    arc: how many defender positions a weapon normal's capsule touches over window [first, last] (hitarc.py: the
      report's Arc grid, behind to ahead and 0-3 m up), with `radius` (move data), `report` (a HitReliabilityReport
      JSON with the defenders' volumes, repo-relative), `defender` ('FightGuy' or 'Manki'), `side` ('all', 'ahead'
      or 'behind') and `calibration` (hitarc.CALIBRATION, e.g. 'bonk')
    """
    kind, tick = t['kind'], t.get('tick')
    a = t.get('a')
    b = t.get('b')
    if kind == 'range':
        first, last = t.get('window', [0, len(clip) - 1])
        return float(np.ptp(clip.positions(a)[first:last + 1, {'right': 0, 'front': 1, 'up': 2}[t['axis']]]))
    if kind == 'arc':
        from .hitarc import CALIBRATION, arc_cells, arc_grid
        repo = Path(__file__).resolve().parents[2]
        grid = arc_grid(clip, t['window'], t['radius'], repo / t['report'], t.get('defender', 'FightGuy'),
                        CALIBRATION.get(t.get('calibration')))
        return float(arc_cells(grid, t.get('side', 'all')))
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
    if report.get('checkpointOnly'):
        lines[0] += ' (starting checkpoint only; no authored changes)'
    if report.get('stage') == 'keys':
        lines[0] = 'KEY POSES ' + lines[0] + ' (blocking only)'
    if report.get('mode') == 'fast':
        lines[0] = 'FAST ' + lines[0] + ' (iteration only)'
        lines.append('  skipped: ' + ', '.join(report['skipped']) + '; run full checks before review or delivery')
    lines += [f"  x {f['check']}: {f['message']}" for f in report['failures']]
    lines += [f"  ! {w['check']}: {w['message']}" for w in report['warnings']]
    if report.get('transitionRisks'):
        lines.append('  Interpolated motion risks (deferred to the motion stage):')
        lines += [f"  ~ {f['check']}: {f['message']}" for f in report['transitionRisks']]
    m = report['metrics']
    lines.append(f"  max {m.get('maxDegreesPerTick', 0):.0f} deg/tick; hip travel {m.get('hipTravelMetres', 0) * 1000:.2f} mm")
    for k, v in m.items():
        if k not in {'maxDegreesPerTick', 'hipTravelMetres', 'closestMetres', 'entryErrorMetres', 'exitErrorMetres'}:
            lines.append(f'  {k}: {v:.3f}')
    return '\n'.join(lines)
