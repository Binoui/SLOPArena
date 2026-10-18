#!/usr/bin/env python3
"""Rebuild Manki's presentation clips from Kenney CC0 packs and generated noise.

The clips are tracked in client/Unity/Assets/Resources/Audio/SFX/Manki/ with their .meta files
(Manki's prefabs reference them by GUID); rerun this only to change a clip, then commit it.
Download Kenney's "Sci-Fi Sounds", "Impact Sounds" and
"RPG Audio" (CC0, https://kenney.nl/assets) and unpack each so --kenney holds scifi/Audio,
impact/Audio and rpg/Audio. Every clip is peak-normalized to -3 dBFS; needs ffmpeg with
libvorbis. Provenance: docs/assets/credits-and-licenses.md.

  python3 tools/sfx/manki_clips.py --kenney /tmp/kenney-dl
"""
import argparse
import re
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SR = 44100
SCIFI = IMPACT = RPG = OUT = TMP = None


def run(args):
    result = subprocess.run(['ffmpeg', '-hide_banner', '-loglevel', 'error', '-y', *args], capture_output=True, text=True)
    if result.returncode:
        sys.exit(result.stderr)


def peak(path):
    result = subprocess.run(['ffmpeg', '-hide_banner', '-i', str(path), '-af', 'volumedetect', '-f', 'null', '-'],
                            capture_output=True, text=True)
    return float(re.search(r'max_volume: (-?[\d.]+) dB', result.stderr).group(1))


def clip(name, inputs, graph, duration):
    """inputs: list of ffmpeg input arg lists; graph ends in [out]."""
    raw = TMP / f'{name}.wav'
    args = []
    for item in inputs:
        args += item
    run([*args, '-filter_complex', graph + f';[out]atrim=0:{duration},aresample={SR},aformat=channel_layouts=mono[o]',
         '-map', '[o]', str(raw)])
    gain = -3.0 - peak(raw)
    run(['-i', str(raw), '-af', f'volume={gain:.2f}dB', '-c:a', 'libvorbis', '-q:a', '6', str(OUT / f'{name}.ogg')])
    print(f'{name}: {duration}s, gain {gain:+.1f} dB')


def src(path):
    return ['-i', str(path)]


def noise(color, seconds, seed=1):
    return ['-f', 'lavfi', '-i', f'anoisesrc=color={color}:duration={seconds}:sample_rate={SR}:seed={seed}']


def pitch(label, factor, out):
    """Resample-based pitch shift (also shortens), for metal tinks and clicks."""
    return f'[{label}]asetrate={int(SR * factor)},aresample={SR}[{out}]'


