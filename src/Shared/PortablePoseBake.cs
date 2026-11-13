using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace SlopArena.Shared;

/// <summary>One bound animation of a portable fighter and the clip that plays it.</summary>
public sealed class PortableMotion
{
    public string SemanticId { get; }
    public string PoseTrackId { get; }
    public GlbClip Clip { get; }
    /// <summary>The GLB clip name, or the shared clip's ID and version.</summary>
    public string Source { get; }
    internal PortableMotion(string semanticId, string poseTrackId, GlbClip clip, string source)
    { SemanticId = semanticId; PoseTrackId = poseTrackId; Clip = clip; Source = source; }
}

/// <summary>A portable fighter's skeleton and every bound animation, read once and shared by bake and renderer.</summary>
public sealed class PortableFighterMotion
{
    public GlbMotionRig Rig { get; }
    public IReadOnlyList<PortableMotion> Animations { get; }
    private PortableFighterMotion(GlbMotionRig rig, PortableMotion[] animations)
    { Rig = rig; Animations = Array.AsReadOnly(animations); }

    /// <summary>Resolve schema-3 binding animations against the item's GLB and the shared-clip registry.</summary>
    internal static PortableFighterMotion Read(JsonElement bindings, IReadOnlyDictionary<string, byte[]> files, bool bindsFrameCounts = true)
    {
        string? humanoid = files.TryGetValue("humanoid.json", out var map) ? Encoding.UTF8.GetString(map) : null;
        var rig = GlbMotionRig.Read(files[bindings.GetProperty("modelFile").GetString()!], humanoid);
        var animations = new List<PortableMotion>();
        foreach (var animation in bindings.GetProperty("animations").EnumerateArray())
        {
            string semantic = animation.GetProperty("semanticId").GetString()!;
            var source = animation.GetProperty("source");
            GlbClip clip;
            string key;
            if (source.GetProperty("kind").GetString() == "glb")
            {
                key = source.GetProperty("clipName").GetString()!;
                clip = rig.Clip(key);
            }
            else
            {
                string id = source.GetProperty("id").GetString()!, version = source.GetProperty("version").GetString()!;
                key = id + "@" + version;
                if (!SharedClipRegistry.Default.TryResolve(id, version, out var shared))
                    throw new GlbMotionException("portable.shared.unsupported", $"Shared clip {key} is not in the registry.");
                clip = shared!.Clip.Retarget(shared.Rig, rig);
            }
            int frames = animation.GetProperty("frameCount").GetInt32();
            if (bindsFrameCounts && frames != clip.FrameCount)
                throw new GlbMotionException("item.pose-bake.frame-count", $"'{semantic}' binds {frames} frames; clip {key} has {clip.FrameCount}.");
            animations.Add(new PortableMotion(semantic, animation.GetProperty("poseTrackId").GetString()!, clip, key));
        }
        return new PortableFighterMotion(rig, animations.ToArray());
    }
}

