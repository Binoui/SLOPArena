using System.Reflection;
using SlopArena.Client.UI;
using Xunit;

namespace SlopArena.Shared.Tests;

public partial class PackageFighterContentTests
{
    static LocalFighterLibrary Library(MatchContentCatalog? catalog, string? diagnostic = null, params string[] ids)
        => (LocalFighterLibrary)Activator.CreateInstance(typeof(LocalFighterLibrary), BindingFlags.NonPublic | BindingFlags.Instance,
            null, new object?[] { catalog, ids.Length == 0 ? catalog?.Entries.Select(e => e.Identity.PackageId).ToArray() ?? Array.Empty<string>() : ids, diagnostic, MatchContentPolicy.LocalPlay }, null)!;

    static MatchContentCatalog RevisionCatalog(string version)
    {
        var fighters = new[] { "local.refresh-player", "local.refresh-cpu" }.Select(id =>
        {
            var fixture = Fixture(id, version);
            return AdmittedFighter.TestFixture(fixture.Files, fixture.Requirement);
        });
        return new MatchContentCatalogBuilder().Build(fighters.ToArray(), MatchContentPolicy.LocalPlay).Catalog!;
    }

    [Fact]
    public void InstalledSnapshotsAppendSortedMadeFightersAndKeepPreparedMatchesAfterRemoval()
    {
        var bundled = Library(Bundled());
        var a = Fixture("local.zebra"); var b = Fixture("local.alpha");
        var made = new[] { AdmittedFighter.TestFixture(a.Files, a.Requirement), AdmittedFighter.TestFixture(b.Files, b.Requirement) };
        var installed = bundled.WithMadeFighters(made);
        Assert.Equal(bundled.PackageIds, installed.PackageIds.Take(bundled.PackageIds.Count));
        Assert.Equal(new[] { "local.alpha", "local.zebra" }, installed.PackageIds.Skip(bundled.PackageIds.Count));
        var choice = installed.Choose("local.alpha");
        Assert.True(installed.TryPrepare(new[] { choice, installed.Choose("fightguy") }, out var started, out var failure), failure);
        var removed = installed.WithMadeFighters(Array.Empty<AdmittedFighter>());
        Assert.False(removed.Contains("local.alpha"));
        Assert.NotNull(choice.Resolve(started!));
        Assert.NotNull(installed.Choose("local.alpha").Resolve(started!));
        Assert.Equal(bundled.PackageIds, installed.PackagesFor(false));
    }
    [Fact]
    public void MadeFightersAreHiddenWhenTheModeRequiresBundledContent()
    {
        var library = Library(RevisionCatalog("1.0.0"));
        Assert.Empty(library.PackagesFor(false));
        Assert.Equal(2, library.PackagesFor(true).Count);
        Assert.Equal("YOURS", library.OriginLabel("local.refresh-player"));
        Assert.Empty(Library(Bundled()).OriginLabel("fightguy"));
    }
    [Fact]
    public void WorkspaceLibraryRetainsDevelopmentPolicyAndListsOnlyResolvedPackages()
    {
        var entry = Bundled().Entries.First();
        var loaded = CookedCharacterPackageLoader.LoadDirectory(Path.Combine(Root, "content-cooked", entry.Identity.PackageId),
            new(entry.Identity.PackageId, entry.Identity.Version, entry.Identity.CookedContentHash, entry.Identity.PackageHash));
        var development = new AdmittedFighter(loaded, null, development: true);
        var old = Fixture("local.old-workspace"); var fresh = Fixture("local.new-workspace");
        var catalog = new MatchContentCatalogBuilder().Build(new[] { development, AdmittedFighter.TestFixture(old.Files, old.Requirement) }, MatchContentPolicy.EditorDevelopment).Catalog!;
        var library = (LocalFighterLibrary)Activator.CreateInstance(typeof(LocalFighterLibrary), BindingFlags.NonPublic | BindingFlags.Instance,
            null, new object?[] { catalog, catalog.Entries.Select(e=>e.Identity.PackageId).ToArray(), null, MatchContentPolicy.EditorDevelopment }, null)!;
        var withLocal = library.WithLocalItem(AdmittedFighter.TestFixture(fresh.Files, fresh.Requirement));
        Assert.True(withLocal.TryGetCatalog(out var content, out var failure), failure);
        Assert.All(withLocal.PackageIds, id=>Assert.NotNull(content!.ResolvePackage(id)));
        Assert.DoesNotContain("local.old-workspace", withLocal.PackageIds);
        var cpu = withLocal.ChooseTrainingCpu();
        Assert.Equal(entry.Identity.PackageId, cpu.Revision.PackageId);
        Assert.NotNull(cpu.Resolve(content!));
        Assert.True(withLocal.TryPrepare(new[] { withLocal.Choose(entry.Identity.PackageId), withLocal.Choose("local.new-workspace") },out _,out failure), failure);
    }
    [Fact]
    public void LaunchRefreshesPlayerAndCpuButStartedCatalogRemainsPinned()
    {
        var started = RevisionCatalog("1.0.0");
        var newer = RevisionCatalog("1.0.1");
        var player = started.ResolvePackage("local.refresh-player")!.Choose();
        var cpu = started.ResolvePackage("local.refresh-cpu")!.Choose();
        var savedPlayer = MatchConfig.Selection;
        var savedSeats = MatchConfig.SoloSeats.ToArray();
        var mode = MatchConfig.Mode;
        try
        {
            MatchConfig.Mode = GameMode.Training;
            MatchConfig.SetPlayerFighter(player);
            MatchConfig.SoloSeats[0] = new SoloSeat { Kind = SeatKind.Cpu, Difficulty = Shared.AI.CpuDifficulty.Hard };
            MatchConfig.SoloSeats[0].SetFighter(cpu);
            Assert.True(MatchConfig.TryResolveLocalChoices(Library(newer), out var choices, out var failure), failure);
            Assert.Equal(newer.ResolvePackage("local.refresh-player")!.Identity, MatchConfig.Selection.Choice.Revision);
            Assert.Equal(newer.ResolvePackage("local.refresh-cpu")!.Identity, MatchConfig.Seat(2).Choice!.Revision);
            Assert.Equal(Shared.AI.CpuDifficulty.Hard, MatchConfig.Seat(2).Difficulty);
            Assert.Equal(2, choices.Count);
            Assert.NotNull(player.Resolve(started));
            Assert.NotNull(cpu.Resolve(started));
            Assert.Null(player.Resolve(newer));
            Assert.Null(cpu.Resolve(newer));
        }
        finally { MatchConfig.SetPlayerFighter(savedPlayer); Array.Copy(savedSeats, MatchConfig.SoloSeats, 3); MatchConfig.Mode = mode; }
    }

