---
name: branch-status
description: "Show status of all local feature branches vs main — commits ahead/behind, last message, open PR if any. Use only for requested branch inventory, branch switching/integration, or PR drafting."
---

# Requested branch inventory

Use only for requested branch status, switching/integration planning or PR drafting.
Read the selected checkout's instructions and current dirty state first. Do not
switch branches, fetch, rewrite history or alter files for an inventory request.

Resolve the requested base, otherwise inspect `refs/remotes/origin/HEAD` and the
local default branch. If unavailable, report that rather than assume an arbitrary
base. Use `git for-each-ref --format='%(refname:short)' refs/heads/` for local branches,
`git rev-list --count <base>..<branch>` and its inverse for ahead/behind, and
`git log -1 --format=%s <branch>` for the last subject.

If authenticated host access is available, query open PRs for requested branches.
A failed lookup is unknown, not proof that no PR exists. Do not initiate login or
install a CLI as a side effect of inspection.

## Drafting or creating a PR

For the requested branch, inspect its actual diff/log against the chosen base.
Describe the concrete behavior change, relevant issue links and performed checks.
Include a Unity checklist only when applicable; do not claim unrun checks passed.
Do not infer an issue closure from a similar title.

Draft-only requests produce a reviewable title/body. A user request to create the
PR authorizes creation within that scope; do not ask for the same permission again.
Commit and push remain separate authorizations. Prefer a structured host tool or
write the exact body to a temporary file and use `gh pr create --body-file <file>`.
If the current app exposes PR registration, register the resulting URL there.
Report the URL and actual verification limitations.
