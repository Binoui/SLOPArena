using System.Buffers.Binary;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class CourseRacingLineTests
{
    [Theory]
    [InlineData("fightguy_sanctuary")] [InlineData("fightguy_pier")]
    [InlineData("manki_quarry")] [InlineData("wibou_mall")] [InlineData("nyx_manor")]
    public void BakedMainPathHasNoTargetDetourReversalsOrInflatedLength(string key)
    {
        var arena = ArenaBinaryFormat.LoadFromFile(Path.Combine(CourseFinishability.Root, "data/arenas", key + ".arena"))!.Value;
        var course = arena.Arcade!.Course!;
        var line = Assert.IsType<CourseRacingLine>(course.RacingLine);
        for (int i = 1; i < line.Points.Count - 1; i++)
        {
            var a = line.Points[i - 1]; var b = line.Points[i]; var c = line.Points[i + 1];
            float dot = (b.X - a.X) * (c.X - b.X) + (b.Y - a.Y) * (c.Y - b.Y) + (b.Z - a.Z) * (c.Z - b.Z);
            float lengths = MathF.Sqrt(CourseRacingLine.DistanceSquared(a, b) * CourseRacingLine.DistanceSquared(b, c));
            Assert.True(dot / lengths >= -MathF.Sqrt(.5f), $"{key} reverses by more than 135 degrees at point {i}.");
        }
        var spawn = arena.SpawnPoints[0];
        var previous = new ArcadePoint(spawn.X, spawn.Y, spawn.Z);
        float checkpointPath = 0;
        foreach (var point in course.Checkpoints.Select(c => c.Volume.Center).Append(course.Finish.Center))
        {
            checkpointPath += MathF.Sqrt(CourseRacingLine.DistanceSquared(previous, point));
            previous = point;
        }
        Assert.InRange(line.Length, checkpointPath, checkpointPath * 2);
    }

    [Theory]
    [InlineData("fightguy_sanctuary")] [InlineData("fightguy_pier")]
    [InlineData("manki_quarry")] [InlineData("wibou_mall")] [InlineData("nyx_manor")]
    public void FollowingBakedLineKeepsProgressThroughSwitchbacks(string key)
    {
        var arena = ArenaBinaryFormat.LoadFromFile(Path.Combine(CourseFinishability.Root, "data/arenas", key + ".arena"))!.Value;
        var line = Assert.IsType<CourseRacingLine>(arena.Arcade!.Course!.RacingLine);
        float progress = 0, expected = 0;
        for (int i = 1; i < line.Points.Count; i++)
        {
            var a = line.Points[i - 1]; var b = line.Points[i];
            float length = MathF.Sqrt(CourseRacingLine.DistanceSquared(a, b));
            int steps = (int)MathF.Ceiling(length / .1f);
            for (int step = 1; step <= steps; step++)
            {
                float t = (float)step / steps;
                progress = line.Project(new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t), progress);
                Assert.True(MathF.Abs(progress - (expected + length * t)) < .15f,
                    $"{key} segment {i} step {step}: expected {expected + length * t}, got {progress}");
            }
            expected += length;
        }
    }

    [Fact]
    public void ShortcutSkippingSavedCheckpointTakesLeadAndStaysAheadOfLeash()
    {
        var sim = Create();
        OnlineCourseTests.Hit(sim, 1, 0);
        OnlineCourseTests.Move(sim, 1, 4); sim.Tick(new());
        Assert.Equal(0, sim.CoursePlayers.First().Course.SavedCheckpoint);
        var shortcut = sim.GetState(2); shortcut.PX = 10; shortcut.PZ = 3; sim.SetState(2, shortcut);
        sim.Tick(new());
        Assert.Equal(-1, sim.CoursePlayers.Last().Course.SavedCheckpoint);
        Assert.Equal(2ul, sim.Scramble!.Value.LeaderEntityId);
        OnlineCourseTests.Move(sim, 1, -19);
        for (int i = 0; i < 90; i++)
        {
            sim.Tick(new());
            Assert.Equal(2ul, sim.Scramble.Value.LeaderEntityId);
            Assert.Equal((ushort)0, sim.CoursePlayers.Last().LeashTicks);
            Assert.True(sim.GetState(2).PX > 9);
        }
    }

    [Fact]
    public void NearbyParallelFloorsStayOnTheirBranchButShortcutLandingAdvances()
    {
        var line = new CourseRacingLine(new ArcadePoint[] { new(0, 1, 0), new(40, 1, 0), new(40, 5, 0), new(0, 5, 0) });
        float ground = line.Project(new(8, 1, 0), 7);
        Assert.Equal(8, ground);
        for (int i = 0; i < 100; i++)
        {
            ground = line.Project(new(8, 2, 0), ground);
            Assert.Equal(8, ground);
        }
        Assert.Equal(76, line.Project(new(8, 5, 0), ground));
        Assert.Equal(76, line.Project(new(8, 4, 0), 76));
        var crossing = new CourseRacingLine(new ArcadePoint[] { new(-10, 1, 0), new(10, 1, 0), new(10, 1, 20), new(0, 1, 0), new(0, 1, -10) });
        Assert.Equal(10, crossing.Project(new(0, 1, 0), 9));
    }

    [Fact]
    public void StackedFloorsDoNotFlipTheLeaderWhenLowerRacerJumps()
    {
        var c = CharacterCourseTests.Arena().Arcade!.Course!;
        var line = new CourseRacingLine(new ArcadePoint[] { new(0, 1, 0), new(40, 1, 0), new(40, 5, 0), new(0, 5, 0) });
        var scramble = new CourseScramble(new(c.Targets, c.Checkpoints, new(new(0, 5, 0), new(2, 2, 2)), line));
        var states = new Dictionary<ulong, CharacterState>
        {
            [1] = new() { PX = 8, PY = 1 }, [2] = new() { PX = 9, PY = 1 }
        };
        foreach (var pair in states) scramble.Register(pair.Key, pair.Value);
        var players = new[] { new CoursePlayerProgress(1, default, 0, false), new CoursePlayerProgress(2, default, 0, false) };
        scramble.Tick(players, states, Array.Empty<ulong>(), (_, _) => { });
        for (int i = 0; i < 90; i++)
        {
            var lower = states[1]; lower.PY = 2; states[1] = lower;
            scramble.Tick(players, states, Array.Empty<ulong>(), (_, _) => { });
            Assert.Equal(2ul, scramble.State.LeaderEntityId);
            Assert.True(scramble.Progress(1) < scramble.Progress(2));
        }
    }

    [Fact]
    public void LineSnapshotRestoresProgressSafeGroundAndReplaysExactly()
    {
        var sim = Create(); var restored = Create();
        var shortcut = sim.GetState(2); shortcut.PX = 10; shortcut.PZ = 3; sim.SetState(2, shortcut);
        for (int i = 0; i < 70; i++) sim.Tick(new());
        restored.RestoreSnapshot(sim.CaptureSnapshot());
        Assert.Equal(sim.CoursePlayers, restored.CoursePlayers);
        for (int i = 0; i < 100; i++)
        {
            sim.Tick(new()); restored.Tick(new());
            Assert.Equal(sim.CaptureSnapshot().Data, restored.CaptureSnapshot().Data);
            Assert.Equal(sim.Scramble, restored.Scramble);
        }
    }

    [Fact]
    public void InvalidSnapshotProgressIsRejectedWithoutChangingTheWorld()
    {
        var sim = Create(); sim.Tick(new());
        byte[] original = sim.CaptureSnapshot().Data;
        int extension = original.Length - 1 - (11 + 2 * (8 + 62 * 20) + 2 * 8);
        foreach (float progress in new[] { -1, float.NaN, float.PositiveInfinity, 10000 })
        {
            byte[] changed = (byte[])original.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(changed.AsSpan(extension + 15), BitConverter.SingleToInt32Bits(progress));
            Assert.Throws<InvalidDataException>(() => sim.RestoreSnapshot(new WorldSnapshot(changed)));
            Assert.Equal(original, sim.CaptureSnapshot().Data);
        }
    }

    [Fact]
    public void CourseV8RoundTripsAndRacingLineDoesNotArchiveSoloRecords()
    {
        var arena = CharacterCourseTests.Arena();
        var fighter = BuiltInContentResolver.Resolve(CharacterClass.FightGuy);
        string before = ArcadeRecordCompatibility.Course(arena, fighter);
        arena.Arcade = WithLine(arena.Arcade!);
        var bytes = ArenaBinaryFormat.Serialize(arena);
        var loaded = ArenaBinaryFormat.Deserialize(bytes)!.Value;
        Assert.Equal(bytes, ArenaBinaryFormat.Serialize(loaded));
        Assert.Equal(before, ArcadeRecordCompatibility.Course(loaded, fighter));
        byte[] section = ArcadeStageBinaryFormat.Serialize(arena.Arcade!);
        Assert.Equal(8, BinaryPrimitives.ReadUInt16LittleEndian(section));
        for (int i = 0; i < section.Length; i++) Assert.Null(ArcadeStageBinaryFormat.Deserialize(section[..i]));
        Assert.Null(ArcadeStageBinaryFormat.Deserialize(section.Concat(new byte[1]).ToArray()));
    }

    [Fact]
    public void LineRejectsInvalidPointsAndArenaEndpoints()
    {
        Assert.Throws<ArgumentException>(() => new CourseRacingLine(new ArcadePoint[1]));
        Assert.Throws<ArgumentException>(() => new CourseRacingLine(new ArcadePoint[65]));
        Assert.Throws<ArgumentException>(() => new CourseRacingLine(new ArcadePoint[] { default, default }));
        foreach (float value in new[] { float.NaN, float.PositiveInfinity, 10001f })
            Assert.Throws<ArgumentException>(() => new CourseRacingLine(new ArcadePoint[] { default, new(value, 1, 1) }));
        var arena = CharacterCourseTests.Arena(); var c = arena.Arcade!.Course!;
        Assert.Throws<ArgumentException>(() => new CharacterCourseDefinition(c.Targets, c.Checkpoints, c.Finish,
            new(new ArcadePoint[] { default, new(100, 1, 0) })));
        arena.Arcade = ArcadeStageDefinition.ForCourse("bad-start", 1, Array.Empty<string>(), new(c.Targets, c.Checkpoints, c.Finish,
            new(new ArcadePoint[] { new(20, 1, 0), c.Finish.Center })));
        Assert.Throws<ArgumentException>(() => ArenaBinaryFormat.Serialize(arena));
    }

    private static ArcadeStageDefinition WithLine(ArcadeStageDefinition stage) => ArcadeStageDefinition.ForCourse(
        stage.ProfileId, stage.GameplayRevision, stage.AllowedPackageIds, new(stage.Course!.Targets, stage.Course.Checkpoints,
            stage.Course.Finish, new(new ArcadePoint[] { new(0, .85f, 0), new(4, .85f, 0), new(4, .85f, 3), new(10, .85f, 3), new(12, .85f, 0) })));

    private static ServerSimulation Create()
    {
        var arena = CharacterCourseTests.Arena(); arena.Arcade = WithLine(arena.Arcade!);
        var sim = new ServerSimulation(arena, new CourseMatchRule(CourseFormat.Scramble));
        foreach (ulong id in new ulong[] { 1, 2 })
        { sim.RegisterEntity(id, TestHelpers.EngineDef, TestHelpers.PlayerState()); sim.SetRespawnPosition(id, 0, .85f, 0); }
        sim.BeginOnlineCourse(); return sim;
    }
}
