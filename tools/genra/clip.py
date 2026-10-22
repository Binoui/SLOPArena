"""Poses, the fixed character frame, keyframed clips and interpolation.

A Pose holds every joint's local channels (translation, quaternion, scale) in UsdSkel
order. A Clip is a dense pose per tick. Authoring usually blocks a few key poses,
then interpolates between them (``Clip.from_keys``).
"""
from __future__ import annotations

import json
import tempfile
from dataclasses import dataclass
from pathlib import Path

import numpy as np
from scipy.interpolate import CubicHermiteSpline, CubicSpline, PchipInterpolator
from scipy.spatial.transform import Rotation, Slerp

from .rig import Rig, rotation_part, unit


@dataclass(frozen=True)
class CharFrame:
    """Fixed authoring frame: origin at the entry hips, axes right/front/up (world up)."""
    origin: np.ndarray
    right: np.ndarray
    front: np.ndarray
    up: np.ndarray

    @classmethod
    def from_pose(cls, pose: 'Pose', facing: bool = True) -> 'CharFrame':
        """Front is the fighter's facing when the rig knows it (Unity rig packs): the direction the game moves
        and attacks along and the match camera looks along. Fighting stances turn the hips 40-90 degrees away from
        it (Bonk's idle 90, Wibou's 77), so the hip line only sets front for a rig without a facing, or with
        facing=False (briefs with "frame": "hips", the convention before 2026-10-18)."""
        rig, world = pose.rig, pose.world()
        up = rig.up
        origin = world[rig.index('Hips'), 3, :3].copy()
        forward = rig.facing if facing else None
        if forward is not None:
            front = unit(forward - up * (forward @ up))
            return cls(origin, np.cross(front, up), front, up)
        right = world[rig.index('RightUpLeg'), 3, :3] - world[rig.index('LeftUpLeg'), 3, :3]
        right = unit(right - up * (right @ up))
        return cls(origin, right, np.cross(up, right), up)

    @property
    def basis(self) -> np.ndarray:
        """Rows right, front, up: char = (world - origin) @ basis.T."""
        return np.stack([self.right, self.front, self.up])

    def to_char(self, world_point) -> np.ndarray:
        return (np.asarray(world_point, float) - self.origin) @ self.basis.T

    def to_world(self, char_point) -> np.ndarray:
        return np.asarray(char_point, float) @ self.basis + self.origin

    def dir_to_world(self, char_dir) -> np.ndarray:
        return unit(np.asarray(char_dir, float) @ self.basis)

    def dir_to_char(self, world_dir) -> np.ndarray:
        return np.asarray(world_dir, float) @ self.basis.T


