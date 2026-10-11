---
name: orient
description: "Minimal read-only SlopArena session orientation: current demo goal, working-tree status, and relevant next action. Load technical references only for the current task."
---

# Bounded session orientation

Use when the user needs project orientation. Load only references relevant to the
current task; ordinary work does not need a full skill catalog or historical plan read.

## Execution root

1. Resolve an explicitly supplied game checkout/worktree first. An invalid target is
   a blocker; never substitute the main checkout.
2. Otherwise inspect the current Git root and require both
   `src/Shared/SlopArena.Shared.csproj` and `tests/Shared.Tests/Shared.Tests.csproj`.
   An enclosing workspace may contain a game link; validate the selected game itself.
3. Canonicalize with host path tools or Python `Path.resolve()`; do not require GNU
   `readlink -f`. Set command `cwd` and file paths to that selected game root.
4. Read its `AGENTS.md`. Keep optional planning artifacts at their supplied location.
   For another repository, load its own instructions instead of running game commands.

## Read and report

Run `git status --short --branch` and `git log --oneline -5`. Read `CONTEXT.md`,
`docs/README.md`, the demo plan or technical references only as the task requires.
Report the current goal, relevant uncommitted work, next action and concrete blocker
in at most ten lines. Do not build, inventory every branch or reopen settled choices.
Historical plans remain records rather than current completion evidence.
