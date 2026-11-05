using System;
namespace SlopArena.Shared.Replays;

internal static partial class ReplaySemanticIds
{
    internal static string Form(byte f) => f switch
    {
        0 => "base",
        1 => "alternate",
        _ => "unknown"
    };
    internal static string Id(ActionState v) => v switch
    {
        ActionState.Warping => "retired-warping",
        ActionState.Idle => "idle",
        ActionState.Hitstun => "hitstun",
        ActionState.Sliding => "sliding",
        ActionState.Attacking => "attacking",
        ActionState.JumpSquat => "jump-squat",
        ActionState.Aiming => "aiming",
        ActionState.Run => "run",
        ActionState.LedgeHang => "ledge-hang",
        ActionState.Crouching => "crouching",
        ActionState.Shielding => "shielding",
        ActionState.ShieldDrop => "shield-drop",
        ActionState.GrabAttempt => "grab-attempt",
        ActionState.Grabbed => "grabbed",
        ActionState.Throwing => "throwing",
        ActionState.AirDodgeMovement => "air-dodge-movement",
        ActionState.AirDodgeRecovery => "air-dodge-recovery",
        ActionState.Grabbing => "grabbing",
        ActionState.HomingDashFloat => "homing-dash-float",
        ActionState.HomingDash => "homing-dash",
        ActionState.HomingDashArrive => "homing-dash-arrive",
        ActionState.HomingDashMiss => "homing-dash-miss",
        ActionState.Shell => "shell",
        ActionState.Flying => "flying",
        ActionState.Reeled => "reeled",
        ActionState.Swept => "swept",
        ActionState.CutHeld => "cut-held",
        ActionState.CommandGrabbed => "command-grabbed",
        _ => "unknown"
    };
    internal static string Id(PresentationEventSource v) => v switch
    {
        PresentationEventSource.Timeline => "timeline",
        PresentationEventSource.CapabilityExplosion => "capability-explosion",
        PresentationEventSource.BlockContact => "block-contact",
        PresentationEventSource.HitContact => "hit-contact",
        PresentationEventSource.CapabilityLaunch => "capability-launch",
        PresentationEventSource.CapabilityLanding => "capability-landing",
        PresentationEventSource.ProjectileCatch => "projectile-catch",
        PresentationEventSource.ProjectileSelfHit => "projectile-self-hit",
        PresentationEventSource.TetherRetract => "tether-retract",
        _ => "unknown"
    };
    internal static string Id(DefenseInteractionPhase v) => v switch
    {
        DefenseInteractionPhase.None => "none",
        DefenseInteractionPhase.Attempt => "attempt",
        DefenseInteractionPhase.Captured => "captured",
        DefenseInteractionPhase.Throwing => "throwing",
        DefenseInteractionPhase.Terminal => "terminal",
        DefenseInteractionPhase.Reeling => "reeling",
        DefenseInteractionPhase.CommandHold => "command-hold",
        DefenseInteractionPhase.UpTossing => "up-tossing",
        DefenseInteractionPhase.FlingReleased => "fling-released",
        DefenseInteractionPhase.UpTossReleased => "up-toss-released",
        _ => "unknown"
    };
    internal static string Id(HitboxShape v) => v switch
    {
        HitboxShape.Sphere => "sphere",
        HitboxShape.Capsule => "capsule",
        HitboxShape.Ring => "ring",
        _ => "unknown"
    };
    internal static string Id(MinePhase v) => v switch
    {
        MinePhase.None => "none",
        MinePhase.Falling => "falling",
        MinePhase.Arming => "arming",
        MinePhase.Armed => "armed",
        MinePhase.Triggered => "triggered",
        _ => "unknown"
    };
    internal static string Id(SlopballPhase v) => v switch
    {
        SlopballPhase.WaitingServe => "waiting-serve",
        SlopballPhase.Live => "live",
        SlopballPhase.Stopped => "stopped",
        SlopballPhase.Countdown => "countdown",
        _ => "unknown"
    };
    internal static string Id(SlopballEventKind v) => v switch
    {
        SlopballEventKind.Served => "served",
        SlopballEventKind.Received => "received",
        SlopballEventKind.Redirected => "redirected",
        SlopballEventKind.Returned => "returned",
        SlopballEventKind.ReServe => "re-serve",
        SlopballEventKind.Missed => "missed",
        SlopballEventKind.Stopped => "stopped",
        SlopballEventKind.ServeReady => "serve-ready",
        SlopballEventKind.PointScored => "point-scored",
        _ => "unknown"
    };
    internal static string Id(SlopballVersusPhase v) => v switch
    {
        SlopballVersusPhase.WaitingServe => "waiting-serve",
        SlopballVersusPhase.HeldServe => "held-serve",
        SlopballVersusPhase.Rally => "rally",
        SlopballVersusPhase.Ended => "ended",
        _ => "unknown"
    };
    internal static string Id(CharacterCoursePhase v) => v switch
    {
        CharacterCoursePhase.Ready => "ready",
        CharacterCoursePhase.Countdown => "countdown",
        CharacterCoursePhase.Running => "running",
        CharacterCoursePhase.Completed => "completed",
        CharacterCoursePhase.Stopped => "stopped",
        _ => "unknown"
    };
    internal static string Id(CharacterCourseEventKind v) => v switch
    {
        CharacterCourseEventKind.Go => "go",
        CharacterCourseEventKind.TargetBroken => "target-broken",
        CharacterCourseEventKind.CheckpointSaved => "checkpoint-saved",
        CharacterCourseEventKind.Respawned => "respawned",
        CharacterCourseEventKind.Completed => "completed",
        CharacterCourseEventKind.TargetHit => "target-hit",
        _ => "unknown"
    };
    internal static string Id(CourseFormat v) => v switch
    {
        CourseFormat.Race => "race",
        CourseFormat.Coop => "coop",
        CourseFormat.Scramble => "scramble",
        _ => "unknown"
    };
    internal static string Id(AuthoringAimMovementMode v) => v switch
    {
        AuthoringAimMovementMode.Fixed => "fixed",
        AuthoringAimMovementMode.Mobile => "mobile",
        _ => "unknown"
    };
    internal static string Id(SlopballServe v) => v switch
    {
        SlopballServe.Lob => "lob", SlopballServe.Zinger => "zinger", SlopballServe.Moonball => "moonball", SlopballServe.Curveball => "curveball", _ => "unknown"
    };
    internal static string Id(RuntimeSolidPhase v) => v switch
    {
        RuntimeSolidPhase.Rising => "rising", RuntimeSolidPhase.Active => "active", RuntimeSolidPhase.Broken => "broken", RuntimeSolidPhase.Sinking => "sinking", _ => "unknown"
    };
    internal static string Id(AuthoringPresentationAttachmentMode v) => v switch
    {
        AuthoringPresentationAttachmentMode.World => "world", AuthoringPresentationAttachmentMode.Bone => "bone", _ => "unknown"
    };
    internal static string Id(SlopballCamp v) => v switch
    {
        SlopballCamp.None => "", SlopballCamp.Teal => "teal", SlopballCamp.Clay => "clay", _ => "unknown"
    };
}
