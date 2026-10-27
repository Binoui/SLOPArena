"""Review loop: the director reviews a clip in Unity, the animator revises it, up to N rounds, unattended.

    <genra python> tools/genra/loop.py BRIEF.json [--from artifacts/genra-runs/<id>] [--rounds 1]
    <genra python> tools/genra/loop.py BRIEF.json --from RUN --start-review <loop>/review-N --polish --rounds 1
    <genra python> tools/genra/loop.py BRIEF.json --keys artifacts/genra-runs/<approved run.py --stage keys run>

Without --from, round 0 authors the clip from the brief (run.py). One revision is the default (owner, 2026-10-21: better
briefs and the owner's own look beat more unattended rounds, which trade small problems for new ones). Each round reviews
the latest clip (review.py, with the
brief's review block for the style reference and travel) and, unless the director accepts it with every check passing,
revises it (run.py --revise). The last revision is reviewed too, so the owner gets the final clip with its review.
--polish keeps revising an accepted clip while the director still lists (minor) issues, and --owner always gets its
revision even when the director accepts round 0; --start-review reuses an
existing review of the --from clip as round 0 instead of rendering and reviewing it again. --owner gives the first
revision the owner's direction after their look at the take, ahead of the director's, and --skip sets aside review
issues the owner does not want (2026-10-22: a smoothing pass that keeps the approved poses, not the re-pose the review
also asked for).
loop.md in artifacts/genra-loops/<id>/ logs every round: the run, the checks, the director's verdict and issues, which
earlier issues were fixed, and the cost.
"""
from __future__ import annotations

import argparse
import datetime
import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
sys.path.insert(0, str(HERE.parent))

from genra.brief import Brief, resolved_brief_path  # noqa: E402
from genra.review import markdown, review, reuse_evidence  # noqa: E402
from genra.run import author  # noqa: E402


FORM = ('left wrist bend max (deg)', 'right wrist bend max (deg)', 'knee bend min (deg)', 'legs mirror error (deg)',
        'left forearm on the torso (share of ticks)', 'right forearm on the torso (share of ticks)', 'plain-sine curves',
        'life: torso swing (deg)', 'life: head bob against the hips / hips bob', 'flow: stops per joint per second',
        'flow: most of the body stopping together (share)', 'flow: frozen joint-ticks (share)')


def form_metrics(checks_text: str) -> dict:
    """The form and life numbers of a check report, so a round that fixes its notes but loses ground elsewhere shows."""
    values = {}
    for line in checks_text.splitlines():
        key, _, value = line.strip().rpartition(': ')
        if key in FORM:
            values[key] = float(value)
    return values


def log_round(lines: list, number: int, run_dir: Path, verdict: dict, review_dir: Path, run_cost: float | None):
    notes = verdict['review'] or {}
    report = (review_dir / 'checks.txt').read_text()
    checks = report.splitlines()[0]
    form = form_metrics(report)
    lines += [f'## Round {number}', '', f'- Clip: `{run_dir.relative_to(REPO)}` (authoring ${run_cost or 0:.2f})',
              f"- Review: `{review_dir.relative_to(REPO)}` (${verdict['costUsd'] or 0:.2f}); checks {checks}",
              f"- Verdict: **{notes.get('verdict', 'none')}**. {notes.get('summary', '')}"]
    if form:
        lines.append('- Form: ' + '; '.join(f'{k} {v:.2f}' for k, v in form.items()))
    lines.append('')
    for item in notes.get('previousIssues', []):
        lines.append(f"- Earlier issue {'fixed' if item['fixed'] else 'NOT fixed'}: {item['problem']} {item.get('note', '')}")
    for k, issue in enumerate(notes.get('issues', []), 1):
        lines.append(f"{k}. ({issue['severity']}) {issue['problem']} Target: {issue['target']}")
    lines.append('')


