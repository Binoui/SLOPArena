using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace SlopArena.Shared;

internal sealed record FighterItemAdmissionResult(AdmittedFighter? Fighter, IReadOnlyList<CharacterDiagnostic> Diagnostics, FighterItemSnapshot? Item = null)
{
    internal bool IsAccepted => Fighter != null;
}

/// <summary>The verified bytes of an admitted fighter item and its motion, for presentation; never reread from disk.</summary>
public sealed class FighterItemSnapshot
{
    private readonly Dictionary<string, byte[]> files;
    public JsonElement Bindings { get; }
    public JsonElement Card { get; }
    /// <summary>The skeleton and bound clips the bake verified; the renderer samples the same objects.</summary>
    public PortableFighterMotion Motion { get; }
    public string ModelFile => Bindings.GetProperty("modelFile").GetString()!;
    internal FighterItemSnapshot(Dictionary<string, byte[]> files, JsonElement bindings, JsonElement card, PortableFighterMotion motion)
    {
        this.files = files;
        Bindings = bindings.Clone(); Card = card.Clone(); Motion = motion;
    }
    public (Dictionary<string, byte[]>? Item, IReadOnlyList<CharacterDiagnostic> Diagnostics) RecookForSteam(ulong publishedFileId)
    {
        if (publishedFileId == 0) throw new ArgumentOutOfRangeException(nameof(publishedFileId));
        var package = System.Text.Json.Nodes.JsonNode.Parse(files["package.json"])!;
        package["packageId"] = "workshop." + publishedFileId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var manifest = WorkshopItemManifestCodec.Read(files["item.json"]).Manifest!;
        var animations = Bindings.GetProperty("animations").EnumerateArray().ToDictionary(
            a => a.GetProperty("semanticId").GetString()!, a => System.Text.Json.Nodes.JsonNode.Parse(a.GetProperty("source").GetRawText())!.AsObject());
        return PortableFighterCook.Cook(new PortableFighterSource(Encoding.UTF8.GetBytes(package.ToJsonString()), files["character.json"], files["card.json"],
            ModelFile, files[ModelFile], animations, manifest.Creator, manifest.License, manifest.Attribution, manifest.AiAssisted,
            files.TryGetValue("humanoid.json", out var map) ? map : null,
            files.Where(f => f.Key.EndsWith(".png", StringComparison.Ordinal) || f.Key.EndsWith(".jpg", StringComparison.Ordinal)).ToDictionary(f => f.Key, f => f.Value),
            Bindings.GetProperty("portraits").EnumerateArray().Select(p => p.ValueKind == JsonValueKind.Null ? null : p.GetString()).ToArray(),
            Bindings.GetProperty("icons").EnumerateArray().Select(form => (IReadOnlyDictionary<string, string?>)form.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ValueKind == JsonValueKind.Null ? null : p.Value.GetString())).ToArray()));
    }
    public byte[] ReadPayload(string name) => (byte[])files[name].Clone();
}

/// <summary>Host-owned origin boundary. Only the Steam adapter supplies downloaded identity; item bytes cannot claim it.</summary>
internal static class FighterItemAdmission
{
    internal static FighterItemAdmissionResult ReadLocalItem(string folder) => Read(folder, null);
    internal static FighterItemAdmissionResult ReadDownloadedItem(string folder, ulong publishedFileId)
        => publishedFileId == 0 ? Failure("item.origin", "Steam PublishedFileId must be nonzero.") : Read(folder, publishedFileId);

    private static FighterItemAdmissionResult Failure(string code, string message)
        => new(null, new[] { MatchContentCatalogBuilder.Error(code, "item", message) });

