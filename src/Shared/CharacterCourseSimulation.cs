using System;
using System.Collections.Generic;

namespace SlopArena.Shared
{
    public enum CharacterCoursePhase : byte { Ready, Countdown, Running, Completed, Stopped }
    /// <summary>TargetHit is a bonus target taking a hit that does not break it yet.</summary>
    public enum CharacterCourseEventKind : byte { Go, TargetBroken, CheckpointSaved, Respawned, Completed, TargetHit }
    public readonly struct CharacterCourseEvent
    {
        public readonly CharacterCourseEventKind Kind;
        public readonly int Index;
        public readonly ulong ElapsedTicks;
        public CharacterCourseEvent(CharacterCourseEventKind kind, int index, ulong ticks)
        { Kind = kind; Index = index; ElapsedTicks = ticks; }
    }
    public readonly struct CharacterCourseState
    {
        public readonly CharacterCoursePhase Phase;
        public readonly ulong ElapsedTicks;
        public readonly int BrokenTargets, TotalTargets, SavedCheckpoint;
        public readonly ushort CountdownTicks;
        public bool Ended => Phase is CharacterCoursePhase.Completed or CharacterCoursePhase.Stopped;
        public CharacterCourseState(CharacterCoursePhase phase, ulong elapsedTicks, int brokenTargets,
            int totalTargets, int savedCheckpoint, ushort countdownTicks)
        { Phase = phase; ElapsedTicks = elapsedTicks; BrokenTargets = brokenTargets;
            TotalTargets = totalTargets; SavedCheckpoint = savedCheckpoint; CountdownTicks = countdownTicks; }
    }

    internal sealed class CharacterCourseSimulation
    {
        public const ulong FirstEntityId = 1UL << 40;
        public const byte FirstInputAlias = 128;
        private readonly CharacterCourseDefinition _definition;
        private readonly ulong _entityBase;
        private readonly bool[] _broken;
        private readonly byte[] _hitsLeft;
        private readonly List<CharacterCourseEvent> _events = new();
        private CharacterCoursePhase _phase;
        private ulong _elapsed;
        private int _brokenCount, _checkpoint = -1;
        private ushort _countdown;
        public IReadOnlyList<CharacterCourseEvent> Events { get; }
        public CharacterCourseState State => new(_phase, _elapsed, _brokenCount, _broken.Length, _checkpoint, _countdown);
        public CharacterCourseSimulation(CharacterCourseDefinition definition, ulong targetOwner = 0)
        {
            _entityBase = FirstEntityId + targetOwner * 128;
            _definition = definition; _broken = new bool[definition.Targets.Count]; Events = _events.AsReadOnly();
            _hitsLeft = new byte[_broken.Length];
            for (int i = 0; i < _hitsLeft.Length; i++) _hitsLeft[i] = definition.Targets[i].Hits;
        }

        internal void BeginRun()
        {
            if (_phase != CharacterCoursePhase.Ready) throw new InvalidOperationException("Begin a clean course.");
            _phase = CharacterCoursePhase.Running;
        }

