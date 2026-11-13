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
    internal static byte[] Glb(float[]? armatureRotation = null)
        => TestGlb.Build(new[] { TestGlb.Wave("fixture", 12), TestGlb.Wave("wave", 30, loop: true) }, armatureRotation);

    internal static byte[] HumanoidJson()
    {
        var bones = new JsonArray();
        foreach (var (human, mixamo) in HumanoidBoneMap.MixamoNames.Where(x => !x.Mixamo.Contains("Hand") || x.Mixamo.EndsWith("Hand") || x.Mixamo.EndsWith("Middle1")))
            bones.Add(new JsonObject { ["human"] = human, ["node"] = TestGlb.P + mixamo });
        return Encoding.UTF8.GetBytes(new JsonObject { ["bones"] = bones }.ToJsonString());
    }

    internal static Dictionary<string, byte[]> Item(string id = "local.tumbleweed", bool media = false, string kit = "Tumbleweed", bool midpoint = false,
        Func<string, JsonObject>? animation = null, byte[]? model = null, string[]? attachments = null, byte[]? humanoid = null)
    {
        string root = CookedPackageEdits.Root;
        var sourceFiles = new[] { "package.json", "character.json" }.ToDictionary(x => x,
            x => File.ReadAllBytes(Path.Combine(root, kit == "Tumbleweed" ? "tests/Shared.Tests/Fixtures/Tumbleweed" : "client/Unity/Assets/CharacterPackages/" + kit, x)));
        var package = JsonNode.Parse(sourceFiles["package.json"])!;
        package["packageId"] = id;
        sourceFiles["package.json"] = Encoding.UTF8.GetBytes(package.ToJsonString());
        if (attachments != null)
        {
            var source = JsonNode.Parse(sourceFiles["character.json"])!;
            source["attachmentBoneIds"] = new JsonArray(attachments.Select(x => JsonValue.Create(x)).ToArray());
            sourceFiles["character.json"] = Encoding.UTF8.GetBytes(source.ToJsonString());
        }
        if (midpoint)
        {
            var source = JsonNode.Parse(sourceFiles["character.json"])!;
            source["movement"]!["airAccelBase"] = JsonNode.Parse("1.00000005960464477539062500000001");
            source["movement"]!["airFloatGravity"] = JsonNode.Parse("-0.0");
            sourceFiles["character.json"] = Encoding.UTF8.GetBytes(source.ToJsonString());
        }
        var compiled = CharacterPackageCompiler.CompilePortable(Encoding.UTF8.GetString(sourceFiles["package.json"]), Encoding.UTF8.GetString(sourceFiles["character.json"]));
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        var required = CharacterPackageAssembler.RequiredAnimations(compiled.CookedPackage!, new List<CharacterDiagnostic>());
        var animations = required.ToDictionary(x => x, x => animation?.Invoke(x) ?? new JsonObject { ["kind"] = "glb", ["clipName"] = "fixture" }, StringComparer.Ordinal);
        Dictionary<string, byte[]>? images = null;
        string?[]? portraits = null;
        IReadOnlyDictionary<string, string?>[]? icons = null;
        if (media)
        {
            var png = new byte[33];
            new byte[] {137,80,78,71,13,10,26,10,0,0,0,13,73,72,68,82}.CopyTo(png, 0);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16), 8);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20), 8);
            images = new Dictionary<string, byte[]> { ["portrait.png"] = png, ["move.png"] = (byte[])png.Clone() };
            var definition = compiled.CookedPackage!.Definition;
            var forms = definition.AlternateForm == null ? new[] { definition.Slots } : new[] { definition.Slots, definition.AlternateForm.Slots };
            portraits = forms.Select((_, form) => form == 0 ? "portrait.png" : null).ToArray();
            icons = forms.Select(slots => (IReadOnlyDictionary<string, string?>)slots.Select(slot => slot.IconId).Where(x => !string.IsNullOrEmpty(x)).Distinct()
                .ToDictionary(x => x, _ => (string?)"move.png")).ToArray();
        }
        var (item, diagnostics) = PortableFighterCook.Cook(new PortableFighterSource(sourceFiles["package.json"], sourceFiles["character.json"],
            Encoding.UTF8.GetBytes("{\"description\":\"Tumbleweed test kit\",\"workInProgress\":true,\"tutorialHint\":null}"), "tumbleweed.glb", model ?? Glb(),
            animations, "test", "CC0", "Tumbleweed proof kit; synthetic fixture media", false, humanoid ?? (media ? HumanoidJson() : null), images, portraits, icons));
        Assert.True(item != null, string.Join("\n", diagnostics));
        return item!;
    }

    internal static void RehashItem(Dictionary<string, byte[]> files)
        => PortableFighterCook.WriteItemManifest(files, "test", "CC0", "Tumbleweed proof kit; synthetic fixture media", false);

    private static void Repin(Dictionary<string, byte[]> item)
    {
        var binding = JsonNode.Parse(item["client.bindings"])!;
        foreach (var name in binding["payloads"]!.AsObject().Select(x => x.Key).ToArray())
            binding["payloads"]![name] = WorkshopItemManifestCodec.HashPayload(item[name]);
        string sourceHash = PortableSourceHash.Compute(item); binding["sourceHash"] = sourceHash;
        var manifest = JsonNode.Parse(item["manifest.json"])!; manifest["sourceHash"] = sourceHash;
        item["manifest.json"] = Encoding.UTF8.GetBytes(manifest.ToJsonString());
        item["client.bindings"] = Encoding.UTF8.GetBytes(binding.ToJsonString());
        item["manifest.json"] = CharacterPackageAssembler.RehashManifest(item);
        RehashItem(item);
    }

    internal static FighterItemAdmissionResult ReadItem(Dictionary<string, byte[]> files)
    {
        string path = Path.Combine(Path.GetTempPath(), "fighter-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try
        {
            foreach (var f in files) File.WriteAllBytes(Path.Combine(path, f.Key), f.Value);
            return FighterItemAdmission.ReadLocalItem(path);
        }
        finally { Directory.Delete(path, true); }
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

    [Fact] public void TumbleweedIsAdmittedWhenItsBakeVerifies()
    {
        var item = Item(midpoint: Environment.GetEnvironmentVariable("SLOP_PORTABLE_PROOF") != null); var result = Read(item);
        if (Environment.GetEnvironmentVariable("SLOP_PORTABLE_PROOF") is { } output)
        {
            Directory.CreateDirectory(output);
            foreach (var f in item) File.WriteAllBytes(Path.Combine(output, f.Key), f.Value);
        }
        Assert.True(result.IsAccepted, string.Join("\n", result.Diagnostics));
        Assert.Empty(result.Diagnostics);
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
    [Fact]
    public void PublishRecooksVerifiedSourceUnderSteamIdentityWithoutChangingLocalCopy()
    {
        var local = Read(Item(media: true));
        Assert.True(local.IsAccepted);
        var original = local.Item!.ReadPayload("package.json");
        var published = local.Item.RecookForSteam(42);
        Assert.NotNull(published.Item);
        var downloaded = Read(published.Item!, 42);
        Assert.True(downloaded.IsAccepted, string.Join("; ", downloaded.Diagnostics));
        Assert.Equal("workshop.42", downloaded.Fighter!.Choose().Revision.PackageId);
        Assert.Equal(original, local.Item.ReadPayload("package.json"));
        Assert.Equal("local.tumbleweed", local.Fighter!.Choose().Revision.PackageId);
        Assert.False(Read(published.Item!, 43).IsAccepted);
        Assert.False(Read(published.Item!).IsAccepted);
        var again = local.Item.RecookForSteam(42);
        foreach (var file in published.Item!) Assert.Equal(file.Value, again.Item![file.Key]);
    }
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
        root["animations"]![0]!["source"] = new JsonObject { ["kind"] = "shared", ["id"] = "slop.anim.idle.default.v1", ["version"] = "1.0.0" };
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
    [Fact] public void IncludedSourceIsNotRuntimeAuthority()
    {
        var item = Item(); var source = JsonNode.Parse(item["character.json"])!; source["displayName"] = "Different";
        item["character.json"] = Encoding.UTF8.GetBytes(source.ToJsonString()); Repin(item);
        Assert.True(Read(item).IsAccepted);
    }
    [Fact] public void IncludedSourceVersionIsEditable()
    {
        var item = Item(); var source = JsonNode.Parse(item["package.json"])!; source["version"] = "9.0.0";
        item["package.json"] = Encoding.UTF8.GetBytes(source.ToJsonString()); Repin(item);
        Assert.True(Read(item).IsAccepted);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void PoseAndClipReferencesMustExist(bool pose)
    {
        var item = Item(); var binding = JsonNode.Parse(item["client.bindings"])!;
        if (pose) binding["animations"]![0]!["frameCount"] = 2;
        else binding["animations"]![0]!["source"]!["clipName"] = "missing";
        item["client.bindings"] = Encoding.UTF8.GetBytes(binding.ToJsonString()); Repin(item);
        Assert.Contains(Read(item).Diagnostics, x => x.Code == (pose ? "package.pose.frame-count-mismatch" : "portable.clip.missing"));
    }
    [Fact] public void InvalidCardAndHumanoidFieldsRefused()
    {
        var item = Item(media: true); item["humanoid.json"] = Encoding.UTF8.GetBytes("{\"bones\":[],\"unknown\":1}"); Repin(item);
        Assert.Contains(Read(item).Diagnostics, x => x.Code == "item.humanoid");
        item = Item(); item["card.json"] = Encoding.UTF8.GetBytes("{\"description\":\"x\",\"workInProgress\":false,\"tutorialHint\":{\"starter\":\"warp\",\"followUp\":\"1\",\"jump\":false}}"); Repin(item);
        Assert.Contains(Read(item).Diagnostics, x => x.Code == "item.card");
    }
    [Fact] public void ImageDimensionsAreBounded()
    {
        var item = Item(media: true);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(item["portrait.png"].AsSpan(16), 100000);
        Repin(item); Assert.Contains(Read(item).Diagnostics, x => x.Code == "item.image.limit");
    }
    [Fact] public void ZeroSteamIdIsRefused() => Assert.Contains(Read(Item("workshop.42"), 0).Diagnostics, x => x.Code == "item.origin");
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
