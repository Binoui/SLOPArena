using System.Collections.Generic;
using Xunit;

namespace SlopArena.Shared.Tests;

public class LocalTrackTests
{
    private const ulong SelfId = 1;
    private const ulong OpponentId = 2;
    private static readonly CharacterDefinition Def = TestHelpers.EngineDef;

    private static CharacterState GroundState(float x = 0f, float z = 0f)
        => TestHelpers.PlayerState(x, z) with { PY = TestHelpers.GroundPY(Def) };

    [Fact]
    public void Tick_AdvancesLikeServerSimulation_ForIdleMovement()
    {
        // A LocalTrack ticked with a rightward-move input should move exactly like a
        // plain ServerSimulation given the same input — no divergence for a fresh sim.
        var arena = TestHelpers.TestArena();
        var def = Def;
        var initial = GroundState();
        var track = new SlopArena.Shared.Rollback.LocalTrack(arena, SelfId);
        track.RegisterEntity(def, initial);

        var reference = TestHelpers.MakeSim(arena);
        TestHelpers.RegisterPlayer(reference, def, initial);

        var input = TestHelpers.Input(moveX: 1f);
        CharacterState localResult = default;
        for (int i = 0; i < 10; i++)
            localResult = track.Tick(input);
        CharacterState referenceResult = default;
        for (int i = 0; i < 10; i++)
        {
            reference.Tick(new System.Collections.Generic.Dictionary<ulong, InputState> { { SelfId, input } });
            referenceResult = reference.GetState(SelfId);
        }

        Assert.Equal(referenceResult.PX, localResult.PX);
        Assert.Equal(referenceResult.PZ, localResult.PZ);
        Assert.Equal(referenceResult.State, localResult.State);
    }

    [Fact]
    public void AirDodgeSelfCorrectionReplaysCapturedDirectionAndRecovery()
    {
        var arena = TestHelpers.TestArena();
        var def = Def;
        var initial = TestHelpers.PlayerState() with
        {
            PX = 10f, PY = 20f, PZ = 10f, IsGrounded = false,
            FacingYaw = 0f, AirDodgesLeft = 1,
        };
        var track = new SlopArena.Shared.Rollback.LocalTrack(arena, SelfId);
        track.RegisterEntity(def, initial);
        var authority = TestHelpers.MakeSim(arena);
        authority.RegisterEntity(SelfId, def, initial);

        CharacterState corrected = default;
        for (int tick = 1; tick <= 20; tick++)
        {
            var input = new InputState
            {
                ShieldPressed = tick == 1, MoveY = tick % 2 == 0 ? -1f : 1f,
                FaceToCamera = tick > 1, AimYaw = 18000,
            };
            track.Tick(input);
            authority.Tick(new Dictionary<ulong, InputState> { [SelfId] = input });
            if (tick == 3)
            {
                corrected = authority.GetState(SelfId);
                corrected.PZ += 0.5f;
                authority.GetAllStates()[SelfId] = corrected;
            }
        }
        track.ReconcileWithServer(new ServerEntityPacket
        {
            EntityId = SelfId, Tick = 3, State = CharacterStatePacket.FromState(corrected),
        });

        var expected = authority.GetState(SelfId);
        var actual = track.GetState();
        Assert.Equal(ActionState.AirDodgeRecovery, actual.State);
        Assert.Equal(expected.StateTicks, actual.StateTicks);
        Assert.Equal(expected.AirDodgeRecoveryTicks, actual.AirDodgeRecoveryTicks);
        Assert.Equal(expected.AirDodgesLeft, actual.AirDodgesLeft);
        Assert.Equal(expected.InvincibilityTicks, actual.InvincibilityTicks);
        TestHelpers.AssertNear(expected.PZ, actual.PZ);
        TestHelpers.AssertNear(expected.VZ, actual.VZ);
        TestHelpers.AssertNear(expected.DashDirZ, actual.DashDirZ);
    }