class Pose:
    def __init__(self, rig: Rig, t, q, s, frame: CharFrame | None = None):
        self.rig = rig
        self.t, self.q, self.s = (np.array(x, float) for x in (t, q, s))
        self.frame = frame
        self._world = None

    def copy(self) -> 'Pose':
        return Pose(self.rig, self.t, self.q, self.s, self.frame)

    def _changed(self):
        self._world = None

    def world(self) -> np.ndarray:
        if self._world is None:
            self._world = self.rig.fk(self.t, self.q, self.s)
        return self._world

    # ----- queries (char-frame metres unless noted) ---------------------------
    def pos(self, joint) -> np.ndarray:
        """Joint position in the character frame (right, front, up) relative to the entry hips."""
        return self.frame.to_char(self.world()[self.rig.index(joint), 3, :3])

    def world_pos(self, joint) -> np.ndarray:
        return self.world()[self.rig.index(joint), 3, :3].copy()

    def axis(self, joint, local_axis) -> np.ndarray:
        """A joint-local direction expressed in the character frame."""
        return self.frame.dir_to_char(np.asarray(local_axis, float) @ rotation_part(self.world()[self.rig.index(joint)]))

    def palm_normal(self, side) -> np.ndarray:
        return self.axis(side + 'Hand', self.rig.palm_axis_local(side))

    def describe(self, joints=None, decimals=3) -> dict:
        joints = joints or ['Hips', 'Spine2', 'Head', 'LeftHand', 'RightHand', 'LeftForeArm', 'RightForeArm',
                            'LeftLeg', 'RightLeg', 'LeftFoot', 'RightFoot']
        return {j: np.round(self.pos(j), decimals).tolist() for j in joints}

    # ----- edits --------------------------------------------------------------
    def local_rotation(self, joint) -> Rotation:
        return Rotation.from_quat(self.q[self.rig.index(joint)])

    def set_local_rotation(self, joint, rotation: Rotation) -> 'Pose':
        self.q[self.rig.index(joint)] = rotation.as_quat()
        self._changed()
        return self

    def rotate(self, joint, axis, degrees, frame='char') -> 'Pose':
        """Rotate a joint (its subtree follows) about an axis through the joint.

        frame='char' takes (right, front, up) components; 'world' takes world
        components; 'local' takes the joint's own local axes.
        """
        j = self.rig.index(joint)
        world = self.world()
        if frame == 'local':
            axis_world = np.asarray(axis, float) @ rotation_part(world[j])
        elif frame == 'char':
            axis_world = self.frame.dir_to_world(axis)
        else:
            axis_world = unit(axis)
        delta = Rotation.from_rotvec(np.radians(degrees) * unit(axis_world)).as_matrix()  # column convention
        return self._apply_world_delta(j, delta)

    def _apply_world_delta(self, j, delta_col) -> 'Pose':
        world = self.world()
        own = rotation_part(world[j])
        parent = rotation_part(world[self.rig.parents[j]]) if self.rig.parents[j] >= 0 else rotation_part(self.rig.skel_to_world)
        new_world = own @ delta_col.T
        local_row = new_world @ parent.T
        self.q[j] = Rotation.from_matrix(local_row.T).as_quat()
        self._changed()
        return self

    def aim(self, joint, child, direction, frame='char') -> 'Pose':
        """Rotate `joint` minimally so the joint->child segment points along `direction`."""
        j, c = self.rig.index(joint), self.rig.index(child)
        world = self.world()
        current = unit(world[c, 3, :3] - world[j, 3, :3])
        target = self.frame.dir_to_world(direction) if frame == 'char' else unit(direction)
        rot, _ = Rotation.align_vectors([target], [current])
        return self._apply_world_delta(j, rot.as_matrix())

    def set_hinge(self, name, degrees) -> 'Pose':
        """Set a hinge (e.g. 'LeftKnee', 'RightElbow', 'LeftIndexMiddle') to `degrees` of correct flexion from bind."""
        _, a, m, d, axis = self.rig.hinges[name]
        rest = Rotation.from_quat(self.rig.bind_q[m])
        bend = Rotation.from_rotvec(np.radians(degrees) * axis)
        # Row convention: new = rest_row @ bend_row  <=>  column: bend_col @ rest_col.
        self.q[m] = (bend * rest).as_quat()
        if m in self.rig.humanoid_digits:  # the bind pose itself is off Unity's finger axes
            return self.snap_to_humanoid([self.rig.names[m]])
        self._changed()
        return self

    def move_hips(self, offset) -> 'Pose':
        """Shift the hips by a char-frame offset in metres (crouch, lunge, weight shift). Everything above
        the feet moves with them, so re-solve planted feet with two_bone. The fighter itself stays in place."""
        j = self.rig.index('Hips')
        parent = self.world()[self.rig.parents[j]] if self.rig.parents[j] >= 0 else self.rig.skel_to_world
        self.t[j] += (np.asarray(offset, float) @ self.frame.basis) @ np.linalg.inv(parent[:3, :3])
        self._changed()
        return self

    def snap_to_humanoid(self, joints=None) -> 'Pose':
        """Project finger, thumb and toe rotations onto what Unity's Humanoid can play: rotations about each
        joint's own muscle axes. fist() does this; call it after editing digits freely (check_clip flags the rest)."""
        rig = self.rig
        for j in ([rig.index(n) for n in joints] if joints else rig.humanoid_digits):
            if j in rig.humanoid:
                angles, _ = rig.humanoid_split(j, self.q[j])
                axes = np.array([m['axis'] for m in rig.humanoid[j]])
                self.q[j] = (Rotation.from_rotvec(axes.T @ angles) * rig.humanoid[j][0]['zero']).as_quat()
        self._changed()
        return self

    def to_dict(self) -> dict:
        return {'t': self.t.tolist(), 'q': self.q.tolist(), 's': self.s.tolist()}


