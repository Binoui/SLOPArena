using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace SlopArena.Shared.Tests;

/// <summary>
/// The protocol-10 per-tick frame (ADR-0039, plan phases 2–3): World Snapshot, relayed inputs and
/// the last three ticks' presentation events and hits, compressed, with a decoder that rejects
/// malformed input rather than crashing or allocating without bound.
/// </summary>
public class WorldFramePacketTests
{
    private readonly ITestOutputHelper _output;
    public WorldFramePacketTests(ITestOutputHelper output) => _output = output;

    private static readonly CharacterClass[] Duel = { CharacterClass.Manki, CharacterClass.Wibou };
    private static readonly CharacterClass[] FourWay =
        { CharacterClass.FightGuy, CharacterClass.Manki, CharacterClass.Wibou, CharacterClass.Bonk };

    [Fact]
    public void FramesRoundTripAndRestoreThroughTheWire()
    {
        var arena = WorldSnapshotTests.LoadArena("table_manners");
        var frames = Fight(arena, Duel, seed: 3, ticks: 1500, out var server);
        int withEvents = 0, withHits = 0, compressed = 0;
        foreach (var (frame, bytes) in frames)
        {
            var decoded = WorldFramePacket.Deserialize(bytes);
            Assert.Equal(frame.Tick, decoded.Tick);
            Assert.Equal(frame.MatchState, decoded.MatchState);
            Assert.True(frame.Snapshot.ContentEquals(decoded.Snapshot));
            Assert.Equal(frame.Inputs, decoded.Inputs);
            Assert.Equal(frame.Events, decoded.Events);
            Assert.Equal(frame.Hits, decoded.Hits);
            Assert.True(WorldFramePacket.TryPeekTick(bytes, out uint peeked));
            Assert.Equal(frame.Tick, peeked);
            if (frame.Events.Count > 0) withEvents++;
            if (frame.Hits.Count > 0) withHits++;
            if ((bytes[6] & 1) != 0) compressed++;
        }
        Assert.True(withEvents >= 30, $"frames with events {withEvents}");
        Assert.True(withHits >= 9, $"frames with hits {withHits}");
        Assert.True(compressed > frames.Count / 2, $"compressed frames {compressed}");

        // A client restored from the last decoded frame holds the GameHost's exact state.
        var client = WorldSnapshotTests.NewMatch(arena, Duel, reverseRegistration: true);
        client.RestoreSnapshot(WorldFramePacket.Deserialize(frames[^1].Bytes).Snapshot);
        Assert.True(client.CaptureSnapshot().ContentEquals(server.CaptureSnapshot()));
    }

    [Fact]
    public void UncompressedBodiesDecodeLikeCompressedOnes()
    {
        var arena = WorldSnapshotTests.LoadArena("slop_pit");
        foreach (var (frame, bytes) in Fight(arena, Duel, seed: 8, ticks: 300, out _).Where((_, i) => i % 25 == 0))
        {
            var raw = AsRaw(bytes);
            var decoded = WorldFramePacket.Deserialize(raw);
            Assert.True(frame.Snapshot.ContentEquals(decoded.Snapshot));
            Assert.Equal(frame.Inputs, decoded.Inputs);
            Assert.Equal(frame.Events, decoded.Events);
            Assert.Equal(frame.Hits, decoded.Hits);
        }
    }

