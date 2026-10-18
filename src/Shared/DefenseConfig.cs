namespace SlopArena.Shared;

/// <summary>Deterministic, shared initial tuning for defense actions (60 Hz simulation ticks).</summary>
public static class DefenseConfig
{
    public const ushort ShieldDropTicks = 7;
    public const ushort GrabStartupTicks = 7;
    // Carry incoming ground momentum briefly, then stop before grab becomes active.
    public const ushort GrabBrakeTicks = 6;
    public const ushort GrabActiveTicks = 3;
    // grab.fbx is 23 frames at 30 fps; retain the contact window and finish the clip on a whiff.
    public const ushort GrabWhiffRecoveryTicks = 36;
    public const ushort GrabClashRecoveryTicks = 10;
    public const ushort GrabHoldTicks = 180;
    public const float GrabWalkSpeedRatio = 0.25f;
    public const float GrabTurnDegreesPerSecond = 60f;
    // Both throw clips share a 30 fps clock: release at frame 11, landing at frame 34.
    public const ushort ThrowReleaseTicks = 22;
    public const ushort ThrowAnimationTicks = 58;
    public const ushort ThrowAttackerRecoveryTicks = ThrowAnimationTicks - ThrowReleaseTicks;
    public const ushort ThrownLandingTicks = 68;
    public const ushort ThrownAnimationTicks = 84;
    public const ushort ThrownRecoveryTicks = ThrownAnimationTicks - ThrownLandingTicks;
    public const ushort AirDodgeInvulnerabilityTicks = 8;
    public const ushort AirDodgeMovementTicks = 12;
    public const ushort AirDodgeRecoveryTicks = 14;
    // A landing caps the remaining commitment, like an aerial's landing lag.
    public const ushort AirDodgeLandingTicks = 10;
    // The burst starts at this multiple of the fighter's airDodgeSpeed and keeps
    // AirDodgeSpeedRetention of its speed each tick, ending near air-drift speed.
    public const float AirDodgeLaunchScale = 2.3f;
    public const float AirDodgeSpeedRetention = 0.9f;
}
