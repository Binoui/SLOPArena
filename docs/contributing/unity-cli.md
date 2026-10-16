# Unity CLI

## Current decision

SLOPArena now uses the Unity CLI with `com.unity.pipeline`. The IvanMurzak Unity MCP
package and its extensions were removed because their Roslyn assemblies conflicted with
Pipeline's live C# evaluation.

Verified on 2026-08-26 with:

- Unity CLI `1.0.0-beta.6`
- Unity Editor `6000.0.78f1`
- `com.unity.pipeline` `0.5.0-exp.1`

Official documentation:

- <https://docs.unity.com/en-us/unity-cli/use-unity-cli>
- <https://docs.unity.com/en-us/unity-production-pipeline/local-tools-cli/unity-pipeline-package>

## Standalone CLI

Use your installed `unity` binary on PATH. For gateway calls, `UNITY_CLI_COMMAND`
may name an explicit executable. Install or upgrade only with user authorization;
inspection commands below do not require upgrading.

```bash
unity --version
unity auth status --format json
unity editors --format json
unity open client/Unity
unity doctor
```

Use `--format json` for automation. Results are written to stdout, errors to stderr, and
command failure produces a nonzero exit code.

### Gateway baseline and upgrades

The shipped [ownership gateway](../../scripts/unity-editor-gateway.ts) uses the
installed Unity CLI without modifying it. The embedded Pipeline fork owns the
lease protocol; see [Editor coordination](unity-editor-coordination.md).
On 2026-10-03 its original gateway was exercised against the canonical Editor for
native ownership, contention, restoration, reload and process-restart behavior.
That historical baseline does not certify new caller setups or CLI versions.
The repository includes mock regressions for admission, failure, restoration,
interruption, caller identity and settled release.

CLI and Pipeline upgrades are separate:

- **CLI:** compatibility depends on `command --runtime-path`, JSON output,
  non-interactive/proxy flags, HTTP routes and result shapes. Before adopting a
  different version, run `node --test --test-concurrency=1
  scripts/unity-editor-gateway.test.ts` from this repository with Node 22.18+.
  Then exercise discovery, command execution, semantic failure, restoration and
  release through your own lease using the actual installed CLI. Mock tests alone
  do not establish live compatibility.
- **Pipeline:** `client/Unity/Packages/com.unity.pipeline/` is a maintained local
  fork, based on upstream revision `17df0ac8d6533a8c22830c89dc85a24c01f30ade`.
  An upstream update must preserve and verify the ownership enforcement and
  lifecycle changes; replacing the folder with stock Pipeline loses them.

The current checkout versions the embedded source, assembly definitions, required
DLLs, metadata, tests and licence. Its upstream-source publication permission is
unresolved; the [ownership proposal](../plans/2026-10-13-unity-dependency-ownership.md)
records a verified upstream base, reconstruction and licensing options. Preserve
the canonical fork and its GUIDs until an approved migration passes ownership
and lifecycle checks. Connection descriptors/tokens
under `Library/`, temporary files and logs remain local; referenced verification
markers and live coordination plans are not disposable output.

## Pipeline connection and shared Editor leases

This section governs live canonical Editor work. Routine Steam release builds
use [the isolated local wrapper](../systems/release-pipeline.md), which does not
operate the canonical Editor or require its lease/shutdown.

Read the canonical [shared Editor protocol](unity-editor-coordination.md)
before operating the main Editor. Each agent runs its own bounded gateway batch.
The gateway waits for a busy Editor, atomically claims its own lease, executes,
restores declared state, and releases only after actual work settles. Invoke it
directly; do not discover/message the current owner, ask for availability or send
ordinary verification requests to their inbox. Coordinate only concrete source
conflicts, explicit task dependencies/handoffs or exceptional recovery under the
shared protocol. Missing, blocked or uncertain ownership never means free.

