using System;
using System.Collections.Generic;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class CrouchBraceTests
{
    private const ulong Attacker = 1;
    private const ulong Target = 100;
    private static readonly float GroundPy = TestHelpers.GroundPY(TestHelpers.EngineDef);

    [Fact]
    public void SettledCrouch_DeferredMeleeLaunch_IsExactlyNinetyPercent()
    {
        var standing = RunDeferredHit(brace: false, projectile: false, explosion: false);
        var crouched = RunDeferredHit(brace: true, projectile: false, explosion: false);

        Assert.Equal(standing.DamagePercent, crouched.DamagePercent);
        Assert.Equal(standing.HitstunTicks, crouched.HitstunTicks);
        Assert.Equal(standing.HitstopTicks, crouched.HitstopTicks);
        Assert.Equal(standing.KVX * .9f, crouched.KVX, 5);
        Assert.Equal(standing.KVY * .9f, crouched.KVY, 5);
        Assert.Equal(standing.KVZ * .9f, crouched.KVZ, 5);
    }

    [Fact]
    public void SettledCrouch_DeferredProjectileLaunch_IsExactlyNinetyPercent()
    {
        var standing = RunDeferredHit(brace: false, projectile: true, explosion: false);
        var crouched = RunDeferredHit(brace: true, projectile: true, explosion: false);

        Assert.Equal(standing.DamagePercent, crouched.DamagePercent);
        Assert.Equal(standing.HitstunTicks, crouched.HitstunTicks);
        Assert.Equal(standing.KVX * .9f, crouched.KVX, 5);
        Assert.Equal(standing.KVY * .9f, crouched.KVY, 5);
        Assert.Equal(standing.KVZ * .9f, crouched.KVZ, 5);
    }

    [Fact]
    public void SettledCrouch_DeferredExplosionLaunch_IsExactlyNinetyPercent()
    {
        var standing = RunDeferredHit(brace: false, projectile: false, explosion: true);
        var crouched = RunDeferredHit(brace: true, projectile: false, explosion: true);

        Assert.Equal(standing.DamagePercent, crouched.DamagePercent);
        Assert.Equal(standing.HitstunTicks, crouched.HitstunTicks);
        Assert.Equal(standing.KVX * .9f, crouched.KVX, 5);
        Assert.Equal(standing.KVY * .9f, crouched.KVY, 5);
        Assert.Equal(standing.KVZ * .9f, crouched.KVZ, 5);
    }

    [Fact]
    public void MissingCrouchPose_DoesNotBraceEvenWhenSettled()
    {
        var standing = RunDeferredHit(brace: false, projectile: false, explosion: false);
        var missingPose = RunDeferredHit(brace: true, projectile: false, explosion: false, withPose: false);

        Assert.Equal(standing.KVX, missingPose.KVX, 5);
        Assert.Equal(standing.KVY, missingPose.KVY, 5);
        Assert.Equal(standing.KVZ, missingPose.KVZ, 5);
    }

    [Fact]
    public void StaticUprightHurtboxes_DoNotBraceDespiteNamedPoseTrack()
    {
        var standing = RunDeferredHit(brace: false, projectile: false, explosion: false);
        var upright = RunDeferredHit(brace: true, projectile: false, explosion: false, withBoneDefs: false);
        Assert.Equal(standing.KVX, upright.KVX, 5);
        Assert.Equal(standing.KVY, upright.KVY, 5);
        Assert.Equal(standing.KVZ, upright.KVZ, 5);
    }

    [Fact]
    public void BraceHelper_ScalesOnlyFinalLaunchVector()
    {
        var state = TestHelpers.PlayerState() with
        {
            DamagePercent = 70,
            HitstopTicks = 4,
            HitstunTicks = 23,
            KVX = 2f,
            KVY = 3f,
            KVZ = 4f,
        };
        Simulation.ApplyCrouchBrace(ref state, eligible: true, multiplier: .9f);

        Assert.Equal(1.8f, state.KVX, 5);
        Assert.Equal(2.7f, state.KVY, 5);
        Assert.Equal(3.6f, state.KVZ, 5);
        Assert.Equal((ushort)70, state.DamagePercent);
        Assert.Equal((ushort)4, state.HitstopTicks);
        Assert.Equal((ushort)23, state.HitstunTicks);
    }

    [Fact]
    public void FixedForceLaunch_IsNotBraceScaled()
    {
        var state = TestHelpers.PlayerState() with
        {
            CrouchSettled = true,
            State = ActionState.Crouching,
        };
        Simulation.ApplyKnockbackForce(ref state, 1f, 0f, 35, 10f, 18);

        Assert.Equal(10f * MathF.Cos(35f * MathF.PI / 180f), state.KVX, 5);
        Assert.Equal(10f * MathF.Sin(35f * MathF.PI / 180f), state.KVY, 5);
    }

    [Theory]
    [InlineData(ActionState.Idle, false, 0f, 0)]
    [InlineData(ActionState.Crouching, true, .3f, 0)]
    [InlineData(ActionState.Sliding, true, 0f, 0)]
    [InlineData(ActionState.JumpSquat, true, 0f, 0)]
    [InlineData(ActionState.Crouching, true, 0f, 3)]
    public void BraceExclusions_DoNotScaleEntryBrakingSlideSquatOrLag(
        ActionState posture, bool settled, float vx, ushort landingLag)
    {
        var standing = RunDeferredHit(brace: false, projectile: false, explosion: false);
        var excluded = RunDeferredHit(
            brace: true, projectile: false, explosion: false,
            targetStateOverride: posture, settledOverride: settled,
            targetVx: vx, targetLandingLag: landingLag);

        Assert.Equal(standing.KVX, excluded.KVX, 5);
        Assert.Equal(standing.KVY, excluded.KVY, 5);
        Assert.Equal(standing.KVZ, excluded.KVZ, 5);
    }

    [Fact]
    public void PressAfterContact_DoesNotEarnBrace()
    {
        var standing = RunDeferredHit(brace: false, projectile: false, explosion: false);
        var late = RunDeferredHit(
            brace: true, projectile: false, explosion: false,
            targetStateOverride: ActionState.Idle, settledOverride: false,
            pressAfterContact: true);

        Assert.Equal(standing.KVX, late.KVX, 5);
        Assert.Equal(standing.KVY, late.KVY, 5);
        Assert.Equal(standing.KVZ, late.KVZ, 5);
    }

    [Fact]
    public void ReleaseDuringFreeze_DoesNotRevokeCapturedBrace()
    {
        var standing = RunDeferredHit(brace: false, projectile: false, explosion: false);
        var released = RunDeferredHit(
            brace: true, projectile: false, explosion: false,
            releaseDuringFreeze: true);

        Assert.Equal(standing.KVX * .9f, released.KVX, 5);
        Assert.Equal(standing.KVY * .9f, released.KVY, 5);
        Assert.Equal(standing.KVZ * .9f, released.KVZ, 5);
    }

    [Fact]
    public void ReplacementHit_ClearsCapturedBraceBeforeTheNextLaunch()
    {
        var standing = RunDeferredHit(
            brace: false, projectile: false, explosion: false,
            replacementHit: true);
        var replaced = RunDeferredHit(
            brace: true, projectile: false, explosion: false,
            replacementHit: true);

        Assert.Equal(standing.KVX, replaced.KVX, 5);
        Assert.Equal(standing.KVY, replaced.KVY, 5);
        Assert.Equal(standing.KVZ, replaced.KVZ, 5);
    }

    private static CharacterState RunDeferredHit(
        bool brace, bool projectile, bool explosion, bool withPose = true,
        bool withBoneDefs = true, ActionState? targetStateOverride = null,
        bool settledOverride = false, float targetVx = 0f,
        ushort targetLandingLag = 0, bool pressAfterContact = false,
        bool releaseDuringFreeze = false, bool replacementHit = false)
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var attackerDef = TestHelpers.EngineDef;
        var targetDef = TestHelpers.EngineDef;
        if (withPose)
        {
            targetDef.CrouchAnim = "crouch";
            targetDef.HurtboxBoneDefs = new[]
            {
                new HurtboxBoneDef("mixamorig:Hips", 0f, 0f, 0f, .3f),
            };
        }
        if (!withBoneDefs)
            targetDef.HurtboxBoneDefs = null;

        var attacker = TestHelpers.PlayerState(-2f, 0f) with
        {
            PY = GroundPy,
            IsGrounded = true,
        };
        ActionState targetPosture = targetStateOverride
            ?? (brace ? ActionState.Crouching : ActionState.Idle);
        var target = TestHelpers.NpcState(0f, 0f) with
        {
            PY = GroundPy,
            IsGrounded = true,
            State = targetPosture,
            CrouchSettled = targetStateOverride.HasValue ? settledOverride : brace,
            VX = targetVx,
            LandingLagTicks = targetLandingLag,
        };
        sim.RegisterEntity(Attacker, attackerDef, attacker);
        sim.RegisterEntity(Target, targetDef, target, withPose ? CrouchPose() : null);

        if (explosion)
        {
            sim.Resolver.Spawn(new Hitbox
            {
                // Land beside the target so the explosion, rather than direct contact, connects.
                X = 1.2f, Y = 1f, Z = 0f,
                VY = -20f,
                Gravity = 36f,
                Radius = .2f,
                Shape = HitboxShape.Sphere,
                Damage = 5f,
                BaseKnockback = 4f,
                KnockbackGrowth = 3f,
                KnockbackAngle = 35,
                StunTicks = 18,
                DurationTicks = 120,
                OwnerId = Attacker,
                Explosion = new ProjectileExplosion
                {
                    Radius = 1.5f,
                    Damage = 5f,
                    Knockback = new KnockbackData
                    {
                        Profile = KnockbackProfile.Custom,
                        Angle = 35,
                        BaseKnockback = 4f,
                        KnockbackGrowth = 3f,
                    },
                    StunTicks = 18,
                    DurationTicks = 1,
                },
            });
        }
        else
        {
            sim.Resolver.Spawn(new Hitbox
            {
                X = projectile ? -.5f : 0f,
                Y = GroundPy,
                Z = 0f,
                VX = projectile ? 30f : 0f,
                Radius = .6f,
                Shape = HitboxShape.Sphere,
                Damage = 5f,
                BaseKnockback = 4f,
                KnockbackGrowth = 3f,
                KnockbackAngle = 35,
                StunTicks = 18,
                DurationTicks = 3,
                OwnerId = Attacker,
                FreezesOwner = !projectile,
            });
        }

        var targetInput = brace && !pressAfterContact ? new InputState { Down = true } : default;
        InputState freezeInput = releaseDuringFreeze
            ? default
            : pressAfterContact ? new InputState { Down = true } : targetInput;
        for (int tick = 0; tick < 180 && sim.GetState(Target).HitstopTicks == 0; tick++)
        {
            sim.Tick(new Dictionary<ulong, InputState>
            {
                [Attacker] = default,
                [Target] = targetInput,
            });
            if (replacementHit && tick == 0 && sim.GetState(Target).HitstopTicks > 0)
            {
                sim.Resolver.Spawn(new Hitbox
                {
                    X = 0f, Y = GroundPy, Z = 0f, Radius = .6f,
                    Shape = HitboxShape.Sphere, Damage = 5f,
                    BaseKnockback = 4f, KnockbackGrowth = 3f,
                    KnockbackAngle = 35, StunTicks = 18,
                    DurationTicks = 1, OwnerId = Attacker,
                    FreezesOwner = true,
                });
            }
        }

        Assert.True(sim.GetState(Target).HitstopTicks > 0, "fixture did not produce a deferred hit");
        while (sim.GetState(Target).HitstopTicks > 0)
            sim.Tick(new Dictionary<ulong, InputState>
            {
                [Attacker] = default,
                [Target] = freezeInput,
            });
        return sim.GetState(Target);
    }

    private static BakedAnimationData CrouchPose()
        => new()
        {
            BoneNames = new[] { "mixamorig:Hips" },
            Animations = new[]
            {
                new BakedAnimationData.BakedAnim
                {
                    Name = "crouch",
                    FrameCount = 1,
                    Frames = new[] { new[] { 0f, 0f, 0f } },
                },
            },
        };
}
