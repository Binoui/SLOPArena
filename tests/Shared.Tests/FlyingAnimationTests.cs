using System.Collections.Generic;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class FlyingAnimationTests
{
    [Theory]
    [InlineData("anim.test.flying")]
    [InlineData("anim.test.other-flight")]
    public void FlyingHurtboxesUseThePackageLoopRepresentativeFrame(string animation)
    {
        var def = TestHelpers.ResolveSourceDef("manki") with
        {
            FlyingAnim = animation,
            HurtboxBoneScale = 1f,
            HurtboxBoneDefs = new[] { new HurtboxBoneDef("mixamorig:Hips", 0f, 0f, 0f, .2f) },
        };
        var poses = new BakedAnimationData
        {
            BoneNames = new[] { "mixamorig:Hips" },
            Animations = new[]
            {
                new BakedAnimationData.BakedAnim { Name = animation,
                    Frames = new[] { new[] { 0f, -.2f, -.3f }, new[] { 0f, .8f, 1f } } },
                new BakedAnimationData.BakedAnim { Name = def.GetCookedSlotAbility(AbilitySlots.E, true)!
                    .Timeline.Stages[0].AnimationIds[0], Frames = new[] { new[] { 0f, 2f, 3f } } },
            },
        };
        var sim = TestHelpers.MakeSim();
        sim.RegisterEntity(1, def, TestHelpers.PlayerState(20f, 20f) with
        {
            PY = 8f, IsGrounded = false, State = ActionState.Flying,
            Flight = new FlyingState { Active = true, FuelTicksLeft = 180 },
        }, poses);
        for (int tick = 0; tick < 60; tick++)
        {
            sim.Tick(new Dictionary<ulong, InputState>());
            var state = sim.GetState(1);
            var body = Assert.Single(sim.GetLastEntityData(), e => e.Id == 1);
            Assert.Equal(def.BoneYToWorldY(state.PY, -.2f), body.PosY, 5);
            Assert.Equal(state.PZ - .3f, body.PosZ, 5);
        }
    }

    [Fact]
    public void MissingFlyingPoseFallsBackToTheOrdinaryAirPose()
    {
        var def = TestHelpers.ResolveSourceDef("manki") with
        {
            FlyingAnim = "anim.test.unavailable", HurtboxBoneScale = 1f,
            HurtboxBoneDefs = new[] { new HurtboxBoneDef("mixamorig:Hips", 0f, 0f, 0f, .2f) },
        };
        var poses = new BakedAnimationData
        {
            BoneNames = new[] { "mixamorig:Hips" },
            Animations = new[] { new BakedAnimationData.BakedAnim { Name = def.FallAnim,
                Frames = new[] { new[] { 0f, -.4f, 0f } } } },
        };
        var sim = TestHelpers.MakeSim();
        sim.RegisterEntity(1, def, TestHelpers.PlayerState(20f, 20f) with
        {
            PY = 8f, IsGrounded = false, State = ActionState.Flying,
            Flight = new FlyingState { Active = true, FuelTicksLeft = 180 },
        }, poses);
        sim.Tick(new Dictionary<ulong, InputState>());
        var body = Assert.Single(sim.GetLastEntityData(), e => e.Id == 1);
        Assert.Equal(def.BoneYToWorldY(sim.GetState(1).PY, -.4f), body.PosY, 5);
    }
}
