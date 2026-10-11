using System;

namespace SlopArena.Shared.Abilities;

/// <summary>
/// Wibou's F — Blade Flurry (ult). A committed forward-moving multi-slash that ends in a
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

        // F timing is authored at 30 FPS: ascend on frames 7-14 (ticks 14-28),
        // float at roughly two metres above launch height until frame 30 (tick 60),
        // then descend through the spike.
        if (_ticks >= 14 && _ticks <= 28)
        {
            SetVelocityInFacing(ref s, _parameters.ForwardSpeed);
            s.VY = 60f * 2f / (28f - 14f);
        }
        else if (_ticks < 60)
        {
            s.VX = 0f;
            s.VZ = 0f;
            s.VY = 0f;
        }
        else
        {
            s.VX = 0f;
            s.VZ = 0f;
            s.VY = -12f;
        }
    }
    public override void OnCancel(ref CharacterState s)
    {
        ClearVelocityOwnership(ref s);
        s.VX = 0f;
        s.VZ = 0f;
    }
}
