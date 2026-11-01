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
    python3 tools/factory.py file <idea> <final.mp4> --title "..." --format trailer [--posted 2026-10-28]
                                      [--source <project dir>] [--idea "..."]
                                                            # file a video made outside the factory: ready, or posted
    python3 tools/factory.py sync [--out ~/Videos/SLOPArena-social]   # chosen takes of Ready and Posted as a folder
    python3 tools/factory.py posted <idea> tiktok|shorts|instagram|x [<link>]   # where it is up; moves Ready to Posted
    python3 tools/factory.py views [--dry-run]              # read TikTok/Shorts numbers into stats; links new posts
    python3 tools/factory.py set <idea> '{"captions": {...}}'   # the caption kit, one entry per platform:
        {"shorts": {"title": "...", "text": "..."}, "tiktok": {"text": "..."}, "instagram": {"text": "..."},
         "x": {"text": "...", "reply": "..."}}            # docs/systems/content-factory.md has the house style
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
import shutil
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request
from datetime import datetime
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


def file_final(idea_id: str, clip: Path, title: str, fmt: str, posted: str | None, source: str, idea: str) -> None:
    """Every video that gets posted lives in the factory, also the ones made by hand elsewhere (trailers, edits).
    Files it as take 1, chosen, in Ready to post, or straight in Posted with its date."""
    if not clip.is_file():
        raise SystemExit(f"factory: no file {clip}")
    fields = {"id": idea_id, "title": title, "format": fmt, "status": "posted" if posted else "ready",
              "chosen": 1, "posted": posted, "idea": idea or title}
    if source:
        fields["source"] = source
    call("POST", "/api/ideas", fields)
    take(idea_id, clip, keep=True)
    print(f"{idea_id}: filed in {'Posted' if posted else 'Ready to post'}")
    sync(SOCIAL)


SOCIAL = Path.home() / "Videos/SLOPArena-social"


def sync(out: Path) -> None:
    """Mirror the board's Ready to post and Posted videos into a plain folder for posting by hand:
    ready/<title>.mp4 and posted/<date> <title>.mp4. A clip that moved to Posted leaves ready/."""
    wanted: dict[Path, dict] = {}
    for idea in call("GET", "/api/ideas"):
        if idea["status"] not in {"ready", "posted"} or idea.get("format") == "shot" or idea.get("usedIn"):
            continue  # usedIn: went out only inside another video
        name = re.sub(r"[^\w !'&,.()+-]+", " ", idea.get("title") or idea["id"]).strip()
        name = re.sub(r" {2,}", " ", name)
        if idea["status"] == "posted":
            wanted[out / "posted" / f"{idea.get('posted') or ''} {name}.mp4".strip()] = idea
        else:
            wanted[out / "ready" / f"{name}.mp4"] = idea
    for folder in ("ready", "posted"):
        for old in (out / folder).glob("*.mp4") if (out / folder).is_dir() else []:
            if old not in wanted:
                old.unlink()
                print(f"removed {old.relative_to(out)}")
    for path, idea in sorted(wanted.items()):
        if not path.is_file():
            fetch(idea["id"], path)
            print(f"added   {path.relative_to(out)}")
    print(f"{out}: {sum(1 for p in wanted if p.parent.name == 'ready')} ready, "
          f"{sum(1 for p in wanted if p.parent.name == 'posted')} posted")


PLATFORMS = ["tiktok", "shorts", "instagram", "x"]


def mark_posted(idea_id: str, platform: str, link: str) -> None:
    idea = get(idea_id)
    posts = idea.get("posts") or {}
    posts[platform] = link or posts.get(platform) or True
    call("PATCH", f"/api/ideas/{idea_id}", {"posts": posts})
    if idea["status"] == "ready":
        call("POST", f"/api/ideas/{idea_id}/verdict", {"status": "posted", "note": ""})
    print(f"{idea_id}: on {', '.join(posts)}")


# Public profiles the views command reads; yt-dlp needs no account or API key for these.
CHANNELS = {"shorts": "https://www.youtube.com/@SLOPArena/shorts", "tiktok": "https://www.tiktok.com/@sloparena"}


