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
        Assert.Contains("\"character\": \"" + packageId + "\"", HurtboxOverride.Serialize(stored, bones!));
    }

    [Theory]
    [InlineData("5")]
    [InlineData("2")]
    [InlineData("Nilus")]
    [InlineData("nilus")]
    [InlineData("None")]
    public void HurtboxFilesRejectRetiredOrNumericIdentity(string stored)
        => Assert.False(HurtboxOverride.TryParse("{\"character\":\"" + stored + "\",\"boneDefs\":[{\"bone\":\"hips\"}]}", out _, out _));

    [Theory]
    [InlineData("6", "bashie")]
    [InlineData(" Manki ", "manki")]
    public void LaunchRetainsMasterLegacyForms(string selector, string expected)
    {
        Assert.True(LegacyFighterProtocol.TryLaunchPackageId(selector, out var package));
        Assert.Equal(expected, package);
    }

    [Theory]
    [InlineData("5")]
    [InlineData("unknown")]
    [InlineData("")]
    [InlineData("Bonk")]
    public void OpponentSelectionRejectsUnresolvableFighter(string selector)
    {
        var identity = new MatchContentIdentity("manki", "1", "", "", "");
        var entry = new MatchContentEntry(new ContentHandle(1), identity, "Manki", TestHelpers.EngineDef);
        var catalog = new MatchContentCatalog(new[] { entry });
        Assert.False(SlopArena.Client.UI.LegacyFighterSelection.TryResolveOpponent(selector, catalog, out _));
        Assert.True(SlopArena.Client.UI.LegacyFighterSelection.TryResolveOpponent("Manki", catalog, out var selected));
        Assert.Same(entry, selected);
    }

    [Fact]
    public void RosterRejectsSelectorPackageMismatch()
    {
        var requirement = new MatchContentPackageRequirement("dusty", "1", new string('a', 64), new string('b', 64));
        var manifest = new BuiltInRosterManifest(1, new[] { new BuiltInRosterEntry("Salamander", "dusty", requirement) });
        Assert.Contains(MatchContentCatalogBuilder.ValidateRoster(manifest), d => d.Code == "catalog.selector.package-mismatch");
        var shipped = BuiltInRosterManifestCodec.Load(Path.Combine(AppContext.BaseDirectory, "../../../../../content-cooked/roster/manifest.json"));
        Assert.Empty(MatchContentCatalogBuilder.ValidateRoster(shipped));
    }

    [Theory]
    [InlineData("5")]
    [InlineData("unknown")]
    [InlineData("")]
    public void OnlineHandleMapRejectsInvalidSelectorAtConstruction(string selector)
    {
        var identity = new MatchContentIdentity("manki", "1", "", "", "");
        Assert.Throws<ArgumentException>(() => new MatchContentHandleMap(2,
            new[] { new MatchContentHandleRecord(new ContentHandle(1), selector, identity, "Manki") }));
    }

    [Fact]
    public void SourceKeepsEnumInsideProtocolBoundary()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var start = new ProcessStartInfo("python3") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("tools/check_fighter_identity.py");
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(30000));
        Assert.True(process.ExitCode == 0, output + error);
    }
}
