#!/usr/bin/env python3
"""Synthesize the Neo Mall course clips (Wibou's course): katana shings and plastic mannequin
clatter with a chiptune derez, a synth pluck that climbs with every agent, the mall's PA chime,
an error buzzer, the four bosses (a squeaky deflating tube man, a hollow giant mannequin, a
fiberglass hot dog, a cardboard cutout) and the game store's door bell, dice and fanfare.

Everything is generated here from oscillators and seeded noise, so there is no third-party
source or licence. The clips are tracked in client/Unity/Assets/Resources/Audio/SFX/CourseMall/
with their .meta files (Resources/Courses/wibou_mall.asset points there); rerun this only to
change a clip, then commit it. Each clip is peak-normalized to -3 dBFS; needs numpy and ffmpeg
with libvorbis. CharacterCoursePresentation pitches course_bell (the pluck, A4) up the minor
pentatonic, one note per agent within a section, and plays course_pop under it.

  python3 tools/sfx/mall_course_clips.py
"""
import math
import subprocess
import sys
import tempfile
import wave
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'client/Unity/Assets/Resources/Audio/SFX/CourseMall'
SR = 44100
A4 = 440.0


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


def tone(freq, seconds, shape="sine", vibrato=(0.0, 0.0)):
    """An oscillator; freq may be an array (glides). vibrato = (Hz, semitones)."""
    x = t(seconds)
    f = np.broadcast_to(np.asarray(freq, dtype=float), x.shape)
    if vibrato[1]:
        f = f * 2 ** (vibrato[1] / 12 * np.sin(math.tau * vibrato[0] * x))
    phase = np.cumsum(math.tau * f / SR)
    if shape == "saw":
        return 2.0 * (phase / math.tau - np.floor(phase / math.tau + 0.5))
    if shape == "square":
        return np.sign(np.sin(phase))
    if shape == "pulse":
        return np.where(np.sin(phase) > 0.5, 1.0, -1.0)
    return np.sin(phase)


def mix(seconds, *parts):
    """parts: (start seconds, signal, gain)."""
    out = np.zeros(int(seconds * SR))
    for start, signal, gain in parts:
        i = int(start * SR)
        j = min(len(out), i + len(signal))
        out[i:j] += signal[:j - i] * gain
    return out


# ------------------------------------------------------------------ voices

def shing(seconds=0.45, seed=0, pitch=3200.0):
    """A katana cut: a bright swish of air into a ringing metal edge."""
    x = t(seconds)
    swish = bandpass(noise(seconds, seed), 2500, 9000) * env(len(x), 0.004, 0.05)
    ring = sum(a * np.sin(math.tau * pitch * r * x) for r, a in ((1.0, 1.0), (1.51, 0.5), (2.37, 0.3)))
    return swish * 0.9 + ring * env(len(x), 0.002, 0.12) * 0.35


def clatter(seconds, hits, seed, low=600.0, high=3500.0, tau=0.03):
    """Hollow plastic pieces knocking about."""
    out = np.zeros(int(seconds * SR))
    rng = np.random.default_rng(seed)
    for k in range(hits):
        i = int(rng.uniform(0, seconds * 0.7) * SR)
        n = noise(0.08, seed + k)
        f = rng.uniform(260, 520)
        knock = bandpass(n, low, high) * np.exp(-np.arange(len(n)) / SR / tau)
        knock += np.sin(math.tau * f * np.arange(len(n)) / SR) * np.exp(-np.arange(len(n)) / SR / (tau * 1.5)) * 0.8
        j = min(len(out), i + len(knock))
        out[i:j] += knock[:j - i] * rng.uniform(0.5, 1.0)
    return out


def derez(seconds=0.35, start=1760.0, seed=0):
    """A chiptune blip falling in steps: the agent breaks up into green code."""
    steps = 7
    out = []
    for k in range(steps):
        f = start * 2 ** (-k * 5 / 12)
        out.append(tone(f, seconds / steps, "pulse") * 0.6)
    x = np.concatenate(out)
    return x * env(len(x), 0.002, seconds * 0.5)


def pluck(freq, seconds=0.5):
    """A soft synth pluck (two detuned pulses through a closing filter)."""
    x = t(seconds)
    raw = tone(freq, seconds, "pulse") * 0.5 + tone(freq * 1.004, seconds, "saw") * 0.5 + tone(freq * 2, seconds) * 0.3
    return lowpass(raw, 300 + 4200 * np.exp(-x / 0.08)) * env(len(x), 0.003, 0.22)


