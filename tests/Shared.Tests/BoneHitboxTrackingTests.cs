using System;
using System.Collections.Generic;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Per-tick bone tracking: bone-attached melee hitboxes re-resolve their bone
/// position every tick instead of freezing at their spawn position.
/// </summary>
public class BoneHitboxTrackingTests
{
    [Fact]
    public void BoneAttachedHitbox_RepositionsEachTick()
    {
        var baked = TestHelpers.LoadBakedData(TestHelpers.FightGuyDef);
        Assert.NotNull(baked); // loaded from the admitted cooked FightGuy pose package

        var sim = TestHelpers.MakeSim();
        var player = TestHelpers.PlayerState();
        player.PY = TestHelpers.GroundPY(TestHelpers.FightGuyDef); // grounded
        player.FacingYaw = 0f;
        sim.RegisterEntity(1, TestHelpers.FightGuyDef, player, baked);

        // Slot1 "Low Kick": RightFoot hitbox, TriggerTick 4, DurationTicks 5.
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: AbilitySlots.Normal1) } });

        // Advance until the hitbox spawns (bounded), then capture position.
        var active = sim.Resolver.GetActiveHitboxes();
        for (int i = 0; i < 12 && active.Count == 0; i++)
        {
            sim.Tick(new() { { 1, default } });
            active = sim.Resolver.GetActiveHitboxes();
        }
        Assert.NotEmpty(active);
        var p1 = (active[0].X, active[0].Y, active[0].Z);

        sim.Tick(new() { { 1, default } });
        var after = sim.Resolver.GetActiveHitboxes();
        Assert.NotEmpty(after);
        var p2 = (after[0].X, after[0].Y, after[0].Z);

        Assert.NotEqual(p1, p2); // hitbox re-resolved to the moving foot (was static pre-fix)
    }
}
