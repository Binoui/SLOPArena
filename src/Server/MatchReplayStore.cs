using System.Collections.Concurrent;
using System.Threading.Channels;
using SlopArena.Shared;
using SlopArena.Shared.Replays;

namespace SlopArena.Server;

/// <summary>Host-owned workers and retention. No filesystem work runs on the Match thread.</summary>
public sealed class MatchReplayStore
{
    readonly string directory;
    readonly int days;
    readonly long maxBytes;
    readonly object retentionGate = new();
    readonly ConcurrentDictionary<string, Task> workers = new();
    readonly ConcurrentDictionary<string, byte> active = new(StringComparer.Ordinal);
    readonly Func<string, Stream> open;
    public MatchReplayStore(string directory, int days = 30, long maxBytes = 5_000_000_000,
        Func<string, Stream>? open = null)
    {
        this.directory = Path.GetFullPath(directory);
        this.days = days > 0 ? days : throw new ArgumentOutOfRangeException(nameof(days));
        this.maxBytes = maxBytes > 0 ? maxBytes : throw new ArgumentOutOfRangeException(nameof(maxBytes));
        this.open = open ?? (path => new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
        Directory.CreateDirectory(this.directory);
        // Fail closed at startup if the configured volume is not writable.
        string probe = Path.Combine(this.directory, ".probe-" + Guid.NewGuid());
        using (File.Create(probe)) { }
        File.Delete(probe);
        Retain();
    }
    public static MatchReplayStore? FromEnvironment()
    {
        string? dir = Environment.GetEnvironmentVariable("SLOPARENA_REPLAY_DIR");
        if (string.IsNullOrWhiteSpace(dir)) return null;
        try
        {
            int days = int.Parse(Environment.GetEnvironmentVariable("SLOPARENA_REPLAY_RETENTION_DAYS") ?? "30");
            long bytes = long.Parse(Environment.GetEnvironmentVariable("SLOPARENA_REPLAY_RETENTION_BYTES") ?? "5000000000");
            return new(dir, days, bytes);
        }
        catch (Exception e) { Console.WriteLine($"[Replay] disabled: {e.Message}"); return null; }
    }
    public MatchReplayRecording Start(ReplayHeader header, MatchContentCatalog content, ArenaDefinition arena,
        MatchDiagnostics diagnostics, DateTimeOffset now, int capacity = 600)
    {
        string path = Path.Combine(directory, now.ToString("yyyy-MM-dd"), Path.GetFileName(header.MatchId) + ".slopreplay.part");
        var recording = new MatchReplayRecording(header, content, arena, capacity);
        active.TryAdd(path, 0);
        var task = Task.Run(async () =>
        {
            ReplayWriteResult? result = null;
            string finalPath = path;
            string detail = "io-failure";
            try
            {
                Report($"start ticks=0 bytes=0 path={path}");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using (var stream = open(path))
                using (var writer = new ReplayWriter(stream, header))
                {
                    await foreach (var tick in recording.Queue.Reader.ReadAllAsync())
                    {
                        var status = writer.Append(tick);
                        if (status != ReplayAppend.BufferedOrCommitted)
                        {
                            recording.Complete(new(status == ReplayAppend.LimitReached ? ReplayEndReason.Limit : ReplayEndReason.Cancelled,
                                status == ReplayAppend.LimitReached ? "byte-limit" : "writer-failed", null));
                            break;
                        }
                    }
                    result = writer.Complete(recording.Conclusion!);
                    stream.Flush();
                    if (stream is FileStream file) file.Flush(flushToDisk: true);
                }
                if (!result.Stopped)
                {
                    var completedPath = path[..^5];
                    lock (retentionGate) File.Move(path, completedPath);
                    finalPath = completedPath;
                }
                detail = result.Conclusion.Reason + ":" + result.Conclusion.Detail;
            }
            catch (Exception e) { detail = "io-failure: " + e.Message; }
            finally
            {
                recording.Complete(new(ReplayEndReason.Cancelled, detail, null));
                // Release queued snapshots promptly on IO failure/limit.
                while (recording.Queue.Reader.TryRead(out _)) { }
                long bytes = result?.BytesWritten ?? 0;
                try { if (File.Exists(finalPath)) bytes = new FileInfo(finalPath).Length; } catch { }
                long ticks = result?.LastTick is uint last ? (long)last - header.StartTick + 1 : 0;
                Report($"end reason={detail} ticks={ticks} bytes={bytes} path={finalPath}");
                active.TryRemove(path, out _);
                Retain();
            }
            void Report(string message)
            {
                diagnostics.Log(header.StartTick, "Replay " + message);
                Console.WriteLine($"[Replay:{header.MatchId}] {message}");
            }
        });
        workers[path] = task;
        _ = task.ContinueWith(_ => workers.TryRemove(path, out var ignored), TaskScheduler.Default);
        return recording;
    }
    public sealed record ReplayFile(string MatchId, long Bytes, bool Complete, DateTime ModifiedAtUtc);
    const long DownloadLimit = 64_000_000;
    static bool Canonical(string id) => Guid.TryParseExact(id, "D", out var guid) && guid.ToString("D") == id;
    IEnumerable<FileInfo> Files() => Directory.EnumerateFiles(directory, "*", new EnumerationOptions
        { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })
        .Where(p => p.EndsWith(".slopreplay", StringComparison.Ordinal) || p.EndsWith(".slopreplay.part", StringComparison.Ordinal))
        .Select(p => new FileInfo(p));
    static string Id(FileInfo file)
    {
        string name = file.Name.EndsWith(".part", StringComparison.Ordinal) ? file.Name[..^5] : file.Name;
        return name[..^11]; // ".slopreplay"
    }
    bool IsActive(string id) => active.Keys.Any(p => Path.GetFileName(p) == id + ".slopreplay.part");
    public IReadOnlyList<ReplayFile> List()
    {
        lock (retentionGate)
        {
            return Files().Where(f => Canonical(Id(f)) && !IsActive(Id(f)))
                .GroupBy(Id).Select(g => g.OrderBy(f => f.Name.EndsWith(".part", StringComparison.Ordinal))
                    .ThenByDescending(f => f.LastWriteTimeUtc).First())
                .OrderByDescending(f => f.LastWriteTimeUtc).ThenBy(f => f.Name, StringComparer.Ordinal)
                .Take(2000).Select(f => new ReplayFile(Id(f), f.Length,
                    !f.Name.EndsWith(".part", StringComparison.Ordinal), f.LastWriteTimeUtc)).ToArray();
        }
    }
    // Return an owned handle so retention cannot replace the selected bytes between lookup and streaming.
    public (int Status, FileStream? Stream) OpenDownload(string id)
    {
        if (!Canonical(id)) return (404, null);
        lock (retentionGate)
        {
            if (IsActive(id)) return (409, null);
            var file = Files().Where(f => Id(f) == id)
                .OrderBy(f => f.Name.EndsWith(".part", StringComparison.Ordinal))
                .ThenByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
            if (file == null) return (404, null);
            try
            {
                var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                    65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
                if (stream.Length > DownloadLimit) { stream.Dispose(); return (413, null); }
                return (200, stream);
            }
            catch (FileNotFoundException) { return (404, null); }
            catch (DirectoryNotFoundException) { return (404, null); }
        }
    }
    public bool WaitForWriters(TimeSpan timeout) => Task.WaitAll(workers.Values.ToArray(), timeout);
    public void Retain()
    {
        lock (retentionGate)
        {
            try
            {
                var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                    .Where(p => p.EndsWith(".slopreplay", StringComparison.Ordinal) || p.EndsWith(".slopreplay.part", StringComparison.Ordinal))
                    .Select(p => new FileInfo(p)).OrderBy(f => f.LastWriteTimeUtc).ThenBy(f => f.FullName, StringComparer.Ordinal).ToArray();
                long total = files.Sum(f => f.Length);
                var cutoff = DateTime.UtcNow.AddDays(-days);
                foreach (var file in files)
                {
                    if (active.ContainsKey(file.FullName)) continue;
                    if (file.LastWriteTimeUtc >= cutoff && total <= maxBytes) continue;
                    long size = file.Length;
                    file.Delete();
                    total -= size;
                }
            }
            catch (Exception e) { Console.WriteLine($"[Replay] retention failed: {e.Message}"); }
        }
    }
}

/// <summary>Single producer capture tap; completion is atomic and never waits for its worker.</summary>
public sealed class MatchReplayRecording
{
    readonly ReplayCapture capture;
    internal readonly Channel<ReplayTickCapture> Queue;
    ReplayConclusion? conclusion;
    public ReplayConclusion? Conclusion => Volatile.Read(ref conclusion);
    public long CaptureCalls { get; private set; }
    internal MatchReplayRecording(ReplayHeader header, MatchContentCatalog content, ArenaDefinition arena, int capacity)
    {
        capture = new(header, content, arena);
        Queue = Channel.CreateBounded<ReplayTickCapture>(new BoundedChannelOptions(capacity)
        { SingleWriter = true, SingleReader = true, FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false });
    }
    public void Capture(ServerSimulation sim, WorldFramePacket packet, ReplayMatchResult? result)
    {
        if (Conclusion != null) return;
        try
        {
            CaptureCalls++;
            var value = capture.Capture(sim, packet);
            if (value.Status == ReplayCaptureStatus.Accepted && !Queue.Writer.TryWrite(value.Capture!))
                Complete(new(ReplayEndReason.Limit, "backlog", null));
            else if (value.Status == ReplayCaptureStatus.Sealed)
                Complete(value.Detail == "ended" && result != null
                    ? new(ReplayEndReason.Finished, "ended", result)
                    : new(ReplayEndReason.Cancelled, value.Detail, null));
        }
        catch (Exception e) { Complete(new(ReplayEndReason.Cancelled, "capture-failed: " + e.Message, null)); }
    }
    public void Complete(ReplayConclusion value)
    {
        if (Interlocked.CompareExchange(ref conclusion, value, null) == null) Queue.Writer.TryComplete();
    }
}
