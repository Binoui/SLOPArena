using SlopArena.Shared;
using SlopArena.Client.UI;
using Xunit;

namespace SlopArena.Shared.Tests;

public partial class PackageFighterContentTests
{
    [Fact]
    public void ChoicePinsRevisionAndRejectsUnsupportedForms()
    {
        var catalog = Bundled();
        var nyx = catalog.ResolvePackage("nyx")!;
        var choice = nyx.Choose(1);
        Assert.Equal(nyx.Identity, choice.Revision);
        Assert.Equal(1, choice.StartingForm);
        Assert.Same(nyx, choice.Resolve(catalog));
        Assert.Equal(nyx.Choose(1), choice);
        Assert.NotEqual(nyx.Choose(), choice);
        Assert.Throws<ArgumentOutOfRangeException>(() => nyx.Choose(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => catalog.ResolvePackage("fightguy")!.Choose(1));
    }

    [Fact]
    public void SelectorFreeCpuChoiceResolvesAndRegistersSeatThree()
    {
        var catalog = Local();
        var seatThree = catalog.ResolvePackage("local.tumbleweed")!.Choose();
        var saved = MatchConfig.Seat(3);
        try
        {
            MatchConfig.Seat(3).Kind = SeatKind.Cpu;
            MatchConfig.Seat(3).SetFighter(seatThree);
            Assert.Equal(SeatKind.Cpu, MatchConfig.Seat(3).Kind);
            Assert.Null(seatThree.LegacySelector);
            var entry = MatchConfig.Seat(3).Choice!.Resolve(catalog)!;
            var arena = default(ArenaDefinition);
            var simulation = new ServerSimulation(arena);
            simulation.RegisterEntity(3, entry.Definition, new CharacterState { EntityId = 3 }, entry.BakedAnimation);
            Assert.Same(entry.Definition, simulation.GetDefinition(3));
            Assert.Null(seatThree.Resolve(Bundled()));
            var changed = new MatchContentEntry(entry.Handle, null, entry.Identity with { PackageHash = new string('0', 64) }, entry.DisplayName, entry.Definition);
            Assert.Null(seatThree.Resolve(new MatchContentCatalog(new[] { changed })));
        }
        finally { MatchConfig.Seat(3) = saved; }
    }
}

public sealed class StoredFighterChoiceTests
{
    [Theory]
    [InlineData("7", 0)]
    [InlineData("missing.package", 0)]
    [InlineData("fightguy", 1)]
    public void InvalidStoredChoiceFallsBackToDefault(string package, byte form)
    {
        var choice = new FighterChoiceState { PackageId = package, Form = form }.Restore();
        Assert.Equal(ShippedRoster.ReadApplication().DefaultFighter.PackageId, choice.Revision.PackageId);
        Assert.Equal(0, choice.StartingForm);
    }
}
