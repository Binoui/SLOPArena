"""Flip Kick, revision 1: flow pass on the approved keys.

The approved keys stay; the motion between them changes:
- body joints pass through the keys on Catmull-Rom tangents (moving holds) instead of stopping on each key;
- knees and elbows follow shaped hinge curves (straight kick held 20-44, right knee bends first after 44,
  left tuck keeps pulling in, legs open right first then left, elbows ease into the fall pose);
- the spine arches ahead of the turn (20-32), pikes as the leg goes over (32-44), curls into the tuck (44-58)
  and uncurls (58-72), each joint up the chain about a tick behind the one below;
- the arms trail (upper arm, forearm, hand), drift between keys, and the fingers curl with each swing;
- the settle after 80 gives once (hips, then chest, then head and arms) and eases into the fall pose.
"""
import numpy as np
from scipy.interpolate import PchipInterpolator
from scipy.spatial.transform import Rotation as Rot
from genra.brief import Brief
from genra.clip import Clip
import genra.curves as gc
import os
SHOULDER_GAIN = float(os.environ.get('SHOULDER_GAIN', 1))
from hinge import hinge_frame, flex

b = Brief('brief.json'); rig = b.rig
N = 97
names = rig.names; J = len(names); ix = names.index
K = Clip.load(rig, 'keys/result.json', frame=b.entry_frame)
T = [0, 12, 20, 32, 44, 58, 72, 80, 96]
keys = {t: K.pose(t) for t in T}
keys[96] = b.exit_pose.copy()
P = Clip.from_keys(keys, N)                    # pchip: shortest-path turns (sign continuity), digits
entry, exit_ = b.entry_pose, b.exit_pose
fall = b.reference('fall')
DIGITS = set(names[j] if isinstance(j, int) else j for j in rig.humanoid_digits)
HINGE = {'LeftKnee': 'LeftLeg', 'RightKnee': 'RightLeg', 'LeftElbow': 'LeftForeArm', 'RightElbow': 'RightForeArm'}

def continuous(q):
    q = q.copy()
    for t in range(1, len(q)):
        if np.dot(q[t], q[t - 1]) < 0:
            q[t] = -q[t]
    return q

def hermite(ts, vals, tans, n=N):
    ts = np.asarray(ts, float); out = np.zeros((n,) + vals.shape[1:])
    for t in range(n):
        if t <= ts[0]: out[t] = vals[0]; continue
        if t >= ts[-1]: out[t] = vals[-1]; continue
        i = np.searchsorted(ts, t, side='right') - 1
        h = ts[i + 1] - ts[i]; s = (t - ts[i]) / h
        h00 = 2*s**3 - 3*s**2 + 1; h10 = s**3 - 2*s**2 + s; h01 = -2*s**3 + 3*s**2; h11 = s**3 - s**2
        out[t] = h00*vals[i] + h10*h*tans[i] + h01*vals[i+1] + h11*h*tans[i+1]
    return out

def cr(ts, vals, tension=0.9, end1=None):
    ts = np.asarray(ts, float); m = np.zeros_like(vals)
    for i in range(1, len(ts) - 1):
        m[i] = tension * (vals[i+1] - vals[i-1]) / (ts[i+1] - ts[i-1])
    if end1 is not None: m[-1] = end1
    return hermite(ts, vals, m)

def curve(points, tension=1.0):
    """Smooth scalar curve through {tick: value} (Catmull-Rom), zero slope at the ends."""
    ts = sorted(points); v = np.array([points[t] for t in ts], float)[:, None]
    return cr(ts, v, tension)[:, 0]

def pchip(points, bounce=None):
    """Monotone curve through {tick: value}. `bounce` {tick: (slope in, slope out)} turns a hinge over crisply at that
    key instead of easing it to a dead stop (degrees per tick)."""
    ts = sorted(points); v = [points[t] for t in ts]
    f = PchipInterpolator(ts, v); d = f.derivative()
    mL = [float(d(t)) for t in ts]; mR = list(mL)
    for t, (a, c) in (bounce or {}).items():
        i = ts.index(t); mL[i], mR[i] = a, c
    out = np.zeros(N)
    for t in range(N):
        if t <= ts[0]: out[t] = v[0]; continue
        if t >= ts[-1]: out[t] = v[-1]; continue
        i = int(np.searchsorted(ts, t, side='right') - 1)
        h = ts[i + 1] - ts[i]; s = (t - ts[i]) / h
        out[t] = ((2*s**3 - 3*s**2 + 1) * v[i] + (s**3 - 2*s**2 + s) * h * mR[i] + (-2*s**3 + 3*s**2) * v[i + 1]
                  + (s**3 - s**2) * h * mL[i + 1])
    return out

