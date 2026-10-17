#!/usr/bin/env python3
"""Plan and present character-presentation review captures (stdlib; media uses ffmpeg).

Run from the repository root. Unity's ``sloparena.hitfeel.*`` commands capture one
deterministic duel per move as JPEG frames (docs/systems/vfx-particles.md). This tool
writes their gateway batch, summarizes the batch output, and turns frames into contact
sheets, labelled side-by-side WebP clips and a review page. The workflow is
skills/sloparena-character-vfx-pass/SKILL.md.

  python3 tools/presentation_review.py batch --attacker manki --stage splash_deck \\
      --output tmp/vfx/manki/before > tmp/vfx/manki/before.json
  bun scripts/unity-editor-gateway.ts --project-path client/Unity \\
      --batch tmp/vfx/manki/before.json > tmp/vfx/manki/before.out
  python3 tools/presentation_review.py summary tmp/vfx/manki/before.out
  python3 tools/presentation_review.py sheet tmp/vfx/manki/before/4 --frames 30-60:3 --out sheet.jpg
  python3 tools/presentation_review.py clips --set TODAY=tmp/vfx/manki/before \\
      --set NEW=tmp/vfx/manki/after --moves 1,4@28+48,A --out tmp/vfx/manki/page
  python3 tools/presentation_review.py page tmp/vfx/manki/page --spec tmp/vfx/manki/page.json
"""
from __future__ import annotations

import argparse
import html
import json
import math
import subprocess
import sys
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
FONT = ROOT / "client/Unity/Assets/Fonts/ArchivoBlack-Regular.ttf"
# Starting points per canonical ground slot: metres apart, victim percent, ticks captured
# after the press. Override per character with --set, e.g. A:distance=4, F:ticks=160,
# A:hold=50 (ticks of aiming after the press, which charges aim-hold specials) or
# F:orbit=70 (degrees the cameras orbit toward the attacker's back, for the gameplay angle).
MOVE_DEFAULTS = {
    "1": {"distance": 1.3, "damage": 40, "ticks": 50},
    "2": {"distance": 1.3, "damage": 40, "ticks": 50},
    "3": {"distance": 1.3, "damage": 40, "ticks": 50},
    "4": {"distance": 1.3, "damage": 60, "ticks": 90},
    "A": {"distance": 4.0, "damage": 40, "ticks": 50},
    "E": {"distance": 1.3, "damage": 40, "ticks": 70},
    "R": {"distance": 1.5, "damage": 40, "ticks": 70},
    "F": {"distance": 1.3, "damage": 60, "ticks": 140},
}
SPEEDS = (("quarter", 4, 15), ("real", 1, 30))  # name, slow-down, output fps


def number(value: float) -> str:
    return f"{value:g}"


def parse_frames(text: str) -> list[int]:
    """'10,12' or '30-60:3' (inclusive range with a step) → frame numbers."""
    frames: list[int] = []
    for part in text.split(","):
        span, _, step = part.partition(":")
        first, _, last = span.partition("-")
        start = int(first)
        frames.extend(range(start, int(last or first) + 1, int(step or 1)))
    return frames


def build_batch(attacker: str, victim: str, stage: str, output: str, moves: list[str],
                overrides: list[str], preset: str, size: str, track: bool) -> dict:
    settings = {move: dict(MOVE_DEFAULTS.get(move, MOVE_DEFAULTS["1"])) for move in moves}
    for override in overrides:
        move, _, assignment = override.partition(":")
        key, _, value = assignment.partition("=")
        if move not in settings or key not in ("distance", "damage", "ticks", "hold", "orbit") or not value:
            raise SystemExit(f"--set {override}: expected MOVE:distance|damage|ticks|hold|orbit=VALUE for a captured move")
        settings[move][key] = float(value) if key in ("distance", "orbit") else int(value)
    width, _, height = size.partition("x")
    commands = [["sloparena.hitfeel.prepare", "--stage", stage], ["editor_play"]]
    for move in moves:
        s = settings[move]
        command = ["sloparena.hitfeel.capture", "--output", f"{output.rstrip('/')}/{move}",
                   "--attacker", attacker, "--victim", victim, "--slot", move,
                   "--distance", number(s["distance"]), "--damage", str(s["damage"]),
                   "--ticks", str(s["ticks"]), "--presets", preset, "--width", width, "--height", height]
        if track:
            command += ["--track", "true"]
        if s.get("hold"):
            command += ["--hold", str(s["hold"])]
        if s.get("orbit"):
            command += ["--orbit", number(s["orbit"])]
        commands.append(command)
    commands.append(["get_console_logs", "--severity", "error", "--limit", "20"])
    return {"commands": commands, "restore": [["sloparena.hitfeel.stop"], ["sloparena.hitfeel.restore"]]}


