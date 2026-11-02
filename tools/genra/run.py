"""Run one animation authoring job: brief -> Opus 5.5 with the genra toolkit -> checks, USD, review sheet.

    <genra python> tools/genra/run.py tools/genra/benchmarks/manki-aerial-double-fist.json [--probe|--dry-run]
    <genra python> tools/genra/run.py BRIEF.json --revise artifacts/genra-runs/<id> --review <review dir>/review.json
    <genra python> tools/genra/run.py BRIEF.json --stage keys                  # key poses only, for the owner
    <genra python> tools/genra/run.py BRIEF.json --keys artifacts/genra-runs/<keys run>   # the motion between them

--revise revises an earlier run: its scripts and result.json go into the new scratch's previous/, and the prompt adds
the director's notes (review.py) with their Unity evidence. loop.py chains reviews and revisions. With --owner and no
--review it revises on the owner's note alone, e.g. one key pose of a --stage keys run to re-pose.
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
import hashlib
import inspect
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
from genra.brief import print_preflight, Brief, brief_purpose  # noqa: E402
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


def guide_for_brief(data: dict) -> str:
    """Select tagged H2 sections. Tags on a section are alternatives; untagged text is shared."""
    purpose, _ = brief_purpose(data)
    tags = {purpose}
    looping = 'loop' in data or purpose in {'idle', 'locomotion', 'loop'}
    if not looping:
        tags.add('one-shot')
    elif purpose != 'idle':
        tags.add('loop')
    if data.get('planted'):
        tags.update({'planted', 'contact'})
    if data.get('two_hands'):
        tags.update({'two_hands', 'weapon', 'contact'})
    targets = data.get('targets', [])
    if any(t.get('kind') == 'arc' for t in targets):
        tags.update({'arc', 'weapon'})
    if data.get('weapon') or re.search(r'weapon|blade|hilt', json.dumps([data.get('strike', []), targets]), re.I):
        tags.add('weapon')
    if data.get('contacts'):
        tags.add('contact')
    text = (HERE / 'GUIDE.md').read_text().split('\n', 2)[2]
    sections = re.split(r'(?=^## )', text, flags=re.MULTILINE)
    selected = []
    for section in sections:
        route = re.search(r'^<!-- guide: ([a-z_ -]+) -->\n', section, re.MULTILINE)
        if route is None or tags.intersection(route[1].split()):
            selected.append(section if route is None else section[:route.start()] + section[route.end():])
    return ''.join(selected)


def build_prompt(brief: Brief) -> str:
    from genra.summary import rig_summary
    c = brief['clock']
    targets = '; '.join(f"{t['name']}" + (f" >= {t['min']}" if 'min' in t else '') + (f" <= {t['max']}" if 'max' in t else '')
                        for t in brief.get('targets', [])) or 'none'
    windows = ', '.join(f'{a}-{b}' for a, b in brief.get('fast_windows', []))
    fast = (f'Fast motion is expected in ticks {windows} (up to 90 degrees per tick in a joint). Elsewhere, more than '
            '35 degrees per tick fails as a snap.' if windows else 'More than 35 degrees per tick in a joint fails as a snap.')
    guide = guide_for_brief(brief.data)
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


OWNER_REVISION = """

## Revision {round}

An earlier version of this clip is in `previous/`: `previous/result.json` and the scripts that built it. The owner
looked at it and asked for the changes below; there is no director review. Change what the owner asks, keep everything
else as it is, and save the revised clip as `result.json`. Start from the previous script or clip
(`Clip.load(rig, 'previous/result.json', frame=b.entry_frame)`). The brief may have changed with the owner's note
(new targets): check each target yourself with the toolkit before saving.

The owner's direction: {owner}

Check report of the previous version:
{checks}"""


KEYS_STAGE = """

## This job: the key poses only (blocking)

The owner approves the key poses before anyone builds the motion between them. Author only these ticks: {beats}.
For each, pose the whole body against the reference images: line of action, silhouette from the gameplay camera,
weight, both hands, the weapon. Solve contacts at the keys (two_hand_grip, a stuck tip at its spot), not between
them, and do not add breakdowns, overlap or polish. Interpolate the keys with `Clip.from_keys` only so the file can be
checked; the checker reports key-pose failures separately from provisional transition risks. Save `result.json`, run
`./genra check result.json`, look at the key ticks from the gameplay, profile and front views, and reply with one short
line per key pose, what it shows (instead of the 8-line reply)."""

APPROVED_KEYS = """

