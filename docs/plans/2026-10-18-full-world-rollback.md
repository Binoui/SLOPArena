# Full-world rollback — implementation plan

**Status:** Approved — 2026-10-18. Phases 0–4 implemented (uncommitted; protocol 10, Steam playtest pending); phases 5–7 pending.
**Decision record:** [ADR-0039](../adr/0039-full-world-rollback.md) (accepted)
**Evidence:** 2026-10-18 Steam playtest, match `ec79dd8e…`; private captures in
`build/diagnostics/2026-10-18-online-session/`; findings in
[netcode architecture](../systems/netcode-architecture.md#self-hit-reactions-and-netplay-diagnostics--2026-10-18).

## Goal

Online play behaves like Training, with prediction instead of delay:

- Every fighter is drawn in one time frame. No five-tick jumps when an opponent
  starts or stops an attack.
- Hits on you and on opponents show their full freeze and hit reaction, predicted
  when the opponent's inputs were guessed right, corrected within a few ticks when not.
- Attacks, grabs, projectiles and trails come from the same simulation as Training,
  not from side channels.
- A half-second upload hiccup no longer drops your shield or eats your presses.
- Logs say what went wrong: corrections, their size, late inputs, snapshot bandwidth.

## Measured baseline

| Item | Value |
| --- | --- |
| RTT / Steam quality (2026-10-18) | 9–13 ms / 1.0 |
| Client pacing (protocol 9) | ≤ 3 irregular frames per 5 s |
| Input lead | 3 ticks; client draws itself about 3 ticks ahead of the server |
| Late inputs (owner) | 40 in the 3 s after GO; two ≈ 29-tick gaps at 15:11 UTC |
| `ServerSimulation.Tick`, real kits in combat, .NET 8 Debug | 0.011 ms (2 fighters), 0.025 ms (4) |
| Today's downstream | ≈ 0.6 KB per tick (two 237-byte entity packets plus side channels) |
| `CharacterState` | 106 primitive fields, ≈ 272 bytes raw |
| Ability runtime | `ServerAbility` base, `CookedTimelineAbility`, 10 built-in capabilities (≈ 2,300 lines) |
| Client / server runtimes | Unity Mono (Windows player, default backend) / .NET 8 (Linux) |

## Phases

Each phase ends at a gate. Phase 0 can ship on its own with protocol 9; phases 1–4
ship together as protocol 10.

### Phase 0 — GameHost input policy (decision B)

`MatchInstance` only; clients unchanged.

- A missing input repeats the newest known input without press edges for
  `MissingInputHoldTicks` (proposed 20 ticks, about ⅓ s), then neutral. Late
  bundles update that newest known input instead of being discarded.
- Late press rescue: press edges (`Jump`, `ActiveSlot`, `ShieldPressed`,
  `GrabPressed`, `DownPressed`, lock/facing/retarget edges) from inputs at most
  `LatePressRescueTicks` late (proposed 10 ticks, about ⅙ s) merge into the next
  consumed tick, once per source tick, using the client's buffered-edge merge rule.
  Older presses are dropped.
- The relay reports the input actually consumed; gap logs gain the rescued count.
- **Gate:** Server tests for hold length, newest-input update from late bundles,
  rescue inside the window, drop outside it, and no double application when a tick
  repeats across bundles; existing Server tests green.
- **Done 2026-10-18 (uncommitted):** `PeerInputTimeline` (Shared) owns queueing, hold
  and rescue; `MatchInstance` uses it and relays inputs as consumed, before the
  simulation clears presses it acted on (previously a press that started an attack
  was relayed as `ActiveSlot = 0`). `InputState.WithEdgesFrom` is the one press-merge
  rule for client and GameHost. Evidence: `PeerInputTimelineTests` (9), an edge-helper
  consistency test, and `MatchClockTests` end to end over UDP (20 held ticks then
  neutral, a gap event, a press one tick late applied within the window and relayed);
  Server 40/40, Shared green apart from another thread's in-progress pier-course
  tests; Unity recompile clean.

### Phase 1 — World Snapshot capture and restore (Shared)

- `WorldSnapshot` data type and `ServerSimulation.CaptureSnapshot`/`RestoreSnapshot`.
  Contents: tick, match/rule state; per fighter the full `CharacterState`; per active
  ability its runtime; resolver hitboxes (with hit sets), projectiles and pending
  explosions; activation and interaction counters, KO and kill credit, animation
  frames and respawn positions. Content references (definitions, baked data, cooked
  slots) are re-linked by entity and slot, never copied.
- Ability snapshot contract: base fields in `ServerAbility`; overrides in
  `CookedTimelineAbility` and each built-in capability. Restore recreates the
  instance through the activation factory path, then restores fields and re-attaches
  wiring (simulation, resolver, arena, presentation sink).
- Canonical order: order-sensitive loops over fighters and abilities iterate sorted
  entity IDs instead of dictionary insertion order. Expect rare golden diffs where
  simultaneous events changed order; review them before regenerating.
- **Gate:** restore-equivalence fuzz for all four kits, 2 and 4 fighters, many
  seeds: capture at every tick of a long fight, restore into a fresh simulation,
  advance both, require byte-identical snapshots. A reflection guard fails when a
  `ServerAbility` subclass has no round-trip coverage. Full Shared suite green.
- **Done 2026-10-18 (uncommitted):** `WorldSnapshot` with `SnapshotWriter`/`SnapshotReader`
  (strings and hit sets written once and referenced, so hit groups shared by hitboxes and
  their ability stay shared), field-complete `SnapshotCodecs`, `ServerSimulation.CaptureSnapshot`
  and `RestoreSnapshot` (decoded completely before applying; content re-linked by owner and
  cooked slot ordinal; capabilities recreated from their timeline operation). Fighters are
  stored in ascending ID order whatever the registration order, and abilities tick in sorted
  owner order. `WorldSnapshotTests`: six fights (all four kits, 2 and 4 fighters, three real
  arenas) compare a continuous simulation with fresh reverse-registered simulations restored
  every 1–40 ticks, byte for byte every tick, with coverage floors (≥ 100 restores, hits,
  hitstop, hitstun, shields, aiming, grabs, throws, air dodges, KOs) and a capture-has-no-side-
  effect check. Sabotaging a capability counter, animation frames, the armor window, hit-set
  sharing or either canonical order each fails 3–6 of them. `WorldSnapshotContractTests`
  round-trips every field of the codecs, every roster capability and every cooked slot
  runtime, and requires every ability type to be reachable through roster content; it found
  `CharacterState.IsAiming` missing from the first codec. Shared and Server suites green in
  the worktree and in the main checkout. Arcade simulations are not captured (they throw).
- **Measured** (combat-heavy fights, .NET 8 Debug): 2 fighters ≈ 0.9 KB per tick median,
  1.4–2.5 KB p95, 3.5 KB max (DEFLATE ≈ 0.35 KB median, 0.6–0.8 KB p95); 4 fighters ≈ 1.8 KB
  median, ≈ 3 KB p95, 5 KB max (DEFLATE ≈ 0.75 KB median, 1.2 KB p95). Capture and restore
  take about 7 µs each with 2 fighters and 12 µs with 4.

### Phase 2 — Snapshot codec and protocol 10 wire format (Shared)

- Binary `WorldSnapshotCodec`: header (protocol, tick, start tick, match state),
  fighters, abilities, resolver, counters, the InputRelay for every fighter and the
  tick's authoritative presentation events. Strings (bones, presentation IDs) travel
  as Match Content Catalog handles.
- Measure typical and peak sizes for 2 and 4 fighters over seeded fights and record
  them here. Proposed budget: ≤ 64 KB/s per client typical, ≤ 160 KB/s peak; over
  budget triggers Phase 6. Phase 1 measurements put raw 4-fighter snapshots at about
  108 KB/s typical, over budget; stateless per-snapshot DEFLATE brings them to about
  45 KB/s (2 fighters ≈ 21 KB/s) without acknowledgements, so the wire format compresses
  each snapshot independently and Phase 6 is not expected to be needed.
- **Gate:** round-trip byte equality, malformed-input rejection fuzz, recorded sizes.
- **Done 2026-10-18 (uncommitted):** `WorldFramePacket`: a 16-byte header (magic, frame
  format, `SimulationProtocol.Version`, compression flag, match state, tick, body length)
  and a body holding the World Snapshot, every fighter's relayed input in the 22-byte input
  wire format, and the tick's presentation events (validated by the per-event wire rules).
  The body is DEFLATE-compressed when smaller; decoding never inflates beyond 64 KB and
  rejects anything malformed with `InvalidDataException`. Strings stay in the snapshot's
  own string table: with DEFLATE, catalog handles are not worth a per-match table. The
  protocol number moves to 10 at the phase 4 cutover; the frame already carries it.
  `WorldFramePacketTests`: round trip and restore through the wire, uncompressed bodies,
  header rejection, invalid events refused on send, and 6,000 mutated frames (compressed
  and raw) that are rejected or decoded and restored without any other exception. Unity's
  Mono 6.13 round-trips DEFLATE (checked in the Editor).
- **Measured frames** (combat fights, 3,000 ticks): 2 fighters 443 B median, 766 B p95,
  1,248 B max (≈ 25 KB/s typical, 73 KB/s peak); 4 fighters 880 B median, 1,341 B p95,
  2,012 B max (≈ 51 KB/s typical, 117 KB/s peak). Both within budget; Phase 6 is not needed.

### Phase 3 — Client predictor (Shared)

- `NetplayPredictor` replaces `RollbackSimulator`: one `ServerSimulation` with every
  fighter. On a newer snapshot it restores it (the Anchor) and re-simulates to the
  local tick with the player's recorded inputs and, for opponents, their last relayed
  input without press edges. Between snapshots it advances one paced tick at a time.
- Presentation ledger: predicted events keyed by tick, authoritative events from
  snapshots for ticks the client did not predict, deduplication across
  re-simulation, retraction of predicted events the anchor contradicts.
- Stats: anchor age, window depth, corrections (predicted vs anchored state at the
  anchor tick), largest correction, re-simulation time.
- **Gate:** multi-client harness (in-process GameHost simulation plus 2–4 predictors
  through the codec, with delay, jitter, loss and late inputs). Scenarios: idle and
  movement (no corrections), trades, combos on the local player (full freeze and
  reaction shown), attack buffered out of hitstun, grab and throw, projectiles,
  KO and respawn, a half-second input stall, disconnect and reconnect.
- **Done 2026-10-18 (uncommitted):** `NetplayPredictor` (Shared/Rollback): one simulation of
  every fighter; each newer frame becomes the Anchor, restored and re-simulated to the local
  tick with the player's recorded inputs (normalised through the input wire format, as the
  GameHost decodes them) and each opponent's relayed input without presses. When its own
  prediction of the anchor tick matches the snapshot byte for byte and the opponents' assumed
  inputs are unchanged it re-simulates nothing. A frame past the local tick is taken as is
  (a jump); with no frame for `MaxPredictionTicks` (60) it stops advancing. Eliminated and
  disconnected fighters consume no input, as on the GameHost. A refused snapshot leaves the
  prediction untouched. Stats per window: anchors, exact anchors, corrections and
  mispredictions (≥ 1 cm, or a state, damage, KO or reaction change), jumps, starved ticks,
  ticks where the GameHost consumed a different input than the player's, anchor age,
  re-simulated ticks and time, largest correction at the anchor tick and largest visible
  snap (self and opponents), effects and hits started and retracted.
