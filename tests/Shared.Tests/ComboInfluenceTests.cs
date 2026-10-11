using System;
using Xunit;

namespace SlopArena.Shared.Tests;


/// <summary>ADR-0019 hit-response contract tests.</summary>
public class ComboInfluenceTests
{
    [Fact]
    public void Knockback_UsesHitDamageAndAccumulatedDamage()
    {
        var state = TestHelpers.PlayerState();
        state.DamagePercent = 50;
        Simulation.ApplyKnockback(ref state, 1f, 0f, 0, 10f, 2f, 12f, 20, 100f);

        // Raw formula: (10 + 2 * (0.5 + 1) + 12 * 0.1) * 200 / (100 + 100) = 14.2.
        // KnockbackScale (0.17) scales launch velocity only; hitstun stays 0.45 * 14.2 = 6 (melee-soft).
        Assert.Equal(14.2f * CombatTuning.Default.KnockbackScale, state.KVX, 3);
        Assert.Equal((ushort)6, state.HitstunTicks);
    }

    [Fact]
    public void Knockback_HitstunIsDerivedFromAppliedMagnitude()
    {
        var state = TestHelpers.PlayerState();
        Simulation.ApplyKnockback(ref state, 1f, 0f, 0, 20f, 0f, 0f, 60, 100f);

        Assert.Equal((ushort)9, state.HitstunTicks); // 0.45 * 20 (melee-soft)
        Assert.Equal(ActionState.Hitstun, state.State);
    }

    [Fact]
    public void Knockback_ZeroStunTicksDoesNotLock()
    {
        var state = TestHelpers.PlayerState();
        Simulation.ApplyKnockback(ref state, 1f, 0f, 0, 20f, 0f, 0f, 0, 100f);

        Assert.Equal((ushort)0, state.HitstunTicks);
        Assert.Equal(ActionState.Idle, state.State);
    }

    [Fact]
    public void Knockback_ZeroMagnitudeWithStunMetadata_StaysIdle()
    {
        var state = TestHelpers.PlayerState();
        Simulation.ApplyKnockback(ref state, 1f, 0f, 20, 0f, 0f, 0f, 20, 100f);

        Assert.Equal(0f, state.KVX);
        Assert.Equal(0f, state.KVY);
        Assert.Equal(0f, state.KVZ);
        Assert.Equal((ushort)0, state.HitstunTicks);
        Assert.Equal(ActionState.Idle, state.State);
    }

    [Fact]
    public void QueuedResolvedForce_ZeroStunStillAppliesLaunch()
    {
        var state = TestHelpers.PlayerState();
        state.HitstopTicks = 1;
        state.QueuedKBResolvedForce = true;
        state.QueuedKBForce = 12f;
        state.QueuedKBDirX = 1f;
        state.QueuedKBAngle = 30;
        state.QueuedKBStun = 0;

        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        sim.RegisterEntity(1, TestHelpers.EngineDef, state);
        sim.Tick(new() { { 1, default } });

        var launched = sim.GetState(1);
        Assert.True(launched.KVX > 10f, $"resolved queued force was lost: KVX={launched.KVX}");
        Assert.True(launched.KVY > 5f, $"resolved queued angle was lost: KVY={launched.KVY}");
        Assert.Equal((ushort)0, launched.HitstunTicks);
        Assert.Equal(ActionState.Idle, launched.State);
        Assert.False(launched.QueuedKBResolvedForce);
    }
    [Fact]
    public void QueuedLaunch_MatchesDirectLaunch()
    {
        var direct = TestHelpers.PlayerState();
        direct.DamagePercent = 50;
        Simulation.ApplyKnockback(ref direct, 1f, 0f, 20, 10f, 2f, 12f, 20, 100f);

        var queued = TestHelpers.PlayerState();
        queued.DamagePercent = 50;
        queued.HitstopTicks = 1;
        queued.QueuedKBDirX = 1f;
        queued.QueuedKBAngle = 20;
        queued.QueuedKBBase = 10f;
        queued.QueuedKBGrowth = 2f;
        queued.QueuedKBDamage = 12f;
        queued.QueuedKBStun = 20;
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        sim.RegisterEntity(1, TestHelpers.EngineDef, queued);
        sim.Tick(new() { { 1, default } });
        queued = sim.GetState(1);

        Assert.Equal(direct.KVX, queued.KVX, 3);
        Assert.Equal(direct.KVY, queued.KVY, 3);
        Assert.Equal(direct.HitstunTicks, queued.HitstunTicks);
    }

    [Fact]
    public void DirectionalInfluence_VerticalLaunchTiltsWithoutChangingMagnitude()
    {
        var state = TestHelpers.PlayerState();
        state.KVY = 10f;
        state.DIX = 1f;
        state.DIY = 0f;

        Simulation.ApplyDirectionalInfluence(ref state);

        float magnitude = MathF.Sqrt(state.KVX * state.KVX + state.KVY * state.KVY + state.KVZ * state.KVZ);
        TestHelpers.AssertNear(10f, magnitude, 0.001f);
        Assert.True(state.KVX > 0f);
        Assert.True(state.KVY < 10f);
    }


