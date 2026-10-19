using System.Buffers.Binary;
using Xunit;

namespace SlopArena.Shared.Tests;

public class SlopballTests
{
    [Fact]
    public void VariedDampedServesCoverBothDirectionsAndDifferentArcsWhileStayingReceivable()
    {
        var arena = VariedCourt(); var profile = arena.Arcade!; var rules = profile.Slopball!;
        rules.ValidateCourt(arena);
        float minYaw = float.MaxValue, maxYaw = float.MinValue, minPitch = float.MaxValue, maxPitch = float.MinValue;
        int shortShots = 0, deepShots = 0;
        var kinds = new HashSet<SlopballServe>();
        float minSpawnX = 999, maxSpawnX = -999, minSpawnZ = 999, maxSpawnZ = -999;
        for (ulong i = 0; i < 1024; i++)
        {
            uint seed = unchecked((uint)(i + 1) * 2654435761u);
            float ceiling = rules.CeilingOf(arena);
            var shot = rules.CreateShot(profile.Ball, i, Math.Min(i, rules.RampReturns), seed, ceiling);
            Assert.Equal(shot, rules.CreateShot(profile.Ball, i, Math.Min(i, rules.RampReturns), seed, ceiling));
            Assert.InRange(shot.Spawn.X, -8, 8);
            Assert.InRange(shot.Spawn.Z, 5, 7);
            Assert.Equal(profile.Ball.Spawn.Y, shot.Spawn.Y);
            minSpawnX = MathF.Min(minSpawnX, shot.Spawn.X); maxSpawnX = MathF.Max(maxSpawnX, shot.Spawn.X);
            minSpawnZ = MathF.Min(minSpawnZ, shot.Spawn.Z); maxSpawnZ = MathF.Max(maxSpawnZ, shot.Spawn.Z);
            float yaw = MathF.Atan2(shot.Velocity.X, -shot.Velocity.Z) * 180 / MathF.PI;
            float pitch = MathF.Atan2(shot.Velocity.Y, MathF.Sqrt(shot.Velocity.X * shot.Velocity.X + shot.Velocity.Z * shot.Velocity.Z)) * 180 / MathF.PI;
            minYaw = MathF.Min(minYaw, yaw); maxYaw = MathF.Max(maxYaw, yaw);
            minPitch = MathF.Min(minPitch, pitch); maxPitch = MathF.Max(maxPitch, pitch);
            var ball = new ArcadeBallSimulation(profile.Ball, arena); ball.Reset(shot.Spawn, shot.Velocity, shot.Curve);
            kinds.Add(shot.Kind);
            bool crossed = false, landed = false;
            for (int tick = 0; tick < shot.FlightTicks + 2; tick++)
            {
                ball.Step(arena, new Dictionary<ulong, CharacterState>(), new Dictionary<ulong, CharacterDefinition>());
                crossed |= ball.State.Position.Z < -rules.NetHalfThickness - profile.Ball.Radius;
                if (ball.Contacts.Any(c => c.NormalY > .5f && c.Position.Y < rules.FloorY + profile.Ball.Radius + .001f))
                {
                    var point = ball.Contacts.First(c => c.NormalY > .5f).Position;
                    Assert.InRange(point.X, -rules.LandingHalfWidth - .005f, rules.LandingHalfWidth + .005f);
                    Assert.InRange(point.Z, rules.LandingMinZ - .005f, rules.LandingMaxZ + .005f);
                    if (point.Z > -5) shortShots++;
                    if (point.Z < -11) deepShots++;
                    landed = true; break;
                }
            }
            Assert.True(crossed); Assert.True(landed);
        }
        Assert.True(minSpawnX < -6 && maxSpawnX > 6 && minSpawnZ < 5.25f && maxSpawnZ > 6.75f);
        Assert.True(minYaw < -20 && maxYaw > 20);
        Assert.True(maxPitch - minPitch > 30);
        Assert.True(shortShots >= 10 && deepShots >= 10);
        Assert.Equal(4, kinds.Count);
        Assert.Equal(ArenaBinaryFormat.Serialize(arena), ArenaBinaryFormat.Serialize(ArenaBinaryFormat.Deserialize(ArenaBinaryFormat.Serialize(arena))!.Value));
    }

