#!/usr/bin/env python3
"""Check, propose, record and recut combo clips (skills/social-clips/SKILL.md "Combo clips").

    python3 tools/combo_clip.py check <combo.json>...      # replay in Unity cheaply; every hit and the KO must match
    python3 tools/combo_clip.py propose <combo.json>...    # check, then add passing combos as proposed ideas
    python3 tools/combo_clip.py record <idea|combo.json>   # footage pack: one gateway pass per camera angle
    python3 tools/combo_clip.py edit <idea> [--no-upload]  # cut the clip from the pack and upload a take

record and edit accept a factory idea or a version-1 combo JSON; --cameras <plan.json> overrides the plan.
All edit cut bounds are inclusive simulation ticks, never video frame numbers.
"""
from __future__ import annotations

import argparse
import base64
import hashlib
import json
import re
import shutil
import subprocess
import sys
from pathlib import Path

import factory
import social_clip

ROOT = factory.ROOT
FPS = 60
DIVERGED = 'Unity diverges from the headless run, so this combo cannot be filmed'


def read(path: Path) -> dict:
    return json.loads(path.read_text())


def write(path: Path, value: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2) + '\n')


def digest(value: dict) -> str:
    return hashlib.sha256(json.dumps(value, sort_keys=True).encode()).hexdigest()


def safe_name(value: str) -> str:
    if not re.fullmatch(r'[a-zA-Z0-9][a-zA-Z0-9_-]*', value):
        raise ValueError(f'unsafe file name {value!r}')
    return value


def load_job(source: str) -> tuple[dict, dict, Path]:
    path = Path(source)
    if path.is_file():
        combo = read(path)
        job = {'id': combo['id'], 'combo': combo}
    else:
        job = factory.get(source)
        combo = job['combo']
        if isinstance(combo, str):
            combo = read(ROOT / combo)
    if combo['version'] != 1:
        raise ValueError('only combo version 1 is supported')
    safe_name(job['id'])
    fighters = combo['fighters']
    if len(fighters) != 2 or {f['role'] for f in fighters} != {'attacker', 'victim'}:
        raise ValueError('combo needs one attacker and one victim')
    if len({f['id'] for f in fighters}) != 2:
        raise ValueError('fighter ids must be distinct')
    for f in fighters:
        tape = base64.b64decode(f['tape'], validate=True)
        if not tape or len(tape) % 22:
            raise ValueError('tape needs whole 22-byte InputState records')
    return job, combo, ROOT / 'tmp/content' / job['id']


def current_content(combo: dict) -> dict:
    manifest = read(ROOT / 'content-cooked/roster/manifest.json')
    packages = {e['packageId']: e['requirement']['cookedContentHash'] for e in manifest['entries']}
    protocol = (ROOT / 'src/Shared/SimulationProtocol.cs').read_text()
    version = int(re.search(r'public const byte Version = (\d+);', protocol)[1])
    stage = safe_name(combo['stage'])
    return {'protocol': version, 'stageHash': hashlib.sha256((ROOT / f'data/arenas/{stage}.arena').read_bytes()).hexdigest(),
            'packages': {f['package']: packages[f['package']] for f in combo['fighters']}}


def require_current(combo: dict) -> None:
    current = current_content(combo)
    if current != combo['content']:
        raise ValueError(f"stale combo {combo['id']}: content differs; re-find. Expected {combo['content']}, current {current}")


def check(combo: dict, out: Path) -> bool:
    """Replay the tapes in Unity with one tiny camera; True when every hit and the KO tick match.

    Content is checked first, so a mismatch here is a Mono/.NET runtime divergence (seen 2026-11-02 on a
    Wibou run attack): the headless search found something Unity does not reproduce.
    """
    require_current(combo)
    ko = combo['timeline']['ko']
    folder = social_clip.fresh(out / 'check')
    spec = {'stage': combo['stage'], 'fighters': [
        {k: v for k, v in f.items() if k in ('id', 'package', 'feet', 'yaw', 'damage', 'tape')} for f in combo['fighters']],
        'cameras': [{'name': 'c', 'pos': [0, 40, -1], 'look': [0, 0, 0], 'fov': 60, 'far': 4000}],
        'capture': {'from': ko, 'to': ko, 'step': 1, 'width': 320, 'height': 180, 'quality': 60}}
    write(folder / 'spec.json', spec)
    try:
        verify(combo, shoot(folder / 'spec.json', folder / 'run'), folder / 'run')
        return True
    except ValueError as error:
        print(f"{combo['id']}: {error}", flush=True)
        return False


