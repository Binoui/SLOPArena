> ⚠️ **SUPERSEDED** — Replaced by `docs/plans/2026-08-01-pvp-roadmap-v2.md` on 2026-08-01.
> The v2 roadmap re-scopes to online PvP with server browser, SignalR lobbies, 2-4 players, and stock mode (ADRs 0003-0008).
> This doc's Phase 1 (PvP Bridge) and Phase 4 (UI Flow) shipped. Phase 2 (Prediction/Rollback) is now v2's Phase 7. Content preserved for reference.

# SLOPArena — PvP Demo Roadmap (archived 2026-07-03)

> Original status: Codebase-audited 2026-07-03. Replaced `2026-06-26-unity-client-architecture.md` and `2026-06-14-online-pvp-roadmap.md` (both deprecated).
> Original goal: Functional Unity client PvP match (not sandbox) over UDP. Two players, each in their own Unity instance, connecting via a headless server.
> Original mindset: The training/sandbox mode works with full combat, VFX, and NPC AI. The server binary is production-grade. The gap is entirely on the **client-side bridge between NetworkClient → simulation**.

---

## Audit Snapshot (2026-07-03)

| Area | Status | What exists |
|---|---|---|
| **Client sim loop** | ✅ Done | TrainingMatch with 60Hz FixedUpdate, LocalSimulationBridge, InputController, 215 xUnit tests |
| **Client combat** | ✅ Done | CombatFeedback (hit sparks), knockback Lerp, hitstun anims, death flash |
| **Client HUD** | ✅ Done | Damage %, 6-slot cooldown fills (UIDocument) |
| **Client aiming** | ✅ Done | Ground ring + arc line for Q bomb, soft-lock target indicator |
| **Client NPC AI** | ✅ Done | NpcAiMode.Attack: follows, attacks every ~2s, jumps, respawns on void death |
| **Client VFX** | 🔶 Partial | Hit sparks exist. No flamethrower/aerosol/bomb explosion VFX |
| **Client network** | 🔶 Partial | NetworkClient (UDP, receive thread, ConcurrentQueue). No prediction/rollback loop, no PvPMatch |
| **Client UI flow** | ❌ Missing | No menus, no character select, no scene loading (empty build settings) |
| **Client FightGuy** | ❌ Missing | `fightguy_skeleton.bin` exists, no animation config or renderer |
| **Server match** | ✅ Done | MatchInstance: ServerSimulation, input buffering, timeout, 3-stock, match lifecycle |
| **Server orchestration** | ✅ Done | MultiMatchOrchestrator: port allocation, 15 concurrent matches |
| **Server registration** | ✅ Done | GameServerRegistration: master server HTTP registration, 10s heartbeat, match results |
| **Server collision** | ✅ Done | JitterCollisionWorld: Jitter2 capsule-vs-triangle collision |
| **Shared code** | ✅ Solid | Pure C# netstandard2.1, no Unity deps, symlinked into Unity Assets as source |

---

## Pre-Phase — Serialization Fix (do this first)

**Deliverable:** `CharacterStatePacket.FromState()` and `ToState()` serialize all fields needed for PvP client rendering. Fix and rebuild shared DLL.

### P0.1: Fix `FromState()` missing fields

**File:** `src/Shared/CharacterStatePacket.cs`, `FromState()` method (lines 48-66)

**Missing from converter:** `AttackSlot`, `ComboStage`, `FacingYaw`, `AnimIndex`, `MatchState`

**Change:** Add the five missing fields to the object initializer:
```csharp
AttackSlot = s.AttackSlot,
ComboStage = s.ComboStage,
FacingYaw = s.FacingYaw,
AnimIndex = s.AnimIndex,
MatchState = s.MatchState,
```

### P0.2: Fix `ToState()` missing fields

**File:** `src/Shared/CharacterStatePacket.cs`, `ToState()` method (lines 69-87)

**Missing from converter:** `ComboStage`, `FacingYaw`, `AnimIndex`, `MatchState` (`AttackSlot` is already set)

**Change:** Add the four missing fields:
```csharp
ComboStage = ComboStage,
FacingYaw = FacingYaw,
AnimIndex = AnimIndex,
MatchState = MatchState,
```

### P0.3: Build + test

```bash
dotnet build src/Shared/ --nologo
dotnet test tests/Shared.Tests/ --nologo
```

The post-build step auto-copies the DLL to `client/Unity/Assets/Plugins/SlopArena.Shared/`.

**Verify:** Unity recompiles. No new warnings.

