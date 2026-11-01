#!/usr/bin/env python3
"""Renumber the three normals from the ADR-0042 ids 1/3/4 to 1/2/3.

ADR-0042 merged the old normals 1 and 2 but kept the ids 1, 3 and 4, so the move players
press second was `ground.3` / `g3` / `AbilitySlots.Slot3`. This tool moves every id to the
number players see. One pass rewrites, all at once:

  * canonical slot ids:      ground.3 -> ground.2, ground.4 -> ground.3 (air likewise)
  * semantic clip ids:       anim.<pkg>[.<form>].g3 -> .g2, taken per package from the slots
                             that use them (Bashie's slot 1 plays anim.bashie.g2 -> .g1)
  * HUD icon ids and files:  icon.3 -> icon.2; icons no slot uses are deleted
  * GenRA brief file names; briefs of retired moves are deleted
Clip files keep their names: they bind by GUID, and most are gitignored local files.
  * code names:              AbilitySlots.Slot3 -> Normal2, input action "Slot3" -> "Normal2",
                             TutorialObjectiveId.HitNormal3 -> HitNormal2, web "slot3" -> "slot2"
  * cooked packages:         ids patched in place, hashes resealed, roster repinned
  * web demo assets:         when client/Web/assets is present, glb clip names, report JSON and
                             HUD icons; then `client/Web/scripts/assets.sh upload` repins them

Source and cooked packages migrate per file, gated on their schema version (authoring 3 -> 4,
cooked 4 -> 5), so a straggler character.json can be migrated later with `--packages-only`.
The repo-wide code pass runs once: it refuses a tree whose AbilitySlots already has Normal2.

Usage:
  python3 tools/slot_renumber.py [--root DIR] [--dry-run]
  python3 tools/slot_renumber.py --packages-only [--dry-run]
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import struct
import subprocess
import sys
from pathlib import Path

NEW_NUMBER = {'1': '1', '3': '2', '4': '3'}
OLD_AUTHORING, NEW_AUTHORING = 3, 4
OLD_COOKED, NEW_COOKED = 4, 5
PAYLOADS = ('character.runtime.json', 'poses.bin', 'client.bindings')
# The web bundle is untracked, so a marker inside it stops a second renumbering.
WEB_MARKER = 'normals-1-2-3.txt'

TEXT_SUFFIXES = {'.cs', '.py', '.js', '.mjs', '.ts', '.json', '.asset', '.prefab', '.uxml', '.uss',
                 '.template', '.bindings', '.yaml', '.yml', '.txt', '.md', '.html'}
# Prose and history keep their wording; they are reviewed by hand. Test fixtures are generated output.
CODE_PASS_SKIP = re.compile(r'^(docs/|artifacts/|tmp/|content-cooked/|tools/(test_)?slot_renumber\.py$)|\.md$')
FIXTURE_MD = re.compile(r'^tests/.*/Fixtures/.*\.md$')

SLOT_ID = re.compile(r'\b(ground|air)\.([1-4])(?!\w)')
ANIM_ID = re.compile(r'\banim\.([a-z]+)((?:\.[a-z]+)?)\.([ga])([1-4])(?!\w)')
ICON_ID = re.compile(r'"iconId"(\s*:\s*)"icon\.([1-4])"')
CODE_RENAMES = [
    (re.compile(r'\bAbilitySlots\.Slot([1-5])\b'),
     lambda m: 'AbilitySlots.' + {'1': 'Normal1', '2': 'RetiredNormal', '3': 'Normal2', '4': 'Normal3',
                                  '5': 'RetiredFifthNormal'}[m.group(1)]),
    (re.compile(r'"Slot([134])"'), lambda m: f'"Normal{NEW_NUMBER[m.group(1)]}"'),
    (re.compile(r'\bHitNormal([134])\b'), lambda m: f'HitNormal{NEW_NUMBER[m.group(1)]}'),
]
SLOT_CONSTANTS = re.compile(r'\bconst byte Slot([1-5])\b')
HUD_ELEMENT = re.compile(r'\bab-([134])\b')
WEB_SLOT = re.compile(r'\bslot([134])\b')
NYX_MOVE = re.compile(r'\b(real|fake)-([ga])([1-4])\b')


def alternation(words) -> re.Pattern:
    return re.compile('|'.join(re.escape(w) for w in sorted(words, key=len, reverse=True)) or r'(?!)')


def tracked_files(root: Path) -> list[str]:
    out = subprocess.run(['git', 'ls-files', '-z', '--cached', '--others', '--exclude-standard'],
                         cwd=root, check=True, capture_output=True).stdout
    return sorted({p for p in out.decode().split('\0') if p and (root / p).is_file()})


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


class Plan:
    def __init__(self, root: Path, dry_run: bool):
        self.root, self.dry_run = root, dry_run
        self.anim_maps: dict[str, dict[str, str]] = {}   # package -> {'g3': 'g2', ...}
        self.icon_maps: dict[str, dict[str, str]] = {}   # package -> {'3': '2', ...}
        self.used_icons: dict[str, set[str]] = {}
        self.writes: dict[str, bytes] = {}
        self.renames: dict[str, str] = {}
        self.deletes: list[str] = []
        self.review: list[str] = []

    # ---- maps ---------------------------------------------------------------------------
    def derive_maps(self, character_json: Path, package: str) -> bool:
        data = json.loads(character_json.read_text())
        if data.get('authoringSchemaVersion') != OLD_AUTHORING:
            return False
        anim_map: dict[str, str] = {}
        icon_map: dict[str, str] = {}
        used: set[str] = set()

        def visit(slots):
            for slot in slots or []:
                match = re.fullmatch(r'(ground|air)\.(\w)', slot.get('id', ''))
                if not match:
                    continue
                old = match.group(2)
                icon = re.fullmatch(r'icon\.([1-4])', slot.get('iconId') or '')
                if old not in NEW_NUMBER:
                    if icon:
                        used.add(icon.group(1))
                    continue
                new = NEW_NUMBER[old]
                for anim in ANIM_ID.finditer(json.dumps(slot)):
                    key, target = anim.group(3) + anim.group(4), anim.group(3) + new
                    if anim_map.setdefault(key, target) != target:
                        raise SystemExit(f'{package}: clip {anim.group(0)} serves two normals')
                if icon:
                    used.add(icon.group(1))
                    if icon_map.setdefault(icon.group(1), new) != new:
                        raise SystemExit(f'{package}: icon.{icon.group(1)} serves two normals')

        visit(data.get('slots'))
        visit((data.get('alternateForm') or {}).get('slots'))
        for letter in 'ga':
            for old, new in NEW_NUMBER.items():
                if letter + old not in anim_map and letter + new not in anim_map.values():
                    anim_map[letter + old] = letter + new
        self.anim_maps[package] = anim_map
        self.icon_maps[package] = icon_map
        self.used_icons[package] = used
        return True

    def map_anim(self, package: str, form: str, letter: str, digit: str) -> str | None:
        mapped = self.anim_maps.get(package, {}).get(letter + digit)
        return None if mapped is None or mapped == letter + digit else f'anim.{package}{form}.{mapped}'

    # ---- text transforms ----------------------------------------------------------------
    def slot_ids(self, text: str, retired: str = '2') -> str:
        numbers = {**NEW_NUMBER, '2': retired}
        return SLOT_ID.sub(lambda m: f'{m.group(1)}.{numbers[m.group(2)]}', text)

    def anim_ids(self, text: str) -> str:
        return ANIM_ID.sub(lambda m: self.map_anim(*m.group(1, 2, 3, 4)) or m.group(0), text)

    def pose_names(self, data: bytes) -> bytes:
        """Rename the clip tracks of a SKEL poses.bin (CharacterPackageAssembler.ParsePoses)."""
        out = bytearray(data)
        _, _, bones, tracks = struct.unpack_from('<4I', data)
        offset = 16

        def name_at(at):
            size = struct.unpack_from('<I', data, at)[0]
            return data[at + 4: at + 4 + size].decode(), at + 4 + size

        for _ in range(bones):
            offset = name_at(offset)[1]
        for _ in range(tracks):
            name, end = name_at(offset)
            renamed = self.anim_ids(name).encode()
            assert len(renamed) == end - offset - 4, f'{name} must keep its length'
            out[offset + 4: end] = renamed
            frames = struct.unpack_from('<I', data, end)[0]
            offset = end + 4 + frames * bones * 3 * 4
        assert offset == len(data), 'poses.bin has trailing bytes'
        return bytes(out)

    def icon_ids(self, package: str, text: str) -> str:
        icon_map = self.icon_maps.get(package, {})
        return ICON_ID.sub(lambda m: f'"iconId"{m.group(1)}"icon.{icon_map.get(m.group(2), m.group(2))}"', text)

    # ---- passes -------------------------------------------------------------------------
    def packages(self):
        for character_json in sorted(self.root.glob('client/Unity/Assets/CharacterPackages/*/character.json')):
            package = character_json.parent.name
            if not self.derive_maps(character_json, package):
                continue
            text = character_json.read_text()
            text = self.icon_ids(package, self.anim_ids(self.slot_ids(text)))
            text, count = re.subn(rf'("authoringSchemaVersion"\s*:\s*){OLD_AUTHORING}\b', rf'\g<1>{NEW_AUTHORING}', text, count=1)
            assert count == 1, character_json
            self.writes[str(character_json.relative_to(self.root))] = text.encode()

    def icon_files(self, folder: Path, suffix: str, metas: bool):
        if not folder.is_dir():
            return
        dirs = {p.name.lower(): p for p in folder.iterdir() if p.is_dir()}
        for package, icon_map in self.icon_maps.items():
            if package not in dirs:
                continue
            for icon in sorted(dirs[package].rglob(f'icon.[1-4]{suffix}')):
                digit = icon.name[5]
                rel = str(icon.relative_to(self.root))
                pair = [rel, rel + '.meta'] if metas else [rel]
                if digit not in self.used_icons[package]:
                    self.deletes += pair
                elif icon_map.get(digit, digit) != digit:
                    target = str(icon.with_name(f'icon.{icon_map[digit]}{suffix}').relative_to(self.root))
                    for src, dst in zip(pair, [target, target + '.meta'] if metas else [target]):
                        self.renames[src] = dst

    def web_assets(self):
        """The web demo's untracked asset bundle names clips and icons like the Unity client."""
        assets = self.root / 'client/Web/assets'
        marker = assets / WEB_MARKER
        if not assets.is_dir() or marker.exists():
            return
        self.writes[str(marker.relative_to(self.root))] = b'Normals numbered 1/2/3 by tools/slot_renumber.py.\n'
        for glb in sorted((assets / 'fighters').glob('*.glb')):
            data = bytearray(glb.read_bytes())
            size, kind = struct.unpack_from('<II', data, 12)
            assert kind == 0x4E4F534A, f'{glb} must start with a JSON chunk'
            renamed = self.anim_ids(data[20:20 + size].decode()).encode()
            assert len(renamed) == size, f'{glb} clip names must keep their length'
            data[20:20 + size] = renamed
            if bytes(data) != glb.read_bytes():
                self.writes[str(glb.relative_to(self.root))] = bytes(data)
        for report in sorted((assets / 'fighters').glob('*.json')):
            text = report.read_text()
            if self.anim_ids(text) != text:
                self.writes[str(report.relative_to(self.root))] = self.anim_ids(text).encode()
        self.icon_files(assets / 'ui/abilities', '.webp', metas=False)

    def package_files(self, files: list[str]):
        """Icon and brief files named after a numbered normal."""
        self.icon_files(self.root / 'client/Unity/Assets/Resources/UI/AbilityCards', '.png', metas=True)

        for rel in files:
            brief = re.match(r'tools/genra/briefs/([a-z]+)((?:-[a-z]+)?)-([ga])([1-4])((?:-.+)?\.json)$', rel)
            if brief and brief.group(1) in self.anim_maps:
                package, form, letter, digit, rest = brief.groups()
                mapped = self.anim_maps[package].get(letter + digit)
                if mapped is None and digit == '2':
                    # A retired move's brief would now target the up normal's clip id.
                    self.deletes.append(rel)
                    continue
                if mapped and mapped != letter + digit:
                    target = f'{package}{form}-{mapped}{rest}'
                else:
                    continue
                self.renames[rel] = 'tools/genra/briefs/' + target

    def code(self, files: list[str]):
        # A brief's name also names its GenRA run folders, which keep their history.
        briefs = {src: (Path(src).stem, Path(dst).stem) for src, dst in self.renames.items() if src.startswith('tools/genra/briefs/')}
        # One alternation per pass, so a chain such as g3 -> g2, g4 -> g3 never renames twice.
        path_targets = {}
        for src, dst in self.renames.items():
            if not src.endswith('.meta'):
                path_targets[src] = dst
                if src.startswith('client/Unity/'):
                    path_targets[src.removeprefix('client/Unity/')] = dst.removeprefix('client/Unity/')
        briefs_by_name = {Path(src).name: Path(dst).name for src, dst in self.renames.items() if src.startswith('tools/genra/briefs/')}
        path_names = alternation(path_targets)
        brief_names = alternation(briefs_by_name)
        for rel in files:
            path = self.root / rel
            if path.suffix not in TEXT_SUFFIXES or rel in self.writes:
                continue
            try:
                original = path.read_text()
            except UnicodeDecodeError:
                continue
            if CODE_PASS_SKIP.search(rel) and not FIXTURE_MD.match(rel):
                # Prose keeps its numbers, but links follow renamed files.
                text = brief_names.sub(lambda m: briefs_by_name[m.group(0)], original) if path.suffix == '.md' else original
                if text != original:
                    self.writes[rel] = text.encode()
                continue
            text = original
            if re.search(r'\b(ground|air)\.2(?!\w)', text) and path.suffix != '.cs' and rel not in self.deletes:
                self.review.append(f'{rel}: names the retired slot 2, which now means the up normal')
            # Code names ground.2 only as the rejected id; ground.4 takes over that role.
            text = self.anim_ids(self.slot_ids(text, retired='4' if path.suffix == '.cs' else '2'))
            for pattern, repl in CODE_RENAMES:
                text = pattern.sub(repl, text)
            if rel == 'src/Shared/AbilitySlots.cs':
                text = SLOT_CONSTANTS.sub(lambda m: 'const byte ' + {'1': 'Normal1', '2': 'RetiredNormal', '3': 'Normal2', '4': 'Normal3', '5': 'RetiredFifthNormal'}[m.group(1)], text)
            if path.suffix in ('.cs', '.uxml', '.uss') and rel.startswith('client/Unity/'):
                text = HUD_ELEMENT.sub(lambda m: f'ab-{NEW_NUMBER[m.group(1)]}', text)
            if re.match(r'client/Web/(app|scripts|sim)/', rel):
                text = WEB_SLOT.sub(lambda m: f'slot{NEW_NUMBER[m.group(1)]}', text)
            if rel.startswith(('tools/nyx/', 'client/Unity/Assets/CharacterPackages/nyx/')):
                text = NYX_MOVE.sub(lambda m: f'{m.group(1)}-{self.anim_maps["nyx"].get(m.group(2) + m.group(3), m.group(2) + m.group(3))}', text)
            text = path_names.sub(lambda m: path_targets[m.group(0)], text)
            if rel in briefs:
                text = text.replace(f'"{briefs[rel][0]}"', f'"{briefs[rel][1]}"')
            if text != original:
                self.writes[rel] = text.encode()

    def cooked(self):
        roster_path = self.root / 'content-cooked/roster/manifest.json'
        roster = roster_path.read_text()
        for manifest_path in sorted(self.root.glob('content-cooked/*/manifest.json')):
            package = manifest_path.parent.name
            if package not in self.anim_maps:
                continue
            manifest = manifest_path.read_text()
            if f'"cookedSchemaVersion":{OLD_COOKED},' not in manifest:
                continue
            payload = {}
            for name in PAYLOADS:
                data = (manifest_path.parent / name).read_bytes()
                if name == 'poses.bin':
                    data = self.pose_names(data)
                else:
                    text = self.anim_ids(self.slot_ids(data.decode()))
                    if name == 'character.runtime.json':
                        text = self.icon_ids(package, text)
                        text = text.replace(f'"cookedSchemaVersion":{OLD_COOKED},', f'"cookedSchemaVersion":{NEW_COOKED},', 1)
                    data = text.encode()
                payload[name] = data
            framed = b''.join(struct.pack('<I', len(n.encode())) + n.encode() + struct.pack('<Q', len(d)) + d
                              for n, d in payload.items())
            old_cooked = json.loads(manifest)['cookedContentHash']
            old_package = json.loads(manifest)['packageHash']
            manifest = manifest.replace(f'"authoringSchemaVersion":{OLD_AUTHORING},', f'"authoringSchemaVersion":{NEW_AUTHORING},', 1)
            manifest = manifest.replace(f'"cookedSchemaVersion":{OLD_COOKED},', f'"cookedSchemaVersion":{NEW_COOKED},', 1)
            for name, data in payload.items():
                manifest = re.sub(rf'("path":"{re.escape(name)}","sha256":")[0-9a-f]{{64}}(","size":)\d+',
                                  rf'\g<1>{sha(data)}\g<2>{len(data)}', manifest)
            cooked_hash = sha(framed)
            manifest = manifest.replace(f'"cookedContentHash":"{old_cooked}"', f'"cookedContentHash":"{cooked_hash}"', 1)
            placeholder = manifest.replace(f'"packageHash":"{old_package}"', '"packageHash":""', 1)
            package_hash = sha(framed + placeholder.encode())
            manifest = placeholder.replace('"packageHash":""', f'"packageHash":"{package_hash}"', 1)
            for name, data in payload.items():
                self.writes[f'content-cooked/{package}/{name}'] = data
            self.writes[f'content-cooked/{package}/manifest.json'] = manifest.encode()
            roster = roster.replace(old_cooked, cooked_hash).replace(old_package, package_hash)
        self.writes['content-cooked/roster/manifest.json'] = roster.encode()

    # ---- apply --------------------------------------------------------------------------
    def check_collisions(self, files: list[str]):
        existing = set(files)
        targets = set(self.renames.values())
        for src, dst in self.renames.items():
            if dst in existing and dst not in self.renames and dst not in self.deletes:
                raise SystemExit(f'rename target exists and is not moving: {src} -> {dst}')
        if len(targets) != len(self.renames):
            raise SystemExit('two files rename to the same target')

    def apply(self):
        for rel, data in sorted(self.writes.items()):
            print(f'write  {rel}')
            if not self.dry_run:
                (self.root / rel).write_bytes(data)
        for rel in self.deletes:
            print(f'delete {rel}')
            if not self.dry_run:
                (self.root / rel).unlink()
        staged = {src: self.root / f'{src}.renumber-tmp' for src in self.renames}
        for src, tmp in staged.items():
            print(f'rename {src} -> {self.renames[src]}')
            if not self.dry_run:
                (self.root / src).rename(tmp)
        for src, tmp in staged.items():
            if self.dry_run:
                continue
            tmp.rename(self.root / self.renames[src])
        for line in self.review:
            print(f'review {line}')


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--root', type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument('--dry-run', action='store_true')
    parser.add_argument('--packages-only', action='store_true',
                        help='migrate schema-3 character.json files and their cooks; skip the code pass')
    args = parser.parse_args()
    root = args.root.resolve()
    plan = Plan(root, args.dry_run)
    files = tracked_files(root)
    plan.packages()
    if not plan.anim_maps:
        print('No authoring-schema-3 packages left to migrate.')
    if not args.packages_only:
        if 'Normal2' in (root / 'src/Shared/AbilitySlots.cs').read_text():
            raise SystemExit('The code pass already ran here (AbilitySlots has Normal2); use --packages-only.')
        plan.package_files(files)
        plan.web_assets()
        plan.check_collisions(files)
        plan.code(files)
    plan.cooked()
    plan.apply()
    return 0


if __name__ == '__main__':
    sys.exit(main())
