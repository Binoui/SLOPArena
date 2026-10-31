#!/usr/bin/env python3
"""Talk to the SLOPArena content factory on alfred (tools/content_factory/server.py) from the studio desk.

The factory is the source of truth for human checks, social ideas, the owner's comments, takes, runs and scout reports;
skills/social-clips/SKILL.md owns the workflow. Its address comes from $SLOPARENA_FACTORY, else
~/.config/sloparena/factory.json ({"url": "..."}), else alfred's tailnet address.

    python3 tools/factory.py list [--status approved]       # ideas by status
    python3 tools/factory.py get <idea>                     # one idea as JSON, with comments and takes
    python3 tools/factory.py add <idea.json>...             # new proposed ideas (or shots, with "for")
    python3 tools/factory.py set <idea> '<json>'            # merge fields (fovScale, camera, beats, ...); null deletes
    python3 tools/factory.py comment <idea> "<text>"        # an agent's note on the idea
    python3 tools/factory.py take <idea> <clip.mp4> [--sheet sheet.jpg] [--note "..."]   # upload a new take
    python3 tools/factory.py fetch <idea> [--out file.mp4]  # download the chosen (or latest) take
    python3 tools/factory.py run <run> running|done|failed [--summary "..."] [--made 12b-x,12c-y,report:2]
                                                            # done needs --made: every idea added or given a take, and reports
    python3 tools/factory.py inbox                          # inbox items not yet answered by a report
    python3 tools/factory.py report scout|designer <report.md> [--inbox 3,4]   # closes those inbox items
    python3 tools/factory.py reports [--kind scout] [--limit 3]
    python3 tools/factory.py import <queue-dir>             # one-time move of the old JSON queue
    python3 tools/factory.py check add --title "..." --steps <file.md|-> (--thread <id>|--no-thread)
                                      [--area Bashie] [--clip <file> ...]
    python3 tools/factory.py check list [--status to-try]
    python3 tools/factory.py check get <id>
    python3 tools/factory.py check note <id> "<text>"
    python3 tools/factory.py check ready <id> [--note "what changed"]
    python3 tools/factory.py check drop <id> [--note "why superseded"]
    python3 tools/factory.py check attach <id> <file>...
    python3 tools/factory.py check qa-brief <id>            # the brief an independent QA subagent runs from
    python3 tools/factory.py check qa <id> --result pass|partial|fail --report <file.md|-> [--clip <file> ...]
    python3 tools/factory.py check import <checks.json>
    python3 tools/factory.py relay [--once] [--interval 20]  # desk outbox worker, T3_CLI overrides t3
"""
from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
import tempfile
import time
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


def t3_cli() -> str:
    return os.environ.get("T3_CLI") or os.environ.get("T3CODE_CLI_PATH") or "t3"


def thread_title(thread: str | None) -> str | None:
    if not thread:
        return None
    try:
        result = subprocess.run([t3_cli(), "thread", "list", "--json"], check=True,
                                capture_output=True, text=True, timeout=30)
        threads = json.loads(result.stdout)
        return next((t["title"] for t in threads if t.get("threadId") == thread), None)
    except (OSError, subprocess.SubprocessError, ValueError, KeyError, TypeError):
        return None


def attach_check(check_id: int, files: list[Path]) -> None:
    for path in files:
        name = re.sub(r"[^a-zA-Z0-9._-]", "-", path.name)
        if not name or not name[0].isalnum():
            name = "media-" + name
        call("PUT", f"/api/checks/{check_id}/media/{quote(name)}", data=path.read_bytes(),
             headers={"Content-Type": "application/octet-stream"})


