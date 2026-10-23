using Xunit;

namespace SlopArena.Shared.Tests;

public class CombatMathTests
{
    // ── IsInCircle ──

    [Fact]
    public void IsInCircle_Center_ReturnsTrue()
    {
        Assert.True(CombatMath.IsInCircle(5, 5, 5, 5, 3));
    }

    [Fact]
    public void IsInCircle_WithinRadius_ReturnsTrue()
    {
        Assert.True(CombatMath.IsInCircle(6, 5, 5, 5, 3));
    }

    [Fact]
    public void IsInCircle_OnEdge_ReturnsTrue()
    {
        Assert.True(CombatMath.IsInCircle(8, 5, 5, 5, 3));
    }

    [Fact]
    public void IsInCircle_OutsideRadius_ReturnsFalse()
    {
        Assert.False(CombatMath.IsInCircle(9, 5, 5, 5, 3));
    }

    // ── IsInCone ──

    [Fact]
    public void IsInCone_DirectlyInFront_ReturnsTrue()
    {
        // origin (0,0), facing +Z, 45deg half-angle, 10m range
        Assert.True(CombatMath.IsInCone(0, 5, 0, 0, 0, 1, MathF.PI / 4, 10));
    }

    [Fact]
    public void IsInCone_WithinHalfAngle_ReturnsTrue()
    {
        // 30deg off center, half-angle 45deg
        float angle30 = MathF.Tan(MathF.PI / 6);
        Assert.True(CombatMath.IsInCone(angle30 * 5, 5, 0, 0, 0, 1, MathF.PI / 4, 10));
    }

    [Fact]
    public void IsInCone_OutsideHalfAngle_ReturnsFalse()
    {
        // 60deg off center, half-angle 45deg
        float angle60 = MathF.Tan(MathF.PI / 3);
        Assert.False(CombatMath.IsInCone(angle60 * 5, 5, 0, 0, 0, 1, MathF.PI / 4, 10));
    }

    [Fact]
    public void IsInCone_AtOrigin_ReturnsTrue()
    {
        Assert.True(CombatMath.IsInCone(0, 0, 0, 0, 0, 1, MathF.PI / 4, 10));
    }

    [Fact]
    public void IsInCone_BeyondRange_ReturnsFalse()
    {
        Assert.False(CombatMath.IsInCone(0, 15, 0, 0, 0, 1, MathF.PI / 4, 10));
    }

    // ── LineIntersectsCircle ──

    [Fact]
    public void LineIntersectsCircle_ThroughCenter_ReturnsTrue()
    {
        Assert.True(CombatMath.LineIntersectsCircle(
            -5, 0, 5, 0, 0, 0, 3));
    }

    [Fact]
    public void LineIntersectsCircle_Tangent_ReturnsTrue()
    {
        // Line from (-5, 3) to (5, 3) is tangent to circle at (0,0) radius 3
        Assert.True(CombatMath.LineIntersectsCircle(
            -5, 3, 5, 3, 0, 0, 3));
    }

    [Fact]
    public void LineIntersectsCircle_Miss_ReturnsFalse()
    {
        Assert.False(CombatMath.LineIntersectsCircle(
            -5, 10, 5, 10, 0, 0, 3));
    }

    [Fact]
    public void LineIntersectsCircle_EntirelyInside_ReturnsTrue()
    {
        // Segment starts and ends inside the circle
        Assert.True(CombatMath.LineIntersectsCircle(
            -1, 0, 1, 0, 0, 0, 3));
    }

    // ── CalculateKnockback ──

    [Fact]
    public void CalculateKnockback_FromAttackerToTarget_Directional()
    {
        CombatMath.CalculateKnockback(
            targetX: 10, targetZ: 0,
            attackerX: 0, attackerZ: 0,
            force: 100, upward: 50,
            out float kbX, out float kbY, out float kbZ);

        Assert.Equal(100, kbX, 4);     // full force in +X
        Assert.Equal(0, kbZ, 4);        // no Z component
        Assert.Equal(50, kbY, 4);       // upward preserved
    }

    [Fact]
    public void CalculateKnockback_SamePosition_DefaultsForward()
    {
        CombatMath.CalculateKnockback(
            targetX: 0, targetZ: 0,
            attackerX: 0, attackerZ: 0,
            force: 100, upward: 30,
            out float kbX, out float kbY, out float kbZ);

        Assert.Equal(0, kbX, 4);
        Assert.Equal(100, kbZ, 4);      // defaults to +Z (forward)
        Assert.Equal(30, kbY, 4);
    }

    [Fact]
    public void CalculateKnockback_Diagonal_Normalized()
    {
        CombatMath.CalculateKnockback(
            targetX: 3, targetZ: 4,
            attackerX: 0, attackerZ: 0,
            force: 50, upward: 10,
            out float kbX, out float kbY, out float kbZ);

        float expectedX = 50 * (3f / 5f); // 30
        float expectedZ = 50 * (4f / 5f); // 40
        Assert.Equal(expectedX, kbX, 4);
        Assert.Equal(expectedZ, kbZ, 4);
        Assert.Equal(10, kbY, 4);
    }

    // ── HorizontalDistance ──

    [Fact]
    public void HorizontalDistance_SamePoint_ReturnsZero()
    {
        Assert.Equal(0, CombatMath.HorizontalDistance(5, 3, 5, 3), 4);
    }

    [Fact]
    public void HorizontalDistance_PositiveDistance()
    {
        float d = CombatMath.HorizontalDistance(0, 0, 3, 4);
        Assert.Equal(5, d, 4);
    }

    // ── ComputeProjectileLaunch ──

    [Theory]
    [InlineData(10f, 30f, 20f, 0f)]
    [InlineData(10f, 30f, 20f, 3f)]
    [InlineData(10f, 30f, 20f, -3f)]
    [InlineData(15f, 45f, 35f, -5f)]
    public void ComputeProjectileLaunch_ReachesRequestedTarget(
        float targetDistance, float launchAngleDegrees, float gravity, float heightOffset)
    {
        float launchAngleRad = launchAngleDegrees * MathF.PI / 180f;
        CombatMath.ComputeProjectileLaunch(
            targetDistance, launchAngleRad, gravity, heightOffset,
            out float speed, out float hSpeed, out float vSpeed);

        Assert.True(speed > 0);
        Assert.True(hSpeed > 0);
        Assert.Equal(speed * MathF.Sin(launchAngleRad), vSpeed, 4);
        Assert.Equal(speed * MathF.Cos(launchAngleRad), hSpeed, 4);

        float flightTime = targetDistance / hSpeed;
        float endpointHeight = vSpeed * flightTime - 0.5f * gravity * flightTime * flightTime;
        Assert.Equal(heightOffset, endpointHeight, 4);
    }
}
