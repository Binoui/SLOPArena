from __future__ import annotations

import contextlib
from dataclasses import replace
import hashlib
import io
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

from PIL import Image

import move_review as review


def fixture(root: Path, forms=(0,), views=('behind', 'front'), ticks=3) -> None:
    moves = []
    form_names = {str(f): ('Nyx' if f == 0 else 'Nyxxie') for f in forms}
    coverage = {str(f): {'ground.1': {'aliasOf': None, 'moves': [f'move-{f}']},
                         'air.A': {'aliasOf': 'ground.A', 'moves': []}} for f in forms}
    for form in forms:
        move = f'move-{form}'
        label = 'Shadow <Flick> & sparks'
        moves.append(dict(id=move, label=label, slot='ground.1', form=form, ticks=ticks, size=[64, 36]))
        path = root/'takes'/move
        path.mkdir(parents=True)
        hit = dict(frame=1, damage=7, blocked=False, target='100')
        receipt = dict(format=1, move=move, package='nyx', recipe='scenarios/moves/nyx.json', recipeSha256='a'*64,
                       slot='ground.1', form=form, formName=form_names[str(form)], label=label, ticks=ticks,
                       size=[64, 36], views=[dict(name=v, yaw=0, pitch=35) for v in views],
                       phases=[dict(name='peak', at='hit', frame=1), dict(name='fade', at='end', frame=ticks-1)],
                       hits=[hit], start=dict(frame=0), framesWritten=ticks*len(views), problems=[])
        review.write_json(path/'take.json', receipt)
        rows = []
        for frame in range(ticks):
            rows.append(dict(frame=frame, matchTick=33+frame, viewTicks={v:33+frame for v in views},
                             boxes={v:[20, 10, 40, 26] for v in views}, fighter=dict(id='1'),
                             hits=[] if frame != 1 else [dict(owner='1', target='100', damage=7, blocked=False)]))
            for view in views:
                folder = path/view
                folder.mkdir(exist_ok=True)
                image = Image.new('RGB', (64, 36))
                image.putdata([(x*3, y*5, frame*70) for y in range(36) for x in range(64)])
                image.save(folder/f'{frame:04d}.jpg', quality=95)
        (path/'frames.jsonl').write_text(''.join(json.dumps(r)+'\n' for r in rows))
    review.write_json(root/'plan.json', dict(format=1, package='nyx', recipe='scenarios/moves/nyx.json',
                                            recipeSha256='a'*64, formNames=form_names, coverage=coverage, moves=moves))


def hashes(root: Path) -> dict:
    return {str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in root.rglob('*') if p.is_file()}


