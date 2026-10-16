> ⚠️ **DEPRECATED** — Contents are stale. This doc predates the Unity migration and describes
> a Godot-era PvP plan that no longer reflects the codebase. The server is vastly more
> advanced (MatchInstance, MultiMatchOrchestrator, GameServerRegistration, Jitter2 collision)
> and client progress is beyond several phases here.
> See `docs/plans/2026-07-03-pvp-roadmap.md` for the current, audited roadmap.

# SLOPArena — Online PvP Implementation Roadmap

> **Goal:** Build the full online PvP experience with 2 characters (Manki + FightGuy).
> **Prerequisite:** Complete before starting new character design work.
> **Approach:** Incremental phases, each producing a verifiable result. Agent-friendly — each task is bite-sized.

---

## Phase 0 — Cleanup (1 session, ~30 min)

**Goal:** Remove dead weight, fix obvious bugs, make the codebase navigable.

### T0.1: Delete dead code files
**Files to remove:**
- `Scripts/Combat/LocalSimulation.cs` (+ .uid) — replaced by LocalServerBridge
- `Scripts/World/DamageNumberManager.cs` (+ .uid) — never called
- `Scripts/Entities/BotController.cs` (+ .uid) — never instantiated
- `Scripts/Combat/ProjectileHelpers.cs` (+ .uid) — never called, uses Godot physics
- `Shared/MovementProfiles.cs` (+ .uid) — dead leftover
- `Shared/StatusType.cs` (+ .uid) — never used
- `Scripts/Combat/Hurtbox.cs` (+ .uid) — OnHit never subscribed
- `assets/characters/bunny/rabbit.tscn` — abandoned 3-line scene

**Verify:** `dotnet build --nologo` succeeds with 0 errors.

### T0.2: Delete orphaned .uid files
**Files to remove:**
- `Scripts/Combat/ProjectileManager.cs.uid`
- `Scripts/Combat/Projectile.cs.uid`
- `Scripts/Combat/Fireball.cs.uid`
- `Scripts/Entities/ClassAbilities.cs.uid`

**Verify:** `find Scripts -name "*.uid" | while read uid; do cs="${uid%.uid}.cs"; [ ! -f "$cs" ] && echo "STILL ORPHAN: $uid"; done` returns nothing.

### T0.3: Delete unused assets
**Remove 86 unused keyboard key PNGs** (keep only: `mouse_left.png`, `mouse_right.png`, `keyboard_q.png`, `keyboard_e.png`, `keyboard_r.png`, `keyboard_f.png` + their `.import` files).

**Remove 90 orphaned `.import` files** in `assets/textures/kenney_prototype/` that reference non-existent PNGs/SVGs.

**Verify:** `du -sh assets/ui/keys assets/textures/kenney_prototype` — should drop from 736K/400K to ~50K/30K.

### T0.4: Fix Makefile report path
**File:** `Makefile:61`
**Change:** `./scripts/quality-report.sh` → `./ci/quality-report.sh`

**Verify:** `make report` doesn't fail with "file not found".

### T0.5: Fix outdated docs
**File:** `docs/characters/manki.md`
- Replace `anim_monkey.glb` → `manki.glb`

**File:** `docs/conventions.md`
- Update Manki kit reference from "Fire Dancer" → "Mad Bomber"

**Verify:** `grep -r "anim_monkey" docs/` returns nothing.

---

## Phase 1 — Critical Bug Fixes (1-2 sessions, ~2h)

**Goal:** Fix all bugs that block multiplayer from working at all.

### T1.1: Fix Y/Z axis swap in MatchInstance.StateToPacket
**Root cause:** `MatchInstance.StateToPacket()` manually constructs a `CharacterStatePacket` with Y↔Z swap. `CharacterStatePacket.FromState()` already does the correct mapping AND includes all fields.

**File:** `Server/MatchInstance.cs`
**Change:** Delete `StateToPacket()` method entirely. Replace calls with `CharacterStatePacket.FromState(state, _serverTick)`.

Lines 269-274 become:
```csharp
var p1Packet = CharacterStatePacket.FromState(_p1State, _serverTick);
var p2Packet = CharacterStatePacket.FromState(_p2State, _serverTick);
```

