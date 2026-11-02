"""Offline flow replay: rebuild past moves from their approved keys with genra.flow and lay them beside the takes that
were made, for the owner to judge by eye. No model calls, no Unity.

    PYTHONPATH=tools <genra python> -m genra.replay_flow [--cases flip-kick,barn-door] [--no-video] [--force] [--out DIR]
    PYTHONPATH=tools <genra python> -m genra.replay_flow verdicts DIR --json '<the verdicts the board copied>'

Writes artifacts/genra-replays/flow-<toolkit hash>/ (or --out): per case the variant clips, full checks, a speed map,
compare.mp4 (game speed, then half speed) and row.json; summary.json; board.html at phone width with a verdict per move.
Variants are flow() dial settings (VARIANTS); the replay adds no motion of its own. It solves each brief's contacts
(two_hands, planted feet) on the ticks between keys for every built variant, as an author does after flow(); the takes
are shown as they were made.
"""
from __future__ import annotations

import argparse
import datetime
import hashlib
import html
import json
import math
import shutil
import subprocess
import sys
import time
from dataclasses import dataclass
from pathlib import Path

import numpy as np

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
sys.path.insert(0, str(HERE.parent))

MANIFEST = HERE / 'benchmarks' / 'flow-replay.json'
OUT_ROOT = REPO / 'artifacts' / 'genra-replays'
VARIANTS: dict[str, dict | None] = {   # None: today's Clip.from_keys through the same keys
    'pose-to-pose': None,
    'tangents': dict(lag=0, drag=0, arcs=0, settle=0, fingers=0),
    'flow': {},
    '+lag': dict(lag=1),
    '+drag': dict(drag=1),
}
VIDEO = ('pose-to-pose', 'tangents', 'flow', 'take', 'accepted')
VERDICTS = ('prefer flow', 'same', 'prefer the take', 'intentionally stiff')
METRICS = (('stops per joint per second', 'stops/s', '{:.2f}'),
           ('most of the body stopping together (share)', 'together', '{:.0%}'),
           ('frozen joint-ticks (share)', 'frozen', '{:.0%}'))
SOURCES = ('flow.py', 'clip.py', 'curves.py', 'checks.py', 'rig.py', 'solve.py', 'brief.py', 'render.py',
           'replay_flow.py')
TILE = 250


@dataclass(frozen=True)
class ReplayCase:
    name: str
    take: Path                  # a full run on approved keys: brief.json, keys/, scratch/result.json
    accepted: Path | None       # the owner-accepted take on the same keys, if any
    game_ticks: int
    clock: str                  # where game_ticks comes from
    note: str
    guard: str | None           # 'loop' or 'planted': a behaviour case, not a flow-quality one


def load_manifest(path: Path = MANIFEST, names=None) -> list[ReplayCase]:
    cases = []
    for entry in json.loads(Path(path).read_text())['cases']:
        unknown = set(entry) - {'name', 'take', 'accepted', 'game_ticks', 'clock', 'note', 'guard'}
        if unknown:
            raise ValueError(f"{entry.get('name')}: unknown fields {sorted(unknown)}")
        case = ReplayCase(entry['name'], REPO / entry['take'], REPO / entry['accepted'] if entry.get('accepted') else None,
                          int(entry['game_ticks']), entry['clock'], entry.get('note', ''), entry.get('guard'))
        for run in filter(None, (case.take, case.accepted)):
            for needed in ('brief.json', 'scratch/result.json'):
                if not (run / needed).is_file():
                    raise ValueError(f'{case.name}: {run / needed} is missing')
        cases.append(case)
    if names:
        missing = set(names) - {c.name for c in cases}
        if missing:
            raise ValueError(f'no such cases: {sorted(missing)}')
        cases = [c for c in cases if c.name in names]
    return cases


def toolkit_hash() -> str:
    digest = hashlib.sha256()
    for name in SOURCES:
        digest.update((HERE / name).read_bytes())
    digest.update(MANIFEST.read_bytes())
    return digest.hexdigest()[:8]


