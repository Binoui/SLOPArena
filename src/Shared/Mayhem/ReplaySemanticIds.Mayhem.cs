using SlopArena.Shared.Mayhem;

namespace SlopArena.Shared.Replays;

/// <summary>
/// Durable View-track identities of the Mayhem enums (ADR-0061). Strings are permanent once recorded. Like every
/// semantic map they are total: a value outside the enum reads as "unknown".
/// </summary>
internal static partial class ReplaySemanticIds
{
    internal static string Id(MayhemPhase v) => v > MayhemEnumLimits.Phase ? "unknown" : v switch
    {
        MayhemPhase.Intro => "intro",
        MayhemPhase.Call => "call",
        MayhemPhase.Play => "play",
        MayhemPhase.Verdict => "verdict",
        MayhemPhase.SpeedUp => "speed-up",
        MayhemPhase.Ended => "ended",
    };

    internal static string Id(MayhemEventKind v) => v > MayhemEnumLimits.Event ? "unknown" : v switch
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

    internal static string Id(FailCause v) => v > MayhemEnumLimits.Failure ? "unknown" : v switch
    {
        FailCause.Hazard => "hazard",
        FailCause.Fell => "fell",
        FailCause.StillCaged => "still-caged",
        FailCause.OutsideSpot => "outside-spot",
        FailCause.Caught => "caught",
        FailCause.BoardsRemain => "boards-remain",
        FailCause.OutOfRing => "out-of-ring",
        FailCause.OutOfFrame => "out-of-frame",
        FailCause.Glitched => "glitched",
        FailCause.NoBonk => "no-bonk",
        FailCause.HeGotUp => "he-got-up",
        FailCause.Misfire => "misfire",
        FailCause.TooSlow => "too-slow",
    };

    internal static string Id(SplatStyle v) => v > MayhemEnumLimits.Splat ? "unknown" : v switch
    {
        SplatStyle.Toasted => "toasted",
        SplatStyle.Flattened => "flattened",
        SplatStyle.Dizzy => "dizzy",
        SplatStyle.Fallen => "fallen",
    };

    internal static string Id(MayhemZoneKind v) => v > MayhemEnumLimits.Zone ? "unknown" : v switch
    {
        MayhemZoneKind.Spotlight => "spotlight",
        MayhemZoneKind.SumoRing => "sumo-ring",
        MayhemZoneKind.SelfieFrame => "selfie-frame",
        MayhemZoneKind.GlitchPanel => "glitch-panel",
    };

    internal static string Id(MayhemTargetKind v) => v > MayhemEnumLimits.Target ? "unknown" : v switch
    {
        MayhemTargetKind.BoardStack => "board-stack",
        MayhemTargetKind.CardboardHead => "cardboard-head",
        MayhemTargetKind.DecoyHead => "decoy-head",
        MayhemTargetKind.DizzyHost => "dizzy-host",
    };
    internal static string Id(MayhemBombKind v) => v > MayhemEnumLimits.BombKind ? "unknown" : v switch
    { MayhemBombKind.Aimed => "aimed", MayhemBombKind.Barrage => "barrage" };

    internal static string Id(MayhemWatchKind v) => v > MayhemEnumLimits.Watch ? "unknown" : v switch
    {
        MayhemWatchKind.Look => "look",
        MayhemWatchKind.FakeLook => "fake-look",
        MayhemWatchKind.Draw => "draw",
        MayhemWatchKind.FakeDraw => "fake-draw",
    };
}
