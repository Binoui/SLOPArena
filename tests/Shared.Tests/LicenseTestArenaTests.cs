using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>The Fighter's License tutorial deck: walled on three sides, one open east edge, lesson surfaces from the layout.</summary>
public sealed class LicenseTestArenaTests
{
    private static readonly TutorialStageLayout Layout = TutorialCurriculum.LicenseTest;

    private static ArenaDefinition Load()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return ArenaBinaryFormat.LoadFromFile(Path.Combine(root, "data", "arenas", Layout.ArenaKey + ".arena"))
            ?? throw new InvalidOperationException("The license_test arena could not be loaded.");
    }

    public static IEnumerable<object[]> Fighters()
    {
        foreach (var cls in new[] { CharacterClass.FightGuy, CharacterClass.Manki, CharacterClass.Wibou, CharacterClass.Bonk })
            yield return new object[] { cls };
    }

    [Fact]
    public void SpawnsAndLessonSurfacesMatchTheTutorialLayout()
    {
        var arena = Load();
        Assert.Equal(4, arena.SpawnPoints.Length);
        foreach (var spawn in arena.SpawnPoints)
            TestHelpers.AssertNear(Layout.FloorHeight, arena.Heightmap.Sample(spawn.X, spawn.Z), 0.01f);
        TestHelpers.AssertNear(Layout.FloorHeight, arena.Heightmap.Sample(Layout.KnockoutX, Layout.KnockoutZ), 0.01f);
        TestHelpers.AssertNear(Layout.BlockHeight, arena.Heightmap.Sample(-12f, 10f), 0.01f);
        TestHelpers.AssertNear(Layout.TallBlockHeight, arena.Heightmap.Sample(-6f, 10f), 0.01f);
        Assert.True(arena.Heightmap.Sample(-0.5f, 10.5f) > Layout.TallBlockHeight, "the lookout tower stands above the tall block");
        TestHelpers.AssertNear(-11f, arena.KillHeight, 0.001f);
    }

    [Fact]
    public void WallTopsStayOutOfReachFromTheTower()
    {
        var arena = Load();
        float tower = arena.Heightmap.Sample(-0.5f, 10.5f);
        float wall = arena.Heightmap.Sample(0f, 15.5f);
        foreach (var cls in new[] { CharacterClass.FightGuy, CharacterClass.Manki, CharacterClass.Wibou, CharacterClass.Bonk })
        {
            var move = TestHelpers.ResolveDef(cls).Movement;
            float full = move.JumpForce * move.JumpForce / (2f * move.Gravity);
            float air = MathF.Pow(move.JumpForce * move.AirJumpVMultiplier, 2) / (2f * move.Gravity);
            Assert.True(tower + full + air < wall, $"{cls} could reach a wall top from the tower");
        }
    }

    [Theory]
    [MemberData(nameof(Fighters))]
    public void OnlyTheEastEdgeLetsAFighterLeave(CharacterClass cls)
    {
        var arena = Load();
        var def = TestHelpers.ResolveDef(cls);
        var candidates = new int[arena.CollisionTriangles.Length];
        bool Blocked(float startX, float startZ, float endX, float endZ, float y)
        {
            int count = ArenaCollision.GetCandidateTrianglesForSweep(startX, y, startZ, endX, y, endZ,
                def.CapsuleRadius, def.CapsuleHeight, in arena, candidates);
            return ArenaCollision.SweepCapsule(startX, y, startZ, endX, y, endZ, def.CapsuleRadius, def.CapsuleHeight,
                in arena, candidates, count, out _);
        }
        Assert.False(Blocked(18, 0, 24, 0, 2), "the east edge is open");
        Assert.True(Blocked(0, 13, 0, 19, 3), "the test-center wall blocks the north side");
        Assert.True(Blocked(0, -13, 0, -19, 3), "the safety fence blocks the south side");
        Assert.True(Blocked(-10, 0, -24, 0, 3), "the west wall blocks the west side");
    }

    [Theory]
    [MemberData(nameof(Fighters))]
    public void ALaunchFromTheKnockoutSpotKillsThroughTheEastEdge(CharacterClass cls)
    {
        var arena = Load();
        var def = TestHelpers.ResolveDef(cls);
        var state = TestHelpers.PlayerState(Layout.KnockoutX, Layout.KnockoutZ);
        state.PY = 1.5f;
        state.IsGrounded = false;
        Simulation.ApplyKnockbackForce(ref state, 1, 0, 0, 120, 1);
        state.HitstunTicks = 60;
        var sim = TestHelpers.MakeSim(arena);
        sim.RegisterEntity(1, def, state);
        for (int tick = 0; tick < 240; tick++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { [1] = default });
            if (sim.LastTickDeaths.Count == 0) continue;
            Assert.Contains(Assert.Single(sim.LastTickDeaths).Boundary, new[] { "maxX", "bottom" });
            return;
        }
        Assert.Fail($"{cls}: an eastward launch from the knockout spot did not leave the deck.");
    }

    [Fact]
    public void RunningOffTheOpenEdgeFallsToTheKillPlane()
    {
        var arena = Load();
        var def = TestHelpers.FightGuyDef;
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState(16f, 1f);
        state.PY = Layout.FloorHeight + def.CapsuleHeight * 0.5f;
        sim.RegisterEntity(1, def, state);
        for (int tick = 0; tick < 300; tick++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { [1] = new InputState { MoveX = 1, Right = true } });
            if (sim.LastTickDeaths.Count == 0) continue;
            Assert.Equal("bottom", Assert.Single(sim.LastTickDeaths).Boundary);
            return;
        }
        Assert.Fail("Running east off the deck should fall past the building's base.");
    }
}
