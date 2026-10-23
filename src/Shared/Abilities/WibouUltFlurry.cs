using System;

namespace SlopArena.Shared.Abilities;

/// <summary>
/// Wibou's F — Blade Flurry (ult). A committed multi-slash that ends in a
/// hard launch. This capability owns movement; the cooked timeline owns hitboxes and completion.
/// </summary>
public sealed class WibouUltFlurry : ServerAbility
{
    public override bool OwnsVerticalMotion => true;

    private readonly CookedWibouBladeFlurryCapabilityParameters _parameters;
    private ushort _ticks;
    public WibouUltFlurry(CookedWibouBladeFlurryCapabilityParameters parameters)
        => _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        ClearVelocityOwnership(ref s);
        s.IsFastFalling = false;

        _ticks = 0;
        s.State = ActionState.Attacking;
        s.AttackSlot = (byte)(Slot + 1);
        AnimIndex = 0;
        s.ComboStage = 0;
        s.AttackElapsedTicks = 0;

        SetVelocityInFacing(ref s, _parameters.ForwardSpeed);
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        ClearVelocityOwnership(ref s);
        s.IsFastFalling = false;
        _ticks++;

        if (_ticks >= _parameters.AscentStartTick && _ticks <= _parameters.AscentEndTick)
        {
            SetVelocityInFacing(ref s, _parameters.ForwardSpeed);
            s.VY = _parameters.RiseSpeed;
        }
        else if (_ticks < _parameters.DescentStartTick)
        {
            s.VX = 0f;
            s.VZ = 0f;
            s.VY = 0f;
        }
        else
        {
            s.VX = 0f;
            s.VZ = 0f;
            s.VY = -_parameters.DescentSpeed;
        }
    }
    public override void OnCancel(ref CharacterState s)
    {
        ClearVelocityOwnership(ref s);
        s.VX = 0f;
        s.VZ = 0f;
    }

    internal override void WriteSnapshot(SnapshotWriter w)
    {
        base.WriteSnapshot(w);
        w.WriteUShort(_ticks);
    }

    internal override void ReadSnapshot(SnapshotReader r)
    {
        base.ReadSnapshot(r);
        _ticks = r.ReadUShort();
    }
}