    private static FighterItemAdmissionResult Read(string folder, ulong? steamId)
    {
        var d = new List<CharacterDiagnostic>();
        FighterItemAdmissionResult Result() => new(null, d.AsReadOnly());
        try
        {
            if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
                return Failure("item.path", "Item folder cannot be a link.");
            long totalBytes = 0;
            byte[] ReadBounded(string path, int limit) => PortableItemFiles.Read(path, limit, ref totalBytes);
            var manifest = WorkshopItemManifestCodec.Read(ReadBounded(Path.Combine(folder, "item.json"), WorkshopItemManifestCodec.MaxManifestBytes));
            d.AddRange(manifest.Diagnostics);
            if (!manifest.IsAccepted) return Result();
            if (manifest.Manifest!.ItemType != "fighter") return Failure("item.kind", "Expected fighter item.");
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (string path in Directory.EnumerateFileSystemEntries(folder))
            {
                string name = Path.GetFileName(path);
                if (name == "item.json") continue;
                if (!manifest.Manifest.Payloads.ContainsKey(name)) return Failure("item.payload.extra", "Unexpected payload " + name);
                int limit = CookedPackageLimits.FileBytes(name);
                if (limit == 0) limit = name.EndsWith(".glb", StringComparison.Ordinal) ? (int)WorkshopGlbLimits.Default.MaxFileBytes
                    : name.EndsWith(".png", StringComparison.Ordinal) || name.EndsWith(".jpg", StringComparison.Ordinal) ? 4 * 1024 * 1024 : name == "humanoid.json" ? PortableItemFiles.HumanoidBytes : 2 * 1024 * 1024;
                files.Add(name, ReadBounded(path, limit));
            }
            d.AddRange(WorkshopItemManifestCodec.Verify(manifest.Manifest, files));
            if (d.Count != 0) return Result();
            foreach (var file in files)
                if (file.Key.EndsWith(".png", StringComparison.Ordinal) || file.Key.EndsWith(".jpg", StringComparison.Ordinal))
                    d.AddRange(WorkshopGlbPrecheck.CheckImage(file.Value));
            if (files.TryGetValue("humanoid.json", out var map) && !HumanoidBoneMap.TryParseMapFile(Encoding.UTF8.GetString(map), out _, out var mapError))
                return Failure("item.humanoid", mapError);
            string model = files.Keys.Single(x => x.EndsWith(".glb", StringComparison.Ordinal));
            d.AddRange(WorkshopGlbPrecheck.Check(files[model], WorkshopGlbLimits.Default, portableImages: true).Diagnostics);
            if (d.Count != 0) return Result();
            using var binding = JsonDocument.Parse(files[CharacterPackageAssembler.BindingPath]);
            PortableFighterBindings.Validate(binding.RootElement, d, files);
            if (d.Count != 0) return Result();
            using var card = JsonDocument.Parse(files["card.json"]);
            PortableFighterBindings.Object(card.RootElement, "description", "workInProgress", "tutorialHint");
            if ((card.RootElement.GetProperty("description").GetString() ?? throw new InvalidDataException("Description must be a string.")).Length > 1024)
                return Failure("item.card", "Invalid card schema or description length.");
            card.RootElement.GetProperty("workInProgress").GetBoolean();
            var hint = card.RootElement.GetProperty("tutorialHint");
            if (hint.ValueKind != JsonValueKind.Null)
            {
                PortableFighterBindings.Object(hint, "starter", "followUp", "jump");
                foreach (string role in new[] { "starter", "followUp" })
                    if (!new[] { "1", "2", "3", "A", "E", "R", "F" }.Contains(hint.GetProperty(role).GetString()))
                        return Failure("item.card", "Tutorial hint must name canonical slots.");
                hint.GetProperty("jump").GetBoolean();
            }
            using var cooked = JsonDocument.Parse(files[CharacterPackageAssembler.ManifestPath]);
            var m = cooked.RootElement;
            string id = m.GetProperty("packageId").GetString() ?? throw new InvalidDataException("Cooked package ID must be a string.");
            string expected = steamId.HasValue ? "workshop." + steamId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "local.";
            if (steamId.HasValue ? id != expected : !id.StartsWith(expected, StringComparison.Ordinal) || id.Length == expected.Length)
                return Failure("item.namespace", "Package namespace disagrees with host origin.");
            if (!CookedCharacterPackageLoader.SupportsCompatibility(m.GetProperty("cookedSchemaVersion").GetUInt16(),
                m.GetProperty("runtimeApiMin").GetString()!, m.GetProperty("runtimeApiMax").GetString()!))
                return Failure("item.needs-update", $"Needs update: cooked by compiler {manifest.Manifest.CompilerVersion}, canonical writer {manifest.Manifest.CanonicalWriterVersion}; runtime API is outside the supported window.");
            if (m.GetProperty("version").GetString() != manifest.Manifest.ItemVersion)
                return Failure("item.identity", "Cooked and item versions must agree.");
            if (m.GetProperty("sourceHash").GetString() != PortableSourceHash.Compute(files))
                return Failure("item.source.hash", "Source hash does not match the included source file bytes.");
            var payload = files.Where(x => CookedPackageLimits.FileBytes(x.Key) != 0).ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
            var requirement = new MatchContentPackageRequirement(id, m.GetProperty("version").GetString()!, m.GetProperty("cookedContentHash").GetString()!, m.GetProperty("packageHash").GetString()!);
            var loaded = CookedCharacterPackageLoader.LoadFiles(payload, requirement, CharacterCookProfile.Workshop);
            d.AddRange(loaded.Diagnostics);
            if (!loaded.IsValid) return Result();
            // Root rule 1: poses.bin must be the exact bake of the bound clips, by the sampler the renderer uses.
            var motion = PortableFighterMotion.Read(binding.RootElement, files);
            var bake = PortablePoseBake.Verify(motion, loaded.Package!.Definition, files[CharacterPackageAssembler.PosePath]);
            if (bake.Count != 0) { d.AddRange(bake); return Result(); }
            return new(new AdmittedFighter(loaded, null), d.AsReadOnly(), new FighterItemSnapshot(new Dictionary<string, byte[]>(files) { ["item.json"] = WorkshopItemManifestCodec.Write(manifest.Manifest!) }, binding.RootElement, card.RootElement, motion));
        }
        catch (GlbMotionException ex) { return Failure(ex.Code, ex.Message); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException or OverflowException or NotSupportedException or System.Security.SecurityException or System.ComponentModel.Win32Exception or TypeLoadException or FormatException)
        { return Failure("item.read", ex.Message); }
    }
}
