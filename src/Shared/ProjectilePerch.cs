using System;

namespace SlopArena.Shared
{
    /// <summary>
    /// A spawnProjectile's optional homing block (runtime API 1.18.0, ADR-0051). The current target is captured at
    /// spawn; the velocity turns toward it by at most <see cref="TurnDegreesPerTick"/> and keeps its speed. Once the
    /// target is behind the projectile, or gone, it flies straight for good.
    /// </summary>
    public sealed record ProjectileHomingSource(float TurnDegreesPerTick);

    /// <summary>
    /// A spawnProjectile's optional perch block (runtime API 1.18.0, ADR-0051, Nyx's Hex Crow). The first fighter
    /// body it meets carries it, with no damage, for <see cref="PerchTicks"/>; a shield spends it. While it rides,
    /// a damaging hit on the carrier from the thrower's other form queues <see cref="Pop"/> on them, after that
    /// hit's hitstop. It never explodes: it expires silently, and a knocked-out carrier or thrower loses it.
    /// </summary>
    public sealed record ProjectilePerchSource(ushort PerchTicks, ProjectilePopSource Pop);

    /// <summary>The perched projectile's bonus hit: an ordinary launch from the thrower, through shields.</summary>
    public sealed record ProjectilePopSource(
        float Damage,
        float Angle,
        float BaseKnockback,
        float KnockbackGrowth,
        ushort StunTicks,
        string PresentationId = "");

    /// <summary>The cooked homing block; the turn's cosine and sine are fixed at cook time.</summary>
    public sealed record CookedProjectileHoming(float TurnDegreesPerTick, float TurnCos, float TurnSin);

    public sealed record CookedProjectilePerch(ushort PerchTicks, CookedProjectilePop Pop);

    public sealed record CookedProjectilePop(
        float Damage,
        float Angle,
        float BaseKnockback,
        float KnockbackGrowth,
        ushort StunTicks,
        string PresentationId = "");

    /// <summary>What a perched projectile carries in the resolver: the pop it queues.</summary>
    public struct ProjectilePerch
    {
        public float Damage;
        public sbyte Angle;
        public float BaseKnockback;
        public float KnockbackGrowth;
        public ushort StunTicks;
        public string? PresentationId;

        public static ProjectilePerch From(CookedProjectilePop pop) => new()
        {
            Damage = pop.Damage,
            Angle = (sbyte)pop.Angle,
            BaseKnockback = pop.BaseKnockback,
            KnockbackGrowth = pop.KnockbackGrowth,
            StunTicks = pop.StunTicks,
            PresentationId = pop.PresentationId.Length == 0 ? null : pop.PresentationId,
        };
    }

    /// <summary>Homing and perch rules shared by the compiler and the cooked loader (ADR-0051).</summary>
    internal static class ProjectilePerchContract
    {
        internal const float MaxTurnDegreesPerTick = 90f;
        internal const ushort MaxPerchTicks = 1200;
        private const ushort MaxPopStunTicks = 240;

        internal static CookedProjectileHoming? Cook(ProjectileHomingSource? homing)
            => homing == null ? null : new CookedProjectileHoming(homing.TurnDegreesPerTick,
                (float)Math.Cos(homing.TurnDegreesPerTick * Math.PI / 180d),
                (float)Math.Sin(homing.TurnDegreesPerTick * Math.PI / 180d));

        internal static CookedProjectilePerch? Cook(ProjectilePerchSource? perch)
            => perch == null ? null : new CookedProjectilePerch(perch.PerchTicks, new CookedProjectilePop(perch.Pop.Damage,
                perch.Pop.Angle, perch.Pop.BaseKnockback, perch.Pop.KnockbackGrowth, perch.Pop.StunTicks,
                perch.Pop.PresentationId));

        /// <summary>Null when the projectile's homing and perch blocks are admissible.</summary>
        internal static string? BoundsError(CookedProjectile projectile)
        {
            if (projectile.Homing is { } homing)
            {
                float norm = homing.TurnCos * homing.TurnCos + homing.TurnSin * homing.TurnSin;
                if (!float.IsFinite(homing.TurnDegreesPerTick) || !(homing.TurnDegreesPerTick > 0f)
                    || homing.TurnDegreesPerTick > MaxTurnDegreesPerTick || !float.IsFinite(norm)
                    || MathF.Abs(norm - 1f) > .00001f || homing.TurnCos < 0f || !(homing.TurnSin > 0f))
                    return "homing.turnDegreesPerTick must be above 0 and at most 90.";
                if (!(projectile.Speed > 0f)) return "A homing projectile needs a positive speed.";
            }
            if (projectile.Perch is not { } perch) return null;
            if (perch.PerchTicks == 0 || perch.PerchTicks > MaxPerchTicks) return "perch.perchTicks must be 1 to 1200.";
            if (projectile.Return != null) return "A perching projectile cannot return.";
            if (projectile.Damage != 0f || projectile.BaseKnockback != 0f || projectile.KnockbackGrowth != 0f
                || projectile.StunTicks != 0)
                return "A perching projectile lands without damage: its own damage, knockback and stun stay zero.";
            var pop = perch.Pop;
            if (!float.IsFinite(pop.Damage) || !float.IsFinite(pop.Angle) || !float.IsFinite(pop.BaseKnockback)
                || !float.IsFinite(pop.KnockbackGrowth))
                return "The pop's values must be finite.";
            if (!(pop.Damage > 0f && pop.Damage <= 100f)) return "perch.pop.damage must be above 0 and at most 100.";
            if (pop.Angle < -90f || pop.Angle > 90f) return "perch.pop.angle must be -90 to 90.";
            if (pop.BaseKnockback < 0f || pop.KnockbackGrowth < 0f) return "The pop's knockback cannot be negative.";
            if (pop.StunTicks > MaxPopStunTicks) return "perch.pop.stunTicks must be 0 to 240.";
            return null;
        }
    }
}
