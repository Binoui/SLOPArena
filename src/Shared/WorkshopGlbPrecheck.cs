using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text.Json;

namespace SlopArena.Shared;

public sealed record WorkshopGlbLimits
{
    public static WorkshopGlbLimits Default { get; } = new();
    public long MaxFileBytes { get; init; } = 30000000;
    public long MaxTriangles { get; init; } = 250000;
    public long MaxVertices { get; init; } = 260000;
    public long MaxNodes { get; init; } = 300;
    public long MaxJoints { get; init; } = 250;
    public long MaxMaterials { get; init; } = 20;
    public long MaxImages { get; init; } = 16;
    public long MaxImageDimension { get; init; } = 4096;
    public long MaxImagePixels { get; init; } = 16777216;
    public int MaxJsonBytes { get; init; } = 1200000;
    public int MaxJsonDepth { get; init; } = 64;
}

public sealed record WorkshopGlbImageSize(long Width, long Height);
public sealed record WorkshopGlbMeasurements
{
    public long FileBytes { get; internal set; }
    public long Triangles { get; internal set; }
    public long Vertices { get; internal set; }
    public long Nodes { get; internal set; }
    public long MaxJoints { get; internal set; }
    public long Materials { get; internal set; }
    public long Images { get; internal set; }
    public IReadOnlyList<WorkshopGlbImageSize> ImageSizes { get; internal set; } = Array.Empty<WorkshopGlbImageSize>();
}
public sealed record WorkshopGlbPrecheckResult(bool IsAccepted, IReadOnlyList<CharacterDiagnostic> Diagnostics, WorkshopGlbMeasurements Measurements);

public static class WorkshopGlbPrecheck
{
    public static WorkshopGlbPrecheckResult Check(ReadOnlySpan<byte> glb, WorkshopGlbLimits limits)
        => Check(glb, limits, false);

