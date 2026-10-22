# Online Character Courses

Approved 2026-10-22: implement Race and Co-op for the existing Character Courses,
following ADR-0040's Shared components, full-world rollback and PvP scene hosting.
This is Online Arcade slice 4. Commit, push and release are separate actions.

## Rules and scope

- Two to four players, all using the selected course's admitted fighter. Players
  choose distinct ring colours and lock in through the existing Room flow.
- Race: personal targets (including bonus hit counts), checkpoints and finish
  times. Rivals can attack each other. First finish wins; the remaining racers
  have 30 seconds to finish. End earlier if everyone finishes or withdraws.
  Same-tick finishes share a place. Unfinished racers show DNF and progress.
- Co-op: shared targets and timer, individual checkpoint visits and fall respawns,
  friendly fire off. All targets plus any one fighter reaching the finish wins.
- Finished and withdrawn racers become untargetable, frozen spectators. A
  disconnect cannot hold the race open. An all-withdrawn run has no winner.
- Reuse existing course data and presentation; derive a compact online grid from
  the solo start without changing Solo fingerprints or writing Solo records.
- Extend the existing Match formats, snapshots, World Frame event history,
  rollback ledgers and result payloads. Ship compatible Master/GameHost/client
  under the next protocol version. No new transport or simulation pipeline.

## Implementation refinement

Race owns one CharacterCourseSimulation per fighter. Co-op applies each accepted
target hit to every fighter's progress together, while retaining individual
checkpoint/respawn state; its collision targets and target events remain shared.
This reuses the tested target, bonus-hit and swept-sensor rules without a second
course execution path. These small states are all captured in World Snapshots.

## Verification

Focused course rules/contact/admission/result tests; snapshot restore equivalence
and predictor convergence through the codecs; Shared and Server suites; Master
Room validation/colour/locked-fighter tests; Unity compile and affected runtime
smoke through the canonical gateway. Human 2–4-player acceptance remains explicit.

## Delivery evidence

- Shared suite: 3,339 passed, 2 existing skips. Focused coverage includes private
  target ownership, shared hits, personal respawns, ties, exact 1,800-tick finish
  timing, withdrawals, snapshot replay, event retraction/deduplication, course
  Results codecs and safe four-player starts on all four existing bakes.
- Server suite: 54 passed, including real two-peer UDP sessions for both formats.
- Master suite: 213 passed, including course fighter locks, colour swaps and
  immutable launch rosters. Shared and GameHost builds pass.
- Canonical Unity: current runtime/editor code compiled without errors. Disposable
  Race and Co-op sessions connected to a local GameHost and headless peer. The
  Co-op peer broke a bonus target through ordinary inputs; both target counts and
  the local target visibility updated. HUD captures were inspected and an overlap
  with fighter cards was corrected.
- Native UI fixtures exercised eight course/format choices, fighter constraints,
  colour controls, Race times/DNF and the Co-op team headline. Current console
  errors were zero. The temporary host was stopped and the original stopped
  Frontend scene restored; the Editor lease was released.
- Documentation and whitespace checks pass. Human multiplayer acceptance and the
  coordinated protocol-15 release are pending; no commit, push or deployment was
  performed. The five-item human checklist is in the ignored `TESTING-UNITY.md`.

Disposable verification sources, captures and logs are retained under the ignored
`tmp/online-courses/` directory.
