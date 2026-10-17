"""Cache reuse must preserve isolation, refuse corrupt snapshots and changed toolchains, and survive source changes."""
import sys
from pathlib import Path
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parent))
import playtest_cache as cache


class ImportCacheTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.project = self.root / "source"
        for name in ("Packages/manifest.json", "ProjectSettings/ProjectVersion.txt", "Library/Artifacts/one"):
            path = self.project / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(name)
        self.local = {"clip.fbx": "a" * 64}
        self.inputs = cache.compatibility(self.project, "6000.0.78f1")
        self.store = cache.ImportCache(self.root / "cache")
        self.store.store(self.project, self.inputs, "a" * 40, self.local)
        self.destination = self.root / "fresh"
        self.destination.mkdir()

    def test_source_revision_change_reuses_independent_files(self):
        observed = self.store.restore(self.destination, self.inputs, self.local)
        self.assertEqual(observed["status"], "hit")
        copied = self.destination / "Library/Artifacts/one"
        copied.write_text("modified by another isolated Editor")
        self.assertEqual((self.project / "Library/Artifacts/one").read_text(), "Library/Artifacts/one")
        again = self.root / "again"
        again.mkdir()
        self.assertEqual(self.store.restore(again, self.inputs, self.local)["status"], "hit")
        self.assertEqual((again / "Library/Artifacts/one").read_text(), "Library/Artifacts/one")

    def test_changed_local_files_reuse_the_snapshot_and_are_listed(self):
        self.store.store(self.project, self.inputs, "b" * 40, {"clip.fbx": "a" * 64, "old.png": "b" * 64})
        observed = self.store.restore(self.destination, self.inputs, {"clip.fbx": "c" * 64, "new.png": "d" * 64})
        self.assertEqual(observed["status"], "hit")
        self.assertEqual(observed["localChanges"], {"added": {"count": 1, "paths": ["new.png"]},
                                                    "removed": {"count": 1, "paths": ["old.png"]},
                                                    "changed": {"count": 1, "paths": ["clip.fbx"]}})
        unchanged = self.root / "unchanged"
        unchanged.mkdir()
        same = self.store.restore(unchanged, self.inputs, {"clip.fbx": "a" * 64, "old.png": "b" * 64})
        self.assertEqual([change["count"] for change in same["localChanges"].values()], [0, 0, 0])

    def test_toolchain_target_settings_and_package_changes_miss(self):
        for field in ("schemaVersion", "unityVersion", "target", "host", "architecture", "settingsHash", "packagesHash"):
            with self.subTest(field=field):
                changed = dict(self.inputs, **{field: "different"})
                self.assertEqual(self.store.restore(self.destination, changed, self.local)["status"], "miss")
        (self.project / "ProjectSettings/ProjectVersion.txt").write_text("changed")
        self.assertNotEqual(cache.compatibility(self.project, "6000.0.78f1"), self.inputs)

    def test_corruption_and_additional_files_fall_back_without_partial_library(self):
        entry = self.store.root / cache.fingerprint(self.inputs)
        (entry / "Library/Artifacts/one").write_text("corrupt")
        self.assertEqual(self.store.restore(self.destination, self.inputs, self.local)["status"], "rejected")
        self.assertFalse((self.destination / "Library").exists())

    def test_live_destination_and_symlinked_snapshot_are_refused(self):
        (self.destination / "Library").mkdir()
        with self.assertRaises(ValueError):
            self.store.restore(self.destination, self.inputs, self.local)
        outside = self.root / "outside"
        outside.write_text("private")
        (self.project / "Library/linked").symlink_to(outside)
        with self.assertRaises(ValueError):
            self.store.store(self.project, self.inputs, "b" * 40, self.local)

    def test_snapshot_replacement_and_retention_are_bounded(self):
        (self.project / "Library/Artifacts/one").write_text("new settled bytes")
        self.store.store(self.project, self.inputs, "b" * 40, self.local)
        self.assertEqual(self.store.restore(self.destination, self.inputs, self.local)["snapshotSourceRevision"], "b" * 40)
        for version in ("6000.0.79f1", "6000.0.80f1"):
            self.store.store(self.project, dict(self.inputs, unityVersion=version), "c" * 40, self.local)
        self.assertEqual(len([p for p in self.store.root.iterdir() if len(p.name) == 64]), 2)

    def test_editor_connection_and_cook_status_are_not_reused(self):
        for name in ("Pipeline/connection", "SlopArena/CharacterCook/status.json"):
            path = self.project / "Library" / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("session state")
        self.store.store(self.project, self.inputs, "a" * 40, self.local)
        self.store.restore(self.destination, self.inputs, self.local)
        self.assertFalse((self.destination / "Library/Pipeline").exists())
        self.assertFalse((self.destination / "Library/SlopArena").exists())
        self.assertTrue((self.destination / "Library/Artifacts/one").exists())
