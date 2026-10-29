#!/usr/bin/env python3
"""Capture one Training take per move, then build synchronized review media.

  python3 tools/move_review.py run nyx --moves 'real-g1,fake-*'
  python3 tools/move_review.py media tmp/moves/nyx/<run>

Unity owns recipe semantics. Python reads only recipe move ids and consumes the
versioned plan, completed take receipts, frame receipts and numbered JPGs.
"""
from __future__ import annotations

import argparse
from dataclasses import dataclass
from datetime import datetime, timezone
import fnmatch
import html
import json
import math
from pathlib import Path
import re
import subprocess
import sys
import time

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
ID = re.compile(r"[a-z0-9][a-z0-9.-]{0,31}\Z")
BACKGROUND = (19, 23, 31)
INK = (233, 238, 247)
ACCENT = (158, 207, 255)


@dataclass(frozen=True)
class View:
    name: str
    yaw: float
    pitch: float


@dataclass(frozen=True)
class Phase:
    name: str
    at: str
    frame: int | None


@dataclass(frozen=True)
class Hit:
    frame: int
    damage: float
    blocked: bool
    target: str


@dataclass(frozen=True)
class Box:
    x0: int
    y0: int
    x1: int
    y1: int


@dataclass(frozen=True)
class PlannedMove:
    id: str
    label: str
    slot: str
    form: int
    ticks: int
    size: tuple[int, int]


@dataclass(frozen=True)
class CoverageSlot:
    alias_of: str | None
    moves: tuple[str, ...]


@dataclass(frozen=True)
class Plan:
    package: str
    recipe: str
    sha256: str
    form_names: dict[int, str]
    moves: tuple[PlannedMove, ...]
    coverage: dict[int, dict[str, CoverageSlot]]


@dataclass(frozen=True)
class Take:
    id: str
    label: str
    slot: str
    form: int
    form_name: str
    views: tuple[View, ...]
    phases: tuple[Phase, ...]
    size: tuple[int, int]
    ticks: int
    start: int | None
    hits: tuple[Hit, ...]
    boxes: tuple[dict[str, Box], ...]
    match_ticks: tuple[int, ...]
    problems: tuple[str, ...]
    root: Path

    def image(self, view: str, frame: int) -> Path:
        return self.root / view / f"{frame:04d}.jpg"


@dataclass(frozen=True)
class Run:
    root: Path
    plan: Plan
    takes: tuple[Take, ...]
    missing: tuple[str, ...]


def read_json(path: Path):
    def unique(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise ValueError(f"{path}: duplicate key {key!r}")
            result[key] = value
        return result
    return json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=unique)


def write_json(path: Path, value) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".writing")
    temporary.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    temporary.replace(path)


def require(ok: bool, message: str) -> None:
    if not ok:
        raise ValueError(message)


def stable_id(value) -> str:
    require(isinstance(value, str) and ID.fullmatch(value) is not None, f"Invalid file id {value!r}")
    return value


def integer(value, low: int, high: int) -> int:
    require(type(value) is int and low <= value <= high, f"Expected integer {low}..{high}, got {value!r}")
    return value


def number(value) -> float:
    require(type(value) in (int, float) and math.isfinite(value), f"Expected finite number, got {value!r}")
    return float(value)


def frame_size(value) -> tuple[int, int]:
    require(isinstance(value, list) and len(value) == 2, "Expected a two-component frame size")
    size = tuple(integer(v, 2, 3840) for v in value)
    require(all(v % 2 == 0 for v in size), "Frame size must have even sides")
    return size


def recipe_move_ids(recipe: Path) -> list[str]:
    ids = [stable_id(move["id"]) for move in read_json(recipe)["moves"]]
    require(bool(ids) and len(set(ids)) == len(ids), "Recipe move ids must be nonempty and unique")
    return ids


def select(ids: list[str], patterns: str | None) -> list[str]:
    if patterns is None:
        return ids
    wanted = patterns.split(",")
    for pattern in wanted:
        require(bool(pattern) and any(fnmatch.fnmatchcase(i, pattern) for i in ids), f"No move matches {pattern!r}")
    return [i for i in ids if any(fnmatch.fnmatchcase(i, pattern) for pattern in wanted)]


