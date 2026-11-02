#!/usr/bin/env python3
"""Pull retained SLOPArena recordings through the owner's console."""
import argparse
import datetime
import json
import http.client
import os
from pathlib import Path
import tempfile
import urllib.request
import uuid

MAX_BYTES = 64_000_000
# The format's eight bytes include the C string's terminating zero.
MAGIC = b"SLOPREP\0"


def pull(base_url, key, dest):
    dest = Path(dest)
    dest.mkdir(parents=True, exist_ok=True)

    def get(path):
        return urllib.request.urlopen(urllib.request.Request(
            base_url.rstrip("/") + path, headers={"X-Console-Key": key}), timeout=120)

    with get("/api/replays") as response:
        listing = response.read(512 * 1024 + 1)
        if len(listing) > 512 * 1024:
            raise ValueError("Replay list exceeds 512 KiB")
        entries = json.loads(listing)["replays"]
    if not isinstance(entries, list) or len(entries) > 2000:
        raise ValueError("Invalid replay list")
    downloaded = skipped = failed = 0
    for entry in entries:
        temp = None
        try:
            match_id, size, complete = entry["matchId"], entry["bytes"], entry["complete"]
            if str(uuid.UUID(match_id)) != match_id or type(size) is not int or not 0 <= size <= MAX_BYTES or type(complete) is not bool:
                raise ValueError("Invalid replay metadata")
            final = dest / (match_id + ".slopreplay" + ("" if complete else ".part"))
            if final.exists() and final.stat().st_size == size:
                with final.open("rb") as existing:
                    if existing.read(8) == MAGIC:
                        print(f"{match_id}: already present")
                        skipped += 1
                        continue
            with get(f"/api/matches/{match_id}/replay") as response:
                with tempfile.NamedTemporaryFile(dir=dest, prefix=".replay-", delete=False) as output:
                    temp = Path(output.name)
                    total = 0
                    magic = b""
                    while chunk := response.read(65536):
                        total += len(chunk)
                        if total > size or total > MAX_BYTES:
                            raise ValueError("Replay exceeds advertised size")
                        magic = (magic + chunk)[:8]
                        output.write(chunk)
                    if total != size or magic != MAGIC:
                        raise ValueError("Replay size or magic mismatch")
                    output.flush()
                    os.fsync(output.fileno())
            os.replace(temp, final)
            temp = None
            if complete:
                (dest / (match_id + ".slopreplay.part")).unlink(missing_ok=True)
            print(f"{match_id}: downloaded {size} bytes ({'complete' if complete else 'partial'})")
            downloaded += 1
        except (OSError, ValueError, KeyError, TypeError, http.client.HTTPException) as error:
            print(f"{entry.get('matchId', '?')}: failed: {error}")
            failed += 1
        finally:
            if temp is not None:
                temp.unlink(missing_ok=True)
    print(f"Downloaded {downloaded}, already present {skipped}, failed {failed}")
    return failed


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base-url", default="http://127.0.0.1:4173")
    parser.add_argument("--dest", type=Path, default=Path(os.environ.get(
        "XDG_DATA_HOME", str(Path.home() / ".local/share"))) / "sloparena/replays" /
        datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%d"))
    args = parser.parse_args()
    key = (Path.home() / ".local/state/sloparena-console/console.key").read_text().strip()
    if not key:
        raise ValueError("Console key is empty")
    return bool(pull(args.base_url, key, args.dest))


if __name__ == "__main__":
    raise SystemExit(main())
