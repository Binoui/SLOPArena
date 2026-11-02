"""Write a genra clip into Unity as a hybrid Humanoid .anim on the character's own prefab, measured in Unity.

    <genra python> tools/genra/unity_clip.py roundtrip fightguy idle [--pack nyx-unity-real]   # proof: clip -> pack -> Unity clip
    <genra python> tools/genra/unity_clip.py write RESULT.json --brief BRIEF.json [--save | --asset Assets/...anim]
    <genra python> tools/genra/unity_clip.py render --brief BRIEF.json --clip new=Assets/...anim --clip current=anim.bonk.a1
    <genra python> tools/genra/unity_clip.py timeline SPEC.json --brief BRIEF.json [--result RESULT.json]
    <genra python> tools/genra/unity_clip.py promote Assets/...accepted.anim --over Assets/CharacterPackages/<pkg>/...anim

Rig packs mirror Unity's x axis (unity_rigpack.py); this inverts that exactly. unity/write_clip.cs.template turns
mapped Humanoid bones into muscle and body (RootT/RootQ) curves with HumanPoseHandler, keeps exact local transform
curves for every other joint and bakes root motion into the pose: PlayerRenderer plays clips through Animancer with
applyRootMotion off, so the hips may move but the fighter never does. Unity measures the clip as the game plays it.

`roundtrip` rebuilds an approved reference clip from its rig-pack samples and compares bones and skinned vertices
with the original. `write` converts a toolkit result: a matched entry's body origin is matched to the entry clip's
actual playback at the entry tick (a free entry keeps the toolkit's origin), a loop from tick 0 is saved looping,
and the clip is saved as a new, unbound asset only with --save or --asset.
`render` draws clips on the actual prefab (materials, weapons, Animancer, stretched over the brief's ticks) from the
toolkit's review views, so a candidate can be judged next to the clip it would replace before anything is bound.
Every Unity job plays on the prefab the rig pack was snapshotted from (mapping.json `prefab`): the catalog rig, or
a candidate model's unbound prefab for a pack built with unity_rigpack.py --prefab.
`timeline` plays clip sequences there as PlayerRenderer drives them (crossfades, speeds, travel): the handoffs into
and out of a candidate. `promote` copies an accepted clip's content over a package clip, keeping its GUID and name, so
its bindings play the new motion; the old file is kept as before.anim. Cook, verify and refresh the roster afterwards.
Evidence goes to artifacts/genra-unity-clips/<id>/.
"""
from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import sys
import tempfile
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


def mesh_vs_toolkit(rig, world, mapping, bakes, offset, grounding=None) -> dict:
    """Max distance (metres) between Unity's skinned vertices as the game plays the clip and the toolkit's own
    skinning of the same ticks (which matches Unity per vertex for the same pose), after the origin offset. A mesh
    whose vertex count no longer matches the pack's (the model was re-exported after the pack was snapshotted, as Sir
    Bonk's was mid-session on 2026-10-22) is listed under 'meshChanged' (pack, Unity) instead: re-snapshot the pack
    (unity_rigpack.py) before trusting the toolkit's skin for that mesh."""
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
            if grounding:
                ours[:, 1] += grounding['translationMetres'][bake['tick']]
            theirs = np.array(renderer['vertices']).reshape(-1, 3)
            if len(theirs) != len(ours):
                worst.setdefault('meshChanged', {})[renderer['path'].rsplit('/', 1)[-1]] = [len(ours), len(theirs)]
                continue
            error = float(distances(theirs, ours).max())
            if error > worst['maxMetres']:
                worst = {'maxMetres': error, 'tick': bake['tick'], 'renderer': renderer['path'].rsplit('/', 1)[-1]}
    return worst


def pack_prefab(mapping) -> str | None:
    """The prefab a rig pack was snapshotted from. Unity jobs play on it, so a candidate pack (unity_rigpack.py
    --prefab) writes and renders on its unbound candidate rather than on the catalog's rig."""
    return mapping.get('prefab')


def pack_meshes(mapping) -> list[str] | None:
    """The skinned meshes a rig pack was built with (unity_rigpack.py --meshes, one body per form), or None for all the
    prefab shows. Unity jobs show the same ones on their temporary instance."""
    return mapping.get('shownMeshes')


