using System.Buffers.Binary;
using Xunit;

namespace SlopArena.Shared.Tests;

public class SlopballTests
{
    [Fact]
    public void StageRoundTripPinsProfileAndKeepsPhaseOneSectionReadable()
    {
        var arena = Court();
        var bytes = ArenaBinaryFormat.Serialize(arena);
        var loaded = ArenaBinaryFormat.Deserialize(bytes)!.Value;
        Assert.Equal(6u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)));
        Assert.Equal(1337u, loaded.Arcade!.Slopball!.Seed);
        Assert.Equal(45, loaded.Arcade.Slopball.ServeGapTicks);
        Assert.Equal(bytes, ArenaBinaryFormat.Serialize(loaded));
        var legacy = new ArcadeStageDefinition("lab", 1, new[] { "fightguy" }, arena.Arcade!.Ball);
        arena.Arcade = legacy;
        Assert.Null(ArenaBinaryFormat.Deserialize(ArenaBinaryFormat.Serialize(arena))!.Value.Arcade!.Slopball);
        Assert.Throws<ArgumentException>(() => new SlopballDefinition(0, .05f, 1.1f, 0, 0, 45, 108, 72, 6, 40, 2, -5, -3));
        arena.Arcade = loaded.Arcade;
        arena.CollisionTriangles = arena.CollisionTriangles.Take(12).ToArray();
        Assert.Throws<ArgumentException>(() => ArenaBinaryFormat.Serialize(arena));
    }

    [Fact]
    public void SeededShotsRepeatVaryAndContinueAtCappedDifficulty()
    {
        var stage = Court().Arcade!; var rules = stage.Slopball!;
        var first = rules.CreateShot(stage.Ball, 0);
        Assert.Equal(first, rules.CreateShot(stage.Ball, 0));
        Assert.NotEqual(first.Velocity, rules.CreateShot(stage.Ball, 1).Velocity);
        for (ulong i = 0; i < 100; i++)
        {
            var shot = rules.CreateShot(stage.Ball, i);
            Assert.InRange(shot.FlightTicks, rules.HardFlightTicks - rules.FlightJitterTicks, rules.EasyFlightTicks + rules.FlightJitterTicks);
            if (i >= rules.RampReturns) Assert.InRange(shot.FlightTicks, rules.HardFlightTicks - rules.FlightJitterTicks, rules.HardFlightTicks + rules.FlightJitterTicks);
        }
        Assert.NotEqual(rules.CreateShot(stage.Ball, 0).Velocity, rules.CreateShot(stage.Ball, 1UL << 32).Velocity);
    }

    [Fact]
    public void UntouchedServeEntersPlayerHalfThenItsFirstFloorEndsAttempt()
    {
        var sim = new ServerSimulation(Court(), NoWinMatchRule.Instance);
        bool received = false;
        for (int i = 0; i < 300 && !sim.Slopball!.Value.Ended; i++)
        {
            sim.Tick(new());
            received |= sim.LastTickSlopballEvents.Any(e => e.Kind == SlopballEventKind.Received);
        }
        Assert.True(received);
        Assert.Equal(SlopballPhase.Missed, sim.Slopball!.Value.Phase);
        Assert.Equal(0UL, sim.Slopball.Value.Returns);
        Assert.False(sim.ArcadeBall!.Value.Active);
        uint tick = sim.SimulationTick;
        sim.Tick(new()); Assert.Equal(tick, sim.SimulationTick);
    }

    [Fact]
    public void SenderFloorReServesIdenticalShotWithFreshIdentityAndContinuedResources()
    {
        var arena = Court(); var ball = new ArcadeBallSimulation(arena.Arcade!.Ball, arena);
        var rules = new SlopballSimulation(arena.Arcade.Slopball!, arena.Arcade.Ball, ball);
        Serve(rules, ball);
        var shot = rules.State.Shot; ulong id = ball.State.EntityId;
        ball.Reset(new(0, .35f, 2), new(0, -120, 0));
        Advance(rules, ball, arena);
        Assert.Equal(SlopballPhase.WaitingServe, rules.State.Phase);
        Assert.Equal(0UL, rules.State.Returns);
        Assert.Equal(shot, rules.State.Shot);
        Serve(rules, ball);
        Assert.NotEqual(id, ball.State.EntityId);
        Assert.Equal(shot.Velocity, ball.State.Velocity);
        Assert.Equal(0f, ball.State.DamagePercent);
    }

    [Fact]
    public void EarlyInterceptionDoesNotArmWhenTheBallLaterEntersPlayerHalf()
    {
        var (arena, ball, rules) = Fixture();
        ball.Reset(new(0, 3, 1), new(0, 0, -240));
        rules.BeginTick(ball); ball.Step(arena, new Dictionary<ulong, CharacterState>(), new Dictionary<ulong, CharacterDefinition>());
        rules.PlanMotion(ball);
        Assert.True(rules.BeforeAttack(ball, .01f));
        Assert.False(rules.State.ReturnArmed);
        // The accepted early launch invalidates later crossings in that pre-hit path.
        rules.FinishTick(ball); Assert.False(rules.State.Received);
        ball.Reset(new(0, 3, -1), new(0, 0, 240));
        Advance(rules, ball, arena);
        Assert.True(rules.State.Received);
        Assert.Equal(0UL, rules.State.Returns);
        Assert.False(rules.State.ReturnArmed);
    }

    [Fact]
    public void ReceivedAttackCanBankOffPlayerWallAndScoresOnlyOnce()
    {
        var (arena, ball, rules) = Fixture();
        ReceiveAndArm(arena, ball, rules);
        ball.Reset(new(5.5f, 3, -1), new(120, 0, 120));
        Advance(rules, ball, arena);
        Assert.Equal(1UL, rules.State.Returns);
        Assert.Equal(SlopballPhase.WaitingServe, rules.State.Phase);
        Assert.Single(rules.Events.Where(e => e.Kind == SlopballEventKind.Returned));
        Assert.Contains(ball.Contacts, c => c.Position.X >= 5.699f);
        for (int i = 0; i < 10; i++) Advance(rules, ball, arena);
        Assert.Equal(1UL, rules.State.Returns);
    }

    [Fact]
    public void EarlierFloorWinsOverLaterClearAndAnEarlierAttackCanSaveTheShot()
    {
        var (arena, ball, rules) = Fixture();
        ReceiveAndArm(arena, ball, rules);
        // Floor at t~.02, net crossing above the net only after its floor rebound.
        ball.Reset(new(0, .35f, -1), new(0, -120, 100));
        Advance(rules, ball, arena);
        Assert.Equal(SlopballPhase.Missed, rules.State.Phase);
        Assert.Equal(0UL, rules.State.Returns);

        (arena, ball, rules) = Fixture();
        ball.Reset(new(0, .4f, -2), new(0, -60, 0));
        rules.BeginTick(ball); ball.Step(arena, new Dictionary<ulong, CharacterState>(), new Dictionary<ulong, CharacterDefinition>()); rules.PlanMotion(ball);
        Assert.True(rules.BeforeAttack(ball, .01f));
        ball.Hit(new SpellResolver.HitResult { ContactTime = .01f, KnockbackAngle = 45 }, 10, 40, 0, 1, 8);
        rules.FinishTick(ball);
        Assert.Equal(SlopballPhase.Live, rules.State.Phase);
        Assert.True(rules.State.ReturnArmed);
        Assert.True(ball.State.Position.Y > ball.State.Radius);
        Assert.True(ball.State.Velocity.Y > 0);
    }

    [Fact]
    public void FloorBoundaryExcludesLaterProjectileContactsWithoutConsumingTheProjectile()
    {
        var (arena, ball, rules) = Fixture();
        ball.Reset(new(0, .35f, -2), new(0, -120, 240));
        rules.BeginTick(ball); ball.Step(arena, new Dictionary<ulong, CharacterState>(), new Dictionary<ulong, CharacterDefinition>());
        rules.PlanMotion(ball);
        var resolver = new SpellResolver();
        resolver.Spawn(new Hitbox { OwnerId = 1, X = 0, Y = 1.2f, Z = -1, Radius = .2f, Shape = HitboxShape.Sphere, Damage = 10, DurationTicks = 30 });
        Assert.Empty(resolver.Tick(new() { ball.CollisionTarget }, orderMovingTargetContacts: true));
        Assert.Single(resolver.GetActiveHitboxes());
        rules.FinishTick(ball);
        Assert.Equal(SlopballPhase.Missed, rules.State.Phase);
    }

    [Fact]
    public void BodyBankAfterReceivingCanScoreWithoutDamageOrAddedSpeed()
    {
        var (arena, ball, rules) = Fixture();
        var def = TestHelpers.EngineDef;
        var body = TestHelpers.PlayerState(); body.PY = def.CapsuleHeight * .5f; body.PZ = -2;
        ball.Reset(new(0, 1.5f, -.7f), new(0, 0, -180));
        rules.BeginTick(ball);
        ball.Step(arena, new Dictionary<ulong, CharacterState> { [1] = body }, new Dictionary<ulong, CharacterDefinition> { [1] = def });
        rules.PlanMotion(ball); rules.FinishTick(ball);
        Assert.Equal(1UL, rules.State.Returns);
        Assert.Equal(0f, ball.State.DamagePercent);
        Assert.Equal(0, body.DamagePercent);
    }

    [Fact]
    public void NetReboundsStayLiveAndDoNotCountAsFloorMisses()
    {
        var (arena, ball, rules) = Fixture();
        ball.Reset(new(0, .8f, -1), new(0, 0, 120));
        Advance(rules, ball, arena);
        Assert.Equal(SlopballPhase.Live, rules.State.Phase);
        Assert.True(ball.State.Velocity.Z < 0);
        Assert.Equal(0UL, rules.State.Returns);
    }

    [Fact]
    public void WaitingGapsKeepCooldownsAndProjectilesMovingAndOnlyOneFighterIsAdmitted()
    {
        var arena = Court(); var sim = new ServerSimulation(arena, NoWinMatchRule.Instance);
        var def = TestHelpers.EngineDef; var state = TestHelpers.PlayerState(); state.PZ = -3;
        state.SetCooldown(AbilitySlots.E, 100); sim.RegisterEntity(1, def, state);
        sim.Resolver.Spawn(new Hitbox { OwnerId = 1, Shape = HitboxShape.Sphere, X = -4, Y = 3, Z = -4, VX = 6, Radius = .1f, DurationTicks = 100 });
        sim.Tick(new());
        Assert.Equal(99, sim.GetState(1).GetCooldown(AbilitySlots.E));
        Assert.Equal(-4f + 6 * Simulation.TickDt, sim.Resolver.GetActiveHitboxes().Single().X, 5);
        Assert.Throws<ArgumentException>(() => sim.RegisterEntity(2, def, state));
        Assert.Throws<InvalidOperationException>(() => sim.ResetArcadeBall());
    }

    [Fact]
    public void FighterHalfConstraintStopsFastCrossingsAtAnyHeight()
    {
        var sim = new ServerSimulation(Court(), NoWinMatchRule.Instance); var def = TestHelpers.EngineDef;
        var state = TestHelpers.PlayerState(); state.PZ = -.4f; state.PY = 4; state.VZ = 1000;
        sim.RegisterEntity(1, def, state); sim.Tick(new() { [1] = new InputState { MoveY = 1 } });
        Assert.True(sim.GetState(1).PZ <= -.05f - def.CapsuleRadius);
        Assert.True(sim.GetState(1).VZ <= 0);
    }

    [Fact]
    public void ResolverSelectsEarliestBallContactRatherThanNewestHitbox()
    {
        var ball = new SpellResolver.EntityData { Id = 999, Active = true, Radius = .3f, Shape = HitboxShape.Sphere,
            MotionPath = new[] { new SphereMotionSegment(0, 1, new(-3, 3, 0), new(3, 3, 0)) } };
        var resolver = new SpellResolver();
        resolver.Spawn(new Hitbox { OwnerId = 1, X = -2, Y = 3, Radius = .1f, Shape = HitboxShape.Sphere, Damage = 3, DurationTicks = 30 });
        resolver.Spawn(new Hitbox { OwnerId = 1, X = 2, Y = 3, Radius = .1f, Shape = HitboxShape.Sphere, Damage = 12, DurationTicks = 30 });
        var hit = Assert.Single(resolver.Tick(new() { ball }, orderMovingTargetContacts: true));
        Assert.Equal(3f, hit.Damage);
        Assert.InRange(hit.ContactTime, 0, .3f);
        Assert.Single(resolver.GetActiveHitboxes());
    }

    private static void ReceiveAndArm(ArenaDefinition arena, ArcadeBallSimulation ball, SlopballSimulation rules)
    {
        ball.Reset(new(0, 3, -1), default);
        rules.BeginTick(ball); ball.Step(arena, new Dictionary<ulong, CharacterState>(), new Dictionary<ulong, CharacterDefinition>()); rules.PlanMotion(ball);
        Assert.True(rules.BeforeAttack(ball, .5f)); rules.FinishTick(ball);
    }
    private static (ArenaDefinition Arena, ArcadeBallSimulation Ball, SlopballSimulation Rules) Fixture()
    {
        var arena = Court(); var ball = new ArcadeBallSimulation(arena.Arcade!.Ball, arena);
        var rules = new SlopballSimulation(arena.Arcade.Slopball!, arena.Arcade.Ball, ball); Serve(rules, ball);
        return (arena, ball, rules);
    }
    private static void Serve(SlopballSimulation rules, ArcadeBallSimulation ball)
    { while (rules.State.Phase == SlopballPhase.WaitingServe) rules.BeginTick(ball); }
    private static void Advance(SlopballSimulation rules, ArcadeBallSimulation ball, ArenaDefinition arena)
    {
        if (!rules.BeginTick(ball)) return;
        ball.Step(arena, new Dictionary<ulong, CharacterState>(), new Dictionary<ulong, CharacterDefinition>());
        rules.PlanMotion(ball); rules.FinishTick(ball);
    }
    internal static ArenaDefinition Court()
    {
        var arena = TestHelpers.TestArena(); arena.Name = "slopball_fixture";
        arena.MinX = arena.MinZ = -6; arena.MaxX = arena.MaxZ = 6;
        arena.Heightmap = new() { Width = 13, Height = 13, CellSize = 1, OriginX = -6, OriginZ = -6, Data = new float[169] };
        arena.SpawnPoints = new[] { new SpawnPoint { Y = .85f, Z = -3 } };
        arena.Arcade = new("slopball_fixture", 1, new[] { "fightguy", "manki", "wibou", "bonk" },
            new(new(0, 2.3f, 3), default, .3f, 8, 1, 2.4f), new(0, .05f, 1.1f, 0, 1337, 45, 108, 72, 6, 40, 2.2f, -5, -3));
        var triangles = new List<CollisionTriangle>();
        Quad(triangles, new(-6, 0, -6), new(-6, 0, 6), new(6, 0, 6), new(6, 0, -6));
        Quad(triangles, new(-6, 6, -6), new(6, 6, -6), new(6, 6, 6), new(-6, 6, 6));
        Quad(triangles, new(-6, 0, -6), new(-6, 6, -6), new(-6, 6, 6), new(-6, 0, 6));
        Quad(triangles, new(6, 0, -6), new(6, 0, 6), new(6, 6, 6), new(6, 6, -6));
        Quad(triangles, new(-6, 0, -6), new(6, 0, -6), new(6, 6, -6), new(-6, 6, -6));
        Quad(triangles, new(-6, 0, 6), new(-6, 6, 6), new(6, 6, 6), new(6, 0, 6));
        Quad(triangles, new(-6, 0, -.05f), new(6, 0, -.05f), new(6, 1.1f, -.05f), new(-6, 1.1f, -.05f));
        Quad(triangles, new(-6, 0, .05f), new(-6, 1.1f, .05f), new(6, 1.1f, .05f), new(6, 0, .05f));
        Quad(triangles, new(-6, 1.1f, -.05f), new(6, 1.1f, -.05f), new(6, 1.1f, .05f), new(-6, 1.1f, .05f));
        arena.CollisionTriangles = triangles.ToArray(); arena.SpatialGrid = ArenaCollision.BuildSpatialGrid(arena);
        return arena;
    }
    private static void Quad(List<CollisionTriangle> list, ArcadePoint a, ArcadePoint b, ArcadePoint c, ArcadePoint d)
    {
        list.Add(new() { AX = a.X, AY = a.Y, AZ = a.Z, BX = b.X, BY = b.Y, BZ = b.Z, CX = c.X, CY = c.Y, CZ = c.Z });
        list.Add(new() { AX = a.X, AY = a.Y, AZ = a.Z, BX = c.X, BY = c.Y, BZ = c.Z, CX = d.X, CY = d.Y, CZ = d.Z });
    }
}
