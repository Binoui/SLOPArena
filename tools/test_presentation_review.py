from __future__ import annotations

import contextlib
import io
import json
import tempfile
import unittest
from pathlib import Path

import presentation_review as review


class PresentationReviewTests(unittest.TestCase):
    def test_frames_accept_lists_and_stepped_ranges(self) -> None:
        self.assertEqual(review.parse_frames("10-16:2,20"), [10, 12, 14, 16, 20])

    def test_batch_captures_each_move_with_defaults_and_overrides(self) -> None:
        batch = review.build_batch("manki", "fightguy", "after_hours", "tmp/vfx/manki/before/", ["1", "A"],
                                   ["A:distance=6", "1:ticks=60", "A:hold=50"], "high:1.6", "1920x1080", True)
        prepare, play, first, second, logs = batch["commands"]
        self.assertEqual(prepare, ["sloparena.hitfeel.prepare", "--stage", "after_hours"])
        self.assertEqual(play, ["editor_play"])
        self.assertEqual(first[first.index("--output") + 1], "tmp/vfx/manki/before/1")
        self.assertEqual(first[first.index("--ticks") + 1], "60")
        self.assertEqual(second[second.index("--distance") + 1], "6")
        self.assertEqual(second[second.index("--width") + 1], "1920")
        self.assertEqual(second[-4:], ["--track", "true", "--hold", "50"])
        self.assertNotIn("--hold", first)
        self.assertEqual(logs[0], "get_console_logs")
        self.assertEqual(batch["restore"], [["sloparena.hitfeel.stop"], ["sloparena.hitfeel.restore"]])

    def test_batch_rejects_overrides_for_uncaptured_moves(self) -> None:
        with self.assertRaises(SystemExit):
            review.build_batch("wibou", "manki", "splash_deck", "tmp/x", ["1"], ["F:ticks=9"],
                               "high:1.6", "960x540", False)

    def test_summary_numbers_hits_by_frame_and_fails_on_console_errors(self) -> None:
        capture = {"command": "command sloparena.hitfeel.capture", "success": True, "data": {"result": {
            "output": "tmp/x/1", "runs": [{"preset": "high", "result": {
                "frames": 51, "pressTick": 40, "failure": None,
                "hits": [{"tick": 58, "slot": 3, "damage": 4, "tier": "Light", "blocked": False}]}}]}}}
        clean = {"command": "command get_console_logs", "success": True,
                 "data": {"parameters": {"severity": "error"}, "result": {"total": 0, "logs": []}}}
        stream = json.dumps(capture) + "\n" + json.dumps(clean) + '\n{"success": true, "state": "free"}\n'
        lines, ok = review.summarize(review.read_stream(stream))
        self.assertTrue(ok)
        self.assertIn("frame 18 slot 3 4% Light", lines[0])
        clean["data"]["result"] = {"total": 1, "logs": [{"type": "Error", "message": "Boom"}]}
        _, ok = review.summarize(review.read_stream(json.dumps(clean)))
        self.assertFalse(ok)

    def test_clip_command_stacks_labelled_sets_and_slows_playback(self) -> None:
        sets = [("TODAY", Path("before/1/high/close")), ("NEW", Path("after/1/high/close"))]
        command = review.clip_command(sets, 28, 48, 4, 15, (560, 315), 50, Path("page/1-quarter.webp"))
        graph = command[command.index("-filter_complex") + 1]
        self.assertIn("text='TODAY'", graph)
        self.assertIn("hstack=inputs=2,setpts=4*PTS,fps=15", graph)
        self.assertEqual(command.count("-start_number"), 2)
        single = review.clip_command(sets[:1], 0, 40, 1, 30, (560, 315), 50, Path("page/1-real.webp"))
        self.assertIn("[v0]null,setpts=1*PTS,fps=30", single[single.index("-filter_complex") + 1])

    def test_page_links_clips_relatively_and_escapes_text(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            page = Path(directory)
            spec = {"title": "Review", "lede": "A & B", "sections": [
                {"clip": "g1", "title": "Quick <Slash>", "note": "note"},
                {"heading": "Dark stage"},
                {"image": "options.jpg", "title": "Options"}]}
            with contextlib.redirect_stdout(io.StringIO()):
                out = review.write_page(page, spec, absolute=False)
            document = out.read_text(encoding="utf-8")
            self.assertIn('"quarter": "g1-quarter.webp"', document)
            self.assertIn('src="options.jpg"', document)
            self.assertIn("Quick &lt;Slash&gt;", document)
            self.assertIn("A &amp; B", document)


if __name__ == "__main__":
    unittest.main()