    [Fact]
    public void EachFrameCarriesTheCompleteRecordOfItsLastThreeTicks()
    {
        var arena = WorldSnapshotTests.LoadArena("table_manners");
        var frames = Fight(arena, Duel, seed: 3, ticks: 1500, out _);
        var eventsByTick = frames.SelectMany(f => f.Frame.Events).GroupBy(e => e.MatchTick)
            .ToDictionary(g => g.Key, g => g.Distinct().Count());
        var hitsByTick = frames.SelectMany(f => f.Frame.Hits).GroupBy(h => h.MatchTick)
            .ToDictionary(g => g.Key, g => g.Distinct().Count());
        Assert.NotEmpty(hitsByTick);
        foreach (var (frame, _) in frames)
        {
            Assert.Equal(frame.Tick >= 2 ? frame.Tick - 2 : 0, frame.EventsFromTick);
            for (uint tick = frame.EventsFromTick; tick <= frame.Tick; tick++)
            {
                Assert.Equal(eventsByTick.GetValueOrDefault(tick), frame.Events.Count(e => e.MatchTick == tick));
                Assert.Equal(hitsByTick.GetValueOrDefault(tick), frame.Hits.Count(h => h.MatchTick == tick));
            }
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void FramesStayWithinTheBandwidthBudget(int fighters)
    {
        // Budget (plan phase 2): 64 KB/s typical and 160 KB/s peak per client at 60 Hz.
        var arena = WorldSnapshotTests.LoadArena(fighters == 4 ? "last_stop" : "slop_pit");
        var roster = fighters == 4 ? FourWay : Duel;
        var sizes = Fight(arena, roster, seed: 31, ticks: 3000, out _).Select(f => f.Bytes.Length).OrderBy(x => x).ToList();
        int median = sizes[sizes.Count / 2], p95 = sizes[sizes.Count * 95 / 100], max = sizes[^1];
        _output.WriteLine($"{fighters} fighters: frame bytes median={median} p95={p95} max={max}; " +
            $"{median * 60 / 1024} KB/s typical, {max * 60 / 1024} KB/s peak");
        Assert.True(median * 60 <= 64 * 1024, $"median frame {median} bytes");
        Assert.True(max * 60 <= 160 * 1024, $"largest frame {max} bytes");
    }

    [Fact]
    public void MalformedHeadersAreRejected()
    {
        var arena = WorldSnapshotTests.LoadArena("slop_pit");
        var bytes = Fight(arena, Duel, seed: 4, ticks: 60, out _)[^1].Bytes;
        Assert.True(WorldFramePacket.TryDeserialize(bytes, out _));

        foreach (var (offset, value) in new (int, byte)[] { (0, 0x00), (4, 99), (5, 1), (6, 0x82), (7, 9) })
        {
            var broken = (byte[])bytes.Clone();
            broken[offset] = value;
            Assert.False(WorldFramePacket.TryDeserialize(broken, out _), $"byte {offset} = {value}");
        }
        var lengthTooBig = (byte[])bytes.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(lengthTooBig.AsSpan(12), WorldFramePacket.MaxBodyBytes + 1);
        Assert.False(WorldFramePacket.TryDeserialize(lengthTooBig, out _));
        var wrongTick = (byte[])bytes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(wrongTick.AsSpan(8), BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8)) + 1);
        Assert.False(WorldFramePacket.TryDeserialize(wrongTick, out _));
        Assert.False(WorldFramePacket.TryDeserialize(bytes.AsSpan(0, WorldFramePacket.HeaderSize - 1), out _));
        Assert.False(WorldFramePacket.TryDeserialize(bytes.AsSpan(0, bytes.Length - 1), out _));
    }

    [Fact]
    public void InvalidPresentationEventsCannotBeSent()
    {
        var sim = WorldSnapshotTests.NewMatch(WorldSnapshotTests.LoadArena("slop_pit"), Duel, false);
        for (int tick = 0; tick < 5; tick++)
            sim.Tick(new Dictionary<ulong, InputState>());
        var snapshot = sim.CaptureSnapshot();
        var noHits = Array.Empty<SpellResolver.HitResult>();
        var noInputs = Array.Empty<RelayedInput>();
        var unnamed = new TimelinePresentationEvent(5, 1, 0, "", 0, PresentationEventSource.Timeline, 0f, 0f, 0f, 0f);
        Assert.ThrowsAny<ArgumentException>(() =>
            new WorldFramePacket(MatchState.Playing, snapshot, noInputs, new[] { unnamed }, noHits).Serialize());
        var tooOld = unnamed with { PresentationId = "presentation.fightguy.normal.hit", MatchTick = 2 };
        Assert.ThrowsAny<ArgumentException>(() =>
            new WorldFramePacket(MatchState.Playing, snapshot, noInputs, new[] { tooOld }, noHits).Serialize());
        var future = new SpellResolver.HitResult { OwnerEntityId = 1, TargetEntityId = 2, MatchTick = 6 };
        Assert.ThrowsAny<ArgumentException>(() =>
            new WorldFramePacket(MatchState.Playing, snapshot, noInputs, Array.Empty<TimelinePresentationEvent>(), new[] { future }).Serialize());
    }

