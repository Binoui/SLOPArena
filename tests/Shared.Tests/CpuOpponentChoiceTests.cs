using System;
using System.Collections.Generic;
using SlopArena.Shared.AI;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// The free-for-all opponent rule: each tick the caller observes every fighter the CPU may fight, chooses,
/// then decides. The rule reads delayed positions only, holds its choice for a dwell and through commitment,
/// and replaces an opponent that died or left at once.
/// </summary>
public sealed class CpuOpponentChoiceTests
{
    private const ulong Self = 1, Near = 2, Far = 3;
    private static readonly int Delay = BotDifficultyProfile.ForDifficulty(CpuDifficulty.Normal).ReactionDelayTicks;

    private sealed class Room
    {
        public readonly BotMemory Memory = new() { Difficulty = CpuDifficulty.Normal };
        public CharacterState Cpu = Fighter(Self, 0f, 0f);
        public readonly List<CharacterState> Others = new();

        public ulong Tick()
        {
            foreach (var other in Others)
                Memory.ObserveOpponent(other);
            ulong chosen = Memory.ChooseOpponent(Cpu);
            Assert.Equal(chosen, Memory.ChooseOpponent(Cpu));
            Memory.EndTick();
            return chosen;
        }

        public void Move(ulong id, float x, float z, byte deaths = 0)
        {
            int index = Others.FindIndex(o => o.EntityId == id);
            Others[index] = Others[index] with { PX = x, PZ = z, Deaths = deaths };
        }
    }

    private static CharacterState Fighter(ulong id, float x, float z)
        => TestHelpers.PlayerState(x, z) with { EntityId = id };

    /// <summary>Two fighters observed until the first choice; returns the room and the first choice's tick count.</summary>
    private static Room Started(out int ticks)
    {
        var room = new Room();
        room.Others.Add(Fighter(Near, 0f, 3f));
        room.Others.Add(Fighter(Far, 0f, 6f));
        ticks = 0;
        while (room.Tick() == 0)
            ticks++;
        Assert.Equal(Delay, ticks);
        Assert.Equal(Near, room.Memory.OpponentId);
        return room;
    }

    [Fact]
    public void NobodyIsChosenUntilADelayedObservationExists()
    {
        var room = new Room();
        Assert.Equal(0ul, room.Tick());
        room.Others.Add(Fighter(Near, 0f, 3f));
        for (int tick = 0; tick < Delay; tick++)
            Assert.Equal(0ul, room.Tick());
        Assert.False(room.Memory.TryGetDelayedOpponent(out _));
        Assert.Equal(Near, room.Tick());
    }

    [Fact]
    public void EqualDistancesGoToTheLowerEntityId()
    {
        var room = new Room();
        room.Others.Add(Fighter(Far, 4f, 0f));
        room.Others.Add(Fighter(Near, 0f, 4f));
        ulong chosen = 0;
        for (int tick = 0; tick <= Delay; tick++)
            chosen = room.Tick();
        Assert.Equal(Near, chosen);
    }

    [Fact]
    public void ANearerFighterTakesOverOnlyAfterTheDwell()
    {
        var room = Started(out _);
        room.Move(Far, 0f, 1f);
        for (int tick = 1; tick < 120; tick++)
            Assert.Equal(Near, room.Tick());
        Assert.Equal(Far, room.Tick());
        Assert.Equal(1, room.Memory.OpponentSwitches);
    }

    [Fact]
    public void AFighterLessThanTwentyPercentNearerDoesNotTakeOver()
    {
        var room = Started(out _);
        room.Move(Far, 0f, 2.5f);
        for (int tick = 1; tick < 200; tick++)
            Assert.Equal(Near, room.Tick());
        room.Move(Far, 0f, 2.2f);
        ulong chosen = Near;
        for (int tick = 0; tick <= Delay; tick++)
            chosen = room.Tick();
        Assert.Equal(Far, chosen);
    }

    [Fact]
    public void ACommittedCpuKeepsItsOpponentPastTheDwell()
    {
        var room = Started(out _);
        room.Move(Far, 0f, 1f);
        room.Cpu = room.Cpu with { State = ActionState.Attacking, AttackSlot = AbilitySlots.Slot1 };
        for (int tick = 1; tick < 150; tick++)
            Assert.Equal(Near, room.Tick());
        room.Cpu = room.Cpu with { State = ActionState.Grabbing, AttackSlot = 0, InteractionId = 7 };
        Assert.Equal(Near, room.Tick());
        room.Cpu = room.Cpu with { State = ActionState.Idle, InteractionId = 0 };
        Assert.Equal(Far, room.Tick());
    }

    [Fact]
    public void AnOpponentThatDiesIsReplacedAtOnceWithoutABlindTick()
    {
        var room = Started(out _);
        for (int tick = 0; tick < 10; tick++)
            room.Tick();
        room.Move(Near, 0f, 3f, deaths: 1);
        Assert.Equal(Far, room.Tick());
        Assert.True(room.Memory.TryGetDelayedOpponent(out var seen));
        Assert.Equal(6f, seen.PZ);
        Assert.Equal(1, room.Memory.OpponentSwitches);
    }

    [Fact]
    public void AnOpponentNoLongerObservedIsReplacedAtOnce()
    {
        var room = Started(out _);
        room.Others.RemoveAt(0);
        Assert.Equal(Far, room.Tick());
        room.Others.Clear();
        Assert.Equal(0ul, room.Tick());
        Assert.False(room.Memory.TryGetDelayedOpponent(out _));
    }

    [Fact]
    public void OnlyHitsOnTheCurrentOpponentBecomeFollowUps()
    {
        var room = Started(out _);
        room.Memory.RecordOpponentHit(AbilitySlots.Slot1, false, room.Others[1], 30, 0);
        for (int tick = 0; tick <= Delay; tick++)
            room.Tick();
        Assert.False(room.Memory.TryGetNewHit(out _));
        room.Memory.RecordOpponentHit(AbilitySlots.Slot1, false, room.Others[0], 30, 0);
        for (int tick = 0; tick <= Delay; tick++)
            room.Tick();
        Assert.True(room.Memory.TryGetNewHit(out var hit));
        Assert.Equal(AbilitySlots.Slot1, hit.Slot);
    }

    [Fact]
    public void TheCpusInputNamesTheOpponentItChose()
    {
        var def = TestHelpers.FightGuyDef;
        var room = Started(out _);
        room.Move(Far, 0f, 1f);
        for (int tick = 1; tick <= 120; tick++)
            room.Tick();
        Assert.Equal(Far, room.Memory.OpponentId);
        foreach (var other in room.Others)
            room.Memory.ObserveOpponent(other);
        room.Memory.ChooseOpponent(room.Cpu);
        var input = new HeuristicBotPolicy().Decide(room.Cpu with { PY = TestHelpers.GroundPY(def) }, def,
            new Random(1), room.Memory);
        Assert.Equal((byte)Far, input.TargetEntityId);
    }
}
