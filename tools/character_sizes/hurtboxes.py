#!/usr/bin/env python3
"""Run mesh fitting or coverage through an owned Unity gateway batch."""
import argparse
import json
from pathlib import Path
import subprocess
import uuid

root = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('mode', choices=['fit', 'evaluate', 'visual'])
parser.add_argument('--packages', nargs='+', required=True)
args = parser.parse_args()
source = (Path(__file__).with_name('hurtbox_fit.eval.cs')).read_text()
source = source.replace('string Mode = "evaluate";', f'string Mode = {json.dumps(args.mode)};', 1)
source = source.replace('string[] Targets = Array.Empty<string>();',
                        'string[] Targets = new[] { ' + ', '.join(json.dumps(p) for p in args.packages) + ' };', 1)
out = root / 'tmp/hurtbox-fit'
out.mkdir(parents=True, exist_ok=True)
script = out / ('request-' + args.mode + '-' + '-'.join(args.packages) + '-' + uuid.uuid4().hex[:8] + '.eval.cs')
script.write_text(source)
commands = [['eval_file', '--file', str(script), '--timeout', '540000']]
if args.mode == 'fit':
    commands.extend(['sloparena.character.cook', '--target', package] for package in args.packages)
batch = out / (script.stem + '.batch.json')
batch.write_text(json.dumps({'commands': commands, 'restore': []}))
result = subprocess.run(['bun', 'scripts/unity-editor-gateway.ts', '--project-path', 'client/Unity',
                         '--batch', str(batch)], cwd=root)
raise SystemExit(result.returncode)