/// <summary>
/// The deterministic pose bake of root rule 1. Byte layout and point order follow the Editor baker, so Shared reads a
/// portable fighter's poses exactly as a built-in's. Every value is computed in double with +, -, *, / and sqrt and
/// rounded to binary32 once, which makes the bytes equal on Mono and CoreCLR.
/// </summary>
public static class PortablePoseBake
{
    /// <summary>Bake every bound animation, ordered by pose track ID.</summary>
    public static byte[] Bake(PortableFighterMotion motion, CookedCharacterDefinition definition)
    {
        var rig = motion.Rig;
        var pointIds = CharacterBoneRegistry.HumanoidPoints.Where(x => x.Required).Select(x => x.Id)
            .Concat(definition.HurtboxBoneDefs.SelectMany(x => new[] { x.BoneId, x.EndBoneId }).Where(x => x != null).Select(x => x!))
            .GroupBy(x => CharacterBoneRegistry.BakedName(x), StringComparer.Ordinal).Select(x => x.First())
            .Concat(definition.AttachmentBoneIds).ToArray();
        var nodes = pointIds.Select(id => PointNode(rig, id)).ToArray();
        var names = pointIds.Select(x => CharacterBoneRegistry.BakedName(x)!).Append(BakedAnimationData.HipsOffsetPointName).ToArray();
        if (names.Length > CookedPackageLimits.PoseBones) throw new GlbMotionException("item.pose-bake.point", "Too many baked points.");
        var chain = rig.Chain(nodes.Append(rig.Hips));
        var inChain = new bool[rig.NodeCount];
        foreach (int node in chain) inChain[node] = true;
        double scale = definition.Presentation.VisualScale, yOffset = definition.Presentation.ModelYOffset;
        var local = new GlbNodePose[rig.NodeCount];
        var world = new double[rig.NodeCount * 12];
        var frames = new Dictionary<GlbClip, float[]>();
        float[] Frames(GlbClip clip)
        {
            if (frames.TryGetValue(clip, out var cached)) return cached;
            var values = new float[clip.FrameCount * names.Length * 3];
            int at = 0;
            for (int tick = 0; tick < clip.FrameCount; tick++)
            {
                rig.SampleLocal(clip, tick, local, inChain);
                rig.World(local, chain, world);
                Model(rig.Hips, out double hx, out double hy, out double hz);
                foreach (int node in nodes)
                {
                    Model(node, out double x, out double y, out double z);
                    at = Write(values, at, x - hx, y - hy, z - hz);
                }
                at = Write(values, at, hx, hy + yOffset, hz);
            }
            return frames[clip] = values;
        }
        void Model(int node, out double x, out double y, out double z)
            => rig.Root.Apply(world[node * 12 + 3], world[node * 12 + 7], world[node * 12 + 11], scale, out x, out y, out z);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(0x4C454B53u); writer.Write(1u); writer.Write((uint)names.Length); writer.Write((uint)motion.Animations.Count);
        foreach (string name in names) WriteString(writer, name);
        foreach (var animation in motion.Animations.OrderBy(x => x.PoseTrackId, StringComparer.Ordinal))
        {
            WriteString(writer, animation.PoseTrackId);
            writer.Write((uint)animation.Clip.FrameCount);
            foreach (float value in Frames(animation.Clip)) writer.Write(BitConverter.SingleToInt32Bits(value));
        }
        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>Re-bake and require the included bytes to be equal; the first difference is named.</summary>
    internal static IReadOnlyList<CharacterDiagnostic> Verify(PortableFighterMotion motion, CookedCharacterDefinition definition, byte[] included)
    {
        byte[] baked = Bake(motion, definition);
        if (baked.AsSpan().SequenceEqual(included)) return Array.Empty<CharacterDiagnostic>();
        return new[] { MatchContentCatalogBuilder.Error("item.pose-bake.mismatch", CharacterPackageAssembler.PosePath, Difference(baked, included)) };
    }

    private static string Difference(byte[] baked, byte[] included)
    {
        BakedAnimationData expected = BakedAnimationData.LoadFromBin(baked), actual;
        try { actual = BakedAnimationData.LoadFromBin(included); }
        catch (Exception) { return "poses.bin is not the bake of the bound clips."; }
        if (!expected.BoneNames.SequenceEqual(actual.BoneNames, StringComparer.Ordinal))
            return "poses.bin points differ from the bake: expected " + string.Join(", ", expected.BoneNames) + ".";
        foreach (var track in expected.Animations)
        {
            int index = actual.FindAnimIndex(track.Name);
            if (index < 0 || actual.Animations[index].FrameCount != track.FrameCount)
                return $"Track '{track.Name}' is missing or has a different frame count than the bake.";
            var other = actual.Animations[index];
            for (int f = 0; f < track.FrameCount; f++)
                for (int p = 0; p < expected.BoneNames.Count; p++)
                {
                    var a = track.Frame(f).Slice(p * 3, 3);
                    var b = other.Frame(f).Slice(p * 3, 3);
                    bool equal = true;
                    for (int c = 0; c < 3; c++) equal &= BitConverter.SingleToInt32Bits(a[c]) == BitConverter.SingleToInt32Bits(b[c]);
                    if (equal) continue;
                    double dx = a[0] - b[0], dy = a[1] - b[1], dz = a[2] - b[2];
                    return $"Track '{track.Name}' frame {f} point '{expected.BoneNames[p]}' is {Math.Sqrt(dx * dx + dy * dy + dz * dz) * 1000:0.###} mm from the bake of its clip.";
                }
        }
        return "poses.bin differs from the bake of the bound clips.";
    }

    /// <summary>
    /// The node a point is baked from. It must be a joint of the drawn skin or a node below one, so every baked point
    /// is a position the renderer draws.
    /// </summary>
    private static int PointNode(GlbMotionRig rig, string id)
    {
        string? name;
        if (CharacterBoneRegistry.Find(id) is { } point) name = rig.BoneMap.NodeFor(point.HumanoidBone);
        else if (CharacterBoneRegistry.IsRigId(id)) name = id.Substring(CharacterBoneRegistry.RigPrefix.Length);
        // An attachment point is a marker node of the same name, e.g. "_weapon_tip".
        else if (id.StartsWith("_", StringComparison.Ordinal) && id != BakedAnimationData.HipsOffsetPointName) name = id;
        else throw new GlbMotionException("item.pose-bake.point", $"Point {id} is not a bone the bake can read.");
        int node = name == null ? -1 : rig.FindNode(name);
        return rig.IsDrawnBone(node) ? node
            : throw new GlbMotionException("item.pose-bake.point", $"Point {id} must be exactly one node among the bones the drawn skin uses.");
    }

    private static int Write(float[] values, int at, double x, double y, double z)
    {
        values[at] = (float)x; values[at + 1] = (float)y; values[at + 2] = (float)z;
        if (!IsFinite(values[at]) || !IsFinite(values[at + 1]) || !IsFinite(values[at + 2]))
            throw new GlbMotionException("item.pose-bake.value", "The bake produced a non-finite point.");
        return at + 3;
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static void WriteString(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        writer.Write((uint)bytes.Length);
        writer.Write(bytes);
    }
}
