using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace SlopArena.Shared.Tests;

public class LogOffArenaTests
{
    // Chopping-block stump (tools/blender/log_off/layout.py SPEC): rim radius
    // 14.5 + 0.3 sin t - 0.2 sin 3t + 0.12 sin 5t, top y = 0, bark to the kill plane.
    private const float ShelfTop = -1.5f;
    private const float ShelfLipX = 18.5f;

    private static ArenaDefinition LoadArena()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var arena = ArenaBinaryFormat.LoadFromFile(Path.Combine(root, "data", "arenas", "log_off.arena"));
        Assert.True(arena.HasValue, "Log Off must ship its baked arena.");
        return arena.Value;
    }

    private static float RimRadius(float theta) =>
        14.5f + 0.3f * MathF.Sin(theta) - 0.2f * MathF.Sin(3f * theta) + 0.12f * MathF.Sin(5f * theta);

    [Fact]
    public void BakedShellMatchesTheChoppingBlock()
    {
        var arena = LoadArena();
        Assert.Equal(-ShelfLipX, arena.MinX, 3);
        Assert.Equal(ShelfLipX, arena.MaxX, 3);
        Assert.Equal(-10f, arena.KillHeight);
        Assert.Equal(20f, arena.KillTop, 3);
        Assert.Equal(-28.5f, arena.KillMinX, 3);
        Assert.Equal(28.5f, arena.KillMaxX, 3);
        var spawns = new[] { (-7f, 5f), (7f, -5f), (-7f, -5f), (7f, 5f) };
        Assert.Equal(spawns.Length, arena.SpawnPoints.Length);
        for (int i = 0; i < spawns.Length; i++)
        {
            Assert.Equal(spawns[i].Item1, arena.SpawnPoints[i].X, 3);
            Assert.Equal(spawns[i].Item2, arena.SpawnPoints[i].Z, 3);
            Assert.Equal(0.85f, arena.SpawnPoints[i].Y, 3);
        }

        Assert.Equal(0f, arena.Heightmap.Sample(0, -8), 3);
        Assert.Equal(float.MinValue, arena.Heightmap.Sample(0, 6));
        Assert.Equal(ShelfTop, arena.Heightmap.Sample(16.5f, 0), 3);
        Assert.Equal(ShelfTop, arena.Heightmap.Sample(-16.5f, 0), 3);
        Assert.Equal(float.MinValue, arena.Heightmap.Sample(0, 16.5f));
        Assert.Equal(float.MinValue, arena.Heightmap.Sample(16.5f, 8f));
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
    public void RunningOffTheFlankRim_LandsOnTheFungusShelf(CharacterClass cls, int direction)
    {
        var arena = LoadArena();
        var def = TestHelpers.ResolveDef(cls);
        var state = TestHelpers.PlayerState(10f * direction, 0f);
        state.PY = TestHelpers.GroundPY(def);
        var sim = TestHelpers.MakeSim(arena);
        sim.RegisterEntity(1, def, state);
        var inputs = new Dictionary<ulong, InputState>();
        bool airborne = false;
        for (int tick = 0; tick < 200; tick++)
        {
            inputs[1] = TestHelpers.Input(moveX: direction);
            sim.Tick(inputs);
            var current = sim.GetState(1);
            Assert.Equal(0, current.Deaths);
            airborne |= !current.IsGrounded;
            if (!airborne || !current.IsGrounded) continue;

            Assert.Equal(TestHelpers.GroundPY(def, ShelfTop), current.PY, precision: 2);
            Assert.InRange(current.PX * direction, 14.5f + def.CapsuleRadius, ShelfLipX);
            return;
        }
        Assert.Fail($"{cls}/{direction}: a full-speed run off the flank rim did not land on the shelf.");
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
    public void OneFullJumpFromTheShelf_ReturnsToTheStumpTop(CharacterClass cls, int direction)
    {
        var arena = LoadArena();
        var def = TestHelpers.ResolveDef(cls);
        var state = TestHelpers.PlayerState(16.5f * direction, 0f);
        state.PY = TestHelpers.GroundPY(def, ShelfTop);
        var sim = TestHelpers.MakeSim(arena);
        sim.RegisterEntity(1, def, state);
        Assert.True(TestHelpers.TickN(sim, default, 10).IsGrounded, $"{cls}/{direction}: shelf did not support the fighter.");
        var inputs = new Dictionary<ulong, InputState>();
        bool airborne = false;
        for (int tick = 0; tick < 150; tick++)
        {
            inputs[1] = TestHelpers.Input(moveX: -direction, jump: tick == 0, jumpHeld: tick < 12);
            sim.Tick(inputs);
            var current = sim.GetState(1);
            Assert.Equal(0, current.Deaths);
            airborne |= !current.IsGrounded;
            if (!airborne || !current.IsGrounded) continue;

            Assert.Equal(TestHelpers.GroundPY(def), current.PY, precision: 2);
            Assert.True(current.PX * direction < 14.5f, $"{cls}/{direction}: landed outside the rim.");
            return;
        }
        Assert.Fail($"{cls}/{direction}: one jump from the shelf did not reach the stump top.");
    }

    [Theory]
    [InlineData(CharacterClass.FightGuy)]
    [InlineData(CharacterClass.Bonk)]
    public void WalkingOffTheShelfLip_FallsToTheBlastPlane(CharacterClass cls)
    {
        var arena = LoadArena();
        var def = TestHelpers.ResolveDef(cls);
        var state = TestHelpers.PlayerState(17.5f, 0f);
        state.PY = TestHelpers.GroundPY(def, ShelfTop);
        var sim = TestHelpers.MakeSim(arena);
        sim.RegisterEntity(1, def, state);
        var inputs = new Dictionary<ulong, InputState>();
        for (int tick = 0; tick < 240; tick++)
        {
            inputs[1] = TestHelpers.Input(moveX: tick < 30 ? 1f : 0f);
            sim.Tick(inputs);
            if (sim.GetState(1).Deaths > 0) return;
        }
        Assert.Fail($"{cls}: the forest below the shelf must not provide a hidden floor.");
    }

    public static IEnumerable<object[]> BarkContacts()
    {
        // Bark between the split mouth and the shelves at a mid and a deep height; the
        // flank bark only below the shelf roots.
        var contacts = new List<(float degrees, float height)>();
        foreach (float degrees in new[] { 30f, 60f, 120f, 150f, 210f, 240f, 270f, 300f, 330f })
        foreach (float height in new[] { -3f, -8f })
            contacts.Add((degrees, height));
        contacts.Add((0f, -7f));
        contacts.Add((180f, -7f));
        foreach (var cls in new[] { CharacterClass.FightGuy, CharacterClass.Manki, CharacterClass.Wibou, CharacterClass.Bonk })
        foreach (var contact in contacts)
            yield return new object[] { cls, contact.degrees, contact.height };
    }

    [Theory]
    [MemberData(nameof(BarkContacts))]
    public void BarkWalls_BlockInwardMovementAndAllowOutwardMovement(CharacterClass cls, float degrees, float height)
    {
        var arena = LoadArena();
        var def = TestHelpers.ResolveDef(cls);
        float theta = degrees * MathF.PI / 180f;
        float rim = RimRadius(theta);
        float ox = MathF.Cos(theta), oz = MathF.Sin(theta);
        var state = TestHelpers.PlayerState(ox * (rim + 1f), oz * (rim + 1f));
        state.PY = height;
        state.IsGrounded = false;
        state.AirTimeTicks = def.Movement.FloatWindowTicks;
        state.State = ActionState.Attacking;
        state.AnimLockTicks = 60;
        state.VX = -ox * 120f;
        state.VZ = -oz * 120f;

        Simulation.SimulateTick(ref state, def, default, arena, out _, out _, DownActionTuning.Default, false);

        float radial = MathF.Sqrt(state.PX * state.PX + state.PZ * state.PZ);
        Assert.True(radial >= rim - 0.03f + def.CapsuleRadius - 0.03f,
            $"{cls}: entered the stump at {degrees} deg, height {height}; radial {radial:F3}, rim {rim:F3}.");
        Assert.False(state.IsGrounded);

        state.VX = ox * 12f;
        state.VZ = oz * 12f;
        Simulation.SimulateTick(ref state, def, default, arena, out _, out _, DownActionTuning.Default, false);
        Assert.True(MathF.Sqrt(state.PX * state.PX + state.PZ * state.PZ) > radial + 0.1f,
            $"{cls}: bark trapped outward movement at {degrees} deg, height {height}.");
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
