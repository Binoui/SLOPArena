#!/usr/bin/env python3
"""Uniformly stretch an uncompressed text .anim to --ticks at 60 Hz.

Preserves keys, poses, tangent weights and loop settings. Scales key times and
inverse-scales tangents to preserve the interpolated curve, including its seam.
Already matching clips are left byte-identical. Does not call the Unity Editor.
"""
import argparse
import math
from pathlib import Path
import re

NUMBER = r'[-+]?(?:\d*\.\d+|\d+\.?\d*)(?:[eE][-+]?\d+)?'


def stretch(text, ticks):
    if '  m_Compressed: 0\n' not in text:
        raise ValueError('Only uncompressed text AnimationClips are supported.')
    stop = re.search(r'^\s+m_StopTime: (' + NUMBER + r')$', text, re.M)
    if stop is None:
        raise ValueError('Missing clip stop time.')
    duration = float(stop[1])
    if duration <= 0 or ticks <= 0:
        raise ValueError('Clip and target durations must be positive.')
    target = ticks / 60
    if math.isclose(duration, target, abs_tol=1e-7):
        return text
    factor = target / duration
    def scale_line(match):
        prefix, key, value = match.groups()
        multiplier = 1 / factor if key in ('inSlope', 'outSlope') else factor
        value = re.sub(NUMBER, lambda n: format(float(n[0]) * multiplier, '.9g'), value)
        return prefix + key + ': ' + value
    return re.sub(r'^(\s*)(time|m_StartTime|m_StopTime|inSlope|outSlope): (.+)$', scale_line, text, flags=re.M)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('clip', type=Path)
    parser.add_argument('--ticks', type=int, required=True)
    parser.add_argument('--write', action='store_true')
    args = parser.parse_args()
    before = args.clip.read_text()
    after = stretch(before, args.ticks)
    print(f'{args.clip}: {args.ticks} ticks, {"already applied" if before == after else "retimed"}')
    if args.write and before != after:
        temporary = args.clip.with_suffix('.anim.tmp')
        temporary.write_text(after)
        temporary.replace(args.clip)


if __name__ == '__main__':
    main()
