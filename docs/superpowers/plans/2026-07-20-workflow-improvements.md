# Workflow Improvements Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reduce manual friction in the SLOPArena development loop — auto-build Shared DLL on edit, protect binary assets, add focused sim-test shortcut, and add branch/PR skill — so DeepSeek V4 and other models can operate confidently without memorizing every manual step.

**Architecture:** Three independent delivery slices: (1) OMP hooks in `.claude/settings.json`, (2) two new skills in `.omp/skills/`, (3) minor `scripts/` helper. No new dependencies.

**Tech Stack:** OMP hooks (PostToolUse/PreToolUse), Bash, dotnet CLI, `gh` CLI (already permitted in `settings.local.json`), existing `scripts/mcp-*.sh` infrastructure.

---

## File Map

| File | Action | Purpose |
|------|--------|---------|
| `.claude/settings.json` | **Create** | OMP hooks: auto-build DLL, block binary writes |
| `.omp/skills/sim-test/SKILL.md` | **Create** | `/sim-test [filter]` — focused xUnit runner |
| `.omp/skills/branch-status/SKILL.md` | **Create** | `/branch-status` — summarise active branches + draft PR |
| `scripts/mcp-check.sh` | **Create** | Smoke-test gamedev-mcp-server is live |

---

## Task 1: OMP hooks — auto-build Shared DLL after edits

**Files:**
- Create: `.claude/settings.json`

- [ ] **Step 1: Create `.claude/settings.json` with the PostToolUse build hook**

  This hook fires after every `Edit` or `Write` tool use. It checks whether the edited path
  contains `src/Shared/` (which covers `client/Unity/Assets/Scripts/Shared/` via symlinks too),
  then runs `dotnet build src/Shared/ --nologo -v q`. Only the last 5 lines are shown so the
  build result is visible without noise. Exit 0 always — a build failure should warn, not block.

  `.claude/settings.json`:
  ```json
  {
    "hooks": {
      "PostToolUse": [
        {
          "matcher": "Edit|Write",
          "hooks": [
            {
              "type": "command",
              "command": "bash -c 'p=$(echo \"$CLAUDE_TOOL_INPUT\" | python3 -c \"import json,sys; d=json.load(sys.stdin); print(d.get(\\\"path\\\",d.get(\\\"file_path\\\",\\\"\\\")))\" 2>/dev/null); echo \"$p\" | grep -qE \"(src/Shared|Scripts/Shared)/\" && dotnet build ~/Projects/SlopArena/src/Shared/ --nologo -v q 2>&1 | tail -5 || true'"
            }
          ]
        }
      ],
      "PreToolUse": [
        {
          "matcher": "Write",
          "hooks": [
            {
              "type": "command",
              "command": "bash -c 'p=$(echo \"$CLAUDE_TOOL_INPUT\" | python3 -c \"import json,sys; d=json.load(sys.stdin); print(d.get(\\\"path\\\",\\\"\\\"))\" 2>/dev/null); echo \"$p\" | grep -qE \"\\.(bin|arena)$\" && echo \"BLOCKED: binary data file — use git to inspect, never overwrite with Write tool.\" && exit 2 || true'"
            }
          ]
        }
      ]
    },
    "permissions": {
      "allow": [
        "Bash(dotnet build *)",
        "Bash(dotnet test *)",
        "Bash(dotnet clean *)"
      ]
    }
  }
  ```

- [ ] **Step 2: Smoke-test the PostToolUse hook**

  Edit any file in `src/Shared/` (e.g., add/remove a trailing blank line in `CombatMath.cs`),
  then save. OMP should automatically print build output ending in `Build succeeded` or a
  compiler error. If nothing prints, verify `CLAUDE_TOOL_INPUT` is being passed (OMP ≥ 0.9
  injects it; older versions may need `--hooks-env`).

- [ ] **Step 3: Smoke-test the PreToolUse binary block**

  Ask the model to write to `data/fightguy_skeleton.bin`. The hook should return exit code 2
  and print `BLOCKED: binary data file`. The Write should be refused.

- [ ] **Step 4: Commit**

  ```bash
  git add .claude/settings.json
  git commit -m "chore: omp hooks — auto-build Shared DLL, block binary writes"
  ```

---

## Task 2: `sim-test` skill — focused xUnit runner

**Files:**
- Create: `.omp/skills/sim-test/SKILL.md`