def main(argv=None):
    global SCIFI, IMPACT, RPG, OUT, TMP
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('--kenney', required=True, type=Path, help='folder holding scifi/, impact/ and rpg/ packs')
    parser.add_argument('--out', type=Path, default=ROOT / 'client/Unity/Assets/Resources/Audio/SFX/Manki')
    args = parser.parse_args(argv)
    SCIFI, IMPACT, RPG = args.kenney / 'scifi/Audio', args.kenney / 'impact/Audio', args.kenney / 'rpg/Audio'
    for pack in (SCIFI, IMPACT, RPG):
        if not pack.is_dir():
            sys.exit(f'missing Kenney pack folder {pack}')
    OUT = args.out
    OUT.mkdir(parents=True, exist_ok=True)
    TMP = Path(tempfile.mkdtemp(prefix='manki-clips-'))

    # Fuse: a fizzing sizzle, two noise layers flickering at different rates.
    clip('manki_fuse', [noise('white', 1.0, 3), noise('pink', 1.0, 4)],
         '[0]highpass=f=3000,tremolo=f=23:d=0.8[a];[1]highpass=f=1800,lowpass=f=6000,tremolo=f=37:d=0.9,volume=0.7[b];'
         '[a][b]amix=inputs=2:normalize=0,afade=t=in:d=0.02,afade=t=out:st=0.6:d=0.3[out]', 0.9)

    # Round Bomb: crunch + low boom, then two junk tinks.
    clip('manki_boom', [src(SCIFI / 'explosionCrunch_002.ogg'), src(SCIFI / 'lowFrequency_explosion_001.ogg'),
                        src(IMPACT / 'impactMetal_light_000.ogg'), src(IMPACT / 'impactMetal_light_002.ogg')],
         f'{pitch(2, 1.3, "t1")};{pitch(3, 1.5, "t2")};[t1]adelay=550|550,volume=0.45[t1d];[t2]adelay=820|820,volume=0.35[t2d];'
         '[0]volume=1.0[c];[1]volume=0.9[l];[c][l][t1d][t2d]amix=inputs=4:normalize=0,afade=t=out:st=1.3:d=0.3[out]', 1.6)

    # Bazooka: the biggest boom, more tinks.
    clip('manki_boom_big', [src(SCIFI / 'explosionCrunch_004.ogg'), src(SCIFI / 'lowFrequency_explosion_000.ogg'),
                            src(IMPACT / 'impactMetal_light_000.ogg'), src(IMPACT / 'impactMetal_light_001.ogg'),
                            src(IMPACT / 'impactMetal_light_002.ogg')],
         f'{pitch(2, 1.2, "t1")};{pitch(3, 1.45, "t2")};{pitch(4, 1.7, "t3")};'
         '[t1]adelay=700|700,volume=0.4[a];[t2]adelay=950|950,volume=0.35[b];[t3]adelay=1150|1150,volume=0.3[c];'
         '[0]volume=1.0[x];[1]volume=1.0[y];[x][y][a][b][c]amix=inputs=5:normalize=0,afade=t=out:st=1.6:d=0.4[out]', 2.0)

    # Jetpack ignition: click-click, then a fwoosh with a little crunch.
    clip('manki_jet_ignite', [src(RPG / 'metalClick.ogg'), src(RPG / 'metalClick.ogg'), src(SCIFI / 'thrusterFire_000.ogg'),
                              src(SCIFI / 'explosionCrunch_000.ogg')],
         '[0]atrim=0:0.1,volume=0.6[c1];[1]atrim=0:0.1,asetrate=52000,aresample=44100,adelay=55|55,volume=0.6[c2];'
         '[2]atrim=0:0.9,afade=t=in:d=0.03,afade=t=out:st=0.4:d=0.5,adelay=80|80[w];[3]volume=0.45,adelay=80|80[x];'
         '[c1][c2][w][x]amix=inputs=4:normalize=0[out]', 1.0)

    # Jetpack sputter: putt-putt of low noise bursts over a thin thruster bed.
    clip('manki_jet_sputter', [noise('brown', 1.0, 5), src(SCIFI / 'thrusterFire_002.ogg')],
         '[0]lowpass=f=700,tremolo=f=13:d=1.0,volume=2.2[p];[1]atrim=0:1,volume=0.35[t];'
         '[p][t]amix=inputs=2:normalize=0,afade=t=in:d=0.03,afade=t=out:st=0.55:d=0.35[out]', 0.9)

    # Jetpack cough: two "pff" bursts and the dropped bolt's tink.
    clip('manki_jet_cough', [noise('brown', 0.2, 6), noise('brown', 0.2, 7), src(IMPACT / 'impactMetal_light_001.ogg')],
         '[0]lowpass=f=900,afade=t=in:d=0.01,afade=t=out:st=0.03:d=0.1,volume=3[a];'
         '[1]lowpass=f=700,afade=t=in:d=0.01,afade=t=out:st=0.03:d=0.12,volume=3,adelay=120|120[b];'
         f'{pitch(2, 1.4, "t")};[t]adelay=330|330,volume=0.5[td];[a][b][td]amix=inputs=3:normalize=0[out]', 0.6)

    # Bazooka startup: clunk-click as he shoulders it.
    clip('manki_bazooka_ready', [src(RPG / 'metalLatch.ogg'), src(RPG / 'metalClick.ogg')],
         '[0]volume=1.0[a];[1]atrim=0:0.12,adelay=100|100,volume=0.8[b];[a][b]amix=inputs=2:normalize=0[out]', 0.25)

    # Bazooka shot: a low FWUMP with a crunch and a hiss of exhaust.
    clip('manki_bazooka_fire', [src(SCIFI / 'lowFrequency_explosion_001.ogg'), src(SCIFI / 'explosionCrunch_001.ogg'),
                                noise('white', 0.4, 8)],
         '[0]atrim=0:0.45,afade=t=out:st=0.2:d=0.25[a];[1]atrim=0:0.4,volume=0.5,afade=t=out:st=0.15:d=0.25[b];'
         '[2]highpass=f=2000,afade=t=in:d=0.01,afade=t=out:st=0.05:d=0.25,volume=0.5[c];'
         '[a][b][c]amix=inputs=3:normalize=0[out]', 0.45)

    # Rocket whistle: a falling chirp.
    clip('manki_rocket_whistle', [['-f', 'lavfi', '-i',
                                   f'aevalsrc=0.6*sin(2*PI*(2400*t-1100*t*t)):s={SR}:d=0.75'], noise('white', 0.75, 9)],
         '[1]highpass=f=4000,volume=0.15[n];[0][n]amix=inputs=2:normalize=0,afade=t=in:d=0.05,afade=t=out:st=0.45:d=0.3[out]', 0.75)

    # Spray can rattle (each charge tier): four metal clicks and a short "pssht".
    clip('manki_can_rattle', [src(IMPACT / 'impactMetal_light_002.ogg'), noise('white', 0.2, 10)],
         f'[0]asplit=4[r0][r1][r2][r3];{pitch("r0", 1.8, "k0")};{pitch("r1", 1.9, "k1")};{pitch("r2", 1.75, "k2")};{pitch("r3", 2.0, "k3")};'
         '[k1]adelay=60|60[d1];[k2]adelay=120|120[d2];[k3]adelay=180|180[d3];'
         '[1]highpass=f=3000,afade=t=in:d=0.02,afade=t=out:st=0.05:d=0.12,volume=0.5,adelay=200|200[h];'
         '[k0][d1][d2][d3][h]amix=inputs=5:normalize=0[out]', 0.38)

    # Lighter flick when the charge starts.
    clip('manki_lighter', [src(RPG / 'metalClick.ogg'), noise('pink', 0.2, 11)],
         f'[0]atrim=0:0.12[c0];{pitch("c0", 1.5, "c")};[1]bandpass=f=1500:w=1200,afade=t=in:d=0.02,afade=t=out:st=0.06:d=0.12,volume=0.8,adelay=50|50[f];'
         '[c][f]amix=inputs=2:normalize=0[out]', 0.3)

    # Aerosol Inferno: "pssht" then a roaring FWOOOM for the stream.
    clip('manki_spray_flame', [noise('white', 0.2, 12), noise('brown', 0.8, 13), src(SCIFI / 'thrusterFire_004.ogg')],
         '[0]highpass=f=2500,afade=t=in:d=0.01,afade=t=out:st=0.06:d=0.08,volume=0.7[s];'
         '[1]lowpass=f=700,afade=t=in:d=0.12,afade=t=out:st=0.45:d=0.25,volume=2.5,adelay=60|60[r];'
         '[2]atrim=0:0.7,afade=t=in:d=0.1,afade=t=out:st=0.4:d=0.3,volume=0.6,adelay=60|60[t];'
         '[s][r][t]amix=inputs=3:normalize=0[out]', 0.72)


if __name__ == '__main__':
    main()
