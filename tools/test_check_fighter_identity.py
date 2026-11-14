import importlib.util
from pathlib import Path
import subprocess
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("identity_guard", Path(__file__).with_name("check_fighter_identity.py"))
guard = importlib.util.module_from_spec(spec)
spec.loader.exec_module(guard)


class FighterIdentityGuardTests(unittest.TestCase):
    def test_deleted_index_entry_and_live_violation(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            subprocess.run(["git", "init", "-q", directory], check=True)
            missing = root / "Deleted.cs"
            missing.write_text("CharacterClass stale;\n")
            subprocess.run(["git", "add", "Deleted.cs"], cwd=root, check=True)
            missing.unlink()
            self.assertEqual([], guard.violations(root))
            (root / "Probe.cs").write_text("CharacterClass planted;\n")
            self.assertEqual(["Probe.cs:1"], guard.violations(root))


if __name__ == "__main__":
    unittest.main()
