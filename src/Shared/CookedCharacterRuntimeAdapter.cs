using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SlopArena.Shared;

public static class CookedCharacterRuntimeAdapter
{
    public static CharacterDefinition ToCharacterDefinition(CookedCharacterPackage package, CharacterClass legacySelector = CharacterClass.None)
    {
        if (package == null) throw new InvalidDataException("Cooked package is not valid.");
        var c = package.Definition;
        var result = new CharacterDefinition
        {
            Class = legacySelector,
            DisplayName = c.DisplayName,
            Weight = c.Weight,
            Movement = new MovementStats { RunSpeed=c.Movement.RunSpeed, RunAccelerationA=c.Movement.RunAccelerationA, RunAccelerationB=c.Movement.RunAccelerationB, DashSpeed=c.Movement.DashSpeed, AirDodgeSpeed=c.Movement.AirDodgeSpeed, AirSpeedMax=c.Movement.AirSpeedMax, AirAccelStick=c.Movement.AirAccelStick, AirAccelBase=c.Movement.AirAccelBase, JumpForce=c.Movement.JumpForce, ShortHopForce=c.Movement.ShortHopForce, AirJumpVMultiplier=c.Movement.AirJumpVMultiplier, AirJumpHMultiplier=c.Movement.AirJumpHMultiplier, Gravity=c.Movement.Gravity, AirFloatGravity=c.Movement.AirFloatGravity, DashDurationTicks=c.Movement.DashDurationTicks, DashCooldownTicks=c.Movement.DashCooldownTicks, GroundFriction=c.Movement.GroundFriction, AirFriction=c.Movement.AirFriction, MaxFallSpeed=c.Movement.MaxFallSpeed, FastFallSpeed=c.Movement.FastFallSpeed, MaxJumps=c.Movement.MaxJumps, JumpSquatTicks=c.Movement.JumpSquatTicks, FloatWindowTicks=c.Movement.FloatWindowTicks, RushTicks=c.Movement.RushTicks },
            CapsuleRadius=c.CapsuleRadius, CapsuleHeight=c.CapsuleHeight, HipHeight=c.HipHeight, HurtboxRadius=c.HurtboxRadius, ShieldRadius=c.ShieldRadius,
            CaptureGeometry = c.CaptureGeometry,
            HurtboxCapsules=c.HurtboxCapsules.Select(x=>new HurtboxCapsule(x.StartX,x.StartY,x.StartZ,x.EndX,x.EndY,x.EndZ,x.Radius)).ToArray(),
            HurtboxBoneDefs=c.HurtboxBoneDefs.Select(x=>new HurtboxBoneDef(RuntimeBoneId(x.BoneId)!,x.OffsetX,x.OffsetY,x.OffsetZ,x.Radius)).ToArray(),
            ModelResourcePath=c.Presentation.ModelResourcePath, VisualScale=c.Presentation.VisualScale, HurtboxBoneScale=c.Presentation.HurtboxBoneScale, ModelYOffset=c.Presentation.ModelYOffset, ModelSoleOffset=c.Presentation.ModelSoleOffset, AutoModelYOffset=c.Presentation.AutoModelYOffset,
            BakedDataPath="",
            IdleAnim=c.Presentation.Idle, RunAnim=c.Presentation.Run, DashAnim=c.Presentation.Dash, JumpAnim=c.Presentation.Jump, FallAnim=c.Presentation.Fall,
            CrouchAnim=c.Presentation.Crouch, SlideAnim=c.Presentation.Slide,
            ShieldAnim = c.Presentation.Shield, GrabAnim = c.Presentation.Grab,
            GrabbedAnim = c.Presentation.Grabbed, ThrowForwardAnim = c.Presentation.ThrowForward,
            AirDodgeAnim = c.Presentation.AirDodge,
            HitSmallAnim=c.Presentation.HitSmall, HitMediumAnim=c.Presentation.HitMedium, HitHardAnim=c.Presentation.HitHard, TumbleAnim=c.Presentation.Tumble, LandStartOffset=c.Presentation.LandStartOffsetSeconds,
            CookedSlots = c.Slots,
        };
        AddLegacyAbilityViews(result, c.Slots);
        return result;
    }

