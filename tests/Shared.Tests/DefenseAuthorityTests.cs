using System.Collections.Generic;
using SlopArena.Shared.Rollback;
using Xunit;

namespace SlopArena.Shared.Tests;

public class DefenseAuthorityTests
{
    private const ulong Self = 1;
    private const ulong Other = 2;

    private static ServerEntityPacket Packet(ulong id, uint tick, CharacterState state) => new()
    {
        EntityId = id, Tick = tick, State = CharacterStatePacket.FromState(state, tick),
    };

    private static RollbackSimulator Match()
    {
        var sim = new RollbackSimulator(TestHelpers.TestArena(), Self);
        sim.RegisterEntity(Self, TestHelpers.MankiDef, TestHelpers.PlayerState());
        sim.RegisterEntity(Other, TestHelpers.MankiDef, TestHelpers.PlayerState(x: 2));
        return sim;
    }

    [Theory]
    [InlineData(ActionState.Grabbing, ActionState.Grabbed)]
    [InlineData(ActionState.Grabbed, ActionState.Grabbing)]
    public void ConfirmedHoldKeepsBothRolesUnderServerAuthority(ActionState selfRole, ActionState partnerRole)
    {
        var sim = Match();
        sim.Tick(new Dictionary<ulong, InputState> { [Self] = default });
        var held = TestHelpers.PlayerState() with
        {
            State = selfRole, StateTicks = DefenseConfig.GrabHoldTicks,
            InteractionId = 19, InteractionPartnerId = Other, InteractionTick = 1,
            InteractionPhase = (byte)DefenseInteractionPhase.Captured,
        };
        var partner = held with { EntityId = Other, State = partnerRole, InteractionPartnerId = Self, PZ = 0.65f };
        sim.IngestAuthoritativeBatch(new[] { Packet(Self, 1, held), Packet(Other, 1, partner) });
        for (int tick = 0; tick < 10; tick++)
            sim.Tick(new Dictionary<ulong, InputState> { [Self] = new InputState { MoveY = 1, ActiveSlot = AbilitySlots.F, Jump = true } });
        Assert.Equal(selfRole, sim.GetState(Self).State);
        Assert.Equal(held.PZ, sim.GetState(Self).PZ);
        Assert.Equal(partnerRole, sim.GetState(Other).State);
        Assert.Equal(held.InteractionId, sim.GetState(Other).InteractionId);

        held.StateTicks = 170;
        held.PZ = 0.2f;
        partner.StateTicks = 170;
        partner.PZ = 0.85f;
        sim.IngestAuthoritativeBatch(new[] { Packet(Self, 10, held), Packet(Other, 10, partner) });
        Assert.Equal(held.PZ, sim.GetState(Self).PZ);
        Assert.Equal(partner.PZ, sim.GetState(Other).PZ);
    }

    [Fact]
    public void ConfirmedCaptureInterruptsComplexLocalAttackAndWaitsForTerminal()
    {
        var sim = Match();
        sim.Tick(new Dictionary<ulong, InputState> { [Self] = new InputState { ActiveSlot = AbilitySlots.Slot1 } });
        var captured = TestHelpers.PlayerState();
        captured.State = ActionState.Grabbed;
        captured.InteractionId = 17;
        captured.InteractionPartnerId = Other;
        captured.InteractionTick = 1;
        sim.IngestAuthoritativeBatch(new[] { Packet(Self, 1, captured) });
        Assert.Equal(ActionState.Grabbed, sim.GetState(Self).State);

        for (int i = 0; i < 3; i++)
            sim.Tick(new Dictionary<ulong, InputState> { [Self] = new InputState { Jump = true, ActiveSlot = AbilitySlots.Slot1 } });
        Assert.Equal(ActionState.Grabbed, sim.GetState(Self).State);
        var partner = TestHelpers.PlayerState(x: 2);
        partner.State = ActionState.Throwing;
        partner.InteractionId = 17;
        partner.InteractionPartnerId = Self;
        partner.InteractionTick = 1;
        sim.IngestAuthoritativeBatch(new[] { Packet(Other, 2, partner) });
        Assert.Equal((ulong)17, sim.GetState(Other).InteractionId);

        var released = TestHelpers.PlayerState();
        released.LastTerminalInteractionId = 17;
        released.InteractionTerminalTick = 3;
        sim.IngestAuthoritativeBatch(new[] { Packet(Self, 3, released) });
        Assert.Equal(ActionState.Idle, sim.GetState(Self).State);
        Assert.Equal((ulong)0, sim.GetState(Self).InteractionId);
        sim.IngestAuthoritativeBatch(new[] { Packet(Self, 1, captured), Packet(Self, 3, released) });
        Assert.Equal(ActionState.Idle, sim.GetState(Self).State);
        sim.Tick(new Dictionary<ulong, InputState> { [Self] = TestHelpers.Input(moveX: 1) });
        Assert.True(sim.GetState(Self).PX > released.PX);
    }

