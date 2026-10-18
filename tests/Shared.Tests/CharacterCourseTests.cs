using System.Buffers.Binary;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class CharacterCourseTests
{
    [Fact]
    public void CourseRoundTripsAsOneArenaAndRejectsEveryTruncation()
    {
        var arena = Arena();
        byte[] bytes = ArenaBinaryFormat.Serialize(arena);
        var loaded = ArenaBinaryFormat.Deserialize(bytes)!.Value;
        Assert.Null(loaded.Arcade!.Ball);
        Assert.Equal("first", loaded.Arcade.Course!.Targets[0].Id);
        Assert.Equal((ushort)2, loaded.Arcade.Course.Checkpoints[1].RequiredTargetCount);
        Assert.Equal(bytes, ArenaBinaryFormat.Serialize(loaded));
        byte[] section = ArcadeStageBinaryFormat.Serialize(arena.Arcade!);
        Assert.Equal(5, BinaryPrimitives.ReadUInt16LittleEndian(section));
        for (int length = 0; length < section.Length; length++)
            Assert.Null(ArcadeStageBinaryFormat.Deserialize(section[..length]));
        Assert.Null(ArcadeStageBinaryFormat.Deserialize(section.Concat(new byte[] { 0 }).ToArray()));
    }

    [Fact]
    public void InvalidTargetsVolumesAndContradictoryCheckpointPrefixesAreRejected()
    {
        var c = Arena().Arcade!.Course!;
        Assert.Throws<ArgumentException>(() => new CourseTargetDefinition("bad", new(float.NaN, 0, 0), 1));
        Assert.Throws<ArgumentException>(() => new ArcadeVolume(default, new(1, 0, 1)));
        Assert.Throws<ArgumentException>(() => new CharacterCourseDefinition(new[] { c.Targets[0], c.Targets[0] }, c.Checkpoints, c.Finish));
        Assert.Throws<ArgumentException>(() => new CharacterCourseDefinition(c.Targets, c.Checkpoints.Reverse().ToArray(), c.Finish));
        Assert.Throws<ArgumentException>(() => new CharacterCourseDefinition(c.Targets,
            new[] { new CourseCheckpointDefinition("bad", c.Finish, default, 0, 3) }, c.Finish));
        var arena = Arena();
        arena.Arcade = ArcadeStageDefinition.ForCourse("bad", 1, new[] { "fightguy" }, new(
            new[] { new CourseTargetDefinition("outside", new(10000, 1, 0), .5f) }, Array.Empty<CourseCheckpointDefinition>(), c.Finish));
        Assert.Throws<ArgumentException>(() => ArenaBinaryFormat.Serialize(arena));
    }

    [Fact]
    public void CountdownFreezesFighterClockAndProjectilesAndRequiresHeldInputRelease()
    {
        var sim = Create(begin: false);
        var initial = sim.GetState(1); initial.SetCooldown(AbilitySlots.E, 81); sim.SetState(1, initial);
        sim.BeginCourseAttempt(180);
        var held = new Dictionary<ulong, InputState> { [1] = new() { MoveX = 1, ActiveSlot = AbilitySlots.A, Jump = true, JumpHeld = true } };
        for (int i = 0; i < 180; i++) sim.Tick(held);
        Assert.Equal(initial, sim.GetState(1));
        Assert.Equal(0ul, sim.Course!.Value.ElapsedTicks);
        Assert.Equal(0u, sim.SimulationTick);
        Assert.Equal(CharacterCoursePhase.Running, sim.Course.Value.Phase);
        for (int i = 0; i < 3; i++) sim.Tick(held);
        Assert.Equal(initial.PX, sim.GetState(1).PX);
        Assert.Equal(0, sim.GetState(1).ChargeTicks);
        Assert.Equal(3ul, sim.Course.Value.ElapsedTicks);
        sim.Tick(new());
        sim.Tick(new() { [1] = new() { MoveX = 1 } });
        Assert.True(sim.GetState(1).VX > 0);
        Assert.Throws<InvalidOperationException>(() => sim.BeginCourseAttempt(180));
        Assert.Throws<ArgumentException>(() => sim.RegisterEntity(2, TestHelpers.EngineDef, default));
    }

    [Fact]
    public void OnlyDamagingContactsBreakNonblockingTargetsAndOtherProjectilesAdvanceOnce()
    {
        var sim = Create();
        var zero = Attack(0); zero.Damage = 0; sim.Resolver.Spawn(zero);
        sim.Tick(new()); Assert.False(sim.IsCourseTargetBroken(0));
        sim.Resolver.Clear();
        var unrelated = Attack(0); unrelated.X = 20; unrelated.VX = 9;
        var damaging = Attack(0); damaging.FreezesOwner = true;
        sim.Resolver.Spawn(damaging); sim.Resolver.Spawn(unrelated);
        sim.Tick(new());
        Assert.True(sim.IsCourseTargetBroken(0));
        Assert.Equal(1, sim.Course!.Value.BrokenTargets);
        Assert.Single(sim.LastTickHits);
        Assert.True(sim.GetState(1).HitstopTicks > 0);
        var remaining = Assert.Single(sim.Resolver.GetActiveHitboxes());
        Assert.Equal(20 + 9 * Simulation.TickDt, remaining.X, 4);
        Assert.Equal(1, remaining.AgeTicks);
        Assert.Single(sim.GetAllStates());
        sim.Resolver.Spawn(Attack(0)); sim.Tick(new()); Assert.Empty(sim.LastTickHits);
        Assert.DoesNotContain(sim.GetLastEntityData(), x => x.Id == CharacterCourseSimulation.FirstEntityId);
    }

    [Fact]
    public void CheckpointsRequireCumulativeTargetsButVisitsCanBeSkippedAndNeverRegress()
    {
        var sim = Create();
        Move(sim, 8); sim.Tick(new()); Assert.Equal(-1, sim.Course!.Value.SavedCheckpoint);
        Break(sim, 1); // Later target does not satisfy the missing first target.
        Move(sim, 8); sim.Tick(new()); Assert.Equal(-1, sim.Course.Value.SavedCheckpoint);
        Break(sim, 0); Move(sim, 8); sim.Tick(new());
        Assert.Equal(1, sim.Course.Value.SavedCheckpoint);
        Move(sim, 4); sim.Tick(new()); Assert.Equal(1, sim.Course.Value.SavedCheckpoint);
        var state = sim.GetState(1); state.PY = -200; state.PX = 10000; state.IsGrounded = false;
        state.SetCooldown(AbilitySlots.E, 123); sim.SetState(1, state);
        ulong before = sim.Course.Value.ElapsedTicks;
        sim.Tick(new());
        Assert.Equal(8, sim.GetState(1).PX);
        Assert.Equal(0, sim.GetState(1).GetCooldown(AbilitySlots.E));
        Assert.Equal(2, sim.Course.Value.BrokenTargets);
        Assert.Equal(before + 1, sim.Course.Value.ElapsedTicks);
        Assert.Equal((byte)1, sim.GetState(1).Deaths);
        Assert.Equal(CharacterCourseEventKind.Respawned, Assert.Single(sim.LastTickCourseEvents).Kind);
    }

    [Fact]
    public void FallingBeforeACheckpointUsesInitialSpawnAndDoesNotSweepSensorsOnTeleport()
    {
        var sim = Create(); Break(sim, 0);
        var state = sim.GetState(1); state.PX = 10000; state.PY = -200; state.PX = 10000; state.IsGrounded = false; sim.SetState(1, state);
        sim.Tick(new());
        Assert.Equal(0, sim.GetState(1).PX);
        Assert.Equal(-1, sim.Course!.Value.SavedCheckpoint);
        Assert.True(sim.IsCourseTargetBroken(0));
    }

    [Fact]
    public void FinishRequiresEveryTargetAndFreezesTheCompletedResultUntilFullRetry()
    {
        var sim = Create(); Move(sim, 12); sim.Tick(new());
        Assert.Equal(CharacterCoursePhase.Running, sim.Course!.Value.Phase);
        Break(sim, 0); Break(sim, 1); Move(sim, 12); sim.Tick(new());
        var result = sim.Course.Value; var state = sim.GetState(1); uint tick = sim.SimulationTick;
        Assert.Equal(CharacterCoursePhase.Completed, result.Phase);
        Assert.True(result.ElapsedTicks > 0);
        sim.StopCourse();
        for (int i = 0; i < 30; i++) sim.Tick(new() { [1] = new() { MoveX = 1, ActiveSlot = 1 } });
        Assert.Equal(result, sim.Course.Value); Assert.Equal(state, sim.GetState(1)); Assert.Equal(tick, sim.SimulationTick);
        var retry = Create(begin: false);
        Assert.Equal(0, retry.Course!.Value.BrokenTargets); Assert.Equal(-1, retry.Course.Value.SavedCheckpoint);
        Assert.Equal(0ul, retry.Course.Value.ElapsedTicks); Assert.Empty(retry.Resolver.GetActiveHitboxes());
    }

    [Fact]
    public void FastSweepsCannotSkipCheckpointOrFinishSensors()
    {
        var course = new CharacterCourseSimulation(Arena().Arcade!.Course!);
        course.Begin(1); course.BeginTick(); course.BeginTick(); course.Hit(0); course.Hit(1);
        course.FinishTick(new(-5, 1, 0), new(20, 1, 0), false);
        Assert.Equal(1, course.State.SavedCheckpoint);
        Assert.Equal(CharacterCoursePhase.Completed, course.State.Phase);
    }

    [Fact]
    public void TargetAliasesSupportNormalLockAndNeverRetargetABrokenObject()
    {
        var sim = Create();
        sim.Tick(new() { [1] = new() { ToggleLock = true, TargetEntityId = sim.CourseTargetInputAlias(0) } });
        var owner = sim.GetState(1); Assert.True(owner.LockOn);
        Assert.Equal(CharacterCourseSimulation.FirstEntityId, owner.TargetEntityId);
        Assert.True(sim.TryGetCombatTargetPosition(owner.TargetEntityId, out _));
        Break(sim, 0);
        Assert.False(sim.TryGetCombatTargetPosition(owner.TargetEntityId, out _));
        sim.Tick(new() { [1] = new() { TargetEntityId = sim.CourseTargetInputAlias(0) } });
        Assert.NotEqual(owner.TargetEntityId, sim.GetState(1).TargetEntityId);
        Assert.Throws<ArgumentException>(() => sim.RegisterEntity(128, TestHelpers.EngineDef, default));
    }

    [Fact]
    public void CourseRecordsRequireCompletionKeepTheFasterTimeAndArchiveMapChanges()
    {
        var book = new ArcadeRecordBook(); var key = new ArcadeRecordKey("character-course", "course", "fightguy", new string('a', 64));
        var provenance = new ArcadeRecordProvenance("1", "cooked", "package", "stage");
        CharacterCourseState Result(ulong ticks) => new(CharacterCoursePhase.Completed, ticks, 2, 2, -1, 0);
        Assert.Throws<ArgumentException>(() => book.BankCourse(key, default, provenance, DateTime.UtcNow));
        book.BankCourse(key, Result(6000), provenance, DateTime.UtcNow);
        Assert.Equal(6000ul, book.BankCourse(key, Result(7000), provenance, DateTime.UtcNow).PersonalBest);
        Assert.Equal(5000ul, book.BankCourse(key, Result(5000), provenance, DateTime.UtcNow).PersonalBest);
        var newer = key with { GameplayHash = new string('b', 64) };
        book.BankCourse(newer, Result(8000), provenance, DateTime.UtcNow);
        book = ArcadeRecordBook.Deserialize(book.Serialize());
        Assert.Equal(5000ul, book.Best(key)); Assert.True(book.Records.Single(x => x.Key == key).Archived);
        book.BankCourse(key, Result(9000), provenance, DateTime.UtcNow);
        Assert.False(book.Records.Single(x => x.Key == key).Archived);
    }

    [Fact]
    public void CourseFingerprintIncludesMarkersAndRulesButExcludesPresentation()
    {
        var arena = Arena(); var entry = BuiltInContentResolver.Resolve(CharacterClass.FightGuy);
        string hash = ArcadeRecordCompatibility.Course(arena, entry);
        arena.DisplayName = "Cosmetic name"; arena.PreviewColor = "#ffffff";
        Assert.Equal(hash, ArcadeRecordCompatibility.Course(arena, entry));
        Assert.NotEqual(hash, ArcadeRecordCompatibility.Course(arena, entry,
            rulesRevision: ArcadeRecordCompatibility.CourseRulesRevision + 1));
        var c = arena.Arcade!.Course!;
        arena.Arcade = ArcadeStageDefinition.ForCourse("fixture", 1, new[] { "fightguy" }, new CharacterCourseDefinition(
            new[] { new CourseTargetDefinition("first", new(3, 1, 0), .5f), c.Targets[1] }, c.Checkpoints, c.Finish));
        Assert.NotEqual(hash, ArcadeRecordCompatibility.Course(arena, entry));
        Assert.Throws<ArgumentException>(() => ArcadeRecordCompatibility.Course(arena, BuiltInContentResolver.Resolve(CharacterClass.Manki)));
    }

    [Fact]
    public void BonusTargetsTakeTheirHitCountKeepTheLockAndGateCheckpointsUntilBroken()
    {
        var sim = new ServerSimulation(BonusArena(hits: 3), NoWinMatchRule.Instance);
        sim.RegisterEntity(1, TestHelpers.EngineDef, TestHelpers.PlayerState());
        sim.SetRespawnPosition(1, 0, .85f, 0);
        sim.BeginCourseAttempt(1); sim.Tick(new()); sim.Tick(new());
        sim.Tick(new() { [1] = new() { ToggleLock = true, TargetEntityId = sim.CourseTargetInputAlias(0) } });
        Assert.Equal(3, sim.CourseTargetHitsLeft(0));
        for (int hit = 1; hit <= 2; hit++)
        {
            Break(sim, 0);
            Assert.False(sim.IsCourseTargetBroken(0));
            Assert.Equal(3 - hit, sim.CourseTargetHitsLeft(0));
            var e = Assert.Single(sim.LastTickCourseEvents);
            Assert.Equal((CharacterCourseEventKind.TargetHit, 0), (e.Kind, e.Index));
            Assert.Single(sim.LastTickHits);
            Assert.True(sim.GetState(1).LockOn);
            Move(sim, 4); sim.Tick(new());
            Assert.Equal(-1, sim.Course!.Value.SavedCheckpoint);
            Move(sim, 0); sim.Tick(new());
        }
        Assert.Equal(0, sim.Course!.Value.BrokenTargets);
        Break(sim, 0);
        Assert.True(sim.IsCourseTargetBroken(0));
        Assert.Equal(0, sim.CourseTargetHitsLeft(0));
        Assert.Equal(CharacterCourseEventKind.TargetBroken, Assert.Single(sim.LastTickCourseEvents).Kind);
        Assert.Equal(1, sim.Course.Value.BrokenTargets);
        Assert.False(sim.GetState(1).LockOn);
        Break(sim, 0); Assert.Empty(sim.LastTickHits);
        Move(sim, 4); sim.Tick(new());
        Assert.Equal(0, sim.Course.Value.SavedCheckpoint);
    }

    [Fact]
    public void BonusTargetCoursesWriteSectionSixWhileSingleHitCoursesKeepFive()
    {
        var single = Arena();
        Assert.False(single.Arcade!.Course!.HasMultiHitTargets);
        Assert.Equal(5, BinaryPrimitives.ReadUInt16LittleEndian(ArcadeStageBinaryFormat.Serialize(single.Arcade)));
        var bonus = BonusArena(hits: 8);
        byte[] bytes = ArenaBinaryFormat.Serialize(bonus);
        var loaded = ArenaBinaryFormat.Deserialize(bytes)!.Value;
        Assert.Equal(new byte[] { 8, 1 }, loaded.Arcade!.Course!.Targets.Select(t => t.Hits).ToArray());
        Assert.Equal(bytes, ArenaBinaryFormat.Serialize(loaded));
        byte[] section = ArcadeStageBinaryFormat.Serialize(bonus.Arcade!);
        Assert.Equal(6, BinaryPrimitives.ReadUInt16LittleEndian(section));
        for (int length = 0; length < section.Length; length++)
            Assert.Null(ArcadeStageBinaryFormat.Deserialize(section[..length]));
        Assert.Throws<ArgumentException>(() => new CourseTargetDefinition("none", new(0, 1, 0), .5f, 0));
        Assert.Throws<ArgumentException>(() => new CourseTargetDefinition("many", new(0, 1, 0), .5f, CourseTargetDefinition.MaxHits + 1));
    }

    [Fact]
    public void HitCountsChangeOnlyBonusCourseFingerprints()
    {
        var entry = BuiltInContentResolver.Resolve(CharacterClass.FightGuy);
        // Single-hit courses project exactly as they did before hit counts existed.
        var options = new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
        Assert.DoesNotContain("Hits", System.Text.Json.JsonSerializer.Serialize(Arena().Arcade!.Course, options));
        string eight = ArcadeRecordCompatibility.Course(BonusArena(hits: 8), entry);
        Assert.Equal(eight, ArcadeRecordCompatibility.Course(BonusArena(hits: 8), entry));
        Assert.NotEqual(eight, ArcadeRecordCompatibility.Course(BonusArena(hits: 6), entry));
        Assert.NotEqual(eight, ArcadeRecordCompatibility.Course(BonusArena(hits: 1), entry));
    }

    private static ArenaDefinition BonusArena(byte hits)
    {
        var arena = Arena(); var c = arena.Arcade!.Course!;
        arena.Arcade = ArcadeStageDefinition.ForCourse("fixture", 1, new[] { "fightguy" }, new CharacterCourseDefinition(
            new[] { new CourseTargetDefinition("bonus", c.Targets[0].Position, c.Targets[0].Radius, hits), c.Targets[1] },
            c.Checkpoints, c.Finish));
        return arena;
    }

    internal static ArenaDefinition Arena()
    {
        var arena = TestHelpers.TestArena(); arena.MinX = arena.MinZ = -20; arena.MaxX = arena.MaxZ = 20; arena.CollisionTriangles = Array.Empty<CollisionTriangle>(); arena.SpawnPoints = new[] { new SpawnPoint { Y = .85f } };
        arena.Arcade = ArcadeStageDefinition.ForCourse("fixture", 1, new[] { "fightguy" }, new CharacterCourseDefinition(
            new[] { new CourseTargetDefinition("first", new(2, 1, 0), .5f), new CourseTargetDefinition("second", new(6, 1, 0), .5f) },
            new[] { new CourseCheckpointDefinition("cp1", new(new(4, 1, 0), new(1, 2, 1)), new(4, .85f, 0), 0, 1),
                new CourseCheckpointDefinition("cp2", new(new(8, 1, 0), new(1, 2, 1)), new(8, .85f, 0), 0, 2) },
            new(new(12, 1, 0), new(1, 2, 1))));
        return arena;
    }
    private static ServerSimulation Create(bool begin = true)
    {
        var sim = new ServerSimulation(Arena(), NoWinMatchRule.Instance);
        sim.RegisterEntity(1, TestHelpers.EngineDef, TestHelpers.PlayerState());
        sim.SetRespawnPosition(1, 0, .85f, 0);
        if (begin) { sim.BeginCourseAttempt(1); sim.Tick(new()); sim.Tick(new()); }
        return sim;
    }
    private static void Move(ServerSimulation sim, float x)
    { var s = sim.GetState(1); s.PX = x; s.PZ = 0; s.VX = s.VY = s.VZ = 0; s.PY = .85f; sim.SetState(1, s); }
    private static Hitbox Attack(int index) => new()
    {
        OwnerId = 1,
        AttackSlot = 1,
        ActivationId = (ulong)(index + 10),
        X = 2 + index * 4,
        Y = 1,
        Radius = .2f,
        Shape = HitboxShape.Sphere,
        Damage = 1,
        DurationTicks = 30,
    };
    private static void Break(ServerSimulation sim, int index) { sim.Resolver.Spawn(Attack(index)); sim.Tick(new()); }
}
