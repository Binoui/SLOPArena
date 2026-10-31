#!/usr/bin/env python3
"""SLOPArena content factory: the board, its data and the clip library (runs on alfred, standard library only).

    python3 server.py --data /data [--host 0.0.0.0] [--port 8765]

<data>/factory.db holds ideas, comments, takes, runs, the scout inbox and reports; <data>/media/<idea>/ holds
each take's clip, poster and contact sheet. Owner buttons create runs and POST them to a T3 webhook task (URL in
<data>/config.json as {"webhook": "..."}), which starts one T3 thread per run on the studio desk. Agents there
talk back through tools/factory.py. skills/social-clips/SKILL.md owns the workflow.
"""
from __future__ import annotations

import argparse
import json
import re
import sqlite3
import threading
import time
import urllib.request
from datetime import date, datetime
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, quote, unquote, urlsplit

HERE = Path(__file__).resolve().parent
DATA = Path("/data")
SCHEMA = """
CREATE TABLE IF NOT EXISTS checks (id INTEGER PRIMARY KEY, title TEXT NOT NULL, steps TEXT NOT NULL,
  area TEXT, status TEXT NOT NULL, thread TEXT, thread_title TEXT, created TEXT NOT NULL, updated TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS check_notes (id INTEGER PRIMARY KEY, check_id INTEGER NOT NULL REFERENCES checks,
  author TEXT NOT NULL, body TEXT NOT NULL, at TEXT NOT NULL, send INTEGER NOT NULL DEFAULT 0,
  delivered TEXT, error TEXT);
CREATE TABLE IF NOT EXISTS ideas (id TEXT PRIMARY KEY, status TEXT NOT NULL, data TEXT NOT NULL, chosen INTEGER,
  posted TEXT, stats TEXT, created TEXT NOT NULL, updated TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS comments (id INTEGER PRIMARY KEY, idea TEXT NOT NULL REFERENCES ideas, author TEXT NOT NULL,
  body TEXT NOT NULL, at TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS takes (idea TEXT NOT NULL REFERENCES ideas, n INTEGER NOT NULL, note TEXT, at TEXT NOT NULL,
  PRIMARY KEY (idea, n));
CREATE TABLE IF NOT EXISTS runs (id INTEGER PRIMARY KEY, kind TEXT NOT NULL, idea TEXT, state TEXT NOT NULL,
  summary TEXT, error TEXT, created TEXT NOT NULL, updated TEXT NOT NULL, made TEXT);
CREATE TABLE IF NOT EXISTS inbox (id INTEGER PRIMARY KEY, body TEXT NOT NULL, at TEXT NOT NULL, report INTEGER);
CREATE TABLE IF NOT EXISTS reports (id INTEGER PRIMARY KEY, kind TEXT NOT NULL, body TEXT NOT NULL, at TEXT NOT NULL);
"""
# The statuses the owner may move an idea to from each status. A new take moves it to "rendered".
VERDICTS = {
    "proposed": {"approved", "rejected"},
    "approved": {"ready", "proposed", "rejected"},  # ready: an earlier take the owner still wants
    "rendered": {"ready", "approved", "rejected"},
    "ready": {"posted", "approved", "rejected"},
    "posted": set(),
    "rejected": {"proposed"},
}
CHECK_MOVES = {"to-try": {"good", "redo", "dropped"}, "redo": {"to-try", "good", "dropped"},
               "good": {"to-try"}, "dropped": {"to-try"}}
CHECK_FIELDS = {"title", "steps", "area", "thread", "thread_title"}
CHECK_MEDIA = {".mp4": "video/mp4", ".jpg": "image/jpeg", ".png": "image/png"}
QA_RESULT = re.compile(r"QA: (PASS|PARTIAL|FAIL)\.")
TAKE_FROM = {"proposed", "approved", "rendered"}
RUN_KINDS = {"propose", "render", "redo", "scout"}
RUN_STATES = {"queued", "sent", "running", "done", "failed"}
UPLOADS = {"clip": "take-{n}.mp4", "poster": "take-{n}.jpg", "sheet": "take-{n}.sheet.jpg"}


