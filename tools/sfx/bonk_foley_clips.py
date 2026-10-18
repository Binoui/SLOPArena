#!/usr/bin/env python3
"""Synthesize Bonk's plank swings and hit impacts (no third-party source).

PlayerRenderer plays bonk_swoosh_01-07 in turn on every hitbox of Bonk's attacks, CombatSFX
plays impact_light/medium/heavy by tier when Bonk lands a hit, and UISFX uses impact_heavy as
the countdown's GO for every match. They replace local clips of unknown origin (2026-10-18),
keeping their names, lengths and rough spectra: swings are low, heavy whomps of a flat plank
cutting the air; impacts are a wooden thock with a crack, a body thump and a crunchy tail
(heavy adds the bucket's tin ring).

Everything is generated here from seeded noise and sine partials, so the clips are tracked in
client/Unity/Assets/Resources/Audio/SFX/Bonk/ with their .meta files; rerun this only to
change a clip, then commit it. Clips are levelled to the old clips' loudness (RMS), soft-limited
under -2 dBFS; needs numpy and ffmpeg with libvorbis.

  python3 tools/sfx/bonk_foley_clips.py [--out /tmp/bonk-preview]
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
OUT = ROOT / 'client/Unity/Assets/Resources/Audio/SFX/Bonk'
SR = 44100


def t(seconds):
    return np.arange(int(seconds * SR)) / SR


def noise(seconds, seed):
    return np.random.default_rng(seed).standard_normal(len(t(seconds)))


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


def bandpass(x, centre, q):
    """State-variable band-pass; `centre` may be an array (a sweep). Stable below SR / 6."""
    centre = np.broadcast_to(np.asarray(centre, dtype=float), x.shape)
    f = 2 * np.sin(np.pi * np.minimum(centre, SR / 6.5) / SR)
    damp = 1.0 / q
    low = band = 0.0
    y = np.empty_like(x)
    for i, v in enumerate(x):
        low += f[i] * band
        high = v - low - damp * band
        band += f[i] * high
        y[i] = band
    return y


def swell(n, peak, attack_shape=2.0, release=3.0):
    """0 -> 1 at `peak` seconds (eased), then a smooth fall to 0 at the end."""
    x = np.arange(n) / SR
    end = n / SR
    rise = np.clip(x / peak, 0, 1) ** attack_shape
    fall = np.clip(1 - (x - peak) / max(end - peak, 1e-3), 0, 1) ** release
    return np.where(x < peak, rise, fall)


def decay(n, attack, tau):
    x = np.arange(n) / SR
    return np.clip(x / max(attack, 1e-4), 0, 1) * np.exp(-x / tau)


# ------------------------------------------------------------------ swings

def bandpass2(x, centre, q):
    """Two state-variable band-passes in series: 12 dB per octave either side of the centre."""
    return bandpass(bandpass(x, centre, q), centre, q)


def swoosh(seconds, peak, f_start, f_peak, f_end, q, flutter_hz, flutter_depth, body_hz, body_gain, seed):
    """A flat plank whomping through the air: band-passed noise whose centre rises to the
    swing's fastest point and falls away, flutter from the flat face, a low pressure thump."""
    n = len(t(seconds))
    x = np.arange(n) / SR
    rise = np.clip(x / peak, 0, 1)
    fall = np.clip((x - peak) / max(seconds - peak, 1e-3), 0, 1)
    centre = np.where(x < peak, f_start * (f_peak / f_start) ** rise, f_peak * (f_end / f_peak) ** fall)
    env = swell(n, peak, 1.6, 2.2)
    core = lowpass(bandpass2(lowpass(noise(seconds, seed), 2500), centre, q), 2600)
    core /= np.sqrt(np.mean(core ** 2)) + 1e-9
    flutter = 1 - flutter_depth * (0.5 + 0.5 * np.sin(math.tau * flutter_hz * x + seed)) * swell(n, peak, 1.0, 1.0)
    body = np.sin(np.cumsum(math.tau * body_hz * (0.85 + 0.3 * swell(n, peak, 1.0, 1.0)) / SR)) * swell(n, peak * 1.1, 2.0, 3.0)
    return core * flutter * env + body_gain * body


