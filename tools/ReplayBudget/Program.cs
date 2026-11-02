using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using SlopArena.Shared;
using SlopArena.Shared.AI;
using SlopArena.Shared.Replays;
namespace SlopArena.ReplayBudget;

internal static class Program
{
    static readonly CharacterClass[] Roster ={
CharacterClass.FightGuy,
CharacterClass.Manki,
CharacterClass.Wibou,
CharacterClass.Bonk};
    static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        int ticks = 18000,
        seed = 31;
        string? golden = null;
        for (int i = 0; i < args.Length; i += 2)
        {
            switch (args[i])
            {
                case "--ticks":
                    ticks = int.Parse(args[i + 1]);
                    break;
                case "--seed":
                    seed = int.Parse(args[i + 1]);
                    break;
                case "--golden":
                    golden = args[i + 1];
                    break;
                default: throw new ArgumentException(args[i]);
            }
        }
        if (ticks < 1) throw new ArgumentException("ticks");
        var manifest = BuiltInRosterManifestCodec.Load("content-cooked/roster/manifest.json");
        var catalog = new MatchContentCatalogBuilder().Build(manifest, manifest.Entries.ToDictionary(e => e.PackageId, e => CookedCharacterPackageLoader.LoadDirectory("content-cooked/" + e.PackageId, e.Requirement))).Catalog ?? throw new InvalidDataException("Catalog");
        foreach (int count in golden == null ? new[]{
2,
4}
      : new[]{
2})
        {
            Run(catalog, count, Math.Min(ticks, 600), seed, null, false);
            Run(catalog, count, ticks, seed, golden);
        }
        return 0;
    }
    static void Run(MatchContentCatalog catalog, int count, int ticks, int seed, string? golden, bool report = true)
    {
        string name = count == 2 ? "slop_pit" : "last_stop";
        var arena = ArenaBinaryFormat.LoadFromFile("data/arenas/" + name + ".arena") ?? throw new InvalidDataException(name);
        var entries = Roster.Take(count).Select(c => catalog.Resolve(c)!).ToArray();
        var sim = new ServerSimulation(arena, new StockMatchRule(250));
        for (int i = 0; i < count; i++)
        {
            var p = arena.SpawnPoints[i % arena.SpawnPoints.Length];
            sim.RegisterEntity((ulong)i + 1, entries[i].Definition, new CharacterState
            {
                EntityId = (ulong)i + 1,
                PX = p.X,
                PY = p.Y,
                PZ = p.Z,
                FacingYaw = p.Yaw,
                IsGrounded = true,
                JumpsLeft = entries[i].Definition.Movement.MaxJumps,
                AirDodgesLeft = 1
            }
            , entries[i].BakedAnimation);
        }
        var header = new ReplayHeader("budget-seed-" + seed, name, new("stock", "off", 250, false), 1, Array.AsReadOnly(entries.Select((e, i) => new ReplayParticipant((ulong)i + 1, "cpu", i.ToString(CultureInfo.InvariantCulture), e.Identity, "base", "", i.ToString(CultureInfo.InvariantCulture))).ToArray()), ReplayBuildIdentity.Current("step1", "budget-catalog"));
        using var stream = new MemoryStream();
        using var writer = new ReplayWriter(stream, header);
        var capture = new ReplayCapture(header, catalog, arena);
        var history = new WorldFrameHistory();
        var rng = new Random(seed);
        var policy = new HeuristicBotPolicy();
        var memories = Enumerable.Range(0, count).Select(_ => new BotMemory
        {
            Difficulty = CpuDifficulty.Normal
        }).ToArray();
        var deaths = new byte[count];
        var inputs = new Dictionary<ulong,
        InputState>();
        var snapshots = new List<byte[]>();
        var captureUs = new List<double>();
        var appendUs = new List<double>();
        double appendBatch = 0;
        int gen0 = GC.CollectionCount(0);
        for (int tick = 0; tick < ticks; tick++)
        {
            for (int i = 0; i < count; i++)
            {
                ulong id = (ulong)i + 1;
                var self = sim.GetState(id);
                if (self.Deaths != deaths[i])
                {
                    memories[i].Reset();
                    memories[i].Difficulty = CpuDifficulty.Normal;
                    deaths[i] = self.Deaths;
                }
                for (int j = 0; j < count; j++) if (i != j) policy.ObserveOpponent(memories[i], sim.GetState((ulong)j + 1), sim.GetDefinition((ulong)j + 1), entries[j].BakedAnimation, sim.GetActiveMove((ulong)j + 1)?.Slot);
                memories[i].ChooseOpponent(self);
                inputs[id] = policy.Decide(self, sim.GetDefinition(id), rng, memories[i], arena, entries[i].BakedAnimation, activeMove: sim.GetActiveMove(id)?.Slot);
            }
            var consumed = inputs.Select(p => new RelayedInput(p.Key, true, p.Value)).ToArray();
            sim.Tick(inputs);
            foreach (var h in sim.LastTickHits) if (h.OwnerEntityId >= 1 && h.OwnerEntityId <= (ulong)count && h.TargetEntityId >= 1 && h.TargetEntityId <= (ulong)count) memories[(int)h.OwnerEntityId - 1].RecordOpponentContact(h.AttackSlot, h.Blocked ? CpuContactKind.Block : CpuContactKind.Hit, sim.GetState(h.TargetEntityId), h.HitstopTicks);
            var snap = sim.CaptureSnapshot();
            snapshots.Add(snap.Data);
            history.Record(snap.Tick, sim.GetPresentationEvents(true), sim.LastTickHits);
            var frame = new WorldFramePacket(MatchState.Playing, snap, consumed, history.EventsForFrame(snap.Tick).ToArray(), history.HitsForFrame(snap.Tick).ToArray());
            long cs = System.Diagnostics.Stopwatch.GetTimestamp();
            var result = capture.Capture(sim, frame);
            captureUs.Add((System.Diagnostics.Stopwatch.GetTimestamp() - cs) * 1e6 / System.Diagnostics.Stopwatch.Frequency);
            if (result.Status != ReplayCaptureStatus.Accepted) throw new InvalidDataException(result.Detail);
            long ws = System.Diagnostics.Stopwatch.GetTimestamp();
            if (writer.Append(result.Capture!) != ReplayAppend.BufferedOrCommitted) throw new InvalidDataException("Writer stopped");
            appendBatch += (System.Diagnostics.Stopwatch.GetTimestamp() - ws) * 1e6 / System.Diagnostics.Stopwatch.Frequency;
            if ((tick + 1) % 60 == 0) { appendUs.Add(appendBatch / 60); appendBatch = 0; }
        }
        long completeStart = System.Diagnostics.Stopwatch.GetTimestamp();
        var end = writer.Complete(new(ReplayEndReason.Cancelled, "bounded CPU-bot recording", null));
        if (end.Stopped) throw new InvalidDataException(end.Conclusion.Detail);
        appendBatch += (System.Diagnostics.Stopwatch.GetTimestamp() - completeStart) * 1e6 / System.Diagnostics.Stopwatch.Frequency;
        if (ticks % 60 != 0) appendUs.Add(appendBatch / (ticks % 60));
        int collections = GC.CollectionCount(0) - gen0;
        if (report)
        {
            Report(count, "Capture", captureUs);
            Report(count, "Append (amortised per tick over 60-tick chunks)", appendUs);
            Console.WriteLine($"{count} fighters: GC gen0 {collections} over {ticks / 60.0:F1} s");
        }
        stream.Position = 0;
        using var reader = ReplayReader.Open(stream).Reader ?? throw new InvalidDataException("Open");
        var env = new ReplayEnvironment("budget-catalog", catalog, new Dictionary<string, ArenaDefinition>{
{
name,
arena}
});
        var player = reader.CreatePlayer(env).Player ?? throw new InvalidDataException("Exact admission");
        var sink = new Verify(snapshots);
        while (player.Step(sink).Status == ReplayStep.Presented)
        {
        }
        if (sink.Count != ticks) throw new InvalidDataException("Playback count");
        if (golden != null) File.WriteAllBytes(golden, stream.ToArray());
        if (report) Console.WriteLine($"{count} fighters: Exact {writer.ExactBytesWritten / 1e6 / (ticks / 3600.0):F6} MB/min; View {writer.ViewBytesWritten / 1e6 / (ticks / 3600.0):F6} MB/min; total {end.BytesWritten / 1e6 / (ticks / 3600.0):F6} MB/min; {ticks} Exact restores verified");
    }
    static void Report(int count, string label, List<double> samples)
    {
        samples.Sort();
        Console.WriteLine($"{count} fighters: {label} median {samples[samples.Count / 2]:F1} us, p95 {samples[(int)(samples.Count * .95)]:F1} us, p99 {samples[(int)(samples.Count * .99)]:F1} us");
    }
    sealed class Verify(List<byte[]> snapshots) : IReplaySink
    {
        public int Count;
        public void Reset(uint tick)
        {
        }
        public void Present(ReplayDisplay d, ReplayDelivery delivery)
        {
            if (!snapshots[(int)d.View.Tick - 1].AsSpan().SequenceEqual(d.ExactSimulation!.CaptureSnapshot().Data)) throw new InvalidDataException("Restore mismatch");
            Count++;
        }
    }
}
