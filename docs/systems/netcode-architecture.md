# SLOPArena Netcode & Simulation Architecture

The GameServer owns match time and results. Online clients use
`RollbackSimulationBridge` and a full-world `NetplaySession`; Training uses
`LocalSimulationBridge`. Both run the same admitted Shared content.

Current wire versions live in [`SimulationProtocol.Version`](../../src/Shared/SimulationProtocol.cs),
[`WorldFramePacket.FormatVersion`](../../src/Shared/WorldFramePacket.cs), and
[`ServerSimulation.SnapshotFormatVersion`](../../src/Shared/ServerSimulation.Snapshot.cs).
Client, GameServer, and Master admission must agree on the protocol. Bump it once per
deploy, not per change: simulation work lists itself under `Unreleased` in
`SimulationProtocol.cs`, and `release.py prepare` refuses a release whose `src/Shared`
changed since the previous release tag while the protocol did not move. The operating
contract starts at [Philosophy](#1-philosophy); the records below preserve protocol cutovers.

Match admission pins both the character packages and the exact baked arena bytes.
The character-only catalog hash remains compatible with schema 1; the schema-2
match hash also covers the arena name and digest. Clients verify their local arena
before connecting, predicting or sending Ready on Steam and development UDP.

The predictor validates a complete frame before publishing metadata or ledger
records. An accepted Ended frame restores the exact final snapshot, retracts future
speculation and stops prediction. Late-input rescue tracks handled source ticks
individually, so a newer neutral bundle cannot suppress an unseen older press
within the rescue window.

World Snapshot format 6 preserves the resolver's emission counter and each hitbox's
`EmissionId`. Unity projectile visuals combine it with owner and move identity, so removing an earlier
projectile does not reassign surviving visuals or trails. Restoring the counter
reproduces future emission identities during resimulation. Snapshot decoding rejects
zero, duplicate, or out-of-range live emission IDs before applying the world.

## Universal ground movement cutover

Simulation protocol 23 applies the fixed Progressive Run rule in every mode.
Start is 60% of RunSpeed, followed by 12 linear increments (protocol 29; protocol 23 shipped 40% and 20). Turnaround retains 100% of current speed.
Release brakes the velocity vector by RunSpeed/20 each tick. Dash overspeed keeps its prior coast behavior.
No packet, World Frame, or World Snapshot layout changes. The former Rush ushort remains reserved and inert.
The existing source/cooked movement fields remain for content compatibility, without per-character ramp overrides.
Saved input routes and replay playback execute the current Shared rule. Historical routes may need new run-ups or jump timing.
Slopball record revision 11 and Course record revision 5 produce new comparison keys. Older personal bests remain stored under their original keys.
Client stick snapping is an ordinary local Controls preference and is not negotiated with the server.
Clients and GameServers must update together. Master admission must learn protocol 23 at release in its separate repository.

## Finger Guns cylinder cutover

Simulation protocol 19 and World Snapshot format 9 add
`CharacterState.FingerGunsShotsSpent`. The cylinder survives activations,
interrupted reloads and rollback; only a completed reload or respawn refills it.
Its capability runtime also snapshots startup/reload progress and committed aim.
Clients, GameServers and Master admission must update together; protocol 18 peers
and older snapshots are rejected. Packages using Finger Guns require runtime API
1.11.0. Projectile presentation continues through the existing spawn path.

## Homing Bazooka cutover

Simulation protocol 18 and World Snapshot format 8 serialize each projectile's
captured homing target, cooked turn cosine and sine, and optional owner-damage
override. The explosion payload retains the same override after the ability ends.
Clients and GameServers must update together, with Master admission configured
for protocol 18. Older snapshot layouts and protocol 17 peers are rejected.

## Historical protocol records

These records preserve the behavior and verification reported at each cutover,
newest first. The newest record may describe the current protocol; compare its
versions with the source constants above. Read older packet layouts, release
instructions, and three-track behavior in their cutover context rather than as
current operating guidance.

### Online Character Courses — protocol 15

Race and Co-op reuse the same full-world prediction as Slopball (ADR-0040).
World Snapshot 5 captures every course's phase, elapsed ticks, target hit counts,
checkpoint, finish place and withdrawal state, plus the Race's 30-second finish
window. World Frame 4 carries course events with stable tick/player/kind/target
identities and a per-tick sequence; a rollback ledger starts and retracts them.
Presentation reads the restored target flags and bonus hit counts, so a corrected
break restores the target. Match Results carry finish times and DNF progress;
colours travel with the immutable roster. Master, GameHost and clients ship
protocol 15 together; older peers are rejected.

### Sir Bonk's lid and joust — protocol 12

Protocol 12 carries the engine work for Sir Bonk's pot lid and joust
([plan](../plans/2026-10-21-sir-bonk-pot-lid-and-joust.md)). World Snapshot format 3 adds
a hitbox's return settings, the leg it is on and its ticks on that leg, the steered
charge's state, and `CharacterState.ReturningProjectileSlot` (the slot waiting for its
lid, read by the CPU and the HUD). Presentation
events gain `ProjectileCatch` (6) and `ProjectileSelfHit` (7). Release client, GameHost
and Master together (Master `LobbyModels.Protocol = 12`); protocol 11 peers are rejected.
No World Frame or input layout changed.

### Full-world rollback — protocol 11

Online play predicts the whole world from complete GameHost snapshots
([ADR-0039](../adr/0039-full-world-rollback.md), [plan](../plans/2026-10-18-full-world-rollback.md)
phases 0–4). Release client, GameHost and Master together; older peers are rejected.
Protocol 11 ([ADR-0040](../adr/0040-online-arcade-room-pick-and-match-format.md)) adds
Match formats: World Snapshot format 2 carries the Arcade ball and Slopball rules, and
World Frame format 2 carries the Slopball events of its event ticks, so co-op Slopball
predicts and presents like a stock fight. Character Courses join this path in protocol 15 (above).

