#!/usr/bin/env python3
"""Cut a generated music track into a seamless stage-theme loop.

UISFX loops the whole clip, and Lyria takes usually open with a short intro and end in a fade
or a few seconds of silence. This tracks the beats and keeps the stretch between a start beat
after the intro and an end beat before the outro, whole four-bar phrases apart, picked where
the bars around the two sound most alike. It aligns the end to the start to the sample,
crossfades the seam and evens the loudness across stages. Writes client/Unity/Assets/Resources/Audio/Music/Stages/<stage key>.ogg, which
UISFX plays on that stage, plus a .meta on the first write. Those clips stay local like the
rest of Resources/Audio (see docs/assets/credits-and-licenses.md). Needs numpy and ffmpeg with
libvorbis.

  python3 tools/music/loop_stage_music.py SOURCE STAGE_KEY [--loop START END] [--preview DIR]

The tool prints the loop it kept; --loop START END (seconds in the source) skips the search,
for example to keep a different section, and still aligns the end to the start. --preview
writes <stage key>_seam.wav: the last and first 6 s of the loop, which is what plays each time
it wraps.
"""
import argparse
import re
import subprocess
import sys
import uuid
import wave
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'client/Unity/Assets/Resources/Audio/Music/Stages'
SR = 44100
FFT = 2048
HOP = 512
FPS = SR / HOP
TARGET_LUFS = -13.5  # the old random match pool averaged -13.1 LUFS; MainTheme is -12.6
PEAK = 0.98
FADE = int(0.04 * SR)  # seam crossfade
LEAD = int(0.01 * SR)  # cut just ahead of the start beat so its attack plays whole
BAR_MULTIPLE = 16  # tracked beats per loop step: a four-bar phrase in 4/4 (two if the tracker runs double time)
SEARCH = 30.0  # seconds after the intro and before the outro where the seam may sit