- [ ] **Step 1: Create the skill file**

  `.omp/skills/sim-test/SKILL.md`:
  ```markdown
  ---
  name: sim-test
  description: Run SLOPArena Shared.Tests with an optional filter. Rebuilds Shared DLL first. Usage — user types "/sim-test" or "/sim-test knockback" or "/sim-test MankiKit". Call this whenever testing sim behavior after a Shared change.
  disable-model-invocation: true
  ---

  # sim-test

  Run the simulation test suite. Always rebuild Shared first so Unity and tests stay in sync.

  ## Usage

  ```
  /sim-test               — full suite
  /sim-test <filter>      — filter by test class or method name substring
  ```

  ## Steps

  1. Rebuild Shared:
     ```bash
     dotnet build ~/Projects/SlopArena/src/Shared/ --nologo -v q
     ```
     Expected last line: `Build succeeded.`

  2. Run tests (with optional filter):
     ```bash
     # No filter:
     dotnet test ~/Projects/SlopArena/tests/Shared.Tests/ --nologo -v n 2>&1 | tail -20

     # With filter (replace FILTER with the argument the user passed):
     dotnet test ~/Projects/SlopArena/tests/Shared.Tests/ --nologo --filter "FullyQualifiedName~FILTER" -v n 2>&1 | tail -30
     ```
     Expected: `Passed! - Failed: 0, Errors: 0`

  3. If tests fail, print the full failure output (not just tail):
     ```bash
     dotnet test ~/Projects/SlopArena/tests/Shared.Tests/ --nologo --filter "FullyQualifiedName~FILTER" 2>&1
     ```
     Then analyse the assertion and the relevant source file before reporting back.

  ## Test files

  All tests live in `tests/Shared.Tests/`. Key files:
  - `CombatMathTests.cs` — knockback scaling, facing
  - `CombatPipelineTests.cs` — full hit pipeline
  - `ServerSimulationTests.cs` — tick-level sim
  - `MankiKitTests.cs` / `MankiLmbTests.cs` — Manki ability coverage
  - `SpellResolverTests.cs` — hitbox collision math
  - `DashTests.cs`, `PhysicsTests.cs` — movement
  ```

- [ ] **Step 2: Verify skill is discovered**

  In OMP, type `/sim-test`. It should invoke this skill immediately without model involvement
  (because `disable-model-invocation: true`). Confirm it runs `dotnet build` then `dotnet test`.

- [ ] **Step 3: Test with a filter**

  Type `/sim-test MankiKit`. Should run only `MankiKitTests.cs` tests.
  Expected output ends with: `Passed! - Failed: 0`

- [ ] **Step 4: Commit**

  ```bash
  git add .omp/skills/sim-test/SKILL.md
  git commit -m "chore: add sim-test skill for focused xUnit runner"
  ```

---

## Task 3: `branch-status` skill — active branches + draft PR helper

**Files:**
- Create: `.omp/skills/branch-status/SKILL.md`

Context: you typically have 4-6 feature branches in flight simultaneously. This skill surfaces
their commit distance from main, last commit message, and whether a PR exists — all in one
call. It also knows your squash-merge convention.

- [ ] **Step 1: Create the skill file**

  `.omp/skills/branch-status/SKILL.md`:
  ```markdown
  ---
  name: branch-status
  description: Show status of all local feature branches vs main — commits ahead/behind, last message, open PR if any. Also helps draft a squash-merge PR description. Run at session start or before switching branches.
  disable-model-invocation: false
  ---

  # branch-status

  Surface the state of all feature branches at a glance and optionally draft a PR.

  ## Step 1: Print branch summary

  Run:
  ```bash
  cd ~/Projects/SlopArena
  for b in $(git branch --format='%(refname:short)' | grep -v '^main$'); do
    ahead=$(git rev-list --count main..$b 2>/dev/null)
    behind=$(git rev-list --count $b..main 2>/dev/null)
    last=$(git log -1 --format="%s" $b 2>/dev/null)
    echo "  $b  (+$ahead/-$behind)  $last"
  done
  ```

  Then list open PRs:
  ```bash
  gh pr list --state open 2>/dev/null || echo "(gh not authenticated or no open PRs)"
  ```

  ## Step 2: If user asks for a PR draft

  For the target branch, run:
  ```bash
  git log main..<branch> --oneline --no-merges
  ```

  Format a squash-merge PR body:
  - Title: derive from the feature name and the commits
  - Body sections: **What**, **Why**, **Testing** (mention which `dotnet test` filter to run)
  - Use the SLOPArena convention: server-authoritative changes note which Shared files changed

  ## Step 3: If user asks to create the PR

  ```bash
  gh pr create --title "<title>" --body "<body>" --base main --head <branch>
  ```

  Always confirm with the user before running `gh pr create`.
  ```

