"""PromotionReceipt persistence and offline provenance, cost and owner-verdict CLI."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import tempfile
from datetime import datetime, timezone
from pathlib import Path
from typing import TypedDict

try:
    from .ledger import recorded_cost
except ImportError:
    from ledger import recorded_cost

REPO = Path(__file__).resolve().parents[2]
STORE = Path(__file__).resolve().parent / 'receipts'


class PromotionReceipt(TypedDict):
    schema_version: int
    move: str
    target: str
    source_asset: str
    source_run: str | None
    revise_parent: str | None
    keys_run: str | None
    transforms: dict | None
    final_check: dict
    clip_sha256: str
    runs: list[dict]
    cost: dict
    promoted_at: str | None
    owner_verdict: str | None
    owner_date: str | None
    owner_note: str | None


def now():
    return datetime.now(timezone.utc).isoformat()


def load(path):
    return json.loads(path.read_text()) if path.is_file() else {}


def resolve(value, repo=REPO):
    path = Path(value)
    return path.resolve() if path.is_absolute() else (repo / path).resolve()


def portable(path, repo=REPO):
    path = resolve(path, repo)
    return str(path.relative_to(repo)) if path.is_relative_to(repo) else str(path)


def sha256(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def source_run_for(result, repo=REPO):
    """A persisted result normally lives at RUN/scratch/result.json."""
    for parent in resolve(result, repo).parents:
        if (parent / 'run-config.json').is_file():
            return parent
    return None


def run_chain(run, repo=REPO):
    """Walk both provenance edges once, including missing runs as unknown evidence."""
    visited, active, rows = set(), set(), []

    def visit(path):
        path = resolve(path, repo)
        if path.is_file():
            path = source_run_for(path, repo) or path
        if path in active:
            raise ValueError(f'Cycle in run provenance at {path}')
        if path in visited:
            return
        active.add(path)
        config = load(path / 'run-config.json')
        for key in ('keys', 'revise'):
            if config.get(key):
                visit(config[key])
        check = load(path / 'check.json')
        flow = {k: v for k, v in check.get('metrics', {}).items() if k.startswith('flow:')}
        issues = check.get('warnings', []) + check.get('failures', [])
        clean = (check.get('passed') is True and not any(i.get('check') == 'flow' for i in issues)) if flow else None
        rows.append({'run': portable(path, repo), 'stage': config.get('stage'),
                     'cost_usd': recorded_cost(path), 'config_recorded': bool(config),
                     'flow_clean': clean, 'check': check})
        active.remove(path)
        visited.add(path)
    if run:
        visit(run)
    return rows


def asset_path(report):
    asset = report.get('asset')
    return asset.get('path') if isinstance(asset, dict) else asset


def export_for(source, clip_path, report_path=None, exports=None):
    """Find export provenance by asset AND content, never by a filename guess."""
    digest = sha256(clip_path)
    if report_path:
        report = load(Path(report_path))
        if not report or asset_path(report) != source:
            raise ValueError('Export report does not name the promoted source asset')
        if report.get('assetSha256') and report['assetSha256'] != digest:
            raise ValueError('Source asset changed since the export report')
        return report
    matches = []
    for path in (exports or REPO / 'artifacts/genra-unity-clips').glob('*/report.json'):
        report = load(path)
        if asset_path(report) == source and report.get('assetSha256') == digest:
            matches.append(report)
    if len(matches) > 1 and any(m.get('sourceRun') != matches[0].get('sourceRun') or
                                m.get('transforms') != matches[0].get('transforms') for m in matches):
        raise ValueError('Ambiguous export provenance; select --write-report')
    return matches[0] if matches else {}


def build_receipt(target, source_asset, clip_path, run=None, export=None, repo=REPO) -> PromotionReceipt:
    export = export or {}
    rows = run_chain(run, repo)
    config = load(resolve(run, repo) / 'run-config.json') if run else {}
    brief = load(resolve(config['brief'], repo)) if config.get('brief') else {}
    missing = [r['run'] for r in rows if r['cost_usd'] is None]
    known = sum(r['cost_usd'] for r in rows if r['cost_usd'] is not None)
    complete = bool(rows) and not missing and all(r['config_recorded'] for r in rows)
    source_check = rows[-1]['check'] if rows else {}
    transforms = export.get('transforms')
    # A transformed phase check does not measure flow. Never reuse source-flow labels as final labels.
    final = export.get('phaseCheck', {}) if transforms and (transforms.get('ticks') or transforms.get('retime')) else source_check
    status = 'PASS' if final.get('passed') is True else 'FAIL' if final.get('passed') is False else 'UNKNOWN'
    return {'schema_version': 1, 'move': brief.get('name') or export.get('name') or Path(target).stem,
            'target': target, 'source_asset': source_asset,
            'source_run': portable(run, repo) if run else None,
            'revise_parent': portable(config['revise'], repo) if config.get('revise') else None,
            'keys_run': portable(config['keys'], repo) if config.get('keys') else None,
            'transforms': transforms,
            'final_check': {'status': status if transforms is not None else 'UNKNOWN',
                            'scope': 'export phase' if final is not source_check else 'source run',
                            'flow_metrics': {k: v for k, v in final.get('metrics', {}).items() if k.startswith('flow:')} if transforms is not None else {},
                            'report': final},
            'clip_sha256': sha256(clip_path), 'runs': rows,
            'cost': {'known_usd': known if rows else None, 'total_usd': known if complete else None,
                     'completeness': 'complete' if complete else 'partial' if any(r['cost_usd'] is not None for r in rows) else 'UNKNOWN',
                     'missing_runs': missing},
            'promoted_at': None, 'owner_verdict': None, 'owner_date': None, 'owner_note': None}


def receipt_path(target, store=STORE):
    parts = Path(target).with_suffix('').parts
    if parts[:2] == ('Assets', 'CharacterPackages') and len(parts) > 2:
        parts = (parts[2], parts[-1])
    return store / ('__'.join(parts) + '.json')


def atomic_json(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, temp = tempfile.mkstemp(dir=path.parent, suffix='.tmp')
    try:
        with os.fdopen(fd, 'w') as stream:
            stream.write(json.dumps(data, indent=2) + '\n')
        os.replace(temp, path)
    finally:
        if os.path.exists(temp):
            os.unlink(temp)


def save_receipt(receipt, store=STORE):
    path = receipt_path(receipt['target'], store)
    previous = load(path)
    identity = ('clip_sha256', 'source_run', 'transforms', 'source_asset')
    if previous and all(previous.get(k) == receipt.get(k) for k in identity):
        for key in ('promoted_at', 'owner_verdict', 'owner_date', 'owner_note'):
            receipt[key] = previous.get(key)
    elif previous:
        archive = store / 'history' / path.stem / (hashlib.sha256(json.dumps(previous, sort_keys=True).encode()).hexdigest() + '.json')
        atomic_json(archive, previous)
    receipt['promoted_at'] = receipt['promoted_at'] or now()
    atomic_json(path, receipt)
    return path


def verdict(selector, value, note=None, store=STORE):
    matches = [p for p in store.glob('*.json') if selector in (load(p).get('target'), load(p).get('move'))]
    if len(matches) != 1:
        raise ValueError(f'Expected one receipt for {selector!r}, found {len(matches)}; use the exact target path')
    receipt = load(matches[0])
    receipt.update(owner_verdict=value, owner_date=now(), owner_note=note)
    atomic_json(matches[0], receipt)
    return matches[0]


def summary(store=STORE):
    lines = ['| Move / target | Runs | List $ | First take accepted? | Flow passes | Verdict |',
             '| --- | ---: | --- | --- | ---: | --- |']
    for path in sorted(store.glob('*.json')):
        r = load(path)
        full = [x for x in r['runs'] if x['stage'] == 'full']
        accepted = ('UNKNOWN' if any(not x['config_recorded'] for x in r['runs']) else 'yes' if len(full) == 1 else 'no') if r['owner_verdict'] == 'good' else 'no' if r['owner_verdict'] in ('redo', 'rejected') else 'UNKNOWN'
        cost = r['cost']
        money = f"{cost['known_usd']:.2f}" if cost['completeness'] == 'complete' else f"UNKNOWN (partial >= {cost['known_usd']:.2f})" if cost['known_usd'] is not None else 'UNKNOWN'
        passes = sum(x['flow_clean'] is True for x in full)
        unknown = sum(x['flow_clean'] is None for x in full)
        lines.append(f"| {r['move']} / {r['target']} | {len(r['runs'])} | {money} | {accepted if full else 'UNKNOWN'} | {passes}" +
                     (f' (+{unknown} UNKNOWN)' if unknown else '') + f" | {r['owner_verdict'] or 'UNKNOWN'} |")
    return '\n'.join(lines)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--store', type=Path, default=STORE)
    sub = parser.add_subparsers(dest='command', required=True)
    vote = sub.add_parser('verdict')
    vote.add_argument('selector')
    vote.add_argument('value', choices=('good', 'redo', 'rejected'))
    vote.add_argument('--note')
    sub.add_parser('summary')
    dry = sub.add_parser('dry-run')
    dry.add_argument('run', type=Path)
    dry.add_argument('--target', required=True)
    dry.add_argument('--clip', type=Path, required=True)
    dry.add_argument('--write-report', type=Path)
    args = parser.parse_args(argv)
    try:
        if args.command == 'verdict':
            print(verdict(args.selector, args.value, args.note, args.store))
        elif args.command == 'summary':
            print(summary(args.store))
        else:
            export = load(args.write_report) if args.write_report else {}
            print(json.dumps(build_receipt(args.target, asset_path(export) or str(args.clip), args.clip, args.run,
                                           export), indent=2))
    except (ValueError, OSError) as exc:
        parser.error(str(exc))


if __name__ == '__main__':
    main()
