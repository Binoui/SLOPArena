using System;
using System.Linq;

namespace SlopArena.Shared.StageHazards
{
    internal enum KrakenSide : byte { Port, Starboard }
    internal sealed class KrakenAction : HazardAction
    {
        internal KrakenSide Side { get; }
        internal ArcadePoint LockedPose { get; }
        internal KrakenAction(uint ordinal, HazardWindow window, KrakenSide side, ArcadePoint pose, StageAttack[] attacks, string presentationId, string variant)
            : base(ordinal, window, attacks, presentationId, variant) { Side = side; LockedPose = pose; }
    }
    internal sealed class KrakenHazard : IStageHazardFamily
    {
        private readonly HazardAnchor.Box[] _lanes;
        private static readonly string[] LaneIds = { "aft", "mid", "fore" };
        public string AmbientId => "kraken";
        private readonly HazardAnchor.Loop _loop;
        internal KrakenHazard(StageHazardAnchors anchors)
        {
            T Require<T>(string name) where T : HazardAnchor => anchors.Entries.TryGetValue(name, out var a) && a is T typed
                ? typed : throw new ArgumentException($"Kraken requires {name} of kind {typeof(T).Name}.");
            _lanes = LaneIds.Select(id => Require<HazardAnchor.Box>("slam_" + id)).ToArray();
            if (_lanes.Any(lane => lane.Yaw != 0 || lane.HalfExtents.X < lane.HalfExtents.Y
                || lane.HalfExtents.Z < lane.HalfExtents.Y)) throw new ArgumentException("Kraken lanes must be axis aligned and wider than their height.");
            _loop = Require<HazardAnchor.Loop>("water_loop");
            Require<HazardAnchor.Box>("bow_wave"); Require<HazardAnchor.Box>("hatch");
        }
        // Provisional tuning, pending playtest selection.
        private static class Tuning
        {
            internal const uint LockLeadTicks = 90, WarningTicks = 120, RecoveryTicks = 45;
            internal const uint MinimumRestTicks = 180, RestJitterTicks = 301, RouteLegTicks = 180;
            internal const ushort ActiveTicks = 8;
            internal const float SlamDamage = 14, SlamLaunch = 18, SlamGrowth = 0.8f;
            internal const sbyte SlamAngle = 55;
        }
        private static uint Hash(uint seed, uint ordinal)
        {
            unchecked
            {
                uint v = seed ^ (ordinal * 0x9e3779b9u) ^ 0x4b52414bu;
                v ^= v >> 16; v *= 0x7feb352du; v ^= v >> 15; v *= 0x846ca68bu; return v ^ (v >> 16);
            }
        }
        public uint NextLock(uint seed, uint ordinal, uint notBeforeTick) =>
            checked(notBeforeTick + Tuning.MinimumRestTicks + Hash(seed, ordinal) % Tuning.RestJitterTicks);
        public ArcadePoint Route(uint seed, uint absoluteTick)
        {
            ulong phase = (ulong)absoluteTick + Hash(seed, 0x525445u);
            int leg = (int)((phase / Tuning.RouteLegTicks) % (ulong)_loop.Points.Count);
            var a = _loop.Points[leg]; var b = _loop.Points[(leg + 1) % _loop.Points.Count];
            float t = (phase % Tuning.RouteLegTicks) / (float)Tuning.RouteLegTicks;
            return new ArcadePoint(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
        }
        HazardAction IStageHazardFamily.Plan(uint seed, uint ordinal, uint lockTick) => Plan(seed, ordinal, lockTick);
        public KrakenAction Plan(uint seed, uint ordinal, uint lockTick)
        {
            var pose = Route(seed, lockTick);
            var side = pose.Z >= 0 ? KrakenSide.Port : KrakenSide.Starboard;
            int index = 0;
            for (int i = 1; i < _lanes.Length; i++)
                if (MathF.Abs(_lanes[i].Center.X - pose.X) < MathF.Abs(_lanes[index].Center.X - pose.X)) index = i;
            var lane = _lanes[index];
            uint warning = checked(lockTick + Tuning.LockLeadTicks), impact = checked(warning + Tuning.WarningTicks);
            var window = new HazardWindow(lockTick, warning, impact, checked(impact + Tuning.ActiveTicks + Tuning.RecoveryTicks));
            // Parallel round sections form the arm, bounded by the baked lane box.
            // Their shared hit set makes the entire arm one attack throughout its lifetime.
            float radius = lane.HalfExtents.Y;
            float halfSpan = lane.HalfExtents.X - radius;
            int sections = Math.Max(2, (int)MathF.Ceiling(halfSpan * 2 / .25f) + 1);
            var attacks = new StageAttack[sections];
            for (int i = 0; i < sections; i++)
            {
                float x = lane.Center.X - halfSpan + 2 * halfSpan * i / (sections - 1);
                var hb = new Hitbox
                {
                    Shape = HitboxShape.Capsule, X = x, Y = lane.Center.Y,
                    Z = lane.Center.Z + (side == KrakenSide.Port ? 1 : -1) * (lane.HalfExtents.Z - radius),
                    EndX = x, EndY = lane.Center.Y, EndZ = lane.Center.Z - (side == KrakenSide.Port ? 1 : -1) * (lane.HalfExtents.Z - radius), Radius = radius,
                    DurationTicks = Tuning.ActiveTicks, Damage = Tuning.SlamDamage, BaseKnockback = Tuning.SlamLaunch,
                    KnockbackGrowth = Tuning.SlamGrowth, KnockbackAngle = Tuning.SlamAngle, HitsMultipleOpponents = true,
                    OwnerId = StageHazardOwner.EntityId
                };
                attacks[i] = new StageAttack((ushort)i, impact, checked(impact + Tuning.ActiveTicks), hb);
            }
            return new KrakenAction(ordinal, window, side, pose, attacks, "kraken.slam",
                (side == KrakenSide.Port ? "port." : "starboard.") + LaneIds[index]);
        }
        public void WriteAction(SnapshotWriter w, HazardAction action)
        {
            var a = (KrakenAction)action;
            w.WriteByte(1); w.WriteUInt(a.Ordinal);
            w.WriteUInt(a.Window.LockTick); w.WriteUInt(a.Window.WarningTick); w.WriteUInt(a.Window.ImpactTick); w.WriteUInt(a.Window.RetireTick);
            w.WriteByte((byte)a.Side); w.WriteFloat(a.LockedPose.X); w.WriteFloat(a.LockedPose.Y); w.WriteFloat(a.LockedPose.Z);
            w.WriteInt(a.Attacks.Count);
            foreach (var attack in a.Attacks)
            {
                w.WriteUShort(attack.Operation); w.WriteUInt(attack.SpawnTick); w.WriteUInt(attack.EndTick);
                SnapshotCodecs.Write(w, attack.Definition, _ => (null, null));
            }
        }
        public HazardAction ReadAction(SnapshotReader r, uint seed)
        {
            if (r.ReadByte() != 1) throw SnapshotReader.Malformed("kraken action tag");
            uint ordinal = r.ReadUInt(), locked = r.ReadUInt();
            try
            {
                var expected = Plan(seed, ordinal, locked);
                var window = new HazardWindow(locked, r.ReadUInt(), r.ReadUInt(), r.ReadUInt());
                var side = (KrakenSide)r.ReadByte(); var pose = new ArcadePoint(r.ReadFloat(), r.ReadFloat(), r.ReadFloat());
                int count = r.ReadCount(1);
                if (count != expected.Attacks.Count) throw SnapshotReader.Malformed("kraken sections");
                var attacks = new StageAttack[count];
                for (int i = 0; i < count; i++)
                    attacks[i] = new StageAttack(r.ReadUShort(), r.ReadUInt(), r.ReadUInt(), SnapshotCodecs.ReadHitbox(r, _ => (null, null)));
                var frozen = new KrakenAction(ordinal, window, side, pose, attacks, expected.PresentationId, expected.Variant);
                var a = new SnapshotWriter(); var b = new SnapshotWriter(); WriteAction(a, expected); WriteAction(b, frozen);
                if (!a.ToArray().SequenceEqual(b.ToArray())) throw SnapshotReader.Malformed("kraken frozen action");
                return frozen;
            }
            catch (ArgumentException) { throw SnapshotReader.Malformed("kraken action"); }
            catch (OverflowException) { throw SnapshotReader.Malformed("kraken tick overflow"); }
        }
    }
}