class Refused(Exception):
    def __init__(self, message: str, code: int = 400):
        super().__init__(message)
        self.code = code


def now() -> str:
    return datetime.now().isoformat(timespec="seconds")


def db() -> sqlite3.Connection:
    con = sqlite3.connect(DATA / "factory.db", timeout=30, isolation_level=None)
    con.row_factory = sqlite3.Row
    return con


def idea_row(con: sqlite3.Connection, idea_id: str) -> sqlite3.Row:
    row = con.execute("SELECT * FROM ideas WHERE id = ?", (idea_id,)).fetchone()
    if not row:
        raise Refused(f"no idea {idea_id}", 404)
    return row


def media_url(idea_id: str, name: str) -> str:
    return f"/media/{idea_id}/{name}" if (DATA / "media" / idea_id / name).is_file() else ""


def full_idea(con: sqlite3.Connection, row: sqlite3.Row) -> dict:
    idea = json.loads(row["data"]) | {"id": row["id"], "status": row["status"], "chosen": row["chosen"],
                                      "posted": row["posted"], "stats": json.loads(row["stats"] or "{}"),
                                      "created": row["created"], "updated": row["updated"]}
    idea["takes"] = [{"n": t["n"], "note": t["note"], "at": t["at"],
                      **{kind: media_url(row["id"], name.format(n=t["n"])) for kind, name in UPLOADS.items()}}
                     for t in con.execute("SELECT * FROM takes WHERE idea = ? ORDER BY n", (row["id"],))]
    idea["comments"] = [dict(c) for c in con.execute(
        "SELECT author, body, at FROM comments WHERE idea = ? ORDER BY id", (row["id"],))]
    return idea


def board() -> dict:
    with db() as con:
        return {
            "checks": [full_check(con, row) for row in con.execute("SELECT * FROM checks ORDER BY created, id")],
            "ideas": [full_idea(con, row) for row in con.execute("SELECT * FROM ideas ORDER BY created, id")],
            "runs": [dict(r) for r in con.execute("SELECT * FROM runs ORDER BY id DESC LIMIT 40")],
            "inbox": [dict(r) for r in con.execute("SELECT * FROM inbox ORDER BY id DESC LIMIT 40")],
            "reports": [dict(r) for r in con.execute("SELECT * FROM reports ORDER BY id DESC LIMIT 10")],
        }


def check_row(con: sqlite3.Connection, check_id: int) -> sqlite3.Row:
    row = con.execute("SELECT * FROM checks WHERE id = ?", (check_id,)).fetchone()
    if not row:
        raise Refused(f"no check {check_id}", 404)
    return row


def full_check(con: sqlite3.Connection, row: sqlite3.Row) -> dict:
    folder = DATA / "media" / f"check-{row['id']}"
    media = [f"/media/check-{row['id']}/{quote(p.name)}" for p in sorted(folder.iterdir())
             if p.is_file() and p.suffix.lower() in CHECK_MEDIA] if folder.is_dir() else []
    notes = [dict(n) for n in con.execute("SELECT * FROM check_notes WHERE check_id = ? ORDER BY id", (row["id"],))]
    qa = next((QA_RESULT.match(n["body"])[1].lower() for n in reversed(notes) if n["author"] == "qa"), None)
    return dict(row) | {"notes": notes, "media": media, "qa": qa}


def check_fields(body: dict, creating: bool = False) -> dict:
    if body.keys() - CHECK_FIELDS:
        raise Refused("check fields are title, steps, area, thread and thread_title")
    fields = {}
    for key, value in body.items():
        if value is not None and not isinstance(value, str):
            raise Refused(f"{key} must be text")
        fields[key] = value.strip() if value else None
    for key in ("title", "steps"):
        if (creating or key in fields) and not fields.get(key):
            raise Refused(f"a check needs {key}")
    if "thread" in fields and not fields["thread"]:
        fields["thread_title"] = None
    return fields


