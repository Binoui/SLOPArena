"""Durable release timings; observations never invent historical start times."""
from contextlib import contextmanager
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import time
import uuid


def utc_now():
    return datetime.now(timezone.utc).isoformat(timespec="milliseconds")


def seconds(start, end):
    return max(0, (datetime.fromisoformat(end) - datetime.fromisoformat(start)).total_seconds())


def atomic_json(path, value):
    path = Path(path)
    if path.is_symlink():
        raise ValueError("Timing evidence may not be a symlink")
    temporary = path.with_name("." + path.name + "." + uuid.uuid4().hex)
    try:
        with os.fdopen(os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600), "w") as output:
            json.dump(value, output, indent=2)
            output.write("\n")
            output.flush()
            os.fsync(output.fileno())
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


TERMINAL = {"completed", "failed", "interrupted", "dispatch-rejected"}


def transition(step, status, timestamp, attempt=None):
    """Repeated polling preserves starts; an explicit retry creates another attempt."""
    history = step.setdefault("timingAttempts", [])
    previous = history[-1] if history else None
    if status == "pending":
        return
    if previous and not previous.get("finishedAtUtc") and (
            previous.get("attempt") is None or previous["status"].startswith("retry-")):
        previous["attempt"] = attempt
    if previous and previous.get("attempt") == attempt and previous["status"] == status:
        return
    if previous and previous.get("attempt") == attempt and not previous.get("finishedAtUtc"):
        current = previous
    elif status in TERMINAL:
        # A first observation of an already finished external operation has no measured start.
        return
    else:
        current = {"startedAtUtc": timestamp, "status": status, "attempt": attempt}
        history.append(current)
    current["status"] = status
    if status in TERMINAL:
        current.update(finishedAtUtc=timestamp,
                       durationSeconds=seconds(current["startedAtUtc"], timestamp))


def summary(step, timestamp=None):
    history = step.get("timingAttempts", [])
    if not history:
        return ""
    current = history[-1]
    elapsed = current.get("durationSeconds")
    if elapsed is None:
        elapsed = seconds(current["startedAtUtc"], timestamp or utc_now())
    rounded = int(elapsed)
    result = f"{rounded // 60}m {rounded % 60:02d}s"
    if not current.get("finishedAtUtc"):
        result += " elapsed"
    if len(history) > 1:
        result += f" (attempt {len(history)})"
    return result


class Timeline:
    def __init__(self, path):
        self.path = Path(path)
        self.value = {"schemaVersion": 1, "phases": []}

    def save(self):
        atomic_json(self.path, self.value)

    @contextmanager
    def stage(self, name):
        row = {"phase": name, "startedAtUtc": utc_now(), "status": "running"}
        self.value["phases"].append(row)
        self.save()
        started = time.monotonic()
        try:
            yield
        except BaseException:
            row["status"] = "failed"
            raise
        else:
            row["status"] = "completed"
        finally:
            row.update(finishedAtUtc=utc_now(), durationSeconds=max(0, time.monotonic() - started))
            self.save()

    def native(self, report):
        """Native entrypoint records disjoint outer phases and nested BuildReport steps."""
        self.value["native"] = report
        unity = next((row for row in self.value["phases"] if row["phase"] == "unity"), None)
        if unity and report.get("enteredAtUtc"):
            self.value["unityStartupAndImportSeconds"] = seconds(unity["startedAtUtc"], report["enteredAtUtc"])
        self.save()
