#!/usr/bin/env python3
"""Read-only check of local Unity packages, managed inputs and catalog GUIDs."""

import argparse
import hashlib
import json
import re
from pathlib import Path


CHARACTERS = ("fightguy", "manki", "wibou", "bonk")
GUID = re.compile(r"\bguid:\s*([0-9a-fA-F]{32})\b")
PACKAGES = {"com.kybernetik.animancer": "8.4.0", "com.unity.pipeline": "0.5.0-exp.1"}


def check(root: Path) -> dict:
    project = root / "client/Unity"
    problems = []
    package_versions = {}
    for name, expected in PACKAGES.items():
        manifest = project / "Packages" / name / "package.json"
        try:
            data = json.loads(manifest.read_text(encoding="utf-8"))
            package_versions[name] = data.get("version")
            if data.get("name") != name or data.get("version") != expected:
                problems.append(f"{manifest.relative_to(root)}: expected {name} {expected}.")
        except (OSError, ValueError, AttributeError, TypeError) as error:
            package_versions[name] = None
            problems.append(f"{manifest.relative_to(root)}: missing or unreadable package ({error}).")

    shared = project / "Assets/Plugins/SlopArena.Shared/SlopArena.Shared.dll"
    if not shared.is_file():
        problems.append("Shared plugin missing; build src/Shared with this Unity project closed or under your own gateway lease.")
    for dll in sorted(shared.parent.glob("*.dll")):
        if dll.name != shared.name:
            problems.append(f"Shared plugin folder contains an unexpected dependency copy: {dll.name}; follow the Unity setup migration guide.")

    nuget = project / "Assets/Plugins/NuGet"
    recorded_dlls = set()
    try:
        installed = json.loads((nuget / ".nuget-installed.json").read_text(encoding="utf-8"))
        for name, package in installed["packages"].items():
            for dll in package.get("dlls", []):
                recorded_dlls.add(dll)
                if Path(dll).name != dll or not (nuget / dll).is_file():
                    problems.append(f"NuGet record {name}: missing DLL {dll}.")
    except (OSError, ValueError, KeyError, TypeError, AttributeError) as error:
        problems.append(f"NuGet inventory missing or unreadable ({error}).")

    pins = {}
    try:
        for line in (nuget / "CHECKSUMS").read_text(encoding="utf-8").splitlines():
            match = re.fullmatch(r"([0-9a-f]{64})  ([^/\\]+\.dll)", line)
            if not match or match[2] in pins:
                problems.append(f"Invalid or repeated NuGet checksum entry: {line}.")
                continue
            digest, name = match.groups()
            pins[name] = digest
            dll = nuget / name
            if dll.is_file() and hashlib.sha256(dll.read_bytes()).hexdigest() != digest:
                problems.append(f"NuGet checksum mismatch: {name}.")
        if pins.keys() != recorded_dlls:
            problems.append("NuGet checksum list does not match the recorded DLL inventory.")
        if {dll.name for dll in nuget.glob("*.dll")} != recorded_dlls:
            problems.append("NuGet folder does not match the recorded DLL inventory.")
    except OSError as error:
        problems.append(f"NuGet checksum list missing or unreadable ({error}).")

    # Scan metadata, never binary art. Cached registry packages can resolve
    # serialized script references after Unity has restored those packages.
    index = {}
    for folder in (project / "Assets", project / "Packages", project / "Library/PackageCache"):
        if not folder.is_dir():
            continue
        for meta in folder.rglob("*.meta"):
            asset = Path(str(meta)[:-5])
            if not asset.is_file():
                continue
            match = GUID.search(meta.read_text(encoding="utf-8", errors="replace"))
            if match:
                index.setdefault(match.group(1).lower(), []).append(asset.relative_to(project).as_posix())

    catalogs = []
    for name in CHARACTERS:
        path = project / "Assets/CharacterPackages" / name / "CharacterAssetCatalog.asset"
        references = []
        try:
            guids = sorted(set(GUID.findall(path.read_text(encoding="utf-8"))))
            if not guids:
                problems.append(f"{name}: catalog contains no serialized asset references.")
            for guid in guids:
                guid = guid.lower()
                if guid.startswith("0000000000000000"):
                    continue  # Unity built-in objects, not provisioned assets.
                matches = sorted(set(index.get(guid, [])))
                references.append({"guid": guid, "paths": matches})
                if not matches:
                    problems.append(f"{name}: missing asset or metadata for GUID {guid}.")
                elif len(matches) > 1:
                    problems.append(f"{name}: ambiguous GUID {guid}: {', '.join(matches)}.")
        except OSError as error:
            problems.append(f"{path.relative_to(root)}: missing or unreadable catalog ({error}).")
        catalogs.append({"package": name, "references": references})

    return {
        "success": not problems,
        "scope": "installed embedded packages, pinned NuGet DLLs, Shared plugin ownership and four catalog references",
        "packages": package_versions,
        "gatewayOwnershipAvailable": (project / "Packages/com.unity.pipeline/Runtime/Common/EditorCommandOwnership.cs").is_file(),
        "catalogs": catalogs,
        "problems": problems,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1], help="Repository checkout to inspect")
    parser.add_argument("--json", action="store_true", help="Print the full reference report as JSON")
    args = parser.parse_args()
    report = check(args.root.resolve())
    if args.json:
        print(json.dumps(report, indent=2))
    else:
        print("Unity input check " + ("passed." if report["success"] else "failed."))
        print("Scope: " + report["scope"] + ".")
        for problem in report["problems"]:
            print("- " + problem)
        print("This does not verify compilation, stages/audio/UI, playable content, or redistribution rights.")
    return 0 if report["success"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
