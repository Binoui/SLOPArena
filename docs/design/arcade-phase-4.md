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

**Backspace**, a half-second **Select** hold on a controller, or **Retry** in the
pause menu starts a complete fresh attempt; the control is rebindable as
**Arcade Retry**. A retry restarts in place with a one-second READY…SLOP IT OUT (see the
[retry pass](#retry-pass--2026-10-17)). **Escape/Start** opens normal local
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

`sloparena.arcade.course.retry` retries the live course and waits for the fresh
attempt, reporting latency and the resulting state. `--via call` (default) calls
the retry directly; `--via gamepad` holds and `--via keyboard` presses the bound
Arcade Retry control on a temporary virtual device, also sampling the hold bar.

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

## Retry pass — 2026-10-17

Retry is the rebindable **Arcade Retry** action: Backspace restarts at once, a
controller Select must be held for half a second (a thin bar fills under the
controls line), and the pause menu offers **Retry** below Resume. The controls
line follows the last device and its current bindings. Slopball uses the same
action and pause entry but still reloads its scene.

A Course retry no longer reloads the scene. `LocalSimulationBridge.Restart`
swaps in a clean simulation on the same arena, so fighter, projectiles, queued
effects, targets, checkpoint and clock start over, while the stage, fighter
model, HUD, music and records stay loaded. The fighter renderer, hit feel,
projectile visuals, timeline presentations, sword trails and aim presentation
drop everything from the old attempt, and the camera cuts back to the start.
Retries use a 60-tick READY…SLOP IT OUT; entering from the menu keeps 3-2-1. The
countdown never advances the simulation, so this does not affect times or the
record fingerprint.

Measured in the Editor before the change, a retry froze one frame for
**2.1–2.5 s** while the scene reloaded. The in-place retry takes **1.4 ms** with no
dropped frame. After a retry, replaying the first 260 ticks of the main route
reproduced the first attempt exactly: one target broken, identical fighter
position. A virtual gamepad hold retried after 499 ms with the bar shown; a
0.4 s hold did not retry. Pause **Retry** resumed at normal time scale into a
fresh READY countdown, and a virtual Backspace retried in the next frame. Unity
compilation and the runtime console have zero errors; the Editor, Play Mode
options and records directory were restored. Linux reported Backspace and Escape
as control characters, so binding labels now fall back to the layout's control
names (also fixing those rows in Settings). Physical controller feel and a
player build remain unreviewed. Local evidence: `tmp/retry-slice/`.

## Feel pass — 2026-10-18

The course now gives feedback in the sanctuary's gold, red and jade rather than
FightGuy's blue ki, so other fighters could run it later. The owner picked the
paper-lantern look from the concept board (painted over real captures) on
2026-10-18. There is no screen shake or slow motion, and gameplay is unchanged:
targets keep their hit spheres and nothing here feeds back into Shared.

- **Targets** are gold paper lanterns: ribbed paper lit from inside with a red
  seal turned toward the camera, red lacquer caps, a tassel, a warm halo and a
  pool of light on the floor. The body nearly fills the hit sphere.
- **A break** shows a cream flash, a gold burst and ring, paper shreds that
  flutter down and the candle's flame floating up. A running count ("7/20")
  floats from the spot. A paper pop plays with the next note of the section's
  five-note phrase (D major pentatonic, D5–B5), so the melody climbs in whatever
  order the targets fall. The section's last break adds a soft rolled chord.
- **Sections** are the target ranges between checkpoint prerequisites (5, 10, 15
  and 20 here). The HUD shows one diamond per target, grouped by section, so a
  skipped target is a gap and a missed one turns red.
- **A checkpoint** lights its teal outline jade, with rising motes, once crossing
  it will save. Saving flashes the frame away with a low bowl tone.
- **A missed target**: reaching a checkpoint that cannot save turns its frame
  red. A red light pillar rises over each missed lantern, with an edge marker
  while it is off screen. A toast says "1 TARGET MISSED / Follow the red light
  back to it", the checkpoint line reads "CHECKPOINT NOT SAVED" in red, and two
  wood knocks play with a falling phrase. Reaching the shrine with targets left
  does the same ("2 TARGETS LEFT"). To explain the miss, the client repeats
  Shared's swept sensor test on the authoritative fighter position; Shared alone
  decides saves.
- **The last target** rings the great gong. An "ALL CLEAR!" stamp appears on the
  gold start card and a gold pillar rises at the shrine. The music dips under
  the gong, then swells by 30% for the run home.
- **The finish** throws prayer-flag confetti and gold rings at the fighter's
  feet, with the gate gong and a chime cascade. The results card follows a
  second later.
- **A retry** clears every cue, effect and queued sound.

`SanctuaryCoursePresentationBuilder` (`sloparena.presentation.course.sanctuary.build`)
generates the lantern mesh, textures, materials and prefabs in
`Stages/fightguy_sanctuary/Presentation/Course/`, plus the course's
`CoursePresentationSet` in `Resources/Courses/fightguy_sanctuary.asset`. Rebuilds are
byte-identical. `PresentationBuilderKit.ForFolder` builds presentation that no
character package owns. `CharacterCoursePresentation` plays the set from Shared
course events. A course without a set keeps the plain orbs and silent cues, but
still gets the pips, toasts and edge markers. The seven clips are synthesized by
[`tools/sfx/course_clips.py`](../../tools/sfx/course_clips.py) into ignored
`Resources/Audio/SFX/Course/`.

Changes from the concept: the toast does not name a place, because the course
has no place names; the pillar and edge marker show where to go. The music lift
changes volume only, with no filter. The checkpoint frames light the stage's
existing teal outlines instead of adding new ones.

Verification (local evidence in `tmp/vfx/course/`):

- The scripted main route still completed 20/20 targets with no falls and three
  saves, and the console stayed clean. Over the run the course played 20 pops
  and 20 bell notes, 3 chords, 3 saves, 1 all-clear gong and 1 finish.
- A copy of the route that never shoots target 3 played the missed cue once,
  raised one pillar and left checkpoint 1 red. A keyboard retry from that state
  restored every lantern and cleared every cue within one frame (16.6 ms).
- Before/after captures of every beat are in `after2/`, `missed2/` and
  `finale/`.

The sounds were checked only as waveforms and the effects only in captures:
nobody has heard or played the pass in game yet.

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