- `RollbackLedger` decides what starts and stops, for presentation events and for hits
  (which drive impact sounds, hit feel and rumble): each key once, predicted items retracted
  when a re-simulation drops them or the GameHost's record of their tick lacks them,
  unpredicted GameHost items started late. The harness showed hits need this as much as
  effects, so the frame format gained them: each `WorldFramePacket` now carries the complete
  event and hit record of its tick and the two before (`WorldFrameHistory` on the GameHost
  side); one or two lost frames lose nothing. Frames grew by 4–11 bytes median.
- **Gate evidence:** `NetplayLab` (GameHost loop with `PeerInputTimeline` per player, frames
  and input bundles through their codecs, per-direction delay, jitter, loss, stalls and cuts)
  and `NetplayPredictorTests` (17), `RollbackLedgerTests` (10), frame tests for the record
  history. Results: the moving player's client confirmed 599 of 599 anchors with an idle
  opponent; in a combo on the local player all 8 hits were predicted three ticks before the
  GameHost simulated them, with every freeze tick (7 per hit) and every hit-reaction tick
  shown; an attack buffered out of hitstun came out on the GameHost's tick; the grabber's
  client predicted grab and throw (victim included) with no correction, and the victim's
  client showed the grab on time; Ki Shot hitboxes matched the GameHost on both clients every
  tick; a KO and respawn appeared on the GameHost's tick on both clients; a FightGuy mirror
  trade showed both hits on both clients; a kick shown as a hit and then shielded on the
  GameHost was retracted one tick later and replaced by the block effect. A half-second
  upload stall: the GameHost held 20 ticks, then 7 neutral, rescued the press 4 ticks late
  and dropped the one 18 ticks late; the client measured 12 mismatched inputs and a 0.28 m
  self correction, and was exact again within 30 ticks. A 130-tick outage: prediction stopped
  after 60 ticks, jumped to the GameHost state on reconnect (5.3 m snap) and was exact again
  within 15 ticks. Random fights (2 and 4 fighters, all kits, delay ≤ 2, jitter ≤ 2, loss
  ≤ 5 %): on every tick whose record arrived, the effects and hits each client stood by were
  exactly the GameHost's; all clients matched the GameHost after a quiet second.
