using System;

namespace SlopArena.Shared
{
    /// <summary>
    /// Field-complete World Snapshot codecs for the simulation's value types. Every field is
    /// written in declaration order; reflection tests fail when a field is added without
    /// being encoded here. Content references (definitions, baked animation) are not written:
    /// a hitbox may only reference its owner's content, re-linked on restore.
    /// </summary>
    public static class SnapshotCodecs
    {
        public static void Write(SnapshotWriter w, in CharacterState s)
        {
            w.WriteFloat(s.PX);
            w.WriteFloat(s.PY);
            w.WriteFloat(s.PZ);
            w.WriteFloat(s.VX);
            w.WriteFloat(s.VY);
            w.WriteFloat(s.VZ);
            w.WriteByte((byte)s.State);
            w.WriteUShort(s.StateTicks);
            w.WriteUShort(s.ShieldDropTicks);
            w.WriteUShort(s.BlockStunTicks);
            w.WriteByte(s.BlockHitstopKind);
            w.WriteUShort(s.AirDodgeRecoveryTicks);
            w.WriteByte(s.InteractionPhase);
            w.WriteULong(s.InteractionId);
            w.WriteULong(s.InteractionPartnerId);
            w.WriteUInt(s.InteractionTick);
            w.WriteULong(s.LastTerminalInteractionId);
            w.WriteUInt(s.InteractionTerminalTick);
            w.WriteShort(s.CapturedYaw);
            w.WriteByte((byte)s.MatchState);
            w.WriteUShort(s.DamagePercent);
            w.WriteByte(s.JumpsLeft);
            w.WriteByte(s.AirDodgesLeft);
            w.WriteUShort(s.AirTimeTicks);
            w.WriteByte(s.JumpHeldTicks);
            w.WriteBool(s.IsGrounded);
            w.WriteBool(s.WasAirborneDuringKnockback);
            w.WriteBool(s.InPostHitstunFlight);
            w.WriteBool(s.IsFastFalling);
            w.WriteByte((byte)s.JumpTakeoff);
            w.WriteBool(s.SlideAttackCarryActive);
            w.WriteByte((byte)s.AttackCarryFrom);
            w.WriteBool(s.CrouchSettled);
            w.WriteBool(s.QueuedCrouchBrace);
            w.WriteByte(s.Deaths);
            w.WriteFloat(s.DashDirX);
            w.WriteFloat(s.DashDirZ);
            w.WriteFloat(s.DashDirY);
            w.WriteULong(s.HomingDashTargetId);
            w.WriteUShort(s.HomingDashCooldownTicks);
            w.WriteBool(s.HomingDashAirUsed);
            w.WriteUShort(s.InvincibilityTicks);
            w.WriteUShort(s.AttackElapsedTicks);
            w.WriteByte(s.AttackSlot);
            w.WriteByte(s.BufferedSlot);
            w.WriteByte(s.ComboStage);
            w.WriteByte(s.AnimIndex);
            w.WriteByte(s.AttackSequence);
            w.WriteUShort(s.AnimLockTicks);
            w.WriteUShort(s.LandingLagTicks);
            w.WriteUShort(s.ChargeTicks);
            w.WriteByte(s.ChargeStockSpent);
            w.WriteUShort(s.ChargeStockRegenTicks);
            w.WriteUShort(s.ChargeStockRegenPeriod);
            w.WriteByte(s.FingerGunsShotsSpent);
            w.WriteFloat(s.KVX);
            w.WriteFloat(s.KVY);
            w.WriteFloat(s.KVZ);
            w.WriteUShort(s.HitstunTicks);
            w.WriteUShort(s.HitReactionSequence);
            w.WriteUShort(s.HitstopTicks);
            w.WriteFloat(s.QueuedKBDirX);
            w.WriteFloat(s.QueuedKBDirZ);
            w.WriteSByte(s.QueuedKBAngle);
            w.WriteFloat(s.QueuedKBBase);
            w.WriteFloat(s.QueuedKBGrowth);
            w.WriteFloat(s.QueuedKBDamage);
            w.WriteFloat(s.QueuedKBForce);
            w.WriteUShort(s.QueuedKBStun);
            w.WriteUShort(s.QueuedKBFixedHitstunTicks);
            w.WriteUShort(s.QueuedKBStunGate);
            w.WriteBool(s.QueuedArmorHitstop);
            w.WriteBool(s.QueuedKBResolvedForce);
            w.WriteBool(s.QueuedKBZero);
            w.WriteByte(s.HitstunLevel);
            w.WriteFloat(s.DIX);
            w.WriteFloat(s.DIY);
            w.WriteBool(s.SdiApplied);
            w.WriteFloat(s.FacingYaw);
            w.WriteFloat(s.AimYaw);
            w.WriteFloat(s.AimTargetDistance);
            w.WriteFloat(s.AimPitch);
            w.WriteFloat(s.AttackPosePitch);
            w.WriteFloat(s.AttackCorrectionStartYaw);
            w.WriteULong(s.AttackCorrectionTargetId);
            w.WriteByte(s.AttackCorrectionTargetDeaths);
            w.WriteBool(s.AttackCorrectionActive);
            w.WriteBool(s.AttackCorrectionOwned);
            w.WriteBool(s.IsAiming);
            w.WriteUShort(s.RushTicks);
            w.WriteUShort(s.LedgeRegrabLockTicks);
            w.WriteFloat(s.LastDirX);
            w.WriteFloat(s.LastDirZ);
            w.WriteULong(s.EntityId);
            w.WriteULong(s.TargetEntityId);
            w.WriteBool(s.LockOn);
            w.WriteBool(s.AutoLockSuppressed);
            w.WriteUShort(s.Cooldown0);
            w.WriteUShort(s.Cooldown1);
            w.WriteUShort(s.Cooldown2);
            w.WriteUShort(s.Cooldown3);
            w.WriteUShort(s.Cooldown4);
            w.WriteUShort(s.Cooldown5);
            w.WriteUShort(s.Cooldown6);
            w.WriteUShort(s.Cooldown7);
            w.WriteUShort(s.Cooldown8);
            w.WriteUShort(s.Cooldown9);
            w.WriteUShort(s.Cooldown10);
            w.WriteByte(s.ReturningProjectileSlot);
        }

