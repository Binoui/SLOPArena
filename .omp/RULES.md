# SlopArena Project Rules

- NEVER commit or push without explicit permission. "commit" = commit only, "commit push" = commit+push, "push" = push existing.
- Explain the problem and intended change once; trace the affected path internally and
  report decisive evidence. No code changes without go/vas y; approval covers the bounded
  task, not a separate confirmation for each step.
- Implement numeric choices without arguing. Suggest once only if correctness issue, then implement their value.
- Never install anything without asking.
- Server-side simulation is the source of truth for everything — no client-side hacks for gameplay mechanics.
- Live Unity interaction MUST use the project-local gateway around the installed Unity CLI
  and `com.unity.pipeline`: `bun /home/binoui/Documents/projects/sloparena-workspace/scripts/unity-editor-gateway.ts --project-path <approved-canonical-project> -- <command>`.
  Set your own runtime-issued `ORCA_TERMINAL_HANDLE` explicitly; never infer caller identity
  from the active pane. T3 Code callers instead use their harness-injected
  `CODEX_THREAD_ID`/`CODEX_SESSION_ID`; the gateway namespaces their identity and
  retains the same lease protocol. Wait for your own lease, run/restoration, then settled release;
  no ordinary owner-inbox verification delegation. Runtime status is authoritative,
  historical ownership records are not. `blocked`/unknown/ambiguous work never permits takeover.
  Coordinate only concrete overlapping source writes or explicitly required stable-source
  verification; honor existing freezes. Routine gateway work needs no owner messages or
  blanket acknowledgments. Plugin copies still require your own bounded lease. Offline
  player/release builds retain separate approval and confirmed-closed-project requirements.
  NEVER use raw unowned Editor commands, removed Unity MCP tools, `gamedev-mcp-server`,
  `localhost:26356`, or deleted `scripts/mcp-*.sh` wrappers. Follow `docs/contributing/unity-cli.md`.
- General verification follows [`docs/testing.md`](../docs/testing.md).