class MoveReviewTests(unittest.TestCase):
    def test_recipe_reader_uses_only_ids_and_selection_preserves_recipe_order(self):
        with tempfile.TemporaryDirectory() as directory:
            recipe = Path(directory)/'recipe.json'
            recipe.write_text(json.dumps(dict(unrecognized={'not': 'Python semantics'}, moves=[
                dict(id='real-g1', input='opaque'), dict(id='fake-g1', pad=None), dict(id='fake-g3')])) )
            ids = review.recipe_move_ids(recipe)
            self.assertEqual(review.select(ids, 'fake-*,real-g1,fake-g1'), ['real-g1', 'fake-g1', 'fake-g3'])
            with self.assertRaises(ValueError):
                review.select(ids, 'missing-*')

    def test_batch_has_one_take_per_move_and_restores_in_order(self):
        batch = review.build_batch('scenarios/moves/nyx.json', 'tmp/moves/x', ['real-g1', 'fake-g1'], 'behind,front', '640x360', 80)
        takes = [c for c in batch['commands'] if c[0]=='sloparena.moves.take']
        self.assertEqual(len(takes), 2)
        prepare = next(c for c in batch['commands'] if c[0]=='sloparena.moves.prepare')
        self.assertEqual(prepare[prepare.index('--moves')+1], 'real-g1,fake-g1')
        for command in [prepare]+takes:
            self.assertEqual(command[command.index('--views')+1], 'behind,front')
            self.assertEqual(command[command.index('--size')+1], '640x360')
        self.assertEqual(takes[1][takes[1].index('--output')+1], 'tmp/moves/x/takes/fake-g1')
        self.assertEqual(takes[0][takes[0].index('--quality')+1], '80')
        self.assertEqual([c[0] for c in batch['restore']], ['sloparena.moves.abort', 'sloparena.photo.stop', 'sloparena.photo.restore', 'editor_status'])

    def test_rejects_unsafe_and_duplicate_move_ids(self):
        with tempfile.TemporaryDirectory() as directory:
            p = Path(directory)/'recipe.json'
            for content in ('{"moves":[{"id":"../oops"}]}', '{"moves":[{"id":"same"},{"id":"same"}]}', '{"moves":[],"moves":[]}'):
                p.write_text(content)
                with self.assertRaises(ValueError):
                    review.recipe_move_ids(p)

    def test_load_preserves_ticks_hits_and_missing_commit_marker(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture(root, forms=(0, 1))
            (root/'takes/move-1/take.json').unlink()
            loaded = review.load_run(root)
            self.assertEqual(loaded.missing, ('move-1',))
            self.assertEqual(loaded.takes[0].match_ticks, (33, 34, 35))
            self.assertEqual(loaded.takes[0].hits, (review.Hit(1, 7, False, '100'),))

    def test_media_reports_a_missing_take_without_using_its_partial_images(self):
        with tempfile.TemporaryDirectory() as directory, contextlib.redirect_stdout(io.StringIO()):
            root = Path(directory)
            fixture(root)
            (root/'takes/move-0/take.json').unlink()
            summary = review.build_media(review.load_run(root))
            self.assertFalse(summary['complete'])
            self.assertEqual(summary['missing'], ['move-0'])
            self.assertEqual(summary['images'], 0)
            self.assertEqual(summary['moves'], [])
            self.assertIn('Missing take: move-0', Path(summary['boards'][0]).read_text())

    def test_load_rejects_skipped_frames_ticks_and_mismatched_view_ticks(self):
        for field in ('frame', 'matchTick', 'viewTicks'):
            with self.subTest(field=field), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                fixture(root)
                path = root/'takes/move-0/frames.jsonl'
                rows = [json.loads(line) for line in path.read_text().splitlines()]
                if field == 'viewTicks':
                    rows[1][field]['front'] = 35
                else:
                    rows[1][field] += 1
                path.write_text(''.join(json.dumps(r)+'\n' for r in rows))
                with self.assertRaises(ValueError):
                    review.load_run(root)

    def test_load_rejects_recipe_drift_and_hit_receipt_drift(self):
        for field, value in (('recipeSha256', 'b'*64), ('hits', []), ('size', [66, 36])):
            with self.subTest(field=field), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                fixture(root)
                path = root/'takes/move-0/take.json'
                receipt = review.read_json(path)
                receipt[field] = value
                review.write_json(path, receipt)
                with self.assertRaises(ValueError):
                    review.load_run(root)

    def test_load_rejects_missing_extra_wrong_sized_and_corrupt_jpgs(self):
        for change in ('missing', 'extra', 'size', 'corrupt'):
            with self.subTest(change=change), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                fixture(root)
                path = root/'takes/move-0/front/0001.jpg'
                if change == 'missing': path.unlink()
                elif change == 'extra': shutil.copy2(path, path.with_name('9999.jpg'))
                elif change == 'size': Image.new('RGB', (60, 36)).save(path)
                else: path.write_text('invalid JPEG')
                with self.assertRaises((ValueError, OSError)):
                    review.load_run(root)

    def test_crop_is_an_enlarged_in_frame_window_even_when_action_fills_the_view(self):
        for box in (review.Box(20, 10, 40, 26), review.Box(0, 0, 64, 36), review.Box(58, 30, 64, 36)):
            x0,y0,x1,y1 = review.crop_box(box, (64, 36))
            self.assertEqual((x1-x0)*3, (y1-y0)*4)
            self.assertGreaterEqual(x0, 0)
            self.assertGreaterEqual(y0, 0)
            self.assertLessEqual(x1, 64)
            self.assertLessEqual(y1, 36)
            self.assertLess(x1-x0, 64)
            self.assertLess(y1-y0, 36)
            if box.x1-box.x0 < 32 and box.y1-box.y0 < 20:
                self.assertLessEqual(x0, box.x0)
                self.assertLessEqual(y0, box.y0)
                self.assertGreaterEqual(x1, box.x1)
                self.assertGreaterEqual(y1, box.y1)
                self.assertGreaterEqual(x1-x0, (box.x1-box.x0)*1.2)
                self.assertGreaterEqual(y1-y0, (box.y1-box.y0)*1.2)

    def test_inset_is_a_nearest_neighbor_crop_of_the_same_frame(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture(root)
            take = review.load_run(root).takes[0]
            image, box = review.inset(take, 'front', take.phases[0], (480, 360))
            with Image.open(take.image('front', 1)) as source:
                expected = source.convert('RGB').crop(box).resize((480, 360), Image.Resampling.NEAREST)
            self.assertEqual(image.tobytes(), expected.tobytes())

    def test_unresolved_phase_is_not_replaced_with_a_guessed_frame(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture(root)
            p = root/'takes/move-0/take.json'
            receipt = review.read_json(p)
            receipt['phases'][0]['frame'] = None
            receipt['problems'] = ['peak unresolved']
            review.write_json(p, receipt)
            take = review.load_run(root).takes[0]
            sheet = review.make_sheet(take, root/'media/sheets/move-0.jpg')
            crops = review.read_json(sheet.with_suffix('.json'))['crops']
            self.assertTrue(all(c['frame'] is None and c['image'] is None and c['box'] is None for c in crops if c['phase']=='peak'))

    def test_full_roster_boards_fit_the_html_budget_with_absolute_images(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)/'a-long-project-path'/'tmp/moves/nyx/20261029-195923-034390'
            fixture(root, forms=(0,1), views=tuple(f'view-{i}' for i in range(10)))
            original = review.load_run(root)
            takes = tuple(replace(t, id=f'move-{t.form}-{i:02d}') for t in original.takes for i in range(14))
            run = replace(original, takes=takes)
            media = root/'media'
            artifacts = {t.id:dict(sheet=str(media/'sheets'/f'{t.id}.jpg'), crops=str(media/'sheets'/f'{t.id}.json'),
                                   peak=str(media/'board/img'/f'{t.id}.jpg'), mosaic=str(media/'clips'/t.id/'all.mp4'),
                                   clips={v.name:str(media/'clips'/t.id/f'{v.name}.mp4') for v in t.views}) for t in takes}
            boards = review.make_boards(run, artifacts, {}, media)
            for board in boards:
                text = board.read_text()
                self.assertLess(len(text.encode()), 30000)
                self.assertEqual(text.count('<article>'), 14)
                self.assertEqual(text.count(f'src="{media}/board/img/'), 14)
                self.assertIn('href="../clips/', text)

    @unittest.skipUnless(shutil.which('ffmpeg') and shutil.which('ffprobe'), 'ffmpeg and ffprobe required')
    def test_media_encodes_both_forms_and_rebuilds_identically(self):
        with tempfile.TemporaryDirectory() as directory, contextlib.redirect_stdout(io.StringIO()):
            root = Path(directory)
            fixture(root, forms=(0,1))
            run = review.load_run(root)
            first = review.build_media(run)
            before = hashes(root/'media')
            summary = (root/'summary.json').read_bytes()
            second = review.build_media(review.load_run(root))
            self.assertEqual(first, second)
            self.assertEqual(before, hashes(root/'media'))
            self.assertEqual(summary, (root/'summary.json').read_bytes())
            self.assertEqual(first['images'], 12)
            self.assertEqual(len(first['boards']), 2)
            self.assertEqual(len(first['montages']), 2)
            for board in first['boards']:
                text = Path(board).read_text()
                self.assertLess(len(text.encode()), 30000)
                self.assertIn('width=device-width', text)
                self.assertIn('max-width:100%', text)
                self.assertIn('Shadow &lt;Flick&gt; &amp; sparks', text)
                self.assertIn(f'src="{root}/media/board/img/', text)
            for move in first['moves']:
                paths = list(move['clips'].values())+[move['mosaic']]
                for path in paths:
                    probe = subprocess.run(['ffprobe','-v','error','-count_frames','-show_entries','stream=codec_name,pix_fmt,nb_read_frames,color_range,width,height','-of','json',path],check=True,capture_output=True,text=True)
                    stream = json.loads(probe.stdout)['streams'][0]
                    self.assertEqual(stream['codec_name'], 'h264')
                    self.assertEqual(stream['pix_fmt'], 'yuv420p')
                    self.assertEqual(stream['color_range'], 'tv')
                    self.assertEqual(int(stream['nb_read_frames']), 3)
                    if path == move['mosaic']:
                        self.assertEqual((stream['width'],stream['height']), (768,432))


if __name__ == '__main__':
    unittest.main()