class Clip:
    """Dense per-tick local channels for every joint."""

    def __init__(self, rig: Rig, t, q, s, frame: CharFrame | None = None):
        self.rig = rig
        self.t, self.q, self.s = (np.array(x, float) for x in (t, q, s))
        self.frame = frame
        self._world = None

    @classmethod
    def from_reference(cls, rig: Rig, ticks=None, frame=None) -> 'Clip':
        a, b = rig.animated_range()
        t, q, s = rig.sample_local(ticks if ticks is not None else range(a, b + 1))
        clip = cls(rig, t, q, s, frame)
        if clip.frame is None:
            clip.frame = CharFrame.from_pose(clip.pose(0))
        return clip

    @classmethod
    def from_keys(cls, keys: dict, n_ticks: int, method='pchip', ease=None, cycle=None) -> 'Clip':
        """Interpolate key poses {tick: Pose} to `n_ticks` dense ticks (0..n_ticks-1).

        method: 'pchip' (monotone cubic on rotation vectors relative to each joint's
        first key: smooth, no overshoot; between two keys a joint turns the shorter way, so
        spins and flips need a key at least every half turn) or 'slerp' (per segment,
        optional ease).
        ease: None/'linear', 'in', 'out', 'inout', or {tick: name} for the segment
        starting at that key (slerp only). Translations (the hips) follow the same
        method; scales are interpolated linearly. Ticks outside the keyed range hold
        the nearest key.
        cycle: (first, last) keyed ticks of a seamless loop (pchip only). Key the same pose
        at both; the keys between them follow a periodic cubic spline, so the motion keeps
        its speed across the seam when the game wraps from `last` back to `first` (it may
        overshoot a little between keys). Keys before `first` (a build-up) arrive at `first`
        with the loop's velocity. A cycle cannot contain a full turn.
        """
        ticks = sorted(int(k) for k in keys)
        if cycle is not None:
            cycle = (int(cycle[0]), int(cycle[1]))
            if method != 'pchip':
                raise ValueError("cycle needs method='pchip'")
            if cycle[0] not in keys or cycle[1] not in keys or cycle[1] - cycle[0] < 2:
                raise ValueError(f'cycle {cycle}: key the same pose at both ticks, at least 2 ticks apart')
            if ticks[-1] > cycle[1]:
                raise ValueError(f'cycle {cycle}: no keys after the loop (tick {ticks[-1]})')
        first = keys[ticks[0]]
        rig = first.rig
        poses = [keys[k] for k in ticks]
        dense = np.arange(n_ticks)
        clamped = np.clip(dense, ticks[0], ticks[-1])
        n = len(rig.names)
        t = np.empty((n_ticks, n, 3)); s = np.empty((n_ticks, n, 3)); q = np.empty((n_ticks, n, 4))
        for j in range(n):
            values = np.array([p.t[j] for p in poses])
            if len(ticks) == 1 or np.ptp(values, axis=0).max() < 1e-12:
                t[:, j] = values[0]
            elif cycle is not None:
                t[:, j] = _cyclic(ticks, values, dense, cycle, f'{rig.names[j]} translation')
            elif method == 'pchip':
                t[:, j] = PchipInterpolator(ticks, values, axis=0)(clamped)
            else:
                t[:, j] = _lerp(ticks, values, clamped, ease)
            for c in range(3):
                s[:, j, c] = np.interp(clamped, ticks, [p.s[j, c] for p in poses])
        for j in range(n):
            rots = Rotation.from_quat([p.q[j] for p in poses])
            if len(ticks) == 1:
                q[:, j] = rots[0].as_quat()
            elif method == 'pchip':
                base = rots[0]
                vec = _continuous_rotvecs((base.inv() * rots).as_rotvec())
                curve = (_cyclic(ticks, vec, dense, cycle, f'{rig.names[j]} rotation') if cycle is not None
                         else PchipInterpolator(ticks, vec, axis=0)(clamped))
                q[:, j] = (base * Rotation.from_rotvec(curve)).as_quat()
            else:
                q[:, j] = _slerp(ticks, rots, clamped, ease)
        return cls(rig, t, q, s, first.frame)

    def __len__(self):
        return len(self.q)

    def pose(self, tick) -> Pose:
        return Pose(self.rig, self.t[tick], self.q[tick], self.s[tick], self.frame)

    def set_pose(self, tick, pose: Pose) -> 'Clip':
        self.t[tick], self.q[tick], self.s[tick] = pose.t, pose.q, pose.s
        self._world = None
        return self

    def world(self) -> np.ndarray:
        if self._world is None:
            self._world = self.rig.fk(self.t, self.q, self.s)
        return self._world

    def positions(self, joint) -> np.ndarray:
        """Per-tick char-frame positions (n_ticks, 3) of a joint."""
        return self.frame.to_char(self.world()[:, self.rig.index(joint), 3, :3])

    def overlap(self, delays: dict, cycle=None) -> 'Clip':
        """Follow-through: a copy in which each listed joint's local rotation plays `delays[joint]` ticks late
        (fractions interpolate), so it trails its parent instead of moving with it in one piece. Typical delays:
        forearm 1-2 and hand 2-3 behind the upper arm; neck 1-2 and head 2-3 behind the chest; a hand that holds a
        weapon 2-4, so the weapon bobs after the body. Delays add up along a chain, because each joint's rotation is
        relative to its parent.
        cycle=(first, last) for a loop: the delay wraps inside the cycle, so the seam stays closed. Otherwise the
        first tick holds while the delayed motion starts. Translations (the hips) keep their path. Re-check planted
        feet, held weapons and clearances afterwards."""
        out = Clip(self.rig, self.t.copy(), self.q.copy(), self.s.copy(), self.frame)
        n = len(self)
        for joint, delay in delays.items():
            j = self.rig.index(joint)
            src = self.q[:, j].copy()
            for tick in range(n):
                if cycle is not None and cycle[0] <= tick <= cycle[1]:
                    period = cycle[1] - cycle[0]
                    x = cycle[0] + (tick - delay - cycle[0]) % period
                else:
                    x = max(0.0, tick - delay)
                a = int(np.floor(x))
                b = min(a + 1, n - 1)
                f = float(x - a)
                if f < 1e-9 or a == b:
                    out.q[tick, j] = src[a]
                    continue
                out.q[tick, j] = Slerp([0.0, 1.0], Rotation.from_quat([src[a], src[b]]))([f]).as_quat()[0]
        out._world = None
        return out

    def segment(self, first: int, last: int) -> 'Clip':
        """Ticks first..last as a clip of their own: one phase of a move the game plays as several clips (a leap's
        flight, then its landing)."""
        return Clip(self.rig, self.t[first:last + 1], self.q[first:last + 1], self.s[first:last + 1], self.frame)

    def retime(self, anchors) -> 'Clip':
        """A time-warped copy. `anchors` are (source tick, new tick) pairs, both increasing, the first new tick 0;
        time runs linearly between them, so one section can play slower (more new ticks than source ticks) while
        another keeps its speed: a slam stays snappy while the haul after it gets room. The last anchor sets the
        length. Rotations slerp; translations and scales lerp between neighbouring source ticks."""
        src = np.array([a[0] for a in anchors], float)
        dst = np.array([a[1] for a in anchors], float)
        if dst[0] != 0 or (np.diff(src) <= 0).any() or (np.diff(dst) <= 0).any() or src[-1] > len(self) - 1:
            raise ValueError('anchors must increase, start at new tick 0 and stay inside the clip')
        times = np.interp(np.arange(int(dst[-1]) + 1), dst, src)
        a = np.floor(times).astype(int)
        b = np.minimum(a + 1, len(self) - 1)
        f = (times - a)[:, None, None]
        q0, q1 = self.q[a], self.q[b]
        dot = np.sum(q0 * q1, axis=-1, keepdims=True)
        q1 = np.where(dot < 0, -q1, q1)
        theta = np.arccos(np.clip(np.abs(dot), 0, 1))
        sin = np.where(theta < 1e-6, 1.0, np.sin(theta))
        w0 = np.where(theta < 1e-6, 1 - f, np.sin((1 - f) * theta) / sin)
        w1 = np.where(theta < 1e-6, f, np.sin(f * theta) / sin)
        q = w0 * q0 + w1 * q1
        q /= np.linalg.norm(q, axis=-1, keepdims=True)
        lerp = lambda x: x[a] * (1 - f) + x[b] * f
        return Clip(self.rig, lerp(self.t), q, lerp(self.s), self.frame)

    def angular_speed(self) -> np.ndarray:
        """Per tick (from 1) and joint: local rotation change in degrees per tick."""
        a, b = self.q[1:], self.q[:-1]
        dots = np.abs(np.sum(a * b, axis=-1)).clip(0, 1)
        return np.degrees(2 * np.arccos(dots))

    def save(self, path) -> Path:
        """Atomically replace the dense clip, preserving the last save on interruption."""
        path = Path(path)
        temporary = None
        try:
            with tempfile.NamedTemporaryFile(mode='w', dir=path.parent, prefix=f'.{path.name}.',
                                             suffix='.tmp', delete=False) as stream:
                temporary = Path(stream.name)
                json.dump({'format': 'genra-clip-1', 'joints': self.rig.names, 'fps': self.rig.fps,
                           't': np.round(self.t, 7).tolist(), 'q': np.round(self.q, 9).tolist(),
                           's': np.round(self.s, 7).tolist()}, stream, allow_nan=False)
            temporary.replace(path)
        finally:
            if temporary is not None:
                temporary.unlink(missing_ok=True)
        return path

    @classmethod
    def load(cls, rig: Rig, path, frame=None) -> 'Clip':
        """A saved clip on `rig`. A clip saved before a held prop joined the pack (unity_rigpack.py --prop) loads with
        that prop's fixed joints (Weapon<k> and its anchors) at their bind locals; any other difference is another rig."""
        data = json.loads(Path(path).read_text())
        if data.get('format') != 'genra-clip-1':
            raise ValueError('Not a genra-clip-1 file for this rig')
        t, q, s = (np.array(data[k], float) for k in ('t', 'q', 's'))
        if data['joints'] != rig.names:
            known = {n: k for k, n in enumerate(data['joints'])}
            props = {d for j, n in enumerate(rig.names) if n.startswith('Weapon') for d in rig.descendants(j)}
            if not set(known) <= set(rig.names) or any(j not in props for j, n in enumerate(rig.names) if n not in known):
                raise ValueError('Not a genra-clip-1 file for this rig')

            def fill(values, bind):
                out = np.repeat(np.asarray(bind, float)[None], len(values), axis=0)
                for j, n in enumerate(rig.names):
                    if n in known:
                        out[:, j] = values[:, known[n]]
                return out
            t, q, s = fill(t, rig.bind_t), fill(q, rig.bind_q), fill(s, rig.bind_s)
        clip = cls(rig, t, q, s, frame)
        clip.q /= np.linalg.norm(clip.q, axis=-1, keepdims=True)
        if clip.frame is None:
            clip.frame = CharFrame.from_pose(clip.pose(0))
        return clip


