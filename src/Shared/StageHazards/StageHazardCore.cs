using SlopArena.Shared.Rollback;
using System;
using System.Collections.Generic;
using System.IO;

namespace SlopArena.Shared.StageHazards
{
    public static class StageHazardOwner
    {
        public const ulong EntityId = (1UL << 48) + 1;
    }
    public sealed class HazardWindow
    {
        public uint LockTick { get; }
        public uint WarningTick { get; }
        public uint ImpactTick { get; }
        public uint RetireTick { get; }
        public HazardWindow(uint lockTick, uint warningTick, uint impactTick, uint retireTick)
        {
            if (!(lockTick < warningTick && warningTick < impactTick && impactTick <= retireTick)
                || warningTick - lockTick <= NetplayPredictor.MaxPredictionTicks
                || impactTick - warningTick <= NetplayPredictor.MaxPredictionTicks)
                throw new ArgumentException("Hazard window violates the prediction window.");
            LockTick = lockTick; WarningTick = warningTick; ImpactTick = impactTick; RetireTick = retireTick;
        }
    }
    internal sealed class StageAttack
    {
        internal ushort Operation { get; }
        internal uint SpawnTick { get; }
        internal uint EndTick { get; }
        internal Hitbox Definition { get; }
        internal StageAttack(ushort operation, uint spawnTick, uint endTick, Hitbox definition)
        {
            if (endTick <= spawnTick || endTick - spawnTick != definition.DurationTicks) throw new ArgumentException("Invalid attack lifetime.");
            Operation = operation; SpawnTick = spawnTick; EndTick = endTick; Definition = definition;
        }
    }
    public sealed class HazardCellSelection
    {
        public string AnchorName { get; }
        public HazardAnchor.Cells Anchor { get; }
        public IReadOnlyList<int> Indices { get; }
        internal HazardCellSelection(string anchorName, HazardAnchor.Cells anchor, int[] indices)
        { AnchorName = anchorName; Anchor = anchor; Indices = Array.AsReadOnly((int[])indices.Clone()); }
    }
    public sealed class HazardCue
    {
        public uint Ordinal { get; }
        public ushort Operation { get; }
        public string PresentationId { get; }
        public string Variant { get; }
        public Hitbox Geometry { get; }
        public uint WarningTick { get; }
        public HazardCellSelection? Cells { get; }
        public uint ImpactTick { get; }
        public uint EndTick { get; }
        internal HazardCue(HazardAction action, StageAttack attack)
        { Ordinal = action.Ordinal; Operation = attack.Operation; Geometry = attack.Definition; ImpactTick = attack.SpawnTick;
          WarningTick = action.Window.WarningTick; Cells = action.Cells;
          EndTick = attack.EndTick; PresentationId = action.PresentationId; Variant = action.Variant; }
    }
    public sealed class HazardAmbientPose
    {
        public string Id { get; }
        public ArcadePoint Position { get; }
        internal HazardAmbientPose(string id, ArcadePoint position) { Id = id; Position = position; }
    }
    public sealed class StageHazardView
    {
        public uint Tick { get; }
        public HazardAmbientPose AmbientPose { get; }
        public IReadOnlyList<HazardCue> Cues { get; }
        internal StageHazardView(uint tick, HazardAmbientPose ambient, HazardAction? action)
        {
            Tick = tick; AmbientPose = ambient;
            var cues = new List<HazardCue>();
            if (action != null && tick >= action.Window.WarningTick && tick < action.Window.RetireTick)
                foreach (var attack in action.Attacks) cues.Add(new HazardCue(action, attack));
            Cues = cues.AsReadOnly();
        }
    }
    internal class HazardAction
    {
        internal uint Ordinal { get; }
        internal HazardWindow Window { get; }
        internal HazardCellSelection? Cells { get; }
        internal IReadOnlyList<StageAttack> Attacks { get; }
        internal StageAttack Attack => Attacks[0];
        internal string PresentationId { get; }
        internal string Variant { get; }
        internal HazardAction(uint ordinal, HazardWindow window, StageAttack[] attacks, string presentationId, string variant, HazardCellSelection? cells = null)
        { Ordinal = ordinal; Window = window; Cells = cells; Attacks = Array.AsReadOnly((StageAttack[])attacks.Clone()); PresentationId = presentationId; Variant = variant; }
    }
    internal interface IStageHazardFamily
    {
        uint NextLock(uint seed, uint ordinal, uint notBeforeTick);
        HazardAction Plan(uint seed, uint ordinal, uint lockTick);
        ArcadePoint Route(uint seed, uint absoluteTick);
        string AmbientId { get; }
        void WriteAction(SnapshotWriter writer, HazardAction action);
        HazardAction ReadAction(SnapshotReader reader, uint seed);
    }
    public static class StageHazardFamilies
    {
        private static readonly IReadOnlyDictionary<string, Func<StageHazardAnchors, IStageHazardFamily>> Families =
            new Dictionary<string, Func<StageHazardAnchors, IStageHazardFamily>>(StringComparer.Ordinal)
            { ["splash_deck"] = anchors => new KrakenHazard(anchors), ["after_hours"] = anchors => new BassDropHazard(anchors) };
        public static bool IsAvailable(in ArenaDefinition arena)
        {
            try { return Bind(arena) != null; }
            catch (ArgumentException) { return false; }
        }
        internal static IStageHazardFamily? Bind(in ArenaDefinition arena)
        {
            if (!Families.TryGetValue(arena.Name, out var create)) return null;
            var anchors = arena.HazardAnchors ?? throw new ArgumentException("Hazard stage is missing anchors.");
            anchors.ValidateArena(arena); return create(anchors);
        }
    }
    internal sealed class StageHazardSession
    {
        private readonly IStageHazardFamily _family;
        private uint _seed, _originTick, _tick, _nextOrdinal, _nextLockTick, _scheduleFrom;
        private HazardAction? _active;
        internal StageHazardView View => new StageHazardView(_tick, new HazardAmbientPose(_family.AmbientId, _family.Route(_seed, _tick)), _active);
        internal StageHazardSession(IStageHazardFamily family, uint seed, uint originTick)
        {
            _family = family; _seed = seed; _originTick = _tick = originTick;
            _scheduleFrom = checked(originTick + 1);
            _nextLockTick = family.NextLock(seed, 0, _scheduleFrom);
        }
        internal void Tick(uint tick, SpellResolver resolver, bool allowActions = true)
        {
            if (tick != checked(_tick + 1)) throw new ArgumentException("Session must advance exactly one tick.");
            if (_active != null && tick >= _active.Window.RetireTick)
            { resolver.RemoveOwnerEffects(StageHazardOwner.EntityId); _active = null; }
            if (!allowActions)
            {
                resolver.RemoveOwnerEffects(StageHazardOwner.EntityId); _active = null;
                _tick = tick;
                // A decided match cannot open more actions, even while callers keep ticking it.
                _scheduleFrom = checked(tick + 1);
                _nextLockTick = _family.NextLock(_seed, _nextOrdinal, _scheduleFrom);
                return;
            }
            if (tick == _nextLockTick)
            {
                if (_active != null) throw new InvalidOperationException("Overlapping hazard actions.");
                var action = _family.Plan(_seed, _nextOrdinal, tick);
                uint ordinal = checked(_nextOrdinal + 1), from = checked(action.Window.RetireTick + 1);
                uint next = _family.NextLock(_seed, ordinal, from);
                _active = action; _nextOrdinal = ordinal; _scheduleFrom = from; _nextLockTick = next;
            }
            if (_active != null && tick == _active.Attack.SpawnTick)
            {
                var hits = new HashSet<ulong>();
                foreach (var attack in _active.Attacks)
                {
                    var hb = attack.Definition;
                    hb.OwnerId = StageHazardOwner.EntityId;
                    hb.ActivationId = ((ulong)_active.Ordinal + 1) << 16;
                    hb.VisualOperationIndex = attack.Operation;
                    hb.HitEntities = hits;
                    resolver.Spawn(hb);
                }
            }
            _tick = tick;
        }
        internal void WriteSnapshot(SnapshotWriter w)
        {
            w.WriteUShort(1); w.WriteUInt(_seed); w.WriteUInt(_originTick); w.WriteUInt(_tick);
            w.WriteUInt(_nextOrdinal); w.WriteUInt(_nextLockTick); w.WriteUInt(_scheduleFrom); w.WriteBool(_active != null);
            if (_active != null) _family.WriteAction(w, _active);
        }
        internal PreparedStageHazards PrepareSnapshot(SnapshotReader r, uint snapshotTick)
        {
            if (r.ReadUShort() != 1) throw SnapshotReader.Malformed("hazard version");
            uint seed = r.ReadUInt(), origin = r.ReadUInt(), tick = r.ReadUInt(), ordinal = r.ReadUInt(), next = r.ReadUInt(), from = r.ReadUInt();
            var active = r.ReadBool() ? _family.ReadAction(r, seed) : null;
            if (tick != snapshotTick || origin > tick || next <= tick || from <= origin
                || from > next
                || next != _family.NextLock(seed, ordinal, from)
                || ordinal == 0 && active != null
                || active == null && ordinal > 0 && from > (ulong)tick + 1
                || active != null && (active.Ordinal == uint.MaxValue || active.Ordinal + 1 != ordinal
                    || active.Window.LockTick <= origin || active.Window.LockTick > tick || active.Window.RetireTick <= tick
                    || from != (ulong)active.Window.RetireTick + 1))
                throw SnapshotReader.Malformed("hazard state");
            return new PreparedStageHazards(this, seed, origin, tick, ordinal, next, from, active);
        }
        internal void Apply(PreparedStageHazards state)
        {
            if (!ReferenceEquals(state.Owner, this)) throw new ArgumentException("Prepared hazard state belongs to another session.");
            _seed = state.Seed; _originTick = state.Origin; _tick = state.Tick;
            _nextOrdinal = state.Ordinal; _nextLockTick = state.Next; _scheduleFrom = state.From; _active = state.Active;
        }
    }
    internal sealed class PreparedStageHazards
    {
        internal StageHazardSession Owner { get; }
        internal uint Seed { get; }
        internal uint Origin { get; }
        internal uint Tick { get; }
        internal uint Ordinal { get; }
        internal uint Next { get; }
        internal uint From { get; }
        internal HazardAction? Active { get; }
        internal PreparedStageHazards(StageHazardSession owner, uint seed, uint origin, uint tick, uint ordinal, uint next, uint from, HazardAction? active)
        { Owner = owner; Seed = seed; Origin = origin; Tick = tick; Ordinal = ordinal; Next = next; From = from; Active = active; }
    }
}
