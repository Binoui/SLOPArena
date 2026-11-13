using System.Text.Json;
using System.Diagnostics;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class FighterPresentationBindingsTests
{
    static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    [Fact]
    public void GeneratedPresentationIsFresh()
    {
        var start = new ProcessStartInfo("python3") { WorkingDirectory = Root, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("tools/fighter-presentation/generate.py");
        start.ArgumentList.Add("--check");
        using var process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(30000), "Fighter presentation generator timed out.");
        Assert.True(process.ExitCode == 0, output);
    }

    [Fact]
    public void EveryRosterFighterHasPresentationOnlyCardAndMatchingGeneratedBindings()
    {
        var roster = BuiltInRosterManifestCodec.Load(Path.Combine(Root, "content-cooked/roster/manifest.json"));
        using var generated = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Root, "client/Unity/Assets/Resources/Generated/FighterUi.json")));
        var bindings = generated.RootElement.GetProperty("fighters").EnumerateArray().ToDictionary(x => x.GetProperty("packageId").GetString()!);
        Assert.Equal(roster.Entries.Count, bindings.Count);
        foreach (var entry in roster.Entries)
        {
            using var card = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Root, "client/Unity/Assets/CharacterPackages", entry.PackageId, "card.json")));
            Assert.Equal(new[] { "description", "tutorialHint", "workInProgress" }, card.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(x => x));
            Assert.False(string.IsNullOrWhiteSpace(card.RootElement.GetProperty("description").GetString()));
            Assert.Contains(card.RootElement.GetProperty("workInProgress").ValueKind, new[] { JsonValueKind.True, JsonValueKind.False });
            var binding = bindings[entry.PackageId];
            Assert.Equal(LegacyFighterProtocol.Selector(entry.PackageId), binding.GetProperty("alias").GetString());
            Assert.Equal(JsonSerializer.Serialize(card.RootElement), JsonSerializer.Serialize(binding.GetProperty("card")));
            using var runtime = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Root, "content-cooked", entry.PackageId, "character.runtime.json")));
            var definition = runtime.RootElement.GetProperty("character");
            Assert.Equal(definition.GetProperty("displayName").GetString(), binding.GetProperty("names")[0].GetString());
            if (definition.TryGetProperty("alternateForm", out var alternate) && alternate.ValueKind != JsonValueKind.Null)
                Assert.Equal(alternate.GetProperty("displayName").GetString(), binding.GetProperty("names")[1].GetString());
            foreach (var portrait in binding.GetProperty("portraits").EnumerateArray())
                if (portrait.GetString() is { Length: > 0 } path)
                    Assert.True(File.Exists(Path.Combine(Root, "client/Unity/Assets/Resources", path + ".png")), path);
            foreach (var form in binding.GetProperty("icons").EnumerateArray())
                foreach (var icon in form.EnumerateObject())
                    foreach (var media in icon.Value.EnumerateObject())
                        Assert.True(File.Exists(Path.Combine(Root, "client/Unity/Assets/Resources", media.Value.GetString() + ".png")));
        }
    }
}
