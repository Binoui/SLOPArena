"""genra toolkit tests. Run with the GenRA environment from the repository root:

    <genra python> -m unittest discover -s tools/genra -p 'test_*.py'

They skip without numpy/scipy/usd-core (plain CI Python) or without the local Manki rig
pack (artifacts/genra-rigpacks/manki-sr199). Comparisons with earlier runs skip when
their artifacts are absent.
"""
from __future__ import annotations

import json
import math
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

try:
    import numpy as np
    from pxr import Usd
except ImportError as error:  # plain CI interpreter
    raise unittest.SkipTest(f'GenRA environment unavailable: {error}')

TOOLS = Path(__file__).resolve().parents[1]
REPO = TOOLS.parent
sys.path.insert(0, str(TOOLS))
BRIEF = TOOLS / 'genra/benchmarks/manki-aerial-double-fist.json'
RIGPACK = REPO / 'artifacts/genra-rigpacks/manki-sr199'
CODEX = REPO / 'artifacts/genra-manki-aerial-20261014/generated/aerial.usda'
OPUS_AB = REPO / 'artifacts/genra-opus-ab-20261016/generated/aerial.usda'

if not (RIGPACK / 'rig.usda').exists():
    raise unittest.SkipTest('Manki rig pack not present')

from genra.brief import Brief  # noqa: E402
from genra.checks import check_clip  # noqa: E402
from genra.clip import Clip  # noqa: E402
from genra.render import contact_sheet, motion_overview, preview_video, strike_visibility  # noqa: E402
from genra.rig import Rig, rotation_part  # noqa: E402
from genra.skin import Skin  # noqa: E402
from genra.solve import fist, solve, two_bone  # noqa: E402
from genra.usdio import write_usd  # noqa: E402


def imported(brief: Brief, path: Path) -> Clip:
    source = Clip.from_reference(Rig(path))
    return Clip(brief.rig, source.t, source.q, source.s, brief.entry_frame)


class GenraTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.brief = Brief(BRIEF)
        cls.rig = cls.brief.rig
        cls.entry = cls.brief.entry_pose
        cls.skin = Skin(cls.rig)

    def test_fk_matches_usd(self):
        for name in self.brief['references']:
            rig = Rig(self.brief.file(self.brief['references'][name]))
            first, last = rig.animated_range()
            ticks = range(first, last + 1)
            world = rig.fk(*rig.sample_local(ticks))
            usd = np.array([[np.array(m) for m in rig.query.ComputeJointSkelTransforms(Usd.TimeCode(t), False)]
                            for t in ticks]) @ rig.skel_to_world
            self.assertLess(np.abs(world - usd).max(), 1e-5, name)

    def test_bind_locals_reproduce_bind(self):
        rig = self.rig
        self.assertLess(np.abs(rig.fk(rig.bind_t, rig.bind_q, rig.bind_s) - rig.bind_world).max(), 1e-5)

    def test_set_hinge_is_exact_and_anatomical(self):
        pose = self.entry.copy()
        for name, degrees in [('LeftKnee', 90), ('RightElbow', 60), ('LeftIndexMiddle', 80), ('RightLittleKnuckle', 70)]:
            pose.set_hinge(name, degrees)
            flexion, alignment = self.rig.hinge_state(pose.world(), name)
            self.assertAlmostEqual(flexion, degrees, places=3)
            self.assertGreater(alignment, 0.999)

    def test_two_bone_reaches_with_anatomical_bend(self):
        pose = self.entry.copy()
        hand_rotation = rotation_part(pose.world()[self.rig.index('RightHand')])
        miss = two_bone(pose, 'Right', 'Arm', [0.12, 0.05, 0.75], pole=[1, -0.3, 0])
        self.assertLess(miss, 1e-5)
        self.assertGreater(self.rig.hinge_state(pose.world(), 'RightElbow')[1], 0.999)
        self.assertLess(np.abs(rotation_part(pose.world()[self.rig.index('RightHand')]) - hand_rotation).max(), 1e-9)
        leg = self.entry.copy()
        self.assertLess(two_bone(leg, 'Left', 'Leg', [-0.1, -0.15, -0.25], pole=[0, 1, 0]), 1e-5)
        self.assertGreater(self.rig.hinge_state(leg.world(), 'LeftKnee')[1], 0.999)
        far = self.entry.copy()
        self.assertGreater(two_bone(far, 'Left', 'Arm', [-0.12, 0.05, 0.75]), 0.05)  # out of reach: reported

    def test_solve_meets_position_and_orientation_targets(self):
        best, report = solve(self.entry, ['RightShoulder', 'RightArm', 'RightForeArm', 'RightHand'],
                             [{'pos': 'RightHand', 'at': [0.05, 0.25, 0.0], 'weight': 5},
                              {'palm': 'Right', 'along': [0, 0, -1]},
                              {'dir': ['RightHand', 'RightHandMiddle1'], 'along': [0, 1, 0]}])
        self.assertLess(report[0]['errorMetres'], 0.002)
        self.assertLess(report[1]['errorDegrees'], 1)
        self.assertGreater(self.rig.hinge_state(best.world(), 'RightElbow')[1], 0.99)

    def test_fist_curls_toward_palm(self):
        pose = self.entry.copy()
        fist(pose, 'Left')
        fist(pose, 'Right')
        for name in self.rig.hinges:
            if 'Knee' in name or 'Elbow' in name:
                continue
            flexion, alignment = self.rig.hinge_state(pose.world(), name)
            self.assertGreater(flexion, 60, name)
            self.assertGreater(alignment, 0.99, name)

    def test_cycle_keys_loop_seamlessly(self):
        a = self.entry.copy()
        two_bone(a, 'Right', 'Arm', [0.12, 0.05, 0.75])
        b = self.entry.copy()
        b.rotate('Hips', [1, 0, 0], 25).move_hips([0, -0.05, 0.03])
        two_bone(b, 'Left', 'Arm', [-0.3, 0.2, 0.5])
        keys = {0: self.entry, 12: a, 20: b, 32: a, 41: self.entry, 50: b}
        spec = {'loop_from_tick': 20, 'clearance': False, 'face': False}
        looped = Clip.from_keys(keys, 51, cycle=(20, 50))
        for tick, pose in keys.items():
            self.assertLess(np.abs(looped.pose(tick).world() - pose.world()).max(), 1e-9, tick)
        report = check_clip(looped, spec)
        self.assertFalse([f for f in report['failures'] if f['check'] == 'loop'], report['failures'])
        self.assertLess(report['metrics']['loopSeamMetres'], 1e-9)
        # Without the cycle the motion stops at the last key and starts again from rest: a jerk at the seam.
        plain = check_clip(Clip.from_keys(keys, 51), spec)
        self.assertTrue([f for f in plain['failures'] if f['check'] == 'loop'])
        # The build-up arrives at the loop start with the loop's own velocity: no acceleration spike there.
        hand = looped.positions('RightHand')
        accel = np.linalg.norm(hand[2:] - 2 * hand[1:-1] + hand[:-2], axis=1)  # row i is tick i + 1
        self.assertLessEqual(accel[19], accel[11:18].max())
        with self.assertRaises(ValueError):
            Clip.from_keys({0: self.entry, 20: a, 50: b}, 51, cycle=(20, 50))
        with self.assertRaises(ValueError):
            Clip.from_keys({**keys, 55: a}, 56, cycle=(20, 50))

    def test_overlap_trails_a_joint_and_keeps_the_loop_closed(self):
        a = self.entry.copy()
        a.rotate('LeftArm', [0, 1, 0], 40).rotate('LeftForeArm', [0, 1, 0], 30)
        looped = Clip.from_keys({0: self.entry, 10: a, 20: self.entry}, 21, cycle=(0, 20))
        trailing = looped.overlap({'LeftForeArm': 2, 'LeftHand': 2.5}, cycle=(0, 20))
        fore, arm = self.rig.index('LeftForeArm'), self.rig.index('LeftArm')
        for tick in range(21):
            source = (tick - 2) % 20
            self.assertLess(np.abs(np.abs(np.sum(trailing.q[tick, fore] * looped.q[source, fore])) - 1), 1e-9, tick)
        self.assertTrue(np.allclose(trailing.q[:, arm], looped.q[:, arm]))  # unlisted joints keep their motion
        self.assertTrue(np.allclose(trailing.world()[20], trailing.world()[0], atol=1e-9))  # the seam stays closed
        held = looped.overlap({'LeftForeArm': 3})  # not a loop: the first tick holds while the motion starts
        self.assertTrue(np.allclose(held.q[:4, fore], looped.q[0, fore]))

    def test_life_warns_about_a_robotic_run(self):
        names = set(self.rig.names)
        needed = {'Spine', 'Spine2', 'LeftShoulder', 'LeftArm', 'LeftUpLeg', 'LeftFoot', 'LeftToeBase', 'Head'}
        if not needed <= names:
            self.skipTest('rig lacks the Mixamo torso and leg joints')
        keys = {}
        for tick, sign in ((0, 1), (11, -1), (22, 1), (33, -1), (44, 1)):
            p = self.entry.copy()
            p.rotate('LeftUpLeg', [1, 0, 0], 35 * sign).rotate('RightUpLeg', [1, 0, 0], -35 * sign)
            p.rotate('LeftArm', [1, 0, 0], -40 * sign).rotate('RightArm', [1, 0, 0], 40 * sign)
            p.move_hips([0, 0, 0.04 if tick % 22 else -0.04])
            keys[tick] = p
        robotic = check_clip(Clip.from_keys(keys, 45, cycle=(0, 44)), {'loop_from_tick': 0, 'clearance': False, 'face': False})
        found = ' '.join(w['message'] for w in robotic['warnings'] if w['check'] == 'life')
        for symptom in ('rigid torso', 'rigid bob', 'frozen collarbone'):
            self.assertIn(symptom, found)
        calm = check_clip(Clip.from_keys({0: self.entry, 30: self.entry}, 31), {'clearance': False, 'face': False})
        self.assertFalse([w for w in calm['warnings'] if w['check'] == 'life'])  # a still pose is not robotic

    def test_form_fails_a_folded_wrist_and_warns_about_mirrored_legs(self):
        folded = self.entry.copy()
        folded.rotate('LeftHand', [0, 0, 1], 125, frame='local')
        report = check_clip(Clip.from_keys({0: self.entry, 10: folded, 20: folded}, 21), {'clearance': False, 'face': False})
        self.assertTrue([f for f in report['failures'] if f['check'] == 'wrist'], report['failures'])
        exempt = check_clip(Clip.from_keys({0: self.entry, 10: folded, 20: folded}, 21),
                            {'clearance': False, 'face': False, 'wrist_exempt_ticks': (0, 20)})
        self.assertFalse([f for f in exempt['failures'] if f['check'] == 'wrist'])
        calm = check_clip(Clip.from_keys({0: self.entry, 20: self.entry}, 21), {'clearance': False, 'face': False})
        self.assertFalse([f for f in calm['failures'] if f['check'] == 'wrist'])
        if not {'LeftUpLeg', 'RightUpLeg', 'LeftLeg', 'RightLeg', 'LeftFoot', 'RightFoot'} <= set(self.rig.names):
            self.skipTest('rig lacks leg joints')
        keys = {}  # one stride per loop: the right leg replays the left leg half a cycle later
        for tick, phase in ((0, 0.0), (11, 0.25), (22, 0.5), (33, 0.75), (44, 1.0)):
            p = self.entry.copy()
            left, right = np.cos(2 * np.pi * phase), np.cos(2 * np.pi * (phase + 0.5))
            p.rotate('LeftUpLeg', [1, 0, 0], 55 * left).rotate('RightUpLeg', [1, 0, 0], 55 * right)
            p.rotate('LeftLeg', [1, 0, 0], 30 + 30 * left).rotate('RightLeg', [1, 0, 0], 30 + 30 * right)
            keys[tick] = p
        mirrored = check_clip(Clip.from_keys(keys, 45, cycle=(0, 44)), {'loop_from_tick': 0, 'clearance': False, 'face': False})
        found = ' '.join(w['message'] for w in mirrored['warnings'])
        self.assertIn('mirror copies', found)
        self.assertIn('plain sine waves', found)

    def test_flow_finds_a_pose_to_pose_body(self):
        from genra.curves import flow, speed_map
        out, back = self.entry.copy(), self.entry.copy()
        for pose, sign in ((out, 1), (back, -1)):  # most of the body turns, so most of it can stop together
            for joint, axis, degrees in (('Hips', [0, 0, 1], 15), ('Spine', [0, 0, 1], 15), ('Spine1', [1, 0, 0], 8),
                                         ('Spine2', [0, 0, 1], 10), ('Neck', [1, 0, 0], 8), ('Head', [1, 0, 0], 10),
                                         ('LeftShoulder', [0, 1, 0], 8), ('LeftArm', [1, 0, 0], -40),
                                         ('LeftForeArm', [1, 0, 0], -30), ('RightShoulder', [0, 1, 0], -8),
                                         ('RightArm', [1, 0, 0], -40), ('RightForeArm', [1, 0, 0], -30),
                                         ('LeftUpLeg', [1, 0, 0], 15), ('RightUpLeg', [1, 0, 0], -15)):
                pose.rotate(joint, axis, degrees * sign)
        # every joint turns back on the same keys, and ticks 20-30 hold one pose
        stiff = Clip.from_keys({0: self.entry, 10: out, 20: back, 30: back, 40: out, 50: self.entry}, 51)
        measured = flow(stiff, (3, 44))
        self.assertGreater(measured['most of the body stopping together (share)'], 0.35)
        self.assertGreater(measured['frozen joint-ticks (share)'], 0.14)
        loose = stiff.overlap({'Spine1': 1, 'Spine2': 2, 'Neck': 3, 'Head': 4, 'LeftForeArm': 2, 'LeftHand': 3,
                               'RightForeArm': 2, 'RightHand': 3})
        self.assertLess(flow(loose, (3, 44))['most of the body stopping together (share)'],
                        measured['most of the body stopping together (share)'])
        report = check_clip(stiff, {'clearance': False, 'face': False})
        self.assertTrue([w for w in report['warnings'] if w['check'] == 'flow'], report['warnings'])
        self.assertIn('flow: frozen joint-ticks (share)', report['metrics'])
        with tempfile.TemporaryDirectory() as tmp:
            speed_map(stiff, Path(tmp) / 'speed.png', reference=loose, window=(3, 44))
            self.assertTrue((Path(tmp) / 'speed.png').exists())

    def test_approved_keys_let_the_body_arrive_two_ticks_late(self):
        half, full = self.entry.copy(), self.entry.copy()
        half.rotate('LeftArm', [1, 0, 0], -45)
        full.rotate('LeftArm', [1, 0, 0], -90)
        clip = Clip.from_keys({0: self.entry, 10: half, 20: full}, 21)  # tick 10 is mid-swing, where lag shows
        late = clip.overlap({'LeftArm': 2})  # the arm reaches its key pose two ticks after the key
        spec = {'clearance': False, 'face': False, 'keys': {'clip': clip, 'ticks': [10]}}
        failed = lambda ticks: [f for f in check_clip(late, dict(spec, keys_ticks=ticks))['failures'] if f['check'] == 'keys']
        self.assertFalse(failed(2))
        self.assertTrue(failed(0))

    def test_carry_holds_a_two_handed_weapon_through_a_body_turn(self):
        pack = REPO / 'artifacts/genra-rigpacks/bonk-unity-sir'
        if not (pack / 'grip.json').exists():
            self.skipTest('needs the Sir Bonk rig pack (a weapon and grip.json)')
        from genra.clip import CharFrame
        from genra.rig import Rig
        from genra.solve import carry, grip_report, two_hand_grip
        rig = Rig(pack / 'rig.usda')
        idle = Clip.from_reference(Rig(pack / 'references' / 'idle.usda'))
        start = Clip(rig, idle.t, idle.q, idle.s).pose(0)
        start.frame = CharFrame.from_pose(start)
        first, _ = two_hand_grip(start.copy(), blade=[1.0, 0.0, 0.1], hilt=[0.35, 0.35, 0.35])
        turned = first.copy()
        turned.rotate('Spine', [0, 0, 1], -35).rotate('Spine1', [0, 0, 1], -25)  # the body turns the held weapon
        second, _ = two_hand_grip(turned, blade=[0.3, 1.0, 0.1], hilt=[0.1, 0.45, 0.35], seed=first)
        clip = Clip.from_keys({0: first, 8: second}, 9)
        report = carry(clip, [0, 8])
        self.assertLess(max(grip_report(clip.pose(t))['leftHandMissMetres'] for t in range(9)), 0.02)
        self.assertIn(8, report['landing (deg)'])
        spec = {'clearance': False, 'face': False, 'two_hands': [0, 8], 'fast_windows': [[0, 8]], 'form': False}
        self.assertFalse([f for f in check_clip(clip, spec)['failures'] if f['check'] in ('flip', 'two_hands', 'grip')])

    def test_a_grip_hand_gap_brings_the_hands_together(self):
        pack = REPO / 'artifacts/genra-rigpacks/bonk-unity-sir'
        if not (pack / 'grip.json').exists():
            self.skipTest('needs the Sir Bonk rig pack (a weapon and grip.json)')
        from genra.clip import CharFrame
        from genra.rig import Rig, handle_offsets, local_matrices
        from genra.solve import two_hand_grip
        from genra.unity_rigpack import learn_grip
        source = json.loads((pack / 'grip.json').read_text())['source']
        frame = (source['clip'], source['tick'])
        close = learn_grip(pack, [], frame=frame, hand_gap=0.12)
        self.assertEqual(close['handGapMetres'], 0.12)
        rig = Rig(pack / 'rig.usda')
        rig.__dict__['grip'] = dict(close, matrix=local_matrices(np.array(close['t']), np.array(close['q']), np.ones(3)))
        idle = Clip.from_reference(Rig(pack / 'references' / 'idle.usda'))
        start = Clip(rig, idle.t, idle.q, idle.s).pose(0)
        start.frame = CharFrame.from_pose(start)
        held, _ = two_hand_grip(start, blade=[1.0, 0.0, 0.1], hilt=[0.35, 0.35, 0.35])
        world = held.world()[None]
        left, right = handle_offsets(rig, world, 'Left')[1][0], handle_offsets(rig, world, 'Right')[1][0]
        self.assertAlmostEqual(float(left - right), 0.12, delta=0.01)

    def test_reviews_render_the_video_beats_camera(self):
        from genra.review import VIEWS, evidence_views
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / 'brief.json'
            for view, expected in ((None, VIEWS), ('profile', VIEWS),
                                   ('front-three-quarter-right', VIEWS + ('front-three-quarter-right',))):
                review = {} if view is None else {'beats': {'file': 'beats.json', 'view': view, 'ticks': {}}}
                path.write_text(json.dumps(dict(self.brief.data, rigpack=str(self.brief.rigpack), review=review)))
                self.assertEqual(evidence_views(Brief(path)), expected)

    def test_wrist_exemption_takes_several_ranges(self):
        from genra.brief import exempt_ranges
        self.assertEqual(exempt_ranges([0, 3]), ((0, 3),))
        self.assertEqual(exempt_ranges([[0, 3], [54, 60]]), ((0, 3), (54, 60)))
        self.assertEqual(exempt_ranges(None), ())
        folded = self.entry.copy()
        folded.rotate('LeftHand', [0, 0, 1], 125, frame='local')
        clip = Clip.from_keys({0: folded, 5: self.entry, 15: self.entry, 20: folded}, 21)  # an idle-to-idle move
        spec = {'clearance': False, 'face': False}
        entry_only = check_clip(clip, dict(spec, wrist_exempt_ticks=(0, 4)))
        self.assertTrue([f for f in entry_only['failures'] if f['check'] == 'wrist'])
        both = check_clip(clip, dict(spec, wrist_exempt_ticks=((0, 4), (15, 20))))
        self.assertFalse([f for f in both['failures'] if f['check'] == 'wrist'])

    def test_curves_measure_named_channels(self):
        from genra.curves import channels, curves_sheet, harmonic_share, mirror_error
        ch = channels(Clip.from_keys({0: self.entry, 20: self.entry}, 21))
        for name in ('Left knee bend', 'Right elbow bend', 'Hips height (m)'):
            self.assertIn(name, ch)
        wave = np.sin(np.linspace(0, 2 * np.pi, 45))
        self.assertLess(harmonic_share(wave), 1e-6)
        self.assertGreater(harmonic_share(np.sign(wave)), 0.05)
        self.assertLess(mirror_error(wave, np.roll(wave[:-1], 22).tolist() + [0]), 0.2)
        with tempfile.TemporaryDirectory() as tmp:
            curves_sheet(Clip.from_keys({0: self.entry, 20: self.entry}, 21), Path(tmp) / 'c.png')
            self.assertTrue((Path(tmp) / 'c.png').exists())

    def test_an_exit_can_be_another_clips_pose(self):
        data = dict(self.brief.data, exit={'from_tick': 20, 'reference': 'fall', 'tick': 3})
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / 'brief.json'
            path.write_text(json.dumps(dict(data, rigpack=str(self.brief.rigpack))))
            brief = Brief(path)
            fall = brief.reference('fall').pose(3)
            self.assertLess(np.abs(brief.exit_pose.world() - fall.world()).max(), 1e-9)
            self.assertIs(brief.check_spec()['exit'], brief.exit_pose)

    def test_preflight_flags_a_folded_entry_unless_exempt(self):
        from genra.loop import preflight
        folded = self.entry.copy()
        folded.rotate('LeftHand', [0, 0, 1], 125, frame='local')
        with tempfile.TemporaryDirectory() as tmp:
            for exempt, flagged in ((None, True), ({'ticks': [0, 3], 'why': 'the entry clip folds it'}, False)):
                data = dict(self.brief.data, rigpack=str(self.brief.rigpack))
                if exempt:
                    data['wrist_exempt'] = exempt
                path = Path(tmp) / 'brief.json'
                path.write_text(json.dumps(data))
                brief = Brief(path)
                self.assertEqual(preflight(brief), [])  # the approved entry pose passes
                brief.__dict__['entry_pose'] = folded  # an entry clip whose own pose folds a wrist
                self.assertEqual(bool(preflight(brief)), flagged)

    def test_closeups_frame_each_hand_and_sheets_wrap(self):
        from genra.render import closeup_sheet
        clip = Clip.from_keys({0: self.entry, 20: self.entry}, 21)
        sheet = closeup_sheet(clip, ['LeftHand', 'RightHand'], [0, 10], size=100, skin=self.skin)
        self.assertEqual(sheet.size, (200, 600))  # 2 ticks x (2 hands x 3 views)
        wrapped = contact_sheet(clip, list(range(0, 21, 2)), ('front',), size=80, skin=self.skin, cols=4)
        self.assertEqual(wrapped.size, (320, 240))  # 11 ticks wrap onto 3 rows of 4

    def test_segments_and_retiming_keep_the_motion(self):
        raised = self.entry.copy()
        two_bone(raised, 'Right', 'Arm', [0.12, 0.05, 0.75])
        clip = Clip.from_keys({0: self.entry, 10: raised, 20: self.entry}, 21)
        part = clip.segment(10, 20)
        self.assertEqual(len(part), 11)
        self.assertLess(np.abs(part.world()[0] - clip.world()[10]).max(), 1e-9)
        slow = clip.retime([(0, 0), (10, 10), (20, 30)])  # the second half plays at half speed
        self.assertEqual(len(slow), 31)
        for new, old in ((0, 0), (10, 10), (30, 20)):
            self.assertLess(np.abs(slow.world()[new] - clip.world()[old]).max(), 1e-9)
        hand = self.rig.index('RightHand')
        halfway = (clip.world()[15, hand, 3, :3] + clip.world()[16, hand, 3, :3]) / 2
        self.assertLess(np.linalg.norm(slow.world()[21, hand, 3, :3] - halfway), 0.02)  # new 21 = source 15.5
        with self.assertRaises(ValueError):
            clip.retime([(0, 0), (30, 10)])

    def test_a_roll_flip_fails_but_a_smooth_roll_passes(self):
        from genra.checks import twist_speed
        rolled = self.entry.copy()
        two_bone(rolled, 'Left', 'Arm', self.entry.pos('LeftHand') + [0, 0.15, 0.1])  # a bent elbow shows the roll
        bone = rolled.pos('LeftForeArm') - rolled.pos('LeftArm')
        flipped = rolled.copy().rotate('LeftArm', bone, 150)  # same hand line, elbow on the other side
        jump = Clip.from_keys({0: rolled, 10: rolled, 11: flipped, 20: flipped}, 21)
        spec = {'clearance': False, 'face': False, 'fast_windows': [[0, 20]]}
        failures = {f['check']: f for f in check_clip(jump, spec)['failures']}
        self.assertIn('flip', failures)  # inside a fast window too: a flip is never speed
        self.assertEqual(failures['flip']['ticks'], [11, 12, 13])  # the tick itself, and the three-tick windows over it
        self.assertGreater(twist_speed(jump)['LeftArm'][10], 140)
        slowed = Clip.from_keys({0: rolled, 10: rolled, 13: flipped, 20: flipped}, 21, method='slerp')  # a retimed flip
        self.assertLess(twist_speed(slowed)['LeftArm'].max(), 60)
        self.assertIn('flip', {f['check'] for f in check_clip(slowed, spec)['failures']})  # 150 deg in three ticks
        smooth = Clip.from_keys({0: rolled, 5: rolled.copy().rotate('LeftArm', bone, 50),
                                 10: rolled.copy().rotate('LeftArm', bone, 100), 15: flipped, 20: flipped}, 21)
        self.assertNotIn('flip', {f['check'] for f in check_clip(smooth, spec)['failures']})

    def test_phase_check_rechecks_a_squeezed_section(self):
        from genra.unity_clip import phase_check
        raised = self.entry.copy()
        two_bone(raised, 'Right', 'Arm', [0.12, 0.05, 0.75])
        clip = Clip.from_keys({0: self.entry, 4: self.entry, 16: raised, 30: raised}, 31)
        brief = {'fast_windows': [[20, 30]]}  # the authored motion is slow; only the tail may be fast
        calm = phase_check(clip.segment(2, 30), brief, (2, 30))
        self.assertFalse([f for f in calm['failures'] if f.startswith(('snap', 'flip'))], calm['failures'])
        self.assertEqual(calm['fastWindows'], [[18, 28]])  # moved with the cut
        anchors = [(0, 0), (2, 2), (14, 4), (28, 18)]  # the 12-tick raise squeezed into 2
        squeezed = phase_check(clip.segment(2, 30).retime(anchors), brief, (2, 30), anchors)
        self.assertFalse(squeezed['passed'])
        self.assertTrue(any(f.startswith('snap') for f in squeezed['failures']))
        self.assertEqual(squeezed['fastWindows'], [[8, 18]])  # and with the warp

    def test_key_poses_and_the_timing_contract_reach_the_prompts(self):
        from genra.review import clock_text
        from genra import run
        raised = self.entry.copy()
        two_bone(raised, 'Right', 'Arm', [0.12, 0.05, 0.75])
        n = self.brief['clock']['duration_ticks']
        approved = Clip.from_keys({0: self.entry, 10: raised, n: self.entry}, n + 1)
        with tempfile.TemporaryDirectory() as tmp:
            approved.save(Path(tmp) / 'keys.json')
            data = dict(self.brief.data, rigpack=str(RIGPACK), keys=[10],
                        timing={'fixed': ['contact at the clock tick'], 'adjustable': ['recovery length']},
                        approved_keys={'file': 'keys.json', 'ticks': [0, 10, n]})
            path = Path(tmp) / 'brief.json'
            path.write_text(json.dumps(data))
            brief = Brief(path)
            self.assertEqual(list(brief.key_beats), [0, 10, n])
            prompt = run.build_prompt(brief)
            self.assertIn('Timing contract: Fixed by the game: contact at the clock tick. Adjustable', prompt)
            self.assertIn('Timing contract', clock_text(brief))
            authoring_path = Path(tmp) / 'authoring-brief.json'
            authoring_path.write_text(json.dumps({k: v for k, v in data.items() if k != 'approved_keys'}))
            for stage in ('keys', 'full'):
                with (self.subTest(stage=stage),
                      mock.patch.object(run.claude_runner, 'run', return_value=(1, None)) as model,
                      mock.patch.object(run, 'postprocess', return_value={})):
                    args = [str(authoring_path), '--stage', stage, '--out-root', str(Path(tmp) / 'runs'),
                            '--name', f'prompt-{Path(tmp).name}-{stage}']
                    if stage == 'full':
                        args += ['--keys', str(Path(tmp) / 'keys.json')]
                    run.main(args)
                    model.assert_called_once()
                    outgoing, _, scratch = model.call_args.args[:3]
                    self.addCleanup(shutil.rmtree, scratch)
                    self.assertIn('Timing contract: Fixed by the game: contact at the clock tick. Adjustable', outgoing)
                    if stage == 'keys':
                        self.assertIn('key poses only', outgoing)
                        self.assertIn(f'Author only these ticks: 0 (entry), 10, {n} (end).', outgoing)
                    else:
                        self.assertIn(f'key poses at ticks 0, 10, {n} in `keys/result.json`', outgoing)
                        self.assertTrue((scratch / 'keys/result.json').is_file())
            spec = brief.check_spec() | {'clearance': False, 'face': False, 'snap_degrees_per_tick': 90}
            self.assertNotIn('keys', {f['check'] for f in check_clip(approved, spec)['failures']})
            moved = Clip(self.rig, approved.t.copy(), approved.q.copy(), approved.s.copy(), approved.frame)
            for tick in range(7, 14):  # the body never meets the key pose within the two ticks it may be early or late
                moved.set_pose(tick, moved.pose(tick).move_hips([0, 0.05, 0]))
            failures = {f['check']: f for f in check_clip(moved, spec)['failures']}
            self.assertIn('keys', failures)
            self.assertEqual(failures['keys']['ticks'], [10])

    def test_a_two_handed_hold_reaches_the_check_and_the_prompt(self):
        from genra.run import build_prompt
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / 'brief.json'
            for hold, text in (('loose', "carry(..., hold='loose'"), (None, 'must stay on the grip')):
                path.write_text(json.dumps(dict(self.brief.data, rigpack=str(RIGPACK), two_hands=[0, 4])
                                           | ({'two_hands_hold': hold} if hold else {})))
                brief = Brief(path)
                self.assertEqual(brief.check_spec().get('two_hands_hold'), hold)
                self.assertIn(text, build_prompt(brief))
            path.write_text(json.dumps(dict(self.brief.data, rigpack=str(RIGPACK), two_hands=[0, 4], two_hands_hold='tight')))
            with self.assertRaises(ValueError):
                Brief(path).check_spec()

    def test_keys_interpolation_and_clip_file(self):
        raised = self.entry.copy()
        two_bone(raised, 'Right', 'Arm', [0.12, 0.05, 0.75])
        keys = {0: self.entry, 12: raised, 30: self.entry}
        for method in ('pchip', 'slerp'):
            clip = Clip.from_keys(keys, 40, method=method)
            for tick, pose in keys.items():
                self.assertLess(np.abs(clip.pose(tick).world() - pose.world()).max(), 1e-9)
        with tempfile.TemporaryDirectory() as tmp:
            clip.save(Path(tmp) / 'c.json')
            back = Clip.load(self.rig, Path(tmp) / 'c.json', frame=self.brief.entry_frame)
            self.assertLess(np.abs(back.world() - clip.world()).max(), 1e-6)

    def test_flips_and_spins_turn_smoothly_past_half_a_turn(self):
        # Keys at 165 and 195 degrees used to spline back through zero: a 113-degree one-tick flip.
        hips = self.rig.index('Hips')
        for axis in ([1, 0, 0], [0, 0, 1]):
            for angles in ([0, 75, 165, 195, 285, 360], [0, 100, 200, 300, 400, 500, 600, 720]):
                keys = {}
                for degrees in angles:
                    pose = self.entry.copy()
                    pose.rotate('Hips', axis, -degrees)
                    keys[round(degrees / 6)] = pose
                world = [rotation_part(m) for m in Clip.from_keys(keys, round(angles[-1] / 6) + 1).world()[:, hips]]
                steps = [np.degrees(np.arccos(np.clip((np.trace(b @ a.T) - 1) / 2, -1, 1))) for a, b in zip(world, world[1:])]
                self.assertLess(max(steps), 8, (axis, angles))
                self.assertAlmostEqual(sum(steps), angles[-1], delta=1, msg=(axis, angles))

    def test_usd_round_trip_keeps_textures(self):
        clip = self.brief.reference('jump')
        with tempfile.TemporaryDirectory() as tmp:
            out = write_usd(clip, Path(tmp) / 'out.usda')
            self.assertTrue((out.parent / 'textures').is_dir())
            back = Clip.from_reference(Rig(out), frame=self.brief.entry_frame)
            self.assertEqual(len(back), len(clip))
            self.assertLess(np.abs(back.world() - clip.world()).max(), 1e-5)

    def test_skinning_matches_usd(self):
        from pxr import UsdSkel, Vt
        rig = Rig(self.brief.file(self.brief['references']['fall']))
        skin = Skin(rig)
        world = rig.fk(*rig.sample_local([10]))[0]
        transforms = rig.query.ComputeSkinningTransforms(Usd.TimeCode(10))
        for mesh, points in zip(skin.meshes, skin.points(world)):
            native = Vt.Vec3fArray(mesh['points'])
            mesh['query'].ComputeSkinnedPoints(transforms, native, Usd.TimeCode(10))
            native = np.c_[np.array(native, float), np.ones(len(native))] @ rig.skel_to_world[:, :3]
            self.assertLess(np.abs(native - points).max(), 1e-5, mesh['name'])

    def test_references_pass_anatomy_checks(self):
        for name in self.brief['references']:
            report = check_clip(self.brief.reference(name), {'clearance': False, 'face': False, 'snap_degrees_per_tick': 90},
                                skin=self.skin)
            self.assertFalse([f for f in report['failures'] if f['check'] == 'hinge'], name)

    @unittest.skipUnless(CODEX.exists(), 'Codex aerial artifacts absent')
    def test_checks_flag_codex_reversed_fingers(self):
        report = check_clip(imported(self.brief, CODEX), dict(self.brief.check_spec(), clearance=False, face=False), skin=self.skin)
        reversed_fingers = {f['joint'] for f in report['failures'] if f['check'] == 'hinge' and f['bend'] == 'reversed'}
        self.assertEqual(len(reversed_fingers), 16)

    @unittest.skipUnless(OPUS_AB.exists(), 'Opus A/B artifacts absent')
    def test_checks_flag_opus_entry_snap_and_fist_gap(self):
        report = check_clip(imported(self.brief, OPUS_AB), dict(self.brief.check_spec(), clearance=False, face=False), skin=self.skin)
        kinds = {f['check'] for f in report['failures']}
        self.assertIn('snap', kinds)
        self.assertIn('fist gap at contact (m)', {f.get('target') for f in report['failures']})
        self.assertNotIn('hinge', kinds)

    def test_snap_exempt_skips_a_spinning_prop(self):
        clip = self.brief.reference('fall')
        from scipy.spatial.transform import Rotation
        head = clip.rig.index('Head')
        clip.q[:, head] = Rotation.from_euler('y', (np.arange(len(clip)) * 50)[:, None], degrees=True).as_quat()
        spec = {'clearance': False, 'face': False, 'snap_degrees_per_tick': 90}
        self.assertIn('Head', {j for f in check_clip(clip, dict(spec, snap_degrees_per_tick=35))['failures']
                               if f['check'] == 'snap' for j in f['joints']})
        report = check_clip(clip, dict(spec, snap_degrees_per_tick=35, snap_exempt=['Head']))
        self.assertNotIn('Head', {j for f in report['failures'] if f['check'] == 'snap' for j in f['joints']})
        self.assertLess(report['metrics']['maxDegreesPerTick'], 50)

    def test_contact_sheet_renders(self):
        sheet = contact_sheet(self.brief.reference('fall'), [0, 20], ('profile', 'three-quarter-right'), size=96, skin=self.skin)
        self.assertEqual(sheet.size, (192, 192))
        self.assertGreater(len(np.unique(np.asarray(sheet).reshape(-1, 3), axis=0)), 50)

    @unittest.skipUnless(shutil.which('ffmpeg'), 'ffmpeg not installed')
    def test_preview_video_has_every_tick_at_two_speeds(self):
        clip = self.brief.reference('fall')
        with tempfile.TemporaryDirectory() as tmp:
            out = preview_video(clip, Path(tmp) / 'p.mp4', ('profile',), size=64, skin=self.skin, contact=3, active=[2, 4], slow=2)
            frames = subprocess.run(['ffprobe', '-v', 'error', '-count_frames', '-select_streams', 'v:0', '-show_entries',
                                     'stream=nb_read_frames', '-of', 'csv=p=0', str(out)], capture_output=True, text=True).stdout
            n = len(clip)  # short clips loop to 2 s at real speed and slow down so their slow pass lasts about 3 s
            self.assertEqual(int(frames), n * max(2, math.ceil(120 / n)) + n * max(2, math.ceil(180 / n)))

    def test_motion_overview_draws_paths(self):
        clip = self.brief.reference('fall')
        sheet = motion_overview(clip, 10, views=('front', 'profile'), size=120, skin=self.skin)
        self.assertEqual(sheet.size, (240, 240))
        pixels = np.asarray(sheet).reshape(-1, 3)
        self.assertTrue((np.abs(pixels - (255, 210, 60)).sum(axis=1) == 0).any())  # the left-hand path colour

    def test_review_view_follows_the_strike(self):
        brief = Brief(BRIEF)
        self.assertEqual(brief.review_views[2], 'three-quarter-right')  # both fists strike
        brief.data['strike'] = ['LeftHand']
        self.assertEqual(brief.review_views[2], 'three-quarter-left')
        punch = self.entry.copy()
        two_bone(punch, 'Right', 'Arm', [0.15, 0.45, 0.35])
        fist(punch, 'Right')
        clip = Clip.from_keys({0: self.entry, 4: punch}, 5)
        seen = strike_visibility(clip, ['RightHand'], 4, ('three-quarter-right', 'three-quarter-left'), skin=self.skin)
        self.assertGreater(seen['three-quarter-right'], 0.7)
        self.assertLess(seen['three-quarter-left'], 0.3)  # the body hides it from the other shoulder


