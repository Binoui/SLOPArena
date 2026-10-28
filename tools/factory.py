#!/usr/bin/env python3
"""Talk to the SLOPArena content factory on alfred (tools/content_factory/server.py) from the studio desk.

The factory is the source of truth for social ideas, the owner's comments, takes, runs and scout reports;
skills/social-clips/SKILL.md owns the workflow. Its address comes from $SLOPARENA_FACTORY, else
~/.config/sloparena/factory.json ({"url": "..."}), else alfred's tailnet address.

    python3 tools/factory.py list [--status approved]       # ideas by status
    python3 tools/factory.py get <idea>                     # one idea as JSON, with comments and takes
    python3 tools/factory.py add <idea.json>...             # new proposed ideas (or shots, with "for")
    python3 tools/factory.py set <idea> '<json>'            # merge fields (fovScale, camera, beats, ...); null deletes
    python3 tools/factory.py comment <idea> "<text>"        # an agent's note on the idea
    python3 tools/factory.py take <idea> <clip.mp4> [--sheet sheet.jpg] [--note "..."]   # upload a new take
    python3 tools/factory.py fetch <idea> [--out file.mp4]  # download the chosen (or latest) take
    python3 tools/factory.py run <run> running|done|failed [--summary "..."]
    python3 tools/factory.py inbox                          # scout inbox items not yet reported on
    python3 tools/factory.py report scout <report.md> [--inbox 3,4]
    python3 tools/factory.py reports [--kind scout] [--limit 3]
    python3 tools/factory.py import <queue-dir>             # one-time move of the old JSON queue
"""
from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import tempfile
import urllib.error
import urllib.request
from pathlib import Path
from urllib.parse import quote

ROOT = Path(__file__).resolve().parents[1]
CONFIG = Path.home() / ".config/sloparena/factory.json"
STATUSES = ["proposed", "approved", "rendered", "ready", "posted", "rejected"]


def base() -> str:
    if os.environ.get("SLOPARENA_FACTORY"):
        return os.environ["SLOPARENA_FACTORY"].rstrip("/")
    if CONFIG.exists():
        return json.loads(CONFIG.read_text())["url"].rstrip("/")
    return "http://100.68.254.9:8765"


def call(method: str, path: str, body=None, data: bytes | None = None, headers: dict | None = None):
    payload = data if data is not None else (json.dumps(body).encode() if body is not None else None)
    request = urllib.request.Request(base() + path, data=payload, method=method,
                                     headers={"Content-Type": "application/json", **(headers or {})})
    try:
        with urllib.request.urlopen(request, timeout=300) as response:
            return json.loads(response.read())
    except urllib.error.HTTPError as error:
        raise SystemExit(f"factory: {json.loads(error.read() or b'{}').get('error', error)}") from None


def get(idea_id: str) -> dict:
    return call("GET", f"/api/ideas/{idea_id}")


def poster(clip: Path, out: Path, explainer: bool) -> None:
    length = float(subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0",
                                   str(clip)], capture_output=True, text=True).stdout or 1)
    at = 0.3 if explainer else length * 0.55
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-ss", f"{at:.2f}", "-i", str(clip), "-frames:v", "1",
                    "-vf", "scale=270:480", "-q:v", "5", str(out)], check=True)


def take(idea_id: str, clip: Path, sheet: Path | None = None, note: str = "", keep: bool = False) -> int:
    """Upload a clip as the idea's next take, with a poster frame and an optional contact sheet."""
    n = call("PUT", f"/api/ideas/{idea_id}/takes{'?keep' if keep else ''}", data=clip.read_bytes(),
             headers={"Content-Type": "video/mp4", "X-Note": quote(note)})["take"]
    with tempfile.TemporaryDirectory() as tmp:
        frame = Path(tmp) / "poster.jpg"
        poster(clip, frame, get(idea_id).get("format") == "explainer")
        call("PUT", f"/api/ideas/{idea_id}/takes/{n}/poster", data=frame.read_bytes(),
             headers={"Content-Type": "image/jpeg"})
    if sheet and sheet.is_file():
        call("PUT", f"/api/ideas/{idea_id}/takes/{n}/sheet", data=sheet.read_bytes(),
             headers={"Content-Type": "image/jpeg"})
    print(f"{idea_id}: take {n} uploaded" + ("" if keep else ", waiting for the owner in To review"))
    return n


