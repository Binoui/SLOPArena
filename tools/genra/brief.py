"""Animation briefs: creative direction plus the machine-checkable clock and targets.

A brief is JSON. Paths are relative to the rig pack. Keys:
  name, rigpack, character, description (creative direction, verbatim)
  references {name: usda}, entry {reference, tick}, exit {from_tick} (exit pose = entry, or exit {from_tick, reference,
    tick} for a clip that ends where the next one starts, such as a jump ending on its fall loop's first pose)
  entry.match false: the entry pose only sets the character frame; tick 0 is free (e.g. a clip that blends
    in from several different reactions)
  loop {from_tick}: ticks from_tick..duration loop in the game; the last tick equals from_tick and the
    motion keeps its speed across the seam (Clip.from_keys(..., cycle=...))
  clock {duration_ticks, contact_tick, active [first, last], iasa_tick}; contact, active and IASA are
    for attacks and may be left out
  views [...]: review views for sheets (default profile, front, three-quarter from the striking side);
  preview_views [...]: views in the MP4 preview (default profile and the three-quarter view)
  fast_windows [[first, last]], targets [...] (see checks.measure), images [...], review_ticks [...]
  planted [joints that must not drift, e.g. feet for grounded moves], snap_degrees_per_tick (tightens the 35 default for
  a calm move), snap_exempt [joints the snap check skips: a prop that spins by design, e.g. the salamander's Hat rotor]
  strike [joints that deliver the hit, e.g. RightHand or bladeEnd: pick the review side, measure visibility]
  two_hands [first, last]: ticks where both hands hold the weapon (the left hand stays on grip.json's grip)
  two_hands_hold: 'exact' (default: grip.json's transform) or 'loose' (solve.handle_target: the left fist anywhere on
    the handle beside the right hand, free to turn and slide; the exact grip winged Sir Bonk's elbows in Barn Door)
  wrist_exempt {ticks [first, last] or [[first, last], ...], why}: ticks the wrist check skips, for a matched entry or
    exit whose own pose folds a wrist (the clip straightens it after them, or bends into it at the end); say why. A
    move that starts and ends on Sir Bonk's idle needs two ranges
  review {reference, travel}: review.py's defaults, the rig pack reference to compare with and the ground speed (metres
    per tick) that trails a cape in the renders
  frame: "facing" (default) puts the character frame's front on the fighter's facing, where the game moves,
    attacks and points the match camera; "hips" keeps the pre-2026-10-18 frame square to the entry hips,
    which fighting stances turn 40-90 degrees away (kept only so older briefs reproduce)
  timing {fixed [...], adjustable [...]}: what the game holds the clip to and what may change to suit the animation,
    as the owner decided before authoring (Sir Bonk's E1 squeezed a 148-tick leap into rules the owner was happy to
    change; nobody had asked). Shown to the animator and the director.
  keys [ticks]: the key poses a blocking stage authors for approval (run.py --stage keys); default the ticks the review
    beats map to, else review_ticks, always with the first and last tick
  approved_keys {file, ticks}: written by run.py --keys into the run's brief copy; the check keeps those poses
  props_shown {"Weapon<k>": [[first, last], ...]}: ticks a pack weapon (the k-th in mapping.json) shows in the Unity
    renders, as the game's hide rules show it; unlisted weapons always show. Sir Bonk's pot lid rides his shoulder
    (Weapon2) until the grab and his hand (Weapon1) until the release, so the renders draw it once
  meshes_shown {"<skinned mesh>": [[first, last], ...]}: ticks a skinned mesh draws in the Unity renders; unlisted pack
    meshes always draw. Nyx's swaps show one form before the flash and the other after it, and her hand-held pole only
    while it is out of the backpack (2026-10-29). A mesh outside the pack (the other form) is drawn only in its windows
"""
from __future__ import annotations

import json
import re
from dataclasses import dataclass
from functools import cached_property
from pathlib import Path

from .clip import CharFrame, Clip, Pose
from .rig import Rig

REPO = Path(__file__).resolve().parents[2]


PURPOSES = ('attack', 'special', 'idle', 'locomotion', 'loop', 'other')
NORMAL_SLOT = r'(?:^|[^a-z0-9])(?:[ga][1-4]|(?:ground|air)[. _-]?[1-4])(?:[^a-z0-9]|$)'


@dataclass(frozen=True)
class BriefWarning:
    rule: str
    message: str


