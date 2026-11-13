using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace SlopArena.Shared;

public sealed record PortableMeshFit(float ModelYOffset, IReadOnlyList<HurtboxBoneSource> Hurtboxes, IReadOnlyList<CharacterDiagnostic> Diagnostics);
internal readonly struct MeshPoint
{
    internal readonly double X, Y, Z;
    internal MeshPoint(double x, double y, double z) { X = x; Y = y; Z = z; }
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static MeshPoint operator +(MeshPoint a, MeshPoint b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static MeshPoint operator -(MeshPoint a, MeshPoint b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static MeshPoint operator *(double s, MeshPoint p) => new(s * p.X, s * p.Y, s * p.Z);
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    internal static double Dot(MeshPoint a, MeshPoint b) => (a.X * b.X + a.Y * b.Y) + a.Z * b.Z;
    internal double Length() => Math.Sqrt(Dot(this, this));
}

/// <summary>Measures the drawn skin at idle frame zero in root-rule-1 coordinates.</summary>
public static class PortableMeshMeasurements
{
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static PortableMeshFit Measure(byte[] glb, string? humanoidJson, string idleClip, float visualScale, CancellationToken cancel = default)
    {
        var diagnostics = new List<CharacterDiagnostic>();
        var rig = GlbMotionRig.Read(glb, humanoidJson);
        var clip = rig.Clip(idleClip);
        var local = new GlbNodePose[rig.NodeCount];
        rig.SampleLocal(clip, 0, local);
        var world = new double[rig.NodeCount * 12];
        rig.World(local, rig.SceneOrder, world);
        int jsonLength = BinaryPrimitives.ReadInt32LittleEndian(glb.AsSpan(12, 4));
        int bin = 28 + jsonLength;
        using var doc = JsonDocument.Parse(glb.AsMemory(20, jsonLength));
        var root = doc.RootElement;
        double[] Accessor(int index, int width)
        {
            var a = root.GetProperty("accessors")[index];
            var v = root.GetProperty("bufferViews")[a.GetProperty("bufferView").GetInt32()];
            int type = a.GetProperty("componentType").GetInt32(), size = type == 5126 || type == 5125 ? 4 : type == 5123 ? 2 : type == 5121 ? 1 : 0;
            if (size == 0) throw new InvalidDataException("Mesh measurements need float, unsigned byte or unsigned short accessors.");
            if (a.GetProperty("type").GetString() != (width == 16 ? "MAT4" : "VEC" + width)) throw new InvalidDataException("Unexpected mesh accessor shape.");
            int count = a.GetProperty("count").GetInt32();
            if (count > 250000) throw new InvalidDataException("Mesh measurement accessor exceeds 250000 entries.");
            int offset = bin + (v.TryGetProperty("byteOffset", out var vo) ? vo.GetInt32() : 0) + (a.TryGetProperty("byteOffset", out var ao) ? ao.GetInt32() : 0);
            int stride = v.TryGetProperty("byteStride", out var st) ? st.GetInt32() : size * width;
            bool normalized = a.TryGetProperty("normalized", out var norm) && norm.GetBoolean();
            var values = new double[checked(count * width)];
            for (int i = 0; i < count; i++)
                for (int k = 0; k < width; k++)
                {
                    int at = checked(offset + i * stride + k * size);
                    double value = type == 5126 ? (double)BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(glb.AsSpan(at, 4)))
                        : type == 5125 ? BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(at, 4)) : type == 5123 ? BinaryPrimitives.ReadUInt16LittleEndian(glb.AsSpan(at, 2)) : glb[at];
                    if (normalized && type != 5126) value /= type == 5121 ? 255.0 : 65535.0;
                    if (double.IsNaN(value) || double.IsInfinity(value)) throw new InvalidDataException("Mesh contains a non-finite value.");
                    values[i * width + k] = value;
                }
            return values;
        }
        MeshPoint Model(double x, double y, double z)
        {
            rig.Root.Apply(x, y, z, visualScale, out double ox, out double oy, out double oz);
            return new MeshPoint(ox, oy, oz);
        }
        MeshPoint BoneNode(int n) => Model(world[n * 12 + 3], world[n * 12 + 7], world[n * 12 + 11]);
        MeshPoint? Bone(string id)
        {
            var human = CharacterBoneRegistry.Find(id)?.HumanoidBone;
            string? name = human == null ? null : rig.BoneMap.NodeFor(human);
            int n = name == null ? -1 : rig.FindNode(name);
            return n < 0 ? null : BoneNode(n);
        }
        var mapped = CharacterBoneRegistry.HumanoidPoints.Select(p => (Point: p, Name: rig.BoneMap.NodeFor(p.HumanoidBone)))
            .Where(p => p.Name != null).GroupBy(p => p.Name!).ToDictionary(g => g.Key, g => g.First().Point.Id);
        string RegionOf(int n)
        {
            while (n >= 0)
            {
                if (rig.NodeNames[n] is { } name && mapped.TryGetValue(name, out var id)) return Region(id);
                n = rig.Parents[n];
            }
            return "other";
        }
        var points = new List<MeshPoint>(); var regions = new List<string>(); var rigid = new List<bool>();
        foreach (int node in rig.SceneOrder)
        {
            cancel.ThrowIfCancellationRequested();
            var n = root.GetProperty("nodes")[node];
            if (!n.TryGetProperty("mesh", out var mesh)) continue;
            if (!n.TryGetProperty("skin", out var skinIndex))
            {
                string region = RegionOf(node);
                if (region == "other")
                {
                    diagnostics.Add(new CharacterDiagnostic(CharacterDiagnosticSeverity.Warning, "mesh.fit.detached", "nodes[" + node + "]", "Rigid mesh '" + (rig.NodeNames[node] ?? node.ToString()) + "' is outside the mapped skeleton and is not included in hurtboxes or the sole. Parent it to a mapped joint to include it."));
                    continue;
                }
                foreach (var primitive in root.GetProperty("meshes")[mesh.GetInt32()].GetProperty("primitives").EnumerateArray())
                {
                    if (primitive.TryGetProperty("targets", out _)) throw new InvalidDataException("Rigid mesh fitting does not support morph targets.");
                    var positions = Accessor(primitive.GetProperty("attributes").GetProperty("POSITION").GetInt32(), 3);
                    if (points.Count + positions.Length / 3 > 250000) throw new InvalidDataException("Too many mesh vertices to fit.");
                    for (int i = 0; i < positions.Length; i += 3)
                    {
                        double x = -positions[i], y = positions[i + 1], z = positions[i + 2]; int w = node * 12;
                        points.Add(Model(world[w] * x + world[w + 1] * y + world[w + 2] * z + world[w + 3], world[w + 4] * x + world[w + 5] * y + world[w + 6] * z + world[w + 7], world[w + 8] * x + world[w + 9] * y + world[w + 10] * z + world[w + 11]));
                        regions.Add(region); rigid.Add(true);
                    }
                }
                continue;
            }
            var skin = root.GetProperty("skins")[skinIndex.GetInt32()];
            var joints = skin.GetProperty("joints").EnumerateArray().Select(j => j.GetInt32()).ToArray();
            double[]? inverse = skin.TryGetProperty("inverseBindMatrices", out var ibm) ? Accessor(ibm.GetInt32(), 16) : null;
            foreach (var primitive in root.GetProperty("meshes")[mesh.GetInt32()].GetProperty("primitives").EnumerateArray())
            {
                var a = primitive.GetProperty("attributes");
                if (a.TryGetProperty("JOINTS_1", out _) || primitive.TryGetProperty("targets", out _)) throw new InvalidDataException("Capsule fitting currently supports four skin influences and no morph targets.");
                var positions = Accessor(a.GetProperty("POSITION").GetInt32(), 3);
                var indices = Accessor(a.GetProperty("JOINTS_0").GetInt32(), 4);
                var weights = Accessor(a.GetProperty("WEIGHTS_0").GetInt32(), 4);
                int count = positions.Length / 3;
                if (indices.Length != count * 4 || weights.Length != count * 4 || count > 250000 - points.Count) throw new InvalidDataException("Invalid or oversized skin arrays.");
                for (int i = 0; i < count; i++)
                {
                    double px = positions[i * 3], py = positions[i * 3 + 1], pz = positions[i * 3 + 2];
                    double x = 0, y = 0, z = 0, sum = 0, best = -1; int dominant = -1;
                    for (int k = 0; k < 4; k++)
                    {
                        double weight = weights[i * 4 + k];
                        if (weight <= 0) continue;
                        int j = checked((int)indices[i * 4 + k]);
                        if (j >= joints.Length) throw new InvalidDataException("Skin joint index is outside the joint array.");
                        double bx = px, by = py, bz = pz;
                        if (inverse != null)
                        {
                            int m = j * 16;
                            bx = inverse[m] * px + inverse[m + 4] * py + inverse[m + 8] * pz + inverse[m + 12];
                            by = inverse[m + 1] * px + inverse[m + 5] * py + inverse[m + 9] * pz + inverse[m + 13];
                            bz = inverse[m + 2] * px + inverse[m + 6] * py + inverse[m + 10] * pz + inverse[m + 14];
                        }
                        bx = -bx;
                        int w = joints[j] * 12;
                        x += weight * (world[w] * bx + world[w + 1] * by + world[w + 2] * bz + world[w + 3]);
                        y += weight * (world[w + 4] * bx + world[w + 5] * by + world[w + 6] * bz + world[w + 7]);
                        z += weight * (world[w + 8] * bx + world[w + 9] * by + world[w + 10] * bz + world[w + 11]);
                        sum += weight;
                        if (weight > best) { best = weight; dominant = joints[j]; }
                    }
                    if (sum <= 0) throw new InvalidDataException("Skin vertex has no positive weights.");
                    points.Add(Model(x / sum, y / sum, z / sum)); regions.Add(RegionOf(dominant)); rigid.Add(false);
                }
            }
        }
        if (points.Count == 0) throw new InvalidDataException("No skinned mesh vertices to measure.");
        var soles = points.Where((p, i) => regions[i] == "foot").Select(p => p.Y).ToArray();
        if (soles.Length == 0) throw new InvalidDataException("No foot vertices to measure the idle sole.");
        var fit = FitCapsules(points, regions, Bone, diagnostics, rigid);
        return new PortableMeshFit((float)Math.Round(-soles.Min(), 4), fit, diagnostics);
    }
        internal static HurtboxBoneSource[] FitCapsules(IReadOnlyList<Vector3> points, IReadOnlyList<string> regions, Func<string, Vector3?> bone)
            => FitCapsules(points.Select(p => new MeshPoint(p.X, p.Y, p.Z)).ToArray(), regions,
                id => bone(id) is { } p ? new MeshPoint(p.X, p.Y, p.Z) : (MeshPoint?)null, new List<CharacterDiagnostic>());
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        internal static HurtboxBoneSource[] FitCapsules(IReadOnlyList<MeshPoint> points, IReadOnlyList<string> regions,
            Func<string, MeshPoint?> bone, List<CharacterDiagnostic> diagnostics, IReadOnlyList<bool>? rigid = null)
        {
            var segs = new List<(string Start, string End, string Region)>();
            void Segment(string start, string end, string region)
            {
                if (bone(start) == null) throw new InvalidDataException("Capsule fit needs " + start);
                segs.Add((start, bone(end) == null ? start : end, region));
            }
            Segment("bone.head", "bone.head", "head");
            Segment("bone.hips", "bone.neck", "torso");
            foreach (var side in new[] { "right", "left" })
            {
                Segment($"bone.{side}-upper-arm", $"bone.{side}-lower-arm", "upperarm");
                Segment($"bone.{side}-lower-arm", $"bone.{side}-hand", "forearm");
                Segment($"bone.{side}-hand", $"bone.{side}-hand-end", "hand");
                Segment($"bone.{side}-upper-leg", $"bone.{side}-lower-leg", "thigh");
                Segment($"bone.{side}-lower-leg", $"bone.{side}-foot", "shin");
                Segment($"bone.{side}-foot", $"bone.{side}-toes", "foot");
            }
            double Pct(List<double> values, double f)
            {
                if (values.Count == 0) return 0;
                values.Sort();
                return values[Math.Min(values.Count - 1, (int)(values.Count * f))];
            }
            var head = bone("bone.head")!.Value;
            var headUp = new List<double>();
            for (int i = 0; i < points.Count; i++)
                if (regions[i] == "head") headUp.Add(Math.Max(0, points[i].Y - head.Y));
            double rigidHeadUp = 0;
            for (int i = 0; i < points.Count; i++) if (rigid != null && rigid[i] && regions[i] == "head") rigidHeadUp = Math.Max(rigidHeadUp, points[i].Y - head.Y);
            double headEnd = Math.Round(Math.Max(Pct(headUp, .95), rigidHeadUp) * .5, 4);
            var starts = segs.Select(s => bone(s.Start)!.Value).ToArray();
            var ends = segs.Select((s, k) => bone(s.End)!.Value + (k == 0 ? new MeshPoint(0, headEnd, 0) : new MeshPoint(0, 0, 0))).ToArray();
            double Dist(MeshPoint p, MeshPoint a, MeshPoint b)
            {
                var d = b - a;
                double dd = MeshPoint.Dot(d, d);
                double t = dd >= 1e-10 ? Math.Min(1, Math.Max(0, MeshPoint.Dot(p - a, d) / dd)) : 0;
                return (p - a - t * d).Length();
            }
            var result = new List<HurtboxBoneSource>();
            for (int k = 0; k < segs.Count; k++)
            {
                var kept = new List<double>(); double rigidRadius = 0;
                for (int i = 0; i < points.Count; i++)
                {
                    if (regions[i] != segs[k].Region) continue;
                    double mine = Dist(points[i], starts[k], ends[k]), best = mine;
                    for (int j = 0; j < segs.Count; j++)
                        if (segs[j].Region == segs[k].Region) best = Math.Min(best, Dist(points[i], starts[j], ends[j]));
                    if (mine <= best + 1e-5) { kept.Add(mine); if (rigid != null && rigid[i]) rigidRadius = Math.Max(rigidRadius, mine); }
                }
                if (segs[k].Region == "torso" && kept.Count > 0)
                {
                    double width = (bone("bone.left-upper-arm")!.Value - bone("bone.right-upper-arm")!.Value).Length();
                    double limit = Math.Max(.015, width * .5);
                    int excluded = kept.Count(x => x > limit);
                    if (excluded > 0) diagnostics.Add(new CharacterDiagnostic(CharacterDiagnosticSeverity.Warning, "mesh.fit.loose-torso", "hurtboxBoneDefs[1]", excluded + " torso-weighted vertices extend beyond the shoulder-width body envelope. Capes, coats, skirts and tails are not body thickness; the torso radius is capped at " + limit.ToString("G9", System.Globalization.CultureInfo.InvariantCulture) + " m. Inspect the fit."));
                    kept = kept.Select(x => Math.Min(x, limit)).ToList();
                    rigidRadius = Math.Min(rigidRadius, limit);
                }
                float radius = (float)Math.Round(Math.Max(.015, Math.Max(Pct(kept, .85), rigidRadius)), 4);
                result.Add(new HurtboxBoneSource(segs[k].Start, 0f, 0f, 0f, radius, segs[k].End, 0f, k == 0 ? (float)headEnd : 0f, 0f));
            }
            return result.ToArray();
        }

        /// <summary>The vertex's region from its dominant bone: the nearest mapped human bone at or above it.</summary>
        public static string Region(string? pointId) => pointId switch
        {
            null => "other",
            var id when id.Contains("hand") => "hand",
            var id when id.Contains("upper-arm") => "upperarm",
            var id when id.Contains("lower-arm") => "forearm",
            var id when id.Contains("upper-leg") => "thigh",
            var id when id.Contains("lower-leg") => "shin",
            var id when id.Contains("foot") || id.Contains("toes") => "foot",
            "bone.head" or "bone.neck" => "head",
            _ => "torso",
        };
}