QA_BRIEF = """You are an independent QA tester for SLOPArena in {root}. Test Slop Factory check #{id} and report what you
find. You did not build this change: work only from the check, the repo docs and the build. Do not read the builder's
thread or reports. You test and report; you never fix, judge taste, or decide the verdict.

1. Read the check: `python3 tools/factory.py check get {id}`. Read AGENTS.md, docs/testing.md and the section
   "Report independent QA" in docs/systems/slop-factory.md.
2. If every "Look for" line is a judgment call (feel, looks, preference), stop and report partial with
   "Nothing a machine can test." Otherwise test every line a machine can observe: run the build, drive the Editor
   or the game, read real values, capture screenshots or clips.
3. Leave things as you found them. Do not edit tracked files; put everything under tmp/qa-{id}/. Before you start,
   snapshot `git status` and hashes of anything you might touch, and compare at the end; other threads have
   uncommitted edits. Do not save or cook packages unless the check says to.
4. The Unity Editor is shared. Use the gateway with your own identity and your own bounded lease, per
   docs/contributing/unity-editor-coordination.md. Wait for it; never take over another batch. If it stays
   blocked, mark the affected lines NOT TESTED and say why.
5. Never press verdict buttons on the live factory, change a check's status, or message real T3 threads. To test
   the factory itself, run a local server with a fake T3_CLI as the slop-factory doc describes.
6. Report once, at the end:
   `python3 tools/factory.py check qa {id} --result pass|partial|fail --report tmp/qa-{id}/report.md --clip <files>`
   pass: every line tested and held. partial: some lines need the owner or could not run. fail: a line broke.
   The report has one numbered entry per step: PASS, FAIL or NOT TESTED, the evidence (numbers, screenshot
   names), then one line on method, and anything in the check's wording that was ambiguous."""


def create_check(title: str, steps: str, area: str | None, thread: str | None, clips: list[Path]) -> int:
    for path in clips:
        if not path.is_file() or path.suffix.lower() not in {".mp4", ".jpg", ".png"}:
            raise SystemExit(f"factory: check media must be an existing .mp4, .jpg or .png file: {path}")
    check = call("POST", "/api/checks", {"title": title, "steps": steps, "area": area,
                                        "thread": thread, "thread_title": thread_title(thread)})
    print(check["id"], flush=True)
    attach_check(check["id"], clips)
    return check["id"]


def check_command(args) -> None:
    if args.check_command == "add":
        steps = sys.stdin.read() if args.steps == "-" else Path(args.steps).read_text()
        check_id = create_check(args.title, steps, args.area, args.thread, args.clip)
        print("\nStart one fresh subagent in the background with this QA brief, unchanged "
              "(reprint it with `check qa-brief`):\n\n" + QA_BRIEF.format(id=check_id, root=ROOT))
    elif args.check_command == "qa-brief":
        print(QA_BRIEF.format(id=args.id, root=ROOT))
    elif args.check_command == "list":
        for check in call("GET", "/api/board")["checks"]:
            if not args.status or check["status"] == args.status:
                print(f"{check['id']:4} {check['status']:8} {check['area'] or '-':16} {check['title']}")
    elif args.check_command == "get":
        check = next((c for c in call("GET", "/api/board")["checks"] if c["id"] == args.id), None)
        if not check:
            raise SystemExit(f"factory: no check {args.id}")
        print(json.dumps(check, indent=1, ensure_ascii=False))
    elif args.check_command == "note":
        call("POST", f"/api/checks/{args.id}/notes", {"author": "agent", "body": args.text, "send": False})
    elif args.check_command == "qa":
        report = sys.stdin.read() if args.report == "-" else Path(args.report).read_text()
        call("POST", f"/api/checks/{args.id}/notes",
             {"author": "qa", "body": f"QA: {args.result.upper()}.\n\n{report.strip()}", "send": False})
        attach_check(args.id, args.clip)
    elif args.check_command in {"ready", "drop"}:
        call("POST", f"/api/checks/{args.id}/verdict",
             {"status": {"ready": "to-try", "drop": "dropped"}[args.check_command],
              "author": "agent", "note": args.note})
    elif args.check_command == "attach":
        attach_check(args.id, args.files)
    elif args.check_command == "import":
        for item in json.loads(args.file.read_text()):
            create_check(item["title"], item["steps"], item.get("area"), item.get("thread"),
                         [Path(p) for p in item.get("clips", [])])


def relay_message(note: dict) -> str:
    body = note["body"]
    match = re.fullmatch(r"Verdict: (TO-TRY|GOOD|REDO|DROPPED)( \+ COMMIT)?\.(?:\n\n(.*))?", body, re.DOTALL)
    if match:
        verdict, commit, text = match[1], match[2], match[3]
        message = f'Owner checked "{note["title"]}" (factory check #{note["check_id"]}): {verdict}.'
        if text:
            message += f"\n\n{text}"
        if commit:
            message += ("\n\nThe owner authorizes a commit (no push). Commit only this work's changes "
                        "(git commit -- <paths>; where a file also holds other threads' uncommitted edits, stage "
                        "only your hunks), following docs/contributing/conventions.md, and reply with the sha. "
                        "Then settle this thread (t3_thread_organize, action settle). If the commit cannot be "
                        "made cleanly, say why and leave the thread active.")
        elif verdict == "REDO":
            message += (f'\n\nWhen fixed, run: python3 tools/factory.py check ready {note["check_id"]} '
                        '--note "<what changed>"')
        elif verdict == "TO-TRY":
            message += "\n\nThe owner reopened this check for another try."
        return message
    return (f'Owner left a note on "{note["title"]}" (factory check #{note["check_id"]}, '
            f'{note["status"]}):\n\n{body}\n\n'
            f'Read the check: python3 tools/factory.py check get {note["check_id"]}')


