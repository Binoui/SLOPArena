namespace SlopArena.Shared;

public readonly record struct MankiCanPayload(
    ushort FuseTicks, ushort WarningTicks, ProjectileExplosion Blast, ProjectileExplosion EarlyBlast);

public readonly record struct MankiCanState(
    ulong Id, ulong OwnerId, ulong ActivationId, byte AttackSlot, bool Airborne, byte AttackSequence,
    float X, float Y, float Z, ushort FuseTicksLeft, ushort WarningTicksLeft, MankiCanPayload Payload)
{
    public bool IsTriggered => WarningTicksLeft > 0;
    public const float Radius = 0.35f;
}
