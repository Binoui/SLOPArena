"""Run one animation authoring job: brief -> Opus 5.5 with the genra toolkit -> checks, USD, review sheet.

    <genra python> tools/genra/run.py tools/genra/benchmarks/manki-aerial-double-fist.json [--probe|--dry-run]
    <genra python> tools/genra/run.py BRIEF.json --revise artifacts/genra-runs/<id> --review <review dir>/review.json
    <genra python> tools/genra/run.py BRIEF.json --stage keys                  # key poses only, for the owner
    <genra python> tools/genra/run.py BRIEF.json --keys artifacts/genra-runs/<keys run>   # the motion between them

--revise revises an earlier run: its scripts and result.json go into the new scratch's previous/, and the prompt adds
the director's notes (review.py) with their Unity evidence. loop.py chains reviews and revisions.
--stage keys authors only the brief's key poses (Brief.key_beats: the video beats' ticks) and renders keys.png and,
with video beats, beats-keys.png for the owner. Once approved, --keys hands that run's poses (or a result.json of
approved key poses, e.g. keys whose arms were re-solved for the owner) to a full run as keys/result.json; its check
fails a clip that moves them (2026-10-21: Sir Bonk's upright launch and hunched haul were
pose problems that survived two 30-45 minute revisions, which spent their time solving transitions).

Run with the GenRA environment's Python (numpy, scipy, usd-core, Pillow); the same
interpreter is offered to the model. Evidence goes to artifacts/genra-runs/<id>/.
"""
from __future__ import annotations

import argparse
import datetime
import json
import re
import shutil
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
TOOLS = HERE.parent
REPO = TOOLS.parent
sys.path.insert(0, str(TOOLS))

from genra import claude_runner  # noqa: E402
from genra.brief import Brief  # noqa: E402
from genra.rigpack import verify_rigpack  # noqa: E402

PROMPT = """You are the animator for one bounded SlopArena animation job. Author the requested move on the actual \
rig with the genra toolkit, check it, look at the renders, fix what fails, then save it. Do not browse, install \
anything, delegate, or read files outside this directory except as the toolkit does.

## Brief: {name}
Character: {character}

{description}

## Clock and constraints (machine-checked)
- Ticks 0..{duration} at 60 Hz, so the clip has {samples} samples.{timing}{contract}
- {entry_rule}
- Rotations animate, and the hips may translate (`move_hips`) for crouches, lunges and weight shifts; the fighter \
itself stays in place. Other joint translations and all scales stay fixed.{planted}
- {fast}
- Numeric targets: {targets}.
- Attached images: {image_notes}

## Rig (already measured; do not re-derive)
{summary}

{guide}"""

PROBE = """Configuration probe. Run each numbered command once with the Bash tool, verbatim, then reply with ONLY a JSON
object mapping each number to "ok: <short output>" or "error: <short reason>".
1. ./genra summary | head -3
2. ./genra sheet reference:fall --ticks 0,20 --views profile,three-quarter-right --out probe.png
3. (Read tool) view probe.png and describe it in at most six words
4. ./py - <<'EOF'
from genra.brief import Brief
b = Brief('brief.json'); print(len(b.rig.names), b.entry_pose.pos('LeftHand').round(3))
EOF
5. head -c 20 {denied}
6. touch {repo}/tools/genra/probe-write-test.txt
7. touch {rigpack}/probe-write-test.txt"""


def wrappers(scratch: Path, python: str):
    for name, line in [('py', f'exec "{python}" "$@"'), ('genra', f'exec "{python}" -m genra "$@"')]:
        path = scratch / name
        path.write_text(f'#!/usr/bin/env bash\nexport PYTHONPATH="{TOOLS}"\n{line}\n')
        path.chmod(0o755)


