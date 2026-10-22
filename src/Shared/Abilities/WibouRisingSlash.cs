using System;

namespace SlopArena.Shared.Abilities;

/// <summary>
/// Wibou's E — Rising Slash. Movement for the cooked timeline.
///
/// - Rises vertically for "rise_ticks", carrying Wibou up (doubles as vertical recovery).
/// - Moves horizontally in the camera direction captured when the move starts, so the
///   recovery remains controllable and does not pull Wibou toward a target.
/// - The cooked timeline owns authored hitboxes and completion.
/// - Limited by the cooked charge pool (see ServerSimulation charge-stock gate): each cast
///   spends one charge, and charges return only through regen. Hits never refund them.
///
/// HomingRange remains reserved in the cooked schema; recovery uses camera direction.
/// </summary>
public sealed class WibouRisingSlash : ServerAbility
{
    public override bool OwnsVerticalMotion => true;

    private readonly CookedWibouRisingSlashCapabilityParameters _parameters;
    private ushort _ticks;
    private float _recoveryYaw;

    public WibouRisingSlash(CookedWibouRisingSlashCapabilityParameters parameters)
        => _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));

    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        ClearVelocityOwnership(ref s);
        s.IsFastFalling = false;
        _ticks = 0;
        _recoveryYaw = s.AimYaw;

        s.State = ActionState.Attacking;
        s.AttackSlot = (byte)(Slot + 1);
        s.FacingYaw = _recoveryYaw;
        AnimIndex = 0;
        s.ComboStage = 0;
        s.AttackElapsedTicks = 0;

        SetVelocity(ref s, 0f, _parameters.RiseSpeed, 0f);
        s.IsGrounded = false; // launch off the ground so the rise isn't clamped
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        ClearVelocityOwnership(ref s);
        s.IsFastFalling = false;
        _ticks++;

        float riseSpeed = _parameters.RiseSpeed;
        ushort riseTicks = _parameters.RiseTicks;

        // Vertical rise for the rise window, then hold (gravity resumes when the ability ends).
        s.VY = _ticks <= riseTicks ? riseSpeed : 0f;

        // Recovery direction is chosen from the camera at activation, never from target position.
        float horizontalSpeed = _parameters.HomingSpeed;
        s.VX = MathF.Sin(_recoveryYaw) * horizontalSpeed;
        s.VZ = MathF.Cos(_recoveryYaw) * horizontalSpeed;
    }

    public override void OnCancel(ref CharacterState s)
    {
        ClearVelocityOwnership(ref s);
        s.IsFastFalling = false;
        s.VX = 0f;
        s.VY = 0f;
        s.VZ = 0f;
    }

    internal override void WriteSnapshot(SnapshotWriter w)
    {
        base.WriteSnapshot(w);
        w.WriteUShort(_ticks);
        w.WriteFloat(_recoveryYaw);
    }

    internal override void ReadSnapshot(SnapshotReader r)
    {
        base.ReadSnapshot(r);
        _ticks = r.ReadUShort();
        _recoveryYaw = r.ReadFloat();
    }
}
