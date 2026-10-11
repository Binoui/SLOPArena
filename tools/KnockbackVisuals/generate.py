"""Render saved knockback comparisons. HTML generation uses only the Python standard library.

Run without arguments to rebuild the permanent viewer from comparison.json.
Use --export to also render PNG/MP4 (requires matplotlib and ffmpeg).
Use --before and --after-pattern to import a new pair of MoveDataReport datasets.
"""
import argparse
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
COLORS = ['#ea9254', '#58dcc7']
BACKGROUND, FOREGROUND, MUTED = '#111b28', '#eef3f9', '#a0b0c3'


def load_comparison(before, after_pattern):
    result = {}
    for character in ['fightguy', 'manki', 'wibou', 'bonk']:
        reports = [json.loads((before / f'{character}.json').read_text()),
                   json.loads(Path(after_pattern.format(character=character)).read_text())]
        moves = []
        assert len(reports[0]['moves']) == len(reports[1]['moves'])
        for old, new in zip(*[r['moves'] for r in reports]):
            assert (old['slot'], old['hitIndex']) == (new['slot'], new['hitIndex'])
            assert len(old['trajectories']) == len(new['trajectories'])
            entries = {}
            for a, b in zip(old['trajectories'], new['trajectories']):
                assert a['pct'] == b['pct']
                entries[str(a['pct'])] = [
                    {'points': [[p['tick'], round(p['travel'], 5), round(p['height'], 5),
                                 p['inHitstun']] for p in t['points']]}
                    for t in [a, b]
                ]
            moves.append({'label': new['label'], 'slot': old['slot'],
                          'hit': old['hitIndex'], 'trajectories': entries,
                          'changed': old.get('frame') != new.get('frame')})
        result[character] = moves
    return result


def trajectory(data, slot, pct):
    return next(m for m in data['fightguy'] if m['slot'] == slot)['trajectories'][str(pct)]