def fetch(idea_id: str, out: Path) -> Path:
    idea = get(idea_id)
    takes = {t["n"]: t for t in idea["takes"] if t["clip"]}
    if not takes:
        raise SystemExit(f"factory: {idea_id} has no take")
    chosen = takes.get(idea.get("chosen")) or takes[max(takes)]
    out.parent.mkdir(parents=True, exist_ok=True)
    with urllib.request.urlopen(base() + chosen["clip"], timeout=300) as response:
        out.write_bytes(response.read())
    return out


def migrate(queue: Path) -> None:
    """Move the old JSON queue in: status, fields, dated owner notes as comments, the current clip as take 1."""
    for path in sorted(queue.glob("*.json")):
        job = json.loads(path.read_text())
        comments = []
        for line in filter(None, (job.pop("feedback", "") or "").splitlines()):
            if line.startswith("Owner ") and line[16:18] == ": ":
                comments.append({"author": "owner", "body": line[18:], "at": line[6:16]})
            else:
                comments.append({"author": "owner", "body": line, "at": job.get("created")})
        clip = ROOT / job.pop("clip") if job.get("clip") else None
        has_take = bool(clip and clip.is_file())
        chosen = 1 if has_take and job["status"] in {"ready", "posted"} else None
        call("POST", "/api/ideas", job | {"comments": comments, "chosen": chosen})
        if has_take:
            take(job["id"], clip, clip.with_name(f"{job['id']}-sheet.jpg"), keep=True)
        print(f"{job['id']}: {job['status']}")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("list").add_argument("--status", choices=STATUSES)
    sub.add_parser("get").add_argument("idea")
    sub.add_parser("add").add_argument("files", nargs="+", type=Path)
    patch = sub.add_parser("set")
    patch.add_argument("idea")
    patch.add_argument("fields")
    note = sub.add_parser("comment")
    note.add_argument("idea")
    note.add_argument("text")
    upload = sub.add_parser("take")
    upload.add_argument("idea")
    upload.add_argument("clip", type=Path)
    upload.add_argument("--sheet", type=Path)
    upload.add_argument("--note", default="")
    download = sub.add_parser("fetch")
    download.add_argument("idea")
    download.add_argument("--out", type=Path)
    run = sub.add_parser("run")
    run.add_argument("run", type=int)
    run.add_argument("state", choices=["running", "done", "failed"])
    run.add_argument("--summary", default="")
    sub.add_parser("inbox")
    report = sub.add_parser("report")
    report.add_argument("kind", choices=["scout", "analyst"])
    report.add_argument("file", type=Path)
    report.add_argument("--inbox", default="")
    reports = sub.add_parser("reports")
    reports.add_argument("--kind", default="")
    reports.add_argument("--limit", type=int, default=3)
    sub.add_parser("import").add_argument("queue", type=Path)
    args = parser.parse_args()

    if args.command == "list":
        ideas = call("GET", f"/api/ideas?status={args.status or ''}")
        for status in STATUSES:
            for idea in (i for i in ideas if i["status"] == status):
                print(f"{status:9} {idea['id']:32} {idea.get('format', ''):9} {idea.get('title', '')}")
    elif args.command == "get":
        print(json.dumps(get(args.idea), indent=1, ensure_ascii=False))
    elif args.command == "add":
        for path in args.files:
            print(call("POST", "/api/ideas", json.loads(path.read_text()))["id"], "added")
    elif args.command == "set":
        call("PATCH", f"/api/ideas/{args.idea}", json.loads(args.fields))
        print(f"{args.idea} updated")
    elif args.command == "comment":
        call("POST", f"/api/ideas/{args.idea}/comments", {"author": "agent", "body": args.text})
    elif args.command == "take":
        take(args.idea, args.clip, args.sheet, args.note)
    elif args.command == "fetch":
        print(fetch(args.idea, args.out or ROOT / f"tmp/content/{args.idea}/{args.idea}-vertical.mp4"))
    elif args.command == "run":
        print(call("PATCH", f"/api/runs/{args.run}", {"state": args.state, "summary": args.summary})["state"])
    elif args.command == "inbox":
        for item in call("GET", "/api/inbox?open=1"):
            print(f"[{item['id']}] {item['at'][:10]}  {item['body']}")
    elif args.command == "report":
        text = sys.stdin.read() if str(args.file) == "-" else args.file.read_text()
        ids = [int(i) for i in args.inbox.split(",") if i]
        print("report", call("POST", "/api/reports", {"kind": args.kind, "body": text, "inbox": ids})["id"])
    elif args.command == "reports":
        for item in call("GET", f"/api/reports?kind={args.kind}&limit={args.limit}"):
            print(f"## {item['kind']} report {item['id']} ({item['at'][:10]})\n\n{item['body']}\n")
    elif args.command == "import":
        migrate(args.queue)


if __name__ == "__main__":
    main()
