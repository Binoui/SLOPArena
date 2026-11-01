"""Flowing clips through approved keys: ``clip, report = flow(brief)``.

Every key lands exactly on its tick. Between keys the body passes through instead of stopping: body joints move on
Catmull-Rom tangents, knees and elbows turn over at a key instead of parking, and secondary layers (lag, settle, finger
curl, the caller's accents) add motion that is exactly zero on every key. Defaults come from the brief's purpose
(PRESETS); normals keep a plain, short recovery. Flow stays advisory: the report gives the flow numbers, it never fails
a build on them.

Contacts stay with solve.py. flow() masks its layers off where the brief holds a contact (two_hands spans, planted
legs, the strike chain in its active window) and on deliberate holds; run carry/pin/track/hold_grip and planted-foot
two_bone after it, as before, then audit the keys again with key_drift().
"""
from __future__ import annotations

import time
from dataclasses import dataclass, field
from typing import Literal, Mapping, Sequence

import numpy as np
from scipy.spatial.transform import Rotation

from .brief import Brief, brief_purpose
from .checks import DEFAULT_SPEC
from .clip import CharFrame, Clip, Pose, _continuous_rotvecs
from .rig import Rig, rotation_part

__all__ = ['flow', 'Accent', 'FlowReport', 'key_drift', 'approved_keys']

Range = tuple[int, int]            # inclusive tick range
Ranges = tuple[Range, ...]

# ----------------------------------------------------------------------------- policy tables (the only copies)
# Starting values from the Flip Kick flow pass (owner "really good", 2026-11-01); replay_flow calibrates them. Arcs and
# drag stay off until the owner's replay board shows they help (synthesis 2026-11-01).
PRESETS: dict[str, dict[str, float]] = {
    'attack':     dict(tangents=0.9, turnover=0.5, lag=0.8, drag=0.0, arcs=0.0, settle=0.5, fingers=1.0),
    'special':    dict(tangents=0.9, turnover=0.5, lag=1.0, drag=0.0, arcs=0.0, settle=1.0, fingers=1.0),
    'other':      dict(tangents=0.9, turnover=0.5, lag=1.0, drag=0.0, arcs=0.0, settle=1.0, fingers=1.0),
    'idle':       dict(tangents=1.0, turnover=0.3, lag=1.0, drag=0.0, arcs=0.0, settle=0.0, fingers=0.5),
    'locomotion': dict(tangents=1.0, turnover=0.5, lag=1.0, drag=0.0, arcs=0.0, settle=0.0, fingers=0.5),
    'loop':       dict(tangents=1.0, turnover=0.5, lag=1.0, drag=0.0, arcs=0.0, settle=0.0, fingers=0.5),
}
DIALS = tuple(PRESETS['special'])
# Keys identical for a joint (an exit hold, a copied pose) hold it: zero slopes at both ends of the segment. Nearly equal
# keys keep their tangents, a moving hold: a 0.5 degree rule stopped 70% of the Flip Kick's body on tick 80.
FLAT_DEGREES = 0.01
FLAT_METRES = 1e-5
AMBIGUOUS_DEGREES = 170.0   # a free joint turning more than this between neighbouring keys has no clear direction
# Peak lag in ticks at lag=1, keyed by the joint name without its Left/Right prefix; drag uses half of it as inertia
# (ticks of world orientation kept). Joints not listed get neither.
LAG_PEAKS: dict[str, float] = {'Spine': 0.5, 'Spine1': 1.0, 'Spine2': 1.0, 'Neck': 1.3, 'Head': 1.5,
                               'Shoulder': 0.6, 'Arm': 1.0, 'ForeArm': 1.8, 'Hand': 2.6, 'Leg': 0.5, 'Foot': 1.0}
LAG_SHARE = 0.2             # lag stays within this share of its key segment, so time keeps running forward
FAST_SHARE = 0.3            # lag in a segment that touches a fast window
DRAG_MAX_DEGREES = 8.0
ARC_SHARE, ARC_MAX_DEGREES, ARC_MAX_AXIAL, REVERSAL_COS = 0.15, 6.0, 3.0, -0.3
SETTLE_MIN_TICKS = 8        # a last key closer than this to the exit gets no settle
FINGER_MAX_DEGREES, FINGER_DELAY_TICKS, FINGER_GAIN, THUMB_SHARE = 10.0, 2, 1.2, 0.6
MASK_FADE_TICKS = 3
CHAINS: dict[str, tuple[str, ...]] = {   # Accent aliases, lowest joint first; names a rig lacks are skipped
    'spine': ('Spine', 'Spine1', 'Spine2', 'Neck', 'Head'),
    'left arm': ('LeftShoulder', 'LeftArm', 'LeftForeArm', 'LeftHand'),
    'right arm': ('RightShoulder', 'RightArm', 'RightForeArm', 'RightHand'),
    'left leg': ('LeftUpLeg', 'LeftLeg', 'LeftFoot'),
    'right leg': ('RightUpLeg', 'RightLeg', 'RightFoot'),
}
SPINE_WEIGHTS = (1.0, 1.0, 0.8, 1.0, 0.6)   # the Flip Kick's arch: the upper chest and head turn a little less


# ----------------------------------------------------------------------------- public types
@dataclass(frozen=True)
class Accent:
    """A move-specific gesture between two keys: the joints turn `degrees` about `axis` and back.

    The turn peaks at `tick` and is zero, with zero slope, on the keys either side, so it never touches a key pose.
    `joints` is a joint name, a hinge name ('RightKnee'), a CHAINS alias ('spine'), or a tuple of joints lowest first;
    each joint up a chain peaks `wave` ticks after the one below it. `axis` is a character-frame direction (right,
    front, up), as in Pose.rotate: (1, 0, 0) leans the spine back. 'bend' instead adds flexion degrees to a hinge, as
    set_hinge counts them. `swirl` adds a share of the turn on a second axis (axis x the bone) that is zero at the keys
    and at the peak, so the out-and-back never stops at its turn. `weights` scales each joint of a chain."""
    joints: str | tuple[str, ...]
    tick: float
    degrees: float
    axis: tuple[float, float, float] | Literal['bend']
    wave: float = 1.0
    swirl: float = 0.25
    weights: tuple[float, ...] | None = None


@dataclass(frozen=True)
class FlowSpec:
    """What one flow() call ran: the purpose preset with the caller's dials applied (FlowReport.spec)."""
    purpose: str
    tangents: float        # share of the Catmull-Rom slope a joint keeps through each key (0 = it eases into every key)
    turnover: float        # degrees per tick a knee or elbow keeps where it reverses at a key (0 = it parks)
    lag: float             # chain follow-through, peaking between keys (0 = none)
    drag: float            # free limbs keep part of their world orientation while the body turns (0 = none)
    arcs: float            # pass-around at keys where a joint reverses (0 = straight out-and-back)
    settle: float          # share of its incoming motion a joint whose keys hold from the last key to the exit
                           # carries through that key, then eases into the exit (0 = it stops on the key)
    fingers: float         # finger curl with arm speed (0 = fingers as keyed)
    holds: Mapping[str, Ranges] = field(default_factory=dict)
    accents: tuple[Accent, ...] = ()

    @classmethod
    def for_purpose(cls, purpose: str, rig: Rig, holds=None, accents=(), **dials) -> 'FlowSpec':
        values = dict(PRESETS.get(purpose, PRESETS['other']))
        for name, value in dials.items():
            if name not in DIALS:
                raise TypeError(f'unknown flow dial {name!r}; dials are {", ".join(DIALS)}')
            if value is None:
                continue
            if not np.isfinite(value) or value < 0:
                raise ValueError(f'flow dial {name} must be a number >= 0, not {value!r}')
            values[name] = float(value)
        normal = {}
        for joint, ranges in (holds or {}).items():
            if joint != '*':
                rig.index(joint)
            if len(ranges) == 2 and all(isinstance(x, (int, np.integer)) for x in ranges):
                ranges = (ranges,)
            normal[joint] = tuple((int(a), int(b)) for a, b in ranges)
            if any(a > b for a, b in normal[joint]):
                raise ValueError(f'hold {joint} {normal[joint]}: each range is (first, last)')
        return cls(purpose=purpose, holds=normal, accents=tuple(accents), **values)

    def to_dict(self) -> dict:
        return {'purpose': self.purpose, **{k: getattr(self, k) for k in DIALS},
                'holds': {k: [list(r) for r in v] for k, v in self.holds.items()},
                'accents': [vars(a) for a in self.accents]}


