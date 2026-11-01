namespace SlopArena.Shared;

public readonly record struct ProjectileLegHit(float Damage, float Angle, float BaseKnockback, float KnockbackGrowth,
    ushort StunTicks, ushort FixedHitstunTicks = 0);

public readonly record struct Challenge(ulong TargetId, byte TargetDeaths, uint UntilTick);