## Phase 1 — PvP Client Bridge (high priority)

**Deliverable:** Two Unity instances connect to the headless server and see each other move. No prediction/rollback yet — raw state display.

### T1.1: Build `NetworkSimulationBridge`

**What:** New `Runtime/Simulation/NetworkSimulationBridge.cs` implementing `ISimulationBridge` pattern (currently only `LocalSimulationBridge` exists).

**Interface:**
```csharp
public interface ISimulationBridge
{
    void Tick(Dictionary<ulong, InputState> inputs);
    CharacterState GetState(ulong entityId);
    Dictionary<ulong, CharacterState> GetAllStates();
}
```

`LocalSimulationBridge` already wraps `ServerSimulation` directly. `NetworkSimulationBridge` wraps `NetworkClient`:
- `Tick()` → send input via `NetworkClient.SendInput()`, call `NetworkClient.ReceiveStates()` to get latest server state
- `GetState()` → read from the received state dictionary
- No prediction yet — display server state with one-frame latency

**Files:**
- Create: `Runtime/Simulation/NetworkSimulationBridge.cs`
- Create: `Runtime/Simulation/ISimulationBridge.cs` (extract interface)
- Modify: `LocalSimulationBridge.cs` — implement `ISimulationBridge`

**Verify:** Console log shows `Received states for N entities` each tick.

### T1.2: Create `PvPMatch`

**What:** New `Runtime/World/PvPMatch.cs` extending `MatchBase`. Same structure as `TrainingMatch` but uses `NetworkSimulationBridge` instead of `LocalSimulationBridge`. No NPC. Players connect by entity ID.

**Files:**
- Create: `Runtime/World/PvPMatch.cs`
- Modify: `MatchBase.cs` — add `matchType` support or make abstract enough for both

**Reference:** `TrainingMatch.cs` is the template — copy the tick structure, strip NPC logic.

**Verify:** Connect two clients to server (`dotnet run --project src/Server/`). Both see opponent's position updating in console logs.

### T1.3: Register PvP scenes in build settings

**What:** Create `Scenes/Arena_PvP.unity` scene with `PvPMatch` MonoBehaviour. Register both `Arena_Offline` and `Arena_PvP` in `EditorBuildSettings`.

**Files:**
- Create: `Scenes/Arena_PvP.unity` (clone Arena_Offline, swap TrainingMatch → PvPMatch)
- Modify: `ProjectSettings/EditorBuildSettings.asset`

**Verify:** Both scenes appear in Build Settings. Build produces a standalone client.

---

## Phase 2 — Prediction + Rollback (high priority)

**Deliverable:** Smooth local movement with server reconciliation. No rubber-banding on stable connection.

### T2.1: Add input + state ring buffers

**What:** 10-frame ring buffer for input history + predicted state history. After each server state arrival, compare predicted state to server state — if mismatch exceeds threshold, re-simulate from the mismatch tick.

