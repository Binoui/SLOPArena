"""Write a genra clip into Unity as a hybrid Humanoid .anim on the character's own prefab, measured in Unity.

    <genra python> tools/genra/unity_clip.py roundtrip fightguy idle     # proof: approved clip -> pack -> Unity clip
    <genra python> tools/genra/unity_clip.py write RESULT.json --brief BRIEF.json [--save | --asset Assets/...anim]
    <genra python> tools/genra/unity_clip.py render --brief BRIEF.json --clip new=Assets/...anim --clip current=anim.bonk.a1

Rig packs mirror Unity's x axis (unity_rigpack.py); this inverts that exactly. unity/write_clip.cs.template turns
mapped Humanoid bones into muscle and body (RootT/RootQ) curves with HumanPoseHandler, keeps exact local transform
curves for every other joint and bakes root motion into the pose: PlayerRenderer plays clips through Animancer with
applyRootMotion off, so the hips may move but the fighter never does. Unity measures the clip as the game plays it.

`roundtrip` rebuilds an approved reference clip from its rig-pack samples and compares bones and skinned vertices
with the original. `write` converts a toolkit result: the body origin is matched to the entry clip's actual
playback at the entry tick, and the clip is saved as a new, unbound asset only with --save or --asset.
`render` draws clips on the actual prefab (materials, weapons, Animancer, stretched over the brief's ticks) from the
toolkit's review views, so a candidate can be judged next to the clip it would replace before anything is bound.
Evidence goes to artifacts/genra-unity-clips/<id>/.
"""
from __future__ import annotations

import argparse
import json
import subprocess
import sys
import time
from pathlib import Path

import numpy as np
from scipy.spatial.transform import Rotation

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
UNITY = REPO / 'client/Unity'
OUT = REPO / 'artifacts/genra-unity-clips'
MIRROR4 = np.diag([-1.0, 1.0, 1.0, 1.0])


def from_row(m):
    """genra row-vector matrices in the pack's mirrored space (..., 4, 4) -> Unity column-vector matrices."""
    return MIRROR4 @ np.swapaxes(np.asarray(m, float), -1, -2) @ MIRROR4


def unity_pose(rig, world, mapping) -> tuple[list[str], np.ndarray, np.ndarray]:
    """Unity paths, local transforms (ticks, joints, px py pz qx qy qz qw sx sy sz) and world positions
    (ticks, joints, 3) for every pack joint that exists in Unity (weapon joints and anchors do not)."""
    keep = [i for i, p in enumerate(rig.joint_paths) if mapping['joints'].get(p)]
    paths = [mapping['joints'][rig.joint_paths[i]] for i in keep]
    unity = from_row(world)  # (ticks, N, 4, 4)
    actor = np.array(mapping['actorRootWorld'], float).reshape(4, 4)
    parents = np.array([unity[:, p] if p >= 0 else np.broadcast_to(actor, unity[:, i].shape) for i, p in
                        ((i, rig.parents[i]) for i in keep)])  # (J, ticks, 4, 4)
    local = np.linalg.inv(parents) @ np.swapaxes(unity[:, keep], 0, 1)  # (J, ticks, 4, 4)
    linear = local[..., :3, :3]
    scale = np.linalg.norm(linear, axis=-2)  # column norms: L = T R S
    rotation = linear / scale[..., None, :]
    if (np.linalg.det(rotation) <= 0).any():
        raise SystemExit('A joint has a mirrored local transform; the writer supports proper rotations only')
    quat = Rotation.from_matrix(rotation.reshape(-1, 3, 3)).as_quat().reshape(*rotation.shape[:-2], 4)
    channels = np.concatenate([local[..., :3, 3], quat, scale], axis=-1)  # (J, ticks, 10)
    return paths, np.swapaxes(channels, 0, 1), unity[:, keep][..., :3, 3]


def run_unity(job: dict, run_dir: Path, template: str = 'write_clip') -> dict:
    """Fill a unity/<template>.cs.template for this job and run it with the gateway's eval_file (own lease)."""
    run_dir.mkdir(parents=True, exist_ok=True)
    job_path, out_path = run_dir / 'job.json', run_dir / 'unity.json'
    job_path.write_text(json.dumps(job))
    code = (HERE / f'unity/{template}.cs.template').read_text().replace('{{JOB}}', str(job_path)).replace('{{OUTPUT}}', str(out_path))
    script = run_dir / f'{template}.cs'
    script.write_text(code)
    command = ['bun', str(REPO / 'scripts/unity-editor-gateway.ts'), '--project-path', str(UNITY), '--',
               'eval_file', '--file', str(script), '--timeout', '600000']
    result = subprocess.run(command, cwd=REPO, capture_output=True, text=True)
    (run_dir / 'gateway.log').write_text(result.stdout + '\n--- stderr ---\n' + result.stderr)
    if result.returncode != 0 or not out_path.exists():
        raise SystemExit(f'Unity {template} failed; see {run_dir / "gateway.log"}')
    return json.loads(out_path.read_text())