def preflight(brief: Brief) -> list[str]:
    """Check failures the brief's fixed poses already have, before anything is spent: an entry or exit pose the clip
    must equal exactly but that fails the anatomy checks (2026-10-21: the jump had to start on the idle, whose plank
    wrist folds 137 degrees, and no revision could pass). A wrist fold is covered when the brief exempts ticks for it."""
    from genra.checks import check_clip
    from genra.clip import Clip
    problems = []
    poses = [('entry', brief.entry_pose)] if brief.entry_matches else []
    if 'exit' in brief.data:
        poses.append(('exit', brief.exit_pose))
    for label, pose in poses:
        report = check_clip(Clip.from_keys({0: pose, 1: pose}, 2), {'clearance': False, 'face': False})
        for failure in report['failures']:
            if failure['check'] == 'wrist' and brief.get('wrist_exempt'):
                continue
            problems.append(f"the {label} pose already fails {failure['check']}: {failure['message']}")
    return problems


def finished(notes, passed: bool, polish: bool) -> bool:
    """The loop stops when the director accepts a clip that passes its checks; with polish, once no issue is left."""
    return notes is None or (notes['verdict'] == 'accept' and passed and not (polish and notes['issues']))


def full_checks_passed(path: Path) -> bool:
    """A fast check is useful during iteration, but cannot qualify a delivery.

    Reports written before modes were introduced are full authoring checks.
    A missing report cannot establish that the candidate passed.
    """
    if not path.is_file():
        return False
    report = json.loads(path.read_text())
    return (report.get('mode', 'full') == 'full' and report.get('stage', 'full') == 'full'
            and report.get('passed') is True and not report.get('checkpointOnly', False))


