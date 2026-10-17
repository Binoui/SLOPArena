# SLOPArena Netcode & Simulation Architecture

## Smooth online play — protocol 9

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
  starvation, ping and frame time.

## Hit reaction snapshots — protocol 7

The Steam PvP playtest exposed repeated opponent hit-reaction restarts: raw network
states omitted HitstunTicks, so presentation received zero and treated unchanged
snapshots as new hits. Protocol 7 appends the authoritative HitstunTicks and
HitReactionSequence. Both launch paths advance the sequence once when a new
hit reaction begins; hitstop and ordinary countdown leave it unchanged.
PlayerRenderer uses the production Shared presentation policy to restart on
entry or a changed reaction identity, including shorter re-hits. Repeated/skipped
snapshots keep the same animation and the replicated timer enables position smoothing.
Release client, GameHost and Master together; protocol 6 peers are rejected.

## Startup-correction protocol cutover verification — 2026-10-04

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


## Authoritative PvP clock — protocol 6

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
facing/lock, defense/grab/retarget and slot press edges stripped, then becomes neutral.
Movement, jump hold and shield hold are retained. The client sends input for the tick being
stepped, never an expired preincrement tick. Catch-up is bounded to four steps per client
update; buffered one-shot edges are consumed once, while earlier catch-up steps retain
held controls without replaying presses. Match End/result handling is consumed independently
of whether the packet's simulation tick remains in local rollback history. A replacement
connection must receive the full roster baseline and Ready again.

## 1. Philosophy

**Server-authoritative Shared simulation with client prediction and reconciliation.**
The GameServer is authoritative. `ServerSimulation` also runs on client tracks so the
client can predict and reconcile without owning gameplay results. Unity presents the
track-selected simulation state and semantic events.

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

1. Builds `InputState` from the Unity input adapter and the canonical active slot.
2. Calls `RollbackSimulationBridge.Tick`.
3. The bridge sends local input through `NetworkClient`, advances the Shared local track,
   drains entity packets, match-result packets, and presentation-event queues, and exposes
   selected state and events for rendering.
4. The bridge routes the self packet to reconciliation and opponent packets to
   `RollbackSimulator`, which chooses `PredictedTrack` or `RawTrack` by action state.
5. `PvPMatch` applies bridge-selected state and presentation events to `PlayerRenderer`
   and other presentation systems.

Unity's project Fixed Timestep is **1/60 second**, matching the authoritative tick
cadence. This sets the rate of the client fixed-update loop; it does not replace
reconciliation or change its existing history-window policy.

See [`PvPMatch.cs`](../../client/Unity/Assets/Scripts/Runtime/World/PvPMatch.cs),
[`RollbackSimulationBridge.cs`](../../client/Unity/Assets/Scripts/Runtime/Simulation/RollbackSimulationBridge.cs),
and [`RollbackSimulator.cs`](../../src/Shared/Rollback/RollbackSimulator.cs).

### 3b. Training flow

Training uses `LocalSimulationBridge` with the same Shared simulation authority and
cooked/admitted content boundary. It builds local input, advances the local simulation,
and exposes state and presentation events to the renderers without a network transport.
Training does not use `NetworkSimulationBridge`.

### 3c. MatchInstance Tick Loop (60Hz, per match, own thread)

```
Tick():

  1. ReceiveInputs() — drain UDP socket, match by entityId
     → Queue inputs per PlayerSlot

  2. Check timeout (5s silence → match stops)

  3. Flush input queues (take last valid packet per slot)
     → _serverTick = max(_serverTick, latestClientTick)

  4. ServerSimulation.Tick(inputs)
     → SimulateTick: movement, gravity, ground, combat — everything
     → Spawn hitboxes from attack events (HitboxEvent.TriggerTick)
     → SpellResolver.Tick: hitbox vs hurtbox collision, damage, knockback, hitstun

  5. Check deaths (first to maxStocks=3 deaths loses → MatchState.Ended)
     → ServerSimulation.CheckVoidDeaths: KO costs a stock, respawn with brief
       invincibility; 0 stocks → eliminated (frozen spectator, untargetable)
     → StockMatchRule.Evaluate: last player standing wins; simultaneous
       last-stock trade → most stocks wins, equal deaths → shared victory (issue #37)

  6. SendState() — broadcast to all connected clients
     → For each client:
       → For each entity (all rostered players):
      → Packet: entityId(8) + tick(4) + CharacterStatePacket(202) + hasInput(1) + InputState(22) = up to 237B
         → tick = _serverTick (echoed back)
         → hasInput/InputState = the input the server consumed for that entity
           that tick, or the no-input marker (issue #80 — input relay)
       → Client filters by entityId
```

---

## 4. Packet Protocol

### 4a. Client → Server

```
Send datagram (InputBundle): entityId(8) + newestTick(4) + count(1) + count × InputState(22)
                             = 35..145 bytes (count 1..6)

[0..7]    entityId        (ulong)
[8..11]   newestTick      (uint)       ← tick of the last input
[12]      count           (byte)       ← 1..InputBundle.MaxInputs (6)
[13..]    InputState × count, oldest first, consecutive ticks ending at newestTick
```

