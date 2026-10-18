#!/usr/bin/env python3
"""Synthesize the Fight Night Pier course clips (FightGuy's course): balloon pops, a fairground
band organ that climbs with every balloon, the ring bell, a crowd that roars for the champ,
and a hit and a break for each bonus target (the car, the piñata, the PUNCH-O-METER, the bag).

Everything is generated here from oscillators and seeded noise, so there is no third-party
source or licence. The clips are tracked in client/Unity/Assets/Resources/Audio/SFX/CoursePier/
with their .meta files (Resources/Courses/fightguy_pier.asset points there); rerun this only to
change a clip, then commit it. Each clip is peak-normalized to -3 dBFS; needs numpy and ffmpeg
with libvorbis. CharacterCoursePresentation pitches course_bell (an organ pipe, C5) up the major
scale, one note per balloon within a section, and plays course_pop under it; each bonus hit
plays course_<target>_hit a semitone higher than the last.

  python3 tools/sfx/pier_course_clips.py
"""
import math
import subprocess
import sys
import tempfile
import wave
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'client/Unity/Assets/Resources/Audio/SFX/CoursePier'
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


def tone(freq, seconds, shape="sine", vibrato=(0.0, 0.0)):
    """An oscillator; freq may be an array (glides). vibrato = (Hz, semitones)."""
    x = t(seconds)
    f = np.broadcast_to(np.asarray(freq, dtype=float), x.shape)
    if vibrato[1]:
        f = f * 2 ** (vibrato[1] / 12 * np.sin(math.tau * vibrato[0] * x))
    phase = np.cumsum(math.tau * f / SR)
    if shape == "square":
        return np.sign(np.sin(phase))
    if shape == "saw":
        return 2.0 * (phase / math.tau - np.floor(phase / math.tau + 0.5))
    return np.sin(phase)


def faded(signal, seconds=0.01):
    """The signal with its last few milliseconds faded out, so a cut tail never clicks."""
    out = np.array(signal, dtype=float)
    n = min(len(out), int(seconds * SR))
    if n:
        out[-n:] *= np.linspace(1, 0, n)
    return out


def mix(seconds, *parts):
    """parts: (start seconds, signal, gain); every part is faded at its cut end."""
    out = np.zeros(int(seconds * SR))
    for start, signal, gain in parts:
        i = int(start * SR)
        j = min(len(out), i + len(signal))
        out[i:j] += faded(signal[:j - i]) * gain
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


# ------------------------------------------------------------------ voices

def pop(seed, size=1.0):
    """A balloon popping: a sharp crack, a rubbery thup and the skin flapping off."""
    x = t(0.35)
    n = noise(0.35, seed)
    crack = highpass(n, 2500) * env(len(x), 0.0002, 0.012) * 1.2
    body = bandpass(n, 400, 3000) * env(len(x), 0.0005, 0.03)
    thup = tone(320 * np.exp(-x / 0.02) + 110 / size, 0.35) * env(len(x), 0.001, 0.05) * 0.9
    flap = bandpass(noise(0.35, seed + 1), 200, 1200) * env(len(x), 0.01, 0.06) * 0.4 * (1 + np.sign(np.sin(math.tau * 38 * x))) / 2
    return crack + body + thup + flap


def organ(freq, seconds, seed=0, tremolo=0.0):
    """A band-organ pipe: a breathy flute stop over a reedy one, a little vibrato."""
    x = t(seconds)
    flute = tone(freq, seconds, vibrato=(6.2, 0.1)) + 0.3 * tone(freq * 2, seconds) + 0.12 * tone(freq * 3, seconds)
    reed = lowpass(tone(freq * 1.002, seconds, "saw", vibrato=(6.2, 0.1)), freq * 4) * 0.35
    chiff = bandpass(noise(seconds, seed), freq, freq * 6) * env(len(x), 0.002, 0.04) * 0.5
    breath = bandpass(noise(seconds, seed + 1), 2000, 7000) * 0.03
    sound = (flute + reed + breath) * adsr(len(x), 0.02, seconds * 0.7, seconds * 0.25) + chiff
    if tremolo:
        sound *= 1 - 0.25 * (0.5 + 0.5 * np.sin(math.tau * tremolo * x))
    return sound


