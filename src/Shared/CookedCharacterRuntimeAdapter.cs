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
            Movement = ToMovementStats(c.Movement),
            CapsuleRadius=c.CapsuleRadius, CapsuleHeight=c.CapsuleHeight, HipHeight=c.HipHeight, HurtboxRadius=c.HurtboxRadius, ShieldRadius=c.ShieldRadius,
            CaptureGeometry = c.CaptureGeometry,
            HurtboxCapsules=c.HurtboxCapsules.Select(x=>new HurtboxCapsule(x.StartX,x.StartY,x.StartZ,x.EndX,x.EndY,x.EndZ,x.Radius)).ToArray(),
            HurtboxBoneDefs=c.HurtboxBoneDefs.Select(x=>new HurtboxBoneDef(RuntimeBoneId(x.BoneId)!,x.OffsetX,x.OffsetY,x.OffsetZ,x.Radius,RuntimeBoneId(x.EndBoneId),x.EndOffsetX,x.EndOffsetY,x.EndOffsetZ)).ToArray(),
            ModelResourcePath=c.Presentation.ModelResourcePath, VisualScale=c.Presentation.VisualScale, HurtboxBoneScale=c.Presentation.HurtboxBoneScale, ModelYOffset=c.Presentation.ModelYOffset, ModelSoleOffset=c.Presentation.ModelSoleOffset, AutoModelYOffset=c.Presentation.AutoModelYOffset,
            BakedDataPath="",
            IdleAnim=c.Presentation.Idle, RunAnim=c.Presentation.Run, DashAnim=c.Presentation.Dash, JumpAnim=c.Presentation.Jump, FallAnim=c.Presentation.Fall,
            CrouchAnim=c.Presentation.Crouch, SlideAnim=c.Presentation.Slide,
            ShieldAnim = c.Presentation.Shield, GrabAnim = c.Presentation.Grab,
            GrabbedAnim = c.Presentation.Grabbed, ThrowForwardAnim = c.Presentation.ThrowForward,
            AirDodgeAnim = c.Presentation.AirDodge,
            ShellAnim = c.Presentation.Shell,
            FlyingAnim = c.Presentation.Flying,
            MayhemLaughAnim = c.Presentation.MayhemLaugh,
            Shell = c.Shell,
            HitSmallAnim=c.Presentation.HitSmall, HitMediumAnim=c.Presentation.HitMedium, HitHardAnim=c.Presentation.HitHard, TumbleAnim=c.Presentation.Tumble, LandStartOffset=c.Presentation.LandStartOffsetSeconds,
            CookedSlots = c.Slots,
        };
        if (c.AlternateForm is not { } form) return result;
        var p = form.Presentation;
        // An empty role plays form 0's clip: one rig carries both bodies (ADR-0051).
        string Or(string role, string real) => string.IsNullOrEmpty(role) ? real : role;
        var alternate = result with
        {
            DisplayName = form.DisplayName,
            Movement = ToMovementStats(form.Movement),
            IdleAnim = Or(p.Idle, result.IdleAnim), RunAnim = Or(p.Run, result.RunAnim), DashAnim = Or(p.Dash, result.DashAnim),
            JumpAnim = Or(p.Jump, result.JumpAnim), FallAnim = Or(p.Fall, result.FallAnim),
            HitSmallAnim = Or(p.HitSmall, result.HitSmallAnim), HitMediumAnim = Or(p.HitMedium, result.HitMediumAnim),
            HitHardAnim = Or(p.HitHard, result.HitHardAnim), TumbleAnim = Or(p.Tumble, result.TumbleAnim),
            ShieldAnim = Or(p.Shield, result.ShieldAnim), GrabAnim = Or(p.Grab, result.GrabAnim),
            GrabbedAnim = Or(p.Grabbed, result.GrabbedAnim), ThrowForwardAnim = Or(p.ThrowForward, result.ThrowForwardAnim),
            AirDodgeAnim = Or(p.AirDodge, result.AirDodgeAnim),
            CookedSlots = form.Slots,
        };
        return CharacterDefinition.WithForms(result, alternate);
    }

    private static MovementStats ToMovementStats(CookedMovement m)
        => new() { RunSpeed=m.RunSpeed, RunAccelerationA=m.RunAccelerationA, RunAccelerationB=m.RunAccelerationB, DashSpeed=m.DashSpeed, AirDodgeSpeed=m.AirDodgeSpeed, AirSpeedMax=m.AirSpeedMax, AirAccelStick=m.AirAccelStick, AirAccelBase=m.AirAccelBase, JumpForce=m.JumpForce, ShortHopForce=m.ShortHopForce, AirJumpVMultiplier=m.AirJumpVMultiplier, AirJumpHMultiplier=m.AirJumpHMultiplier, Gravity=m.Gravity, AirFloatGravity=m.AirFloatGravity, DashDurationTicks=m.DashDurationTicks, DashCooldownTicks=m.DashCooldownTicks, GroundFriction=m.GroundFriction, AirFriction=m.AirFriction, MaxFallSpeed=m.MaxFallSpeed, FastFallSpeed=m.FastFallSpeed, MaxJumps=m.MaxJumps, JumpSquatTicks=m.JumpSquatTicks, FloatWindowTicks=m.FloatWindowTicks, RushTicks=m.RushTicks };

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
            HitPresentationId = hitbox.HitPresentationId,
            RequiresFacingOwner = hitbox.RequiresFacingOwner,
        };

    internal static string? RuntimeBoneId(string? value)
        => CharacterBoneRegistry.BakedName(value);
}
