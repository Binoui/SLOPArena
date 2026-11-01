"""Offline receipts coverage. No Unity or paid model calls."""
import contextlib
import io
import json
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
from importlib.util import find_spec

sys.path.insert(0, str(Path(__file__).resolve().parent))
import ledger
import receipts


class ReceiptTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.repo = Path(self.temp.name)
        self.store = self.repo / 'receipts'
        self.clip = self.repo / 'clip.anim'
        self.clip.write_text('animation bytes')
        self.target = 'Assets/CharacterPackages/example/Animations/kick.anim'
        self.brief = self.repo / 'brief.json'
        self.brief.write_text(json.dumps({'name': 'Kick'}))

    def run_dir(self, name, cost=1, stage='full', keys=None, revise=None, flow=True):
        run = self.repo / name
        run.mkdir()
        (run / 'run-config.json').write_text(json.dumps({'brief': str(self.brief), 'stage': stage,
                                                        'keys': keys, 'revise': revise}))
        (run / 'summary.json').write_text(json.dumps({'total_cost_usd': cost}))
        (run / 'check.json').write_text(json.dumps({'passed': True, 'warnings': [], 'failures': [],
                         'metrics': {'flow: stops per joint per second': .3} if flow else {}}))
        return run

    def build(self, run, export=None):
        return receipts.build_receipt(self.target, 'Assets/candidate.anim', self.clip, run,
                                      export if export is not None else {'transforms': {'ticks': None, 'retime': None}}, self.repo)

    def test_shared_keys_are_counted_once_and_checks_snapshotted(self):
        self.run_dir('keys', .7, 'keys', flow=False)
        self.run_dir('first', .8, keys='keys')
        run = self.run_dir('revision', 3.5, keys='keys', revise='first')
        r = self.build(run)
        self.assertEqual([x['run'] for x in r['runs']], ['keys', 'first', 'revision'])
        self.assertAlmostEqual(r['cost']['total_usd'], 5)
        self.assertEqual(r['keys_run'], 'keys')
        self.assertEqual(r['revise_parent'], 'first')
        self.assertEqual(r['final_check']['status'], 'PASS')
        self.assertIn('flow: stops per joint per second', r['final_check']['flow_metrics'])
        self.assertEqual(r['clip_sha256'], receipts.sha256(self.clip))
        self.assertIsNone(r['owner_verdict'])

    def test_unknown_cost_is_partial_even_with_known_zero(self):
        self.run_dir('keys', None, 'keys')
        r = self.build(self.run_dir('take', 0, keys='keys'))
        self.assertEqual(r['cost']['known_usd'], 0)
        self.assertIsNone(r['cost']['total_usd'])
        self.assertEqual(r['cost']['completeness'], 'partial')
        receipts.save_receipt(r, self.store)
        self.assertIn('UNKNOWN (partial >= 0.00)', receipts.summary(self.store))

    def test_missing_summary_and_missing_parent_remain_unknown(self):
        run = self.run_dir('take', None, revise='missing')
        (run / 'summary.json').unlink()
        r = self.build(run)
        self.assertEqual(len(r['runs']), 2)
        self.assertEqual(r['cost']['completeness'], 'UNKNOWN')
        self.assertIsNone(r['cost']['total_usd'])

    def test_explicit_zero_cost_is_complete(self):
        r = self.build(self.run_dir('take', 0))
        self.assertEqual(r['cost']['total_usd'], 0)
        self.assertEqual(r['cost']['completeness'], 'complete')

    def test_result_file_keys_link_finds_owning_run(self):
        keys = self.run_dir('keys', .5, stage='keys')
        result = keys / 'scratch' / 'result.json'
        result.parent.mkdir()
        result.write_text('{}')
        self.assertEqual(receipts.source_run_for(result), keys)
        r = self.build(self.run_dir('take', 1, keys=str(result)))
        self.assertEqual(len(r['runs']), 2)
        self.assertEqual(r['cost']['total_usd'], 1.5)

    def test_cycles_are_rejected(self):
        run = self.run_dir('take', revise='take')
        with self.assertRaisesRegex(ValueError, 'Cycle'):
            self.build(run)

    def test_repromotion_keeps_verdict_and_changed_clip_archives_it(self):
        run = self.run_dir('take')
        receipts.save_receipt(self.build(run), self.store)
        receipts.verdict('Kick', 'good', 'Played it', self.store)
        path = receipts.receipt_path(self.target, self.store)
        original = path.read_text()
        receipts.save_receipt(self.build(run), self.store)
        self.assertEqual(path.read_text(), original)
        self.assertIn('| yes | 1 | good |', receipts.summary(self.store))
        self.clip.write_text('changed')
        receipts.save_receipt(self.build(run), self.store)
        self.assertIsNone(receipts.load(path)['owner_verdict'])
        history = list((self.store / 'history').glob('*/*.json'))
        self.assertEqual(len(history), 1)
        self.assertEqual(receipts.load(history[0])['owner_verdict'], 'good')

    def test_revised_good_take_is_not_first_take_acceptance(self):
        self.run_dir('first')
        receipts.save_receipt(self.build(self.run_dir('second', revise='first')), self.store)
        receipts.verdict(self.target, 'good', store=self.store)
        self.assertIn('| no | 2 | good |', receipts.summary(self.store))

    def test_missing_flow_and_verdict_are_unknown(self):
        receipts.save_receipt(self.build(self.run_dir('take', flow=False)), self.store)
        self.assertIn('| UNKNOWN | 0 (+1 UNKNOWN) | UNKNOWN |', receipts.summary(self.store))

    def test_transformed_check_does_not_reuse_source_flow(self):
        run = self.run_dir('take')
        r = self.build(run, {'transforms': {'retime': [[0, 0], [10, 20]]},
                             'phaseCheck': {'passed': False, 'failures': ['snap']}})
        self.assertEqual(r['final_check']['status'], 'FAIL')
        self.assertEqual(r['final_check']['flow_metrics'], {})
        self.assertEqual(r['final_check']['scope'], 'export phase')

    def test_unrecorded_export_cannot_claim_final_pass(self):
        r = self.build(self.run_dir('take'), {})
        self.assertEqual(r['final_check']['status'], 'UNKNOWN')

    def test_export_lookup_requires_content_and_rejects_stale_report(self):
        exports = self.repo / 'exports'
        report = exports / 'one' / 'report.json'
        report.parent.mkdir(parents=True)
        data = {'asset': {'path': 'Assets/candidate.anim', 'guid': 'example'}, 'assetSha256': receipts.sha256(self.clip), 'sourceRun': 'run'}
        report.write_text(json.dumps(data))
        self.assertEqual(receipts.export_for(data['asset']['path'], self.clip, exports=exports), data)
        self.clip.write_text('changed')
        self.assertEqual(receipts.export_for(data['asset']['path'], self.clip, exports=exports), {})
        with self.assertRaisesRegex(ValueError, 'changed'):
            receipts.export_for(data['asset']['path'], self.clip, report)

    def test_ambiguous_move_requires_target(self):
        r = self.build(self.run_dir('take'))
        receipts.save_receipt(r, self.store)
        r = dict(r, target='Assets/another.anim')
        receipts.save_receipt(r, self.store)
        with self.assertRaisesRegex(ValueError, 'found 2'):
            receipts.verdict('Kick', 'redo', store=self.store)
        receipts.verdict(self.target, 'rejected', store=self.store)

    def test_cli_verdict_summary_and_dry_run(self):
        run = self.run_dir('take')
        receipts.save_receipt(self.build(run), self.store)
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            receipts.main(['--store', str(self.store), 'verdict', 'Kick', 'good', '--note', 'Works'])
            receipts.main(['--store', str(self.store), 'summary'])
        self.assertIn('good', output.getvalue())
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            receipts.main(['--store', str(self.repo / 'unused'), 'dry-run', str(run), '--target', self.target, '--clip', str(self.clip)])
        self.assertEqual(json.loads(output.getvalue())['cost']['total_usd'], 1)
        self.assertFalse((self.repo / 'unused').exists())

    def test_ledger_unknown_is_not_zero(self):
        run = self.run_dir('take', None)
        self.assertIsNone(ledger.recorded_cost(run))
        self.assertIn('UNKNOWN', ledger.table([ledger.row(run)]))
        self.assertNotIn('0.00 (', ledger.table([ledger.row(run)]))
        (run / 'summary.json').unlink()
        self.assertIn('UNKNOWN', ledger.table([ledger.row(run)]))

    @unittest.skipUnless(find_spec('numpy') and find_spec('scipy'), 'GenRA environment unavailable')
    def test_promote_writes_receipt_from_actual_target_without_unity(self):
        sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
        from genra import unity_clip
        source = 'Assets/candidate.anim'
        unity = self.repo / 'unity'
        (unity / 'Assets').mkdir(parents=True)
        (unity / source).write_text('source')
        run = self.run_dir('take')
        def promote_job(job, directory, template):
            target = unity / job['target']
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text('target bytes with retained name')
            return {'target': job['target']}
        with patch.object(unity_clip, 'UNITY', unity), patch.object(unity_clip, 'OUT', self.repo / 'exports'), \
             patch.object(unity_clip, 'REPO', self.repo), patch.object(unity_clip, 'run_unity', side_effect=promote_job), \
             patch('genra.receipts.save_receipt', side_effect=lambda r: receipts.save_receipt(r, self.store)):
            result = unity_clip.promote(source, self.target, run)
        r = receipts.load(self.repo / result['receipt'])
        self.assertEqual(r['clip_sha256'], receipts.sha256(unity / self.target))
        self.assertIsNone(r['owner_verdict'])


if __name__ == '__main__':
    unittest.main()
