using System;
using System.Buffers.Binary;
using System.IO;

namespace SlopArena.Shared
{
    public struct CharacterStatePacket
    {
        public uint TickNumber;
        public float PositionX;
        public float PositionY;
        public float PositionZ;
        public float VelocityX;
        public float VelocityY;
        public float VelocityZ;
        /// <summary>
        /// ActionState value, including append-only defense and air-dodge phases.
        /// </summary>
        public byte CurrentActionState;
        /// <summary>
        /// Number of physics frames remaining in this state
        /// </summary>
        public ushort StateDurationFrames;

        /// <summary>IsGrounded flag from server.</summary>
        public bool IsGrounded;

        /// <summary>Attack slot (1-6) for animation selection on client/ghost.</summary>
        public byte AttackSlot;
        /// <summary>Combo stage index for animation selection.</summary>
        public byte ComboStage;
        /// <summary>Animation index into the ability's AnimationNames[] (set by server ability class).</summary>
        public byte AnimIndex;
        /// <summary>Changes on every ability activation, including same-slot IASA restarts.</summary>
        public byte AttackSequence;
        /// <summary>Facing yaw in radians, from server authority.</summary>
        public float FacingYaw;

        /// <summary>Match lifecycle state from server.</summary>
        public MatchState MatchState;
        /// <summary>Hitstun animation tier: 0=small, 1=medium, 2=hard.</summary>
        public byte HitstunLevel;
        public ushort HitstunTicks;
        public ushort HitReactionSequence;
        /// <summary>Aim pitch in radians, from server authority.</summary>
        public float AimPitch;
        /// <summary>Match death counter (stock counter: stocks = maxStocks - Deaths). Issue #37.</summary>
        public byte Deaths;
        /// <summary>Smash-style damage percent 0-999, sent so the client HUD can show every player's %. Issue #38.</summary>
        public ushort DamagePercent;
        /// <summary>Per-slot cooldown ticks (0-10), sent so the local player's HUD cooldown fills work in PvP. Issue #38, ADR-0016.</summary>
        public ushort Cooldown0, Cooldown1, Cooldown2, Cooldown3, Cooldown4, Cooldown5,
            Cooldown6, Cooldown7, Cooldown8, Cooldown9, Cooldown10;
        /// <summary>Consecutive jump-held ticks (issue #116) — needed for byte-identical replay of a JumpSquat opponent.</summary>
        public byte JumpHeldTicks;
        /// <summary>Persistent target lock state for the client lock indicator.</summary>
        public bool LockOn;
        /// <summary>Auto-lock suppression latch, replicated for rollback reconstruction.</summary>
        public bool AutoLockSuppressed;
        // Predictable ActionState fields carried for rollback. AirDodgeMovement uses
        // DashDirX/Z as its captured stick or facing direction; DashDurationTicks and
        // DashCooldownTicks remain legacy wire fields and do not time or gate air dodge.
        public ushort AirTimeTicks;
        public ushort DashDurationTicks;
        public float DashDirX, DashDirZ;
        public ushort DashCooldownTicks;
        public byte AirDodgesLeft;
        public byte JumpsLeft;
        public ushort InvincibilityTicks;
        public ushort RushTicks;
        public float LastDirX, LastDirZ;
        public bool WasAirborneDuringKnockback;
        /// <summary>Replicated low-locomotion and fast-fall flags.</summary>
        public bool IsFastFalling;
        public bool JumpFromSlide;
        public bool SlideAttackCarryActive;
        public bool CrouchSettled;
        public bool QueuedCrouchBrace;
        public bool InPostHitstunFlight;
        /// <summary>Remaining hitstop freeze ticks (ADR-0012).</summary>
        public ushort HitstopTicks;
        /// <summary>Reserved retired Burst wire field; no gameplay or HUD meaning.</summary>
        public ushort BurstCooldownTicks;
        /// <summary>Reserved retired Burst wire field; never locks actions.</summary>
        public ushort BurstRecoveryTicks;
        /// <summary>Reserved ledge re-grab timer field; kept in the versioned state packet
        /// while automatic ledge grabs are disabled.</summary>
        public ushort LedgeRegrabLockTicks;
        /// <summary>Remaining landing-lag lock ticks. Authoritative so local and remote presentation/rollback tracks agree.</summary>
        public ushort LandingLagTicks;
        public ushort ShieldDropTicks;
        public ushort BlockStunTicks;
        public byte BlockHitstopKind;
        public byte InteractionPhase;
        public ulong InteractionId;
        public ulong InteractionPartnerId;
        public ulong LastTerminalInteractionId;
        public uint InteractionTick;
        public uint InteractionTerminalTick;
        public short CapturedYaw;
        public ushort AirDodgeRecoveryTicks;
        /// <summary>Sticky target ID, required to replay lock acquisition deterministically.</summary>
        public ulong TargetEntityId;
        public float AttackPosePitch;
        public float AttackCorrectionStartYaw;
        public ulong AttackCorrectionTargetId;
        public byte AttackCorrectionTargetDeaths;
        public bool AttackCorrectionActive;
        public bool AttackCorrectionOwned;
        /// <summary>Elapsed ticks of the active attack; drives remote trails, accents and swing audio.</summary>
        public ushort AttackElapsedTicks;
        /// <summary>Held charge of the active charged ability; drives remote charge visuals.</summary>
        public ushort ChargeTicks;
        /// <summary>Knockback velocity; launch presentation and predicted post-hitstun flight.</summary>
        public float KnockbackVelocityX, KnockbackVelocityY, KnockbackVelocityZ;
        /// <summary>Fixed state, target lock, startup correction, protocol version and remote presentation timers.</summary>
        public const int Size = 202;