This also fixes the missing fields bug — `IsGrounded`, `AttackSlot`, `ComboStage`, `FacingYaw` are now broadcast.

**Verify:** Build succeeds. Read the code — `FromState()` maps `PY→PositionY`, `PZ→PositionZ` (correct).

### T1.2: Make SpellResolver instance-based (fix thread safety)
**Root cause:** `SpellResolver` is a `static class` with `static List<Hitbox>`. Multiple matches share state.

**Files:**
- `Shared/SpellResolver.cs` — convert to instance class, remove `static` from class and `_hitboxes`
- `Shared/ServerSimulation.cs` — create `SpellResolver _resolver = new()` field, use `_resolver.Spawn()`, `_resolver.Tick()`
- All call sites that use `SpellResolver.Spawn()` or `SpellResolver.Tick()` — update to use instance

**Change in SpellResolver.cs:**
```csharp
// Before:
public static class SpellResolver
{
    private static readonly List<Hitbox> _hitboxes = new();
    public static void Spawn(Hitbox hb) { ... }
    public static List<HitResult> Tick(List<EntityData> entities) { ... }
}

// After:
public class SpellResolver
{
    private readonly List<Hitbox> _hitboxes = new();
    public void Spawn(Hitbox hb) { ... }
    public List<HitResult> Tick(List<EntityData> entities) { ... }
}
```

**Call sites to update:**
- `ServerSimulation.cs:310` — `SpellResolver.Spawn(hb)` → `_resolver.Spawn(hb)`
- `ServerSimulation.cs:336` — `SpellResolver.Tick(entityList)` → `_resolver.Tick(entityList)`
- `Scripts/Combat/CombatComponent.cs` — `SpellResolver.Spawn(hb)` → create local instance
- `Scripts/World/MatchManager.cs:371` — `SpellResolver.GetActiveHitboxes()` → use `_localSim`'s resolver

**Verify:** `dotnet build --nologo` — 0 errors. Search for `SpellResolver.` (static calls) — should only find instance-based usage.

### T1.3: Uncomment AddChild in StatusSpells
**Root cause:** `StatusSpells.AddToScene()` has `AddChild(node)` commented out, making all spell VFX invisible.

**File:** `Scripts/Spells/StatusSpells.cs:177`
**Change:** Uncomment `current.AddChild(node)`.

**Verify:** Build. Run sandbox — Manki's flamethrower/aerosol/bomb effects should be visible.

### T1.4: Fix float timers → ushort ticks in client states
**Root cause:** Multiple state files use `float _timer -= delta` instead of `ushort` ticks. At 120fps, landing/hitstun lasts half as long.

**Files to fix:**
- `Scripts/Animation/States/LandingState.cs` — `_timer` → `ushort _landingTicks`
- `Scripts/Animation/States/HitReactionState.cs` — `_flashTimer` → `ushort _flashTicks`

**Pattern:**
```csharp
// Before:
private float _timer;
public override void OnProcess(double delta) { _timer -= (float)delta; if (_timer <= 0) ... }

// After:
private ushort _ticks;
public override void OnPhysicsProcess(double delta) { if (--_ticks == 0) ... }
```
Move decrement from `_Process` (variable-rate) to `_PhysicsProcess` (fixed 60Hz).

**Verify:** Build. Test in sandbox — landing and hitstun durations feel consistent regardless of monitor refresh rate.

### T1.5: Unify server — integrate ServerSimulation into MatchInstance
**Root cause:** `MatchInstance` calls `Simulation.SimulateTick()` directly, skipping hit detection, hurtboxes, void death. `ServerApp` has this working but no multiplayer.

**File:** `Server/MatchInstance.cs`

**Changes:**
1. Add `private ServerSimulation _sim;` field
2. Add `private SpellResolver _spellResolver = new();` field (post-T1.2)
3. In `Run()`, after creating `_p1Def`/`_p2Def`:
   ```csharp
   _sim = new ServerSimulation(_arena);
   _sim.RegisterEntity(1, _p1Def, _p1State, bakedData1);
   _sim.RegisterEntity(2, _p2Def, _p2State, bakedData2);
   ```
4. In `Tick()`, replace the two `Simulation.SimulateTick()` calls with:
   ```csharp
   _sim.Tick(new Dictionary<ulong, InputState> {
       { 1, p1input }, { 2, p2input }
   });
   ```