def build_prompt(brief: Brief) -> str:
    from genra.summary import rig_summary
    c = brief['clock']
    targets = '; '.join(f"{t['name']}" + (f" >= {t['min']}" if 'min' in t else '') + (f" <= {t['max']}" if 'max' in t else '')
                        for t in brief.get('targets', [])) or 'none'
    windows = ', '.join(f'{a}-{b}' for a, b in brief.get('fast_windows', []))
    fast = (f'Fast motion is expected in ticks {windows} (up to 90 degrees per tick in a joint). Elsewhere, more than '
            '35 degrees per tick fails as a snap.' if windows else 'More than 35 degrees per tick in a joint fails as a snap.')
    guide = (HERE / 'GUIDE.md').read_text().split('\n', 2)[2]  # drop the title line
    duration = c['duration_ticks']
    timing = ''
    if 'contact_tick' in c:
        # A throw has a contact (the release) but no hitbox window of its own: the projectile is the hit.
        active = f"; active ticks {'-'.join(map(str, c['active']))}" if 'active' in c else ''
        timing = (f" Contact at tick {c['contact_tick']}{active}"
                  + (f"; recovery (IASA) at tick {c['iasa_tick']}." if 'iasa_tick' in c else '.'))
    if brief.entry_matches:
        exit_from = brief['exit']['from_tick'] if 'exit' in brief.data else duration
        entry_rule = f'Tick 0 is the entry pose `b.entry_pose`; ticks {exit_from}..{duration} must equal it exactly.'
        if 'reference' in brief.data.get('exit', {}):
            e = brief['exit']
            entry_rule = (f'Tick 0 is the entry pose `b.entry_pose`. Ticks {exit_from}..{duration} must equal the exit pose '
                          f"`b.exit_pose` exactly: tick {e.get('tick', 0)} of the reference '{e['reference']}', the clip "
                          'the game plays next.')
    else:
        entry_rule = ('`b.entry_pose` only sets the character frame (its hips are the origin, its facing is front); '
                      'tick 0 is free.')
    if brief.loop_from is not None:
        entry_rule += (f'\n- Ticks {brief.loop_from}..{duration} loop in the game: tick {duration} must equal tick '
                       f'{brief.loop_from} exactly, and the motion must keep its speed across the seam. Key that stretch '
                       f'with `Clip.from_keys(keys, {duration + 1}, cycle=({brief.loop_from}, {duration}))`.')
    contract = f'\n- Timing contract: {brief.timing_text()}' if brief.timing else ''
    return PROMPT.format(name=brief['name'], character=brief.get('character', ''), description=brief['description'],
                         duration=duration, samples=duration + 1, timing=timing, contract=contract, entry_rule=entry_rule,
                         fast=fast, targets=targets, image_notes=brief.get('image_notes', 'none'),
                         planted=(('\n- Planted (must not drift more than 1 cm): ' + ', '.join(brief['planted'])
                                   + '. Moving the hips or spine moves the legs: re-solve them with two_bone.')
                                  if brief.get('planted') else '')
                         + (f"\n- Both hands hold the weapon on ticks {brief['two_hands'][0]}-{brief['two_hands'][1]}: "
                            + ("the left hand holds loosely, anywhere on the handle beside the right hand, and the elbows "
                               "hang: two_hand_grip(..., hold='loose', elbows=True) at the keys, carry(..., hold='loose', "
                               "elbows=True, both_ways=True) between them." if brief.get('two_hands_hold') == 'loose'
                               else 'the left hand must stay on the grip (two_hand_grip, hold_grip).')
                            if brief.get('two_hands') else ''),
                         summary=rig_summary(brief), guide='## Toolkit\n' + guide)


REVISION = """

## Revision {round}

An earlier version of this clip is in `previous/`: `previous/result.json` and the scripts that built it. The animation
director reviewed it on the real character in Unity. Fix the issues below, keep what works, and save the revised clip
as `result.json`. Start from the previous script or clip (`Clip.load(rig, 'previous/result.json', frame=b.entry_frame)`)
unless an issue needs a different approach. Check each target yourself with the toolkit before saving.

Director's summary: {summary}
{owner}{accepted}
Issues:
{issues}

Keep: {keep}

Check report of the previous version:
{checks}

The last {count} attached images are the director's evidence:
{captions}"""


