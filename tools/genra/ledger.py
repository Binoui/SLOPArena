"""Tabulate authoring runs: time, turns, checks and cost, from each run's recorded evidence.

    python3 tools/genra/ledger.py artifacts/genra-runs/*            # Markdown table

The total is the CLI's own list-price figure (`total_cost_usd`). The split uses the Opus 5.5
list rates below (USD per million tokens, October 2026): cache writes are priced by the
run's cache lifetime. On a Claude subscription these figures measure plan usage, not billing.
"""
from __future__ import annotations

import json
import math
import sys
from pathlib import Path

RATES = {'input': 4.0, 'output': 20.0, 'cache_read': 0.20, 'cache_write_1h': 8.0, 'cache_write_5m': 5.0}


def recorded_cost(run: Path) -> float | None:
    """CLI-recorded list cost, including genuine zero; missing evidence is unknown."""
    summary = run / 'summary.json'
    if not summary.exists():
        return None
    value = json.loads(summary.read_text()).get('total_cost_usd')
    if isinstance(value, bool) or not isinstance(value, (float, int)):
        return None
    return value if math.isfinite(value) and value >= 0 else None


def estimated_cost(run: Path) -> float | None:
    """List-price estimate from the event stream, for a job the runner stopped before the CLI reported its cost
    (plateau, stall, timeout): Wibou's round-2 key runs (2026-11-10) left `total_cost_usd` empty. Each assistant
    message's largest recorded usage is priced at RATES; streamed messages can under-report output, so read it as a
    lower bound: on three completed Wibou runs it read 58-67% of the recorded cost. It never replaces the recorded cost,
    and missing events stay unknown."""
    events = run / 'claude-events.jsonl'
    if not events.exists():
        return None
    config = run / 'run-config.json'
    cache5m = json.loads(config.read_text()).get('cache5m', True) if config.exists() else True
    usage: dict[str, dict] = {}
    for line in events.read_text().splitlines():
        try:
            event = json.loads(line)
        except json.JSONDecodeError:
            continue
        message = event.get('message') if event.get('type') == 'assistant' else None
        if not isinstance(message, dict) or not isinstance(message.get('usage'), dict):
            continue
        seen = usage.setdefault(message.get('id') or str(len(usage)), {})
        for key, value in message['usage'].items():
            if isinstance(value, (int, float)) and not isinstance(value, bool):
                seen[key] = max(seen.get(key, 0), value)
    if not usage:
        return None
    write = RATES['cache_write_5m'] if cache5m else RATES['cache_write_1h']
    return sum(u.get('input_tokens', 0) * RATES['input'] + u.get('output_tokens', 0) * RATES['output']
               + u.get('cache_read_input_tokens', 0) * RATES['cache_read']
               + u.get('cache_creation_input_tokens', 0) * write for u in usage.values()) / 1e6


def row(run: Path) -> dict | None:
    summary, config = run / 'summary.json', run / 'run-config.json'
    s = json.loads(summary.read_text()) if summary.exists() else {}
    if s.get('status') == 'running':
        return None
    c = json.loads(config.read_text()) if config.exists() else {}
    u = s.get('usage') or {}
    write_rate = RATES['cache_write_5m'] if c.get('cache5m') else RATES['cache_write_1h']
    split = {'output': u.get('output_tokens', 0) * RATES['output'] / 1e6,
             'cacheRead': u.get('cache_read_input_tokens', 0) * RATES['cache_read'] / 1e6,
             'cacheWrite': u.get('cache_creation_input_tokens', 0) * write_rate / 1e6}
    return {'run': run.name, 'wall': s.get('wallSeconds'), 'turns': s.get('num_turns'), 'tools': s.get('toolCalls'),
            'output': u.get('output_tokens'), 'thinking': (u.get('output_tokens_details') or {}).get('thinking_tokens'),
            'read': u.get('cache_read_input_tokens'), 'write': u.get('cache_creation_input_tokens'),
            'cost': recorded_cost(run), 'estimate': estimated_cost(run), 'split': split, 'passed': s.get('passed'),
            'failures': s.get('failures'), 'warnings': s.get('warnings'), 'cache5m': bool(c.get('cache5m'))}


def table(rows) -> str:
    lines = ['| Run | Wall | Turns | Tool calls | Output tok (thinking) | Cache read | Cache write | List $ (out / read / write) | Checks |',
             '| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- |']
    for r in rows:
        sp = r['split']
        fmt = lambda value, spec: format(value, spec) if value is not None else 'unknown'
        checks = 'PASS' if r['passed'] else f"{r['failures']} failures" if r['passed'] is not None else 'no result'
        read = f"{r['read'] / 1e6:.2f}M" if r['read'] is not None else 'unknown'
        write = f"{r['write'] / 1e3:.0f}K" if r['write'] is not None else 'unknown'
        cost = (f"{r['cost']:.2f} ({sp['output']:.2f} / {sp['cacheRead']:.2f} / {sp['cacheWrite']:.2f})"
                if r['cost'] is not None else
                f"UNKNOWN (events: at least ~{r['estimate']:.2f})" if r.get('estimate') is not None else 'UNKNOWN')
        lines.append(f"| {r['run']}{' (5m cache)' if r['cache5m'] else ''} | {fmt(r['wall'], '.0f')} s | {r['turns']} | {r['tools']} | "
                     f"{fmt(r['output'], ',')} ({fmt(r['thinking'], ',')}) | {read} | {write} | {cost} | "
                     f"{checks}, {r['warnings'] or 0} warnings |")
    return '\n'.join(lines)


if __name__ == '__main__':
    rows = [r for r in (row(Path(p)) for p in sys.argv[1:]) if r]
    print(table(rows))
