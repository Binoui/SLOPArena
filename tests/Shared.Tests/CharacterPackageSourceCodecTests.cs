using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class CharacterPackageSourceCodecTests
{
    private static string Fixture(string name) => File.ReadAllText(FindRepoFile("client/Unity/Assets/CharacterPackages/fightguy/" + name));
    private static string FindRepoFile(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(directory.FullName, relative);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException(relative);
    }

    [Theory]
    [InlineData("")]
    [InlineData("anim.fightguy.wall-jump")]
    public void WallJump_RoundTripsAsOptionalPresentation(string animationId)
    {
        var character = JsonNode.Parse(Fixture("character.json"))!.AsObject();
        character["presentation"]!.AsObject().Remove("wallJump");
        if (animationId.Length > 0) character["presentation"]!["wallJump"] = animationId;
        var parsed = CharacterPackageSourceCodec.Load(Fixture("package.json"), character.ToJsonString());
        Assert.True(parsed.IsValid, string.Join("\n", parsed.Diagnostics));
        string serialized = CharacterPackageSourceCodec.SerializeCharacter(parsed.Source!.Character);
        var roundTrip = CharacterPackageSourceCodec.Load(Fixture("package.json"), serialized);
        Assert.True(roundTrip.IsValid, string.Join("\n", roundTrip.Diagnostics));
        Assert.Equal(animationId, roundTrip.Source!.Character.Presentation.WallJump);
        Assert.Equal(animationId.Length > 0, JsonNode.Parse(serialized)!["presentation"]!.AsObject().ContainsKey("wallJump"));
    }

    [Fact]
    public void HitPresentationId_RoundTripsThroughSourceEditAndRejectsNonString()
    {
        var json = JsonNode.Parse(Fixture("character.json"))!.AsObject();
        json["presentationIds"]!.AsArray().Add("presentation.test.hit");
        var slot = json["slots"]!.AsArray().Single(x => x!["id"]!.GetValue<string>() == "ground.A")!.AsObject();
        slot["hitPresentationId"] = "presentation.test.hit";
        var loaded = CharacterPackageSourceCodec.Load(Fixture("package.json"), json.ToJsonString());
        Assert.True(loaded.IsValid, string.Join("\n", loaded.Diagnostics));
        var edited = CharacterPackageSourceCodec.ReplaceSlot(loaded.Source!, 4, loaded.Source!.Character.Slots[4] with { Name = "Edited" });
        Assert.True(edited.IsValid);
        var rewritten = CharacterPackageSourceCodec.SerializeCharacter(edited.Source!.Character);
        var roundTrip = CharacterPackageSourceCodec.Load(Fixture("package.json"), rewritten);
        Assert.True(roundTrip.IsValid, string.Join("\n", roundTrip.Diagnostics));
        Assert.Equal("presentation.test.hit", roundTrip.Source!.Character.Slots.Single(x => x.Id == "ground.A").HitPresentationId);

        slot["hitPresentationId"] = 7;
        var malformed = CharacterPackageSourceCodec.Load(Fixture("package.json"), json.ToJsonString());
        Assert.Contains(malformed.Diagnostics, diagnostic => diagnostic.Path.EndsWith(".hitPresentationId", StringComparison.Ordinal)
            && diagnostic.Code == "value.out-of-range");
    }

    [Fact]
    public void CapsuleHurtbox_RoundTripKeepsBothBonesAndOffsets()
    {
        var source = CharacterPackageSourceCodec.Load(Fixture("package.json"), Fixture("character.json")).Source!;
        var bone = new HurtboxBoneSource("bone.head", .1f, .2f, .3f, .15f, "bone.neck", -.1f, -.2f, -.3f);
        source = source with { Character = source.Character with { HurtboxBoneDefs = new[] { bone } } };
        var loaded = CharacterPackageSourceCodec.Load(Fixture("package.json"), CharacterPackageSourceCodec.SerializeCharacter(source.Character));
        Assert.True(loaded.IsValid, string.Join("\n", loaded.Diagnostics));
        Assert.Equal(bone, Assert.Single(loaded.Source!.Character.HurtboxBoneDefs));
    }

    [Fact]
    public void FightGuy_RoundTripsDeterministically()
    {
        var first = CharacterPackageSourceCodec.Load(Fixture("package.json"), Fixture("character.json"));
        Assert.Equal(11f, first.Source!.Character.Movement.AirDodgeSpeed);
        Assert.Equal(1.05f, first.Source.Character.ShieldRadius);
        var second = CharacterPackageSourceCodec.Load(CharacterPackageSourceCodec.SerializeManifest(first.Source.Manifest), CharacterPackageSourceCodec.SerializeCharacter(first.Source.Character));
        Assert.True(second.IsValid);
        Assert.Equal(CharacterPackageSourceCodec.SerializeManifest(first.Source.Manifest), CharacterPackageSourceCodec.SerializeManifest(second.Source!.Manifest));
        Assert.Equal(CharacterPackageSourceCodec.SerializeCharacter(first.Source.Character), CharacterPackageSourceCodec.SerializeCharacter(second.Source.Character));
        Assert.Equal(11f, second.Source.Character.Movement.AirDodgeSpeed);
        Assert.Equal(1.05f, second.Source.Character.ShieldRadius);
    }

    [Fact]
    public void StartupAimCorrection_StrictlyRoundTripsItsCutoffAndLimits()
    {
        var parsed = CharacterPackageSourceCodec.Load(Fixture("package.json"), Fixture("character.json"));
        Assert.True(parsed.IsValid, string.Join("\n", parsed.Diagnostics));
        var source = parsed.Source!.Character.Slots.Single(x => x.Id == "air.1")
            .Timeline.Stages[0].Operations.OfType<StartupAimCorrectionOperationSource>().Single();
        Assert.Equal((ushort)0, source.Tick);
        Assert.Equal((ushort)8, source.EndTick);
        Assert.Equal(15f, source.MaxPitchDegrees);
        Assert.Equal(180f, source.PitchDegreesPerSecond);

        string serialized = CharacterPackageSourceCodec.SerializeCharacter(parsed.Source.Character);
        Assert.Contains("\"kind\": \"startupAimCorrection\"", serialized);
        Assert.Contains("\"endTick\": 8", serialized);
        var roundTrip = CharacterPackageSourceCodec.Load(Fixture("package.json"), serialized);
        Assert.True(roundTrip.IsValid, string.Join("\n", roundTrip.Diagnostics));
        var cooked = CharacterPackageCompiler.Compile(roundTrip.Source!, CharacterCookProfile.TrustedBuiltIn);
        Assert.NotNull(cooked.CookedPackage);
        var operation = Assert.IsType<CookedStartupAimCorrectionOperation>(
            cooked.CookedPackage!.Definition.Slots.Single(x => x.Id == "air.1").Timeline.Stages[0].Operations[0]);
        Assert.Equal((ushort)8, operation.EndTick);
        Assert.Equal(15f, operation.MaxPitchDegrees);

        var malformed = JsonNode.Parse(serialized)!.AsObject();
        var normal = malformed["slots"]!.AsArray().Single(x => x!["id"]!.GetValue<string>() == "air.1")!;
        normal["timeline"]!["stages"]![0]!["operations"]![0]!["speed"] = 1;
        var rejected = CharacterPackageSourceCodec.Load(Fixture("package.json"), malformed.ToJsonString());
        Assert.False(rejected.IsValid);
        Assert.Contains(rejected.Diagnostics, x => x.Code == "operation.parameter-unknown");
    }

    [Fact]
    public void CaptureGeometryAndDefenseAnimationRoles_RoundTripThroughAuthoringSource()
    {
        var character = JsonNode.Parse(Fixture("character.json"))!.AsObject();
        character["captureGeometry"]!["reach"] = 1.15f;
        character["captureGeometry"]!["attackerAnchor"]!["z"] = 0.24f;
        character["presentation"]!["shield"] = "anim.fightguy.shield";
        character["presentation"]!["grab"] = "anim.fightguy.grab";
        character["presentation"]!["grabbed"] = "anim.fightguy.grabbed";
        character["presentation"]!["throwForward"] = "anim.fightguy.throw-forward";
        character["presentation"]!["airDodge"] = "anim.fightguy.air-dodge";

        var parsed = CharacterPackageSourceCodec.Load(Fixture("package.json"), character.ToJsonString());
        Assert.True(parsed.IsValid, string.Join("\n", parsed.Diagnostics));
        string serialized = CharacterPackageSourceCodec.SerializeCharacter(parsed.Source!.Character);
        var roundTrip = CharacterPackageSourceCodec.Load(Fixture("package.json"), serialized);

        Assert.True(roundTrip.IsValid, string.Join("\n", roundTrip.Diagnostics));
        var source = roundTrip.Source!.Character;
        Assert.Equal(1.15f, source.CaptureGeometry.Reach);
        Assert.Equal(0.24f, source.CaptureGeometry.AttackerAnchor.Z);
        Assert.Equal("anim.fightguy.shield", source.Presentation.Shield);
        Assert.Equal("anim.fightguy.grab", source.Presentation.Grab);
        Assert.Equal("anim.fightguy.grabbed", source.Presentation.Grabbed);
        Assert.Equal("anim.fightguy.throw-forward", source.Presentation.ThrowForward);
        Assert.Equal("anim.fightguy.air-dodge", source.Presentation.AirDodge);

        character.Remove("captureGeometry");
        var missing = CharacterPackageSourceCodec.Load(Fixture("package.json"), character.ToJsonString());
        Assert.Contains(missing.Diagnostics, x => x.Code == "schema.missing" && x.Path == "character.captureGeometry");
    }

    [Fact]
    public void EditingGrabGeometryUpdatesCookedCaptureWithoutChangingMoveSlots()
    {
        var parsed = CharacterPackageSourceCodec.Load(Fixture("package.json"), Fixture("character.json"));
        Assert.True(parsed.IsValid);
        var source = parsed.Source!;
        var geometry = source.Character.CaptureGeometry with
        {
            Reach = 0.93f,
            Width = 0.81f,
            VictimAnchor = source.Character.CaptureGeometry.VictimAnchor with { Z = 0.52f },
        };
        var edited = CharacterPackageSourceCodec.ReplaceCaptureGeometry(source, geometry);
        Assert.True(edited.IsValid, string.Join("\n", edited.Diagnostics));

        var compiled = CharacterPackageCompiler.Compile(edited.Source!, CharacterCookProfile.TrustedBuiltIn);
        Assert.DoesNotContain(compiled.Diagnostics, d => d.Severity == CharacterDiagnosticSeverity.Error);
        Assert.Equal(0.93f, compiled.CookedPackage!.Definition.CaptureGeometry.Reach);
        Assert.Equal(0.81f, compiled.CookedPackage.Definition.CaptureGeometry.Width);
        Assert.Equal(0.52f, compiled.CookedPackage.Definition.CaptureGeometry.VictimAnchor.Z);
    }

    [Fact]
    public void TumbleAndLowPoseBindings_AreOptionalAndRoundTripWhenDeclared()
    {
        var legacyJson = JsonNode.Parse(Fixture("character.json"))!.AsObject();
        ((JsonObject)legacyJson["presentation"]!).Remove("tumble");
        ((JsonObject)legacyJson["presentation"]!).Remove("crouch");
        ((JsonObject)legacyJson["presentation"]!).Remove("slide");
        var legacy = CharacterPackageSourceCodec.Load(Fixture("package.json"), legacyJson.ToJsonString());
        Assert.True(legacy.IsValid, string.Join("\n", legacy.Diagnostics));
        Assert.Equal("", legacy.Source!.Character.Presentation.Tumble);
        Assert.Equal("", legacy.Source.Character.Presentation.Crouch);
        Assert.Equal("", legacy.Source.Character.Presentation.Slide);
        Assert.DoesNotContain("\"tumble\"", CharacterPackageSourceCodec.SerializeCharacter(legacy.Source.Character));
        Assert.DoesNotContain("\"crouch\"", CharacterPackageSourceCodec.SerializeCharacter(legacy.Source.Character));
        Assert.DoesNotContain("\"slide\"", CharacterPackageSourceCodec.SerializeCharacter(legacy.Source.Character));

        var json = JsonNode.Parse(Fixture("character.json"))!.AsObject();
        var presentation = (JsonObject)json["presentation"]!;
        presentation["tumble"] = "anim.tumble";
        presentation["crouch"] = "anim.fightguy.crouch";
        presentation["slide"] = "anim.fightguy.slide";
        var parsed = CharacterPackageSourceCodec.Load(Fixture("package.json"), json.ToJsonString());
        Assert.True(parsed.IsValid, string.Join("\n", parsed.Diagnostics));
        Assert.Equal("anim.tumble", parsed.Source!.Character.Presentation.Tumble);
        Assert.Equal("anim.fightguy.crouch", parsed.Source.Character.Presentation.Crouch);
        Assert.Equal("anim.fightguy.slide", parsed.Source.Character.Presentation.Slide);
        string serialized = CharacterPackageSourceCodec.SerializeCharacter(parsed.Source.Character);
        Assert.Contains("\"crouch\": \"anim.fightguy.crouch\"", serialized);
        Assert.Contains("\"slide\": \"anim.fightguy.slide\"", serialized);
        var reparsed = CharacterPackageSourceCodec.Load(Fixture("package.json"), serialized);
        Assert.True(reparsed.IsValid, string.Join("\n", reparsed.Diagnostics));
        Assert.Equal("anim.fightguy.crouch", reparsed.Source!.Character.Presentation.Crouch);
        Assert.Equal("anim.fightguy.slide", reparsed.Source.Character.Presentation.Slide);
    }

    [Fact]
    public void AimMovementPolicy_RoundTripsAndMissingDefaultsToFixed()
    {
        var missingJson = JsonNode.Parse(Fixture("character.json"))!.AsObject();
        ((JsonObject)missingJson["slots"]![0]!).Remove("aimMovement");
        var missing = CharacterPackageSourceCodec.Load(Fixture("package.json"), missingJson.ToJsonString());
        Assert.True(missing.IsValid, string.Join("\n", missing.Diagnostics));
        Assert.Equal(AuthoringAimMovementMode.Fixed, missing.Source!.Character.Slots[0].AimMovement);

        var mobileJson = JsonNode.Parse(Fixture("character.json"))!.AsObject();
        ((JsonObject)mobileJson["slots"]![0]!)["aimMovement"] = "mobile";
        string mobileSource = mobileJson.ToJsonString();
        var mobile = CharacterPackageSourceCodec.Load(Fixture("package.json"), mobileSource);
        Assert.True(mobile.IsValid, string.Join("\n", mobile.Diagnostics));
        Assert.Equal(AuthoringAimMovementMode.Mobile, mobile.Source!.Character.Slots[0].AimMovement);
        Assert.Contains("\"aimMovement\": \"mobile\"", CharacterPackageSourceCodec.SerializeCharacter(mobile.Source.Character));

        var unknownJson = JsonNode.Parse(Fixture("character.json"))!.AsObject();
        ((JsonObject)unknownJson["slots"]![0]!)["aimMovement"] = "unknown";

        var unknown = CharacterPackageSourceCodec.Load(Fixture("package.json"), unknownJson.ToJsonString());
        Assert.Contains(unknown.Diagnostics, x => x.Code == "enum.unknown");
    }

    [Fact]
    public void AimMovementSpeedScale_RoundTripsAndMissingDefaultsToFullSpeed()
    {
        var missing = CharacterPackageSourceCodec.Load(Fixture("package.json"), Fixture("character.json"));
        Assert.True(missing.IsValid, string.Join("\n", missing.Diagnostics));
        Assert.Equal(1f, missing.Source!.Character.Slots[0].AimMovementSpeedScale);
        Assert.DoesNotContain("aimMovementSpeedScale", CharacterPackageSourceCodec.SerializeCharacter(missing.Source.Character));

        var scaledJson = JsonNode.Parse(Fixture("character.json"))!.AsObject();
        ((JsonObject)scaledJson["slots"]![0]!)["aimMovement"] = "mobile";
        ((JsonObject)scaledJson["slots"]![0]!)["aimMovementSpeedScale"] = 0.4;
        var scaled = CharacterPackageSourceCodec.Load(Fixture("package.json"), scaledJson.ToJsonString());
        Assert.True(scaled.IsValid, string.Join("\n", scaled.Diagnostics));
        Assert.Equal(0.4f, scaled.Source!.Character.Slots[0].AimMovementSpeedScale);
        var reparsed = CharacterPackageSourceCodec.Load(Fixture("package.json"),
            CharacterPackageSourceCodec.SerializeCharacter(scaled.Source.Character));
        Assert.True(reparsed.IsValid, string.Join("\n", reparsed.Diagnostics));
        Assert.Equal(0.4f, reparsed.Source!.Character.Slots[0].AimMovementSpeedScale);
    }

    [Fact]
    public void AllowSlideCarry_RoundTripsAndMissingDefaultsFalse()
    {
        var missingJson = JsonNode.Parse(Fixture("character.json"))!.AsObject();
        ((JsonObject)missingJson["slots"]![0]!).Remove("allowSlideCarry");
        var missing = CharacterPackageSourceCodec.Load(Fixture("package.json"), missingJson.ToJsonString());
        Assert.True(missing.IsValid, string.Join("\n", missing.Diagnostics));
        Assert.False(missing.Source!.Character.Slots[0].AllowSlideCarry);

        var enabledJson = JsonNode.Parse(Fixture("character.json"))!.AsObject();
        ((JsonObject)enabledJson["slots"]![0]!)["allowSlideCarry"] = true;
        var enabled = CharacterPackageSourceCodec.Load(Fixture("package.json"), enabledJson.ToJsonString());
        Assert.True(enabled.IsValid, string.Join("\n", enabled.Diagnostics));
        Assert.True(enabled.Source!.Character.Slots[0].AllowSlideCarry);
        var serialized = CharacterPackageSourceCodec.SerializeCharacter(enabled.Source.Character);
        Assert.Contains("\"allowSlideCarry\": true", serialized);
        var reparsed = CharacterPackageSourceCodec.Load(Fixture("package.json"), serialized);
        Assert.True(reparsed.IsValid, string.Join("\n", reparsed.Diagnostics));
        Assert.True(reparsed.Source!.Character.Slots[0].AllowSlideCarry);
    }
    [Fact]
    public void RenameUpdatesOptionalTumbleAndLowPoseReferences()
    {
        var parsed = CharacterPackageSourceCodec.Load(Fixture("package.json"), Fixture("character.json"));
        Assert.True(parsed.IsValid, string.Join("\n", parsed.Diagnostics));
        var source = parsed.Source! with
        {
            Character = parsed.Source.Character with
            {
                Presentation = parsed.Source.Character.Presentation with
                {
                    Tumble = "anim.tumble",
                    Crouch = "anim.crouch",
                    Slide = "anim.slide"
                }
            }
        };
        var renamedCrouch = CharacterPackageSourceCodec.RenameSemanticId(
            source, "anim.crouch", "anim.fall-crouch",
            new[] { new CharacterAssetCatalogBindingSnapshot("anim.crouch", "anim.crouch") });
        Assert.True(renamedCrouch.IsValid, string.Join("\n", renamedCrouch.Diagnostics));
        Assert.Equal("anim.fall-crouch", renamedCrouch.Source!.Character.Presentation.Crouch);
        Assert.Equal("anim.slide", renamedCrouch.Source.Character.Presentation.Slide);
        var renamedSlide = CharacterPackageSourceCodec.RenameSemanticId(
            renamedCrouch.Source, "anim.slide", "anim.fall-slide",
            new[] { new CharacterAssetCatalogBindingSnapshot("anim.slide", "anim.slide") });
        Assert.True(renamedSlide.IsValid, string.Join("\n", renamedSlide.Diagnostics));
        Assert.Equal("anim.fall-slide", renamedSlide.Source!.Character.Presentation.Slide);
        Assert.Equal("anim.fall-crouch", renamedSlide.Source.Character.Presentation.Crouch);
        Assert.Equal("anim.tumble", renamedSlide.Source.Character.Presentation.Tumble);
    }


    [Fact]
    public void StageTargetingMetadata_MissingDefaultsDisabledAndUnknownFieldsFail()
    {
        var json = JsonNode.Parse(Fixture("character.json"))!.AsObject();
        var stage = (JsonObject)json["slots"]![0]!["timeline"]!["stages"]![0]!;
        stage.Remove("attackRange");
        stage.Remove("warpRange");
        stage.Remove("useTargetLock");
        stage.Remove("rotateTowardTarget");
        stage.Remove("trackingStrength");
        var missing = CharacterPackageSourceCodec.Load(Fixture("package.json"), json.ToJsonString());
        Assert.True(missing.IsValid, string.Join("\n", missing.Diagnostics));
        var defaults = missing.Source!.Character.Slots[0].Timeline.Stages[0];
        Assert.Equal(0f, defaults.AttackRange);
        Assert.Equal(0f, defaults.WarpRange);
        Assert.False(defaults.UseTargetLock);
        Assert.False(defaults.RotateTowardTarget);
        Assert.Equal(0f, defaults.TrackingStrength);

        stage["unknownTargetingField"] = true;
        var unknown = CharacterPackageSourceCodec.Load(Fixture("package.json"), json.ToJsonString());
        Assert.Contains(unknown.Diagnostics, x => x.Code == "field.unknown");
    }


    [Fact]
    public void FloatSerializationIsInvariantShortestRoundTrip()
    {
        var minimal = CharacterPackageSourceCodec.CreateMinimal("test-character", "Test", "Binoui", "MIT", "SlopArena");
        var character = minimal.Character with
        {
            Weight = 0.35f,
            Movement = minimal.Character.Movement with
            {
                RunSpeed = 1.7f,
                AirSpeedMax = 0.8f,
                AirAccelStick = 0.85f,
                Gravity = 1.2345678f,
            },
            Presentation = minimal.Character.Presentation with { LandStartOffsetSeconds = 0.49f },
        };

        var priorCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            var json = CharacterPackageSourceCodec.SerializeCharacter(character);
            Assert.Contains("\"weight\": 0.35", json);
            Assert.Contains("\"runSpeed\": 1.7", json);
            Assert.Contains("\"airSpeedMax\": 0.8", json);
            Assert.Contains("\"gravity\": 1.2345678", json);
            Assert.Contains("\"landStartOffsetSeconds\": 0.49", json);
        }
        finally
        {
            CultureInfo.CurrentCulture = priorCulture;
        }
    }

    [Fact]
    public void AuthoredSourceRoundTripIsByteStable()
    {
        var parsed = CharacterPackageSourceCodec.Load(Fixture("package.json"), Fixture("character.json"));
        Assert.True(parsed.IsValid, string.Join("\n", parsed.Diagnostics));
        var serialized = CharacterPackageSourceCodec.SerializeCharacter(parsed.Source!.Character);
        var reparsed = CharacterPackageSourceCodec.Load(Fixture("package.json"), serialized);
        Assert.True(reparsed.IsValid, string.Join("\n", reparsed.Diagnostics));
        Assert.Equal(serialized, CharacterPackageSourceCodec.SerializeCharacter(reparsed.Source!.Character));
    }

    [Fact]
    public void MinimalTemplateHasExactlyUniversalSlotsAndNoCapabilities()
    {
        var source = CharacterPackageSourceCodec.CreateMinimal("test-character", "Test Character", "Binoui", "MIT", "SlopArena");
        Assert.Equal(new[] { "ground.1", "ground.2", "ground.3", "ground.A", "ground.E", "ground.R", "ground.F", "air.1", "air.2", "air.3", "air.A", "air.E", "air.R", "air.F" }, source.Character.Slots.Select(x => x.Id));
        Assert.Empty(source.Character.CapabilityRequirements);
        Assert.All(source.Character.Slots, x => Assert.Empty(x.Timeline.Stages));
        Assert.DoesNotContain("FightGuy", CharacterPackageSourceCodec.SerializeCharacter(source.Character));
    }

    [Fact]
    public void AuthoringReadyTemplateHasUsableDefaultsAndIndependentMoveAnimations()
    {
        var source = CharacterPackageSourceCodec.CreateAuthoringReady("test-character", "Test Character", "Binoui", "MIT", "SlopArena");
        Assert.Equal(100f, source.Character.Weight);
        Assert.Equal(14f, source.Character.Movement.RunSpeed);
        Assert.Equal(1.7f, source.Character.CapsuleHeight);
        Assert.Equal(7, source.Character.HurtboxBoneDefs.Count);
        Assert.All(source.Character.Slots, slot =>
        {
            Assert.Single(slot.Timeline.Stages);
            Assert.Equal(30, slot.Timeline.Stages[0].DurationTicks);
            Assert.Single(slot.Timeline.Stages[0].AnimationIds);
            Assert.Empty(slot.Timeline.Stages[0].Operations);
        });
        Assert.Equal(source.Character.Slots.Count,
            source.Character.Slots.SelectMany(slot => slot.Timeline.Stages).SelectMany(stage => stage.AnimationIds).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void CodecRefusesUnknownLegacyAndUnsupportedFields()
    {
        string package = Fixture("package.json").Replace("\"dependencies\": []", "\"dependencies\": [], \"id\": \"legacy\"");
        var result = CharacterPackageSourceCodec.Load(package, Fixture("character.json"));
        Assert.Contains(result.Diagnostics, x => x.Code == "source.identity-forbidden");
        string unsupported = Fixture("character.json").Replace("\"authoringSchemaVersion\": 3", "\"authoringSchemaVersion\": 2");
        result = CharacterPackageSourceCodec.Load(Fixture("package.json"), unsupported);
        Assert.Contains(result.Diagnostics, x => x.Code == "schema.unsupported");
        var missingTuning = JsonNode.Parse(Fixture("character.json"))!.AsObject();
        missingTuning["movement"]!.AsObject().Remove("airDodgeSpeed");
        result = CharacterPackageSourceCodec.Load(Fixture("package.json"), missingTuning.ToJsonString());
        Assert.Contains(result.Diagnostics, x => x.Code == "schema.missing" && x.Path == "character.movement.airDodgeSpeed");
    }

    [Fact]
    public void TypedOperationsPreserveAuthoredOrder()
    {
        var parsed = CharacterPackageSourceCodec.Load(Fixture("package.json"), Fixture("character.json"));
        Assert.True(parsed.IsValid);
        var original = parsed.Source!.Character.Slots.SelectMany(x => x.Timeline.Stages).SelectMany(x => x.Operations).ToArray();
        var roundTrip = CharacterPackageSourceCodec.Load(Fixture("package.json"), CharacterPackageSourceCodec.SerializeCharacter(parsed.Source.Character));
        var restored = roundTrip.Source!.Character.Slots.SelectMany(x => x.Timeline.Stages).SelectMany(x => x.Operations).ToArray();
        Assert.Equal(original.Select(x => x.GetType()), restored.Select(x => x.GetType()));
        Assert.Equal(original.Select(x => x.Tick), restored.Select(x => x.Tick));
    }

    [Fact]
    public void RenameUpdatesSourceReferencesAndRejectsCollision()
    {
        var parsed = CharacterPackageSourceCodec.Load(Fixture("package.json"), Fixture("character.json"));
        Assert.True(parsed.IsValid);
        var source = parsed.Source!;
        var snapshots = new[] { new CharacterAssetCatalogBindingSnapshot("anim.run", "anim.run") };
        var renamed = CharacterPackageSourceCodec.RenameSemanticId(source, "anim.run", "anim.sprint", snapshots);
        Assert.Equal("anim.sprint", renamed.Source!.Character.Presentation.Run);
        Assert.DoesNotContain("anim.run", renamed.Source.Character.PresentationIds);
        var collision = CharacterPackageSourceCodec.RenameSemanticId(source, "anim.run", "anim.idle", snapshots);
        Assert.Contains(collision.Diagnostics, x => x.Code == "rename.collision");
    }

    [Fact]
    public void EditHelpersRejectOutOfRangeWithoutMutation()
    {
        var source = CharacterPackageSourceCodec.CreateMinimal("test-character", "Test", "Binoui", "MIT", "SlopArena");
        var result = CharacterPackageSourceCodec.RemoveStage(source, 14, 0);
        Assert.False(result.IsValid);
        Assert.Equal(14, source.Character.Slots.Count);
    }
}
