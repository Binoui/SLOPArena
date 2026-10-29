"""Where a weapon's hitbox reaches: the arc a held weapon sweeps over a move's active ticks, tested against the game's
own hurtboxes (spheres on the defender's bones, posed in its idle) on a grid of defender positions in front of, above
and behind the attacker. For weapon normals the arc is the hitbox (Sir Bonk's plank is a capsule from its hilt to its
tip), so it decides whether the move is any good (owner, 2026-10-22: "for those moves it's the most important part").

    <genra python> tools/genra/hitarc.py RESULT.json --brief BRIEF.json --radius 0.65 --active 21-40 \
        --report artifacts/hit-reliability/bonk.json [--calibration bonk] [--out arc.png]

The defender's idle contact volumes come from a HitReliabilityReport JSON (`targets`: the game's own hurtboxes). It
stands on the attacker's facing line from 2.5 m behind to 3.5 m ahead with its feet 0-3 m above the floor (the report's
Arc grid); a cell counts when any active tick's weapon capsule overlaps any of its volumes. A brief's `arc` targets
(checks.measure) count those cells.
"""
from __future__ import annotations

import argparse
import functools
import json
from pathlib import Path

import numpy as np

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
# genra character frame (right, up from the floor, front) -> the game's grounded origin frame, per axis (scale,
# offset), fitted on Sir Bonk's Barn Door: the cooked plank's hilt and tip on its 15 active ticks against the
# toolkit's (rms 4-6 cm). The game holds his plank about 0.2 m higher than the rig pack does.
CALIBRATION = {'bonk': ((1.025, -0.129), (1.262, -0.124), (1.024, 0.156))}
TOE_ABOVE_FLOOR = 0.05  # a flat foot's toe joint sits about this far above the floor
# The report's Arc grid: distance along the facing (behind to ahead) and the defender's feet height.
ARC_DISTANCES = np.round(np.arange(-2.5, 3.5 + 1e-9, 0.25), 3)
ARC_HEIGHTS = np.round(np.arange(0.0, 3.0 + 1e-9, 0.25), 3)


def floor_of(pose) -> float:
    """The floor's height (char frame up) under a standing pose: its lower toe joint less TOE_ABOVE_FLOOR."""
    return min(pose.pos(n)[2] for n in ('LeftToeBase', 'RightToeBase')) - TOE_ABOVE_FLOOR


def weapon_segments(clip, ticks, floor: float, start='bladeHilt', end='bladeEnd', calibration=None) -> np.ndarray:
    """(T, 2, 3): the weapon line on each tick (right, front, up from the floor), mapped into the game's frame by a
    CALIBRATION entry when given."""
    seg = np.array([[clip.pose(t).pos(j) for j in (start, end)] for t in ticks])
    seg[..., 2] -= floor
    if calibration:
        (sx, ox), (sy, oy), (sz, oz) = calibration
        seg = seg * np.array([sx, sz, sy]) + np.array([ox, oz, oy])
    return seg


@functools.lru_cache(maxsize=None)
def _report_targets(report: str) -> dict:
    return json.loads(Path(report).read_text())['targets']


def report_spheres(report: Path, target: str) -> np.ndarray:
    """(N, 4) the target's contact volumes (centre right, front, up; radius) from a HitReliabilityReport JSON's
    `targets`: the game's own idle hurtboxes at the origin, facing the attacker (all spheres today)."""
    volumes = _report_targets(str(report))[target]
    return np.array([[v[0], v[2], v[1], v[6]] for v in volumes])


def coverage(segments, radius, spheres, distances, heights, lateral=0.0) -> np.ndarray:
    """(len(heights), len(distances)) booleans: the defender at forward distance d, feet h above the floor, is hit by
    any of the weapon capsules (segments (T, 2, 3) of radius `radius`) in any of its spheres (N, 4)."""
    seg = np.asarray(segments, float)
    a, ab = seg[:, 0], seg[:, 1] - seg[:, 0]
    dd, hh = np.meshgrid(np.asarray(distances, float), np.asarray(heights, float))
    centres = np.stack([np.full(dd.shape, lateral), dd, hh], -1)[:, :, None, :] + spheres[None, None, :, :3]
    points = centres[..., None, :]  # (H, D, N, 1, 3)
    t = np.clip(((points - a) * ab).sum(-1) / np.maximum((ab * ab).sum(-1), 1e-12), 0.0, 1.0)
    gap = np.linalg.norm(points - (a + t[..., None] * ab), axis=-1)  # (H, D, N, T)
    return (gap < (radius + spheres[:, 3])[None, None, :, None]).any(axis=(2, 3))


def arc_grid(clip, window, radius, report, defender='FightGuy', calibration=None, joints=('bladeHilt', 'bladeEnd')) \
        -> np.ndarray:
    """The ARC_HEIGHTS x ARC_DISTANCES grid of defender positions the clip's weapon (or the `joints` line of a limb
    that is the hitbox, such as a kick's foot) touches over `window` [first, last], with the floor under its first
    tick (a move that starts standing; for an aerial, heights are relative to its feet)."""
    first, last = window
    seg = weapon_segments(clip, range(first, last + 1), floor_of(clip.pose(0)), *joints, calibration=calibration)
    return coverage(seg, radius, report_spheres(report, defender), ARC_DISTANCES, ARC_HEIGHTS)


