using System.Collections.Generic;
using SlopArena.Shared.Rollback;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>Exercise admitted definitions and poses through delayed, serialized netplay.</summary>
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
    public void DelayedNormal_RecoversAndAcceptsMovementAndAnotherAttack(CharacterClass fighter, int dropEvery)
    {
        var match = new DelayedRosterMatch(BuiltInContentResolver.Resolve(fighter), dropEvery);
        for (int tick = 0; tick < 10; tick++) match.Step(default);

        match.Step(TestHelpers.Input(activeSlot: AbilitySlots.Slot1));
        Assert.Equal(ActionState.Attacking, match.ClientState.State);
        Assert.Equal(AbilitySlots.Slot1, match.ClientState.AttackSlot);
        AssertRecoveryAndNextInputs(match);
    }

    [Theory]
    [InlineData(CharacterClass.FightGuy, AbilitySlots.A, 0)]
    [InlineData(CharacterClass.Manki, AbilitySlots.A, 0)]
    [InlineData(CharacterClass.Wibou, AbilitySlots.R, 0)]
    [InlineData(CharacterClass.Bonk, AbilitySlots.E, 0)]
    [InlineData(CharacterClass.FightGuy, AbilitySlots.A, 5)]
    [InlineData(CharacterClass.Manki, AbilitySlots.A, 5)]
    [InlineData(CharacterClass.Wibou, AbilitySlots.R, 5)]
    [InlineData(CharacterClass.Bonk, AbilitySlots.E, 5)]
    public void DelayedAimRelease_RecoversAndAcceptsMovementAndAnotherAttack(
        CharacterClass fighter, byte slot, int dropEvery)
    {
        var match = new DelayedRosterMatch(BuiltInContentResolver.Resolve(fighter), dropEvery);
        for (int tick = 0; tick < 10; tick++) match.Step(default);

        match.Step(TestHelpers.Input(activeSlot: slot, aiming: true, aimDistance: 400));
        for (int tick = 0; tick < 30; tick++)
            match.Step(TestHelpers.Input(aiming: true, aimDistance: 400));
        Assert.Equal(ActionState.Aiming, match.ClientState.State);
        Assert.Equal(ActionState.Aiming, match.ServerState.State);

        match.Step(TestHelpers.Input(aimDistance: 400));
        AssertRecoveryAndNextInputs(match);
    }

    private static void AssertRecoveryAndNextInputs(DelayedRosterMatch match)
    {
        Assert.True(match.Settle(), $"Recovery diverged: server={match.ServerState.State}, client={match.ClientState.State}");
        Assert.Equal(ActionState.Idle, match.ClientState.State);
        Assert.Equal((byte)0, match.ClientState.AttackSlot);
        Assert.False(match.ClientState.IsAiming);

        float beforeX = match.ClientState.PX;
        for (int tick = 0; tick < 20; tick++) match.Step(TestHelpers.Input(moveX: 1f));
        Assert.True(match.ClientState.PX > beforeX, "Recovered self must accept movement.");
        Assert.True(match.Settle(), "Movement must converge after the delayed input drains.");

        match.Step(TestHelpers.Input(activeSlot: AbilitySlots.Slot1));
        Assert.Equal(ActionState.Attacking, match.ClientState.State);
        Assert.Equal(AbilitySlots.Slot1, match.ClientState.AttackSlot);
        match.Step(default); // Deliver the second attack's uplink to the server.
        Assert.Equal(ActionState.Attacking, match.ServerState.State);
        Assert.True(match.Settle(), "A second attack must recover and converge too.");
    }

    private sealed class DelayedRosterMatch
    {
        private const ulong SelfId = 1;
        private const ulong OpponentId = 2;
        private const int UplinkTicks = 1;
        private const int DownlinkTicks = 3;
        private readonly ServerSimulation _server;
        private readonly RollbackSimulator _client;
        private readonly Queue<InputState> _uplink = new();
        private readonly Queue<ServerEntityPacket[]> _downlink = new();
        private readonly int _dropEvery;
        private uint _tick;

        public CharacterState ServerState => _server.GetState(SelfId);
        public CharacterState ClientState => _client.GetState(SelfId);

        public DelayedRosterMatch(MatchContentEntry content, int dropEvery)
        {
            _dropEvery = dropEvery;
            var arena = TestHelpers.TestArena();
            _server = new ServerSimulation(arena);
            _client = new RollbackSimulator(arena, SelfId);
            foreach (ulong id in new[] { SelfId, OpponentId })
            {
                var state = TestHelpers.PlayerState(x: 50f, z: id == SelfId ? 50f : 80f) with
                {
                    EntityId = id, PY = TestHelpers.GroundPY(content.Definition),
                    JumpsLeft = content.Definition.Movement.MaxJumps,
                };
                _server.RegisterEntity(id, content.Definition, state, content.Baked);
                _client.RegisterEntity(id, content.Definition, state, content.Baked);
            }
        }

        public void Step(InputState input, bool dropsEnabled = true)
        {
            _tick++;
            var inputBytes = new byte[InputState.Size];
            input.Write(inputBytes);
            input = InputState.Deserialize(inputBytes);
            _uplink.Enqueue(input);
            var authorityInput = _uplink.Count > UplinkTicks ? _uplink.Dequeue() : default;
            // Tick consumes ActiveSlot; relay the consumed input without changing
            // the true player input used by local prediction.
            var consumedInputs = new Dictionary<ulong, InputState>
            {
                [SelfId] = authorityInput, [OpponentId] = default,
            };
            _server.Tick(consumedInputs);
            _client.Tick(new Dictionary<ulong, InputState> { [SelfId] = input });

            var batch = new ServerEntityPacket[2];
            for (int index = 0; index < batch.Length; index++)
            {
                ulong id = (ulong)index + 1;
                var packet = new ServerEntityPacket
                {
                    EntityId = id, Tick = _tick,
                    State = CharacterStatePacket.FromState(_server.GetState(id), _tick),
                    HasInput = true, Input = consumedInputs[id],
                };
                var bytes = new byte[packet.WireSize];
                packet.Serialize(bytes);
                batch[index] = ServerEntityPacket.Deserialize(bytes);
            }
            _downlink.Enqueue(batch);
            if (_downlink.Count > DownlinkTicks)
            {
                var delivered = _downlink.Dequeue();
                if (!dropsEnabled || _dropEvery == 0 || delivered[0].Tick % _dropEvery != 0)
                    _client.IngestAuthoritativeBatch(delivered);
            }
        }

        public bool Settle()
        {
            // Drain in-flight loss before allowing a later authority state to
            // converge. Do not pin the roster's authored recovery durations.
            int stableIdleTicks = 0;
            for (int tick = 0; tick < 900; tick++)
            {
                Step(default, dropsEnabled: false);
                if (ServerState.State == ActionState.Idle && ClientState.State == ActionState.Idle &&
                    CharacterStatePacket.FromState(ServerState).Equals(CharacterStatePacket.FromState(ClientState)))
                {
                    if (++stableIdleTicks > UplinkTicks + DownlinkTicks + 30) return true;
                }
                else stableIdleTicks = 0;
            }
            return false;
        }
    }
}
