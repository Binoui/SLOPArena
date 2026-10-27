using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Manki Mayhem: a bare 24 x 20 deck at y=0, with straight sides down to the KO line.
/// </summary>
public class MankiMayhemArenaTests
{
    private const float KillY = -11f;

    private static readonly CharacterClass[] Roster =
        { CharacterClass.FightGuy, CharacterClass.Manki, CharacterClass.Wibou, CharacterClass.Bonk };

    private static readonly (float X, float Z)[] Floor =
    {
        (-12, -10), (12, -10), (12, 10), (-12, 10)
    };

    private static ArenaDefinition Load()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return ArenaBinaryFormat.LoadFromFile(Path.Combine(root, "data", "arenas", "manki_mayhem.arena"))!.Value;
    }

    [Fact]
    public void MayhemSectionRoundTripsAsArcadeAndStockRefusesIt()
    {
        var arena = Load(); Assert.Equal(ArenaPurpose.Arcade, arena.Purpose);
        var mayhem = Assert.IsType<MankiMayhemDefinition>(arena.Arcade!.Mayhem);
        Assert.Null(arena.Arcade.Ball); Assert.Null(arena.Arcade.Course);
        Assert.Empty(arena.Arcade.AllowedPackageIds); Assert.True(arena.Arcade.AllowsPackage("any-admitted-fighter"));
        Assert.Equal((0f, 6.5f, 41.5f), (mayhem.HostOrigin.X, mayhem.HostOrigin.Y, mayhem.HostOrigin.Z));
        Assert.Equal((-12f, 12f, -10f, 10f, 0f), (mayhem.MinX, mayhem.MaxX, mayhem.MinZ, mayhem.MaxZ, mayhem.DeckTop));
        var section = ArcadeStageBinaryFormat.Serialize(arena.Arcade);
        Assert.Equal((ushort)7, BitConverter.ToUInt16(section, 0));
        Assert.Equal(section, ArcadeStageBinaryFormat.Serialize(ArcadeStageBinaryFormat.Deserialize(section)!));
        Assert.False(MatchFormat.Stock.Admits(arena)); Assert.True(MatchFormat.MankiMayhem.Admits(arena));
        byte[] bytes = ArenaBinaryFormat.Serialize(arena);
        Assert.Equal(bytes, ArenaBinaryFormat.Serialize(ArenaBinaryFormat.Deserialize(bytes)!.Value));
    }

    [Fact]
    public void ExistingSlopballAndCourseArenaSectionsKeepTheirOriginalBytes()
    {
        string root = MankiMayhemTests.Root;
        int balls = 0, courses = 0;
        foreach (string path in Directory.EnumerateFiles(Path.Combine(root, "data/arenas"), "*.arena"))
        {
            byte[] bytes = File.ReadAllBytes(path); var arena = ArenaBinaryFormat.Deserialize(bytes);
            if (arena?.Arcade == null || arena.Value.Arcade.Mayhem != null) continue;
            if (arena.Value.Arcade.Ball != null) balls++;
            if (arena.Value.Arcade.Course != null) courses++;
            Assert.Equal(bytes, ArenaBinaryFormat.Serialize(arena.Value));
        }
        Assert.True(balls > 0); Assert.True(courses > 0);
    }

    [Fact]
    public void BakedShellMatchesTheLockedDeck()
    {
        var arena = Load();
        Assert.Equal((-12f, 12f, -10f, 10f), (arena.MinX, arena.MaxX, arena.MinZ, arena.MaxZ));
        Assert.Equal(KillY, arena.KillHeight);
        Assert.Equal(20f, arena.KillTop, 3);
        Assert.Equal((-22f, 22f, -20f, 20f), (arena.KillMinX, arena.KillMaxX, arena.KillMinZ, arena.KillMaxZ));
        // Closed deck: two top, two bottom and eight wall triangles.
        Assert.Equal(12, arena.CollisionTriangles.Length);
        var spawns = new[] { (-5f, -4f), (5f, 4f), (-5f, 4f), (5f, -4f) };
        Assert.Equal(spawns.Length, arena.SpawnPoints.Length);
        for (int i = 0; i < spawns.Length; i++)
        {
            var spawn = arena.SpawnPoints[i];
            Assert.Equal(spawns[i].Item1, spawn.X, 3);
            Assert.Equal(spawns[i].Item2, spawn.Z, 3);
            Assert.Equal(0.85f, spawn.Y, 3);
            // Every spawn stands on the deck facing its centre: yaw 0 looks along +Z.
            float toCentre = MathF.Atan2(-spawn.X, -spawn.Z);
            float delta = MathF.IEEERemainder(spawn.Yaw - toCentre, 2f * MathF.PI);
            Assert.InRange(delta, -0.001f, 0.001f);
        }
    }

    public static IEnumerable<object[]> SurfaceSamples()
    {
        foreach (float x in new[] { -11.5f, -9f, -5f, 0f, 5f, 9f, 11.5f })
        foreach (float z in new[] { -9.5f, -7f, -4f, 0f, 4f, 7f, 9.5f })
            yield return new object[] { x, z };
    }

    [Theory]
    [MemberData(nameof(SurfaceSamples))]
    public void EntireDeckIsFlat(float x, float z)
    {
        Assert.InRange(Load().Heightmap.Sample(x, z), -0.001f, 0.001f);
    }

    public static IEnumerable<object[]> RecoveryContacts()
    {
        // Every wall's midpoint and every convex corner, approached along its outward normal.
        var contacts = new List<(float x, float z, float dx, float dz)>();
        for (int i = 0; i < Floor.Length; i++)
        {
            var a = Floor[i];
            var b = Floor[(i + 1) % Floor.Length];
            var prev = Floor[(i + Floor.Length - 1) % Floor.Length];
            var (nx, nz) = OutwardNormal(a, b);
            contacts.Add(((a.X + b.X) / 2, (a.Z + b.Z) / 2, nx, nz));
            var (px, pz) = OutwardNormal(prev, a);
            float length = MathF.Sqrt((nx + px) * (nx + px) + (nz + pz) * (nz + pz));
            contacts.Add((a.X, a.Z, (nx + px) / length, (nz + pz) / length));
        }
        foreach (var cls in Roster)
        foreach (var contact in contacts)
        foreach (float height in new[] { -2f, -9f })
            yield return new object[] { cls, contact.x, contact.z, contact.dx, contact.dz, height };
    }

    [Theory]
    [MemberData(nameof(RecoveryContacts))]
    public void RecoveryWallsBlockInwardMovementAndAllowOutwardMovement(
        CharacterClass cls, float edgeX, float edgeZ, float outwardX, float outwardZ, float height)
    {
        var arena = Load();
        var def = TestHelpers.ResolveDef(cls);
        var state = Airborne(def, edgeX + outwardX, edgeZ + outwardZ, height, -outwardX * 120f, -outwardZ * 120f);

        Simulation.SimulateTick(ref state, def, default, arena, out _, out _, DownActionTuning.Default, false);

        float distance = DistanceOutside(Floor, state.PX, state.PZ);
        Assert.True(distance >= def.CapsuleRadius - 0.03f,
            $"{cls}: entered the floor volume from ({edgeX},{edgeZ}) at height {height}; final ({state.PX},{state.PZ}), clearance {distance}.");
        Assert.False(state.IsGrounded);
        Assert.InRange(state.PY, height - 0.1f, height + 0.01f);

        state.VX = outwardX * 12f;
        state.VZ = outwardZ * 12f;
        Simulation.SimulateTick(ref state, def, default, arena, out _, out _, DownActionTuning.Default, false);
        Assert.True(DistanceOutside(Floor, state.PX, state.PZ) > distance + 0.1f,
            $"{cls}: wall trapped outward movement from ({edgeX},{edgeZ}) at height {height}.");
    }

    [Theory]
    [MemberData(nameof(RosterClasses))]
    public void AllFourSpawnsGroundEveryRosterFighter(CharacterClass cls)
    {
        var arena = Load();
        var def = TestHelpers.ResolveDef(cls);
        foreach (var spawn in arena.SpawnPoints)
        {
            var state = TestHelpers.PlayerState(spawn.X, spawn.Z);
            state.PY = spawn.Y;
            state.IsGrounded = false;
            for (int t = 0; t < 90; t++)
                Simulation.SimulateTick(ref state, def, default, arena, out _, out _, DownActionTuning.Default, false);
            Assert.True(state.IsGrounded, $"{cls}: spawn ({spawn.X},{spawn.Z}) did not ground.");
            Assert.Equal(def.CapsuleHeight * 0.5f, state.PY, 2);
        }
    }

    public static IEnumerable<object[]> RosterClasses() => Roster.Select(cls => new object[] { cls });

    private static CharacterState Airborne(CharacterDefinition def, float x, float z, float y, float vx, float vz)
    {
        var state = TestHelpers.PlayerState(x, z);
        state.PY = y;
        state.IsGrounded = false;
        state.AirTimeTicks = def.Movement.FloatWindowTicks;
        state.State = ActionState.Attacking;
        state.AnimLockTicks = 60;
        state.VX = vx;
        state.VZ = vz;
        return state;
    }

    private static (float X, float Z) OutwardNormal((float X, float Z) a, (float X, float Z) b)
    {
        float ex = b.X - a.X, ez = b.Z - a.Z;
        float length = MathF.Sqrt(ex * ex + ez * ez);
        return (ez / length, -ex / length);
    }

    /// <summary>Distance from a point outside a convex CCW outline to it; zero inside.</summary>
    private static float DistanceOutside((float X, float Z)[] outline, float x, float z)
    {
        bool inside = true;
        float best = float.MaxValue;
        for (int i = 0; i < outline.Length; i++)
        {
            var a = outline[i];
            var b = outline[(i + 1) % outline.Length];
            var (nx, nz) = OutwardNormal(a, b);
            if ((x - a.X) * nx + (z - a.Z) * nz > 0) inside = false;
            float ex = b.X - a.X, ez = b.Z - a.Z;
            float t = Math.Clamp(((x - a.X) * ex + (z - a.Z) * ez) / (ex * ex + ez * ez), 0f, 1f);
            float dx = x - (a.X + ex * t), dz = z - (a.Z + ez * t);
            best = MathF.Min(best, MathF.Sqrt(dx * dx + dz * dz));
        }
        return inside ? 0f : best;
    }
}
