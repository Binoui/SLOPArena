using System;
using System.Linq;
using SlopArena.Shared;

namespace SlopArena.Client.UI
{
    public static class LegacyFighterSelection
    {
        public static string PackageId(string selector)
            => Enum.GetNames(typeof(CharacterClass)).Any(name => string.Equals(name, selector, StringComparison.OrdinalIgnoreCase))
                && Enum.TryParse<CharacterClass>(selector, true, out var legacy)
                && ShippedRoster.TryReadApplication(out var roster, out _)
                ? roster.Manifest.Resolve(legacy)?.PackageId ?? "" : "";
        public static string DisplayKey(string selector)
        {
            var package = PackageId(selector);
            return string.IsNullOrEmpty(package) ? selector ?? "" : package;
        }
        public static string Selector(string package)
            => ShippedRoster.TryReadApplication(out var roster, out _)
                ? roster.Manifest.ResolvePackage(package)?.Selector.ToString() ?? "" : "";
    }
}
