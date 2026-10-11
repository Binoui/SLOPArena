> ⚠️ **DEPRECATED / SUPERSEDED** — Contents are historical. Its `Scripts/Combat/TargetLockSystem.cs` does not exist, and the dormant automatic warp and per-stage tracking runtime was deleted in the 2026-10-11 simulation cleanup. Current target lock remains in `ServerSimulation.ProcessTargetLock`. See [Combat Systems](combat-systems.md) and [Ability Architecture](ability-architecture.md).

# Range-Based Combat System

Complete implementation of Range-based combat (Range-Based) inspired combat mechanics for SlopArena.

## 🎯 Overview

This system implements three core Range-Based mechanics:
1. **Soft Lock** - Automatic target tracking (no button press)
2. **Attack Range** - Distance where attacks hit immediately
3. **Warp Range** - Distance where auto-dash triggers before attack

## 📁 Architecture

```
Scripts/Combat/
├── TargetLockSystem.cs      — Soft lock (auto-track nearest enemy)
├── CombatComponent.cs        — Range checks + warp execution
└── Hurtbox.cs

Shared/
├── Simulation.cs             — ProcessWarp() for server-authoritative warping
├── Abilities/                — ServerAbility implementations control warp movement
└── AttackData.cs             — Attack definitions with Range-Based ranges
```

---

## 🔴 Soft Lock System

**File**: `Scripts/Combat/TargetLockSystem.cs`

Continuously tracks the nearest enemy in camera view without button press.

### Features
- **Automatic tracking**: Updates every 100ms
- **Cone-based**: 45° half-angle (90° total FOV)
- **Range-limited**: 20m max distance
- **Sticky targeting**: Hysteresis prevents target flickering
- **Visual feedback**: Red ring indicator under target

### Configuration
```csharp
[Export] public float LockRange = 20f;           // Max lock distance
[Export] public float LockAngle = 45f;           // Half-angle cone
[Export] public float UpdateInterval = 0.1f;     // Update frequency
[Export] public float StickyMultiplier = 1.2f;   // Hysteresis threshold
```

### Usage
```csharp
// In PlayerController._Ready()
_targetLock = new TargetLockSystem
{
    Camera = _camera.GetCamera(),
    LockRange = 20f,
    LockAngle = 45f,
};
AddChild(_targetLock);

// Query target
float dist = _targetLock.GetDistanceToTarget();
Vector3 dir = _targetLock.GetDirectionToTarget();
```

---

## ⚡ Attack Warping

**File**: `Shared/Simulation.cs` (ProcessWarp function)

Server-authoritative auto-dash toward target when in warp range but outside attack range.

### Behavior
```
Player presses attack button:
├─ Zone 3: distance ≤ AttackRange (2m)
│  ├─ ✅ Rotate toward target
│  ├─ ❌ Warp (already in range)
│  └─ ✅ Lunge toward target
│
├─ Zone 2: AttackRange < distance ≤ WarpRange (2-10m)
│  ├─ ✅ Rotate toward target
│  ├─ ✅ Warp (auto-dash to AttackRange)
│  └─ ✅ Lunge toward target (after warp)
│
└─ Zone 1: distance > WarpRange (10m+)
   ├─ ❌ No rotation (no snap-turn)
   ├─ ❌ No warp (too far)
   └─ ✅ Lunge in current facing direction
```

### Features
- **Server-authoritative**: Warp movement runs in `Simulation.SimulateTick()`
- **Tick-based interpolation**: `WarpSpeed` controls interpolation factor per tick
- **Prediction-friendly**: Client simulates same warp logic for smooth rendering
### Warp Formula

Warp uses **exponential convergence**: each tick closes `WarpSpeed` fraction of remaining distance.

```csharp
// Velocity = dx * WarpSpeed / TickDt
// After each tick: distance_remaining *= (1 - WarpSpeed)
s.VX = dx * s.WarpSpeed / TickDt;
s.VZ = dz * s.WarpSpeed / TickDt;
```

At `WarpSpeed = 0.3`:
- Tick 1: closes 30% of remaining distance
- Tick 2: closes 30% of what remains
- Arrival in ~3 ticks for a 7m gap (within AttackRange=4)

After warp completes, the ability's lunge phase applies naturally (guarded by `WarpSpeed <= 0f`).

