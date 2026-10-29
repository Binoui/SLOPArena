# Hitbox and hurtbox runtime

This page describes Shared collision and preserves explicitly labeled historical examples.
Use [ability architecture](ability-architecture.md), [combat systems](combat-systems.md),
and the [character guide](../characters/adding-a-new-character.md) for package authoring.

## Runtime ownership

Cooked timeline operations and admitted capabilities spawn hitboxes into the Shared
`SpellResolver`. The GameServer and client prediction run the same simulation.
Unity renders the resulting state and events; animation callbacks never decide hits.

A hitbox is a runtime sphere or capsule. Hurtboxes are per-tick target volumes resolved
from cooked pose tracks or authored fallback capsules. Shared tests collision with pure
math and uses ticks for timing. See [ability architecture](ability-architecture.md) for
current package authoring.

## Source layout

- [`Hitbox.cs`](../../src/Shared/Hitbox.cs) defines runtime geometry, ownership, hit history, and lifecycle flags.
- [`AttackData.cs`](../../src/Shared/AttackData.cs) contains the internal `HitboxEvent` projection used by helpers.
- [`ServerAbility.SpawnHitbox`](../../src/Shared/Abilities/ServerAbility.cs) resolves geometry and registers melee windows.
- [`HitboxGeometry.cs`](../../src/Shared/HitboxGeometry.cs) resolves cooked poses and swept geometry.
- [`SpellResolver.cs`](../../src/Shared/SpellResolver.cs) tests contact and manages active volumes.
- [`ServerSimulation.cs`](../../src/Shared/ServerSimulation.cs) builds hurtboxes and applies authoritative hit outcomes.
- [`UnityCharacterAssetCooker.cs`](../../client/Unity/Assets/Scripts/Editor/UnityCharacterAssetCooker.cs) cooks package assets and deterministic pose tracks.

---

## Hitbox runtime fields

### Selected fields from `src/Shared/Hitbox.cs`

```csharp
public struct Hitbox
{
    public float X, Y, Z;             // Absolute position (world space)
    public float VX, VY, VZ;          // World-space velocity
    public float Radius;              // Sphere radius
    public ushort DurationTicks;      // Lifetime in ticks (60Hz)
    public ushort AgeTicks;           // Current age (not incremented for a melee hitbox while its owner is in Hitstop)

    public HitboxShape Shape;         // Sphere or Capsule
    public float EndX, EndY, EndZ;    // Capsule end point (ignored for Sphere)

    public float Damage;              // Damage amount
    public float BaseKnockback;        // Base horizontal knockback (minimum at 0% damage)
    public float KnockbackGrowth;      // Additional knockback per dmg% * 0.01
    public ushort StunTicks;          // Zero/nonzero gate for formula Hitstun
    public ulong OwnerId;             // Attacker identity; CanHitOwner controls self-contact

    public bool Active;               // false = removed or expired
    public bool CancelWithAbility;    // owning activation controls this window's lifetime
    public bool HitsMultipleOpponents; // persists after contact, tags each opponent once
    public HashSet<ulong>? HitEntities; // per-window or shared HitGroup history
}
```

### HitboxShape enum

```csharp
public enum HitboxShape : byte
{
    Sphere = 0,
    Capsule = 1    // segment from (X,Y,Z) to (EndX,EndY,EndZ), radius = Radius
}
```

---

## Historical HitboxEvent authoring examples

The structs and Manki combo below preserve an earlier authoring model. They are not
the current package schema or roster move data. In current `AttackData.cs`, offsets
use `Off*` and `EndOff*`, bone endpoints use `BoneName` and `EndBoneName`, and
knockback uses `KnockbackData`. The old `BoneOff*`, Warp, and LMB chain recipes below
do not define current behavior.

### Historical HitboxEvent sketch

