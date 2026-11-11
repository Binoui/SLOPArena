using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Shared.Tests;

public class WorkshopGlbPrecheckTests
{
    private static JsonObject Valid() => JsonNode.Parse("""
        {"asset":{"version":"2.0"},"buffers":[{"byteLength":36}],"bufferViews":[{"buffer":0,"byteLength":36}],
        "accessors":[{"count":3}],"meshes":[{"primitives":[{"attributes":{"POSITION":0}}]}],
        "nodes":[{"mesh":0,"skin":0}],"skins":[{"joints":[0]}],"materials":[{}],"images":[{"bufferView":0,"mimeType":"image/png"}]}
        """)!.AsObject();
    private static byte[] Png(int w = 16, int h = 8) { var b = new byte[36]; new byte[] { 137,80,78,71,13,10,26,10,0,0,0,13,73,72,68,82 }.CopyTo(b, 0); BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(16), w); BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(20), h); return b; }
    private static byte[] Glb(JsonObject? o = null, byte[]? bin = null) => Container(Encoding.UTF8.GetBytes((o ?? Valid()).ToJsonString()), bin ?? Png());
    private static byte[] Container(byte[] json, byte[] bin)
    {
        int j = (json.Length + 3) & ~3, b = (bin.Length + 3) & ~3;
        var bytes = new byte[28 + j + b];
        void U(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
        U(0, 0x46546c67); U(4, 2); U(8, (uint)bytes.Length); U(12, (uint)j); U(16, 0x4e4f534a);
        bytes.AsSpan(20, j).Fill(32); json.CopyTo(bytes, 20); U(20 + j, (uint)b); U(24 + j, 0x004e4942); bin.CopyTo(bytes, 28 + j); return bytes;
    }
    private static void Refuses(byte[] b, string code, WorkshopGlbLimits? limits = null) { var r = WorkshopGlbPrecheck.Check(b, limits ?? WorkshopGlbLimits.Default); Assert.False(r.IsAccepted); Assert.Contains(r.Diagnostics, x => x.Code == code); }
    [Fact] public void MeasuresValidSkinnedGlb() { var r = WorkshopGlbPrecheck.Check(Glb(), WorkshopGlbLimits.Default); Assert.True(r.IsAccepted, string.Join(",", r.Diagnostics)); Assert.Equal(1, r.Measurements.Triangles); Assert.Equal(3, r.Measurements.Vertices); Assert.Equal(1, r.Measurements.Nodes); Assert.Equal(1, r.Measurements.MaxJoints); Assert.Equal(1, r.Measurements.Materials); Assert.Equal(1, r.Measurements.Images); Assert.Equal(new WorkshopGlbImageSize(16, 8), Assert.Single(r.Measurements.ImageSizes)); }
    [Fact] public void IndexedPrimitiveUsesIndexCount() { var o = Valid(); o["accessors"]!.AsArray().Add(new JsonObject { ["count"] = 12 }); o["meshes"]![0]!["primitives"]![0]!["indices"] = 1; var r = WorkshopGlbPrecheck.Check(Glb(o), WorkshopGlbLimits.Default); Assert.True(r.IsAccepted); Assert.Equal(4, r.Measurements.Triangles); Assert.Equal(3, r.Measurements.Vertices); }
    [Theory] [InlineData(5)] [InlineData(6)] public void StripAndFanTrianglesAreCounted(int mode) { var o = Valid(); o["accessors"]![0]!["count"] = 8; o["meshes"]![0]!["primitives"]![0]!["mode"] = mode; Assert.Equal(6, WorkshopGlbPrecheck.Check(Glb(o), WorkshopGlbLimits.Default).Measurements.Triangles); }
    [Fact] public void BadMagicIsRefused() { var b = Glb(); b[0] = 0; Refuses(b, "glb.container"); }
    [Fact] public void BadVersionIsRefused() { var b = Glb(); b[4] = 1; Refuses(b, "glb.container"); }
    [Fact] public void WrongFileLengthIsRefused() { var b = Glb(); b[8] ^= 1; Refuses(b, "glb.container"); }
    [Fact] public void OversizeChunkIsRefused() { var b = Glb(); BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(12), uint.MaxValue - 3); Refuses(b, "glb.container"); }
    [Fact] public void UnalignedChunkIsRefused() { var b = Glb(); b[12] |= 1; Refuses(b, "glb.container"); }
    [Fact] public void WrongFirstChunkIsRefused() { var b = Glb(); b[16] = 0; Refuses(b, "glb.container"); }
    [Fact] public void InvalidJsonIsRefused() => Refuses(Container(Encoding.UTF8.GetBytes("{"), Png()), "glb.json");
    [Fact] public void DeepJsonIsRefused() { var o = Valid(); o["extras"] = JsonNode.Parse(new string('[', 20) + "0" + new string(']', 20)); Refuses(Glb(o), "glb.json", WorkshopGlbLimits.Default with { MaxJsonDepth = 8 }); }
    [Fact] public void JsonByteLimitIsEnforced() => Refuses(Glb(), "glb.limit.json-bytes", WorkshopGlbLimits.Default with { MaxJsonBytes = 4 });
    [Fact] public void FileByteLimitIsEnforced() => Refuses(Glb(), "glb.limit.file-bytes", WorkshopGlbLimits.Default with { MaxFileBytes = 1 });
    [Fact] public void TriangleLimitIsEnforced() => Refuses(Glb(), "glb.limit.triangles", WorkshopGlbLimits.Default with { MaxTriangles = 0 });
    [Fact] public void VertexLimitIsEnforced() => Refuses(Glb(), "glb.limit.vertices", WorkshopGlbLimits.Default with { MaxVertices = 2 });
    [Fact] public void NodeLimitIsEnforced() => Refuses(Glb(), "glb.limit.nodes", WorkshopGlbLimits.Default with { MaxNodes = 0 });
    [Fact] public void JointLimitIsEnforced() => Refuses(Glb(), "glb.limit.joints", WorkshopGlbLimits.Default with { MaxJoints = 0 });
    [Fact] public void MaterialLimitIsEnforced() => Refuses(Glb(), "glb.limit.materials", WorkshopGlbLimits.Default with { MaxMaterials = 0 });
    [Fact] public void ImageLimitIsEnforced() => Refuses(Glb(), "glb.limit.images", WorkshopGlbLimits.Default with { MaxImages = 0 });
    [Fact] public void ImageDimensionLimitIsEnforced() => Refuses(Glb(), "glb.limit.image-dimension", WorkshopGlbLimits.Default with { MaxImageDimension = 15 });
    [Fact] public void ImagePixelLimitIsEnforced() => Refuses(Glb(), "glb.limit.image-pixels", WorkshopGlbLimits.Default with { MaxImagePixels = 127 });
    [Fact] public void LimitDiagnosticNamesLimitAndMeasurement() { var r = WorkshopGlbPrecheck.Check(Glb(), WorkshopGlbLimits.Default with { MaxVertices = 2 }); Assert.Contains(r.Diagnostics, x => x.Code == "glb.limit.vertices" && x.Message.Contains("2") && x.Message.Contains("3")); }
    [Fact] public void MissingSkinnedMeshIsRefused() { var o = Valid(); o["nodes"]![0]!.AsObject().Remove("skin"); Refuses(Glb(o), "glb.skin.missing"); }
    [Fact] public void EmptySkinIsRefused() { var o = Valid(); o["skins"]![0]!["joints"] = new JsonArray(); Refuses(Glb(o), "glb.skin.missing"); }
    [Theory] [InlineData("buffers", "file.bin")] [InlineData("buffers", "data:application/octet-stream;base64,AA==")] [InlineData("images", "file.png")] [InlineData("images", "data:image/png;base64,AA==")]
    public void ResourceUrisAreRefused(string array, string uri) { var o = Valid(); o[array]![0]!["uri"] = uri; Refuses(Glb(o), "glb.uri"); }
    [Fact] public void BufferOutsideFileIsRefused() { var o = Valid(); o["buffers"]![0]!["byteLength"] = 1000; Refuses(Glb(o), "glb.buffer.bounds"); }
    [Fact] public void ImageViewOutsideFileIsRefused() { var o = Valid(); o["bufferViews"]![0]!["byteOffset"] = 35; Refuses(Glb(o), "glb.buffer.bounds"); }
    [Fact] public void UnsupportedImageFormatIsRefused() => Refuses(Glb(bin: new byte[36]), "glb.image.format");
    [Fact] public void MimeMismatchIsRefused() { var o = Valid(); o["images"]![0]!["mimeType"] = "image/ktx2"; Refuses(Glb(o), "glb.image.format"); }
    [Theory] [InlineData("KHR_draco_mesh_compression")] [InlineData("EXT_meshopt_compression")] [InlineData("KHR_texture_basisu")]
    public void RequiredExtensionsAreRefusedByName(string name) { var o = Valid(); o["extensionsRequired"] = new JsonArray(name); var r = WorkshopGlbPrecheck.Check(Glb(o), WorkshopGlbLimits.Default); Assert.Contains(r.Diagnostics, x => x.Code == "glb.extension.required" && x.Message.Contains(name)); }
    [Fact] public void MeshQuantizationIsTheOneAcceptedRequiredExtension() { var o = Valid(); o["extensionsRequired"] = new JsonArray("KHR_mesh_quantization"); Assert.True(WorkshopGlbPrecheck.Check(Glb(o), WorkshopGlbLimits.Default).IsAccepted); }
    [Fact] public void InvalidAccessorIndexIsRefused() { var o = Valid(); o["meshes"]![0]!["primitives"]![0]!["indices"] = long.MaxValue; Refuses(Glb(o), "glb.structure"); }
    [Fact] public void WrongArrayTypeIsRefused() { var o = Valid(); o["meshes"] = true; Refuses(Glb(o), "glb.structure"); }
    [Fact] public void NegativeCountIsRefused() { var o = Valid(); o["accessors"]![0]!["count"] = -1; Refuses(Glb(o), "glb.structure"); }
    [Fact] public void HugeCountIsRefusedWithoutAllocation() { var o = Valid(); o["accessors"]![0]!["count"] = long.MaxValue; Refuses(Glb(o), "glb.limit.vertices"); }
    [Fact] public void JpegFrameIsMeasured() { var b = new byte[36]; new byte[] {255,216,255,192,0,17,8,0,24,0,32,3}.CopyTo(b, 0); var o = Valid(); o["images"]![0]!["mimeType"] = "image/jpeg"; var r = WorkshopGlbPrecheck.Check(Glb(o, b), WorkshopGlbLimits.Default); Assert.True(r.IsAccepted); Assert.Equal(new WorkshopGlbImageSize(32,24), Assert.Single(r.Measurements.ImageSizes)); }
    [Fact] public void TruncatedJpegSegmentIsRefused() { var b = new byte[36]; new byte[] {255,216,255,224,255,255}.CopyTo(b, 0); Refuses(Glb(bin: b), "glb.image.format"); }
    [Fact] public void ZeroPngDimensionIsRefused() => Refuses(Glb(bin: Png(0, 8)), "glb.image.format");
    [Fact] public void EveryTruncatedPrefixIsRefusedWithoutThrowing() { var b = Glb(); for (int n = 0; n < b.Length; n++) Assert.False(WorkshopGlbPrecheck.Check(b.AsSpan(0,n), WorkshopGlbLimits.Default).IsAccepted); }
    [Fact] public void DeterministicMutationsNeverThrow() { var random = new Random(816); var original = Glb(); for (int n = 0; n < 1000; n++) { var b = (byte[])original.Clone(); b[random.Next(b.Length)] = (byte)random.Next(256); WorkshopGlbPrecheck.Check(b, WorkshopGlbLimits.Default); } }
    [Fact] public void CountsAllPrimitivesAndLargestSkin()
    {
        var o = Valid();
        o["meshes"]![0]!["primitives"]!.AsArray().Add(o["meshes"]![0]!["primitives"]![0]!.DeepClone());
        o["skins"]!.AsArray().Add(new JsonObject { ["joints"] = new JsonArray(0, 0) });
        var r = WorkshopGlbPrecheck.Check(Glb(o), WorkshopGlbLimits.Default);
        Assert.True(r.IsAccepted); Assert.Equal(2, r.Measurements.Triangles); Assert.Equal(6, r.Measurements.Vertices); Assert.Equal(2, r.Measurements.MaxJoints);
    }
    [Fact] public void CountOverflowIsRefused()
    {
        var o = Valid(); o["accessors"]![0]!["count"] = long.MaxValue;
        o["meshes"]![0]!["primitives"]!.AsArray().Add(o["meshes"]![0]!["primitives"]![0]!.DeepClone());
        Refuses(Glb(o), "glb.structure");
    }
    [Fact] public void InvalidAssetVersionIsRefused() { var o = Valid(); o["asset"]!["version"] = "1.0"; Refuses(Glb(o), "glb.structure"); }
    [Fact] public void InvalidSkinReferenceIsRefused() { var o = Valid(); o["nodes"]![0]!["skin"] = 99; Refuses(Glb(o), "glb.structure"); }
    [Fact] public void InvalidUtf8InUnusedJsonStringIsRefused()
    {
        var o = Valid(); o["extras"] = "unique-text";
        var json = Encoding.UTF8.GetBytes(o.ToJsonString());
        int offset = Encoding.UTF8.GetString(json).IndexOf("unique-text", StringComparison.Ordinal);
        json[offset] = 255;
        Refuses(Container(json, Png()), "glb.json");
    }
    [Fact] public void DuplicateJsonPropertyIsRefused()
    {
        var json = Valid().ToJsonString().Replace("\"asset\":", "\"asset\":{},\"asset\":");
        Refuses(Container(Encoding.UTF8.GetBytes(json), Png()), "glb.structure");
    }
}