        /// <summary>Convert from CharacterState to serializable packet.</summary>
        public static CharacterStatePacket FromState(CharacterState s, uint tick = 0)
        {
            return new CharacterStatePacket
            {
                TickNumber = tick,
                PositionX = s.PX,
                PositionY = s.PY,
                PositionZ = s.PZ,
                VelocityX = s.VX,
                VelocityY = s.VY,
                VelocityZ = s.VZ,
                CurrentActionState = (byte)s.State,
                StateDurationFrames = s.StateTicks,
                IsGrounded = s.IsGrounded,
                AttackSlot = s.AttackSlot,
                ComboStage = s.ComboStage,
                FacingYaw = s.FacingYaw,
                AnimIndex = s.AnimIndex,
                AttackSequence = s.AttackSequence,
                MatchState = s.MatchState,
                HitstunLevel = s.HitstunLevel,
                HitstunTicks = s.HitstunTicks,
                HitReactionSequence = s.HitReactionSequence,
                AimPitch = s.AimPitch,
                Deaths = s.Deaths,
                DamagePercent = s.DamagePercent,
                Cooldown0 = s.Cooldown0,
                Cooldown1 = s.Cooldown1,
                Cooldown2 = s.Cooldown2,
                Cooldown3 = s.Cooldown3,
                Cooldown4 = s.Cooldown4,
                Cooldown5 = s.Cooldown5,
                Cooldown6 = s.Cooldown6,
                Cooldown7 = s.Cooldown7,
                Cooldown8 = s.Cooldown8,
                Cooldown9 = s.Cooldown9,
                Cooldown10 = s.Cooldown10,
                JumpHeldTicks = s.JumpHeldTicks,
                LockOn = s.LockOn,
                AutoLockSuppressed = s.AutoLockSuppressed,
                AirTimeTicks = s.AirTimeTicks,
                DashDurationTicks = s.DashDurationTicks,
                DashDirX = s.DashDirX,
                DashDirZ = s.DashDirZ,
                DashCooldownTicks = s.DashCooldownTicks,
                AirDodgesLeft = s.AirDodgesLeft,
                JumpsLeft = s.JumpsLeft,
                InvincibilityTicks = s.InvincibilityTicks,
                RushTicks = s.RushTicks,
                LastDirX = s.LastDirX,
                LastDirZ = s.LastDirZ,
                WasAirborneDuringKnockback = s.WasAirborneDuringKnockback,
                IsFastFalling = s.IsFastFalling,
                JumpFromSlide = s.JumpFromSlide,
                SlideAttackCarryActive = s.SlideAttackCarryActive,
                CrouchSettled = s.CrouchSettled,
                QueuedCrouchBrace = s.QueuedCrouchBrace,
                InPostHitstunFlight = s.InPostHitstunFlight,
                HitstopTicks = s.HitstopTicks,
                BurstCooldownTicks = s.BurstCooldownTicks,
                BurstRecoveryTicks = s.BurstRecoveryTicks,
                LedgeRegrabLockTicks = s.LedgeRegrabLockTicks,
                LandingLagTicks = s.LandingLagTicks,
                ShieldDropTicks = s.ShieldDropTicks,
                BlockStunTicks = s.BlockStunTicks,
                BlockHitstopKind = s.BlockHitstopKind,
                InteractionPhase = s.InteractionPhase,
                InteractionId = s.InteractionId,
                InteractionPartnerId = s.InteractionPartnerId,
                LastTerminalInteractionId = s.LastTerminalInteractionId,
                InteractionTick = s.InteractionTick,
                InteractionTerminalTick = s.InteractionTerminalTick,
                CapturedYaw = s.CapturedYaw,
                AirDodgeRecoveryTicks = s.AirDodgeRecoveryTicks,
                TargetEntityId = s.TargetEntityId,
                AttackPosePitch = s.AttackPosePitch,
                AttackCorrectionStartYaw = s.AttackCorrectionStartYaw,
                AttackCorrectionTargetId = s.AttackCorrectionTargetId,
                AttackCorrectionTargetDeaths = s.AttackCorrectionTargetDeaths,
                AttackCorrectionActive = s.AttackCorrectionActive,
                AttackCorrectionOwned = s.AttackCorrectionOwned,
                // Ability timers only mean something while an ability runs; a stale value
                // after recovery must not make otherwise identical snapshots differ.
                AttackElapsedTicks = s.State is ActionState.Attacking or ActionState.Aiming ? s.AttackElapsedTicks : (ushort)0,
                ChargeTicks = s.State is ActionState.Attacking or ActionState.Aiming ? s.ChargeTicks : (ushort)0,
                KnockbackVelocityX = s.KVX,
                KnockbackVelocityY = s.KVY,
                KnockbackVelocityZ = s.KVZ,
            };
        }

