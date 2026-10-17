"""Tabulate authoring runs: time, turns, checks and cost, from each run's recorded evidence.

    python3 tools/genra/ledger.py artifacts/genra-runs/*            # Markdown table

The total is the CLI's own list-price figure (`total_cost_usd`). The split uses the Opus 5.5
list rates below (USD per million tokens, October 2026): cache writes are priced by the
run's cache lifetime. On a Claude subscription these figures measure plan usage, not billing.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

RATES = {'input': 4.0, 'output': 20.0, 'cache_read': 0.20, 'cache_write_1h': 8.0, 'cache_write_5m': 5.0}


def row(run: Path) -> dict | None:
    summary, config = run / 'summary.json', run / 'run-config.json'
    if not summary.exists():
        return None
    s = json.loads(summary.read_text())
    c = json.loads(config.read_text()) if config.exists() else {}
    u = s.get('usage') or {}
    write_rate = RATES['cache_write_5m'] if c.get('cache5m') else RATES['cache_write_1h']
    split = {'output': u.get('output_tokens', 0) * RATES['output'] / 1e6,
             'cacheRead': u.get('cache_read_input_tokens', 0) * RATES['cache_read'] / 1e6,
             'cacheWrite': u.get('cache_creation_input_tokens', 0) * write_rate / 1e6}
    return {'run': run.name, 'wall': s.get('wallSeconds'), 'turns': s.get('num_turns'), 'tools': s.get('toolCalls'),
            'output': u.get('output_tokens'), 'thinking': (u.get('output_tokens_details') or {}).get('thinking_tokens'),
            'read': u.get('cache_read_input_tokens'), 'write': u.get('cache_creation_input_tokens'),
            'cost': s.get('total_cost_usd'), 'split': split, 'passed': s.get('passed'),
            'failures': s.get('failures'), 'warnings': s.get('warnings'), 'cache5m': bool(c.get('cache5m'))}


def table(rows) -> str:
    lines = ['| Run | Wall | Turns | Tool calls | Output tok (thinking) | Cache read | Cache write | List $ (out / read / write) | Checks |',
             '| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- |']
    for r in rows:
        sp = r['split']
        checks = 'PASS' if r['passed'] else f"{r['failures']} failures" if r['passed'] is not None else 'no result'
        lines.append(f"| {r['run']}{' (5m cache)' if r['cache5m'] else ''} | {r['wall']:.0f} s | {r['turns']} | {r['tools']} | "
                     f"{r['output']:,} ({r['thinking'] or 0:,}) | {r['read'] / 1e6:.2f}M | {r['write'] / 1e3:.0f}K | "
                     f"{r['cost']:.2f} ({sp['output']:.2f} / {sp['cacheRead']:.2f} / {sp['cacheWrite']:.2f}) | "
                     f"{checks}, {r['warnings'] or 0} warnings |")
    return '\n'.join(lines)


if __name__ == '__main__':
    rows = [r for r in (row(Path(p)) for p in sys.argv[1:]) if r]
    print(table(rows))