        private static byte ReadEnumByte(SnapshotReader r, byte max, string what)
        {
            byte value = r.ReadByte();
            if (value > max) throw SnapshotReader.Malformed(what);
            return value;
        }

        public static CharacterState ReadCharacterState(SnapshotReader r)
        {
            var s = new CharacterState();
            s.PX = r.ReadFloat();
            s.PY = r.ReadFloat();
            s.PZ = r.ReadFloat();
            s.VX = r.ReadFloat();
            s.VY = r.ReadFloat();
            s.VZ = r.ReadFloat();
            s.State = (ActionState)r.ReadByte();
            s.StateTicks = r.ReadUShort();
            s.ShieldDropTicks = r.ReadUShort();
            s.BlockStunTicks = r.ReadUShort();
            s.BlockHitstopKind = r.ReadByte();
            s.AirDodgeRecoveryTicks = r.ReadUShort();
            s.InteractionPhase = r.ReadByte();
            s.InteractionId = r.ReadULong();
            s.InteractionPartnerId = r.ReadULong();
            s.InteractionTick = r.ReadUInt();
            s.LastTerminalInteractionId = r.ReadULong();
            s.InteractionTerminalTick = r.ReadUInt();
            s.CapturedYaw = r.ReadShort();
            s.MatchState = (MatchState)r.ReadByte();
            s.DamagePercent = r.ReadUShort();
            s.JumpsLeft = r.ReadByte();
            s.AirDodgesLeft = r.ReadByte();
            s.AirTimeTicks = r.ReadUShort();
            s.JumpHeldTicks = r.ReadByte();
            s.IsGrounded = r.ReadBool();
            s.WasAirborneDuringKnockback = r.ReadBool();
            s.InPostHitstunFlight = r.ReadBool();
            s.IsFastFalling = r.ReadBool();
            s.JumpTakeoff = (JumpTakeoff)ReadEnumByte(r, (byte)JumpTakeoff.Shell, "jump takeoff");
            s.SlideAttackCarryActive = r.ReadBool();
            s.AttackCarryFrom = (AttackCarryOrigin)ReadEnumByte(r, (byte)AttackCarryOrigin.Shell, "attack carry origin");
            s.CrouchSettled = r.ReadBool();
            s.QueuedCrouchBrace = r.ReadBool();
            s.Deaths = r.ReadByte();
            s.DashDirX = r.ReadFloat();
            s.DashDirZ = r.ReadFloat();
            s.DashDirY = r.ReadFloat();
            s.HomingDashTargetId = r.ReadULong();
            s.HomingDashCooldownTicks = r.ReadUShort();
            s.HomingDashAirUsed = r.ReadBool();
            s.InvincibilityTicks = r.ReadUShort();
            s.AttackElapsedTicks = r.ReadUShort();
            s.AttackSlot = r.ReadByte();
            s.BufferedSlot = r.ReadByte();
            s.ComboStage = r.ReadByte();
            s.AnimIndex = r.ReadByte();
            s.AttackSequence = r.ReadByte();
            s.AnimLockTicks = r.ReadUShort();
            s.LandingLagTicks = r.ReadUShort();
            s.ChargeTicks = r.ReadUShort();
            s.ChargeStockSpent = r.ReadByte();
            s.ChargeStockRegenTicks = r.ReadUShort();
            s.ChargeStockRegenPeriod = r.ReadUShort();
            s.FingerGunsShotsSpent = r.ReadByte();
            s.KVX = r.ReadFloat();
            s.KVY = r.ReadFloat();
            s.KVZ = r.ReadFloat();
            s.HitstunTicks = r.ReadUShort();
            s.HitReactionSequence = r.ReadUShort();
            s.HitstopTicks = r.ReadUShort();
            s.QueuedKBDirX = r.ReadFloat();
            s.QueuedKBDirZ = r.ReadFloat();
            s.QueuedKBAngle = r.ReadSByte();
            s.QueuedKBBase = r.ReadFloat();
            s.QueuedKBGrowth = r.ReadFloat();
            s.QueuedKBDamage = r.ReadFloat();
            s.QueuedKBForce = r.ReadFloat();
            s.QueuedKBStun = r.ReadUShort();
            s.QueuedKBFixedHitstunTicks = r.ReadUShort();
            s.QueuedKBStunGate = r.ReadUShort();
            s.QueuedArmorHitstop = r.ReadBool();
            s.QueuedKBResolvedForce = r.ReadBool();
            s.QueuedKBZero = r.ReadBool();
            s.HitstunLevel = r.ReadByte();
            s.DIX = r.ReadFloat();
            s.DIY = r.ReadFloat();
            s.SdiApplied = r.ReadBool();
            s.FacingYaw = r.ReadFloat();
            s.AimYaw = r.ReadFloat();
            s.AimTargetDistance = r.ReadFloat();
            s.AimPitch = r.ReadFloat();
            s.AttackPosePitch = r.ReadFloat();
            s.AttackCorrectionStartYaw = r.ReadFloat();
            s.AttackCorrectionTargetId = r.ReadULong();
            s.AttackCorrectionTargetDeaths = r.ReadByte();
            s.AttackCorrectionActive = r.ReadBool();
            s.AttackCorrectionOwned = r.ReadBool();
            s.IsAiming = r.ReadBool();
            s.RushTicks = r.ReadUShort();
            s.LedgeRegrabLockTicks = r.ReadUShort();
            s.LastDirX = r.ReadFloat();
            s.LastDirZ = r.ReadFloat();
            s.EntityId = r.ReadULong();
            s.TargetEntityId = r.ReadULong();
            s.LockOn = r.ReadBool();
            s.AutoLockSuppressed = r.ReadBool();
            s.Cooldown0 = r.ReadUShort();
            s.Cooldown1 = r.ReadUShort();
            s.Cooldown2 = r.ReadUShort();
            s.Cooldown3 = r.ReadUShort();
            s.Cooldown4 = r.ReadUShort();
            s.Cooldown5 = r.ReadUShort();
            s.Cooldown6 = r.ReadUShort();
            s.Cooldown7 = r.ReadUShort();
            s.Cooldown8 = r.ReadUShort();
            s.Cooldown9 = r.ReadUShort();
            s.Cooldown10 = r.ReadUShort();
            s.ReturningProjectileSlot = r.ReadByte();
            return s;
        }