CURRENT = [p for p in ('manki', 'fightguy', 'bonk', 'wibou') if (REPO / f'artifacts/genra-rigpacks/{p}-unity/rig.usda').exists()]


BASHIE_PACK = REPO / 'artifacts/genra-rigpacks/manki-unity-bashie'


@unittest.skipUnless((BASHIE_PACK / 'rig.usda').exists(), 'Bashie candidate pack absent')
class PropRegionTests(unittest.TestCase):
    """Rigid props on joints of their own get their own region, and the clearance check watches them."""

    def test_shell_and_whistle_are_props_checked_against_arms(self):
        from genra.skin import DEFAULT_PAIRS
        rig = Rig(BASHIE_PACK / 'rig.usda')
        skin = Skin(rig)
        self.assertEqual(sorted(skin.props), ['Bashie_Shell', 'Bashie_Whistle'])  # the cord blends Spine2 and Whistle
        self.assertIn('Prop', skin.region_names)
        body = next(m for m in skin.meshes if m['name'] == 'Bashie_Body')
        self.assertNotIn(skin.region_names.index('Prop'), set(body['region']))
        clip = Clip.from_reference(Rig(BASHIE_PACK / 'references/idle.usda'))
        world = Clip(rig, clip.t, clip.q, clip.s).world()[0]
        self.assertIn('LeftForeArm-Prop', skin.clearances(world))
        self.assertIn(('LeftForeArm', 'Prop'), DEFAULT_PAIRS)


