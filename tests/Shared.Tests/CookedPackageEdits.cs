using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Copies a shipped cooked package, applies one edit, and re-hashes the manifest so the loader's
/// hash check passes and its content validation is what a test exercises.
/// </summary>
public static class CookedPackageEdits
{
    public static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    public static readonly BuiltInRosterManifest Roster =
        BuiltInRosterManifestCodec.Load(Path.Combine(Root, "content-cooked/roster/manifest.json"));

    private static readonly string[] FilePaths =
    {
        CharacterPackageAssembler.ManifestPath, CharacterPackageAssembler.RuntimePath,
        CharacterPackageAssembler.PosePath, CharacterPackageAssembler.BindingPath,
    };

    public static Dictionary<string, byte[]> Shipped(string packageId) => FilePaths.ToDictionary(
        path => path, path => File.ReadAllBytes(Path.Combine(Root, "content-cooked", packageId, path)), StringComparer.Ordinal);

    public static Dictionary<string, byte[]> Edit(string packageId, Action<PackageDraft> edit)
    {
        var draft = new PackageDraft(Shipped(packageId));
        edit(draft);
        return draft.Finish();
    }

    public static MatchContentPackageRequirement RequirementOf(IReadOnlyDictionary<string, byte[]> files)
    {
        var manifest = JsonNode.Parse(files[CharacterPackageAssembler.ManifestPath])!;
        return new MatchContentPackageRequirement(
            manifest["packageId"]!.GetValue<string>(), manifest["version"]!.GetValue<string>(),
            manifest["cookedContentHash"]!.GetValue<string>(), manifest["packageHash"]!.GetValue<string>());
    }

    public sealed class PackageDraft
    {
        private readonly Dictionary<string, byte[]> _files;
        private readonly Dictionary<string, JsonObject> _parsed = new(StringComparer.Ordinal);

        internal PackageDraft(Dictionary<string, byte[]> files) => _files = files;

        public JsonObject Runtime => Json(CharacterPackageAssembler.RuntimePath);
        public JsonObject Manifest => Json(CharacterPackageAssembler.ManifestPath);
        public JsonObject Bindings => Json(CharacterPackageAssembler.BindingPath);

        public byte[] Poses
        {
            get => _files[CharacterPackageAssembler.PosePath];
            set => _files[CharacterPackageAssembler.PosePath] = value;
        }

        public JsonObject Slot(string id) => Runtime["character"]!["slots"]!.AsArray()
            .Single(slot => slot!["id"]!.GetValue<string>() == id)!.AsObject();

        private JsonObject Json(string path)
        {
            if (!_parsed.TryGetValue(path, out var node))
                _parsed[path] = node = JsonNode.Parse(_files[path])!.AsObject();
            return node;
        }

        internal Dictionary<string, byte[]> Finish()
        {
            foreach (var (path, node) in _parsed)
                _files[path] = Encoding.UTF8.GetBytes(node.ToJsonString());
            _files[CharacterPackageAssembler.ManifestPath] = CharacterPackageAssembler.RehashManifest(_files);
            return _files;
        }
    }

    /// <summary>Byte offsets inside poses.bin: the first animation's frame-count field and its first sample.</summary>
    public static (int FrameCountOffset, int FirstSampleOffset) PoseOffsets(byte[] poses)
    {
        int offset = 8;
        uint bones = BinaryPrimitives.ReadUInt32LittleEndian(poses.AsSpan(offset)); offset += 8;
        for (uint i = 0; i < bones; i++)
            offset += 4 + (int)BinaryPrimitives.ReadUInt32LittleEndian(poses.AsSpan(offset));
        offset += 4 + (int)BinaryPrimitives.ReadUInt32LittleEndian(poses.AsSpan(offset));
        return (offset, offset + 4);
    }
}
