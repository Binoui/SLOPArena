#!/usr/bin/env python3
"""Synthesize Bashie's Hose Down, Shell Slide and NO RUNNING! sounds (no third-party source).

Shell Slide (R) plays bashie_shell_tuck (the dive into her shell), bashie_shell_slide (a loop of water rushing
under the shell while she slides) and bashie_shell_hit (the shell bowling someone over).
Hose Down (A) plays bashie_pump (the rubbery strokes of her toy blaster's pump while she holds),
bashie_spray (the hose's gush on the release), bashie_splash (a drop of the stream splashing on
whoever she hoses) and bashie_sign (the NO SPLASHING! sign popping up: the rule she is breaking).
All of them are low and soft: the owner found the first, brighter set shrill (2026-10-22). See
docs/characters/bashie.md.

Everything is generated here from seeded noise and sine partials, so the clips are tracked in
client/Unity/Assets/Resources/Audio/SFX/Bashie/ with their .meta files; rerun this only to change
a clip, then commit it. Each clip is peak-normalized to -3 dBFS; needs numpy and ffmpeg with
libvorbis.

  python3 tools/sfx/bashie_clips.py [--out /tmp/bashie-preview]
"""
import argparse
import subprocess
import sys
import tempfile
import wave
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'client/Unity/Assets/Resources/Audio/SFX/Bashie'
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


def band(f, centre, width):
    return np.exp(-((f - centre) / width) ** 2)


def place(total, part, at):
    k = int(at * SR)
    total[k:k + len(part)] += part[:max(0, len(total) - k)]


def soft(x, corner=3200.0):
    """Take the edge off: a gentle 4th-order low-pass. The owner found the first set too shrill (2026-10-22)."""
    return spectral(x, lambda f: 1 / (1 + (f / corner) ** 4))


def stroke(seed, up):
    """One pump stroke: a low rubbery squish gliding up (or down), a puff of air, a soft plastic tock at the end."""
    rng = np.random.default_rng(seed)
    time = t(0.24)
    glide = np.clip(time / 0.14, 0, 1)
    f0 = (190 + 90 * glide) if up else (280 - 80 * glide)
    f0 = f0 * (1 + 0.04 * np.sin(2 * np.pi * 26 * time))            # the rubber chatters on the barrel
    phase = 2 * np.pi * np.cumsum(f0) / SR
    squish = sum(np.sin(h * phase) / h ** 1.6 for h in range(1, 5)) * np.exp(-((time - 0.07) / 0.06) ** 2)
    air = spectral(rng.standard_normal(len(time)), lambda f: band(f, 850, 500)) * np.exp(-time / 0.05) * 0.3
    tock = np.zeros_like(time)
    k = int(0.16 * SR)
    tt = t((len(time) - k) / SR)
    tock[k:] = np.sin(2 * np.pi * 520 * tt) * np.exp(-tt / 0.012) * 0.5
    return soft(squish * 0.7 + air + tock, 2400)


def pump():
    """Strokes of the toy pump, about one every 0.2 s for six, as her pump loop does in a full pump."""
    x = np.zeros(int(1.25 * SR))
    for i, at in enumerate((0.0, 0.2, 0.42, 0.62, 0.84, 1.0)):
        place(x, stroke(40 + i, up=i % 2 == 0) * (0.85 if i % 2 else 1.0), at)
    return x


def spray():
    """The hose: a low gush, then a soft rushing spray that sputters a little, then a drip-off."""
    seconds = 0.9
    time = t(seconds)
    rng = np.random.default_rng(5)
    rush = spectral(rng.standard_normal(len(time)), lambda f: band(f, 900, 650) + 0.35 * band(f, 1800, 700))
    sputter = 1 + 0.18 * np.sin(2 * np.pi * 9 * time + 1.3) + 0.08 * np.sin(2 * np.pi * 17 * time)
    gush = spectral(rng.standard_normal(len(time)), lambda f: band(f, 260, 160)) * np.exp(-time / 0.14) * 1.4
    x = rush * sputter * envelope(len(time), 0.03, 0.25) + gush * np.clip(time / 0.006, 0, 1)
    return soft(x, 2600)


def splash():
    """One splash: a low slap of water, droplets falling back, two little low bloops."""
    seconds = 0.38
    time = t(seconds)
    rng = np.random.default_rng(17)
    slap = spectral(rng.standard_normal(len(time)), lambda f: 1 / (1 + (f / 1200) ** 2) - 0.7 / (1 + (f / 150) ** 2))
    slap *= np.exp(-time / 0.05) * np.clip(time / 0.003, 0, 1)
    drops = spectral(rng.standard_normal(len(time)), lambda f: band(f, 1700, 700)) * np.exp(-((time - 0.12) / 0.08) ** 2) * 0.2
    x = slap + drops
    for at, f0, gain in ((0.07, 420, 0.35), (0.15, 560, 0.25), (0.22, 340, 0.2)):
        bt = t(0.07)
        pitch = f0 * (1 + 0.5 * bt / 0.07)                              # a bubble's rising bloop
        bloop = np.sin(2 * np.pi * np.cumsum(pitch) / SR) * np.exp(-bt / 0.02) * gain
        place(x, bloop, at)
    return soft(x, 2200)


