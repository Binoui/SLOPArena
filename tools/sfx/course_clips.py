#!/usr/bin/env python3
"""Synthesize the FightGuy Sanctuary course clips (bells, gongs, temple blocks, paper).

Everything is generated here from sine partials and seeded noise, so there is no third-party
source or licence. The clips are tracked in client/Unity/Assets/Resources/Audio/SFX/Course/ with
their .meta files; rerun this only to change a clip, then commit it. Each clip is peak-normalized to -3 dBFS; needs numpy
and ffmpeg with libvorbis. CharacterCoursePresentation pitches course_bell up the D major
pentatonic (D5 E5 F#5 A5 B5), one note per break within a section.

  python3 tools/sfx/course_clips.py
"""
import math
import random
import subprocess
import sys
import tempfile
import wave
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'client/Unity/Assets/Resources/Audio/SFX/Course'
SR = 44100
D5 = 587.33
# D major pentatonic from D5; course_bell is D5 and the runtime pitches it to the others.
PENTA = [D5 * 2 ** (s / 12) for s in (0, 2, 4, 7, 9)]


def t(seconds):
    return np.arange(int(seconds * SR)) / SR


def env(n, attack, tau):
    x = np.arange(n) / SR
    return np.clip(x / max(attack, 1e-4), 0, 1) * np.exp(-x / tau)


def lowpass(x, cutoff):
    """One-pole low-pass (a plain loop: the clips are short)."""
    a = math.exp(-math.tau * cutoff / SR)
    y = np.empty_like(x)
    acc = 0.0
    for i, v in enumerate(x):
        acc = (1 - a) * v + a * acc
        y[i] = acc
    return y


def highpass(x, cutoff):
    return x - lowpass(x, cutoff)


def noise(seconds, seed):
    return np.random.default_rng(seed).standard_normal(len(t(seconds)))


def bell(freq, seconds=2.2, partials=((1.0, 1.0), (2.76, 0.42), (5.40, 0.2), (8.93, 0.08)), tau=1.1, beat=0.7, seed=0):
    """A struck bell: inharmonic partials, each a slightly detuned (beating) pair, plus a strike click."""
    rnd = random.Random(seed)
    x = t(seconds)
    out = np.zeros_like(x)
    for k, (ratio, amp) in enumerate(partials):
        f = freq * ratio
        phase = rnd.uniform(0, math.tau)
        pair = np.sin(math.tau * f * x + phase) + np.sin(math.tau * (f + beat * (k + 1) * 0.5) * x + phase)
        out += amp * 0.5 * pair * env(len(x), 0.002, tau / (1 + k * 0.9))
    strike = noise(seconds, seed) * env(len(x), 0.0005, 0.006) * 0.15
    return out + highpass(strike, 2500)


def bowl(freq, seconds=3.5, seed=0):
    """A singing bowl: a bell with slower decay and wider beating."""
    return bell(freq, seconds, partials=((1.0, 1.0), (2.71, 0.5), (5.15, 0.22), (8.40, 0.1)), tau=2.2, beat=1.3, seed=seed)


def gong(freq, seconds, seed):
    """A gong: inharmonic partials that bloom after the strike, a short pitch glide and a wash."""
    rnd = random.Random(seed)
    x = t(seconds)
    out = np.zeros_like(x)
    glide = 1 + 0.04 * np.exp(-x / 0.35)
    for k, ratio in enumerate([1.0, 1.47, 2.09, 2.56, 3.11, 3.68, 4.23, 5.02, 6.11, 7.4]):
        amp = rnd.uniform(0.5, 1.0) / (1 + k * 0.35)
        bloom = 1 - np.exp(-x / (0.05 + 0.08 * k))
        phase = np.cumsum(math.tau * freq * ratio * glide / SR)
        out += amp * np.sin(phase + rnd.uniform(0, math.tau)) * bloom * np.exp(-x / (4.0 / (1 + k * 0.25)))
    return out + lowpass(noise(seconds, seed), 900) * env(len(x), 0.01, 0.6) * 0.25


