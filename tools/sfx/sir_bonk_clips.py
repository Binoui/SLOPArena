#!/usr/bin/env python3
"""Synthesize Sir Bonk's storybook sounds (no third-party source).

Leap of Faith plays bonk_choir (a heavenly "aah" while the plank is stuck) and bonk_pop (a cork
pop when it comes out); Blade Storm plays bonk_creak (a wooden windmill creak) under its whirl
and, higher, under the tiny windmill afterwards. The Joust plays bonk_neigh (his own neigh at the
rear), bonk_clip and bonk_clop (coconut hooves, one knock per footfall of the 24-tick gallop loop),
bonk_poof (the hobby horse arriving and leaving in a puff of straw), bonk_squeal (his own brakes in
the skid) and bonk_gulp (the look-down on thin air). The Pot-Lid Throw plays bonk_lid_fwip (the release),
bonk_lid_whirr (a loop on the flying lid), bonk_lid_tink (its hit, out and back: the owner preferred a tink to a
DOOONG), bonk_lid_clank (the catch) and bonk_hm (his own puzzled "hm?" when it bonks him). See docs/characters/bonk.md.

Everything is generated here from seeded noise and sine partials, so the clips are tracked in
client/Unity/Assets/Resources/Audio/SFX/Bonk/ with their .meta files; rerun this only to change
a clip, then commit it. Each clip is peak-normalized to -3 dBFS; needs numpy and ffmpeg with
libvorbis.

  python3 tools/sfx/sir_bonk_clips.py [--out /tmp/sir-bonk-preview]
"""
import argparse
import subprocess
import sys
import tempfile
import wave
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'client/Unity/Assets/Resources/Audio/SFX/Bonk'
SR = 44100


def t(seconds):
    return np.arange(int(seconds * SR)) / SR


def envelope(n, attack, release):
    """Smooth rise over `attack` seconds and fall over the last `release` seconds."""
    x = np.arange(n) / SR
    end = n / SR
    rise = np.clip(x / attack, 0, 1)
    fall = np.clip((end - x) / release, 0, 1)
    return (rise * rise * (3 - 2 * rise)) * (fall * fall * (3 - 2 * fall))


def spectral(x, shape):
    """Filter by multiplying the spectrum with shape(frequencies)."""
    spectrum = np.fft.rfft(x)
    return np.fft.irfft(spectrum * shape(np.fft.rfftfreq(len(x), 1 / SR)), len(x))


def choir():
    """A major chord sung on "aah": vibrato voices through the vowel's formants, a breath, a room."""
    seconds = 1.15
    time = t(seconds)
    rng = np.random.default_rng(7)
    voice = np.zeros_like(time)
    for f0, gain in ((220.0, 0.9), (277.18, 0.75), (329.63, 0.75), (440.0, 0.6)):
        vibrato = 1 + 0.006 * np.sin(2 * np.pi * rng.uniform(4.8, 5.6) * time + rng.uniform(0, 6.28))
        phase = 2 * np.pi * np.cumsum(f0 * vibrato) / SR
        voice += gain * sum(np.sin(h * phase) / h ** 1.1 for h in range(1, 25))
    breath = rng.standard_normal(len(time)) * 0.08
    formants = ((800, 90, 1.0), (1150, 110, 0.6), (2900, 200, 0.25), (3300, 250, 0.15))

    def vowel(f):
        return 0.04 + sum(a * np.exp(-((f - c) / w) ** 2) for c, w, a in formants)

    sung = spectral(voice + breath, vowel) * envelope(len(time), 0.12, 0.4)
    room = sung.copy()
    for delay, gain in ((0.045, 0.35), (0.09, 0.2), (0.15, 0.12)):
        k = int(delay * SR)
        room[k:] += gain * sung[:-k]
    return spectral(room, lambda f: 1 / (1 + (f / 6000) ** 2))


def pop():
    """A cork pop: a falling resonant thwop, a click, a low body and a little air."""
    seconds = 0.22
    time = t(seconds)
    rng = np.random.default_rng(11)
    pitch = 320 + 580 * np.exp(-time / 0.012)
    thwop = np.sin(2 * np.pi * np.cumsum(pitch) / SR) * np.exp(-time / 0.025) * np.clip(time / 0.001, 0, 1)
    click = np.diff(rng.standard_normal(len(time) + 1)) * np.exp(-time / 0.0015) * 0.5
    body = np.sin(2 * np.pi * 180 * time) * np.exp(-time / 0.04) * 0.3
    air = spectral(rng.standard_normal(len(time)), lambda f: np.exp(-((f - 3500) / 2500) ** 2)) * np.exp(-time / 0.05) * 0.06
    return thwop + click + body + air


