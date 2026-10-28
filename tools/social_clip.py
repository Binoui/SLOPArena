#!/usr/bin/env python3
"""Render social clip ideas from the queue as vertical 9:16 clips with the game's own sound.

Each idea is one JSON file in scenarios/social/queue/ (skills/social-clips/SKILL.md owns the workflow):

    {"id": "02-home-run", "status": "approved", "format": "moment", "title": "...", "idea": "...",
     "spec": "scenarios/social/specs/02-splash-deck-home-run.json", "fovScale": 1.1,
     "camera": {"lookOffset": [-0.2, 0.9, -0.5], "ease": 0.2}, "capture": {"to": 365}, "end": 328, "hold": 0.6}

`spec` is a photo-mode spec (docs/steam/store-screenshots.md). `fovScale` scales its lenses for portrait;
`camera` overrides fields of every spec camera; `capture` overrides the shoot window; `end` cuts the
picture before the shoot's last tick (keep shooting past a KO so the respawn stays out of view while the
audio runs on); `hold` freezes the last frame for that many seconds.

`make` reshoots the spec in portrait at 1080x1920 through the Unity Editor gateway (photo mode records
the game's sound without music), writes `tmp/content/<id>/<id>-vertical.mp4` and a contact sheet, and
marks the idea `rendered`. Music and text are added in CapCut.

    python3 tools/social_clip.py list                       # the queue by status
    python3 tools/social_clip.py make <idea.json> --probe   # cheap 540x960 framing check (~15 s of Editor)
    python3 tools/social_clip.py make <idea.json>           # full shoot, encode and mix
    python3 tools/social_clip.py mix <idea.json>            # re-encode the last full shoot
"""
from __future__ import annotations

import argparse
import json
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
QUEUE = ROOT / "scenarios/social/queue"
STATUSES = ["proposed", "approved", "rendered", "ready", "posted", "rejected"]
FPS = 60


def rel(path: Path) -> str:
    return str(path.relative_to(ROOT))


def load_job(path: str) -> tuple[dict, Path]:
    job = json.loads(Path(path).read_text())
    out = ROOT / job.get("out", f"tmp/content/{job['id']}")
    out.mkdir(parents=True, exist_ok=True)
    return job, out


def fresh(path: Path) -> Path:
    """Keep earlier shoots: an existing run moves aside to <name>-1, -2, ..."""
    if path.exists():
        n = 1
        while path.with_name(f"{path.name}-{n}").exists():
            n += 1
        for old in [path, *path.parent.glob(f"{path.name}.*")]:
            old.rename(old.with_name(old.name.replace(path.name, f"{path.name}-{n}", 1)))
    return path


def vertical_spec(job: dict, out: Path, probe: bool) -> Path:
    spec = json.loads((ROOT / job["spec"]).read_text())
    capture = spec["capture"]
    capture.update(job.get("capture", {}))
    capture.update({"step": 3, "width": 540, "height": 960, "quality": 85} if probe
                   else {"step": 1, "width": 1080, "height": 1920, "quality": 92})
    for camera in spec["cameras"]:
        camera["fov"] = round(camera.get("fov", 42) * job.get("fovScale", 1.1), 1)
        camera.update(job.get("camera", {}))
    path = out / ("spec-probe.json" if probe else "spec-vertical.json")
    path.write_text(json.dumps(spec, indent=1))
    return path


def shoot(spec: Path, run: Path) -> None:
    batch = run.parent / f"{run.name}.batch.json"
    with batch.open("w") as handle:
        subprocess.run([sys.executable, "tools/photo_shoot.py", "batch", rel(spec), "--output", rel(run)],
                       cwd=ROOT, stdout=handle, check=True)
    result = run.parent / f"{run.name}.out.json"
    # Gateway output goes to files; a reader that exits early kills it before it releases the lease.
    with result.open("w") as stdout, (run.parent / f"{run.name}.err").open("w") as stderr:
        code = subprocess.run(["timeout", "590", "bun", "scripts/unity-editor-gateway.ts", "--project-path",
                               "client/Unity", "--batch", rel(batch)], cwd=ROOT, stdout=stdout, stderr=stderr).returncode
    if code != 0:
        raise SystemExit(f"gateway exited {code}; read {rel(result)} and {run.name}.err")
    shot = shoot_result(result)
    for h in shot.get("hits", []):
        print(f"  t{h['tick']:>4} {h['owner']}->{h['target']} slot {h['slot']:>2} dmg {h['damage']:>3} {h['tier']}"
              f"{' BLOCKED' if h['blocked'] else ''}")


def shoot_result(path: Path) -> dict:
    text, decoder, i = path.read_text(), json.JSONDecoder(), 0
    while i < len(text):
        while i < len(text) and text[i].isspace():
            i += 1
        if i >= len(text):
            break
        doc, i = decoder.raw_decode(text, i)
        data = doc.get("data") or {}
        if data.get("command") == "sloparena.photo.shoot":
            inner = (data.get("result") or {}).get("result") or {}
            if not inner.get("success"):
                raise SystemExit(f"shoot failed: {inner.get('failure')}")
            return inner
    raise SystemExit(f"no sloparena.photo.shoot result in {path}")


