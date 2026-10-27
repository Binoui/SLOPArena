using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SlopArena.Shared;
using SlopArena.Shared.Rollback;
using Xunit;
using Xunit.Abstractions;

namespace SlopArena.Shared.Tests;

public class MayhemOnlineCodecTests
{
    private readonly ITestOutputHelper _output;
    public MayhemOnlineCodecTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void SeededMayhemFramesCarryAllEventsAndStayWithinTheFrameBudget()
    {
        var sim = MankiMayhemTests.NewMatch(41, roster: true);
        var history = new WorldFrameHistory();
        var kinds = new HashSet<MankiMayhemEventKind>();
        int maxSnapshot = 0, maxFrame = 0, frames = 0;
        while (!sim.EvaluateMatch().IsEnded && sim.CurrentTick < 18000)
        {
            if (sim.CurrentTick == 20) MankiMayhemTests.Ko(sim, 2);
            if (sim.MayhemWalls.FirstOrDefault(w => w.Blocks) is { EntityId: > 0 } wall)
                sim.Resolver.Spawn(new Hitbox { OwnerId = 1, X = wall.Center.X, Y = 1, Z = wall.Center.Z,
                    Radius = .5f, Damage = 1, DurationTicks = 1 });
            sim.Tick(new Dictionary<ulong, InputState>());
            history.Record(sim.CurrentTick, sim.GetPresentationEvents(true), sim.LastTickHits,
                mayhemEvents: sim.LastTickMayhemEvents);
            var frame = new WorldFramePacket(MatchState.Playing, sim.CaptureSnapshot(), Array.Empty<RelayedInput>(),
                history.EventsForFrame(sim.CurrentTick), history.HitsForFrame(sim.CurrentTick),
                mayhemEvents: history.MayhemEventsForFrame(sim.CurrentTick));
            var bytes = frame.Serialize();
            var decoded = WorldFramePacket.Deserialize(bytes);
            Assert.Equal(frame.MayhemEvents, decoded.MayhemEvents);
            foreach (var evt in decoded.MayhemEvents) kinds.Add(evt.Kind);
            maxSnapshot = Math.Max(maxSnapshot, frame.Snapshot.Data.Length);
            maxFrame = Math.Max(maxFrame, bytes.Length); frames++;
        }
        Assert.Equal(Enum.GetValues<MankiMayhemEventKind>().Order(), kinds.Order());
        Assert.True(sim.EvaluateMatch().IsEnded);
        Assert.True(maxFrame < WorldFramePacket.MaxBodyBytes);
        _output.WriteLine($"four fighters, seed 41: {frames} frames, max snapshot {maxSnapshot} bytes, max compressed frame {maxFrame} bytes");
    }

    [Fact]
    public void MayhemWireRejectsMalformedEventsAndOversizedHistory()
    {
        var sim = MankiMayhemTests.NewMatch(); sim.Tick(new());
        var call = Assert.Single(sim.LastTickMayhemEvents);
        WorldFramePacket Frame(params MankiMayhemEvent[] events) => new(MatchState.Playing, sim.CaptureSnapshot(),
            Array.Empty<RelayedInput>(), Array.Empty<TimelinePresentationEvent>(), Array.Empty<SpellResolver.HitResult>(), mayhemEvents: events);
        foreach (var invalid in new[] { call with { Kind = (MankiMayhemEventKind)255 }, call with { EntityId = 1 },
            call with { Kind = MankiMayhemEventKind.Bench, EntityId = RuntimeSolidSet.FirstEntityId },
            call with { MatchTick = 100 }, call with { RoundIndex = ushort.MaxValue }, call with { Points = 1 }, call with { OperationIndex = -1 } })
            Assert.Throws<ArgumentOutOfRangeException>(() => Frame(invalid).Serialize());
        Assert.Throws<ArgumentOutOfRangeException>(() => Frame(Enumerable.Repeat(call, WorldFramePacket.MaxMayhemEvents + 1).ToArray()));
        var wire = Frame(call).Serialize(); wire[4]--;
        Assert.False(WorldFramePacket.TryDeserialize(wire, out _));
    }

    [Fact]
    public void MayhemResultRoundTripsTiesWithdrawalsAndScoresAndRejectsTruncation()
    {
        var result = new MayhemRunResult(new[] { new MayhemFinishResult(1, 7, 6, 1, false),
            new MayhemFinishResult(2, 7, 6, 1, false), new MayhemFinishResult(3, 8, 5, 3, true) });
        var entries = result.Players.Select(p => new MatchResultEntry(p.EntityId, result.PlaceFor(p.EntityId), 0, 1)).ToArray();
        var packet = new MatchResultPacket(2600, true, entries, mayhem: result);
        var bytes = new byte[packet.WireSize]; packet.Serialize(bytes);
        Assert.True(MatchResultPacket.TryDeserialize(bytes, out var decoded));
        Assert.Equal(result.Players, decoded!.Mayhem!.Players);
        Assert.Equal(new byte[] { 1, 1, 0 }, decoded.Entries.Select(e => e.Placement));
        for (int length = 10 + entries.Length * 11 + 1; length < bytes.Length; length++)
            Assert.False(MatchResultPacket.TryDeserialize(bytes.AsSpan(0, length), out _));
        bytes[^1] = 2;
        Assert.False(MatchResultPacket.TryDeserialize(bytes, out _));
        Assert.Throws<ArgumentException>(() => new MatchResultPacket(1, false, entries, run: new(1, 1, 1), mayhem: result));
    }
}
