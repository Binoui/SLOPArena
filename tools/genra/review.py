"""Review one generated clip as the animation director (REVIEW.md) and return structured notes.

    <genra python> tools/genra/review.py BRIEF.json RESULT.json [--asset Assets/...anim] [--reference oldrun]
        [--travel 0.1515] [--previous review.json]

Opus 5.5 gets:
- Unity renders of the clip, and of a style reference played on the same character;
- joint curves over the reference's;
- hand close-ups;
- the check report.

It returns issues (ticks, joints, evidence, a fix and a measurable target), what to keep, and a verdict. Without
--asset the clip is first written to Assets/Art/Experiments/GenRA/<package>/<name>.anim. The brief's optional "review"
block sets the defaults, e.g. {"reference": "oldrun", "travel": 0.1515}. The reference is a rig pack reference: an
approved clip of the character, or another character's clip carried with unity_rigpack.py --reference. Travel is the
game's ground speed in metres per tick, so a cape trails as in a run. Evidence goes to artifacts/genra-reviews/<id>/.
"""
from __future__ import annotations

import argparse
import datetime
import hashlib
import json
import math
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
TOOLS = HERE.parent
REPO = TOOLS.parent
sys.path.insert(0, str(TOOLS))

from genra import claude_runner  # noqa: E402
from genra.brief import Brief, resolved_brief_path  # noqa: E402

VIEWS = ('gameplay', 'profile', 'front')
VIEW_NAMES = {'gameplay': 'gameplay view (the match camera: behind and above)',
              'profile': "side view (from the character's right)", 'front': 'front view',
              'three-quarter-right': 'three-quarter rear view (behind the right shoulder)',
              'three-quarter-left': 'three-quarter rear view (behind the left shoulder)',
              'front-three-quarter-right': 'front three-quarter view (in front of his right side)',
              'front-three-quarter-left': 'front three-quarter view (in front of his left side)'}


def evidence_views(brief: Brief) -> tuple[str, ...]:
    """The views a review renders in Unity: the filmstrip views, plus the camera of the brief's video beats, which
    beat_pairs pairs each video frame with (Sir Bonk's normals' references are front three-quarter shots)."""
    beats = (brief.get('review') or {}).get('beats')
    view = beats.get('view', 'profile') if beats else None
    return VIEWS + ((view,) if view and view not in VIEWS else ())
SCHEMA = {
    'type': 'object', 'required': ['verdict', 'summary', 'issues', 'keep'],
    'properties': {
        'verdict': {'enum': ['accept', 'revise']},
        'summary': {'type': 'string', 'description': 'Two or three sentences: how the clip reads and why the verdict.'},
        'issues': {'type': 'array', 'maxItems': 6, 'items': {
            'type': 'object', 'required': ['severity', 'problem', 'ticks', 'joints', 'evidence', 'fix', 'target'],
            'properties': {
                'severity': {'enum': ['major', 'minor']},
                'problem': {'type': 'string'},
                'ticks': {'type': 'string', 'description': 'e.g. "0-44" or "10, 32"'},
                'joints': {'type': 'array', 'items': {'type': 'string'}},
                'evidence': {'type': 'string', 'description': 'Which image and tick, or which curve, shows it.'},
                'fix': {'type': 'string', 'description': 'Which joint, which way, how much, at which ticks.'},
                'target': {'type': 'string', 'description': 'A measurable target the animator can check.'}}}},
        'keep': {'type': 'array', 'items': {'type': 'string'}},
        'previousIssues': {'type': 'array', 'items': {
            'type': 'object', 'required': ['problem', 'fixed'],
            'properties': {'problem': {'type': 'string'}, 'fixed': {'type': 'boolean'}, 'note': {'type': 'string'}}}}}}

PROMPT = """{rubric}

## The clip: {name}

Character: {character}

### The brief's direction (what the animator was asked for)

{description}

### Clock

{clock}

### Check report

{checks}
{previous}
## Images

{images}

Reply with the JSON object only."""


def subject_box(images, background, threshold=16, pad=0.04):
    """The union box of everything that differs from a flat background, padded: one crop for a set of frames."""
    import numpy as np
    mask = np.zeros(images[0].size[::-1], bool)
    for image in images:
        mask |= np.abs(np.asarray(image, int) - np.asarray(background)).max(axis=2) > threshold
    ys, xs = np.nonzero(mask)
    if not len(xs):
        return (0, 0, images[0].width, images[0].height)
    margin = pad * max(np.ptp(xs), np.ptp(ys))
    return (max(0, xs.min() - margin), max(0, ys.min() - margin), min(mask.shape[1], xs.max() + margin),
            min(mask.shape[0], ys.max() + margin))


