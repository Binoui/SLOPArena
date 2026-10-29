using System.Linq;
using Xunit;

namespace SlopArena.Shared.Tests;

public class RosterRollbackConvergenceTests
{
    [Theory]
    [InlineData(CharacterClass.FightGuy, 0)]
    [InlineData(CharacterClass.Manki, 0)]
    [InlineData(CharacterClass.Wibou, 0)]
    [InlineData(CharacterClass.Bonk, 0)]
    [InlineData(CharacterClass.FightGuy, 5)]
    [InlineData(CharacterClass.Manki, 5)]
    [InlineData(CharacterClass.Wibou, 5)]
    [InlineData(CharacterClass.Bonk, 5)]
    public void DelayedNormal_RecoversAndAcceptsMovementAndAnotherAttack(CharacterClass fighter, int lossDenominator)
    {
        var lab = Match(fighter, lossDenominator);
        lab[1].Input = (_, tick) => new InputState { ActiveSlot = tick == 30 ? AbilitySlots.Slot1 : (byte)0 };
        lab.Run(30);
        Assert.Equal(ActionState.Attacking, lab[1].State(1).State);
        Assert.Equal(AbilitySlots.Slot1, lab[1].State(1).AttackSlot);
        AssertRecoveryAndNextInputs(lab);
    }

    [Theory]
    [InlineData(CharacterClass.FightGuy, AbilitySlots.A, 0)]
    [InlineData(CharacterClass.Bonk, AbilitySlots.E, 0)]
    [InlineData(CharacterClass.FightGuy, AbilitySlots.A, 5)]
    [InlineData(CharacterClass.Bonk, AbilitySlots.E, 5)]
    public void DelayedAimRelease_RecoversAndAcceptsMovementAndAnotherAttack(
        CharacterClass fighter, byte slot, int lossDenominator)
    {
        var lab = Match(fighter, lossDenominator);
        lab[1].Input = (_, tick) => TestHelpers.Input(
            activeSlot: tick == 30 ? slot : (byte)0, aiming: tick is >= 30 and < 65, aimDistance: 400);
        lab.Run(50);
        Assert.Equal(ActionState.Aiming, lab[1].State(1).State);
        Assert.Equal(ActionState.Aiming, lab.Server.GetState(1).State);
        lab.Run(30);
        AssertRecoveryAndNextInputs(lab);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void QuickDrawConvergesWithDelayAndLoss(int lossDenominator)
    {
        var lab = Match(CharacterClass.Wibou, lossDenominator);
        lab[1].Input = (_, tick) => new InputState { ActiveSlot = tick == 30 ? AbilitySlots.R : (byte)0,
            IsAiming = tick is >= 30 and < 65, AimYaw = 9000 };
        lab.Run(50);
        Assert.Equal(ActionState.Attacking, lab[1].State(1).State);
        Assert.False(lab[1].State(1).IsAiming);
        Assert.False(lab.Server.GetState(1).IsAiming);
        lab.Run(30);
        AssertRecoveryAndNextInputs(lab);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void FlyingBomberConvergesWithDelayAndLoss(int lossDenominator)
    {
        var lab = Match(CharacterClass.Manki, lossDenominator);
        lab[1].Input = (_, tick) => new InputState
        {
            ActiveSlot = tick switch { 30 => AbilitySlots.E, 40 => AbilitySlots.A, 80 => AbilitySlots.R,
                130 => AbilitySlots.F, _ => (byte)0 },
            Jump = tick == 50,
        };
        lab.Run(200);
        AssertRecoveryAndNextInputs(lab);
    }

    private static NetplayLab Match(CharacterClass fighter, int lossDenominator)
    {
        var lab = new NetplayLab("slop_pit", new[] { fighter, fighter }, seed: 41 + (int)fighter);
        foreach (var player in lab.Players)
        {
            player.Up.DelayTicks = 1;
            player.Down.DelayTicks = 3;
            player.Up.Loss = player.Down.Loss = lossDenominator == 0 ? 0 : 1.0 / lossDenominator;
        }
        return lab;
    }

    private static void AssertRecoveryAndNextInputs(NetplayLab lab)
    {
        lab.Quiesce(400);
        AssertRecovered(lab);
        float beforeX = lab[1].State(1).PX;
        lab[1].Input = (_, _) => TestHelpers.Input(moveX: 1f);
        lab.Run(20);
        Assert.True(lab[1].State(1).PX > beforeX, "Recovered fighter must accept movement.");
        lab.Quiesce(80);
        AssertRecovered(lab);

        uint pressTick = lab[1].Predictor.LocalTick + 1;
        lab[1].Input = (_, tick) => TestHelpers.Input(activeSlot: tick == pressTick ? AbilitySlots.Slot1 : (byte)0);
        lab.Step();
        Assert.Equal(ActionState.Attacking, lab[1].State(1).State);
        Assert.Equal(AbilitySlots.Slot1, lab[1].State(1).AttackSlot);
        lab.Run(5);
        Assert.Equal(ActionState.Attacking, lab.Server.GetState(1).State);
        lab.Quiesce(400);
        AssertRecovered(lab);
    }

    private static void AssertRecovered(NetplayLab lab)
    {
        Assert.Equal(ActionState.Idle, lab.Server.GetState(1).State);
        Assert.Equal(ActionState.Idle, lab[1].State(1).State);
        Assert.Equal((byte)0, lab[1].State(1).AttackSlot);
        Assert.False(lab[1].State(1).IsAiming);
        foreach (var player in lab.Players)
            foreach (var (tick, states) in lab.ServerStates.Where(x => x.Key > lab.Tick - 30))
            {
                Assert.True(player.Presented.TryGetValue(tick, out var presented), $"player {player.Id} missing tick {tick}");
                foreach (ulong id in lab.Ids)
                    Assert.Equal(states[id], presented![id]);
            }
    }
}
