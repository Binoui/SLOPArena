"""Command line: python -m genra {check,sheet,closeup,curves,motion,video,contacts,summary} ... (run with tools/ on PYTHONPATH)."""
from __future__ import annotations

import argparse
import json
import sys

from .brief import Brief, resolved_brief_path
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
    check.add_argument('--fast', action='store_true',
                       help='iteration only: numeric checks, no mesh checks or automatic renders; full checks remain the default')
    check.add_argument('--stage', choices=['keys', 'full'], help='default: the saved authoring stage')
    check.add_argument('--sheet', help="contact sheet path (default sheet.png, none with --fast; '' to skip)")
    check.add_argument('--motion', help="motion paths (default motion.png, none with --fast; '' to skip)")
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
    sheet.add_argument('--every', type=int, help='a column every N ticks over the whole clip (a filmstrip; overrides --ticks)')
    sheet.add_argument('--cols', type=int, help='wrap each view onto more rows past this many columns')
    close = sub.add_parser('closeup', help='close-ups centred on hands or feet: front, outer side and above, per tick')
    close.add_argument('clip')
    close.add_argument('--brief', default='brief.json')
    close.add_argument('--joints', default='LeftHand,RightHand', help='comma-separated joints (default: both hands)')
    close.add_argument('--ticks', type=_ints)
    close.add_argument('--out', default='closeup.png')
    close.add_argument('--size', type=int, default=220)
    close.add_argument('--span', type=float, help='visible width in metres (default: 2.4 forearms for a hand)')
    close.add_argument('--weapons', action='store_true', help='show held weapons (hidden by default: they cover the hand)')
    curves = sub.add_parser('curves', help="joint-angle curves (knees, thighs, elbows, wrists, pelvis, heights), over a reference's in grey")
    curves.add_argument('clip')
    curves.add_argument('--brief', default='brief.json')
    curves.add_argument('--reference', help='reference:<name> or another result.json, drawn in grey (time-normalised)')
    curves.add_argument('--out', default='curves.png')
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
    contacts = sub.add_parser('contacts', help='when each foot lands, for footstep sounds, dust and other cues')
    contacts.add_argument('clip')
    contacts.add_argument('--brief', default='brief.json')
    contacts.add_argument('--tolerance', type=float, default=0.02,
                          help="metres above a foot or toe joint's lowest point that still count as down (default 0.02)")
    contacts.add_argument('--json', action='store_true')
    continuity = sub.add_parser('continuity', help='read-only key interval/contact diagnostics before retries')
    continuity.add_argument('clip')
    continuity.add_argument('--brief', default='brief.json')
    continuity.add_argument('--keys', type=_ints, help='comma-separated ticks, default brief key beats')
    continuity.add_argument('--replay-carry', action='store_true', help='one offline both-ways carry on a copy per interval')
    summary = sub.add_parser('summary', help='print the rig summary')
    summary.add_argument('--brief', default='brief.json')
    args = parser.parse_args(argv)

    brief_path = (resolved_brief_path(args.brief, args.clip)
                  if args.command in ('check', 'continuity') and not args.clip.startswith('reference:') else args.brief)
    brief = Brief(brief_path)
    if args.command == 'summary':
        from .summary import rig_summary
        print(rig_summary(brief))
        return 0
    clip = _clip(brief, args.clip)
    if args.command == 'continuity':
        from .solve import continuity_report
        spec = brief.check_spec()
        print(json.dumps(continuity_report(clip, args.keys or brief.key_beats,
                         hold=spec.get('two_hands_hold', 'exact'), grip_span=spec.get('two_hands'),
                         replay_carry=args.replay_carry), indent=2))
        return 0
    ticks = getattr(args, 'ticks', None) or brief.review_ticks
    views = tuple(v for v in args.views.split(',') if v) if getattr(args, 'views', None) else brief.review_views
    if args.command == 'motion':
        from .render import motion_sheet
        first, last = (int(x) for x in args.range.split('-')) if args.range else (0, len(clip) - 1)
        views = tuple(v for v in args.views.split(',') if v) if args.views else ('front', brief.review_views[2], 'profile')
        motion_sheet(clip, views, args.out, ticks=range(first, last + 1), every=args.every, marks=(brief.split_tick,))
        print(f'wrote {args.out} (ticks {first}-{last})')
        return 0
    if args.command == 'video':
        from .render import preview_video
        clock = brief['clock']
        views = tuple(v for v in args.views.split(',') if v) if args.views else brief.preview_views
        preview_video(clip, args.out, views, contact=clock.get('contact_tick'), active=clock.get('active'),
                      loop_from=brief.loop_from)
        print(f'wrote {args.out} ({len(clip)} ticks)')
        return 0
    if args.command == 'sheet':
        center = [float(x) for x in args.center.split(',')] if args.center else None
        ticks = list(range(0, len(clip), args.every)) if args.every else ticks
        contact_sheet(clip, ticks, views, args.out, size=args.size, span=args.span, center=center, cols=args.cols)
        print(f'wrote {args.out} ({len(views)} views x {len(ticks)} ticks)')
        return 0
    if args.command == 'closeup':
        from .render import closeup_sheet
        joints = [j for j in args.joints.split(',') if j]
        closeup_sheet(clip, joints, ticks, args.out, size=args.size, span=args.span, weapons=args.weapons)
        print(f'wrote {args.out} ({len(joints)} joints x 3 views x {len(ticks)} ticks)')
        return 0
    if args.command == 'contacts':
        from .contacts import foot_contacts, format_contacts
        result = foot_contacts(clip, args.tolerance, brief.loop_from)
        print(json.dumps(result, indent=2) if args.json else format_contacts(result))
        return 0
    if args.command == 'curves':
        from .curves import curves_sheet
        reference = _clip(brief, args.reference) if args.reference else None
        curves_sheet(clip, args.out, reference=reference,
                     title=f'Joint curves: {args.clip}' + (f' (grey: {args.reference}, time-normalised)' if reference else ''))
        print(f'wrote {args.out}')
        return 0
    report = check_clip(clip, brief.check_spec(stage=args.stage), mode='fast' if args.fast else 'full')
    if args.json:
        print(json.dumps({k: v for k, v in report.items()}, indent=2, default=str))
    else:
        print(format_report(report))
    sheet = args.sheet if args.sheet is not None else ('' if args.fast else 'sheet.png')
    motion = args.motion if args.motion is not None else ('' if args.fast else 'motion.png')
    if sheet:
        contact_sheet(clip, ticks, views, sheet)
        print(f'sheet: {sheet} ({len(views)} views x ticks {ticks})')
    if motion:
        from .render import motion_overview
        motion_overview(clip, brief.split_tick, motion, ('front', brief.review_views[2], 'profile'))
        print(f'motion: {motion} (paths from entry to tick {brief.split_tick}, then to the end)')
    return 0 if report['passed'] else 1


if __name__ == '__main__':
    sys.exit(main())
