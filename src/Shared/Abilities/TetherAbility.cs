using System;

namespace SlopArena.Shared.Abilities;

/// <summary>
/// slop.ability.tether.v1 (ADR-0048), Bashie's Rescue Ring. At the throw tick the ring flies straight at her
/// target (her facing without one). The first fighter it meets is caught: the simulation pairs them and this
/// move reels them to her feet, one step per tick, until they arrive and she is free. A range, stage, shield or
/// bounced miss reels the empty ring back for a time fixed at the miss, then a short recovery: the punish. She
/// stays Attacking throughout and the move owns its end. The ring in flight is an ordinary projectile of this
/// activation; the caught fighter is held by the pair fields and moved by <see cref="ServerSimulation.ReelTether"/>.
/// </summary>
public sealed class TetherAbility : ServerAbility, ILandingContinuationCapability
{
    private enum Phase : byte { Windup, Out, Reel, Retract, Done }

    private readonly CookedTetherCapabilityParameters _parameters;
    private Phase _phase;
    private ushort _phaseTicks;
    // The empty reel plus the recovery, fixed when the miss happens.
    private ushort _retractTicks;
    // Where the ring is: its next position in flight, the caught fighter while reeling. A miss reels back from here.
    private float _ringX, _ringY, _ringZ;

    public TetherAbility(CookedTetherCapabilityParameters parameters)
        => _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));

    public override TetherAbility? Tether => this;
    public override float GravityMultiplier => _parameters.AirGravityScale;

    /// <summary>The ring is out and its contacts catch.</summary>
    internal bool Catches => _phase == Phase.Out;
    /// <summary>A caught fighter is on the line.</summary>
    internal bool Reeling => _phase == Phase.Reel;

    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        s.State = ActionState.Attacking;
        s.IsAiming = false;
        s.IsFastFalling = false;
        if (s.IsGrounded) Plant(ref s);
        _phase = Phase.Windup;
        _phaseTicks = 0;
        // An upper bound until the ring resolves; each later phase sets its own.
        s.AnimLockTicks = (ushort)(_parameters.ThrowTick + _parameters.Projectile.MaxFlightTicks + 3);
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        if (_phase == Phase.Done) return;
        if (s.IsGrounded) Plant(ref s);
        if (_phaseTicks < ushort.MaxValue) _phaseTicks++;
        switch (_phase)
        {
            case Phase.Windup:
                if (_phaseTicks >= _parameters.ThrowTick) Throw(ref s);
                break;
            case Phase.Out:
                TickOut(ref s);
                break;
            case Phase.Reel:
                // The catch lands outside this tick, so the reel clip starts here, where the move picks it up.
                if (_phaseTicks == 1) ShowClip(ref s, _parameters.ReelAnimationId);
                switch (OwnerSimulation!.ReelTether(ref s, _parameters, out var ring))
                {
                    case TetherStep.Holding:
                        (_ringX, _ringY, _ringZ) = ring;
                        break;
                    case TetherStep.Arrived:
                        Finish(ref s);
                        break;
                    case TetherStep.Snapped:
                        StartRetract(ref s, ring);
                        break;
                    default:
                        StartRetract(ref s, (_ringX, _ringY, _ringZ));
                        break;
                }
                break;
            case Phase.Retract:
                if (_phaseTicks >= _retractTicks) Finish(ref s);
                break;
        }
    }

    /// <summary>The ring leaves her hand toward her target's torso at this tick, which also turns her to face it.</summary>
    private void Throw(ref CharacterState s)
    {
        var ring = SpawnCookedProjectile(ref s, _parameters.Projectile, PresentationOperationIndex, autoTarget: true,
            cancelWithAbility: true);
        Emit(ref s, _parameters.ThrowPresentationId, PresentationEventSource.CapabilityLaunch, ring.X, ring.Y, ring.Z, 0);
        _phase = Phase.Out;
        _phaseTicks = 0;
        // The resolver moves the ring later this tick: a wall right in front stops it before that first travel.
        if (Arena is { } arena && Resolver.MeetsStage(in ring, in arena, out _, out _, out _))
        {
            Resolver.RemoveOwnedHitboxes(s.EntityId, ActivationId);
            StartRetract(ref s, (ring.X, ring.Y, ring.Z));
            return;
        }
        CacheNextPosition(in ring);
    }

    /// <summary>
    /// The ring flies until the resolver spends it (a catch already moved this move on; a shield, a ball, a target
    /// or the end of its range is a miss) or this tick's travel would meet the stage.
    /// </summary>
    private void TickOut(ref CharacterState s)
    {
        if (!Resolver.TryGetActivationProjectile(s.EntityId, ActivationId, out var ring))
        {
            StartRetract(ref s, (_ringX, _ringY, _ringZ));
            return;
        }
        if (Arena is { } arena && Resolver.MeetsStage(in ring, in arena, out _, out _, out _))
        {
            Resolver.RemoveOwnedHitboxes(s.EntityId, ActivationId);
            StartRetract(ref s, (ring.X, ring.Y, ring.Z));
            return;
        }
        CacheNextPosition(in ring);
    }

    /// <summary>Called by the simulation in the tick its ring catches a fighter: the reel starts.</summary>
    internal void OnCaught(ref CharacterState s, float victimX, float victimY, float victimZ)
    {
        _phase = Phase.Reel;
        _phaseTicks = 0;
        (_ringX, _ringY, _ringZ) = (victimX, victimY, victimZ);
        s.AnimLockTicks = (ushort)(_parameters.MaxReelTicks + 3);
    }

    /// <summary>
    /// The empty ring comes back from <paramref name="from"/> to her hand at the retract speed, then she recovers.
    /// The ring has no body on the way: one presentation event carries its start and its time home.
    /// </summary>
    private void StartRetract(ref CharacterState s, (float X, float Y, float Z) from)
    {
        var hand = Hand(in s);
        float dx = from.X - hand.X, dy = from.Y - hand.Y, dz = from.Z - hand.Z;
        float distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        // Rounded up, forgiving float noise on an exact whole number of ticks.
        ushort home = (ushort)Math.Max(1, (int)MathF.Ceiling(distance / (_parameters.RetractSpeed * Simulation.TickDt) - 1e-3f));
        _retractTicks = (ushort)(home + _parameters.MissRecoveryTicks);
        _phase = Phase.Retract;
        _phaseTicks = 0;
        // She is free on the tick after the last one, so the lock outlasts this phase by one tick.
        s.AnimLockTicks = (ushort)(_retractTicks + 1);
        ShowClip(ref s, _parameters.RetractAnimationId);
        Emit(ref s, _parameters.RetractPresentationId, PresentationEventSource.TetherRetract, from.X, from.Y, from.Z, home);
    }

    private void Finish(ref CharacterState s)
    {
        _phase = Phase.Done;
        s.AnimLockTicks = 0;
        EndAbility(ref s);
    }

    public override void OnCancel(ref CharacterState s) => _phase = Phase.Done;

    private static void Plant(ref CharacterState s)
    {
        ClearVelocityOwnership(ref s);
        s.VX = s.VZ = 0f;
    }

    private (float X, float Y, float Z) Hand(in CharacterState s)
    {
        var ring = _parameters.Projectile;
        float sin = MathF.Sin(s.FacingYaw), cos = MathF.Cos(s.FacingYaw);
        return (s.PX + ring.LaunchOffsetX * cos + ring.LaunchOffsetZ * sin, s.PY + ring.LaunchOffsetY,
            s.PZ - ring.LaunchOffsetX * sin + ring.LaunchOffsetZ * cos);
    }

    private void CacheNextPosition(in Hitbox ring)
    {
        _ringX = ring.X + ring.VX * Simulation.TickDt;
        _ringY = ring.Y + ring.VY * Simulation.TickDt;
        _ringZ = ring.Z + ring.VZ * Simulation.TickDt;
    }

    /// <summary>Switches to a phase clip listed in the stage, from its first frame.</summary>
    private void ShowClip(ref CharacterState s, string animationId)
    {
        if (string.IsNullOrEmpty(animationId)) return;
        int index = Move?.FindClip(animationId) ?? -1;
        if (index <= 0 || index > byte.MaxValue) return;
        s.ComboStage = (byte)index;
        AnimIndex = (byte)index;
        s.AttackElapsedTicks = 0;
    }

    private void Emit(ref CharacterState s, string id, PresentationEventSource source, float x, float y, float z,
        ushort durationTicks)
    {
        // The empty ring's retract is always announced: the client draws it from the source, the ID only adds effects.
        if (string.IsNullOrEmpty(id) && source != PresentationEventSource.TetherRetract) return;
        PresentationSink?.Invoke(new TimelinePresentationEvent(0, s.EntityId, PresentationOperationIndex, id ?? "",
            PresentationAttackSequence, source, x, y, z, s.FacingYaw)
        {
            Placement = new PresentationPlacement(DurationTicks: durationTicks),
        });
    }

    internal override void WriteSnapshot(SnapshotWriter w)
    {
        base.WriteSnapshot(w);
        w.WriteByte((byte)_phase);
        w.WriteUShort(_phaseTicks);
        w.WriteUShort(_retractTicks);
        w.WriteFloat(_ringX);
        w.WriteFloat(_ringY);
        w.WriteFloat(_ringZ);
    }

    internal override void ReadSnapshot(SnapshotReader r)
    {
        base.ReadSnapshot(r);
        _phase = (Phase)r.ReadByte();
        if (_phase > Phase.Done) throw SnapshotReader.Malformed("tether phase");
        _phaseTicks = r.ReadUShort();
        _retractTicks = r.ReadUShort();
        _ringX = r.ReadFloat();
        _ringY = r.ReadFloat();
        _ringZ = r.ReadFloat();
    }
}