def export_plots(data, output):
    import matplotlib
    matplotlib.use('Agg')
    import matplotlib.pyplot as plt
    from matplotlib.animation import FFMpegWriter
    from matplotlib.lines import Line2D
    import numpy as np

    plt.rcParams.update({'font.family': 'DejaVu Sans', 'font.size': 12,
                         'text.color': FOREGROUND, 'axes.labelcolor': MUTED,
                         'xtick.color': MUTED, 'ytick.color': MUTED,
                         'axes.edgecolor': '#38495b', 'savefig.facecolor': BACKGROUND})

    def setup(ax, curves, title, subtitle, stretch=1):
        ax.set_facecolor(BACKGROUND)
        maximum_x = max(p[1] for c in curves for p in c['points'])
        maximum_y = max(p[2] for c in curves for p in c['points'])
        ax.set_xlim(-maximum_x * .035, maximum_x * 1.12)
        ax.set_ylim(-maximum_y * .12, maximum_y * 1.26)
        ax.set_aspect(stretch)
        ax.axhline(0, color='#64788d', linewidth=1.4)
        ax.set_xlabel('Sideways travel (m)', labelpad=8)
        ax.set_ylabel('Rise above start (m)', labelpad=9)
        ax.grid(alpha=.09, color=FOREGROUND)
        ax.spines[['top', 'right']].set_visible(False)
        ax.set_title(title + '\n' + subtitle, loc='left', fontsize=14, pad=16)
        for c, color in zip(curves, COLORS):
            x, y = np.array(c['points'])[:, 1:3].T.astype(float)
            ax.plot(x, y, color=color, lw=3)
            apex = int(np.argmax(y))
            ax.plot(x[apex], y[apex], 'o', color=color, ms=6)
            ax.plot(x[-1], y[-1], '|', color=color, ms=18, mew=3)

    fig = plt.figure(figsize=(14, 10), facecolor=BACKGROUND)
    grid = fig.add_gridspec(2, 2, height_ratios=[1.35, 1], hspace=.6, wspace=.35,
                           left=.08, right=.96, top=.82, bottom=.10)
    fig.text(.065, .952, 'KNOCKBACK: BEFORE / AFTER', fontsize=24, weight='bold')
    fig.text(.065, .915, 'Same hit. Same launch angle. Real simulation paths.', color=MUTED, fontsize=14)
    handles = [Line2D([0], [0], color=c, lw=4, label=l)
               for c, l in zip(COLORS, ['Before', 'After'])]
    fig.legend(handles=handles, loc='upper right', bbox_to_anchor=(.97, .96),
               frameon=False, ncol=2, fontsize=14)
    setup(fig.add_subplot(grid[0, :]), trajectory(data, 'g4', 120),
          'Strong send · Double Kick · 120% damage',
          'Much lower arc; still travels outward, but lands sooner.')
    setup(fig.add_subplot(grid[1, 0]), trajectory(data, 'a3', 120),
          'Launcher · High Kick · 120%',
          'Still launches upward; less overhead hang.')
    setup(fig.add_subplot(grid[1, 1]), trajectory(data, 'g1', 0),
          'Weak hit · Low Kick · 0%',
          'The small pop remains. Height magnified 5×.', stretch=5)
    fig.text(.065, .035, 'Side view • no player input • each panel has its own scale • grounded carry after the report ends is excluded',
             fontsize=10, color=MUTED)
    fig.savefig(output / 'overview.png', dpi=110)
    plt.close(fig)

    fig, ax = plt.subplots(figsize=(12.8, 7.2), facecolor=BACKGROUND)
    fig.subplots_adjust(left=.08, right=.95, top=.74, bottom=.16)
    curves = trajectory(data, 'g4', 120)
    setup(ax, curves, 'Double Kick · 120% damage', 'Same scale, synchronized playback · half speed')
    for line in ax.lines[1:]:
        line.set_alpha(.22)
    fig.text(.065, .92, 'LESS HEIGHT. EARLIER LANDING.', fontsize=23, weight='bold')
    fig.legend(handles=handles, loc='upper right', bbox_to_anchor=(.96, .96),
               frameon=False, ncol=2, fontsize=14)
    time_label = fig.text(.065, .855, '', color=MUTED, fontsize=13)
    fig.text(.065, .045, 'Recorded shared simulation • no player input • positions stop at the end of each report',
             color=MUTED, fontsize=10)
    markers = [ax.plot([], [], 'o', color=c, ms=11, mec=BACKGROUND, mew=2)[0] for c in COLORS]
    trails = [ax.plot([], [], color=c, lw=3)[0] for c in COLORS]
    states = [fig.text(.66, .83 - i * .047, '', color=c, fontsize=13)
              for i, c in enumerate(COLORS)]
    writer = FFMpegWriter(fps=30, codec='libx264', bitrate=1800,
                         extra_args=['-pix_fmt', 'yuv420p', '-movflags', '+faststart'])
    duration = max(c['points'][-1][0] for c in curves)
    with writer.saving(fig, str(output / 'before-after.mp4'), dpi=100):
        for frame in range(duration + 61):
            tick = max(0, min(duration, frame - 15))
            time_label.set_text(f'Time since launch: {tick / 60:.2f} s')
            for i, c in enumerate(curves):
                points = np.array(c['points'])
                index = min(tick, len(points) - 1)
                p = points[index]
                markers[i].set_data([p[1]], [p[2]])
                trails[i].set_data(points[:index + 1, 1], points[:index + 1, 2])
                status = 'On ground' if tick > 0 and p[2] == 0 else 'In the air'
                states[i].set_text(['Before', 'After'][i] + ': ' + status)
            writer.grab_frame()
    plt.close(fig)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--before', type=Path)
    parser.add_argument('--after-pattern')
    parser.add_argument('--comparison', type=Path, default=Path(__file__).with_name('comparison.json'))
    parser.add_argument('--save-comparison', type=Path, help='Save imported datasets for future regeneration')
    parser.add_argument('--label', default='Custom knockback comparison', help='Label for imported datasets')
    parser.add_argument('--output', type=Path, default=ROOT / 'docs/generated/knockback-visuals')
    parser.add_argument('--export', action='store_true', help='Also regenerate the PNG overview and MP4')
    parser.add_argument('--html-only', action='store_true', help='Skip PNG/MP4, including when --export is set')
    args = parser.parse_args()
    if bool(args.before) != bool(args.after_pattern):
        parser.error('--before and --after-pattern must be supplied together')
    if args.save_comparison and not args.before:
        parser.error('--save-comparison requires imported --before and --after-pattern datasets')
    if args.before:
        snapshot = {'schemaVersion': 1, 'metadata': {'label': args.label},
                    'characters': load_comparison(args.before, args.after_pattern)}
        if args.save_comparison:
            args.save_comparison.parent.mkdir(parents=True, exist_ok=True)
            args.save_comparison.write_text(json.dumps(snapshot, separators=(',', ':')) + '\n')
    else:
        snapshot = json.loads(args.comparison.read_text())
        if snapshot['schemaVersion'] != 1:
            parser.error('Unsupported comparison schema version')
    args.output.mkdir(parents=True, exist_ok=True)
    data = snapshot['characters']
    template = Path(__file__).with_name('template.html').read_text()
    # Keep the viewer self-contained and safe to open directly as a local file.
    encode = lambda value: json.dumps(value, separators=(',', ':')).replace('<', '\\u003c')
    html = template.replace('__TRAJECTORIES__', encode(data)).replace('__COMPARISON_META__', encode(snapshot['metadata']))
    (args.output / 'index.html').write_text(html)
    if args.export and not args.html_only:
        export_plots(data, args.output)
    print(args.output)


if __name__ == '__main__':
    main()
