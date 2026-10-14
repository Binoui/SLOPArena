using System;

namespace SlopArena.Shared.Abilities;

/// <summary>
/// Wibou's E — Rising Slash. Movement and charge refunds for the cooked timeline.
///
/// - Rises vertically for "rise_ticks", carrying Wibou up (doubles as vertical recovery).
/// - Moves horizontally in the camera direction captured when the move starts, so the
///   recovery remains controllable and does not pull Wibou toward a target.
/// - The cooked timeline owns authored hitboxes and completion.
/// - Limited by a refundable charge pool (see ServerSimulation charge-stock gate): each cast
///   spends one charge; landing a hit refunds it (OnHitEntity) so a connected juggle sustains,
///   while whiffing in empty air burns charges — capping recovery to the pool size.
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

    /// <summary>Refund the spent charge on a connect so a landed juggle keeps its charges.</summary>
    public override void OnHitEntity(ref CharacterState attacker, ref CharacterState target,
        CharacterDefinition attackerDef, CharacterDefinition targetDef,
        ref float damage, ref float knockbackForce)
        => RefundCharge(ref attacker);

    public override void OnHitObject(ref CharacterState attacker, CharacterDefinition attackerDef,
        ref float damage, ref float knockbackForce) => RefundCharge(ref attacker);

    private static void RefundCharge(ref CharacterState attacker)
    {
        if (attacker.ChargeStockSpent > 0)
        {
            attacker.ChargeStockSpent--;
            // Refunding to a full pool stops regen; clear the stale timer so the NEXT spend
            // starts a fresh full regen period rather than reusing the partial countdown.
            if (attacker.ChargeStockSpent == 0)
                attacker.ChargeStockRegenTicks = 0;
        }
    }
    public override void OnCancel(ref CharacterState s)
    {
        ClearVelocityOwnership(ref s);
        s.IsFastFalling = false;
        s.VX = 0f;
        s.VY = 0f;
        s.VZ = 0f;
    }
}