# ---------------------------------------------------------------- body tracks through the keys
Q = np.zeros((N, J, 4))
for j, name in enumerate(names):
    qc = continuous(P.q[:, j])
    if name in DIGITS or name in HINGE.values():
        Q[:, j] = qc; continue
    vals = qc[T]
    f0, f1 = fall.q[0, j], fall.q[1, j] * np.sign(np.dot(fall.q[1, j], fall.q[0, j]) or 1)
    v_end = (f1 - f0) * np.sign(np.dot(vals[-1], f0) or 1) * 0.5     # arrive with a little of the fall's own motion
    d = cr(T, vals, 0.9, end1=v_end)
    Q[:, j] = d / np.linalg.norm(d, axis=1, keepdims=True)
Tr = P.t.copy(); hi = ix('Hips')
Tr[:, hi] = cr(T, P.t[T, hi], 0.9)

# ---------------------------------------------------------------- hinges
def hinge_track(h, points, bounce=None):
    """Drive a hinge by its flexion (degrees, as measured by hinge.flex) about the axis its approved keys turn on."""
    j = ix(HINGE[h])
    q0, ax0 = hinge_frame(entry, h)
    fl = np.array([flex(q0, ax0, K.q[t, j])[0] for t in T])
    ref = Rot.from_quat(K.q[0, j])
    far = int(np.argmax(np.abs(fl - fl[0])))
    ax = (ref.inv() * Rot.from_quat(K.q[T[far], j])).as_rotvec()
    ax = ax / np.linalg.norm(ax) * np.sign(fl[far] - fl[0])
    th = pchip(points, bounce)
    Q[:, j] = continuous(np.array([(ref * Rot.from_rotvec(ax * np.radians(a - fl[0]))).as_quat() for a in th]))
    return th

# Right knee: snaps straight at 20 and locks (it gives a degree, never bends), bends first after 44, opens at 70.
KNEE_R = {0: 45.3, 12: 47.0, 17: 12, 20: -5.0, 32: -4.3, 44: -3.6, 45: -1.5, 58: 126.9, 70: 26.0, 72: 27.4,
          80: 45.3, 88: 46.6, 96: 45.3}
# Left knee: the tuck keeps pulling in 20-52, it opens 2-3 ticks after the right (73.5).
KNEE_L = {0: 94.5, 12: 90.3, 20: 115.0, 32: 118.5, 44: 121.0, 52: 123.8, 58: 121.5, 72: 71.0, 73.5: 70.4, 80: 93.5,
          88: 96.0, 96: 94.5}
ELB_L = {0: 96.3, 12: 11.7, 20: 36.2, 32: 26.1, 44: 43.4, 58: 66.4, 72: 57.5, 80: 95.0, 86: 98.5, 96: 96.3}
ELB_R = {0: 81.1, 12: 11.8, 20: 29.3, 32: 27.0, 44: 38.0, 58: 57.9, 72: 52.2, 80: 80.0, 86: 83.5, 96: 81.1}
hinge_track('RightKnee', KNEE_R, {12: (0.5, -0.5), 20: (-1.5, 0.35), 70: (-0.6, 0.45), 88: (0.4, -0.4)})
hinge_track('LeftKnee', KNEE_L, {12: (-0.6, 0.6), 73.5: (-0.5, 0.5), 88: (0.4, -0.4)})
hinge_track('LeftElbow', ELB_L, {12: (-1.0, 1.0), 20: (0.6, -0.5), 32: (-0.5, 0.6), 58: (0.6, -0.5),
                                 72: (-0.5, 0.6), 86: (0.45, -0.4)})
hinge_track('RightElbow', ELB_R, {12: (-1.0, 1.0), 20: (0.6, -0.5), 32: (-0.5, 0.6), 58: (0.6, -0.5),
                                  72: (-0.5, 0.6), 86: (0.45, -0.4)})

# ---------------------------------------------------------------- offsets (degrees about a char axis at the entry)
R, F, U = [1, 0, 0], [0, 1, 0], [0, 0, 1]
def local_axis(joint, axis, pose=entry):
    M = np.stack([pose.axis(joint, e) for e in np.eye(3)], axis=1)   # columns: local axes in char
    v = M.T @ np.asarray(axis, float)
    return v / np.linalg.norm(v)
