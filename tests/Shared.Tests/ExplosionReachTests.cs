using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// A projectile stops at the first thing it touches, but its explosion hits everything inside
/// the blast radius once: every fighter (the owner too when the blast allows it), balls and
/// course targets. Before 2026-10-18 the blast hitbox died on its first contact, so a bomb
/// landing between two opponents or two targets only ever hit one of them.
/// </summary>
public sealed class ExplosionReachTests
{
    private const float Centre = 100f;

    private static ArenaDefinition Flat(CharacterCourseDefinition? course = null)
    {
        const int size = 200;
        var arena = new ArenaDefinition
        {
            Name = "explosion-reach", DisplayName = "Explosion Reach", KillHeight = -40f,
            SpawnPoints = new[] { new SpawnPoint { X = Centre, Y = 0f, Z = Centre, Yaw = 0f } },
            Heightmap = new ArenaHeightmap { Data = new float[size * size], Width = size, Height = size, CellSize = 1f },
        };
        if (course != null) arena.Arcade = ArcadeStageDefinition.ForCourse("explosion-reach", 1, new[] { "manki" }, course);
        return arena;
    }

    private static CharacterState Grounded(ulong id, CharacterDefinition def, float x, float z) => new()
    {
        EntityId = id, PX = x, PY = def.CapsuleHeight * .5f, PZ = z, IsGrounded = true,
        JumpsLeft = def.Movement.MaxJumps, AirDodgesLeft = 1,
    };

    /// <summary>Hold A aimed straight ahead (+Z) at <paramref name="metres"/>, release, then let the bomb land.</summary>
    private static void ThrowBomb(ServerSimulation sim, float metres, params ulong[] bystanders)
    {
        for (int tick = 0; tick < 150; tick++)
        {
            var inputs = new Dictionary<ulong, InputState>
            {
                [1] = new() { ActiveSlot = tick == 0 ? AbilitySlots.A : (byte)0, IsAiming = tick < 12, AimDistance = (ushort)(metres * 100f) },
            };
            foreach (ulong id in bystanders) inputs[id] = default;
            sim.Tick(inputs);
        }
    }

    [Fact]
    public void RoundBombBreaksEveryCourseTargetInsideItsBlast()
    {
        // Three crates around the landing point, none on the bomb's path: only the blast reaches them.
        var targets = new[]
        {
            new CourseTargetDefinition("left", new(Centre - 1.6f, 1.3f, Centre + 10f), .65f),
            new CourseTargetDefinition("right", new(Centre + 1.6f, 1.3f, Centre + 10f), .65f),
            new CourseTargetDefinition("behind", new(Centre, 1.3f, Centre + 11.8f), .65f),
            new CourseTargetDefinition("far", new(Centre + 6f, 1.3f, Centre + 10f), .65f),
        };
        var course = new CharacterCourseDefinition(targets, Array.Empty<CourseCheckpointDefinition>(),
            new ArcadeVolume(new(Centre, 1f, Centre + 40f), new(2f, 2f, 2f)));
        var manki = TestHelpers.MankiDef;
        var sim = new ServerSimulation(Flat(course), NoWinMatchRule.Instance);
        sim.RegisterEntity(1, manki, Grounded(1, manki, Centre, Centre));
        sim.BeginCourseAttempt(1); sim.Tick(new()); sim.Tick(new() { [1] = default });

        ThrowBomb(sim, 10f);

        Assert.True(sim.IsCourseTargetBroken(0), "left crate");
        Assert.True(sim.IsCourseTargetBroken(1), "right crate");
        Assert.True(sim.IsCourseTargetBroken(2), "crate behind the landing point");
        Assert.False(sim.IsCourseTargetBroken(3), "a crate 6 m away is outside the 3 m blast");
    }

    [Fact]
    public void RoundBombBlastHitsBothOpponentsBesideTheLandingPoint()
    {
        var manki = TestHelpers.MankiDef; var target = TestHelpers.EngineDef;
        var sim = new ServerSimulation(Flat(), NoWinMatchRule.Instance);
        sim.RegisterEntity(1, manki, Grounded(1, manki, Centre, Centre));
        sim.RegisterEntity(100, target, Grounded(100, target, Centre - 1.5f, Centre + 10f));
        sim.RegisterEntity(101, target, Grounded(101, target, Centre + 1.5f, Centre + 10f));

        ThrowBomb(sim, 10f, 100, 101);

        Assert.True(sim.GetState(100).DamagePercent > 0, "west opponent");
        Assert.True(sim.GetState(101).DamagePercent > 0, "east opponent");
        Assert.Equal(sim.GetState(100).DamagePercent, sim.GetState(101).DamagePercent);
    }

    [Fact]
    public void BazookaRocketJumpAlsoHitsTheOpponentStandingBeside()
    {
        var manki = TestHelpers.MankiDef; var target = TestHelpers.EngineDef;
        var sim = new ServerSimulation(Flat(), NoWinMatchRule.Instance);
        sim.RegisterEntity(1, manki, Grounded(1, manki, Centre, Centre));
        sim.RegisterEntity(100, target, Grounded(100, target, Centre + 1.4f, Centre));

        for (int tick = 0; tick < 90; tick++)
            sim.Tick(new()
            {
                [1] = new() { ActiveSlot = tick == 0 ? AbilitySlots.R : (byte)0, IsAiming = tick < 2, AimPitch = -9000 },
                [100] = default,
            });

        Assert.Equal(4, sim.GetState(1).DamagePercent);   // the Bazooka's fixed self-damage
        Assert.True(sim.GetState(100).DamagePercent > 0, "the same blast also hits the opponent");
    }
}
