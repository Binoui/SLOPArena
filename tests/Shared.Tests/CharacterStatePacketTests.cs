using Xunit;

namespace SlopArena.Shared.Tests;

public class CharacterStatePacketTests
{
    [Fact]
    public void HitstunTimerSurvivesNetworkSnapshotAndAuthoritativeCorrection()
    {
        var state = new CharacterState { State = ActionState.Hitstun, HitstunTicks = 28 };
        var bytes = new byte[CharacterStatePacket.Size];
        CharacterStatePacket.FromState(state).Serialize(bytes);
        var packet = CharacterStatePacket.Deserialize(bytes);
        Assert.Equal(state.HitstunTicks, packet.ToState().HitstunTicks);
        var history = new CharacterState { HitstunTicks = 99, AttackElapsedTicks = 17 };
        packet.ApplyTo(ref history);
        Assert.Equal(state.HitstunTicks, history.HitstunTicks);
        Assert.Equal((ushort)17, history.AttackElapsedTicks);
    }

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        // Arrange: a CharacterState with every PvP-relevant field set to non-default values
        var original = new CharacterState
        {
            PX = 12.5f,
            PY = 3.25f,
            PZ = -7.1f,
            VX = 1.5f,
            VY = -2.5f,
            VZ = 0.75f,
            State = ActionState.Attacking,
            StateTicks = 42,
            IsGrounded = true,
            AttackSlot = 3,
            ComboStage = 2,
            AnimIndex = 5,
            AttackSequence = 9,
            FacingYaw = 1.234f,
            MatchState = MatchState.Playing,
            HitstunLevel = 2,
            AimPitch = -0.5f,
            Deaths = 2,
            DamagePercent = 87,
            Cooldown0 = 1,
            Cooldown1 = 12,
            Cooldown2 = 33,
            Cooldown3 = 44,
            Cooldown4 = 55,
            Cooldown5 = 66,
            AirTimeTicks = 37,
            DashDurationTicks = 9,
            DashDirX = 0.6f,
            DashDirZ = -0.8f,
            DashCooldownTicks = 20,
            AirDodgesLeft = 1,
            JumpsLeft = 2,
            InvincibilityTicks = 15,
            RushTicks = 4,
            LastDirX = 1f,
            LastDirZ = 0f,
            WasAirborneDuringKnockback = true,
            IsFastFalling = true,
            JumpFromSlide = true,
            SlideAttackCarryActive = true,
            CrouchSettled = true,
            QueuedCrouchBrace = true,
            InPostHitstunFlight = true,
            HitstopTicks = 17,
            BurstCooldownTicks = 1234,
            BurstRecoveryTicks = 25,
            LandingLagTicks = 18,
            ShieldDropTicks = 6,
            BlockStunTicks = 9,
            BlockHitstopKind = (byte)DefenseBlockHitstopKind.ShieldContact,
            AirDodgeRecoveryTicks = 14,
            InteractionPhase = (byte)DefenseInteractionPhase.Throwing,
            InteractionId = 0x0102030405060708UL,
            InteractionPartnerId = 0x1112131415161718UL,
            LastTerminalInteractionId = 0x2122232425262728UL,
            InteractionTick = 0x31323334,
            InteractionTerminalTick = 0x41424344,
            CapturedYaw = -12345,
        };

        // Act: FromState → Serialize → Deserialize → ToState
        var packet = CharacterStatePacket.FromState(original, tick: 999);
        byte[] buffer = new byte[CharacterStatePacket.Size];
        packet.Serialize(buffer);
        var restoredPacket = CharacterStatePacket.Deserialize(buffer);
        var restored = restoredPacket.ToState();

