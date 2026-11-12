using System.IO;

namespace SlopArena.Shared.Replays
{
    public sealed record ReplaySummary(ReplayHeader Header, ReplayRange? Range, ReplayConclusion? Conclusion, ReplayPrefixReason PrefixReason);
    public sealed record ReplaySummaryResult(ReplaySummary? Summary, ReplayFailure? Failure);
    public sealed record ReplayTrackForecast(ReplayTrack Track, ReplayFailure? ExactRefusal);

    /// <summary>
    /// For lists. Reads the header, hops chunk headers and reads the trailer, without inflating or checking lanes.
    /// ReplayWorld.Open stays authoritative: a file the summary accepts may still fail there.
    /// </summary>
    public static class ReplaySummaries
    {
        /// <summary>Borrows <paramref name="file"/>, which must be readable and seekable.</summary>
        public static ReplaySummaryResult Read(Stream file)
        {
            var opened = ReplayReader.Open(file, summary: true);
            if (opened.Failure != null) return new(null, opened.Failure);
            using var reader = opened.Reader!;
            return new(new(reader.Header, reader.Range, reader.Conclusion, reader.PrefixReason), null);
        }

        /// <summary>The track ReplayWorld.Open would choose with Best, from the header and today's content alone.</summary>
        public static ReplayTrackForecast Forecast(ReplayHeader header, ReplayContent content)
        {
            var arena = content.LoadArena(header.ArenaId);
            if (arena == null) return new(ReplayTrack.View, new(ReplayFailureCode.ArenaUnavailable, header.ArenaId, "available", ""));
            var refusal = ReplayExact.Check(header, ReplayExactEnvironment.For(content, arena, header));
            return new(refusal == null ? ReplayTrack.Exact : ReplayTrack.View, refusal);
        }
    }
}