def relay(once: bool, interval: float) -> None:
    while True:
        try:
            for note in call("GET", "/api/outbox"):
                try:
                    subprocess.run([t3_cli(), "thread", "send", note["thread"], "-"],
                                   input=relay_message(note), text=True, capture_output=True, check=True, timeout=60)
                except (OSError, subprocess.SubprocessError) as error:
                    detail = error.stderr.strip() if isinstance(error, subprocess.CalledProcessError) else str(error)
                    call("POST", f"/api/outbox/{note['id']}", {"error": detail or str(error)})
                    print(f"note {note['id']}: {detail or error}", file=sys.stderr, flush=True)
                else:
                    call("POST", f"/api/outbox/{note['id']}", {"delivered": True})
                    print(f"note {note['id']}: delivered", flush=True)
        except (OSError, SystemExit) as error:
            if once:
                raise
            print(f"relay: {error}", file=sys.stderr, flush=True)
        if once:
            return
        time.sleep(interval)


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
    run.add_argument("--made", help="comma-separated idea ids and report:<n> this run produced; required with done")
    sub.add_parser("inbox")
    report = sub.add_parser("report")
    report.add_argument("kind", choices=["scout", "designer", "analyst"])
    report.add_argument("file", type=Path)
    report.add_argument("--inbox", default="")
    reports = sub.add_parser("reports")
    reports.add_argument("--kind", default="")
    reports.add_argument("--limit", type=int, default=3)
    sub.add_parser("import").add_argument("queue", type=Path)
    checks = sub.add_parser("check").add_subparsers(dest="check_command", required=True)
    add = checks.add_parser("add")
    add.add_argument("--title", required=True)
    add.add_argument("--steps", required=True)
    add.add_argument("--area")
    thread = add.add_mutually_exclusive_group(required=True)
    thread.add_argument("--thread")
    thread.add_argument("--no-thread", action="store_true")
    add.add_argument("--clip", nargs="+", type=Path, default=[])
    checks.add_parser("list").add_argument("--status", choices=["to-try", "redo", "good", "dropped"])
    checks.add_parser("get").add_argument("id", type=int)
    note = checks.add_parser("note")
    note.add_argument("id", type=int)
    note.add_argument("text")
    checks.add_parser("qa-brief").add_argument("id", type=int)
    qa = checks.add_parser("qa")
    qa.add_argument("id", type=int)
    qa.add_argument("--result", required=True, choices=["pass", "partial", "fail"])
    qa.add_argument("--report", required=True)
    qa.add_argument("--clip", nargs="+", type=Path, default=[])
    for command in ("ready", "drop"):
        move = checks.add_parser(command)
        move.add_argument("id", type=int)
        move.add_argument("--note", default="")
    attach = checks.add_parser("attach")
    attach.add_argument("id", type=int)
    attach.add_argument("files", nargs="+", type=Path)
    checks.add_parser("import").add_argument("file", type=Path)
    worker = sub.add_parser("relay")
    worker.add_argument("--once", action="store_true")
    worker.add_argument("--interval", type=float, default=20)
    args = parser.parse_args()
    if args.command == "relay" and args.interval <= 0:
        parser.error("relay interval must be positive")

    if args.command == "check":
        check_command(args)
    elif args.command == "relay":
        relay(args.once, args.interval)
    elif args.command == "list":
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
        if args.state == "done" and args.made is None:
            parser.error('done needs --made (ideas added or given a take, report:<n>; --made "" when nothing)')
        made = None if args.made is None else [x.strip() for x in args.made.split(",") if x.strip()]
        print(call("PATCH", f"/api/runs/{args.run}", {"state": args.state, "summary": args.summary, "made": made})["state"])
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