def creak():
    """A wooden windmill creak: stick-slip clicks at a wandering rate, ringing a wooden body."""
    seconds = 0.85
    time = t(seconds)
    rng = np.random.default_rng(23)
    rate = 38 + 22 * np.sin(np.pi * time / seconds) + 4 * np.sin(2 * np.pi * 3.1 * time)
    ticks = np.diff(np.floor(np.cumsum(rate) / SR), prepend=0) > 0
    impulses = ticks * rng.uniform(0.7, 1.0, len(time))
    kernel_time = t(0.04)
    kernel = sum(a * np.sin(2 * np.pi * f * kernel_time) * np.exp(-kernel_time / tau)
                 for f, tau, a in ((620, 0.012, 1.0), (1180, 0.008, 0.6), (2300, 0.004, 0.3)))
    wood = np.convolve(impulses, kernel)[:len(time)]
    return spectral(wood, lambda f: 1 / (1 + (f / 4000) ** 2)) * envelope(len(time), 0.15, 0.25)


def formant(frequencies, bands_):
    """A vowel's spectral envelope: Gaussian bumps (centre, width, gain) over a small floor."""
    return 0.03 + sum(g * np.exp(-((frequencies - c) / w) ** 2) for c, w, g in bands_)


EE = ((300, 90, 0.5), (2300, 190, 1.0), (3050, 260, 0.45))      # "ee"
EH = ((620, 110, 1.0), (1850, 170, 0.7), (2600, 230, 0.3))      # "eh"


def neigh():
    """His own neigh, done with his voice: a squeaky "neeee" falling into a trilled "hee-hee-hee"."""
    seconds = 1.05
    time = t(seconds)
    rng = np.random.default_rng(31)
    trill = 1 + 0.08 * np.sin(2 * np.pi * (7.5 + 4 * time) * time) * np.clip((time - 0.2) / 0.2, 0, 1)
    f0 = (300 + 520 * np.exp(-time / 0.55)) * trill
    phase = 2 * np.pi * np.cumsum(f0) / SR
    voice = sum(np.sin(h * phase) / h ** 1.05 for h in range(1, 28))
    ee = spectral(voice, lambda f: formant(f, EE))
    eh = spectral(voice, lambda f: formant(f, EH))
    morph = np.clip((time - 0.25) / 0.45, 0, 1)
    sung = ee * (1 - morph) + eh * morph
    # "hee-hee-hee": syllable pulses in the second half, each with a breath of "h" before it
    pulse = np.clip(np.sin(2 * np.pi * 6.5 * (time - 0.36)), 0, 1) ** 0.5
    syllables = np.where(time < 0.36, 1.0, 0.35 + 0.65 * pulse)
    breath = spectral(rng.standard_normal(len(time)), lambda f: np.exp(-((f - 2600) / 1600) ** 2))
    aspiration = breath * np.where(time < 0.36, 0.05, 0.22 * (1 - pulse))
    nose = np.clip(time / 0.05, 0, 1)                            # the "n" opens into the vowel
    return (sung * syllables * (0.4 + 0.6 * nose) + aspiration) * envelope(len(time), 0.015, 0.18)


def knock(time, f1, f2, seed):
    """One coconut half knocked down: a click and two hollow shell modes."""
    rng = np.random.default_rng(seed)
    click = rng.standard_normal(len(time)) * np.exp(-time / 0.0012) * 0.4
    shell = np.sin(2 * np.pi * f1 * time) * np.exp(-time / 0.024) + 0.5 * np.sin(2 * np.pi * f2 * time) * np.exp(-time / 0.011)
    return (click + shell) * envelope(len(time), 0.0004, 0.035)   # no click where the knock is cut


def hoof(f1, f2, seed):
    """One coconut hoof: a knock, softened above 7 kHz."""
    return spectral(knock(t(0.09), f1, f2, seed), lambda f: 1 / (1 + (f / 7000) ** 2))


def clip():
    """Coconut hooves: the higher "clip" on one footfall of the gallop loop."""
    return hoof(980, 2250, 1)


def clop():
    """The lower "clop" on the other footfall, 12 ticks later (the cue plays it a little quieter)."""
    return hoof(760, 1760, 2)


