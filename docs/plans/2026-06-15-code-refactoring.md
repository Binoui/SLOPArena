> ⚠️ **DEPRECATED / SUPERSEDED** — Contents are stale. Godot-era plan; the codebase is Unity. See `docs/architecture-overview.md`.

# Code Refactoring Plan — SLOPArena Scripts/

> Goal: split overgrown files into focused, single-responsibility classes.
> Principle: each file does one thing. No file over 300 lines.

---

## 1. PlayerController.cs (1301 lines → 4 files)

**Current:** Does model loading, input building, FSM driving, combat setup,
respawn logic, debug hooks.

**Split into:**

### 1a. `Scripts/Entities/PlayerModel.cs` (~120 lines)
Loads the GLB, finds skeleton, applies skins, sets up AnimationTree.
- `LoadPlayerModel(CharacterClass)` — current lines ~1060-1160
- `ApplySkinRecursive()`, `FindSkeleton()`, `FindAnimationPlayer()`
- `ComputeModelYOffset()`
- `FixAnimationTracks()` — move from AnimationController
- **Shared by PlayerController AND DummyManager** (kill the copy-paste)

### 1b. `Scripts/Entities/PlayerInput.cs` (~100 lines)
Builds `InputState` from Godot input.
- `BuildInputState()` — current lines ~751-856
- `GetCurrentInput()` — wrapper for NPC vs player
- `InjectAIInput()` (for NPCs)
- `_pendingSlotPress` logic

### 1c. `Scripts/Entities/PlayerFSM.cs` (~150 lines)
Drives the animation state machine from sim state.
- `_PhysicsProcess` FSM logic — current lines ~639-730
- `ExecuteSlot()`, `ExecuteAttackStage()` — ability triggering
- State transition glue between sim and animation

### 1d. `Scripts/Entities/PlayerController.cs` (~250 lines) — what's left
- `_Ready()` — sets up PlayerModel, PlayerInput, PlayerFSM
- `ApplyServerState()`, `SetClass()`, `SetNPC()`
- `TakeDamage()`, respawn, model emission
- Public API: `GetDamagePercent()`, `IsAlive()`, events

**Verification:** Sandbox F5 — Manki moves, attacks, combos work identically.

---

## 2. DummyManager.cs — Delete, replace with shared PlayerModel

**Current:** 542 lines, 60% copy-paste of PlayerController's model loading.
Creates standalone physics dummies outside the server sim.

**After PlayerModel split:** DummyManager becomes ~150 lines.
- Uses `PlayerModel.Load()` instead of copy-paste
- Creates dummies as `PlayerController` nodes with `SetNPC(true)` + `SetClass()`
- Removes standalone physics (dummies use server sim now)

**Verification:** Training dummy appears, takes hits, respawns.

---

## 3. MatchManager.cs → TrainingMatch.cs + PvPMatch.cs

**Current:** 533 lines. Hybrid mode — NPCs AND opponent live in same class.
Branches on `_isNPC`, `_isPlayerControlled` everywhere.

### 3a. `Scripts/World/TrainingMatch.cs` (~200 lines)
Pure sandbox — player + N dummies, no network.
- Local `ServerSimulation`, no `NetworkClient`
- `_PhysicsProcess`: build input → local sim tick → apply to player + dummies
- `_Process`: nothing (no server to reconcile with)
- No rollback, no opponent, no `_serverConfirmedStates`

### 3b. `Scripts/World/PvPMatch.cs` (~200 lines)
Online match — player + opponent, network client.
- `NetworkClient`, `_playerEntityId = 1`, `OpponentEntityId = 2`
- `_PhysicsProcess`: build input → send to server → local sim predict
- `_Process`: receive server state → reconcile rollback
- No NPCs

### 3c. Shared helpers (stay in `MatchManager.cs`, renamed or extracted)
- `CreateTargetRing()` → `Scripts/World/TargetRing.cs`
- `SpawnPointExtensions.ToGodot()` → keep

**Verification:** Sandbox mode unchanged. PvP mode toggled via `Main.cs`.

---

## 4. CombatComponent.cs → split statuses from hitboxes

**Current:** 539 lines. Status effect tracking mixed with hitbox spawning.

### 4a. `Scripts/Combat/StatusComponent.cs` (~180 lines)
Status effect storage + events.
- `_statuses` dictionary, `ApplyStatus()`, `HasStatus()`, `ConsumeStatus()`
- `OnStatusApplied`, `OnStatusConsumed`, `OnStatusExpired` events
- Status timer tick in `_Process`

### 4b. `Scripts/Combat/CombatComponent.cs` (~200 lines) — what's left
- Hitbox spawning: `CheckMeleeCone()`, `CheckCircleHit()`
- `BuildEntityList()`, damage routing
- `TakeDamage()`, damage calc with status modifiers

**Verification:** Burning/shield statuses still apply and expire. Attacks still register.

---

## 5. Main.cs → extract UI factory

**Current:** 262 lines. `_Ready()` builds the entire UI inline.

### `Scripts/World/GameUI.cs` (~200 lines)
All UI creation extracted from `Main._Ready()`.
- `BuildHUD()` → ActionBarHUD, UnitFrames, RespawnTimer, crosshair
- `BuildMenus()` → EscapeMenu, Settings, ClassSelect
- `SetupInputActions()`
- Takes `MatchManager` as parameter

`Main.cs` becomes ~80 lines: class select → start match → delegate UI to `GameUI`.

**Verification:** All UI elements appear, settings menu works, escape menu works.

---

## Execution Order

| Step | Files | Risk | Depends on |
|------|-------|------|------------|
| **1.** PlayerModel extraction | 2 new, 1 modified | Medium | — |
| **2.** PlayerInput extraction | 1 new, 1 modified | Low | Step 1 |
| **3.** PlayerFSM extraction | 1 new, 1 modified | Medium | Step 1 |
| **4.** DummyManager → use PlayerModel | 1 modified | Low | Step 1 |
| **5.** TrainingMatch + PvPMatch split | 2 new, 1 modified | High | All above |
| **6.** StatusComponent split | 1 new, 1 modified | Low | — |
| **7.** GameUI extraction | 1 new, 1 modified | Low | Step 5 |
| **8.** Delete dead leftovers | deletions | Low | All above |

**Total:** 8 steps, ~10 new files, ~5 modified files. Each step is verifiable via F5 sandbox.

**After refactor:**
- No file over 300 lines
- No copy-pasted model loading
- Clear sandbox vs PvP separation
- Statuses in their own module (ready for server authority)

---

## Verification After Each Step

```bash
dotnet build --nologo    # 0 errors
# F5 in Godot:
# - Select character → training mode appears
# - Movement, attacks, combos work
# - Dummy takes damage
# - UI intact (action bar, unit frames, escape menu)
```
