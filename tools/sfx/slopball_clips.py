#!/usr/bin/env python3
"""Synthesize the Slopball feel-pass clips: the rubber ball, the chain-link cage, the sender and the scoring cues.

Everything is generated here from sine partials and seeded noise, so there is no third-party
source or licence. Writes client/Unity/Assets/Resources/Audio/SFX/Slopball/*.ogg; rerun this
only to change a clip. Each clip is peak-normalized to -3 dBFS; needs numpy and ffmpeg with
libvorbis. SlopballPresentation pitches slopball_note up the C major pentatonic (one note per
return in a combo) and slopball_beep up a fifth for the sender's last countdown light;
slopball_heat marks rally heat stepping up and slopball_trick a trick shot.

  python3 tools/sfx/slopball_clips.py
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
OUT = ROOT / 'client/Unity/Assets/Resources/Audio/SFX/Slopball'
SR = 44100
C5 = 523.25


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


def bandpass(x, lo, hi):
    return lowpass(highpass(x, lo), hi)


def noise(seconds, seed):
    return np.random.default_rng(seed).standard_normal(len(t(seconds)))


def glide(f0, f1, seconds, tau):
    """A sine whose pitch falls exponentially from f0 toward f1."""
    x = t(seconds)
    f = f1 + (f0 - f1) * np.exp(-x / tau)
    return np.sin(np.cumsum(math.tau * f / SR))


def mix(seconds, *parts):
    """parts: (start seconds, signal, gain)."""
    out = np.zeros(int(seconds * SR))
    for start, signal, gain in parts:
        i = int(start * SR)
        j = min(len(out), i + len(signal))
        out[i:j] += signal[:j - i] * gain
    return out


def room(x, amount, seed):
    """A short warehouse tail: the dry hit convolved with dark, decaying noise."""
    ir = lowpass(lowpass(noise(0.6, seed), 1800), 1800)
    ir = ir * env(len(ir), 0.004, 0.16)
    wet = np.convolve(x, ir)[:len(x) + len(ir)]
    dry = np.concatenate([x, np.zeros(len(ir))])[:len(wet)]
    return dry + wet * amount / (np.max(np.abs(wet)) + 1e-9) * np.max(np.abs(x))


# ── The ball ─────────────────────────────────────────────────────────────────

def kick(strength, seed):
    """A rubber ball struck: a pitched body thump, a slap and the ball's hollow 'boing'."""
    s = 0.35 if strength < 0.5 else 0.5
    n = int(s * SR)
    body = glide(230 - 110 * strength, 120 - 50 * strength, s, 0.05) * env(n, 0.001, 0.05 + 0.07 * strength)
    slap = lowpass(bandpass(noise(s, seed), 600, 3000), 3000) * env(n, 0.0005, 0.005 + 0.008 * strength) * (0.5 + 0.5 * strength)
    hollow = glide(470 - 120 * strength, 400 - 110 * strength, s, 0.08) * env(n, 0.002, 0.07 + 0.08 * strength) * 0.35
    sub = np.sin(math.tau * 55 * t(s)) * env(n, 0.002, 0.08) * 0.5 * strength
    return room(body + slap + hollow + sub, 0.12 + 0.12 * strength, seed)


def floor_bounce(seed):
    n = int(0.3 * SR)
    body = glide(170, 105, 0.3, 0.04) * env(n, 0.001, 0.05)
    tick = lowpass(highpass(noise(0.3, seed), 1500), 5000) * env(n, 0.0003, 0.003) * 0.3
    hollow = glide(420, 380, 0.3, 0.1) * env(n, 0.002, 0.06) * 0.25
    return room(body + tick + hollow, 0.2, seed)


def cage(seed, length=0.55):
    """Chain-link rattle: a mesh thump and a cloud of tiny metallic tinks that thins out."""
    rnd = random.Random(seed)
    out = glide(110, 70, length, 0.05) * env(int(length * SR), 0.001, 0.05) * 0.8
    for _ in range(70):
        start = (rnd.random() ** 2.2) * length * 0.8
        f = rnd.uniform(2200, 6800)
        d = rnd.uniform(0.01, 0.04)
        tink = np.sin(math.tau * f * t(d) + rnd.uniform(0, 6)) * env(int(d * SR), 0.0002, d / 3)
        tink += np.sin(math.tau * f * 1.5 * t(d)) * env(int(d * SR), 0.0002, d / 5) * 0.4
        k = int(start * SR)
        out[k:k + len(tink)] += tink[:len(out) - k] * rnd.uniform(0.15, 0.5) * (1 - start / length)
    rattle = bandpass(noise(length, seed + 1), 1500, 6000) * env(len(out), 0.002, 0.12) \
        * (0.6 + 0.4 * np.sin(math.tau * 31 * t(length))) * 0.35
    return room(out + rattle, 0.25, seed)


