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

Check for a running Editor before `unity open`. The gateway reports a missing descriptor both
when no Editor runs and while one is still starting. A Hub-launched Editor runs as
`unityhub-unity-editor-<version> -projectpath …`, so look for it with
`pgrep -af -- '-projectpath .*client/Unity'` and rerun gateway `status` after a minute.
Opening a project that is already open starts a second Editor that quits at once, but first it
rotates the log: the live Editor keeps writing to `~/.config/unity3d/Editor-prev.log`, and the
older log is lost. Moving `Editor-prev.log` back to `Editor.log` restores the live log's usual
path. This happened on 2026-10-29.

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
Callbacks an `eval` adds (`EditorApplication.update`, `RenderPipelineManager` events) outlive the
batch and Play Mode. Wrap their body in `try`/`catch`, unsubscribe on any exception or when Play Mode
ends, and cap their lifetime; a throwing leftover hook trips Error Pause in later batches.

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
or writing source/cooked files. The persisted loader supplies verified runtime,
pose, catalog and rig prerequisites; missing/invalid pose or rig blocks scenarios.
Ordinary window refresh applies the live gameplay draft even when disk hashes
match. A clean prepared source may report `dirty: false` and
`authoritativePreview: false`. Dirty status is workspace-edit state, not a hit
verdict. Isolated scenarios do not establish saved-package playback, online
behavior or game feel. Use the
[persisted gameplay checks](../characters/adding-a-new-character.md#verify-persisted-gameplay)
for accepted saved content.

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
requested, proposed, before and observed after values. Supported keys are `cameraStyle`
(`Manual`, `Arena`, or `Steady`; also `controllerCameraStyle`), `uiScale`
(80/90/100/110/120/130/140 percent), `targetOpacity` (20–100 percent),
`screenShake` (0–100 percent), `showOverheadDamage`, `reducedFlashing`, `stick8WaySnap` and
`recordSessionLog` (booleans; the last starts a [practice session log](../systems/practice-session-log.md)
with the next Training or Solo match). No scene places the settings owner; it is
created at runtime, so these commands need Play Mode.
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


### Live gameplay clips with a controller

`sloparena.photo.live-begin` records the composited gameplay camera and HUD in
Training, Slopball, Character Course, or Tutorial. It writes numbered JPG frames
and `states.jsonl` below a fresh `--output` directory. Training receipts include
the selected CPU and moving projectile positions and homing targets.
`--from 0 --to 299 --step 1` records five seconds at 60 fps. Capture uses
`Time.captureFramerate = 60` and one Shared tick per rendered frame.
Omit `--camera`, `--yaw`, and `--pitch` to retain the real gameplay camera.
`sloparena.photo.live-end` writes the receipt and restores timing and quality.

For a disposable Training capture, use `sloparena.photo.prepare --stage after_hours
--fighter manki --training true`, then `editor_play`. This reuses stage-view
scene restoration and keeps Training, input, aim, and camera components enabled.
`sloparena.training.arrange --distance 10 --behavior Attack` resets the duel and
places the selected CPU in front of the existing camera. It changes no camera
settings. CPU behaviors are `Idle`, `Attack`, `Shield`, and `Avoid`.
The supported arrangement distance is 1–25 metres.
`photo.prepare --opponent fightguy` selects the CPU and restores the previous
Training P2 fighter afterward. `--damage 60` sets
its starting percent. `--no-cooldowns true` uses Training's existing player
no-cooldowns option, which allows repeated shots for an airborne follow-up.

`sloparena.input.pad-run --timeline tmp/pad-timeline.json --timeout 180` creates a
virtual Gamepad and queues its states before normal Dynamic Input System updates.
The real actions, bindings, `InputController`, and `AimHandler` consume that pad.
It does not inject semantic player input. The file has this shape:

```json
{
  "frames": 300,
  "events": [
    { "frame": 15, "rightStick": [0, -1] },
    { "frame": 20, "rightStick": [0, 0] },
    { "frame": 30, "down": ["South"] },
    { "frame": 60, "leftStick": [0.5, 0] },
    { "frame": 72, "leftStick": [0, 0] },
    { "frame": 100, "up": ["South"] }
  ]
}
```

`frames` is 1–600. Events use ascending zero-based rendered-frame offsets.
At capture's 60 fps, these are also 60 Hz offsets. Fields are patches, so omitted
controls retain their state. `down` and `up` accept case-sensitive Unity
`GamepadButton` names. `leftStick` and `rightStick` are two-component vectors
with magnitude at most one. `leftTrigger` and `rightTrigger` accept 0–1.
The parser rejects unknown fields, duplicate keys, and out-of-range values.
Files must be repository-relative below `tmp/` or `scenarios/`.
The runner removes its device on completion, failure, Play Mode exit, or reload.
`sloparena.input.pad-stop` also removes it and is safe when none is active.

For a scripted Training CPU, add `--target-timeline tmp/target-timeline.json`
to the same `pad-run` command. The CPU receives semantic Shared inputs through
Training's existing NPC input path. Player input still comes from the virtual pad.
The target file has this shape:

```json
{
  "ticks": 300,
  "space": "camera",
  "events": [
    { "tick": 0, "move": [1, 0] },
    { "tick": 40, "jump": true, "jumpHeld": true },
    { "tick": 55, "jumpHeld": false },
    { "tick": 60, "airDodge": true },
    { "tick": 100, "move": [0, 0] }
  ]
}
```

`ticks` is 1–600 and must equal the pad file's `frames`. Target events have
strictly ascending zero-based ticks. `space` accepts `camera`, the default, or
`world`. `move` is a two-component vector of magnitude at most one.
Camera coordinates are right and forward, resolved once from the gameplay
camera's yaw at run admission. World coordinates are X and Z. Full magnitude
requests normal running. There is no separate run button.
`jump` and `airDodge` are one-tick press edges. `jumpHeld`, `shield`, and
`fastFall` are held booleans. A rising `shield` sets the defense press edge.
`airDodge` uses the held movement direction and Shared decides its admission.
`fastFall` supplies the normal Down input, including its rising edge.
Omitted held fields persist. Press edges clear on the next tick.

The target timeline starts on the first virtual pad input frame. The receipt's
`targetStartTick` identifies that first Shared input tick, before it runs.
Live capture `matchTick` identifies the completed tick, so target tick zero
appears at `matchTick = targetStartTick + 1`. Capture may begin before pad-run;
use these receipts instead of assuming capture frame zero equals input tick zero.
The target returns to its configured CPU behavior when pad-run completes, fails,
stops, exits Play Mode, or reloads assemblies. With no target file, CPU behavior
is unchanged. `sloparena.training.target-clear` explicitly clears this runner's
target input and is safe to repeat. Include it in capture restoration alongside
`pad-stop`, `live-end --wait 0`, `photo.stop`, and `photo.restore`.
`sloparena.training.target-test` checks coordinates, holds, edge lifetime, and
rejected inputs without advancing Training.
Training `states.jsonl` receipts include the actual target input, action state,
velocity, damage, accepted hits, and projectile velocity and homing target.

By default a special is a right-stick flick (ADR-0042, amended 2026-10-28): down is Q,
left R, up E and right F. It fires when the stick passes 60% and stays held until the stick
drops below 35%, so return `rightStick` to `[0, 0]` before the next special. `LeftShoulder`
alone grabs, so LB + face-button chords grab instead of casting. The Settings → Controls →
Right Stick: Camera fallback restores the chords (Q `LeftShoulder` + `South`, R
`LeftShoulder` + `West`). User rebindings still apply. The example flicks Q, jumps, then
walks right.

Keep setup, capture, and pad playback in your own gateway batch. Its `restore`
commands must include `sloparena.input.pad-stop`, `sloparena.training.target-clear`,
`sloparena.photo.live-end --wait 0`,
`sloparena.photo.stop`, and `sloparena.photo.restore`, in that order.
Stage-view restoration also restores the selected Game View size and gizmos.
Use `sloparena.ui.viewport` before capture if a particular resolution is required.
Check `live-end`'s `done`, `frames`, and `screen` fields and inspect the frames
before encoding. For contiguous frames starting at zero:

```bash
ffmpeg -framerate 60 -i tmp/clip/%04d.jpg -c:v libx264 -crf 18 \
  -pix_fmt yuv420p -movflags +faststart tmp/clip.mp4
```

## Move review

From stopped Edit Mode, review an admitted character's animation and VFX from ten
simultaneous cameras with one Training take per recipe move:

```bash
python3 tools/move_review.py run nyx
python3 tools/move_review.py run nyx --moves 'real-g1,fake-*' --views behind,front --size 640x360
python3 tools/move_review.py run nyx --dry-run
python3 tools/move_review.py media tmp/moves/nyx/<run>
```

The runner uses your gateway identity and one bounded batch. It waits for its own
lease, prepares Training, captures each move, stops Play Mode, and restores the
saved scene and configuration. On hosts that require scoped long jobs, use
`systemd-run --user --scope python3 tools/move_review.py run nyx`.

Recipes live in `scenarios/moves/<package>.json`. Unity owns their parsing and
validation; Python reads only move ids. See the
[recipe and media guide](../../scenarios/moves/README.md) for forms, inputs,
camera geometry, phase anchors, selection, and output details. Adding or retiming
a move requires updating its recipe. Training uses the installed development
catalog throughout each take so its live animation and VFX bindings remain valid.

| Native command | Mode and result |
| --- | --- |
| `sloparena.moves.prepare --recipe R --output RUN` | Stopped Edit Mode. Validate the recipe and admitted slots, write `plan.json`, and prepare disposable Training. Optional `--moves`, `--views`, and `--size` describe a selected run. |
| `sloparena.moves.take --recipe R --move ID --output DIR` | Ready Play Mode Training. Arrange facing, form, and dummy, settle for 30 uncaptured ticks, and record every view in one take. Optional `--views`, `--size`, `--quality`, and Pipeline `--timeout 180`. |
| `sloparena.moves.abort` | Restore active take resources, timing, quality, camera pin, interpolation, and input ownership. Safe to repeat, including after reload. |
| `sloparena.moves.test` | Check recipe parsing, facing rotation, capture budgeting, phase resolution, and the Nyx recipe without advancing gameplay. |

For a native batch, pass the same `--views` and `--size` to prepare and take.
Enter Play Mode with `editor_play` after prepare and declare this restoration:

```json
{"commands":[],"restore":[["sloparena.moves.abort"],["sloparena.photo.stop"],["sloparena.photo.restore"],["editor_status"]]}
```

`sloparena.training.arrange` also accepts `--facing <degrees>` and `--form 0|1`.
Omitting them retains its existing camera-facing and current-form behavior.
Photo preparation/restoration saves both player and opponent forms.

Frame zero is the state after the first input and Shared step. Every camera's
frame k carries the same `matchTick`; `frames.jsonl` records the observed render
ticks and action boxes. `take.json` is written last, after all JPGs finish. Fresh
capture directories prevent overwriting evidence. Error Pause during a take is
resumed and counted in its receipt. Failed takes retain partial output.

Below `tmp/moves/<package>/<run>/media/`, inspect phase sheets with same-pixel zooms,
per-view H.264 yuv420p clips, synchronized move mosaics, and form montages.
`summary.json` lists completed/missing takes, resolved phases, problems, and paths;
`timing.json` separates gateway and media wall time. `media` rebuilds without the
Editor and rejects skipped frames, unequal camera ticks, and missing images.
Boards at `media/board/form-<n>.html` stay under 30 KB and use absolute image paths.
The owner's thread previews them at 390 px. A child returns the paths without
rendering or sending captures. Views omit HUD and audio in this version.

## Screenshots and visual evidence

Choose the capture path by surface:

| Surface | Command |
| --- | --- |
| Ability Lab action/scenario frames | `sloparena.lab.capture` |
| Runtime menus, HUD, or gameplay including Screen Space - Overlay UI (Play Mode) | `sloparena.capture.game-view --source screen` |
| Camera-only frame or Scene View | `sloparena.capture.game-view --source camera` or `--source scene` |
| PvP stage or Slopball court art from the gameplay camera | `sloparena.stage.views.*` batch in the [stage-authoring workflow](../../skills/sloparena-stage-authoring/SKILL.md#procedural-presentation-and-visual-review) |
| Arcade course gameplay | [Course replay runner](../testing/course-replay.md) |
| Tutorial lessons and its map | `sloparena.tutorial.*` batch in the [tutorial record](../design/tutorial.md#native-commands) |

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
resolution. Set the viewport first when the intended UI layout or on-screen pixel sizes depend on
it: the docked Game View can be far smaller than 1080p, and its frame is then upscaled.

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

**Play Mode and reloads:** `editor_play` enters Play Mode without a domain reload by
default and puts the project's Enter Play Mode Options back when Play Mode ends (also
after a crash, on the next Editor start); `editor_play --reload true` opts into a
fresh domain. Without a reload, static state set before Play Mode survives, so reset
what a test depends on. Never commit an `m_EnterPlayModeOptions` change in
`ProjectSettings/EditorSettings.asset`: 18e7809b captured a temporary `3`, so the owner's
own Play skipped the reload and a stale chat-gate frame muted every input (2026-10-28).
Runtime statics that gate input reset in a `SubsystemRegistration` hook (see
`ChatInputGate.ResetForPlay`). Workflows that need Play Mode still prefer the stage-views
commands or the course replay runner, which also save and restore scenes and
selection.

Before 2026-10-17 a domain reload could hang this Editor: the Pipeline server kept
accepting requests while the domain unloaded, and a command still waiting for the
main thread blocked the unload (main thread on a futex, log silent, lease `blocked`).
The server now stops serving before a reload, fails queued main-thread work so those
waits return, and holds reloads while an async command is running. A request that
arrives during a reload fails as an uncertain command instead of hanging the Editor;
check `status` and rerun it once the Editor has settled.

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

`bind` replaces an existing semantic ID's clip; it does not create one. To add an ID (a new
aim loop, for example), add its `_bindings` entry to `CharacterAssetCatalog.asset` and import
the catalog before `character.json` refers to it: an `eval` of
`AssetDatabase.ImportAsset("Assets/CharacterPackages/<package>/CharacterAssetCatalog.asset")`
imports just that file, where a full `Refresh` would also compile other threads' scripts.
In between, every command that loads the package fails with `reference.animation.unresolved`,
including other threads' commands.
`unbind` clears an ID's clip but keeps its entry, which the cook then reports as missing and,
once `character.json` stops using the ID, as an orphan. To retire an ID, remove its `_bindings`
entry and import the catalog the same way (2026-10-28, Bashie's retired R and E clips).

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

**Reload hazard:** a refresh compiles every script edited since the last compile,
including another thread's, and the reload follows during a later command. The
gateway therefore checks before each batch: when a `.cs`, `.asmdef`, `.asmref` or
plugin `.dll` under `Assets/` or `Packages/` is newer than `Library/ScriptAssemblies`,
it runs `recompile` (and `recompile_status`) first as its own step and reports the
file. Compile errors from someone else's unfinished scripts are reported and the
batch continues with the loaded scripts. `SLOPARENA_GATEWAY_AUTO_RECOMPILE=0` turns
the check off for a batch that must not compile. The check can miss a script edited
just after another thread's compile updated `Library/ScriptAssemblies` (2026-10-27).
If a batch reports "No scripts needed recompilation" but behaves like old code, compare
the edited file's mtime with `Assembly-CSharp-Editor.dll` before debugging further. Prefer typed commands that import
only their own files (`sloparena.stage.art-apply` does) over an `eval_file` that calls
`AssetDatabase.Refresh`.

That check covers scripts only. Edited `.uss` and `.uxml` files are imported when
something refreshes, so after a stylesheet-only change a capture still shows the
old styles. Import exactly the files you edited first, in the same batch:
`eval` of `AssetDatabase.ImportAsset("Assets/UI/FrontendShell.uss", ImportAssetOptions.ForceUpdate)`
(one call per file). It neither recompiles nor imports anyone else's work.

Use `eval_file` only when a genuinely novel probe needs more code than an inline
expression. Pass it an absolute path; a repository-relative path is not found. First check the relevant native and SLOPArena command schemas. Keep
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
