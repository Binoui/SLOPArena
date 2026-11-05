using SlopArena.Shared.Mayhem;

namespace SlopArena.Shared.Replays;

/// <summary>
/// Durable View-track identities of the Mayhem enums (ADR-0061). Strings are permanent once recorded. Like every
/// semantic map they are total: a value outside the enum reads as "unknown".
/// </summary>
internal static partial class ReplaySemanticIds
{
    internal static string Id(MayhemPhase v) => v > MayhemPhase.Ended ? "unknown" : v switch
    {
        MayhemPhase.Intro => "intro",
        MayhemPhase.Call => "call",
        MayhemPhase.Play => "play",
        MayhemPhase.Verdict => "verdict",
        MayhemPhase.SpeedUp => "speed-up",
        MayhemPhase.Ended => "ended",
    };

    internal static string Id(MayhemEventKind v) => v > MayhemEventKind.Impact ? "unknown" : v switch
    {
        MayhemEventKind.Intro => "intro",
        MayhemEventKind.Call => "call",
        MayhemEventKind.Play => "play",
        MayhemEventKind.Verdict => "verdict",
        MayhemEventKind.SpeedUp => "speed-up",
        MayhemEventKind.Ended => "ended",
        MayhemEventKind.WindUp => "wind-up",
        MayhemEventKind.Launch => "launch",
        MayhemEventKind.Slam => "slam",
        MayhemEventKind.WatchWarning => "watch-warning",
        MayhemEventKind.WatchStart => "watch-start",
        MayhemEventKind.WatchEnd => "watch-end",
        MayhemEventKind.SolidHit => "solid-hit",
        MayhemEventKind.SolidBroken => "solid-broken",
        MayhemEventKind.TargetHit => "target-hit",
        MayhemEventKind.TargetBroken => "target-broken",
        MayhemEventKind.Pass => "pass",
        MayhemEventKind.Fail => "fail",
        MayhemEventKind.Impact => "impact",
    };

    internal static string Id(FailCause v) => v > FailCause.BoardsRemain ? "unknown" : v switch
    {
        FailCause.Hazard => "hazard",
        FailCause.Fell => "fell",
        FailCause.StillCaged => "still-caged",
        FailCause.OutsideSpot => "outside-spot",
        FailCause.Caught => "caught",
        FailCause.BoardsRemain => "boards-remain",
    };

    internal static string Id(SplatStyle v) => v > SplatStyle.Fallen ? "unknown" : v switch
    {
        SplatStyle.Toasted => "toasted",
        SplatStyle.Flattened => "flattened",
        SplatStyle.Dizzy => "dizzy",
        SplatStyle.Fallen => "fallen",
    };

    internal static string Id(MayhemZoneKind v) => v > MayhemZoneKind.Spotlight ? "unknown" : v switch
    {
        MayhemZoneKind.Spotlight => "spotlight",
    };

    internal static string Id(MayhemTargetKind v) => v > MayhemTargetKind.BoardStack ? "unknown" : v switch
    {
        MayhemTargetKind.BoardStack => "board-stack",
    };
}