- **Measured** (lab, 10 ms-class links: a 3-tick lead and 1-tick delay give a 3–4 tick
  window): a running opponent who turns snaps up to 1.2 m, a chaser
  starting to run up to 0.7 m (correction easing in phase 4 must hide this). Hit feedback
  started after its tick was first shown: 0 of 8 in the combo, 1–2 of 12 in a clean random
  duel, about 20 of 48 per client in the four-fighter lossy random fight. Costs: a simulation
  tick takes 21–25 µs on slop_pit and 49–82 µs on table_manners (Unity Editor Mono and .NET 8
  Release alike), capture 5–13 µs, restore 5–10 µs; re-simulation costs about 0.1–0.35 ms per
  frame. Shared 3,082 passed (2 skipped), Server 41; Unity loads the new Shared DLL with a
  clean console.
- **For phase 4:** the GameHost frames with `WorldFrameHistory` and must give countdown
  frames a meaningful tick (the simulation tick does not advance during the countdown);
  the bridge must not send input for a tick `Advance` refused and must follow the
  predictor's local tick after a jump; `CombatFeedback` should take hits from the hit ledger
  (retracted hits can cancel hit feel) and the dispatcher needs a stop for retracted long
  effects; opponent snaps need easing.

### Phase 4 — GameHost broadcast and Unity integration