        public CharacterState ToState()
        {
            return new CharacterState
            {
                PX = PositionX,
                PY = PositionY,
                PZ = PositionZ,
                VX = VelocityX,
                VY = VelocityY,
                VZ = VelocityZ,
                State = (ActionState)CurrentActionState,
                IsGrounded = IsGrounded,
                StateTicks = StateDurationFrames,
                AttackSlot = AttackSlot,
                ComboStage = ComboStage,
                AnimIndex = AnimIndex,
                AttackSequence = AttackSequence,
                FacingYaw = FacingYaw,
                MatchState = MatchState,
                HitstunLevel = HitstunLevel,
                HitstunTicks = HitstunTicks,
                HitReactionSequence = HitReactionSequence,
                AimPitch = AimPitch,
                Deaths = Deaths,
                DamagePercent = DamagePercent,
                Cooldown0 = Cooldown0,
                Cooldown1 = Cooldown1,
                Cooldown2 = Cooldown2,
                Cooldown3 = Cooldown3,
                Cooldown4 = Cooldown4,
                Cooldown5 = Cooldown5,
                Cooldown6 = Cooldown6,
                Cooldown7 = Cooldown7,
                Cooldown8 = Cooldown8,
                Cooldown9 = Cooldown9,
                Cooldown10 = Cooldown10,
                JumpHeldTicks = JumpHeldTicks,
                LockOn = LockOn,
                AutoLockSuppressed = AutoLockSuppressed,
                AirTimeTicks = AirTimeTicks,
                DashDurationTicks = DashDurationTicks,
                DashDirX = DashDirX,
                DashDirZ = DashDirZ,
                DashCooldownTicks = DashCooldownTicks,
                AirDodgesLeft = AirDodgesLeft,
                JumpsLeft = JumpsLeft,
                InvincibilityTicks = InvincibilityTicks,
                RushTicks = RushTicks,
                LastDirX = LastDirX,
                LastDirZ = LastDirZ,
                WasAirborneDuringKnockback = WasAirborneDuringKnockback,
                IsFastFalling = IsFastFalling,
                JumpFromSlide = JumpFromSlide,
                SlideAttackCarryActive = SlideAttackCarryActive,
                CrouchSettled = CrouchSettled,
                QueuedCrouchBrace = QueuedCrouchBrace,
                InPostHitstunFlight = InPostHitstunFlight,
                HitstopTicks = HitstopTicks,
                BurstCooldownTicks = BurstCooldownTicks,
                BurstRecoveryTicks = BurstRecoveryTicks,
                LedgeRegrabLockTicks = LedgeRegrabLockTicks,
                LandingLagTicks = LandingLagTicks,
                ShieldDropTicks = ShieldDropTicks,
                BlockStunTicks = BlockStunTicks,
                BlockHitstopKind = BlockHitstopKind,
                InteractionPhase = InteractionPhase,
                InteractionId = InteractionId,
                InteractionPartnerId = InteractionPartnerId,
                LastTerminalInteractionId = LastTerminalInteractionId,
                InteractionTick = InteractionTick,
                InteractionTerminalTick = InteractionTerminalTick,
                CapturedYaw = CapturedYaw,
                AirDodgeRecoveryTicks = AirDodgeRecoveryTicks,
                TargetEntityId = TargetEntityId,
                AttackPosePitch = AttackPosePitch,
                AttackCorrectionStartYaw = AttackCorrectionStartYaw,
                AttackCorrectionTargetId = AttackCorrectionTargetId,
                AttackCorrectionTargetDeaths = AttackCorrectionTargetDeaths,
                AttackCorrectionActive = AttackCorrectionActive,
                AttackCorrectionOwned = AttackCorrectionOwned,
                AttackElapsedTicks = AttackElapsedTicks,
                ChargeTicks = ChargeTicks,
                KVX = KnockbackVelocityX,
                KVY = KnockbackVelocityY,
                KVZ = KnockbackVelocityZ,
            };
        }

