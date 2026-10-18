#!/usr/bin/env python3
"""Rebuild Bonk's presentation clips from Kenney CC0 packs and generated noise.

The clips are tracked in client/Unity/Assets/Resources/Audio/SFX/Bonk/ with their .meta files
(Bonk's prefabs reference them by GUID); rerun this only to change a clip, then commit it. His
swings and hit impacts come from tools/sfx/bonk_foley_clips.py instead. Download Kenney's "Impact Sounds" and "Sci-Fi Sounds" (CC0,
https://kenney.nl/assets) and unpack each so --kenney holds impact/Audio and scifi/Audio, as
for tools/sfx/manki_clips.py. Every clip is peak-normalized to -3 dBFS; needs ffmpeg with
libvorbis. Provenance: docs/assets/credits-and-licenses.md.

  python3 tools/sfx/bonk_clips.py --kenney /tmp/kenney-dl
"""
import argparse
import re
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SR = 44100
IMPACT = SCIFI = OUT = TMP = None


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
    """Resample-based pitch shift (also changes length): lower for a bigger, duller object."""
    return f'[{label}]asetrate={int(SR * factor)},aresample={SR}[{out}]'


def main(argv=None):
    global IMPACT, SCIFI, OUT, TMP
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('--kenney', required=True, type=Path, help='folder holding impact/ and scifi/ packs')
    parser.add_argument('--out', type=Path, default=ROOT / 'client/Unity/Assets/Resources/Audio/SFX/Bonk')
    args = parser.parse_args(argv)
    IMPACT, SCIFI = args.kenney / 'impact/Audio', args.kenney / 'scifi/Audio'
    for pack in (IMPACT, SCIFI):
        if not pack.is_dir():
            sys.exit(f'missing Kenney pack folder {pack}')
    OUT = args.out
    OUT.mkdir(parents=True, exist_ok=True)
    TMP = Path(tempfile.mkdtemp(prefix='bonk-clips-'))

    # Normal contact: a wooden THOCK with a short, low metal ring.
    clip('bonk_thock', [src(IMPACT / 'impactWood_heavy_001.ogg'), src(IMPACT / 'impactMetal_light_003.ogg')],
         f'[0]volume=1.0[w];{pitch(1, 0.75, "m")};[m]volume=0.35,afade=t=out:st=0.12:d=0.25[mr];'
         '[w][mr]amix=inputs=2:normalize=0[out]', 0.45)

    # The BONK: a tin bucket "bonk" over a wooden thud and a punch's low end.
    clip('bonk_bonk', [src(IMPACT / 'impactTin_medium_002.ogg'), src(IMPACT / 'impactWood_heavy_003.ogg'),
                       src(IMPACT / 'impactPunch_heavy_001.ogg')],
         f'{pitch(0, 0.85, "t")};[t]volume=1.0[tv];[1]volume=0.8[w];[2]lowpass=f=1500,volume=0.6[p];'
         '[tv][w][p]amix=inputs=3:normalize=0,afade=t=out:st=0.4:d=0.25[out]', 0.65)

    # His bucket ringing like a bell after a headbutt or a finisher: low, long and wobbling.
    clip('bonk_doong', [src(IMPACT / 'impactBell_heavy_002.ogg')],
         f'{pitch(0, 0.7, "b")};[b]tremolo=f=6:d=0.35,afade=t=out:st=0.6:d=0.9[out]', 1.5)

    # The bull snort before an armoured charge: "hmph-hmph", two breathy bursts through a nasal band.
    clip('bonk_snort', [noise('pink', 0.5, 21), noise('pink', 0.5, 22)],
         '[0]bandpass=f=650:t=h:w=500,afade=t=in:d=0.015,afade=t=out:st=0.04:d=0.08,volume=4[a];'
         '[1]bandpass=f=560:t=h:w=450,afade=t=in:d=0.015,afade=t=out:st=0.05:d=0.12,volume=4,adelay=150|150[b];'
         '[a][b]amix=inputs=2:normalize=0[out]', 0.4)

    # Charge start: a heavy foot plant.
    clip('bonk_stomp', [src(IMPACT / 'footstep_concrete_002.ogg'), src(IMPACT / 'impactSoft_heavy_000.ogg')],
         f'{pitch(0, 0.75, "f")};[f]volume=1.0[fv];{pitch(1, 0.8, "s")};[s]volume=0.8[sv];'
         '[fv][sv]amix=inputs=2:normalize=0[out]', 0.4)

    # Plank swings: a low "whoomp" of air; heavies are lower and longer.
    clip('bonk_swing', [noise('pink', 0.5, 23)],
         '[0]bandpass=f=500:t=h:w=600,afade=t=in:d=0.12,afade=t=out:st=0.14:d=0.16,volume=3.5[out]', 0.32)
    clip('bonk_swing_heavy', [noise('brown', 0.6, 24), noise('pink', 0.6, 25)],
         '[0]lowpass=f=500,afade=t=in:d=0.16,afade=t=out:st=0.2:d=0.22,volume=4[a];'
         '[1]bandpass=f=380:t=h:w=400,afade=t=in:d=0.14,afade=t=out:st=0.18:d=0.2,volume=2.5[b];'
         '[a][b]amix=inputs=2:normalize=0[out]', 0.45)

    # Jump Slam takeoff: a stomp and an upward whoosh.
    clip('bonk_hup', [src(IMPACT / 'footstep_concrete_004.ogg'), noise('pink', 0.6, 26)],
         f'{pitch(0, 0.7, "f")};[f]volume=0.9[fv];'
         '[1]bandpass=f=900:t=h:w=1200,afade=t=in:d=0.08,afade=t=out:st=0.15:d=0.3,volume=2.2,adelay=30|30[w];'
         '[fv][w]amix=inputs=2:normalize=0[out]', 0.55)

    # Jump Slam landing: THOOM, a thud and rubble rattling down.
    clip('bonk_thoom', [src(SCIFI / 'lowFrequency_explosion_000.ogg'), src(IMPACT / 'impactSoft_heavy_002.ogg'),
                        src(IMPACT / 'impactMining_001.ogg'), src(IMPACT / 'impactMining_003.ogg')],
         f'[0]atrim=0:1.0,volume=0.9[l];[1]volume=0.9[s];{pitch(2, 0.9, "r1")};[r1]adelay=90|90,volume=0.5[r1d];'
         f'{pitch(3, 1.1, "r2")};[r2]adelay=260|260,volume=0.35[r2d];'
         '[l][s][r1d][r2d]amix=inputs=4:normalize=0,afade=t=out:st=0.8:d=0.35[out]', 1.15)

    # Blade Storm: "whum-whum-whum" as the plank goes round.
    clip('bonk_storm', [noise('pink', 0.9, 27), noise('brown', 0.9, 28)],
         '[0]bandpass=f=420:t=h:w=450,tremolo=f=6.5:d=0.85,volume=3[a];[1]lowpass=f=300,tremolo=f=6.5:d=0.7,volume=2.5[b];'
         '[a][b]amix=inputs=2:normalize=0,afade=t=in:d=0.08,afade=t=out:st=0.55:d=0.25[out]', 0.8)

    # Armour shrugs off a hit: a small metal tink, then a rising "bwip?" for the question mark.
    clip('bonk_tink', [src(IMPACT / 'impactMetal_light_001.ogg'),
                       ['-f', 'lavfi', '-i', f'aevalsrc=0.5*sin(2*PI*(520*t+1500*t*t)):s={SR}:d=0.16']],
         f'{pitch(0, 1.25, "k")};[k]volume=0.8[kv];[1]afade=t=in:d=0.01,afade=t=out:st=0.08:d=0.08,adelay=160|160[b];'
         '[kv][b]amix=inputs=2:normalize=0[out]', 0.45)

    # A planted foot's soft scuff on normals.
    clip('bonk_scuff', [src(IMPACT / 'footstep_concrete_001.ogg')],
         f'{pitch(0, 0.8, "f")};[f]volume=0.8,afade=t=out:st=0.15:d=0.15[out]', 0.3)


if __name__ == '__main__':
    main()