def build_batch(recipe: str, run: str, ids: list[str], views: str | None = None,
                size: str | None = None, quality: int | None = None) -> dict:
    common = ([] if views is None else ["--views", views]) + ([] if size is None else ["--size", size])
    prepare = ["sloparena.moves.prepare", "--recipe", recipe, "--output", run, "--moves", ",".join(ids)] + common
    commands = [prepare, ["editor_play"]]
    for move in ids:
        command = ["sloparena.moves.take", "--recipe", recipe, "--move", move,
                   "--output", f"{run}/takes/{move}"] + common
        if quality is not None:
            command += ["--quality", str(quality)]
        commands.append(command + ["--timeout", "180"])
    commands.append(["get_console_logs", "--severity", "error", "--limit", "30"])
    return {"commands": commands, "restore": [["sloparena.moves.abort"], ["sloparena.photo.stop"],
                                               ["sloparena.photo.restore"], ["editor_status"]]}


def run_gateway(batch: Path, root: Path, ids: list[str]) -> int:
    command = ["bun", "scripts/unity-editor-gateway.ts", "--project-path", "client/Unity", "--batch", str(batch)]
    reported = set()
    with (root / "gateway.out").open("w") as output, (root / "gateway.err").open("w") as errors:
        process = subprocess.Popen(command, cwd=ROOT, stdout=output, stderr=errors)
        try:
            while True:
                for move in ids:
                    receipt = root / "takes" / move / "take.json"
                    if move not in reported and receipt.exists():
                        data = read_json(receipt)
                        print(f"take {move}: {data['ticks']} frames x {len(data['views'])} views", flush=True)
                        reported.add(move)
                try:
                    return process.wait(timeout=0.5)
                except subprocess.TimeoutExpired:
                    pass
        except BaseException:
            process.terminate()
            process.wait()
            raise


def load_take(path: Path, planned: PlannedMove, plan: Plan) -> Take:
    data = read_json(path / "take.json")
    require(data["format"] == 1 and data["move"] == planned.id, f"{path}: invalid take identity or format")
    require(data["recipeSha256"] == plan.sha256 and data["recipe"] == plan.recipe
            and data["package"] == plan.package, f"{path}: take differs from the prepared recipe")
    size = frame_size(data["size"])
    ticks = integer(data["ticks"], 1, 600)
    require((data["slot"], data["form"], data["label"], ticks, size) ==
            (planned.slot, planned.form, planned.label, planned.ticks, planned.size), f"{path}: take differs from plan")
    require(data["formName"] == plan.form_names[planned.form], f"{path}: form name differs from plan")
    views = tuple(View(stable_id(v["name"]), number(v["yaw"]), number(v["pitch"])) for v in data["views"])
    names = [v.name for v in views]
    require(1 <= len(names) <= 10 and len(set(names)) == len(names), f"{path}: invalid views")
    require(data["framesWritten"] == ticks * len(views), f"{path}: incomplete image count")
    phases = tuple(Phase(p["name"], p["at"], None if p["frame"] is None else integer(p["frame"], 0, ticks - 1))
                   for p in data["phases"])
    require(1 <= len(phases) <= 6 and len({p.name for p in phases}) == len(phases), f"{path}: invalid phases")
    hits = []
    for hit in data["hits"]:
        require(type(hit["blocked"]) is bool, f"{path}: invalid blocked flag")
        hits.append(Hit(integer(hit["frame"], 0, ticks - 1), number(hit["damage"]), hit["blocked"], hit["target"]))
    start = None if data["start"] is None else integer(data["start"]["frame"], 0, ticks - 1)
    require(isinstance(data["problems"], list) and all(isinstance(p, str) for p in data["problems"]), f"{path}: invalid problems")
    boxes, match_ticks, frame_hits = [], [], []
    with (path / "frames.jsonl").open(encoding="utf-8") as stream:
        for frame, line in enumerate(stream):
            row = json.loads(line)
            require(frame < ticks and row["frame"] == frame, f"{path}: noncontiguous frames at {frame}")
            tick = integer(row["matchTick"], 0, 2**32 - 1)
            require(not match_ticks or tick == match_ticks[-1] + 1, f"{path}: Shared tick skipped at frame {frame}")
            require(set(row["viewTicks"]) == set(names) and all(t == tick for t in row["viewTicks"].values()),
                    f"{path}: view tick mismatch at frame {frame}")
            require(set(row["boxes"]) == set(names), f"{path}: action box views differ")
            frame_boxes = {}
            for name in names:
                value = row["boxes"][name]
                require(isinstance(value, list) and len(value) == 4, f"{path}: invalid action box")
                x0, y0, x1, y1 = [integer(v, 0, max(size)) for v in value]
                require(0 <= x0 < x1 <= size[0] and 0 <= y0 < y1 <= size[1], f"{path}: action box outside image")
                frame_boxes[name] = Box(x0, y0, x1, y1)
            for hit in row["hits"]:
                if hit["owner"] == row["fighter"]["id"]:
                    frame_hits.append(Hit(frame, number(hit["damage"]), hit["blocked"], hit["target"]))
            boxes.append(frame_boxes)
            match_ticks.append(tick)
    require(len(match_ticks) == ticks, f"{path}: incomplete frame receipts")
    require(tuple(frame_hits) == tuple(hits), f"{path}: hit receipt differs from recorded ticks")
    expected = [f"{frame:04d}.jpg" for frame in range(ticks)]
    for name in names:
        require(sorted(p.name for p in (path / name).glob("*.jpg")) == expected, f"{path}/{name}: missing or extra JPGs")
        for filename in expected:
            with Image.open(path / name / filename) as image:
                require(image.size == size and image.format == "JPEG", f"{path}/{name}/{filename}: invalid image size or format")
                image.verify()
    return Take(planned.id, planned.label, planned.slot, planned.form, data["formName"], views, phases, size,
                ticks, start, tuple(hits), tuple(boxes), tuple(match_ticks), tuple(data["problems"]), path)


