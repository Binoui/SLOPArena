using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Shared.Mayhem;
using Xunit;
using Xunit.Abstractions;

namespace SlopArena.Shared.Tests;

public class MayhemOnlineCodecTests
{
    private readonly ITestOutputHelper _output;
    public MayhemOnlineCodecTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void SeededShowFramesCarryEveryEventKindAndStayWithinTheFrameBudget()
    {
        var kinds = new HashSet<MayhemEventKind>();
        int maxSnapshot = 0, maxFrame = 0, frames = 0, maxEvents = 0;
        foreach (uint seed in new[] { 41u, MayhemWorld.SeedWith(MayhemChannels.NoRunning, 0).Seed,
            MayhemWorld.SeedWith(MayhemChannels.BreakBoards, 0).Seed, MayhemWorld.SeedWith(MayhemChannels.BreakOut, 0).Seed }.Distinct())
        {
            var history = new WorldFrameHistory();
            var sim = MayhemWorld.NewShow(seed, roster: MayhemWorld.Four);
            var cpus = MayhemWorld.Cpus(sim, 6);
            uint fall = MayhemWorld.Plan(sim).PlayStart(0) + 10;
            while (!sim.EvaluateMatch().IsEnded && sim.CurrentTick < 20000)
            {
                if (sim.CurrentTick == fall) MayhemWorld.Ko(sim, 2, -5f);
                MayhemWorld.StepCpus(sim, cpus);
                history.Record(sim.CurrentTick, sim.GetPresentationEvents(true), sim.LastTickHits, mayhemEvents: sim.LastTickMayhemEvents);
                var frame = new WorldFramePacket(MatchState.Playing, sim.CaptureSnapshot(), Array.Empty<RelayedInput>(),
                    history.EventsForFrame(sim.CurrentTick), history.HitsForFrame(sim.CurrentTick),
                    mayhemEvents: history.MayhemEventsForFrame(sim.CurrentTick));
                var bytes = frame.Serialize();
                var decoded = WorldFramePacket.Deserialize(bytes);
                Assert.Equal(frame.MayhemEvents, decoded.MayhemEvents);
                Assert.Equal(frame.MayhemEvents.Count, frame.MayhemEvents.Select(e => e.Key).Distinct().Count());
                foreach (var evt in decoded.MayhemEvents) kinds.Add(evt.Kind);
                maxEvents = Math.Max(maxEvents, frame.MayhemEvents.Count);
                maxSnapshot = Math.Max(maxSnapshot, frame.Snapshot.Data.Length);
                maxFrame = Math.Max(maxFrame, bytes.Length); frames++;
            }
            Assert.True(sim.EvaluateMatch().IsEnded);
        }
        Assert.True(Enum.GetValues<MayhemEventKind>().All(kinds.Contains),
            "missing " + string.Join(",", Enum.GetValues<MayhemEventKind>().Where(k => !kinds.Contains(k))));
        Assert.True(maxFrame < WorldFramePacket.MaxBodyBytes);
        Assert.True(maxEvents < WorldFramePacket.MaxMayhemEvents);
        _output.WriteLine($"four CPUs, fixed seed corpus: {frames} frames, max snapshot {maxSnapshot} bytes, max frame {maxFrame} bytes, max events {maxEvents}");
    }