def strip(frames: Path, view: str, ticks, title: str, cols=6, width=1440):
    """A filmstrip of Unity frames (frames/<view>/<tick>.png), wrapped onto rows of `cols`. Every frame is cropped to
    the same box, the union of the character over the shown ticks, so the body fills the cells at one scale."""
    from PIL import Image, ImageDraw
    from genra.render import BACKGROUND, _font
    images = [Image.open(frames / view / f'{tick:04d}.png').convert('RGB') for tick in ticks]
    box = subject_box(images, BACKGROUND)
    cell_w = width // cols
    cell_h = round(cell_w * (box[3] - box[1]) / (box[2] - box[0]))
    header = 34
    rows = -(-len(ticks) // cols)
    sheet = Image.new('RGB', (cols * cell_w, header + rows * cell_h), BACKGROUND)
    ImageDraw.Draw(sheet).text((8, 8), title, fill=(235, 238, 242), font=_font(18))
    for k, (tick, image) in enumerate(zip(ticks, images)):
        cell = image.crop(tuple(round(v) for v in box)).resize((cell_w, cell_h), Image.LANCZOS)
        ImageDraw.Draw(cell).text((6, 4), f't{tick}', fill=(235, 238, 242), font=_font(15))
        sheet.paste(cell, ((k % cols) * cell_w, header + (k // cols) * cell_h))
    return sheet


def beat_pairs(beats: list[dict], frames: Path, view: str, cols=5, width=1440):
    """The chosen video's key poses (top) over the clip's Unity frames at the ticks the brief maps them to (bottom),
    beat by beat. Each video frame is cropped to its subject; the Unity frames share one crop."""
    from PIL import Image, ImageDraw
    from genra.render import BACKGROUND, _font
    cell = width // cols
    unity = [Image.open(frames / view / f"{b['tick']:04d}.png").convert('RGB') for b in beats]
    ubox = subject_box(unity, BACKGROUND)
    rows = -(-len(beats) // cols)
    half = cell
    sheet = Image.new('RGB', (cols * cell, rows * 2 * half), BACKGROUND)
    font = _font(14)
    for k, (b, frame) in enumerate(zip(beats, unity)):
        video = Image.open(b['image']).convert('RGB')
        corner = video.getpixel((4, 4))  # the generated video's backdrop
        vbox = subject_box([video], corner, threshold=28, pad=0.12)
        x, y = (k % cols) * cell, (k // cols) * 2 * half
        for image, box, top, text in ((video, vbox, y, f"{b['beat']}. {b['label']} (video {b['seconds']:.2f} s)"),
                                      (frame, ubox, y + half, f"clip t{b['tick']}")):
            crop = image.crop(tuple(round(v) for v in box))
            crop.thumbnail((cell - 8, half - 8), Image.LANCZOS)
            sheet.paste(crop, (x + (cell - crop.width) // 2, top + (half - crop.height) // 2))
            ImageDraw.Draw(sheet).text((x + 6, top + 4), text, fill=(235, 238, 242), font=font, stroke_width=2,
                                       stroke_fill=(0, 0, 0))
    return sheet


def stack(images, path: Path) -> Path:
    from PIL import Image
    from genra.render import BACKGROUND
    sheet = Image.new('RGB', (max(i.width for i in images), sum(i.height for i in images)), BACKGROUND)
    y = 0
    for image in images:
        sheet.paste(image, (0, y))
        y += image.height
    sheet.save(path)
    return path


def clock_text(brief: Brief) -> str:
    c = brief['clock']
    n = c['duration_ticks']
    text = f'Ticks 0..{n} at 60 Hz ({n / 60:.2f} s).'
    if brief.loop_from is not None:
        text += f' Ticks {brief.loop_from}..{n} loop in the game (tick {n} equals tick {brief.loop_from}).'
    if 'contact_tick' in c:
        active = f", active {c['active'][0]}-{c['active'][1]}" if 'active' in c else ''   # none for a throw's release
        text += (f" Contact at tick {c['contact_tick']}{active}"
                 + (f", recovery from tick {c['iasa_tick']}." if 'iasa_tick' in c else '.'))
    if brief.timing:  # what the owner allows to change: a fix may retime it instead of squeezing the motion
        text += ' Timing contract: ' + brief.timing_text()
    if brief.get('approved_keys'):
        text += (' Approved key poses at ticks ' + ', '.join(map(str, brief['approved_keys']['ticks']))
                 + ' must be preserved. Revise the motion between them, not those poses.')
    return text


def check_result(brief_path: Path, result: Path, run_dir: Path) -> dict:
    """Always run the full checks on the saved result and its resolved contract."""
    from genra.checks import check_clip, format_report
    from genra.clip import Clip
    brief = Brief(resolved_brief_path(brief_path, result))
    clip = Clip.load(brief.rig, result, frame=brief.entry_frame)
    report = check_clip(clip, brief.check_spec())
    (run_dir / 'checks.json').write_text(json.dumps(report, indent=2, default=str))
    (run_dir / 'checks.txt').write_text(format_report(report) + '\n')
    return report


def result_identity(result: Path) -> dict:
    return {'result': str(Path(result).resolve()),
            'resultSha256': hashlib.sha256(Path(result).read_bytes()).hexdigest()}


def evidence(brief_path: Path, result: Path, run_dir: Path, asset=None, reference=None, travel=0.0,
             name=None) -> tuple[list, list]:
    """Write the review images; returns (image paths, captions). `name` (default: the run directory's) names the Unity
    clip written for a result without an asset, and the render."""
    name = name or run_dir.name
    import numpy as np
    from PIL import Image
    from genra import unity_clip
    from genra.render import BACKGROUND
    from genra.clip import Clip
    from genra.curves import curves_sheet
    from genra.render import closeup_sheet
    brief_path = resolved_brief_path(brief_path, result)
    brief = Brief(brief_path)
    clip = Clip.load(brief.rig, result, frame=brief.entry_frame)
    n = len(clip)
    check_result(brief_path, result, run_dir)
    mapping = json.loads((brief.rigpack / 'mapping.json').read_text())
    offset = None
    if asset is None:
        written = unity_clip.write(result, brief_path, f"Assets/Art/Experiments/GenRA/{mapping['package']}/{name}.anim", name)
        asset, offset = written['asset']['path'], np.array(written['originOffsetMetres'])
    clips = [{'label': 'candidate', 'asset': asset}]
    if reference:
        source = mapping['references'][reference]
        clips.append({'label': 'reference', **({'asset': source['asset']} if 'asset' in source else {'semanticId': source['semanticId']})})
    rendered = unity_clip.render(brief_path, clips, result=result, views=evidence_views(brief), name=name, offset=offset,
                                 travel=travel)
    frames = REPO / rendered['evidence'] / 'frames'
    loop = brief.loop_from is not None
    step = max(1, math.ceil(n / 24))
    ticks = list(range(0, n - 1 if loop else n, step))
    images, captions = [], []
    parts = [ticks[i:i + 12] for i in range(0, len(ticks), 12)]  # 12 frames an image keeps them large once downscaled
    for view in VIEWS:
        for k, part in enumerate(parts):
            path = run_dir / f'unity-{view}-{k + 1}.png'
            strip(frames / 'candidate', view, part, f'clip, {view}, ticks {part[0]}-{part[-1]}').save(path)
            images.append(path)
            captions.append(f'Unity: the clip, {VIEW_NAMES[view]}, ticks {part[0]}-{part[-1]} every {step}'
                            + (f' (part {k + 1} of {len(parts)}).' if len(parts) > 1 else '.'))
    if reference:  # side by side, so a changed silhouette shows (a review once called a raised elbow "almost exactly" the same)
        few = [round(i * (n - 1) / 3) for i in range(3)]
        rows = []
        for view in VIEWS:
            left = strip(frames / 'reference', view, few, f'reference {reference}, {view}', cols=3, width=720)
            right = strip(frames / 'candidate', view, few, f'clip, {view}', cols=3, width=720)
            row = Image.new('RGB', (1440, max(left.height, right.height)), BACKGROUND)
            row.paste(left, (0, 0))
            row.paste(right, (720, 0))
            rows.append(row)
        path = stack(rows, run_dir / 'unity-reference.png')
        images.append(path)
        captions.append(f"Unity, side by side: the reference '{reference}' ({mapping['references'][reference]['name']}, left) and "
                        f'the clip (right), both on the same character over the same ticks ({", ".join(map(str, few))}), from the '
                        'gameplay, side and front views. Compare the silhouettes.')
    beats = (brief.get('review') or {}).get('beats')
    if beats:  # the owner's chosen video reference, beat by beat (video_ref.py frames)
        listed = json.loads((REPO / beats['file']).read_text())['beats']
        chosen = [b | {'tick': beats['ticks'][b['label']]} for b in listed if b['label'] in beats['ticks']]
        view = beats.get('view', 'profile')
        path = run_dir / 'beats.png'
        beat_pairs(chosen, frames / 'candidate', view).save(path)
        images.append(path)
        captions.append(f"The chosen video reference's key poses (top of each pair) over the clip's Unity frames at the ticks "
                        f'the brief maps them to (bottom), {VIEW_NAMES.get(view, view)}. The video sets poses and ideas, not timing: '
                        'compare the poses and the silhouettes.')
    path = run_dir / 'curves.png'
    curves_sheet(clip, path, reference=brief.reference(reference) if reference else None,
                 title='Joint curves of the clip' + (f' (grey: {reference}, time-normalised)' if reference else ''))
    images.append(path)
    captions.append('Joint curves of the clip in colour' + (f", over the reference '{reference}' in grey" if reference else '')
                    + '. Left side solid, right side dashed.')
    from genra.curves import flow_window, speed_map
    path = run_dir / 'speed.png'
    window = flow_window(n, hold_from=(brief.get('exit') or {}).get('from_tick'))
    speed_map(clip, path, reference=brief.reference(reference) if reference else None, window=window,
              title='Joint speed per tick (dark red = stopped, bright = turning fast); yellow lines: the judged window')
    images.append(path)
    captions.append('Speed map: one row per joint, one column per tick, dark red where the joint has stopped'
                    + (f", with the reference '{reference}' below at the same length" if reference else '')
                    + '. Hand-made motion stays lit; dark columns are the whole body halting on a key, dark blocks are '
                      'dead holds. The check report has the flow numbers and the hand-made range.')
    close = [round(i * (n - 1) / 4) for i in range(4)]
    path = run_dir / 'hands.png'
    closeup_sheet(clip, ['LeftHand', 'RightHand'], close, path)
    images.append(path)
    captions.append(f'Hand close-ups (flat-shaded, held weapons hidden): each hand from the front, its outer side and above, '
                    f'at ticks {", ".join(map(str, close))}.')
    (run_dir / 'evidence.json').write_text(json.dumps({**result_identity(result), 'brief': str(brief_path),
                                                       'asset': asset, 'unityRender': rendered['evidence'], 'ticks': ticks,
                                                       'images': [str(p) for p in images], 'captions': captions}, indent=2))
    return images, captions


def reuse_evidence(source: Path, run_dir: Path, brief_path: Path, result: Path) -> tuple[list, list]:
    """Reuse the same clip's images, but recheck the resolved contract, never an old PASS.

    New evidence records a content hash. Legacy reviews identify an archived result
    in review.json; compare its bytes before reusing its images.
    """
    import shutil
    data = json.loads((source / 'evidence.json').read_text())
    identity = result_identity(result)
    recorded = data.get('resultSha256')
    if recorded is None:
        previous = json.loads((source / 'review.json').read_text())['result']
        recorded = result_identity(Path(previous))['resultSha256']
    if recorded != identity['resultSha256']:
        raise ValueError(f'{source}: review evidence belongs to a different clip; render a fresh review')
    images = []
    for image in data['images']:
        images.append(Path(shutil.copy2(image, run_dir / Path(image).name)))
    check_result(brief_path, result, run_dir)
    (run_dir / 'evidence.json').write_text(json.dumps(data | identity | {
        'brief': str(resolved_brief_path(brief_path, result)), 'images': [str(p) for p in images],
        'reusedFrom': str(source), 'checksRefreshed': True}, indent=2))
    return images, data['captions']


def review(brief_path: Path, result: Path, run_dir: Path, asset=None, reference=None, travel=0.0, previous=None,
           model='claude-opus-5-5', effort='high', timeout=1200, name=None, reuse=None) -> dict:
    """Review a clip; `reuse` takes the evidence of an earlier review of it instead of rendering again."""
    brief_path = resolved_brief_path(brief_path, result)
    brief = Brief(brief_path)
    images, captions = (reuse_evidence(Path(reuse), run_dir, brief_path, result) if reuse
                        else evidence(brief_path, result, run_dir, asset, reference, travel, name))
    earlier = ''
    if previous:
        notes = previous.get('issues', [])
        earlier = ('\n### Previous review\n\nThe animator revised the clip after these notes. Fill previousIssues: for each, '
                   'is it fixed?\n\n' + '\n'.join(f"- ({i['severity']}) {i['problem']} Target: {i['target']}" for i in notes) + '\n')
    prompt = PROMPT.format(rubric=(HERE / 'REVIEW.md').read_text().split('\n', 2)[2].strip(), name=brief['name'],
                           character=brief.get('character', ''), description=brief['description'], clock=clock_text(brief),
                           checks=(run_dir / 'checks.txt').read_text().strip(), previous=earlier,
                           images='\n'.join(f'{k + 1}. {c}' for k, c in enumerate(captions)))
    (run_dir / 'prompt.txt').write_text(prompt)
    scratch = Path('/tmp/genra-reviews') / run_dir.name
    scratch.mkdir(parents=True, exist_ok=True)
    code, event = claude_runner.run(prompt, images, scratch, run_dir, [], [], model, effort, tools='', timeout=timeout,
                                    schema=SCHEMA)
    notes = (event or {}).get('structured_output')
    if notes is None and event and event.get('result'):
        try:
            notes = json.loads(event['result'])
        except json.JSONDecodeError:
            notes = None
    out = {'brief': str(brief_path), 'result': str(result), 'reference': reference, 'model': model, 'effort': effort,
           'returncode': code, 'costUsd': (event or {}).get('total_cost_usd'), 'review': notes}
    (run_dir / 'review.json').write_text(json.dumps(out, indent=2))
    if notes:
        (run_dir / 'review.md').write_text(markdown(notes))
    return out


def markdown(notes: dict) -> str:
    lines = [f"**Verdict: {notes['verdict']}.** {notes['summary']}", '']
    for k, issue in enumerate(notes['issues'], 1):
        lines += [f"{k}. ({issue['severity']}) {issue['problem']}",
                  f"   - Ticks {issue['ticks']}; {', '.join(issue['joints'])}. Evidence: {issue['evidence']}",
                  f"   - Fix: {issue['fix']}", f"   - Target: {issue['target']}"]
    if notes.get('keep'):
        lines += ['', 'Keep: ' + '; '.join(notes['keep'])]
    for item in notes.get('previousIssues', []):
        lines.append(f"- Previous: {item['problem']}: {'fixed' if item['fixed'] else 'NOT fixed'}. {item.get('note', '')}")
    return '\n'.join(lines) + '\n'


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    parser.add_argument('brief')
    parser.add_argument('result')
    parser.add_argument('--asset', help='an existing Unity clip of this result (skips writing one)')
    parser.add_argument('--reference', help="rig pack reference to compare with (default: the brief's review.reference)")
    parser.add_argument('--travel', type=float, help="ground speed in metres per tick for the renders (default: the brief's review.travel)")
    parser.add_argument('--previous', help='review.json of the previous round: the reviewer says which issues are fixed')
    parser.add_argument('--reuse', help="an earlier review folder of this clip: reuse its evidence (no Unity), e.g. to try REVIEW.md changes")
    parser.add_argument('--name')
    parser.add_argument('--model', default='claude-opus-5-5')
    parser.add_argument('--effort', default='high')
    parser.add_argument('--out-root', default=str(REPO / 'artifacts/genra-reviews'))
    args = parser.parse_args(argv)
    brief = Brief(resolved_brief_path(args.brief, args.result))
    defaults = brief.get('review', {})
    stamp = datetime.datetime.now().strftime('%Y%m%d-%H%M%S')
    run_dir = Path(args.out_root) / f"{stamp}-{args.name or brief['name']}"
    run_dir.mkdir(parents=True)
    previous = json.loads(Path(args.previous).read_text())['review'] if args.previous else None
    out = review(brief.path, Path(args.result).resolve(), run_dir, args.asset,
                 args.reference or defaults.get('reference'), args.travel if args.travel is not None else defaults.get('travel', 0.0),
                 previous, args.model, args.effort, reuse=args.reuse)
    print(run_dir)
    print((run_dir / 'review.md').read_text() if out['review'] else f"no review (returncode {out['returncode']})")
    return 0 if out['review'] else 1


if __name__ == '__main__':
    sys.exit(main())
