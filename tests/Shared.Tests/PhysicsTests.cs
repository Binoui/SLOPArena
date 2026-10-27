using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Tests for state machine transitions (ActionState changes) and movement physics.
/// </summary>
public class PhysicsTests
{
    // Classic definition with FloatWindowTicks=0 for full-gravity mechanics tests.
    private static readonly CharacterDefinition Def = CreateClassicDef();
    private static readonly MovementStats Move = Def.Movement;
    private static readonly float GroundPx = Def.CapsuleHeight * 0.5f;
    private static readonly float GravPerTick = Move.Gravity * Simulation.TickDt;
    private static int FullJumpDecisionTicksAfterSquatRemainder =>
        Simulation.ShortHopWindowTicks > Move.JumpSquatTicks
            ? Simulation.ShortHopWindowTicks - Move.JumpSquatTicks + 1
            : 1;

    private static CharacterDefinition CreateClassicDef()
    {
        var def = TestHelpers.EngineDef;
        def = def with { Movement = def.Movement with { FloatWindowTicks = 0 } };
        return def;
    }
    // ── Jump ──

    [Fact]
    public void GroundJump_EnterJumpSquatThenJump()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = GroundPx;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Tick with jump input (held — a held jump is the full jump; taps short-hop, issue #116)
        var t0 = TestHelpers.TickHold(sim, TestHelpers.Input(jump: true, jumpHeld: true), 1);
        Assert.Equal(ActionState.JumpSquat, t0.State);
        Assert.Equal(Move.JumpSquatTicks, (int)t0.StateTicks);
        Assert.Equal(1u, t0.JumpsLeft);

        // The rest of the squat ticks (still holding)
        for (int i = 1; i < Move.JumpSquatTicks; i++)
        {
            var s = TestHelpers.TickHold(sim, TestHelpers.Input(jumpHeld: true), 1);
            Assert.Equal(ActionState.JumpSquat, s.State);
        }

        // Squat expiry is deferred while a held input is still inside the short-hop
        // window; continue through that boundary so this is a genuine full jump.
        var tJump = TestHelpers.TickHold(
            sim, TestHelpers.Input(jumpHeld: true), FullJumpDecisionTicksAfterSquatRemainder);
        Assert.Equal(ActionState.Idle, tJump.State);
        Assert.False(tJump.IsGrounded);
        TestHelpers.AssertNear(Move.JumpForce - GravPerTick, tJump.VY, 0.01f);
    }

    [Fact]
    public void DoubleJump_ConsumesJumpsLeft()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = GroundPx;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Squat to get airborne (held = full ground jump; the jump EDGE is 1 tick, the
        // hold continues — holding the edge would re-trigger as a double jump on fire).
        TestHelpers.TickN(sim, TestHelpers.Input(jump: true, jumpHeld: true), 1);
        TestHelpers.TickHold(sim, TestHelpers.Input(jumpHeld: true),
            Math.Max(Move.JumpSquatTicks, Simulation.ShortHopWindowTicks));
        var afterJump = sim.GetState(1);
        Assert.False(afterJump.IsGrounded);
        Assert.Equal(1u, afterJump.JumpsLeft);

        // Double jump in air (air jumps are always full — no short hop in the air, issue #116)
        var doubled = TestHelpers.TickN(sim, TestHelpers.Input(jump: true), 1);
        Assert.Equal(0u, doubled.JumpsLeft);
        TestHelpers.AssertNear(Move.JumpForce * Move.AirJumpVMultiplier - GravPerTick, doubled.VY, 0.01f);
    }

