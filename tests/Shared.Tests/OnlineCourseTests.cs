using SlopArena.Shared.Rollback;
using Xunit;

namespace SlopArena.Shared.Tests;

public class OnlineCourseTests
{
    [Theory]
    [InlineData("fightguy_pier", CharacterClass.FightGuy)]
    [InlineData("fightguy_sanctuary", CharacterClass.FightGuy)]
    [InlineData("manki_quarry", CharacterClass.Manki)]
    [InlineData("wibou_mall", CharacterClass.Wibou)]
    public void ExistingCoursesHaveSafeFourPlayerStartsWithoutChangingSoloData(string key, CharacterClass fighter)
    {
        var arena = WorldSnapshotTests.LoadArena(key);
        var before = ArenaBinaryFormat.Serialize(arena);
        var entry = BuiltInContentResolver.Resolve(fighter);
        var sim = new ServerSimulation(arena, new CourseMatchRule(false));
        for (int i = 0; i < 4; i++)
        {
            var start = MatchFormat.CourseRace.StartFor(arena, i, 4);
            sim.RegisterEntity((ulong)i + 1, entry.Definition, new CharacterState
            { PX = start.X, PY = start.Y, PZ = start.Z, FacingYaw = start.Yaw, IsGrounded = true,
                JumpsLeft = entry.Definition.Movement.MaxJumps, AirDodgesLeft = 1 }, entry.BakedAnimation);
            sim.SetRespawnPosition((ulong)i + 1, start.X, start.Y, start.Z, start.Yaw);
        }
        sim.BeginOnlineCourse();
        for (int tick = 0; tick < 120; tick++) sim.Tick(new());
        Assert.All(sim.GetAllStates().Values, s => { Assert.Equal((byte)0, s.Deaths); Assert.True(s.IsGrounded); });
        Assert.Equal(before, ArenaBinaryFormat.Serialize(arena));
    }

    [Fact]
    public void RaceTargetsBelongToTheAttackerAndAliasesSelectTheirOwnSet()
    {
        var sim = Create(false);
        Hit(sim, 1, 0);
        Assert.True(sim.IsCourseTargetBroken(1, 0));
        Assert.False(sim.IsCourseTargetBroken(2, 0));
        Assert.Equal(sim.CourseTargetEntityId(1, 0), Assert.Single(sim.LastTickHits).TargetEntityId);
        sim.Tick(new() { [2] = new() { ToggleLock = true, TargetEntityId = sim.CourseTargetInputAlias(0) } });
        Assert.Equal(sim.CourseTargetEntityId(2, 0), sim.GetState(2).TargetEntityId);
    }

    [Fact]
    public void CoopSharesTargetHitsPassesThroughTeammatesAndKeepsIndividualCheckpoints()
    {
        var sim = Create(true);
        Move(sim, 2, 2);
        Hit(sim, 1, 0);
        Assert.True(sim.IsCourseTargetBroken(1, 0)); Assert.True(sim.IsCourseTargetBroken(2, 0));
        Assert.Equal(0, sim.GetState(2).DamagePercent);
        Assert.Single(sim.LastTickOnlineCourseEvents.Where(x => x.Event.Kind == CharacterCourseEventKind.TargetBroken));
        Move(sim, 1, 4); Move(sim, 2, 0); sim.Tick(new());
        Assert.Equal(0, sim.CourseFor(1)!.Value.SavedCheckpoint);
        Assert.Equal(-1, sim.CourseFor(2)!.Value.SavedCheckpoint);
        foreach (ulong id in new ulong[] { 1, 2 })
        { var state = sim.GetState(id); state.PY = -200; state.PX = 10000; state.IsGrounded = false; sim.SetState(id, state); }
        sim.Tick(new());
        Assert.Equal(4, sim.GetState(1).PX); Assert.Equal(0, sim.GetState(2).PX);
        Assert.Equal(2, sim.LastTickOnlineCourseEvents.Count(x => x.Event.Kind == CharacterCourseEventKind.Respawned));
    }

