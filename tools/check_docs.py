#!/usr/bin/env python3
"""Check living documentation for broken links and obsolete workflow guidance.

Run from the repository root with ``python3 tools/check_docs.py``. The checker is
intentionally small: it validates paths and a short list of known migration traps,
not generated packet sizes, test totals, or other volatile implementation details.
"""
from __future__ import annotations

import os
import re
import subprocess
import sys
from pathlib import Path
from urllib.parse import unquote, urlsplit

import sync_agent_skills

ROOT = Path(__file__).resolve().parent.parent

# These are the current instructional surfaces. ADRs, plans, research, generated
# reports, handoffs, and archived tool skills remain records and are not linted for
# present-tense workflow vocabulary.
LIVING_DOCS = {
    Path("README.md"),
    Path("CONTRIBUTING.md"),
    Path("CONTEXT.md"),
    Path("AGENTS.md"),
    Path(".omp/AGENTS.md"),
    Path("docs/README.md"),
    Path("docs/architecture-overview.md"),
    Path("docs/testing.md"),
    Path("docs/characters/adding-a-new-character.md"),
    Path("docs/characters/fightguy.md"),
    Path("docs/systems/ability-architecture.md"),
    Path("docs/systems/ability-lab.md"),
    Path("docs/systems/animation-system.md"),
    Path("docs/systems/combat-systems.md"),
    Path("docs/systems/hitstun-di.md"),
    Path("docs/systems/netcode-architecture.md"),
    Path("docs/contributing/conventions.md"),
    Path("docs/contributing/quality.md"),
    Path("docs/contributing/unity-cli.md"),
    Path("docs/contributing/agents.md"),
    Path("docs/contributing/unity-editor-coordination.md"),
    Path("skills/README.md"),
}

CANONICAL_PATHS = {
    "src/Shared",
    "src/Server",
    "tests/Shared.Tests",
    "client/Unity/Assets/CharacterPackages",
    "content-cooked",
    "docs/characters/adding-a-new-character.md",
    "docs/characters/fightguy.md",
    "docs/systems/ability-architecture.md",
    "docs/systems/ability-lab.md",
    "docs/systems/animation-system.md",
    "docs/systems/combat-systems.md",
    "docs/systems/netcode-architecture.md",
    "docs/testing.md",
    "docs/contributing/conventions.md",
    "docs/contributing/unity-cli.md",
    "docs/contributing/agents.md",
    "docs/contributing/unity-editor-coordination.md",
    "AGENTS.md",
}

FORBIDDEN_CURRENT_TERMS = (
    (re.compile(r"\bGodot\b|_PhysicsProcess|MoveAndSlide|\bGD\."), "Godot workflow"),
    (re.compile(r"\bBuildFightGuy\b|\bBuild<Name>\b|\bBuildRegistry\b"), "registry factory workflow"),
    (re.compile(r"\bCharacterRegistry\b"), "global character registry workflow"),
    (re.compile(r"\bCharacterDefinition\.cs\b"), "legacy character-definition file workflow"),
    (re.compile(r"\bCharacterAnimationConfig\b"), "universal manual animation-config workflow"),
)

# A forbidden term is acceptable when a living guide explicitly scopes it to the
# legacy path or warns readers not to use it. This keeps migration notices useful.
LEGACY_OR_WARNING = re.compile(
    r"legacy|historical|superseded|deprecated|modification.only|not a template|"
    r"do not|must not|never|avoid|not universal|not a runtime|not current|compatibility|"
    r"\bno\b",
    re.IGNORECASE,
)

AUTHOR_PATH = re.compile(r"file:///|/(?:home|Users)/[^\s/`]+/|[A-Za-z]:[\\/]Users[\\/]")

