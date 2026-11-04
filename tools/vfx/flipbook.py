#!/usr/bin/env python3
"""Turn a video clip of an effect on black into a toon-graded flipbook sheet.

    python3 tools/vfx/flipbook.py timeline CLIP.mp4 --out timeline.jpg
    python3 tools/vfx/flipbook.py sheet CLIP.mp4 OUT.png --grid 8 8 --cell 256 --frames 6:118:2
    python3 tools/vfx/flipbook.py grade OUT.png GRADED.png --ramp "0:0e3a76;.3:2895e6;.7:86dcff;.9:f4fdff"
    python3 tools/vfx/flipbook.py preview GRADED.png preview.mp4 --fps 30 --bg 2a3140

`sheet` keys the black background into straight alpha, crops every frame with one shared box
(no jitter), masks each cell so its border is transparent and packs a grid with a sidecar
`.json` (grid, cell size, source frames, measured bottom inset). `grade` recolours by density
(alpha x luminance) into a few hard steps, dark rim -> body -> light -> core, the same bands
ToonShapes bakes, so a clip reads like the rest of the roster. Requires numpy, Pillow and ffmpeg.
Idea from kanaworksai-hue/effekseer-vfx-skill (MIT); this is a separate implementation.
"""
from __future__ import annotations

import argparse
import json
import subprocess
import tempfile
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter


def decode(clip: Path, folder: Path, fps: float | None = None) -> list[Path]:
    rate = ['-vf', f'fps={fps}'] if fps else []
    subprocess.run(['ffmpeg', '-v', 'error', '-i', str(clip), *rate, str(folder / 'f%04d.png')], check=True)
    return sorted(folder.glob('f*.png'))


def pick(frames: list[Path], spec: str | None) -> list[int]:
    if not spec:
        return list(range(len(frames)))
    start, end, *step = (spec.split(':') + [''])[:3]
    return list(range(int(start or 0), min(int(end or len(frames)), len(frames)), int(step[0] or 1) if step else 1))


def smoothstep(a: float, b: float, x: np.ndarray) -> np.ndarray:
    t = np.clip((x - a) / max(b - a, 1e-6), 0, 1)
    return t * t * (3 - 2 * t)


def key(rgb: np.ndarray, floor: float, gain: float) -> np.ndarray:
    """Straight-alpha RGBA from an effect on black: alpha from brightness, colour unpremultiplied."""
    value = rgb.max(axis=2)
    alpha = np.clip(smoothstep(floor, 1.0, value) * gain, 0, 1)
    colour = np.where(alpha[..., None] > 1e-3, np.clip(rgb / np.maximum(value[..., None], 1e-3), 0, 1), 0)
    return np.dstack([colour, alpha])