def ring_bell(seconds=1.2, seed=0, freq=1180.0):
    """The ring bell: a bright brass gong struck by a hammer."""
    x = t(seconds)
    partials = ((1.0, 1.0, 0.55), (2.32, 0.6, 0.35), (3.86, 0.4, 0.22), (5.3, 0.25, 0.14), (6.9, 0.12, 0.09))
    out = sum(a * np.sin(math.tau * freq * r * x + k) * np.exp(-x / tau) for k, (r, a, tau) in enumerate(partials))
    strike = highpass(noise(seconds, seed), 2500) * env(len(x), 0.0002, 0.006) * 0.6
    return out * (1 + 0.15 * np.sin(math.tau * 4.5 * x) * np.exp(-x / 0.4)) + strike


def crowd(seconds, seed, swell=0.4, cheer=1.0):
    """A pier crowd: many voices (vowel bands with their own swells), whistles and claps."""
    x = t(seconds)
    rng = np.random.default_rng(seed)
    out = np.zeros_like(x)
    for k in range(10):
        lo = rng.uniform(250, 600)
        voice = bandpass(noise(seconds, seed + 10 + k), lo, lo * rng.uniform(2.5, 4.5))
        wobble = 0.6 + 0.4 * np.sin(math.tau * rng.uniform(0.5, 1.6) * x + rng.uniform(0, math.tau))
        out += voice * wobble
    out *= adsr(len(x), swell, seconds * 0.45, seconds * 0.45)
    claps = np.zeros_like(x)
    for i in rng.integers(0, max(1, len(x) - 800), size=int(seconds * 26 * cheer)):
        claps[i:i + 600] += bandpass(noise(600 / SR, int(i)), 800, 5000)[:600] * np.exp(-np.arange(600) / 90) * rng.uniform(0.3, 1.0)
    whistle = np.zeros_like(x)
    for k in range(int(2 * cheer)):
        f0 = rng.uniform(1700, 2300)
        dur = rng.uniform(0.4, 0.7)
        y = t(dur)
        w = tone(f0 * (1 + 0.25 * np.sin(math.pi * y / dur)), dur) * adsr(len(y), 0.05, dur * 0.6, dur * 0.3)
        start = int(rng.uniform(0.15, 0.6) * len(x))
        whistle[start:start + len(w)] += w[:len(whistle) - start]
    return out * 0.6 + claps * 0.5 * cheer * adsr(len(x), swell * 0.6, seconds * 0.5, seconds * 0.4) + whistle * 0.12


def clank(freqs, seconds, seed, tau=0.25):
    """Struck sheet metal: inharmonic partials with a noisy hit."""
    x = t(seconds)
    rng = np.random.default_rng(seed)
    out = sum(np.sin(math.tau * f * x + rng.uniform(0, math.tau)) * np.exp(-x / (tau * rng.uniform(0.5, 1.2))) for f in freqs)
    hit = bandpass(noise(seconds, seed), 300, 4000) * env(len(x), 0.0005, 0.02)
    return out / len(freqs) + hit


def thump(low, seconds, seed, slap=1.0):
    """A heavy blow: a pitched-down body thump and a leather slap."""
    x = t(seconds)
    body = tone(low * 2.6 * np.exp(-x / 0.03) + low, seconds) * env(len(x), 0.001, 0.12)
    smack = bandpass(noise(seconds, seed), 700, 3500) * env(len(x), 0.0005, 0.02) * slap
    return body * 1.3 + smack


def tinkles(seconds, count, seed, lo=3000, hi=7000, decay=0.12):
    """Glass, candy or coins: many small bright strikes thinning out."""
    x = t(seconds)
    rng = np.random.default_rng(seed)
    out = np.zeros_like(x)
    for k in range(count):
        start = int((rng.random() ** 2) * seconds * 0.8 * SR)
        f = rng.uniform(lo, hi)
        y = t(min(0.25, seconds - start / SR))
        ping = faded(np.sin(math.tau * f * y) * np.exp(-y / (decay * rng.uniform(0.3, 1.0))) * rng.uniform(0.3, 1.0), 0.004)
        out[start:start + len(ping)] += ping
    return out