def chime(freq, seconds=1.4):
    """A vibraphone-like PA chime tone."""
    x = t(seconds)
    body = sum(a * np.sin(math.tau * freq * r * x) * np.exp(-x / tau)
               for r, a, tau in ((1.0, 1.0, 0.9), (4.0, 0.25, 0.25), (10.0, 0.06, 0.08)))
    trem = 1 + 0.15 * np.sin(math.tau * 5.5 * x)
    return body * trem * np.clip(x / 0.004, 0, 1)


def buzzer(freq, seconds):
    x = t(seconds)
    return lowpass(tone(freq, seconds, "square") + 0.5 * tone(freq * 1.5, seconds, "saw"), 1800) * adsr(len(x), 0.005, seconds * 0.85, 0.03)


def register(seconds=1.2, seed=0):
    """A cash register: drawer slide, a bell ding and coins."""
    x = t(seconds)
    slide = bandpass(noise(0.25, seed), 300, 2000) * env(len(t(0.25)), 0.01, 0.08)
    bell = sum(a * np.sin(math.tau * 2100 * r * x) * np.exp(-x / 0.4) for r, a in ((1.0, 1.0), (2.7, 0.3)))
    coins = clatter(0.6, 9, seed + 3, low=3000, high=9000, tau=0.01)
    return mix(seconds, (0.0, slide, 0.8), (0.05, bell, 0.6), (0.12, coins, 0.5))


def door_bell(seconds=1.6, seed=0):
    """A shop's brass door bell: a few jingling strikes."""
    out = np.zeros(int(seconds * SR))
    rng = np.random.default_rng(seed)
    for k in range(6):
        i = int((0.02 + k * 0.07 + rng.uniform(0, 0.03)) * SR)
        x = t(1.0)
        f = rng.choice([2350.0, 2640.0, 2960.0])
        s = sum(a * np.sin(math.tau * f * r * x) * np.exp(-x / tau) for r, a, tau in ((1.0, 1.0, 0.35), (2.4, 0.4, 0.15)))
        j = min(len(out), i + len(s))
        out[i:j] += s[:j - i] * rng.uniform(0.4, 0.9)
    return out


def dice(seconds=1.2, seed=0):
    """A d20 rolling across a table: clicks that slow and settle."""
    out = np.zeros(int(seconds * SR))
    tt, gap = 0.0, 0.05
    k = 0
    while tt < seconds * 0.85:
        n = noise(0.03, seed + k)
        click = bandpass(n, 1500, 7000) * np.exp(-np.arange(len(n)) / SR / 0.004)
        i = int(tt * SR)
        j = min(len(out), i + len(click))
        out[i:j] += click[:j - i] * (1.0 - tt / seconds)
        tt += gap
        gap *= 1.18
        k += 1
    return out


def arpeggio(root, steps, note=0.09, shape="pulse"):
    out = [pluck(root * 2 ** (s / 12), note * 1.6)[:int(note * SR)] for s in steps]
    return np.concatenate(out)


def squeak(f0, f1, seconds):
    x = t(seconds)
    f = f0 * (f1 / f0) ** (x / seconds)
    return tone(f, seconds, "saw", vibrato=(30.0, 0.4)) * adsr(len(x), 0.01, seconds * 0.7, seconds * 0.2)


def hiss(seconds, seed, low=1500.0, high=7000.0):
    x = t(seconds)
    return bandpass(noise(seconds, seed), low, high) * adsr(len(x), 0.02, seconds * 0.6, seconds * 0.35)


def thunk(freq, seconds, seed):
    x = t(seconds)
    body = np.sin(math.tau * freq * x) * np.exp(-x / 0.09) + 0.5 * np.sin(math.tau * freq * 2.3 * x) * np.exp(-x / 0.05)
    return body + lowpass(noise(seconds, seed), 900) * env(len(x), 0.001, 0.04) * 1.5