        public static void Write(SnapshotWriter w, in InputState input)
        {
            w.WriteBool(input.Up);
            w.WriteBool(input.Down);
            w.WriteBool(input.DownPressed);
            w.WriteBool(input.Left);
            w.WriteBool(input.Right);
            w.WriteBool(input.Jump);
            w.WriteBool(input.ShieldHeld);
            w.WriteBool(input.ShieldPressed);
            w.WriteBool(input.GrabPressed);
            w.WriteBool(input.DashPressed);
            w.WriteBool(input.JumpHeld);
            w.WriteBool(input.FaceToCamera);
            w.WriteBool(input.ToggleLock);
            w.WriteBool(input.RetargetPressed);
            w.WriteByte((byte)input.LockMode);
            w.WriteFloat(input.MoveX);
            w.WriteFloat(input.MoveY);
            w.WriteByte(input.ActiveSlot);
            w.WriteBool(input.IsAiming);
            w.WriteShort(input.FacingYaw);
            w.WriteShort(input.AimYaw);
            w.WriteUShort(input.AimDistance);
            w.WriteShort(input.AimPitch);
            w.WriteByte(input.TargetEntityId);
        }

        public static InputState ReadInputState(SnapshotReader r)
        {
            var input = new InputState();
            input.Up = r.ReadBool();
            input.Down = r.ReadBool();
            input.DownPressed = r.ReadBool();
            input.Left = r.ReadBool();
            input.Right = r.ReadBool();
            input.Jump = r.ReadBool();
            input.ShieldHeld = r.ReadBool();
            input.ShieldPressed = r.ReadBool();
            input.GrabPressed = r.ReadBool();
            input.DashPressed = r.ReadBool();
            input.JumpHeld = r.ReadBool();
            input.FaceToCamera = r.ReadBool();
            input.ToggleLock = r.ReadBool();
            input.RetargetPressed = r.ReadBool();
            input.LockMode = (TargetLockMode)r.ReadByte();
            input.MoveX = r.ReadFloat();
            input.MoveY = r.ReadFloat();
            input.ActiveSlot = r.ReadByte();
            input.IsAiming = r.ReadBool();
            input.FacingYaw = r.ReadShort();
            input.AimYaw = r.ReadShort();
            input.AimDistance = r.ReadUShort();
            input.AimPitch = r.ReadShort();
            input.TargetEntityId = r.ReadByte();
            return input;
        }

