#!/usr/bin/env python3
"""Record each clip's authored duration, frame count and loop flag in GLB animation extras.slop (ADR-0065 rule 1).

python3 add_extras.py <in.glb> <clips.json> <out.glb>
The duration is the largest binary32 at most frames/60 whose frame count max(1, ceil(binary32(d*60))) equals frames.
"""
import json, math, struct, sys
import numpy as np

src, manifest, dst = sys.argv[1:4]
data = open(src, 'rb').read()
(json_length,) = struct.unpack_from('<I', data, 12)
gltf = json.loads(data[20:20 + json_length])
rest = data[20 + json_length:]
clips = {c['name']: c for c in json.load(open(manifest))}


def duration(frames):
    d = np.float32(frames / 60.0)
    while max(1, math.ceil(np.float32(np.float64(d) * 60.0))) > frames:
        d = np.nextafter(d, np.float32(0))
    assert max(1, math.ceil(np.float32(np.float64(d) * 60.0))) == frames
    return d


for animation in gltf['animations']:
    c = clips[animation['name']]
    d = duration(c['frames'])
    slop = {'durationSecondsBits': int(np.frombuffer(np.float32(d).tobytes(), np.int32)[0]), 'frameCount': c['frames']}
    if c['loop']:
        slop['loop'] = True
    animation.setdefault('extras', {})['slop'] = slop
missing = set(clips) - {a['name'] for a in gltf['animations']}
assert not missing, missing
body = json.dumps(gltf, separators=(',', ':')).encode()
body += b' ' * (-len(body) % 4)
out = struct.pack('<III', 0x46546C67, 2, 12 + 8 + len(body) + len(rest)) + struct.pack('<II', len(body), 0x4E4F534A) + body + rest
open(dst, 'wb').write(out)
print('wrote', dst, len(out), 'bytes,', len(gltf['animations']), 'clips')
