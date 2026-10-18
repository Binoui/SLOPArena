
namespace SlopArena.Shared.Abilities;

/// <summary>Hold F to aim the aerosol stream, then release to fire.</summary>
public sealed class MankiAerosolInferno : ServerAbility, IAimHoldCapability
{
    public override bool OwnsVerticalMotion => true;

    private readonly CookedMankiAerosolInfernoCapabilityParameters _parameters;
    private bool _fired;
    private bool _released;

    public MankiAerosolInferno(CookedMankiAerosolInfernoCapabilityParameters parameters)
    {
        _parameters = parameters;
    }

    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        ClearVelocityOwnership(ref s);
        s.IsFastFalling = false;
        _fired = false;
        _released = false;
        s.State = ActionState.Aiming;
        s.IsAiming = true;
        s.ComboStage = 0;
        s.AttackElapsedTicks = 0;
        s.ChargeTicks = 0;
        s.VX = 0f;
        s.VY = 0f;
        s.VZ = 0f;
        AnimIndex = 0;
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        ClearVelocityOwnership(ref s);
        s.IsFastFalling = false;
        if (!_released)
        {
            s.VX = 0f;
            s.VY = 0f;
            s.VZ = 0f;
            s.FacingYaw = s.AimYaw;
            // A tap queues release, but cannot skip the authored minimum charge.
            if (s.ChargeTicks < _parameters.MinimumChargeTicks)
            {
                s.ChargeTicks++;
                return;
            }
            if (input.IsAiming)
            {
                if (s.ChargeTicks < _parameters.MaxChargeTicks) s.ChargeTicks++;
                return;
            }

            _released = true;
            s.State = ActionState.Attacking;
            s.IsAiming = false;
            s.ComboStage = 1;
            s.AttackElapsedTicks = 0;
            s.AnimLockTicks = _parameters.FireDurationTicks;
            AnimIndex = 1;
            return;
        }

        if (!_fired && s.AttackElapsedTicks >= _parameters.FireTriggerTick)
        {
            _fired = true;
            SpawnFlame(ref s);
        }

        if (s.AttackElapsedTicks >= _parameters.FireDurationTicks)
            EndAbility(ref s);
    }

    public override void OnEnd(ref CharacterState s)
    {
        s.IsAiming = false;
        s.ChargeTicks = 0;
    }

    public override void OnCancel(ref CharacterState s)
    {
        s.IsAiming = false;
        s.ChargeTicks = 0;
    }

    public PresentationPlacement ScaleFlamePresentation(ushort chargeTicks, PresentationPlacement placement)
    {
        var scale = _parameters.GetChargeTier(chargeTicks).ReachScale;
        return placement with
        {
            LocalScaleX = placement.LocalScaleX * scale,
            LocalScaleY = placement.LocalScaleY * scale,
            LocalScaleZ = placement.LocalScaleZ * scale,
        };
    }

    private void SpawnFlame(ref CharacterState s)
    {
        var tier = _parameters.GetChargeTier(s.ChargeTicks);
        SpawnHitbox(ref s, new HitboxEvent
        {
            TriggerTick = _parameters.FireTriggerTick,
            DurationTicks = _parameters.HitboxDurationTicks,
            Shape = HitboxShape.Capsule,
            Radius = _parameters.HitboxRadius * tier.ReachScale,
            OffY = _parameters.OffsetY,
            // Keep the near edge anchored while the capsule grows forward.
            OffZ = _parameters.OffsetZ - _parameters.HitboxRadius + _parameters.HitboxRadius * tier.ReachScale,
            EndOffZ = _parameters.EndOffsetZ * tier.ReachScale,
            Damage = tier.Damage,
            Knockback = new KnockbackData
            {
                Profile = KnockbackProfile.Custom,
                Angle = (sbyte)_parameters.KnockbackAngle,
                BaseKnockback = _parameters.KnockbackBase,
                KnockbackGrowth = _parameters.KnockbackGrowth,
            },
            StunTicks = _parameters.StunTicks,
            Interruptible = true,
            HitGroup = _parameters.HitGroup,
            KnockbackDirection = AuthoringKnockbackDirection.AwayFromOwner,
        });
    }

    internal override void WriteSnapshot(SnapshotWriter w)
    {
        base.WriteSnapshot(w);
        w.WriteBool(_fired);
        w.WriteBool(_released);
    }

    internal override void ReadSnapshot(SnapshotReader r)
    {
        base.ReadSnapshot(r);
        _fired = r.ReadBool();
        _released = r.ReadBool();
    }
}
