using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class FighterItemAdmissionTests
{
    internal static Dictionary<string, byte[]> Item(string id = "local.tumbleweed", bool media = false)
    {
        string root = CookedPackageEdits.Root;
        var sourceFiles = new[] { "package.json", "character.json" }.ToDictionary(x => x,
            x => File.ReadAllBytes(Path.Combine(root, "tests/Shared.Tests/Fixtures/Tumbleweed", x)));
        var package = JsonNode.Parse(sourceFiles["package.json"])!;
        package["packageId"] = id;
        sourceFiles["package.json"] = Encoding.UTF8.GetBytes(package.ToJsonString());
        var compiled = CharacterPackageCompiler.CompilePortable(Encoding.UTF8.GetString(sourceFiles["package.json"]), Encoding.UTF8.GetString(sourceFiles["character.json"]));
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        var input = CharacterPackageAssemblerTests.BuildInput(compiled.CookedPackage!, Array.Empty<PackageDependencySource>(), compiled.CookedPackage!.Definition.CapabilityRequirements, Array.Empty<CharacterDiagnostic>());
        var old = JsonNode.Parse(input.BindingBytes)!;
        var animations = new JsonArray();
        foreach (var animation in old["animations"]!.AsArray())
            animations.Add(new JsonObject { ["semanticId"] = animation!["semanticId"]!.DeepClone(), ["poseTrackId"] = animation["poseTrackId"]!.DeepClone(), ["frameCount"] = 1,
                ["source"] = new JsonObject { ["kind"] = "glb", ["clipName"] = "fixture" } });
        sourceFiles["card.json"] = Encoding.UTF8.GetBytes("{\"description\":\"Tumbleweed test kit\",\"workInProgress\":true,\"tutorialHint\":null}");
        sourceFiles["tumbleweed.glb"] = MinimalGlb();
        if (media)
        {
            var png = new byte[33];
            new byte[] {137,80,78,71,13,10,26,10,0,0,0,13,73,72,68,82}.CopyTo(png, 0);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16), 8);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20), 8);
            sourceFiles["portrait.png"] = png;
            sourceFiles["move.png"] = (byte[])png.Clone();
            sourceFiles["humanoid.json"] = Encoding.UTF8.GetBytes("{\"bones\":[]}");
        }
        var hashes = new JsonObject();
        foreach (var f in sourceFiles) hashes[f.Key] = WorkshopItemManifestCodec.HashPayload(f.Value);
        var bindings = new JsonObject
        {
            ["packageId"] = id, ["sourceHash"] = input.SourceHash, ["catalogSchemaVersion"] = 3, ["bindingSchemaVersion"] = 3,
            ["poseFormat"] = "SKEL", ["poseVersion"] = 1, ["sampleRate"] = 60, ["rootRuleVersion"] = 1,
            ["payloads"] = hashes, ["modelFile"] = "tumbleweed.glb", ["portraitFile"] = media ? "portrait.png" : null, ["icons"] = media ? new JsonObject { ["jab"] = "move.png" } : new JsonObject(), ["animations"] = animations,
        };
        var assembly = CharacterPackageAssembler.Assemble(new CharacterPackageAssemblyInput(id, input.Version, input.Creator, input.License, input.Attribution,
            input.AuthoringSchemaVersion, input.CookedSchemaVersion, input.RuntimeApiMin, input.RuntimeApiMax, input.SourceHash, input.Dependencies,
            input.CapabilityRequirements, "portable-1", "portable", 3, "SKEL", 1, 60, input.Warnings, input.RuntimeBytes, input.PoseBytes,
            Encoding.UTF8.GetBytes(bindings.ToJsonString()), input.CookedPackage));
        Assert.True(assembly.IsValid, string.Join("\n", assembly.Diagnostics));
        sourceFiles[CharacterPackageAssembler.ManifestPath] = assembly.ManifestBytes;
        sourceFiles[CharacterPackageAssembler.RuntimePath] = assembly.RuntimeBytes;
        sourceFiles[CharacterPackageAssembler.BindingPath] = assembly.BindingBytes;
        sourceFiles[CharacterPackageAssembler.PosePath] = assembly.PoseBytes;
        RehashItem(sourceFiles);
        return sourceFiles;
    }

    internal static void RehashItem(Dictionary<string, byte[]> files)
    {
        var hashes = new JsonObject();
        foreach (var f in files.Where(x => x.Key != "item.json")) hashes[f.Key] = WorkshopItemManifestCodec.HashPayload(f.Value);
        files["item.json"] = Encoding.UTF8.GetBytes(new JsonObject { ["schemaVersion"] = 2, ["itemType"] = "fighter", ["itemVersion"] = "0.1.0",
            ["creator"] = "test", ["license"] = "CC0", ["attribution"] = "Tumbleweed proof kit; synthetic fixture media", ["aiAssisted"] = false, ["payloads"] = hashes }.ToJsonString());
    }

    private static byte[] MinimalGlb()
    {
        var json = Encoding.UTF8.GetBytes("""
        {"asset":{"version":"2.0"},"buffers":[{"byteLength":112}],
        "bufferViews":[{"buffer":0,"byteLength":36},{"buffer":0,"byteOffset":36,"byteLength":12},
        {"buffer":0,"byteOffset":48,"byteLength":48},{"buffer":0,"byteOffset":96,"byteLength":4},{"buffer":0,"byteOffset":100,"byteLength":12}],
        "accessors":[{"bufferView":0,"componentType":5126,"type":"VEC3","count":3,"min":[0,0,0],"max":[1,1,0]},
        {"bufferView":1,"componentType":5121,"type":"VEC4","count":3},{"bufferView":2,"componentType":5126,"type":"VEC4","count":3},
        {"bufferView":3,"componentType":5126,"type":"SCALAR","count":1,"min":[0],"max":[0]},
        {"bufferView":4,"componentType":5126,"type":"VEC3","count":1}],
        "meshes":[{"primitives":[{"attributes":{"POSITION":0,"JOINTS_0":1,"WEIGHTS_0":2}}]}],
        "nodes":[{"mesh":0,"skin":0},{"name":"root"}],"skins":[{"joints":[1]}],"scene":0,"scenes":[{"nodes":[0,1]}],
        "animations":[{"name":"fixture","samplers":[{"input":3,"output":4,"interpolation":"LINEAR"}],
        "channels":[{"sampler":0,"target":{"node":1,"path":"translation"}}]}]}
        """);
        int length = (json.Length + 3) & ~3;
        var result = new byte[28 + length + 112];
        void U(int offset, uint value) => System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset), value);
        U(0, 0x46546c67); U(4, 2); U(8, (uint)result.Length); U(12, (uint)length); U(16, 0x4e4f534a);
        result.AsSpan(20, length).Fill(32); json.CopyTo(result, 20); U(20 + length, 112); U(24 + length, 0x004e4942);
        int bin = 28 + length;
        U(bin + 12, 0x3f800000); U(bin + 28, 0x3f800000);
        for (int vertex = 0; vertex < 3; vertex++) U(bin + 48 + vertex * 16, 0x3f800000);
        return result;
    }

    private static FighterItemAdmissionResult Read(Dictionary<string, byte[]> files, ulong? steam = null)
    {
        string path = Path.Combine(Path.GetTempPath(), "fighter-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try
        {
            foreach (var f in files) File.WriteAllBytes(Path.Combine(path, f.Key), f.Value);
            return steam.HasValue ? FighterItemAdmission.ReadDownloadedItem(path, steam.Value) : FighterItemAdmission.ReadLocalItem(path);
        }
        finally { Directory.Delete(path, true); }
    }

    [Fact] public void TumbleweedAdmittedEndToEnd()
    {
        var item = Item(); var result = Read(item);
        if (Environment.GetEnvironmentVariable("SLOP_PORTABLE_PROOF") is { } output)
        {
            Directory.CreateDirectory(output);
            foreach (var f in item) File.WriteAllBytes(Path.Combine(output, f.Key), f.Value);
        }
        Assert.True(result.IsAccepted, string.Join("\n", result.Diagnostics));
        Assert.Equal("local.tumbleweed", result.Fighter!.Identity.PackageId);
        Assert.Null(result.Fighter.LegacySelector);
        var bytes = result.Item!.ReadPayload("tumbleweed.glb"); bytes[0] ^= 1;
        Assert.Equal(item["tumbleweed.glb"], result.Item.ReadPayload("tumbleweed.glb"));
        Assert.Equal("Tumbleweed test kit", result.Item.Card.GetProperty("description").GetString());
        var manifest = WorkshopItemManifestCodec.Read(item["item.json"]).Manifest!;
        Assert.Equal(WorkshopItemManifestCodec.Write(manifest), WorkshopItemManifestCodec.Write(WorkshopItemManifestCodec.Read(WorkshopItemManifestCodec.Write(manifest)).Manifest!));
    }
    [Fact] public void ChangedByteInEveryPayloadFails()
    {
        var item = Item(media: true);
        Assert.True(Read(item).IsAccepted);
        foreach (var file in item.Keys.Where(x => x != "item.json").ToArray())
        {
            item[file][0] ^= 1;
            Assert.Contains(Read(item).Diagnostics, x => x.Code == "item.payload.mismatch");
            item[file][0] ^= 1;
        }
    }
    [Theory] [InlineData("fightguy", 0UL)] [InlineData("workshop.42", 0UL)] [InlineData("local.tumbleweed", 42UL)] [InlineData("workshop.41", 42UL)]
    public void HostNamespaceIsEnforced(string id, ulong steam)
    { Assert.Contains(Read(Item(id), steam == 0 ? null : steam).Diagnostics, x => x.Code == "item.namespace"); }
    [Fact] public void DownloadUsesHostSteamId() => Assert.True(Read(Item("workshop.42"), 42).IsAccepted);
    [Fact] public void ExtraFileFails() { var item = Item(); item["other.txt"] = new byte[1]; Assert.Contains(Read(item).Diagnostics, x => x.Code == "item.payload.extra"); }
    [Fact] public void RenamedFileFails() { var item = Item(); item["renamed.glb"] = item["tumbleweed.glb"]; item.Remove("tumbleweed.glb"); Assert.False(Read(item).IsAccepted); }
    [Fact] public void OuterRehashCannotHideChangedMedia() { var item = Item(); item["card.json"][1] ^= 1; RehashItem(item); Assert.Contains(Read(item).Diagnostics, x => x.Code == "portable.payload.mismatch"); }
    [Theory]
    [InlineData("rootRuleVersion", "9", "portable.metadata")]
    [InlineData("sampleRate", "30", "portable.metadata")]
    [InlineData("rigGlobalObjectId", "\"editor\"", "portable.schema")]
    public void InvalidBindingMetadataRefused(string field, string value, string code)
    {
        var root = JsonNode.Parse(Item()["client.bindings"])!.AsObject(); root[field] = JsonNode.Parse(value);
        var d = CharacterPackageAssembler.ValidateClientBindings(Encoding.UTF8.GetBytes(root.ToJsonString()));
        Assert.Contains(d, x => x.Code == code);
    }
    [Fact] public void SharedClipsAreUnavailable()
    {
        var root = JsonNode.Parse(Item()["client.bindings"])!;
        root["animations"]![0]!["source"] = new JsonObject { ["kind"] = "shared", ["id"] = "slop.anim.idle", ["version"] = "1" };
        Assert.Contains(CharacterPackageAssembler.ValidateClientBindings(Encoding.UTF8.GetBytes(root.ToJsonString())), x => x.Code == "portable.shared.unsupported");
    }
    [Theory] [InlineData("../a.glb")] [InlineData("a/b.glb")] [InlineData("C:a.glb")] [InlineData("CON.glb")] [InlineData("a.glb ")] [InlineData(".model.glb")]
    public void FighterPathTricksRefused(string name)
    {
        var manifest = JsonNode.Parse(Item()["item.json"])!; manifest["payloads"]![name] = new string('a', 64);
        Assert.Contains(WorkshopItemManifestCodec.Read(Encoding.UTF8.GetBytes(manifest.ToJsonString())).Diagnostics, x => x.Code == "item.payload.name");
    }
    [Theory] [InlineData("description")] [InlineData("workInProgress")] [InlineData("baseFighterPackageId")]
    public void CardOrBaseFieldsCannotAppearInFighterManifest(string field)
    {
        var manifest = JsonNode.Parse(Item()["item.json"])!; manifest[field] = "invalid";
        Assert.Contains(WorkshopItemManifestCodec.Read(Encoding.UTF8.GetBytes(manifest.ToJsonString())).Diagnostics, x => x.Code == "item.field.unknown");
    }
    [Fact] public void OversizePayloadRefusedBeforeParsing()
    {
        var item = Item(); item["card.json"] = new byte[2 * 1024 * 1024 + 1]; RehashItem(item);
        Assert.Contains(Read(item).Diagnostics, x => x.Code == "item.read" && x.Message.Contains("limit"));
    }
    [Theory] [InlineData("modelFile")] [InlineData("sourceHash")] [InlineData("packageId")] [InlineData("payloads")] [InlineData("animations")] [InlineData("icons")]
    public void InvalidBindingTypesReturnDiagnostics(string field)
    {
        var binding = JsonNode.Parse(Item()["client.bindings"])!; binding[field] = null;
        Assert.NotEmpty(CharacterPackageAssembler.ValidateClientBindings(Encoding.UTF8.GetBytes(binding.ToJsonString())));
    }
    [Fact] public void DuplicateAndMissingPortableFieldsRefused()
    {
        var binding = Encoding.UTF8.GetString(Item()["client.bindings"]);
        var duplicate = binding.Replace("\"sampleRate\":60", "\"sampleRate\":60,\"sampleRate\":60");
        Assert.Contains(CharacterPackageAssembler.ValidateClientBindings(Encoding.UTF8.GetBytes(duplicate)), x => x.Code == "portable.schema");
        var root = JsonNode.Parse(binding)!.AsObject(); root.Remove("modelFile");
        Assert.Contains(CharacterPackageAssembler.ValidateClientBindings(Encoding.UTF8.GetBytes(root.ToJsonString())), x => x.Code == "portable.schema");
    }
    [Fact] public void SymlinkPayloadRefused()
    {
        if (OperatingSystem.IsWindows()) return;
        var item = Item(); string folder = Path.Combine(Path.GetTempPath(), "fighter-link-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            foreach (var f in item) File.WriteAllBytes(Path.Combine(folder, f.Key), f.Value);
            File.Delete(Path.Combine(folder, "card.json")); File.CreateSymbolicLink(Path.Combine(folder, "card.json"), "/dev/zero");
            Assert.Contains(FighterItemAdmission.ReadLocalItem(folder).Diagnostics, x => x.Code == "item.read");
        }
        finally { Directory.Delete(folder, true); }
    }
    [Fact] public void MissingPayloadRefused()
    { var item = Item(); item.Remove("character.json"); Assert.Contains(Read(item).Diagnostics, x => x.Code == "item.payload.missing"); }
    [Fact] public void PortableNumberRoundTripsEveryFloatClass()
    {
        var random = new Random(674);
        for (int i = 0; i < 10000; i++)
        {
            int bits = random.Next() ^ (i % 2 == 0 ? int.MinValue : 0);
            float value = BitConverter.Int32BitsToSingle(bits);
            if (float.IsNaN(value) || float.IsInfinity(value)) continue;
            using var doc = System.Text.Json.JsonDocument.Parse(PortableCanonicalJson.FloatDecimal(value));
            Assert.Equal(value, doc.RootElement.GetSingle());
        }
    }
    [Theory] [InlineData("3.2", "3.20000005")] [InlineData("1e-45", "1.40129846E-45")]
    public void MonoAndCoreClrSpellingsProduceSameBytes(string a, string b)
    { Assert.Equal(PortableCanonicalJson.Normalize(Encoding.UTF8.GetBytes("[" + a + "]")), PortableCanonicalJson.Normalize(Encoding.UTF8.GetBytes("[" + b + "]"))); }
}