        public static void Write(SnapshotWriter w, in KnockbackData knockback)
        {
            w.WriteByte((byte)knockback.Profile);
            w.WriteSByte(knockback.Angle);
            w.WriteFloat(knockback.BaseKnockback);
            w.WriteFloat(knockback.KnockbackGrowth);
        }

        public static KnockbackData ReadKnockbackData(SnapshotReader r)
        {
            var knockback = new KnockbackData();
            knockback.Profile = (KnockbackProfile)r.ReadByte();
            knockback.Angle = r.ReadSByte();
            knockback.BaseKnockback = r.ReadFloat();
            knockback.KnockbackGrowth = r.ReadFloat();
            return knockback;
        }

        public static void Write(SnapshotWriter w, in HitboxEvent evt)
        {
            w.WriteUShort(evt.TriggerTick);
            w.WriteUShort(evt.DurationTicks);
            w.WriteByte((byte)evt.Shape);
            w.WriteFloat(evt.Radius);
            w.WriteFloat(evt.OffX);
            w.WriteFloat(evt.OffY);
            w.WriteFloat(evt.OffZ);
            w.WriteFloat(evt.EndOffX);
            w.WriteFloat(evt.EndOffY);
            w.WriteFloat(evt.EndOffZ);
            w.WriteString(evt.BoneName);
            w.WriteString(evt.EndBoneName);
            w.WriteFloat(evt.Damage);
            Write(w, evt.Knockback);
            w.WriteUShort(evt.StunTicks);
            w.WriteUShort(evt.FixedHitstunTicks);
            w.WriteBool(evt.Interruptible);
            w.WriteByte(evt.HitGroup);
            w.WriteByte((byte)evt.KnockbackDirection);
        }

