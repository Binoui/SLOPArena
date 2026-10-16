using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// The kids' craft table (tools/blender/table_manners/layout.py SPEC): a round tabletop whose
/// floor-length tablecloth is a solid wall down to the KO line, with a play-dough cake riser
/// in the centre.
/// </summary>
public class TableMannersArenaTests
{
    private const float TableRadius = 15f;
    private const int TableSegments = 64;
    private const float CakeRadius = 4f;
    private const int CakeSegments = 32;
    private const float CakeTop = 1.5f;
    private const float KillY = -15f;

    private static readonly CharacterClass[] Roster =
        { CharacterClass.FightGuy, CharacterClass.Manki, CharacterClass.Wibou, CharacterClass.Bonk };

    private static readonly (float X, float Z)[] Table = Circle(TableRadius, TableSegments);
    private static readonly (float X, float Z)[] Cake = Circle(CakeRadius, CakeSegments);

    private static ArenaDefinition Load()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return ArenaBinaryFormat.LoadFromFile(Path.Combine(root, "data", "arenas", "table_manners.arena"))!.Value;
    }

    [Fact]
    public void BakedShellMatchesTheCraftTable()
    {
        var arena = Load();
        Assert.Equal((-15f, 15f, -15f, 15f), (arena.MinX, arena.MaxX, arena.MinZ, arena.MaxZ));
        Assert.Equal(KillY, arena.KillHeight);
        Assert.Equal(21.5f, arena.KillTop, 3);
        Assert.Equal((-25f, 25f, -25f, 25f), (arena.KillMinX, arena.KillMaxX, arena.KillMinZ, arena.KillMaxZ));
        // Table drum (top, walls, bottom) plus the cake (top and walls).
        Assert.Equal(TableSegments * 4 + CakeSegments * 3, arena.CollisionTriangles.Length);
        var spawns = new[] { (-6.5f, -6.5f), (6.5f, 6.5f), (-6.5f, 6.5f), (6.5f, -6.5f) };
        Assert.Equal(spawns.Length, arena.SpawnPoints.Length);
        for (int i = 0; i < spawns.Length; i++)
        {
            var spawn = arena.SpawnPoints[i];
            Assert.Equal(spawns[i].Item1, spawn.X, 3);
            Assert.Equal(spawns[i].Item2, spawn.Z, 3);
            Assert.Equal(0.85f, spawn.Y, 3);
            // Every spawn faces the cake: yaw 0 looks along +Z.
            float toCentre = MathF.Atan2(-spawn.X, -spawn.Z);
            float delta = MathF.IEEERemainder(spawn.Yaw - toCentre, 2f * MathF.PI);
            Assert.InRange(delta, -0.001f, 0.001f);
        }
    }

    public static IEnumerable<object[]> SurfaceSamples()
    {
        yield return new object[] { 0f, 0f, CakeTop };
        for (int degrees = 0; degrees < 360; degrees += 30)
        foreach (float radius in new[] { 2f, 3.3f, 4.8f, 9f, 14.4f })
        {
            float angle = degrees * MathF.PI / 180f;
            yield return new object[] { MathF.Cos(angle) * radius, MathF.Sin(angle) * radius, radius < CakeRadius ? CakeTop : 0f };
        }
    }

    [Theory]
    [MemberData(nameof(SurfaceSamples))]
    public void TabletopIsFlatAroundTheCakeAndOnItsTop(float x, float z, float expected)
    {
        Assert.InRange(Load().Heightmap.Sample(x, z), expected - 0.001f, expected + 0.001f);
    }

    public static IEnumerable<object[]> ClothContacts()
    {
        // Every fourth wall midpoint and vertex, approached along its outward normal.
        var contacts = new List<(float x, float z, float dx, float dz)>();
        for (int i = 0; i < Table.Length; i += 4)
        {
            var a = Table[i];
            var b = Table[(i + 1) % Table.Length];
            var prev = Table[(i + Table.Length - 1) % Table.Length];
            var (nx, nz) = OutwardNormal(a, b);
            contacts.Add(((a.X + b.X) / 2, (a.Z + b.Z) / 2, nx, nz));
            var (px, pz) = OutwardNormal(prev, a);
            float length = MathF.Sqrt((nx + px) * (nx + px) + (nz + pz) * (nz + pz));
            contacts.Add((a.X, a.Z, (nx + px) / length, (nz + pz) / length));
        }
        foreach (var cls in Roster)
        foreach (var contact in contacts)
        foreach (float height in new[] { -2f, -11f })
            yield return new object[] { cls, contact.x, contact.z, contact.dx, contact.dz, height };
    }

    [Theory]
    [MemberData(nameof(ClothContacts))]
    public void ClothWallsBlockInwardMovementAndAllowOutwardMovement(
        CharacterClass cls, float edgeX, float edgeZ, float outwardX, float outwardZ, float height)
    {
        var arena = Load();
        var def = TestHelpers.ResolveDef(cls);
        var state = Airborne(def, edgeX + outwardX, edgeZ + outwardZ, height, -outwardX * 120f, -outwardZ * 120f);

        Simulation.SimulateTick(ref state, def, default, arena, out _, out _, DownActionTuning.Default, false);

        float distance = DistanceOutside(Table, state.PX, state.PZ);
        Assert.True(distance >= def.CapsuleRadius - 0.03f,
            $"{cls}: entered the table from ({edgeX},{edgeZ}) at height {height}; final ({state.PX},{state.PZ}), clearance {distance}.");
        Assert.False(state.IsGrounded);
        Assert.InRange(state.PY, height - 0.1f, height + 0.01f);

        state.VX = outwardX * 12f;
        state.VZ = outwardZ * 12f;
        Simulation.SimulateTick(ref state, def, default, arena, out _, out _, DownActionTuning.Default, false);
        Assert.True(DistanceOutside(Table, state.PX, state.PZ) > distance + 0.1f,
            $"{cls}: tablecloth trapped outward movement from ({edgeX},{edgeZ}) at height {height}.");
    }

    public static IEnumerable<object[]> CakeContacts()
    {
        foreach (var cls in Roster)
        for (int degrees = 0; degrees < 360; degrees += 45)
            yield return new object[] { cls, degrees };
    }

    [Theory]
    [MemberData(nameof(CakeContacts))]
    public void CakeSidesBlockAFighterBelowItsTop(CharacterClass cls, int degrees)
    {
        var arena = Load();
        var def = TestHelpers.ResolveDef(cls);
        float angle = degrees * MathF.PI / 180f;
        float dx = MathF.Cos(angle), dz = MathF.Sin(angle);
        float start = CakeRadius + def.CapsuleRadius + 1f;
        // Feet 0.2 m above the tabletop, well below the cake top.
        var state = Airborne(def, dx * start, dz * start, def.CapsuleHeight * 0.5f + 0.2f, -dx * 120f, -dz * 120f);

        Simulation.SimulateTick(ref state, def, default, arena, out _, out _, DownActionTuning.Default, false);

        float clearance = DistanceOutside(Cake, state.PX, state.PZ);
        Assert.True(clearance >= def.CapsuleRadius - 0.03f,
            $"{cls}: entered the cake at {degrees} degrees; final ({state.PX},{state.PZ}), clearance {clearance}.");
    }

    [Theory]
    [MemberData(nameof(RosterClasses))]
    public void EveryFighterReachesTheCakeTopWithOneJump(CharacterClass cls)
    {
        var arena = Load();
        var def = TestHelpers.ResolveDef(cls);
        float half = def.CapsuleHeight * 0.5f;
        var sim = new ServerSimulation(arena);
        sim.RegisterEntity(1, def, new CharacterState
        {
            PX = -(CakeRadius + def.CapsuleRadius + 0.3f), PY = half, PZ = 0f,
            State = ActionState.Idle,
            IsGrounded = true,
            JumpsLeft = def.Movement.MaxJumps,
            AirDodgesLeft = 1,
        });
        var inputs = new Dictionary<ulong, InputState>();
        bool landed = false;
        for (int t = 0; t < 120 && !landed; t++)
        {
            // One jump only: the press on tick 0, held through the squat for a full jump.
            inputs[1] = new InputState { MoveX = 1f, Jump = t == 0, JumpHeld = t < 10 };
            sim.Tick(inputs);
            var s = sim.GetState(1);
            landed = t > 0 && s.IsGrounded && MathF.Abs(s.PY - (CakeTop + half)) < 0.02f
                && DistanceOutside(Cake, s.PX, s.PZ) == 0f;
        }
        Assert.True(landed, $"{cls}: a single jump from the cake's side did not land on its top.");
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

    private static (float X, float Z)[] Circle(float radius, int segments) =>
        Enumerable.Range(0, segments)
            .Select(i => (radius * MathF.Cos(2f * MathF.PI * i / segments), radius * MathF.Sin(2f * MathF.PI * i / segments)))
            .ToArray();

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