    [Fact]
    public void MutatedFramesAreRejectedOrDecodedButNeverCrash()
    {
        var arena = WorldSnapshotTests.LoadArena("last_stop");
        var frames = Fight(arena, FourWay, seed: 17, ticks: 600, out _);
        var samples = frames.Where((_, i) => i % 20 == 0).Select(f => f.Bytes).ToList();
        samples.AddRange(samples.Select(AsRaw).ToList()); // raw bodies expose the inner decoders
        var target = WorldSnapshotTests.NewMatch(arena, FourWay, reverseRegistration: false);
        var rng = new Random(1234);
        int decoded = 0, rejected = 0, restored = 0;
        for (int i = 0; i < 6000; i++)
        {
            var mutated = Mutate(samples[rng.Next(samples.Count)], rng);
            WorldFramePacket frame;
            try
            {
                frame = WorldFramePacket.Deserialize(mutated);
            }
            catch (InvalidDataException)
            {
                rejected++;
                continue;
            }
            decoded++;
            try
            {
                target.RestoreSnapshot(frame.Snapshot);
                restored++;
            }
            catch (Exception e) when (e is InvalidDataException or InvalidOperationException)
            {
            }
        }
        _output.WriteLine($"mutations: rejected={rejected} decoded={decoded} restored={restored}");
        Assert.True(rejected > 1000 && decoded > 100, $"rejected {rejected}, decoded {decoded}");
    }

    private static byte[] Mutate(byte[] source, Random rng)
    {
        var bytes = (byte[])source.Clone();
        switch (rng.Next(4))
        {
            case 0:
                for (int n = rng.Next(1, 4); n > 0; n--)
                    bytes[rng.Next(bytes.Length)] ^= (byte)(1 << rng.Next(8));
                return bytes;
            case 1:
                bytes[rng.Next(WorldFramePacket.HeaderSize, bytes.Length)] = (byte)rng.Next(256);
                return bytes;
            case 2:
                return bytes.AsSpan(0, rng.Next(bytes.Length)).ToArray();
            default:
                var longer = new byte[bytes.Length + rng.Next(1, 9)];
                bytes.CopyTo(longer, 0);
                rng.NextBytes(longer.AsSpan(bytes.Length));
                return longer;
        }
    }

    /// <summary>The same frame with an uncompressed body.</summary>
    private static byte[] AsRaw(byte[] frame)
    {
        if ((frame[6] & 1) == 0) return frame;
        int length = BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(12));
        var body = new byte[length];
        using (var inflate = new DeflateStream(new MemoryStream(frame, WorldFramePacket.HeaderSize,
                   frame.Length - WorldFramePacket.HeaderSize), CompressionMode.Decompress))
        {
            int read = 0;
            while (read < length) read += inflate.Read(body, read, length - read);
        }
        var raw = new byte[WorldFramePacket.HeaderSize + length];
        frame.AsSpan(0, WorldFramePacket.HeaderSize).CopyTo(raw);
        raw[6] = 0;
        body.CopyTo(raw, WorldFramePacket.HeaderSize);
        return raw;
    }

    /// <summary>A GameHost-like loop: tick, then frame the snapshot, the consumed inputs and the
    /// last three ticks' events and hits.</summary>
    private static List<(WorldFramePacket Frame, byte[] Bytes)> Fight(ArenaDefinition arena, CharacterClass[] roster,
        int seed, int ticks, out ServerSimulation server)
    {
        server = WorldSnapshotTests.NewMatch(arena, roster, reverseRegistration: false);
        var ids = Enumerable.Range(1, roster.Length).Select(i => (ulong)i).ToArray();
        var driver = new WorldSnapshotTests.FightDriver(seed, ids);
        var inputs = new Dictionary<ulong, InputState>();
        var history = new WorldFrameHistory();
        var frames = new List<(WorldFramePacket, byte[])>();
        for (int i = 0; i < ticks; i++)
        {
            driver.Next(server, inputs);
            var consumed = new Dictionary<ulong, InputState>(inputs);
            server.Tick(new Dictionary<ulong, InputState>(inputs));
            var relayed = ids.Select(id => consumed.TryGetValue(id, out var input)
                ? new RelayedInput(id, true, input) : new RelayedInput(id, false, default)).ToArray();
            var snapshot = server.CaptureSnapshot();
            history.Record(snapshot.Tick, server.GetPresentationEvents(clear: true), server.LastTickHits);
            var frame = new WorldFramePacket(MatchState.Playing, snapshot, relayed,
                history.EventsForFrame(snapshot.Tick).ToArray(), history.HitsForFrame(snapshot.Tick).ToArray());
            frames.Add((frame, frame.Serialize()));
        }
        return frames;
    }
}
