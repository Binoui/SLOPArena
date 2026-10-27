namespace SlopArena.Shared;

/// <summary>One ground locomotion rule for every fighter and simulation at 60 Hz.</summary>
public static class GroundSpeedTuning
{
    public const float StartRatio = 0.4f;
    public const ushort RampTicks = 20;
    public const ushort StopTicks = 20;
}
