"""Offline regressions for run constraints and fast checks; no model or Unity calls.

Run with the GenRA Python environment, like test_genra.py. Rig-dependent tests
skip on machines without the local Manki pack.
"""
from __future__ import annotations

import contextlib
import io
import json
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

try:
    import numpy  # noqa: F401
    from pxr import Usd  # noqa: F401
except ImportError as error:
    raise unittest.SkipTest(f'GenRA environment unavailable: {error}')

TOOLS = Path(__file__).resolve().parents[1]
REPO = TOOLS.parent
sys.path.insert(0, str(TOOLS))

from genra.brief import Brief, resolved_brief_path
from genra.checks import check_clip, format_report
from genra.clip import Clip
from genra.run import prepare_brief

PACK = REPO / 'artifacts/genra-rigpacks/manki-sr199'


@unittest.skipUnless((PACK / 'rig.usda').is_file(), 'Manki rig pack not present')
class PipelineTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.reference = Brief(TOOLS / 'genra/benchmarks/manki-aerial-double-fist.json')
        cls.entry = cls.reference.entry_pose
        cls.clip = Clip.from_keys({0: cls.entry, 12: cls.entry}, 13)

    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.source = self.root / 'source.json'
        self.source.write_text(json.dumps({
            'name': 'pipeline-test', 'description': 'Keep the approved poses.', 'rigpack': str(PACK),
            'references': self.reference['references'], 'entry': self.reference['entry'],
            'clock': {'duration_ticks': 12}, 'keys': [0, 6, 12], 'review_ticks': [0, 6, 12],
            'timing': {'fixed': ['12 ticks'], 'adjustable': []}}))
        self.keys = self.clip.save(self.root / 'approved.json')

    def make_run(self, name='run', moved=False, passed=True):
        run = self.root / name
        scratch = run / 'scratch'
        scratch.mkdir(parents=True)
        brief = prepare_brief(Brief(self.source), scratch, run, self.keys)
        clip = Clip.load(brief.rig, self.keys, frame=brief.entry_frame)
        if moved:
            for tick in range(4, 9):
                clip.set_pose(tick, clip.pose(tick).move_hips([0, 0.1, 0]))
        clip.save(scratch / 'result.json')
        (run / 'run-config.json').write_text(json.dumps({'brief': str(self.source), 'stage': 'full'}))
        (run / 'check.json').write_text(json.dumps({'passed': passed, 'mode': 'full'}))
        (run / 'summary.json').write_text(json.dumps({'total_cost_usd': 0}))
        return run, brief

    def test_run_keeps_its_contract_when_source_and_scratch_change(self):
        run, brief = self.make_run(moved=True)
        self.source.write_text('{}')
        (run / 'scratch/brief.json').write_text('{}')
        (run / 'scratch/keys/result.json').write_text('{}')
        self.keys.unlink()
        resolved = resolved_brief_path(self.source, run / 'scratch/result.json')
        self.assertEqual(resolved, run / 'brief.json')
        clip = Clip.load(brief.rig, run / 'scratch/result.json', frame=brief.entry_frame)
        report = check_clip(clip, Brief(resolved).check_spec(), mode='fast')
        self.assertIn('keys', {f['check'] for f in report['failures']})

    def test_a_brief_with_existing_approved_keys_copies_them_and_their_ticks(self):
        data = json.loads(self.source.read_text())
        data['approved_keys'] = {'file': self.keys.name, 'ticks': [6]}
        self.source.write_text(json.dumps(data))
        run = self.root / 'run'
        scratch = run / 'scratch'
        scratch.mkdir(parents=True)
        brief = prepare_brief(Brief(self.source), scratch, run)
        self.keys.unlink()
        self.assertEqual(brief.check_spec()['keys']['ticks'], [6])
        self.assertEqual(Brief(scratch / 'brief.json').check_spec()['keys']['ticks'], [6])

    def test_legacy_run_uses_matching_archive_and_rejects_a_lost_key_lock(self):
        run, _ = self.make_run()
        (run / 'keys/result.json').unlink()  # old runs only archived this file in scratch/
        result = run / 'scratch/result.json'
        self.assertEqual(resolved_brief_path(self.source, result), run / 'scratch/brief.json')
        data = json.loads((run / 'scratch/brief.json').read_text())
        del data['approved_keys']
        (run / 'scratch/brief.json').write_text(json.dumps(data))
        with self.assertRaisesRegex(ValueError, 'missing or the brief copies disagree'):
            resolved_brief_path(self.source, result)

    def test_standalone_clips_keep_the_supplied_brief(self):
        self.assertEqual(resolved_brief_path(self.source, self.keys), self.source)

    def test_fast_keeps_hard_failures_and_skips_all_mesh_queries(self):
        run, brief = self.make_run(moved=True)
        clip = Clip.load(brief.rig, run / 'scratch/result.json', frame=brief.entry_frame)
        clip.set_pose(6, clip.pose(6).rotate('RightArm', [0, 1, 0], 150))
        spec = brief.check_spec() | {'duration_ticks': 11, 'planted': ['LeftFoot'],
            'loop_from_tick': 0, 'targets': [
                {'name': 'impossible gap', 'kind': 'distance', 'a': 'LeftHand', 'b': 'RightHand', 'tick': 6, 'min': 10}]}
        # Force the gait branch so a forgotten arm-gap query would construct Skin too.
        with patch('genra.checks.stepping', return_value=True), \
                patch('genra.skin.Skin', side_effect=AssertionError('fast mode touched the mesh')):
            fast = check_clip(clip, spec, mode='fast')
            numeric = check_clip(clip, spec | {'clearance': False, 'face': False, 'arm_gap': False})
        self.assertEqual(fast['failures'], numeric['failures'])
        self.assertEqual(fast['metrics'], numeric['metrics'])
        self.assertTrue({'clock', 'keys', 'planted', 'snap', 'target'} <= {f['check'] for f in fast['failures']})
        self.assertEqual(fast['skipped'], ['clearance', 'face', 'arm_gap'])
        self.assertTrue(format_report(fast).startswith('FAST FAIL'))
        with self.assertRaises(ValueError):
            check_clip(clip, spec, mode='typo')

    def test_fast_cli_has_no_automatic_renders_and_enforces_saved_keys(self):
        from genra.__main__ import main
        run, _ = self.make_run(moved=True)
        output = io.StringIO()
        with patch('genra.__main__.contact_sheet') as sheet, patch('genra.render.motion_overview') as motion, \
                patch('genra.skin.Skin', side_effect=AssertionError('fast mode touched the mesh')), \
                contextlib.redirect_stdout(output):
            code = main(['check', str(run / 'scratch/result.json'), '--brief', str(self.source), '--fast', '--json'])
        report = json.loads(output.getvalue())
        self.assertEqual(code, 1)
        self.assertIn('keys', {f['check'] for f in report['failures']})
        sheet.assert_not_called()
        motion.assert_not_called()

    def test_default_cli_still_runs_full_checks_and_renders(self):
        from genra.__main__ import main
        run, _ = self.make_run()
        with patch('genra.__main__.check_clip', return_value={
                'mode': 'full', 'passed': True, 'failures': [], 'warnings': [], 'metrics': {}}) as check, \
                patch('genra.__main__.contact_sheet') as sheet, patch('genra.render.motion_overview') as motion, \
                contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(main(['check', str(run / 'scratch/result.json'), '--brief', str(self.source)]), 0)
        self.assertEqual(check.call_args.kwargs['mode'], 'full')
        sheet.assert_called_once()
        motion.assert_called_once()

    def test_review_uses_the_saved_contract_in_its_evidence_and_prompt(self):
        from genra.review import review
        run, _ = self.make_run()
        output = self.root / 'review'
        output.mkdir()

        def evidence(brief_path, result, directory, *args):
            self.assertEqual(brief_path, run / 'brief.json')
            self.assertIn('keys', Brief(brief_path).check_spec())
            (directory / 'checks.txt').write_text('PASS\n')
            return [], []

        with patch('genra.review.evidence', side_effect=evidence), \
                patch('genra.review.claude_runner.run', return_value=(0, {
                    'structured_output': {'verdict': 'accept', 'summary': 'Fine.', 'issues': [], 'keep': []}})) as model:
            verdict = review(self.source, run / 'scratch/result.json', output)
        self.assertEqual(verdict['brief'], str(run / 'brief.json'))
        self.assertIn('Approved key poses at ticks 0, 6, 12 must be preserved', model.call_args.args[0])

    def test_reused_legacy_pass_is_rechecked_and_catches_moved_keys(self):
        from genra.review import reuse_evidence
        run, _ = self.make_run(moved=True)
        result = run / 'scratch/result.json'
        source, output = self.root / 'old-review', self.root / 'new-review'
        source.mkdir()
        output.mkdir()
        (source / 'evidence.json').write_text(json.dumps({'images': [], 'captions': []}))
        (source / 'review.json').write_text(json.dumps({'result': str(result)}))
        (source / 'checks.txt').write_text('PASS\n')  # the historical bug
        reuse_evidence(source, output, self.source, result)
        report = json.loads((output / 'checks.json').read_text())
        self.assertEqual(report['mode'], 'full')
        self.assertFalse(report['passed'])
        self.assertIn('keys', {f['check'] for f in report['failures']})
        self.assertTrue((source / 'checks.txt').read_text().startswith('PASS'))  # historical evidence stays intact

    def test_reuse_rejects_evidence_for_different_clip_bytes(self):
        from genra.review import result_identity, reuse_evidence
        run, _ = self.make_run(moved=True)
        source, output = self.root / 'old-review', self.root / 'new-review'
        source.mkdir()
        output.mkdir()
        (source / 'evidence.json').write_text(json.dumps(result_identity(self.keys) | {'images': [], 'captions': []}))
        with self.assertRaisesRegex(ValueError, 'different clip'):
            reuse_evidence(source, output, self.source, run / 'scratch/result.json')
        self.assertFalse((output / 'checks.txt').exists())

    def loop_fixture(self, passing_first):
        from genra.loop import main
        first, _ = self.make_run('r0', passed=passing_first)
        second, _ = self.make_run('r1', passed=False)

        def review(brief_path, result, directory, *args, **kwargs):
            self.assertEqual(brief_path, result.parent.parent / 'brief.json')
            # Even a stale or mistaken review PASS cannot override a failed authoring gate.
            (directory / 'checks.json').write_text(json.dumps({'passed': True, 'mode': 'full'}))
            (directory / 'checks.txt').write_text('PASS\n')
            (directory / 'evidence.json').write_text(json.dumps({'asset': 'candidate.anim', 'unityRender': 'renders'}))
            notes = {'verdict': 'revise' if result.parent.parent == first else 'accept',
                     'summary': 'Review', 'issues': [], 'keep': []}
            return {'review': notes, 'costUsd': 0}

        with patch('genra.loop.REPO', self.root), patch('genra.loop.preflight', return_value=[]), \
                patch('genra.loop.author', return_value=(0, second)), patch('genra.loop.review', side_effect=review), \
                contextlib.redirect_stdout(io.StringIO()):
            code = main([str(self.source), '--from', str(first), '--rounds', '1'])
        log = next((self.root / 'artifacts/genra-loops').glob('*/loop.md')).read_text()
        return code, log

    def test_loop_rejects_failed_revision_despite_review_pass_and_acceptance(self):
        code, log = self.loop_fixture(passing_first=True)
        self.assertEqual(code, 0)
        self.assertIn('final is round 0', log)
        self.assertIn('Clip: `r0`', log.split('## Final')[1])

    def test_loop_reports_no_candidate_if_every_authoring_check_failed(self):
        code, log = self.loop_fixture(passing_first=False)
        self.assertEqual(code, 1)
        self.assertIn('## No passing candidate', log)
        self.assertNotIn('## Final', log)

    def test_a_fast_pass_cannot_qualify_for_final_selection(self):
        from genra.loop import full_checks_passed, select_final
        report = self.root / 'check.json'
        report.write_text(json.dumps({'mode': 'fast', 'passed': True}))
        self.assertFalse(full_checks_passed(report))
        self.assertIsNone(select_final([(0, None, None, full_checks_passed(report))]))
        report.write_text(json.dumps({'passed': True}))  # legacy full report
        self.assertTrue(full_checks_passed(report))
        report.unlink()
        self.assertFalse(full_checks_passed(report))


if __name__ == '__main__':
    unittest.main()
