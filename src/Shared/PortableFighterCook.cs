using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace SlopArena.Shared;

/// <summary>What a creator hands the portable cook: source, card, one GLB, optional images, and one motion source per animation.</summary>
public sealed record PortableFighterSource(
    byte[] PackageJson,
    byte[] CharacterJson,
    byte[] CardJson,
    string ModelFile,
    byte[] Model,
    IReadOnlyDictionary<string, JsonObject> Animations,
    string Creator,
    string License,
    string Attribution,
    bool AiAssisted = false,
    byte[]? HumanoidJson = null,
    IReadOnlyDictionary<string, byte[]>? Images = null,
    IReadOnlyList<string?>? Portraits = null,
    IReadOnlyList<IReadOnlyDictionary<string, string?>>? Icons = null);

/// <summary>
/// Compiles a portable fighter, binds its animations, bakes poses.bin with root rule 1 and writes a schema-2 item.
/// Unity-free, so an offline tool, the tests and an in-game cook produce the same bytes.
/// </summary>
public static class PortableFighterCook
{
    public static (Dictionary<string, byte[]>? Item, IReadOnlyList<CharacterDiagnostic> Diagnostics) Cook(PortableFighterSource source, System.Threading.CancellationToken cancel = default)
    {
        cancel.ThrowIfCancellationRequested();
        var compiled = CharacterPackageCompiler.CompilePortable(Encoding.UTF8.GetString(source.PackageJson), Encoding.UTF8.GetString(source.CharacterJson));
        if (compiled.HasErrors || compiled.CookedPackage == null) return (null, compiled.Diagnostics);
        var package = compiled.CookedPackage;
        var definition = package.Definition;
        var diagnostics = new List<CharacterDiagnostic>();
        var required = CharacterPackageAssembler.RequiredAnimations(package, diagnostics);
        foreach (string id in required.Where(id => !source.Animations.ContainsKey(id)).OrderBy(id => id, StringComparer.Ordinal))
            diagnostics.Add(MatchContentCatalogBuilder.Error("portable.animation.unbound", id, "Every animation the kit names needs a motion source."));
        if (diagnostics.Count != 0) return (null, diagnostics);

        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["package.json"] = source.PackageJson, ["character.json"] = source.CharacterJson, ["card.json"] = source.CardJson, [source.ModelFile] = source.Model,
        };
        if (source.HumanoidJson != null) files["humanoid.json"] = source.HumanoidJson;
        foreach (var image in source.Images ?? new Dictionary<string, byte[]>()) files[image.Key] = image.Value;
        var payloads = new JsonObject();
        foreach (var file in files.OrderBy(x => x.Key, StringComparer.Ordinal)) payloads[file.Key] = WorkshopItemManifestCodec.HashPayload(file.Value);