    [Fact]
    public void TerminalWithoutCaptureAndLateCompanionCannotResurrectInteraction()
    {
        var sim = Match();
        var terminal = TestHelpers.PlayerState();
        terminal.LastTerminalInteractionId = 51;
        terminal.InteractionTerminalTick = 8;
        sim.IngestAuthoritativeBatch(new[] { Packet(Self, 8, terminal) });
        var staleSelf = TestHelpers.PlayerState();
        staleSelf.State = ActionState.Grabbed;
        staleSelf.InteractionId = 51;
        staleSelf.InteractionPartnerId = Other;
        staleSelf.InteractionTick = 6;
        var staleOther = staleSelf;
        staleOther.InteractionPartnerId = Self;
        sim.IngestAuthoritativeBatch(new[] { Packet(Self, 7, staleSelf), Packet(Other, 7, staleOther) });
        sim.IngestAuthoritativeBatch(new[] { Packet(Other, 9, staleOther) });
        sim.IngestAuthoritativeBatch(new[] { Packet(Self, 10, staleSelf) });
        Assert.Equal(ActionState.Idle, sim.GetState(Self).State);
        Assert.Equal((ulong)0, sim.GetState(Self).InteractionId);
        Assert.Equal((ulong)0, sim.GetState(Other).InteractionId);

        sim.IngestAuthoritativeBatch(new[] { Packet(Self, 8, terminal) });
        Assert.Equal(1, sim.CorrectionCount);
    }

    [Fact]
    public void TerminalWithoutCaptureCorrectsComplexHistoryOnce()
    {
        var sim = Match();
        sim.Tick(new Dictionary<ulong, InputState> { [Self] = new InputState { ActiveSlot = AbilitySlots.Slot1 } });
        var terminal = TestHelpers.PlayerState();
        terminal.LastTerminalInteractionId = 52;
        terminal.InteractionTerminalTick = 1;
        var packet = Packet(Self, 1, terminal);

        sim.IngestAuthoritativeBatch(new[] { packet });

        Assert.Equal(ActionState.Idle, sim.GetState(Self).State);
        Assert.Equal((ulong)0, sim.GetState(Self).InteractionId);
        Assert.Equal(1, sim.CorrectionCount);
        sim.IngestAuthoritativeBatch(new[] { packet });
        Assert.Equal(1, sim.CorrectionCount);
    }

