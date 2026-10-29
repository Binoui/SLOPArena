namespace SlopArena.Shared;

public enum PresentationEventSource : byte
{
    Timeline = 0,
    CapabilityExplosion = 1,
    BlockContact = 2,
    HitContact = 3,
    CapabilityLaunch = 4,
    CapabilityLanding = 5,
    /// <summary>A returning projectile reached its free owner and is caught (back on his shoulder).</summary>
    ProjectileCatch = 6,
    /// <summary>A returning projectile hit its busy owner.</summary>
    ProjectileSelfHit = 7,
    /// <summary>
    /// An empty tether ring starts back to its thrower (ADR-0048). The position is where it starts and
    /// <c>Placement.DurationTicks</c> the whole retract: the client draws the ring home over that time.
    /// </summary>
    TetherRetract = 8,
}

public readonly record struct PresentationEventKey(
    uint MatchTick,
    ulong EntityId,
    byte AttackSequence,
    PresentationEventSource Source,
    int OperationIndex);

public readonly record struct TimelinePresentationEvent(
    uint MatchTick,
    ulong EntityId,
    int OperationIndex,
    string PresentationId,
    byte AttackSequence,
    PresentationEventSource Source,
    float WorldX,
    float WorldY,
    float WorldZ,
    float WorldYaw)
{
    public PresentationPlacement Placement { get; init; } = new PresentationPlacement();

    public PresentationEventKey Key =>
        new(MatchTick, EntityId, AttackSequence, Source, OperationIndex);
}