- **World Frames.** Each tick the GameHost sends every client the same `WorldFramePacket`
  (Steam tag `Frame`, unreliable; a raw datagram on development UDP) in place of
  per-entity state, presentation-event, projectile-visual and sword-trail packets: the
  World Snapshot after the tick, the input each fighter consumed (connected fighters
  relay neutral input before the first playing tick) and the complete event and hit
  record of the tick and the two before it. Bodies are DEFLATE-compressed when smaller:
  about 450 bytes median with two fighters and 900 with four (26–52 KB/s). Countdown
  frames carry the server tick, and the frame that turns Playing is the tick before
  `StartTick`. The GameHost sets Steam's send rate to 256 KB/s–1 MB/s; both ends accept
  Steam messages up to a full frame.
- **Prediction.** `RollbackSimulationBridge` wraps the transport-free `NetplaySession`.
  Its `NetplayPredictor` (one `ServerSimulation` with every fighter) restores the newest
  frame, the Anchor, and re-simulates the Prediction Window with the player's recorded
  inputs and each opponent's relayed input without presses; when its earlier prediction
  of the anchor tick matches byte for byte, nothing is re-simulated. Prediction starts at
  the frame that turns Playing, waits after 60 ticks without a frame, and jumps to the
  GameHost when a frame is past the local tick (older inputs then leave the bundle).
  NetplayClock pacing, the adaptive input lead and input bundles are unchanged.
- **Presentation.** Every fighter renders from the predicted world, as in Training:
  projectile visuals and sword trails come from the predicted resolver; presentation
  events and hits come from rollback ledgers, each once, with predicted ones the GameHost
  rules out stopped and unpredicted ones started when their record arrives. Position
  corrections keep their easing.
- **Diagnostics.** `[Netplay]` every 5 s: pacing (`updates`, `skipped`, `extraSteps`,
  `lead`, `ahead`), frames (`frames`, `stale`, `rejected`), anchors (`anchors`, `exact`,
  `corrections`, `mispredicts` ≥ 1 cm or a state, damage, KO or reaction change),
  largest correction at the anchor tick (`selfCorr`, `oppCorr`) and visible jump
  (`selfSnap`, `oppSnap`), `window` (ticks predicted ahead, max anchor age), `resim`
  ticks and `resimMs`, `jumps`, `starved`, `effects`/`hits` started and retracted,
  `selfInputMismatch` (anchors where the GameHost consumed a different input than
  yours), then `serverLate`, `minSlack`, `ping` and frame times. `[Netplay] serverLate`
  and the GameHost's input lines are unchanged.
- **Superseded.** This cutover retired LocalTrack, PredictedTrack, raw playout and
  the self hit-reaction hold from online use. Their classes and per-entity packet
  types were retained at the cutover, then removed with migrated full-world coverage
  under the owner's 2026-10-23 [retirement approval](../plans/2026-10-23-simulation-review-fixes.md).

### Self hit reactions and netplay diagnostics — 2026-10-18

> Superseded online by protocol 10 above; kept as the record of the protocol 9 fix.

The 2026-10-18 Steam playtest again had a clean network (RTT 9–13 ms, Steam quality
1.0, server at 60 Hz) and smooth pacing, but players still saw rollbacks and a game
that felt fast. When the local player was hit online, the freeze never showed (0 of
23 hits, against 30 of 30 offline): `LocalTrack` skipped every self snapshot with
`HitstopTicks > 0` and any correction across an attack, so the player kept moving
about 12 ticks and then slid into a hit reaction that was already partly over.

- **Hold.** A self snapshot with a freeze and new damage is a hit the self track
  cannot predict (opponent mirrors have no hitboxes) or rebuild (the queued launch is
  not on the wire). The track cancels the interrupted local attack and pauses on the
  server's frozen snapshot.
- **Re-seed.** The first snapshot after the freeze carries the applied launch. The
  track re-seeds from it, restores the Combo Influence from the player's own inputs
  consumed during the freeze, and replays its recorded inputs to the local tick. The
  local simulation then runs the hitstun itself, so an attack buffered at the end of
  hitstun starts on time. A launch without a freeze re-seeds directly.
- **Presentation.** Until the server reports the self out of the reaction,
  `RollbackSimulator.GetState(self)` plays the self's snapshots on the raw playout
  clock, in the attacker's time frame: the full freeze and the whole hit reaction
  render at their authored length. Leaving the reaction returns to the local
  timeline, a few ticks ahead, with the usual correction easing.
- **Diagnostics.** `[Netplay]` now logs a UTC time, `reconciles` (snapshots applied),
  `mispredicts`/`maxSnap` (corrections that moved the self ≥ 1 cm or changed its
  state), `hitsTaken`/`hitLate` (server hits presented and how many ticks the local
  timeline had run past them), `serverLate`/`minSlack` (the GameServer's arrival
  report) and `maxFrameMs`. Each late-input report also logs `[Netplay] serverLate`
  with Steam's view of the connection (send queue, pending bytes, rates). The GameHost
  records `Input gap entity N` when a peer's input resumes after three or more missing
  ticks: start tick, held/neutral ticks, and whether delayed bundles arrived (worst
  slack) or none did.

- **Missing and late input (GameHost).** `PeerInputTimeline` owns each peer's input
  stream. A missing tick repeats the newest known input without press edges for up to
  20 ticks after that input's tick, then neutral; late datagrams refresh that newest
  input instead of being discarded. Press edges that arrive at most 10 ticks late merge
  into the next consumed tick, once per source tick (`InputState.WithEdgesFrom`, the
  rule the client already used for buffered presses). The relay now carries inputs as
  consumed, before the simulation clears presses it acted on. `Inputs entity N` lines
  add `rescued` and `dropped` (late presses applied or too old), and gap events note
  late presses applied.