def add_check(body: dict) -> dict:
    fields = check_fields(body, creating=True)
    with db() as con:
        check_id = con.execute(
            "INSERT INTO checks (title, steps, area, status, thread, thread_title, created, updated) "
            "VALUES (?, ?, ?, 'to-try', ?, ?, ?, ?)",
            (fields["title"], fields["steps"], fields.get("area"), fields.get("thread"),
             fields.get("thread_title"), now(), now())).lastrowid
        return full_check(con, check_row(con, check_id))


def patch_check(check_id: int, body: dict) -> dict:
    fields = check_fields(body)
    with db() as con:
        con.execute("BEGIN IMMEDIATE")
        row = check_row(con, check_id)
        if "thread" in fields and fields["thread"] != row["thread"] and "thread_title" not in fields:
            fields["thread_title"] = None
        for key, value in fields.items():
            con.execute(f"UPDATE checks SET {key} = ? WHERE id = ?", (value, check_id))
        con.execute("UPDATE checks SET updated = ? WHERE id = ?", (now(), check_id))
        return full_check(con, check_row(con, check_id))


def check_note(con: sqlite3.Connection, check_id: int, author: str, body: str, send: bool) -> None:
    if author not in {"owner", "agent", "qa"} or not isinstance(body, str) or not body.strip():
        raise Refused("a note needs an author (owner, agent or qa) and a body")
    if author == "qa" and not QA_RESULT.match(body.strip()):
        raise Refused("a QA note starts with QA: PASS., QA: PARTIAL. or QA: FAIL.")
    if not isinstance(send, bool):
        raise Refused("send must be true or false")
    if send and author != "owner":
        raise Refused("only owner notes can be sent to a thread")
    con.execute("INSERT INTO check_notes (check_id, author, body, at, send) VALUES (?, ?, ?, ?, ?)",
                (check_id, author, body.strip(), now(), send))
    con.execute("UPDATE checks SET updated = ? WHERE id = ?", (now(), check_id))


def add_check_note(check_id: int, body: dict) -> dict:
    with db() as con:
        con.execute("BEGIN IMMEDIATE")
        row = check_row(con, check_id)
        send = body.get("send", False)
        if not isinstance(send, bool):
            raise Refused("send must be true or false")
        check_note(con, check_id, body.get("author", "agent"), body.get("body", ""),
                   send and bool(row["thread"]))
        return full_check(con, check_row(con, check_id))


def check_verdict(check_id: int, body: dict) -> dict:
    status, note, author = body.get("status"), body.get("note", ""), body.get("author", "owner")
    send, commit = body.get("send", True), body.get("commit", False)
    if not isinstance(note, str):
        raise Refused("note must be text")
    if author not in {"owner", "agent"}:
        raise Refused("author must be owner or agent")
    if not isinstance(send, bool) or not isinstance(commit, bool):
        raise Refused("send and commit must be true or false")
    if commit and (status != "good" or author != "owner" or not send):
        raise Refused("only an owner Good sent to the thread can ask for a commit")
    with db() as con:
        con.execute("BEGIN IMMEDIATE")
        row = check_row(con, check_id)
        if not isinstance(status, str) or status not in CHECK_MOVES[row["status"]]:
            raise Refused(f"check {check_id} is {row['status']}: it cannot become {status}", 409)
        if author == "agent" and not (status == "dropped" or (row["status"] == "redo" and status == "to-try")):
            raise Refused("agents can ready a redo or drop a superseded check", 409)
        if status == "redo" and not note.strip():
            raise Refused("redo needs a note explaining what to change")
        if commit and not row["thread"]:
            raise Refused(f"check {check_id} has no thread to commit", 409)
        con.execute("UPDATE checks SET status = ?, updated = ? WHERE id = ?", (status, now(), check_id))
        # Keep the verdict in the note so a later move cannot change a queued outcome.
        text = (f"Verdict: {status.upper()}{' + COMMIT' if commit else ''}."
                + (f"\n\n{note.strip()}" if note.strip() else ""))
        check_note(con, check_id, author, text, send and author == "owner" and bool(row["thread"]))
        return full_check(con, check_row(con, check_id))


