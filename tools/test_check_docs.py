from __future__ import annotations

import contextlib
import io
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest import mock

import check_docs


class CheckDocsTests(unittest.TestCase):
    def make_repo(self) -> Path:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        self.git(root, "init")
        for relative in check_docs.CANONICAL_PATHS:
            path = root / relative
            if relative.endswith(".md"):
                path.parent.mkdir(parents=True, exist_ok=True)
                path.touch()
            else:
                path.mkdir(parents=True, exist_ok=True)
        return root

    @staticmethod
    def git(root: Path, *arguments: str) -> None:
        subprocess.run(
            ["git", *arguments],
            cwd=root,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            check=True,
        )

    def run_checker(self, root: Path) -> tuple[int, str]:
        output = io.StringIO()
        with mock.patch.object(check_docs, "ROOT", root), contextlib.redirect_stdout(output):
            result = check_docs.main()
        return result, output.getvalue()

    def test_scans_nonignored_untracked_docs_not_ignored_vendor(self) -> None:
        root = self.make_repo()
        (root / ".gitignore").write_text(".agents/\n", encoding="utf-8")
        (root / "README.md").write_text(
            "[guide](docs/guide.md)\n", encoding="utf-8"
        )
        (root / "docs/guide.md").touch()
        vendor = root / ".agents/vendor/SKILL.md"
        vendor.parent.mkdir(parents=True)
        vendor.write_text("[missing](missing.md)\n", encoding="utf-8")
        self.git(root, "add", ".gitignore", "README.md")

        result, output = self.run_checker(root)
        self.assertEqual(result, 0, output)

        new_doc = root / "docs/new guide.md"
        new_doc.write_text("[missing](missing.md)\n", encoding="utf-8")
        result, output = self.run_checker(root)
        self.assertEqual(result, 1)
        self.assertIn("docs/new guide.md", output)

        new_doc.write_text("[guide](guide.md)\n", encoding="utf-8")
        result, output = self.run_checker(root)
        self.assertEqual(result, 0, output)

    def test_force_tracked_ignored_markdown_is_checked(self) -> None:
        root = self.make_repo()
        (root / ".gitignore").write_text(".agents/\n", encoding="utf-8")
        owned = root / ".agents/owned.md"
        owned.parent.mkdir(parents=True)
        owned.write_text("[missing](missing.md)\n", encoding="utf-8")
        self.git(root, "add", ".gitignore")
        self.git(root, "add", "-f", ".agents/owned.md")

        result, output = self.run_checker(root)
        self.assertEqual(result, 1)
        self.assertIn(".agents/owned.md", output)

    def test_deleted_indexed_markdown_is_ignored(self) -> None:
        root = self.make_repo()
        deleted = root / "docs/deleted.md"
        deleted.write_text("[missing](missing.md)\n", encoding="utf-8")
        self.git(root, "add", "docs/deleted.md")
        deleted.unlink()

        result, output = self.run_checker(root)
        self.assertEqual(result, 0, output)

    def test_non_git_root_reports_enumeration_error(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            result, output = self.run_checker(Path(temporary))

        self.assertEqual(result, 1)
        self.assertIn("unable to enumerate project Markdown:", output)

    def test_quality_guide_accepts_existing_commands_and_paths(self) -> None:
        root = self.make_repo()
        (root / "Makefile").write_text("build check:\n\ttrue\n", encoding="utf-8")
        (root / "tools").mkdir(exist_ok=True)
        (root / "tools/check_docs.py").touch()
        (root / "tools/test_check_docs.py").touch()
        (root / "tests/Shared.Tests").mkdir(parents=True, exist_ok=True)
        workflow = root / ".github/workflows/ci.yml"
        workflow.parent.mkdir(parents=True, exist_ok=True)
        workflow.touch()
        (root / "SlopArena.csproj").touch()
        (root / ".githooks").mkdir()
        (root / ".githooks/pre-commit").touch()
        guide = root / "docs/contributing/quality.md"
        guide.parent.mkdir(parents=True, exist_ok=True)
        guide.write_text(
            "See `.github/workflows/ci.yml`, `SlopArena.csproj`, and `.githooks/pre-commit`.\n"
            "```bash\nmake build\npython3 -m unittest discover -s tools -p 'test_check_docs.py'\n"
            "dotnet test tests/Shared.Tests/ --nologo\npython3 tools/check_docs.py\n```\n",
            encoding="utf-8",
        )
        self.git(root, "add", "docs/contributing/quality.md")

        result, output = self.run_checker(root)
        self.assertEqual(result, 0, output)

    def test_quality_guide_rejects_missing_target_and_paths_with_source_line(self) -> None:
        root = self.make_repo()
        (root / "tools").mkdir()
        guide = root / "docs/contributing/quality.md"
        guide.parent.mkdir(parents=True, exist_ok=True)
        guide.write_text(
            "See `.github/workflows/missing.yml`, `SlopArena.csproj`, and `.githooks/pre-commit`.\n"
            "```bash\nmake missing\npython3 -m unittest discover -s tools -p 'test_check_docs.py'\n"
            "dotnet test tests/Missing.Tests/\npython3 tools/missing.py\n```\n",
            encoding="utf-8",
        )
        self.git(root, "add", "docs/contributing/quality.md")

        result, output = self.run_checker(root)
        self.assertEqual(result, 1)
        self.assertIn("missing referenced workflow/script/project .github/workflows/missing.yml", output)
        self.assertIn("missing referenced workflow/script/project SlopArena.csproj", output)
        self.assertIn("missing referenced workflow/script/project .githooks/pre-commit", output)
        self.assertIn("docs/contributing/quality.md:3: missing executable target/path missing", output)
        self.assertIn("docs/contributing/quality.md:4: missing executable target/path tools", output)
        self.assertIn("docs/contributing/quality.md:5: missing executable target/path tests/Missing.Tests/", output)
        self.assertIn("docs/contributing/quality.md:6: missing executable target/path tools/missing.py", output)

    def test_historical_quality_copy_is_exempt_from_executable_path_check(self) -> None:
        root = self.make_repo()
        guide = root / "docs/plans/quality.md"
        guide.parent.mkdir(parents=True, exist_ok=True)
        guide.write_text(
            "See `.github/workflows/missing.yml`.\n```bash\nmake missing\n```\n",
            encoding="utf-8",
        )
        self.git(root, "add", "docs/plans/quality.md")

        result, output = self.run_checker(root)
        self.assertEqual(result, 0, output)

    def test_current_guidance_rejects_private_paths_but_preserves_history(self) -> None:
        root = self.make_repo()
        guide = root / "docs/contributing/agents.md"
        guide.write_text("Run /home/alice/private/gateway.ts\n", encoding="utf-8")
        historical = root / "docs/plans/old-workflow.md"
        historical.parent.mkdir(parents=True)
        historical.write_text("Historical path /home/alice/private/gateway.ts\n")
        result, output = self.run_checker(root)
        self.assertEqual(result, 1)
        self.assertIn("author-specific filesystem path", output)
        self.assertNotIn("old-workflow.md", output)
        guide.write_text("Run scripts/unity-editor-gateway.ts\n")
        result, output = self.run_checker(root)
        self.assertEqual(result, 0, output)

    def test_canonical_skills_are_checked_for_retired_workflows(self) -> None:
        root = self.make_repo()
        guide = root / "skills/example/SKILL.md"
        guide.parent.mkdir(parents=True)
        guide.write_text("Use CharacterRegistry to add new fighters.\n")
        errors: list[str] = []
        with mock.patch.object(check_docs, "ROOT", root):
            check_docs.check_forbidden_terms(errors, [guide.relative_to(root)])
        self.assertTrue(any("global character registry" in error for error in errors))

    def test_current_file_uri_is_rejected_even_though_link_has_a_scheme(self) -> None:
        root = self.make_repo()
        guide = root / "docs/contributing/agents.md"
        guide.write_text("Read [protocol](file:///Users/alice/private/protocol.md).\n")
        result, output = self.run_checker(root)
        self.assertEqual(result, 1)
        self.assertIn("author-specific filesystem path", output)


if __name__ == "__main__":
    unittest.main()
