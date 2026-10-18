using System;

namespace SlopArena.Shared.Abilities;

/// <summary>Hold a capped three-tier charge; release one projectile after authored startup.</summary>
public sealed class ChargedProjectileAbility : ServerAbility, IAimHoldCapability
{
    private readonly CookedChargedProjectileCapabilityParameters _parameters;
    private bool _released;
    private bool _fired;
    private float _radius;
    private float _aimYaw;
    private float _aimPitch;

    public ChargedProjectileAbility(CookedChargedProjectileCapabilityParameters parameters)
        => _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));

    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        _released = _fired = false;
        _radius = _parameters.Projectile.Radius;
        _aimYaw = s.AimYaw;
        _aimPitch = s.AimPitch;
        s.State = ActionState.Aiming;
        s.AttackSlot = (byte)(Slot + 1);
        s.AttackElapsedTicks = 0;
        s.ChargeTicks = 0;
        s.IsAiming = true;
        s.ComboStage = 0;
        AnimIndex = 0;
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        if (!_released)
        {
            if (input.IsAiming)
            {
                if (s.ChargeTicks < _parameters.MaxChargeTicks) s.ChargeTicks++;
                _aimYaw = s.AimYaw;
                _aimPitch = s.AimPitch;
                return;
            }
            _released = true;
            _radius = _parameters.GetRadius(s.ChargeTicks);
            s.State = ActionState.Attacking;
            s.IsAiming = false;
            s.AttackElapsedTicks = 0;
        }
        if (!_fired && s.AttackElapsedTicks >= _parameters.FireTriggerTick)
        {
            _fired = true;
            var slot = def.GetCookedSlotAbility((byte)(Slot + 1), AirborneAtStart);
            bool autoTarget = slot?.AimMode == AuthoringAimMode.None;
            s.AimYaw = _aimYaw;
            s.AimPitch = _aimPitch;
            SpawnCookedProjectile(ref s, _parameters.Projectile with { Radius = _radius },
                PresentationOperationIndex, autoTarget);
        }
    }

    internal override void WriteSnapshot(SnapshotWriter w)
    {
        base.WriteSnapshot(w);
        w.WriteBool(_released);
        w.WriteBool(_fired);
        w.WriteFloat(_radius);
        w.WriteFloat(_aimYaw);
        w.WriteFloat(_aimPitch);
    }

    internal override void ReadSnapshot(SnapshotReader r)
    {
        base.ReadSnapshot(r);
        _released = r.ReadBool();
        _fired = r.ReadBool();
        _radius = r.ReadFloat();
        _aimYaw = r.ReadFloat();
        _aimPitch = r.ReadFloat();
    }
}
