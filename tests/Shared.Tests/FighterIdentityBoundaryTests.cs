using System.Diagnostics;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class FighterIdentityBoundaryTests
{
    [Theory]
    [InlineData("FightGuy", "fightguy")]
    [InlineData("Manki", "manki")]
    [InlineData("nYx", "nyx")]
    public void LegacyNamesResolveWithoutNumericInterpretation(string selector, string packageId)
    {
        Assert.True(LegacyFighterProtocol.TryPackageId(selector, out var parsed));
        Assert.Equal(packageId, parsed);
        Assert.True(LegacyFighterProtocol.TryPackageId(LegacyFighterProtocol.Selector(packageId), out parsed));
        Assert.Equal(packageId, parsed);
    }

    [Theory]
    [InlineData("5")]
    [InlineData("2")]
    [InlineData("Nilus")]
    [InlineData("None")]
    public void InvalidLegacyIdentityIsRejected(string selector)
        => Assert.False(LegacyFighterProtocol.TryPackageId(selector, out _));

    [Theory]
    [InlineData("FightGuy", "fightguy")]
    [InlineData("local.tumbleweed", "local.tumbleweed")]
    public void HurtboxFilesReadOldNamesAndWritePackageIds(string stored, string packageId)
    {
        string json = "{\"character\":\"" + stored + "\",\"boneDefs\":[{\"bone\":\"hips\",\"r\":0.2}]}";
        Assert.True(HurtboxOverride.TryParse(json, out var parsed, out var bones));
        Assert.Equal(packageId, parsed);
        Assert.Contains("\"character\": \"" + packageId + "\"", HurtboxOverride.Serialize(parsed, bones!));
    }

    [Theory]
    [InlineData("5")]
    [InlineData("2")]
    [InlineData("Nilus")]
    [InlineData("nilus")]
    [InlineData("None")]
    public void HurtboxFilesRejectRetiredOrNumericIdentity(string stored)
        => Assert.False(HurtboxOverride.TryParse("{\"character\":\"" + stored + "\",\"boneDefs\":[{\"bone\":\"hips\"}]}", out _, out _));

    [Fact]
    public void SourceKeepsEnumInsideProtocolBoundary()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var start = new ProcessStartInfo("python3") { WorkingDirectory = root, RedirectStandardOutput = true };
        start.ArgumentList.Add("tools/check_fighter_identity.py");
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        Assert.True(process.WaitForExit(30000));
        Assert.True(process.ExitCode == 0, output);
    }
}