        public static HitboxEvent ReadHitboxEvent(SnapshotReader r)
        {
            var evt = new HitboxEvent();
            evt.TriggerTick = r.ReadUShort();
            evt.DurationTicks = r.ReadUShort();
            evt.Shape = (HitboxShape)r.ReadByte();
            evt.Radius = r.ReadFloat();
            evt.OffX = r.ReadFloat();
            evt.OffY = r.ReadFloat();
            evt.OffZ = r.ReadFloat();
            evt.EndOffX = r.ReadFloat();
            evt.EndOffY = r.ReadFloat();
            evt.EndOffZ = r.ReadFloat();
            evt.BoneName = r.ReadString();
            evt.EndBoneName = r.ReadString();
            evt.Damage = r.ReadFloat();
            evt.Knockback = ReadKnockbackData(r);
            evt.StunTicks = r.ReadUShort();
            evt.FixedHitstunTicks = r.ReadUShort();
            evt.Interruptible = r.ReadBool();
            evt.HitGroup = r.ReadByte();
            evt.KnockbackDirection = (AuthoringKnockbackDirection)r.ReadByte();
            return evt;
        }

        public static void Write(SnapshotWriter w, in ProjectileExplosion explosion)
        {
            w.WriteFloat(explosion.Radius);
            w.WriteFloat(explosion.Damage);
            Write(w, explosion.Knockback);
            w.WriteUShort(explosion.StunTicks);
            w.WriteUShort(explosion.DurationTicks);
            w.WriteBool(explosion.CanHitOwner);
            w.WriteBool(explosion.OwnerDamage.HasValue);
            if (explosion.OwnerDamage.HasValue) w.WriteFloat(explosion.OwnerDamage.Value);
            w.WriteString(explosion.ExplosionPresentationId);
            w.WriteByte(explosion.PresentationAttackSequence);
            w.WriteInt(explosion.PresentationOperationIndex);
            w.WriteFloat(explosion.PresentationYaw);
        }

        public static ProjectileExplosion ReadProjectileExplosion(SnapshotReader r)
        {
            var explosion = new ProjectileExplosion();
            explosion.Radius = r.ReadFloat();
            explosion.Damage = r.ReadFloat();
            explosion.Knockback = ReadKnockbackData(r);
            explosion.StunTicks = r.ReadUShort();
            explosion.DurationTicks = r.ReadUShort();
            explosion.CanHitOwner = r.ReadBool();
            if (r.ReadBool()) explosion.OwnerDamage = r.ReadFloat();
            explosion.ExplosionPresentationId = r.ReadString()!;
            explosion.PresentationAttackSequence = r.ReadByte();
            explosion.PresentationOperationIndex = r.ReadInt();
            explosion.PresentationYaw = r.ReadFloat();
            return explosion;
        }

        public static void Write(SnapshotWriter w, in ProjectileReturn projectileReturn)
        {
            w.WriteUShort(projectileReturn.OutboundTicks);
            w.WriteFloat(projectileReturn.ReturnSpeed);
            w.WriteFloat(projectileReturn.ReturnAcceleration);
            w.WriteFloat(projectileReturn.SelfHitDamage);
            w.WriteUShort(projectileReturn.SelfHitStunTicks);
            w.WriteString(projectileReturn.CatchPresentationId);
            w.WriteString(projectileReturn.SelfHitPresentationId);
        }

