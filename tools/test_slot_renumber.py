import json
import struct
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

TOOL = Path(__file__).resolve().parent / 'slot_renumber.py'


def poses(names, bones=('hips',)):
    data = struct.pack('<4I', 0x4C454B53, 1, len(bones), len(names))
    for bone in bones:
        data += struct.pack('<I', len(bone)) + bone.encode()
    for name in names:
        data += struct.pack('<I', len(name)) + name.encode() + struct.pack('<I', 1) + b'\0' * 12 * len(bones)
    return data


def slot(slot_id, anim, icon):
    return {'id': slot_id, 'iconId': icon, 'timeline': {'animationId': anim}}


class SlotRenumberTests(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp())
        pkg = self.root / 'client/Unity/Assets/CharacterPackages/kit'
        pkg.mkdir(parents=True)
        (pkg / 'character.json').write_text(json.dumps({'authoringSchemaVersion': 3, 'slots': [
            slot('ground.1', 'anim.kit.g2', 'icon.2'), slot('ground.3', 'anim.kit.g3', 'icon.3'),
            slot('ground.4', 'anim.kit.g4', 'icon.4'), slot('air.3', 'anim.kit.a3', 'icon.3')]}, indent=2))
        cards = self.root / 'client/Unity/Assets/Resources/UI/AbilityCards/Kit'
        cards.mkdir(parents=True)
        for digit in '1234':
            (cards / f'icon.{digit}.png').write_bytes(digit.encode())
            (cards / f'icon.{digit}.png.meta').write_text(digit)
        cooked = self.root / 'content-cooked/kit'
        cooked.mkdir(parents=True)
        (cooked / 'character.runtime.json').write_text(
            '{"metadata":{"cookedSchemaVersion":4,"x":1},"slots":[{"id":"ground.3","iconId":"icon.3"}]}')
        (cooked / 'poses.bin').write_bytes(poses(['anim.kit.a3', 'anim.kit.g2', 'anim.kit.g3', 'anim.kit.g4']))
        (cooked / 'client.bindings').write_text('{"animations":[{"semanticId":"anim.kit.g3"}]}')
        payloads = ','.join(f'{{"path":"{n}","sha256":"{"0" * 64}","size":0}}'
                            for n in ('character.runtime.json', 'poses.bin', 'client.bindings'))
        (cooked / 'manifest.json').write_text(
            f'{{"authoringSchemaVersion":3,"cookedSchemaVersion":4,"cookedContentHash":"{"a" * 64}",'
            f'"packageHash":"{"b" * 64}","payloads":[{payloads}]}}')
        (self.root / 'content-cooked/roster').mkdir()
        (self.root / 'content-cooked/roster/manifest.json').write_text(f'{{"kit":["{"a" * 64}","{"b" * 64}"]}}')
        fighters = self.root / 'client/Web/assets/fighters'
        fighters.mkdir(parents=True)
        chunk = json.dumps({'animations': [{'name': 'anim.kit.g3'}, {'name': 'anim.kit.g4'}]}).encode()
        (fighters / 'kit.glb').write_bytes(struct.pack('<III', 0x46546C67, 2, 20 + len(chunk))
                                           + struct.pack('<II', len(chunk), 0x4E4F534A) + chunk)
        shared = self.root / 'src/Shared'
        shared.mkdir(parents=True)
        (shared / 'AbilitySlots.cs').write_text('public const byte Slot1 = 3; public const byte Slot3 = 8; public const byte Slot4 = 9;')
        (shared / 'Use.cs').write_text('var s = AbilitySlots.Slot3; Reject("ground.2"); Load("air.4", "anim.kit.g4");')
        (self.root / '.gitignore').write_text('client/Web/assets/\n')
        subprocess.run(['git', 'init', '-q'], cwd=self.root, check=True)
        subprocess.run(['git', 'add', '-A'], cwd=self.root, check=True)

    def run_tool(self, *args):
        return subprocess.run([sys.executable, str(TOOL), '--root', str(self.root), *args],
                              capture_output=True, text=True)

    def test_renumbers_source_code_icons_and_cook(self):
        self.assertEqual(self.run_tool().returncode, 0)
        source = json.loads((self.root / 'client/Unity/Assets/CharacterPackages/kit/character.json').read_text())
        self.assertEqual(source['authoringSchemaVersion'], 4)
        self.assertEqual([(s['id'], s['timeline']['animationId'], s['iconId']) for s in source['slots']], [
            ('ground.1', 'anim.kit.g1', 'icon.1'), ('ground.2', 'anim.kit.g2', 'icon.2'),
            ('ground.3', 'anim.kit.g3', 'icon.3'), ('air.2', 'anim.kit.a2', 'icon.2')])
        cards = self.root / 'client/Unity/Assets/Resources/UI/AbilityCards/Kit'
        self.assertEqual({p.name: p.read_bytes() for p in cards.glob('*.png')},
                         {'icon.1.png': b'2', 'icon.2.png': b'3', 'icon.3.png': b'4'})
        self.assertEqual((self.root / 'src/Shared/Use.cs').read_text(),
                         'var s = AbilitySlots.Normal2; Reject("ground.4"); Load("air.3", "anim.kit.g3");')
        self.assertIn('const byte Normal3 = 9', (self.root / 'src/Shared/AbilitySlots.cs').read_text())

        cooked = self.root / 'content-cooked/kit'
        pose = (cooked / 'poses.bin').read_bytes()
        self.assertEqual(pose, poses(['anim.kit.a2', 'anim.kit.g1', 'anim.kit.g2', 'anim.kit.g3']))
        manifest = json.loads((cooked / 'manifest.json').read_text())
        self.assertEqual((manifest['authoringSchemaVersion'], manifest['cookedSchemaVersion']), (4, 5))
        self.assertIn('"cookedSchemaVersion":5', (cooked / 'character.runtime.json').read_text())
        self.assertIn('"ground.2"', (cooked / 'character.runtime.json').read_text())
        roster = (self.root / 'content-cooked/roster/manifest.json').read_text()
        self.assertIn(manifest['cookedContentHash'], roster)
        self.assertIn(manifest['packageHash'], roster)

    def web_clip_names(self):
        data = (self.root / 'client/Web/assets/fighters/kit.glb').read_bytes()
        size = struct.unpack_from('<I', data, 12)[0]
        return [a['name'] for a in json.loads(data[20:20 + size])['animations']]

    def test_web_bundle_renumbers_once(self):
        self.assertEqual(self.run_tool().returncode, 0)
        self.assertEqual(self.web_clip_names(), ['anim.kit.g2', 'anim.kit.g3'])
        # A fresh checkout of the old tree next to an already renumbered local bundle.
        subprocess.run(['git', 'checkout', '-q', '--', '.'], cwd=self.root, check=True)
        subprocess.run(['git', 'clean', '-qfd'], cwd=self.root, check=True)
        self.assertEqual(self.run_tool().returncode, 0)
        self.assertEqual(self.web_clip_names(), ['anim.kit.g2', 'anim.kit.g3'])

    def test_second_run_refuses_the_code_pass_and_finds_no_packages(self):
        self.assertEqual(self.run_tool().returncode, 0)
        again = self.run_tool()
        self.assertNotEqual(again.returncode, 0)
        self.assertIn('already ran', again.stderr)
        self.assertIn('No authoring-schema-3 packages', self.run_tool('--packages-only').stdout)
        self.assertEqual((self.root / 'src/Shared/Use.cs').read_text(),
                         'var s = AbilitySlots.Normal2; Reject("ground.4"); Load("air.3", "anim.kit.g3");')


if __name__ == '__main__':
    unittest.main()
