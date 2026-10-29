using System;
using System.Linq;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Baked points are hips-relative. A bake with the synthetic <c>_hips_offset</c> point
/// places the hips where the animation draws them; legacy bakes keep the fixed
/// <c>HipHeight</c> pivot. Hitboxes, hurtboxes and the Ability Lab share one resolver.
/// </summary>
public class BakedHipsOffsetTests
{
    private const float Yaw = 0.6f;

    // Drawn hips: 5 cm right, 40 cm below the capsule centre, 20 cm forward.
    private static readonly float[] HipsOffset = { 0.05f, -0.4f, 0.2f };

    private static BakedAnimationData Bake(bool withHipsOffset)
    {
        var names = withHipsOffset
            ? new[] { "mixamorig:Hips", "mixamorig:Head", "_weapon_hilt", "_weapon_tip", BakedAnimationData.HipsOffsetPointName }
            : new[] { "mixamorig:Hips", "mixamorig:Head", "_weapon_hilt", "_weapon_tip" };
        float[] Frame(float tipForward)
        {
            var frame = new float[names.Length * 3];
            frame[4] = 0.9f; frame[5] = 0.1f;              // Head
            frame[7] = 0.6f; frame[8] = 0.3f;              // _weapon_hilt
            frame[10] = 0.5f; frame[11] = tipForward;      // _weapon_tip
            if (withHipsOffset) Array.Copy(HipsOffset, 0, frame, 12, 3);
            return frame;
        }
        return new BakedAnimationData
        {
            BoneNames = names,
            Animations = new[]
            {
                new BakedAnimationData.BakedAnim
                {
                    Name = "attack",
                    Frames = new[] { Frame(1.2f), Frame(1.6f) },
                },
            },
        };
    }

    private static CharacterDefinition Def()
    {
        var def = TestHelpers.EngineDef;
        def = def with { HipHeight = 0.5f };
        def = def with { HurtboxBoneScale = 0.85f };
        var slots = def.CookedSlots!.ToArray();
        slots[0] = new CookedSlotDefinition(0, "ground.1", false, "Hips offset test", "", "",
            AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, 0, false, false,
            new CookedTimeline(new[]
            {
                new CookedStage(2, 0, 0, 0, 0, new[] { "attack" }, Array.Empty<CookedTimelineOperation>()),
            }));
        def = def with { CookedSlots = slots };

        def = def with { HurtboxBoneDefs = new[]
        {
            new HurtboxBoneDef("mixamorig:Hips", 0f, 0f, 0f, 0.26f),
            new HurtboxBoneDef("mixamorig:Head", 0f, 0f, 0f, 0.22f),
        } };
        return def;
    }

    private static CharacterState State(ushort elapsed = 0) => new()
    {
        PX = 1f, PY = 0.85f, PZ = -2f, FacingYaw = Yaw, AttackElapsedTicks = elapsed,
    };

    /// <summary>Expected world point: capsule centre + facing-rotated local offset.</summary>
    private static (float x, float y, float z) World(in CharacterState s, float lx, float ly, float lz)
    {
        float cos = MathF.Cos(s.FacingYaw), sin = MathF.Sin(s.FacingYaw);
        return (s.PX + lx * cos + lz * sin, s.PY + ly, s.PZ - lx * sin + lz * cos);
    }

    private static void AssertPoint((float x, float y, float z) expected, float x, float y, float z)
    {
        TestHelpers.AssertNear(expected.x, x, 0.0001f);
        TestHelpers.AssertNear(expected.y, y, 0.0001f);
        TestHelpers.AssertNear(expected.z, z, 0.0001f);
    }

    [Fact]
    public void HipsOffsetIndex_FollowsBoneNames()
    {
        var baked = Bake(withHipsOffset: true);
        Assert.Equal(4, baked.HipsOffsetIndex);
        baked = new BakedAnimationData { BoneNames = new[] { "mixamorig:Hips" } };
        Assert.Equal(-1, baked.HipsOffsetIndex);
    }