    [Fact]
    public void RandomLauncherBoundsRoundTripWhileLegacyProfilesKeepFixedOrigins()
    {
        var profile = VariedCourt().Arcade!;
        var bytes = ArcadeStageBinaryFormat.Serialize(profile);
        Assert.Equal(4, BinaryPrimitives.ReadUInt16LittleEndian(bytes));
        var loaded = ArcadeStageBinaryFormat.Deserialize(bytes)!;
        Assert.Equal(8, loaded.Slopball!.SpawnHalfWidth);
        Assert.Equal(1, loaded.Slopball.SpawnHalfDepth);
        Assert.Equal(bytes, ArcadeStageBinaryFormat.Serialize(loaded));
        for (int length = 0; length < bytes.Length; length++)
            Assert.Null(ArcadeStageBinaryFormat.Deserialize(bytes[..length]));
        var legacy = Court().Arcade!;
        for (ulong i = 0; i < 128; i++)
            Assert.Equal(legacy.Ball.Spawn, legacy.Slopball!.CreateShot(legacy.Ball, i).Spawn);
        var r = profile.Slopball!;
        SlopballDefinition Bounds(float width, float depth) => new(r.NetZ, r.NetHalfThickness, r.NetTop, r.FloorY,
            r.Seed, r.ServeGapTicks, r.EasyFlightTicks, r.HardFlightTicks, r.FlightJitterTicks,
            r.RampReturns, r.LandingHalfWidth, r.LandingMinZ, r.LandingMaxZ, width, depth);
        Assert.Throws<ArgumentException>(() => Bounds(-1, 1));
        Assert.Throws<ArgumentException>(() => Bounds(8, float.NaN));
        foreach (var bounds in new[] { Bounds(20, 1), Bounds(8, 6), Bounds(8, 20) })
        {
            var arena = VariedCourt();
            arena.Arcade = new(profile.ProfileId, profile.GameplayRevision, profile.AllowedPackageIds, profile.Ball, bounds);
            Assert.Throws<ArgumentException>(() => ArenaBinaryFormat.Serialize(arena));
        }
    }