def load_run(root: Path) -> Run:
    root = root.resolve()
    data = read_json(root / "plan.json")
    require(data["format"] == 1, "Unsupported plan format")
    moves = tuple(PlannedMove(stable_id(m["id"]), m["label"], m["slot"], integer(m["form"], 0, 1),
                              integer(m["ticks"], 1, 600), frame_size(m["size"])) for m in data["moves"])
    require(bool(moves) and len({m.id for m in moves}) == len(moves), "Plan move ids must be nonempty and unique")
    forms = {integer(int(k), 0, 1): v for k, v in data["formNames"].items()}
    coverage = {int(form): {slot: CoverageSlot(value["aliasOf"], tuple(value["moves"])) for slot, value in slots.items()}
                for form, slots in data["coverage"].items()}
    plan = Plan(stable_id(data["package"]), data["recipe"], data["recipeSha256"], forms, moves, coverage)
    takes, missing = [], []
    for move in moves:
        path = root / "takes" / move.id
        if not (path / "take.json").exists():
            missing.append(move.id)
        else:
            takes.append(load_take(path, move, plan))
    require(not takes or all(tuple(v.name for v in t.views) == tuple(v.name for v in takes[0].views) for t in takes),
            "Completed takes must use the same view order for synchronized montages")
    return Run(root, plan, tuple(takes), tuple(missing))


def font(size: int):
    for path in (ROOT / "client/Unity/Assets/Fonts/ArchivoBlack-Regular.ttf", Path("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf")):
        if path.exists():
            return ImageFont.truetype(str(path), size)
    return ImageFont.load_default()


def wrapped(draw: ImageDraw.ImageDraw, text: str, xy: tuple[int, int], width: int, size: int, lines: int = 2) -> int:
    face = font(size)
    words, line, result = text.split(), "", []
    for word in words:
        candidate = f"{line} {word}".strip()
        if line and draw.textlength(candidate, font=face) > width:
            result.append(line)
            line = word
        else:
            line = candidate
    result.append(line)
    for index, line in enumerate(result[:lines]):
        draw.text((xy[0], xy[1] + index * (size + 5)), line, font=face, fill=INK)
    return xy[1] + min(lines, len(result)) * (size + 5)


def crop_box(box: Box, size: tuple[int, int]) -> tuple[int, int, int, int]:
    unit = max(1, math.ceil((box.x1 - box.x0) * 1.2 / 4), math.ceil((box.y1 - box.y0) * 1.2 / 3))
    # A spread-out launch still needs a zoom; the full frame retains the outer action.
    unit = min(unit, max(1, int(min(size[0] / 4, size[1] / 3) * 0.9)))
    w, h = 4 * unit, 3 * unit
    x = min(max(0, round((box.x0 + box.x1 - w) / 2)), size[0] - w)
    y = min(max(0, round((box.y0 + box.y1 - h) / 2)), size[1] - h)
    return x, y, x + w, y + h


