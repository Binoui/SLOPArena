using Xunit;
namespace SlopArena.Shared.Tests;

public sealed class CourseScrambleTests
{
    [Fact]
    public void BreakerScoresSharedTargetAndLastHitOwnsBonus()
    {
        var sim = Create(bonusHits: 3);
        OnlineCourseTests.Hit(sim, 2, 0);
        Assert.All(sim.CoursePlayers, p => Assert.Equal(1, p.Course.BrokenTargets));
        Assert.Equal((ushort)1, Player(sim, 2).Points);
        Assert.Equal(2ul, sim.CourseTargetBreaker(0));
        OnlineCourseTests.Hit(sim, 2, 1); OnlineCourseTests.Hit(sim, 2, 1);
        Assert.Equal((ushort)1, Player(sim, 2).Points);
        Assert.Equal(0ul, sim.CourseTargetBreaker(1));
        OnlineCourseTests.Hit(sim, 1, 1);
        Assert.Equal((ushort)3, Player(sim, 1).Points);
        Assert.Equal(1ul, sim.CourseTargetBreaker(1));
        Assert.Equal(1ul, Assert.Single(sim.LastTickOnlineCourseEvents, e => e.Event.Kind == CharacterCourseEventKind.TargetBroken).EntityId);
        OnlineCourseTests.Hit(sim, 2, 1);
        Assert.Equal((ushort)1, Player(sim, 2).Points);
    }

    [Fact]
    public void FinishRequiresEveryTargetAndPlacesAwardFiveThreeTwoOne()
    {
        var sim = Create(count: 4);
        Move(sim, 1, 12); sim.Tick(new()); Assert.Equal((byte)0, Player(sim, 1).Place);
        OnlineCourseTests.Hit(sim, 4, 0); OnlineCourseTests.Hit(sim, 4, 1);
        for (ulong id = 1; id <= 4; id++)
        { Move(sim, id, 12); sim.Tick(new()); Assert.Equal((byte)id, Player(sim, id).Place); }
        Assert.Equal(new ushort[] { 5, 3, 2, 3 }, sim.CoursePlayers.Select(p => p.Points));
        Assert.True(sim.EvaluateMatch().IsEnded); Assert.Equal(1ul, sim.EvaluateMatch().WinnerEntityId);
    }

    [Fact]
    public void SameTickFinishSharesPlaceAndBonusAndTiedPointsShareVictory()
    {
        var sim = Create(); OnlineCourseTests.Hit(sim, 1, 0); OnlineCourseTests.Hit(sim, 2, 1);
        Move(sim, 1, 12); Move(sim, 2, 12); sim.Tick(new());
        Assert.All(sim.CoursePlayers, p => { Assert.Equal((byte)1, p.Place); Assert.Equal((ushort)6, p.Points); });
        Assert.True(sim.EvaluateMatch().IsSharedVictory); Assert.Equal(0ul, sim.EvaluateMatch().WinnerEntityId);
    }

    [Fact]
    public void MostPointsWinsEvenWhenAnotherRacerFinishesFirst()
    {
        var sim = Create(bonusHits: 3);
        OnlineCourseTests.Hit(sim, 2, 0);
        for (int i = 0; i < 3; i++) OnlineCourseTests.Hit(sim, 2, 1);
        Move(sim, 1, 12); sim.Tick(new()); Move(sim, 2, 12); sim.Tick(new());
        Assert.Equal(2ul, sim.EvaluateMatch().WinnerEntityId);
        Assert.Equal(new ulong[] { 2, 1 }, CourseRunResult.From(sim).RankedPlayers.Select(p => p.EntityId));
    }

    [Fact]
    public void FinishWindowExpiresAfterThirtySecondsAndResultsRoundTripPoints()
    {
        var sim = Create(); OnlineCourseTests.Hit(sim, 1, 0); OnlineCourseTests.Hit(sim, 2, 1);
        Move(sim, 1, 12); sim.Tick(new()); Assert.Equal((ushort)1800, sim.OnlineCourse!.Value.FinishWindowTicksLeft);
        for (int i = 0; i < 1799; i++) { Move(sim, 2, 2); sim.Tick(new()); } Assert.False(sim.EvaluateMatch().IsEnded);
        sim.Tick(new()); Assert.True(sim.EvaluateMatch().IsEnded);
        var result = CourseRunResult.From(sim);
        var packet = new MatchResultPacket(sim.CurrentTick, false, result.Players.Select(p =>
            new MatchResultEntry(p.EntityId, result.PlaceFor(p.EntityId), 0, 0)).ToArray(), course: result);
        var bytes = new byte[packet.WireSize]; packet.Serialize(bytes);
        Assert.True(MatchResultPacket.TryDeserialize(bytes, out var read));
        Assert.Equal(CourseFormat.Scramble, read!.Course!.Format); Assert.Equal(result.Players, read.Course.Players);
        Assert.Equal((byte)0, result.Players.Single(p => p.EntityId == 2).Place);
        for (int i = 33; i < bytes.Length; i++) Assert.False(MatchResultPacket.TryDeserialize(bytes.AsSpan(0, i), out _));
    }

