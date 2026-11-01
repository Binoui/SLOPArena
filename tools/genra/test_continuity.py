"""Offline continuity and checkpoint plateau regressions. No provider or Unity calls."""
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import MagicMock, patch

try:
    import numpy as np
    from pxr import Usd  # noqa: F401
    from scipy.spatial.transform import Rotation
except ImportError as error:  # plain CI interpreter
    raise unittest.SkipTest(f'GenRA environment unavailable: {error}')

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from genra.brief import Brief
from genra.clip import Clip
from genra.solve import continuity_report
from genra.claude_runner import CheckpointPlateau, run
from genra.run import assess_checkpoint

REPO = Path(__file__).resolve().parents[2]
PACK = REPO / 'artifacts/genra-rigpacks/manki-sr199'


def report(*checks):
    return {'failures': [{'check': c, 'message': 'repair grip at ticks 17..21', 'ticks': [17, 21]}
                         for c in checks]}


class PlateauTests(unittest.TestCase):
    def test_three_stale_changed_reports_stop_and_name_next_task(self):
        p = CheckpointPlateau()
        self.assertFalse(p.observe(report('grip')))
        self.assertFalse(p.observe(report('grip')))
        self.assertFalse(p.observe(report('grip')))
        self.assertTrue(p.observe(report('grip')))
        self.assertEqual(p.next_task['interval'], [17, 21])
        self.assertEqual(p.next_task['check'], 'grip')

    def test_fewer_failures_or_subset_of_kinds_resets_streak(self):
        p = CheckpointPlateau(2)
        p.observe(report('grip', 'flip'))
        p.observe(report('grip', 'flip'))
        self.assertFalse(p.observe(report('grip')))
        self.assertEqual(p.stale, 0)
        p.observe(report('grip', 'flip'))
        self.assertTrue(p.observe(report('grip')))
        # Oscillation back to a previous best is not progress.

    def test_fewer_kinds_can_improve_without_fewer_failure_rows(self):
        p = CheckpointPlateau()
        p.observe(report('grip', 'flip'))
        p.observe(report('grip', 'grip', 'grip'))
        self.assertEqual(p.stale, 0)
        self.assertEqual(p.best_count, 2)

    def test_pass_does_not_stop_and_zero_disables(self):
        for limit in (0, 3):
            p = CheckpointPlateau(limit)
            for _ in range(8):
                self.assertFalse(p.observe(report() if limit else report('grip')))
        with self.assertRaises(ValueError):
            CheckpointPlateau(-1)

    def test_plateau_kills_provider_group_and_returns_distinct_code(self):
        clock = [0]
        process = MagicMock()
        process.poll.return_value = None
        def communicate(payload, timeout):
            clock[0] += timeout
            raise subprocess.TimeoutExpired('mock', timeout)
        process.communicate.side_effect = communicate
        with tempfile.TemporaryDirectory() as tmp, \
             patch('genra.claude_runner.subprocess.Popen', return_value=process), \
             patch('genra.claude_runner.shutil.which', return_value='/mock/claude'), \
             patch('genra.claude_runner.time.monotonic', side_effect=lambda: clock[0]), \
             patch('genra.claude_runner.os.killpg') as kill:
            code, _ = run('test', [], tmp, tmp, [], [], progress=lambda: True, stop=lambda: True)
            self.assertEqual(code, 125)
            kill.assert_called_once()
            process.wait.assert_called_once()


