using System;
using System.IO;
using System.Linq;

namespace SlopArena.Shared;

/// <summary>Authority read from the application installation, never from a caller's manifest DTO.</summary>
public sealed class ShippedRoster
{
    public BuiltInRosterManifest Manifest { get; }
    public string CookedRoot { get; }

    private ShippedRoster(string root, BuiltInRosterManifest manifest)
    {
        CookedRoot = root;
        Manifest = manifest;
    }

    public static ShippedRoster ReadApplication()
    {
        if (System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name == "SlopArena.Server")
            return ReadRoot(Path.Combine(AppContext.BaseDirectory, "content-cooked"));
        var directory = new DirectoryInfo(Path.GetDirectoryName(typeof(ShippedRoster).Assembly.Location) ?? AppContext.BaseDirectory);
        for (; directory != null; directory = directory.Parent)
        {
            if (directory.Name.EndsWith("_Data", StringComparison.Ordinal))
                return ReadRoot(Path.Combine(directory.FullName, "StreamingAssets/content-cooked"));
            if (File.Exists(Path.Combine(directory.FullName, "SlopArena.sln")))
                return ReadRoot(Path.Combine(directory.FullName, "content-cooked"));
        }
        throw new FileNotFoundException("Application shipped roster installation could not be located beside the Shared assembly.");
    }

    private static ShippedRoster ReadRoot(string root)
    {
        var manifest = BuiltInRosterManifestCodec.Load(Path.Combine(root, "roster/manifest.json"));
        var diagnostics = MatchContentCatalogBuilder.ValidateRoster(manifest);
        if (manifest.Entries.Count == 0 || diagnostics.Count != 0)
            throw new InvalidDataException("Application shipped roster is empty or invalid: " + string.Join("; ", diagnostics));
        return new ShippedRoster(root, manifest);
    }

    internal bool Authorizes(MatchContentIdentity identity)
    {
        var pin = Manifest.ResolvePackage(identity.PackageId)?.Requirement;
        return pin != null && pin.Version == identity.Version && pin.CookedContentHash == identity.CookedContentHash
            && pin.PackageHash == identity.PackageHash;
    }
}