    [Fact]
    public void LeaderOrdersCheckpointThenDistanceThenIdentity()
    {
        var sim = Create();
        Move(sim, 1, 2); Move(sim, 2, 6);
        sim.Tick(new()); Assert.Equal(1ul, sim.Scramble!.Value.LeaderEntityId);
        Move(sim, 1, 0); Move(sim, 2, 2); sim.Tick(new()); Assert.Equal(2ul, sim.Scramble!.Value.LeaderEntityId);
        OnlineCourseTests.Hit(sim, 2, 0); Move(sim, 1, 4); sim.Tick(new());
        Move(sim, 1, 0); Move(sim, 2, 2); sim.Tick(new());
        Assert.Equal(1ul, sim.Scramble!.Value.LeaderEntityId);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void LeashAndFallsReturnRacerToLeaderSafeGround(bool fall)
    {
        var sim = Create(); Move(sim, 1, 2);
        for (int i = 0; i < 61; i++) sim.Tick(new());
        var safe = sim.Scramble!.Value.SafeGround;
        Move(sim, 2, fall ? 10000 : -19);
        if (fall) { var state = sim.GetState(2); state.PY = -200; state.IsGrounded = false; sim.SetState(2, state); }
        for (int i = 0; i < (fall ? 1 : 44); i++) sim.Tick(new());
        if (!fall) { Assert.True(sim.GetState(2).PX < 0); sim.Tick(new()); }
        var racer = sim.GetState(2);
        Assert.Equal(safe.Position.X, racer.PX); Assert.Equal(safe.Position.Y, racer.PY); Assert.Equal(safe.Position.Z, racer.PZ);
        Assert.Equal((ushort)0, Player(sim, 2).LeashTicks);
        Assert.Contains(sim.LastTickOnlineCourseEvents, e => e.EntityId == 2 && e.Event.Kind == CharacterCourseEventKind.Respawned);
    }

    [Fact]
    public void LeashResetsWhenRacerReturnsAndSafeGroundWaitsSixtyTicks()
    {
        var sim = Create(); Move(sim, 2, 10); sim.Tick(new()); Move(sim, 1, 2);
        for (int i = 0; i < 60; i++) sim.Tick(new());
        Assert.Equal(0, sim.Scramble!.Value.SafeGround.Position.X);
        sim.Tick(new()); Assert.Equal(2, sim.Scramble!.Value.SafeGround.Position.X);
        Move(sim, 2, -19); for (int i = 0; i < 44; i++) sim.Tick(new());
        Move(sim, 2, 0); sim.Tick(new()); Assert.Equal((ushort)0, Player(sim, 2).LeashTicks);
    }

    [Fact]
    public void MidScrambleRestoreReplaysScoresHistoryAndPendingLeashExactly()
    {
        var sim = Create(bonusHits: 3); var restored = Create(bonusHits: 3, reverse: true);
        OnlineCourseTests.Hit(sim, 1, 0); OnlineCourseTests.Hit(sim, 2, 1); Move(sim, 1, 4); sim.Tick(new());
        Move(sim, 2, -19); for (int i = 0; i < 30; i++) sim.Tick(new());
        restored.RestoreSnapshot(sim.CaptureSnapshot());
        Assert.Equal(sim.CaptureSnapshot().Data, restored.CaptureSnapshot().Data);
        for (int i = 0; i < 100; i++)
        {
            var input = new Dictionary<ulong, InputState> { [1] = new() { MoveY = .1f } };
            sim.Tick(new(input)); restored.Tick(new(input));
            Assert.Equal(sim.CaptureSnapshot().Data, restored.CaptureSnapshot().Data);
            Assert.Equal(sim.LastTickOnlineCourseEvents, restored.LastTickOnlineCourseEvents);
        }
        OnlineCourseTests.Hit(sim, 2, 1); OnlineCourseTests.Hit(sim, 1, 1);
        Move(sim, 1, 12); sim.Tick(new()); restored.RestoreSnapshot(sim.CaptureSnapshot());
        Assert.Equal(sim.CaptureSnapshot().Data, restored.CaptureSnapshot().Data);
    }

    [Fact]
    public void ScrambleIsReservedButNotAdmittedOnline()
    {
        Assert.Equal("course-scramble", MatchFormat.CourseScramble.ToWire());
        Assert.True(MatchFormats.TryParseWire("course-scramble", out var parsed));
        Assert.Equal(MatchFormat.CourseScramble, parsed);
        Assert.False(MatchFormat.CourseScramble.Admits(CharacterCourseTests.Arena()));
    }

    [Fact]
    public void EqualPointsShareVictoryEvenWithDifferentFinishPlaces()
    {
        var sim = Create(); OnlineCourseTests.Hit(sim, 2, 0); OnlineCourseTests.Hit(sim, 2, 1);
        Move(sim, 1, 12); sim.Tick(new()); Move(sim, 2, 12); sim.Tick(new());
        Assert.All(sim.CoursePlayers, p => Assert.Equal((ushort)5, p.Points));
        Assert.True(sim.EvaluateMatch().IsSharedVictory);
        Assert.Equal(new ulong[] { 1, 2 }, CourseRunResult.From(sim).RankedPlayers.Select(p => p.EntityId));
    }

    [Fact]
    public void VerticalLeashPullsAfterFortyFiveTicksWithoutADeath()
    {
        var sim = Create(); Move(sim, 2, 1); OnlineCourseTests.Hit(sim, 1, 0); Move(sim, 1, 4); sim.Tick(new());
        for (int i = 0; i < 61; i++) { Move(sim, 2, 1); sim.Tick(new()); }
        Assert.Equal(0, Player(sim, 1).Course.SavedCheckpoint);
        Assert.Equal(-1, Player(sim, 2).Course.SavedCheckpoint);
        for (int i = 0; i < 45; i++)
        {
            var leader = sim.GetState(1); leader.PY = 20; leader.VY = 0; leader.IsGrounded = false; sim.SetState(1, leader);
            sim.Tick(new());
            if (i == 43) Assert.Equal((ushort)44, Player(sim, 2).LeashTicks);
        }
        Assert.Equal(sim.Scramble!.Value.SafeGround.Position.X, sim.GetState(2).PX);
        Assert.Equal((byte)0, sim.GetState(2).Deaths);
        Assert.Equal((ushort)0, Player(sim, 2).LeashTicks);
    }

    [Fact]
    public void MalformedScrambleSnapshotsAreRejectedWithoutChangingTheWorld()
    {
        var sim = Create(); OnlineCourseTests.Hit(sim, 2, 0); sim.Tick(new());
        byte[] original = sim.CaptureSnapshot().Data;
        int extension = original.Length - 1 - (11 + 2 * (4 + 32 + 60 * 16) + 2 * 8);
        foreach (var mutation in new Action<byte[]>[]
        {
            bytes => bytes[extension] = 99,
            bytes => System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(extension + 1), 127),
            bytes => System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(extension + 9), 60),
            bytes => System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(extension + 11), 999),
            bytes => System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(extension + 13), 45),
            bytes => System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(extension + 15), BitConverter.SingleToInt32Bits(float.NaN)),
            bytes => System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(bytes.Length - 17), 127),
        })
        {
            byte[] bytes = (byte[])original.Clone(); mutation(bytes);
            Assert.Throws<InvalidDataException>(() => sim.RestoreSnapshot(new WorldSnapshot(bytes)));
            Assert.Equal(original, sim.CaptureSnapshot().Data);
        }
        var restored = Create(); restored.RestoreSnapshot(new WorldSnapshot(original));
        Assert.Equal(original, restored.CaptureSnapshot().Data);
    }

    private static CoursePlayerProgress Player(ServerSimulation sim, ulong id) => sim.CoursePlayers.Single(p => p.EntityId == id);
    private static void Move(ServerSimulation sim, ulong id, float x) => OnlineCourseTests.Move(sim, id, x);
    private static ServerSimulation Create(byte bonusHits = 1, int count = 2, bool reverse = false)
    {
        var arena = CharacterCourseTests.Arena(); var c = arena.Arcade!.Course!;
        arena.Arcade = ArcadeStageDefinition.ForCourse("scramble-test", 1, Array.Empty<string>(), new(
            new[] { c.Targets[0], new CourseTargetDefinition("bonus", c.Targets[1].Position, c.Targets[1].Radius, bonusHits) }, c.Checkpoints, c.Finish));
        var sim = new ServerSimulation(arena, new CourseMatchRule(CourseFormat.Scramble));
        var ids = Enumerable.Range(1, count).Select(i => (ulong)i); if (reverse) ids = ids.Reverse();
        foreach (ulong id in ids)
        { sim.RegisterEntity(id, TestHelpers.EngineDef, TestHelpers.PlayerState()); sim.SetRespawnPosition(id, 0, .85f, 0); }
        sim.BeginOnlineCourse(); return sim;
    }
}
