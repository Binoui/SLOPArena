#!/usr/bin/env python3
"""Synthesize the After Hours Bass Drop clips: the DJ's countdown kicks, the riser and the drop.

Everything is generated here from sine partials and seeded noise, so there is no third-party
source or licence. Writes client/Unity/Assets/Resources/Audio/SFX/BassDrop/*.ogg; rerun this only
to change a clip. Each clip is peak-normalized to -3 dBFS (BassDropPresentation mixes them under
the fight sounds); needs numpy and ffmpeg with libvorbis.

The hazard runs at 120 BPM: the DJ counts 4, 3, 2, 1 on the beats (0.5 s apart) and the floor drops
on the next downbeat. bass_drop_kick_4 .. _1 grow in weight and pitch toward the drop, the riser
starts on "1" and ends on the drop, and bass_drop_hit is the drop itself. A recorded "DROP!" shout
would be bass_drop_voice; that slot is empty on purpose.

  python3 tools/sfx/bass_drop_clips.py [--sequence out.wav]

--sequence also writes one countdown-and-drop sequence as a WAV, on the beat, for review.
"""
import math
import subprocess
import sys
import tempfile
import wave
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
from slopball_clips import SR, bandpass, env, glide, highpass, lowpass, mix, noise, room, t  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'client/Unity/Assets/Resources/Audio/SFX/BassDrop'
BEAT = 0.5
# The countdown's synth blips climb a minor triad and its fifth toward the drop (A minor).
NOTES = {4: 220.0, 3: 261.63, 2: 329.63, 1: 440.0}


def blip(freq, seconds=0.16):
    """A short detuned saw-and-square stab, the DJ's cue light as a sound."""
    x = t(seconds)
    out = np.zeros_like(x)
    for detune in (-0.007, 0.0, 0.007):
        out += (2 * ((x * freq * (1 + detune)) % 1.0) - 1) * 0.3
    out += np.sign(np.sin(math.tau * freq * 0.5 * x)) * 0.2
    return lowpass(out, 2600) * env(len(x), 0.003, seconds * 0.35)


def kick(count, seed):
    """A club kick that gets heavier and higher as the count falls toward the drop."""
    w = (4 - count) / 3
    s = 0.42
    n = int(s * SR)
    body = glide(150 + 50 * w, 46 + 8 * w, s, 0.04) * env(n, 0.001, 0.15 + 0.08 * w)
    click = highpass(noise(s, seed), 2500) * env(n, 0.0003, 0.003) * (0.25 + 0.15 * w)
    snap = bandpass(noise(s, seed + 1), 900, 5000) * env(n, 0.001, 0.03) * 0.35 * w
    out = np.tanh((body + click + snap) * (1.4 + 0.8 * w))
    out = mix(s, (0, out, 1.0), (0.0, blip(NOTES[count]), 0.32 + 0.12 * w))
    return room(out, 0.12, seed)


def riser(seconds, seed):
    """Into the drop: a noise sweep and a pitch climb, gated on sixteenths, cut dead at the end."""
    x = t(seconds)
    sweep = np.zeros_like(x)
    base = noise(seconds, seed)
    for k, (lo, hi) in enumerate(((400, 1200), (1000, 3000), (2400, 7000), (5000, 12000))):
        center = (k + 0.5) / 4
        sweep += bandpass(base, lo, hi) * np.exp(-((x / seconds - center) / 0.25) ** 2)
    f = 220 * (1760 / 220) ** (x / seconds) ** 1.3
    tone = np.sin(np.cumsum(math.tau * f / SR)) * 0.35
    gate = 0.55 + 0.45 * (np.sin(math.tau * 8 * x) > 0)        # sixteenths at 120 BPM
    out = (sweep * 0.9 + tone) * gate * np.clip(x / seconds, 0, 1) ** 1.6
    fade = int(0.006 * SR)
    out[-fade:] *= np.linspace(1, 0, fade)
    return out


def drop(seed):
    """The drop: a sub-bass boom with a kick on top, a short wobble and a crash that thins out."""
    s = 1.5
    n = int(s * SR)
    x = t(s)
    sub = glide(78, 38, s, 0.22) * env(n, 0.003, 0.55)
    wobble = 0.6 + 0.4 * np.cos(math.tau * 4 * x)                     # a quarter-note wub
    sub = np.tanh(sub * 2.2) * np.where(x > 0.25, wobble, 1.0)
    punch = glide(190, 50, s, 0.03) * env(n, 0.001, 0.09)
    thump = lowpass(lowpass(noise(s, seed), 320), 320) * env(n, 0.002, 0.12) * 3.0
    crash = highpass(noise(s, seed + 1), 4000) * env(n, 0.002, 0.28) * 0.18
    zap = glide(1400, 90, s, 0.05) * env(n, 0.001, 0.08) * 0.25
    return room(sub * 1.0 + punch * 0.8 + thump + crash + zap, 0.18, seed)


def normalize(x):
    x = x / (np.max(np.abs(x)) + 1e-9) * 10 ** (-3 / 20)
    fade = min(len(x), int(0.02 * SR))
    x[-fade:] *= np.linspace(1, 0, fade)
    return x


def write_wav(path, x):
    with wave.open(str(path), 'wb') as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes((np.clip(x, -1, 1) * 32767).astype(np.int16).tobytes())


def write(name, x, tmp):
    x = normalize(x)
    raw = Path(tmp) / f'{name}.wav'
    write_wav(raw, x)
    result = subprocess.run(['ffmpeg', '-hide_banner', '-loglevel', 'error', '-y', '-i', str(raw),
                             '-c:a', 'libvorbis', '-q:a', '6', str(OUT / f'{name}.ogg')], capture_output=True, text=True)
    if result.returncode:
        sys.exit(result.stderr)
    print(f'{name}: {len(x) / SR:.2f}s')
    return x


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    clips = {}
    with tempfile.TemporaryDirectory() as tmp:
        for count in (4, 3, 2, 1):
            clips[f'kick_{count}'] = write(f'bass_drop_kick_{count}', kick(count, 10 + count), tmp)
        clips['riser'] = write('bass_drop_riser', riser(BEAT, 20), tmp)
        clips['hit'] = write('bass_drop_hit', drop(30), tmp)
    if '--sequence' in sys.argv:
        # The same levels BassDropPresentation plays them at: kicks 0.42..0.57, riser 0.32, drop 0.62.
        path = Path(sys.argv[sys.argv.index('--sequence') + 1])
        parts = [(0.5 + i * BEAT, clips[f'kick_{4 - i}'], 0.42 + 0.05 * i) for i in range(4)]
        parts += [(0.5 + 3 * BEAT, clips['riser'], 0.32), (0.5 + 4 * BEAT, clips['hit'], 0.62)]
        write_wav(path, mix(4.6, *parts))
        print(f'sequence: {path}')


if __name__ == '__main__':
    main()
