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


def add_reference(rigpack, label: str, clip, source='') -> Path:
    """Carry an authored clip as references/<label>.usda, so a brief can hand off to a take that is not bound in Unity
    yet: Wibou's jump ends on his new fall loop's first key pose (`exit.reference`, 2026-11-10). The manifest gains the
    file's hash. A label is never replaced: briefs and approved keys read these poses, so a changed take gets a new
    label."""
    import re
    from .usdio import write_usd
    root = Path(rigpack)
    if not re.fullmatch(r'[a-z0-9]+', label):
        raise ValueError(f'reference labels take lowercase letters and digits, not {label!r}')
    rel = f'references/{label}.usda'
    if (root / rel).exists():
        raise FileExistsError(f'{root / rel} exists; give the new take another label')
    manifest = verify_rigpack(root)
    template = next(iter(sorted(root.glob('references/*.usda'))), None)  # skeleton only, like the pack's own
    write_usd(clip, root / rel, template=template)
    manifest['files'][rel] = sha256(root / rel)
    manifest.setdefault('authored', {})[rel] = str(source)
    (root / 'manifest.json').write_text(json.dumps(manifest, indent=2))
    return root / rel


def verify_rigpack(rigpack) -> dict:
    root = Path(rigpack)
    manifest = json.loads((root / 'manifest.json').read_text())
    changed = [rel for rel, digest in manifest['files'].items() if sha256(root / rel) != digest]
    if changed:
        raise SystemExit(f'Rig pack files changed since the manifest: {changed}')
    return manifest


def main(argv=None) -> int:
    """python -m genra.rigpack reference PACK LABEL RESULT.json: carry an authored take as references/LABEL.usda."""
    import argparse
    parser = argparse.ArgumentParser(prog='genra.rigpack')
    sub = parser.add_subparsers(dest='command', required=True)
    ref = sub.add_parser('reference', help='carry an authored result.json as references/LABEL.usda (never replaced)')
    ref.add_argument('rigpack')
    ref.add_argument('label')
    ref.add_argument('result')
    args = parser.parse_args(argv)
    from .clip import Clip
    from .rig import Rig
    clip = Clip.load(Rig(Path(args.rigpack) / 'rig.usda'), args.result)
    print(add_reference(args.rigpack, args.label, clip, source=Path(args.result).resolve()))
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
