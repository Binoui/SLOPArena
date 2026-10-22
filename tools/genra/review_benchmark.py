"""Run the reviewer on clips with the owner's verdicts and tabulate how it compares (benchmarks/review/cases.json).

    <genra python> tools/genra/review_benchmark.py [--only sir-run-v2,sir-idle] [--effort high]
    <genra python> tools/genra/review_benchmark.py --reuse artifacts/genra-reviews/<stamp>-benchmark   # new REVIEW.md, no Unity

Each case is reviewed as review.py would review it: Unity renders of its saved asset, the style reference and travel from its
brief's review block. Evidence goes to artifacts/genra-reviews/<stamp>-benchmark/: a folder per case and summary.md.
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

from genra.brief import Brief  # noqa: E402
from genra.review import review  # noqa: E402


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    parser.add_argument('--cases', default=str(HERE / 'benchmarks/review/cases.json'))
    parser.add_argument('--only', help='comma-separated case names')
    parser.add_argument('--effort', default='high')
    parser.add_argument('--reuse', action='append', default=[],
                        help="an earlier benchmark folder whose case folders' evidence to reuse (repeatable; the latest wins)")
    args = parser.parse_args(argv)
    cases = json.loads(Path(args.cases).read_text())['cases']
    if args.only:
        cases = [c for c in cases if c['name'] in args.only.split(',')]
    root = REPO / 'artifacts/genra-reviews' / f"{datetime.datetime.now().strftime('%Y%m%d-%H%M%S')}-benchmark"
    rows = ['| Case | Owner | Reviewer | Major issues | Minor | $ |', '| --- | --- | --- | --- | ---: | ---: |']
    for case in cases:
        brief_path = (HERE / case['brief']).resolve()
        defaults = Brief(brief_path).get('review', {})
        run_dir = root / case['name']
        run_dir.mkdir(parents=True)
        earlier = [Path(r) / case['name'] for r in args.reuse if (Path(r) / case['name'] / 'evidence.json').exists()]
        out = review(brief_path, REPO / case['result'], run_dir, case['asset'], defaults.get('reference'),
                     defaults.get('travel', 0.0), effort=args.effort, reuse=earlier[-1] if earlier else None)
        notes = out['review'] or {'verdict': 'none', 'issues': []}
        major = [i['problem'] for i in notes['issues'] if i['severity'] == 'major']
        minor = sum(1 for i in notes['issues'] if i['severity'] == 'minor')
        rows.append(f"| {case['name']} | {case['owner']['verdict']} | {notes['verdict']} | {' / '.join(major) or '-'} | {minor} | "
                    f"{out['costUsd'] or 0:.2f} |")
        print(rows[-1], flush=True)
    (root / 'summary.md').write_text('\n'.join(rows) + '\n')
    print(root)
    return 0


if __name__ == '__main__':
    sys.exit(main())