# Inline Markdown links. Reference-style links are intentionally outside this
# check because their destinations are not recoverable without a full Markdown AST.
MARKDOWN_LINK = re.compile(r"(?<!!)\[[^\]]*\]\(([^)\s]+)(?:\s+[^)]*)?\)")
QUALITY_CODE_BLOCK = re.compile(r"(?m)^```(?:bash|sh)\s*\n(.*?)^```\s*$", re.DOTALL)
QUALITY_COMMAND = re.compile(
    r"(?m)^\s*(?:make\s+(?P<make>[A-Za-z0-9_.-]+)|"
    r"dotnet\s+(?:build|test)\s+(?P<dotnet>[^-\s\\][^\s\\]*)|"
    r"python3?\s+-m\s+unittest\s+discover\s+-s\s+(?P<unittest_start>[^\s]+)\s+"
    r"-p\s+['\"]?(?P<unittest_pattern>[^'\"\s]+)|"
    r"python3?\s+(?P<python>[^-\s\\][^\s\\]*)|bun\s+(?P<bun>[^-\s\\][^\s\\]*))"
)
QUALITY_REFERENCED_PATH = re.compile(r"`(?P<path>(?:[A-Za-z0-9_.-]+/)*[A-Za-z0-9_.-]+)`")


def check_quality_commands(errors: list[str], files: list[Path]) -> None:
    source = Path("docs/contributing/quality.md")
    if source not in files:
        return
    text = (ROOT / source).read_text(encoding="utf-8", errors="replace")
    for match in QUALITY_REFERENCED_PATH.finditer(text):
        target = match.group("path")
        if "/" not in target and not target.endswith((".csproj", ".sln")):
            continue
        if not (ROOT / target).is_file():
            line = text.count("\n", 0, match.start()) + 1
            errors.append(f"{source}:{line}: missing referenced workflow/script/project {target}")
    for block in QUALITY_CODE_BLOCK.finditer(text):
        for match in QUALITY_COMMAND.finditer(block.group(1)):
            make_target = match.group("make")
            unittest_start = match.group("unittest_start")
            unittest_pattern = match.group("unittest_pattern")
            target = make_target or match.group("dotnet") or match.group("python") or match.group("bun") or unittest_start
            if make_target:
                makefile = ROOT / "Makefile"
                targets: set[str] = set()
                if makefile.is_file():
                    for line in makefile.read_text(encoding="utf-8", errors="replace").splitlines():
                        if line and not line[0].isspace() and ":" in line:
                            targets.update(line.split(":", 1)[0].split())
                exists = make_target in targets
            elif unittest_start:
                test_dir = ROOT / unittest_start
                exists = test_dir.is_dir() and any(path.is_file() for path in test_dir.glob(unittest_pattern))
            elif match.group("python") or match.group("bun") or target.endswith((".csproj", ".sln")):
                exists = (ROOT / target).is_file()
            else:
                exists = (ROOT / target).is_dir()
            if not exists:
                line = text.count("\n", 0, block.start(1) + match.start()) + 1
                errors.append(f"{source}:{line}: missing executable target/path {target}")




def markdown_files() -> list[Path]:
    try:
        result = subprocess.run(
            [
                "git",
                "ls-files",
                "-z",
                "--cached",
                "--others",
                "--exclude-standard",
                "--",
                "*.md",
            ],
            cwd=ROOT,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            check=True,
        )
    except (OSError, subprocess.CalledProcessError) as error:
        diagnostic = getattr(error, "stderr", None) or str(error)
        raise RuntimeError(
            f"unable to enumerate project Markdown: {os.fsdecode(diagnostic).strip()}"
        ) from error

    root = ROOT.resolve()
    paths = {
        Path(os.fsdecode(item))
        for item in result.stdout.split(b"\0")
        if item
    }
    return sorted(
        path
        for path in paths
        if path.suffix == ".md"
        and (ROOT / path).is_file()
        and (ROOT / path).resolve().is_relative_to(root)
    )


