using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopArena.Shared;

/// <summary>Course objectives decide the outcome; falls never eliminate a fighter.</summary>
public sealed class CourseMatchRule : IMatchRule
{
    public bool Cooperative { get; }
    public CourseMatchRule(bool cooperative) => Cooperative = cooperative;
    public bool IsEliminated(CharacterState state) => false;
    public MatchOutcome Evaluate(IReadOnlyDictionary<ulong, CharacterState> states) => default;
}

public readonly record struct CoursePlayerProgress(ulong EntityId, CharacterCourseState Course, byte Place, bool Withdrawn);
public readonly record struct OnlineCourseState(bool Cooperative, ulong ElapsedTicks, ushort FinishWindowTicksLeft,
    bool Ended, ulong WinnerEntityId);
public readonly record struct CourseEventKey(uint MatchTick, ulong EntityId, CharacterCourseEventKind Kind, int Index, ushort Sequence);
public readonly record struct CourseTickEvent(uint MatchTick, ulong EntityId, CharacterCourseEvent Event, ushort Sequence = 0)
{
    public CourseEventKey Key => new(MatchTick, EntityId, Event.Kind, Event.Index, Sequence);
}

/// <summary>
/// Multiplayer composition of the ordinary course rules. Co-op shares target hits while each
/// fighter retains their own checkpoint. Race gives each attacker a private collision target set.
/// </summary>
internal sealed class OnlineCourseSimulation
{
    internal const ushort FinishWindowTicks = 30 * 60;
    private readonly CharacterCourseDefinition _definition;
    private readonly bool _cooperative;
    private readonly SortedDictionary<ulong, CharacterCourseSimulation> _courses = new();
    private readonly Dictionary<ulong, byte> _places = new();
    private readonly HashSet<ulong> _withdrawn = new();
    private readonly List<CourseTickEvent> _events = new();
    private bool _begun, _ended;
    private ulong _elapsed, _firstFinish;
    private ushort _remaining;

    public OnlineCourseSimulation(CharacterCourseDefinition definition, bool cooperative)
    { _definition = definition; _cooperative = cooperative; }
    public OnlineCourseState State => new(_cooperative, _elapsed, _remaining, _ended, Winner);
    private ulong Winner => _places.Where(x => x.Value == 1).Select(x => x.Key).DefaultIfEmpty().Min();
    public IReadOnlyList<CourseTickEvent> Events => _events;
    public IEnumerable<CoursePlayerProgress> Players => _courses.Select(x =>
        new CoursePlayerProgress(x.Key, x.Value.State, Place(x.Key), _withdrawn.Contains(x.Key)));
    public CharacterCourseSimulation? For(ulong id) => _courses.TryGetValue(id, out var course) ? course : null;
    private byte Place(ulong id) => _places.TryGetValue(id, out var place) ? place : (byte)0;
    public bool Inactive(ulong id) => _withdrawn.Contains(id) || Place(id) != 0;

