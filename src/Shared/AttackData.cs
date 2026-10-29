namespace SlopArena.Shared
{
    /// <summary>
    /// A hitbox spawned during an attack at a specific tick.
    /// </summary>
    /// <summary>Hitbox event triggered by an attack stage at a specific tick.</summary>
    public struct HitboxEvent
    {
        /// <summary>Frame offset (in ticks) from the start of the stage.</summary>
        public ushort TriggerTick;
        /// <summary>Lifetime of the spawned hitbox in ticks.</summary>
        public ushort DurationTicks;
        /// <summary>Hitbox shape (Sphere or Capsule).</summary>
        public HitboxShape Shape;
        public float Radius;
        /// <summary>Offset from attacker center (rotated by facing yaw).</summary>
        public float OffX, OffY, OffZ;
        /// <summary>Capsule end offset (relative to OffX/Y/Z, rotated by facing yaw).</summary>
        public float EndOffX, EndOffY, EndOffZ;
        /// <summary>If set, anchor the hitbox at this bone's world position (plus the
        /// OffX/Y/Z offset). Null = anchor at entity origin.</summary>
        public string? BoneName;
        /// <summary>If set (with BoneName), the capsule's end anchors at this baked
        /// point's world position (plus the EndOffX/Y/Z delta, default 0). Re-resolved
        /// per tick like BoneName — lets a hitbox sweep from one baked point to
        /// another (e.g. hand → weapon tip). Null = the EndOff* delta only.</summary>
        public string? EndBoneName;

        public float Damage;
        /// <summary>Knockback profile + optional custom overrides. Resolved at spawn time.</summary>
        public KnockbackData Knockback;
        public ushort StunTicks;
        /// <summary>Optional fixed hitstun override; zero uses the launch formula.</summary>
        public ushort FixedHitstunTicks;
        /// <summary>If false: persists even if attacker is hit during startup.</summary>
        public bool Interruptible;

        /// <summary>
        /// Cross-event hit identity within one ability activation. 0 = this event has its own
        /// one-hit-per-opponent set. Matching non-zero values share a set, so a sweetspot can
        /// hand off to a sourspot without hitting the same opponent twice.
        /// </summary>
        public byte HitGroup;
        /// <summary>Direction of horizontal knockback relative to the hitbox owner.</summary>
        public AuthoringKnockbackDirection KnockbackDirection;
    }

    /// <summary>
    /// A projectile that comes back to its thrower, like a boomerang (Sir Bonk's pot lid). It flies
    /// out for <see cref="OutboundTicks"/> (or until it meets the stage or a shield), then steers back
    /// at the owner's chest at up to <see cref="ReturnSpeed"/>, turning at <see cref="ReturnAcceleration"/>.
    /// It hits each fighter at most once per leg, pulling them toward the owner on the way back. Reaching
    /// the owner, it is caught when he is free and hits him (a stun, no launch) when he is busy.
    /// </summary>
    public struct ProjectileReturn
    {
        public ushort OutboundTicks;
        public float ReturnSpeed;
        public float ReturnAcceleration;
        public float SelfHitDamage;
        public ushort SelfHitStunTicks;
        /// <summary>Optional semantic presentation emitted when the owner catches it.</summary>
        public string CatchPresentationId;
        /// <summary>Optional semantic presentation emitted when it hits its busy owner.</summary>
        public string SelfHitPresentationId;
    }

    /// <summary>
    /// Explosion spawned when a projectile hits an entity or the ground.
    /// Larger radius, separate damage/knockback from the direct projectile hit.
    /// </summary>
    public struct ProjectileExplosion
    {
        public float Radius;
        public float Damage;
        /// <summary>Knockback profile + optional custom overrides. Resolved at explosion spawn time.</summary>
        public KnockbackData Knockback;
        public ushort StunTicks;
        public ushort DurationTicks;
        /// <summary>If true, this explosion can hit its spawner (mine jump, etc.).</summary>
        public bool CanHitOwner;
        public float SelfPushSpeed;
        public bool EndsOwnerActivation;
        /// <summary>Optional self-damage, retained after the firing ability ends.</summary>
        public float? OwnerDamage;
        /// <summary>Optional semantic presentation emitted when this explosion is spawned.</summary>
        public string ExplosionPresentationId;
        /// <summary>Attack sequence captured by the originating capability activation.</summary>
        public byte PresentationAttackSequence;
        /// <summary>Flattened timeline operation index captured by the originating capability.</summary>
        public int PresentationOperationIndex;
        /// <summary>Facing yaw captured by the originating capability activation, in radians.</summary>
        public float PresentationYaw;
    }
}
