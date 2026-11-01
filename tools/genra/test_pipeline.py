"""Offline regressions for run constraints and fast checks; no model or Unity calls.

Run with the GenRA Python environment, like test_genra.py. Rig-dependent tests
skip on machines without the local Manki pack.
"""
from __future__ import annotations

import contextlib
import io
import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

try:
    import numpy as np
    from pxr import Usd  # noqa: F401
except ImportError as error:
    raise unittest.SkipTest(f'GenRA environment unavailable: {error}')

TOOLS = Path(__file__).resolve().parents[1]
REPO = TOOLS.parent
sys.path.insert(0, str(TOOLS))

from genra.brief import Brief, resolved_brief_path
from genra.checks import check_clip, format_report
from genra.clip import Clip
from genra.run import author, capture_checkpoint, prepare_brief, seed_candidate

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
            code = main([str(self.source), '--from', str(first), '--rounds', '1', '--director'])
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

    def test_blocking_or_placeholder_cannot_qualify_as_finished_motion(self):
        from genra.loop import full_checks_passed
        report = self.root / 'check.json'
        for flags in ({'stage': 'keys'}, {'checkpointOnly': True}):
            report.write_text(json.dumps({'mode': 'full', 'passed': True, **flags}))
            self.assertFalse(full_checks_passed(report))

    def test_key_stage_defers_interpolation_and_window_targets(self):
        clip = Clip.from_keys({0: self.entry, 12: self.entry}, 13)
        clip.set_pose(3, clip.pose(3).rotate('RightArm', [0, 1, 0], 150))
        spec = Brief(self.source).check_spec(stage='keys')
        spec['targets'] = [{'name': 'window target', 'kind': 'range', 'a': 'LeftHand', 'axis': 'up',
                            'window': [0, 12], 'min': 10},
                           {'name': 'key target', 'kind': 'distance', 'a': 'LeftHand', 'b': 'RightHand',
                            'tick': 6, 'min': 10}]
        report = check_clip(clip, spec, mode='fast')
        self.assertNotIn('snap', {f['check'] for f in report['failures']})
        self.assertIn('snap', {f['check'] for f in report['transitionRisks']})
        self.assertEqual([f['target'] for f in report['failures'] if f['check'] == 'target'], ['key target'])
        self.assertIn('KEY POSES', format_report(report))

    def test_key_stage_does_not_lose_a_bad_key_when_worst_humanoid_sample_is_between_keys(self):
        from scipy.spatial.transform import Rotation
        clip = Clip.from_keys({0: self.entry, 12: self.entry}, 13)
        clip.set_pose(3, clip.pose(3).rotate('RightArm', [0, 1, 0], 90))
        clip.set_pose(6, clip.pose(6).rotate('RightArm', [0, 1, 0], 30))
        j = clip.rig.index('RightArm')
        reference = Rotation.from_quat(self.entry.q[j])

        def excess(index, q):
            return float(np.degrees((reference.inv() * Rotation.from_quat(q)).magnitude())), 'test muscle'

        with patch.object(clip.rig, 'humanoid_limited', [j]), patch.object(clip.rig, 'humanoid_excess', side_effect=excess):
            report = check_clip(clip, Brief(self.source).check_spec(stage='keys'), mode='fast')
        self.assertEqual(next(f['tick'] for f in report['failures'] if f['check'] == 'humanoid'), 6)
        self.assertEqual(next(f['tick'] for f in report['transitionRisks'] if f['check'] == 'humanoid'), 3)

    def test_cli_inherits_saved_blocking_stage_and_allows_full_motion_check(self):
        from genra.__main__ import main
        run, _ = self.make_run()
        for directory in (run, run / 'scratch'):
            data = json.loads((directory / 'brief.json').read_text())
            data['authoring_stage'] = 'keys'
            (directory / 'brief.json').write_text(json.dumps(data))
        for flags, expected in (([], 'keys'), (['--stage', 'full'], None)):
            output = io.StringIO()
            with contextlib.redirect_stdout(output):
                main(['check', str(run / 'scratch/result.json'), '--brief', str(self.source), '--fast', '--json', *flags])
            self.assertEqual(json.loads(output.getvalue()).get('stage'), expected)

    def test_atomic_save_preserves_the_previous_clip_when_serialization_fails(self):
        before = self.keys.read_bytes()

        def partial(value, stream, **kwargs):
            stream.write('{')
            raise ValueError('interrupted serialization')

        with patch('genra.clip.json.dump', side_effect=partial), self.assertRaises(ValueError):
            self.clip.save(self.keys)
        self.assertEqual(self.keys.read_bytes(), before)
        self.assertFalse(list(self.root.glob('*.tmp')))

    def test_scaffold_runs_offline_and_checkpoint_keeps_the_last_loadable_save(self):
        run, brief = self.make_run()
        scratch = run / 'scratch'
        checkpoint = seed_candidate(brief, scratch, run)
        self.assertIn('Clip.load(rig:', (scratch / 'API.md').read_text())
        previous = Path.cwd()
        try:
            os.chdir(scratch)
            with contextlib.redirect_stdout(io.StringIO()):
                exec(compile((scratch / 'author.py').read_text(), 'author.py', 'exec'), {})
        finally:
            os.chdir(previous)
        clip = Clip.load(brief.rig, scratch / 'result.json', frame=brief.entry_frame)
        clip.set_pose(6, clip.pose(6).move_hips([0, 0, 0.01]))
        clip.save(scratch / 'result.json')
        self.assertTrue(capture_checkpoint(brief, scratch, run, checkpoint))
        saved = (run / 'checkpoint-result.json').read_bytes()
        (scratch / 'result.json').write_text('{')
        self.assertFalse(capture_checkpoint(brief, scratch, run, checkpoint))
        self.assertEqual((run / 'checkpoint-result.json').read_bytes(), saved)

    def test_timeout_recovers_a_saved_candidate_and_reports_unknown_cost(self):
        def provider(prompt, images, scratch, evidence, *args, **kwargs):
            brief = Brief(scratch / 'brief.json')
            clip = Clip.load(brief.rig, scratch / 'result.json', frame=brief.entry_frame)
            clip.set_pose(6, clip.pose(6).move_hips([0, 0, 0.01]))
            clip.save(scratch / 'result.json')
            self.assertTrue(kwargs['progress']())
            (scratch / 'result.json').write_text('{')
            return None, None

        with patch('genra.run.build_prompt', return_value='Test'), \
                patch('genra.run.verify_rigpack', return_value={'files': {}}), \
                patch('genra.run.claude_runner.run', side_effect=provider), \
                patch('genra.run.postprocess', return_value={'passed': False}), \
                contextlib.redirect_stdout(io.StringIO()):
            code, run = author(self.source, name='offline-timeout', out_root=self.root / 'runs')
        self.addCleanup(__import__('shutil').rmtree, Path('/tmp/genra-runs') / run.name)
        summary = json.loads((run / 'summary.json').read_text())
        self.assertEqual(code, 1)
        self.assertEqual(summary['status'], 'timed_out')
        self.assertIsNone(summary['total_cost_usd'])
        self.assertTrue(summary['recoveredCheckpoint'])
        self.assertFalse(summary['checkpointOnly'])
        self.assertEqual((run / 'scratch/result.json').read_bytes(), (run / 'checkpoint-result.json').read_bytes())

    def test_chatter_warning_does_not_add_a_hard_failure(self):
        sample = {'channel': 'Left knee bend', 'ticks': [2, 10], 'reversals': 6, 'maxStepDegrees': 15.0}
        spec = {'clearance': False, 'face': False, 'arm_gap': False}
        before = check_clip(self.clip, spec | {'chatter': False})
        with patch('genra.curves.chatter', return_value=[sample]):
            after = check_clip(self.clip, spec)
        self.assertEqual(before['failures'], after['failures'])
        self.assertIn('chatter', {w['check'] for w in after['warnings']})

    def test_loop_does_not_buy_a_review_for_an_unchanged_starting_checkpoint(self):
        from genra.loop import main
        run, _ = self.make_run()
        (run / 'summary.json').write_text(json.dumps({'checkpointOnly': True}))
        with patch('genra.loop.REPO', self.root), patch('genra.loop.preflight', return_value=[]), \
                patch('genra.loop.review') as review, patch('genra.loop.author') as author_job, \
                contextlib.redirect_stdout(io.StringIO()):
            code = main([str(self.source), '--from', str(run), '--director'])
        self.assertEqual(code, 1)
        review.assert_not_called()
        author_job.assert_not_called()


