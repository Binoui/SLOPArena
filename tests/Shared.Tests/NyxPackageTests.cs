using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SlopArena.Shared.Tests;

public class NyxPackageTests
{
    [Theory]
    [InlineData(AbilitySlots.Slot1, false)]
    [InlineData(AbilitySlots.Slot3, false)]
    [InlineData(AbilitySlots.Slot4, false)]
    [InlineData(AbilitySlots.A, false)]
    [InlineData(AbilitySlots.E, false)]
    [InlineData(AbilitySlots.R, false)]
    [InlineData(AbilitySlots.F, false)]
    [InlineData(AbilitySlots.Slot1, true)]
    [InlineData(AbilitySlots.Slot3, true)]
    [InlineData(AbilitySlots.Slot4, true)]
    [InlineData(AbilitySlots.A, true)]
    [InlineData(AbilitySlots.E, true)]
    [InlineData(AbilitySlots.R, true)]
    [InlineData(AbilitySlots.F, true)]
    public void SavedPackage_AllSlotsActivateAndRecover(byte slot, bool airborne)
    {
        var content = BuiltInContentResolver.Resolve(CharacterClass.Nyx);
        var def = content.Definition;
        Assert.NotNull(content.BakedAnimation);
        var sim = TestHelpers.MakeSim();
        var actor = TestHelpers.PlayerState(x: 20, z: 10) with
        {
            PY = TestHelpers.GroundPY(def) + (airborne ? 3f : 0f),
            IsGrounded = !airborne,
        };
        sim.RegisterEntity(1, def, actor, content.BakedAnimation);
        sim.Tick(new Dictionary<ulong, InputState> { [1] = new() { ActiveSlot = slot } });
        Assert.NotNull(sim.GetActiveAbility(1));
        for (int tick = 0; tick < 360; tick++) sim.Tick(new Dictionary<ulong, InputState>());
        Assert.Null(sim.GetActiveAbility(1));
        var after = sim.GetState(1);
        Assert.True(float.IsFinite(after.PX) && float.IsFinite(after.PY) && float.IsFinite(after.PZ));
    }

    [Theory]
    [InlineData(1.2f, false, true)]
    [InlineData(1.2f, true, true)]
    [InlineData(12f, false, false)]
    public void SavedPackage_FirstNormalHitsBlocksAndMisses(float distance, bool shielding, bool contact)
    {
        var content = BuiltInContentResolver.Resolve(CharacterClass.Nyx);
        var def = content.Definition;
        var sim = TestHelpers.MakeSim();
        sim.RegisterEntity(1, def, TestHelpers.PlayerState(x: 20, z: 10) with
            { PY = TestHelpers.GroundPY(def) }, content.BakedAnimation);
        sim.RegisterEntity(100, def, TestHelpers.NpcState(x: 20, z: 10 + distance) with
            { PY = TestHelpers.GroundPY(def), FacingYaw = MathF.PI }, content.BakedAnimation);
        var hits = new List<SpellResolver.HitResult>();
        for (int tick = 0; tick < 90; tick++)
        {
            sim.Tick(new Dictionary<ulong, InputState>
            {
                [1] = new() { ActiveSlot = tick == 0 ? AbilitySlots.Slot1 : AbilitySlots.None },
                [100] = new() { FacingYaw = -18000, ShieldHeld = shielding, ShieldPressed = shielding && tick == 0 },
            });
            hits.AddRange(sim.LastTickHits.Where(h => h.OwnerEntityId == 1 && h.TargetEntityId == 100));
        }
        Assert.Equal(contact, hits.Count > 0);
        if (contact) Assert.All(hits, h => Assert.Equal(shielding, h.Blocked));
        if (contact && !shielding) Assert.True(sim.GetState(100).DamagePercent > 0);
    }
}