No wire field changed in this protocol 9 fix. The GameHost input policy carries over to
protocol 10; the self-track hold and the switch between predicted and raw time frames
were replaced by full-world rollback (section above).

### Smooth online play — protocol 9

The 2026-10-16 Steam playtest had a clean network (about 10 ms RTT, no loss, healthy
GameHost ticks) yet felt laggy and showed opponent animations missing. The causes were
client pacing and presentation, so protocol 9 changes how time advances and what
snapshots carry. Release client, GameHost and Master together; protocol 8 peers are rejected.

- **Paced prediction.** `NetplayClock.StepsForUpdate` advances local prediction one tick
  per fixed update. A `TickPacer` absorbs ±1 tick of frontier jitter and corrects
  persistent drift one extra or skipped step at a time; it catches up immediately only
  when four or more ticks behind and runs at half speed when far ahead. Previously each
  update jumped to a wall-clock target, producing many 0/2-tick updates per second on
  any refresh rate other than an exact 60 Hz.
- **Clock estimate.** The server/local offset is the best Clock sample in a two-second
  window, so a delayed sample or stale latency reading expires instead of accumulating.
- **Adaptive lead.** Clock controls report, per peer, the minimum input slack (ticks
  between arrival and consumption; 1 means just in time) and late input datagrams since
  the previous Clock. A late report raises the lead at once (rate-limited for 0.5 s);
  four seconds of slack of three or more lower it. The lead stays between 2 and 12 ticks.
- **Redundant inputs.** Each uplink datagram is an `InputBundle` carrying the newest
  simulated tick and up to five preceding ticks, including catch-up ticks. A lost
  datagram no longer drops a press, and the GameServer consumes exactly what the client
  predicted. Duplicate ticks replace each other in the server's tick buffer.
- **Opponent prediction.** `PredictedTrack` reseeds every tracked opponent from its own
  confirmed snapshot (an opponent missing from a batch is not extrapolated twice), holds
  relayed inputs through `InputState.WithoutEdges` (the same rule the GameServer and the
  bridge use, so no relayed press is replayed), and advances one tick per local tick
  between batches.
- **Raw playout.** Complex-state opponents play back received snapshots on a clock that
  advances one tick per local tick, an adaptive 1–4 ticks behind the newest server tick,
  so bunched arrival no longer stalls or skips them. Logic (the self mirror, interaction
  barriers) still uses the newest snapshot; coupled interactions bypass the delay.
- **Event identity.** `LocalTrack` replays rewind the simulation tick, so the local
  player's predicted presentation events keep the server's tick and deduplicate against
  the authoritative copy instead of playing twice.
- **Remote presentation fields.** Snapshots now carry the attack elapsed ticks, held
  charge and knockback velocity. Remote trails, accents, swing audio, charge visuals,
  launch effects and post-hitstun flight prediction use them. Ability timers serialize
  as zero outside Attacking/Aiming, and `ApplyTo` leaves the self track's live ability
  timers alone.
- **Transport.** Clients send inputs and controls without Nagle delay and read Steam
  messages at the start of each fixed update, stamping Clock samples with Steam's arrival
  time. The GameHost wakes its Steam loop when a match finishes a tick and flushes each
  connection after the drain instead of polling every 8 ms behind a 5 ms Nagle timer.
- **Presentation.** PvP and Training renderers interpolate between fixed steps, and
  authoritative corrections or prediction-track switches ease out over a few steps
  instead of snapping. Teleports and respawns still snap.
- **Robustness.** A match-loop exception now cancels only that match with the
  `match_failed` reason instead of terminating the process. Oversized projectile
  snapshots are capped, and unencodable presentation events are dropped with a
  diagnostic. A stale Ready across the countdown/GO edge is ignored rather than treated
  as spoofing. Clients time out admission locally instead of comparing the descriptor
  deadline with a possibly skewed PC clock.
- **Diagnostics.** Every five seconds the GameHost logs `Inputs entity N: bundles late
  early held neutral minSlack` per peer during play. The client logs `[Netplay]` with
  skipped/extra-step updates, lead, ticks ahead, corrections, raw playout delay and
  starvation, ping and frame time (extended on 2026-10-18; see above).

### Hit reaction snapshots — protocol 7

The Steam PvP playtest exposed repeated opponent hit-reaction restarts: raw network
states omitted HitstunTicks, so presentation received zero and treated unchanged
snapshots as new hits. Protocol 7 appends the authoritative HitstunTicks and
HitReactionSequence. Both launch paths advance the sequence once when a new
hit reaction begins; hitstop and ordinary countdown leave it unchanged.
PlayerRenderer uses the production Shared presentation policy to restart on
entry or a changed reaction identity, including shorter re-hits. Repeated/skipped
snapshots keep the same animation and the replicated timer enables position smoothing.
Release client, GameHost and Master together; protocol 6 peers are rejected.

### Startup-correction protocol cutover verification — 2026-10-04

- Shared authoritative snapshots now carry the captured correction target, initial yaw, committed pose pitch, active/owned flags and captured target deaths; protocol 6 retains this state layout and packet sizing.
- This verification recorded the protocol 5 Master cutover. It was superseded by the coordinated protocol 6 clock cutover described below; neither cutover supports mixed-version clients.
- Protocol 6 focused clock/rollback checks passed; full Shared reported 1,231 passed,
  2 skipped, and 5 failures (four Manki normal target-lock cases and one Manki airkick
  scenario). Full GameServer tests passed 27/27 and MasterServer tests passed 158/158.
  Unity compilation, a rendered two-client match, packaged join/rematch, live Steam
  play and human-feel acceptance remain unverified; no release rollout is claimed.
  An isolated real-bridge smoke also passed bounded stall catch-up, buffered lock-edge
  consumption, reconnect-baseline reset, and stopping gameplay for outside-history
  Ended/result-only delivery. Its injected transport queues do not prove native networking
  or Unity presentation.


