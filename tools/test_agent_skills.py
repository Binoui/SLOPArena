from __future__ import annotations

import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

import sync_agent_skills as adapters


class AgentSkillTests(unittest.TestCase):
    def make_repo(self) -> Path:
        temporary = tempfile.TemporaryDirectory(prefix="skill checkout ")
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name) / "game"
        root.mkdir()
        (root / ".gitignore").write_text("private/\n", encoding="utf-8")
        (root / "AGENTS.md").write_text("Project policy.\n", encoding="utf-8")
        self.skill(root, "example")
        return root

    @staticmethod
    def skill(root: Path, name: str, explicit: bool = False) -> Path:
        source = root / "skills" / name / "SKILL.md"
        source.parent.mkdir(parents=True)
        source.write_text(
            f'---\nname: {name}\ndescription: "Use for {name} work."\n---\n\nMaintained workflow.\n',
            encoding="utf-8",
        )
        if explicit:
            policy = source.parent / "agents/openai.yaml"
            policy.parent.mkdir()
            policy.write_text("policy:\n  allow_implicit_invocation: false\n", encoding="utf-8")
        return source

    def test_relocated_checkout_has_no_adapter_drift_and_keeps_vendor_ignored(self) -> None:
        root = self.make_repo()
        self.assertTrue(adapters.sync(root, write=True))
        self.assertEqual(adapters.sync(root), [])
        relocated = root.with_name("relocated clone")
        root.rename(relocated)
        self.assertEqual(adapters.sync(relocated), [])
        subprocess.run(["git", "init", "-q"], cwd=relocated, check=True)
        for path, ignored in [
            (".agents/skills/example/SKILL.md", False),
            (".agents/skills/example/session.json", True),
            (".agents/skills/vendor/SKILL.md", True),
            (".agents/local-config.json", True),
        ]:
            result = subprocess.run(["git", "check-ignore", "-q", path], cwd=relocated)
            self.assertEqual(result.returncode, 0 if ignored else 1, path)

    def test_description_and_explicit_policy_reach_both_harnesses(self) -> None:
        root = self.make_repo()
        source = self.skill(root, "manual", explicit=True)
        adapters.sync(root, write=True)
        for harness in (".agents", ".omp"):
            wrapper = root / harness / "skills/manual/SKILL.md"
            self.assertIn('description: "Use for manual work."', wrapper.read_text())
            self.assertIn("../../../skills/manual/SKILL.md", wrapper.read_text())
            self.assertNotIn("Maintained workflow.", wrapper.read_text())
        self.assertIn("disable-model-invocation: true", (root / ".omp/skills/manual/SKILL.md").read_text())
        self.assertEqual((root / ".agents/skills/manual/agents/openai.yaml").read_text(),
                         (source.parent / "agents/openai.yaml").read_text())
        source.write_text(source.read_text().replace("manual work.", "manual review."))
        before = (root / ".agents/skills/manual/SKILL.md").read_text()
        drift = adapters.sync(root)
        self.assertIn(".agents/skills/manual/SKILL.md", drift)
        self.assertEqual((root / ".agents/skills/manual/SKILL.md").read_text(), before)
        adapters.sync(root, write=True)
        self.assertEqual(adapters.sync(root), [])

    def test_unmanaged_adapter_is_preserved_before_any_write(self) -> None:
        root = self.make_repo()
        target = root / ".omp/skills/example/SKILL.md"
        target.parent.mkdir(parents=True)
        target.write_text("Operator-owned skill.\n")
        with self.assertRaisesRegex(ValueError, "unmanaged adapter"):
            adapters.sync(root, write=True)
        self.assertEqual(target.read_text(), "Operator-owned skill.\n")
        self.assertFalse((root / ".agents/skills/example/SKILL.md").exists())
        self.assertEqual((root / ".gitignore").read_text(), "private/\n")

    def test_removed_skill_retires_only_generated_adapters(self) -> None:
        root = self.make_repo()
        source = self.skill(root, "retired", explicit=True)
        adapters.sync(root, write=True)
        vendor = root / ".agents/skills/vendor/SKILL.md"
        vendor.parent.mkdir()
        vendor.write_text("Operator-owned skill.\n")
        shutil.rmtree(source.parent)
        self.assertIn("obsolete: .agents/skills/retired/SKILL.md", adapters.sync(root))
        adapters.sync(root, write=True)
        self.assertFalse((root / ".omp/skills/retired/SKILL.md").exists())
        self.assertFalse((root / ".agents/skills/retired/agents/openai.yaml").exists())
        self.assertEqual(vendor.read_text(), "Operator-owned skill.\n")
        self.assertEqual(adapters.sync(root), [])

    def test_mismatched_names_fail_before_generation(self) -> None:
        root = self.make_repo()
        source = root / "skills/example/SKILL.md"
        source.write_text(source.read_text().replace("name: example", "name: other"))
        with self.assertRaisesRegex(ValueError, "name must match"):
            adapters.sync(root, write=True)
        self.assertFalse((root / ".agents").exists())

    def test_removing_explicit_policy_does_not_leave_a_stale_native_policy(self) -> None:
        root = self.make_repo()
        source = self.skill(root, "manual", explicit=True)
        adapters.sync(root, write=True)
        (source.parent / "agents/openai.yaml").unlink()
        self.assertIn("obsolete: .agents/skills/manual/agents/openai.yaml", adapters.sync(root))
        adapters.sync(root, write=True)
        self.assertFalse((root / ".agents/skills/manual/agents/openai.yaml").exists())
        self.assertNotIn("disable-model-invocation", (root / ".omp/skills/manual/SKILL.md").read_text())
        self.assertEqual(adapters.sync(root), [])


if __name__ == "__main__":
    unittest.main()
