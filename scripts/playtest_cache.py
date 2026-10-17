"""Opt-in, checked snapshots of settled release Libraries, copied into isolated projects."""
from contextlib import contextmanager
import fcntl
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import tempfile
import uuid

import playtest_timing


def fingerprint(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(",", ":")).encode()).hexdigest()


def files(root):
    """Reject symlinks and special files before reading a snapshot or input tree."""
    result = {}
    for path in sorted(root.rglob("*")):
        if path.is_symlink() or not (path.is_file() or path.is_dir()):
            raise ValueError("Snapshot contains an unsupported file")
        if path.is_file():
            result[path.relative_to(root).as_posix()] = hashlib.sha256(path.read_bytes()).hexdigest()
    return result


def compatibility(project, unity_version):
    """What can change every import at once. Source files are not part of it: tracked or
    local-only, Unity reimports each changed file and its dependents from a restored Library."""
    return {"schemaVersion": 2, "unityVersion": unity_version, "target": "StandaloneWindows64",
            "host": platform.system(), "architecture": platform.machine(),
            "packagesHash": fingerprint(files(project / "Packages")),
            "settingsHash": fingerprint(files(project / "ProjectSettings"))}


def local_changes(before, after, limit=20):
    """Local-only files that differ from the snapshot's, for the attempt's evidence."""
    changes = {"added": sorted(set(after) - set(before)),
               "removed": sorted(set(before) - set(after)),
               "changed": sorted(name for name in set(before) & set(after) if before[name] != after[name])}
    return {kind: {"count": len(names), "paths": names[:limit]} for kind, names in changes.items()}


class ImportCache:
    def __init__(self, directory):
        original = Path(directory).expanduser().absolute()
        if original != original.resolve():
            raise ValueError("Import cache path may not traverse symlinks")
        self.root = original
        self.root.mkdir(parents=True, exist_ok=True, mode=0o700)
        info = self.root.stat()
        if info.st_uid != os.getuid() or info.st_mode & 0o077:
            raise ValueError("Import cache must be private and owned by this user")

    @contextmanager
    def locked(self):
        fd = os.open(self.root / ".lock", os.O_CREAT | os.O_RDWR | os.O_NOFOLLOW, 0o600)
        try:
            fcntl.flock(fd, fcntl.LOCK_EX)
            yield
        finally:
            os.close(fd)

    def restore(self, project, inputs, local_inputs):
        key = fingerprint(inputs)
        library = project / "Library"
        if library.exists() or library.is_symlink():
            raise ValueError("Cache restore requires a fresh isolated Library")
        with self.locked():
            entry = self.root / key
            if not entry.exists():
                return {"status": "miss", "key": key}
            try:
                if entry.is_symlink() or (entry / "manifest.json").is_symlink():
                    raise ValueError("Invalid cache entry")
                manifest = json.loads((entry / "manifest.json").read_text())
                if manifest["compatibility"] != inputs or manifest["key"] != key:
                    raise ValueError("Cache compatibility changed")
                source = entry / "Library"
                if any((source / name).exists() for name in ("Pipeline", "SlopArena")):
                    raise ValueError("Cache contains Editor session state")
                if source.is_symlink() or not source.is_dir() or files(source) != manifest["files"]:
                    raise ValueError("Cache bytes differ from the settled snapshot")
                # Independent files only: every Editor must own its writable Library.
                shutil.copytree(source, library)
                if files(library) != manifest["files"]:
                    raise ValueError("Cache copy did not preserve snapshot bytes")
                return {"status": "hit", "key": key, "snapshotSourceRevision": manifest["sourceRevision"],
                        "localChanges": local_changes(manifest["localInputs"], local_inputs)}
            except (OSError, ValueError, KeyError, TypeError):
                if library.is_dir() and not library.is_symlink():
                    shutil.rmtree(library)
                return {"status": "rejected", "key": key, "reason": "invalid_snapshot"}

    def store(self, project, inputs, revision, local_inputs):
        library = project / "Library"
        if library.is_symlink() or not library.is_dir():
            raise ValueError("Settled isolated Library is missing")
        files(library)
        key = fingerprint(inputs)
        with self.locked(), tempfile.TemporaryDirectory(prefix=".snapshot-", dir=self.root) as directory:
            staged = Path(directory) / key
            staged.mkdir()
            # Connection/lease state and advisory cooked output belong to an Editor
            # session, not to reusable imported assets.
            def ignore(directory, names):
                return set(names) & {"Pipeline", "SlopArena"} if Path(directory) == library else set()
            shutil.copytree(library, staged / "Library", ignore=ignore)
            hashes = files(staged / "Library")
            manifest = {"key": key, "compatibility": inputs, "sourceRevision": revision,
                        "createdAtUtc": playtest_timing.utc_now(), "localInputs": local_inputs, "files": hashes}
            playtest_timing.atomic_json(staged / "manifest.json", manifest)
            entry = self.root / key
            old = self.root / (".previous-" + uuid.uuid4().hex)
            if entry.exists() or entry.is_symlink():
                os.replace(entry, old)
            try:
                os.replace(staged, entry)
            except BaseException:
                if old.exists() or old.is_symlink():
                    os.replace(old, entry)
                raise
            finally:
                if old.is_symlink():
                    old.unlink()
                elif old.is_dir():
                    shutil.rmtree(old)
            # Keep at most two compatibility snapshots. Never touch unrelated files.
            snapshots = [path for path in self.root.iterdir() if len(path.name) == 64 and
                         all(char in "0123456789abcdef" for char in path.name) and path.is_dir() and not path.is_symlink()]
            for path in sorted(snapshots, key=lambda item: item.stat().st_mtime, reverse=True)[2:]:
                shutil.rmtree(path)
            return {"status": "stored", "key": key, "fileCount": len(hashes)}
