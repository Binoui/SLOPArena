using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Short-hop tests (issue #116 / #106, ADR-0016 → ADR-0020): releasing the jump key within
/// <see cref="Simulation.ShortHopWindowTicks"/> of the press produces a reduced jump
/// (<c>MovementStats.ShortHopForce</c>); holding past the window produces the full jump
/// (<c>JumpForce</c>). The decision runs at JumpSquat expiry, deferring the force one tick at
/// a time while the player is still holding inside the window (so a release just past squat
/// expiry still counts as a short hop). Air double jumps always use the air-jump force
/// (<c>JumpForce × AirJumpVMultiplier</c>) — no short hop in the air.
/// </summary>
public class ShortHopTests
{
    // Classic definition: full gravity from the first air tick.
    private static readonly CharacterDefinition Def = CreateClassicDef();
    private static readonly MovementStats Move = Def.Movement;
    private static readonly float GroundPy = Def.CapsuleHeight * 0.5f;
    private static readonly float GravPerTick = Move.Gravity * Simulation.TickDt;
    private static readonly float ShortHopForce = Move.ShortHopForce;

    private static CharacterDefinition CreateClassicDef()
    {
        var def = TestHelpers.EngineDef;
        def = def with { Movement = def.Movement with { FloatWindowTicks = 0 } };
        return def;
    }

    private static ServerSimulation SimWithGroundedPlayer()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);
        return sim;
    }

    [Fact]
    public void TapRelease_ShortHop_ReducedJumpVelocity()
    {
        var sim = SimWithGroundedPlayer();
        TestHelpers.TickN(sim, TestHelpers.Input(jump: true, jumpHeld: true), Move.JumpSquatTicks + 1);
        var s = sim.GetState(1);

        Assert.False(s.IsGrounded);
        Assert.Equal(ActionState.Idle, s.State);
        TestHelpers.AssertNear(ShortHopForce - GravPerTick, s.VY, 0.01f);
    }

    [Fact]
    public void HoldThroughSquat_FullJump()
    {
        // Hold beyond both squat expiry and the short-hop decision window.
        var sim = SimWithGroundedPlayer();
        TestHelpers.TickN(sim, TestHelpers.Input(jump: true, jumpHeld: true), 1);
        TestHelpers.TickHold(sim, TestHelpers.Input(jumpHeld: true),
            Math.Max(Move.JumpSquatTicks, Simulation.ShortHopWindowTicks));
        var s = sim.GetState(1);

        Assert.False(s.IsGrounded);
        TestHelpers.AssertNear(Move.JumpForce - GravPerTick, s.VY, 0.01f);
    }

    [Fact]
    public void ShortHop_PeakHeight_LowerThanFullHop()
    {
        // AC: "short-hop vs full-hop trajectories differ as specified" — the tap jump must
        // peak lower than the held jump.
        var tapSim = SimWithGroundedPlayer();
        var fullSim = SimWithGroundedPlayer();

        TestHelpers.TickN(tapSim, TestHelpers.Input(jump: true, jumpHeld: true), Move.JumpSquatTicks + 1);
        TestHelpers.TickN(fullSim, TestHelpers.Input(jump: true, jumpHeld: true), 1);
        TestHelpers.TickHold(fullSim, TestHelpers.Input(jumpHeld: true),
            Math.Max(Move.JumpSquatTicks, Simulation.ShortHopWindowTicks));

        float tapPeak = 0f, fullPeak = 0f;
        for (int i = 0; i < 90; i++)
        {
            TestHelpers.TickDefault(tapSim, 1);
            TestHelpers.TickDefault(fullSim, 1);
            tapPeak = MathF.Max(tapPeak, tapSim.GetState(1).PY);
            fullPeak = MathF.Max(fullPeak, fullSim.GetState(1).PY);
        }

        Assert.True(tapPeak < fullPeak,
            $"short hop must peak lower: tap={tapPeak:F3} vs full={fullPeak:F3}");
        Assert.True(fullPeak > GroundPy + 0.5f, $"full hop should be a real jump: {fullPeak:F3}");
    }

    [Fact]
    public void ReleaseInsideWindow_AfterSquatExpiry_ShortHop()
    {
        // Hold through squat expiry but release at the decision window boundary.
        var sim = SimWithGroundedPlayer();
        int heldTicks = Math.Max(Move.JumpSquatTicks + 1, Simulation.ShortHopWindowTicks);
        TestHelpers.TickHold(sim, TestHelpers.Input(jump: true, jumpHeld: true), heldTicks);
        Assert.Equal(ActionState.JumpSquat, sim.GetState(1).State);

        TestHelpers.TickN(sim, TestHelpers.Input(), 1);
        var s = sim.GetState(1);
        Assert.Equal(ActionState.Idle, s.State);
        Assert.False(s.IsGrounded);
        TestHelpers.AssertNear(ShortHopForce - GravPerTick, s.VY, 0.01f);
    }

    [Fact]
    public void HoldPastWindow_AtSquatExpiry_FullJump()
    {
        var sim = SimWithGroundedPlayer();
        TestHelpers.TickN(sim, TestHelpers.Input(jump: true, jumpHeld: true), 1);
        int heldTicks = Math.Max(Move.JumpSquatTicks + 1, Simulation.ShortHopWindowTicks + 1);
        TestHelpers.TickHold(sim, TestHelpers.Input(jumpHeld: true), heldTicks - 1);
        var s = sim.GetState(1);
        Assert.Equal(ActionState.Idle, s.State);
        Assert.False(s.IsGrounded);
        TestHelpers.AssertNear(Move.JumpForce - GravPerTick, s.VY, 0.01f);
    }

    [Fact]
    public void AirDoubleJump_UsesAirJumpForce()
    {
        // Air jumps apply the (weaker) air-jump force on the press tick — no short hop in the
        // air (issue #116), but ADR-0020 scales it by AirJumpVMultiplier.
        var sim = SimWithGroundedPlayer();
        TestHelpers.TickN(sim, TestHelpers.Input(jump: true, jumpHeld: true), 1);
        // Finish the full ground jump before testing the independent air-jump edge.
        TestHelpers.TickHold(sim, TestHelpers.Input(jumpHeld: true),
            Math.Max(Move.JumpSquatTicks, Simulation.ShortHopWindowTicks));

        var doubled = TestHelpers.TickN(sim, TestHelpers.Input(jump: true), 1);
        Assert.Equal(0u, doubled.JumpsLeft);
        TestHelpers.AssertNear(Move.JumpForce * Move.AirJumpVMultiplier - GravPerTick, doubled.VY, 0.01f);
    }
}