def ytdlp(*args: str) -> dict | None:
    """yt-dlp JSON, or None when the site refused. Runs the latest release through uvx when yt-dlp is not
    installed, so TikTok and YouTube breakages get upstream's fixes without a reinstall."""
    command = ["yt-dlp"] if shutil.which("yt-dlp") else ["uvx", "yt-dlp@latest"]
    result = subprocess.run(command + ["-J", "--skip-download", "--no-warnings", *args],
                            capture_output=True, text=True, timeout=180)
    if result.returncode:
        print(f"yt-dlp {' '.join(args)}: {result.stderr.strip().splitlines()[-1:] or 'failed'}", file=sys.stderr)
        return None
    return json.loads(result.stdout)


def words(text: str) -> set[str]:
    return {w for w in re.findall(r"[a-z0-9']+", text.lower().replace("#", " ")) if len(w) > 2}


def match_post(video: dict, ideas: list[dict], platform: str) -> dict | None:
    """The idea a newly found post belongs to: the posted text shares most of its words with the idea's title or
    its caption kit for that platform (owners paste the kit). Only a clear, unique winner counts."""
    said = words(f"{video.get('title') or ''} {video.get('description') or ''}")
    if not said:
        return None
    scores = []
    for idea in ideas:
        kit = (idea.get("captions") or {}).get(platform) or {}
        best = max((len(said & words(t)) / len(said) for t in (idea.get("title"), kit.get("title"), kit.get("text"))
                    if t), default=0)
        scores.append((best, idea))
    scores.sort(key=lambda x: -x[0])
    if scores and scores[0][0] >= 0.6 and (len(scores) == 1 or scores[1][0] < scores[0][0] - 0.2):
        return scores[0][1]
    return None


def views(dry_run: bool) -> None:
    """Link new posts found on the public profiles, then store each linked post's views, likes and comments.
    stats keeps <platform>, <platform>_likes, <platform>_comments and checked; history keeps one row a day."""
    ideas = [i for i in call("GET", "/api/ideas") if i["status"] in {"ready", "posted"} and not i.get("usedIn")
             and i.get("format") != "shot"]
    linked = {v for i in ideas for v in (i.get("posts") or {}).values() if isinstance(v, str)}
    for platform, channel in CHANNELS.items():
        listing = ytdlp("--flat-playlist", channel) or {}
        for video in listing.get("entries") or []:
            url = video.get("url") or ""
            if platform == "shorts":
                url = f"https://www.youtube.com/shorts/{video['id']}"
            if not url or url in linked:
                continue
            idea = match_post(video, [i for i in ideas if not isinstance((i.get("posts") or {}).get(platform), str)],
                              platform)
            if not idea:
                print(f"unlinked {platform} post: {url} \"{(video.get('title') or '')[:60]}\"\n"
                      f"  link it: python3 tools/factory.py posted <idea> {platform} {url}")
                continue
            print(f"{idea['id']}: found on {platform} {url}")
            idea.setdefault("posts", {})[platform] = url
            linked.add(url)
            if not dry_run:
                mark_posted(idea["id"], platform, url)
    stamp = datetime.now().isoformat(timespec="minutes")
    for idea in ideas:
        stats, today = dict(idea.get("stats") or {}), {}
        for platform, url in (idea.get("posts") or {}).items():
            if platform not in CHANNELS or not isinstance(url, str):
                continue
            video = ytdlp(url)
            if not video:
                continue
            for suffix, key in (("", "view_count"), ("_likes", "like_count"), ("_comments", "comment_count")):
                if video.get(key) is not None:
                    stats[platform + suffix] = video[key]
            today[platform] = video.get("view_count")
        if not today:
            continue
        stats["checked"] = stamp
        history = [h for h in idea.get("history") or [] if h.get("at") != stamp[:10]] + [{"at": stamp[:10], **today}]
        print(f"{idea['id']}: " + ", ".join(f"{p} {v}" for p, v in today.items()))
        if not dry_run:
            call("POST", f"/api/ideas/{idea['id']}/stats", {"stats": stats})
            call("PATCH", f"/api/ideas/{idea['id']}", {"history": history})


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