def rip(seconds, seed):
    """Tearing paper or leather: crackly noise opening up."""
    x = t(seconds)
    n = noise(seconds, seed)
    grain = (np.random.default_rng(seed + 1).random(len(x)) > 0.6).astype(float)
    return bandpass(n * grain, 600, 2500 + 4000 * x / seconds) * adsr(len(x), 0.01, seconds * 0.7, seconds * 0.25)


def boom(seconds, seed, low=55.0):
    x = t(seconds)
    n = noise(seconds, seed)
    body = lowpass(n, 1400 * np.exp(-x / 0.12) + 120) * env(len(x), 0.002, 0.22)
    thud = tone(low * 2.4 * np.exp(-x / 0.07) + low, seconds) * env(len(x), 0.002, 0.25) * 1.3
    roll = lowpass(n, 180) * env(len(x), 0.05, seconds * 0.35) * 2.0
    return body + thud + roll


def car_horn(seconds):
    """A tired two-tone car horn."""
    x = t(seconds)
    horn = tone(415, seconds, "square") + tone(523, seconds, "square") * 0.8
    return bandpass(horn, 300, 2500) * adsr(len(x), 0.01, seconds * 0.8, seconds * 0.15) * (0.85 + 0.15 * np.sin(math.tau * 7 * x))


def cymbal(seconds, seed):
    x = t(seconds)
    return highpass(noise(seconds, seed), 4000) * env(len(x), 0.002, seconds * 0.35)