EASES = {
    None: lambda u: u, 'linear': lambda u: u,
    'in': lambda u: u * u, 'out': lambda u: 1 - (1 - u) ** 2,
    'inout': lambda u: u * u * (3 - 2 * u),
}


def _segment(ticks, x, ease):
    """(segment index, eased 0..1 position) of dense tick x between keys."""
    k = max(0, min(np.searchsorted(ticks, x, side='right') - 1, len(ticks) - 2))
    a, b = ticks[k], ticks[k + 1]
    u = 0.0 if b == a else (x - a) / (b - a)
    name = ease.get(a) if isinstance(ease, dict) else ease
    return k, EASES[name](float(np.clip(u, 0, 1)))


def _lerp(ticks, values, dense, ease):
    out = np.empty((len(dense), values.shape[1]))
    for i, x in enumerate(dense):
        k, u = _segment(ticks, x, ease)
        out[i] = values[k] + (values[k + 1] - values[k]) * u
    return out


def _cyclic(ticks, values, dense, cycle, what):
    """One channel with a seamless loop over cycle = (first, last): periodic cubic between those keys,
    monotone cubic before `first` that arrives with the loop's velocity, held after `last`."""
    first, last = cycle
    inside = [i for i, tick in enumerate(ticks) if first <= tick <= last]
    if np.abs(values[inside[-1]] - values[inside[0]]).max() > 1e-6:
        raise ValueError(f'cycle {cycle}: {what} differs between the two ends; key the same pose at both '
                         'and keep full turns out of the loop')
    loop_values = values[inside].copy()
    loop_values[-1] = loop_values[0]
    loop_ticks = [ticks[i] for i in inside]
    if len(inside) == 2:
        loop_ticks.insert(1, (first + last) / 2)  # two equal keys: hold the pose
        loop_values = np.insert(loop_values, 1, loop_values[0], axis=0)
    periodic = CubicSpline(loop_ticks, loop_values, axis=0, bc_type='periodic')
    out = np.empty((len(dense),) + values.shape[1:])
    looped = (dense >= first) & (dense <= last)
    out[looped] = periodic(dense[looped])
    out[dense > last] = loop_values[0]
    before = [i for i, tick in enumerate(ticks) if tick <= first]
    early = dense < first
    if len(before) >= 2 and early.any():
        build_ticks = [ticks[i] for i in before]
        slopes = PchipInterpolator(build_ticks, values[before], axis=0).derivative()(build_ticks)
        slopes[-1] = periodic.derivative()(first)
        build = CubicHermiteSpline(build_ticks, values[before], slopes, axis=0)
        out[early] = build(np.clip(dense[early], build_ticks[0], first))
    elif early.any():
        out[early] = values[before[0]]
    return out