    [Fact]
    public void HitstopSdi_UsesFirstInput_WhileDiUsesLatestInput()
    {
        var state = TestHelpers.PlayerState(x: 10f, z: 10f);
        state.HitstopTicks = 2;
        state.KVX = 10f;
        state.KVY = 5f;
        state.QueuedKBResolvedForce = true;
        state.QueuedKBForce = 10f;
        state.QueuedKBDirX = 1f;
        state.QueuedKBAngle = 30;

        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        sim.RegisterEntity(1, TestHelpers.EngineDef, state);
        sim.Tick(new() { { 1, new InputState { MoveX = 1f } } });
        var first = sim.GetState(1);
        sim.Tick(new() { { 1, new InputState { MoveY = 1f } } });
        var second = sim.GetState(1);

        TestHelpers.AssertNear(10.4f, second.PX, 0.001f);
        TestHelpers.AssertNear(10f, second.PZ, 0.001f);
        Assert.True(second.KVZ > 0f);
        Assert.True(second.KVX < first.KVX);
        Assert.False(second.SdiApplied);
    }
    [Fact]
    public void PostHitstunFlight_AppliesFrictionGravity_AndTerminates()
    {
        var state = TestHelpers.PlayerState(x: 50f, z: 50f);
        state.PY = 10f;
        state.IsGrounded = false;
        state.KVX = 10f;
        state.KVY = 5f;
        state.State = ActionState.Idle;
        state.AirTimeTicks = Simulation.KnockbackPopTicks;

        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        sim.RegisterEntity(1, TestHelpers.EngineDef, state);

        sim.Tick(new() { { 1, default } });
        var first = sim.GetState(1);
        TestHelpers.AssertNear(10f - Simulation.FlightFriction * Simulation.TickDt, first.KVX, 0.001f);
        TestHelpers.AssertNear(5f - Simulation.FlightGravity * Simulation.TickDt, first.KVY, 0.001f);

        sim.Tick(new() { { 1, default } });
        var second = sim.GetState(1);
        TestHelpers.AssertNear(first.KVX - Simulation.FlightFriction * Simulation.TickDt, second.KVX, 0.001f);
        TestHelpers.AssertNear(first.KVY - Simulation.FlightGravity * Simulation.TickDt, second.KVY, 0.001f);

        for (int i = 0; i < 240 && (second.KVX != 0f || second.KVY != 0f || second.KVZ != 0f); i++)
        {
            sim.Tick(new() { { 1, default } });
            second = sim.GetState(1);
        }

        Assert.Equal(0f, second.KVX);
        Assert.Equal(0f, second.KVY);
        Assert.Equal(0f, second.KVZ);
    }
    [Fact]
    public void DirectionalInfluence_PerpendicularHold_ReachesTurnCapWithoutChangingMagnitude()
    {
        var state = TestHelpers.PlayerState();
        state.KVZ = 10f;
        state.DIX = 1f;

        Simulation.ApplyDirectionalInfluence(ref state);

        float magnitude = MathF.Sqrt(state.KVX * state.KVX + state.KVY * state.KVY + state.KVZ * state.KVZ);
        TestHelpers.AssertNear(10f, magnitude, 0.001f);
        TestHelpers.AssertNear(10f * MathF.Sin(18f * MathF.PI / 180f), state.KVX, 0.001f);
        TestHelpers.AssertNear(10f * MathF.Cos(18f * MathF.PI / 180f), state.KVZ, 0.001f);
    }

    [Fact]
    public void DirectionalInfluence_AxisHoldDoesNotRotate()
    {
        var withLaunch = TestHelpers.PlayerState();
        withLaunch.KVZ = 10f;
        withLaunch.DIY = 1f;
        Simulation.ApplyDirectionalInfluence(ref withLaunch);

        var againstLaunch = TestHelpers.PlayerState();
        againstLaunch.KVZ = 10f;
        againstLaunch.DIY = -1f;
        Simulation.ApplyDirectionalInfluence(ref againstLaunch);

        TestHelpers.AssertNear(0f, withLaunch.KVX, 0.001f);
        TestHelpers.AssertNear(10f, withLaunch.KVZ, 0.001f);
        TestHelpers.AssertNear(0f, againstLaunch.KVX, 0.001f);
        TestHelpers.AssertNear(10f, againstLaunch.KVZ, 0.001f);
    }

    [Fact]
    public void DirectionalInfluence_DiagonalHoldUsesSinSquaredCurve()
    {
        var state = TestHelpers.PlayerState();
        state.KVZ = 10f;
        state.DIX = 1f;
        state.DIY = 1f;

        Simulation.ApplyDirectionalInfluence(ref state);

        float angle = 9f * MathF.PI / 180f;
        TestHelpers.AssertNear(10f * MathF.Sin(angle), state.KVX, 0.001f);
        TestHelpers.AssertNear(10f * MathF.Cos(angle), state.KVZ, 0.001f);
    }

    [Fact]
    public void DirectionalInfluence_NeverExceedsTurnCap()
    {
        for (int i = 0; i <= 36; i++)
        {
            float angle = i * 5f * MathF.PI / 180f;
            float launchX = 10f * MathF.Sin(angle);
            float launchZ = 10f * MathF.Cos(angle);
            var state = TestHelpers.PlayerState();
            state.KVX = launchX;
            state.KVZ = launchZ;
            state.DIX = 1f;

            Simulation.ApplyDirectionalInfluence(ref state);

            float dot = launchX * state.KVX + launchZ * state.KVZ;
            float actual = MathF.Acos(Math.Clamp(dot / 100f, -1f, 1f)) * 180f / MathF.PI;
            Assert.True(actual <= 18.01f, $"turn {actual:F2}° exceeds the 18° cap");
        }
    }

}

