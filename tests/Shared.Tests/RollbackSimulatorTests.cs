using System.Collections.Generic;
using Xunit;

namespace SlopArena.Shared.Tests;

public class RollbackSimulatorTests
{
    private const ulong SelfId = 1;
    private const ulong OpponentId = 2;

    private static ServerEntityPacket MakePacket(ulong entityId, uint tick, CharacterState state, bool hasInput = false, InputState input = default)
        => new ServerEntityPacket
        {
            EntityId = entityId,
            Tick = tick,
            State = CharacterStatePacket.FromState(state, tick),
            HasInput = hasInput,
            Input = input,
        };

    [Fact]
    public void TimelineAlignmentAndFutureSelfSnapshotsUseAbsoluteTicks()
    {
        var sim = new SlopArena.Shared.Rollback.RollbackSimulator(TestHelpers.TestArena(), SelfId);
        sim.RegisterEntity(SelfId, TestHelpers.MankiDef, TestHelpers.PlayerState());
        sim.RegisterEntity(OpponentId, TestHelpers.MankiDef, TestHelpers.PlayerState(x: 10f));
        Assert.True(sim.SetTimeline(300));

        var newest = TestHelpers.PlayerState(x: 77f);
        var older = TestHelpers.PlayerState(x: 33f);
        sim.IngestAuthoritativeBatch(new[] { MakePacket(SelfId, 305, newest) });
        sim.IngestAuthoritativeBatch(new[] { MakePacket(SelfId, 304, older) });
        for (int i = 0; i < 5; i++)
            sim.Tick(new Dictionary<ulong, InputState>());

        Assert.Equal(77f, sim.GetState(SelfId).PX);
        Assert.False(sim.SetTimeline(306));
    }

    [Fact]
    public void SelfEntity_UsesLocalTrack_OpponentIdle_UsesPredictedTrack()
    {
        var arena = TestHelpers.TestArena();
        var def = TestHelpers.MankiDef;
        var sim = new SlopArena.Shared.Rollback.RollbackSimulator(arena, SelfId);
        sim.RegisterEntity(SelfId, def, TestHelpers.PlayerState());
        sim.RegisterEntity(OpponentId, def, TestHelpers.PlayerState(x: 10f));

        sim.Tick(new Dictionary<ulong, InputState> { { SelfId, TestHelpers.Input(moveX: 1f) } });
        sim.IngestOpponentBatch(new[] { MakePacket(OpponentId, 1, TestHelpers.PlayerState(x: 10f)) });

        // Self moved (LocalTrack advanced it); opponent reflects the ingested packet.
        Assert.NotEqual(0f, sim.GetState(SelfId).PX);
        Assert.Equal(10f, sim.GetState(OpponentId).PX);
    }

    [Fact]
    public void OpponentEnteringComplexState_SwitchesToRawTrack_NoLongerRebuilt()
    {
        var arena = TestHelpers.TestArena();
        var def = TestHelpers.MankiDef;
        var sim = new SlopArena.Shared.Rollback.RollbackSimulator(arena, SelfId);
        sim.RegisterEntity(SelfId, def, TestHelpers.PlayerState());
        sim.RegisterEntity(OpponentId, def, TestHelpers.PlayerState(x: 10f));

        // Tick 1: opponent Idle — PredictedTrack picks it up.
        sim.IngestOpponentBatch(new[] { MakePacket(OpponentId, 1, TestHelpers.PlayerState(x: 10f)) });
        Assert.Equal(10f, sim.GetState(OpponentId).PX);

        // Tick 2: server reports the opponent now Attacking, at a new position — Complex state,
        // must land on RawTrack: rendered exactly as reported, no re-simulation.
        var attackingState = TestHelpers.PlayerState(x: 11f);
        attackingState.State = ActionState.Attacking;
        sim.IngestOpponentBatch(new[] { MakePacket(OpponentId, 2, attackingState) });

        Assert.Equal(11f, sim.GetState(OpponentId).PX);
        Assert.Equal(ActionState.Attacking, sim.GetState(OpponentId).State);
    }

    [Fact]
    public void OpponentHitstopSnapshot_SwitchesToRawTrack()
    {
        var arena = TestHelpers.TestArena();
        var def = TestHelpers.MankiDef;
        var sim = new SlopArena.Shared.Rollback.RollbackSimulator(arena, SelfId);
        sim.RegisterEntity(SelfId, def, TestHelpers.PlayerState());
        sim.RegisterEntity(OpponentId, def, TestHelpers.PlayerState(x: 10f));

        sim.IngestOpponentBatch(new[] { MakePacket(OpponentId, 1, TestHelpers.PlayerState(x: 10f)) });
        var frozen = TestHelpers.PlayerState(x: 77f);
        frozen.HitstopTicks = 3;
        sim.IngestOpponentBatch(new[] { MakePacket(OpponentId, 2, frozen) });

        Assert.Equal(77f, sim.GetState(OpponentId).PX);
        Assert.Equal((ushort)3, sim.GetState(OpponentId).HitstopTicks);
    }

