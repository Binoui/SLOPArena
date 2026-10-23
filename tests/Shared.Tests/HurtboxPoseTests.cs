using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Pose-resolution tests for the unified hurtbox path (spec #119).
/// BuildHurtboxList now resolves every entity's hurtboxes through the same
/// BuildEntitiesFromState the Ability Lab preview uses. The golden test locks the
/// real Manki def + shared 7-bone set against the pre-unification formula, proving
/// the refactor is behavior-preserving for shipped characters (all offsets are zero);
/// the offset tests lock the newly activated per-def offset semantics.
/// </summary>
public class HurtboxPoseTests
{
    private static byte[] MakeBin(string[] boneNames, Func<int, int, int, float> bonePos)
    {
        var bytes = new List<byte>();
        bytes.AddRange(Encoding.ASCII.GetBytes("SKEL"));
        bytes.AddRange(BitConverter.GetBytes(1u));
        bytes.AddRange(BitConverter.GetBytes((uint)boneNames.Length));
        bytes.AddRange(BitConverter.GetBytes(1u));
        foreach (var name in boneNames)
        {
            byte[] nameBytes = Encoding.UTF8.GetBytes(name);
            bytes.AddRange(BitConverter.GetBytes((uint)nameBytes.Length));
            bytes.AddRange(nameBytes);
        }
        byte[] animBytes = Encoding.UTF8.GetBytes("idle");
        bytes.AddRange(BitConverter.GetBytes((uint)animBytes.Length));
        bytes.AddRange(animBytes);
        bytes.AddRange(BitConverter.GetBytes(1u)); // 1 frame
        for (int bone = 0; bone < boneNames.Length; bone++)
            for (int axis = 0; axis < 3; axis++)
                bytes.AddRange(BitConverter.GetBytes(bonePos(0, bone, axis)));
        return bytes.ToArray();
    }

    /// <summary>The seven Mixamorig fixture bones, in deterministic bake order.</summary>
    private static readonly string[] BoneNames =
    {
        "mixamorig:Head", "mixamorig:Spine2", "mixamorig:Hips",
        "mixamorig:RightHand", "mixamorig:LeftHand",
        "mixamorig:RightFoot", "mixamorig:LeftFoot",
    };

    /// <summary>Realistic Hips-relative idle pose (bone index → x, y, z).</summary>
    private static float BonePos(int bone, int axis) => (bone, axis) switch
    {
        (0, 1) => 0.90f,  // Head
        (1, 1) => 0.50f,  // Spine2
        (2, 1) => 0.00f,  // Hips
        (3, 0) => 0.30f, (3, 1) => 0.35f, (3, 2) => 0.05f,  // RightHand
        (4, 0) => -0.30f, (4, 1) => 0.35f, (4, 2) => 0.05f, // LeftHand
        (5, 0) => 0.12f, (5, 1) => -0.50f, (5, 2) => 0.02f, // RightFoot
        (6, 0) => -0.12f, (6, 1) => -0.50f, (6, 2) => 0.02f,// LeftFoot
        _ => 0f,
    };

    [Fact]
    public void UnifiedPose_MatchesLegacyTickPath_ForShippedMankiDef()
    {
        // Real Manki def from the registry — the shared 7-bone set, scale 0.85.
        var def = TestHelpers.MankiDef;
        Assert.NotNull(def.HurtboxBoneDefs);
        Assert.Equal(BoneNames.Length, def.HurtboxBoneDefs!.Count);
        var baked = BakedAnimationData.LoadFromBin(MakeBin(BoneNames, (f, bone, axis) => BonePos(bone, axis)));

        var state = new CharacterState
        {
            PX = 1f, PY = TestHelpers.GroundPY(def), PZ = 2f,
            FacingYaw = 0.7f,
        };
        const ulong entityId = 7;

        var resolved = ServerSimulation.BuildEntitiesFromState(state, def, baked, "idle", 0, entityId);

        Assert.Equal(def.HurtboxBoneDefs.Count, resolved.Count);
        float cos = MathF.Cos(state.FacingYaw), sin = MathF.Sin(state.FacingYaw);
        float scale = def.HurtboxBoneScale;
        for (int i = 0; i < def.HurtboxBoneDefs.Count; i++)
        {
            var hbd = def.HurtboxBoneDefs[i];
            // Cooked defs are sorted by bone ID while the fixture keeps bake order:
            // each def must land on the fixture bone it names.
            int bone = Array.IndexOf(BoneNames, hbd.BoneName);
            Assert.True(bone >= 0, $"{hbd.BoneName} is not a fixture bone.");
            // Legacy BuildHurtboxList formula (pre-spec-#119): scale, rotate, BoneYToWorldY,
            // offsets added raw (all zero for shipped defs → identical to unified).
            float bx = BonePos(bone, 0) * scale, by = BonePos(bone, 1) * scale, bz = BonePos(bone, 2) * scale;
            float wx = state.PX + ((bx * cos) + (bz * sin));
            float wy = def.BoneYToWorldY(state.PY, by);
            float wz = state.PZ + ((-bx * sin) + (bz * cos));

            var e = resolved[i];
            Assert.Equal(hbd.Radius, e.Radius, 5);
            Assert.Equal(HitboxShape.Sphere, e.Shape);
            Assert.Equal(entityId, e.Id);
            Assert.True(e.Active);
            TestHelpers.AssertNear(wx, e.PosX, 0.0001f);
            TestHelpers.AssertNear(wy, e.PosY, 0.0001f);
            TestHelpers.AssertNear(wz, e.PosZ, 0.0001f);
        }
    }

    /// <summary>The rig bone each authored semantic hurtbox bone names in the cooked bakes.</summary>
    private static readonly Dictionary<string, string> RigBones = new()
    {
        ["bone.head"] = "mixamorig:Head",
        ["bone.spine2"] = "mixamorig:Spine2",
        ["bone.hips"] = "mixamorig:Hips",
        ["bone.right-hand"] = "mixamorig:RightHand",
        ["bone.left-hand"] = "mixamorig:LeftHand",
        ["bone.right-foot"] = "mixamorig:RightFoot",
        ["bone.left-foot"] = "mixamorig:LeftFoot",
    };

    [Theory]
    [InlineData(CharacterClass.FightGuy)]
    [InlineData(CharacterClass.Manki)]
    [InlineData(CharacterClass.Wibou)]
    [InlineData(CharacterClass.Bonk)]
    public void CookedRoster_AppliesEachAuthoredRadiusToItsNamedBone(CharacterClass fighter)
    {
        // Issue #282: cooked defs are sorted by bone ID while poses.bin keeps bake order.
        var entry = BuiltInContentResolver.Resolve(fighter);
        var def = entry.Definition;
        var baked = entry.BakedAnimation!;
        var authored = entry.CookedCharacterPackage!.Definition.HurtboxBoneDefs;
        Assert.True(baked.FindAnimIndex(def.IdleAnim) >= 0);
        var state = new CharacterState
        {
            PX = 1f, PY = TestHelpers.GroundPY(def), PZ = 2f,
            FacingYaw = 0.7f,
        };

        var resolved = ServerSimulation.BuildEntitiesFromState(state, def, baked, def.IdleAnim, 0, 7);

        Assert.Equal(authored.Count, resolved.Count);
        float cos = MathF.Cos(state.FacingYaw), sin = MathF.Sin(state.FacingYaw);
        float scale = def.HurtboxBoneScale;
        int idle = baked.FindAnimIndex(def.IdleAnim);
        foreach (var bone in authored)
        {
            Assert.True(RigBones.TryGetValue(bone.BoneId, out var rigBone), $"{bone.BoneId} has no expected rig bone.");
            int index = baked.FindBoneIndex(rigBone);
            Assert.True(index >= 0, $"{fighter} bake lacks {rigBone}.");
            // The named bone where it is drawn (cooked hips offset), then the authored offset.
            Assert.True(HitboxGeometry.BakedPointToWorld(state, def, baked, idle, 0, index, scale,
                out float bx, out float by, out float bz));
            float wx = bx + (bone.OffsetX * cos) + (bone.OffsetZ * sin);
            float wy = by + bone.OffsetY;
            float wz = bz + (-bone.OffsetX * sin) + (bone.OffsetZ * cos);

            var sphere = Assert.Single(resolved, e =>
                MathF.Abs(e.PosX - wx) < 0.0001f && MathF.Abs(e.PosY - wy) < 0.0001f && MathF.Abs(e.PosZ - wz) < 0.0001f);
            Assert.True(MathF.Abs(bone.Radius - sphere.Radius) < 0.00001f,
                $"{fighter} {bone.BoneId}: authored radius {bone.Radius}, resolved {sphere.Radius}.");
        }
    }

    [Fact]
    public void UnifiedPose_SkipsDefWhoseBoneIsNotBaked()
    {
        // A def naming a bone the bake lacks resolves nothing; it must not borrow the
        // baked bone at its own index.
        var def = new CharacterDefinition
        {
            CapsuleHeight = 1.5f,
            HipHeight = 0.5f,
            HurtboxBoneScale = 1.0f,
            HurtboxBoneDefs = new[]
            {
                new HurtboxBoneDef("mixamorig:Neck", 0, 0, 0, 0.5f),
                new HurtboxBoneDef("mixamorig:Spine2", 0, 0, 0, 0.26f),
            },
        };
        var baked = BakedAnimationData.LoadFromBin(MakeBin(BoneNames, (f, bone, axis) => BonePos(bone, axis)));
        var state = new CharacterState { PX = 0f, PY = 0.75f, PZ = 0f };

        var resolved = ServerSimulation.BuildEntitiesFromState(state, def, baked, "idle", 0, 0);

        var e = Assert.Single(resolved);
        // Spine2 (baked y 0.5) → world Y = py - h/2 + HipHeight + by = 0.75 - 0.75 + 0.5 + 0.5 = 1.0
        Assert.Equal(1.0f, e.PosY, 5);
        Assert.Equal(0.26f, e.Radius, 5);
    }

    [Fact]
    public void SimTick_UsesCookedPresentationIdsForLocomotionHurtboxes()
    {
        var def = new CharacterDefinition
        {
            Movement = new MovementStats
            {
                RunSpeed = 14f, RunAccelerationA = 20f, RunAccelerationB = 12f,
                GroundFriction = 8f, AirFriction = 6f, Gravity = 36f,
                MaxFallSpeed = 48f, MaxJumps = 2, JumpSquatTicks = 4,
            },
            CapsuleHeight = 1.5f,
            HipHeight = 0.5f,
            HurtboxBoneScale = 1.0f,
            IdleAnim = "anim.idle",
            RunAnim = "anim.run",
            HurtboxCapsules = new[] { new HurtboxCapsule(0, 0, 0, 0, 0, 0, 0.9f) },
            HurtboxBoneDefs = new[] { new HurtboxBoneDef("mixamorig:Hips", 0, 0, 0, 0.2f) },
        };
        var baked = new BakedAnimationData
        {
            BoneNames = new[] { "mixamorig:Hips" },
            Animations = new[]
            {
                new BakedAnimationData.BakedAnim
                {
                    Name = "anim.idle",

                    Frames = new[] { new[] { 0f, 0f, 0f } },
                },
                new BakedAnimationData.BakedAnim
                {
                    Name = "anim.run",

                    Frames = new[] { new[] { 0.4f, 0f, 0f } },
                },
            },
        };

        var idleSim = TestHelpers.MakeSim();
        idleSim.RegisterEntity(1, def, new CharacterState
        {
            PX = 0f, PY = 0.75f, PZ = 0f, IsGrounded = true,
            JumpsLeft = def.Movement.MaxJumps,
        }, baked);
        TestHelpers.TickDefault(idleSim, 1);
        Assert.Equal(0.2f, Assert.Single(idleSim.GetLastEntityData()).Radius, 5);

        var runSim = TestHelpers.MakeSim();
        runSim.RegisterEntity(1, def, new CharacterState
        {
            PX = 0f, PY = 0.75f, PZ = 0f, IsGrounded = true,
            JumpsLeft = def.Movement.MaxJumps,
        }, baked);
        runSim.Tick(new Dictionary<ulong, InputState>
        {
            [1] = new InputState { MoveY = 1f },
        });
        Assert.Equal(0.2f, Assert.Single(runSim.GetLastEntityData()).Radius, 5);
    }

    [Fact]
    public void SimTick_SelectsGroundedCrouchTrackAtFrameZero()
    {
        var def = new CharacterDefinition
        {
            Movement = TestHelpers.EngineDef.Movement,
            CapsuleHeight = 1.5f,
            HipHeight = 0.5f,
            HurtboxBoneScale = 1f,
            IdleAnim = "anim.idle",
            CrouchAnim = "anim.crouch",
            HurtboxBoneDefs = new[] { new HurtboxBoneDef("mixamorig:Hips", 0, 0, 0, 0.2f) },
        };
        var baked = new BakedAnimationData
        {
            BoneNames = new[] { "mixamorig:Hips" },
            Animations = new[]
            {
                new BakedAnimationData.BakedAnim
                {
                    Name = "anim.idle",

                    Frames = new[] { new[] { 0f, 0f, 0f } },
                },
                new BakedAnimationData.BakedAnim
                {
                    Name = "anim.crouch",

                    Frames = new[]
                    {
                        new[] { 0f, -0.2f, 0f },
                        new[] { 0f, -0.1f, 0f },
                    },
                },
            },
        };

        var sim = TestHelpers.MakeSim();
        sim.RegisterEntity(1, def, new CharacterState
        {
            PX = 0f,
            PY = 0.75f,
            PZ = 0f,
            IsGrounded = true,
            State = ActionState.Crouching,
            JumpsLeft = def.Movement.MaxJumps,
        }, baked);
        sim.Tick(new Dictionary<ulong, InputState> { [1] = new InputState { Down = true } });
        float firstY = Assert.Single(sim.GetLastEntityData()).PosY;
        sim.Tick(new Dictionary<ulong, InputState> { [1] = new InputState { Down = true } });
        float secondY = Assert.Single(sim.GetLastEntityData()).PosY;

        Assert.Equal(0.3f, firstY, 5);
        Assert.Equal(firstY, secondY, 5);
    }

    [Fact]
    public void UnifiedPose_AppliesOffsetRotatedByFacing()
    {
        var def = new CharacterDefinition
        {
            CapsuleHeight = 1.5f,
            HipHeight = 0.5f,
            HurtboxBoneScale = 1.0f,
            HurtboxBoneDefs = new[]
            {
                new HurtboxBoneDef("mixamorig:Hips", 0.2f, 0.1f, 0f, 0.3f), // offset (x=+0.2, y=+0.1)
            },
        };
        var baked = BakedAnimationData.LoadFromBin(MakeBin(new[] { "mixamorig:Hips" }, (f, bone, axis) => 0f));
        var state = new CharacterState
        {
            PX = 0f, PY = 0.75f, PZ = 0f, // grounded: py = h/2
            FacingYaw = MathF.PI / 2f,
        };

        var resolved = ServerSimulation.BuildEntitiesFromState(state, def, baked, "idle", 0, 0);

        var e = Assert.Single(resolved);
        // Hips at baked (0,0,0) → world (0, HipHeight=0.5, 0).
        // Offset (0.2, 0.1, 0) rotated by yaw PI/2: +x stays +x, +z→ +0.2·sin? —
        // rotate((ox, oz)) = (ox·cos + oz·sin, -ox·sin + oz·cos) with cos=0, sin=1 → (0, -0.2).
        Assert.Equal(0f, e.PosX, 5);
        Assert.Equal(0.6f, e.PosY, 5);
        Assert.Equal(-0.2f, e.PosZ, 5);
        Assert.Equal(0.3f, e.Radius, 5);
    }

    [Fact]
    public void OverriddenDef_FlowsThroughSimTick_HurtboxListReflectsEdits()
    {
        // Simulates the host having applied an override (HurtboxOverride.Apply) before
        // registration: the sim's BuildHurtboxList must resolve the overridden def.
        var baseDef = new CharacterDefinition
        {
            CapsuleHeight = 1.5f,
            HipHeight = 0.5f,
            HurtboxBoneScale = 1.0f,
            Movement = TestHelpers.EngineDef.Movement,
            HurtboxBoneDefs = new[] { new HurtboxBoneDef("mixamorig:Hips", 0, 0, 0, 0.26f) },
        };
        var overridden = HurtboxOverride.Apply(baseDef, new[]
        {
            new HurtboxBoneDef("mixamorig:Hips", 0.3f, 0f, 0f, 0.5f), // radius +0.24, offset +0.3 x
        });
        var baked = BakedAnimationData.LoadFromBin(MakeBin(new[] { "mixamorig:Hips" }, (f, bone, axis) => 0f));

        var sim = TestHelpers.MakeSim();
        sim.RegisterEntity(1, overridden, new CharacterState
        {
            PX = 0f, PY = 0.75f, PZ = 0f, FacingYaw = 0f,
            JumpsLeft = overridden.Movement.MaxJumps,
        }, baked);
        TestHelpers.TickDefault(sim, 1);

        var data = sim.GetLastEntityData();
        var e = Assert.Single(data);
        Assert.Equal(0.5f, e.Radius, 5);      // override radius
        Assert.Equal(0.3f, e.PosX, 5);        // override offset (yaw 0 → +x)
        Assert.Equal(1ul, e.Id);
    }
}