def brief_purpose(data: dict) -> tuple[str, bool]:
    """Return the purpose and whether it was inferred from existing direction."""
    if 'purpose' in data:
        value = data['purpose']
        if value not in PURPOSES:
            raise ValueError(f'purpose must be one of {PURPOSES}, not {value!r}')
        return value, False
    name = data.get('name', '').lower()
    notes = data.get('notes', '').lower()
    if 'idle' in name:
        return 'idle', True
    if re.search(NORMAL_SLOT, name):
        return 'attack', True
    if re.search(r'walk|run|jump|fall|dash|locomotion', name):
        return 'locomotion', True
    if re.search(r'(?:^|[-_])(?:[ga]?[aqerf]|sr|special)(?:[-_]|$)', name):
        return 'special', True
    if re.search(r'\b(?:idle animation|idle loop|calm idle)\b', notes):
        return 'idle', True
    if re.search(r'anim\.[a-z0-9_-]+\.[ga]?[aqerf]\b|\([aqerf]\)|\bspecial (?:attack|move)\b', notes):
        return 'special', True
    if re.search(NORMAL_SLOT, notes):
        return 'attack', True
    if 'contact_tick' in data.get('clock', {}):
        return 'attack', True
    return ('loop' if 'loop' in data else 'other'), True


def lint_brief(data: dict) -> list[BriefWarning]:
    """Advisory direction checks, independent of rig files and model calls."""
    purpose, _ = brief_purpose(data)
    text = ' '.join(str(data.get(k, '')) for k in ('name', 'notes', 'description', 'timing')).lower()
    warnings = []
    def warn(rule, message):
        warnings.append(BriefWarning(rule, message))
    if purpose in ('attack', 'special'):
        clock = data.get('clock', {})
        game = clock.get('game_duration_ticks')
        if game is not None:
            ratio = clock['duration_ticks'] / game if game > 0 else 0
            slow = 1.7 <= ratio <= 2.3
        else:
            lengths = re.findall(r'(?:game (?:length|stage)(?: of)?|proposed game (?:length|stage))[^0-9]{0,12}(\d+)\s*(?:ticks?|[- ]tick)', text)
            slow = (any(1.7 <= clock['duration_ticks'] / int(n) <= 2.3 for n in lengths if int(n) > 0)
                    or bool(re.search(r'\b2\s*[x×]|twice|double speed|double the game', text)))
        if not slow:
            warn('author-slow', 'State an authored clock about 2x the game length (or explain the intentional exception).')
        strike = set(data.get('strike', []))
        targets = data.get('targets', [])
        positional = {'arc', 'char', 'ahead', 'below', 'distance', 'position'}
        if not any(t.get('kind') in positional and
                   (bool(strike & ({t.get('a'), t.get('b')} | set(t.get('joints', [])))) if strike
                    else t.get('kind') == 'arc' or any(
                        re.search(r'hand|foot|toe|weapon|blade|prop', str(t.get(k, '')), re.I) for k in ('a', 'b')))
                   for t in targets):
            warn('strike-target', 'Add position or arc targets for the striking part; identify it with strike.')
    if purpose == 'idle':
        calm = bool(re.search(r'calm|slow|quiet|subtle', text))
        budget = bool(data.get('snap_degrees_per_tick') or re.search(
            r'\d+\s*(?:deg|cm|mm|m/s|metres|meters)|(?:one|single|no|at most one)\s+(?:slow\s+)?gesture', text))
        if not (calm and budget):
            warn('idle-budget', 'State a calm, slow loop speed budget with at most one slow gesture.')
    normal = purpose == 'attack' and bool(re.search(NORMAL_SLOT, data.get('name', '').lower() + ' ' + data.get('notes', '').lower()))
    # Restrict personality lint to recovery direction, not the strike or the character biography.
    sentences = re.split(r'[\n.!?;]+', str(data.get('description', '')).lower())
    recovery = [s for s in sentences if re.search(r'\b(?:recover\w*|settle\w*|ending|return\w*|back to)\b', s)]
    # Remove only negated acting requests. A "cocky settle with no flourish" still conflicts.
    positive_recovery = [re.sub(
        r'\b(?:no|without|never|avoid)\s+(?:(?:new|extra|a|any)\s+)*(?:gestures?|flourish(?:es)?|poses?|taunts?|spins?)\b',
        '', sentence) for sentence in recovery]
    acting = r'\b(?:flourish|cocky|taunt|spin)\b|\bpose\b'
    positive_recovery = [re.sub(r'\b(?:idle|fall|entry|ready|reference|stance|airborne) pose\b', '', sentence)
                         for sentence in positive_recovery]
    if normal:
        if not any(re.search(r'plain|short|quick|calm|direct|shortest', s) for s in recovery):
            warn('normal-recovery', 'Ask for a plain, short recovery; personality can live in the move itself.')
        if any(re.search(acting, s) for s in positive_recovery):
            warn('recovery-flourish', 'Recovery asks for a flourish, pose, cocky settle, taunt, or spin.')
    if re.search(r'no (?:gestures?|flourish(?:es)?)', text) and any(
            re.search(r'cocky|taunt|flourish', s) for s in positive_recovery):
        warn('contradiction', 'Recovery personality conflicts with the no-flourish direction.')
    return warnings