def poof():
    """The hobby horse arriving or leaving: a soft low fwump of straw and dust, with a rustle."""
    seconds = 0.4
    time = t(seconds)
    rng = np.random.default_rng(41)
    body = spectral(rng.standard_normal(len(time)), lambda f: 1 / (1 + (f / 450) ** 2)) * np.clip(time / 0.014, 0, 1) * np.exp(-time / 0.09)
    rustle = spectral(rng.standard_normal(len(time)), lambda f: np.exp(-((f - 4200) / 2300) ** 2)) * np.exp(-time / 0.06) * 0.35
    thump = np.sin(2 * np.pi * 85 * time) * np.exp(-time / 0.05) * 0.6
    return body * 1.6 + rustle + thump


def squeal():
    """His own brakes in the skid: a strained "eeeeee" sliding down, a scratch of boots on wood, and a stop."""
    seconds = 0.65
    time = t(seconds)
    rng = np.random.default_rng(53)
    f0 = 760 - 230 * (time / seconds) ** 1.4 + 14 * np.sin(2 * np.pi * 10.5 * time)
    phase = 2 * np.pi * np.cumsum(f0) / SR
    voice = spectral(sum(np.sin(h * phase) / h ** 1.2 for h in range(1, 22)), lambda f: formant(f, EE))
    jitter = 0.55 + 0.45 * np.abs(np.sin(2 * np.pi * 41 * time + rng.uniform(0, 6.28)))
    scratch = spectral(rng.standard_normal(len(time)), lambda f: np.exp(-((f - 3600) / 1500) ** 2)) * jitter * 0.3
    stop = np.clip((seconds - 0.05 - time) / 0.03, 0, 1)
    return (voice + scratch) * envelope(len(time), 0.03, 0.02) * stop


def gulp():
    """The look-down: a throaty "glk", then a smaller second swallow."""
    seconds = 0.32
    out = np.zeros(int(seconds * SR))
    for start, gain, top in ((0.0, 1.0, 420), (0.15, 0.55, 360)):
        time = t(0.12)
        pitch = 150 + (top - 150) * np.exp(-time / 0.028)
        blip = np.sin(2 * np.pi * np.cumsum(pitch) / SR) * np.exp(-time / 0.045) * envelope(len(time), 0.003, 0.05)
        k = int(start * SR)
        out[k:k + len(time)] += gain * blip
    return spectral(out, lambda f: 1 / (1 + (f / 1300) ** 2))


def lid(f0, seconds, partials, wobble, seed, glide=0.0, strike=0.5):
    """A pot lid struck: a few inharmonic plate modes (ratio, decay, gain), each a detuned pair that starts in phase
    (loudest at the strike) and then beats as the lid wobbles, a short strike of noise, and an optional downward
    glide at the start."""
    time = t(seconds)
    rng = np.random.default_rng(seed)
    bend = 1 - glide * (1 - np.exp(-time / 0.25))
    out = np.zeros_like(time)
    for ratio, tau, gain in partials:
        f = f0 * ratio
        start = rng.uniform(0, 6.28)
        for detune in (-wobble, wobble):
            out += gain * np.sin(2 * np.pi * np.cumsum(f * bend + detune) / SR + start) * np.exp(-time / tau)
    hit = spectral(rng.standard_normal(len(time)), lambda fr: np.exp(-((fr - 4200) / 2600) ** 2)) * np.exp(-time / 0.006)
    return (out + strike * hit) * envelope(len(time), 0.0008, 0.12)


def lid_tink():
    """The lid's hit: one bright tin tink."""
    return lid(1450, 0.3, ((1.0, 0.11, 1.0), (2.2, 0.05, 0.4), (3.7, 0.03, 0.2)), 3.0, 9, strike=0.4)


def lid_clank():
    """The catch: the lid snapping back on his shoulder, a short tin clank with a click."""
    x = lid(820, 0.32, ((1.0, 0.09, 1.0), (1.63, 0.06, 0.7), (2.31, 0.04, 0.5), (3.1, 0.03, 0.3)), 6.0, 7, strike=0.9)
    second = lid(1180, 0.32, ((1.0, 0.05, 0.6), (1.7, 0.03, 0.4)), 4.0, 8, strike=0.6)
    k = int(0.045 * SR)
    x[k:] += 0.7 * second[:len(x) - k]
    return x