    public void Register(ulong id)
    {
        if (_begun || id == 0 || id >= CharacterCourseSimulation.FirstInputAlias || _courses.Count >= 4)
            throw new ArgumentException("Online courses admit two to four fighter identities below target aliases.");
        _courses.Add(id, new CharacterCourseSimulation(_definition, _cooperative ? 0 : id));
    }
    public void Begin()
    {
        if (_begun || _courses.Count is < 2 or > 4) throw new InvalidOperationException("An online course needs its full roster.");
        foreach (var course in _courses.Values) course.BeginRun();
        _begun = true;
    }
    public bool BeginTick()
    {
        _events.Clear();
        if (!_begun || _ended)
        { foreach (var course in _courses.Values) course.ClearEvents(); return false; }
        foreach (var course in _courses.Values) course.BeginTick();
        _elapsed++;
        if (_remaining > 0) _remaining--;
        return true;
    }
    public void Withdraw(ulong id)
    {
        if (!_courses.ContainsKey(id) || !_withdrawn.Add(id)) return;
        if (Place(id) == 0) _courses[id].Stop();
        CheckEnd();
    }
    public bool TryTarget(ulong targetId, out CharacterCourseSimulation? course, out int index)
    {
        foreach (var pair in _courses)
        {
            if (_cooperative && _withdrawn.Contains(pair.Key)) continue;
            if (pair.Value.TryIndex(targetId, out index)) { course = pair.Value; return true; }
        }
        course = null; index = -1; return false;
    }
    public bool CanAttack(ulong owner, ulong target)
    {
        if (Inactive(owner) || Inactive(target)) return false;
        if (TryTarget(target, out var targets, out _))
            return _cooperative || ReferenceEquals(targets, For(owner));
        return !_cooperative || owner == target || !_courses.ContainsKey(target);
    }
    public bool Hit(ulong owner, int index)
    {
        if (Inactive(owner) || !_courses.TryGetValue(owner, out var course)) return false;
        if (!_cooperative) return course.Hit(index);
        bool accepted = false;
        foreach (var pair in _courses)
            if (!_withdrawn.Contains(pair.Key)) accepted |= pair.Value.Hit(index);
        return accepted;
    }
    public IEnumerable<SpellResolver.EntityData> CollisionTargets()
    {
        foreach (var pair in _courses)
        {
            if (Inactive(pair.Key)) continue;
            foreach (var target in pair.Value.CollisionTargets()) yield return target;
            if (_cooperative) yield break;
        }
    }
    public void FinishTick(uint tick, IReadOnlyDictionary<ulong, ArcadePoint> previous,
        IReadOnlyDictionary<ulong, CharacterState> states, IReadOnlyList<ulong> deaths)
    {
        foreach (var pair in _courses)
        {
            if (Inactive(pair.Key)) continue;
            var current = states[pair.Key];
            pair.Value.FinishTick(previous[pair.Key], new(current.PX, current.PY, current.PZ), deaths.Contains(pair.Key));
            if (pair.Value.State.Phase == CharacterCoursePhase.Completed)
            {
                if (_firstFinish == 0) { _firstFinish = _elapsed; _remaining = FinishWindowTicks; }
                byte place = (byte)(1 + _places.Count(x => _courses[x.Key].State.ElapsedTicks < _elapsed));
                _places.Add(pair.Key, place);
            }
        }
        ulong sharedEventsOwner = _courses.FirstOrDefault(x => !_withdrawn.Contains(x.Key)).Key;
        ushort sequence = 0;
        foreach (var pair in _courses)
            foreach (var item in pair.Value.Events)
            {
                bool target = item.Kind is CharacterCourseEventKind.TargetBroken or CharacterCourseEventKind.TargetHit;
                if (_cooperative && target && pair.Key != sharedEventsOwner) continue;
                _events.Add(new(tick, _cooperative && target ? 0 : pair.Key, item, sequence++));
            }
        CheckEnd();
    }
    private void CheckEnd()
    {
        _ended |= _begun && (_cooperative && _places.Count > 0
            || _courses.Keys.All(Inactive) || _firstFinish > 0 && _remaining == 0);
    }
    public MatchOutcome Outcome => !_ended ? default : _cooperative
        ? new(true, 0, _places.Count > 0)
        : new(true, _places.Count(x => x.Value == 1) == 1 ? Winner : 0, _places.Count(x => x.Value == 1) > 1);

    public void WriteSnapshot(SnapshotWriter w)
    {
        w.WriteBool(_begun); w.WriteBool(_ended); w.WriteULong(_elapsed); w.WriteULong(_firstFinish);
        w.WriteUShort(_remaining); w.WriteInt(_courses.Count);
        foreach (var pair in _courses)
        {
            w.WriteULong(pair.Key); w.WriteByte(Place(pair.Key)); w.WriteBool(_withdrawn.Contains(pair.Key));
            pair.Value.WriteSnapshot(w);
        }
    }
    internal sealed record Snapshot(bool Begun, bool Ended, ulong Elapsed, ulong FirstFinish, ushort Remaining,
        (ulong Id, byte Place, bool Withdrawn, CharacterCourseSimulation.Snapshot Course)[] Players);
    public Snapshot ReadSnapshot(SnapshotReader r)
    {
        bool begun = r.ReadBool(), ended = r.ReadBool(); ulong elapsed = r.ReadULong(), first = r.ReadULong();
        ushort remaining = r.ReadUShort(); int count = r.ReadCount(10);
        if (count != _courses.Count || remaining > FinishWindowTicks || first > elapsed
            || first == 0 && remaining != 0 || !begun && (elapsed != 0 || first != 0 || ended))
            throw SnapshotReader.Malformed("online course clock");
        var players = new (ulong Id, byte Place, bool Withdrawn, CharacterCourseSimulation.Snapshot Course)[count];
        int i = 0;
        foreach (var pair in _courses)
        {
            ulong id = r.ReadULong(); byte place = r.ReadByte(); bool withdrawn = r.ReadBool();
            if (id != pair.Key || place > count) throw SnapshotReader.Malformed("course racer identity/place");
            var course = pair.Value.ReadSnapshot(r);
            if ((place != 0) != (course.Phase == CharacterCoursePhase.Completed) || course.Elapsed > elapsed)
                throw SnapshotReader.Malformed("course finish");
            players[i++] = (id, place, withdrawn, course);
        }
        return new(begun, ended, elapsed, first, remaining, players);
    }
    public void Apply(Snapshot snapshot)
    {
        _begun = snapshot.Begun; _ended = snapshot.Ended; _elapsed = snapshot.Elapsed;
        _firstFinish = snapshot.FirstFinish; _remaining = snapshot.Remaining;
        _places.Clear(); _withdrawn.Clear(); _events.Clear();
        foreach (var player in snapshot.Players)
        {
            _courses[player.Id].Apply(player.Course);
            if (player.Place != 0) _places.Add(player.Id, player.Place);
            if (player.Withdrawn) _withdrawn.Add(player.Id);
        }
    }
}
