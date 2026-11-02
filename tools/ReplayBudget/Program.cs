using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using SlopArena.Shared;
using SlopArena.Shared.AI;

namespace SlopArena.ReplayBudget;

internal static class Program
{
    private static readonly CharacterClass[] Roster = { CharacterClass.FightGuy, CharacterClass.Manki, CharacterClass.Wibou, CharacterClass.Bonk };
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private sealed record Distribution(double Median, double P95, double Min, double Max, int Samples);
    private sealed record Track(string Name, int ChunkTicks, long Bytes, double MBPerMinute, double RatioVsWire,
        Distribution CompressionMsPerMatchSecond, Distribution InflateUs, Distribution DecodeUs, Distribution SeekUs);
    private sealed record Result(int Fighters, string Arena, int Ticks, int Seed, string[] Roster, long WireBytes,
        long SnapshotBytes, long InputBytes, long EnvelopeAndHistoryBytes, long CurrentEvents, long CurrentHits,
        long ActiveShapeTicks, byte[] Deaths, long QuantizedValues, long QuantizationOverflows, double MaxQuantizationError,
        int StringTableBytes, Distribution TickWithBotsUs, Distribution CaptureSerializeUs, Distribution ViewUs, Distribution QuantizedViewUs,
        Distribution RestoreUs, Track[] Tracks);