def is_historical(path: Path) -> bool:
    historical_dirs = {
        Path("docs/adr"),
        Path("docs/plans"),
        Path("docs/research"),
        Path("docs/generated"),
        Path("docs/handoffs"),
        Path("docs/superpowers"),
        Path(".claude"),
    }
    return (
        path.name == ".memlog.md"
        or any(part.startswith("spec-") for part in path.parts)
        or any(path == directory or directory in path.parents for directory in historical_dirs)
    )


def is_current_guide(path: Path) -> bool:
    if is_historical(path):
        return False
    return (
        path in LIVING_DOCS
        or path == Path("CLAUDE.md")
        or path.parts[:1] == ("skills",)
        or path.parts[:2] in {(".omp", "skills"), ("docs", "contributing")}
    )


def check_portability(errors: list[str], files: list[Path]) -> None:
    for source in files:
        if not is_current_guide(source):
            continue
        for number, line in enumerate((ROOT / source).read_text(encoding="utf-8").splitlines(), 1):
            if AUTHOR_PATH.search(line):
                errors.append(f"{source}:{number}: author-specific filesystem path in current guidance")


def check_skill_adapters(errors: list[str]) -> None:
    if not any((ROOT / "skills").glob("*/SKILL.md")):
        return
    try:
        drift = sync_agent_skills.sync(ROOT)
    except (OSError, ValueError) as error:
        errors.append(f"skill adapters: {error}")
        return
    if drift:
        errors.append("stale skill adapters; run python3 tools/sync_agent_skills.py --write: " + ", ".join(drift))


def resolve_link(source: Path, target: str) -> Path | None:
    target = unquote(target.strip("<>"))
    if not target or target.startswith("#"):
        return source
    parsed = urlsplit(target)
    if parsed.scheme or parsed.netloc:
        return None
    raw_path = parsed.path
    if not raw_path:
        return source
    candidate = (ROOT / raw_path.lstrip("/")) if raw_path.startswith("/") else (ROOT / source.parent / raw_path)
    return candidate.resolve().relative_to(ROOT.resolve()) if candidate.resolve().is_relative_to(ROOT.resolve()) else candidate


def check_links(errors: list[str], files: list[Path]) -> None:
    for source in files:
        text = (ROOT / source).read_text(encoding="utf-8", errors="replace")
        for match in MARKDOWN_LINK.finditer(text):
            target = match.group(1)
            if target.startswith(("http://", "https://", "mailto:", "ftp://")):
                continue
            resolved = resolve_link(source, target)
            if resolved is None:
                continue
            full = ROOT / resolved
            if not full.exists():
                errors.append(f"{source}: broken internal link {target}")


def check_forbidden_terms(errors: list[str], files: list[Path]) -> None:
    for source in files:
        if not is_current_guide(source):
            continue
        lines = (ROOT / source).read_text(encoding="utf-8", errors="replace").splitlines()
        for line_number, line in enumerate(lines, 1):
            context = "\n".join(lines[max(0, line_number - 3):line_number])
            if LEGACY_OR_WARNING.search(context):
                continue
            for pattern, label in FORBIDDEN_CURRENT_TERMS:
                if pattern.search(line):
                    errors.append(f"{source}:{line_number}: forbidden current-doc term ({label})")


def check_canonical_paths(errors: list[str]) -> None:
    for relative in sorted(CANONICAL_PATHS):
        if not (ROOT / relative).exists():
            errors.append(f"missing canonical path: {relative}")


def main() -> int:
    errors: list[str] = []
    try:
        files = markdown_files()
    except RuntimeError as error:
        print("Documentation checks failed:")
        print(f"- {error}")
        return 1
    check_links(errors, files)
    check_forbidden_terms(errors, files)
    check_portability(errors, files)
    check_skill_adapters(errors)
    check_quality_commands(errors, files)
    check_canonical_paths(errors)
    if errors:
        print("Documentation checks failed:")
        for error in errors:
            print(f"- {error}")
        return 1
    print(f"Documentation checks passed ({len(files)} Markdown files scanned).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
