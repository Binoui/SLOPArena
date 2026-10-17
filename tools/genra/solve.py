"""Inverse kinematics that respects anatomy.

two_bone: analytic limb IK; the middle joint only bends about its anatomical hinge.
solve: least-squares targets over chosen joints; hinge joints get one degree of freedom.
fist: closes the fingers toward the palm and wraps the thumb.
Targets and directions are in the character frame (right, front, up) unless noted.
"""
from __future__ import annotations

import numpy as np
from scipy.optimize import brentq, least_squares
from scipy.spatial.transform import Rotation

from .clip import Pose
from .rig import FINGERS, rotation_part, unit

LIMBS = {'Leg': ('UpLeg', 'Leg', 'Foot', 'Knee'), 'Arm': ('Arm', 'ForeArm', 'Hand', 'Elbow')}


def hinge_for(rig, middle) -> str | None:
    m = rig.index(middle)
    return next((name for name, h in rig.hinges.items() if h[2] == m), None)


def two_bone(pose: Pose, side: str, limb: str, target, pole=None, keep_end_orientation=True) -> float:
    """Place the end of a limb ('Arm' -> hand, 'Leg' -> foot) at `target` (char frame).

    Edits `pose` in place and returns the remaining miss in metres (non-zero when
    the target is out of reach: raise the shoulder/spine or move the target).
    The elbow/knee bends only about its anatomical hinge. `pole` (char direction) is
    where the elbow/knee should point; by default the current bend plane is kept.
    """
    upper, middle, end, hinge = (side + x for x in LIMBS[limb])
    rig = pose.rig
    a, m, e = rig.index(upper), rig.index(middle), rig.index(end)
    end_world_rot = rotation_part(pose.world()[e])
    root = pose.world()[a, 3, :3]
    goal = pose.frame.to_world(target)
    d = np.linalg.norm(goal - root)

    def reach(phi):  # upper-to-end distance depends only on the hinge angle
        trial = pose.copy().set_hinge(hinge, phi).world()
        return np.linalg.norm(trial[e, 3, :3] - trial[a, 3, :3]) - d

    lo, hi = 0.0, 170.0
    if reach(lo) <= 0:
        phi = lo  # target beyond the straight limb: reach toward it
    elif reach(hi) >= 0:
        phi = hi  # target closer than the deepest fold
    else:
        phi = brentq(reach, lo, hi, xtol=1e-7)
    pose.set_hinge(hinge, phi)
    world = pose.world()
    current = world[e, 3, :3] - root
    rot, _ = Rotation.align_vectors([goal - root], [current])
    pose._apply_world_delta(a, rot.as_matrix())
    if pole is not None:
        world = pose.world()
        axis = unit(goal - root)
        knee = world[m, 3, :3] - root
        want = pose.frame.dir_to_world(pole)
        k = knee - axis * (knee @ axis)
        w = want - axis * (want @ axis)
        if np.linalg.norm(k) > 1e-6 and np.linalg.norm(w) > 1e-6:
            k, w = unit(k), unit(w)
            angle = np.arctan2(np.cross(k, w) @ axis, k @ w)
            pose._apply_world_delta(a, Rotation.from_rotvec(angle * axis).as_matrix())
    if keep_end_orientation:
        _set_world_rotation(pose, e, end_world_rot)
    return float(np.linalg.norm(pose.world()[e, 3, :3] - goal))


def _set_world_rotation(pose: Pose, j: int, world_rot_row):
    rig = pose.rig
    parent = rotation_part(pose.world()[rig.parents[j]]) if rig.parents[j] >= 0 else rotation_part(rig.skel_to_world)
    pose.q[j] = Rotation.from_matrix((world_rot_row @ parent.T).T).as_quat()
    pose._changed()