def sign():
    """The sign popping up: a soft cartoon pop and the woody tock of the board, low and short."""
    seconds = 0.3
    time = t(seconds)
    rng = np.random.default_rng(29)
    pitch = 240 + 260 * np.exp(-time / 0.02)                           # the pop falls as it opens
    pop = np.sin(2 * np.pi * np.cumsum(pitch) / SR) * np.exp(-time / 0.05) * np.clip(time / 0.002, 0, 1)
    tock = sum(a * np.sin(2 * np.pi * f * time) * np.exp(-time / tau)
               for f, tau, a in ((330, 0.05, 0.5), (690, 0.025, 0.25)))
    air = spectral(rng.standard_normal(len(time)), lambda f: band(f, 900, 500)) * np.exp(-time / 0.03) * 0.15
    return soft(pop + np.concatenate([np.zeros(int(0.035 * SR)), tock[:len(time) - int(0.035 * SR)]]) + air, 2000)


def shell_tuck():
    """Shell Slide: she dives into her shell. A soft low fwump (the shell closing over her) and a small slap of water."""
    time = t(0.42)
    rng = np.random.default_rng(31)
    pitch = 150 * np.exp(-time / 0.08) + 70                             # the thump falls as the shell settles
    thump = np.sin(2 * np.pi * np.cumsum(pitch) / SR) * np.exp(-time / 0.09) * np.clip(time / 0.004, 0, 1)
    air = spectral(rng.standard_normal(len(time)), lambda f: band(f, 300, 200)) * np.exp(-time / 0.05) * 0.5
    x = thump + air
    place(x, splash()[:int(0.3 * SR)] * 0.45, 0.03)
    return soft(x, 1800)


def shell_slide():
    """Shell Slide: water rushing under her shell while she slides. A seamless 1.2 s loop: a low rush with a slow swell."""
    seconds = 1.2
    n = int(seconds * SR)
    rng = np.random.default_rng(37)
    noise = rng.standard_normal(n)
    rush = spectral(noise, lambda f: band(f, 520, 380) + 0.5 * band(f, 180, 120))   # circular: the FFT filter wraps
    k = np.arange(n) / n
    swell = 1 + 0.22 * np.sin(2 * np.pi * 3 * k) + 0.1 * np.sin(2 * np.pi * 7 * k + 1.0)   # whole cycles: loops clean
    return soft(rush * swell, 1600)


def shell_hit():
    """Shell Slide: the shell bowls someone over. A hollow wooden knock on the shell and a splash."""
    time = t(0.5)
    knock = sum(a * np.sin(2 * np.pi * f * time) * np.exp(-time / tau)
                for f, tau, a in ((165, 0.09, 1.0), (330, 0.05, 0.45), (510, 0.03, 0.2)))
    x = knock * np.clip(time / 0.002, 0, 1)
    place(x, splash() * 0.7, 0.02)
    return soft(x, 2000)


def whistle_inhale():
    """A long, soft breath into a balloon belly, with a low rubber creak."""
    time = t(0.8)
    rng = np.random.default_rng(51)
    breath = spectral(rng.standard_normal(len(time)), lambda f: band(f, 650, 420))
    creak = np.sin(2 * np.pi * np.cumsum(110 + 65 * time / 0.8) / SR) * 0.07
    return soft((breath + creak) * envelope(len(time), 0.22, 0.08), 1800)


def whistle_tweet():
    """A warm low pea-whistle trill, with no piercing high partials."""
    time = t(0.42)
    rng = np.random.default_rng(53)
    pitch = 720 + 28 * np.sin(2 * np.pi * 24 * time) + 45 * np.exp(-time / 0.04)
    phase = 2 * np.pi * np.cumsum(pitch) / SR
    trill = (np.sin(phase) + 0.16 * np.sin(2 * phase)) * (0.82 + 0.18 * np.sin(2 * np.pi * 24 * time))
    breath = spectral(rng.standard_normal(len(time)), lambda f: band(f, 900, 450)) * 0.08
    return soft((trill + breath) * envelope(len(time), 0.025, 0.16), 1900)


def whistle_deflate():
    """A short descending balloon squeal, rounded and below 2 kHz."""
    time = t(0.3)
    rng = np.random.default_rng(59)
    pitch = (470 - 280 * time / 0.3) * (1 + 0.09 * np.sin(2 * np.pi * 32 * time))
    phase = 2 * np.pi * np.cumsum(pitch) / SR
    rubber = np.sin(phase) + 0.18 * np.sin(2 * phase) + 0.06 * np.sin(3 * phase)
    air = spectral(rng.standard_normal(len(time)), lambda f: band(f, 600, 350)) * 0.22
    return soft((rubber + air) * envelope(len(time), 0.012, 0.12), 1600)


CLIPS = {'bashie_pump': pump, 'bashie_spray': spray, 'bashie_splash': splash, 'bashie_sign': sign,
         'bashie_shell_tuck': shell_tuck, 'bashie_shell_slide': shell_slide, 'bashie_shell_hit': shell_hit,
         'bashie_whistle_inhale': whistle_inhale, 'bashie_whistle_tweet': whistle_tweet,
         'bashie_whistle_deflate': whistle_deflate}


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
    parser.add_argument('--out', type=Path, default=OUT, help='output folder (default: the tracked Bashie SFX folder)')
    args = parser.parse_args(argv)
    args.out.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory() as tmp:
        for name, make in CLIPS.items():
            write(args.out, name, make(), tmp)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