def timeline(args) -> None:
    with tempfile.TemporaryDirectory() as tmp:
        frames = decode(args.clip, Path(tmp))
        chosen = frames[::args.step]
        thumb_w = 1600 // args.columns
        first = Image.open(chosen[0])
        thumb_h = round(first.height * thumb_w / first.width)
        rows = (len(chosen) + args.columns - 1) // args.columns
        sheet = Image.new('RGB', (args.columns * thumb_w, rows * thumb_h), (40, 40, 40))
        draw = ImageDraw.Draw(sheet)
        for i, path in enumerate(chosen):
            x, y = (i % args.columns) * thumb_w, (i // args.columns) * thumb_h
            sheet.paste(Image.open(path).convert('RGB').resize((thumb_w, thumb_h)), (x, y))
            draw.text((x + 4, y + 2), f'{i * args.step}', fill=(255, 220, 0))
        sheet.save(args.out, quality=88)
    print(json.dumps({'frames': len(frames), 'timeline': str(args.out)}))


def sheet(args) -> None:
    cols, rows = args.grid
    cell_w, cell_h = args.cell, args.cell_h or args.cell
    with tempfile.TemporaryDirectory() as tmp:
        frames = decode(args.clip, Path(tmp))
        indices = pick(frames, args.frames)
        if len(indices) > cols * rows:
            raise SystemExit(f'{len(indices)} frames do not fit a {cols}x{rows} grid; raise the step or the grid')
        keyed = [key(np.asarray(Image.open(frames[i]).convert('RGB'), np.float32) / 255, args.floor, args.gain) for i in indices]
    h, w = keyed[0].shape[:2]
    if args.box:
        cx, cy, half = (float(v) for v in args.box.split(','))
        half_w, half_h = half, half * cell_h / cell_w
    else:  # one box over every frame, so the effect never jitters between cells
        union = np.zeros((h, w), bool)
        for frame in keyed:
            union |= frame[..., 3] > 0.04
        ys, xs = np.nonzero(union)
        if not len(xs):
            raise SystemExit('the clip has no visible effect above --floor')
        cx, cy = (xs.min() + xs.max()) / 2, (ys.min() + ys.max()) / 2
        half_w, half_h = (xs.max() - xs.min()) / 2 * args.pad, (ys.max() - ys.min()) / 2 * args.pad
        aspect = cell_w / cell_h
        half_w, half_h = max(half_w, half_h * aspect), max(half_h, half_w / aspect)
    box = (round(cx - half_w), round(cy - half_h), round(cx + half_w), round(cy + half_h))
    # rounded-box mask (power 2 = ellipse, higher = squarer) plus a fully transparent gutter
    v, u = np.mgrid[0:cell_h, 0:cell_w].astype(np.float32)
    nx, ny = (u + 0.5) / cell_w * 2 - 1, (v + 0.5) / cell_h * 2 - 1
    radius = (np.abs(nx) ** args.mask_power + np.abs(ny) ** args.mask_power) ** (1 / args.mask_power)
    mask = 1 - smoothstep(1 - args.feather, 1.0, radius)
    g = args.gutter
    mask[:g], mask[-g:], mask[:, :g], mask[:, -g:] = 0, 0, 0, 0
    out = np.zeros((rows * cell_h, cols * cell_w, 4), np.float32)
    bottom = cell_h
    for n, frame in enumerate(keyed):
        cell = np.asarray(Image.fromarray((frame * 255).astype(np.uint8), 'RGBA').crop(box)
                          .resize((cell_w, cell_h), Image.Resampling.LANCZOS), np.float32) / 255
        cell[..., 3] *= mask
        rows_lit = np.nonzero(cell[..., 3].max(axis=1) > 0.04)[0]
        if len(rows_lit):
            bottom = min(bottom, cell_h - 1 - rows_lit.max())
        r, c = divmod(n, cols)
        out[r * cell_h:(r + 1) * cell_h, c * cell_w:(c + 1) * cell_w] = cell
    Image.fromarray((out * 255).round().astype(np.uint8), 'RGBA').save(args.out)
    meta = {'source': str(args.clip), 'grid': [cols, rows], 'cell': [cell_w, cell_h], 'frames': len(keyed),
            'sourceFrames': indices, 'box': box, 'bottomInset': bottom / cell_h,
            'params': {k: getattr(args, k) for k in ('floor', 'gain', 'pad', 'mask_power', 'feather', 'gutter')}}
    Path(args.out).with_suffix('.json').write_text(json.dumps(meta, indent=2) + '\n')
    print(json.dumps({'sheet': str(args.out), 'frames': len(keyed), 'box': box}))


def parse_ramp(spec: str) -> list[tuple[float, np.ndarray]]:
    stops = []
    for part in spec.split(';'):
        pos, hexcolour = part.split(':')
        stops.append((float(pos), np.array([int(hexcolour[i:i + 2], 16) for i in (0, 2, 4)], np.float32) / 255))
    return sorted(stops, key=lambda s: s[0])


def grade(args) -> None:
    src = Image.open(args.sheet).convert('RGBA')
    meta_path = Path(args.sheet).with_suffix('.json')
    meta = json.loads(meta_path.read_text()) if meta_path.exists() else {}
    cols, rows = meta.get('grid', args.grid)
    frames = meta.get('frames', cols * rows)
    px = np.asarray(src, np.float32) / 255
    rgb, alpha = px[..., :3], px[..., 3]
    luminance = rgb @ np.array([0.299, 0.587, 0.114], np.float32)
    # alpha holds the clip's brightness and colour is unpremultiplied, so density = alpha x hue luminance
    lit = alpha > 0.1
    top = max(float(np.percentile(alpha[lit] * luminance[lit], 98)), 1e-3) if lit.any() else 1.0
    density = np.clip(alpha * luminance / top, 0, 1)
    if args.blur:
        density = np.asarray(Image.fromarray((density * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(args.blur)), np.float32) / 255
    stops = parse_ramp(args.ramp)
    colour = np.broadcast_to(stops[0][1], rgb.shape).copy()
    for pos, value in stops[1:]:  # hard cel steps, anti-aliased over `soft`
        colour += (value - colour) * smoothstep(pos - args.soft, pos + args.soft, density)[..., None]
    # erode the haze keying leaves, so edges are crisp
    a = np.clip((alpha - args.erode) / max(1 - args.erode, 1e-3), 0, 1) ** args.erode_pow
    a = smoothstep(0.5 - args.edge, 0.5 + args.edge, a) if args.edge else a
    if args.rim:  # a dark edge on black keys away, so paint the rim stop back onto the outline
        solid = Image.fromarray(((a > 0.5) * 255).astype(np.uint8))
        inner = np.asarray(solid.filter(ImageFilter.MinFilter(2 * args.rim + 1)).filter(ImageFilter.GaussianBlur(0.7)), np.float32) / 255
        colour += (stops[0][1] - colour) * (1 - inner)[..., None]
    cell_w, cell_h = src.width // cols, src.height // rows
    for n in range(frames):  # fade the last cells to nothing so the loop never pops
        left = frames - 1 - n
        if left < args.tail_fade:
            r, c = divmod(n, cols)
            a[r * cell_h:(r + 1) * cell_h, c * cell_w:(c + 1) * cell_w] *= left / args.tail_fade
    out = np.dstack([colour, a])
    Image.fromarray((out * 255).round().astype(np.uint8), 'RGBA').save(args.out)
    meta.update({'graded': {'from': str(args.sheet), 'ramp': args.ramp, 'soft': args.soft, 'erode': args.erode,
                            'erodePow': args.erode_pow, 'edge': args.edge, 'blur': args.blur, 'rim': args.rim, 'tailFade': args.tail_fade}})
    Path(args.out).with_suffix('.json').write_text(json.dumps(meta, indent=2) + '\n')
    print(json.dumps({'graded': str(args.out)}))


def preview(args) -> None:
    src = Image.open(args.sheet).convert('RGBA')
    meta_path = Path(args.sheet).with_suffix('.json')
    meta = json.loads(meta_path.read_text()) if meta_path.exists() else {}
    cols, rows = meta.get('grid', args.grid)
    frames = meta.get('frames', cols * rows)
    cell_w, cell_h = src.width // cols, src.height // rows
    bg = tuple(int(args.bg[i:i + 2], 16) for i in (0, 2, 4))
    with tempfile.TemporaryDirectory() as tmp:
        for n in range(frames + args.hold):
            r, c = divmod(min(n, frames - 1), cols)
            cell = src.crop((c * cell_w, r * cell_h, (c + 1) * cell_w, (r + 1) * cell_h))
            if n >= frames:
                cell = Image.new('RGBA', cell.size, (0, 0, 0, 0))
            canvas = Image.new('RGBA', (cell_w * args.scale, cell_h * args.scale), bg + (255,))
            canvas.alpha_composite(cell.resize(canvas.size, Image.Resampling.LANCZOS))
            canvas.convert('RGB').save(Path(tmp) / f'p{n:04d}.png')
        subprocess.run(['ffmpeg', '-v', 'error', '-y', '-framerate', str(args.fps), '-i', str(Path(tmp) / 'p%04d.png'),
                        '-vf', 'pad=ceil(iw/2)*2:ceil(ih/2)*2', '-pix_fmt', 'yuv420p', '-c:v', 'libx264', '-crf', '18',
                        str(args.out)], check=True)
    print(json.dumps({'preview': str(args.out)}))


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest='command', required=True)
    t = sub.add_parser('timeline', help='labelled frames of a clip, to pick the span')
    t.add_argument('clip', type=Path)
    t.add_argument('--out', type=Path, required=True)
    t.add_argument('--step', type=int, default=4)
    t.add_argument('--columns', type=int, default=8)
    t.set_defaults(run=timeline)
    s = sub.add_parser('sheet', help='key, crop, mask and pack a clip into a flipbook sheet')
    s.add_argument('clip', type=Path)
    s.add_argument('out', type=Path)
    s.add_argument('--grid', type=int, nargs=2, default=[8, 8])
    s.add_argument('--cell', type=int, default=256)
    s.add_argument('--cell-h', type=int, default=0)
    s.add_argument('--frames', help='start:end:step in source frames')
    s.add_argument('--box', help='cx,cy,half-width in source pixels, instead of the automatic shared box')
    s.add_argument('--pad', type=float, default=1.06)
    s.add_argument('--floor', type=float, default=0.06, help='brightness that keys to zero alpha')
    s.add_argument('--gain', type=float, default=1.2)
    s.add_argument('--mask-power', type=float, default=3.0)
    s.add_argument('--feather', type=float, default=0.12)
    s.add_argument('--gutter', type=int, default=4)
    s.set_defaults(run=sheet)
    g = sub.add_parser('grade', help='recolour a sheet into hard toon steps')
    g.add_argument('sheet', type=Path)
    g.add_argument('out', type=Path)
    g.add_argument('--ramp', required=True, help='pos:rrggbb;... from rim (low density) to core')
    g.add_argument('--grid', type=int, nargs=2, default=[8, 8])
    g.add_argument('--soft', type=float, default=0.015)
    g.add_argument('--erode', type=float, default=0.15)
    g.add_argument('--erode-pow', type=float, default=1.5)
    g.add_argument('--edge', type=float, default=0.0, help='harden the alpha edge (0 keeps it soft, 0.1 is crisp)')
    g.add_argument('--blur', type=float, default=0.0, help='smooth density before the steps, in pixels')
    g.add_argument('--tail-fade', type=int, default=6)
    g.add_argument('--rim', type=int, default=0, help='dark rim width in pixels, in the first ramp colour')
    g.set_defaults(run=grade)
    p = sub.add_parser('preview', help='play a sheet over a flat colour as an mp4')
    p.add_argument('sheet', type=Path)
    p.add_argument('out', type=Path)
    p.add_argument('--grid', type=int, nargs=2, default=[8, 8])
    p.add_argument('--fps', type=float, default=30)
    p.add_argument('--bg', default='2a3140')
    p.add_argument('--scale', type=int, default=2)
    p.add_argument('--hold', type=int, default=8)
    p.set_defaults(run=preview)
    args = parser.parse_args()
    args.run(args)


if __name__ == '__main__':
    main()