# Compressed In Memory: no decompress stall on Play and about 5 MB per loaded theme.
META = """fileFormatVersion: 2
guid: {guid}
AudioImporter:
  externalObjects: {{}}
  serializedVersion: 8
  defaultSettings:
    serializedVersion: 2
    loadType: 1
    sampleRateSetting: 0
    sampleRateOverride: 44100
    compressionFormat: 1
    quality: 1
    conversionMode: 0
    preloadAudioData: 0
  platformSettingOverrides: {{}}
  forceToMono: 0
  normalize: 1
  loadInBackground: 0
  ambisonic: 0
  3D: 1
  userData:
  assetBundleName:
  assetBundleVariant:
"""
FOLDER_META = """fileFormatVersion: 2
guid: {guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def decode(path):
    raw = subprocess.run(['ffmpeg', '-v', 'error', '-i', str(path), '-ac', '2', '-ar', str(SR),
                          '-f', 'f32le', '-'], check=True, capture_output=True).stdout
    return np.frombuffer(raw, dtype=np.float32).reshape(-1, 2)


def spectral_features(mono):
    """Per STFT frame (frame t centred on sample t * HOP): 40 log band energies and a 12-bin chroma."""
    padded = np.pad(mono, FFT // 2)
    frames = np.lib.stride_tricks.sliding_window_view(padded, FFT)[::HOP]
    window = np.hanning(FFT).astype(np.float32)
    freqs = np.fft.rfftfreq(FFT, 1 / SR)
    edges = np.geomspace(30, 16000, 41)
    bands = np.zeros((len(freqs), 40), np.float32)
    for b in range(40):
        bands[(freqs >= edges[b]) & (freqs < edges[b + 1]), b] = 1
    pitched = (freqs >= 110) & (freqs <= 5000)
    chroma_map = np.zeros((len(freqs), 12), np.float32)
    classes = (np.round(12 * np.log2(freqs[pitched] / 440)).astype(int) + 9) % 12
    chroma_map[np.where(pitched)[0], classes] = 1
    band_db, chroma = [], []
    for start in range(0, len(frames), 2048):
        mag = np.abs(np.fft.rfft(frames[start:start + 2048] * window, axis=1)).astype(np.float32)
        band_db.append(10 * np.log10(mag ** 2 @ bands + 1e-10))
        chroma.append(mag @ chroma_map)
    return np.concatenate(band_db), np.concatenate(chroma)


def moving_average(x, n):
    kernel = np.ones(n) / n
    return np.convolve(np.pad(x, (n // 2, n - 1 - n // 2), mode='edge'), kernel, mode='valid')


def onset_strength(band_db):
    flux = np.maximum(np.diff(band_db, axis=0, prepend=band_db[:1]), 0).mean(axis=1)
    flux = np.maximum(flux - moving_average(flux, int(0.4 * FPS)), 0)
    return flux / (flux.std() + 1e-9)


def estimate_bpm(onset):
    """Autocorrelation at the beat lag and its multiples, under a broad prior around 105 BPM."""
    o = onset - onset.mean()
    spectrum = np.fft.rfft(o, 2 * len(o))
    ac = np.fft.irfft(np.abs(spectrum) ** 2)[:len(o)]
    ac /= np.arange(len(o), 0, -1)
    bpms = np.arange(60, 200, 0.02)
    lags = 60 * FPS / bpms
    score = sum(np.interp(m * lags, np.arange(len(ac)), ac) for m in (1, 2, 4, 8, 16)) / 5
    prior = np.exp(-0.5 * np.log2(bpms / 105) ** 2)
    return float(bpms[np.argmax(score * prior)])


def track_beats(onset, period, tightness=100.0):
    """Dynamic-programming beat tracker (Ellis 2007): beat frames that follow onsets at about one period."""
    offsets = np.arange(-int(round(2 * period)), -int(round(period / 2)) + 1)
    penalty = -tightness * np.log(-offsets / period) ** 2
    score = onset.astype(np.float64).copy()
    back = np.full(len(onset), -1)
    for t in range(-offsets[-1], len(onset)):
        prev = t + offsets
        valid = prev >= 0
        candidates = score[prev[valid]] + penalty[valid]
        best = int(np.argmax(candidates))
        score[t] = onset[t] + candidates[best]
        back[t] = prev[valid][best]
    t = len(onset) - int(period) + int(np.argmax(score[-int(period):]))
    beats = [t]
    while back[beats[-1]] >= 0:
        beats.append(back[beats[-1]])
    return np.array(beats[::-1])


def beat_features(beats, band_db, chroma, onset):
    """One row per beat: spectral balance, harmony (weighted up: a chord jump is the loudest seam) and
    the strongest onset in each eighth of the beat."""
    rows = []
    for a, b in zip(beats[:-1], beats[1:]):
        c = chroma[a:b].mean(axis=0)
        edges = np.linspace(a, b, 9).astype(int)
        pattern = [onset[lo:max(hi, lo + 1)].max() for lo, hi in zip(edges[:-1], edges[1:])]
        rows.append(np.concatenate([band_db[a:b].mean(axis=0), c / (np.linalg.norm(c) + 1e-9), pattern]))
    f = np.array(rows)
    f = (f - f.mean(axis=0)) / (f.std(axis=0) + 1e-9)
    f[:, :40] *= 0.6
    f[:, 40:52] *= 1.2
    return f / (np.linalg.norm(f, axis=1, keepdims=True) + 1e-9)


def downbeat_phase(beats, band_db, chroma):
    """Which of every four tracked beats starts the bar: the one where the kick and the chord change land."""
    low = np.maximum(np.diff(band_db[:, :6], axis=0, prepend=band_db[:1, :6]), 0).mean(axis=1)
    kick = np.array([low[max(b - 2, 0):b + 3].max() for b in beats[:-1]])
    c = np.array([chroma[a:b].mean(axis=0) for a, b in zip(beats[:-1], beats[1:])])
    c /= np.linalg.norm(c, axis=1, keepdims=True) + 1e-9
    change = np.zeros(len(c))
    for k in range(4, len(c) - 4):
        before, after = c[k - 4:k].mean(axis=0), c[k:k + 4].mean(axis=0)
        change[k] = 1 - before @ after / (np.linalg.norm(before) * np.linalg.norm(after) + 1e-9)
    z = (kick - kick.mean()) / (kick.std() + 1e-9) + (change - change.mean()) / (change.std() + 1e-9)
    return int(np.argmax([z[p::4].mean() for p in range(4)]))


def groove_span(mono, duration):
    """Seconds from the end of the last quiet second in the opening quarter (the intro) to the start
    of the quiet run that closes the track (its fade or silence). A late breakdown stays in."""
    window = SR
    n = len(mono) // window
    rms = np.sqrt(np.mean(mono[:n * window].reshape(n, window) ** 2, axis=1))
    level = 20 * np.log10(np.maximum(rms, 1e-9))
    quiet = level < np.percentile(level, 90) - 10
    head = [i for i in range(n) if quiet[i] and i < 0.25 * n]
    tail = n
    while tail > 0.75 * n and quiet[tail - 1]:
        tail -= 1
    return (head[-1] + 1 if head else 0.0), float(min(tail, duration))


def find_loop(stereo, verbose=False):
    """Pick a start beat just after the intro and an end beat just before the outro, whole bars apart."""
    mono = stereo.mean(axis=1)
    duration = len(mono) / SR
    band_db, chroma = spectral_features(mono)
    onset = onset_strength(band_db)
    bpm = estimate_bpm(onset)
    beats = track_beats(onset, 60 * FPS / bpm)
    times = beats * HOP / SR
    features = beat_features(beats, band_db, chroma, onset)
    similarity = features @ features.T
    first, last = groove_span(mono, duration)
    n = len(features)
    near, wide = 4, 8  # beats compared either side of the seam: one bar, then two
    window = np.arange(-wide, wide)
    phase = downbeat_phase(beats, band_db, chroma)
    starts = [i for i in range(wide, n - wide) if times[i - wide] >= first and times[i] <= first + SEARCH]
    ends = [j for j in range(wide, n - wide) if times[j + wide] <= last and times[j] >= last - SEARCH]
    candidates = []
    for i in starts:
        for j in ends:
            if (j - i) % BAR_MULTIPLE:
                continue
            diag = similarity[i + window, j + window]
            candidates.append((0.5 * diag[wide - near:wide + near].mean() + 0.5 * diag.mean(), i, j))
    if not candidates:
        raise SystemExit('No bar-aligned loop fits inside the groove; pass --loop START END.')
    beat = 60 / bpm
    ranked = []
    best = max(candidates)[0]
    for score, i, j in sorted(candidates, reverse=True)[:40]:
        if score < best - 0.1:
            break
        start = int(round(times[i] * SR)) - LEAD
        end, fit = align_end(mono, start, int(round(times[j] * SR)) - LEAD, beat)
        # Bars that match and kick and bass that line up, then as much of the track as possible; a
        # seam on the downbeat hides better when the two sides differ, and the loop starts on beat one.
        bonus = 0.05 if (i - phase) % 4 == 0 else 0.0
        ranked.append((score + 0.25 * fit + 0.004 * (end - start) / SR + bonus, score, fit, i, j, start, end))
    ranked.sort(reverse=True)
    if verbose:
        print(f'  groove {first:.1f}-{last:.1f} s, {len(beats)} beats')
        for total, score, fit, i, j, _, _ in ranked[:5]:
            print(f'  {times[i]:7.2f}-{times[j]:7.2f} s  {j - i:3d} beats  beat {(i - phase) % 4 + 1} of the bar  '
                  f'bars {score:.3f}  kick/bass fit {fit:.2f}')
    _, score, fit, i, j, start, end = ranked[0]
    return bpm, start, end, (f'{j - i} beats from beat {(i - phase) % 4 + 1} of the bar, bar match {score:.2f}, '
                             f'kick/bass fit {fit:.2f}')


def lowpass(x, cutoff=300):
    spectrum = np.fft.rfft(x)
    spectrum[np.fft.rfftfreq(len(x), 1 / SR) > cutoff] = 0
    return np.fft.irfft(spectrum, len(x))


def align_end(mono, start, end, beat):
    """Move the end within +-40 ms so the kick and bass after it line up with those after the start."""
    reach = int(0.04 * SR)
    length = int(2 * beat * SR)
    ref = lowpass(mono[start - reach:start + length + reach])[reach:-reach]
    seg = lowpass(mono[end - 2 * reach:end + length + 2 * reach])[reach:-reach]
    size = 2 * len(seg)
    corr = np.fft.irfft(np.fft.rfft(seg, size) * np.conj(np.fft.rfft(ref, size)), size)[:2 * reach + 1]
    energy = np.convolve(seg ** 2, np.ones(length), mode='valid')[:2 * reach + 1]
    corr /= np.sqrt(energy * np.dot(ref, ref)) + 1e-9
    shift = int(np.argmax(corr))
    return end - reach + shift, float(corr[shift])


def build_loop(stereo, start, end):
    loop = stereo[start:end].copy()
    fade = (0.5 - 0.5 * np.cos(np.linspace(0, np.pi, FADE)))[:, None].astype(np.float32)
    loop[-FADE:] = stereo[end - FADE:end] * (1 - fade) + stereo[start - FADE:start] * fade
    return loop


def loudness(stereo):
    result = subprocess.run(['ffmpeg', '-hide_banner', '-nostats', '-f', 'f32le', '-ar', str(SR), '-ac', '2',
                             '-i', '-', '-af', 'ebur128=framelog=quiet', '-f', 'null', '-'],
                            input=stereo.astype(np.float32).tobytes(), capture_output=True, check=True)
    values = re.findall(r'I:\s+(-?[\d.]+) LUFS', result.stderr.decode())
    return float(values[-1])


def encode(stereo, path):
    subprocess.run(['ffmpeg', '-v', 'error', '-y', '-f', 'f32le', '-ar', str(SR), '-ac', '2', '-i', '-',
                    '-c:a', 'libvorbis', '-q:a', '8', str(path)],
                   input=stereo.astype(np.float32).tobytes(), check=True)


def write_wav(stereo, path):
    pcm = (np.clip(stereo, -1, 1) * 32767).astype('<i2')
    with wave.open(str(path), 'wb') as out:
        out.setnchannels(2)
        out.setsampwidth(2)
        out.setframerate(SR)
        out.writeframes(pcm.tobytes())


def main():
    parser = argparse.ArgumentParser(description=__doc__.split('\n\n')[0])
    parser.add_argument('source', type=Path)
    parser.add_argument('stage', help='stage key, e.g. slop_pit or fightguy_pier')
    parser.add_argument('--loop', nargs=2, type=float, metavar=('START', 'END'),
                        help='keep this stretch (seconds in the source) instead of searching')
    parser.add_argument('--preview', type=Path, help='directory for <stage>_seam.wav')
    parser.add_argument('--verbose', action='store_true', help='print the best seam candidates')
    parser.add_argument('--out', type=Path, default=OUT, help='output directory (default: the game\'s Stages folder)')
    args = parser.parse_args()
    if not re.fullmatch(r'[a-z0-9_]+', args.stage):
        parser.error('stage keys are lower-case letters, digits and underscores')

    stereo = decode(args.source)
    mono = stereo.mean(axis=1)
    if args.loop:
        if not 0.5 <= args.loop[0] < args.loop[1] - 10 <= len(mono) / SR - 15:
            parser.error('--loop needs START >= 0.5 s, at least 10 s of loop and 5 s of source after END')
        band_db, _ = spectral_features(mono)
        bpm = estimate_bpm(onset_strength(band_db))
        start = int(round(args.loop[0] * SR)) - LEAD
        end, fit = align_end(mono, start, int(round(args.loop[1] * SR)) - LEAD, 60 / bpm)
        note = f'given loop, kick/bass fit {fit:.2f}'
    else:
        bpm, start, end, note = find_loop(stereo, args.verbose)
    loop = build_loop(stereo, start, end)
    gain = 10 ** ((TARGET_LUFS - loudness(loop)) / 20)
    gain = min(gain, PEAK / float(np.abs(loop).max()))
    loop *= gain

    args.out.mkdir(parents=True, exist_ok=True)
    folder_meta = args.out.with_name(args.out.name + '.meta')
    if args.out == OUT and not folder_meta.exists():
        folder_meta.write_text(FOLDER_META.format(guid=uuid.uuid4().hex))
    target = args.out / f'{args.stage}.ogg'
    encode(loop, target)
    meta = target.with_name(target.name + '.meta')
    if args.out == OUT and not meta.exists():
        meta.write_text(META.format(guid=uuid.uuid4().hex))
    if args.preview:
        args.preview.mkdir(parents=True, exist_ok=True)
        write_wav(np.concatenate([loop[-6 * SR:], loop[:6 * SR]]), args.preview / f'{args.stage}_seam.wav')
    print(f'{args.stage}: {bpm:.1f} BPM, kept {start / SR:.2f}-{end / SR:.2f} s of {len(mono) / SR:.1f} s '
          f'({(end - start) / SR:.1f} s; {note}), gain {20 * np.log10(gain):+.1f} dB -> {target}')
    print(f'  rerun: --loop {(start + LEAD) / SR:.3f} {(end + LEAD) / SR:.3f}')


if __name__ == '__main__':
    sys.exit(main())