def arc_cells(grid, side='all') -> int:
    """Touched cells of an arc_grid: all of them, or only those ahead of or behind the attacker."""
    cols = {'all': ARC_DISTANCES == ARC_DISTANCES, 'ahead': ARC_DISTANCES > 0, 'behind': ARC_DISTANCES < 0}[side]
    return int(grid[:, cols].sum())


def intervals(row, distances, step) -> list[tuple[float, float]]:
    out, start = [], None
    for d, hit in zip(distances, row):
        if hit and start is None:
            start = d
        if not hit and start is not None:
            out.append((round(start, 2), round(d - step, 2)))
            start = None
    if start is not None:
        out.append((round(start, 2), round(distances[-1], 2)))
    return out


def draw(path, maps, distances, heights, segments_by_label, title):
    """Side view per option: the weapon line on each active tick (light to dark through the window) and the defender
    positions it hits (FightGuy's feet as squares, Manki's as dots)."""
    from PIL import Image, ImageDraw, ImageFont
    font = ImageFont.truetype('/usr/share/fonts/TTF/DejaVuSans.ttf', 15)
    bold = ImageFont.truetype('/usr/share/fonts/TTF/DejaVuSans-Bold.ttf', 17)
    x0, x1, y0, y1 = distances[0] - 0.25, distances[-1] + 0.25, -0.1, heights[-1] + 0.6
    w, h, pad = 440, 360, 36
    cells = []
    for label, (seg, grids) in segments_by_label.items():
        im = Image.new('RGB', (w + 2 * pad, h + 2 * pad + 24), (21, 23, 28))
        g = ImageDraw.Draw(im)
        X = lambda v: pad + (v - x0) / (x1 - x0) * w
        Y = lambda v: pad + 24 + h - (v - y0) / (y1 - y0) * h
        g.line([(X(x0), Y(0)), (X(x1), Y(0))], fill=(90, 90, 96), width=2)
        for m in np.arange(np.ceil(x0), x1, 1.0):
            g.line([(X(m), Y(0) - 4), (X(m), Y(0) + 4)], fill=(90, 90, 96))
            g.text((X(m) - 8, Y(0) + 6), f'{m:+.0f}', font=font, fill=(130, 130, 136))
        g.rectangle([X(-0.3), Y(1.9), X(0.3), Y(0)], outline=(110, 110, 120))  # the attacker, roughly
        for name, colour, marker in (('FightGuy', (90, 190, 120), 'square'), ('Manki', (240, 200, 70), 'dot')):
            grid = grids.get(name)
            if grid is None:
                continue
            for i, hh in enumerate(heights):
                for j, dd in enumerate(distances):
                    if grid[i, j]:
                        cx, cy = X(dd) + (-3 if name == 'FightGuy' else 3), Y(hh)
                        if marker == 'square':
                            g.rectangle([cx - 3, cy - 3, cx + 3, cy + 3], fill=colour)
                        else:
                            g.ellipse([cx - 3, cy - 3, cx + 3, cy + 3], fill=colour)
        n = len(seg)
        for k, (a, b) in enumerate(seg):
            shade = int(120 + 135 * k / max(n - 1, 1))
            g.line([(X(a[1]), Y(a[2])), (X(b[1]), Y(b[2]))], fill=(shade, int(shade * 0.55), 60), width=3)
        g.text((pad, 8), label, font=bold, fill=(235, 231, 223))
        cells.append(im)
    sheet = Image.new('RGB', (sum(c.width for c in cells), cells[0].height + 30), (21, 23, 28))
    x = 0
    for c in cells:
        sheet.paste(c, (x, 30))
        x += c.width
    ImageDraw.Draw(sheet).text((10, 6), title, font=font, fill=(200, 200, 205))
    sheet.save(path)
    return sheet


def main(argv=None):
    from .brief import Brief
    from .clip import Clip
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    parser.add_argument('result')
    parser.add_argument('--brief', required=True)
    parser.add_argument('--radius', type=float, required=True, help="the move's weapon capsule radius (move data)")
    parser.add_argument('--active', required=True, help='first-last active tick (move data)')
    parser.add_argument('--report', required=True, help="a HitReliabilityReport JSON (its `targets` volumes)")
    parser.add_argument('--calibration', help=f"the attacker's rig-to-game calibration ({', '.join(CALIBRATION)})")
    parser.add_argument('--joints', default='bladeHilt,bladeEnd',
                        help='the hitbox line, start,end joints (a kick: RightFoot,RightToeBase)')
    parser.add_argument('--out')
    args = parser.parse_args(argv)
    joints = tuple(args.joints.split(','))
    brief = Brief(Path(args.brief).resolve())
    clip = Clip.load(brief.rig, args.result, frame=brief.entry_frame)
    window = [int(x) for x in args.active.split('-')]
    cal = CALIBRATION.get(args.calibration) if args.calibration else None
    grids = {name: arc_grid(clip, window, args.radius, args.report, name, cal, joints) for name in ('FightGuy', 'Manki')}
    for name, grid in grids.items():
        print(f"{name}: {arc_cells(grid)} positions touched ({arc_cells(grid, 'ahead')} ahead, "
              f"{arc_cells(grid, 'behind')} behind); standing {intervals(grid[0], ARC_DISTANCES, 0.25)}")
    if args.out:
        seg = weapon_segments(clip, range(window[0], window[1] + 1), floor_of(clip.pose(0)), *joints, calibration=cal)
        draw(args.out, None, ARC_DISTANCES, ARC_HEIGHTS, {Path(args.result).parent.parent.name: (seg, grids)}, 'hit arc')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
