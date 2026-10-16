using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// The underground fight ring (tools/blender/slop_pit/layout.py SPEC): a 32 x 32 ringside
/// floor with chamfered corners whose walls drop to the KO line, and a raised 16 x 16 ring in
/// its centre where all four fighters spawn.
/// </summary>
public class SlopPitArenaTests
{
    private const float RingTop = 1.2f;
    private const float RingHalf = 8f;
    private const float KillY = -11f;

    private static readonly CharacterClass[] Roster =
        { CharacterClass.FightGuy, CharacterClass.Manki, CharacterClass.Wibou, CharacterClass.Bonk };

    // Ringside floor: 32 x 32 with 5 m chamfered corners (CCW in the X/Z plane).
    private static readonly (float X, float Z)[] Floor =
    {
        (-11, -16), (11, -16), (16, -11), (16, 11), (11, 16), (-11, 16), (-16, 11), (-16, -11)
    };

    private static readonly (float X, float Z)[] Ring =
    {
        (RingHalf, -RingHalf), (RingHalf, RingHalf), (-RingHalf, RingHalf), (-RingHalf, -RingHalf)
    };

    private static ArenaDefinition Load()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return ArenaBinaryFormat.LoadFromFile(Path.Combine(root, "data", "arenas", "slop_pit.arena"))!.Value;
    }

    [Fact]
    public void BakedShellMatchesTheRingAndRingside()
    {
        var arena = Load();
        Assert.Equal((-16f, 16f, -16f, 16f), (arena.MinX, arena.MaxX, arena.MinZ, arena.MaxZ));
        Assert.Equal(KillY, arena.KillHeight);
        Assert.Equal(RingTop + 20f, arena.KillTop, 3);
        Assert.Equal((-26f, 26f, -26f, 26f), (arena.KillMinX, arena.KillMaxX, arena.KillMinZ, arena.KillMaxZ));
        // Floor prism (fan top and bottom, eight walls) plus the ring (top and four walls).
        Assert.Equal(6 * 2 + 8 * 2 + 2 + 4 * 2, arena.CollisionTriangles.Length);
        var spawns = new[] { (-5f, -5f), (5f, 5f), (-5f, 5f), (5f, -5f) };
        Assert.Equal(spawns.Length, arena.SpawnPoints.Length);
        for (int i = 0; i < spawns.Length; i++)
        {
            var spawn = arena.SpawnPoints[i];
            Assert.Equal(spawns[i].Item1, spawn.X, 3);
            Assert.Equal(spawns[i].Item2, spawn.Z, 3);
            Assert.Equal(RingTop + 0.85f, spawn.Y, 3);
            // Every spawn stands in the ring facing its centre: yaw 0 looks along +Z.
            float toCentre = MathF.Atan2(-spawn.X, -spawn.Z);
            float delta = MathF.IEEERemainder(spawn.Yaw - toCentre, 2f * MathF.PI);
            Assert.InRange(delta, -0.001f, 0.001f);
        }
    }

    public static IEnumerable<object[]> SurfaceSamples()
    {
        yield return new object[] { 0f, 0f, RingTop };
        for (int degrees = 0; degrees < 360; degrees += 30)
        {
            float angle = degrees * MathF.PI / 180f;
            float dx = MathF.Cos(angle), dz = MathF.Sin(angle);
            // Inside the ring, then across the ringside band to just inside the floor edge.
            foreach (float radius in new[] { 3f, 6.5f })
                yield return new object[] { dx * radius, dz * radius, RingTop };
            float ringEdge = RingHalf / MathF.Max(MathF.Abs(dx), MathF.Abs(dz));
            float floorEdge = DistanceToEdgeAlong(Floor, dx, dz);
            foreach (float t in new[] { 0.15f, 0.5f, 0.85f })
            {
                float radius = ringEdge + 0.8f + (floorEdge - ringEdge - 1.6f) * t;
                yield return new object[] { dx * radius, dz * radius, 0f };
            }
        }
    }

    [Theory]
    [MemberData(nameof(SurfaceSamples))]
    public void RingTopAndRingsideFloorAreFlat(float x, float z, float expected)
    {
        Assert.InRange(Load().Heightmap.Sample(x, z), expected - 0.001f, expected + 0.001f);
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

    public static IEnumerable<object[]> RingContacts()
    {
        foreach (var cls in Roster)
        for (int degrees = 0; degrees < 360; degrees += 45)
            yield return new object[] { cls, degrees };
    }

    [Theory]
    [MemberData(nameof(RingContacts))]
    public void RingSidesBlockAFighterBelowItsTop(CharacterClass cls, int degrees)
    {
        var arena = Load();
        var def = TestHelpers.ResolveDef(cls);
        float angle = degrees * MathF.PI / 180f;
        float dx = MathF.Cos(angle), dz = MathF.Sin(angle);
        float start = RingHalf / MathF.Max(MathF.Abs(dx), MathF.Abs(dz)) + def.CapsuleRadius + 1f;
        // Feet 0.2 m above the ringside floor, well below the ring top.
        var state = Airborne(def, dx * start, dz * start, def.CapsuleHeight * 0.5f + 0.2f, -dx * 120f, -dz * 120f);

        Simulation.SimulateTick(ref state, def, default, arena, out _, out _, DownActionTuning.Default, false);

        float clearance = DistanceOutside(Ring, state.PX, state.PZ);
        Assert.True(clearance >= def.CapsuleRadius - 0.03f,
            $"{cls}: entered the ring at {degrees} degrees; final ({state.PX},{state.PZ}), clearance {clearance}.");
    }

    [Theory]
    [MemberData(nameof(RosterClasses))]
    public void EveryFighterReachesTheRingWithOneJump(CharacterClass cls)
    {
        var arena = Load();
        var def = TestHelpers.ResolveDef(cls);
        float half = def.CapsuleHeight * 0.5f;
        var sim = new ServerSimulation(arena);
        sim.RegisterEntity(1, def, new CharacterState
        {
            PX = -(RingHalf + def.CapsuleRadius + 0.3f), PY = half, PZ = 0f,
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
            landed = t > 0 && s.IsGrounded && MathF.Abs(s.PY - (RingTop + half)) < 0.02f
                && DistanceOutside(Ring, s.PX, s.PZ) == 0f;
        }
        Assert.True(landed, $"{cls}: a single jump from ringside did not land in the ring.");
    }

    [Theory]
    [MemberData(nameof(RosterClasses))]
    public void WalkingOutOfTheRingLandsRingside(CharacterClass cls)
    {
        var arena = Load();
        var def = TestHelpers.ResolveDef(cls);
        float half = def.CapsuleHeight * 0.5f;
        var sim = new ServerSimulation(arena);
        sim.RegisterEntity(1, def, new CharacterState
        {
            PX = 6f, PY = RingTop + half, PZ = 0f,
            State = ActionState.Idle,
            IsGrounded = true,
            JumpsLeft = def.Movement.MaxJumps,
            AirDodgesLeft = 1,
        });
        // Walk east until the fighter steps off the ring edge, then let go of the stick.
        var inputs = new Dictionary<ulong, InputState> { [1] = new InputState { MoveX = 1f } };
        bool leftRing = false;
        for (int t = 0; t < 60 && !leftRing; t++)
        {
            sim.Tick(inputs);
            leftRing = !sim.GetState(1).IsGrounded;
        }
        inputs[1] = default;
        for (int t = 0; t < 60; t++) sim.Tick(inputs);
        var s = sim.GetState(1);
        Assert.True(leftRing, $"{cls}: never left the ring top.");
        Assert.True(s.IsGrounded && MathF.Abs(s.PY - half) < 0.02f,
            $"{cls}: walking off the ring did not settle on the ringside floor; final ({s.PX},{s.PY},{s.PZ}).");
        Assert.InRange(s.PX, RingHalf + def.CapsuleRadius - 0.03f, 16f);
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

    /// <summary>Distance from the centre to a convex CCW outline along a unit direction.</summary>
    private static float DistanceToEdgeAlong((float X, float Z)[] outline, float dx, float dz)
    {
        float best = float.MaxValue;
        for (int i = 0; i < outline.Length; i++)
        {
            var a = outline[i];
            var (nx, nz) = OutwardNormal(a, outline[(i + 1) % outline.Length]);
            float along = dx * nx + dz * nz;
            if (along > 1e-6f) best = MathF.Min(best, (a.X * nx + a.Z * nz) / along);
        }
        return best;
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
