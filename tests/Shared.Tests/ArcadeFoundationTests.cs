using System.Buffers.Binary;
using SlopArena.Shared.Abilities;
using Xunit;

namespace SlopArena.Shared.Tests;

public class ArcadeFoundationTests
{
    [Fact]
    public void ArcadeStageRoundTripContainsOneStartAndImmutableBallProfile()
    {
        var arena = Court(new(1f, 2f, 3f), new(4f, 5f, 6f));
        byte[] bytes = ArenaBinaryFormat.Serialize(arena);
        Assert.Equal(6u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)));
        var loaded = Assert.IsType<ArenaDefinition>(ArenaBinaryFormat.Deserialize(bytes));
        Assert.Equal(ArenaPurpose.Arcade, loaded.Purpose);
        Assert.Single(loaded.SpawnPoints);
        Assert.Equal("ball_fixture", loaded.Arcade!.ProfileId);
        Assert.Equal(1u, loaded.Arcade.GameplayRevision);
        Assert.True(loaded.Arcade.AllowsPackage("fightguy"));
        Assert.False(loaded.Arcade.AllowsPackage("unknown"));
        Assert.Equal(3f, loaded.Arcade.Ball.Spawn.Z);
        Assert.Equal(5f, loaded.Arcade.Ball.InitialVelocity.Y);
        Assert.Equal(arena.CollisionTriangles.Length, loaded.CollisionTriangles.Length);
        Assert.Equal(bytes, ArenaBinaryFormat.Serialize(loaded));
    }

    [Fact]
    public void OrdinaryStageExportsStayByteIdenticalToExistingV5Artifacts()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !Directory.Exists(Path.Combine(root.FullName, "data", "arenas"))) root = root.Parent;
        Assert.NotNull(root);
        int checkedStages = 0;
        foreach (string path in Directory.GetFiles(Path.Combine(root!.FullName, "data", "arenas"), "*.arena"))
        {
            byte[] original = File.ReadAllBytes(path);
            if (BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(4)) != 5u) continue;
            var arena = ArenaBinaryFormat.Deserialize(original)!.Value;
            Assert.Equal(ArenaPurpose.PvP, arena.Purpose);
            Assert.Equal(original, ArenaBinaryFormat.Serialize(arena));
            checkedStages++;
        }
        Assert.True(checkedStages > 0);
    }

    [Fact]
    public void TruncatedOrUnknownArcadeSectionIsRejected()
    {
        byte[] bytes = ArenaBinaryFormat.Serialize(Court(new(0f, 2f, 0f), default));
        for (int length = 0; length < bytes.Length; length++)
            Assert.Null(ArenaBinaryFormat.Deserialize(bytes.AsSpan(0, length).ToArray()));
        byte[] unknown = (byte[])bytes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(unknown.AsSpan(4), 7u);
        Assert.Null(ArenaBinaryFormat.Deserialize(unknown));
        byte[] invalid = (byte[])bytes.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(invalid.AsSpan(invalid.Length - 4), BitConverter.SingleToInt32Bits(float.NaN));
        Assert.Null(ArenaBinaryFormat.Deserialize(invalid));
    }

    [Fact]
    public void ArcadeProfileRejectsBadTuningDuplicatePackagesAndMultiplePlayerStarts()
    {
        Assert.Throws<ArgumentException>(() => new ArcadeBallDefinition(default, default, .3f, float.NaN, 1f, 1f));
        var ball = new ArcadeBallDefinition(default, default, .3f, 0f, 1f, 1f);
        Assert.Throws<ArgumentException>(() => new ArcadeStageDefinition("profile", 1, new[] { "fightguy", "fightguy" }, ball));
        var arena = Court(new(0f, 2f, 0f), default);
        arena.SpawnPoints = new SpawnPoint[4];
        Assert.Throws<ArgumentException>(() => ArenaBinaryFormat.Serialize(arena));
    }

    [Theory]
    [InlineData(5.5f, 2f, 120f, 0f)]
    [InlineData(0f, 7.5f, 0f, 120f)]
    [InlineData(0f, .5f, 0f, -120f)]
    public void FastBallReboundsFromBakedWallsCeilingAndFloor(float x, float y, float vx, float vy)
    {
        var sim = new ServerSimulation(Court(new(x, y, 0f), new(vx, vy, 0f)), NoWinMatchRule.Instance);
        sim.Tick(new());
        var ball = sim.ArcadeBall!.Value;
        Assert.InRange(ball.Position.X, -5.701f, 5.701f);
        Assert.InRange(ball.Position.Y, .299f, 7.701f);
        if (vx != 0f) Assert.True(ball.Velocity.X < 0f);
        else Assert.True(ball.Velocity.Y * vy < 0f);
    }

    [Fact]
    public void SeveralReboundsWithinOneTickDoNotTunnelOutOfTheCourt()
    {
        var sim = new ServerSimulation(Court(new(0f, 2f, 0f), new(2100f, 0f, 0f)), NoWinMatchRule.Instance);
        for (int i = 0; i < 10; i++)
        {
            sim.Tick(new());
            Assert.InRange(sim.ArcadeBall!.Value.Position.X, -5.701f, 5.701f);
        }
        Assert.True(sim.GetLastEntityData().Single().MotionPath!.Count > 1);
    }

    [Theory]
    [InlineData(ActionState.Idle)]
    [InlineData(ActionState.Shielding)]
    [InlineData(ActionState.AirDodgeMovement)]
    public void PassiveBodyDeflectionConservesBallSpeedAndLeavesFighterHarmless(ActionState stance)
    {
        var sim = PlayerCourt(new(-1.2f, .75f, 0f), new(60f, 0f, 0f), out var def);
        var fighter = TestHelpers.PlayerState();
        fighter.PY = def.CapsuleHeight * .5f;
        fighter.State = stance; fighter.InvincibilityTicks = 20;
        sim.SetState(1, fighter);
        sim.Tick(new());
        var ball = sim.ArcadeBall!.Value;
        Assert.Equal(-60f, ball.Velocity.X, 2);
        Assert.Equal(0f, ball.DamagePercent);
        Assert.Equal(0, sim.GetState(1).DamagePercent);
        Assert.Equal(0, sim.GetState(1).HitstunTicks);
        Assert.Equal(0f, sim.GetState(1).KVX);
        Assert.Empty(sim.LastTickHits);
    }

    [Fact]
    public void MovingBallCanCrossAStationaryMeleeCapsuleBetweenFrames()
    {
        var sim = PlayerCourt(new(-3f, 2f, 0f), new(360f, 0f, 0f), out _);
        MovePlayerAway(sim);
        var melee = Attack(0f, 2f, 0f);
        melee.Shape = HitboxShape.Capsule; melee.EndX = 0f; melee.EndY = 3f; melee.EndZ = 0f;
        melee.HitsMultipleOpponents = true; melee.FreezesOwner = true;
        sim.Resolver.Spawn(melee);
        sim.Tick(new());
        var hit = Assert.Single(sim.LastTickHits);
        Assert.Equal(sim.ArcadeBall!.Value.EntityId, hit.TargetEntityId);
        Assert.InRange(hit.ContactTime, .1f, .9f);
        Assert.True(sim.GetState(1).HitstopTicks > 0);
        Assert.True(sim.ArcadeBall.Value.Position.X < 0f);
        Assert.Single(sim.GetAllStates());
        Assert.Equal(0UL, sim.GetState(hit.TargetEntityId).EntityId);
    }

    [Fact]
    public void MovingProjectileAndBallMeetContinuouslyAndOtherProjectilesAdvanceOnce()
    {
        var sim = PlayerCourt(new(1f, 2f, 0f), new(-60f, 0f, 0f), out _);
        MovePlayerAway(sim);
        var projectile = Attack(-1f, 2f, 0f); projectile.VX = 120f;
        var unrelated = Attack(3f, 4f, 4f); unrelated.VX = 9f; unrelated.OwnerId = 1;
        sim.Resolver.Spawn(projectile); sim.Resolver.Spawn(unrelated);
        sim.Tick(new());
        var hit = Assert.Single(sim.LastTickHits);
        Assert.InRange(hit.ContactTime, 0f, 1f);
        Assert.True(hit.ContactTime < 1f);
        Assert.Equal(10f, sim.ArcadeBall!.Value.DamagePercent);
        var remaining = Assert.Single(sim.Resolver.GetActiveHitboxes());
        Assert.Equal(3f + 9f * Simulation.TickDt, remaining.X, 5);
        Assert.Equal(1, remaining.AgeTicks);
        Assert.Equal(0, sim.GetState(1).HitstopTicks); // projectiles retain normal owner timing
    }

    [Fact]
    public void FreshBallIdentityAllowsSameLiveMeleeWindowToHitAgainWithoutResettingKit()
    {
        var sim = PlayerCourt(new(0f, 2f, 0f), default, out _);
        MovePlayerAway(sim);
        var player = sim.GetState(1); player.SetCooldown(AbilitySlots.E, 120); sim.SetState(1, player);
        var melee = Attack(0f, 2f, 0f); melee.HitsMultipleOpponents = true;
        sim.Resolver.Spawn(melee);
        sim.Tick(new()); Assert.Single(sim.LastTickHits);
        ulong previous = sim.ArcadeBall!.Value.EntityId;
        sim.Tick(new()); Assert.Empty(sim.LastTickHits);
        ushort cooldown = sim.GetState(1).GetCooldown(AbilitySlots.E);
        sim.ResetArcadeBall();
        Assert.NotEqual(previous, sim.ArcadeBall.Value.EntityId);
        Assert.Equal(0f, sim.ArcadeBall.Value.DamagePercent);
        Assert.Equal(cooldown, sim.GetState(1).GetCooldown(AbilitySlots.E));
        sim.Tick(new()); Assert.Single(sim.LastTickHits);
    }

    [Fact]
    public void BallPercentAndMoveStrengthBothIncreaseLaunchAndDirectionIsPreserved()
    {
        var sim = PlayerCourt(new(0f, 2f, 0f), default, out _);
        MovePlayerAway(sim);
        var first = Attack(0f, 2f, 0f); first.KnockbackAngle = 50;
        sim.Resolver.Spawn(first); sim.Tick(new());
        float speed = Speed(sim.ArcadeBall!.Value.Velocity);
        var position = sim.ArcadeBall.Value.Position;
        var stronger = Attack(position.X, position.Y, position.Z);
        stronger.BaseKnockback = first.BaseKnockback * 2f;
        stronger.KnockbackGrowth = first.KnockbackGrowth * 2f;
        stronger.KnockbackAngle = -30;
        stronger.KnockbackDirection = AuthoringKnockbackDirection.TowardOwner;
        sim.Resolver.Spawn(stronger); sim.Tick(new());
        var ball = sim.ArcadeBall.Value;
        Assert.Equal(20f, ball.DamagePercent);
        Assert.True(Speed(ball.Velocity) > speed);
        Assert.True(ball.Velocity.Y < 0f);
        Assert.True(ball.Velocity.X < 0f && ball.Velocity.Z < 0f);
    }

    [Fact]
    public void NormalTargetLockUsesLocalAliasAndDropsOldBallIdentityOnReset()
    {
        var sim = PlayerCourt(new(0f, 2f, 3f), default, out _);
        var input = new InputState { TargetEntityId = ServerSimulation.ArcadeBallTargetInputId, ToggleLock = true };
        sim.Tick(new() { [1] = input });
        ulong previous = sim.ArcadeBall!.Value.EntityId;
        Assert.True(sim.GetState(1).LockOn);
        Assert.Equal(previous, sim.GetState(1).TargetEntityId);
        Assert.True(sim.TryGetCombatTargetPosition(previous, out var position));
        Assert.Equal(3f, position.Z);
        sim.ResetArcadeBall(); sim.Tick(new());
        Assert.False(sim.TryGetCombatTargetPosition(previous, out _));
        Assert.Equal(sim.ArcadeBall.Value.EntityId, sim.GetState(1).TargetEntityId);
        Assert.Throws<ArgumentException>(() => sim.RegisterEntity(255, TestHelpers.EngineDef, default));
    }

    [Fact]
    public void SameInputsReplayIdenticalBallAndFighterState()
    {
        var first = PlayerCourt(new(0f, 2f, 2f), new(16f, 9f, 21f), out _);
        var second = PlayerCourt(new(0f, 2f, 2f), new(16f, 9f, 21f), out _);
        for (int tick = 0; tick < 300; tick++)
        {
            var inputs = new Dictionary<ulong, InputState> { [1] = new() { MoveX = tick < 30 ? .5f : 0f } };
            first.Tick(inputs); second.Tick(inputs);
            var a = first.ArcadeBall!.Value; var b = second.ArcadeBall!.Value;
            Assert.Equal(a.Position.X, b.Position.X); Assert.Equal(a.Position.Y, b.Position.Y);
            Assert.Equal(a.Position.Z, b.Position.Z); Assert.Equal(a.Velocity.X, b.Velocity.X);
            Assert.Equal(first.GetState(1).PX, second.GetState(1).PX);
        }
    }

    private static ServerSimulation PlayerCourt(ArcadePoint position, ArcadePoint velocity, out CharacterDefinition def)
    {
        var sim = new ServerSimulation(Court(position, velocity), NoWinMatchRule.Instance);
        def = TestHelpers.EngineDef;
        var state = TestHelpers.PlayerState(); state.PY = def.CapsuleHeight * .5f;
        sim.RegisterEntity(1, def, state);
        return sim;
    }
    private static void MovePlayerAway(ServerSimulation sim)
    { var state = sim.GetState(1); state.PX = state.PZ = -4f; sim.SetState(1, state); }
    private static float Speed(ArcadePoint velocity)
        => MathF.Sqrt(velocity.X * velocity.X + velocity.Y * velocity.Y + velocity.Z * velocity.Z);
    private static Hitbox Attack(float x, float y, float z) => new()
    {
        OwnerId = 1, AttackSlot = 1, ActivationId = 7,
        X = x, Y = y, Z = z, EndX = x, EndY = y, EndZ = z,
        Radius = .15f, Shape = HitboxShape.Sphere, Damage = 10f,
        BaseKnockback = 20f, KnockbackGrowth = 20f, KnockbackAngle = 30,
        DurationTicks = 30,
    };
    private static ArenaDefinition Court(ArcadePoint position, ArcadePoint velocity)
    {
        var arena = TestHelpers.TestArena();
        arena.Name = "ball_fixture";
        arena.MinX = arena.MinZ = -6f; arena.MaxX = arena.MaxZ = 6f;
        arena.Heightmap = new ArenaHeightmap
        {
            Width = 13, Height = 13, CellSize = 1f, OriginX = -6f, OriginZ = -6f,
            Data = new float[13 * 13],
        };
        arena.SpawnPoints = new[] { new SpawnPoint { Y = .75f } };
        arena.Arcade = new("ball_fixture", 1, new[] { "fightguy" }, new(position, velocity, .3f, 0f, 1f, .5f));
        var triangles = new List<CollisionTriangle>();
        Quad(triangles, new(-6, 0, -6), new(-6, 0, 6), new(6, 0, 6), new(6, 0, -6));
        Quad(triangles, new(-6, 8, -6), new(6, 8, -6), new(6, 8, 6), new(-6, 8, 6));
        Quad(triangles, new(-6, 0, -6), new(-6, 8, -6), new(-6, 8, 6), new(-6, 0, 6));
        Quad(triangles, new(6, 0, -6), new(6, 0, 6), new(6, 8, 6), new(6, 8, -6));
        Quad(triangles, new(-6, 0, -6), new(6, 0, -6), new(6, 8, -6), new(-6, 8, -6));
        Quad(triangles, new(-6, 0, 6), new(-6, 8, 6), new(6, 8, 6), new(6, 0, 6));
        arena.CollisionTriangles = triangles.ToArray();
        arena.SpatialGrid = ArenaCollision.BuildSpatialGrid(in arena);
        return arena;
    }
    private static void Quad(List<CollisionTriangle> triangles, ArcadePoint a, ArcadePoint b, ArcadePoint c, ArcadePoint d)
    {
        triangles.Add(new() { AX = a.X, AY = a.Y, AZ = a.Z, BX = b.X, BY = b.Y, BZ = b.Z, CX = c.X, CY = c.Y, CZ = c.Z });
        triangles.Add(new() { AX = a.X, AY = a.Y, AZ = a.Z, BX = c.X, BY = c.Y, BZ = c.Z, CX = d.X, CY = d.Y, CZ = d.Z });
    }
}
