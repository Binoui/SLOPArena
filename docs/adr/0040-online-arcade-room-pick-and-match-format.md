# ADR-0040: Stage-first Room preparation and typed formats for online Arcade

**Status:** Accepted — 2026-10-19. Slices 1 (stage-first Room phases, Room entry check)
and 2 (co-op Slopball online, protocol 11) are implemented; their two-client playtests
and slices 3–4 are pending. Decisions 4, 6 and 8 were refined while building slice 2;
see [Revisions](#revisions-during-slice-2) and the
[implementation options](../design/online-arcade-implementation.md).
**Extends:** [ADR-0037](0037-arcade-shared-objects-and-unified-stage-artifact.md) (focused
Shared Arcade objects) and [ADR-0039](0039-full-world-rollback.md) (World Snapshots)

## Context

The maintainer selected online Slopball and Character Courses as the next multiplayer
modes on 2026-10-19, ahead of upgrade rounds ([brief](../design/online-arcade.md)). The
online stack only knows stock fights:

- Master orders a Room Lobby → Character Select → Stage Select. A Character Course
  decides its fighter and a volleyball format decides camps, so fighters cannot be
  chosen before the stage.
- The Match start contract carries an arena, fighters and a stock count; the GameHost
  refuses Arcade arenas and always builds a stock rule.
- Slopball and Course simulation accept exactly one fighter, and World Snapshots throw
  for Arcade objects, so rollback cannot cover them.
- Master never checked a client's version for Rooms; an outdated client met a
  contract change only when its Match descriptor failed.

## Decision

1. **Stage-first preparation.** A Room moves Lobby → Stage Select → Character Select →
   Match Starting. The leader chooses what to play first; Character Select then checks
   each selection against that choice. The choice survives membership changes while it
   still fits the Room; fewer than two members still resets preparation to Lobby. A
   failed launch returns to Character Select.
2. **Typed Room Pick.** Stage Select stores a pick of PvP or Arcade, its arena, format
   and rules. Slice 1 stores only the PvP arena; Arcade formats, camps and colours join
   the Room snapshot with the slices that use them.
3. **Room entry checks the client.** The client sends its protocol when it connects to
   Master's hub. On the VPS, creating or joining a Room requires Master's protocol;
   development accepts a missing, development (0) or matching protocol. Outdated clients
   keep chat and get an actionable message instead of a broken Room flow.
4. **Arcade admission from `roster.json`.** An `arcade` list names each entry's arena,
   formats and, for a course, its fighter. Releases write it to Master, as they write
   the PvP `picker`; a shipping test checks it against the baked arenas.
5. **Match Format in the start contract.** The start request and client descriptor carry
   a Match Format (Stock, Volley Versus, Volley Co-op, Course Race, Course Co-op), its
   rules and a camp per player. The GameHost checks format, arena purpose and course
   fighter, and every client builds the same simulation from it.
6. **Formats are focused Shared components.** `ServerSimulation` takes the format at
   construction and adds its rally, co-op, race or team-course component plus an
   `IMatchRule`, following ADR-0037. Camp starts and race grids are Arcade data outside
   the solo start, so solo record fingerprints stay comparable.
7. **Arcade state is in the World Snapshot.** Ball, rally, serve and course state are
   captured and restored, the restore-equivalence fuzz covers Arcade arenas, and Arcade
   events ride the WorldFrame like hits.
8. **Arcade scenes run online.** The Slopball and course scenes take the rollback
   bridge; the PvP scene's connection, countdown and results become a shared component.

## Revisions during slice 2

- **Decision 8, client hosting:** `PvPMatch` hosts Arcade presentation instead of the
  Arcade scenes going online. The Arcade scenes are built around local attempts (retry,
  records, the pace trial, a solo countdown), while `PvPMatch` already owns the online
  connection, countdown, teammates' renderers and results. The Slopball presentation
  classes take explicit dependencies, so `OnlineSlopball` reuses them in the PvP scene
  without duplicating the court. This was the alternative the options listed; the
  recommendation flipped once the code was read.
- **Decision 6, starts:** co-op Slopball spreads the court's one start sideways in Shared
  (`MatchFormats.StartFor`), the documented fallback. The arena's Arcade section version
  numbers above 4 already mean a course, so Slopball can't gain start data without a
  format migration; Versus can mirror the same starts across the net. Courses still
  author race grids.
- **Decision 4, admission:** Master admits Arcade picks from `Room:AdmittedArcade`, with
  `slopball-coop:slopball` as its default. Writing that list from `roster.json` at release
  changes the VPS release helpers, which the operator reinstalls by hand; it follows as a
  separate step.
- **Decision 7, events:** World Frame format 2 carries the Slopball events of its event
  ticks, and the client predictor runs them through a rollback ledger like hits.

## Consequences

- Slice 1 changed only the Room contract between Master and the client. Slice 2 changes
  the Match start contract, the World Snapshot and the World Frame, so the simulation
  protocol moves to 11: Master, GameHosts and clients ship together, and Room entry
  refuses older builds.
- Later slices that change the Match start contract or World Snapshot bump the protocol.
- Solo and Training keep choosing fighters before stages.
- Rejected alternatives: a Mode step before today's order (courses would still pick
  fighters after stages), a simulation class per mode (duplicates the tick and
  contradicts ADR-0037) and server-only Arcade objects (players would hit a late ball).