KEYS_STAGE = """

## This job: the key poses only (blocking)

The owner approves the key poses before anyone builds the motion between them. Author only these ticks: {beats}.
For each, pose the whole body against the reference images: line of action, silhouette from the gameplay camera,
weight, both hands, the weapon. Solve contacts at the keys (two_hand_grip, a stuck tip at its spot), not between
them, and do not add breakdowns, overlap or polish. Interpolate the keys with `Clip.from_keys` only so the file can be
checked; failures between the keys do not count in this stage, failures at the keys do. Save `result.json`, run
`./genra check result.json`, look at the key ticks from the gameplay, profile and front views, and reply with one short
line per key pose, what it shows (instead of the 8-line reply)."""

APPROVED_KEYS = """

## Approved key poses

The owner approved the key poses at ticks {ticks} in `keys/result.json`: load it with
`keys = Clip.load(rig, 'keys/result.json', frame=b.entry_frame)` and start each key from `keys.pose(t)`. Keep them:
the check fails a clip whose joints move more than 2 cm from them at those ticks (fingers may still close). Build the
motion between them: breakdowns, arcs, contacts (track, pin, hold_grip), overlap and polish."""


def key_sheets(brief: Brief, clip, run_dir: Path, skin) -> list[Path]:
    """The blocking stage's evidence for the owner: the key poses from the gameplay, side and front views, and with the
    brief's video beats each beat's video frame over the key pose it maps to (toolkit renders, no Unity)."""
    from genra.render import Renderer, contact_sheet
    paths = [run_dir / 'keys.png']
    contact_sheet(clip, list(brief.key_beats), ('gameplay', 'profile', 'front'), paths[0], skin=skin)
    beats = (brief.get('review') or {}).get('beats')
    if beats:
        from genra.review import beat_pairs
        listed = json.loads((REPO / beats['file']).read_text())['beats']
        chosen = [b | {'tick': beats['ticks'][b['label']], 'image': str(REPO / b['image'])}
                  for b in listed if b['label'] in beats['ticks'] and beats['ticks'][b['label']] < len(clip)]
        view = beats.get('view', 'profile')
        frames = run_dir / 'key-frames'
        (frames / view).mkdir(parents=True, exist_ok=True)
        renderer, world = Renderer(skin, clip.frame), clip.world()
        span, center = renderer.fit([world[b['tick']] for b in chosen])
        for b in chosen:
            renderer.image(world[b['tick']], view, 480, span, center).save(frames / view / f"{b['tick']:04d}.png")
        paths.append(run_dir / 'beats-keys.png')
        beat_pairs(chosen, frames, view).save(paths[1])
    return paths


def revision_notes(review_path: Path, round_number: int, evidence_images: int = 4, owner=None,
                   skip=()) -> tuple[str, list[Path]]:
    """The revision section of the prompt and the review images to attach (the first part of each Unity view and the
    hand close-ups). `owner` is the owner's direction for this revision, ahead of the director's; `skip` holds the
    numbers (from 1) of the director's issues the owner set aside, which the animator is told not to pursue (Sir Bonk's
    Barn Door, 2026-10-22: 'a smoothing pass', while the review also asked to re-pose an approved key)."""
    review_dir = review_path.parent
    notes = json.loads(review_path.read_text())['review']
    evidence = json.loads((review_dir / 'evidence.json').read_text())
    chosen = [(Path(i), c) for i, c in zip(evidence['images'], evidence['captions'])
              if Path(i).name.endswith('-1.png') or Path(i).name == 'hands.png'][:evidence_images]
    kept = [i for k, i in enumerate(notes['issues'], 1) if k not in set(skip)]
    dropped = [i for k, i in enumerate(notes['issues'], 1) if k in set(skip)]
    issues = '\n'.join(f"{k}. ({i['severity']}) {i['problem']}\n   - Ticks {i['ticks']}; {', '.join(i['joints'])}. "
                       f"Evidence: {i['evidence']}\n   - Fix: {i['fix']}\n   - Target: {i['target']}"
                       for k, i in enumerate(kept, 1)) or '(none)'
    said = (f'\nThe owner\'s direction for this revision, which comes before the director\'s: {owner}\n' if owner else '')
    said += ''.join(f"The owner set this review issue aside; do not pursue it: {i['problem']}\n" for i in dropped)
    accepted = ('\nThe director accepted this version; the issues are polish. Keep everything under Keep as it is unless an '
                'issue asks to change it, and re-check the earlier fixes (wrists, knees, clearances) before saving.\n'
                if notes['verdict'] == 'accept' else '')
    text = REVISION.format(round=round_number, summary=notes['summary'], owner=said, accepted=accepted, issues=issues,
                           keep='; '.join(notes.get('keep', [])),
                           checks=(review_dir / 'checks.txt').read_text().strip(), count=len(chosen),
                           captions='\n'.join(f'- {c}' for _, c in chosen))
    return text, [p for p, _ in chosen]