OFF = []   # (joint, local axis, dense degrees)
PROBES = np.random.default_rng(0).normal(size=(600, 3)); PROBES /= np.linalg.norm(PROBES, axis=1, keepdims=True)
def no_muscle_axis(joint, q=None):
    """The local axis the Humanoid has no muscle for (a collarbone's roll), found by probing small turns."""
    j = ix(joint); q = entry.local_rotation(joint) if q is None else Rot.from_quat(q)
    dirs = PROBES
    ex = np.array([rig.humanoid_excess(j, (q * Rot.from_rotvec(d * np.radians(4))).as_quat())[0] for d in dirs])
    return dirs[ex.argmax()]

def offset(joint, axis, deg):
    ax = local_axis(joint, axis)
    if joint.endswith('Shoulder'):         # a collarbone never rolls about its own length (no Humanoid muscle does)
        roll = no_muscle_axis(joint)
        ax = ax - np.dot(ax, roll) * roll; ax = ax / np.linalg.norm(ax)
    OFF.append((joint, ax, np.asarray(deg, float) * (SHOULDER_GAIN if joint.endswith('Shoulder') else 1)))

def swirl(segments, shift=0.0):
    """A secondary curve that is zero on each key and in the middle of each segment, steepest at the middle: added
    on a second axis it turns a bump's out-and-back into an arc, so the joint never stops at the bump's peak."""
    out = np.zeros(N)
    for (a, c), amp in segments.items():
        for t in range(N):
            s_ = (t - shift - a) / (c - a)
            if 0 < s_ < 1:
                out[t] += amp * np.sin(2 * np.pi * s_)
    return out

# Spine: arch back ahead of the turn, pike as the leg passes over, curl into the tuck, uncurl; each joint a tick
# behind the one below. + leans back (extends), - curls forward.
CHAIN = [('Spine', 1.0), ('Spine1', 1.0), ('Spine2', 0.8), ('Neck', 1.0), ('Head', 0.6)]
for i, (joint, w) in enumerate(CHAIN):
    s = i * 1.0
    chin = 1.6 if joint in ('Neck', 'Head') else 1.0
    pts = {0: 0, 12: 0, 20: 0,
           24 + s * 0.8: 4.5 * w, 28 + s * 0.7: 3.0 * w, 32: 0,
           37 + s * 0.6: -2.0 * w, 44: 0,
           49 + s * 0.8: -4.5 * w * chin, 54 + s * 0.6: -4.0 * w * chin, 58: 0,
           62 + s * 0.7: 3.5 * w, 67 + s * 0.6: 2.5 * w, 72: 0,
           76: 0, 80: 0, 84.5 + s * 0.9: -2.5 * w, 93 + s * 0.3: -0.4 * w, 96: 0}
    offset(joint, R, curve(pts))
    offset(joint, F, swirl({(20, 32): 1.2, (32, 44): -1.0, (44, 58): 1.2, (58, 72): -1.0, (80, 93): 0.8}, shift=s))

# Settle: the hips carry the flip a few degrees past upright first (83), the chest gives after them (spine chain
# above), the head and arms last; then everything eases into the fall pose.
offset('Hips', R, curve({0: 0, 76: 0, 80: 0, 83: 3.0, 89: 1.0, 96: 0}))
offset('Hips', F, swirl({(80, 92): 0.8}))

# The free arms drift between keys (out and round, for balance), never parked; each drift is a small loop.
for side, sgn in (('Left', -1), ('Right', 1)):
    offset(f'{side}Arm', F, curve({0: 0, 12: 0, 20: 0, 26: 5 * sgn, 32: 0, 38: -5 * sgn, 44: 0, 51: 4 * sgn, 58: 0,
                                   65: -4 * sgn, 72: 0, 80: 0, 96: 0}))
    offset(f'{side}Arm', R, swirl({(20, 32): -4, (32, 44): 4, (44, 58): -3, (58, 72): 3, (80, 93): 2.0}))
    offset(f'{side}Shoulder', F, curve({0: 0, 12: 0, 20: 0, 26: 3 * sgn, 32: 0, 39: -3 * sgn, 44: 0, 52: 3 * sgn,
                                        58: 0, 66: -3 * sgn, 72: 0, 80: 0, 85: 3 * sgn, 96: 0}))
    offset(f'{side}Shoulder', U, swirl({(20, 32): 2, (32, 44): -2, (44, 58): 2, (58, 72): -2, (80, 96): 1.5}))
    offset(f'{side}Hand', R, curve({0: 0, 12: 0, 20: 0, 27: 6, 32: 0, 39: -6, 44: 0, 51: 6, 58: 0, 65: -6, 72: 0,
                                    80: 0, 86: 3, 92: 0.6, 96: 0}))
    offset(f'{side}Hand', F, swirl({(20, 32): 4 * sgn, (32, 44): -4 * sgn, (44, 58): 4 * sgn, (58, 72): -4 * sgn,
                                    (80, 93): 2 * sgn}))
