---
name: sloparena-netcode
description: "SLOPArena server-authoritative UDP netcode with Shared simulation, client prediction, rollback reconciliation, match content admission, and Unity bridges."
metadata: {"triggers": ["sloparena netcode", "server-authoritative", "rollback", "udp client server", "client-side prediction", "match manager", "apply server state"]}
---

# SLOPArena Netcode

SLOPArena has three server-like layers:

- **Master server** — separate repository; owns ServerBrowser, LobbyRoom, character selection, and results.
- **GameServer** — this repository's `src/Server/` process; owns match orchestration, UDP transport, and authoritative match ticks.
- **ServerSimulation** — pure Shared C# simulation; runs on the GameServer and in each client's full-world prediction.

The GameServer validates the exact Match Content Catalog before simulation. Clients verify package IDs, versions, dependency hashes, capability versions, and cooked hashes; missing or mismatched content fails closed.

## Wire boundary

The packet formats are defined by the `Size` constants and serializer implementations in `src/Shared/`:

- `InputState.Size` is the client input payload size; uplink datagrams are `InputBundle`s
  repeating the newest simulated ticks, so a lost datagram never drops a press;
- `WorldFramePacket` (protocol 10) carries, each tick, the World Snapshot, every fighter's
  consumed input (InputRelay) and the complete presentation event and hit record of the
  last three ticks; the World Snapshot layout is `ServerSimulation.SnapshotFormatVersion`.

When changing a wire field, update the corresponding size constant and every serializer/deserializer, then run codec tests. Do not copy packet byte counts into general gameplay documentation; they are implementation contracts and can change with accepted protocol work.

InputRelay carries the input consumed for each fighter at the frame's tick, including the explicit no-input marker for eliminated or disconnected fighters. The predictor repeats it without presses for opponents, as the GameServer holds a missing input. Held input on the server, the session's catch-up ticks and opponent prediction all use `InputState.WithoutEdges`; keep them identical.

## Client flow

`TrainingMatch` uses `LocalSimulationBridge`. `PvPMatch` uses `RollbackSimulationBridge`:

1. `InputController.Poll()` produces the current `InputState`.
2. `PumpNetwork` feeds Clock controls and World Frames to the transport-free `NetplaySession`; its `NetplayPredictor` restores the newest frame (the Anchor) and re-simulates every fighter to the local tick.
3. `Tick` advances paced steps (`NetplayClock.StepsForUpdate`) and sends the input bundle ending at the local tick through `NetworkClient`.
4. The ledgers hand out presentation events and hits to start, and those a rollback ruled out; projectile visuals and sword trails read the predicted resolver.
5. `PlayerRenderer.ApplyServerState` renders the predicted state with eased corrections; it never decides gameplay.

Training and PvP therefore share the same Shared simulation contract while using different transport/composition seams.

## GameServer flow

`MatchInstance` receives UDP or Steam inputs into each player's `PeerInputTimeline`, advances `ServerSimulation` at 60 Hz, evaluates the match rule, and sends every client the same `WorldFramePacket` each tick (countdown frames carry the server tick; the frame that turns Playing is `StartTick − 1`). `MultiMatchOrchestrator` allocates match ports; `MatchControlServer` receives match-start requests from the Master server.

The GameServer is the sole authority for movement, active content, hitboxes, projectiles, damage, Knockback, Hitstun, respawn, stocks, and match completion. Client prediction is a replay aid, not an authority override.

## Prediction and rollback invariants

- Every online fighter is predicted in one `ServerSimulation`; each newer World Frame is restored and the Prediction Window re-simulated. The client keeps no divergent history.
- A World Snapshot must be complete: every ability and capability implements the snapshot contract (`docs/systems/ability-architecture.md`, step 6); `WorldSnapshotTests` and `WorldSnapshotContractTests` catch missing fields.
- Re-simulation uses the same Shared definitions, arena, content handles, rule and input omissions as the server; fighters iterate in entity order.
- Presentation events and hits go through `RollbackLedger`: one start per key, retraction when the anchor rules a prediction out.
- Prediction never starts before the frame that turns Playing, and the bundle never carries inputs from before a timeline jump.

Do not add client-only gameplay corrections, Unity physics authority, or a second ability implementation.

## Verification

After Shared or packet changes:

```bash
dotnet build src/Shared/ --nologo -p:SkipUnityPluginCopy=true
dotnet test tests/Shared.Tests/ --nologo -p:SkipUnityPluginCopy=true
```

After server changes:

```bash
dotnet build src/Server/ --nologo -p:SkipUnityPluginCopy=true
```

`tests/Shared.Tests/NetplayPredictorTests.cs` (the `NetplayLab`: GameHost loop and predictors through the codecs, with delay, jitter, loss, stalls and cuts), `NetplaySessionTests` and `tests/Server.Tests/FullWorldNetplayTests.cs` (a real `MatchInstance` over UDP with two `NetplaySession` peers) cover the protocol without Unity.

For Unity bridge changes, run the Unity CLI recompile/status and console-error gate from [`docs/contributing/unity-cli.md`](../../docs/contributing/unity-cli.md), then exercise a local PvP match. Without a second client: run a development GameServer (`src/Server/server.json` copy, unreachable `masterServerUrl` is fine), `POST /match/start` a two-player roster, enter Play Mode, set `SLOPARENA_DEV_UDP=1` in the Editor process and call `ClientSession.ApplyMatchStarted` with the returned content map and port (entity 1), and drive entity 2 with a headless `NetplaySession` peer like the one in `FullWorldNetplayTests`. Restore the environment variable and stop Play Mode afterwards.
