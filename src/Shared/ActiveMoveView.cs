using System;

namespace SlopArena.Shared;

/// <summary>The presentation clock, separate from the running timeline stage.</summary>
public readonly record struct AttackPose(string AnimationId, float ElapsedTicks, ushort DurationTicks, bool IsPhaseClip)
{
    public float FrameAt(int frameCount) => IsPhaseClip
        ? Math.Clamp(ElapsedTicks, 0f, Math.Max(0, frameCount - 1))
        : HitboxGeometry.AttackFrame(ElapsedTicks, frameCount, DurationTicks);
}

/// <summary>Immutable clip metadata derived from the cooked timeline.</summary>
public readonly record struct MoveClip(string AnimationId, int StageIndex, ushort StartTick,
    ushort DurationTicks, bool IsPhaseClip)
{
    public AttackPose At(float elapsedTicks)
        => new(AnimationId, Math.Max(0f, elapsedTicks - StartTick), DurationTicks, IsPhaseClip);
}

/// <summary>A computed read of an activation's cooked move, timeline cursor and presentation pose.</summary>
public readonly record struct ActiveMoveView(
    CookedSlotDefinition Slot, int StageIndex, ushort StageTick, bool AirborneAtStart,
    byte ClipIndex, AttackPose Pose)
{
    public CookedStage Stage => Slot.Timeline.Stages[StageIndex];

    internal static AttackPose ResolvePose(CookedSlotDefinition slot, int clipIndex, float elapsedTicks)
        => slot.Clip(clipIndex).At(elapsedTicks);
}
