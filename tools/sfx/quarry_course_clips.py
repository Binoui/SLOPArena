#!/usr/bin/env python3
"""Synthesize the Kaboom Quarry course clips (Manki's course): cartoon booms, a clown-horn
honk that climbs with every crate, bike-horn fanfares, a sad trombone, a fuse and the big one.

Everything is generated here from oscillators and seeded noise, so there is no third-party
source or licence. The clips are tracked in client/Unity/Assets/Resources/Audio/SFX/CourseQuarry/
with their .meta files (Resources/Courses/manki_quarry.asset points there); rerun this only to
change a clip, then commit it. Each clip is peak-normalized to -3 dBFS; needs numpy and ffmpeg with libvorbis.
CharacterCoursePresentation pitches course_bell (the honk, C5) up the major pentatonic, one
note per crate within a section, and plays course_pop under it.

  python3 tools/sfx/quarry_course_clips.py
"""
import math
import subprocess
import sys
import tempfile
import wave
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'client/Unity/Assets/Resources/Audio/SFX/CourseQuarry'
SR = 44100
C5 = 523.25


def t(seconds):
    return np.arange(int(seconds * SR)) / SR


def env(n, attack, tau):
    x = np.arange(n) / SR
    return np.clip(x / max(attack, 1e-4), 0, 1) * np.exp(-x / tau)


def adsr(n, attack, hold, release):
    x = np.arange(n) / SR
    a = np.clip(x / max(attack, 1e-4), 0, 1)
    r = np.clip(1 - (x - attack - hold) / max(release, 1e-4), 0, 1)
    return a * r


def lowpass(x, cutoff):
    """One-pole low-pass; cutoff may be an array (a sweep)."""
    cutoff = np.broadcast_to(np.asarray(cutoff, dtype=float), x.shape)
    a = np.exp(-math.tau * cutoff / SR)
    y = np.empty_like(x)
    acc = 0.0
    for i in range(len(x)):
        acc = (1 - a[i]) * x[i] + a[i] * acc
        y[i] = acc
    return y


def highpass(x, cutoff):
    return x - lowpass(x, cutoff)


def bandpass(x, low, high):
    return lowpass(highpass(x, low), high)


def noise(seconds, seed):
    return np.random.default_rng(seed).standard_normal(len(t(seconds)))


def saw(phase):
    return 2.0 * (phase / math.tau - np.floor(phase / math.tau + 0.5))


def tone(freq, seconds, shape="saw", vibrato=(0.0, 0.0)):
    """An oscillator; freq may be an array (glides). vibrato = (Hz, semitones)."""
    x = t(seconds)
    f = np.broadcast_to(np.asarray(freq, dtype=float), x.shape)
    if vibrato[1]:
        f = f * 2 ** (vibrato[1] / 12 * np.sin(math.tau * vibrato[0] * x))
    phase = np.cumsum(math.tau * f / SR)
    if shape == "saw":
        return saw(phase)
    if shape == "square":
        return np.sign(np.sin(phase))
    return np.sin(phase)


def boom(seconds, seed, low=55.0, crack=1.0, rumble=1.0):
    """A cartoon explosion: a crack, a pitched-down thump and a rolling low rumble."""
    x = t(seconds)
    n = noise(seconds, seed)
    crack_ = highpass(n, 1800) * env(len(x), 0.0005, 0.03) * 0.8 * crack
    body = lowpass(n, 1400 * np.exp(-x / 0.12) + 120) * env(len(x), 0.002, 0.22)
    thump = np.sin(np.cumsum(math.tau * (low * 2.4 * np.exp(-x / 0.07) + low) / SR)) * env(len(x), 0.002, 0.25) * 1.3
    roll = lowpass(n, 180) * env(len(x), 0.05, seconds * 0.35) * 2.2 * rumble
    return crack_ + body + thump + roll


def honk(freq, seconds=0.38, seed=0):
    """A clown bulb horn: a nasal reed that squeezes up into pitch and lets go."""
    x = t(seconds)
    bend = freq * (1 - 0.12 * np.exp(-x / 0.03))
    reed = tone(bend, seconds, "square", vibrato=(23.0, 0.25)) * 0.6 + tone(bend * 1.003, seconds, "saw") * 0.6
    nasal = bandpass(reed, freq * 1.2, freq * 4.5)
    breath = highpass(noise(seconds, seed), 3000) * 0.06
    return (nasal + breath) * adsr(len(x), 0.012, seconds * 0.55, seconds * 0.4)


def trombone(freq, seconds, wah=True, slide=0.0):
    """A plunger-muted trombone note; slide in semitones over the note."""
    x = t(seconds)
    f = freq * 2 ** (slide / 12 * np.clip(x / seconds, 0, 1))
    brass = tone(f, seconds, "saw", vibrato=(5.5, 0.18 if seconds > 0.6 else 0.05))
    cutoff = 500 + 1500 * (0.5 - 0.5 * np.cos(math.tau * 2.2 * x)) if wah else 1800
    return lowpass(lowpass(brass, cutoff), cutoff * 1.4) * adsr(len(x), 0.03, seconds * 0.7, seconds * 0.25)


