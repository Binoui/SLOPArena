using System;

namespace SlopArena.Shared;

/// <summary>Immutable knockback and hitstun settings for one simulation.</summary>
public sealed class CombatTuning
{
    /// <summary>Scales damage-formula launch velocity, after hitstun is calculated.</summary>
    public float KnockbackScale { get; }
    /// <summary>Hitstun ticks per unit of unscaled launch magnitude.</summary>
    public float HitstunCoefficient { get; }
    /// <summary>Additional magnitude used for damage-formula hitstun; fixed-force launches omit it.</summary>
    public float HitstunMagnitudeBonus { get; }

    public CombatTuning(float knockbackScale = 0.17f, float hitstunCoefficient = 0.45f,
        float hitstunMagnitudeBonus = 0f)
    {
        if (float.IsNaN(knockbackScale) || float.IsInfinity(knockbackScale) || knockbackScale <= 0f)
            throw new ArgumentOutOfRangeException(nameof(knockbackScale));
        if (float.IsNaN(hitstunCoefficient) || float.IsInfinity(hitstunCoefficient) || hitstunCoefficient <= 0f)
            throw new ArgumentOutOfRangeException(nameof(hitstunCoefficient));
        if (float.IsNaN(hitstunMagnitudeBonus) || float.IsInfinity(hitstunMagnitudeBonus) || hitstunMagnitudeBonus < 0f)
            throw new ArgumentOutOfRangeException(nameof(hitstunMagnitudeBonus));
        KnockbackScale = knockbackScale;
        HitstunCoefficient = hitstunCoefficient;
        HitstunMagnitudeBonus = hitstunMagnitudeBonus;
    }

    /// <summary>Shipped melee-soft tuning: stun 0.45 × magnitude, launch velocity × 0.17.</summary>
    public static readonly CombatTuning Default = new();
}
