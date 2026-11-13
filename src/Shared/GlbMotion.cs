using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace SlopArena.Shared;

/// <summary>A node's local transform in game coordinates: the glTF X axis reflected, as glTFast places it in Unity.</summary>
public readonly struct GlbNodePose
{
    public readonly float TX, TY, TZ, QX, QY, QZ, QW, SX, SY, SZ;

    public GlbNodePose(float tx, float ty, float tz, float qx, float qy, float qz, float qw, float sx, float sy, float sz)
    {
        TX = tx; TY = ty; TZ = tz; QX = qx; QY = qy; QZ = qz; QW = qw; SX = sx; SY = sy; SZ = sz;
    }
}

/// <summary>Turns the rest skeleton upright and puts its rest hips on the model origin (root rule 1).</summary>
public readonly struct GlbRootCorrection
{
    /// <summary>Rows of the turn: the rest skeleton's right, up and forward in game coordinates.</summary>
    public readonly double RightX, RightY, RightZ, UpX, UpY, UpZ, ForwardX, ForwardY, ForwardZ;
    /// <summary>Added after the turn, before the visual scale: minus the turned rest hips.</summary>
    public readonly double OriginX, OriginY, OriginZ;

    internal GlbRootCorrection(double[] right, double[] up, double[] forward, double[] origin)
    {
        RightX = right[0]; RightY = right[1]; RightZ = right[2];
        UpX = up[0]; UpY = up[1]; UpZ = up[2];
        ForwardX = forward[0]; ForwardY = forward[1]; ForwardZ = forward[2];
        OriginX = origin[0]; OriginY = origin[1]; OriginZ = origin[2];
    }

    internal void Apply(double x, double y, double z, double scale, out double ox, out double oy, out double oz)
    {
        ox = (RightX * x + RightY * y + RightZ * z + OriginX) * scale;
        oy = (UpX * x + UpY * y + UpZ * z + OriginY) * scale;
        oz = (ForwardX * x + ForwardY * y + ForwardZ * z + OriginZ) * scale;
    }
}

/// <summary>A creator file breaks a motion rule; <see cref="Code"/> is the diagnostic code.</summary>
public sealed class GlbMotionException : Exception
{
    public string Code { get; }
    public GlbMotionException(string code, string message) : base(message) { Code = code; }
}

/// <summary>
/// One clip under sampling rule 1, normalized so a tick reads a stored key and never interpolates.
/// </summary>
public sealed class GlbClip
{
    internal enum ChannelPath : byte { Translation, Rotation, Scale }

    internal sealed class Channel
    {
        public int Node;
        public ChannelPath Path;
        /// <summary>Strictly increasing key ticks; a tick reads the last key at or before it, the first before them all.</summary>
        public int[] Ticks = Array.Empty<int>();
        public float[] Values = Array.Empty<float>();
        public int Width => Path == ChannelPath.Rotation ? 4 : 3;

        public int KeyAt(int tick)
        {
            int low = 0, high = Ticks.Length - 1;
            while (low < high)
            {
                int mid = (low + high + 1) >> 1;
                if (Ticks[mid] <= tick) low = mid; else high = mid - 1;
            }
            return low * Width;
        }
    }

    internal readonly Channel[] Channels;
    public string Name { get; }
    public int FrameCount { get; }
    public float DurationSeconds { get; }
    public bool Loop { get; }
    /// <summary>Nodes this clip animates, ascending.</summary>
    public IReadOnlyList<int> AnimatedNodes { get; }

    /// <summary>What this clip animates on <paramref name="node"/>: 1 translation, 2 rotation, 4 scale.</summary>
    public byte Paths(int node)
    {
        byte paths = 0;
        foreach (var channel in Channels)
            if (channel.Node == node) paths |= (byte)(1 << (int)channel.Path);
        return paths;
    }

    internal GlbClip(string name, int frameCount, float duration, bool loop, Channel[] channels)
    {
        Name = name; FrameCount = frameCount; DurationSeconds = duration; Loop = loop; Channels = channels;
        AnimatedNodes = Array.AsReadOnly(channels.Select(x => x.Node).Distinct().OrderBy(x => x).ToArray());
    }

