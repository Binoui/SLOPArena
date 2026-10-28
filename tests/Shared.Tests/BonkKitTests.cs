using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SlopArena.Shared;
using System.Text.Json.Nodes;
using SlopArena.Shared.Abilities;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class BonkKitTests
{
    private const string CapabilityId = CharacterPackageCompiler.TargetedLeapCapabilityId;

    [Fact]
    public void Bonk_TrustedPackage_CooksCanonicalKit()
    {
        var first = CompileBonk();
        var second = CompileBonk();
        Assert.NotNull(first.CookedPackage);
        Assert.DoesNotContain(first.Diagnostics, d => d.Severity == CharacterDiagnosticSeverity.Error);
        Assert.Equal(first.CookedPackage!.CanonicalBytes, second.CookedPackage!.CanonicalBytes);

        var package = first.CookedPackage;
        Assert.Equal(14, package.Definition.Slots.Count);
        Assert.Equal(14, package.Budget.SlotCount);
        Assert.Equal(new[] { CharacterPackageCompiler.SteeredChargeCapabilityId, CapabilityId },
            package.Definition.CapabilityRequirements.Select(x => x.CapabilityId).OrderBy(x => x, StringComparer.Ordinal));
        Assert.All(package.Definition.CapabilityRequirements, x => Assert.Equal("1", x.CapabilityVersion));

        var expectedIds = new[]
        {
            "ground.1", "ground.3", "ground.4",
            "ground.A", "ground.E", "ground.R", "ground.F",
            "air.1", "air.3", "air.4",
            "air.A", "air.E", "air.R", "air.F"
        };
        Assert.Equal(expectedIds.OrderBy(x => x), package.Definition.Slots.Select(x => x.Id).OrderBy(x => x));
    }


    [Fact]
    public void BonkTargetedLeap_CompilesBothVariantsWithTypedParameters()
    {
        var package = CompileBonk().CookedPackage!;
        foreach (string slotId in new[] { "ground.E", "air.E" })
        {
            var operation = Assert.Single(package.Definition.Slots.Single(x => x.Id == slotId)
                .Timeline.Stages.Single().Operations.OfType<CookedStartCapabilityOperation>());
            Assert.Equal(CapabilityId, operation.CapabilityId);
            Assert.Equal("1", operation.CapabilityVersion);
            Assert.IsType<CookedTargetedLeapCapabilityParameters>(operation.Parameters);
        }
    }

    [Fact]
    public void TargetedLeapCompiler_PreservesDistinctiveFixtureParameters()
    {
        var source = CharacterPackageSourceCodec.CreateAuthoringReady(
            "leap-fixture", "Leap fixture", "Tests", "MIT", "Tests");
        var hitbox = new HitboxSource(
            AuthoringHitboxShape.Capsule, 0.65f,
            1.25f, -0.75f, 2.5f, -1.5f, 0.875f, 3.25f,
            "bone.right-hand", "bone.left-foot", 7f, 43f, 8.5f, 23.5f,
            19, 9, false, 3, AuthoringKnockbackDirection.TowardOwner, 21);
        var parameters = new TargetedLeapCapabilityParameters(
            17, 113, 2.25f, 17.75f, 19.5f, 11, 37, hitbox,
            "presentation.fixture.launch", "presentation.fixture.land");
        source = source with
        {
            Character = source.Character with
            {
                CapabilityRequirements = new[] { new CapabilityRequirementSource(CapabilityId, "1") },
                PresentationIds = new[] { "presentation.fixture.launch", "presentation.fixture.land" },
                Slots = source.Character.Slots.Select(slot => slot.Id is "ground.E" or "air.E"
                    ? slot with
                    {
                        Behavior = AuthoringAbilityBehavior.AimedProjectile,
                        AimMode = AuthoringAimMode.GroundCursor,
                        Timeline = new CharacterTimelineSource(new[]
                        {
                            new CharacterStageSource(64, 0, 0, 0, 0,
                                slot.Timeline.Stages.Single().AnimationIds,
                                new CharacterTimelineOperationSource[]
                                {
                                    new StartCapabilityOperationSource(0, AuthoringUnit.Ticks,
                                        CapabilityId, "1", parameters),
                                }),
                        }),
                    } : slot).ToArray(),
            },
        };
        var result = CharacterPackageCompiler.Compile(source, CharacterCookProfile.Workshop);
        Assert.True(result.CookedPackage != null, string.Join("; ", result.Diagnostics));
        var expected = new CookedTargetedLeapCapabilityParameters(
            17, 113, 2.25f, 17.75f, 19.5f, 11, 37,
            new CookedHitbox(AuthoringHitboxShape.Capsule, 0.65f,
                1.25f, -0.75f, 2.5f, -1.5f, 0.875f, 3.25f,
                "bone.right-hand", "bone.left-foot", 7f, 43f, 8.5f, 23.5f,
                19, 9, false, 3, AuthoringKnockbackDirection.TowardOwner, 21),
            "presentation.fixture.launch", "presentation.fixture.land");
        foreach (string slotId in new[] { "ground.E", "air.E" })
        {
            var operation = Assert.Single(result.CookedPackage!.Definition.Slots.Single(x => x.Id == slotId)
                .Timeline.Stages.Single().Operations.OfType<CookedStartCapabilityOperation>());
            Assert.Equal(expected, Assert.IsType<CookedTargetedLeapCapabilityParameters>(operation.Parameters));
        }
    }
    [Fact]
    public void Bonk_AerialAAndRApplyTheirAuthoredGravityWindow()
    {
        var def = BonkDefinition();
        foreach (byte slot in new[] { AbilitySlots.A, AbilitySlots.R })
        {
            var window = Assert.Single(def.GetCookedSlotAbility(slot, airborne: true)!
                .Timeline.Stages.Single().Operations.OfType<CookedGravityWindowOperation>());
            Assert.Equal((ushort)0, window.Tick);
            Assert.Equal(0.5f, window.GravityScale);
            Assert.Equal((ushort)30, window.DurationTicks);
            var sim = TestHelpers.MakeSim();
            sim.RegisterEntity(1, def, TestHelpers.PlayerState() with
            {
                PY = 20f,
                VY = 0f,
                AirTimeTicks = 100,
                IsGrounded = false,
            });

            sim.Tick(new Dictionary<ulong, InputState>
            {
                [1] = new InputState { ActiveSlot = slot },
            });

            var state = sim.GetState(1);
            Assert.Equal(ActionState.Attacking, state.State);
            TestHelpers.AssertNear(-def.Movement.Gravity * Simulation.TickDt * 0.5f, state.VY);
        }
    }


    [Fact]
    public void BonkCapabilityParametersRejectUnknownAndMissingFields()
    {
        var unknown = JsonNode.Parse(File.ReadAllText(RepoFile("client/Unity/Assets/CharacterPackages/bonk/character.json")))!.AsObject();
        var unknownParameters = (JsonObject)unknown["slots"]![4]!["timeline"]!["stages"]![0]!["operations"]![0]!["parameters"]!;
        unknownParameters["extra"] = 1;
        var unknownResult = CharacterPackageCompiler.Compile(
            File.ReadAllText(RepoFile("client/Unity/Assets/CharacterPackages/bonk/package.json")),
            unknown.ToJsonString(),
            CharacterCookProfile.TrustedBuiltIn);
        Assert.Contains(unknownResult.Diagnostics, x => x.Code == "operation.parameter-unknown");
        Assert.Null(unknownResult.CookedPackage);

        var missing = JsonNode.Parse(File.ReadAllText(RepoFile("client/Unity/Assets/CharacterPackages/bonk/character.json")))!.AsObject();
        var missingParameters = (JsonObject)missing["slots"]![4]!["timeline"]!["stages"]![0]!["operations"]![0]!["parameters"]!;
        missingParameters.Remove("hitbox");
        var missingResult = CharacterPackageCompiler.Compile(
            File.ReadAllText(RepoFile("client/Unity/Assets/CharacterPackages/bonk/package.json")),
            missing.ToJsonString(),
            CharacterCookProfile.TrustedBuiltIn);
        Assert.Contains(missingResult.Diagnostics, x => x.Code == "operation.parameter-missing");
        Assert.Null(missingResult.CookedPackage);
    }

    [Fact]
    public void TargetedLeap_PublicCapabilityIsWorkshopAdmittedAndVersioned()
    {
        string packageJson = File.ReadAllText(RepoFile("client/Unity/Assets/CharacterPackages/bonk/package.json"));
        string characterJson = File.ReadAllText(RepoFile("client/Unity/Assets/CharacterPackages/bonk/character.json"));
        var workshop = CharacterPackageCompiler.Compile(packageJson, characterJson, CharacterCookProfile.Workshop);
        Assert.NotNull(workshop.CookedPackage);

        var versionMismatch = JsonNode.Parse(characterJson)!.AsObject();
        versionMismatch["capabilityRequirements"]![0]!["capabilityVersion"] = "2";
        foreach (int slotIndex in new[] { 4, 11 })
            versionMismatch["slots"]![slotIndex]!["timeline"]!["stages"]![0]!["operations"]![0]!["capabilityVersion"] = "2";
        var rejected = CharacterPackageCompiler.Compile(
            packageJson, versionMismatch.ToJsonString(), CharacterCookProfile.Workshop);
        Assert.Null(rejected.CookedPackage);
        Assert.Contains(rejected.Diagnostics, x => x.Code == "capability.version-mismatch");
    }
    [Fact]
    public void TargetedLeapNumericAndBoneReferencesAreValidatedInBothCookProfiles()
    {
        string packageJson = File.ReadAllText(RepoFile("client/Unity/Assets/CharacterPackages/bonk/package.json"));
        string characterJson = File.ReadAllText(RepoFile("client/Unity/Assets/CharacterPackages/bonk/character.json"));
        foreach (var profile in new[] { CharacterCookProfile.Workshop, CharacterCookProfile.TrustedBuiltIn })
        {
            var badRange = JsonNode.Parse(characterJson)!.AsObject();
            badRange["slots"]![4]!["timeline"]!["stages"]![0]!["operations"]![0]!["parameters"]!["maxRange"] = 0;
            var rangeResult = CharacterPackageCompiler.Compile(packageJson, badRange.ToJsonString(), profile);
            Assert.Null(rangeResult.CookedPackage);
            Assert.Contains(rangeResult.Diagnostics, x => x.Code == "value.out-of-range");

            var badBone = JsonNode.Parse(characterJson)!.AsObject();
            badBone["slots"]![4]!["timeline"]!["stages"]![0]!["operations"]![0]!["parameters"]!["hitbox"]!["startBoneId"] = "_not_declared";
            var boneResult = CharacterPackageCompiler.Compile(packageJson, badBone.ToJsonString(), profile);
            Assert.Null(boneResult.CookedPackage);
            Assert.Contains(boneResult.Diagnostics, x => x.Code == "reference.unresolved");
        }
    }
    [Fact]
    public void TargetedLeapCompilerRejectsMultipleLifecycleOwnersInOneSlot()
    {
        string packageJson = File.ReadAllText(RepoFile("client/Unity/Assets/CharacterPackages/bonk/package.json"));
        var character = JsonNode.Parse(File.ReadAllText(RepoFile("client/Unity/Assets/CharacterPackages/bonk/character.json")))!.AsObject();
        var operations = (JsonArray)character["slots"]![4]!["timeline"]!["stages"]![0]!["operations"]!;
        operations.Add(operations[0]!.DeepClone());
        var result = CharacterPackageCompiler.Compile(
            packageJson, character.ToJsonString(), CharacterCookProfile.Workshop);
        Assert.Null(result.CookedPackage);
        Assert.Contains(result.Diagnostics, x => x.Code == "capability.ambiguous");
    }



    [Fact]
    public void RetiredBonkCapabilityIsRejectedDuringSourceAdmission()
    {
        const string retiredId = "slop.internal.bonk.targeted-jump-slam.v1";
        string packageJson = File.ReadAllText(RepoFile("client/Unity/Assets/CharacterPackages/bonk/package.json"));
        var character = JsonNode.Parse(File.ReadAllText(RepoFile("client/Unity/Assets/CharacterPackages/bonk/character.json")))!.AsObject();
        character["capabilityRequirements"]![0]!["capabilityId"] = retiredId;
        foreach (int slotIndex in new[] { 4, 11 })
            character["slots"]![slotIndex]!["timeline"]!["stages"]![0]!["operations"]![0]!["capabilityId"] = retiredId;
        var result = CharacterPackageCompiler.Compile(
            packageJson, character.ToJsonString(), CharacterCookProfile.TrustedBuiltIn);
        Assert.Null(result.CookedPackage);
        Assert.Contains(result.Diagnostics, x => x.Code == "capability.retired");
    }

    [Fact]
    public void TargetedLeapRegistryRequiresExactVersionAndType()
    {
        var package = CompileBonk().CookedPackage!;
        var operation = Assert.Single(package.Definition.Slots.Single(x => x.Id == "ground.E")
            .Timeline.Stages.Single().Operations.OfType<CookedStartCapabilityOperation>());
        var parameters = Assert.IsType<CookedTargetedLeapCapabilityParameters>(operation.Parameters);
        Assert.True(InternalCapabilityRegistry.TryCreate(CapabilityId, "1", parameters, out var capability));
        Assert.IsType<TargetedLeapAbility>(capability);
        Assert.False(InternalCapabilityRegistry.TryCreate(CapabilityId, "2", parameters, out _));
        Assert.False(InternalCapabilityRegistry.TryCreate(
            CapabilityId, "1", new CookedRisingDragonCapabilityParameters(1, 1, 1), out _));
    }

    [Fact]
    public void BonkE_HoldsCachesReleaseYawAndSlamsOnLanding()
    {
        var east = RunE(9000, grounded: true, out var eastHitbox);
        Assert.Equal(ActionState.Attacking, east.State);
        Assert.True(eastHitbox);
        Assert.True(east.PX > 20f, "positive yaw must travel in positive world-space direction");

        var opposite = RunE(-9000, grounded: true, out var oppositeHitbox);
        Assert.True(oppositeHitbox);
        Assert.True(opposite.PX < 20f, "opposite yaw must travel in the opposite world-space direction");
    }

    [Fact]
    public void BonkE_Ground_LandingStartsTheLandClip_AndRunsFullRecovery()
    {
        var def = BonkDefinition();
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState(x: 20f, z: 10f);
        state.PY = TestHelpers.GroundPY(def);
        sim.RegisterEntity(1, def, state);

        sim.Tick(new Dictionary<ulong, InputState> { [1] = AimInput(9000, 600, 4, true) });
        for (var i = 0; i < 9; i++)
            sim.Tick(new Dictionary<ulong, InputState> { [1] = AimInput(9000, 600, 0, true) });
        sim.Tick(new Dictionary<ulong, InputState> { [1] = AimInput(-9000, 600, 0, false) });

        int impactTick = -1, windowEdges = 0, endTick = -1, landedTick = -1, abilityEndTick = -1;
        ushort impactElapsed = 0;
        byte impactClip = 0;
        int slamDuration = -1;
        bool wasActive = false;
        for (var i = 0; i < 200; i++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { [1] = default });
            var st = sim.GetState(1);
            if (landedTick < 0 && i > 0 && st.IsGrounded) landedTick = i;
            if (abilityEndTick < 0 && sim.GetActiveAbility(1) == null) abilityEndTick = i;
            bool active = sim.Resolver.GetActiveHitboxes().Any(x => x.OwnerId == 1 && x.Damage == 13f);
            if (active && !wasActive)
            {
                windowEdges++;
                if (impactTick < 0)
                {
                    impactTick = i;
                    impactElapsed = st.AttackElapsedTicks;
                    impactClip = st.ComboStage;
                    slamDuration = sim.Resolver.GetActiveHitboxes()
                        .First(x => x.OwnerId == 1 && x.Damage == 13f).DurationTicks;
                }
            }
            wasActive = active;
            if (impactTick >= 0 && endTick < 0 && st.State != ActionState.Attacking)
                endTick = i;
        }

        Assert.True(impactTick >= 0,
            $"landing must spawn the slam window; landedTick={landedTick} abilityEndTick={abilityEndTick} "
            + $"endTick={endTick} state={sim.GetState(1).State} "
            + $"grounded={sim.GetState(1).IsGrounded} active={sim.Resolver.GetActiveHitboxes().Count}");
        Assert.Equal((ushort)0, impactElapsed);  // the landing clip starts at its first frame
        Assert.Equal((byte)1, impactClip);       // ... and it is the stage's second clip, anim.bonk.ge-land
        Assert.Equal(1, windowEdges);            // exactly one damage window opens
        Assert.Equal(6, slamDuration);           // authored six-tick slam window, independent of recovery
        Assert.True(endTick >= 0, "recovery must complete into locomotion");
        Assert.Equal(68, endTick - impactTick);  // the landing clip: slam, handstand, haul, pop
    }

    [Fact]
    public void BonkE_RecoveryPausesDuringHitstop()
    {
        var def = BonkDefinition();
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState(x: 20f, z: 10f);
        state.PY = TestHelpers.GroundPY(def);
        sim.RegisterEntity(1, def, state);

        sim.Tick(new Dictionary<ulong, InputState> { [1] = AimInput(0, 600, 4, true) });
        for (var i = 0; i < 9; i++)
            sim.Tick(new Dictionary<ulong, InputState> { [1] = AimInput(0, 600, 0, true) });
        sim.Tick(new Dictionary<ulong, InputState> { [1] = AimInput(0, 600, 0, false) });

        bool impactSeen = false;
        for (var i = 0; i < 100; i++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { [1] = default });
            if (sim.Resolver.GetActiveHitboxes().Any(x => x.OwnerId == 1 && x.Damage == 13f))
            {
                impactSeen = true;
                break;
            }
        }
        Assert.True(impactSeen, "landing must spawn the authored hitbox before recovery is frozen");

        sim.SetState(1, sim.GetState(1) with { HitstopTicks = 3 });
        ushort frozenElapsed = sim.GetState(1).AttackElapsedTicks;
        while (sim.GetState(1).HitstopTicks > 0)
        {
            sim.Tick(new Dictionary<ulong, InputState> { [1] = default });
            Assert.NotNull(sim.GetActiveAbility(1));
            Assert.Equal(frozenElapsed, sim.GetState(1).AttackElapsedTicks);
        }
        for (var i = 0; i < 67; i++)  // the 68-tick landing clip, minus the tick that spawned the slam
            sim.Tick(new Dictionary<ulong, InputState> { [1] = default });
        Assert.NotNull(sim.GetActiveAbility(1));
        sim.Tick(new Dictionary<ulong, InputState> { [1] = default });
        Assert.Null(sim.GetActiveAbility(1));
    }

    [Fact]
    public void BonkE_AirborneStartContinuesThroughLandingIntoSlam()
    {
        var def = BonkDefinition();
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState(x: 20f, z: 10f);
        state.PY = TestHelpers.GroundPY(def) + 3f;
        state.IsGrounded = false;
        state.VY = -1f;
        sim.RegisterEntity(1, def, state);

        sim.Tick(new Dictionary<ulong, InputState> { [1] = AimInput(0, 600, 4, true) });
        for (var i = 0; i < 8; i++)
            sim.Tick(new Dictionary<ulong, InputState> { [1] = AimInput(0, 600, 0, true) });

        sim.Tick(new Dictionary<ulong, InputState> { [1] = AimInput(0, 600, 0, false) });
        Assert.Equal(ActionState.Attacking, sim.GetState(1).State);
        Assert.False(sim.GetState(1).IsGrounded);

        var slamSeen = false;
        for (var i = 0; i < 120; i++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { [1] = default });
            slamSeen |= sim.Resolver.GetActiveHitboxes().Any(x => x.OwnerId == 1 && x.Damage == 13f);
            if (slamSeen) break;
        }

        Assert.True(slamSeen, $"state={sim.GetState(1).State} slot={sim.GetState(1).AttackSlot} py={sim.GetState(1).PY} grounded={sim.GetState(1).IsGrounded} active={sim.GetActiveAbility(1)?.GetType().Name}");
    }

    [Fact]
    public void BonkE_HoldsPastAimCap_StopsMovementAndReleasesCachedTarget()
    {
        var def = BonkDefinition();
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState(x: 20f, z: 10f);
        state.PY = TestHelpers.GroundPY(def);
        sim.RegisterEntity(1, def, state);

        float startZ = state.PZ;
        var held = new InputState { ActiveSlot = 4, AimYaw = 9000, AimDistance = 600, MoveY = 0.25f, IsAiming = true };
        for (var i = 0; i < 300; i++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { [1] = held });
            Assert.Equal(ActionState.Aiming, sim.GetState(1).State);
            Assert.NotNull(sim.GetActiveAbility(1));
            Assert.DoesNotContain(sim.Resolver.GetActiveHitboxes(), x => x.OwnerId == 1 && x.Damage == 13f);
        }

        var heldState = sim.GetState(1);
        Assert.Equal(startZ, heldState.PZ);
        Assert.Equal(0f, heldState.VZ);

        sim.Tick(new Dictionary<ulong, InputState>
        {
            [1] = new InputState { ActiveSlot = 4, AimYaw = -9000, AimDistance = 1200, IsAiming = false },
        });
        var released = sim.GetState(1);
        Assert.Equal(ActionState.Attacking, released.State);
        Assert.True(released.VX > 0f, "release must use the cached held yaw");

        var slamWindows = 0;
        var wasActive = false;
        for (var i = 0; i < 120; i++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { [1] = default });
            var active = sim.Resolver.GetActiveHitboxes().Any(x => x.OwnerId == 1 && x.Damage == 13f);
            if (active && !wasActive) slamWindows++;
            wasActive = active;
        }
        Assert.Equal(1, slamWindows);
    }


    [Fact]
    public void BonkE_ExpiresOffstageWithoutPhantomSlam()
    {
        var def = BonkDefinition();
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = 100f;
        state.IsGrounded = false;
        sim.RegisterEntity(1, def, state);
        var held = AimInput(9000, 600, 4, true);
        sim.Tick(new Dictionary<ulong, InputState> { [1] = held });
        for (var i = 0; i < 9; i++) sim.Tick(new Dictionary<ulong, InputState> { [1] = AimInput(9000, 600, 0, true) });
        sim.Tick(new Dictionary<ulong, InputState> { [1] = AimInput(9000, 600, 0, false) });
        // maxFlightTicks 96 (human-approved) plus release debounce; the ability must
        // terminate in the air without ever opening the slam window.
        for (var i = 0; i < 120; i++) sim.Tick(new Dictionary<ulong, InputState> { [1] = default });
        Assert.Null(sim.GetActiveAbility(1));
        Assert.DoesNotContain(sim.Resolver.GetActiveHitboxes(), x => x.OwnerId == 1 && x.Damage == 13f);
    }

    [Fact]
    public void BonkE_Interrupted_NoStaleSlamOrLock()
    {
        var def = BonkDefinition();
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState(x: 20f, z: 10f);
        state.PY = TestHelpers.GroundPY(def);
        sim.RegisterEntity(1, def, state);

        sim.Tick(new Dictionary<ulong, InputState> { [1] = AimInput(9000, 600, 4, true) });
        for (var i = 0; i < 9; i++)
            sim.Tick(new Dictionary<ulong, InputState> { [1] = AimInput(9000, 600, 0, true) });
        sim.Tick(new Dictionary<ulong, InputState> { [1] = AimInput(-9000, 600, 0, false) });
        for (var i = 0; i < 20; i++) sim.Tick(new Dictionary<ulong, InputState> { [1] = default });
        Assert.NotNull(sim.GetActiveAbility(1)); // mid-flight when the interruption lands

        // Mirror the post-hit state the real hit pipeline writes: State leaves
        // Attacking, which is the generic TickAbilities cancellation seam.
        sim.SetState(1, sim.GetState(1) with { HitstunTicks = 30, HitstunLevel = 0, State = ActionState.Hitstun });

        for (var i = 0; i < 120; i++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { [1] = default });
            Assert.DoesNotContain(sim.Resolver.GetActiveHitboxes(),
                x => x.OwnerId == 1 && x.Damage == 13f);
        }
        var end = sim.GetState(1);
        Assert.Null(sim.GetActiveAbility(1));
        Assert.Equal((byte)0, end.AttackSlot);
        Assert.Equal((ushort)0, end.AnimLockTicks);
    }

    [Fact]
    public void BonkF_LandsOneHoldHitPerTurnThenTheFinisher()
    {
        var def = BonkDefinition();
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState(x: 100f, z: 100f);
        state.PY = TestHelpers.GroundPY(def);
        sim.RegisterEntity(1, def, state);
        var targetDef = TestHelpers.CombatDef;
        // Oversized hurtbox isolates per-hit identity from blade reach and body separation.
        targetDef = targetDef with { HurtboxCapsules = new[]
        {
            new HurtboxCapsule(0f, -0.65f, 0f, 0f, 0.65f, 0f, 1f),
        } };
        var target = TestHelpers.NpcState(x: 100f, z: 100.1f);
        target.PY = TestHelpers.GroundPY(targetDef);
        sim.RegisterEntity(100, targetDef, target);
        var damage = new List<float>();
        for (var i = 0; i < 200; i++)
        {
            // Hold a damageable dummy in contact to test hit identity, not knockback escape.
            sim.SetState(100, target with { DamagePercent = sim.GetState(100).DamagePercent });
            sim.Tick(new Dictionary<ulong, InputState>
            {
                [1] = i == 0 ? TestHelpers.Input(activeSlot: AbilitySlots.F) : default,
                [100] = default,
            });
            damage.AddRange(sim.LastTickHits
                .Where(x => x.OwnerEntityId == 1 && x.TargetEntityId == 100)
                .Select(x => x.Damage));
        }
        Assert.Equal(new[] { 2.5f, 2.5f, 2.5f, 2.5f, 12f }, damage);
        Assert.Null(sim.GetActiveAbility(1));
    }

    [Fact]
    public void BonkF_WalksSlowlyDuringTheSpinAndStopsForTheRecovery()
    {
        // The storm can be steered at a fraction of his speed while it spins. When the spin
        // ends, so does the walking: no slide through the recovery, on the ground or after
        // landing from the air version.
        var def = BonkDefinition();
        foreach (bool airborne in new[] { false, true })
        {
            var sim = TestHelpers.MakeSim();
            var state = TestHelpers.PlayerState(x: 20f, z: 10f);
            state.PY = TestHelpers.GroundPY(def) + (airborne ? 3f : 0f);
            state.IsGrounded = !airborne;
            sim.RegisterEntity(1, def, state);
            float scale = (def.GetCookedSlotAbility(AbilitySlots.F, airborne)?.AimMovementSpeedScale ?? 1f);
            Assert.InRange(scale, 0.1f, 0.9f);

            var heavySeen = false;
            float fastest = 0f;
            CharacterState? spinEnd = null;
            for (var i = 0; i < 160 && (i == 0 || sim.GetActiveAbility(1) != null); i++)
            {
                sim.Tick(new Dictionary<ulong, InputState> { [1] = TestHelpers.Input(activeSlot: i == 0 ? AbilitySlots.F : (byte)0, moveY: 1f) });
                var st = sim.GetState(1);
                heavySeen |= sim.Resolver.GetActiveHitboxes().Any(x => x.OwnerId == 1 && x.Damage == 12f);
                if (st.State == ActionState.Aiming)
                    fastest = MathF.Max(fastest, MathF.Sqrt(st.VX * st.VX + st.VZ * st.VZ));
                else if (spinEnd == null && st.AttackSlot != 0)
                    spinEnd = st;
                if (spinEnd is { } end && st.AttackSlot != 0)
                {
                    TestHelpers.AssertNear(end.PX, st.PX, 1e-4f);
                    TestHelpers.AssertNear(end.PZ, st.PZ, 1e-4f);
                }
            }
            float cap = scale * (airborne ? def.Movement.AirSpeedMax : def.Movement.RunSpeed);
            Assert.InRange(fastest, 0.5f * cap, Math.Max(cap, scale * def.Movement.RunSpeed) + 1e-4f);
            Assert.True(sim.GetState(1).PZ > state.PZ, "F must let him walk while spinning");
            Assert.NotNull(spinEnd);
            Assert.True(heavySeen);
            Assert.Null(sim.GetActiveAbility(1));
        }
    }

    [Theory]
    [InlineData(AbilitySlots.Slot1, 2.2f, false, true)]
    [InlineData(AbilitySlots.Slot1, 2.2f, true, true)]
    [InlineData(AbilitySlots.Slot1, 8f, false, false)]
    [InlineData(AbilitySlots.Slot3, 2.2f, false, true)]
    [InlineData(AbilitySlots.Slot3, 2.2f, true, true)]
    [InlineData(AbilitySlots.Slot3, 8f, false, false)]
    [InlineData(AbilitySlots.Slot4, 2.2f, false, true)]
    [InlineData(AbilitySlots.Slot4, 2.2f, true, true)]
    [InlineData(AbilitySlots.Slot4, 8f, false, false)]
    public void BonkAerialNormals_CookedSwordPathHitsBlocksAndMisses(byte slot, float distance, bool shielding, bool expectContact)
    {
        var content = BuiltInContentResolver.Resolve(CharacterClass.Bonk);
        var def = content.Definition;
        var baked = content.BakedAnimation;
        Assert.NotNull(baked);
        var sim = TestHelpers.MakeSim();
        // A low aerial should meet a standing opponent. The Lab's default aerial
        // starts a full capsule height above its grounded dummy and passes over it.
        var actor = TestHelpers.PlayerState(x: 20f, z: 10f) with
        {
            PY = TestHelpers.GroundPY(def) + 0.25f,
            IsGrounded = false,
        };
        var target = TestHelpers.NpcState(x: 20f, z: 10f + distance) with
        {
            PY = TestHelpers.GroundPY(def),
            FacingYaw = MathF.PI,
        };
        sim.RegisterEntity(1, def, actor, baked);
        sim.RegisterEntity(100, def, target, baked);
        var hits = new List<SpellResolver.HitResult>();
        ushort maxBlockStun = 0;
        for (int tick = 0; tick < 90; tick++)
        {
            sim.Tick(new Dictionary<ulong, InputState>
            {
                [1] = new() { ActiveSlot = tick == 0 ? slot : AbilitySlots.None },
                [100] = new()
                {
                    FacingYaw = -18000,
                    ShieldHeld = shielding,
                    ShieldPressed = shielding && tick == 0,
                },
            });
            hits.AddRange(sim.LastTickHits.Where(hit => hit.OwnerEntityId == 1 && hit.TargetEntityId == 100));
            maxBlockStun = Math.Max(maxBlockStun, sim.GetState(100).BlockStunTicks);
        }

        if (expectContact)
        {
            var hit = Assert.Single(hits);
            Assert.Equal(slot, hit.AttackSlot);
            Assert.Equal(shielding, hit.Blocked);
            if (shielding)
            {
                Assert.Equal(0f, hit.Damage);
                Assert.Equal((ushort)0, sim.GetState(100).DamagePercent);
                Assert.True(maxBlockStun > 0);
            }
            else
            {
                Assert.True(hit.Damage > 0);
                Assert.True(sim.GetState(100).DamagePercent > 0);
            }
        }
        else
        {
            Assert.Empty(hits);
            Assert.Equal((ushort)0, sim.GetState(100).DamagePercent);
        }
        Assert.Equal(ActionState.Idle, sim.GetState(1).State);
        Assert.Null(sim.GetActiveAbility(1));
    }

    private static CharacterCompileResult CompileBonk()
    {
        var result = CharacterPackageCompiler.Compile(
            File.ReadAllText(RepoFile("client/Unity/Assets/CharacterPackages/bonk/package.json")),
            File.ReadAllText(RepoFile("client/Unity/Assets/CharacterPackages/bonk/character.json")),
            CharacterCookProfile.TrustedBuiltIn);
        Assert.True(result.CookedPackage != null,
            string.Join("; ", result.Diagnostics.Select(x => $"{x.Code}: {x.Message} ({x.Path})")));
        return result;

    }
    private static CharacterDefinition BonkDefinition()
        => CookedCharacterRuntimeAdapter.ToCharacterDefinition(CompileBonk().CookedPackage!);

    private static CharacterState RunE(short yaw, bool grounded, out bool hitboxSeen)
    {
        var def = BonkDefinition();
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState(x: 20f, z: 10f);
        state.PY = grounded ? TestHelpers.GroundPY(def) : 100f;
        state.IsGrounded = grounded;
        sim.RegisterEntity(1, def, state);
        sim.Tick(new Dictionary<ulong, InputState> { [1] = AimInput(yaw, 600, 4, true) });
        Assert.Equal(ActionState.Aiming, sim.GetState(1).State);
        Assert.True(sim.GetState(1).IsAiming);
        for (var i = 0; i < 9; i++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { [1] = AimInput(yaw, 600, 0, true) });
            Assert.NotNull(sim.GetActiveAbility(1));
        }
        Assert.Equal((byte)4, sim.GetState(1).AttackSlot);
        var release = AimInput((short)-yaw, 600, 0, false);
        sim.Tick(new Dictionary<ulong, InputState> { [1] = release });
        var afterRelease = sim.GetState(1);
        Assert.True(afterRelease.State == ActionState.Attacking, $"state={afterRelease.State} slot={afterRelease.AttackSlot} active={sim.GetActiveAbility(1)?.GetType().Name} py={afterRelease.PY} grounded={afterRelease.IsGrounded}");
        var flight = sim.GetState(1);
        Assert.Equal(ActionState.Attacking, flight.State);
        Assert.False(flight.IsAiming);
        Assert.True(Math.Abs(flight.FacingYaw - yaw * .01f * MathF.PI / 180f) < .001f);
        Assert.True(Math.Sign(flight.VX) == Math.Sign(yaw), "release camera yaw must not replace cached held yaw");
        hitboxSeen = false;
        for (var i = 0; i < 80; i++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { [1] = default });
            if (sim.Resolver.GetActiveHitboxes().Any(x => x.OwnerId == 1 && x.Damage == 13f))
            {
                hitboxSeen = true;
                break;
            }
        }
        Assert.True(hitboxSeen, $"final state={sim.GetState(1).State} slot={sim.GetState(1).AttackSlot} px={sim.GetState(1).PX} pz={sim.GetState(1).PZ} py={sim.GetState(1).PY} grounded={sim.GetState(1).IsGrounded} active={sim.Resolver.GetActiveHitboxes().Count}");
        return sim.GetState(1);
    }

    private static InputState AimInput(short yaw, ushort distance, byte slot, bool aiming)
        => new() { AimYaw = yaw, AimDistance = distance, ActiveSlot = slot, IsAiming = aiming };

    private static string RepoFile(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relative);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException(relative);
    }
}