    internal static WorkshopGlbPrecheckResult Check(ReadOnlySpan<byte> glb, WorkshopGlbLimits limits, bool portableImages)
    {
        if (limits == null) throw new ArgumentNullException(nameof(limits));
        var m = new WorkshopGlbMeasurements { FileBytes = glb.Length };
        var d = new List<CharacterDiagnostic>();
        void Limit(string name, long measured, long max)
        { if (measured > max) d.Add(MatchContentCatalogBuilder.Error("glb.limit." + name, name, $"Limit {max}; measured {measured}.")); }
        WorkshopGlbPrecheckResult Result() => new(d.Count == 0, d.AsReadOnly(), m);
        Limit("file-bytes", glb.Length, limits.MaxFileBytes);
        if (d.Count != 0) return Result();
        try
        {
            Require(glb.Length >= 20 && U32(glb, 0) == 0x46546c67 && U32(glb, 4) == 2 && U32(glb, 8) == glb.Length, "glb.container", "Expected binary glTF 2.0 with matching file length.");
            int jsonOffset = 0, jsonLength = 0, binOffset = 0, binLength = 0, chunks = 0;
            for (int offset = 12; offset < glb.Length; chunks++)
            {
                Require(glb.Length - offset >= 8, "glb.container", "Truncated chunk header.");
                uint length = U32(glb, offset), type = U32(glb, offset + 4);
                Require(length % 4 == 0 && length <= glb.Length - offset - 8, "glb.container", "Chunk length exceeds file or is not aligned.");
                if (chunks == 0)
                {
                    Require(type == 0x4e4f534a, "glb.container", "First chunk must be JSON.");
                    jsonOffset = offset + 8; jsonLength = (int)length;
                }
                else if (type == 0x004e4942)
                { Require(binOffset == 0 && chunks == 1, "glb.container", "BIN must be the second chunk and unique."); binOffset = offset + 8; binLength = (int)length; }
                else Require(type != 0x4e4f534a, "glb.container", "Duplicate JSON chunk.");
                offset += 8 + (int)length;
            }
            Limit("json-bytes", jsonLength, limits.MaxJsonBytes);
            if (d.Count != 0) return Result();
            // Hard ceilings keep caller-supplied limits from disabling parser bounds.
            Require(jsonLength <= 16000000 && limits.MaxJsonDepth > 0 && limits.MaxJsonDepth <= 128, "glb.json", "JSON parser bounds exceeded.");
            using var doc = JsonDocument.Parse(glb.Slice(jsonOffset, jsonLength).ToArray(), new JsonDocumentOptions { MaxDepth = limits.MaxJsonDepth });
            var root = doc.RootElement;
            ValidateObjects(root);
            Require(root.ValueKind == JsonValueKind.Object && root.TryGetProperty("asset", out var asset) && asset.ValueKind == JsonValueKind.Object && asset.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.String && version.GetString() == "2.0", "glb.structure", "Expected glTF asset version 2.0.");
            // The importer reads quantized meshes without an extra package, and the game's own exports require them.
            foreach (var extension in Array(root, "extensionsRequired").EnumerateArray())
                if (extension.ValueKind != JsonValueKind.String || extension.GetString() != "KHR_mesh_quantization")
                    d.Add(MatchContentCatalogBuilder.Error("glb.extension.required", "extensionsRequired", "Unsupported required extension: " + extension.ToString()));
            var buffers = Array(root, "buffers");
            Require(buffers.GetArrayLength() <= 1, "glb.buffer.bounds", "GLB may contain only its embedded buffer.");
            long bufferLength = 0;
            foreach (var buffer in buffers.EnumerateArray())
            {
                NoUri(buffer);
                bufferLength = Number(buffer, "byteLength");
                Require(binOffset != 0 && bufferLength <= binLength && binLength - bufferLength <= 3, "glb.buffer.bounds", "Buffer length disagrees with BIN chunk.");
            }
            var views = Array(root, "bufferViews");
            foreach (var view in views.EnumerateArray())
            {
                Require(Number(view, "buffer") == 0 && buffers.GetArrayLength() == 1, "glb.buffer.bounds", "Buffer view references missing buffer.");
                var offset = Number(view, "byteOffset", 0); var length = Number(view, "byteLength");
                Require(offset <= bufferLength && length <= bufferLength - offset, "glb.buffer.bounds", "Buffer view lies outside embedded buffer.");
            }
            var accessors = Array(root, "accessors");
            var meshes = Array(root, "meshes");
            foreach (var mesh in meshes.EnumerateArray()) foreach (var primitive in Array(mesh, "primitives").EnumerateArray())
            {
                Require(primitive.TryGetProperty("attributes", out var attributes) && attributes.ValueKind == JsonValueKind.Object, "glb.structure", "Primitive requires attributes.");
                var positions = At(accessors, Number(attributes, "POSITION"));
                long vertices = Number(positions, "count");
                m.Vertices = checked(m.Vertices + vertices);
                long count = primitive.TryGetProperty("indices", out _) ? Number(At(accessors, Number(primitive, "indices")), "count") : vertices;
                var mode = Number(primitive, "mode", 4);
                Require(mode <= 6, "glb.structure", "Invalid primitive mode.");
                m.Triangles = checked(m.Triangles + (mode == 4 ? count / 3 : mode == 5 || mode == 6 ? Math.Max(0, count - 2) : 0));
            }
            var nodes = Array(root, "nodes"); var skins = Array(root, "skins");
            bool skinned = false;
            foreach (var skin in skins.EnumerateArray())
            {
                var joints = Array(skin, "joints"); m.MaxJoints = Math.Max(m.MaxJoints, joints.GetArrayLength());
                foreach (var joint in joints.EnumerateArray()) At(nodes, Integer(joint));
            }
            foreach (var node in nodes.EnumerateArray())
                if (node.TryGetProperty("skin", out _) && node.TryGetProperty("mesh", out _))
                {
                    var skin = At(skins, Number(node, "skin")); var mesh = At(meshes, Number(node, "mesh"));
                    if (Array(skin, "joints").GetArrayLength() > 0 && Array(mesh, "primitives").GetArrayLength() > 0) skinned = true;
                }
            Require(skinned, "glb.skin.missing", "At least one node must reference a mesh and a nonempty skin.");
            m.Nodes = nodes.GetArrayLength(); m.Materials = Array(root, "materials").GetArrayLength();
            var images = Array(root, "images"); m.Images = images.GetArrayLength();
            var sizes = new List<WorkshopGlbImageSize>(); m.ImageSizes = sizes.AsReadOnly();
            foreach (var image in images.EnumerateArray())
            {
                NoUri(image);
                var view = At(views, Number(image, "bufferView"));
                var bytes = glb.Slice(checked(binOffset + (int)Number(view, "byteOffset", 0)), (int)Number(view, "byteLength"));
                var size = ImageSize(bytes);
                if (portableImages) RequireSingleJpegFrame(bytes);
                if (image.TryGetProperty("mimeType", out var mime))
                    Require(mime.ValueKind == JsonValueKind.String && mime.GetString() == (bytes[0] == 137 ? "image/png" : "image/jpeg"), "glb.image.format", "Image MIME type disagrees with PNG/JPEG header.");
                sizes.Add(size);
                Limit("image-dimension", Math.Max(size.Width, size.Height), limits.MaxImageDimension);
                Limit("image-pixels", size.Width * size.Height, limits.MaxImagePixels);
            }
            Limit("triangles", m.Triangles, limits.MaxTriangles); Limit("vertices", m.Vertices, limits.MaxVertices);
            Limit("nodes", m.Nodes, limits.MaxNodes); Limit("joints", m.MaxJoints, limits.MaxJoints);
            Limit("materials", m.Materials, limits.MaxMaterials); Limit("images", m.Images, limits.MaxImages);
            RequireNodeForest(root, nodes, meshes, skins);
            RequireAccessorsInBounds(accessors, views, meshes, skins);
        }
        catch (Refusal ex) { d.Add(MatchContentCatalogBuilder.Error(ex.Code, "glb", ex.Message)); }
        catch (JsonException ex) { d.Add(MatchContentCatalogBuilder.Error("glb.json", "glb", ex.Message)); }
        catch (InvalidOperationException ex) { d.Add(MatchContentCatalogBuilder.Error("glb.json", "glb", ex.Message)); }
        catch (OverflowException) { d.Add(MatchContentCatalogBuilder.Error("glb.structure", "glb", "Measured counts overflow 64-bit range.")); }
        return Result();
    }