def read_stream(text: str) -> list[dict]:
    """The gateway prints one JSON document per command plus status documents."""
    decoder, index, documents = json.JSONDecoder(), 0, []
    while index < len(text):
        while index < len(text) and text[index].isspace():
            index += 1
        if index >= len(text):
            break
        document, index = decoder.raw_decode(text, index)
        documents.append(document)
    return documents


def summarize(documents: list[dict]) -> tuple[list[str], bool]:
    lines, ok = [], True
    for document in documents:
        command = str(document.get("command", "")).removeprefix("command ")
        if not command:
            continue
        result = (document.get("data") or {}).get("result")
        if not document.get("success", False):
            ok = False
        if command == "sloparena.hitfeel.capture" and isinstance(result, dict):
            for run in result.get("runs", []):
                capture = run.get("result", {})
                press = capture.get("pressTick", 40)
                hits = ", ".join(
                    f"frame {hit['tick'] - press} slot {hit['slot']} {hit['damage']}% {hit['tier']}"
                    + (" blocked" if hit.get("blocked") else "") for hit in capture.get("hits", []))
                failure = capture.get("failure")
                ok = ok and not failure
                lines.append(f"capture {result.get('output')} [{run.get('preset')}]: "
                             f"{capture.get('frames')} frames; hits: {hits or 'none'}"
                             + (f"; FAILED: {failure}" if failure else ""))
        elif command == "recompile_status":
            status = json.loads(result) if isinstance(result, str) else result or {}
            ok = ok and not status.get("failed")
            lines.append(f"recompile: {status.get('status')} failed={status.get('failed')} "
                         + "; ".join(status.get("errors", [])))
        elif command == "sloparena.character.cook" and isinstance(result, dict):
            lines.append(f"cook {result.get('packageId')}: success={result.get('success')} "
                         f"content={str(result.get('cookedContentHash'))[:12]}")
        elif command == "sloparena.character.verify" and isinstance(result, dict):
            inspection = result.get("inspection", {})
            ok = ok and inspection.get("status") == "valid"
            lines.append(f"verify {result.get('packageId')}: {inspection.get('status')} "
                         f"stale={inspection.get('dirtyOrStale')} diagnostics={len(result.get('diagnostics', []))}")
        elif command == "get_console_logs" and isinstance(result, dict):
            logs = result.get("logs", [])
            severity = document.get("data", {}).get("parameters", {}).get("severity", "")
            if logs and severity == "error":
                ok = False
            groups = Counter((log.get("type"), log.get("message", "")[:120]) for log in logs)
            lines.append(f"console {severity or 'logs'}: {result.get('total', len(logs))}"
                         + "".join(f"\n  {count}× {kind}: {message}" for (kind, message), count in groups.most_common(8)))
        elif command.startswith("sloparena.presentation.") and isinstance(result, dict):
            warnings = result.get("warnings", [])
            lines.append(f"{command}: success={result.get('success')} bindings={len(result.get('bindings', []))}"
                         + "".join(f"\n  warning: {warning}" for warning in warnings))
        elif command == "eval":
            value = result.get("result") if isinstance(result, dict) else result
            error = result.get("error") if isinstance(result, dict) else None
            ok = ok and not error
            lines.append(f"eval: {error or str(value)[:300]}")
        else:
            lines.append(f"{command}: success={document.get('success')}")
    return lines, ok


