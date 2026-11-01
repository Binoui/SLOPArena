using System;

namespace SlopArena.Shared
{
    /// <summary>
    /// A spawnProjectile's optional homing block (runtime API 1.18.0, ADR-0051). The current target is captured at
    /// spawn; the velocity turns toward it by at most <see cref="TurnDegreesPerTick"/> and keeps its speed. Once the
    /// target is behind the projectile, or gone, it flies straight for good. A perching projectile dives instead.
    /// </summary>
    public sealed record ProjectileHomingSource(float TurnDegreesPerTick);

    /// <summary>
    /// A spawnProjectile's optional perch block (ADR-0051, Nyx's Hex Crow), a mark with a lifecycle (section 9):
    /// it rises over its target, marks a spot under itself, dives along that committed line, and the first fighter
    /// body it meets in the dive carries it, with no damage, for <see cref="PerchTicks"/>; a shield spends it. While
    /// it rides it may peck its carrier, and a shield or an air dodge swats it off. A damaging hit on the carrier from
    /// the thrower's other form queues <see cref="Pop"/> on them, after that hit's hitstop. It never explodes: every
    /// other ending scatters it, and a knocked-out carrier or thrower loses it. Without <see cref="Dive"/> (runtime API
    /// 1.18.0) it is in its dive from the throw: the first body it meets in flight carries it, and a homing block may
    /// steer it.
    /// </summary>
    public sealed record ProjectilePerchSource(
        ushort PerchTicks,
        ProjectilePopSource Pop,
        ProjectileDiveSource? Dive = null,
        ProjectilePeckSource? Peck = null,
        string ScatterPresentationId = "");

    /// <summary>The perched projectile's bonus hit: an ordinary launch from the thrower, through shields.</summary>
    public sealed record ProjectilePopSource(
        float Damage,
        float Angle,
        float BaseKnockback,
        float KnockbackGrowth,
        ushort StunTicks,
        string PresentationId = "");

    /// <summary>
    /// How a perch arrives (runtime API 1.21.0). It rises at the projectile's speed toward a point
    /// <see cref="HoverHeight"/> above its target's feet, following the target, for at most
    /// <see cref="MaxRiseTicks"/>. It then holds for <see cref="MarkTicks"/> with the spot fixed at the target's feet,
    /// and dives straight at that spot at <see cref="DiveSpeed"/>. Without a target the spot is
    /// <see cref="NoTargetDistance"/> ahead of the thrower at her feet.
    /// </summary>
    public sealed record ProjectileDiveSource(
        float HoverHeight,
        ushort MaxRiseTicks,
        ushort MarkTicks,
        float DiveSpeed,
        float NoTargetDistance);

    /// <summary>A perch's chip damage on its carrier every <see cref="IntervalTicks"/>, with no reaction (runtime API 1.21.0).</summary>
    public sealed record ProjectilePeckSource(ushort IntervalTicks, float Damage, string PresentationId = "");

    /// <summary>The cooked homing block; the turn's cosine and sine are fixed at cook time.</summary>
    public sealed record CookedProjectileHoming(float TurnDegreesPerTick, float TurnCos, float TurnSin);

    public sealed record CookedProjectilePerch(
        ushort PerchTicks,
        CookedProjectilePop Pop,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        CookedProjectileDive? Dive = null,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        CookedProjectilePeck? Peck = null,
        string ScatterPresentationId = "");

    public sealed record CookedProjectilePop(
        float Damage,
        float Angle,
        float BaseKnockback,
        float KnockbackGrowth,
        ushort StunTicks,
        string PresentationId = "");

    public sealed record CookedProjectileDive(
        float HoverHeight,
        ushort MaxRiseTicks,
        ushort MarkTicks,
        float DiveSpeed,
        float NoTargetDistance);

    public sealed record CookedProjectilePeck(ushort IntervalTicks, float Damage, string PresentationId = "");

    /// <summary>
    /// A perch's lifecycle phase (ADR-0051 section 9). Perched is exactly the phase with a carrier
    /// (<see cref="Hitbox.AttachedEntityId"/>); every ending removes the projectile.
    /// </summary>
    public enum PerchPhase : byte
    {
        Rising = 0,
        Marking = 1,
        Diving = 2,
        Perched = 3,
    }

    /// <summary>What a perching projectile carries in the resolver: its rules, copied at spawn, and its phase.</summary>
    public struct ProjectilePerch
    {
        public float Damage;
        public sbyte Angle;
        public float BaseKnockback;
        public float KnockbackGrowth;
        public ushort StunTicks;
        public string? PresentationId;

        public float RiseSpeed, HoverHeight, DiveSpeed;
        public ushort MaxRiseTicks, MarkTicks;
        public ushort PeckIntervalTicks;
        public float PeckDamage;
        public string? PeckPresentationId;
        public string? ScatterPresentationId;

        public PerchPhase Phase;
        /// <summary>Ticks spent in <see cref="Phase"/>; perched, ticks on the carrier.</summary>
        public ushort PhaseTicks;
        /// <summary>The fighter it rises over; zero without one, or once it is gone.</summary>
        public ulong TargetId;
        /// <summary>Its target's feet while rising, then the committed spot it dives at.</summary>
        public float SpotX, SpotY, SpotZ;
        /// <summary>The dive's length in ticks, fixed when the dive starts.</summary>
        public ushort DiveTicks;