### Authoritative PvP clock — protocol 6

Protocol 6 coordinates the client, GameServer, and MasterServer; protocol 5 clients are
incompatible. Both Steam and development UDP use the same Bootstrap/Ready/Clock control
packet. Steam wraps it in the reliable control frame; UDP sends the packet as a control
datagram and repeats bootstrap/Ready idempotently until the readiness barrier completes.

The client requests bootstrap after joining, records the initial authoritative state for
every roster entity, then sends Ready. The server starts the existing five-second
countdown only after every currently bound connection is Ready. Loading time does not
advance or establish gameplay time. Clock controls carry the server tick and gameplay
start tick; the client freezes prediction/input until authoritative Playing and that
start tick, then seeds its timeline from the server clock.

Transport selection and network pumping remain independent of the gameplay clock gate.
Clock resets must not disable the selected transport: clients still need to connect,
receive the roster baseline, and acknowledge readiness while prediction is frozen.

During play, one `NetplayClock` estimate drives prediction, input target ticks, and
reconciliation. On receipt it compensates for estimated one-way packet transit using
half the measured RTT; the scheduling lead adds half-RTT ticks plus two safety ticks,
bounded to 2–12. Clock samples that regress in server tick, start tick, or monotonic
receive time are ignored. Future self snapshots are retained until the local timeline
reaches them; stale packets cannot rewind that timeline.

The server clock advances at 60 Hz through countdown and play. During play, it consumes
input only at its exact target tick, accepts future targets no more than 30 ticks ahead, and ignores
expired targets. Missing input holds the last state for at most six ticks with jump,
facing/lock, defense/grab/retarget and slot press edges stripped, then becomes neutral
(replaced on 2026-10-18 by a 20-tick hold with late-press rescue; see the top section).
Movement, jump hold and shield hold are retained. The client sends input for the tick being
stepped, never an expired preincrement tick. Catch-up is bounded to four steps per client
update; buffered one-shot edges are consumed once, while earlier catch-up steps retain
held controls without replaying presses. Match End/result handling is consumed independently
of whether the packet's simulation tick remains in local rollback history. A replacement
connection must receive the full roster baseline and Ready again.

## 1. Philosophy

**Server-authoritative Shared simulation with client prediction and reconciliation.**
The GameServer is authoritative. Online, `NetplaySession` runs one `ServerSimulation`
with every fighter so the client can predict and reconcile without owning gameplay
results. Unity presents the predicted world and semantic events.

---

## 2. Components

```text
Unity ServerBrowser -- SignalR GetRooms --> Master Room directory
Unity Room/selection/chat <-- SignalR --> Master RoomManager + ChatService
                                           |
                  RoomStartMatch -> select compatible GameHost
                                           |
                                    POST /match/start
                                           v
                             GameHost MultiMatchOrchestrator
                             + concurrent MatchInstances
                                           |
                       authoritative Match state/results
                                           v
                 Unity RollbackSimulationBridge + Results UI

GameHost -- authenticated result/cancel --> Master Match record
                                            + matching Room -> Lobby
Unity Results -- GetMyRoom --> original Room, or browser if membership ended
```

The Room ID identifies persistent membership and Server Chat, never the
physical GameHost or a player's game identity. Master pins the roster,
Character selections, Arena and content for each Match; the GameHost runs
the authoritative 60 Hz Shared simulation and reports its outcome. Results
can remain visible after the Room resets for a rematch. The ordinary
browser lists public Rooms in every phase with joinability and member
counts; explicit development host/address lookup is not that directory.

### Launch session and chat

[`ChatSession`](../../client/Unity/Assets/Scripts/Runtime/Network/ChatSession.cs) owns
guest authentication, credential renewal, and a single reusable
[`LobbyClient`](../../client/Unity/Assets/Scripts/Runtime/Network/LobbyClient.cs) for
the application launch. Only the chosen display name is saved locally. A new launch
gets a new guest identity; reconnect and token renewal preserve the current identity
and session tag. Configure a development Master endpoint before authentication.
Scenes consume this session rather than authenticating or creating hub connections.

- **Global:** shared across menus, Training, PvP, and Results.
- **Direct:** addressed by player ID, not display name. Incoming messages mark an unread
  conversation without opening it or entering the public gameplay feed.
- **Server:** follows the authenticated player's current RoomManager attachment, not
  physical GameServer or waiting-roster membership. The Room ID is the conversation
  identity; guessing it grants no access. Distinct Rooms remain isolated even when they
  share a physical GameHost. Create/join and reconnect deliver that Room's authorized
  history. Leave, revocation, and membership expiry clear the prior Room conversation;
  queued pushes for an old Room are rejected.

Physical server admission and `JOIN BY ADDRESS` remain gameplay paths only; neither
grants Server Chat membership.

The client retains at most 32 conversations with 50 messages each. Drafts and local
mutes are launch-scoped. Offline or rejected sends retain the draft and never queue an
automatic resend; uncertain transport outcomes are reported as uncertain. Names and
messages are literal text, limited to 24 and 500 Unicode scalars respectively.

[`ChatOverlay`](../../client/Unity/Assets/Scripts/Runtime/UI/ChatOverlay.cs) attaches to
the scene's existing `UIDocument`. During gameplay its compact feed shows at most
three recent Global/current-Room Server lines, expires them after eight seconds, and never
shows Direct messages. Enter opens or submits the composer; Escape closes it while
preserving the draft. `ChatInputGate` suppresses human movement, attacks, camera, and
conflicting UI shortcuts while composing. Held controls must be released before they
can resume gameplay. Chat does not pause simulation or grant invulnerability.

---

## 3. Data Flow

### 3a. PvP flow