def cmd_check(args) -> None:
    failed = 0
    for source in args.combos:
        _, combo, out = load_job(source)
        failed += not check(combo, out)
    if failed:
        raise SystemExit(f'{failed} combo(s) do not reproduce in Unity')


def cmd_propose(args) -> None:
    # Server body() reads Content-Length without a size cap; add_idea persists
    # arbitrary fields as SQLite JSON. No tape truncation or path fallback needed.
    for source in args.combos:
        _, combo, out = load_job(source)
        if not args.no_check and not check(combo, out):
            print(f"{combo['id']} skipped: not reproducible in Unity", flush=True)
            continue
        idea_id = re.sub('[^a-z0-9-]', '-', combo['id'].lower())
        idea = {'id': idea_id, 'format': 'combo', 'status': 'proposed',
                'title': combo['summary'], 'idea': combo['summary'], 'combo': combo}
        print(factory.call('POST', '/api/ideas', idea)['id'], 'added')


def shoot(spec: Path, run: Path) -> dict:
    batch = run.parent / f'{run.name}.batch.json'
    with batch.open('w') as out:
        subprocess.run([sys.executable, 'tools/photo_shoot.py', 'batch', social_clip.rel(spec), '--output', social_clip.rel(run)],
                       cwd=ROOT, stdout=out, check=True)
    result = run.parent / f'{run.name}.out.json'
    error = run.parent / f'{run.name}.err'
    with result.open('w') as out, error.open('w') as err:
        code = subprocess.run(['systemd-run', '--user', '--scope', '--quiet', 'bun',
                               'scripts/unity-editor-gateway.ts', '--project-path', 'client/Unity',
                               '--batch', str(batch)], cwd=ROOT, stdout=out, stderr=err).returncode
    if code:
        # Not a divergence: the Editor did not run the shoot, so nothing was compared. Stop instead of skipping.
        raise RuntimeError(f'gateway exited {code}; stop and read {result} and {error}')
    return social_clip.shoot_result(result)


def wire_slots() -> dict[str, int]:
    # Read the authoritative constants, rather than create another slot mapping.
    source = (ROOT / 'src/Shared/AbilitySlots.cs').read_text()
    constants = {name: int(n) for name, n in re.findall(r'const byte (\w+) = (\d+)', source)}
    return {label: constants['Normal' + label if label.isdigit() else label] for label in '123AERF'}


def verify(combo: dict, shot: dict, run: Path) -> None:
    attacker = next(f for f in combo['fighters'] if f['role'] == 'attacker')
    victim = next(f for f in combo['fighters'] if f['role'] == 'victim')
    timeline, slots = combo['timeline'], wire_slots()
    hits = [h for h in shot['hits'] if h['tick'] <= timeline['ticks'] and not h['blocked']]
    taken = sum(h['target'] == str(attacker['id']) for h in hits)
    if taken != timeline['taken']:
        raise ValueError(f"first differing hit: attacker took {taken}, expected {timeline['taken']}; {DIVERGED}")
    mine = [h for h in hits if h['owner'] == str(attacker['id']) and h['target'] == str(victim['id'])]
    if 'hits' in timeline:
        expected = [(h['tick'], h['owner'], h['slot'], round(h['damage'], 3), h['blocked']) for h in timeline['hits']]
        actual = [(h['tick'], int(h['owner']), h['slot'], round(h['damage'], 3), h['blocked'])
                  for h in shot['hits'] if h['tick'] <= timeline['ticks']]
        for want, got in zip(expected, actual):
            if want != got:
                raise ValueError(f'first differing hit: expected {want}, got {got}; {DIVERGED}')
        if len(expected) != len(actual):
            raise ValueError(f'hit count differs: expected {len(expected)}, got {len(actual)}; {DIVERGED}')
    # Merged moves (all a combo carried before raw hits): compare each move's first/last
    # tick, wire slot and total damage, and reject every extra landed hit. Raw hits above
    # already prove this, and a projectile landing after a later move breaks the merge.
    index = 0
    for move in ([] if 'hits' in timeline else timeline['moves']):
        actual = []
        while index < len(mine) and mine[index]['tick'] <= move['lastTick']:
            actual.append(mine[index])
            index += 1
        expected_slot = slots[move['label'][-1]]
        if (not actual or actual[0]['tick'] != move['tick'] or actual[-1]['tick'] != move['lastTick']
                or any(h['slot'] != expected_slot for h in actual)
                or abs(sum(h['damage'] for h in actual) - move['damage']) > 0.001):
            raise ValueError(f'first differing hit: expected {move}, got {actual}; {DIVERGED}')
    if 'hits' not in timeline and index != len(mine):
        raise ValueError(f'first differing hit: unexpected {mine[index]}; {DIVERGED}')
    deaths = victim.get('deaths', 0)
    ko = None
    for line in (run / 'states.jsonl').read_text().splitlines():
        row = json.loads(line)
        state = next(f for f in row['fighters'] if f['id'] == victim['id'])
        if state['deaths'] > deaths:
            ko = row['tick']
            break
    if ko != timeline['ko']:
        raise ValueError(f"KO tick differs: expected {timeline['ko']}, got {ko}; {DIVERGED}")
    print(f"verified {len(mine)} hits / {len(timeline['moves'])} moves, taken {taken}, KO {ko}", flush=True)