def inset(take: Take, view: str, phase: Phase, size: tuple[int, int]) -> tuple[Image.Image, tuple | None]:
    if phase.frame is None:
        image = Image.new("RGB", size, BACKGROUND)
        ImageDraw.Draw(image).text((12, 12), "Unresolved anchor", font=font(18), fill=ACCENT)
        return image, None
    box = crop_box(take.boxes[phase.frame][view], take.size)
    with Image.open(take.image(view, phase.frame)) as source:
        return source.convert("RGB").crop(box).resize(size, Image.Resampling.NEAREST), box


def hit_text(take: Take) -> str:
    return ", ".join(f"f{h.frame} {h.damage:g}%{' blocked' if h.blocked else ''}" for h in take.hits) or "no accepted hits"


def make_sheet(take: Take, out: Path) -> Path:
    width, full_h, inset_h, gap, title_h, label_h = 480, round(480 * take.size[1] / take.size[0]), 360, 8, 100, 34
    row_h = full_h + inset_h + label_h + gap
    sheet = Image.new("RGB", (len(take.phases) * (width + gap) + gap, title_h + len(take.views) * row_h + gap), BACKGROUND)
    draw = ImageDraw.Draw(sheet)
    draw.text((8, 8), f"{take.label} | {take.slot} | {take.form_name}", font=font(24), fill=INK)
    wrapped(draw, f"start f{take.start} | {hit_text(take)} | {take.ticks} frames at 60 Hz", (8, 44), sheet.width - 16, 18)
    crops = []
    for row, view in enumerate(take.views):
        for col, phase in enumerate(take.phases):
            x, y = gap + col * (width + gap), title_h + row * row_h
            label = f"{view.name} | {phase.name} | " + ("unresolved" if phase.frame is None else f"f{phase.frame} / tick {take.match_ticks[phase.frame]}")
            draw.text((x, y + 4), label, font=font(16), fill=ACCENT)
            if phase.frame is not None:
                with Image.open(take.image(view.name, phase.frame)) as image:
                    sheet.paste(image.convert("RGB").resize((width, full_h), Image.Resampling.LANCZOS), (x, y + label_h))
            crop, box = inset(take, view.name, phase, (width, inset_h))
            sheet.paste(crop, (x, y + label_h + full_h))
            crops.append({"view": view.name, "phase": phase.name, "frame": phase.frame, "box": box,
                          "image": None if phase.frame is None else str(take.image(view.name, phase.frame)),
                          "actionBox": None if phase.frame is None else vars(take.boxes[phase.frame][view.name]),
                          "zoom": None if box is None else take.size[0] / (box[2] - box[0]), "resampling": "nearest"})
    out.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(out, quality=90)
    write_json(out.with_suffix(".json"), {"move": take.id, "sourceSize": take.size, "crops": crops})
    return out


def make_peak(take: Take, out: Path) -> Path:
    phase = next((p for p in take.phases if p.name.lower() == "peak"),
                 next((p for p in take.phases if p.frame is not None), take.phases[0]))
    image = Image.new("RGB", (640, math.ceil(len(take.views) / 2) * 312), BACKGROUND)
    draw = ImageDraw.Draw(image)
    for index, view in enumerate(take.views):
        x, y = index % 2 * 320, index // 2 * 312
        draw.text((x + 6, y + 3), view.name, font=font(28), fill=INK)
        draw.text((x + 6, y + 38), f"{phase.name} " + ("?" if phase.frame is None else f"f{phase.frame}"), font=font(24), fill=ACCENT)
        crop, _ = inset(take, view.name, phase, (320, 240))
        image.paste(crop, (x, y + 72))
    out.parent.mkdir(parents=True, exist_ok=True)
    image.save(out, quality=88)
    return out


def ffmpeg(args: list[str], cwd: Path) -> None:
    result = subprocess.run(["ffmpeg", "-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-filter_complex_threads", "1"] + args,
                            cwd=cwd, capture_output=True, text=True)
    if result.returncode:
        raise RuntimeError(f"ffmpeg failed in {cwd}: {result.stderr[-6000:]}")


def encode_options() -> list[str]:
    return ["-an", "-c:v", "libx264", "-preset", "veryfast", "-crf", "20", "-threads", "2",
            "-pix_fmt", "yuv420p", "-color_range", "tv", "-movflags", "+faststart"]