def sheet_command(directory: Path, frames: list[int], columns: int, width: int, out: Path) -> list[str]:
    rows = math.ceil(len(frames) / columns)
    select = "+".join(f"eq(n,{frame})" for frame in frames)
    # Frames are 60 Hz ticks, so t*60 labels each tile with its frame number.
    label = (f"drawtext=fontfile={FONT}:text='%{{eif\\:round(t*60)\\:d}}':x=8:y=6:fontsize=28"
             ":fontcolor=white:borderw=3:bordercolor=black@0.85")
    return ["ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-framerate", "60", "-start_number", "0",
            "-i", str(directory / "%03d.jpg"),
            "-vf", f"select='{select}',{label},scale={width}:-2,"
                   f"tile={columns}x{rows}:margin=4:padding=4:color=0x1b1a18",
            "-frames:v", "1", "-q:v", "3", str(out)]


def clip_command(sets: list[tuple[str, Path]], start: int, count: int, slow: int, fps: int,
                 cell: tuple[int, int], quality: int, out: Path) -> list[str]:
    inputs, chains = [], []
    width, height = cell
    for index, (label, frames) in enumerate(sets):
        inputs += ["-framerate", "60", "-start_number", str(start), "-i", str(frames / "%03d.jpg")]
        chains.append(f"[{index}:v]trim=end_frame={count},scale={width}:{height},"
                      f"drawtext=fontfile={FONT}:text='{label}':x=10:y=8:fontsize={height // 11}"
                      f":fontcolor=white:borderw=3:bordercolor=black@0.85[v{index}]")
    labelled = "".join(f"[v{index}]" for index in range(len(sets)))
    combine = f"hstack=inputs={len(sets)}" if len(sets) > 1 else "null"
    graph = ";".join(chains) + f";{labelled}{combine},setpts={slow}*PTS,fps={fps}"
    return ["ffmpeg", "-hide_banner", "-loglevel", "error", "-y", *inputs, "-filter_complex", graph,
            "-c:v", "libwebp_anim", "-q:v", str(quality), "-loop", "0", str(out)]


def frames_dir(root: Path, move: str, preset: str, view: str) -> Path:
    return root / move / preset / view