@unittest.skipUnless(CURRENT, 'No Unity-built rig packs present')
class CurrentCharacterPackTests(unittest.TestCase):
    """Packs built by unity_rigpack.py from the game's own prefabs and clips."""

    def test_packs_match_unity_and_pass_their_own_clips(self):
        import json
        from genra.clip import CharFrame
        from genra.summary import group_of
        for package in CURRENT:
            with self.subTest(package=package):
                root = REPO / f'artifacts/genra-rigpacks/{package}-unity'
                validation = json.loads((root / 'validation.json').read_text())
                self.assertTrue(validation['passed'])
                self.assertLess(validation['maxSkinErrorMetres'], 1e-4)
                self.assertTrue((root / 'anatomy.json').exists())
                rig = Rig(root / 'rig.usda')
                skin = Skin(rig)
                self.assertIn('LeftElbow', rig.hinges)
                self.assertTrue({group_of(n) for n in rig.names} >= {'left arm', 'right leg', 'spine and head'})
                if 'bladeEnd' in rig.names:  # weapons take the side of the hand holding them
                    self.assertIn(rig.side('bladeEnd'), ('Left', 'Right'))
                for label in ('idle', 'fall'):
                    source = Clip.from_reference(Rig(root / 'references' / f'{label}.usda'))
                    clip = Clip(rig, source.t, source.q, source.s)
                    clip.frame = CharFrame.from_pose(clip.pose(0))
                    report = check_clip(clip, {'clearance': False, 'face': False, 'snap_degrees_per_tick': 180,
                                               'freeze_translations': False, 'root_fixed': False}, skin=skin)
                    self.assertFalse([f for f in report['failures'] if f['check'] == 'hinge'], label)
                sheet = contact_sheet(clip, [0], ('front',), size=64, skin=skin)
                self.assertGreater(len(np.unique(np.asarray(sheet).reshape(-1, 3), axis=0)), 20)
                closed = clip.pose(0)
                fist(closed, 'Left')
                fist(closed, 'Right')  # three-finger, mitten and five-finger hands
                for name, hinge in rig.hinges.items():
                    if hinge[0] == 'finger':  # fists also drop the bind pose's own splay, which Unity cannot play
                        self.assertGreater(rig.hinge_state(closed.world(), name)[1], 0.8, name)
                # Unity plays the fist as authored: every digit stays on its Humanoid muscle axes.
                self.assertTrue(rig.humanoid_digits)
                self.assertLess(max(rig.humanoid_split(j, closed.q[j])[1] for j in rig.humanoid_digits), 1e-3)
                off = check_clip(clip, {'clearance': False, 'face': False, 'snap_degrees_per_tick': 180, 'freeze_translations': False,
                                        'root_fixed': False}, skin=skin)['metrics']['humanoid digit off-axis (deg)']
                self.assertLess(off, 0.01)  # the character's own clips, as the game plays them


    @unittest.skipUnless((REPO / 'artifacts/genra-rigpacks/bonk-unity/grip.json').exists(), 'No two-handed grip measured')
    def test_two_hand_grip_holds_the_measured_grip(self):
        from genra.clip import CharFrame
        from genra.solve import grip_report, hold_grip, two_hand_grip
        root = REPO / 'artifacts/genra-rigpacks/bonk-unity'
        rig = Rig(root / 'rig.usda')
        source = Clip.from_reference(Rig(root / 'references' / 'idle.usda'))
        idle = Clip(rig, source.t, source.q, source.s)
        idle.frame = CharFrame.from_pose(idle.pose(0))
        entry = idle.pose(0)
        self.assertGreater(grip_report(entry)['leftHandMissMetres'], 0.2)  # idle: one hand on the sword
        head = entry.pos('Head')
        ready, report = two_hand_grip(entry.copy(), blade=[0, 0.35, 1], hilt=[0, 0.45, head[2] - 0.55])
        self.assertLess(report['leftHandMissMetres'], 0.002)
        self.assertLess(report['leftHandMissDegrees'], 1)
        self.assertLess(report['bladeErrorDegrees'], 1)
        for side in ('Left', 'Right'):  # elbows stay on their hinges, within range
            flexion, alignment = rig.hinge_state(ready.world(), side + 'Elbow')
            self.assertGreater(alignment, 0.95)  # the idle start carries some twist of its own
            self.assertLess(flexion, 161)
        # Within what Unity's Humanoid plays (the unbounded solver rolled Bonk's collarbones up to 122 degrees past it).
        self.assertLess(max(rig.humanoid_excess(j, ready.q[j])[0] for j in rig.humanoid_limited), 0.5)
        clip = Clip.from_keys({0: entry, 8: ready, 16: ready, 24: entry}, 25)
        spec = {'clearance': False, 'face': False, 'snap_degrees_per_tick': 90, 'two_hands': [8, 16]}
        self.assertNotIn('grip', {f['check'] for f in check_clip(clip, spec)['failures']})
        self.assertNotIn('humanoid', {f['check'] for f in check_clip(clip, spec)['failures']})
        rolled = entry.copy()  # a collarbone turned about its own length: no muscle plays that
        rolled.rotate('RightShoulder', rolled.pos('RightArm') - rolled.pos('RightShoulder'), 40)
        self.assertGreater(rig.humanoid_excess(rig.index('RightShoulder'), rolled.q[rig.index('RightShoulder')])[0], 30)
        rolling = Clip.from_keys({0: entry, 8: rolled, 24: entry}, 25)
        self.assertIn('humanoid', {f['check'] for f in check_clip(rolling, spec)['failures']})
        drifting = Clip.from_keys({0: entry, 8: ready, 24: entry}, 25)
        # The grip is measured after turning it about the handle to the hand (a held hand may sit anywhere around it), so
        # the drift counts from 2 cm off the turned grip: tick 13 here.
        self.assertIn('grip', {f['check'] for f in check_clip(drifting, dict(spec, two_hands=[8, 14]))['failures']})
        held = hold_grip(drifting, 8, 14)
        self.assertLess(held['maxMissMetres'], 0.005)
        self.assertLess(held['maxWristDegrees'], 50)  # the held arm turns instead of folding the wrist
        self.assertNotIn('grip', {f['check'] for f in check_clip(drifting, dict(spec, two_hands=[8, 14]))['failures']})
        self.assertNotIn('flip', {f['check'] for f in check_clip(drifting, spec)['failures']})

    def test_frame_front_is_the_fighters_facing(self):
        """Fighting stances turn the hips 40-90 degrees off the facing; front follows the facing (the game's
        forward and the match camera) unless a brief pins the old hip frame."""
        from genra.brief import Brief
        root = REPO / f'artifacts/genra-rigpacks/{CURRENT[0]}-unity'
        with tempfile.TemporaryDirectory() as tmp:
            frames = {}
            for kind in ('facing', 'hips'):
                path = Path(tmp) / f'{kind}.json'
                path.write_text(json.dumps({'name': kind, 'rigpack': str(root), 'frame': kind, 'references': {'idle': 'references/idle.usda'},
                                            'entry': {'reference': 'idle', 'tick': 0}, 'clock': {'duration_ticks': 10}}))
                frames[kind] = Brief(path).entry_frame
        rig = Rig(root / 'rig.usda')
        facing, hips = frames['facing'], frames['hips']
        np.testing.assert_allclose(facing.front, rig.facing, atol=1e-9)
        for frame in (facing, hips):  # one handedness: front = up x right
            np.testing.assert_allclose(np.cross(frame.up, frame.right), frame.front, atol=1e-9)
        self.assertGreater(np.degrees(np.arccos(np.clip(facing.front @ hips.front, -1, 1))), 20)
        np.testing.assert_allclose(facing.origin, hips.origin)

    def test_hips_can_drop_while_the_fighter_stays(self):
        from genra.clip import CharFrame
        package = CURRENT[0]
        root = REPO / f'artifacts/genra-rigpacks/{package}-unity'
        rig = Rig(root / 'rig.usda')
        skin = Skin(rig)
        source = Clip.from_reference(Rig(root / 'references' / 'idle.usda'))
        idle = Clip(rig, source.t, source.q, source.s)
        idle.frame = CharFrame.from_pose(idle.pose(0))
        entry = idle.pose(0)
        crouch = entry.copy()
        feet = {side: crouch.pos(side + 'Foot') for side in ('Left', 'Right')}
        crouch.move_hips([0, 0.03, -0.12])
        self.assertLess(np.abs(crouch.pos('Hips') - (entry.pos('Hips') + [0, 0.03, -0.12])).max(), 1e-9)
        for side, target in feet.items():
            self.assertLess(two_bone(crouch, side, 'Leg', target), 1e-5)
        clip = Clip.from_keys({0: entry, 10: crouch, 20: entry}, 21)
        hips = clip.positions('Hips')
        self.assertLess(np.abs(hips[10] - crouch.pos('Hips')).max(), 1e-9)
        self.assertLess(np.abs(np.diff(hips[:, 2], 2)).max(), 0.01)  # smooth (pchip), not piecewise linear
        report = check_clip(clip, {'clearance': False, 'face': False, 'snap_degrees_per_tick': 90}, skin=skin)
        self.assertFalse([f for f in report['failures'] if f['check'] in ('translations', 'root')])
        self.assertAlmostEqual(report['metrics']['hipTravelMetres'], float(np.linalg.norm([0, 0.03, 0.12])), places=6)
        moved = clip.pose(5).copy()
        moved.t[rig.index('LeftForeArm')] *= 1.1  # a bone length change still fails
        clip.set_pose(5, moved)
        self.assertIn('translations', {f['check'] for f in check_clip(clip, {'clearance': False, 'face': False}, skin=skin)['failures']})



