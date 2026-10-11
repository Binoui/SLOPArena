using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace SlopArena.Shared.Tests;

public class LogOffArenaTests
{
    private static ArenaDefinition LoadArena()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var arena = ArenaBinaryFormat.LoadFromFile(Path.Combine(root, "data", "arenas", "log_off.arena"));
        Assert.True(arena.HasValue, "Log Off must ship its baked arena.");
        return arena.Value;
    }

    [Theory]
    [InlineData(CharacterClass.FightGuy, -1)]
    [InlineData(CharacterClass.FightGuy, 1)]
    [InlineData(CharacterClass.Manki, -1)]
    [InlineData(CharacterClass.Manki, 1)]
    [InlineData(CharacterClass.Wibou, -1)]
    [InlineData(CharacterClass.Wibou, 1)]
    [InlineData(CharacterClass.Bonk, -1)]
    [InlineData(CharacterClass.Bonk, 1)]
    public void RunningFullJump_CrossesSplitAndLandsOnOppositeShoulder(CharacterClass cls, int direction)
    {
        var arena = LoadArena();
        var def = TestHelpers.ResolveDef(cls);
        var state = TestHelpers.PlayerState(-7f * direction, 6f);
        state.PY = TestHelpers.GroundPY(def);
        var sim = TestHelpers.MakeSim(arena);
        sim.RegisterEntity(1, def, state);
        var inputs = new Dictionary<ulong, InputState>();
        int jumpTick = -1;
        bool airborne = false;
        bool crossed = false;
        for (int tick = 0; tick < 160; tick++)
        {
            var before = sim.GetState(1);
            bool press = jumpTick < 0 && before.PX * direction >= -3.1f;
            if (press) jumpTick = tick;
            inputs[1] = TestHelpers.Input(moveX: direction, jump: press,
                jumpHeld: jumpTick >= 0 && tick - jumpTick < 10);
            sim.Tick(inputs);
            var current = sim.GetState(1);
            Assert.Equal(0, current.Deaths);
            airborne |= jumpTick >= 0 && !current.IsGrounded && current.VY > 0;
            crossed |= airborne && !current.IsGrounded && current.PX * direction > 1.5f;
            if (!airborne || !current.IsGrounded) continue;

            Assert.True(crossed, $"{cls}/{direction}: landed without crossing the split at tick {tick}.");
            Assert.True(current.PX * direction > 1.5f + def.CapsuleRadius,
                $"{cls}/{direction}: landing body overlaps the split lip at X={current.PX:F3}.");
            Assert.Equal(TestHelpers.GroundPY(def), current.PY, precision: 2);
            TestHelpers.TickN(sim, default, 20);
            Assert.True(sim.GetState(1).IsGrounded, $"{cls}/{direction}: landing did not retain support.");
            return;
        }
        Assert.Fail($"{cls}/{direction}: no opposite-shoulder landing; final X={sim.GetState(1).PX:F3}.");
    }

    [Theory]
    [InlineData(CharacterClass.FightGuy)]
    [InlineData(CharacterClass.Manki)]
    [InlineData(CharacterClass.Wibou)]
    [InlineData(CharacterClass.Bonk)]
    public void GroundRoute_ConnectsShouldersWithoutJumping(CharacterClass cls)
    {
        var arena = LoadArena();
        var def = TestHelpers.ResolveDef(cls);
        var state = TestHelpers.PlayerState(-7, 6);
        state.PY = TestHelpers.GroundPY(def);
        var sim = TestHelpers.MakeSim(arena);
        sim.RegisterEntity(1, def, state);
        var inputs = new Dictionary<ulong, InputState>();
        int leg = 0;
        for (int tick = 0; tick < 600; tick++)
        {
            var before = sim.GetState(1);
            if (leg == 0 && before.PZ < -4f) leg = 1;
            if (leg == 1 && before.PX > 7f) leg = 2;
            if (leg == 2 && before.PZ > 6f) return;
            inputs[1] = leg switch
            {
                0 => TestHelpers.Input(moveY: -1),
                1 => TestHelpers.Input(moveX: 1),
                _ => TestHelpers.Input(moveY: 1)
            };
            sim.Tick(inputs);
            var current = sim.GetState(1);
            Assert.True(current.IsGrounded, $"{cls}: ground route lost support at ({current.PX:F2}, {current.PZ:F2}).");
            Assert.Equal(0, current.Deaths);
        }
        Assert.Fail($"{cls}: ground route did not reach the opposite shoulder.");
    }

    [Theory]
    [InlineData(CharacterClass.FightGuy)]
    [InlineData(CharacterClass.Manki)]
    [InlineData(CharacterClass.Wibou)]
    [InlineData(CharacterClass.Bonk)]
    public void FourSpawns_StayGroundedAndSplitDropReachesBlastPlane(CharacterClass cls)
    {
        var arena = LoadArena();
        var def = TestHelpers.ResolveDef(cls);
        Assert.Equal(4, arena.SpawnPoints.Length);
        foreach (var spawn in arena.SpawnPoints)
        {
            var sim = TestHelpers.MakeSim(arena);
            var state = TestHelpers.PlayerState(spawn.X, spawn.Z);
            state.PY = TestHelpers.GroundPY(def);
            sim.RegisterEntity(1, def, state);
            var final = TestHelpers.TickN(sim, default, 90);
            Assert.True(final.IsGrounded, $"{cls}: spawn ({spawn.X}, {spawn.Z}) lost support.");
            Assert.Equal(0, final.Deaths);
        }

        Assert.Equal(float.MinValue, arena.Heightmap.Sample(0, 6));
        var drop = TestHelpers.MakeSim(arena);
        var falling = TestHelpers.PlayerState(0, 6);
        falling.PY = -0.5f;
        falling.IsGrounded = false;
        falling.JumpsLeft = 0;
        drop.RegisterEntity(1, def, falling);
        Assert.True(TestHelpers.TickN(drop, default, 150).Deaths > 0,
            $"{cls}: the split must remain a lethal drop, without a hidden floor.");
    }

    [Theory]
    [InlineData(CharacterClass.FightGuy, -1)]
    [InlineData(CharacterClass.FightGuy, 1)]
    [InlineData(CharacterClass.Manki, -1)]
    [InlineData(CharacterClass.Manki, 1)]
    [InlineData(CharacterClass.Wibou, -1)]
    [InlineData(CharacterClass.Wibou, 1)]
    [InlineData(CharacterClass.Bonk, -1)]
    [InlineData(CharacterClass.Bonk, 1)]
    public void UpperSplit_OneAirJumpRecoversOntoEitherShoulder(CharacterClass cls, int direction)
    {
        var arena = LoadArena();
        var def = TestHelpers.ResolveDef(cls);
        var state = TestHelpers.PlayerState(direction, 6);
        state.PY = -0.25f;
        state.IsGrounded = false;
        state.JumpsLeft = 1;
        var sim = TestHelpers.MakeSim(arena);
        sim.RegisterEntity(1, def, state);
        var inputs = new Dictionary<ulong, InputState>();
        for (int tick = 0; tick < 120; tick++)
        {
            inputs[1] = TestHelpers.Input(moveX: direction, jump: tick == 0, jumpHeld: tick < 10);
            sim.Tick(inputs);
            var current = sim.GetState(1);
            Assert.Equal(0, current.Deaths);
            if (!current.IsGrounded) continue;
            Assert.True(current.PX * direction > 1.5f + def.CapsuleRadius,
                $"{cls}/{direction}: recovery did not clear the inner split wall.");
            Assert.Equal(TestHelpers.GroundPY(def), current.PY, precision: 2);
            return;
        }
        Assert.Fail($"{cls}/{direction}: upper-split recovery did not land.");
    }
}