# Kicking foot: points harder as it leads the leg up, eases as it goes behind.
offset('RightFoot', R, curve({0: 0, 20: 0, 26: -5, 32: 0, 38: 4, 44: 0, 96: 0}))
offset('RightFoot', F, swirl({(20, 32): 2, (32, 44): -2}))
offset('LeftFoot', R, curve({0: 0, 20: 0, 27: -3, 32: 0, 38: 3, 44: 0, 51: -3, 58: 0, 96: 0}))
offset('LeftFoot', F, swirl({(20, 32): 2, (32, 44): -2, (44, 58): 2}))
offset('LeftUpLeg', R, curve({0: 0, 20: 0, 26: 3, 32: 0, 38: 3, 44: 0, 50: 4, 58: 0, 96: 0}))
offset('LeftUpLeg', U, swirl({(20, 32): 2, (32, 44): -2, (44, 58): 2}))

def apply_offsets(Q):
    Q = Q.copy()
    for joint, ax, deg in OFF:
        j = ix(joint)
        for t in range(N):
            if abs(deg[t]) > 1e-9:
                a = ax
                if joint.endswith('Shoulder'):
                    roll = no_muscle_axis(joint, Q[t, j]); a = ax - np.dot(ax, roll) * roll; a = a / np.linalg.norm(a)
                Q[t, j] = (Rot.from_quat(Q[t, j]) * Rot.from_rotvec(a * np.radians(deg[t]))).as_quat()
    return Q
Q = apply_offsets(Q)

# ---------------------------------------------------------------- lags (ticks; negative leads)
def ramp_profile(points):
    return curve(points)

def between_keys(peak, at=None):
    """A lag that peaks between the keys and is back to zero on each key, so every key still lands."""
    pts = {t: 0.0 for t in T}
    for a, c in zip(T[:-1], T[1:]):
        pts[(a + c) / 2] = peak * (at or {}).get(a, 1.0)
    del pts[88.0]                      # the settle: trail early, then let go well before the fall pose
    pts[86], pts[92] = peak * (at or {}).get(80, 1.0), 0.15 * peak
    return curve(pts)

SEG = {0: 0.5, 12: 0.5, 80: 0.5}     # less lag in the gather and the swing out of it, more in the settle
LAG = {}
for joint, d in (('Spine', 0.5), ('Spine1', 1.0), ('Spine2', 1.0), ('Neck', 1.3), ('Head', 1.5)):
    LAG[joint] = between_keys(d, at=SEG)
for side in ('Left', 'Right'):
    LAG[f'{side}Shoulder'] = between_keys(0.6, at=SEG)
    LAG[f'{side}Arm'] = between_keys(1.0, at=SEG)
    LAG[f'{side}ForeArm'] = between_keys(1.8, at=SEG)
    LAG[f'{side}Hand'] = between_keys(2.6, at=SEG)
# Legs open on the way out: right first, left 2-3 ticks later (the knees carry it: KNEE_R, KNEE_L).
for joint in ('RightUpLeg', 'RightLeg', 'RightFoot'):
    LAG[joint] = ramp_profile({0: 0, 60: 0, 66: -0.6, 71: 0, 96: 0})
for joint in ('LeftUpLeg', 'LeftFoot'):
    LAG[joint] = ramp_profile({0: 0, 60: 0, 67: 0.6, 72: 0.3, 78: 0, 96: 0})

def slerp_at(qs, tau):
    i = int(np.clip(np.floor(tau), 0, N - 2)); f = float(np.clip(tau - i, 0, 1))
    a, c = qs[i], qs[i + 1]
    if np.dot(a, c) < 0: c = -c
    if f == 0: return a
    r = Rot.from_quat(a); d = (r.inv() * Rot.from_quat(c)).as_rotvec()
    return (r * Rot.from_rotvec(d * f)).as_quat()

