using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class BonkV2ContractTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../client/Unity/Assets/CharacterPackages/bonk"));
    private static JsonObject Source() => JsonNode.Parse(File.ReadAllText(Path.Combine(Root, "character.json")))!.AsObject();
    private static string Manifest => File.ReadAllText(Path.Combine(Root, "package.json"));
    private static JsonObject Parameters(JsonObject source, string slot)
        => source["slots"]!.AsArray().Single(s => (string?)s!["id"] == slot)!["timeline"]!["stages"]![0]!["operations"]!
            .AsArray().Single(o => (string?)o!["kind"] == "startCapability")!["parameters"]!.AsObject();

    [Fact]
    public void SourceRoundTripPreservesAllV2ValuesAndRequiresApi122()
    {
        var loaded = CharacterPackageSourceCodec.Load(Manifest, Source().ToJsonString());
        Assert.True(loaded.IsValid);
        var first = CharacterPackageCompiler.Compile(loaded.Source!, CharacterCookProfile.TrustedBuiltIn);
        var second = CharacterPackageCompiler.Compile(Manifest,
            CharacterPackageSourceCodec.SerializeCharacter(loaded.Source!.Character), CharacterCookProfile.TrustedBuiltIn);
        Assert.NotNull(first.CookedPackage); Assert.NotNull(second.CookedPackage);
        Assert.Equal("1.22.0", first.CookedPackage.Metadata.RuntimeApiMin);
        Assert.Equal(first.CookedPackage.CanonicalBytes, second.CookedPackage.CanonicalBytes);
        var q = first.CookedPackage.Definition.Slots.Single(s => s.Id == "ground.A").Timeline.Stages[0].Operations.OfType<CookedSpawnProjectileOperation>().Single().Projectile;
        Assert.Equal((ushort)120, q.ChallengeTicks);
        Assert.Equal(new ProjectileLegHit(5, 0, 20, 0, 1, 48), q.Return!.Hit);
        Assert.Equal(1.25f, q.Homing!.TurnDegreesPerTick);
    }

    [Theory]
    [InlineData("assistRange", -1)]
    [InlineData("assistRange", 11)]
    [InlineData("assistHalfAngleDegrees", 91)]
    [InlineData("assistDegreesPerSecond", 1441)]
    public void InvalidAssistIsRejected(string field, int value)
    {
        var source = Source();
        Parameters(source, "ground.R")["assist"] = JsonNode.Parse("{\"assistRange\":3.5,\"assistHalfAngleDegrees\":70,\"assistDegreesPerSecond\":900}");
        Parameters(source, "ground.R")["assist"]![field] = value;
        var result = CharacterPackageCompiler.Compile(Manifest, source.ToJsonString(), CharacterCookProfile.TrustedBuiltIn);
        Assert.Null(result.CookedPackage);
        Assert.Contains(result.Diagnostics, d => d.Code == "value.out-of-range");
    }

    [Theory]
    [InlineData("ground.E", "riseTicks", 0)]
    [InlineData("ground.E", "dropTicks", 0)]
    [InlineData("ground.E", "hitRecoveryTicks", 0)]
    [InlineData("ground.R", "trackDegreesPerSecond", 5000)]
    [InlineData("ground.R", "challengedRearTicks", 0)]
    public void InvalidV2ValuesAreRejected(string slot, string field, int value)
    {
        var source = Source(); Parameters(source, slot)[field] = value;
        var result = CharacterPackageCompiler.Compile(Manifest, source.ToJsonString(), CharacterCookProfile.TrustedBuiltIn);
        Assert.Null(result.CookedPackage);
        Assert.Contains(result.Diagnostics, d => d.Code == "value.out-of-range");
    }

    [Theory]
    [InlineData("ground.E", "riseTicks")]
    [InlineData("ground.R", "carry")]
    [InlineData("ground.R", "chestHitbox")]
    public void MissingRequiredV2FieldsAreDiagnostics(string slot, string field)
    {
        var source = Source(); Parameters(source, slot).Remove(field);
        var result = CharacterPackageCompiler.Compile(Manifest, source.ToJsonString(), CharacterCookProfile.TrustedBuiltIn);
        Assert.Null(result.CookedPackage);
        Assert.Contains(result.Diagnostics, d => d.Severity == CharacterDiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("steered-charge")]
    [InlineData("targeted-leap")]
    public void V1IdsAreRetiredWithNoExecutionPath(string name)
    {
        string source = Source().ToJsonString().Replace("slop.ability." + name + ".v2", "slop.ability." + name + ".v1");
        var result = CharacterPackageCompiler.Compile(Manifest, source, CharacterCookProfile.TrustedBuiltIn);
        Assert.Null(result.CookedPackage);
        Assert.Contains(result.Diagnostics, d => d.Code == "capability.retired");
    }
    [Fact]
    public void V2RegistryRejectsWrongVersionsAndParameterTypes()
    {
        Assert.True(Abilities.InternalCapabilityRegistry.TryCreate(CharacterPackageCompiler.SteeredChargeCapabilityId,
            "2", SteeredChargeTests.Parameters, out _));
        Assert.True(Abilities.InternalCapabilityRegistry.TryCreate(CharacterPackageCompiler.TargetedLeapCapabilityId,
            "2", TargetedLeapTests.Parameters, out _));
        Assert.False(Abilities.InternalCapabilityRegistry.TryCreate(CharacterPackageCompiler.TargetedLeapCapabilityId,
            "1", TargetedLeapTests.Parameters, out _));
        Assert.False(Abilities.InternalCapabilityRegistry.TryCreate(CharacterPackageCompiler.SteeredChargeCapabilityId,
            "2", TargetedLeapTests.Parameters, out _));
    }

    [Fact]
    public void FlingClipAliasesKeepTheRearAsTheFirstSortedClip()
    {
        var source = Source(); Parameters(source, "ground.R")["carry"]!["flingAnimationId"] = "anim.bonk.g4";
        var stage = source["slots"]!.AsArray().Single(s => (string?)s!["id"] == "ground.R")!["timeline"]!["stages"]![0]!;
        var clips = stage["animationIds"]!.AsArray();
        for (int i = 0; i < clips.Count; i++)
            if ((string?)clips[i] == "anim.bonk.gr-fling") clips[i] = "anim.bonk.g4";
        var result = CharacterPackageCompiler.Compile(Manifest, source.ToJsonString(), CharacterCookProfile.TrustedBuiltIn);
        Assert.Null(result.CookedPackage);
        Assert.Contains(result.Diagnostics, d => d.Code == "value.out-of-range" && d.Path.EndsWith("carry.flingAnimationId"));
    }

}