    [Fact]
    public void LegacyBake_KeepsFixedHipPivot()
    {
        var def = Def();
        var s = State();
        Assert.True(HitboxGeometry.BakedPointToWorld(s, def, Bake(withHipsOffset: false), 0, 0, 1, def.HurtboxBoneScale,
            out float x, out float y, out float z));

        float cos = MathF.Cos(Yaw), sin = MathF.Sin(Yaw);
        float bz = 0.1f * def.HurtboxBoneScale;
        TestHelpers.AssertNear(s.PX + bz * sin, x, 0.0001f);
        Assert.Equal(def.BoneYToWorldY(s.PY, 0.9f * def.HurtboxBoneScale), y);
        TestHelpers.AssertNear(s.PZ + bz * cos, z, 0.0001f);
    }

    [Fact]
    public void HipsOffset_PlacesHipsWhereDrawn()
    {
        var def = Def();
        var s = State();
        var baked = Bake(withHipsOffset: true);

        Assert.True(HitboxGeometry.BakedPointToWorld(s, def, baked, 0, 0, 0, def.HurtboxBoneScale,
            out float x, out float y, out float z));
        AssertPoint(World(s, HipsOffset[0], HipsOffset[1], HipsOffset[2]), x, y, z);

        // Resolving the synthetic point itself also lands on the drawn hips.
        Assert.True(HitboxGeometry.BakedPointToWorld(s, def, baked, 0, 0, baked.HipsOffsetIndex, 1f,
            out x, out y, out z));
        AssertPoint(World(s, HipsOffset[0], HipsOffset[1], HipsOffset[2]), x, y, z);
    }

    [Fact]
    public void HipsOffset_ShrinksBodyPointsTowardDrawnHips()
    {
        var def = Def();
        var s = State();
        Assert.True(HitboxGeometry.BakedPointToWorld(s, def, Bake(withHipsOffset: true), 0, 0, 1, def.HurtboxBoneScale,
            out float x, out float y, out float z));

        float k = def.HurtboxBoneScale;
        AssertPoint(World(s, HipsOffset[0], HipsOffset[1] + 0.9f * k, HipsOffset[2] + 0.1f * k), x, y, z);
    }

    [Fact]
    public void SwordHitbox_FollowsDrawnBladeAtFullScale()
    {
        var def = Def();
        var baked = Bake(withHipsOffset: true);
        var evt = new HitboxEvent { BoneName = "_weapon_hilt", EndBoneName = "_weapon_tip", Shape = HitboxShape.Capsule };

        // Slot1 (wire slot 2): elapsed 1 of its 2-tick stage over 2 frames selects
        // frame 1, where the tip is 1.6 m forward.
        var s = State(elapsed: 1);
        HitboxGeometry.ResolvePositions(s, evt, baked, def, def?.GetCookedSlotAbility((byte)((2) + 1), false), 0, out float hx, out float hy, out float hz, out float tx, out float ty, out float tz);

        AssertPoint(World(s, HipsOffset[0], HipsOffset[1] + 0.6f, HipsOffset[2] + 0.3f), hx, hy, hz);
        AssertPoint(World(s, HipsOffset[0], HipsOffset[1] + 0.5f, HipsOffset[2] + 1.6f), tx, ty, tz);
    }

    [Fact]
    public void Hurtboxes_FollowDrawnHips()
    {
        var def = Def();
        var s = State();
        var hurtboxes = ServerSimulation.BuildEntitiesFromState(s, def, Bake(withHipsOffset: true), "attack", 0, 3);

        Assert.Equal(2, hurtboxes.Count);
        float k = 1f;
        var hips = World(s, HipsOffset[0], HipsOffset[1], HipsOffset[2]);
        var head = World(s, HipsOffset[0], HipsOffset[1] + 0.9f * k, HipsOffset[2] + 0.1f * k);
        AssertPoint(hips, hurtboxes[0].PosX, hurtboxes[0].PosY, hurtboxes[0].PosZ);
        AssertPoint(head, hurtboxes[1].PosX, hurtboxes[1].PosY, hurtboxes[1].PosZ);
    }
}
