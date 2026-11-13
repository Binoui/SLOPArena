using System;
using System.Linq;
using SlopArena.Shared;

namespace SlopArena.Client.UI
{
    public static class LegacyFighterSelection
    {
        public static string PackageId(string selector)
            => LegacyFighterProtocol.TryPackageId(selector, out var packageId) ? packageId : "";
        public static string DisplayKey(string selector)
        {
            var package = PackageId(selector);
            return string.IsNullOrEmpty(package) ? selector ?? "" : package;
        }
        public static string Selector(string package)
            => ShippedRoster.TryReadApplication(out var roster, out _)
                ? roster.Manifest.ResolvePackage(package) is { } entry ? LegacyFighterProtocol.Selector(entry.PackageId) : "" : "";
    }
}