### Usage

Warp parameters are set by `ServerSimulation.ProcessTargetLock()`, not by `OnStart`:

```csharp
// ProcessTargetLock sets these when target within WarpRange:
// s.WarpSpeed = 0.3f;
// s.WarpTargetX = target.PX;
// s.WarpTargetZ = target.PZ;
```

### Features
- **Server-authoritative**: Warp movement runs in `Simulation.SimulateTick()`
- **Tick-based interpolation**: `WarpSpeed` controls exponential convergence factor per tick
- **Prediction-friendly**: Client simulates same warp logic for smooth rendering
- **Cancellable**: Cleared on hitstun or death

---

## 🎮 Attack Stage Configuration

**File**: `Shared/AttackData.cs`

### Current Fields

Damage, knockback, and stun now live in `HitboxEvent[]` (not `AttackStage` directly).
Timing uses `DurationTicks` for total animation lock.

```csharp
public struct AttackStage
{
    public ushort DurationTicks;          // Total animation lock duration in ticks
    public HitboxEvent[] HitboxEvents;    // Damage/hitbox events triggered during this stage
    public float LungeForce;              // Forward burst during attack
    public ushort ChainWindowTicks;       // Ticks to buffer next input (0 = final/no chain)

    // Range-Based-style range system
    public float AttackRange;       // Distance where attack hits immediately (e.g., 4m)
    public float WarpRange;         // Distance where auto-dash triggers (e.g., 12m)

    // WarpSpeed now driven by character Movement.SprintSpeed (not per-stage)
    public bool UseTargetLock;      // true = use soft lock for this attack
    public bool RotateTowardTarget; // true = auto-rotate during attack
    public float TrackingStrength;  // 0-1: rotation lerp strength toward target
}
```

`HitboxEvent` contains the old AttackStage damage fields:

```csharp
public struct HitboxEvent
{
    public ushort TriggerTick;       // Tick from attack start when hitbox spawns
    public ushort DurationTicks;     // How many frames the hitbox stays active
    public float Radius;
    public float OffX, OffY, OffZ;   // Offset from attacker center
    public float Damage;
    public float BaseKnockback;
    public float KnockbackGrowth;
    public float KnockbackUpward;
    public ushort StunTicks;
    public bool Interruptible;       // false = persists even if attacker is hit
}
```

### Example Definitions

#### Light Attack (fast, long warp)
```csharp
new AttackStage
{
    DurationTicks = 48,        // Total animation lock
    LungeForce = 5f,
    ChainWindowTicks = 8,      // 8 ticks to buffer next input

    // Hitbox event (damage data lives here, not on AttackStage)
    HitboxEvents = new[]
    {
        new HitboxEvent
        {
            TriggerTick = 8, DurationTicks = 2, Radius = 0.5f,
            OffX = 1.5f, OffY = 1.0f, OffZ = 0f,
            Damage = 12f, BaseKnockback = 10f, KnockbackGrowth = 15f,
            KnockbackUpward = 5f, StunTicks = 30,
            Interruptible = true,
        },
    },

    // Range-Based ranges
    AttackRange = 4f,      // Hit within 4m
    WarpRange = 10f,       // Warp if 4-10m away
    UseTargetLock = true,
    RotateTowardTarget = true,
    TrackingStrength = 0.9f,  // Strong tracking
}
```

#### Heavy Attack (slow, shorter warp)
```csharp
new AttackStage
{
    DurationTicks = 80,        // Slower attack
    LungeForce = 8f,
    ChainWindowTicks = 0,      // No chain (heavy attack is standalone)

    HitboxEvents = new[]
    {
        new HitboxEvent
        {
            TriggerTick = 20, DurationTicks = 3, Radius = 0.8f,
            OffX = 2.5f, OffY = 1.0f, OffZ = 0f,
            Damage = 25f, BaseKnockback = 20f, KnockbackGrowth = 30f,
            KnockbackUpward = 10f, StunTicks = 60,
            Interruptible = true,
        },
    },

    // Range-Based ranges
    AttackRange = 6f,      // Longer reach
    WarpRange = 15f,       // Longer warp range
    UseTargetLock = true,
    RotateTowardTarget = true,
    TrackingStrength = 0.5f,  // Less tracking (dodgeable)
}
```

