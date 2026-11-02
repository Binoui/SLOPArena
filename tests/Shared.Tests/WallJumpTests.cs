using System;
using System.IO;
using Xunit;

namespace SlopArena.Shared.Tests;

public class WallJumpTests
{
    private static readonly CharacterDefinition Def = TestHelpers.EngineDef;
    private static readonly InputState IntoWall = new() { Jump = true, JumpHeld = true, MoveX = -1f };

    private static ArenaDefinition Arena()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return ArenaBinaryFormat.LoadFromFile(Path.Combine(root, "data", "arenas", "slop_pit.arena"))!.Value;
    }

    private static CharacterState BesideWall(byte jumps = 1) => TestHelpers.PlayerState() with
    {
        PX = 16f + Def.CapsuleRadius + 0.01f, PY = -3f,
        IsGrounded = false, JumpsLeft = jumps, VY = -2f,
    };

    private static ServerSimulation Sim(CharacterState state)
    {
        var sim = TestHelpers.MakeSim(Arena());
        TestHelpers.RegisterPlayer(sim, Def, state);
        return sim;
    }

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)1)]
    public void WallJumpPushesUpAndAwayWithoutSpendingAirJump(byte jumps)
    {
        var sim = Sim(BesideWall(jumps) with { VX = -20f, VZ = 3f });
        var after = TestHelpers.TickN(sim, IntoWall, 1);
        Assert.True(after.VY > 0f);
        Assert.True(after.VX > 0f);
        Assert.InRange(MathF.Abs(after.VZ), 0f, 0.01f);
        Assert.Equal(jumps, after.JumpsLeft);
        Assert.True(after.WallJumpUsed);
        Assert.InRange(after.FacingYaw, -MathF.PI / 2f - 0.01f, -MathF.PI / 2f + 0.01f);
    }

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)1)]
    public void UsedWallJumpFallsBackToRemainingAirJump(byte jumps)
    {
        var sim = Sim(BesideWall(jumps) with { WallJumpUsed = true });
        var after = TestHelpers.TickN(sim, IntoWall, 1);
        Assert.Equal((byte)0, after.JumpsLeft);
        Assert.True(after.WallJumpUsed);
        Assert.Equal(jumps > 0, after.VY > 0f);
    }

    [Fact]
    public void LandingRefreshesWallJump()
    {
        var sim = Sim(BesideWall(0) with
        {
            PX = 10f, PZ = 10f, PY = Def.CapsuleHeight * 0.5f + 0.01f,
            VY = -5f, WallJumpUsed = true,
        });
        var after = TestHelpers.TickDefault(sim, 1);
        Assert.True(after.IsGrounded);
        Assert.False(after.WallJumpUsed);
    }

    [Theory]
    [InlineData(1f, 0f)]
    [InlineData(0f, 1f)]
    [InlineData(0f, 0f)]
    public void AwayParallelOrNeutralStickUsesOrdinaryAirJump(float x, float z)
    {
        var sim = Sim(BesideWall());
        var after = TestHelpers.TickN(sim, new InputState { Jump = true, JumpHeld = true, MoveX = x, MoveY = z }, 1);
        Assert.False(after.WallJumpUsed);
        Assert.Equal((byte)0, after.JumpsLeft);
        Assert.True(after.VY > 0f);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HitstunOrKnockbackBlocksWallJump(bool knockback)
    {
        var state = BesideWall(0);
        if (knockback) state.KVX = 5f;
        else { state.HitstunTicks = 20; state.State = ActionState.Hitstun; }
        var after = TestHelpers.TickN(Sim(state), IntoWall, 1);
        Assert.False(after.WallJumpUsed);
        Assert.True(after.VY <= 0f);
    }

    [Theory]
    [InlineData(ActionState.Shell)]
    [InlineData(ActionState.Idle)]
    public void WallJumpClearsFastFallAndExitsShell(ActionState action)
    {
        var after = TestHelpers.TickN(Sim(BesideWall(0) with
        {
            State = action, IsFastFalling = true,
        }), IntoWall, 1);
        Assert.True(after.WallJumpUsed);
        Assert.False(after.IsFastFalling);
        Assert.Equal(ActionState.Idle, after.State);
    }

    [Fact]
    public void TakingHitstunDoesNotRefreshWallJump()
    {
        var after = TestHelpers.TickN(Sim(BesideWall(0) with
        {
            WallJumpUsed = true, State = ActionState.Hitstun, HitstunTicks = 20,
        }), IntoWall, 1);
        Assert.True(after.WallJumpUsed);
    }

    [Fact]
    public void KnockbackLandingRefreshesWallJump()
    {
        var after = TestHelpers.TickDefault(Sim(BesideWall(0) with
        {
            PX = 10f, PZ = 10f, PY = Def.CapsuleHeight * 0.5f + 0.01f,
            KVX = 1f, KVY = -5f, WallJumpUsed = true,
        }), 1);
        Assert.True(after.IsGrounded);
        Assert.False(after.WallJumpUsed);
    }

    [Fact]
    public void SnapshotRestoresUsedWallJump()
    {
        var sim = Sim(BesideWall(0) with { WallJumpUsed = true });
        var snapshot = sim.CaptureSnapshot();
        var copy = Sim(BesideWall(0));
        copy.RestoreSnapshot(snapshot);
        Assert.True(copy.GetState(1).WallJumpUsed);
        Assert.True(snapshot.ContentEquals(copy.CaptureSnapshot()));
    }

    [Fact]
    public void MashingJumpAgainstSingleWallEventuallyFallsToDeathPlane()
    {
        var sim = Sim(BesideWall());
        int wallJumps = 0;
        bool used = false;
        bool exhausted = false;
        for (int tick = 0; tick < 600; tick++)
        {
            var after = TestHelpers.TickN(sim, IntoWall, 1);
            if (after.Deaths > 0)
            {
                Assert.Equal(1, wallJumps);
                Assert.True(exhausted);
                return;
            }
            if (after.WallJumpUsed && !used) wallJumps++;
            used = after.WallJumpUsed;
            exhausted |= used && after.JumpsLeft == 0;
            Assert.False(after.IsGrounded);
        }
        Assert.Fail("Repeated jump presses must exhaust recovery and fall below the death plane.");
    }
}
