using SlopArena.Shared;
using SlopArena.Shared.Tests;
using Xunit;

namespace SlopArena.Tests;

public sealed class CharacterDefinitionImmutabilityTests
{
    [Fact]
    public void ConstructionBuffersCannotChangeAdmittedDefinitionOrIndependentReplay()
    {
        var capsules = new[] { new HurtboxCapsule(0, 0, 0, 0, 1, 0, .3f) };
        var bones = new[] { new HurtboxBoneDef("hips", 0, 0, 0, .4f) };
        var clips = new[] { new AnimationClipConfig { Name = "idle", FrameRateOverride = 30 } };
        var slots = TestHelpers.EngineDef.CookedSlots!.ToArray();
        var def = new CharacterDefinition {
            Movement = TestHelpers.EngineDef.Movement, CapsuleHeight = 1.8f,
            HurtboxCapsules = capsules, HurtboxBoneDefs = bones, ClipOverrides = clips, CookedSlots = slots,
        };
        var first = new ServerSimulation(TestHelpers.TestArena());
        var second = new ServerSimulation(TestHelpers.TestArena());
        var state = TestHelpers.PlayerState(); state.PY = .9f;
        first.RegisterEntity(1, def, state); second.RegisterEntity(1, def, state);
        var before = first.CaptureSnapshot();
        capsules[0].Radius = 9; bones[0].Radius = 9; clips[0].FrameRateOverride = 99; slots[0] = slots[1];
        RejectMutation(def.HurtboxCapsules!, default);
        RejectMutation(def.HurtboxBoneDefs!, default);
        RejectMutation(def.ClipOverrides!, default);
        RejectMutation(def.CookedSlots!, slots[1]);
        var movement = def.Movement;
        movement.Gravity = 999;
        Assert.Equal(TestHelpers.EngineDef.Movement.Gravity, second.GetDefinition(1).Movement.Gravity);
        Assert.Equal(.3f, first.GetDefinition(1).HurtboxCapsules![0].Radius);
        Assert.Equal(.4f, second.GetDefinition(1).HurtboxBoneDefs![0].Radius);
        Assert.Equal(30, first.GetDefinition(1).ClipOverrides![0].FrameRateOverride);
        Assert.Equal("ground.1", first.GetDefinition(1).CookedSlots![0].Id);
        Assert.Same(def, first.GetDefinition(1)); Assert.Same(def, second.GetDefinition(1));
        for(int i=0;i<8;i++) first.Tick(new() { [1] = TestHelpers.Input(moveY: 1) });
        second.RestoreSnapshot(before);
        for(int i=0;i<8;i++) second.Tick(new() { [1] = TestHelpers.Input(moveY: 1) });
        Assert.Equal(first.CaptureSnapshot().Data, second.CaptureSnapshot().Data);
    }
    [Fact]
    public void TimelineConstructionBuffersCannotChangeContactsAcrossIndependentReplay()
    {
        var hit = new CookedHitbox(AuthoringHitboxShape.Sphere, 1f, 0, 0, .5f, 0, 0, 0,
            null, null, 7f, 20f, 1f, 1f, 0, 1, true, 0);
        var operations = new CookedTimelineOperation[] { new CookedSpawnHitboxOperation(1, AuthoringUnit.Meters, hit) };
        var animations = new[] { "attack" };
        var stages = new[] { new CookedStage(10, 0, 0, 0, 0, animations, operations) };
        var slots = TestHelpers.EngineDef.CookedSlots!.ToArray();
        slots[0] = new CookedSlotDefinition(0, "ground.1", false, "Contact", "", "",
            AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, 0, false, false, new CookedTimeline(stages));
        var definition = TestHelpers.EngineDef with { CookedSlots = slots };
        ServerSimulation Create()
        {
            var sim = new ServerSimulation(TestHelpers.TestArena());
            var state = TestHelpers.PlayerState(10, 10); state.PY = TestHelpers.GroundPY(definition);
            sim.RegisterEntity(1, definition, state);
            state.EntityId = 2; state.PZ += 1;
            sim.RegisterEntity(2, TestHelpers.EngineDef, state);
            return sim;
        }
        var first = Create(); var second = Create();
        var checkpoint = first.CaptureSnapshot();
        void Replay(ServerSimulation sim)
        {
            for (int tick = 0; tick < 5; tick++)
                sim.Tick(new() { [1] = new InputState { ActiveSlot = tick == 0 ? AbilitySlots.Slot1 : (byte)0 } });
        }
        Replay(first);
        Assert.Equal(7, first.GetState(2).DamagePercent);
        operations[0] = new CookedSpawnHitboxOperation(0, AuthoringUnit.Meters, hit with { Damage = 99 });
        animations[0] = "changed";
        stages[0] = new CookedStage(200, 0, 0, 0, 0, animations, operations);
        slots[0] = slots[1];
        RejectMutation(definition.CookedSlots![0].Timeline.Stages, stages[0]);
        RejectMutation(definition.CookedSlots[0].Timeline.Stages[0].Operations, operations[0]);
        RejectMutation(definition.CookedSlots[0].Timeline.Stages[0].AnimationIds, "changed");
        second.RestoreSnapshot(checkpoint);
        Replay(second);
        Assert.Equal(7, second.GetState(2).DamagePercent);
        Assert.Equal(first.CaptureSnapshot().Data, second.CaptureSnapshot().Data);
        Assert.Same(definition, second.GetDefinition(1));
    }

    [Fact]
    public void ReplacementDefinitionHasIndependentIdentityAndRetainsOriginalContent()
    {
        var definition = TestHelpers.EngineDef;
        var replacement = definition with { DisplayName = "Replacement" };
        Assert.False(definition.Equals(replacement));
        Assert.False(definition == replacement);
        Assert.Equal(2, new HashSet<CharacterDefinition> { definition, replacement }.Count);
        Assert.NotEqual("Replacement", definition.DisplayName);
        Assert.Same(definition.CookedSlots![0], replacement.CookedSlots![0]);
    }

    private static void RejectMutation<T>(IReadOnlyList<T> values, T replacement)
    {
        Assert.False(values is T[]);
        if (values is IList<T> list)
            Assert.Throws<NotSupportedException>(() => list[0] = replacement);
    }

}
