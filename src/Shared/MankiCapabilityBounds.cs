using System;
using System.Linq;

namespace SlopArena.Shared;

internal static class MankiCapabilityBounds
{
    internal static string? Error(CookedCapabilityParameters parameters) => parameters switch
    {
        CookedMankiRoundBombCapabilityParameters q when
            !new[] { q.Gravity, q.HitboxRadius, q.TriggerRadius, q.ExplosionDamage,
                q.ExplosionRadius, q.ExplosionKbBase, q.ExplosionKbGrowth, q.ExplosionKbAngle }.All(float.IsFinite)
            || q.ArmTicks is 0 or > 600 || q.MaxFlightTicks is 0 or > 600
            || q.Gravity is <= 0 or > 120 || q.HitboxRadius is <= 0 or > 4
            || q.TriggerRadius is <= 0 or > 12 || q.WarningTicks is 0 or > 120
            || q.LifetimeTicks is 0 or > 3600 || q.MaxMines is 0 or > 3
            || !ValidBlast(q.ExplosionDamage, q.ExplosionRadius, q.ExplosionKbBase, q.ExplosionKbGrowth,
                q.ExplosionKbAngle, q.ExplosionStunTicks, q.ExplosionDurationTicks, 0f)
            => "Proximity Mines requires bounded drop, arming, trigger, lifetime, cap and blast values.",
        CookedMankiJetpackBoostCapabilityParameters e when
            !new[] { e.VerticalSpeed, e.HorizontalSpeed, e.SinkSpeed, e.JumpBurstSpeed,
                e.ExplosionRadius, e.ExplosionDamage, e.ExplosionKbAngle, e.ExplosionKbBase, e.ExplosionKbGrowth }.All(float.IsFinite)
            || e.StartupTicks is 0 or > 120 || e.FuelTicks is 0 or > 600
            || e.VerticalSpeed is <= 0 or > 30 || e.HorizontalSpeed is <= 0 or > 40
            || e.SinkSpeed is <= 0 or > 10 || e.JumpBurstSpeed is <= 0 or > 30
            || e.JumpBurstTicks is 0 or > 60 || e.JumpBurstTicks > e.FuelTicks
            || !ValidBlast(e.ExplosionDamage, e.ExplosionRadius, e.ExplosionKbBase, e.ExplosionKbGrowth,
                e.ExplosionKbAngle, e.ExplosionStunTicks, e.ExplosionDurationTicks, 0f)
            => "Jetpack requires bounded ignition, fuel, steering and jump-burst values.",
        CookedMankiKamikazeCapabilityParameters f when
            !new[] { f.Speed, f.ContactRadius, f.ExplosionDamage, f.ExplosionRadius,
                f.ExplosionKbBase, f.ExplosionKbGrowth, f.ExplosionKbAngle, f.SelfPushSpeed }.All(float.IsFinite)
            || f.RunTicks is 0 or > 180 || f.Speed is <= 0 or > 60 || f.ContactRadius is <= 0 or > 4
            || !ValidBlast(f.ExplosionDamage, f.ExplosionRadius, f.ExplosionKbBase, f.ExplosionKbGrowth,
                f.ExplosionKbAngle, f.ExplosionStunTicks, f.ExplosionDurationTicks, f.SelfPushSpeed)
            => "Kamikaze requires bounded run, contact and blast values.",
        _ => null,
    };

    private static bool ValidBlast(float damage, float radius, float kbBase, float growth, float angle,
        ushort stun, ushort duration, float selfPush)
        => damage is >= 0 and <= 100 && radius is > 0 and <= 12 && kbBase is >= 0 and <= 60
            && growth is >= 0 and <= 100 && MathF.Abs(angle) <= 90 && stun <= 240
            && duration is > 0 and <= 60 && selfPush is >= 0 and <= 40;
}