        // Assert: every PvP-relevant field round-trips
        Assert.Equal(999u, restoredPacket.TickNumber);
        Assert.Equal(original.PX, restored.PX);
        Assert.Equal(original.PY, restored.PY);
        Assert.Equal(original.PZ, restored.PZ);
        Assert.Equal(original.VX, restored.VX);
        Assert.Equal(original.VY, restored.VY);
        Assert.Equal(original.VZ, restored.VZ);
        Assert.Equal(original.State, restored.State);
        Assert.Equal(original.StateTicks, restored.StateTicks);
        Assert.Equal(original.IsGrounded, restored.IsGrounded);
        Assert.Equal(original.AttackSlot, restored.AttackSlot);
        Assert.Equal(original.ComboStage, restored.ComboStage);
        Assert.Equal(original.AnimIndex, restored.AnimIndex);
        Assert.Equal(original.AttackSequence, restored.AttackSequence);
        Assert.Equal(original.FacingYaw, restored.FacingYaw);
        Assert.Equal(original.MatchState, restored.MatchState);
        Assert.Equal(original.HitstunLevel, restored.HitstunLevel);
        Assert.Equal(original.AimPitch, restored.AimPitch);
        Assert.Equal(original.Deaths, restored.Deaths);
        Assert.Equal(original.DamagePercent, restored.DamagePercent);
        Assert.Equal(original.Cooldown0, restored.Cooldown0);
        Assert.Equal(original.Cooldown1, restored.Cooldown1);
        Assert.Equal(original.Cooldown2, restored.Cooldown2);
        Assert.Equal(original.Cooldown3, restored.Cooldown3);
        Assert.Equal(original.Cooldown4, restored.Cooldown4);
        Assert.Equal(original.Cooldown5, restored.Cooldown5);
        Assert.Equal(original.AirTimeTicks, restored.AirTimeTicks);
        Assert.Equal(original.DashDurationTicks, restored.DashDurationTicks);
        Assert.Equal(original.DashDirX, restored.DashDirX);
        Assert.Equal(original.DashDirZ, restored.DashDirZ);
        Assert.Equal(original.DashCooldownTicks, restored.DashCooldownTicks);
        Assert.Equal(original.AirDodgesLeft, restored.AirDodgesLeft);
        Assert.Equal(original.IsFastFalling, restored.IsFastFalling);
        Assert.Equal(original.JumpFromSlide, restored.JumpFromSlide);
        Assert.Equal(original.SlideAttackCarryActive, restored.SlideAttackCarryActive);
        Assert.Equal(original.CrouchSettled, restored.CrouchSettled);
        Assert.Equal(original.QueuedCrouchBrace, restored.QueuedCrouchBrace);
        Assert.Equal(original.InPostHitstunFlight, restored.InPostHitstunFlight);
        Assert.Equal(original.JumpsLeft, restored.JumpsLeft);
        Assert.Equal(original.InvincibilityTicks, restored.InvincibilityTicks);
        Assert.Equal(original.RushTicks, restored.RushTicks);
        Assert.Equal(original.LastDirX, restored.LastDirX);
        Assert.Equal(original.LastDirZ, restored.LastDirZ);
        Assert.Equal(original.WasAirborneDuringKnockback, restored.WasAirborneDuringKnockback);
        Assert.Equal(original.HitstopTicks, restored.HitstopTicks);
        Assert.Equal(original.BurstCooldownTicks, restored.BurstCooldownTicks);
        Assert.Equal(original.BurstRecoveryTicks, restored.BurstRecoveryTicks);
        Assert.Equal(original.LandingLagTicks, restored.LandingLagTicks);
        Assert.Equal(original.ShieldDropTicks, restored.ShieldDropTicks);
        Assert.Equal(original.BlockStunTicks, restored.BlockStunTicks);
        Assert.Equal(original.BlockHitstopKind, restored.BlockHitstopKind);
        Assert.Equal(original.AirDodgeRecoveryTicks, restored.AirDodgeRecoveryTicks);
        Assert.Equal(original.InteractionPhase, restored.InteractionPhase);
        Assert.Equal(original.InteractionId, restored.InteractionId);
        Assert.Equal(original.InteractionPartnerId, restored.InteractionPartnerId);
        Assert.Equal(original.LastTerminalInteractionId, restored.LastTerminalInteractionId);
        Assert.Equal(original.InteractionTick, restored.InteractionTick);
        Assert.Equal(original.InteractionTerminalTick, restored.InteractionTerminalTick);
        Assert.Equal(original.CapturedYaw, restored.CapturedYaw);
    }


    [Fact]
    public void Roundtrip_Cooldown6To10_And_JumpHeldTicks()
    {
        var original = new CharacterState
        {
            Cooldown6 = 111, Cooldown7 = 222, Cooldown8 = 333, Cooldown9 = 444, Cooldown10 = 555,
            JumpHeldTicks = 4,
        };
        var packet = CharacterStatePacket.FromState(original);
        byte[] buffer = new byte[CharacterStatePacket.Size];
        packet.Serialize(buffer);
        var restored = CharacterStatePacket.Deserialize(buffer).ToState();

        Assert.Equal((ushort)111, restored.Cooldown6);
        Assert.Equal((ushort)222, restored.Cooldown7);
        Assert.Equal((ushort)333, restored.Cooldown8);
        Assert.Equal((ushort)444, restored.Cooldown9);
        Assert.Equal((ushort)555, restored.Cooldown10);
        Assert.Equal((byte)4, restored.JumpHeldTicks);
    }

    [Fact]
    public void ApplyTo_OverwritesOnlyWireFields_PreservesRest()
    {
        // Non-wire fields survive while all carried fields, including the state timer and
        // defense reconstruction data, are overwritten by the server snapshot.
        var target = new CharacterState
        {
            PX = 1f,
            StateTicks = 900,
            ShieldDropTicks = 300,
            InteractionId = 90,
            AttackElapsedTicks = 500, // NOT carried by CharacterStatePacket — must survive
            AirTimeTicks = 999,       // IS carried — must be overwritten
            QueuedKBDirX = 3.5f,      // NOT carried (queued launch payload, ADR-0012) — must survive
            HitstopTicks = 0,         // IS carried — must be overwritten
        };
        var packet = CharacterStatePacket.FromState(new CharacterState
        {
            PX = 42f,
            StateTicks = 7,
            AirTimeTicks = 7,
            HitstopTicks = 9,
            ShieldDropTicks = 4,
            InteractionId = 12,
            InteractionPartnerId = 13,
            BlockStunTicks = 8,
            BlockHitstopKind = (byte)DefenseBlockHitstopKind.ShieldContact,
            AirDodgeRecoveryTicks = 17,
            InteractionPhase = (byte)DefenseInteractionPhase.Captured,
            LastTerminalInteractionId = 14,
            InteractionTick = 15,
            InteractionTerminalTick = 16,
            CapturedYaw = 1700,
        });

        // Act
        packet.ApplyTo(ref target);

        // Assert
        Assert.Equal(42f, target.PX);       // wire field overwritten
        Assert.Equal((ushort)7, target.AirTimeTicks); // wire field overwritten
        Assert.Equal((ushort)9, target.HitstopTicks); // wire field overwritten (ADR-0012)
        Assert.Equal((ushort)7, target.StateTicks);
        Assert.Equal((ushort)4, target.ShieldDropTicks);
        Assert.Equal(12UL, target.InteractionId);
        Assert.Equal(13UL, target.InteractionPartnerId);
        Assert.Equal((ushort)8, target.BlockStunTicks);
        Assert.Equal((byte)DefenseBlockHitstopKind.ShieldContact, target.BlockHitstopKind);
        Assert.Equal((ushort)17, target.AirDodgeRecoveryTicks);
        Assert.Equal((byte)DefenseInteractionPhase.Captured, target.InteractionPhase);
        Assert.Equal(14UL, target.LastTerminalInteractionId);
        Assert.Equal(15u, target.InteractionTick);
        Assert.Equal(16u, target.InteractionTerminalTick);
        Assert.Equal((short)1700, target.CapturedYaw);
        Assert.Equal((ushort)500, target.AttackElapsedTicks); // non-wire field preserved
        Assert.Equal(3.5f, target.QueuedKBDirX);             // non-wire field preserved
    }

    [Fact]
    public void DefenseActionStateCodes_AreAppendedAndRoundTrip()
    {
        Assert.Equal((byte)11, (byte)ActionState.Crouching);
        var states = new (ActionState State, byte WireValue)[]
        {
            (ActionState.Shielding, 12),
            (ActionState.ShieldDrop, 13),
            (ActionState.GrabAttempt, 14),
            (ActionState.Grabbed, 15),
            (ActionState.Throwing, 16),
            (ActionState.AirDodgeMovement, 18),
            (ActionState.AirDodgeRecovery, 19),
        };
        foreach (var (state, wireValue) in states)
        {
            var packet = CharacterStatePacket.FromState(new CharacterState { State = state, StateTicks = 7 });
            byte[] buffer = new byte[CharacterStatePacket.Size];
            packet.Serialize(buffer);
            var restored = CharacterStatePacket.Deserialize(buffer).ToState();
            Assert.Equal(wireValue, packet.CurrentActionState);
            Assert.Equal(state, restored.State);
            Assert.Equal((ushort)7, restored.StateTicks);
        }
    }

    [Theory]
    [InlineData(ActionState.AirDodgeMovement, (byte)18, (ushort)0, (ushort)4)]
    [InlineData(ActionState.AirDodgeRecovery, (byte)19, (ushort)12, (ushort)0)]
    public void AirDodgePhasePacket_RoundTripsStateDirectionAndTimers(
        ActionState state, byte wireValue, ushort recovery, ushort invincibility)
    {
        var original = new CharacterState
        {
            State = state,
            StateTicks = 9,
            DashDirX = -0.6f,
            DashDirZ = 0.8f,
            AirDodgeRecoveryTicks = recovery,
            InvincibilityTicks = invincibility,
            AirDodgesLeft = 0,
        };

        var packet = CharacterStatePacket.FromState(original);
        byte[] buffer = new byte[CharacterStatePacket.Size];
        packet.Serialize(buffer);
        var restored = CharacterStatePacket.Deserialize(buffer).ToState();

        Assert.Equal(wireValue, packet.CurrentActionState);
        Assert.Equal(state, restored.State);
        Assert.Equal(original.StateTicks, restored.StateTicks);
        Assert.Equal(original.DashDirX, restored.DashDirX);
        Assert.Equal(original.DashDirZ, restored.DashDirZ);
        Assert.Equal(original.AirDodgeRecoveryTicks, restored.AirDodgeRecoveryTicks);
        Assert.Equal(original.InvincibilityTicks, restored.InvincibilityTicks);
        Assert.Equal(original.AirDodgesLeft, restored.AirDodgesLeft);
    }

    [Fact]
    public void RoundTrip_LockStateAndTargetId()
    {
        // Lock state and selected target participate in rollback reconstruction.
        var original = new CharacterState { LockOn = true, TargetEntityId = 100 };
        var packet = CharacterStatePacket.FromState(original);
        byte[] buffer = new byte[CharacterStatePacket.Size];
        packet.Serialize(buffer);
        var restored = CharacterStatePacket.Deserialize(buffer).ToState();

        Assert.True(restored.LockOn);
        Assert.False(restored.AutoLockSuppressed);
        Assert.Equal(100UL, restored.TargetEntityId);

        // ApplyTo is the LocalTrack patch path and must preserve lock state and target.
        var target = new CharacterState();
        CharacterStatePacket.FromState(original).ApplyTo(ref target);
        Assert.True(target.LockOn);
        Assert.False(target.AutoLockSuppressed);
        Assert.Equal(100UL, target.TargetEntityId);

    }
    [Fact]
    public void RoundTrip_AutoLockSuppression()
    {
        var original = new CharacterState { AutoLockSuppressed = true };
        var packet = CharacterStatePacket.FromState(original);
        byte[] buffer = new byte[CharacterStatePacket.Size];
        packet.Serialize(buffer);
        var restored = CharacterStatePacket.Deserialize(buffer).ToState();

        Assert.False(restored.LockOn);
        Assert.True(restored.AutoLockSuppressed);

        var target = new CharacterState();
        packet.ApplyTo(ref target);
        Assert.False(target.LockOn);
        Assert.True(target.AutoLockSuppressed);
    }


    [Fact]
    public void RoundTrip_AnimIndex_NonZero()
    {
        // AnimIndex was previously missing from Serialize/Deserialize (the original bug).
        // This test defends against regression: a non-zero AnimIndex must survive the wire.
        var state = new CharacterState { AnimIndex = 7 };
        var packet = CharacterStatePacket.FromState(state);
        byte[] buffer = new byte[CharacterStatePacket.Size];
        packet.Serialize(buffer);
        var restored = CharacterStatePacket.Deserialize(buffer).ToState();
        Assert.Equal((byte)7, restored.AnimIndex);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CorrectionSnapshotPreservesCommittedPoseAndCapturedTarget(bool active)
    {
        var original = new CharacterState
        {
            AttackPosePitch = -.2f,
            AimPitch = .4f,
            AttackCorrectionStartYaw = 1.1f,
            AttackCorrectionTargetId = 0xfedcba9876543210UL,
            AttackCorrectionTargetDeaths = 2,
            AttackCorrectionActive = active,
            AttackCorrectionOwned = true,
        };
        var buffer = new byte[CharacterStatePacket.Size];
        CharacterStatePacket.FromState(original).Serialize(buffer);
        var packet = CharacterStatePacket.Deserialize(buffer);
        var restored = packet.ToState();
        Assert.Equal(original.AttackPosePitch, restored.AttackPosePitch);
        Assert.Equal(original.AimPitch, restored.AimPitch);
        Assert.Equal(original.AttackCorrectionStartYaw, restored.AttackCorrectionStartYaw);
        Assert.Equal(original.AttackCorrectionTargetId, restored.AttackCorrectionTargetId);
        Assert.Equal(original.AttackCorrectionTargetDeaths, restored.AttackCorrectionTargetDeaths);
        Assert.Equal(active, restored.AttackCorrectionActive);
        Assert.True(restored.AttackCorrectionOwned);
        var history = new CharacterState { AttackElapsedTicks = 13, AttackPosePitch = .7f };
        packet.ApplyTo(ref history);
        Assert.Equal(original.AttackPosePitch, history.AttackPosePitch);
        Assert.Equal(original.AttackCorrectionTargetId, history.AttackCorrectionTargetId);
        Assert.Equal(original.AttackCorrectionTargetDeaths, history.AttackCorrectionTargetDeaths);
        Assert.Equal(active, history.AttackCorrectionActive);
        Assert.True(history.AttackCorrectionOwned);
        Assert.Equal((ushort)13, history.AttackElapsedTicks);
    }

    [Fact]
    public void Deserialize_RejectsTruncatedLegacyAndWrongVersionPayloads()
    {
        var packet = CharacterStatePacket.FromState(default);
        var buffer = new byte[CharacterStatePacket.Size];
        packet.Serialize(buffer);

        // A pre-defense protocol-v1 payload has the old size and must not be accepted.
        Assert.Throws<ArgumentException>(() => CharacterStatePacket.Deserialize(buffer.AsSpan(0, 114)));
        Assert.Throws<ArgumentException>(() => CharacterStatePacket.Deserialize(buffer.AsSpan(0, 182)));

        var wrongVersion = (byte[])buffer.Clone();
        wrongVersion[163] = 3;
        Assert.Throws<InvalidDataException>(() => CharacterStatePacket.Deserialize(wrongVersion));
    }
}
