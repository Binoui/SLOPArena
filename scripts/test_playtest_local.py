"""Local producer input snapshots must survive clean-checkout and cache differences."""
import importlib.util
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest import mock

SPEC = importlib.util.spec_from_file_location("local_producer_tests", Path(__file__).with_name("build-playtest-local.py"))
producer = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(producer)


class LicensedInputTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.project = self.root / "client/Unity"
        self.paths = ["client/Unity/Packages/com.kybernetik.animancer/package.json",
                      "client/Unity/Assets/Art/clip.fbx", "client/Unity/Assets/Art/clip.fbx.meta",
                      "client/Unity/Assets/Generated/cache.asset"]
        for name in self.paths:
            path = self.root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(name)
        self.capture = mock.patch.object(producer, "run_capture", side_effect=[
            str(self.root), "\0".join(self.paths), str(self.root), "\0".join(self.paths)])
        self.capture.start()
        self.addCleanup(self.capture.stop)

    def test_identical_source_inputs_have_same_fingerprint_and_exclude_generated_catalogs(self):
        _, first = producer.licensed_inputs(self.project)
        _, second = producer.licensed_inputs(self.project)
        self.assertEqual(first, second)
        self.assertNotIn("client/Unity/Assets/Generated/cache.asset", first)

    def test_importer_metadata_change_is_part_of_input_snapshot(self):
        _, first = producer.licensed_inputs(self.project)
        (self.root / self.paths[2]).write_text("changed importer")
        _, second = producer.licensed_inputs(self.project)
        self.assertNotEqual(first[self.paths[2]], second[self.paths[2]])
        self.assertEqual(first[self.paths[1]], second[self.paths[1]])

    def test_source_replacement_or_external_symlink_is_refused(self):
        outside = self.root / "outside.fbx"
        outside.write_text("outside")
        (self.root / self.paths[1]).unlink()
        (self.root / self.paths[1]).symlink_to(outside)
        with self.assertRaisesRegex(producer.BuildError, "outside"):
            producer.licensed_inputs(self.project)

    def test_dirty_or_unpublished_source_fails_before_build(self):
        with mock.patch.object(producer, "run_capture", side_effect=[str(self.root), "a" * 40, " M tracked.cs"]):
            with self.assertRaisesRegex(producer.BuildError, "dirty"):
                producer.check_source(self.root, "a" * 40, "0.2.0-playtest.42")
        with mock.patch.object(producer, "run_capture", side_effect=[
                str(self.root), "a" * 40, "", "b" * 40 + "\trefs/heads/main"]):
            with self.assertRaisesRegex(producer.BuildError, "published"):
                producer.check_source(self.root, "a" * 40, "0.2.0-playtest.42")