def keep_contacts(clip, brief, key_ticks) -> dict[str, float]:
    """The brief's contacts on the ticks between keys, as an author runs them after flow(): the two-handed hold
    (hold_grip per open key interval inside two_hands) and planted feet (two_bone toward their tick-0 spot). Key ticks
    are never solved. Returns label -> largest remaining miss (metres)."""
    from genra.solve import hold_grip, two_bone
    rig, out = clip.rig, {}
    span = brief.get('two_hands')
    if span and rig.grip:
        hold, worst = brief.get('two_hands_hold', 'exact'), 0.0
        for a, b in zip(key_ticks[:-1], key_ticks[1:]):
            first, last = max(a + 1, span[0]), min(b - 1, span[1])
            if first <= last:
                report = hold_grip(clip, first, last, hold=hold)
                worst = max(worst, report.get('maxMissMetres', report.get('maxOffHandleMetres', 0.0)))
        out[f'two_hands {span[0]}..{span[1]}'] = worst
    feet = sorted({rig.side(j) for j in brief.get('planted') or [] if 'Foot' in j or 'Toe' in j} - {None})
    for side in feet:
        target, worst = clip.pose(0).pos(side + 'Foot'), 0.0
        for tick in range(len(clip)):
            if tick in key_ticks:
                continue
            pose = clip.pose(tick)
            worst = max(worst, two_bone(pose, side, 'Leg', target))
            clip.set_pose(tick, pose)
        out[f'planted {side}Foot'] = worst
    clip._world = None
    return out


def measure(clip, spec, approved, window, skin) -> dict:
    """A take's numbers: flow on the case's window, exact-tick drift from the approved keys, the full check."""
    from genra.checks import check_clip
    from genra.curves import flow as flow_numbers
    from genra.flow import key_drift
    report = check_clip(clip, spec, skin=skin, mode='full')
    drift = key_drift(clip, approved) if len(clip) == spec['duration_ticks'] + 1 else {}
    return {'flow': flow_numbers(clip, window),
            'driftCm': {t: round(d.metres * 100, 5) for t, d in drift.items()},
            'maxDriftCm': max((d.metres * 100 for d in drift.values()), default=None),
            'maxDriftDegrees': max((d.degrees for d in drift.values()), default=None),
            'passed': report['passed'], 'failures': sorted({f['check'] for f in report['failures']}),
            'failureText': [f['message'] for f in report['failures']],
            'warnings': sorted({w['check'] for w in report['warnings']})}