def upload_check(check_id: int, name: str, length: int, stream) -> dict:
    if not re.fullmatch(r"[a-zA-Z0-9][a-zA-Z0-9._-]*", name) or Path(name).suffix.lower() not in CHECK_MEDIA:
        raise Refused("check media needs a safe file name ending in .mp4, .jpg or .png")
    if length <= 0:
        raise Refused("media upload is empty")
    with db() as con:
        check_row(con, check_id)
    folder = DATA / "media" / f"check-{check_id}"
    folder.mkdir(parents=True, exist_ok=True)
    part = folder / f".upload-{threading.get_ident()}"
    try:
        with part.open("wb") as handle:
            left = length
            while left:
                chunk = stream.read(min(left, 1 << 20))
                if not chunk:
                    raise Refused("upload ended early")
                handle.write(chunk)
                left -= len(chunk)
        part.replace(folder / name)
    finally:
        part.unlink(missing_ok=True)
    with db() as con:
        con.execute("UPDATE checks SET updated = ? WHERE id = ?", (now(), check_id))
        return full_check(con, check_row(con, check_id))


def outbox() -> list:
    with db() as con:
        return [dict(n) for n in con.execute(
            "SELECT n.*, c.title, c.thread, c.status FROM check_notes n JOIN checks c ON c.id = n.check_id "
            "WHERE n.author = 'owner' AND n.send = 1 AND n.delivered IS NULL "
            "AND c.thread IS NOT NULL AND c.thread != '' ORDER BY n.id")]


def outbox_result(note_id: int, body: dict) -> dict:
    if body.get("delivered") is not True and not isinstance(body.get("error"), str):
        raise Refused("report delivered: true or an error string")
    with db() as con:
        con.execute("BEGIN IMMEDIATE")
        row = con.execute("SELECT * FROM check_notes WHERE id = ? AND author = 'owner' AND send = 1",
                          (note_id,)).fetchone()
        if not row:
            raise Refused(f"no sendable note {note_id}", 404)
        if not row["delivered"]:
            con.execute("UPDATE check_notes SET delivered = ?, error = ? WHERE id = ?",
                        (now() if body.get("delivered") is True else None,
                         None if body.get("delivered") is True else body["error"], note_id))
        return dict(con.execute("SELECT * FROM check_notes WHERE id = ?", (note_id,)).fetchone())


def add_idea(body: dict) -> dict:
    """Agents' proposals, and the one-time import (which may carry a status, take choice and history)."""
    idea_id = body.get("id", "")
    if not re.fullmatch(r"[a-z0-9][a-z0-9-]*", idea_id):
        raise Refused("an idea needs an id of lowercase letters, digits and dashes")
    if re.fullmatch(r"check-\d+", idea_id):
        raise Refused("check-<number> is reserved for check media")
    status = body.get("status", "proposed")
    if status not in VERDICTS:
        raise Refused(f"unknown status {status}")
    data = {k: v for k, v in body.items()
            if k not in {"id", "status", "chosen", "posted", "stats", "created", "comments", "takes"}}
    stamp = body.get("created") or now()
    with db() as con:
        if con.execute("SELECT 1 FROM ideas WHERE id = ?", (idea_id,)).fetchone():
            raise Refused(f"idea {idea_id} already exists", 409)
        con.execute("INSERT INTO ideas VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                    (idea_id, status, json.dumps(data), body.get("chosen"), body.get("posted"),
                     json.dumps(body["stats"]) if body.get("stats") else None, stamp, now()))
        for c in body.get("comments", []):
            con.execute("INSERT INTO comments (idea, author, body, at) VALUES (?, ?, ?, ?)",
                        (idea_id, c["author"], c["body"], c.get("at") or now()))
        return full_idea(con, idea_row(con, idea_id))