- GameHost serialises one snapshot per tick and sends the same bytes to every client
  (Steam unreliable without Nagle; development UDP). Clock controls, bootstrap and
  reliable match results stay.
- `RollbackSimulationBridge` wraps `NetplayPredictor`. `PvPMatch` presents like
  Training: resolver-driven projectiles and sword trails, simulation events through
  the ledger, correction easing kept.
- Master and all clients move to protocol 10.
- **Gate:** Unity compile and console clean; local two-client match (local GameHost,
  development UDP, Editor plus a development player); the harness scenarios by hand;
  then a Steam playtest against a checklist.
- **Done 2026-10-18 (uncommitted):** the GameHost sends every peer the same
  `WorldFramePacket` each tick (Steam tag `Frame`, unreliable and flushed per tick; a raw
  datagram on development UDP) built from `WorldFrameHistory`, and no longer sends
  per-entity state, presentation-event, projectile-visual or sword-trail packets.
  Countdown frames carry the server tick (the simulation's clock is set, nothing is
  simulated) and the frame that turns Playing is `StartTick − 1`; before the first
  playing tick, connected fighters relay neutral input. Steam message limits cover a full
  frame (GameHost send, client receive) and the GameHost sets Steam's send rate to
  256 KB/s–1 MB/s. Shared gained `NetplaySession`, the transport-free client protocol
  (Clock samples, frame ingestion, paced steps with buffered presses, bundles that restart
  after a timeline jump, stats), and protocol 10. Unity: `RollbackSimulationBridge` wraps
  it; `NetworkClient` decodes frames into a two-second queue; `PvPMatch` reports Ready
  after the first frame, predicts with the admitted stock rule, takes projectile visuals
  (`ProjectileVFXManager.SetPredictedSimulation`, rollback-stable keys, no removal
  bursts) and sword trails from the predicted resolver, stops retracted effects
  (`TimelinePresentationDispatcher.Retract`), feeds started hits to `CombatFeedback`,
  keeps correction easing and logs the new `[Netplay]` fields. Master:
  `SteamMatchDescriptor.Protocol = 10` (uncommitted in its checkout; release together).
