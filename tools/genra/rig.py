"""Skeleton, forward kinematics, body frames and anatomy from a USD rig pack.

Conventions follow UsdSkel: joint-local transforms are (translation, rotation,
scale) composed as scale * rotate * translate with row vectors, skeleton space is
local @ parent, and world space is skeleton space @ the skeleton prim's transform.
Rotations are scipy-ordered quaternions (x, y, z, w). Joints are addressed by
short names: ``mixamorig_LeftHand`` -> ``LeftHand``; ``tail_1`` stays ``tail_1``.
"""
from __future__ import annotations

import json
from functools import cached_property
from pathlib import Path

import numpy as np
from pxr import Usd, UsdGeom, UsdSkel
from scipy.spatial.transform import Rotation

FINGERS = ('Index', 'Middle', 'Ring', 'Little', 'Pinky')
# Humanoid joints play only within their muscles. Approved clips reach about 2.5 times the default muscle ranges
# (head nods, chest, shoulder shrugs); Unity folds a twist back past 90 degrees and a swing past 180.
HUMANOID_REACH = 2.5
HUMANOID_TWIST_LIMIT = 85.0
HUMANOID_SWING_LIMIT = 170.0


def short_name(path: str) -> str:
    leaf = str(path).rsplit('/', 1)[-1].split(':')[-1]
    return leaf[len('mixamorig_'):] if leaf.startswith('mixamorig_') else leaf


def unit(v):
    v = np.asarray(v, float)
    return v / np.linalg.norm(v, axis=-1, keepdims=True)


def rotation_part(matrix) -> np.ndarray:
    """Nearest orthonormal row-convention rotation of a 4x4 or 3x3 (strips scale)."""
    u, _, vt = np.linalg.svd(np.asarray(matrix)[..., :3, :3])
    return u @ vt


def local_matrices(t, q, s) -> np.ndarray:
    """(..., N, 4, 4) row-convention local transforms: scale * rotate * translate."""
    t, q, s = (np.asarray(x, float) for x in (t, q, s))
    lead = q.shape[:-1]
    rot = Rotation.from_quat(q.reshape(-1, 4)).as_matrix().reshape(*lead, 3, 3)
    out = np.zeros(lead + (4, 4))
    out[..., :3, :3] = s[..., :, None] * np.swapaxes(rot, -1, -2)
    out[..., 3, :3] = t
    out[..., 3, 3] = 1
    return out