def replay_case(case: ReplayCase, out: Path, video: bool = True) -> dict:
    """Build every variant from the take's approved keys, solve the brief's contacts, check and measure them beside the
    take (and the accepted take), and write out/<case>/. Deterministic for the same inputs and toolkit."""
    from genra.brief import Brief, resolved_brief_path
    from genra.checks import DEFAULT_SPEC
    from genra.clip import Clip
    from genra.curves import flow as flow_numbers, flow_window, speed_map
    from genra.flow import _KeySet, _MoveFacts, approved_keys, flow
    from genra.skin import Skin
    started = time.perf_counter()
    brief = Brief(resolved_brief_path(case.take / 'brief.json', case.take / 'scratch/result.json'))
    n = brief['clock']['duration_ticks'] + 1
    skin = Skin(brief.rig)
    approved = approved_keys(brief)
    window = flow_window(n, hold_from=(brief.get('exit') or {}).get('from_tick'))
    spec = brief.check_spec(stage='full')
    work = out / f'{case.name}.tmp'
    shutil.rmtree(work, ignore_errors=True)
    work.mkdir(parents=True)
    keyset = _KeySet.build(_MoveFacts.from_brief(brief), None)  # flow's own key set, also when flow refuses
    keys = dict(zip(keyset.ticks, keyset.poses))
    key_ticks = sorted(keys)
    try:
        _, base = flow(brief)
        refused = None
    except ValueError as error:
        base, refused = None, str(error)
    rows, clips = {}, {}
    for name, dials in VARIANTS.items():
        try:
            if dials is None:
                cycle = (brief.loop_from, n - 1) if brief.loop_from is not None else None
                clip, report = Clip.from_keys(keys, n, cycle=cycle), None
            else:
                clip, report = flow(brief, **dials)
        except ValueError as error:
            rows[name] = {'error': str(error)}
            continue
        raw = flow_numbers(clip, window)
        contacts = keep_contacts(clip, brief, key_ticks)
        clips[name] = clip
        rows[name] = measure(clip, spec, approved, window, skin) | {
            'rawFlow': raw, 'contacts': contacts, 'flowReport': report.to_dict() if report else None}
        clip.save(work / f"{name.replace('/', '-').replace('+', 'plus-')}.json")
    for label, run in (('take', case.take), ('accepted', case.accepted)):
        if run is None:
            continue
        own = Brief(resolved_brief_path(run / 'brief.json', run / 'scratch/result.json'))
        clip = Clip.load(brief.rig, run / 'scratch/result.json', frame=brief.entry_frame)
        clips[label] = clip
        same_keys = (own.get('approved_keys') or {}).get('run') == (brief.get('approved_keys') or {}).get('run')
        rows[label] = measure(clip, own.check_spec(stage='full'), approved if same_keys else {}, window, skin) | {
            'run': str(run.relative_to(REPO)), 'sameKeys': same_keys}
    flowed, take = rows.get('flow', {}), rows['take']
    one_shot = brief.loop_from is None
    numbers = flowed.get('flow', {})
    row = {
        'name': case.name, 'note': case.note, 'guard': case.guard, 'run': str(case.take.relative_to(REPO)),
        'keysRun': (brief.get('approved_keys') or {}).get('run'),
        'ticks': n, 'gameTicks': case.game_ticks, 'clock': case.clock, 'keyTicks': key_ticks,
        'purpose': base.spec.purpose if base else None,
        'approvedTicks': sorted(approved), 'window': list(window), 'variants': rows,
        # Flow's own failures: not in the take and not already in the keys (pose-to-pose through the same keys).
        'newFailures': (sorted(set(flowed.get('failures', [])) - set(take['failures'])
                               - set(rows.get('pose-to-pose', {}).get('failures', []))) if 'flow' in clips else []),
        'refused': refused,
        'inheritedFailures': sorted((set(flowed.get('failures', [])) & set(rows.get('pose-to-pose', {}).get('failures', [])))
                                    - set(take['failures'])),
        'inEnvelope': (one_shot and bool(numbers) and
                       numbers['stops per joint per second'] <= DEFAULT_SPEC['flow_stops_per_second'] and
                       numbers['most of the body stopping together (share)'] <= DEFAULT_SPEC['flow_together_share'] and
                       numbers['frozen joint-ticks (share)'] <= DEFAULT_SPEC['flow_frozen_share']),
        'driftOk': flowed.get('maxDriftCm') is not None and flowed['maxDriftCm'] <= DEFAULT_SPEC['keys_metres'] * 100,
        'keysHold': [list(h) for h in base.holds] if base else [], 'flowReport': str(base) if base else refused,
        'seconds': None}
    if 'flow' in clips:
        speed_map(clips['flow'], work / 'speed.png', reference=clips['take'], window=window,
                  title=f'{case.name}: flow (top) and the take (below), joint speed per tick')
    if video:
        compare_video(case, brief, clips, skin, work / 'compare.mp4')
        row['video'] = f'{case.name}/compare.mp4'
    row['seconds'] = round(time.perf_counter() - started, 1)
    (work / 'row.json').write_text(json.dumps(row, indent=1, default=float))
    final = out / case.name
    shutil.rmtree(final, ignore_errors=True)
    work.rename(final)
    return row