def print_preflight(data: dict):
    purpose, inferred = brief_purpose(data)
    print(f'Brief purpose: {purpose}' + (' (inferred from name/notes; set purpose to override)' if inferred else ''))
    for finding in lint_brief(data):
        print(f'WARNING [{finding.rule}]: {finding.message}')


def resolved_brief_path(brief_path, result) -> Path:
    """A saved authoring result is checked against its run's brief, including --keys.

    Older runs kept the key file only in scratch/. Use that copy only when its
    brief still agrees with the run snapshot; never silently drop a missing lock.
    Standalone clips use the supplied brief.
    """
    result = Path(result).resolve()
    run = result.parent.parent
    snapshot = run / 'brief.json'
    if (result.name != 'result.json' or result.parent.name != 'scratch'
            or not (run / 'run-config.json').is_file()):
        return Path(brief_path).resolve()
    data = json.loads(snapshot.read_text())
    keys = data.get('approved_keys')
    if keys and not (run / keys['file']).is_file():
        archived = result.parent / 'brief.json'
        if (json.loads(archived.read_text()) != data
                or not (archived.parent / keys['file']).is_file()):
            raise ValueError(f'{run}: the saved approved keys are missing or the brief copies disagree')
        return archived
    return snapshot


def exempt_ranges(ticks) -> tuple[tuple[int, int], ...]:
    """Tick ranges from one [first, last] pair or a list of pairs (wrist_exempt), as ((first, last), ...)."""
    if not ticks:
        return ()
    if all(isinstance(t, (int, float)) for t in ticks):
        ticks = [ticks]
    return tuple((int(a), int(b)) for a, b in ticks)


