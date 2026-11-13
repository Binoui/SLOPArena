using System;

namespace SlopArena.Shared;

/// <summary>The Phase 2 Room, launch and schema 2 selector boundary.</summary>
public static class LegacyFighterProtocol
{
    private enum CharacterClass : byte
    {
        None, Manki, FightGuy, Wibou, Bonk,
        Bashie = 6, Salamander = 7, Nyx = 8
    }

    public static bool TryPackageId(string? selector, out string packageId)
    {
        packageId = "";
        if (string.IsNullOrEmpty(selector)) return false;
        foreach (var name in Enum.GetNames(typeof(CharacterClass)))
        {
            if (name == nameof(CharacterClass.None) || !string.Equals(name, selector, StringComparison.OrdinalIgnoreCase)) continue;
            packageId = name.ToLowerInvariant();
            return true;
        }
        return false;
    }

    public static bool TryLaunchPackageId(string? selector, out string packageId)
    {
        packageId = "";
        if (!Enum.TryParse<CharacterClass>(selector, true, out var value)
            || value == CharacterClass.None || !Enum.IsDefined(typeof(CharacterClass), value)) return false;
        packageId = value.ToString().ToLowerInvariant();
        return true;
    }

    public static string Selector(string packageId)
    {
        foreach (var name in Enum.GetNames(typeof(CharacterClass)))
            if (name != nameof(CharacterClass.None) && name.ToLowerInvariant() == packageId) return name;
        throw new ArgumentException("Package has no online selector.", nameof(packageId));
    }
}
