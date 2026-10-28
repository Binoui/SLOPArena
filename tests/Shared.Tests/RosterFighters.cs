using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>Every fighter in the cooked roster manifest, for promises that must hold for the whole roster.</summary>
public static class RosterFighters
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    public static TheoryData<CharacterClass> All()
    {
        var data = new TheoryData<CharacterClass>();
        foreach (var entry in BuiltInRosterManifestCodec.Load(Path.Combine(Root, "content-cooked/roster/manifest.json")).Entries)
            data.Add(entry.Selector);
        return data;
    }
}
