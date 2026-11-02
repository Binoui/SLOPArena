using System.Security.Cryptography;
using System.Text.Json;
using SlopArena.Shared;

namespace SlopArena.ComboFinder;

sealed class Content
{
    internal readonly ArenaDefinition Arena;
    readonly MatchContentCatalog catalog;
    readonly string stageHash;
    readonly Dictionary<string, string> hashes;
    internal static string FindRoot()
    {
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "content-cooked/roster/manifest.json"))) return d.FullName;
        throw new ArgumentException("Cannot locate repository content");
    }
    internal Content(string stage)
    {
        if (Path.GetFileName(stage) != stage) throw new ArgumentException("Invalid stage name");
        string root = FindRoot(), arenaPath = Path.Combine(root, "data/arenas", stage + ".arena");
        Arena = ArenaBinaryFormat.LoadFromFile(arenaPath) ?? throw new ArgumentException("Invalid arena binary");
        stageHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(arenaPath))).ToLowerInvariant();
        string manifestPath = Path.Combine(root, "content-cooked/roster/manifest.json");
        var manifest = BuiltInRosterManifestCodec.Load(manifestPath);
        var packages = manifest.Entries.ToDictionary(e => e.PackageId, e => CookedCharacterPackageLoader.LoadDirectory(
            Path.Combine(root, "content-cooked", e.PackageId), e.Requirement));
        var built = new MatchContentCatalogBuilder().Build(manifest, packages);
        catalog = built.Catalog ?? throw new ArgumentException(string.Join("; ", built.Diagnostics.Select(d => d.Message)));
        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        hashes = doc.RootElement.GetProperty("entries").EnumerateArray().ToDictionary(
            e => e.GetProperty("packageId").GetString()!, e => e.GetProperty("requirement").GetProperty("cookedContentHash").GetString()!);
    }
    internal MatchContentEntry Resolve(string package)
    {
        if (package is not ("bonk" or "fightguy" or "manki" or "wibou"))
            throw new ArgumentException($"Package {package} is outside the approved four-fighter roster");
        return catalog.ResolvePackage(package) ?? throw new ArgumentException($"Missing package {package}");
    }
    internal ContentStamp Stamp(IEnumerable<FighterTape> fighters) => new(SimulationProtocol.Version, stageHash,
        fighters.Select(f => f.Package).Distinct().Order(StringComparer.Ordinal).ToDictionary(p => p, p => hashes[p]));
    internal FighterTape[] Fighters(SearchOptions o)
    {
        var floor = Arena.Heightmap;
        var spawns = Arena.SpawnPoints.Where(p => floor.Sample(p.X, p.Z) != float.MinValue).Take(2).ToArray();
        if (spawns.Length < 2) throw new ArgumentException("Arena needs two spawn points with floor");
        var a = o.AttackerPosition ?? new[] { spawns[0].X, spawns[0].Z };
        var v = o.VictimPosition ?? new[] { spawns[1].X, spawns[1].Z };
        float yaw = MathF.Round(MathF.Atan2(v[0] - a[0], v[1] - a[1]) * 180f / MathF.PI, 1);
        return new[] { Make(1, "attacker", o.Attacker, a, yaw, 0), Make(2, "victim", o.Victim, v, yaw + 180f, o.Damage) };
        FighterTape Make(int id, string role, string pkg, float[] p, float y, ushort damage)
        {
            Resolve(pkg);
            float feet = floor.Sample(p[0], p[1]);
            if (feet == float.MinValue) throw new ArgumentException($"{role} start has no floor");
            return new(id, role, pkg, new[] { p[0], feet, p[1] }, MathF.Round(y, 1), damage, "");
        }
    }
}