@dataclass(frozen=True)
class KeyDrift:
    """How far a clip sits from one key pose on the key's own tick: the largest joint displacement (FK, metres) and
    local rotation error (geodesic degrees), body and digits apart, with the worst body joints."""
    metres: float
    degrees: float
    digit_metres: float
    digit_degrees: float
    worst_metres: str
    worst_degrees: str


@dataclass(frozen=True)
class FlowReport:
    """What flow() did, in the units the check and the owner use. str(report) prints a few lines."""
    spec: FlowSpec
    approved: dict[int, KeyDrift]              # every approved key, measured on its own tick
    keys: dict[int, Pose]                      # every pose the clip passes through exactly (contract poses included)
    flow_before: dict[str, float]              # curves.flow of Clip.from_keys over the same keys and window
    flow_after: dict[str, float]
    window: Range
    layers: dict[str, dict[str, float]]        # layer -> joint -> largest degrees (lag: ticks; settle: deg/tick)
    clamped: dict[str, list]                   # joint -> [(first key, last key, kept share)]: Humanoid range kept
    lost: dict[str, float]                     # joint -> most degrees asked about an axis it cannot play
    holds: tuple[tuple[int, int, float], ...]  # key segments where this share of the body holds still in the keys
    notes: tuple[str, ...]
    seconds: float

    @property
    def max_drift(self) -> tuple[float, float]:
        """(metres, degrees): the largest body drift over the approved keys."""
        return (max((d.metres for d in self.approved.values()), default=0.0),
                max((d.degrees for d in self.approved.values()), default=0.0))

    def to_dict(self) -> dict:
        return {'spec': self.spec.to_dict(), 'approved': {t: vars(d) for t, d in self.approved.items()},
                'keyTicks': sorted(self.keys), 'flowBefore': self.flow_before, 'flowAfter': self.flow_after,
                'window': list(self.window), 'layers': self.layers, 'clamped': self.clamped, 'lost': self.lost,
                'holds': [list(h) for h in self.holds], 'notes': list(self.notes), 'seconds': self.seconds}

    def __str__(self) -> str:
        def numbers(f):
            return (f"{f['stops per joint per second']:.2f}/s {f['most of the body stopping together (share)']:.0%} "
                    f"{f['frozen joint-ticks (share)']:.0%}")
        metres, degrees = self.max_drift
        lines = [f'flow (stops, together, frozen) {numbers(self.flow_before)} -> {numbers(self.flow_after)} '
                 f'on ticks {self.window[0]}..{self.window[1]}',
                 f'approved keys: {len(self.approved)}, max drift {metres * 100:.4f} cm {degrees:.4f} deg on their ticks']
        units = {'lag': 'ticks late', 'settle': 'deg/tick carried'}
        sizes = [f'{layer} up to {max(v.values()):.1f} {units.get(layer, "deg")} ({len(v)} joints)'
                 for layer, v in self.layers.items() if v]
        if sizes:
            lines.append('layers: ' + ', '.join(sizes))
        if self.clamped:
            lines.append('Humanoid range kept by easing: ' + ', '.join(
                f'{j} {a}..{b} x{g:.2f}' for j, rows in self.clamped.items() for a, b, g in rows))
        lost = {j: d for j, d in self.lost.items() if d > 1.0}
        if lost:
            lines.append('asked about axes the joint cannot play (dropped): ' + ', '.join(
                f'{j} {d:.1f} deg' for j, d in lost.items()))
        for a, b, share in self.holds:
            lines.append(f'keys hold {a}..{b}: {share:.0%} of the body barely moves between those keys; flow keeps '
                         'the hold (add an Accent or a breakdown key to move it)')
        lines += list(self.notes)
        return '\n'.join(lines)


# ----------------------------------------------------------------------------- the one entry point
def flow(brief: Brief, keys: Mapping[int, Pose] | None = None, *, tangents: float | None = None,
         turnover: float | None = None, lag: float | None = None, drag: float | None = None,
         arcs: float | None = None, settle: float | None = None, fingers: float | None = None,
         holds: Mapping[str, Sequence[Range]] | None = None,
         accents: Sequence[Accent] = ()) -> tuple[Clip, FlowReport]:
    """The clip through `keys` on the brief's clock, flowing: every key lands exactly on its tick and the body moves
    through the keys instead of stopping on them.

    keys: every pose the clip passes through exactly. The default is the brief's approved keys; pass a dict to add
    breakdowns, or your own keys in a run without a keys stage. Approved ticks must keep their approved pose (within the
    check's keys_metres) or flow raises ValueError naming the tick. The entry pose (a matched entry) and the exit pose
    from exit.from_tick are added from the brief; a loop (brief loop.from_tick) flows periodically and closes its seam.
    Dials (None = the brief purpose's preset, PRESETS): tangents, turnover, lag, drag, arcs, settle, fingers; see
    FlowSpec. holds: {'*' or joint: [(first, last), ...]} keeps stiffness you want: inside a range the joint gets no
    layers and stops on the range's keys. accents: move-specific gestures between keys (Accent).
    Brief contacts are not solved here: run hold_grip/carry/two_bone after flow, then key_drift() to audit the keys.
    Returns (clip, report), like solve()."""
    started = time.perf_counter()
    facts = _MoveFacts.from_brief(brief)
    spec = FlowSpec.for_purpose(facts.purpose, facts.rig, holds=holds, accents=accents, tangents=tangents,
                                turnover=turnover, lag=lag, drag=drag, arcs=arcs, settle=settle, fingers=fingers)
    keyset = _KeySet.build(facts, keys)
    masks, held = _masks(keyset, facts, spec)
    settled: dict[str, float] = {}
    layers = _Layers.empty(keyset)
    tracks, clamped = _tracks(keyset, facts, spec, masks, settled, layers.notes)
    hips = _hips_path(keyset, facts, spec, masks, settled)
    pchip = Clip.from_keys(dict(zip(keyset.ticks, keyset.poses)), keyset.n_ticks, cycle=facts.cycle)
    base = _compose(keyset, tracks, hips, pchip, _Layers.empty(keyset), {})
    if settled:
        layers.sizes['settle'] = settled
    for layer in (_lag, _drag, _arcs, _accents, _finger_curl):
        layer(layers, keyset, tracks, base, masks, held, facts, spec)
    clip = _compose(keyset, tracks, hips, pchip, layers, clamped)
    return clip, _measure(clip, pchip, keyset, facts, spec, layers, clamped, started)


def approved_keys(brief: Brief) -> dict[int, Pose]:
    """The brief's approved key poses {tick: Pose} (run.py --keys writes them into the run's brief copy), or {}."""
    keys = brief.get('approved_keys')
    if not keys:
        return {}
    clip = Clip.load(brief.rig, brief.path.parent / keys['file'], frame=brief.entry_frame)
    return {int(t): clip.pose(int(t)) for t in keys['ticks'] if int(t) < len(clip)}


def key_drift(clip: Clip, keys: Mapping[int, Pose]) -> dict[int, KeyDrift]:
    """Each key pose against the clip on the key's own tick (no nearby-tick search, unlike the check's 2-tick slack):
    the audit to run again after any contact solve or edit on a flowed clip."""
    rig = clip.rig
    digits = np.zeros(len(rig.names), bool)
    digits[list(rig.humanoid_digits)] = True
    world = clip.world()
    out = {}
    for tick, pose in sorted(keys.items()):
        moved = np.linalg.norm(world[tick, :, 3, :3] - pose.world()[:, 3, :3], axis=1)
        dots = np.abs(np.sum(clip.q[tick] * pose.q, axis=1) /
                      (np.linalg.norm(clip.q[tick], axis=1) * np.linalg.norm(pose.q, axis=1)))
        turned = np.degrees(2 * np.arccos(np.clip(dots, 0, 1)))
        body = ~digits
        out[int(tick)] = KeyDrift(float(moved[body].max()), float(turned[body].max()),
                                  float(moved[digits].max()) if digits.any() else 0.0,
                                  float(turned[digits].max()) if digits.any() else 0.0,
                                  rig.names[int(np.flatnonzero(body)[moved[body].argmax()])],
                                  rig.names[int(np.flatnonzero(body)[turned[body].argmax()])])
    return out