    private static void AddLegacyAbilityViews(CharacterDefinition def, IReadOnlyList<CookedSlotDefinition> slots)
    {
        foreach (var slot in slots)
        {
            var ability = new AbilitySpec { Name = slot.Name, Description = slot.Description, IconName = slot.IconId, CooldownTicks = slot.CooldownTicks, IsRecoveryMove = slot.IsRecoveryMove, PreserveMomentumOnStart = slot.PreserveMomentumOnStart, Behavior = (AbilityBehavior)slot.Behavior, AimMode = (AimMode)slot.AimMode, AimMovement = slot.AimMovement == AuthoringAimMovementMode.Mobile ? AimMovementMode.Mobile : AimMovementMode.Fixed, AimAnimationId = slot.AimAnimationId, HitPresentationId = slot.HitPresentationId, AnimationNames = slot.Timeline.Stages.SelectMany(x => x.AnimationIds).ToArray(), Stages = slot.Timeline.Stages.Select(ToAttackStage).ToArray() };
            if (!slot.IsAir) SetGroundAbility(def, slot.Ordinal, ability); else SetAirAbility(def, slot.Ordinal - 8, ability);
        }
    }

    private static void SetGroundAbility(CharacterDefinition d, int ordinal, AbilitySpec a) { switch (ordinal) { case 0: d.Slot1=a; break; case 1: d.Slot2=a; break; case 2: d.Slot3=a; break; case 3: d.Slot4=a; break; case 4: d.A=a; break; case 5: d.E=a; break; case 6: d.R=a; break; case 7: d.F=a; break; } }
    private static void SetAirAbility(CharacterDefinition d, int ordinal, AbilitySpec a) { switch (ordinal) { case 0: d.AirSlot1=a; break; case 1: d.AirSlot2=a; break; case 2: d.AirSlot3=a; break; case 3: d.AirSlot4=a; break; case 4: d.AirA=a; break; case 5: d.AirE=a; break; case 6: d.AirR=a; break; case 7: d.AirF=a; break; } }
    private static AttackStage ToAttackStage(CookedStage stage)
    {
        var result = new AttackStage
        {
            DurationTicks = stage.DurationTicks,
            IasaTicks = stage.IasaTicks,
            LandingLagTicks = stage.LandingLagTicks,
            AutoCancelBeforeTicks = stage.AutoCancelBeforeTicks,
            AutoCancelAfterTicks = stage.AutoCancelAfterTicks,
        };
        var events = new List<HitboxEvent>();
        foreach (var operation in stage.Operations)
        {
            if (operation is CookedSpawnHitboxOperation hit)
                events.Add(ToHitboxEvent(hit.Hitbox, hit.Tick));
            else if (operation is CookedSetVelocityOperation velocity)
            {
                result.MoveX = velocity.X;
                result.MoveY = velocity.Y;
                result.MoveZ = velocity.Z;
            }
        }
        result.HitboxEvents = events.ToArray();
        return result;
    }

    internal static HitboxEvent ToHitboxEvent(CookedHitbox hitbox, int triggerTick = 0)
        => new()
        {
            TriggerTick = (ushort)Math.Clamp(triggerTick, 0, ushort.MaxValue),
            DurationTicks = hitbox.DurationTicks,
            Shape = (HitboxShape)hitbox.Shape,
            Radius = hitbox.Radius,
            OffX = hitbox.OffsetX, OffY = hitbox.OffsetY, OffZ = hitbox.OffsetZ,
            EndOffX = hitbox.EndOffsetX, EndOffY = hitbox.EndOffsetY, EndOffZ = hitbox.EndOffsetZ,
            BoneName = RuntimeBoneId(hitbox.StartBoneId),
            EndBoneName = RuntimeBoneId(hitbox.EndBoneId),
            Damage = hitbox.Damage,
            Knockback = new KnockbackData
            {
                Profile = KnockbackProfile.Custom,
                Angle = (sbyte)hitbox.Angle,
                BaseKnockback = hitbox.BaseKnockback,
                KnockbackGrowth = hitbox.KnockbackGrowth,
            },
            KnockbackDirection = hitbox.KnockbackDirection,
            StunTicks = hitbox.StunTicks,
            FixedHitstunTicks = hitbox.FixedHitstunTicks,
            Interruptible = hitbox.Interruptible,
            HitGroup = hitbox.HitGroup,
        };

    internal static string? RuntimeBoneId(string? value)
        => value switch
        {
            "bone.head" => "mixamorig:Head",
            "bone.hips" => "mixamorig:Hips",
            "bone.spine" => "mixamorig:Spine2",
            "bone.right-hand" => "mixamorig:RightHand",
            "bone.left-hand" => "mixamorig:LeftHand",
            "bone.right-foot" => "mixamorig:RightFoot",
            "bone.left-foot" => "mixamorig:LeftFoot",
            null => null,
            _ => value,
        };
}
