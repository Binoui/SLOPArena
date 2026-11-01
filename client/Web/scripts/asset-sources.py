#!/usr/bin/env python3
"""Which web-demo fighter models are stale against their Unity sources.

A fighter's fingerprint hashes every Unity file its glb is built from. It starts at the catalog's rig prefab,
weapon config and bound clips, then follows GUID references through prefabs, materials, meshes, textures and FBX
files, including each file's .meta importer settings. It also hashes the converter itself. Presentation prefabs are
left out because CI re-exports effects on every deploy. client/Web/assets.sources.json records the fingerprint each
glb in the pinned bundle was built from.

    python3 client/Web/scripts/asset-sources.py check            # report stale fighters; exit 1 if any
    python3 client/Web/scripts/asset-sources.py check --github   # the same as GitHub warnings, exit 0
    python3 client/Web/scripts/asset-sources.py stale            # stale package names, one per line
    python3 client/Web/scripts/asset-sources.py dirty nyx        # those whose sources have uncommitted changes
    python3 client/Web/scripts/asset-sources.py stamp nyx bonk   # record the current sources after re-exporting
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
ASSETS = REPO / 'client/Unity/Assets'
PACKAGES = ASSETS / 'CharacterPackages'
LOCK = REPO / 'client/Web/assets.sources.json'
CONVERTER = REPO / 'client/Web/tools/fighter_glb.py'
GUID = re.compile(rb'guid: ([0-9a-f]{32})')
_index: dict[bytes, Path] | None = None


def guid_index() -> dict[bytes, Path]:
    """GUIDs of the files Git sees, tracked or new. Gitignored local files are left out, so a fingerprint
    computed here matches the one CI computes from a clean checkout."""
    global _index
    if _index is None:
        _index = {}
        listed = subprocess.run(['git', 'ls-files', '-z', '--cached', '--others', '--exclude-standard', '--',
                                 str(ASSETS.relative_to(REPO))], cwd=REPO, capture_output=True, check=True).stdout
        metas = [REPO / p for p in listed.decode().split('\0') if p.endswith('.meta')]
        for meta in metas:
            if not meta.is_file():
                continue
            match = re.search(rb'^guid: ([0-9a-f]{32})', meta.read_bytes()[:400], re.M)
            if match:
                _index[match[1]] = meta.with_suffix('')
    return _index


def packages() -> list[str]:
    return sorted(p.parent.name for p in PACKAGES.glob('*/CharacterAssetCatalog.asset')
                  if (REPO / 'content-cooked' / p.parent.name / 'client.bindings').exists())


def sources(package: str) -> list[Path]:
    """The catalog plus every file reachable from its rig, weapon config and bound clips."""
    catalog = PACKAGES / package / 'CharacterAssetCatalog.asset'
    text = catalog.read_bytes()
    text = text.split(b'\n  _presentations:', 1)[0]
    index, seen, queue = guid_index(), {catalog}, list(GUID.findall(text))
    while queue:
        path = index.get(queue.pop())
        if path is None or path in seen or not path.is_file():
            continue  # built-in, package or gitignored assets, folders
        seen.add(path)
        data = path.read_bytes()
        if data.startswith(b'%YAML'):
            queue.extend(GUID.findall(data))
    return sorted(seen)


def fingerprint(package: str) -> str:
    sha = hashlib.sha256(b'converter ' + hashlib.sha256(CONVERTER.read_bytes()).hexdigest().encode() + b'\n')
    for path in sources(package):
        meta = path.with_name(path.name + '.meta')
        sha.update(f'{path.relative_to(REPO).as_posix()} {hashlib.sha256(path.read_bytes()).hexdigest()} '
                   f'{hashlib.sha256(meta.read_bytes()).hexdigest() if meta.exists() else "-"}\n'.encode())
    return sha.hexdigest()


def recorded() -> dict[str, str]:
    return json.loads(LOCK.read_text())['fighters'] if LOCK.exists() else {}


def stale() -> list[str]:
    pinned = recorded()
    return [p for p in packages() if pinned.get(p) != fingerprint(p)]


def dirty(names: list[str]) -> list[str]:
    out = subprocess.run(['git', 'status', '--porcelain', '-z', '--', 'client/Unity/Assets'], cwd=REPO,
                         capture_output=True, check=True).stdout.decode()
    changed = {REPO / entry[3:] for entry in out.split('\0') if len(entry) > 3}
    result = []
    for name in names:
        files = set(sources(name))
        files |= {f.with_name(f.name + '.meta') for f in files}
        if files & changed:
            result.append(name)
    return result


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('command', choices=['check', 'stale', 'dirty', 'stamp'])
    parser.add_argument('packages', nargs='*')
    parser.add_argument('--github', action='store_true', help='emit GitHub warning annotations and exit 0')
    args = parser.parse_args(argv)
    if args.command == 'stale':
        print('\n'.join(stale()))
    elif args.command == 'dirty':
        print('\n'.join(dirty(args.packages or packages())))
    elif args.command == 'stamp':
        if not args.packages:
            parser.error('stamp needs the packages you re-exported')
        pinned = recorded()
        pinned.update({p: fingerprint(p) for p in args.packages})
        LOCK.write_text(json.dumps({'fighters': dict(sorted(pinned.items()))}, indent=1) + '\n')
    else:
        names = stale()
        for name in names:
            message = (f'Web demo model for {name} is older than its Unity sources (rig, clips or converter). '
                       f'Run client/Web/scripts/refresh-assets.sh and commit the new locks.')
            print(f'::warning title=Stale web demo fighter::{message}' if args.github else message)
        if not names:
            print('Web demo fighter models match their Unity sources.')
        return 0 if args.github or not names else 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