def _continuous_rotvecs(vec):
    """Rotation vectors (angle times axis) -> the equivalent ones (angle + 2*pi*k about the same axis)
    closest to the previous key's. A rotation vector wraps at half a turn, so a spin keyed at 165 and
    195 degrees would otherwise spline back through zero; with this a joint turns from key to key the
    shorter way, for any number of turns."""
    out = np.array(vec, dtype=float)
    for i in range(1, len(out)):
        prev, v = out[i - 1], out[i]
        angle = np.linalg.norm(v)
        if angle > 1e-9:
            axis = v / angle
        elif np.linalg.norm(prev) > 1e-9:
            axis = prev / np.linalg.norm(prev)  # identity: full turns about the previous axis
        else:
            continue
        k = np.round((prev @ axis - angle) / (2 * np.pi))
        out[i] = min((v + 2 * np.pi * (k + d) * axis for d in (-1, 0, 1)), key=lambda c: np.linalg.norm(c - prev))
    return out


def _slerp(ticks, rots, dense, ease):
    out = np.empty((len(dense), 4))
    for i, x in enumerate(dense):
        k, u = _segment(ticks, x, ease)
        out[i] = Slerp([0, 1], Rotation.concatenate([rots[k], rots[k + 1]]))([u])[0].as_quat()
    return out
