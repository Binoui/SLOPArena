using System.Collections.Generic;

namespace SlopArena.Shared.Mayhem;

/// <summary>Host hazard tuning shared by every channel that throws bombs or slams rings. Provisional.</summary>
public static class MayhemHazardTuning
{
    public const float BombRadius = .35f, BombGravity = 30f;
    /// <summary>
    /// A bomb drops from this high above its landing, set back this far toward the host booth: steep enough that it
    /// only ever touches someone standing at its ring, never a fighter it passes over.
    /// </summary>
    public const float DropHeight = 16f, DropSetBack = 4f;
    public const ushort BlastTicks = 2;
    /// <summary>A bomb lives this long past its planned impact, so rounding never lets it expire in the air.</summary>
    public const ushort FlightGraceTicks = 20;
    public const ushort WindUpTicks = 24;
}

/// <summary>
/// The only way a channel touches the world: it launches host bombs and rings through the resolver and emits
/// keyed cue events. Host hitboxes pack (episode, lane, ordinal) into ActivationId and VisualOperationIndex, so
/// a blast still names its source after the projectile is gone.
/// </summary>
internal sealed class MayhemHazards
{
    private readonly List<MayhemEvent> _events;
    private readonly ulong _hostId;
    private SpellResolver? _resolver;

    internal MayhemHazards(List<MayhemEvent> events, ulong hostId, MankiMayhemDefinition stage)
    { _events = events; _hostId = hostId; Stage = stage; }

    internal MankiMayhemDefinition Stage { get; }
    internal uint Tick { get; private set; }
    internal int Episode { get; private set; }

    internal void Bind(SpellResolver? resolver, uint tick, int episode) { _resolver = resolver; Tick = tick; Episode = episode; }

    internal static ulong Activation(int episode, byte lane, ushort ordinal)
        => (ulong)(episode + 1) << 32 | (ulong)lane << 24 | (ulong)ordinal + 1;

    internal static bool TryDecode(ulong activation, out int episode, out byte lane, out ushort ordinal)
    {
        episode = (int)(activation >> 32) - 1;
        lane = (byte)(activation >> 24);
        ulong low = activation & 0xFFFFFF;
        ordinal = (ushort)(low == 0 ? 0 : low - 1);
        return episode is >= 0 and < MayhemShowTuning.Episodes && lane <= MayhemEvent.MaxLane && low is > 0 and <= ushort.MaxValue + 1UL;
    }

    internal static int VisualOperation(byte lane, ushort ordinal) => lane << 16 | ordinal;

    /// <summary>An impact bomb from the host: it explodes where it touches a fighter or the deck, never sticks.</summary>
    internal void Bomb(byte lane, ushort ordinal, ArcadePoint landing, ushort flightTicks, float blastRadius)
    {
        // Aimed a little into the deck so rounding never leaves it hovering a tick past its planned impact.
        var target = new ArcadePoint(landing.X, landing.Y + MayhemHazardTuning.BombRadius * .9f, landing.Z);
        var origin = DropOrigin(landing);
        CombatMath.ComputeProjectileLaunch(origin, target, MayhemHazardTuning.BombGravity, flightTicks, out var velocity);
        int operation = VisualOperation(lane, ordinal);
        _resolver?.Spawn(new Hitbox
        {
            OwnerId = _hostId, AttackSequence = (byte)Episode, ActivationId = Activation(Episode, lane, ordinal),
            VisualOperationIndex = operation,
            X = origin.X, Y = origin.Y, Z = origin.Z,
            VX = velocity.X, VY = velocity.Y, VZ = velocity.Z, Gravity = MayhemHazardTuning.BombGravity,
            Radius = MayhemHazardTuning.BombRadius, DurationTicks = (ushort)(flightTicks + MayhemHazardTuning.FlightGraceTicks),
            ContactExplosion = true,
            Explosion = new ProjectileExplosion
            {
                Radius = blastRadius, DurationTicks = MayhemHazardTuning.BlastTicks,
                ExplosionPresentationId = "mayhem.bomb.blast", PresentationAttackSequence = (byte)Episode,
                PresentationOperationIndex = operation,
            },
        });
        Cue(MayhemEventKind.Launch, lane, ordinal);
    }

    private ArcadePoint DropOrigin(ArcadePoint landing)
    {
        float dx = Stage.HostOrigin.X - landing.X, dz = Stage.HostOrigin.Z - landing.Z;
        float length = System.MathF.Sqrt(dx * dx + dz * dz);
        float back = length > .001f ? MayhemHazardTuning.DropSetBack / length : 0f;
        return new ArcadePoint(landing.X + dx * back, landing.Y + MayhemHazardTuning.DropHeight, landing.Z + dz * back);
    }

    /// <summary>An unblockable shockwave ring that expands from a slam and catches grounded fighters.</summary>
    internal void Ring(byte lane, ushort ordinal, ArcadePoint center, float speed, float halfWidth, float halfHeight,
        ushort lifetimeTicks)
    {
        _resolver?.Spawn(new Hitbox
        {
            OwnerId = _hostId, AttackSequence = (byte)Episode, ActivationId = Activation(Episode, lane, ordinal),
            VisualOperationIndex = VisualOperation(lane, ordinal),
            X = center.X, Y = center.Y, Z = center.Z,
            Shape = HitboxShape.Ring, RingSpeed = speed, RingHalfWidth = halfWidth, RingHalfHeight = halfHeight,
            DurationTicks = lifetimeTicks, Unblockable = true, HitsMultipleOpponents = true,
        });
        Cue(MayhemEventKind.Slam, lane, ordinal);
    }

    internal void Cue(MayhemEventKind kind, byte lane, ushort ordinal)
        => _events.Add(new(Tick, (ushort)Episode, lane, ordinal, kind, _hostId));

    internal void ObjectHit(MayhemEventKind kind, byte lane, ulong subject, ulong attacker, ulong activation, byte hitsLeft)
        => _events.Add(new(Tick, (ushort)Episode, lane, MayhemEvent.HitOperation(attacker, activation), kind, subject, hitsLeft));
}