def patch_idea(idea_id: str, fields: dict) -> dict:
    """Agents adjust what an idea is (render fields, beats, title); status moves only by verdicts and takes."""
    if {"id", "status", "chosen", "posted", "stats"} & fields.keys():
        raise Refused("id, status, chosen, posted and stats are not idea fields")
    with db() as con:
        data = json.loads(idea_row(con, idea_id)["data"])
        for key, value in fields.items():
            if value is None:
                data.pop(key, None)
            else:
                data[key] = value
        con.execute("UPDATE ideas SET data = ?, updated = ? WHERE id = ?", (json.dumps(data), now(), idea_id))
        return full_idea(con, idea_row(con, idea_id))


def comment(con: sqlite3.Connection, idea_id: str, author: str, text: str) -> None:
    if text.strip():
        con.execute("INSERT INTO comments (idea, author, body, at) VALUES (?, ?, ?, ?)",
                    (idea_id, author, text.strip(), now()))


def verdict(idea_id: str, status: str, note: str, take: int | None) -> dict:
    """The owner's buttons. Sending a rendered or ready clip back to approved is a redo and starts a run."""
    with db() as con:
        row = idea_row(con, idea_id)
        if status and status not in VERDICTS[row["status"]]:
            raise Refused(f"{idea_id} is {row['status']}: it cannot become {status}", 409)
        comment(con, idea_id, "owner", note)
        redo = status == "approved" and row["status"] in {"rendered", "ready"}
        if status:
            chosen = row["chosen"]
            if status == "ready":
                last = con.execute("SELECT max(n) FROM takes WHERE idea = ?", (idea_id,)).fetchone()[0]
                if last is None:
                    raise Refused(f"{idea_id} has no take to mark ready", 409)
                chosen = take or last
            con.execute("UPDATE ideas SET status = ?, chosen = ?, posted = ?, updated = ? WHERE id = ?",
                        (status, chosen, date.today().isoformat() if status == "posted" else row["posted"],
                         now(), idea_id))
    if redo:
        start_run("redo", idea_id)
    with db() as con:
        return full_idea(con, idea_row(con, idea_id))


def set_stats(idea_id: str, stats: dict) -> dict:
    with db() as con:
        idea_row(con, idea_id)
        con.execute("UPDATE ideas SET stats = ?, updated = ? WHERE id = ?", (json.dumps(stats), now(), idea_id))
        return full_idea(con, idea_row(con, idea_id))


def upload(idea_id: str, kind: str, n: int | None, length: int, stream, note: str, keep: bool) -> dict:
    """A clip upload makes the next take and moves the idea to rendered; posters and sheets attach to a take.
    `keep` (the one-time import) files a take without touching the idea's status."""
    folder = DATA / "media" / idea_id
    folder.mkdir(parents=True, exist_ok=True)
    part = folder / f".upload-{threading.get_ident()}"
    with part.open("wb") as handle:
        left = length
        while left:
            chunk = stream.read(min(left, 1 << 20))
            if not chunk:
                raise Refused("upload ended early")
            handle.write(chunk)
            left -= len(chunk)
    with db() as con:
        con.execute("BEGIN IMMEDIATE")
        row = idea_row(con, idea_id)
        if kind == "clip":
            if row["status"] not in TAKE_FROM and not keep:
                part.unlink()
                raise Refused(f"{idea_id} is {row['status']}: send it back to approved before a new take", 409)
            n = (con.execute("SELECT max(n) FROM takes WHERE idea = ?", (idea_id,)).fetchone()[0] or 0) + 1
            con.execute("INSERT INTO takes VALUES (?, ?, ?, ?)", (idea_id, n, note or None, now()))
            if not keep:
                con.execute("UPDATE ideas SET status = 'rendered', updated = ? WHERE id = ?", (now(), idea_id))
        elif not con.execute("SELECT 1 FROM takes WHERE idea = ? AND n = ?", (idea_id, n)).fetchone():
            part.unlink()
            raise Refused(f"{idea_id} has no take {n}", 404)
        part.replace(folder / UPLOADS[kind].format(n=n))
        con.execute("COMMIT")
        return {"take": n}


