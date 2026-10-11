namespace SlopArena.Shared;

/// <summary>Deterministic, shared initial tuning for defense actions (60 Hz simulation ticks).</summary>
public static class DefenseConfig
{
    public const ushort ShieldDropTicks = 7;
    public const ushort GrabStartupTicks = 7;
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
    public const ushort AirDodgeInvulnerabilityTicks = 5;
    public const ushort AirDodgeMovementTicks = 10;
    public const ushort AirDodgeRecoveryTicks = 20;
}
