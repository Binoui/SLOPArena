using System.Buffers.Binary;
using System.Reflection;
using SlopArena.Shared.AI;
using SlopArena.Shared.Replays;
using Xunit;
namespace SlopArena.Shared.Tests.Replays;

public class ReplayTests
{
    static readonly MatchContentCatalog Catalog = new(new[]{
BuiltInContentResolver.Resolve(CharacterClass.FightGuy),
BuiltInContentResolver.Resolve(CharacterClass.Manki)});
    static string Root
    {
        get
        {
            var p = new DirectoryInfo(AppContext.BaseDirectory);
            while (p != null && !File.Exists(Path.Combine(p.FullName, "SlopArena.sln"))) p = p.Parent;
            return p!.FullName;
        }
    }
    static (ReplayHeader Header, ArenaDefinition Arena, ServerSimulation Sim) Setup(MatchFormat format = MatchFormat.Stock)
    {
        string name = format switch
        {
            MatchFormat.Stock => "slop_pit",
            MatchFormat.SlopballCoop or MatchFormat.SlopballVersus => "slopball",
            MatchFormat.MankiMayhem => "manki_mayhem",
            _ => "fightguy_pier"
        };
        var arena = ArenaBinaryFormat.LoadFromFile(Path.Combine(Root, "data/arenas/" + name + ".arena"))!.Value;
        var roster = Catalog.Entries.Select((e, i) => new ReplayParticipant((ulong)i + 1, "cpu", i.ToString(), e.Identity, "base", format == MatchFormat.SlopballVersus ? (i == 0 ? "teal" : "clay") : "", i.ToString())).ToArray();
        var h = new ReplayHeader("tests", name, new(format.ToWire(), "off", 250, false), 1, roster, ReplayBuildIdentity.Current("tests", "hash"));
        var env = Env(h, arena);
        var camps = roster.ToDictionary(p => p.EntityId, p => p.CampId == "teal" ? SlopballCamp.Teal : p.CampId == "clay" ? SlopballCamp.Clay : SlopballCamp.None);
        var sim = new ServerSimulation(arena, format.CreateRule(h.Rules.MaxStocks, FriendlyFireMode.Off, camps, false));
        foreach (var p in roster)
        {
            var entry = Catalog.ResolvePackage(p.Package.PackageId)!;
            sim.RegisterEntity(p.EntityId, entry.Definition, new CharacterState { EntityId = p.EntityId }, entry.BakedAnimation);
        }
        switch (format)
        {
            case MatchFormat.SlopballCoop:
                sim.BeginSlopballRun(7);
                break;
            case MatchFormat.SlopballVersus:
                sim.BeginVersusSlopball();
                break;
            case MatchFormat.CourseRace:
            case MatchFormat.CourseCoop:
            case MatchFormat.CourseScramble:
                sim.BeginOnlineCourse();
                break;
            case MatchFormat.MankiMayhem:
                sim.BeginMankiMayhem(7);
                break;
        }
        return (h, arena, sim);
    }
    static ReplayEnvironment Env(ReplayHeader h, ArenaDefinition a) => new("hash", Catalog, new Dictionary<string, ArenaDefinition>{
{
h.ArenaId,
a}
});
    static WorldFramePacket Frame(ServerSimulation s, MatchState phase = MatchState.Playing) => new(phase, s.CaptureSnapshot(), Array.Empty<RelayedInput>(), s.GetPresentationEvents().ToArray(), s.LastTickHits.ToArray(), s.LastTickSlopballEvents.Select(e => new SlopballTickEvent(s.CurrentTick, e)).ToArray(), s.LastTickOnlineCourseEvents, s.LastTickMayhemEvents);
    static (byte[] Bytes, ReplayHeader Header, ArenaDefinition Arena, List<byte[]> Snapshots) Record(MatchFormat format = MatchFormat.Stock, int ticks = 125)
    {
        var (h, a, s) = Setup(format);
        using var stream = new MemoryStream();
        using var w = new ReplayWriter(stream, h);
        var c = new ReplayCapture(h, Catalog, a);
        var snapshots = new List<byte[]>();
        for (int i = 0; i < ticks; i++)
        {
            s.Tick(new());
            snapshots.Add(s.CaptureSnapshot().Data);
            w.Append(c.Capture(s, Frame(s)).Capture!);
        }
        w.Complete(new(ReplayEndReason.Cancelled, "test", null));
        return (stream.ToArray(), h, a, snapshots);
    }
    sealed class Sink : IReplaySink
    {
        public int Resets;
        public List<(ReplayDisplay Display, ReplayDelivery Delivery)> Frames = new();
        public void Reset(uint t)
        {
            Resets++;
        }
        public void Present(ReplayDisplay d, ReplayDelivery delivery) => Frames.Add((d, delivery));
    }
    sealed class ExactSink(List<byte[]> snapshots) : IReplaySink
    {
        public int Count;
        public void Reset(uint t)
        {
        }
        public void Present(ReplayDisplay d, ReplayDelivery delivery)
        {
            Assert.Equal(snapshots[(int)d.View.Tick - 1], d.ExactSimulation!.CaptureSnapshot().Data);
            Count++;
        }
    }
    [Theory]
    [InlineData(MatchFormat.Stock)]
    [InlineData(MatchFormat.SlopballCoop)]
    [InlineData(MatchFormat.SlopballVersus)]
    [InlineData(MatchFormat.CourseRace)]
    [InlineData(MatchFormat.CourseCoop)]
    [InlineData(MatchFormat.CourseScramble)]
    [InlineData(MatchFormat.MankiMayhem)]
    public void ExactRestoresEverySnapshot(MatchFormat format)
    {
        var r = Record(format);
        using var stream = new MemoryStream(r.Bytes);
        var opened = ReplayReader.Open(stream);
        Assert.Null(opened.Failure);
        using var reader = opened.Reader!;
        var result = reader.CreatePlayer(Env(r.Header, r.Arena));
        Assert.Null(result.Failure);
        var sink = new ExactSink(r.Snapshots);
        while (result.Player!.Step(sink).Status == ReplayStep.Presented)
        {
        }
        Assert.Equal(125, sink.Count);
    }
    static List<(int Position, int Total)> Chunks(byte[] b)
    {
        var list = new List<(int, int)>();
        int p = 24 + BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(12));
        while (BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(p)) == ReplayStorage.ChunkMagic)
        {
            int n = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(p + 4));
            list.Add((p, n));
            p += n;
        }
        return list;
    }
    [Fact]
    public void EveryLastChunkCutAndZeroTailPreservesCommittedPrefix()
    {
        var r = Record();
        var chunks = Chunks(r.Bytes);
        int pos = chunks[^1].Position;
        for (int n = 0; n < chunks[^1].Total; n++)
        {
            using var s = new MemoryStream(r.Bytes[..(pos + n)]);
            var o = ReplayReader.Open(s);
            Assert.Null(o.Failure);
            using var reader = o.Reader!;
            Assert.Equal(120u, reader.Range!.Value.Last);
            Assert.Null(reader.Conclusion);
            Assert.Equal(n == 0 ? ReplayPrefixReason.CleanEofWithoutTrailer : ReplayPrefixReason.TruncatedTail, reader.PrefixReason);
        }
        var zero = r.Bytes[..pos].Concat(new byte[5000]).ToArray();
        using var zs = new MemoryStream(zero);
        using var zr = ReplayReader.Open(zs).Reader!;
        Assert.Equal(120u, zr.Range!.Value.Last);
        Assert.Equal(ReplayPrefixReason.CorruptTail, zr.PrefixReason);
    }
    [Fact]
    public void ExactGatesAndNewerFileVersionGiveTypedReasons()
    {
        var r = Record();
        using var s = new MemoryStream(r.Bytes);
        using var reader = ReplayReader.Open(s).Reader!;
        Assert.Equal(ReplayFailureCode.ContentHashMismatch, reader.CreatePlayer(Env(r.Header, r.Arena) with
        {
            MatchContentHash = "other"
        }).Failure!.Code);
        var b = r.Bytes.ToArray();
        b[10] = 2;
        using var newer = new MemoryStream(b);
        Assert.Equal(ReplayFailureCode.PleaseUpdate, ReplayReader.Open(newer).Failure!.Code);
        foreach (var pair in new[]{
(r.Header.Build with{
Protocol=999}
,ReplayFailureCode.ProtocolMismatch),
(r.Header.Build with{
FrameFormat=999}
,ReplayFailureCode.FrameFormatMismatch),
(r.Header.Build with{
SnapshotFormat=999}
,ReplayFailureCode.SnapshotFormatMismatch)}) Assert.Equal(pair.Item2, ReplayExact.Check(r.Header with
{
    Build = pair.Item1
}
      , Env(r.Header, r.Arena))!.Code);
        Assert.Equal(ReplayFailureCode.ArenaIdentityMismatch, ReplayExact.Check(r.Header with
        {
            ArenaDigest = new string('a', 64)
        }
      , Env(r.Header, r.Arena))!.Code);
        var roster = r.Header.Roster.ToArray();
        roster[0] = roster[0] with
        {
            Package = roster[0].Package with
            {
                Version = "other"
            }
        };
        Assert.Equal(ReplayFailureCode.PackageIdentityMismatch, ReplayExact.Check(r.Header with
        {
            Roster = roster
        }
        , Env(r.Header, r.Arena))!.Code);
    }
    [Fact]
    public void MembershipIgnoresBeforeGoRepeatAndAfterSeal()
    {
        var (h, a, s) = Setup();
        var c = new ReplayCapture(h, Catalog, a);
        Assert.Equal(ReplayCaptureStatus.Ignored, c.Capture(s, Frame(s, MatchState.Countdown)).Status);
        s.Tick(new());
        Assert.Equal(ReplayCaptureStatus.Accepted, c.Capture(s, Frame(s)).Status);
        Assert.Equal(ReplayCaptureStatus.Ignored, c.Capture(s, Frame(s)).Status);
        s.Tick(new());
        s.Tick(new());
        var seal = c.Capture(s, Frame(s));
        Assert.Equal(ReplayCaptureStatus.Sealed, seal.Status);
        Assert.Equal(1u, seal.LastAcceptedTick);
        Assert.Equal(ReplayCaptureStatus.Ignored, c.Capture(s, Frame(s)).Status);
    }
    [Fact]
    public void EndedAndOutOfOrderSealWithoutAccepting()
    {
        foreach (bool ended in new[]{
false,
true})
        {
            var (h, a, s) = Setup();
            var c = new ReplayCapture(h, Catalog, a);
            for (int i = 0; i < 2; i++)
            {
                s.Tick(new());
                c.Capture(s, Frame(s));
            }
            if (!ended) s.SetTick(0);
            var result = c.Capture(s, Frame(s, ended ? MatchState.Ended : MatchState.Playing));
            Assert.Equal(ReplayCaptureStatus.Sealed, result.Status);
            Assert.Equal(2u, result.LastAcceptedTick);
        }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EventsAreSingleTickAndBackwardSeekStartsNewEpoch(bool exact)
    {
        var (h, a, s) = Setup();
        using var stream = new MemoryStream();
        using var w = new ReplayWriter(stream, h);
        var c = new ReplayCapture(h, Catalog, a);
        var history = new WorldFrameHistory();
        for (int i = 0; i < 65; i++)
        {
            s.Tick(new());
            var e = new TimelinePresentationEvent(s.CurrentTick, 1, 0, "presentation.test", 1, PresentationEventSource.Timeline, 0, 0, 0, 0);
            history.Record(s.CurrentTick, new[]{
e}
            , s.LastTickHits);
            var f = new WorldFramePacket(MatchState.Playing, s.CaptureSnapshot(), Array.Empty<RelayedInput>(), history.EventsForFrame(s.CurrentTick).ToArray(), history.HitsForFrame(s.CurrentTick).ToArray());
            w.Append(c.Capture(s, f).Capture!);
        }
        w.Complete(new(ReplayEndReason.Finished, "", null));
        using var reader = ReplayReader.Open(stream).Reader!;
        var player = reader.CreatePlayer(exact ? Env(h, a) : null).Player!;
        var sink = new Sink();
        while (player.Step(sink).Status == ReplayStep.Presented)
        {
        }
        Assert.Equal(65, sink.Frames.Count);
        Assert.All(sink.Frames, f => Assert.Single(f.Display.View.Events));
        Assert.Equal(ReplayStep.End, player.Step(sink).Status);
        sink.Frames.Clear();
        Assert.Null(player.Seek(40, sink));
        Assert.Equal(31, sink.Frames.Count);
        Assert.Equal(30u, sink.Frames[0].Display.TicksBeforeTarget);
        Assert.All(sink.Frames, f => Assert.Equal(ReplayDelivery.Warmup, f.Delivery));
        Assert.Equal(ReplayStep.Presented, player.Step(sink).Status);
        Assert.Equal(41u, sink.Frames[^1].Display.View.Tick);
        Assert.Equal(2, sink.Resets);
        Assert.Single(sink.Frames.Where(f => f.Delivery == ReplayDelivery.Live && f.Display.View.Tick == 41));
        Assert.Null(player.Seek(40, sink));
        Assert.Equal(3, sink.Resets);
        sink.Frames.Clear();
        Assert.Equal(ReplayStep.Presented, player.Step(sink).Status);
        Assert.Single(sink.Frames.Where(f => f.Delivery == ReplayDelivery.Live && f.Display.View.Tick == 41));
        Assert.Null(player.Seek(20, sink));
        Assert.Equal(4, sink.Resets);
        Assert.Equal(ReplayStep.Presented, player.Step(sink).Status);
        Assert.Equal(21u, sink.Frames[^1].Display.View.Tick);
    }
    [Fact]
    public void EveryReleasedGoldenPlaysViewWithoutCurrentContent()
    {
        var files = Directory.GetFiles(Path.Combine(Root, "tests/Shared.Tests/Golden/Replays"), "*.slopreplay");
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            using var s = File.OpenRead(file);
            var o = ReplayReader.Open(s);
            Assert.Null(o.Failure);
            using var r = o.Reader!;
            var player = r.CreatePlayer().Player!;
            var sink = new Sink();
            ReplayStepResult result;
            do
            {
                result = player.Step(sink);
                Assert.Null(result.Failure);
            }
            while (result.Status == ReplayStep.Presented);
            Assert.Equal((int)(r.Range!.Value.Last - r.Range.Value.First + 1), sink.Frames.Count);
        }
    }
    [Fact]
    public void ReleasedGoldenEncodingIsByteIdentical()
    {
        using var stream = File.OpenRead(Path.Combine(Root, "tests/Shared.Tests/Golden/Replays/v1-stock-bots.slopreplay"));
        using var reader = ReplayReader.Open(stream).Reader!;
        // Released goldens are checked through the View Track only: their Exact frames belong to the
        // build that recorded them, and a later protocol rightly refuses to decode them.
        var record = typeof(ReplayReader).GetMethod("Record", BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (uint tick = reader.Range!.Value.First; tick <= reader.Range.Value.Last; tick++)
        {
            var original = (byte[])record.Invoke(reader, new object[] { tick, false })!;
            Assert.Equal(original, ReplayViewCodec.Encode(reader.ReadView(tick).Tick!));
        }
    }
    [Fact]
    public void FieldConformanceExercisesEveryTagAndMissingDefault()
    {
        var tables = typeof(ReplayViewCodec).GetFields(BindingFlags.Static | BindingFlags.NonPublic).Where(f => f.Name.EndsWith("Table"));
        Assert.Equal(typeof(ReplayViewTick).Assembly.GetTypes().Count(t => t.Namespace == typeof(ReplayViewTick).Namespace && !t.IsAbstract && t.GetProperties().Any(p => p.SetMethod?.IsAssembly == true)), tables.Count());
        foreach (var tableField in tables)
        {
            var t = tableField.FieldType.GetGenericArguments()[0];
            var table = tableField.GetValue(null)!;
            var fields = (System.Collections.IEnumerable)table.GetType().GetField("Fields", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(table)!;
            var entries = fields.Cast<object>().ToArray();
            string Name(object f) => (string)f.GetType().GetProperty("Name")!.GetValue(f)!;
            ushort Tag(object f) => (ushort)f.GetType().GetProperty("Tag")!.GetValue(f)!;
            var props = t.GetProperties().Where(p => !(t == typeof(ReplayViewTick) && p.Name == "Tick")).ToArray();
            Assert.Equal(props.Select(p => p.Name).OrderBy(x => x), entries.Select(Name).OrderBy(x => x));
            Assert.Equal(entries.Length, entries.Select(Tag).Distinct().Count());
            var write = typeof(ReplayViewCodec).GetMethod("Write" + (t.BaseType == typeof(ReplayEvent) ? "ReplayEvent" : t.BaseType == typeof(ReplayModeObject) ? "ReplayModeObject" : t.Name), BindingFlags.Static | BindingFlags.NonPublic)!;
            var read = typeof(ReplayViewCodec).GetMethod("Read" + (t.BaseType == typeof(ReplayEvent) ? "ReplayEvent" : t.BaseType == typeof(ReplayModeObject) ? "ReplayModeObject" : t.Name), BindingFlags.Static | BindingFlags.NonPublic)!;
            byte[] Encode(object x) => (byte[])write.Invoke(null, new[] { x })!;
            var defaults = Encode(Activator.CreateInstance(t, true)!);
            foreach (var p in props)
            {
                ushort tag = Tag(entries.Single(f => Name(f) == p.Name));
                var x = Activator.CreateInstance(t, true)!;
                bool kind = p.Name == "Kind" && (t.BaseType == typeof(ReplayEvent) || t.BaseType == typeof(ReplayModeObject));
                if (!kind) p.SetValue(x, Nondefault(p.PropertyType));
                var encoded = Encode(x);
                if (!kind) Assert.NotEqual(defaults, encoded);
                Assert.Equal(encoded, Encode(read.Invoke(null, new object[] { encoded })!));
                var absent = ReplayBinary.Bytes(w => { foreach (var f in ReplayBinary.Fields(encoded)) if (f.Key != tag) ReplayBinary.Field(w, f.Key, f.Value); });
                if (p.Name == "Kind" && (t.BaseType == typeof(ReplayEvent) || t.BaseType == typeof(ReplayModeObject))) Assert.Throws<TargetInvocationException>(() => read.Invoke(null, new object[] { absent }));
                else Assert.Equal(defaults, Encode(read.Invoke(null, new object[] { absent })!));
            }
        }
    }
    static object Nondefault(Type t)
    {
        if (t == typeof(string)) return "test";
        if (t == typeof(bool)) return true;
        if (t == typeof(float)) return 2.5f;
        if (t == typeof(ReplayVector)) return new ReplayVector(1, 2, 3);
        if (t == typeof(byte)) return (byte)3;
        if (t == typeof(ushort)) return (ushort)3;
        if (t == typeof(ulong)) return 3UL;
        if (t == typeof(uint)) return 3u;
        if (t == typeof(int)) return 3;
        if (t.IsGenericType)
        {
            var e = t.GetGenericArguments()[0];
            var array = Array.CreateInstance(e, 1);
            array.SetValue(Activator.CreateInstance(e == typeof(ReplayEvent) ? typeof(ReplayPresentationEvent) : e == typeof(ReplayModeObject) ? typeof(ReplayBall) : e, true), 0);
            return array;
        }
        var obj = Activator.CreateInstance(t, true)!;
        t.GetProperty("BoneId")?.SetValue(obj, "test");
        return obj;
    }
    [Fact]
    public void DecimalByteLimitDropsWholePendingChunk()
    {
        Assert.Equal(64_000_000, ReplayStorage.MaxFileBytes);
        var (h, a, s) = Setup();
        using var stream = new MemoryStream();
        using var writer = new ReplayWriter(stream, h);
        var binding = ReplayStorage.Preamble(h);
        var random = new Random(7);
        uint tick = 0;
        ReplayAppend result;
        do
        {
            tick++;
            var bytes = new byte[62000];
            random.NextBytes(bytes);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, ServerSimulation.SnapshotFormatVersion);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), tick);
            var packet = new WorldFramePacket(MatchState.Playing, new WorldSnapshot(bytes), Array.Empty<RelayedInput>(), Array.Empty<TimelinePresentationEvent>(), Array.Empty<SpellResolver.HitResult>());
            result = writer.Append(new ReplayTickCapture(packet, new ReplayViewTick
            {
                Tick = tick
            }
            , binding));
        }
        while (result != ReplayAppend.LimitReached);
        var end = writer.Complete(new(ReplayEndReason.Finished, "", null));
        Assert.True(end.BytesWritten <= 64_000_000);
        Assert.Equal(ReplayEndReason.Limit, end.Conclusion.Reason);
        Assert.Equal(tick - 60, end.LastTick);
        Assert.Equal(0u, end.LastTick % 60);
        using var reader = ReplayReader.Open(stream).Reader!;
        Assert.Equal(end.LastTick, reader.Range!.Value.Last);
        Assert.Equal(ReplayEndReason.Limit, reader.Conclusion!.Reason);
    }
    [Fact]
    public void WireCodecStillMatchesRawBody()
    {
        var (_, _, s) = Setup();
        s.Tick(new());
        var f = Frame(s);
        var raw = f.SerializeUncompressedForReplay();
        var wire = f.Serialize();
        Assert.Equal(f.Snapshot.Data, WorldFramePacket.Deserialize(raw).Snapshot.Data);
        Assert.Equal(WorldFramePacket.Deserialize(wire).SerializeUncompressedForReplay(), raw);
        Assert.Equal(0, raw[6]);
    }
    [Fact]
    public void CorruptMiddleChunkStopsWithoutResynchronizing()
    {
        var r = Record(ticks: 185);
        var c = Chunks(r.Bytes);
        r.Bytes[c[1].Position + 44 + 60 * 16] ^= 1;
        using var s = new MemoryStream(r.Bytes);
        var o = ReplayReader.Open(s);
        Assert.Null(o.Failure);
        using var reader = o.Reader!;
        Assert.Equal(60u, reader.Range!.Value.Last);
        Assert.Equal(ReplayPrefixReason.CorruptTail, reader.PrefixReason);
        Assert.Null(reader.Conclusion);
    }
    [Fact]
    public void HeaderOnlyCrashAndEmptyConclusionHaveNoFabricatedTick()
    {
        var (h, _, _) = Setup();
        using var s = new MemoryStream();
        using (var writer = new ReplayWriter(s, h))
        {
        }
        using var reader = ReplayReader.Open(s).Reader!;
        Assert.Null(reader.Range);
        Assert.Null(reader.Conclusion);
        Assert.Equal(ReplayPrefixReason.CleanEofWithoutTrailer, reader.PrefixReason);
        Assert.Equal(ReplayFailureCode.EmptyReplay, reader.CreatePlayer().Failure!.Code);
        using var e = new MemoryStream();
        using var w = new ReplayWriter(e, h);
        var conclusion = new ReplayConclusion(ReplayEndReason.Cancelled, "before-go", null);
        var end = w.Complete(conclusion);
        Assert.Null(end.LastTick);
        Assert.Equal(end, w.Complete(conclusion));
        Assert.Throws<InvalidOperationException>(() => w.Complete(conclusion with
        {
            Detail = "conflict"
        }));
        using var er = ReplayReader.Open(e).Reader!;
        Assert.Null(er.Range);
        Assert.Equal(conclusion, er.Conclusion);
    }
    [Fact]
    public void RejectedSeekDoesNotResetOrAdvance()
    {
        var r = Record();
        using var s = new MemoryStream(r.Bytes);
        using var reader = ReplayReader.Open(s).Reader!;
        var player = reader.CreatePlayer().Player!;
        var sink = new Sink();
        Assert.Null(player.Seek(50, sink));
        int resets = sink.Resets;
        Assert.Equal(ReplayFailureCode.TickOutOfRange, player.Seek(999, sink)!.Code);
        Assert.Equal(resets, sink.Resets);
        Assert.Equal(50u, player.CurrentTick);
        Assert.Equal(ReplayStep.Presented, player.Step(sink).Status);
        Assert.Equal(51u, player.CurrentTick);
    }
    [Fact]
    public void UnknownExactVersionsRemainOpaqueInViewMode()
    {
        var r = Record();
        using var original = new MemoryStream(r.Bytes);
        using var reader = ReplayReader.Open(original).Reader!;
        var header = r.Header with
        {
            Build = r.Header.Build with
            {
                Protocol = 999,
                FrameFormat = 999,
                SnapshotFormat = 999,
                ContentHash = "past"
            }
        };
        var before = ReplayStorage.Preamble(r.Header);
        var after = ReplayStorage.Preamble(header);
        using var changed = new MemoryStream(after.Concat(r.Bytes.Skip(before.Length)).ToArray());
        var o = ReplayReader.Open(changed);
        Assert.Null(o.Failure);
        using var view = o.Reader!;
        Assert.Null(view.ReadView(1).Failure);
        Assert.Equal(ReplayFailureCode.ProtocolMismatch, view.CreatePlayer(Env(r.Header, r.Arena)).Failure!.Code);
    }
    [Fact]
    public void CaptureOwnsSnapshotAndHistoryCollections()
    {
        var (h, a, s) = Setup();
        s.Tick(new());
        var snapshot = s.CaptureSnapshot();
        var events = new List<TimelinePresentationEvent>{
new(s.CurrentTick,1,0,"test",1,PresentationEventSource.Timeline,0,0,0,0)};
        var inputs = new List<RelayedInput> { new(1, true, default) };
        var f = new WorldFramePacket(MatchState.Playing, snapshot, inputs, events, s.LastTickHits);
        var captured = new ReplayCapture(h, Catalog, a).Capture(s, f).Capture!;
        var expected = snapshot.Data.ToArray();
        Array.Clear(snapshot.Data);
        events.Clear();
        inputs.Clear();
        Assert.Single(captured.Packet.Inputs);
        Assert.Equal(expected, captured.Packet.Snapshot.Data);
        Assert.Single(captured.Packet.Events);
        Assert.Single(captured.View.Events);
    }
    [Fact]
    public void MalformedTagsWidthsAndNonfiniteNumbersReject()
    {
        Assert.Throws<InvalidDataException>(() => ReplayViewCodec.ReadReplayFighter(ReplayBinary.Bytes(w =>
        {
            ReplayBinary.Field(w, 1, new byte[8]);
            ReplayBinary.Field(w, 1, new byte[8]);
        })));
        Assert.Throws<InvalidDataException>(() => ReplayViewCodec.ReadReplayFighter(ReplayBinary.Bytes(w => ReplayBinary.Field(w, 6, BitConverter.GetBytes(float.NaN)))));
        Assert.Throws<InvalidDataException>(() => ReplayViewCodec.ReadReplayFighter(ReplayBinary.Bytes(w => ReplayBinary.Field(w, 7, new byte[] { 2 }))));
        Assert.Throws<InvalidDataException>(() => ReplayViewCodec.ReadReplayEvent(Array.Empty<byte>()));
    }

    [Fact]
    public void SemanticMappingsCoverEveryEnumUniquelyAndUnknownIsTotal()
    {
        foreach (var method in typeof(ReplaySemanticIds).GetMethods(BindingFlags.Static | BindingFlags.NonPublic).Where(m => m.Name == "Id"))
        {
            var type = method.GetParameters()[0].ParameterType;
            var ids = Enum.GetValues(type).Cast<object>().Select(v => (string)method.Invoke(null, new[] { v })!).ToArray();
            Assert.DoesNotContain("unknown", ids);
            Assert.Equal(ids.Length, ids.Distinct().Count());
            Assert.All(ids, id => Assert.NotNull(id));
            Assert.Equal("unknown", method.Invoke(null, new[] { Enum.ToObject(type, 255) }));
        }
        Assert.Equal("unknown", ReplaySemanticIds.Form(255));
    }
    [Fact]
    public void FirstChunkAndTrailerCrashesRemainPartial()
    {
        var r = Record();
        var chunks = Chunks(r.Bytes);
        int start = chunks[0].Position;
        for (int cut = start; cut < start + chunks[0].Total; cut++)
        {
            using var stream = new MemoryStream(r.Bytes[..cut]);
            var result = ReplayReader.Open(stream);
            Assert.Null(result.Failure);
            using var reader = result.Reader!;
            Assert.Null(reader.Range);
            Assert.Equal(cut == start ? ReplayPrefixReason.CleanEofWithoutTrailer : ReplayPrefixReason.TruncatedTail, reader.PrefixReason);
        }
        start = chunks[^1].Position + chunks[^1].Total;
        for (int cut = start; cut < r.Bytes.Length; cut++)
        {
            using var stream = new MemoryStream(r.Bytes[..cut]);
            var result = ReplayReader.Open(stream);
            Assert.Null(result.Failure);
            using var reader = result.Reader!;
            Assert.Equal(125u, reader.Range!.Value.Last);
            Assert.Null(reader.Conclusion);
            Assert.Equal(cut == start ? ReplayPrefixReason.CleanEofWithoutTrailer : ReplayPrefixReason.TruncatedTail, reader.PrefixReason);
        }
    }
    sealed class FailingStream : MemoryStream
    {
        public bool Fail;
        public bool FailFlush;
        public override void Write(byte[] buffer, int offset, int count) { if (Fail) throw new IOException("injected"); base.Write(buffer, offset, count); }
        public override void Flush() { if (Fail || FailFlush) throw new IOException("injected"); base.Flush(); }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WriterFailureLatchesAcrossAppendAndComplete(bool onComplete)
    {
        var (h,a,s) = Setup();
        using var stream = new FailingStream();
        using var writer = new ReplayWriter(stream, h);
        var capture = new ReplayCapture(h, Catalog, a);
        ReplayTickCapture tick = null!;
        for (int i = 0; i < (onComplete ? 1 : 60); i++)
        {
            s.Tick(new()); tick = capture.Capture(s, Frame(s)).Capture!;
            if (!onComplete && i == 59) stream.Fail = true;
            var result = writer.Append(tick);
            if (!onComplete && i == 59) Assert.Equal(ReplayAppend.Stopped, result);
        }
        if (onComplete) stream.FailFlush = true; else stream.Fail = true;
        var end = writer.Complete(new(ReplayEndReason.Finished, "", null));
        Assert.True(end.Stopped);
        Assert.Contains("writer-failed:", end.Conclusion.Detail);
        stream.Fail = false;
        Assert.Equal(ReplayAppend.Stopped, writer.Append(tick));
        Assert.Equal(end, writer.Complete(new(ReplayEndReason.Cancelled, "other", null)));
    }
    [Fact]
    public void ProjectionFailureSealsWithoutThrowing()
    {
        var (h,a,s) = Setup();
        s.Tick(new());
        var capture = new ReplayCapture(h, Catalog, a);
        s.SetTick(2);
        var result = capture.Capture(s, new WorldFramePacket(MatchState.Playing, new WorldSnapshot(Frame(s).Snapshot.Data.Select((b,i) => i == 4 ? (byte)1 : b).ToArray()), Array.Empty<RelayedInput>(), Array.Empty<TimelinePresentationEvent>(), Array.Empty<SpellResolver.HitResult>()));
        Assert.Equal(ReplayCaptureStatus.Sealed, result.Status);
        Assert.StartsWith("projection-failed:", result.Detail);
    }

    [Fact]
    public void WireBytesMatchPreReplayCodecGolden()
    {
        // Fixture generated with the archived 2bece1f0 Shared assembly, independently of this codec.
        var bytes = new byte[256]; new Random(7).NextBytes(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, ServerSimulation.SnapshotFormatVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 42);
        var frame = new WorldFramePacket(MatchState.Playing, new WorldSnapshot(bytes), Array.Empty<RelayedInput>(), new[] { new TimelinePresentationEvent(42, 1, 3, "presentation.golden", 2, PresentationEventSource.Timeline, 1, 2, 3, .5f) }, Array.Empty<SpellResolver.HitResult>());
        var expected = File.ReadAllBytes(Path.Combine(Root, "tests/Shared.Tests/Golden/Replays/world-frame-2bece1f0.bin"));
        var actual = frame.Serialize();
        // Header byte 5 is SimulationProtocol.Version, which releases bump. DEFLATE output differs
        // between zlib builds (CI vs workstation), so the body is compared after inflating.
        expected[5] = SimulationProtocol.Version;
        Assert.Equal(expected.AsSpan(0, WorldFramePacket.HeaderSize).ToArray(), actual.AsSpan(0, WorldFramePacket.HeaderSize).ToArray());
        Assert.Equal(WireBody(expected), WireBody(actual));
    }
    static byte[] WireBody(byte[] frame)
    {
        var body = frame.AsSpan(WorldFramePacket.HeaderSize).ToArray();
        if ((frame[6] & 1) == 0) return body;
        using var inflate = new System.IO.Compression.DeflateStream(new MemoryStream(body), System.IO.Compression.CompressionMode.Decompress);
        using var output = new MemoryStream();
        inflate.CopyTo(output);
        return output.ToArray();
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CpuCombatProjectsFullMoveIdentityIncludingAlternateNyx(bool nyx)
    {
        var entries = new[] { BuiltInContentResolver.Resolve(nyx ? CharacterClass.Nyx : CharacterClass.FightGuy), BuiltInContentResolver.Resolve(CharacterClass.Manki) };
        var catalog = new MatchContentCatalog(entries);
        var arena = ArenaBinaryFormat.LoadFromFile(Path.Combine(Root, "data/arenas/slop_pit.arena"))!.Value;
        var roster = entries.Select((e,i) => new ReplayParticipant((ulong)i+1, "cpu", i.ToString(), e.Identity, nyx && i == 0 ? "alternate" : "base", "", "")).ToArray();
        var header = new ReplayHeader("combat", arena.Name, new("stock", "off", 250, false), 1, roster, ReplayBuildIdentity.Current("test", "test"));
        var sim = new ServerSimulation(arena, MatchFormat.Stock.CreateRule(250, FriendlyFireMode.Off));
        for (int i = 0; i < 2; i++)
        {
            var spawn = arena.SpawnPoints[i];
            var state = new CharacterState { EntityId = (ulong)i+1, PX = spawn.X, PY = spawn.Y, PZ = spawn.Z, FacingYaw = spawn.Yaw, IsGrounded = true, JumpsLeft = entries[i].Definition.Movement.MaxJumps, AirDodgesLeft = 1, DamagePercent = 300 };
            if (nyx && i == 0) state.Form.Active = 1;
            sim.RegisterEntity(state.EntityId, entries[i].Definition, state, entries[i].BakedAnimation);
        }
        var capture = new ReplayCapture(header, catalog, arena);
        var policy = new HeuristicBotPolicy(); var rng = new Random(7);
        var memories = new[] { new BotMemory { Difficulty = CpuDifficulty.Normal }, new BotMemory { Difficulty = CpuDifficulty.Normal } };
        var lastHits = new Dictionary<ulong, SpellResolver.HitResult>();
        int moves = 0, shapes = 0, hits = 0, deaths = 0, alternate = 0;
        void Identity(ulong owner, byte form, byte slot, bool air, string package, string formId, string slotId, string name)
        {
            var entry = entries[(int)owner-1];
            var move = entry.Definition.ForForm(form).GetCookedSlotAbility(slot, air)!;
            Assert.Equal(entry.Identity.PackageId, package);
            Assert.Equal(form == 0 ? "base" : "alternate", formId);
            Assert.Equal(move.Id, slotId); Assert.Equal(move.Name, name);
        }
        for (int tick = 0; tick < 3600; tick++)
        {
            var inputs = new Dictionary<ulong, InputState>();
            for (int i = 0; i < 2; i++)
            {
                ulong id = (ulong)i+1, other = (ulong)(1-i)+1;
                policy.ObserveOpponent(memories[i], sim.GetState(other), sim.GetDefinition(other), entries[1-i].BakedAnimation, sim.GetActiveMove(other)?.Slot);
                memories[i].ChooseOpponent(sim.GetState(id));
                inputs[id] = policy.Decide(sim.GetState(id), sim.GetDefinition(id), rng, memories[i], arena, entries[i].BakedAnimation, activeMove: sim.GetActiveMove(id)?.Slot);
            }
            sim.Tick(inputs);
            foreach (var hit in sim.LastTickHits) lastHits[hit.TargetEntityId] = hit;
            // Intentionally omit all frame hits: projection must use uncapped authoritative live contacts.
            var frame = new WorldFramePacket(MatchState.Playing, sim.CaptureSnapshot(), Array.Empty<RelayedInput>(), sim.GetPresentationEvents(true).ToArray(), Array.Empty<SpellResolver.HitResult>());
            var result = capture.Capture(sim, frame);
            Assert.Equal(ReplayCaptureStatus.Accepted, result.Status);
            var view = result.Capture!.View;
            foreach (var f in view.Fighters.Where(f => f.SlotId != ""))
            {
                var move = sim.GetActiveMove(f.EntityId)!.Value;
                Assert.Equal(move.Slot.Name, f.MoveName); Assert.Equal(move.Slot.Id, f.SlotId);
                Assert.Equal(entries[(int)f.EntityId-1].Identity.PackageId, f.PackageId);
                Assert.Equal(ReplaySemanticIds.Form(move.Slot.Form), f.MoveFormId);
                moves++; if (f.EntityId == 1 && f.MoveFormId == "alternate") alternate++;
            }
            foreach (var shape in view.Shapes.Where(h => h.SlotId != ""))
            {
                var live = sim.Resolver.GetActiveHitboxes().Single(h => h.EmissionId == shape.EmissionId);
                Identity(shape.OwnerId, live.Form, live.AttackSlot, live.ActivationAirborne, shape.PackageId, shape.FormId, shape.SlotId, shape.MoveName); shapes++;
            }
            for (int i = 0; i < view.Hits.Count; i++)
            {
                var h = view.Hits[i]; var live = sim.LastTickHits[i];
                if (live.AttackSlot == 0) continue;
                Identity(h.OwnerId, live.Form, live.AttackSlot, live.Airborne, h.PackageId, h.FormId, h.SlotId, h.MoveName);
                Assert.Equal(live.HitPresentationId ?? "", h.PresentationId); hits++;
            }
            foreach (var d in view.Deaths)
            {
                if (!lastHits.TryGetValue(d.EntityId, out var live) || live.AttackSlot == 0) continue;
                Identity(d.LastHitOwnerId, live.Form, live.AttackSlot, live.Airborne, d.LastHitPackageId, d.LastHitFormId, d.LastHitSlotId, d.LastHitMoveName); deaths++;
            }
            Assert.All(view.Fighters.SelectMany(f => f.Cooldowns), c => { Assert.True(c.TicksLeft > 0); Assert.True(c.DurationTicks >= c.TicksLeft); });
        }
        Assert.True(moves > 0, "moves"); Assert.True(shapes > 0, "shapes"); Assert.True(hits > 0, "hits"); Assert.True(deaths > 0, "deaths");
        if (nyx) Assert.True(alternate > 0);
    }

    [Fact]
    public void ExactEncodingFailureStopsWriterAtSixtyAndNeverRetries()
    {
        var (h,_,_) = Setup();
        using var stream = new MemoryStream();
        using var writer = new ReplayWriter(stream, h);
        long initial = stream.Length;
        for (uint tick = 1; tick <= 60; tick++)
        {
            var bytes = new byte[70000];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, ServerSimulation.SnapshotFormatVersion);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), tick);
            var packet = new WorldFramePacket(MatchState.Playing, new WorldSnapshot(bytes), Array.Empty<RelayedInput>(), Array.Empty<TimelinePresentationEvent>(), Array.Empty<SpellResolver.HitResult>());
            var capture = new ReplayTickCapture(packet, new ReplayViewTick { Tick = tick }, ReplayStorage.Preamble(h));
            Assert.Equal(tick < 60 ? ReplayAppend.BufferedOrCommitted : ReplayAppend.Stopped, writer.Append(capture));
            Assert.Equal(initial, stream.Length);
        }
        var end = writer.Complete(new(ReplayEndReason.Finished, "", null));
        Assert.True(end.Stopped);
        Assert.Contains("writer-failed:", end.Conclusion.Detail);
        Assert.Equal(end, writer.Complete(new(ReplayEndReason.Cancelled, "", null)));
    }

    [Fact]
    public void EncodingBoundsStopWriterAtLastCommittedTick()
    {
        var (h,a,s) = Setup();
        var capture = new ReplayCapture(h, Catalog, a);
        using var stream = new MemoryStream();
        using var writer = new ReplayWriter(stream, h);
        for (int i = 0; i < 61; i++)
        {
            s.Tick(new());
            Assert.Equal(ReplayAppend.BufferedOrCommitted, writer.Append(capture.Capture(s, Frame(s)).Capture!));
        }
        s.Tick(new());
        var frame = new WorldFramePacket(MatchState.Playing, s.CaptureSnapshot(), Array.Empty<RelayedInput>(), new[] { new TimelinePresentationEvent(s.CurrentTick, 1, 0, new string('x', 4097), 1, PresentationEventSource.Timeline, 0, 0, 0, 0) }, Array.Empty<SpellResolver.HitResult>());
        var result = capture.Capture(s, frame);
        Assert.Equal(ReplayCaptureStatus.Accepted, result.Status);
        Assert.Equal(ReplayAppend.Stopped, writer.Append(result.Capture!));
        var end = writer.Complete(new(ReplayEndReason.Finished, "", null));
        Assert.True(end.Stopped);
        Assert.Equal(60u, end.LastTick);
        Assert.Equal(ReplayAppend.Stopped, writer.Append(result.Capture!));
        Assert.Equal(end, writer.Complete(new(ReplayEndReason.Cancelled, "", null)));
        Assert.Throws<InvalidDataException>(() => ReplayViewCodec.Encode(new ReplayViewTick { Fighters = new ReplayFighter[5] }));
    }

    [Fact]
    public void CooldownsRecordOnlyRunningSlotsAndRetainObservedDuration()
    {
        var (h,a,s) = Setup();
        var capture = new ReplayCapture(h, Catalog, a);
        var state = s.GetState(1); state.SetCooldown(AbilitySlots.Normal1, 60); s.SetState(1, state);
        s.Tick(new());
        var first = capture.Capture(s, Frame(s)).Capture!.View.Fighters[0].Cooldowns;
        Assert.NotEmpty(first);
        Assert.All(first, c => { Assert.Equal((ushort)59, c.TicksLeft); Assert.Equal(c.TicksLeft, c.DurationTicks); Assert.Equal(h.Roster[0].Package.PackageId, c.PackageId); Assert.Equal("base", c.FormId); Assert.NotEmpty(c.SlotId); Assert.NotEmpty(c.MoveName); });
        s.Tick(new());
        var next = capture.Capture(s, Frame(s)).Capture!.View.Fighters[0].Cooldowns;
        Assert.All(next, c => { Assert.Equal((ushort)58, c.TicksLeft); Assert.Equal((ushort)59, c.DurationTicks); });
        state = s.GetState(1); state.SetCooldown(AbilitySlots.Normal1, 0); s.SetState(1, state);
        s.Tick(new());
        Assert.Empty(capture.Capture(s, Frame(s)).Capture!.View.Fighters[0].Cooldowns);
    }

}