def solve(pose: Pose, joints, targets, reg=0.02, hinges=True, max_nfev=200, humanoid=True):
    """Least-squares IK. Returns (new_pose, report).

    joints: names to vary. Hinge joints (knee/elbow/finger middles) vary only about
    their anatomical axis when hinges=True; others get a rotation in their parent frame.
    targets: list of dicts, each with 'weight' (default 1) and one of
      {'pos': joint, 'at': [r, f, u]}                  joint position (char metres)
      {'dir': [joint_a, joint_b], 'along': [r, f, u]}  segment a->b direction
      {'axis': joint, 'local': [x, y, z], 'along': [r, f, u]}  a joint-local axis
      {'palm': 'Left'|'Right', 'along': [r, f, u]}     palm normal direction
    reg pulls each varied joint toward its starting rotation (radians).
    humanoid: spine, neck, head, collarbone, upper-arm and thigh joints vary only through their Unity Humanoid
    muscles, within the limits the game can play (rig.humanoid_limited, rig.humanoid_bounds); one that starts
    past them never goes further out.
    """
    rig = pose.rig
    idx = [rig.index(j) for j in joints]
    hinge_axis = {}
    if hinges:
        for name, (_, a, m, d, axis) in rig.hinges.items():
            if m in idx:
                hinge_axis[m] = axis
    start = pose.q.copy()
    muscles = {}  # joint -> (axes, starting angles, lower, upper, rotation at zero)
    if humanoid:
        for j in idx:
            if j not in hinge_axis and j in rig.humanoid_limited:
                axes, low, high = rig.humanoid_bounds(j)
                muscles[j] = (axes, rig.humanoid_split(j, start[j])[0], low, high, rig.humanoid[j][0]['zero'])
    sizes = [1 if j in hinge_axis else len(muscles[j][0]) if j in muscles else 3 for j in idx]
    # Hinges stay within their anatomical range (no hyperextension), measured from the starting flexion.
    lower, upper = [], []
    for j, n in zip(idx, sizes):
        if n == 1:
            name = hinge_for(rig, rig.names[j])
            angle, align = rig.hinge_state(pose.world(), name)
            flexion = np.radians(angle if align >= 0 else -angle)
            low, high = np.radians(HINGE_RANGE[rig.hinges[name][0]])
            lower.append(min(low - flexion, -1e-6))
            upper.append(max(high - flexion, 1e-6))
        elif j in muscles:  # never further out than the start
            _, angles, low, high, _ = muscles[j]
            lower += list(np.minimum(low - angles, -1e-6))
            upper += list(np.maximum(high - angles, 1e-6))
        else:
            lower += [-np.inf] * 3
            upper += [np.inf] * 3

    def apply(x):
        p = pose.copy()
        k = 0
        for j, n in zip(idx, sizes):
            if j in muscles:
                axes, angles, _, _, zero = muscles[j]
                p.q[j] = (Rotation.from_rotvec(axes.T @ (angles + x[k:k + n])) * zero).as_quat()
            else:
                delta = Rotation.from_rotvec(x[k] * hinge_axis[j]) if n == 1 else Rotation.from_rotvec(x[k:k + 3])
                p.q[j] = (delta * Rotation.from_quat(start[j])).as_quat()
            k += n
        p._changed()
        return p

    def residual(x):
        p = apply(x)
        out = []
        for t in targets:
            w = t.get('weight', 1.0)
            if 'pos' in t:
                out += list(w * (p.pos(t['pos']) - np.asarray(t['at'], float)))
            elif 'dir' in t:
                a, b = t['dir']
                out += list(w * (unit(p.pos(b) - p.pos(a)) - unit(t['along'])))
            elif 'axis' in t:
                out += list(w * (unit(p.axis(t['axis'], t['local'])) - unit(t['along'])))
            elif 'palm' in t:
                out += list(w * (unit(p.palm_normal(t['palm'])) - unit(t['along'])))
            elif 'attach' in t:
                out += list(w * _attach_error(p, t)[0])
            else:
                raise ValueError(f'Unknown target {t}')
        out += list(reg * x)
        return np.asarray(out)

    result = least_squares(residual, np.zeros(sum(sizes)), bounds=(lower, upper), max_nfev=max_nfev)
    best = apply(result.x)
    report = []
    for t in targets:
        if 'pos' in t:
            report.append({'pos': t['pos'], 'errorMetres': float(np.linalg.norm(best.pos(t['pos']) - np.asarray(t['at'], float)))})
        elif 'dir' in t:
            report.append({'dir': t['dir'], 'errorDegrees': _angle(best.pos(t['dir'][1]) - best.pos(t['dir'][0]), t['along'])})
        elif 'axis' in t:
            report.append({'axis': t['axis'], 'errorDegrees': _angle(best.axis(t['axis'], t['local']), t['along'])})
        elif 'attach' in t:
            _, metres, degrees = _attach_error(best, t)
            report.append({'attach': t['attach'], 'to': t['to'], 'errorMetres': metres, 'errorDegrees': degrees})
        else:
            report.append({'palm': t['palm'], 'errorDegrees': _angle(best.palm_normal(t['palm']), t['along'])})
    return best, report


HINGE_RANGE = {'knee': (-5.0, 160.0), 'elbow': (-5.0, 160.0), 'finger': (-15.0, 110.0)}  # degrees of flexion