def audit(calls, allowed) -> dict:
    pattern = re.compile(re.escape(str(REPO)) + r"/[^\s'\"\\),;]+")
    referenced = sorted({m for c in calls for m in pattern.findall(json.dumps(c['input']))})
    outside = [p for p in referenced if not any(p.rstrip('/') == str(a) or p.startswith(str(a) + '/') for a in allowed)]
    return {'toolCalls': len(calls), 'errors': sum(1 for c in calls if c['isError']),
            'repositoryPathsOutsideAllowed': outside, 'calls': calls}


def postprocess(brief: Brief, scratch: Path, run_dir: Path, stage: str = 'full') -> dict:
    from genra.checks import check_clip, format_report
    from genra.clip import Clip
    from genra.render import contact_sheet, motion_overview, preview_video, strike_visibility
    from genra.skin import Skin
    from genra.usdio import write_usd
    result = scratch / 'result.json'
    if not result.exists():
        return {'result': None}
    clip = Clip.load(brief.rig, result, frame=brief.entry_frame)
    skin = Skin(brief.rig)
    if stage == 'keys':  # what the owner approves: the key poses, at the keys only
        report = check_clip(clip, brief.check_spec() | {'clearance_ticks': list(brief.key_beats), 'face': False,
                                                         'life': False}, skin=skin)
        ticks = set(brief.key_beats)
        report['failures'] = [f for f in report['failures'] if not f.get('ticks') or set(f['ticks']) & ticks]
        report['passed'] = not report['failures']
        (run_dir / 'check.json').write_text(json.dumps(report, indent=2, default=str))
        (run_dir / 'check.txt').write_text(format_report(report) + '\n')
        sheets = key_sheets(brief, clip, run_dir, skin)
        return {'result': str(result), 'stage': 'keys', 'keys': list(brief.key_beats), 'passed': report['passed'],
                'failures': len(report['failures']), 'sheets': [str(p) for p in sheets]}
    report = check_clip(clip, brief.check_spec(), skin=skin)
    text = format_report(report)
    seen = {}
    clock = brief['clock']
    contact = clock.get('contact_tick')
    if brief.strike and contact is not None:  # review information, not a check
        seen = strike_visibility(clip, brief.strike, contact, brief.review_views + ('gameplay',), skin=skin)
        text += f'\n  strike visible at contact (tick {contact}): ' + ', '.join(f'{v} {f:.0%}' for v, f in seen.items())
    (run_dir / 'check.json').write_text(json.dumps(report, indent=2, default=str))
    (run_dir / 'check.txt').write_text(text + '\n')
    usd = write_usd(clip, run_dir / 'usd' / f"{brief['name']}.usda")
    contact_sheet(clip, brief.review_ticks, brief.review_views, run_dir / 'sheet.png', skin=skin)
    motion_overview(clip, brief.split_tick, run_dir / 'motion.png', ('front', brief.review_views[2], 'profile'), skin=skin)
    try:  # timing can only be judged in motion: side and three-quarter views, real speed then slowed down
        preview = str(preview_video(clip, run_dir / 'preview.mp4', brief.preview_views, skin=skin,
                                    contact=contact, active=clock.get('active'), loop_from=brief.loop_from))
    except (OSError, RuntimeError) as error:
        preview = f'not written: {error}'
    return {'result': str(result), 'passed': report['passed'], 'failures': len(report['failures']),
            'warnings': len(report['warnings']), 'strikeVisibility': seen, 'usd': str(usd), 'preview': preview}