    [Theory]
    [InlineData("slide-stop", 28)]
    [InlineData("fast-fall", 5)]
    [InlineData("slide-jump", 2)]
    [InlineData("wall-contact", 8)]
    public void ReconcileWithServer_ReplaysDownFlowFromCorrectedHistory(string scenario, int ticks)
    {
        var arena = TestHelpers.TestArena();
        var def = Def;
        var initial = GroundState() with
        {
            PY = TestHelpers.GroundPY(def),
            VX = def.Movement.RunSpeed,
            State = ActionState.Run,
        };
        if (scenario == "slide-stop")
            initial.VX *= .8f; // Finish braking within the 30-tick replay window.
        if (scenario == "fast-fall")
        {
            initial.IsGrounded = false;
            initial.PY += 4f;
            initial.VY = -2f;
            initial.State = ActionState.Idle;
        }
        if (scenario == "slide-jump")
        {
            initial.State = ActionState.Sliding;
            initial.VX *= 1.2f;
        }
        if (scenario == "wall-contact")
        {
            initial.State = ActionState.Sliding;
            initial.VZ = def.Movement.RunSpeed * .5f;
            arena.CollisionTriangles = new[]
            {
                new CollisionTriangle { AX = -20, AZ = -20, BX = -20, BZ = 20, CX = 20, CZ = -20 },
                new CollisionTriangle { AX = 20, AZ = 20, BX = 20, BZ = -20, CX = -20, CZ = 20 },
                new CollisionTriangle { AX = 1.5f, AZ = -10, BX = 1.5f, BY = 8, BZ = -10, CX = 1.5f, CZ = 10 },
            };
        }
        var track = new SlopArena.Shared.Rollback.LocalTrack(arena, SelfId);
        track.RegisterEntity(def, initial);
        var corrected = initial;
        corrected.PX += .5f;
        var reference = TestHelpers.MakeSim(arena);
        TestHelpers.RegisterPlayer(reference, def, corrected);
        for (int tick = 0; tick < ticks; tick++)
        {
            var input = scenario == "slide-jump"
                ? new InputState { Jump = tick == 0, JumpHeld = true, Down = true }
                : new InputState { Down = scenario != "fast-fall" || tick == 0, DownPressed = tick == 0 };
            track.Tick(input);
            reference.Tick(new Dictionary<ulong, InputState> { [SelfId] = input });
        }
        track.ReconcileWithServer(new ServerEntityPacket
        {
            EntityId = SelfId, Tick = 0, State = CharacterStatePacket.FromState(corrected),
        });
        Assert.Equal(1, track.CorrectionCount);
        var expected = reference.GetState(SelfId);
        var actual = track.GetState();
        Assert.Equal(expected.State, actual.State);
        Assert.Equal(expected.IsGrounded, actual.IsGrounded);
        Assert.Equal(expected.IsFastFalling, actual.IsFastFalling);
        Assert.Equal(expected.JumpFromSlide, actual.JumpFromSlide);
        Assert.Equal(expected.CrouchSettled, actual.CrouchSettled);
        TestHelpers.AssertNear(expected.PX, actual.PX);
        TestHelpers.AssertNear(expected.PY, actual.PY);
        TestHelpers.AssertNear(expected.PZ, actual.PZ);
        TestHelpers.AssertNear(expected.VX, actual.VX);
        TestHelpers.AssertNear(expected.VY, actual.VY);
        TestHelpers.AssertNear(expected.VZ, actual.VZ);
        if (scenario == "fast-fall") Assert.True(actual.IsFastFalling);
        if (scenario == "slide-jump") Assert.True(actual.JumpFromSlide);
        if (scenario == "wall-contact")
        {
            TestHelpers.AssertNear(0, actual.VX);
            Assert.True(actual.VZ > 0);
            Assert.True(actual.PX <= 1.5f - def.CapsuleRadius + .01f);
        }
    }
    [Fact]
    public void ReconcileWithServer_SnapsPositionWhenServerDisagrees_DuringPredictableWindow()
    {
        var arena = TestHelpers.TestArena();
        var def = Def;
        var track = new SlopArena.Shared.Rollback.LocalTrack(arena, SelfId);
        track.RegisterEntity(def, GroundState());

        // Advance 5 ticks of pure idle (Predictable) — matches the ring's recorded ticks 1..5.
        CharacterState state = default;
        for (int i = 0; i < 5; i++)
            state = track.Tick(default);
        Assert.Equal(0, track.CorrectionCount);

        // Server disagrees on tick 3's position (simulated packet loss / float drift).
        var serverPacket = new CharacterStatePacket
        {
            PositionX = state.PX + 5f, // deliberately wrong vs. what we actually had at tick 3
            CurrentActionState = (byte)ActionState.Idle,
        };
        track.ReconcileWithServer(new ServerEntityPacket { EntityId = SelfId, Tick = 3, State = serverPacket });

        Assert.Equal(1, track.CorrectionCount);
        // After replaying ticks 4-5 forward from the corrected tick-3 base with zero input,
        // PX should now reflect the server's correction, not the original run's value.
        Assert.Equal(state.PX + 5f, track.GetState().PX);
    }

