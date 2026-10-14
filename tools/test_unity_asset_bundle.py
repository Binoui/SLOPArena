import json
import subprocess
import tempfile
import unittest
import zipfile
from pathlib import Path

from unity_asset_bundle import ANCHORS, collect, export_bundle, import_bundle


class AssetBundleTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="sloparena assets ")
        self.addCleanup(self.temp.cleanup)
        self.base = Path(self.temp.name)
        self.source = self.base / "source"
        self.source.mkdir()
        self.git(self.source, "init", "-q")
        self.write("src/Shared/SlopArena.Shared.csproj", "fixture")
        self.write("tests/Shared.Tests/Shared.Tests.csproj", "fixture")
        self.write(".gitignore", "*.fbx\n*.anim\n*.unitypackage\nclient/Unity/Assets/Local/\n"
                   "client/Unity/Packages/com.kybernetik.animancer/\nclient/Unity/Assets/Generated/\n")
        for name in ANCHORS:
            self.write(name, "fixture\n")
        for character in ("fightguy", "manki", "wibou", "bonk"):
            for name in ("CharacterAssetCatalog.asset", "character.json", "package.json"):
                self.write(f"client/Unity/Assets/CharacterPackages/{character}/{name}", "fixture\n")
        self.write("client/Unity/Assets/CharacterPackages/fightguy/run.anim.meta", "guid: tracked\n")
        self.git(self.source, "add", ".")
        self.git(self.source, "-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid",
                 "commit", "-qm", "fixture")
        self.write("client/Unity/Packages/com.kybernetik.animancer/package.json", "{}")
        self.write("client/Unity/Assets/Local/model.fbx", "local model")
        self.write("client/Unity/Assets/Local/model.fbx.meta", "guid: local\n")
        self.write("client/Unity/Assets/Local.meta", "guid: folder\n")
        self.write("client/Unity/Assets/CharacterPackages/fightguy/run.anim", "adapted animation")
        self.write("client/Unity/Assets/CharacterPackages/fightguy/new.anim", "new adapted animation")
        self.write("client/Unity/Assets/CharacterPackages/fightguy/new.anim.meta", "guid: untracked companion\n")
        self.write("client/Unity/Assets/Local/installer.unitypackage", "installer")
        self.write("client/Unity/Assets/Local/installer.unitypackage.meta", "installer metadata")
        self.write("client/Unity/Assets/Generated/cache.asset", "generated")
        self.write("client/Unity/Assets/Local/orphan.fbx.meta", "orphan")
        self.target = self.base / "target"
        self.git(self.base, "clone", "-q", str(self.source), str(self.target))
        self.archive = self.base / "transfer" / "assets.zip"

    def git(self, root, *args):
        return subprocess.check_output(["git", "-C", str(root), *args], stderr=subprocess.PIPE)

    def write(self, name, text):
        path = self.source / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")

    def test_clone_roundtrip_preserves_guid_metadata_and_is_repeatable(self):
        manifest = export_bundle(self.source, self.archive)
        import_bundle(self.target, self.archive, dry_run=True)
        self.assertFalse((self.target / "client/Unity/Assets/Local/model.fbx").exists())
        import_bundle(self.target, self.archive)
        import_bundle(self.target, self.archive)
        for name in manifest["files"]:
            self.assertEqual((self.source / name).read_bytes(), (self.target / name).read_bytes())
        self.assertTrue((self.target / "client/Unity/Assets/Local").is_dir())
        self.assertTrue((self.target / "client/Unity/Assets/CharacterPackages/fightguy/new.anim.meta").exists())
        self.assertFalse((self.target / "client/Unity/Assets/Generated").exists())
        self.assertFalse((self.target / "client/Unity/Assets/Local/installer.unitypackage").exists())
        self.assertFalse((self.target / "client/Unity/Assets/Local/orphan.fbx.meta").exists())

    def test_conflicting_local_asset_stops_before_any_import(self):
        export_bundle(self.source, self.archive)
        conflict = self.target / "client/Unity/Assets/Local/model.fbx"
        conflict.parent.mkdir(parents=True)
        conflict.write_text("laptop edit")
        with self.assertRaisesRegex(ValueError, "Existing local asset differs"):
            import_bundle(self.target, self.archive)
        self.assertEqual(conflict.read_text(), "laptop edit")
        self.assertFalse((self.target / "client/Unity/Packages/com.kybernetik.animancer").exists())

    def test_tracked_metadata_mismatch_stops_before_import(self):
        export_bundle(self.source, self.archive)
        metadata = self.target / "client/Unity/Assets/CharacterPackages/fightguy/run.anim.meta"
        metadata.write_text("guid: laptop edit\n")
        with self.assertRaisesRegex(ValueError, "Tracked input differs"):
            import_bundle(self.target, self.archive)
        self.assertEqual(metadata.read_text(), "guid: laptop edit\n")
        self.assertFalse((self.target / "client/Unity/Assets/Local").exists())

    def test_line_ending_change_in_tracked_metadata_is_compatible(self):
        export_bundle(self.source, self.archive)
        metadata = self.target / "client/Unity/Assets/CharacterPackages/fightguy/run.anim.meta"
        metadata.write_bytes(metadata.read_bytes().replace(b"\n", b"\r\n"))
        import_bundle(self.target, self.archive, dry_run=True)

    def test_symlinked_destination_is_rejected_without_touching_external_files(self):
        export_bundle(self.source, self.archive)
        external = self.base / "external"
        external.mkdir()
        (self.target / "client/Unity/Assets/Local").symlink_to(external, target_is_directory=True)
        with self.assertRaisesRegex(ValueError, "Symlink"):
            import_bundle(self.target, self.archive)
        self.assertEqual(list(external.iterdir()), [])

    def test_corrupt_asset_is_detected_before_any_import(self):
        export_bundle(self.source, self.archive)
        corrupt = self.base / "corrupt.zip"
        with zipfile.ZipFile(self.archive) as source, zipfile.ZipFile(corrupt, "w") as target:
            for entry in source.infolist():
                data = source.read(entry)
                if entry.filename.endswith("model.fbx"):
                    data = b"x" * len(data)
                target.writestr(entry, data)
        with self.assertRaisesRegex(ValueError, "checksum mismatch"):
            import_bundle(self.target, corrupt)
        self.assertFalse((self.target / "client/Unity/Assets/Local").exists())

    def test_archive_traversal_is_rejected(self):
        manifest = export_bundle(self.source, self.archive)
        name, record = next(iter(manifest["files"].items()))
        manifest["files"]["client/Unity/Assets/../../../outside.fbx"] = manifest["files"].pop(name)
        malicious = self.base / "malicious.zip"
        with zipfile.ZipFile(self.archive) as source, zipfile.ZipFile(malicious, "w") as target:
            for entry in source.infolist():
                if entry.filename == "asset-manifest.json":
                    target.writestr(entry, json.dumps(manifest))
                elif entry.filename == name:
                    target.writestr("client/Unity/Assets/../../../outside.fbx", source.read(entry))
                else:
                    target.writestr(entry, source.read(entry))
        with self.assertRaisesRegex(ValueError, "Invalid asset path"):
            import_bundle(self.target, malicious)
        self.assertFalse((self.target / "client/Unity/Assets/Local").exists())


if __name__ == "__main__":
    unittest.main()