#### No-Warp Attack (projectile/AoE)
```csharp
new AttackStage
{
    DurationTicks = 30,        // Quick cast
    LungeForce = 0f,
    ChainWindowTicks = 0,

    HitboxEvents = new[]
    {
        new HitboxEvent
        {
            TriggerTick = 5, DurationTicks = 1, Radius = 2f,
            OffX = 3f, OffY = 1.5f, OffZ = 0f,
            Damage = 8f, BaseKnockback = 2f, KnockbackGrowth = 3f,
            KnockbackUpward = 0f, StunTicks = 10,
            Interruptible = true,
        },
    },

    // No warp
    AttackRange = 8f,      // Range check only
    WarpRange = 0f,        // No warp
    UseTargetLock = false, // No target lock
}
```

---

## 🔧 Combat Component Integration

**File**: `Scripts/Combat/CombatComponent.cs`

### New Methods

#### ExecuteAttackWithWarp
```csharp
public void ExecuteAttackWithWarp(AttackStage stage, Action onAttackStart)
```
Main entry point for Range-Based-style attacks. Checks range and initiates warp if needed.

**Flow:**
1. Check if `UseTargetLock = true` and target exists
2. Measure distance to target
3. If `dist ≤ AttackRange`: Execute immediately
4. If `AttackRange < dist ≤ WarpRange`: Start warp → execute after
5. If `dist > WarpRange`: Execute in place (miss)

#### CancelAttackWarp
```csharp
public void CancelAttackWarp()
```
Cancel active warp (e.g., player gets hit during warp).

#### IsWarping
```csharp
public bool IsWarping()
```
Check if currently warping toward target.

---

## 🎯 Target Groups

Entities must be in the `"enemies"` group to be targetable.

### Setup
```csharp
// In DummyManager.cs (NPCs)
body.AddToGroup("enemies");

// In PlayerController.cs (for PvP)
AddToGroup("enemies");  // If PvP enabled
```

---

## 📊 Performance Notes

- **Soft lock update**: 10 Hz (every 0.1s) - negligible cost
- **Warp tracking**: Updates every physics frame (~60 Hz) only during active warp
- **Target search**: O(N) where N = enemies in scene (typically < 10)
- **No allocations**: Reuses existing node queries

---

## 🚀 Usage Example

### In CharacterDefinition.cs
```csharp
new AbilityData
{
    Name = "Monkey Combo",
    CooldownTicks = 0,
    Stages = new AttackStage[]
    {
        // Stage 1: Light punch
        new()
        {
            DurationTicks = 52,
            LungeForce = 4f,
            ChainWindowTicks = 8,

            HitboxEvents = new[]
            {
                new HitboxEvent
                {
                    TriggerTick = 6, DurationTicks = 2, Radius = 0.5f,
                    OffX = 1.5f, OffY = 1.0f, OffZ = 0f,
            Damage = 4f, BaseKnockback = 1.2f, KnockbackGrowth = 1.8f,
                    KnockbackUpward = 2f, StunTicks = 10,
                    Interruptible = true,
                },
            },

            AttackRange = 4f,     // Hit within 4m
            WarpRange = 10f,      // Warp if 4-10m away
            UseTargetLock = true,
            RotateTowardTarget = true,
            TrackingStrength = 0.9f,
        },
        // Stages 2-3: Similar with adjusted values
    },
    AnimationNames = new[] { "spell_lmb_1", "spell_lmb_2", "spell_lmb_3" },
}
```

### In PlayerController.cs (future - not yet wired)
```csharp
private void OnAttackInput(int abilitySlot)
{
    var ability = _charDef.Abilities[abilitySlot];
    var stage = ability.Stages[0];
    
    // Range-Based-style: check range + warp if needed
    _combatComponent.ExecuteAttackWithWarp(stage, () =>
    {
        // This callback fires after warp completes (or immediately if no warp)
        StartAttackAnimation(abilitySlot);
    });
}
```

---

## 🎨 Visual Feedback

### Current
- ✅ Red ring under soft-locked target
- ✅ Ring rotates slowly (visual polish)
- ✅ Ring hidden when no target

### Future (TODO)
- ⏳ Dash trail VFX during warp
- ⏳ Target reticle on HUD
- ⏳ Distance indicator (color-coded by range)
- ⏳ Warp startup animation