- **Gate evidence:** Shared and Server suites green, including `NetplaySessionTests` and
  `FullWorldNetplayTests` (a real `MatchInstance` over UDP with two `NetplaySession`
  peers: Ready, countdown, first predicted tick at or just after `StartTick`, no rejected
  frames, the moving peer's own client never corrected, the other saw it within 1 cm);
  the Server tests that read the old packets now read frames (projectile and sword-trail
  checks restore the opponent's frames). Unity recompile and console clean. Local match:
  development GameServer, the Unity Editor as entity 1 (idle FightGuy) and a scripted
  .NET peer as entity 2 (Manki: chase, jabs, Round Bombs) over development UDP — Waiting,
  Countdown, Playing, three KOs with respawns, Ended with results. The Editor logged no
  rejected or starved frames, no late or mismatched inputs, a 2–4 tick window (12 once
  across a 369 ms Editor hitch), self corrections ≤ 3 cm, opponent corrections ≤ 0.38 m
  and visible opponent snaps ≤ 0.75 m, re-simulation ≤ 2.2 ms per frame, effects and hits
  started with none retracted, and no console errors. Master tests 183 green.
- **Noted for phase 5:** in the Editor no anchor was byte-exact while the opponent
  re-aimed every tick, whereas the .NET peer predicting the idle Editor fighter was exact
  89 % of the time; phase 5 must separate Mono/.NET drift from input changes.
- **Noted for phase 5 (CI, 2026-10-18):** even .NET 8 on two Linux hosts diverges. The
  seeded slop_pit FightGuy–Manki lab fight lands 12 GameHost hits on the maintainer's
  machine (glibc 2.44, zlib-ng) and 7 on GitHub's ubuntu-latest (older glibc and zlib),
  so the C library's float functions alone change fight outcomes. The lab tests now
  check consistency, not an exact fight, and truncation by cutting into the compressed body.
- **Remaining:** the Steam playtest against the checklist (TESTING-UNITY.md).

### Phase 5 — Parity measurement and diagnostics

- Replay a recorded input trace in the Unity player and compare per-tick snapshot
  hashes with .NET 8. Report the first divergence per match. This also serves the
  Replay v1 parity gate ([ADR-0033](../adr/0033-gamehost-input-replays-with-shared-playback.md)).
  Frequent large divergence inside the prediction window triggers a
  deterministic-math follow-up (for example, Shared trigonometry); anchoring keeps it
  a quality issue meanwhile.
- `[Netplay]` and GameHost logs report snapshot bytes per second, serialisation and
  decode time, and the Phase 3 stats.

### Phase 6 — Snapshot compression (only if Phase 2 measurements exceed budget)

Lossless only: restoration needs exact state. Delta against the last snapshot the
client acknowledged (acknowledgement carried in input bundles), field change masks,
periodic full snapshots.

### Phase 7 — Removal and documentation

Completed on 2026-10-23 under the separate [simulation review approval](2026-10-23-simulation-review-fixes.md).
Useful regression assertions were migrated to full-world APIs before deleting the
unused runtime and codecs. This approval replaced the removal gate without claiming
completion of the pending Steam playtest or cross-runtime parity measurement.

Delete LocalTrack, PredictedTrack, RawTrack, `RollbackSimulator`, the
Predictable/Complex partition, per-entity state packets, projectile-visual and
sword-trail packets and the 2026-10-18 self hit-reaction hold. Mark ADR-0010 and
ADR-0011 superseded, accept ADR-0039, update `CONTEXT.md` (Anchor, Prediction
Window, World Snapshot replace ConfirmedTick, RollbackWindow and the tracks), the
netcode architecture, troubleshooting and the ops skill.

## Risks

| Risk | Mitigation |
| --- | --- |
| A runtime field missing from the snapshot | Phase 1 restore-equivalence fuzz and reflection guard; new abilities cannot merge without coverage |
| Mono vs .NET results differ | Anchoring each tick limits drift to the prediction window; Phase 5 measures it |
| Bandwidth | Measured in Phase 2; Phase 6 is ready if needed |
| Effects playing twice, or for a hit that was rolled back | Event keys, presentation ledger with retraction, harness assertions |
| Concurrent Shared work on abilities, hitboxes and the resolver | Start Phase 1 after that work lands; build phases 1–3 on a dedicated branch |
| Long cutover | Phase 0 plus the current self-hit fix and logging ship first under protocol 9 |

## Decisions (approved 2026-10-18)

1. **Snapshot every tick** (recommended) rather than inputs plus periodic snapshots.
   Robust to cross-runtime drift; costs bandwidth.
2. **Opponent prediction:** repeat their last input without presses, with no added
   input delay at first (recommended). Delay is tunable after playtests.
3. **Effects of predicted hits:** play them immediately and retract long ones if the
   hit is rolled back (recommended), or wait for confirmation (about 70 ms later).
4. **Phase 0 numbers:** hold 20 ticks then neutral; rescue presses up to 10 ticks late.
5. **Server-side rewind** for late inputs stays out of this plan (snapshots allow it later).
6. **Ship now:** release Phase 0 with the current self-hit fix and logging under
   protocol 9 while phases 1–4 are built (recommended).

## Rough effort

| Phase | Estimate |
| --- | --- |
| 0 GameHost input policy | ≈ 1 day |
| 1 World Snapshot | 3–4 days |
| 2 Codec and sizes | 1–2 days |
| 3 Predictor and harness | 2–3 days |
| 4 GameHost and Unity integration | 3–5 days |
| 5 Parity and diagnostics | 1–2 days |
| 6 Compression (conditional) | 2–3 days |
| 7 Removal and docs | ≈ 1 day |

Playtests gate phases 0 and 4; the estimates exclude waiting for them.