def make_view_clips(take: Take, out: Path) -> list[Path]:
    out.mkdir(parents=True, exist_ok=True)
    clips = []
    for view in take.views:
        label = f"{view.name}.txt"
        (out / label).write_text(f"{take.label} | {take.slot} | {take.form_name} | {view.name}", encoding="utf-8")
        text_size = max(8, min(20, take.size[0] // 40))
        filters = (f"scale=in_range=pc:out_range=tv,drawtext=textfile={label}:expansion=none:fontsize={text_size}:"
                   "fontcolor=white:box=1:boxcolor=black@0.7:x=8:y=8,"
                   f"drawtext=text='frame %{{n}}':fontsize={text_size}:fontcolor=white:box=1:boxcolor=black@0.7:x=8:y=h-th-8")
        path = out / f"{view.name}.mp4"
        ffmpeg(["-threads", "2", "-framerate", "60", "-start_number", "0", "-i", str(take.root / view.name / "%04d.jpg"),
                "-frames:v", str(take.ticks), "-vf", filters] + encode_options() + [path.name], out)
        clips.append(path)
    return clips


def make_mosaic(take: Take, out: Path) -> Path:
    tile_w, tile_h = 384, 216
    info = Image.new("RGB", (tile_w, tile_h), BACKGROUND)
    draw = ImageDraw.Draw(info)
    y = wrapped(draw, take.label, (12, 8), tile_w - 24, 19)
    y = wrapped(draw, f"{take.form_name} | {take.slot}", (12, y + 4), tile_w - 24, 15)
    wrapped(draw, f"start f{take.start} | {hit_text(take)}", (12, y + 8), tile_w - 24, 14, 3)
    info.save(out.parent / "info.png")
    inputs = []
    filters = []
    for index, view in enumerate(take.views):
        inputs += ["-threads", "2", "-i", f"{view.name}.mp4"]
        filters.append(f"[{index}:v]scale={tile_w}:{tile_h},setsar=1[v{index}]")
    inputs += ["-loop", "1", "-framerate", "60", "-i", "info.png"]
    index = len(take.views)
    info_filter = f"[{index}:v]drawtext=text='frame %{{n}}':fontsize=18:fontcolor=white:x=12:y=h-27"
    resolved = sorted((p for p in take.phases if p.frame is not None), key=lambda p: p.frame)
    for ordinal, phase in enumerate(resolved):
        filename = f"phase-{ordinal}.txt"
        (out.parent / filename).write_text(phase.name, encoding="utf-8")
        end = resolved[ordinal + 1].frame - 1 if ordinal + 1 < len(resolved) else take.ticks - 1
        info_filter += f",drawtext=textfile={filename}:expansion=none:fontsize=16:fontcolor=0x9ecfff:x=12:y=h-54:enable='between(n,{phase.frame},{end})'"
    filters.append(info_filter + f"[v{index}]")
    columns = math.ceil(math.sqrt(len(take.views) + 1))
    layout = "|".join(f"{i % columns * tile_w}_{i // columns * tile_h}" for i in range(index + 1))
    filters.append("".join(f"[v{i}]" for i in range(index + 1)) + f"xstack=inputs={index + 1}:layout={layout}:fill=0x13171f,format=yuv420p[all]")
    ffmpeg(inputs + ["-filter_complex", ";".join(filters), "-map", "[all]", "-frames:v", str(take.ticks)]
           + encode_options() + [out.name], out.parent)
    return out


def make_montage(takes: list[Take], clips: Path, out: Path) -> Path:
    out.parent.mkdir(parents=True, exist_ok=True)
    listing = out.with_suffix(".txt")
    listing.write_text("".join("file '" + str(clips / t.id / "all.mp4").replace("'", "'\\''") + "'\n" for t in takes), encoding="utf-8")
    ffmpeg(["-f", "concat", "-safe", "0", "-i", listing.name, "-map", "0:v:0", "-c", "copy", "-movflags", "+faststart", out.name], out.parent)
    return out


def make_boards(run: Run, artifacts: dict, montages: dict[int, str], media: Path) -> list[Path]:
    board = media / "board"
    board.mkdir(parents=True, exist_ok=True)
    esc = lambda value: html.escape(str(value), quote=True)
    link = lambda path: esc(Path(path).relative_to(media).as_posix())
    css = "body{margin:0;background:#13171f;color:#e9eef7;font:16px/1.5 system-ui}main{max-width:840px;margin:auto;padding:16px}h1{font-size:26px}h2{font-size:21px}a{color:#9ecfff;overflow-wrap:anywhere}img,video{display:block;max-width:100%;height:auto;margin:12px auto}article{border-top:1px solid #526078;padding:20px 0}p,li{overflow-wrap:anywhere}.coverage{display:grid;grid-template-columns:repeat(auto-fit,minmax(145px,1fr));gap:8px}.coverage div{padding:8px;background:#202735}.problem{color:#ffd790}nav a{margin-right:12px}"
    paths = []
    navigation = "".join(f'<a href="form-{form}.html">{esc(name)}</a>' for form, name in run.plan.form_names.items())
    for form, name in run.plan.form_names.items():
        selected = [m for m in run.plan.moves if m.form == form]
        takes = [t for t in run.takes if t.form == form]
        problems = [f"{t.id}: {p}" for t in takes for p in t.problems] + [f"Missing take: {m.id}" for m in selected if m.id in run.missing]
        body = f'<nav>{navigation}</nav><h1>{esc(name)} move review</h1><p>{esc(run.plan.package)} · 60 Hz · recipe {esc(run.plan.sha256[:12])}</p>'
        body += '<h2>Problems</h2>' + ('<ul class="problem">' + ''.join(f'<li>{esc(p)}</li>' for p in problems) + '</ul>' if problems else '<p>No recorded problems.</p>')
        body += '<h2>Canonical slot coverage</h2><div class="coverage">'
        for slot, coverage in run.plan.coverage[form].items():
            state = ', '.join(coverage.moves) or 'no recipe'
            if coverage.alias_of:
                state += f" · alias of {coverage.alias_of}"
            body += f'<div>{esc(slot)}<br>{esc(state)}</div>'
        body += '</div>'
        if form in montages:
            body += f'<h2>Form montage</h2><video controls preload="none" src="{esc(montages[form])}"></video>'
        for take in takes:
            item = artifacts[take.id]
            body += (f'<article><h2>{esc(take.label)}</h2><p>{esc(take.id)} · {esc(take.slot)} · start f{take.start}<br>{esc(hit_text(take))}</p>'
                     f'<img loading="lazy" width="640" src="{esc(item["peak"])}" alt="{esc(take.label)} resolved phase in every view">'
                     f'<p><a href="../{link(item["sheet"])}">Phase sheet</a> · <a href="../{link(item["mosaic"])}">Synchronized mosaic</a> · '
                     f'<a href="../{link(item["crops"])}">Crop receipts</a></p><details><summary>View clips</summary><p>')
            body += ' · '.join(f'<a href="../{link(path)}">{esc(view)}</a>' for view, path in item['clips'].items())
            body += '</p></details></article>'
        document = f'<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>{esc(name)} moves</title><style>{css}</style><main>{body}</main></html>\n'
        require(len(document.encode('utf-8')) < 30000, f"Form {form} board exceeds 30 KB")
        path = board / f"form-{form}.html"
        path.write_text(document, encoding="utf-8")
        paths.append(path)
    (board / 'index.html').write_text(f'<!doctype html><meta name="viewport" content="width=device-width,initial-scale=1"><title>Move review</title><style>{css}</style><main><h1>{esc(run.plan.package)} move review</h1>{navigation}</main>\n', encoding='utf-8')
    return paths


def build_media(run: Run) -> dict:
    media = run.root / "media"
    artifacts = {}
    for take in run.takes:
        print(f"media {take.id}", flush=True)
        sheet = make_sheet(take, media / "sheets" / f"{take.id}.jpg")
        peak = make_peak(take, media / "board" / "img" / f"{take.id}.jpg")
        clips = make_view_clips(take, media / "clips" / take.id)
        mosaic = make_mosaic(take, media / "clips" / take.id / "all.mp4")
        artifacts[take.id] = {"sheet": str(sheet), "crops": str(sheet.with_suffix('.json')), "peak": str(peak),
                              "clips": {v.name: str(p) for v, p in zip(take.views, clips)}, "mosaic": str(mosaic)}
    montages = {}
    for form in run.plan.form_names:
        takes = [t for t in run.takes if t.form == form]
        if takes:
            montages[form] = str(make_montage(takes, media / "clips", media / "montage" / f"form-{form}.mp4"))
    boards = make_boards(run, artifacts, montages, media)
    summary = {"format": 1, "package": run.plan.package, "recipe": run.plan.recipe, "recipeSha256": run.plan.sha256,
               "complete": not run.missing, "missing": list(run.missing), "plannedMoves": len(run.plan.moves),
               "completedMoves": len(run.takes), "images": sum(t.ticks * len(t.views) for t in run.takes),
               "boards": [str(p) for p in boards], "montages": montages,
               "moves": [{"id": t.id, "label": t.label, "slot": t.slot, "form": t.form, "ticks": t.ticks,
                          "views": [v.name for v in t.views], "size": t.size, "start": t.start,
                          "hits": [vars(h) for h in t.hits], "phases": [vars(p) for p in t.phases],
                          "firstMatchTick": t.match_ticks[0], "lastMatchTick": t.match_ticks[-1],
                          "problems": list(t.problems), **artifacts[t.id]} for t in run.takes]}
    write_json(run.root / "summary.json", summary)
    return summary


def print_summary(summary: dict, root: Path) -> None:
    for path in summary["boards"] + list(summary["montages"].values()) + [str(root / "summary.json")]:
        print(path)
    for move in summary["moves"]:
        for problem in move["problems"]:
            print(f"problem {move['id']}: {problem}")
    for move in summary["missing"]:
        print(f"problem {move}: missing completed take")


def cmd_run(args) -> int:
    stable_id(args.package)
    recipe = (ROOT / args.recipe if args.recipe else ROOT / "scenarios" / "moves" / f"{args.package}.json").resolve()
    require(recipe.is_relative_to(ROOT / "tmp") or recipe.is_relative_to(ROOT / "scenarios/moves"), "Recipe must be below tmp/ or scenarios/moves/")
    ids = select(recipe_move_ids(recipe), args.moves)
    root = (ROOT / args.output if args.output else ROOT / "tmp/moves" / args.package / datetime.now(timezone.utc).strftime("%Y%m%d-%H%M%S-%f")).resolve()
    require(root.is_relative_to(ROOT / "tmp/moves"), "Run output must be below tmp/moves/")
    batch = build_batch(recipe.relative_to(ROOT).as_posix(), root.relative_to(ROOT).as_posix(), ids, args.views, args.size, args.quality)
    if args.dry_run:
        print(json.dumps(batch, indent=2))
        return 0
    root.mkdir(parents=True, exist_ok=False)
    write_json(root / "batch.json", batch)
    print(f"run {root}", flush=True)
    start = time.monotonic()
    gateway_code = run_gateway(root / "batch.json", root, ids)
    captured = time.monotonic()
    timing = {"gatewaySeconds": captured - start, "gatewayExitCode": gateway_code}
    try:
        summary = build_media(load_run(root))
        print_summary(summary, root)
        return gateway_code or (0 if summary["complete"] else 1)
    finally:
        timing["mediaSeconds"] = time.monotonic() - captured
        timing["totalSeconds"] = time.monotonic() - start
        write_json(root / "timing.json", timing)
        print(f"wall time {timing['totalSeconds']:.3f} seconds; gateway {timing['gatewaySeconds']:.3f}, media {timing['mediaSeconds']:.3f}")


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    commands = parser.add_subparsers(dest="command", required=True)
    run = commands.add_parser("run", help="Capture selected recipe moves through one gateway batch, then build media")
    run.add_argument("package")
    run.add_argument("--recipe")
    run.add_argument("--output")
    run.add_argument("--moves")
    run.add_argument("--views")
    run.add_argument("--size")
    run.add_argument("--quality", type=int, choices=range(50, 101), metavar="50..100")
    run.add_argument("--dry-run", action="store_true")
    media = commands.add_parser("media", help="Rebuild all media from an existing run without using the Editor")
    media.add_argument("run", type=Path)
    args = parser.parse_args(argv)
    try:
        if args.command == "run":
            return cmd_run(args)
        loaded = load_run(args.run)
        summary = build_media(loaded)
        print_summary(summary, loaded.root)
        return 0 if summary["complete"] else 1
    except (OSError, ValueError, KeyError, TypeError, RuntimeError) as error:
        print(f"move review: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