class Rig:
    """A skinned skeleton loaded from a USD file (skeleton, meshes, optional animation)."""

    def __init__(self, path):
        self.path = Path(path)
        self.stage = Usd.Stage.Open(str(self.path))
        skel_prim = next(p for p in self.stage.Traverse() if p.IsA(UsdSkel.Skeleton))
        self.skeleton = UsdSkel.Skeleton(skel_prim)
        self.query = UsdSkel.Cache().GetSkelQuery(self.skeleton)
        self.joint_paths = [str(p) for p in self.skeleton.GetJointsAttr().Get()]
        self.names = [short_name(p) for p in self.joint_paths]
        if len(set(self.names)) != len(self.names):
            raise ValueError('Short joint names are not unique')
        self.parents = np.array(UsdSkel.Topology(self.skeleton.GetJointsAttr().Get()).GetParentIndices())
        self.fps = self.stage.GetTimeCodesPerSecond()
        self.up_axis = UsdGeom.GetStageUpAxis(self.stage)
        self.skel_to_world = np.array(UsdGeom.XformCache(Usd.TimeCode(0)).GetLocalToWorldTransform(skel_prim))
        self.bind_skel = np.array([np.array(m) for m in self.skeleton.GetBindTransformsAttr().Get()])
        self.bind_world = self.bind_skel @ self.skel_to_world
        # Bind-pose local channels (restTransforms may differ from the bind pose): the
        # zero for hinge angles and the anatomical reference for every check.
        parent_bind = np.array([self.bind_skel[p] if p >= 0 else np.eye(4) for p in self.parents])
        self.bind_t, self.bind_q, self.bind_s = decompose(self.bind_skel @ np.linalg.inv(parent_bind))
        self._index = {n: i for i, n in enumerate(self.names)}

    # ----- naming -------------------------------------------------------------
    def index(self, name) -> int:
        if isinstance(name, (int, np.integer)):
            return int(name)
        key = short_name(name)
        if key not in self._index:
            raise KeyError(f'Unknown joint {name!r}; see rig.names')
        return self._index[key]

    def children(self, name) -> list[int]:
        j = self.index(name)
        return [i for i, p in enumerate(self.parents) if p == j]

    def descendants(self, name, include_self=True) -> list[int]:
        j = self.index(name)
        out, frontier = ([j] if include_self else []), [j]
        while frontier:
            kids = [i for i, p in enumerate(self.parents) if p in frontier]
            out += kids
            frontier = kids
        return out

    def chain(self, start, end) -> list[int]:
        """Joint indices from start down to end (inclusive)."""
        path, j = [], self.index(end)
        while j != -1:
            path.append(j)
            if j == self.index(start):
                return path[::-1]
            j = self.parents[j]
        raise ValueError(f'{end} is not below {start}')

    def side(self, name) -> str | None:
        """'Left' or 'Right' for a joint on, or held by, one side of the body (nearest sided ancestor)."""
        j = self.index(name)
        while j >= 0:
            side = next((s for s in ('Left', 'Right') if self.names[j].startswith(s)), None)
            if side:
                return side
            j = int(self.parents[j])
        return None

    @property
    def up(self) -> np.ndarray:
        return np.array({'Z': [0., 0., 1.], 'Y': [0., 1., 0.]}[self.up_axis])

    # ----- kinematics ---------------------------------------------------------
    def fk(self, t, q, s) -> np.ndarray:
        """World transforms (..., N, 4, 4) for local channels shaped (..., N, 3|4)."""
        local = local_matrices(t, q, s)
        skel = np.empty_like(local)
        for j, p in enumerate(self.parents):
            skel[..., j, :, :] = local[..., j, :, :] if p < 0 else local[..., j, :, :] @ skel[..., p, :, :]
        return skel @ self.skel_to_world

    # ----- reference animation ------------------------------------------------
    def animated_range(self) -> tuple[int, int] | None:
        anim = self.query.GetAnimQuery()
        if not anim:
            return None
        return int(self.stage.GetStartTimeCode()), int(self.stage.GetEndTimeCode())

    def sample_local(self, ticks) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
        """Local (t, q, s) of the stored animation at integer time codes, rest where unanimated."""
        frames = [np.array([np.array(m) for m in self.query.ComputeJointLocalTransforms(Usd.TimeCode(f))]) for f in ticks]
        return decompose(np.array(frames))

    # ----- anatomy -----------------------------------------------------------
    @cached_property
    def bind_positions(self) -> np.ndarray:
        return self.bind_world[:, 3, :3]

    def body_axes_world(self, world) -> np.ndarray:
        """Rows right, front, up of the hips-anchored anatomical frame for world transforms (N,4,4)."""
        b = self.bind_positions
        h = self.index('Hips')
        up = unit(b[self.index('Spine2')] - b[h])
        right = unit(b[self.index('RightUpLeg')] - b[self.index('LeftUpLeg')])
        right = unit(right - up * (right @ up))
        front = np.cross(up, right)
        carry = rotation_part(self.bind_world[h]).T @ rotation_part(world[h])
        return np.stack([right, front, up]) @ carry

    def palm_normal_bind(self, side) -> np.ndarray:
        """World palm normal at bind. The thumb hangs to the palm side of the knuckle plane."""
        b = self.bind_positions
        hand = b[self.index(side + 'Hand')]
        first = next((side + 'Hand' + f + '1' for f in ('Middle', 'Index') if side + 'Hand' + f + '1' in self._index), None)
        outer = next((side + 'Hand' + f + '1' for f in ('Little', 'Pinky', 'Ring', 'Middle')
                      if side + 'Hand' + f + '1' in self._index and side + 'Hand' + f + '1' != first), None)
        if first is None or outer is None or side + 'HandIndex1' not in self._index:
            # One finger chain (mitten hands): assume a palms-down bind; anatomy.json corrects it from clips.
            along = unit(b[self.index(first)] - hand) if first else np.zeros(3)
            down = -self.up
            return unit(down - along * (down @ along))
        along = unit(b[self.index(first)] - hand)
        across = unit(b[self.index(side + 'HandIndex1')] - b[self.index(outer)])
        normal = unit(np.cross(along, across))
        thumb = b[self.index(side + 'HandThumb3')] - hand if side + 'HandThumb3' in self._index else normal
        return normal if thumb @ normal > 0 else -normal

    def palm_axis_local(self, side) -> np.ndarray:
        """The palm normal as a fixed axis in the hand joint's local frame."""
        return self.palm_normal_bind(side) @ rotation_part(self.bind_world[self.index(side + 'Hand')]).T

    @cached_property
    def hinges(self) -> dict:
        """Anatomical hinges: name -> (kind, frame joint, middle, distal, axis in the frame joint's local frame).

        The frame joint is the middle joint's parent (where its local rotation acts).

        The axis is the rotation axis of a correct flexion of the distal segment,
        measured from rest: knees fold the shin backward, elbows fold the forearm
        forward, fingers curl toward the palm.
        """
        b = self.bind_world
        right, front, up = self.body_axes_world(b)
        out = {}

        def add(name, kind, m, d, toward):
            if m not in self._index or d not in self._index:
                return
            im, idx = self.index(m), self.index(d)
            ip = int(self.parents[im])  # the frame the middle joint's local rotation acts in
            segment = unit(b[idx, 3, :3] - b[im, 3, :3])
            axis_world = unit(np.cross(segment, toward))
            out[name] = (kind, ip, im, idx, axis_world @ rotation_part(b[ip]).T)

        for side in ('Left', 'Right'):
            add(side + 'Knee', 'knee', side + 'Leg', side + 'Foot', -front)
            add(side + 'Elbow', 'elbow', side + 'ForeArm', side + 'Hand', front)
            palm = self.palm_normal_bind(side)
            for finger in FINGERS:
                f = side + 'Hand' + finger
                if all(n in self._index for n in (f + '1', f + '2', f + '3')):
                    add(side + finger + 'Knuckle', 'finger', f + '1', f + '2', palm)
                    add(side + finger + 'Middle', 'finger', f + '2', f + '3', palm)
        # The rig pack's anatomy.json (axes chosen from the character's approved clips) replaces heuristics, and
        # Unity's own curl axes (humanoid.json) replace both for fingers: the game plays nothing else.
        for name, entry in self.anatomy.get('hinges', {}).items():
            if name in out and 'axis' in entry:
                kind, ip, im, idx, _ = out[name]
                out[name] = (kind, ip, im, idx, unit(np.array(entry['axis'], float)))
        # Not for elbows and knees: Unity passes part of the upper limb's twist into them (the Avatar's twist
        # distribution), which turns their bend plane, so the axes learned from played clips fit better.
        for name, (kind, ip, im, idx, axis) in list(out.items()):
            curl = next((m['axis'] for m in self.humanoid.get(im, []) if 'Stretched' in m['muscle']), None)
            if kind == 'finger' and curl is not None:
                out[name] = (kind, ip, im, idx, curl if curl @ axis >= 0 else -curl)
        return out

    @cached_property
    def humanoid(self) -> dict:
        """Joint index -> Unity Humanoid muscles from the rig pack's humanoid.json, each a dict with 'muscle',
        'bone', 'axis' (parent frame, toward +1), 'minus'/'plus' (degrees at -1/+1) and 'zero' (local Rotation
        at muscle zero). Empty for rigs without one."""
        path = self.path.parent / 'humanoid.json'
        if not path.exists():
            return {}
        index = {p: i for i, p in enumerate(self.joint_paths)}
        return {index[joint]: [{'muscle': m['muscle'], 'bone': m['humanBone'], 'axis': np.array(m['axisParent'], float),
                                'minus': m['degreesAtMinusOne'], 'plus': m['degreesAtPlusOne'], 'zero': Rotation.from_quat(m['zero'])}
                               for m in muscles]
                for joint, muscles in json.loads(path.read_text()).items() if joint in index}

    @cached_property
    def humanoid_digits(self) -> list[int]:
        """Finger, thumb and toe joints: Unity plays only rotations about their own muscle axes (1-2 per joint)."""
        digits = ('Thumb', 'Index', 'Middle', 'Ring', 'Little', 'Toes')
        return sorted(j for j, muscles in self.humanoid.items() if all(any(d in m['bone'] for d in digits) for m in muscles))

    @cached_property
    def humanoid_limited(self) -> list[int]:
        """Humanoid joints the solvers and checks keep to their muscle limits: spine, neck, head, collarbones, upper
        arms and thighs. Elbows and knees are hinges, digits snap to their axes, and hands and feet stay free:
        Unity plays their twist through the forearm and shin."""
        hinged = {joint for (_, _, joint, _, _) in self.hinges.values()}
        return sorted(j for j in self.humanoid if j not in hinged and j not in self.humanoid_digits
                      and not self.names[j].endswith(('Hand', 'Foot')))

    def humanoid_bounds(self, j) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
        """(muscle axes, lower, upper radians) of joint j for humanoid_split's angles: HUMANOID_REACH times each
        muscle's range, within the twist and swing Unity can still play."""
        muscles = self.humanoid[j]
        lower, upper = [], []
        for m in muscles:
            cap = HUMANOID_TWIST_LIMIT if 'Twist' in m['muscle'] or 'Turn' in m['muscle'] else HUMANOID_SWING_LIMIT
            lower.append(max(HUMANOID_REACH * m['minus'], -cap))
            upper.append(min(HUMANOID_REACH * m['plus'], cap))
        return np.array([m['axis'] for m in muscles]), np.radians(lower), np.radians(upper)

    def humanoid_excess(self, j, q) -> tuple[float, str]:
        """Degrees joint j's local rotation q goes past humanoid_bounds (or turns about an axis none of its muscles
        has, such as a collarbone's roll), with the muscle concerned."""
        angles, off = self.humanoid_split(j, q)
        _, lower, upper = self.humanoid_bounds(j)
        past = np.degrees(np.maximum(lower - angles, angles - upper).clip(min=0))
        k = int(np.argmax(past))
        return (float(past[k]), self.humanoid[j][k]['muscle']) if past[k] >= off else (off, 'an axis no muscle has')

    def humanoid_split(self, j, q) -> tuple[np.ndarray, float]:
        """Split joint j's local rotation q into its muscle angles (radians, least squares) and the degrees no
        muscle can reach. The reachable rotation is Rotation.from_rotvec(axes.T @ angles) * zero."""
        muscles = self.humanoid[j]
        axes = np.array([m['axis'] for m in muscles])
        v = (Rotation.from_quat(q) * muscles[0]['zero'].inv()).as_rotvec()
        angles = np.linalg.lstsq(axes.T, v, rcond=None)[0]
        return angles, float(np.degrees(np.linalg.norm(v - axes.T @ angles)))

    @cached_property
    def grip(self) -> dict:
        """The rig pack's grip.json (how the left hand holds the weapon in an approved two-handed frame) with its
        transform as a row-convention 'matrix' (hand = matrix @ weapon). Empty without one."""
        path = self.path.parent / 'grip.json'
        if not path.exists():
            return {}
        grip = json.loads(path.read_text())
        grip['matrix'] = local_matrices(np.array(grip['t'], float), np.array(grip['q'], float), np.ones(3))
        return grip

    @cached_property
    def anatomy(self) -> dict:
        path = self.path.parent / 'anatomy.json'
        return json.loads(path.read_text()) if path.exists() else {}

    def reversed_allowance(self, name) -> float:
        """Backward bend (degrees) the character's own approved clips already use for this hinge."""
        return float(self.anatomy.get('hinges', {}).get(name, {}).get('reversedAllowanceDegrees', 0.0))

    def hinge_axis_state(self, world, name) -> tuple[float, np.ndarray]:
        """(flexion degrees since bind, unit rotation axis in the frame joint's local frame)."""
        _, a, m, d, _ = self.hinges[name]
        rest = unit(self.bind_world[d, 3, :3] - self.bind_world[m, 3, :3]) @ rotation_part(self.bind_world[a]).T
        now = unit(world[d, 3, :3] - world[m, 3, :3]) @ rotation_part(world[a]).T
        angle = float(np.degrees(np.arccos(np.clip(rest @ now, -1, 1))))
        cross = np.cross(rest, now)
        norm = np.linalg.norm(cross)
        return angle, (cross / norm if norm > 1e-9 else np.zeros(3))

    def hinge_state(self, world, name) -> tuple[float, float]:
        """(flexion degrees since rest, alignment with the correct axis in [-1, 1])."""
        _, a, m, d, axis = self.hinges[name]
        rest = unit(self.bind_world[d, 3, :3] - self.bind_world[m, 3, :3]) @ rotation_part(self.bind_world[a]).T
        now = unit(world[d, 3, :3] - world[m, 3, :3]) @ rotation_part(world[a]).T
        angle = float(np.degrees(np.arccos(np.clip(rest @ now, -1, 1))))
        cross = np.cross(rest, now)
        norm = np.linalg.norm(cross)
        return angle, (float(cross @ axis / norm) if norm > 1e-9 else 1.0)


def decompose(matrices) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    """Row-convention (..., 4, 4) -> translations, scipy quaternions (x, y, z, w), scales."""
    m = np.asarray(matrices, float)
    t = m[..., 3, :3].copy()
    s = np.linalg.norm(m[..., :3, :3], axis=-1)
    rot_row = m[..., :3, :3] / s[..., :, None]
    lead = m.shape[:-2]
    q = Rotation.from_matrix(np.swapaxes(rot_row, -1, -2).reshape(-1, 3, 3)).as_quat().reshape(*lead, 4)
    return t, q, s
