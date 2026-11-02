namespace SlopArena.Shared;

public sealed record SteeredChargeCarrySource(float AnchorDistance, float AnchorHeight, ushort MaxCarryTicks,
    GrabLaunchSource Fling, ushort FlingTicks, string FlingAnimationId = "",
    string ScoopPresentationId = "", string FlingPresentationId = "");

public sealed record CookedSteeredChargeCarry(float AnchorDistance, float AnchorHeight, ushort MaxCarryTicks,
    CookedGrabLaunch Fling, ushort FlingTicks, string FlingAnimationId = "",
    string ScoopPresentationId = "", string FlingPresentationId = "");
