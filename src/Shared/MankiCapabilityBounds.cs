using System;
using System.Linq;

namespace SlopArena.Shared;

internal static class MankiCapabilityBounds
{
    internal static string? Error(CookedCapabilityParameters parameters) => parameters switch
    {
        CookedMankiRoundBombCapabilityParameters q when
            !new[] { q.MaxRange, q.LaunchAngle, q.Gravity, q.HitboxRadius, q.Damage, q.KbAngle,
                q.ExplosionDamage, q.ExplosionRadius, q.ExplosionKbBase, q.ExplosionKbGrowth,
                q.ExplosionKbAngle, q.EarlyDamage, q.EarlyRadius, q.ImpactRadius }.All(float.IsFinite)
            || q.ThrowTriggerTick is 0 or > 60 || q.MaxFlightTicks is 0 or > 600
            || q.MaxRange is <= 0 or > 60 || q.LaunchAngle is <= 0 or >= 90 || q.Gravity is <= 0 or > 120
            || q.HitboxRadius is <= 0 or > 4 || q.ImpactRadius is <= 0 or > 8
            || q.Damage is < 0 or > 100 || q.ExplosionDamage is < 0 or > 100
            || q.EarlyDamage <= q.ExplosionDamage || q.EarlyDamage > 100
            || q.ExplosionRadius is <= 0 or > 12 || q.EarlyRadius <= q.ExplosionRadius || q.EarlyRadius > 16
            || q.ExplosionKbBase is < 0 or > 60 || q.ExplosionKbGrowth is < 0 or > 100
            || MathF.Abs(q.ExplosionKbAngle) > 90 || MathF.Abs(q.KbAngle) > 90
            || q.FuseTicks is 0 or > 1800 || q.WarningTicks is 0 or > 60 || q.WarningTicks >= q.FuseTicks
            || q.ExplosionDurationTicks is 0 or > 60 || q.ExplosionStunTicks > 240 || q.StunTicks > 240
            => "Can Bomb requires bounded launch/impact/blast values, a positive fuse and warning, and a larger early blast.",
        CookedMankiJetpackBoostCapabilityParameters e when
            !new[] { e.VerticalSpeed, e.HorizontalSpeed, e.SinkSpeed, e.JumpBurstSpeed, e.ExhaustRadius,
                e.ExplosionRadius, e.ExplosionDamage, e.ExplosionKbAngle, e.ExplosionKbBase, e.ExplosionKbGrowth }.All(float.IsFinite)
            || e.StartupTicks is 0 or > 120 || e.FuelTicks is 0 or > 600
            || e.VerticalSpeed is <= 0 or > 30 || e.HorizontalSpeed is <= 0 or > 40
            || e.SinkSpeed is <= 0 or > 10 || e.JumpBurstSpeed is <= 0 or > 30
            || e.JumpBurstTicks is 0 or > 60 || e.JumpBurstTicks > e.FuelTicks
            || e.ExhaustRadius is <= 0 or > 4 || e.ExplosionRadius is <= 0 or > 8
            || e.ExplosionDamage is < 0 or > 100 || MathF.Abs(e.ExplosionKbAngle) > 90
            || e.ExplosionKbBase is < 0 or > 60 || e.ExplosionKbGrowth is < 0 or > 100
            || e.ExplosionDurationTicks is 0 or > 60 || e.ExplosionStunTicks > 240
            => "Jetpack flight requires bounded ignition, fuel, movement, jump-burst and exhaust values.",
        _ => null,
    };
}
