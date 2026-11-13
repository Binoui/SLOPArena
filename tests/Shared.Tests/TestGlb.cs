using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace SlopArena.Shared.Tests;

/// <summary>Writes small skinned humanoid GLBs with 60 Hz clips; every value is rational or a square root, so bytes match on any runtime.</summary>
internal static class TestGlb
{
    internal sealed record Node(string Name, string? Parent, float[] Translation, float[]? Rotation = null);

    internal sealed record Channel(string Node, string Path, float[] Times, float[] Values);

    internal sealed record Clip(string Name, float Duration, IReadOnlyList<Channel> Channels, bool Loop = false, int? FrameCount = null,
        string Interpolation = "LINEAR", bool Extras = true, JsonObject? ExtraSlop = null);

    internal const string P = "mixamorig:";

    /// <summary>A T-pose in glTF axes: +Y up, +Z forward, +X the figure's left.</summary>
    internal static List<Node> Skeleton()
    {
        var nodes = new List<Node>
        {
            new(P + "Hips", "Armature", V(0, 1, 0)), new(P + "Spine", P + "Hips", V(0, .1f, 0)), new(P + "Spine1", P + "Spine", V(0, .1f, 0)),
            new(P + "Spine2", P + "Spine1", V(0, .1f, 0)), new(P + "Neck", P + "Spine2", V(0, .15f, 0)), new(P + "Head", P + "Neck", V(0, .1f, 0)),
            new("Shell", P + "Spine1", V(0, 0, -.15f)),
        };
        foreach (var (side, x) in new[] { ("Left", 1f), ("Right", -1f) })
        {
            nodes.Add(new(P + side + "Shoulder", P + "Spine2", V(.05f * x, .1f, 0)));
            nodes.Add(new(P + side + "Arm", P + side + "Shoulder", V(.1f * x, 0, 0)));
            nodes.Add(new(P + side + "ForeArm", P + side + "Arm", V(.25f * x, 0, 0)));
            nodes.Add(new(P + side + "Hand", P + side + "ForeArm", V(.25f * x, 0, 0)));
            nodes.Add(new(P + side + "HandMiddle1", P + side + "Hand", V(.08f * x, 0, 0)));
            nodes.Add(new(P + side + "UpLeg", P + "Hips", V(.1f * x, -.05f, 0)));
            nodes.Add(new(P + side + "Leg", P + side + "UpLeg", V(0, -.45f, 0)));
            nodes.Add(new(P + side + "Foot", P + side + "Leg", V(0, -.45f, 0)));
            nodes.Add(new(P + side + "ToeBase", P + side + "Foot", V(0, -.05f, .12f)));
        }
        nodes.Add(new("_weapon_hilt", P + "RightHand", V(-.05f, 0, 0)));
        nodes.Add(new("_weapon_tip", P + "RightHand", V(-.8f, 0, 0)));
        string tail = P + "Hips";
        for (int i = 1; i <= 5; i++) { nodes.Add(new("Tail" + i, tail, V(0, 0, -.1f))); tail = "Tail" + i; }
        return nodes;
    }

    internal static float[] V(float x, float y, float z) => new[] { x, y, z };

    /// <summary>A unit quaternion about one axis from the rational half-angle tangent t.</summary>
    internal static float[] Turn(int axis, double t)
    {
        double length = Math.Sqrt(1 + t * t);
        var q = new float[] { 0, 0, 0, (float)(1 / length) };
        q[axis] = (float)(t / length);
        return q;
    }

    internal static float[] Ticks(int count, float start = 0f) => Enumerable.Range(0, count).Select(k => (float)(start + k / 60.0)).ToArray();

    /// <summary>Raise the left arm and bob the hips over <paramref name="frames"/> keys.</summary>
    internal static Clip Wave(string name, int frames, bool loop = false, float start = 0f)
    {
        var times = Ticks(frames, start);
        var arm = Enumerable.Range(0, frames).SelectMany(k => Turn(2, (k + 1) / (double)frames)).ToArray();
        var forearm = Enumerable.Range(0, frames).SelectMany(k => Turn(1, -k / (double)(2 * frames))).ToArray();
        var hips = Enumerable.Range(0, frames).SelectMany(k => V(0, 1 - k / (float)(4 * frames), k / (float)(10 * frames))).ToArray();
        return new Clip(name, frames / 60f, new[]
        {
            new Channel(P + "LeftArm", "rotation", times, arm), new Channel(P + "LeftForeArm", "rotation", times, forearm),
            new Channel(P + "Hips", "translation", times, hips),
        }, loop);
    }

