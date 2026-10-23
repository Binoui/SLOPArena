using System.Text.Json.Nodes;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class ArcadeReplayTests
{
    [Fact]
    public void CountdownKeepsTheSpawnAndKitUntouchedAndServesExactlyAtGo()
    {
        var arena = SlopballTests.Court(); var sim = new ServerSimulation(arena, NoWinMatchRule.Instance);
        var def = TestHelpers.FightGuyDef;
        var initial = TestHelpers.PlayerState(); initial.PZ = -3;
        initial.SetCooldown(AbilitySlots.E, 37);
        sim.RegisterEntity(1, def, initial);
        sim.BeginSlopballAttempt(180);
        var held = new Dictionary<ulong, InputState> { [1] = new()
            { MoveX = 1, MoveY = 1, ActiveSlot = AbilitySlots.A, IsAiming = true, Jump = true, JumpHeld = true } };
        for (int i = 0; i < 179; i++) sim.Tick(held);
        Assert.Equal(SlopballPhase.Countdown, sim.Slopball!.Value.Phase);
        Assert.Equal(1, sim.Slopball.Value.CountdownTicks);
        Assert.Equal(initial, sim.GetState(1));
        Assert.Equal(0u, sim.SimulationTick);
        Assert.False(sim.ArcadeBall!.Value.Active);
        Assert.Empty(sim.Resolver.GetActiveHitboxes());
        sim.Tick(held);
        Assert.Equal(SlopballPhase.Live, sim.Slopball.Value.Phase);
        Assert.Equal(0u, sim.SimulationTick);
        Assert.Equal(initial, sim.GetState(1));
        Assert.Equal(arena.Arcade!.Ball.Spawn, sim.ArcadeBall.Value.Position);
        Assert.Equal(SlopballEventKind.Served, Assert.Single(sim.LastTickSlopballEvents).Kind);
        for (int i = 0; i < 3; i++) sim.Tick(held);
        Assert.Equal(0, sim.GetState(1).ChargeTicks);
        Assert.Equal(initial.PX, sim.GetState(1).PX);
        Assert.Equal(0, sim.GetState(1).AttackSlot);
        sim.Tick(new() { [1] = default });
        sim.Tick(new() { [1] = new() { MoveX = 1 } });
        Assert.True(sim.GetState(1).VX > 0);
        Assert.Throws<InvalidOperationException>(() => sim.BeginSlopballAttempt(180));
    }

    [Fact]
    public void CountdownCannotBeAddedToAnOrdinaryOrDirtyMatchAndStopFreezesIt()
    {
        Assert.Throws<InvalidOperationException>(() => TestHelpers.MakeSim().BeginSlopballAttempt(180));
        var sim = new ServerSimulation(SlopballTests.Court(), NoWinMatchRule.Instance);
        sim.RegisterEntity(1, TestHelpers.EngineDef, TestHelpers.PlayerState() with { PZ = -3 });
        Assert.Throws<ArgumentOutOfRangeException>(() => sim.BeginSlopballAttempt(0));
        sim.BeginSlopballAttempt(10);
        sim.Tick(new()); sim.StopSlopball();
        var stopped = sim.Slopball!.Value;
        for (int i = 0; i < 20; i++) sim.Tick(new());
        Assert.Equal(stopped, sim.Slopball.Value);
        Assert.False(sim.ArcadeBall!.Value.Active);
        Assert.Equal(0u, sim.SimulationTick);
    }

    [Fact]
    public void PersonalBestsPersistPerFighterArchiveGameplayChangesAndRestoreOldCategories()
    {
        var book = new ArcadeRecordBook(); var key = Key("fightguy", 'a');
        var first = book.BankSlopball(key, 7, Provenance, DateTime.UtcNow);
        Assert.Null(first.PreviousBest); Assert.Equal(7ul, first.PersonalBest);
        var lower = book.BankSlopball(key, 3, Provenance, DateTime.UtcNow);
        Assert.False(lower.Improved); Assert.Equal(7ul, lower.PersonalBest);
        var higher = book.BankSlopball(key, 9, Provenance, DateTime.UtcNow);
        Assert.True(higher.Improved); Assert.Equal(7ul, higher.PreviousBest);
        book.BankSlopball(Key("manki", 'a'), 4, Provenance, DateTime.UtcNow);
        book.BankSlopball(Key("fightguy", 'b'), 2, Provenance, DateTime.UtcNow);
        book = ArcadeRecordBook.Deserialize(book.Serialize());
        Assert.Equal(3, book.Records.Count);
        Assert.True(book.Records.Single(x => x.Key == key).Archived);
        Assert.Equal(9ul, book.Best(key));
        Assert.Equal(4ul, book.Best(Key("manki", 'a')));
        book.BankSlopball(key, 1, Provenance, DateTime.UtcNow);
        Assert.False(book.Records.Single(x => x.Key == key).Archived);
        Assert.True(book.Records.Single(x => x.Key == Key("fightguy", 'b')).Archived);
        Assert.Equal(9ul, book.Best(key));
    }

    [Fact]
    public void RecordsKeepExactLargeCountsAndRejectCorruptOrDuplicateData()
    {
        var book = new ArcadeRecordBook(); var key = Key("fightguy", 'a');
        book.BankSlopball(key, ulong.MaxValue, Provenance, DateTime.UtcNow);
        Assert.Equal(ulong.MaxValue, ArcadeRecordBook.Deserialize(book.Serialize()).Best(key));
        var document = JsonNode.Parse(book.Serialize())!;
        document["Records"]!.AsArray().Add(document["Records"]![0]!.DeepClone());
        Assert.Throws<System.IO.InvalidDataException>(() => ArcadeRecordBook.Deserialize(document.ToJsonString()));
        Assert.Throws<System.IO.InvalidDataException>(() => ArcadeRecordBook.Deserialize("{bad json"));
        Assert.Throws<System.IO.InvalidDataException>(() => ArcadeRecordBook.Deserialize("{\"Version\":2,\"Records\":[]}"));
    }

    [Theory]
    [InlineData(CharacterClass.FightGuy)]
    [InlineData(CharacterClass.Manki)]
    [InlineData(CharacterClass.Wibou)]
    [InlineData(CharacterClass.Bonk)]
    public void EveryRosterFighterHasAStableGameplayComparisonKey(CharacterClass fighter)
    {
        var arena = SlopballTests.Court(); var entry = BuiltInContentResolver.Resolve(fighter);
        string hash = ArcadeRecordCompatibility.Slopball(arena, entry);
        Assert.Equal(64, hash.Length);
        Assert.Equal(hash, ArcadeRecordCompatibility.Slopball(arena, entry));
        Assert.NotEqual(hash, ArcadeRecordCompatibility.Slopball(arena, entry, rulesRevision: ArcadeRecordCompatibility.RulesRevision + 1));
        Assert.NotEqual(hash, ArcadeRecordCompatibility.Slopball(arena, entry, tuning: new CombatTuning(.2f)));
    }

    [Fact]
    public void AirResistanceChangesTheGameplayComparisonKey()
    {
        var arena = SlopballTests.VariedCourt(); var entry = BuiltInContentResolver.Resolve(CharacterClass.FightGuy);
        string original = ArcadeRecordCompatibility.Slopball(arena, entry);
        var s = arena.Arcade!; var b = s.Ball;
        arena.Arcade = new(s.ProfileId, s.GameplayRevision, s.AllowedPackageIds,
            new(b.Spawn, b.InitialVelocity, b.Radius, b.Gravity, b.Restitution, b.LaunchScale, .5f), s.Slopball);
        Assert.NotEqual(original, ArcadeRecordCompatibility.Slopball(arena, entry));
    }

    [Fact]
    public void LauncherAreaChangesTheGameplayComparisonKeyWithoutARevisionBump()
    {
        var arena = SlopballTests.VariedCourt(); var entry = BuiltInContentResolver.Resolve(CharacterClass.FightGuy);
        string original = ArcadeRecordCompatibility.Slopball(arena, entry);
        var s = arena.Arcade!; var r = s.Slopball!;
        arena.Arcade = new(s.ProfileId, s.GameplayRevision, s.AllowedPackageIds, s.Ball,
            new(r.NetZ, r.NetHalfThickness, r.NetTop, r.FloorY, r.Seed, r.ServeGapTicks,
                r.EasyFlightTicks, r.HardFlightTicks, r.FlightJitterTicks, r.RampReturns,
                r.LandingHalfWidth, r.LandingMinZ, r.LandingMaxZ, 4, r.SpawnHalfDepth));
        Assert.NotEqual(original, ArcadeRecordCompatibility.Slopball(arena, entry));
    }

    [Fact]
    public void CosmeticPresentationAndProvenanceChangesKeepComparisonWhileGameplayAndPosesSeparateIt()
    {
        var arena = SlopballTests.Court(); var entry = BuiltInContentResolver.Resolve(CharacterClass.FightGuy);
        var original = ArcadeRecordCompatibility.Slopball(arena, entry);
        arena.DisplayName = "Renamed sports court"; arena.PreviewColor = "#ff0000";
        var c = entry.CookedCharacterPackage!.Definition;
        var cosmetic = Copy(entry, c.Movement, c.Presentation with
            { ModelResourcePath = "cosmetic/new-rig", VisualScale = 2, ModelYOffset = .2f },
            c.Slots.Select(slot => CopySlot(slot, slot.Timeline.Stages.Select(stage => CopyStage(stage,
                stage.Operations.Concat(new[] { new CookedEmitPresentationOperation(0, AuthoringUnit.Ticks, "cosmetic.new", 99) }).ToArray())).ToArray(),
                name: "New cosmetic move name")).ToArray());
        Assert.Equal(original, ArcadeRecordCompatibility.Slopball(arena, cosmetic));
        var moved = Copy(entry, c.Movement with { RunSpeed = c.Movement.RunSpeed + 1 }, c.Presentation, c.Slots);
        Assert.NotEqual(original, ArcadeRecordCompatibility.Slopball(arena, moved));
        // Modify an actual collision bone in a pose used by idle locomotion.
        int pose = cosmetic.BakedAnimation!.FindAnimIndex(cosmetic.Definition.IdleAnim);
        int bone = cosmetic.BakedAnimation.FindBoneIndex(CookedCharacterRuntimeAdapter.RuntimeBoneId(c.HurtboxBoneDefs[0].BoneId));
        var changedPoses = new BakedAnimationData
        {
            BoneNames = cosmetic.BakedAnimation.BoneNames,
            Animations = cosmetic.BakedAnimation.Animations.Select((animation, index) => new BakedAnimationData.BakedAnim
            {
                Name = animation.Name,
                Frames = animation.Frames.Select((frame, tick) => frame.Select((value, component) =>
                    index == pose && tick == 0 && component == bone * 3 ? value + .5f : value).ToArray()).ToArray(),
            }).ToArray(),
        };
        cosmetic = new MatchContentEntry(cosmetic.Handle, cosmetic.LegacySelector, cosmetic.Identity,
            cosmetic.DisplayName, cosmetic.Definition, changedPoses, cosmetic.CookedCharacterPackage);
        Assert.NotEqual(original, ArcadeRecordCompatibility.Slopball(arena, cosmetic));
        arena.CollisionTriangles[0].AX += .1f;
        Assert.NotEqual(original, ArcadeRecordCompatibility.Slopball(arena, entry));
    }

    [Fact]
    public void AbilityDamageAndShotSeedChangeComparisonButCapabilityVfxDoesNot()
    {
        var arena = SlopballTests.Court(); var entry = BuiltInContentResolver.Resolve(CharacterClass.Manki);
        var original = ArcadeRecordCompatibility.Slopball(arena, entry);
        var c = entry.CookedCharacterPackage!.Definition;
        MatchContentEntry Map(Func<CookedTimelineOperation, CookedTimelineOperation> map) => Copy(entry, c.Movement, c.Presentation,
            c.Slots.Select(slot => CopySlot(slot, slot.Timeline.Stages.Select(s => CopyStage(s, s.Operations.Select(map).ToArray())).ToArray())).ToArray());
        var cosmetic = Map(op => op is CookedStartCapabilityOperation cap && cap.Parameters is CookedMankiBazookaCapabilityParameters p
            ? new CookedStartCapabilityOperation(cap.Tick, cap.Unit, cap.CapabilityId, cap.CapabilityVersion, p with { ExplosionPresentationId = "new.vfx" }) : op);
        Assert.Equal(original, ArcadeRecordCompatibility.Slopball(arena, cosmetic));
        var stronger = Map(op => op is CookedStartCapabilityOperation cap && cap.Parameters is CookedMankiBazookaCapabilityParameters p
            ? new CookedStartCapabilityOperation(cap.Tick, cap.Unit, cap.CapabilityId, cap.CapabilityVersion, p with { Damage = p.Damage + 1 }) : op);
        Assert.NotEqual(original, ArcadeRecordCompatibility.Slopball(arena, stronger));
        var s = arena.Arcade!; var p = s.Slopball!;
        arena.Arcade = new(s.ProfileId, s.GameplayRevision, s.AllowedPackageIds, s.Ball,
            new(p.NetZ, p.NetHalfThickness, p.NetTop, p.FloorY, 1338, p.ServeGapTicks, p.EasyFlightTicks,
                p.HardFlightTicks, p.FlightJitterTicks, p.RampReturns, p.LandingHalfWidth, p.LandingMinZ, p.LandingMaxZ));
        Assert.NotEqual(original, ArcadeRecordCompatibility.Slopball(arena, entry));
    }

    private static ArcadeRecordKey Key(string fighter, char hash) => new("slopball", "court", fighter, new string(hash, 64));
    private static readonly ArcadeRecordProvenance Provenance = new("1.0.0", "cooked", "package", "stage");
    private static CookedStage CopyStage(CookedStage s, IReadOnlyList<CookedTimelineOperation> operations)
        => new(s.DurationTicks, s.IasaTicks, s.LandingLagTicks, s.AutoCancelBeforeTicks, s.AutoCancelAfterTicks,
            s.AnimationIds, operations, s.AttackRange, s.WarpRange, s.UseTargetLock, s.RotateTowardTarget, s.TrackingStrength);
    private static CookedSlotDefinition CopySlot(CookedSlotDefinition s, IReadOnlyList<CookedStage> stages, string? name = null)
        => new(s.Ordinal, s.Id, s.IsAir, name ?? s.Name, "cosmetic description", "new.icon", s.Behavior, s.AimMode,
            s.CooldownTicks, s.IsRecoveryMove, s.PreserveMomentumOnStart, new(stages), s.ChargePool, s.AimMovement,
            s.AimAnimationId, s.AllowSlideCarry, "new.hit.vfx");
    private static MatchContentEntry Copy(MatchContentEntry source, CookedMovement movement, CookedPresentation presentation,
        IReadOnlyList<CookedSlotDefinition> slots)
    {
        var p = source.CookedCharacterPackage!; var c = p.Definition;
        var definition = new CookedCharacterDefinition("Cosmetic fighter name", c.Weight, movement, presentation,
            c.CapsuleRadius, c.CapsuleHeight, c.HipHeight, c.HurtboxRadius, c.ShieldRadius, c.CaptureGeometry,
            c.HurtboxCapsules, c.HurtboxBoneDefs, c.AttachmentBoneIds, new[] { "new.vfx" }, c.CapabilityRequirements, slots);
        var package = new CookedCharacterPackage(p.Metadata with { Version = "9.9.9" }, definition, p.Budget, p.Diagnostics, new byte[] { 1 });
        return new(source.Handle, source.LegacySelector, source.Identity with { Version = "9.9.9", PackageHash = "different" },
            "Cosmetic fighter name", CookedCharacterRuntimeAdapter.ToCharacterDefinition(package, source.LegacySelector!.Value),
            source.BakedAnimation, package);
    }
}
