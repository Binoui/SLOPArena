using System.Collections.Generic;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Walk-off regression: running off a platform must start falling immediately, not ride
/// the float window (AirFloatGravity = 0 → a full FloatWindowTicks of zero gravity, a
/// ~0.5-0.67s air-run off the ledge), and must not self-grab the ledge it just left.
/// </summary>
public class LedgeWalkOffTests
{
    /// <summary>Arena: a 16-cell-wide platform at y=6 floating over void.</summary>
    private static ArenaDefinition PlatformArena(float platformY = 6f, int edgeCellX = 16)
    {
        const int cells = 200;
        var data = new float[cells * cells];
        for (int i = 0; i < data.Length; i++) data[i] = float.MinValue; // no surface
        for (int z = 0; z < cells; z++)
            for (int x = 0; x < edgeCellX; x++)
                data[z * cells + x] = platformY;
        return new ArenaDefinition
        {
            Name = "probe-platform",
            DisplayName = "Probe Platform",
            KillHeight = -20f,
            SpawnPoints = new[] { new SpawnPoint { X = 12, Y = platformY, Z = 100, Yaw = 0 } },
            Heightmap = new ArenaHeightmap
            {
                Data = data,
                Width = cells,
                Height = cells,
                CellSize = 1f,
                OriginX = 0f,
                OriginZ = 0f,
            },
        };
    }

    [Theory]
    [InlineData(CharacterClass.FightGuy)]
    [InlineData(CharacterClass.Manki)]
    [InlineData(CharacterClass.Wibou)]
    [InlineData(CharacterClass.Bonk)]
    public void RunOffPlatform_FallsImmediately_NoHover_NoSelfGrab(CharacterClass cls)
    {
        const float platformY = 6f;
        const int edgeCell = 16; // solid cells x in [0,16) — world edge at x=16
        var def = TestHelpers.ResolveDef(cls);
        var sim = TestHelpers.MakeSim(PlatformArena(platformY, edgeCell));
        var s = TestHelpers.PlayerState(12f, 100f);
        s.PY = platformY + def.CapsuleHeight * 0.5f;
        s.State = ActionState.Run;
        s.IsGrounded = true;
        s.VX = def.Movement.RunSpeed;
        s.VZ = 0f;
        sim.RegisterEntity(1, def, s);
        var input = TestHelpers.Input(moveX: 1f);

        CharacterState leave = default;
        for (int t = 0; t < 60; t++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { { 1, input } });
            var st = sim.GetState(1);
            if (st.State == ActionState.LedgeHang)
                Assert.Fail("walking off a platform must not self-grab the ledge");
            if (leave.EntityId == 0 && !st.IsGrounded)
                leave = st; // first airborne tick
        }
        Assert.True(leave.EntityId != 0, "character never left the platform");
        Assert.True(leave.VY <= 0f && !leave.IsGrounded, "must be airborne after walking off");
        // Immediate fall: gravity must already be (or be about to be) applied — the float
        // window is leaped, so no ~40-tick zero-gravity hover. Within a few ticks VY is
        // clearly negative and height is dropping.
        Assert.True(leave.AirTimeTicks >= def.Movement.FloatWindowTicks
            || leave.VY < 0f, "walk-off must not ride the float window");
    }

    [Fact]
    public void RunOffPlatform_FallsWithinTwoTicks()
    {
        const float platformY = 6f;
        const int edgeCell = 16;
        var def = BuiltInContentResolver.Resolve(CharacterClass.FightGuy).Definition;
        var sim = TestHelpers.MakeSim(PlatformArena(platformY, edgeCell));
        var s = TestHelpers.PlayerState(12f, 100f);
        s.PY = platformY + def.CapsuleHeight * 0.5f;
        s.State = ActionState.Run;
        s.IsGrounded = true;
        s.VX = def.Movement.RunSpeed;
        s.VZ = 0f;
        sim.RegisterEntity(1, def, s);
        var input = TestHelpers.Input(moveX: 1f);

        int leaveTick = -1;
        for (int t = 0; t < 60; t++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { { 1, input } });
            var st = sim.GetState(1);
            if (leaveTick < 0 && !st.IsGrounded) leaveTick = t;
            if (leaveTick >= 0 && st.VY < 0f)
            {
                // Falling must start within 2 ticks of leaving the platform (no hover).
                Assert.True(t - leaveTick <= 2, $"fall started {t - leaveTick} ticks after leaving (should be ≤2)");
                Assert.True(st.PY < platformY + def.CapsuleHeight * 0.5f - 0.001f, "must have started losing height");
                return;
            }
        }
        Assert.Fail("character never started falling after walking off the platform");
    }

    [Theory]
    [InlineData(CharacterClass.FightGuy)]
    [InlineData(CharacterClass.Manki)]
    [InlineData(CharacterClass.Wibou)]
    [InlineData(CharacterClass.Bonk)]
    public void RunOffPlatform_FastFallWorksImmediately(CharacterClass cls)
    {
        const float platformY = 6f;
        const int edgeCell = 16;
        var def = TestHelpers.ResolveDef(cls);
        var sim = TestHelpers.MakeSim(PlatformArena(platformY, edgeCell));
        var s = TestHelpers.PlayerState(12f, 100f);
        s.PY = platformY + def.CapsuleHeight * 0.5f;
        s.State = ActionState.Run;
        s.IsGrounded = true;
        s.VX = def.Movement.RunSpeed;
        s.VZ = 0f;
        sim.RegisterEntity(1, def, s);
        var input = TestHelpers.Input(moveX: 1f);

        int leaveTick = -1;
        for (int t = 0; t < 60; t++)
        {
            var before = sim.GetState(1);
            input.DownPressed = !before.IsGrounded && before.VY < 0f && !before.IsFastFalling;
            input.Down = !before.IsGrounded;
            sim.Tick(new Dictionary<ulong, InputState> { { 1, input } });
            var st = sim.GetState(1);
            if (leaveTick < 0 && !st.IsGrounded) leaveTick = t;
            if (leaveTick >= 0 && t - leaveTick == 3)
            {
                // A fresh descending edge must bypass the ordinary float window.
                Assert.True(st.VY <= -def.Movement.FastFallSpeed + 0.1f,
                    $"fast-fall must engage by 3 ticks after leaving; VY={st.VY:F3}");
                return;
            }
        }
        Assert.Fail("character never left the platform");
    }
}