    [Fact]
    public void GroundJump_PreservesHorizontalMomentum()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = GroundPx;
        state.VX = Move.RunSpeed; // running at run speed
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Enter JumpSquat — VX preserved (not zeroed)
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(jump: true, jumpHeld: true), 1);
        Assert.Equal(ActionState.JumpSquat, t0.State);
        Assert.Equal(Move.RunSpeed, t0.VX);

        // Remainder of JumpSquat — VX stays at run speed (no friction during squat)
        for (int i = 1; i < Move.JumpSquatTicks; i++)
        {
            var s = TestHelpers.TickHold(sim, TestHelpers.Input(jumpHeld: true), 1);
            Assert.Equal(ActionState.JumpSquat, s.State);
            Assert.Equal(Move.RunSpeed, s.VX);
        }

        // Continue through the deferred boundary before asserting the airborne state.
        var tJump = TestHelpers.TickHold(
            sim, TestHelpers.Input(jumpHeld: true), FullJumpDecisionTicksAfterSquatRemainder);
        Assert.False(tJump.IsGrounded);
        Assert.True(tJump.VX > 0f, $"Expected VX > 0 after jump, got {tJump.VX:F3}");
    }

    [Fact]
    public void JumpBlocked_NoJumpsLeft()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = 2f; // well above ground snap window (0.75), so gravity is the only VY modifier
        state.IsGrounded = false;
        state.JumpsLeft = 0;
        state.VY = 5f;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var after = TestHelpers.TickN(sim, TestHelpers.Input(jump: true), 1);
        Assert.Equal(0u, after.JumpsLeft);
        // VY decays by gravity (no new jump force)
        TestHelpers.AssertNear(5f - GravPerTick, after.VY, 0.01f);
    }

    [Fact]
    public void JumpBlocked_DuringHitstun()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = GroundPx;
        state.HitstunTicks = 5;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var after = TestHelpers.TickN(sim, TestHelpers.Input(jump: true), 1);
        Assert.Equal(0f, after.VY);
        Assert.Equal(4, (int)after.HitstunTicks);
    }


    // ── Landing ──

    [Fact]
    public void Land_ResetsJumpsAndAirDodges()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = 5f;
        state.IsGrounded = false;
        state.JumpsLeft = 0;
        state.AirDodgesLeft = 0;
        state.VY = -35f;
        TestHelpers.RegisterPlayer(sim, Def, state);

        for (int i = 0; i < 120; i++)
            TestHelpers.TickDefault(sim, 1);

        var landed = sim.GetState(1);
        Assert.True(landed.IsGrounded);
        Assert.Equal(2u, landed.JumpsLeft);
        Assert.Equal(1u, landed.AirDodgesLeft);
    }

    // ── Run / Friction ──

    [Fact]
    public void RunForward_MovesPosition()
    {
        var arena = TestHelpers.TestArena();
        var state = TestHelpers.PlayerState();
        var sim = TestHelpers.MakeSim(arena);
        state.PY = GroundPx;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Feed move input every tick for 60 ticks
        CharacterState final = default;
        for (int i = 0; i < 60; i++)
        {
            sim.Tick(new() { { 1, TestHelpers.Input(moveY: 1f) } });
            final = sim.GetState(1);
        }

        // Single Run tier: VZ accelerates to RunSpeed and holds there.
        Assert.Equal(ActionState.Run, final.State);
        TestHelpers.AssertNear(Move.RunSpeed, final.VZ, 0.1f);
        Assert.True(final.PZ > 5f, $"Expected meaningful forward progress, got PZ={final.PZ:F2}");
    }

    [Fact]
    public void Run_AcceleratesToRunSpeed()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = GroundPx;
        TestHelpers.RegisterPlayer(sim, Def, state);

        for (int i = 0; i < 40; i++)
            sim.Tick(new() { { 1, TestHelpers.Input(moveY: 1f) } });

        var s = sim.GetState(1);
        Assert.Equal(ActionState.Run, s.State);
        TestHelpers.AssertNear(Move.RunSpeed, s.VZ, 0.1f);
    }

    [Fact]
    public void RunReversal_AtCruiseFlipsInstantly()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var state = TestHelpers.PlayerState(50f, 50f);
        state.PY = GroundPx;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Reach full speed before reversing.
        for (int i = 0; i < 30; i++)
            sim.Tick(new() { { 1, TestHelpers.Input(moveY: 1f) } });
        var before = sim.GetState(1);
        TestHelpers.AssertNear(Move.RunSpeed, before.VZ, 0.1f);

        // Cruise reversal keeps full speed.
        sim.Tick(new() { { 1, TestHelpers.Input(moveY: -1f) } });
        var after = sim.GetState(1);
        TestHelpers.AssertNear(-Move.RunSpeed, after.VZ, 0.1f);
    }


    [Fact]
    public void RunRelease_BrakesToStop()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var state = TestHelpers.PlayerState(50f, 50f);
        state.PY = GroundPx;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Release after reaching cruise speed.
        for (int i = 0; i < 30; i++)
            sim.Tick(new() { { 1, TestHelpers.Input(moveY: 1f) } });
        var before = sim.GetState(1);
        TestHelpers.AssertNear(Move.RunSpeed, before.VZ, 0.1f);

        sim.Tick(new() { { 1, default(InputState) } });
        var after = sim.GetState(1);
        Assert.True(after.VZ > 0f && after.VZ < before.VZ,
            $"Run release should brake (not stop dead), got VZ={after.VZ:F3}");
    }

    [Fact]
    public void RunPerpendicularRedirect_ClearsOldAxis()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var state = TestHelpers.PlayerState(50f, 50f);
        state.PY = GroundPx;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Run right to full speed, then press forward (+Z): the
        // rightward velocity must be cleared instantly — no diagonal drag.
        for (int i = 0; i < 30; i++)
            sim.Tick(new() { { 1, TestHelpers.Input(moveX: 1f) } });
        TestHelpers.AssertNear(Move.RunSpeed, sim.GetState(1).VX, 0.1f);

        sim.Tick(new() { { 1, TestHelpers.Input(moveY: 1f) } });
        var after = sim.GetState(1);
        Assert.Equal(0f, after.VX);
        TestHelpers.AssertNear(Move.RunSpeed, after.VZ, 0.1f);
    }

    [Fact]
    public void RunDiagonalStraighten_ClearsReleasedAxis()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var state = TestHelpers.PlayerState(50f, 50f);
        state.PY = GroundPx;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Reach cruise on the diagonal before releasing one axis.
        for (int i = 0; i < 30; i++)
            sim.Tick(new() { { 1, TestHelpers.Input(moveX: 0.707f, moveY: 0.707f) } });

        // Release W (keep D): the released Z axis must clear instantly.
        sim.Tick(new() { { 1, TestHelpers.Input(moveX: 1f) } });
        var after = sim.GetState(1);
        Assert.Equal(0f, after.VZ);
        TestHelpers.AssertNear(Move.RunSpeed, after.VX, 0.1f);
    }

    // ── ServerAbility attack lifecycle (hitstun after hit) ──


    // ── Hitstun ──

    [Fact]
    public void Hitstun_AppliesKnockbackThenExpires()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = GroundPx;
        state.KVX = 10f;
        state.KVY = 5f;
        state.HitstunTicks = 12;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var t1 = TestHelpers.TickDefault(sim, 1);
        // ProcessKnockback applies KV→V, decays KV per tick
        Assert.True(t1.KVX != 0 || t1.KVY != 0);
        Assert.Equal(11, (int)t1.HitstunTicks);

        // Tick through hitstun
        for (int i = 0; i < 20; i++)
            TestHelpers.TickDefault(sim, 1);

        var after = sim.GetState(1);
        Assert.Equal(0, (int)after.HitstunTicks);
        Assert.Equal(ActionState.Idle, after.State);
    }
    // ── Float-window gravity ──
    private static readonly CharacterDefinition FallRampDef = CreateFallRampDef();
    private static readonly float FallRampFloatPerTick = FallRampDef.Movement.AirFloatGravity * Simulation.TickDt;
    private static readonly float FallRampFullPerTick = FallRampDef.Movement.Gravity * Simulation.TickDt;

    private static CharacterDefinition CreateFallRampDef()
    {
        var def = TestHelpers.EngineDef;
        def = def with { Movement = def.Movement with { AirFloatGravity = 6f, FloatWindowTicks = 10 } };
        return def;
    }

    [Fact]
    public void FallRamp_AirTimeIncrementsEachTick()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = 2f;
        state.IsGrounded = false;
        state.JumpsLeft = 0;
        TestHelpers.RegisterPlayer(sim, FallRampDef, state);

        // AirTime starts at 0 (default)
        Assert.Equal(0, (int)sim.GetState(1).AirTimeTicks);

        var t1 = TestHelpers.TickDefault(sim, 1);
        Assert.Equal(1, (int)t1.AirTimeTicks);

        var t2 = TestHelpers.TickDefault(sim, 1);
        Assert.Equal(2, (int)t2.AirTimeTicks);
    }

    [Fact]
    public void FallRamp_AirTimeResetsOnLanding()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = 2f;
        state.IsGrounded = false;
        state.JumpsLeft = 0;
        TestHelpers.RegisterPlayer(sim, FallRampDef, state);

        // Fall for 30 ticks (accumulates AirTime)
        for (int i = 0; i < 30; i++)
            TestHelpers.TickDefault(sim, 1);

        var before = sim.GetState(1);
        Assert.True(before.IsGrounded, "Should have landed after 30 ticks");
        Assert.Equal(0, (int)before.AirTimeTicks);
    }
    
    [Fact]
    public void FallRamp_FloatWindowUsesReducedGravity()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = 2f;
        state.IsGrounded = false;
        state.JumpsLeft = 0;
        state.VY = 0f;
        TestHelpers.RegisterPlayer(sim, FallRampDef, state);
        
        // Tick 1: still in FloatWindow (FloatWindowTicks=10)
        var t1 = TestHelpers.TickDefault(sim, 1);
        float expectedVY = 0f - FallRampFloatPerTick;
        TestHelpers.AssertNear(expectedVY, t1.VY, 0.001f);
        
        // Tick 2: still in FloatWindow
        var t2 = TestHelpers.TickDefault(sim, 1);
        expectedVY -= FallRampFloatPerTick;
        TestHelpers.AssertNear(expectedVY, t2.VY, 0.001f);
    }
    
    [Fact]
    public void AfterFloatWindow_UsesFullGravity()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = 8f;
        state.IsGrounded = false;
        state.JumpsLeft = 0;
        state.VY = 0f;
        TestHelpers.RegisterPlayer(sim, FallRampDef, state);

        // Tick through the FloatWindow (10 ticks) — full gravity past it.
        for (int i = 0; i < 10; i++)
            TestHelpers.TickDefault(sim, 1);

        float vyBefore = sim.GetState(1).VY;
        TestHelpers.TickDefault(sim, 1);
        float vyDelta = sim.GetState(1).VY - vyBefore;

        TestHelpers.AssertNear(-FallRampFullPerTick, vyDelta, 0.01f);
    }
    
    [Fact]
    public void FallRamp_NoRampWhenGrounded()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = 0f + FallRampDef.CapsuleHeight * 0.5f; // grounded
        state.IsGrounded = true;
        state.JumpsLeft = 2;
        TestHelpers.RegisterPlayer(sim, FallRampDef, state);
        
        // Grounded: AirTime should be 0, gravity should not apply (grounded has its own path)
        TestHelpers.TickDefault(sim, 5);
        var s = sim.GetState(1);
        Assert.True(s.IsGrounded);
        Assert.Equal(0, (int)s.AirTimeTicks);
    }
    
    [Fact]
    public void FallRamp_AirTimeResetsOnDoubleJump()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = 2f;
        state.IsGrounded = false;
        state.JumpsLeft = 2;
        state.VY = 0f;
        TestHelpers.RegisterPlayer(sim, FallRampDef, state);
        
        // Fall for 15 ticks to accumulate AirTime
        for (int i = 0; i < 15; i++)
            TestHelpers.TickDefault(sim, 1);
        Assert.True(sim.GetState(1).AirTimeTicks > 0, "Should have accumulated AirTime");
        
        // Double jump — AirTime set to FloatWindowTicks, then gravity increments by 1.
        // FallRampDef: FloatWindowTicks=10 → AirTime = 10 + 1 = 11.
        var afterJump = TestHelpers.TickN(sim, TestHelpers.Input(jump: true), 1);
        Assert.Equal(FallRampDef.Movement.FloatWindowTicks + 1, (int)afterJump.AirTimeTicks);
    }

        // ── Jump arc timing ──

    [Fact]
    public void JumpArcTiming_UsesSyntheticMovement()
    {
        var mov = Def.Movement;
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var state = TestHelpers.PlayerState() with { PY = GroundPx };
        TestHelpers.RegisterPlayer(sim, Def, state);

        var squat = TestHelpers.TickN(sim, TestHelpers.Input(jump: true, jumpHeld: true), 1);
        Assert.Equal(ActionState.JumpSquat, squat.State);
        Assert.Equal(mov.JumpSquatTicks, (int)squat.StateTicks);
        TestHelpers.TickHold(
            sim,
            TestHelpers.Input(jumpHeld: true),
            Math.Max(mov.JumpSquatTicks, Simulation.ShortHopWindowTicks));
        Assert.False(sim.GetState(1).IsGrounded);
        Assert.True(sim.GetState(1).VY > 0f);
    }
}
