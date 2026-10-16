---
name: sloparena-finish-branch
description: "Prepare and verify a SLOPArena branch, then perform explicitly authorized commits, pushes or PR creation. Use for finish-branch or ship requests."
metadata: {"triggers": ["finish", "pr", "pull request", "ship"], "invocation": "explicit"}
---

# Finish a SLOPArena branch

Prepare a reviewable branch and perform only the commit, push and PR operations
explicitly authorized by the user. Loading or invoking this skill alone is not
"commit push" permission. A request to prepare a PR can be completed locally with
a title, body and reviewed diff before any missing publication approval is needed.

## Scope and verification

Resolve the selected checkout, current branch, requested base and dirty state.
Preserve unrelated staged, unstaged and untracked work. Do not commit on `main`
without explicit authorization; use an approved feature branch for PR work.
Inspect the actual branch diff and any in-scope working changes, not just its log.

Choose checks from [docs/testing.md](../../docs/testing.md) for the changed surface.
Offline Shared tests/builds use `-p:SkipUnityPluginCopy=true`. Relevant failures
must be resolved or accurately reported before delivery; do not repeatedly run
unrelated full suites. Include required human/Unity acceptance as pending when
unavailable instead of substituting compilation evidence.

## Authorized commits

Use a user-supplied subject, otherwise derive a Conventional Commit subject from
actual behavior under [commit conventions](../../docs/contributing/conventions.md).
Stage only approved paths/hunks with `git add -- <paths>`; inspect the full staged
diff before committing. Existing staged changes are not automatically yours.
Never use `git add -A` as a branch-finishing shortcut in a shared dirty checkout.

Preserve existing history unless the user requested squash/rewrite. A requested
squash needs a reviewed base and a snapshot containing only intended work. Do not
run an unconditional soft reset to `main`, amend another contributor's work or
rewrite published commits merely because this workflow was invoked.

## Authorized push and PR

Push normally to the approved remote/branch. A non-fast-forward rejection requires
investigation; it is not automatic permission to force push. Use force-with-lease
only for an explicitly approved rewrite after verifying the remote expectation.

For an authorized PR, write a concrete title and body: behavior change and reason,
relevant issue closure if established, checks/results, and any Unity checklist
from ignored `TESTING-UNITY.md`. Keep diffstats fenced if included. Prefer a
structured host tool; with `gh`, write actual newlines to a temporary body file
and pass `--body-file`. Check existing PRs to avoid duplicates. Do not install or
start authentication without permission. Register the URL with the current app
when it exposes that capability.

Report the performed operations, commit/PR URL and remaining validation. Leave
branch deletion and merging to the user unless separately requested.
