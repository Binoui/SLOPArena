"""Rig packs: the self-contained inputs for one character, with a hash manifest.

Layout: rig.usda (skeleton + skinned meshes), references/<name>.usda, textures/,
images/, manifest.json. A brief points at a rig pack; the authoring sandbox may read
only the rig pack, the toolkit and its Python environment.
"""
from __future__ import annotations

import hashlib
import json
import shutil
from pathlib import Path


def sha256(path) -> str:
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def make_rigpack(out_dir, rig_usd, references: dict, images: dict | None = None, textures=None, notes='') -> dict:
    out = Path(out_dir)
    if out.exists() and any(out.iterdir()):
        raise SystemExit(f'{out} is not empty')
    (out / 'references').mkdir(parents=True)
    copies = {'rig.usda': rig_usd}
    copies.update({f'references/{k}.usda': v for k, v in references.items()})
    copies.update({f'images/{k}': v for k, v in (images or {}).items()})
    for rel, src in copies.items():
        (out / rel).parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(src, out / rel)
    if textures:
        shutil.copytree(textures, out / 'textures')
    files = sorted(p for p in out.rglob('*') if p.is_file())
    manifest = {'notes': notes, 'sources': {k: str(v) for k, v in copies.items()},
                'files': {str(p.relative_to(out)): sha256(p) for p in files}}
    (out / 'manifest.json').write_text(json.dumps(manifest, indent=2))
    return manifest


def verify_rigpack(rigpack) -> dict:
    root = Path(rigpack)
    manifest = json.loads((root / 'manifest.json').read_text())
    changed = [rel for rel, digest in manifest['files'].items() if sha256(root / rel) != digest]
    if changed:
        raise SystemExit(f'Rig pack files changed since the manifest: {changed}')
    return manifest