    [Fact]
    public void ReconcileWithServer_SkipsCorrection_WhenPacketTickOutsideWindow()
    {
        var track = new SlopArena.Shared.Rollback.LocalTrack(TestHelpers.TestArena(), SelfId);
        track.RegisterEntity(Def, GroundState());

        // Tick 999 was never in this LocalTrack's history — must be a no-op, not a crash.
        track.ReconcileWithServer(new ServerEntityPacket { EntityId = SelfId, Tick = 999, State = default });

        Assert.Equal(0, track.CorrectionCount);
    }

    [Theory]
    [InlineData(ActionState.Attacking)]
    [InlineData(ActionState.Aiming)]
    public void ReconcileWithServer_DelayedAbilitySnapshotCannotRestartRecoveredSelf(ActionState action)
    {
        var track = new SlopArena.Shared.Rollback.LocalTrack(TestHelpers.TestArena(), SelfId);
        track.RegisterEntity(Def, GroundState(x: 50f, z: 50f));
        for (int tick = 0; tick < 5; tick++) track.Tick(default);
        var before = track.GetState();
        var lateAbility = before with
        {
            State = action, AttackSlot = AbilitySlots.Slot1, IsAiming = action == ActionState.Aiming,
            PX = before.PX + 8f,
        };

        track.ReconcileWithServer(new ServerEntityPacket
        {
            EntityId = SelfId, Tick = 3, State = CharacterStatePacket.FromState(lateAbility),
        });

        Assert.Equal(0, track.CorrectionCount);
        Assert.Equal(CharacterStatePacket.FromState(before), CharacterStatePacket.FromState(track.GetState()));

        // The next recoverable authority packet must still correct position, and
        // subsequent player inputs must be able to move and start a new ability.
        var recovered = before with { PX = before.PX + 2f };
        track.ReconcileWithServer(new ServerEntityPacket
        {
            EntityId = SelfId, Tick = 5, State = CharacterStatePacket.FromState(recovered),
        });
        Assert.Equal(1, track.CorrectionCount);
        Assert.Equal(recovered.PX, track.GetState().PX);
        for (int tick = 0; tick < 10; tick++) track.Tick(TestHelpers.Input(moveX: 1f));
        Assert.True(track.GetState().PX > recovered.PX);
        var nextAttack = track.Tick(TestHelpers.Input(activeSlot: AbilitySlots.Slot1));
        Assert.Equal(ActionState.Attacking, nextAttack.State);
        Assert.Equal(AbilitySlots.Slot1, nextAttack.AttackSlot);
    }