- [ ] **Step 2: Verify skill is discovered**

  Type `/branch-status` in OMP. It should print the branch table. With 5+ branches active,
  expected output resembles:
  ```
    feat/aim-camera  (+3/-0)  fix: cinemachine aim blend
    feature/pvp-phase1  (+12/-2)  feat: PvP match bridge
    ...
  ```

- [ ] **Step 3: Commit**

  ```bash
  git add .omp/skills/branch-status/SKILL.md
  git commit -m "chore: add branch-status skill for branch management"
  ```

---

## Task 4: `scripts/mcp-check.sh` — gamedev-mcp-server health probe

**Files:**
- Create: `scripts/mcp-check.sh`

The gamedev-mcp-server runs locally on port 26356. If Unity isn't open or the server crashed,
all MCP tool calls silently fail or return cryptic errors. This script makes the liveness check
explicit, so a model (or you) can confirm the server is up before attempting a Unity operation.

- [ ] **Step 1: Create the script**

  `scripts/mcp-check.sh`:
  ```bash
  #!/usr/bin/env bash
  # Probe gamedev-mcp-server liveness.
  # Exit 0 = server is alive and responded.
  # Exit 1 = server is down or Unity is not running.
  # Usage: scripts/mcp-check.sh
  #        scripts/mcp-check.sh --quiet   (no output, just exit code)

  QUIET=false
  [ "${1:-}" = "--quiet" ] && QUIET=true

  response=$(curl -s --max-time 2 http://localhost:26356/mcp \
    -H "Content-Type: application/json" \
    -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"omp","version":"1.0"}}}' 2>/dev/null)

  if echo "$response" | grep -q '"result"'; then
    $QUIET || echo "gamedev-mcp-server: OK (Unity is running)"
    exit 0
  else
    $QUIET || echo "gamedev-mcp-server: DOWN — start Unity and wait for the MCP server to bind on :26356"
    exit 1
  fi
  ```

  Then make it executable:
  ```bash
  chmod +x scripts/mcp-check.sh
  ```

- [ ] **Step 2: Test it with Unity open and closed**

  With Unity running:
  ```bash
  scripts/mcp-check.sh
  # Expected: gamedev-mcp-server: OK (Unity is running)
  ```

  With Unity closed:
  ```bash
  scripts/mcp-check.sh
  # Expected: gamedev-mcp-server: DOWN — start Unity...
  ```

- [ ] **Step 3: Add a note to the unity-mcp-gamedev skill to call mcp-check first**

  Open `.omp/skills/unity-mcp-gamedev/SKILL.md` and add after the Quick Reference block:

  ```markdown
  ## Pre-flight check

  Before calling any MCP tool, verify the server is alive:
  ```bash
  scripts/mcp-check.sh
  ```
  If it returns DOWN, all subsequent MCP calls will silently fail. No need to proceed until Unity is running.
  ```

- [ ] **Step 4: Commit**

  ```bash
  git add scripts/mcp-check.sh .omp/skills/unity-mcp-gamedev/SKILL.md
  git commit -m "chore: add mcp-check.sh health probe + pre-flight note in skill"
  ```

---

## Verification checklist

After all four tasks:

- [ ] Edit a `.cs` file in `src/Shared/` — OMP prints build output automatically
- [ ] Ask model to `write` to `data/fightguy_skeleton.bin` — blocked with message
- [ ] `/sim-test` runs full suite and shows pass count
- [ ] `/sim-test CombatMath` runs only CombatMathTests and shows pass count
- [ ] `/branch-status` prints branch table with ahead/behind counts
- [ ] `scripts/mcp-check.sh` exits 0 when Unity is running
- [ ] `scripts/mcp-check.sh` exits 1 when Unity is closed

---

## What this does NOT cover (intentional non-goals)

- `add-character` scaffolding skill — separate project, tracked separately
- Arena file validation subagent — needs ArenaDefinition schema stabilization first
- CI/CD or GitHub Actions changes — out of scope for local workflow
