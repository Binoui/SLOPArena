# Arcade Phase 3: menu, replay and personal bests

**Status:** implemented; Shared/Server and native integrated checks passed — 2026-10-14.
Human controller and gamefeel acceptance remains separate from native checks.

Arcade is now a main-menu mode. Choose Slopball and a fighter, start from a clean
locked countdown, and play continuously with fresh random serves. A small
combo counter and persistent best provide light feedback.
[Phase 4](arcade-phase-4.md) supplies the FightGuy Character Course.

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

The HUD keeps the ball-play cue prominent, with combo/best in smaller text.
A floor miss resets the combo and sends a new random ball after the short gap;
no results screen interrupts movement or normal kit cooldowns. **Backspace**
(the rebindable Arcade Retry; hold **Select** on a controller, or pause **Retry**)
starts a fresh practice session with a new seed. **Escape/Start** uses normal
local pause; **Leave Match** saves the session best and returns to Arcade.

The existing normal Editor Play Mode domain-reload stall remains. For local
Editor verification, temporarily enable Enter Play Mode options and disable
**Reload Domain**, then restore the original setting. It is not an Arcade rule
or a player-build setting.

On an art checkout missing the local Resources court prefab, run **Tools →
SLOPArena → Arcade → Phase 3 → Prepare menu and Slopball**. Preparation preserves
the existing frontend, adds its Arcade page, recreates missing original Slopball
content through the maintained preparer, and adds the gameplay scene to builds.

## Records and compatibility

Records live in `arcade-records.json` under Unity's `Application.persistentDataPath`,
separately from settings and online identity. Each best is keyed by challenge,
profile, stable fighter package ID and gameplay fingerprint. Failure, voluntary
exit, restart and application/scene teardown preserve the session’s highest combo;
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
| Selection page | [ModeSelectController](../../client/Unity/Assets/Scripts/Runtime/UI/ModeSelectController.cs) and [Mode Select UXML](../../client/Unity/Assets/UI/ModeSelect.uxml) |
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

## Initial Phase 3 verification (historical survival flow)

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


## Second playtest follow-up — 2026-10-15

The court is now 40×40 units with a 20-unit ceiling, reducing all three expanded
room dimensions by a factor of 1.5. Ball size, gravity, opening serves, the net and
fighter spawn remain at their accepted playtest values. The stage profile revision
advances to 3; older records remain archived under their prior gameplay category.

Arcade was binding the normal projectile-effects manager and HUD but omitted
`ProjectileVFXManager.OnTick()` and `HUDManager.Refresh()` after simulation ticks.
The common ball match now calls both, matching Training. This restores travelling
projectile visuals and live cooldown overlays/timers without changing kit rules.
The stage retune command also uses Mesh setters to invalidate render buffers when
resizing existing original assets; serialized geometry and visible geometry agree.

Native menu-to-Slopball verification found the FightGuy projectile configuration
loaded and one live Ki Shot visual. The cooldown overlay showed 93 ticks / 1.6s
at 78% fill, then counted down to 61 ticks / 1.0s. Observed Game View captures are
under `tmp/slopball-feedback/`; Unity compilation and runtime errors are clean.


## Continuous ball play — 2026-10-15

Playtest direction now favors a relaxed ball playground. Every new session uses a
fresh seed supplied to Shared; every fresh ball varies direction and arc. Misses
reset the small combo counter and difficulty, preserve the session best, and
schedule another random ball while fighter movement and kit state continue.
The failure/result overlay is removed. Bests remain local background feedback.

The stage profile adds .35 linear drag, .8 restitution, a wider receiving fan and
±24 ticks of flight-time variation. Launch strength/direction and percent still
come from normal fighter moves. The serve solver accounts for discrete drag and
gravity. Typed Arcade section v3 stores drag; v1/v2 keep zero drag and their bytes.
Rules revision 3 separates these casual-play records from earlier survival runs.

