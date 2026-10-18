# ADR-0039: Full-world rollback anchored on complete server snapshots

**Status:** Accepted — 2026-10-18 (protocol 10 implemented through plan phase 4; Steam playtest, parity measurement and removal pending; see the [plan](../plans/2026-10-18-full-world-rollback.md))
**Supersedes:** [ADR-0011](0011-rollback-scope-track-model.md) (LocalTrack / PredictedTrack / RawTrack)
**Completes:** [ADR-0010](0010-all-entity-prediction-input-relay.md) (all-entity prediction via InputRelay)

## Context

ADR-0010 chose to predict every fighter and re-simulate from a confirmed base. It
could not be built as specified because snapshots carried only part of the
simulation: active ability runtime, the resolver's hitboxes and projectiles, and the
queued launch of a frozen victim existed only in GameHost memory. ADR-0011 therefore
split prediction into three tracks: the self simulated alone, opponents predicted only
in movement states, and attacks, hits and freezes shown from received snapshots.

The 2026-10-16 and 2026-10-18 Steam playtests both had a clean network (RTT about
10 ms, Steam quality 1.0, steady 60 Hz server). The 10-16 lag came from client pacing,
fixed in protocol 9. On 10-18 players still reported rollbacks, lag and an overly fast
feel; apart from two upload hiccups, those defects trace back to the track split:

- The self track cannot predict being hit (opponent mirrors have no hitboxes) and
  cannot rewind through attacks, Hitstop or Hitstun. Online, the local player never
  saw their own hit freeze (0 of 23 hits, against 30 of 30 offline).
- Opponents are drawn about four ticks ahead while moving and about one tick behind
  the server while attacking or hit, so they jump about five ticks at every switch.
- Hits between the self and an opponent are judged against copies in different time
  frames, so predicted hits disagree with the server and correct late.
- Remote presentation needs side channels (projectile visuals, sword trails, attack
  timers on the wire) that Training gets from its own simulation.

The client and GameHost run different runtimes (Unity Mono on Windows, .NET 8 on
Linux), and the simulation uses `MathF` trigonometry and dictionary iteration whose
results are not guaranteed identical across them. Pure input-only rollback would
drift, so authoritative state must keep anchoring the client.

## Decision

Make the complete simulation state a first-class value, the **World Snapshot**, and
predict the whole world from the newest one.

1. **World Snapshot.** Shared can capture and restore the full `ServerSimulation`
   state at a tick: every fighter's complete `CharacterState`, each active ability's
   runtime (cooked timeline cursor and capability state, through an explicit
   per-ability snapshot contract), the resolver's hitboxes, projectiles and pending
   explosions, simulation counters (activation and interaction IDs, kill credit,
   animation frames) and match-rule state. Restoring a snapshot into a fresh
   simulation and continuing with the same inputs reproduces the same snapshots on
   the same runtime. Iteration order is canonical (sorted entity IDs), not insertion
   history.
2. **Broadcast.** Every tick the GameHost sends each client the World Snapshot of the
   tick it just simulated, the inputs it consumed for every fighter (InputRelay) and
   the complete record of presentation events and hits for that tick and the two
   before it, so one or two lost frames lose neither. Delivery is unreliable; a newer
   snapshot supersedes a lost one.
3. **Client prediction.** The client runs one `ServerSimulation` containing every
   fighter, with full content and baked data. When a newer snapshot arrives it
   restores it (the **Anchor**) and re-simulates up to its local tick: its own recorded
   inputs for itself and, for opponents, their last relayed input with one-tick press
   edges removed (the rule the GameHost uses for missing input). The client never keeps
   a divergent history of its own; any cross-runtime drift is confined to the few
   ticks of the **Prediction Window**.
4. **One time frame.** All fighters render from the predicted world at the local
   tick. Hits, freezes, hit reactions, grabs, projectiles and trails come from the same
   simulation as Training. Corrections ease positions as today; presentation events
   and hits (which drive impact sounds and hit feel) are keyed and deduplicated across
   re-simulation, predicted ones that the anchor later contradicts are retracted, and
   the GameHost's records cover what the client did not predict.
5. **Missing and late input on the GameHost.** A missing input repeats the newest
   known input without press edges for a longer, configurable window before falling
   back to neutral. Press edges that arrive late are applied on the next unconsumed
   tick within a short window instead of being discarded. The relay reports exactly
   what was consumed.
6. **Pacing stays.** `NetplayClock` paced prediction and the adaptive input lead
   (protocol 9) are unchanged.

This is a coordinated protocol cutover (client, GameHost and Master move to protocol
10 together). LocalTrack, PredictedTrack, RawTrack, the Predictable/Complex
partition and the per-entity state, projectile-visual and sword-trail packets are
removed once the new path is accepted.

## Consequences

- Every ability runtime, built-in capability and resolver feature must implement the
  snapshot contract. A completeness test restores at every tick of seeded fights for
  each roster kit and fails on any divergence, so a missed field is caught in CI, not
  online.
- Downstream bandwidth rises from about 0.6 KB per tick to a measured 0.9 KB median
  (3.5 KB max) for two fighters and 1.8 KB (5 KB max) for four before compression.
  Each complete frame, compressed independently, measures 447 bytes median (1.3 KB
  max) for two fighters and 891 bytes (2.1 KB max) for four: about 26 and 52 KB/s.
- Client CPU rises to a few re-simulated ticks per update. Measured with real kits in
  combat, a tick costs 21–25 µs on slop_pit and 49–82 µs on the denser table_manners
  (Unity Editor Mono and .NET 8 Release alike); capture and restore cost 5–13 µs. A
  3–4 tick Prediction Window costs about 0.1–0.35 ms per frame.
- Opponent mispredictions still pop, as in any rollback design, but within one
  consistent time frame and with complete freezes and reactions.
- World Snapshots double as complete checkpoints: Replay seeking, mid-Match spectator
  admission and Training save states become possible later
  ([ADR-0033](0033-gamehost-input-replays-with-shared-playback.md) deferred them).
- Cross-runtime parity is measured, not assumed; anchoring makes divergence a
  quality issue, not a correctness one.

## Considered alternatives

- **Keep the track split and smooth the switches.** Cosmetic; hits on the self and
  cross-time-frame hit checks stay wrong. Rejected as a middle ground.
- **Input-only rollback with checksums (GGPO style), full state only on desync.**
  Least bandwidth, but correct only with bit-identical results across Unity Mono and
  .NET 8, which is unproven and unlikely with `MathF` trigonometry. Every desync would
  cost a resynchronisation round trip. Still possible later as a bandwidth
  optimisation once parity is measured.
- **Server-side rewind for late inputs.** Honours late input at its original tick at
  the cost of corrections for everyone else. Snapshots make it possible later; not in
  this decision.
