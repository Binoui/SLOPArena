# Online Arcade implementation options

**Status:** Recommendations approved 2026-10-19 and recorded in
[ADR-0040](../adr/0040-online-arcade-room-pick-and-match-format.md). Slices 1 and 2 are
implemented; slice 3 (Versus 1v1/2v2) is built and awaits human multiplayer acceptance.
Slice 4 (Course Race and Co-op, protocol 15) is implemented and awaits human playtests. Building slice 2
refined admission, starts and client hosting (below and in the ADR). The
[feature brief](online-arcade.md) owns gameplay and UX; this page covers the
architecture choices that brief needs.

## Where the code stands

| Area | Current behavior |
| --- | --- |
| Room order | Master uses Lobby → Stage Select → Character Select → Match Starting → In Match. Results return to Lobby. |
| Room admission | PvP arenas use `Room:AdmittedArenas`; Arcade uses `Room:AdmittedArcade`. Course entries also name the locked fighter. |
| Client pages | Stage Select offers Slopball and courses. Courses constrain Character Select to their fighter and distinct player colours. |
| Match start | Shared typed formats, immutable rules and player colours reach GameHost and client through the existing contract. |
| GameHost | Validates format/arena and course package/colour admission, builds the Shared rule, and broadcasts course results. |
| Shared | Slopball supports Co-op and Versus. Courses compose the tested solo rules for personal Race or shared-target Co-op. |
| Rollback | World Snapshot 5 covers course progress; World Frame 4 carries course events through the predictor ledger. |
| Results | Stock statistics, Slopball scores, or course finish times, places and DNF target counts for up to four players. |
| Client hosting | `PvPMatch` hosts `OnlineSlopball` or `OnlineCharacterCourse`; Solo retains its dedicated scenes. |
| Stages | Solo starts remain unchanged; online formats derive their grids. No Solo records are written online. |

## 1. Room preparation contract

| Option | Shape | Trade-off |
| --- | --- | --- |
| **A. Stage-first phases (recommended)** | Lobby → Stage Select → Character Select → Match Starting. Stage Select stores a typed **Room Pick**: PvP or Arcade, arena, format and rules. Character Select checks each selection against the pick: a course admits only its fighter, and a Versus format needs a camp per member with camp sizes that fit. Members gain a camp and a colour. | Changes Master's contract, so Master, client and GameHost release together under a protocol bump that refuses mixed versions. |
| B. A mode step before today's order | Lobby → Mode → Character Select → Stage Select. Keeps today's pages in place. | A course must still be chosen before fighters, so PvP and Arcade would follow different orders. |

Results keep returning the Room to Lobby. The brief's Again proposal and member
suggestions add a remembered pick and a per-member suggestion to the same snapshot when
selected.

**Slice 1, as built.** Master moves a Room Lobby → Stage Select → Character Select;
`RoomStartCharacterSelect` needs a chosen arena and `RoomStartMatch` starts from
Character Select. The arena survives departures while two members remain, and a failed
launch returns to Character Select. The client sends its protocol in the hub URL
(`/lobby?protocol=10`), and Master refuses `CreateRoom` and `JoinRoom` from a different
or, on the VPS, missing protocol. The Match contract is unchanged, so the simulation
protocol stays at 10.

## 2. Admission

**Recommended:** `roster.json` gains an `arcade` list. Each entry names its arena, the
formats it supports and, for a course, the locked fighter package. Releases write it to
Master next to `Room__AdmittedArenas__*`, as they already do for `picker`. A shipping
test checks every entry against its baked arena: supported formats exist and the course
fighter matches the arena's allowed packages.

Master reading baked arenas itself is not an option: Master never loads simulation
content.

## 3. Match start contract

**Recommended:** `MatchStartedConfig`, `MatchStartRequest` and the client descriptor gain
a typed **Match Format** (Stock, Volley Versus, Volley Co-op, Course Race, Course Co-op),
its rules (net hits, who serves, the target scheme) and a camp per player. The GameHost
checks that the format matches the arena's purpose and that course players use the
course fighter. Every client builds the same simulation from the descriptor.

## 4. Shared simulation