def shutter(seed):
    """A camera shutter: click-clack."""
    a = highpass(noise(0.02, seed), 2000) * np.exp(-np.arange(int(0.02 * SR)) / 120)
    return mix(0.08, (0, a, 1.0), (0.045, a, 0.7))


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    major = [C5 * 2 ** (s / 12) for s in (0, 2, 4, 5, 7, 9, 11, 12)]
    with tempfile.TemporaryDirectory() as tmp:
        # Every balloon: the pop, then the organ (course_bell) on the section's next note.
        write('course_pop', pop(1), tmp)
        write('course_bell', organ(C5, 0.42, seed=2), tmp)
        # A section's last balloon: an organ flourish up the arpeggio into a held chord.
        flourish = [(i * 0.09, organ(f, 0.16, seed=3 + i), 0.7) for i, f in enumerate((major[0], major[2], major[4], major[7]))]
        chord = sum(organ(f, 1.1, seed=9 + k, tremolo=5.5) for k, f in enumerate((major[0], major[2], major[4], major[7] * 0.5)))
        write('course_chord', mix(1.6, *flourish, (0.38, chord, 0.45)), tmp)
        # Crossing a lit checkpoint: ding ding, the ring bell between rounds.
        write('course_saved', mix(1.5, (0, ring_bell(1.1, 5), 1.0), (0.26, ring_bell(1.2, 6), 0.95)), tmp)
        # Reaching a checkpoint or the ring with balloons left: the organ sags, wah wah wah waaah.
        g = 392.0
        sad = [(0.0, organ(g * 2 ** (2 / 12), 0.34, 21, tremolo=7), 1.0), (0.38, organ(g * 2 ** (1 / 12), 0.34, 22, tremolo=7), 1.0),
               (0.76, organ(g, 0.34, 23, tremolo=7), 1.0), (1.14, organ(g * 2 ** (-1 / 12) * np.exp(-t(1.0) / 3.0), 1.0, 24, tremolo=7), 1.0)]
        write('course_missed', mix(2.3, *sad), tmp)
        # The last balloon: the crowd erupts, the bell rings out and the organ plays him in.
        bells = [(0.05 + i * 0.16, ring_bell(0.9, 30 + i), 0.55) for i in range(5)]
        fanfare = [(0.25 + i * 0.12, organ(f, 0.3 if i < 3 else 1.1, seed=40 + i, tremolo=5), 0.5)
                   for i, f in enumerate((major[0], major[2], major[4], major[7]))]
        write('course_all_clear', mix(3.4, (0, crowd(3.4, 50, swell=0.25), 0.9), *bells, *fanfare), tmp)
        # Into the ring: the bell rings the title fight out, the crowd roars, a cymbal, the air
        # horn and the photographers.
        bells = [(i * 0.13, ring_bell(1.0, 60 + i, freq=1180 * (1.0 if i % 2 == 0 else 1.06)), 0.6) for i in range(8)]
        x = t(1.8)
        horn = bandpass(tone(233, 1.8, "saw") + tone(294, 1.8, "saw") + tone(349, 1.8, "saw"), 300, 3000) * adsr(len(x), 0.03, 1.3, 0.4)
        clicks = [(0.9 + k * 0.23 + 0.07 * math.sin(k * 7.1), shutter(70 + k), 0.35) for k in range(10)]
        write('course_finish', mix(5.0, *bells, (0.15, crowd(4.8, 80, swell=0.35, cheer=1.6), 1.1), (0.05, cymbal(2.5, 81), 0.5),
                                   (0.5, horn, 0.35), *clicks), tmp)

        # Bonus targets: each hit plays a semitone higher than the last; the eighth breaks.
        # The car: dented sheet metal and a cracking windscreen; then it caves in, the glass
        # goes and the horn gets stuck.
        write('course_car_hit', mix(0.6, (0, clank((310, 770, 1290, 2140, 3020), 0.6, 101), 1.0),
                                    (0, thump(70, 0.4, 102, slap=0.5), 0.6), (0.01, tinkles(0.4, 5, 103), 0.25)), tmp)
        write('course_car_break', mix(2.6, (0, boom(1.6, 111, low=48), 1.0), (0, clank((180, 420, 690, 1130, 1870), 1.2, 112, tau=0.5), 0.8),
                                      (0.02, tinkles(1.6, 60, 113), 0.45), (0.5, car_horn(1.8), 0.3)), tmp)
        # The piñata: papery cardboard thwacks; then it tears open and the candy rains.
        write('course_pinata_hit', mix(0.5, (0, thump(110, 0.35, 121, slap=0.4), 0.8),
                                       (0.005, bandpass(noise(0.3, 122), 1500, 6000) * env(len(t(0.3)), 0.002, 0.06), 0.5)), tmp)
        candy = tinkles(1.8, 80, 133, lo=1800, hi=4500, decay=0.05)
        write('course_pinata_break', mix(2.4, (0, thump(90, 0.5, 131), 0.8), (0.02, rip(0.4, 132), 0.7), (0.15, candy, 0.6),
                                         (0.5, organ(major[4], 0.18, 134), 0.35), (0.68, organ(major[7], 0.6, 135), 0.4)), tmp)
        # The PUNCH-O-METER: a thud on the pad and the meter's bleep climbing; then the jackpot
        # bell, the coins and the score going off the top.
        bleep = tone(880, 0.12, "square") * adsr(len(t(0.12)), 0.003, 0.08, 0.03)
        write('course_punch_hit', mix(0.5, (0, thump(80, 0.4, 141), 1.0), (0.06, lowpass(bleep, 3000), 0.3)), tmp)
        jackpot = [(i * 0.07, ring_bell(0.5, 150 + i, freq=1500 if i % 2 == 0 else 1900), 0.4) for i in range(16)]
        climb = [(0.1 + i * 0.06, lowpass(tone(440 * 2 ** (i / 4), 0.08, "square"), 3500) * adsr(len(t(0.08)), 0.003, 0.05, 0.02), 0.25)
                 for i in range(12)]
        write('course_punch_break', mix(2.8, (0, thump(70, 0.5, 151), 0.9), *climb, (0.35, tinkles(2.2, 90, 152, 2500, 6000, 0.2), 0.5),
                                        *[(0.3 + s, sig, g) for s, sig, g in jackpot]), tmp)
        # The bag: a heavy leather thump and the chains jingling; then it bursts and pours sand.
        write('course_bag_hit', mix(0.6, (0, thump(58, 0.5, 161, slap=0.8), 1.0), (0.04, tinkles(0.4, 6, 162, 2500, 4500, 0.05), 0.2)), tmp)
        x = t(1.8)
        sand = lowpass(highpass(noise(1.8, 172), 500), 3500) * adsr(len(x), 0.05, 0.9, 0.8)
        write('course_bag_break', mix(2.4, (0, thump(50, 0.7, 171, slap=1.2), 1.0), (0.01, rip(0.35, 173), 0.8), (0.2, sand, 0.45),
                                      (0.25, tinkles(1.0, 14, 174, 2000, 4000, 0.06), 0.3)), tmp)


if __name__ == '__main__':
    main()
