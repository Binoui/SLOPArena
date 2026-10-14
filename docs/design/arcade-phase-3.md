# Arcade Phase 3: menu, replay and personal bests

**Status:** implemented; Shared/Server and native integrated checks passed — 2026-10-14.
Human controller and gamefeel acceptance remains separate from native checks.

Arcade is now a main-menu mode. Choose Slopball and a fighter, start from a clean
locked countdown, receive/return balls, and compare the result with a persistent
personal best. Character Course content remains Phase 4.

## Play

Start the normal Frontend, select **ARCADE**, choose a fighter, then select
**PLAY SLOPBALL**. This launches `Assets/Scenes/Arcade_Slopball.unity`, included in
Editor build settings. The launch does not wait for authentication, a lobby,
match allocation or a connection. Existing locally accepted display-name
preferences and the shared frontend shell are reused.

The three-second countdown uses 180 Shared admission ticks. Fighter position,
resources, cooldowns and gameplay ticks stay fixed. Go launches the first ball
immediately; held controls must be released before gameplay input is accepted.
The existing Solo stamp presentation and audio remain presentation only.

The HUD shows returns, the receive/redirect objective and ball percent. A floor
miss freezes the earned result and shows the current count, compatible personal
best and improvement. **RETRY** starts a full clean attempt; **ARCADE** returns to
selection. **Backspace** also retries during play. **Escape/Start** uses normal
local pause; **Leave Match** banks earned returns and returns to Arcade.

The existing normal Editor Play Mode domain-reload stall remains. For local
Editor verification, temporarily enable Enter Play Mode options and disable
**Reload Domain**, then restore the original setting. It is not an Arcade rule
or a player-build setting.

On an art checkout missing the local Resources court prefab, run **Tools →
SlopArena → Arcade → Phase 3 → Prepare menu and Slopball**. Preparation preserves
the existing frontend, adds its Arcade page, recreates missing original Slopball
content through the maintained preparer, and adds the gameplay scene to builds.

## Records and compatibility

Records live in `arcade-records.json` under Unity's `Application.persistentDataPath`,
separately from settings and online identity. Each best is keyed by challenge,
profile, stable fighter package ID and gameplay fingerprint. Failure, voluntary
exit, retry and application/scene teardown preserve the verified return count;
cancelling before Go creates no record. Pausing preserves record eligibility.

A lower run never replaces a better one. A new gameplay category archives earlier
bests; returning to an older compatible category reuses its best. Selection offers
**OLDER BESTS** with their values and dates. Exact package/version/stage hashes
remain record provenance, separate from the comparison key.

The explicit fingerprint includes stage collision, ground, spawn and blast data;
ball/shot tuning and seed; fighter movement, collision and resources; typed combat
operations/capability parameters; and the admitted deterministic collision poses.
It excludes names, descriptions, icons, models, Unity bindings, VFX/presentation
operations and presentation-only capability fields. Cosmetic changes therefore
preserve comparison. A collision-pose edit is a gameplay change.

`ArcadeRecordCompatibility.RulesRevision` is the policy revision for effective
Shared primitives, trusted capability code and challenge rules. Bump it when a
code change alters comparability without changing projected data. New unsupported
operation/capability types require updating the projection before records can be
used. Data changes in the projection are detected automatically.

Writes use a temporary file and replace the prior file while keeping a backup.
Unreadable older data is preserved before a new file is written. Storage failures
keep the best in memory and show that it could not be saved to disk.

## Implementation

| Part | Source |
| --- | --- |
| Countdown and first-serve admission | [SlopballSimulation](../../src/Shared/SlopballSimulation.cs) and [ServerSimulation.Arcade](../../src/Shared/ServerSimulation.Arcade.cs) |
| Gameplay projection | [ArcadeRecordCompatibility](../../src/Shared/ArcadeRecordCompatibility.cs) |
| Persistent categories and codec | [ArcadeRecords](../../src/Shared/ArcadeRecords.cs) |
| Local storage | [ArcadeRecordsStore](../../client/Unity/Assets/Scripts/Runtime/UI/ArcadeRecordsStore.cs) |
| Selection page | [ArcadeSelectController](../../client/Unity/Assets/Scripts/Runtime/UI/ArcadeSelectController.cs) and [Arcade UXML](../../client/Unity/Assets/UI/Arcade.uxml) |
| Gameplay and immutable result capture | [SlopballMatch](../../client/Unity/Assets/Scripts/Runtime/World/SlopballMatch.cs) |
| HUD/results | [SlopballView](../../client/Unity/Assets/Scripts/Runtime/UI/SlopballView.cs) and [Arcade styles](../../client/Unity/Assets/Resources/UI/Arcade.uss) |
| Native setup/observation | [Phase 3 commands](../../client/Unity/Assets/Scripts/Editor/SlopArenaArcadePhase3Commands.cs) |

The normal frontend activation/context/focus lifecycle hosts the new page. Full
retry reloads the complete scene, clearing fighter state, projectiles, queued
effects, input edges and shot progression. The result is captured once at the
Shared terminal boundary; presentation and subsequent frames cannot alter it.
Pause controls gained stable UI names so ordinary native UI commands can exercise
leave/resume without fabricating input or scores.
Repeated Editor Play Mode checks also exposed shared panel scales surviving
teardown. The settings owner now restores its base panel values on destruction,
preventing UI scale from accumulating across Play Mode entries.

## Verification

Ten focused replay/record cases and thirteen Slopball cases passed. Full Shared:
1,857 passed, two existing skips. Full Server: 35 passed. The Shared plugin was
copied under an owned hold and matches the built assembly. Native compilation and
accepted runtime probes have no current errors.

The normal Home Arcade action opened selection and launched the gameplay scene.
A native FightGuy run held movement/attack/jump during countdown without moving,
charging or advancing gameplay ticks. Pause froze countdown and explicit fighter
fields across commands. At Go, a genuine Ki Shot scored one return at tick 115;
the next shot missed at tick 265 and persisted a best of 1.

A live retry after earning a return banked it before destruction and created a new
match at countdown with zero returns, shot index zero, cleared cooldowns and no live
projectiles. A later zero-return miss retained the best of 1. Terminal ticks/result
remained frozen. Native **RETRY** and **ARCADE** button flows passed.

Voluntary **Leave Match** after a genuine return persisted that count. Manki,
Wibou and Bonk each launched from their own menu card, completed countdown,
missed naturally and recorded separate results. Storage checks passed file
round trips/backups, damaged-file preservation and honest feedback on a failed
write; the damaged-file check caught and fixed an uncaught `InvalidDataException`.

Menu/results captures were inspected at 1920×1080 and 1366×768, including 140%
UI scale and reduced flashing. Compact header sizing and scrollable body content
prevent selection text overlapping the fighter cards. The older-best display
uses an explicitly labeled archive fixture in the isolated records directory;
its value is not a claimed gameplay result. Results retain a usable cursor after
pause/resume. The final stopped Frontend, viewport, preferences, Play Mode options
and base panel scale are restored, with ownership free/settled.

Native evidence is under `tmp/arcade-phase3/`; test logs are under
`/tmp/sloparena-phase3-*.log`. Runtime record tests use isolated directories under
`/tmp/sloparena-arcade-phase3-*`, preserving actual player records. The final
root `TESTING-UNITY.md` checklist records remaining native checks and human review.

The exported player and physical controller/audio have not been exercised in this
slice. Human play must still judge the shared shot profile, camera framing and
returning/juggling feel across the roster. Those acceptance/polish goals remain
Phase 5; implementation of the FightGuy Course remains Phase 4.