Reference: [Akaneia v0.8 release notes](https://github.com/akaneia/akaneia-build/releases/tag/0.8)
confirm volleyball and a solo auto-return mode. The public repository contains
build/project data; its README requires extracting runtime files from the patched
ISO. No readable ball-physics source was found in its public file tree. Our tuning
and implementation are in the existing Shared ball and stage-profile seams.


## Heavier ball and short/deep serves — 2026-10-15

Stage profile revision 5 raises ball gravity from 2 to 6 while retaining radius .65,
linear drag .35, restitution .8 and the existing move launch response. Opening
flight time is 138 ticks (previously 180), ramping to 108, with ±30 ticks of arc
variation. Landing targets now span X ±6 and Z −14..−2.5, mixing drops just behind
the net with deeper balls instead of the previous narrow Z −9..−5.5 band.
The damped serve solver and continuous playground flow remain unchanged.

The focused Shared check covers 128 independent seeded shots: both sides exceed
20° yaw, pitch spans more than 30°, at least ten short and ten deep balls appear,
and every actual swept ball trajectory clears the net and lands in its authored
receiving region. Human playtesting still decides the desired weight and pace.


## Random launcher origins — 2026-10-15

Stage profile revision 6 chooses a new sender position per fresh serve: X ±8 and
Z 5..7 at the existing 2.3-unit ball height. The narrow depth band preserves
clearance for the fastest short balls. Shared's deterministic shot solver uses
the chosen origin when aiming at the existing receiving region; session seeds,
shot index, continuous play and all ball tuning remain as above. The cosmetic
launcher repositions during the serve gap and remains at that origin while the
ball is live. Typed section v4 stores the optional spawn bounds; older section
versions retain fixed origins and their existing serialized bytes.

Verification: 1,024 independent seeded swept trajectories clear the net and land
inside the receiving region. Full Shared suite: 1,869 passed, two existing skips.
Native recompile/bake/inspection passed; four distinct live serve origins matched
the visible launcher throughout observed ticks, and three misses preserved play.
Restart varied the session seed; console had zero errors. Original Editor state
was restored and player records were isolated. Native evidence:
`/tmp/sloparena-launcher-runtime-result.json`,
`tmp/slopball-launcher/random-origins.png`. Existing high-ball camera framing
remains a separate polish issue.


## Underground club finish — 2026-10-15

The user approved the room composition and its concrete, teal padding, warm
fixtures and court graphics. The finished cosmetic prefab adds one Slopball
club sign, a roof grid and a compact dressed sender. Static glyph meshes reuse
the existing project font resource. Generated meshes, textures and materials are
persisted in the cosmetic prefab; `sloparena.arcade.slopball.finish` reapplies the
finish, and the existing missing-prefab/retune path includes it.

The source scene, collision meshes and baked arena retain their pre-art hashes.
Native compilation and structural inspection pass with no missing references,
unsupported shaders or cosmetic Colliders. Full interior orbit/low captures and
the normal gameplay camera were reviewed. The final live check observed four
distinct serve origins and three natural misses with continuous movement, then
a fresh restart; console errors were zero. Player records were isolated and
Editor state restored. Existing high-ball camera framing remains separate from
this cosmetic pass.

The follow-up playful finish brightens the concrete and sports-floor palette,
mixes coral/mint bays into the teal padding, adds a winking ball and
"HIT BALL FEEL GOOD" motto to the existing sign, and gives the sender a face.
These are presentation changes within the accepted composition. Reapplying the
builder preserves generated asset names so material reuse remains stable. Native
compilation, repeat application, structural inspection and live play passed;
full orbit/low views and the normal gameplay camera were reviewed. The final
play check retained automatic serves after misses and a fresh restart seed.

## Camera wall correction — 2026-10-15

Slopball now builds the existing camera-only collision mirror from the baked
court, so normal orbit and aiming retract inside its walls. At very close range,
the local fighter's renderers are hidden for that camera's render and their
original flags restored afterward. This keeps the fighter mesh from filling the
screen when the camera has little room behind it.

Native checks covered 30 orbit/aim poses at both rear corners and the net, with
3,104 rendered frames and no room/near-plane crossings. All four fighters hid
in the close view and became visible again after moving away. Runtime consoles
were clean after the separately recorded Editor layout error on Play entry.
The source scene, baked court and presentation prefab retain their hashes.
