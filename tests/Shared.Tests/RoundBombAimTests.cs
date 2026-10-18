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

    [Fact]
    public void CursorDistanceLandsTheBombOnARaisedIsland()
    {
        // Flat floor at 0 with a 4 x 4 m island 2.4 m high centred 7.5 m ahead; a crate on it.
        // (Height costs reach: at 9 m this island would already need a 12.5 m throw.)
        const int size = 200; const float centre = 100f, island = 2.4f;
        var heights = new float[size * size];
        for (int z = 105; z <= 110; z++) for (int x = 98; x <= 102; x++) heights[z * size + x] = island;
        var crate = new CourseTargetDefinition("crate", new(centre, island + 1.1f, centre + 7.5f), .65f);
        var arena = new ArenaDefinition
        {
            Name = "bomb-aim", DisplayName = "Bomb Aim", KillHeight = -40f,
            SpawnPoints = new[] { new SpawnPoint { X = centre, Y = 0f, Z = centre } },
            Heightmap = new ArenaHeightmap { Data = heights, Width = size, Height = size, CellSize = 1f },
            Arcade = ArcadeStageDefinition.ForCourse("bomb-aim", 1, new[] { "manki" },
                new CharacterCourseDefinition(new[] { crate }, Array.Empty<CourseCheckpointDefinition>(),
                    new ArcadeVolume(new(centre, 1f, centre + 40f), new(2f, 2f, 2f)))),
        };
        var manki = TestHelpers.MankiDef;
        float body = manki.CapsuleHeight * .5f;
        var sim = new ServerSimulation(arena, NoWinMatchRule.Instance);
        sim.RegisterEntity(1, manki, new CharacterState
        {
            EntityId = 1, PX = centre, PY = body, PZ = centre, IsGrounded = true,
            JumpsLeft = manki.Movement.MaxJumps, AirDodgesLeft = 1,
        });
        sim.BeginCourseAttempt(1); sim.Tick(new()); sim.Tick(new() { [1] = default });

        // What the client sends with its cursor on the island top, 7.5 m ahead.
        float launchY = body + MankiRoundBomb.LaunchOffsetY;
        float sent = CombatMath.LobDistanceThrough(7.5f, island - launchY, Launch, -body - MankiRoundBomb.LaunchOffsetY);
        Assert.InRange(sent, 8f, 12f);   // longer than the cursor distance, inside the 12 m reach
        for (int tick = 0; tick < 150; tick++)
            sim.Tick(new() { [1] = new() { ActiveSlot = tick == 0 ? AbilitySlots.A : (byte)0, IsAiming = tick < 12, AimDistance = (ushort)MathF.Round(sent * 100f) } });

        Assert.True(sim.IsCourseTargetBroken(0));
    }
}
