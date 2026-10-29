"""Measure hem deformation after aligning the pinned waistband. Use <capture directory>."""
from pathlib import Path
import json
import sys
import numpy as np

out = Path(sys.argv[1]).resolve()
rows = [json.loads(line) for line in (out/'hem-motion.jsonl').read_text().splitlines()]
clips = {}
for name in ['idle', 'run-turn', 'jump-landing', 'high-kick']:
    states = [json.loads(line) for line in (out/name/'states.jsonl').read_text().splitlines()]
    clips[name] = [s['matchTick'] for s in states]
runs = json.loads((out/'training-run-receipts.json').read_text())
for label, step in [('run',runs[1]['steps'][1]),('turn',runs[1]['steps'][2])]:
    clips[label] = list(range(step['startTick'],step['endTick']+1))
reference_rows = [r for r in rows if r['tick'] in clips['idle'][-30:]]
reference = reference_rows[-1]
p0 = np.array(reference['pinned'])
h0 = np.array(reference['hem'])
aligned = []
residual = []
for row in rows:
    pins = np.array(row['pinned'])
    u, _, vt = np.linalg.svd((pins-pins.mean(0)).T@(p0-p0.mean(0)))
    correction = np.eye(3)
    correction[2,2] = np.linalg.det(u@vt)
    rotation = u@correction@vt
    hem = (np.array(row['hem'])-pins.mean(0))@rotation+p0.mean(0)
    aligned.append(hem)
    residual.append(float(np.sqrt(np.mean(np.sum(((pins-pins.mean(0))@rotation+p0.mean(0)-p0)**2,axis=1)))))
aligned = np.array(aligned)
report = {'status': 'FAILED first tuning pass; movement is excessive and the idle skirt collapses',
          'samples': len(rows), 'hem_vertices': len(h0), 'method': 'Rigid Kabsch alignment of the same 12 pinned waist vertices removes anchor translation and rotation; compare the same 72 hem particles against the final idle frame. Distances are Unity Cloth vertex metres. This measures deformation, not fighter travel.',
          'reference_tick': reference['tick'], 'max_waist_alignment_residual_m': max(residual), 'clips': {}}
for name,ticks in clips.items():
    indices = [i for i,r in enumerate(rows) if r['tick'] in ticks]
    delta = aligned[indices]-h0
    distances = np.linalg.norm(delta,axis=2)
    center = delta.mean(1)
    report['clips'][name] = {'samples':len(indices),'peak_particle_displacement_m':float(distances.max()),
        'rms_particle_displacement_m':float(np.sqrt(np.mean(distances**2))),
        'peak_hem_centroid_displacement_m':float(np.linalg.norm(center,axis=1).max())}
report['limits'] = ['One run only; no acceptance claim.', 'The captured source predates the current invalid Nyx gameplay edits.', 'Pin alignment includes a small non-rigid waist fitting residual.', 'The first idle includes initialization and is unsuitable as an equilibrium stability score.']
(out/'motion-report.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