def airhorn(freqs, seconds):
    x = t(seconds)
    out = np.zeros_like(x)
    for f in freqs:
        out += tone(f, seconds, "saw") + 0.5 * tone(f * 1.006, seconds, "square")
    return bandpass(out, 300, 3200) * adsr(len(x), 0.02, seconds * 0.75, seconds * 0.2) / len(freqs)


def ding(freq, seconds=1.2, seed=0):
    x = t(seconds)
    out = sum(a * np.sin(math.tau * freq * r * x) * np.exp(-x / (0.5 / (1 + k)))
              for k, (r, a) in enumerate(((1.0, 1.0), (2.76, 0.35), (5.4, 0.15))))
    return out + highpass(noise(seconds, seed), 3000) * env(len(x), 0.0005, 0.004) * 0.3


def ratchet(seconds, clicks, seed):
    x = t(seconds)
    out = np.zeros_like(x)
    for k in range(clicks):
        i = int(k / clicks * seconds * 0.8 * SR)
        n = noise(0.03, seed + k)
        j = min(len(out), i + len(n))
        out[i:j] += bandpass(n, 1500, 6000)[:j - i] * np.exp(-np.arange(j - i) / SR / 0.006)
    return out


def sizzle(seconds, seed):
    """A lit fuse: crackling hiss with sputters."""
    x = t(seconds)
    n = noise(seconds, seed)
    hiss = bandpass(n, 2500, 9000) * (0.7 + 0.3 * np.sin(math.tau * 9 * x))
    rng = np.random.default_rng(seed + 1)
    pops = np.zeros_like(x)
    for i in rng.integers(0, len(x) - 400, size=int(seconds * 40)):
        pops[i:i + 300] += highpass(noise(300 / SR, int(i)), 2000)[:300] * np.exp(-np.arange(300) / 60) * rng.uniform(0.5, 1.5)
    return hiss * 0.5 + pops * 0.6


def slide_whistle(f0, f1, seconds):
    x = t(seconds)
    f = f0 * (f1 / f0) ** (x / seconds)
    return tone(f, seconds, "sine", vibrato=(6.0, 0.1)) * adsr(len(x), 0.02, seconds * 0.8, seconds * 0.15)


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
    penta = [C5 * 2 ** (s / 12) for s in (0, 2, 4, 7, 9, 12)]
    with tempfile.TemporaryDirectory() as tmp:
        # Every crate: a short cartoon KA-BOOM with splintering wood, then the honk (course_bell)
        # pitched to the section's next note.
        splinters = bandpass(noise(0.5, 3), 900, 5000) * env(len(t(0.5)), 0.001, 0.08) * 0.6
        write('course_pop', mix(0.9, (0, boom(0.9, 1, low=70, rumble=0.6), 1.0), (0.01, splinters, 1.0)), tmp)
        write('course_bell', honk(C5, 0.4, seed=2), tmp)
        # A section's last crate: a bike-horn fanfare, da-da-DAAA.
        write('course_chord', mix(1.6, (0.0, honk(penta[0], 0.16, 5), 0.8), (0.16, honk(penta[2], 0.16, 6), 0.8),
                                  (0.32, airhorn((penta[0], penta[2], penta[3]), 1.1), 0.9)), tmp)
        # Crossing a lit checkpoint: the plunger box clicks and dings.
        write('course_saved', mix(1.5, (0, ratchet(0.25, 5, 7), 0.8), (0.24, ding(penta[3] * 2, 1.2, 8), 0.6)), tmp)
        # Reaching a checkpoint or the detonator with crates left: the sad trombone.
        g = 196.0
        sad = [(0.0, trombone(g * 2 ** (2 / 12), 0.36), 1.0), (0.4, trombone(g * 2 ** (1 / 12), 0.36), 1.0),
               (0.8, trombone(g, 0.36), 1.0), (1.2, trombone(g * 2 ** (-1 / 12), 1.1, slide=-0.6), 1.0)]
        write('course_missed', mix(2.4, *sad), tmp)
        # The last crate: a fuse fizzes, a slide whistle climbs, then a big boom and an air horn.
        write('course_all_clear', mix(4.2, (0, sizzle(1.1, 11), 0.55), (0.2, slide_whistle(400, 1400, 0.9), 0.35),
                                      (1.1, boom(2.4, 12, low=45), 1.0), (1.25, airhorn((C5, penta[2], penta[3]), 1.4), 0.55)), tmp)
        # Crossing the finish: the plunger goes down, a beat of silence, then the big one and a
        # rain of junk.
        x = t(1.6)
        rain = bandpass(noise(1.6, 14), 1500, 7000) * (0.4 + 0.6 * (np.random.default_rng(15).random(len(x)) > 0.995)) \
            * env(len(x), 0.2, 0.7)
        write('course_finish', mix(5.5, (0, ratchet(0.3, 7, 16), 0.9), (0.55, boom(4.5, 17, low=38, rumble=1.4), 1.0),
                                   (0.6, boom(1.2, 18, low=70), 0.5), (1.4, rain, 0.35),
                                   (0.75, airhorn((C5 / 2, penta[2] / 2, penta[3] / 2), 1.6), 0.45)), tmp)


if __name__ == '__main__':
    main()
