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
            byte[] ReadBounded(string path, int limit)
            {
                if ((File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                    throw new InvalidDataException("Payloads must be regular files.");
                using var stream = File.OpenRead(path);
                if (stream.Length > limit) throw new InvalidDataException("Byte limit exceeded for " + Path.GetFileName(path));
                var bytes = new byte[checked((int)stream.Length)];
                int offset = 0, read;
                while (offset < bytes.Length && (read = stream.Read(bytes, offset, bytes.Length - offset)) > 0) offset += read;
                if (offset != bytes.Length || stream.ReadByte() != -1) throw new InvalidDataException("File changed while reading.");
                return bytes;
            }
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
                    : name.EndsWith(".png", StringComparison.Ordinal) || name.EndsWith(".jpg", StringComparison.Ordinal) ? 4 * 1024 * 1024 : 2 * 1024 * 1024;
                files.Add(name, ReadBounded(path, limit));
            }
            d.AddRange(WorkshopItemManifestCodec.Verify(manifest.Manifest, files));
            if (d.Count != 0) return Result();
            foreach (var file in files)
                if (file.Key.EndsWith(".png", StringComparison.Ordinal) || file.Key.EndsWith(".jpg", StringComparison.Ordinal))
                    d.AddRange(WorkshopGlbPrecheck.CheckImage(file.Value));
            if (files.TryGetValue("humanoid.json", out var map) && !HumanoidBoneMap.TryParseMapFile(Encoding.UTF8.GetString(map), out _, out var mapError))
                return Failure("item.humanoid", mapError);
            string model = files.Keys.Single(x => x.EndsWith(".glb", StringComparison.OrdinalIgnoreCase));
            d.AddRange(WorkshopGlbPrecheck.Check(files[model], WorkshopGlbLimits.Default).Diagnostics);
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
            var source = CharacterPackageSourceCodec.Load(Encoding.UTF8.GetString(files["package.json"]), Encoding.UTF8.GetString(files["character.json"]));
            d.AddRange(source.Diagnostics);
            using var cooked = JsonDocument.Parse(files[CharacterPackageAssembler.ManifestPath]);
            var m = cooked.RootElement;
            string id = m.GetProperty("packageId").GetString()!;
            string expected = steamId.HasValue ? "workshop." + steamId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "local.";
            if (steamId.HasValue ? id != expected : !id.StartsWith(expected, StringComparison.Ordinal) || id.Length == expected.Length)
                return Failure("item.namespace", "Package namespace disagrees with host origin.");
            if (source.Source == null || source.Source.Manifest.PackageId != id || source.Source.Manifest.Version != manifest.Manifest.ItemVersion
                || m.GetProperty("version").GetString() != manifest.Manifest.ItemVersion)
                return Failure("item.source.identity", "Source, cooked and item identity must agree.");
            if (d.Count != 0) return Result();
            var compiled = CharacterPackageCompiler.Compile(source.Source, CharacterCookProfile.Workshop);
            d.AddRange(compiled.Diagnostics);
            if (compiled.CookedPackage == null || !compiled.CookedPackage.CanonicalBytes.SequenceEqual(files[CharacterPackageAssembler.RuntimePath]))
                return Failure("item.source.runtime", "Portable runtime must match the Workshop compilation of the included source.");
            var payload = files.Where(x => CookedPackageLimits.FileBytes(x.Key) != 0).ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
            var requirement = new MatchContentPackageRequirement(id, m.GetProperty("version").GetString()!, m.GetProperty("cookedContentHash").GetString()!, m.GetProperty("packageHash").GetString()!);
            var loaded = CookedCharacterPackageLoader.LoadFiles(payload, requirement, CharacterCookProfile.Workshop);
            d.AddRange(loaded.Diagnostics);
            return loaded.IsValid ? new(new AdmittedFighter(loaded, null, null), d.AsReadOnly(), new FighterItemSnapshot(files, binding.RootElement, card.RootElement)) : Result();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException or OverflowException)
        { return Failure("item.read", ex.Message); }
    }
}
