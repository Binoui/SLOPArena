using System;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;
using static SlopArena.Shared.Tests.TestGlb;

namespace SlopArena.Shared.Tests;

/// <summary>Hostile GLBs: every count an item supplies is bounded before anything is sized from it.</summary>
public sealed class GlbMotionLimitTests
{
    private const long Budget = 16L << 20;
    private static int Bits(float value) => BitConverter.SingleToInt32Bits(value);

    private static string Refusal(byte[] glb, string clip, out long allocated)
    {
        var rig = GlbMotionRig.Read(glb, null);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var error = Assert.Throws<GlbMotionException>(() => rig.Clip(clip));
        allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        return error.Code;
    }

    [Theory]
    [InlineData(float.MaxValue, 1)]
    [InlineData(1e30f, 1)]
    [InlineData(69f, 4140)]
    public void ADurationBeyondThePoseFrameLimitIsRefusedBeforeKeysAreSized(float duration, int frames)
    {
        var keys = new Channel(P + "Hips", "translation", new[] { 0f, 1_000_000f }, new float[] { 0, 1, 0, 0, 2, 0 });
        var glb = Build(new[] { new Clip("huge", duration, new[] { keys }, Interpolation: "STEP",
            ExtraSlop: new JsonObject { ["durationSecondsBits"] = Bits(duration), ["frameCount"] = frames }) });
        Assert.Equal("glb.clip.frame-count", Refusal(glb, "huge", out long allocated));
        Assert.True(allocated < Budget, $"allocated {allocated} bytes");
    }

    [Theory]
    [InlineData(0x7FC00000)]
    [InlineData(0x7F800000)]
    [InlineData(unchecked((int)0xBF800000))]
    [InlineData(0)]
    public void ANonFiniteOrNonPositiveDurationIsRefused(int bits)
    {
        var keys = new Channel(P + "Hips", "translation", new[] { 0f }, new float[] { 0, 1, 0 });
        var glb = Build(new[] { new Clip("bad", 1f, new[] { keys }, ExtraSlop: new JsonObject { ["durationSecondsBits"] = bits, ["frameCount"] = 1 }) });
        Assert.Equal("glb.clip.extras", Refusal(glb, "bad", out _));
    }

    [Fact]
    public void KeysBeyondTheFrameCountAreRefusedBeforeTheSharedAccessorIsRead()
    {
        const int keys = 1_500_000;
        var glb = Rewrite(Build(new[] { Wave("tight", 1) }), (json, append) =>
        {
            int input = append(Enumerable.Range(0, keys).Select(i => i * 1e-9f).ToArray(), "SCALAR");
            int output = append(new float[keys * 3], "VEC3");
            var samplers = new JsonArray();
            var channels = new JsonArray();
            var nodes = json["nodes"]!.AsArray();
            for (int node = 0; node < nodes.Count; node++)
                foreach (string path in new[] { "translation", "scale" })
                {
                    samplers.Add(new JsonObject { ["input"] = input, ["output"] = output, ["interpolation"] = "STEP" });
                    channels.Add(new JsonObject { ["sampler"] = samplers.Count - 1, ["target"] = new JsonObject { ["node"] = node, ["path"] = path } });
                }
            json["animations"]![0]!["samplers"] = samplers;
            json["animations"]![0]!["channels"] = channels;
        });
        Assert.Equal("glb.clip.keys", Refusal(glb, "tight", out long allocated));
        Assert.True(allocated < Budget, $"allocated {allocated} bytes");
    }

    [Fact]
    public void TwoKeysOnOneTickAreRefused()
    {
        var keys = new Channel(P + "Hips", "translation", new[] { 0f, 1e-6f, 1 / 60f }, new float[] { 0, 1, 0, 0, 1, 0, 0, 1, 0 });
        var glb = Build(new[] { new Clip("twice", 2 / 60f, new[] { keys }, Interpolation: "STEP") });
        Assert.Equal("glb.clip.timing", Refusal(glb, "twice", out _));
    }