## Approved key poses

The owner approved the key poses at ticks {ticks} in `keys/result.json`: load it with
`keys = Clip.load(rig, 'keys/result.json', frame=b.entry_frame)` and start each key from `keys.pose(t)`. Keep them:
the check fails a clip whose joints move more than 2 cm from them at those ticks (fingers may still close). The
runner's `result.json` is already `flow(b)` through these keys (its report is in `flow.txt`): look at it at game speed
first, then direct it with dials, `Accent` and `holds`, add breakdown keys where a path needs them, run contacts after
flow (carry, pin, track, hold_grip) and audit the keys with `key_drift`. If `flow.txt` says flow was not used, fix that
first (usually a breakdown key for a near half turn).

The runner has already saved a recoverable `result.json`. Start there or from `author.py`; `API.md` has working
imports and live signatures. Save progress before checking, including versions with unresolved failures. The runner
stops a job after 20 minutes without a changed, loadable checkpoint, or at its overall time limit. If a problem
resists, keep the best take, explain the tradeoff and stop. Preserve the approved intent; do not spend the whole job
chasing a score or an imperceptible pose difference. Exact locking applies only where the brief explicitly asks
for it; report any conflict with its machine constraints instead of silently changing the contract."""


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


def postprocess(brief: Brief, scratch: Path, run_dir: Path, stage: str = 'full', checkpoint_only=False) -> dict:
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
        report = check_clip(clip, brief.check_spec(stage='keys'), skin=skin)
        report['checkpointOnly'] = checkpoint_only
        (run_dir / 'check.json').write_text(json.dumps(report, indent=2, default=str))
        (run_dir / 'check.txt').write_text(format_report(report) + '\n')
        sheets = [] if checkpoint_only else key_sheets(brief, clip, run_dir, skin)
        return {'result': str(result), 'stage': 'keys', 'keys': list(brief.key_beats), 'passed': report['passed'],
                'failures': len(report['failures']), 'sheets': [str(p) for p in sheets]}
    report = check_clip(clip, brief.check_spec(stage='full'), skin=skin)
    report['checkpointOnly'] = checkpoint_only
    text = format_report(report)
    seen = {}
    clock = brief['clock']
    contact = clock.get('contact_tick')
    if brief.strike and contact is not None:  # review information, not a check
        seen = strike_visibility(clip, brief.strike, contact, brief.review_views + ('gameplay',), skin=skin)
        text += f'\n  strike visible at contact (tick {contact}): ' + ', '.join(f'{v} {f:.0%}' for v, f in seen.items())
    (run_dir / 'check.json').write_text(json.dumps(report, indent=2, default=str))
    (run_dir / 'check.txt').write_text(text + '\n')
    if checkpoint_only:
        return {'result': str(result), 'passed': report['passed'], 'failures': len(report['failures']),
                'warnings': len(report['warnings'])}
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


def prepare_brief(brief: Brief, scratch: Path, run_dir: Path, keys=None, stage='full') -> Brief:
    """Snapshot the contract and approved poses outside the animator's writable scratch.

    Both brief copies resolve their own keys, so authoring and later reviews can
    use the same contract without depending on /tmp or the original keys run.
    """
    resolved = dict(brief.data, rigpack=str(brief.rigpack), authoring_stage=stage)
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


def api_reference() -> str:
    """Small, scratch-readable API reference generated from the installed toolkit."""
    from genra.clip import Clip, Pose
    from genra import flow, solve
    symbols = [Clip.load, Clip.save, Clip.from_keys, Clip.pose, Clip.set_pose, Clip.overlap,
               Pose.copy, Pose.pos, Pose.rotate, Pose.axis, Pose.set_hinge, flow.flow, flow.Accent, flow.key_drift,
               solve.solve, solve.continuity_report, solve.two_bone, solve.two_hand_grip, solve.track, solve.carry, solve.hold_grip]
    lines = ['# Authoring API', '', 'Pose and Clip live in genra.clip; there is no genra.pose module.',
             'Use Pose.copy(), or load a Clip again. Do not deepcopy/pickle a USD stage.',
             'solve() returns (Pose, list[dict]); two_bone() edits a Pose and returns a float miss.',
             'two_hand_grip() returns (Pose, dict); carry()/track()/hold_grip() edit the Clip and return a dict.',
             'flow(b) (genra.flow) returns (Clip, FlowReport): the approved keys exactly, moving between them; with '
             'approved keys, result.json starts as flow(b) and flow.txt prints its report. Run contacts after it.', '']
    for symbol in symbols:
        lines += [f'## {symbol.__qualname__}{inspect.signature(symbol)}', '',
                  inspect.getdoc(symbol) or '', '']
    return '\n'.join(lines)


def seed_candidate(brief: Brief, scratch: Path, run_dir: Path) -> dict:
    """Initialize a recoverable candidate, without claiming that it passes checks. A full run on approved keys starts
    from flow(b), the keys exactly with the body moving between them, so a first take flows even when the animator
    changes nothing; scratch/flow.txt holds its report (or why the plain keys were used instead)."""
    from genra.clip import Clip
    n = brief['clock']['duration_ticks'] + 1
    source = next((p for p in (scratch / 'previous/result.json', scratch / 'keys/result.json') if p.is_file()), None)
    clip = Clip.load(brief.rig, source, frame=brief.entry_frame) if source else None
    seeded = {}
    if (clip is not None and len(clip) == n and source.parent.name == 'keys' and brief.get('approved_keys')
            and brief.get('authoring_stage', 'full') == 'full'):
        from genra.flow import flow
        try:
            clip, report = flow(brief)
            text, source = str(report), f'flow(b) through {source}'
            seeded['flow'] = {'before': report.flow_before, 'after': report.flow_after}
        except Exception as error:  # the plain keys still make a valid start; say why flow was not used
            text = f'flow(b) not used, starting from the plain keys: {type(error).__name__}: {error}'
            seeded['flowError'] = text
            print(f'WARNING: {text}', file=sys.stderr)
        (scratch / 'flow.txt').write_text(text + '\n')
    if clip is None or len(clip) != n:
        source = None  # a changed clock needs fresh authoring, never an implicit retime
        clip = Clip.from_keys({0: brief.entry_pose, n - 1: brief.exit_pose}, n)
    clip.save(scratch / 'result.json')
    shutil.copy2(scratch / 'result.json', run_dir / 'initial-result.json')
    shutil.copy2(scratch / 'result.json', run_dir / 'checkpoint-result.json')
    (scratch / 'author.py').write_text((HERE / 'author.py.template').read_text())
    (scratch / 'API.md').write_text(api_reference())
    digest = hashlib.sha256((scratch / 'result.json').read_bytes()).hexdigest()
    return {'source': str(source) if source else 'entry/exit placeholder', 'sha256': digest, 'initialSha256': digest,
            **seeded}


def capture_checkpoint(brief: Brief, scratch: Path, run_dir: Path, checkpoint: dict) -> bool:
    """Archive changed, structurally valid saves outside writable scratch, even if a move still fails checks."""
    import numpy as np
    from genra.clip import Clip
    path = scratch / 'result.json'
    try:
        data = path.read_bytes()
        digest = hashlib.sha256(data).hexdigest()
        if digest == checkpoint['sha256']:
            return False
        clip = Clip.load(brief.rig, path, frame=brief.entry_frame)
        n, joints = brief['clock']['duration_ticks'] + 1, len(brief.rig.names)
        if (any(values.shape != (n, joints, width) or not np.isfinite(values).all()
                for values, width in ((clip.t, 3), (clip.q, 4), (clip.s, 3)))
                or np.any(np.linalg.norm(clip.q, axis=2) < 1e-6)
                or path.read_bytes() != data):
            return False
    except (OSError, ValueError, KeyError, TypeError, IndexError):
        return False  # a partial/invalid save must not replace the last recoverable clip
    (run_dir / 'checkpoint-result.json').write_bytes(data)
    checkpoint.update(sha256=digest)
    return True


def assess_checkpoint(brief, run_dir, checkpoint, plateau, stage):
    """Check the archived bytes, not scratch; retain every measured candidate and report."""
    from genra.clip import Clip
    from genra.checks import check_clip
    archive = run_dir / 'checkpoints'
    archive.mkdir(exist_ok=True)
    number = checkpoint.get('assessed', 0)
    candidate = archive / f'{number:04d}-result.json'
    shutil.copy2(run_dir / 'checkpoint-result.json', candidate)
    checkpoint['assessed'] = number + 1
    report_path = archive / f'{number:04d}-check.json'
    try:
        clip = Clip.load(brief.rig, candidate, frame=brief.entry_frame)
        report = check_clip(clip, brief.check_spec(stage=stage), mode='fast')
        report['sha256'] = checkpoint['sha256']
        report.setdefault('keyTicks', sorted(brief.key_beats))
        report_path.write_text(json.dumps(report, indent=2, default=str))
        plateau.observe(json.loads(report_path.read_text()))
    except Exception as error:
        # A checker failure is not evidence of quality progress or of a plateau.
        plateau.stale = 0
        report_path.write_text(json.dumps({'sha256': checkpoint['sha256'],
                                         'error': f'{type(error).__name__}: {error}'}, indent=2))


def author(brief_path, name=None, model='claude-opus-5-5', effort='medium', timeout=1800,
           python=str(REPO / 'artifacts/genra-20261014/.venv/bin/python'), out_root=str(REPO / 'artifacts/genra-runs'),
           prompt_from=None, cache_1h=False, probe=False, dry_run=False, revise=None, review=None, round_number=1,
           stage='full', keys=None, owner=None, skip=(), checkpoint_timeout=1200, plateau_checkpoints=3):
    """One authoring (or revision) job. Returns (exit code, run directory). stage 'keys' authors only the key poses;
    `keys` is an approved keys run whose poses a full run keeps; a revision carries the owner's direction (`owner`)
    and leaves the review issues numbered in `skip` aside (revision_notes)."""
    plateau = claude_runner.CheckpointPlateau(plateau_checkpoints)
    brief = Brief(brief_path)
    print_preflight(brief.data)
    print('Route: approved keys -> first take -> owner look -> run.py --revise --owner (or --owner-file). Director is opt-in.')
    manifest = verify_rigpack(brief.rigpack)
    stamp = datetime.datetime.now().strftime('%Y%m%d-%H%M%S')
    run_id = f"{stamp}-{name or brief['name']}" + ('-probe' if probe else '-keys' if stage == 'keys' else '')
    run_dir = Path(out_root) / run_id
    scratch = Path('/tmp/genra-runs') / run_id
    run_dir.mkdir(parents=True)
    scratch.mkdir(parents=True)
    brief = prepare_brief(brief, scratch, run_dir, keys, stage)
    wrappers(scratch, python)
    venv = Path(python).parent.parent
    # A weapon normal's arc targets read the defenders' hurtboxes from a hit-reliability report: let the check see it
    # (Sunrise's keys, 2026-10-22: the sandbox hid it and `./genra check` crashed on the arc).
    reports = sorted({(REPO / t['report']).resolve() for t in brief.get('targets', []) if t.get('kind') == 'arc'})
    readable = [HERE, brief.rigpack, venv] + reports
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
        if review:
            text, evidence = revision_notes(Path(review), round_number, owner=owner, skip=skip)
        else:  # the owner's note alone (e.g. a key pose to re-pose): no director review to run first
            check = Path(revise) / 'check.txt'
            text = OWNER_REVISION.format(round=round_number, owner=owner,
                                         checks=check.read_text().strip() if check.exists() else '(none)')
            evidence = []
        prompt += text
        images = list(images) + evidence
    checkpoint = seed_candidate(brief, scratch, run_dir) if not probe else None
    if not probe:
        prompt += ('\n\nA starting result.json and author.py are already here. Read API.md for the actual signatures. '
                   'Save result.json before checks and after every verified fix; failed checks do not prevent saving. '
                   f'The runner stops after {plateau_checkpoints} changed checkpoints without reducing failures or '
                   'failing check kinds (0 disables this bound), and archives numeric checks with the saved candidates. '
                   'When a contact or grip check fails, first run ./genra continuity result.json. '
                   'Read the implicated key interval, roll, limit and reach diagnostics before another solve. '
                   'Use --replay-carry only for a chosen interval with --keys a,b; it runs one both-ways solve '
                   'on a copy and reports landing disagreement. Large key turns are risks, not proof of flips. '
                   'Quality warnings are advisory: preserve the move\'s intent and explain tradeoffs. '
                   'Do not add arbitrary sway just to lower a flow score.\n'
                   'Build flow into this first take. Normals default to a plain, short recovery with no extra '
                   'gesture or flourish; personality can live in the move itself, and specials can carry more.\n')
    (run_dir / 'prompt.txt').write_text(prompt)
    config = {'brief': str(Path(brief_path).resolve()), 'model': model, 'effort': effort, 'timeout': timeout,
              'python': python, 'scratch': str(scratch), 'readable': [str(p) for p in readable],
              'rigpackFiles': manifest['files'], 'promptCharacters': len(prompt), 'images': [str(p) for p in images],
              'promptFrom': prompt_from, 'cache5m': not cache_1h, 'revise': str(revise) if revise else None,
              'review': str(review) if review else None, 'stage': stage, 'keys': str(keys) if keys else None,
              'owner': owner, 'skippedIssues': list(skip), 'checkpointTimeout': checkpoint_timeout,
              'plateauCheckpoints': plateau_checkpoints,
              'initialCheckpoint': dict(checkpoint) if checkpoint else None}
    (run_dir / 'run-config.json').write_text(json.dumps(config, indent=2))
    print(f'{run_id}: prompt {len(prompt)} chars, {len(images)} images', flush=True)
    if dry_run:
        return 0, run_dir
    started = time.time()
    summary = {'status': 'running', 'total_cost_usd': None}
    (run_dir / 'summary.json').write_text(json.dumps(summary, indent=2))
    # No --add-dir: add-dir paths become writable sandbox working directories (even with denyWrite).
    def progress():
        changed = capture_checkpoint(brief, scratch, run_dir, checkpoint)
        if changed:
            assess_checkpoint(brief, run_dir, checkpoint, plateau, stage)
        return changed

    if checkpoint:
        assess_checkpoint(brief, run_dir, checkpoint, plateau, stage)

    try:
        code, result = claude_runner.run(prompt, images, scratch, run_dir, readable, [], model,
                                         effort, timeout=timeout, cache_5m=not cache_1h,
                                         progress=progress if checkpoint else None, progress_timeout=checkpoint_timeout,
                                         stop=lambda: plateau.stopped)
    except Exception as error:
        code, result = 1, None
        summary['providerError'] = f'{type(error).__name__}: {error}'
    wall = time.time() - started
    events = run_dir / 'claude-events.jsonl'
    calls = claude_runner.tool_calls(events) if events.is_file() else []
    (run_dir / 'tool-audit.json').write_text(json.dumps(audit(calls, readable + [scratch]), indent=2))
    if checkpoint:
        progress()
        summary.update(plateauCheckpoints=plateau.limit, plateauStale=plateau.stale)
        if code == 125:
            summary['nextTask'] = plateau.next_task
        saved = run_dir / 'checkpoint-result.json'
        if not (scratch / 'result.json').is_file() or (scratch / 'result.json').read_bytes() != saved.read_bytes():
            shutil.copy2(saved, scratch / 'result.json')
            summary['recoveredCheckpoint'] = True
        summary['checkpointOnly'] = checkpoint['sha256'] == checkpoint['initialSha256']
    shutil.copytree(scratch, run_dir / 'scratch', ignore=shutil.ignore_patterns('.claude'))
    summary.update(returncode=code, wallSeconds=round(wall, 1), toolCalls=len(calls),
                   status='timed_out' if code is None else 'stalled' if code == 124 else 'plateaued' if code == 125 else
                   'interrupted' if code == 130 else 'completed' if code == 0 else 'failed')
    if code == 0 and summary.get('checkpointOnly'):
        summary['status'] = 'no_authored_changes'
    if result:
        summary.update({k: result.get(k) for k in ('num_turns', 'total_cost_usd', 'duration_ms', 'usage')})
        (run_dir / 'reply.txt').write_text(result.get('result') or '')
    (run_dir / 'summary.json').write_text(json.dumps(summary, indent=2, default=str))
    if not probe:
        try:
            summary.update(postprocess(brief, scratch, run_dir, stage, summary['checkpointOnly']))
        except Exception as error:
            summary.update(status='postprocess_failed', postprocessError=f'{type(error).__name__}: {error}')
            code = 1
    (run_dir / 'summary.json').write_text(json.dumps(summary, indent=2, default=str))
    print(json.dumps({k: v for k, v in summary.items() if k != 'usage'}, indent=2, default=str), flush=True)
    return (0 if code == 0 and not summary.get('checkpointOnly') else 1), run_dir


def owner_direction(owner=None, owner_file=None):
    """Read Markdown verbatim, or serialize a JSON direction for the prompt."""
    if owner_file is None:
        return owner
    path = Path(owner_file)
    text = path.read_text()
    if path.suffix.lower() == '.json':
        value = json.loads(text)
        text = value if isinstance(value, str) else json.dumps(value, indent=2, ensure_ascii=False)
    if not text.strip():
        raise ValueError('owner direction file is empty')
    return text


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    parser.add_argument('brief')
    parser.add_argument('--name')
    parser.add_argument('--model', default='claude-opus-5-5')
    parser.add_argument('--effort', default='medium')
    parser.add_argument('--timeout', type=int, default=1800)
    parser.add_argument('--checkpoint-timeout', type=int, default=1200,
                        help='stop after this many seconds without a changed, loadable result (default 1200)')
    parser.add_argument('--plateau-checkpoints', type=int, default=3,
                        help='stop after K changed checkpoints without fewer failures or failing check kinds; 0 disables (default 3)')
    parser.add_argument('--python', default=str(REPO / 'artifacts/genra-20261014/.venv/bin/python'))
    parser.add_argument('--out-root', default=str(REPO / 'artifacts/genra-runs'))
    parser.add_argument('--prompt-from', help="reuse an earlier run's prompt.txt verbatim (replicates)")
    parser.add_argument('--cache-1h', action='store_true', help='1-hour prompt cache (default 5 minutes, cheaper writes)')
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument('--probe', action='store_true', help='cheap sandbox/toolkit probe instead of authoring')
    mode.add_argument('--dry-run', action='store_true', help='prepare prompt and scratch without a model call')
    parser.add_argument('--revise', help='an earlier run directory to revise (its scratch scripts and result.json)')
    parser.add_argument('--review', help="the director's review.json of that run (review.py); optional with --owner")
    parser.add_argument('--round', type=int, default=1, help='revision number, for the prompt')
    direction = parser.add_mutually_exclusive_group()
    direction.add_argument('--owner-file', help='Markdown or JSON owner direction; see briefs/OWNER-DIRECTION.md')
    direction.add_argument('--owner', help="the owner's direction for a revision, ahead of the director's review")
    parser.add_argument('--skip', default='', help='review issue numbers (from 1) the owner set aside, e.g. 2,5')
    parser.add_argument('--stage', choices=['full', 'keys'], default='full',
                        help='keys: author only the key poses (Brief.key_beats) for the owner to approve first')
    parser.add_argument('--keys', help='an approved --stage keys run (or a result.json of approved key poses): a full '
                        'run keeps its key poses')
    args = parser.parse_args(argv)
    try:
        args.owner = owner_direction(args.owner, args.owner_file)
    except (OSError, ValueError) as error:
        parser.error(str(error))
    if args.review and not args.revise:
        parser.error('--review needs --revise')
    if args.revise and not (args.review or args.owner):
        parser.error('--revise needs --review (the director\'s) or --owner (the owner\'s note alone)')
    if args.keys and args.stage == 'keys':
        parser.error('--keys hands approved key poses to a full run')
    code, _ = author(args.brief, args.name, args.model, args.effort, args.timeout, args.python, args.out_root,
                     args.prompt_from, args.cache_1h, args.probe, args.dry_run, args.revise, args.review, args.round,
                     args.stage, args.keys, args.owner, [int(k) for k in args.skip.split(',') if k.strip()],
                     args.checkpoint_timeout, args.plateau_checkpoints)
    return code


if __name__ == '__main__':
    sys.exit(main())