# ----------------------------------------------------------------------------- private: brief facts
@dataclass(frozen=True)
class _MoveFacts:
    """Everything flow reads from a Brief, parsed once. Layers take this, never the Brief."""
    rig: Rig
    frame: CharFrame
    purpose: str
    n_ticks: int
    cycle: Range | None                        # (loop.from_tick, last tick) for loops
    entry: Pose | None                         # tick 0 when the entry matches
    exit: Pose | None
    exit_from: int | None                      # ticks from here on hold the exit pose exactly
    exit_motion: tuple[Pose, Pose] | None      # the exit reference's first step, for a single-tick exit
    fast_windows: Ranges
    active: Range | None
    strike: tuple[str, ...]
    two_hands: Range | None
    planted: tuple[str, ...]
    exempt: tuple[str, ...]                    # snap_exempt joints and props: no layers
    snap_limit: float                          # the check's degrees per tick outside fast windows
    approved: dict[int, Pose]

    @classmethod
    def from_brief(cls, brief: Brief) -> '_MoveFacts':
        data, rig = brief.data, brief.rig
        n = int(data['clock']['duration_ticks']) + 1
        exit_ = data.get('exit')
        exit_from = int(exit_['from_tick']) if exit_ else None
        cycle = (int(brief.loop_from), n - 1) if brief.loop_from is not None else None
        exit_motion = None
        if exit_ is not None and exit_from == n - 1 and cycle is None:
            name, tick = (exit_['reference'], int(exit_.get('tick', 0))) if 'reference' in exit_ else (
                data['entry']['reference'], int(data['entry']['tick']))
            reference = brief.reference(name)
            if tick + 1 < len(reference):
                exit_motion = (reference.pose(tick), reference.pose(tick + 1))
        props = tuple(n_ for n_ in rig.names if n_.startswith(('Weapon', 'blade')))
        active = data['clock'].get('active')
        return cls(rig=rig, frame=brief.entry_frame, purpose=brief_purpose(data)[0], n_ticks=n, cycle=cycle,
                   entry=brief.entry_pose if brief.entry_matches else None,
                   exit=brief.exit_pose if exit_ is not None else None, exit_from=exit_from, exit_motion=exit_motion,
                   fast_windows=tuple((int(a), int(b)) for a, b in data.get('fast_windows', [])),
                   active=(int(active[0]), int(active[1])) if active else None, strike=tuple(brief.strike),
                   two_hands=tuple(data['two_hands']) if data.get('two_hands') else None,
                   planted=tuple(data.get('planted') or ()), exempt=tuple(data.get('snap_exempt') or ()) + props,
                   snap_limit=float(data.get('snap_degrees_per_tick') or DEFAULT_SPEC['snap_degrees_per_tick']),
                   approved=approved_keys(brief))


# ----------------------------------------------------------------------------- private: keys
@dataclass(frozen=True)
class _KeySet:
    """Every pose the clip passes through exactly, sorted by tick. Ticks strictly increase inside [0, n_ticks-1]; in a
    cycle both cycle ends are keys with the same pose; tick 0 is the entry pose when the entry matches; exit_from and the
    last tick hold the exit pose."""
    rig: Rig
    frame: CharFrame
    n_ticks: int
    ticks: tuple[int, ...]
    poses: tuple[Pose, ...]
    approved: frozenset[int]
    notes: tuple[str, ...]

    @classmethod
    def build(cls, facts: _MoveFacts, keys: Mapping[int, Pose] | None) -> '_KeySet':
        n, rig = facts.n_ticks, facts.rig
        given = {int(t): p.copy() for t, p in (facts.approved if keys is None else keys).items()}
        if not given:
            raise ValueError('flow needs keys: approve a keys stage (run.py --keys) or pass keys={tick: Pose}')
        notes = []
        limit = DEFAULT_SPEC['keys_metres']
        for tick, pose in facts.approved.items():
            if tick not in given:
                raise ValueError(f'approved key {tick} is missing from keys; keep every approved key and add '
                                 'breakdowns between them')
            moved = _distance(given[tick], pose)
            if moved > limit:
                raise ValueError(f'approved key {tick} moved {moved * 100:.1f} cm from its approved pose (at most '
                                 f'{limit * 100:.0f} cm): keep the approved poses and change the motion between them')
        for tick, pose in given.items():
            if not 0 <= tick < n:
                raise ValueError(f'key {tick} lies outside ticks 0..{n - 1}')
            if len(pose.q) != len(rig.names):
                raise ValueError(f'key {tick} is on another rig ({len(pose.q)} joints, not {len(rig.names)})')
        if facts.entry is not None:
            if 0 in given and _distance(given[0], facts.entry) > 1e-3:
                notes.append(f'tick 0 keeps the entry pose ({_distance(given[0], facts.entry) * 100:.1f} cm from key 0)')
            given[0] = facts.entry.copy()
        if facts.exit_from is not None:
            for tick in [t for t in given if t > facts.exit_from]:
                del given[tick]
            given[facts.exit_from] = facts.exit.copy()
            given[n - 1] = facts.exit.copy()
        if facts.cycle is not None:
            first, last = facts.cycle
            if first not in given:
                raise ValueError(f'loop from tick {first}: key the loop start')
            if last in given and _distance(given[last], given[first]) > 1e-3:
                raise ValueError(f'loop {first}..{last}: key the same pose at both ends '
                                 f'({_distance(given[last], given[first]) * 100:.1f} cm apart)')
            given[last] = given[first].copy()
        ticks = tuple(sorted(given))
        for pose in given.values():
            pose.frame = facts.frame
        return cls(rig, facts.frame, n, ticks, tuple(given[t] for t in ticks),
                   frozenset(t for t in facts.approved if t in given), tuple(notes))


def _distance(a: Pose, b: Pose) -> float:
    """Largest body-joint distance (metres) between two poses; digits excluded, as the keys check does."""
    digits = set(a.rig.humanoid_digits)
    body = [j for j in range(len(a.rig.names)) if j not in digits]
    return float(np.linalg.norm(a.world()[body, 3, :3] - b.world()[body, 3, :3], axis=1).max())


# ----------------------------------------------------------------------------- private: curves
def _hermite(ticks, values, slope_in, slope_out, t) -> np.ndarray:
    """Cubic Hermite through `values` (K, d) at `ticks` with per-key arriving and leaving slopes (units per tick),
    evaluated at fractional ticks t (T,). Outside the keys it holds the end values."""
    ticks = np.asarray(ticks, float)
    t = np.clip(np.asarray(t, float), ticks[0], ticks[-1])
    i = np.clip(np.searchsorted(ticks, t, side='right') - 1, 0, len(ticks) - 2)
    h = ticks[i + 1] - ticks[i]
    s = ((t - ticks[i]) / h)[:, None]
    h00, h10, h01, h11 = 2 * s ** 3 - 3 * s ** 2 + 1, s ** 3 - 2 * s ** 2 + s, -2 * s ** 3 + 3 * s ** 2, s ** 3 - s ** 2
    return h00 * values[i] + h10 * h[:, None] * slope_out[i] + h01 * values[i + 1] + h11 * h[:, None] * slope_in[i + 1]


def _neighbours(ticks, cycle):
    """Per key ((previous key index, ticks back to it), (next key index, ticks on to it)), None at an open end. Inside
    a cycle the neighbours wrap across the seam, so both seam keys get one slope."""
    k = len(ticks)
    out = []
    for i, tick in enumerate(ticks):
        prev = (i - 1, tick - ticks[i - 1]) if i > 0 else None
        nxt = (i + 1, ticks[i + 1] - tick) if i < k - 1 else None
        if cycle is not None:
            first, last = ticks.index(cycle[0]), ticks.index(cycle[1])
            period = cycle[1] - cycle[0]
            if i == first:
                prev = (last - 1, tick - (ticks[last - 1] - period))
            if i == last:
                nxt = (first + 1, ticks[first + 1] + period - tick)
        out.append((prev, nxt))
    return out


def _catmull_rom(ticks, values, gain, cycle, flat, start=None, end=None):
    """Non-uniform Catmull-Rom slopes (K, d) times `gain`; `start`/`end` slopes at open ends (default 0). A segment
    whose keys differ by less than `flat` holds: zero slopes at both of its keys."""
    ticks = tuple(int(t) for t in ticks)
    slopes = np.zeros_like(values)
    for i, (prev, nxt) in enumerate(_neighbours(ticks, cycle)):
        if prev is not None and nxt is not None:
            slopes[i] = gain * (values[nxt[0]] - values[prev[0]]) / (prev[1] + nxt[1])
    if cycle is None or ticks[0] < cycle[0]:
        slopes[0] = 0 if start is None else start
    if cycle is None:
        slopes[-1] = 0 if end is None else end
    for i in range(len(ticks) - 1):
        if np.linalg.norm(values[i + 1] - values[i]) < flat:
            slopes[i] = slopes[i + 1] = 0
    if cycle is not None:
        a, b = ticks.index(cycle[0]), ticks.index(cycle[1])
        if not slopes[a].any() or not slopes[b].any():
            slopes[a] = slopes[b] = 0
    return slopes


