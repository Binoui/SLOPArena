using System.Text;
using System.Text.Json.Nodes;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Shared.Tests;

public class WorkshopItemManifestTests
{
    private static JsonObject Valid() => new()
    {
        ["schemaVersion"] = 1, ["itemType"] = "model-swap", ["itemVersion"] = "1.0.0",
        ["payloads"] = new JsonObject { ["model.glb"] = WorkshopItemManifestCodec.HashPayload(new byte[] { 1, 2 }) },
        ["creator"] = "Creator", ["license"] = "CC0", ["attribution"] = "", ["aiAssisted"] = false, ["baseFighterPackageId"] = "bonk"
    };
    private static WorkshopItemManifestReadResult Read(JsonObject o) => WorkshopItemManifestCodec.Read(Encoding.UTF8.GetBytes(o.ToJsonString()));
    private static void Refuses(JsonObject o, string code) { var r = Read(o); Assert.False(r.IsAccepted); Assert.Null(r.Manifest); Assert.Contains(r.Diagnostics, x => x.Code == code); }
    [Fact] public void WriterRoundTripsCanonicalBytes() { var m = Read(Valid()).Manifest!; var bytes = WorkshopItemManifestCodec.Write(m); Assert.Equal(bytes, WorkshopItemManifestCodec.Write(WorkshopItemManifestCodec.Read(bytes).Manifest!)); }
    [Fact] public void PropertyOrderDoesNotChangeCanonicalBytes() { var o = Valid(); var reversed = new JsonObject(); foreach (var p in o.Reverse()) reversed.Add(p.Key, p.Value?.DeepClone()); Assert.Equal(WorkshopItemManifestCodec.Write(Read(o).Manifest!), WorkshopItemManifestCodec.Write(Read(reversed).Manifest!)); }
    [Fact] public void UnknownFieldsAreRefused() { var o = Valid(); o["other"] = 1; Refuses(o, "item.field.unknown"); }
    [Theory] [InlineData("schemaVersion")] [InlineData("itemType")] [InlineData("itemVersion")] [InlineData("payloads")] [InlineData("creator")] [InlineData("license")] [InlineData("attribution")] [InlineData("aiAssisted")] [InlineData("baseFighterPackageId")]
    public void MissingFieldsAreRefused(string field) { var o = Valid(); o.Remove(field); Refuses(o, "item.field.missing"); }
    [Fact] public void DuplicateFieldsAreRefused() { var s = Valid().ToJsonString().Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1"); Assert.Contains(WorkshopItemManifestCodec.Read(Encoding.UTF8.GetBytes(s)).Diagnostics, x => x.Code == "item.field.duplicate"); }
    [Fact] public void DuplicatePayloadFieldsAreRefused() { var s = Valid().ToJsonString().Replace("\"model.glb\":", "\"model.glb\":\"" + new string('a', 64) + "\",\"model.glb\":"); Assert.Contains(WorkshopItemManifestCodec.Read(Encoding.UTF8.GetBytes(s)).Diagnostics, x => x.Code == "item.field.duplicate"); }
    [Theory] [InlineData("schemaVersion")] [InlineData("itemType")] [InlineData("itemVersion")] [InlineData("payloads")] [InlineData("creator")] [InlineData("license")] [InlineData("attribution")] [InlineData("aiAssisted")] [InlineData("baseFighterPackageId")]
    public void WrongValueTypesAreRefused(string field) { var o = Valid(); o[field] = null; Refuses(o, "item.type"); }
    [Fact] public void UnsupportedSchemaIsRefused() { var o = Valid(); o["schemaVersion"] = 2; Refuses(o, "item.schema.unsupported"); }
    [Theory] [InlineData("fighter")] [InlineData("stage")] [InlineData("unknown")]
    public void UnsupportedItemTypesAreRefused(string type) { var o = Valid(); o["itemType"] = type; Refuses(o, "item.kind.unsupported"); }
    [Theory] [InlineData("")] [InlineData("../a.glb")] [InlineData("a..glb")] [InlineData("/a.glb")] [InlineData("C:a.glb")] [InlineData("a/b.glb")] [InlineData("a\\b.glb")]
    public void UnsafePayloadNamesAreRefused(string name) { var o = Valid(); o["payloads"] = new JsonObject { [name] = new string('a', 64) }; Refuses(o, "item.payload.name"); }
    [Theory] [InlineData("bad")] [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void MalformedHashesAreRefused(string hash) { var o = Valid(); o["payloads"]!["model.glb"] = hash; Refuses(o, "item.payload.hash"); }
    [Fact] public void NonStringHashIsRefused() { var o = Valid(); o["payloads"]!["model.glb"] = 1; Refuses(o, "item.type"); }
    [Fact] public void InvalidBaseIdIsRefused() { var o = Valid(); o["baseFighterPackageId"] = "Bad/ID"; Refuses(o, "item.base.invalid"); }
    [Fact] public void LongTextIsRefused() { var o = Valid(); o["creator"] = new string('a', WorkshopItemManifestCodec.MaxTextLength + 1); Refuses(o, "item.text.length"); }
    [Fact] public void OversizeManifestIsRefused() => Assert.Contains(WorkshopItemManifestCodec.Read(new byte[WorkshopItemManifestCodec.MaxManifestBytes + 1]).Diagnostics, x => x.Code == "item.size");
    [Fact] public void InvalidJsonIsRefused() => Assert.Contains(WorkshopItemManifestCodec.Read(Encoding.UTF8.GetBytes("{" )).Diagnostics, x => x.Code == "item.json");
    [Fact] public void NonObjectIsRefused() => Assert.Contains(WorkshopItemManifestCodec.Read(Encoding.UTF8.GetBytes("[]")).Diagnostics, x => x.Code == "item.type");
    [Fact] public void EmptyVersionIsRefused() { var o = Valid(); o["itemVersion"] = ""; Refuses(o, "item.version.invalid"); }
    [Fact] public void MultiplePayloadsAreRefused() { var o = Valid(); o["payloads"]!["second.glb"] = new string('a', 64); Refuses(o, "item.payload.shape"); }
    [Fact] public void NonGlbPayloadIsRefused() { var o = Valid(); o["payloads"] = new JsonObject { ["model.png"] = new string('a', 64) }; Refuses(o, "item.payload.shape"); }
    [Fact] public void MissingPayloadIsRefused() => Assert.Contains(WorkshopItemManifestCodec.Verify(Read(Valid()).Manifest!, new Dictionary<string, byte[]>()), x => x.Code == "item.payload.missing");
    [Fact] public void ExtraPayloadIsRefused() => Assert.Contains(WorkshopItemManifestCodec.Verify(Read(Valid()).Manifest!, new Dictionary<string, byte[]> { ["model.glb"] = new byte[] { 1, 2 }, ["other"] = Array.Empty<byte>() }), x => x.Code == "item.payload.extra");
    [Fact] public void HashMismatchIsRefused() => Assert.Contains(WorkshopItemManifestCodec.Verify(Read(Valid()).Manifest!, new Dictionary<string, byte[]> { ["model.glb"] = new byte[] { 3 } }), x => x.Code == "item.payload.mismatch");
    [Fact] public void CorrectPayloadVerifies() => Assert.Empty(WorkshopItemManifestCodec.Verify(Read(Valid()).Manifest!, new Dictionary<string, byte[]> { ["model.glb"] = new byte[] { 1, 2 } }));
    [Fact] public void MetadataIsCompactHashMap() { Assert.True(WorkshopItemManifestCodec.TryWriteSteamMetadata(Read(Valid()).Manifest!, out var s, out var d)); Assert.Null(d); Assert.Equal("{\"model.glb\":\"" + WorkshopItemManifestCodec.HashPayload(new byte[] { 1, 2 }) + "\"}", s); }
    [Fact] public void OversizeSteamMetadataIsRefused() { var o = Valid(); o["payloads"] = new JsonObject { [new string('界', 900) + ".glb"] = new string('a', 64) }; var r = Read(o); Assert.True(r.IsAccepted); Assert.False(WorkshopItemManifestCodec.TryWriteSteamMetadata(r.Manifest!, out var s, out var d)); Assert.Empty(s); Assert.Equal("item.metadata.size", d!.Code); }
    [Theory] [InlineData(4999, true)] [InlineData(5000, false)]
    public void SteamMetadataBoundaryCountsEncodedBytes(int size, bool accepted)
    {
        var o = Valid();
        o["payloads"] = new JsonObject { [new string('界', 820) + new string('a', size - 4995) + ".glb"] = new string('a', 64) };
        var m = Read(o).Manifest!;
        Assert.Equal(accepted, WorkshopItemManifestCodec.TryWriteSteamMetadata(m, out var metadata, out var diagnostic));
        if (accepted) Assert.Equal(size, Encoding.UTF8.GetByteCount(metadata));
        else Assert.Contains("5000", diagnostic!.Message);
    }
    [Fact] public void InvalidUtf8IsRefusedWithoutThrowing() { var b = Encoding.UTF8.GetBytes(Valid().ToJsonString()); b[2] = 255; Assert.False(WorkshopItemManifestCodec.Read(b).IsAccepted); }
    [Theory] [InlineData("fighter")] [InlineData("stage")]
    public void FutureTypesWithoutBaseFighterAreStillReportedAsUnsupported(string kind)
    { var o = Valid(); o["itemType"] = kind; o.Remove("baseFighterPackageId"); Refuses(o, "item.kind.unsupported"); }
}
