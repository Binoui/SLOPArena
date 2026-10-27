#!/usr/bin/env python3
"""Rebuild the homing dash lock-on sounds from generated tones and noise (no third-party audio).

Three beats (ADR-0042, concept C): "lock" as the reticle lands on the float, "whoosh" as the
dash flies, "arrive" with the "!" stamp. Two sets: A is tonal (soft blips and a ding), B is
paper (clicks, a band-passed whoosh, a stamp); B is the owner's pick (2026-10-27). Everything stays mostly under 2.5 kHz and is
levelled to -19.5 dBFS RMS with peaks below -3 dBFS, like the air dodge's fwip.

--pick installs one set as client/Unity/Assets/Resources/Audio/SFX/HomingDash/homing_dash_*.ogg,
the clips HomingDashPresentation plays; --out also writes every candidate as WAV for review.
Needs numpy and ffmpeg with libvorbis. Provenance: docs/assets/credits-and-licenses.md.

  python3 tools/sfx/homing_dash_clips.py --pick b --out /tmp/homing-dash-sfx
"""
import argparse
import math
import subprocess
import sys
import tempfile
import wave
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
TARGET = ROOT / 'client/Unity/Assets/Resources/Audio/SFX/HomingDash'
SR = 44100
BEATS = ('lock', 'whoosh', 'arrive')


def t_axis(seconds):
    return np.arange(int(SR * seconds)) / SR


def env(t, attack, decay):
    return np.minimum(t / attack, 1.0) * np.exp(-np.maximum(t - attack, 0.0) / decay)


def tone(t, freq, harmonics=((1, 1.0),)):
    phase = 2 * math.pi * np.cumsum(np.broadcast_to(freq, t.shape)) / SR
    return sum(a * np.sin(k * phase) for k, a in harmonics)


def bandpass(signal, centre, q):
    """RBJ band-pass whose centre frequency follows `centre` sample by sample."""
    centre = np.broadcast_to(centre, signal.shape)
    out = np.zeros_like(signal)
    x1 = x2 = y1 = y2 = 0.0
    for n, x in enumerate(signal):
        w0 = 2 * math.pi * centre[n] / SR
        alpha = math.sin(w0) / (2 * q)
        y = (alpha * x - alpha * x2 + 2 * math.cos(w0) * y1 - (1 - alpha) * y2) / (1 + alpha)
        x2, x1, y2, y1 = x1, x, y1, y
        out[n] = y
    return out


def lowpass(signal, cutoff):
    a = math.exp(-2 * math.pi * cutoff / SR)
    out = np.zeros_like(signal)
    y = 0.0
    for n, x in enumerate(signal):
        y = (1 - a) * x + a * y
        out[n] = y
    return out


def place(total, *parts):
    """Mix (start_seconds, signal) parts into one buffer `total` seconds long."""
    out = np.zeros(int(SR * total))
    for start, part in parts:
        i = int(SR * start)
        out[i:i + len(part)] += part[:len(out) - i]
    return out


def sweep(t, start, end, seconds):
    return start * (end / start) ** np.minimum(t / seconds, 1.0)


# ── Set A: tonal ──

def a_lock(rng):
    """Two soft rising blips, a target-acquired 'bip-bip'."""
    t = t_axis(0.09)
    blip = lambda f: tone(t, f, ((1, 1.0), (2, 0.18))) * env(t, 0.004, 0.025)
    return place(0.2, (0.0, blip(880.0)), (0.075, blip(1175.0)))


def a_whoosh(rng):
    """A soft airy rise with a low tone gliding up under it."""
    t = t_axis(0.42)
    air = bandpass(lowpass(rng.standard_normal(len(t)), 3000), sweep(t, 420, 1700, 0.3), 1.4)
    glide = tone(t, sweep(t, 180, 360, 0.3)) * 0.35
    shape = np.minimum(t / 0.12, 1.0) ** 1.4 * np.exp(-np.maximum(t - 0.22, 0) / 0.08)
    return (air + glide) * shape


