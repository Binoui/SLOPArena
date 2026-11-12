using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SlopArena.Shared;

namespace SlopArena.Server;

/// <summary>Builds an immutable content catalog for each match assignment.</summary>
public sealed class MatchContentCatalogProvider
{
    private readonly string? _cookedRoot;
    private readonly string? _manifestPath;

    public MatchContentCatalogProvider() { }

    internal MatchContentCatalogProvider(string cookedRoot, string manifestPath)
    {
        _cookedRoot = cookedRoot ?? throw new ArgumentNullException(nameof(cookedRoot));
        _manifestPath = manifestPath ?? throw new ArgumentNullException(nameof(manifestPath));
    }

    public bool TryBuild(out MatchContentCatalog? catalog, out MatchContentHandleMap? handleMap, out string? error)
    {
        catalog = null; handleMap = null; error = null;
        try
        {
            var shipped = ShippedRoster.ReadApplication();
            var manifest = _manifestPath == null ? shipped.Manifest : BuiltInRosterManifestCodec.Load(_manifestPath);
            if (manifest.Entries.Count == 0 || manifest.Entries.Count != shipped.Manifest.Entries.Count
                || manifest.Entries.Any(e => shipped.Manifest.Resolve(e.Selector) != e))
                throw new InvalidDataException("Server roster must contain the complete application shipped roster.");
            var packages = new Dictionary<string, CookedCharacterPackageLoadResult>(StringComparer.Ordinal);
            foreach (var rosterEntry in manifest.Entries)
            {
                string directory = Path.Combine(_cookedRoot ?? shipped.CookedRoot, rosterEntry.PackageId);
                packages[rosterEntry.PackageId] = CookedCharacterPackageLoader.LoadDirectory(directory, rosterEntry.Requirement);
            }
            var result = new MatchContentCatalogBuilder().Build(shipped, packages);
            if (!result.IsValid || result.Catalog == null)
            {
                error = string.Join("; ", result.Diagnostics);
                return false;
            }
            catalog = result.Catalog;
            var records = new List<MatchContentHandleRecord>();
            foreach (var entry in catalog.Entries)
                if (entry.LegacySelector.HasValue)
                    records.Add(new MatchContentHandleRecord(entry.Handle, entry.LegacySelector.Value, entry.Identity, entry.DisplayName));
            handleMap = new MatchContentHandleMap(MatchContentHandleMap.CurrentSchemaVersion, records);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
