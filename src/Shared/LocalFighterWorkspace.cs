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
    public static LocalFighterItemResult Read(string folder)
    {
        var result = FighterItemAdmission.ReadLocalItem(folder);
        return new(result.Fighter, result.Item, result.Diagnostics);
    }
}

public sealed record LocalFighterItemResult(AdmittedFighter? Fighter, FighterItemSnapshot? Item,
    IReadOnlyList<CharacterDiagnostic> Diagnostics);

/// <summary>An immutable read of a creator folder. Call disk operations on a worker.</summary>
public sealed class LocalFighterWorkspace
{
    private readonly Dictionary<string, byte[]> files;
    public string Stamp { get; }
    public float? SuggestedModelYOffset { get; private set; }
    private LocalFighterWorkspace(Dictionary<string, byte[]> files)
    {
        this.files = files;
        Stamp = string.Join("|", files.OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => x.Key + ":" + WorkshopItemManifestCodec.HashPayload(x.Value)));
    }
    public static LocalFighterWorkspace Read(string folder)
    {
        if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Choose a regular source folder, not a link.");
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        long total = 0;
        foreach (string path in Directory.EnumerateFileSystemEntries(folder))
        {
            string name = Path.GetFileName(path);
            bool glb = name.EndsWith(".glb", StringComparison.Ordinal);
            if (!(glb || name.EndsWith(".json", StringComparison.Ordinal) || name.EndsWith(".png", StringComparison.Ordinal) || name.EndsWith(".jpg", StringComparison.Ordinal))) continue;
            int limit = glb ? (int)WorkshopGlbLimits.Default.MaxFileBytes : name.EndsWith(".json", StringComparison.Ordinal) ? 2 * 1024 * 1024 : 4 * 1024 * 1024;
            files.Add(name, PortableItemFiles.Read(path, limit, ref total));
        }
        return new LocalFighterWorkspace(files);
    }
    private byte[] Bytes(string name) => files.TryGetValue(name, out var bytes) ? bytes : throw new InvalidDataException("Missing " + name + " in the source folder.");
    public (Dictionary<string, byte[]>? Item, IReadOnlyList<CharacterDiagnostic> Diagnostics) Cook()
    {
        string file = "cook.json";
        try
        {
            using var cook = JsonDocument.Parse(Bytes(file));
            var c = cook.RootElement;
            PortableFighterBindings.Object(c, new[] { "model", "animations", "creator", "license", "attribution", "aiAssisted" }
                .Concat(new[] { "portraits", "icons" }.Where(name => c.TryGetProperty(name, out _))).ToArray());
            string model = c.GetProperty("model").GetString()!;
            if (Path.GetFileName(model) != model || !model.EndsWith(".glb", StringComparison.Ordinal)) throw new InvalidDataException("model must name a .glb directly in this folder.");
            var animations = c.GetProperty("animations").EnumerateObject().ToDictionary(x => x.Name,
                x => new JsonObject { ["kind"] = "glb", ["clipName"] = x.Value.GetString() }, StringComparer.Ordinal);
            file = "package.json";
            using var package = JsonDocument.Parse(Bytes(file));
            string id = package.RootElement.GetProperty("packageId").GetString()!;
            if (!id.StartsWith("local.", StringComparison.Ordinal) || id.Length == 6)
                return (null, new[] { MatchContentCatalogBuilder.Error("item.namespace", "manifest.packageId", "Use a package ID beginning with local., such as local.my-fighter.") });
            file = "character.json";
            var source = new PortableFighterSource(Bytes("package.json"), Bytes(file), Bytes("card.json"), model, Bytes(model), animations,
                c.GetProperty("creator").GetString()!, c.GetProperty("license").GetString()!, c.GetProperty("attribution").GetString()!, c.GetProperty("aiAssisted").GetBoolean(),
                files.TryGetValue("humanoid.json", out var map) ? map : null,
                files.Where(x => x.Key.EndsWith(".png", StringComparison.Ordinal) || x.Key.EndsWith(".jpg", StringComparison.Ordinal)).ToDictionary(x => x.Key, x => x.Value),
                c.TryGetProperty("portraits", out var portraits) ? portraits.EnumerateArray().Select(p => p.GetString()).ToArray() : null,
                c.TryGetProperty("icons", out var icons) ? icons.EnumerateArray().Select(form => (IReadOnlyDictionary<string, string?>)form.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString())).ToArray() : null);
            var compiled = CharacterPackageCompiler.CompilePortable(Encoding.UTF8.GetString(source.PackageJson), Encoding.UTF8.GetString(source.CharacterJson));

            var clipDiagnostics = new List<CharacterDiagnostic>();
            var rig = GlbMotionRig.Read(source.Model, source.HumanoidJson == null ? null : Encoding.UTF8.GetString(source.HumanoidJson));
            foreach (var animation in animations)
            {
                try { rig.Clip(animation.Value["clipName"]!.GetValue<string>()); }
                catch (GlbMotionException e) { clipDiagnostics.Add(MatchContentCatalogBuilder.Error(e.Code, "cook.json.animations." + animation.Key, e.Message)); }
            }
            if (compiled.HasErrors || compiled.CookedPackage == null || clipDiagnostics.Count != 0)
                return (null, compiled.Diagnostics.Concat(clipDiagnostics).ToArray());
            var definition = compiled.CookedPackage.Definition;
            if (!animations.TryGetValue(definition.Presentation.Idle, out var idle))
                return PortableFighterCook.Cook(source);
            file = model;
            var fit = PortableMeshMeasurements.Measure(source.Model, source.HumanoidJson == null ? null : Encoding.UTF8.GetString(source.HumanoidJson),
                idle["clipName"]!.GetValue<string>(), definition.Presentation.VisualScale);
            SuggestedModelYOffset = (float)Math.Round(fit.ModelYOffset - definition.CapsuleHeight * .5f, 4);
            var character = JsonNode.Parse(source.CharacterJson)!;
            character["hurtboxBoneDefs"] = JsonSerializer.SerializeToNode(fit.Hurtboxes, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var cooked = PortableFighterCook.Cook(source with { CharacterJson = Encoding.UTF8.GetBytes(character.ToJsonString()) });
            return cooked;
        }
        catch (GlbMotionException e) { return (null, new[] { MatchContentCatalogBuilder.Error(e.Code, "model.glb", e.Message) }); }
        catch (Exception e) when (e is JsonException or InvalidDataException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        { return (null, new[] { MatchContentCatalogBuilder.Error("workspace.source", file, e.Message) }); }
    }
    public void ApplyModelYOffset(string folder)
    {
        if (!SuggestedModelYOffset.HasValue) throw new InvalidOperationException("Cook the fighter before applying the sole suggestion.");
        if (Read(folder).Stamp != Stamp) throw new IOException("Files changed. Wait for the next cook before applying the suggestion.");
        var character = JsonNode.Parse(Bytes("character.json"))!;
        character["presentation"]!["modelYOffset"] = SuggestedModelYOffset.Value;
        File.WriteAllText(Path.Combine(folder, "character.json"), character.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
    public static string DiagnosticPath(CharacterDiagnostic d)
        => d.Path.StartsWith("manifest", StringComparison.Ordinal) ? "package.json" + d.Path.Substring(8)
         : d.Path.StartsWith("character", StringComparison.Ordinal) ? "character.json" + d.Path.Substring(9)
         : d.Path;
    public static string DiagnosticText(CharacterDiagnostic d) => $"{d.Severity} {d.Code} {DiagnosticPath(d)}: {d.Message}";
}