def write_page(page: Path, spec: dict, absolute: bool) -> Path:
    def source(name: str) -> str:
        return str((page / name).resolve()) if absolute else name

    def text(value: str) -> str:
        return html.escape(value or "")

    sections, clips = [], {}
    for index, section in enumerate(spec.get("sections", [])):
        if "heading" in section:
            sections.append(f"  <h2>{text(section['heading'])}</h2>")
            continue
        meta = f"<span class=\"meta\">{text(section['meta'])}</span>" if section.get("meta") else ""
        head = f"<div class=\"head\"><h3>{text(section.get('title'))}</h3>{meta}</div>"
        note = f"\n    <p class=\"look\">{text(section['note'])}</p>" if section.get("note") else ""
        if "clip" in section:
            key = f"s{index}"
            clips[key] = {speed: source(f"{section['clip']}-{speed}.webp") for speed, _, _ in SPEEDS}
            sections.append(
                f"  <section data-clip=\"{key}\">\n    {head}\n"
                "    <div class=\"seg\"><button data-v=\"quarter\" aria-pressed=\"true\">¼×</button>"
                "<button data-v=\"real\">1×</button></div>\n"
                f"    <img class=\"clip\" alt=\"{text(section.get('title'))}\">{note}\n  </section>")
        else:
            sections.append(f"  <section>\n    {head}\n    <img class=\"clip\" src=\"{text(source(section['image']))}\" "
                            f"alt=\"{text(section.get('title'))}\">{note}\n  </section>")
    document = f"""<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>{text(spec.get('title', 'Presentation review'))}</title>
<style>
  body {{ margin: 0; font-family: var(--font-sans, system-ui, sans-serif); color: var(--foreground, #1d1b20); }}
  .wrap {{ display: flex; flex-direction: column; gap: 24px; }}
  .lede, .look, .meta {{ color: var(--muted-foreground, #5f5a66); }}
  .lede {{ margin: 0; font-size: 14px; line-height: 1.5; }}
  section {{ display: flex; flex-direction: column; gap: 8px; }}
  .head {{ display: flex; flex-wrap: wrap; align-items: baseline; gap: 4px 10px; }}
  h2 {{ margin: 6px 0 0; font-size: 16px; }}
  h3 {{ margin: 0; font-size: 15px; }}
  .meta {{ font-size: 12.5px; }}
  .seg {{ display: inline-flex; align-self: flex-start; border: 1px solid var(--border, #c9c4cf); border-radius: var(--radius, 8px); overflow: hidden; }}
  .seg button {{ font: inherit; font-size: 12.5px; padding: 4px 10px; border: 0; background: transparent; color: inherit; cursor: pointer; }}
  .seg button + button {{ border-left: 1px solid var(--border, #c9c4cf); }}
  .seg button[aria-pressed="true"] {{ background: var(--secondary, #e8e4ee); font-weight: 600; }}
  img.clip {{ display: block; width: 100%; height: auto; border-radius: var(--radius, 8px); background: #17131b; }}
  .look {{ margin: 0; font-size: 13px; line-height: 1.45; }}
</style>
</head>
<body>
<div class="wrap">
  <p class="lede">{text(spec.get('lede'))}</p>
{chr(10).join(sections)}
</div>
<script>
const clips = {json.dumps(clips, ensure_ascii=False)};
for (const section of document.querySelectorAll('section[data-clip]')) {{
  const img = section.querySelector('img.clip'), seg = section.querySelector('.seg');
  const show = speed => {{ img.src = clips[section.dataset.clip][speed]; }};
  seg.addEventListener('click', event => {{
    const button = event.target.closest('button'); if (!button) return;
    for (const other of seg.querySelectorAll('button')) other.setAttribute('aria-pressed', String(other === button));
    show(button.dataset.v);
  }});
  show('quarter');
}}
</script>
</body>
</html>
"""
    out = page / "index.html"
    out.write_text(document, encoding="utf-8")
    return out


