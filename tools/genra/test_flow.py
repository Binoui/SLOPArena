"""genra.flow tests: exact keys, Humanoid-legal motion, turns, loops, layers. Run with the GenRA environment:

    <genra python> -m unittest discover -s tools/genra -p 'test_*.py'

They need the FightGuy Unity rig pack (artifacts/genra-rigpacks/fightguy-unity) and skip without it.
"""
from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path

try:
    import numpy as np
    from pxr import Usd  # noqa: F401
except ImportError as error:  # plain CI interpreter
    raise unittest.SkipTest(f'GenRA environment unavailable: {error}')

TOOLS = Path(__file__).resolve().parents[1]
REPO = TOOLS.parent
sys.path.insert(0, str(TOOLS))
PACK = REPO / 'artifacts/genra-rigpacks/fightguy-unity'
FLIP_KICK = REPO / 'artifacts/genra-runs/20261029-210728-fightguy-a3-flip-kick/brief.json'
if not (PACK / 'rig.usda').exists():
    raise unittest.SkipTest('FightGuy rig pack not present')

from scipy.spatial.transform import Rotation  # noqa: E402

from genra.brief import Brief  # noqa: E402
from genra.checks import check_clip  # noqa: E402
from genra.clip import Clip  # noqa: E402
from genra.flow import Accent, approved_keys, flow, key_drift  # noqa: E402


class FlowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.TemporaryDirectory()
        cls.dir = Path(cls.tmp.name)
        base = cls.brief({}, 'base', duration=40)
        cls.rig, cls.entry = base.rig, base.entry_pose

    @classmethod
    def tearDownClass(cls):
        cls.tmp.cleanup()

    @classmethod
    def brief(cls, keys, name, duration, **extra) -> Brief:
        """A brief on the FightGuy pack whose approved keys are `keys` {tick: Pose} (none for {})."""
        data = {'name': name, 'rigpack': str(PACK), 'references': {'fall': 'references/fall.usda'},
                'entry': {'reference': 'fall', 'tick': 0}, 'clock': {'duration_ticks': duration}, **extra}
        if keys:
            Clip.from_keys(keys, duration + 1).save(cls.dir / f'{name}-keys.json')
            data['approved_keys'] = {'file': f'{name}-keys.json', 'ticks': sorted(keys)}
        path = cls.dir / f'{name}.json'
        path.write_text(json.dumps(data))
        brief = Brief(path)
        if hasattr(cls, 'rig'):
            brief.__dict__['rig'] = cls.rig
        return brief

    def swing_keys(self):
        """Arms, spine, knees and hips moving back and forth through five keys (reversals on the keys)."""
        keys = {0: self.entry.copy()}
        for tick, sign in ((10, 1), (20, -1), (30, 1)):
            pose = self.entry.copy()
            pose.rotate('Spine1', (1, 0, 0), 12 * sign).rotate('Hips', (0, 0, 1), 15 * sign)
            pose.rotate('LeftArm', (0, 1, 0), 35 * sign).rotate('RightArm', (1, 0, 0), -30 * sign)
            pose.rotate('LeftShoulder', (0, 0, 1), 8 * sign)
            pose.set_hinge('LeftElbow', 60 + 30 * sign).set_hinge('RightKnee', 50 + 25 * sign)
            keys[tick] = pose
        keys[40] = self.entry.copy()
        return keys

    def test_keys_land_exactly_on_their_ticks(self):
        keys = self.swing_keys()
        brief = self.brief(keys, 'exact', 40, exit={'from_tick': 40}, purpose='special')
        clip, report = flow(brief, lag=1, accents=[Accent('spine', tick=15, degrees=6, axis=(1, 0, 0))])
        drift = key_drift(clip, keys)
        self.assertEqual(sorted(report.approved), sorted(keys))
        self.assertLess(max(d.metres for d in drift.values()), 1e-6)
        self.assertLess(np.radians(max(d.degrees for d in drift.values())), 1e-6)
        self.assertLess(np.radians(max(d.digit_degrees for d in drift.values())), 1e-6)
        self.assertGreater(report.layers['lag']['LeftHand'], 0.5, 'the hand trails between keys')
        self.assertIn('Spine', report.layers['accents'])
        self.assertLess(check_clip(clip, brief.check_spec(), mode='fast')['metrics']['approved keys moved (m)'], 1e-6)

    def test_keys_pass_through_instead_of_stopping(self):
        keys = self.swing_keys()
        brief = self.brief(keys, 'through', 40, exit={'from_tick': 40}, purpose='special')
        _, report = flow(brief)
        for metric in ('stops per joint per second', 'most of the body stopping together (share)'):
            self.assertLess(report.flow_after[metric], report.flow_before[metric], metric)

    def test_collarbone_never_gains_roll(self):
        keys = self.swing_keys()
        brief = self.brief(keys, 'roll', 40, exit={'from_tick': 40}, purpose='special')
        shoulder = self.rig.index('LeftShoulder')
        keyed = max(self.rig.humanoid_split(shoulder, p.q[shoulder])[1] for p in keys.values())
        clip, report = flow(brief, drag=1, arcs=1, accents=[
            Accent(('LeftShoulder', 'LeftArm'), tick=25, degrees=10, axis=(0.6, 0.6, 0.5))])
        rolled = max(self.rig.humanoid_split(shoulder, q)[1] for q in clip.q[:, shoulder])
        self.assertLessEqual(rolled, keyed + 1e-6)
        self.assertGreater(report.lost.get('LeftShoulder', 0), 0.1, 'the roll it was asked for is reported, not played')

    def test_full_flip_keeps_turning_the_same_way(self):
        keys = {}
        for k, tick in enumerate(range(0, 41, 10)):
            keys[tick] = self.entry.copy().rotate('Hips', (1, 0, 0), 90 * k)
        brief = self.brief(keys, 'flip', 40, purpose='special')
        clip, _ = flow(brief)
        hips = clip.rig.index('Hips')
        turn = Rotation.from_quat(clip.q[:, hips]) * Rotation.from_quat(clip.q[0, hips]).inv()
        axis = turn[20].as_rotvec() / np.linalg.norm(turn[20].as_rotvec())
        quat = turn.as_quat()
        angle = np.degrees(np.unwrap(2 * np.arctan2(quat[:, :3] @ axis, quat[:, 3])))
        self.assertTrue((np.diff(angle) > -1e-6).all(), 'the flip never turns back')
        self.assertAlmostEqual(angle[-1] - angle[0], 360.0, places=3)

    def test_ambiguous_half_turn_names_the_tick(self):
        keys = {0: self.entry.copy(), 12: self.entry.copy().rotate('Hips', (0, 0, 1), 179.5), 24: self.entry.copy()}
        brief = self.brief(keys, 'half', 24, purpose='special')
        with self.assertRaisesRegex(ValueError, r'Hips turns 18\d degrees between keys 0 and 12'):
            flow(brief)
        warned = check_clip(Clip.from_keys(keys, 25), {'stage': 'keys', 'key_ticks': sorted(keys)}, mode='fast')
        self.assertEqual([w['ticks'] for w in warned['warnings'] if w['check'] == 'half-turn'], [[0, 12], [12, 24]],
                         'the keys check warns at approval time, before flow refuses the keys')

    def test_loop_seam_closes(self):
        keys = {0: self.entry.copy(), 14: self.entry.copy(), 28: self.entry.copy()}
        keys[7] = self.entry.copy().rotate('Spine1', (1, 0, 0), 10).rotate('LeftArm', (0, 1, 0), 30)
        keys[21] = self.entry.copy().rotate('Spine1', (1, 0, 0), -10).rotate('LeftArm', (0, 1, 0), -30)
        keys[14].set_hinge('LeftKnee', 70)
        brief = self.brief(keys, 'loop', 28, loop={'from_tick': 0}, purpose='loop')
        clip, report = flow(brief)
        result = check_clip(clip, brief.check_spec(), mode='fast')
        self.assertNotIn('loop', {f['check'] for f in result['failures']})
        self.assertLess(result['metrics']['loopSeamMetres'], 1e-6)
        self.assertLess(report.max_drift[0], 1e-6)

    def test_a_brief_without_keys_needs_them(self):
        brief = self.brief({}, 'nokeys', 20, purpose='special')
        with self.assertRaisesRegex(ValueError, 'flow needs keys'):
            flow(brief)
        clip, report = flow(brief, keys={0: self.entry.copy(), 10: self.entry.copy().rotate('Spine', (1, 0, 0), 9),
                                         20: self.entry.copy()})
        self.assertEqual(len(clip), 21)
        self.assertEqual(report.approved, {})

    def test_a_moved_approved_key_is_refused(self):
        keys = self.swing_keys()
        brief = self.brief(keys, 'moved', 40, exit={'from_tick': 40}, purpose='special')
        edited = dict(approved_keys(brief))
        edited[20] = edited[20].copy().rotate('Spine', (1, 0, 0), 20)
        with self.assertRaisesRegex(ValueError, 'approved key 20 moved'):
            flow(brief, keys=edited)

    def test_holds_keep_a_joint_still_on_their_keys(self):
        keys = {0: self.entry.copy(), 40: self.entry.copy()}
        for k, tick in enumerate((10, 20, 30), start=1):
            keys[tick] = self.entry.copy().rotate('LeftArm', (0, 1, 0), 20 * k)
        brief = self.brief(keys, 'held', 40, exit={'from_tick': 40}, purpose='special')
        free, _ = flow(brief)
        held, report = flow(brief, holds={'LeftArm': [(10, 30)]})
        arm = self.rig.index('LeftArm')
        for tick in (10, 20):
            around = lambda c: c.angular_speed()[tick - 1:tick + 1, arm].mean()
            self.assertLess(around(held), 0.5 * around(free), f'the held arm eases into key {tick}')
        self.assertLess(report.max_drift[0], 1e-6)
        still, _ = flow(brief, holds={'LeftArm': [(10, 30)]}, lag=0, fingers=0, settle=0)
        np.testing.assert_allclose(held.q[10:31, arm], still.q[10:31, arm], atol=1e-12, err_msg='no layers in a hold')

    def test_overlap_between_keys_keeps_them(self):
        keys = self.swing_keys()
        clip = Clip.from_keys(keys, 41)
        delays = {'LeftForeArm': 2, 'LeftHand': 3, 'LeftShoulder': 1.5}
        trailed = clip.overlap(delays, preserve_ticks=sorted(keys))
        forearm, shoulder = self.rig.index('LeftForeArm'), self.rig.index('LeftShoulder')
        for tick in keys:
            np.testing.assert_array_equal(trailed.q[tick], clip.q[tick])
        self.assertGreater(np.abs(trailed.q[15, forearm] - clip.q[15, forearm]).max(), 1e-3, 'trails between keys')
        keyed = max(self.rig.humanoid_split(shoulder, q)[1] for q in clip.q[:, shoulder])
        self.assertLessEqual(max(self.rig.humanoid_split(shoulder, q)[1] for q in trailed.q[:, shoulder]), keyed + 1e-6)
        shifted = clip.overlap(delays)
        self.assertGreater(np.abs(shifted.q[10, forearm] - clip.q[10, forearm]).max(), 1e-3, 'default still shifts')

    def test_runner_seeds_a_full_run_with_flow(self):
        from genra.run import prepare_brief, seed_candidate
        keys = self.swing_keys()
        source = self.brief(keys, 'seed', 40, exit={'from_tick': 40}, purpose='special')
        run = self.dir / 'seed-run'
        (run / 'scratch').mkdir(parents=True)
        prepared = prepare_brief(source, run / 'scratch', run)
        seeded = seed_candidate(prepared, run / 'scratch', run)
        self.assertTrue(seeded['source'].startswith('flow(b) through'), seeded)
        self.assertLess(seeded['flow']['after']['stops per joint per second'],
                        seeded['flow']['before']['stops per joint per second'])
        clip = Clip.load(self.rig, run / 'scratch/result.json', frame=prepared.entry_frame)
        self.assertLess(max(d.metres for d in key_drift(clip, keys).values()), 1e-5)
        self.assertIn('approved keys', (run / 'scratch/flow.txt').read_text())
        self.assertIn('## flow(brief', (run / 'scratch/API.md').read_text())

    @unittest.skipUnless(FLIP_KICK.exists(), 'Flip Kick run not present')
    def test_flip_kick_keys_stay_exact(self):
        brief = Brief(FLIP_KICK)
        clip, report = flow(brief)
        self.assertLess(report.max_drift[0], 1e-6)
        self.assertLess(np.radians(report.max_drift[1]), 1e-6)
        self.assertLess(report.flow_after['stops per joint per second'], report.flow_before['stops per joint per second'])


if __name__ == '__main__':
    unittest.main()