def webhook() -> str:
    return json.loads((DATA / "config.json").read_text())["webhook"]


def send(run_id: int) -> None:
    """Hand a run to the T3 webhook. The relay holds requests while the desk is offline."""
    with db() as con:
        run = con.execute("SELECT * FROM runs WHERE id = ?", (run_id,)).fetchone()
    body = json.dumps({"action": run["kind"], "run": str(run_id), "id": run["idea"] or ""}).encode()
    try:
        request = urllib.request.Request(webhook(), data=body, method="POST",
                                         headers={"Content-Type": "application/json",
                                                  "User-Agent": "sloparena-factory/1"})  # Cloudflare 403s urllib's
        with urllib.request.urlopen(request, timeout=20):
            state, error = "sent", None
    except Exception as failure:  # kept queued; the resend loop and the Resend button retry it
        state, error = "queued", str(failure)
    with db() as con:
        con.execute("UPDATE runs SET state = ?, error = ?, updated = ? WHERE id = ?", (state, error, now(), run_id))


def start_run(kind: str, idea_id: str | None) -> dict:
    if kind not in RUN_KINDS:
        raise Refused(f"unknown run kind {kind}")
    if kind == "redo" and not idea_id:
        raise Refused("a redo names its idea")
    with db() as con:
        if idea_id:
            idea_row(con, idea_id)
        run_id = con.execute("INSERT INTO runs (kind, idea, state, created, updated) VALUES (?, ?, 'queued', ?, ?)",
                             (kind, idea_id, now(), now())).lastrowid
    send(run_id)
    with db() as con:
        return dict(con.execute("SELECT * FROM runs WHERE id = ?", (run_id,)).fetchone())


def report_run(run_id: int, state: str, summary: str, made: list | None) -> dict:
    """`made` lists what the run produced, for the board's links: idea ids (shots included) and "report:<n>"."""
    if state not in RUN_STATES - {"queued", "sent"}:
        raise Refused("an agent reports running, done or failed")
    with db() as con:
        if not con.execute("SELECT 1 FROM runs WHERE id = ?", (run_id,)).fetchone():
            raise Refused(f"no run {run_id}", 404)
        for item in made or []:
            report = re.fullmatch(r"report:(\d+)", str(item))
            found = con.execute("SELECT 1 FROM reports WHERE id = ?", (int(report[1]),)) if report else \
                con.execute("SELECT 1 FROM ideas WHERE id = ?", (str(item),))
            if not found.fetchone():
                raise Refused(f"made: no idea or report {item}", 404)
        con.execute("UPDATE runs SET state = ?, summary = coalesce(?, summary), made = coalesce(?, made), updated = ? "
                    "WHERE id = ?", (state, summary or None, None if made is None else json.dumps(made), now(), run_id))
        return dict(con.execute("SELECT * FROM runs WHERE id = ?", (run_id,)).fetchone())


def resend_loop() -> None:
    while True:
        time.sleep(120)
        with db() as con:
            waiting = [r["id"] for r in con.execute("SELECT id FROM runs WHERE state = 'queued'")]
        for run_id in waiting:
            send(run_id)


def add_inbox(text: str) -> dict:
    if not text.strip():
        raise Refused("empty inbox item")
    with db() as con:
        item_id = con.execute("INSERT INTO inbox (body, at) VALUES (?, ?)", (text.strip(), now())).lastrowid
        return dict(con.execute("SELECT * FROM inbox WHERE id = ?", (item_id,)).fetchone())