    [Fact]
    public void FirstFinishStartsExactlyThirtySecondsAndFinishedRacerCannotInterfere()
    {
        var sim = Create(false); Hit(sim, 1, 0); Hit(sim, 1, 1);
        Move(sim, 1, 12); sim.Tick(new());
        Assert.False(sim.EvaluateMatch().IsEnded);
        Assert.Equal((ushort)1800, sim.OnlineCourse!.Value.FinishWindowTicksLeft);
        Assert.True(sim.IsEntityInactive(1));
        var finished = sim.GetState(1);
        for (int tick = 0; tick < 1799; tick++) sim.Tick(new() { [1] = new() { MoveX = 1, ActiveSlot = 1 } });
        Assert.False(sim.EvaluateMatch().IsEnded); Assert.Equal(finished, sim.GetState(1));
        sim.Tick(new()); Assert.True(sim.EvaluateMatch().IsEnded); Assert.Equal(1ul, sim.EvaluateMatch().WinnerEntityId);
        var result = CourseRunResult.From(sim);
        Assert.Equal(0ul, result.Players.Single(x => x.EntityId == 2).FinishTicks);
        Assert.Equal((byte)0, result.Players.Single(x => x.EntityId == 2).Place);
    }

    [Fact]
    public void SameTickFinishesTieAndRaceEndsEarlyWhenEveryoneIsDone()
    {
        var sim = Create(false);
        foreach (ulong id in new ulong[] { 1, 2 }) { Hit(sim, id, 0); Hit(sim, id, 1); }
        Move(sim, 1, 12); Move(sim, 2, 12); sim.Tick(new());
        Assert.True(sim.EvaluateMatch().IsEnded); Assert.True(sim.EvaluateMatch().IsSharedVictory);
        Assert.All(sim.CoursePlayers, x => Assert.Equal((byte)1, x.Place));
    }

