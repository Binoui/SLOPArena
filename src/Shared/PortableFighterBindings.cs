using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SlopArena.Shared;

/// <summary>Portable schema 3. All file roles resolve through the pinned payload map.</summary>
internal static class PortableFighterBindings
{
    internal static void Validate(JsonElement root, List<CharacterDiagnostic> diagnostics,
        IReadOnlyDictionary<string, byte[]>? files = null)
    {
        void Error(string code, string path, string message) => diagnostics.Add(MatchContentCatalogBuilder.Error(code, path, message));
        try
        {
            Object(root, "packageId", "sourceHash", "catalogSchemaVersion", "bindingSchemaVersion", "poseFormat", "poseVersion", "sampleRate",
                "rootRuleVersion", "payloads", "modelFile", "portraitFile", "icons", "animations");
            if (root.GetProperty("bindingSchemaVersion").GetInt32() != 3 || root.GetProperty("catalogSchemaVersion").GetInt32() != 3
                || root.GetProperty("rootRuleVersion").GetInt32() != 1 || root.GetProperty("poseVersion").GetInt32() != 1
                || root.GetProperty("poseFormat").GetString() != "SKEL" || root.GetProperty("sampleRate").GetInt32() != 60
                || !MatchContentCatalogBuilder.IsStablePackageId(Text(root.GetProperty("packageId")))
                || !MatchContentCatalogBuilder.IsSha(Text(root.GetProperty("sourceHash"))))
                Error("portable.metadata", "client.bindings", "Expected schema 3, root rule 1, SKEL 1 at 60 Hz and valid identity.");
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var p in root.GetProperty("payloads").EnumerateObject())
            {
                if (!WorkshopItemManifestCodec.PlainFileName(p.Name) || !MatchContentCatalogBuilder.IsSha(Text(p.Value)) || !hashes.TryAdd(p.Name, Text(p.Value)))
                    Error("portable.payload", p.Name, "Invalid or duplicate portable payload.");
                if (files != null && (!files.TryGetValue(p.Name, out var bytes) || WorkshopItemManifestCodec.HashPayload(bytes) != p.Value.GetString()))
                    Error("portable.payload.mismatch", p.Name, "Portable digest does not match verified item bytes.");
            }
            var used = new HashSet<string>(new[] { "package.json", "character.json", "card.json" }, StringComparer.Ordinal);
            if (hashes.ContainsKey("humanoid.json")) used.Add("humanoid.json");
            string model = Text(root.GetProperty("modelFile"));
            used.Add(model);
            if (!model.EndsWith(".glb", StringComparison.Ordinal)) Error("portable.model", model, "Expected GLB model file.");
            void Image(string file)
            {
                used.Add(file);
                if (!(file.EndsWith(".png", StringComparison.Ordinal) || file.EndsWith(".jpg", StringComparison.Ordinal)))
                    Error("portable.image", file, "Expected PNG or JPEG file.");
            }
            var portrait = root.GetProperty("portraitFile");
            if (portrait.ValueKind != JsonValueKind.Null) Image(Text(portrait));
            var iconIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var icon in root.GetProperty("icons").EnumerateObject())
            {
                if (!iconIds.Add(icon.Name) || string.IsNullOrWhiteSpace(icon.Name)) Error("portable.icon", icon.Name, "Invalid or duplicate icon ID.");
                if (icon.Value.ValueKind != JsonValueKind.Null) Image(Text(icon.Value));
            }
            if (!used.SetEquals(hashes.Keys)) Error("portable.payload.shape", "payloads", "Every external file must have exactly one pinned payload entry and a role.");
            if (files != null && !used.SetEquals(files.Keys.Where(x => CookedPackageLimits.FileBytes(x) == 0)))
                Error("portable.payload.shape", "payloads", "Portable bindings must pin every non-cooked item file.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            var poses = new HashSet<string>(StringComparer.Ordinal);
            foreach (var animation in root.GetProperty("animations").EnumerateArray())
            {
                Object(animation, "semanticId", "poseTrackId", "frameCount", "source");
                string id = Text(animation.GetProperty("semanticId")), pose = Text(animation.GetProperty("poseTrackId"));
                int count = animation.GetProperty("frameCount").GetInt32();
                if (string.IsNullOrWhiteSpace(id) || id.Length > 256 || string.IsNullOrWhiteSpace(pose) || pose.Length > 128
                    || !names.Add(id) || !poses.Add(pose) || count < 1 || count > CookedPackageLimits.PoseFrames)
                    Error("portable.animation", id, "Invalid or duplicate semantic/pose ID or frame count.");
                var source = animation.GetProperty("source");
                string kind = Text(source.GetProperty("kind"));
                if (kind == "glb")
                {
                    Object(source, "kind", "clipName");
                    string clip = Text(source.GetProperty("clipName"));
                    if (string.IsNullOrWhiteSpace(clip) || clip.Length > 256) Error("portable.clip", id, "A GLB clip name is required.");
                    if (files != null && files.TryGetValue(model, out var glb))
                    {
                        int length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(glb.AsSpan(12, 4));
                        using var doc = JsonDocument.Parse(glb.AsMemory(20, length));
                        if (!doc.RootElement.TryGetProperty("animations", out var clips)
                            || clips.EnumerateArray().Count(x => x.TryGetProperty("name", out var n) && n.GetString() == clip) != 1)
                            Error("portable.clip.missing", clip, "GLB must contain exactly one clip with this name.");
                    }
                }
                else if (kind == "shared")
                {
                    Object(source, "kind", "id", "version");
                    Error("portable.shared.unsupported", id, "The shared-clip registry is empty; required clip is unavailable.");
                }
                else Error("portable.source", id, "Expected glb or shared motion source.");
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException or OverflowException or InvalidDataException)
        { Error("portable.schema", "client.bindings", ex.Message); }
    }

    private static string Text(JsonElement value) => value.GetString() ?? throw new InvalidDataException("Expected string.");

    internal static void Object(JsonElement value, params string[] fields)
    {
        var expected = new HashSet<string>(fields, StringComparer.Ordinal);
        foreach (var p in value.EnumerateObject())
            if (!expected.Remove(p.Name)) throw new InvalidDataException("Unknown or duplicate field: " + p.Name);
        if (expected.Count != 0) throw new InvalidDataException("Missing field: " + expected.First());
    }
}