5. After `_sim.Tick()`, read back states: `_p1State = _sim.GetState(1)`, `_p2State = _sim.GetState(2)`
6. Load baked skeleton data for both characters (reuse pattern from `ServerApp/Program.cs:29-45`)

**Verify:** `dotnet build --nologo`. Manual test: connect two clients, attacks should produce hitboxes and damage on the server (check server console for hit logs).

### T1.6: Send both player states to both players
**Root cause:** `SendState()` sends P1→P1 only, P2→P2 only. Clients need both to render opponents.

**File:** `Server/MatchInstance.cs`, `SendState()` method

**Change:** Instead of one `CharacterStatePacket` per player, send a multi-entity packet or send both states to each player:
```csharp
// Send both states to player 1
var p1Buf = new byte[CharacterStatePacket.Size * 2 + 4]; // 4 = entity count
// ... serialize entity count (2) + p1State + p2State ...
_udpServer.Send(p1Buf, p1Buf.Length, _player1EndPoint);

// Same for player 2
_udpServer.Send(p2Buf, p2Buf.Length, _player2EndPoint);
```

Alternative (simpler): just send each player's packet to the other endpoint as well:
```csharp
if (_player1EndPoint != null)
{
    _udpServer.Send(p1Buffer.ToArray(), CharacterStatePacket.Size, _player1EndPoint);
    _udpServer.Send(p2Buffer.ToArray(), CharacterStatePacket.Size, _player1EndPoint); // add this
}
if (_player2EndPoint != null)
{
    _udpServer.Send(p1Buffer.ToArray(), CharacterStatePacket.Size, _player2EndPoint); // add this
    _udpServer.Send(p2Buffer.ToArray(), CharacterStatePacket.Size, _player2EndPoint);
}
```

**Verify:** Client receives states for both entity IDs. Opponent character moves on screen.

---

## Phase 2 — Netcode Reliability (2-3 sessions, ~3h)

**Goal:** Make the UDP connection robust enough for real gameplay.

### T2.1: Add server tick ACK
**Root cause:** Client doesn't know which tick the server processed. Can't reconcile prediction.

**File:** `Server/MatchInstance.cs`

**Change:** After processing inputs in `Tick()`, set `packet.TickNumber` to `_serverTick` before calling `FromState()`:
```csharp
var p1Packet = CharacterStatePacket.FromState(_p1State, _serverTick);
```
(This already works after T1.1 — `FromState(state, tick)` is already called with tick.)

**Client side:** `NetworkClient.ReceiveStates()` already receives ticks. Verify the existing reconciliation code in `MatchManager._Process()` uses server tick correctly.

**Verify:** Server sends tick number. Client logs show `serverTick` incrementing.

### T2.2: Add connection timeout
**Root cause:** If a player disconnects, match runs forever with zero input.

**File:** `Server/MatchInstance.cs`

**Change:** Track last packet receive time per player:
```csharp
private DateTime _lastP1Packet = DateTime.UtcNow;
private DateTime _lastP2Packet = DateTime.UtcNow;
```
In `ReceiveInputs()`, update timestamp on each received packet.
In `Tick()`, if `(DateTime.UtcNow - _lastP1Packet).TotalSeconds > 5`, mark P1 disconnected and end match.
When a player times out, call `Stop()` to clean up.

**Verify:** Kill a client process during match. After 5 seconds, server stops the match and frees the port.

### T2.3: Add match lifecycle (ready → countdown → play → end)
**Root cause:** No formal match start/end. Players connect and immediately start.

**Files:** `Server/MatchInstance.cs`, `Shared/CharacterStatePacket.cs` (add match state byte)

**Changes:**
1. Add `MatchState` enum: `Waiting, Countdown, Playing, Ended`
2. When second player connects, enter `Countdown` for 180 ticks (3 seconds)
3. After countdown, set `Playing`
4. When a player dies (void death), increment their death counter. First to 3 deaths loses.
5. On match end, send final state, wait 5 seconds, close.

**Verify:** Connect two clients. Server logs show: "Match starting in 3...", "GO!", then kill tracking, then "Match ended".

