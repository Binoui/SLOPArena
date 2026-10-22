"""Joint-angle curves: the animator's curve editor for a clip, plus the motion statistics behind the checks.

channels(clip) measures named per-tick series in the character frame (degrees unless the name ends in "(m)"): knee
and elbow bend, thigh swing and the upper arm's angle out of the side plane (both forward +), the wrist's angle off the
forearm (what the wrist check measures), foot pitch (toes down +), pelvis turn and tilt, the chest's turn against the
pelvis, spine lean, and the heights of the hips, head and toes. Angles are unwrapped, so a swing past straight up does
not jump by 360. curves_sheet() draws them as small multiples, left and right overlaid, with an optional reference clip
in grey (time-normalised, so a 44-tick run reads against a 32-tick one).
harmonic_share() and mirror_error() are the statistics behind the plain-sine and mirror-copy checks.
"""
from __future__ import annotations

import numpy as np
from PIL import Image, ImageDraw

from .render import _font

PANELS = (
    ('Knee bend', ('Left knee bend', 'Right knee bend')),
    ('Thigh swing (forward +)', ('Left thigh swing', 'Right thigh swing')),
    ('Elbow bend', ('Left elbow bend', 'Right elbow bend')),
    ('Upper-arm swing (forward of the side plane +)', ('Left upper-arm swing', 'Right upper-arm swing')),
    ('Wrist angle off the forearm', ('Left wrist angle', 'Right wrist angle')),
    ('Foot pitch (toes down +)', ('Left foot pitch', 'Right foot pitch')),
    ('Pelvis and chest', ('Pelvis turn', 'Pelvis tilt', 'Chest turn vs pelvis', 'Spine lean')),
    ('Heights (m)', ('Hips height (m)', 'Head height (m)', 'Left toe height (m)', 'Right toe height (m)')),
)
LINE_COLORS = [(42, 120, 214), (235, 104, 52), (27, 175, 122), (237, 161, 0)]  # validated categorical slots 1-4


def _angle(v, w):
    return np.degrees(np.unwrap(np.arctan2(v, w)))


def channels(clip) -> dict[str, np.ndarray]:
    """Named per-tick series (see the module docstring). Joints a rig lacks are skipped."""
    rig, world = clip.rig, clip.world()
    names = set(rig.names)
    pos = {j: clip.positions(j) for j in names}
    out = {}
    for side in ('Left', 'Right'):
        for hinge, label in ((side + 'Knee', 'knee bend'), (side + 'Elbow', 'elbow bend')):
            if hinge in rig.hinges:
                out[f'{side} {label}'] = np.array([rig.hinge_state(world[t], hinge)[0] for t in range(len(clip))])
        if side + 'UpLeg' in names and side + 'Leg' in names:
            v = pos[side + 'Leg'] - pos[side + 'UpLeg']
            out[f'{side} thigh swing'] = _angle(v[:, 1], -v[:, 2])
        if side + 'Arm' in names and side + 'ForeArm' in names:  # out of the side plane: an arm held out sideways (a
            v = pos[side + 'ForeArm'] - pos[side + 'Arm']       # big character's elbows) has no stable angle within it
            out[f'{side} upper-arm swing'] = np.degrees(np.arcsin(np.clip(v[:, 1] / np.linalg.norm(v, axis=1), -1, 1)))
        hand, elbow, knuckle = side + 'Hand', side + 'ForeArm', next(
            (side + 'Hand' + f + '1' for f in ('Middle', 'Index') if side + 'Hand' + f + '1' in names), None)
        if knuckle and hand in names and elbow in names:
            f, h = pos[hand] - pos[elbow], pos[knuckle] - pos[hand]
            cos = np.sum(f * h, 1) / (np.linalg.norm(f, axis=1) * np.linalg.norm(h, axis=1) + 1e-12)
            out[f'{side} wrist angle'] = np.degrees(np.arccos(np.clip(cos, -1, 1)))
        foot, toe = side + 'Foot', side + 'ToeBase'
        if foot in names and toe in names:
            v = pos[toe] - pos[foot]
            out[f'{side} foot pitch'] = _angle(-v[:, 2], np.hypot(v[:, 0], v[:, 1]))
    if {'LeftUpLeg', 'RightUpLeg'} <= names:
        r = pos['RightUpLeg'] - pos['LeftUpLeg']
        out['Pelvis turn'] = _angle(r[:, 1], r[:, 0])
        out['Pelvis tilt'] = _angle(r[:, 2], r[:, 0])
        if {'LeftArm', 'RightArm'} <= names:
            c = pos['RightArm'] - pos['LeftArm']
            out['Chest turn vs pelvis'] = _angle(c[:, 1], c[:, 0]) - out['Pelvis turn']
    if {'Hips', 'Spine2'} <= names:
        s = pos['Spine2'] - pos['Hips']
        out['Spine lean'] = _angle(s[:, 1], s[:, 2])
    for joint, label in (('Hips', 'Hips height (m)'), ('Head', 'Head height (m)'),
                         ('LeftToeBase', 'Left toe height (m)'), ('RightToeBase', 'Right toe height (m)')):
        if joint in names:
            out[label] = pos[joint][:, 2]
    return out