def compare_video(case: ReplayCase, brief, clips: dict, skin, path: Path) -> Path:
    """Tiles of VIDEO in two columns, one shared camera (brief.preview_views' last view, framed over every clip): first
    at game speed (each frame the authored tick the game shows), looped, then at half game speed."""
    from PIL import Image, ImageDraw
    from genra.render import BACKGROUND, Renderer, _font, encode_passes
    shown = [(label, clips[label]) for label in VIDEO if label in clips and len(clips[label]) == len(clips['take'])]
    n = len(clips['take'])
    view = brief.preview_views[-1]
    renderer = Renderer(skin, brief.entry_frame)
    worlds = {label: clip.world() for label, clip in shown}
    points = np.concatenate([np.concatenate(skin.points(w)) for world in worlds.values() for w in world[::3]])
    right, up, toward = renderer.camera(view)
    screen = np.stack([points @ right, points @ up], axis=1)
    lo, hi = screen.min(axis=0), screen.max(axis=0)
    span = float((hi - lo).max() * 1.1)  # the view's own extent: a larger figure than the 3D box diagonal gives
    middle = (lo + hi) / 2
    center = brief.entry_frame.to_char(middle[0] * right + middle[1] * up + toward * float((points @ toward).mean()))
    cols, rows = 2, math.ceil(len(shown) / 2)
    head, font = 28, _font(15)
    frames = []
    for tick in range(n):
        frame = Image.new('RGB', (cols * TILE, head + rows * TILE), BACKGROUND)
        for k, (label, _) in enumerate(shown):
            tile = renderer.image(worlds[label][tick], view, TILE, span, center, label)
            frame.paste(tile, ((k % cols) * TILE, head + (k // cols) * TILE))
        ImageDraw.Draw(frame).text((8, 6), f'{case.name}  t{tick}', fill=(235, 238, 242), font=font)
        frames.append(frame)
    game = [frames[round(i * (n - 1) / case.game_ticks)] for i in range(case.game_ticks + 1)]
    half = max(1, round(2 * case.game_ticks / (n - 1)))
    return encode_passes([('game', 1, game * max(2, math.ceil(150 / len(game)))), ('1/2 game', half, frames)], path)


# ----------------------------------------------------------------------------- board
def write_board(out: Path, summary: dict) -> Path:
    """board.html for the owner at phone width: a headline, then per move its video, the metric mini-table, exact
    drift, new-failure chips, a keys-hold flag and a verdict with a note; 'Copy verdicts' exports them as JSON for
    `replay_flow verdicts`."""
    rows = summary['cases']
    head = summary['headline']
    esc = html.escape
    cards = []
    for row in rows:
        if 'error' in row:
            cards.append(f'<section class="card" id="{esc(row["name"])}"><h2>{esc(row["name"])}</h2>'
                         f'<p class="bad">flow refused these keys: {esc(row["error"])}</p></section>')
            continue
        v = row['variants']
        lines = []
        for label in list(VARIANTS) + ['take', 'accepted']:
            take = v.get(label)
            if take is None:
                continue
            if 'error' in take:
                lines.append(f'<tr><th>{esc(label)}</th><td colspan="5" class="bad">{esc(take["error"])}</td></tr>')
                continue
            cells = ''.join(f'<td>{fmt.format(take["flow"][key])}</td>' for key, _, fmt in METRICS)
            drift = take.get('maxDriftCm')
            cells += f'<td>{"-" if drift is None else f"{drift:.2f}"}</td>'
            fails = take['failures']
            cells += f'<td class="{"bad" if fails else ""}">{esc(", ".join(fails)) if fails else "pass"}</td>'
            mark = ' class="hl"' if label in ('flow', 'take') else ''
            lines.append(f'<tr{mark}><th>{esc(label)}</th>{cells}</tr>')
        chips = [f'<span class="chip bad">flow refused these keys: {esc(row["refused"])}</span>'] if row.get('refused') else []
        chips += [f'<span class="chip bad">new failure: {esc(f)}</span>' for f in row['newFailures']]
        chips += [f'<span class="chip muted">from the keys: {esc(f)}</span>' for f in row.get('inheritedFailures', [])]
        chips += [f'<span class="chip warn">keys hold {a}..{b} ({share:.0%})</span>' for a, b, share in row['keysHold']]
        if row['guard']:
            ok = 'flow' in row['variants'] and row['guard'] not in row['variants']['flow'].get('failures', [row['guard']])
            chips.append(f'<span class="chip {"good" if ok else "bad"}">guard: {esc(row["guard"])} '
                         f'{"holds" if ok else "broken"}</span>')
        elif row['inEnvelope']:
            chips.append('<span class="chip good">in the flow envelope</span>')
        if row['driftOk']:
            chips.append('<span class="chip good">keys exact</span>')
        choices = ''.join(f'<label><input type="radio" name="v-{esc(row["name"])}" value="{esc(c)}"> {esc(c)}</label>'
                          for c in VERDICTS)
        video = (f'<video src="{esc(row["video"])}" controls muted loop playsinline preload="metadata"></video>'
                 if row.get('video') else '<p class="muted">no video</p>')
        cards.append(f'''<section class="card" id="{esc(row["name"])}">
<h2>{esc(row["name"])} <small>{esc(row["purpose"] or "")} · {row["ticks"] - 1} ticks, game {row["gameTicks"]}</small></h2>
<p class="muted">{esc(row["note"])}</p>
{video}
<p class="legend">Tiles: pose-to-pose · tangents / flow · the take{" / accepted" if "accepted" in v else ""}. Game speed, then half.</p>
<div class="chips">{"".join(chips)}</div>
<table><tr><th></th><th>stops/s</th><th>together</th><th>frozen</th><th>drift cm</th><th>checks</th></tr>{"".join(lines)}</table>
<p class="muted small"><a href="{esc(row["name"])}/speed.png">speed map</a> · keys {", ".join(map(str, row["approvedTicks"]))}</p>
<details class="small"><summary>flow report</summary><pre>{esc(row["flowReport"] or "")}</pre></details>
<fieldset><legend>Your verdict</legend>{choices}<textarea placeholder="note (optional)" data-case="{esc(row["name"])}"></textarea></fieldset>
</section>''')
    thresholds = summary['thresholds']
    page = f'''<!doctype html><html lang="en"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Flow replay {esc(summary["toolkit"])}</title>
<style>
:root {{ color-scheme: light; --ink:#202124; --muted:#6b6f76; --line:#e3e3df; --good:#1b7f5a; --bad:#b3261e; --warn:#8a5a00; }}
* {{ box-sizing: border-box; }}
body {{ margin:0; font:15px/1.4 system-ui, -apple-system, sans-serif; color:var(--ink); background:#f6f6f3; }}
main {{ max-width: 560px; margin: 0 auto; padding: 12px; }}
h1 {{ font-size: 20px; margin: 4px 0 8px; }}
h2 {{ font-size: 17px; margin: 0 0 4px; }} h2 small {{ font-weight: 400; color: var(--muted); font-size: 13px; }}
.card {{ background:#fff; border:1px solid var(--line); border-radius:12px; padding:12px; margin:12px 0; }}
video {{ width:100%; border-radius:8px; background:#181c24; display:block; }}
table {{ width:100%; border-collapse:collapse; font-size:12.5px; margin-top:8px; font-variant-numeric: tabular-nums; }}
th, td {{ padding:3px 4px; border-bottom:1px solid var(--line); text-align:right; }}
th:first-child {{ text-align:left; white-space:nowrap; }} td:last-child {{ text-align:left; }}
tr.hl {{ background:#f1f5ff; }}
.bad {{ color:var(--bad); }} .muted {{ color:var(--muted); }} .small, .legend {{ font-size:12.5px; }}
.legend {{ color:var(--muted); margin:6px 0 0; }}
.chips {{ display:flex; flex-wrap:wrap; gap:6px; margin-top:8px; }}
.chip {{ font-size:12px; border-radius:999px; padding:2px 8px; border:1px solid currentColor; }}
.chip.good {{ color:var(--good); }} .chip.bad {{ color:var(--bad); }} .chip.warn {{ color:var(--warn); }}
.chip.muted {{ color:var(--muted); }}
fieldset {{ border:1px solid var(--line); border-radius:8px; margin-top:10px; }}
fieldset label {{ display:block; padding:4px 0; }}
textarea {{ width:100%; min-height:44px; margin-top:6px; font:inherit; }}
button {{ font:inherit; padding:10px 14px; border-radius:8px; border:1px solid #2a5bd7; background:#2a5bd7; color:#fff; }}
#out {{ width:100%; min-height:90px; font:12px ui-monospace, monospace; }}
pre {{ white-space:pre-wrap; font:11.5px/1.35 ui-monospace, monospace; }}
</style></head><body><main>
<h1>Flow replay: first takes from the approved keys</h1>
<p>{head["inEnvelope"]} of {head["oneShots"]} moves flow inside the check's envelope (stops &le; {thresholds["stops"]}/s,
together &le; {thresholds["together"]:.0%}, frozen &le; {thresholds["frozen"]:.0%}); keys exact on {head["driftOk"]} of {head["cases"]};
new check failures on {head["newFailures"]}; keys hold on {head["keysHold"]}. Flow stays advisory: you judge by eye.</p>
<p class="muted small">Rows: pose-to-pose is today's interpolation, tangents the carrier alone, +lag adds follow-through,
flow is the default the runner now seeds (purpose preset), +arcs and +drag add the two layers still off by default. The take is
the first take made on these keys. Drift is measured on each key's own tick. Toolkit {esc(summary["toolkit"])},
{esc(summary["created"])}.</p>
{"".join(cards)}
<section class="card"><h2>Send your verdicts</h2>
<p class="small muted">Copies the choices as JSON (also kept in this browser). Paste it into the thread.</p>
<button id="copy">Copy verdicts</button>
<textarea id="out" readonly></textarea></section>
</main>
<script>
const KEY = 'flow-replay-{esc(summary["toolkit"])}';
const cases = {json.dumps([r["name"] for r in rows])};
function collect() {{
  const out = {{board: '{esc(summary["toolkit"])}', verdicts: {{}}}};
  for (const c of cases) {{
    const pick = document.querySelector(`input[name="v-${{c}}"]:checked`);
    const note = document.querySelector(`textarea[data-case="${{c}}"]`).value.trim();
    if (pick || note) out.verdicts[c] = {{verdict: pick ? pick.value : null, note}};
  }}
  return out;
}}
function save() {{ const data = collect(); localStorage.setItem(KEY, JSON.stringify(data));
  document.getElementById('out').value = JSON.stringify(data); }}
try {{ const saved = JSON.parse(localStorage.getItem(KEY) || 'null');
  if (saved) for (const [c, v] of Object.entries(saved.verdicts || {{}})) {{
    if (v.verdict) {{ const el = document.querySelector(`input[name="v-${{c}}"][value="${{v.verdict}}"]`); if (el) el.checked = true; }}
    const t = document.querySelector(`textarea[data-case="${{c}}"]`); if (t) t.value = v.note || '';
  }} }} catch (e) {{}}
document.addEventListener('change', save); document.addEventListener('input', save); save();
document.getElementById('copy').onclick = async () => {{ save(); const text = document.getElementById('out').value;
  try {{ await navigator.clipboard.writeText(text); document.getElementById('copy').textContent = 'Copied'; }}
  catch (e) {{ document.getElementById('out').select(); }} }};
</script></body></html>'''
    path = out / 'board.html'
    path.write_text(page)
    return path


def record_verdicts(out: Path, text: str) -> Path:
    """Validate the board's copied JSON against summary.json and write out/verdicts.json with the date."""
    summary = json.loads((out / 'summary.json').read_text())
    data = json.loads(text)
    names = {row['name'] for row in summary['cases']}
    if data.get('board') != summary['toolkit']:
        raise ValueError(f"these verdicts are for board {data.get('board')}, not {summary['toolkit']}")
    for name, verdict in data.get('verdicts', {}).items():
        if name not in names:
            raise ValueError(f'unknown case {name!r}')
        if verdict.get('verdict') not in VERDICTS + (None,):
            raise ValueError(f"{name}: verdict must be one of {VERDICTS}, not {verdict.get('verdict')!r}")
    path = out / 'verdicts.json'
    path.write_text(json.dumps({'recorded': datetime.datetime.now().isoformat(timespec='seconds'), **data}, indent=2))
    return path


def summarize(out: Path, rows: list[dict], manifest: Path) -> dict:
    from genra.checks import DEFAULT_SPEC
    from genra.flow import PRESETS
    one_shots = [r for r in rows if not r['guard']]
    git = subprocess.run(['git', 'rev-parse', 'HEAD'], cwd=REPO, capture_output=True, text=True).stdout.strip()
    summary = {
        'created': datetime.datetime.now().isoformat(timespec='seconds'), 'toolkit': out.name.removeprefix('flow-'),
        'manifestSha256': hashlib.sha256(manifest.read_bytes()).hexdigest(), 'gitHead': git,
        'variants': VARIANTS, 'presets': PRESETS,
        'thresholds': {'stops': DEFAULT_SPEC['flow_stops_per_second'], 'together': DEFAULT_SPEC['flow_together_share'],
                       'frozen': DEFAULT_SPEC['flow_frozen_share'], 'keysCm': DEFAULT_SPEC['keys_metres'] * 100},
        'headline': {'cases': len(rows), 'oneShots': len(one_shots),
                     'inEnvelope': sum(r['inEnvelope'] for r in one_shots),
                     'driftOk': sum(r['driftOk'] for r in rows),
                     'newFailures': sum(bool(r['newFailures']) for r in rows),
                     'keysHold': sum(bool(r['keysHold']) for r in rows)},
        'cases': rows}
    (out / 'summary.json').write_text(json.dumps(summary, indent=1, default=float))
    return summary


def main(argv=None) -> int:
    argv = list(sys.argv[1:] if argv is None else argv)
    if argv[:1] == ['verdicts']:
        parser = argparse.ArgumentParser(prog='replay_flow verdicts')
        parser.add_argument('out', type=Path)
        parser.add_argument('--json', required=True)
        args = parser.parse_args(argv[1:])
        print(record_verdicts(args.out, args.json))
        return 0
    parser = argparse.ArgumentParser(prog='replay_flow', description=__doc__.split('\n\n')[0])
    parser.add_argument('--manifest', type=Path, default=MANIFEST)
    parser.add_argument('--cases', help='comma-separated case names (default: all)')
    parser.add_argument('--out', type=Path)
    parser.add_argument('--no-video', action='store_true')
    parser.add_argument('--force', action='store_true', help='rebuild cases that are already complete')
    args = parser.parse_args(argv)
    cases = load_manifest(args.manifest, args.cases.split(',') if args.cases else None)
    out = args.out or OUT_ROOT / f'flow-{toolkit_hash()}'
    out.mkdir(parents=True, exist_ok=True)
    rows = []
    for case in load_manifest(args.manifest):
        done = out / case.name / 'row.json'
        if case in cases and (args.force or not done.exists()):
            try:
                row = replay_case(case, out, video=not args.no_video)
            except ValueError as error:  # flow refused these keys (e.g. an ambiguous turn): show it on the board
                row = {'name': case.name, 'note': case.note, 'guard': case.guard, 'error': str(error), 'variants': {},
                       'newFailures': ['flow failed'], 'inEnvelope': False, 'driftOk': False, 'keysHold': [],
                       'seconds': None}
                print(f'{case.name:24s} flow failed: {error}')
                rows.append(row)
                continue
        elif done.exists():
            row = json.loads(done.read_text())
        else:
            continue
        flowed = row['variants'].get('flow', {}).get('flow')
        print(f"{case.name:24s} " + (' '.join(f'{label} {fmt.format(flowed[key])}' for key, label, fmt in METRICS)
                                     if flowed else 'flow failed') +
              f"  drift ok {row['driftOk']}  new failures {row['newFailures'] or '-'}  {row['seconds']}s")
        rows.append(row)
    summary = summarize(out, rows, args.manifest)
    print(write_board(out, summary))
    return 0


if __name__ == '__main__':
    sys.exit(main())