`PvPMatch` starts from the admitted match catalog and owns the
[`RollbackSimulationBridge`](../../client/Unity/Assets/Scripts/Runtime/Simulation/RollbackSimulationBridge.cs).
Its fixed update:

1. Records displayed positions, then `PumpNetwork`: Clock controls feed the
   NetplayClock, World Frames feed the predictor (restore and re-simulation), and the
   match result is kept.
2. Hands the position change the re-simulation caused to correction easing.
3. Reports Ready once a frame has arrived, builds `InputState` from the Unity input
   adapter and the canonical active slot, and calls `Tick`: paced prediction steps,
   the input bundle, and the ledgers' started and retracted events and hits.
4. Applies predicted states to `PlayerRenderer`, updates projectile visuals and sword
   trails from the predicted resolver, stops retracted effects, plays new ones, and
   passes new hits to `CombatFeedback`.

Unity's project Fixed Timestep is **1/60 second**, matching the authoritative tick
cadence. This sets the rate of the client fixed-update loop; it does not replace
reconciliation or change its existing history-window policy.

See [`PvPMatch.cs`](../../client/Unity/Assets/Scripts/Runtime/World/PvPMatch.cs),
[`RollbackSimulationBridge.cs`](../../client/Unity/Assets/Scripts/Runtime/Simulation/RollbackSimulationBridge.cs),
[`NetplaySession.cs`](../../src/Shared/Rollback/NetplaySession.cs) and
[`NetplayPredictor.cs`](../../src/Shared/Rollback/NetplayPredictor.cs).

### 3b. Training flow

Training uses `LocalSimulationBridge` with the same Shared simulation authority and
cooked/admitted content boundary. It builds local input, advances the local simulation,
and exposes state and presentation events to the renderers without a network transport.
Training does not use `NetworkSimulationBridge`.

### 3c. MatchInstance tick loop

[`MatchInstance`](../../src/Server/MatchInstance.cs) paces each match at 60 Hz on its
own thread. Client tick numbers select queued input; they never advance the server clock.

1. Receive and validate inputs and controls. Each player's `PeerInputTimeline` queues
   future inputs by tick. Steam reception uses the same timeline as development UDP.
2. Apply connection and admission rules. UDP silence beyond `TimeoutSeconds` marks
   only that peer disconnected, clears its input timeline, and leaves the match running.
   Steam connection changes and the separate absent-opponent grace period have their own handling.
3. While Waiting, send the baseline without simulating. Countdown advances the server
   tick without gameplay. Its final frame is Playing at `StartTick - 1`.
4. During Playing, consume each active connected fighter's input at exactly
   `_serverTick + 1`. Omit input for inactive or disconnected fighters. A disconnected
   course player is withdrawn from the course.
5. Copy the consumed inputs for InputRelay, advance one tick, and call
   `ServerSimulation.Tick`. Shared owns cooked timelines, collision, hit responses,
   blast deaths, and respawn.
6. Record the tick's presentation events, hits, Slopball events, and course events.
   Evaluate the configured match rule, then send the same World Frame to every connected
   peer, plus controls and results as applicable.

[`PeerInputTimeline`](../../src/Shared/Rollback/PeerInputTimeline.cs) repeats missing
input through `InputState.WithoutEdges` within its hold window, then supplies neutral
input. Late inputs refresh held controls and can rescue press edges once per source
tick within the rescue window. Use its `DefaultHoldTicks` and `DefaultRescueTicks`
constants, and the horizon passed by `MatchInstance`, when inspecting timing policy.

---

## 4. Packet Protocol

### 4a. Client → Server

[`InputBundle`](../../src/Shared/InputBundle.cs) carries an entity ID, the newest tick,
and consecutive inputs ordered oldest first. `HeaderSize`, `MaxInputs`, `SizeFor`,
and `InputState.Size` define the envelope. Steam prefixes it with the `Input` kind byte.

Every simulated tick's input is sent, including catch-up ticks, and repeated in the next
datagrams. This redundancy protects presses against isolated packet loss. The
InputRelay reports what the GameServer actually consumed, including held or rescued input.

**InputState fields:** Offsets and encodings are owned by
[`InputState.Write` and `InputState.Read`](../../src/Shared/InputState.cs).

| Offset | Type    | Field           | Notes                              |
|--------|---------|-----------------|------------------------------------|
| 0-3    | float   | MoveX           | Horizontal analog input            |
| 4-7    | float   | MoveY           | Vertical analog input              |
| 8      | byte    | flags           | bit0:Up, 1:Down, 2:Left, 3:Right, 4:Jump, 5:legacy Dash (reserved/inert; not an air-dodge edge), 6:retired Burst, 7:IsAiming |
| 9      | byte    | ActiveSlot      | `0=none`; Shared `AbilitySlots` selects the canonical ground or air move. Physical bindings belong to the input adapter. |
| 10-11  | short   | FacingYaw       | Degrees × 100 (movement-facing)    |
| 12-13  | short   | AimYaw          | Degrees × 100 (combat-facing, reserved) |
| 14-15  | short   | AimPitch        | Degrees × 100 (camera vertical aim) |
| 16-17  | ushort  | AimDistance     | cm (0-6500 = 0-65m)                |
| 18     | byte    | TargetEntityId  | Client-selected target (0 = none)  |
| 19     | byte    | flags2          | bit0: JumpHeld, bit1: FaceToCamera, bit2: ToggleLock, bit3: DownPressed, bit4: ShieldHeld, bit5: ShieldPressed, bit6: GrabPressed, bit7: RetargetPressed |
| 20     | byte    | LockMode        | `0=Never`, `1=Always`, `2=OnHit` |
| 21     | byte    | protocolVersion | `SimulationProtocol.Version` |

