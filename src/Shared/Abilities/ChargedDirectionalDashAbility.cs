using System;
using System.Collections.Generic;

namespace SlopArena.Shared.Abilities;

/// <summary>Charge in place, commit one aim direction, dash to a fixed endpoint, then recover.</summary>
public sealed class ChargedDirectionalDashAbility : ServerAbility, IAimHoldCapability
{
    private enum Phase : byte { Charge, Dash, Recovery, Ended }

    private readonly CookedChargedDirectionalDashCapabilityParameters _parameters;
    private Phase _phase;
    private float _dashYaw;
    private float _directionX;
    private float _directionZ;
    private float _startX;
    private float _startZ;
    private float _distance;
    private float _lastProgress;
    private float _commandedDistance;
    private ushort _dashTravelTicks;
    private ushort _recoveryTicks;
    private bool _finisherSpawned;
    private HashSet<ulong>? _traversalHitEntities;

    public override bool IgnoresFighterPushboxes => _phase == Phase.Dash;
    public override bool OwnsVerticalMotion => _phase == Phase.Dash;

    public ChargedDirectionalDashAbility(CookedChargedDirectionalDashCapabilityParameters parameters)
        => _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));

    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        _phase = Phase.Charge;
        _dashYaw = s.AimYaw;
        _directionX = _directionZ = 0f;
        _startX = _startZ = 0f;
        _distance = _lastProgress = _commandedDistance = 0f;
        _dashTravelTicks = _recoveryTicks = 0;
        _finisherSpawned = false;
        _traversalHitEntities = null;

        s.State = ActionState.Aiming;
        s.AttackSlot = (byte)(Slot + 1);
        s.ComboStage = 0;
        s.AttackElapsedTicks = 0;
        s.ChargeTicks = 0;
        s.IsAiming = true;
        s.StateTicks = 0;
        AnimIndex = 0;
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        switch (_phase)
        {
            case Phase.Charge:
                TickCharge(ref s, input);
                break;
            case Phase.Dash:
                TickDash(ref s);
                break;
            case Phase.Recovery:
                TickRecovery(ref s);
                break;
        }
    }

    private void TickCharge(ref CharacterState s, InputState input)
    {
        s.AttackElapsedTicks = 0;
        s.IsAiming = true;
        if (input.IsAiming)
        {
            _dashYaw = s.AimYaw;
            s.FacingYaw = _dashYaw;
            if (s.ChargeTicks < _parameters.MaxChargeTicks)
                s.ChargeTicks++;
            return;
        }

        StartDash(ref s);
    }

    private void StartDash(ref CharacterState s)
    {
        _phase = Phase.Dash;
        _distance = _parameters.GetDashDistance(s.ChargeTicks);
        _directionX = MathF.Sin(_dashYaw);
        _directionZ = MathF.Cos(_dashYaw);
        _startX = s.PX;
        _startZ = s.PZ;
        _lastProgress = 0f;
        _finisherSpawned = false;
        _recoveryTicks = 0;
        _dashTravelTicks = (ushort)Math.Max(1,
            (int)MathF.Ceiling(_distance / (_parameters.DashSpeed * Simulation.TickDt)));
        _commandedDistance = MathF.Min(_distance, _parameters.DashSpeed * Simulation.TickDt);

        s.State = ActionState.Attacking;
        s.IsAiming = false;
        s.FacingYaw = _dashYaw;
        s.AttackElapsedTicks = 0;
        ClearVelocityOwnership(ref s);
        s.IsFastFalling = false;
        s.VY = 0f;
        s.AnimLockTicks = (ushort)(_dashTravelTicks + _parameters.RecoveryTicks);
        s.VX = _directionX * (_commandedDistance / Simulation.TickDt);
        s.VZ = _directionZ * (_commandedDistance / Simulation.TickDt);

        // Travel pauses during Hitstop, but resolver lifetimes do not. Keep the same
        // hit history until StopDash explicitly removes traversal at the endpoint.
        _traversalHitEntities = SpawnHitbox(ref s, ToEvent(_parameters.TraversalHitbox,
            ushort.MaxValue, _parameters.TraversalHitbox.Damage), followOwner: true);
        if (_dashTravelTicks <= _parameters.FinisherLeadTicks)
            SpawnFinisher(ref s);
    }

    private void TickDash(ref CharacterState s)
    {
        s.IsAiming = false;
        s.FacingYaw = _dashYaw;

        float progress = (s.PX - _startX) * _directionX + (s.PZ - _startZ) * _directionZ;
        float moved = progress - _lastProgress;
        if (moved + 0.0001f < _commandedDistance)
        {
            StopDash(ref s, clearHitboxes: true);
            return;
        }
        _lastProgress = progress;

        float remaining = _distance - progress;
        if (remaining <= 0.0001f)
        {
            s.PX = _startX + _directionX * _distance;
            s.PZ = _startZ + _directionZ * _distance;
            StopDash(ref s, clearHitboxes: false);
            return;
        }

        if (!_finisherSpawned && remaining <= _parameters.DashSpeed * Simulation.TickDt * _parameters.FinisherLeadTicks)
        {
            SpawnFinisher(ref s);
        }
        else if (!_finisherSpawned)
        {
            ushort lastWindupPose = _parameters.FinisherSeekTick == 0
                ? (ushort)0 : (ushort)(_parameters.FinisherSeekTick - 1);
            if (s.AttackElapsedTicks > lastWindupPose)
                s.AttackElapsedTicks = lastWindupPose;
        }

        _commandedDistance = MathF.Min(_parameters.DashSpeed * Simulation.TickDt, remaining);
        float speed = _commandedDistance / Simulation.TickDt;
        s.VX = _directionX * speed;
        s.VZ = _directionZ * speed;
    }

    private void SpawnFinisher(ref CharacterState s)
    {
        _finisherSpawned = true;
        s.AttackElapsedTicks = _parameters.FinisherSeekTick;
        SpawnHitbox(ref s, ToEvent(_parameters.FinisherHitbox,
            _parameters.FinisherHitbox.DurationTicks,
            _parameters.GetFinisherDamage(s.ChargeTicks)), followOwner: true);
    }

    private void StopDash(ref CharacterState s, bool clearHitboxes)
    {
        if (_traversalHitEntities != null)
        {
            Resolver.RemoveOwnedHitboxes(s.EntityId, ActivationId, _traversalHitEntities);
            _traversalHitEntities = null;
        }
        if (clearHitboxes)
            Resolver.RemoveOwnedHitboxes(s.EntityId, ActivationId);

        _phase = Phase.Recovery;
        _recoveryTicks = 0;
        s.VX = s.VZ = 0f;
        s.AnimLockTicks = _parameters.RecoveryTicks;
        if (!_finisherSpawned && s.AttackElapsedTicks >= _parameters.FinisherSeekTick)
            s.AttackElapsedTicks = (ushort)(_parameters.FinisherSeekTick - 1);
    }

    private void TickRecovery(ref CharacterState s)
    {
        s.IsAiming = false;
        s.FacingYaw = _dashYaw;
        s.VX = s.VZ = 0f;
        if (!_finisherSpawned && s.AttackElapsedTicks >= _parameters.FinisherSeekTick)
            s.AttackElapsedTicks = (ushort)(_parameters.FinisherSeekTick - 1);
        if (++_recoveryTicks >= _parameters.RecoveryTicks)
        {
            _phase = Phase.Ended;
            EndAbility(ref s);
        }
    }

    public override void OnShieldBlock(ref CharacterState attacker, ref CharacterState defender)
    {
        if (_phase == Phase.Dash)
            StopDash(ref attacker, clearHitboxes: true);
    }

    public override void OnEnd(ref CharacterState s)
    {
        _phase = Phase.Ended;
        s.IsAiming = false;
        s.ChargeTicks = 0;
        if (s.State != ActionState.Hitstun && s.HitstunTicks == 0 && !Simulation.HasKnockback(s))
            s.VX = s.VZ = 0f;
    }

    public override void OnCancel(ref CharacterState s)
    {
        bool ownsDashVelocity = _phase == Phase.Dash;
        _phase = Phase.Ended;
        s.IsAiming = false;
        s.ChargeTicks = 0;
        if (ownsDashVelocity && s.State != ActionState.Hitstun && s.HitstunTicks == 0 && !Simulation.HasKnockback(s))
            s.VX = s.VZ = 0f;
    }

    private static HitboxEvent ToEvent(CookedHitbox hitbox, ushort durationTicks, float damage)
        => CookedCharacterRuntimeAdapter.ToHitboxEvent(hitbox) with
        {
            DurationTicks = durationTicks,
            Damage = damage,
            HitGroup = 0,
        };
}