Every simulated tick's input is sent, including catch-up ticks, and repeated in the next
datagrams, so the GameServer consumes exactly what the client predicted and a lost
datagram does not drop a press. Steam prefixes the bundle with the `Input` kind byte.

**InputState layout (22 bytes):**
| Offset | Type    | Field           | Notes                              |
|--------|---------|-----------------|------------------------------------|
| 0-3    | float   | MoveX           | Horizontal analog input            |
| 4-7    | float   | MoveY           | Vertical analog input              |
| 8      | byte    | flags           | bit0:Up, 1:Down, 2:Left, 3:Right, 4:Jump, 5:legacy Dash (reserved/inert; not an air-dodge edge), 6:retired Burst, 7:IsAiming |
| 9      | byte    | ActiveSlot      | 0=none, 1=LMB, 2=RMB, 3=key"1", 4=E, 5=R, 6=F, 7-10=keys"2"-"5", 11=A (ADR-0016) |
| 10-11  | short   | FacingYaw       | Degrees × 100 (movement-facing)    |
| 12-13  | short   | AimYaw          | Degrees × 100 (combat-facing, reserved) |
| 14-15  | short   | AimPitch        | Degrees × 100 (camera vertical aim) |
| 16-17  | ushort  | AimDistance     | cm (0-6500 = 0-65m)                |
| 18     | byte    | TargetEntityId  | Client-selected target (0 = none)  |
| 19     | byte    | flags2          | bit0: JumpHeld, bit1: FaceToCamera, bit2: ToggleLock, bit3: DownPressed, bit4: ShieldHeld, bit5: ShieldPressed, bit6: GrabPressed, bit7: RetargetPressed |
| 20     | byte    | LockMode        | `0=Never`, `1=Always`, `2=OnHit` |
| 21     | byte    | protocolVersion | `SimulationProtocol.Version = 9` |

`ShieldHeld` is the per-tick physical hold used for grounded shield. `ShieldPressed` is a
fresh logical edge and the sole airborne dodge input; the simulation accepts it only while
airborne. `GrabPressed` is also a one-tick edge. A completed controller grab chord consumes
the competing defense edge without clearing the physical hold.

Validate the exact envelope, input count and supported version before endpoint
registration or reconnect/countdown side effects. This is a coordinated cutover, not
backward-compatible partial decoding.

### 4b. Server → Client (per entity)