def _attach_error(pose: Pose, target, lever=0.1):
    """Residual (position, then two axes x lever) of joint `attach` against its fixed transform `local` in the
    frame of joint `to` (row convention: attach = local @ to), with the miss in metres and degrees."""
    rig, world = pose.rig, pose.world()
    goal = np.asarray(target['local'], float) @ world[rig.index(target['to'])]
    now = world[rig.index(target['attach'])]
    a, b = rotation_part(now), rotation_part(goal)
    residual = np.concatenate([now[3, :3] - goal[3, :3], lever * (a[0] - b[0]), lever * (a[1] - b[1])])
    degrees = float(np.degrees(Rotation.from_matrix(a.T @ b).magnitude()))
    return residual, float(np.linalg.norm(now[3, :3] - goal[3, :3])), degrees


GRIP_ARMS = [side + joint for side in ('Right', 'Left') for joint in ('Shoulder', 'Arm', 'ForeArm', 'Hand')]


def two_hand_grip(pose: Pose, blade=None, hilt=None, hilt_weight=0.3, spine=True, reg=0.02) -> tuple[Pose, dict]:
    """Hold the weapon with both hands the way the character's own clips do: the rig pack's grip.json is the left
    hand's exact transform on the handle in an approved two-handed frame. The right hand carries the weapon; the
    spine and both arms are solved together so the left hand lands on that grip, the blade points along `blade`
    (char-frame direction, hilt to tip) and the hilt sits near `hilt` (a soft char-frame point: it moves to where
    both arms can hold it). Elbows bend only on their hinges, within range.

    Returns (pose, report) with the left hand's remaining miss from the grip (metres, degrees), the blade's
    direction error (degrees) and how far the hilt sits from `hilt` (metres)."""
    rig = pose.rig
    grip = rig.grip
    if not grip:
        raise ValueError('No grip.json in this rig pack: the character has no approved two-handed frame')
    torso = [n for n in ('Spine', 'Spine1', 'Spine2') if spine and n in rig.names]
    hold = {'attach': grip['hand'], 'to': grip['weapon'], 'local': grip['matrix'], 'weight': 10.0}
    aims = []
    if blade is not None:
        aims.append({'dir': ['bladeHilt', 'bladeEnd'], 'along': blade, 'weight': 5.0})
    if hilt is not None:
        aims.append({'pos': 'bladeHilt', 'at': hilt, 'weight': hilt_weight})
    if aims:  # the weapon arm aims first, which gives the joint solve a good start
        pose, _ = solve(pose, torso + [n for n in GRIP_ARMS[:4] if n in rig.names], aims, reg=reg)
    pose, _ = solve(pose, torso + [n for n in GRIP_ARMS if n in rig.names], [hold] + aims, reg=reg, max_nfev=400)
    return pose, grip_report(pose, blade, hilt)


def grip_report(pose: Pose, blade=None, hilt=None) -> dict:
    grip = pose.rig.grip
    _, metres, degrees = _attach_error(pose, {'attach': grip['hand'], 'to': grip['weapon'], 'local': grip['matrix']})
    report = {'leftHandMissMetres': round(metres, 4), 'leftHandMissDegrees': round(degrees, 1)}
    if blade is not None:
        report['bladeErrorDegrees'] = round(_angle(pose.pos('bladeEnd') - pose.pos('bladeHilt'), blade), 1)
    if hilt is not None:
        report['hiltFromRequestMetres'] = round(float(np.linalg.norm(pose.pos('bladeHilt') - np.asarray(hilt, float))), 3)
    return report


def hold_grip(clip, first, last, reg=0.05):
    """Keep the left hand on the grip on every tick from `first` to `last` (interpolation between keys lets it
    drift): the left arm is re-solved per tick from the interpolated pose. Returns the largest remaining miss."""
    rig = clip.rig
    grip = rig.grip
    hold = {'attach': grip['hand'], 'to': grip['weapon'], 'local': grip['matrix'], 'weight': 10.0}
    arm = [n for n in GRIP_ARMS[4:] if n in rig.names]
    worst = (0.0, 0.0)
    for tick in range(first, last + 1):
        pose, _ = solve(clip.pose(tick), arm, [hold], reg=reg)
        clip.set_pose(tick, pose)
        report = grip_report(pose)
        worst = (max(worst[0], report['leftHandMissMetres']), max(worst[1], report['leftHandMissDegrees']))
    return {'maxMissMetres': worst[0], 'maxMissDegrees': worst[1]}


def _angle(a, b) -> float:
    return float(np.degrees(np.arccos(np.clip(unit(a) @ unit(b), -1, 1))))


FIST = {'Knuckle': 80.0, 'Middle': 95.0, 'Tip': 60.0}


