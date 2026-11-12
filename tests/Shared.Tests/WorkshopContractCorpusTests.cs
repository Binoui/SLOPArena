using System.Text.Json;
using SlopArena.KitCheck;
using Xunit;

namespace SlopArena.Shared.Tests;

public class WorkshopContractCorpusTests
{
    internal static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "src/Shared/CharacterPackageCompiler.cs"))) directory = directory.Parent;
            return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
        }
    }

    [Fact]
    public void Original_corpus_preserves_output_bytes_and_diagnostic_codes()
    {
        var path = Path.Combine(Root, "tests/Shared.Tests/Fixtures/WorkshopContract/baseline.json");
        var cases = JsonSerializer.Deserialize<CorpusCase[]>(File.ReadAllText(path))!;
        Assert.Equal(43, cases.Length);
        Assert.Equal(34, cases.SelectMany(x => x.Diagnostics).Select(x => x.Code).Distinct().Count());
        foreach (var expected in cases)
        {
            var actual = ContractCorpus.Capture(expected.Name, expected.Profile, expected.Manifest, expected.Character);
            Assert.True(expected.Bytes == actual.Bytes, "Cooked bytes changed for " + expected.Name);
            Assert.True(expected.Diagnostics.Select(x => (x.Severity, x.Code)).SequenceEqual(actual.Diagnostics.Select(x => (x.Severity, x.Code))),
                "Diagnostic codes changed for " + expected.Name + "\nExpected " + string.Join(", ", expected.Diagnostics.Select(x => x.Code))
                + "\nActual " + string.Join(", ", actual.Diagnostics.Select(x => x.Code)));
        }
    }
}
