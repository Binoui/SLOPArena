using SlopArena.KitCheck;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Shared.Tests;

public class WorkshopAuthoringReferenceTests
{
    private static string Root
    {
        get
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            while (folder != null && !File.Exists(Path.Combine(folder.FullName, "src/Shared/CharacterPackageCompiler.cs"))) folder = folder.Parent;
            return folder?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
        }
    }

    [Fact]
    public void Generated_contract_matches_reference()
    {
        var document = File.ReadAllText(Path.Combine(Root, "docs/characters/workshop-authoring-reference.md")).Replace("\r\n", "\n");
        var start = document.IndexOf(AuthoringReference.Start, StringComparison.Ordinal);
        var end = document.IndexOf(AuthoringReference.End, StringComparison.Ordinal) + AuthoringReference.End.Length;
        Assert.Equal(AuthoringReference.Generate().TrimEnd(), document[start..end]);
    }

    [Fact]
    public void Worked_example_compiles_without_diagnostics_under_workshop_profile()
    {
        var folder = Path.Combine(Root, "docs/characters/examples/workshop-kit");
        var result = CharacterPackageCompiler.Compile(File.ReadAllText(Path.Combine(folder, "package.json")),
            File.ReadAllText(Path.Combine(folder, "character.json")), CharacterCookProfile.Workshop);
        Assert.False(result.HasErrors);
        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.CookedPackage);
    }
}
