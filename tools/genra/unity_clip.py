"""Write a genra clip into Unity as a hybrid Humanoid .anim on the character's own prefab, measured in Unity.

    <genra python> tools/genra/unity_clip.py roundtrip fightguy idle     # proof: approved clip -> pack -> Unity clip
    <genra python> tools/genra/unity_clip.py write RESULT.json --brief BRIEF.json [--save | --asset Assets/...anim]

Rig packs mirror Unity's x axis (unity_rigpack.py); this inverts that exactly. unity/write_clip.cs.template turns
mapped Humanoid bones into muscle and body (RootT/RootQ) curves with HumanPoseHandler, keeps exact local transform
curves for every other joint and bakes root motion into the pose: PlayerRenderer plays clips through Animancer with
applyRootMotion off, so the hips may move but the fighter never does. Unity measures the clip as the game plays it.

`roundtrip` rebuilds an approved reference clip from its rig-pack samples and compares bones and skinned vertices
with the original. `write` converts a toolkit result: the body origin is matched to the entry clip's actual
playback at the entry tick, and the clip is saved as a new, unbound asset only with --save or --asset.
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


def run_unity(job: dict, run_dir: Path) -> dict:
    """Render the writer template for this job and run it with the gateway's eval_file (own lease)."""
    run_dir.mkdir(parents=True, exist_ok=True)
    job_path, out_path = run_dir / 'job.json', run_dir / 'unity.json'
    job_path.write_text(json.dumps(job))
    code = (HERE / 'unity/write_clip.cs.template').read_text().replace('{{JOB}}', str(job_path)).replace('{{OUTPUT}}', str(out_path))
    script = run_dir / 'write_clip.cs'
    script.write_text(code)
    command = ['bun', str(REPO / 'scripts/unity-editor-gateway.ts'), '--project-path', str(UNITY), '--',
               'eval_file', '--file', str(script), '--timeout', '600000']
    result = subprocess.run(command, cwd=REPO, capture_output=True, text=True)
    (run_dir / 'gateway.log').write_text(result.stdout + '\n--- stderr ---\n' + result.stderr)
    if result.returncode != 0 or not out_path.exists():
        raise SystemExit(f'Unity clip writer failed; see {run_dir / "gateway.log"}')
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
    args = parser.parse_args(argv)
    sys.path.insert(0, str(HERE.parent))
    if args.command == 'roundtrip':
        report = roundtrip(args.package, args.clip)
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
