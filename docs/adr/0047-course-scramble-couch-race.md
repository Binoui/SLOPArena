# ADR-0047: Course Scramble, a points race on one screen

**Status:** Accepted for the couch prototype — 2026-10-28. The maintainer chose shared targets with points
("i like this shared targets with points system") and a leader camera for couch races. Online play may
adopt it later; this slice ships it in Local Play only. Values under
[Prototype defaults](#prototype-defaults) are the coordinator's and stay open to the maintainer's play verdict.
**Extends:** [ADR-0046](0046-courses-open-to-every-fighter.md) (any fighter on any course),
[ADR-0041](0041-couch-play-in-solo.md) (Local Play seats) and
[ADR-0040](0040-online-arcade-room-pick-and-match-format.md) slice 4 (Race and Co-op composition)

## Context

Courses are becoming racing maps for friends. Online Race gives every racer a private copy of each
target, which works when each player has a screen. On one shared screen the camera can only follow one
place. A leader camera that pulls stragglers forward would skip targets they still owe under Race rules.

## Decision

1. **Course Scramble** is a third course format beside Race and Co-op: two to four fighters, one shared
   set of targets, points to whoever breaks each target. `OnlineCourseSimulation` takes a format value
   (Race, Co-op, Scramble) in place of its co-op flag; Scramble reuses Co-op's shared target collision
   and personal checkpoints and Race's places and finish window. All Scramble state is in the World
   Snapshot so a later online format needs no new pipeline. Wire name `course-scramble`, reserved and
   not admitted online in this slice.
2. **Scoring.** A target scores for the fighter whose hit breaks it; a multi-hit bonus target scores for
   its last hit. The finish opens once every target is broken. Finishing adds a bonus by place. The
   race ends when everyone has finished or the finish window closes. Most points wins; tied leaders
   share the win. Racers can hit each other, as in Race.
3. **Catch-up instead of elimination.** Shared names a leader each tick by course progress: the furthest
   saved checkpoint, then the shortest distance to the next checkpoint or the finish, then entity id.
   A racer who falls, or stays beyond the leash from the leader, respawns at the leader's recent safe
   ground. Shared records that position deterministically; the camera never decides gameplay.
4. **Leader camera.** Local Scramble frames the leader and every racer inside the leash, in the Steady
   camera's style. It is presentation only.
5. **Local Play.** Slop Rally with one player stays the solo time trial with Personal Bests. With
   two to four human seats the course plays as Scramble. CPUs are not offered for courses. Scramble
   writes no records.
6. **Presentation.** Targets break with the breaker's seat colour and a point popup; the HUD shows each
   seat's points and place; results rank points, then finish place.

## Prototype defaults

| Value | Default |
| --- | --- |
| Target / bonus target | 1 / 3 points |
| Finish bonus, 1st to 4th | 5 / 3 / 2 / 1 points |
| Finish window after the first finish | 30 s (the Race value) |
| Leash | 20 m horizontal or 10 m below the leader, held for 45 ticks |
| Leader safe-ground lag | the leader's grounded position 60 ticks earlier |

## Consequences

- Online Scramble needs only Room admission, Master format validation and a protocol bump on top of
  this slice.
- Race and Co-op behaviour and their snapshot bytes for existing formats stay unchanged.
- Rejected: personal targets with a skip penalty (being pulled forward costs points you could not
  defend) and couch Co-op only (no competition on the couch).

## Amendment 2026-10-28

Shortcuts must count even when they bypass checkpoint sensors. A course may embed an ordered
3D racing line with 2–64 points. Course section v8 stores it alongside target hit counts.
Readers retain v5/v6 courses and v7 Mayhem. A course without a line keeps its original bytes.
Shared rejects nonfinite or unbounded points, zero-length segments, points outside blast bounds,
a start more than 3 m from the spawn, or an endpoint outside the finish volume plus 3 m.

Scramble progress is arc length at the racer's projection onto the line. Projection searches
within 12 m of previous progress. It can jump forward to a later segment within 1.25 m when
that segment is closer than the local branch by more than 0.25 square metres. Equal-distance
crossings keep the local branch, which prevents nearby stacked floors from stealing progress.
The leader has the greatest progress, with entity ID breaking ties. Courses without a line
keep checkpoint, distance and entity ID ordering. Checkpoints continue to own ordinary respawns.

Each racer's progress and safe-ground progress history belong to the deterministic World Snapshot.
Catch-up restores the progress belonging to the leader's delayed safe ground. Race and Co-op
snapshot bytes stay unchanged. The line has no effect on solo completion or Personal Best
compatibility, so it is excluded from the solo gameplay fingerprint. Arena hashes change when
the line is baked. Scramble still writes no records.

The four lines are generated from `CourseFinishability.Routes`, using actual verified landing
heights. The generator samples route segments every 0.25 m and erases excursions when the route
returns within 1 m of an earlier point before simplifying to at most 64 points. Return distance
is measured in 3D so climbs above an earlier section remain. Baked-line tests reject turns over
135 degrees and lengths above twice the start-to-finish path through the ordered checkpoints.
Run `CourseRacingLineGenerator` with
`SLOPARENA_GENERATE_RACING_LINES=1` to regenerate `tools/blender/<key>/racing_line.json`.
Each layout includes these points in its shell document. Sanctuary uses a markers-only shell
to retain its original box collision source. Apply and bake through the maintained stage commands.

The mode is named **Slop Rally** (maintainer pick, 2026-10-28). Time Trial, Scramble, Race and
Co-op keep their format names. Code identifiers and wire names remain unchanged.

Local Play course cards and their large preview use each course's captured stage thumbnail.
The shared Courses poster remains the fallback when a course thumbnail is absent.
