"""Rigid vs spring-poncho review videos for Dusty, captured in the shared Unity Editor.

python3 tools/blender/salamander_rig/spring_poncho_review.py plan <out_dir> [take ...]
bun scripts/unity-editor-gateway.ts --project-path client/Unity --batch <out_dir>/batch.json > <out_dir>/batch.jsonl
python3 tools/blender/salamander_rig/spring_poncho_review.py encode <out_dir>

`plan` writes one 60 fps timeline per take (<take>.timeline: root offset, facing and the clips' local times and
weights, 0.1 s crossfades), one copy of spring_poncho_capture.eval.cs per take and view, and the gateway batch.
Root motion uses his character.json run speed, jump force and gravity. The run turns 180 degrees in one tick, as the
simulation does when the stick reverses. `encode` stacks each take as a 2 x 2 video (gameplay camera on top,
three-quarter below; live rigid poncho left, spring candidate right), joins them into review.mp4 and writes
metrics.md from the capture's per-frame hem offsets and lift angles.
"""
import json
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
TEMPLATE = Path(__file__).with_name("spring_poncho_capture.eval.cs")
FPS, FADE = 60, 6
VIEWS = ["gameplay", "threequarter"]
TAKES = {
    "idle": [("idle", 240, "stop")],
    "run-turn": [("idle", 18, "stop"), ("run", 66, "forward"), ("run", 66, "back"), ("idle", 60, "stop")],
    "jump": [("idle", 20, "stop"), ("jump", 4, "squat"), ("jump", None, "rise"), ("fall", None, "drop"), ("idle", 60, "stop")],
    "g1-haymaker": [("idle", 24, "stop"), ("g1", 72, "stop"), ("idle", 48, "stop")],
    "copter": [("idle", 24, "stop"), ("copter", 100, "stop"), ("idle", 48, "stop")],
}
LABELS = {"gameplay": "gameplay cam", "threequarter": "3/4"}


def stats():
    text = (ROOT / "client/Unity/Assets/CharacterPackages/salamander/character.json").read_text()
    first = lambda key: float(re.search(rf'"{key}"\s*:\s*(-?[0-9.eE+-]+)', text).group(1))
    return {"run": first("runSpeed"), "jump": first("jumpForce"), "gravity": first("gravity")}


def timeline(segments, s):
    rows, pos, vel, yaw = [], [0.0, 0.0, 0.0], [0.0, 0.0, 0.0], 0.0
    clip_time, previous = {}, None
    dt = 1.0 / FPS
    for clip, frames, motion in segments:
        start = len(rows)
        fade_from = None
        if previous is not None and previous[0] != clip:
            fade_from = previous
            clip_time[clip] = 0.0
        clip_time.setdefault(clip, 0.0)
        if motion == "rise":
            vel[1] = s["jump"]
        f = 0
        while True:
            if frames is not None and f >= frames:
                break
            if motion == "rise" and vel[1] <= 0:
                break
            if motion == "drop" and pos[1] <= 0 and f > 0:
                break
            if motion == "forward":
                yaw, target = 0.0, s["run"]
                vel[2] = min(target, max(vel[2], 0.6 * target) + 0.4 * target / 12)
            elif motion == "back":
                yaw = 180.0
                vel[2] = max(-s["run"], vel[2] - 2 * s["run"] / 6)
            elif motion in ("stop", "squat"):
                vel[2] = vel[2] - min(abs(vel[2]), s["run"] / 6) * (1 if vel[2] > 0 else -1)
            if motion in ("rise", "drop"):
                vel[1] -= s["gravity"] * dt
            for k in range(3):
                pos[k] += vel[k] * dt
            if pos[1] < 0:
                pos[1], vel[1] = 0.0, 0.0
            row = [f"{pos[0]:.5f}", f"{pos[1]:.5f}", f"{pos[2]:.5f}", f"{yaw:.1f}"]
            fade = min(1.0, (len(rows) - start + 1) / FADE) if fade_from else 1.0
            row += [clip, f"{clip_time[clip]:.5f}", f"{fade:.4f}"]
            if fade < 1.0:
                fade_clip, fade_time = fade_from
                row += [fade_clip, f"{fade_time + (len(rows) - start) * dt:.5f}", f"{1 - fade:.4f}"]
            rows.append(" ".join(row))
            clip_time[clip] += dt
            f += 1
        previous = (clip, clip_time[clip])
    return rows


