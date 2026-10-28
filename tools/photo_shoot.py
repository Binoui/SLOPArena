#!/usr/bin/env python3
"""Contact sheets, stage surveys and final exports for `sloparena.photo.*` shoots.

Unity's photo shoot (client/Unity/Assets/Scripts/Editor/SlopArenaPhotoCommands.cs) writes
`<run>/<camera>/NNN.jpg` per captured tick, an optional transparent `<run>/hud/NNN.png`,
`<run>/states.jsonl` (fighter states per tick) and `<run>/survey.json`. Run from the repo root.

  python3 tools/photo_shoot.py sheet tmp/store/runs/x --camera a --frames 40-100:2 --out sheet.jpg
  python3 tools/photo_shoot.py grid tmp/store/runs/survey --camera top --step 5 --out top-grid.jpg
  python3 tools/photo_shoot.py final tmp/store/runs/x --camera a --tick 52 --out final.png
  python3 tools/photo_shoot.py final --image tmp/store/runs/live/0063.jpg --out final.png
  python3 tools/photo_shoot.py states tmp/store/runs/x --frames 40-60
  python3 tools/photo_shoot.py clip tmp/store/runs/x --camera a --from 0 --to 299 --out clip.mp4
  python3 tools/photo_shoot.py batch scenarios/store/02-splash-deck-bonk.json --output tmp/store/runs/02 > batch.json

`batch` turns a recipe from scenarios/store/ into a gateway batch: a PvP photo spec, a live Slopball
recipe or a Character Course window (replayed through the sloparena.replay.* runner). Run it with
`bun scripts/unity-editor-gateway.ts --project-path client/Unity --batch batch.json`.
"""
from __future__ import annotations

import argparse
import json
import math
import subprocess
import uuid
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
FONT = ROOT / "client/Unity/Assets/Fonts/ArchivoBlack-Regular.ttf"


def font(size: int) -> ImageFont.ImageFont:
    try:
        return ImageFont.truetype(str(FONT), size)
    except OSError:
        return ImageFont.load_default()


def parse_frames(text: str | None, available: list[int]) -> list[int]:
    """'40-100:2' or '40,52,60' → ticks that exist; empty means every available tick."""
    if not text:
        return available
    wanted: list[int] = []
    for part in text.split(","):
        span, _, step = part.partition(":")
        first, _, last = span.partition("-")
        start, end = int(first), int(last or first)
        wanted.extend(range(start, end + 1, int(step or 1)))
    have = set(available)
    return [tick for tick in wanted if tick in have]


def ticks_in(folder: Path, suffix: str) -> list[int]:
    return sorted(int(path.stem) for path in folder.glob(f"*{suffix}") if path.stem.isdigit())


def with_hud(image: Image.Image, hud_path: Path) -> Image.Image:
    if not hud_path.exists():
        return image
    hud = np.asarray(Image.open(hud_path).convert("RGBA")).astype(np.float32) / 255.0
    base = np.asarray(image).astype(np.float32) / 255.0
    alpha = hud[..., 3:4]
    # UI Toolkit renders premultiplied colour into its cleared target.
    out = hud[..., :3] + base * (1.0 - alpha)
    return Image.fromarray(np.clip(out * 255.0 + 0.5, 0, 255).astype(np.uint8))


def composite(run: Path, camera: str, tick: int) -> Image.Image:
    image = Image.open(run / camera / f"{tick:03d}.jpg").convert("RGB")
    return with_hud(image, run / "hud" / f"{tick:03d}.png")


