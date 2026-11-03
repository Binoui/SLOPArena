using System.Diagnostics;
using System.Reflection;
using SlopArena.Server;
using SlopArena.Shared;
using SlopArena.Shared.Replays;
using Xunit;
using Xunit.Abstractions;

namespace SlopArena.Server.Tests;

public class MatchReplayTests(ITestOutputHelper output)
{
    static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.Parse("2026-11-02T12:00:00Z");
    }
    sealed class Rig : IDisposable
    {
        public readonly string Dir = Path.Combine(Path.GetTempPath(), "slop-replay-" + Guid.NewGuid());
        public readonly MatchContentCatalog Catalog;
        public readonly MatchContentHandleMap Map;
        public readonly ArenaDefinition Arena;
        public readonly string Hash;
        public Rig()
        {
            ArenaRegistry.LoadFromDirectory(Path.Combine(Root, "data/arenas"));
            Assert.True(ArenaRegistry.TryGetMatchArena("slop_pit", out Arena, out var identity));
            var provider = new MatchContentCatalogProvider();
            Assert.True(provider.TryBuild(out var catalog, out var map, out var error), error);
            Catalog = catalog!; Map = map!.WithArena(identity!); Hash = SteamMatchDescriptor.HashContent(Map);
        }
        public MatchInstance Match(MatchReplayStore? store, Action<Guid, ulong, byte[], bool>? send = null, string? displayName = null) => new(0,
            Guid.NewGuid().ToString(), Arena, new[] { new MatchPlayer(1001, CharacterClass.FightGuy, 1, Colour: 2, DisplayName: displayName),
                new MatchPlayer(1002, CharacterClass.Manki, 2, Colour: 3) }, Catalog, _ => { },
            maxStocks: 1, contentHash: Hash, contentHandleMap: Map, clock: new Clock(),
            steamSend: send ?? ((_, _, _, _) => { }), replays: store);
        public void Dispose() { if (Directory.Exists(Dir)) Directory.Delete(Dir, true); }
    }
    static void Set(MatchInstance match, string name, object value) => typeof(MatchInstance).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(match, value);
    static ServerSimulation Sim(MatchInstance match) => (ServerSimulation)typeof(MatchInstance).GetField("_sim", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(match)!;
    static void Playing(MatchInstance match)
    {
        match.InitializeSimulation();
        Set(match, "_matchState", MatchState.Countdown);
        Set(match, "_countdownTicks", (ushort)1);
        match.Tick(); // GO is the first Playing snapshot.
    }
    static ReplayReader Read(string path)
    {
        // Reader borrows this in-memory stream; no filesystem handles escape the test.
        var opened = ReplayReader.Open(new MemoryStream(File.ReadAllBytes(path)));
        Assert.Null(opened.Failure); return opened.Reader!;
    }
    sealed class Sink(Action<ReplayDisplay> present) : IReplaySink
    {
        public int Count;
        public void Reset(uint tick) { }
        public void Present(ReplayDisplay display, ReplayDelivery delivery) { Count++; present(display); }
    }
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("Bi\0n\noui\t\u007f\u0085", "Binoui")]
    [InlineData("abcdefghijklmnopqrstuvwxy", "abcdefghijklmnopqrstuvwx")]
    [InlineData("abcdefghijklmnopqrstuvw🐸x", "abcdefghijklmnopqrstuvw🐸")]
    [InlineData("🐸🐸🐸🐸🐸🐸🐸🐸🐸🐸🐸🐸🐸🐸🐸🐸🐸🐸🐸🐸🐸🐸🐸🐸🐸", "🐸🐸🐸🐸🐸🐸🐸🐸🐸🐸🐸🐸🐸🐸🐸🐸🐸🐸🐸🐸🐸🐸🐸🐸")]
    public void MatchCaptureBoundsDisplayNameAtTheHeaderBoundary(string? name, string expected)
    {
        using var rig = new Rig();
        var store = new MatchReplayStore(rig.Dir);
        var match = rig.Match(store, displayName: name);
        Playing(match);
        match.Stop();
        Assert.True(store.WaitForWriters(TimeSpan.FromSeconds(5)));
        using var reader = Read(Assert.Single(Directory.GetFiles(rig.Dir, "*.slopreplay", SearchOption.AllDirectories)));
        Assert.Equal(expected, reader.Header.Roster[0].DisplayName);
        Assert.Equal("", reader.Header.Roster[1].DisplayName);
    }

    [Fact]
    public void MatchRecordsViewAndEveryExactTickWithTheWireResult()
    {
        using var rig = new Rig();
        var store = new MatchReplayStore(rig.Dir);
        var frames = new Dictionary<uint, byte[]>();
        MatchResultPacket? result = null;
        var match = rig.Match(store, (_, id, bytes, _) =>
        {
            if (id != 1) return;
            if (bytes[0] == SteamGameplayWire.Frame)
            {
                var frame = WorldFramePacket.Deserialize(bytes.AsSpan(1));
                if (frame.MatchState == MatchState.Playing) frames[frame.Tick] = frame.Snapshot.Data;
            }
            if (bytes[0] == SteamGameplayWire.Result) Assert.True(MatchResultPacket.TryDeserialize(bytes.AsSpan(1), out result));
        });
        Assert.True(match.TryBindSteamPlayer(1001, 11, rig.Hash, out _, out _, out _));
        Assert.True(match.TryBindSteamPlayer(1002, 22, rig.Hash, out _, out _, out _));
        Playing(match);
        for (int i = 0; i < 130; i++) match.Tick();
        var state = Sim(match).GetState(2); state.Deaths = 1; Sim(match).SetState(2, state);
        match.Tick();
        Assert.True(store.WaitForWriters(TimeSpan.FromSeconds(5)));
        using var reader = Read(Assert.Single(Directory.GetFiles(rig.Dir, "*.slopreplay", SearchOption.AllDirectories)));
        Assert.NotNull(result);
        Assert.Equal(ReplayEndReason.Finished, reader.Conclusion!.Reason);
        Assert.Equal(new ulong[] { 1 }, reader.Conclusion.Result!.WinnerIds);
        foreach (var entry in result!.Entries)
        {
            var standing = reader.Conclusion.Result.Standings.Single(s => s.EntityId == entry.EntityId);
            Assert.Equal(entry.Placement, standing.Place); Assert.Equal(entry.Falls, standing.Deaths); Assert.Equal(entry.KOs, standing.KOs);
        }
        Assert.Equal("1001", reader.Header.Roster[0].ParticipantSubject);
        Assert.Equal("2", reader.Header.Roster[0].ColourId);
        Assert.Equal(rig.Map.Arena!.Digest, reader.Header.ArenaDigest);
        var view = reader.CreatePlayer(); Assert.Null(view.Failure);
        var viewSink = new Sink(d => Assert.Null(d.ExactSimulation));
        while (view.Player!.Step(viewSink).Status == ReplayStep.Presented) { }
        var exact = reader.CreatePlayer(new(rig.Hash, rig.Catalog, new Dictionary<string, ArenaDefinition> { [rig.Arena.Name] = rig.Arena },
            new Dictionary<string, string> { [rig.Arena.Name] = rig.Map.Arena.Digest }));
        Assert.Null(exact.Failure);
        var exactSink = new Sink(d => Assert.Equal(frames[d.View.Tick], d.ExactSimulation!.CaptureSnapshot().Data));
        while (exact.Player!.Step(exactSink).Status == ReplayStep.Presented) { }
        Assert.Equal(frames.Count, exactSink.Count); Assert.Equal(frames.Count, viewSink.Count);

        // A ranked shared victory must not turn the lower-ranked fighter into a winner.
        var tie = new MatchResultPacket(1, true,
            new[] { new MatchResultEntry(1, 1, 0, 0), new MatchResultEntry(2, 1, 0, 0), new MatchResultEntry(3, 3, 0, 0) },
            mayhem: new MayhemRunResult(new[] {
                new MayhemFinishResult(1, MankiMayhemTuning.SurvivalPoints, 1, 0, false),
                new MayhemFinishResult(2, MankiMayhemTuning.SurvivalPoints, 1, 0, false),
                new MayhemFinishResult(3, 0, 0, 0, false) }));
        Set(match, "_matchResultPacket", tie);
        Set(match, "_format", MatchFormat.MankiMayhem);
        Set(match, "_winnerEntityId", 0UL);
        var tiedResult = (ReplayMatchResult)typeof(MatchInstance).GetMethod("ReplayResult", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(match, null)!;
        Assert.Equal(new ulong[] { 1, 2 }, tiedResult.WinnerIds);
        Assert.True(tiedResult.Draw);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RealMatchLoopCompletesOrCancelsRecording(bool failLoop)
    {
        using var rig = new Rig();
        var store = new MatchReplayStore(rig.Dir);
        MatchInstance match = null!;
        using var finished = new ManualResetEventSlim();
        var clock = new Clock();
        var sentTicks = new HashSet<uint>();
        MatchResultPacket? wireResult = null;
        bool sawPlaying = false;
        match = new MatchInstance(0, Guid.NewGuid().ToString(), rig.Arena,
            new[] { new MatchPlayer(1001, CharacterClass.FightGuy, 1), new MatchPlayer(1002, CharacterClass.Manki, 2) },
            rig.Catalog, _ => finished.Set(), maxStocks: 1, contentHash: rig.Hash, contentHandleMap: rig.Map,
            clock: clock, replays: store,
            steamSend: (_, id, bytes, _) =>
            {
                if (id != 1) return;
                if (bytes[0] == SteamGameplayWire.Result)
                    Assert.True(MatchResultPacket.TryDeserialize(bytes.AsSpan(1), out wireResult));
                if (bytes[0] != SteamGameplayWire.Frame) return;
                var frame = WorldFramePacket.Deserialize(bytes.AsSpan(1));
                if (frame.MatchState != MatchState.Playing) return;
                sawPlaying = true;
                sentTicks.Add(frame.Tick);
                // Test setup runs on the Match thread through the transport seam.
                if (sentTicks.Count == 65 && !failLoop)
                {
                    var state = Sim(match).GetState(2); state.Deaths = 1; Sim(match).SetState(2, state);
                }
            },
            steamFlush: () => { if (failLoop && sawPlaying) throw new IOException("test loop failure"); });
        foreach (var (steam, conn, entity) in new[] { (1001UL, 11L, 1UL), (1002UL, 22L, 2UL) })
        {
            Assert.True(match.TryBindSteamPlayer(steam, conn, rig.Hash, out _, out _, out _));
            Assert.True(match.TryMarkSteamReady(conn, new(NetplayControlKind.Ready, entity, 0, 0)));
        }
        try
        {
            match.Start();
            Assert.True(finished.Wait(TimeSpan.FromSeconds(13)), "Real Match loop did not end.");
        }
        finally { match.Stop(); Assert.True(match.WaitForStop(TimeSpan.FromSeconds(2))); }
        Assert.True(store.WaitForWriters(TimeSpan.FromSeconds(5)));
        using var reader = Read(Assert.Single(Directory.GetFiles(rig.Dir, "*.slopreplay", SearchOption.AllDirectories)));
        Assert.Equal(sentTicks.Count, (int)(reader.Range!.Value.Last - reader.Range.Value.First + 1));
        if (failLoop)
        {
            Assert.Equal(ReplayEndReason.Cancelled, reader.Conclusion!.Reason);
            Assert.Equal(MatchInstance.MatchFailedReason, reader.Conclusion.Detail);
        }
        else
        {
            Assert.NotNull(wireResult);
            Assert.Equal(ReplayEndReason.Finished, reader.Conclusion!.Reason);
            Assert.Equal(wireResult!.Entries.Select(e => e.EntityId), reader.Conclusion.Result!.Standings.Select(s => s.EntityId));
            Assert.Equal(new ulong[] { 1 }, reader.Conclusion.Result.WinnerIds);
        }
    }
    [Theory]
    [InlineData("stopped")]
    [InlineData("host_restart")]
    [InlineData(MatchInstance.MatchFailedReason)]
    public void StopDrainsCancelledPrefixEvenWithNoPeers(string reason)
    {
        using var rig = new Rig(); var store = new MatchReplayStore(rig.Dir);
        var match = rig.Match(store); Playing(match);
        for (int i = 0; i < 10; i++) match.Tick();
        match.Stop(reason);
        Assert.True(store.WaitForWriters(TimeSpan.FromSeconds(5)));
        using var reader = Read(Assert.Single(Directory.GetFiles(rig.Dir, "*.slopreplay", SearchOption.AllDirectories)));
        Assert.Equal(ReplayEndReason.Cancelled, reader.Conclusion!.Reason); Assert.Equal(reason, reader.Conclusion.Detail);
        Assert.Equal(11u, reader.Range!.Value.Last);
        Assert.True(match.ReplayCaptureCalls >= 11);
    }
    [Fact]
    public void BlockedWriterSealsBacklogAndLeavesTicksAndWireUnchanged()
    {
        using var rig = new Rig(); using var gate = new ManualResetEventSlim();
        var store = new MatchReplayStore(rig.Dir, open: path => { gate.Wait(); return File.Create(path); });
        var recordedWire = new List<byte[]>(); var offWire = new List<byte[]>();
        var on = rig.Match(store, (_, id, bytes, _) => { if (id == 1) recordedWire.Add(bytes); });
        var off = rig.Match(null, (_, id, bytes, _) => { if (id == 1) offWire.Add(bytes); });
        foreach (var m in new[] { on, off }) Assert.True(m.TryBindSteamPlayer(1001, 11, rig.Hash, out _, out _, out _));
        Playing(off); Playing(on);
        var offTimes = new List<double>(); var onTimes = new List<double>();
        try
        {
            for (int i = 0; i < 700; i++)
            {
                var sw = Stopwatch.StartNew(); off.Tick(); offTimes.Add(sw.Elapsed.TotalMilliseconds);
                sw.Restart(); on.Tick(); onTimes.Add(sw.Elapsed.TotalMilliseconds);
            }
            Assert.Equal(off.ServerTick, on.ServerTick);
            Assert.Equal(offWire.Count, recordedWire.Count);
            for (int i = 0; i < offWire.Count; i++) Assert.Equal(offWire[i], recordedWire[i]);
            Assert.Equal(0, off.ReplayCaptureCalls);
            Assert.Equal(601, on.ReplayCaptureCalls);
            var offSample = offTimes.Skip(100).Take(499).Order().ToArray();
            var onSample = onTimes.Skip(100).Take(499).Order().ToArray();
            output.WriteLine($"Paired Match.Tick (499 accepting ticks after 100 warmup; blocked writer): mean off={offSample.Average():F6}ms on={onSample.Average():F6}ms; median off={offSample[249]:F6}ms on={onSample[249]:F6}ms delta={onSample[249]-offSample[249]:F6}ms; max on={onSample.Max():F3}ms");
        }
        finally { gate.Set(); on.Stop(); off.Stop(); Assert.True(store.WaitForWriters(TimeSpan.FromSeconds(5))); }
        using var reader = Read(Assert.Single(Directory.GetFiles(rig.Dir, "*.slopreplay", SearchOption.AllDirectories)));
        Assert.Equal(ReplayEndReason.Limit, reader.Conclusion!.Reason); Assert.Equal("backlog", reader.Conclusion.Detail);
        Assert.Equal(600u, reader.Range!.Value.Last);
    }
    [Fact]
    public void IoFailureDoesNotStopTheMatch()
    {
        using var rig = new Rig(); var store = new MatchReplayStore(rig.Dir, open: _ => throw new IOException("disk full"));
        var match = rig.Match(store); Playing(match);
        Assert.True(store.WaitForWriters(TimeSpan.FromSeconds(5)));
        for (int i = 0; i < 100; i++) match.Tick();
        Assert.True(match.IsRunning); Assert.Equal(101u, match.ServerTick);
        Assert.Contains(match.Diagnostics.Snapshot().Events, e => e.Message.Contains("io-failure: disk full"));
    }
    sealed class FailingFile(string path, ManualResetEventSlim committed) : FileStream(path, FileMode.CreateNew)
    {
        public volatile bool Fail;
        int writes;
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (Fail) throw new IOException("disk full during append");
            base.Write(buffer, offset, count);
            if (++writes == 7) { Flush(); committed.Set(); }
        }
    }
    [Fact]
    public void DiskFullDuringAppendLeavesReadablePartAndContinuesMatch()
    {
        using var rig = new Rig(); using var committed = new ManualResetEventSlim();
        FailingFile file = null!;
        var store = new MatchReplayStore(rig.Dir, open: path => file = new(path, committed));
        var match = rig.Match(store); Playing(match);
        for (int i = 0; i < 59; i++) match.Tick();
        Assert.True(committed.Wait(TimeSpan.FromSeconds(5)));
        file.Fail = true;
        for (int i = 0; i < 60; i++) match.Tick();
        Assert.True(store.WaitForWriters(TimeSpan.FromSeconds(5)));
        for (int i = 0; i < 10; i++) match.Tick();
        Assert.True(match.IsRunning);
        using var reader = Read(Assert.Single(Directory.GetFiles(rig.Dir, "*.part", SearchOption.AllDirectories)));
        Assert.Equal(60u, reader.Range!.Value.Last); Assert.Null(reader.Conclusion);
        Assert.Contains(match.Diagnostics.Snapshot().Events, e => e.Message.Contains("disk full during append"));
    }
    [Fact]
    public void DisabledDoesNotCaptureOrCreateFiles()
    {
        using var rig = new Rig(); var match = rig.Match(null); Playing(match);
        for (int i = 0; i < 100; i++) match.Tick();
        match.Stop();
        Assert.Equal(0, match.ReplayCaptureCalls); Assert.False(Directory.Exists(rig.Dir));
    }
    [Fact]
    public void CompletionRunsRetention()
    {
        using var rig = new Rig(); var store = new MatchReplayStore(rig.Dir, maxBytes: 1);
        var match = rig.Match(store); Playing(match); match.Stop();
        Assert.True(store.WaitForWriters(TimeSpan.FromSeconds(5)));
        Assert.Empty(Directory.GetFiles(rig.Dir, "*", SearchOption.AllDirectories));
    }
    [Fact]
    public void RetentionRemovesOldestIncludingAbandonedParts()
    {
        using var rig = new Rig(); Directory.CreateDirectory(rig.Dir);
        void FileAt(string name, int age) { string path = Path.Combine(rig.Dir, name); File.WriteAllBytes(path, new byte[10]); File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-age)); }
        FileAt("expired.slopreplay.part", 31); FileAt("old.slopreplay", 3); FileAt("new.slopreplay", 1);
        _ = new MatchReplayStore(rig.Dir, maxBytes: 10);
        Assert.Equal("new.slopreplay", Path.GetFileName(Assert.Single(Directory.GetFiles(rig.Dir))));
    }
}
