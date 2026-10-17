"""Animation briefs: creative direction plus the machine-checkable clock and targets.

A brief is JSON. Paths are relative to the rig pack. Keys:
  name, rigpack, character, description (creative direction, verbatim)
  references {name: usda}, entry {reference, tick}, exit {from_tick} (exit pose = entry)
  clock {duration_ticks, contact_tick, active [first, last], iasa_tick}
  fast_windows [[first, last]], targets [...] (see checks.measure), images [...], review_ticks [...]
  planted [joints that must not drift, e.g. feet for grounded moves]
  strike [joints that deliver the hit, e.g. RightHand or bladeEnd: pick the review side, measure visibility]
  two_hands [first, last]: ticks where both hands hold the weapon (the left hand stays on grip.json's grip)
"""
from __future__ import annotations

import json
from functools import cached_property
from pathlib import Path

from .clip import CharFrame, Clip, Pose
from .rig import Rig

REPO = Path(__file__).resolve().parents[2]


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
        source = Rig(self.file(self.data['references'][name]))
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
        pose.frame = CharFrame.from_pose(pose)
        return pose

    @property
    def entry_frame(self):
        return self.entry_pose.frame

    def check_spec(self) -> dict:
        clock = self.data['clock']
        spec = {'duration_ticks': clock['duration_ticks'], 'entry': self.entry_pose,
                'fast_windows': self.data.get('fast_windows', []), 'targets': self.data.get('targets', [])}
        if 'exit' in self.data:
            spec['exit_from_tick'] = self.data['exit']['from_tick']
        if self.data.get('planted'):
            spec['planted'] = self.data['planted']
        if self.data.get('two_hands'):
            spec['two_hands'] = self.data['two_hands']
        return spec

    @property
    def images(self) -> list[Path]:
        return [self.file(p) for p in self.data.get('images', [])]

    @property
    def review_ticks(self) -> list[int]:
        return self.data.get('review_ticks') or [0, self.data['clock']['contact_tick'], self.data['clock']['duration_ticks']]

    @property
    def strike(self) -> list[str]:
        return self.data.get('strike', [])

    @property
    def review_views(self) -> tuple[str, ...]:
        """Profile, front and the three-quarter rear view from the striking side (right unless every
        strike joint is on the left)."""
        sides = {self.rig.side(j) for j in self.strike}
        return ('profile', 'front', 'three-quarter-left' if sides == {'Left'} else 'three-quarter-right')
