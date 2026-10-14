"""Time evidence survives polling/retry and retains failures without invented history."""
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parent))
import playtest_timing as timing


class TimingTests(unittest.TestCase):
    def setUp(self):
        self.clock = 100.0
        patch = mock.patch.object(timing.time, "monotonic", side_effect=lambda: self.clock)
        patch.start()
        self.addCleanup(patch.stop)

    def test_polling_and_resume_keep_the_original_start(self):
        step = {}
        timing.transition(step, "running", "2026-10-14T10:00:00+00:00")
        # Resuming from persisted JSON must use the original monotonic start.
        step = json.loads(json.dumps(step))
        self.clock += 60
        timing.transition(step, "in_progress", "2026-10-14T10:01:00+00:00", 1)
        self.clock += 60
        timing.transition(step, "in_progress", "2026-10-14T10:02:00+00:00", 1)
        self.clock += 60
        timing.transition(step, "completed", "2026-10-14T10:03:00+00:00", 1)
        self.clock += 60
        timing.transition(step, "completed", "2026-10-14T10:04:00+00:00", 1)
        self.assertEqual(len(step["timingAttempts"]), 1)
        self.assertEqual(timing.summary(step), "3m 00s")

    def test_retry_has_separate_duration_and_first_finished_observation_is_unknown(self):
        step = {}
        timing.transition(step, "completed", "2026-10-14T10:00:00+00:00", 1)
        self.assertEqual(timing.summary(step), "")
        timing.transition(step, "running", "2026-10-14T10:01:00+00:00", 1)
        self.clock += 60
        timing.transition(step, "failed", "2026-10-14T10:02:00+00:00", 1)
        self.clock += 60
        timing.transition(step, "retry-requested", "2026-10-14T10:03:00+00:00", 1)
        self.clock += 60
        timing.transition(step, "in_progress", "2026-10-14T10:04:00+00:00", 2)
        self.clock += 60
        timing.transition(step, "completed", "2026-10-14T10:05:00+00:00", 2)
        self.assertEqual(len(step["timingAttempts"]), 2)
        self.assertEqual(step["timingAttempts"][0]["durationSeconds"], 60)
        self.assertEqual(step["timingAttempts"][1]["attempt"], 2)
        self.assertEqual(timing.summary(step), "2m 00s (attempt 2)")

    def test_wall_clock_adjustments_do_not_change_live_or_finished_duration(self):
        for observed in ("2026-10-14T09:00:00+00:00", "2026-10-14T11:00:00+00:00"):
            with self.subTest(observed=observed):
                step = {}
                timing.transition(step, "running", "2026-10-14T10:00:00+00:00", 1)
                self.clock += 65
                self.assertEqual(timing.summary(step), "1m 05s elapsed")
                timing.transition(step, "completed", observed, 1)
                self.assertEqual(timing.summary(step), "1m 05s")
                self.assertEqual(step["timingAttempts"][0]["finishedAtUtc"], observed)

    def test_reboot_or_old_start_does_not_invent_a_duration(self):
        for clock_id in ("another-boot", None):
            with self.subTest(clock_id=clock_id):
                step = {}
                timing.transition(step, "running", "2026-10-14T10:00:00+00:00", 1)
                step = json.loads(json.dumps(step))
                row = step["timingAttempts"][0]
                row["clockId"] = clock_id
                self.assertEqual(timing.summary(step), "duration unavailable")
                timing.transition(step, "completed", "2026-10-14T10:03:00+00:00", 1)
                self.assertNotIn("durationSeconds", row)
                self.assertEqual(timing.summary(step), "duration unavailable")
                row["durationSeconds"] = 180
                self.assertEqual(timing.summary(step), "3m 00s")

    def test_failed_substage_is_durable_and_uses_monotonic_duration(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "timings.json"
            timeline = timing.Timeline(path)
            with mock.patch.object(timing.time, "monotonic", side_effect=[100, 102.5]):
                with self.assertRaises(RuntimeError):
                    with timeline.stage("unity"):
                        self.assertEqual(json.loads(path.read_text())["phases"][0]["status"], "running")
                        raise RuntimeError("build failed")
            row = json.loads(path.read_text())["phases"][0]
            self.assertEqual(row["status"], "failed")
            self.assertEqual(row["durationSeconds"], 2.5)