@unittest.skipUnless((PACK / 'rig.usda').exists(), 'local Manki rig absent')
class ContinuityTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.brief = Brief(REPO / 'tools/genra/benchmarks/manki-aerial-double-fist.json')
        cls.pose = cls.brief.entry_pose

    def test_static_report_and_input_unchanged(self):
        c = Clip.from_keys({0: self.pose, 5: self.pose}, 6)
        before = c.q.copy()
        r = continuity_report(c, [0, 5])['intervals'][0]
        self.assertEqual(r['rollFlips'], [])
        self.assertEqual(r['unreachable'], [])
        self.assertAlmostEqual(r['landing'][0]['degrees'], 0)
        np.testing.assert_array_equal(before, c.q)

    def test_large_roll_names_tick_joint_and_interval(self):
        c = Clip.from_keys({0: self.pose, 5: self.pose}, 6)
        j = c.rig.index('LeftArm')
        c.q[3:, j] = (Rotation.from_rotvec([0, np.radians(160), 0]) *
                      Rotation.from_quat(c.q[3:, j])).as_quat()
        r = continuity_report(c, [0, 5])['intervals'][0]
        self.assertTrue(any(x['joint'] == 'LeftArm' and x['degrees'] > 150 for x in r['branchRisks']))
        self.assertEqual(r['interval'], [0, 5])

    def test_unreachable_hand_target_is_geometric_lower_bound(self):
        c = Clip.from_keys({0: self.pose, 5: self.pose}, 6)
        r = continuity_report(c, [0, 5], targets=[{'pos': 'LeftHand', 'at': [100, 0, 0]}])
        self.assertGreater(r['intervals'][0]['unreachable'][0]['beyondReachMetres'], 90)
        with self.assertRaises(ValueError):
            continuity_report(c, [0, 7])

    def test_archived_check_is_used_and_checker_error_is_not_progress(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            c = Clip.from_keys({0: self.pose, 5: self.pose}, 6)
            c.save(root / 'checkpoint-result.json')
            checkpoint = {'sha256': 'test'}
            p = CheckpointPlateau(2)
            with patch('genra.checks.check_clip', return_value=report('grip')):
                assess_checkpoint(self.brief, root, checkpoint, p, 'full')
            self.assertEqual(p.best_count, 1)
            archived = json.loads((root / 'checkpoints/0000-check.json').read_text())
            self.assertEqual(archived['sha256'], 'test')
            p.stale = 1
            with patch('genra.checks.check_clip', side_effect=ValueError('broken target')):
                assess_checkpoint(self.brief, root, checkpoint, p, 'full')
            self.assertFalse(p.stopped)
            self.assertIn('error', json.loads((root / 'checkpoints/0001-check.json').read_text()))

    def test_author_hands_back_archived_plateau_and_next_interval(self):
        import contextlib
        import io
        import shutil
        from genra.run import author
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            source = root / 'brief.json'
            source.write_text(json.dumps({
                'name': 'plateau-test', 'description': 'Keep the pose', 'rigpack': str(PACK),
                'references': self.brief['references'], 'entry': self.brief['entry'],
                'clock': {'duration_ticks': 5}, 'keys': [0, 2, 5], 'review_ticks': [0, 2, 5],
                'timing': {'fixed': ['5 ticks'], 'adjustable': []}}))
            def provider(prompt, images, scratch, *args, **kwargs):
                brief = Brief(scratch / 'brief.json')
                clip = Clip.load(brief.rig, scratch / 'result.json', frame=brief.entry_frame)
                for step in range(3):
                    clip.set_pose(2, clip.pose(2).move_hips([0, 0, .01]))
                    clip.save(scratch / 'result.json')
                    self.assertTrue(kwargs['progress']())
                    self.assertEqual(kwargs['stop'](), step == 2)
                return 125, None
            failure = {'failures': [{'check': 'grip', 'message': 'off handle', 'tick': 3}]}
            with patch('genra.run.verify_rigpack', return_value={'files': {}}), \
                 patch('genra.run.build_prompt', return_value='test'), \
                 patch('genra.run.claude_runner.run', side_effect=provider), \
                 patch('genra.checks.check_clip', return_value=failure), \
                 patch('genra.run.postprocess', return_value={'passed': False}), \
                 contextlib.redirect_stdout(io.StringIO()):
                code, folder = author(source, out_root=root / 'runs')
            self.addCleanup(shutil.rmtree, Path('/tmp/genra-runs') / folder.name)
            summary = json.loads((folder / 'summary.json').read_text())
            self.assertEqual(code, 1)
            self.assertEqual(summary['status'], 'plateaued')
            self.assertEqual(summary['nextTask']['interval'], [2, 5])
            self.assertEqual(summary['nextTask']['check'], 'grip')
            self.assertEqual(len(list((folder / 'checkpoints').glob('*-check.json'))), 4)


class SavedKeysTests(unittest.TestCase):
    def load(self, name):
        folder = REPO / 'artifacts/genra-runs' / name
        if not (folder / 'scratch/result.json').exists():
            self.skipTest('saved Bonk keys absent')
        brief = Brief(folder / 'brief.json')
        clip = Clip.load(brief.rig, folder / 'scratch/result.json', frame=brief.entry_frame)
        return brief, clip

    def test_sunrise_names_documented_roll_and_humanoid_interval(self):
        brief, clip = self.load('20261022-131708-bonk-sir-g3-sunrise-keys')
        row = continuity_report(clip, [31, 35], hold='loose', grip_span=[5, 33])['intervals'][0]
        self.assertTrue(any(r['joint'] == 'LeftArm' and abs(r['degrees'] - 159.02) < .1
                            for r in row['branchRisks']))
        self.assertTrue(any(r['joint'] == 'LeftHand' and r['tick'] == 35 and r['degrees'] > 173
                            for r in row['rollFlips']))
        self.assertTrue(any(r['joint'] == 'LeftArm' and r['tick'] == 33 and r['degrees'] > 40
                            for r in row['humanoidExcess']))

    def test_barn_door_names_return_interval_and_replay_preserves_input(self):
        brief, clip = self.load('20261022-001243-bonk-sir-g2-barn-door-keys')
        before = clip.q.copy()
        row = continuity_report(clip, [41, 47], hold='loose', replay_carry=True)['intervals'][0]
        self.assertEqual(row['branchRisks'][0]['joint'], 'RightHand')
        self.assertGreater(row['carry']['landing (deg)'][47], 50)
        np.testing.assert_array_equal(clip.q, before)


if __name__ == '__main__':
    unittest.main()
