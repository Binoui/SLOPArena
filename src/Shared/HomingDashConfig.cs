namespace SlopArena.Shared;

/// <summary>
/// Universal homing dash (ADR-0042) at 60 Hz. A startup float, then a dash that turns toward
/// the target at a limited rate. Arrival opens a short window any action can cancel; running
/// out of travel is a miss with a punishable end. Values are first-pass Training numbers.
/// </summary>
public static class HomingDashConfig
{
    public const ushort CooldownTicks = 180;
    public const ushort FloatTicks = 12;
    public const float Speed = 24f;
    /// <summary>Travel cap: <see cref="Speed"/> × this many ticks is the dash range (16 m).</summary>
    public const ushort MaxTravelTicks = 40;
    /// <summary>Gap between the two capsules at which the dash counts as arrived.</summary>
    public const float ArrivalGap = 0.5f;
    public const ushort ArriveWindowTicks = 14;
    public const ushort MissRecoveryTicks = 30;
    // Turn limit per tick, 4° (240°/s). Literal cosine and sine keep the steering free of
    // runtime trigonometry, so server and client predictions stay bit-identical.
    public const float TurnCos = 0.99756405f;
    public const float TurnSin = 0.06975647f;
}
