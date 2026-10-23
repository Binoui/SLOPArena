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
        return result;
    }

    /// <summary>Read authored melee operations as independent geometry values for previews and reports.</summary>
    public static HitboxEvent[] ReadHitboxes(this CookedStage stage)
        => stage.Operations.OfType<CookedSpawnHitboxOperation>()
            .Select(hit => ToHitboxEvent(hit.Hitbox, hit.Tick)).ToArray();

    public static HitboxEvent ToHitboxEvent(CookedHitbox hitbox, int triggerTick = 0)
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
            "bone.spine2" => "mixamorig:Spine2",
            "bone.right-hand" => "mixamorig:RightHand",
            "bone.left-hand" => "mixamorig:LeftHand",
            "bone.right-foot" => "mixamorig:RightFoot",
            "bone.left-foot" => "mixamorig:LeftFoot",
            null => null,
            _ => value,
        };
}