def _resample(values, n):
    if len(values) == n:
        return values
    return np.interp(np.linspace(0, 1, n), np.linspace(0, 1, len(values)), values)


def harmonic_share(values, keep=2) -> float:
    """Share of a loop's variation beyond its first `keep` harmonics (0 = plain sines, 1 = all detail)."""
    v = np.asarray(values, float)[:-1] if len(values) > 2 else np.asarray(values, float)
    spectrum = np.abs(np.fft.rfft(v - v.mean())) ** 2
    total = spectrum[1:].sum()
    return float(spectrum[keep + 1:].sum() / total) if total > 1e-9 else 0.0


def mirror_error(left, right) -> float:
    """Mean degrees between one side and the other side half a loop later (0 = exact mirror copies).

    Each curve's mean is removed first: a constant offset between the legs (a stance) is not a different motion.
    """
    a, b = np.asarray(left, float)[:-1], np.asarray(right, float)[:-1]
    a, b = a - a.mean(), b - b.mean()
    return float(np.abs(np.roll(a, len(a) // 2) - b).mean())


def chatter(clip, fast_windows=()) -> list[dict]:
    """Advisory bursts of substantial angle reversals every one or two ticks.

    A single snap, smooth step or small numerical noise is not chatter. Annotate
    fast windows; they may allow the body's spin while its legs still chatter.
    """
    fast = {t for a, b in fast_windows for t in range(a, b + 1)}
    rows = []
    for name, values in channels(clip).items():
        if not any(label in name for label in ('knee bend', 'elbow bend', 'foot pitch', 'wrist angle')):
            continue
        delta = np.diff(values)
        reversals = np.flatnonzero((delta[:-1] * delta[1:] < 0)
                                  & (np.abs(delta[:-1]) >= 3) & (np.abs(delta[1:]) >= 3)) + 1
        for group in np.split(reversals, np.flatnonzero(np.diff(reversals) > 2) + 1):
            if len(group) < 4:
                continue
            first, last = int(group[0]), int(group[-1])
            rows.append({'channel': name, 'ticks': [first, last], 'reversals': int(len(group)),
                         'maxStepDegrees': float(np.abs(delta[first - 1:last + 1]).max()),
                         'inFastWindow': any(t in fast for t in range(first, last + 1))})
    return rows


def curves_sheet(clip, path=None, reference=None, title=None, size=(1500, 860)) -> Image.Image:
    """Small multiples of channels(clip); `reference` (another Clip) is drawn in grey under it, time-normalised."""
    ch = channels(clip)
    ref = channels(reference) if reference is not None else {}
    W, H = size
    surface, ink, muted, grid = (252, 252, 251), (32, 33, 36), (140, 142, 148), (226, 226, 222)
    img = Image.new('RGB', (W, H), surface)
    d = ImageDraw.Draw(img)
    bold, small = _font(17), _font(12)
    d.text((16, 10), title or 'Joint curves (grey: reference, time-normalised)', font=bold, fill=ink)
    cols, rows, gap = 4, 2, 80
    pw, ph = (W - 70 - (cols - 1) * gap) // cols, (H - 70) // rows - 60
    n = len(clip)
    for k, (panel, names) in enumerate(PANELS):
        x0 = 60 + (k % cols) * (pw + gap)
        y0 = 60 + (k // cols) * (ph + 60)
        series = [(name, ch[name]) for name in names if name in ch]
        refs = [ref[name] for name in names if name in ref]
        if not series:
            continue
        everything = [v for _, v in series] + refs
        lo = min(float(np.min(v)) for v in everything)
        hi = max(float(np.max(v)) for v in everything)
        pad = max((hi - lo) * 0.08, 0.02 if panel.endswith('(m)') else 2)
        lo, hi = lo - pad, hi + pad
        d.text((x0, y0 - 22), panel, font=_font(14), fill=ink)

        def X(i, count):
            return x0 + i / max(count - 1, 1) * pw

        def Y(v):
            return y0 + ph - (v - lo) / (hi - lo) * ph
        for frac in (0, 0.5, 1):
            v = lo + frac * (hi - lo)
            d.line((x0, Y(v), x0 + pw, Y(v)), fill=grid)
            text = f'{v:.2f}' if panel.endswith('(m)') else f'{v:.0f}°'
            d.text((x0 - 6 - d.textlength(text, font=small), Y(v) - 7), text, font=small, fill=muted)
        for values in refs:
            values = _resample(values, n)
            pts = [(X(i, n), Y(v)) for i, v in enumerate(values)]
            d.line(pts, fill=(196, 198, 204), width=2)
        for c, (name, values) in enumerate(series):
            pts = [(X(i, n), Y(v)) for i, v in enumerate(values)]
            color = LINE_COLORS[c % len(LINE_COLORS)]
            if c % 2 == 1 and len(series) == 2:  # right side dashed: identity is not colour alone
                for i in range(0, len(pts) - 1, 2):
                    d.line((pts[i], pts[i + 1]), fill=color, width=2)
            else:
                d.line(pts, fill=color, width=2)
        for c, (name, _) in enumerate(series):
            d.text((x0 + 4 + (c % 2) * (pw // 2), y0 + ph + 6 + (c // 2) * 14), name, font=small,
                   fill=LINE_COLORS[c % len(LINE_COLORS)])
    if path:
        img.save(path)
    return img


# Flow: how the body moves between poses. Hand-made one-shot attacks (the liveliness corpus, 2026-10-21) rarely stop a
# joint and almost never stop much of the body at once; pose-to-pose interpolation stops every joint that turns back at
# a key, all on the key's tick. Sir Bonk's first GenRA Squire's Cut stopped 55% of the body together and read stiff next
# to the hand-made clip it replaced, though it was less jerky.
FLOW_JOINTS = ('Hips', 'Spine', 'Spine1', 'Spine2', 'Neck', 'Head', 'LeftShoulder', 'LeftArm', 'LeftForeArm', 'LeftHand',
               'RightShoulder', 'RightArm', 'RightForeArm', 'RightHand', 'LeftUpLeg', 'LeftLeg', 'LeftFoot', 'RightUpLeg',
               'RightLeg', 'RightFoot')
FLOW_CHAINS = (('Hips', 'Spine', 'Spine1', 'Spine2', 'Neck', 'Head'),
               ('Spine2', 'LeftShoulder', 'LeftArm', 'LeftForeArm', 'LeftHand'),
               ('Spine2', 'RightShoulder', 'RightArm', 'RightForeArm', 'RightHand'))
STOP_DEGREES = 0.35     # a joint turning slower than this (deg/tick) at a local minimum of its speed has stopped...
MOVING_DEGREES = 1.5    # ...when it moves faster than this within 4 ticks either side
FROZEN_DEGREES = 0.15   # a joint turning slower than this on a tick is frozen


def flow_window(n: int, entry=3, hold_from=None) -> tuple[int, int]:
    """The speed ticks flow is judged on: after a matched entry's first ticks, up to the exit hold (or 6 from the end)."""
    last = (hold_from - 1) if hold_from is not None else n - 7
    return entry, max(entry + 1, min(last, n - 2))


def flow(clip, window=None) -> dict:
    """Flow statistics over speed ticks [first, last]: stops per joint per second, the largest share of the joints that
    stop within the same three ticks, the share of frozen joint-ticks, and how many ticks a child joint's speed trails
    its parent's along the spine and arms (overlap; 0 means the whole chain moves as one)."""
    rig = clip.rig
    joints = [j for j in FLOW_JOINTS if j in rig.names]
    speed = clip.angular_speed()[:, [rig.index(j) for j in joints]]
    first, last = window or flow_window(len(clip))
    s = speed[first:last + 1]
    stops = np.zeros_like(s, bool)
    for j in range(s.shape[1]):
        v = speed[:, j]
        for t in range(first, last + 1):
            if 0 < t < len(v) - 1 and v[t] <= v[t - 1] and v[t] <= v[t + 1] and v[t] < STOP_DEGREES \
                    and v[max(0, t - 4):t + 5].max() > MOVING_DEGREES:
                stops[t - first, j] = True
    seconds = max(len(s), 1) / 60
    together = max((float(stops[max(0, t - 1):t + 2].any(axis=0).mean()) for t in range(len(s))), default=0.0)
    lags = []
    for chain in FLOW_CHAINS:
        present = [j for j in chain if j in joints]
        for parent, child in zip(present, present[1:]):
            a, b = s[:, joints.index(parent)], s[:, joints.index(child)]
            a, b = a - a.mean(), b - b.mean()
            if a.std() < 0.2 or b.std() < 0.2:
                continue
            scores = {k: float(np.dot(a[:len(a) - k] if k > 0 else a[-k:], b[k:] if k > 0 else b[:len(b) + k]))
                      for k in range(-4, 5)}
            lags.append(max(scores, key=scores.get))
    return {'stops per joint per second': float(stops.sum() / len(joints) / seconds),
            'most of the body stopping together (share)': together,
            'frozen joint-ticks (share)': float(np.mean(s < FROZEN_DEGREES)),
            'child trails parent (ticks)': float(np.mean(lags)) if lags else 0.0}


def speed_map(clip, path=None, reference=None, title=None, window=None, cell=11) -> Image.Image:
    """Each joint's turning speed per tick: bright moving, dark red stopped. A pose-to-pose clip shows dark columns where
    the whole body halts on a key; hand-made motion stays lit. An optional reference clip (time-normalised to the clip's
    length) is drawn below in the same scale."""
    font, small = _font(15), _font(12)
    left, row = 118, cell + 2

    def panel(c, heading):
        rig = c.rig
        joints = [j for j in FLOW_JOINTS if j in rig.names]
        sp = c.angular_speed()[:, [rig.index(j) for j in joints]]
        image = Image.new('RGB', (left + cell * sp.shape[0] + 12, 30 + row * len(joints) + 22), (21, 23, 28))
        draw = ImageDraw.Draw(image)
        draw.text((6, 6), heading, font=font, fill=(235, 231, 223))
        for j, name in enumerate(joints):
            draw.text((6, 30 + j * row), name, font=small, fill=(167, 172, 180))
            for t in range(sp.shape[0]):
                v = min(1.0, sp[t, j] / 6.0)
                colour = (60, 30, 30) if sp[t, j] < STOP_DEGREES else (int(40 + 215 * v), int(60 + 150 * v), int(90 + 60 * v))
                x, y = left + t * cell, 30 + j * row
                draw.rectangle([x, y, x + cell - 2, y + row - 3], fill=colour)
        for t in range(0, sp.shape[0] + 1, 10):
            draw.text((left + t * cell - 4, 30 + row * len(joints) + 4), f't{t}', font=small, fill=(167, 172, 180))
        if window is not None and c is clip:
            for t in window:
                draw.line([left + t * cell - 1, 26, left + t * cell - 1, 30 + row * len(joints)], fill=(255, 210, 63))
        return image

    panels = [panel(clip, title or 'Joint speed per tick: dark red = stopped, bright = turning fast')]
    if reference is not None:
        ref = reference.retime([(0, 0), (len(reference) - 1, len(clip) - 1)]) if len(reference) != len(clip) else reference
        panels.append(panel(ref, 'Reference, time-normalised to the same ticks'))
    sheet = Image.new('RGB', (max(p.width for p in panels), sum(p.height for p in panels) + 8 * (len(panels) - 1)), (21, 23, 28))
    y = 0
    for p in panels:
        sheet.paste(p, (0, y))
        y += p.height + 8
    if path:
        sheet.save(path)
    return sheet
