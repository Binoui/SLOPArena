---
name: sloparena-build
description: "Verify SlopArena Unity compilation and affected runtime behavior through Unity CLI and com.unity.pipeline after Unity-facing changes or compiler errors; also use for runtime Unity screenshots, UI, HUD, and viewport tasks. Use docs/testing.md to distinguish local iteration from package publishing."
---
# SlopArena Unity CLI Build Gate

For live Editor commands, use your own caller identity and bounded gateway lease from the [Unity CLI guide](../../docs/contributing/unity-cli.md) and [coordination protocol](../../docs/contributing/unity-editor-coordination.md). Run project commands from the selected checkout. Busy ownership waits in the gateway; blocked or unknown status requires recovery.

Run routine Unity work directly; do not discover/message the Editor owner or collect
blanket no-write acknowledgments. Coordinate only concrete source conflicts,
explicit task dependencies/handoffs or exceptional recovery, as defined by the protocol.

Use this skill for runtime visual inspection as well as compilation. For live menus, HUD,
settings, or other runtime UI, first read the
[`Unity CLI screenshot guide`](../../docs/contributing/unity-cli.md#screenshots-and-visual-evidence).
Use `sloparena.ui.status`, `sloparena.ui.navigate`, and `sloparena.ui.viewport` for live
state/navigation/viewport, then await `sloparena.capture.game-view --source screen` for
composited runtime UI. Discover its safe output syntax with `--query screenshot` before
capturing and check `data.result.success`; route camera-only frames through its documented
source options. A screenshot-only task uses its own lease and preserves current mode: do not
recompile, stop, or restart it. Recompile only when actual C# changes require it.

Example discovery and status:

```bash
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- --query screenshot --detail full
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- sloparena.ui.status
```

Website/browser work and user-provided image/mockup review do not launch Unity.

For package move preview, scrubbing, capture, or diagnostics, route through the
[Ability Lab skill](../sloparena-ability-lab/SKILL.md) and
[Unity CLI reference](../../docs/contributing/unity-cli.md).

## Local iteration

Use the existing Editor development content and exercise the affected Ability Lab or
Training path. Recompile only when changed C# or the Shared plugin requires it; prose
and numerical JSON tuning do not require a forced project recompile.

## Integrated change

For Unity-facing code or plugin changes:

1. Build Shared when Shared code or the Unity Shared plugin may be stale:

   Acquire your own bounded gateway hold; run the plugin-copying build from a separate shell while that hold remains active, wait for imports/compilation to settle, then end your hold. Honor explicit source freezes and coordinate only writers whose changes can affect this build or interrupt the Editor; do not ask the current lease holder for permission. Do not copy without your own lease or nest a fresh gateway claim while holding.
   ```bash
   bun scripts/unity-editor-gateway.ts --project-path client/Unity --hold -- editor_status
   ```

   While the gateway hold remains open, run the copy-producing build in a separate shell:
   ```bash
   dotnet build src/Shared/ --nologo
   ```

2. Confirm the live Editor/Pipeline connection:

   ```bash
   bun scripts/unity-editor-gateway.ts status --project-path client/Unity
   ```

   Status is read-only observation. If another owner reports `held` or `releasing`, invoke a gateway command and let it wait for your independent lease; do not exit or manually poll for `free`. If this is your own hold, wait for imports/compilation to settle and end that hold before any later gateway command. Stop on `blocked` or unknown. `free`, `settled: true`, and zero active operations only describe availability at that instant.

3. Request a project recompile and read errors in one dependent batch:

   Pass `--batch <json-file>` to the gateway with:
   ```json
   {"commands":[["recompile"],["get_console_logs","--severity","error","--limit","20"]],"restore":[]}
   ```
   The gateway waits for compile settlement and checks `recompile_status` itself;
   no separate status-monitoring lease is needed.

4. Require zero compiler errors, exceptions or asserts, then exercise the affected path
   with typed CLI commands. Keep dependent setup, scenario, capture and restoration
   in one `--batch`; separate invocations release the lease and may interleave.
   Declare required restoration in `restore`, including approved mode restoration.
   Compilation alone is not runtime proof.

   Use `eval` or `eval_file` for targeted live C# probes. Prefer typed project commands
   over ad-hoc probes: for screenshots, viewport changes, or runtime UI/HUD state use
   `sloparena.capture.game-view` and `sloparena.ui.*` (see `docs/contributing/unity-cli.md`,
   *Screenshots and visual evidence*) instead of temporary eval code.

## Distributable demo

Accepted package changes cross the cook/inspect/roster-refresh boundary below. Verify
the packaged client/server path; do not treat local preview as package verification.

## Accepted package and asset verification

Use the typed Pipeline commands for accepted package work:

```bash
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- sloparena.character.inspect --target <package>
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- sloparena.character.cook --target <package>
```
Inspect is read-only. Cook is the persistence boundary. Require valid inspect status,
`dirtyOrStale: false`, and matching hashes where those fields apply.

## Prohibited workflow

Do not use or recreate:

- `scripts/mcp-*.sh`
- `scripts/mcp-unwrap.py`
- `gamedev-mcp-server`
- `localhost:26356/mcp`
- `xd://mcp__unity_mcp_*`
- the removed IvanMurzak Unity MCP package or MCP-only extensions

If the Unity CLI or Pipeline endpoint is unavailable, report that exact failure. Do not
claim Unity verification from a shell-only build or fall back to MCP.

Canonical reference: `docs/contributing/unity-cli.md`.
