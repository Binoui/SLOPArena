"""Run one animation authoring job: brief -> Opus 5.5 with the genra toolkit -> checks, USD, review sheet.

    <genra python> tools/genra/run.py tools/genra/benchmarks/manki-aerial-double-fist.json [--probe|--dry-run]

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
- Ticks 0..{duration} at 60 Hz, so the clip has {samples} samples. Contact at tick {contact}; active ticks {active}; \
recovery (IASA) at tick {iasa}.
- Tick 0 is the entry pose `b.entry_pose`; ticks {exit}..{duration} must equal it exactly.
- Rotations animate, and the hips may translate (`move_hips`) for crouches, lunges and weight shifts; the fighter \
itself stays in place. Other joint translations and all scales stay fixed.{planted}
- Fast motion is expected in ticks {fast} (up to 90 degrees per tick in a joint). Elsewhere, more than 35 degrees per tick fails as a snap.
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
    fast = ', '.join(f'{a}-{b}' for a, b in brief.get('fast_windows', [])) or 'none'
    guide = (HERE / 'GUIDE.md').read_text().split('\n', 2)[2]  # drop the title line
    return PROMPT.format(name=brief['name'], character=brief.get('character', ''), description=brief['description'],
                         duration=c['duration_ticks'], samples=c['duration_ticks'] + 1, contact=c['contact_tick'],
                         active='-'.join(map(str, c['active'])), iasa=c['iasa_tick'],
                         exit=brief['exit']['from_tick'] if 'exit' in brief.data else c['duration_ticks'],
                         fast=fast, targets=targets, image_notes=brief.get('image_notes', 'none'),
                         planted=(('\n- Planted (must not drift more than 1 cm): ' + ', '.join(brief['planted'])
                                   + '. Moving the hips or spine moves the legs: re-solve them with two_bone.')
                                  if brief.get('planted') else '')
                         + (f"\n- Both hands hold the weapon on ticks {brief['two_hands'][0]}-{brief['two_hands'][1]}: "
                            'the left hand must stay on the grip (two_hand_grip, hold_grip).' if brief.get('two_hands') else ''),
                         summary=rig_summary(brief), guide='## Toolkit\n' + guide)


def audit(calls, allowed) -> dict:
    pattern = re.compile(re.escape(str(REPO)) + r"/[^\s'\"\\),;]+")
    referenced = sorted({m for c in calls for m in pattern.findall(json.dumps(c['input']))})
    outside = [p for p in referenced if not any(p.rstrip('/') == str(a) or p.startswith(str(a) + '/') for a in allowed)]
    return {'toolCalls': len(calls), 'errors': sum(1 for c in calls if c['isError']),
            'repositoryPathsOutsideAllowed': outside, 'calls': calls}


def postprocess(brief: Brief, scratch: Path, run_dir: Path) -> dict:
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
    report = check_clip(clip, brief.check_spec(), skin=skin)
    text = format_report(report)
    seen = {}
    if brief.strike:  # review information, not a check
        contact = brief['clock']['contact_tick']
        seen = strike_visibility(clip, brief.strike, contact, brief.review_views + ('gameplay',), skin=skin)
        text += f'\n  strike visible at contact (tick {contact}): ' + ', '.join(f'{v} {f:.0%}' for v, f in seen.items())
    (run_dir / 'check.json').write_text(json.dumps(report, indent=2, default=str))
    (run_dir / 'check.txt').write_text(text + '\n')
    usd = write_usd(clip, run_dir / 'usd' / f"{brief['name']}.usda")
    contact_sheet(clip, brief.review_ticks, brief.review_views, run_dir / 'sheet.png', skin=skin)
    clock = brief['clock']
    motion_overview(clip, clock['contact_tick'], run_dir / 'motion.png', ('front', brief.review_views[2], 'profile'), skin=skin)
    try:  # timing can only be judged in motion: side and three-quarter views, real speed then slowed down
        preview = str(preview_video(clip, run_dir / 'preview.mp4', brief.review_views[::2], skin=skin,
                                    contact=clock['contact_tick'], active=clock['active']))
    except (OSError, RuntimeError) as error:
        preview = f'not written: {error}'
    return {'result': str(result), 'passed': report['passed'], 'failures': len(report['failures']),
            'warnings': len(report['warnings']), 'strikeVisibility': seen, 'usd': str(usd), 'preview': preview}


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
    args = parser.parse_args(argv)

    brief = Brief(args.brief)
    manifest = verify_rigpack(brief.rigpack)
    stamp = datetime.datetime.now().strftime('%Y%m%d-%H%M%S')
    run_id = f"{stamp}-{args.name or brief['name']}" + ('-probe' if args.probe else '')
    run_dir = Path(args.out_root) / run_id
    scratch = Path('/tmp/genra-runs') / run_id
    run_dir.mkdir(parents=True)
    scratch.mkdir(parents=True)
    resolved = dict(brief.data, rigpack=str(brief.rigpack))
    (scratch / 'brief.json').write_text(json.dumps(resolved, indent=2))
    (run_dir / 'brief.json').write_text(json.dumps(resolved, indent=2))
    wrappers(scratch, args.python)
    venv = Path(args.python).parent.parent
    readable = [HERE, brief.rigpack, venv]
    if args.probe:
        prompt = PROBE.format(denied=REPO / 'artifacts/genra-manki-aerial-20261014/generated/raw-response.json', repo=REPO,
                              rigpack=brief.rigpack)
        images = []
    else:
        prompt = Path(args.prompt_from).read_text() if args.prompt_from else build_prompt(brief)
        images = brief.images
    (run_dir / 'prompt.txt').write_text(prompt)
    config = {'brief': str(Path(args.brief).resolve()), 'model': args.model, 'effort': args.effort, 'timeout': args.timeout,
              'python': args.python, 'scratch': str(scratch), 'readable': [str(p) for p in readable],
              'rigpackFiles': manifest['files'], 'promptCharacters': len(prompt), 'images': [str(p) for p in images],
              'promptFrom': args.prompt_from, 'cache5m': not args.cache_1h}
    (run_dir / 'run-config.json').write_text(json.dumps(config, indent=2))
    print(f'{run_id}: prompt {len(prompt)} chars, {len(images)} images', flush=True)
    if args.dry_run:
        return 0
    started = time.time()
    # No --add-dir: add-dir paths become writable sandbox working directories (even with denyWrite).
    code, result = claude_runner.run(prompt, images, scratch, run_dir, readable, [], args.model,
                                     args.effort, timeout=args.timeout, cache_5m=not args.cache_1h)
    wall = time.time() - started
    calls = claude_runner.tool_calls(run_dir / 'claude-events.jsonl')
    (run_dir / 'tool-audit.json').write_text(json.dumps(audit(calls, readable + [scratch]), indent=2))
    shutil.copytree(scratch, run_dir / 'scratch', ignore=shutil.ignore_patterns('.claude'))
    summary = {'returncode': code, 'wallSeconds': round(wall, 1), 'toolCalls': len(calls)}
    if result:
        summary.update({k: result.get(k) for k in ('num_turns', 'total_cost_usd', 'duration_ms', 'usage')})
        (run_dir / 'reply.txt').write_text(result.get('result') or '')
    if not args.probe:
        summary.update(postprocess(brief, scratch, run_dir))
    (run_dir / 'summary.json').write_text(json.dumps(summary, indent=2, default=str))
    print(json.dumps({k: v for k, v in summary.items() if k != 'usage'}, indent=2, default=str), flush=True)
    return 0 if code == 0 else 1


if __name__ == '__main__':
    sys.exit(main())
