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

    private static readonly Cache Application = new(() => LocateApplication(
        typeof(ShippedRoster).Assembly.Location, AppContext.BaseDirectory,
        System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name == "SlopArena.Server"));

    public static ShippedRoster ReadApplication() => Application.Read();

    public static bool TryReadApplication(out ShippedRoster? roster, out string? diagnostic)
    {
        roster = null;
        diagnostic = null;
        try { roster = ReadApplication(); return true; }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        { diagnostic = ex.Message; return false; }
    }

    public BuiltInRosterEntry DefaultFighter => Manifest.Entries.FirstOrDefault(entry => entry.PackageId == "fightguy") ?? Manifest.Entries[0];

    internal static (string Root, bool Watch) LocateApplication(string assemblyLocation, string baseDirectory, bool server)
    {
        if (server) return (Path.Combine(baseDirectory, "content-cooked"), false);
        if (string.IsNullOrWhiteSpace(assemblyLocation) && string.IsNullOrWhiteSpace(baseDirectory))
            throw new InvalidDataException("content.application.location-empty: Shared assembly location and application base directory are empty.");
        var directory = new DirectoryInfo(string.IsNullOrWhiteSpace(assemblyLocation)
            ? baseDirectory : Path.GetDirectoryName(Path.GetFullPath(assemblyLocation))!);
        for (; directory != null; directory = directory.Parent)
        {
            if (directory.Name.EndsWith("_Data", StringComparison.Ordinal))
                return (Path.Combine(directory.FullName, "StreamingAssets/content-cooked"), false);
            if (File.Exists(Path.Combine(directory.FullName, "SlopArena.sln")))
                return (Path.Combine(directory.FullName, "content-cooked"), true);
        }
        throw new InvalidDataException("content.application.root-missing: No Unity *_Data installation or SlopArena.sln was found for Shared. "
            + (string.IsNullOrWhiteSpace(assemblyLocation) ? "Assembly.Location is empty; searched from the application base directory." : "Searched from " + assemblyLocation));
    }

    internal sealed class Cache
    {
        private readonly Func<(string Root, bool Watch)> locate;
        private readonly object gate = new();
        private (string Root, bool Watch)? source;
        private ShippedRoster? roster;
        private string? diagnostic;
        private bool loaded;
        private (DateTime Written, long Length) stamp;

        internal Cache(Func<(string Root, bool Watch)> locate) => this.locate = locate;

        internal ShippedRoster Read()
        {
            lock (gate)
            {
                if (source == null)
                {
                    if (loaded) throw new InvalidDataException(diagnostic);
                    try { source = locate(); }
                    catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
                    { loaded = true; diagnostic = ex.Message; throw new InvalidDataException(diagnostic, ex); }
                }
                var path = Path.Combine(source.Value.Root, "roster/manifest.json");
                var info = new FileInfo(path);
                var current = !source.Value.Watch && loaded ? stamp
                    : info.Exists ? (info.LastWriteTimeUtc, info.Length) : default;
                if (!loaded || current != stamp)
                {
                    stamp = current;
                    loaded = true;
                    roster = null;
                    try { roster = ReadRoot(source.Value.Root); diagnostic = null; }
                    catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
                    { diagnostic = "content.roster.invalid: " + path + ": " + ex.Message; }
                }
                if (roster == null) throw new InvalidDataException(diagnostic);
                return roster;
            }
        }
    }

    private static ShippedRoster ReadRoot(string root)
    {
        var manifest = BuiltInRosterManifestCodec.Load(Path.Combine(root, "roster/manifest.json"));
        var diagnostics = MatchContentCatalogBuilder.ValidateRoster(manifest);
        if (manifest.Entries.Count == 0 || diagnostics.Count != 0)
            throw new InvalidDataException("Application shipped roster is empty or invalid: " + string.Join("; ", diagnostics));
        return new ShippedRoster(root, manifest);
    }

    /// <summary>Only source beside this application's shipped roster can use the first-party cook profile.</summary>
    public bool AuthorizesCookSource(string projectRoot, string packageRoot, string packageId)
    {
        if (Manifest.ResolvePackage(packageId) == null) return false;
        string expectedProject = Path.GetFullPath(Path.Combine(CookedRoot, "../client/Unity"));
        string project = Path.GetFullPath(projectRoot);
        string source = Path.GetFullPath(Path.Combine(project, packageRoot));
        string expected = Path.Combine(expectedProject, "Assets/CharacterPackages", packageId);
        if (!string.Equals(project, expectedProject, StringComparison.Ordinal)
            || !string.Equals(source, expected, StringComparison.Ordinal)) return false;
        foreach (string file in new[] { "package.json", "character.json" })
        {
            var info = new FileInfo(Path.Combine(source, file));
            if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0) return false;
        }
        for (var directory = new DirectoryInfo(source); directory != null; directory = directory.Parent)
        {
            if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0) return false;
            if (string.Equals(directory.FullName, expectedProject, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    internal bool Authorizes(MatchContentIdentity identity)
    {
        var pin = Manifest.ResolvePackage(identity.PackageId)?.Requirement;
        return pin != null && pin.Version == identity.Version && pin.CookedContentHash == identity.CookedContentHash
            && pin.PackageHash == identity.PackageHash;
    }
}
