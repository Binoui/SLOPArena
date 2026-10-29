using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

namespace SlopArena.Shared;

public sealed record AbilityLabTimelineProjection
{
    public IReadOnlyList<AbilityLabStageProjection> Stages { get; }
    public int DurationTicks { get; }

    private AbilityLabTimelineProjection(IReadOnlyList<AbilityLabStageProjection> stages, int durationTicks)
    {
        Stages = stages;
        DurationTicks = durationTicks;
    }
    public static int SnapTick(double normalized, int durationTicks)
    {
        if (durationTicks <= 0) return 0;
        normalized = Math.Clamp(normalized, 0d, 1d);
        return Math.Clamp((int)Math.Round(normalized * durationTicks, MidpointRounding.AwayFromZero), 0, durationTicks);
    }

    public static int ClampOperationTick(int tick, int stageDurationTicks)
        => stageDurationTicks <= 0 ? 0 : Math.Clamp(tick, 0, stageDurationTicks - 1);

    public static int ClampHitboxDuration(int triggerTick, int durationTicks, int stageDurationTicks)
    {
        if (stageDurationTicks <= 0) return 0;
        var maxDuration = stageDurationTicks - ClampOperationTick(triggerTick, stageDurationTicks);
        return Math.Clamp(durationTicks, 1, maxDuration);
    }

    public static int ClampPresentationDuration(int triggerTick, int durationTicks, int stageDurationTicks)
    {
        if (stageDurationTicks <= 0) return 0;
        var maxDuration = stageDurationTicks - ClampOperationTick(triggerTick, stageDurationTicks);
        return Math.Clamp(durationTicks, 1, maxDuration);
    }


    public static AbilityLabTimelineProjection Build(CharacterSlotSource slot)
    {
        if (slot == null) throw new ArgumentNullException(nameof(slot));

        var stages = new List<AbilityLabStageProjection>();
        int startTick = 0;
        var sourceStages = slot.Timeline?.Stages ?? Array.Empty<CharacterStageSource>();
        for (int stageIndex = 0; stageIndex < sourceStages.Count; stageIndex++)
        {
            var sourceStage = sourceStages[stageIndex] ?? throw new InvalidDataException($"Stage {stageIndex} is null.");
            int endTick = startTick + sourceStage.DurationTicks;
            var operations = new List<AbilityLabOperationProjection>();
            var sourceOperations = sourceStage.Operations ?? Array.Empty<CharacterTimelineOperationSource>();
            for (int operationIndex = 0; operationIndex < sourceOperations.Count; operationIndex++)
            {
                var operation = sourceOperations[operationIndex] ?? throw new InvalidDataException($"Operation {stageIndex}:{operationIndex} is null.");
                int operationStart = startTick + operation.Tick;
                int operationEnd = operation switch
                {
                    SpawnHitboxOperationSource hitbox => operationStart + hitbox.Hitbox.DurationTicks,
                    GravityWindowOperationSource gravity => operationStart + gravity.DurationTicks,
                    ArmorWindowOperationSource armorWindow => operationStart + armorWindow.DurationTicks,
                    ForwardLungeOperationSource lunge => operationStart + lunge.DurationTicks,
                    EmitPresentationOperationSource presentation => operationStart + presentation.Placement.DurationTicks,
                    StartupAimCorrectionOperationSource correction => startTick + correction.EndTick,
                    _ => operationStart + 1,
                };
                operations.Add(new AbilityLabOperationProjection(
                    stageIndex,
                    operationIndex,
                    operationStart,
                    operationEnd,
                    KindOf(operation),
                    SummaryOf(operation),
                    operation));
            }

            stages.Add(new AbilityLabStageProjection(
                stageIndex,
                startTick,
                endTick,
                sourceStage.DurationTicks,
                sourceStage.IasaTicks,
                sourceStage.LandingLagTicks,
                sourceStage.AutoCancelBeforeTicks,
                sourceStage.AutoCancelAfterTicks,
                sourceStage.AnimationIds ?? Array.Empty<string>(),
                operations));
            startTick = endTick;
        }

        return new AbilityLabTimelineProjection(
            new ReadOnlyCollection<AbilityLabStageProjection>(stages), startTick);
    }