class OwnerRouteTests(unittest.TestCase):
    def test_default_and_zero_rounds_never_call_director(self):
        from genra.loop import main
        for options in ([], ['--rounds', '0']):
            with patch('genra.loop.author', return_value=(0, Path('/tmp/take'))) as job, \
                    patch('genra.loop.review') as director:
                self.assertEqual(main(['brief.json', *options]), 0)
                job.assert_called_once()
                director.assert_not_called()

    def test_director_dry_run_never_calls_review(self):
        from genra.loop import main
        with patch('genra.loop.author', return_value=(0, Path('/tmp/take'))) as job, \
                patch('genra.loop.review') as director:
            self.assertEqual(main(['brief.json', '--director', '--dry-run']), 0)
            self.assertTrue(job.call_args.kwargs['dry_run'])
            director.assert_not_called()

    def test_owner_file_markdown_json_and_inline_reach_revision(self):
        from genra.run import main
        with tempfile.TemporaryDirectory() as tmp:
            for suffix, content, expected in (
                    ('.md', '# Keep\nKeys within 2 cm.', '# Keep\nKeys within 2 cm.'),
                    ('.json', '{"keep": "keys"}', '{\n  "keep": "keys"\n}')):
                path = Path(tmp) / ('direction' + suffix)
                path.write_text(content)
                with patch('genra.run.author', return_value=(0, Path(tmp))) as job:
                    self.assertEqual(main(['brief.json', '--revise', 'take', '--owner-file', str(path)]), 0)
                    self.assertEqual(job.call_args.args[16], expected)
            with patch('genra.run.author', return_value=(0, Path(tmp))) as job:
                main(['brief.json', '--revise', 'take', '--owner', 'Keep the arc'])
                self.assertEqual(job.call_args.args[16], 'Keep the arc')

    def test_loop_cost_display_preserves_unknown_and_real_zero(self):
        from genra.loop import log_round
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / 'checks.txt').write_text('PASS\n')
            lines = []
            with patch('genra.loop.REPO', root):
                log_round(lines, 0, root, {'review': None, 'costUsd': None}, root, None)
            self.assertEqual('\n'.join(lines).count('UNKNOWN'), 2)
            self.assertNotIn('$0.00', '\n'.join(lines))
            with patch('genra.loop.REPO', root):
                log_round(lines, 1, root, {'review': None, 'costUsd': 0}, root, 0)
            self.assertEqual('\n'.join(lines).count('$0.00'), 2)

    def test_lint_reports_named_advisory_rules_and_infers_purpose(self):
        from genra.brief import lint_brief, brief_purpose
        data = {'name': 'fighter-a3', 'clock': {'duration_ticks': 60, 'game_duration_ticks': 60},
                'description': 'Recovery has a cocky settle. No flourish.'}
        self.assertEqual(brief_purpose(data), ('attack', True))
        self.assertEqual({w.rule for w in lint_brief(data)},
                         {'author-slow', 'strike-target', 'normal-recovery', 'recovery-flourish', 'contradiction'})
        data.update(purpose='idle', description='A busy loop with gestures.')
        self.assertEqual([w.rule for w in lint_brief(data)], ['idle-budget'])
        data['description'] = 'A calm slow loop, at most one slow gesture.'
        self.assertEqual(lint_brief(data), [])

    def test_lint_detects_same_sentence_conflict_and_respects_numeric_clock(self):
        from genra.brief import lint_brief, brief_purpose
        data = {'purpose': 'attack', 'name': 'test-ground-1',
                'clock': {'duration_ticks': 60, 'game_duration_ticks': 30},
                'strike': ['RightHand'],
                'targets': [{'kind': 'ahead', 'a': 'RightHand', 'b': 'Hips'}],
                'description': 'A plain short recovery with a cocky settle, no flourish.'}
        self.assertEqual({w.rule for w in lint_brief(data)}, {'recovery-flourish', 'contradiction'})
        data['description'] = 'A plain short recovery to the idle pose. No flourish.'
        self.assertEqual(lint_brief(data), [])
        data['clock']['game_duration_ticks'] = 60
        self.assertEqual([w.rule for w in lint_brief(data)], ['author-slow'])
        data.pop('purpose')
        data['name'] = 'fighter-gf-special'
        data['notes'] = 'Blends to air.2 afterwards.'
        self.assertEqual(brief_purpose(data), ('special', True))

    def test_flip_kick_brief_passes_lint_without_loading_rig(self):
        from genra.brief import lint_brief
        path = REPO / 'tools/genra/briefs/fightguy-a2-flip-kick.json'
        self.assertEqual(lint_brief(json.loads(path.read_text())), [])

    def test_lint_prints_before_any_provider_call(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / 'brief.json'
            path.write_text(json.dumps({'name': 'test-g1', 'rigpack': 'missing',
                                        'clock': {'duration_ticks': 60}}))
            output = io.StringIO()
            with patch('genra.run.verify_rigpack', side_effect=ValueError('missing rig')), \
                    patch('genra.claude_runner.run') as provider, contextlib.redirect_stdout(output):
                with self.assertRaisesRegex(ValueError, 'missing rig'):
                    author(path)
            self.assertIn('WARNING [strike-target]', output.getvalue())
            provider.assert_not_called()


class RunnerAndChatterTests(unittest.TestCase):
    def test_chatter_ignores_noise_smooth_motion_and_isolated_snap(self):
        from genra.curves import chatter
        curves = {'Left knee bend': np.tile([0.0, 15.0], 20)}
        with patch('genra.curves.channels', return_value=curves):
            self.assertTrue(chatter(None))
            self.assertTrue(chatter(None, [[0, 40]])[0]['inFastWindow'])
        for values in (np.tile([0.0, 0.01], 20), np.linspace(0, 90, 40),
                       np.r_[np.zeros(20), np.ones(20) * 50], np.sin(np.linspace(0, 2 * np.pi, 60)) * 30):
            with patch('genra.curves.channels', return_value={'Left knee bend': values}):
                self.assertEqual(chatter(None), [])

    def runner(self, timeout, stall, saves=False, interrupted=False):
        from genra.claude_runner import run
        from unittest.mock import MagicMock
        clock = [0.0]
        process = MagicMock()
        process.poll.return_value = None

        def communicate(payload, timeout):
            if interrupted:
                raise KeyboardInterrupt
            clock[0] += timeout
            raise subprocess.TimeoutExpired('mocked provider', timeout)

        process.communicate.side_effect = communicate
        with tempfile.TemporaryDirectory() as tmp, \
                patch('genra.claude_runner.subprocess.Popen', return_value=process), \
                patch('genra.claude_runner.shutil.which', return_value='/mocked/claude'), \
                patch('genra.claude_runner.time.monotonic', side_effect=lambda: clock[0]), \
                patch('genra.claude_runner.os.killpg') as kill:
            code, event = run('test', [], tmp, tmp, [], [], timeout=timeout,
                              progress=lambda: saves, progress_timeout=stall)
            kill.assert_called_once()
            process.wait.assert_called_once()
        return code, process.communicate.call_args_list

    def test_stall_timeout_stops_the_process_and_sends_prompt_only_once(self):
        code, calls = self.runner(30, 10)
        self.assertEqual(code, 124)
        self.assertEqual(len(calls), 2)
        self.assertIsInstance(calls[0].args[0], str)
        self.assertIsNone(calls[1].args[0])

    def test_checkpoints_cannot_extend_the_overall_timeout(self):
        code, calls = self.runner(6, 10, saves=True)
        self.assertIsNone(code)
        self.assertEqual(len(calls), 2)

    def test_interruption_still_cleans_up_the_provider(self):
        code, _ = self.runner(30, 10, interrupted=True)
        self.assertEqual(code, 130)


if __name__ == '__main__':
    unittest.main()
