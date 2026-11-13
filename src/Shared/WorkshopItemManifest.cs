using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SlopArena.Shared;

public sealed class WorkshopItemManifest
{
    public int SchemaVersion { get; }
    public string ItemType { get; }
    public string ItemVersion { get; }
    public IReadOnlyDictionary<string, string> Payloads { get; }
    public string Creator { get; }
    public string License { get; }
    public string Attribution { get; }
    public bool AiAssisted { get; }
    public string BaseFighterPackageId { get; }

    internal WorkshopItemManifest(int schema, string type, string version, Dictionary<string, string> payloads,
        string creator, string license, string attribution, bool aiAssisted, string baseId)
    {
        SchemaVersion = schema; ItemType = type; ItemVersion = version;
        Payloads = new ReadOnlyDictionary<string, string>(payloads);
        Creator = creator; License = license; Attribution = attribution;
        AiAssisted = aiAssisted; BaseFighterPackageId = baseId;
    }
}

public sealed record WorkshopItemManifestReadResult(WorkshopItemManifest? Manifest, IReadOnlyList<CharacterDiagnostic> Diagnostics)
{
    public bool IsAccepted => Manifest != null && Diagnostics.Count == 0;
}

public static class WorkshopItemManifestCodec
{
    public const int SchemaVersion = 1;
    public const int MaxManifestBytes = 16384;
    public const int MaxTextLength = 1024;
    public const int SteamMetadataByteLimit = 5000;
    public const int FighterSchemaVersion = 2;
    internal static readonly string[] FighterRequiredFiles = { "manifest.json", "character.runtime.json", "poses.bin", "client.bindings", "package.json", "character.json", "card.json" };
    private static readonly string[] Fields = { "schemaVersion", "itemType", "itemVersion", "payloads", "creator", "license", "attribution", "aiAssisted", "baseFighterPackageId" };