    private static CookedOperationKind KindOf(CharacterTimelineOperationSource operation) => operation switch
    {
        SetVelocityOperationSource => CookedOperationKind.SetVelocity,
        ForwardLungeOperationSource => CookedOperationKind.ForwardLunge,
        GravityWindowOperationSource => CookedOperationKind.GravityWindow,
        ArmorWindowOperationSource => CookedOperationKind.ArmorWindow,
        SpawnHitboxOperationSource => CookedOperationKind.SpawnHitbox,
        SpawnProjectileOperationSource => CookedOperationKind.SpawnProjectile,
        SetAimStateOperationSource => CookedOperationKind.SetAimState,
        StartCapabilityOperationSource => CookedOperationKind.StartCapability,
        EmitPresentationOperationSource => CookedOperationKind.EmitPresentation,
        CompleteTimelineOperationSource => CookedOperationKind.CompleteTimeline,
        SwapFormOperationSource => CookedOperationKind.SwapForm,
        StartupAimCorrectionOperationSource => CookedOperationKind.StartupAimCorrection,
        _ => throw new InvalidDataException($"Unknown timeline operation type '{operation.GetType().FullName}'."),
    };

    private static string SummaryOf(CharacterTimelineOperationSource operation) => operation switch
    {
        SetVelocityOperationSource => "Set velocity",
        ForwardLungeOperationSource => "Forward lunge",
        GravityWindowOperationSource => "Gravity window",
        ArmorWindowOperationSource => "Armor window",
        SpawnHitboxOperationSource => "Hitbox",
        SpawnProjectileOperationSource => "Projectile",
        SetAimStateOperationSource => "Set aim",
        StartCapabilityOperationSource => "Start ability",
        EmitPresentationOperationSource => "Presentation",
        CompleteTimelineOperationSource => "Complete move",
        SwapFormOperationSource => "Swap form",
        StartupAimCorrectionOperationSource => "Startup aim correction",
        _ => throw new InvalidDataException($"Unknown timeline operation type '{operation.GetType().FullName}'."),
    };
}

public sealed record AbilityLabStageProjection
{
    public int SourceStageIndex { get; }
    public int StartTick { get; }
    public int EndTick { get; }
    public ushort DurationTicks { get; }
    public ushort IasaTicks { get; }
    public ushort LandingLagTicks { get; }
    public ushort AutoCancelBeforeTicks { get; }
    public ushort AutoCancelAfterTicks { get; }
    public IReadOnlyList<string> AnimationIds { get; }
    public IReadOnlyList<AbilityLabOperationProjection> Operations { get; }

    public AbilityLabStageProjection(
        int sourceStageIndex,
        int startTick,
        int endTick,
        ushort durationTicks,
        ushort iasaTicks,
        ushort landingLagTicks,
        ushort autoCancelBeforeTicks,
        ushort autoCancelAfterTicks,
        IReadOnlyList<string> animationIds,
        IReadOnlyList<AbilityLabOperationProjection> operations)
    {
        SourceStageIndex = sourceStageIndex;
        StartTick = startTick;
        EndTick = endTick;
        DurationTicks = durationTicks;
        IasaTicks = iasaTicks;
        LandingLagTicks = landingLagTicks;
        AutoCancelBeforeTicks = autoCancelBeforeTicks;
        AutoCancelAfterTicks = autoCancelAfterTicks;
        AnimationIds = new ReadOnlyCollection<string>(new List<string>(animationIds ?? Array.Empty<string>()));
        Operations = new ReadOnlyCollection<AbilityLabOperationProjection>(new List<AbilityLabOperationProjection>(operations ?? Array.Empty<AbilityLabOperationProjection>()));
    }
}

public sealed record AbilityLabOperationProjection
{
    public int SourceStageIndex { get; }
    public int SourceOperationIndex { get; }
    public int StartTick { get; }
    public int EndTick { get; }
    public CookedOperationKind Kind { get; }
    public string Summary { get; }
    public CharacterTimelineOperationSource Source { get; }

    public AbilityLabOperationProjection(
        int sourceStageIndex,
        int sourceOperationIndex,
        int startTick,
        int endTick,
        CookedOperationKind kind,
        string summary,
        CharacterTimelineOperationSource source)
    {
        SourceStageIndex = sourceStageIndex;
        SourceOperationIndex = sourceOperationIndex;
        StartTick = startTick;
        EndTick = endTick;
        Kind = kind;
        Summary = summary;
        Source = source;
    }
}