    /// <summary>
    /// glTFast walks parent links to find a skin's skeleton root and recurses through children when it builds the
    /// scene, so a node graph that is not a forest of trees can hang or overflow the client. The walk here is
    /// iterative and visits each node once.
    /// </summary>
    private static void RequireNodeForest(JsonElement root, JsonElement nodes, JsonElement meshes, JsonElement skins)
    {
        const string forest = "glb.nodes.forest";
        int count = nodes.GetArrayLength();
        var parent = new int[count];
        for (int i = 0; i < count; i++) parent[i] = -1;
        for (int i = 0; i < count; i++)
        {
            var node = nodes[i];
            if (node.TryGetProperty("mesh", out _)) At(meshes, Number(node, "mesh"));
            if (node.TryGetProperty("skin", out _)) At(skins, Number(node, "skin"));
            Require(!(node.TryGetProperty("extensions", out var extensions) && extensions.ValueKind == JsonValueKind.Object
                && extensions.TryGetProperty("EXT_mesh_gpu_instancing", out _)), "glb.extension.instancing",
                $"Node {i} uses EXT_mesh_gpu_instancing; a model swap draws one fighter.");
            foreach (var entry in Array(node, "children").EnumerateArray())
            {
                long child = Integer(entry);
                Require(child < count, forest, $"Node {i} lists child {child}, which does not exist.");
                Require(parent[child] < 0, forest, $"Node {child} has two parents, nodes {parent[child]} and {i}.");
                parent[child] = i;
            }
        }
        // 0: not reached yet, 1: on the walk being followed, 2: known to reach a root.
        var state = new byte[count];
        var walk = new List<int>();
        for (int i = 0; i < count; i++)
        {
            int n = i;
            walk.Clear();
            while (n >= 0 && state[n] == 0)
            {
                state[n] = 1;
                walk.Add(n);
                n = parent[n];
            }
            Require(n < 0 || state[n] == 2, forest, $"Node {n} is its own ancestor.");
            foreach (int visited in walk) state[visited] = 2;
        }
        var scenes = Array(root, "scenes");
        if (root.TryGetProperty("scene", out _)) At(scenes, Number(root, "scene"));
        foreach (var scene in scenes.EnumerateArray())
        {
            var roots = new HashSet<long>();
            foreach (var entry in Array(scene, "nodes").EnumerateArray())
            {
                long n = Integer(entry);
                Require(n < count, forest, $"A scene lists node {n}, which does not exist.");
                Require(parent[n] < 0, forest, $"Scene root {n} is also a child of node {parent[n]}.");
                Require(roots.Add(n), forest, $"A scene lists node {n} twice.");
            }
        }
        foreach (var skin in skins.EnumerateArray())
            if (skin.TryGetProperty("skeleton", out _)) At(nodes, Number(skin, "skeleton"));
    }

