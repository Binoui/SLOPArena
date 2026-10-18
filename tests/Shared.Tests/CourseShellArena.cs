using System.Text.Json;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Builds an arena from a stage shell document (<c>layout.py --unity-shell</c>) the way
/// <c>sloparena.stage.bake</c> does for a solo Arcade source: collision triangles as written,
/// bounds and kill planes from the meshes, a 0.5 m heightmap of upward faces, one start and the
/// compiled Character Course. Route tests use it to calibrate a layout offline before the real
/// bake; the shipped route tests still load <c>data/arenas/&lt;key&gt;.arena</c>.
/// </summary>
internal static class CourseShellArena
{
    private const float CellSize = .5f;

    public static ArenaDefinition FromShellFile(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var triangles = new List<CollisionTriangle>();
        foreach (var mesh in root.GetProperty("meshes").EnumerateArray())
        {
            var vertices = mesh.GetProperty("vertices").EnumerateArray().Select(Point).ToArray();
            var indices = mesh.GetProperty("triangles").EnumerateArray().Select(x => x.GetInt32()).ToArray();
            for (int i = 0; i < indices.Length; i += 3)
                triangles.Add(Triangle(vertices[indices[i]], vertices[indices[i + 1]], vertices[indices[i + 2]]));
        }
        var spawn = root.GetProperty("spawns")[0];
        var start = Point(spawn.GetProperty("position"));
        var arena = Build(root.GetProperty("stage").GetString()!, triangles,
            new SpawnPoint { X = start.X, Y = start.Y, Z = start.Z, Yaw = Radians(spawn.GetProperty("yaw")) },
            root.GetProperty("killY").GetSingle());
        if (root.TryGetProperty("course", out var course))
            arena.Arcade = Course(course);
        arena.Arcade?.Course?.ValidateArena(in arena);
        return arena;
    }

    /// <summary>Closed axis-aligned boxes (x0, y0, z0, x1, y1, z1); the kill plane is their lowest base.</summary>
    public static ArenaDefinition FromBoxes(IEnumerable<(float x0, float y0, float z0, float x1, float y1, float z1)> boxes,
        SpawnPoint start, CharacterCourseDefinition? course = null, string profile = "probe")
    {
        var triangles = new List<CollisionTriangle>();
        float killY = float.MaxValue;
        foreach (var b in boxes) { triangles.AddRange(Box(b)); killY = MathF.Min(killY, b.y0); }
        var arena = Build("probe", triangles, start, killY);
        if (course != null) arena.Arcade = ArcadeStageDefinition.ForCourse(profile, 1, new[] { "fightguy" }, course);
        return arena;
    }

