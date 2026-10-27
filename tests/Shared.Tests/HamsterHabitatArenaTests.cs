using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Hamster Habitat gray-box (tools/blender/hamster_habitat/layout.py SPEC): a desk cage at
/// y 0 and a shelf cage at y -3 across an open gap, joined by a low tube with a junction
/// step under a roof hatch and a high tube from the house roof to the wheel deck.
/// </summary>
public class HamsterHabitatArenaTests
{
    private const float Desk = 0f, Shelf = -3f, Deck = -1f, Roof = 4.4f, StepTop = 0.1f;
    // High tube control points (x, z) in route order; a player steers along them.
    private static readonly (float X, float Z)[] HighTubeLine =
        { (-23.4f, -5.2f), (-19.5f, -5.2f), (-10.4f, -3.9f), (0f, -2.08f), (8.45f, -0.52f), (14.95f, 0.39f), (25.35f, 0.39f) };

    private static float HighTubeZ(float x)
    {
        for (int i = 1; i < HighTubeLine.Length; i++)
            if (x <= HighTubeLine[i].X)
            {
                var (ax, az) = HighTubeLine[i - 1];
                var (bx, bz) = HighTubeLine[i];
                return az + (bz - az) * Math.Clamp((x - ax) / (bx - ax), 0f, 1f);
            }
        return HighTubeLine[^1].Z;
    }

    // The low tube's axis falls almost linearly from y 1.96 at x -10.4 to -1.04 at x 10.4; its top is 2.2 above.
    private static float LowTubeTop(float x) => 0.46f - 3f / 20.8f * Math.Clamp(x, -10.4f, 10.4f) + 2.2f;

    public static IEnumerable<object[]> Roster()
    {
        foreach (var cls in new[] { CharacterClass.FightGuy, CharacterClass.Manki, CharacterClass.Wibou,
                     CharacterClass.Bonk, CharacterClass.Bashie })
            yield return new object[] { cls };
    }

    public static IEnumerable<object[]> RosterBothWays()
    {
        foreach (var row in Roster())
        {
            yield return new[] { row[0], 1 };
            yield return new[] { row[0], -1 };
        }
    }