WEAPON_PACKS = [REPO / f'artifacts/genra-rigpacks/{p}' for p in ('bonk-unity-sir', 'bonk-unity')
                if (REPO / f'artifacts/genra-rigpacks/{p}/grip.json').exists()]


@unittest.skipUnless(WEAPON_PACKS, 'No rig pack with a two-handed grip present')
class ContactTests(unittest.TestCase):
    """Holding and planting a weapon: wrists, pins, grips that turn about the handle (Sir Bonk's E1, 2026-10-21)."""

    @classmethod
    def setUpClass(cls):
        from genra.clip import CharFrame
        cls.root = WEAPON_PACKS[0]
        cls.rig = Rig(cls.root / 'rig.usda')
        source = Clip.from_reference(Rig(cls.root / 'references' / 'idle.usda'))
        idle = Clip(cls.rig, source.t, source.q, source.s)
        idle.frame = CharFrame.from_pose(idle.pose(0))
        cls.entry = idle.pose(0)

    def ready(self, **options):
        from genra.solve import two_hand_grip
        head = self.entry.pos('Head')
        return two_hand_grip(self.entry.copy(), blade=[0, 0.35, 1], hilt=[0, 0.45, head[2] - 0.55], **options)

    def wrists(self, pose):
        from genra.solve import wrist_bend, wrist_joints
        return [wrist_bend(pose.world(), wrist_joints(self.rig, side)) for side in ('Left', 'Right')]

    def test_the_solver_keeps_wrists_in_line_instead_of_folding_them(self):
        folded, _ = self.ready(wrists=None)
        held, report = self.ready()  # wrists=45 by default
        self.assertLess(report['leftHandMissMetres'], 0.005)
        self.assertLess(report['bladeErrorDegrees'], 2)
        self.assertLess(max(self.wrists(held)), 47)
        self.assertGreater(max(self.wrists(folded)), max(self.wrists(held)) + 20)

    def test_pin_keeps_a_stuck_tip_while_the_body_moves_around_it(self):
        from genra.checks import twist_speed
        from genra.solve import grip_report, pin
        ready, _ = self.ready()
        low = ready.copy().move_hips([0, -0.15, -0.2])  # the body sinks back; the arms ride along
        clip = Clip.from_keys({0: ready, 12: low, 24: ready}, 25)
        tip = clip.positions('bladeEnd')
        self.assertGreater(np.linalg.norm(tip - tip[0], axis=1).max(), 0.1)  # unpinned, the tip travels
        report = pin(clip, 0, 24, 'bladeEnd', grip='roll')
        tip = clip.positions('bladeEnd')
        self.assertLess(np.linalg.norm(tip - tip[0], axis=1).max(), 0.005)
        self.assertLess(max(float(r.max()) for r in twist_speed(clip).values()), 30)  # continuous: no flips
        self.assertLess(max(grip_report(clip.pose(t))['leftHandMissMetres'] for t in range(25)), 0.01)
        self.assertLess(max(v for k, v in report.items() if k.startswith('wrist')), 55)

    def test_a_grip_is_measured_after_turning_it_about_the_handle(self):
        from scipy.spatial.transform import Rotation
        from genra.solve import _attach_error, grip_target
        ready, _ = self.ready()
        world, grip = ready.world(), grip_target(self.rig)
        inverse = np.linalg.inv(world[self.rig.index(grip['to'])])  # the weapon joint carries the pack's scale
        hilt, end = (world[self.rig.index(n), 3, :3] for n in ('bladeHilt', 'bladeEnd'))
        centre = (np.append(hilt, 1) @ inverse)[:3]  # the handle line in the weapon's frame
        axis = (np.append(hilt - end, 0) @ inverse)[:3]
        turn = np.eye(4)
        turn[:3, :3] = Rotation.from_rotvec(np.radians(40) * axis / np.linalg.norm(axis)).as_matrix().T
        turn[3, :3] = centre - centre @ turn[:3, :3]
        turned = dict(grip, local=grip['local'] @ turn)  # the same grip, 40 degrees around the handle
        _, metres, degrees, _ = _attach_error(ready, turned)
        self.assertGreater(degrees, 35)  # rigid: the hand is off this grip
        _, metres, degrees, roll = _attach_error(ready, dict(turned, roll=0.0))
        self.assertLess(metres, 1e-3)
        self.assertLess(degrees, 0.5)
        self.assertAlmostEqual(abs(roll), 40, delta=0.5)

    def test_a_loose_hold_keeps_the_fist_on_the_handle_beside_the_weapon_hand(self):
        from genra.solve import ELBOW_DROP, _handle_error, handle_target, hold_report
        target = handle_target(self.rig)
        low, high = target['along']
        ready, _ = self.ready()
        self.assertLess(float(np.abs(_handle_error(ready, target)[0]).max()), 1e-3)  # grip.json's own hold is loose too
        held, report = self.ready(hold='loose', elbows=True)
        self.assertLess(report['leftHandOffHandleMetres'], target['slack'] + 0.002)
        self.assertLess(report['leftHandTiltDegrees'], target['tilt'] + 1)
        self.assertTrue(low - 0.002 <= report['leftHandPastHiltMetres'] <= high + 0.002, report)  # never past the
        self.assertLess(report['bladeErrorDegrees'], 2)                                             # weapon hand
        for side in ('Left', 'Right'):
            self.assertGreater(held.pos(side + 'Arm')[2] - held.pos(side + 'ForeArm')[2], ELBOW_DROP - 0.01)
        self.assertGreater(hold_report(self.entry, 'loose')['leftHandOffHandleMetres'], 0.1)  # the idle's fist on the hip

    def test_overhead_hands_keep_the_elbows_under_the_hands_only(self):
        from genra.solve import elbows_down, two_hand_grip
        self.assertEqual({t['than'][-4:] for t in elbows_down(self.rig, shoulders=False)}, {'Hand'})
        head = self.entry.pos('Head')
        up, report = two_hand_grip(self.entry.copy(), blade=[0, 0.15, 1], hilt=[0.05, 0.3, head[2] + 0.35], hilt_weight=1.0,
                                   spine=False, reg=0.05, hold='loose', elbows='hands')
        self.assertLess(report['bladeErrorDegrees'], 3)
        for side in ('Left', 'Right'):
            self.assertLess(up.pos(side + 'ForeArm')[2], up.pos(side + 'Hand')[2] + 0.01)
        with self.assertRaises(ValueError):
            two_hand_grip(self.entry.copy(), blade=[0, 0, 1], elbows='up')

    def test_a_weapon_carried_between_two_equal_keys_stays_put(self):
        # carry() aimed the hilt-to-hand line at a direction the hand never reaches (77 degrees off on Sir Bonk's
        # plank), which tipped every carried tick ~18 degrees (2026-10-22).
        from genra.solve import carry
        held, _ = self.ready()
        clip = Clip.from_keys({0: held, 8: held}, 9)
        carry(clip, [0, 8])
        self.assertLess(max(np.linalg.norm(clip.pose(t).pos('bladeEnd') - held.pos('bladeEnd')) for t in range(9)), 0.003)

    def test_a_loose_two_handed_carry_lands_on_each_key_both_ways(self):
        from genra.solve import carry, hold_report, two_hand_grip
        chest, keys = self.entry.pos('Spine2'), {}
        for tick, turn, blade, hilt in ((0, -30, [1, 0.2, 0], [0.35, 0.45, 0.0]), (6, 0, [0, 1, 0.05], [0.05, 0.75, 0.0]),
                                        (12, 30, [-1, 0.4, 0], [-0.2, 0.55, 0.0])):  # a flat swing the body turns
            body = self.entry.copy().rotate('Spine', [0, 0, 1], turn)
            keys[tick], _ = two_hand_grip(body, blade=blade, hilt=list(chest + hilt), hilt_weight=1.0, spine=False,
                                          reg=0.05, hold='loose', elbows=True, seed=keys.get(tick - 6))
        landing = {}
        for both in (False, True):
            clip = Clip.from_keys(keys, 13)
            landing[both] = max(carry(clip, [0, 6, 12], hold='loose', elbows=True, both_ways=both)['landing (deg)'].values())
        self.assertLess(landing[True], 20)
        self.assertLess(landing[True], landing[False])  # forward only, the arms reach a key on another solution
        self.assertLess(max(hold_report(clip.pose(t), 'loose')['leftHandOffHandleMetres'] for t in range(13)), 0.035)
        spec = {'clearance': False, 'face': False, 'two_hands': [0, 12], 'form': False}
        grips = lambda hold: [f for f in check_clip(clip, dict(spec, two_hands_hold=hold))['failures'] if f['check'] == 'grip']
        self.assertFalse(grips('loose'))
        self.assertTrue(grips('exact'))  # beside grip.json's place, not on it

    def test_a_re_exported_mesh_is_reported_not_compared(self):
        from genra.unity_clip import mesh_vs_toolkit
        mapping = json.loads((self.root / 'mapping.json').read_text())
        path = next(iter(mapping['meshes'].values()))
        clip = Clip.from_keys({0: self.entry, 1: self.entry}, 2)
        bakes = [{'tick': 0, 'renderers': [{'path': path, 'vertices': [0.0] * 30}]}]  # 10 vertices: not the pack's mesh
        report = mesh_vs_toolkit(self.rig, clip.world(), mapping, bakes, np.zeros(3))
        self.assertEqual(report['meshChanged'][path.rsplit('/', 1)[-1]][1], 10)

    def test_without_a_grip_the_other_hand_must_still_be_on_the_handle(self):
        ready, _ = self.ready()
        bare = Rig(self.root / 'rig.usda')
        bare.__dict__['grip'] = {}  # as a pack without grip.json
        spec = {'clearance': False, 'face': False, 'snap_degrees_per_tick': 180, 'two_hands': [0, 1]}
        for pose, held in ((ready, True), (self.entry, False)):  # the idle holds the weapon in one hand
            clip = Clip(bare, *(np.stack([getattr(pose, k)] * 2) for k in ('t', 'q', 's')), self.entry.frame)
            grips = [f for f in check_clip(clip, spec)['failures'] if f['check'] == 'grip']
            self.assertEqual(not grips, held, grips)

    @unittest.skipUnless(len(WEAPON_PACKS) == 2, 'Needs both Bonk packs')
    def test_grip_learning_scales_with_the_weapon_hand(self):
        from genra.unity_rigpack import learn_grip
        for root in WEAPON_PACKS:
            labels = [label for label, ref in json.loads((root / 'mapping.json').read_text())['references'].items()
                      if 'semanticId' in ref]
            learned, saved = learn_grip(root, labels), json.loads((root / 'grip.json').read_text())
            self.assertEqual(learned['source'], saved['source'], root.name)
            self.assertLess(learned['offAxisMetres'], learned['weaponHandOffAxisMetres'] + 0.03)
        # A thick handle: Sir Bonk's right hand sits 3.2 cm off the plank's axis, past the old fixed 3 cm.
        self.assertGreater(json.loads((WEAPON_PACKS[0] / 'grip.json').read_text())['weaponHandOffAxisMetres'], 0.03)