    internal static byte[] Build(IReadOnlyList<Clip> clips, float[]? armatureRotation = null, List<Node>? skeleton = null)
    {
        skeleton ??= Skeleton();
        var names = new List<string> { "Armature" };
        names.AddRange(skeleton.Select(x => x.Name));
        names.Add("Body");
        int Index(string name) => names.IndexOf(name);
        var bin = new MemoryStream();
        var views = new JsonArray();
        var accessors = new JsonArray();
        int Accessor(float[] values, string type, int count, int componentType = 5126, bool minMax = false)
        {
            while (bin.Length % 4 != 0) bin.WriteByte(0);
            long offset = bin.Length;
            foreach (float value in values) { var b = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(b, BitConverter.SingleToInt32Bits(value)); bin.Write(b, 0, 4); }
            views.Add(new JsonObject { ["buffer"] = 0, ["byteOffset"] = offset, ["byteLength"] = values.Length * 4 });
            var accessor = new JsonObject { ["bufferView"] = views.Count - 1, ["componentType"] = componentType, ["type"] = type, ["count"] = count };
            if (minMax) { accessor["min"] = new JsonArray(values.Min()); accessor["max"] = new JsonArray(values.Max()); }
            accessors.Add(accessor);
            return accessors.Count - 1;
        }
        int position = Accessor(new float[] { 0, 1, 0, .1f, 1, 0, 0, 1.1f, 0 }, "VEC3", 3);
        accessors[position]!["min"] = new JsonArray(0f, 1f, 0f); accessors[position]!["max"] = new JsonArray(.1f, 1.1f, 0f);
        while (bin.Length % 4 != 0) bin.WriteByte(0);
        long jointsOffset = bin.Length;
        bin.Write(new byte[12], 0, 12);
        views.Add(new JsonObject { ["buffer"] = 0, ["byteOffset"] = jointsOffset, ["byteLength"] = 12 });
        accessors.Add(new JsonObject { ["bufferView"] = views.Count - 1, ["componentType"] = 5121, ["type"] = "VEC4", ["count"] = 3 });
        int joints = accessors.Count - 1;
        int weights = Accessor(new float[] { 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0 }, "VEC4", 3);

        var nodes = new JsonArray();
        var armature = new JsonObject { ["name"] = "Armature", ["children"] = new JsonArray() };
        if (armatureRotation != null) armature["rotation"] = new JsonArray(armatureRotation.Select(x => JsonValue.Create(x)).ToArray());
        nodes.Add(armature);
        foreach (var node in skeleton)
        {
            var element = new JsonObject { ["name"] = node.Name, ["translation"] = new JsonArray(node.Translation.Select(x => JsonValue.Create(x)).ToArray()) };
            if (node.Rotation != null) element["rotation"] = new JsonArray(node.Rotation.Select(x => JsonValue.Create(x)).ToArray());
            nodes.Add(element);
        }
        nodes.Add(new JsonObject { ["name"] = "Body", ["mesh"] = 0, ["skin"] = 0 });
        foreach (var node in skeleton)
        {
            var parent = nodes[Index(node.Parent!)]!.AsObject();
            if (!parent.ContainsKey("children")) parent["children"] = new JsonArray();
            parent["children"]!.AsArray().Add(Index(node.Name));
        }

        var animations = new JsonArray();
        foreach (var clip in clips)
        {
            var samplers = new JsonArray();
            var channels = new JsonArray();
            foreach (var channel in clip.Channels)
            {
                int input = Accessor(channel.Times, "SCALAR", channel.Times.Length, minMax: true);
                int output = Accessor(channel.Values, channel.Path == "rotation" ? "VEC4" : "VEC3", channel.Times.Length);
                samplers.Add(new JsonObject { ["input"] = input, ["output"] = output, ["interpolation"] = clip.Interpolation });
                channels.Add(new JsonObject { ["sampler"] = samplers.Count - 1, ["target"] = new JsonObject { ["node"] = Index(channel.Node), ["path"] = channel.Path } });
            }
            var animation = new JsonObject { ["name"] = clip.Name, ["samplers"] = samplers, ["channels"] = channels };
            if (clip.Extras)
            {
                var slop = clip.ExtraSlop ?? new JsonObject();
                if (!slop.ContainsKey("durationSecondsBits")) slop["durationSecondsBits"] = BitConverter.SingleToInt32Bits(clip.Duration);
                if (!slop.ContainsKey("frameCount")) slop["frameCount"] = clip.FrameCount ?? Math.Max(1, (int)Math.Ceiling((double)(float)(clip.Duration * 60.0)));
                if (clip.Loop) slop["loop"] = true;
                animation["extras"] = new JsonObject { ["slop"] = slop };
            }
            animations.Add(animation);
        }
        while (bin.Length % 4 != 0) bin.WriteByte(0);
        var gltf = new JsonObject
        {
            ["asset"] = new JsonObject { ["version"] = "2.0" },
            ["buffers"] = new JsonArray(new JsonObject { ["byteLength"] = bin.Length }),
            ["bufferViews"] = views, ["accessors"] = accessors,
            ["meshes"] = new JsonArray(new JsonObject { ["primitives"] = new JsonArray(new JsonObject { ["attributes"] = new JsonObject { ["POSITION"] = position, ["JOINTS_0"] = joints, ["WEIGHTS_0"] = weights } }) }),
            ["skins"] = new JsonArray(new JsonObject { ["joints"] = new JsonArray(skeleton.Where(x => x.Name.StartsWith(P, StringComparison.Ordinal)).Select(x => JsonValue.Create(Index(x.Name))).ToArray()) }),
            ["nodes"] = nodes, ["scene"] = 0,
            ["scenes"] = new JsonArray(new JsonObject { ["nodes"] = new JsonArray(0, Index("Body")) }),
            ["animations"] = animations,
        };
        byte[] json = Encoding.UTF8.GetBytes(gltf.ToJsonString());
        int jsonLength = (json.Length + 3) & ~3;
        byte[] binBytes = bin.ToArray();
        var glb = new byte[12 + 8 + jsonLength + 8 + binBytes.Length];
        void U(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(at), value);
        U(0, 0x46546c67); U(4, 2); U(8, (uint)glb.Length); U(12, (uint)jsonLength); U(16, 0x4e4f534a);
        glb.AsSpan(20, jsonLength).Fill(32); json.CopyTo(glb, 20);
        U(20 + jsonLength, (uint)binBytes.Length); U(24 + jsonLength, 0x004e4942);
        binBytes.CopyTo(glb, 28 + jsonLength);
        return glb;
    }
}
