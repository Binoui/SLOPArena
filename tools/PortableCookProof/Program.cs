using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using SlopArena.Shared;

internal static class Program
{
    private const string Midpoint = "1.00000005960464477539062500000001";

    /// <summary><c>cook &lt;source&gt; &lt;item&gt;</c> writes an item; <c>&lt;item&gt;</c> admits it and re-cooks it byte for byte.</summary>
    public static int Main(string[] args)
    {
        if (args[0] == "cook")
        {
            var (item, diagnostics) = PortableFighterCook.Cook(Source(args[1]));
            if (item == null) throw new Exception(string.Join("\n", diagnostics));
            Directory.CreateDirectory(args[2]);
            foreach (var file in item) File.WriteAllBytes(Path.Combine(args[2], file.Key), file.Value);
            Console.WriteLine(WorkshopItemManifestCodec.HashPayload(item[CharacterPackageAssembler.PosePath]));
            return 0;
        }
        string folder = args[0];
        string characterJson = File.ReadAllText(Path.Combine(folder, "character.json"));
        var compiled = CharacterPackageCompiler.CompilePortable(File.ReadAllText(Path.Combine(folder, "package.json")), characterJson);
        if (compiled.HasErrors) throw new Exception(string.Join("\n", compiled.Diagnostics));
        if (characterJson.Contains(Midpoint) && (BitConverter.SingleToInt32Bits(compiled.CookedPackage.Definition.Movement.AirAccelBase) != 0x3f800001
            || BitConverter.SingleToInt32Bits(compiled.CookedPackage.Definition.Movement.AirFloatGravity) != 0))
            throw new Exception("Portable midpoint rounding or negative zero differs.");
        var admission = typeof(CharacterPackageCompiler).Assembly.GetType("SlopArena.Shared.FighterItemAdmission")
            .GetMethod("ReadLocalItem", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, new object[] { folder });
        var diagnosticsRead = (IReadOnlyList<CharacterDiagnostic>)admission.GetType().GetProperty("Diagnostics").GetValue(admission);
        if (diagnosticsRead.Count != 0 || admission.GetType().GetProperty("Fighter").GetValue(admission) == null)
            throw new Exception("Expected the item to be admitted with its bake verified: " + string.Join(";", diagnosticsRead));
        var (recooked, recookDiagnostics) = PortableFighterCook.Cook(ItemSource(folder));
        if (recooked == null) throw new Exception(string.Join("\n", recookDiagnostics));
        var files = Directory.GetFiles(folder).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (!files.SequenceEqual(recooked.Keys.OrderBy(x => x, StringComparer.Ordinal)))
            throw new Exception("Re-cook wrote a different file set.");
        foreach (string file in files)
            if (!recooked[file].AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(folder, file))))
                throw new Exception("Re-cooked " + file + " differs.");
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "manifest.json")));
        Console.WriteLine(manifest.RootElement.GetProperty("cookedContentHash").GetString() + " " + manifest.RootElement.GetProperty("packageHash").GetString()
            + " poses " + WorkshopItemManifestCodec.HashPayload(recooked[CharacterPackageAssembler.PosePath]));
        return 0;
    }

    /// <summary>A creator folder: package.json, character.json, card.json, the GLB and cook.json naming each animation's clip.</summary>
    private static PortableFighterSource Source(string folder)
    {
        using var cook = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "cook.json")));
        var root = cook.RootElement;
        string model = root.GetProperty("model").GetString();
        var animations = root.GetProperty("animations").EnumerateObject().ToDictionary(x => x.Name,
            x => new JsonObject { ["kind"] = "glb", ["clipName"] = x.Value.GetString() }, StringComparer.Ordinal);
        return new PortableFighterSource(Read(folder, "package.json"), Read(folder, "character.json"), Read(folder, "card.json"), model, Read(folder, model),
            animations, root.GetProperty("creator").GetString(), root.GetProperty("license").GetString(), root.GetProperty("attribution").GetString(),
            root.GetProperty("aiAssisted").GetBoolean(), File.Exists(Path.Combine(folder, "humanoid.json")) ? Read(folder, "humanoid.json") : null);
    }

    /// <summary>The same source read back from an item's own payloads, bindings and manifest.</summary>
    private static PortableFighterSource ItemSource(string folder)
    {
        using var item = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "item.json")));
        using var bindings = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, CharacterPackageAssembler.BindingPath)));
        var b = bindings.RootElement;
        string model = b.GetProperty("modelFile").GetString();
        var animations = b.GetProperty("animations").EnumerateArray().ToDictionary(x => x.GetProperty("semanticId").GetString(),
            x => (JsonObject)JsonNode.Parse(x.GetProperty("source").GetRawText()), StringComparer.Ordinal);
        var images = b.GetProperty("payloads").EnumerateObject().Select(x => x.Name).Where(x => x.EndsWith(".png", StringComparison.Ordinal) || x.EndsWith(".jpg", StringComparison.Ordinal))
            .ToDictionary(x => x, x => Read(folder, x), StringComparer.Ordinal);
        var portraits = b.GetProperty("portraits").EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : null).ToArray();
        var icons = b.GetProperty("icons").EnumerateArray().Select(form => (IReadOnlyDictionary<string, string>)form.EnumerateObject()
            .ToDictionary(x => x.Name, x => x.Value.ValueKind == JsonValueKind.String ? x.Value.GetString() : null)).ToArray();
        var i = item.RootElement;
        return new PortableFighterSource(Read(folder, "package.json"), Read(folder, "character.json"), Read(folder, "card.json"), model, Read(folder, model),
            animations, i.GetProperty("creator").GetString(), i.GetProperty("license").GetString(), i.GetProperty("attribution").GetString(),
            i.GetProperty("aiAssisted").GetBoolean(), File.Exists(Path.Combine(folder, "humanoid.json")) ? Read(folder, "humanoid.json") : null,
            images, portraits, icons);
    }

    private static byte[] Read(string folder, string file) => File.ReadAllBytes(Path.Combine(folder, file));
}