        public static ProjectileReturn ReadProjectileReturn(SnapshotReader r)
        {
            var projectileReturn = new ProjectileReturn();
            projectileReturn.OutboundTicks = r.ReadUShort();
            projectileReturn.ReturnSpeed = r.ReadFloat();
            projectileReturn.ReturnAcceleration = r.ReadFloat();
            projectileReturn.SelfHitDamage = r.ReadFloat();
            projectileReturn.SelfHitStunTicks = r.ReadUShort();
            projectileReturn.CatchPresentationId = r.ReadString()!;
            projectileReturn.SelfHitPresentationId = r.ReadString()!;
            return projectileReturn;
        }

        public static void Write(SnapshotWriter w, in HitboxPose pose)
        {
            w.WriteFloat(pose.X);
            w.WriteFloat(pose.Y);
            w.WriteFloat(pose.Z);
            w.WriteFloat(pose.EndX);
            w.WriteFloat(pose.EndY);
            w.WriteFloat(pose.EndZ);
        }

        public static HitboxPose ReadHitboxPose(SnapshotReader r)
        {
            var pose = new HitboxPose();
            pose.X = r.ReadFloat();
            pose.Y = r.ReadFloat();
            pose.Z = r.ReadFloat();
            pose.EndX = r.ReadFloat();
            pose.EndY = r.ReadFloat();
            pose.EndZ = r.ReadFloat();
            return pose;
        }

        public static void Write(SnapshotWriter w, in HitboxSweepOrigin origin)
        {
            w.WriteFloat(origin.PX);
            w.WriteFloat(origin.PY);
            w.WriteFloat(origin.PZ);
            w.WriteFloat(origin.FacingYaw);
            w.WriteFloat(origin.AttackPosePitch);
            w.WriteUShort(origin.AttackElapsedTicks);
            w.WriteByte(origin.ComboStage);
            Write(w, origin.Pose);
        }

        public static HitboxSweepOrigin ReadHitboxSweepOrigin(SnapshotReader r)
        {
            var origin = new HitboxSweepOrigin();
            origin.PX = r.ReadFloat();
            origin.PY = r.ReadFloat();
            origin.PZ = r.ReadFloat();
            origin.FacingYaw = r.ReadFloat();
            origin.AttackPosePitch = r.ReadFloat();
            origin.AttackElapsedTicks = r.ReadUShort();
            origin.ComboStage = r.ReadByte();
            origin.Pose = ReadHitboxPose(r);
            return origin;
        }

