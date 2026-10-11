using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace SlopArena.Shared.Tests;

public class LastStopArenaTests
{
    private static ArenaDefinition Load()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return ArenaBinaryFormat.LoadFromFile(Path.Combine(root, "data", "arenas", "last_stop.arena"))
            ?? throw new InvalidOperationException("Last Stop arena could not be loaded.");
    }

    public static IEnumerable<object[]> Approaches()
    {
        foreach (var cls in new[] { CharacterClass.FightGuy, CharacterClass.Manki, CharacterClass.Wibou, CharacterClass.Bonk })
        foreach (int direction in new[] { -1, 1 })
            yield return new object[] { cls, direction };
    }

    [Theory]
    [MemberData(nameof(Approaches))]
    public void CenterRouteCrossesFloorTriangleJoinsWithoutLosingSupport(CharacterClass cls, int direction)
    {
        var arena = Load();
        var def = TestHelpers.ResolveDef(cls);
        var state = TestHelpers.PlayerState(-direction * 14f, 0);
        state.PY = TestHelpers.GroundPY(def);
        var sim = TestHelpers.MakeSim(arena);
        sim.RegisterEntity(1, def, state);
        var inputs = new Dictionary<ulong, InputState> { [1] = TestHelpers.Input(moveX: direction) };
        bool crossed = false;
        for (int tick = 0; tick < 600; tick++)
        {
            sim.Tick(inputs);
            var current = sim.GetState(1);
            Assert.True(current.IsGrounded, $"{cls}: lost floor support at tick {tick}, x={current.PX}, y={current.PY}.");
            Assert.InRange(current.PY, TestHelpers.GroundPY(def) - .03f, TestHelpers.GroundPY(def) + .03f);
            if (direction * current.PX >= 14f) { crossed = true; break; }
        }
        Assert.True(crossed, $"{cls}: stalled on a floor seam.");
    }

    [Theory]
    [MemberData(nameof(Approaches))]
    public void OneHeldJumpReachesEitherBaggagePlatform(CharacterClass cls, int direction)
    {
        var arena = Load();
        var def = TestHelpers.ResolveDef(cls);
        var state = TestHelpers.PlayerState(direction * 6f, direction * 3f);
        state.PY = TestHelpers.GroundPY(def);
        var sim = TestHelpers.MakeSim(arena);
        sim.RegisterEntity(1, def, state);
        var inputs = new Dictionary<ulong, InputState>();
        bool airborne = false;
        for (int tick = 0; tick < 150; tick++)
        {
            var current = sim.GetState(1);
            inputs[1] = TestHelpers.Input(moveX: direction * current.PX < 9f ? direction : 0f,
                jump: tick == 0, jumpHeld: tick < 10);
            sim.Tick(inputs);
            airborne |= !sim.GetState(1).IsGrounded;
        }
        var landed = sim.GetState(1);
        Assert.True(airborne, "High ground requires a jump.");
        Assert.True(landed.IsGrounded, $"{cls}: did not settle on the baggage platform.");
        Assert.InRange(landed.PY, TestHelpers.GroundPY(def, 1.5f) - .03f, TestHelpers.GroundPY(def, 1.5f) + .03f);
        Assert.InRange(direction * landed.PX, 7.5f, 12.5f);
    }

    [Theory]
    [MemberData(nameof(Approaches))]
    public void VisiblePlatformFoundationBlocksInwardRecoveryAndAllowsRetreat(CharacterClass cls, int direction)
    {
        var arena = Load();
        var def = TestHelpers.ResolveDef(cls);
        var state = TestHelpers.PlayerState(direction * 19f, 0);
        state.PY = -5;
        state.IsGrounded = false;
        state.AirTimeTicks = def.Movement.FloatWindowTicks;
        state.State = ActionState.Attacking;
        state.AnimLockTicks = 60;
        state.VX = -direction * 120f;
        Simulation.SimulateTick(ref state, def, default, arena, out _, out _, DownActionTuning.Default, false);
        float clearance = direction * state.PX - 18;
        Assert.InRange(clearance, def.CapsuleRadius - .03f, def.CapsuleRadius + .05f);
        Assert.False(state.IsGrounded);
        state.VX = direction * 12f;
        Simulation.SimulateTick(ref state, def, default, arena, out _, out _, DownActionTuning.Default, false);
        Assert.True(direction * state.PX - 18 > clearance + .1f, $"{cls}: foundation trapped outward recovery.");
    }
}