def net(seed):
    n = int(0.4 * SR)
    sweep = bandpass(noise(0.4, seed), 1800, 7000) * env(n, 0.01, 0.09) * (1 + np.sin(math.tau * 9 * t(0.4)) * 0.3)
    rope = glide(140, 100, 0.4, 0.06) * env(n, 0.002, 0.06) * 0.6
    return sweep * 0.8 + rope


# ── The sender ───────────────────────────────────────────────────────────────

def beep(freq, seconds=0.09):
    x = t(seconds)
    square = np.sign(np.sin(math.tau * freq * x)) * 0.35 + np.sin(math.tau * freq * x) * 0.65
    return lowpass(square, 4000) * env(len(x), 0.002, seconds * 0.8)


def serve_pop(seed):
    """Compressed-air cannon: a low thoomp, a burst, a cork and an airy hiss."""
    n = int(0.6 * SR)
    thoomp = glide(120, 48, 0.6, 0.06) * env(n, 0.002, 0.11)
    burst = lowpass(noise(0.6, seed), 1600) * env(n, 0.001, 0.035) * 0.9
    hiss = bandpass(noise(0.6, seed + 1), 3000, 9000) * env(n, 0.01, 0.12) * 0.22
    cork = glide(900, 500, 0.6, 0.02) * env(n, 0.0005, 0.015) * 0.25
    return room(thoomp + burst + hiss + cork, 0.2, seed)


# ── Scoring ──────────────────────────────────────────────────────────────────

def splat(seed, wet=1.0):
    """A wet SLOP: a noise splash, a falling 'blop' and a few bubbly pops."""
    rnd = random.Random(seed)
    n = int(0.5 * SR)
    out = lowpass(noise(0.5, seed), 2600) * env(n, 0.001, 0.05) * 0.9 + glide(320, 120, 0.5, 0.05) * env(n, 0.002, 0.07)
    for _ in range(5):
        start = rnd.uniform(0.04, 0.22)
        f = rnd.uniform(500, 1100)
        bubble = glide(f, f * 1.6, 0.06, 0.02) * env(int(0.06 * SR), 0.001, 0.015) * 0.25 * wet
        k = int(start * SR)
        out[k:k + len(bubble)] += bubble
    return room(out, 0.15, seed)


def pluck(freq, seconds=0.9, seed=0):
    """A bright kalimba-like pluck for the combo ladder."""
    x = t(seconds)
    n = len(x)
    out = np.sin(math.tau * freq * x) * env(n, 0.002, 0.35)
    out += 0.45 * np.sin(math.tau * freq * 2 * x) * env(n, 0.002, 0.12)
    out += 0.2 * np.sin(math.tau * freq * 3.01 * x) * env(n, 0.001, 0.05)
    return out + highpass(noise(seconds, seed), 3000) * env(n, 0.0005, 0.004) * 0.2


def flip(seed, count=2):
    """The combo board's cards: quick plastic clacks."""
    rnd = random.Random(seed)
    out = np.zeros(int(0.3 * SR))
    for i in range(count):
        start = i * 0.07 + rnd.uniform(0, 0.01)
        c = bandpass(noise(0.03, seed + i), 1200, 6000) * env(int(0.03 * SR), 0.0003, 0.005)
        c += np.sin(math.tau * 1700 * t(0.03)) * env(int(0.03 * SR), 0.0003, 0.006) * 0.3
        k = int(start * SR)
        out[k:k + len(c)] += c
    return out


def stab(freqs, seconds=0.7):
    """A funky synth-brass chord stab: detuned saws through a closing low-pass."""
    x = t(seconds)
    out = np.zeros_like(x)
    for f in freqs:
        for detune in (-0.006, 0.0, 0.006):
            out += (2 * ((x * f * (1 + detune)) % 1.0) - 1) * 0.25
    cutoff = 600 + 4200 * np.exp(-x / 0.12)
    y = np.empty_like(out)
    acc = 0.0
    for i, v in enumerate(out):
        a = math.exp(-math.tau * cutoff[i] / SR)
        acc = (1 - a) * v + a * acc
        y[i] = acc
    return y * env(len(x), 0.004, 0.25)