def cmd_record(args) -> None:
    job, combo, out = load_job(args.source)
    require_current(combo)
    for orientation in args.orientation.split(','):
        if orientation not in ('portrait', 'landscape'):
            raise ValueError('orientation must be portrait or landscape')
        folder = social_clip.fresh(out / 'footage' / orientation)
        folder.mkdir(parents=True, exist_ok=True)
        combo_path = folder / 'combo.json'
        write(combo_path, combo)
        if args.cameras:
            plan = read(Path(args.cameras))
        else:
            # Never let this build copy the Shared plugin into the open Editor (that needs a lease).
            result = subprocess.run(['dotnet', 'run', '--project', 'tools/ComboFinder', '-c', 'Release',
                                     '-p:SkipUnityPluginCopy=true', '--',
                                     'cameras', str(combo_path), '--orientation', orientation], cwd=ROOT,
                                    capture_output=True, text=True, check=True)
            plan = json.loads(result.stdout)
        if plan['combo'] != combo['id'] or plan['orientation'] != orientation:
            raise ValueError('camera plan combo/orientation does not match')
        wanted = args.angles.split(',') if args.angles else [a['name'] for a in plan['angles']]
        victim_id = next(f['id'] for f in combo['fighters'] if f['role'] == 'victim')
        angles = {a['name']: a for a in plan['angles']}
        if len(set(wanted)) != len(wanted) or not set(wanted) <= angles.keys():
            raise ValueError(f'angles must be unique names in {list(angles)}')
        pack = {'version': 1, 'combo': combo['id'], 'comboHash': digest(combo), 'content': combo['content'],
                'cameras': plan, 'angles': {}, 'verified': False}
        write(folder / 'pack.json', pack)
        for name in wanted:
            safe_name(name)
            angle = angles[name]
            fighters = [{k: v for k, v in f.items() if k in ('id', 'package', 'feet', 'yaw', 'damage', 'tape')}
                        for f in combo['fighters']]
            if angle.get('victimRespawn'):
                # Out of this angle's view, so the frames after the KO show the burst and no respawned victim.
                next(f for f in fighters if f['id'] == victim_id)['respawn'] = angle['victimRespawn']
            spec = {'stage': combo['stage'], 'fighters': fighters, 'cameras': [dict(angle['camera'], name=name)],
                'capture': {'from': angle['from'], 'to': angle['to'], 'step': 1,
                            'width': plan['width'], 'height': plan['height'], 'quality': 92}}
            if angle['to'] < combo['timeline']['ko']:
                raise ValueError(f'{name} capture must include the KO tick for verification')
            spec_path = folder / f'{name}.spec.json'
            write(spec_path, spec)
            run = folder / f'{name}-run'
            shot = shoot(spec_path, run)
            verify(combo, shot, run)
            frames = sorted(int(p.stem) for p in (run / name).glob('*.jpg'))
            if frames != list(range(angle['from'], angle['to'] + 1)):
                raise ValueError(f'{name}: missing or extra captured frames')
            subprocess.run([sys.executable, 'tools/photo_shoot.py', 'clip', str(run), '--camera', name,
                            '--from', str(angle['from']), '--to', str(angle['to']), '--width', str(plan['width']),
                            '--height', str(plan['height']), '--out', str(folder / f'{name}.mp4')], cwd=ROOT, check=True)
            shutil.copyfile(run / 'audio.wav', folder / f'{name}.wav')
            pack['angles'][name] = {'from': angle['from'], 'to': angle['to'], 'frames': len(frames)}
        require_current(combo)
        pack['verified'] = True
        write(folder / 'pack.json', pack)
        print(folder / 'pack.json', flush=True)


