---
name: sloparena-ability-lab
description: "Use when an agent needs native-first Ability Lab runs or scenarios against idle/shield opponents, Shared hit/block/miss/grab outcomes, recorded-frame preview or capture, or workspace diagnostics."
metadata: {"category": "game-dev"}
---
For live Editor commands, use your own caller identity and bounded gateway lease from the [Unity CLI guide](../../docs/contributing/unity-cli.md) and [coordination protocol](../../docs/contributing/unity-editor-coordination.md). Run project commands from the selected checkout. Busy ownership waits in the gateway; blocked or unknown status requires recovery.

# SLOPArena Ability Lab

Use native typed Pipeline commands for workspace inspection and real Shared
two-fighter runs. Do not use `eval` or temporary C# to construct gameplay.
Lab capture renders only Ability Lab preview/scenario frames; it is not the runtime
game-view screenshot route. For runtime menus, settings, or HUD, use
`sloparena.capture.game-view --source screen` and the live
`sloparena.ui.status`/navigation/viewport commands instead (see the Unity CLI
reference, *Screenshots and visual evidence*).
To show the editor itself (timeline, fields and viewport) for a demo or social
clip, use `sloparena.lab.capture-window` with the same action/ticks/view options
and `--frame fit`. See the Ability Lab guide.

## Steps

1. Observe gateway status. If `held` or `releasing`, invoke the next gateway command and let it wait for an independent lease; if `blocked` or unknown, stop. `free`, settled, and zero active operations are availability observations only. Then discover the focused command family:
   ```bash
bun scripts/unity-editor-gateway.ts status --project-path client/Unity
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- --query sloparena.lab --detail full
   ```
2. Open the package and run an actual scenario in one gateway `--batch`.
   Choose distance/opponent deliberately; `run` does not force a hit.
   Use the complete hit/block/miss/grab and capture batches in the
   [Ability Lab guide](../../docs/systems/ability-lab.md#agent-facing-workspace-commands).
   Put required restoration in `restore`; do not split dependent workspace
   operations across leases.
3. Check `.data.result.success` and diagnostics, then assess observations:
   `scenario.frames[]` contains both fighters' state, movement/knockback
   velocity, facingYaw (radians), damage, hitstop/hitstun/blockStun and
   interaction phase/timing. `contacts[]` contains accepted Shared contacts;
   zero-damage blocked contact is still a contact. Grab capture/release are
   in `interactions[]`, not fabricated hit results. Also inspect
   `presentationEvents[]` and `deaths[]`. A successful run can legitimately
   have no contacts; judge semantic outcomes, not just command success.
4. `preview --action <id|grab> --tick <frame>` seeks the same action's recorded
   run. `capture --action <id|grab> --ticks <frames>` captures that matching
   recorded run/options; first run the desired scenario in the same batch.
   Omit capture's `--charge-ticks` to replay the recorded charge.
   Explicit `--charge-ticks 0` requests a tap rather than reusing a held run.
   Changing capture's charge creates a new scenario with default opponent
   settings; use `run` first to choose distance/opponent/damage deliberately.
   Without a matching run or explicit charge, grab uses default scenario
   options, never another action's options; canonical actions retain ordinary
   authoring preview.
5. Use `inspect` for current workspace/run state (the gateway already forces JSON). Capture writes
   PNGs only under `.ability-lab-cache/`, rejects existing files and unsafe
   paths, cleans partial outputs on failure, and restores prior cursor,
   playback, visibility, camera and render target. Mutating commands require
   Edit Mode and report structured failures for invalid mode, missing/invalid
   preview/action/options/frame, or capture input/path issues. Require semantic
   result success; inspect diagnostics.
   `inspect` exposes `activeVfxCount`, `presentationDiagnostics` and the live
   `rigInstanceId`. Hidden preview rigs do not resolve reliably by hierarchy
   path. If a typed transform command is needed, target the observed
   `instanceId:<rigInstanceId>`; inspect again after reload or rig recreation.

`--ticks` on `run` is the last frame (0–3600); defaults are last frame 60,
distance 2.5 m, idle opponent, damage 0, facing 180°, and zero held ticks.
`--opponent` is `idle` or `shield`; damage range is 0–999.
`run --charge-ticks N` (0–600 input frames) holds aim for N ticks, then releases.
Zero means no held input, including for targeted leaps; use an explicit hold
when target acquisition needs it. Reaching a move's charge cap does not release
it early. Choose a scenario horizon long enough to observe release and recovery.

### Charged run and recorded capture

Save this batch as `/tmp/ability-lab-charge.json`, choose a fresh capture output
directory, and invoke it with your own terminal identity already set.
The batch intentionally leaves Wibou selected in the Lab; declare any required
restoration in `restore`. Capture omits charge so it replays the 45-tick hold:

```json
{
  "commands": [
    ["sloparena.lab.open", "--target", "wibou"],
    ["sloparena.lab.run", "--action", "ground.R", "--charge-ticks", "45", "--ticks", "180", "--distance", "5.6", "--opponent", "idle"],
    ["sloparena.lab.inspect"],
    ["sloparena.lab.capture", "--action", "ground.R", "--ticks", "44,80", "--view", "right", "--output", ".ability-lab-cache/wibou/charged-run"]
  ],
  "restore": []
}
```

```bash
bun scripts/unity-editor-gateway.ts \
  --project-path client/Unity \
  --batch /tmp/ability-lab-charge.json
```

The workspace prepares current source in memory using existing compiler,
verified poses, catalog and rig; it does not save/cook, alter Undo history,
or write source/cooked data. The persisted loader supplies verified runtime,
pose, catalog and rig prerequisites; missing/invalid pose or rig blocks scenarios.
Ordinary window refresh applies the live gameplay draft even when disk hashes
match. `dirty` reports workspace edits and may be false while
`authoritativePreview` is false. Never imply isolated scenarios prove online
play or game feel. For persistence and accepted content, use
[Character Workflow](../sloparena-character-workflow/SKILL.md). Full contracts
and CLI routing: [Unity CLI reference](../../docs/contributing/unity-cli.md)
and [Ability Lab guide](../../docs/systems/ability-lab.md).
