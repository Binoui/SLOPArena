#!/usr/bin/env python3
"""Compile probe scripts using a retained isolated Unity import. Never operates an Editor."""
import argparse
from pathlib import Path
import re
import subprocess

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--worktree', type=Path, required=True)
parser.add_argument('--import-project', type=Path, required=True)
parser.add_argument('--unity-data', type=Path, required=True)
parser.add_argument('--output', type=Path, required=True)
parser.add_argument('--editor', action='store_true')
args = parser.parse_args()
worktree, cache, output = (p.resolve() for p in (args.worktree, args.import_project, args.output))
output.mkdir(parents=True, exist_ok=True)
project = worktree / 'client/Unity'
subprocess.run(['dotnet', 'build', str(worktree / 'src/Shared/SlopArena.Shared.csproj'),
                '-p:SkipUnityPluginCopy=true', '-v:q'], check=True)
shared = worktree / 'artifacts/Shared/bin/Debug/netstandard2.1/SlopArena.Shared.dll'
for name in ['Assembly-CSharp', 'Assembly-CSharp-Editor'] if args.editor else ['Assembly-CSharp']:
    candidates = list((cache / 'Library/Bee/artifacts').glob('*/' + name + '.rsp'))
    if len(candidates) != 1:
        raise RuntimeError(f'Expected one {name} response file, found {len(candidates)}')
    rows = []
    for line in candidates[0].read_text().splitlines():
        if line.startswith(('"Assets/', '"Packages/')):
            continue
        if not args.editor and line.startswith('-define:UNITY_EDITOR'):
            continue
        if line.startswith(('-out:', '-refout:')):
            target = output / (name + ('.ref.dll' if line.startswith('-refout:') else '.dll'))
            line = re.sub(r'".*"', '"' + str(target) + '"', line)
        def path(match):
            value = match[1]
            if value.startswith('Library/'):
                value = str(cache / value)
            elif value == 'Assets/Plugins/SlopArena.Shared/SlopArena.Shared.dll':
                value = str(shared)
            elif value.startswith(('Assets/', 'Packages/')):
                value = str(project / value if (project / value).exists() else cache / value)
            if name.endswith('Editor') and value.endswith('/Assembly-CSharp.ref.dll'):
                value = str(output / 'Assembly-CSharp.dll')
            return '"' + value + '"'
        rows.append(re.sub(r'"([^"]+)"', path, line))
    directories = ['Assets/Scripts/Editor', 'Assets/AbilityLab/Editor'] if name.endswith('Editor') else ['Assets/Scripts/Runtime', 'Assets/AbilityLab/Runtime']
    for directory in directories:
        rows.extend('"' + str(p) + '"' for p in (project / directory).rglob('*.cs'))
    if not name.endswith('Editor'):
        rows.extend('"' + str(p) + '"' for p in Path(__file__).resolve().parent.glob('*.cs'))
    response = output / (name + '.rsp')
    response.write_text('\n'.join(rows))
    result = subprocess.run([str(args.unity_data / 'NetCoreRuntime/dotnet'),
                             str(args.unity_data / 'DotNetSdkRoslyn/csc.dll'), '@' + str(response)], capture_output=True, text=True)
    (output / (name + '.log')).write_text(result.stdout + result.stderr)
    print(name, 'exit', result.returncode)
    if result.returncode:
        print('\n'.join(line for line in (result.stdout + result.stderr).splitlines() if ' error ' in line))
        raise SystemExit(result.returncode)
