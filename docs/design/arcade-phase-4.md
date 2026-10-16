# Arcade Phase 4: FightGuy Sanctuary course

**Status:** implemented greybox; Shared/Server and native course checks passed — 2026-10-15.
Human route, controller and gamefeel acceptance remains separate.

## Play

Open the normal Frontend, choose **ARCADE → SANCTUARY COURSE → PLAY COURSE**.
The pilot selects FightGuy and works offline. **MOVEMENT TIPS** is opt-in on the
selection page. Break all 20 gold/red targets, then reach the finish shrine.
The timer starts at Go after the locked three-second countdown.

The existing normal Editor Play Mode domain-reload stall still applies. For local
Editor play, temporarily enable Enter Play Mode options and disable **Reload
Domain**, then restore the original setting afterward. This is the same workaround
used for Phases 1–3; it is not a player-build setting or a course rule.

The [approved revised route](arcade.md#identity-and-authoring-route) winds around
a central sanctuary and descends back to the starting courtyard. A broken bridge
offers a broad jump → R crossing and a four-jump pole detour. Rising terraces
offer jump → E or an offset ledge climb. The summit combines jump → E → R,
with a narrower island detour. Four staggered roofs descend toward the finish.
Simple connecting surfaces and floor arrows make the route continuous.

Required targets sit over shared approaches, landings and roofs, clearable from
solid footing after either branch. The maintainer approved this layout on
2026-10-15 and prioritized traversal over final target placement. All surfaces
remain static. The [stage design brief](stages/fightguy_sanctuary.design.md)
records the approved implementation scope and pending human layout review.
Finished mountain scenery and environment polish remain Phase 5.

The subsequent [Phase 5 spacing revision](arcade-phase-5.md) expands this same
route by about 25%, with short hops, full jumps and double-jump transfers. Its
verification supersedes the earlier geometry and scripted times below.

Targets do not block movement. One damaging normal attack or projectile breaks
one through the existing Shared resolver. Existing aim/lock assistance and the
normal cooked FightGuy kit apply. Target arrows omit the ball/fighter percent
readout because course targets have no damage accumulation.

Three optional checkpoint frames save progress after the first 5, 10 and 15
targets respectively. All earlier targets in that prefix must be broken;
visiting earlier checkpoints is optional. Saved checkpoints never regress.
Falls use normal PvP fighter respawns at the saved location, refreshing the usual
resources/cooldowns while preserving broken targets and the whole-run timer.
Intentional falls are legal. No extra time penalty is added.

**Backspace** starts a complete fresh attempt. **Escape/Start** opens normal local
pause, freezing gameplay and timing without invalidating records. Completion
freezes the result and offers **RETRY** or **ARCADE**. Leaving or retrying an
unfinished run creates no course record. Completed records use ticks; lower times
win. Gameplay changes archive older categories; cosmetic changes preserve them.
The existing Slopball categories and fingerprints are unchanged.

## Authoring and native commands

On an art checkout missing the ignored presentation prefab, run **Tools →
SLOPArena → Arcade → Phase 4 → Prepare FightGuy Course**. Preparation creates
missing original assets, the dedicated scene and its build entry, then uses the
maintained stage baker. It preserves the current scene. `--rebuild true` is an
explicit reconstruction of this generated greybox, including source geometry.
Save drafts before using it.

Through an owned [gateway lease](../contributing/unity-editor-coordination.md):

```bash
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- sloparena.arcade.course.prepare
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- sloparena.arcade.course.open
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- sloparena.arcade.course.status
```

`sloparena.arcade.course.run` accepts the existing Training semantic-step JSON,
up to 600 ticks, and returns real hits, target/checkpoint events and state.
`--from-go true` waits through the real countdown. For a reproducible multi-part
native run, `--freeze-after true` stops simulation at the exact last-input boundary;
the next run installs its inputs before resuming. Keep the entire scenario,
captures and restoration in one gateway batch. Without that boundary, ordinary
human input and automatic lock-on resume between commands and may alter a replay.
This native probe freeze is diagnostic, not a course rule or a player pause.

## Runtime and artifact contract

`CharacterCourseDefinition` contains immutable ordered target identities, spherical
hurtboxes, cumulative checkpoint sensors/respawns and a finish volume. Typed
Arcade section v5 embeds these in the existing arena v6 artifact. Ball sections
v1–v4 and normal PvP v5 exports retain their bytes. Invalid markers, contradictory
prerequisites, ungrounded checkpoint spawns and markers beyond blast bounds are
rejected. The compiled section retains its size limit.

`CharacterCourseSimulation` owns progress, swept hip-position sensors, countdown,
an unsigned 64-bit elapsed clock and terminal completion. Targets join the same
single resolver pass as normal attacks/projectiles, retain normal owner effects,
and never become dummy fighters. Local input aliases 128–254 identify at most 127
targets, with separate ulong collision identities. Destroyed aliases become
ineligible; retry creates a fresh simulation and clears old effects and input.

`CharacterCourseMatch` uses `LocalSimulationBridge` and the existing MatchBase
fighter, HUD, camera, projectile and presentation helpers. Its view renders Shared
state. `ArcadeRecordCompatibility.Course` includes course markers and the existing
fighter/stage gameplay projection with its own rules revision. Completed times
reuse the separate `arcade-records.json` store and its atomic write/backup policy.

## Winding layout verification — 2026-10-15

The ordinary and ability routes both complete the actual revised bake using
the cooked FightGuy kit and normal inputs, with all 20 targets, all three saves
and zero falls. The ordinary route uses no E/R; the ability route uses each
introductory crossing and the single summit combination. All targets in these
runs can be cleared from shared footing without visiting the other branch.

Focused course and ArenaShipping coverage passes **134 tests**; Arcade PvP
admission passes **three Server tests**. Shared/Server builds and Unity compilation
pass. Bake/inspection source hashes match, with resolved references, supported
shaders, one grounded start and no cosmetic Colliders. The final geometry also
removes the coplanar overlaps observed at the connecting galleries.

Normal-speed native runs completed the winding ordinary route in **4,771 ticks
(1:19.51)** and the ability route in **4,180 ticks (1:09.66)**, both without falls.
Retry retained the first best, and the quicker completion replaced it. These are
scripted runs, not human completion pacing. A focused Shared probe also crossed
the first R gap at five startup timings, each at three lateral takeoff positions;
all 15 landed. Later combination probes retain normal E commitment before R.

Native captures cover the three fork approaches, their common landings and the
return toward the visible courtyard. Targets, scoring, kit behavior and the
normal free camera remain unchanged in authority. Geometry changes create a new
record compatibility hash, preserving older times in their existing category.

After trimming the connecting surfaces, the focused 134-test set passed again,
and a final normal-speed native ability run retained **4,180 ticks**, all targets,
all checkpoints and zero falls. Quarter-turn and low camera captures were reviewed
from the upper save. The final native console contains zero errors; the original
stopped Frontend, Play Mode options, tips and records directory are restored, with
ownership free/settled. The player's actual record file was not changed by probes.

Evidence for this earlier layout: `.stage-authoring-cache/fightguy_sanctuary/routing-inspection.json`,
`tmp/arcade-phase4/routing-*.png` and `/tmp/sloparena-course-routing-*`.
Human review still owns route readability, controller difficulty and final target
placement. The earlier fixture's verification below is historical evidence.

## Initial fixture verification — before the routing revision

Focused mechanic coverage checks countdown admission/release, damaging contacts,
single-pass projectile motion, target identity/lock loss, cumulative optional saves,
legal falls, frozen completion, fresh retry, format validation and record categories.
Two real cooked-kit input regressions load the actual baked sanctuary and complete
both the main route and an E/R shortcut without falls. The shortcut skips the first
checkpoint and bypasses two terraces; every target and the finish remain required.

Full Shared: **1,884 passed, two existing skips**. Full Server: **37 passed**,
including rejection of all three Arcade fixtures from PvP assignment. Shared and
Server builds passed; the Shared plugin was copied under an owned bounded hold.
The stage bakes and inspects with matching source/output hashes, 360 collision
triangles, one player start, supported shaders, resolved references and zero
cosmetic Colliders. Ordinary arena exports remain covered by the existing
byte-preservation tests. Comparing the previous and current fingerprint functions
against the real Slopball stage preserves all four roster hashes exactly.

Native menu launch, countdown, all targets and the finish passed with normal
semantic inputs and cooked FightGuy. The main route completed in **4,873 ticks
(1:21.21)** without falls and saved all three checkpoints. A clean Retry preserved
that best while resetting progress, clock, checkpoint and live effects. The E/R
route then completed in **4,444 ticks (1:14.06)**, skipped the first checkpoint and
replaced the slower best. These numbers describe scripted attempts, not human
acceptance or theoretical best times.

A native normal-1 hit broke the first target; unfinished retry created no course
record. A genuine pause preserved the complete explicit fighter, course and tick
snapshot. Two natural falls returned to checkpoint 1 with all six broken targets
preserved, the clock continuing to tick 1,560 and normal cooldown refresh. Ordinary
Slopball launched after leaving the course. Menu/HUD layouts were inspected at
1920×1080 and 1366×768, including opted-in tips and the results controls.

A standalone shortcut run completed in **4,442 ticks (1:14.03)** and its time
survived a fresh records-store file load. Completion stayed frozen after lifting
the native probe freeze, with a usable results cursor. A final presentation-only
pass explicitly imported the changed stylesheet, checked readable Retry/Arcade
button colors and retained all eight bound HUD sprites across normal respawn.
That pass replayed at four times wall-clock speed with the same 60 Hz simulation
step, then restored normal time. The earlier gameplay runs used normal speed.
Native compilation and final runtime consoles have zero errors. The final Editor
is stopped on the original clean Frontend, original Play Mode options and records
directory restored, normal time active, and ownership free/settled.

The initial freely running segmented replay missed one pole target after ordinary
automatic lock-on resumed between commands; reaching the finish correctly refused
completion. That run is not counted as a completion. The controlled run uses the
explicit native probe boundary described above and reproduces the Shared inputs.
No targets, scores, clocks or fighter positions were fabricated for native proof.

Local evidence is under `tmp/arcade-phase4/` and `/tmp/sloparena-course-*`.
The root `TESTING-UNITY.md` lists the remaining human acceptance checks. A scripted
run proves the exercised inputs and presentation, not controller feel or the pace
of a first human attempt. No exported player was exercised in this slice.