```csharp
public struct HitboxEvent
{
    public ushort TriggerTick;         // Tick from attack start when this hitbox spawns
    public ushort DurationTicks;       // How many frames the hitbox stays active
    public float Radius;               // Hitbox radius
    public float OffX, OffY, OffZ;     // Offset from attacker center (rotated by facing yaw)
    public float EndOffX, EndOffY, EndOffZ; // Capsule end offset (relative to OffX/Y/Z)
    public string? BoneName;            // If set, position hitbox at this bone instead of OffX/Y/Z
    public float BoneOffX, BoneOffY, BoneOffZ; // Local offset from bone origin (rotated by facing yaw)
    public float Damage;
    public float BaseKnockback;
    public float KnockbackGrowth;
    public ushort StunTicks;
    public bool Interruptible;         // If false: persists even if attacker is hit during startup
}
```

### Historical AttackStage sketch

```csharp
public struct AttackStage
{
    public ushort DurationTicks;        // Total animation lock duration in ticks
    public HitboxEvent[] HitboxEvents;  // Hitbox events triggered during this stage
    public float LungeForce;            // Forward burst during attack
    public ushort ChainWindowTicks;     // Frames to buffer next input (0 = final/no chain)
    public float AttackRange;           // Distance where auto-dash triggers
    public float WarpRange;
    public bool UseTargetLock;
    public bool RotateTowardTarget;
    public float TrackingStrength;      // 0-1 rotation lerp toward target per frame
}
```

### Historical Manki LMB combo

```csharp
new AbilityData
{
    Name = "LMB Combo",
    Stages = new[]
    {
        // Stage 0: Jab
        new AttackStage
        {
            DurationTicks = 12,
            HitboxEvents = new[]
            {
                new HitboxEvent
                {
                    TriggerTick = 6, DurationTicks = 2, Radius = 0.5f,
                    OffX = 1.5f, OffY = 1.0f, OffZ = 0f,
                    Damage = 4, BaseKnockback = 3.2f, KnockbackGrowth = 4.8f, KnockbackUpward = 3,
                    StunTicks = 10, Interruptible = true
                }
            },
            ChainWindowTicks = 8,    // 8 ticks to buffer next input
        },
        // Stage 1: Melee
        new AttackStage
        {
            DurationTicks = 14,
            HitboxEvents = new[]
            {
                new HitboxEvent
                {
                    TriggerTick = 8, DurationTicks = 2, Radius = 0.7f,
                    OffX = 2.0f, OffY = 1.0f, OffZ = 0f,
                    Damage = 6, BaseKnockback = 4.8f, KnockbackGrowth = 7.2f, KnockbackUpward = 5,
                    StunTicks = 15, Interruptible = true
                }
            },
            ChainWindowTicks = 6,
        },
        // Stage 2: Flying Kick (final)
        new AttackStage
        {
            DurationTicks = 20,
            HitboxEvents = new[]
            {
                new HitboxEvent
                {
                    TriggerTick = 8, DurationTicks = 3, Radius = 0.8f,
                    OffX = 2.5f, OffY = 0.5f, OffZ = 0f,
                    Damage = 12, BaseKnockback = 8.0f, KnockbackGrowth = 12.0f, KnockbackUpward = 10,
                    StunTicks = 20, Interruptible = true
                }
            },
            ChainWindowTicks = 0,  // Final stage — no chain
        }
    }
}
```

---

## Shared spawning and lifecycle

`CookedTimelineAbility` executes ordered operations on its authored tick timeline.
Melee operations use `ServerAbility.SpawnHitbox`, which resolves geometry through
`HitboxGeometry.ResolvePositions` and adds a runtime `Hitbox` to the resolver.

The helper sets `CancelWithAbility`, `FreezesOwner`, and `HitsMultipleOpponents`.
Each window tags each opponent once and can hit several opponents before it expires.
Matching nonzero `HitGroup` values share hit history across windows in one activation.

