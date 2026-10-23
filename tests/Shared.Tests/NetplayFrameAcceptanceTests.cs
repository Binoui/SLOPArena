using SlopArena.Shared.Rollback;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class NetplayFrameAcceptanceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RejectedEndedFramesPublishNoReadinessMetadataOrOutputs(int mode)
    {
        var f = new Fixture(mode);
        f.Server.SetTick(1);
        f.Session.IngestFrames(new[] { f.Frame(MatchState.Playing) });
        var before = f.Session.Predictor.Simulation.CaptureSnapshot();
        f.Session.ResetConnection();
        var foreign = f.NewServer(extraFighter: true);
        foreign.SetTick(10);
        f.Session.IngestFrames(new[] { f.Frame(MatchState.Ended, foreign, records: true) });

        Assert.False(f.Session.HasFrame);
        Assert.Equal(MatchState.Playing, f.Session.MatchState);
        Assert.Equal(1u, f.Session.NewestFrameTick);
        Assert.Equal(before.Data, f.Session.Predictor.Simulation.CaptureSnapshot().Data);
        Assert.Equal(1, f.Session.Predictor.TakeStats().RejectedFrames);
        f.AssertNoOutputs();

        // The identical records from an accepted roster must reach each mode's consumers.
        f.Server.SetTick(10);
        f.Session.IngestFrames(new[] { f.Frame(MatchState.Playing, records: true) });
        Assert.True(f.Session.HasFrame);
        f.AssertRecordsStarted();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidNewerFramesDoNotHideValidAnchorsAndStaleHistoryStillArrives(bool reverse)
    {
        var f = new Fixture();
        f.Server.SetTick(5);
        var valid = f.Frame(MatchState.Playing);
        var foreign = f.NewServer(extraFighter: true);
        foreign.SetTick(10);
        var invalid = f.Frame(MatchState.Ended, foreign, records: true);
        f.Session.IngestFrames(reverse ? new[] { invalid, valid } : new[] { valid, invalid });
        Assert.True(f.Session.HasFrame);
        Assert.Equal(5u, f.Session.NewestFrameTick);
        Assert.Equal(MatchState.Playing, f.Session.MatchState);
        f.AssertNoOutputs();

        // A stale foreign frame must not poison the ledger before the real history arrives.
        f.Server.SetTick(20);
        f.Session.IngestFrames(new[] { f.Frame(MatchState.Playing) });
        foreign.SetTick(4);
        f.Session.IngestFrames(new[] { f.Frame(MatchState.Ended, foreign, records: true) });
        f.AssertNoOutputs();
        f.Server.SetTick(4);
        f.Session.IngestFrames(new[] { f.Frame(MatchState.Playing, records: true) });
        f.AssertRecordsStarted();
        Assert.Equal(20u, f.Session.NewestFrameTick);
        Assert.Equal(MatchState.Playing, f.Session.MatchState);
        Assert.Equal(2, f.Session.Predictor.TakeStats().RejectedFrames);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void MalformedProgrammaticFramesAreRejectedIndividuallyIncludingStaleOnes(int fault)
    {
        var f = new Fixture();
        f.Server.SetTick(20);
        f.Session.IngestFrames(new[] { f.Frame(MatchState.Playing) });
        f.Session.ResetConnection();
        f.Server.SetTick(4);
        var snapshot = f.Server.CaptureSnapshot();
        var events = new[] { Fixture.Event(4) };
        var hits = new[] { Fixture.Hit(4) };
        var inputs = new[] { new RelayedInput(1, true, default) };
        var state = MatchState.Ended;
        if (fault == 0) snapshot = new WorldSnapshot(snapshot.Data.Concat(new byte[] { 1 }).ToArray());
        if (fault == 1) events[0] = events[0] with { MatchTick = 5 };
        if (fault == 2) events[0] = events[0] with { Source = (PresentationEventSource)255 };
        if (fault == 3) inputs[0] = new RelayedInput(99, true, default);
        if (fault == 4) hits[0] = hits[0] with { KnockbackDirection = (AuthoringKnockbackDirection)255 };
        if (fault == 5) state = (MatchState)255;
        if (fault == 6) inputs[0] = new RelayedInput(1, true, new InputState { LockMode = (TargetLockMode)255 });
        var malformed = new WorldFramePacket(state, snapshot, inputs, events, hits);
        f.Session.IngestFrames(new[] { malformed });
        Assert.False(f.Session.HasFrame);
        Assert.Equal(MatchState.Playing, f.Session.MatchState);
        Assert.Equal(20u, f.Session.LocalTick);
        Assert.Equal(1, f.Session.Predictor.TakeStats().RejectedFrames);
        f.AssertNoOutputs();

        f.Session.IngestFrames(new[] { malformed, f.Frame(MatchState.Playing, records: true), malformed });
        f.AssertRecordsStarted();
        Assert.True(f.Session.HasFrame);
        Assert.Equal(20u, f.Session.NewestFrameTick);
        Assert.Equal(2, f.Session.Predictor.TakeStats().RejectedFrames);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RelayedLockModeIsValidatedOnlyWhenTheInputIsPresent(bool hasInput)
    {
        var f = new Fixture();
        f.Server.SetTick(1);
        f.Session.IngestFrames(new[] { f.Frame(MatchState.Playing) });
        var before = f.Session.Predictor.Simulation.CaptureSnapshot();
        f.Session.ResetConnection();
        f.Server.SetTick(5);
        var snapshot = f.Server.CaptureSnapshot();
        var records = f.Frame(MatchState.Ended, records: true);
        var frame = new WorldFramePacket(MatchState.Ended, snapshot,
            new[] { new RelayedInput(1, hasInput, new InputState { LockMode = (TargetLockMode)255 }) },
            records.Events, records.Hits);

        f.Session.IngestFrames(new[] { frame });

        Assert.Equal(!hasInput, f.Session.HasFrame);
        Assert.Equal(hasInput ? MatchState.Playing : MatchState.Ended, f.Session.MatchState);
        Assert.Equal(hasInput ? 1u : 5u, f.Session.NewestFrameTick);
        Assert.Equal(hasInput ? before.Data : snapshot.Data, f.Session.Predictor.Simulation.CaptureSnapshot().Data);
        Assert.Equal(hasInput ? 1 : 0, f.Session.Predictor.TakeStats().RejectedFrames);
        if (hasInput) f.AssertNoOutputs();
        else f.AssertRecordsStarted();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FinishRestoresHistoricalWorldAndRetractsSpeculativeCombatExactlyOnce(bool corrected)
    {
        var arena = TestHelpers.TestArena();
        var content = BuiltInContentResolver.Resolve(CharacterClass.FightGuy);
        var server = new ServerSimulation(arena);
        var session = new NetplaySession(arena, 1, new StockMatchRule(3));
        foreach (ulong id in new ulong[] { 1, 2 })
        {
            var state = TestHelpers.PlayerState() with
            {
                EntityId = id, PX = id == 1 ? 0 : 1, PY = TestHelpers.GroundPY(content.Definition),
                FacingYaw = id == 1 ? MathF.PI / 2 : -MathF.PI / 2,
            };
            server.RegisterEntity(id, content.Definition, state, content.BakedAnimation);
            session.RegisterEntity(id, content.Definition, state, content.BakedAnimation);
        }
        WorldFramePacket Frame(MatchState state) => new(state, server.CaptureSnapshot(),
            new[] { new RelayedInput(1, true, default), new RelayedInput(2, true, default) },
            Array.Empty<TimelinePresentationEvent>(), Array.Empty<SpellResolver.HitResult>());
        session.IngestFrames(new[] { Frame(MatchState.Playing) });
        for (int i = 0; i < 3; i++)
        {
            server.Tick(new());
            Assert.True(session.Predictor.Advance(default));
        }
        if (corrected)
            server.SetState(1, server.GetState(1) with { PX = -5 });
        var finish = Frame(MatchState.Ended);
        for (int i = 0; i < 30; i++)
            Assert.True(session.Predictor.Advance(i == 0
                ? new InputState { ActiveSlot = AbilitySlots.Slot1, FacingYaw = 9000, TargetEntityId = 2 }
                : default));
        var effects = new List<TimelinePresentationEvent>(); var stopped = new List<TimelinePresentationEvent>();
        var hits = new List<SpellResolver.HitResult>(); var retracted = new List<SpellResolver.HitResult>();
        session.Predictor.DrainPresentation(effects, stopped);
        session.Predictor.DrainHits(hits, retracted);
        Assert.NotEmpty(effects);
        Assert.NotEmpty(hits);
        Assert.Empty(stopped);
        Assert.Empty(retracted);
        Assert.NotEqual(finish.Snapshot.Data, session.Predictor.Simulation.CaptureSnapshot().Data);

        session.IngestFrames(new[] { finish });
        Assert.Equal(MatchState.Ended, session.MatchState);
        Assert.Equal(3u, session.LocalTick);
        Assert.Equal(3u, session.NewestFrameTick);
        Assert.Equal(0u, session.Predictor.PredictionTicks);
        Assert.Equal(finish.Snapshot.Data, session.Predictor.Simulation.CaptureSnapshot().Data);
        Assert.False(session.Predictor.TryGetRecordedInput(4, out _));
        Assert.False(session.Predictor.Advance(new InputState { Jump = true }));
        Assert.False(session.Update(new InputState { Jump = true }, 100));
        var afterEffects = new List<TimelinePresentationEvent>(); var afterHits = new List<SpellResolver.HitResult>();
        session.Predictor.DrainPresentation(afterEffects, stopped);
        session.Predictor.DrainHits(afterHits, retracted);
        Assert.Empty(afterEffects);
        Assert.Empty(afterHits);
        Assert.Equal(effects.Select(e => e.Key).OrderBy(e => e.MatchTick), stopped.Select(e => e.Key).OrderBy(e => e.MatchTick));
        Assert.Equal(hits.Select(HitKey.Of).OrderBy(e => e.MatchTick), retracted.Select(HitKey.Of).OrderBy(e => e.MatchTick));

        server.SetTick(50);
        session.IngestFrames(new[] { finish, Frame(MatchState.Playing) });
        stopped.Clear(); retracted.Clear();
        session.Predictor.DrainPresentation(afterEffects, stopped);
        session.Predictor.DrainHits(afterHits, retracted);
        Assert.Empty(stopped); Assert.Empty(retracted);
        Assert.Equal(3u, session.LocalTick);
        Assert.Equal(MatchState.Ended, session.MatchState);
        Assert.Equal(finish.Snapshot.Data, session.Predictor.Simulation.CaptureSnapshot().Data);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void FinishRetractsFutureArcadeOutputsAndAllowsOlderConfirmedHistory(int mode)
    {
        var f = new Fixture(mode);
        if (mode == 1) f.Server.BeginSlopballRun(1337);
        else f.Server.BeginOnlineCourse();
        f.Session.IngestFrames(new[] { f.Frame(MatchState.Playing) });
        var finish = f.Frame(MatchState.Ended);
        if (mode == 2)
            f.Session.Predictor.Resolver.Spawn(new Hitbox { OwnerId = 1, X = 2, Y = 1, Radius = .2f,
                Shape = HitboxShape.Sphere, Damage = 1, DurationTicks = 1 });
        int ticks = mode == 1 ? 45 : 1;
        for (int i = 0; i < ticks; i++) Assert.True(f.Session.Predictor.Advance(default));
        var balls = new List<SlopballTickEvent>(); var stoppedBalls = new List<SlopballTickEvent>();
        var courses = new List<CourseTickEvent>(); var stoppedCourses = new List<CourseTickEvent>();
        f.Session.Predictor.DrainSlopballEvents(balls, stoppedBalls);
        f.Session.Predictor.DrainCourseEvents(courses, stoppedCourses);
        if (mode == 1) Assert.NotEmpty(balls);
        else Assert.NotEmpty(courses);
        f.Session.IngestFrames(new[] { finish });
        var startsBall = new List<SlopballTickEvent>(); var startsCourse = new List<CourseTickEvent>();
        f.Session.Predictor.DrainSlopballEvents(startsBall, stoppedBalls);
        f.Session.Predictor.DrainCourseEvents(startsCourse, stoppedCourses);
        Assert.Equal(balls.Select(x => x.Key), stoppedBalls.Select(x => x.Key));
        Assert.Equal(courses.Select(x => x.Key), stoppedCourses.Select(x => x.Key));
        Assert.Equal(finish.Snapshot.Data, f.Session.Predictor.Simulation.CaptureSnapshot().Data);
        Assert.False(f.Session.Predictor.Advance(default));
        f.Session.IngestFrames(new[] { f.Frame(MatchState.Playing, records: true) });
        f.AssertRecordsStarted();
        Assert.Equal(MatchState.Ended, f.Session.MatchState);
        Assert.Equal(0u, f.Session.LocalTick);
        Assert.Equal(finish.Snapshot.Data, f.Session.Predictor.Simulation.CaptureSnapshot().Data);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFinishInAMixedBatchCannotBeReopenedByLaterFrames(bool reverse)
    {
        var f = new Fixture();
        f.Server.SetTick(3);
        var finish = f.Frame(MatchState.Ended);
        f.Server.SetTick(9);
        var later = f.Frame(MatchState.Playing, records: true);
        f.Session.IngestFrames(reverse ? new[] { later, finish } : new[] { finish, later });
        Assert.Equal(MatchState.Ended, f.Session.MatchState);
        Assert.Equal(3u, f.Session.LocalTick);
        Assert.Equal(finish.Snapshot.Data, f.Session.Predictor.Simulation.CaptureSnapshot().Data);
        f.AssertNoOutputs();
    }

    [Fact]
    public void FinishAtTheLocalTickClearsRecentInputsAndRestartsPacing()
    {
        var f = new Fixture();
        f.Session.IngestFrames(new[] { f.Frame(MatchState.Playing) });
        f.Session.ObserveControl(new NetplayControlPacket(NetplayControlKind.Clock, 1, 0, 1), 0, 0);
        double now = 1.0 / 60;
        Assert.True(f.Session.Update(new InputState { MoveY = 1 }, now));
        bool skipped = false;
        for (int i = 0; i < 20 && !skipped; i++)
            skipped = !f.Session.Update(new InputState { Jump = true }, now);
        Assert.True(skipped);
        Assert.NotEmpty(f.Session.RecentInputs.ToArray());
        uint tick = f.Session.LocalTick;
        f.Server.SetTick(tick);
        var finish = f.Frame(MatchState.Ended);
        f.Session.IngestFrames(new[] { finish });
        Assert.Equal(tick, f.Session.LocalTick);
        Assert.Empty(f.Session.RecentInputs.ToArray());
        Assert.Equal(finish.Snapshot.Data, f.Session.Predictor.Simulation.CaptureSnapshot().Data);
        Assert.Equal(f.Session.Clock.CatchUpSteps(0, now), f.Session.Clock.StepsForUpdate(0, now));
        Assert.False(f.Session.Update(default, now + 10));
        Assert.False(f.Session.Predictor.Advance(default));
    }

    private sealed class Fixture
    {
        private readonly ArenaDefinition _arena;
        private readonly IMatchRule _rule;
        private readonly int _mode;
        public readonly ServerSimulation Server;
        public readonly NetplaySession Session;

        public Fixture(int mode = 0)
        {
            _mode = mode;
            _arena = mode == 1 ? SlopballTests.Court() : mode == 2 ? CharacterCourseTests.Arena() : TestHelpers.TestArena();
            _rule = mode == 1 ? new SlopballCoopMatchRule() : mode == 2 ? new CourseMatchRule(false) : new StockMatchRule(3);
            Server = NewServer();
            Session = new NetplaySession(_arena, 1, _rule);
            foreach (var pair in Server.GetAllStates()) Session.RegisterEntity(pair.Key, TestHelpers.EngineDef, pair.Value);
        }

        public ServerSimulation NewServer(bool extraFighter = false)
        {
            var server = new ServerSimulation(_arena, _rule);
            for (ulong id = 1; id <= (extraFighter ? 3u : 2u); id++)
                server.RegisterEntity(id, TestHelpers.EngineDef, TestHelpers.PlayerState() with { EntityId = id, PX = (float)id * 20 });
            return server;
        }

        public WorldFramePacket Frame(MatchState state, ServerSimulation? simulation = null, bool records = false)
        {
            var sim = simulation ?? Server;
            uint tick = sim.CurrentTick;
            return new(state, sim.CaptureSnapshot(), new[] { new RelayedInput(1, true, default), new RelayedInput(2, true, default) },
                records ? new[] { Event(tick), Event(tick) with { OperationIndex = 2, Source = PresentationEventSource.ProjectileSelfHit, PresentationId = "self.hit.reaction" } } : Array.Empty<TimelinePresentationEvent>(),
                records ? new[] { Hit(tick) } : Array.Empty<SpellResolver.HitResult>(),
                records && _mode == 1 ? new[] { new SlopballTickEvent(tick, new SlopballEvent(SlopballEventKind.Served, ArcadeBallSimulation.FirstEntityId, 1, 0)) } : null,
                records && _mode == 2 ? new[] { new CourseTickEvent(tick, 1, new CharacterCourseEvent(CharacterCourseEventKind.TargetBroken, 0, 0)) } : null);
        }

        public static TimelinePresentationEvent Event(uint tick) => new(tick, 1, 1, "sword.trail", 1, PresentationEventSource.Timeline, 0, 0, 0, 0);
        public static SpellResolver.HitResult Hit(uint tick) => new() { MatchTick = tick, OwnerEntityId = 1, TargetEntityId = 2, AttackSlot = 1, Damage = 1 };

        public void AssertNoOutputs()
        {
            var effects = new List<TimelinePresentationEvent>(); var stopped = new List<TimelinePresentationEvent>();
            var hits = new List<SpellResolver.HitResult>(); var retracted = new List<SpellResolver.HitResult>();
            var balls = new List<SlopballTickEvent>(); var stoppedBalls = new List<SlopballTickEvent>();
            var courses = new List<CourseTickEvent>(); var stoppedCourses = new List<CourseTickEvent>();
            Session.Predictor.DrainPresentation(effects, stopped); Session.Predictor.DrainHits(hits, retracted);
            Session.Predictor.DrainSlopballEvents(balls, stoppedBalls); Session.Predictor.DrainCourseEvents(courses, stoppedCourses);
            Assert.Empty(effects); Assert.Empty(stopped); Assert.Empty(hits); Assert.Empty(retracted);
            Assert.Empty(balls); Assert.Empty(stoppedBalls); Assert.Empty(courses); Assert.Empty(stoppedCourses);
        }

        public void AssertRecordsStarted()
        {
            var effects = new List<TimelinePresentationEvent>(); var stopped = new List<TimelinePresentationEvent>();
            var hits = new List<SpellResolver.HitResult>(); var retracted = new List<SpellResolver.HitResult>();
            var balls = new List<SlopballTickEvent>(); var stoppedBalls = new List<SlopballTickEvent>();
            var courses = new List<CourseTickEvent>(); var stoppedCourses = new List<CourseTickEvent>();
            Session.Predictor.DrainPresentation(effects, stopped); Session.Predictor.DrainHits(hits, retracted);
            Session.Predictor.DrainSlopballEvents(balls, stoppedBalls); Session.Predictor.DrainCourseEvents(courses, stoppedCourses);
            Assert.Equal(2, effects.Count); Assert.Single(hits);
            Assert.Empty(stopped); Assert.Empty(retracted); Assert.Empty(stoppedBalls); Assert.Empty(stoppedCourses);
            if (_mode == 1) Assert.Single(balls); else Assert.Empty(balls);
            if (_mode == 2) Assert.Single(courses); else Assert.Empty(courses);
        }
    }
}