def run(command: list[str]) -> None:
    try:
        subprocess.run(command, check=True)
    except FileNotFoundError:
        raise SystemExit("ffmpeg is required for sheets and clips.")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    commands = parser.add_subparsers(dest="command", required=True)

    batch = commands.add_parser("batch", help="write a gateway batch that captures a move matrix")
    batch.add_argument("--attacker", required=True, help="roster package ID")
    batch.add_argument("--victim", default="manki", help="roster package ID of the idle victim")
    batch.add_argument("--stage", default="splash_deck", help="baked PvP stage key, e.g. splash_deck or after_hours")
    batch.add_argument("--output", required=True, help="fresh repository-relative directory below tmp/")
    batch.add_argument("--moves", default="1,2,3,4,A,E,R,F", help="comma list of ground slots")
    batch.add_argument("--set", action="append", default=[], metavar="MOVE:KEY=VALUE",
                       help="override distance, damage or ticks for one move")
    batch.add_argument("--preset", default="high:1.6", help="hit-feel preset name:scale (default: the shipped strength)")
    batch.add_argument("--size", default="960x540", help="frame size WxH")
    batch.add_argument("--track", action=argparse.BooleanOptionalAction, default=True,
                       help="let the wide camera follow both fighters (default on)")

    summary = commands.add_parser("summary", help="summarize a gateway batch's output; exits 1 on failures")
    summary.add_argument("output", nargs="?", help="saved stdout of the gateway run (default: stdin)")

    sheet = commands.add_parser("sheet", help="tile chosen frames of one capture into a contact sheet")
    sheet.add_argument("capture", help="a <output>/<move> capture or a directory of NNN.jpg frames")
    sheet.add_argument("--frames", required=True, help="e.g. 10,12,14 or 30-60:3")
    sheet.add_argument("--preset", default="high")
    sheet.add_argument("--view", default="close", choices=("close", "wide"))
    sheet.add_argument("--columns", type=int, default=4)
    sheet.add_argument("--width", type=int, default=480, help="tile width in pixels")
    sheet.add_argument("--out", required=True)

    clips = commands.add_parser("clips", help="labelled side-by-side WebP clips at ¼× and 1×")
    clips.add_argument("--set", action="append", required=True, metavar="LABEL=OUTPUT",
                       help="capture set root (the batch --output) and its label, left to right")
    clips.add_argument("--moves", required=True, help="comma list of MOVE[@START+COUNT], default 0+40")
    clips.add_argument("--preset", default="high")
    clips.add_argument("--view", default="close", choices=("close", "wide"))
    clips.add_argument("--cell", default="560x315", help="size of each set's panel WxH")
    clips.add_argument("--quality", type=int, default=50, help="WebP quality; lower it if a page grows too large")
    clips.add_argument("--name", help="file name stem when one move is assembled (default: the move)")
    clips.add_argument("--out", required=True, help="page directory")

    page = commands.add_parser("page", help="write <page>/index.html for the clips")
    page.add_argument("page", help="page directory holding <clip>-quarter.webp and <clip>-real.webp")
    page.add_argument("--spec", required=True,
                      help='JSON: {"title", "lede", "sections": [{"clip"|"image", "title", "meta", "note"} | {"heading"}]}')
    page.add_argument("--absolute", action="store_true", help="absolute image paths, for renderers that inline local files")

    args = parser.parse_args(argv)
    if args.command == "batch":
        moves = [move.strip() for move in args.moves.split(",") if move.strip()]
        batch = build_batch(args.attacker, args.victim, args.stage, args.output, moves,
                            args.set, args.preset, args.size, args.track)
        rows = ",\n".join("  " + json.dumps(command) for command in batch["commands"])
        print(f'{{"commands": [\n{rows}],\n "restore": {json.dumps(batch["restore"])}}}')
    elif args.command == "summary":
        text = Path(args.output).read_text(encoding="utf-8") if args.output else sys.stdin.read()
        lines, ok = summarize(read_stream(text))
        print("\n".join(lines))
        return 0 if ok else 1
    elif args.command == "sheet":
        directory = Path(args.capture)
        if not (directory / "000.jpg").is_file():
            directory = directory / args.preset / args.view
        out = Path(args.out)
        out.parent.mkdir(parents=True, exist_ok=True)
        run(sheet_command(directory, parse_frames(args.frames), args.columns, args.width, out))
        print(out)
    elif args.command == "clips":
        sets = []
        for entry in args.set:
            label, _, root = entry.partition("=")
            if not root or any(character in label for character in "':\\"):
                raise SystemExit(f"--set {entry}: expected LABEL=OUTPUT without quotes, colons or backslashes")
            sets.append((label, Path(root)))
        width, _, height = args.cell.partition("x")
        out_dir = Path(args.out)
        out_dir.mkdir(parents=True, exist_ok=True)
        for spec in args.moves.split(","):
            move, _, window = spec.strip().partition("@")
            first, _, length = window.partition("+")
            start, count = int(first or 0), int(length or 40)
            frame_sets = [(label, frames_dir(root, move, args.preset, args.view)) for label, root in sets]
            for label, frames in frame_sets:
                if not (frames / f"{start:03d}.jpg").is_file():
                    raise SystemExit(f"{label}: no frame {start:03d}.jpg in {frames}")
            stem = args.name or move
            for speed, slow, fps in SPEEDS:
                out = out_dir / f"{stem}-{speed}.webp"
                run(clip_command(frame_sets, start, count, slow, fps, (int(width), int(height)), args.quality, out))
                print(f"{out} {out.stat().st_size / 1048576:.2f} MB")
    elif args.command == "page":
        spec = json.loads(Path(args.spec).read_text(encoding="utf-8"))
        print(write_page(Path(args.page), spec, args.absolute))
    return 0


if __name__ == "__main__":
    sys.exit(main())
