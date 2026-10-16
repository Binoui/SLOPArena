#!/usr/bin/env python3
"""Transfer this checkout's ignored Unity inputs to another private development machine."""

import argparse
import hashlib
import json
import shutil
import subprocess
import sys
import zipfile
from datetime import datetime, timezone
from pathlib import Path, PurePosixPath


MANIFEST = "asset-manifest.json"
ROOTS = ("client/Unity/Assets/", "client/Unity/Packages/")
GENERATED = tuple("client/Unity/Assets/" + name for name in (
    "Generated", "Resources/Generated", "Plugins/SlopArena.Shared", "Temp",
    "StreamingAssets", "Library", "StageDesignPreviews",
))
INSTALLERS = (".zip", ".unitypackage", ".7z", ".tar", ".tar.gz", ".tgz")
SCRATCH = (".pdb", ".log", ".pyc", ".uid", ".blend1", ".tmp", ".bak")
ANCHORS = (
    "client/Unity/ProjectSettings/ProjectVersion.txt",
    "client/Unity/Packages/manifest.json",
    "client/Unity/Packages/packages-lock.json",
)


def git(root, *args):
    return subprocess.check_output(["git", "-C", str(root), *args], stderr=subprocess.PIPE)


def checkout(root):
    root = root.expanduser().resolve(strict=True)
    if Path(git(root, "rev-parse", "--show-toplevel").decode().strip()).resolve() != root:
        raise ValueError("--root must be the SLOPArena repository root, not client/Unity.")
    for name in ("src/Shared/SlopArena.Shared.csproj", "tests/Shared.Tests/Shared.Tests.csproj"):
        if not (root / name).is_file():
            raise ValueError(f"Not a SLOPArena checkout: missing {name}")
    return root


def names(root, *args):
    return {p.decode("utf-8") for p in git(root, "ls-files", "-z", *args).split(b"\0") if p}


def allowed(name):
    # Apply the same exclusions to an asset and its sidecar metadata.
    asset = name.removesuffix(".meta")
    parts = PurePosixPath(asset).parts
    return (name.startswith(ROOTS) and not any(p.startswith(".") or p == "__pycache__" for p in parts)
            and not any(asset == p or asset.startswith(p + "/") for p in GENERATED)
            and not asset.lower().endswith(INSTALLERS + SCRATCH))


def destination(root, name):
    parts = PurePosixPath(name).parts
    if (not allowed(name) or ".." in parts or "\\" in name or ":" in name
            or PurePosixPath(name).is_absolute() or PurePosixPath(name).as_posix() != name):
        raise ValueError(f"Invalid asset path: {name}")
    path = root.joinpath(*parts)
    for parent in (path, *path.parents):
        if parent == root:
            break
        if parent.is_symlink():
            raise ValueError(f"Symlink in asset path: {name}")
        if parent != path and parent.exists() and not parent.is_dir():
            raise ValueError(f"File blocks an asset directory: {name}")
    return path


def digest_file(path, text=False):
    if text:
        return hashlib.sha256(path.read_bytes().replace(b"\r\n", b"\n")).hexdigest()
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def collect(root):
    tracked = names(root)
    selected = set()
    directories = set()
    required = set(ANCHORS)
    for name in sorted(names(root, "--others", "--ignored", "--exclude-standard", "--", *ROOTS)):
        if not allowed(name):
            continue
        path = destination(root, name)
        if not path.is_file():
            continue
        if name.endswith(".meta"):
            asset = path.with_suffix("")
            if not asset.exists():
                continue
            if asset.is_dir():
                directories.add(name[:-5])
            elif name[:-5] in tracked:
                # A local sidecar must not silently change a tracked asset's binding.
                required.add(name[:-5])
        selected.add(name)
        for candidate in (path, *path.parents):
            if candidate == root / "client/Unity":
                break
            meta = candidate.relative_to(root).as_posix() + ".meta"
            if meta in tracked:
                required.add(meta)
            elif allowed(meta) and (root / meta).is_file():
                destination(root, meta)
                selected.add(meta)
                if candidate.is_dir():
                    directories.add(candidate.relative_to(root).as_posix())
    for character in ("fightguy", "manki", "wibou", "bonk"):
        for name in ("CharacterAssetCatalog.asset", "character.json", "package.json"):
            required.add(f"client/Unity/Assets/CharacterPackages/{character}/{name}")
    if "client/Unity/Packages/com.kybernetik.animancer/package.json" not in selected:
        raise ValueError("The source checkout does not have the local Animancer package.")
    anchors = {name: digest_file(root / name, text=True) for name in sorted(required)}
    return sorted(selected), sorted(directories), anchors


