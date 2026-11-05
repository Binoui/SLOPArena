"""Factory feedback integration tests. No external services or packages required."""
import hashlib
import json
import tempfile
import threading
import unittest
import uuid
from concurrent.futures import ThreadPoolExecutor
from contextlib import closing
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from unittest.mock import patch
from urllib.parse import parse_qs, urlsplit

from content_factory import server as factory


class FakeMaster(BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass

    def do_GET(self):
        state = self.server.state
        self.server.calls.append(self.path)
        if self.headers.get('X-Console-Key') != state['key']:
            self.send_error(403)
            return
        path = urlsplit(self.path)
        if state.get('error'):
            self.send_response(500)
            self.end_headers()
            self.wfile.write(state['key'].encode())
            return
        if path.path == '/owner/feedback':
            index = int(parse_qs(path.query).get('cursor', ['0'])[0])
            reports = state['reports']
            data = json.dumps({'items': [dict(reportId=d['reportId'], receivedAtUtc=d['receivedAtUtc'],
                    kind=d['kind'], appVersion=d['context']['appVersion'], hasScreenshot=d['screenshot'] is not None)
                    for d in reports[index:index+1]],
                    'nextCursor': str(index+1) if index+1 < len(reports) else None}).encode()
        else:
            report_id = path.path.split('/')[3]
            report = next(d for d in state['reports'] if d['reportId'] == report_id)
            if path.path.endswith('/screenshot'):
                data = state['image'][:-2] if state.get('truncate') else state['image']
            else:
                data = json.dumps(report).encode()
        self.send_response(200)
        self.send_header('Content-Length', str(len(data)))
        self.end_headers()
        self.wfile.write(data)


def report(image=None, **changes):
    return dict(reportId=str(uuid.uuid4()), receivedAtUtc='2026-11-04T12:00:00Z',
        reporter={'steamId': '76561198000000000', 'displayName': 'Playtester'}, kind='bug',
        message='Landing felt stuck after the hit.', context={'appVersion': '0.9-demo', 'protocolVersion': 17,
        'platform': 'Linux', 'scene': 'Arena', 'phase': 'match', 'observedAtUtc': '2026-11-04T11:59:58Z',
        'match': {'mode': 'online', 'stageId': 'roof', 'fighters': [{'entityId': 1, 'packageId': 'bashie'},
          {'entityId': 2, 'packageId': 'wibou'}]}}, onlineMatchId='friends-demo',
        screenshot={'bytes': len(image), 'sha256': hashlib.sha256(image).hexdigest()} if image else None) | changes


class FeedbackTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.old_data = factory.DATA
        self.addCleanup(setattr, factory, 'DATA', self.old_data)
        factory.DATA = Path(self.temp.name)
        with closing(factory.db()) as con:
            con.executescript(factory.SCHEMA)
        self.key = 'SENSITIVE-owner-key-do-not-expose'
        self.key_file = factory.DATA / 'owner.key'
        self.key_file.write_text(self.key + '\n')
        self.image = b'\xff\xd8\xfffake-jpeg\xff\xd9'
        self.state = dict(key=self.key, reports=[report(self.image)], image=self.image)
        self.master = ThreadingHTTPServer(('127.0.0.1', 0), FakeMaster)
        self.master.state, self.master.calls = self.state, []
        threading.Thread(target=self.master.serve_forever, daemon=True).start()
        self.addCleanup(self.master.server_close)
        self.addCleanup(self.master.shutdown)
        self.source = factory.FeedbackSource('local-master', 'http://127.0.0.1:' + str(self.master.server_port), self.key_file)
        self.id = self.state['reports'][0]['reportId']
        factory.PULL_HEALTH.update(lastSuccessUtc=None, lastError=None)

    def pull(self):
        return factory.pull_player_reports_once(self.source)

    def test_second_pull_only_lists(self):
        self.assertEqual(self.pull()['imported'], 1)
        before = len(self.master.calls)
        self.assertEqual(self.pull()['imported'], 0)
        self.assertEqual(len(self.master.calls) - before, 1)
        self.assertEqual(factory.player_reports()['items'][0]['imageUrl'],
                         '/api/player-reports/local-master/' + self.id + '/screenshot.jpg')

    def test_paging_remote_and_board(self):
        self.state['reports'] += [report(), report()]
        self.assertEqual(self.pull()['imported'], 3)
        first = factory.player_reports(limit=2)
        self.assertEqual(len(first['items']), 2)
        self.assertEqual(len(factory.player_reports(before=first['nextCursor'], limit=2)['items']), 1)

    def test_interrupted_screenshot_retries(self):
        self.state['truncate'] = True
        self.assertEqual(self.pull()['failed'], 1)
        self.assertEqual(factory.player_reports()['items'], [])
        self.state['truncate'] = False
        self.assertEqual(self.pull()['imported'], 1)

    def test_hash_mismatch_not_imported(self):
        self.state['reports'][0]['screenshot']['sha256'] = '0' * 64
        self.assertEqual(self.pull()['failed'], 1)
        self.assertEqual(factory.player_reports()['items'], [])

    def test_bad_item_does_not_block_next(self):
        self.state['reports'][0]['kind'] = 'invalid'
        self.state['reports'].append(report())
        result = self.pull()
        self.assertEqual((result['imported'], result['failed']), (1, 1))

    def test_repull_preserves_triage(self):
        self.pull()
        factory.patch_player_report(self.source.name, self.id, {'dismissed': True})
        self.pull()
        self.assertEqual(factory.player_report(self.source.name, self.id)['status'], 'dismissed')
        factory.patch_player_report(self.source.name, self.id, {'dismissed': False})
        self.assertEqual(factory.player_report(self.source.name, self.id)['status'], 'new')

    def test_concurrent_promote_creates_one_check_with_image(self):
        self.pull()
        def promote(_):
            return factory.promote_player_report(self.source.name, self.id, {'title': 'Landing', 'steps': 'Try landing.'})
        with ThreadPoolExecutor(2) as pool:
            checks = list(pool.map(promote, range(2)))
        self.assertEqual(checks[0]['id'], checks[1]['id'])
        self.assertTrue(checks[0]['media'][0].endswith('/screenshot.jpg'))
        with closing(factory.db()) as con:
            self.assertEqual(con.execute('SELECT COUNT(*) FROM checks').fetchone()[0], 1)
        self.pull()
        self.assertEqual(factory.player_report(self.source.name, self.id)['checkId'], checks[0]['id'])
        repeated = factory.promote_player_report(self.source.name, self.id, {})
        self.assertEqual(repeated['title'], 'Landing')

    def test_promotion_rollback(self):
        self.pull()
        original = factory._insert_check
        def fail(con, fields):
            original(con, fields)
            raise RuntimeError('interrupted')
        with patch.object(factory, '_insert_check', fail), self.assertRaises(RuntimeError):
            factory.promote_player_report(self.source.name, self.id, {'title': 'x', 'steps': 'y'})
        with closing(factory.db()) as con:
            self.assertEqual(con.execute('SELECT COUNT(*) FROM checks').fetchone()[0], 0)
        self.assertEqual(factory.player_report(self.source.name, self.id)['status'], 'new')

    def test_key_never_in_status_or_board(self):
        self.state['error'] = True
        result = self.pull()
        self.assertIsNotNone(result['lastError'])
        self.assertNotIn(self.key, json.dumps(result))
        self.assertNotIn(self.key, json.dumps(factory.board()))
        self.key_file.unlink()
        self.assertNotIn(self.key, json.dumps(self.pull()))

    def test_worker_disabled_unconfigured(self):
        with patch.dict('os.environ', {}, clear=True):
            self.assertIsNone(factory.start_player_feedback_worker())
            self.assertFalse(factory.request_player_report_pull()['enabled'])

    def test_http_routes(self):
        import urllib.request
        self.pull()
        server = ThreadingHTTPServer(('127.0.0.1', 0), factory.Handler)
        threading.Thread(target=server.serve_forever, daemon=True).start()
        self.addCleanup(server.server_close)
        self.addCleanup(server.shutdown)
        base = 'http://127.0.0.1:' + str(server.server_port)
        with urllib.request.urlopen(base + '/api/player-reports') as r:
            self.assertEqual(len(json.load(r)['items']), 1)
        with urllib.request.urlopen(base + '/api/player-reports/local-master/' + self.id + '/screenshot.jpg') as r:
            self.assertEqual(r.read(), self.image)
            self.assertEqual(r.headers['Content-Type'], 'image/jpeg')
            self.assertEqual(r.headers['X-Content-Type-Options'], 'nosniff')


if __name__ == '__main__':
    unittest.main()
