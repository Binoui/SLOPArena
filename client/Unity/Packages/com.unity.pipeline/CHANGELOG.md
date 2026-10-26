# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

- Pressing Play in the Editor claims the lease for `human:owner`, so agent batches queue until Stop. Play pressed during an agent batch is cancelled, queued ahead of every agent and started by itself when that batch settles (pressing Play again cancels it); the handoff is atomic and revokes the agent token. While the owner plays, assembly reloads and auto-refresh wait. `Window/Pipeline/Hold Editor for Me` holds the lease in Edit Mode. `editor_play` and Play Mode test runs stay the agent's; the main window title shows a queued or held human lease.
- Stop serving before a domain reload: close the listener, fail queued main-thread work and let admitted requests answer before ownership is persisted, keeping the discovery descriptor for the next domain. A request waiting for the main thread during the unload previously kept a thread in the old domain and hung the Editor.
- Hold domain reloads while a command's returned Task is still running (test runs exempt), so a compile that finishes mid-command reloads after it instead of destroying its continuation and leaving its ownership operation unknown.
- `editor_play` skips the domain reload unless `reload=true`, and restores the project's Enter Play Mode Options when Play Mode ends or on the next Editor start after a crash.
- Add the human-only `Window/Pipeline/Recover Editor Lease...` action for a held, settled lease whose gateway lost its release token. Confirmation is bound to the current session, lease and revision; active/unknown work is refused and the previous token is revoked. No remote recovery endpoint or automatic timeout takeover.
- Correlate package operations with persisted Editor ownership IDs and command identity; keep package status serviceable while work settles, and recover add/remove only from a matching native request or a changed manifest plus registered-package postcondition with a matching resolved version/source. Preserve failures and report unproven reloads as unknown without settling ownership.

## [0.5.0-exp.1] - 2026-08-10

- New `GET /api/progress` endpoint: the currently executing command's task progress, served off the main thread so it answers even while a long synchronous command has the Editor blocked. Command authors report via the new `CliProgress.Report(title, info, current, total, progress)` API (thread-safe, callable from a blocked main thread) or the `CliEditorProgress.DisplayProgressBar` drop-in wrappers around `EditorUtility.DisplayProgressBar`; running `UnityEditor.Progress` items are mirrored automatically. Consumed by the `unity` CLI to render live terminal progress bars during `unity command` / `unity run --command`. (CLI-488, coordinated with CLI-335)
- Detached command jobs (CLI-335): `POST /api/exec` with `"job": true` returns a job id immediately and runs the command in the background — poll `GET /api/job?id=…` for state, live progress, and the retained result, so a client can reattach and collect the outcome after its own HTTP timeout. `POST /api/job/cancel` cancels a queued job outright and requests cooperative cancellation of a running one (long-running command/eval code can check the new public `PipelineCancellation` API). Results are retained for 1 hour / last 100 jobs; jobs do not survive domain reloads.
- The `eval` timeout cap is raised from 30 seconds to 24 hours (CLI-335) — long evaluations are legitimate now that clients can set custom timeouts or detach and reattach.
- The HTTP server now processes requests concurrently: an in-flight `/api/exec` command no longer blocks every other request (`/api/status` probes and `/api/progress` polls answer immediately, so a busy Editor no longer looks unreachable during a long command). Command execution itself is still strictly serialized — one `/api/exec` at a time, queued in arrival order, exactly as before. (CLI-335)
- Hardening for the above: a command's own timeout (not a fixed 60s) now governs how long `/api/exec` waits for it, `editor_status`/`test_status` queue behind the same exec gate instead of racing an in-flight command, and progress/job state is owned per server instance so a test server can never cross-attribute progress or job cancellation with the live server. Detached jobs are capped at 100 queued/running at once — further submissions get `429` until some finish. (CLI-335)
- Fix `executedAt` being returned as the zero-value instead of the actual execution time for `eval`/`eval_file`, `hot-reload`, and `run_tests`/`test_status`.
- New `audit` / `audit_status` commands: start a Project Auditor static-analysis scan and poll it, producing a CSV of the reported issues (`Category, Severity, Areas, Description, RelativePath, Line, DescriptorId, Recommendation`) for a client or coding agent to act on. Project Auditor is reached by reflection, so the package still compiles and runs in Editors without it, and `--categories` works against both its deployments. An Editor whose Project Auditor has no analysis rules — in a built-in-module Editor these ship in the separate `com.unity.project-auditor-rules` package — reports `unavailable` and names the package to install, rather than an empty scan that reads as a clean project.
- [UUM-148802] Fix `get_console_logs`/`clear_console` and `console` silently losing all console capture after exiting Play Mode (recovering only on the next recompile): re-subscribe to Unity's log callback on every play-mode transition instead of only once at domain load.
- [UUM-148605] Persist `set_autotick`'s enabled/interval state in SessionState so it survives a domain reload instead of silently turning itself off after every recompile. Add a `persist` flag (default `true`) to opt a one-off/expensive setting (e.g. `interval_ms=0`) out of that persistence.
- Emit a busy/settling signal while the Editor is still importing/compiling after the server starts (cold project import), instead of letting early commands fail with an opaque null-data envelope. `/api/status` reports `settling` (withholding `ready`) until the Editor is first seen idle, and `/api/exec` rejects main-thread commands during that window with HTTP 503 and a structured envelope (`error: "Server Busy"`, `status: "busy"`, `retryable: true`). Background commands (e.g. `recompile_status`, `package_status`, `console`) and `editor_status` stay servable so callers can observe progress. The gate is scoped to the editor session's initial settle window: warm starts settle immediately, and servers recreated by domain reloads or started during mid-session imports/compiles never re-arm it. (AUTHAPI-35)
- [UUM-149016] Fix `InvalidOperationException`s logged after running `run_tests` more than once: a stale `TestResultCollector` left registered with Unity's TestRunnerApi kept receiving later runs' completion notifications and re-completed its own already-completed result.