| Option | Shape | Trade-off |
| --- | --- | --- |
| **A. Formats as focused Shared components (recommended)** | `ServerSimulation` takes the Match Format at construction, following [ADR-0037](../adr/0037-arcade-shared-objects-and-unified-stage-artifact.md). Each format adds an `IMatchRule` for its ending and results. | Extends one tick pipeline; each format is testable on its own. |
| B. A simulation class per mode | Separate Volley and Course simulations. | Duplicates the tick pipeline and contradicts ADR-0037's focused objects. |
| C. Server-only Arcade objects | Clients render the ball and targets from received state without predicting them. | Simplest, but the ball is what players hit; a late ball would feel worse than PvP did before ADR-0039. |

Under option A:

- **Volley Versus** reuses `ArcadeBallSimulation` and adds a rally component: a held
  serve owned by one camp, camp floors that score points, ball percent reset per point
  and a net clamp per camp.
- **Volley Co-op** lets `SlopballSimulation` serve several fighters on one half with a
  shared return count and an end to the run.
- **Course Race** keeps one `CharacterCourseSimulation` per racer. If each racer has
  their own targets, the resolver counts a target hit only in the attacker's set. Each
  racer saves their own checkpoint through the existing `SetRespawnPosition`.
- **Course Co-op** shares one course state between all fighters.

**Starts.** Camps and racers need more than one start. Recommended: author camp starts
and a race grid as Arcade data outside the solo start, so solo record fingerprints stay
comparable. Deriving offsets from the one start is the fallback.

## 5. Rollback coverage

**Required for any option except 4C.** Capture and restore cover the ball, the rally and
serve state (including the serve sequence position) and every course state. Bump
`SnapshotFormatVersion` and extend the restore-equivalence fuzz to Arcade arenas. Arcade
events (serves, returns, points, target breaks) ride the WorldFrame like hits and
presentation events, so re-simulated ticks never play an effect twice. The ball and a
course's target flags are small next to fighter state; measure them against the existing
snapshot budget.

## 6. Client hosting

| Option | Shape | Trade-off |
| --- | --- | --- |
| A. Arcade scenes run online (first recommendation) | The Slopball and course scenes take the rollback bridge. `PvPMatch`'s connection, countdown and results move into a shared online component. | Reuses the scenes' presentation, HUD and cameras, which already read Shared state. |
| **B. `PvPMatch` hosts Arcade presentation (built)** | One online scene loads the Arcade presentation when the arena needs it. | Keeps one online lifecycle; Arcade presentation classes must take explicit dependencies. |

Slice 2 chose B: the Arcade scenes are built around local attempts (retry, records, the
pace trial, a solo countdown), while `PvPMatch` already owns connection, countdown,
teammates' renderers and results. `OnlineSlopball` reuses `SlopballPresentation`,
`SlopballView` and `SlopballCourtShow` with ledger input. Player colours and the camp and
race HUD are new presentation either way.

## 7. Results and records

Results gain format-specific values: camp scores, run returns, finish order and times,
still for up to four players. Online runs don't write Solo personal bests. If Co-op team
bests are selected, they get their own record keys.

## Delivery slices

Each slice needs its own approval and ends at a playable check.

1. **Room flow** (implemented): stage-first phases and the Room entry protocol check in
   Master, the PVP and ARCADE tabs, and Character Select as the last step with Start
   Match. Arcade cards read "coming online". The camp and locked Character Select shapes
   arrive with the formats that use them.
2. **Online Arcade foundations, proven by Co-op Slopball** (implemented): see below.
3. **Versus Slopball** (implemented): 1v1/2v2 camps, held serves, first to 21 points,
   hits across the net, Ball only and camp colours. See the
   [implementation plan](../plans/2026-10-22-versus-slopball.md).
4. **Course Race and Co-op** (implemented): personal/shared targets, per-player checkpoints, mirror colours, a 30-second Race finish window and course Results.

### Slice 2, as built

