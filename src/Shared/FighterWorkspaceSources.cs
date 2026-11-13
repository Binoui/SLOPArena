using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SlopArena.Shared;

public sealed record FighterWorkspaceSource(string Key, string? SourceFolder, string? ItemFolder, string? Diagnostic);

public static class FighterWorkspaceSources
{
    public static string DuplicateDiagnostic(string packageId, IEnumerable<string> paths)
    {
        var ordered = paths.OrderBy(p => p, StringComparer.Ordinal).ToArray();
        return packageId + " is used by " + string.Join(" and ", ordered) + ". Rename package.json.packageId in "
            + string.Join(" and ", ordered.Skip(1)) + " to a unique local ID, then cook again.";
    }
    public static IReadOnlyList<FighterWorkspaceSource> Read(string cacheRoot)
    {
        var result = new List<FighterWorkspaceSource>();
        if (!Directory.Exists(cacheRoot)) return result;
        foreach (string cache in Directory.GetDirectories(cacheRoot))
        {
            string? source = null;
            try
            {
                string record = Path.Combine(cache, "source.txt");
                if (!File.Exists(record))
                {
                    result.Add(new(cache, null, null, "This older cook has no workspace path. Cook the workspace again to list it."));
                    continue;
                }
                source = Encoding.UTF8.GetString(WorkshopRead(record, 32768));
                if (!Path.IsPathRooted(source)) throw new InvalidDataException("Workspace path must be absolute.");
                string key = Path.GetFullPath(source);
                if (!Directory.Exists(key))
                { result.Add(new(cache, key, null, "Workspace moved or deleted. Cook its current folder to list it again.")); continue; }
                string name = Encoding.UTF8.GetString(WorkshopRead(Path.Combine(cache, "latest.txt"), 256));
                if (!name.StartsWith("revision-", StringComparison.Ordinal) || Path.GetFileName(name) != name) throw new InvalidDataException("Invalid latest revision pointer.");
                result.Add(new(cache, key, Path.Combine(cache, name), null));
            }
            catch (Exception e) when (LocalFighterWorkspace.BoundaryFailure(e)) { result.Add(new(cache, source, null, e.Message)); }
        }
        return result;
    }
    static byte[] WorkshopRead(string file, int limit)
    { long total = 0; return PortableItemFiles.Read(file, limit, ref total); }
}
