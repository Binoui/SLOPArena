using System;
using System.Collections.Generic;
using System.IO;

namespace SlopArena.Shared.Replays
{
    public readonly record struct ReplayRange(uint First, uint Last);
    public sealed record ReplayBuildIdentity(string Build, uint Protocol, uint FrameFormat,
        uint SnapshotFormat, string ContentHash)
    {
        /// <summary>Protocol/frame/snapshot constants captured from this build; label is diagnostic.</summary>
        public static ReplayBuildIdentity Current(string build, string matchContentHash)
            => new(build, SimulationProtocol.Version, WorldFramePacket.FormatVersion, ServerSimulation.SnapshotFormatVersion, matchContentHash);
    }
    public sealed record ReplayParticipant(ulong EntityId, string ParticipantProvider,
        string ParticipantSubject, MatchContentIdentity Package, string FormId, string CampId, string ColourId, string DisplayName = "");
    public sealed record ReplayRules(string FormatId, string FriendlyFireId, byte MaxStocks, bool BallOnly);

    /// <summary>
    /// File's Match identity. Roster is sorted by entity ID, unique and 2..4 fighters.
    /// Build.ContentHash includes the admitted match content (including arena). Steam subject
    /// is the invariant-culture decimal SteamId. DisplayName is optional presentation metadata.
    /// </summary>
    public sealed record ReplayHeader(string MatchId, string ArenaId, ReplayRules Rules,
        uint StartTick, IReadOnlyList<ReplayParticipant> Roster, ReplayBuildIdentity Build, string ArenaDigest = "")
    {
        public MatchContentHandleMap? ContentMap { get; init; }
        public const ushort TickRate = 60;

        public bool TryWithLocalContent(MatchContentCatalog catalog, out ReplayHeader? prepared, out string? refusal)
        {
            prepared = null;
            refusal = null;
            try { prepared = WithLocalContent(catalog); return true; }
            catch (Exception ex) when (ReplayStorage.ParseFailure(ex))
            {
                refusal = "Recording refused before capture. The header must fit 64 KiB: " + ex.Message;
                return false;
            }
        }

        public ReplayHeader WithLocalContent(MatchContentCatalog catalog)
        {
            var map = MatchContentHandleMap.CaptureLocal(catalog, new MatchArenaRecord(ArenaId, ArenaDigest));
            var prepared = this with { ContentMap = map, Build = Build with { ContentHash = MatchContentHandleMapCodec.HashLocalContent(map) } };
            _ = ReplayStorage.Preamble(prepared);
            return prepared;
        }
    }

    public enum ReplayEndReason
    {
        Finished,
        Cancelled,
        Limit
    }
    public sealed record ReplayStanding(ulong EntityId, byte Deaths, byte KOs, ushort Score,
        byte Place, bool Withdrawn, ulong CompletedTicks);
    public sealed record ReplayMatchResult(IReadOnlyList<ulong> WinnerIds, string WinningCampId,
        bool Draw, IReadOnlyList<ReplayStanding> Standings, ulong TeamReturns,
        ushort TealScore, ushort ClayScore, string TerminalModePhaseId);
    public sealed record ReplayConclusion(ReplayEndReason Reason, string Detail, ReplayMatchResult? Result);
    public sealed record ReplayWriteResult(uint? LastTick, ReplayConclusion Conclusion, long BytesWritten, bool Stopped = false);

    /// <summary>No malformed file, version mismatch or unsupported content throws through the public reader.</summary>
    public enum ReplayFailureCode
    {
        PleaseUpdate,
        InvalidHeader,
        InvalidView,
        InvalidExact,
        InvalidRules,
        EmptyReplay,
        ProtocolMismatch,
        FrameFormatMismatch,
        SnapshotFormatMismatch,
        ContentHashMismatch,
        PackageUnavailable,
        PackageIdentityMismatch,
        ArenaUnavailable,
        ArenaIdentityMismatch,
        TickOutOfRange,
        FileTooLarge,
        IoFailure
    }
    /// <summary>Expected/Actual are useful diagnostics, never a free-form success/failure decision.</summary>
    public sealed record ReplayFailure(ReplayFailureCode Code, string Subject, string Expected, string Actual);
    public enum ReplayPrefixReason
    {
        None,
        CleanEofWithoutTrailer,
        TruncatedTail,
        CorruptTail
    }
    public enum ReplayAppend
    {
        BufferedOrCommitted,
        LimitReached,
        Stopped
    }
    public enum ReplayStep
    {
        Presented,
        End,
        Failed
    }
    public sealed record ReplayStepResult(ReplayStep Status, ReplayFailure? Failure);

    // Reader entry points return either a value or a typed failure.
    public sealed record ReplayOpenResult(ReplayReader? Reader, ReplayFailure? Failure);
    public sealed record ReplayViewReadResult(ReplayViewTick? Tick, ReplayFailure? Failure);
    public sealed record ReplayPlayerOpenResult(ReplayPlayer? Player, ReplayFailure? Failure);

}