    [Fact]
    public void AirDodgeSnapshot_ReplaysInvulnerabilityMovementAndRecoveryBoundaries()
    {
        var arena = TestHelpers.TestArena();
        var def = TestHelpers.MankiDef;
        var airborne = TestHelpers.PlayerState(x: 10f);
        airborne.PY = TestHelpers.GroundPY(def) + 10f;
        airborne.VX = airborne.VY = airborne.VZ = 0f;
        airborne.IsGrounded = false;
        airborne.JumpsLeft = 0;
        airborne.AirDodgesLeft = 1;
        airborne.FacingYaw = 0f;

        var authority = TestHelpers.MakeSim(arena);
        authority.RegisterEntity(OpponentId, def, airborne);
        var dodgeInput = new InputState { ShieldHeld = true, ShieldPressed = true };
        authority.Tick(new Dictionary<ulong, InputState> { [OpponentId] = dodgeInput });
        var accepted = authority.GetState(OpponentId);
        Assert.Equal(ActionState.AirDodgeMovement, accepted.State);
        Assert.Equal(DefenseConfig.AirDodgeInvulnerabilityTicks, accepted.InvincibilityTicks);
        Assert.Equal((byte)0, accepted.AirDodgesLeft);
        Assert.Equal(0f, accepted.DashDirX);
        Assert.Equal(1f, accepted.DashDirZ);

        var authoritativePacket = new ServerEntityPacket
        {
            EntityId = OpponentId,
            Tick = 1,
            State = CharacterStatePacket.FromState(accepted, tick: 1),
            HasInput = true,
            Input = dodgeInput,
        };
        var bytes = new byte[ServerEntityPacket.MaxSize];
        authoritativePacket.Serialize(bytes);
        var decodedPacket = ServerEntityPacket.Deserialize(bytes);

        var reference = TestHelpers.MakeSim(arena);
        reference.RegisterEntity(OpponentId, def, accepted);
        var referenceInput = new Dictionary<ulong, InputState> { [OpponentId] = default };

        CharacterState ReconstructAt(uint replayTicks)
        {
            var rollback = new SlopArena.Shared.Rollback.RollbackSimulator(arena, SelfId);
            var self = TestHelpers.PlayerState(x: -10f);
            self.PY = TestHelpers.GroundPY(def);
            self.IsGrounded = true;
            rollback.RegisterEntity(SelfId, def, self);
            rollback.RegisterEntity(OpponentId, def, accepted);

            var localInput = new Dictionary<ulong, InputState> { [SelfId] = default };
            for (uint tick = 0; tick < replayTicks + 1; tick++)
                rollback.Tick(localInput);
            rollback.IngestOpponentBatch(new[] { decodedPacket });
            return rollback.GetState(OpponentId);
        }

        // Boundaries: last invulnerable tick, first vulnerable tick, last burst tick,
        // first glide tick, last recovery tick and the first actionable tick.
        uint invulnerable = DefenseConfig.AirDodgeInvulnerabilityTicks;
        uint movement = DefenseConfig.AirDodgeMovementTicks;
        uint actionable = movement + DefenseConfig.AirDodgeRecoveryTicks;
        var boundaries = new[] { invulnerable - 1, invulnerable, movement - 1, movement, actionable - 1, actionable };
        for (uint replayTicks = 1; replayTicks <= actionable; replayTicks++)
        {
            reference.Tick(referenceInput);
            if (Array.IndexOf(boundaries, replayTicks) < 0)
                continue;

            var expected = reference.GetState(OpponentId);
            var actual = ReconstructAt(replayTicks);
            Assert.Equal(expected.State, actual.State);
            Assert.Equal(expected.StateTicks, actual.StateTicks);
            Assert.Equal(expected.IsGrounded, actual.IsGrounded);
            Assert.Equal(expected.PX, actual.PX);
            Assert.Equal(expected.PY, actual.PY);
            Assert.Equal(expected.PZ, actual.PZ);
            Assert.Equal(expected.VX, actual.VX);
            Assert.Equal(expected.VY, actual.VY);
            Assert.Equal(expected.VZ, actual.VZ);
            Assert.Equal(expected.DashDirX, actual.DashDirX);
            Assert.Equal(expected.DashDirZ, actual.DashDirZ);
            Assert.Equal(expected.AirTimeTicks, actual.AirTimeTicks);
            Assert.Equal(expected.InvincibilityTicks, actual.InvincibilityTicks);
            Assert.Equal(expected.AirDodgesLeft, actual.AirDodgesLeft);
            Assert.Equal(expected.AirDodgeRecoveryTicks, actual.AirDodgeRecoveryTicks);

            if (replayTicks == invulnerable - 1)
            {
                Assert.Equal(ActionState.AirDodgeMovement, actual.State);
                Assert.Equal((ushort)1, actual.InvincibilityTicks);
                Assert.True(actual.PZ > accepted.PZ);
            }
            else if (replayTicks == invulnerable)
            {
                Assert.Equal(ActionState.AirDodgeMovement, actual.State);
                Assert.Equal((ushort)0, actual.InvincibilityTicks);
            }
            else if (replayTicks == movement - 1)
                Assert.Equal(ActionState.AirDodgeMovement, actual.State);
            else if (replayTicks == movement)
            {
                Assert.Equal(ActionState.AirDodgeRecovery, actual.State);
                Assert.Equal(DefenseConfig.AirDodgeRecoveryTicks, actual.AirDodgeRecoveryTicks);
                Assert.True(actual.VZ > 0f);
            }
            else if (replayTicks == actionable - 1)
            {
                Assert.Equal(ActionState.AirDodgeRecovery, actual.State);
                Assert.Equal((ushort)1, actual.AirDodgeRecoveryTicks);
            }
            else
            {
                Assert.Equal(ActionState.Idle, actual.State);
                Assert.Equal((ushort)0, actual.AirDodgeRecoveryTicks);
            }
        }
    }