def slide_down(seconds=0.5):
    x = t(seconds)
    f = 1150 * (300 / 1150) ** (x / seconds)
    return np.sin(np.cumsum(math.tau * f * (1 + 0.015 * np.sin(math.tau * 7 * x)) / SR)) * env(len(x), 0.01, seconds * 0.7)


def wood(freq, seed):
    x = t(0.3)
    knock = (np.sin(math.tau * freq * x) + 0.4 * np.sin(math.tau * freq * 1.43 * x)) * env(len(x), 0.0008, 0.035)
    return knock + highpass(noise(0.3, seed), 2000) * env(len(x), 0.0003, 0.004) * 0.4


def riser(seconds, seed):
    """A rising whoosh: band-passed noise sweeping up, swelling, then cut."""
    x = t(seconds)
    n = noise(seconds, seed)
    out = np.zeros_like(x)
    for k, (lo, hi) in enumerate(((300, 900), (700, 2000), (1600, 4500), (3500, 9000))):
        band = bandpass(n, lo, hi)
        center = (k + .5) / 4
        out += band * np.exp(-((x / seconds - center) / .22) ** 2)
    return out * np.clip(x / seconds, 0, 1) ** 1.5


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
    third = 2 ** (4 / 12)
    fifth = 2 ** (7 / 12)
    with tempfile.TemporaryDirectory() as tmp:
        # The sender's countdown lights (the third is pitched up a fifth at runtime), then the cannon.
        write('slopball_beep', beep(880), tmp)
        write('slopball_pop', serve_pop(1), tmp)
        # Hits on the ball: chosen by launch speed, with pitch and volume following it.
        write('slopball_kick_light', kick(0.2, 2), tmp)
        write('slopball_kick_heavy', kick(1.0, 3), tmp)
        # Bounces: the chain-link walls, the net (and, pitched down, the cargo net) and the floor.
        write('slopball_cage', cage(4), tmp)
        write('slopball_net', net(5), tmp)
        write('slopball_floor', floor_bounce(6), tmp)
        # A return lands: the SLOP! splat, the next note of the combo ladder and the board's cards.
        write('slopball_splat', splat(7), tmp)
        write('slopball_note', pluck(C5, seed=20), tmp)
        write('slopball_flip', flip(30), tmp)
        # Every fifth return: a chord stab and a quick run up the scale.
        penta = [C5 * 2 ** (s / 12) for s in (0, 2, 4, 7, 9, 12, 14, 16)]
        write('slopball_chord', mix(1.4, (0, stab([C5 / 2, C5 * third / 2, C5 * fifth / 2], 0.8), 0.8),
                                    *[(0.12 + i * 0.07, pluck(penta[i + 3], 0.7, seed=40 + i), 0.4) for i in range(5)]), tmp)
        # Passing your best mid-rally: a short fanfare.
        write('slopball_best', mix(2.0, *[(i * 0.09, pluck(f, 1.0, seed=50 + i), 0.6)
                                          for i, f in enumerate([C5, penta[2], penta[3], penta[5], penta[7]])],
                                   (0.45, stab([C5, C5 * third, C5 * fifth], 0.9), 0.5)), tmp)
        # Rally heat steps up (every fifth return): a whoosh rising into a bright stab.
        write('slopball_heat', mix(1.3, (0, riser(.55, 60), .9), (.5, stab([C5, C5 * third, C5 * fifth, C5 * 2], .7), .55),
                                   *[(.52 + i * .05, pluck(f * 2, .6, seed=61 + i), .3) for i, f in enumerate([C5, C5 * third, C5 * fifth])]), tmp)
        # A trick shot (bank, sky ball, net cord, juggle, body bank): a quick sparkling run of bells.
        write('slopball_trick', mix(1.0, *[(i * .055, pluck(f, .7, seed=70 + i), .7 - i * .08)
                                           for i, f in enumerate([penta[4] * 2, penta[5] * 2, penta[6] * 2, penta[7] * 2])]), tmp)
        # A ball lands on your side: a flat splat, a slide whistle and two soft wood knocks, never a buzzer.
        write('slopball_miss', mix(1.6, (0, splat(13, wet=0.4), 1.0), (0.08, slide_down(0.55), 0.35),
                                   (0.7, wood(300, 14), 0.5), (0.86, wood(240, 15), 0.45)), tmp)


if __name__ == '__main__':
    main()