def _hinge_slopes(ticks, theta, turnover, cycle):
    """(arriving, leaving) flexion slopes (degrees per tick): monotone (pchip) where the bend keeps its direction, and a
    one-sided `turnover` where it reverses at a key, a crisp turn instead of a park. Each side stays within 3x its
    segment's secant (the Fritsch-Carlson box), so no segment overshoots its keys. Flat segments hold; a loop's seam
    keys share one smooth slope."""
    ticks = tuple(int(t) for t in ticks)
    k = len(ticks)
    arriving, leaving = np.zeros(k), np.zeros(k)
    seam = {ticks.index(cycle[0]), ticks.index(cycle[1])} if cycle is not None else set()
    for i, (prev, nxt) in enumerate(_neighbours(ticks, cycle)):
        if prev is None or nxt is None:
            continue
        before = (theta[i] - theta[prev[0]]) / prev[1]
        after = (theta[nxt[0]] - theta[i]) / nxt[1]
        if before * after > 0:
            w1, w2 = 2 * nxt[1] + prev[1], nxt[1] + 2 * prev[1]
            arriving[i] = leaving[i] = (w1 + w2) / (w1 / before + w2 / after)
        elif i not in seam and before * after < 0:
            arriving[i] = np.sign(before) * min(turnover, 3 * abs(before))
            leaving[i] = np.sign(after) * min(turnover, 3 * abs(after))
    for i in np.flatnonzero(np.abs(np.diff(theta)) < FLAT_DEGREES):
        arriving[i] = leaving[i] = arriving[i + 1] = leaving[i + 1] = 0
    if seam and any(arriving[s_] == 0 for s_ in seam):
        for s_ in seam:
            arriving[s_] = leaving[s_] = 0
    return arriving, leaving


# ----------------------------------------------------------------------------- private: channel tracks
ChannelKind = Literal['muscle', 'hinge', 'free', 'digit']


@dataclass(frozen=True)
class _Track:
    """One joint's local rotation through the keys, as Hermite curves in its channel coordinates.

    muscle: x = rotation vector of q * zero^-1 (humanoid_split's space, the parent frame). Hermite combinations of keys
            in a collarbone's two-muscle span stay in that span, so it cannot roll; offsets are projected onto the
            muscle axes before they are added, and eased per key segment where they would leave the Humanoid range.
    hinge:  x = (theta, swing): q * rest^-1 = exp(swing) * exp(theta * axis), the bend about the rig's hinge axis (as
            set_hinge counts it) and the rest. theta turns over at a key with a set slope instead of parking.
    free:   sign-continuous quaternion Hermite (hips, hands, feet, props, joints without Humanoid data); the lift is
            kept across full turns, so a 360-degree flip keeps turning the same way.
    digit:  the pchip clip (today's interpolation), offsets added, then snapped to the Humanoid's finger axes."""
    joint: int
    kind: ChannelKind
    ticks: np.ndarray
    values: np.ndarray
    slope_in: np.ndarray
    slope_out: np.ndarray
    zero: Rotation | None = None
    axes: np.ndarray | None = None        # muscle: (m, 3) parent-frame muscle axes; hinge: (3,) bend axis
    bounds: tuple[np.ndarray, np.ndarray] | None = None
    rest: Rotation | None = None

    def evaluate(self, t: np.ndarray, offset: np.ndarray | None, pchip: Clip):
        """(local quaternions (T, 4) at fractional ticks t, with `offset` (T, 3) parent-frame rotation vectors added as
        what the joint can play; degrees per tick it could not play (T,); [(first key, last key, kept share)] where a
        muscle's layers were eased to stay in the Humanoid range)."""
        lost, eased = np.zeros(len(t)), []
        if self.kind == 'digit':
            q = pchip.q[np.rint(t).astype(int), self.joint]
            if offset is not None:
                q = _sign_like((Rotation.from_rotvec(offset) * Rotation.from_quat(q)).as_quat(), q)
            return q, lost, eased
        x = _hermite(self.ticks, self.values, self.slope_in, self.slope_out, t)
        if self.kind == 'free':
            q = x / np.linalg.norm(x, axis=1, keepdims=True)
            if offset is not None:
                q = _sign_like((Rotation.from_rotvec(offset) * Rotation.from_quat(q)).as_quat(), q)
            return q, lost, eased
        if self.kind == 'muscle':
            if offset is not None:
                angles = np.linalg.lstsq(self.axes.T, offset.T, rcond=None)[0].T
                lost = np.degrees(np.linalg.norm(offset - angles @ self.axes, axis=1))
                angles, eased = self._in_range(x, angles)
                x = x + angles @ self.axes
            return (Rotation.from_rotvec(x) * self.zero).as_quat(), lost, eased
        theta, swing = x[:, 0], x[:, 1:]
        if offset is not None:
            along = offset @ self.axes
            lost = np.degrees(np.linalg.norm(offset - along[:, None] * self.axes, axis=1))
            theta = theta + along
        bend = Rotation.from_rotvec(theta[:, None] * self.axes)
        return (Rotation.from_rotvec(swing) * bend * self.rest).as_quat(), lost, eased

    def _in_range(self, x, extra):
        """Scale each key segment's layer angles by the largest of 1, 0.75, 0.5, 0.25, 0 that keeps the muscle angles in
        the Humanoid range (widened to the segment's own curve, which the tangent search already kept in range).
        A per-segment share keeps the layer's zero value and slope on the keys: no clipped plateau."""
        project = np.linalg.pinv(self.axes.T)
        own = x @ project.T
        lower, upper = self.bounds
        t = np.arange(len(x))
        eased = []
        for a, b in zip(self.ticks[:-1], self.ticks[1:]):
            inside = (t >= a) & (t <= b)
            lo, hi = np.minimum(lower, own[inside].min(axis=0)), np.maximum(upper, own[inside].max(axis=0))
            for share in (1.0, 0.75, 0.5, 0.25, 0.0):
                trial = own[inside] + share * extra[inside]
                if (trial >= lo - 1e-9).all() and (trial <= hi + 1e-9).all():
                    break
            if share < 1.0:
                extra[inside] *= share
                eased.append((int(a), int(b), share))
        return extra, eased


def _sign_like(q, reference):
    return np.where(np.sum(q * reference, axis=1, keepdims=True) < 0, -q, q)


def _channel_kinds(rig: Rig) -> dict[int, tuple[ChannelKind, str | None]]:
    hinges = {m: name for name, (kind, _, m, _, _) in rig.hinges.items() if kind in ('knee', 'elbow')}
    digits, limited = set(rig.humanoid_digits), set(rig.humanoid_limited)
    return {j: (('digit', None) if j in digits else ('hinge', hinges[j]) if j in hinges
                else ('muscle', None) if j in limited else ('free', None)) for j in range(len(rig.names))}


def _settle_key(keyset: _KeySet, facts: _MoveFacts) -> int | None:
    """Index of the last key before the exit, the key the body settles out of: none in a loop, without a key before
    it to read the incoming motion, closer than SETTLE_MIN_TICKS to the exit, or inside the active window."""
    if facts.cycle is not None:
        return None
    end = facts.exit_from if facts.exit_from is not None else keyset.n_ticks - 1
    if end not in keyset.ticks or keyset.ticks.index(end) < 2:
        return None
    i = keyset.ticks.index(end) - 1
    start = keyset.ticks[i]
    if end - start < SETTLE_MIN_TICKS or (facts.active and facts.active[0] <= start <= facts.active[1]):
        return None
    return i


def _settle_slope(ticks, values, i, gain):
    """The Catmull-Rom slope a joint that holds from key i to the exit would carry through key i."""
    return gain * (values[i + 1] - values[i - 1]) / (ticks[i + 1] - ticks[i - 1])