        public void Serialize(Span<byte> buffer)
        {
            if (buffer.Length < Size)
                throw new ArgumentException("Buffer too small");

            BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(0, 4), TickNumber);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(4, 4), BitConverter.SingleToInt32Bits(PositionX));
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(8, 4), BitConverter.SingleToInt32Bits(PositionY));
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(12, 4), BitConverter.SingleToInt32Bits(PositionZ));
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(16, 4), BitConverter.SingleToInt32Bits(VelocityX));
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(20, 4), BitConverter.SingleToInt32Bits(VelocityY));
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(24, 4), BitConverter.SingleToInt32Bits(VelocityZ));
            buffer[28] = CurrentActionState;
            buffer[29] = IsGrounded ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(30, 2), StateDurationFrames);
            buffer[32] = AttackSlot;
            buffer[33] = ComboStage;
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(34, 4), BitConverter.SingleToInt32Bits(FacingYaw));
            buffer[38] = (byte)MatchState;
            buffer[39] = AnimIndex;
            buffer[40] = HitstunLevel;
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(41, 4), BitConverter.SingleToInt32Bits(AimPitch));
            buffer[45] = Deaths;
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(46, 2), DamagePercent);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(48, 2), Cooldown0);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(50, 2), Cooldown1);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(52, 2), Cooldown2);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(54, 2), Cooldown3);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(56, 2), Cooldown4);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(58, 2), Cooldown5);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(60, 2), Cooldown6);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(62, 2), Cooldown7);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(64, 2), Cooldown8);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(66, 2), Cooldown9);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(68, 2), Cooldown10);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(70, 2), AirTimeTicks);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(72, 2), DashDurationTicks);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(74, 4), BitConverter.SingleToInt32Bits(DashDirX));
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(78, 4), BitConverter.SingleToInt32Bits(DashDirZ));
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(82, 2), DashCooldownTicks);
            buffer[84] = AirDodgesLeft;
            buffer[85] = JumpsLeft;
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(86, 2), InvincibilityTicks);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(88, 2), RushTicks);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(90, 4), BitConverter.SingleToInt32Bits(LastDirX));
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(94, 4), BitConverter.SingleToInt32Bits(LastDirZ));
            buffer[98] = WasAirborneDuringKnockback ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(99, 2), HitstopTicks);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(101, 2), BurstCooldownTicks);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(103, 2), BurstRecoveryTicks);
            buffer[105] = JumpHeldTicks;
            buffer[106] = LockOn ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(107, 2), LedgeRegrabLockTicks);
            buffer[109] = AttackSequence;
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(110, 2), LandingLagTicks);
            byte movementFlags = 0;
            if (IsFastFalling) movementFlags |= 0x01;
            if (JumpFromSlide) movementFlags |= 0x02;
            if (SlideAttackCarryActive) movementFlags |= 0x04;
            if (CrouchSettled) movementFlags |= 0x08;
            if (QueuedCrouchBrace) movementFlags |= 0x10;
            if (InPostHitstunFlight) movementFlags |= 0x20;
            if (AutoLockSuppressed) movementFlags |= 0x40;
            buffer[112] = movementFlags;
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(113, 2), ShieldDropTicks);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(115, 2), BlockStunTicks);
            buffer[117] = BlockHitstopKind;
            buffer[118] = InteractionPhase;
            BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(119, 8), InteractionId);
            BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(127, 8), InteractionPartnerId);
            BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(135, 8), LastTerminalInteractionId);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(143, 4), InteractionTick);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(147, 4), InteractionTerminalTick);
            BinaryPrimitives.WriteInt16LittleEndian(buffer.Slice(151, 2), CapturedYaw);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(153, 2), AirDodgeRecoveryTicks);
            BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(155, 8), TargetEntityId);
            buffer[163] = SimulationProtocol.Version;
            BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(164, 8), AttackCorrectionTargetId);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(172, 4), BitConverter.SingleToInt32Bits(AttackCorrectionStartYaw));
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(176, 4), BitConverter.SingleToInt32Bits(AttackPosePitch));
            buffer[180] = (byte)((AttackCorrectionActive ? 1 : 0) | (AttackCorrectionOwned ? 2 : 0));
            buffer[181] = AttackCorrectionTargetDeaths;
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(182, 2), HitstunTicks);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(184, 2), HitReactionSequence);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(186, 2), AttackElapsedTicks);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(188, 2), ChargeTicks);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(190, 4), BitConverter.SingleToInt32Bits(KnockbackVelocityX));
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(194, 4), BitConverter.SingleToInt32Bits(KnockbackVelocityY));
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(198, 4), BitConverter.SingleToInt32Bits(KnockbackVelocityZ));
        }

        public static CharacterStatePacket Deserialize(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length != Size)
                throw new ArgumentException($"State payload must be exactly {Size} bytes.", nameof(buffer));
            if (buffer[163] != SimulationProtocol.Version)
                throw new InvalidDataException($"Unsupported state protocol version {buffer[163]}.");
            var packet = new CharacterStatePacket();
            packet.TickNumber = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(0, 4));
            packet.PositionX = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(4, 4)));
            packet.PositionY = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(8, 4)));
            packet.PositionZ = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(12, 4)));
            packet.VelocityX = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(16, 4)));
            packet.VelocityY = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(20, 4)));
            packet.VelocityZ = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(24, 4)));
            packet.CurrentActionState = buffer[28];
            packet.IsGrounded = buffer[29] != 0;
            packet.StateDurationFrames = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(30, 2));
            packet.AttackSlot = buffer[32];
            packet.ComboStage = buffer[33];
            packet.FacingYaw = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(34, 4)));
            packet.MatchState = (MatchState)buffer[38];
            packet.AnimIndex = buffer[39];
            packet.HitstunLevel = buffer[40];
            packet.AimPitch = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(41, 4)));
            packet.Deaths = buffer[45];
            packet.DamagePercent = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(46, 2));
            packet.Cooldown0 = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(48, 2));
            packet.Cooldown1 = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(50, 2));
            packet.Cooldown2 = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(52, 2));
            packet.Cooldown3 = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(54, 2));
            packet.Cooldown4 = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(56, 2));
            packet.Cooldown5 = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(58, 2));
            packet.Cooldown6 = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(60, 2));
            packet.Cooldown7 = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(62, 2));
            packet.Cooldown8 = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(64, 2));
            packet.Cooldown9 = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(66, 2));
            packet.Cooldown10 = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(68, 2));
            packet.AirTimeTicks = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(70, 2));
            packet.DashDurationTicks = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(72, 2));
            packet.DashDirX = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(74, 4)));
            packet.DashDirZ = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(78, 4)));
            packet.DashCooldownTicks = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(82, 2));
            packet.AirDodgesLeft = buffer[84];
            packet.JumpsLeft = buffer[85];
            packet.InvincibilityTicks = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(86, 2));
            packet.RushTicks = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(88, 2));
            packet.LastDirX = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(90, 4)));
            packet.LastDirZ = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(94, 4)));
            packet.WasAirborneDuringKnockback = buffer[98] != 0;
            packet.HitstopTicks = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(99, 2));
            packet.BurstCooldownTicks = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(101, 2));
            packet.BurstRecoveryTicks = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(103, 2));
            packet.JumpHeldTicks = buffer[105];
            packet.LockOn = buffer[106] != 0;
            packet.LedgeRegrabLockTicks = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(107, 2));
            packet.AttackSequence = buffer[109];
            packet.LandingLagTicks = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(110, 2));
            byte movementFlags = buffer[112];
            packet.IsFastFalling = (movementFlags & 0x01) != 0;
            packet.JumpFromSlide = (movementFlags & 0x02) != 0;
            packet.SlideAttackCarryActive = (movementFlags & 0x04) != 0;
            packet.CrouchSettled = (movementFlags & 0x08) != 0;
            packet.QueuedCrouchBrace = (movementFlags & 0x10) != 0;
            packet.InPostHitstunFlight = (movementFlags & 0x20) != 0;
            packet.AutoLockSuppressed = (movementFlags & 0x40) != 0;
            packet.ShieldDropTicks = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(113, 2));
            packet.BlockStunTicks = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(115, 2));
            packet.BlockHitstopKind = buffer[117];
            packet.InteractionPhase = buffer[118];
            packet.InteractionId = BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(119, 8));
            packet.InteractionPartnerId = BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(127, 8));
            packet.LastTerminalInteractionId = BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(135, 8));
            packet.InteractionTick = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(143, 4));
            packet.InteractionTerminalTick = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(147, 4));
            packet.CapturedYaw = BinaryPrimitives.ReadInt16LittleEndian(buffer.Slice(151, 2));
            packet.AirDodgeRecoveryTicks = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(153, 2));
            packet.TargetEntityId = BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(155, 8));
            packet.AttackCorrectionTargetId = BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(164, 8));
            packet.AttackCorrectionStartYaw = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(172, 4)));
            packet.AttackPosePitch = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(176, 4)));
            packet.AttackCorrectionActive = (buffer[180] & 1) != 0;
            packet.AttackCorrectionOwned = (buffer[180] & 2) != 0;
            packet.AttackCorrectionTargetDeaths = buffer[181];
            packet.HitstunTicks = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(182, 2));
            packet.HitReactionSequence = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(184, 2));
            packet.AttackElapsedTicks = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(186, 2));
            packet.ChargeTicks = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(188, 2));
            packet.KnockbackVelocityX = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(190, 4)));
            packet.KnockbackVelocityY = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(194, 4)));
            packet.KnockbackVelocityZ = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(198, 4)));
            return packet;
        }

        /// <summary>
        /// Overwrite only the fields this packet carries on an existing CharacterState,
        /// in place. Unlike ToState() (which builds a fresh CharacterState and leaves every
        /// non-wire field at its default), this preserves everything ApplyTo doesn't touch —
        /// used by LocalTrack (ADR-0011), which must patch its own full-fidelity self state
        /// with the server's authoritative wire fields without clobbering fields the wire
        /// doesn't carry (e.g. the queued launch payload, directional influence, charge stock)
        /// or the live ability timers it carries only for remote presentation.
        /// </summary>
        public void ApplyTo(ref CharacterState s)
        {
            s.PX = PositionX; s.PY = PositionY; s.PZ = PositionZ;
            s.VX = VelocityX; s.VY = VelocityY; s.VZ = VelocityZ;
            s.State = (ActionState)CurrentActionState;
            s.StateTicks = StateDurationFrames;
            s.IsGrounded = IsGrounded;
            s.AttackSlot = AttackSlot;
            s.ComboStage = ComboStage;
            s.AnimIndex = AnimIndex;
            s.AttackSequence = AttackSequence;
            s.FacingYaw = FacingYaw;
            s.MatchState = MatchState;
            s.HitstunLevel = HitstunLevel;
            s.HitstunTicks = HitstunTicks;
            s.HitReactionSequence = HitReactionSequence;
            s.AimPitch = AimPitch;
            s.Deaths = Deaths;
            s.DamagePercent = DamagePercent;
            s.Cooldown0 = Cooldown0; s.Cooldown1 = Cooldown1; s.Cooldown2 = Cooldown2;
            s.Cooldown3 = Cooldown3; s.Cooldown4 = Cooldown4; s.Cooldown5 = Cooldown5;
            s.Cooldown6 = Cooldown6; s.Cooldown7 = Cooldown7; s.Cooldown8 = Cooldown8;
            s.Cooldown9 = Cooldown9; s.Cooldown10 = Cooldown10;
            s.JumpHeldTicks = JumpHeldTicks;
            s.LockOn = LockOn;
            s.AutoLockSuppressed = AutoLockSuppressed;
            s.AirTimeTicks = AirTimeTicks;
            s.DashDurationTicks = DashDurationTicks;
            s.DashDirX = DashDirX; s.DashDirZ = DashDirZ;
            s.DashCooldownTicks = DashCooldownTicks;
            s.AirDodgesLeft = AirDodgesLeft;
            s.JumpsLeft = JumpsLeft;
            s.InvincibilityTicks = InvincibilityTicks;
            s.RushTicks = RushTicks;
            s.LastDirX = LastDirX; s.LastDirZ = LastDirZ;
            s.WasAirborneDuringKnockback = WasAirborneDuringKnockback;
            s.IsFastFalling = IsFastFalling;
            s.JumpFromSlide = JumpFromSlide;
            s.SlideAttackCarryActive = SlideAttackCarryActive;
            s.CrouchSettled = CrouchSettled;
            s.QueuedCrouchBrace = QueuedCrouchBrace;
            s.InPostHitstunFlight = InPostHitstunFlight;
            s.HitstopTicks = HitstopTicks;
            s.BurstCooldownTicks = BurstCooldownTicks;
            s.BurstRecoveryTicks = BurstRecoveryTicks;
            s.LedgeRegrabLockTicks = LedgeRegrabLockTicks;
            s.LandingLagTicks = LandingLagTicks;
            s.ShieldDropTicks = ShieldDropTicks;
            s.BlockStunTicks = BlockStunTicks;
            s.BlockHitstopKind = BlockHitstopKind;
            s.InteractionPhase = InteractionPhase;
            s.InteractionId = InteractionId;
            s.InteractionPartnerId = InteractionPartnerId;
            s.LastTerminalInteractionId = LastTerminalInteractionId;
            s.InteractionTick = InteractionTick;
            s.InteractionTerminalTick = InteractionTerminalTick;
            s.CapturedYaw = CapturedYaw;
            s.AirDodgeRecoveryTicks = AirDodgeRecoveryTicks;
            s.TargetEntityId = TargetEntityId;
            s.AttackPosePitch = AttackPosePitch;
            s.AttackCorrectionStartYaw = AttackCorrectionStartYaw;
            s.AttackCorrectionTargetId = AttackCorrectionTargetId;
            s.AttackCorrectionTargetDeaths = AttackCorrectionTargetDeaths;
            s.AttackCorrectionActive = AttackCorrectionActive;
            s.AttackCorrectionOwned = AttackCorrectionOwned;
            // AttackElapsedTicks and ChargeTicks are carried for remote presentation only;
            // the self track's live ability owns its own timers.
            s.KVX = KnockbackVelocityX;
            s.KVY = KnockbackVelocityY;
            s.KVZ = KnockbackVelocityZ;
        }
    }
}