        var forms = definition.AlternateForm == null ? new[] { definition.Slots } : new[] { definition.Slots, definition.AlternateForm.Slots };
        var portraits = new JsonArray();
        var icons = new JsonArray();
        for (int form = 0; form < forms.Length; form++)
        {
            portraits.Add(source.Portraits != null && form < source.Portraits.Count && source.Portraits[form] != null ? JsonValue.Create(source.Portraits[form]) : null);
            var map = new JsonObject();
            foreach (string icon in forms[form].Select(slot => slot.IconId).Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.Ordinal))
                map[icon] = source.Icons != null && form < source.Icons.Count && source.Icons[form].TryGetValue(icon, out var image) && image != null ? JsonValue.Create(image) : null;
            icons.Add(map);
        }
        var animations = new JsonArray();
        foreach (string id in required.OrderBy(x => x, StringComparer.Ordinal))
            animations.Add(new JsonObject { ["semanticId"] = id, ["poseTrackId"] = id, ["frameCount"] = 1, ["source"] = source.Animations[id].DeepClone() });
        var presentations = new JsonObject();
        foreach (string id in definition.PresentationIds) presentations[id] = new JsonObject { ["kind"] = "none" };
        string sourceHash = PortableSourceHash.Compute(files);
        var bindings = new JsonObject
        {
            ["packageId"] = package.Metadata.PackageId, ["sourceHash"] = sourceHash, ["catalogSchemaVersion"] = 3, ["bindingSchemaVersion"] = 3,
            ["poseFormat"] = "SKEL", ["poseVersion"] = 1, ["sampleRate"] = 60, ["rootRuleVersion"] = GlbMotionRig.RootRuleVersion,
            ["payloads"] = payloads, ["modelFile"] = source.ModelFile, ["portraits"] = portraits, ["icons"] = icons,
            ["animations"] = animations, ["presentations"] = presentations,
        };

        byte[] poses;
        try
        {
            using var draft = System.Text.Json.JsonDocument.Parse(bindings.ToJsonString());
            var motion = PortableFighterMotion.Read(draft.RootElement, files, bindsFrameCounts: false);
            foreach (var animation in animations)
                animation!["frameCount"] = motion.Animations.Single(x => x.SemanticId == animation["semanticId"]!.GetValue<string>()).Clip.FrameCount;
            poses = PortablePoseBake.Bake(motion, definition, cancel);
        }
        catch (GlbMotionException ex) { return (null, new[] { MatchContentCatalogBuilder.Error(ex.Code, "portable", ex.Message) }); }

        var assembly = CharacterPackageAssembler.Assemble(new CharacterPackageAssemblyInput(package.Metadata.PackageId, package.Metadata.Version,
            source.Creator, source.License, source.Attribution, JsonNode.Parse(source.CharacterJson)!["authoringSchemaVersion"]!.GetValue<ushort>(), package.Metadata.CookedSchemaVersion,
            package.Metadata.RuntimeApiMin, package.Metadata.RuntimeApiMax, sourceHash, Array.Empty<PackageDependencySource>(),
            definition.CapabilityRequirements, "portable-1", "portable", 3, "SKEL", 1, 60, compiled.Diagnostics.Where(x => x.Severity != CharacterDiagnosticSeverity.Error).ToArray(),
            package.CanonicalBytes, poses, Encoding.UTF8.GetBytes(bindings.ToJsonString()), package));
        if (!assembly.IsValid) return (null, assembly.Diagnostics);
        files[CharacterPackageAssembler.ManifestPath] = assembly.ManifestBytes;
        files[CharacterPackageAssembler.RuntimePath] = assembly.RuntimeBytes;
        files[CharacterPackageAssembler.BindingPath] = assembly.BindingBytes;
        files[CharacterPackageAssembler.PosePath] = assembly.PoseBytes;
        WriteItemManifest(files, source.Creator, source.License, source.Attribution, source.AiAssisted);
        return (files, compiled.Diagnostics);
    }

    internal static void WriteItemManifest(Dictionary<string, byte[]> files, string creator, string license, string attribution, bool aiAssisted)
    {
        var hashes = new JsonObject();
        foreach (var file in files.Where(x => x.Key != "item.json").OrderBy(x => x.Key, StringComparer.Ordinal))
            hashes[file.Key] = WorkshopItemManifestCodec.HashPayload(file.Value);
        files["item.json"] = Encoding.UTF8.GetBytes(new JsonObject
        {
            ["schemaVersion"] = 2, ["itemType"] = "fighter", ["itemVersion"] = JsonNode.Parse(files[CharacterPackageAssembler.ManifestPath])!["version"]!.DeepClone(),
            ["compilerVersion"] = CharacterPackageCompiler.PortableCompilerVersion, ["canonicalWriterVersion"] = CharacterPackageCompiler.PortableCanonicalWriterVersion,
            ["creator"] = creator, ["license"] = license, ["attribution"] = attribution, ["aiAssisted"] = aiAssisted, ["payloads"] = hashes,
        }.ToJsonString());
    }
}
