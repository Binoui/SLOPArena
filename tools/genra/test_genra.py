"""genra toolkit tests. Run with the GenRA environment from the repository root:

    <genra python> -m unittest discover -s tools/genra -p 'test_*.py'

They skip without numpy/scipy/usd-core (plain CI Python) or without the local Manki rig
pack (artifacts/genra-rigpacks/manki-sr199). Comparisons with earlier runs skip when
their artifacts are absent.
"""
from __future__ import annotations

import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

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
            self.assertEqual(int(frames), 2 * len(clip) + 2 * len(clip))

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
        clip = Clip.from_keys({0: entry, 8: ready, 16: ready, 24: entry}, 25)
        spec = {'clearance': False, 'face': False, 'snap_degrees_per_tick': 90, 'two_hands': [8, 16]}
        self.assertNotIn('grip', {f['check'] for f in check_clip(clip, spec)['failures']})
        drifting = Clip.from_keys({0: entry, 8: ready, 24: entry}, 25)
        self.assertIn('grip', {f['check'] for f in check_clip(drifting, dict(spec, two_hands=[8, 12]))['failures']})
        held = hold_grip(drifting, 8, 12)
        self.assertLess(held['maxMissMetres'], 0.005)
        self.assertNotIn('grip', {f['check'] for f in check_clip(drifting, dict(spec, two_hands=[8, 12]))['failures']})

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


if __name__ == '__main__':
    unittest.main()