        public static ProjectilePerch From(CookedProjectilePerch perch, float riseSpeed)
        {
            var pop = perch.Pop;
            // Without a dive block the whole flight is its dive.
            var dive = perch.Dive ?? new CookedProjectileDive(0f, 0, 0, 0f, 0f);
            return new ProjectilePerch
            {
                Phase = perch.Dive == null ? PerchPhase.Diving : PerchPhase.Rising,
                DiveTicks = perch.Dive == null ? ushort.MaxValue : (ushort)0,
                Damage = pop.Damage,
                Angle = (sbyte)pop.Angle,
                BaseKnockback = pop.BaseKnockback,
                KnockbackGrowth = pop.KnockbackGrowth,
                StunTicks = pop.StunTicks,
                PresentationId = NullIfEmpty(pop.PresentationId),
                RiseSpeed = riseSpeed,
                HoverHeight = dive.HoverHeight,
                DiveSpeed = dive.DiveSpeed,
                MaxRiseTicks = dive.MaxRiseTicks,
                MarkTicks = dive.MarkTicks,
                PeckIntervalTicks = perch.Peck?.IntervalTicks ?? 0,
                PeckDamage = perch.Peck?.Damage ?? 0f,
                PeckPresentationId = NullIfEmpty(perch.Peck?.PresentationId),
                ScatterPresentationId = NullIfEmpty(perch.ScatterPresentationId),
            };
        }

        /// <summary>Perched, whether this tick on the carrier is a peck.</summary>
        public readonly bool PecksNow => Phase == PerchPhase.Perched && PeckIntervalTicks > 0 && PhaseTicks > 0
            && PhaseTicks % PeckIntervalTicks == 0;

        private static string? NullIfEmpty(string? id) => string.IsNullOrEmpty(id) ? null : id;
    }

    /// <summary>Homing and perch rules shared by the compiler and the cooked loader (ADR-0051).</summary>
    internal static class ProjectilePerchContract
    {
        internal const float MaxTurnDegreesPerTick = 90f;
        internal const ushort MaxPerchTicks = 1200;
        private const ushort MaxPopStunTicks = 240;
        private const ushort MaxDivePhaseTicks = 240;
        private const float MaxHoverHeight = 12f;
        private const float MaxDiveSpeed = 60f;
        private const float MaxNoTargetDistance = 20f;
        private const float MaxPeckDamage = 20f;

        internal static CookedProjectileHoming? Cook(ProjectileHomingSource? homing)
            => homing == null ? null : new CookedProjectileHoming(homing.TurnDegreesPerTick,
                (float)Math.Cos(homing.TurnDegreesPerTick * Math.PI / 180d),
                (float)Math.Sin(homing.TurnDegreesPerTick * Math.PI / 180d));

        internal static CookedProjectilePerch? Cook(ProjectilePerchSource? perch)
            => perch == null ? null : new CookedProjectilePerch(perch.PerchTicks, new CookedProjectilePop(perch.Pop.Damage,
                    perch.Pop.Angle, perch.Pop.BaseKnockback, perch.Pop.KnockbackGrowth, perch.Pop.StunTicks,
                    perch.Pop.PresentationId),
                perch.Dive is { } dive ? new CookedProjectileDive(dive.HoverHeight, dive.MaxRiseTicks, dive.MarkTicks,
                    dive.DiveSpeed, dive.NoTargetDistance) : null,
                perch.Peck is { } peck ? new CookedProjectilePeck(peck.IntervalTicks, peck.Damage, peck.PresentationId) : null,
                perch.ScatterPresentationId);

        /// <summary>Uses a field runtime API 1.21.0 added: the perch's dive, peck or scatter.</summary>
        internal static bool RequiresRuntimeApi121(CookedProjectilePerch? perch)
            => perch != null && (perch.Dive != null || perch.Peck != null || perch.ScatterPresentationId.Length > 0);

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
            if (projectile.Homing != null && perch.Dive != null)
                return "A perch with a dive block commits to its line: remove its homing block.";
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
            if (perch.Peck is { } peck)
            {
                if (peck.IntervalTicks == 0 || peck.IntervalTicks > perch.PerchTicks)
                    return "perch.peck.intervalTicks must be 1 to perch.perchTicks.";
                if (!float.IsFinite(peck.Damage) || !(peck.Damage >= 1f) || peck.Damage > MaxPeckDamage
                    || peck.Damage != MathF.Floor(peck.Damage))
                    return "perch.peck.damage must be a whole number from 1 to 20.";
            }
            if (perch.Dive is not { } dive) return null;
            if (projectile.Gravity != 0f) return "A diving perch flies without gravity.";
            if (!(projectile.Speed > 0f && projectile.Speed <= MaxDiveSpeed))
                return "A perching projectile rises at its speed: above 0 and at most 60.";
            if (!float.IsFinite(dive.HoverHeight) || !(dive.HoverHeight > 0f) || dive.HoverHeight > MaxHoverHeight)
                return "perch.dive.hoverHeight must be above 0 and at most 12.";
            if (dive.MaxRiseTicks == 0 || dive.MaxRiseTicks > MaxDivePhaseTicks) return "perch.dive.maxRiseTicks must be 1 to 240.";
            if (dive.MarkTicks == 0 || dive.MarkTicks > MaxDivePhaseTicks) return "perch.dive.markTicks must be 1 to 240.";
            if (!float.IsFinite(dive.DiveSpeed) || !(dive.DiveSpeed > 0f) || dive.DiveSpeed > MaxDiveSpeed)
                return "perch.dive.diveSpeed must be above 0 and at most 60.";
            if (!float.IsFinite(dive.NoTargetDistance) || dive.NoTargetDistance < 0f || dive.NoTargetDistance > MaxNoTargetDistance)
                return "perch.dive.noTargetDistance must be 0 to 20.";
            if (projectile.MaxFlightTicks <= dive.MaxRiseTicks + dive.MarkTicks)
                return "A diving perch's maxFlightTicks must outlast its rise and mark, leaving time to dive.";
            return null;
        }
    }
}