def add_report(kind: str, text: str, inbox: list[int]) -> dict:
    if kind not in {"scout", "designer", "analyst"} or not text.strip():
        raise Refused("a report has a kind (scout, designer or analyst) and a body")
    with db() as con:
        report_id = con.execute("INSERT INTO reports (kind, body, at) VALUES (?, ?, ?)",
                                (kind, text.strip(), now())).lastrowid
        con.executemany("UPDATE inbox SET report = ? WHERE id = ?", [(report_id, i) for i in inbox])
        return dict(con.execute("SELECT * FROM reports WHERE id = ?", (report_id,)).fetchone())


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

    def body(self) -> dict:
        return json.loads(self.rfile.read(int(self.headers.get("Content-Length", 0))) or b"{}")

    def file(self, path: Path, kind: str, download: str = "") -> None:
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
        if download:
            self.send_header("Content-Disposition", f'attachment; filename="{download}"')
        self.end_headers()
        with path.open("rb") as handle:
            handle.seek(start)
            left = end - start + 1
            while left:
                chunk = handle.read(min(left, 1 << 20))
                self.wfile.write(chunk)
                left -= len(chunk)

    def route(self, method: str) -> None:
        url = urlsplit(self.path)
        path, query = unquote(url.path), {k: v[0] for k, v in parse_qs(url.query, keep_blank_values=True).items()}
        parts = path.strip("/").split("/")
        if method == "GET" and path == "/":
            return self.send(200, (HERE / "index.html").read_bytes(), "text/html; charset=utf-8")
        if method == "GET" and path == "/font.ttf":
            return self.file(HERE / "ArchivoBlack-Regular.ttf", "font/ttf")
        if method == "GET" and parts[0] == "media" and len(parts) == 3:
            target = (DATA / "media" / parts[1] / parts[2]).resolve()
            if not target.is_relative_to((DATA / "media").resolve()) or not target.is_file():
                raise Refused("no such file", 404)
            kind = CHECK_MEDIA.get(target.suffix.lower(), "image/jpeg")
            return self.file(target, kind, f"{parts[1]}-{parts[2]}" if "dl" in query else "")
        if parts[0] != "api":
            raise Refused("not found", 404)
        api = parts[1:]
        if method == "GET" and api == ["board"]:
            return self.json(board())
        if method == "POST" and api == ["checks"]:
            return self.json(add_check(self.body()))
        if api[:1] == ["checks"] and len(api) >= 2:
            if not api[1].isdigit():
                raise Refused("a check id is a number")
            check_id = int(api[1])
            if method == "PATCH" and len(api) == 2:
                return self.json(patch_check(check_id, self.body()))
            if method == "POST" and api[2:] == ["verdict"]:
                return self.json(check_verdict(check_id, self.body()))
            if method == "POST" and api[2:] == ["notes"]:
                return self.json(add_check_note(check_id, self.body()))
            if method == "PUT" and len(api) == 4 and api[2] == "media":
                return self.json(upload_check(check_id, api[3], int(self.headers.get("Content-Length", 0)), self.rfile))
        if method == "GET" and api == ["outbox"]:
            return self.json(outbox())
        if method == "POST" and len(api) == 2 and api[0] == "outbox":
            if not api[1].isdigit():
                raise Refused("a note id is a number")
            return self.json(outbox_result(int(api[1]), self.body()))
        if method == "GET" and api == ["ideas"]:
            with db() as con:
                rows = con.execute("SELECT * FROM ideas WHERE ? = '' OR status = ? ORDER BY created, id",
                                   (query.get("status", ""), query.get("status", "")))
                return self.json([full_idea(con, row) for row in rows])
        if method == "POST" and api == ["ideas"]:
            return self.json(add_idea(self.body()))
        if api[0] == "ideas" and len(api) >= 2:
            idea_id = api[1]
            if method == "GET" and len(api) == 2:
                with db() as con:
                    return self.json(full_idea(con, idea_row(con, idea_id)))
            if method == "PATCH" and len(api) == 2:
                return self.json(patch_idea(idea_id, self.body()))
            if method == "POST" and api[2:] == ["verdict"]:
                body = self.body()
                return self.json(verdict(idea_id, body.get("status", ""), body.get("note", ""), body.get("take")))
            if method == "POST" and api[2:] == ["comments"]:
                body = self.body()
                with db() as con:
                    idea_row(con, idea_id)
                    comment(con, idea_id, body.get("author", "agent"), body.get("body", ""))
                    return self.json(full_idea(con, idea_row(con, idea_id)))
            if method == "POST" and api[2:] == ["stats"]:
                return self.json(set_stats(idea_id, self.body().get("stats", {})))
            if method == "PUT" and api[2:3] == ["takes"]:
                kind, n = ("clip", None) if len(api) == 3 else (api[4], int(api[3]))
                if kind not in UPLOADS:
                    raise Refused(f"uploads are {', '.join(UPLOADS)}")
                return self.json(upload(idea_id, kind, n, int(self.headers["Content-Length"]), self.rfile,
                                        unquote(self.headers.get("X-Note", "")), "keep" in query))
        if method == "POST" and api == ["runs"]:
            body = self.body()
            return self.json(start_run(body.get("kind", ""), body.get("idea") or None))
        if api[0] == "runs" and len(api) >= 2:
            if method == "PATCH" and len(api) == 2:
                body = self.body()
                return self.json(report_run(int(api[1]), body.get("state", ""), body.get("summary", ""), body.get("made")))
            if method == "POST" and api[2:] == ["resend"]:
                send(int(api[1]))
                with db() as con:
                    return self.json(dict(con.execute("SELECT * FROM runs WHERE id = ?", (int(api[1]),)).fetchone()))
        if api == ["inbox"]:
            if method == "POST":
                return self.json(add_inbox(self.body().get("body", "")))
            with db() as con:
                where = "WHERE report IS NULL" if "open" in query else ""
                return self.json([dict(r) for r in con.execute(f"SELECT * FROM inbox {where} ORDER BY id")])
        if api == ["reports"]:
            if method == "POST":
                body = self.body()
                return self.json(add_report(body.get("kind", ""), body.get("body", ""), body.get("inbox", [])))
            with db() as con:
                return self.json([dict(r) for r in con.execute(
                    "SELECT * FROM reports WHERE ? = '' OR kind = ? ORDER BY id DESC LIMIT ?",
                    (query.get("kind", ""), query.get("kind", ""), int(query.get("limit", 5))))])
        raise Refused("unknown request", 404)

    def handle_method(self, method: str) -> None:
        try:
            self.route(method)
        except (BrokenPipeError, ConnectionResetError):
            pass
        except Refused as refusal:
            self.json({"error": str(refusal)}, refusal.code)
        except Exception as error:  # report to the caller instead of dropping the connection
            self.json({"error": f"{type(error).__name__}: {error}"}, 500)

    def do_GET(self) -> None:
        self.handle_method("GET")

    def do_POST(self) -> None:
        self.handle_method("POST")

    def do_PATCH(self) -> None:
        self.handle_method("PATCH")

    def do_PUT(self) -> None:
        self.handle_method("PUT")


def main() -> None:
    global DATA
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--data", type=Path, default=DATA)
    parser.add_argument("--host", default="0.0.0.0")
    parser.add_argument("--port", type=int, default=8765)
    args = parser.parse_args()
    DATA = args.data
    (DATA / "media").mkdir(parents=True, exist_ok=True)
    with db() as con:
        con.executescript(SCHEMA)
        if "made" not in {col["name"] for col in con.execute("PRAGMA table_info(runs)")}:
            con.execute("ALTER TABLE runs ADD COLUMN made TEXT")
    threading.Thread(target=resend_loop, daemon=True).start()
    print(f"SLOPArena content factory on http://{args.host}:{args.port}, data in {DATA}", flush=True)
    ThreadingHTTPServer((args.host, args.port), Handler).serve_forever()


if __name__ == "__main__":
    main()
