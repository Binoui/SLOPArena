import importlib.util
import json
from pathlib import Path
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import tempfile
import threading
import unittest
from unittest.mock import patch
import uuid

spec = importlib.util.spec_from_file_location("replay_pull", Path(__file__).parents[1] / "replay_pull.py")
puller = importlib.util.module_from_spec(spec)
spec.loader.exec_module(puller)


class PullTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.dest = Path(self.temp.name)
        self.match = str(uuid.uuid4())
        self.body = puller.MAGIC + b"test"
        self.complete = False
        self.calls = []
        self.truncate = False
        owner = self

        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *args):
                pass

            def do_GET(self):
                owner.assertEqual("secret", self.headers["X-Console-Key"])
                if self.path == "/api/replays":
                    body = json.dumps({"replays": [{"matchId": owner.match, "bytes": len(owner.body),
                                                   "complete": owner.complete}]}).encode()
                else:
                    owner.assertEqual(f"/api/matches/{owner.match}/replay", self.path)
                    owner.calls.append(self.path)
                    body = owner.body
                self.send_response(200)
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body[:-2] if owner.truncate and self.path != "/api/replays" else body)

        self.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.thread = threading.Thread(target=self.server.serve_forever)
        self.thread.start()
        self.url = f"http://127.0.0.1:{self.server.server_port}"

    def tearDown(self):
        self.server.shutdown()
        self.thread.join()
        self.server.server_close()
        self.temp.cleanup()

    def run_pull(self):
        return puller.pull(self.url, "secret", self.dest)

    def test_new_then_present_then_complete_same_size(self):
        self.assertEqual(0, self.run_pull())
        part = self.dest / (self.match + ".slopreplay.part")
        self.assertEqual(self.body, part.read_bytes())
        self.assertEqual(0, self.run_pull())
        self.assertEqual(1, len(self.calls))
        self.complete = True
        self.assertEqual(0, self.run_pull())
        self.assertEqual(2, len(self.calls))
        self.assertFalse(part.exists())
        self.assertEqual(self.body, (self.dest / (self.match + ".slopreplay")).read_bytes())

    def test_bad_magic_leaves_no_file(self):
        self.body = b"badmagic123"
        self.assertEqual(1, self.run_pull())
        self.assertEqual([], list(self.dest.iterdir()))

    def test_truncated_body_preserves_old_file(self):
        final = self.dest / (self.match + ".slopreplay.part")
        final.write_bytes(b"old")
        self.truncate = True
        self.assertEqual(1, self.run_pull())
        self.assertEqual(b"old", final.read_bytes())
        self.assertEqual([final], list(self.dest.iterdir()))

    def test_atomic_replace_keeps_old_file_until_validated(self):
        final = self.dest / (self.match + ".slopreplay.part")
        final.write_bytes(b"old")
        original = puller.os.replace

        def replace(source, target):
            self.assertEqual(b"old", final.read_bytes())
            self.assertEqual(self.dest, Path(source).parent)
            self.assertEqual(self.body, Path(source).read_bytes())
            original(source, target)

        with patch.object(puller.os, "replace", side_effect=replace) as renamed:
            self.assertEqual(0, self.run_pull())
            renamed.assert_called_once()
        self.assertEqual([final], list(self.dest.iterdir()))

    def test_failed_replace_preserves_old_and_removes_temp(self):
        final = self.dest / (self.match + ".slopreplay.part")
        final.write_bytes(b"old")
        with patch.object(puller.os, "replace", side_effect=OSError("interrupted")):
            self.assertEqual(1, self.run_pull())
        self.assertEqual(b"old", final.read_bytes())
        self.assertEqual([final], list(self.dest.iterdir()))


if __name__ == "__main__":
    unittest.main()
