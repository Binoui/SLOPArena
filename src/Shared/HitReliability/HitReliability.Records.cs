using System;
using System.Collections.Generic;

namespace SlopArena.Shared;

/// <summary>One normal's measurements against standing roster targets.</summary>
public sealed class MoveReliability
{
    public string Slot = "";
    public string Name = "";
    public bool Air;
    public ushort DurationTicks;
    public int BakedFrames;
    /// <summary>Baked 60 Hz animation frames played per gameplay tick (1 = authored speed).</summary>
    public float PlaybackSpeed;
    public int ActiveTicks;
    /// <summary>Active ticks whose hitboxes touch a standing medium target <see cref="HitReliability.ContactProbeMeters"/> ahead.</summary>
    public int ContactTicksAtProbe;
    public List<ReachBand> Reach = new();
    public List<VerticalBand> Vertical = new();
    public List<SamplingRow> Sampling = new();
    /// <summary>Ground moves with several damaging hitboxes: where around the attacker each one can land.</summary>
    public List<HitCoverage> Hits = new();
    /// <summary>Ground moves: where in front of, above and behind the attacker an idle target is touched.</summary>
    public List<ArcMap> Arc = new();
    /// <summary>Each active tick's tested hitboxes in the grounded origin frame: tick, start xyz, end xyz, radius
    /// (metres; x right, y up from the floor, z ahead).</summary>
    public List<float[]> ActiveHitboxes = new();
    public List<string> Flags = new();
}

/// <summary>
/// The side view of a ground move's reach (owner, 2026-10-22: for weapon normals the arc is the most important part):
/// for each target feet height (rows, highest first) and distance along the attacker's facing (columns, behind to
/// ahead), whether any active tick touches the idle target placed there, from the cooked hitbox geometry with the
/// attacker fixed (as <see cref="VerticalBand"/>). '#' touched, '.' not.
/// </summary>
public sealed class ArcMap
{
    public string Target = "";
    public List<float> Distances = new();
    public List<float> Heights = new();
    public List<string> Rows = new();
}

/// <summary>Contiguous starting distances (metres) at which a ground normal connects with an idle target.</summary>
public sealed class ReachBand
{
    public string Target = "";
    public float BearingDegrees;
    public List<(float From, float To)> Intervals = new();
    public float? Nearest => Intervals.Count == 0 ? null : Intervals[0].From;
    public float? Farthest => Intervals.Count == 0 ? null : Intervals[^1].To;
}

/// <summary>Target feet heights, relative to the attacker's feet, that an aerial's active ticks can touch.</summary>
public sealed class VerticalBand
{
    public string Target = "";
    public float? Lowest;
    public float? Highest;
}

/// <summary>Positions a bone or weapon hitbox visibly sweeps between ticks but never tests.</summary>
public sealed class SamplingRow
{
    public int Event;
    public string Anchor = "";
    public float Radius;
    public float MaxTipStepPerTick;
    public float TargetFeetHeight;
    public int SweptPositions;
    public int TestedPositions;
    public double LostShare => SweptPositions == 0 ? 0 : (SweptPositions - TestedPositions) / (double)SweptPositions;
}

/// <summary>Bearings around the attacker at which one hit of a multi-hit ground move touches a standing target.</summary>
public sealed class HitCoverage
{
    public int Event;
    public int FirstTick;
    public int LastTick;
    public float Damage;
    public float Radius;
    /// <summary>Per target and distance: bearings (of <see cref="HitReliability.CoverageBearings"/>) any tested pose touches.</summary>
    public List<(string Target, float Distance, int Bearings)> Touched = new();
}