    /// <summary>The same keys aimed at another skeleton's nodes of the same names (shared clips, provisional).</summary>
    internal GlbClip Retarget(GlbMotionRig source, GlbMotionRig target)
    {
        var channels = new Channel[Channels.Length];
        for (int i = 0; i < channels.Length; i++)
        {
            var from = Channels[i];
            string name = source.NodeNames[from.Node] ?? throw new GlbMotionException("glb.clip.target", $"Clip '{Name}' animates an unnamed node.");
            int node = target.FindNode(name);
            if (node < 0) throw new GlbMotionException("glb.clip.target", $"Clip '{Name}' animates node '{name}', which the fighter does not have exactly once.");
            channels[i] = new Channel { Node = node, Path = from.Path, Ticks = from.Ticks, Values = from.Values };
        }
        return new GlbClip(Name, FrameCount, DurationSeconds, Loop, channels);
    }
}

/// <summary>
/// Root and sampling rule 1 (ADR-0065): a creator GLB's scene skeleton, its root correction and its clips.
/// <see cref="SampleLocal"/> is the one sampler the pose bake and the renderer share.
/// </summary>
public sealed class GlbMotionRig
{
    public const int RootRuleVersion = 1;
    /// <summary>A key may sit this far off its tick, in ticks; binary32 times up to 4096 frames err below 1e-4.</summary>
    public const double TickTolerance = 1e-3;

    private readonly JsonElement[] animations;
    private readonly byte[] glb;
    private readonly int binOffset;
    private readonly int[] scene;
    private readonly GlbNodePose[] rest;
    private readonly double[]?[] restMatrix;
    private readonly Dictionary<string, GlbClip> clips = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> clipIndex = new(StringComparer.Ordinal);
    private readonly JsonElement accessors, views;
    private readonly Dictionary<int, float[]> accessorValues = new();
    private readonly long accessorBudget;
    private readonly bool[] drawnBone;

    public int NodeCount { get; }
    public IReadOnlyList<string?> NodeNames { get; }
    /// <summary>Parent node, or -1 for a scene root and for nodes outside the scene.</summary>
    public IReadOnlyList<int> Parents { get; }
    /// <summary>The default scene's nodes, parents before children.</summary>
    public IReadOnlyList<int> SceneOrder { get; }
    public HumanoidBoneMap BoneMap { get; }
    public int Hips { get; }
    public GlbRootCorrection Root { get; }
    public IReadOnlyList<string> ClipNames { get; }

    private GlbMotionRig(byte[] glb, int binOffset, int binLength, JsonElement root, string? humanoidJson)
    {
        this.glb = glb;
        this.binOffset = binOffset;
        accessorBudget = binLength;
        accessors = Array(root, "accessors");
        views = Array(root, "bufferViews");
        var nodes = Array(root, "nodes");
        NodeCount = nodes.GetArrayLength();
        var names = new string?[NodeCount];
        var parents = Enumerable.Repeat(-1, NodeCount).ToArray();
        rest = new GlbNodePose[NodeCount];
        restMatrix = new double[]?[NodeCount];
        for (int i = 0; i < NodeCount; i++)
        {
            var node = nodes[i];
            names[i] = node.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null;
            foreach (var child in Array(node, "children").EnumerateArray()) parents[child.GetInt32()] = i;
            ReadRest(node, i);
        }
        NodeNames = System.Array.AsReadOnly(names);
        Parents = System.Array.AsReadOnly(parents);
        int sceneIndex = root.TryGetProperty("scene", out var s) ? s.GetInt32() : 0;
        var scenes = Array(root, "scenes");
        if (sceneIndex >= scenes.GetArrayLength()) throw new GlbMotionException("glb.rig.scene", "The GLB has no scene to draw.");
        var order = new List<int>();
        var queue = new Queue<int>(Array(scenes[sceneIndex], "nodes").EnumerateArray().Select(x => x.GetInt32()));
        while (queue.Count > 0)
        {
            int n = queue.Dequeue();
            order.Add(n);
            foreach (var child in Array(nodes[n], "children").EnumerateArray()) queue.Enqueue(child.GetInt32());
        }
        scene = order.ToArray();
        SceneOrder = System.Array.AsReadOnly(scene);
        var inScene = new bool[NodeCount];
        foreach (int n in scene) inScene[n] = true;
        // A drawn bone is a joint of a skin that a mesh node in the default scene draws, or a node below one.
        var skins = Array(root, "skins");
        drawnBone = new bool[NodeCount];
        foreach (int n in scene)
            if (nodes[n].TryGetProperty("mesh", out _) && nodes[n].TryGetProperty("skin", out var skin))
                foreach (var joint in Array(skins[skin.GetInt32()], "joints").EnumerateArray())
                    if (inScene[joint.GetInt32()]) drawnBone[joint.GetInt32()] = true;
        foreach (int n in scene)
            if (parents[n] >= 0 && drawnBone[parents[n]]) drawnBone[n] = true;

        if (humanoidJson != null)
        {
            if (!HumanoidBoneMap.TryParseMapFile(humanoidJson, out var parsed, out string error)) throw new GlbMotionException("glb.rig.bone-map", error);
            BoneMap = parsed!;
        }
        else
        {
            var joints = Array(root, "skins").EnumerateArray().SelectMany(skin => Array(skin, "joints").EnumerateArray())
                .Select(j => names[j.GetInt32()]).Where(x => x != null).Select(x => x!);
            BoneMap = HumanoidBoneMap.FromMixamoNames(joints.Concat(names.Where(x => x != null).Select(x => x!)));
        }
        int Bone(string human)
        {
            string? nodeName = BoneMap.NodeFor(human);
            int node = nodeName == null ? -1 : FindNode(nodeName);
            if (node < 0 || !drawnBone[node])
                throw new GlbMotionException("glb.rig.bone", $"The rig has no {human} among the bones its drawn skin uses. Name its bones as Mixamo does, or add a humanoid.json.");
            return node;
        }
        Hips = Bone("Hips");
        Root = Upright(Bone("Hips"), Bone("Head"), Bone("LeftUpperLeg"), Bone("RightUpperLeg"));

        animations = Array(root, "animations").EnumerateArray().ToArray();
        var clipNames = new List<string>();
        for (int i = 0; i < animations.Length; i++)
            if (animations[i].TryGetProperty("name", out var clipName) && clipName.ValueKind == JsonValueKind.String
                && clipIndex.TryAdd(clipName.GetString()!, i))
                clipNames.Add(clipName.GetString()!);
        ClipNames = clipNames.AsReadOnly();
    }