---

## 🐛 Debugging

### Console Output
Warp system prints debug messages:
```
[Attack] Target in range (3.2m <= 4.0m), attacking
[Attack] Target in warp range (7.5m), warping to 4.0m
[Warp] Starting warp: 3.5m at 25.0m/s
[Warp] Warp complete, executing attack
[Warp] Safety timeout, ending warp
```

### Common Issues

**Target not locking:**
- Check entity is in `"enemies"` group
- Verify camera is set in `TargetLockSystem.Camera`
- Check target within `LockRange` (20m default)
- Check target within `LockAngle` (45° default)

**Warp not triggering:**
- Verify `UseTargetLock = true` in `AttackStage`
- Check `WarpRange > AttackRange`
- Check character's `Movement.SprintSpeed > 0` (warp speed)
- Verify `CombatComponent.Setup()` received `TargetLockSystem`

**Warp overshooting:**
- Reduce character `Movement.SprintSpeed` (try 15-20 m/s equivalent)
- Increase `AttackRange` (stop sooner)

---

## ✅ Implementation Checklist

- [x] TargetLockSystem.cs (soft lock)
- [x] Server-side warp (Simulation.ProcessWarp)
- [x] AttackData.cs (Range-Based range fields)
- [x] CombatComponent.cs (range check + warp integration)
- [x] PlayerController.cs (target lock setup)
- [x] DummyManager.cs (add NPCs to "enemies" group)
- [x] CharacterDefinition.cs (add ranges to Manki attacks)
- [x] ServerAbility implementations (warp via IsWarping flag)
- [ ] Add rotation toward target during attack (TrackingStrength)
- [ ] Add warp VFX (dash trail)
- [ ] Add target reticle HUD element

---

## 🎮 Design Rationale

### Why Range-Based-style over traditional targeting?

**Problem**: 3D brawlers suffer from depth perception issues. Players miss attacks due to:
- Camera angle obscuring distance
- Fast-paced movement making manual aim difficult
- Frustration when attacks "pass through" enemies

**Solution**: Range-Based's three-tier system
1. **Soft lock**: Removes need for precise camera aiming
2. **Attack range**: Immediate hit within melee range (forgiving)
3. **Warp range**: Magnetic dash feels responsive (not cheap)

### Range Tuning Guidelines

**Light attacks** (jabs, quick hits):
- `AttackRange`: 3-5m (short reach)
- `WarpRange`: 8-12m (long dash for mobility)
- Warp speed: 25-30 m/s (fast, responsive; set via character `Movement.SprintSpeed`)
- `TrackingStrength`: 0.8-1.0 (strong tracking)

**Heavy attacks** (smashes, slams):
- `AttackRange`: 5-7m (longer reach)
- `WarpRange`: 10-15m (moderate dash)
- Warp speed: 15-20 m/s (slower, more telegraphed; set via character `Movement.SprintSpeed`)
- `TrackingStrength`: 0.4-0.6 (less tracking, dodgeable)

**Projectiles/AoE**:
- `AttackRange`: weapon-dependent
- `WarpRange`: 0 (no warp)
- `UseTargetLock`: false

---

## 🔮 Future Enhancements

### Phase 1 (Core - DONE)
- [x] Soft lock system
- [x] Attack warping
- [x] Range-based execution

### Phase 2 (Polish - TODO)
- [ ] Auto-rotation toward target during attack (`RotateTowardTarget`)
- [ ] Warp VFX (dash trail, speed lines)
- [ ] Target reticle HUD
- [ ] Distance-based color coding (green = in range, yellow = warp range, red = out of range)

### Phase 3 (Advanced - TODO)
- [ ] Multi-target soft lock (cycle with Tab)
- [ ] Soft lock priority (prefer low HP targets)
- [ ] Warp canceling via dodge/roll
- [ ] Aerial warp (dash in 3D space, not just horizontal)

---

## 📚 References

- **Range-based combat (Range-Based)**: Original inspiration for range-based combat
- **Smash Bros**: Hitbox/hurtbox system (see `HITBOX_SYSTEM.md`)
- **Naraka Bladepoint**: Similar soft lock + dash system
- **Dragon Ball FighterZ**: Auto-tracking during combos

---

**Questions?** Read this doc, test in-game with NPCs, watch console output during attacks.
