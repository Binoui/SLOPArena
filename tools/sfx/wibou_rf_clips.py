#!/usr/bin/env python3
"""Synthesize Wibou's steel Quick Draw and code flash. No third-party samples.

Rebuild with python3 tools/sfx/wibou_rf_clips.py. Seeded noise, sine partials,
quantized sample holds and a 2.5 kHz low-pass keep repeated cuts soft.
"""
import argparse
import tempfile
from pathlib import Path

import numpy as np

from homing_dash_clips import bandpass, encode, env, lowpass, sweep, t_axis, tone, write_wav

ROOT = Path(__file__).resolve().parents[2]
TARGET = ROOT / 'client/Unity/Assets/Resources/Audio/SFX/Wibou'


def crush(signal, hold=12):
    held = np.repeat(signal[::hold], hold)[:len(signal)]
    return np.round(held * 12) / 12


def clips():
    rng = np.random.default_rng(5291)
    t = t_axis(.32)
    shing = sum(a * tone(t, f) * env(t, .003, d) for f, a, d in
                [(690, 1, .075), (1103, .6, .11), (1739, .25, .055), (2261, .12, .04)])
    shing += bandpass(rng.standard_normal(len(t)), sweep(t, 900, 2100, .12), 1.7) * env(t, .012, .045) * .8
    t = t_axis(.1)
    tick = (tone(t, 940) + .4 * tone(t, 1500) + .6 * bandpass(rng.standard_normal(len(t)), 1300, 2)) * env(t, .001, .012)
    t = t_axis(.4)
    dropout = crush((tone(t, sweep(t, 390, 85, .36)) + .25 * rng.standard_normal(len(t))) * env(t, .012, .15), 22)
    dropout *= np.where((t * 42).astype(int) % 4 == 3, .12, 1)
    t = t_axis(.105)
    blip = crush((tone(t, np.where(t < .035, 780, 1170)) + .3 * tone(t, 390)) * env(t, .002, .025))
    t = t_axis(.085)
    click = bandpass(rng.standard_normal(len(t)), 1050, 1.2) * env(t, .0007, .009)
    t = t_axis(.28)
    pop = crush((tone(t, sweep(t, 240, 65, .12)) + .65 * rng.standard_normal(len(t))) * env(t, .001, .045), 18)
    return {'quick_shing': (shing, -22), 'quick_tick': (tick, -27),
            'flash_dropout': (dropout, -24), 'flash_blip': (blip, -25),
            'flash_click': (click, -24), 'flash_pop': (pop, -21)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out', type=Path, default=TARGET)
    args = parser.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory() as tmp:
        for name, (signal, db) in clips().items():
            signal = lowpass(signal, 2500)
            wav = Path(tmp) / f'{name}.wav'
            write_wav(wav, signal, db)
            path = args.out / f'wibou_{name}.ogg'
            encode(wav, path)
            print(path.relative_to(ROOT) if path.is_relative_to(ROOT) else path)


if __name__ == '__main__':
    main()
