namespace SlopArena.Shared;

/// <summary>One ground locomotion rule for every fighter and simulation at 60 Hz.</summary>
public static class GroundSpeedTuning
{
    public const float StartRatio = 0.6f;
    public const ushort RampTicks = 12;
    public const ushort StopTicks = 20;
}
