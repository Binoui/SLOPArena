#!/usr/bin/env python3
"""Rebuild the air dodge's paper "fwip" from generated noise (no third-party audio).

Three candidates are synthesized with numpy (seeded noise through a time-varying band-pass)
and peak-normalized to -3 dBFS; B, the owner's pick (2026-10-18), is levelled to -19.5 dBFS
RMS instead. --pick installs one as
client/Unity/Assets/Resources/Audio/SFX/AirDodge/air_dodge_fwip.ogg, the clip
AirDodgePresentation plays; --out also writes every candidate as WAV for review.
Needs numpy and ffmpeg with libvorbis. Provenance: docs/assets/credits-and-licenses.md.

  python3 tools/sfx/air_dodge_clips.py --pick b --out /tmp/air-dodge-sfx
"""
import argparse
import math
import re
import subprocess
import sys
import tempfile
import wave
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
TARGET = ROOT / 'client/Unity/Assets/Resources/Audio/SFX/AirDodge/air_dodge_fwip.ogg'
SR = 44100


def bandpass(signal, centre, q):
    """RBJ band-pass (0 dB peak) whose centre frequency follows `centre` sample by sample."""
    out = np.zeros_like(signal)
    x1 = x2 = y1 = y2 = 0.0
    for n, x in enumerate(signal):
        w0 = 2 * math.pi * centre[n] / SR
        alpha = math.sin(w0) / (2 * q)
        a0 = 1 + alpha
        y = (alpha * x - alpha * x2 + 2 * math.cos(w0) * y1 - (1 - alpha) * y2) / a0
        x2, x1, y2, y1 = x1, x, y1, y
        out[n] = y
    return out


def lowpass(signal, cutoff):
    """One-pole low-pass: takes the hiss off noise before it is band-passed."""
    a = math.exp(-2 * math.pi * cutoff / SR)
    out = np.zeros_like(signal)
    y = 0.0
    for n, x in enumerate(signal):
        y = (1 - a) * x + a * y
        out[n] = y
    return out


def sweep(start, end, seconds, samples):
    """Exponential frequency glide over `seconds`, then held."""
    t = np.minimum(np.arange(samples) / SR / seconds, 1.0)
    return start * (end / start) ** t


def envelope(samples, attack, decay):
    t = np.arange(samples) / SR
    return np.minimum(t / attack, 1.0) * np.exp(-np.maximum(t - attack, 0.0) / decay)


def candidate_a(rng):
    """Paper flick: a bright, quick downward band sweep with a crisp edge."""
    n = int(SR * 0.17)
    body = bandpass(rng.standard_normal(n), sweep(7200, 1700, 0.11, n), 1.8) * envelope(n, 0.003, 0.045)
    snap = bandpass(rng.standard_normal(n), np.full(n, 3200.0), 5.0) * envelope(n, 0.0005, 0.004)
    return body + 0.6 * snap


def candidate_b(rng):
    """Card whip: a rising band that swells in and lets go, like a flicked card.

    The owner's pick, softened after "a bit hard on the ears": the sweep stops at 2.7 kHz
    instead of 4.4, the band is wider, the noise is low-passed and the end releases smoothly.
    """
    n = int(SR * 0.19)
    rise, hold, release = 0.03, 0.075, 0.045
    body = bandpass(lowpass(rng.standard_normal(n), 4200), sweep(750, 2700, rise + hold, n), 1.6)
    t = np.arange(n) / SR
    swell = np.minimum(t / rise, 1.0) ** 1.5
    tail = np.where(t < rise + hold, 1.0, np.exp(-(t - rise - hold) / release))
    return body * swell * tail


def candidate_c(rng):
    """Snap and whoosh: a short paper snap, then a soft falling whoosh."""
    n = int(SR * 0.2)
    snap = bandpass(rng.standard_normal(n), np.full(n, 2600.0), 6.0) * envelope(n, 0.0005, 0.005)
    whoosh = np.zeros(n)
    lead = int(SR * 0.006)
    m = n - lead
    whoosh[lead:] = bandpass(rng.standard_normal(m), sweep(3400, 1300, 0.14, m), 1.2) * envelope(m, 0.008, 0.06)
    return 0.8 * snap + whoosh


CANDIDATES = {'a': candidate_a, 'b': candidate_b, 'c': candidate_c}
# Loudness targets in dBFS RMS; the others are peak-normalized.
LEVEL = {'b': -19.5}


def write_wav(path, signal, rms_db=None):
    ceiling = 10 ** (-3 / 20)
    if rms_db is None:
        signal = signal / (float(np.max(np.abs(signal))) or 1.0) * ceiling
    else:
        signal = signal / (float(np.sqrt((signal ** 2).mean())) or 1.0) * 10 ** (rms_db / 20)
        signal = signal * min(1.0, ceiling / (float(np.max(np.abs(signal))) or 1.0))
    pcm = np.clip(signal, -1.0, 1.0)
    with wave.open(str(path), 'wb') as out:
        out.setnchannels(1)
        out.setsampwidth(2)
        out.setframerate(SR)
        out.writeframes((pcm * 32767).astype('<i2').tobytes())


def encode(wav, ogg):
    ogg.parent.mkdir(parents=True, exist_ok=True)
    result = subprocess.run(['ffmpeg', '-hide_banner', '-loglevel', 'error', '-y', '-i', str(wav),
                             '-c:a', 'libvorbis', '-q:a', '6', str(ogg)], capture_output=True, text=True)
    if result.returncode:
        sys.exit(result.stderr)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--pick', choices=sorted(CANDIDATES), help='install this candidate as the game clip')
    parser.add_argument('--out', type=Path, help='also write every candidate as WAV here')
    args = parser.parse_args()
    if not args.pick and not args.out:
        parser.error('nothing to do: pass --pick and/or --out')
    clips = {name: make(np.random.default_rng(1103 + i)) for i, (name, make) in enumerate(sorted(CANDIDATES.items()))}
    if args.out:
        args.out.mkdir(parents=True, exist_ok=True)
        for name, signal in clips.items():
            write_wav(args.out / f'air_dodge_fwip_{name}.wav', signal, LEVEL.get(name))
            print(f'{name}: {len(signal) / SR:.3f}s -> {args.out}/air_dodge_fwip_{name}.wav')
    if args.pick:
        with tempfile.TemporaryDirectory() as tmp:
            wav = Path(tmp) / 'fwip.wav'
            write_wav(wav, clips[args.pick], LEVEL.get(args.pick))
            encode(wav, TARGET)
        print(f'installed {args.pick} -> {TARGET.relative_to(ROOT)}')


if __name__ == '__main__':
    main()