class HitArcTests(unittest.TestCase):
    """Weapon arcs against the game's hurtbox spheres (hitarc.py), no rig pack needed."""

    def test_a_flat_swing_hits_what_it_passes_and_misses_below(self):
        from genra.hitarc import coverage, intervals, report_spheres
        with tempfile.TemporaryDirectory() as tmp:
            report = Path(tmp) / 'r.json'  # one 0.2 m head sphere 1.2 m up (game axes: x right, y up, z ahead)
            report.write_text(json.dumps({'targets': {'T': [[0, 1.2, 0, 0, 1.2, 0, 0.2]]}}))
            head = report_spheres(report, 'T')
        self.assertEqual(head.tolist(), [[0, 0, 1.2, 0.2]])
        plank = np.array([[[0.0, 0.3, 1.5], [0.0, 2.0, 1.5]]])  # ahead of the attacker, 1.5 m up, r 0.2
        distances, heights = np.round(np.arange(-1, 3.01, 0.25), 2), np.array([0.0, 1.0])
        grid = coverage(plank, 0.2, head, distances, heights)
        self.assertEqual(intervals(grid[0], distances, 0.25), [(0.25, 2.25)])  # standing: the head passes under it (the round ends reach 26 cm past it)
        self.assertFalse(grid[1].any())  # feet 1 m up: the head is 2.2 m up, far above the plank

    def test_the_calibration_lifts_sir_bonks_plank_into_the_games_frame(self):
        from genra.hitarc import CALIBRATION, weapon_segments

        class Pose:
            def pos(self, joint):
                return np.array([0.0, 1.0, 0.4 if joint == 'bladeHilt' else 1.0])

        class Clip:
            def pose(self, tick):
                return Pose()

        seg = weapon_segments(Clip(), [0], floor=-1.0, calibration=CALIBRATION['bonk'])
        self.assertAlmostEqual(float(seg[0, 1, 2]), 1.262 * 2.0 - 0.124, places=5)  # up from the floor, then lifted
        self.assertAlmostEqual(float(seg[0, 1, 1]), 1.024 * 1.0 + 0.156, places=5)


