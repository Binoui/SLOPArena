using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class CharacterPackageAssemblerTests
{
    [Fact]
    public void CapsuleEndpointsSurviveCookedAdmissionAndRuntimeAdaptation()
    {
        string root = FindRepoFile("client/Unity/Assets/CharacterPackages/fightguy");
        var source = CharacterPackageSourceCodec.Load(File.ReadAllText(Path.Combine(root, "package.json")),
            File.ReadAllText(Path.Combine(root, "character.json"))).Source!;
        var bones = source.Character.HurtboxBoneDefs.ToArray();
        bones[0] = bones[0] with { EndBoneId = "bone.neck", EndOffsetX = .1f, EndOffsetY = -.2f, EndOffsetZ = .3f };
        source = source with { Character = source.Character with { HurtboxBoneDefs = bones } };
        var compiled = CharacterPackageCompiler.Compile(source, CharacterCookProfile.TrustedBuiltIn).CookedPackage!;
        var admitted = CookedCharacterPackageLoader.LoadAssembly(CharacterPackageAssembler.Assemble(BuildInput(compiled,
            Array.Empty<PackageDependencySource>(), compiled.Definition.CapabilityRequirements, Array.Empty<CharacterDiagnostic>())));
        Assert.True(admitted.IsValid, string.Join("\n", admitted.Diagnostics));
        var bone = admitted.Package!.Definition.HurtboxBoneDefs.Single(x => x.BoneId == bones[0].BoneId);
        Assert.Equal("bone.neck", bone.EndBoneId);
        Assert.Equal(.1f, bone.EndOffsetX); Assert.Equal(-.2f, bone.EndOffsetY); Assert.Equal(.3f, bone.EndOffsetZ);
        var runtime = admitted.ToCharacterDefinition().HurtboxBoneDefs!.Single(x => x.BoneName == CharacterBoneRegistry.BakedName(bone.BoneId));
        Assert.Equal(CharacterBoneRegistry.BakedName("bone.neck"), runtime.EndBoneName);
        Assert.Equal(bone.EndOffsetX, runtime.EndOffX); Assert.Equal(bone.EndOffsetY, runtime.EndOffY); Assert.Equal(bone.EndOffsetZ, runtime.EndOffZ);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingHurtboxPointRejectsThePackage(bool endBone)
    {
        var package = Compile();
        var runtime = JsonNode.Parse(package.CanonicalBytes)!;
        var bone = runtime["character"]!["hurtboxBoneDefs"]![0]!;
        bone[endBone ? "endBoneId" : "boneId"] = "bone.missing";
        var input = BuildInput(package, Array.Empty<PackageDependencySource>(),
            package.Definition.CapabilityRequirements, Array.Empty<CharacterDiagnostic>());
        var candidate = new CharacterPackageAssemblyInput(input.PackageId, input.Version, input.Creator, input.License,
            input.Attribution, input.AuthoringSchemaVersion, input.CookedSchemaVersion, input.RuntimeApiMin, input.RuntimeApiMax,
            input.SourceHash, input.Dependencies, input.CapabilityRequirements, input.CookerVersion, input.UnityVersion,
            input.BindingSchemaVersion, input.PoseFormat, input.PoseVersion, input.SampleRate, input.Warnings,
            Encoding.UTF8.GetBytes(runtime.ToJsonString()), input.PoseBytes, input.BindingBytes,
            new CookedCharacterPackage(package.Metadata, package.Definition, package.Budget, package.Diagnostics, Encoding.UTF8.GetBytes(runtime.ToJsonString())));
        var result = CharacterPackageAssembler.Assemble(candidate);
        Assert.True(result.IsValid, string.Join("\n", result.Diagnostics));
        var verification = CharacterPackageAssembler.Verify(Files(result));
        Assert.False(verification.IsValid);
        Assert.Contains(verification.Diagnostics, x => x.Code == "package.pose.hurtbox-missing");
    }

    [Fact]
    public void FlyingAnimationSurvivesSourceEditsAndCookedAdmission()
    {
        string root = FindRepoFile("client/Unity/Assets/CharacterPackages/manki");
        var source = CharacterPackageSourceCodec.Load(File.ReadAllText(Path.Combine(root, "package.json")),
            File.ReadAllText(Path.Combine(root, "character.json"))).Source!;
        source = source with { Character = source.Character with
        {
            Presentation = source.Character.Presentation with { Flying = "anim.test.flight" }
        } };
        var renamed = CharacterPackageSourceCodec.RenameSemanticId(source, "anim.test.flight", "anim.test.flying",
            new[] { new CharacterAssetCatalogBindingSnapshot("anim.test.flight", "anim.test.flight") });
        Assert.True(renamed.IsValid);
        var compiled = CharacterPackageCompiler.Compile(CharacterPackageSourceCodec.SerializeManifest(renamed.Source!.Manifest),
            CharacterPackageSourceCodec.SerializeCharacter(renamed.Source.Character), CharacterCookProfile.TrustedBuiltIn);
        Assert.NotNull(compiled.CookedPackage);
        var package = compiled.CookedPackage!;
        var assembly = CharacterPackageAssembler.Assemble(BuildInput(package, Array.Empty<PackageDependencySource>(),
            package.Definition.CapabilityRequirements, Array.Empty<CharacterDiagnostic>()));
        var loaded = CookedCharacterPackageLoader.LoadAssembly(assembly);
        Assert.True(loaded.IsValid, string.Join("\n", loaded.Diagnostics));
        Assert.Equal("anim.manki.gf-loop", loaded.Package!.Definition.Presentation.MayhemLaugh);
        Assert.Equal("anim.manki.gf-loop", loaded.ToCharacterDefinition().MayhemLaughAnim);
        Assert.True(loaded.BakedAnimation!.FindAnimIndex("anim.manki.gf-loop") >= 0);
        Assert.Equal("anim.test.flying", loaded.Package.Definition.Presentation.Flying);
        Assert.Equal("anim.test.flying", CookedCharacterRuntimeAdapter.ToCharacterDefinition(loaded.Package).FlyingAnim);
        Assert.True(BakedAnimationData.LoadFromBin(assembly.PoseBytes).FindAnimIndex("anim.test.flying") >= 0);
    }

    [Theory]
    [InlineData("roundtrip")]
    [InlineData("api")]
    [InlineData("coverage")]
    [InlineData("aim")]
    [InlineData("bone")]
    [InlineData("duration")]
    [InlineData("retired")]
    public void DashStrikeCookedAdmissionMatchesTheSourceContract(string mutation)
    {
        string root = FindRepoFile("client/Unity/Assets/CharacterPackages/wibou");
        var compiled = CharacterPackageCompiler.Compile(File.ReadAllText(Path.Combine(root, "package.json")),
            File.ReadAllText(Path.Combine(root, "character.json")), CharacterCookProfile.TrustedBuiltIn);
        Assert.NotNull(compiled.CookedPackage);
        var package = compiled.CookedPackage!;
        var runtime = JsonNode.Parse(package.CanonicalBytes)!.AsObject();
        var move = runtime["character"]!["slots"]!.AsArray().Single(x => (string?)x!["id"] == "ground.R")!;
        var stage = move["timeline"]!["stages"]![0]!;
        var operation = stage["operations"]!.AsArray().Single(x => x!["capabilityId"] != null)!;
        switch (mutation)
        {
            case "coverage": operation["parameters"]!["lightHitbox"]!["radius"] = .1; break;
            case "aim": move["aimMode"] = (byte)AuthoringAimMode.GroundVector; break;
            case "bone": operation["parameters"]!["tipHitbox"]!["startBoneId"] = "_weapon_tip"; break;
            case "duration": stage["durationTicks"] = 5; break;
            case "retired": operation["capabilityId"] = "slop.ability.charged-directional-dash.v1"; break;
        }
        if (mutation == "api")
        {
            runtime["metadata"]!["compatibility"]!["runtimeApiMin"] = "1.14.0";
            package = new CookedCharacterPackage(package.Metadata with { RuntimeApiMin = "1.14.0" }, package.Definition,
                package.Budget, package.Diagnostics, Encoding.UTF8.GetBytes(runtime.ToJsonString()));
        }
        else if (mutation != "roundtrip")
            package = new CookedCharacterPackage(package.Metadata, package.Definition,
                package.Budget, package.Diagnostics, Encoding.UTF8.GetBytes(runtime.ToJsonString()));
        var loaded = CookedCharacterPackageLoader.LoadAssembly(CharacterPackageAssembler.Assemble(BuildInput(package,
            Array.Empty<PackageDependencySource>(), package.Definition.CapabilityRequirements, Array.Empty<CharacterDiagnostic>())));
        Assert.Equal(mutation == "roundtrip", loaded.IsValid);
        if (mutation == "roundtrip")
        {
            var actual = loaded.Package!.Definition.Slots.Single(x => x.Id == "ground.R").Timeline.Stages[0]
                .Operations.OfType<CookedStartCapabilityOperation>().Single();
            Assert.IsType<Abilities.DashStrikeAbility>(Construct(actual));
        }
        else Assert.Contains(loaded.Diagnostics, x => x.Severity == CharacterDiagnosticSeverity.Error);
        static Abilities.ServerAbility Construct(CookedStartCapabilityOperation x)
        {
            Assert.True(Abilities.InternalCapabilityRegistry.TryCreate(x.CapabilityId, x.CapabilityVersion, x.Parameters, out var ability));
            return ability;
        }
    }

    [Theory]
    [InlineData("roundtrip")]
    [InlineData("api")]
    [InlineData("coverage")]
    [InlineData("hold")]
    [InlineData("clip")]
    [InlineData("duration")]
    [InlineData("retired")]
    public void FlashCutCookedAdmissionMatchesTheSourceContract(string mutation)
    {
        string root = FindRepoFile("client/Unity/Assets/CharacterPackages/wibou");
        var compiled = CharacterPackageCompiler.Compile(File.ReadAllText(Path.Combine(root, "package.json")),
            File.ReadAllText(Path.Combine(root, "character.json")), CharacterCookProfile.TrustedBuiltIn);
        Assert.NotNull(compiled.CookedPackage);
        var package = compiled.CookedPackage!;
        var runtime = JsonNode.Parse(package.CanonicalBytes)!.AsObject();
        var move = runtime["character"]!["slots"]!.AsArray().Single(x => (string?)x!["id"] == "ground.F")!;
        var stage = move["timeline"]!["stages"]![0]!;
        var operation = stage["operations"]!.AsArray().Single(x => x!["capabilityId"] != null)!;
        switch (mutation)
        {
            case "coverage": operation["parameters"]!["cutHitbox"]!["radius"] = .5; break;
            case "hold": operation["parameters"]!["sheatheTicks"] = 120; operation["parameters"]!["legTicks"] = 30; break;
            case "clip": operation["parameters"]!["stanceAnimationId"] = "anim.wibou.g1"; break;
            case "duration": stage["durationTicks"] = 60; break;
            case "retired": operation["capabilityId"] = "slop.internal.wibou.blade-flurry.v2"; break;
        }
        if (mutation == "api")
            package = new CookedCharacterPackage(package.Metadata with { RuntimeApiMin = "1.14.0" }, package.Definition,
                package.Budget, package.Diagnostics, package.CanonicalBytes);
        else if (mutation != "roundtrip")
            package = new CookedCharacterPackage(package.Metadata, package.Definition,
                package.Budget, package.Diagnostics, Encoding.UTF8.GetBytes(runtime.ToJsonString()));
        var loaded = CookedCharacterPackageLoader.LoadAssembly(CharacterPackageAssembler.Assemble(BuildInput(package,
            Array.Empty<PackageDependencySource>(), package.Definition.CapabilityRequirements, Array.Empty<CharacterDiagnostic>())));
        Assert.Equal(mutation == "roundtrip", loaded.IsValid);
        if (mutation == "roundtrip")
        {
            var actual = loaded.Package!.Definition.Slots.Single(x => x.Id == "ground.F").Timeline.Stages[0]
                .Operations.OfType<CookedStartCapabilityOperation>().Single();
            Assert.True(Abilities.InternalCapabilityRegistry.TryCreate(actual.CapabilityId, actual.CapabilityVersion,
                actual.Parameters, out var ability));
            Assert.IsType<Abilities.FlashCutAbility>(ability);
        }
        else Assert.Contains(loaded.Diagnostics, x => x.Severity == CharacterDiagnosticSeverity.Error);
    }

    private const string ProfileMediaId = "GlobalObjectId_V1-1-0123456789abcdef0123456789abcdef-100100000-0";

    [Fact]
    public void PresentationV2AssemblesAndVerifiesWithoutChangingV1Fixtures()
    {
        Assert.True(AssembleFixture().IsValid);
        var assembly = CharacterPackageAssembler.Assemble(PresentationInput());
        Assert.True(assembly.IsValid, string.Join("; ", assembly.Diagnostics));
        Assert.True(CharacterPackageAssembler.Verify(Files(assembly)).IsValid);
    }

    [Theory]
    [InlineData("bad-package-id")]
    [InlineData("bad-source-hash")]
    [InlineData("bad-pose-format")]
    [InlineData("bad-pose-version")]
    [InlineData("bad-sample-rate")]
    [InlineData("missing-profile")]
    [InlineData("mixed-schema")]
    [InlineData("future-schema")]
    [InlineData("unknown-root")]
    [InlineData("unknown-profile")]
    [InlineData("unknown-pool")]
    [InlineData("duplicate-slot")]
    [InlineData("foreign-slot")]
    [InlineData("too-many-slots")]
    [InlineData("bad-orientation")]
    [InlineData("bad-selection")]
    [InlineData("fixed-cardinality")]
    [InlineData("cycle-empty")]
    [InlineData("tint-nan")]
    [InlineData("scale-zero")]
    [InlineData("scale-infinity")]
    [InlineData("float-string")]
    [InlineData("symbolic-media")]
    [InlineData("bad-clip-media")]
    [InlineData("bad-bone")]
    [InlineData("bad-suppression")]
    [InlineData("missing-pool")]
    public void PresentationV2RejectsMalformedMetadataDuringAssemblyAndVerification(string mutation)
    {
        void Mutate(JsonObject root)
        {
            var profile = root["presentationProfile"]!.AsObject();
            var rows = profile["projectiles"]!.AsArray();
            switch (mutation)
            {
                case "bad-package-id": root["packageId"] = "Invalid Package"; break;
                case "bad-source-hash": root["sourceHash"] = "symbolic"; break;
                case "bad-pose-format": root["poseFormat"] = "OTHER"; break;
                case "bad-pose-version": root["poseVersion"] = 2; break;
                case "bad-sample-rate": root["sampleRate"] = 30; break;
                case "missing-profile": root.Remove("presentationProfile"); break;
                case "mixed-schema": root["catalogSchemaVersion"] = 1; break;
                case "future-schema": root["catalogSchemaVersion"] = 3; root["bindingSchemaVersion"] = 3; break;
                case "unknown-root": root["future"] = 1; break;
                case "unknown-profile": profile["future"] = 1; break;
                case "unknown-pool": profile["swing"]!["future"] = 1; break;
                case "duplicate-slot": rows.Add(rows[0]!.DeepClone()); break;
                case "foreign-slot": rows[0]!["slotId"] = "ground.LMB"; break;
                case "too-many-slots": for (int i = 0; i < 16; i++) rows.Add(rows[0]!.DeepClone()); break;
                case "bad-orientation": rows[0]!["orientation"] = 2; break;
                case "bad-selection": profile["swing"]!["selection"] = 2; break;
                case "fixed-cardinality": profile["lightImpact"]!["clipGlobalObjectIds"] = new JsonArray(); break;
                case "cycle-empty": profile["swing"]!["selection"] = 1; break;
                case "tint-nan": profile["respawnTintBits"]![0] = BitConverter.SingleToInt32Bits(float.NaN); break;
                case "scale-zero": rows[0]!["scaleBits"] = 0; break;
                case "scale-infinity": rows[0]!["scaleBits"] = BitConverter.SingleToInt32Bits(float.PositiveInfinity); break;
                case "float-string": rows[0]!["scaleBits"] = "1065353216"; break;
                case "symbolic-media": rows[0]!["prefabGlobalObjectId"] = "prefab"; break;
                case "bad-clip-media": profile["lightImpact"]!["clipGlobalObjectIds"]![0] = "clip"; break;
                case "bad-bone": rows[0]!["heldBoneId"] = "right-hand"; break;
                case "bad-suppression": profile["suppressRemovalExplosion"] = 1; break;
                case "missing-pool": profile.Remove("heavyImpact"); break;
            }
        }
        var rejected = CharacterPackageAssembler.Assemble(PresentationInput(Mutate));
        Assert.False(rejected.IsValid);
        Assert.Contains(rejected.Diagnostics, d => d.Code.StartsWith("package.binding.") || d.Code.StartsWith("package.field."));
        var baseline = CharacterPackageAssembler.Assemble(PresentationInput());
        Assert.True(baseline.IsValid, string.Join("; ", baseline.Diagnostics));
        var files = Files(baseline);
        var payload = JsonNode.Parse(files[CharacterPackageAssembler.BindingPath])!.AsObject();
        Mutate(payload);
        files[CharacterPackageAssembler.BindingPath] = Encoding.UTF8.GetBytes(payload.ToJsonString());
        var verification = CharacterPackageAssembler.Verify(files);
        Assert.False(verification.IsValid);
        Assert.Contains(verification.Diagnostics, d => d.Code.StartsWith("package.binding.") || d.Code.StartsWith("package.field."));
    }

    [Fact]
    public void PresentationV2PreservesOrderedSilentPoolPositionsAndHashesEveryScalarAndMediaIdentity()
    {
        var baseline = CharacterPackageAssembler.Assemble(PresentationInput(root =>
        {
            root["presentationProfile"]!["swing"]!["selection"] = 1;
            root["presentationProfile"]!["swing"]!["clipGlobalObjectIds"] = new JsonArray(ProfileMediaId, "", ProfileMediaId);
        }));
        Assert.True(baseline.IsValid, string.Join("; ", baseline.Diagnostics));
        using var parsed = JsonDocument.Parse(baseline.BindingBytes);
        Assert.Equal("", parsed.RootElement.GetProperty("presentationProfile").GetProperty("swing").GetProperty("clipGlobalObjectIds")[1].GetString());
        foreach (Action<JsonObject> edit in new Action<JsonObject>[]
        {
            r => r["presentationProfile"]!["respawnTintBits"]![0] = BitConverter.SingleToInt32Bits(.25f),
            r => r["presentationProfile"]!["suppressRemovalExplosion"] = true,
            r => r["presentationProfile"]!["projectiles"]![0]!["scaleBits"] = BitConverter.SingleToInt32Bits(2f),
            r => r["presentationProfile"]!["projectiles"]![0]!["orientation"] = 1,
            r => r["presentationProfile"]!["projectiles"]![0]!["heldBoneId"] = "bone.right-hand",
            r => r["presentationProfile"]!["lightImpact"]!["clipGlobalObjectIds"]![0] = ProfileMediaId.Replace("100100000", "100100001"),
        })
        {
            var changed = CharacterPackageAssembler.Assemble(PresentationInput(edit));
            Assert.True(changed.IsValid, string.Join("; ", changed.Diagnostics));
            Assert.NotEqual(CharacterPackageAssembler.Assemble(PresentationInput()).CookedContentHash, changed.CookedContentHash);
        }
    }

    [Fact]
    public void PresentationV1RejectsV2ProfileAndUnknownFields()
    {
        var v2 = PresentationInput();
        var root = JsonNode.Parse(v2.BindingBytes)!.AsObject();
        root["bindingSchemaVersion"] = 1;
        root["catalogSchemaVersion"] = 1;
        var input = CopyBindings(v2, root, 1);
        var rejected = CharacterPackageAssembler.Assemble(input);
        Assert.False(rejected.IsValid);
        Assert.Contains(rejected.Diagnostics, d => d.Code == "package.field.unknown");
    }

    [Fact]
    public void ValidateClientBindingsDirectlyAcceptsCompleteV2Metadata()
        => Assert.Empty(CharacterPackageAssembler.ValidateClientBindings(PresentationInput().BindingBytes));

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void PresentationV2AcceptsAllDeclaredAnimationExtrapolationModes(int mode, bool valid)
    {
        var input = PresentationInput(root =>
        {
            foreach (var row in root["animations"]!.AsArray()) row!["extrapolation"] = mode;
        });
        var diagnostics = CharacterPackageAssembler.ValidateClientBindings(input.BindingBytes);
        Assert.Equal(valid, diagnostics.Count == 0);
        var result = CharacterPackageAssembler.Assemble(input);
        Assert.Equal(valid, result.IsValid);
        if (valid) Assert.True(CharacterPackageAssembler.Verify(Files(result)).IsValid);
        else Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "package.binding.value");
    }

    [Theory]
    [InlineData("packageId")]
    [InlineData("sourceHash")]
    [InlineData("poseFormat")]
    [InlineData("poseVersion")]
    [InlineData("sampleRate")]
    public void ValidateClientBindingsRejectsIntrinsicMetadataWithoutCrossPayloadChecks(string field)
    {
        var input = PresentationInput(root =>
        {
            switch (field)
            {
                case "packageId": root[field] = "Invalid Package"; break;
                case "sourceHash": root[field] = "symbolic"; break;
                case "poseFormat": root[field] = "OTHER"; break;
                case "poseVersion": root[field] = 2; break;
                case "sampleRate":
                    root[field] = 30;
                    foreach (var row in root["animations"]!.AsArray()) row!["sampleRate"] = 30;
                    break;
            }
        });
        Assert.Contains(CharacterPackageAssembler.ValidateClientBindings(input.BindingBytes), d => d.Code == "package.binding.metadata-invalid");
        var root = JsonNode.Parse(input.BindingBytes)!.AsObject();
        var aligned = new CharacterPackageAssemblyInput(input.PackageId, input.Version, input.Creator, input.License,
            input.Attribution, input.AuthoringSchemaVersion, input.CookedSchemaVersion, input.RuntimeApiMin,
            input.RuntimeApiMax, input.SourceHash, input.Dependencies, input.CapabilityRequirements, input.CookerVersion,
            input.UnityVersion, 2, root["poseFormat"]!.GetValue<string>(), root["poseVersion"]!.GetValue<int>(),
            root["sampleRate"]!.GetValue<int>(), input.Warnings, input.RuntimeBytes, input.PoseBytes, input.BindingBytes, input.CookedPackage);
        Assert.False(CharacterPackageAssembler.Assemble(aligned).IsValid);
    }

    [Theory]
    [InlineData("packageId")]
    [InlineData("catalogSchemaVersion")]
    [InlineData("bindingSchemaVersion")]
    [InlineData("poseFormat")]
    [InlineData("poseVersion")]
    [InlineData("sampleRate")]
    [InlineData("sourceHash")]
    [InlineData("rigGlobalObjectId")]
    [InlineData("weaponConfigGlobalObjectId")]
    [InlineData("animations")]
    [InlineData("presentations")]
    [InlineData("presentationProfile")]
    public void ValidateClientBindingsDirectlyRejectsMissingV2Fields(string field)
    {
        var input = PresentationInput(root => root.Remove(field));
        Assert.NotEmpty(CharacterPackageAssembler.ValidateClientBindings(input.BindingBytes));
    }

    [Theory]
    [InlineData("bone.RightHand")]
    [InlineData("bone.right/hand")]
    [InlineData("bone.right\0hand")]
    [InlineData("bone.abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijk")]
    public void PresentationV2HeldBonesUseBoundedLowercaseSemanticIds(string bone)
    {
        var input = PresentationInput(root => root["presentationProfile"]!["projectiles"]![0]!["heldBoneId"] = bone);
        Assert.Contains(CharacterPackageAssembler.ValidateClientBindings(input.BindingBytes), d => d.Code == "package.binding.profile");
        Assert.False(CharacterPackageAssembler.Assemble(input).IsValid);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("complete")]
    [InlineData("orphan")]
    public void PresentationV2RequiresAllDeclaredTimelineEffectsEvenWithoutHitIds(string mode)
    {
        string path = FindRepoFile("client/Unity/Assets/CharacterPackages/fightguy");
        var source = JsonNode.Parse(File.ReadAllText(Path.Combine(path, "character.json")))!.AsObject();
        foreach (var slot in source["slots"]!.AsArray()) slot!.AsObject().Remove("hitPresentationId");
        var compiled = CharacterPackageCompiler.Compile(File.ReadAllText(Path.Combine(path, "package.json")), source.ToJsonString(), CharacterCookProfile.TrustedBuiltIn);
        Assert.NotNull(compiled.CookedPackage);
        var package = compiled.CookedPackage!;
        Assert.NotEmpty(package.Definition.PresentationIds);
        Assert.All(package.Definition.Slots, slot => Assert.True(string.IsNullOrEmpty(slot.HitPresentationId)));
        void Mutate(JsonObject root)
        {
            if (mode == "missing") root.Remove("presentations");
            if (mode == "empty") root["presentations"] = new JsonArray();
            if (mode == "orphan") root["presentations"]!.AsArray().Add(new JsonObject { ["semanticId"] = "presentation.orphan", ["prefabGlobalObjectId"] = ProfileMediaId });
        }
        var complete = CharacterPackageAssembler.Assemble(PresentationInput(packageOverride: package));
        Assert.True(complete.IsValid, string.Join("; ", complete.Diagnostics));
        var result = CharacterPackageAssembler.Assemble(PresentationInput(Mutate, package));
        Assert.Equal(mode == "complete", result.IsValid);
        var files = Files(complete);
        var payload = JsonNode.Parse(complete.BindingBytes)!.AsObject();
        Mutate(payload);
        files[CharacterPackageAssembler.BindingPath] = Encoding.UTF8.GetBytes(payload.ToJsonString());
        var verification = CharacterPackageAssembler.Verify(files);
        Assert.Equal(mode == "complete", verification.IsValid);
        if (mode != "complete") Assert.Contains(verification.Diagnostics, d => d.Code is "package.binding.presentation-missing" or "package.binding.presentation-orphan" or "package.field.missing");
        if (mode is "missing" or "empty")
        {
            payload["catalogSchemaVersion"] = 1; payload["bindingSchemaVersion"] = 1; payload.Remove("presentationProfile");
            Assert.True(CharacterPackageAssembler.Assemble(CopyBindings(PresentationInput(packageOverride: package), payload, 1)).IsValid);
        }
    }

    private static CharacterPackageAssemblyInput PresentationInput(Action<JsonObject>? edit = null, CookedCharacterPackage? packageOverride = null)
    {
        var package = packageOverride ?? Compile();
        var input = BuildInput(package, Array.Empty<PackageDependencySource>(), package.Definition.CapabilityRequirements, Array.Empty<CharacterDiagnostic>());
        var root = JsonNode.Parse(input.BindingBytes)!.AsObject();
        root["bindingSchemaVersion"] = 2;
        root["catalogSchemaVersion"] = 2;
        root["rigGlobalObjectId"] = ProfileMediaId;
        root["weaponConfigGlobalObjectId"] = "";
        foreach (var row in root["animations"]!.AsArray())
        {
            row!["clipGlobalObjectId"] = ProfileMediaId;
            row["clipLengthBits"] = BitConverter.SingleToInt32Bits(1f / 60f);
        }
        foreach (var row in root["presentations"]!.AsArray()) row!["prefabGlobalObjectId"] = ProfileMediaId;
        JsonObject Pool(params string[] clips) => new() { ["selection"] = 0, ["clipGlobalObjectIds"] = new JsonArray(clips.Select(c => (JsonNode?)JsonValue.Create(c)).ToArray()) };
        root["presentationProfile"] = new JsonObject
        {
            ["respawnTintBits"] = new JsonArray(0, 0, 0, BitConverter.SingleToInt32Bits(1f)),
            ["swing"] = Pool(), ["lightImpact"] = Pool(ProfileMediaId), ["mediumImpact"] = Pool(""), ["heavyImpact"] = Pool(ProfileMediaId),
            ["suppressRemovalExplosion"] = false,
            ["projectiles"] = new JsonArray(new JsonObject
            {
                ["slotId"] = "ground.A", ["prefabGlobalObjectId"] = ProfileMediaId,
                ["scaleBits"] = BitConverter.SingleToInt32Bits(1f), ["orientation"] = 0, ["heldBoneId"] = "",
            }),
        };
        edit?.Invoke(root);
        return CopyBindings(input, root, 2);
    }

    private static CharacterPackageAssemblyInput CopyBindings(CharacterPackageAssemblyInput input, JsonObject bindings, int version)
        => new(input.PackageId, input.Version, input.Creator, input.License, input.Attribution,
            input.AuthoringSchemaVersion, input.CookedSchemaVersion, input.RuntimeApiMin, input.RuntimeApiMax,
            input.SourceHash, input.Dependencies, input.CapabilityRequirements, input.CookerVersion, input.UnityVersion,
            version, input.PoseFormat, input.PoseVersion, input.SampleRate, input.Warnings,
            input.RuntimeBytes, input.PoseBytes, Encoding.UTF8.GetBytes(bindings.ToJsonString()), input.CookedPackage);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AimedRecoveryRoundTripsSourceAndCookedAdmissionAndRejectsDowngrades(bool hop)
    {
        string root = FindRepoFile("client/Unity/Assets/CharacterPackages/bashie");
        string manifest = File.ReadAllText(Path.Combine(root, "package.json"));
        var character = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "character.json")))!;
        RingBounceFixture.Apply(character);
        if (!hop)
        {
            var p = character["slots"]!.AsArray().Single(s => (string?)s!["id"] == "ground.E")!
                ["timeline"]!["stages"]![0]!["operations"]![0]!["parameters"]!.AsObject();
            p.Remove("hop"); p["startupTicks"] = 12; p["preparationRiseSpeed"] = .8f;
        }
        var source = CharacterPackageSourceCodec.Load(manifest, character.ToJsonString());
        Assert.True(source.IsValid, string.Join("; ", source.Diagnostics));
        var rewritten = CharacterPackageSourceCodec.Load(manifest,
            CharacterPackageSourceCodec.SerializeCharacter(source.Source!.Character));
        Assert.True(rewritten.IsValid, string.Join("; ", rewritten.Diagnostics));
        var compiled = CharacterPackageCompiler.Compile(rewritten.Source!, CharacterCookProfile.TrustedBuiltIn);
        Assert.NotNull(compiled.CookedPackage);
        var package = compiled.CookedPackage!;
        var loaded = Load(package);
        Assert.True(loaded.IsValid, string.Join("; ", loaded.Diagnostics));
        foreach (string id in new[] { "ground.E", "air.E" })
        {
            var operation = loaded.Package!.Definition.Slots.Single(s => s.Id == id).Timeline.Stages.Single()
                .Operations.OfType<CookedStartCapabilityOperation>().Single();
            Assert.Equal(hop ? new CookedAimedRecoveryCapabilityParameters(44, 15, 13, 8, 0f,
                new RecoveryHopParameters(18, 34, .792f, .495f, .198f))
                : new CookedAimedRecoveryCapabilityParameters(12, 15, 13, 8, .8f), operation.Parameters);
        }

        foreach (bool downgrade in new[] { true, false })
        {
            var runtime = JsonNode.Parse(package.CanonicalBytes)!.AsObject();
            string api = downgrade ? (hop ? "1.9.0" : "1.8.0") : (hop ? "1.10.0" : "1.9.0");
            runtime["metadata"]!["compatibility"]!["runtimeApiMin"] = api;
            if (!downgrade)
            {
                var p = runtime["character"]!["slots"]!.AsArray().Single(s => (string?)s!["id"] == "ground.E")!
                    ["timeline"]!["stages"]![0]!["operations"]![0]!["parameters"]!;
                if (hop) p["hop"]!["arcHeight"] = 100;
                else p["directionalVerticalSpeed"] = 100;
            }
            var tampered = new CookedCharacterPackage(package.Metadata with { RuntimeApiMin = api }, package.Definition,
                package.Budget, package.Diagnostics, Encoding.UTF8.GetBytes(runtime.ToJsonString()));
            var rejected = Load(tampered);
            Assert.False(rejected.IsValid);
            Assert.Contains(rejected.Diagnostics, d => d.Code == (downgrade
                ? "package.compatibility.unsupported" : "package.capability.parameters-invalid"));
        }

        static CookedCharacterPackageLoadResult Load(CookedCharacterPackage p)
            => CookedCharacterPackageLoader.LoadAssembly(CharacterPackageAssembler.Assemble(BuildInput(p,
                Array.Empty<PackageDependencySource>(), p.Definition.CapabilityRequirements, Array.Empty<CharacterDiagnostic>())));
    }

    [Fact]
    public void AlternateFormRoundTripsSourceAndCookedAdmissionAndRejectsMismatches()
    {
        string root = FindRepoFile("client/Unity/Assets/CharacterPackages/fightguy");
        string manifest = File.ReadAllText(Path.Combine(root, "package.json"));
        JsonObject Character(bool block = true, bool capability = true)
        {
            var c = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "character.json")))!.AsObject();
            FightGuyFixture.WithoutTitleMatch(c);
            if (capability)
                c["capabilityRequirements"]!.AsArray().Add(new JsonObject
                    { ["capabilityId"] = CharacterPackageCompiler.FormsCapabilityId, ["capabilityVersion"] = "1" });
            c["slots"]!.AsArray().Single(s => (string?)s!["id"] == "ground.F")!["timeline"]!["stages"]![0]!["operations"]!
                .AsArray().Add(new JsonObject { ["kind"] = "swapForm", ["tick"] = 0, ["unit"] = "ticks" });
            if (!block) return c;
            var movement = c["movement"]!.DeepClone().AsObject();
            movement["runSpeed"] = 16;
            c["alternateForm"] = new JsonObject
            {
                ["id"] = "fake",
                ["displayName"] = "Fixture Fake",
                ["movement"] = movement,
                ["presentation"] = new JsonObject { ["run"] = c["presentation"]!["dash"]!.DeepClone() },
                ["slots"] = c["slots"]!.DeepClone(),
                ["aliases"] = c["aliases"]!.DeepClone(),
            };
            return c;
        }
        CharacterCompileResult Compile(JsonObject c)
            => CharacterPackageCompiler.Compile(manifest, c.ToJsonString(), CharacterCookProfile.TrustedBuiltIn);

        var source = CharacterPackageSourceCodec.Load(manifest, Character().ToJsonString());
        Assert.True(source.IsValid, string.Join("; ", source.Diagnostics));
        var rewritten = CharacterPackageSourceCodec.Load(manifest,
            CharacterPackageSourceCodec.SerializeCharacter(source.Source!.Character));
        Assert.True(rewritten.IsValid, string.Join("; ", rewritten.Diagnostics));
        Assert.Equal(source.Source.Character.AlternateForm!.Slots.Count, rewritten.Source!.Character.AlternateForm!.Slots.Count);
        var compiled = CharacterPackageCompiler.Compile(rewritten.Source, CharacterCookProfile.TrustedBuiltIn);
        Assert.True(compiled.CookedPackage != null, string.Join("; ", compiled.Diagnostics.Select(d => d.Code + " " + d.Path)));
        var package = compiled.CookedPackage!;
        Assert.Equal("1.16.0", package.Metadata.RuntimeApiMin);

        var loaded = CookedCharacterPackageLoader.LoadAssembly(CharacterPackageAssembler.Assemble(BuildInput(package,
            Array.Empty<PackageDependencySource>(), package.Definition.CapabilityRequirements, Array.Empty<CharacterDiagnostic>())));
        Assert.True(loaded.IsValid, string.Join("; ", loaded.Diagnostics.Select(d => d.Code + " " + d.Message)));
        var form = loaded.Package!.Definition.AlternateForm!;
        Assert.Equal(("fake", "Fixture Fake"), (form.Id, form.DisplayName));
        Assert.Equal(CharacterPackageCompiler.CanonicalSlotIds, form.Slots.Select(s => s.Id));
        Assert.All(form.Slots, s => Assert.Equal(1, s.Form));
        Assert.All(loaded.Package.Definition.Slots, s => Assert.Equal(0, s.Form));
        foreach (var grid in new[] { loaded.Package.Definition.Slots, form.Slots })
        foreach (string id in new[] { "ground.F", "air.F" })
            Assert.Single(grid.Single(s => s.Id == id).Timeline.Stages.SelectMany(st => st.Operations).OfType<CookedSwapFormOperation>());

        var real = loaded.ToCharacterDefinition(CharacterClass.FightGuy);
        var fake = real.ForForm(1);
        Assert.Equal(1, fake.Form);
        Assert.Same(real, fake.ForForm(0));
        Assert.Equal(16f, fake.Movement.RunSpeed);
        Assert.Equal(real.DashAnim, fake.RunAnim);
        Assert.Equal(real.IdleAnim, fake.IdleAnim);
        Assert.Equal((real.CapsuleHeight, real.ShieldRadius, real.Weight), (fake.CapsuleHeight, fake.ShieldRadius, fake.Weight));

        var runtime = JsonNode.Parse(package.CanonicalBytes)!.AsObject();
        runtime["metadata"]!["compatibility"]!["runtimeApiMin"] = "1.15.0";
        var downgraded = new CookedCharacterPackage(package.Metadata with { RuntimeApiMin = "1.15.0" }, package.Definition,
            package.Budget, package.Diagnostics, Encoding.UTF8.GetBytes(runtime.ToJsonString()));
        var rejected = CookedCharacterPackageLoader.LoadAssembly(CharacterPackageAssembler.Assemble(BuildInput(downgraded,
            Array.Empty<PackageDependencySource>(), downgraded.Definition.CapabilityRequirements, Array.Empty<CharacterDiagnostic>())));
        Assert.Contains(rejected.Diagnostics, d => d.Code == "package.compatibility.unsupported");

        Assert.Contains(Compile(Character(block: false)).Diagnostics, d => d.Code == "schema.missing" && d.Path == "character.alternateForm");
        Assert.Contains(Compile(Character(capability: false)).Diagnostics, d => d.Code == "capability.unknown" && d.Path == "character.alternateForm");
        Assert.Null(Compile(Character(block: false, capability: false)).CookedPackage);
        var duplicated = Character();
        var formSlots = duplicated["alternateForm"]!["slots"]!.AsArray();
        formSlots.Add(formSlots[0]!.DeepClone());
        Assert.Contains(Compile(duplicated).Diagnostics,
            d => d.Code == "id.duplicate" && d.Path == $"character.alternateForm.slots[{formSlots.Count - 1}].id");
    }

    [Fact]
    public void FingerGunsRoundTripsTheVerifiedCookedPackage()
    {
        var package = CompileFingerGuns();
        var loaded = LoadFingerGuns(package);
        Assert.True(loaded.IsValid, string.Join("; ", loaded.Diagnostics));
        Assert.Equal("1.11.0", loaded.Package!.Metadata.RuntimeApiMin);
        Assert.Equal(package.Definition.Slots.Single(slot => slot.Id == "ground.A").Timeline.Stages.Single().Operations
                .OfType<CookedStartCapabilityOperation>().Single().Parameters,
            loaded.Package.Definition.Slots.Single(slot => slot.Id == "ground.A").Timeline.Stages.Single().Operations
                .OfType<CookedStartCapabilityOperation>().Single().Parameters);
    }

    [Theory]
    [InlineData("api")]
    [InlineData("fireTriggerTick")]
    [InlineData("recoveryTicks")]
    [InlineData("shotsPerReload")]
    [InlineData("reloadTicks")]
    [InlineData("reloadAnimationId")]
    [InlineData("airMaxFallSpeed")]
    [InlineData("airRecoilSpeed")]
    [InlineData("projectile")]
    [InlineData("iasa")]
    public void FingerGunsLoaderRejectsTamperedCookedParametersOrApi(string field)
    {
        var package = CompileFingerGuns();
        var runtime = JsonNode.Parse(package.CanonicalBytes)!;
        string api = field == "api" ? "1.10.0" : "1.11.0";
        runtime["metadata"]!["compatibility"]!["runtimeApiMin"] = api;
        var stage = runtime["character"]!["slots"]!.AsArray().Single(s => (string?)s!["id"] == "ground.A")!
            ["timeline"]!["stages"]![0]!;
        var p = stage["operations"]![0]!["parameters"]!;
        switch (field)
        {
            case "api": break;
            case "reloadAnimationId": p[field] = "anim.missing"; break;
            case "airMaxFallSpeed": case "airRecoilSpeed": p[field] = 31; break;
            case "projectile": p[field]!["speed"] = 0; break;
            case "iasa": stage["iasaTicks"] = 1; break;
            default: p[field] = 0; break;
        }
        var tampered = new CookedCharacterPackage(package.Metadata with { RuntimeApiMin = api }, package.Definition,
            package.Budget, package.Diagnostics, Encoding.UTF8.GetBytes(runtime.ToJsonString()));
        var rejected = LoadFingerGuns(tampered);
        Assert.False(rejected.IsValid);
        Assert.Contains(rejected.Diagnostics, d => d.Code == (field == "api"
            ? "package.compatibility.unsupported" : "package.capability.parameters-invalid"));
    }

    [Fact]
    public void TetherRoundTripsTheVerifiedCookedPackage()
    {
        var package = RingDuel.Compile(null);
        var loaded = LoadFingerGuns(package);
        Assert.True(loaded.IsValid, string.Join("; ", loaded.Diagnostics));
        Assert.Equal("1.14.0", loaded.Package!.Metadata.RuntimeApiMin);
        Assert.Equal(TetherTests.Parameters, loaded.Package.Definition.Slots.Single(slot => slot.Id == "ground.A")
            .Timeline.Stages.Single().Operations.OfType<CookedStartCapabilityOperation>().Single().Parameters);
    }

    [Theory]
    [InlineData("api")]
    [InlineData("tick")]
    [InlineData("iasa")]
    [InlineData("operation")]
    [InlineData("gravity")]
    [InlineData("knockback")]
    [InlineData("speed")]
    [InlineData("arrivalStunTicks")]
    [InlineData("reelAnimationId")]
    public void TetherLoaderRejectsTamperedCookedSlotsOrApi(string field)
    {
        var package = RingDuel.Compile(null);
        var runtime = JsonNode.Parse(package.CanonicalBytes)!;
        string api = field == "api" ? "1.12.0" : "1.14.0";
        runtime["metadata"]!["compatibility"]!["runtimeApiMin"] = api;
        var stage = runtime["character"]!["slots"]!.AsArray().Single(s => (string?)s!["id"] == "ground.A")!
            ["timeline"]!["stages"]![0]!;
        var operation = stage["operations"]![0]!;
        var p = operation["parameters"]!;
        switch (field)
        {
            case "api": break;
            case "tick": operation["tick"] = 1; break;
            case "iasa": stage["iasaTicks"] = 1; break;
            case "operation": stage["operations"]!.AsArray().Add(operation.DeepClone()); break;
            case "gravity": p["projectile"]!["gravity"] = 4; break;
            case "knockback": p["projectile"]!["baseKnockback"] = 4; break;
            case "speed": p["projectile"]!["speed"] = 0; break;
            case "arrivalStunTicks": p[field] = 0; break;
            case "reelAnimationId": p[field] = "anim.bashie.ring"; break;
        }
        var tampered = new CookedCharacterPackage(package.Metadata with { RuntimeApiMin = api }, package.Definition,
            package.Budget, package.Diagnostics, Encoding.UTF8.GetBytes(runtime.ToJsonString()));
        var rejected = LoadFingerGuns(tampered);
        Assert.False(rejected.IsValid);
        Assert.Contains(rejected.Diagnostics, d => d.Code == (field == "api"
            ? "package.compatibility.unsupported" : "package.capability.parameters-invalid"));
    }

    private static CookedCharacterPackage CompileFingerGuns()
    {
        string root = FindRepoFile("client/Unity/Assets/CharacterPackages/fightguy");
        var character = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "character.json")))!.AsObject();
        FingerGunsTests.UseGuns(character, slot => slot["aimMode"] = "groundVector");
        var compiled = CharacterPackageCompiler.Compile(File.ReadAllText(Path.Combine(root, "package.json")),
            character.ToJsonString(), CharacterCookProfile.TrustedBuiltIn);
        Assert.NotNull(compiled.CookedPackage);
        return compiled.CookedPackage!;
    }

    private static CookedCharacterPackageLoadResult LoadFingerGuns(CookedCharacterPackage package)
        => CookedCharacterPackageLoader.LoadAssembly(CharacterPackageAssembler.Assemble(BuildInput(package,
            Array.Empty<PackageDependencySource>(), package.Definition.CapabilityRequirements, Array.Empty<CharacterDiagnostic>())));

    [Theory]
    [InlineData("ground.A", "fuseTicks", 0)]
    [InlineData("ground.A", "hopBackSpeed", 0)]
    [InlineData("ground.A", "explosionDamage", -1)]
    [InlineData("ground.F", "speed", 61)]
    [InlineData("ground.F", "selfPushSpeed", 41)]
    [InlineData("ground.E", "fuelTicks", 0)]
    [InlineData("ground.E", "sinkSpeed", 0)]
    [InlineData("ground.E", "jumpBurstTicks", 181)]
    [InlineData("ground.E", "jumpBurstSpeed", 31)]
    [InlineData("ground.E", "horizontalSpeed", 41)]
    public void MankiSourceAndCookedLoaderRejectTheSameInvalidCanAndFlightBounds(string slotId, string field, int value)
    {
        string root = FindRepoFile("client/Unity/Assets/CharacterPackages/manki");
        string metadata = File.ReadAllText(Path.Combine(root, "package.json"));
        string source = File.ReadAllText(Path.Combine(root, "character.json"));
        JsonNode Parameters(JsonNode character) => character["slots"]!.AsArray()
            .Single(slot => (string?)slot!["id"] == slotId)!["timeline"]!["stages"]![0]!["operations"]!.AsArray()
            .Single(operation => operation!.AsObject().ContainsKey("capabilityId"))!["parameters"]!;
        var authoring = JsonNode.Parse(source)!;
        Parameters(authoring)[field] = value;
        var rejectedSource = CharacterPackageCompiler.Compile(metadata, authoring.ToJsonString(), CharacterCookProfile.TrustedBuiltIn);
        Assert.Null(rejectedSource.CookedPackage);
        Assert.Contains(rejectedSource.Diagnostics, d => d.Code == "value.out-of-range");

        var package = CharacterPackageCompiler.Compile(metadata, source, CharacterCookProfile.TrustedBuiltIn).CookedPackage!;
        var runtime = JsonNode.Parse(package.CanonicalBytes)!;
        Parameters(runtime["character"]!)[field] = value;
        var tampered = new CookedCharacterPackage(package.Metadata, package.Definition,
            package.Budget, package.Diagnostics, Encoding.UTF8.GetBytes(runtime.ToJsonString()));
        var rejectedCooked = LoadFingerGuns(tampered);
        Assert.False(rejectedCooked.IsValid);
        Assert.Contains(rejectedCooked.Diagnostics, d => d.Code == "package.capability.parameters-invalid");
    }

    [Fact]
    public void ChargedStreamRoundTripsTheVerifiedCookedPackage()
    {
        string root = FindRepoFile("client/Unity/Assets/CharacterPackages/fightguy");
        var character = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "character.json")))!.AsObject();
        ChargedStreamTests.UseStream(character);
        var compiled = CharacterPackageCompiler.Compile(File.ReadAllText(Path.Combine(root, "package.json")),
            character.ToJsonString(), CharacterCookProfile.TrustedBuiltIn);
        Assert.NotNull(compiled.CookedPackage);
        var package = compiled.CookedPackage!;
        var assembly = CharacterPackageAssembler.Assemble(BuildInput(package, Array.Empty<PackageDependencySource>(),
            package.Definition.CapabilityRequirements, Array.Empty<CharacterDiagnostic>()));
        var loaded = CookedCharacterPackageLoader.LoadAssembly(assembly);
        Assert.True(loaded.IsValid, string.Join("; ", loaded.Diagnostics));
        Assert.Equal(package.Definition.Slots.Single(slot => slot.Id == "ground.A").Timeline.Stages.Single().Operations
                .OfType<CookedStartCapabilityOperation>().Single().Parameters,
            loaded.Package!.Definition.Slots.Single(slot => slot.Id == "ground.A").Timeline.Stages.Single().Operations
                .OfType<CookedStartCapabilityOperation>().Single().Parameters);
    }





    [Fact]
    public void Assemble_IsDeterministic_AndVerifiesCanonicalTree()
    {
        var first = AssembleFixture();
        var second = AssembleFixture();
        Assert.True(first.IsValid, string.Join("\n", first.Diagnostics.Select(x => x.Message)));
        Assert.Equal(first.ManifestBytes, second.ManifestBytes);
        Assert.Equal(first.RuntimeBytes, second.RuntimeBytes);
        Assert.Equal(first.PoseBytes, second.PoseBytes);
        Assert.Equal(first.BindingBytes, second.BindingBytes);
        Assert.Equal(first.PackageHash, second.PackageHash);
        Assert.Equal(new[] { "manifest.json", "character.runtime.json", "poses.bin", "client.bindings" },
            new[] { CharacterPackageAssembler.ManifestPath, CharacterPackageAssembler.RuntimePath, CharacterPackageAssembler.PosePath, CharacterPackageAssembler.BindingPath });

        var files = Files(first);
        var verification = CharacterPackageAssembler.Verify(files);
        Assert.True(verification.IsValid, string.Join("\n", verification.Diagnostics.Select(x => x.Message)));
    }

    [Fact]
    public void CookedLoader_RoundTripsShieldRadiusThroughVerifiedPackage()
    {
        var assembled = AssembleFixture();
        var requirement = new MatchContentPackageRequirement(
            "fightguy", "0.0.0-dev", assembled.CookedContentHash, assembled.PackageHash);
        var loaded = CookedCharacterPackageLoader.LoadFiles(Files(assembled), requirement);

        Assert.True(loaded.IsValid, string.Join("\n", loaded.Diagnostics.Select(x => x.Message)));
        Assert.Equal(1.05f, loaded.Package!.Definition.ShieldRadius);
        Assert.Equal(1.05f, loaded.ToCharacterDefinition(CharacterClass.FightGuy).ShieldRadius);
    }
    [Fact]
    public void TargetedLeap_LoadedCookedPackageProducesLandingImpactAndRecovery()
    {
        string root = FindRepoFile("client/Unity/Assets/CharacterPackages/bonk");
        var compile = CharacterPackageCompiler.Compile(
            File.ReadAllText(Path.Combine(root, "package.json")),
            File.ReadAllText(Path.Combine(root, "character.json")),
            CharacterCookProfile.TrustedBuiltIn);
        Assert.True(compile.CookedPackage != null,
            string.Join("; ", compile.Diagnostics.Select(x => $"{x.Code}: {x.Message} ({x.Path})")));

        var package = compile.CookedPackage!;
        var assembly = CharacterPackageAssembler.Assemble(BuildInput(
            package, Array.Empty<PackageDependencySource>(), package.Definition.CapabilityRequirements,
            Array.Empty<CharacterDiagnostic>()));
        Assert.True(assembly.IsValid, string.Join("; ", assembly.Diagnostics.Select(x => x.Message)));

        var loaded = CookedCharacterPackageLoader.LoadAssembly(assembly);
        Assert.True(loaded.IsValid, string.Join("; ", loaded.Diagnostics.Select(x => x.Message)));
        var def = CookedCharacterRuntimeAdapter.ToCharacterDefinition(loaded.Package!);
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState(x: 20f, z: 10f);
        state.PY = TestHelpers.GroundPY(def);
        sim.RegisterEntity(1, def, state, loaded.BakedAnimation);
        sim.Tick(new Dictionary<ulong, InputState> { [1] = new InputState
            { ActiveSlot = 4, AimDistance = 400, IsAiming = true } });
        for (var i = 0; i < 9; i++)
            sim.Tick(new Dictionary<ulong, InputState> { [1] = new InputState
                { AimDistance = 400, IsAiming = true } });
        sim.Tick(new Dictionary<ulong, InputState> { [1] = new InputState { AimDistance = 400 } });
        Assert.True(sim.GetState(1).VY > 0f, "loaded move must launch toward its target");

        int impactTick = -1, endTick = -1, windows = 0;
        bool wasActive = false;
        for (var i = 0; i < 200; i++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { [1] = default });
            var current = sim.GetState(1);
            bool active = sim.Resolver.GetActiveHitboxes().Any(x => x.OwnerId == 1 && x.Damage == 13f);
            if (active && !wasActive)
            {
                windows++;
                impactTick = i;
                Assert.Equal((ushort)0, current.AttackElapsedTicks);  // E1: the landing clip starts at landing
            }
            if (impactTick >= 0 && endTick < 0 && current.State != ActionState.Attacking)
                endTick = i;
            wasActive = active;
        }
        Assert.Equal(1, windows);
        Assert.Equal(68, endTick - impactTick);
    }
    [Fact]
    public void SteeredCharge_LoadedCookedPackageGallopsAndEndsWithItsSkid()
    {
        string root = FindRepoFile("client/Unity/Assets/CharacterPackages/fightguy");
        var character = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(root, "character.json")))!.AsObject();
        SteeredChargeFixture.Apply(character);
        var compile = CharacterPackageCompiler.Compile(File.ReadAllText(Path.Combine(root, "package.json")),
            character.ToJsonString(), CharacterCookProfile.Workshop);
        Assert.True(compile.CookedPackage != null,
            string.Join("; ", compile.Diagnostics.Select(x => $"{x.Code}: {x.Message} ({x.Path})")));
        var package = compile.CookedPackage!;
        var assembly = CharacterPackageAssembler.Assemble(BuildInput(
            package, Array.Empty<PackageDependencySource>(), package.Definition.CapabilityRequirements,
            Array.Empty<CharacterDiagnostic>()));
        Assert.True(assembly.IsValid, string.Join("; ", assembly.Diagnostics.Select(x => x.Message)));
        var loaded = CookedCharacterPackageLoader.LoadAssembly(assembly);
        Assert.True(loaded.IsValid, string.Join("; ", loaded.Diagnostics.Select(x => x.Message)));
        Assert.IsType<CookedSteeredChargeCapabilityParameters>(loaded.Package!.Definition.Slots
            .Single(x => x.Id == "ground.R").Timeline.Stages.Single().Operations
            .OfType<CookedStartCapabilityOperation>().Single().Parameters);

        var def = CookedCharacterRuntimeAdapter.ToCharacterDefinition(loaded.Package!);
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState(x: 100f, z: 20f);
        state.PY = TestHelpers.GroundPY(def);
        sim.RegisterEntity(1, def, state, loaded.BakedAnimation);
        sim.Tick(new Dictionary<ulong, InputState> { [1] = new InputState { ActiveSlot = AbilitySlots.R } });
        int gallop = 0;
        bool skid = false;
        for (var i = 0; i < 200 && sim.GetActiveAbility(1) != null; i++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { [1] = default });
            var current = sim.GetState(1);
            if (current.VZ > 11.99f) gallop++;
            skid |= current.ComboStage == 4;
        }
        Assert.InRange(gallop, 58, 61);
        Assert.True(skid);
        Assert.InRange(sim.GetState(1).PZ, 20f + 12f - .5f, 20f + 12f + 2f);
    }

    [Fact]
    public void CookedLoader_RejectsRetiredTargetedLeapInIsolatedPackage()
    {
        const string retiredId = "slop.internal.bonk.targeted-jump-slam.v1";
        string root = FindRepoFile("client/Unity/Assets/CharacterPackages/bonk");
        var compile = CharacterPackageCompiler.Compile(
            File.ReadAllText(Path.Combine(root, "package.json")),
            File.ReadAllText(Path.Combine(root, "character.json")),
            CharacterCookProfile.TrustedBuiltIn);
        Assert.True(compile.CookedPackage != null,
            string.Join("; ", compile.Diagnostics.Select(x => $"{x.Code}: {x.Message} ({x.Path})")));

        var package = compile.CookedPackage!;
        var runtime = JsonNode.Parse(Encoding.UTF8.GetString(package.CanonicalBytes))!.AsObject();
        var character = runtime["character"]!.AsObject();
        character["capabilityRequirements"]![0]!["capabilityId"] = retiredId;
        foreach (var slot in character["slots"]!.AsArray())
        {
            string? slotId = slot?["id"]?.GetValue<string>();
            if (slotId != "ground.E" && slotId != "air.E")
                continue;
            foreach (var operation in slot!["timeline"]!["stages"]![0]!["operations"]!.AsArray())
                if (operation?["kind"]?.GetValue<int>() == (int)CookedOperationKind.StartCapability)
                    operation!["capabilityId"] = retiredId;
        }

        byte[] runtimeBytes = Encoding.UTF8.GetBytes(runtime.ToJsonString());
        var retiredPackage = new CookedCharacterPackage(
            package.Metadata, package.Definition, package.Budget, package.Diagnostics, runtimeBytes);
        var input = BuildInput(
            retiredPackage, Array.Empty<PackageDependencySource>(),
            new[] { new CookedCapabilityRequirement(retiredId, "1") },
            Array.Empty<CharacterDiagnostic>());
        var assembly = CharacterPackageAssembler.Assemble(input);
        Assert.True(assembly.IsValid, string.Join("; ", assembly.Diagnostics.Select(x => x.Message)));

        var loaded = CookedCharacterPackageLoader.LoadAssembly(assembly);
        Assert.False(loaded.IsValid);
        Assert.Contains(loaded.Diagnostics, x => x.Code == "package.capability.retired");
    }


    [Fact]
    public void CookedLoader_RejectsPreviousSchemaVersion()
    {
        var current = Compile();
        var oldMetadata = current.Metadata with { CookedSchemaVersion = 3 };
        byte[] oldRuntime = Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(current.CanonicalBytes)
                .Replace("\"cookedSchemaVersion\":4", "\"cookedSchemaVersion\":3", StringComparison.Ordinal));
        var oldPackage = new CookedCharacterPackage(
            oldMetadata, current.Definition, current.Budget, current.Diagnostics, oldRuntime);
        var assembled = CharacterPackageAssembler.Assemble(BuildInput(
            oldPackage, Array.Empty<PackageDependencySource>(),
            Array.Empty<CookedCapabilityRequirement>(), Array.Empty<CharacterDiagnostic>()));
        Assert.True(assembled.IsValid, string.Join("\n", assembled.Diagnostics.Select(x => x.Message)));

        var requirement = new MatchContentPackageRequirement(
            "fightguy", "0.0.0-dev", assembled.CookedContentHash, assembled.PackageHash);
        var loaded = CookedCharacterPackageLoader.LoadFiles(Files(assembled), requirement);
        Assert.False(loaded.IsValid);
        Assert.Contains(loaded.Diagnostics, x => x.Code == "package.compatibility.unsupported");
        Assert.Null(loaded.Package);
    }

    [Theory]
    [InlineData("warpRange", "4.25")]
    [InlineData("useTargetLock", "true")]
    [InlineData("rotateTowardTarget", "true")]
    [InlineData("trackingStrength", "0.4")]
    public void CookedLoader_RejectsRetiredTargetingInVerifiedPayload(string field, string value)
    {
        var package = Compile();
        var runtime = JsonNode.Parse(Encoding.UTF8.GetString(package.CanonicalBytes))!;
        runtime["character"]!["slots"]![0]!["timeline"]!["stages"]![0]![field] = JsonNode.Parse(value);
        var modified = new CookedCharacterPackage(package.Metadata, package.Definition,
            package.Budget, package.Diagnostics, Encoding.UTF8.GetBytes(runtime.ToJsonString()));
        var assembled = CharacterPackageAssembler.Assemble(BuildInput(modified,
            Array.Empty<PackageDependencySource>(), package.Definition.CapabilityRequirements,
            Array.Empty<CharacterDiagnostic>()));
        Assert.True(assembled.IsValid, string.Join("; ", assembled.Diagnostics.Select(x => x.Message)));

        var loaded = CookedCharacterPackageLoader.LoadAssembly(assembled);

        Assert.False(loaded.IsValid);
        Assert.Null(loaded.Package);
        Assert.Contains(loaded.Diagnostics, diagnostic => diagnostic.Code == "package.targeting.retired");
    }

    [Fact]
    public void Verify_RejectsMissingExtraAndTamperedPayloads()
    {
        var result = AssembleFixture();
        var files = Files(result);
        files.Remove(CharacterPackageAssembler.PosePath);
        Assert.False(CharacterPackageAssembler.Verify(files).IsValid);
        files[CharacterPackageAssembler.PosePath] = result.PoseBytes;
        files["unexpected"] = new byte[] { 1 };
        Assert.False(CharacterPackageAssembler.Verify(files).IsValid);
        files.Remove("unexpected");
        files[CharacterPackageAssembler.BindingPath][0] ^= 1;
        Assert.False(CharacterPackageAssembler.Verify(files).IsValid);
    }

    [Fact]
    public void Verify_RejectsTrailingPoseBytesAndOrphanBinding()
    {
        var result = AssembleFixture();
        var files = Files(result);
        files[CharacterPackageAssembler.PosePath] = result.PoseBytes.Concat(new byte[] { 0 }).ToArray();
        Assert.False(CharacterPackageAssembler.Verify(files).IsValid);

        files = Files(result);
        string json = Encoding.UTF8.GetString(files[CharacterPackageAssembler.BindingPath]);
        json = json.Replace("]}", ",{\"semanticId\":\"anim.orphan\",\"poseTrackId\":\"anim.orphan\",\"clipGlobalObjectId\":\"x\",\"poseName\":\"anim.orphan\",\"frameCount\":1,\"clipLengthBits\":0,\"sampleRate\":60,\"extrapolation\":0}]}", StringComparison.Ordinal);
        files[CharacterPackageAssembler.BindingPath] = Encoding.UTF8.GetBytes(json);
        Assert.False(CharacterPackageAssembler.Verify(files).IsValid);
    }

    [Fact]
    public void Verify_RejectsTruncatedPayloadsAndUnknownManifestPath()
    {
        var result = AssembleFixture();
        var files = Files(result);
        files[CharacterPackageAssembler.PosePath] = result.PoseBytes.Take(7).ToArray();
        Assert.False(CharacterPackageAssembler.Verify(files).IsValid);

        files = Files(result);
        files[CharacterPackageAssembler.ManifestPath] = Encoding.UTF8.GetBytes("{\"manifestSchemaVersion\":1}");
        Assert.False(CharacterPackageAssembler.Verify(files).IsValid);
    }

    [Fact]
    public void WarningsAndSortedMetadataSurviveAssembly()
    {
        var package = Compile();
        var input = BuildInput(package,
            new[] { new PackageDependencySource("zeta", "1.0.0", Hash("z")), new PackageDependencySource("alpha", "1.0.0", Hash("a")) },
            new[] { new CookedCapabilityRequirement("zeta.cap", "1"), new CookedCapabilityRequirement("alpha.cap", "1") },
            new[] { new CharacterDiagnostic(CharacterDiagnosticSeverity.Warning, "warning.one", "test", "kept") });
        var result = CharacterPackageAssembler.Assemble(input);
        Assert.True(result.IsValid, string.Join("\n", result.Diagnostics.Select(x => x.Message)));
        string manifest = Encoding.UTF8.GetString(result.ManifestBytes);
        Assert.True(manifest.IndexOf("\"packageId\":\"alpha\"", StringComparison.Ordinal) < manifest.IndexOf("\"packageId\":\"zeta\"", StringComparison.Ordinal));
        Assert.Contains("\"severity\":\"warning\",\"code\":\"warning.one\"", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void Assemble_RequiresDeclaredTumbleAndLowPoseBindings()
    {
        string root = FindRepoFile("client/Unity/Assets/CharacterPackages/fightguy");
        var character = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "character.json")))!.AsObject();
        character["presentation"]!["tumble"] = "anim.tumble";
        character["presentation"]!["crouch"] = "anim.fightguy.crouch";
        character["presentation"]!["slide"] = "anim.fightguy.slide";
        character["presentation"]!["shield"] = "anim.fightguy.shield";
        var compile = CharacterPackageCompiler.Compile(
            File.ReadAllText(Path.Combine(root, "package.json")),
            character.ToJsonString(), CharacterCookProfile.TrustedBuiltIn);
        Assert.NotNull(compile.CookedPackage);
        var package = compile.CookedPackage!;
        var input = BuildInput(package, Array.Empty<PackageDependencySource>(), Array.Empty<CookedCapabilityRequirement>(), Array.Empty<CharacterDiagnostic>());
        var binding = JsonNode.Parse(input.BindingBytes)!.AsObject();
        var animations = binding["animations"]!.AsArray();
        animations.Remove(animations.Single(x => x!["semanticId"]!.GetValue<string>() == "anim.tumble"));
        animations.Remove(animations.Single(x => x!["semanticId"]!.GetValue<string>() == "anim.fightguy.crouch"));
        animations.Remove(animations.Single(x => x!["semanticId"]!.GetValue<string>() == "anim.fightguy.shield"));
        var broken = new CharacterPackageAssemblyInput(
            input.PackageId, input.Version, input.Creator, input.License, input.Attribution,
            input.AuthoringSchemaVersion, input.CookedSchemaVersion, input.RuntimeApiMin, input.RuntimeApiMax,
            input.SourceHash, input.Dependencies, input.CapabilityRequirements, input.CookerVersion, input.UnityVersion,
            input.BindingSchemaVersion, input.PoseFormat, input.PoseVersion, input.SampleRate, input.Warnings,
            input.RuntimeBytes, input.PoseBytes, Encoding.UTF8.GetBytes(binding.ToJsonString()), input.CookedPackage);
        var result = CharacterPackageAssembler.Assemble(broken);
        Assert.Contains(result.Diagnostics, x => x.Code == "package.binding.missing");
    }

    [Fact]
    public void ReturningProjectile_LoadedCookedPackageThrowsTheLidAndRejectsTamperedReturns()
    {
        string root = FindRepoFile("client/Unity/Assets/CharacterPackages/fightguy");
        var character = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "character.json")))!.AsObject();
        ReturningProjectileFixture.Apply(character);
        var compile = CharacterPackageCompiler.Compile(File.ReadAllText(Path.Combine(root, "package.json")),
            character.ToJsonString(), CharacterCookProfile.Workshop);
        Assert.True(compile.CookedPackage != null,
            string.Join("; ", compile.Diagnostics.Select(x => $"{x.Code}: {x.Message} ({x.Path})")));
        var package = compile.CookedPackage!;
        var input = BuildInput(package, Array.Empty<PackageDependencySource>(), package.Definition.CapabilityRequirements,
            Array.Empty<CharacterDiagnostic>());
        var loaded = CookedCharacterPackageLoader.LoadAssembly(CharacterPackageAssembler.Assemble(input));
        Assert.True(loaded.IsValid, string.Join("; ", loaded.Diagnostics.Select(x => x.Message)));
        Assert.Equal((ushort)20, loaded.Package!.Definition.Slots.Single(x => x.Id == "ground.1").Timeline.Stages.Single()
            .Operations.OfType<CookedSpawnProjectileOperation>().Single().Projectile.Return!.OutboundTicks);

        var def = CookedCharacterRuntimeAdapter.ToCharacterDefinition(loaded.Package!);
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState(x: 100f, z: 20f);
        state.PY = TestHelpers.GroundPY(def);
        sim.RegisterEntity(1, def, state, loaded.BakedAnimation);
        sim.Tick(new Dictionary<ulong, InputState> { [1] = new InputState { ActiveSlot = AbilitySlots.Slot1 } });
        bool thrown = false;
        for (var i = 0; i < 30 && !thrown; i++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { [1] = default });
            thrown = sim.Resolver.GetActiveHitboxes().Any(x => x.Return is { OutboundTicks: 20 });
        }
        Assert.True(thrown, "the loaded package throws a returning lid");

        CookedCharacterPackageLoadResult Tampered(Action<JsonObject> mutate, string runtimeApi = "1.8.0")
        {
            var runtime = JsonNode.Parse(package.CanonicalBytes)!.AsObject();
            mutate(runtime);
            runtime["metadata"]!["compatibility"]!["runtimeApiMin"] = runtimeApi;
            byte[] bytes = Encoding.UTF8.GetBytes(runtime.ToJsonString());
            var tampered = new CookedCharacterPackage(package.Metadata with { RuntimeApiMin = runtimeApi }, package.Definition,
                package.Budget, package.Diagnostics, bytes);
            var tamperedInput = BuildInput(tampered, Array.Empty<PackageDependencySource>(),
                package.Definition.CapabilityRequirements, Array.Empty<CharacterDiagnostic>());
            return CookedCharacterPackageLoader.LoadAssembly(CharacterPackageAssembler.Assemble(tamperedInput));
        }
        static JsonNode Back(JsonObject runtime) => runtime["character"]!["slots"]!.AsArray()
            .Single(x => (string?)x!["id"] == "ground.1")!["timeline"]!["stages"]![0]!["operations"]!.AsArray()
            .Single(x => x!.AsObject().ContainsKey("projectile"))!["projectile"]!["return"]!;
        var untouched = Tampered(_ => { });
        Assert.True(untouched.IsValid, string.Join("; ", untouched.Diagnostics.Select(x => x.Message)));
        foreach (var (field, value) in new (string, JsonNode)[]
                 { ("outboundTicks", 240), ("returnSpeed", 0), ("returnAcceleration", -2), ("selfHitStunTicks", 0) })
        {
            var rejected = Tampered(runtime => Back(runtime)[field] = value.DeepClone());
            Assert.False(rejected.IsValid, field);
            Assert.Contains(rejected.Diagnostics, x => x.Code == "package.capability.parameters-invalid");
        }
        var oldApi = Tampered(_ => { }, "1.7.0");
        Assert.False(oldApi.IsValid);
        Assert.Contains(oldApi.Diagnostics, x => x.Code == "package.compatibility.unsupported");
    }

    [Fact]
    public void HitPresentationRequiresPrefabBindingButNotAnimationOrPose()
    {
        string root = FindRepoFile("client/Unity/Assets/CharacterPackages/fightguy");
        var character = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "character.json")))!.AsObject();
        const string hitId = "presentation.test.contact";
        character["presentationIds"]!.AsArray().Add(hitId);
        character["slots"]![0]!["hitPresentationId"] = hitId;
        var compiled = CharacterPackageCompiler.Compile(File.ReadAllText(Path.Combine(root, "package.json")),
            character.ToJsonString(), CharacterCookProfile.TrustedBuiltIn);
        Assert.True(compiled.CookedPackage != null, string.Join("; ", compiled.Diagnostics.Select(x => x.Message)));
        var package = compiled.CookedPackage!;
        var input = BuildInput(package, Array.Empty<PackageDependencySource>(),
            Array.Empty<CookedCapabilityRequirement>(), Array.Empty<CharacterDiagnostic>());
        var assembled = CharacterPackageAssembler.Assemble(input);
        Assert.True(assembled.IsValid, string.Join("; ", assembled.Diagnostics.Select(x => x.Message)));
        var loaded = CookedCharacterPackageLoader.LoadAssembly(assembled);
        Assert.True(loaded.IsValid, string.Join("; ", loaded.Diagnostics.Select(x => x.Message)));
        Assert.Equal(hitId, loaded.ToCharacterDefinition().GetCookedSlotAbility(AbilitySlots.Slot1, false)!.HitPresentationId);

        var bindings = JsonNode.Parse(input.BindingBytes)!.AsObject();
        bindings["presentations"] = new JsonArray();
        var missing = new CharacterPackageAssemblyInput(
            input.PackageId, input.Version, input.Creator, input.License, input.Attribution,
            input.AuthoringSchemaVersion, input.CookedSchemaVersion, input.RuntimeApiMin, input.RuntimeApiMax,
            input.SourceHash, input.Dependencies, input.CapabilityRequirements, input.CookerVersion, input.UnityVersion,
            input.BindingSchemaVersion, input.PoseFormat, input.PoseVersion, input.SampleRate, input.Warnings,
            input.RuntimeBytes, input.PoseBytes, Encoding.UTF8.GetBytes(bindings.ToJsonString()), input.CookedPackage);
        var rejected = CharacterPackageAssembler.Assemble(missing);
        Assert.Contains(rejected.Diagnostics, diagnostic => diagnostic.Code == "package.binding.presentation-missing");

        var runtime = JsonNode.Parse(package.CanonicalBytes)!.AsObject();
        var declared = runtime["character"]!["presentationIds"]!.AsArray();
        declared.Remove(declared.Single(id => id!.GetValue<string>() == hitId));
        var undeclaredPackage = new CookedCharacterPackage(package.Metadata, package.Definition,
            package.Budget, package.Diagnostics, Encoding.UTF8.GetBytes(runtime.ToJsonString()));
        var undeclared = CharacterPackageAssembler.Assemble(BuildInput(undeclaredPackage,
            Array.Empty<PackageDependencySource>(), Array.Empty<CookedCapabilityRequirement>(), Array.Empty<CharacterDiagnostic>()));
        var rejectedRuntime = CookedCharacterPackageLoader.LoadAssembly(undeclared);
        Assert.False(rejectedRuntime.IsValid);
        Assert.Contains(rejectedRuntime.Diagnostics, diagnostic => diagnostic.Code == "package.binding.presentation-undeclared");
    }

    [Fact]
    public void GravityWindowSurvivesCookedAssemblyAndRuntimeLoading()
    {
        string root = FindRepoFile("client/Unity/Assets/CharacterPackages/fightguy");
        var character = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "character.json")))!.AsObject();
        character["slots"]![0]!["allowSlideCarry"] = false;
        character["captureGeometry"]!["reach"] = 1.1f;
        character["captureGeometry"]!["attackerAnchor"]!["z"] = 0.22f;
        character["presentation"]!["shield"] = "anim.fightguy.shield";
        ((JsonArray)character["slots"]![0]!["timeline"]!["stages"]![0]!["operations"]!).Add(new JsonObject
        {
            ["kind"] = "gravityWindow",
            ["tick"] = 0,
            ["unit"] = "normalized",
            ["gravityScale"] = 0.5,
            ["durationTicks"] = 5,
        });

        var compile = CharacterPackageCompiler.Compile(
            File.ReadAllText(Path.Combine(root, "package.json")),
            character.ToJsonString(),
            CharacterCookProfile.TrustedBuiltIn);
        Assert.True(compile.CookedPackage != null,
            string.Join("; ", compile.Diagnostics.Select(x => $"{x.Code}: {x.Message} ({x.Path})")));
        var assembly = CharacterPackageAssembler.Assemble(BuildInput(
            compile.CookedPackage, Array.Empty<PackageDependencySource>(),
            Array.Empty<CookedCapabilityRequirement>(), Array.Empty<CharacterDiagnostic>()));
        Assert.True(assembly.IsValid, string.Join("; ", assembly.Diagnostics.Select(x => x.Message)));

        var loaded = CookedCharacterPackageLoader.LoadAssembly(assembly);
        Assert.True(loaded.IsValid, string.Join("; ", loaded.Diagnostics.Select(x => x.Message)));
        var loadedPackage = loaded.Package!;
        Assert.Equal(1.1f, loadedPackage.Definition.CaptureGeometry.Reach);
        Assert.Equal(0.22f, loadedPackage.Definition.CaptureGeometry.AttackerAnchor.Z);
        Assert.Equal("anim.fightguy.shield", loadedPackage.Definition.Presentation.Shield);
        var operation = Assert.Single(loadedPackage.Definition.Slots
            .Single(slot => slot.Id == "ground.1").Timeline.Stages[0].Operations
            .OfType<CookedGravityWindowOperation>());
        Assert.Equal((0.5f, (ushort)5), (operation.GravityScale, operation.DurationTicks));
    }
    [Theory]
    [InlineData("1.0.0", false, true)]
    [InlineData("1.1.0", false, true)]
    [InlineData("1.2.0", false, true)]
    [InlineData("1.3.0", false, true)]
    [InlineData("1.4.0", false, true)]
    [InlineData("1.5.0", false, true)]
    [InlineData("2.0.0", false, false)]
    [InlineData("1.0.0", true, false)]
    [InlineData("1.3.0", true, false)]
    [InlineData("1.4.0", true, true)]
    [InlineData("1.5.0", true, true)]
    public void Loader_EnforcesRuntimeMinimumAgainstFeatures(string minimum, bool startupCorrection, bool supported)
    {
        string root = FindRepoFile("client/Unity/Assets/CharacterPackages/fightguy");
        var character = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "character.json")))!;
        FightGuyFixture.WithoutTitleMatch(character.AsObject());
        foreach (var slot in character["slots"]!.AsArray())
        foreach (var stage in slot!["timeline"]!["stages"]!.AsArray())
        {
            var operations = stage!["operations"]!.AsArray();
            for (int index = operations.Count - 1; index >= 0; index--)
            {
                var operation = operations[index]!;
                string? kind = operation["kind"]?.GetValue<string>();
                if (kind == "armorWindow" || !startupCorrection && kind == "startupAimCorrection" ||
                    operation["capabilityId"]?.GetValue<string>() == CharacterPackageCompiler.ChargedProjectileCapabilityId)
                    operations.RemoveAt(index);
                else if (operation["hitbox"]?["fixedHitstunTicks"] is not null)
                    operation["hitbox"]!["fixedHitstunTicks"] = 0;
            }
        }
        var compiled = CharacterPackageCompiler.Compile(File.ReadAllText(Path.Combine(root, "package.json")),
            character.ToJsonString(), CharacterCookProfile.TrustedBuiltIn);
        Assert.NotNull(compiled.CookedPackage);
        var package = compiled.CookedPackage!;
        var input = BuildInput(package, Array.Empty<PackageDependencySource>(),
            Array.Empty<CookedCapabilityRequirement>(), Array.Empty<CharacterDiagnostic>());
        var runtime = JsonNode.Parse(input.RuntimeBytes)!.AsObject();
        runtime["metadata"]!["compatibility"]!["runtimeApiMin"] = minimum;
        var runtimeBytes = Encoding.UTF8.GetBytes(runtime.ToJsonString());
        var candidatePackage = new CookedCharacterPackage(package.Metadata with { RuntimeApiMin = minimum },
            package.Definition, package.Budget, package.Diagnostics, runtimeBytes);
        var candidate = new CharacterPackageAssemblyInput(
            input.PackageId, input.Version, input.Creator, input.License, input.Attribution,
            input.AuthoringSchemaVersion, input.CookedSchemaVersion, minimum, input.RuntimeApiMax,
            input.SourceHash, input.Dependencies, input.CapabilityRequirements, input.CookerVersion, input.UnityVersion,
            input.BindingSchemaVersion, input.PoseFormat, input.PoseVersion, input.SampleRate, input.Warnings,
            runtimeBytes, input.PoseBytes, input.BindingBytes, candidatePackage);
        var assembled = CharacterPackageAssembler.Assemble(candidate);
        Assert.True(assembled.IsValid, string.Join("\n", assembled.Diagnostics));
        var loaded = CookedCharacterPackageLoader.LoadAssembly(assembled);
        Assert.Equal(supported, loaded.IsValid);
        if (!supported)
            Assert.Contains(loaded.Diagnostics, diagnostic => diagnostic.Code == "package.compatibility.unsupported");
    }

    [Fact]
    public void CaptureGeometryChangesCookedContentAndPackageHashes()
    {
        var baselinePackage = Compile();
        string root = FindRepoFile("client/Unity/Assets/CharacterPackages/fightguy");
        var character = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "character.json")))!.AsObject();
        character["captureGeometry"]!["reach"] = 1.2f;
        var changedCompile = CharacterPackageCompiler.Compile(
            File.ReadAllText(Path.Combine(root, "package.json")),
            character.ToJsonString(),
            CharacterCookProfile.TrustedBuiltIn);
        Assert.NotNull(changedCompile.CookedPackage);

        var emptyDependencies = Array.Empty<PackageDependencySource>();
        var emptyCapabilities = Array.Empty<CookedCapabilityRequirement>();
        var noWarnings = Array.Empty<CharacterDiagnostic>();
        var baseline = CharacterPackageAssembler.Assemble(BuildInput(
            baselinePackage, emptyDependencies, emptyCapabilities, noWarnings));
        var changed = CharacterPackageAssembler.Assemble(BuildInput(
            changedCompile.CookedPackage!, emptyDependencies, emptyCapabilities, noWarnings));

        Assert.True(baseline.IsValid, string.Join("; ", baseline.Diagnostics.Select(x => x.Message)));
        Assert.True(changed.IsValid, string.Join("; ", changed.Diagnostics.Select(x => x.Message)));
        Assert.NotEqual(baseline.CookedContentHash, changed.CookedContentHash);
        Assert.NotEqual(baseline.PackageHash, changed.PackageHash);
    }

    [Theory]
    [InlineData("wibou", "1.15.0", "1.14.0")]
    [InlineData("fightguy", "1.20.0", "1.19.0")]
    [InlineData("bonk", "1.8.0", "1.7.0")]
    public void CookedLoaderRestoresParametersAndEnforcesApiFloor(string id, string api, string incompatibleApi)
    {
        string root = FindRepoFile("client/Unity/Assets/CharacterPackages/" + id);
        var compiled = CharacterPackageCompiler.Compile(
            File.ReadAllText(Path.Combine(root, "package.json")),
            File.ReadAllText(Path.Combine(root, "character.json")),
            CharacterCookProfile.TrustedBuiltIn);
        Assert.NotNull(compiled.CookedPackage);
        var package = compiled.CookedPackage!;
        Assert.Equal(api, package.Metadata.RuntimeApiMin);
        var assembly = CharacterPackageAssembler.Assemble(BuildInput(
            package, Array.Empty<PackageDependencySource>(), package.Definition.CapabilityRequirements,
            Array.Empty<CharacterDiagnostic>()));
        Assert.True(assembly.IsValid, string.Join("; ", assembly.Diagnostics.Select(x => x.Message)));
        var loaded = CookedCharacterPackageLoader.LoadAssembly(assembly);
        Assert.True(loaded.IsValid, string.Join("; ", loaded.Diagnostics.Select(x => x.Message)));
        if (id == "fightguy")
        {
            var charge = Assert.IsType<CookedChargedProjectileCapabilityParameters>(loaded.Package!.Definition.Slots
                .Single(slot => slot.Id == "ground.A").Timeline.Stages[0].Operations
                .OfType<CookedStartCapabilityOperation>().Single().Parameters);
            Assert.Equal(1f, charge.GetRadius(45));
            Assert.Equal(20f, charge.Projectile.Speed);
            Assert.Equal((ushort)112, charge.Projectile.MaxFlightTicks);
            var source = package.Definition.Slots.Single(slot => slot.Id == "ground.F");
            var grab = Assert.IsType<CookedCommandGrabCapabilityParameters>(loaded.Package!.Definition.Slots
                .Single(slot => slot.Id == "ground.F").Timeline.Stages[0].Operations
                .OfType<CookedStartCapabilityOperation>().Single().Parameters);
            Assert.Equal(source.Timeline.Stages[0].Operations.OfType<CookedStartCapabilityOperation>().Single().Parameters, grab);
        }
        if (id == "bonk")
            Assert.All(loaded.Package!.Definition.Slots.Where(slot => slot.Id.EndsWith(".F")),
                slot => Assert.True(slot.AimMovementSpeedScale is > 0f and < 1f));
        var input = BuildInput(package, Array.Empty<PackageDependencySource>(),
            package.Definition.CapabilityRequirements, Array.Empty<CharacterDiagnostic>());
        var runtime = JsonNode.Parse(input.RuntimeBytes)!.AsObject();
        runtime["metadata"]!["compatibility"]!["runtimeApiMin"] = incompatibleApi;
        byte[] runtimeBytes = Encoding.UTF8.GetBytes(runtime.ToJsonString());
        var incompatiblePackage = new CookedCharacterPackage(
            package.Metadata with { RuntimeApiMin = incompatibleApi }, package.Definition,
            package.Budget, package.Diagnostics, runtimeBytes);
        var incompatibleInput = new CharacterPackageAssemblyInput(
            input.PackageId, input.Version, input.Creator, input.License, input.Attribution,
            input.AuthoringSchemaVersion, input.CookedSchemaVersion, incompatibleApi, input.RuntimeApiMax,
            input.SourceHash, input.Dependencies, input.CapabilityRequirements, input.CookerVersion,
            input.UnityVersion, input.BindingSchemaVersion, input.PoseFormat, input.PoseVersion,
            input.SampleRate, input.Warnings, runtimeBytes, input.PoseBytes, input.BindingBytes,
            incompatiblePackage);
        var incompatibleAssembly = CharacterPackageAssembler.Assemble(incompatibleInput);
        Assert.True(incompatibleAssembly.IsValid, string.Join("; ", incompatibleAssembly.Diagnostics));
        var rejected = CookedCharacterPackageLoader.LoadAssembly(incompatibleAssembly);
        Assert.False(rejected.IsValid);
        Assert.Contains(rejected.Diagnostics, x => x.Code == "package.compatibility.unsupported");
    }
    private static CharacterPackageAssemblyResult AssembleFixture()
    {
        var package = Compile();
        return CharacterPackageAssembler.Assemble(BuildInput(package, Array.Empty<PackageDependencySource>(), Array.Empty<CookedCapabilityRequirement>(), Array.Empty<CharacterDiagnostic>()));
    }

    private static CharacterPackageAssemblyInput BuildInput(CookedCharacterPackage package, IReadOnlyList<PackageDependencySource> dependencies, IReadOnlyList<CookedCapabilityRequirement> capabilities, IReadOnlyList<CharacterDiagnostic> warnings)
    {
        string sourceHash = new string('a', 64);
        var names = new HashSet<string>(StringComparer.Ordinal)
        {
            package.Definition.Presentation.Idle, package.Definition.Presentation.Run, package.Definition.Presentation.Dash,
            package.Definition.Presentation.Jump, package.Definition.Presentation.Fall, package.Definition.Presentation.HitSmall,
            package.Definition.Presentation.HitMedium, package.Definition.Presentation.HitHard,
        };
        if (!string.IsNullOrEmpty(package.Definition.Presentation.Tumble))
            names.Add(package.Definition.Presentation.Tumble);
        if (!string.IsNullOrEmpty(package.Definition.Presentation.Crouch))
            names.Add(package.Definition.Presentation.Crouch);
        if (!string.IsNullOrEmpty(package.Definition.Presentation.Slide))
            names.Add(package.Definition.Presentation.Slide);
        foreach (string role in new[]
        {
            package.Definition.Presentation.Shield,
            package.Definition.Presentation.Grab,
            package.Definition.Presentation.Grabbed,
            package.Definition.Presentation.ThrowForward,
            package.Definition.Presentation.AirDodge,
            package.Definition.Presentation.Flying,
            package.Definition.Presentation.MayhemLaugh,
        })
            if (!string.IsNullOrEmpty(role))
                names.Add(role);
        foreach (var (_, role) in package.Definition.AlternateForm?.Presentation.Roles ?? Array.Empty<(string, string)>())
            if (!string.IsNullOrEmpty(role))
                names.Add(role);
        foreach (var slot in package.Definition.AllSlots)
        {
            if (!string.IsNullOrEmpty(slot.AimAnimationId)) names.Add(slot.AimAnimationId);
            foreach (var stage in slot.Timeline.Stages)
                foreach (string id in stage.AnimationIds) names.Add(id);
        }

        var binding = new StringBuilder("{\"packageId\":")
            .Append(JsonSerializer.Serialize(package.Metadata.PackageId))
            .Append(",\"catalogSchemaVersion\":1,\"bindingSchemaVersion\":1,\"poseFormat\":\"SKEL\",\"poseVersion\":1,\"sampleRate\":60,\"sourceHash\":\"")
            .Append(sourceHash).Append("\",\"rigGlobalObjectId\":\"rig\",\"animations\":[");
        bool first = true;
        foreach (string name in names.OrderBy(x => x, StringComparer.Ordinal))
        {
            if (!first) binding.Append(',');
            first = false;
            binding.Append("{\"semanticId\":").Append(JsonSerializer.Serialize(name)).Append(",\"poseTrackId\":").Append(JsonSerializer.Serialize(name)).Append(",\"clipGlobalObjectId\":\"clip\",\"poseName\":").Append(JsonSerializer.Serialize(name)).Append(",\"frameCount\":1,\"clipLengthBits\":0,\"sampleRate\":60,\"extrapolation\":0}");
        }
        binding.Append("],\"presentations\":[");
        first = true;
        foreach (string id in package.Definition.PresentationIds.OrderBy(x => x, StringComparer.Ordinal))
        {
            if (!first) binding.Append(',');
            first = false;
            binding.Append("{\"semanticId\":").Append(JsonSerializer.Serialize(id)).Append(",\"prefabGlobalObjectId\":\"prefab\"}");
        }
        binding.Append("]}");

        var boneNames = new[] { "root" }
            .Concat(package.Definition.AttachmentBoneIds)
            .Concat(package.Definition.HurtboxBoneDefs.SelectMany(x => new[] { x.BoneId, x.EndBoneId })
                .Where(x => x != null).Select(x => CharacterBoneRegistry.BakedName(x)!))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        using var poses = new MemoryStream();
        WriteUInt32(poses, 0x4C454B53); WriteUInt32(poses, 1);
        WriteUInt32(poses, (uint)boneNames.Length); WriteUInt32(poses, (uint)names.Count);
        foreach (string boneName in boneNames) WriteString(poses, boneName);
        foreach (string name in names.OrderBy(x => x, StringComparer.Ordinal))
        {
            WriteString(poses, name);
            WriteUInt32(poses, 1);
            foreach (string _ in boneNames)
            {
                WriteUInt32(poses, 0); WriteUInt32(poses, 0); WriteUInt32(poses, 0);
            }
        }
        return new CharacterPackageAssemblyInput(
            package.Metadata.PackageId, package.Metadata.Version, "Binoui", "MIT", "SlopArena", 3,
            package.Metadata.CookedSchemaVersion, package.Metadata.RuntimeApiMin, package.Metadata.RuntimeApiMax, sourceHash,
            dependencies, capabilities, "test-cooker", "test-unity", 1, "SKEL", 1, 60, warnings,
            package.CanonicalBytes, poses.ToArray(), Encoding.UTF8.GetBytes(binding.ToString()), package);
    }

    private static CookedCharacterPackage Compile()
    {
        string root = FindRepoFile("client/Unity/Assets/CharacterPackages/fightguy");
        var result = CharacterPackageCompiler.Compile(
            File.ReadAllText(Path.Combine(root, "package.json")),
            File.ReadAllText(Path.Combine(root, "character.json")), CharacterCookProfile.TrustedBuiltIn);
        Assert.NotNull(result.CookedPackage);
        return result.CookedPackage!;
    }

    private static Dictionary<string, byte[]> Files(CharacterPackageAssemblyResult result) => new(StringComparer.Ordinal)
    {
        [CharacterPackageAssembler.ManifestPath] = result.ManifestBytes,
        [CharacterPackageAssembler.RuntimePath] = result.RuntimeBytes,
        [CharacterPackageAssembler.PosePath] = result.PoseBytes,
        [CharacterPackageAssembler.BindingPath] = result.BindingBytes,
    };

    private static string FindRepoFile(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(directory.FullName, relative);
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException(relative);
    }

    [Fact]
    public void CommittedFightGuyPackage_Verifies()
    {
        string directory = FindRepoFile("content-cooked/fightguy");
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (string name in new[] { CharacterPackageAssembler.ManifestPath, CharacterPackageAssembler.RuntimePath, CharacterPackageAssembler.PosePath, CharacterPackageAssembler.BindingPath })
            files[name] = File.ReadAllBytes(Path.Combine(directory, name));
        var result = CharacterPackageAssembler.Verify(files);
        Assert.True(result.IsValid, string.Join("\n", result.Diagnostics.Select(x => $"{x.Code} {x.Path}: {x.Message}")));
    }

    private static string Hash(string value) => new string('b', 64);
    private static void WriteUInt32(Stream stream, uint value) => stream.Write(BitConverter.GetBytes(value), 0, 4);
    private static void WriteString(Stream stream, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        WriteUInt32(stream, (uint)bytes.Length);
        stream.Write(bytes, 0, bytes.Length);
    }
}
