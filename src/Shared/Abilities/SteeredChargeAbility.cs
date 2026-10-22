using System;
using System.Collections.Generic;

namespace SlopArena.Shared.Abilities;

/// <summary>
/// A steerable charge with a lance (slop.ability.steered-charge.v1), Sir Bonk's joust: a gallop at a
/// fixed speed whose heading the stick turns within a capped rate, a lance hitbox and optional armour
/// while it lasts. The first fighter the lance hits ends it with the hit ending. A shield, a wall or
/// running out of gallop ends it with the miss ending: a skid to a stop, optionally turning round. With
/// no ground under it (started in the air, or galloped off a ledge) it strides level on thin air, hangs
/// for the look-down and ends, so the fighter falls and can act again. It owns the end of its move.
/// </summary>
public sealed class SteeredChargeAbility : ServerAbility, ILandingContinuationCapability
{
    private enum Phase : byte { Gallop, AirStrides, LookDown, HitEnding, MissEnding, Ended }
    private enum Outcome : byte { None, Hit, Blocked }

    // Terrain stops the gallop when a tick keeps less than this share of its forward travel, as the
    // directional dash does: walls hit head-on stop it, seams and gentle ramps do not.
    private const float BlockedTravelRatio = 0.6f;
    // Airborne ticks a ground gallop tolerates before it counts as galloping off a ledge: a small step
    // down or a seam must not start the air ending.
    internal const ushort LedgeGraceTicks = 4;
    private const float SteerDeadZone = 0.2f;

    private readonly CookedSteeredChargeCapabilityParameters _parameters;
    private Phase _phase;
    private Outcome _outcome;
    private ushort _phaseTicks;
    private ushort _gallopTicks;
    private ushort _airborneTicks;
    private float _yaw;
    private float _prevX;
    private float _prevZ;
    private bool _measureTravel;
    // A timeline that starts this capability after tick zero ticks it in the same frame, before the
    // movement step has applied the velocity OnStart set: that tick has nothing to measure or count.
    private bool _startedThisTick;
    private HashSet<ulong>? _lanceHits;