def prepare_brief(brief: Brief, scratch: Path, run_dir: Path, keys=None) -> Brief:
    """Snapshot the contract and approved poses outside the animator's writable scratch.

    Both brief copies resolve their own keys, so authoring and later reviews can
    use the same contract without depending on /tmp or the original keys run.
    """
    resolved = dict(brief.data, rigpack=str(brief.rigpack))
    approved = brief.get('approved_keys')
    if keys or approved:
        if keys:
            source = Path(keys)
            source = source if source.suffix == '.json' else source / 'scratch' / 'result.json'
            approved = {'ticks': list(brief.key_beats), 'run': str(keys)}
        else:
            source = brief.path.parent / approved['file']
        for directory in (run_dir, scratch):
            (directory / 'keys').mkdir()
            shutil.copy2(source, directory / 'keys' / 'result.json')
        resolved['approved_keys'] = dict(approved, file='keys/result.json')
    for directory in (run_dir, scratch):
        (directory / 'brief.json').write_text(json.dumps(resolved, indent=2))
    return Brief(run_dir / 'brief.json')


def author(brief_path, name=None, model='claude-opus-5-5', effort='medium', timeout=1800,
           python=str(REPO / 'artifacts/genra-20261014/.venv/bin/python'), out_root=str(REPO / 'artifacts/genra-runs'),
           prompt_from=None, cache_1h=False, probe=False, dry_run=False, revise=None, review=None, round_number=1,
           stage='full', keys=None, owner=None, skip=()):
    """One authoring (or revision) job. Returns (exit code, run directory). stage 'keys' authors only the key poses;
    `keys` is an approved keys run whose poses a full run keeps; a revision carries the owner's direction (`owner`)
    and leaves the review issues numbered in `skip` aside (revision_notes)."""
    brief = Brief(brief_path)
    manifest = verify_rigpack(brief.rigpack)
    stamp = datetime.datetime.now().strftime('%Y%m%d-%H%M%S')
    run_id = f"{stamp}-{name or brief['name']}" + ('-probe' if probe else '-keys' if stage == 'keys' else '')
    run_dir = Path(out_root) / run_id
    scratch = Path('/tmp/genra-runs') / run_id
    run_dir.mkdir(parents=True)
    scratch.mkdir(parents=True)
    brief = prepare_brief(brief, scratch, run_dir, keys)
    wrappers(scratch, python)
    venv = Path(python).parent.parent
    readable = [HERE, brief.rigpack, venv]
    if probe:
        prompt = PROBE.format(denied=REPO / 'artifacts/genra-manki-aerial-20261014/generated/raw-response.json', repo=REPO,
                              rigpack=brief.rigpack)
        images = []
    else:
        prompt = Path(prompt_from).read_text() if prompt_from else build_prompt(brief)
        images = brief.images
        if stage == 'keys':
            prompt += KEYS_STAGE.format(beats=', '.join(f'{t} ({label})' if label else str(t)
                                                        for t, label in brief.key_beats.items()))
        if brief.get('approved_keys'):
            prompt += APPROVED_KEYS.format(ticks=', '.join(map(str, brief['approved_keys']['ticks'])))
    if revise:
        previous = scratch / 'previous'
        previous.mkdir()
        for item in (Path(revise) / 'scratch').iterdir():
            if item.suffix == '.py' or item.name == 'result.json':
                shutil.copy2(item, previous / item.name)
        text, evidence = revision_notes(Path(review), round_number, owner=owner, skip=skip)
        prompt += text
        images = list(images) + evidence
    (run_dir / 'prompt.txt').write_text(prompt)
    config = {'brief': str(Path(brief_path).resolve()), 'model': model, 'effort': effort, 'timeout': timeout,
              'python': python, 'scratch': str(scratch), 'readable': [str(p) for p in readable],
              'rigpackFiles': manifest['files'], 'promptCharacters': len(prompt), 'images': [str(p) for p in images],
              'promptFrom': prompt_from, 'cache5m': not cache_1h, 'revise': str(revise) if revise else None,
              'review': str(review) if review else None, 'stage': stage, 'keys': str(keys) if keys else None,
              'owner': owner, 'skippedIssues': list(skip)}
    (run_dir / 'run-config.json').write_text(json.dumps(config, indent=2))
    print(f'{run_id}: prompt {len(prompt)} chars, {len(images)} images', flush=True)
    if dry_run:
        return 0, run_dir
    started = time.time()
    # No --add-dir: add-dir paths become writable sandbox working directories (even with denyWrite).
    code, result = claude_runner.run(prompt, images, scratch, run_dir, readable, [], model,
                                     effort, timeout=timeout, cache_5m=not cache_1h)
    wall = time.time() - started
    calls = claude_runner.tool_calls(run_dir / 'claude-events.jsonl')
    (run_dir / 'tool-audit.json').write_text(json.dumps(audit(calls, readable + [scratch]), indent=2))
    shutil.copytree(scratch, run_dir / 'scratch', ignore=shutil.ignore_patterns('.claude'))
    summary = {'returncode': code, 'wallSeconds': round(wall, 1), 'toolCalls': len(calls)}
    if result:
        summary.update({k: result.get(k) for k in ('num_turns', 'total_cost_usd', 'duration_ms', 'usage')})
        (run_dir / 'reply.txt').write_text(result.get('result') or '')
    if not probe:
        summary.update(postprocess(brief, scratch, run_dir, stage))
    (run_dir / 'summary.json').write_text(json.dumps(summary, indent=2, default=str))
    print(json.dumps({k: v for k, v in summary.items() if k != 'usage'}, indent=2, default=str), flush=True)
    return (0 if code == 0 else 1), run_dir


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    parser.add_argument('brief')
    parser.add_argument('--name')
    parser.add_argument('--model', default='claude-opus-5-5')
    parser.add_argument('--effort', default='medium')
    parser.add_argument('--timeout', type=int, default=1800)
    parser.add_argument('--python', default=str(REPO / 'artifacts/genra-20261014/.venv/bin/python'))
    parser.add_argument('--out-root', default=str(REPO / 'artifacts/genra-runs'))
    parser.add_argument('--prompt-from', help="reuse an earlier run's prompt.txt verbatim (replicates)")
    parser.add_argument('--cache-1h', action='store_true', help='1-hour prompt cache (default 5 minutes, cheaper writes)')
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument('--probe', action='store_true', help='cheap sandbox/toolkit probe instead of authoring')
    mode.add_argument('--dry-run', action='store_true', help='prepare prompt and scratch without a model call')
    parser.add_argument('--revise', help='an earlier run directory to revise (its scratch scripts and result.json)')
    parser.add_argument('--review', help="the director's review.json of that run (review.py)")
    parser.add_argument('--round', type=int, default=1, help='revision number, for the prompt')
    parser.add_argument('--owner', help="the owner's direction for a revision, ahead of the director's review")
    parser.add_argument('--skip', default='', help='review issue numbers (from 1) the owner set aside, e.g. 2,5')
    parser.add_argument('--stage', choices=['full', 'keys'], default='full',
                        help='keys: author only the key poses (Brief.key_beats) for the owner to approve first')
    parser.add_argument('--keys', help='an approved --stage keys run (or a result.json of approved key poses): a full '
                        'run keeps its key poses')
    args = parser.parse_args(argv)
    if bool(args.revise) != bool(args.review):
        parser.error('--revise and --review go together')
    if args.keys and args.stage == 'keys':
        parser.error('--keys hands approved key poses to a full run')
    code, _ = author(args.brief, args.name, args.model, args.effort, args.timeout, args.python, args.out_root,
                     args.prompt_from, args.cache_1h, args.probe, args.dry_run, args.revise, args.review, args.round,
                     args.stage, args.keys, args.owner, [int(k) for k in args.skip.split(',') if k.strip()])
    return code


if __name__ == '__main__':
    sys.exit(main())
