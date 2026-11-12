using System.Text.Json;
using SlopArena.KitCheck;
using Xunit;
using Xunit.Abstractions;

namespace SlopArena.Shared.Tests;

public sealed class WorkshopMutationContractTests(ITestOutputHelper output)
{
    private static string Fixture(string name) => Path.Combine(WorkshopContractCorpusTests.Root, "tests/Shared.Tests/Fixtures/WorkshopContract", name);

    [Fact]
    public void Deterministic_mutations_preserve_original_bytes_codes_order_and_round_trips()
    {
        var original = JsonSerializer.Deserialize<MutationDigest[]>(File.ReadAllText(Fixture("mutation-baseline.json")))!;
        var head = JsonSerializer.Deserialize<MutationDigest[]>(File.ReadAllText(Fixture("mutation-head-baseline.json")))!;
        var preexisting = original.Zip(head).Where(pair => pair.First.ContractHash != pair.Second.ContractHash).ToArray();
        Assert.Equal(new[] { "manki/Workshop/types", "manki/TrustedBuiltIn/types" },
            preexisting.Select(pair => pair.First.Fighter + "/" + pair.First.Profile + "/" + pair.First.Family));
        var actual = MutationContract.Run(Fixture("mutation-inputs.json"), Fixture("mutation-edges.json"), output.WriteLine);
        Assert.Equal(head.Length, actual.Count);
        foreach (var (expected, current) in head.Zip(actual))
        {
            Assert.Equal((expected.Fighter, expected.Profile, expected.Family, expected.Cases, expected.Successful, expected.RoundTrips, expected.ContractHash),
                (current.Fighter, current.Profile, current.Family, current.Cases, current.Successful, current.RoundTrips, current.ContractHash));
            Assert.True(current.UnusedWarnings <= expected.UnusedWarnings, "Only false unused-cue warnings may disappear.");
        }
        output.WriteLine($"{actual.Sum(x => x.Cases)} cases; {actual.Sum(x => x.Successful)} successful; {actual.Sum(x => x.RoundTrips)} round trips.");
    }

    [Fact]
    public void Removal_contracts_cover_every_schema_and_all_writable_defaults_preserve_exact_bytes()
    {
        var inputs = JsonSerializer.Deserialize<MutationInput[]>(File.ReadAllText(Fixture("mutation-inputs.json")))!;
        var expected = JsonSerializer.Deserialize<FieldDigest[]>(File.ReadAllText(Fixture("field-baseline.json")))!;
        var actual = FieldContract.Run(inputs, output.WriteLine);
        Assert.Equal(CharacterSourceSchema.Objects.Select(x => x.Id).OrderBy(x => x, StringComparer.Ordinal), actual.Select(x => x.Schema));
        Assert.Equal(expected, actual);
        output.WriteLine($"{actual.Count} schemas; {actual.Sum(x => x.Objects)} objects; {actual.Sum(x => x.Removals)} removals; {actual.Sum(x => x.DefaultInsertions)} default insertions.");
    }
}