    private static ArenaDefinition Build(string name, List<CollisionTriangle> triangles, SpawnPoint start, float killY)
    {
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var t in triangles)
            foreach (var (x, y, z) in new[] { (t.AX, t.AY, t.AZ), (t.BX, t.BY, t.BZ), (t.CX, t.CY, t.CZ) })
            {
                minX = MathF.Min(minX, x); maxX = MathF.Max(maxX, x); minY = MathF.Min(minY, y);
                minZ = MathF.Min(minZ, z); maxZ = MathF.Max(maxZ, z);
            }
        if (MathF.Abs(killY - minY) > .001f) throw new ArgumentException($"The kill plane {killY} must be the lowest vertex {minY}.");
        int width = (int)MathF.Ceiling((maxX - minX) / CellSize) + 1, height = (int)MathF.Ceiling((maxZ - minZ) / CellSize) + 1;
        var data = Enumerable.Repeat(float.MinValue, width * height).ToArray();
        foreach (var t in triangles) Rasterize(t, minX, minZ, width, height, data);
        float highest = data.Where(x => x > float.MinValue / 2f).DefaultIfEmpty(killY + 10f).Max();
        var arena = new ArenaDefinition
        {
            Name = name, DisplayName = name, PreviewColor = "#24374d",
            KillHeight = killY, KillTop = highest + ArenaCollision.TopBlastMargin,
            KillMinX = minX - ArenaCollision.SideBlastMargin, KillMaxX = maxX + ArenaCollision.SideBlastMargin,
            KillMinZ = minZ - ArenaCollision.SideBlastMargin, KillMaxZ = maxZ + ArenaCollision.SideBlastMargin,
            MinX = minX, MaxX = maxX, MinZ = minZ, MaxZ = maxZ,
            SpawnPoints = new[] { start }, CollisionTriangles = triangles.ToArray(),
            Heightmap = new ArenaHeightmap { Width = width, Height = height, CellSize = CellSize, OriginX = minX, OriginZ = minZ, Data = data },
        };
        arena.SpatialGrid = ArenaCollision.BuildSpatialGrid(in arena);
        return arena;
    }

    private static ArcadeStageDefinition Course(JsonElement c)
    {
        var targets = c.GetProperty("targets").EnumerateArray().Select(t => new CourseTargetDefinition(
            t.GetProperty("id").GetString()!, Arcade(t.GetProperty("position")), t.GetProperty("radius").GetSingle(),
            t.TryGetProperty("hits", out var hits) ? (byte)hits.GetInt32() : (byte)1)).ToArray();
        var checkpoints = c.GetProperty("checkpoints").EnumerateArray().Select(x => new CourseCheckpointDefinition(
            x.GetProperty("id").GetString()!, new(Arcade(x.GetProperty("centre")), Arcade(x.GetProperty("halfSize"))),
            Arcade(x.GetProperty("respawn")), Radians(x.GetProperty("yaw")), (ushort)x.GetProperty("requiredTargets").GetInt32())).ToArray();
        var finish = c.GetProperty("finish");
        return ArcadeStageDefinition.ForCourse(c.GetProperty("profileId").GetString()!, c.GetProperty("gameplayRevision").GetUInt32(),
            c.GetProperty("allowedPackageIds").EnumerateArray().Select(x => x.GetString()!).ToArray(),
            new CharacterCourseDefinition(targets, checkpoints, new(Arcade(finish.GetProperty("centre")), Arcade(finish.GetProperty("halfSize")))));
    }

    /// <summary>Unity stores yaw in degrees; the bake keeps eulerAngles.y (0–360) in radians.</summary>
    private static float Radians(JsonElement degrees)
    {
        float d = degrees.GetSingle() % 360f;
        return (d < 0f ? d + 360f : d) * MathF.PI / 180f;
    }

    private static (float X, float Y, float Z) Point(JsonElement v) => (v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle());
    private static ArcadePoint Arcade(JsonElement v) => new(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle());
    private static CollisionTriangle Triangle((float X, float Y, float Z) a, (float X, float Y, float Z) b, (float X, float Y, float Z) c)
        => new() { AX = a.X, AY = a.Y, AZ = a.Z, BX = b.X, BY = b.Y, BZ = b.Z, CX = c.X, CY = c.Y, CZ = c.Z };

    /// <summary>Twelve outward triangles, wound like stagekit's layout boxes.</summary>
    private static IEnumerable<CollisionTriangle> Box((float x0, float y0, float z0, float x1, float y1, float z1) b)
    {
        (float, float, float) V(int i, int j, int k) => (i == 1 ? b.x1 : b.x0, j == 1 ? b.y1 : b.y0, k == 1 ? b.z1 : b.z0);
        int[][][] quads =
        {
            new[] { new[] { 0, 1, 0 }, new[] { 0, 1, 1 }, new[] { 1, 1, 1 }, new[] { 1, 1, 0 } },
            new[] { new[] { 0, 0, 0 }, new[] { 1, 0, 0 }, new[] { 1, 0, 1 }, new[] { 0, 0, 1 } },
            new[] { new[] { 1, 0, 0 }, new[] { 1, 1, 0 }, new[] { 1, 1, 1 }, new[] { 1, 0, 1 } },
            new[] { new[] { 0, 0, 0 }, new[] { 0, 0, 1 }, new[] { 0, 1, 1 }, new[] { 0, 1, 0 } },
            new[] { new[] { 0, 0, 1 }, new[] { 1, 0, 1 }, new[] { 1, 1, 1 }, new[] { 0, 1, 1 } },
            new[] { new[] { 0, 0, 0 }, new[] { 0, 1, 0 }, new[] { 1, 1, 0 }, new[] { 1, 0, 0 } },
        };
        foreach (var q in quads)
        {
            var p = q.Select(v => V(v[0], v[1], v[2])).ToArray();
            yield return Triangle(p[0], p[1], p[2]);
            yield return Triangle(p[0], p[2], p[3]);
        }
    }

    private static void Rasterize(CollisionTriangle t, float minX, float minZ, int width, int height, float[] data)
    {
        var a = new System.Numerics.Vector3(t.AX, t.AY, t.AZ);
        var b = new System.Numerics.Vector3(t.BX, t.BY, t.BZ);
        var c = new System.Numerics.Vector3(t.CX, t.CY, t.CZ);
        var normal = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(b - a, c - a));
        if (normal.Y <= .001f) return;   // Arcade bakes upward faces only
        int x0 = Math.Max(0, (int)MathF.Floor((MathF.Min(a.X, MathF.Min(b.X, c.X)) - minX) / CellSize));
        int x1 = Math.Min(width - 1, (int)MathF.Floor((MathF.Max(a.X, MathF.Max(b.X, c.X)) - minX) / CellSize));
        int z0 = Math.Max(0, (int)MathF.Floor((MathF.Min(a.Z, MathF.Min(b.Z, c.Z)) - minZ) / CellSize));
        int z1 = Math.Min(height - 1, (int)MathF.Floor((MathF.Max(a.Z, MathF.Max(b.Z, c.Z)) - minZ) / CellSize));
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                float wx = minX + (x + .5f) * CellSize, wz = minZ + (z + .5f) * CellSize;
                if (!InTriangle(a, b, c, wx, wz)) continue;
                float y = a.Y - (normal.X * (wx - a.X) + normal.Z * (wz - a.Z)) / normal.Y;
                int index = z * width + x;
                if (y > data[index]) data[index] = y;
            }
    }

    private static bool InTriangle(System.Numerics.Vector3 a, System.Numerics.Vector3 b, System.Numerics.Vector3 c, float x, float z)
    {
        float Sign(System.Numerics.Vector3 p, System.Numerics.Vector3 q) => (x - q.X) * (p.Z - q.Z) - (p.X - q.X) * (z - q.Z);
        float d1 = Sign(a, b), d2 = Sign(b, c), d3 = Sign(c, a);
        bool negative = d1 < 0 || d2 < 0 || d3 < 0, positive = d1 > 0 || d2 > 0 || d3 > 0;
        return !(negative && positive);
    }
}
