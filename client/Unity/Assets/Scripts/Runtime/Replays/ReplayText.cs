using System.Globalization;
using System.Linq;
using SlopArena.Shared;
using SlopArena.Shared.Replays;

namespace SlopArena.Client.Replays
{
    /// <summary>Player-facing words for Replay facts and failures.</summary>
    public static class ReplayText
    {
        public static string Describe(ReplayFailure failure) => failure.Code switch
        {
            ReplayFailureCode.PleaseUpdate => "Made by a newer build. Update SLOPArena.",
            ReplayFailureCode.InvalidHeader => "Not a readable Replay file.",
            ReplayFailureCode.EmptyReplay => "This Replay has no ticks.",
            ReplayFailureCode.ArenaUnavailable => $"This build has no arena '{failure.Subject}'.",
            ReplayFailureCode.PackageUnavailable => $"This build has no fighter '{failure.Subject}'.",
            ReplayFailureCode.InvalidView or ReplayFailureCode.InvalidExact => "Part of this Replay is damaged.",
            ReplayFailureCode.FileTooLarge => "This Replay file is too large.",
            ReplayFailureCode.IoFailure => "The Replay file could not be read.",
            _ => failure.Code.ToString(),
        };

        /// <summary>Why a Replay plays View on this build, in a few words.</summary>
        public static string ViewReason(ReplayFailure? refusal) => refusal?.Code switch
        {
            null => "chosen",
            ReplayFailureCode.ProtocolMismatch or ReplayFailureCode.FrameFormatMismatch
                or ReplayFailureCode.SnapshotFormatMismatch => "other build",
            ReplayFailureCode.ContentHashMismatch or ReplayFailureCode.PackageIdentityMismatch => "content changed",
            ReplayFailureCode.PackageUnavailable => "fighter missing",
            ReplayFailureCode.ArenaUnavailable or ReplayFailureCode.ArenaIdentityMismatch => "arena changed",
            var code => code.ToString(),
        };

        public static string Track(ReplayTrack track, ReplayFailure? refusal)
            => track == ReplayTrack.Exact ? "EXACT" : $"VIEW · {ViewReason(refusal)}";

        public static string Clock(uint ticks)
        {
            uint seconds = ticks / ReplayHeader.TickRate;
            return $"{seconds / 60}:{seconds % 60:00}";
        }

        public static string Mode(string formatId) => formatId switch
        {
            "stock" => "Brawl",
            "slopball-coop" => "Slopball",
            "slopball-versus" => "Slopball Versus",
            "course-race" => "Rally Race",
            "course-coop" => "Rally Co-op",
            "course-scramble" => "Scramble",
            "manki-mayhem" => "Manki Mayhem",
            _ => formatId,
        };

        /// <summary>The recorded player name, or the fighter name with a short Steam ID.</summary>
        public static string Fighter(ReplayCastMember member)
            => ReplayLabels.ReplayParticipantLabel(member.Recorded, member.Content.DisplayName);

        public static string Conclusion(ReplayConclusion? conclusion, ReplayPrefixReason prefix)
        {
            if (conclusion == null)
                return prefix switch
                {
                    ReplayPrefixReason.TruncatedTail => "Partial Replay: the file was cut off",
                    ReplayPrefixReason.CorruptTail => "Partial Replay: the end of the file is damaged",
                    _ => "Partial Replay: no result recorded",
                };
            if (conclusion.Reason != ReplayEndReason.Finished || conclusion.Result == null)
                return $"Ended early: {conclusion.Reason.ToString().ToLowerInvariant()} {conclusion.Detail}".TrimEnd();
            var result = conclusion.Result;
            if (result.Draw) return "Draw";
            if (result.WinningCampId.Length > 0) return $"{result.WinningCampId.ToUpperInvariant()} wins";
            return result.WinnerIds.Count == 0 ? "No winner"
                : string.Join(", ", result.WinnerIds.Select(id => "P" + id.ToString(CultureInfo.InvariantCulture))) + " wins";
        }
    }
}