# (seconds, peak, f_start, f_peak, f_end, q, flutter Hz, flutter depth, body Hz, body gain): the
# centres and body follow the spectra of the clips these replace.
SWOOSHES = [
    (0.26, 0.10, 240, 420, 200, 1.4, 26, 0.35, 90, 0.35),     # short, low
    (0.50, 0.09, 700, 1250, 1400, 1.2, 34, 0.25, 0, 0.0),     # long and bright: the big overhead
    (0.48, 0.12, 300, 520, 480, 1.3, 30, 0.30, 85, 0.25),     # long, mid
    (0.34, 0.12, 200, 320, 170, 1.6, 22, 0.45, 72, 1.6),      # heavy, mostly thump
    (0.30, 0.12, 380, 720, 460, 1.2, 28, 0.35, 78, 1.4),      # thump plus air
    (0.30, 0.10, 220, 360, 200, 1.5, 24, 0.40, 88, 0.75),     # dull, low
    (0.27, 0.10, 240, 400, 210, 1.5, 31, 0.30, 96, 0.65),     # quick
]


# ------------------------------------------------------------------ impacts

def thock(seconds, freq, seed, decays=(0.07, 0.045, 0.03, 0.02)):
    """A wooden knock: inharmonic decaying partials with a short pitch drop."""
    x = t(seconds)
    rng = np.random.default_rng(seed)
    out = np.zeros_like(x)
    for k, (ratio, amp) in enumerate(((1.0, 1.0), (2.37, 0.55), (3.91, 0.3), (5.29, 0.16))):
        f = freq * ratio * (1 + 0.06 * np.exp(-x / 0.012))
        out += amp * np.sin(np.cumsum(math.tau * f / SR) + rng.uniform(0, math.tau)) * decay(len(x), 0.0006, decays[k])
    return out


def crack(seconds, seed, low=2200, high=7000, tau=0.006):
    return lowpass(highpass(noise(seconds, seed), low), high) * decay(len(t(seconds)), 0.0003, tau)


def smack(seconds, seed, centre, tau):
    """The flat of the plank on a body: a short mid noise burst."""
    return bandpass2(noise(seconds, seed), centre, 1.0) * decay(len(t(seconds)), 0.0008, tau)


def thump(seconds, f_hi, f_lo, tau):
    x = t(seconds)
    f = f_lo + (f_hi - f_lo) * np.exp(-x / 0.03)
    return np.sin(np.cumsum(math.tau * f / SR)) * decay(len(x), 0.001, tau)


def crunch(seconds, seed, centre, q, swell_at, tau, grain_hz):
    """The splintery tail: band-passed noise broken into grains, swelling then dying away."""
    n = len(t(seconds))
    x = np.arange(n) / SR
    rng = np.random.default_rng(seed)
    grains = np.repeat(rng.uniform(0.2, 1.0, int(seconds * grain_hz) + 2), int(SR / grain_hz) + 1)[:n]
    grains = lowpass(grains, grain_hz * 3)
    shape = np.clip(x / swell_at, 0, 1) ** 0.5 * np.exp(-np.maximum(x - swell_at, 0) / tau)
    band = bandpass2(noise(seconds, seed + 7), centre, q)
    band /= np.sqrt(np.mean(band ** 2)) + 1e-9
    return band * grains * shape


def tin_ring(seconds, freq, seed):
    """The bucket: a short metallic ring with beating partials."""
    x = t(seconds)
    rng = np.random.default_rng(seed)
    out = np.zeros_like(x)
    for k, (ratio, amp) in enumerate(((1.0, 1.0), (2.76, 0.6), (5.40, 0.35), (8.93, 0.15))):
        f = freq * ratio
        phase = rng.uniform(0, math.tau)
        out += amp * 0.5 * (np.sin(math.tau * f * x + phase) + np.sin(math.tau * (f + 3.5 * (k + 1)) * x + phase)) \
            * decay(len(x), 0.001, 0.22 / (1 + 0.8 * k))
    return out


def mix(seconds, *parts):
    """parts: (start seconds, signal, gain)."""
    out = np.zeros(int(seconds * SR))
    for start, signal, gain in parts:
        i = int(start * SR)
        j = min(len(out), i + len(signal))
        out[i:j] += signal[:j - i] * gain
    return out


