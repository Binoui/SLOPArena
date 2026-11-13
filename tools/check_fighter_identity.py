#!/usr/bin/env python3
"""Keep the retired selector enum inside the online protocol boundary."""
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parents[1]
ALLOWED = {"src/Shared/LegacyFighterProtocol.cs"}


def violations(root=ROOT):
    paths = subprocess.check_output(["git", "ls-files", "--cached", "--others", "--exclude-standard", "-z", "*.cs"], cwd=root).decode().split("\0")
    pattern = re.compile(r"\bCharacterClass\b")
    return [f"{name}:{i}" for name in paths if name and name not in ALLOWED
            for i, line in enumerate((root / name).read_text().splitlines(), 1) if pattern.search(line)]


if __name__ == "__main__":
    found = violations()
    print("\n".join(found) if found else "Fighter identity guard passed.")
    raise SystemExit(bool(found))
