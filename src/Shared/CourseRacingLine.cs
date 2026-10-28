using System;
using System.Collections.Generic;

namespace SlopArena.Shared;

/// <summary>Immutable route geometry. Progress is distance along the line, in metres.</summary>
public sealed class CourseRacingLine
{
    public const int MaxPoints = 64;
    public const float LocalWindow = 12f, ShortcutSnapDistance = 1.25f, EndpointTolerance = 3f;
    public IReadOnlyList<ArcadePoint> Points { get; }
    public float Length => _distances[_distances.Length - 1];
    private readonly float[] _distances;

    public CourseRacingLine(IReadOnlyList<ArcadePoint> points)
    {
        if (points == null || points.Count is < 2 or > MaxPoints)
            throw new ArgumentException("A racing line needs 2–64 points.");
        var copy = new ArcadePoint[points.Count];
        _distances = new float[points.Count];
        for (int i = 0; i < copy.Length; i++)
        {
            var p = copy[i] = points[i];
            if (!p.IsFinite || MathF.Abs(p.X) > 10000 || MathF.Abs(p.Y) > 10000 || MathF.Abs(p.Z) > 10000)
                throw new ArgumentException("Racing line points must be finite and bounded.");
            if (i == 0) continue;
            float length = MathF.Sqrt(DistanceSquared(copy[i - 1], p));
            if (length < .01f) throw new ArgumentException("Racing line segments must have length.");
            _distances[i] = _distances[i - 1] + length;
        }
        Points = Array.AsReadOnly(copy);
    }

    public float Project(ArcadePoint position, float previous)
    {
        float progress = previous, distance = float.PositiveInfinity;
        float low = MathF.Max(0, previous - LocalWindow), high = MathF.Min(Length, previous + LocalWindow);
        for (int i = 0; i < Points.Count - 1; i++)
        {
            if (_distances[i + 1] < low || _distances[i] > high) continue;
            var candidate = Projection(i, position, MathF.Max(low, _distances[i]), MathF.Min(high, _distances[i + 1]));
            float change = MathF.Abs(candidate.Progress - previous), bestChange = MathF.Abs(progress - previous);
            if (candidate.Distance < distance - .0001f || MathF.Abs(candidate.Distance - distance) <= .0001f
                && (change < bestChange - .001f || MathF.Abs(change - bestChange) <= .001f && candidate.Progress > progress))
            { progress = candidate.Progress; distance = candidate.Distance; }
        }
        float localDistance = distance;
        for (int i = 0; i < Points.Count - 1; i++)
        {
            if (_distances[i + 1] <= high) continue;
            var candidate = Projection(i, position, _distances[i], _distances[i + 1]);
            // Equal-distance crossings stay on the local branch. A shortcut must land closer.
            if (candidate.Progress > high && candidate.Distance <= ShortcutSnapDistance * ShortcutSnapDistance
                && candidate.Distance + .25f < localDistance && candidate.Distance < distance)
            { progress = candidate.Progress; distance = candidate.Distance; }
        }
        return progress;
    }

    private (float Progress, float Distance) Projection(int i, ArcadePoint p, float low, float high)
    {
        var a = Points[i]; var b = Points[i + 1];
        float x = b.X - a.X, y = b.Y - a.Y, z = b.Z - a.Z;
        float length = _distances[i + 1] - _distances[i];
        float offset = ((p.X - a.X) * x + (p.Y - a.Y) * y + (p.Z - a.Z) * z) / length;
        float progress = Math.Clamp(_distances[i] + offset, low, high);
        float t = (progress - _distances[i]) / length;
        return (progress, DistanceSquared(p, new(a.X + t * x, a.Y + t * y, a.Z + t * z)));
    }

    internal static float DistanceSquared(ArcadePoint a, ArcadePoint b)
    { float x = a.X - b.X, y = a.Y - b.Y, z = a.Z - b.Z; return x * x + y * y + z * z; }
}