    [Fact]
    public void RemovedStartingFormFallsBackWithinTheSamePackage()
    {
        MatchContentCatalog Build(bool forms)
        {
            var fixture = Fixture("local.refresh-forms", forms ? "1.0.0" : "2.0.0", forms);
            return new MatchContentCatalogBuilder().Build(new[] { AdmittedFighter.TestFixture(fixture.Files, fixture.Requirement) }, MatchContentPolicy.LocalPlay).Catalog!;
        }
        var before = Build(true);
        var after = Build(false);
        var choice = before.ResolvePackage("local.refresh-forms")!.Choose(1);
        Assert.True(Library(after).TryRefreshChoice(choice, out var resolved));
        Assert.Equal("local.refresh-forms", resolved!.Revision.PackageId);
        Assert.Equal((byte)0, resolved.StartingForm);
        Assert.Equal((byte)1, choice.StartingForm);
        Assert.NotNull(choice.Resolve(before));
        Assert.Null(choice.Resolve(after));
    }

    [Fact]
    public void SnapshotCacheIsLazyWatchesSourcesAndRetriesFailedCompile()
    {
        int reads = 0;
        object stamp = new();
        var content = RevisionCatalog("2.0.0");
        var cache = new LocalFighterLibrary.SourceCache(() => ++reads == 1 ? Library(null, "compile failed") : Library(content), () => stamp);
        Assert.Equal(0, reads);
        var failedSnapshot = cache.Read();
        Assert.False(failedSnapshot.TryGetCatalog(out _, out _));
        Assert.Same(failedSnapshot, cache.Read());
        Assert.Equal(1, reads);
        var fixedSnapshot = cache.Refresh();
        Assert.True(fixedSnapshot.TryGetCatalog(out var fixedCatalog, out _));
        Assert.Same(content, fixedCatalog);
        Assert.Same(fixedSnapshot, cache.Read());
        Assert.Equal(2, reads);
        stamp = new object();
        Assert.NotSame(fixedSnapshot, cache.Read());
        Assert.Equal(3, reads);
        cache.Invalidate();
        cache.Read();
        Assert.Equal(4, reads);
    }