class UnityJobTests(unittest.TestCase):
    """How results and timelines become Unity jobs; no Unity or rig pack needed."""

    def test_free_entries_keep_their_origin_and_only_whole_loops_cycle(self):
        from genra.brief import Brief
        from genra.unity_clip import write_flags
        cases = {  # entry, loop -> flags
            'attack': ({'reference': 'idle', 'tick': 0}, None, {'alignOrigin': True, 'loopTime': False}),
            'aim hold': ({'reference': 'idle', 'tick': 0, 'match': False}, {'from_tick': 0}, {'alignOrigin': False, 'loopTime': True}),
            'tumble': ({'reference': 'fall', 'tick': 0, 'match': False}, {'from_tick': 24}, {'alignOrigin': False, 'loopTime': False}),
        }
        with tempfile.TemporaryDirectory() as tmp:
            for name, (entry, loop, flags) in cases.items():
                data = {'name': name, 'rigpack': '.', 'entry': entry, 'clock': {'duration_ticks': 30}}
                if loop:
                    data['loop'] = loop
                path = Path(tmp) / f'{name}.json'
                path.write_text(json.dumps(data))
                self.assertEqual(write_flags(Brief(path)), flags, name)

    def test_timeline_events_name_assets_or_bound_clips(self):
        from genra.unity_clip import timeline_rows
        spec = {'ticks': 10, 'rows': [{'label': 'B', 'title': 'B: beckon', 'moves': [[3, 5, 0.2]], 'events': [
            {'tick': 0, 'clip': 'anim.wibou.idle'}, {'tick': 2, 'clip': 'Assets/X/loop.anim', 'fade': 0.1}]}]}
        self.assertEqual(timeline_rows(spec), [{'label': 'B', 'moves': [[3, 5, 0.2]], 'events': [
            {'tick': 0, 'semanticId': 'anim.wibou.idle'}, {'tick': 2, 'fade': 0.1, 'asset': 'Assets/X/loop.anim'}]}])
        spec['rows'][0]['props_shown'] = {'Weapon1': [[5, 17]]}
        self.assertEqual(timeline_rows(spec)[0]['props_shown'], {'Weapon1': [[5, 17]]})

    def test_props_shown_names_pack_weapons_by_joint(self):
        # Sir Bonk's pot lid rides his shoulder (Weapon2) until the grab and his hand (Weapon1) until the release; the
        # plank (Weapon0) has no window and always shows.
        from genra.unity_clip import weapon_attachments
        weapon = {'bone': 'b', 'prefab': 'p', 'positionOffset': [0, 0, 0], 'rotationOffset': [0, 0, 0], 'slot': 0}
        jobs = weapon_attachments({'weapons': [weapon] * 3}, {'Weapon1': [[4, 14]], 'Weapon2': [[0, 3]]})
        self.assertEqual([j['name'] for j in jobs], ['Weapon0', 'Weapon1', 'Weapon2'])
        self.assertEqual([j.get('shown') for j in jobs], [None, [[4, 14]], [[0, 3]]])
        self.assertNotIn('slot', jobs[0])

    def test_revision_prompt_carries_the_directors_notes(self):
        from genra.run import revision_notes
        with tempfile.TemporaryDirectory() as tmp:
            d = Path(tmp)
            images = [d / n for n in ('unity-gameplay-1.png', 'unity-gameplay-2.png', 'curves.png', 'hands.png')]
            for image in images:
                image.write_bytes(b'png')
            (d / 'evidence.json').write_text(json.dumps({'images': [str(i) for i in images], 'captions': ['g1', 'g2', 'c', 'h']}))
            (d / 'checks.txt').write_text('FAIL (1)\n  x wrist: folds\n')
            (d / 'review.json').write_text(json.dumps({'review': {
                'verdict': 'revise', 'summary': 'Close.', 'keep': ['the lean'], 'issues': [{
                    'severity': 'major', 'problem': 'Folded wrist.', 'ticks': '0-44', 'joints': ['LeftHand'],
                    'evidence': 'hands t11', 'fix': 'Straighten it.', 'target': 'under 45 degrees'}]}}))
            text, attached = revision_notes(d / 'review.json', 2)
        for part in ('## Revision 2', '1. (major) Folded wrist.', 'Target: under 45 degrees', 'x wrist: folds', 'Keep: the lean'):
            self.assertIn(part, text)
        self.assertEqual([p.name for p in attached], ['unity-gameplay-1.png', 'hands.png'])

    def test_the_owners_direction_leads_a_revision_and_sets_issues_aside(self):
        from genra.run import revision_notes
        with tempfile.TemporaryDirectory() as tmp:
            d = Path(tmp)
            (d / 'evidence.json').write_text(json.dumps({'images': [], 'captions': []}))
            (d / 'checks.txt').write_text('PASS\n')
            issue = {'severity': 'minor', 'ticks': '13-16', 'joints': ['RightArm'], 'evidence': 'e', 'fix': 'f', 'target': 't'}
            (d / 'review.json').write_text(json.dumps({'review': {
                'verdict': 'revise', 'summary': 'Pose-to-pose.', 'keep': [],
                'issues': [dict(issue, severity='major', problem='Stops everywhere.'), dict(issue, problem='Re-pose the load.'),
                           dict(issue, problem='Head bob.')]}}))
            text, _ = revision_notes(d / 'review.json', 1, owner='Smooth it; keep the poses.', skip=[2])
        self.assertIn("The owner's direction for this revision, which comes before the director's: Smooth it", text)
        self.assertIn('set this review issue aside; do not pursue it: Re-pose the load.', text)
        self.assertIn('1. (major) Stops everywhere.', text)
        self.assertIn('2. (minor) Head bob.', text)  # renumbered
        self.assertNotIn('. (minor) Re-pose the load.', text)

    def test_the_loop_stops_on_acceptance_or_polishes_on(self):
        from genra.loop import finished, form_metrics
        report = 'PASS\n  knee bend min (deg): 21.906\n  legs mirror error (deg): 7.213\n  hips drop into the landings (m): 0.094\n'
        self.assertEqual(form_metrics(report), {'knee bend min (deg)': 21.906, 'legs mirror error (deg)': 7.213})
        accepted = {'verdict': 'accept', 'issues': [{'severity': 'minor'}]}
        self.assertTrue(finished(accepted, True, polish=False))
        self.assertFalse(finished(accepted, True, polish=True))
        self.assertTrue(finished({'verdict': 'accept', 'issues': []}, True, polish=True))
        self.assertFalse(finished({'verdict': 'accept', 'issues': []}, False, polish=False))  # a failing check keeps going
        self.assertFalse(finished({'verdict': 'revise', 'issues': []}, True, polish=False))

    def test_review_filmstrips_crop_to_the_character(self):
        from PIL import Image
        from genra.render import BACKGROUND
        from genra.review import strip
        with tempfile.TemporaryDirectory() as tmp:
            frames = Path(tmp)
            (frames / 'front').mkdir()
            for tick, x in ((0, 100), (2, 140)):
                image = Image.new('RGB', (400, 400), BACKGROUND)
                image.paste((200, 60, 60), (x, 150, x + 60, 270))
                image.save(frames / 'front' / f'{tick:04d}.png')
            sheet = strip(frames, 'front', [0, 2], 'test', cols=2, width=400)
        cell = np.asarray(sheet.crop((0, 34, 200, sheet.height)), int)
        self.assertEqual(sheet.width, 400)
        self.assertGreater((np.abs(cell - BACKGROUND).max(axis=2) > 16).mean(), 0.3)  # the body fills the cell

    def test_candidate_packs_are_labelled_and_play_on_their_own_prefab(self):
        from genra.unity_clip import job_for, pack_prefab
        from genra.unity_rigpack import CHARACTERS, pack_dir, variant
        self.assertEqual(variant('bonk', None), 'bonk')
        self.assertEqual(variant('bonk', None, 'sir'), 'bonk+sir')
        self.assertEqual(variant('wibou', CHARACTERS['wibou'] | {'dash': 'anim.wibou.r'}), 'wibou+dash')
        self.assertEqual(variant('bonk', CHARACTERS['bonk'] | {'oldrun': 'Assets/A/run.FBX|Run_ver_B'}, 'sir'), 'bonk+sir')
        self.assertEqual(pack_dir('bonk', label='sir').name, 'bonk-unity-sir')
        self.assertEqual(pack_dir('bonk').name, 'bonk-unity')
        candidate = {'package': 'bonk', 'prefab': 'Assets/CharacterPackages/bonk/Presentation/SirBonkRig.prefab'}
        self.assertEqual(job_for('bonk', 'idle', [], np.zeros((1, 0, 10)), prefab=pack_prefab(candidate))['prefab'],
                         candidate['prefab'])
        self.assertIsNone(pack_prefab({'package': 'bonk'}))

    def test_a_clip_saved_without_a_prop_loads_on_a_pack_with_it(self):
        import json
        import tempfile
        root = REPO / 'artifacts/genra-rigpacks/bonk-unity'
        if not (root / 'rig.usda').exists():
            self.skipTest('No Bonk Unity pack')
        rig = Rig(root / 'rig.usda')
        source = Clip.from_reference(Rig(root / 'references' / 'swing.usda'))
        clip = Clip(rig, source.t, source.q, source.s)
        with tempfile.TemporaryDirectory() as tmp:
            data = json.loads(clip.save(Path(tmp) / 'full.json').read_text())
            sword = set(rig.descendants('Weapon0'))
            keep = [j for j in range(len(rig.names)) if j not in sword]
            data['joints'] = [rig.names[j] for j in keep]  # as saved before the sword joined the pack
            for k in ('t', 'q', 's'):
                data[k] = np.asarray(data[k])[:, keep].tolist()
            (Path(tmp) / 'old.json').write_text(json.dumps(data))
            loaded = Clip.load(rig, Path(tmp) / 'old.json')
            data['joints'][0] = 'Tail9'  # a joint the rig lacks: another rig
            (Path(tmp) / 'other.json').write_text(json.dumps(data))
            with self.assertRaises(ValueError):
                Clip.load(rig, Path(tmp) / 'other.json')
        tip = rig.index('bladeEnd')
        self.assertLess(np.abs(loaded.world()[:, tip, 3, :3] - clip.world()[:, tip, 3, :3]).max(), 1e-4)

    def test_a_held_prop_gets_its_narrow_end_as_the_tip(self):
        from genra.unity_rigpack import prop_ends
        angle = np.linspace(0, 2 * np.pi, 24, endpoint=False)
        body = np.concatenate([np.c_[0.12 * np.cos(angle), 0.12 * np.sin(angle), np.full(24, z)] for z in (-0.26, 0.2)])
        nozzle = np.c_[0.02 * np.cos(angle), 0.02 * np.sin(angle) + 0.03, np.full(24, 0.26)]  # a can, nozzle off-centre
        tip, base = prop_ends([body, nozzle])
        self.assertTrue(np.allclose(tip, [0, 0.03, 0.26], atol=1e-9))
        self.assertTrue(np.allclose(base, [0, 0, -0.26], atol=1e-9))

    def test_a_prop_pack_attaches_only_the_named_slot_prop(self):
        from genra.unity_rigpack import SLOTS, split_props
        sword, can, bazooka = {'slot': 0}, {'slot': SLOTS['F']}, {'slot': SLOTS['R']}
        self.assertEqual(split_props([sword, can, bazooka]), ([sword], [can, bazooka]))
        self.assertEqual(split_props([sword, can, bazooka], (SLOTS['F'],)), ([sword, can], [bazooka]))


