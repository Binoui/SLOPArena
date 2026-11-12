using System;
using SlopArena.Shared;

namespace SlopArena.Client.UI
{
    public static class LegacyFighterSelection
    {
        public static string PackageId(string selector)
            => Enum.TryParse<CharacterClass>(selector, true, out var legacy)
                && ShippedRoster.TryReadApplication(out var roster, out _)
                ? roster.Manifest.Resolve(legacy)?.PackageId ?? "" : "";
        public static string Selector(string package)
            => ShippedRoster.TryReadApplication(out var roster, out _)
                ? roster.Manifest.ResolvePackage(package)?.Selector.ToString() ?? "" : "";
    }
}