        public void Begin(ushort countdown)
        {
            if (_phase != CharacterCoursePhase.Ready || countdown == 0 || countdown > 600)
                throw new InvalidOperationException("Begin a clean course with 1–600 countdown ticks.");
            _countdown = countdown; _phase = CharacterCoursePhase.Countdown;
        }
        public bool BeginTick()
        {
            _events.Clear();
            if (_phase == CharacterCoursePhase.Countdown)
            {
                if (--_countdown == 0) { _phase = CharacterCoursePhase.Running; Emit(CharacterCourseEventKind.Go); }
                return false;
            }
            if (_phase != CharacterCoursePhase.Running) return false;
            if (_elapsed == ulong.MaxValue) throw new InvalidOperationException("Course clock exhausted.");
            _elapsed++; return true;
        }
        public void Stop() { if (_phase != CharacterCoursePhase.Completed) { _phase = CharacterCoursePhase.Stopped; _countdown = 0; } }
        internal void ClearEvents() => _events.Clear();
        public bool IsBroken(int index) => _broken[index];
        /// <summary>Hits this target still needs: its full count until hit, 0 once broken.</summary>
        public int HitsLeft(int index) => _hitsLeft[index];
        public bool TryIndex(ulong id, out int index)
        {
            index = id >= _entityBase && id - _entityBase < (ulong)_broken.Length ? (int)(id - _entityBase) : -1;
            return index >= 0;
        }
        public ulong ResolveAlias(byte alias)
        {
            int index = alias - FirstInputAlias;
            return index >= 0 && index < _broken.Length && !_broken[index] ? _entityBase + (ulong)index : 0;
        }
        internal ulong EntityId(int index) => _entityBase + (ulong)index;
        /// <summary>
        /// One accepted damaging hit: false while not running or already broken. The last hit a
        /// target needs breaks it (TargetBroken); a bonus target's earlier hits emit TargetHit.
        /// </summary>
        public bool Hit(int index)
        {
            if (_phase != CharacterCoursePhase.Running || _broken[index]) return false;
            if (--_hitsLeft[index] > 0) { Emit(CharacterCourseEventKind.TargetHit, index); return true; }
            _broken[index] = true; _brokenCount++; Emit(CharacterCourseEventKind.TargetBroken, index); return true;
        }
        public IEnumerable<SpellResolver.EntityData> CollisionTargets()
        {
            if (_phase != CharacterCoursePhase.Running) yield break;
            for (int i = 0; i < _broken.Length; i++)
            {
                if (_broken[i]) continue;
                var target = _definition.Targets[i];
                yield return new SpellResolver.EntityData { Id = _entityBase + (ulong)i,
                    PosX = target.Position.X, PosY = target.Position.Y, PosZ = target.Position.Z,
                    EndX = target.Position.X, EndY = target.Position.Y, EndZ = target.Position.Z,
                    Radius = target.Radius, Shape = HitboxShape.Sphere, Active = true };
            }
        }
        internal void WriteSnapshot(SnapshotWriter w)
        {
            w.WriteByte((byte)_phase); w.WriteULong(_elapsed); w.WriteInt(_checkpoint);
            w.WriteUShort(_countdown); w.WriteInt(_hitsLeft.Length);
            foreach (byte hits in _hitsLeft) w.WriteByte(hits);
        }
        internal Snapshot ReadSnapshot(SnapshotReader r)
        {
            var phase = (CharacterCoursePhase)r.ReadByte();
            ulong elapsed = r.ReadULong(); int checkpoint = r.ReadInt(); ushort countdown = r.ReadUShort();
            int count = r.ReadCount(1);
            if (phase > CharacterCoursePhase.Stopped || checkpoint < -1 || checkpoint >= _definition.Checkpoints.Count
                || count != _hitsLeft.Length || countdown > 600
                || (phase == CharacterCoursePhase.Countdown) != (countdown > 0))
                throw SnapshotReader.Malformed("course progress");
            var hits = new byte[count];
            for (int i = 0; i < count; i++)
            {
                hits[i] = r.ReadByte();
                if (hits[i] > _definition.Targets[i].Hits) throw SnapshotReader.Malformed("course target hits");
            }
            if (phase == CharacterCoursePhase.Completed && Array.Exists(hits, x => x != 0))
                throw SnapshotReader.Malformed("unfinished course completion");
            return new Snapshot(phase, elapsed, checkpoint, countdown, hits);
        }
        internal void Apply(Snapshot snapshot)
        {
            _phase = snapshot.Phase; _elapsed = snapshot.Elapsed; _checkpoint = snapshot.Checkpoint;
            _countdown = snapshot.Countdown; _brokenCount = 0; _events.Clear();
            for (int i = 0; i < _hitsLeft.Length; i++)
            {
                _hitsLeft[i] = snapshot.Hits[i]; _broken[i] = _hitsLeft[i] == 0;
                if (_broken[i]) _brokenCount++;
            }
        }
        internal readonly record struct Snapshot(CharacterCoursePhase Phase, ulong Elapsed, int Checkpoint,
            ushort Countdown, byte[] Hits);
        public void FinishTick(ArcadePoint previous, ArcadePoint current, bool respawned)
        {
            if (respawned) { Emit(CharacterCourseEventKind.Respawned, _checkpoint); return; }
            for (int i = _checkpoint + 1; i < _definition.Checkpoints.Count; i++)
            {
                var checkpoint = _definition.Checkpoints[i];
                bool eligible = true;
                for (int j = 0; j < checkpoint.RequiredTargetCount; j++) eligible &= _broken[j];
                if (!eligible || !checkpoint.Volume.Crosses(previous, current)) continue;
                _checkpoint = i; Emit(CharacterCourseEventKind.CheckpointSaved, i);
            }
            if (_brokenCount == _broken.Length && _definition.Finish.Crosses(previous, current))
            { _phase = CharacterCoursePhase.Completed; Emit(CharacterCourseEventKind.Completed); }
        }
        private void Emit(CharacterCourseEventKind kind, int index = -1) => _events.Add(new(kind, index, _elapsed));
    }
}