def distances(a, b) -> np.ndarray:
    return np.linalg.norm(np.asarray(a, float) - np.asarray(b, float), axis=-1)


def worst(errors: np.ndarray, paths) -> dict:
    """Max error (metres) with its tick and joint, from a (ticks, joints) array."""
    tick, joint = np.unravel_index(int(np.argmax(errors)), errors.shape)
    return {'maxMetres': float(errors.max()), 'tick': int(tick), 'joint': paths[joint].rsplit('/', 1)[-1]}


def fit(data) -> dict:
    """How closely Unity's Humanoid muscles reproduce the toolkit pose (SetHumanPose), before and after refinement."""
    after, before = np.array(data['fit']['afterMetres']), np.array(data['fit']['beforeMetres'])
    tick = int(np.argmax(after))
    return {'maxMetres': float(after.max()), 'tick': tick, 'joint': data['fit']['worstJoint'][tick].rsplit('/', 1)[-1],
            'maxDegrees': float(max(data['fit']['afterDegrees'])),
            'maxDegreesJoint': data['fit']['worstAngleJoint'][int(np.argmax(data['fit']['afterDegrees']))].rsplit('/', 1)[-1],
            'beforeRefinementMetres': float(before.max())}


def mesh_vs_toolkit(rig, world, mapping, bakes, offset) -> dict:
    """Max distance (metres) between Unity's skinned vertices as the game plays the clip and the toolkit's own
    skinning of the same ticks (which matches Unity per vertex for the same pose), after the origin offset."""
    from genra.skin import Skin
    skin = Skin(rig)
    by_path = {mapping['meshes'][m['name']]: k for k, m in enumerate(skin.meshes) if m['name'] in mapping['meshes']}
    worst = {'maxMetres': 0.0, 'tick': None, 'renderer': None}
    for bake in bakes:
        points = skin.points(world[bake['tick']])
        for renderer in bake['renderers']:
            k = by_path.get(renderer['path'])
            if k is None:
                continue
            ours = points[k] * [-1.0, 1.0, 1.0] + offset  # pack space mirrors Unity's x
            error = float(distances(np.array(renderer['vertices']).reshape(-1, 3), ours).max())
            if error > worst['maxMetres']:
                worst = {'maxMetres': error, 'tick': bake['tick'], 'renderer': renderer['path'].rsplit('/', 1)[-1]}
    return worst


