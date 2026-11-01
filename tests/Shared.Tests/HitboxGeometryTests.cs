using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Tests for the shared hitbox position resolver (spec #119): the same pure function
/// the server (ServerAbility.SpawnHitbox) and the Ability Lab preview both call.
/// Covers the entity-relative path, the bone-attached path, capsule ends, facing
/// rotation, and the tick→baked-frame projection.
/// </summary>
public class HitboxGeometryTests
{
    private static byte[] BuildTestBin(string[] boneNames, (string name, int frameCount)[] anims, Func<int, int, int, float> bonePos)
    {
        var bytes = new List<byte>();
        bytes.AddRange(Encoding.ASCII.GetBytes("SKEL"));
        bytes.AddRange(BitConverter.GetBytes(1u));
        bytes.AddRange(BitConverter.GetBytes((uint)boneNames.Length));
        bytes.AddRange(BitConverter.GetBytes((uint)anims.Length));
        foreach (var name in boneNames)
        {
            byte[] nameBytes = Encoding.UTF8.GetBytes(name);
            bytes.AddRange(BitConverter.GetBytes((uint)nameBytes.Length));
            bytes.AddRange(nameBytes);
        }
        foreach (var (animName, frameCount) in anims)
        {
            byte[] animNameBytes = Encoding.UTF8.GetBytes(animName);
            bytes.AddRange(BitConverter.GetBytes((uint)animNameBytes.Length));
            bytes.AddRange(animNameBytes);
            bytes.AddRange(BitConverter.GetBytes((uint)frameCount));
            for (int f = 0; f < frameCount; f++)
                for (int bone = 0; bone < boneNames.Length; bone++)
                    for (int axis = 0; axis < 3; axis++)
                        bytes.AddRange(BitConverter.GetBytes(bonePos(f, bone, axis)));
        }
        return bytes.ToArray();
    }

    /// <summary>Fixed engine body and synthetic normal timelines for pose projection tests.</summary>
    private static CharacterDefinition BoneDef(ushort groundDuration = 60, ushort airDuration = 60)
    {
        var def = TestHelpers.EngineDef;
        def = def with { HipHeight = 0.5f };
        var slots = def.CookedSlots!.ToArray();
        slots[0] = NormalSlot(0, "ground.1", false, groundDuration);
        slots[7] = NormalSlot(7, "air.1", true, airDuration);
        def = def with { CookedSlots = slots };


        def = def with { HurtboxBoneScale = 1f };
        return def;
    }

    private static CookedSlotDefinition NormalSlot(int ordinal, string id, bool air, ushort duration)
        => new(ordinal, id, air, "Geometry test", "", "", AuthoringAbilityBehavior.MeleeCombo,
            AuthoringAimMode.None, 0, false, false,
            new CookedTimeline(new[]
            {
                new CookedStage(duration, 0, 0, 0, 0, new[] { "attack" },
                    Array.Empty<CookedTimelineOperation>()),
            }));

    [Fact]
    public void EntityRelative_FacingZero_OffZIsFront()
    {
        var s = new CharacterState { PX = 1f, PY = 2f, PZ = 3f, FacingYaw = 0f };
        var evt = new HitboxEvent { OffX = 0f, OffY = 0.5f, OffZ = 1.5f };

        HitboxGeometry.ResolvePositions(s, evt, null, null, null, 0, out float wx, out float wy, out float wz, out _, out _, out _);

        // Facing +Z (yaw 0): OffZ=front → wz, OffY=up → wy, OffX=0.
        Assert.Equal(1f, wx, 5);
        Assert.Equal(2.5f, wy, 5);
        Assert.Equal(4.5f, wz, 5);
    }

    [Fact]
    public void EntityRelative_FacingPiOver2_FrontRotatesToPlusX()
    {
        var s = new CharacterState { PX = 1f, PY = 2f, PZ = 3f, FacingYaw = MathF.PI / 2f };
        var evt = new HitboxEvent { OffZ = 1.5f };

        HitboxGeometry.ResolvePositions(s, evt, null, null, null, 0, out float wx, out float wy, out float wz, out _, out _, out _);

        // Facing +X: local +Z (front) maps to world +X.
        Assert.Equal(2.5f, wx, 5);
        Assert.Equal(2f, wy, 5);
        Assert.Equal(3f, wz, 5);
    }

    [Fact]
    public void CapsuleEnd_ExtendsAlongFacing()
    {
        var s = new CharacterState { PX = 0f, PY = 1f, PZ = 0f, FacingYaw = 0f };
        var evt = new HitboxEvent { OffZ = 0.5f, EndOffZ = 1.2f, EndOffY = 0.1f };

        HitboxGeometry.ResolvePositions(s, evt, null, null, null, 0, out float wx, out float wy, out float wz, out float wex, out float wey, out float wez);

        // Start at (0, 1, 0.5); EndOffZ extends along facing (+Z).
        Assert.Equal(0f, wx, 5);
        Assert.Equal(1f, wy, 5);
        Assert.Equal(0.5f, wz, 5);
        Assert.Equal(0f, wex, 5);
        Assert.Equal(1.1f, wey, 5);
        Assert.Equal(1.7f, wez, 5);
    }

    [Fact]
    public void BoneAttached_PositionsAtBoneWorldPosition()
    {
        // 60-frame "attack": frame f puts Head at Y = 0.9 + f*0.01, X/Z = 0.
        var baked = BakedAnimationData.LoadFromBin(BuildTestBin(
            new[] { "mixamorig:Head", "mixamorig:Hips" },
            new[] { ("attack", 60) },
            (f, bone, axis) => (bone, axis) switch { (0, 1) => 0.9f + f * 0.01f, (1, 1) => 0.4f, _ => 0f }));
        var def = BoneDef();
        var s = new CharacterState { PX = 1f, PY = 0.75f, PZ = 2f, FacingYaw = 0f, AttackElapsedTicks = 30 };
        var evt = new HitboxEvent { BoneName = "mixamorig:Head" };

        HitboxGeometry.ResolvePositions(s, evt, baked, def, def.GetCookedSlotAbility(AbilitySlots.Normal1, false), 0, out float wx, out float wy, out float wz, out _, out _, out _);

        // bakedFrame = min(30 * 60 / 60, 59) = 30 → Head Y = 0.9 + 0.3 = 1.2
        // world Y = py - h/2 + HipHeight + by = 0.75 - 0.75 + 0.5 + 1.2 = 1.7
        Assert.Equal(1f, wx, 5);
        Assert.Equal(1.7f, wy, 5);
        Assert.Equal(2f, wz, 5);
    }

    [Fact]
    public void BoneAttached_AppliesBoneOffsetRotated()
    {
        var baked = BakedAnimationData.LoadFromBin(BuildTestBin(
            new[] { "mixamorig:Head", "mixamorig:Hips" },
            new[] { ("attack", 1) },
            (f, bone, axis) => 0f));
        var def = BoneDef();
        var s = new CharacterState { PX = 0f, PY = 0.75f, PZ = 0f, FacingYaw = MathF.PI / 2f };
        var evt = new HitboxEvent { BoneName = "mixamorig:Hips", OffZ = 0.2f, OffY = 0.1f };

        HitboxGeometry.ResolvePositions(s, evt, baked, def, def.GetCookedSlotAbility(AbilitySlots.Normal1, false), 0, out float wx, out float wy, out float wz, out _, out _, out _);

        // Hips at (0,0,0) baked → world (0, 0.5, 0); BoneOffZ=0.2 at yaw PI/2 → +X.
        Assert.Equal(0.2f, wx, 5);
        Assert.Equal(0.6f, wy, 5);
        Assert.Equal(0f, wz, 5);
    }

    [Fact]
    public void BoneNameInBakeButNotDefs_AttachesAtBone()
    {
        // The bake carries the full mixamorig skeleton (here incl. LeftToes), while the
        // defs only cover the hurtbox subset — a hitbox must still attach to any baked
        // bone (Ability Lab bone dropdown lists the full bake).
        var baked = BakedAnimationData.LoadFromBin(BuildTestBin(
            new[] { "mixamorig:Head", "mixamorig:Hips", "mixamorig:LeftToes" },
            new[] { ("attack", 1) },
            (f, bone, axis) => 0f));
        var def = BoneDef(); // defs: Head, Hips only
        var s = new CharacterState { PX = 0f, PY = 0.75f, PZ = 0f, FacingYaw = 0f };
        var evt = new HitboxEvent { BoneName = "mixamorig:LeftToes" };

        HitboxGeometry.ResolvePositions(s, evt, baked, def, def.GetCookedSlotAbility(AbilitySlots.Normal1, false), 0, out float wx, out float wy, out float wz, out _, out _, out _);

        // LeftToes at (0,0,0) baked → world (0, py - h/2 + HipHeight + 0, 0) = (0, 0.5, 0).
        Assert.Equal(0f, wx, 5);
        Assert.Equal(0.5f, wy, 5);
        Assert.Equal(0f, wz, 5);
    }

    [Fact]
    public void BoneNameWithoutBakedData_FallsBackToEntityOffset()
    {
        var def = BoneDef();
        var s = new CharacterState { PX = 1f, PY = 2f, PZ = 3f, FacingYaw = 0f };
        var evt = new HitboxEvent { BoneName = "mixamorig:Head", OffZ = 1.5f };

        HitboxGeometry.ResolvePositions(s, evt, null, def, def.GetCookedSlotAbility(AbilitySlots.Normal1, false), 0, out float wx, out float wy, out float wz, out _, out _, out _);

        Assert.Equal(1f, wx, 5); // entity-relative: OffZ lands on wz at yaw 0
        Assert.Equal(2f, wy, 5);
        Assert.Equal(4.5f, wz, 5);
    }

    [Fact]
    public void BoneNameNotInDefs_FallsBackToEntityOffset()
    {
        var baked = BakedAnimationData.LoadFromBin(BuildTestBin(
            new[] { "mixamorig:Head", "mixamorig:Hips" },
            new[] { ("attack", 1) },
            (f, bone, axis) => 0f));
        var def = BoneDef();
        var s = new CharacterState { PX = 0f, PY = 2f, PZ = 0f, FacingYaw = 0f };
        var evt = new HitboxEvent { BoneName = "mixamorig:LeftFoot", OffZ = 0.5f };

        HitboxGeometry.ResolvePositions(s, evt, baked, def, def.GetCookedSlotAbility(AbilitySlots.Normal1, false), 0, out float wx, out float wy, out float wz, out _, out _, out _);

        Assert.Equal(0f, wx, 5);
        Assert.Equal(2f, wy, 5);
        Assert.Equal(0.5f, wz, 5);
    }

    [Fact]
    public void BoneNameOnlyInHurtboxDefs_FallsBackToEntityOffset()
    {
        // Hurtbox defs are not a bone index table: a def naming a bone the bake lacks
        // must not borrow the baked bone at the def's index (here Hips).
        var baked = BakedAnimationData.LoadFromBin(BuildTestBin(
            new[] { "mixamorig:Hips" },
            new[] { ("attack", 1) },
            (f, bone, axis) => axis == 1 ? 0.4f : 0f));
        var def = BoneDef();
        def = def with { HurtboxBoneDefs = new[] { new HurtboxBoneDef("mixamorig:Head", 0, 0, 0, 0.22f) } };
        var s = new CharacterState { PX = 0f, PY = 2f, PZ = 0f, FacingYaw = 0f };
        var evt = new HitboxEvent { BoneName = "mixamorig:Head", OffZ = 0.5f };

        HitboxGeometry.ResolvePositions(s, evt, baked, def, def.GetCookedSlotAbility(AbilitySlots.Normal1, false), 0, out float wx, out float wy, out float wz, out _, out _, out _);

        Assert.Equal(0f, wx, 5);
        Assert.Equal(2f, wy, 5); // the Hips bone would give 2 - 0.75 + 0.5 + 0.4 = 2.15
        Assert.Equal(0.5f, wz, 5);
    }

    [Fact]
    public void EndBoneName_AnchorsCapsuleEndAtSecondPoint()
    {
        // 1-frame bake: RightHand at (0, 0.9, 0.1), _weapon_tip at (0, 0.9, 1.6).
        var baked = BakedAnimationData.LoadFromBin(BuildTestBin(
            new[] { "mixamorig:RightHand", "mixamorig:Hips", "_weapon_tip" },
            new[] { ("attack", 1) },
            (f, bone, axis) => (bone, axis) switch
            {
                (0, 1) => 0.9f, (0, 2) => 0.1f,
                (1, 1) => 0.4f,
                (2, 1) => 0.9f, (2, 2) => 1.6f,
                _ => 0f,
            }));
        var def = BoneDef(); // defs: Head, Hips only — both points resolve via the bake
        var s = new CharacterState { PX = 0f, PY = 0.75f, PZ = 0f, FacingYaw = 0f };
        var evt = new HitboxEvent { BoneName = "mixamorig:RightHand", EndBoneName = "_weapon_tip" };

        HitboxGeometry.ResolvePositions(s, evt, baked, def, def.GetCookedSlotAbility(AbilitySlots.Normal1, false), 0, out float wx, out float wy, out float wz, out float wex, out float wey, out float wez);

        // Start anchors at RightHand, end anchors at the baked tip (NOT the EndOff
        // delta — EndOff* defaults to 0 here).
        // world Y = py - h/2 + HipHeight + by = 0.75 - 0.75 + 0.5 + 0.9 = 1.4
        Assert.Equal(0f, wx, 5);
        Assert.Equal(1.4f, wy, 5);
        Assert.Equal(0.1f, wz, 5);
        Assert.Equal(0f, wex, 5);
        Assert.Equal(1.4f, wey, 5);
        Assert.Equal(1.6f, wez, 5);
    }

    [Fact]
    public void WeaponAnchoredCapsule_IgnoresHurtboxBoneScale()
    {
        // A weapon capsule (RightHand → _weapon_tip) must span the exact visual blade:
        // the synthetic tip is baked at VISUAL scale, so HurtboxBoneScale must not shrink
        // it (or the capsule starts short of the hand and ends short of the tip).
        var baked = BakedAnimationData.LoadFromBin(BuildTestBin(
            new[] { "mixamorig:RightHand", "mixamorig:Hips", "_weapon_tip" },
            new[] { ("attack", 1) },
            (f, bone, axis) => (bone, axis) switch
            {
                (0, 1) => 0.9f, (0, 2) => 0.1f,   // RightHand at (0, 0.9, 0.1)
                (1, 1) => 0.4f,                    // Hips
                (2, 1) => 0.9f, (2, 2) => 1.6f,   // _weapon_tip at (0, 0.9, 1.6)
                _ => 0f,
            }));
        var def = BoneDef();
        def = def with { HurtboxBoneScale = 0.5f }; // would shrink a real bone; weapon points must not
        var s = new CharacterState { PX = 0f, PY = 0.75f, PZ = 0f, FacingYaw = 0f };
        var evt = new HitboxEvent { BoneName = "mixamorig:RightHand", EndBoneName = "_weapon_tip" };

        HitboxGeometry.ResolvePositions(s, evt, baked, def, def.GetCookedSlotAbility(AbilitySlots.Normal1, false), 0, out float wx, out float wy, out float wz, out float wex, out float wey, out float wez);

        // Full-scale: world Y = py - h/2 + HipHeight + by = 0.75 - 0.75 + 0.5 + 0.9 = 1.4.
        // If HurtboxBoneScale (0.5) applied, the end would collapse to Z 0.8 / Y 0.95.
        Assert.Equal(0f, wx, 5);
        Assert.Equal(1.4f, wy, 5);
        Assert.Equal(0.1f, wz, 5);
        Assert.Equal(0f, wex, 5);
        Assert.Equal(1.4f, wey, 5);
        Assert.Equal(1.6f, wez, 5);
    }

    [Fact]
    public void EndBoneNameNotInBake_FallsBackToEndOffDelta()
    {
        // EndBoneName missing from the bake → graceful fallback to the existing
        // EndOff delta from the resolved start point.
        var baked = BakedAnimationData.LoadFromBin(BuildTestBin(
            new[] { "mixamorig:Head", "mixamorig:Hips" },
            new[] { ("attack", 1) },
            (f, bone, axis) => 0f));
        var def = BoneDef();
        var s = new CharacterState { PX = 0f, PY = 0.75f, PZ = 0f, FacingYaw = 0f };
        var evt = new HitboxEvent { BoneName = "mixamorig:Hips", EndBoneName = "_weapon_tip", EndOffZ = 1.2f };

        HitboxGeometry.ResolvePositions(s, evt, baked, def, def.GetCookedSlotAbility(AbilitySlots.Normal1, false), 0, out float wx, out float wy, out float wz, out float wex, out float wey, out float wez);

        // Hips at (0,0,0) → start world (0, 0.5, 0); end = start + EndOffZ(1.2) facing.
        Assert.Equal(0f, wx, 5);
        Assert.Equal(0.5f, wy, 5);
        Assert.Equal(0f, wz, 5);
        Assert.Equal(0f, wex, 5);
        Assert.Equal(0.5f, wey, 5);
        Assert.Equal(1.2f, wez, 5);
    }

    [Fact]
    public void AirborneMove_UsesAirStageDurationForFrameProjection()
    {
        // 60-frame "attack": frame f puts Head at Y = 0.9 + f*0.01, X/Z = 0.
        var baked = BakedAnimationData.LoadFromBin(BuildTestBin(
            new[] { "mixamorig:Head", "mixamorig:Hips" },
            new[] { ("attack", 60) },
            (f, bone, axis) => (bone, axis) switch { (0, 1) => 0.9f + f * 0.01f, (1, 1) => 0.4f, _ => 0f }));

        var def = BoneDef(groundDuration: 60, airDuration: 30);

        var s = new CharacterState { PX = 0f, PY = 0.75f, PZ = 0f, FacingYaw = 0f, AttackElapsedTicks = 15 };
        var evt = new HitboxEvent { BoneName = "mixamorig:Head" };

        HitboxGeometry.ResolvePositions(s, evt, baked, def, def?.GetCookedSlotAbility((byte)((2) + 1), true), 0, out float wx, out float wy, out float wz, out _, out _, out _);

        // bakedFrame = min(15 * 60 / 30, 59) = 30 → Head Y = 0.9 + 0.3 = 1.2
        // world Y = py - h/2 + HipHeight + by = 0.75 - 0.75 + 0.5 + 1.2 = 1.7
        Assert.Equal(0f, wx, 5);
        Assert.Equal(1.7f, wy, 5); // would be 1.55 if the ground duration were used
        Assert.Equal(0f, wz, 5);
    }
    [Fact]
    public void RunningGroundMoveUsesItsActivationVariantForHurtboxesAndSpawnGeometry()
    {
        var baked = BakedAnimationData.LoadFromBin(BuildTestBin(new[] { "point" },
            new[] { ("attack", 60), ("airattack", 60) },
            (frame, _, axis) => axis == 0 ? frame : 0));
        var def = BoneDef(groundDuration: 60, airDuration: 30);
        def = def with { HurtboxBoneDefs = new[] { new HurtboxBoneDef("point", 0, 0, 0, .1f) } };
        var slots = def.CookedSlots!.ToArray();
        slots[0] = new CookedSlotDefinition(0, "ground.1", false, "test", "", "",
            AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, 0, false, false,
            new CookedTimeline(new[]
            {
                new CookedStage(60, 0, 0, 0, 0, new[] { "attack" }, new CookedTimelineOperation[]
                {
                    new CookedSpawnHitboxOperation(5, AuthoringUnit.Meters,
                        new CookedHitbox(AuthoringHitboxShape.Sphere, .1f, 0, 0, 0, 0, 0, 0,
                            "point", null, 0, 0, 0, 0, 0, 20, true, 0)),
                }),
            }));
        slots[7] = new CookedSlotDefinition(7, "air.1", true, "test", "", "",
            AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, 0, false, false,
            new CookedTimeline(new[] { new CookedStage(30, 0, 0, 0, 0, new[] { "airattack" }, Array.Empty<CookedTimelineOperation>()) }));
        def = def with { CookedSlots = slots };
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        sim.RegisterEntity(1, def, TestHelpers.PlayerState() with { PX = 50, PZ = 50, PY = 10, IsGrounded = true }, baked);
        sim.Tick(new() { [1] = new InputState { ActiveSlot = AbilitySlots.Normal1 } });
        for (int tick = 1; tick < 5; tick++) sim.Tick(new());
        var owner = sim.GetState(1);
        Assert.False(owner.IsGrounded);
        Assert.Equal("attack", sim.GetActiveMove(1)!.Value.Pose.AnimationId);
        var hitbox = Assert.Single(sim.Resolver.GetActiveHitboxes());
        Assert.False(hitbox.Airborne);
        Assert.Equal(owner.PX + 5f, hitbox.X, 5);
        Assert.Equal(owner.PX + 5f, Assert.Single(sim.GetLastEntityData()).PosX, 5);
    }

    [Fact]
    public void PhaseClipUsesItsReseekedClockAtRealTime()
    {
        var def = BoneDef();
        var slots = def.CookedSlots!.ToArray();
        slots[0] = new CookedSlotDefinition(0, "ground.1", false, "test", "", "",
            AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, 0, false, false,
            new CookedTimeline(new[] { new CookedStage(20, 0, 0, 0, 0,
                new[] { "attack", "landing" }, Array.Empty<CookedTimelineOperation>()) }));
        def = def with { CookedSlots = slots };
        var baked = BakedAnimationData.LoadFromBin(BuildTestBin(new[] { "point" },
            new[] { ("landing", 60) }, (frame, _, axis) => axis == 0 ? frame : 0));
        var state = new CharacterState { AttackElapsedTicks = 7, ComboStage = 1 };
        var pose = ActiveMoveView.ResolvePose(slots[0], 1, state.AttackElapsedTicks);
        Assert.True(pose.IsPhaseClip);
        Assert.Equal(7f, pose.FrameAt(60));
        HitboxGeometry.ResolvePositions(state, new HitboxEvent { BoneName = "point" }, baked, def, def?.GetCookedSlotAbility(AbilitySlots.Normal1, false), 1, out float x, out _, out _, out _, out _, out _);
        Assert.Equal(7f, x, 5);
    }

    [Fact]
    public void AttackPosePitch_RotatesEndpointAroundHipPivot()
    {
        var state = new CharacterState
        {
            PX = 2f, PY = 3f, PZ = -1f,
            FacingYaw = 0f, AttackPosePitch = MathF.PI / 2f,
        };
        var def = TestHelpers.EngineDef;
        def = def with { CapsuleHeight = 2f };
        def = def with { HipHeight = 1f };
        float x = 2f, y = 4f, z = 1f;

        HitboxGeometry.ApplyAttackPosePitch(in state, def, ref x, ref y, ref z);

        Assert.Equal(2f, x, 5);
        Assert.Equal(5f, y, 5); // Hip pivot Y=3 plus the original two-metre forward offset.
        Assert.Equal(-2f, z, 5);
    }

    [Fact]
    public void AttackPosePitch_TransformsBothResolvedCapsuleEndpoints()
    {
        var state = new CharacterState
        {
            PX = 0f, PY = 2f, PZ = 0f,
            FacingYaw = MathF.PI / 2f, AttackPosePitch = MathF.PI / 2f,
        };
        var def = TestHelpers.EngineDef;
        def = def with { CapsuleHeight = 2f };
        def = def with { HipHeight = 1f };
        var evt = new HitboxEvent { Shape = HitboxShape.Capsule, OffZ = 1f, EndOffZ = 1f };
        HitboxGeometry.ResolvePositions(in state, in evt, null, def, def?.GetCookedSlotAbility((byte)((2) + 1), false), 0, out float startX, out float startY, out float startZ, out float endX, out float endY, out float endZ);

        Assert.Equal(0f, startX, 5);
        Assert.Equal(3f, startY, 5);
        Assert.Equal(0f, startZ, 5);
        Assert.Equal(0f, endX, 5);
        Assert.Equal(4f, endY, 5);
        Assert.Equal(0f, endZ, 5);
    }
}

