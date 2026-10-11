using System.Collections.Generic;

namespace SlopArena.Shared
{
    public enum HitboxShape : byte
    {
        Sphere = 0,
        /// <summary>
        /// segment from (X,Y,Z) to (EndX,EndY,EndZ), radius = Radius
        /// </summary>
        Capsule = 1
    }

    /// <summary>
    /// A single hitbox spawned by an ability.
    /// Position is absolute. Non-tracked hitboxes keep their spawn position and
    /// move only by velocity (Velocity (0,0,0) = static melee hitbox, non-zero =
    /// projectile). Tracked hitboxes (TracksBone) re-resolve X/Y/Z and capsule
    /// EndX/EndY/EndZ every tick from the owner's baked bone pose, or facing-relative
    /// root offsets when no baked pose is available, and ignore VX/VY/VZ.
    /// Shape: Sphere (default) or Capsule (uses EndX/EndY/EndZ).
    /// Resolved via sphere/capsule collision each tick in SpellResolver.
    /// </summary>
    public struct Hitbox
    {
        public float X, Y, Z;
        public float VX, VY, VZ;
        public float Radius;
        public ushort DurationTicks;
        public ushort AgeTicks;

        /// <summary>
        /// Capsule support
        /// </summary>
        public HitboxShape Shape;
        /// <summary>
        /// Capsule end point (ignored for Sphere)
        /// </summary>
        public float EndX, EndY, EndZ;

        /// <summary>
        /// Damage data
        /// </summary>
        public float Damage;
        public float BaseKnockback;
        public float KnockbackGrowth;
        /// <summary>Launch angle in degrees (-90 to 90). Resolved from profile at spawn time.</summary>
        public sbyte KnockbackAngle;
        /// <summary>Horizontal launch direction relative to the hitbox owner.</summary>
        public AuthoringKnockbackDirection KnockbackDirection;
        public ushort StunTicks;
        /// <summary>Optional fixed hitstun override; zero uses the launch formula.</summary>
        public ushort FixedHitstunTicks;
        public ulong OwnerId;

        /// <summary>Unique server-local ability activation that spawned this hitbox.</summary>
        public ulong ActivationId;
        /// <summary>Ground/air identity of the activation, independent of bone pose sampling.</summary>
        public bool ActivationAirborne;
        /// <summary>Stable per-owner attack sequence captured at activation for feedback identity.</summary>
        public byte AttackSequence;

        /// <summary>Originating attack slot for presentation routing (0 = unspecified).</summary>
        public byte AttackSlot;
        /// <summary>Cooked projectile operation that spawned this visual (stable within an activation).</summary>
        public int VisualOperationIndex;

        public bool Active;

        /// <summary>Melee windows are removed on interruption; launched projectiles keep travelling.</summary>
        public bool CancelWithAbility;

        /// <summary>
        /// Gravity applied each tick (m/s²). 0 = no gravity (default for melee hitboxes).
        /// </summary>
        public float Gravity;

        /// <summary>
        /// Optional explosion spawned when this hitbox deactivates (hits entity or expires).
        /// Used by projectiles to create an AoE burst on impact.
        /// </summary>
        public ProjectileExplosion? Explosion;

        /// <summary>If true, this hitbox can hit the entity that spawned it.</summary>
        public bool CanHitOwner;

        /// <summary>If true, this hit freezes the owner too (melee contact). Projectiles and explosions freeze the receiver alone.</summary>
        public bool FreezesOwner;

        /// <summary>
        /// True = hit each opponent once and persist until the window expires (Melee-style
        /// melee hitboxes). False = die on first contact (projectiles). Uses HitEntities.
        /// </summary>
        public bool HitsMultipleOpponents;

        /// <summary>
        /// Entities already hit by this hitbox (one-hit-per-opponent). Null unless
        /// HitsMultipleOpponents; allocated at spawn. A reference field on a struct — the
        /// set is shared across the struct copies SpellResolver makes.
        /// </summary>
        public HashSet<ulong>? HitEntities;


        /// <summary>True = owner-following melee hitbox; resolves bone or root-relative offsets each tick.</summary>
        public bool TracksBone;

        /// <summary>Original HitboxEvent (BoneName + Off* + EndOff*), for per-tick re-resolution.</summary>
        public HitboxEvent SourceEvent;

        /// <summary>Optional baked skeleton for tracked hitbox resolution; null uses root-relative offsets.</summary>
        public BakedAnimationData? Baked;

        /// <summary>Owner's CharacterDefinition (HurtboxBoneScale, HipHeight, stage duration). Null unless TracksBone.</summary>
        public CharacterDefinition? Def;

        /// <summary>Ability AnimationNames[] for pose selection. Null unless TracksBone.</summary>
        public string[]? AnimationNames;

        /// <summary>AnimIndex into AnimationNames at spawn time.</summary>
        public byte AnimIndex;

        /// <summary>0-based ability slot (stage-duration lookup in ResolvePositions).</summary>
        public byte Slot;

        /// <summary>
        /// True when the owner was airborne when this hitbox spawned. Air hitboxes only
        /// spawn while airborne, so this is the move's stable identity — re-resolution uses
        /// it (not the live IsGrounded, which can flip mid-window if the owner lands).
        /// </summary>
        public bool Airborne;
    }
}
