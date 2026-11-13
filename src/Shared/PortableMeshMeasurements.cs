using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace SlopArena.Shared;

public sealed record PortableMeshFit(float ModelYOffset, IReadOnlyList<HurtboxBoneSource> Hurtboxes);

/// <summary>Measures the drawn skin at idle frame zero in root-rule-1 coordinates.</summary>
public static class PortableMeshMeasurements
{
    public static PortableMeshFit Measure(byte[] glb, string? humanoidJson, string idleClip, float visualScale)
    {
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
                    double value = type == 5126 ? BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(glb.AsSpan(at, 4)))
                        : type == 5125 ? BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(at, 4)) : type == 5123 ? BinaryPrimitives.ReadUInt16LittleEndian(glb.AsSpan(at, 2)) : glb[at];
                    if (normalized && type != 5126) value /= type == 5121 ? 255.0 : 65535.0;
                    if (double.IsNaN(value) || double.IsInfinity(value)) throw new InvalidDataException("Mesh contains a non-finite value.");
                    values[i * width + k] = value;
                }
            return values;
        }
        Vector3 Model(double x, double y, double z)
        {
            rig.Root.Apply(x, y, z, visualScale, out double ox, out double oy, out double oz);
            return new Vector3((float)ox, (float)oy, (float)oz);
        }
        Vector3 BoneNode(int n) => Model(world[n * 12 + 3], world[n * 12 + 7], world[n * 12 + 11]);
        Vector3? Bone(string id)
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
        var points = new List<Vector3>(); var regions = new List<string>();
        foreach (int node in rig.SceneOrder)
        {
            var n = root.GetProperty("nodes")[node];
            if (!n.TryGetProperty("mesh", out var mesh) || !n.TryGetProperty("skin", out var skinIndex)) continue;
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
                    points.Add(Model(x / sum, y / sum, z / sum)); regions.Add(RegionOf(dominant));
                }
            }
        }
        if (points.Count == 0) throw new InvalidDataException("No skinned mesh vertices to measure.");
        var soles = points.Where((p, i) => regions[i] == "foot").Select(p => p.Y).ToArray();
        if (soles.Length == 0) throw new InvalidDataException("No foot vertices to measure the idle sole.");
        return new PortableMeshFit((float)Math.Round(-soles.Min(), 4), FitCapsules(points, regions, Bone));
    }
        internal static HurtboxBoneSource[] FitCapsules(IReadOnlyList<Vector3> points, IReadOnlyList<string> regions,
            Func<string, Vector3?> bone)
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
            float Pct(List<float> values, float f)
            {
                if (values.Count == 0) return 0f;
                values.Sort();
                return values[Math.Min(values.Count - 1, (int)(values.Count * f))];
            }
            var head = bone("bone.head")!.Value;
            var headUp = new List<float>();
            for (int i = 0; i < points.Count; i++)
                if (regions[i] == "head") headUp.Add(Math.Max(0f, points[i].Y - head.Y));
            float headEnd = (float)Math.Round(Pct(headUp, .95f) * .5f, 4);
            var starts = segs.Select(s => bone(s.Start)!.Value).ToArray();
            var ends = segs.Select((s, k) => bone(s.End)!.Value + (k == 0 ? new Vector3(0, headEnd, 0) : Vector3.Zero)).ToArray();
            float Dist(Vector3 p, Vector3 a, Vector3 b)
            {
                var d = b - a;
                float dd = Vector3.Dot(d, d);
                float t = dd >= 1e-10f ? Math.Min(1f, Math.Max(0f, Vector3.Dot(p - a, d) / dd)) : 0f;
                return (p - a - t * d).Length();
            }
            var result = new List<HurtboxBoneSource>();
            for (int k = 0; k < segs.Count; k++)
            {
                var kept = new List<float>();
                for (int i = 0; i < points.Count; i++)
                {
                    if (regions[i] != segs[k].Region) continue;
                    float mine = Dist(points[i], starts[k], ends[k]), best = mine;
                    for (int j = 0; j < segs.Count; j++)
                        if (segs[j].Region == segs[k].Region) best = Math.Min(best, Dist(points[i], starts[j], ends[j]));
                    if (mine <= best + 1e-5f) kept.Add(mine);
                }
                float radius = (float)Math.Round(Math.Max(.015f, Pct(kept, .85f)), 4);
                result.Add(new HurtboxBoneSource(segs[k].Start, 0f, 0f, 0f, radius, segs[k].End, 0f, k == 0 ? headEnd : 0f, 0f));
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