def select_final(rounds):
    """The latest round that passed both authoring and review checks, or no valid candidate."""
    return next((r for r in reversed(rounds) if r[3]), None)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    parser.add_argument('brief')
    parser.add_argument('--from', dest='start', help='start from this run directory instead of authoring round 0')
    parser.add_argument('--rounds', type=int, default=1,
                        help='revisions at most (default 1: one review, one revision, a final review; the owner looks next)')
    parser.add_argument('--effort', default='medium', help="the animator's effort")
    parser.add_argument('--timeout', type=int, default=1800, help="the animator's time limit in seconds (a long move needs more)")
    parser.add_argument('--review-effort', default='high')
    parser.add_argument('--start-review', help='an existing review folder of the --from clip: round 0 reuses it')
    parser.add_argument('--polish', action='store_true', help='keep revising an accepted clip while issues remain')
    parser.add_argument('--keys', help='an approved run.py --stage keys run (or a result.json of approved key poses): '
                        'every round keeps its key poses')
    parser.add_argument('--owner', help="the owner's direction for the first revision, ahead of the director's review "
                        '(after seeing the take: show takes to the owner before revising them)')
    parser.add_argument('--skip', default='', help='issue numbers (from 1) of the first review that the owner set aside')
    args = parser.parse_args(argv)
    if args.start_review and not args.start:
        parser.error('--start-review reviews the --from clip')
    problems = preflight(Brief(Path(args.brief).resolve()))
    if problems:
        raise SystemExit('Pre-flight: ' + '; '.join(problems) + '. Fix the brief (or the clip it starts or ends on) first.')
    if not Brief(Path(args.brief).resolve()).timing:
        print('Pre-flight note: the brief has no timing contract ("timing": fixed / adjustable); ask the owner '
              'what the game may change before a long move', file=sys.stderr)
    brief_path = Path(args.brief).resolve()
    brief = Brief(brief_path)
    defaults = brief.get('review', {})
    stamp = datetime.datetime.now().strftime('%Y%m%d-%H%M%S')
    root = REPO / 'artifacts/genra-loops' / f"{stamp}-{brief['name']}"
    root.mkdir(parents=True)
    lines = [f"# Review loop: {brief['name']}", '', f'Brief: `{brief_path}`; style reference '
             f"{defaults.get('reference') or 'none'}; up to {args.rounds} revisions"
             + (', polishing accepted clips' if args.polish else '') + '.', '']
    if args.start:
        run_dir, run_cost = Path(args.start).resolve(), None
    else:
        code, run_dir = author(brief_path, f"{brief['name']}-loop{stamp[-6:]}-r0", effort=args.effort, timeout=args.timeout,
                               keys=args.keys)
        run_cost = json.loads((run_dir / 'summary.json').read_text()).get('total_cost_usd')
    previous = None
    rounds = []  # (round, run dir, review dir, checks passed)
    for number in range(args.rounds + 1):
        if json.loads((run_dir / 'summary.json').read_text()).get('checkpointOnly'):
            lines += [f'Round {number} has only its starting checkpoint; no paid review or revision was started.', '']
            break
        review_dir = root / f'review-{number}'
        result = run_dir / 'scratch' / 'result.json'
        resolved = resolved_brief_path(brief_path, result)
        review_defaults = Brief(resolved).get('review', {})
        review_dir.mkdir()
        if number == 0 and args.start_review:
            source = Path(args.start_review).resolve()
            reuse_evidence(source, review_dir, resolved, result)
            verdict = json.loads((source / 'review.json').read_text()) | {
                'brief': str(resolved), 'result': str(result), 'reusedFrom': str(source), 'checksRefreshed': True}
            (review_dir / 'review.json').write_text(json.dumps(verdict, indent=2))
            if verdict['review']:
                (review_dir / 'review.md').write_text(markdown(verdict['review']))
            lines += [f'Round 0 reuses the review in `{source.relative_to(REPO)}` with fresh full checks.', '']
        else:
            verdict = review(resolved, result, review_dir, None, review_defaults.get('reference'),
                             review_defaults.get('travel', 0.0), previous, effort=args.review_effort, name=f'{root.name}-r{number}')
        author_passed = full_checks_passed(run_dir / 'check.json')
        lines += [f'Authoring checks: {"PASS" if author_passed else "FAIL or no full check report"}.', '']
        log_round(lines, number, run_dir, verdict, review_dir, run_cost)
        (root / 'loop.md').write_text('\n'.join(lines))
        notes = verdict['review']
        passed = author_passed and full_checks_passed(review_dir / 'checks.json')
        rounds.append((number, run_dir, review_dir, passed))
        # An owner's direction always gets its revision: the director accepting round 0 does not overrule the owner
        # (salamander run, 2026-10-27: the review accepted it, so the owner's fluidity note was dropped unseen).
        owner_pending = number == 0 and bool(args.owner)
        if (finished(notes, passed, args.polish) and not owner_pending) or number == args.rounds:
            break
        previous = notes
        first = number == 0
        skip = [int(k) for k in args.skip.split(',') if k.strip()] if first else []
        if first and (args.owner or skip):
            lines += [f'Owner for revision 1: {args.owner or "-"}' + (f"; set aside issue(s) {', '.join(map(str, skip))}"
                                                                     if skip else '') + '.', '']
        code, run_dir = author(brief_path, f"{brief['name']}-loop{stamp[-6:]}-r{number + 1}", effort=args.effort,
                               timeout=args.timeout, revise=run_dir, review=review_dir / 'review.json', round_number=number + 1,
                               keys=args.keys, owner=args.owner if first else None, skip=skip)
        run_cost = json.loads((run_dir / 'summary.json').read_text()).get('total_cost_usd')
        if not (run_dir / 'scratch' / 'result.json').exists():
            lines += [f'Revision {number + 1} saved no clip (exit {code}); see `{run_dir.relative_to(REPO)}`.', '']
            break
        if code != 0:  # a time limit still leaves the last saved clip, which is worth a review
            lines += [f'Revision {number + 1} ended early (exit {code}, e.g. its time limit); reviewing its last saved clip.', '']
    # A revision must not trade the checks for the review's notes: Sir Bonk's Joust gallop r1 (2026-10-21) followed a
    # review against his run, hunched him over the horse and moved the owner's approved keys 24 cm; the owner kept r0.
    final = select_final(rounds)
    if final is None:
        lines += ['## No passing candidate', '',
                  'No round passed both full authoring and review checks. Inspect the failures above before delivery.', '']
        (root / 'loop.md').write_text('\n'.join(lines))
        print(root)
        return 1
    if final != rounds[-1]:
        lines += [f'Round {rounds[-1][0]} failed its checks, so the final is round {final[0]}, the last one that passed.', '']
    _, run_dir, review_dir, _ = final
    evidence = json.loads((review_dir / 'evidence.json').read_text())
    lines += ['## Final', '', f"- Clip: `{run_dir.relative_to(REPO)}`; Unity asset `{evidence['asset']}`",
              f"- Unity render: `{evidence['unityRender']}` (real.mp4)", '']
    (root / 'loop.md').write_text('\n'.join(lines))
    print(root)
    return 0


if __name__ == '__main__':
    sys.exit(main())