    /// <summary>Parse a GLB that passed <see cref="WorkshopGlbPrecheck"/>; clips are validated when first read.</summary>
    public static GlbMotionRig Read(byte[] glb, string? humanoidJson)
    {
        var check = WorkshopGlbPrecheck.Check(glb, WorkshopGlbLimits.Default, portableImages: true);
        if (!check.IsAccepted) throw new GlbMotionException(check.Diagnostics[0].Code, check.Diagnostics[0].Message);
        int jsonLength = BinaryPrimitives.ReadInt32LittleEndian(glb.AsSpan(12, 4));
        int binOffset = 20 + jsonLength + 8;
        int binLength = glb.Length >= binOffset ? BinaryPrimitives.ReadInt32LittleEndian(glb.AsSpan(binOffset - 8, 4)) : 0;
        try
        {
            using var document = JsonDocument.Parse(glb.AsMemory(20, jsonLength), new JsonDocumentOptions { MaxDepth = WorkshopGlbLimits.Default.MaxJsonDepth });
            return new GlbMotionRig(glb, binOffset, binLength, document.RootElement.Clone(), humanoidJson);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or IndexOutOfRangeException or OverflowException)
        { throw new GlbMotionException("glb.rig.structure", ex.Message); }
    }

    /// <summary>The node with this exact name, or -1 when none or several have it.</summary>
    public int FindNode(string name)
    {
        int found = -1;
        for (int i = 0; i < NodeCount; i++)
            if (NodeNames[i] == name)
            {
                if (found >= 0) return -1;
                found = i;
            }
        return found;
    }

    public bool HasClip(string name) => clipIndex.ContainsKey(name);

    /// <summary>Bytes of distinct animation accessors read so far; never more than the BIN chunk holds.</summary>
    internal long AnimationBytesRead { get; private set; }

    /// <summary>The node is a joint of a skin drawn in the default scene, or below one: the only nodes the bake reads.</summary>
    public bool IsDrawnBone(int node) => node >= 0 && node < NodeCount && drawnBone[node];

    /// <summary>The node uses a fixed matrix, which clips may not animate and the sampler reports as identity.</summary>
    public bool IsMatrixNode(int node) => restMatrix[node] != null;

