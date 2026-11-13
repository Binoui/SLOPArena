using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SlopArena.Shared;

/// <summary>A registered shared clip: the library GLB's skeleton and the clip in it.</summary>
public sealed class SharedClip
{
    public GlbMotionRig Rig { get; }
    public GlbClip Clip { get; }
    internal SharedClip(GlbMotionRig rig, GlbClip clip) { Rig = rig; Clip = clip; }
}

/// <summary>
/// The engine's shared motion: <c>SharedClips/registry.json</c> rows naming an exact <c>slop.anim.*</c> ID and version, a
/// library GLB beside it in <c>SharedClips/</c> with its SHA-256, and the clip name. Adding a clip is a row and a file;
/// both build into Shared, so the client and the GameServer resolve the same bytes. The table is empty until the owner's
/// clip inventory says which clips may ship as baked joint positions.
/// </summary>
public sealed class SharedClipRegistry
{
    private sealed record Row(string File, string ClipName, string Sha256);

    private readonly Dictionary<(string Id, string Version), Row> rows = new();
    private readonly Func<string, byte[]?> files;
    private readonly Dictionary<string, GlbMotionRig> rigs = new(StringComparer.Ordinal);

    private static SharedClipRegistry? embedded;
    private static readonly System.Threading.AsyncLocal<SharedClipRegistry?> scoped = new();
    public static SharedClipRegistry Default => scoped.Value ?? (embedded ??= Parse(Resource("registry.json") ?? throw new InvalidDataException("Shared clip table is missing."), Resource));

    /// <summary>Tests resolve against another table inside the returned scope; the shipped table is unchanged.</summary>
    internal static IDisposable Use(SharedClipRegistry registry)
    {
        var previous = scoped.Value;
        scoped.Value = registry;
        return new Restore(() => scoped.Value = previous);
    }

    private sealed class Restore : IDisposable
    {
        private readonly Action undo;
        public Restore(Action undo) { this.undo = undo; }
        public void Dispose() => undo();
    }

    private SharedClipRegistry(Func<string, byte[]?> files) { this.files = files; }

    internal static SharedClipRegistry Parse(byte[] table, Func<string, byte[]?> files)
    {
        var registry = new SharedClipRegistry(files);
        using var document = JsonDocument.Parse(table);
        var root = document.RootElement;
        PortableFighterBindings.Object(root, "schemaVersion", "clips");
        if (root.GetProperty("schemaVersion").GetInt32() != 1) throw new InvalidDataException("Shared clip table schema must be 1.");
        foreach (var clip in root.GetProperty("clips").EnumerateArray())
        {
            PortableFighterBindings.Object(clip, "id", "version", "file", "clipName", "sha256");
            using var reference = JsonDocument.Parse(JsonSerializer.Serialize(new { id = clip.GetProperty("id").GetString(), version = clip.GetProperty("version").GetString() }));
            PortableRegistryReference.Validate(reference.RootElement, "anim");
            var key = (clip.GetProperty("id").GetString()!, clip.GetProperty("version").GetString()!);
            if (!registry.rows.TryAdd(key, new Row(clip.GetProperty("file").GetString()!, clip.GetProperty("clipName").GetString()!, clip.GetProperty("sha256").GetString()!)))
                throw new InvalidDataException($"Shared clip {key.Item1} {key.Item2} is listed twice.");
        }
        return registry;
    }

    public bool Contains(string id, string version) => rows.ContainsKey((id, version));

    /// <summary>The exact ID and version, never a nearest version; false when it is not registered.</summary>
    public bool TryResolve(string id, string version, out SharedClip? clip)
    {
        clip = null;
        if (!rows.TryGetValue((id, version), out var row)) return false;
        if (!rigs.TryGetValue(row.File, out var rig))
        {
            byte[] bytes = files(row.File) ?? throw new InvalidDataException($"Shared clip file {row.File} is missing.");
            if (WorkshopItemManifestCodec.HashPayload(bytes) != row.Sha256) throw new InvalidDataException($"Shared clip file {row.File} has the wrong SHA-256.");
            rigs[row.File] = rig = GlbMotionRig.Read(bytes, null);
        }
        clip = new SharedClip(rig, rig.Clip(row.ClipName));
        return true;
    }

    private static byte[]? Resource(string file)
    {
        using var stream = typeof(SharedClipRegistry).Assembly.GetManifestResourceStream("SlopArena.Shared.SharedClips." + file);
        if (stream == null) return null;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
