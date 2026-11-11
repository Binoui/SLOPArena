"""Stage presentations: the committed JSON is what the generator writes, and a look is well formed."""
from __future__ import annotations

import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
BLENDER = ROOT / "tools" / "blender"
sys.path.insert(0, str(BLENDER))

from stagekit import presentation  # noqa: E402


class CommittedPresentationTests(unittest.TestCase):
    def test_committed_json_is_what_the_generator_writes(self) -> None:
        stages = sorted(path.parent.name for path in BLENDER.glob("*/presentation.py") if path.parent.name != "stagekit")
        self.assertGreater(len(stages), 0)
        for stage in stages:
            with self.subTest(stage=stage), tempfile.TemporaryDirectory() as out:
                # A process per stage: every generator has its own `palette` and `layout` modules.
                run = subprocess.run([sys.executable, str(BLENDER / "stagekit" / "presentation.py"), stage, "--out", out],
                                     capture_output=True, text=True)
                if "No module named 'numpy'" in run.stderr:
                    self.skipTest(stage + " reads its layout with numpy")
                self.assertEqual(run.returncode, 0, run.stderr)
                written = Path(run.stdout.strip())
                document = json.loads(written.read_text())
                committed = ROOT / "client" / "Unity" / document["folder"] / written.name
                self.assertTrue(committed.is_file(), "no committed " + written.name)
                self.assertEqual(json.loads(committed.read_text()), document,
                                 "rewrite it with stagekit/presentation.py and run sloparena.stage.art-apply")


class LookFileTests(unittest.TestCase):
    """A stage without a generator keeps its look in <stage>.look.json, applied by sloparena.stage.look-apply."""

    def test_look_files_are_well_formed_and_never_shadow_a_presentation(self) -> None:
        stages = ROOT / "client" / "Unity" / "Assets" / "Stages"
        files = sorted(stages.glob("*/*.look.json"))
        self.assertGreater(len(files), 0)
        for path in files:
            with self.subTest(stage=path.parent.name):
                self.assertEqual(path.name, path.parent.name + ".look.json")
                presentation.validate_look(json.loads(path.read_text()))
                self.assertEqual(list(path.parent.glob("Presentation/**/*.presentation.json")), [],
                                 "a generated stage keeps its look in presentation.py")


class LookValidationTests(unittest.TestCase):
    def test_a_stage_without_a_look_is_valid(self) -> None:
        presentation.validate_look(None)

    def test_parts_are_optional(self) -> None:
        presentation.validate_look({"grade": {"tonemap": "neutral"}, "tints": {"City *": [0.5, 0.5, 0.6]}})

    def test_unknown_part_is_rejected(self) -> None:
        with self.assertRaisesRegex(ValueError, "unknown part glow"):
            presentation.validate_look({"glow": {}})

    def test_a_tint_needs_a_name_and_a_colour(self) -> None:
        for tints in ({"*": [1, 1, 1]}, {"Sky": [1, 1]}):
            with self.subTest(tints=tints), self.assertRaises(ValueError):
                presentation.validate_look({"tints": tints})

    def test_kicks_come_in_pairs(self) -> None:
        with self.assertRaisesRegex(ValueError, "both kickA and kickB"):
            presentation.validate_look({"fighters": {"kickA": {"color": [1, 1, 1], "direction": [0, 1, 0]}}})

    def test_at_most_two_pools(self) -> None:
        with self.assertRaisesRegex(ValueError, "at most two pools"):
            presentation.validate_look({"pools": [{}, {}, {}]})


if __name__ == "__main__":
    unittest.main()
