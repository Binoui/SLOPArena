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
                "rootRuleVersion", "payloads", "modelFile", "portraits", "icons", "animations", "presentations");
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
            var portraits = root.GetProperty("portraits");
            var forms = root.GetProperty("icons");
            if (portraits.GetArrayLength() is < 1 or > 2 || forms.GetArrayLength() != portraits.GetArrayLength())
                Error("portable.forms", "portraits", "One portrait and icon map per form are required.");
            foreach (var portrait in portraits.EnumerateArray())
                if (portrait.ValueKind != JsonValueKind.Null) Image(Text(portrait));
            foreach (var icons in forms.EnumerateArray())
            {
                var iconIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var icon in icons.EnumerateObject())
                {
                    if (!iconIds.Add(icon.Name) || !CanonicalIconIds.Contains(icon.Name)) Error("portable.icon", icon.Name, "Unknown or duplicate canonical icon ID.");
                    if (icon.Value.ValueKind != JsonValueKind.Null) Image(Text(icon.Value));
                }
            }
            var presentationIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var presentation in root.GetProperty("presentations").EnumerateObject())
            {
                if (string.IsNullOrWhiteSpace(presentation.Name) || presentation.Name.Length > 256 || !presentationIds.Add(presentation.Name))
                    throw new InvalidDataException("Invalid or duplicate presentation ID.");
                ValidatePresentation(presentation.Value, presentation.Name, diagnostics);
            }
            if (root.GetProperty("animations").GetArrayLength() > CookedPackageLimits.PoseAnimations)
            { Error("portable.limit", "animations", "Animation binding count limit exceeded."); return; }
            if (!used.SetEquals(hashes.Keys)) Error("portable.payload.shape", "payloads", "Every external file must have exactly one pinned payload entry and a role.");
            if (files != null && !used.SetEquals(files.Keys.Where(x => CookedPackageLimits.FileBytes(x) == 0)))
                Error("portable.payload.shape", "payloads", "Portable bindings must pin every non-cooked item file.");
            var clipsByName = new HashSet<string>(StringComparer.Ordinal);
            if (files != null && files.TryGetValue(model, out var modelBytes))
            {
                int length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(modelBytes.AsSpan(12, 4));
                using var glbDocument = JsonDocument.Parse(modelBytes.AsMemory(20, length));
                if (glbDocument.RootElement.TryGetProperty("animations", out var clipArray))
                    foreach (var clip in clipArray.EnumerateArray())
                        if (clip.TryGetProperty("name", out var name) && !clipsByName.Add(Text(name)))
                            Error("portable.clip.duplicate", Text(name), "GLB clip names must be unique.");
            }
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
                    if (files != null && !clipsByName.Contains(clip))
                        Error("portable.clip.missing", clip, "GLB must contain exactly one clip with this name.");
                }
                else if (kind == "shared")
                {
                    Object(source, "kind", "id", "version");
                    var reference = System.Text.Json.JsonSerializer.SerializeToElement(new { id = Text(source.GetProperty("id")), version = Text(source.GetProperty("version")) });
                    PortableRegistryReference.Validate(reference, "anim");
                    Error("portable.shared.unsupported", id, "The shared-clip registry is empty; required clip is unavailable.");
                }
                else Error("portable.source", id, "Expected glb or shared motion source.");
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException or OverflowException or InvalidDataException)
        { Error("portable.schema", "client.bindings", ex.Message); }
    }

    private static readonly string[] CanonicalIconIds = { "icon.1", "icon.2", "icon.3", "icon.a", "icon.e", "icon.r", "icon.f" };

    private static void ValidatePresentation(JsonElement value, string id, List<CharacterDiagnostic> d)
    {
        string kind = Text(value.GetProperty("kind"));
        if (kind == "none") { Object(value, "kind"); return; }
        if (kind != "engine") throw new InvalidDataException("Expected none or engine presentation.");
        Object(value, "kind", "vfx", "audio", "projectile");
        bool any = false;
        foreach (string role in new[] { "vfx", "projectile" })
            if (value.GetProperty(role).ValueKind != JsonValueKind.Null)
            { any = true; PortableRegistryReference.Validate(value.GetProperty(role), "vfx"); }
        var audio = value.GetProperty("audio");
        if (audio.ValueKind != JsonValueKind.Null)
        {
            any = true; Object(audio, "selection", "clips");
            string selection = Text(audio.GetProperty("selection"));
            int count = audio.GetProperty("clips").GetArrayLength();
            if (selection is not ("fixed" or "cycle") || count is < 1 or > 16 || selection == "fixed" && count != 1)
                throw new InvalidDataException("Audio pools require one fixed or one to sixteen cycled clips.");
            foreach (var reference in audio.GetProperty("clips").EnumerateArray()) PortableRegistryReference.Validate(reference, "sfx");
        }
        if (!any) throw new InvalidDataException("An empty engine presentation must be explicit none.");
        d.Add(MatchContentCatalogBuilder.Error("portable.presentation.unsupported", id, "Required presentation registry is empty."));
    }

    internal static void ValidateRuntime(JsonElement bindings, JsonElement runtime, List<CharacterDiagnostic> d)
    {
        try
        {
            var character = runtime.GetProperty("character");
            var forms = new List<JsonElement> { character };
            if (character.TryGetProperty("alternateForm", out var alternate)) forms.Add(alternate);
            if (bindings.GetProperty("portraits").GetArrayLength() != forms.Count || bindings.GetProperty("icons").GetArrayLength() != forms.Count)
                throw new InvalidDataException("Portraits and icons must match the runtime form count.");
            for (int form = 0; form < forms.Count; form++)
            {
                var expected = new HashSet<string>(forms[form].GetProperty("slots").EnumerateArray()
                    .Select(slot => Text(slot.GetProperty("iconId"))).Where(x => x.Length != 0), StringComparer.Ordinal);
                if (expected.Any(x => !CanonicalIconIds.Contains(x))) throw new InvalidDataException("Runtime icon IDs must be canonical for portable fighters.");
                foreach (var icon in bindings.GetProperty("icons")[form].EnumerateObject())
                    if (!expected.Remove(icon.Name)) throw new InvalidDataException("Unknown or duplicate icon for form: " + icon.Name);
                if (expected.Count != 0) throw new InvalidDataException("Every slot icon requires an image or explicit null.");
            }
            var declared = new HashSet<string>(character.GetProperty("presentationIds").EnumerateArray().Select(Text), StringComparer.Ordinal);
            foreach (var entry in bindings.GetProperty("presentations").EnumerateObject())
                if (!declared.Remove(entry.Name)) throw new InvalidDataException("Unknown or duplicate presentation ID: " + entry.Name);
            if (declared.Count != 0) throw new InvalidDataException("Every declared presentation requires a binding or explicit none.");
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or KeyNotFoundException)
        { d.Add(MatchContentCatalogBuilder.Error("portable.runtime", "client.bindings", ex.Message)); }
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