    [Fact]
    public void AliasedAccessorsCannotReadMoreThanTheFileHolds()
    {
        var glb = Rewrite(Build(new[] { Wave("long", 4096) }), (json, append) =>
        {
            var animation = json["animations"]![0]!;
            int input = animation["samplers"]![0]!["input"]!.GetValue<int>();
            int view = json["accessors"]![animation["samplers"]![0]!["output"]!.GetValue<int>()]!["bufferView"]!.GetValue<int>();
            var accessors = json["accessors"]!.AsArray();
            var samplers = new JsonArray();
            var channels = new JsonArray();
            var nodes = json["nodes"]!.AsArray();
            for (int node = 0; node < nodes.Count; node++)
            {
                accessors.Add(new JsonObject { ["bufferView"] = view, ["componentType"] = 5126, ["type"] = "VEC4", ["count"] = 4096 });
                samplers.Add(new JsonObject { ["input"] = input, ["output"] = accessors.Count - 1 });
                channels.Add(new JsonObject { ["sampler"] = samplers.Count - 1, ["target"] = new JsonObject { ["node"] = node, ["path"] = "rotation" } });
            }
            animation["samplers"] = samplers;
            animation["channels"] = channels;
        });
        Assert.Equal("glb.limit.animation-bytes", Refusal(glb, "long", out _));
    }

    [Fact]
    public void AnAccessorSharedBySamplersIsReadAndCountedOnce()
    {
        var glb = Rewrite(Build(new[] { Wave("shared", 30) }), (json, _) =>
        {
            var samplers = json["animations"]![0]!["samplers"]!.AsArray();
            int input = samplers[0]!["input"]!.GetValue<int>();
            foreach (var sampler in samplers) sampler!["input"] = input;
        });
        var rig = GlbMotionRig.Read(glb, null);
        rig.Clip("shared");
        rig.Clip("shared");
        Assert.Equal(30 * 4 + 30 * 4 * 4 + 30 * 4 * 4 + 30 * 3 * 4, rig.AnimationBytesRead);
    }

    [Fact]
    public void ReadingAClipAllocatesInProportionToTheDataItReads()
    {
        const int frames = 4096;
        var glb = Rewrite(Build(new[] { Wave("full", frames) }), (json, append) =>
        {
            var animation = json["animations"]![0]!;
            int input = animation["samplers"]![0]!["input"]!.GetValue<int>();
            var samplers = new JsonArray();
            var channels = new JsonArray();
            var nodes = json["nodes"]!.AsArray();
            int translation = append(new float[frames * 3], "VEC3"), rotation = append(Enumerable.Range(0, frames * 4).Select(i => i % 4 == 3 ? 1f : 0f).ToArray(), "VEC4");
            for (int node = 0; node < nodes.Count; node++)
                foreach (string path in new[] { "translation", "rotation", "scale" })
                {
                    samplers.Add(new JsonObject { ["input"] = input, ["output"] = path == "rotation" ? rotation : translation });
                    channels.Add(new JsonObject { ["sampler"] = samplers.Count - 1, ["target"] = new JsonObject { ["node"] = node, ["path"] = path } });
                }
            animation["samplers"] = samplers;
            animation["channels"] = channels;
        });
        var rig = GlbMotionRig.Read(glb, null);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var clip = rig.Clip("full");
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(frames * 4 + frames * 12 + frames * 16, rig.AnimationBytesRead);
        Assert.True(allocated < 2 * rig.AnimationBytesRead + clip.Channels.Length * frames * 4 + (256 << 10), $"allocated {allocated} bytes for {rig.AnimationBytesRead} read");
    }

    [Fact]
    public void APointOutsideTheDrawnSceneIsRefused()
    {
        var detached = Rewrite(FighterItemAdmissionTests.Glb(), (json, _) =>
        {
            var nodes = json["nodes"]!.AsArray();
            int hand = nodes.Select((n, i) => (n, i)).Single(x => x.n!["name"]!.GetValue<string>() == P + "RightHand").i;
            foreach (var node in nodes)
                if (node!["children"] is JsonArray children && children.Any(c => c!.GetValue<int>() == hand))
                    node["children"] = new JsonArray(children.Where(c => c!.GetValue<int>() != hand).Select(c => JsonValue.Create(c!.GetValue<int>())).ToArray());
        });
        var error = Assert.Throws<Xunit.Sdk.TrueException>(() => FighterItemAdmissionTests.Item(model: detached));
        Assert.Contains("item.pose-bake.point", error.Message);
    }

    [Fact]
    public void APointOutsideTheDrawnSkinIsRefused()
    {
        var map = JsonNode.Parse(FighterItemAdmissionTests.HumanoidJson())!;
        foreach (var bone in map["bones"]!.AsArray())
            if (bone!["human"]!.GetValue<string>() == "RightHand") bone["node"] = "Body";
        var error = Assert.Throws<Xunit.Sdk.TrueException>(() => FighterItemAdmissionTests.Item(humanoid: Encoding.UTF8.GetBytes(map.ToJsonString())));
        Assert.Contains("item.pose-bake.point", error.Message);
    }
}