`ShieldHeld` is the per-tick physical hold used for grounded shield. `ShieldPressed` is a
fresh logical edge and the sole airborne dodge input; the simulation accepts it only while
airborne. `GrabPressed` is also a one-tick edge. A completed controller grab chord consumes
the competing defense edge without clearing the physical hold.

Validate the exact envelope, input count and supported version before endpoint
registration or reconnect/countdown side effects. This is a coordinated cutover, not
backward-compatible partial decoding.

### 4b. Server → Client: World Frame

```
Header (WorldFramePacket.HeaderSize)
[0..3]    magic "WFRM"
[4]       WorldFramePacket.FormatVersion
[5]       SimulationProtocol.Version
[6]       flags — bit 0: body DEFLATE-compressed
[7]       MatchState
[8..11]   tick — the World Snapshot's tick
[12..15]  uncompressed body length (bounded by WorldFramePacket.MaxBodyBytes)
Body
  World Snapshot          length + ServerSimulation.CaptureSnapshot bytes
  relayed inputs          count; per fighter in ascending entity order:
                          entityId + hasInput [+ InputState when hasInput]
  presentation events     complete records from EventsFromTick through Tick
  hits                    complete records from EventsFromTick through Tick
  Slopball events         match tick, kind, ball ID, returns, contact time, camp
  course events           match tick, entity ID, kind, target/checkpoint index,
                          elapsed course ticks, per-tick sequence
```

[`WorldFramePacket`](../../src/Shared/WorldFramePacket.cs) owns the exact encoding,
format and protocol checks, event limits, and bounded DEFLATE decoding. Its
`EventHistoryTicks` defines the repeated history window. Mode event lists are empty
when inapplicable. Malformed frames are rejected with `InvalidDataException`.
The GameHost keeps this history in [`WorldFrameHistory`](../../src/Shared/WorldFrameHistory.cs).

### 4c. Retired: per-entity state packet (protocol 9)

Protocol 9 sent one of these per fighter per tick. Full-world rollback replaced it
with World Snapshots. The layout and acceptance rules below are historical records,
not the current online codec contract. The type names below identify that earlier implementation.

```
Receive packet per entity: entityId(8) + tick(4) + CharacterStatePacket(202) + hasInput(1) + InputState(22) = up to 237 bytes

[0..7]      entityId          (ulong)
[8..11]     tick              (uint)       ← echoes client's tick number
[12..213]   CharacterStatePacket (202 bytes) — fixed state payload; see §4b table below
[214]       hasInput          (byte)       ← exactly 0 or 1
[215..236]  InputState       (22 bytes)   ← present iff hasInput == 1
```

**The relay section** carries the exact input consumed for that entity/tick. A missing
exact server input may extend prior held input but clears one-shot `DownPressed`,
`ShieldPressed`, and `GrabPressed`; `ShieldHeld` remains latched. `hasInput = 0` denotes
no consumed input and is not a truncated relay. The envelope must be exactly 215 bytes
for marker 0 or 237 bytes for marker 1; mismatches are rejected. The client discards
malformed/incompatible state datagrams without ending its receive loop. Historical codec source:
`src/Shared/ServerEntityPacket.cs`.

InputState is 22 bytes. `Flags` (byte 8) carries `0x20=DashPressed`, the homing dash edge
(ADR-0042); `0x40` stays reserved. `Flags2` (byte 19) assigns bits `0x10=ShieldHeld`,
`0x20=ShieldPressed`, `0x40=GrabPressed`, and `0x80=RetargetPressed`.
`ShieldPressed` and `GrabPressed` are one-tick edges. The sim chooses grounded shield
versus airborne dodge from authoritative state; client-side chord binding emits
`GrabPressed` and removes a competing defense edge without clearing the physical hold.

