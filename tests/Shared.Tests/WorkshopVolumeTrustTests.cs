using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class WorkshopVolumeTrustTests
{
    [Theory]
    [InlineData(CharacterCookProfile.TrustedBuiltIn)]
    [InlineData(CharacterCookProfile.Workshop)]
    public void HostAdmissionProfileSurvivesRuntimeConversionAndBothForms(CharacterCookProfile profile)
    {
        var loaded = CookedCharacterPackageLoader.LoadFiles(CookedPackageEdits.Shipped("nyx"),
            CookedPackageEdits.Roster.ResolvePackage("nyx")!.Requirement, profile);
        Assert.True(loaded.IsValid, string.Join(";", loaded.Diagnostics));
        var definition = loaded.ToCharacterDefinition();
        Assert.Equal(profile, definition.AdmissionProfile);
        Assert.Equal(profile, definition.ForForm(1).AdmissionProfile);
        Assert.Equal(profile, definition.ForForm(1).ForForm(0).AdmissionProfile);
        Assert.Equal(CharacterCookProfile.Workshop,
            CookedCharacterRuntimeAdapter.ToCharacterDefinition(loaded.Package!).AdmissionProfile);
    }

    [Theory]
    [InlineData(false, 12)]
    [InlineData(true, 8)]
    public void TrustedMoveSpawnsEverythingBesideACappedWorkshopOwnerAndReplaysIdentically(bool tracked, int workshopLimit)
    {
        var loaded = LoadFightGuy();
        var trusted = BurstDefinition(loaded, tracked);
        var workshop = trusted with { AdmissionProfile = CharacterCookProfile.Workshop };
        var sim = Match(trusted, workshop, loaded.BakedAnimation!);
        sim.Tick(new Dictionary<ulong, InputState> { [2] = new() { ActiveSlot = AbilitySlots.Normal1 } });
        Assert.Equal(workshopLimit, sim.Resolver.GetActiveHitboxes().Count(h => h.OwnerId == 2));
        Assert.True(sim.Resolver.RefusedSpawns > 0);
        var beforeTrustedMove = sim.CaptureSnapshot();

        var restored = Match(trusted, workshop, loaded.BakedAnimation!);
        restored.RestoreSnapshot(beforeTrustedMove);
        long refused = sim.Resolver.RefusedSpawns;
        sim.Tick(new Dictionary<ulong, InputState> { [1] = new() { ActiveSlot = AbilitySlots.Normal1 } });
        restored.Tick(new Dictionary<ulong, InputState> { [1] = new() { ActiveSlot = AbilitySlots.Normal1 } });
        Assert.Equal(80, sim.Resolver.GetActiveHitboxes().Count(h => h.OwnerId == 1));
        Assert.Equal(80 + workshopLimit, sim.Resolver.PeakLiveVolumes);
        Assert.Equal(refused, sim.Resolver.RefusedSpawns);
        Assert.True(sim.CaptureSnapshot().ContentEquals(restored.CaptureSnapshot()));

        // A trusted definition attached to an emission cannot override its registered Workshop owner.
        var denied = new Hitbox { OwnerId = 2, Def = trusted, DurationTicks = 600 };
        sim.Resolver.Spawn(denied);
        restored.Resolver.Spawn(denied);
        Assert.Equal(refused + 1, sim.Resolver.RefusedSpawns);
        Assert.Equal(workshopLimit, sim.Resolver.GetActiveHitboxes().Count(h => h.OwnerId == 2));
        Assert.True(sim.CaptureSnapshot().ContentEquals(restored.CaptureSnapshot()));
    }

    [Fact]
    public void TrustedVolumesConsumeWorldCapacityForOtherWorkshopOwners()
    {
        var loaded = LoadFightGuy();
        var trusted = loaded.ToCharacterDefinition();
        var sim = Match(trusted, trusted with { AdmissionProfile = CharacterCookProfile.Workshop }, loaded.BakedAnimation!);
        for (int n = 0; n < 64; n++)
            sim.Resolver.Spawn(new Hitbox { OwnerId = 1, FreezesOwner = true, DurationTicks = 600 });
        sim.Resolver.Spawn(new Hitbox { OwnerId = 2, FreezesOwner = true, DurationTicks = 600 });
        Assert.Equal(1, sim.Resolver.RefusedSpawns);
        Assert.Equal(64, sim.Resolver.PeakLiveVolumes);
        sim.Resolver.Spawn(new Hitbox { OwnerId = 1, DurationTicks = 600 });
        Assert.Equal(65, sim.Resolver.PeakLiveVolumes);
        Assert.Equal(1, sim.Resolver.RefusedSpawns);
    }

    [Fact]
    public void TrustedOwnerAdmissionSurvivesRemovalForLingeringEmissions()
    {
        var loaded = LoadFightGuy();
        var sim = TestHelpers.MakeSim();
        sim.RegisterEntity(1, loaded.ToCharacterDefinition(), TestHelpers.PlayerState());
        sim.RemoveEntity(1);
        for (int n = 0; n < 80; n++) sim.Resolver.Spawn(new Hitbox { OwnerId = 1, DurationTicks = 600 });
        Assert.Equal(80, sim.Resolver.GetActiveHitboxes().Count);
        Assert.Equal(0, sim.Resolver.RefusedSpawns);
    }

    private static CookedCharacterPackageLoadResult LoadFightGuy()
    {
        var loaded = CookedCharacterPackageLoader.LoadFiles(CookedPackageEdits.Shipped("fightguy"),
            CookedPackageEdits.Roster.ResolvePackage("fightguy")!.Requirement, CharacterCookProfile.TrustedBuiltIn);
        Assert.True(loaded.IsValid, string.Join(";", loaded.Diagnostics));
        return loaded;
    }

    private static CharacterDefinition BurstDefinition(CookedCharacterPackageLoadResult loaded, bool tracked)
    {
        var definition = loaded.ToCharacterDefinition();
        var slots = definition.CookedSlots!.ToArray();
        var hitbox = loaded.Package!.Definition.AllSlots.SelectMany(s => s.Timeline.Stages)
            .SelectMany(s => s.Operations).OfType<CookedSpawnHitboxOperation>().First().Hitbox
            with { OffsetY = 100, DurationTicks = 600 };
        var projectile = FingerGunsTests.Parameters.Projectile with { LaunchOffsetY = 100, Speed = 0, MaxFlightTicks = 600 };
        var operations = Enumerable.Range(0, 80).Select(_ => tracked
            ? (CookedTimelineOperation)new CookedSpawnHitboxOperation(0, AuthoringUnit.Ticks, hitbox)
            : new CookedSpawnProjectileOperation(0, AuthoringUnit.Ticks, projectile)).ToArray();
        slots[0] = new CookedSlotDefinition(0, "ground.1", false, "Host burst fixture", "", "",
            AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, 0, false, false,
            new CookedTimeline(new[] { new CookedStage(600, 0, 0, 0, 0, Array.Empty<string>(), operations) }));
        return definition with { CookedSlots = slots };
    }

    private static ServerSimulation Match(CharacterDefinition trusted, CharacterDefinition workshop, BakedAnimationData baked)
    {
        var sim = TestHelpers.MakeSim();
        sim.RegisterEntity(1, trusted, TestHelpers.PlayerState(10, 10), baked);
        sim.RegisterEntity(2, workshop, TestHelpers.PlayerState(30, 30), baked);
        return sim;
    }
}