def cmd_sheet(args: argparse.Namespace) -> None:
    run = Path(args.run)
    folder = run / args.camera
    frames = parse_frames(args.frames, ticks_in(folder, ".jpg"))
    if not frames:
        raise SystemExit(f"No frames in {folder} for {args.frames!r}")
    cell_w = args.cell
    first = Image.open(folder / f"{frames[0]:03d}.jpg")
    cell_h = round(cell_w * first.height / first.width)
    cols = min(args.cols, len(frames))
    rows = math.ceil(len(frames) / cols)
    sheet = Image.new("RGB", (cols * cell_w + (cols + 1) * 4, rows * cell_h + (rows + 1) * 4), (18, 18, 18))
    label = font(max(14, cell_w // 18))
    draw = ImageDraw.Draw(sheet)
    for index, tick in enumerate(frames):
        image = composite(run, args.camera, tick) if args.hud else Image.open(folder / f"{tick:03d}.jpg").convert("RGB")
        image = image.resize((cell_w, cell_h), Image.LANCZOS)
        x = 4 + (index % cols) * (cell_w + 4)
        y = 4 + (index // cols) * (cell_h + 4)
        sheet.paste(image, (x, y))
        draw.rectangle((x, y, x + label.size * 3 + 8, y + label.size + 8), fill=(0, 0, 0))
        draw.text((x + 4, y + 2), str(tick), font=label, fill=(255, 220, 60))
    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(out, quality=90)
    print(f"{out} ({len(frames)} frames)")


def cmd_grid(args: argparse.Namespace) -> None:
    """Overlay world X/Z gridlines on a top-down orthographic survey camera."""
    run = Path(args.run)
    survey = json.loads((run / "survey.json").read_text())
    camera = next(c for c in survey["cameras"] if c["name"] == args.camera)
    if not camera["ortho"]:
        raise SystemExit("grid needs an orthographic camera looking straight down with up = +Z")
    tick = ticks_in(run / args.camera, ".jpg")[0]
    image = Image.open(run / args.camera / f"{tick:03d}.jpg").convert("RGB")
    draw = ImageDraw.Draw(image, "RGBA")
    w, h = image.size
    half_h = camera["ortho"]
    half_w = half_h * camera["aspect"]
    cx, _, cz = camera["position"]

    def to_px(x: float, z: float) -> tuple[float, float]:
        return ((x - cx + half_w) / (2 * half_w) * w, (cz + half_h - z) / (2 * half_h) * h)

    label = font(max(12, w // 90))
    step = args.step
    for x in range(math.floor((cx - half_w) / step) * step, math.ceil(cx + half_w) + 1, step):
        px, _ = to_px(x, 0)
        draw.line((px, 0, px, h), fill=(255, 255, 255, 200 if x == 0 else 90), width=3 if x == 0 else 1)
        draw.text((px + 3, 3), f"x{x}", font=label, fill=(255, 230, 80, 255))
    for z in range(math.floor((cz - half_h) / step) * step, math.ceil(cz + half_h) + 1, step):
        _, pz = to_px(0, z)
        draw.line((0, pz, w, pz), fill=(255, 255, 255, 200 if z == 0 else 90), width=3 if z == 0 else 1)
        draw.text((3, pz + 2), f"z{z}", font=label, fill=(120, 230, 255, 255))
    for index, spawn in enumerate(survey.get("spawns", [])):
        px, pz = to_px(spawn["x"], spawn["z"])
        draw.ellipse((px - 9, pz - 9, px + 9, pz + 9), outline=(255, 60, 60, 255), width=3)
        yaw = math.radians(spawn["yaw"])
        draw.line((px, pz, px + 26 * math.sin(yaw), pz - 26 * math.cos(yaw)), fill=(255, 60, 60, 255), width=3)
        draw.text((px + 11, pz - 11), f"S{index}", font=label, fill=(255, 90, 90, 255))
    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    image.save(out, quality=92)
    print(out)


def cmd_final(args: argparse.Namespace) -> None:
    if args.image:
        image = Image.open(args.image).convert("RGB")
    elif args.run and args.tick is not None:
        image = composite(Path(args.run), args.camera, args.tick)
    else:
        raise SystemExit("final needs either --image or a run with --tick")
    if args.crop:
        left, top, width = (float(v) for v in args.crop.split(","))
        w, h = image.size
        box_w = width * w
        box_h = box_w * 9 / 16
        x0, y0 = left * w, top * h
        image = image.crop((round(x0), round(y0), round(x0 + box_w), round(y0 + box_h)))
    image = image.resize((args.width, args.height), Image.LANCZOS)
    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    image.save(out, optimize=True)
    print(f"{out} {image.size[0]}x{image.size[1]}")


def cmd_clip(args: argparse.Namespace) -> None:
    run = Path(args.run)
    frames = sorted((path for path in (run / args.camera).glob("*.jpg") if path.stem.isdigit()),
                    key=lambda path: int(path.stem))
    frames = [path for path in frames if args.start <= int(path.stem) <= args.end]
    if not frames:
        raise SystemExit(f"no frames in {run / args.camera} between {args.start} and {args.end}")
    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    size = f"{args.width}x{args.height}"
    # Browsers and phones refuse full-range yuvj420p, so encode limited-range yuv420p.
    encoder = subprocess.Popen(
        ["ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", size,
         "-r", str(args.fps), "-i", "-", "-c:v", "libx264", "-preset", "slow", "-crf", str(args.crf),
         "-pix_fmt", "yuv420p", "-movflags", "+faststart", str(out)], stdin=subprocess.PIPE)
    assert encoder.stdin is not None
    for path in frames:
        image = Image.open(path).convert("RGB")
        if args.hud:
            image = with_hud(image, run / "hud" / f"{path.stem}.png")
        if image.size != (args.width, args.height):
            image = image.resize((args.width, args.height), Image.LANCZOS)
        encoder.stdin.write(image.tobytes())
    encoder.stdin.close()
    if encoder.wait() != 0:
        raise SystemExit("ffmpeg failed")
    print(f"{out} {len(frames)} frames, {len(frames) / args.fps:.2f} s at {args.fps} fps")


def cmd_states(args: argparse.Namespace) -> None:
    rows = [json.loads(line) for line in (Path(args.run) / "states.jsonl").read_text().splitlines() if line]
    ticks = parse_frames(args.frames, [row["tick"] for row in rows])
    wanted = set(ticks)
    for row in rows:
        if row["tick"] not in wanted:
            continue
        if "fighters" not in row:  # live Arcade captures log one fighter plus ball or course fields
            print(f"{row['tick']:5d}  " + json.dumps({k: v for k, v in row.items() if k != "tick"}))
            continue
        parts = [
            f"{f['id']}:{f['state'][:10]:<10} s{f['slot']:<2} ({f['x']:6.2f},{f['y']:6.2f},{f['z']:6.2f}) "
            f"{'G' if f['grounded'] else 'A'} {f['damage']:3d}% hs{f['hitstun']}"
            for f in row["fighters"]
        ]
        print(f"{row['tick']:4d}  " + " | ".join(parts))


# One-shot press fields: a step split across replay chunks must not press them twice.
EDGES = ("jumpPressed", "downPressed", "shieldPressed", "grabPressed", "faceToCamera", "toggleLock", "retargetPressed", "slot")


def slice_steps(steps: list[dict], start: int, end: int) -> list[dict]:
    """Ticks [start, end) of a Training step list, as scripts/sloparena-replay.ts sliceSteps."""
    tick, result = 0, []
    for step in steps:
        following = tick + step["ticks"]
        if following > start and tick < end:
            part = dict(step, ticks=min(following, end) - max(tick, start))
            if start > tick:
                for key in EDGES:
                    part.pop(key, None)
            result.append(part)
        tick = following
        if tick >= end:
            break
    return result


def cmd_batch(args: argparse.Namespace) -> None:
    recipe_path = Path(args.recipe)
    recipe = json.loads(recipe_path.read_text())
    output = args.output.rstrip("/")
    logs = ["get_console_logs", "--severity", "error", "--limit", "20"]
    if "fighters" in recipe:  # PvP photo spec
        commands = [["sloparena.photo.prepare", "--stage", recipe["stage"]], ["editor_play"],
                    ["sloparena.photo.shoot", "--spec", str(recipe_path), "--output", output, "--timeout", "300"], logs]
        restore = [["sloparena.photo.stop"], ["sloparena.photo.restore"]]
    elif "live" in recipe:  # live Slopball
        live = recipe["live"]
        command = ["sloparena.photo.live", "--output", output, "--intercept", json.dumps(live["intercept"]),
                   "--step", str(live.get("step", 2)), "--quality", str(live.get("quality", 94))]
        if "camera" in live:
            command += ["--camera", json.dumps(live["camera"])]
        commands = [recipe["prepare"], ["editor_play"], recipe["viewport"], command, logs]
        restore = [["sloparena.photo.live-end", "--wait", "0"], ["sloparena.photo.stop"], ["sloparena.photo.restore"]]
    elif "window" in recipe:  # Character Course window through the replay runner
        steps = json.loads((recipe_path.parent / recipe["inputs"]).read_text())
        start, end = recipe["window"]["from"], recipe["window"]["to"]
        session = uuid.uuid4().hex
        chunks: list[list[dict]] = []
        commands = [["sloparena.replay.prepare", "--session", session, "--stage", recipe["stage"],
                     "--fighter", recipe["fighter"], "--output", output], ["editor_play"],
                    ["sloparena.ui.viewport", "--width", "1920", "--height", "1080", "--gizmos", "off"]]

        def run_until(stop: int, tick: int) -> int:
            while tick < stop:
                to = min(tick + 600, stop)
                chunks.append(slice_steps(steps, tick, to))
                commands.append(["sloparena.replay.run", "--session", session, "--chunk", str(len(chunks) - 1),
                                 "--from-go", "true" if tick == 0 else "false"])
                tick = to
            return tick

        tick = run_until(start, 0)
        commands += [["sloparena.ui.viewport", "--width", "3840", "--height", "2160", "--gizmos", "off"],
                     ["sloparena.photo.live-begin", "--output", f"{output}/frames", "--from", str(start), "--to", str(end),
                      "--step", "2", "--quality", "94", "--camera", json.dumps(recipe["camera"])]]
        run_until(end + 2, tick)
        commands += [["sloparena.photo.live-end", "--wait", "5"], logs]
        restore = [["sloparena.photo.live-end", "--wait", "0"], ["sloparena.replay.stop", "--session", session],
                   ["sloparena.replay.restore", "--session", session]]
        # The replay runner reads its input chunks from the fresh output directory.
        directory = ROOT / output
        directory.mkdir(parents=True, exist_ok=False)
        (directory / "chunks.json").write_text(json.dumps(chunks))
    else:
        raise SystemExit(f"{recipe_path} is not a photo spec, live Slopball recipe or course window")
    print(json.dumps({"commands": commands, "restore": restore}, indent=1, ensure_ascii=False))


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    sheet = sub.add_parser("sheet", help="contact sheet of one camera's ticks")
    sheet.add_argument("run")
    sheet.add_argument("--camera", default="a")
    sheet.add_argument("--frames")
    sheet.add_argument("--cols", type=int, default=5)
    sheet.add_argument("--cell", type=int, default=384)
    sheet.add_argument("--hud", action="store_true", help="composite the HUD layer")
    sheet.add_argument("--out", required=True)
    sheet.set_defaults(func=cmd_sheet)
    grid = sub.add_parser("grid", help="world grid over a top-down orthographic survey")
    grid.add_argument("run")
    grid.add_argument("--camera", default="top")
    grid.add_argument("--step", type=int, default=5)
    grid.add_argument("--out", required=True)
    grid.set_defaults(func=cmd_grid)
    final = sub.add_parser("final", help="composite, downscale and save one tick as PNG")
    final.add_argument("run", nargs="?")
    final.add_argument("--camera", default="a")
    final.add_argument("--tick", type=int)
    final.add_argument("--image", help="export this frame directly (live captures write <run>/NNNN.jpg)")
    final.add_argument("--crop", help="left,top,width as fractions of the frame (16:9 box)")
    final.add_argument("--width", type=int, default=1920)
    final.add_argument("--height", type=int, default=1080)
    final.add_argument("--out", required=True)
    final.set_defaults(func=cmd_final)
    clip = sub.add_parser("clip", help="encode a camera's frames as an H.264 MP4")
    clip.add_argument("run")
    clip.add_argument("--camera", default="a", help="frame folder below the run (live captures: frames)")
    clip.add_argument("--from", dest="start", type=int, default=0)
    clip.add_argument("--to", dest="end", type=int, default=10**6)
    clip.add_argument("--fps", type=int, default=60, help="60 for step-1 shoots, 30 for step-2 captures")
    clip.add_argument("--hud", action="store_true", help="composite the HUD layer")
    clip.add_argument("--width", type=int, default=1920)
    clip.add_argument("--height", type=int, default=1080)
    clip.add_argument("--crf", type=int, default=18)
    clip.add_argument("--out", required=True)
    clip.set_defaults(func=cmd_clip)
    states = sub.add_parser("states", help="print fighter states per tick")
    states.add_argument("run")
    states.add_argument("--frames")
    states.set_defaults(func=cmd_states)
    batch = sub.add_parser("batch", help="gateway batch for a scenarios/store recipe")
    batch.add_argument("recipe")
    batch.add_argument("--output", required=True, help="fresh run directory below tmp/")
    batch.set_defaults(func=cmd_batch)
    args = parser.parse_args()
    args.func(args)


if __name__ == "__main__":
    main()