    private static readonly Dictionary<long, int> ComponentBytes = new() { [5120] = 1, [5121] = 1, [5122] = 2, [5123] = 2, [5125] = 4, [5126] = 4 };
    private static readonly Dictionary<string, int> ComponentCount = new(StringComparer.Ordinal)
    { ["SCALAR"] = 1, ["VEC2"] = 2, ["VEC3"] = 3, ["VEC4"] = 4, ["MAT2"] = 4, ["MAT3"] = 9, ["MAT4"] = 16 };

    /// <summary>
    /// glTFast sizes what it allocates from each accessor's count, including animation accessors it loads even with
    /// clips off. Every accessor must fit inside its buffer view, every attribute of a primitive must count as many
    /// vertices as POSITION, and the features a model swap never needs are refused: sparse accessors and morph targets.
    /// </summary>
    private static void RequireAccessorsInBounds(JsonElement accessors, JsonElement views, JsonElement meshes, JsonElement skins)
    {
        int index = 0;
        foreach (var accessor in accessors.EnumerateArray())
        {
            int a = index++;
            Require(!accessor.TryGetProperty("sparse", out _), "glb.accessor.sparse", $"Accessor {a} is sparse; a model swap needs none.");
            Require(accessor.TryGetProperty("bufferView", out _), "glb.accessor.bounds", $"Accessor {a} has no buffer view.");
            var view = At(views, Number(accessor, "bufferView"));
            Require(ComponentBytes.TryGetValue(Number(accessor, "componentType"), out int bytes), "glb.accessor.bounds", $"Accessor {a} has an unknown component type.");
            int components = 0;
            Require(accessor.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
                && ComponentCount.TryGetValue(type.GetString()!, out components), "glb.accessor.bounds", $"Accessor {a} has an unknown type.");
            long count = Number(accessor, "count");
            Require(count >= 1, "glb.accessor.bounds", $"Accessor {a} is empty.");
            long element = (long)bytes * components;
            long stride = Number(view, "byteStride", 0);
            if (stride == 0) stride = element;
            Require(stride >= element, "glb.accessor.bounds", $"Accessor {a} is wider than its buffer view's stride.");
            long end = checked(Number(accessor, "byteOffset", 0) + (count - 1) * stride + element);
            Require(end <= Number(view, "byteLength"), "glb.accessor.bounds", $"Accessor {a} reads past the end of its buffer view.");
        }
        foreach (var mesh in meshes.EnumerateArray()) foreach (var primitive in Array(mesh, "primitives").EnumerateArray())
        {
            Require(!primitive.TryGetProperty("targets", out _), "glb.morph-targets", "Morph targets are not supported in a model swap.");
            var attributes = primitive.GetProperty("attributes");
            long vertices = Number(At(accessors, Number(attributes, "POSITION")), "count");
            foreach (var attribute in attributes.EnumerateObject())
                Require(Number(At(accessors, Integer(attribute.Value)), "count") == vertices, "glb.accessor.count",
                    $"Attribute {attribute.Name} counts a different number of vertices than POSITION.");
        }
        foreach (var skin in skins.EnumerateArray())
            if (skin.TryGetProperty("inverseBindMatrices", out _))
            {
                var matrices = At(accessors, Number(skin, "inverseBindMatrices"));
                Require(Number(matrices, "count") == Array(skin, "joints").GetArrayLength()
                    && matrices.GetProperty("type").GetString() == "MAT4", "glb.skin.inverse-bind",
                    "A skin's inverse bind matrices must be one MAT4 per joint.");
            }
    }

