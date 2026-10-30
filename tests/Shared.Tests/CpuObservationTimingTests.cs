using SlopArena.Shared.AI;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class CpuObservationTimingTests
{
    private static CharacterDefinition Kit(ushort iasa = 70)
        => TestHelpers.WithMove(TestHelpers.EngineDef, AbilitySlots.Slot1, false, "Long whiff", 90, iasa,
            hits: new[] { new HitboxEvent { TriggerTick = 5, DurationTicks = 2, Radius = 0.4f, OffZ = 1f, Damage = 5f } });
    private static CharacterState Attacking(ushort elapsed = 12)
        => TestHelpers.NpcState(0f, 2f) with { State = ActionState.Attacking, AttackSlot = AbilitySlots.Slot1,
            AttackElapsedTicks = elapsed, AnimLockTicks = (ushort)(90 - elapsed) };

    [Fact]
    public void RecoveryMetadataAndEpisodeAreDelayedTogether()
    {
        var def = Kit(); var policy = new HeuristicBotPolicy(); var memory = new BotMemory();
        var active = def.GetCookedSlotAbility(AbilitySlots.Slot1, false);
        for (int tick = 0; tick < 18; tick++)
        {
            policy.ObserveOpponent(memory, Attacking(), def, activeMove: active);
            Assert.False(memory.TryGetDelayedOpponent(out _));
        }
        policy.ObserveOpponent(memory, Attacking(30), def, activeMove: active);
        Assert.True(memory.TryGetDelayedOpponent(out var seen));
        Assert.Equal(CpuAttackPhase.Recovery, seen.Timing.Phase);
        Assert.NotEqual(0u, seen.Timing.RecoveryEpisode);
        Assert.Equal(58, seen.Timing.LockTicks);
        Assert.True(RecoveryOpportunity.TryFromDelayed(seen, memory.Clock, out var opening));
        Assert.Equal(40, opening.Deadline - memory.Clock);
    }

    [Fact]
    public void IasaAndHitstopBoundTheAgedOpening()
    {
        var def = Kit(20); var policy = new HeuristicBotPolicy(); var memory = new BotMemory();
        var target = Attacking() with { HitstopTicks = 15 };
        for (int i = 0; i <= 18; i++) policy.ObserveOpponent(memory, target, def, activeMove: def.GetCookedSlotAbility(AbilitySlots.Slot1, false));
        Assert.True(memory.TryGetDelayedOpponent(out var seen));
        Assert.Equal(8, seen.Timing.LockTicks);
        Assert.True(RecoveryOpportunity.TryFromDelayed(seen, memory.Clock, out var opening));
        Assert.Equal(5, opening.Deadline - memory.Clock);
        var expired = seen.WithAge(23);
        Assert.False(RecoveryOpportunity.TryFromDelayed(expired, memory.Clock + 5, out _));
    }

    [Fact]
    public void RepeatedAttackSnapshotsKeepOneEpisodeAndRestartGetsAnother()
    {
        var def = Kit(); var policy = new HeuristicBotPolicy(); var memory = new BotMemory();
        for (int i = 0; i <= 18; i++) policy.ObserveOpponent(memory, Attacking(), def, activeMove: def.GetCookedSlotAbility(AbilitySlots.Slot1, false));
        memory.TryGetDelayedOpponent(out var first);
        Assert.NotEqual(0u, first.Timing.AttackEpisode);
        for (int i = 0; i < 20; i++) policy.ObserveOpponent(memory, Attacking(13), def, activeMove: def.GetCookedSlotAbility(AbilitySlots.Slot1, false));
        memory.TryGetDelayedOpponent(out var repeated);
        Assert.Equal(first.Timing.AttackEpisode, repeated.Timing.AttackEpisode);
        for (int i = 0; i < 20; i++) policy.ObserveOpponent(memory, Attacking(1), def, activeMove: def.GetCookedSlotAbility(AbilitySlots.Slot1, false));
        memory.TryGetDelayedOpponent(out var restarted);
        Assert.NotEqual(first.Timing.AttackEpisode, restarted.Timing.AttackEpisode);
    }

    [Fact]
    public void BareUnknownAttackDoesNotInventARecoveryWindow()
    {
        var memory = new BotMemory(); var policy = new HeuristicBotPolicy();
        for (int i = 0; i <= 18; i++) policy.ObserveOpponent(memory, Attacking(), Kit());
        Assert.True(memory.TryGetDelayedOpponent(out var seen));
        Assert.False(RecoveryOpportunity.TryFromDelayed(seen, memory.Clock, out _));
    }

    [Fact]
    public void LandingLagHasItsOwnDelayedOpening()
    {
        var memory = new BotMemory(); var policy = new HeuristicBotPolicy(); var def = Kit();
        var target = TestHelpers.NpcState() with { LandingLagTicks = 45 };
        for (int i = 0; i <= 18; i++) policy.ObserveOpponent(memory, target, def);
        memory.TryGetDelayedOpponent(out var seen);
        Assert.True(RecoveryOpportunity.TryFromDelayed(seen, memory.Clock, out var opening));
        Assert.Equal(27, opening.Deadline - memory.Clock);
    }
}
