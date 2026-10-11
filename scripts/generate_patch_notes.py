#!/usr/bin/env python3
"""Generate patch notes for a tagged SlopArena release.

Parses Conventional Commit subjects between the previous and new `v*` tag
into a factual changelog (New / Fixed / Changed; docs/chore/test collapsed
into an "Internal" footer count), asks DeepSeek for a short context blurb,
and creates/edits the GitHub release for the tag via `gh`.

Runs in CI on tag push (.github/workflows/patch-notes.yml). The release
itself is still cut manually via `gh release create` (see
scripts/build-release.sh / skills/sloparena-build-export) -- this only
automates the notes content, overwriting whatever placeholder text the
`--notes` flag passed at creation time.

Usage: scripts/generate_patch_notes.py <new-tag>
Env:   GH_TOKEN (for gh cli), DEEPSEEK_API_KEY
"""
from __future__ import annotations

import json
import os
import re
import subprocess
import sys
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
TEMPLATE = ROOT / "docs/release/RELEASE_NOTES.template.md"
REPO = "Binoui/SlopArena"

COMMIT_RE = re.compile(r"^(feat|fix|refactor|docs|chore|test)(?:\(([^)]+)\))?: (.+)$")
ISSUE_RE = re.compile(r"\s*\(issue #(\d+)\)\s*$")
PR_SUFFIX_RE = re.compile(r"\s*\(#\d+\)\s*$")  # GitHub squash-merge auto-append

TYPE_SECTION = {"feat": "New", "fix": "Fixed", "refactor": "Changed"}
INTERNAL_TYPES = {"docs", "chore", "test"}


def run(*args: str) -> str:
    return subprocess.run(
        args, cwd=ROOT, check=True, capture_output=True, text=True
    ).stdout


def previous_tag(new_tag: str) -> str | None:
    tags = run("git", "tag", "-l", "v*", "--sort=-version:refname").split()
    if new_tag not in tags:
        # Tag was just created and this checkout hasn't indexed it via `git
        # tag -l` sorting order in some edge case -- fall back to placing it
        # first (it's the newest by construction: CI only runs on its push).
        tags.insert(0, new_tag)
    idx = tags.index(new_tag)
    return tags[idx + 1] if idx + 1 < len(tags) else None


def commit_subjects(rev_range: str) -> list[str]:
    out = run(
        "git", "log", "--no-merges", "--first-parent", "--pretty=format:%s", rev_range
    )
    return [line for line in out.splitlines() if line.strip()]


def parse(subject: str) -> dict | None:
    m = COMMIT_RE.match(subject)
    if not m:
        return None
    ctype, scope, rest = m.groups()
    rest = PR_SUFFIX_RE.sub("", rest)
    issue = None
    im = ISSUE_RE.search(rest)
    if im:
        issue = im.group(1)
        rest = ISSUE_RE.sub("", rest)
    return {"type": ctype, "scope": scope, "summary": rest.strip(), "issue": issue}


def bucket(subjects: list[str]) -> tuple[dict[str, list[dict]], int, int]:
    sections: dict[str, list[dict]] = {"New": [], "Fixed": [], "Changed": []}
    internal = 0
    unparsed = 0
    for subject in subjects:
        item = parse(subject)
        if item is None:
            unparsed += 1
            continue
        section = TYPE_SECTION.get(item["type"])
        if section:
            sections[section].append(item)
        elif item["type"] in INTERNAL_TYPES:
            internal += 1
    return sections, internal, unparsed


def format_bullet(item: dict) -> str:
    scope = f"**{item['scope']}** — " if item["scope"] else ""
    line = f"- {scope}{item['summary']}"
    if item["issue"]:
        line += f" ([#{item['issue']}](https://github.com/{REPO}/issues/{item['issue']}))"
    return line


def deepseek_blurb(version: str, sections: dict[str, list[dict]], api_key: str) -> str:
    facts = [
        f"{name}: {item['summary']}"
        for name in ("New", "Fixed", "Changed")
        for item in sections[name]
    ]
    if not facts:
        return f"Housekeeping patch — no player-facing changes in {version}."

    prompt = (
        "You write short patch-note intros for an indie 3D platform fighter "
        "(SlopArena), in the style of League of Legends patch notes: one "
        "voicey paragraph (2-4 sentences) giving context/theme for the "
        "patch. Do not invent facts, numbers, or specifics beyond the list "
        "below -- stay thematic and high-level. Plain prose only, no "
        "markdown headers, no bullet list.\n\nFacts for this patch:\n"
        + "\n".join(f"- {f}" for f in facts)
    )
    body = json.dumps(
        {
            "model": "deepseek-v4-flash",
            "messages": [
                {"role": "system", "content": "You are a game dev writing release notes."},
                {"role": "user", "content": prompt},
            ],
            "thinking": {"type": "disabled"},
            "temperature": 0.7,
            "stream": False,
        }
    ).encode()
    req = urllib.request.Request(
        "https://api.deepseek.com/chat/completions",
        data=body,
        headers={"Authorization": f"Bearer {api_key}", "Content-Type": "application/json"},
    )
    with urllib.request.urlopen(req, timeout=60) as resp:
        data = json.load(resp)
    return data["choices"][0]["message"]["content"].strip()


def static_tail(template_text: str) -> str:
    """Everything from '## Online' onward: server info / how-to-play /
    known issues -- hand-maintained boilerplate, not per-release facts."""
    marker = "## Online"
    idx = template_text.index(marker)
    return template_text[idx:]


def main() -> None:
    if len(sys.argv) != 2:
        sys.exit("usage: generate_patch_notes.py <new-tag>")
    new_tag = sys.argv[1]
    version = new_tag.removeprefix("v")

    api_key = os.environ.get("DEEPSEEK_API_KEY")
    if not api_key:
        sys.exit("error: DEEPSEEK_API_KEY secret not set (repo Settings -> Secrets -> Actions)")

    prev_tag = previous_tag(new_tag)
    rev_range = f"{prev_tag}..{new_tag}" if prev_tag else new_tag
    subjects = commit_subjects(rev_range)
    sections, internal, unparsed = bucket(subjects)
    blurb = deepseek_blurb(version, sections, api_key)

    lines = [f"# SlopArena {version} — patch notes", "", blurb, ""]
    for name in ("New", "Fixed", "Changed"):
        if sections[name]:
            lines += [f"## {name}", ""]
            lines += [format_bullet(item) for item in sections[name]]
            lines.append("")

    lines.append(static_tail(TEMPLATE.read_text()).rstrip())
    lines.append("")

    footer_bits = []
    if internal:
        footer_bits.append(f"{internal} internal change(s) (docs/chores/tests) not shown")
    if unparsed:
        footer_bits.append(f"{unparsed} commit(s) skipped (non-conventional subject)")
    if footer_bits:
        lines.append(f"_{'; '.join(footer_bits)}._")

    out_path = ROOT / "build" / f"patch-notes-{version}.md"
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text("\n".join(lines))

    title = f"SlopArena {version}"
    existing = subprocess.run(
        ["gh", "release", "view", new_tag], cwd=ROOT, capture_output=True
    )
    if existing.returncode == 0:
        run("gh", "release", "edit", new_tag, "--notes-file", str(out_path))
    else:
        run("gh", "release", "create", new_tag, "--title", title, "--notes-file", str(out_path))

    print(out_path.read_text())


if __name__ == "__main__":
    main()
