"""Inverse kinematics that respects anatomy.

two_bone: analytic limb IK; the middle joint only bends about its anatomical hinge.
solve: least-squares targets over chosen joints; hinge joints get one degree of freedom.
track, pin, hold_grip: per-tick solves that keep contacts between keys, each tick seeded from the last.
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


WRIST_WEIGHT = 0.5  # residual per radian of wrist bend past the limit: a grip (10 per metre) still wins a conflict
ROLL_WEIGHT = 0.1   # residual per radian a held hand turns about the handle with a 'roll' grip


def wrist_joints(rig, side) -> tuple[int, int, int] | None:
    """(forearm, hand, middle knuckle) joint indices of one side, or None: the wrist bend is the angle between the
    forearm and the hand."""
    knuckle = next((side + 'Hand' + f + '1' for f in ('Middle', 'Index') if side + 'Hand' + f + '1' in rig.names), None)
    if knuckle is None or side + 'ForeArm' not in rig.names or side + 'Hand' not in rig.names:
        return None
    return rig.index(side + 'ForeArm'), rig.index(side + 'Hand'), rig.index(knuckle)


def wrist_bend(world, joints) -> float:
    """Degrees between the forearm (elbow to wrist) and the hand (wrist to middle knuckle), as the form check measures
    them. `joints` comes from wrist_joints."""
    e, h, k = (world[j, 3, :3] for j in joints)
    return _angle(h - e, k - h)


def solve(pose: Pose, joints, targets, reg=0.02, hinges=True, max_nfev=200, humanoid=True, wrists=None, seed=None,
          seed_weight=0.0):
    """Least-squares IK. Returns (new_pose, report).

    joints: names to vary. Hinge joints (knee/elbow/finger middles) vary only about
    their anatomical axis when hinges=True; others get a rotation in their parent frame.
    targets: list of dicts, each with 'weight' (default 1) and one of
      {'pos': joint, 'at': [r, f, u]}                  joint position (char metres)
      {'dir': [joint_a, joint_b], 'along': [r, f, u]}  segment a->b direction
      {'axis': joint, 'local': [x, y, z], 'along': [r, f, u]}  a joint-local axis
      {'palm': 'Left'|'Right', 'along': [r, f, u]}     palm normal direction
      {'attach': joint, 'to': joint, 'local': 4x4}     a fixed transform in another joint's frame (grip_target)
      {'handle': hand, ...}                            a loose hold on the weapon's handle (handle_target)
      {'below': joint, 'than': joint, 'by': metres}    a joint no higher than another, less `by` (an elbow kept down)
    reg pulls each varied joint toward its starting rotation (radians).
    humanoid: spine, neck, head, collarbone, upper-arm and thigh joints vary only through their Unity Humanoid
    muscles, within the limits the game can play (rig.humanoid_limited, rig.humanoid_bounds); one that starts
    past them never goes further out.
    wrists: a soft limit in degrees for each hand among `joints`. Past it the bend off the forearm costs, so the solver
    turns the forearm, elbow and shoulder instead of folding the wrist (45 keeps a hand in line as the approved clips'
    grips do). A target the hand can only reach by folding still wins. The report adds each hand's bend.
    seed: a Pose to start from, usually the neighbouring tick's or key's solution. The solve then stays on the same
    elbow and roll solution as its neighbour: keys solved separately from one start can land on different ones and
    flip between them in one tick (Sir Bonk's E1 slam rolled an upper arm 153 degrees). `reg` still pulls toward `pose`.
    seed_weight (per radian) also keeps the result near the seed: a start alone can still slide onto another solution
    when `pose` sits far from it. track() uses it, so an elbow and its roll stay continuous from tick to tick.
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
    x0 = np.zeros(sum(sizes))
    if seed is not None:  # the same parameters, read off the seed's rotations
        x0 = []
        for j, n in zip(idx, sizes):
            if j in muscles:
                x0 += list(rig.humanoid_split(j, seed.q[j])[0] - muscles[j][1])
            else:
                delta = (Rotation.from_quat(seed.q[j]) * Rotation.from_quat(start[j]).inv()).as_rotvec()
                x0 += [float(delta @ hinge_axis[j])] if n == 1 else list(delta)
        x0 = np.clip(x0, np.asarray(lower) + 1e-9, np.asarray(upper) - 1e-9)
    bends = [w for w in (wrist_joints(rig, side) for side in ('Left', 'Right')) if w and w[1] in idx] if wrists is not None else []
    limit = np.radians(wrists or 0.0)

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
            elif 'handle' in t:
                out += list(w * _handle_error(p, t)[0])
            elif 'below' in t:
                out.append(w * max(0.0, p.pos(t['below'])[2] - p.pos(t['than'])[2] + t.get('by', 0.0)))
            else:
                raise ValueError(f'Unknown target {t}')
        if bends:
            world = p.world()
            out += [WRIST_WEIGHT * max(0.0, np.radians(wrist_bend(world, w)) - limit) for w in bends]
        out += list(reg * x)
        if seed is not None and seed_weight:
            out += list(seed_weight * (x - x0))
        return np.asarray(out)

    result = least_squares(residual, x0, bounds=(lower, upper), max_nfev=max_nfev)
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
            _, metres, degrees, turned = _attach_error(best, t)
            report.append({'attach': t['attach'], 'to': t['to'], 'errorMetres': metres, 'errorDegrees': degrees}
                          | ({'rollDegrees': turned} if t.get('roll') is not None else {}))
        elif 'handle' in t:
            report.append({'handle': t['handle']} | _handle_error(best, t)[1])
        elif 'below' in t:
            report.append({'below': t['below'], 'than': t['than'],
                           'aboveMetres': float(best.pos(t['below'])[2] - best.pos(t['than'])[2] + t.get('by', 0.0))})
        else:
            report.append({'palm': t['palm'], 'errorDegrees': _angle(best.palm_normal(t['palm']), t['along'])})
    report += [{'wrist': rig.names[w[1]][:-len('Hand')], 'degrees': wrist_bend(best.world(), w)} for w in bends]
    return best, report


HINGE_RANGE = {'knee': (-5.0, 160.0), 'elbow': (-5.0, 160.0), 'finger': (-15.0, 110.0)}  # degrees of flexion


def _attach_error(pose: Pose, target, lever=0.1):
    """Residual (position, then two axes x lever) of joint `attach` against its fixed transform `local` in the
    frame of joint `to` (row convention: attach = local @ to), with the miss in metres and degrees and the roll used.
    With 'roll' (a cost per radian) the transform may turn about the weapon's handle line (bladeEnd to bladeHilt): the
    hand keeps its place along the handle and its hold on it, and turns around it where the wrist needs that; the miss
    is then measured from the turned grip."""
    rig, world = pose.rig, pose.world()
    goal = np.asarray(target['local'], float) @ world[rig.index(target['to'])]
    now = world[rig.index(target['attach'])]
    extra, turned = [], 0.0
    if target.get('roll') is not None:
        c = world[rig.index('bladeHilt'), 3, :3]
        u = unit(c - world[rig.index('bladeEnd'), 3, :3])
        a, g = now[3, :3] - c, goal[3, :3] - c
        a, g = a - u * (a @ u), g - u * (g @ u)
        turned = float(np.arctan2(np.cross(g, a) @ u, g @ a))  # turns the grip's position onto the hand's
        spin = Rotation.from_rotvec(turned * u).as_matrix()  # column convention
        goal = goal.copy()
        goal[:3, :3] = goal[:3, :3] @ spin.T
        goal[3, :3] = c + spin @ (goal[3, :3] - c)
        extra = [target['roll'] * turned]
    a, b = rotation_part(now), rotation_part(goal)
    residual = np.concatenate([now[3, :3] - goal[3, :3], lever * (a[0] - b[0]), lever * (a[1] - b[1]), extra])
    degrees = float(np.degrees(Rotation.from_matrix(a.T @ b).magnitude()))
    return residual, float(np.linalg.norm(now[3, :3] - goal[3, :3])), degrees, float(np.degrees(turned))


GRIP_ARMS = [side + joint for side in ('Right', 'Left') for joint in ('Shoulder', 'Arm', 'ForeArm', 'Hand')]


def grip_target(rig, roll=False, weight=10.0) -> dict:
    """The other hand's hold on the weapon as a solve() target: grip.json's transform. roll=True (or a cost per
    radian) lets the hand turn about the handle, which straightens a wrist when the body moves around a held weapon."""
    grip = rig.grip
    if not grip:
        raise ValueError('No grip.json in this rig pack: run unity_rigpack.py grip for the character')
    target = {'attach': grip['hand'], 'to': grip['weapon'], 'local': grip['matrix'], 'weight': weight}
    if roll:
        target['roll'] = ROLL_WEIGHT if roll is True else float(roll)
    return target


TILT_WEIGHT = 0.1  # residual (metres) per radian a loose hold's fist tilts off the handle past its tolerance


def handle_target(rig, along=None, gap=0.08, slack=0.03, tilt=40.0, weight=10.0) -> dict:
    """A loose hold for the other hand as a solve() target: its fist stays on the weapon's handle beside the weapon hand,
    nothing more. The fist is the point grip.json's handle runs through, in the hand's own frame; it stays within
    `slack` metres of the handle line (bladeHilt to bladeEnd, extended past the hilt), between `along` metres past the
    hilt (default: from `gap` beyond the weapon hand to 5 cm beyond grip.json's hold, on the grip's side of the weapon
    hand, so the forearms never cross), at least `gap` metres along the handle from the weapon hand, and the fist's own
    handle axis within `tilt` degrees of the handle. The hand may turn about the handle and slide along it, so the arm
    keeps its natural path: an exact grip.json transform locks the arm into one solution, which can wing the elbows up
    (Sir Bonk's Barn Door, 2026-10-22)."""
    grip = rig.grip
    if not grip:
        raise ValueError('No grip.json in this rig pack: run unity_rigpack.py grip for the character')
    world = rig.bind_world
    goal = np.asarray(grip['matrix'], float) @ world[rig.index(grip['weapon'])]
    turn = rotation_part(goal)
    hilt = world[rig.index('bladeHilt'), 3, :3]
    u = unit(hilt - world[rig.index('bladeEnd'), 3, :3])  # toward the pommel
    past = float((goal[3, :3] - hilt) @ u)
    on = hilt + u * past
    j = rig.index(grip['weapon'])
    while j >= 0 and not rig.names[j].endswith('Hand'):
        j = rig.parents[j]
    if j < 0:
        raise ValueError(f"{grip['weapon']} hangs under no hand joint")
    if along is None:
        own = float((world[j, 3, :3] - hilt) @ u)  # the weapon hand's place along the handle
        side = 1.0 if past >= own else -1.0
        along = sorted([own + side * gap, past + side * 0.05])
    return {'handle': grip['hand'], 'beside': rig.names[j], 'fist': ((on - goal[3, :3]) @ turn.T).tolist(),
            'fistAxis': (u @ turn.T).tolist(), 'along': list(along), 'gap': gap, 'slack': slack, 'tilt': tilt,
            'weight': weight}


def _handle_error(pose: Pose, target):
    """Residual of a handle_target (metres; tilt past its tolerance at TILT_WEIGHT per radian) and its report."""
    rig, world = pose.rig, pose.world()
    hand = world[rig.index(target['handle'])]
    turn = rotation_part(hand)
    fist = hand[3, :3] + np.asarray(target['fist'], float) @ turn
    hilt = world[rig.index('bladeHilt'), 3, :3]
    u = unit(hilt - world[rig.index('bladeEnd'), 3, :3])
    v = fist - hilt
    past = float(v @ u)
    off = v - u * past
    distance = float(np.linalg.norm(off))
    lo, hi = target['along']
    gap = past - float((world[rig.index(target['beside']), 3, :3] - hilt) @ u)
    tilt = _angle(np.asarray(target['fistAxis'], float) @ turn, u)
    residual = np.concatenate([off * (max(0.0, distance - target['slack']) / distance if distance > 1e-9 else 0.0),
                               [max(0.0, lo - past) + max(0.0, past - hi), max(0.0, target['gap'] - abs(gap)),
                                TILT_WEIGHT * np.radians(max(0.0, tilt - target['tilt']))]])
    return residual, {'offHandleMetres': round(distance, 4), 'pastHiltMetres': round(past, 3),
                      'gapMetres': round(gap, 3), 'tiltDegrees': round(tilt, 1)}


HOLDS = ('exact', 'loose')


def hold_target(rig, hold='exact', roll=False) -> dict:
    """The other hand's two-handed hold as a solve() target: 'exact' is grip.json's transform (grip_target, `roll` as
    there), 'loose' its fist anywhere on the handle beside the weapon hand (handle_target)."""
    if hold not in HOLDS:
        raise ValueError(f'hold is one of {HOLDS}, not {hold!r}')
    return handle_target(rig) if hold == 'loose' else grip_target(rig, roll)


ELBOW_DROP = 0.05  # metres an elbow kept down sits below its shoulder, at least


def elbows_down(rig, sides=('Right', 'Left'), weight=2.0, shoulders=True) -> list[dict]:
    """solve() targets that keep each elbow below its shoulder (by ELBOW_DROP) and no higher than its hand, so arms reach
    a held weapon with the elbows hanging, not winged up (Sir Bonk's Barn Door: the exact two-handed grip lifted his
    left elbow 11 cm over the shoulder). With the hands overhead (a rising slash's finish) the elbows belong above the
    shoulders: shoulders=False keeps them only under the hands. Start the solve from drop_elbows(): it will not swing an
    elbow over the top."""
    pairs = (('Arm', ELBOW_DROP), ('Hand', 0.0)) if shoulders else (('Hand', 0.0),)
    return [{'below': side + 'ForeArm', 'than': side + joint, 'by': by, 'weight': weight}
            for side in sides if side + 'ForeArm' in rig.names for joint, by in pairs]


def _elbow_targets(rig, elbows) -> list[dict]:
    """elbows_down for an `elbows` option: False (none), True (below the shoulders and the hands) or 'hands' (below
    the hands only, for poses with the hands overhead)."""
    if elbows not in (False, True, 'hands'):
        raise ValueError(f"elbows is True, False or 'hands', not {elbows!r}")
    return elbows_down(rig, shoulders=elbows is True) if elbows else []


def drop_elbows(pose: Pose, sides=('Right', 'Left')) -> Pose:
    """Swing each arm about its shoulder-to-hand line until the elbow hangs down (and a little forward), the hand where
    it was and as it was (two_bone with a pole), then bring the upper arm back within what Unity's Humanoid plays
    (clamp_humanoid: two_bone ignores the muscle limits, and solve() never takes a joint further out than its start;
    an unclamped start left Barn Door's left upper arm twisted 14 degrees past them, which the game would not play).
    Edits `pose` in place: the start for a solve with elbows_down()."""
    front = _chest_frame(pose)[0][:, 1].copy()
    front[2] = 0.0
    pole = 0.3 * unit(front) + np.array([0.0, 0.0, -1.0])
    for side in sides:
        two_bone(pose, side, 'Arm', list(pose.pos(side + 'Hand')), pole=list(pole))
        clamp_humanoid(pose, [side + 'Shoulder', side + 'Arm'])
    return pose


def clamp_humanoid(pose: Pose, joints) -> Pose:
    """Clamp each Humanoid-limited joint among `joints` to its muscle ranges (rig.humanoid_bounds), as the game
    plays it. Edits `pose` in place."""
    rig = pose.rig
    for name in joints:
        j = rig.index(name) if name in rig.names else None
        if j is None or j not in rig.humanoid_limited:
            continue
        axes, low, high = rig.humanoid_bounds(j)
        angles = rig.humanoid_split(j, pose.q[j])[0]
        pose.q[j] = (Rotation.from_rotvec(axes.T @ np.clip(angles, low, high)) * rig.humanoid[j][0]['zero']).as_quat()
    pose._changed()
    return pose


def two_hand_grip(pose: Pose, blade=None, hilt=None, hilt_weight=0.3, spine=True, reg=0.02, wrists=45.0, roll=False,
                  seed=None, hold='exact', elbows=False) -> tuple[Pose, dict]:
    """Hold the weapon with both hands. The right hand carries the weapon; the spine and both arms are solved together
    so the left hand takes its `hold`, the blade points along `blade` (char-frame direction, hilt to tip) and the hilt
    sits near `hilt` (a soft char-frame point: it moves to where both arms can hold it). hold='exact' puts the left
    hand on the rig pack's grip.json, its exact transform in an approved two-handed frame (`roll` lets it turn about
    the handle, see grip_target); hold='loose' only keeps its fist on the handle beside the right hand (handle_target).
    `elbows` keeps both elbows hanging (drop_elbows, elbows_down): with the loose hold, the arms of a two-handed swing
    look natural (Sir Bonk's Barn Door); elbows='hands' keeps them only under the hands, for poses with the hands
    overhead. Elbows bend only on their hinges, within range; wrists past `wrists` degrees cost. `seed`: the
    neighbouring key's pose.

    Returns (pose, report): the left hand against its hold (hold_report), the blade's direction error (degrees) and how
    far the hilt sits from `hilt` (metres)."""
    rig = pose.rig
    torso = [n for n in ('Spine', 'Spine1', 'Spine2') if spine and n in rig.names]
    target = hold_target(rig, hold, roll)
    aims = []
    if blade is not None:
        aims.append({'dir': ['bladeHilt', 'bladeEnd'], 'along': blade, 'weight': 5.0})
    if hilt is not None:
        aims.append({'pos': 'bladeHilt', 'at': hilt, 'weight': hilt_weight})
    down = _elbow_targets(rig, elbows)
    if elbows:
        pose = drop_elbows(pose.copy())
    if aims:  # the weapon arm aims first, which gives the joint solve a good start
        pose, _ = solve(pose, torso + [n for n in GRIP_ARMS[:4] if n in rig.names],
                        aims + [t for t in down if t['below'].startswith('Right')], reg=reg, wrists=wrists, seed=seed)
        seed = None
    pose, _ = solve(pose, torso + [n for n in GRIP_ARMS if n in rig.names], [target] + aims + down, reg=reg,
                    max_nfev=400, wrists=wrists, seed=seed)
    return pose, hold_report(pose, hold, blade, hilt)


def hold_report(pose: Pose, hold='exact', blade=None, hilt=None) -> dict:
    """The left hand against its two-handed hold: grip_report for 'exact'; for 'loose' how far its fist sits off the
    handle line (metres), its tilt on the handle (degrees), how far past the hilt it holds and its gap along the handle
    from the weapon hand (metres)."""
    if hold == 'exact':
        return grip_report(pose, blade, hilt)
    info = _handle_error(pose, hold_target(pose.rig, hold))[1]
    report = {'leftHandOffHandleMetres': info['offHandleMetres'], 'leftHandTiltDegrees': info['tiltDegrees'],
              'leftHandPastHiltMetres': info['pastHiltMetres'], 'leftHandGapMetres': info['gapMetres']}
    return report | {k: v for k, v in grip_report(pose, blade, hilt).items() if not k.startswith('leftHand')}


def grip_report(pose: Pose, blade=None, hilt=None) -> dict:
    """The left hand against grip.json: its miss in metres and degrees after turning the grip about the handle to the
    hand (a held hand may sit anywhere around it), and how far it turned."""
    grip = pose.rig.grip
    target = {'attach': grip['hand'], 'to': grip['weapon'], 'local': grip['matrix'], 'roll': 0.0}
    _, metres, degrees, turned = _attach_error(pose, target)
    report = {'leftHandMissMetres': round(metres, 4), 'leftHandMissDegrees': round(degrees, 1),
              'leftHandRollDegrees': round(turned, 1)}
    if blade is not None:
        report['bladeErrorDegrees'] = round(_angle(pose.pos('bladeEnd') - pose.pos('bladeHilt'), blade), 1)
    if hilt is not None:
        report['hiltFromRequestMetres'] = round(float(np.linalg.norm(pose.pos('bladeHilt') - np.asarray(hilt, float))), 3)
    return report


def track(clip, first, last, joints, targets, reg=0.05, wrists=45.0, max_nfev=100, continuity=0.3) -> dict:
    """Re-solve `joints` on every tick from `first` to `last` so `targets` hold between keys, where interpolation lets
    hands slide off a handle and pinned points drift. Each tick starts from the previous tick's solution and stays near
    it (`continuity`, per radian), so the elbow and its roll stay on one solution (no flips) and the solve converges in a
    few steps; `reg` pulls toward the interpolated pose, so the keys' motion still shows. Key the first and last ticks
    with the same contact (two_hand_grip, or the pin's target) so the tracked stretch starts and ends where the keys
    are. `targets` is a list used on every tick or a function (tick, pose) -> list. Edits the clip in place. Returns
    the largest remaining error of each target (metres or degrees), the largest wrist bends and roll, and the time."""
    import time
    started = time.time()
    worst, previous = {}, None
    for tick in range(first, last + 1):
        pose = clip.pose(tick)
        wanted = targets(tick, pose) if callable(targets) else targets
        solved, rows = solve(pose, joints, wanted, reg=reg, wrists=wrists, seed=previous, max_nfev=max_nfev,
                             seed_weight=continuity)
        clip.set_pose(tick, solved)
        previous = solved
        _worst(worst, rows)
    return worst | {'seconds': round(time.time() - started, 1)}


def _worst(worst, rows):
    """Fold solve() report rows into `worst`: the largest error (or bend, roll, offset) of each target."""
    for row in rows:
        kind = next(k for k in ('pos', 'dir', 'axis', 'palm', 'attach', 'handle', 'below', 'wrist') if k in row)
        name = '-'.join(row[kind]) if kind == 'dir' else row[kind]
        for key in ('errorMetres', 'errorDegrees', 'degrees', 'rollDegrees', 'offHandleMetres', 'tiltDegrees',
                    'aboveMetres'):
            if key in row:
                label = f'{kind} {name} {key}'
                worst[label] = max(worst.get(label, 0.0), abs(round(float(row[key]), 4)) if key != 'aboveMetres'
                                   else round(float(row[key]), 4))


def _chain(rig, point) -> list[str]:
    """The animatable joints from `point` up its side of the body (collarbone or thigh first); the weapon joints a
    hand carries are rigid."""
    out, j = [], rig.index(point)
    while j >= 0 and rig.side(rig.names[j]):
        if not rig.names[j].startswith(('Weapon', 'blade')):
            out.append(rig.names[j])
        j = int(rig.parents[j])
    return out[::-1]


def pin(clip, first, last, point='bladeEnd', at=None, joints=None, grip=None, extra=None, reg=0.05, wrists=45.0,
        weight=10.0, continuity=0.3) -> dict:
    """Keep `point` (a joint or a weapon anchor such as bladeEnd) where it is at `first`, or at char-frame `at`, on every
    tick from `first` to `last` while the keyed body moves. A weapon stuck in the floor then pivots about its tip, and
    the hand that carries it goes where the body takes it. Pin what is physically fixed: pinning the hand instead also
    freezes its orientation (a weapon is rigid in its hand), so the wrists fold as the body turns around it (Sir Bonk's
    stuck plank: the right hand did not turn for 32 ticks, through a handstand and a haul, and the haul became a crouch).

    joints: what may move (default: the chain that carries the point, collarbone to hand). grip: 'fixed' or 'roll'
    keeps the other hand on the weapon too (grip.json; 'roll' lets it turn about the handle). extra(tick, pose) returns
    more targets for a tick, e.g. a soft hand position that steers how far the handle rocks. Returns track()'s report."""
    rig = clip.rig
    at = clip.pose(first).pos(point) if at is None else np.asarray(at, float)
    joints = list(joints) if joints is not None else _chain(rig, point)
    hold = []
    if grip:
        other = 'Left' if rig.side(point) == 'Right' else 'Right'
        joints += [other + j for j in ('Shoulder', 'Arm', 'ForeArm', 'Hand') if other + j in rig.names and other + j not in joints]
        hold = [grip_target(rig, roll=grip == 'roll')]

    def targets(tick, pose):
        return [{'pos': point, 'at': at, 'weight': weight}] + hold + (extra(tick, pose) if extra else [])

    return track(clip, first, last, joints, targets, reg=reg, wrists=wrists, continuity=continuity)


def _chest_frame(pose: Pose):
    """The chest's axes (side, front, up as columns) and origin from joint positions, in the character frame."""
    up = unit(pose.pos('Neck') - pose.pos('Spine2'))
    side = pose.pos('RightArm') - pose.pos('LeftArm')
    side = unit(side - up * float(np.dot(side, up)))
    return np.column_stack([side, np.cross(up, side), up]), pose.pos('Spine2')


def _weapon_frame(pose: Pose, hand='RightHand'):
    """The weapon's axes (blade, toward the weapon hand, their cross) and its hilt, in the character frame."""
    hilt = pose.pos('bladeHilt')
    blade = unit(pose.pos('bladeEnd') - hilt)
    toward = pose.pos(hand) - hilt
    toward = unit(toward - blade * float(np.dot(toward, blade)))
    return np.column_stack([blade, toward, np.cross(blade, toward)]), hilt


def carry(clip, keys, grip=True, reg=0.05, wrists=45.0, continuity=0.3, hold='exact', elbows=False, path=3.0,
          both_ways=False) -> dict:
    """A held weapon carried by the chest between keyed ticks `keys`: its pose relative to the chest is interpolated
    (rotation slerped, hilt on a monotone curve) and the arms are re-solved on every tick between the keys to hold it,
    the other hand in its `hold` when `grip` (hold_target: grip.json's 'exact' transform, or 'loose' anywhere on the
    handle beside the weapon hand) and, with `elbows`, the elbows hanging (elbows_down; 'hands' for overhead swings).
    In a two-handed swing the arms
    and weapon move almost as one piece and the body turns them, so a big sweep comes from the hips and chest;
    interpolating each arm joint on its own rolls the hands round the handle instead (Sir Bonk's Barn Door, 2026-10-21:
    the left hand turned 163 degrees in three ticks).

    The blade's direction and roll are the swing and stay firm; `path` weights the interpolated hilt position (per
    metre), so a lower weight (1) lets the weapon give a few centimetres to where both arms hold it best. Solves run
    forward from each key (track's continuity). With `both_ways` each stretch is also solved backward from the next key,
    the two are blended across it and re-solved, so every tick answers to the key after it as well as the one before
    and the arms arrive on the next key's own solution. `landing` reports how far the last solved tick sits from the
    next key in the arms' rotations, so keys whose arms disagree show up: solve such keys from their neighbour
    (two_hand_grip(..., seed=K[previous])). Edits the clip in place."""
    from scipy.interpolate import PchipInterpolator
    from scipy.spatial.transform import Slerp
    keys = sorted(int(k) for k in keys)
    rig = clip.rig
    arms = [n for n in (GRIP_ARMS if grip else GRIP_ARMS[:4]) if n in rig.names]
    rel_rot, rel_pos = [], []
    for tick in keys:
        pose = clip.pose(tick)
        chest, origin = _chest_frame(pose)
        weapon, hilt = _weapon_frame(pose)
        rel_rot.append(Rotation.from_matrix(chest.T @ weapon))
        rel_pos.append(chest.T @ (hilt - origin))
    turn = Slerp(keys, Rotation.concatenate(rel_rot))
    curve = PchipInterpolator(keys, np.array(rel_pos), axis=0)
    extra = ([hold_target(rig, hold)] if grip else []) + _elbow_targets(rig, elbows)
    # The weapon hand sits fixed in the weapon's frame (along the handle, a little off its axis): its direction from
    # the hilt in the wanted frame turns the weapon's roll. Until 2026-10-22 this aimed the hilt-to-hand line at the
    # frame's off-axis column, 77 degrees off for Sir Bonk's plank, which tipped every carried tick ~18 degrees.
    weapon, hilt = _weapon_frame(clip.pose(keys[0]))
    hand = weapon.T @ (clip.pose(keys[0]).pos('RightHand') - hilt)

    def wanted(tick, pose):
        chest, origin = _chest_frame(pose)
        weapon = chest @ turn([tick])[0].as_matrix()
        hilt = origin + chest @ curve(tick)
        return [{'pos': 'bladeHilt', 'at': list(hilt), 'weight': path},
                {'dir': ['bladeHilt', 'bladeEnd'], 'along': list(weapon[:, 0]), 'weight': 5.0},
                {'dir': ['bladeHilt', 'RightHand'], 'along': list(weapon @ hand), 'weight': 3.0}] + extra

    report, landing = {}, {}
    idx = [rig.index(n) for n in arms]
    for a, b in zip(keys, keys[1:]):
        if b - a < 2:
            continue
        if not both_ways:
            part = track(clip, a + 1, b - 1, arms, wanted, reg=reg, wrists=wrists, continuity=continuity)
            for k, v in part.items():
                report[k] = max(report.get(k, 0.0), v) if k != 'seconds' else report.get(k, 0.0) + v
        else:
            report['seconds'] = report.get('seconds', 0.0) + _carry_both_ways(clip, a, b, arms, wanted, reg, wrists,
                                                                              continuity, report)
        last, key = clip.q[b - 1, idx], clip.q[b, idx]
        dots = np.abs(np.sum(last * key, axis=1)).clip(0, 1)
        landing[b] = round(float(np.degrees(2 * np.arccos(dots)).max()), 1)
    return report | {'landing (deg)': landing}


BLEND_HOLD = 2.0  # per radian: the final solve of a both-ways carry stays near the blend of its two passes


def _carry_both_ways(clip, a, b, arms, wanted, reg, wrists, continuity, worst) -> float:
    """carry() between keys a and b solved forward from a and backward from b, blended (smoothstep from a's solution
    to b's) and re-solved near the blend. Adds the largest target errors to `worst`; returns the seconds taken."""
    import time
    from scipy.spatial.transform import Slerp
    started = time.time()
    base = {t: clip.pose(t) for t in range(a + 1, b)}
    passes = []
    for ticks, start in ((range(a + 1, b), a), (range(b - 1, a, -1), b)):
        solved, previous = {}, clip.pose(start)
        for t in ticks:
            solved[t], _ = solve(base[t], arms, wanted(t, base[t]), reg=reg, wrists=wrists, seed=previous,
                                 seed_weight=continuity, max_nfev=100)
            previous = solved[t]
        passes.append(solved)
    forward, backward = passes
    for t in range(a + 1, b):
        s = (t - a) / (b - a)
        s = s * s * (3 - 2 * s)
        pose = base[t].copy()
        for j in arms:
            ends = Rotation.concatenate([forward[t].local_rotation(j), backward[t].local_rotation(j)])
            pose.set_local_rotation(j, Slerp([0, 1], ends)([s])[0])
        solved, rows = solve(pose, arms, wanted(t, pose), reg=0.0, wrists=wrists, seed=pose, seed_weight=BLEND_HOLD,
                             max_nfev=100)
        clip.set_pose(t, solved)
        _worst(worst, rows)
    return round(time.time() - started, 1)


def hold_grip(clip, first, last, reg=0.05, wrists=45.0, roll=False, hold='exact'):
    """Keep the left hand in its two-handed `hold` on every tick from `first` to `last` (interpolation between keys
    lets it drift): the left arm is re-solved per tick (track: seeded from the previous tick, wrists kept in line,
    `roll` lets an exact grip turn about the handle). Returns the largest remaining miss."""
    rig = clip.rig
    arm = [n for n in GRIP_ARMS[4:] if n in rig.names]
    report = track(clip, first, last, arm, [hold_target(rig, hold, roll)], reg=reg, wrists=wrists)
    misses = [hold_report(clip.pose(tick), hold) for tick in range(first, last + 1)]
    wrist = {'maxWristDegrees': max([v for k, v in report.items() if k.startswith('wrist')] or [0.0])}
    if hold == 'loose':
        return {'maxOffHandleMetres': max(r['leftHandOffHandleMetres'] for r in misses),
                'maxTiltDegrees': max(r['leftHandTiltDegrees'] for r in misses)} | wrist
    return {'maxMissMetres': max(r['leftHandMissMetres'] for r in misses),
            'maxMissDegrees': max(r['leftHandMissDegrees'] for r in misses),
            'maxRollDegrees': max(abs(r['leftHandRollDegrees']) for r in misses)} | wrist


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