JOUST_GALLOP = REPO / 'artifacts/genra-runs/20261021-202827-bonk-sir-r-gallop-loop202827-r0'


class FootContactTests(unittest.TestCase):
    """genra.contacts: landing ticks from foot heights, the up column of char-frame positions."""

    class Stub:
        def __init__(self, heights):
            self.heights = heights
            self.rig = type('StubRig', (), {'names': list(heights)})()

        def __len__(self):
            return len(next(iter(self.heights.values())))

        def positions(self, joint):
            height = np.asarray(self.heights[joint], float)
            return np.stack([np.zeros_like(height), np.full_like(height, -5.0), height], axis=1)  # front must not count

    def test_a_foot_lands_on_its_first_down_tick_and_runs_wrap_round_a_loop(self):
        from genra.contacts import foot_contacts
        # a 6-tick loop whose last tick repeats tick 0: the left foot is down on 4, 5 and 0, the right foot on 2
        left = [0.0, 0.3, 0.3, 0.3, 0.01, 0.0, 0.0]
        right = [0.3, 0.3, 0.0, 0.3, 0.3, 0.3, 0.3]
        looped = foot_contacts(self.Stub({'LeftFoot': left, 'RightFoot': right}), loop_from=0)
        self.assertEqual(looped['feet']['LeftFoot'], {'landings': [4], 'down': [[4, 0]]})
        self.assertEqual(looped['landings'], [[2, 'RightFoot'], [4, 'LeftFoot']])
        once = foot_contacts(self.Stub({'LeftFoot': left[:-1]}))
        self.assertEqual(once['feet']['LeftFoot']['down'], [[0, 0], [4, 5]])

    @unittest.skipUnless((JOUST_GALLOP / 'scratch/result.json').exists()
                         and (REPO / 'artifacts/genra-rigpacks/bonk-unity-sirjoust/rig.usda').exists(),
                         'Sir Bonk Joust gallop run or rig pack absent')
    def test_the_joust_gallop_lands_where_the_game_plays_its_hooves(self):
        from genra.contacts import foot_contacts
        # Measured in Unity on the promoted bonk_sir_r_gallop (2026-10-22): left heel down on 1, right on 13, a tick
        # after the brief's "contact 0" beats; LoopFootfalls' knocks are keyed to these ticks.
        brief = Brief(JOUST_GALLOP / 'brief.json')
        clip = Clip.load(brief.rig, JOUST_GALLOP / 'scratch/result.json', frame=brief.entry_frame)
        self.assertEqual(foot_contacts(clip, loop_from=brief.loop_from)['landings'], [[1, 'LeftFoot'], [13, 'RightFoot']])


if __name__ == '__main__':
    unittest.main()
