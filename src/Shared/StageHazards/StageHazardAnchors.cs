using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SlopArena.Shared.StageHazards
{
    public abstract class HazardAnchor
    {
        private HazardAnchor() { }
        public sealed class Point : HazardAnchor
        {
            public ArcadePoint Position { get; }
            public Point(ArcadePoint position, in ArenaDefinition arena) { Position = position; Validate(arena); }
        }
        public sealed class Origin : HazardAnchor
        {
            public ArcadePoint Position { get; }
            public Origin(ArcadePoint position) { Position = position; Finite(position); }
        }
        public sealed class Capsule : HazardAnchor
        {
            public ArcadePoint A { get; }
            public ArcadePoint B { get; }
            public float Radius { get; }
            public Capsule(ArcadePoint a, ArcadePoint b, float radius, in ArenaDefinition arena)
            { A = a; B = b; Radius = radius; Validate(arena); }
        }
        public sealed class Box : HazardAnchor
        {
            public ArcadePoint Center { get; }
            public ArcadePoint HalfExtents { get; }
            public float Yaw { get; }
            public Box(ArcadePoint center, ArcadePoint halfExtents, float yaw, in ArenaDefinition arena)
            { Center = center; HalfExtents = halfExtents; Yaw = yaw; Validate(arena); }
        }
        public sealed class Cells : HazardAnchor
        {
            public IReadOnlyList<ArcadePoint> Points { get; }
            public float Radius { get; }
            public Cells(IEnumerable<ArcadePoint> points, float radius, in ArenaDefinition arena)
            { Points = Copy(points, 1); Radius = radius; Validate(arena); }
        }
        public sealed class Loop : HazardAnchor
        {
            public IReadOnlyList<ArcadePoint> Points { get; }
            public Loop(IEnumerable<ArcadePoint> points, in ArenaDefinition arena)
            { Points = Copy(points, 3); Validate(arena); }
        }
        private static IReadOnlyList<ArcadePoint> Copy(IEnumerable<ArcadePoint> points, int minimum)
        {
            var copy = points.Take(StageHazardAnchors.MaxPoints + 1).ToArray();
            if (copy.Length < minimum || copy.Length > StageHazardAnchors.MaxPoints) throw new ArgumentException("Invalid anchor point count.");
            return Array.AsReadOnly(copy);
        }
        internal static void Finite(float value)
        { if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentException("Anchor values must be finite."); }
        private static void Finite(ArcadePoint p) { Finite(p.X); Finite(p.Y); Finite(p.Z); }
        internal void Validate(in ArenaDefinition arena)
        {
            var lines = ArenaCollision.ResolveBlastLines(arena);
            void Inside(ArcadePoint p, float rx = 0, float ry = 0, float rz = 0)
            {
                Finite(p); Finite(rx); Finite(ry); Finite(rz);
                if (!(p.X - rx > lines.KillMinX && p.X + rx < lines.KillMaxX
                    && p.Z - rz > lines.KillMinZ && p.Z + rz < lines.KillMaxZ
                    && p.Y - ry > lines.KillHeight && p.Y + ry < lines.KillTop))
                    throw new ArgumentException("Anchor leaves the blast corridor.");
            }
            void Radius(float r) { Finite(r); if (r <= 0) throw new ArgumentException("Anchor radius must be positive."); }
            switch (this)
            {
                case Origin o: Finite(o.Position); break;
                case Point p: Inside(p.Position); break;
                case Capsule c: Radius(c.Radius); Inside(c.A, c.Radius, c.Radius, c.Radius); Inside(c.B, c.Radius, c.Radius, c.Radius); break;
                case Cells c: Radius(c.Radius); foreach (var p in c.Points) Inside(p, c.Radius, c.Radius, c.Radius); break;
                case Loop l: foreach (var p in l.Points) Inside(p); break;
                case Box b:
                    Finite(b.Center); Finite(b.HalfExtents); Finite(b.Yaw);
                    if (b.HalfExtents.X <= 0 || b.HalfExtents.Y <= 0 || b.HalfExtents.Z <= 0) throw new ArgumentException("Box extents must be positive.");
                    float cosine = MathF.Abs(MathF.Cos(b.Yaw)), s = MathF.Abs(MathF.Sin(b.Yaw));
                    Inside(b.Center, cosine * b.HalfExtents.X + s * b.HalfExtents.Z, b.HalfExtents.Y, s * b.HalfExtents.X + cosine * b.HalfExtents.Z); break;
            }
        }
    }

    public sealed class StageHazardAnchors
    {
        public const int MaxAnchors = 64, MaxNameLength = 64, MaxPoints = 128;
        public IReadOnlyDictionary<string, HazardAnchor> Entries { get; }
        public StageHazardAnchors(IEnumerable<KeyValuePair<string, HazardAnchor>> entries, ArenaDefinition arena)
        {
            var copy = new SortedDictionary<string, HazardAnchor>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (copy.Count >= MaxAnchors || string.IsNullOrWhiteSpace(entry.Key) || entry.Key.Length > MaxNameLength
                    || entry.Key.Any(c => !(c <= 127 && (char.IsLetterOrDigit(c) || c == '_' || c == '-')))
                    || entry.Value == null || copy.ContainsKey(entry.Key)) throw new ArgumentException("Invalid or duplicate anchor name/count.");
                entry.Value.Validate(arena); copy.Add(entry.Key, entry.Value);
            }
            if (copy.Count == 0) throw new ArgumentException("Anchor section must not be empty.");
            Entries = new ReadOnlyDictionary<string, HazardAnchor>(copy);
            ValidateArena(arena);
        }
        public void ValidateArena(in ArenaDefinition arena)
        {
            if (arena.Arcade != null) throw new ArgumentException("Arcade stages cannot carry hazard anchors.");
            foreach (var anchor in Entries.Values) anchor.Validate(arena);
        }
    }
}
