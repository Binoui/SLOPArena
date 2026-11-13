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

internal sealed class FighterItemSnapshot
{
    private readonly Dictionary<string, byte[]> files;
    internal JsonElement Bindings { get; }
    internal JsonElement Card { get; }
    internal FighterItemSnapshot(Dictionary<string, byte[]> files, JsonElement bindings, JsonElement card)
    {
        this.files = files;
        Bindings = bindings.Clone(); Card = card.Clone();
    }
    internal byte[] ReadPayload(string name) => (byte[])files[name].Clone();
}

/// <summary>Host-owned origin boundary. No product caller admits external files yet.</summary>
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
            string id = m.GetProperty("packageId").GetString()!;
            string expected = steamId.HasValue ? "workshop." + steamId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "local.";
            if (steamId.HasValue ? id != expected : !id.StartsWith(expected, StringComparison.Ordinal) || id.Length == expected.Length)
                return Failure("item.namespace", "Package namespace disagrees with host origin.");
            if (m.GetProperty("version").GetString() != manifest.Manifest.ItemVersion)
                return Failure("item.identity", "Cooked and item versions must agree.");
            if (m.GetProperty("sourceHash").GetString() != PortableSourceHash.Compute(files))
                return Failure("item.source.hash", "Source hash does not match the included source file bytes.");
            var payload = files.Where(x => CookedPackageLimits.FileBytes(x.Key) != 0).ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
            var requirement = new MatchContentPackageRequirement(id, m.GetProperty("version").GetString()!, m.GetProperty("cookedContentHash").GetString()!, m.GetProperty("packageHash").GetString()!);
            var loaded = CookedCharacterPackageLoader.LoadFiles(payload, requirement, CharacterCookProfile.Workshop);
            d.AddRange(loaded.Diagnostics);
            if (loaded.Diagnostics.Any(x => x.Code == "package.compatibility.unsupported"))
                d.Add(MatchContentCatalogBuilder.Error("item.needs-update", "item.json", $"Needs update: cooked by compiler {manifest.Manifest.CompilerVersion}, canonical writer {manifest.Manifest.CanonicalWriterVersion}; runtime API is outside the supported window."));
            if (loaded.IsValid && binding.RootElement.GetProperty("animations").EnumerateArray().Any(x => x.GetProperty("source").GetProperty("kind").GetString() == "glb"))
                return Failure("item.pose-bake.unavailable", "Pose bake verification not available. Unit 4c must verify poses.bin against the bound clips before Match admission.");
            return loaded.IsValid ? new(new AdmittedFighter(loaded, null, null), d.AsReadOnly(), new FighterItemSnapshot(files, binding.RootElement, card.RootElement)) : Result();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException or OverflowException or NotSupportedException or System.Security.SecurityException or System.ComponentModel.Win32Exception or TypeLoadException)
        { return Failure("item.read", ex.Message); }
    }
}
