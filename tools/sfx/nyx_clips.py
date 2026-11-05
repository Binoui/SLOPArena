#!/usr/bin/env python3
"""Rebuild Nyx contact sounds. Original procedural synthesis, CC0. No downloaded sources.

Seeded paper/ink noise, rounded sine bodies and camera pulses, lowpassed at 2200 Hz.
Needs ffmpeg with libvorbis. Outputs ignored local Resources audio. Normalizes
the synthesis to -3 dBFS before filtering; finished clips peak around -4 dBFS.
"""
import argparse
import math
import random
import struct
import subprocess
import tempfile
import wave
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SR = 44100


def build(out):
    out.mkdir(parents=True, exist_ok=True)
    recipes = [('rake', .18, 120, [0, .025]), ('clench', .28, 110, [0, .035, .08]),
               ('feather', .25, 260, [0, .055, .11]), ('like', .19, 440, [0]),
               ('shutter', .2, 180, [0, .045]), ('selfie', .3, 160, [0, .09]),
               ('heavy', .4, 72, [0, .04, .1])]
    with tempfile.TemporaryDirectory(prefix='nyx-sfx-') as tmp:
        for seed, (name, duration, hz, pulses) in enumerate(recipes):
            rng = random.Random(541 + seed)
            samples = []
            low = 0
            for i in range(int(SR * duration)):
                t = i / SR
                low += .14 * (rng.uniform(-1, 1) - low)
                env = sum(math.exp(-(t-p)*45) for p in pulses if t >= p)
                body = math.sin(2*math.pi*(hz*t + (700*t*t if name == 'like' else -hz*.5*t*t))) * math.exp(-t*19)
                value = (.65*low*env + .34*body) * min(1, t/.004) * min(1, (duration-t)/.03)
                samples.append(value)
            peak = max(abs(x) for x in samples)
            raw = Path(tmp)/f'{name}.wav'
            with wave.open(str(raw), 'wb') as f:
                f.setparams((1, 2, SR, 0, 'NONE', 'not compressed'))
                f.writeframes(b''.join(struct.pack('<h', int(x/peak*23000)) for x in samples))
            subprocess.run(['ffmpeg', '-v', 'error', '-y', '-i', str(raw), '-af', 'lowpass=f=2200',
                            '-c:a', 'libvorbis', '-q:a', '6', str(out/f'nyx_{name}.ogg')], check=True)
            print(name, duration)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out', type=Path, default=ROOT/'client/Unity/Assets/Resources/Audio/SFX/Nyx')
    build(parser.parse_args().out)