    private static int Main(string[] args)
    {
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var options = new Dictionary<string, string>();
            for (int i = 0; i < args.Length; i += 2)
            {
                if (i + 1 >= args.Length || args[i] is not ("--ticks" or "--seed" or "--json"))
                    throw new ArgumentException("Usage: ReplayBudget [--ticks positive-int] [--seed int] [--json path]");
                options.Add(args[i], args[i + 1]);
            }
            int ticks = options.TryGetValue("--ticks", out var t) ? int.Parse(t) : 18000;
            int seed = options.TryGetValue("--seed", out var s) ? int.Parse(s) : 31;
            if (ticks < 1) throw new ArgumentOutOfRangeException("--ticks");
            string root = Directory.GetCurrentDirectory();
            while (!File.Exists(Path.Combine(root, "SlopArena.sln")))
                root = Directory.GetParent(root)?.FullName ?? throw new DirectoryNotFoundException("Run inside the repository.");
            var content = Roster.Select(c => BuiltInContentResolver.Resolve(c, Path.Combine(root, "content-cooked"))).ToArray();
            string manifestHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "content-cooked/roster/manifest.json"))));
            // Warm the actual simulation, projection, wire codec, chunk codecs and restore paths before sampling.
            int warmupTicks = Math.Min(ticks, 18000);
            Run(root, content, 2, warmupTicks, seed);
            Run(root, content, 4, warmupTicks, seed);
            var results = new[] { Run(root, content, 2, ticks, seed), Run(root, content, 4, ticks, seed) };
            foreach (var r in results) Print(r);
            var summary = new { SchemaVersion = 1, Runtime = Environment.Version.ToString(), OS = Environment.OSVersion.ToString(),
                LogicalCores = Environment.ProcessorCount, WarmupTicksPerRoster = warmupTicks, Stopwatch.Frequency, WorldFramePacket.FormatVersion,
                SimulationProtocol = SimulationProtocol.Version, RosterManifestSHA256 = manifestHash,
                Compression = "DEFLATE Optimal; int32 record lengths; 12-byte chunk header; decimal MB; 60Hz",
                Timing = "One measured match per roster after an unmeasured match per roster; operation distributions, not independent trial confidence intervals. Restore uses a fresh registered shell per tick, construction excluded. Seek uses a reusable registered shell, construction excluded. Decode includes inflate and field parsing; view decode validates fields without rendering. Disk/network excluded.",
                Caveats = "Normal CPU bots, 250 stocks, four-way target observations; stock mode only, no mode objects. Float32 view prototype is not a playback contract. Quantization uses 0.01 units and float32 escape on overflow. Shape data includes projectiles via resolver. No tuning verdict from one run.", Results = results };
            string json = JsonSerializer.Serialize(summary, JsonOptions);
            Console.WriteLine(json);
            if (options.TryGetValue("--json", out var path)) File.WriteAllText(path, json + Environment.NewLine);
            return 0;
        }
        catch (Exception e) when (e is ArgumentException or IOException or InvalidOperationException or NotSupportedException or OverflowException) { Console.Error.WriteLine(e); return 1; }
    }

    private static ServerSimulation Shell(ArenaDefinition arena, MatchContentEntry[] content, int fighters)
    {
        var sim = new ServerSimulation(arena, new StockMatchRule(250));
        for (int i = 0; i < fighters; i++)
        {
            var spawn = arena.SpawnPoints[i % arena.SpawnPoints.Length];
            var state = new CharacterState { EntityId = (ulong)i + 1, PX = spawn.X + i / arena.SpawnPoints.Length * 1.5f,
                PY = spawn.Y, PZ = spawn.Z, FacingYaw = spawn.Yaw, State = ActionState.Idle, IsGrounded = true,
                JumpsLeft = content[i].Definition.Movement.MaxJumps, AirDodgesLeft = 1 };
            sim.RegisterEntity(state.EntityId, content[i].Definition, state, content[i].BakedAnimation);
        }
        return sim;
    }

    private static Result Run(string root, MatchContentEntry[] content, int fighters, int ticks, int seed)
    {
        string arenaName = fighters == 2 ? "slop_pit" : "last_stop";
        var arena = ArenaBinaryFormat.LoadFromFile(Path.Combine(root, "data/arenas", arenaName + ".arena"))
            ?? throw new InvalidDataException(arenaName);
        var sim = Shell(arena, content, fighters);
        var restore = Shell(arena, content, fighters);
        var rng = new Random(seed);
        var policy = new HeuristicBotPolicy();
        var memories = Enumerable.Range(0, fighters).Select(_ => new BotMemory { Difficulty = CpuDifficulty.Normal }).ToArray();
        var previousDeaths = new byte[fighters];
        var inputs = new Dictionary<ulong, InputState>();
        var history = new WorldFrameHistory();
        var strings = new Strings();
        var view = new ViewProjection(strings);
        var exactFrames = new List<byte[]>(ticks);
        var viewFrames = new List<byte[]>(ticks);
        var quantFrames = new List<byte[]>(ticks);
        var captureTimes = new List<double>(ticks);
        var tickTimes = new List<double>(ticks);
        var viewTimes = new List<double>(ticks);
        var quantTimes = new List<double>(ticks);
        var restoreTimes = new List<double>(ticks);
        long wire = 0, snapshotBytes = 0, inputBytes = 0, historyBytes = 0, events = 0, hits = 0, shapes = 0;
        for (int tick = 0; tick < ticks; tick++)
        {
            long tickStart = Stopwatch.GetTimestamp();
            for (int i = 0; i < fighters; i++)
            {
                ulong id = (ulong)i + 1;
                var self = sim.GetState(id);
                if (self.Deaths != previousDeaths[i]) { memories[i].Reset(); memories[i].Difficulty = CpuDifficulty.Normal; previousDeaths[i] = self.Deaths; }
                for (int j = 0; j < fighters; j++)
                    if (i != j) policy.ObserveOpponent(memories[i], sim.GetState((ulong)j + 1), sim.GetDefinition((ulong)j + 1),
                        content[j].BakedAnimation, sim.GetActiveMove((ulong)j + 1)?.Slot);
                memories[i].ChooseOpponent(self);
                inputs[id] = policy.Decide(self, sim.GetDefinition(id), rng, memories[i], arena, content[i].BakedAnimation,
                    activeMove: sim.GetActiveMove(id)?.Slot);
            }
            var consumed = new Dictionary<ulong, InputState>(inputs);
            sim.Tick(inputs);
            tickTimes.Add(Us(tickStart));
            foreach (var hit in sim.LastTickHits)
                for (int i = 0; i < fighters; i++)
                    if (hit.OwnerEntityId == (ulong)i + 1 && hit.TargetEntityId >= 1 && hit.TargetEntityId <= (ulong)fighters)
                        memories[i].RecordOpponentContact(hit.AttackSlot, hit.Blocked ? CpuContactKind.Block : CpuContactKind.Hit,
                            sim.GetState(hit.TargetEntityId), hit.HitstopTicks);
            long start = Stopwatch.GetTimestamp();
            var relayed = Enumerable.Range(1, fighters).Select(i => new RelayedInput((ulong)i, true, consumed[(ulong)i])).ToArray();
            var snapshot = sim.CaptureSnapshot();
            history.Record(snapshot.Tick, sim.GetPresentationEvents(clear: true), sim.LastTickHits);
            var frame = new WorldFramePacket(MatchState.Playing, snapshot, relayed,
                history.EventsForFrame(snapshot.Tick).ToArray(), history.HitsForFrame(snapshot.Tick).ToArray());
            byte[] bytes = frame.Serialize();
            captureTimes.Add(Us(start));
            wire += bytes.Length;
            var raw = AsRaw(bytes);
            exactFrames.Add(raw);
            snapshotBytes += snapshot.Data.Length;
            long thisInput = 4 + fighters * (9 + InputState.Size);
            inputBytes += thisInput;
            historyBytes += raw.Length - snapshot.Data.Length - thisInput;
            events += frame.Events.Count(e => e.MatchTick == frame.Tick);
            hits += frame.Hits.Count(h => h.MatchTick == frame.Tick);
            shapes += sim.Resolver.GetActiveHitboxes().Count;
            start = Stopwatch.GetTimestamp();
            viewFrames.Add(view.Write(sim, frame, fighters, false));
            viewTimes.Add(Us(start));
            start = Stopwatch.GetTimestamp();
            quantFrames.Add(view.Write(sim, frame, fighters, true));
            quantTimes.Add(Us(start));
            restore = Shell(arena, content, fighters);
            start = Stopwatch.GetTimestamp();
            restore.RestoreSnapshot(snapshot);
            restoreTimes.Add(Us(start));
            if (!restore.CaptureSnapshot().ContentEquals(snapshot)) throw new InvalidDataException("Restore mismatch");
            if (!WorldFramePacket.Deserialize(raw).Snapshot.ContentEquals(snapshot)) throw new InvalidDataException("Raw frame mismatch");
        }
        int tableBytes = strings.Bytes().Length;
        var tracks = new List<Track>();
        foreach (int chunkTicks in new[] { 60, 300 })
        {
            tracks.Add(MeasureChunks("exact", exactFrames, chunkTicks, 0, wire, restore));
            tracks.Add(MeasureChunks("view-f32", viewFrames, chunkTicks, tableBytes, wire, null));
            tracks.Add(MeasureChunks("view-q16", quantFrames, chunkTicks, tableBytes, wire, null));
        }
        return new(fighters, arenaName, ticks, seed, Roster.Take(fighters).Select(c => c.ToString()).ToArray(), wire,
            snapshotBytes, inputBytes, historyBytes, events, hits, shapes, Enumerable.Range(1, fighters).Select(i => sim.GetState((ulong)i).Deaths).ToArray(), view.Values, view.Overflows, view.MaxError,
            tableBytes, Stats(tickTimes), Stats(captureTimes), Stats(viewTimes), Stats(quantTimes), Stats(restoreTimes), tracks.ToArray());
    }

    private static Track MeasureChunks(string name, List<byte[]> frames, int chunkTicks, int tableBytes, long wire, ServerSimulation? restore)
    {
        long total = tableBytes;
        var compress = new List<double>(); var inflate = new List<double>(); var decode = new List<double>(); var seek = new List<double>();
        for (int first = 0; first < frames.Count; first += chunkTicks)
        {
            int count = Math.Min(chunkTicks, frames.Count - first);
            long start = Stopwatch.GetTimestamp();
            using var raw = new MemoryStream();
            using (var w = new BinaryWriter(raw, System.Text.Encoding.UTF8, true))
                for (int i = first; i < first + count; i++) { w.Write(frames[i].Length); w.Write(frames[i]); }
            byte[] body = raw.ToArray();
            using var packed = new MemoryStream();
            using (var deflate = new DeflateStream(packed, CompressionLevel.Optimal, true)) deflate.Write(body);
            byte[] compressed = packed.ToArray();
            compress.Add(Us(start) / 1000 / (count / 60.0));
            total += compressed.Length + 12; // tick, raw length, compressed length
            start = Stopwatch.GetTimestamp();
            byte[] inflated = Inflate(compressed);
            inflate.Add(Us(start));
            if (!body.AsSpan().SequenceEqual(inflated)) throw new InvalidDataException("Chunk roundtrip mismatch");
            start = Stopwatch.GetTimestamp();
            var records = DecodeChunk(compressed, restore != null);
            decode.Add(Us(start));
            if (records.Length != count) throw new InvalidDataException("Chunk record count mismatch");
            if (restore != null)
            {
                // A seek inflates a chunk, scans lengths, and decodes/restores only its last frame.
                start = Stopwatch.GetTimestamp();
                var data = Inflate(compressed);
                int offset = 0, last = 0, length = 0;
                while (offset < data.Length) { length = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset)); last = offset + 4; offset = last + length; }
                var frame = WorldFramePacket.Deserialize(data.AsSpan(last, length));
                restore.RestoreSnapshot(frame.Snapshot);
                seek.Add(Us(start));
                if (!restore.CaptureSnapshot().ContentEquals(frame.Snapshot)) throw new InvalidDataException("Seek restore mismatch");
            }
        }
        return new(name, chunkTicks, total, total / 1e6 / (frames.Count / 3600.0), total / (double)wire,
            Stats(compress), Stats(inflate), Stats(decode), Stats(seek));
    }

    private static byte[][] DecodeChunk(byte[] compressed, bool exact)
    {
        using var r = new BinaryReader(new MemoryStream(Inflate(compressed)));
        var records = new List<byte[]>();
        while (r.BaseStream.Position < r.BaseStream.Length)
        {
            int length = r.ReadInt32();
            byte[] record = r.ReadBytes(length);
            if (record.Length != length) throw new EndOfStreamException();
            if (exact) _ = WorldFramePacket.Deserialize(record);
            else ViewProjection.Validate(record);
            records.Add(record);
        }
        return records.ToArray();
    }

    private static byte[] AsRaw(byte[] frame)
    {
        if ((frame[6] & 1) == 0) return frame;
        byte[] body = Inflate(frame.AsSpan(WorldFramePacket.HeaderSize).ToArray());
        if (body.Length != BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(12))) throw new InvalidDataException("Body length");
        var raw = new byte[WorldFramePacket.HeaderSize + body.Length];
        frame.AsSpan(0, WorldFramePacket.HeaderSize).CopyTo(raw); raw[6] = 0;
        body.CopyTo(raw, WorldFramePacket.HeaderSize); return raw;
    }

    private static byte[] Inflate(byte[] bytes)
    {
        using var stream = new DeflateStream(new MemoryStream(bytes), CompressionMode.Decompress);
        using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray();
    }
    private static double Us(long start) => (Stopwatch.GetTimestamp() - start) * 1e6 / Stopwatch.Frequency;
    private static Distribution Stats(List<double> values)
    {
        if (values.Count == 0) return new(0, 0, 0, 0, 0);
        var a = values.OrderBy(v => v).ToArray();
        return new(a[a.Length / 2], a[Math.Min(a.Length - 1, (int)Math.Ceiling(a.Length * .95) - 1)], a[0], a[^1], a.Length);
    }
    private static void Print(Result r)
    {
        Console.WriteLine($"{r.Fighters} fighters | {r.Arena} | ticks={r.Ticks} seed={r.Seed}");
        Console.WriteLine("track          chunk    MB/min  stored/wire  compress ms/match-s p50/p95   inflate us p50/p95    decode us p50/p95     seek us p50/p95");
        Console.WriteLine($"wire               1 {r.WireBytes / 1e6 / (r.Ticks / 3600.0),9:F3}       1.000");
        foreach (var t in r.Tracks)
            Console.WriteLine($"{t.Name,-14} {t.ChunkTicks,5} {t.MBPerMinute,9:F3} {t.RatioVsWire,11:F3} {Pair(t.CompressionMsPerMatchSecond),28} {Pair(t.InflateUs),22} {Pair(t.DecodeUs),22} {(t.SeekUs.Samples == 0 ? "n/a" : Pair(t.SeekUs)),22}");
        Console.WriteLine($"tick us p50/p95 | bots+sim {Pair(r.TickWithBotsUs)} | capture+wire {Pair(r.CaptureSerializeUs)} | view {Pair(r.ViewUs)} | q16 {Pair(r.QuantizedViewUs)} | restore {Pair(r.RestoreUs)}");
        Console.WriteLine($"raw bytes | snapshot={r.SnapshotBytes} inputs={r.InputBytes} envelope+history={r.EnvelopeAndHistoryBytes}; current events={r.CurrentEvents} hits={r.CurrentHits} shape-ticks={r.ActiveShapeTicks}");
        Console.WriteLine($"q16 values={r.QuantizedValues} escapes={r.QuantizationOverflows} max-error={r.MaxQuantizationError:F6}; strings={r.StringTableBytes} bytes; all frame/chunk/restore checks passed");
    }
    private static string Pair(Distribution d) => $"{d.Median:F2}/{d.P95:F2}";
}

internal sealed class Strings
{
    private readonly Dictionary<string, ushort> _ids = new(StringComparer.Ordinal);
    public ushort Id(string? value)
    {
        value ??= "";
        if (_ids.TryGetValue(value, out var id)) return id;
        id = checked((ushort)_ids.Count); _ids.Add(value, id); return id;
    }
    public byte[] Bytes()
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
        w.Write((ushort)_ids.Count); foreach (string value in _ids.Keys) w.Write(value); return stream.ToArray();
    }
}