If the gateway process exits and loses its release token, a settled lease can remain
held indefinitely. A human can select `Window/Pipeline/Recover Editor Lease...`
and confirm the displayed owner/batch. The action rejects busy, blocked or changed
state and revokes the previous token; callers then acquire normally. It is not a
CLI recovery command or timeout takeover. See the shared protocol for the recovery
constraints. Starting/stopping Pipeline alone does not clear session ownership.

The gateway uses the installed Unity CLI and the canonical project's
`Library/Pipeline/.unity-pipeline-port`; never overwrite that descriptor or use
raw `unity command --project-path` to bypass ownership.

Use your own identity under the [caller protocol](unity-editor-coordination.md#caller-and-commands):
Orca's verified terminal, a distinct launcher-configured agent/session pair, or the
session IDs your agent runtime injects (Codex, Claude Code; inside or outside T3).
Missing/invalid identity fails before claim; never borrow another caller's identity
or infer it from an active pane.

For the examples below, define this local shell helper once; it is not an installed
alias and does not modify shell configuration:

```bash
editor() {
  bun scripts/unity-editor-gateway.ts \
    --project-path client/Unity -- "$@"
}

# Observation only: no lease and no Editor command.
bun scripts/unity-editor-gateway.ts \
  status --project-path client/Unity

# Discovery is part of this agent's own lease.
editor --query sloparena --detail compact
```

The project exposes self-describing commands. Discover the relevant command family
with `--query` or `--tag`; use `--detail full` only when its argument schema is needed.
The gateway controls connection/output flags and forces JSON. Stdout is a JSON stream
containing native command results followed by final ownership status; diagnostics go
to stderr. It exits nonzero for transport, typed semantic, compile or native test failure.
When filtering a result, enable `set -o pipefail` and select native `.data` objects,
not the final ownership object.

For dependent commands, pass `--batch <json-file>` before `--` instead of invoking
the helper separately. Input is `{"commands":[["editor_status"],["get_console_logs",
"--severity","error","--limit","20"]],"restore":[]}`. Put required restoration in
`restore`; it runs before release, including after a settled command failure.
Do not retain a lease while doing unrelated coding. Interactive `--hold` is an
explicit exception, not the default. Ctrl-C before acquisition cancels only the wait;
after acquisition the gateway settles owned work/restoration before release.


## Ability Lab agent commands

For gameplay scenarios, run and inspect the native Shared two-fighter scenario
first. Do not generate temporary C# or use `eval` to create opponents or arrange
gameplay. `eval` is reserved for novel diagnostics not covered by a typed command.

Keep open/run/inspect in one gateway batch. Save this as `/tmp/ability-lab-run.json`:

```json
{
  "commands": [
    ["sloparena.lab.open", "--target", "fightguy"],
    ["sloparena.lab.run", "--action", "ground.1", "--ticks", "60", "--distance", "1.2", "--opponent", "idle"],
    ["sloparena.lab.inspect"]
  ],
  "restore": []
}
```

```bash
bun scripts/unity-editor-gateway.ts \
  --project-path client/Unity \
  --batch /tmp/ability-lab-run.json
```

This Edit Mode example intentionally leaves the Lab selected; declare any
task-required restoration in `restore`. Complete shield/miss/grab and capture
batches are in the [Ability Lab guide](../systems/ability-lab.md#agent-facing-workspace-commands).

The Ability Lab UI shows both fighters and scenario controls. The `run`
contract is `--action <canonical-id|grab> --ticks <last-frame>` (0–3600),
`--distance <metres>`, `--opponent idle|shield`, `--damage 0..999`,
`--facing <relative-degrees>`, and `--charge-ticks 0..600`.
Defaults are 60, 2.5, idle, 0, 180°, and zero held ticks.
Charge ticks hold aim, then release; zero means no held input, including for
targeted leaps. A capped charge stays held until the requested release.
Choose a horizon long enough to observe release and recovery.
Outcomes are genuine Shared results: success may mean hit, blocked contact,
miss, grab whiff, or paired capture/release, not guaranteed success.

Use `preview --action <id|grab> --tick <frame>` to seek recorded scenario
frames; frame 0 is the first input at MatchTick 1. `capture --action` uses the
matching recorded run and its options when `--charge-ticks` is omitted.
Explicit `--charge-ticks 0` requests a tap, not a replay of a held charge.
Changing capture's charge creates a new scenario with default opponent settings;
prefer an explicit `run` followed by capture with charge omitted to preserve
your chosen distance, opponent, damage and horizon.
Without a matching run or explicit charge, `grab` preview/capture creates a default
grab scenario; it does not inherit another action's options. Canonical actions
without a matching run retain ordinary authoring preview.
Keep open/run/preview/capture in one batch; see the
[grab capture batch](../systems/ability-lab.md#agent-facing-workspace-commands)
and the [charged-run recipe](../../skills/sloparena-ability-lab/SKILL.md#charged-run-and-recorded-capture).

This scenario frame numbering differs from canonical authoring timeline
preview: authoring cumulative ticks include the requested duration endpoint,
which applies the final authored stage tick (`durationTicks - 1`).

Check semantic `data.result.success` and diagnostics, then inspect
`data.result.scenario`: `frames[]` includes both fighters' state, movement
velocity, knockback velocity, facingYaw (radians), damage, hitstop/hitstun/
blockStun, and interaction phase/IDs/timing. `contacts[]` are accepted Shared
hit/block records with damage, blocked, force, knockback, stun/hitstop and
position. Grab capture/release are `interactions[]`, not fabricated hits;
`presentationEvents[]` and `deaths[]` are observed outcomes. Distinguish a
valid miss from command failure. `inspect` reports current selection, preview,
workspace, scenario, and diagnostics.

`open`, `run`, `preview`, and `capture` are Edit Mode only. Structured
diagnostics cover unsupported mode, missing package/rig, invalid/unavailable
action/draft/options/frame and capture dimension/path/tick/existing-file
errors. Capture rejects unsafe paths/symlinks and removes partial PNGs on
failure; it restores prior scenario/cursor/playback/visibility/camera/render
target. Capture accepts up to 64 distinct ticks, 64–4096 pixel dimensions,
and writes only below repository-relative `.ability-lab-cache/`; no overwrite.

Capture supports repeatable actor-relative `--view front|back|left|right|top|bottom`
(default `current`), or absolute camera angles in degrees via `--camera-yaw` [-180,180]
and `--camera-pitch` [-90,90]. Do not combine a view preset with angles. Orientation
is applied to the capture camera after seeking the pose, not to a camera changed
between CLI calls. Each `captures[].camera` reports actual position/forward/quaternion
and effective overlay/dummy flags.

Both capture and preview accept `--overlays current|none|hitboxes,hurtboxes,bones,trajectory`
and `--dummy on|off`. `none` also hides dummy hurtbox lines and the dummy;
combining it with `--dummy on` is rejected. Preview retains the chosen visibility;
capture restores it. Use a fresh output directory for each capture:

```bash
editor sloparena.lab.capture --action ground.F --ticks 18 --view front \
  --overlays none --output .ability-lab-cache/manki/front-check
```

The workspace prepares current source in memory using existing compilation,
verified poses, catalog and rig without saving, cooking, changing Undo history,
or writing source/cooked files. Persisted authoritative preview still requires
valid cooked runtime/pose/catalog/rig prerequisites; missing/invalid pose or rig
blocks scenarios. A clean prepared source may report `dirty: false` and
`authoritativePreview: false`. Dirty status is workspace-edit state, not a hit
verdict. Isolated scenarios do not establish online behavior or game feel.

For ordinary authoring preview, `open --target` accepts a package ID or
project-relative root below `Assets/CharacterPackages`; preview/capture actions
use canonical IDs (`ground.1`, `air.R`, etc.). Reopening the same target
preserves a dirty draft; switching away is rejected. `inspect` is read-only.
Capture's `captures[]` gives action, requested tick, PNG path, and either
scenario frame/MatchTick or authoring applied stage/local/cumulative tick.
Root result is the restored workspace state. See the
[Ability Lab guide](../systems/ability-lab.md) for the complete contract.

Pipeline wraps typed results under `data.result`; outer command success is transport
status. The gateway propagates semantic failure. To inspect a typed result explicitly:

```bash
set -o pipefail
editor sloparena.lab.run --action ground.1 --distance 1.2 --opponent idle \
  | jq -e 'select(.data != null) | .data.result.success'
```

Discover this family narrowly:

```bash
editor --query sloparena.lab --detail full
```

Routing: use native Pipeline commands for generic Unity tasks and
`sloparena.lab.*` for these domain workflows. Avoid `eval` where a typed
command covers the operation; never invent aliases or obsolete `--slot`
arguments.

### Evaluated presentation inspection

```bash
editor sloparena.lab.inspect --bones bone.head,bone.right-hand
editor sloparena.presentation.inspect --target lab --bones bone.head,bone.right-hand
```

These inspect existing actor transforms, evaluated animation, named bone transforms,
renderer bounds and weapon attachments without refreshing the pose, creating a rig
or initializing an animation graph. Missing observations carry diagnostics, not
idle/root substitutes. The Lab window's transient rig is a valid inspection target.
Standalone runtime inspection uses `--target runtime --entity <decimal-string-id>`
in Play Mode; missing or ambiguous entities fail. `lab.inspect` reports an unavailable
presentation through `presentationError`, separately from its workspace status.

The stopped-Editor Lab path is owner-verified; runtime entity inspection still needs
a Play-Mode smoke in your own bounded gateway batch. Compile success alone is not runtime acceptance.

### Supported preferences and live Training

**Verification limit:** parser regressions passed in the stopped Editor; live
preference application and Training sequence execution remain unverified. Exercise
these commands in your own bounded gateway batch with approved setup/restoration;
no retained Editor owner's permission or response is required.

`sloparena.settings.inspect` reads the existing settings owner without creating it.
`sloparena.settings.apply --patch '<JSON object>'` defaults to dry-run; it reports
requested, proposed, before and observed after values. Supported keys are `uiScale`
(80/90/100/110/120/130/140 percent), `targetOpacity` (20–100 percent),
`screenShake` (0–100 percent), `showOverheadDamage` and `reducedFlashing` (booleans).
Unknown/duplicate keys and invalid types/ranges fail before setters run.
Actual application requires `--dry-run false --confirm true` and persists through
the settings service. Save original values and restore them after verification.
Check `persistenceStatus` and nullable `persisted`; a partial failure is not rollback.

`sloparena.training.run --steps '[{"ticks":1,"slot":"1"},{"ticks":30}]'`
intentionally mutates an existing, unpaused Play-Mode Training match through Shared;
it does not restore gameplay state. Each step has positive integer `ticks`, with
at most 600 total simulation ticks and a 30-second deadline. `slot` accepts canonical
labels `1/2/3/4/A/E/R/F`; slot and `*Pressed` inputs apply only on the first tick of
each step, while held values apply throughout that step. Discover all supported
fields via `--query sloparena.training.run --detail full`. Yaw/pitch input units are
degrees × 100; aim distance is centimeters. IDs cross JSON as decimal strings.
Receipts report actual Shared ticks, participant states, accepted hits and presentation
events. Paused, unsupported or capture-owned sessions reject without stepping/resuming.
This semantic route does not verify physical key bindings or InputController routing.


## Screenshots and visual evidence

Choose the capture path by surface:

| Surface | Command |
| --- | --- |
| Ability Lab action/scenario frames | `sloparena.lab.capture` |
| Runtime menus, HUD, or gameplay including Screen Space - Overlay UI (Play Mode) | `sloparena.capture.game-view --source screen` |
| Camera-only frame or Scene View | `sloparena.capture.game-view --source camera` or `--source scene` |
| PvP stage art from the gameplay camera | `sloparena.stage.views.*` batch in the [stage-authoring workflow](../../skills/sloparena-stage-authoring/SKILL.md#procedural-presentation-and-visual-review) |
| Arcade course gameplay | [Course replay runner](../testing/course-replay.md) |

The native Pipeline `screenshot` command is camera-only and does not capture composited
Screen Space - Overlay UI. Its `--output` accepts an absolute path or a path relative to the
Unity project root, defaults to `Temp/pipeline-screenshots`, and does not call
`AssetDatabase.Refresh`:

```bash
editor screenshot --output "Temp/pipeline-screenshots/menu-camera.png"
```

Native `capture_game_view` and `capture_scene_view` instead use `--save_path`, sandboxed
under `Assets/`; saving triggers `AssetDatabase.Refresh` and imports the PNG, which can
interrupt a live frontend session. For repository-relative evidence paths, use the
project-safe `sloparena.capture.game-view` command below.

For repository evidence (including outside `Assets/`), prefer `sloparena.capture.game-view`.
Its required `--output` accepts only a repository-relative PNG path below `.impeccable/`,
`.ability-lab-cache/`, `client/Unity/Temp/`, or `tmp/`; it rejects absolute paths, existing
files and unsafe paths, and never imports assets. There is no default output path.
Discover the exact options and safe output syntax
before use:

```bash
editor --query screenshot --detail full
```

`--source screen` captures the composited Game View backbuffer and requires Play Mode.
It retains the selected main Game View and waits up to five seconds for a camera render
inside that view's actual rendering scope, then subsequent Editor updates before reading
the same composited target. A view/target change fails rather than certifying unrelated
rendering. This is an observation, not an instantaneous/latest-frame guarantee.
Its `freshness` is `observed-game-render` for this path. Results include
`scene`, `frame`, `renderedFrame`, and `stepped`; require semantic success
(`data.result.success`) and inspect those fields. A missing render or target is an actionable
failure, not a fresh capture or fallback.

When the Editor is paused, default capture fails immediately with instructions. `--step`
explicitly authorizes advancing exactly one frame, after which capture observes the render.
Use `--last-frame` only to explicitly snapshot the existing target while retaining the current
pause/frame; the result is labeled `last-rendered-unverified`. `--step` and `--last-frame` are
mutually exclusive, and neither applies to camera or scene sources. A matching frame or
checksum/hash is not proof of freshness.

For example, request an observed runtime render and check semantic success:

```bash
set -o pipefail
editor sloparena.capture.game-view --source screen \
  --output .impeccable/review/runtime-hud.png \
  | jq -e 'select(.data != null) | .data.result.success'
```

For an explicitly paused Editor, add `--step` to authorize one frame. For an explicitly
unverified existing target, use `--last-frame` instead. Do not combine them.

Runtime UI workflows use typed commands instead of temporary `eval` code. Discover command
schemas first; the UI commands cover currently available live controls and inspection, not
every possible runtime probe:

```bash
editor --query sloparena.ui --detail full
# Current page, mode, viewport, HUD card geometry, and visible buttons
editor sloparena.ui.status
# Page/mode navigation; --step advances exactly one frame when paused
editor sloparena.ui.navigate --mode Solo --step
# Submit a UI Toolkit button by UXML name (for example char-Manki, btn-select, stage-slop_pit)
editor sloparena.ui.click --name btn-select --step
# Set the actual Game View layout before capturing at this viewport
editor sloparena.ui.viewport --width 1920 --height 1080 --gizmos off
```

Capture-output resizing changes the PNG dimensions only; it does not set the Game View layout
resolution. Set the viewport first when the intended UI layout depends on it.

`viewport` verifies the selected size/gizmo settings and reports `completion: settings-applied`
plus `screenSizeMatches`; this is not proof of a newly rendered screenshot. Use capture's
render observation for that. Navigation waits for an activated frontend context, not merely
the default Home enum. Mode navigation submits the real shell controls, preserving identity,
modal and online-room guards; leave a room or close a modal through its live controls first.
Status and click share live document enumeration. A button is clickable only when enabled,
ancestor-visible and hit-testable at its center, so clipped or modal-obscured controls are
unavailable; scroll/reveal them before submitting.

Use `sloparena.ui.status --element <exact-UXML-name> [--document <exact-UIDocument-name>]`
for named geometry without opening, scrolling or navigating UI. The optional `element`
result includes finite world/local bounds, effective ancestor visibility, center
picking and enclosing ScrollView viewport/content/offset/ranges. Missing or ambiguous
names fail explicitly; qualify duplicates with `--document`. Unavailable geometry is
null, not a fabricated zero rectangle. `clippingCoverage: inline-and-scroll-viewports`
is deliberately partial: stylesheet-computed clipping is not fully enumerated, and
center picking proves only that point, not full-element visibility.

Use your own gateway lease and preserve the current mode. A screenshot-only task does not recompile,
stop, or restart the Editor; actual C# changes are what gate recompile. Website/browser work
and user-provided image/mockup review do not launch Unity.

Keep `eval` for genuinely novel probes (private runtime state, one-off diagnostics); do not
write screenshot, navigation, viewport, or HUD-inspection code in `eval` while a typed command
covers the operation.

## Skill sources

Maintained SLOPArena workflows live in repository `skills/`; generated `.agents/skills/`
and `.omp/skills/` adapters expose the same source. See [agent setup](agents.md).
Load relevant project guidance instead of an operational generic Unity skill that
expects a conflicting REST/MCP route. Optional local advisory skills are not public
workflow dependencies. Do not rewrite user-level installations to fix repository
routing; refresh discovery using your assistant's supported reload/new-session path.

## Live Editor commands

```bash
editor editor_status
editor list_open_scenes
editor get_scene_hierarchy
editor get_console_logs --severity error --limit 20
editor recompile
editor recompile_status
editor editor_play
editor editor_stop
```

**Play Mode stall:** this Editor deadlocks when Play Mode is entered with a domain
reload, and the lease stays `blocked` until a user-approved restart. Workflows that
need Play Mode use the stage-views commands or the course replay runner, which save
the Play Mode options, disable domain reload and restore everything afterwards.
Before any other `editor_play`, set `EditorSettings.enterPlayModeOptions` to
`DisableDomainReload` the same way and restore it after `editor_stop`.

## Agent-facing character authoring

Character source files are the agent-facing authoring representation. SLOPArena CLI
commands validate, cook, inspect, and operate on Unity-owned concerns; they are not
intended to replace source editing with a command per property.

Character inspection and cooking both accept a package ID or a project-relative
package root under `Assets/CharacterPackages`:

```bash
editor sloparena.character.inspect --target fightguy
editor sloparena.character.cook --target fightguy
```

The authoring boundary also exposes read-only planning and typed catalog operations:

```bash
editor sloparena.character.cook --target bonk --dry-run
editor sloparena.character.verify --target bonk
editor sloparena.character.bind --target bonk --semantic-id anim.run \
  --asset-path Assets/CharacterPackages/bonk/Animations/bonk_run.anim
editor sloparena.character.unbind --target bonk --semantic-id anim.run
editor sloparena.character.roster.refresh --package-id bonk
editor sloparena.character.assets --target bonk --semantic-id anim.run
```

`cook --dry-run` returns the predicted hashes and output paths without writing
canonical cooked artifacts, generated catalogs, or cook status. Roster admission is
explicit through `sloparena.character.roster.admit` and requires a passing verification.
After an admitted package is recooked, `sloparena.character.roster.refresh` explicitly
repins its existing roster entry to the newly verified version and hashes. It does not
admit a new package or change the selector.

Pipeline wraps the typed command result under `data.result`. Inspect returns the
canonical 16-slot summary, source/cooked hashes, status, stale reasons, and existing
compiler/cooker diagnostics. Cook returns source, cooked-content, and package hashes
when successful. Semantic cook failures return `data.result.success: false` with
structured diagnostics; the outer Pipeline transport command remains successful.
The gateway propagates semantic cook failure; shell consumers can also inspect it:

```bash
set -o pipefail
editor sloparena.character.cook --target fightguy \
  | jq -e 'select(.data != null) | .data.result.success'
```

Choose the verification mode from [`docs/testing.md`](../testing.md):

- **Local iteration:** edit source or assets, exercise the affected Ability Lab or
  Training path, and do not cook a publishing package for every tuning change.
- **Accepted or distributable content:** inspect, cook, verify, and refresh the
  admitted roster entry when the change is intended to ship.

The commands above remain the concrete Unity CLI reference. A failed cook does not
replace the last valid cooked package, generated assets, or persisted cook status.

## Live C# evaluation

`eval` runs C# inside the live Editor on its main thread without a project-level recompile:

```bash
editor eval \
  'return new { unity = UnityEngine.Application.unityVersion, playing = UnityEditor.EditorApplication.isPlaying };'
```

Verified result:

```json
{"unity":"6000.0.78f1","playing":false}
```

**Reload hazard:** after editing C# (including Editor scripts), run `recompile` as its
own command first and check errors before any `eval`/`eval_file` that calls
`AssetDatabase.Refresh` or imports assets. Otherwise the refresh starts an uncorrelated
compile, the domain reload lands during a later command, and Pipeline marks the lease
`blocked` (completion unknown) until an explicitly authorized Editor restart.

Use `eval_file` only when a genuinely novel probe needs more code than an inline
expression. First check the relevant native and SLOPArena command schemas. Keep
task-specific assertions temporary; promote repeated setup/control operations into
the existing typed command seam rather than rewriting them in each session.

Two Unity behaviours have broken eval probes silently:

- `GetComponent<T>() ?? AddComponent<T>()` never adds: a missing component is a
  Unity fake-null object, not C# `null`. Compare with `== null` instead.
- `Vector3.normalized` returns zero below a magnitude of about 1e-5. Sums of tiny
  cross products (small triangles in metres) need scaling first, or the normals go
  black.

## Migration notes

Removed from the active project:

- `com.ivanmurzak.unity.mcp`
- IvanMurzak ProBuilder, Animation, InputSystem, Navigation, and ParticleSystem extensions
- MCP-only Roslyn, ReflectorNet, McpPlugin, and R3 binaries
- `scripts/mcp-*.sh`, `scripts/mcp-unwrap.py`, and `.omp/mcp.json`
- The `com.ivanmurzak` OpenUPM scope

Application dependencies remain, including SignalR and `System.Text.Json` used by the
client lobby code.

## Verification modes

Choose the applicable mode in [`docs/testing.md`](../testing.md):

- **Local iteration:** use the existing Editor development content and exercise
  the affected Ability Lab or Training path. Recompile when code or the Shared
  plugin requires it; do not force a recompile for prose or every numeric JSON edit.
- **Integrated change:** for Unity-facing behavior, confirm Pipeline reachability,
  recompile when required, read current error logs, and exercise the affected runtime.
- **Distributable demo:** inspect and cook accepted packages, refresh admitted roster
  pins, and verify the packaged client/server path.

The standalone CLI and Pipeline package are experimental. If a future Pipeline update
reintroduces dependency conflicts, keep the project on the last verified version rather
than restoring the removed MCP integration.