## [0.4.0-exp.1] - 2026-07-23
- Persist the pipeline server auth token across editor domain reloads so long-lived clients (MCP/IDE) no longer get `401` after a recompile. (CLI-412)
- `capture_game_view` gains a `source` parameter: `camera` (default, unchanged) or `screen`. `source=screen` captures the composited game view backbuffer so Screen Space - Overlay UI (HUDs, menus) is included — the camera path never sees overlay canvases. Screen capture requires Play Mode; in Edit Mode it returns a clear error. (AUTHAPI-10)
- `capture_game_view`/`capture_scene_view` with `save_path` now return a path-only result (no base64) so agent tool results stay small; pass `include_inline_image=true` for the old behavior. Add `max_resolution` to cap the inline image size. (AUTHAPI-8)
- Object-reference string handles now accept authoring-root-relative asset paths (e.g. `Materials/Floor.mat`, not just `Assets/Materials/Floor.mat`): a relative string with a file extension is treated as an asset path and normalized under the authoring root, and a failed lookup now reports every strategy tried instead of a misleading hierarchy-path-only error. (AUTHAPI-9)

## [0.3.1-exp.1] - 2026-07-16
- Update docs

## [0.3.0-exp.1] - 2026-07-13

- Improve security by ensuring token usage and enforcing read control on the token.
- Fix all upm-pvp warnings.
- Fix Samples installation.
- All server works if the App is minimized (in the RunInBackground).
- Rework all docs.
- Improve connectivity regaridng IPv4 vs IPv6 support.
- Add `eval_file` command: evaluate C# code read from a `.cs` file on disk, as a file-based alternative to `eval` (which takes inline `code`). Both commands run the source through the same evaluation path.
- Add a large set of Editor automation commands for agentic content-pipeline control:
  - **Assets & files:** `create_asset`, `import_asset`, `move_asset`, `copy_asset`, `rename_asset`, `delete_asset`, `find_assets`, `create_folder`, `get_import_settings`, `set_import_settings`, `read_text_file`, `write_text_file`.
  - **Scenes:** `create_scene`, `open_scene`, `save_scene`, `save_all`, `list_open_scenes`, `set_active_scene`, `get_scene_hierarchy`, `add_scene_to_build`, `remove_scene_from_build`.
  - **GameObjects & components:** `create_gameobject`, `create_gameobjects`, `delete_gameobject`, `find_gameobjects`, `rename_gameobject`, `set_parent`, `set_transform`, `set_active`, `set_tag`, `set_layer`, `add_component`, `remove_component`, `get_component_properties`, `set_component_properties`.
  - **Prefabs:** `create_prefab`, `create_prefab_variant`, `instantiate_prefab`, `apply_prefab_overrides`, `revert_prefab_overrides`, `unpack_prefab`, `save_prefab_contents`.
  - **Scripts & serialized fields:** `create_script`, `attach_script`, `get_serialized_fields`, `set_serialized_field`.
  - **Selection & search:** `get_selection`, `set_selection`, `search`.
  - **Capture & screenshots:** `screenshot`, `capture_game_view`, `capture_scene_view`, `capture_editor_element`, `capture_runtime_element`.
  - **Build:** `build`, `build_status`.
  - **Console & diagnostics:** `console`, `clear_console`, `get_console_logs`, `get_performance_stats`.
  - **Editor menus & authoring root:** `menu`, `get_authoring_root`, `set_authoring_root`.
  - **Materials & shaders:** `get_material_properties`, `set_material_properties`, `get_shader_properties`, `list_shaders`.
  - **Animation & Animator:** `create_animation_clip`, `get_animation_clip`, `set_animation_curve`, `remove_animation_curve`, `create_animator_controller`, `get_animator_controller`, `add_animator_layer`, `add_animator_parameter`, `add_animator_state`, `add_animator_transition`.
  - **Timeline:** `create_timeline`, `get_timeline`, `add_timeline_track`, `add_timeline_clip`.
  - **Lighting:** `bake_lighting`, `cancel_lighting_bake`, `lighting_bake_status`, `clear_baked_lighting`, `get_lighting_settings`, `set_lighting_settings`.
  - **NavMesh:** `bake_navmesh`, `bake_navmesh_surfaces`, `cancel_navmesh_bake`, `navmesh_bake_status`, `clear_navmesh`, `get_navmesh_settings`, `set_navmesh_settings`.
  - **Occlusion culling:** `bake_occlusion_culling`, `cancel_occlusion_bake`, `occlusion_bake_status`, `clear_occlusion_culling`.
- Update Wrench
- Warn user if Unity Editor is started in non-automated mode.

## [0.2.0-exp.2] - 2026-06-24

- Fix security audit flaws
- First official published version

## [0.1.0-exp.1] - 2026-06-09

### This is the first release of _Unity Pipeline_.
