using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SlopArena.Shared;

/// <summary>A host may admit local items, but cannot claim Steam provenance.</summary>
public static class LocalFighterItems
{
    public static LocalFighterItemResult ReadDownloaded(string folder, ulong publishedFileId)
    {
        var result = FighterItemAdmission.ReadDownloadedItem(folder, publishedFileId);
        return new(result.Fighter, result.Item, result.Diagnostics);
    }
    public static LocalFighterItemResult Read(string folder)
    {
        var result = FighterItemAdmission.ReadLocalItem(folder);
        return new(result.Fighter, result.Item, result.Diagnostics);
    }
}

public sealed record LocalFighterItemResult(AdmittedFighter? Fighter, FighterItemSnapshot? Item,
    IReadOnlyList<CharacterDiagnostic> Diagnostics);

/// <summary>A bounded source snapshot, including read failures, rather than a partially readable folder.</summary>
public sealed class LocalFighterWorkspace
{
    private readonly Dictionary<string, byte[]> files;
    private readonly IReadOnlyList<CharacterDiagnostic> readDiagnostics;
    public string Stamp { get; }
    public float? SuggestedModelYOffset { get; private set; }
    public static int HumanoidByteLimit => PortableItemFiles.HumanoidBytes;
    private LocalFighterWorkspace(Dictionary<string, byte[]> files, IReadOnlyList<CharacterDiagnostic> diagnostics, string probe)
    {
        this.files = files; readDiagnostics = diagnostics;
        Stamp = probe + "|" + string.Join("|", files.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key + ":" + WorkshopItemManifestCodec.HashPayload(x.Value)));
    }
    public static string Probe(string folder)
    {
        try
        {
            var dir = new DirectoryInfo(folder);
            if (!dir.Exists) return "missing:" + folder;
            var names = new HashSet<string>(new[] { "package.json", "character.json", "card.json", "cook.json", "humanoid.json" }, StringComparer.Ordinal);
            string recipe = Path.Combine(folder, "cook.json");
            if (File.Exists(recipe) && new FileInfo(recipe).Length <= 65536)
            {
                long total = 0;
                var parsed = FighterCookDocument.Read(PortableItemFiles.Read(recipe, 65536, ref total));
                if (parsed.Document is { } cook)
                { names.Add(cook.Model); names.Add(cook.Humanoid); foreach (string image in cook.Images) names.Add(image); }
            }
            return string.Join("|", names.OrderBy(x => x, StringComparer.Ordinal).Select(name =>
            {
                var f = new FileInfo(Path.Combine(folder, name));
                return name + (f.Exists ? ":" + f.Length + ":" + f.LastWriteTimeUtc.Ticks + ":" + f.CreationTimeUtc.Ticks : ":missing");
            }));
        }
        catch (Exception e) when (BoundaryFailure(e)) { return "unreadable:" + folder + ":" + e.GetType().Name + ":" + e.Message; }
    }
    internal static bool BoundaryFailure(Exception e) => e is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.Security.SecurityException or NotSupportedException or OverflowException;
    public static LocalFighterWorkspace Read(string folder)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal); var d = new List<CharacterDiagnostic>();
        string probe = Probe(folder); long total = 0;
        void ReadFile(string name, int limit, bool optional = false)
        {
            if (files.ContainsKey(name)) return;
            try
            {
                if (!FighterCookDocument.FileName(name)) throw new InvalidDataException("Expected a filename directly in the source folder.");
                if (optional && !File.Exists(Path.Combine(folder, name))) return;
                files[name] = PortableItemFiles.Read(Path.Combine(folder, name), limit, ref total);
            }
            catch (Exception e) when (BoundaryFailure(e)) { d.Add(FighterCookDocument.Error("workspace.read", name, e.Message)); }
        }
        try
        {
            if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Choose a regular source folder, not a link.");
            ReadFile("cook.json", 65536);
            foreach (string name in new[] { "package.json", "character.json", "card.json" }) ReadFile(name, 2 * 1024 * 1024);
            if (files.TryGetValue("cook.json", out var bytes))
            {
                var parsed = FighterCookDocument.Read(bytes); d.AddRange(parsed.Diagnostics);
                if (parsed.Document is { } cook)
                {
                    ReadFile(cook.Model, (int)WorkshopGlbLimits.Default.MaxFileBytes);
                    ReadFile(cook.Humanoid, HumanoidByteLimit, !cook.ExplicitHumanoid);
                    foreach (string name in cook.Images) ReadFile(name, 4 * 1024 * 1024);
                }
            }
            if (Probe(folder) != probe) d.Add(FighterCookDocument.Error("workspace.changed", "cook.json", "Source files changed while reading. Wait for the save to finish."));
        }
        catch (Exception e) when (BoundaryFailure(e)) { d.Add(FighterCookDocument.Error("workspace.read", folder, e.Message)); }
        return new LocalFighterWorkspace(files, d, probe);
    }
    public (Dictionary<string, byte[]>? Item, IReadOnlyList<CharacterDiagnostic> Diagnostics) Cook(System.Threading.CancellationToken cancel = default)
    {
        cancel.ThrowIfCancellationRequested();
        var d = new List<CharacterDiagnostic>(readDiagnostics); string file = "cook.json";
        try
        {
            CharacterCompileResult? compiled = null;
            if (files.TryGetValue("package.json", out var package) && files.TryGetValue("character.json", out var character))
            {
                bool validJson = true;
                foreach (var input in new[] { (Name: "package.json", Bytes: package), (Name: "character.json", Bytes: character) })
                    try
                    {
                        using var parsedJson = JsonDocument.Parse(input.Bytes);
                        void Numbers(JsonElement value, string path)
                        {
                            if (value.ValueKind == JsonValueKind.Object) foreach (var p in value.EnumerateObject()) Numbers(p.Value, path + "." + p.Name);
                            else if (value.ValueKind == JsonValueKind.Array) { int i = 0; foreach (var child in value.EnumerateArray()) Numbers(child, path + "[" + i++ + "]"); }
                            else if (value.ValueKind == JsonValueKind.Number && !value.TryGetInt32(out _))
                                try { PortableDecimal.Parse(value.GetRawText()); }
                                catch (Exception e) when (e is ArgumentException or FormatException or OverflowException)
                                { validJson = false; d.Add(FighterCookDocument.Error("value.out-of-range", path, e.Message)); }
                        }
                        Numbers(parsedJson.RootElement, input.Name);
                    }
                    catch (JsonException e) { validJson = false; d.Add(FighterCookDocument.Error("schema.invalid-json", input.Name + (e.Path == "$" ? "" : e.Path?.TrimStart('$')), e.Message)); }
                if (validJson) compiled = CharacterPackageCompiler.CompilePortable(Encoding.UTF8.GetString(package), Encoding.UTF8.GetString(character));
                if (compiled != null) d.AddRange(compiled.Diagnostics);
            }
            var required = compiled?.CookedPackage == null ? null : CharacterPackageAssembler.RequiredAnimations(compiled.CookedPackage, d);
            FighterCookDocument? cook = null;
            if (files.TryGetValue("cook.json", out var recipe))
            {
                var parsed = FighterCookDocument.Read(recipe, required); cook = parsed.Document;
                foreach (var diagnostic in parsed.Diagnostics) if (!d.Contains(diagnostic)) d.Add(diagnostic);
            }
            if (cook != null && compiled?.CookedPackage != null) d.AddRange(cook.CheckKit(compiled.CookedPackage.Definition));
            if (cook != null && files.TryGetValue(cook.Model, out var model))
            {
                string? map = files.TryGetValue(cook.Humanoid, out var m) ? Encoding.UTF8.GetString(m) : null;
                d.AddRange(cook.CheckClips(model, map));
            }
            if (d.Any(x => x.Severity == CharacterDiagnosticSeverity.Error) || compiled?.CookedPackage == null || cook == null) return (null, d);
            if (!compiled.CookedPackage.Metadata.PackageId.StartsWith("local.", StringComparison.Ordinal) || compiled.CookedPackage.Metadata.PackageId.Length == 6)
                return (null, new[] { FighterCookDocument.Error("item.namespace", "package.json.packageId", "Use a package ID beginning with local., such as local.my-fighter.") });
            file = cook.Model;
            var definition = compiled.CookedPackage.Definition;
            var fit = PortableMeshMeasurements.Measure(files[cook.Model], files.TryGetValue(cook.Humanoid, out var mapBytes) ? Encoding.UTF8.GetString(mapBytes) : null,
                cook.Animations[definition.Presentation.Idle], definition.Presentation.VisualScale, cancel);
            d.AddRange(fit.Diagnostics.Select(x => x with { Path = file + "." + x.Path }));
            SuggestedModelYOffset = (float)Math.Round((double)fit.ModelYOffset - (double)definition.CapsuleHeight * .5, 4);
            var effective = JsonNode.Parse(files["character.json"])!;
            effective["hurtboxBoneDefs"] = JsonSerializer.SerializeToNode(fit.Hurtboxes, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var animations = cook.Animations.ToDictionary(x => x.Key, x => new JsonObject { ["kind"] = "glb", ["clipName"] = x.Value }, StringComparer.Ordinal);
            var source = new PortableFighterSource(files["package.json"], PortableCanonicalJson.Normalize(Encoding.UTF8.GetBytes(effective.ToJsonString())), files["card.json"], cook.Model, files[cook.Model], animations,
                cook.Creator, cook.License, cook.Attribution, cook.AiAssisted, mapBytes,
                cook.Images.ToDictionary(x => x, x => files[x], StringComparer.Ordinal), cook.Portraits, cook.Icons);
            var result = PortableFighterCook.Cook(source, cancel);
            return (result.Item, d.Concat(result.Diagnostics).Distinct().ToArray());
        }
        catch (GlbMotionException e) { d.Add(FighterCookDocument.Error(e.Code, file, e.Message)); }
        catch (Exception e) when (BoundaryFailure(e) || e is JsonException or KeyNotFoundException or IndexOutOfRangeException or FormatException)
        { d.Add(FighterCookDocument.Error("workspace.source", file, e.Message)); }
        return (null, d);
    }
    public void ApplyModelYOffset(string folder) => ApplyModelYOffset(folder, null);
    internal void ApplyModelYOffset(string folder, Action? beforeCommit)
    {
        if (!SuggestedModelYOffset.HasValue) throw new InvalidOperationException("Cook the fighter before applying the sole suggestion.");
        byte[] original = files["character.json"];
        byte[] replacement = ReplaceOffset(original, SuggestedModelYOffset.Value);
        string path = Path.Combine(folder, "character.json"), temporary = Path.Combine(folder, ".workspace-" + Guid.NewGuid().ToString("N"));
        string backup = temporary + ".backup";
        bool conflict = false;
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(replacement, 0, replacement.Length); stream.Flush(true); }
            if (Read(folder).Stamp != Stamp || !File.ReadAllBytes(path).SequenceEqual(original)) throw new IOException("character.json changed. Wait for the next cook before applying the suggestion.");
            beforeCommit?.Invoke();
            File.Replace(temporary, path, backup);
            if (!File.ReadAllBytes(backup).SequenceEqual(original))
            {
                conflict = true;
                if (File.ReadAllBytes(path).SequenceEqual(replacement)) File.Replace(backup, path, temporary);
                throw new IOException("character.json changed during replacement. Review character.json and any retained conflict files at " + temporary + " or " + backup + ". No conflicting bytes are deleted.");
            }
        }
        finally { if (!conflict && File.Exists(temporary)) File.Delete(temporary); if (File.Exists(backup) && File.ReadAllBytes(backup).SequenceEqual(original)) File.Delete(backup); }
    }
    internal static byte[] ReplaceOffset(byte[] original, float value)
    {
        int bom = original.Length >= 3 && original[0] == 239 && original[1] == 187 && original[2] == 191 ? 3 : 0;
        var reader = new Utf8JsonReader(original.AsSpan(bom)); bool presentation = false, offset = false;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName && reader.CurrentDepth == 1) presentation = reader.ValueTextEquals("presentation");
            if (presentation && reader.TokenType == JsonTokenType.PropertyName && reader.CurrentDepth == 2 && reader.ValueTextEquals("modelYOffset")) { offset = true; continue; }
            if (offset)
            {
                if (reader.TokenType != JsonTokenType.Number) throw new InvalidDataException("character.json.presentation.modelYOffset must be a number.");
                int start = bom + (int)reader.TokenStartIndex, end = bom + (int)reader.BytesConsumed;
                byte[] number = Encoding.UTF8.GetBytes(PortableCanonicalJson.FloatDecimal(value));
                return original.Take(start).Concat(number).Concat(original.Skip(end)).ToArray();
            }
        }
        throw new InvalidDataException("character.json.presentation.modelYOffset is missing.");
    }
    public static string DiagnosticPath(CharacterDiagnostic d)
        => d.Path == "manifest" || d.Path.StartsWith("manifest.", StringComparison.Ordinal) ? "package.json" + d.Path.Substring(8)
         : d.Path == "character" || d.Path.StartsWith("character.", StringComparison.Ordinal) && !d.Path.StartsWith("character.json", StringComparison.Ordinal) ? "character.json" + d.Path.Substring(9)
         : d.Path == "json" || d.Path.StartsWith("json.", StringComparison.Ordinal) ? "character.json." + d.Path : d.Path;
    public static string DiagnosticText(CharacterDiagnostic d) => $"{d.Severity} {d.Code} {DiagnosticPath(d)}: {d.Message}";
}
