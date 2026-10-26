using System;

namespace SlopArena.Shared;

public enum GroundSpeedModel { Current, Tiers, Progressive, Hybrid }
public enum GroundSpeedCurve { Linear, SlowStart }

/// <summary>Training prototype settings. No new snapshot state or online content contract.</summary>
public sealed record GroundSpeedTuning
{
    public GroundSpeedModel Model { get; }
    public float WalkRatio { get; }
    public float TiltThreshold { get; }
    public ushort WalkRampTicks { get; }
    public ushort RunRampTicks { get; }
    public ushort WalkStopTicks { get; }
    public ushort RampTicks { get; }
    public GroundSpeedCurve Curve { get; }
    public float TurnaroundKeep { get; }
    public ushort StopTicks { get; }
    public float StartRatio { get; }

    private GroundSpeedTuning(GroundSpeedModel model, float walkRatio, float tiltThreshold,
        ushort walkRampTicks, ushort runRampTicks, ushort walkStopTicks,
        ushort rampTicks = 20, GroundSpeedCurve curve = GroundSpeedCurve.Linear,
        float turnaroundKeep = 1f, ushort stopTicks = 20, float startRatio = 0.4f)
    {
        Model = model;
        WalkRatio = walkRatio;
        TiltThreshold = tiltThreshold;
        WalkRampTicks = walkRampTicks;
        RunRampTicks = runRampTicks;
        WalkStopTicks = walkStopTicks;
        RampTicks = rampTicks;
        Curve = curve;
        TurnaroundKeep = turnaroundKeep;
        StopTicks = stopTicks;
        StartRatio = startRatio;
    }

    private static readonly GroundSpeedTuning[] Profiles =
    {
        new(GroundSpeedModel.Current,     0.4f, 0.7f, 1,  1, 3),
        new(GroundSpeedModel.Tiers,       0.4f, 0.7f, 1,  1, 3),
        new(GroundSpeedModel.Progressive, 0.4f, 0.7f, 1, 20, 3),
        new(GroundSpeedModel.Hybrid,      0.4f, 0.7f, 3,  6, 3),
    };

    public static GroundSpeedTuning Default => Profiles[0];

    public static GroundSpeedTuning Progressive(int rampTicks = 20,
        GroundSpeedCurve curve = GroundSpeedCurve.Linear, float turnaroundKeep = 1f, int stopTicks = 20,
        float startRatio = 0.4f)
    {
        if (rampTicks < 6 || rampTicks > 40) throw new ArgumentOutOfRangeException(nameof(rampTicks));
        if (curve != GroundSpeedCurve.Linear && curve != GroundSpeedCurve.SlowStart)
            throw new ArgumentOutOfRangeException(nameof(curve));
        if (turnaroundKeep != 0f && turnaroundKeep != 0.25f && turnaroundKeep != 0.5f
            && turnaroundKeep != 0.75f && turnaroundKeep != 1f)
            throw new ArgumentOutOfRangeException(nameof(turnaroundKeep));
        if (stopTicks < 4 || stopTicks > 30) throw new ArgumentOutOfRangeException(nameof(stopTicks));
        if (startRatio != 0f && startRatio != 0.2f && startRatio != 0.3f && startRatio != 0.4f
            && startRatio != 0.5f && startRatio != 0.6f)
            throw new ArgumentOutOfRangeException(nameof(startRatio));
        return new GroundSpeedTuning(GroundSpeedModel.Progressive, 0.4f, 0.7f, 1, (ushort)rampTicks, 3,
            (ushort)rampTicks, curve, turnaroundKeep, (ushort)stopTicks, startRatio);
    }

    public static GroundSpeedTuning For(GroundSpeedModel model)
        => (uint)model < Profiles.Length ? Profiles[(int)model]
            : throw new ArgumentOutOfRangeException(nameof(model));
}
