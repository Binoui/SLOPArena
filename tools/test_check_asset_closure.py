import contextlib
import io
import json
import subprocess
import tempfile
import unittest
from pathlib import Path

from check_asset_closure import check, main

MATERIAL = "a" * 32
OWN_TEXTURE = "b" * 32
STAGE_TEXTURE = "c" * 32
VENDOR_MODEL = "d" * 32
REGISTRY_SHADER = "e" * 32
DANGLING = "f" * 32
MODEL_MATERIAL = "1" * 32
BUILTIN = "0000000000000000f000000000000000"
STAGE = "2" * 32


class AssetClosureTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        subprocess.run(["git", "init", "-q", str(self.root)], check=True)
        self.write(".gitignore", "\n".join([
            "client/Unity/Assets/Vendor/",
            "client/Unity/Library/",
            "*.png",
            "!client/Unity/Assets/Art/own.png*",
            "",
        ]))
        self.asset("Stage/stage.prefab", STAGE, references=[MATERIAL, VENDOR_MODEL, REGISTRY_SHADER, DANGLING, BUILTIN])
        self.asset("Stage/stage.mat", MATERIAL, references=[OWN_TEXTURE, STAGE_TEXTURE])
        self.asset("Stage/tex.png", STAGE_TEXTURE)
        self.asset("Art/own.png", OWN_TEXTURE)
        self.asset("Vendor/model.fbx", VENDOR_MODEL, meta_references=[MODEL_MATERIAL])
        self.asset("Vendor/model.mat", MODEL_MATERIAL)
        self.write("client/Unity/Library/PackageCache/com.example.shaders@1/Lit.shader", "Shader {}")
        self.write("client/Unity/Library/PackageCache/com.example.shaders@1/Lit.shader.meta", f"guid: {REGISTRY_SHADER}\n")
        self.git("add", ".gitignore", "client/Unity/Assets/Stage/stage.prefab", "client/Unity/Assets/Stage/stage.prefab.meta")

    def write(self, path, text):
        file = self.root / path
        file.parent.mkdir(parents=True, exist_ok=True)
        file.write_text(text)

    def asset(self, path, guid, references=(), meta_references=()):
        body = "".join(f"  ref: {{fileID: 2100000, guid: {g}, type: 2}}\n" for g in references)
        self.write(f"client/Unity/Assets/{path}", "%YAML 1.1\n" + body)
        meta = f"fileFormatVersion: 2\nguid: {guid}\n"
        meta += "".join(f"  externalObjects:\n  - second: {{fileID: 2100000, guid: {g}, type: 2}}\n" for g in meta_references)
        self.write(f"client/Unity/Assets/{path}.meta", meta)

    def git(self, *args):
        subprocess.run(["git", "-C", str(self.root), *args], check=True, stdout=subprocess.DEVNULL)

    def statuses(self, result):
        return {entry["path"].removeprefix("client/Unity/Assets/"): entry["status"] for entry in result["entries"]}

    def test_classifies_every_reachable_reference(self):
        result = check(self.root, ["client/Unity/Assets/Stage/stage.prefab"])
        statuses = self.statuses(result)
        self.assertFalse(result["success"])
        self.assertEqual(statuses["Stage/stage.prefab"], "tracked")
        self.assertEqual(statuses["Stage/stage.mat"], "to-add")
        self.assertEqual(statuses["Art/own.png"], "to-add", "a ! pattern re-includes the file")
        self.assertEqual(statuses["Stage/tex.png"], "ignored")
        self.assertEqual(statuses["Vendor/model.fbx"], "ignored")
        self.assertEqual(statuses["Vendor/model.mat"], "ignored", ".meta references are followed")
        self.assertEqual(statuses["client/Unity/Library/PackageCache/com.example.shaders@1/Lit.shader"], "registry")
        self.assertEqual(statuses[DANGLING], "dangling")
        self.assertNotIn(BUILTIN, statuses)
        self.assertEqual(result["counts"]["ignored"], 3)

    def test_reports_the_first_referrer(self):
        result = check(self.root, ["client/Unity/Assets/Stage/stage.prefab"])
        texture = next(e for e in result["entries"] if e["path"].endswith("Stage/tex.png"))
        self.assertEqual(texture["referencedBy"], "client/Unity/Assets/Stage/stage.mat")
        self.assertTrue(texture["rule"].endswith(":*.png"))

    def test_folder_targets_pass_when_everything_ships(self):
        self.write(".gitignore", (self.root / ".gitignore").read_text() + "!client/Unity/Assets/Stage/tex.png*\n")
        self.asset("Stage/stage.prefab", STAGE, references=[MATERIAL, BUILTIN])
        self.write("client/Unity/Assets/Stage/notes.txt", "not an asset reference")
        result = check(self.root, ["client/Unity/Assets/Stage"])
        self.assertTrue(result["success"], result["entries"])
        self.assertIn("client/Unity/Assets/Stage/notes.txt", result["roots"])
        self.assertNotIn("client/Unity/Assets/Stage/stage.prefab.meta", result["roots"])

    def test_exit_status_and_json(self):
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            status = main(["--root", str(self.root), "--json", "client/Unity/Assets/Stage/stage.prefab"])
        self.assertEqual(status, 1)
        self.assertFalse(json.loads(output.getvalue())["success"])
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(main(["--root", str(self.root), "client/Unity/Assets/Missing.prefab"]), 2)
            self.assertEqual(main(["--root", str(self.root), "client/Unity/Assets/Art/own.png"]), 0)


if __name__ == "__main__":
    unittest.main()
