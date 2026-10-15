using System;
using System.Collections.Generic;

namespace SlopArena.Shared
{
    public enum CharacterCoursePhase : byte { Ready, Countdown, Running, Completed, Stopped }
    public enum CharacterCourseEventKind : byte { Go, TargetBroken, CheckpointSaved, Respawned, Completed }
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
        private readonly bool[] _broken;
        private readonly List<CharacterCourseEvent> _events = new();
        private CharacterCoursePhase _phase;
        private ulong _elapsed;
        private int _brokenCount, _checkpoint = -1;
        private ushort _countdown;
        public IReadOnlyList<CharacterCourseEvent> Events { get; }
        public CharacterCourseState State => new(_phase, _elapsed, _brokenCount, _broken.Length, _checkpoint, _countdown);
        public CharacterCourseSimulation(CharacterCourseDefinition definition)
        { _definition = definition; _broken = new bool[definition.Targets.Count]; Events = _events.AsReadOnly(); }

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
        public void Stop() { if (_phase != CharacterCoursePhase.Completed) _phase = CharacterCoursePhase.Stopped; }
        public bool IsBroken(int index) => _broken[index];
        public bool TryIndex(ulong id, out int index)
        {
            index = id >= FirstEntityId && id - FirstEntityId < (ulong)_broken.Length ? (int)(id - FirstEntityId) : -1;
            return index >= 0;
        }
        public ulong ResolveAlias(byte alias)
        {
            int index = alias - FirstInputAlias;
            return index >= 0 && index < _broken.Length && !_broken[index] ? FirstEntityId + (ulong)index : 0;
        }
        public bool Break(int index)
        {
            if (_phase != CharacterCoursePhase.Running || _broken[index]) return false;
            _broken[index] = true; _brokenCount++; Emit(CharacterCourseEventKind.TargetBroken, index); return true;
        }
        public IEnumerable<SpellResolver.EntityData> CollisionTargets()
        {
            if (_phase != CharacterCoursePhase.Running) yield break;
            for (int i = 0; i < _broken.Length; i++)
            {
                if (_broken[i]) continue;
                var target = _definition.Targets[i];
                yield return new SpellResolver.EntityData { Id = FirstEntityId + (ulong)i,
                    PosX = target.Position.X, PosY = target.Position.Y, PosZ = target.Position.Z,
                    EndX = target.Position.X, EndY = target.Position.Y, EndZ = target.Position.Z,
                    Radius = target.Radius, Shape = HitboxShape.Sphere, Active = true };
            }
        }
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