def tear(seconds, seed):
    """Cardboard ripping: a crackly band of noise with jagged amplitude."""
    x = t(seconds)
    n = noise(seconds, seed)
    jag = (np.random.default_rng(seed + 1).random(len(x)) > 0.6).astype(float)
    jag = lowpass(jag, 120)
    return bandpass(n, 800, 5000) * (0.3 + jag * 1.5) * adsr(len(x), 0.01, seconds * 0.8, seconds * 0.15)


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
    minor = [A4 * 2 ** (s / 12) for s in (0, 3, 5, 7, 10, 12)]
    major = [A4 * 2 ** (s / 12) for s in (0, 4, 7, 12)]
    with tempfile.TemporaryDirectory() as tmp:
        # Every agent: the katana shing, the mannequin clattering apart and a chiptune derez.
        write('course_pop', mix(0.8, (0.0, shing(0.45, 1), 0.9), (0.04, clatter(0.6, 6, 2), 0.7), (0.06, derez(0.3, 1760, 3), 0.35)), tmp)
        write('course_bell', pluck(A4, 0.55), tmp)
        # A section's last agent: the mall PA's three-note chime.
        write('course_chord', mix(2.4, (0.0, chime(major[2] * 2), 0.8), (0.38, chime(major[1] * 2), 0.8), (0.76, chime(major[0] * 2, 1.6), 0.9)), tmp)
        # Crossing a YOU ARE HERE map: the PA's bing-bong and a little save chirp.
        write('course_saved', mix(1.6, (0.0, chime(major[3] * 2, 1.0), 0.7), (0.3, chime(major[1] * 2, 1.2), 0.7),
                                  (0.05, arpeggio(A4 * 2, (0, 7, 12), 0.05), 0.25)), tmp)
        # Reaching a checkpoint or the store with agents left: the error buzzer, twice, lower.
        write('course_missed', mix(1.2, (0.0, buzzer(196.0, 0.32), 0.8), (0.42, buzzer(146.8, 0.5), 0.8)), tmp)
        # The last agent: the cash register and a chiptune run up.
        write('course_all_clear', mix(2.6, (0.0, register(1.2, 21), 0.9), (0.35, arpeggio(A4, (0, 3, 7, 10, 12, 15, 19, 24), 0.08), 0.6),
                                      (1.0, pluck(A4 * 4, 1.2), 0.5)), tmp)
        # Through the game store's door: the brass door bell, a d20 rolling, a fanfare.
        fanfare = mix(1.6, (0.0, arpeggio(A4, (0, 4, 7), 0.12), 0.7), (0.36, pluck(A4 * 2, 1.2), 0.7), (0.36, pluck(A4 * 2 * 2 ** (4 / 12), 1.2), 0.5),
                      (0.36, pluck(A4 * 3, 1.2), 0.4))
        write('course_finish', mix(4.0, (0.0, door_bell(1.6, 31), 0.8), (0.5, dice(1.3, 32), 0.9), (1.7, fanfare, 0.9)), tmp)
        # Bosses: a hit each, then the break.
        write('course_tubeman_hit', mix(0.7, (0.0, shing(0.35, 41, 2800), 0.7), (0.03, squeak(900, 600, 0.35), 0.5),
                                        (0.0, hiss(0.4, 42, 1200, 6000), 0.4)), tmp)
        write('course_tubeman_break', mix(2.4, (0.0, shing(0.45, 43, 2600), 0.7), (0.1, squeak(800, 140, 1.8), 0.7),
                                          (0.1, hiss(2.0, 44, 600, 4000), 0.5), (1.6, clatter(0.6, 4, 45, 200, 1200, 0.05), 0.5)), tmp)
        write('course_agent_hit', mix(0.7, (0.0, shing(0.35, 51, 3400), 0.7), (0.02, thunk(170.0, 0.4, 52), 0.8)), tmp)
        write('course_agent_break', mix(2.0, (0.0, shing(0.5, 53, 3000), 0.8), (0.04, thunk(140.0, 0.5, 54), 0.9),
                                        (0.15, clatter(1.6, 14, 55, 300, 2800, 0.05), 0.8), (0.1, derez(0.6, 1320, 56), 0.4)), tmp)
        write('course_hotdog_hit', mix(0.7, (0.0, shing(0.35, 61, 3000), 0.6), (0.02, thunk(420.0, 0.35, 62), 0.7),
                                       (0.02, thunk(1100.0, 0.2, 63), 0.3)), tmp)
        write('course_hotdog_break', mix(2.0, (0.0, shing(0.5, 64, 2800), 0.7), (0.02, thunk(380.0, 0.5, 65), 0.8),
                                         (0.2, squeak(220, 660, 0.4), 0.4), (0.5, clatter(1.2, 8, 66, 200, 1500, 0.06), 0.7)), tmp)
        write('course_cop_hit', mix(0.6, (0.0, shing(0.3, 71, 3200), 0.6), (0.02, tear(0.25, 72), 0.7)), tmp)
        write('course_cop_break', mix(2.0, (0.0, shing(0.5, 73, 3000), 0.7), (0.05, tear(0.9, 74), 0.9),
                                      (1.0, thunk(120.0, 0.4, 75), 0.6)), tmp)


if __name__ == '__main__':
    main()
