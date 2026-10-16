using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// The fishing boat (tools/blender/splash_deck/layout.py SPEC): a flat deck with a square
/// transom, parallel sides and a parabolic bow, whose hull walls drop straight to the KO
/// line, plus the barge's two raised spots (aft wheelhouse, forward fish hatch).
/// </summary>
public class SplashDeckArenaTests
{
    private const float SternX = -15.5f;
    private const float QuarterRadius = 3f;
    private const int QuarterSegments = 6;
    private const float HalfBeam = 11f;
    private const float ShoulderX = 3f;
    private const float BowX = 20.5f;
    private const int BowSegments = 20;
    private const float RaisedTop = 1.5f;
    private const float KillY = -12f;

    private static readonly CharacterClass[] Roster =
        { CharacterClass.FightGuy, CharacterClass.Manki, CharacterClass.Wibou, CharacterClass.Bonk };

    private static readonly (float X, float Z)[] Hull = Outline();
    private static readonly (float X, float Z)[] Wheelhouse = Box(-13.5f, -2.5f, -7.5f, 2.5f);
    private static readonly (float X, float Z)[] FishHatch = Box(7.5f, -2.5f, 13.5f, 2.5f);

    private static ArenaDefinition Load()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return ArenaBinaryFormat.LoadFromFile(Path.Combine(root, "data", "arenas", "splash_deck.arena"))!.Value;
    }

    [Fact]
    public void BakedShellMatchesTheFishingBoat()
    {
        var arena = Load();
        Assert.Equal((SternX, BowX, -HalfBeam, HalfBeam), (arena.MinX, arena.MaxX, arena.MinZ, arena.MaxZ));
        Assert.Equal(KillY, arena.KillHeight);
        Assert.Equal(21.5f, arena.KillTop, 3);
        Assert.Equal((-25.5f, 30.5f, -21f, 21f), (arena.KillMinX, arena.KillMaxX, arena.KillMinZ, arena.KillMaxZ));
        // Hull prism (deck, walls, bottom) plus two open-bottomed raised boxes.
        Assert.Equal(Hull.Length * 4 - 4 + 2 * 10, arena.CollisionTriangles.Length);
        // The barge's spawns and diagonal facings are unchanged; yaw 0 looks along +Z.
        var spawns = new[] { (-6f, -5f, 45f), (6f, 5f, 225f), (-6f, 5f, 135f), (6f, -5f, 315f) };
        Assert.Equal(spawns.Length, arena.SpawnPoints.Length);
        for (int i = 0; i < spawns.Length; i++)
        {
            var spawn = arena.SpawnPoints[i];
            Assert.Equal(spawns[i].Item1, spawn.X, 3);
            Assert.Equal(spawns[i].Item2, spawn.Z, 3);
            Assert.Equal(0.85f, spawn.Y, 3);
            float delta = MathF.IEEERemainder(spawn.Yaw - spawns[i].Item3 * MathF.PI / 180f, 2f * MathF.PI);
            Assert.InRange(delta, -0.001f, 0.001f);
        }
    }

    public static IEnumerable<object[]> SurfaceSamples()
    {
        // Open deck, including the bow and the narrow strip behind the wheelhouse.
        foreach (var (x, z) in new[] { (0f, 0f), (0f, 10.5f), (0f, -10.5f), (-6f, 5f), (6f, -5f), (-14.8f, 0f),
                     (-14.5f, 7.5f), (5f, 10f), (15f, 0f), (15f, 5f), (18f, 2f), (19.5f, 0f), (-10.5f, 4f), (10.5f, -4f) })
            yield return new object[] { x, z, 0f };
        foreach (var (x, z) in new[] { (-10.5f, 0f), (-13f, 2f), (-8f, -2f), (10.5f, 0f), (8f, 2f), (13f, -2f) })
            yield return new object[] { x, z, RaisedTop };
    }

    [Theory]
    [MemberData(nameof(SurfaceSamples))]
    public void DeckIsFlatAroundTheRaisedSpotsAndOnTheirTops(float x, float z, float expected)
    {
        Assert.InRange(Load().Heightmap.Sample(x, z), expected - 0.001f, expected + 0.001f);
    }

    [Theory]
    [InlineData(18f, 8f)]
    [InlineData(18f, -8f)]
    [InlineData(16f, 10f)]
    [InlineData(-15.3f, 10.8f)]
    [InlineData(-15.3f, -10.8f)]
    public void OpenWaterBeyondTheBowAndQuartersHasNoSurface(float x, float z)
    {
        Assert.Equal(float.MinValue, Load().Heightmap.Sample(x, z));
    }

    public static IEnumerable<object[]> HullContacts()
    {
        // Every third wall midpoint and vertex (stem and transom corners included),
        // approached along its outward normal.
        var contacts = new List<(float x, float z, float dx, float dz)>();
        for (int i = 0; i < Hull.Length; i += 3)
        {
            var a = Hull[i];
            var b = Hull[(i + 1) % Hull.Length];
            var prev = Hull[(i + Hull.Length - 1) % Hull.Length];
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
    [MemberData(nameof(HullContacts))]
    public void HullWallsBlockInwardMovementAndAllowOutwardMovement(
        CharacterClass cls, float edgeX, float edgeZ, float outwardX, float outwardZ, float height)
    {
        var arena = Load();
        var def = TestHelpers.ResolveDef(cls);
        var state = Airborne(def, edgeX + outwardX, edgeZ + outwardZ, height, -outwardX * 120f, -outwardZ * 120f);

        Simulation.SimulateTick(ref state, def, default, arena, out _, out _, DownActionTuning.Default, false);

        float distance = DistanceOutside(Hull, state.PX, state.PZ);
        Assert.True(distance >= def.CapsuleRadius - 0.03f,
            $"{cls}: entered the hull from ({edgeX},{edgeZ}) at height {height}; final ({state.PX},{state.PZ}), clearance {distance}.");
        Assert.False(state.IsGrounded);
        Assert.InRange(state.PY, height - 0.1f, height + 0.01f);

        state.VX = outwardX * 12f;
        state.VZ = outwardZ * 12f;
        Simulation.SimulateTick(ref state, def, default, arena, out _, out _, DownActionTuning.Default, false);
        Assert.True(DistanceOutside(Hull, state.PX, state.PZ) > distance + 0.1f,
            $"{cls}: the hull trapped outward movement from ({edgeX},{edgeZ}) at height {height}.");
    }

    public static IEnumerable<object[]> RaisedContacts()
    {
        foreach (var cls in Roster)
        foreach (string spot in new[] { "Wheelhouse", "FishHatch" })
        for (int degrees = 0; degrees < 360; degrees += 45)
            yield return new object[] { cls, spot, degrees };
    }

    [Theory]
    [MemberData(nameof(RaisedContacts))]
    public void RaisedSpotsBlockAFighterBelowTheirTop(CharacterClass cls, string spot, int degrees)
    {
        var arena = Load();
        var def = TestHelpers.ResolveDef(cls);
        var box = spot == "Wheelhouse" ? Wheelhouse : FishHatch;
        float cx = box.Average(p => p.X), cz = box.Average(p => p.Z);
        float angle = degrees * MathF.PI / 180f;
        float dx = MathF.Cos(angle), dz = MathF.Sin(angle);
        // Start on a ray from the centre, a metre outside the footprint, feet 0.2 m above the deck.
        float reach = 0f;
        while (DistanceOutside(box, cx + dx * reach, cz + dz * reach) == 0f) reach += 0.05f;
        float start = reach + def.CapsuleRadius + 1f;
        var state = Airborne(def, cx + dx * start, cz + dz * start, def.CapsuleHeight * 0.5f + 0.2f, -dx * 120f, -dz * 120f);

        Simulation.SimulateTick(ref state, def, default, arena, out _, out _, DownActionTuning.Default, false);

        float clearance = DistanceOutside(box, state.PX, state.PZ);
        Assert.True(clearance >= def.CapsuleRadius - 0.03f,
            $"{cls}: entered the {spot} at {degrees} degrees; final ({state.PX},{state.PZ}), clearance {clearance}.");
    }

    public static IEnumerable<object[]> OneJumpClimbs()
    {
        foreach (var cls in Roster)
        foreach (string spot in new[] { "Wheelhouse", "FishHatch" })
            yield return new object[] { cls, spot };
    }

    [Theory]
    [MemberData(nameof(OneJumpClimbs))]
    public void EveryFighterReachesEachRaisedSpotWithOneJump(CharacterClass cls, string spot)
    {
        var arena = Load();
        var def = TestHelpers.ResolveDef(cls);
        float half = def.CapsuleHeight * 0.5f;
        // Start on the open deck beside the inner face, moving towards the spot.
        bool aft = spot == "Wheelhouse";
        var box = aft ? Wheelhouse : FishHatch;
        float direction = aft ? -1f : 1f;
        float face = aft ? -7.5f : 7.5f;
        var sim = new ServerSimulation(arena);
        sim.RegisterEntity(1, def, new CharacterState
        {
            PX = face - direction * (def.CapsuleRadius + 0.3f), PY = half, PZ = 0f,
            State = ActionState.Idle,
            IsGrounded = true,
            JumpsLeft = def.Movement.MaxJumps,
            AirDodgesLeft = 1,
        });
        var inputs = new Dictionary<ulong, InputState>();
        bool landed = false;
        bool cleared = false;
        for (int t = 0; t < 120 && !landed; t++)
        {
            // One full jump: pressed on tick 0 and held through the squat. Steer towards
            // the spot until the feet clear its top, then let go so the landing stays on it.
            inputs[1] = new InputState { MoveX = cleared ? 0f : direction, Jump = t == 0, JumpHeld = t < 10 };
            sim.Tick(inputs);
            var s = sim.GetState(1);
            cleared |= s.PY - half > RaisedTop + 0.05f && DistanceOutside(box, s.PX, s.PZ) == 0f;
            landed = t > 0 && s.IsGrounded && MathF.Abs(s.PY - (RaisedTop + half)) < 0.02f
                && DistanceOutside(box, s.PX, s.PZ) == 0f;
        }
        Assert.True(landed, $"{cls}: a single jump from the deck did not land on the {spot}.");
    }

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

    /// <summary>CCW deck outline from the stem, mirroring layout.py's port_side/outline.</summary>
    private static (float X, float Z)[] Outline()
    {
        var port = new List<(float X, float Z)>();
        for (int k = 0; k <= BowSegments; k++)
        {
            float u = 1f - (float)k / BowSegments;
            port.Add((ShoulderX + u * (BowX - ShoulderX), HalfBeam * (1f - u * u)));
        }
        float cx = SternX + QuarterRadius, cz = HalfBeam - QuarterRadius;
        for (int j = 0; j <= QuarterSegments; j++)
        {
            float a = (90f + 90f * j / QuarterSegments) * MathF.PI / 180f;
            port.Add((cx + QuarterRadius * MathF.Cos(a), cz + QuarterRadius * MathF.Sin(a)));
        }
        port[0] = (BowX, 0f);
        var ring = new List<(float X, float Z)>(port);
        for (int i = port.Count - 1; i >= 1; i--)
            ring.Add((port[i].X, -port[i].Z));
        return ring.ToArray();
    }

    private static (float X, float Z)[] Box(float x0, float z0, float x1, float z1) =>
        new[] { (x1, z0), (x1, z1), (x0, z1), (x0, z0) };

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