def sheet(run: Path, spec: dict, start: int, end: int, every: int, out: Path) -> None:
    subprocess.run([sys.executable, "tools/photo_shoot.py", "sheet", rel(run), "--camera", spec["cameras"][0]["name"],
                    "--frames", f"{start}-{end}:{every}", "--out", rel(out)], cwd=ROOT, check=True)


def mix(job: dict, out: Path) -> Path:
    run = out / "run"
    spec = json.loads((out / "spec-vertical.json").read_text())
    start, end = spec["capture"]["from"], job.get("end", spec["capture"]["to"])
    hold = job.get("hold", 0.0)
    video = out / "video.mp4"
    subprocess.run([sys.executable, "tools/photo_shoot.py", "clip", rel(run), "--camera", spec["cameras"][0]["name"],
                    "--from", str(start), "--to", str(end), "--width", "1080", "--height", "1920",
                    "--out", rel(video)], cwd=ROOT, check=True)
    seconds = (end - start + 1) / FPS + hold
    final = out / f"{job['id']}-vertical.mp4"
    # Photo mode records the game's own sound (music stopped) frame-locked to the shoot in audio.wav.
    trim = f"apad,atrim=0:{seconds}"
    subprocess.run(["ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-i", str(video), "-i", str(run / "audio.wav"),
                    "-filter_complex", f"[0:v]tpad=stop_mode=clone:stop_duration={hold}[v];"
                    f"[1:a]{trim},{loudness(run / 'audio.wav', trim)}[a]",
                    "-map", "[v]", "-map", "[a]", "-c:v", "libx264", "-preset", "slow", "-crf", "18",
                    "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-shortest",
                    "-movflags", "+faststart", str(final)], check=True)
    sheet(run, spec, start, end, 30, out / f"{job['id']}-sheet.jpg")
    print(final)
    return final


def loudness(source: Path, pre: str) -> str:
    """Two-pass loudnorm to -14 LUFS, -2 dBTP: one pass guesses badly on a 5-second clip."""
    target = "loudnorm=I=-14:TP=-2:LRA=11"
    report = subprocess.run(["ffmpeg", "-hide_banner", "-i", str(source), "-af", f"{pre},{target}:print_format=json",
                             "-f", "null", "-"], capture_output=True, text=True, check=True).stderr
    m = json.loads(report[report.rindex("{"):report.rindex("}") + 1])
    if m["input_i"] == "-inf":
        return "anull"  # a silent shot (no hits, or a package without sounds yet) stays silent
    return (f"{target}:linear=true:measured_I={m['input_i']}:measured_TP={m['input_tp']}:measured_LRA={m['input_lra']}"
            f":measured_thresh={m['input_thresh']}:offset={m['target_offset']},alimiter=limit=0.79:level=false")


def normalize(source: Path, out: Path) -> None:
    """Copy the picture, bring the sound to the clips' loudness."""
    subprocess.run(["ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-i", str(source), "-af",
                    loudness(source, "anull"), "-c:v", "copy", "-c:a", "aac", "-b:a", "192k", "-ar", "48000",
                    "-movflags", "+faststart", str(out)], check=True)


def set_status(path: str, status: str, **fields: str) -> None:
    job = json.loads(Path(path).read_text())
    job.update(status=status, **fields)
    Path(path).write_text(json.dumps(job, indent=1, ensure_ascii=False) + "\n")


def cmd_make(args: argparse.Namespace) -> None:
    job, out = load_job(args.job)
    spec_path = vertical_spec(job, out, args.probe)
    run = fresh(out / ("probe" if args.probe else "run"))
    shoot(spec_path, run)
    if args.probe:
        capture = json.loads(spec_path.read_text())["capture"]
        sheet(run, json.loads(spec_path.read_text()), capture["from"], capture["to"], 15, out / "probe.sheet.jpg")
        return
    final = mix(job, out)
    set_status(args.job, "rendered", clip=rel(final))


def cmd_list(args: argparse.Namespace) -> None:
    jobs = [json.loads(p.read_text()) | {"file": p.name} for p in sorted(QUEUE.glob("*.json"))]
    for status in STATUSES:
        for job in (j for j in jobs if j.get("status") == status):
            print(f"{status:9} {job['id']:32} {job.get('format', ''):9} {job.get('title', '')}")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("list", help="print the queue by status").set_defaults(func=cmd_list)
    make = sub.add_parser("make", help="shoot an idea vertically, then encode and mix")
    make.add_argument("job")
    make.add_argument("--probe", action="store_true", help="cheap 540x960 framing check, no encode")
    make.set_defaults(func=cmd_make)
    redo = sub.add_parser("mix", help="re-encode and re-mix the last full shoot")
    redo.add_argument("job")
    redo.set_defaults(func=lambda a: mix(*load_job(a.job)))
    args = parser.parse_args()
    args.func(args)


if __name__ == "__main__":
    main()
