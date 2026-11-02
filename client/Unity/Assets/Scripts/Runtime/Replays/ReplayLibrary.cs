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
            var files = new List<FileInfo>();
            try
            {
                foreach (var path in Directory.EnumerateFiles(root, "*.slopreplay*", SearchOption.AllDirectories))
                    if (ReplayDirectory.IsReplayFile(path)) files.Add(new FileInfo(path));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            var rows = new List<ReplayListing>();
            foreach (var file in files.OrderByDescending(f => f.LastWriteTimeUtc).Take(MaxFiles))
            {
                cancellation.ThrowIfCancellationRequested();
                var key = (file.FullName, file.Length, file.LastWriteTimeUtc);
                ReplayListing? row;
                lock (CacheLock) Cache.TryGetValue(key, out row);
                if (row == null)
                {
                    row = Read(file);
                    lock (CacheLock) Cache[key] = row;
                }
                rows.Add(row);
            }
            return rows;
        }

        private static ReplayListing Read(FileInfo file)
        {
            try
            {
                using var stream = file.OpenRead();
                var read = ReplaySummaries.Read(stream);
                return new ReplayListing(file.FullName, file.LastWriteTime, file.Length, read.Summary, read.Failure);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return new ReplayListing(file.FullName, file.LastWriteTime, file.Length, null,
                    new ReplayFailure(ReplayFailureCode.IoFailure, e.Message, "", ""));
            }
        }
    }
}