def fist(pose: Pose, side: str, amount=1.0, thumb=1.0, spread=0.0) -> Pose:
    """Close a hand: fingers curl toward the palm (bind-relative), the thumb wraps across.

    amount scales finger flexion (0 open .. 1 full fist); thumb scales the thumb wrap.
    """
    rig = pose.rig
    for finger in FINGERS:
        base = side + 'Hand' + finger
        if base + '3' not in rig.names:
            continue
        pose.set_hinge(side + finger + 'Knuckle', FIST['Knuckle'] * amount)
        pose.set_hinge(side + finger + 'Middle', FIST['Middle'] * amount)
        # The last phalanx has no tip joint: bend it about the middle hinge axis carried into its parent,
        # or about Unity's own curl axis for it when the rig pack has one.
        _, _, m2, _, axis1 = rig.hinges[side + finger + 'Middle']
        tip = rig.index(base + '3')
        axis2 = Rotation.from_quat(rig.bind_q[m2]).inv().apply(axis1)
        curl = next((m['axis'] for m in rig.humanoid.get(tip, []) if 'Stretched' in m['muscle']), None)
        if curl is not None:
            axis2 = curl if curl @ axis2 >= 0 else -curl
        pose.q[tip] = (Rotation.from_rotvec(np.radians(FIST['Tip'] * amount) * axis2) * Rotation.from_quat(rig.bind_q[tip])).as_quat()
    _thumb(pose, side, thumb)
    pose._changed()
    digits = [j for j in rig.humanoid_digits if rig.names[j].startswith(side + 'Hand')]
    if digits:  # what Unity's Humanoid can play
        pose.snap_to_humanoid([rig.names[j] for j in digits])
    return pose


def _thumb(pose: Pose, side, amount):
    """Thumb joints flex toward the palm, each in whichever direction brings the thumb tip closer to the
    curled fingers (rig-independent; bind-derived axes alone point the wrong way on some hands)."""
    rig = pose.rig
    names = [side + 'HandThumb' + str(i) for i in (1, 2, 3)]
    if not all(n in rig.names for n in names):
        return
    b = rig.bind_positions
    palm = rig.palm_normal_bind(side)
    index1 = b[rig.index(side + 'HandIndex1')]
    outer = next((side + 'Hand' + f + '1' for f in ('Little', 'Pinky', 'Ring', 'Middle') if side + 'Hand' + f + '1' in rig.names), None)
    if outer:
        across = unit(b[rig.index(outer)] - index1)  # toward the little-finger side
    else:  # one finger chain (mitten hand): away from the thumb, across the palm
        along = unit(index1 - b[rig.index(side + 'Hand')])
        away = index1 - b[rig.index(side + 'HandThumb1')]
        across = unit(away - along * (away @ along))
    curled = [side + 'Hand' + f + '2' for f in ('Index', 'Middle') if side + 'Hand' + f + '2' in rig.names]
    tip = rig.index(names[-1])

    def target():
        world = pose.world()
        return np.mean([world[rig.index(n), 3, :3] for n in curled], axis=0)

    if all(rig.index(n) in rig.humanoid for n in names):
        # Unity's thumb muscles: curl each joint about its own axes (Thumb 1 also spreads across the palm), within
        # their ranges, choosing the directions that bring the tip to the curled fingers.
        for n in names:
            j = rig.index(n)
            bind = Rotation.from_quat(rig.bind_q[j])
            muscles = rig.humanoid[j]
            options = [np.zeros(3)]
            for m in muscles:
                reach = np.radians(min(abs(m['minus']), abs(m['plus']))) * amount
                options = [o + s * reach * m['axis'] for o in options for s in (-1.0, 0.0, 1.0)]
            best = None
            for vector in options:
                pose.q[j] = (Rotation.from_rotvec(vector) * bind).as_quat()
                pose._changed()
                gap = np.linalg.norm(pose.world()[tip, 3, :3] - target())
                if best is None or gap < best[0]:
                    best = (gap, pose.q[j].copy())
            pose.q[j] = best[1]
            pose._changed()
        return
    angles = {names[0]: 35.0, names[1]: 40.0, names[2]: 45.0}
    for n in names:
        j = rig.index(n)
        parent = rig.parents[j]
        child = rig.children(n)
        seg = unit(b[child[0]] - b[j]) if child else unit(b[j] - b[parent])
        axis_world = unit(np.cross(seg, unit(palm + 0.6 * across)))
        axis_parent = axis_world @ rotation_part(rig.bind_world[parent]).T
        best = None
        for sign in (1.0, -1.0):
            q = (Rotation.from_rotvec(sign * np.radians(angles[n] * amount) * axis_parent) * Rotation.from_quat(rig.bind_q[j])).as_quat()
            pose.q[j] = q
            pose._changed()
            gap = np.linalg.norm(pose.world()[tip, 3, :3] - target())
            if best is None or gap < best[0]:
                best = (gap, q)
        pose.q[j] = best[1]
        pose._changed()
