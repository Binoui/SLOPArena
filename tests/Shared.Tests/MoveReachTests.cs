using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Issue #151 — deterministic authored hitbox reach chart geometry (<see cref="MoveReach"/>),
/// validated against <see cref="HitboxGeometry.ResolvePositions"/> — the exact function
/// ServerAbility.SpawnHitbox uses. All hitboxes are entity-relative unless noted; the
/// character stands at the grounded origin frame (PY = CapsuleHeight/2, yaw 0 → forward = +Z).
/// </summary>
public class MoveReachTests
{
    private const float Tol = 0.05f; // ±5 cm reach tolerance

    /// <summary>Entity-relative sphere hitbox (Custom knockback profile, default radius 0.3).</summary>
    private static HitboxEvent Sphere(float offZ, float offY = 0f, float radius = 0.3f,
        ushort trigger = 0, ushort duration = 1, string? bone = null)
        => new()
        {
            TriggerTick = trigger, DurationTicks = duration, Shape = HitboxShape.Sphere,
            Radius = radius, OffX = 0f, OffY = offY, OffZ = offZ, BoneName = bone,
            Damage = 4f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 0, BaseKnockback = 1f, KnockbackGrowth = 1f },
            StunTicks = 10, Interruptible = true,
        };


    [Fact]
    public void SampleHit_EntityRelativeSphere_ReachMatchesAuthoredExtent()
    {
        var def = TestHelpers.EngineDef;
        var samples = MoveReach.SampleHit(def, Sphere(offZ: 0.5f), slot: 2, airborne: false, 0, baked: null);

        Assert.Single(samples);
        float y = def.CapsuleHeight / 2f;
        var ext = MoveReach.ExtentAt(samples, y);
        Assert.NotNull(ext);
        TestHelpers.AssertNear(0.2f, ext.Value.MinZ, Tol); // 0.5 − 0.3
        TestHelpers.AssertNear(0.8f, ext.Value.MaxZ, Tol); // 0.5 + 0.3
    }

    [Fact]
    public void SampleHit_ActiveWindow_ResolvesEveryTick_WithAuthoredTicks()
    {
        var def = TestHelpers.EngineDef;
        var samples = MoveReach.SampleHit(def, Sphere(offZ: 0.5f, trigger: 2, duration: 8),
            slot: 2, airborne: false, 0, baked: null);

        Assert.Equal(8, samples.Length);
        for (int i = 0; i < samples.Length; i++)
            Assert.Equal((ushort)(2 + i), samples[i].Tick); // authored tick offsets, not 0-based

        float y = def.CapsuleHeight / 2f;
        var ext = MoveReach.ExtentAt(samples, y);
        Assert.NotNull(ext);
        TestHelpers.AssertNear(0.2f, ext.Value.MinZ, Tol);
        TestHelpers.AssertNear(0.8f, ext.Value.MaxZ, Tol);
    }

    [Fact]
    public void SampleHit_CapsuleSweep_SpansEndOff()
    {
        var def = TestHelpers.EngineDef;
        // Horizontal capsule from z 0.3 to z 0.3 + 1.2 = 1.5 at sphere-center height.
        var evt = new HitboxEvent
        {
            TriggerTick = 0, DurationTicks = 1, Shape = HitboxShape.Capsule, Radius = 0.25f,
            OffX = 0f, OffY = 0f, OffZ = 0.3f, EndOffX = 0f, EndOffY = 0f, EndOffZ = 1.2f,
            Damage = 4f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 0, BaseKnockback = 1f, KnockbackGrowth = 1f },
            StunTicks = 10, Interruptible = true,
        };
        var samples = MoveReach.SampleHit(def, evt, slot: 2, airborne: false, 0, baked: null);

        float y = def.CapsuleHeight / 2f;
        var ext = MoveReach.ExtentAt(samples, y);
        Assert.NotNull(ext);
        TestHelpers.AssertNear(0.05f, ext.Value.MinZ, Tol); // 0.3 − 0.25
        TestHelpers.AssertNear(1.75f, ext.Value.MaxZ, Tol); // 1.5 + 0.25 (spherical end cap)
    }

    [Fact]
    public void ExtentAt_HeightOutsideVolume_ReturnsNull()
    {
        var def = TestHelpers.EngineDef;
        // Sphere center at world y 1.1 (OffY 0.35 above PY 0.75), radius 0.3 → volume 0.8–1.4.
        var samples = MoveReach.SampleHit(def, Sphere(offZ: 0.5f, offY: 0.35f, radius: 0.3f),
            slot: 2, airborne: false, 0, baked: null);

        Assert.Null(MoveReach.ExtentAt(samples, 0.7f)); // below the volume
        var ext = MoveReach.ExtentAt(samples, 1.1f);     // center height
        Assert.NotNull(ext);
        TestHelpers.AssertNear(0.2f, ext.Value.MinZ, Tol);
        TestHelpers.AssertNear(0.8f, ext.Value.MaxZ, Tol);
    }

    [Fact]
    public void BandExtent_SphereOnlyInHighBand_LowAndMidNull()
    {
        var def = TestHelpers.EngineDef; // CapsuleHeight 1.5
        float h = def.CapsuleHeight;
        // Sphere center at world y 1.45 (OffY 0.7), radius 0.3 → volume 1.15–1.75 ⊆ high band [1, 2].
        var samples = MoveReach.SampleHit(def, Sphere(offZ: 0.5f, offY: 0.7f, radius: 0.3f),
            slot: 2, airborne: false, 0, baked: null);

        Assert.Null(MoveReach.BandExtent(samples, 0f, h / 3f));
        Assert.Null(MoveReach.BandExtent(samples, h / 3f, 2f * h / 3f));
        var high = MoveReach.BandExtent(samples, 2f * h / 3f, h + 0.5f);
        Assert.NotNull(high);
        TestHelpers.AssertNear(0.2f, high.Value.MinZ, Tol);
        TestHelpers.AssertNear(0.8f, high.Value.MaxZ, Tol);
    }

    [Fact]
    public void ReachOrdering_ShorterMoveReachesLess_AtMidHeight()
    {
        var def = TestHelpers.EngineDef;
        float y = def.CapsuleHeight / 2f;
        var shortSamples = MoveReach.SampleHit(def, Sphere(offZ: 0.6f), slot: 2, airborne: false, 0, baked: null);
        var longSamples = MoveReach.SampleHit(def, Sphere(offZ: 1.2f), slot: 2, airborne: false, 0, baked: null);

        var shortExt = MoveReach.ExtentAt(shortSamples, y);
        var longExt = MoveReach.ExtentAt(longSamples, y);
        Assert.NotNull(shortExt);
        Assert.NotNull(longExt);
        TestHelpers.AssertNear(0.9f, shortExt.Value.MaxZ, Tol);
        TestHelpers.AssertNear(1.5f, longExt.Value.MaxZ, Tol);
        Assert.True(shortExt.Value.MaxZ < longExt.Value.MaxZ,
            $"shorter move ({shortExt.Value.MaxZ:F2}) must reach less than the longer one ({longExt.Value.MaxZ:F2})");
    }

    [Fact]
    public void ReachOrdering_SyntheticBoneCapsule_ExtendsBeyondEntityFallback()
    {
        var def = TestHelpers.EngineDef;
        def = TestHelpers.WithMove(def, AbilitySlots.Slot1, false, "Reach", 60, animations: new[] { "attack" });
        var baked = BakedAnimationData.LoadFromBin(BuildTestBin(
            new[] { "hand", "tip" },
            new[] { ("attack", 1) },
            (frame, bone, axis) => bone == 1 && axis == 2 ? 1.6f : 0f));
        var evt = new HitboxEvent
        {
            DurationTicks = 1,
            Shape = HitboxShape.Capsule,
            Radius = 0.25f,
            BoneName = "hand",
            EndBoneName = "tip",
            Damage = 4f,
            Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 0, BaseKnockback = 1f, KnockbackGrowth = 1f },
            StunTicks = 10,
            Interruptible = true,
        };
        float midMin = def.CapsuleHeight / 3f, midMax = 2f * def.CapsuleHeight / 3f;
        var bakedSamples = MoveReach.SampleHit(def, evt, slot: 2, airborne: false, 0, baked);
        var bakedMid = MoveReach.BandExtent(bakedSamples, midMin, midMax);
        Assert.NotNull(bakedMid);
        var fallbackSamples = MoveReach.SampleHit(def, evt, slot: 2, airborne: false, 0, baked: null);
        var fallbackMid = MoveReach.BandExtent(fallbackSamples, midMin, midMax);
        Assert.NotNull(fallbackMid);
        TestHelpers.AssertNear(0.25f, fallbackMid.Value.MaxZ, Tol);
        Assert.True(bakedMid.Value.MaxZ > fallbackMid.Value.MaxZ);
    }

    [Fact]
    public void WibouNormal_ReferencedBonesAndAnimationsResolveInCookedPose()
    {
        var def = TestHelpers.WibouDef;
        var baked = TestHelpers.LoadBakedData(def);
        Assert.NotNull(baked);
        var ability = def.GetCookedSlotAbility(3, false)!;
        foreach (var animation in ability.Timeline.Stages.SelectMany(x => x.AnimationIds))
            Assert.True(baked.FindAnimIndex(animation) >= 0, $"Unresolved Wibou normal pose: {animation}");
        foreach (var hitbox in ability.Timeline.Stages!.SelectMany(stage => stage.ReadHitboxes()))
        {
            if (hitbox.BoneName != null)
                Assert.Contains(hitbox.BoneName, baked.BoneNames);
            if (hitbox.EndBoneName != null)
                Assert.Contains(hitbox.EndBoneName, baked.BoneNames);
        }
    }

    private static byte[] BuildTestBin(string[] boneNames, (string name, int frameCount)[] anims,
        Func<int, int, int, float> bonePos)
    {
        var bytes = new List<byte>();
        bytes.AddRange(Encoding.ASCII.GetBytes("SKEL"));
        bytes.AddRange(BitConverter.GetBytes(1u));
        bytes.AddRange(BitConverter.GetBytes((uint)boneNames.Length));
        bytes.AddRange(BitConverter.GetBytes((uint)anims.Length));
        foreach (string name in boneNames)
        {
            byte[] nameBytes = Encoding.UTF8.GetBytes(name);
            bytes.AddRange(BitConverter.GetBytes((uint)nameBytes.Length));
            bytes.AddRange(nameBytes);
        }
        foreach (var (name, frameCount) in anims)
        {
            byte[] nameBytes = Encoding.UTF8.GetBytes(name);
            bytes.AddRange(BitConverter.GetBytes((uint)nameBytes.Length));
            bytes.AddRange(nameBytes);
            bytes.AddRange(BitConverter.GetBytes((uint)frameCount));
            for (int frame = 0; frame < frameCount; frame++)
                for (int bone = 0; bone < boneNames.Length; bone++)
                    for (int axis = 0; axis < 3; axis++)
                        bytes.AddRange(BitConverter.GetBytes(bonePos(frame, bone, axis)));
        }
        return bytes.ToArray();
    }
}