def cmd_edit(args) -> None:
    job, combo, out = load_job(args.source)
    require_current(combo)
    folder = out / 'footage' / args.orientation
    pack = read(folder / 'pack.json')
    if not pack['verified'] or pack['comboHash'] != digest(combo) or pack['content'] != combo['content']:
        raise ValueError('footage pack missing verification or stale; record again')
    edit = pack['cameras']['edit']
    cuts = json.loads(args.cuts) if args.cuts else job.get('edit')
    if cuts is None:
        cuts = [{'angle': 'side', 'to': edit['cut'] - 1}, {'angle': 'kill'}]
    if not isinstance(cuts, list) or not cuts:
        raise ValueError('cuts must be a nonempty list')
    work = social_clip.fresh(out / f'edit-{args.orientation}')
    work.mkdir(parents=True)
    start, filters, inputs, pairs = edit['from'], [], [], []
    for i, cut in enumerate(cuts):
        name, end = safe_name(cut['angle']), cut.get('to', edit['end'])
        angle = pack['angles'][name]
        if not angle['from'] <= start <= end <= min(angle['to'], edit['end']):
            raise ValueError(f'{name}: cut {start}-{end} outside captured window or edit end')
        inputs += ['-i', str(folder / f'{name}.mp4'), '-i', str(folder / f'{name}.wav')]
        offset, frames = start - angle['from'], end - start + 1
        hold = ',tpad=stop_mode=clone:stop_duration=0.6' if i == len(cuts) - 1 else ''
        seconds = frames / FPS + (0.6 if hold else 0)
        filters += [f'[{2*i}:v]trim=start_frame={offset}:end_frame={offset+frames},setpts=PTS-STARTPTS{hold}[v{i}]',
                    f'[{2*i+1}:a]atrim=start={offset/FPS},asetpts=PTS-STARTPTS,apad,atrim=duration={seconds}[a{i}]']
        pairs.append(f'[v{i}][a{i}]')
        start = end + 1
    filters.append(''.join(pairs) + f'concat=n={len(cuts)}:v=1:a=1[v][a]')
    subprocess.run(['ffmpeg', '-v', 'error', '-y', *inputs, '-filter_complex', ';'.join(filters),
                    '-map', '[v]', '-map', '[a]', '-c:v', 'libx264', '-preset', 'slow', '-crf', '18',
                    '-pix_fmt', 'yuv420p', '-c:a', 'pcm_s16le', str(work / 'joined.mkv')], check=True)
    final = work / f"{job['id']}-{args.orientation}.mp4"
    social_clip.normalize(work / 'joined.mkv', final)
    write(work / 'edit.json', {'comboHash': digest(combo), 'orientation': args.orientation, 'cuts': cuts, 'hold': 0.6})
    sheet = work / 'sheet.jpg'
    duration = (start - edit['from']) / FPS + 0.6
    subprocess.run(['ffmpeg', '-v', 'error', '-y', '-i', str(final), '-vf',
                    f'fps={20/duration},scale=216:-1,tile=5x4', '-frames:v', '1', str(sheet)], check=True)
    print(final)
    print(sheet)
    if not args.no_upload:
        subprocess.run([sys.executable, 'tools/factory.py', 'take', job['id'], str(final), '--sheet', str(sheet)],
                       cwd=ROOT, check=True)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest='command', required=True)
    propose = sub.add_parser('propose')
    propose.add_argument('combos', nargs='+')
    propose.add_argument('--no-check', action='store_true', help='skip the Unity reproduction check')
    propose.set_defaults(func=cmd_propose)
    checker = sub.add_parser('check', help='replay combos in Unity at low cost and verify every hit and the KO')
    checker.add_argument('combos', nargs='+')
    checker.set_defaults(func=cmd_check)
    record = sub.add_parser('record')
    record.add_argument('source', help='factory idea id or combo.json')
    record.add_argument('--orientation', default='portrait', help='portrait, landscape, or comma-separated both')
    record.add_argument('--angles', help='comma-separated plan angle names; default all')
    record.add_argument('--cameras', help='camera plan JSON override, for one orientation')
    record.set_defaults(func=cmd_record)
    edit = sub.add_parser('edit')
    edit.add_argument('source', help='factory idea id or local combo.json')
    edit.add_argument('--orientation', choices=('portrait', 'landscape'), default='portrait')
    edit.add_argument('--cuts', help='JSON cut list overriding the idea edit field')
    edit.add_argument('--no-upload', action='store_true')
    edit.set_defaults(func=cmd_edit)
    args = parser.parse_args()
    try:
        args.func(args)
    except (ValueError, RuntimeError, KeyError, OSError, subprocess.CalledProcessError) as error:
        parser.exit(1, f'combo_clip: {error}\n')


if __name__ == '__main__':
    main()
