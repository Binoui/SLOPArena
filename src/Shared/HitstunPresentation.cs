namespace SlopArena.Shared;

/// <summary>Presentation uses reaction identity, not snapshot arrival cadence.</summary>
public static class HitstunPresentation
{
    public static bool ShouldRestart(ActionState lastPresentedState, ushort presentedSequence,
        in CharacterState current) => current.State == ActionState.Hitstun && current.HitstopTicks == 0 &&
        (lastPresentedState != ActionState.Hitstun || presentedSequence != current.HitReactionSequence);
}
