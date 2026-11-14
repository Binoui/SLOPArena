#!/usr/bin/env python3
"""Keep the retired selector enum inside the online protocol boundary."""
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parents[1]
ALLOWED = {"src/Shared/LegacyFighterProtocol.cs"}
# Comment lines are not uses. Some commented files are cook inputs (WeaponAttachConfig.cs is a
# source of four fighters' hashes), so editing their comments would force a recook.


def violations(root=ROOT):
    paths = subprocess.check_output(["git", "ls-files", "--cached", "--others", "--exclude-standard", "-z", "*.cs"], cwd=root).decode().split("\0")
    pattern = re.compile(r"\bCharacterClass\b")
    return [f"{name}:{i}" for name in paths if name and name not in ALLOWED and (root / name).is_file()
            for i, line in enumerate((root / name).read_text().splitlines(), 1)
            if pattern.search(line) and not line.lstrip().startswith("//")]


if __name__ == "__main__":
    found = violations()
    print("\n".join(found) if found else "Fighter identity guard passed.")
    raise SystemExit(bool(found))