    [Fact]
    public void CoopCompletesForTheTeamAndWithdrawnRacerNeverHoldsARaceOpen()
    {
        var coop = Create(true); Hit(coop, 1, 0); Hit(coop, 2, 1); Move(coop, 2, 12); coop.Tick(new());
        Assert.True(coop.EvaluateMatch().IsSharedVictory);
        Assert.All(CourseRunResult.From(coop).Players, x => Assert.Equal((byte)1, x.Place));
        var race = Create(false); race.WithdrawCoursePlayer(2); Hit(race, 1, 0); Hit(race, 1, 1);
        Move(race, 1, 12); race.Tick(new()); Assert.True(race.EvaluateMatch().IsEnded);
        Assert.True(race.CoursePlayers.Single(x => x.EntityId == 2).Withdrawn);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RestoreRetainsTargetsCheckpointsAndFinishWindowAndReplaysExactly(bool cooperative)
    {
        var sim = Create(cooperative); Hit(sim, 1, 0); Move(sim, 1, 4); sim.Tick(new());
        var restored = Create(cooperative, reverse: true); restored.RestoreSnapshot(sim.CaptureSnapshot());
        for (int tick = 0; tick < 120; tick++)
        {
            var inputs = new Dictionary<ulong, InputState> { [1] = new() { MoveY = tick < 60 ? .3f : -.3f } };
            sim.Tick(new(inputs)); restored.Tick(new(inputs));
            Assert.Equal(sim.CaptureSnapshot().Data, restored.CaptureSnapshot().Data);
        }
        Hit(sim, 1, 1); Move(sim, 1, 12); sim.Tick(new());
        restored.RestoreSnapshot(sim.CaptureSnapshot());
        Assert.Equal(sim.CaptureSnapshot().Data, restored.CaptureSnapshot().Data);
        for (int tick = 0; tick < 1800; tick++)
        { sim.Tick(new()); restored.Tick(new()); Assert.Equal(sim.CaptureSnapshot().Data, restored.CaptureSnapshot().Data); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PredictorRestoresCourseSnapshotsAndDeduplicatesCourseEvents(bool cooperative)
    {
        var server = Create(cooperative);
        var predictor = new NetplayPredictor(CharacterCourseTests.Arena(), 1, new CourseMatchRule(cooperative));
        foreach (ulong id in new ulong[] { 1, 2 }) predictor.RegisterEntity(id, TestHelpers.EngineDef, TestHelpers.PlayerState());
        Hit(server, 1, 0);
        var frame = new WorldFramePacket(MatchState.Playing, server.CaptureSnapshot(),
            new[] { new RelayedInput(1, true, default), new RelayedInput(2, true, default) },
            Array.Empty<TimelinePresentationEvent>(), server.LastTickHits, courseEvents: server.LastTickOnlineCourseEvents);
        var decoded = WorldFramePacket.Deserialize(frame.Serialize()); predictor.IngestFrame(decoded);
        var started = new List<CourseTickEvent>(); var retracted = new List<CourseTickEvent>();
        predictor.DrainCourseEvents(started, retracted); Assert.Single(started); Assert.Empty(retracted);
        started.Clear(); retracted.Clear(); predictor.IngestFrame(decoded); predictor.DrainCourseEvents(started, retracted); Assert.Empty(started);
        Assert.Equal(server.CaptureSnapshot().Data, predictor.Simulation.CaptureSnapshot().Data);
        server.Tick(new() { [1] = default, [2] = default }); predictor.Advance(default);
        Assert.Equal(server.CaptureSnapshot().Data, predictor.Simulation.CaptureSnapshot().Data);
    }

    [Fact]
    public void CourseResultsCarryFinishTimesDnfProgressAndRejectMalformedSuffixes()
    {
        var sim = Create(false); Hit(sim, 1, 0); Hit(sim, 1, 1); Move(sim, 1, 12); sim.Tick(new());
        Hit(sim, 2, 0);
        for (int i = 0; i < 1800; i++) sim.Tick(new());
        var course = CourseRunResult.From(sim);
        var packet = new MatchResultPacket(sim.CurrentTick, false,
            course.Players.Select(p => new MatchResultEntry(p.EntityId, p.Place, 0, 0)).ToArray(), course: course);
        byte[] bytes = new byte[packet.WireSize]; packet.Serialize(bytes);
        Assert.True(MatchResultPacket.TryDeserialize(bytes, out var decoded));
        Assert.Equal(course.Players.ToArray(), decoded!.Course!.Players.ToArray());
        Assert.Equal((ushort)1, decoded.Course.Players.Single(x => x.EntityId == 2).BrokenTargets);
        for (int length = 33; length < bytes.Length; length++)
            Assert.False(MatchResultPacket.TryDeserialize(bytes.AsSpan(0, length), out _));
        Assert.False(MatchResultPacket.TryDeserialize(bytes.Concat(new byte[] { 1 }).ToArray(), out _));
    }

    [Fact]
    public void AuthoritativeCorrectionRestoresPredictedTargetAndRetractsItsBreak()
    {
        var host = Create(false);
        var predictor = new NetplayPredictor(CharacterCourseTests.Arena(), 1, new CourseMatchRule(false));
        foreach (ulong id in new ulong[] { 1, 2 }) predictor.RegisterEntity(id, TestHelpers.EngineDef, TestHelpers.PlayerState());
        WorldFramePacket Frame() => new(MatchState.Playing, host.CaptureSnapshot(),
            new[] { new RelayedInput(1, true, default), new RelayedInput(2, true, default) },
            Array.Empty<TimelinePresentationEvent>(), Array.Empty<SpellResolver.HitResult>());
        predictor.IngestFrame(Frame());
        predictor.Simulation.Resolver.Spawn(new Hitbox { OwnerId = 1, X = 2, Y = 1, Radius = .2f,
            Shape = HitboxShape.Sphere, Damage = 1, DurationTicks = 1 });
        predictor.Advance(default);
        var started = new List<CourseTickEvent>(); var retracted = new List<CourseTickEvent>();
        predictor.DrainCourseEvents(started, retracted); Assert.Single(started);
        Assert.True(predictor.Simulation.IsCourseTargetBroken(1, 0));
        host.Tick(new() { [1] = default, [2] = default }); predictor.IngestFrame(Frame());
        started.Clear(); retracted.Clear(); predictor.DrainCourseEvents(started, retracted);
        Assert.Empty(started); Assert.Single(retracted);
        Assert.False(predictor.Simulation.IsCourseTargetBroken(1, 0));
    }

    internal static ServerSimulation Create(bool cooperative, bool reverse = false)
    {
        var sim = new ServerSimulation(CharacterCourseTests.Arena(), new CourseMatchRule(cooperative));
        foreach (ulong id in reverse ? new ulong[] { 2, 1 } : new ulong[] { 1, 2 })
        { sim.RegisterEntity(id, TestHelpers.EngineDef, TestHelpers.PlayerState()); sim.SetRespawnPosition(id, 0, .85f, 0); }
        sim.BeginOnlineCourse(); return sim;
    }
    internal static void Move(ServerSimulation sim, ulong id, float x)
    { var state = sim.GetState(id); state.PX = x; state.PY = .85f; state.PZ = 0; state.VX = state.VY = state.VZ = 0; sim.SetState(id, state); }
    internal static void Hit(ServerSimulation sim, ulong owner, int index)
    {
        sim.Resolver.Spawn(new Hitbox { OwnerId = owner, AttackSlot = 1, ActivationId = sim.CurrentTick + 10,
            X = 2 + index * 4, Y = 1, Radius = .2f, Shape = HitboxShape.Sphere, Damage = 1, DurationTicks = 1 });
        sim.Tick(new());
    }
}
