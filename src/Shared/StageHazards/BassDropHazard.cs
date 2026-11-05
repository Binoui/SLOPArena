using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopArena.Shared.StageHazards
{
    internal sealed class BassDropHazard : IStageHazardFamily
    {
        internal static readonly string[] PatternNames = { "checkerboard", "north_south_stripes", "east_west_stripes", "west_half", "east_half", "middle_rows" };
        private readonly HazardAnchor.Cells _pads;
        public string AmbientId => "bass_drop";
        public ArcadePoint Route(uint seed, uint absoluteTick) => _pads.Points[12];
        internal BassDropHazard(StageHazardAnchors anchors)
        {
            if (!anchors.Entries.TryGetValue("dance_pads", out var a) || !(a is HazardAnchor.Cells pads)
                || pads.Points.Count != 20 || pads.Radius != 1)
                throw new ArgumentException("Bass Drop requires dance_pads: 20 row-major 2 m Cells.");
            _pads = pads; var origin = pads.Points[0];
            for (int i = 0; i < 20; i++)
            {
                var p = pads.Points[i];
                if (p.X != origin.X + (i % 5) * 2 || p.Z != origin.Z + (i / 5) * 2 || p.Y != origin.Y)
                    throw new ArgumentException("Bass Drop pads must be a flat row-major 5 by 4 grid at 2 m spacing.");
            }
        }
        private static uint Hash(uint seed, uint ordinal)
        {
            unchecked { uint v = seed ^ (ordinal * 0x9e3779b9u) ^ 0x42415353u;
                v ^= v >> 16; v *= 0x7feb352du; v ^= v >> 15; v *= 0x846ca68bu; return v ^ (v >> 16); }
        }
        public uint NextLock(uint seed, uint ordinal, uint notBeforeTick) => checked(notBeforeTick + 180 + Hash(seed, ordinal) % 301);
        public HazardAction Plan(uint seed, uint ordinal, uint lockTick)
        {
            int pattern = (int)(Hash(seed, ordinal) % (uint)PatternNames.Length);
            bool Lit(int i)
            {
                int col = i % 5, row = i / 5;
                switch (pattern)
                {
                    case 0: return (col + row) % 2 == 0;
                    case 1: return col % 2 == 0;
                    case 2: return row % 2 == 0;
                    case 3: return col < 2;
                    case 4: return col >= 3;
                    default: return row == 1 || row == 2;
                }
            }
            uint warning = checked(lockTick + 90), impact = checked(warning + 120), end = checked(impact + 1);
            var window = new HazardWindow(lockTick, warning, impact, checked(end + 45));
            var indices = Enumerable.Range(0, 20).Where(Lit).ToArray();
            var attacks = new List<StageAttack>();
            // Thin parallel capsules tessellate each pad. Every capsule stays within its
            // pad footprint and the common hit set makes all pads one drop.
            foreach (int index in indices)
            {
                var p = _pads.Points[index]; const float radius = .25f;
                for (int strip = 0; strip < 7; strip++)
                {
                    float z = p.Z - .75f + strip * .25f;
                    var h = new Hitbox { Shape = HitboxShape.Capsule,
                        X = p.X - .75f, EndX = p.X + .75f, Y = p.Y + .25f, EndY = p.Y + .25f, Z = z, EndZ = z,
                        Radius = radius, DurationTicks = 1, Damage = 14, BaseKnockback = 18, KnockbackGrowth = .8f,
                        KnockbackAngle = 90, HitsMultipleOpponents = true, OwnerId = StageHazardOwner.EntityId };
                    attacks.Add(new StageAttack((ushort)attacks.Count, impact, end, h));
                }
            }
            return new HazardAction(ordinal, window, attacks.ToArray(), "bass_drop.drop", PatternNames[pattern],
                new HazardCellSelection("dance_pads", _pads, indices));
        }
        public void WriteAction(SnapshotWriter w, HazardAction a)
        {
            w.WriteByte(1); w.WriteUInt(a.Ordinal); w.WriteUInt(a.Window.LockTick);
            w.WriteUInt(a.Window.WarningTick); w.WriteUInt(a.Window.ImpactTick); w.WriteUInt(a.Window.RetireTick);
            w.WriteString(a.Variant); w.WriteInt(a.Attacks.Count);
            foreach (var attack in a.Attacks)
            {
                w.WriteUShort(attack.Operation); w.WriteUInt(attack.SpawnTick); w.WriteUInt(attack.EndTick);
                SnapshotCodecs.Write(w, attack.Definition, _ => (null, null));
            }
        }
        public HazardAction ReadAction(SnapshotReader r, uint seed)
        {
            if (r.ReadByte() != 1) throw SnapshotReader.Malformed("bass drop action tag");
            uint ordinal = r.ReadUInt(), locked = r.ReadUInt();
            try
            {
                var expected = Plan(seed, ordinal, locked);
                var window = new HazardWindow(locked, r.ReadUInt(), r.ReadUInt(), r.ReadUInt());
                string variant = r.ReadString(); int count = r.ReadCount(1);
                if (count != expected.Attacks.Count) throw SnapshotReader.Malformed("bass drop sections");
                var attacks = new StageAttack[count];
                for (int i = 0; i < count; i++) attacks[i] = new StageAttack(r.ReadUShort(), r.ReadUInt(), r.ReadUInt(), SnapshotCodecs.ReadHitbox(r, _ => (null, null)));
                var frozen = new HazardAction(ordinal, window, attacks, expected.PresentationId, variant, expected.Cells);
                var a = new SnapshotWriter(); var b = new SnapshotWriter(); WriteAction(a, expected); WriteAction(b, frozen);
                if (!a.ToArray().SequenceEqual(b.ToArray())) throw SnapshotReader.Malformed("bass drop frozen action");
                return frozen;
            }
            catch (ArgumentException) { throw SnapshotReader.Malformed("bass drop action"); }
            catch (OverflowException) { throw SnapshotReader.Malformed("bass drop tick overflow"); }
        }
    }
}
