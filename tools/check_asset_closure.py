#!/usr/bin/env python3
"""Check that Unity assets would arrive complete in a fresh clone.

Walk every GUID reference reachable from the given assets and report the files
that Git would not ship. Run it before un-ignoring or committing art, from a
checkout that has the local art installed, so that every reference resolves:

    python3 tools/check_asset_closure.py client/Unity/Assets/Resources/Stages/log_off.prefab
    python3 tools/check_asset_closure.py --json client/Unity/Assets/Art/Characters/manki

Each reachable file is classified as:

- tracked: already in Git;
- to-add: not ignored, so it ships once it is added;
- ignored: excluded by .gitignore, so a fresh clone would miss it;
- registry: provided by a Unity package that the Package Manager downloads;
- builtin: a Unity built-in resource;
- dangling: no file on this machine has the GUID, so the reference is already broken.

The check reads text-serialized Unity assets and .meta files. References stored
in binary assets and shader #include paths are not followed. Exit status is 0
when everything ships, 1 when something would be missing and 2 for usage errors.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
from collections import defaultdict
from pathlib import Path

UNITY = Path("client/Unity")
GUID_REFERENCE = re.compile(rb"guid[:=]\s*([0-9a-f]{32})")
META_GUID = re.compile(rb"^guid:\s*([0-9a-f]{32})", re.M)
TEXT_ASSETS = {
    ".anim", ".asset", ".brush", ".controller", ".cubemap", ".flare", ".fontsettings",
    ".guiskin", ".lighting", ".mask", ".mat", ".overrideController", ".physicMaterial",
    ".physicsMaterial2D", ".playable", ".prefab", ".preset", ".renderTexture", ".shadergraph",
    ".shadersubgraph", ".signal", ".spriteatlas", ".spriteatlasv2", ".terrainlayer", ".tss",
    ".unity", ".uss", ".uxml",
}
FINE = ("tracked", "to-add", "registry", "builtin")
PROBLEMS = ("ignored", "dangling")


def _git(root: Path, *args: str, stdin: str | None = None) -> str:
    result = subprocess.run(
        ["git", "-C", str(root), *args], input=stdin, text=True,
        stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=False)
    if result.returncode not in (0, 1):  # check-ignore exits 1 when nothing matched
        raise RuntimeError(result.stderr.strip() or f"git {' '.join(args)} failed")
    return result.stdout


def _meta_guid(meta: Path) -> str | None:
    try:
        match = META_GUID.search(meta.read_bytes()[:512])
    except OSError:
        return None
    return match.group(1).decode() if match else None


def _index_guids(base: Path, root: Path) -> dict[str, str]:
    """Map each .meta GUID under base to its asset path relative to root."""
    guids: dict[str, str] = {}
    if not base.is_dir():
        return guids
    for directory, _, files in os.walk(base):
        for name in files:
            if name.endswith(".meta"):
                meta = Path(directory) / name
                guid = _meta_guid(meta)
                if guid:
                    guids[guid] = meta.relative_to(root).as_posix()[: -len(".meta")]
    return guids


def _references(path: Path) -> set[str]:
    """GUIDs referenced by an asset's text content and by its .meta file."""
    found: set[str] = set()
    if path.suffix in TEXT_ASSETS and path.is_file():
        try:
            found.update(m.group(1).decode() for m in GUID_REFERENCE.finditer(path.read_bytes()))
        except OSError:
            pass
    meta = path.with_name(path.name + ".meta")
    if meta.is_file():
        own = _meta_guid(meta)
        found.update(m.group(1).decode() for m in GUID_REFERENCE.finditer(meta.read_bytes()))
        found.discard(own or "")
    return found


def _expand(root: Path, targets: list[str]) -> list[str]:
    assets: list[str] = []
    for target in targets:
        path = (root / target).resolve()
        if not path.exists():
            raise FileNotFoundError(target)
        if not path.is_relative_to(root):
            raise ValueError(f"{target} is outside the checkout")
        if path.is_dir():
            assets.extend(
                p.relative_to(root).as_posix() for p in sorted(path.rglob("*"))
                if p.is_file() and p.suffix != ".meta")
        else:
            assets.append(path.relative_to(root).as_posix())
    return assets


