using SlopArena.Shared.Abilities;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// The Round Bomb always throws to foot height at the sent distance. The client's ground cursor
/// sends <see cref="CombatMath.LobDistanceThrough"/> for the surface it points at, so the same
/// server arc lands on a raised island or a lower roof (2026-10-18 cursor fix).
/// </summary>
public sealed class RoundBombAimTests
{
    private const float Launch = 30f * MathF.PI / 180f;

    [Theory]
    [InlineData(8f, -1.95f)]   // same floor: the sent distance is the cursor distance
    [InlineData(8f, 0.6f)]     // a raised island
    [InlineData(10f, -6f)]     // a roof well below
    public void SentDistanceArcPassesThroughThePoint(float horizontal, float height)
    {
        const float landing = -1.95f, gravity = 30f;
        float sent = CombatMath.LobDistanceThrough(horizontal, height, Launch, landing);
        CombatMath.ComputeProjectileLaunch(sent, Launch, gravity, landing, out _, out float hSpeed, out float vSpeed);
        float t = horizontal / hSpeed;
        Assert.Equal(height, vSpeed * t - .5f * gravity * t * t, 3);
        if (MathF.Abs(height - landing) < .001f) Assert.Equal(horizontal, sent, 3);
    }

    [Fact]
    public void PointAboveTheLaunchLineIsOutOfReach()
        => Assert.True(float.IsPositiveInfinity(CombatMath.LobDistanceThrough(4f, 4f, Launch, -1.95f)));

}
