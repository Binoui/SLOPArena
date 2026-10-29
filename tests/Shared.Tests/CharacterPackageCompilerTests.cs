using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class CharacterPackageCompilerTests
{
    [Theory]
    [InlineData("startupTicks", 0)]
    [InlineData("startupTicks", 18)]
    [InlineData("startupTicks", 121)]
    [InlineData("verticalSpeed", 0)]
    [InlineData("verticalSpeed", 61)]
    [InlineData("directionalVerticalSpeed", 16)]
    [InlineData("directionalVerticalSpeed", 0)]
    [InlineData("horizontalSpeed", -1)]
    [InlineData("preparationRiseSpeed", 5)]
    public void AimedRecoveryRejectsUnboundedParameters(string field, int value)
        => AssertError(CompileRecovery(c => Recovery(c)["parameters"]![field] = value), "value.out-of-range");

    [Fact]
    public void AimedRecoveryIsPublicAndRejectsCompetingMotionOrEarlyCancel()
    {
        Assert.True(CharacterPackageCompiler.IsPublicCapability(CharacterPackageCompiler.AimedRecoveryCapabilityId, "1"));
        Assert.False(CharacterPackageCompiler.IsPublicCapability(CharacterPackageCompiler.AimedRecoveryCapabilityId, "2"));
        var valid = CompileRecovery();
        Assert.NotNull(valid.CookedPackage);
        Assert.Equal("1.10.0", valid.CookedPackage!.Metadata.RuntimeApiMin);
        AssertError(CompileRecovery(c => Recovery(c)["tick"] = 1), "capability.ambiguous");
        AssertError(CompileRecovery(c => RecoverySlot(c)["isRecoveryMove"] = false), "capability.ambiguous");
        AssertError(CompileRecovery(c => RecoverySlot(c)["timeline"]!["stages"]![0]!["iasaTicks"] = 2), "capability.ambiguous");
        AssertError(CompileRecovery(c => RecoverySlot(c)["timeline"]!["stages"]![0]!["operations"]!.AsArray()
            .Add(JsonNode.Parse("""{"kind":"setVelocity","tick":1,"unit":"metersPerSecond","velocityMode":"absolute","x":0,"y":1,"z":0}"""))), "capability.ambiguous");
        AssertError(CompileRecovery(c => Recovery(c)["parameters"]!["unknownField"] = 1), "operation.parameter-unknown");
        AssertError(CompileRecovery(c => Recovery(c)["parameters"]!.AsObject().Remove("horizontalSpeed")), "operation.parameter-missing");
        AssertError(CompileRecovery(c => Recovery(c)["parameters"]!["hop"]!["unknownField"] = 1), "operation.parameter-unknown");
        Assert.Null(CompileRecovery(c => Recovery(c)["parameters"]!["hop"]!.AsObject().Remove("arcHeight")).CookedPackage);
    }

    [Theory]
    [InlineData("startTicks", 0)]
    [InlineData("startTicks", 34)]
    [InlineData("contactTicks", 18)]
    [InlineData("contactTicks", 44)]
    [InlineData("forwardDistance", 0)]
    [InlineData("forwardDistance", 4)]
    [InlineData("arcHeight", 0)]
    [InlineData("arcHeight", 4)]
    [InlineData("landingHeight", -1)]
    [InlineData("landingHeight", 2)]
    public void AimedRecoveryRejectsUnboundedOrAmbiguousSetupHop(string field, int value)
        => AssertError(CompileRecovery(c => Recovery(c)["parameters"]!["hop"]![field] = value), "value.out-of-range");

    [Fact]
    public void AimedRecoveryWithoutHopRetainsItsLegacyContract()
    {
        var legacy = CompileRecovery(c =>
        {
            var stage = RecoverySlot(c)["timeline"]!["stages"]![0]!;
            var capability = Recovery(c).DeepClone();
            capability["parameters"]!.AsObject().Remove("hop");
            capability["parameters"]!["startupTicks"] = 12;
            capability["parameters"]!["preparationRiseSpeed"] = .8f;
            stage["durationTicks"] = 18;
            stage["operations"] = new JsonArray(capability);
        });
        Assert.NotNull(legacy.CookedPackage);
        Assert.Equal("1.9.0", legacy.CookedPackage!.Metadata.RuntimeApiMin);
        Assert.DoesNotContain("\"hop\"", System.Text.Encoding.UTF8.GetString(legacy.CookedPackage.CanonicalBytes));
    }

    private static CharacterCompileResult CompileRecovery(Action<JsonObject>? edit = null)
    {
        string root = Path.GetDirectoryName(FindRepoFile("client/Unity/Assets/CharacterPackages/bashie/character.json"))!;
        var character = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "character.json")))!.AsObject();
        RingBounceFixture.Apply(character);
        edit?.Invoke(character);
        return CharacterPackageCompiler.Compile(File.ReadAllText(Path.Combine(root, "package.json")),
            character.ToJsonString(), CharacterCookProfile.TrustedBuiltIn);
    }

    private static JsonNode RecoverySlot(JsonObject c) => c["slots"]!.AsArray().Single(s => (string?)s!["id"] == "ground.E")!;
    private static JsonNode Recovery(JsonObject c) => RecoverySlot(c)["timeline"]!["stages"]![0]!["operations"]![0]!;

    private static string Fixture(string name)
    {
        string relative = name == "cooked.expected.hex"
            ? "tests/Shared.Tests/Fixtures/FightGuyAuthoring/cooked.expected.hex"
            : $"client/Unity/Assets/CharacterPackages/fightguy/{name}";
        return File.ReadAllText(FindRepoFile(relative));
    }
    private static CharacterCompileResult CompileCharacter(Action<JsonObject>? mutate = null, CharacterCookProfile profile = CharacterCookProfile.TrustedBuiltIn)
    {
        var character = JsonNode.Parse(Fixture("character.json"))!.AsObject();
        mutate?.Invoke(character);
        return CharacterPackageCompiler.Compile(Fixture("package.json"), character.ToJsonString(), profile);
    }
    private static CharacterCompileResult CompileCapabilityOperation(Action<JsonObject> mutate)
        => CompileCharacter(character =>
        {
            var operation = JsonNode.Parse("""
                {"kind":"startCapability","tick":0,"unit":"ticks","capabilityId":"slop.internal.fightguy.rising-dragon.v1","capabilityVersion":"1","parameters":{"riseSpeed":11,"riseTicks":12,"riseDelay":8}}
                """)!.AsObject();
            mutate(operation);
            character["slots"]![0]!["timeline"]!["stages"]![0]!["operations"]!.AsArray().Add(operation);
        });
    private static string[] Codes(CharacterCompileResult result) => result.Diagnostics.Select(x => x.Code).ToArray();
    private static void AssertError(CharacterCompileResult result, string code)
    {
        Assert.Contains(result.Diagnostics, x => x.Severity == CharacterDiagnosticSeverity.Error && x.Code == code);
        Assert.Null(result.CookedPackage);
    }

    [Fact]
    public void FightGuy_TrustedProfile_CooksFourteenExplicitSlots()
    {
        var result = CharacterPackageCompiler.Compile(Fixture("package.json"), Fixture("character.json"), CharacterCookProfile.TrustedBuiltIn);
        Assert.NotNull(result.CookedPackage);
        Assert.DoesNotContain(result.Diagnostics, x => x.Severity == CharacterDiagnosticSeverity.Error);
        Assert.Equal(14, result.CookedPackage!.Definition.Slots.Count);
        Assert.Equal(14, result.CookedPackage.Budget.SlotCount);
        var second = CharacterPackageCompiler.Compile(Fixture("package.json"), Fixture("character.json"), CharacterCookProfile.TrustedBuiltIn);
        Assert.Equal(result.CookedPackage.CanonicalBytes, second.CookedPackage!.CanonicalBytes);
    }

    [Theory]
    [InlineData("tier2Ticks", 0)]
    [InlineData("tier2Ticks", 45)]
    [InlineData("tier3Ticks", 61)]
    [InlineData("fireTriggerTick", 0)]
    [InlineData("fireTriggerTick", 45)]
    [InlineData("tier2Radius", 0.5)]
    [InlineData("tier3Radius", 0.75)]
    public void ChargedProjectile_RejectsInvalidThresholdsAndDimensions(string field, double value)
    {
        AssertError(CompileCharacter(character =>
        {
            var slot = character["slots"]!.AsArray().Single(slot => slot!["id"]!.GetValue<string>() == "ground.A")!;
            var operation = slot["timeline"]!["stages"]![0]!["operations"]!.AsArray()
                .Single(op => op!["kind"]!.GetValue<string>() == "startCapability")!;
            operation["parameters"]![field] = value;
        }), "value.out-of-range");
    }

    [Fact]
    public void ChargedProjectile_RejectsCompetingProjectileLifecycle()
    {
        AssertError(CompileCharacter(character =>
        {
            var slot = character["slots"]!.AsArray().Single(slot => slot!["id"]!.GetValue<string>() == "ground.A")!;
            var operations = slot["timeline"]!["stages"]![0]!["operations"]!.AsArray();
            var charge = operations.Single(op => op!["kind"]!.GetValue<string>() == "startCapability")!;
            operations.Add(new JsonObject
            {
                ["kind"] = "spawnProjectile", ["tick"] = 8, ["unit"] = "meters",
                ["projectile"] = charge["parameters"]!["projectile"]!.DeepClone(),
            });
        }), "capability.ambiguous");
    }

    [Fact]
    public void AirDodgeSpeed_CooksAndAdaptsIndependentlyFromDashSpeed()
    {
        var result = CompileCharacter(character => character["movement"]!["airDodgeSpeed"] = 12.5f);
        Assert.NotNull(result.CookedPackage);
        Assert.Equal(20f, result.CookedPackage!.Definition.Movement.DashSpeed);
        Assert.Equal(12.5f, result.CookedPackage.Definition.Movement.AirDodgeSpeed);
        var runtime = CookedCharacterRuntimeAdapter.ToCharacterDefinition(result.CookedPackage);
        Assert.Equal(12.5f, runtime.Movement.AirDodgeSpeed);
        Assert.Contains("\"airDodgeSpeed\":12.5", System.Text.Encoding.UTF8.GetString(result.CookedPackage.CanonicalBytes));

        AssertError(CompileCharacter(character => character["movement"]!["airDodgeSpeed"] = 0f), "value.out-of-range");
        AssertError(CompileCharacter(character => character["movement"]!["airDodgeSpeed"] = -1f), "value.out-of-range");
    }

    [Fact]
    public void ShieldRadius_CooksAndAdaptsExactly_AndRejectsInvalidGeometry()
    {
        var compiled = CompileCharacter();
        Assert.NotNull(compiled.CookedPackage);
        Assert.Equal(1.05f, compiled.CookedPackage!.Definition.ShieldRadius);
        var runtime = CookedCharacterRuntimeAdapter.ToCharacterDefinition(compiled.CookedPackage);
        Assert.Equal(1.05f, runtime.ShieldRadius);
        Assert.Contains("\"shieldRadius\":1.05", System.Text.Encoding.UTF8.GetString(compiled.CookedPackage.CanonicalBytes));

        AssertError(CompileCharacter(character => character["shieldRadius"] = 0f), "value.out-of-range");
        AssertError(CompileCharacter(character => character["shieldRadius"] = -1f), "value.out-of-range");
        AssertError(CompileCharacter(character => character["shieldRadius"] = 0.85f), "value.out-of-range");
        AssertError(CompileCharacter(character => character.Remove("shieldRadius")), "schema.missing");
        var source = CharacterPackageSourceCodec.Load(Fixture("package.json"), Fixture("character.json")).Source!;
        var nonFinite = source with { Character = source.Character with { ShieldRadius = float.PositiveInfinity } };
        AssertError(CharacterPackageCompiler.Compile(nonFinite, CharacterCookProfile.TrustedBuiltIn), "value.non-finite");
    }
    [Fact]
    public void ShieldRadius_IsPreservedForEveryPackageFighter()
    {
        foreach (var (packageId, expected) in new[]
        {
            ("fightguy", 1.05f), ("wibou", 1.05f), ("bonk", 1.05f), ("manki", 0.95f),
        })
        {
            var result = CharacterPackageCompiler.Compile(
                File.ReadAllText(FindRepoFile($"client/Unity/Assets/CharacterPackages/{packageId}/package.json")),
                File.ReadAllText(FindRepoFile($"client/Unity/Assets/CharacterPackages/{packageId}/character.json")),
                CharacterCookProfile.TrustedBuiltIn);
            Assert.NotNull(result.CookedPackage);
            Assert.Equal(expected, result.CookedPackage!.Definition.ShieldRadius);
            Assert.Equal(expected, CookedCharacterRuntimeAdapter.ToCharacterDefinition(result.CookedPackage).ShieldRadius);
        }
    }
    [Fact]
    public void CaptureGeometryAndDefenseRoles_CookDeterministicallyIntoContent()
    {
        var baseline = CompileCharacter();
        var configured = CompileCharacter(character =>
        {
            character["captureGeometry"]!["reach"] = 1.1f;
            character["captureGeometry"]!["attackerAnchor"]!["z"] = 0.22f;
            character["presentation"]!["shield"] = "anim.fightguy.shield";
            character["presentation"]!["grab"] = "anim.fightguy.grab";
            character["presentation"]!["grabbed"] = "anim.fightguy.grabbed";
            character["presentation"]!["throwForward"] = "anim.fightguy.throw-forward";
            character["presentation"]!["airDodge"] = "anim.fightguy.air-dodge";
        });

        Assert.NotNull(baseline.CookedPackage);
        Assert.NotNull(configured.CookedPackage);
        var definition = configured.CookedPackage!.Definition;
        Assert.Equal(1.1f, definition.CaptureGeometry.Reach);
        Assert.Equal(0.22f, definition.CaptureGeometry.AttackerAnchor.Z);
        Assert.Equal("anim.fightguy.shield", definition.Presentation.Shield);
        Assert.Equal("anim.fightguy.grab", definition.Presentation.Grab);
        Assert.Equal("anim.fightguy.grabbed", definition.Presentation.Grabbed);
        Assert.Equal("anim.fightguy.throw-forward", definition.Presentation.ThrowForward);
        Assert.Equal("anim.fightguy.air-dodge", definition.Presentation.AirDodge);
        Assert.False(baseline.CookedPackage!.CanonicalBytes.SequenceEqual(configured.CookedPackage.CanonicalBytes));

        var repeated = CompileCharacter(character =>
        {
            character["captureGeometry"]!["reach"] = 1.1f;
            character["captureGeometry"]!["attackerAnchor"]!["z"] = 0.22f;
            character["presentation"]!["shield"] = "anim.fightguy.shield";
            character["presentation"]!["grab"] = "anim.fightguy.grab";
            character["presentation"]!["grabbed"] = "anim.fightguy.grabbed";
            character["presentation"]!["throwForward"] = "anim.fightguy.throw-forward";
            character["presentation"]!["airDodge"] = "anim.fightguy.air-dodge";
        });
        Assert.Equal(configured.CookedPackage.CanonicalBytes, repeated.CookedPackage!.CanonicalBytes);

        AssertError(CompileCharacter(character => character["captureGeometry"]!["reach"] = 0), "value.out-of-range");
        AssertError(CompileCharacter(character => character["presentation"]!["shield"] = "anim.idle"), "id.duplicate");
    }

    [Fact]
    public void SlideCarry_CompilesGroundNormalsAndDefaultsFalseElsewhere()
    {
        var result = CompileCharacter();
        Assert.NotNull(result.CookedPackage);
        Assert.DoesNotContain(result.Diagnostics, x => x.Severity == CharacterDiagnosticSeverity.Error);
        foreach (var id in new[] { "ground.1", "ground.3", "ground.4" })
            Assert.True(result.CookedPackage!.Definition.Slots.Single(x => x.Id == id).AllowSlideCarry);
        Assert.False(result.CookedPackage!.Definition.Slots.Single(x => x.Id == "ground.A").AllowSlideCarry);
        Assert.False(result.CookedPackage.Definition.Slots.Single(x => x.Id == "air.1").AllowSlideCarry);
    }

    [Fact]
    public void SlideCarry_AliasProjectsValueAndRejectsMotionConflict()
    {
        var aliased = CompileCharacter(character =>
        {
            var slots = (JsonArray)character["slots"]!;
            slots.Remove(slots.Single(slot => slot!["id"]!.GetValue<string>() == "ground.3"));
            ((JsonArray)character["aliases"]!).Add(new JsonObject { ["from"] = "ground.3", ["to"] = "ground.1" });
        });
        Assert.NotNull(aliased.CookedPackage);
        Assert.True(aliased.CookedPackage!.Definition.Slots.Single(x => x.Id == "ground.3").AllowSlideCarry);

        var conflict = CompileCharacter(character =>
        {
            var operations = (JsonArray)character["slots"]![0]!["timeline"]!["stages"]![0]!["operations"]!;
            operations.Add(new JsonObject
            {
                ["kind"] = "setVelocity",
                ["tick"] = 1,
                ["unit"] = "metersPerSecond",
                ["velocityMode"] = "additive",
                ["x"] = 1,
                ["y"] = 0,
                ["z"] = 0,
            });
        });
        AssertError(conflict, "slot.slide-carry.motion-conflict");
    }

    [Fact]
    public void HitPresentationId_RoundTripsThroughSourceCookRuntimeAndAliasProjection()
    {
        var json = JsonNode.Parse(Fixture("character.json"))!.AsObject();
        json["presentationIds"]!.AsArray().Add("presentation.test.hit");
        var ground = json["slots"]!.AsArray().Single(x => x!["id"]!.GetValue<string>() == "ground.1")!.AsObject();
        var target = json["slots"]!.AsArray().Single(x => x!["id"]!.GetValue<string>() == "ground.3")!;
        target["hitPresentationId"] = "presentation.test.hit";
        var air = json["slots"]!.AsArray().Single(x => x!["id"]!.GetValue<string>() == "air.1")!;
        air["hitPresentationId"] = "presentation.test.hit";
        var slotIndex = json["slots"]!.AsArray().IndexOf(ground);
        ((JsonArray)json["slots"]!).RemoveAt(slotIndex);
        ((JsonArray)json["aliases"]!).Add(new JsonObject { ["from"] = "ground.1", ["to"] = "ground.3" });

        var loaded = CharacterPackageSourceCodec.Load(Fixture("package.json"), json.ToJsonString());
        Assert.True(loaded.IsValid, string.Join("\n", loaded.Diagnostics));
        var rewritten = CharacterPackageSourceCodec.SerializeCharacter(loaded.Source!.Character);
        var reloaded = CharacterPackageSourceCodec.Load(Fixture("package.json"), rewritten);
        Assert.True(reloaded.IsValid, string.Join("\n", reloaded.Diagnostics));
        Assert.Equal("presentation.test.hit", reloaded.Source!.Character.Slots.Single(x => x.Id == "ground.3").HitPresentationId);

        var compiled = CharacterPackageCompiler.Compile(loaded.Source, CharacterCookProfile.TrustedBuiltIn);
        Assert.NotNull(compiled.CookedPackage);
        var cooked = compiled.CookedPackage!.Definition.Slots.Single(x => x.Id == "ground.1");
        Assert.Equal("presentation.test.hit", cooked.HitPresentationId);
        Assert.Equal("presentation.test.hit", compiled.CookedPackage.Definition.Slots.Single(x => x.Id == "air.1").HitPresentationId);

        var definition = CookedCharacterRuntimeAdapter.ToCharacterDefinition(compiled.CookedPackage);
        Assert.Equal("presentation.test.hit", definition.GetCookedSlotAbility(AbilitySlots.Slot1, false)!.HitPresentationId);
        Assert.Equal("presentation.test.hit", definition.GetCookedSlotAbility(AbilitySlots.Slot1, true)!.HitPresentationId);
        Assert.Equal("presentation.test.hit", definition.GetCookedSlotAbility(AbilitySlots.Slot3, false)!.HitPresentationId);
        Assert.Equal(compiled.CookedPackage.CanonicalBytes, CharacterPackageCompiler.Compile(
            reloaded.Source!, CharacterCookProfile.TrustedBuiltIn).CookedPackage!.CanonicalBytes);
    }

    [Fact]
    public void HitPresentationId_RejectsUndeclaredPresentationAndAbsentUsesDefault()
    {
        var absent = CompileCharacter(character =>
        {
            foreach (var slot in character["slots"]!.AsArray())
                slot!.AsObject().Remove("hitPresentationId");
        });
        Assert.NotNull(absent.CookedPackage);
        Assert.All(absent.CookedPackage!.Definition.Slots, slot => Assert.Null(slot.HitPresentationId));
        Assert.Null(CookedCharacterRuntimeAdapter.ToCharacterDefinition(absent.CookedPackage).GetCookedSlotAbility(AbilitySlots.A, false)!.HitPresentationId);

        AssertError(CompileCharacter(x => x["slots"]![0]!["hitPresentationId"] = "presentation.unknown.hit"), "reference.unresolved");
        AssertError(CompileCharacter(x => x["slots"]![0]!["hitPresentationId"] = "Bad ID"), "id.invalid");
    }

    [Fact]
    public void TumbleAndLowPoses_CookAndAdaptWithoutChangingTimelineData()
    {
        var result = CompileCharacter(character =>
        {
            var presentation = (JsonObject)character["presentation"]!;
            presentation["tumble"] = "anim.tumble";
            presentation["crouch"] = "anim.fightguy.crouch";
            presentation["slide"] = "anim.fightguy.slide";
        });
        Assert.NotNull(result.CookedPackage);
        Assert.DoesNotContain(result.Diagnostics, x => x.Severity == CharacterDiagnosticSeverity.Error);
        Assert.Equal("anim.tumble", result.CookedPackage!.Definition.Presentation.Tumble);
        Assert.Equal("anim.fightguy.crouch", result.CookedPackage.Definition.Presentation.Crouch);
        Assert.Equal("anim.fightguy.slide", result.CookedPackage.Definition.Presentation.Slide);
        string canonical = System.Text.Encoding.UTF8.GetString(result.CookedPackage.CanonicalBytes);
        Assert.Contains("\"crouch\":\"anim.fightguy.crouch\"", canonical);
        Assert.Contains("\"slide\":\"anim.fightguy.slide\"", canonical);
        var runtime = CookedCharacterRuntimeAdapter.ToCharacterDefinition(result.CookedPackage);
        Assert.Equal("anim.tumble", runtime.TumbleAnim);
        Assert.Equal("anim.fightguy.crouch", runtime.CrouchAnim);
        Assert.Equal("anim.fightguy.slide", runtime.SlideAnim);
    }
    [Fact]
    public void AimMovementSpeedScale_CooksOnMobileSlotsWithRuntimeApi17()
    {
        var result = CompileCharacter(character =>
        {
            FightGuyFixture.WithoutTitleMatch(character);
            character["slots"]![0]!["allowSlideCarry"] = false;
            character["slots"]![0]!["aimMovement"] = "mobile";
            character["slots"]![0]!["aimMovementSpeedScale"] = 0.4;
        });
        Assert.NotNull(result.CookedPackage);
        Assert.Equal(0.4f, result.CookedPackage!.Definition.Slots.Single(x => x.Id == "ground.1").AimMovementSpeedScale);
        Assert.Equal("1.7.0", result.CookedPackage.Metadata.RuntimeApiMin);
        Assert.Contains("\"aimMovementSpeedScale\":0.4", System.Text.Encoding.UTF8.GetString(result.CookedPackage.CanonicalBytes));

        // Full speed is the default and stays out of the canonical bytes.
        var unscaled = CompileCharacter();
        Assert.DoesNotContain("aimMovementSpeedScale", System.Text.Encoding.UTF8.GetString(unscaled.CookedPackage!.CanonicalBytes));
    }

    [Theory]
    [InlineData("mobile", 0.0, "value.out-of-range")]
    [InlineData("mobile", 1.5, "value.out-of-range")]
    [InlineData("fixed", 0.4, "slot.aim-movement-speed.requires-mobile")]
    public void AimMovementSpeedScale_RejectsOutOfRangeOrFixedAim(string movement, double scale, string code)
    {
        var result = CompileCharacter(character =>
        {
            character["slots"]![0]!["allowSlideCarry"] = false;
            character["slots"]![0]!["aimMovement"] = movement;
            character["slots"]![0]!["aimMovementSpeedScale"] = scale;
        });
        AssertError(result, code);
    }

    [Fact]
    public void StageAttackRange_SurvivesCompileAndRemainsDeterministic()
    {
        CharacterCompileResult result = CompileCharacter(character =>
        {
            character["slots"]![0]!["allowSlideCarry"] = false;
            var stage = (JsonObject)character["slots"]![0]!["timeline"]!["stages"]![0]!;
            stage["attackRange"] = 3.5;
        });
        Assert.NotNull(result.CookedPackage);
        var stageResult = result.CookedPackage!.Definition.Slots.Single(x => x.Id == "ground.1").Timeline.Stages.Single();
        Assert.Equal(3.5f, stageResult.AttackRange);
        Assert.Equal(0f, stageResult.WarpRange);
        Assert.False(stageResult.UseTargetLock);
        Assert.False(stageResult.RotateTowardTarget);
        Assert.Equal(0f, stageResult.TrackingStrength);

        CharacterCompileResult repeated = CompileCharacter(character =>
        {
            character["slots"]![0]!["allowSlideCarry"] = false;
            var stage = (JsonObject)character["slots"]![0]!["timeline"]!["stages"]![0]!;
            stage["attackRange"] = 3.5;
        });
        Assert.Equal(result.CookedPackage.CanonicalBytes, repeated.CookedPackage!.CanonicalBytes);
    }

    [Theory]
    [InlineData("warpRange", "4.25")]
    [InlineData("useTargetLock", "true")]
    [InlineData("rotateTowardTarget", "true")]
    [InlineData("trackingStrength", "0.4")]
    public void RetiredStageTargeting_IsRejected(string field, string value)
    {
        var result = CompileCharacter(character =>
        {
            character["slots"]![0]!["timeline"]!["stages"]![0]![field] = JsonNode.Parse(value);
        });
        AssertError(result, "timeline.targeting.retired");
    }

    [Fact]
    public void CookedBoneGeometryAgreesAcrossProjectionRuntimeAndCpuReach()
    {
        var compiled = CompileCharacter(character =>
        {
            character["hurtboxBoneDefs"]![1]!["boneId"] = "bone.spine";
            var operations = character["slots"]![0]!["timeline"]!["stages"]![0]!["operations"]!.AsArray();
            var hit = operations.Single(op => op!["kind"]!.GetValue<string>() == "spawnHitbox")!["hitbox"]!;
            hit["startBoneId"] = "bone.spine";
            hit["endBoneId"] = "bone.left-hand";
        });
        Assert.NotNull(compiled.CookedPackage);
        var def = CookedCharacterRuntimeAdapter.ToCharacterDefinition(compiled.CookedPackage!);
        var slot = def.GetCookedSlotAbility(AbilitySlots.Slot1, false)!;
        var authored = slot.Timeline.Stages[0].Operations.OfType<CookedSpawnHitboxOperation>().Single();
        var projected = Assert.Single(def.GetCookedSlotAbility(3, false)!.Timeline.Stages[0].ReadHitboxes());
        Assert.Equal("mixamorig:Spine2", projected.BoneName);
        Assert.Equal("mixamorig:LeftHand", projected.EndBoneName);
        var baked = new BakedAnimationData
        {
            BoneNames = new[] { "mixamorig:Spine2", "mixamorig:LeftHand" },
            Animations = new[]
            {
                new BakedAnimationData.BakedAnim
                {
                    Name = slot.Timeline.Stages[0].AnimationIds.Single(),
                    Frames = new[] { new[] { 0f, 0f, 4f, 0f, 0f, 6f } },
                },
            },
        };
        var sim = TestHelpers.MakeSim();
        sim.RegisterEntity(1, def, TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(def) }, baked);
        sim.ActivateAbility(1, new SlopArena.Shared.Abilities.CookedTimelineAbility(slot), 2, def);
        for (int tick = 0; tick < authored.Tick; tick++)
            sim.TickAbilities(new());

        var hitbox = Assert.Single(sim.Resolver.GetActiveHitboxes());
        TestHelpers.AssertNear(4f * def.HurtboxBoneScale + authored.Hitbox.OffsetZ, hitbox.Z);
        TestHelpers.AssertNear(6f * def.HurtboxBoneScale + authored.Hitbox.EndOffsetZ, hitbox.EndZ);
        Assert.True(SlopArena.Shared.AI.HeuristicBotPolicy.ForwardReach(def, AbilitySlots.Slot1, false, baked) >= hitbox.EndZ);
    }
    [Fact]
    public void Wibou_Package_CooksCanonicalSlotsAttachmentsAndSpread()
    {
        var result = CharacterPackageCompiler.Compile(
            File.ReadAllText(FindRepoFile("client/Unity/Assets/CharacterPackages/wibou/package.json")),
            File.ReadAllText(FindRepoFile("client/Unity/Assets/CharacterPackages/wibou/character.json")),
            CharacterCookProfile.TrustedBuiltIn);
        Assert.NotNull(result.CookedPackage);
        var package = result.CookedPackage!;
        Assert.Equal(14, package.Definition.Slots.Count);
        Assert.Equal(new[] { "_weapon_hilt", "_weapon_tip" }, package.Definition.AttachmentBoneIds.OrderBy(x => x).ToArray());
        var shuriken = package.Definition.Slots.Single(x => x.Id == "ground.A").Timeline.Stages.Single().Operations.OfType<CookedSpawnProjectileOperation>().ToArray();
        Assert.Equal(new[] { -15f, 0f, 15f }, shuriken.Select(x => x.Projectile.YawOffsetDegrees).OrderBy(x => x).ToArray());
        // Rising Slash is a plain cooldown move: Wibou has no stored uses.
        Assert.Null(package.Definition.Slots.Single(x => x.Id == "ground.E").ChargePool);
    }
    [Fact]
    public void DashStrike_IsPublicRoundTripAndValidatesCoverageAndLifecycle()
    {
        string manifest = File.ReadAllText(FindRepoFile("client/Unity/Assets/CharacterPackages/wibou/package.json"));
        var character = JsonNode.Parse(File.ReadAllText(FindRepoFile("client/Unity/Assets/CharacterPackages/wibou/character.json")))!.AsObject();
        var publicResult = CompileCharacter(fixture =>
        {
            fixture["capabilityRequirements"] = new JsonArray(new JsonObject {
                ["capabilityId"] = "slop.ability.dash-strike.v1", ["capabilityVersion"] = "1" });
            foreach (var slot in fixture["slots"]!.AsArray())
                foreach (var stage in slot!["timeline"]!["stages"]!.AsArray())
                {
                    var operations = stage!["operations"]!.AsArray();
                    for (int i = operations.Count - 1; i >= 0; i--)
                        if ((string?)operations[i]!["kind"] == "startCapability") operations.RemoveAt(i);
                }
            var op = DashOperation(character).DeepClone(); op["parameters"]!["tipPresentationId"] = "";
            var move = fixture["slots"]![0]!;
            move["behavior"] = "meleeCombo"; move["aimMode"] = "none"; move["allowSlideCarry"] = false;
            var phase = move["timeline"]!["stages"]![0]!;
            phase["durationTicks"] = 80; phase["iasaTicks"] = 0; phase["operations"] = new JsonArray(op);
        }, CharacterCookProfile.Workshop);
        Assert.True(publicResult.CookedPackage != null, string.Join("; ", publicResult.Diagnostics.Select(x => x.Message)));
        var parsed = CharacterPackageSourceCodec.Load(manifest, character.ToJsonString());
        Assert.True(parsed.IsValid);
        var result = CharacterPackageCompiler.Compile(parsed.Source!, CharacterCookProfile.TrustedBuiltIn);
        Assert.True(result.CookedPackage != null, string.Join("; ", result.Diagnostics.Select(x => x.Message)));
        Assert.True(CharacterPackageCompiler.IsPublicCapability("slop.ability.dash-strike.v1", "1"));
        Assert.Equal("1.15.0", result.CookedPackage!.Metadata.RuntimeApiMin);
        Assert.Equal(result.CookedPackage.CanonicalBytes, CharacterPackageCompiler.Compile(manifest,
            CharacterPackageSourceCodec.SerializeCharacter(parsed.Source!.Character), CharacterCookProfile.TrustedBuiltIn).CookedPackage!.CanonicalBytes);
        foreach (var field in new[] { "speed", "distance" })
        {
            var copy = (JsonObject)character.DeepClone();
            DashOperation(copy)["parameters"]![field] = 0;
            AssertError(CharacterPackageCompiler.Compile(manifest, copy.ToJsonString(), CharacterCookProfile.TrustedBuiltIn), "value.out-of-range");
        }
        var gap = (JsonObject)character.DeepClone();
        DashOperation(gap)["parameters"]!["lightHitbox"]!["radius"] = .1;
        AssertError(CharacterPackageCompiler.Compile(manifest, gap.ToJsonString(), CharacterCookProfile.TrustedBuiltIn), "value.out-of-range");
        var hold = (JsonObject)character.DeepClone();
        DashSlot(hold)["aimMode"] = "groundVector";
        AssertError(CharacterPackageCompiler.Compile(manifest, hold.ToJsonString(), CharacterCookProfile.TrustedBuiltIn), "capability.ambiguous");
        var retired = character.ToJsonString().Replace("slop.ability.dash-strike.v1", "slop.ability.charged-directional-dash.v1");
        AssertError(CharacterPackageCompiler.Compile(manifest, retired, CharacterCookProfile.TrustedBuiltIn), "capability.retired");
    }

    private static JsonObject DashSlot(JsonObject character)
        => character["slots"]!.AsArray().Select(node => node!.AsObject())
            .Single(slot => (string?)slot["id"] == "ground.R");

    [Fact]
    public void SteeredCharge_IsPublicCooksRuntimeApi18AndValidatesBounds()
    {
        string manifest = Fixture("package.json");
        var character = JsonNode.Parse(Fixture("character.json"))!.AsObject();
        SteeredChargeFixture.Apply(character);
        var compiled = CharacterPackageCompiler.Compile(manifest, character.ToJsonString(), CharacterCookProfile.Workshop);
        Assert.True(compiled.CookedPackage != null,
            string.Join("; ", compiled.Diagnostics.Select(d => $"{d.Code}: {d.Message} ({d.Path})")));
        Assert.Equal("1.8.0", compiled.CookedPackage!.Metadata.RuntimeApiMin);
        var charge = Assert.IsType<CookedSteeredChargeCapabilityParameters>(compiled.CookedPackage.Definition.Slots
            .Single(x => x.Id == "ground.R").Timeline.Stages.Single().Operations
            .OfType<CookedStartCapabilityOperation>().Single().Parameters);
        Assert.Equal((12f, 180f, (ushort)60, true), (charge.GallopSpeed, charge.TurnDegreesPerSecond, charge.MaxGallopTicks, charge.Armored));
        Assert.Equal(("anim.fix.r-gallop", (ushort)24, "anim.fix.r-air"),
            (charge.GallopAnimationId, charge.GallopLoopTicks, charge.AirEndingAnimationId));
        Assert.Equal(10f, charge.LanceHitbox.Damage);

        var loadedSource = CharacterPackageSourceCodec.Load(manifest, character.ToJsonString());
        Assert.True(loadedSource.IsValid, string.Join("; ", loadedSource.Diagnostics.Select(x => x.Message)));
        string roundTripped = CharacterPackageSourceCodec.SerializeCharacter(loadedSource.Source!.Character);
        Assert.Equal(compiled.CookedPackage.CanonicalBytes, CharacterPackageCompiler.Compile(manifest, roundTripped,
            CharacterCookProfile.Workshop).CookedPackage!.CanonicalBytes);

        CharacterCompileResult Mutated(Action<JsonObject> mutate)
        {
            var copy = (JsonObject)character.DeepClone();
            mutate(copy);
            return CharacterPackageCompiler.Compile(manifest, copy.ToJsonString(), CharacterCookProfile.Workshop);
        }
        AssertError(Mutated(c => SteeredChargeFixture.ChargeOperation(c)["parameters"]!["gallopSpeed"] = 0), "value.out-of-range");
        AssertError(Mutated(c => SteeredChargeFixture.ChargeOperation(c)["parameters"]!["turnDegreesPerSecond"] = 5000), "value.out-of-range");
        AssertError(Mutated(c => SteeredChargeFixture.ChargeOperation(c)["parameters"]!["skidTicks"] = 31), "value.out-of-range");
        AssertError(Mutated(c => SteeredChargeFixture.ChargeOperation(c)["parameters"]!["gallopLoopTicks"] = 0), "value.out-of-range");
        AssertError(Mutated(c => SteeredChargeFixture.ChargeOperation(c)["parameters"]!["lanceHitbox"]!["hitGroup"] = 1), "value.out-of-range");
        AssertError(Mutated(c => SteeredChargeFixture.ChargeOperation(c)["parameters"]!["hitEndingAnimationId"] = "anim.fix.r-elsewhere"),
            "reference.unresolved");
        AssertError(Mutated(c => SteeredChargeFixture.ChargeOperation(c)["parameters"]!["missEndingAnimationId"] = "anim.fix.r"),
            "value.out-of-range");
        AssertError(Mutated(c => SteeredChargeFixture.ChargeOperation(c)["parameters"]!["unknownField"] = 1), "operation.parameter-unknown");
        AssertError(Mutated(c => SteeredChargeFixture.ChargeOperation(c)["parameters"]!.AsObject().Remove("lookDownTicks")),
            "operation.parameter-missing");
        AssertError(Mutated(c => SteeredChargeFixture.ChargeOperation(c)["capabilityVersion"] = "2"), "capability.version-mismatch");
        AssertError(Mutated(c => SteeredChargeFixture.Slot(c, "ground.R")["timeline"]!["stages"]![0]!["iasaTicks"] = 1),
            "capability.ambiguous");
        AssertError(Mutated(c => ((JsonArray)SteeredChargeFixture.Slot(c, "ground.R")["timeline"]!["stages"]![0]!["operations"]!)
            .Add(JsonNode.Parse("""
                {"kind":"forwardLunge","tick":2,"unit":"metersPerSecond","speed":4,"durationTicks":4}
                """))), "capability.ambiguous");
        AssertError(Mutated(c => ((JsonArray)SteeredChargeFixture.Slot(c, "ground.R")["timeline"]!["stages"]![0]!["operations"]!)
            .Add(JsonNode.Parse("""
                {"kind":"startupAimCorrection","tick":0,"unit":"ticks","endTick":14,"acquisitionRange":8,"acquisitionHalfAngleDegrees":90,"maxYawDegrees":90,"maxPitchDegrees":0,"yawDegreesPerSecond":720,"pitchDegreesPerSecond":0}
                """))), "operation.cutoff-after-commitment");
    }

    [Fact]
    public void ReturningProjectile_IsPublicCooksRuntimeApi18AndValidatesItsReturn()
    {
        string manifest = Fixture("package.json");
        var character = JsonNode.Parse(Fixture("character.json"))!.AsObject();
        ReturningProjectileFixture.Apply(character);
        var compiled = CharacterPackageCompiler.Compile(manifest, character.ToJsonString(), CharacterCookProfile.Workshop);
        Assert.True(compiled.CookedPackage != null,
            string.Join("; ", compiled.Diagnostics.Select(d => $"{d.Code}: {d.Message} ({d.Path})")));
        Assert.Equal("1.8.0", compiled.CookedPackage!.Metadata.RuntimeApiMin);
        var lid = compiled.CookedPackage.Definition.Slots.Single(x => x.Id == "ground.1").Timeline.Stages.Single()
            .Operations.OfType<CookedSpawnProjectileOperation>().Single().Projectile;
        Assert.Equal(new CookedProjectileReturn(20, 16f, 80f, 0f, 30, ReturningProjectileFixture.CatchPresentationId, ""),
            lid.Return);

        var loadedSource = CharacterPackageSourceCodec.Load(manifest, character.ToJsonString());
        Assert.True(loadedSource.IsValid, string.Join("; ", loadedSource.Diagnostics.Select(x => x.Message)));
        string roundTripped = CharacterPackageSourceCodec.SerializeCharacter(loadedSource.Source!.Character);
        Assert.Equal(compiled.CookedPackage.CanonicalBytes, CharacterPackageCompiler.Compile(manifest, roundTripped,
            CharacterCookProfile.Workshop).CookedPackage!.CanonicalBytes);

        CharacterCompileResult Mutated(Action<JsonObject> mutate)
        {
            var copy = (JsonObject)character.DeepClone();
            mutate(copy);
            return CharacterPackageCompiler.Compile(manifest, copy.ToJsonString(), CharacterCookProfile.Workshop);
        }
        AssertError(Mutated(c => ReturningProjectileFixture.Return(c)["outboundTicks"] = 0), "value.out-of-range");
        AssertError(Mutated(c => ReturningProjectileFixture.Return(c)["outboundTicks"] = 240), "value.out-of-range");
        AssertError(Mutated(c => ReturningProjectileFixture.Return(c)["returnSpeed"] = 0), "value.out-of-range");
        AssertError(Mutated(c => ReturningProjectileFixture.Return(c)["returnAcceleration"] = -1), "value.out-of-range");
        AssertError(Mutated(c => ReturningProjectileFixture.Return(c)["selfHitDamage"] = -1), "value.out-of-range");
        AssertError(Mutated(c => ReturningProjectileFixture.Return(c)["selfHitStunTicks"] = 0), "value.out-of-range");
        AssertError(Mutated(c => ReturningProjectileFixture.Return(c)["selfHitStunTicks"] = 241), "value.out-of-range");
        AssertError(Mutated(c => ReturningProjectileFixture.Projectile(c)["gravity"] = -9.8), "value.out-of-range");
        AssertError(Mutated(c => ReturningProjectileFixture.Return(c)["catchPresentationId"] = "presentation.fightguy.undeclared"),
            "reference.unresolved");
        AssertError(Mutated(c => ReturningProjectileFixture.Return(c)["selfHitPresentationId"] = "lid.bonk"), "id.invalid");
        AssertError(Mutated(c => ReturningProjectileFixture.Return(c)["bounces"] = 2), "field.unknown");
        AssertError(Mutated(c => ReturningProjectileFixture.Return(c).Remove("returnSpeed")), "schema.missing");
        AssertError(Mutated(c => ReturningProjectileFixture.Operations(c).Add(ReturningProjectileFixture.Operation())),
            "capability.ambiguous");

        var chargedWithReturn = CompileCharacter(c =>
        {
            var charge = ((JsonArray)SteeredChargeFixture.Slot(c, "ground.A")["timeline"]!["stages"]![0]!["operations"]!)
                .Single(x => (string?)x!["kind"] == "startCapability")!;
            charge["parameters"]!["projectile"]!["return"] = ReturningProjectileFixture.Operation()["projectile"]!["return"]!.DeepClone();
        });
        Assert.Contains(chargedWithReturn.Diagnostics, x => x.Code == "value.out-of-range"
            && x.Path == "character.operation.parameters.projectile.return");
        Assert.Null(chargedWithReturn.CookedPackage);
    }

    private static JsonObject DashOperation(JsonObject character)
        => DashSlot(character)["timeline"]!["stages"]![0]!["operations"]![0]!.AsObject();

    [Fact]
    public void ForwardLungeRequiresPositiveSpeedAndStageBoundedDuration()
    {
        CharacterCompileResult zeroSpeed = CompileCharacter(character =>
        {
            var operations = (JsonArray)character["slots"]![0]!["timeline"]!["stages"]![0]!["operations"]!;
            operations.Add(new JsonObject
            {
                ["kind"] = "forwardLunge",
                ["tick"] = 1,
                ["unit"] = "metersPerSecond",
                ["speed"] = 0,
                ["durationTicks"] = 1,
            });
        });
        AssertError(zeroSpeed, "value.out-of-range");

        CharacterCompileResult pastStage = CompileCharacter(character =>
        {
            var stage = (JsonObject)character["slots"]![0]!["timeline"]!["stages"]![0]!;
            var operations = (JsonArray)stage["operations"]!;
            operations.Add(new JsonObject
            {
                ["kind"] = "forwardLunge",
                ["tick"] = (int)stage["durationTicks"]! - 1,
                ["unit"] = "metersPerSecond",
                ["speed"] = 1,
                ["durationTicks"] = 2,
            });
        });
        AssertError(pastStage, "value.out-of-range");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForwardLungeRangeBrakingSurvivesSourceRoundTripAndCompilation(bool enabled)
    {
        var character = JsonNode.Parse(Fixture("character.json"))!.AsObject();
        character["slots"]![0]!["allowSlideCarry"] = false;
        var operations = character["slots"]![0]!["timeline"]!["stages"]![0]!["operations"]!.AsArray();
        operations.RemoveAt(0);
        operations.Insert(0, new JsonObject
        {
            ["kind"] = "forwardLunge", ["tick"] = 0, ["unit"] = "metersPerSecond",
            ["speed"] = 3, ["durationTicks"] = 2, ["stopInAttackRange"] = enabled,
        });
        var source = CharacterPackageSourceCodec.Load(Fixture("package.json"), character.ToJsonString()).Source!;
        var roundTrip = CharacterPackageSourceCodec.Load(Fixture("package.json"),
            CharacterPackageSourceCodec.SerializeCharacter(source.Character)).Source!;
        var operation = Assert.IsType<ForwardLungeOperationSource>(
            roundTrip.Character.Slots[0].Timeline.Stages[0].Operations[0]);
        Assert.Equal(enabled, operation.StopInAttackRange);
        var compiled = CharacterPackageCompiler.Compile(roundTrip, CharacterCookProfile.TrustedBuiltIn);
        Assert.NotNull(compiled.CookedPackage);
        var cooked = Assert.IsType<CookedForwardLungeOperation>(
            compiled.CookedPackage!.Definition.Slots[0].Timeline.Stages[0].Operations[0]);
        Assert.Equal(enabled, cooked.StopInAttackRange);
    }

    [Fact]
    public void RangeBrakingRequiresASubsequentSameStageHitbox()
    {
        var compiled = CompileCharacter(character =>
        {
            var operations = character["slots"]![0]!["timeline"]!["stages"]![0]!["operations"]!.AsArray();
            operations.Clear();
            operations.Add(new JsonObject
            {
                ["kind"] = "forwardLunge", ["tick"] = 0, ["unit"] = "metersPerSecond",
                ["speed"] = 3, ["durationTicks"] = 2, ["stopInAttackRange"] = true,
            });
        });
        AssertError(compiled, "operation.forward-lunge.no-subsequent-hitbox");
    }

    [Fact]
    public void GravityWindowCompilesAsNormalizedTimedGravityAndValidatesBounds()
    {
        CharacterCompileResult compiled = CompileCharacter(character =>
        {
            character["slots"]![0]!["allowSlideCarry"] = false;
            ((JsonArray)character["slots"]![0]!["timeline"]!["stages"]![0]!["operations"]!).Add(new JsonObject
            {
                ["kind"] = "gravityWindow",
                ["tick"] = 2,
                ["unit"] = "normalized",
                ["gravityScale"] = 0.5,
                ["durationTicks"] = 4,
            });
        });

        Assert.True(compiled.CookedPackage != null,
            string.Join("; ", compiled.Diagnostics.Select(x => $"{x.Code}: {x.Message} ({x.Path})")));
        var operation = Assert.Single(compiled.CookedPackage.Definition.Slots
            .Single(slot => slot.Id == "ground.1").Timeline.Stages[0].Operations
            .OfType<CookedGravityWindowOperation>());
        Assert.Equal((AuthoringUnit.Normalized, 2f, 0.5f, (ushort)4),
            (operation.Unit, (float)operation.Tick, operation.GravityScale, operation.DurationTicks));

        AssertError(CompileCharacter(character =>
        {
            character["slots"]![0]!["allowSlideCarry"] = false;
            ((JsonArray)character["slots"]![0]!["timeline"]!["stages"]![0]!["operations"]!).Add(new JsonObject
            {
                ["kind"] = "gravityWindow",
                ["tick"] = 2,
                ["unit"] = "normalized",
                ["gravityScale"] = 1.01,
                ["durationTicks"] = 4,
            });
        }), "value.out-of-range");

        AssertError(CompileCharacter(character =>
        {
            character["slots"]![0]!["allowSlideCarry"] = false;
            var stage = (JsonObject)character["slots"]![0]!["timeline"]!["stages"]![0]!;
            ((JsonArray)stage["operations"]!).Add(new JsonObject
            {
                ["kind"] = "gravityWindow",
                ["tick"] = (int)stage["durationTicks"]! - 1,
                ["unit"] = "normalized",
                ["gravityScale"] = 0.5,
                ["durationTicks"] = 2,
            });
        }), "value.out-of-range");
    }

    [Fact]
    public void WorkshopRejectsTrustedCapabilities()
    {
        var result = CharacterPackageCompiler.Compile(Fixture("package.json"), Fixture("character.json"), CharacterCookProfile.Workshop);
        AssertError(result, "capability.untrusted");
    }

    [Fact]
    public void LegacyAndFutureSchemasFailClosed()
    {
        AssertError(CompileCharacter(x => x["authoringSchemaVersion"] = 1), "schema.unsupported");
        AssertError(CompileCharacter(x => x.Remove("authoringSchemaVersion")), "schema.missing");
        AssertError(CompileCharacter(x => { x["schemaVersion"] = 1; x["id"] = "fightguy"; x["class"] = "FightGuy"; }), "schema.unsupported");
        var manifest = JsonNode.Parse(Fixture("package.json"))!.AsObject();
        manifest["manifestSchemaVersion"] = 2;
        var result = CharacterPackageCompiler.Compile(manifest.ToJsonString(), Fixture("character.json"), CharacterCookProfile.TrustedBuiltIn);
        AssertError(result, "schema.unsupported");
    }

    [Fact]
    public void StrictReaderRejectsUnknownDuplicateAndIntegerEnumFields()
    {
        AssertError(CompileCharacter(x => x["unknown"] = true), "field.unknown");
        AssertError(CompileCharacter(x => x["movement"]!["unknown"] = true), "field.unknown");
        var duplicate = Fixture("character.json").Replace("\"displayName\": \"FightGuy\",", "\"displayName\": \"FightGuy\",\n  \"displayName\": \"FightGuy\",", StringComparison.Ordinal);
        AssertError(CharacterPackageCompiler.Compile(Fixture("package.json"), duplicate, CharacterCookProfile.TrustedBuiltIn), "field.duplicate");
        AssertError(CompileCharacter(x => x["slots"]![0]!["behavior"] = 0), "enum.unknown");
    }

    [Fact]
    public void StrictReaderRejectsOperationsAndParameters()
    {
        AssertError(CompileCharacter(x => x["slots"]![0]!["timeline"]!["stages"]![0]!["operations"]![0]!["kind"] = "branch"), "operation.unknown");
        AssertError(CompileCharacter(x => x["slots"]![0]!["timeline"]!["stages"]![0]!["operations"]![0]!["unit"] = "bogus"), "unit.unknown");
        AssertError(CompileCapabilityOperation(operation => operation["parameters"]!["extra"] = 1), "operation.parameter-unknown");
        AssertError(CompileCapabilityOperation(operation => operation["parameters"]!.AsObject().Remove("riseSpeed")), "operation.parameter-missing");
        AssertError(CompileCharacter(x => x["slots"]![0]!["timeline"]!["stages"]![0]!["operations"]![1]!["hitbox"]!["durationTicks"] = 0), "value.out-of-range");
        AssertError(CompileCharacter(x => x["slots"]![0]!["timeline"]!["stages"]![0]!["operations"]![1]!["hitbox"]!["radius"] = -1), "value.out-of-range");
    }

    [Fact]
    public void AliasesResolveAndRejectMissingCyclesDuplicates()
    {
        var missing = CompileCharacter(x => ((JsonArray)x["aliases"]!).RemoveAt(0));
        AssertError(missing, "alias.missing-target");
        var cycle = CompileCharacter(x => { x["aliases"]![0]!["to"] = "air.E"; x["aliases"]![1]!["to"] = "air.A"; });
        AssertError(cycle, "alias.cycle");
        var duplicateAlias = CompileCharacter(x => ((JsonArray)x["aliases"]!).Add(((JsonArray)x["aliases"]!)[0]!.DeepClone()));
        AssertError(duplicateAlias, "id.duplicate");
        var duplicateSlot = CompileCharacter(x => ((JsonArray)x["slots"]!).Add(((JsonArray)x["slots"]!)[0]!.DeepClone()));
        AssertError(duplicateSlot, "id.duplicate");
    }

    [Fact]
    public void CapabilityAdmissionIsExact()
    {
        AssertError(CompileCharacter(x => x["capabilityRequirements"]![0]!["capabilityId"] = "slop.internal.fightguy.dragon-beam.v1"), "capability.unknown");
        AssertError(CompileCharacter(x => x["capabilityRequirements"]![0]!["capabilityVersion"] = "2"), "capability.unknown");
        AssertError(CompileCapabilityOperation(operation => operation["capabilityVersion"] = "2"), "capability.version-mismatch");
    }

    [Fact]
    public void PresentationWarningsDoNotChangeCookedBytes()
    {
        var baseline = CompileCharacter();
        var warning = CompileCharacter(x => ((JsonArray)x["presentationIds"]!).Add("presentation.unused"));
        var warningRepeat = CompileCharacter(x => ((JsonArray)x["presentationIds"]!).Add("presentation.unused"));
        Assert.NotNull(warning.CookedPackage);
        Assert.Contains(warning.Diagnostics, x => x.Code == "presentation.unused-id" && x.Severity == CharacterDiagnosticSeverity.Warning);
        Assert.Equal(warning.CookedPackage!.CanonicalBytes, warningRepeat.CookedPackage!.CanonicalBytes);
    }
    
    [Fact]
    public void CanonicalBytesIgnoreWhitespaceAndPropertyOrderButPreserveOperationOrder()
    {
        var baseline = CompileCharacter();
        var reordered = JsonNode.Parse(Fixture("character.json"))!.AsObject();
        var compact = reordered.ToJsonString();
        var same = CharacterPackageCompiler.Compile(Fixture("package.json"), compact, CharacterCookProfile.TrustedBuiltIn);
        Assert.Equal(baseline.CookedPackage!.CanonicalBytes, same.CookedPackage!.CanonicalBytes);

        var swapped = CompileCharacter(x =>
        {
            var operations = (JsonArray)x["slots"]!.AsArray().Single(slot => slot!["id"]!.GetValue<string>() == "ground.E")!["timeline"]!["stages"]![0]!["operations"]!;
            operations[1]!["tick"] = operations[0]!["tick"]!.GetValue<int>();
        });
        Assert.NotEqual(Convert.ToHexString(baseline.CookedPackage.CanonicalBytes), Convert.ToHexString(swapped.CookedPackage!.CanonicalBytes));
    }

    [Fact]
    public void StartupAimCorrection_CooksBoundedCutoffAndRejectsLateOrDuplicateWindows()
    {

        AssertError(CompileCharacter(character =>
        {
            var operations = character["slots"]![0]!["timeline"]!["stages"]![0]!["operations"]!.AsArray();
            int firstContact = operations.First(operation => operation!["kind"]!.GetValue<string>() == "spawnHitbox")!["tick"]!.GetValue<int>();
            operations[0]!["endTick"] = firstContact + 1;
        }),
            "operation.cutoff-after-commitment");
        AssertError(CompileCharacter(character =>
        {
            var operations = (JsonArray)character["slots"]![0]!["timeline"]!["stages"]![0]!["operations"]!;
            operations.Add(JsonNode.Parse(operations[0]!.ToJsonString()));
        }), "operation.ambiguous");
        AssertError(CompileCharacter(character =>
            character["slots"]![0]!["timeline"]!["stages"]![0]!["operations"]![0]!["acquisitionRange"] = 33),
            "value.out-of-range");
    }

    [Theory]
    [InlineData("absolute")]
    [InlineData("additive")]
    public void StartupAimCorrectionCannotContinueAfterHorizontalVelocityLaunch(string velocityMode)
    {
        AssertError(CompileCharacter(character =>
        {
            var operations = character["slots"]![0]!["timeline"]!["stages"]![0]!["operations"]!.AsArray();
            operations.Insert(1, new JsonObject
            {
                ["kind"] = "setVelocity", ["tick"] = 1, ["unit"] = "metersPerSecond",
                ["velocityMode"] = velocityMode, ["x"] = 1f, ["y"] = 0f, ["z"] = 0f,
            });
        }), "operation.cutoff-after-commitment");
    }

    [Fact]
    public void ReferencesAndIdsAreValidated()
    {
        AssertError(CompileCharacter(x => x["hurtboxBoneDefs"]![0]!["boneId"] = "Bad Bone"), "id.invalid");
        AssertError(CompileCharacter(x => x["slots"]![0]!["timeline"]!["stages"]![0]!["animationIds"]![0] = "missing"), "reference.unresolved");
        AssertError(CompileCharacter(x => x["slots"]![0]!["timeline"]!["stages"]![0]!["operations"]![1]!["hitbox"]!["startBoneId"] = "bone.missing"), "reference.unresolved");
    }

    [Fact]
    public void NullInputAndTrailingDataFailAsDiagnostics()
    {
        var nullResult = CharacterPackageCompiler.Compile(null!, Fixture("character.json"));
        AssertError(nullResult, "schema.missing");
        var trailing = CharacterPackageCompiler.Compile(Fixture("package.json"), Fixture("character.json") + " {}", CharacterCookProfile.TrustedBuiltIn);
        AssertError(trailing, "schema.invalid-json");
    }
    private static string FindRepoFile(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(directory.FullName, relative);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException($"Could not locate repo file: {relative}");
    }

}