def job_for(package, name, paths, local, reference=None, compare=None, asset=None) -> dict:
    ticks = len(local)
    bake = {0, ticks // 2, ticks - 1} if compare else set(range(0, ticks, 4)) | {ticks - 1}  # conversions: whole clip
    return {'package': package, 'name': name, 'fps': 60, 'paths': paths, 'local': np.round(local, 7).tolist(),
            'reference': reference, 'compare': compare, 'asset': asset, 'bakeTicks': sorted(bake)}


def roundtrip(package: str, label: str) -> dict:
    from genra.rig import Rig
    pack = REPO / f'artifacts/genra-rigpacks/{package}-unity'
    mapping = json.loads((pack / 'mapping.json').read_text())
    rig, source = Rig(pack / 'rig.usda'), Rig(pack / 'references' / f'{label}.usda')
    first, last = source.animated_range()
    world = rig.fk(*source.sample_local(range(first, last + 1)))
    paths, local, intended = unity_pose(rig, world, mapping)
    semantic = mapping['references'][label]['semanticId']
    run_dir = OUT / f'{time.strftime("%Y%m%d-%H%M%S")}-roundtrip-{package}-{label}'
    data = run_unity(job_for(package, f'genra_roundtrip_{package}_{label}', paths, local,
                             reference={'semanticId': semantic, 'tick': 0}, compare=semantic), run_dir)
    new_sample, new_runtime = np.array(data['newSample']), np.array(data['newRuntime'])
    old_sample, old_runtime = np.array(data['compareSample']), np.array(data['compareRuntime'])
    report = {
        'package': package, 'clip': label, 'original': data['compare'], 'ticks': data['ticks'], 'humanMotion': data['humanMotion'],
        'curves': data['curves'], 'mappedJoints': len(data['mapped']), 'curveJoints': len(data['generic']),
        'inBetweenJoints': data['inBetween'], 'originBodyOffset': data['originBodyOffset'], 'muscleFit': fit(data),
        'packMatchesOriginalPlayback': worst(distances(intended, old_runtime), paths),
        'bonesVsOriginal': worst(distances(new_runtime, old_runtime), paths),
        'skinVsOriginal': data['skin'],
        'playbackVsSampling': worst(distances(new_runtime, new_sample), paths),
        'originalPlaybackVsSampling': worst(distances(old_runtime, old_sample), paths),
        'evidence': str(run_dir.relative_to(REPO))}
    (run_dir / 'report.json').write_text(json.dumps(report, indent=2))
    return report


def write(result: Path, brief_path: Path, asset: str | None, name: str | None) -> dict:
    from genra.brief import Brief
    from genra.clip import Clip
    brief = Brief(brief_path)
    mapping = json.loads((brief.rigpack / 'mapping.json').read_text())
    clip = Clip.load(brief.rig, result, frame=brief.entry_frame)
    paths, local, intended = unity_pose(brief.rig, clip.world(), mapping)
    entry = brief['entry']
    reference = {'semanticId': mapping['references'][entry['reference']]['semanticId'], 'tick': int(entry['tick'])}
    name = name or brief['name']
    run_dir = OUT / f'{time.strftime("%Y%m%d-%H%M%S")}-{name}'
    data = run_unity(job_for(mapping['package'], name, paths, local, reference=reference, asset=asset), run_dir)
    runtime = np.array(data['newRuntime'])
    start = np.array(data['reference']['positions'])
    offset = (runtime[0] - intended[0]).mean(axis=0)  # the constant origin correction, in metres
    report = {
        'package': mapping['package'], 'name': name, 'ticks': data['ticks'], 'humanMotion': data['humanMotion'],
        'curves': data['curves'], 'entryClip': data['reference']['clip'], 'entryTick': reference['tick'],
        'originOffsetMetres': offset.round(5).tolist(), 'muscleFit': fit(data),
        'playbackVsToolkit': worst(distances(runtime - offset, intended), paths),
        'meshVsToolkit': mesh_vs_toolkit(brief.rig, clip.world(), mapping, data['bakes'], offset),
        'entryVsEntryClip': worst(distances(runtime[:1], start[None]), paths),
        'exitVsEntryClip': worst(distances(runtime[-1:], start[None]), paths),
        'playbackVsSampling': worst(distances(runtime, np.array(data['newSample'])), paths),
        'inBetweenJoints': data['inBetween'], 'asset': data['asset'], 'evidence': str(run_dir.relative_to(REPO))}
    (run_dir / 'report.json').write_text(json.dumps(report, indent=2))
    return report


def render(brief_path: Path, clips: list[dict], result: Path | None = None, views=('gameplay', 'three-quarter-right', 'profile'),
           size=360, slow=4, name=None, offset=None, margin=1.3) -> dict:
    """Render clips on the character's actual prefab (materials, weapons, Animancer playback stretched over the brief's
    ticks, as the game plays them) from the toolkit's review views and framing, into one synchronized video: a row per
    clip, a column per view. `clips` are {'label', 'asset'} (an Assets/... .anim) or {'label', 'semanticId'} (the clip
    bound in the package catalog, e.g. the move it would replace). `result` (the toolkit clip) sets the framing."""
    from PIL import Image, ImageDraw
    from genra.brief import Brief
    from genra.clip import Clip
    from genra.render import BACKGROUND, Renderer, _font, encode_video, tick_label
    from genra.skin import Skin
    brief = Brief(brief_path)
    mapping = json.loads((brief.rigpack / 'mapping.json').read_text())
    clock = brief['clock']
    renderer = Renderer(Skin(brief.rig), brief.entry_frame)
    worlds = [brief.entry_pose.world()]
    if result:
        worlds += list(Clip.load(brief.rig, result, frame=brief.entry_frame).world())
    points = np.concatenate([np.concatenate(renderer.skin.points(w))[::5] for w in worlds[::2]])
    mirror = np.array([-1.0, 1.0, 1.0])  # pack space mirrors Unity's x; mirroring the camera too keeps the image
    cameras = []
    for view in views:  # each view frames the candidate's whole motion, with room for the clips compared with it
        right, up, toward = renderer.camera(view)
        x, y = points @ right, points @ up
        center = right * (x.min() + x.max()) / 2 + up * (y.min() + y.max()) / 2 + toward * float((points @ toward).mean())
        target = center * mirror + (offset if offset is not None else 0.0)
        cameras.append({'name': view, 'position': (target + toward * mirror * 20).tolist(), 'forward': (-toward * mirror).tolist(),
                        'up': (up * mirror).tolist(), 'size': max(np.ptp(x), np.ptp(y)) / 2 * margin})
    weapons = [{'bone': w['bone'], 'prefab': w['prefab'], 'positionOffset': w['positionOffset'], 'rotationOffset': w['rotationOffset']}
               for w in mapping.get('weapons', [])]
    name = name or brief['name']
    run_dir = OUT / f'{time.strftime("%Y%m%d-%H%M%S")}-render-{name}'
    job = {'package': mapping['package'], 'clips': clips, 'ticks': clock['duration_ticks'], 'size': size * 2,
           'frames': str(run_dir / 'frames'), 'background': [c / 255 for c in BACKGROUND], 'views': cameras, 'weapons': weapons}
    data = run_unity(job, run_dir, 'render_clip')
    header, font, small = 28, _font(16), _font(13)
    def cell(label, view, tick):
        image = Image.open(run_dir / 'frames' / label / view / f'{tick:04d}.png').convert('RGB').resize((size, size), Image.LANCZOS)
        ImageDraw.Draw(image).text((6, size - 20), f'{label} · {view}', fill=(200, 205, 214), font=small)
        return image
    frames = []
    for tick in range(clock['duration_ticks'] + 1):
        frame = Image.new('RGB', (size * len(views), header + size * len(clips)), BACKGROUND)
        for r, c in enumerate(clips):
            for k, view in enumerate(views):
                frame.paste(cell(c['label'], view, tick), (k * size, header + r * size))
        tick_label(frame, tick, clock['contact_tick'], clock['active'], font)
        frames.append(frame)
    video = encode_video(frames, run_dir / 'real.mp4', slow)
    ticks = brief.review_ticks
    sheet = Image.new('RGB', (200 * len(ticks), 200 * len(clips) * len(views)), BACKGROUND)
    for r, (c, view) in enumerate((c, v) for c in clips for v in views):
        for k, tick in enumerate(ticks):
            image = cell(c['label'], view, tick).resize((200, 200), Image.LANCZOS)
            ImageDraw.Draw(image).text((6, 4), f't{tick}', fill=(235, 238, 242), font=small)
            sheet.paste(image, (200 * k, 200 * r))
    sheet.save(run_dir / 'real-sheet.png')
    report = {'package': mapping['package'], 'name': name, 'clips': data['clips'], 'views': list(views), 'ticks': clock['duration_ticks'],
              'renderPipeline': data['renderPipeline'], 'unitySeconds': data['seconds'], 'video': str(video),
              'sheet': str(run_dir / 'real-sheet.png'), 'evidence': str(run_dir.relative_to(REPO))}
    (run_dir / 'report.json').write_text(json.dumps(report, indent=2))
    return report


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    sub = parser.add_subparsers(dest='command', required=True)
    trip = sub.add_parser('roundtrip', help='rebuild an approved reference clip and compare it with the original')
    trip.add_argument('package')
    trip.add_argument('clip', choices=['idle', 'run', 'jump', 'fall'])
    out = sub.add_parser('write', help='convert a toolkit result into a Unity clip')
    out.add_argument('result')
    out.add_argument('--brief', required=True)
    out.add_argument('--name')
    target = out.add_mutually_exclusive_group()
    target.add_argument('--save', action='store_true', help='save to Assets/Art/Experiments/GenRA/<package>/<name>.anim')
    target.add_argument('--asset', help='save to this new Assets/... .anim path')
    show = sub.add_parser('render', help='render clips on the actual prefab as the game plays them, side by side')
    show.add_argument('--brief', required=True)
    show.add_argument('--clip', action='append', required=True, metavar='LABEL=SOURCE',
                      help='SOURCE is an Assets/... .anim or a bound semantic id such as anim.bonk.a1 (repeat to compare)')
    show.add_argument('--result', help='toolkit result.json that sets the framing')
    show.add_argument('--write-report', help="report.json of the `write` that made the clip: aligns the framing with its origin")
    show.add_argument('--views', default='gameplay,three-quarter-right,profile')
    show.add_argument('--size', type=int, default=360)
    show.add_argument('--name')
    args = parser.parse_args(argv)
    sys.path.insert(0, str(HERE.parent))
    if args.command == 'roundtrip':
        report = roundtrip(args.package, args.clip)
    elif args.command == 'render':
        clips = []
        for item in args.clip:
            label, _, source = item.partition('=')
            clips.append({'label': label, 'asset': source} if source.startswith('Assets/') else {'label': label, 'semanticId': source})
        offset = np.array(json.loads(Path(args.write_report).read_text())['originOffsetMetres']) if args.write_report else None
        report = render(Path(args.brief), clips, Path(args.result) if args.result else None, tuple(args.views.split(',')),
                        args.size, name=args.name, offset=offset)
    else:
        from genra.brief import Brief
        brief = Brief(args.brief)
        name = args.name or brief['name']
        package = json.loads((brief.rigpack / 'mapping.json').read_text())['package']
        asset = args.asset or (f'Assets/Art/Experiments/GenRA/{package}/{name}.anim' if args.save else None)
        report = write(Path(args.result), Path(args.brief), asset, name)
    print(json.dumps(report, indent=2))
    return 0


if __name__ == '__main__':
    sys.exit(main())