**CharacterStatePacket layout (202 bytes):**
| Offset | Type    | Field                       | Notes                              |
|--------|---------|-----------------------------|------------------------------------|
| 0-3    | uint    | TickNumber                  | Echoed client tick (for matching)  |
| 4-7    | float   | PositionX                   | World X                            |
| 8-11   | float   | PositionY                   | World Y (up)                       |
| 12-15  | float   | PositionZ                   | World Z (forward)                  |
| 16-19  | float   | VelocityX                   | World velocity X                   |
| 20-23  | float   | VelocityY                   | World velocity Y                   |
| 24-27  | float   | VelocityZ                   | World velocity Z                   |
| 28     | byte    | CurrentActionState          | 0, 2-4, 6-16 stable; 1, 5, 17 reserved; 18 AirDodgeMovement, 19 AirDodgeRecovery |
| 29     | byte    | IsGrounded                  | 0 or 1                             |
| 30-31  | ushort  | StateDurationFrames         | Remaining ticks in current state   |
| 32     | byte    | AttackSlot                  | 0=none, 1-11 (ADR-0016 slot layout)|
| 33     | byte    | ComboStage                  | 0-3 combo chain stage              |
| 34-37  | float   | FacingYaw                   | Server-authoritative facing (radians) |
| 38     | byte    | MatchState                  | Match lifecycle (Waiting/Countdown/Playing/Ended) |
| 39     | byte    | AnimIndex                   | Animation index into ability's AnimationNames[] |
| 40     | byte    | HitstunLevel                | 0=small, 1=medium, 2=hard          |
| 41-44  | float   | AimPitch                    | Server-authoritative aim pitch (radians) |
| 45     | byte    | Deaths                      | Stock counter: stocks left = maxStocks - Deaths (issue #37) |
| 46-47  | ushort  | DamagePercent               | Smash-style damage %, HUD display (issue #38) |
| 48-69  | ushort×11| Cooldown0..10              | Per-slot cooldown ticks (ADR-0016: 11 slots), local HUD fills (issue #38) |
| 70-71  | ushort  | AirTimeTicks                | FloatWindow gravity timer |
| 72-73  | ushort  | DashDurationTicks           | Reserved legacy universal-dash timer; not the air-dodge phase timer |
| 74-77  | float   | DashDirX                    | AirDodgeMovement direction X, captured from the stick (facing when neutral) |
| 78-81  | float   | DashDirZ                    | AirDodgeMovement direction Z, captured from the stick (facing when neutral) |
| 82-83  | ushort  | DashCooldownTicks           | Reserved legacy universal-dash cooldown |
| 84     | byte    | AirDodgesLeft               | Remaining air dodges (D10)         |
| 85     | byte    | JumpsLeft                   | Remaining jumps (D10)              |
| 86-87  | ushort  | InvincibilityTicks          | Air-dodge/respawn invincibility (D10) |
| 88-89  | ushort  | RushTicks                   | Rush window remaining (ADR-0020)   |
| 90-93  | float   | LastDirX                    | Last input direction X (D10)       |
| 94-97  | float   | LastDirZ                    | Last input direction Z (D10)       |
| 98     | byte    | WasAirborneDuringKnockback  | Landing/tech context flag (D10)    |
| 99-100 | ushort  | HitstopTicks                | Remaining hitstop freeze ticks (ADR-0012) |
| 101-102| ushort  | BurstCooldownTicks          | Reserved retired Burst field; no gameplay meaning |
| 103-104| ushort  | BurstRecoveryTicks          | Reserved retired Burst field; never locks actions |
| 105    | byte    | JumpHeldTicks               | Consecutive jump-held ticks — short-hop replay (ADR-0016) |
| 106    | byte    | LockOn                      | Persistent target-lock flag (ADR-0018) |
| 107-108| ushort  | LedgeRegrabLockTicks         | Reserved field while automatic ledge grabs are disabled |
| 109    | byte    | AttackSequence              | Changes for each ability activation |
| 110-111| ushort  | LandingLagTicks             | Authoritative landing lock for reconciliation and presentation |
| 112    | byte    | MovementFlags               | bit0: IsFastFalling; bit1: JumpFromSlide; bit2: SlideAttackCarryActive; bit3: CrouchSettled; bit4: QueuedCrouchBrace; bit5: InPostHitstunFlight; bit6: AutoLockSuppressed |
| 113-114| ushort  | ShieldDropTicks             | Remaining vulnerable shield-drop recovery |
| 115-116| ushort  | BlockStunTicks              | Remaining block stun |
| 117    | byte    | BlockHitstopKind            | `0=none`, `1=shield contact` |
| 118    | byte    | InteractionPhase            | `0=none`, `1=attempt`, `2=captured`, `3=throwing`, `4=terminal` |
| 119-126| ulong   | InteractionId               | Stable active interaction identity; zero means no active interaction |
| 127-134| ulong   | InteractionPartnerId        | Paired fighter entity ID; zero means no partner |
| 135-142| ulong   | LastTerminalInteractionId   | Last completed/interrupted interaction; zero means none recorded |
| 143-146| uint    | InteractionTick             | Authoritative capture tick |
| 147-150| uint    | InteractionTerminalTick     | Authoritative terminal outcome tick |
| 151-152| short   | CapturedYaw                  | Grab facing snapshot, signed degrees × 100 |
| 153-154| ushort  | AirDodgeRecoveryTicks        | Grounded commitment left after an air-dodge landing |
| 155-162| ulong   | TargetEntityId               | Sticky selected target for deterministic lock reconstruction |
| 163    | byte    | ProtocolVersion              | `SimulationProtocol.Version = 9` |
| 164-171| ulong   | AttackCorrectionTargetId     | Single target captured for this activation |
| 172-175| float   | AttackCorrectionStartYaw     | Initial attack-facing yaw (radians) |
| 176-179| float   | AttackPosePitch              | Authoritative startup correction pose pitch (radians) |
| 180    | byte    | AttackCorrectionFlags        | bit0: correction active; bit1: correction owned by activation |
| 181    | byte    | AttackCorrectionTargetDeaths | Captured target death count; invalidates a respawned target |
| 182-183| ushort  | HitstunTicks | Remaining authoritative hit reaction ticks |
| 184-185| ushort  | HitReactionSequence | Changes for each new hit reaction; repeated snapshots keep it |
| 186-187| ushort  | AttackElapsedTicks | Active attack time for remote presentation; 0 outside Attacking/Aiming |
| 188-189| ushort  | ChargeTicks | Held charge for remote charge visuals; 0 outside Attacking/Aiming |
| 190-201| float×3 | KnockbackVelocityX/Y/Z | Knockback velocity; launch presentation and predicted flight |

**Historical packet sizes and cutover:** `CharacterStatePacket` was 202 bytes and
`ServerEntityPacket` 237 bytes with input. Input was 22 bytes. The subsequent protocol
10 cutover required matching inputs, controls, and Steam admission across Master,
GameServer, and clients. Its 20-byte control frame appended the Clock input report:
`InputSlack` (sbyte, 127 = no input since the previous Clock) and `LateInputs` (byte,
saturating).

---

## 5. CharacterState and packet roles (Shared)

[`CharacterState`](../../src/Shared/CharacterState.cs) is mutable per-fighter Shared
state. It is not the complete rollback snapshot or a fixed wire-size contract.

[`ServerSimulation.CaptureSnapshot` and `RestoreSnapshot`](../../src/Shared/ServerSimulation.Snapshot.cs)
capture and restore the full world. This includes fighter state and timers, identity
counters, hit credit, respawn overrides, active cooked timelines and capability state,
resolver hitboxes and projectiles, the Arcade ball and rules, and course progress.
Restore requires the same arena, rule, registered fighters, and admitted content.
Per-tick scratch collections and presentation outputs are not persistent snapshot state.

Active abilities must support the cooked snapshot contract. Capture rejects unsupported
ability runtimes instead of substituting raw presentation for re-simulation. Grabs,
throws, blocks, and hit reactions run in the predicted world and can be corrected by
the next authoritative frame.

`CharacterStatePacket`, `ServerEntityPacket`, and their partial `ApplyTo` semantics
were part of the retired per-entity path. They do not define current online
reconciliation. See the historical layout in section 4c.

---

## 6. Prediction & Rollback

The client predicts every fighter in one Shared simulation (ADR-0039):

- [`NetplayPredictor`](../../src/Shared/Rollback/NetplayPredictor.cs) restores the newest
  World Frame (the Anchor) and re-simulates the Prediction Window to the local tick: the
  player's recorded inputs (as the GameHost decodes them) and, for each opponent, the input
  relayed at the anchor tick without presses. Eliminated and disconnected fighters consume
  no input, as on the GameHost. Coupled interactions (grabs, throws, blocks) are predicted
  like everything else; the anchor corrects them. An exact anchor snapshot with unchanged
  assumed inputs can skip restoration and re-simulation.
- [`RollbackLedger`](../../src/Shared/Rollback/RollbackLedger.cs) hands out presentation
  events and hits once per key across re-simulation, retracts predicted ones that a
  re-simulation drops or the GameHost's record of their tick lacks, and starts GameHost
  ones the client never predicted.
- [`NetplaySession`](../../src/Shared/Rollback/NetplaySession.cs) adds the transport-free
  client protocol: Clock samples, frame ingestion, paced steps with buffered presses, and
  the input bundle ending at the local tick. The Unity bridge and the GameHost tests both
  drive it.

Cross-runtime differences (Unity Mono against .NET 8) only affect the few predicted ticks:
every frame re-anchors on GameHost state. Every ability must implement the snapshot
contract ([ability architecture](ability-architecture.md), step 6).

The protocol 9 three-track model (LocalTrack, PredictedTrack and RawTrack in
`RollbackSimulator`, with raw playout and a defense-interaction barrier) was superseded
by full-world rollback. It is not a current runtime or roster authoring contract.

---

## 7. ActiveSlot Pipeline

The gameplay path is:

```text
InputState.ActiveSlot
  → canonical ground/air slot
  → admitted character definition
  → cooked timeline and admitted trusted capabilities
  → Shared simulation state and presentation events
  → bridge-selected presentation
```

Physical controls are input adapters. They select the canonical slot; they are not a
second persisted move mapping. The admitted definition comes from the immutable Match
Content Catalog. Character content executes through cooked timelines; trusted temporary
capabilities use the shared ability lifecycle. There is no legacy character execution fallback.

The Shared simulation owns timing, hitbox/projectile resolution, damage, Knockback,
Hitstun, interruption, and emitted events. Unity does not resolve collisions or decide
gameplay results. See [`ServerSimulation.cs`](../../src/Shared/ServerSimulation.cs),
[`CharacterPackageCompiler.cs`](../../src/Shared/CharacterPackageCompiler.cs), and
[`hitbox-system.md`](hitbox-system.md).

---

## 8. Client presentation state

The client does not independently drive gameplay state. `PlayerRenderer` and related
visual systems consume the bridge's simulation output — online, the predicted world with
its eased corrections — and semantic presentation events:

```text
PvPMatch / Training
  → bridge-selected Shared state and events
  → PlayerRenderer / Animancer / UI / VFX
```

Animation, VFX, and audio remain presentation-only. They do not infer gameplay from raw
input and do not feed results back into Shared simulation.

Projectile visuals follow moving hitboxes in a Shared resolver: Training's own, and
online the predicted world's (`ProjectileVFXManager.SetPredictedSimulation`). Visuals are
keyed by owner, activation and authored visual operation (plus resolver order for
duplicates), so a projectile keeps its visual and trail through re-simulation. Impact
bursts on hitbox removal are Training-only, because re-simulation repeats removals;
confirmed hits play their own presentation events online. Visuals never feed Unity
position or collision back into the Shared simulation. `ProjectileVFXConfig` maps Wibou
slot A to the same `WibouKunaiProjectile` prefab (kunai model plus trail effects) on ground
and in air, and FightGuy slot A to `FightGuyKiBall`, scaled by the charge tier's radius.

Wibou sword trails follow the same resolver: `_weapon_hilt` → `_weapon_tip` hitboxes
that are active show the owner's trail, and their removal clears it. Training, Ability
Lab and online matches all read it directly.
The weapon's trail-style prefab (Wibou's generated pixel style) supplies the
cosmetic material, color and fade; Unity renders a short-lived blade sweep from
the attached sword's hilt/tip poses while active. VFX never owns hit timing or
collision.

---

## 9. Debug visualization

Use the existing [Hitbox System](hitbox-system.md) guidance for visualization. Debug
drawings may display geometry derived from Shared state or received events, but
visualization does not own collision, hit results, damage, or match authority. No new
debug wire protocol is defined by this document.

---

## 10. Implementation references and remaining product work

The implemented boundaries are:

- Shared deterministic simulation: [`src/Shared/ServerSimulation.cs`](../../src/Shared/ServerSimulation.cs);
- full-world client rollback through `NetplaySession`: [`src/Shared/Rollback/`](../../src/Shared/Rollback/) and
  [`RollbackSimulationBridge.cs`](../../client/Unity/Assets/Scripts/Runtime/Simulation/RollbackSimulationBridge.cs);
- content admission: [`MatchContentCatalog.cs`](../../src/Shared/MatchContentCatalog.cs) and
  the GameServer match-control/catalog providers under `src/Server/`;
- transport and packet handling: the Unity `NetworkClient` and Shared packet types.

The [playable friends demo reset](../plans/2026-09-05-playable-demo-reset.md) tracks the
remaining product work, including roster-complete publishing and remote join-to-rematch
acceptance. This guide does not claim unexercised remote play as verified.
