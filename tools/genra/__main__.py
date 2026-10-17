"""Command line: python -m genra {check,sheet,motion,video,summary} ... (run with tools/ on PYTHONPATH)."""
from __future__ import annotations

import argparse
import json
import sys

from .brief import Brief
from .checks import check_clip, format_report
from .clip import Clip
from .render import contact_sheet


VIEWS_HELP = 'comma-separated views (default: profile, front, three-quarter from the striking side)'


def _ints(text):
    return [int(x) for x in text.split(',') if x]


def _clip(brief, spec):
    if spec.startswith('reference:'):
        return brief.reference(spec.split(':', 1)[1])
    return Clip.load(brief.rig, spec, frame=brief.entry_frame)


def main(argv=None):
    parser = argparse.ArgumentParser(prog='genra')
    sub = parser.add_subparsers(dest='command', required=True)
    check = sub.add_parser('check', help='check a saved clip against the brief; optionally render a sheet')
    check.add_argument('clip')
    check.add_argument('--brief', default='brief.json')
    check.add_argument('--sheet', default='sheet.png', help="contact sheet path ('' to skip)")
    check.add_argument('--motion', default='motion.png', help="motion paths (entry-contact, contact-end) ('' to skip)")
    check.add_argument('--ticks', type=_ints)
    check.add_argument('--views', help=VIEWS_HELP)
    check.add_argument('--json', action='store_true')
    sheet = sub.add_parser('sheet', help='render a contact sheet of a clip or reference:<name>')
    sheet.add_argument('clip')
    sheet.add_argument('--brief', default='brief.json')
    sheet.add_argument('--out', default='sheet.png')
    sheet.add_argument('--ticks', type=_ints)
    sheet.add_argument('--views', help=VIEWS_HELP)
    sheet.add_argument('--size', type=int, default=240)
    sheet.add_argument('--span', type=float, help='visible width in metres (default: fit the poses)')
    sheet.add_argument('--center', help='char-frame point r,f,u at the image centre')
    motion = sub.add_parser('motion', help='ghosted poses and hand, elbow and blade paths over a tick range')
    motion.add_argument('clip')
    motion.add_argument('--brief', default='brief.json')
    motion.add_argument('--out', default='motion.png')
    motion.add_argument('--range', help='first-last tick, e.g. 0-18 (default: the whole clip)')
    motion.add_argument('--views', help='comma-separated views (default: front, three-quarter, profile)')
    motion.add_argument('--every', type=int, default=4, help='a ghosted pose every N ticks')
    video = sub.add_parser('video', help='render an MP4 preview (real speed, then slowed) of a clip or reference:<name>')
    video.add_argument('clip')
    video.add_argument('--brief', default='brief.json')
    video.add_argument('--out', default='preview.mp4')
    video.add_argument('--views', help='comma-separated views (default: profile and the three-quarter view from the striking side)')
    summary = sub.add_parser('summary', help='print the rig summary')
    summary.add_argument('--brief', default='brief.json')
    args = parser.parse_args(argv)

    brief = Brief(args.brief)
    if args.command == 'summary':
        from .summary import rig_summary
        print(rig_summary(brief))
        return 0
    clip = _clip(brief, args.clip)
    ticks = getattr(args, 'ticks', None) or brief.review_ticks
    views = tuple(v for v in args.views.split(',') if v) if args.views else brief.review_views
    if args.command == 'motion':
        from .render import motion_sheet
        first, last = (int(x) for x in args.range.split('-')) if args.range else (0, len(clip) - 1)
        views = tuple(v for v in args.views.split(',') if v) if args.views else ('front', brief.review_views[2], 'profile')
        motion_sheet(clip, views, args.out, ticks=range(first, last + 1), every=args.every, marks=(brief['clock']['contact_tick'],))
        print(f'wrote {args.out} (ticks {first}-{last})')
        return 0
    if args.command == 'video':
        from .render import preview_video
        clock = brief['clock']
        views = tuple(v for v in args.views.split(',') if v) if args.views else brief.review_views[::2]
        preview_video(clip, args.out, views, contact=clock['contact_tick'], active=clock['active'])
        print(f'wrote {args.out} ({len(clip)} ticks)')
        return 0
    if args.command == 'sheet':
        center = [float(x) for x in args.center.split(',')] if args.center else None
        contact_sheet(clip, ticks, views, args.out, size=args.size, span=args.span, center=center)
        print(f'wrote {args.out} ({len(views)} views x {len(ticks)} ticks)')
        return 0
    report = check_clip(clip, brief.check_spec())
    if args.json:
        print(json.dumps({k: v for k, v in report.items()}, indent=2, default=str))
    else:
        print(format_report(report))
    if args.sheet:
        contact_sheet(clip, ticks, views, args.sheet)
        print(f'sheet: {args.sheet} ({len(views)} views x ticks {ticks})')
    if args.motion:
        from .render import motion_overview
        motion_overview(clip, brief['clock']['contact_tick'], args.motion, ('front', brief.review_views[2], 'profile'))
        print(f'motion: {args.motion} (paths from entry to contact, then contact to the end)')
    return 0 if report['passed'] else 1


if __name__ == '__main__':
    sys.exit(main())
