using System.Text.Json.Nodes;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class BladeFlurryAdmissionTests
{
    [Theory]
    [InlineData("ascentStartTick", 0f)]
    [InlineData("ascentEndTick", 13f)]
    [InlineData("descentStartTick", 28f)]
    [InlineData("descentStartTick", 128f)]
    [InlineData("riseSpeed", 0f)]
    [InlineData("descentSpeed", -1f)]
    [InlineData("forwardSpeed", 61f)]
    public void InvalidAuthoredFlightIsRejected(string field, float value)
    {
        var (manifest, character) = Source();
        Flurry(character)["parameters"]![field] = value;
        var result = CharacterPackageCompiler.Compile(manifest, character.ToJsonString(), CharacterCookProfile.TrustedBuiltIn);
        Assert.Null(result.CookedPackage);
        Assert.Contains(result.Diagnostics, d => d.Code == "value.out-of-range");
    }

    [Fact]
    public void TheRetiredContractCannotBeReadmittedByTheTrustedProfile()
    {
        var (manifest, character) = Source();
        var requirement = character["capabilityRequirements"]!.AsArray()
            .Single(x => (string?)x!["capabilityId"] == "slop.internal.wibou.blade-flurry.v2")!;
        requirement["capabilityId"] = "slop.internal.wibou.blade-flurry.v1";
        requirement["capabilityVersion"] = "1";
        var operation = Flurry(character);
        operation["capabilityId"] = "slop.internal.wibou.blade-flurry.v1";
        operation["capabilityVersion"] = "1";
        operation["parameters"] = new JsonObject { ["forwardSpeed"] = 0, ["moveTicks"] = 90 };

        var result = CharacterPackageCompiler.Compile(manifest, character.ToJsonString(), CharacterCookProfile.TrustedBuiltIn);
        Assert.Null(result.CookedPackage);
        Assert.Contains(result.Diagnostics, d => d.Code == "capability.retired");
    }

    [Fact]
    public void VersionedFlightRemainsRestrictedToTrustedContent()
    {
        var (manifest, character) = Source();
        Assert.NotNull(CharacterPackageCompiler.Compile(manifest, character.ToJsonString(), CharacterCookProfile.TrustedBuiltIn).CookedPackage);
        var workshop = CharacterPackageCompiler.Compile(manifest, character.ToJsonString(), CharacterCookProfile.Workshop);
        Assert.Null(workshop.CookedPackage);
        Assert.Contains(workshop.Diagnostics, d => d.Code == "capability.untrusted");
    }

    private static JsonNode Flurry(JsonObject character) => character["slots"]!.AsArray()
        .Single(x => (string?)x!["id"] == "ground.F")!["timeline"]!["stages"]![0]!["operations"]!.AsArray()
        .Single(x => (string?)x!["capabilityId"] == "slop.internal.wibou.blade-flurry.v2")!;

    private static (string Manifest, JsonObject Character) Source()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        const string relative = "client/Unity/Assets/CharacterPackages/wibou";
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, relative)))
            directory = directory.Parent;
        Assert.NotNull(directory);
        string path = Path.Combine(directory!.FullName, relative);
        return (File.ReadAllText(Path.Combine(path, "package.json")),
            JsonNode.Parse(File.ReadAllText(Path.Combine(path, "character.json")))!.AsObject());
    }
}