    [Fact]
    public void LaunchKeepsOverrideLocalAndRefusesRemovedPackages()
    {
        var catalog = RevisionCatalog("1.0.0");
        var selected = catalog.ResolvePackage("local.refresh-player")!.Choose();
        var saved = MatchConfig.Selection;
        var seat = MatchConfig.SoloSeats[0];
        var mode = MatchConfig.Mode;
        try
        {
            MatchConfig.Mode = GameMode.Training;
            MatchConfig.SetPlayerFighter(selected);
            MatchConfig.SoloSeats[0].SetFighter(catalog.ResolvePackage("local.refresh-cpu")!.Choose());
            var selection = MatchConfig.Selection;
            Assert.True(MatchConfig.TryResolveLocalChoices(Library(catalog), out var choices, out _, "local.refresh-cpu"));
            Assert.Equal("local.refresh-cpu", choices[0].Revision.PackageId);
            Assert.Same(selection, MatchConfig.Selection);
            Assert.False(MatchConfig.TryResolveLocalChoices(Library(new MatchContentCatalog(Array.Empty<MatchContentEntry>())), out _, out _));
            Assert.Same(selection, MatchConfig.Selection);
        }
        finally { MatchConfig.SetPlayerFighter(saved); MatchConfig.Mode = mode; MatchConfig.SoloSeats[0] = seat; }
    }

    [Fact]
    public void MissingCatalogAndDefaultDoNotThrowFromInputOrRestore()
    {
        var cache = typeof(LocalFighterLibrary).GetField("source", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        _ = LocalFighterLibrary.Current;
        var snapshot = cache.GetType().GetField("snapshot", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var original = snapshot.GetValue(cache);
        var seats = MatchConfig.SoloSeats.ToArray();
        var player = MatchConfig.Selection;
        try
        {
            snapshot.SetValue(cache, Library(new MatchContentCatalog(Array.Empty<MatchContentEntry>())));
            Array.Clear(MatchConfig.SoloSeats);
            Assert.Equal(0, MatchConfig.JoinSoloSeat(23));
            Assert.Equal(SeatKind.Open, MatchConfig.SoloSeats[0].Kind);
            MatchConfig.SoloSeats[0].SetFighter(player.Choice);
            Assert.Equal(0, MatchConfig.JoinSoloSeat(23));
            Assert.Equal(SeatKind.Open, MatchConfig.SoloSeats[0].Kind);
            Assert.Same(player.Choice, MatchConfig.SoloSeats[0].Choice);
            var seat = new SoloSeat();
            seat.Fighter = "fightguy";
            Assert.Null(seat.Choice);
            Assert.Null(new FighterChoiceState { PackageId = "fightguy" }.Restore());
            Assert.Equal(SeatKind.Open, new SoloSeatState { Kind = SeatKind.Cpu }.Restore().Kind);
            MatchConfig.SetPlayerFighter(new FighterChoiceState { PackageId = "missing" });
            Assert.Same(player, MatchConfig.Selection);
        }
        finally { snapshot.SetValue(cache, original); Array.Copy(seats, MatchConfig.SoloSeats, 3); MatchConfig.SetPlayerFighter(player); }
    }

    [Fact]
    public void LibrarySkipsUnresolvedRosterRowsAndCatalogReadIgnoresSeats()
    {
        var content = RevisionCatalog("3.0.0");
        var library = Library(content, null, "local.refresh-player", "missing", "local.refresh-cpu");
        Assert.Equal(new[] { "local.refresh-player", "local.refresh-cpu" }, library.PackageIds);
        var saved = MatchConfig.Selection;
        var mode = MatchConfig.Mode;
        try
        {
            MatchConfig.Mode = GameMode.Training;
            MatchConfig.SetPlayerFighter(Bundled().ResolvePackage("wibou")!.Choose());
            Assert.False(MatchConfig.TryResolveLocalChoices(library, out _, out _));
            Assert.True(library.TryGetCatalog(out var read, out _));
            Assert.Same(content, read);
        }
        finally { MatchConfig.SetPlayerFighter(saved); MatchConfig.Mode = mode; }
    }

    [Theory]
    [InlineData("Wibou", "wibou")]
    [InlineData("wibou", "wibou")]
    [InlineData("7", "")]
    [InlineData("129", "")]
    [InlineData("unlisted-selector", "")]
    public void LegacySessionMigratesOnlyNamedSelectors(string input, string expected)
        => Assert.Equal(expected, LegacyFighterSelection.PackageId(input));

    [Theory]
    [InlineData("unlisted-selector")]
    [InlineData("7")]
    public void UnresolvedRelayKeepsItsDisplayName(string selector)
        => Assert.Equal(selector, LegacyFighterSelection.DisplayKey(selector));
}

[CollectionDefinition("Local fighter state", DisableParallelization = true)]
public sealed class LocalFighterStateCollection { }
