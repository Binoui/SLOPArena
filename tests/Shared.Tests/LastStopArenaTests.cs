using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// "Mind the Gap": an island platform (main floor, top 0) faces a parked train (roof 1.8,
/// cab 2.7) across an open 3-unit gap; Portal Rock (top 3.8) ends the train at the tunnel.
/// </summary>
public class LastStopArenaTests
{
    private const float GapSouth = 0f, GapNorth = 3f, TrainNorth = 7f;
    private const float RoofY = 1.8f, CabY = 2.7f, RockY = 3.7f;

    private static ArenaDefinition Load()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return ArenaBinaryFormat.LoadFromFile(Path.Combine(root, "data", "arenas", "last_stop.arena"))
            ?? throw new InvalidOperationException("Last Stop arena could not be loaded.");
    }

    private static readonly CharacterClass[] Roster =
        { CharacterClass.FightGuy, CharacterClass.Manki, CharacterClass.Wibou, CharacterClass.Bonk };

    public static IEnumerable<object[]> Characters()
    {
        foreach (var cls in Roster) yield return new object[] { cls };
    }

    public static IEnumerable<object[]> Approaches()
    {
        foreach (var cls in Roster)
        foreach (int direction in new[] { -1, 1 })
            yield return new object[] { cls, direction };
    }

    public static IEnumerable<object[]> TrainCars()
    {
        foreach (var cls in Roster)
        foreach (float x in new[] { -14f, -3f, 9f })
            yield return new object[] { cls, x };
    }

    private static CharacterState Grounded(CharacterDefinition def, float x, float z, float floorY)
    {
        var state = TestHelpers.PlayerState(x, z);
        state.PY = TestHelpers.GroundPY(def, floorY);
        return state;
    }

    /// <summary>
    /// Steer toward (moveX, moveY), press a held full jump once <paramref name="jumpWhen"/> fires,
    /// stop steering once <paramref name="stopSteering"/> fires, and return the first landing.
    /// </summary>
    private static CharacterState JumpRoute(ArenaDefinition arena, CharacterDefinition def, CharacterState start,
        float moveX, float moveY, Func<CharacterState, bool> jumpWhen, Func<CharacterState, bool> stopSteering)
    {
        var sim = TestHelpers.MakeSim(arena);
        sim.RegisterEntity(1, def, start);
        var inputs = new Dictionary<ulong, InputState>();
        int jumpTick = -1;
        bool airborne = false;
        for (int tick = 0; tick < 240; tick++)
        {
            var before = sim.GetState(1);
            bool press = jumpTick < 0 && jumpWhen(before);
            if (press) jumpTick = tick;
            bool steer = !stopSteering(before);
            inputs[1] = TestHelpers.Input(moveX: steer ? moveX : 0f, moveY: steer ? moveY : 0f,
                jump: press, jumpHeld: jumpTick >= 0 && tick - jumpTick < 12);
            sim.Tick(inputs);
            var current = sim.GetState(1);
            Assert.Equal(0, current.Deaths);
            airborne |= jumpTick >= 0 && !current.IsGrounded;
            if (airborne && current.IsGrounded) return current;
        }
        Assert.Fail($"{def.Class}: jump route never landed; final ({sim.GetState(1).PX:F2}, {sim.GetState(1).PY:F2}, {sim.GetState(1).PZ:F2}).");
        return default;
    }

    [Theory]
    [MemberData(nameof(Characters))]
    public void FourPlatformSpawns_StayGrounded(CharacterClass cls)
    {
        var arena = Load();
        var def = TestHelpers.ResolveDef(cls);
        Assert.Equal(4, arena.SpawnPoints.Length);
        foreach (var spawn in arena.SpawnPoints)
        {
            Assert.InRange(spawn.Z, -12f, GapSouth);
            var sim = TestHelpers.MakeSim(arena);
            sim.RegisterEntity(1, def, Grounded(def, spawn.X, spawn.Z, 0f));
            var final = TestHelpers.TickN(sim, default, 90);
            Assert.True(final.IsGrounded, $"{cls}: spawn ({spawn.X}, {spawn.Z}) lost support.");
            Assert.Equal(TestHelpers.GroundPY(def), final.PY, precision: 2);
            Assert.Equal(0, final.Deaths);
        }
    }

    [Theory]
    [MemberData(nameof(Approaches))]
    public void PlatformLength_RunsAlongTheGapWithoutLosingSupport(CharacterClass cls, int direction)
    {
        var arena = Load();
        var def = TestHelpers.ResolveDef(cls);
        var sim = TestHelpers.MakeSim(arena);
        sim.RegisterEntity(1, def, Grounded(def, -direction * 15f, -1.2f, 0f));
        var inputs = new Dictionary<ulong, InputState> { [1] = TestHelpers.Input(moveX: direction) };
        for (int tick = 0; tick < 600; tick++)
        {
            sim.Tick(inputs);
            var current = sim.GetState(1);
            Assert.True(current.IsGrounded, $"{cls}: lost platform support at x={current.PX:F2}.");
            Assert.InRange(current.PY, TestHelpers.GroundPY(def) - .03f, TestHelpers.GroundPY(def) + .03f);
            if (direction * current.PX >= 15f) return;
        }
        Assert.Fail($"{cls}: stalled along the platform.");
    }

    [Theory]
    [MemberData(nameof(Characters))]
    public void Gap_IsALethalDropWithoutAHiddenFloor(CharacterClass cls)
    {
        var arena = Load();
        var def = TestHelpers.ResolveDef(cls);
        foreach (float x in new[] { -14f, 0f, 14f })
        {
            Assert.Equal(float.MinValue, arena.Heightmap.Sample(x, (GapSouth + GapNorth) * 0.5f));
            var sim = TestHelpers.MakeSim(arena);
            var falling = TestHelpers.PlayerState(x, 1.5f);
            falling.PY = -0.5f;
            falling.IsGrounded = false;
            falling.JumpsLeft = 0;
            sim.RegisterEntity(1, def, falling);
            Assert.True(TestHelpers.TickN(sim, default, 150).Deaths > 0,
                $"{cls}: the gap at x={x} must stay lethal.");
        }
    }

    [Theory]
    [MemberData(nameof(TrainCars))]
    public void RunningJumpFromThePlatformEdge_LandsOnTheTrainRoof(CharacterClass cls, float x)
    {
        var arena = Load();
        var def = TestHelpers.ResolveDef(cls);
        var landed = JumpRoute(arena, def, Grounded(def, x, -4f, 0f), 0f, 1f,
            s => s.PZ >= GapSouth - 0.35f - def.CapsuleRadius, s => s.PZ > 4.2f);
        Assert.Equal(TestHelpers.GroundPY(def, RoofY), landed.PY, precision: 2);
        Assert.InRange(landed.PZ, GapNorth, TrainNorth);
    }

    [Theory]
    [MemberData(nameof(Characters))]
    public void TrainRoof_OneJumpReachesPortalRock(CharacterClass cls)
    {
        var arena = Load();
        var def = TestHelpers.ResolveDef(cls);
        var landed = JumpRoute(arena, def, Grounded(def, 11f, 5f, RoofY), 1f, 0f,
            s => s.PX >= 15.2f - def.CapsuleRadius, s => s.PX > 17.5f);
        Assert.InRange(landed.PY, TestHelpers.GroundPY(def, RockY) - .05f, TestHelpers.GroundPY(def, RockY) + .03f);
        Assert.True(landed.PX + def.CapsuleRadius > 16.1f, $"{cls}: landed short of Portal Rock at x={landed.PX:F2}.");
    }

    [Theory]
    [InlineData(CharacterClass.FightGuy)]
    [InlineData(CharacterClass.Wibou)]
    [InlineData(CharacterClass.Bonk)]
    public void CarriageRoof_OneJumpReachesTheCab(CharacterClass cls)
    {
        var arena = Load();
        var def = TestHelpers.ResolveDef(cls);
        var landed = JumpRoute(arena, def, Grounded(def, -6f, 5f, RoofY), -1f, 0f,
            s => s.PX <= -6.6f, s => s.PX < -9f);
        Assert.Equal(TestHelpers.GroundPY(def, CabY), landed.PY, precision: 2);
        Assert.InRange(landed.PX, -11f, -8f);
    }

    [Theory]
    [MemberData(nameof(Characters))]
    public void GapLip_OneAirJumpRecoversOntoThePlatform(CharacterClass cls)
    {
        var arena = Load();
        var def = TestHelpers.ResolveDef(cls);
        var state = TestHelpers.PlayerState(0f, 1.5f);
        state.PY = -0.25f;
        state.IsGrounded = false;
        state.JumpsLeft = 1;
        var sim = TestHelpers.MakeSim(arena);
        sim.RegisterEntity(1, def, state);
        var inputs = new Dictionary<ulong, InputState>();
        for (int tick = 0; tick < 120; tick++)
        {
            inputs[1] = TestHelpers.Input(moveY: -1f, jump: tick == 0, jumpHeld: tick < 10);
            sim.Tick(inputs);
            var current = sim.GetState(1);
            Assert.Equal(0, current.Deaths);
            if (!current.IsGrounded) continue;
            Assert.True(current.PZ < GapSouth - def.CapsuleRadius + .05f,
                $"{cls}: recovery did not clear the platform lip.");
            Assert.Equal(TestHelpers.GroundPY(def), current.PY, precision: 2);
            return;
        }
        Assert.Fail($"{cls}: gap recovery did not land.");
    }

    public static IEnumerable<object[]> OuterWalls()
    {
        foreach (var cls in Roster)
        {
            yield return new object[] { cls, 0f, -1f, -12f };  // platform south face
            yield return new object[] { cls, 0f, 1f, TrainNorth }; // train north face
            yield return new object[] { cls, -1f, 0f, -16f }; // platform west end
        }
    }

    [Theory]
    [MemberData(nameof(OuterWalls))]
    public void OuterWalls_BlockInwardRecoveryAndAllowRetreat(CharacterClass cls, float dirX, float dirZ, float face)
    {
        var arena = Load();
        var def = TestHelpers.ResolveDef(cls);
        float outsideX = dirX != 0f ? face + dirX * 1.5f : -6f;
        float outsideZ = dirZ != 0f ? face + dirZ * 1.5f : -6f;
        var state = TestHelpers.PlayerState(outsideX, outsideZ);
        state.PY = -5f;
        state.IsGrounded = false;
        state.AirTimeTicks = def.Movement.FloatWindowTicks;
        state.State = ActionState.Attacking;
        state.AnimLockTicks = 60;
        state.VX = -dirX * 120f;
        state.VZ = -dirZ * 120f;
        Simulation.SimulateTick(ref state, def, default, arena, out _, out _, DownActionTuning.Default, false);
        float clearance = dirX != 0f ? dirX * (state.PX - face) : dirZ * (state.PZ - face);
        Assert.InRange(clearance, def.CapsuleRadius - .03f, def.CapsuleRadius + .05f);
        Assert.False(state.IsGrounded);
        state.VX = dirX * 12f;
        state.VZ = dirZ * 12f;
        Simulation.SimulateTick(ref state, def, default, arena, out _, out _, DownActionTuning.Default, false);
        float after = dirX != 0f ? dirX * (state.PX - face) : dirZ * (state.PZ - face);
        Assert.True(after > clearance + .1f, $"{cls}: wall trapped outward recovery.");
    }
}