    [Fact]
    public void MayhemWireRejectsMalformedEventsAndOversizedHistory()
    {
        var (sim, episode) = MayhemWorld.Airing(MayhemChannels.Dodge, SpeedTier.One);
        MayhemWorld.Step(sim);
        var call = Assert.Single(sim.LastTickMayhemEvents);
        Assert.Equal(MayhemEventKind.Call, call.Kind);
        WorldFramePacket Frame(params MayhemEvent[] events) => new(MatchState.Playing, sim.CaptureSnapshot(),
            Array.Empty<RelayedInput>(), Array.Empty<TimelinePresentationEvent>(), Array.Empty<SpellResolver.HitResult>(), mayhemEvents: events);
        foreach (var invalid in new[]
        {
            call with { Kind = (MayhemEventKind)255 }, call with { Subject = 1 }, call with { MatchTick = call.MatchTick + 100 },
            call with { Episode = MayhemShowTuning.Episodes }, call with { Lane = 2 }, call with { Value = 1 }, call with { Operation = 7 },
            call with { Kind = MayhemEventKind.Pass, Subject = 1, Value = 9 }, call with { Kind = MayhemEventKind.Fail, Subject = 1, Value = 99 },
            call with { Kind = MayhemEventKind.SolidHit, Subject = 1 }, call with { Kind = MayhemEventKind.TargetBroken, Subject = MayhemEvent.FirstTargetEntityId, Value = 1 },
            call with { Kind = MayhemEventKind.SpeedUp, Episode = MayhemEvent.ShowEpisode, Value = 0 },
        })
            Assert.Throws<ArgumentOutOfRangeException>(() => Frame(invalid).Serialize());
        Assert.Throws<ArgumentOutOfRangeException>(() => Frame(Enumerable.Repeat(call, WorldFramePacket.MaxMayhemEvents + 1).ToArray()));
        var valid = new[]
        {
            call,
            call with { Kind = MayhemEventKind.Pass, Subject = 2, Value = MayhemShowPlan.Award(episode) },
            call with { Kind = MayhemEventKind.TargetHit, Subject = MayhemEvent.FirstTargetEntityId, Operation = MayhemEvent.HitOperation(1, 77), Value = 2 },
        };
        var decoded = WorldFramePacket.Deserialize(Frame(valid).Serialize());
        Assert.Equal(valid, decoded.MayhemEvents);
        var wire = Frame(call).Serialize(); wire[4]--;
        Assert.False(WorldFramePacket.TryDeserialize(wire, out _));
    }

    [Fact]
    public void MayhemResultRoundTripsPassMasksTiesAndWithdrawalsAndRecomputesRatings()
    {
        uint boss = 1u << MayhemShowTuning.RegularPerAct;
        var result = new MayhemRunResult(new[]
        {
            new MayhemFinishResult(1, 0b111 | boss, false), new MayhemFinishResult(2, 0b11111, false),
            new MayhemFinishResult(3, 0b1, false), new MayhemFinishResult(4, MayhemShowPlan.AllEpisodesMask, true),
        });
        Assert.Equal(new ushort[] { 5, 6, 1, 18 }, result.Players.Select(p => p.Ratings));
        Assert.Equal(new byte[] { 2, 1, 3, 0 }, result.Players.Select(p => result.PlaceFor(p.EntityId)));
        var tie = new MayhemRunResult(new[] { new MayhemFinishResult(1, 0b11, false), new MayhemFinishResult(2, 0b101, false) });
        Assert.Equal(new byte[] { 1, 1 }, tie.Players.Select(p => tie.PlaceFor(p.EntityId)));
        var entries = result.Players.Select(p => new MatchResultEntry(p.EntityId, result.PlaceFor(p.EntityId), 0, 0)).ToArray();
        var packet = new MatchResultPacket(6600, false, entries, mayhem: result);
        var bytes = new byte[packet.WireSize]; packet.Serialize(bytes);
        Assert.True(MatchResultPacket.TryDeserialize(bytes, out var decoded));
        Assert.Equal(result.Players, decoded!.Mayhem!.Players);
        Assert.Equal(result.Players.Select(p => p.Ratings), decoded.Mayhem.Players.Select(p => p.Ratings));
        for (int length = 10 + entries.Length * 11 + 1; length < bytes.Length; length++)
            Assert.False(MatchResultPacket.TryDeserialize(bytes.AsSpan(0, length), out _));
        var badFlag = bytes.ToArray(); badFlag[^1] = 2;
        Assert.False(MatchResultPacket.TryDeserialize(badFlag, out _));
        var outOfRange = bytes.ToArray(); outOfRange[^2] = 0x80;
        Assert.False(MatchResultPacket.TryDeserialize(outOfRange, out _));
        var wrongPlace = entries.ToArray(); wrongPlace[0] = new MatchResultEntry(1, 1, 0, 0);
        Assert.Throws<ArgumentException>(() => new MatchResultPacket(1, false, wrongPlace, mayhem: result));
        Assert.Throws<ArgumentException>(() => new MatchResultPacket(1, false, entries, run: new(1, 1, 1), mayhem: result));
    }
}