def check(root: Path, targets: list[str]) -> dict:
    root = root.resolve()
    unity = root / UNITY
    roots = _expand(root, targets)
    project_guids = {}
    project_guids.update(_index_guids(unity / "Packages", root))
    project_guids.update(_index_guids(unity / "Assets", root))
    registry_guids = _index_guids(unity / "Library/PackageCache", root)
    tracked = set(_git(root, "ls-files", "-z", "--", str(UNITY)).split("\0"))

    # Breadth-first walk; remember who first referenced each file for the report.
    referrer: dict[str, str | None] = {path: None for path in roots}
    queue = list(roots)
    special: dict[str, str] = {}
    while queue:
        current = queue.pop(0)
        for guid in sorted(_references(root / current)):
            if guid.startswith("000000000000000"):
                continue
            if guid in project_guids:
                target = project_guids[guid]
                if target not in referrer:
                    referrer[target] = current
                    queue.append(target)
            elif guid in registry_guids:
                special.setdefault(f"registry:{registry_guids[guid]}", current)
            else:
                special.setdefault(f"dangling:{guid}", current)

    files = sorted(referrer)
    candidates = files + [f + ".meta" for f in files]
    ignored: dict[str, str] = {}
    # -z output is four NUL-terminated fields per path: source, line, pattern, path.
    # A matching "!" pattern means the path is re-included, not ignored.
    fields = _git(root, "check-ignore", "-v", "-n", "-z", "--stdin", stdin="\0".join(candidates) + "\0").split("\0")
    for source, line, pattern, path in zip(fields[0::4], fields[1::4], fields[2::4], fields[3::4]):
        if source and path and not pattern.startswith("!"):
            asset = path[: -len(".meta")] if path.endswith(".meta") else path
            ignored.setdefault(asset, f"{source}:{line}:{pattern}")

    entries = []
    for path in files:
        if path in tracked:
            status = "tracked"
        elif path in ignored:
            status = "ignored"
        else:
            status = "to-add"
        entries.append({"path": path, "status": status, "rule": ignored.get(path, ""), "referencedBy": referrer[path]})
    for key, by in sorted(special.items()):
        kind, _, value = key.partition(":")
        entries.append({"path": value, "status": kind, "rule": "", "referencedBy": by})

    counts = {status: sum(1 for e in entries if e["status"] == status) for status in FINE + PROBLEMS}
    return {
        "success": not any(counts[p] for p in PROBLEMS),
        "roots": roots,
        "counts": counts,
        "registryIndexed": bool(registry_guids),
        "entries": entries,
    }


def _group(path: str) -> str:
    parts = Path(path).parts
    if len(parts) > 3 and parts[:2] == ("client", "Unity"):
        return "/".join(parts[2:5 if parts[2] == "Assets" else 4])
    return path


def _print(result: dict) -> None:
    counts = result["counts"]
    print(f"Checked {len(result['roots'])} asset(s): "
          + ", ".join(f"{counts[s]} {s}" for s in FINE + PROBLEMS))
    if not result["registryIndexed"]:
        print("Note: client/Unity/Library/PackageCache is missing, so Unity package references show as dangling.")
    ignored = [e for e in result["entries"] if e["status"] == "ignored"]
    if ignored:
        print("\nIgnored by Git, so a fresh clone would miss them:")
        groups: dict[str, list[dict]] = defaultdict(list)
        for entry in ignored:
            groups[_group(entry["path"])].append(entry)
        for group, members in sorted(groups.items(), key=lambda kv: (-len(kv[1]), kv[0])):
            example = members[0]
            print(f"  {len(members):4d}  {group}  ({example['rule'].split(':')[-1] or 'ignored'})")
            print(f"        e.g. {example['path']}")
            print(f"        referenced by {example['referencedBy']}")
    dangling = [e for e in result["entries"] if e["status"] == "dangling"]
    if dangling:
        print("\nReferences that no file on this machine provides (already broken):")
        for entry in dangling:
            print(f"  guid {entry['path']}  referenced by {entry['referencedBy']}")
    to_add = [e for e in result["entries"] if e["status"] == "to-add"]
    if to_add:
        print(f"\n{len(to_add)} file(s) are not ignored but not yet in Git; add them with the assets that use them.")
    print("\nAll referenced files ship." if result["success"] else "\nSome referenced files would be missing from a fresh clone.")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("targets", nargs="+", help="asset files or folders, relative to the checkout root")
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parent.parent,
                        help="checkout to inspect (default: this repository)")
    parser.add_argument("--json", action="store_true", help="print the full result as JSON")
    args = parser.parse_args(argv)
    try:
        result = check(args.root, args.targets)
    except (FileNotFoundError, ValueError, RuntimeError) as error:
        print(f"check_asset_closure: {error}", file=sys.stderr)
        return 2
    if args.json:
        print(json.dumps(result, indent=2))
    else:
        _print(result)
    return 0 if result["success"] else 1


if __name__ == "__main__":
    sys.exit(main())
