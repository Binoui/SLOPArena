# Course replay and capture

Replay saved semantic inputs against a baked Character Course in the canonical
Unity Editor, capture named simulation boundaries and check the observed result:

```bash
bun scripts/sloparena-replay.ts --scenario scenarios/arcade/sanctuary-main.json
```

The runner prints a JSON report and exits nonzero on validation, native command,
capture, expectation or restoration failure. It creates a fresh ignored directory
under `tmp/replays/` by default. Optional arguments:

```bash
bun scripts/sloparena-replay.ts \
  --scenario scenarios/arcade/sanctuary-main.json \
  --output tmp/replays/sanctuary-review \
  --project-path client/Unity
```

`--output` must be a new directory below `tmp/`, `.ability-lab-cache/`,
`.impeccable/` or `client/Unity/Temp/`. Existing directories and symlink traversal
are rejected. Artifacts belong to the selected Editor checkout. A worktree caller
must explicitly name its approved canonical Editor project; the runner never
substitutes another checkout when its default project is unavailable.

## Scenario format

```json
{
  "version": 1,
  "stage": "fightguy_sanctuary",
  "fighter": "fightguy",
  "inputs": "sanctuary-main.inputs.json",
  "captures": [
    {
      "tick": 604,
      "name": "first-gap",
      "camera": { "yaw": 0, "pitch": 25 }
    }
  ],
  "expect": {
    "completed": true,
    "targets": 20,
    "falls": 0,
    "checkpoints": 3
  }
}
```

The input path is relative to the scenario file. `fighter` is a package ID admitted
by that stage's compiled Arcade profile. The stage must already be a baked solo
Character Course with one start and a matching local presentation prefab. This
runner does not author, bake or select a fallback map/kit.

`captures` and `expect` are optional. Capture ticks are positive elapsed course
ticks after Go, with a maximum equal to the requested input duration. A capture
at tick 604 occurs after 604 input ticks have been consumed. Names must be unique,
file-safe labels. Multiple views may use the same tick. Camera yaw is degrees
−360..360; pitch is 0..45. Omit `camera` to retain the current framing.

`completed: false` explicitly checks a partial run; omitting it permits either
terminal state. `targets` checks broken targets, `falls` checks fighter deaths,
and `checkpoints` checks the saved prefix (zero before any save). The current
format accepts up to 36,000 input ticks and 64 captures. If the course completes
early, remaining input chunks are skipped; a requested later capture fails rather
than producing a falsely labelled screenshot.

## Inputs and timing

Inputs reuse the [Training semantic-step format](../contributing/unity-cli.md#supported-preferences-and-live-training):

```json
[
  { "ticks": 20, "moveY": 1 },
  { "ticks": 25, "moveY": 1, "jumpPressed": true, "jumpHeld": true },
  { "ticks": 15, "moveY": 1, "jumpPressed": true, "jumpHeld": true }
]
```

Held values apply throughout a step. Pressed fields, `slot`, `faceToCamera`,
`toggleLock` and `retargetPressed` apply only on its first tick. When the runner
splits a step at a capture or the 600-tick native limit, it preserves held values
and removes those one-tick actions from the continuation. Facing/aim yaw use
degrees × 100; target input aliases are decimal strings, not physical key names.
Movement axes are the existing semantic/world-direction inputs consumed by the
simulation, not mouse-relative recording instructions.

The fresh attempt starts frozen before countdown advances. The first chunk
releases that freeze, waits through the real countdown with neutral input and
starts at Go. Every chunk freezes on its final tick. A capture checks the exact
elapsed tick, changes framing if requested, waits for a fresh composited Game View
frame and confirms that simulation time did not advance. Screenshots include HUD
at 1920×1080; wall-clock delays between commands do not add course time.

The saved Sanctuary fixture is a known ordinary-jump route for the wider Phase 5
layout. It uses normal attacks and movement, all required targets and the three
saves. It is not a route finder or an optimal-time claim. Geometry or kit changes
can invalidate its inputs; inspect the failure and deliberately revise the trace.

## Lifecycle, evidence and restoration

Run from stopped Edit Mode with every open scene saved/named and a Game View open.
The runner validates files locally, then uses exactly one bounded
[gateway batch](../contributing/unity-editor-coordination.md). The usual caller
identity and canonical-project rules apply. It launches the existing local course
scene using the selected stage/package; this checks gameplay and presentation,
not navigation through the Arcade menu.

The typed `sloparena.replay.prepare/run/capture/stop/restore` commands reuse the normal
course input driver, semantic parser, content catalog and capture implementation.
The repository runner owns scenario planning, chunking and result checks. An
all-in-one Editor implementation was considered; keeping orchestration outside
Play Mode reuses the gateway's failure/restoration lifecycle and avoids another
Shared replay system.

Setup temporarily disables domain reload for this Editor's known Play Mode stall,
isolates records under the output directory, suppresses tips, and selects the
capture viewport. The session saves the scene setup/selection, prior Play Mode
options, pause/time scale, relevant course selection, records override and Game
View size/gizmos. Restoration stops only its matching replay session and restores
that saved configuration. Stop and restore are separate declared commands so the
gateway settles the actual Play Mode transition before scene restoration; Unity's
initial playing flag alone does not establish that boundary. A setup failure before mutation makes restoration a
no-op; a failed or foreign session never authorizes stopping unrelated gameplay.

The artifact directory contains:

- `request.json`: scenario/input hashes, requested map/package and execution paths;
- `chunks.json` and `batch.json`: the exact bounded replay plan;
- named PNGs with fresh-render receipts;
- `gateway.stdout.json` and `gateway.stderr.log`: native results and ownership evidence;
- `report.json`: expectation checks, final state, events/hits, observed stage/kit
  provenance, capture receipts and restoration/release evidence;
- `records/`: isolated completed-run records, if any.

The console snapshot retains existing Editor history and timestamps, including
logs from earlier runs. Replay success uses the native receipts, requested result
checks, fresh captures and restoration/release evidence.

SIGINT/SIGTERM is forwarded to the gateway; the runner waits for its settlement
and restoration attempt. It does not automatically retry uncertain commands,
force-release ownership or restart Unity. If completion/restoration is unconfirmed,
keep the artifacts and reconcile the gateway status before another run. A settled
session whose restoration failed retains its ID in Editor SessionState and can be
restored explicitly through an owned batch declaring `sloparena.replay.stop
--session <id>` followed by `sloparena.replay.restore --session <id>`.

Focused runner regressions:

```bash
node --test scripts/sloparena-replay.test.ts
```

Scripted success establishes those inputs and captures against the observed
content. Human/controller gamefeel, menu entry and player-build acceptance remain
separate. See [Traversal Courses](../design/traversal-courses.md) for design and
calibration.