    [Fact]
    public void ReconcileWithServer_AuthoritativeHitstunStillInterruptsPredictableSelf()
    {
        var track = new SlopArena.Shared.Rollback.LocalTrack(TestHelpers.TestArena(), SelfId);
        track.RegisterEntity(Def, GroundState(x: 50f, z: 50f));
        track.Tick(default);
        var hit = track.GetState() with { State = ActionState.Hitstun, HitstunTicks = 12, DamagePercent = 7 };

        track.ReconcileWithServer(new ServerEntityPacket
        {
            EntityId = SelfId, Tick = 1, State = CharacterStatePacket.FromState(hit),
        });

        Assert.Equal(1, track.CorrectionCount);
        Assert.Equal(ActionState.Hitstun, track.GetState().State);
        Assert.Equal(hit.HitstunTicks, track.GetState().HitstunTicks);
        Assert.Equal(hit.DamagePercent, track.GetState().DamagePercent);
        Assert.Equal(ActionState.Hitstun, track.Tick(default).State);
    }

    [Fact]
    public void ReconcileWithServer_SkipsAuthoritativeHitstopSnapshot()
    {
        var track = new SlopArena.Shared.Rollback.LocalTrack(TestHelpers.TestArena(), SelfId);
        track.RegisterEntity(Def, GroundState());
        var before = track.GetState();
        var packetState = CharacterStatePacket.FromState(before, 0);
        packetState.HitstopTicks = 1;
        packetState.PositionX = before.PX + 99f;

        track.ReconcileWithServer(new ServerEntityPacket
        {
            EntityId = SelfId,
            Tick = 0,
            State = packetState,
        });

        Assert.Equal(0, track.CorrectionCount);
        Assert.Equal(before.PX, track.GetState().PX);
    }

    [Fact]
    public void ReconcileWithServer_SkipsReplaySuffixContainingHitstop()
    {
        var initial = GroundState();
        initial.HitstopTicks = 2;
        var track = new SlopArena.Shared.Rollback.LocalTrack(TestHelpers.TestArena(), SelfId);
        track.RegisterEntity(Def, initial);
        track.Tick(default);

        var packetState = CharacterStatePacket.FromState(GroundState(), 0);
        packetState.PositionX = 99f;
        track.ReconcileWithServer(new ServerEntityPacket
        {
            EntityId = SelfId,
            Tick = 0,
            State = packetState,
        });

        Assert.Equal(0, track.CorrectionCount);
        Assert.NotEqual(99f, track.GetState().PX);
    }

    [Fact]
    public void SyncOpponentMirror_PreventsTargetLockCrash()
    {
        // Regression test for the KeyNotFoundException risk: ServerSimulation.Tick()
        // indexes _states[targetId] directly whenever input.TargetEntityId != 0, for ANY
        // entity, attacking or not. A self-only LocalTrack sim with no opponents registered
        // must not crash when the player has an opponent soft-locked on screen.
        var arena = TestHelpers.TestArena();
        var track = new SlopArena.Shared.Rollback.LocalTrack(arena, SelfId);
        track.RegisterEntity(Def, GroundState());
        track.SyncOpponentMirror(OpponentId, Def, GroundState(x: 5f));

        var input = new InputState { TargetEntityId = (byte)OpponentId };
        var ex = Record.Exception(() => { track.Tick(input); });

        Assert.Null(ex);
    }

    [Fact]
    public void SyncOpponentMirror_CoupledStateDoesNotAuthoritativelyLinkSelf()
    {
        var track = new SlopArena.Shared.Rollback.LocalTrack(TestHelpers.TestArena(), SelfId);
        track.RegisterEntity(Def, GroundState());
        var opponent = GroundState(x: 5f);
        opponent.State = ActionState.Throwing;
        opponent.InteractionId = 72;
        opponent.InteractionPartnerId = SelfId;
        opponent.InteractionTick = 1;
        track.SyncOpponentMirror(OpponentId, Def, opponent);

        track.Tick(default);

        Assert.Equal((ulong)0, track.GetState().InteractionId);
        Assert.NotEqual(ActionState.Grabbed, track.GetState().State);
        Assert.NotEqual(ActionState.Throwing, track.GetState().State);
    }

}
