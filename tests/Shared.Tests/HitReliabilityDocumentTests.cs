using System;
using System.Collections.Generic;
using SlopArena.Shared;
namespace SlopArena.Shared.Tests;

internal static class HitReliabilityGoldenFixture
{
    internal static readonly MatchContentIdentity Identity = new("fixture", "1.2.3", "source", "0123456789abcdef", "package");
    internal static MatchContentEntry Target(CharacterClass cls) => new(new ContentHandle(1), cls, Identity, cls.ToString(),
        new CharacterDefinition { Class = cls, CapsuleHeight = 1.8f, CapsuleRadius = 0.3f, HurtboxRadius = 0.3f });
    internal static MoveReliability Move() => new()
    {
        Slot = "ground.2", Name = "Blade <swing> ×", DurationTicks = 24, BakedFrames = 36, PlaybackSpeed = 1.5f,
        ActiveTicks = 3, ContactTicksAtProbe = 2,
        Reach = { new() { Target = "FightGuy", Intervals = { (0.3f, 0.55f), (0.9f, 2.1f) } } },
        Vertical = { new() { Target = "Manki", Lowest = -1.5f, Highest = 2f } },
        Sampling = { new() { Event = 1, Anchor = "hand→tip", Radius = 0.2f, MaxTipStepPerTick = 0.4f, SweptPositions = 80, TestedPositions = 60 } },
        Hits = { new() { Event = 1, FirstTick = 4, LastTick = 6, Damage = 7f, Radius = 0.2f, Touched = { ("FightGuy", 1f, 3), ("Manki", 1.5f, 0) } } },
        Arc = { new() { Target = "FightGuy", Distances = { -0.25f, 0f, 0.25f }, Heights = { 1f, 0f }, Rows = { ".#.", "###" } } },
        ActiveHitboxes = { new[] { 4f, 0f, 1f, 0.5f, 0f, 1f, 2f, 0.2f } },
        Flags = { "clip plays 1.50× authored speed" },
    };
}

public sealed class HitReliabilityDocumentTests
{
    [Xunit.Fact]
    public void JsonAndMarkdown_MatchPreExtractionSyntheticGolden()
    {
        var medium = HitReliabilityGoldenFixture.Target(CharacterClass.FightGuy);
        var targets = new HitReliabilityTargets(medium, HitReliabilityGoldenFixture.Target(CharacterClass.Manki));
        var moves = new[] { HitReliabilityGoldenFixture.Move() };
        var document = new HitReliabilityDocument(HitReliabilityGoldenFixture.Identity, moves, targets,
            new DateTime(2026, 10, 31, 12, 34, 56, DateTimeKind.Utc));
        Xunit.Assert.Equal(System.IO.File.ReadAllText(Fixture("hit-reliability.expected.json")), document.ToJson());
        Xunit.Assert.Equal(System.IO.File.ReadAllText(Fixture("hit-reliability.expected.md")),
            SlopArena.HitReliabilityReport.Program.Markdown(medium, moves));
    }

    [Xunit.Fact]
    public void Measure_RejectsForgedSlotsAndSkipsEmptyCanonicalSlots()
    {
        var targets = new HitReliabilityTargets(HitReliabilityGoldenFixture.Target(CharacterClass.FightGuy),
            HitReliabilityGoldenFixture.Target(CharacterClass.Manki));
        var definition = new CharacterDefinition();
        Xunit.Assert.Null(HitReliability.Measure(definition, null, CanonicalSlotProjection.All[0], targets));
        Xunit.Assert.Throws<ArgumentException>(() => HitReliability.Measure(definition, null,
            new SlotAddress("ground.1", true, "1", 0), targets));
    }

    private static string Fixture(string name)
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "tests/Shared.Tests/Fixtures")))
            dir = dir.Parent;
        return System.IO.Path.Combine(dir!.FullName, "tests/Shared.Tests/Fixtures", name);
    }
}