- **Shared:** `MatchFormat` (`stock`, `slopball-coop`) with its arena check, rule and
  starts; `SlopballCoopMatchRule` admits two to four fighters, and
  `ServerSimulation.BeginSlopballRun` starts a run with no Slopball countdown or input
  release, serving after the normal gap. The default miss budget is zero (unlimited):
  a miss resets combo and heat while returns add to the team total across combos.
  Each serve auto-locks the ball unless manually suppressed; fighter targets are rejected.
  With friendly fire off (the default), attack contacts and grabs exclude teammates
  before collision can consume a projectile. `SlopballCoopMatchRule.FriendlyFire` is
  immutable and is passed through Match setup to both server and client prediction.
  World Snapshot format 2 carries the
  ball and the rally; World Frame format 2 carries Slopball events; the predictor runs
  them through a rollback ledger. Protocol 13 adds the co-op friendly fire selection;
  older clients/hosts cannot join that release. World Snapshot is now format 3 for
  returning projectiles, independently of this fixed Match setting.
- **GameHost:** the start request's `format` picks the rule and arena; Arcade arenas are
  admitted only by their format. The run's seed is random per Match and reaches clients
  in the snapshot. Results append the team's returns, best combo and misses.
- **Master:** `Room:AdmittedArcade` (default `slopball-coop:slopball`),
  `RoomChoosePick(format, arena)`, the Room snapshot's `format` and `admittedArcade`, and
  `format` in the Match start body and the clients' push. `RoomReturnToLobby(matchId)`
  checks the current co-op Match and leader, calls the existing GameHost abort endpoint,
  then uses match completion to reset preparation and notify the roster without clearing
  membership or chat. A stale request cannot stop a rematch; a failed abort allows retry.
  `RoomSetFriendlyFire(enabled)` is leader-only and Lobby-only. Its preference defaults
  off in new Rooms, survives rematches, and is projected only into co-op Match starts.
- **Client:** the ARCADE tab's Slopball card picks co-op when Master admits it; Character
  Select labels the pick; `PvPMatch` hosts `OnlineSlopball` (ball, Launcher, combo board,
  team HUD, ball lock-on); the leader's pause menu offers Return to Lobby. The team HUD
  shows total returns, combo and best combo instead of a miss allowance.
  The Room lobby shows the current co-op friendly fire preference; only the connected
  leader can change it before Stage Select. Match setup freezes the value.
- **Not built:** called serves, multiball and assists (brief proposals), the
  enclosed-court camera hiding the Solo court uses, and release-written Arcade admission.

### Slice 4, as built

`CourseMatchRule` and `OnlineCourseSimulation` compose ordinary course simulations.
Race assigns target identities per fighter and filters contacts before a projectile
can be consumed. Co-op shares target hits and keeps checkpoint visits individual.
`CourseRunResult` carries finish ticks, tied places and DNF progress. Finished and
withdrawn players become spectators; disconnects cannot hold a race open.

`PvPMatch` hosts the existing target/checkpoint presentation with predicted state
and deduplicated course events. Target visibility and bonus damage stages follow
restored state, including a retracted predicted break. Online HUD/results exclude
Solo retry actions and personal records. All four current course bakes have safe
four-player offset starts. Master admits `course-race:<arena>:<fighter>` and
`course-coop:<arena>:<fighter>`; selecting an occupied colour swaps it and unlocks
both players. Protocol 15 requires compatible Master/GameHost/client releases.

## Verification

Follow [testing](../testing.md) for the applicable mode:

- Shared: rule tests per format, restore equivalence with Arcade objects, and GameHost
  versus client-predictor determinism.
- Master, in its repository: phase order, pick validation and admission.
- GameHost: format and arena admission and results.
- Unity: two local clients through Room → Stage Select → Character Select → match →
  Results, then a Steam playtest per slice.

## Docs and terms

Record the approved contract in an ADR. When slice 1 lands, update the Room Flow entry
in [CONTEXT.md](../../CONTEXT.md) and add the new terms (Room Pick, Match Format, Camp)
once they exist in code.

## Risks

| Risk | Mitigation |
| --- | --- |
| A Room contract change across Master and the client, then Match contract changes in later slices | One coordinated release each; Room entry refuses mismatched clients, and Match changes bump the protocol |
| An Arcade field missing from snapshots | The restore-equivalence fuzz covers Arcade arenas |
| Unreadable mirror races | Player colours ship with the first locked format |
| Extra starts archiving solo records | Online starts live outside the solo start |