def impact(tier):
    """A wooden thock with a crack at the contact, then the crunch the old clips were mostly made
    of: bright for light hits, mid and long for heavy ones (heavy adds the bucket's ring)."""
    if tier == 'light':
        hit = mix(0.37, (0, thock(0.25, 760, 11, (0.05, 0.035, 0.025, 0.018)), 0.35), (0, crack(0.06, 12, 2500, 7000, 0.006), 0.6),
                  (0, thump(0.15, 170, 110, 0.03), 0.08), (0.002, crunch(0.36, 13, 4200, 1.0, 0.06, 0.16, 110), 0.24))
        return lowpass(lowpass(hit, 7500), 7500)
    if tier == 'medium':
        hit = mix(0.43, (0, thock(0.3, 540, 21), 0.4), (0, crack(0.06, 22, 2200, 7000, 0.007), 0.45),
                  (0, smack(0.14, 24, 1600, 0.045), 0.7), (0, thump(0.25, 190, 80, 0.06), 0.15),
                  (0.003, crunch(0.42, 23, 2100, 0.8, 0.08, 0.2, 90), 0.24))
        return lowpass(lowpass(hit, 8000), 8000)
    hit = mix(0.5, (0, thock(0.35, 520, 31, (0.09, 0.06, 0.04, 0.025)), 0.35), (0, crack(0.07, 32, 2000, 6500, 0.008), 0.3),
              (0, smack(0.18, 34, 1300, 0.06), 0.85), (0, thump(0.35, 200, 58, 0.11), 0.2),
              (0.004, crunch(0.5, 33, 1700, 0.8, 0.05, 0.26, 70), 0.3), (0.01, tin_ring(0.48, 430, 35), 0.1))
    return lowpass(lowpass(hit, 7000), 7000)


# Loudness of each clip these replace (RMS of the whole clip, measured 2026-10-18), so the
# game's mix does not move.
TARGET_RMS = {'bonk_swoosh_01': 0.175, 'bonk_swoosh_02': 0.123, 'bonk_swoosh_03': 0.119, 'bonk_swoosh_04': 0.194,
              'bonk_swoosh_05': 0.186, 'bonk_swoosh_06': 0.187, 'bonk_swoosh_07': 0.200,
              'impact_light': 0.272, 'impact_medium': 0.127, 'impact_heavy': 0.139}
PEAK_CAP = 10 ** (-2 / 20)  # Vorbis rings over sharp transients: leave headroom


def level(x, target_rms):
    """Scale to the target RMS; soft-limit peaks under PEAK_CAP, gaining back up to 6 dB."""
    x = x - np.mean(x)
    x = x * (target_rms / (np.sqrt(np.mean(x ** 2)) + 1e-9))
    for _ in range(12):
        y = PEAK_CAP * np.tanh(x / PEAK_CAP)
        rms = np.sqrt(np.mean(y ** 2))
        if rms >= target_rms * 0.97:
            return y
        x = x * min(1.12, target_rms / rms)
    return y


def write(out, name, x, target_rms, tmp):
    x = level(x, target_rms)
    fade = min(len(x), int(0.015 * SR))
    x[-fade:] *= np.linspace(1, 0, fade)
    raw = Path(tmp) / f'{name}.wav'
    with wave.open(str(raw), 'wb') as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes((np.clip(x, -1, 1) * 32767).astype(np.int16).tobytes())
    result = subprocess.run(['ffmpeg', '-hide_banner', '-loglevel', 'error', '-y', '-i', str(raw),
                             '-c:a', 'libvorbis', '-q:a', '6', str(out / f'{name}.ogg')], capture_output=True, text=True)
    if result.returncode:
        sys.exit(result.stderr)
    print(f'{name}: {len(x) / SR:.2f}s rms {np.sqrt(np.mean(x ** 2)):.3f} peak {np.max(np.abs(x)):.2f}')


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('--out', type=Path, default=OUT, help='write somewhere else to preview')
    out = parser.parse_args(argv).out
    out.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory() as tmp:
        for i, params in enumerate(SWOOSHES):
            name = f'bonk_swoosh_{i + 1:02d}'
            write(out, name, swoosh(*params, seed=100 + 10 * i), TARGET_RMS[name], tmp)
        for tier in ('light', 'medium', 'heavy'):
            write(out, f'impact_{tier}', impact(tier), TARGET_RMS[f'impact_{tier}'], tmp)


if __name__ == '__main__':
    main()