def _tracks(keyset: _KeySet, facts: _MoveFacts, spec: FlowSpec, masks: np.ndarray, settled: dict[str, float],
            notes: list[str]) -> tuple[dict[int, _Track], dict[str, list]]:
    """A track per joint. Slopes: spec.tangents x non-uniform Catmull-Rom (periodic inside a cycle); the clip leaves its
    first key at rest and arrives at a single-tick exit with half the exit reference's own motion; held joints stop on
    the keys of their hold ranges. Settle: a joint whose keys hold from the last key to the exit carries spec.settle of
    its incoming motion through that key instead of stopping on it (lag then trails the chain behind the hips). Muscle
    segments that would leave the Humanoid range ease their slopes."""
    rig, ticks = keyset.rig, np.array(keyset.ticks)
    q = np.array([p.q for p in keyset.poses])
    q /= np.linalg.norm(q, axis=2, keepdims=True)
    gain, flat = spec.tangents, np.radians(FLAT_DEGREES)
    held = _held_keys(keyset, spec)
    settle_at = _settle_key(keyset, facts) if spec.settle > 0 else None
    tracks, clamped = {}, {}
    contact_owned = set(_two_hand_joints(rig)) if facts.two_hands else set()
    for j, (kind, hinge) in _channel_kinds(rig).items():
        name = rig.names[j]
        still = sorted(held.get(name, set()) | held.get('*', set()))
        settles = settle_at is not None and masks[ticks[settle_at], j] > 0 and settle_at not in still
        if kind == 'digit':
            tracks[j] = _Track(j, 'digit', ticks, q[:, j], q[:, j], q[:, j])
            continue
        if kind in ('free', 'muscle'):
            if kind == 'free':
                values = _lifted(name, ticks, q[:, j].copy(), facts.cycle, keyset,
                                 facts.two_hands if name in contact_owned else None, notes)
                end = _exit_slope(facts, keyset, j, values[-1])
                slopes = _catmull_rom(ticks, values, gain, facts.cycle, 2 * np.sin(flat / 4), end=end)
            else:
                zero = rig.humanoid[j][0]['zero']
                axes, lower, upper = rig.humanoid_bounds(j)
                values = _continuous_rotvecs((Rotation.from_quat(q[:, j]) * zero.inv()).as_rotvec())
                end = _exit_slope(facts, keyset, j, None, zero)
                slopes = _catmull_rom(ticks, values, gain, facts.cycle, flat, end=end)
            if settles and not slopes[settle_at].any():
                slopes[settle_at] = spec.settle * masks[ticks[settle_at], j] * _settle_slope(ticks, values, settle_at, gain)
                carried = np.linalg.norm(slopes[settle_at]) * (2 if kind == 'free' else 1)
                if carried > 1e-9:
                    settled[name] = float(np.degrees(carried))
            slopes[still] = 0
            if kind == 'free':
                tracks[j] = _Track(j, 'free', ticks, values, slopes, slopes)
                continue
            slopes, kept = _keep_in_range(ticks, values, slopes, axes, lower, upper)
            if kept:
                clamped[name] = kept
            tracks[j] = _Track(j, 'muscle', ticks, values, slopes, slopes, zero=zero, axes=axes, bounds=(lower, upper))
            continue
        axis = rig.hinges[hinge][4]
        rest = Rotation.from_quat(rig.bind_q[j])
        theta, swing = _split_hinge(Rotation.from_quat(q[:, j]) * rest.inv(), axis)
        degrees = np.degrees(theta)
        arriving, leaving = _hinge_slopes(ticks, degrees, spec.turnover, facts.cycle)
        swing_slopes = _catmull_rom(ticks, swing, gain, facts.cycle, flat)
        if settles and arriving[settle_at] == 0 and leaving[settle_at] == 0:
            carry = spec.settle * masks[ticks[settle_at], j] * _settle_slope(ticks, degrees, settle_at, gain)
            floor = min(degrees[settle_at], 0.0) - rig.reversed_allowance(hinge)
            overshoot = carry * (ticks[settle_at + 1] - ticks[settle_at]) * 4 / 27
            if degrees[settle_at] + overshoot < floor:  # never settle a straight knee or elbow into a reversed bend
                carry *= (degrees[settle_at] - floor) / -overshoot
            arriving[settle_at] = leaving[settle_at] = carry
            if abs(carry) > 1e-9:
                settled[name] = float(abs(carry))
            if not swing_slopes[settle_at].any():
                swing_slopes[settle_at] = spec.settle * _settle_slope(ticks, swing, settle_at, gain)
        arriving[still] = leaving[still] = 0
        swing_slopes[still] = 0
        values = np.column_stack([theta, swing])
        slope_in = np.column_stack([np.radians(arriving), swing_slopes])
        slope_out = np.column_stack([np.radians(leaving), swing_slopes])
        tracks[j] = _Track(j, 'hinge', ticks, values, slope_in, slope_out, axes=axis, rest=rest)
    return tracks, clamped


def _lifted(name, ticks, values, cycle, keyset, owned=None, notes=None):
    """Key quaternions with signs lifted in sequence (never reset, so a full flip keeps its lift). A step too close to
    half a turn raises, naming its keys, unless `owned` (first, last) is a two_hands span that holds the whole step:
    the contact solve after flow decides those ticks, so the shorter way is kept and noted."""
    for i in range(1, len(values)):
        if values[i] @ values[i - 1] < 0:
            values[i] = -values[i]
        angle = np.degrees(2 * np.arccos(np.clip(abs(values[i] @ values[i - 1]), 0, 1)))
        if angle > AMBIGUOUS_DEGREES:
            if owned and owned[0] <= ticks[i - 1] and ticks[i] <= owned[1]:
                notes.append(f'{name} turns {angle:.0f} degrees between keys {ticks[i - 1]} and {ticks[i]} inside '
                             'two_hands: kept the shorter way; the two-handed contact solve decides those ticks')
                continue
            raise ValueError(f'{name} turns {angle:.0f} degrees between keys {ticks[i - 1]} and {ticks[i]}: too close '
                             'to half a turn to tell which way; add a breakdown key between them')
    if cycle is not None:
        a, b = keyset.ticks.index(cycle[0]), keyset.ticks.index(cycle[1])
        if values[b] @ values[a] < 0:
            raise ValueError(f'{name} makes a full turn inside the loop {cycle}; a loop cannot')
        values[b] = values[a]
    return values