### T2.4: Add reconnection support
**Root cause:** If a client drops and reconnects, they have no state.

**File:** `Server/MatchInstance.cs`

**Change:** When a player sends a packet after being disconnected:
1. Server sends a "full state" snapshot (both players' complete CharacterState)
2. Client resets prediction to server state
3. Match resumes

This requires the server to remember disconnected players for ~30 seconds before fully removing them.

**Verify:** Disconnect a client, reconnect within 30 seconds — match resumes from correct state.

### T2.5: Add packet sequence validation
**Root cause:** No duplicate prevention beyond tick filtering. Stale packets can be processed.

**File:** `Server/MatchInstance.cs`

**Change:** Track last processed tick per player. Only process packets with `tick > lastProcessedTick`. Already partially implemented — enhance it to also reject packets more than 60 ticks old (1 second stale).

**Verify:** Spam the server with old packets — they are discarded. Only fresh inputs processed.

---

## Phase 3 — Client Polish (1-2 sessions, ~2h)

**Goal:** Make the client-side experience smooth and cheat-resistant.

### T3.1: Fix rollback to check X/Z axis
**Root cause:** Rollback only triggers on `dy > 0.5f`. Horizontal desyncs go undetected.

**File:** `Scripts/World/MatchManager.cs:252-253`

**Change:**
```csharp
// Before:
float dy = predicted.PY - serverState.PY;
if (MathF.Abs(dy) > 0.5f)

// After:
float dx = predicted.PX - serverState.PX;
float dy = predicted.PY - serverState.PY;
float dz = predicted.PZ - serverState.PZ;
float distSq = dx * dx + dy * dy + dz * dz;
if (distSq > 0.25f) // 0.5m threshold
```

**Verify:** Move character rapidly — if server disagrees on position, rollback triggers for any axis.

### T3.2: Move status effects to Shared/ server authority
**Root cause:** Status effects are entirely client-side in `CombatComponent.cs` with float timers. Cheat-able.

**Files:**
- `Shared/CharacterState.cs` — add status fields: `StatusFlags` (bitfield), `StatusTicks` per status
- `Shared/Simulation.cs` — add status tick decrement in `SimulateTick()`
- `Shared/SpellResolver.cs` — apply statuses on hit (e.g., burn from fire damage)
- `Scripts/Combat/CombatComponent.cs` — read statuses from `CharacterState`, remove local tracking

**Status bitfield:**
```csharp
[Flags]
public enum StatusFlags : byte
{
    None = 0,
    Burning = 1 << 0,
    Slowed = 1 << 1,
    Silenced = 1 << 2,
    Shielded = 1 << 3,
    Rooted = 1 << 4,
}
```

**Verify:** Status effects persist across client restarts (server-authoritative). Hacked client can't grant itself shield.

### T3.3: Add AttackWarping server reconciliation

> **✅ RESOLVED (2026-06-22):** This task was completed in the ServerAbility refactor. `AttackWarping.cs` was deleted and warp movement is now server-authoritative via `Simulation.ProcessWarp()`. See `docs/superpowers/plans/2026-06-22-server-ability-refactor.md` for implementation details.

**Root cause:** `AttackWarping.cs` moves player via `body.MoveAndSlide()` with no server awareness. Rubberbanding on state sync.

**File:** `Scripts/Combat/AttackWarping.cs`

**Change:** Instead of moving the body directly, apply warp velocity as input to `Simulation.SimulateTick()`:
1. During warp, set a `WarpVelocity` on the `CharacterState`
2. `Simulation.SimulateTick()` applies warp velocity before normal movement
3. Server receives same warp input and simulates identically

Or (simpler): make warp purely cosmetic — just play the animation at the body's position, don't move the character. The LMB combo movement is already done by `MoveTowardTarget()` during attacks.

**Verify:** Warp attacks don't cause position corrections from server.

### T3.4: Increase input buffer size
**Root cause:** `RollbackFrames = 10` (167ms). Any RTT > 80ms causes buffer overrun.

**File:** `Scripts/World/MatchManager.cs:57`

**Change:** `RollbackFrames = 30` (500ms). Adaptive sizing can come later.

**Verify:** Test with simulated 200ms latency — buffer doesn't overrun during normal play.

### T3.5: Add PlayerController to opponent rendering
**Root cause:** `MatchManager` only spawns NPCs visually. No visual representation of the remote PvP opponent.

**File:** `Scripts/World/MatchManager.cs`

**Change:** In `StartMatch()`, spawn a second `PlayerController` for the remote player:
```csharp
_opponent = new PlayerController { Name = "Opponent" };
_opponent.SetClass(opponentClass);
AddChild(_opponent);
```
In `_Process()`, apply opponent's server state:
```csharp
if (serverStates.TryGetValue(opponentEntityId, out var oppServer))
    _opponent.ApplyServerState(oppServer.state);
```

**Verify:** Two characters visible on screen. Both move independently when their respective player presses keys.

---

## Phase 4 — Production Hardening (1-2 sessions, ~2h)

**Goal:** Security, logging, and deployability.

### T4.1: Add player authentication
**Root cause:** Players identified by IP:port — spoofable.

**File:** `Server/MatchInstance.cs`

**Change:** Add a `uint JoinToken` to the first handshake. Server generates a random token per player slot. Client must include the token in every packet. Reject packets with wrong token.

Simple scheme: first packet from a new IP gets a `sessionToken` echoed back. Subsequent packets must include it.

**Verify:** Spoofed packets from different port are rejected. Same-port replay attacks still possible (UDP limitation), but better than nothing.

### T4.2: Add structured server logging
**Root cause:** Server uses `Console.WriteLine` with ad-hoc formatting.

**File:** `Server/MatchInstance.cs`

**Change:** Add a simple log wrapper:
```csharp
private void Log(string msg) => Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}][M:{_matchId}] {msg}");
```

Replace all `Console.WriteLine` calls with `Log()`.

Add log levels: hits (verbose), connections (info), errors (always).

**Verify:** Server output is timestamped, match-scoped, filterable.

### T4.3: Add arena data path to CLI args
**Root cause:** `MatchInstance` hardcodes `arena = ArenaRegistry.Get("pit")` in `InitializePlayerState`.

**File:** `Server/MatchInstance.cs`

**Change:** Pass arena name through constructor → `Run()` uses `_arenaName` instead of hardcoded "pit".

**Verify:** `dotnet SlopArena.Server.dll --arena split` spawns players in the split arena.

### T4.4: Add basic metrics
**Goal:** Know if the game is running correctly.

**File:** `Server/MatchInstance.cs`

**Add:** Per-tick counters:
- `_ticksSimulated` — total ticks processed
- `_packetsReceived` — total input packets
- `_packetsDropped` — stale/duplicate packets
- `_hitEvents` — total hits resolved

Print summary every 600 ticks (10 seconds):
```
[Match:abc123] Stats: 600 ticks | 1187 pkts (12 dup, 3 stale) | 47 hits | 2 deaths
```

**Verify:** Running match shows periodic stat dumps.

---

## Execution Plan Summary

| Phase | Tasks | Est. Time | Output |
|-------|-------|-----------|--------|
| **0 — Cleanup** | 5 | 30 min | Clean codebase, smaller assets |
| **1 — Critical Fixes** | 6 | 2h | Working hit detection, state sync, thread-safe |
| **2 — Netcode** | 5 | 3h | Reliable UDP, match lifecycle, reconnection |
| **3 — Client Polish** | 5 | 2h | Smooth prediction, server-auth status, opponent rendering |
| **4 — Production** | 4 | 2h | Auth, logging, CLI args, metrics |

**Total:** ~10 hours, 25 tasks.

**First working PvP demo:** After Phase 1 (T1.1-T1.6). Two clients can connect, move, attack, and see hits register.

**Production-ready PvP:** After Phase 2. Matches have proper lifecycle, timeout, reconnection.

**Shippable MVP:** After Phase 3-4. Cheat-resistant, polished, monitorable.

---

## Principle: Execute Incrementally

- Each task produces a verifiable result.
- Each task is a single commit.
- Run `dotnet build --nologo` after EVERY change.
- Test manually after each phase, not just at the end.
- Phases 0-1 can be done in any order within the phase.
- Phases 2-4 are sequential — each builds on the previous.

> ⚠️ **DEPRECATED** — See `docs/plans/2026-07-03-pvp-roadmap.md` for the current roadmap.
