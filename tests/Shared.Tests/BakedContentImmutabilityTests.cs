using SlopArena.Shared;
using SlopArena.Shared.Tests;
using Xunit;

namespace SlopArena.Tests;

public sealed class BakedContentImmutabilityTests
{
    [Fact]
    public void ChangingCapabilityTierBuffersCannotRetuneAnAdmittedMove()
    {
        var slot = TestHelpers.MankiDef.GetCookedSlotAbility(AbilitySlots.F, false)!;
        var parameters = slot.Timeline.Stages.SelectMany(stage => stage.Operations)
            .OfType<CookedStartCapabilityOperation>()
            .Select(operation => operation.Parameters)
            .OfType<CookedMankiAerosolInfernoCapabilityParameters>().Single();
        var tiers = new[] { new MankiAerosolChargeTier(1, 7f, 1f) };
        var isolated = parameters with { ChargeTiers = tiers };

        tiers[0] = new MankiAerosolChargeTier(1, 99f, 1f);

        Assert.Equal(7f, isolated.GetChargeTier(1).Damage);
    }

    [Fact]
    public void ChangingConstructionBuffersCannotChangeAnAdmittedPose()
    {
        var names = new[] { "hand" };
        var frame = new[] { 1f, 2f, 3f };
        var frames = new[] { frame };
        var animations = new[] { new BakedAnimationData.BakedAnim { Name = "strike", Frames = frames } };
        var baked = new BakedAnimationData { BoneNames = names, Animations = animations };

        names[0] = "changed";
        frame[0] = 99;
        frames[0] = new[] { 20f, 30f, 40f };
        animations[0] = new BakedAnimationData.BakedAnim { Name = "changed", Frames = frames };

        Assert.Equal(0, baked.FindBoneIndex("hand"));
        Assert.Equal(0, baked.FindAnimIndex("strike"));
        Assert.True(baked.TryGetPoint(0, 0, 0, out var x, out var y, out var z));
        Assert.Equal((1f, 2f, 3f), (x, y, z));
    }

    [Fact]
    public void PublishedPoseCollectionsDoNotExposeTheirBackingArrays()
    {
        var baked = new BakedAnimationData {
            BoneNames = new[] { "hand" },
            Animations = new[] { new BakedAnimationData.BakedAnim { Name = "strike", Frames = new[] { new[] { 1f, 2f, 3f } } } }
        };

        Assert.False(baked.BoneNames is string[]);
        Assert.False(baked.Animations is BakedAnimationData.BakedAnim[]);
    }
}