    /// <summary>The named clip, validated against sampling rule 1.</summary>
    public GlbClip Clip(string name)
    {
        if (clips.TryGetValue(name, out var cached)) return cached;
        if (!clipIndex.TryGetValue(name, out int index)) throw new GlbMotionException("glb.clip.missing", $"The GLB has no clip named '{name}'.");
        try { return clips[name] = ReadClip(name, animations[index]); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or IndexOutOfRangeException or OverflowException)
        { throw new GlbMotionException("glb.clip.structure", $"Clip '{name}': {ex.Message}"); }
    }

    /// <summary>
    /// Rule 1's sampler: every node's local transform at <paramref name="tick"/> (0 .. FrameCount-1). Nodes the clip
    /// does not animate keep their rest transform; channels clamp outside their key range.
    /// </summary>
    public void SampleLocal(GlbClip clip, int tick, Span<GlbNodePose> output) => SampleLocal(clip, tick, output, null);

    /// <summary>The same sampler, with channels limited to <paramref name="nodes"/> when given (the bake's chain).</summary>
    internal void SampleLocal(GlbClip clip, int tick, Span<GlbNodePose> output, bool[]? nodes)
    {
        if (output.Length != NodeCount) throw new ArgumentException("One pose per node is required.", nameof(output));
        rest.AsSpan().CopyTo(output);
        foreach (var channel in clip.Channels)
        {
            if (nodes != null && !nodes[channel.Node]) continue;
            int k = channel.KeyAt(tick);
            var v = channel.Values;
            var p = output[channel.Node];
            output[channel.Node] = channel.Path switch
            {
                GlbClip.ChannelPath.Translation => new GlbNodePose(-v[k], v[k + 1], v[k + 2], p.QX, p.QY, p.QZ, p.QW, p.SX, p.SY, p.SZ),
                GlbClip.ChannelPath.Rotation => new GlbNodePose(p.TX, p.TY, p.TZ, v[k], -v[k + 1], -v[k + 2], v[k + 3], p.SX, p.SY, p.SZ),
                _ => new GlbNodePose(p.TX, p.TY, p.TZ, p.QX, p.QY, p.QZ, p.QW, v[k], v[k + 1], v[k + 2]),
            };
        }
    }

    /// <summary>The rest transform of every node.</summary>
    public void SampleRest(Span<GlbNodePose> output) => rest.AsSpan().CopyTo(output);

    /// <summary>
    /// World affine transforms (3x4 row-major, 12 doubles per node) of <paramref name="nodes"/>, which must list
    /// parents before children and include every ancestor. Game coordinates, before the root correction.
    /// </summary>
    internal void World(ReadOnlySpan<GlbNodePose> local, IReadOnlyList<int> nodes, double[] world)
    {
        Span<double> m = stackalloc double[12];
        foreach (int n in nodes)
        {
            if (restMatrix[n] is { } fixedMatrix) fixedMatrix.AsSpan().CopyTo(m);
            else Compose(local[n], m);
            int parent = Parents[n];
            int o = n * 12;
            if (parent < 0) { m.CopyTo(world.AsSpan(o, 12)); continue; }
            int p = parent * 12;
            for (int r = 0; r < 3; r++)
            {
                double a = world[p + r * 4], b = world[p + r * 4 + 1], c = world[p + r * 4 + 2];
                world[o + r * 4] = a * m[0] + b * m[4] + c * m[8];
                world[o + r * 4 + 1] = a * m[1] + b * m[5] + c * m[9];
                world[o + r * 4 + 2] = a * m[2] + b * m[6] + c * m[10];
                world[o + r * 4 + 3] = a * m[3] + b * m[7] + c * m[11] + world[p + r * 4 + 3];
            }
        }
    }

    /// <summary>Every ancestor of <paramref name="targets"/> and the targets, parents first.</summary>
    internal int[] Chain(IEnumerable<int> targets)
    {
        var needed = new bool[NodeCount];
        foreach (int target in targets)
            for (int n = target; n >= 0 && !needed[n]; n = Parents[n]) needed[n] = true;
        return scene.Where(n => needed[n]).ToArray();
    }