```
Receive packet per entity: entityId(8) + tick(4) + CharacterStatePacket(202) + hasInput(1) + InputState(22) = up to 237 bytes

[0..7]      entityId          (ulong)
[8..11]     tick              (uint)       ← echoes client's tick number
[12..213]   CharacterStatePacket (202 bytes) — fixed state payload; see §4b table below
[214]       hasInput          (byte)       ← exactly 0 or 1
[215..236]  InputState       (22 bytes)   ← present iff hasInput == 1

**The relay section** carries the exact input consumed for that entity/tick. A missing
exact server input may extend prior held input but clears one-shot `DownPressed`,
`ShieldPressed`, and `GrabPressed`; `ShieldHeld` remains latched. `hasInput = 0` denotes
no consumed input and is not a truncated relay. The envelope must be exactly 215 bytes
for marker 0 or 237 bytes for marker 1; mismatches are rejected. The client discards
malformed/incompatible state datagrams without ending its receive loop. Codec owner:
`src/Shared/ServerEntityPacket.cs`.

InputState is 22 bytes. `Flags2` (byte 19) assigns bits `0x10=ShieldHeld`,
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
| 74-77  | float   | DashDirX                    | AirDodgeMovement direction X, captured from facing |
| 78-81  | float   | DashDirZ                    | AirDodgeMovement direction Z, captured from facing |
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

**Packet sizes:** `CharacterStatePacket` is 202 bytes. `ServerEntityPacket` is 214 bytes
before its mandatory relay marker, 215 bytes without input and 237 bytes with input.
Input is 22 bytes. Non-v9 state/input/control payloads are rejected. Steam admission also
uses protocol version 9; coordinate Master `protocolVersion` with GameServer and clients.
The 20-byte control frame appends the Clock input report: `InputSlack` (sbyte, 127 = no
input since the previous Clock) and `LateInputs` (byte, saturating).

**The server sends ALL states to every client.** Clients ignore the ones that don't concern them. No routing overhead.

**Tick echo:** The server echoes the consumed client tick so `RollbackSimulationBridge`
can match the self response to local history for reconciliation. Opponent packets are
ingested by `RollbackSimulator` into predicted or raw tracks; the tick is not merely
informational.

---

## 5. CharacterState and packet roles (Shared)

`CharacterState` is the mutable per-entity Shared simulation state. It is used directly
by `ServerSimulation` and the rollback tracks; it is not a hand-maintained wire-size
contract.

`CharacterStatePacket` is the explicit serialized state snapshot used for reconciliation
and opponent ingestion. `ServerEntityPacket` wraps an entity identity, tick, state packet,
and optional relayed input. Their layouts and codecs are owned by the source files:

- [`CharacterState.cs`](../../src/Shared/CharacterState.cs)
- [`CharacterStatePacket.cs`](../../src/Shared/CharacterStatePacket.cs)
- [`ServerEntityPacket.cs`](../../src/Shared/ServerEntityPacket.cs)

Snapshots serialize the fields needed to rebuild predictable movement state. Ability
instance fields such as active hitbox/projectile lists and private lifecycle state are
not fully reconstructible; complex action states therefore use received state rather than
client re-simulation.

`ShieldDropTicks` is serialized with the other defense timers. `ApplyTo` overwrites
carried state including `AirTimeTicks`; it preserves local-only fields such as attack
elapsed ticks and queued knockback. Both full decode and reconciliation must retain
these authoritative timers rather than clearing them or retaining stale local values.

---

## 6. Prediction & Rollback

The client uses the three-track model implemented in
[`RollbackSimulator`](../../src/Shared/Rollback/RollbackSimulator.cs):

- **LocalTrack** continuously simulates the self entity with the player's input and
  reconciles corrections from the server.
- **PredictedTrack** replays predictable opponents from confirmed state and relayed input.
- **RawTrack** renders complex or unknown opponents from their latest received state when
  their private ability state cannot be reconstructed.

This is narrower than predicting every remote ability: predictable opponents can be
replayed, while complex or unknown opponents use received state.

Defense interactions use a targeted authoritative barrier. The client predicts shield,
grab attempt/whiff, and airborne dodge but sets
`ServerSimulation.PredictCoupledInteractions = false` for the self track. A server capture
or block contact replaces self state even across locally complex attack history;
`ApplyAuthoritativeState` cancels the old live ability and owned hitboxes first.
During capture the self track holds the newest authoritative snapshot rather than
replaying itself through a two-fighter interval. The bridge ingests a full packet drain
together. Captured opponents stay raw; matching interaction ID, partner IDs and capture
tick pair companion snapshots when they arrive. Missing companions are bounded to a
30-tick pending window. A terminal outcome is sufficient without a prior capture and
allows ordinary prediction on later snapshots; per-entity packet ticks and terminal identity/tick reject
duplicate or older capture packets. Presentation events retain their stable event-key
deduplication across rollback. This barrier does not make arbitrary ability snapshots
reconstructible.

Sliding and Crouching are predictable locomotion states. Exact history replay preserves
DownPressed; every speculative opponent frontier tick clears it, including the first
tick after a received relay. Held Down remains intact.

An authoritative snapshot or local replay suffix with `HitstopTicks > 0` cannot be
reconstructed/replayed, even if its ActionState is ordinarily predictable. Frozen
opponents use RawTrack. Packets preserve QueuedCrouchBrace but do not contain the complete
queued launch payload or ability instance; only a live full-fidelity simulation resolves
that launch. This is not arbitrary combat rewind.

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
visual systems consume the bridge-selected simulation output, including local prediction,
reconciled self state, predicted opponents, raw opponents, and semantic presentation events:

```text
PvPMatch / Training
  → bridge-selected Shared state and events
  → PlayerRenderer / Animancer / UI / VFX
```

Animation, VFX, and audio remain presentation-only. They do not infer gameplay from raw
input and do not feed results back into Shared simulation.

Wibou Q projectile visuals also use a dedicated, presentation-only
`ProjectileVisualPacket`: the GameServer snapshots each active kunai's owner,
activation, authored operation index, position, and velocity every simulation
tick, including empty snapshots that remove expired or impacted visuals.
`PvPMatch` renders these server snapshots for both players; Training follows its
local Shared resolver. The packet never feeds Unity position or collision back
into the Shared simulation. `ProjectileVFXConfig` maps Wibou slot A to the same
`WibouKunaiProjectile` prefab (kunai model plus trail effects) on ground and in air,
and FightGuy slot A to `FightGuyKiBall`, scaled by the charge tier's radius.

Wibou sword trails use the separate presentation-only `SwordTrailSnapshotPacket`.
Its per-tick active-owner set is derived from server-owned `_weapon_hilt` →
`_weapon_tip` hitboxes; empty snapshots clear trails after expiration, interruption,
or match end. Training and Ability Lab read the same Shared resolver directly.
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
- three-track client rollback: [`src/Shared/Rollback/`](../../src/Shared/Rollback/) and
  [`RollbackSimulationBridge.cs`](../../client/Unity/Assets/Scripts/Runtime/Simulation/RollbackSimulationBridge.cs);
- content admission: [`MatchContentCatalog.cs`](../../src/Shared/MatchContentCatalog.cs) and
  the GameServer match-control/catalog providers under `src/Server/`;
- transport and packet handling: the Unity `NetworkClient` and Shared packet types.

The [playable friends demo reset](../plans/2026-09-05-playable-demo-reset.md) tracks the
remaining product work, including roster-complete publishing and remote join-to-rematch
acceptance. This guide does not claim unexercised remote play as verified.