    private static ArenaDefinition Load()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var arena = ArenaBinaryFormat.LoadFromFile(Path.Combine(root, "data", "arenas", "hamster_habitat.arena"));
        Assert.True(arena.HasValue, "Hamster Habitat must have its baked arena.");
        return arena.Value;
    }

    private static (ServerSimulation sim, CharacterDefinition def) Place(CharacterClass cls, float x, float floor, float z)
    {
        var def = TestHelpers.ResolveDef(cls);
        var state = TestHelpers.PlayerState(x, z);
        state.PY = TestHelpers.GroundPY(def, floor);
        var sim = TestHelpers.MakeSim(Load());
        sim.RegisterEntity(1, def, state);
        Assert.True(TestHelpers.TickN(sim, default, 5).IsGrounded, $"{cls}: start at ({x}, {floor}, {z}) is not supported.");
        return (sim, def);
    }

    /// <summary>Runs `drive` until `done` holds (true) or the tick budget or a death ends it (false).</summary>
    private static bool Run(ServerSimulation sim, int ticks, Func<int, CharacterState, InputState> drive,
        Func<CharacterState, bool> done, out CharacterState last)
    {
        var inputs = new Dictionary<ulong, InputState>();
        last = sim.GetState(1);
        for (int tick = 0; tick < ticks; tick++)
        {
            inputs[1] = drive(tick, last);
            sim.Tick(inputs);
            last = sim.GetState(1);
            if (last.Deaths > 0) return false;
            if (done(last)) return true;
        }
        return false;
    }

    [Fact]
    public void BakedShellMatchesTheLayout()
    {
        var arena = Load();
        Assert.Equal(-14f, arena.KillHeight);
        Assert.Equal(-42.5f, arena.KillMinX, 3);
        Assert.Equal(42.5f, arena.KillMaxX, 3);
        Assert.Equal(-20.4f, arena.KillMinZ, 3);
        Assert.Equal(20.4f, arena.KillMaxZ, 3);
        var spawns = new[] { (-14.95f, Desk, 0.65f), (22.1f, Shelf, 7.15f), (-27.3f, Desk, 7.15f), (13.65f, Shelf, -6.5f) };
        Assert.Equal(spawns.Length, arena.SpawnPoints.Length);
        for (int i = 0; i < spawns.Length; i++)
        {
            Assert.Equal(spawns[i].Item1, arena.SpawnPoints[i].X, 3);
            Assert.Equal(spawns[i].Item2 + 0.85f, arena.SpawnPoints[i].Y, 3);
            Assert.Equal(spawns[i].Item3, arena.SpawnPoints[i].Z, 3);
        }
    }

    [Theory]
    [MemberData(nameof(RosterBothWays))]
    public void LowTube_CarriesEveryFighterAcrossTheGap(CharacterClass cls, int direction)
    {
        float startX = -16.9f * direction;
        var (sim, def) = Place(cls, startX, direction > 0 ? Desk : Shelf, 6.5f);
        float floor = direction > 0 ? Shelf : Desk;
        int jumpTick = -100;
        bool arrived = Run(sim, 900, (tick, s) =>
        {
            // Hop the junction step whenever grounded just short of it.
            float toStep = -s.PX * direction - 1.1f;
            bool hop = s.IsGrounded && toStep is > -0.2f and < 2.2f && s.PY < TestHelpers.GroundPY(def, StepTop) - 0.3f
                && tick - jumpTick > 20;
            if (hop) jumpTick = tick;
            return TestHelpers.Input(moveX: direction, jump: hop, jumpHeld: tick - jumpTick < 12);
        }, s => s.IsGrounded && s.PX * direction > 16.3f, out var last);
        Assert.True(arrived, $"{cls}/{direction}: stopped in the low tube at ({last.PX:F2}, {last.PY:F2}, {last.PZ:F2}), deaths {last.Deaths}.");
        Assert.Equal(TestHelpers.GroundPY(def, floor), last.PY, precision: 2);
    }

    [Theory]
    [MemberData(nameof(RosterBothWays))]
    public void HighTube_RunsBetweenTheHouseRoofAndTheWheelDeck(CharacterClass cls, int direction)
    {
        // +1 starts on the roof in front of the tube mouth, -1 on the deck in front of the other mouth.
        var (sim, def) = direction > 0 ? Place(cls, -28f, Roof, -5.2f) : Place(cls, 27.3f, Deck, 0.39f);
        bool arrived = Run(sim, 900, (_, s) =>
                TestHelpers.Input(moveX: direction, moveY: Math.Clamp((HighTubeZ(s.PX + 1.5f * direction) - s.PZ) * 1.5f, -1f, 1f)),
            s => s.IsGrounded && (direction > 0 ? s.PX > 26f : s.PX < -24.7f), out var last);
        Assert.True(arrived, $"{cls}/{direction}: stopped in the high tube at ({last.PX:F2}, {last.PY:F2}, {last.PZ:F2}), deaths {last.Deaths}.");
        Assert.Equal(TestHelpers.GroundPY(def, direction > 0 ? Deck : Roof), last.PY, precision: 2);
    }

    [Theory]
    [MemberData(nameof(Roster))]
    public void FoodBowl_DoubleJumpReachesTheHouseRoof(CharacterClass cls)
    {
        var (sim, def) = Place(cls, -26.65f, Desk + 1.2f, 1.82f);
        bool second = false;
        bool landed = Run(sim, 200, (tick, s) =>
        {
            bool jump = tick == 0 || (!second && tick > 8 && s.VY <= 0f);
            if (tick > 0 && jump) second = true;
            return TestHelpers.Input(moveY: tick > 4 ? -1f : 0f, jump: jump, jumpHeld: true);
        }, s => second && s.IsGrounded, out var last);
        Assert.True(landed, $"{cls}: never landed after the double jump ({last.PX:F2}, {last.PY:F2}, {last.PZ:F2}).");
        Assert.Equal(TestHelpers.GroundPY(def, Roof), last.PY, precision: 2);
    }

    [Theory]
    [MemberData(nameof(Roster))]
    public void JunctionStep_DoubleJumpPopsOutThroughTheHatch(CharacterClass cls)
    {
        var (sim, def) = Place(cls, 0f, StepTop, 6.5f);
        bool second = false;
        bool landed = Run(sim, 200, (tick, s) =>
        {
            bool jump = tick == 0 || (!second && tick > 8 && s.VY <= 0f);
            if (tick > 0 && jump) second = true;
            bool clear = s.PY - def.CapsuleHeight * 0.5f > LowTubeTop(s.PX) + 0.1f;
            return TestHelpers.Input(moveX: clear ? 1f : 0f, jump: jump, jumpHeld: true);
        }, s => second && s.IsGrounded, out var last);
        Assert.True(landed, $"{cls}: never landed after popping out ({last.PX:F2}, {last.PY:F2}, {last.PZ:F2}).");
        Assert.True(last.PY - def.CapsuleHeight * 0.5f > LowTubeTop(last.PX) - 0.4f,
            $"{cls}: landed back inside the tube at ({last.PX:F2}, {last.PY:F2}, {last.PZ:F2}) instead of on its top.");
    }

    [Theory]
    [MemberData(nameof(Roster))]
    public void OneJumpFromTheShelfFloor_ReachesTheWheelDeck(CharacterClass cls)
    {
        var (sim, def) = Place(cls, 15.4f, Shelf, -5.2f);
        bool landed = Run(sim, 150, (tick, _) => TestHelpers.Input(moveX: 1f, jump: tick == 0, jumpHeld: tick < 12),
            s => s.IsGrounded && s.PX > 17.4f, out var last);
        Assert.True(landed, $"{cls}: one jump did not reach the wheel deck ({last.PX:F2}, {last.PY:F2}, {last.PZ:F2}).");
        Assert.Equal(TestHelpers.GroundPY(def, Deck), last.PY, precision: 2);
    }

    [Theory]
    [InlineData(CharacterClass.FightGuy, 1)]
    [InlineData(CharacterClass.FightGuy, -1)]
    [InlineData(CharacterClass.Bonk, 1)]
    public void WalkingIntoTheGap_IsAKnockOut(CharacterClass cls, int direction)
    {
        var (sim, _) = Place(cls, -11.7f * direction, direction > 0 ? Desk : Shelf, 0f);
        var inputs = new Dictionary<ulong, InputState>();
        for (int tick = 0; tick < 300; tick++)
        {
            inputs[1] = TestHelpers.Input(moveX: tick < 40 ? direction : 0f);
            sim.Tick(inputs);
            if (sim.GetState(1).Deaths > 0) return;
        }
        Assert.Fail($"{cls}/{direction}: the gap between the cages must have no floor.");
    }
}