    // Rotation from a quaternion of any nonzero length (s = 2/|q|^2), columns scaled, then translation.
    private static void Compose(in GlbNodePose p, Span<double> m)
    {
        double x = p.QX, y = p.QY, z = p.QZ, w = p.QW;
        double norm = x * x + y * y + z * z + w * w;
        if (!(norm > 0)) throw new GlbMotionException("glb.clip.value", "A rotation is the zero quaternion.");
        double s = 2.0 / norm;
        double sx = p.SX, sy = p.SY, sz = p.SZ;
        m[0] = (1 - s * (y * y + z * z)) * sx; m[1] = s * (x * y - z * w) * sy; m[2] = s * (x * z + y * w) * sz; m[3] = p.TX;
        m[4] = s * (x * y + z * w) * sx; m[5] = (1 - s * (x * x + z * z)) * sy; m[6] = s * (y * z - x * w) * sz; m[7] = p.TY;
        m[8] = s * (x * z - y * w) * sx; m[9] = s * (y * z + x * w) * sy; m[10] = (1 - s * (x * x + y * y)) * sz; m[11] = p.TZ;
    }

    private GlbRootCorrection Upright(int hips, int head, int leftThigh, int rightThigh)
    {
        var poses = new GlbNodePose[NodeCount];
        SampleRest(poses);
        var world = new double[NodeCount * 12];
        World(poses, Chain(new[] { hips, head, leftThigh, rightThigh }), world);
        double[] At(int n) => new[] { world[n * 12 + 3], world[n * 12 + 7], world[n * 12 + 11] };
        double[] hip = At(hips), up = Sub(At(head), hip), right = Sub(At(rightThigh), At(leftThigh));
        double upLength = Math.Sqrt(Dot(up, up));
        if (!(upLength > 1e-9)) throw new GlbMotionException("glb.rig.upright", "The rest pose puts the head on the hips, so the model cannot be stood upright.");
        up = Scale(up, 1 / upLength);
        right = Sub(right, Scale(up, Dot(right, up)));
        double rightLength = Math.Sqrt(Dot(right, right));
        if (!(rightLength > 1e-9)) throw new GlbMotionException("glb.rig.upright", "The rest pose lines the thighs up with the spine, so the model's facing cannot be read.");
        right = Scale(right, 1 / rightLength);
        var forward = new[] { right[1] * up[2] - right[2] * up[1], right[2] * up[0] - right[0] * up[2], right[0] * up[1] - right[1] * up[0] };
        var origin = new[] { -Dot(right, hip), -Dot(up, hip), -Dot(forward, hip) };
        return new GlbRootCorrection(right, up, forward, origin);
    }

    private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
    private static double[] Sub(double[] a, double[] b) => new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
    private static double[] Scale(double[] a, double s) => new[] { a[0] * s, a[1] * s, a[2] * s };

    private void ReadRest(JsonElement node, int index)
    {
        if (node.TryGetProperty("matrix", out var matrix))
        {
            var v = Numbers(matrix, 16, "matrix");
            // Column-major glTF; X reflected on both sides, as glTFast converts it.
            restMatrix[index] = new double[]
            {
                v[0], -v[4], -v[8], -v[12],
                -v[1], v[5], v[9], v[13],
                -v[2], v[6], v[10], v[14],
            };
            rest[index] = new GlbNodePose(0, 0, 0, 0, 0, 0, 1, 1, 1, 1);
            return;
        }
        var t = node.TryGetProperty("translation", out var tv) ? Numbers(tv, 3, "translation") : new[] { 0f, 0f, 0f };
        var r = node.TryGetProperty("rotation", out var rv) ? Numbers(rv, 4, "rotation") : new[] { 0f, 0f, 0f, 1f };
        var sc = node.TryGetProperty("scale", out var sv) ? Numbers(sv, 3, "scale") : new[] { 1f, 1f, 1f };
        rest[index] = new GlbNodePose(-t[0], t[1], t[2], r[0], -r[1], -r[2], r[3], sc[0], sc[1], sc[2]);
    }

