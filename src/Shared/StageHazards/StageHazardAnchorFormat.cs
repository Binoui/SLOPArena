using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SlopArena.Shared.StageHazards
{
    internal static class StageHazardAnchorFormat
    {
        internal const int MaxBytes = 65536;
        internal static byte[] Serialize(StageHazardAnchors anchors)
        {
            var w = new SnapshotWriter(); w.WriteUShort(1); w.WriteInt(anchors.Entries.Count);
            void Point(ArcadePoint p) { w.WriteFloat(p.X); w.WriteFloat(p.Y); w.WriteFloat(p.Z); }
            void Points(IReadOnlyList<ArcadePoint> points) { w.WriteInt(points.Count); foreach (var p in points) Point(p); }
            foreach (var e in anchors.Entries)
            {
                w.WriteString(e.Key);
                switch (e.Value)
                {
                    case HazardAnchor.Point a: w.WriteByte(0); Point(a.Position); break;
                    case HazardAnchor.Origin a: w.WriteByte(1); Point(a.Position); break;
                    case HazardAnchor.Capsule a: w.WriteByte(2); Point(a.A); Point(a.B); w.WriteFloat(a.Radius); break;
                    case HazardAnchor.Box a: w.WriteByte(3); Point(a.Center); Point(a.HalfExtents); w.WriteFloat(a.Yaw); break;
                    case HazardAnchor.Cells a: w.WriteByte(4); Points(a.Points); w.WriteFloat(a.Radius); break;
                    case HazardAnchor.Loop a: w.WriteByte(5); Points(a.Points); break;
                }
            }
            if (w.Length > MaxBytes) throw new ArgumentException("Hazard section too large.");
            return w.ToArray();
        }
        internal static StageHazardAnchors? Deserialize(byte[] bytes, in ArenaDefinition arena)
        {
            try
            {
                if (bytes.Length > MaxBytes) return null;
                var r = new SnapshotReader(bytes);
                if (r.ReadUShort() != 1) return null;
                int n = r.ReadCount(5); if (n > StageHazardAnchors.MaxAnchors) return null;
                ArcadePoint Point() => new ArcadePoint(r.ReadFloat(), r.ReadFloat(), r.ReadFloat());
                ArcadePoint[] Points()
                {
                    int count = r.ReadCount(12); if (count > StageHazardAnchors.MaxPoints) throw new InvalidDataException();
                    var points = new ArcadePoint[count]; for (int i = 0; i < count; i++) points[i] = Point(); return points;
                }
                var entries = new List<KeyValuePair<string, HazardAnchor>>();
                for (int i = 0; i < n; i++)
                {
                    string name = r.ReadString() ?? throw new InvalidDataException();
                    HazardAnchor a = r.ReadByte() switch
                    {
                        0 => new HazardAnchor.Point(Point(), arena), 1 => new HazardAnchor.Origin(Point()),
                        2 => new HazardAnchor.Capsule(Point(), Point(), r.ReadFloat(), arena),
                        3 => new HazardAnchor.Box(Point(), Point(), r.ReadFloat(), arena),
                        4 => new HazardAnchor.Cells(Points(), r.ReadFloat(), arena), 5 => new HazardAnchor.Loop(Points(), arena),
                        _ => throw new InvalidDataException()
                    };
                    entries.Add(new KeyValuePair<string, HazardAnchor>(name, a));
                }
                return r.AtEnd ? new StageHazardAnchors(entries, arena) : null;
            }
            catch (ArgumentException) { return null; }
            catch (InvalidDataException) { return null; }
        }
    }
    public static class StageHazardAuthoring
    {
        public static StageHazardAnchors Parse(string shellJson, in ArenaDefinition arena)
        {
            using var doc = JsonDocument.Parse(shellJson);
            var block = doc.RootElement.GetProperty("hazardAnchors");
            if (block.GetArrayLength() > StageHazardAnchors.MaxAnchors) throw new ArgumentException("Too many anchors.");
            ArcadePoint Point(JsonElement e)
            {
                if (e.GetArrayLength() != 3) throw new ArgumentException("Expected three coordinates.");
                return new ArcadePoint(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle());
            }
            ArcadePoint[] Points(JsonElement e)
            {
                if (e.GetArrayLength() > StageHazardAnchors.MaxPoints) throw new ArgumentException("Too many points.");
                return e.EnumerateArray().Select(Point).ToArray();
            }
            var entries = new List<KeyValuePair<string, HazardAnchor>>();
            foreach (var e in block.EnumerateArray())
            {
                HazardAnchor a = e.GetProperty("kind").GetString() switch
                {
                    "Point" => new HazardAnchor.Point(Point(e.GetProperty("position")), arena),
                    "Origin" => new HazardAnchor.Origin(Point(e.GetProperty("position"))),
                    "Capsule" => new HazardAnchor.Capsule(Point(e.GetProperty("a")), Point(e.GetProperty("b")), e.GetProperty("radius").GetSingle(), arena),
                    "Box" => new HazardAnchor.Box(Point(e.GetProperty("center")), Point(e.GetProperty("halfExtents")), e.GetProperty("yaw").GetSingle(), arena),
                    "Cells" => new HazardAnchor.Cells(Points(e.GetProperty("points")), e.GetProperty("radius").GetSingle(), arena),
                    "Loop" => new HazardAnchor.Loop(Points(e.GetProperty("points")), arena),
                    _ => throw new ArgumentException("Unknown anchor kind.")
                };
                entries.Add(new KeyValuePair<string, HazardAnchor>(e.GetProperty("name").GetString()!, a));
            }
            return new StageHazardAnchors(entries, arena);
        }
    }
}