        /// <summary>
        /// Write a hitbox. <paramref name="ownerContent"/> returns the owner's registered
        /// definition and baked data; a hitbox referencing any other content cannot be
        /// re-linked and is rejected.
        /// </summary>
        public static void Write(SnapshotWriter w, in Hitbox hb,
            Func<ulong, (CharacterDefinition? Def, BakedAnimationData? Baked)> ownerContent)
        {
            w.WriteULong(hb.EmissionId);
            w.WriteFloat(hb.X);
            w.WriteFloat(hb.Y);
            w.WriteFloat(hb.Z);
            w.WriteFloat(hb.VX);
            w.WriteFloat(hb.VY);
            w.WriteFloat(hb.VZ);
            w.WriteFloat(hb.Radius);
            w.WriteUShort(hb.DurationTicks);
            w.WriteUShort(hb.AgeTicks);
            w.WriteByte((byte)hb.Shape);
            w.WriteFloat(hb.EndX);
            w.WriteFloat(hb.EndY);
            w.WriteFloat(hb.EndZ);
            w.WriteFloat(hb.PreviousRadius); w.WriteFloat(hb.RingSpeed);
            w.WriteFloat(hb.RingHalfWidth); w.WriteFloat(hb.RingHalfHeight); w.WriteBool(hb.Unblockable);
            w.WriteFloat(hb.Damage);
            w.WriteFloat(hb.BaseKnockback);
            w.WriteFloat(hb.KnockbackGrowth);
            w.WriteSByte(hb.KnockbackAngle);
            w.WriteByte((byte)hb.KnockbackDirection);
            w.WriteUShort(hb.StunTicks);
            w.WriteUShort(hb.FixedHitstunTicks);
            w.WriteULong(hb.OwnerId);
            w.WriteULong(hb.ActivationId);
            w.WriteBool(hb.ActivationAirborne);
            w.WriteByte(hb.AttackSequence);
            w.WriteByte(hb.AttackSlot);
            w.WriteInt(hb.VisualOperationIndex);
            w.WriteBool(hb.Active);
            w.WriteBool(hb.CancelWithAbility);
            w.WriteFloat(hb.Gravity);
            w.WriteUShort(hb.StickyFuseTicks); w.WriteULong(hb.AttachedEntityId); w.WriteUShort(hb.FuseTicksLeft);
            w.WriteULong(hb.HomingTargetEntityId);
            w.WriteFloat(hb.HomingTurnCos);
            w.WriteFloat(hb.HomingTurnSin);
            w.WriteBool(hb.OwnerDamage.HasValue);
            if (hb.OwnerDamage.HasValue) w.WriteFloat(hb.OwnerDamage.Value);
            w.WriteBool(hb.Explosion.HasValue);
            if (hb.Explosion.HasValue)
                Write(w, hb.Explosion.Value);
            w.WriteBool(hb.Return.HasValue);
            if (hb.Return.HasValue)
                Write(w, hb.Return.Value);
            w.WriteBool(hb.ReturnLeg);
            w.WriteUShort(hb.LegTicks);
            w.WriteBool(hb.CanHitOwner);
            w.WriteBool(hb.FreezesOwner);
            w.WriteBool(hb.OwnerWindow);
            w.WriteBool(hb.HitsMultipleOpponents);
            w.WriteSet(hb.HitEntities);
            w.WriteBool(hb.TracksBone);
            Write(w, hb.SourceEvent);
            var owner = hb.Def != null || hb.Baked != null ? ownerContent(hb.OwnerId) : default;
            if (hb.Def != null && !ReferenceEquals(hb.Def, owner.Def))
                throw new InvalidOperationException($"Hitbox of entity {hb.OwnerId} references another fighter's definition.");
            if (hb.Baked != null && !ReferenceEquals(hb.Baked, owner.Baked))
                throw new InvalidOperationException($"Hitbox of entity {hb.OwnerId} references another fighter's baked animation.");
            w.WriteBool(hb.Baked != null);
            w.WriteBool(hb.Def != null);
            w.WriteByte(hb.AnimIndex);
            w.WriteByte(hb.Slot);
            w.WriteBool(hb.Airborne);
            w.WriteBool(hb.HasSweepOrigin);
            Write(w, hb.SweepOrigin);
            w.WriteInt(hb.Sweep?.Length ?? -1);
            if (hb.Sweep != null)
                foreach (var pose in hb.Sweep)
                    Write(w, pose);
        }