    internal static ArenaDefinition VariedCourt()
    {
        var arena = Court();
        arena.MinX = arena.MinZ = -20; arena.MaxX = arena.MaxZ = 20;
        arena.Heightmap = new() { Width = 41, Height = 41, CellSize = 1, OriginX = -20, OriginZ = -20, Data = new float[1681] };
        for (int i = 0; i < arena.CollisionTriangles.Length; i++)
        {
            var t = arena.CollisionTriangles[i];
            static float Side(float x) => MathF.Abs(x) == 6 ? MathF.CopySign(20, x) : x;
            static float Roof(float y) => y == 6 ? 20 : y;
            t.AX = Side(t.AX); t.AZ = Side(t.AZ); t.AY = Roof(t.AY);
            t.BX = Side(t.BX); t.BZ = Side(t.BZ); t.BY = Roof(t.BY);
            t.CX = Side(t.CX); t.CZ = Side(t.CZ); t.CY = Roof(t.CY);
            arena.CollisionTriangles[i] = t;
        }
        arena.SpawnPoints = new[] { new SpawnPoint { Y = .85f, Z = -6 } };
        arena.Arcade = new("varied_slopball", 6, arena.Arcade!.AllowedPackageIds,
            new(new(0, 2.3f, 6), default, .65f, 6, .8f, 2.4f, .35f),
            new(0, .05f, 1.1f, 0, 1337, 45, 138, 108, 30, 40, 6, -14, -2.5f, 8, 1));
        arena.SpatialGrid = ArenaCollision.BuildSpatialGrid(arena);
        return arena;
    }

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
            int quickest = rules.HardFlightTicks - rules.FlightJitterTicks, slowest = rules.EasyFlightTicks + rules.FlightJitterTicks;
            if (shot.Kind == SlopballServe.Zinger) Assert.InRange(shot.FlightTicks, 30, slowest * .68f + 1);
            else if (shot.Kind == SlopballServe.Moonball) Assert.InRange(shot.FlightTicks, quickest, 300);
            else
            {
                Assert.InRange(shot.FlightTicks, quickest, slowest);
                if (i >= rules.RampReturns) Assert.InRange(shot.FlightTicks, quickest, rules.HardFlightTicks + rules.FlightJitterTicks);
            }
        }
        Assert.NotEqual(rules.CreateShot(stage.Ball, 0).Velocity, rules.CreateShot(stage.Ball, 1UL << 32).Velocity);
    }

    [Fact]
    public void UntouchedServeResetsTheComboAndAutomaticallyContinues()
    {
        var sim = new ServerSimulation(Court(), NoWinMatchRule.Instance);
        bool received = false, missed = false;
        for (int i = 0; i < 300 && !missed; i++)
        {
            sim.Tick(new());
            received |= sim.LastTickSlopballEvents.Any(e => e.Kind == SlopballEventKind.Received);
            missed |= sim.LastTickSlopballEvents.Any(e => e.Kind == SlopballEventKind.Missed);
        }
        Assert.True(received); Assert.True(missed);
        Assert.False(sim.Slopball!.Value.Ended);
        Assert.Equal(SlopballPhase.WaitingServe, sim.Slopball!.Value.Phase);
        Assert.Equal(0UL, sim.Slopball.Value.Returns);
        Assert.False(sim.ArcadeBall!.Value.Active);
        uint tick = sim.SimulationTick;
        var previous = sim.Slopball.Value.Shot;
        sim.Tick(new()); Assert.Equal(tick + 1, sim.SimulationTick);
        for (int i = 1; i < 45; i++) sim.Tick(new());
        Assert.Equal(SlopballPhase.Live, sim.Slopball.Value.Phase);
        Assert.True(sim.ArcadeBall.Value.Active);
        Assert.Equal(previous, sim.Slopball.Value.Shot);
    }

    [Fact]
    public void MissKeepsTheBestComboWhileResettingDifficultyAndAdvancingTheShotSequence()
    {
        var (arena, ball, rules) = Fixture();
        for (int i = 0; i < 2; i++)
        {
            ReceiveAndArm(arena, ball, rules);
            ball.Reset(new(0, 3, -1), new(0, 0, 120)); Advance(rules, ball, arena);
            ball.Reset(new(0, .35f, 2), new(0, -120, 0)); Advance(rules, ball, arena);
            Serve(rules, ball);
        }
        Assert.Equal(2UL, rules.State.Returns);
        Assert.Equal(2UL, rules.State.BestStreak);
        var shot = rules.State.Shot;
        ball.Reset(new(0, .35f, -2), new(0, -120, 0)); Advance(rules, ball, arena);
        Assert.Equal(2UL, Assert.Single(rules.Events.Where(e => e.Kind == SlopballEventKind.Missed)).Returns);
        Assert.Equal(0UL, rules.State.Returns);
        Assert.Equal(2UL, rules.State.BestStreak);
        Assert.Equal(0f, rules.State.Difficulty);
        Assert.Equal(shot.Index + 1, rules.State.Shot.Index);
        Assert.NotEqual(shot.Velocity, rules.State.Shot.Velocity);
        Assert.False(rules.State.Ended);
        Serve(rules, ball);
        Assert.Equal(SlopballPhase.Live, rules.State.Phase);
        Assert.Equal(0f, ball.State.DamagePercent);
        rules.Stop(ball);
        Assert.Equal(2UL, rules.State.BestStreak);
    }

    [Fact]
    public void MissKeepsFighterPositionCooldownsAndExistingProjectiles()
    {
        var sim = new ServerSimulation(Court(), NoWinMatchRule.Instance);
        var player = TestHelpers.PlayerState(); player.PX = 4; player.PZ = -3;
        player.SetCooldown(AbilitySlots.E, 600);
        sim.RegisterEntity(1, TestHelpers.EngineDef, player);
        sim.Resolver.Spawn(new Hitbox { OwnerId = 1, X = -4, Y = 3, Z = -4, VX = .1f,
            Radius = .05f, Shape = HitboxShape.Sphere, DurationTicks = 1000 });
        for (int i = 0; i < 250; i++)
        {
            sim.Tick(new());
            if (sim.LastTickSlopballEvents.Any(e => e.Kind == SlopballEventKind.Missed)) break;
        }
        Assert.Contains(sim.LastTickSlopballEvents, e => e.Kind == SlopballEventKind.Missed);
        var state = sim.GetState(1); uint tick = sim.SimulationTick;
        Assert.Equal(4f, state.PX);
        Assert.Equal((ushort)(600 - tick), state.GetCooldown(AbilitySlots.E));
        int age = Assert.Single(sim.Resolver.GetActiveHitboxes()).AgeTicks;
        sim.Tick(new() { [1] = new InputState { MoveX = 1 } });
        Assert.True(sim.GetState(1).PX > state.PX);
        Assert.Equal(state.GetCooldown(AbilitySlots.E) - 1, sim.GetState(1).GetCooldown(AbilitySlots.E));
        Assert.Equal(age + 1, Assert.Single(sim.Resolver.GetActiveHitboxes()).AgeTicks);
        Assert.Equal(tick + 1, sim.SimulationTick);
    }

    [Fact]
    public void SessionSeedsVaryTheFirstServeButStillAllowDeterministicReplay()
    {
        ServerSimulation Make(uint seed)
        {
            var sim = new ServerSimulation(VariedCourt(), NoWinMatchRule.Instance);
            sim.RegisterEntity(1, TestHelpers.EngineDef, TestHelpers.PlayerState() with { PZ = -6 });
            sim.BeginSlopballAttempt(3, seed);
            for (int i = 0; i < 3; i++) sim.Tick(new());
            return sim;
        }
        var first = Make(123); var replay = Make(123); var other = Make(456);
        Assert.Equal(123u, first.Slopball!.Value.SessionSeed);
        Assert.Equal(first.Slopball.Value.Shot, replay.Slopball!.Value.Shot);
        Assert.NotEqual(first.Slopball.Value.Shot.Velocity, other.Slopball!.Value.Shot.Velocity);
    }

    [Fact]
    public void UncreditedSenderFloorSendsAFreshRandomShotWithoutAdvancingTheCombo()
    {
        var arena = Court(); var ball = new ArcadeBallSimulation(arena.Arcade!.Ball, arena);
        var rules = new SlopballSimulation(arena.Arcade.Slopball!, arena.Arcade.Ball, ball);
        Serve(rules, ball);
        var shot = rules.State.Shot; ulong id = ball.State.EntityId;
        ball.Reset(new(0, .35f, 2), new(0, -120, 0));
        Advance(rules, ball, arena);
        Assert.Equal(SlopballPhase.WaitingServe, rules.State.Phase);
        Assert.Equal(0UL, rules.State.Returns);
        Assert.NotEqual(shot.Velocity, rules.State.Shot.Velocity);
        Assert.Equal(shot.Index + 1, rules.State.Shot.Index);
        var next = rules.State.Shot;
        Serve(rules, ball);
        Assert.NotEqual(id, ball.State.EntityId);
        Assert.Equal(next.Velocity, ball.State.Velocity);
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
        Assert.Equal(0UL, rules.State.Returns);
        Assert.Equal(SlopballPhase.Live, rules.State.Phase);
        Assert.True(rules.State.ClearedNet);
        Assert.True(ball.State.Active);
        Assert.Contains(ball.Contacts, c => c.Position.X >= 5.699f);
        ball.Reset(new(0, .35f, 2), new(0, -120, 0));
        Advance(rules, ball, arena);
        Assert.Equal(1UL, rules.State.Returns);
        Assert.Equal(SlopballPhase.WaitingServe, rules.State.Phase);
        Assert.Single(rules.Events.Where(e => e.Kind == SlopballEventKind.Returned));
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
        Assert.Equal(SlopballPhase.WaitingServe, rules.State.Phase);
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
        Assert.Equal(SlopballPhase.WaitingServe, rules.State.Phase);
    }

    [Theory]
    [InlineData(ActionState.Idle)]
    [InlineData(ActionState.Shielding)]
    [InlineData(ActionState.AirDodgeMovement)]
    public void BallPassesThroughFighterWithoutArmingAReturn(ActionState stance)
    {
        var (arena, ball, rules) = Fixture();
        var def = TestHelpers.EngineDef;
        var body = TestHelpers.PlayerState(); body.PY = def.CapsuleHeight * .5f; body.PZ = -2;
        body.State = stance;
        ball.Reset(new(0, 1.5f, -.7f), new(0, 0, -180));
        rules.BeginTick(ball);
        ball.Step(arena, new Dictionary<ulong, CharacterState> { [1] = body }, new Dictionary<ulong, CharacterDefinition> { [1] = def });
        rules.PlanMotion(ball); rules.FinishTick(ball);
        Assert.Equal(0UL, rules.State.Returns);
        Assert.True(rules.State.Received);
        Assert.False(rules.State.ReturnArmed);
        Assert.False(rules.State.ClearedNet);
        Assert.Equal(SlopballPhase.Live, rules.State.Phase);
        Assert.True(ball.State.Position.Z < body.PZ - def.CapsuleRadius);
        Assert.Equal(-180f, ball.State.Velocity.Z);
        Assert.DoesNotContain(ball.Contacts, c => c.Body);
        Assert.DoesNotContain(rules.Events, e => e.Kind == SlopballEventKind.Redirected);
        ball.Reset(new(0, .35f, 2), new(0, -120, 0));
        Advance(rules, ball, arena);
        Assert.Equal(0UL, rules.State.Returns);
        Assert.Contains(rules.Events, e => e.Kind == SlopballEventKind.ReServe);
        Assert.Equal(0f, ball.State.DamagePercent);
        Assert.Equal(0, body.DamagePercent);
    }

    [Fact]
    public void AttackInsideFighterBodyCanHitAnIncomingBall()
    {
        var (arena, ball, rules) = Fixture();
        var def = TestHelpers.EngineDef;
        var body = TestHelpers.PlayerState() with { PY = def.CapsuleHeight * .5f, PZ = -2 };
        ball.Reset(new(0, .75f, -.7f), new(0, 0, -180));
        rules.BeginTick(ball);
        ball.Step(arena, new Dictionary<ulong, CharacterState> { [1] = body }, new Dictionary<ulong, CharacterDefinition> { [1] = def });
        rules.PlanMotion(ball);
        var resolver = new SpellResolver();
        resolver.Spawn(new Hitbox
        {
            OwnerId = 1, X = 0, Y = .75f, Z = -2, Radius = .05f,
            Shape = HitboxShape.Sphere, Damage = 3, DurationTicks = 2,
        });
        var hit = Assert.Single(resolver.Tick(new() { ball.CollisionTarget }, orderMovingTargetContacts: true));
        Assert.Equal(ball.State.EntityId, hit.TargetEntityId);
        Assert.True(rules.BeforeAttack(ball, hit.ContactTime));
        Assert.True(rules.State.ReturnArmed);
        Assert.Contains(rules.Events, e => e.Kind == SlopballEventKind.Redirected);
    }

    [Fact]
    public void ClearedBallCanReturnToPlayerHalfAndItsFloorResetsTheCombo()
    {
        var (arena, ball, rules) = Fixture();
        ReceiveAndArm(arena, ball, rules);
        ball.Reset(new(0, 3, -1), new(0, 0, 120));
        Advance(rules, ball, arena);
        Assert.True(rules.State.ClearedNet);
        Assert.Equal(0UL, rules.State.Returns);
        Assert.Equal(SlopballPhase.Live, rules.State.Phase);
        // A sender-wall bank can cross back. Clearing is not a completed return.
        ball.Reset(new(0, .35f, -2), new(0, -120, 0));
        Advance(rules, ball, arena);
        Assert.Equal(SlopballPhase.WaitingServe, rules.State.Phase);
        Assert.Equal(0UL, rules.State.Returns);
    }

    [Fact]
    public void ProjectileCanHitAfterClearButNotAfterSenderFloor()
    {
        var (arena, ball, rules) = Fixture();
        ReceiveAndArm(arena, ball, rules);
        ball.Reset(new(0, 3, -1), new(0, 0, 120));
        rules.BeginTick(ball);
        ball.Step(arena, new Dictionary<ulong, CharacterState>(), new Dictionary<ulong, CharacterDefinition>());
        rules.PlanMotion(ball);
        Assert.True(rules.BeforeAttack(ball, .95f));
        Assert.True(rules.State.ClearedNet);
        Assert.True(ball.State.Active);
        rules.FinishTick(ball);
        ball.Reset(new(0, .35f, 2), new(0, -120, 240));
        rules.BeginTick(ball);
        ball.Step(arena, new Dictionary<ulong, CharacterState>(), new Dictionary<ulong, CharacterDefinition>());
        rules.PlanMotion(ball);
        var resolver = new SpellResolver();
        resolver.Spawn(new Hitbox { OwnerId = 1, X = 0, Y = 1.2f, Z = 3, Radius = .2f, Shape = HitboxShape.Sphere, Damage = 10, DurationTicks = 30 });
        Assert.Empty(resolver.Tick(new() { ball.CollisionTarget }, orderMovingTargetContacts: true));
        Assert.Single(resolver.GetActiveHitboxes());
        rules.FinishTick(ball);
        Assert.Equal(1UL, rules.State.Returns);
        Serve(rules, ball);
        Assert.False(rules.State.ClearedNet);
        Assert.False(rules.State.ReturnArmed);
        Assert.False(rules.State.Received);
        Assert.Equal(0f, ball.State.DamagePercent);
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

    [Theory]
    [InlineData(false, 0f, .1f, 10f, false)]
    [InlineData(true, 0f, .1f, 10f, false)]
    [InlineData(false, 0f, -.1f, 10f, true)]
    [InlineData(true, 0f, -.1f, 10f, true)]
    [InlineData(false, .1f, 0f, 10f, false)]
    [InlineData(true, .1f, 0f, 10f, false)]
    [InlineData(false, 0f, -.1f, -45f, false)]
    [InlineData(true, 0f, -.1f, -45f, false)]
    public void WeakHitsKeepTheirDirectionAndAngle(bool coop, float offsetX, float offsetZ, float angle, bool scores)
    {
        var (sim, ball) = ReceivedBallSimulation(coop);
        // Close enough for the ball and fighter body to overlap: the attack must still reach it.
        sim.SetState(1, sim.GetState(1) with
        { PX = ball.Position.X + offsetX, PY = ball.Position.Y, PZ = ball.Position.Z + offsetZ });
        sim.Resolver.Spawn(new Hitbox
        {
            OwnerId = 1, AttackSlot = 1, ActivationId = 3, X = ball.Position.X, Y = ball.Position.Y, Z = ball.Position.Z,
            EndX = ball.Position.X, EndY = ball.Position.Y, EndZ = ball.Position.Z, Radius = .45f, Shape = HitboxShape.Sphere,
            Damage = 3f, BaseKnockback = 2f, KnockbackGrowth = 2f, KnockbackAngle = (sbyte)angle, DurationTicks = 2,
        });
        sim.Tick(new());
        var hit = Assert.Single(sim.LastTickHits, x => x.TargetEntityId == ball.EntityId);
        var launched = sim.ArcadeBall!.Value;
        var velocity = launched.Velocity;
        float horizontal = MathF.Sqrt(velocity.X * velocity.X + velocity.Z * velocity.Z);
        float speed = MathF.Sqrt(horizontal * horizontal + velocity.Y * velocity.Y);
        Assert.True(hit.ImpactForce < 3f);
        Assert.Equal(hit.ImpactForce, speed, 4);
        Assert.Equal(angle, MathF.Atan2(velocity.Y, horizontal) * 180f / MathF.PI, 3);
        if (offsetX != 0f) Assert.Equal(-MathF.Sign(offsetX), MathF.Sign(velocity.X));
        if (offsetZ != 0f) Assert.Equal(-MathF.Sign(offsetZ), MathF.Sign(velocity.Z));
        Assert.Equal(3f, launched.DamagePercent);
        Assert.True(launched.HitstopTicks > 0);
        Assert.True(sim.Slopball!.Value.ReturnArmed);
        Assert.DoesNotContain(sim.LastTickArcadeBallContacts, c => c.Body);
        bool landed = false;
        var expected = scores ? SlopballEventKind.Returned : SlopballEventKind.Missed;
        var unexpected = scores ? SlopballEventKind.Missed : SlopballEventKind.Returned;
        for (int i = 0; i < 400 && !landed; i++)
        {
            sim.Tick(new());
            landed = sim.LastTickSlopballEvents.Any(e => e.Kind == expected);
            Assert.DoesNotContain(sim.LastTickSlopballEvents, e => e.Kind == unexpected);
        }
        Assert.True(landed);
        Assert.Equal(scores ? 1UL : 0UL, sim.Slopball!.Value.Returns);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BackwardHitCanBankOffTheBackWallAndScore(bool coop)
    {
        var (sim, ball) = ReceivedBallSimulation(coop);
        sim.SetState(1, sim.GetState(1) with
        { PX = ball.Position.X, PY = ball.Position.Y, PZ = ball.Position.Z + .1f });
        sim.Resolver.Spawn(new Hitbox
        {
            OwnerId = 1, AttackSlot = 1, ActivationId = 3, X = ball.Position.X, Y = ball.Position.Y, Z = ball.Position.Z,
            Radius = .45f, Shape = HitboxShape.Sphere, Damage = 3f, BaseKnockback = 40f, KnockbackAngle = 0, DurationTicks = 2,
        });
        sim.Tick(new());
        Assert.Single(sim.LastTickHits, x => x.TargetEntityId == ball.EntityId);
        Assert.True(sim.ArcadeBall!.Value.Velocity.Z < 0f);
        Assert.Equal(0f, sim.ArcadeBall.Value.Velocity.Y);
        bool banked = false, returned = false;
        for (int i = 0; i < 400 && !returned; i++)
        {
            sim.Tick(new());
            banked |= sim.LastTickArcadeBallContacts.Any(c => c.Position.Z < -5f && c.NormalZ > .5f);
            returned = sim.LastTickSlopballEvents.Any(e => e.Kind == SlopballEventKind.Returned);
            Assert.DoesNotContain(sim.LastTickSlopballEvents, e => e.Kind == SlopballEventKind.Missed);
        }
        Assert.True(banked);
        Assert.True(returned);
        Assert.Equal(1UL, sim.Slopball!.Value.Returns);
    }

    [Fact]
    public void RallyHeatMixesInZingersMoonballsAndCurveballsByLevel()
    {
        var stage = Court().Arcade!; var rules = stage.Slopball!;
        Assert.Equal(0, SlopballDefinition.HeatOf(4)); Assert.Equal(1, SlopballDefinition.HeatOf(5));
        Assert.Equal(3, SlopballDefinition.HeatOf(17)); Assert.Equal(4, SlopballDefinition.HeatOf(999));
        foreach (ulong returns in new ulong[] { 0, 4, 5, 10, 15, 20 })
        {
            var kinds = new HashSet<SlopballServe>();
            for (ulong i = 0; i < 400; i++) kinds.Add(rules.CreateShot(stage.Ball, i, returns, 77).Kind);
            int heat = SlopballDefinition.HeatOf(returns);
            Assert.Contains(SlopballServe.Lob, kinds);
            Assert.Equal(heat >= 1, kinds.Contains(SlopballServe.Zinger));
            Assert.Equal(heat >= 2, kinds.Contains(SlopballServe.Moonball));
            Assert.Equal(heat >= 3, kinds.Contains(SlopballServe.Curveball));
        }
        // The same draw with a special serve forced keeps its landing point and only changes the flight.
        var lob = rules.CreateShot(stage.Ball, 3, 0, 77);
        var zinger = rules.CreateShot(stage.Ball, 3, 0, 77, kind: SlopballServe.Zinger);
        var moon = rules.CreateShot(stage.Ball, 3, 0, 77, kind: SlopballServe.Moonball);
        Assert.Equal(SlopballServe.Lob, lob.Kind);
        Assert.True(zinger.Kind == SlopballServe.Lob || zinger.FlightTicks < lob.FlightTicks);
        Assert.True(moon.FlightTicks > lob.FlightTicks && moon.Velocity.Y > lob.Velocity.Y);
    }

    [Fact]
    public void SpecialServesClearTheNetLandInTheReceivingAreaAndStayUnderALowCeiling()
    {
        var arena = Court(); var profile = arena.Arcade!; var rules = profile.Slopball!;
        float ceiling = rules.CeilingOf(arena);
        Assert.Equal(6f, ceiling);
        rules.ValidateCourt(arena);
        foreach (var kind in new[] { SlopballServe.Zinger, SlopballServe.Moonball, SlopballServe.Curveball })
        for (ulong i = 0; i < 64; i++)
        {
            var shot = rules.CreateShot(profile.Ball, i, 20, 91, ceiling, kind);
            var ball = new ArcadeBallSimulation(profile.Ball, arena); ball.Reset(shot.Spawn, shot.Velocity, shot.Curve);
            float apex = 0f;
            bool crossed = false, landed = false;
            for (int tick = 0; tick < 400 && !landed; tick++)
            {
                var before = ball.State.Position;
                ball.Step(arena, new Dictionary<ulong, CharacterState>(), new Dictionary<ulong, CharacterDefinition>());
                var after = ball.State.Position;
                apex = MathF.Max(apex, after.Y);
                if (before.Z > rules.NetZ && after.Z <= rules.NetZ) crossed = true;
                Assert.DoesNotContain(ball.Contacts, c => c.NormalY < -.5f);
                foreach (var contact in ball.Contacts)
                    if (contact.NormalY > .5f)
                    {
                        Assert.InRange(contact.Position.X, -rules.LandingHalfWidth - .01f, rules.LandingHalfWidth + .01f);
                        Assert.InRange(contact.Position.Z, rules.LandingMinZ - .01f, rules.LandingMaxZ + .01f);
                        landed = true;
                    }
            }
            Assert.True(crossed && landed);
            Assert.True(apex + profile.Ball.Radius < ceiling);
            if (shot.Kind == SlopballServe.Curveball) Assert.NotEqual(0f, shot.Curve);
        }
    }

    [Fact]
    public void CurveballBendsSidewaysAndStillLandsOnItsLobsSpot()
    {
        var arena = VariedCourt(); var profile = arena.Arcade!; var rules = profile.Slopball!;
        float ceiling = rules.CeilingOf(arena);
        var lob = rules.CreateShot(profile.Ball, 9, 0, 5, ceiling);
        var curve = rules.CreateShot(profile.Ball, 9, 0, 5, ceiling, SlopballServe.Curveball);
        Assert.Equal(SlopballServe.Curveball, curve.Kind);
        ArcadePoint Land(SlopballShot shot, out float bend)
        {
            var ball = new ArcadeBallSimulation(profile.Ball, arena); ball.Reset(shot.Spawn, shot.Velocity, shot.Curve);
            bend = 0f;
            for (int tick = 0; tick < 600; tick++)
            {
                ball.Step(arena, new Dictionary<ulong, CharacterState>(), new Dictionary<ulong, CharacterDefinition>());
                bend = MathF.Max(bend, MathF.Abs(ball.State.Curve));
                foreach (var contact in ball.Contacts) if (contact.NormalY > .5f) return contact.Position;
            }
            throw new InvalidOperationException("The serve never landed.");
        }
        var lobLanding = Land(lob, out _);
        var curveLanding = Land(curve, out float pull);
        Assert.Equal(lobLanding.X, curveLanding.X, 1);
        Assert.Equal(lobLanding.Z, curveLanding.Z, 1);
        Assert.InRange(MathF.Abs(curve.Curve), .5f, 4.5f);
        Assert.Equal(MathF.Abs(curve.Curve), pull);
        // It leaves sideways against its bend, then the pull brings it back onto the lob's spot.
        Assert.True(MathF.Sign(curve.Velocity.X - lob.Velocity.X) == -MathF.Sign(curve.Curve));
    }

    [Fact]
    public void HeatRisesEveryFiveReturnsAndAMissCoolsIt()
    {
        var (arena, ball, rules) = Fixture();
        for (int i = 0; i < 5; i++)
        {
            ReceiveAndArm(arena, ball, rules);
            ball.Reset(new(0, 3, -1), new(0, 0, 120)); Advance(rules, ball, arena);
            ball.Reset(new(0, .35f, 2), new(0, -120, 0)); Advance(rules, ball, arena);
            Assert.Equal(i == 4 ? 1 : 0, rules.State.Heat);
            Serve(rules, ball);
        }
        Assert.Equal(5UL, rules.State.Returns);
        Assert.Equal(1, rules.State.Heat);
        ball.Reset(new(0, .35f, -2), new(0, -120, 0)); Advance(rules, ball, arena);
        Assert.Equal(0UL, rules.State.Returns);
        Assert.Equal(0, rules.State.Heat);
        Assert.Equal(SlopballServe.Lob, rules.State.Shot.Kind);
    }

    internal static void ReceiveAndArm(ArenaDefinition arena, ArcadeBallSimulation ball, SlopballSimulation rules)
    {
        ball.Reset(new(0, 3, -1), default);
        rules.BeginTick(ball); ball.Step(arena, new Dictionary<ulong, CharacterState>(), new Dictionary<ulong, CharacterDefinition>()); rules.PlanMotion(ball);
        Assert.True(rules.BeforeAttack(ball, .5f)); rules.FinishTick(ball);
    }
    private static (ServerSimulation Simulation, ArcadeBallState Ball) ReceivedBallSimulation(bool coop)
    {
        var sim = new ServerSimulation(Court(), coop ? new SlopballCoopMatchRule() : NoWinMatchRule.Instance);
        var def = TestHelpers.EngineDef;
        var state = TestHelpers.PlayerState() with { PX = 5, PZ = -5, PY = def.CapsuleHeight * .5f };
        sim.RegisterEntity(1, def, state);
        if (coop)
        {
            sim.RegisterEntity(2, def, state with { EntityId = 2, PX = -5 });
            sim.BeginSlopballRun(1337);
        }
        for (int i = 0; i < 400; i++)
        {
            sim.Tick(new());
            var ball = sim.ArcadeBall!.Value;
            if (ball.Active && ball.Position.Z < -1.5f && ball.Velocity.Y < 0) return (sim, ball);
        }
        throw new InvalidOperationException("The serve never reached the player half while descending.");
    }
    private static (ArenaDefinition Arena, ArcadeBallSimulation Ball, SlopballSimulation Rules) Fixture()
    {
        var arena = Court(); var ball = new ArcadeBallSimulation(arena.Arcade!.Ball, arena);
        var rules = new SlopballSimulation(arena.Arcade.Slopball!, arena.Arcade.Ball, ball); Serve(rules, ball);
        return (arena, ball, rules);
    }
    internal static void Serve(SlopballSimulation rules, ArcadeBallSimulation ball)
    { while (rules.State.Phase == SlopballPhase.WaitingServe) rules.BeginTick(ball); }
    internal static void Advance(SlopballSimulation rules, ArcadeBallSimulation ball, ArenaDefinition arena)
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
