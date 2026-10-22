"""The liveliness corpus behind the flow checks: hand-made attacks retargeted onto a character, and their flow numbers.

    <genra python> tools/genra/flow_corpus.py build     # Unity snapshot through your own gateway lease (unity_rigpack)
    <genra python> tools/genra/flow_corpus.py measure   # curves.flow on every clip; rewrites benchmarks/flow-corpus.json

The clip list and the measured ranges live in benchmarks/flow-corpus.json. The thresholds in checks.DEFAULT_SPEC
('flow_*') sit above these ranges, so a new move is judged against hand-made animation without a reference of its own.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
CORPUS = HERE / 'benchmarks' / 'flow-corpus.json'


def build(data: dict) -> int:
    from genra.unity_rigpack import main as rigpack
    args = ['all', data['package'], '--label', data['label'], '--prefab', data['prefab'],
            '--weapon-config', data['weaponConfig']]
    for label, source in data['references'].items():
        args += ['--reference', f'{label}={source}']
    return rigpack(args)


def measure(data: dict) -> dict:
    import numpy as np
    from genra.clip import Clip
    from genra.curves import flow
    from genra.rig import Rig
    pack = REPO / 'artifacts/genra-rigpacks' / f"{data['package']}-unity-{data['label']}"
    rig = Rig(pack / 'rig.usda')
    mapping = json.loads((pack / 'mapping.json').read_text())
    rows = {}
    for label in mapping['references']:
        if label in ('idle', 'run', 'jump', 'fall'):  # loops and locomotion are not one-shot moves
            continue
        source = Clip.from_reference(Rig(pack / 'references' / f'{label}.usda'))
        clip = Clip(rig, source.t, source.q, source.s)
        if len(clip) >= 20:
            rows[label] = flow(clip)
    summary = {}
    for key in next(iter(rows.values())):
        v = np.array([r[key] for r in rows.values()])
        summary[key] = {'min': round(float(v.min()), 3), 'p10': round(float(np.percentile(v, 10)), 3),
                        'median': round(float(np.median(v)), 3), 'p90': round(float(np.percentile(v, 90)), 3),
                        'max': round(float(v.max()), 3)}
    return {'clips': len(rows), 'flow': summary}


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    parser.add_argument('command', choices=['build', 'measure'])
    args = parser.parse_args(argv)
    sys.path.insert(0, str(HERE.parent))
    data = json.loads(CORPUS.read_text())
    if args.command == 'build':
        return build(data)
    import datetime
    data['measured'] = {**measure(data), 'date': datetime.date.today().isoformat()}
    CORPUS.write_text(json.dumps(data, indent=2) + '\n')
    print(json.dumps(data['measured'], indent=2))
    return 0


if __name__ == '__main__':
    sys.exit(main())
