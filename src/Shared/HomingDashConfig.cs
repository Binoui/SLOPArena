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
    /// <summary>Gap between the two capsules where the dash arrives; the last step shortens to land on it.</summary>
    public const float ArrivalGap = 0.3f;
    /// <summary>
    /// An airborne dash aims its feet this share of a fighter's height above the target's feet, so it
    /// arrives off the floor and the follow-up is an aerial. Measured best of 0–50 % (2026-10-28).
    /// </summary>
    public const float AirAimLift = 0.1f;
    public const ushort ArriveWindowTicks = 14;
    /// <summary>An airborne arrival hangs without gravity this long, so an aerial can start at the target's height.</summary>
    public const ushort ArriveHoverTicks = 20;
    /// <summary>Horizontal glide an airborne arrival keeps along the dash; air friction then stops it.</summary>
    public const float ArriveCarrySpeed = 3f;
    public const ushort MissRecoveryTicks = 30;
    // Turn limit per tick, 4° (240°/s). Literal cosine and sine keep the steering free of
    // runtime trigonometry, so server and client predictions stay bit-identical.
    public const float TurnCos = 0.99756405f;
    public const float TurnSin = 0.06975647f;
}