def paper_pop(seed):
    """A paper lantern bursting: a filtered noise snap, a rustle and a soft thump."""
    n = noise(0.35, seed)
    x = t(0.35)
    burst = highpass(lowpass(n, 6000), 900) * env(len(n), 0.001, 0.025)
    rustle = highpass(n, 3000) * env(len(n), 0.01, 0.09) * 0.25
    thump = np.sin(math.tau * 140 * x * (1 - 0.3 * x)) * env(len(x), 0.001, 0.05) * 0.6
    return burst * 0.9 + rustle + thump


def temple_block(freq, seed):
    x = t(0.35)
    knock = (np.sin(math.tau * freq * x) + 0.4 * np.sin(math.tau * freq * 1.43 * x)) * env(len(x), 0.0008, 0.035)
    return knock + highpass(noise(0.35, seed), 2000) * env(len(x), 0.0003, 0.004) * 0.4


def mix(seconds, *parts):
    """parts: (start seconds, signal, gain)."""
    out = np.zeros(int(seconds * SR))
    for start, signal, gain in parts:
        i = int(start * SR)
        j = min(len(out), i + len(signal))
        out[i:j] += signal[:j - i] * gain
    return out


def write(name, x, tmp):
    x = x / (np.max(np.abs(x)) + 1e-9) * 10 ** (-3 / 20)
    fade = min(len(x), int(0.02 * SR))
    x[-fade:] *= np.linspace(1, 0, fade)
    raw = Path(tmp) / f'{name}.wav'
    with wave.open(str(raw), 'wb') as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes((x * 32767).astype(np.int16).tobytes())
    result = subprocess.run(['ffmpeg', '-hide_banner', '-loglevel', 'error', '-y', '-i', str(raw),
                             '-c:a', 'libvorbis', '-q:a', '6', str(OUT / f'{name}.ogg')], capture_output=True, text=True)
    if result.returncode:
        sys.exit(result.stderr)
    print(f'{name}: {len(x) / SR:.2f}s')


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory() as tmp:
        # Every break: the paper pop, then course_bell pitched to the section's next note.
        write('course_pop', paper_pop(1), tmp)
        write('course_bell', bell(D5, 2.2, seed=2), tmp)
        # A section's last break: a soft rolled D major chord an octave up.
        write('course_chord', mix(3.4, *[(k * 0.06, bell(f, 3.2, tau=1.6, seed=40 + k), 1.0)
                                          for k, f in enumerate((D5 * 2, PENTA[2] * 2, PENTA[3] * 2))]), tmp)
        # Crossing a lit checkpoint: one low singing bowl.
        write('course_saved', mix(3.0, (0, bowl(D5 / 2, 3.0, seed=3), 1.0), (0.02, bell(PENTA[3] * 2, 1.4, tau=0.5, seed=4), 0.12)), tmp)
        # Reaching a checkpoint or the shrine with targets missing: two soft wood knocks and a
        # gentle falling phrase, never a buzzer.
        write('course_missed', mix(2.6, (0, temple_block(700, 6), 0.7), (0.17, temple_block(560, 7), 0.6),
                                   (0.45, bell(PENTA[3], 2.0, tau=0.9, seed=8), 0.35),
                                   (0.75, bell(PENTA[0], 1.85, tau=1.0, seed=9), 0.35)), tmp)
        # The last target: the great gong, then a rising shimmer as the shrine lights up.
        shimmer = [(0.5 + i * 0.07, bell(f * 2, 2.0, tau=0.8, seed=60 + i), 0.18) for i, f in enumerate(PENTA + [D5 * 2])]
        write('course_all_clear', mix(6.5, (0, gong(78, 6.5, 11), 1.0), *shimmer), tmp)
        # Crossing the finish: the gate gong, prayer flags fluttering and a quick chime cascade.
        x = t(1.4)
        flutter = highpass(noise(1.4, 13), 1200) * (0.6 + 0.4 * np.sin(math.tau * 14 * x)) * env(len(x), 0.05, 0.5) * 0.35
        cascade = [(0.08 + i * 0.055, bell(f, 1.8, tau=0.7, seed=70 + i), 0.22)
                   for i, f in enumerate(PENTA + [D5 * 2, PENTA[2] * 2])]
        write('course_finish', mix(6.0, (0, gong(104, 6.0, 14), 0.9), (0.02, flutter, 1.0), *cascade), tmp)


if __name__ == '__main__':
    main()
