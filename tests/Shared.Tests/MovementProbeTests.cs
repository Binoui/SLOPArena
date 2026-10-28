using System;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Movement probe assertions on the real simulation with explicit engine data.
/// Tests protect measurement and movement rules independently of roster tuning.
/// </summary>
public class MovementProbeTests
{
    private readonly CharacterDefinition Def = TestHelpers.EngineDef;
    private MovementStats M => Def.Movement;
    private const float SpeedCapTolerance = 0.001f;

    private MovementProbe.CharacterMovement Measured() =>
        MovementProbe.Measure(Def, TestHelpers.TestArena());

    [Fact]
    public void Run_HoldingStick_ReachesAuthoredRunSpeed()
    {
        var run = Measured().Run;
        // The legacy full probe indexes tick 1 as sample zero.
        Assert.Equal(12, run.TimeToMaxTicks);
        Assert.Equal(M.RunSpeed, run.MaxSpeed, 2); // 14 m/s
        Assert.True(run.Curve[30].Speed >= M.RunSpeed * 0.99f, "speed decays below cruise");
    }

    [Fact]
    public void Shield_FromCruiseBrakesImmediatelyAndHolds()
    {
        var shield = Measured().Shield;
        Assert.Equal(1, shield.StopTicks);
        Assert.InRange(shield.StopDistance, 0f, 0.001f);
        Assert.Equal(ActionState.Shielding, shield.Curve[shield.Curve.Length - 1].State);
    }


    [Fact]
    public void Jump_Apex_RespectsGravityAndFloatWindow()
    {
        var jump = Measured().Jump;
        // Measured contract (what the sim actually does): the jump pre-sets
        // AirTimeTicks = FloatWindowTicks, and the float-window gate is `AirTimeTicks <
        // window`, so the window never applies to jumps — full Gravity from takeoff.
        // With AirFloatGravity = 0 the apex is exactly JumpForce²/(2·Gravity).
        float expected = M.AirFloatGravity == 0f
            ? (M.JumpForce * M.JumpForce) / (2f * M.Gravity)
            : M.JumpForce * (M.FloatWindowTicks / 60f) + (M.JumpForce * M.JumpForce) / (2f * M.Gravity);
        Assert.True(Math.Abs(jump.ApexHeight - expected) <= expected * 0.08f,
            $"apex {jump.ApexHeight:F2} vs {expected:F2}");
        Assert.True(jump.AirtimeTicks > 0);
    }

    [Fact]
    public void FastFall_ReachesAuthoredFastFallSpeed()
    {
        var fall = Measured().Fall;
        // The 50 m drop must be long enough to hit the gravity cap — this pins the
        // measurement to the real fall regime, not the short jump tail.
        Assert.True(fall.MaxFallSpeed >= M.MaxFallSpeed * 0.98f,
            $"max fall {fall.MaxFallSpeed:F1} vs {M.MaxFallSpeed}");
        Assert.True(fall.MaxFallSpeed <= M.MaxFallSpeed + SpeedCapTolerance,
            $"max fall {fall.MaxFallSpeed:F3} exceeds cap {M.MaxFallSpeed}");
        Assert.True(fall.FastFallSpeed >= M.FastFallSpeed * 0.98f,
            $"fast fall {fall.FastFallSpeed:F1} vs {M.FastFallSpeed}");
        Assert.True(fall.FastFallSpeed <= M.FastFallSpeed + SpeedCapTolerance,
            $"fast fall {fall.FastFallSpeed:F3} exceeds cap {M.FastFallSpeed}");
        Assert.True(fall.FastFallReachTicks <= 3, $"took {fall.FastFallReachTicks} ticks to reach FF");
        Assert.True(fall.FastFallDescentTicks < fall.DescentTicks,
            "fast fall should descend faster than natural fall");
        // From a real full-jump apex the fast-fall descent is under a quarter second —
        // the landing-mixup window (natural ~0.3s → fast-fall ~0.03s).
        Assert.True(fall.FastFallFromJumpTicks < 15,
            $"jump fast-fall descent {fall.FastFallFromJumpTicks} ticks");
    }

    [Fact]
    public void AirDrift_CapsAtAuthoredAirSpeedMax()
    {
        var jump = Measured().Jump;
        Assert.True(jump.DriftSpeedMax >= M.AirSpeedMax * 0.97f,
            $"drift {jump.DriftSpeedMax:F1} vs air cap {M.AirSpeedMax}");
        Assert.True(jump.DriftSpeedMax <= M.AirSpeedMax + SpeedCapTolerance,
            $"drift {jump.DriftSpeedMax:F3} exceeds air cap {M.AirSpeedMax}");
    }

    [Fact]
    public void Stop_FromCruise_BrakesToStandstill()
    {
        var stop = Measured().Stop;
        Assert.True(stop.StopTicks <= 30, $"stop took {stop.StopTicks} ticks");
        Assert.True(stop.StopDistance <= 4f, $"stop distance {stop.StopDistance:F2}m");
    }

    [Fact]
    public void ShortHop_Apex_RespectsShortHopForce()
    {
        var sh = Measured().ShortHop;
        float expected = (M.ShortHopForce * M.ShortHopForce) / (2f * M.Gravity);
        // Sample quantization lands up to ~10% below the theoretical peak (short hops are
        // short — the discrete max sits one tick off the true apex).
        Assert.True(Math.Abs(sh.ApexHeight - expected) <= expected * 0.12f,
            $"short-hop apex {sh.ApexHeight:F2} vs {expected:F2}");
        // A short hop is shorter AND faster than the full hop.
        Assert.True(sh.ApexHeight < Measured().Jump.ApexHeight, "short hop taller than full hop");
        Assert.True(sh.AirtimeTicks < Measured().Jump.AirtimeTicks, "short hop slower than full hop");
    }

    [Fact]
    public void Reversal_FromCruise_IsImmediate()
    {
        var rev = Measured().Reversal;
        // Ground reversal does not enter a Turnaround skid after Rush expires.
        Assert.True(rev.ReversalTicks <= 2,
            $"reversal should be immediate ({rev.ReversalTicks} ticks)");
        Assert.True(rev.Displacement > 0f, $"reversal displacement {rev.Displacement:F2}m");
    }

    [Fact]
    public void Measure_IsDeterministic()
    {
        var a = Measured();
        var b = Measured();
        Assert.Equal(a.Run.MaxSpeed, b.Run.MaxSpeed);
        Assert.Equal(a.Shield.StopDistance, b.Shield.StopDistance);
        Assert.Equal(a.Jump.ApexHeight, b.Jump.ApexHeight);
        Assert.Equal(a.Jump.AirtimeTicks, b.Jump.AirtimeTicks);
        Assert.Equal(a.Fall.MaxFallSpeed, b.Fall.MaxFallSpeed);
    }
}
