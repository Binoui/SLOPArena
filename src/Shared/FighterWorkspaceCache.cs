using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace SlopArena.Shared;

public sealed record FighterWorkspaceCookResult(LocalFighterItemResult? Admission, IReadOnlyList<CharacterDiagnostic> Diagnostics, string? Folder);

/// <summary>Each workspace has a serialized publisher and immutable, atomically installed revisions.</summary>
public static class FighterWorkspaceCache
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, int> Pins = new(StringComparer.Ordinal);
    public static IDisposable Pin(string folder)
    {
        if (!Directory.Exists(folder)) throw new IOException("The cooked revision was superseded. Press Cook again before Training.");
        lock (Gate) Pins[folder] = Pins.TryGetValue(folder, out int count) ? count + 1 : 1;
        return new PinLease(folder);
    }
    private sealed class PinLease : IDisposable
    {
        private string? folder;
        internal PinLease(string path) => folder = path;
        public void Dispose()
        {
            lock (Gate)
            {
                if (folder == null) return;
                if (--Pins[folder] == 0) Pins.Remove(folder);
                string parent = Path.GetDirectoryName(folder)!; folder = null;
                if (Directory.Exists(parent)) Prune(parent);
            }
        }
    }
    private static void Prune(string root)
    {
        string latest = File.Exists(Path.Combine(root, "latest.txt")) ? File.ReadAllText(Path.Combine(root, "latest.txt")) : "";
        foreach (string directory in Directory.GetDirectories(root).Where(p => Path.GetFileName(p) != latest && !Path.GetFileName(p).StartsWith(".pending-", StringComparison.Ordinal)))
            if (!Pins.ContainsKey(directory)) Directory.Delete(directory, true);
    }
    public static FighterWorkspaceCookResult Cook(string cacheRoot, string sourceFolder, LocalFighterWorkspace snapshot, CancellationToken cancel = default)
    {
        string root = Path.Combine(cacheRoot, WorkshopItemManifestCodec.HashPayload(Encoding.UTF8.GetBytes(Path.GetFullPath(sourceFolder))));
        string? pending = null; string target = root;
        var cooked = snapshot.Cook(cancel);
        if (cooked.Item == null) return new(null, cooked.Diagnostics, null);
        try
        {
            cancel.ThrowIfCancellationRequested();
            Directory.CreateDirectory(root);
            pending = Path.Combine(root, ".pending-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(pending);
            foreach (var file in cooked.Item) { cancel.ThrowIfCancellationRequested(); target = Path.Combine(pending, file.Key); File.WriteAllBytes(target, file.Value); }
            var admitted = LocalFighterItems.Read(pending);
            var diagnostics = cooked.Diagnostics.Concat(admitted.Diagnostics).ToArray();
            if (admitted.Fighter == null) return new(null, diagnostics, null);
            cancel.ThrowIfCancellationRequested();
            lock (Gate)
            {
                cancel.ThrowIfCancellationRequested();
                using var publishLock = new FileStream(Path.Combine(root, ".publish.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                string name = "revision-" + Guid.NewGuid().ToString("N"), final = Path.Combine(root, name);
                Directory.Move(pending, final); pending = null;
                string pointer = Path.Combine(root, ".latest-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(pointer, name);
                string latest = Path.Combine(root, "latest.txt");
                if (File.Exists(latest)) File.Replace(pointer, latest, null); else File.Move(pointer, latest);
                foreach (string legacy in Directory.GetDirectories(cacheRoot))
                    if (File.Exists(Path.Combine(legacy, "item.json")) && !Pins.ContainsKey(legacy)) Directory.Delete(legacy, true);
                Prune(root);
                return new(admitted, diagnostics, final);
            }
        }
        catch (Exception e) when (LocalFighterWorkspace.BoundaryFailure(e))
        { return new(null, new[] { FighterCookDocument.Error("workspace.output", target, "Cannot publish the cooked item: " + e.Message + " Fix the folder permissions or free disk space, then press Cook again.") }, null); }
        finally { if (pending != null && Directory.Exists(pending))
            try { Directory.Delete(pending, true); } catch (Exception e) when (LocalFighterWorkspace.BoundaryFailure(e)) { System.Diagnostics.Trace.TraceWarning("workspace.cleanup " + pending + ": " + e.Message); } }
    }
}
