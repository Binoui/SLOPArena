using System.Numerics;
using System.Text.Json;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class CourseRacingLineGenerator
{
    // SLOPARENA_GENERATE_RACING_LINES=1 dotnet test tests/Shared.Tests -p:SkipUnityPluginCopy=true --filter FullyQualifiedName~CourseRacingLineGenerator
    [Fact]
    public void GenerateFromVerifiedRoutesWhenRequested()
    {
        if (Environment.GetEnvironmentVariable("SLOPARENA_GENERATE_RACING_LINES") != "1") return;
        foreach (var route in CourseFinishability.Routes)
        {
            var driver = new CourseFinishability(route.ArenaKey, CharacterClass.FightGuy);
            driver.Run(route);
            var points = new List<Vector3>();
            foreach (var p in driver.RoutePoints)
            {
                var point = new Vector3(p.X, p.Y, p.Z);
                if (points.Count == 0 || Vector3.DistanceSquared(points[^1], point) > .01f) points.Add(point);
            }
            var finish = driver.Arena.Arcade!.Course!.Finish.Center;
            var end = new Vector3(finish.X, finish.Y, finish.Z);
            if (Vector3.DistanceSquared(points[^1], end) < .01f) points[^1] = end;
            else points.Add(end);
            points = RemoveLoops(points);
            for (float tolerance = .05f; points.Count > CourseRacingLine.MaxPoints; tolerance += .05f)
                points = Simplify(points, tolerance);
            var path = Path.Combine(CourseFinishability.Root, "tools/blender", route.ArenaKey, "racing_line.json");
            File.WriteAllText(path, JsonSerializer.Serialize(points.Select(p => new[] {
                MathF.Round(p.X, 3), MathF.Round(p.Y, 3), MathF.Round(p.Z, 3) }), new JsonSerializerOptions { WriteIndented = true }) + "\n");
        }
    }

    internal static List<Vector3> RemoveLoops(List<Vector3> points)
    {
        const float sampleSpacing = .25f, returnRadius = 1f;
        var path = new List<Vector3> { points[0] };
        for (int segment = 1; segment < points.Count; segment++)
        {
            var start = points[segment - 1]; var end = points[segment];
            int samples = (int)MathF.Ceiling(Vector3.Distance(start, end) / sampleSpacing);
            for (int sample = 1; sample <= samples; sample++)
            {
                var point = Vector3.Lerp(start, end, (float)sample / samples);
                // Sampling catches returns between route waypoints. Full 3D distance keeps stacked climbs.
                for (int earlier = 0; earlier < path.Count - 1; earlier++)
                {
                    if (Vector3.DistanceSquared(path[earlier], point) > returnRadius * returnRadius) continue;
                    path.RemoveRange(earlier + 1, path.Count - earlier - 1);
                    break;
                }
                if (Vector3.DistanceSquared(path[^1], point) > .01f) path.Add(point);
            }
        }
        path[^1] = points[^1];
        return path;
    }

    [Fact]
    public void ReturningBetweenWaypointsErasesTargetExcursion()
    {
        var path = RemoveLoops(new() { new(0, 0, 0), new(6, 0, 0), new(6, 0, 8), new(6.3f, 0, 0), new(12, 0, 0) });
        Assert.Equal(Vector3.Zero, path[0]);
        Assert.Equal(new Vector3(12, 0, 0), path[^1]);
        Assert.All(path, point => Assert.InRange(point.Z, 0, 1f));
    }

    [Fact]
    public void ForwardTurnsAndStackedClimbSurviveLoopRemoval()
    {
        var path = RemoveLoops(new() { new(0, 0, 0), new(8, 0, 0), new(8, 4, 0), new(0, 4, 0), new(0, 4, 8) });
        Assert.Contains(path, point => point.X > 7 && point.Y < 1);
        Assert.Contains(path, point => point.X > 7 && point.Y > 3);
        Assert.Contains(path, point => point.X < 1 && point.Y > 3);
        Assert.Equal(new Vector3(0, 4, 8), path[^1]);
    }

    private static List<Vector3> Simplify(List<Vector3> points, float tolerance)
    {
        int split = -1; float furthest = tolerance * tolerance;
        var a = points[0]; var delta = points[^1] - a;
        for (int i = 1; i < points.Count - 1; i++)
        {
            float t = delta.LengthSquared() == 0 ? 0 : Math.Clamp(Vector3.Dot(points[i] - a, delta) / delta.LengthSquared(), 0, 1);
            float distance = Vector3.DistanceSquared(points[i], a + delta * t);
            if (distance > furthest) { furthest = distance; split = i; }
        }
        if (split < 0) return new() { points[0], points[^1] };
        var left = Simplify(points.GetRange(0, split + 1), tolerance);
        left.RemoveAt(left.Count - 1);
        left.AddRange(Simplify(points.GetRange(split, points.Count - split), tolerance));
        return left;
    }
}