Melee windows do not age while the owner is in Hitstop. Cancellation removes the
activation's owned windows through `ServerSimulation.CancelAbilityEffects` and
`SpellResolver.RemoveOwnedHitboxes`. Normal gameplay cancellation preserves launched
projectiles whose lifetime is independent of the ability. Entity removal can request
projectile cleanup too.

### Bone-attached hitboxes

`ServerAbility.SpawnHitbox` supports bone attachment directly. With usable baked
poses, `BoneName` anchors the start and `EndBoneName` can anchor a capsule's end.
`Off*` and `EndOff*` apply facing-relative offsets at those anchors. Weapon points
resolve at visual scale; body points use `HurtboxBoneScale`.

Tracked bone hitboxes re-resolve every tick and sweep between ticks. Explicit
`followOwner` attachment also supports root-relative geometry. A missing bake,
animation, or bone falls back to the corresponding entity-relative geometry; the
helper does not silently discard the hitbox. Runtime fallback is not proof that a
package's bake or visual alignment is acceptable.

For sweet and sour spots, use the package's hitbox operations and shared hit identity
when both volumes must count as one hit per opponent. Inspect the cooked result and
preview against the actual character. New roster work does not add character-specific
`ServerAbility` classes or manually resolve bones in Unity.

#### Clip clock and swept bone hitboxes — since 2026-10-17