def a_arrive(rng):
    """A short bright ding, a fifth over a soft body."""
    t = t_axis(0.45)
    ding = tone(t, 988.0, ((1, 1.0), (2, 0.25), (3, 0.08))) * env(t, 0.003, 0.12)
    body = tone(t, 659.0) * env(t, 0.003, 0.06) * 0.6
    return ding + body


# ── Set B: paper ──

def b_lock(rng):
    """A camera-focus 'ch-chk': two short paper clicks."""
    t = t_axis(0.05)
    click = lambda c: bandpass(rng.standard_normal(len(t)), c, 7.0) * env(t, 0.0008, 0.008)
    return place(0.15, (0.0, click(1100.0)), (0.06, click(1500.0) * 1.2))


def b_whoosh(rng):
    """A card whip stretched out: a band rising through the dash, released smoothly."""
    t = t_axis(0.42)
    body = bandpass(lowpass(rng.standard_normal(len(t)), 2600), sweep(t, 550, 1900, 0.28), 1.6)
    shape = np.minimum(t / 0.1, 1.0) ** 1.5 * np.exp(-np.maximum(t - 0.24, 0) / 0.07)
    return body * shape


def b_arrive(rng):
    """A rubber-stamp thunk with a short paper slap on top."""
    t = t_axis(0.3)
    thunk = tone(t, sweep(t, 360, 170, 0.08), ((1, 1.0), (2, 0.3))) * env(t, 0.002, 0.07)
    slap = bandpass(rng.standard_normal(len(t)), 1600.0, 3.0) * env(t, 0.001, 0.02) * 0.8
    return thunk + slap


SETS = {
    'a': {'lock': a_lock, 'whoosh': a_whoosh, 'arrive': a_arrive},
    'b': {'lock': b_lock, 'whoosh': b_whoosh, 'arrive': b_arrive},
}
RMS_DB = {'lock': -21.0, 'whoosh': -22.0, 'arrive': -19.5}


def write_wav(path, signal, rms_db):
    signal = signal / (float(np.sqrt((signal ** 2).mean())) or 1.0) * 10 ** (rms_db / 20)
    ceiling = 10 ** (-3 / 20)
    signal = signal * min(1.0, ceiling / (float(np.max(np.abs(signal))) or 1.0))
    with wave.open(str(path), 'wb') as out:
        out.setnchannels(1)
        out.setsampwidth(2)
        out.setframerate(SR)
        out.writeframes((np.clip(signal, -1, 1) * 32767).astype('<i2').tobytes())


def encode(wav, ogg):
    ogg.parent.mkdir(parents=True, exist_ok=True)
    result = subprocess.run(['ffmpeg', '-hide_banner', '-loglevel', 'error', '-y', '-i', str(wav),
                             '-c:a', 'libvorbis', '-q:a', '6', str(ogg)], capture_output=True, text=True)
    if result.returncode:
        sys.exit(result.stderr)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--pick', choices=sorted(SETS), help='install this set as the game clips')
    parser.add_argument('--out', type=Path, help='also write every candidate as WAV here')
    args = parser.parse_args()
    if not args.pick and not args.out:
        parser.error('nothing to do: pass --pick and/or --out')
    clips = {(s, b): SETS[s][b](np.random.default_rng(2042 + i))
             for i, (s, b) in enumerate((s, b) for s in sorted(SETS) for b in BEATS)}
    if args.out:
        args.out.mkdir(parents=True, exist_ok=True)
        for (s, b), signal in clips.items():
            path = args.out / f'homing_dash_{b}_{s}.wav'
            write_wav(path, signal, RMS_DB[b])
            print(f'{s} {b}: {len(signal) / SR:.3f}s -> {path}')
    if args.pick:
        with tempfile.TemporaryDirectory() as tmp:
            for b in BEATS:
                wav = Path(tmp) / f'{b}.wav'
                write_wav(wav, clips[(args.pick, b)], RMS_DB[b])
                encode(wav, TARGET / f'homing_dash_{b}.ogg')
        print(f'installed set {args.pick} -> {TARGET.relative_to(ROOT)}')


if __name__ == '__main__':
    main()