    internal static IReadOnlyList<CharacterDiagnostic> CheckImage(byte[] bytes)
    {
        try
        {
            var size = ImageSize(bytes);
            RequireSingleJpegFrame(bytes);
            if (size.Width > WorkshopGlbLimits.Default.MaxImageDimension || size.Height > WorkshopGlbLimits.Default.MaxImageDimension
                || size.Width * size.Height > WorkshopGlbLimits.Default.MaxImagePixels)
                return new[] { MatchContentCatalogBuilder.Error("item.image.limit", "image", "Image dimensions exceed the GLB texture limits.") };
            return System.Array.Empty<CharacterDiagnostic>();
        }
        catch (Refusal ex) { return new[] { MatchContentCatalogBuilder.Error(ex.Code, "image", ex.Message) }; }
    }

    private static void RequireSingleJpegFrame(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 2 || bytes[0] != 255 || bytes[1] != 216) return;
        int frames = 0, offset = 2;
        bool entropy = false;
        while (offset < bytes.Length)
        {
            if (entropy)
            {
                while (offset < bytes.Length && bytes[offset] != 255) offset++;
                if (offset == bytes.Length) break;
            }
            Require(bytes[offset++] == 255, "item.jpeg", "Malformed JPEG marker.");
            while (offset < bytes.Length && bytes[offset] == 255) offset++;
            Require(offset < bytes.Length, "item.jpeg", "Truncated JPEG marker.");
            int marker = bytes[offset++];
            if (entropy && (marker == 0 || marker is >= 208 and <= 215)) continue;
            if (marker == 217)
            {
                Require(frames == 1 && offset == bytes.Length, "item.jpeg", "JPEG requires exactly one frame and no trailing image.");
                return;
            }
            Require(marker != 216 && marker != 0, "item.jpeg", "Multiple JPEG images are refused.");
            entropy = false;
            if (marker == 1 || marker is >= 208 and <= 215) continue;
            Require(bytes.Length - offset >= 2, "item.jpeg", "Truncated JPEG segment.");
            int length = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset, 2));
            Require(length >= 2 && length <= bytes.Length - offset, "item.jpeg", "JPEG segment exceeds image.");
            if (marker >= 192 && marker <= 207 && marker != 196 && marker != 200 && marker != 204)
                Require(++frames == 1, "item.jpeg", "Multiple JPEG frame headers are refused.");
            offset += length;
            if (marker == 218) entropy = true;
        }
        throw new Refusal("item.jpeg", "JPEG must end after exactly one frame.");
    }

    private static WorkshopGlbImageSize ImageSize(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 33 && b.Slice(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            Require(BinaryPrimitives.ReadUInt32BigEndian(b.Slice(8, 4)) == 13 && b.Slice(12, 4).SequenceEqual(new byte[] { 73, 72, 68, 82 }), "glb.image.format", "PNG requires IHDR.");
            long w = BinaryPrimitives.ReadUInt32BigEndian(b.Slice(16, 4)), h = BinaryPrimitives.ReadUInt32BigEndian(b.Slice(20, 4));
            Require(w > 0 && h > 0 && w <= int.MaxValue && h <= int.MaxValue, "glb.image.format", "Invalid PNG dimensions."); return new(w, h);
        }
        if (b.Length >= 4 && b[0] == 255 && b[1] == 216)
        {
            int offset = 2;
            while (offset < b.Length)
            {
                Require(b[offset++] == 255, "glb.image.format", "Malformed JPEG marker.");
                while (offset < b.Length && b[offset] == 255) offset++;
                Require(offset < b.Length, "glb.image.format", "Truncated JPEG marker.");
                int marker = b[offset++];
                Require(marker != 0 && marker != 217 && marker != 218, "glb.image.format", "JPEG has no frame header.");
                if (marker == 1 || marker >= 208 && marker <= 215) continue;
                Require(b.Length - offset >= 2, "glb.image.format", "Truncated JPEG segment.");
                int length = BinaryPrimitives.ReadUInt16BigEndian(b.Slice(offset, 2));
                Require(length >= 2 && length <= b.Length - offset, "glb.image.format", "JPEG segment exceeds image.");
                if (marker >= 192 && marker <= 207 && marker != 196 && marker != 200 && marker != 204)
                {
                    Require(length >= 8, "glb.image.format", "Truncated JPEG frame.");
                    int h = BinaryPrimitives.ReadUInt16BigEndian(b.Slice(offset + 3, 2)), w = BinaryPrimitives.ReadUInt16BigEndian(b.Slice(offset + 5, 2));
                    Require(w > 0 && h > 0, "glb.image.format", "Invalid JPEG dimensions."); return new(w, h);
                }
                offset += length;
            }
        }
        throw new Refusal("glb.image.format", "Expected embedded PNG or JPEG frame header.");
    }
    private static void ValidateObjects(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in e.EnumerateObject()) { Require(seen.Add(p.Name), "glb.structure", "Duplicate JSON property: " + p.Name); ValidateObjects(p.Value); }
        }
        else if (e.ValueKind == JsonValueKind.Array) foreach (var v in e.EnumerateArray()) ValidateObjects(v);
        else if (e.ValueKind == JsonValueKind.String) _ = e.GetString();
    }
    private static readonly JsonElement EmptyArray = JsonDocument.Parse("[]").RootElement.Clone();
    private static JsonElement Array(JsonElement e, string name)
    { Require(e.ValueKind == JsonValueKind.Object, "glb.structure", "Expected object."); if (!e.TryGetProperty(name, out var a)) return EmptyArray; Require(a.ValueKind == JsonValueKind.Array, "glb.structure", "Expected array: " + name); return a; }
    private static long Number(JsonElement e, string name, long fallback = -1)
    { Require(e.ValueKind == JsonValueKind.Object, "glb.structure", "Expected object."); return e.TryGetProperty(name, out var v) ? Integer(v) : fallback >= 0 ? fallback : throw new Refusal("glb.structure", "Missing integer: " + name); }
    private static long Integer(JsonElement v)
    { Require(v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out _), "glb.structure", "Expected nonnegative integer."); long n = v.GetInt64(); Require(n >= 0, "glb.structure", "Negative integer."); return n; }
    private static JsonElement At(JsonElement a, long i)
    { Require(i < a.GetArrayLength(), "glb.structure", "Index outside array."); return a[(int)i]; }
    private static void NoUri(JsonElement e)
    { Require(e.ValueKind == JsonValueKind.Object, "glb.structure", "Expected object."); Require(!e.TryGetProperty("uri", out _), "glb.uri", "External resources and data URIs are forbidden."); }
    private static uint U32(ReadOnlySpan<byte> b, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(offset, 4));
    private static void Require(bool condition, string code, string message) { if (!condition) throw new Refusal(code, message); }
    private sealed class Refusal : Exception { public string Code { get; } public Refusal(string code, string message) : base(message) { Code = code; } }
}