class Brief:
    def __init__(self, path, rigpack=None):
        self.path = Path(path)
        self.data = json.loads(self.path.read_text())
        given = Path(rigpack or self.data['rigpack'])
        candidates = [given] if given.is_absolute() else [Path.cwd() / given, REPO / given, self.path.parent / given]
        self.rigpack = next((c.resolve() for c in candidates if (c / 'rig.usda').exists()), candidates[0])

    def __getitem__(self, key):
        return self.data[key]

    def get(self, key, default=None):
        return self.data.get(key, default)

    def file(self, rel) -> Path:
        return self.rigpack / rel

    @cached_property
    def rig(self) -> Rig:
        return Rig(self.file('rig.usda'))

    def reference(self, name) -> Clip:
        """A reference clip on the brief's skeleton: one the brief lists, or any in the rig pack's references/."""
        source = Rig(self.file(self.data['references'].get(name, f'references/{name}.usda')))
        clip = Clip.from_reference(source)
        clip.rig = self.rig  # same skeleton; evaluate against the rig pack
        clip.frame = self.entry_frame
        return clip

    @cached_property
    def entry_pose(self) -> Pose:
        e = self.data['entry']
        source = Rig(self.file(self.data['references'][e['reference']]))
        t, q, s = source.sample_local([e['tick']])
        pose = Pose(self.rig, t[0], q[0], s[0])
        pose.frame = CharFrame.from_pose(pose, facing=self.frame_kind == 'facing')
        return pose

    @cached_property
    def exit_pose(self) -> Pose:
        """The pose the clip ends on: the entry pose, or exit.reference's pose at exit.tick (in the entry frame)."""
        e = self.data.get('exit', {})
        if 'reference' not in e:
            return self.entry_pose
        source = Rig(self.file(self.data['references'].get(e['reference'], f"references/{e['reference']}.usda")))
        t, q, s = source.sample_local([e.get('tick', 0)])
        pose = Pose(self.rig, t[0], q[0], s[0])
        pose.frame = self.entry_frame
        return pose

    @property
    def frame_kind(self) -> str:
        """'facing' (default): front is the fighter's facing. 'hips': front is square to the entry hips, as briefs
        before 2026-10-18 were authored; it is 40-90 degrees off the facing for fighting stances."""
        kind = self.data.get('frame', 'facing')
        if kind not in ('facing', 'hips'):
            raise ValueError(f'frame must be "facing" or "hips", not {kind!r}')
        return kind

    @property
    def entry_frame(self):
        return self.entry_pose.frame

    @property
    def entry_matches(self) -> bool:
        """False when the entry pose only sets the character frame and tick 0 is free."""
        return self.data['entry'].get('match', True)

    @property
    def loop_from(self) -> int | None:
        return self.data['loop']['from_tick'] if 'loop' in self.data else None

    @property
    def split_tick(self) -> int:
        """Where review motion paths split in two: contact for attacks, else the loop start, else the middle."""
        clock = self.data['clock']
        if 'contact_tick' in clock:
            return clock['contact_tick']
        return self.loop_from if self.loop_from is not None else clock['duration_ticks'] // 2

    def check_spec(self, *, stage=None) -> dict:
        clock = self.data['clock']
        spec = {'duration_ticks': clock['duration_ticks'],
                'fast_windows': self.data.get('fast_windows', []), 'targets': self.data.get('targets', [])}
        stage = stage or self.data.get('authoring_stage', 'full')
        if stage not in ('full', 'keys'):
            raise ValueError(f'authoring stage must be full or keys, not {stage!r}')
        if stage == 'keys':
            spec.update(stage='keys', key_ticks=list(self.key_beats))
        if self.entry_matches:
            spec['entry'] = self.entry_pose
        if 'exit' in self.data:
            spec['exit_from_tick'] = self.data['exit']['from_tick']
            spec['exit'] = self.exit_pose
        if self.loop_from is not None:
            spec['loop_from_tick'] = self.loop_from
        if self.data.get('snap_degrees_per_tick'):  # a calm move tightens the 35 deg/tick default (salamander reload)
            spec['snap_degrees_per_tick'] = float(self.data['snap_degrees_per_tick'])
        if self.data.get('snap_exempt'):
            spec['snap_exempt'] = list(self.data['snap_exempt'])
        if self.data.get('planted'):
            spec['planted'] = self.data['planted']
        if self.data.get('two_hands'):
            spec['two_hands'] = self.data['two_hands']
        if self.data.get('two_hands_hold'):
            if self.data['two_hands_hold'] not in ('exact', 'loose'):
                raise ValueError(f"two_hands_hold is 'exact' or 'loose', not {self.data['two_hands_hold']!r}")
            spec['two_hands_hold'] = self.data['two_hands_hold']
        if self.data.get('wrist_exempt'):
            spec['wrist_exempt_ticks'] = self.wrist_exempt_ranges
        if self.data.get('approved_keys'):
            keys = self.data['approved_keys']
            spec['keys'] = {'clip': Clip.load(self.rig, self.path.parent / keys['file'], frame=self.entry_frame),
                            'ticks': keys['ticks']}
        return spec

    @property
    def wrist_exempt_ranges(self) -> tuple[tuple[int, int], ...]:
        """The wrist exemption as (first, last) tick ranges: one [first, last] pair or a list of them."""
        return exempt_ranges((self.data.get('wrist_exempt') or {}).get('ticks'))

    @property
    def timing(self) -> dict:
        """What the game holds the clip to ('fixed') and what may change to suit the animation ('adjustable')."""
        return self.data.get('timing') or {}

    def timing_text(self) -> str:
        """The timing contract in one paragraph, or '' without one."""
        t = self.timing
        parts = [f"{label}: {'; '.join(t[key])}" for key, label in (('fixed', 'Fixed by the game'),
                                                                     ('adjustable', 'Adjustable to suit the animation'))
                 if t.get(key)]
        return '. '.join(parts) + ('.' if parts else '')

    @property
    def key_beats(self) -> dict[int, str]:
        """The key poses a blocking stage authors for approval, tick -> beat label: the brief's keys, else the ticks
        its review beats map to, else its review ticks; always with the first and last tick."""
        n = self.data['clock']['duration_ticks']
        beats = (self.data.get('review') or {}).get('beats')
        named = {int(t): label for label, t in beats['ticks'].items()} if beats else {}  # labels listed keys too
        ticks = self.data.get('keys') or list(named) or self.review_ticks
        out = {int(t): named.get(int(t), '') for t in ticks}
        out.setdefault(0, 'entry')
        out.setdefault(n, 'end')
        return dict(sorted(out.items()))

    @property
    def images(self) -> list[Path]:
        return [self.file(p) for p in self.data.get('images', [])]

    @property
    def review_ticks(self) -> list[int]:
        return self.data.get('review_ticks') or [0, self.split_tick, self.data['clock']['duration_ticks']]

    @property
    def strike(self) -> list[str]:
        return self.data.get('strike', [])

    @property
    def review_views(self) -> tuple[str, ...]:
        """Profile, front and the three-quarter rear view from the striking side (right unless every
        strike joint is on the left), unless the brief lists its own views."""
        if self.data.get('views'):
            return tuple(self.data['views'])
        sides = {self.rig.side(j) for j in self.strike}
        return ('profile', 'front', 'three-quarter-left' if sides == {'Left'} else 'three-quarter-right')

    @property
    def preview_views(self) -> tuple[str, ...]:
        return tuple(self.data.get('preview_views') or self.review_views[::2])