    public SteeredChargeAbility(CookedSteeredChargeCapabilityParameters parameters)
        => _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));

    public override bool OwnsVerticalMotion => _phase is Phase.AirStrides or Phase.LookDown;
    // Fighters move before abilities tick, so the strides and the look-down switch gravity off to
    // hold their height instead of only zeroing the vertical speed afterwards.
    public override float GravityMultiplier => _phase is Phase.AirStrides or Phase.LookDown ? 0f : 1f;
    public override bool HasArmor => _parameters.Armored && (_phase is Phase.Gallop or Phase.AirStrides);

    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        _outcome = Outcome.None;
        _gallopTicks = 0;
        _airborneTicks = 0;
        _yaw = s.FacingYaw;
        s.State = ActionState.Attacking;
        s.IsAiming = false;
        s.IsFastFalling = false;
        ShowClip(ref s, _parameters.GallopAnimationId);
        _lanceHits = SpawnHitbox(ref s, CookedCharacterRuntimeAdapter.ToHitboxEvent(_parameters.LanceHitbox) with
        {
            DurationTicks = ushort.MaxValue,
            HitGroup = 0,
        }, followOwner: true);
        if (s.IsGrounded)
            Enter(ref s, Phase.Gallop);
        else
            StartAirStrides(ref s);
        Drive(ref s, _parameters.GallopSpeed);
        Emit(ref s, def, _parameters.GallopPresentationId, PresentationEventSource.CapabilityLaunch);
        _startedThisTick = true;
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        if (_startedThisTick)
        {
            _startedThisTick = false;
            if (s.PX == _prevX && s.PZ == _prevZ)
                return;
        }
        if (_phaseTicks < ushort.MaxValue) _phaseTicks++;
        switch (_phase)
        {
            case Phase.Gallop: TickGallop(ref s, input, def); break;
            case Phase.AirStrides: TickAirStrides(ref s, input, def); break;
            case Phase.LookDown: TickLookDown(ref s); break;
            case Phase.HitEnding: TickHitEnding(ref s); break;
            case Phase.MissEnding: TickMissEnding(ref s); break;
        }
    }

    private void TickGallop(ref CharacterState s, InputState input, CharacterDefinition def)
    {
        if (_outcome != Outcome.None)
        {
            StartEnding(ref s, def, _outcome == Outcome.Hit ? Phase.HitEnding : Phase.MissEnding);
            return;
        }
        if (!s.IsGrounded)
        {
            if (++_airborneTicks > LedgeGraceTicks)
            {
                StartAirStrides(ref s);
                Drive(ref s, _parameters.GallopSpeed);
                return;
            }
        }
        else
        {
            if (_measureTravel && _airborneTicks == 0 && Blocked(s))
            {
                StartEnding(ref s, def, Phase.MissEnding);
                return;
            }
            _airborneTicks = 0;
        }
        if (++_gallopTicks >= _parameters.MaxGallopTicks)
        {
            StartEnding(ref s, def, Phase.MissEnding);
            return;
        }
        Steer(input);
        LoopGallopClip(ref s);
        Drive(ref s, _parameters.GallopSpeed);
    }

    private void TickAirStrides(ref CharacterState s, InputState input, CharacterDefinition def)
    {
        if (s.IsGrounded && _phaseTicks > 1)
        {
            // Landed on something during the strides: the rest of the gallop goes on along the ground.
            Enter(ref s, Phase.Gallop);
            Drive(ref s, _parameters.GallopSpeed);
            return;
        }
        if (_outcome != Outcome.None || _phaseTicks > _parameters.AirStrideTicks)
        {
            StartLookDown(ref s, def);
            return;
        }
        Steer(input);
        LoopGallopClip(ref s);
        s.VY = 0f;
        s.IsFastFalling = false;
        Drive(ref s, _parameters.GallopSpeed);
    }

    private void TickLookDown(ref CharacterState s)
    {
        Hold(ref s);
        s.VY = 0f;
        s.IsFastFalling = false;
        if (_phaseTicks >= _parameters.LookDownTicks)
            Finish(ref s);
    }

    private void TickHitEnding(ref CharacterState s)
    {
        Hold(ref s);
        if (_phaseTicks >= _parameters.HitEndingTicks)
            Finish(ref s);
    }

    private void TickMissEnding(ref CharacterState s)
    {
        if (!s.IsGrounded && ++_airborneTicks > LedgeGraceTicks)
        {
            // Skidded off a ledge: the move ends and he falls like anyone else.
            Finish(ref s);
            return;
        }
        if (s.IsGrounded) _airborneTicks = 0;
        float skid = _parameters.SkidTicks == 0 ? 0f
            : MathF.Max(0f, 1f - (float)_phaseTicks / _parameters.SkidTicks);
        Drive(ref s, _parameters.GallopSpeed * skid);
        if (_phaseTicks >= _parameters.MissEndingTicks)
        {
            if (_parameters.TurnAroundOnMiss)
            {
                _yaw = NormalizeYaw(_yaw + MathF.PI);
                s.FacingYaw = _yaw;
            }
            Finish(ref s);
        }
    }

    private void StartAirStrides(ref CharacterState s)
    {
        Enter(ref s, Phase.AirStrides);
        ClearVelocityOwnership(ref s);
        s.VY = 0f;
        s.IsFastFalling = false;
    }

    private void StartLookDown(ref CharacterState s, CharacterDefinition def)
    {
        RemoveLance(ref s);
        Enter(ref s, Phase.LookDown);
        ShowClip(ref s, _parameters.AirEndingAnimationId);
        Hold(ref s);
        s.VY = 0f;
        Emit(ref s, def, _parameters.AirEndingPresentationId, PresentationEventSource.CapabilityLanding);
        if (_parameters.LookDownTicks == 0)
            Finish(ref s);
    }

    private void StartEnding(ref CharacterState s, CharacterDefinition def, Phase ending)
    {
        RemoveLance(ref s);
        Enter(ref s, ending);
        _airborneTicks = 0;
        bool hit = ending == Phase.HitEnding;
        ShowClip(ref s, hit ? _parameters.HitEndingAnimationId : _parameters.MissEndingAnimationId);
        Emit(ref s, def, hit ? _parameters.HitEndingPresentationId : _parameters.MissEndingPresentationId,
            PresentationEventSource.CapabilityLanding);
        if (hit)
            Hold(ref s);
        else
            Drive(ref s, _parameters.SkidTicks == 0 ? 0f : _parameters.GallopSpeed);
    }

    private void Enter(ref CharacterState s, Phase phase)
    {
        _phase = phase;
        _phaseTicks = 0;
        _measureTravel = false;
        s.AnimLockTicks = phase switch
        {
            Phase.Gallop => (ushort)Math.Min(ushort.MaxValue,
                _parameters.MaxGallopTicks - _gallopTicks + _parameters.MissEndingTicks + 1),
            Phase.AirStrides => (ushort)Math.Min(ushort.MaxValue,
                _parameters.AirStrideTicks + _parameters.LookDownTicks + 1),
            Phase.LookDown => _parameters.LookDownTicks,
            Phase.HitEnding => _parameters.HitEndingTicks,
            Phase.MissEnding => _parameters.MissEndingTicks,
            _ => 0,
        };
    }

    /// <summary>True when the last tick kept too little of its commanded forward travel: a wall.</summary>
    private bool Blocked(in CharacterState s)
    {
        float expected = _parameters.GallopSpeed * Simulation.TickDt;
        float moved = (s.PX - _prevX) * MathF.Sin(_yaw) + (s.PZ - _prevZ) * MathF.Cos(_yaw);
        return moved < expected * BlockedTravelRatio;
    }

    private void Steer(InputState input)
    {
        float turn = _parameters.TurnDegreesPerSecond * MathF.PI / 180f * Simulation.TickDt;
        if (turn <= 0f || input.MoveX * input.MoveX + input.MoveY * input.MoveY < SteerDeadZone * SteerDeadZone)
            return;
        float delta = NormalizeYaw(MathF.Atan2(input.MoveX, input.MoveY) - _yaw);
        _yaw = NormalizeYaw(_yaw + Math.Clamp(delta, -turn, turn));
    }

    private void Drive(ref CharacterState s, float speed)
    {
        ClearVelocityOwnership(ref s);
        s.FacingYaw = _yaw;
        s.VX = MathF.Sin(_yaw) * speed;
        s.VZ = MathF.Cos(_yaw) * speed;
        _prevX = s.PX;
        _prevZ = s.PZ;
        _measureTravel = speed > 0f;
    }

    private void Hold(ref CharacterState s)
    {
        ClearVelocityOwnership(ref s);
        s.FacingYaw = _yaw;
        s.VX = s.VZ = 0f;
        _measureTravel = false;
    }

    /// <summary>The gallop clip loops: its phase clip plays in real time from AttackElapsedTicks.</summary>
    private void LoopGallopClip(ref CharacterState s)
    {
        if (string.IsNullOrEmpty(_parameters.GallopAnimationId) || _parameters.GallopLoopTicks == 0) return;
        int elapsed = _phase == Phase.Gallop ? _gallopTicks : _phaseTicks;
        s.AttackElapsedTicks = (ushort)(elapsed % _parameters.GallopLoopTicks);
    }

    /// <summary>Switches to a phase clip listed in the stage, from its first frame.</summary>
    private void ShowClip(ref CharacterState s, string animationId)
    {
        if (string.IsNullOrEmpty(animationId)) return;
        int index = Array.IndexOf(AnimationNames, animationId);
        if (index <= 0 || index > byte.MaxValue) return;
        s.ComboStage = (byte)index;
        AnimIndex = (byte)index;
        s.AttackElapsedTicks = 0;
    }

    private void RemoveLance(ref CharacterState s)
    {
        if (_lanceHits == null) return;
        Resolver.RemoveOwnedHitboxes(s.EntityId, ActivationId, _lanceHits);
        _lanceHits = null;
    }

    private void Finish(ref CharacterState s)
    {
        _phase = Phase.Ended;
        EndAbility(ref s);
    }

    private void Emit(ref CharacterState s, CharacterDefinition def, string id, PresentationEventSource source)
    {
        if (string.IsNullOrEmpty(id)) return;
        PresentationSink?.Invoke(new TimelinePresentationEvent(0, s.EntityId, PresentationOperationIndex,
            id, PresentationAttackSequence, source, s.PX, s.PY - def.CapsuleHeight * 0.5f, s.PZ, s.FacingYaw));
    }

    public override void OnHitEntity(ref CharacterState attacker, ref CharacterState target,
        CharacterDefinition attackerDef, CharacterDefinition targetDef, ref float damage, ref float knockbackForce)
    {
        if (target.EntityId != attacker.EntityId && (_phase is Phase.Gallop or Phase.AirStrides))
        {
            _outcome = Outcome.Hit;
            // He stops dead on the hit. The hit ending holds him too, but only from the next ability tick: the hitstop
            // would otherwise release one more stride of gallop speed first.
            attacker.VX = attacker.VZ = 0f;
        }
    }

    public override void OnShieldBlock(ref CharacterState attacker, ref CharacterState defender)
    {
        if ((_phase is Phase.Gallop or Phase.AirStrides) && _outcome == Outcome.None)
            _outcome = Outcome.Blocked;
    }

    public override void OnEnd(ref CharacterState s)
    {
        _phase = Phase.Ended;
        if (s.State != ActionState.Hitstun && s.HitstunTicks == 0 && !Simulation.HasKnockback(s))
            s.VX = s.VZ = 0f;
    }

    public override void OnCancel(ref CharacterState s)
    {
        bool ownsVelocity = _phase is Phase.Gallop or Phase.AirStrides or Phase.MissEnding;
        _phase = Phase.Ended;
        if (ownsVelocity && s.State != ActionState.Hitstun && s.HitstunTicks == 0 && !Simulation.HasKnockback(s))
            s.VX = s.VZ = 0f;
    }

    private static float NormalizeYaw(float yaw)
    {
        while (yaw > MathF.PI) yaw -= 2f * MathF.PI;
        while (yaw < -MathF.PI) yaw += 2f * MathF.PI;
        return yaw;
    }

    internal override void WriteSnapshot(SnapshotWriter w)
    {
        base.WriteSnapshot(w);
        w.WriteByte((byte)_phase);
        w.WriteByte((byte)_outcome);
        w.WriteUShort(_phaseTicks);
        w.WriteUShort(_gallopTicks);
        w.WriteUShort(_airborneTicks);
        w.WriteFloat(_yaw);
        w.WriteFloat(_prevX);
        w.WriteFloat(_prevZ);
        w.WriteBool(_measureTravel);
        w.WriteBool(_startedThisTick);
        w.WriteSet(_lanceHits);
    }

    internal override void ReadSnapshot(SnapshotReader r)
    {
        base.ReadSnapshot(r);
        _phase = (Phase)r.ReadByte();
        _outcome = (Outcome)r.ReadByte();
        _phaseTicks = r.ReadUShort();
        _gallopTicks = r.ReadUShort();
        _airborneTicks = r.ReadUShort();
        _yaw = r.ReadFloat();
        _prevX = r.ReadFloat();
        _prevZ = r.ReadFloat();
        _measureTravel = r.ReadBool();
        _startedThisTick = r.ReadBool();
        _lanceHits = r.ReadSet();
    }
}
