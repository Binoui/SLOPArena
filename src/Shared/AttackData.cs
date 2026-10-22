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
    /// One stage of an ability. A simple ability has 1 stage; multi-hit moves
    /// declare several HitboxEvents within the stage (issue #115 — no chained stages).
    /// </summary>
    public struct AttackStage
    {
        /// <summary>Total animation lock duration in ticks.</summary>
        public ushort DurationTicks;
        /// <summary>
        /// IASA early-out tick (issue #124): from this tick of the stage onward, any ability
        /// input interrupts the recovery — the current animation stops and the new ability
        /// starts immediately. 0 = none (default): full lock until the stage ends, the
        /// pre-IASA behavior. No input still lets the move complete at its normal duration.
        /// </summary>
        public ushort IasaTicks;
        /// <summary>
        /// Landing lag lock applied when the character lands while this stage's ability is
        /// active (issue #125): the full attack-lock semantics — no ability input, no jump,
        /// no dash, no burst, no input movement — for this many ticks. 0 = none (default):
        /// landing never locks, the pre-issue behavior. Air stages only: the lock fires when
        /// the ability that was started airborne lands mid-stage. Independent of IasaTicks —
        /// landing lag is a hard lock that even an IASA-unlocked stage does not bypass.
        /// </summary>
        public ushort LandingLagTicks;
        /// <summary>
        /// Auto-cancel window start (issue #125): a landing at stage-elapsed tick
        /// <c>&lt;= AutoCancelBeforeTicks</c> skips the landing lag entirely — act immediately.
        /// 0 = window disabled. The early Melee window: land right after the move starts,
        /// before the hitbox comes out, and there is no commitment.
        /// </summary>
        public ushort AutoCancelBeforeTicks;
        /// <summary>
        /// Auto-cancel window end (issue #125): a landing at stage-elapsed tick
        /// <c>&gt;= AutoCancelAfterTicks</c> skips the landing lag entirely — act immediately.
        /// 0 = window disabled. The classic Melee autocancel: land after the active frames
        /// are over and there is no landing lag. Declare <c>&gt;= DurationTicks</c> to disable.
        /// </summary>
        public ushort AutoCancelAfterTicks;
        /// <summary>Hitbox events triggered during this stage.</summary>
        public HitboxEvent[] HitboxEvents;
        /// <summary>Forward burst at attack start (applied once).</summary>
        public float LungeForce;
        /// <summary>
        /// Per-tick velocity during this stage (world space). Set VY for jump arcs / slams.
        /// Non-zero components are written each tick; a zero component is left alone so a
        /// MoveY-only stage keeps its LungeForce horizontal velocity. Consumers must apply
        /// these values during their attack tick.
        /// </summary>
        public float MoveX, MoveY, MoveZ;

        /// <summary>Optional bone trail VFX for this stage. If null, falls back to AbilitySpec.BoneTrails.</summary>
        public BoneTrailDef[]? BoneTrails;
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