def job_for(package, name, paths, local, reference=None, compare=None, asset=None, prefab=None, meshes=None) -> dict:
    ticks = len(local)
    bake = {0, ticks // 2, ticks - 1} if compare else set(range(0, ticks, 4)) | {ticks - 1}  # conversions: whole clip
    return {'package': package, 'prefab': prefab, 'meshes': meshes, 'name': name, 'fps': 60, 'paths': paths, 'local': np.round(local, 7).tolist(),
            'reference': reference, 'compare': compare, 'asset': asset, 'bakeTicks': sorted(bake)}


def roundtrip(package: str, label: str, pack_name: str | None = None) -> dict:
    from genra.rig import Rig
    pack = REPO / 'artifacts/genra-rigpacks' / (pack_name or f'{package}-unity')
    mapping = json.loads((pack / 'mapping.json').read_text())
    rig, source = Rig(pack / 'rig.usda'), Rig(pack / 'references' / f'{label}.usda')
    first, last = source.animated_range()
    world = rig.fk(*source.sample_local(range(first, last + 1)))
    paths, local, intended = unity_pose(rig, world, mapping)
    semantic = mapping['references'][label]['semanticId']
    run_dir = OUT / f'{time.strftime("%Y%m%d-%H%M%S")}-roundtrip-{package}-{label}'
    data = run_unity(job_for(package, f'genra_roundtrip_{package}_{label}', paths, local,
                             reference={'semanticId': semantic, 'tick': 0}, compare=semantic, prefab=pack_prefab(mapping), meshes=pack_meshes(mapping)), run_dir)
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


def write_flags(brief) -> dict:
    """How a brief's clip goes into Unity. A free entry (entry.match false) starts from its own pose, so its body
    stays where the toolkit put it: moving its centre onto the entry clip's lifted a crouched loop 17-21 cm off the
    floor. Unity can only loop a whole clip: a loop from tick 0 is saved looping (an aim hold plays it as is); a loop
    after a build-up is saved one-shot and the game wraps it (PlayerRenderer's tumble)."""
    flags = {'alignOrigin': brief.entry_matches, 'loopTime': brief.loop_from == 0}
    grounding = brief.get('grounding')
    if grounding is not None:
        if grounding != 'reference-sole':
            raise ValueError('grounding must be "reference-sole" when specified')
        flags['groundToReference'] = True
    return flags


def phase_check(clip, brief, ticks=None, retime=None) -> dict:
    """The motion checks again on a phase cut (`ticks`) or time-warped (`retime`) out of an authored result: a squeezed
    section turns faster than the authored clip did, so it can snap or flip where the run's own check passed. The
    brief's fast windows move with the cut and the warp; poses, contacts and targets are the authored ones."""
    from genra.brief import exempt_ranges
    from genra.checks import check_clip
    first = ticks[0] if ticks else 0
    length = (ticks[1] - ticks[0] + 1) if ticks else (int(retime[-1][0]) + 1 if retime else len(clip))

    def moved(a, b):  # a brief's tick window in the phase's ticks, or None outside it
        a, b = max(a - first, 0), min(b - first, length - 1)
        if a > b:
            return None
        if retime:
            a, b = np.interp([a, b], [s for s, _ in retime], [d for _, d in retime])
        return [int(np.floor(a)), int(np.ceil(b))]

    windows = [w for w in (moved(a, b) for a, b in brief.get('fast_windows', [])) if w]
    spec = {'fast_windows': windows, 'clearance': False, 'face': False, 'life': False}
    exempt = [w for w in (moved(a, b) for a, b in exempt_ranges((brief.get('wrist_exempt') or {}).get('ticks'))) if w]
    if exempt:  # a documented fold (e.g. the idle's own plank wrist at the end) stays exempt
        spec['wrist_exempt_ticks'] = tuple(tuple(w) for w in exempt)
    report = check_clip(clip, spec)
    return {'passed': report['passed'], 'fastWindows': windows,
            'failures': [f"{f['check']}: {f['message']}" for f in report['failures']],
            'maxDegreesPerTick': round(report['metrics']['maxDegreesPerTick'], 1)}


def write(result: Path, brief_path: Path, asset: str | None, name: str | None, ticks=None, retime=None) -> dict:
    """`ticks` (first, last) writes one phase of the result as its own clip (a leap's flight or its landing); a phase
    that starts after tick 0 keeps the toolkit's origin. `retime` is Clip.retime's (source tick, new tick) anchors,
    counted from the phase's first tick: a section can play slower without regenerating anything. A cut or warped phase
    is checked again (phase_check) and its failures are printed and reported as phaseCheck."""
    from genra.brief import Brief
    from genra.clip import Clip
    brief = Brief(brief_path)
    mapping = json.loads((brief.rigpack / 'mapping.json').read_text())
    clip = Clip.load(brief.rig, result, frame=brief.entry_frame)
    if ticks:
        clip = clip.segment(*ticks)
    if retime:
        clip = clip.retime(retime)
    checked = phase_check(clip, brief, ticks, retime) if ticks or retime else None
    if checked and not checked['passed']:
        print('warning: the written phase fails checks: ' + '; '.join(checked['failures']), file=sys.stderr)
    paths, local, intended = unity_pose(brief.rig, clip.world(), mapping)
    entry = brief['entry']
    source = mapping['references'][entry['reference']]  # a bound clip, or a clip asset (an unbound aim loop)
    reference = {k: source[k] for k in ('semanticId', 'asset') if k in source} | {'tick': int(entry['tick'])}
    name = name or brief['name']
    run_dir = OUT / f'{time.strftime("%Y%m%d-%H%M%S")}-{name}'
    flags = write_flags(brief)
    if ticks and ticks[0] > 0:
        flags['alignOrigin'] = False  # the phase does not start on the entry pose
    data = run_unity(job_for(mapping['package'], name, paths, local, reference=reference, asset=asset,
                             prefab=pack_prefab(mapping), meshes=pack_meshes(mapping)) | flags, run_dir)
    runtime = np.array(data['newRuntime'])
    start = np.array(data['reference']['positions'])
    expected = intended.copy()
    if data.get('grounding'):
        moving = {mapping['joints'].get(brief.rig.joint_paths[i]) for i in brief.rig.descendants('Hips')}
        for i, path in enumerate(paths):
            if path in moving:
                expected[:, i, 1] += data['grounding']['translationMetres']
    offset = (runtime[0] - expected[0]).mean(axis=0)  # the constant origin correction, in metres
    if np.linalg.norm(offset) > 0.01:
        print(f'warning: the clip plays {np.linalg.norm(offset) * 100:.1f} cm from where the toolkit put it: tick 0 '
              'misses the entry pose, or the rig pack is older than the prefab', file=sys.stderr)
    from genra.receipts import asset_path, portable, sha256, source_run_for
    report = {
        'sourceRun': portable(source_run_for(result)) if source_run_for(result) else None,
        'sourceResult': portable(result), 'brief': portable(brief_path),
        'transforms': {'ticks': ticks, 'retime': retime, **flags, 'grounding': data.get('grounding')},
        'assetSha256': sha256(UNITY / asset_path(data)) if asset_path(data) else None,
        'package': mapping['package'], 'name': name, 'ticks': data['ticks'], 'humanMotion': data['humanMotion'],
        'curves': data['curves'], 'entryClip': data['reference']['clip'], 'entryTick': reference['tick'], **flags,
        'originOffsetMetres': offset.round(5).tolist(), 'muscleFit': fit(data),
        'playbackVsToolkit': worst(distances(runtime - offset, expected), paths),
        'meshVsToolkit': mesh_vs_toolkit(brief.rig, clip.world(), mapping, data['bakes'], offset, data.get('grounding')),
        'entryVsEntryClip': worst(distances(runtime[:1], start[None]), paths),
        'exitVsEntryClip': worst(distances(runtime[-1:], start[None]), paths),
        'playbackVsSampling': worst(distances(runtime, np.array(data['newSample'])), paths),
        'inBetweenJoints': data['inBetween'], 'asset': data['asset'], 'evidence': str(run_dir.relative_to(REPO))}
    if data.get('grounding'):
        report['grounding'] = data['grounding']
    if checked:
        report['phaseCheck'] = checked
    if brief.loop_from is not None:  # the last tick replays the loop's first: the seam as the game plays it
        report['loopSeam'] = worst(distances(runtime[-1:], runtime[brief.loop_from][None]), paths)
        if brief.loop_from:
            report['loopFromSeconds'] = brief.loop_from / 60
    (run_dir / 'report.json').write_text(json.dumps(report, indent=2))
    return report


def clip_source(source: str) -> dict:
    """An Assets/... .anim path, or a clip bound in the package catalog by semantic id (e.g. anim.bonk.a1)."""
    return {'asset': source} if source.startswith('Assets/') else {'semanticId': source}


def review_cameras(brief, result: Path | None, views, margin: float, offset=None, focus=None, focus_ticks=None) -> list[dict]:
    """Unity cameras for the toolkit's review views, framing the brief's entry pose and the toolkit result's whole
    motion with `margin` around it; `offset` is the originOffsetMetres of the `write` that made the clip. `focus` (joint
    names) frames only the vertices those joints and their children drive, and `focus_ticks` (first, last) only those
    ticks of the result: a hand close-up (2026-10-27, the salamander's finger gun was 60 px across at body framing)."""
    from genra.clip import Clip
    from genra.render import Renderer
    from genra.skin import Skin
    renderer = Renderer(Skin(brief.rig), brief.entry_frame)
    worlds = [] if result and focus_ticks else [brief.entry_pose.world()]
    if result:
        motion = list(Clip.load(brief.rig, result, frame=brief.entry_frame).world())
        worlds += motion[focus_ticks[0]:focus_ticks[1] + 1] if focus_ticks else motion
    masks = None
    if focus:
        joints = {j for name in focus for j in brief.rig.descendants(name)}
        masks = [np.isin(m['dominant'], list(joints)) for m in renderer.skin.meshes]
    def sample(world):
        meshes = renderer.skin.points(world)
        return np.concatenate([p[k] for p, k in zip(meshes, masks)] if masks else meshes)[::1 if masks else 5]
    points = np.concatenate([sample(w) for w in (worlds if masks else worlds[::2])])
    mirror = np.array([-1.0, 1.0, 1.0])  # pack space mirrors Unity's x; mirroring the camera too keeps the image
    cameras = []
    for view in views:  # each view frames the candidate's whole motion, with room for the clips compared with it
        right, up, toward = renderer.camera(view)
        x, y = points @ right, points @ up
        center = right * (x.min() + x.max()) / 2 + up * (y.min() + y.max()) / 2 + toward * float((points @ toward).mean())
        target = center * mirror + (offset if offset is not None else 0.0)
        cameras.append({'name': view, 'position': (target + toward * mirror * 20).tolist(), 'forward': (-toward * mirror).tolist(),
                        'up': (up * mirror).tolist(), 'size': max(np.ptp(x), np.ptp(y)) / 2 * margin})
    return cameras


def weapon_attachments(mapping, shown=None) -> list[dict]:
    """The pack's weapons for a render job, named as the pack's joints (Weapon<k>, the k-th in mapping.json). `shown` (a
    brief's props_shown) gives a weapon the tick windows it shows in, as the game's hide rules do: a prop that changes
    hands (Sir Bonk's pot lid: shoulder, then hand, then thrown) is drawn once, not on every bone that carries it."""
    shown = shown or {}
    return [{'name': f'Weapon{k}', 'bone': w['bone'], 'prefab': w['prefab'], 'positionOffset': w['positionOffset'],
             'rotationOffset': w['rotationOffset']} | ({'shown': shown[f'Weapon{k}']} if f'Weapon{k}' in shown else {})
            for k, w in enumerate(mapping.get('weapons', []))]


def render(brief_path: Path, clips: list[dict], result: Path | None = None, views=('gameplay', 'three-quarter-right', 'profile'),
           size=360, slow=4, name=None, offset=None, margin=1.3, springs=True, travel=0.0, background=None, focus=None,
           focus_ticks=None) -> dict:
    """Render clips on the character's actual prefab (materials, weapons, Animancer playback stretched over the brief's
    ticks, as the game plays them) from the toolkit's review views and framing, into one synchronized video: a row per
    clip, a column per view. `clips` are {'label', 'asset'} (an Assets/... .anim) or {'label', 'semanticId'} (the clip
    bound in the package catalog, e.g. the move it would replace). `result` (the toolkit clip) sets the framing.
    Presentation springs (BoneChainSpring: a tail, a cape) are stepped once per tick as in Play Mode unless `springs` is
    False; `travel` (metres per tick) carries the fighter forward so they trail as they do in a run. `background` is an
    RGB colour (0-255) for the frames, e.g. a light studio grey for first frames of generated videos. `focus` and
    `focus_ticks` frame a close-up (review_cameras)."""
    from PIL import Image, ImageDraw
    from genra.brief import Brief
    from genra.render import BACKGROUND, _font, encode_video, tick_label
    brief = Brief(brief_path)
    mapping = json.loads((brief.rigpack / 'mapping.json').read_text())
    clock = brief['clock']
    name = name or brief['name']
    run_dir = OUT / f'{time.strftime("%Y%m%d-%H%M%S")}-render-{name}'
    job = {'package': mapping['package'], 'prefab': pack_prefab(mapping), 'meshes': pack_meshes(mapping), 'clips': clips, 'ticks': clock['duration_ticks'], 'size': size * 2,
           'frames': str(run_dir / 'frames'), 'background': [c / 255 for c in (background or BACKGROUND)],
           'views': review_cameras(brief, result, views, margin, offset, focus, focus_ticks),
           'weapons': weapon_attachments(mapping, brief.get('props_shown')), 'springs': springs, 'travel': travel,
           'meshesShown': brief.get('meshes_shown')}
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
        tick_label(frame, tick, clock.get('contact_tick'), clock.get('active'), font)
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


def promote(source: str, target: str, source_run: Path | None = None, write_report: Path | None = None) -> dict:
    """Copy an accepted clip's content over a package clip (same GUID and name); before.anim keeps the old file. A missing
    target is created as a copy: bind it with sloparena.character.bind."""
    for path in (source, target):
        if not path.startswith('Assets/') or not path.endswith('.anim') or '..' in path:
            raise SystemExit(f'promote takes Assets/... .anim paths, not {path}')
    from genra.receipts import build_receipt, export_for, save_receipt
    export = export_for(source, UNITY / source, write_report, OUT)
    if source_run and export.get('sourceRun'):
        from genra.receipts import resolve
        if resolve(source_run) != resolve(export['sourceRun']):
            raise ValueError('--run-dir disagrees with the export source run')
    source_run = source_run or (Path(export['sourceRun']) if export.get('sourceRun') else None)
    # Validate provenance before changing the game asset. Hash the actual target after Unity preserves its name.
    receipt = build_receipt(target, source, UNITY / source, source_run, export)
    OUT.mkdir(parents=True, exist_ok=True)
    run_dir = Path(tempfile.mkdtemp(prefix=f'{time.strftime("%Y%m%d-%H%M%S")}-promote-{Path(target).stem}-', dir=OUT))
    if (UNITY / target).exists():
        shutil.copy2(UNITY / target, run_dir / 'before.anim')
    data = run_unity({'source': source, 'target': target}, run_dir, 'promote_clip')
    from genra.receipts import sha256
    receipt['clip_sha256'] = sha256(UNITY / target)
    data['receipt'] = str(save_receipt(receipt).relative_to(REPO))
    (run_dir / 'report.json').write_text(json.dumps(data, indent=2))
    return data | {'evidence': str(run_dir.relative_to(REPO))}


def timeline_rows(spec: dict) -> list[dict]:
    """The timeline spec's rows as render_clip jobs take them: each event's `clip` becomes an asset or semantic id. A
    row's `props_shown` (as a brief's, in the row's ticks) hides pack weapons outside their windows."""
    return [{'label': row['label'], 'moves': row.get('moves', []),
             'events': [{k: v for k, v in e.items() if k != 'clip'} | clip_source(e['clip']) for e in row['events']]}
            | ({'props_shown': row['props_shown']} if 'props_shown' in row else {})
            for row in spec['rows']]


def timeline(spec_path: Path, brief_path: Path, result: Path | None = None, views=('gameplay', 'three-quarter-right', 'front'),
             size=300, slow=4, name=None, margin=1.9, springs=True) -> dict:
    """Play clip sequences on the actual prefab as PlayerRenderer drives them, a row per variant: the handoffs a
    single-clip `render` cannot show (idle -> aim loop -> release into the attack, an attack's exit into idle).
    The spec is JSON: {"name", "ticks", "phases": [[tick, label]], "review_ticks": [...], "rows": [{"label", "title",
    "moves": [[first tick, last tick, metres per tick forward, optional metres per tick up]], "events": [{"tick", "clip"
    (an Assets/... .anim or a bound semantic id), "fade" (seconds), "time" (seconds), "speed", or "ticks" to stretch the
    clip over that many stage ticks as an ability does}], "props_shown": {"Weapon<k>": [[first tick, last tick]]}}],
    "floor": optional floor height in rig-pack metres (up), drawn as a slab so a landing or a stuck weapon reads}. The
    brief and toolkit result frame the views; the cameras follow the travel, up and down too."""
    from PIL import Image, ImageDraw
    from genra.brief import Brief
    from genra.render import BACKGROUND, _font, encode_video
    spec = json.loads(Path(spec_path).read_text())
    brief = Brief(brief_path)
    mapping = json.loads((brief.rigpack / 'mapping.json').read_text())
    ticks, rows = int(spec['ticks']), timeline_rows(spec)
    titles = {row['label']: row.get('title', row['label']) for row in spec['rows']}
    phases = sorted(spec.get('phases', []))
    name = name or spec.get('name') or Path(spec_path).stem
    run_dir = OUT / f'{time.strftime("%Y%m%d-%H%M%S")}-timeline-{name}'
    job = {'package': mapping['package'], 'prefab': pack_prefab(mapping), 'meshes': pack_meshes(mapping), 'rows': rows, 'ticks': ticks, 'size': size * 2, 'frames': str(run_dir / 'frames'),
           'background': [c / 255 for c in BACKGROUND], 'views': review_cameras(brief, result, views, margin),
           'weapons': weapon_attachments(mapping), 'springs': springs, 'floor': spec.get('floor')}
    data = run_unity(job, run_dir, 'render_clip')
    header, font, small = 28, _font(16), _font(13)
    def cell(label, view, tick):
        image = Image.open(run_dir / 'frames' / label / view / f'{tick:04d}.png').convert('RGB').resize((size, size), Image.LANCZOS)
        ImageDraw.Draw(image).text((6, size - 20), f'{titles[label]} · {view}', fill=(200, 205, 214), font=small)
        return image
    frames = []
    for tick in range(ticks + 1):
        frame = Image.new('RGB', (size * len(views), header + size * len(rows)), BACKGROUND)
        phase = next((label for start, label in reversed(phases) if start <= tick), '')
        ImageDraw.Draw(frame).text((8, 5), f't{tick}  {phase}', fill=(235, 238, 242), font=font)
        for r, row in enumerate(rows):
            for k, view in enumerate(views):
                frame.paste(cell(row['label'], view, tick), (k * size, header + r * size))
        frames.append(frame)
    video = encode_video(frames, run_dir / 'timeline.mp4', slow)
    review = spec.get('review_ticks') or sorted({t for start, _ in phases for t in (start, start + 4) if t <= ticks} | {ticks})
    sheet = Image.new('RGB', (200 * len(review), 200 * len(rows) * len(views)), BACKGROUND)
    for r, (row, view) in enumerate((row, v) for row in rows for v in views):
        for k, tick in enumerate(review):
            image = cell(row['label'], view, tick).resize((200, 200), Image.LANCZOS)
            ImageDraw.Draw(image).text((6, 4), f't{tick}', fill=(235, 238, 242), font=small)
            sheet.paste(image, (200 * k, 200 * r))
    sheet.save(run_dir / 'timeline-sheet.png')
    report = {'package': mapping['package'], 'name': name, 'rows': data['clips'], 'views': list(views), 'ticks': ticks,
              'unitySeconds': data['seconds'], 'video': str(video), 'sheet': str(run_dir / 'timeline-sheet.png'),
              'evidence': str(run_dir.relative_to(REPO))}
    (run_dir / 'report.json').write_text(json.dumps(report, indent=2))
    return report


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    sub = parser.add_subparsers(dest='command', required=True)
    trip = sub.add_parser('roundtrip', help='rebuild an approved reference clip and compare it with the original')
    trip.add_argument('package')
    trip.add_argument('clip', choices=['idle', 'run', 'jump', 'fall'])
    trip.add_argument('--pack', help='a labelled rig pack, e.g. nyx-unity-real (default <package>-unity)')
    out = sub.add_parser('write', help='convert a toolkit result into a Unity clip')
    out.add_argument('result')
    out.add_argument('--brief', required=True)
    out.add_argument('--name')
    out.add_argument('--ticks', help='write one phase only, first-last (e.g. 96-148)')
    out.add_argument('--retime', help="time warp of the phase: source:new tick pairs, e.g. '0:0,6:6,20:23,36:51'")
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
    show.add_argument('--margin', type=float, default=1.3,
                      help='framing around the motion; raise it when no --result frames the clip (another character)')
    show.add_argument('--name')
    show.add_argument('--no-springs', action='store_true', help='keep tails and capes (BoneChainSpring) at rest')
    show.add_argument('--background', help='frame colour r,g,b (0-255; default the review dark blue)')
    show.add_argument('--travel', type=float, default=0.0,
                      help='carry the fighter forward this many metres per tick (a run at ground speed), for springs')
    show.add_argument('--focus', help='close-up: frame only these joints and their children, e.g. RightHand (needs --result)')
    show.add_argument('--focus-ticks', help='close-up: frame only these ticks of the result, first-last (e.g. 6-18)')
    line = sub.add_parser('timeline', help='play clip sequences on the actual prefab as the game drives them (handoffs)')
    line.add_argument('spec', help='timeline JSON: rows of clip events and travel (see timeline())')
    line.add_argument('--brief', required=True, help='brief whose rig pack and entry pose frame the views')
    line.add_argument('--result', help='toolkit result.json that widens the framing to its motion')
    line.add_argument('--views', default='gameplay,three-quarter-right,front')
    line.add_argument('--size', type=int, default=300)
    line.add_argument('--margin', type=float, default=1.9, help='framing around the motion')
    line.add_argument('--name')
    line.add_argument('--no-springs', action='store_true', help='keep tails and capes (BoneChainSpring) at rest')
    up = sub.add_parser('promote', help="copy an accepted clip's content over a package clip (same GUID and name)")
    up.add_argument('source', help='the accepted Assets/... .anim')
    up.add_argument('--run-dir', type=Path, help='source authoring run, required for historical exports without provenance')
    up.add_argument('--write-report', type=Path, help='export report recording the source and final transforms')
    up.add_argument('--over', required=True, help='the package clip it replaces, Assets/CharacterPackages/<pkg>/...anim '
                                                  '(created if missing; then bind it)')
    args = parser.parse_args(argv)
    sys.path.insert(0, str(HERE.parent))
    if args.command == 'roundtrip':
        report = roundtrip(args.package, args.clip, args.pack)
    elif args.command == 'render':
        clips = []
        for item in args.clip:
            label, _, source = item.partition('=')
            clips.append({'label': label} | clip_source(source))
        offset = np.array(json.loads(Path(args.write_report).read_text())['originOffsetMetres']) if args.write_report else None
        report = render(Path(args.brief), clips, Path(args.result) if args.result else None, tuple(args.views.split(',')),
                        args.size, name=args.name, offset=offset, margin=args.margin, springs=not args.no_springs,
                        travel=args.travel, background=[int(c) for c in args.background.split(',')] if args.background else None,
                        focus=args.focus.split(',') if args.focus else None,
                        focus_ticks=tuple(int(t) for t in args.focus_ticks.split('-')) if args.focus_ticks else None)
    elif args.command == 'promote':
        report = promote(args.source, args.over, args.run_dir, args.write_report)
    elif args.command == 'timeline':
        report = timeline(Path(args.spec), Path(args.brief), Path(args.result) if args.result else None,
                          tuple(args.views.split(',')), args.size, name=args.name, margin=args.margin,
                          springs=not args.no_springs)
    else:
        from genra.brief import Brief
        brief = Brief(args.brief)
        name = args.name or brief['name']
        package = json.loads((brief.rigpack / 'mapping.json').read_text())['package']
        asset = args.asset or (f'Assets/Art/Experiments/GenRA/{package}/{name}.anim' if args.save else None)
        ticks = tuple(int(x) for x in args.ticks.split('-')) if args.ticks else None
        retime = [tuple(int(v) for v in pair.split(':')) for pair in args.retime.split(',')] if args.retime else None
        report = write(Path(args.result), Path(args.brief), asset, name, ticks, retime)
    print(json.dumps(report, indent=2))
    return 0


if __name__ == '__main__':
    sys.exit(main())
