import json
import tempfile
import unittest
from pathlib import Path

from check_unity_setup import CHARACTERS, PACKAGES, check


class UnitySetupTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.project = self.root / "client/Unity"
        for name, version in PACKAGES.items():
            self.write(f"Packages/{name}/package.json", json.dumps({"name": name, "version": version}))
        self.write("Assets/Plugins/SlopArena.Shared/SlopArena.Shared.dll", "fixture")
        self.write("Assets/Plugins/NuGet/.nuget-installed.json", '{"packages":{"Fixture":{"dlls":["Fixture.dll"]}}}')
        self.write("Assets/Plugins/NuGet/Fixture.dll", "fixture")
        self.write("Assets/Plugins/NuGet/CHECKSUMS", hashlib.sha256(b"fixture").hexdigest() + "  Fixture.dll\n")
        self.guid = "1234567890abcdef1234567890abcdef"
        self.write("Assets/Local/rig.fbx", "fixture")
        self.write("Assets/Local/rig.fbx.meta", "guid: " + self.guid)
        for name in CHARACTERS:
            self.write(f"Assets/CharacterPackages/{name}/CharacterAssetCatalog.asset", "_rig: {fileID: 42, guid: " + self.guid + ", type: 3}")

    def write(self, path, text):
        file = self.project / path
        file.parent.mkdir(parents=True, exist_ok=True)
        file.write_text(text)

    def test_provisioned_references_pass_without_changing_inputs(self):
        before = {p: p.read_bytes() for p in self.project.rglob("*") if p.is_file()}
        result = check(self.root)
        self.assertTrue(result["success"], result["problems"])
        self.assertEqual(before, {p: p.read_bytes() for p in self.project.rglob("*") if p.is_file()})
        self.assertFalse(result["gatewayOwnershipAvailable"])

    def test_meta_without_asset_is_missing(self):
        (self.project / "Assets/Local/rig.fbx").unlink()
        result = check(self.root)
        self.assertFalse(result["success"])
        self.assertTrue(any("missing asset or metadata" in message for message in result["problems"]))

    def test_duplicate_guid_is_ambiguous(self):
        self.write("Assets/Other/rig.fbx", "other fixture")
        self.write("Assets/Other/rig.fbx.meta", "guid: " + self.guid)
        self.assertTrue(any("ambiguous GUID" in message for message in check(self.root)["problems"]))

    def test_other_package_version_is_not_certified(self):
        self.write("Packages/com.kybernetik.animancer/package.json", '{"name":"com.kybernetik.animancer","version":"8.3.0"}')
        self.assertFalse(check(self.root)["success"])

    def test_retired_nuget_record_is_detected(self):
        (self.project / "Assets/Plugins/NuGet/Fixture.dll").unlink()
        self.assertTrue(any("missing DLL" in message for message in check(self.root)["problems"]))

    def test_changed_managed_dependency_is_detected(self):
        self.write("Assets/Plugins/NuGet/Fixture.dll", "unexpected replacement")
        self.assertTrue(any("checksum mismatch" in message for message in check(self.root)["problems"]))

    def test_foreign_shared_dependency_requires_migration(self):
        self.write("Assets/Plugins/SlopArena.Shared/Fixture.dll", "fixture")
        self.assertTrue(any("unexpected dependency copy" in message for message in check(self.root)["problems"]))

    def test_unrecorded_nuget_dependency_is_detected(self):
        self.write("Assets/Plugins/NuGet/Other.dll", "fixture")
        self.assertTrue(any("folder does not match" in message for message in check(self.root)["problems"]))

    def test_incomplete_checksum_inventory_is_detected(self):
        self.write("Assets/Plugins/NuGet/CHECKSUMS", "")
        self.assertTrue(any("checksum list does not match" in message for message in check(self.root)["problems"]))

    def test_built_in_guid_needs_no_local_asset(self):
        for name in CHARACTERS:
            self.write(f"Assets/CharacterPackages/{name}/CharacterAssetCatalog.asset", "guid: 0000000000000000e000000000000000")
        self.assertTrue(check(self.root)["success"])

    def test_empty_catalog_is_not_reported_ready(self):
        self.write("Assets/CharacterPackages/fightguy/CharacterAssetCatalog.asset", "")
        self.assertFalse(check(self.root)["success"])

    def test_invalid_manifest_is_a_reported_problem(self):
        self.write("Packages/com.kybernetik.animancer/package.json", "[]")
        result = check(self.root)
        self.assertFalse(result["success"])
        self.assertIsNone(result["packages"]["com.kybernetik.animancer"])


if __name__ == "__main__":
    unittest.main()
import hashlib
