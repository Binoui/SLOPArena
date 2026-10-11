using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class TrainingArenaTests
{
    private static ArenaDefinition Load()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return ArenaBinaryFormat.LoadFromFile(Path.Combine(root, "data", "arenas", "training.arena"))
            ?? throw new InvalidOperationException("Training arena could not be loaded.");
    }

    public static IEnumerable<object[]> Edges()
    {
        foreach (var cls in new[] { CharacterClass.FightGuy, CharacterClass.Manki, CharacterClass.Wibou, CharacterClass.Bonk })
        foreach (bool west in new[] { true, false })
            yield return new object[] { cls, west };
    }

    [Theory]
    [MemberData(nameof(Edges))]
    public void WestAndSouthEdgesAllowAnAirborneExit(CharacterClass cls, bool west)
    {
        var arena = Load();
        var def = TestHelpers.ResolveDef(cls);
        var candidates = new int[arena.CollisionTriangles.Length];
        float startX = west ? -18 : 0, startZ = west ? 0 : -18;
        float endX = west ? -24 : 0, endZ = west ? 0 : -24;
        int count = ArenaCollision.GetCandidateTrianglesForSweep(
            startX, 5, startZ, endX, 5, endZ, def.CapsuleRadius, def.CapsuleHeight, in arena, candidates);

        Assert.False(ArenaCollision.SweepCapsule(
            startX, 5, startZ, endX, 5, endZ, def.CapsuleRadius, def.CapsuleHeight,
            in arena, candidates, count, out _));
    }

    [Theory]
    [MemberData(nameof(Edges))]
    public void NorthAndEastWallsStillBlockAnAirborneExit(CharacterClass cls, bool east)
    {
        var arena = Load();
        var def = TestHelpers.ResolveDef(cls);
        var candidates = new int[arena.CollisionTriangles.Length];
        float startX = east ? 18 : 0, startZ = east ? 0 : 18;
        float endX = east ? 24 : 0, endZ = east ? 0 : 24;
        int count = ArenaCollision.GetCandidateTrianglesForSweep(
            startX, 5, startZ, endX, 5, endZ, def.CapsuleRadius, def.CapsuleHeight, in arena, candidates);

        Assert.True(ArenaCollision.SweepCapsule(
            startX, 5, startZ, endX, 5, endZ, def.CapsuleRadius, def.CapsuleHeight,
            in arena, candidates, count, out _));
    }

    [Theory]
    [MemberData(nameof(Edges))]
    public void OutwardKnockbackCanKillThroughEitherOpenEdge(CharacterClass cls, bool west)
    {
        var arena = Load();
        var def = TestHelpers.ResolveDef(cls);
        var state = TestHelpers.PlayerState(west ? -18 : 0, west ? 0 : -18);
        state.PY = 5;
        state.IsGrounded = false;
        Simulation.ApplyKnockbackForce(ref state, west ? -1 : 0, west ? 0 : -1, 0, 120, 1);
        state.HitstunTicks = 60;
        var sim = TestHelpers.MakeSim(arena);
        sim.RegisterEntity(1, def, state);

        for (int tick = 0; tick < 120; tick++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { [1] = default });
            if (sim.LastTickDeaths.Count == 0) continue;
            var death = Assert.Single(sim.LastTickDeaths);
            Assert.Equal(west ? "minX" : "minZ", death.Boundary);
            Assert.Equal(1, sim.GetState(1).Deaths);
            return;
        }
        Assert.Fail($"{cls}: outward knockback could not kill through the {(west ? "west" : "south")} edge.");
    }
}
