# Arcade Phase 2: playable Slopball

**Status:** implemented; Shared/Server and native gameplay checks passed — 2026-10-14.
Human acceptance of feel and readability remains open.

This handoff records the Phase 2 prototype. [Phase 3](arcade-phase-3.md) now supplies
main-menu entry, the locked Go boundary, finished results/retry and persistent bests;
use that handoff for current play instructions.

This slice adds the approved solo receive-and-return challenge to the
[Phase 1 foundation](arcade-phase-1.md). Main-menu entry, locked countdown,
persistent personal records and the finished results flow remain Phase 3.

## Play

Use the canonical Unity project and the [Editor protocol](../contributing/unity-editor-coordination.md).
The existing normal Play Mode domain-reload stall remains; temporarily enable
Enter Play Mode options and disable **Reload Domain** for this prototype.
Restore the original setting after playing.

1. If the local cosmetic prefab is missing, select **Tools → SlopArena → Arcade →
   Phase 2 → Prepare Slopball**. It creates missing original assets and bakes the source.
2. Select **Open FightGuy**, **Open Manki**, **Open Wibou** or **Open Bonk** in that
   menu, then press Play.
3. Receive the ball on the blue player half. Hit it with normal moves/projectiles
   or redirect it with your body, then clear the net into the purple sender half.
   Walls and ceiling allow banks; net contact stays live. Juggle without a touch
   limit. Your first floor touch ends the attempt.
4. **Backspace** or **Try again** reloads the dedicated scene for a clean attempt.
   **Escape** opens the normal local pause menu.

Normal ability aim still applies. Manki's Bazooka uses its ordinary hold/release
and camera direction; it does not become an automatically aimed projectile.
The initial gap permits movement in this prototype. Phase 3 adds the clean locked
Go boundary and the full Arcade entry/replay interface.

## Rules and runtime

The embedded Slopball profile contains a fixed seed, net/floor planes, landing
region, serve gap and capped flight-time progression. It stays in the same baked
artifact as collision. Its typed section is version 2; the Phase 1 section and
ordinary v5 stages retain their existing exports/read behavior.

Shared owns shots, reception, accepted contacts, chronological floor/crossing
goals, returns, re-serves, the fighter half constraint and terminal state. Unity
reads snapshots/events and presents the fighter, ball and counter. Ball and
projectile motion advance in the existing single resolver pass.

An attack before a provisional floor boundary invalidates that later motion
because the hit changes the launch. The path exposed to collision excludes contacts
after a terminal boundary; floor contact wins ties. Competing attacks use the
earliest ball contact, preserving ordinary insertion priority for equal times.

After the first playtest, crossing stays live. A received, redirected ball must
clear the net and land on the sender floor to score once and schedule a fresh shot. An
uncredited sender-floor landing schedules the identical shot with a fresh identity
and zero percent. Neither transition refreshes the fighter or clears ordinary
projectiles; they keep their normal commitments, cooldowns and lifetimes. A full
retry destroys the old scene/simulation/presentation and starts the seed again.

The static court uses original meshes/materials. Slopball now retains the same
camera-only collision mirror as normal stages, keeping orbit and aim views inside
the enclosed room. If the camera retracts into the local fighter, that camera
temporarily hides the fighter's renderers to keep the court visible. Their flags
are restored after rendering; Shared state is unaffected. The prototype stores a
useful normal orbit radius in its own scene.

Repeated scene loads exposed a stale ChatOverlay event callback. Its disable/destroy
paths now detach managed session handlers even when Unity has destroyed the
publisher, with a destroyed-view guard for in-flight notification snapshots.
Backspace also respects the existing chat input boundary.

## Sources

| Part | Location |
| --- | --- |
| Shot profile and validation | [SlopballDefinition](../../src/Shared/SlopballDefinition.cs) |
| Challenge state, goals and scheduling | [SlopballSimulation](../../src/Shared/SlopballSimulation.cs) |
| Ball motion and contact path | [ArcadeBallSimulation](../../src/Shared/ArcadeBallSimulation.cs) |
| Local runner and presentation | [SlopballMatch](../../client/Unity/Assets/Scripts/Runtime/World/SlopballMatch.cs) and [ArcadeBallMatch](../../client/Unity/Assets/Scripts/Runtime/World/ArcadeBallMatch.cs) |
| Native authoring/probes | [Slopball commands](../../client/Unity/Assets/Scripts/Editor/SlopArenaSlopballCommands.cs) |
| Locked court brief | [Slopball Court](stages/slopball.design.md) |
| Collision source | [slopball.unity](../../client/Unity/Assets/Stages/slopball/slopball.unity) |
| Runtime stage | [slopball.arena](../../data/arenas/slopball.arena) |
| Playable scene | [Slopball.unity](../../client/Unity/Assets/Scenes/Prototypes/Slopball.unity) |

The Resources prefab follows the project's existing local/ignored stage policy;
preparation recreates it without an external asset dependency. Native commands
are `sloparena.arcade.slopball.prepare`, `.open --fighter <package>` and
`.run --steps <JSON>` through the owned gateway. The run command uses bounded
ordinary semantic inputs and stops early at a miss; it does not inject hits or goals.

## Verification

Thirteen focused Slopball cases cover seeded/capped shots, return/re-serve ordering,
body banks, net rebounds, fast floors, earlier saves, projectile preservation and
the half constraint. The Phase 1 contact/serialization cases also passed. The full
Shared and Server suites passed; final counts are recorded in the session checklist.

The Shared plugin copy ran under an owned lease and its hash matches the built
assembly. Unity compilation completed with no errors. Native bake/inspection passed
with 18 triangles, one grounded start, a flat floor heightmap, matching source/baked
data, no cosmetic Colliders and no diagnostics. The six court captures were inspected.
All five locked court decisions passed the brief-conformance check, recorded in
the local `.stage-authoring-cache/slopball/production.md` report. This is structural
and implementation conformance; human readability/feel acceptance remains open.

Native ordinary-kit probes each scored one return and launched the next zero-percent
ball: FightGuy Ki Shot; Manki aimed Bazooka plus its normal explosion; Wibou Shuriken;
Bonk normal 3. Selected earlier probes whiffed and were excluded from return evidence.
The passive-body redirects observed during those probes did not damage the fighter.
Every accepted final roster probe had an empty runtime error console.

A native retry after an earned return created a new match instance with zero returns,
shot index zero, cleared A cooldown and no live projectiles. The normal pause menu
froze Shared ticks, ball fields and serve scheduling across command boundaries.
Evidence is in `tmp/arcade-phase2/`: `roster-native.json`, `melee-roster-native.json`,
`manki-bazooka-native.json`, `retry-pause-native.json` and observed runtime PNGs.
The final ball-lab and ordinary Training regression probes passed with no console
errors, preserving their camera collision mirrors. `regression-native.json` also
records the stopped clean Frontend, restored Play Mode settings and free/settled lease.

These are technical and selected-kit checks. Human play still needs to judge the
shared profile at higher difficulty, camera framing across normal orbit/ability
aim, juggling and physical controls. Final art/camera polish belongs to Phase 5.