**Clip clock.** An attack pose sits at the fractional clip frame
`HitboxGeometry.AttackFrame(stageElapsed, frameCount, stageDurationTicks)`, where
`stageElapsed` is `AttackElapsedTicks` minus the earlier stages' durations, the same clip time the
renderer plays. Between two baked frames, points interpolate. A capsule with two baked ends moves
rigidly: the start point is interpolated and the end follows the blended direction at the
blended length, so a fast blade keeps its length (`HitboxGeometry.BlendSegment`). The
attacker's own hurtboxes use the same clock. They used to read a per-entity frame counter that
ran a tick behind the hitboxes and wrapped at the clip's frame count, so on clips playing
under 1× speed the body snapped back to the opening pose for the last ticks of the move (up to
1.6 m on Bonk's Reverse Air Slash). `PlayerRenderer`'s baked blade path (the sword trail) and a
freshly started attack clip use the same clock, so what is drawn matches what is tested.

**Sweep.** From a tracked hitbox's second active tick on, `SpellResolver.UpdateBoneHitboxes`
fills `Hitbox.Sweep` with the poses it passed through since the previous tick
(`HitboxGeometry.SweepPoses`). The owner's position, facing and pitch blend between the two
ticks. The clip follows every baked frame in between, and poses are spaced at most half the
radius apart (24 poses at most, the current one included). `SpellResolver.Tick` tests the swept
poses earliest first, then the current pose; the first pose in contact supplies the contact
point and launch geometry. Rules:

- The first active tick tests only its own pose, so no move starts hitting earlier.
- Each opponent is still hit at most once per window.
- An attack-clock seek or reset, a stage change, and hitstop (nothing moves) produce no sweep.

Before the sweep, 11–15% of the positions where Wibou's drawn sword passed through a standing
opponent registered nothing, and 37–40% for Air Slash and Reverse Slash against an opponent
1 m below. Fists and feet, with 1 m hit spheres, lost 5% or less. Several consumers read the
swept poses:

- `LastTickCheckedHitboxes` entries carry the sweep.
- The Ability Lab and Training hitbox display draw it.
- The practice session log's closest approach includes it.
- `tools/HitReliabilityReport` counts it as tested.
---

## Hurtboxes

### Cooked pose pipeline

The Unity package cook samples the bound rig and clips into deterministic pose tracks.
`MatchContentCatalog` admits those tracks together with the runtime definition.
`ServerSimulation` receives the resolved `BakedAnimationData` when each fighter registers.

Each tick, `ResolveBoneAnimFrame` selects the semantic pose and frame. Attacks use the
fractional clip clock shared with hitboxes. Other states use their own pose selection
and frame policy. `HitboxGeometry.BakedPointToWorld` applies scale, facing, and the
fighter's world position. Hurtbox construction then applies `HitboxGeometry.ApplyAttackPosePitch`.

Shared does not parse a rig or walk a live Unity skeleton at runtime. It still performs
pose lookup, interpolation, and world-space math.

#### Drawn hips — package cooks since 2026-10-17

Points stay hips-relative, but the package cook also writes `_hips_offset`: where the
animation draws the hips, relative to the capsule centre in the facing frame. The cook
evaluates each clip the way Animancer plays it (playable graph, no root motion, no foot IK);
`AnimationClip.SampleAnimation` agrees on the hips-relative pose but put the hips up to 3.5 m
elsewhere for clips with root curves. `HitboxGeometry.BakedPointToWorld` places every baked
point for hitboxes, hurtboxes and the Ability Lab: the hips at `_hips_offset`, every other
point at hips + scale × (point − hips), with scale 1 for hurtboxes and weapon attack points. Body-bone attack points retain
`HurtboxBoneScale`. Bakes without `_hips_offset` keep the fixed `HipHeight` pivot. Before this,
the fixed pivot put Wibou's tested sword up to 37 cm from the drawn blade in his normals and
53 cm in Skyfall's dive, and every fighter's hurtboxes trailed animated hip motion by up to
1.2 m (Bonk's Targeted Jump Slam). The `Rendered Pose Alignment` Editor self-test renders
every clip frame through Animancer and requires each resolved point within 0.1 mm.

### Hurtbox construction and fallback

`ServerSimulation.BuildHurtboxList` and `BuildEntitiesFromState` produce the same
`SpellResolver.EntityData` for simulation, Training, and Ability Lab.
Bone definitions pair with baked points by name, independently of definition order.
Eliminated fighters do not supply ordinary vulnerable hurtboxes.

A package `hurtboxBoneDefs` entry with only `boneId`, offsets, and `radius` produces a sphere.
An optional `endBoneId` produces a capsule, with independent `endOffsetX/Y/Z` values that default to zero.
Both endpoints resolve on the drawn pose at scale 1. Both offsets rotate with facing,
and both points receive `HitboxGeometry.ApplyAttackPosePitch`.
`HurtboxBoneScale` still controls body-bone attack reach and does not shrink hurtbox points.

`CharacterBoneRegistry` maps semantic IDs to humanoid mappings and stable baked names.
The cook uses `Animator.GetBoneTransform`, including on rigs whose transform names differ.
`rig:<exact transform name>` names a non-humanoid point such as a tail bone.
The cook retains the original required points and adds every hurtbox start and end.
Package admission fails with `package.pose.hurtbox-missing` if either endpoint is absent from `poses.bin`.
An invalid runtime definition throws instead of silently omitting a vulnerable body region.

The roster uses mesh-fitted head, torso, arm, hand, leg, and foot capsules.
Salamander and Manki also have tail capsules. Bashie's shell is part of her body.
Nyx's skirt, Bonk's cape, Wibou's coat, and Salamander's hat do not add hurtboxes.
See [ADR-0055](../adr/0055-capsule-hurtboxes.md) and the
[fit and coverage tools](../../tools/character_sizes/README.md).

When the baked-animation path is unavailable, the fallback uses authored `HurtboxCapsules`.
Callers must register the resolved `BakedAnimationData`; a `BakedDataPath` string
cannot load poses into a directly registered entity. Missing poses and missing
fallback capsules can remove collision rather than create a generic hurtbox.
Keep fallback capsules intact through definition serialization and catalog cloning.

---

## Collision engine

### Tick-by-tick flow

`SpellResolver.Tick` moves projectiles, checks eligible target volumes, and returns
hit records. `ServerSimulation.ResolveHits` applies combat outcomes. The source owns
the exact order, shield policy, moving-target contact ordering, and launch calculation.

Current contact and lifecycle rules are:

- Sphere and capsule contact use Shared geometry. Tracked bone hitboxes test swept poses
  before their current pose.
- `hitThisTick` prevents a target from receiving two resolver hits in the same tick.
- `HitsMultipleOpponents` keeps a window alive after contact. `HitEntities` prevents
  repeat hits on the same opponent for that window or shared group.
- A hitbox without `HitsMultipleOpponents` deactivates on its first accepted contact.
  Standard projectiles use this policy; explosions can hit several targets.
- Owner hits require `CanHitOwner`. Inactive, invincible, and disallowed friendly
  targets are rejected before consuming contact.
- Expiration and activation cancellation remove windows. Hitstop pauses aging for
  the owner's melee windows, while independent projectiles keep their own time.

### Hit records

`SpellResolver.HitResult` carries the owner, target, canonical slot, activation identity,
contact geometry, and authored launch data. `ServerSimulation` resolves the accepted
combat result, including block state, impact force, and Hitstop. The current fields
and packet encoding live in Shared source rather than this page's historical sketches.

---

## Debug visualization

Training's `OnDrawGizmos` draws hurtboxes from Shared target data and hitboxes from
`SpellResolver.LastTickCheckedHitboxes`. The hitbox view includes the last active
tick and swept poses. Enable Gizmos in the Unity Scene view. The serialized
`TrainingMatch._showHitboxes` option also enables runtime debug lines.

Ability Lab exposes hitbox and hurtbox overlays for its Shared source preview.
Holding F3 in PvP displays prediction statistics. It does not toggle a
`DebugHitboxDraw` component; that component is not part of the current client.

---

## Ground collision and projectile explosions

After entity collision checks in `SpellResolver.Tick()`, a separate pass
`CheckGroundCollision(ArenaDefinition)` samples the arena heightmap at each
projectile's XZ position. If the projectile's bottom edge (`Y - Radius`) has
reached or passed the terrain surface, the projectile's explosion spawns at
ground height and the projectile is deactivated.

This applies to active gravity-affected projectiles with an explosion definition.
`CheckGroundCollision` queues the explosion, reports hitbox removal, and removes the
projectile. The authoritative follow-up spawns the explosion hitbox. Package operations
and Shared projectile configuration determine this behavior, not a physical key binding
or Unity collider callback.

---

## Performance and allocation

Collision work depends on active hitboxes, target volumes, and swept poses. Broad
sphere/capsule checks use pure math without engine physics queries.

`SpellResolver.Tick` allocates a result list and a per-tick `HashSet`.
Multi-opponent windows allocate hit-history sets, and debug snapshots and swept-pose
arrays can allocate too. Struct geometry does not make this path allocation-free.
Measure the affected scenario before making a performance claim.

## Extension points

Use the source layout above to trace collision changes. New package moves normally
reuse existing cooked operations and geometry. A new shape or runtime field is engine
work and also needs snapshot and codec coverage; editing a legacy authoring sketch is
not enough.

---

## Add a package attack

1. Define the canonical ground or air move in the character package.
2. Author its fixed timeline, hitbox windows, semantic bone attachments, and presentation bindings.
3. Preview against the actual character, cook, and validate the resulting package.
4. Check contact, interruption, multiple opponents, and visual alignment through the
   focused verification in [ability architecture](ability-architecture.md) and the
   [character guide](../characters/adding-a-new-character.md).

Shared owns hit timing and outcomes. Unity consumes state and semantic events.

---

## Historical client-side hitbox spawning

The excerpt below records the retired Godot-era split between LMB and other slots.
It is not implemented roster behavior or a transition to preserve. Current package
moves run in Shared for every canonical slot.

```csharp
// PlayerController.ResolveAbilityStages() — for RMB/special abilities only
// Sim handles LMB hitbox spawning (triggered by HitboxEvent.TriggerTick)
// Client still handles other slots (RMB, Q, E, R, F) via AbilityRegistry effects
```