    // JSON decimals round once to binary32 so Mono and CoreCLR read the same rest pose.
    private static float[] Numbers(JsonElement array, int count, string field)
    {
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() != count)
            throw new GlbMotionException("glb.rig.structure", $"Node {field} needs {count} numbers.");
        return array.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Number ? PortableDecimal.Parse(x.GetRawText())
            : throw new GlbMotionException("glb.rig.structure", $"Node {field} must hold numbers.")).ToArray();
    }

    private GlbClip ReadClip(string name, JsonElement animation)
    {
        var (duration, frameCount, loop) = ReadExtras(name, animation);
        var samplers = Array(animation, "samplers");
        var channelElements = Array(animation, "channels").EnumerateArray().ToArray();
        if (channelElements.Length == 0) throw new GlbMotionException("glb.clip.empty", $"Clip '{name}' has no channels.");
        // Ticks run 0 .. frameCount and repeat nowhere, so no channel has more keys than that.
        int maxKeys = frameCount + 1;
        float[] Times(int sampler)
        {
            var values = Floats(samplers[sampler].GetProperty("input").GetInt32(), "SCALAR", maxKeys, name);
            for (int i = 1; i < values.Length; i++)
                if (!(values[i] > values[i - 1])) throw new GlbMotionException("glb.clip.timing", $"Clip '{name}' key times must increase.");
            return values;
        }
        float clipStart = channelElements.Min(c => Times(c.GetProperty("sampler").GetInt32())[0]);
        var seen = new HashSet<(int, GlbClip.ChannelPath)>();
        var channels = new List<GlbClip.Channel>();
        foreach (var element in channelElements)
        {
            int samplerIndex = element.GetProperty("sampler").GetInt32();
            var sampler = samplers[samplerIndex];
            var target = element.GetProperty("target");
            if (!target.TryGetProperty("node", out var nodeElement)) throw new GlbMotionException("glb.clip.target", $"Clip '{name}' has a channel without a node.");
            int node = nodeElement.GetInt32();
            if (node >= NodeCount || !scene.Contains(node)) throw new GlbMotionException("glb.clip.target", $"Clip '{name}' animates node {node}, which is not drawn.");
            if (restMatrix[node] != null) throw new GlbMotionException("glb.clip.target", $"Clip '{name}' animates node {node}, which uses a matrix.");
            var path = target.GetProperty("path").GetString() switch
            {
                "translation" => GlbClip.ChannelPath.Translation,
                "rotation" => GlbClip.ChannelPath.Rotation,
                "scale" => GlbClip.ChannelPath.Scale,
                var other => throw new GlbMotionException("glb.clip.target", $"Clip '{name}' animates '{other}'; only translation, rotation and scale are drawn."),
            };
            if (!seen.Add((node, path))) throw new GlbMotionException("glb.clip.target", $"Clip '{name}' animates node {node} {path} twice.");
            string interpolation = sampler.TryGetProperty("interpolation", out var i) ? i.GetString() ?? "" : "LINEAR";
            if (interpolation is not ("LINEAR" or "STEP"))
                throw new GlbMotionException("glb.clip.interpolation", $"Clip '{name}' uses {interpolation} keys; export LINEAR or STEP keys on every 60 Hz frame.");
            var input = Times(samplerIndex);
            int width = path == GlbClip.ChannelPath.Rotation ? 4 : 3;
            var output = Floats(sampler.GetProperty("output").GetInt32(), width == 4 ? "VEC4" : "VEC3", input.Length, name);
            if (output.Length != input.Length * width) throw new GlbMotionException("glb.clip.structure", $"Clip '{name}' has a sampler whose outputs do not match its keys.");
            var ticks = new int[input.Length];
            for (int k = 0; k < input.Length; k++)
            {
                double offset = ((double)input[k] - clipStart) * 60.0;
                double tick = Math.Floor(offset + 0.5);
                if (Math.Abs(offset - tick) > TickTolerance)
                    throw new GlbMotionException("glb.clip.timing", $"Clip '{name}' has a key at {input[k].ToString("R", CultureInfo.InvariantCulture)} s, off the 60 Hz grid from its start; export at 60 fps.");
                if (tick > duration * 60.0 + TickTolerance)
                    throw new GlbMotionException("glb.clip.duration", $"Clip '{name}' has a key after its recorded duration.");
                ticks[k] = (int)tick;
                if (k > 0 && ticks[k] == ticks[k - 1])
                    throw new GlbMotionException("glb.clip.timing", $"Clip '{name}' has two keys on tick {ticks[k]}; export one key per 60 Hz frame.");
                if (k > 0 && ticks[k] > ticks[k - 1] + 1 && interpolation == "LINEAR")
                    for (int c = 0; c < width; c++)
                        if (BitConverter.SingleToInt32Bits(output[(k - 1) * width + c]) != BitConverter.SingleToInt32Bits(output[k * width + c]))
                            throw new GlbMotionException("glb.clip.timing", $"Clip '{name}' skips frames between keys that differ; export a key on every 60 Hz frame.");
            }
            channels.Add(new GlbClip.Channel { Node = node, Path = path, Ticks = ticks, Values = output });
        }
        return new GlbClip(name, frameCount, duration, loop, channels.OrderBy(c => c.Node).ThenBy(c => c.Path).ToArray());
    }

    private static (float Duration, int FrameCount, bool Loop) ReadExtras(string name, JsonElement animation)
    {
        string Missing() => $"Clip '{name}' needs extras.slop with durationSecondsBits and frameCount.";
        if (!animation.TryGetProperty("extras", out var extras) || extras.ValueKind != JsonValueKind.Object
            || !extras.TryGetProperty("slop", out var slop) || slop.ValueKind != JsonValueKind.Object)
            throw new GlbMotionException("glb.clip.extras", Missing());
        int? bits = null, frames = null;
        bool loop = false;
        foreach (var field in slop.EnumerateObject())
            switch (field.Name)
            {
                case "durationSecondsBits": bits = field.Value.GetInt32(); break;
                case "frameCount": frames = field.Value.GetInt32(); break;
                case "loop": loop = field.Value.GetBoolean(); break;
                default: throw new GlbMotionException("glb.clip.extras", $"Clip '{name}' has an unknown extras.slop field '{field.Name}'.");
            }
        if (bits == null || frames == null) throw new GlbMotionException("glb.clip.extras", Missing());
        float duration = BitConverter.Int32BitsToSingle(bits.Value);
        if (!(duration > 0) || float.IsInfinity(duration)) throw new GlbMotionException("glb.clip.extras", $"Clip '{name}' needs a finite positive duration.");
        // Range-checked in floating point; nothing converts to int until the frame count is known to fit.
        double scaled = (float)(duration * 60.0);
        if (!(scaled <= CookedPackageLimits.PoseFrames))
            throw new GlbMotionException("glb.clip.frame-count", $"Clip '{name}' lasts more than {CookedPackageLimits.PoseFrames} frames.");
        int expected = Math.Max(1, (int)Math.Ceiling(scaled));
        if (frames.Value != expected)
            throw new GlbMotionException("glb.clip.frame-count", $"Clip '{name}' records {frames} frames; its duration gives {expected}.");
        return (duration, expected, loop);
    }

    /// <summary>
    /// An animation accessor's floats, read once per accessor and cached. Its count is bounded by
    /// <paramref name="maxCount"/> and the total of distinct accessors by the BIN chunk, before any allocation.
    /// </summary>
    private float[] Floats(int accessorIndex, string type, int maxCount, string clip)
    {
        var accessor = accessors[accessorIndex];
        if (accessor.GetProperty("componentType").GetInt32() != 5126 || accessor.GetProperty("type").GetString() != type
            || accessor.TryGetProperty("normalized", out var normalized) && normalized.GetBoolean())
            throw new GlbMotionException("glb.clip.accessor", $"Clip '{clip}' must store {type} keys as 32-bit floats.");
        long count = accessor.GetProperty("count").GetInt64();
        if (count < 1 || count > maxCount)
            throw new GlbMotionException("glb.clip.keys", $"Clip '{clip}' has an accessor of {count} keys; at most {maxCount} fit its frames.");
        if (accessorValues.TryGetValue(accessorIndex, out var cached)) return cached;
        int width = type == "SCALAR" ? 1 : type == "VEC3" ? 3 : 4;
        long bytes = count * width * 4;
        if (AnimationBytesRead + bytes > accessorBudget)
            throw new GlbMotionException("glb.limit.animation-bytes", $"Clip '{clip}' reads more animation data than the file holds ({accessorBudget} bytes).");
        AnimationBytesRead += bytes;
        var view = views[accessor.GetProperty("bufferView").GetInt32()];
        int stride = view.TryGetProperty("byteStride", out var st) ? st.GetInt32() : 0;
        if (stride == 0) stride = width * 4;
        int start = binOffset + (view.TryGetProperty("byteOffset", out var vo) ? vo.GetInt32() : 0)
            + (accessor.TryGetProperty("byteOffset", out var ao) ? ao.GetInt32() : 0);
        var values = new float[count * width];
        for (int i = 0; i < count; i++)
            for (int c = 0; c < width; c++)
            {
                float value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(glb.AsSpan(start + i * stride + c * 4, 4)));
                if (float.IsNaN(value) || float.IsInfinity(value)) throw new GlbMotionException("glb.clip.value", $"Clip '{clip}' has a non-finite key.");
                values[i * width + c] = value;
            }
        return accessorValues[accessorIndex] = values;
    }

    private static readonly JsonElement EmptyArray = JsonDocument.Parse("[]").RootElement.Clone();
    private static JsonElement Array(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value : EmptyArray;
}