def plan(out, takes):
    out.mkdir(parents=True, exist_ok=True)
    s = stats()
    template = TEMPLATE.read_text()
    commands = []
    for take in takes:
        rows = timeline(TAKES[take], s)
        (out / f"{take}.timeline").write_text("\n".join(rows) + "\n")
        for view in VIEWS:
            body = template.replace("@OUT@", str(out)).replace("@TAKE@", take).replace("@VIEW@", view)
            path = out / f"{take}-{view}.eval.cs"
            path.write_text(body)
            commands.append(["eval_file", "--file", str(path)])
        print(take, len(rows), "frames")
    (out / "batch.json").write_text(json.dumps({"commands": commands, "restore": []}, indent=1))
    print(out / "batch.json")


def run(cmd):
    subprocess.run(cmd, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)


def label(text):
    return (f"drawtext=fontfile=/usr/share/fonts/noto/NotoSans-Regular.ttf:text='{text}':x=14:y=12:fontsize=26:"
            "fontcolor=white:box=1:boxcolor=black@0.45:boxborderw=8")


def encode(out):
    takes = [t for t in TAKES if (out / t / "gameplay" / "spring").exists()]
    grids = []
    for take in takes:
        inputs, chains = [], []
        for v, view in enumerate(VIEWS):
            for r, (rig, name) in enumerate([("rigid", f"{take} - LIVE rigid"), ("spring", "CANDIDATE spring + 2-sided")]):
                inputs += ["-framerate", str(FPS), "-i", str(out / take / view / rig / "f%04d.jpg")]
                chains.append(f"[{2 * v + r}]{label(f'{name} - {LABELS[view]}')}[c{2 * v + r}]")
        grid = out / f"{take}.mp4"
        run(["ffmpeg", "-y", "-loglevel", "error", *inputs, "-filter_complex",
             ";".join(chains) + ";[c0][c1]hstack[top];[c2][c3]hstack[bottom];[top][bottom]vstack,scale=out_range=tv,format=yuv420p",
             "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "20", "-r", str(FPS), str(grid)])
        grids.append(grid)
    listing = out / "review.txt"
    listing.write_text("".join(f"file '{g}'\n" for g in grids))
    run(["ffmpeg", "-y", "-loglevel", "error", "-f", "concat", "-safe", "0", "-i", str(listing), "-c", "copy", str(out / "review.mp4")])
    # Hem offset: the furthest hem vertex from where Spine2 would carry its rest position (game metres). The largest
    # one-frame change is where a pop would show; right after the run's one-tick turn it is the body flipping.
    lines = ["| take | rig | hem offset max (m) | largest one-frame change (m) | largest lift (deg) |", "|---|---|---|---|---|"]
    for take in takes:
        metrics = json.loads((out / take / "gameplay" / "metrics.json").read_text())
        for rig in ("rigid", "spring"):
            travel = [m[rig]["hemTravel"] for m in metrics]
            jump = max(abs(b - a) for a, b in zip(travel, travel[1:]))
            lift = f"{max(max(m[rig]['lift']) for m in metrics):.0f}" if metrics[0][rig].get("lift") else "-"
            lines.append(f"| {take} | {rig} | {max(travel):.3f} | {jump:.3f} | {lift} |")
    (out / "metrics.md").write_text("\n".join(lines) + "\n")
    print("\n".join(lines))
    print(out / "review.mp4")


if __name__ == "__main__":
    command, out = sys.argv[1], Path(sys.argv[2]).resolve()
    if command == "plan":
        plan(out, sys.argv[3:] or list(TAKES))
    elif command == "encode":
        encode(out)
    else:
        sys.exit(__doc__)
