using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SlopArena.Shared;

/// <summary>Resolves a built-in selector through the cooked catalog boundary used by matches.</summary>
public static class BuiltInContentResolver
{
    public static MatchContentEntry Resolve(CharacterClass selector)
    {
        var shipped = ShippedRoster.ReadApplication();
        return ResolveCatalog(selector, shipped.CookedRoot, shipped);
    }

    public static MatchContentEntry Resolve(CharacterClass selector, string cookedRoot)
        => ResolveCatalog(selector, ResolveRoot(cookedRoot), null);

    private static MatchContentEntry ResolveCatalog(CharacterClass selector, string cookedRoot, ShippedRoster? shipped)
    {
        var manifest = shipped?.Manifest ?? BuiltInRosterManifestCodec.Load(Path.Combine(cookedRoot, "roster", "manifest.json"));
        var roster = manifest.Resolve(selector) ?? throw new InvalidDataException($"Roster selector '{selector}' is not available.");
        var packages = new Dictionary<string, CookedCharacterPackageLoadResult>(StringComparer.Ordinal);
        foreach (var rosterEntry in manifest.Entries)
        {
            var loaded = CookedCharacterPackageLoader.LoadDirectory(
                Path.Combine(cookedRoot, rosterEntry.PackageId), rosterEntry.Requirement);
            if (!loaded.IsValid)
                throw new InvalidDataException(string.Join("; ", loaded.Diagnostics.Select(x => $"{x.Code}:{x.Message}")));
            packages[rosterEntry.PackageId] = loaded;
        }
        var builder = new MatchContentCatalogBuilder();
        var result = shipped == null
            ? builder.Build(manifest, packages, MatchContentPolicy.EditorDevelopment)
            : builder.Build(shipped, packages, MatchContentPolicy.LocalPlay);
        if (!result.IsValid || result.Catalog == null) throw new InvalidDataException(string.Join("; ", result.Diagnostics.Select(x => $"{x.Code}:{x.Message}")));
        return result.Catalog.Resolve(selector) ?? throw new InvalidDataException($"Catalog selector '{selector}' is unavailable.");
    }

    private static string ResolveRoot(string root)
    {
        bool Ready(string basePath)
        {
            string manifestPath = Path.Combine(basePath, root, "roster", "manifest.json");
            if (!File.Exists(manifestPath)) return false;
            try
            {
                var manifest = BuiltInRosterManifestCodec.Load(manifestPath);
                foreach (var entry in manifest.Entries)
                    if (!File.Exists(Path.Combine(basePath, root, entry.PackageId, "manifest.json")))
                        return false;
                return true;
            }
            catch { return false; }
        }

        if (Path.IsPathRooted(root) && Ready("")) return root;
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (int i = 0; i < 10 && current != null; i++, current = current.Parent)
            if (Ready(current.FullName))
                return Path.Combine(current.FullName, root);
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 10 && directory != null; i++, directory = directory.Parent)
            if (Ready(directory.FullName))
                return Path.Combine(directory.FullName, root);
        return root;
    }
}
