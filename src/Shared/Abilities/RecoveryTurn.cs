using System;

namespace SlopArena.Shared.Abilities;

/// <summary>
/// A RecoveryMove goes where the stick points, not where the fighter faces (Melee's up-B reversal,
/// in 360°). The stick held on the press tick or either of the next two ticks turns the move; a neutral
/// stick keeps the current facing. Cursor-aimed recoveries keep their cursor.
/// </summary>
public static class RecoveryTurn
{
    /// <summary>The press tick and the two after it.</summary>
    public const ushort WindowTicks = 3;
    public const float StickDeadZone = 0.2f;

    public static bool Applies(CookedSlotDefinition slot)
        => slot.IsRecoveryMove && slot.AimMode == AuthoringAimMode.None;

    /// <summary>The stick's world yaw, when it is held past the dead zone.</summary>
    public static bool TryStickYaw(in InputState input, out float yaw)
    {
        yaw = 0f;
        if (input.MoveX * input.MoveX + input.MoveY * input.MoveY <= StickDeadZone * StickDeadZone)
            return false;
        yaw = MathF.Atan2(input.MoveX, input.MoveY);
        return true;
    }
}

/// <summary>
/// A capability whose travel direction was picked before the turn window closed. It re-reads
/// <c>FacingYaw</c> (already turned) and the stick when the move turns on a later window tick.
/// </summary>
public interface IRecoveryTurnCapability
{
    void OnRecoveryTurn(ref CharacterState s, in InputState input);
}
