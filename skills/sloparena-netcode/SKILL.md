---
name: sloparena-netcode
description: "SLOPArena server-authoritative UDP netcode with Shared simulation, client prediction, rollback reconciliation, match content admission, and Unity bridges."
metadata: {"triggers": ["sloparena netcode", "server-authoritative", "rollback", "udp client server", "client-side prediction", "match manager", "apply server state"]}
---

# SLOPArena Netcode

SLOPArena has three server-like layers:

- **Master server** — separate repository; owns ServerBrowser, LobbyRoom, character selection, and results.
- **GameServer** — this repository's `src/Server/` process; owns match orchestration, UDP transport, and authoritative match ticks.
- **ServerSimulation** — pure Shared C# simulation; runs on the GameServer and on client prediction tracks.

The GameServer validates the exact Match Content Catalog before simulation. Clients verify package IDs, versions, dependency hashes, capability versions, and cooked hashes; missing or mismatched content fails closed.

## Wire boundary

The packet formats are defined by the `Size` constants and serializer implementations in `src/Shared/`:

- `InputState.Size` is the client input payload size;
- `CharacterStatePacket.Size` is the authoritative state payload size;
- `ServerEntityPacket` wraps entity ID, tick, state, and optional InputRelay data.

When changing a wire field, update the corresponding size constant and every serializer/deserializer, then run codec tests. Do not copy packet byte counts into general gameplay documentation; they are implementation contracts and can change with accepted protocol work.

InputRelay carries the input consumed for an entity/tick, including the explicit no-input marker. This lets PredictedTrack replay the same input omissions as the GameServer.

## Client flow

`TrainingMatch` uses `LocalSimulationBridge`. `PvPMatch` uses `RollbackSimulationBridge`:

1. `InputController.Poll()` produces the current `InputState`.
2. The bridge advances the local player's `LocalTrack` and sends input through `NetworkClient`.
3. Received `ServerEntityPacket` values reconcile the self track and feed opponent packets into `RollbackSimulator`.
4. Predictable opponent states rebuild from `ConfirmedTick` and replay InputRelay through the rollback window.
5. Complex/raw states render the latest authoritative packet without speculative gameplay.
6. `PlayerRenderer.ApplyServerState` renders state; it never decides gameplay.

Training and PvP therefore share the same Shared simulation contract while using different transport/composition seams.

## GameServer flow

`MatchInstance` receives UDP inputs, queues them per player, advances `ServerSimulation` at 60 Hz, evaluates the match rule, and broadcasts `ServerEntityPacket` values. `MultiMatchOrchestrator` allocates match ports; `MatchControlServer` receives match-start requests from the Master server.

The GameServer is the sole authority for movement, active content, hitboxes, projectiles, damage, Knockback, Hitstun, respawn, stocks, and match completion. Client prediction is a replay aid, not an authority override.

## Prediction and rollback invariants

- `LocalTrack` runs continuously from the player's true input and reconciles wire-serialized fields without rebuilding lossy ability-instance state.
- `PredictedTrack` rebuilds only entities in the predictable ActionState partition from a confirmed snapshot and relayed inputs.
- `RawTrack` renders complex states directly from the latest server packet.
- `ConfirmedTick` is the highest tick with a complete authoritative baseline for the tracked entity set.
- Rollback replay uses the same Shared definitions, arena, content handles, and input omissions as the server.
- Presentation effects deduplicate by stable event identity across prediction and rollback.

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

For Unity bridge changes, run the Unity CLI recompile/status and console-error gate from [`docs/contributing/unity-cli.md`](../../docs/contributing/unity-cli.md), then exercise a local PvP match when available.
