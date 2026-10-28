#!/usr/bin/env python3
"""Local web dashboard for the social clip queue (scenarios/social/queue), standard library only.

    python3 tools/social_dashboard.py [--host 127.0.0.1] [--port 8765]

The page shows every idea by status with its vertical clip, and writes the owner's verdicts and notes back
into the queue files that agents read. "Propose clips" and "Render approved" POST to a T3 webhook task
whose URL lives in ~/.config/sloparena/social-dashboard.json ({"webhook": "..."}), outside the repo.
"""
from __future__ import annotations

import argparse
import json
import subprocess
import threading
import time
import urllib.request
from datetime import date
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
QUEUE = ROOT / "scenarios/social/queue"
MEDIA = ROOT / "tmp/content"
THUMBS = MEDIA / ".thumbs"
PAGE = Path(__file__).with_name("social_dashboard.html")
FONT = ROOT / "client/Unity/Assets/Fonts/ArchivoBlack-Regular.ttf"
CONFIG = Path.home() / ".config/sloparena/social-dashboard.json"
STATUSES = {"proposed", "approved", "rendered", "ready", "posted", "rejected"}
_editor = {"at": 0.0, "state": "unknown"}


def entries() -> list[dict]:
    items = []
    for path in sorted(QUEUE.glob("*.json")):
        job = json.loads(path.read_text())
        clip = ROOT / job["clip"] if job.get("clip") else None
        job["thumb"] = f"/thumb/{job['id']}" if clip and clip.exists() else ""
        job["video"] = f"/media/{job['clip']}" if clip and clip.exists() else ""
        items.append(job)
    return items


def thumbnail(job_id: str) -> Path | None:
    job = json.loads((QUEUE / f"{job_id}.json").read_text())
    clip = ROOT / job.get("clip", "")
    if not clip.is_file():
        return None
    thumb = THUMBS / f"{job_id}.jpg"
    if not thumb.exists() or thumb.stat().st_mtime < clip.stat().st_mtime:
        THUMBS.mkdir(parents=True, exist_ok=True)
        length = float(subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0",
                                       str(clip)], capture_output=True, text=True).stdout or 1)
        at = 0.3 if job.get("format") == "explainer" else length * 0.55
        subprocess.run(["ffmpeg", "-v", "error", "-y", "-ss", f"{at:.2f}", "-i", str(clip), "-frames:v", "1",
                        "-vf", "scale=270:480", "-q:v", "5", str(thumb)], check=True)
    return thumb


def running() -> dict:
    jobs = subprocess.run(["pgrep", "-af", "social_clip.py|social_explainer.py"], capture_output=True, text=True).stdout
    work = [line.split("scenarios/social/queue/")[-1].replace(".json", "") for line in jobs.splitlines()
            if "scenarios/social/queue/" in line]
    if time.time() - _editor["at"] > 20:
        _editor["at"] = time.time()
        # Gateway status goes to a file, never a pipe that could close early on the lease holder.
        out = MEDIA / ".dashboard-status.json"
        with out.open("w") as handle:
            subprocess.run(["timeout", "30", "bun", "scripts/unity-editor-gateway.ts", "status", "--project-path",
                            "client/Unity"], cwd=ROOT, stdout=handle, stderr=subprocess.DEVNULL)
        try:
            text = out.read_text()
            _editor["state"] = json.loads(text[text.rindex("\n{") + 1:] if "\n{" in text else text).get("state", "unknown")
        except (ValueError, json.JSONDecodeError):
            _editor["state"] = "unknown"
    return {"work": sorted(set(work)), "editor": _editor["state"]}


def update(job_id: str, status: str, note: str, stats: dict | None) -> dict:
    path = QUEUE / f"{job_id}.json"
    job = json.loads(path.read_text())
    if status:
        if status not in STATUSES:
            raise ValueError(f"unknown status {status}")
        job["status"] = status
        if status == "posted":
            job.setdefault("posted", date.today().isoformat())
    if note.strip():
        stamp = f"Owner {date.today().isoformat()}: {note.strip()}"
        job["feedback"] = f"{job['feedback']}\n{stamp}" if job.get("feedback") else stamp
    if stats is not None:
        job["stats"] = stats
    path.write_text(json.dumps(job, indent=1, ensure_ascii=False) + "\n")
    return job


def trigger(action: str) -> str:
    url = json.loads(CONFIG.read_text())["webhook"]
    request = urllib.request.Request(url, data=json.dumps({"action": action}).encode(), method="POST",
                                     headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(request, timeout=20) as response:
        return str(response.status)


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *args) -> None:
        pass

    def send(self, code: int, body: bytes, kind: str) -> None:
        self.send_response(code)
        self.send_header("Content-Type", kind)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def json(self, value, code: int = 200) -> None:
        self.send(code, json.dumps(value).encode(), "application/json")

    def file(self, path: Path, kind: str) -> None:
        size = path.stat().st_size
        start, end = 0, size - 1
        ranged = self.headers.get("Range", "").startswith("bytes=")
        if ranged:  # browsers seek video with byte ranges
            first, _, last = self.headers["Range"][6:].partition("-")
            start, end = int(first or 0), int(last) if last else size - 1
        self.send_response(206 if ranged else 200)
        self.send_header("Content-Type", kind)
        self.send_header("Accept-Ranges", "bytes")
        self.send_header("Content-Length", str(end - start + 1))
        if ranged:
            self.send_header("Content-Range", f"bytes {start}-{end}/{size}")
        self.end_headers()
        with path.open("rb") as handle:
            handle.seek(start)
            self.wfile.write(handle.read(end - start + 1))

    def do_GET(self) -> None:
        try:
            if self.path == "/":
                self.send(200, PAGE.read_bytes(), "text/html; charset=utf-8")
            elif self.path == "/font.ttf":
                self.file(FONT, "font/ttf")
            elif self.path == "/api/queue":
                self.json({"items": entries(), "running": running()})
            elif self.path.startswith("/thumb/"):
                thumb = thumbnail(Path(self.path[7:]).name)
                self.file(thumb, "image/jpeg") if thumb else self.send(404, b"", "text/plain")
            elif self.path.startswith("/media/"):
                path = (ROOT / self.path[7:]).resolve()
                if not path.is_relative_to(MEDIA.resolve()) or not path.is_file():
                    self.send(404, b"", "text/plain")
                else:
                    self.file(path, "video/mp4")
            else:
                self.send(404, b"", "text/plain")
        except (BrokenPipeError, ConnectionResetError):
            pass

    def do_POST(self) -> None:
        body = json.loads(self.rfile.read(int(self.headers.get("Content-Length", 0))) or b"{}")
        try:
            if self.path == "/api/update":
                self.json(update(Path(body["id"]).name, body.get("status", ""), body.get("note", ""), body.get("stats")))
            elif self.path == "/api/run" and body.get("action") in ("propose", "render"):
                self.json({"ok": trigger(body["action"])})
            else:
                self.json({"error": "unknown request"}, 404)
        except Exception as error:  # report to the page instead of dropping the connection
            self.json({"error": str(error)}, 400)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8765)
    args = parser.parse_args()
    server = ThreadingHTTPServer((args.host, args.port), Handler)
    threading.Thread(target=running, daemon=True).start()
    print(f"SLOPArena content factory on http://{args.host}:{args.port}")
    server.serve_forever()


if __name__ == "__main__":
    main()