def _split_hinge(relative: Rotation, axis: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """Swing-twist split of q * rest^-1 about the hinge axis: (theta radians, unwrapped over the keys; swing rotvecs)."""
    quat = relative.as_quat()
    theta = np.unwrap(2 * np.arctan2(quat[:, :3] @ axis, quat[:, 3]))
    swing = (relative * Rotation.from_rotvec(theta[:, None] * axis).inv()).as_rotvec()
    return theta, swing


def _keep_in_range(ticks, values, slopes, axes, lower, upper):
    """Ease a muscle segment's slopes where its curve would leave the Humanoid range between keys: a deterministic
    search that keeps 0.75, 0.5, 0.25, then 0 of the slopes at both of the segment's keys (one slope per key, so the
    curve stays smooth). A key outside the range keeps its pose; the range widens to it. Returns (slopes, [(first key,
    last key, kept share)])."""
    project = np.linalg.pinv(axes.T)
    base, kept = slopes.copy(), {}
    for _ in range(4 * len(ticks)):
        slopes = base * np.array([min(kept.get(i - 1, 1.0), kept.get(i, 1.0)) for i in range(len(ticks))])[:, None]
        worst = None
        for i in range(len(ticks) - 1):
            t = np.linspace(ticks[i], ticks[i + 1], 2 * (ticks[i + 1] - ticks[i]) + 1)
            angles = _hermite(ticks, values, slopes, slopes, t) @ project.T
            ends = values[i:i + 2] @ project.T
            lo, hi = np.minimum(lower, ends.min(axis=0)), np.maximum(upper, ends.max(axis=0))
            if (angles < lo - 1e-9).any() or (angles > hi + 1e-9).any():
                worst = i
                break
        if worst is None:
            break
        kept[worst] = max(0.0, kept.get(worst, 1.0) - 0.25)
    return slopes, [(int(ticks[i]), int(ticks[i + 1]), share) for i, share in sorted(kept.items())]


def _exit_slope(facts: _MoveFacts, keyset: _KeySet, j: int, last_value, zero: Rotation | None = None):
    """Arriving slope at a single-tick exit: half the exit reference's own first step (the next clip's motion)."""
    if facts.exit_motion is None or keyset.ticks[-1] != facts.n_ticks - 1:
        return None
    a, b = (Rotation.from_quat(p.q[j]) for p in facts.exit_motion)
    if zero is not None:
        return 0.5 * ((b * zero.inv()).as_rotvec() - (a * zero.inv()).as_rotvec())
    qa, qb = a.as_quat(), b.as_quat()
    qb = qb if qb @ qa >= 0 else -qb
    sign = 1.0 if qa @ last_value >= 0 else -1.0
    return 0.5 * sign * (qb - qa)


def _held_keys(keyset: _KeySet, spec: FlowSpec) -> dict[str, set[int]]:
    """Joint name or '*' -> key indices inside a hold range (those keys get zero slopes: the joint stops on them)."""
    return {joint: {i for i, t in enumerate(keyset.ticks) if any(a <= t <= b for a, b in ranges)}
            for joint, ranges in spec.holds.items()}


def _hips_path(keyset: _KeySet, facts: _MoveFacts, spec: FlowSpec, masks: np.ndarray,
               settled: dict[str, float]) -> np.ndarray:
    """(n_ticks, 3) hips local translation on the same Hermite as the rotations, settle included."""
    j = keyset.rig.index('Hips')
    ticks = np.array(keyset.ticks)
    values = np.array([p.t[j] for p in keyset.poses])
    slopes = _catmull_rom(ticks, values, spec.tangents, facts.cycle, FLAT_METRES)
    settle_at = _settle_key(keyset, facts) if spec.settle > 0 else None
    if settle_at is not None and masks[ticks[settle_at], j] > 0 and not slopes[settle_at].any():
        slopes[settle_at] = spec.settle * masks[ticks[settle_at], j] * _settle_slope(ticks, values, settle_at,
                                                                                     spec.tangents)
    held = _held_keys(keyset, spec)
    slopes[sorted(held.get('Hips', set()) | held.get('*', set()))] = 0
    return _hermite(ticks, values, slopes, slopes, np.arange(keyset.n_ticks))


# ----------------------------------------------------------------------------- private: envelopes and masks
def _between_keys(ticks, n, scale=None) -> np.ndarray:
    """(n,) 16u^2(1-u)^2 in each key segment: 1 halfway, 0 with zero slope on every key. `scale(a, b)` per segment."""
    out = np.zeros(n)
    for a, b in zip(ticks[:-1], ticks[1:]):
        u = (np.arange(a, b + 1) - a) / (b - a)
        out[a:b + 1] = 16 * u ** 2 * (1 - u) ** 2 * (scale(a, b) if scale else 1.0)
    return out


def _bump(u, peak):
    """u^(6p) (1-u)^(6(1-p)) scaled to 1 at u = p (p in [1/3, 2/3]): zero with zero slope and curvature at 0 and 1, and
    its derivative shape (zero at both ends and at the peak, largest 1), for a quadrature swirl."""
    p = float(np.clip(peak, 1 / 3, 2 / 3))
    u = np.clip(u, 0, 1)
    a, b = 6 * p, 6 * (1 - p)
    top = p ** a * (1 - p) ** b
    value = u ** a * (1 - u) ** b / top
    fine = np.linspace(1e-6, 1 - 1e-6, 401)
    scale = np.abs(fine ** a * (1 - fine) ** b * (a / fine - b / (1 - fine))).max()
    with np.errstate(divide='ignore', invalid='ignore'):
        slope = np.where((u > 0) & (u < 1), u ** a * (1 - u) ** b * (a / u - b / (1 - u)), 0.0) / scale
    return value, slope


def _plateau(ticks, n, ramp=3.0) -> np.ndarray:
    """(n,) 0 on every key, easing (zero slope) to 1 within `ramp` ticks of it, or halfway on shorter segments."""
    out = np.zeros(n)
    for a, b in zip(ticks[:-1], ticks[1:]):
        t = np.arange(a, b + 1)
        x = np.clip(np.minimum(t - a, b - t) / min(ramp, (b - a) / 2), 0, 1)
        out[a:b + 1] = x * x * (3 - 2 * x)
    return out


def _masks(keyset: _KeySet, facts: _MoveFacts, spec: FlowSpec) -> tuple[np.ndarray, np.ndarray]:
    """((n, J) weights where generated layers may act, (n, J) weights outside explicit holds), 0..1 with
    MASK_FADE_TICKS fades at range edges. Layers stay off: on snap_exempt joints and props; on both arms, hand digits
    and the weapon inside two_hands; on the hips and the planted legs (they would slide planted feet); on the strike
    chain inside the active window (the hit path stays the keyed one); and inside holds ('*' = every joint)."""
    rig, n = keyset.rig, keyset.n_ticks
    t = np.arange(n)
    everything = ((0, n - 1),)
    blocked: dict[int, list[Range]] = {}

    def block(joints, ranges):
        for j in joints:
            blocked.setdefault(j, []).extend(ranges)

    block([rig.index(name) for name in facts.exempt if name in rig.names], everything)
    if facts.two_hands:
        block([rig.index(name) for name in _two_hand_joints(rig)], (facts.two_hands,))
    if facts.planted:
        legs = {rig.index('Hips')}
        for joint in facts.planted:
            j = rig.index(joint)
            legs |= set(rig.descendants(j))
            while j >= 0 and rig.names[j] != 'Hips':
                legs.add(j)
                j = int(rig.parents[j])
        block(sorted(legs), everything)
    if facts.active and facts.strike:
        chain = set()
        for joint in facts.strike:
            j = rig.index(joint)
            while j >= 0 and not rig.names[j].startswith(('Spine', 'Hips', 'Neck', 'Chest', 'Head')):
                chain.add(j)
                j = int(rig.parents[j])
        block(sorted(chain), (facts.active,))
    holds: dict[int, list[Range]] = {}
    for joint, ranges in spec.holds.items():
        for j in (range(len(rig.names)) if joint == '*' else [rig.index(joint)]):
            holds.setdefault(j, []).extend(ranges)

    def weights(ranges_of):
        out = np.ones((n, len(rig.names)))
        for j, ranges in ranges_of.items():
            for a, b in ranges:
                gap = np.maximum(a - t, t - b)
                x = np.clip(gap / MASK_FADE_TICKS, 0, 1)
                out[:, j] *= np.where(gap <= 0, 0.0, x * x * (3 - 2 * x))
        return out

    held = weights(holds)
    for j, ranges in holds.items():
        blocked.setdefault(j, []).extend(ranges)
    return weights(blocked), held


# ----------------------------------------------------------------------------- private: layers
@dataclass
class _Layers:
    """What the layers ask for, accumulated before composing: per joint a time warp (ticks late) and a parent-frame
    rotation-vector offset (radians), and per layer the largest size per joint for the report."""
    warp: dict[int, np.ndarray]
    offset: dict[int, np.ndarray]
    sizes: dict[str, dict[str, float]]
    notes: list[str]
    n: int

    @classmethod
    def empty(cls, keyset: _KeySet) -> '_Layers':
        return cls({}, {}, {}, [], keyset.n_ticks)

    def add(self, layer: str, rig: Rig, j: int, offset: np.ndarray) -> None:
        self.offset[j] = self.offset.get(j, np.zeros((self.n, 3))) + offset
        size = float(np.degrees(np.linalg.norm(offset, axis=1).max(initial=0)))
        if size > 1e-6:
            sizes = self.sizes.setdefault(layer, {})
            sizes[rig.names[j]] = max(size, sizes.get(rig.names[j], 0.0))


def _two_hand_joints(rig: Rig) -> list[str]:
    """Both arms with hands and digits, and the weapon: what a two_hands contact solve owns."""
    return [name for name in rig.names if name.startswith(('Weapon', 'blade')) or any(
        name.startswith(side + part) for side in ('Left', 'Right') for part in ('Shoulder', 'Arm', 'ForeArm', 'Hand'))]


def _unsided(name: str) -> str:
    return name[4:] if name.startswith('Left') else name[5:] if name.startswith('Right') else name


def _lag_warp(ticks, n, delay, fast_windows=(), mask=None, speed=None, limit=None) -> tuple[np.ndarray, set[Range]]:
    """The lag engine (flow's lag and Clip.overlap(preserve_ticks=...)): (n,) ticks late, delay x E(u) in each key
    segment with E = 16u^2(1-u)^2 (zero with zero slope on every key), FAST_SHARE of it in segments touching a fast
    window, at most LAG_SHARE of the segment, times `mask`; halved where a fading mask would turn time back. The warp
    speeds the joint up by up to 1 + 3.08 d/h while it catches up: with the joint's own `speed` (deg/tick, row i = tick
    i to i+1) and the snap `limit`, d stays where the fastest tick keeps under 0.9 of the limit. Returns (warp, the
    segments whose delay was capped)."""
    warp, capped = np.zeros(n), set()
    for a, b in zip(ticks[:-1], ticks[1:]):
        fast = any(a <= w1 and w0 <= b for w0, w1 in fast_windows)
        d = delay * (FAST_SHARE if fast else 1.0)
        if abs(d) > LAG_SHARE * (b - a):
            capped.add((a, b))
            d = np.sign(d) * LAG_SHARE * (b - a)
        if speed is not None and not fast:
            fastest = float(speed[a:b].max(initial=0))
            room = max(0.0, (0.9 * limit / fastest - 1) * (b - a) / 3.08) if fastest > 1e-9 else abs(d)
            if abs(d) > room:
                capped.add((a, b))
                d = np.sign(d) * room
        u = (np.arange(a, b + 1) - a) / (b - a)
        warp[a:b + 1] = d * 16 * u ** 2 * (1 - u) ** 2
    if mask is not None:
        warp *= mask
    for a, b in zip(ticks[:-1], ticks[1:]):
        while (np.diff(np.arange(a, b + 1) - warp[a:b + 1]) < 0.05).any():
            warp[a:b + 1] *= 0.5
    return warp, capped


def _lag(layers, keyset, tracks, base, masks, held, facts, spec) -> None:
    """Chain follow-through as a time warp: joint j plays its own curve (never another layer's output) at t - warp,
    with LAG_PEAKS x spec.lag peaking between keys (_lag_warp), times the mask."""
    if spec.lag <= 0:
        return
    rig, n = keyset.rig, keyset.n_ticks
    ticks = [t for t in keyset.ticks if facts.exit_from is None or t <= facts.exit_from]  # the exit hold stays still
    speed = base.angular_speed()
    capped = set()
    for j, track in tracks.items():
        peak = LAG_PEAKS.get(_unsided(rig.names[j]), 0.0) * spec.lag
        if track.kind == 'digit' or peak <= 0:
            continue
        warp, short = _lag_warp(ticks, n, peak, facts.fast_windows, masks[:, j], speed[:, j], facts.snap_limit)
        capped |= short
        if warp.max() > 1e-9:
            layers.warp[j] = warp
            layers.sizes.setdefault('lag', {})[rig.names[j]] = float(warp.max())
    if capped:
        layers.notes.append('lag capped (a fifth of the segment, or the snap limit) on keys ' + ', '.join(
            f'{a}..{b}' for a, b in sorted(capped)))


def _overlap_between(clip: Clip, delays: Mapping[str, float], preserve_ticks: Sequence[int],
                     cycle: Range | None) -> Clip:
    """Clip.overlap(preserve_ticks=...): each joint samples its own dense curve at t - _lag_warp, so it trails between
    the given ticks and keeps them exactly. Humanoid-limited joints interpolate in muscle space (no collarbone roll),
    others by slerp between neighbouring ticks."""
    n, rig = len(clip), clip.rig
    ticks = sorted({int(t) for t in preserve_ticks} | (set(cycle) if cycle else set()))
    if ticks[0] < 0 or ticks[-1] > n - 1:
        raise ValueError(f'preserve_ticks must lie in 0..{n - 1}')
    out = Clip(rig, clip.t.copy(), clip.q.copy(), clip.s.copy(), clip.frame)
    limited = set(rig.humanoid_limited)
    for joint, delay in delays.items():
        j = rig.index(joint)
        tau = np.arange(n) - _lag_warp(ticks, n, float(delay))[0]
        i = np.clip(np.floor(tau).astype(int), 0, n - 2)
        f = (tau - i)[:, None]
        if j in limited:
            zero = rig.humanoid[j][0]['zero']
            v = (Rotation.from_quat(clip.q[:, j]) * zero.inv()).as_rotvec()
            q = (Rotation.from_rotvec(v[i] * (1 - f) + v[i + 1] * f) * zero).as_quat()
        else:
            a, b = clip.q[i, j], clip.q[i + 1, j]
            b = np.where(np.sum(a * b, axis=1, keepdims=True) < 0, -b, b)
            angle = np.arccos(np.clip(np.abs(np.sum(a * b, axis=1, keepdims=True)), 0, 1))
            sin = np.where(angle < 1e-9, 1.0, np.sin(angle))
            q = np.where(angle < 1e-9, a * (1 - f) + b * f, (np.sin((1 - f) * angle) * a + np.sin(f * angle) * b) / sin)
        q /= np.linalg.norm(q, axis=1, keepdims=True)
        exact = np.abs(tau - np.rint(tau)) < 1e-12
        q[exact] = clip.q[np.rint(tau[exact]).astype(int), j]
        out.q[:, j] = q
    out._world = None
    return out


def _parent_rotations(rig: Rig, world: np.ndarray, j: int) -> np.ndarray:
    """(n, 3, 3) row-convention rotations of joint j's parent frame: v_parent = v_world @ R.T."""
    p = int(rig.parents[j])
    return rotation_part(world[:, p]) if p >= 0 else np.repeat(rotation_part(rig.skel_to_world)[None], len(world), 0)


def _drag(layers, keyset, tracks, base, masks, held, facts, spec) -> None:
    """Inertia: a joint keeps part of its world orientation while its parent turns, offset -g x the parent's angular
    velocity in the parent frame (g = half its LAG_PEAKS ticks x spec.drag), at most DRAG_MAX_DEGREES, times the
    between-keys envelope and the mask. It moves limbs the keys park while the body turns, which lag cannot."""
    if spec.drag <= 0:
        return
    rig, n = keyset.rig, keyset.n_ticks
    world = base.world()
    envelope = _between_keys(keyset.ticks, n)
    for j, track in tracks.items():
        peak = LAG_PEAKS.get(_unsided(rig.names[j]), 0.0)
        if track.kind == 'digit' or peak <= 0:
            continue
        rows = _parent_rotations(rig, world, j)
        cols = Rotation.from_matrix(np.swapaxes(rows, 1, 2))
        spin = np.zeros((n, 3))
        spin[1:-1] = (cols[2:] * cols[:-2].inv()).as_rotvec() / 2
        local = np.einsum('ti,tji->tj', spin, rows)
        offset = -0.5 * peak * spec.drag * local
        size = np.linalg.norm(offset, axis=1, keepdims=True)
        offset *= np.minimum(1.0, np.radians(DRAG_MAX_DEGREES) / np.maximum(size, 1e-12))
        layers.add('drag', rig, j, offset * (envelope * masks[:, j])[:, None])


def _arcs(layers, keyset, tracks, base, masks, held, facts, spec) -> None:
    """Pass-around where a joint reverses at a key (cos(chord in, chord out) < REVERSAL_COS): a sideways offset
    A n u(1-|u|)^2 / 0.148 around the key, zero on it and on its neighbours, passing it at A/h instead of stopping.
    n is the sideways part of where the joint goes next; A = spec.arcs x ARC_SHARE x the smaller chord, capped."""
    if spec.arcs <= 0:
        return
    rig, n = keyset.rig, keyset.n_ticks
    t = np.arange(n, dtype=float)
    for j, track in tracks.items():
        if track.kind not in ('muscle', 'free'):
            continue
        if track.kind == 'muscle':
            chords = np.diff(track.values, axis=0)
        else:
            r = Rotation.from_quat(track.values)
            chords = (r[1:] * r[:-1].inv()).as_rotvec()
        cap = np.radians(ARC_MAX_AXIAL if _unsided(rig.names[j]).startswith(('Spine', 'Neck', 'Head'))
                         else ARC_MAX_DEGREES)
        offset = np.zeros((n, 3))
        for i in range(1, len(track.ticks) - 1):
            c_in, c_out = chords[i - 1], chords[i]
            size_in, size_out = np.linalg.norm(c_in), np.linalg.norm(c_out)
            if min(size_in, size_out) < 1e-6 or c_in @ c_out / (size_in * size_out) >= REVERSAL_COS:
                continue
            along = c_in / size_in
            side = c_out - (c_out @ along) * along
            if np.linalg.norm(side) < 0.1 * size_out:
                side = np.cross(along, np.eye(3)[int(np.argmin(np.abs(along)))])
            side /= np.linalg.norm(side)
            amplitude = min(spec.arcs * ARC_SHARE * min(size_in, size_out), cap)
            a, k, b = track.ticks[i - 1], track.ticks[i], track.ticks[i + 1]
            u = np.where(t < k, (t - k) / (k - a), (t - k) / (b - k))
            shape = np.where(np.abs(u) < 1, u * (1 - np.abs(u)) ** 2, 0.0)
            offset += (amplitude / 0.148) * shape[:, None] * side
        layers.add('arcs', rig, j, offset * masks[:, j][:, None])


def _accent_joints(rig: Rig, accent: Accent) -> tuple[list[int], list[np.ndarray | None]]:
    names = (CHAINS[accent.joints] if isinstance(accent.joints, str) and accent.joints in CHAINS
             else (accent.joints,) if isinstance(accent.joints, str) else tuple(accent.joints))
    joints, axes = [], []
    for name in names:
        if name in rig.hinges:
            joints.append(rig.hinges[name][2])
            axes.append(rig.hinges[name][4])
        elif name in rig.names:
            j = rig.index(name)
            joints.append(j)
            axes.append(next((h[4] for h in rig.hinges.values() if h[2] == j), None))
        elif not (isinstance(accent.joints, str) and accent.joints in CHAINS):
            raise ValueError(f'Accent joint {name!r} is not on this rig (see rig.names, rig.hinges, flow.CHAINS)')
    if accent.axis == 'bend' and any(a is None for a in axes):
        raise ValueError(f"Accent {accent.joints!r}: axis='bend' needs hinges (knees, elbows, fingers)")
    return joints, axes


def _accents(layers, keyset, tracks, base, masks, held, facts, spec) -> None:
    """Each Accent: a bump between the keys either side of its tick, zero with zero slope on them, peaking `wave` ticks
    later for each joint up its chain; a character-frame axis is turned into each joint's parent frame per tick, 'bend'
    adds hinge flexion; the swirl adds the bump's slope shape on axis x the bone. Holds win; other masks do not apply,
    since an accent is an explicit request."""
    rig, n, ticks = keyset.rig, keyset.n_ticks, keyset.ticks
    world = base.world()
    t = np.arange(n, dtype=float)
    for accent in spec.accents:
        if not ticks[0] < accent.tick < ticks[-1] or accent.tick in ticks:
            raise ValueError(f'Accent at tick {accent.tick} must lie strictly between two keys {ticks}')
        i = int(np.searchsorted(ticks, accent.tick))
        a, c = ticks[i - 1], ticks[i]
        joints, hinge_axes = _accent_joints(rig, accent)
        chain = isinstance(accent.joints, str) and accent.joints == 'spine'
        weights = accent.weights or (SPINE_WEIGHTS if chain else (1.0,) * len(joints))
        u = (t - a) / (c - a)
        inside = ((t >= a) & (t <= c))[:, None]
        for k, (j, hinge_axis) in enumerate(zip(joints, hinge_axes)):
            value, slope = _bump(u, (accent.tick + k * accent.wave - a) / (c - a))
            degrees = accent.degrees * (weights[k] if k < len(weights) else 1.0)
            if accent.axis == 'bend':
                offset = np.radians(degrees) * value[:, None] * hinge_axis[None]
            else:
                rows = _parent_rotations(rig, world, j)
                axis_world = keyset.frame.dir_to_world(accent.axis)
                offset = np.radians(degrees) * value[:, None] * np.einsum('i,tji->tj', axis_world, rows)
                child = next((c_ for c_ in rig.children(j)), None)
                if accent.swirl and child is not None:
                    bone = world[:, child, 3, :3] - world[:, j, 3, :3]
                    second = np.cross(axis_world[None], bone)
                    second /= np.maximum(np.linalg.norm(second, axis=1, keepdims=True), 1e-12)
                    offset += (np.radians(degrees * accent.swirl) * slope[:, None]
                               * np.einsum('ti,tji->tj', second, rows))
            layers.add('accents', rig, j, offset * inside * held[:, j][:, None])


def _closing_axes(rig: Rig, side: str) -> list[tuple[int, np.ndarray, float]]:
    """(digit joint, parent-frame axis that closes it, share) for one hand, from fist() on the bind pose."""
    from .solve import fist
    open_ = Pose(rig, rig.bind_t, rig.bind_q, rig.bind_s)
    closed = fist(open_.copy(), side, amount=1.0, thumb=1.0)
    out = []
    for j in rig.humanoid_digits:
        if not rig.names[j].startswith(side + 'Hand'):
            continue
        turn = (Rotation.from_quat(closed.q[j]) * Rotation.from_quat(open_.q[j]).inv()).as_rotvec()
        if np.linalg.norm(turn) > np.radians(1):
            out.append((j, turn / np.linalg.norm(turn), THUMB_SHARE if 'Thumb' in rig.names[j] else 1.0))
    return out


def _finger_curl(layers, keyset, tracks, base, masks, held, facts, spec) -> None:
    """Fingers loosen and curl with each arm swing: per hand, spec.fingers x clip(FINGER_GAIN x the upper arm's
    smoothed speed FINGER_DELAY_TICKS ticks late, 0, FINGER_MAX_DEGREES) about each digit's closing axis (thumb x
    THUMB_SHARE), zero on keys, off for a hand that carries a weapon joint or holds two-handed (masks)."""
    rig, n = keyset.rig, keyset.n_ticks
    if spec.fingers <= 0 or not rig.humanoid_digits:
        return
    holding = {rig.side(name) for name in rig.names if name.startswith(('Weapon', 'blade'))}
    envelope = _plateau(keyset.ticks, n)
    speed = np.vstack([np.zeros((1, len(rig.names))), base.angular_speed()])
    for side in ('Left', 'Right'):
        if side in holding or side + 'Arm' not in rig.names:
            continue
        smooth = np.convolve(speed[:, rig.index(side + 'Arm')], np.ones(5) / 5, mode='same')
        late = np.r_[np.zeros(FINGER_DELAY_TICKS), smooth[:-FINGER_DELAY_TICKS]]
        curl = spec.fingers * np.clip(FINGER_GAIN * late, 0, FINGER_MAX_DEGREES) * envelope
        for j, axis, share in _closing_axes(rig, side):
            layers.add('fingers', rig, j, np.radians(curl * share * masks[:, j])[:, None] * axis[None])


# ----------------------------------------------------------------------------- private: compose and report
def _compose(keyset: _KeySet, tracks: Mapping[int, _Track], hips: np.ndarray, pchip: Clip, layers: _Layers,
             clamped: dict[str, list]) -> Clip:
    """The dense clip: each track at t - warp with its summed offset, the hips path, translations and scales from the
    pchip clip, edited digits snapped; then every key tick gets its key channels exactly. Guard: every warp and offset
    is 0 on the key ticks (an AssertionError here is a flow bug, not a user error)."""
    n, rig = keyset.n_ticks, keyset.rig
    t = np.arange(n, dtype=float)
    key_ticks = list(keyset.ticks)
    for name, values in (('warp', layers.warp), ('offset', layers.offset)):
        for j, v in values.items():
            if np.abs(v[key_ticks]).max(initial=0) > 1e-12:
                raise AssertionError(f'flow layer {name} on {rig.names[j]} is not zero on the keys')
    out = Clip(rig, pchip.t.copy(), pchip.q.copy(), pchip.s.copy(), keyset.frame)
    out.t[:, rig.index('Hips')] = hips
    lost = {}
    for j, track in tracks.items():
        when = t - layers.warp[j] if j in layers.warp else t
        offset = layers.offset[j].copy() if j in layers.offset else None
        q, missed, eased = track.evaluate(when, offset, pchip)
        out.q[:, j] = q
        if missed.max(initial=0) > 1e-6:
            lost[rig.names[j]] = float(missed.max())
        if eased:
            clamped[rig.names[j] + ' (layers)'] = eased
    digits = [rig.names[j] for j in rig.humanoid_digits if j in layers.offset]
    if digits:
        for tick in range(n):
            if tick not in key_ticks:
                pose = out.pose(tick)
                pose.snap_to_humanoid(digits)
                out.q[tick] = pose.q
    for tick, pose in zip(keyset.ticks, keyset.poses):
        out.t[tick], out.s[tick] = pose.t, pose.s
        out.q[tick] = _sign_like(pose.q / np.linalg.norm(pose.q, axis=1, keepdims=True), out.q[tick])
    out.q = _continuous_signs(out.q)
    out._world = None
    layers.sizes['lost'] = lost
    return out


def _continuous_signs(q: np.ndarray) -> np.ndarray:
    q = q.copy()
    for tick in range(1, len(q)):
        flip = np.sum(q[tick] * q[tick - 1], axis=1) < 0
        q[tick, flip] = -q[tick, flip]
    return q


def _measure(clip: Clip, pchip: Clip, keyset: _KeySet, facts: _MoveFacts, spec: FlowSpec, layers: _Layers,
             clamped: dict, started: float) -> FlowReport:
    from .curves import FLOW_JOINTS, flow as flow_numbers, flow_window
    window = flow_window(keyset.n_ticks, hold_from=facts.exit_from)
    rig = keyset.rig
    joints = [rig.index(j) for j in FLOW_JOINTS if j in rig.names]
    holds = []
    for a, b, pa, pb in zip(keyset.ticks, keyset.ticks[1:], keyset.poses, keyset.poses[1:]):
        if facts.exit_from is not None and a >= facts.exit_from:
            continue
        dots = np.abs(np.sum(pa.q[joints] * pb.q[joints], axis=1)).clip(0, 1)
        share = float(np.mean(np.degrees(2 * np.arccos(dots)) < 2.0))
        if share > 0.5 and b - a >= 4:
            holds.append((a, b, share))
    sizes = dict(layers.sizes)
    lost = sizes.pop('lost', {})
    return FlowReport(spec=spec, approved=key_drift(clip, {t: facts.approved[t] for t in keyset.approved}),
                      keys=dict(zip(keyset.ticks, keyset.poses)), flow_before=flow_numbers(pchip, window),
                      flow_after=flow_numbers(clip, window), window=window, layers=sizes, clamped=clamped, lost=lost,
                      holds=tuple(holds), notes=keyset.notes + tuple(layers.notes),
                      seconds=time.perf_counter() - started)
