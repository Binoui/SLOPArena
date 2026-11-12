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
            var arena = TestHelpers.TestArena();
            var simulation = new SlopArena.Client.Simulation.LocalSimulationBridge(arena);
            simulation.RegisterEntity(3, entry.Definition, TestHelpers.PlayerState() with { EntityId = 3 }, entry.BakedAnimation);
            Assert.Same(entry.Definition, simulation.InternalSim.GetDefinition(3));
            var policy = new SlopArena.Shared.AI.HeuristicBotPolicy();
            var memory = new SlopArena.Shared.AI.BotMemory();
            var rng = new Random(3);
            for (int tick = 0; tick < 5; tick++)
            {
                var input = policy.Decide(simulation.GetState(3), TestHelpers.PlayerState() with { EntityId = 1 }, entry.Definition, rng, memory, arena);
                simulation.Tick(new Dictionary<ulong, InputState> { [3] = input });
            }
            Assert.Equal((ulong)3, simulation.GetState(3).EntityId);
            Assert.Null(seatThree.Resolve(Bundled()));
            var changed = new MatchContentEntry(entry.Handle, null, entry.Identity with { PackageHash = new string('0', 64) }, entry.DisplayName, entry.Definition);
            Assert.Null(seatThree.Resolve(new MatchContentCatalog(new[] { changed })));
        }
        finally { MatchConfig.Seat(3) = saved; }
    }
}

public sealed class StoredFighterChoiceTests
{
    [Fact]
    public void SingleFormChoiceKeepsNyxMenuPreferenceWithoutAdmittingAnInvalidForm()
    {
        try
        {
            MatchConfig.SetPlayerFighter("nyx"); MatchConfig.PlayerForm = 1;
            MatchConfig.SetPlayerFighter("fightguy");
            Assert.Equal(0, MatchConfig.Selection.Choice.StartingForm);
            Assert.Equal(1, MatchConfig.PreferredFormOf(1));
            var saved = new FighterChoiceState(MatchConfig.Selection.Choice, MatchConfig.PreferredFormOf(1));
            MatchConfig.PlayerForm = 0;
            MatchConfig.SetPlayerFighter(saved);
            Assert.Equal(1, MatchConfig.PreferredFormOf(1));
            MatchConfig.SetPlayerFighter("nyx");
            Assert.Equal(1, MatchConfig.Selection.Choice.StartingForm);
            var seat = new SoloSeat { Fighter = "nyx", Form = 1 };
            seat.Fighter = "fightguy";
            Assert.Equal(0, seat.Choice!.StartingForm);
            Assert.Equal(1, seat.PreferredForm);
            seat.Fighter = "nyx";
            Assert.Equal(1, seat.Choice!.StartingForm);
        }
        finally { MatchConfig.PlayerForm = 0; MatchConfig.Reset(); }
    }

    [Fact]
    public void LegacyNumericSeatChoiceCannotBecomeAPackage()
    {
        var state = System.Text.Json.JsonSerializer.Deserialize<SoloSeatState>(
            "{\"Kind\":1,\"Fighter\":7,\"Form\":1,\"Difficulty\":2,\"DeviceId\":0}",
            new System.Text.Json.JsonSerializerOptions { IncludeFields = true })!;
        var seat = state.Restore();
        Assert.Equal(SeatKind.Cpu, seat.Kind);
        Assert.Equal(ShippedRoster.ReadApplication().DefaultFighter.PackageId, seat.Choice!.Revision.PackageId);
        Assert.Equal(0, seat.Form);
    }

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