    [Fact]
    public void ReorderedTerminalEndsCaptureAndRepeatedUpdatesDoNotReapplyIt()
    {
        var sim = Match();
        Assert.True(sim.SetTimeline(9));
        var captured = TestHelpers.PlayerState();
        captured.State = ActionState.Grabbed;
        captured.InteractionId = 53;
        captured.InteractionPartnerId = Other;
        captured.InteractionTick = 2;
        sim.IngestAuthoritativeBatch(new[] { Packet(Self, 10, captured) });
        sim.Tick(new Dictionary<ulong, InputState> { [Self] = new InputState { Jump = true } });
        Assert.Equal(ActionState.Grabbed, sim.GetState(Self).State);

        var capturedPartner = TestHelpers.PlayerState(x: 2);
        capturedPartner.State = ActionState.Throwing;
        capturedPartner.InteractionId = 53;
        capturedPartner.InteractionPartnerId = Self;
        capturedPartner.InteractionTick = 2;
        sim.IngestAuthoritativeBatch(new[] { Packet(Other, 10, capturedPartner) });
        Assert.Equal((ulong)53, sim.GetState(Other).InteractionId);

        var released = TestHelpers.PlayerState();
        released.LastTerminalInteractionId = 53;
        released.InteractionTerminalTick = 8;
        var terminalPacket = Packet(Self, 8, released);
        var releasedPartner = TestHelpers.PlayerState(x: 2);
        releasedPartner.LastTerminalInteractionId = 53;
        releasedPartner.InteractionTerminalTick = 8;
        sim.IngestAuthoritativeBatch(new[] { terminalPacket, Packet(Other, 8, releasedPartner) });
        Assert.Equal(ActionState.Idle, sim.GetState(Self).State);
        Assert.Equal((ulong)0, sim.GetState(Self).InteractionId);
        Assert.Equal((ulong)0, sim.GetState(Other).InteractionId);
        Assert.Equal(2, sim.CorrectionCount);

        sim.IngestAuthoritativeBatch(new[] { terminalPacket });
        sim.IngestAuthoritativeBatch(new[]
        {
            Packet(Self, 11, captured),
            Packet(Other, 11, capturedPartner),
        });
        Assert.Equal(ActionState.Idle, sim.GetState(Self).State);
        Assert.Equal((ulong)0, sim.GetState(Self).InteractionId);
        Assert.Equal((ulong)0, sim.GetState(Other).InteractionId);
        Assert.Equal(2, sim.CorrectionCount);
        sim.Tick(new Dictionary<ulong, InputState> { [Self] = TestHelpers.Input(moveX: 1) });
        Assert.True(sim.GetState(Self).PX > released.PX);
    }

    [Fact]
    public void OpponentCaptureArrivingFirstUsesNewestSnapshotOnlyAfterMatchingSelfIdentityAndTick()
    {
        var sim = Match();
        Assert.True(sim.SetTimeline(3));
        sim.Tick(new Dictionary<ulong, InputState>());
        var older = TestHelpers.PlayerState(x: 3);
        older.State = ActionState.Throwing;
        older.InteractionId = 72;
        older.InteractionPartnerId = Self;
        older.InteractionTick = 1;
        var newest = TestHelpers.PlayerState(x: 4);
        newest.State = ActionState.Throwing;
        newest.InteractionId = 72;
        newest.InteractionPartnerId = Self;
        newest.InteractionTick = 1;
        sim.IngestAuthoritativeBatch(new[] { Packet(Other, 4, newest), Packet(Other, 3, older) });
        Assert.Equal((ulong)0, sim.GetState(Other).InteractionId);
        Assert.Equal((ulong)0, sim.GetState(Self).InteractionId);

        var captured = TestHelpers.PlayerState();
        captured.State = ActionState.Grabbed;
        captured.InteractionId = 72;
        captured.InteractionPartnerId = Other;
        captured.InteractionTick = 1;
        sim.IngestAuthoritativeBatch(new[] { Packet(Self, 4, captured) });
        Assert.Equal((ulong)72, sim.GetState(Self).InteractionId);
        Assert.Equal((ulong)72, sim.GetState(Other).InteractionId);
        Assert.Equal(newest.PX, sim.GetState(Other).PX);
    }

    [Fact]
    public void LateBlockStunOverridesComplexAttackWithoutReplayingAttack()
    {
        var sim = Match();
        sim.Tick(new Dictionary<ulong, InputState> { [Self] = new InputState { ActiveSlot = AbilitySlots.Slot1 } });
        var blocked = TestHelpers.PlayerState();
        blocked.State = ActionState.Shielding;
        blocked.BlockStunTicks = 6;
        blocked.BlockHitstopKind = (byte)DefenseBlockHitstopKind.ShieldContact;
        sim.IngestAuthoritativeBatch(new[] { Packet(Self, 1, blocked) });
        Assert.Equal((ushort)6, sim.GetState(Self).BlockStunTicks);
        Assert.Equal(ActionState.Shielding, sim.GetState(Self).State);
        sim.IngestAuthoritativeBatch(new[] { Packet(Self, 1, blocked) });
        Assert.Equal(1, sim.CorrectionCount);
    }
}