def apply_lags(Q):
    out = Q.copy()
    for joint, d in LAG.items():
        j = ix(joint)
        for t in range(N):
            out[t, j] = slerp_at(Q[:, j], np.clip(t - d[t], 0, N - 1))
    return out
Q = apply_lags(Q)

def clean_collarbones(Q):
    """Turn out the small roll that composing two muscle-axis turns leaves on a collarbone."""
    from scipy.optimize import minimize_scalar
    for joint in ('LeftShoulder', 'RightShoulder'):
        j = ix(joint)
        for t in range(1, N - 1):
            if rig.humanoid_excess(j, Q[t, j])[0] < 0.02:
                continue
            roll = no_muscle_axis(joint, Q[t, j]); r = Rot.from_quat(Q[t, j])
            fix = lambda a: (r * Rot.from_rotvec(roll * np.radians(a))).as_quat()
            a = minimize_scalar(lambda a: rig.humanoid_excess(j, fix(a))[0], bounds=(-4, 4), method='bounded').x
            Q[t, j] = fix(a)
    return Q
Q = clean_collarbones(Q)

# ---------------------------------------------------------------- fingers loosen and curl with the arm swings
def finger_curl(Q):
    ang = np.zeros((N, 2))
    for k, side in enumerate(('Left', 'Right')):
        j = ix(f'{side}Arm')
        sp = np.r_[0, np.degrees(2 * np.arccos(np.abs(np.sum(Q[1:, j] * Q[:-1, j], axis=1)).clip(0, 1)))]
        sp = np.convolve(sp, np.ones(5) / 5, mode='same')
        lagged = np.r_[np.zeros(2), sp[:-2]]                       # fingers trail the arm by two ticks
        ang[:, k] = np.clip(lagged * 1.2, 0, 10)                     # up to 10 degrees of extra curl per joint
    w = np.ones(N); w[:4] = np.linspace(0, 1, 4); w[-8:] = np.linspace(1, 0, 8)
    return ang * w[:, None]

# ---------------------------------------------------------------- assemble
def assemble(Q):
    c = Clip(rig, Tr.copy(), Q.copy(), P.s.copy(), frame=P.frame)
    curl = finger_curl(Q)
    for t in range(1, N - 1):
        p = c.pose(t)
        for k, side in enumerate(('Left', 'Right')):
            for joint in (f'{side}HandIndex1', f'{side}HandIndex2', f'{side}HandIndex3'):
                p.rotate(joint, p.axis(joint, local_axis_digit[joint]), curl[t, k])
            p.rotate(f'{side}HandThumb2', p.axis(f'{side}HandThumb2', local_axis_digit[f'{side}HandThumb2']),
                     curl[t, k] * 0.6)
        p.snap_to_humanoid()
        c.set_pose(t, p)
    c.set_pose(0, entry.copy()); c.set_pose(N - 1, exit_.copy())
    return c

# the local axis each digit closes about (from fist())
from genra.solve import fist
local_axis_digit = {}
for side in ('Left', 'Right'):
    a = entry.copy(); fist(a, side, amount=1.0, thumb=1.0)
    for joint in (f'{side}HandIndex1', f'{side}HandIndex2', f'{side}HandIndex3', f'{side}HandThumb2'):
        rv = (entry.local_rotation(joint).inv() * a.local_rotation(joint)).as_rotvec()
        local_axis_digit[joint] = rv / (np.linalg.norm(rv) + 1e-12)

def key_report(c):
    pos = np.stack([c.positions(n) for n in names], axis=1)
    kp = np.stack([K.positions(n) for n in names], axis=1)
    body = [j for j, n in enumerate(names) if n not in DIGITS and n != 'Armature']
    out = {}
    for t in T[1:-1]:
        near = pos[max(0, t - 2):t + 3][:, body]
        gap = np.linalg.norm(near - kp[t, body][None], axis=2).min(axis=0)
        out[t] = (round(float(gap.max()) * 100, 2), names[body[int(gap.argmax())]])
    return out

if __name__ == '__main__':
    clip = assemble(Q)
    clip.save('result.json')
    print('keys cm', key_report(clip))
    print({k: round(v, 3) for k, v in gc.flow(clip, (3, 95)).items()})