        public static Hitbox ReadHitbox(SnapshotReader r,
            Func<ulong, (CharacterDefinition? Def, BakedAnimationData? Baked)> ownerContent)
        {
            var hb = new Hitbox();
            hb.EmissionId = r.ReadULong();
            hb.X = r.ReadFloat();
            hb.Y = r.ReadFloat();
            hb.Z = r.ReadFloat();
            hb.VX = r.ReadFloat();
            hb.VY = r.ReadFloat();
            hb.VZ = r.ReadFloat();
            hb.Radius = r.ReadFloat();
            hb.DurationTicks = r.ReadUShort();
            hb.AgeTicks = r.ReadUShort();
            hb.Shape = (HitboxShape)r.ReadByte();
            hb.EndX = r.ReadFloat();
            hb.EndY = r.ReadFloat();
            hb.EndZ = r.ReadFloat();
            hb.PreviousRadius = r.ReadFloat(); hb.RingSpeed = r.ReadFloat();
            hb.RingHalfWidth = r.ReadFloat(); hb.RingHalfHeight = r.ReadFloat(); hb.Unblockable = r.ReadBool();
            if (hb.Shape > HitboxShape.Ring || hb.Shape == HitboxShape.Ring &&
                (!float.IsFinite(hb.Radius) || !float.IsFinite(hb.PreviousRadius) || !float.IsFinite(hb.RingSpeed)
                 || !float.IsFinite(hb.RingHalfWidth) || !float.IsFinite(hb.RingHalfHeight)
                 || hb.PreviousRadius < 0 || hb.Radius < hb.PreviousRadius || hb.RingSpeed < 0
                 || hb.RingHalfWidth <= 0 || hb.RingHalfHeight <= 0))
                throw SnapshotReader.Malformed("expanding ring");
            hb.Damage = r.ReadFloat();
            hb.BaseKnockback = r.ReadFloat();
            hb.KnockbackGrowth = r.ReadFloat();
            hb.KnockbackAngle = r.ReadSByte();
            hb.KnockbackDirection = (AuthoringKnockbackDirection)r.ReadByte();
            hb.StunTicks = r.ReadUShort();
            hb.FixedHitstunTicks = r.ReadUShort();
            hb.OwnerId = r.ReadULong();
            hb.ActivationId = r.ReadULong();
            hb.ActivationAirborne = r.ReadBool();
            hb.AttackSequence = r.ReadByte();
            hb.AttackSlot = r.ReadByte();
            hb.VisualOperationIndex = r.ReadInt();
            hb.Active = r.ReadBool();
            hb.CancelWithAbility = r.ReadBool();
            hb.Gravity = r.ReadFloat();
            hb.StickyFuseTicks = r.ReadUShort(); hb.AttachedEntityId = r.ReadULong(); hb.FuseTicksLeft = r.ReadUShort();
            hb.HomingTargetEntityId = r.ReadULong();
            hb.HomingTurnCos = r.ReadFloat();
            hb.HomingTurnSin = r.ReadFloat();
            if (r.ReadBool()) hb.OwnerDamage = r.ReadFloat();
            if (r.ReadBool())
                hb.Explosion = ReadProjectileExplosion(r);
            if (r.ReadBool())
                hb.Return = ReadProjectileReturn(r);
            hb.ReturnLeg = r.ReadBool();
            hb.LegTicks = r.ReadUShort();
            hb.CanHitOwner = r.ReadBool();
            hb.FreezesOwner = r.ReadBool();
            hb.OwnerWindow = r.ReadBool();
            hb.HitsMultipleOpponents = r.ReadBool();
            hb.HitEntities = r.ReadSet();
            hb.TracksBone = r.ReadBool();
            hb.SourceEvent = ReadHitboxEvent(r);
            bool hasBaked = r.ReadBool();
            bool hasDef = r.ReadBool();
            if (hasBaked || hasDef)
            {
                var owner = ownerContent(hb.OwnerId);
                if ((hasDef && owner.Def == null) || (hasBaked && owner.Baked == null))
                    throw SnapshotReader.Malformed($"hitbox owner {hb.OwnerId} content");
                hb.Def = hasDef ? owner.Def : null;
                hb.Baked = hasBaked ? owner.Baked : null;
            }
            hb.AnimIndex = r.ReadByte();
            hb.Slot = r.ReadByte();
            hb.Airborne = r.ReadBool();
            hb.HasSweepOrigin = r.ReadBool();
            hb.SweepOrigin = ReadHitboxSweepOrigin(r);
            int poses = r.ReadOptionalCount(24);
            if (poses >= 0)
            {
                hb.Sweep = new HitboxPose[poses];
                for (int i = 0; i < poses; i++)
                    hb.Sweep[i] = ReadHitboxPose(r);
            }
            return hb;
        }
    }
}