    [Fact]
    public void ReconcileSelf_RoutesToLocalTrack_IncrementsCorrectionCount()
    {
        var arena = TestHelpers.TestArena();
        var def = TestHelpers.MankiDef;
        var sim = new SlopArena.Shared.Rollback.RollbackSimulator(arena, SelfId);
        sim.RegisterEntity(SelfId, def, TestHelpers.PlayerState());
        for (int i = 0; i < 3; i++)
            sim.Tick(new Dictionary<ulong, InputState> { { SelfId, default } });

        var wrongState = TestHelpers.PlayerState(x: 999f);
        sim.ReconcileSelf(MakePacket(SelfId, 1, wrongState));

        Assert.Equal(1, sim.CorrectionCount);
        Assert.Equal(999f, sim.GetState(SelfId).PX);
    }

    [Fact]
    public void GetAllStates_IncludesSelfAndEveryRegisteredOpponent()
    {
        var arena = TestHelpers.TestArena();
        var def = TestHelpers.MankiDef;
        var sim = new SlopArena.Shared.Rollback.RollbackSimulator(arena, SelfId);
        sim.RegisterEntity(SelfId, def, TestHelpers.PlayerState());
        sim.RegisterEntity(OpponentId, def, TestHelpers.PlayerState(x: 10f));

        var all = sim.GetAllStates();

        Assert.True(all.ContainsKey(SelfId));
        Assert.True(all.ContainsKey(OpponentId));
    }

    [Fact]
    public void IngestOpponentBatch_UnregisteredOpponent_FallsBackToRawTrack_NoThrow()
    {
        // Regression (PvP crash): PvPMatch originally never registered entities, so the
        // bridge's _defs was empty. The first Predictable opponent packet threw
        // KeyNotFoundException on defs[EntityId] — but only AFTER _registered.Add had
        // already marked the entity, so the next batch took the SetState branch and
        // created an unpaired _states key (state without def), crashing
        // SimulateMovement every tick. Unknown entities must fall back to RawTrack.
        var arena = TestHelpers.TestArena();
        var def = TestHelpers.MankiDef;
        var sim = new SlopArena.Shared.Rollback.RollbackSimulator(arena, SelfId);
        sim.RegisterEntity(SelfId, def, TestHelpers.PlayerState());

        // Opponent 2 is deliberately NOT registered — the PvP bug condition.
        var idle = TestHelpers.PlayerState(x: 10f);
        for (int i = 0; i < 3; i++)
            sim.IngestOpponentBatch(new[] { MakePacket(OpponentId, (uint)(i + 1), idle) });

        // Raw fallback: rendered exactly as reported, no re-simulation, no throw.
        Assert.Equal(10f, sim.GetState(OpponentId).PX);
        Assert.Equal(ActionState.Idle, sim.GetState(OpponentId).State);
    }
}
