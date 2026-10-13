#!/usr/bin/env python3
"""Build and package a source-pinned Windows playtest client with installed Unity."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import secrets
import shutil
import subprocess
import sys
import time
from pathlib import Path
from urllib.parse import urlsplit

DEFAULT_UNITY = str(Path.home() / "Unity/Hub/Editor/6000.0.78f1/Editor/Unity")
VERSION_RE = re.compile(r"[0-9]+\.[0-9]+\.[0-9]+-playtest\.[0-9]+\Z")
REVISION_RE = re.compile(r"[0-9a-f]{40}\Z")


class BuildError(RuntimeError):
    pass


def version_arg(value: str) -> str:
    if len(value) > 64 or VERSION_RE.fullmatch(value) is None:
        raise argparse.ArgumentTypeError("version must be X.Y.Z-playtest.N")
    return value


def revision_arg(value: str) -> str:
    if REVISION_RE.fullmatch(value) is None:
        raise argparse.ArgumentTypeError("source revision must be a full lowercase 40-character commit SHA")
    return value


def endpoint_arg(value: str) -> str:
    if len(value) > 256 or any(char.isspace() or ord(char) < 32 for char in value):
        raise argparse.ArgumentTypeError("endpoint must be a bounded HTTPS origin")
    try:
        parsed = urlsplit(value)
        valid = (parsed.scheme == "https" and bool(parsed.hostname) and not parsed.username and
                 not parsed.password and parsed.path in ("", "/") and not parsed.query and not parsed.fragment)
        if parsed.port is not None and not 1 <= parsed.port <= 65535:
            valid = False
    except ValueError:
        valid = False
    if not valid:
        raise argparse.ArgumentTypeError("endpoint must be a bounded HTTPS origin")
    return value


def unity_path_arg(value: str) -> Path:
    if any(ord(char) < 32 for char in value):
        raise argparse.ArgumentTypeError("Unity Editor path contains a control character")
    path = Path(value).expanduser()
    if not path.is_absolute():
        raise argparse.ArgumentTypeError("Unity Editor path must be absolute")
    return path


def run_capture(command: list[str], cwd: Path, failure: str) -> str:
    try:
        result = subprocess.run(command, cwd=cwd, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE,
                                stderr=subprocess.DEVNULL, text=True, check=False,
                                env={**os.environ, "GIT_TERMINAL_PROMPT": "0"})
    except OSError as exc:
        raise BuildError(failure) from exc
    if result.returncode:
        raise BuildError(failure)
    return result.stdout


def run_logged(command: list[str], cwd: Path, log_path: Path, label: str) -> None:
    try:
        with log_path.open("x", encoding="utf-8") as log:
            log.write(label + "\n")
            result = subprocess.run(command, cwd=cwd, stdin=subprocess.DEVNULL,
                                    stdout=log, stderr=subprocess.STDOUT, check=False)
    except OSError as exc:
        raise BuildError(f"Could not run {label}; see {log_path}") from exc
    if result.returncode:
        raise BuildError(f"{label} exited with status {result.returncode}; see {log_path}")


def check_editor(editor_arg: Path) -> Path:
    try:
        editor = editor_arg.resolve(strict=True)
    except OSError as exc:
        raise BuildError(f"Unity Editor executable is missing: {editor_arg}") from exc
    if not editor.is_file() or not os.access(editor, os.X_OK):
        raise BuildError(f"Unity Editor is not an executable file: {editor}")
    if editor.name != "Unity":
        raise BuildError("Unity Editor path must name an installed Unity executable")
    support = editor.parent / "Data/PlaybackEngines/WindowsStandaloneSupport"
    required = (
        support / "modules.asset",
        support / "Variations/win64_player_nondevelopment_mono/WindowsPlayer.exe",
        support / "Variations/win64_player_development_mono/WindowsPlayer.exe",
    )
    missing = [path for path in required if not path.is_file() or path.stat().st_size == 0]
    if missing:
        raise BuildError("Installed Unity lacks Windows Mono build support; missing " + ", ".join(map(str, missing)))
    if not shutil.which("git"):
        raise BuildError("git is required for the read-only source pin and isolated clone")
    dotnet = shutil.which("dotnet")
    if not dotnet:
        raise BuildError(".NET SDK (`dotnet`) is required to build src/Shared")
    try:
        sdks = subprocess.run([dotnet, "--list-sdks"], stdin=subprocess.DEVNULL, stdout=subprocess.PIPE,
                              stderr=subprocess.DEVNULL, text=True, check=False)
    except OSError as exc:
        raise BuildError("Could not inspect installed .NET SDK prerequisites") from exc
    if sdks.returncode or not sdks.stdout.strip():
        raise BuildError("A .NET SDK is required to build src/Shared")
    return editor


def check_source(root: Path, revision: str, version: str) -> None:
    top = run_capture(["git", "rev-parse", "--show-toplevel"], root, "Could not identify the canonical Git checkout").strip()
    if Path(top).resolve() != root:
        raise BuildError("Run this wrapper from its canonical Git checkout")
    head = run_capture(["git", "rev-parse", "HEAD"], root, "Could not read canonical checkout HEAD").strip()
    if head != revision:
        raise BuildError("Selected source revision does not equal canonical checkout HEAD")
    status = run_capture(["git", "status", "--porcelain=v1", "--untracked-files=all"], root,
                         "Could not verify canonical checkout cleanliness")
    if status:
        raise BuildError("Canonical checkout is dirty; refusing to clone or build")
    published = run_capture(["git", "ls-remote", "--exit-code", "origin", "refs/heads/main"], root,
                             "Could not read published origin/main; no fetch or reset was attempted")
    refs = [line.split() for line in published.splitlines() if line.split() and line.split()[-1] == "refs/heads/main"]
    if len(refs) != 1 or refs[0][0] != revision:
        raise BuildError("Selected source revision does not equal published origin/main")
    ignored = subprocess.run(["git", "check-ignore", "--quiet", "--no-index", "--", f"build/playtest/{version}"],
                             cwd=root, stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL,
                             stderr=subprocess.DEVNULL, check=False)
    if ignored.returncode:
        raise BuildError("build/playtest/<version> is not ignored; refusing to write build evidence")


def make_dirs_beneath(root: Path, target: Path) -> None:
    try:
        relative = target.relative_to(root)
    except ValueError as exc:
        raise BuildError(f"Refusing path outside canonical checkout: {target}") from exc
    current = root
    for part in relative.parts:
        current = current / part
        if current.is_symlink():
            raise BuildError(f"Refusing symlinked build path: {current}")
        if current.exists():
            if not current.is_dir():
                raise BuildError(f"Expected a directory but found a file: {current}")
        else:
            current.mkdir()


def reject_project_symlinks(project: Path) -> None:
    if project.is_symlink():
        raise BuildError("Isolated Unity project root is symlinked")
    for directory, folders, files in os.walk(project, followlinks=False):
        for name in (*folders, *files):
            if (Path(directory) / name).is_symlink():
                raise BuildError("Isolated Unity project contains a symlink; refusing import/build")


def project_is_open(project: Path) -> bool:
    proc = Path("/proc")
    if not proc.is_dir():
        raise BuildError("Cannot verify Unity project closure: /proc is unavailable")
    project = project.resolve(strict=True)
    for entry in proc.iterdir():
        if not entry.name.isdigit():
            continue
        try:
            executable = Path(os.readlink(entry / "exe"))
        except PermissionError as exc:
            try:
                process_name = (entry / "comm").read_text(encoding="utf-8").strip()
            except OSError:
                continue
            if process_name in ("Unity", "Unity.exe"):
                raise BuildError("Cannot verify whether a Unity Editor process has this project open") from exc
            continue
        except OSError:
            continue
        if executable.name not in ("Unity", "Unity.exe"):
            continue
        try:
            arguments = (entry / "cmdline").read_bytes().decode(errors="replace").split("\0")
            cwd = Path(os.readlink(entry / "cwd")).resolve()
        except (FileNotFoundError, ProcessLookupError):
            continue
        except PermissionError as exc:
            raise BuildError("Cannot verify whether a Unity Editor process has this project open") from exc
        if cwd == project:
            return True
        for index, argument in enumerate(arguments):
            candidate = None
            if argument in ("-projectPath", "-projectpath") and index + 1 < len(arguments):
                candidate = arguments[index + 1]
            elif argument.startswith("-projectPath=") or argument.startswith("-projectpath="):
                candidate = argument.split("=", 1)[1]
            if candidate:
                path = Path(candidate)
                if not path.is_absolute():
                    path = cwd / path
                if path.resolve() == project:
                    return True
    return False


def licensed_inputs(local_project: Path) -> tuple[Path, dict[str, str]]:
    """Fingerprint the same allowed inputs used by the producer, without copying."""
    local_project = local_project.resolve(strict=True)
    source_root = Path(run_capture(["git", "rev-parse", "--show-toplevel"], local_project,
                                   "Local Unity dependency source is not a Git checkout").strip()).resolve()
    if local_project != source_root / "client/Unity":
        raise BuildError("Local dependency source must be that checkout's client/Unity project")
    if not (local_project / "Packages/com.kybernetik.animancer/package.json").is_file():
        raise BuildError("Local dependency source lacks the installed licensed Animancer package")
    paths = run_capture(["git", "ls-files", "--others", "--ignored", "--exclude-standard", "-z", "--",
                         "client/Unity/Assets", "client/Unity/Packages"], source_root,
                        "Could not enumerate ignored local Unity dependencies").split("\0")
    generated = ("client/Unity/Assets/Generated/", "client/Unity/Assets/Resources/Generated/",
                 "client/Unity/Assets/Plugins/SlopArena.Shared/", "client/Unity/Assets/Temp/")
    hashes = {}
    for name in paths:
        if not name or name.startswith(generated):
            continue
        relative = Path(name)
        if any(part.startswith(".") for part in relative.parts) or relative.suffix in (".pdb", ".log"):
            continue
        source = source_root / relative
        if not source.is_file():
            continue
        if not source.resolve().is_relative_to(local_project):
            raise BuildError(f"Local dependency resolves outside the supplied project: {name}")
        hashes[name] = hashlib.sha256(source.read_bytes()).hexdigest()
    return source_root, hashes


def stage_local_dependencies(local_project: Path, checkout: Path, evidence: Path) -> None:
    source_root, hashes = licensed_inputs(local_project)
    for name, before in hashes.items():
        source = source_root / name
        destination = checkout / name
        if destination.exists() or destination.is_symlink():
            raise BuildError(f"Local dependency would replace committed source: {name}")
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, destination)
        if hashlib.sha256(destination.read_bytes()).hexdigest() != before:
            raise BuildError(f"Local dependency changed while copying: {name}")
    _, after = licensed_inputs(local_project)
    if hashes != after:
        raise BuildError("Licensed inputs changed while staging; prepare a new input snapshot")
    (evidence / "local-dependencies.json").write_text(json.dumps(hashes, indent=2, sort_keys=True) + "\n")
    reject_project_symlinks(checkout / "client/Unity")


def run(args: argparse.Namespace) -> dict:
    root = Path(__file__).resolve().parents[1]
    editor = check_editor(args.unity_editor)
    check_source(root, args.source_revision, args.version)

    attempt = time.strftime("%Y%m%dT%H%M%SZ", time.gmtime()) + "-" + secrets.token_hex(4)
    build_root = root / "build"
    evidence_parent = root / "build/playtest" / args.version
    make_dirs_beneath(root, evidence_parent)
    archive = evidence_parent / f"SlopArena-{args.version}.tar.gz"
    if archive.exists() or archive.is_symlink():
        raise BuildError("Publish-ready archive already exists; use a new version rather than overwrite it")
    evidence = evidence_parent / attempt
    evidence.mkdir(mode=0o700)
    args.evidence_directory = evidence
    unity_version = re.search(r"m_EditorVersion:\s*(\S+)", (root / "client/Unity/ProjectSettings/ProjectVersion.txt").read_text()).group(1)
    if editor.parent.parent.name != unity_version:
        raise BuildError(f"Installed Unity must match pinned project version {unity_version}")
    (evidence / "environment.json").write_text(json.dumps({
        "sourceRevision": args.source_revision, "unityVersion": unity_version,
        "buildTarget": "Win64", "unityEditor": str(editor), "version": args.version,
    }, indent=2) + "\n")
    stage = build_root / (".playtest-local-" + attempt)
    stage.mkdir(mode=0o700)
    owner_file = stage / ".owner"
    owner_file.write_text(attempt + "\n", encoding="ascii")
    checkout = stage / "source"
    checkout_path = str(checkout.resolve())

    run_logged(["git", "clone", "--no-hardlinks", "--no-checkout", str(root), str(checkout)], root,
               evidence / "clone.log", "Cloning approved source into a fresh isolated checkout")
    run_logged(["git", "checkout", "--detach", args.source_revision], checkout,
               evidence / "checkout.log", "Pinning isolated checkout to approved source")
    isolated_head = run_capture(["git", "rev-parse", "HEAD"], checkout, "Could not verify isolated checkout revision").strip()
    if isolated_head != args.source_revision:
        raise BuildError("Isolated checkout revision differs from the approved source revision")
    if run_capture(["git", "status", "--porcelain=v1", "--untracked-files=all"], checkout,
                   "Could not verify isolated checkout cleanliness"):
        raise BuildError("Fresh isolated checkout is unexpectedly dirty")

    project = checkout / "client/Unity"
    canonical_project = root / "client/Unity"
    if not project.is_dir() or project.is_symlink() or project.resolve() != project or project.resolve() == canonical_project.resolve():
        raise BuildError("Isolated Unity project is missing, symlinked, or resolves to the canonical project")
    reject_project_symlinks(project)
    isolated_build = checkout / "build"
    if isolated_build.is_symlink() or (isolated_build.exists() and not isolated_build.is_dir()):
        raise BuildError("Isolated build output root is symlinked or not a directory")
    if project_is_open(project):
        raise BuildError("Isolated Unity project is already open in an Editor; refusing batchmode")

    stage_local_dependencies(args.local_unity_project or canonical_project, checkout, evidence)
    if args.cook_baseline and args.cook_baseline.is_dir():
        baseline = checkout / "build/playtest/accepted-cook-status"
        baseline.mkdir(parents=True)
        for source in args.cook_baseline.glob("*.json"):
            if source.is_symlink() or re.fullmatch(r"[a-z][a-z0-9.-]*\.json", source.name) is None:
                raise BuildError("Invalid advisory cook-status snapshot")
            json.loads(source.read_text())
            shutil.copy2(source, baseline / source.name)

    shared_log = evidence / "shared-build.log"
    run_logged(["dotnet", "build", "src/Shared/", "--configuration", "Release", "--nologo"], checkout,
               shared_log, "Building Shared and copying its plugin closure only into the isolated Unity project")

    producer = checkout / "scripts/ci/PlaytestReleaseBuilder.cs"
    editor_assets = project / "Assets/Editor"
    make_dirs_beneath(project.resolve(), editor_assets)
    injected = editor_assets / "PlaytestReleaseBuilder.cs"
    if injected.exists() or injected.is_symlink():
        raise BuildError("Isolated project already contains PlaytestReleaseBuilder.cs; refusing to overwrite it")
    if not producer.is_file() or producer.is_symlink():
        raise BuildError("Unchanged scripts/ci/PlaytestReleaseBuilder.cs is missing or symlinked")
    shutil.copy2(producer, injected)

    release = checkout / "build/release" / ("SlopArena-" + args.version)
    client_receipt = checkout / "build/playtest/client.json"
    if release.exists() or release.is_symlink() or client_receipt.exists() or client_receipt.is_symlink():
        raise BuildError("Isolated release output or receipt already exists; refusing to overwrite it")
    if project_is_open(project):
        raise BuildError("Isolated Unity project became open before batchmode; refusing to build")

    unity_log = evidence / "unity.log"
    launcher_log = evidence / "unity-launcher.log"
    # Platform-sensitive dependency hashes must be computed for the player target.
    unity_command = [str(editor), "-batchmode", "-quit", "-buildTarget", "Win64",
                     "-projectPath", str(project.resolve()),
                     "-executeMethod", "PlaytestReleaseBuilder.DiagnoseCook" if args.diagnose_cook else "PlaytestReleaseBuilder.Build",
                     "-releaseVersion", args.version,
                     "-releaseSourceRevision", args.source_revision,
                     "-releaseMasterEndpoint", args.endpoint,
                     "-logFile", str(unity_log)]
    try:
        run_logged(unity_command, checkout, launcher_log, "Running installed licensed Unity batchmode without activation arguments")
    except BuildError:
        report = checkout / "build/playtest/cook-verification.json"
        if report.is_file():
            shutil.copy2(report, evidence / "cook-verification.json")
        raise
    if args.diagnose_cook:
        diagnostics = checkout / "build/playtest/cook-diagnostics"
        if not (diagnostics / "cook.json").is_file():
            raise BuildError("Diagnostic cook did not produce its complete roster report")
        shutil.copytree(diagnostics, evidence / "cook-diagnostics")
        patch = subprocess.run(["git", "diff", "--binary", "--", "content-cooked"], cwd=checkout,
                               stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, check=True)
        (evidence / "cook-correction.patch").write_bytes(patch.stdout)
        handoff = {"version": args.version, "sourceRevision": args.source_revision,
                   "result": "diagnostic-cook", "report": str(evidence / "cook-diagnostics/cook.json"),
                   "correctionPatch": str(evidence / "cook-correction.patch"),
                   "isolatedRoot": checkout_path, "isolatedSourceRemoved": False}
        (evidence / "handoff.json").write_text(json.dumps(handoff, indent=2) + "\n")
        return handoff
    if not unity_log.is_file() or f"PLAYTEST_RELEASE_BUILD_OK version={args.version} source={args.source_revision}" not in unity_log.read_text(encoding="utf-8", errors="replace"):
        raise BuildError(f"Unity did not report a successful real player build; see {unity_log} and {launcher_log}")
    if not client_receipt.is_file() or not release.is_dir():
        raise BuildError("Unity reported success but the producer receipt or Windows output is missing")

    coordinator = [sys.executable, "scripts/playtest-release.py"]
    run_logged(coordinator + ["verify-client", "--client", "build/playtest/client.json",
                              "--version", args.version, "--source-revision", args.source_revision,
                              "--endpoint", args.endpoint], checkout, evidence / "verify-client.log",
               "Verifying isolated client receipt and packaged bytes")
    run_logged(coordinator + ["pack-client", "--client", str(checkout), "--version", args.version,
                              "--source-revision", args.source_revision, "--endpoint", args.endpoint,
                              "--output", str(archive)], checkout, evidence / "pack-client.log",
               "Packing the source-pinned client archive with the existing coordinator CLI")
    if not archive.is_file() or archive.stat().st_size == 0:
        raise BuildError("Coordinator did not create a non-empty client archive")

    shutil.copy2(client_receipt, evidence / "client.json")
    output_evidence = evidence / "release" / release.name
    output_evidence.parent.mkdir()
    shutil.copytree(release, output_evidence)

    if owner_file.read_text(encoding="ascii").strip() != attempt or stage.is_symlink():
        raise BuildError("Task-owned isolated source marker changed; refusing to remove staging")
    shutil.rmtree(stage)
    handoff = {
        "version": args.version,
        "sourceRevision": args.source_revision,
        "endpoint": args.endpoint,
        "isolatedRoot": checkout_path,
        "artifactArchive": str(archive.resolve()),
        "publicClientReceipt": str((evidence / "client.json").resolve()),
        "releaseOutput": str(output_evidence.resolve()),
        "logs": {path.stem: str(path.resolve()) for path in evidence.glob("*.log")},
        "result": "succeeded",
        "isolatedSourceRemoved": True,
    }
    with (evidence / "handoff.json").open("x", encoding="utf-8") as receipt:
        json.dump(handoff, receipt, indent=2, sort_keys=True)
        receipt.write("\n")
    return handoff


def parser() -> argparse.ArgumentParser:
    result = argparse.ArgumentParser(description=__doc__)
    result.add_argument("--version", required=True, type=version_arg)
    result.add_argument("--source-revision", required=True, type=revision_arg)
    result.add_argument("--endpoint", required=True, type=endpoint_arg)
    result.add_argument("--unity-editor", type=unity_path_arg,
                        default=os.environ.get("UNITY_EDITOR", DEFAULT_UNITY))
    result.add_argument("--local-unity-project", type=Path,
                        help="Read ignored licensed dependencies from this checkout's client/Unity; never writes there")
    result.add_argument("--diagnose-cook", action="store_true",
                        help="Produce a cold Win64 cook report and reviewable correction; never builds or publishes a client")
    result.add_argument("--cook-baseline", type=Path,
                        help="Optional advisory dependency snapshots for automatic diffs; never an admission authority")
    return result


def main() -> int:
    args = parser().parse_args()
    try:
        print(json.dumps(run(args), indent=2, sort_keys=True))
        return 0
    except (BuildError, OSError) as exc:
        if getattr(args, "evidence_directory", None):
            (args.evidence_directory / "failure.json").write_text(json.dumps({
                "result": "failed", "version": args.version, "sourceRevision": args.source_revision,
                "reason": str(exc), "publishReady": False,
            }, indent=2) + "\n")
        print(f"Local playtest build failed: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