    public static WorkshopItemManifestReadResult Read(ReadOnlySpan<byte> bytes)
    {
        var d = new List<CharacterDiagnostic>();
        WorkshopItemManifestReadResult Fail(string code, string path, string message)
        { d.Add(Error(code, path, message)); return new(null, d.AsReadOnly()); }
        if (bytes.Length > MaxManifestBytes) return Fail("item.size", "item.json", $"Manifest limit {MaxManifestBytes} bytes; measured {bytes.Length}.");
        try
        {
            using var doc = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 8 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Fail("item.type", "item.json", "Expected object.");
            bool fighter = root.TryGetProperty("schemaVersion", out var sv) && sv.ValueKind == JsonValueKind.Number && sv.TryGetInt32(out var sn) && sn == FighterSchemaVersion
                && root.TryGetProperty("itemType", out var it) && it.ValueKind == JsonValueKind.String && it.GetString() == "fighter";
            var fields = fighter ? Fields.Where(x => x != "baseFighterPackageId").ToArray() : Fields;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in root.EnumerateObject())
            {
                if (!seen.Add(p.Name)) d.Add(Error("item.field.duplicate", p.Name, "Duplicate field."));
                if (!fields.Contains(p.Name, StringComparer.Ordinal)) d.Add(Error("item.field.unknown", p.Name, "Unknown field."));
            }
            if (root.TryGetProperty("itemType", out var kind) && kind.ValueKind == JsonValueKind.String && kind.GetString() != "model-swap" && !fighter)
                d.Add(Error("item.kind.unsupported", "itemType", $"Item type '{kind.GetString()}' is not supported yet."));
            foreach (var f in fields) if (!seen.Contains(f)) d.Add(Error("item.field.missing", f, "Required field missing."));
            if (d.Count != 0) return new(null, d.AsReadOnly());
            string Text(string name)
            {
                var v = root.GetProperty(name);
                if (v.ValueKind != JsonValueKind.String) { d.Add(Error("item.type", name, "Expected string.")); return ""; }
                var s = v.GetString()!;
                if (s.Length > MaxTextLength) d.Add(Error("item.text.length", name, $"Text limit {MaxTextLength}; measured {s.Length}."));
                return s;
            }
            var schema = root.GetProperty("schemaVersion");
            int version = 0;
            if (schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out version)) d.Add(Error("item.type", "schemaVersion", "Expected integer."));
            else if (version != (fighter ? FighterSchemaVersion : SchemaVersion)) d.Add(Error("item.schema.unsupported", "schemaVersion", $"Unsupported schema {version}; expected {SchemaVersion}."));
            var type = Text("itemType");
            var itemVersion = Text("itemVersion");
            if (string.IsNullOrWhiteSpace(itemVersion)) d.Add(Error("item.version.invalid", "itemVersion", "Item version must not be empty."));
            var creator = Text("creator"); var license = Text("license"); var attribution = Text("attribution"); var baseId = fighter ? "" : Text("baseFighterPackageId");
            if (!fighter && !MatchContentCatalogBuilder.IsStablePackageId(baseId)) d.Add(Error("item.base.invalid", "baseFighterPackageId", "Expected stable package ID."));
            var ai = root.GetProperty("aiAssisted");
            if (ai.ValueKind != JsonValueKind.True && ai.ValueKind != JsonValueKind.False) d.Add(Error("item.type", "aiAssisted", "Expected boolean."));
            var payloads = new Dictionary<string, string>(StringComparer.Ordinal);
            var map = root.GetProperty("payloads");
            var payloadNames = new HashSet<string>(StringComparer.Ordinal);
            if (map.ValueKind != JsonValueKind.Object) d.Add(Error("item.type", "payloads", "Expected hash map object."));
            else foreach (var p in map.EnumerateObject())
            {
                if (!payloadNames.Add(p.Name)) d.Add(Error("item.field.duplicate", "payloads." + p.Name, "Duplicate payload field."));
                if (!PlainFileName(p.Name)) d.Add(Error("item.payload.name", p.Name, "Payload must be a plain file name without '..' or separators."));
                if (p.Value.ValueKind != JsonValueKind.String) { d.Add(Error("item.type", p.Name, "Expected SHA-256 string.")); continue; }
                var hash = p.Value.GetString()!;
                if (!MatchContentCatalogBuilder.IsSha(hash)) d.Add(Error("item.payload.hash", p.Name, "Expected 64 lowercase hexadecimal SHA-256 characters."));
                payloads[p.Name] = hash;
            }
            if (payloads.Keys.Count(x => x.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)) != 1 ||
                payloads.Keys.Any(x => !x.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) && x != "humanoid.json"
                    && !(fighter && (FighterRequiredFiles.Contains(x) || x.EndsWith(".png", StringComparison.Ordinal) || x.EndsWith(".jpg", StringComparison.Ordinal)))))
                d.Add(Error("item.payload.shape", "payloads", "Model swap requires one .glb and optionally humanoid.json."));
            if (fighter && (FighterRequiredFiles.Any(x => !payloads.ContainsKey(x)) || payloads.Count > 40))
                d.Add(Error("item.payload.shape", "payloads", "Fighter requires seven canonical files, one GLB, and at most 40 payloads."));
            return new(d.Count == 0 ? new WorkshopItemManifest(version, type, itemVersion, payloads, creator, license, attribution, ai.ValueKind == JsonValueKind.True, baseId) : null, d.AsReadOnly());
        }
        catch (JsonException ex) { return Fail("item.json", "item.json", ex.Message); }
        catch (FormatException ex) { return Fail("item.json", "item.json", ex.Message); }
        catch (InvalidOperationException ex) { return Fail("item.json", "item.json", ex.Message); }
    }

    public static byte[] Write(WorkshopItemManifest manifest)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject(); w.WriteNumber("schemaVersion", manifest.SchemaVersion);
            w.WriteString("itemType", manifest.ItemType); w.WriteString("itemVersion", manifest.ItemVersion);
            w.WritePropertyName("payloads"); WriteHashes(w, manifest);
            w.WriteString("creator", manifest.Creator); w.WriteString("license", manifest.License); w.WriteString("attribution", manifest.Attribution);
            w.WriteBoolean("aiAssisted", manifest.AiAssisted); if (manifest.ItemType == "model-swap") w.WriteString("baseFighterPackageId", manifest.BaseFighterPackageId); w.WriteEndObject();
        }
        return stream.ToArray();
    }

    public static bool TryWriteSteamMetadata(WorkshopItemManifest manifest, out string metadata, out CharacterDiagnostic? diagnostic)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream)) WriteHashes(w, manifest);
        if (stream.Length >= SteamMetadataByteLimit)
        { metadata = ""; diagnostic = Error("item.metadata.size", "payloads", $"Steam metadata must be below {SteamMetadataByteLimit} bytes; measured {stream.Length}."); return false; }
        metadata = Encoding.UTF8.GetString(stream.ToArray()); diagnostic = null; return true;
    }

    public static IReadOnlyList<CharacterDiagnostic> Verify(WorkshopItemManifest manifest, IReadOnlyDictionary<string, byte[]> files)
    {
        var d = new List<CharacterDiagnostic>();
        foreach (var p in manifest.Payloads)
            if (!files.TryGetValue(p.Key, out var bytes)) d.Add(Error("item.payload.missing", p.Key, "Payload file missing."));
            else if (HashPayload(bytes) != p.Value) d.Add(Error("item.payload.mismatch", p.Key, "Payload SHA-256 mismatch."));
        foreach (var file in files.Keys) if (!manifest.Payloads.ContainsKey(file)) d.Add(Error("item.payload.extra", file, "Unexpected payload file."));
        return d.AsReadOnly();
    }

    public static string HashPayload(byte[] bytes)
    { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    private static void WriteHashes(Utf8JsonWriter w, WorkshopItemManifest m)
    { w.WriteStartObject(); foreach (var p in m.Payloads.OrderBy(x => x.Key, StringComparer.Ordinal)) w.WriteString(p.Key, p.Value); w.WriteEndObject(); }
    internal static bool PlainFileName(string name) => name.Length > 0 && name.Length <= MaxTextLength && !name.Contains("..") && !name.Any(c => c == '/' || c == '\\' || c == ':' || char.IsControl(c));
    private static CharacterDiagnostic Error(string code, string path, string message) => MatchContentCatalogBuilder.Error(code, path, message);
}