def lid_fwip():
    """The release: a quick airy fwip, rising."""
    time = t(0.28)
    rng = np.random.default_rng(5)
    centre = 700 + 2600 * (time / 0.28) ** 0.8
    noise = rng.standard_normal(len(time))
    out = np.zeros_like(time)
    hop = 512
    for k in range(0, len(time), hop):          # a band that sweeps up, filtered block by block
        seg = noise[k:k + hop * 2]
        c = centre[min(k, len(time) - 1)]
        out[k:k + len(seg)] += spectral(seg, lambda f: np.exp(-((f - c) / (c * 0.45)) ** 2))[:len(out) - k] * np.hanning(len(seg))
    return out * envelope(len(time), 0.05, 0.12)


def lid_whirr():
    """The lid in flight: an airy flutter at twice its 3 turns a second, with a faint hum. It loops."""
    seconds = 1.0
    time = t(seconds)
    rng = np.random.default_rng(6)
    air = spectral(rng.standard_normal(len(time)), lambda f: np.exp(-((f - 1300) / 700) ** 2))
    flutter = 0.45 + 0.55 * np.abs(np.sin(2 * np.pi * 3 * time))
    x = air * flutter + 0.25 * np.sin(2 * np.pi * 190 * time) * flutter
    fade = int(0.05 * SR)                         # the loop seam: fold the tail into the head
    x[:fade] = x[:fade] * np.linspace(0, 1, fade) + x[-fade:] * np.linspace(1, 0, fade)
    return x[:-fade]


UH = ((640, 120, 1.0), (1150, 160, 0.6), (2450, 260, 0.25))     # "uh"


def hm():
    """The self-hit: his own puzzled "hm?", rising at the end, after a beat for the tink."""
    seconds = 0.42
    time = t(seconds)
    rng = np.random.default_rng(12)
    f0 = 135 + 80 * np.clip((time - 0.18) / 0.2, 0, 1) ** 1.5 + 3 * np.sin(2 * np.pi * 6 * time)
    phase = 2 * np.pi * np.cumsum(f0) / SR
    voice = sum(np.sin(h * phase) / h ** 1.15 for h in range(1, 30))
    closed = spectral(voice, lambda f: 0.004 + np.exp(-((f - 240) / 110) ** 2) / (1 + (f / 400) ** 4))   # the "m"
    opened = spectral(voice, lambda f: formant(f, UH))
    mix = np.clip((time - 0.12) / 0.08, 0, 1)
    breath = spectral(rng.standard_normal(len(time)), lambda f: np.exp(-((f - 1800) / 1200) ** 2)) * 0.02
    x = (closed * (1 - mix) + opened * mix + breath) * envelope(len(time), 0.02, 0.08)
    return np.concatenate([np.zeros(int(0.16 * SR)), x])


CLIPS = {'bonk_choir': choir, 'bonk_pop': pop, 'bonk_creak': creak, 'bonk_neigh': neigh, 'bonk_clip': clip,
         'bonk_clop': clop, 'bonk_poof': poof, 'bonk_squeal': squeal, 'bonk_gulp': gulp, 'bonk_lid_fwip': lid_fwip,
         'bonk_lid_whirr': lid_whirr, 'bonk_lid_tink': lid_tink, 'bonk_lid_clank': lid_clank, 'bonk_hm': hm}


def write(out, name, x, tmp):
    x = x / np.max(np.abs(x)) * 10 ** (-3 / 20)
    raw = Path(tmp) / f'{name}.wav'
    with wave.open(str(raw), 'wb') as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes((x * 32767).astype('<i2').tobytes())
    # bitexact: the Ogg muxer otherwise picks a random stream serial, so a rerun would rewrite every clip
    result = subprocess.run(['ffmpeg', '-hide_banner', '-loglevel', 'error', '-y', '-i', str(raw),
                             '-c:a', 'libvorbis', '-q:a', '6', '-fflags', '+bitexact', '-flags:a', '+bitexact',
                             '-map_metadata', '-1', str(out / f'{name}.ogg')], capture_output=True, text=True)
    if result.returncode:
        sys.exit(result.stderr)
    print(f'{name}: {len(x) / SR:.2f}s')


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--out', type=Path, default=OUT, help='output folder (default: the tracked Bonk SFX folder)')
    args = parser.parse_args(argv)
    args.out.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory() as tmp:
        for name, make in CLIPS.items():
            write(args.out, name, make(), tmp)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