**Files:**
- Create: `Runtime/Simulation/RingBuffer.cs` (generic ring buffer, pure C#, testable)
- Modify: `NetworkSimulationBridge.cs` — store inputs + predicted states per tick

**Prediction loop:**
```csharp
// Each FixedUpdate:
1. Send current input to server
2. Predict: run ServerSimulation.Tick(localInputs) locally
3. Store predicted state in ring buffer
4. If server state arrived this tick:
   a. Find matching tick in buffer
   b. Compare predicted state to server state
   c. If mismatch > threshold (0.5m position, 5deg rotation):
      - Roll back to that tick
      - Re-simulate with stored inputs up to current tick
      - Apply corrected state
```

**Verify:** Standalone client moves smoothly. Temporarily add lag (Thread.Sleep on server) — client snaps back on desync, doesn't drift.

### T2.2: Extract local sim from TrainingMatch

**What:** `TrainingMatch` currently owns its `ServerSimulation` instance. For PvP rollback, `PvPMatch` needs its own local `ServerSimulation` for prediction AND the bridge for remote state. Extract into a shared pattern.

**Current flow:**
```
TrainingMatch → owns LocalSimulationBridge → wraps ServerSimulation
```

**Target:**
```
PvPMatch → owns NetworkSimulationBridge → wraps NetworkClient
          → owns local ServerSimulation (for prediction rollback)
```

**Files:**
- Modify: `TrainingMatch.cs` — minor refactor, no behavioral change
- Modify: `NetworkSimulationBridge.cs` — accept a local `ServerSimulation` for prediction

**Verify:** Training mode unchanged. PvP mode: local prediction feels instant, snaps to server state on desync.

### T2.3: Add XZ rollback threshold

**What:** Rollback currently only checks Y axis (from old Godot code). Check all axes. Threshold: `distSq > 0.25f` (0.5m).

**Reference:** Already documented in old PvP roadmap T3.1 — port the logic.

**Files:**
- Modify: `NetworkSimulationBridge.cs` (or where reconciliation lives)

**Verify:** Client with artificially wrong local physics snaps to correct position.

---

## Phase 3 — Client Polish (medium priority)

**Deliverable:** Feels like a real game. Hit feedback, sounds, smooth visuals.

### T3.1: Combat VFX — Flamethrower, Bomb, Aerosol

**What:** Wire Unity particle systems for Manki's three spell VFX:

| Ability | VFX |
|---|---|
| RMB (Aerosol Flame) | Cone-shaped flame particle, streams while held |
| Q (Round Bomb) | Projectile arc (exists), explosion sphere on impact |
| E (Bazooka) | Rocket projectile trail + explosion |

**Files:**
- Create: `Runtime/VFX/SpellVFXManager.cs` — reads ability events from simulation, routes to correct VFX
- Create: `Runtime/VFX/FlamethrowerVFX.cs` — particle system attached to character muzzle
- Create: `Runtime/VFX/ExplosionVFX.cs` — sphere explosion at hit location
- Create: VFX prefabs in `Assets/Prefabs/VFX/`

**Signals:** Use `HitResult` from `SpellResolver.Tick()` output, plus ability activation events.

**Reference:** `CombatFeedback.cs` already handles hit sparks — extend it to route ability-specific VFX.

**Verify:** RMB → flame cone appears. Q → bomb arcs then explodes. E → rocket trails + explosion.

### T3.2: Sound effects placeholder

**What:** Add `AudioSource` + placeholder SFX for: hit confirm, jump, dash, death, explosion. Use free/placeholder assets.

**Files:**
- Create: `Runtime/Combat/CombatSFX.cs` — plays hit sounds on `LastTickHits`
- Create: `Runtime/Entities/PlayerSFX.cs` — jump, dash, death sounds

**Verify:** Hit dummy → hit sound plays. Jump → jump sound.

### T3.3: Hit pause / hitstop

**What:** Brief (2-3 tick) visual freeze on hit. Only on the client, no server impact.

**Files:**
- Modify: `CombatFeedback.cs` — on hit, set `Time.timeScale = 0f` for 2-3 frames via coroutine

**Verify:** Hit connects → brief freeze frame. Game resumes. No desync.

---

## Phase 4 — UI Flow ✅ (superseded)

**Superseded by:** `docs/superpowers/specs/2026-07-09-menu-ui-flow-design.md` and `docs/superpowers/plans/2026-07-09-menu-ui-flow.md`

**What shipped:** MainMenu (nested list with Training/Multiplayer/Host/Join) → Lobby → CharSelect (2-panel: grid + 3D preview with ability cards) → StageSelect → match. `MatchConfig` static class carries char/arena across scene loads. `MatchBase` no longer uses Inspector fields for char/arena.

**Files:**
- Create: `Scenes/MainMenu.unity`
- Create: `Runtime/UI/MainMenuUI.cs`
- Modify: `EditorBuildSettings.asset` — add MainMenu as scene 0

**Verify:** Run game → Main Menu appears. Click Training → loads Arena_Offline.

### T4.2: Character Select scene

**What:** `Scenes/CharacterSelect.unity` with Manki + FightGuy portraits. Player picks, then match starts.

**Files:**
- Create: `Scenes/CharacterSelect.unity`
- Create: `Runtime/UI/CharacterSelectUI.cs`
- Create: `Runtime/World/MatchOrchestrator.cs` — handles scene transitions, passes selected character class

**Verify:** Main Menu → Character Select → pick Manki → loads TrainingMatch with Manki.

### T4.3: Scene load integration

**What:** Wire `SceneManager.LoadSceneAsync` with a loading screen overlay. Both training and PvP matches receive selected character via `MatchOrchestrator`.

**Files:**
- Modify: `MatchBase.cs` — accept `CharacterClass` parameter
- Modify: `TrainingMatch.cs` — use passed character class instead of hardcoded `CharacterClass.Manki`
- Modify: `PvPMatch.cs` — same

**Verify:** Pick FightGuy in Character Select → Training starts with FightGuy.

---

## Phase 5 — FightGuy (low priority)

**Deliverable:** Second character playable in both Training and PvP modes.

### T5.1: Create FightGuy `CharacterAnimationConfig`

**What:** Create FightGuy animation config ScriptableObject, assign clips from `bunny.glb` (embedded animations).

**Files:**
- Create: `Assets/Resources/Characters/FightGuyAnimConfig.asset`
- Modify: `FightGuyData.cs` — set `ModelResourcePath = "Characters/FightGuy"` (or wherever the prefab lives)

**Reference:** Manki's config pattern — same clip names (`idle`, `run`, `jump`, `fall`, `hit_small`, etc.)

**Verify:** Select FightGuy → all animations play correctly.

### T5.2: Wire FightGuy abilities

**What:** Define FightGuy's ability slots (LMB/RMB/Q/E/R/F) in `FightGuyData.cs`. Implement any custom ability behaviors.

**Reference:** `MankiData.cs` + `MankiLmbCombo.cs`, `MankiAerosolFlame.cs`, etc.

**Verify:** FightGuy's RMB/Q/E abilities work in training mode.

### T5.3: FightGuy VFX

**What:** Particle effects for FightGuy's abilities (e.g., ice/spell VFX from existing assets in `AnimationPacks/`).

**Files:**
- Modify: `SpellVFXManager.cs` — add FightGuy ability routing

**Verify:** FightGuy abilities produce VFX.

---

## Phase 6 — Hardening (low priority, pre-release)

**Deliverable:** PvP demo playable over LAN, stable for extended matches.

### T6.1: Server-client handshake

**What:** Formal connection handshake: client sends `ConnectPacket` with desired character/region, server responds with `WelcomePacket` containing `EntityId`, tick, and initial state.

**Files:**
- Modify: `MatchInstance.cs` — add handshake state
- Modify: `NetworkClient.cs` — add connection sequence

**Verify:** Client connects → receives entity ID + initial state before first Tick.

### T6.2: Packet loss resilience

**What:** Input state includes a sequence number. Server acknowledges received ticks. Client resends unacknowledged inputs.

**Files:**
- Modify: `InputState.cs` — add `SequenceNumber`
- Modify: `NetworkClient.cs` — store unacknowledged packets, resend on timeout
- Modify: `MatchInstance.cs` — send ACK per tick

**Verify:** Kill server for 1 second → client reconnects and resumes.

### T6.3: Disconnect + rejoin

**What:** Client disconnects → shows "Connection Lost" screen → retry. Server holds state for 30s.

**Files:**
- Modify: `PvPMatch.cs` — detect `IsServerConnected == false`, show overlay
- Modify: `MatchInstance.cs` — 30s grace period before cleanup

**Verify:** Kill server process → client shows connection lost → restart server → client reconnects.

---

## Dependency Graph

```
Phase 1 (PvP Bridge) ─── Phase 2 (Prediction) ─── Phase 6 (Hardening)
       │                        │
       ├── Phase 3 (Polish)     │
       │                        │
Phase 4 (UI Flow) ─────────────┤
       │                        │
Phase 5 (FightGuy) ───────────────┘
```

Phases 1 → 2 → 6 are sequential (each builds on the last).
Phase 3 and Phase 4 are independent of each other, both need Phase 1.
Phase 5 needs Phase 1 + 3, independent of 2/4/6.

## Testing Strategy

- **Phase 1-2:** Run server + two Unity editor instances. Verify PvP state sync in console/Gizmos.
- **Phase 3:** Visual inspection + screen recording.
- **Phase 4:** Click through the full flow. Verify scene transitions.
- **Phase 5:** Select FightGuy in training. Verify all abilities work.
- **Phase 6:** Kill/restart server mid-match. Verify recovery.
- **Continuous:** `dotnet test tests/Shared.Tests/` after every shared code change (215+ tests, ~3s).

## Key Design Decisions

1. **No AnimatorController** — PlayerRenderer uses **Animancer** (third-party) for frame-by-frame clip control. Animation is driven by state, not triggers. All migration docs referencing "Unity Animator triggers" are stale.
2. **Local sim for prediction** — PvP mode runs a local `ServerSimulation` identical to training mode. The bridge only replaces where the authoritative state comes from (local vs remote).
3. **Server is the source of truth** — Always. Client prediction is speculative and gets corrected. No client-side "fixes" for gameplay.
4. **No physics on client** — Jitter2 collision runs server-side only. Client snaps position from server state (with lerp smoothing during knockback).
5. **Scene-based, not addressable** — Simple `SceneManager.LoadScene` for now. Addressables if the build grows.
