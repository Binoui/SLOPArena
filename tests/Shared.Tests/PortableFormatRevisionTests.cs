using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SlopArena.Client.Workshop;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class PortableFormatRevisionTests
{
    private static JsonObject Bindings(Dictionary<string, byte[]> item) => JsonNode.Parse(item["client.bindings"])!.AsObject();
    private static IReadOnlyList<CharacterDiagnostic> Validate(JsonObject binding) => CharacterPackageAssembler.ValidateClientBindings(Encoding.UTF8.GetBytes(binding.ToJsonString()));
    private static void Repin(Dictionary<string, byte[]> item, JsonObject? binding = null)
    {
        binding ??= Bindings(item);
        var hash = PortableSourceHash.Compute(item); binding["sourceHash"] = hash;
        foreach (var f in binding["payloads"]!.AsObject().ToArray()) binding["payloads"]![f.Key] = WorkshopItemManifestCodec.HashPayload(item[f.Key]);
        item["client.bindings"] = Encoding.UTF8.GetBytes(binding.ToJsonString());
        var manifest = JsonNode.Parse(item["manifest.json"])!; manifest["sourceHash"] = hash;
        item["manifest.json"] = Encoding.UTF8.GetBytes(manifest.ToJsonString());
        item["manifest.json"] = CharacterPackageAssembler.RehashManifest(item);
        FighterItemAdmissionTests.RehashItem(item);
    }
    private static T Folder<T>(Dictionary<string, byte[]> item, Func<string, T> action)
    {
        string path = Path.Combine(Path.GetTempPath(), "portable-revision-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path);
        try { foreach (var f in item) File.WriteAllBytes(Path.Combine(path, f.Key), f.Value); return action(path); }
        finally { Directory.Delete(path, true); }
    }
    private static FighterItemAdmissionResult Read(Dictionary<string, byte[]> item) => Folder(item, FighterItemAdmission.ReadLocalItem);

    [Fact] public void OldCompilerAndFutureSourceFieldsCannotRefuseValidCookedRuntime()
    {
        var item = FighterItemAdmissionTests.Item();
        var source = JsonNode.Parse(item["character.json"])!; source["futureFeature"] = new JsonObject { ["anything"] = 1 };
        item["character.json"] = Encoding.UTF8.GetBytes(source.ToJsonString()); Repin(item);
        var outer = JsonNode.Parse(item["item.json"])!; outer["compilerVersion"] = "0.9.0"; outer["canonicalWriterVersion"] = 7;
        item["item.json"] = Encoding.UTF8.GetBytes(outer.ToJsonString());
        var result = Read(item);
        Assert.Equal("item.pose-bake.unavailable", Assert.Single(result.Diagnostics).Code);
        var cooked = item.Where(f => CookedPackageLimits.FileBytes(f.Key) != 0).ToDictionary(f => f.Key, f => f.Value);
        Assert.True(CookedCharacterPackageLoader.LoadFiles(cooked, CookedPackageEdits.RequirementOf(cooked), CharacterCookProfile.Workshop).IsValid);
    }
    [Fact] public void OutsideApiWindowNamesOriginalProducer()
    {
        var item = FighterItemAdmissionTests.Item();
        foreach (string file in new[] { "manifest.json", "character.runtime.json" })
        {
            var root = JsonNode.Parse(item[file])!;
            if (file == "manifest.json") root["runtimeApiMin"] = "999.0.0";
            else
            {
                root["metadata"]!["compatibility"]!["runtimeApiMin"] = "999.0.0";
                root["character"]!["futureFieldFromNewerApi"] = 1;
            }
            item[file] = Encoding.UTF8.GetBytes(root.ToJsonString());
        }
        Repin(item);
        var outer = JsonNode.Parse(item["item.json"])!; outer["compilerVersion"] = "0.8.0"; item["item.json"] = Encoding.UTF8.GetBytes(outer.ToJsonString());
        Assert.Contains(Read(item).Diagnostics, d => d.Code == "item.needs-update" && d.Message.Contains("0.8.0") && d.Message.Contains("writer 1"));
    }
    [Theory]
    [InlineData("1.00000005960464477539062500000001", 0x3f800001)]
    [InlineData("1.000000059604644775390625", 0x3f800000)]
    [InlineData("1.00000005960464477539062499999999", 0x3f800000)]
    [InlineData("-1.00000005960464477539062500000001", unchecked((int)0xbf800001))]
    [InlineData("-0.0", 0)]
    [InlineData("1.4012984643248170709237295832899161312802619418765e-45", 1)]
    public void SourceDecimalRoundsOnce(string value, int bits)
    {
        Assert.Equal(bits, BitConverter.SingleToInt32Bits(PortableDecimal.Parse(value)));
        var item = FighterItemAdmissionTests.Item();
        string source = Encoding.UTF8.GetString(item["character.json"]);
        var node = JsonNode.Parse(source)!; node["movement"]!["airAccelBase"] = JsonNode.Parse(value);
        var parsed = CharacterPackageSourceCodec.LoadPortable(Encoding.UTF8.GetString(item["package.json"]), node.ToJsonString());
        Assert.True(parsed.IsValid); Assert.Equal(bits, BitConverter.SingleToInt32Bits(parsed.Source!.Character.Movement.AirAccelBase));
    }
    [Fact] public void NegativeZeroIsNormalizedInTheCookedDefinition()
    {
        var item = FighterItemAdmissionTests.Item(); var source = JsonNode.Parse(item["character.json"])!; source["movement"]!["airFloatGravity"] = JsonNode.Parse("-0.0");
        var result = CharacterPackageCompiler.CompilePortable(Encoding.UTF8.GetString(item["package.json"]), source.ToJsonString());
        Assert.False(result.HasErrors); Assert.Equal(0, BitConverter.SingleToInt32Bits(result.CookedPackage!.Definition.Movement.AirFloatGravity));
    }
    [Fact] public void BashieSourceAssemblesWithEveryPresentationExplicitlyNone()
    {
        var item = FighterItemAdmissionTests.Item("local.bashie", kit: "bashie");
        var binding = Bindings(item); Assert.NotEmpty(binding["presentations"]!.AsObject());
        Assert.All(binding["presentations"]!.AsObject(), p => Assert.Equal("none", p.Value!["kind"]!.GetValue<string>()));
        Assert.Equal("item.pose-bake.unavailable", Assert.Single(Read(item).Diagnostics).Code);
        binding["presentations"]!.AsObject().Remove(binding["presentations"]!.AsObject().First().Key); Repin(item, binding);
        Assert.Contains(Read(item).Diagnostics, d => d.Code is "portable.runtime" or "package.binding.presentation-missing");
    }
    [Theory] [InlineData("slop.anim.idle.v1", "1.0.0", "portable.shared.unsupported")]
    [InlineData("slop.anim.idle", "1.0.0", "portable.schema")]
    [InlineData("slop.anim.idle.v1", "2.0.0", "portable.schema")]
    [InlineData("slop.anim.idle.v1", "1.x", "portable.schema")]
    [InlineData("slop.vfx.idle.v1", "1.0.0", "portable.schema")]
    public void SharedReferenceGrammarAndExactVersion(string id, string version, string code)
    {
        var binding = Bindings(FighterItemAdmissionTests.Item()); binding["animations"]![0]!["source"] = new JsonObject { ["kind"] = "shared", ["id"] = id, ["version"] = version };
        Assert.Contains(Validate(binding), d => d.Code == code);
    }
    [Theory] [InlineData("slop.vfx.hit.heavy.v1", "1.0.0", "portable.presentation.unsupported")]
    [InlineData("slop.vfx.hit.heavy.v1", "1.*", "portable.schema")]
    [InlineData("Assets/hit.prefab", "1.0.0", "portable.schema")]
    public void PresentationReferencesAreVersioned(string id, string version, string code)
    {
        var binding = Bindings(FighterItemAdmissionTests.Item()); binding["presentations"]!["cue.hit"] = new JsonObject
        { ["kind"] = "engine", ["vfx"] = new JsonObject { ["id"] = id, ["version"] = version }, ["audio"] = null, ["projectile"] = null };
        Assert.Contains(Validate(binding), d => d.Code == code);
    }
    [Theory] [InlineData("portraits")] [InlineData("icons")]
    public void ArtworkFormCountMustMatchRuntime(string field)
    {
        var item = FighterItemAdmissionTests.Item(); var binding = Bindings(item); binding[field]!.AsArray().Add(field == "icons" ? new JsonObject() : null); Repin(item, binding);
        Assert.Contains(Read(item).Diagnostics, d => d.Code is "portable.forms" or "portable.runtime");
    }
    [Fact] public void UnknownIconAndMissingDeclaredIconRefused()
    {
        var binding = Bindings(FighterItemAdmissionTests.Item()); binding["icons"]![0]!["jab"] = null;
        Assert.Contains(Validate(binding), d => d.Code == "portable.icon");
        var item = FighterItemAdmissionTests.Item(); binding = Bindings(item); binding["icons"]![0]!.AsObject().Remove(binding["icons"]![0]!.AsObject().First().Key); Repin(item, binding);
        Assert.Contains(Read(item).Diagnostics, d => d.Code == "portable.runtime");
    }
    [Fact] public void SourceHashIsCheckedAgainstBytes()
    {
        var item = FighterItemAdmissionTests.Item(); var binding = Bindings(item); binding["sourceHash"] = new string('a', 64);
        var manifest = JsonNode.Parse(item["manifest.json"])!; manifest["sourceHash"] = new string('a', 64);
        item["manifest.json"] = Encoding.UTF8.GetBytes(manifest.ToJsonString()); item["client.bindings"] = Encoding.UTF8.GetBytes(binding.ToJsonString());
        item["manifest.json"] = CharacterPackageAssembler.RehashManifest(item); FighterItemAdmissionTests.RehashItem(item);
        Assert.Contains(Read(item).Diagnostics, d => d.Code == "item.source.hash");
    }
    [Fact] public void FifoAndDeviceNeverBlockOrEscapeAdmission()
    {
        if (!OperatingSystem.IsLinux()) return;
        Folder(FighterItemAdmissionTests.Item(), path =>
        {
            string card = Path.Combine(path, "card.json"); File.Delete(card); Assert.Equal(0, MkFifo(card, 0x180));
            var read = System.Threading.Tasks.Task.Run(() => FighterItemAdmission.ReadLocalItem(path));
            Assert.True(read.Wait(TimeSpan.FromSeconds(2)), "FIFO read blocked."); Assert.Contains(read.Result.Diagnostics, d => d.Code == "item.read");
            return true;
        });
        long total = 0; Assert.Throws<InvalidDataException>(() => PortableItemFiles.Read("/dev/zero", 10, ref total));
    }
    [Fact] public void HardLinksAreHarmlessPinnedBytes()
    {
        if (!OperatingSystem.IsLinux()) return;
        Folder(FighterItemAdmissionTests.Item(), path =>
        {
            string card = Path.Combine(path, "card.json"), copy = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            File.Move(card, copy);
            try { Assert.Equal(0, Link(copy, card)); Assert.Contains(FighterItemAdmission.ReadLocalItem(path).Diagnostics, d => d.Code == "item.pose-bake.unavailable"); }
            finally { File.Delete(copy); }
            return true;
        });
    }
    [Fact] public void LargeAnimationArraysRefuseBeforeClipParsing()
    {
        var binding = Bindings(FighterItemAdmissionTests.Item()); var animation = binding["animations"]![0]!.DeepClone(); binding["animations"] = new JsonArray();
        for (int i = 0; i < 600; i++) binding["animations"]!.AsArray().Add(animation.DeepClone());
        Assert.Contains(Validate(binding), d => d.Code == "portable.limit");
    }
    [Fact] public void AcceptedManifestAlwaysFitsSteamMetadataIncludingWorstNames()
    {
        var original = JsonNode.Parse(FighterItemAdmissionTests.Item()["item.json"])!;
        for (int length = 8; length <= 128; length += 8)
        {
            var manifest = original.DeepClone();
            for (int i = 0; i < 32; i++) manifest["payloads"]![new string('a', length - 8) + i.ToString("D4") + ".png"] = new string('a', 64);
            var read = WorkshopItemManifestCodec.Read(Encoding.UTF8.GetBytes(manifest.ToJsonString()));
            if (read.IsAccepted) { Assert.True(WorkshopItemManifestCodec.TryWriteSteamMetadata(read.Manifest!, out var text, out _)); Assert.True(Encoding.UTF8.GetByteCount(text) < 5000); }
            else Assert.Contains(read.Diagnostics, d => d.Code == "item.metadata.size");
        }
    }
    [Fact] public void TotalItemSizeIsBoundedBeforeAllocation()
    {
        Folder(FighterItemAdmissionTests.Item(), path =>
        {
            long total = PortableItemFiles.TotalBytes;
            Assert.Throws<InvalidDataException>(() => PortableItemFiles.Read(Path.Combine(path, "item.json"), 16384, ref total)); return true;
        });
    }
    [Fact] public void FileExtensionsHaveOneLowercaseRuleForFighters()
    {
        var item = FighterItemAdmissionTests.Item(); var outer = JsonNode.Parse(item["item.json"])!;
        var hash = outer["payloads"]!["tumbleweed.glb"]!.DeepClone(); outer["payloads"]!.AsObject().Remove("tumbleweed.glb"); outer["payloads"]!["tumbleweed.GLB"] = hash;
        Assert.Contains(WorkshopItemManifestCodec.Read(Encoding.UTF8.GetBytes(outer.ToJsonString())).Diagnostics, d => d.Code == "item.payload.shape");
    }
    [Fact] public void ModelSwapLibraryRejectsFighterBeforeBaseLookup()
    { Folder(FighterItemAdmissionTests.Item(), path => { Assert.Contains("type not supported here", Assert.Throws<InvalidDataException>(() => WorkshopLibrary.Verify(path)).Message); return true; }); }
    [Fact] public void HumanoidCapMatchesModelSwapCap()
    {
        var item = FighterItemAdmissionTests.Item(media: true); item["humanoid.json"] = new byte[65537]; Repin(item);
        Assert.Contains(Read(item).Diagnostics, d => d.Code == "item.read" && d.Message.Contains("65536"));
    }
    [Fact] public void MultipleJpegFramesAreRefusedIncludingAfterEntropy()
    {
        byte[] frame = {255,192,0,8,8,0,8,0,8,1};
        byte[] first = new byte[] {255,216}.Concat(frame).Concat(new byte[] {255,217}).ToArray();
        Assert.Empty(WorkshopGlbPrecheck.CheckImage(first));
        var twice = new byte[] {255,216}.Concat(frame).Concat(frame).Concat(new byte[] {255,217}).ToArray();
        Assert.Contains(WorkshopGlbPrecheck.CheckImage(twice), d => d.Code == "item.jpeg");
        var entropy = new byte[] {255,216}.Concat(frame).Concat(new byte[] {255,218,0,2,1,2,255,0,3}).Concat(frame).Concat(new byte[] {255,217}).ToArray();
        Assert.Contains(WorkshopGlbPrecheck.CheckImage(entropy), d => d.Code == "item.jpeg");
    }
    [Fact] public void AdapterRejectsTrustedInternalCapabilityDeclarations()
    {
        var item = FighterItemAdmissionTests.Item();
        var capability = JsonNode.Parse(CookedPackageEdits.Shipped("fightguy")["manifest.json"])!["capabilityRequirements"]!.AsArray()
            .First(x => x!["capabilityId"]!.GetValue<string>().StartsWith("slop.internal.", StringComparison.Ordinal))!;
        var manifest = JsonNode.Parse(item["manifest.json"])!;
        manifest["capabilityRequirements"]!.AsArray().Add(capability.DeepClone());
        item["manifest.json"] = Encoding.UTF8.GetBytes(manifest.ToJsonString());
        var runtime = JsonNode.Parse(item["character.runtime.json"])!;
        runtime["character"]!["capabilityRequirements"]!.AsArray().Add(capability.DeepClone());
        item["character.runtime.json"] = Encoding.UTF8.GetBytes(runtime.ToJsonString()); Repin(item);
        Assert.Contains(Read(item).Diagnostics, d => d.Code == "capability.untrusted");
    }
    [Fact] public void NyxPortableArtworkHasTwoIndependentForms()
    {
        var item = FighterItemAdmissionTests.Item("local.nyx", kit: "nyx"); var binding = Bindings(item);
        Assert.Equal(2, binding["portraits"]!.AsArray().Count); Assert.Equal(2, binding["icons"]!.AsArray().Count);
        Assert.Contains(Read(item).Diagnostics, d => d.Code == "item.pose-bake.unavailable");
        binding["icons"]![1]!["icon.unknown"] = null; Repin(item, binding);
        Assert.Contains(Read(item).Diagnostics, d => d.Code == "portable.icon");
    }
    [Theory] [InlineData("fixed", 2, "portable.schema")] [InlineData("cycle", 2, "portable.presentation.unsupported")]
    public void AudioPoolsAndProjectileReferencesSharePresentationShape(string selection, int count, string code)
    {
        var binding = Bindings(FighterItemAdmissionTests.Item()); var clips = new JsonArray();
        for (int i = 0; i < count; i++) clips.Add(new JsonObject { ["id"] = "slop.sfx.hit.default.v1", ["version"] = "1.0.0" });
        binding["presentations"]!["cue.hit"] = new JsonObject { ["kind"] = "engine", ["vfx"] = null,
            ["projectile"] = new JsonObject { ["id"] = "slop.vfx.projectile.default.v1", ["version"] = "1.0.0" },
            ["audio"] = new JsonObject { ["selection"] = selection, ["clips"] = clips } };
        Assert.Contains(Validate(binding), d => d.Code == code);
    }
    [DllImport("libc", EntryPoint="mkfifo")] private static extern int MkFifo(string path, uint mode);
    [DllImport("libc", EntryPoint="link")] private static extern int Link(string source, string target);
}