def owner_thread(thread: str | None) -> tuple[str | None, str | None]:
    """Return (id, title) of the thread the owner talks to: subagent threads resolve to their top parent."""
    title = None
    for _ in range(5):
        if not thread:
            break
        try:
            result = subprocess.run([t3_cli(), "thread", "show", thread, "--json", "--messages", "0"], check=True,
                                    capture_output=True, text=True, timeout=30)
            info = json.loads(result.stdout)["thread"]
        except (OSError, subprocess.SubprocessError, ValueError, KeyError, TypeError):
            break
        title = info.get("title")
        if not info.get("parentThreadId"):
            break
        thread = info["parentThreadId"]
    return thread, title


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
6. Do not stop or hand back while your own captures, builds or test runs are still going: wait for them, then
   report. Stopping early reads as finished to whoever started you.
7. Report once, at the end, and post it once (re-running the command adds a second note):
   `python3 tools/factory.py check qa {id} --result pass|partial|fail --report tmp/qa-{id}/report.md --clip <files>`
   pass: every line tested and held. partial: some lines need the owner or could not run. fail: a line broke.
   The report has one numbered entry per step: PASS, FAIL or NOT TESTED, the evidence (numbers, screenshot
   names), then one line on method, and anything in the check's wording that was ambiguous."""


QA_RESULT_RULE = ("When its result comes back: on pass or partial, add nothing to the thread unless a line needs the "
                  "owner. On fail, or a bug QA found outside the lines, make one fix pass, record it with `check note`, "
                  "and do not re-run QA.")


def create_check(title: str, steps: str, area: str | None, thread: str | None, clips: list[Path]) -> int:
    for path in clips:
        if not path.is_file() or path.suffix.lower() not in {".mp4", ".jpg", ".png"}:
            raise SystemExit(f"factory: check media must be an existing .mp4, .jpg or .png file: {path}")
    thread, thread_name = owner_thread(thread)
    check = call("POST", "/api/checks", {"title": title, "steps": steps, "area": area,
                                        "thread": thread, "thread_title": thread_name})
    print(check["id"], flush=True)
    attach_check(check["id"], clips)
    return check["id"]


def check_command(args) -> None:
    if args.check_command == "add":
        steps = sys.stdin.read() if args.steps == "-" else Path(args.steps).read_text()
        check_id = create_check(args.title, steps, args.area, args.thread, args.clip)
        print("\nStart one fresh subagent in the background with this QA brief, unchanged "
              "(reprint it with `check qa-brief`). " + QA_RESULT_RULE + "\n\n" + QA_BRIEF.format(id=check_id, root=ROOT))
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


STAY_ACTIVE = ("\n\nIf this thread is settled, unsettle it (t3_thread_organize, action unsettle) and leave it "
               "active so the owner sees your reply; only a Good + commit settles it.")


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
        return message if commit else message + STAY_ACTIVE
    return (f'Owner left a note on "{note["title"]}" (factory check #{note["check_id"]}, '
            f'{note["status"]}):\n\n{body}\n\n'
            f'Read the check: python3 tools/factory.py check get {note["check_id"]}' + STAY_ACTIVE)


def relay(once: bool, interval: float) -> None:
    while True:
        try:
            for note in call("GET", "/api/outbox"):
                try:
                    subprocess.run([t3_cli(), "thread", "send", owner_thread(note["thread"])[0], "-"],
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
    final = sub.add_parser("file")
    final.add_argument("idea")
    final.add_argument("clip", type=Path)
    final.add_argument("--title", required=True)
    final.add_argument("--format", required=True, help="explainer, trailer, edit, ...")
    final.add_argument("--posted", help="YYYY-MM-DD when it is already posted; else it lands in Ready to post")
    final.add_argument("--source", default="", help="the project that made it, relative to the repo")
    final.add_argument("--idea", dest="idea_text", default="", help="one line on what it shows")
    sub.add_parser("sync").add_argument("--out", type=Path, default=SOCIAL)
    sub.add_parser("views").add_argument("--dry-run", action="store_true")
    up = sub.add_parser("posted")
    up.add_argument("idea")
    up.add_argument("platform", choices=PLATFORMS)
    up.add_argument("link", nargs="?", default="")
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
    elif args.command == "file":
        file_final(args.idea, args.clip, args.title, args.format, args.posted, args.source, args.idea_text)
    elif args.command == "posted":
        mark_posted(args.idea, args.platform, args.link)
        sync(SOCIAL)
    elif args.command == "views":
        views(args.dry_run)
    elif args.command == "sync":
        sync(args.out.expanduser())
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
