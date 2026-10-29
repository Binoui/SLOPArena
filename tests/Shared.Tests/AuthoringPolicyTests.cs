using Xunit;
using SlopArena.Shared;

namespace SlopArena.Shared.Tests;

/// <summary>
/// ADR-0021 authoring policy (§1 IASA, §3 landing lag + auto-cancel): every standard
/// normal authors an IASA early-out, and every standard aerial additionally authors its
/// landing-lag + both auto-cancel windows. These are the migration-safe contracts the kit
/// data must uphold — a designer deleting an IASA or landing-lag declaration fails here.
/// (Specials/recovery/charge retain their ADR-0021 default policy; data migrations are not
/// asserted here.)
/// </summary>
public class AuthoringPolicyTests
{
    private static readonly (string Name, CharacterDefinition Def)[] Kits =
    {
        ("FightGuy", TestHelpers.FightGuyDef),
        ("Wibou", TestHelpers.WibouDef),
        ("Manki", TestHelpers.MankiDef),
    };

    [Fact]
    public void EveryGroundNormal_AuthorsIasa()
    {
        foreach (var (name, def) in Kits)
        {
            var ground = def.GetCookedSlotAbility(AbilitySlots.Slot1, airborne: false);
            Assert.NotNull(ground);
            var stage = ground!.Timeline.Stages[0];
            Assert.True(stage.IasaTicks > 0, $"{name} ground normal must author IasaTicks (ADR-0021 §1)");
            Assert.True(stage.IasaTicks < stage.DurationTicks,
                $"{name} ground normal IasaTicks must precede the stage end");
        }
    }

    [Fact]
    public void EveryAirNormal_AuthorsIasaAndLandingLag()
    {
        foreach (var (name, def) in Kits)
        {
            var air = def.GetCookedSlotAbility(AbilitySlots.Slot1, airborne: true);
            Assert.NotNull(air);
            AssertAirNormal(air!.Timeline.Stages[0], name);
        }
    }

    [Fact]
    public void FightGuy_AirNormals_AuthorLandingLag()
    {
        // FightGuy is the only kit with a full normal tier (keys 1, 3, 4) — every air variant
        // must carry the same landing-lag + auto-cancel declarations as the ground normal.
        var def = TestHelpers.FightGuyDef;
        AssertAirNormal(def.GetCookedSlotAbility(AbilitySlots.Slot1, airborne: true)!.Timeline.Stages[0], "Slot1 (Floating Kick)");
        AssertAirNormal(def.GetCookedSlotAbility(AbilitySlots.Slot3, airborne: true)!.Timeline.Stages[0], "Slot3 (Flip Kick)");
        AssertAirNormal(def.GetCookedSlotAbility(AbilitySlots.Slot4, airborne: true)!.Timeline.Stages[0], "Slot4 (Air Smash)");
    }

    private static void AssertAirNormal(CookedStage stage, string label)
    {
        Assert.True(stage.IasaTicks > 0, $"{label} must author IasaTicks");
        Assert.True(stage.LandingLagTicks > 0, $"{label} must author LandingLagTicks");
        Assert.True(stage.AutoCancelBeforeTicks > 0, $"{label} must author AutoCancelBeforeTicks");
        Assert.True(stage.AutoCancelAfterTicks > 0, $"{label} must author AutoCancelAfterTicks");
    }
}
