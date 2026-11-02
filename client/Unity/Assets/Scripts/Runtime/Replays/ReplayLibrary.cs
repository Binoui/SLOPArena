using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SlopArena.Shared.Replays;

namespace SlopArena.Client.Replays
{
    public enum ReplayCompleteness { Complete, Partial, Empty }

    /// <summary>One file of the list: its summary, or the failure that keeps it from opening.</summary>
    public sealed class ReplayListing
    {
        public ReplayListing(string path, DateTime downloaded, long size, ReplaySummary? summary, ReplayFailure? failure)
        {
            Path = path;
            Downloaded = downloaded;
            Size = size;
            Summary = summary;
            Failure = failure;
        }

        public string Path { get; }
        /// <summary>The file's modification time: when it was pulled, not when the Match was played.</summary>
        public DateTime Downloaded { get; }
        public long Size { get; }
        public ReplaySummary? Summary { get; }
        public ReplayFailure? Failure { get; }

        public ReplayCompleteness Completeness => Summary?.Range == null ? ReplayCompleteness.Empty
            : Summary.Conclusion == null ? ReplayCompleteness.Partial : ReplayCompleteness.Complete;
        public bool CanOpen => Summary?.Range != null;
    }

    /// <summary>
    /// The list's boundary: never throws for a file, and one unreadable file is one row with its reason. Reading runs
    /// on a worker, newest first and at most <see cref="MaxFiles"/> files, cached by path, size and modification time.
    /// </summary>
    public static class ReplayLibrary
    {
        public const int MaxFiles = 500;
        private static readonly Dictionary<(string Path, long Size, DateTime Modified), ReplayListing> Cache = new();
        private static readonly object CacheLock = new();

        public static Task<IReadOnlyList<ReplayListing>> ScanAsync(string root, CancellationToken cancellation)
            => Task.Run(() => Scan(root, cancellation), cancellation);

        public static IReadOnlyList<ReplayListing> Scan(string root, CancellationToken cancellation)
        {
            if (!Directory.Exists(root)) return Array.Empty<ReplayListing>();
            var rows = new List<ReplayListing>();
            var files = new List<(FileInfo File, DateTime Modified)>();
            try
            {
                foreach (var path in Directory.EnumerateFiles(root, "*.slopreplay*", SearchOption.AllDirectories))
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (!ReplayDirectory.IsReplayFile(path)) continue;
                    try
                    {
                        var file = new FileInfo(path);
                        files.Add((file, file.LastWriteTimeUtc));
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        rows.Add(Failure(path, DateTime.MinValue, 0, e));
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                rows.Add(Failure(root, DateTime.MinValue, 0, e));
            }
            foreach (var entry in files.OrderByDescending(f => f.Modified).Take(MaxFiles))
            {
                cancellation.ThrowIfCancellationRequested();
                rows.Add(Read(entry.File));
            }
            return rows.OrderByDescending(r => r.Downloaded).ToArray();
        }

        private static ReplayListing Read(FileInfo file)
        {
            string path = file.FullName;
            DateTime modified = DateTime.MinValue;
            long size = 0;
            try
            {
                size = file.Length;
                modified = file.LastWriteTimeUtc;
                var key = (path, size, modified);
                lock (CacheLock)
                    if (Cache.TryGetValue(key, out var cached)) return cached;
                using var stream = file.OpenRead();
                var read = ReplaySummaries.Read(stream);
                var row = new ReplayListing(path, modified.ToLocalTime(), size, read.Summary, read.Failure);
                lock (CacheLock) Cache[key] = row;
                return row;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return Failure(path, modified, size, e);
            }
        }

        private static ReplayListing Failure(string path, DateTime modified, long size, Exception error)
            => new(path, modified, size, null, new ReplayFailure(ReplayFailureCode.IoFailure, error.Message, "", ""));
    }
}