def export_bundle(root, archive):
    archive = archive.expanduser().absolute()
    if archive.exists():
        raise ValueError(f"Archive already exists: {archive}")
    selected, directories, anchors = collect(root)
    archive.parent.mkdir(parents=True, exist_ok=True)
    partial = archive.with_name(archive.name + ".partial")
    if partial.exists():
        raise ValueError(f"An unfinished export already exists; choose another archive name: {partial}")
    manifest = {"format": 1, "createdUtc": datetime.now(timezone.utc).isoformat(),
                "sourceCommit": git(root, "rev-parse", "HEAD").decode().strip(),
                "unityVersion": "6000.0.78f1", "requiredTrackedFiles": anchors,
                "directories": directories, "files": {}}
    total = sum((root / name).stat().st_size for name in selected)
    print(f"Packing {len(selected):,} files ({total / 1024**3:.2f} GiB before compression).", flush=True)
    try:
        with zipfile.ZipFile(partial, "x", compression=zipfile.ZIP_DEFLATED, compresslevel=3) as bundle:
            for index, name in enumerate(selected, 1):
                path = destination(root, name)
                before = path.stat()
                digest = hashlib.sha256()
                info = zipfile.ZipInfo.from_file(path, arcname=name, strict_timestamps=False)
                info.compress_type = zipfile.ZIP_DEFLATED
                with path.open("rb") as source, bundle.open(info, "w", force_zip64=True) as target:
                    for chunk in iter(lambda: source.read(1024 * 1024), b""):
                        target.write(chunk)
                        digest.update(chunk)
                after = path.stat()
                if (before.st_size, before.st_mtime_ns) != (after.st_size, after.st_mtime_ns):
                    raise ValueError(f"Asset changed while exporting; rerun: {name}")
                manifest["files"][name] = {"size": after.st_size, "sha256": digest.hexdigest()}
                if index % 2000 == 0:
                    print(f"Packed {index:,}/{len(selected):,} files.", flush=True)
            for name in directories:
                bundle.writestr(name + "/", b"")
            if anchors != {name: digest_file(root / name, text=True) for name in anchors}:
                raise ValueError("Tracked asset metadata changed while exporting; rerun.")
            bundle.writestr(MANIFEST, json.dumps(manifest, indent=2) + "\n")
        partial.rename(archive)
    except BaseException:
        partial.unlink(missing_ok=True)
        raise
    archive.with_suffix(".manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    checksum = digest_file(archive)
    archive.with_suffix(archive.suffix + ".sha256").write_text(f"{checksum}  {archive.name}\n", encoding="utf-8")
    script = archive.parent / "unity_asset_bundle.py"
    if script.resolve() != Path(__file__).resolve():
        shutil.copy2(__file__, script)
    print(f"Created {archive} ({archive.stat().st_size / 1024**3:.2f} GiB).")
    print(f"SHA-256: {checksum}")
    return manifest


def import_bundle(root, archive, dry_run=False):
    if (root / "client/Unity/Temp/UnityLockfile").exists():
        raise ValueError("Close Unity for the destination project before importing.")
    tracked = names(root)
    with zipfile.ZipFile(archive) as bundle:
        manifest = json.loads(bundle.read(MANIFEST))
        if manifest.get("format") != 1:
            raise ValueError("Unsupported asset manifest format.")
        entries = bundle.infolist()
        expected = set(manifest["files"]) | {MANIFEST} | {p + "/" for p in manifest["directories"]}
        if len(entries) != len(expected) or {p.filename for p in entries} != expected:
            raise ValueError("ZIP entries do not match the asset manifest.")
        for name, digest in manifest["requiredTrackedFiles"].items():
            if name not in tracked or not (root / name).is_file() or digest_file(root / name, text=True) != digest:
                raise ValueError(f"Tracked input differs from the asset snapshot: {name}. Use the matching checkout.")
        for name in manifest["directories"]:
            path = destination(root, name)
            if path.exists() and not path.is_dir():
                raise ValueError(f"Expected a directory: {name}")
        missing = []
        for name, record in manifest["files"].items():
            path = destination(root, name)
            if name in tracked:
                raise ValueError(f"Refusing to replace tracked source: {name}")
            info = bundle.getinfo(name)
            if info.file_size != record["size"] or (info.external_attr >> 16) & 0o170000 == 0o120000:
                raise ValueError(f"Invalid ZIP asset: {name}")
            if path.exists():
                if not path.is_file() or digest_file(path) != record["sha256"]:
                    raise ValueError(f"Existing local asset differs; preserved without changes: {name}")
            else:
                missing.append(name)
            digest = hashlib.sha256()
            with bundle.open(info) as source:
                for chunk in iter(lambda: source.read(1024 * 1024), b""):
                    digest.update(chunk)
            if digest.hexdigest() != record["sha256"]:
                raise ValueError(f"Archive checksum mismatch: {name}")
        print(f"Verified {len(manifest['files']):,} assets; {len(missing):,} need importing.", flush=True)
        print(f"Snapshot source commit: {manifest['sourceCommit']}")
        if dry_run:
            print("Dry run complete; no files written.")
            return manifest
        for name in manifest["directories"]:
            destination(root, name).mkdir(parents=True, exist_ok=True)
        for index, name in enumerate(missing, 1):
            path = destination(root, name)
            path.parent.mkdir(parents=True, exist_ok=True)
            with bundle.open(name) as source, path.open("xb") as target:
                shutil.copyfileobj(source, target, length=1024 * 1024)
            if index % 2000 == 0:
                print(f"Imported {index:,}/{len(missing):,} files.", flush=True)
        print("Asset import complete. Build Shared, run tools/check_unity_setup.py, then open Unity.")
        return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    subcommands = parser.add_subparsers(dest="command", required=True)
    for name in ("export", "import"):
        command = subcommands.add_parser(name)
        command.add_argument("archive", type=Path, help="Private ZIP archive path")
        command.add_argument("--root", type=Path, default=Path.cwd(), help="SLOPArena repository root")
        if name == "import":
            command.add_argument("--dry-run", action="store_true", help="Verify archive and destination without writing")
    args = parser.parse_args()
    try:
        root = checkout(args.root)
        if args.command == "export":
            export_bundle(root, args.archive)
        else:
            import_bundle(root, args.archive, args.dry_run)
        return 0
    except (OSError, ValueError, KeyError, TypeError, subprocess.CalledProcessError, zipfile.BadZipFile) as error:
        print(f"Asset bundle error: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
