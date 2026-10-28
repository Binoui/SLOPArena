using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopArena.Shared;

public enum CourseFormat : byte { Race, Coop, Scramble }

public static class CourseScrambleTuning
{
    public const ushort TargetPoints = 1;
    public const ushort BonusTargetPoints = 3;
    public const ushort FirstFinishPoints = 5, SecondFinishPoints = 3, ThirdFinishPoints = 2, FourthFinishPoints = 1;
    public const ushort FinishWindowTicks = 30 * 60;
    public const float HorizontalLeash = 20f, BelowLeaderLeash = 10f;
    public const ushort LeashTicks = 45;
    public const ushort SafeGroundLagTicks = 60;
    public static ushort FinishPoints(byte place) => place switch
    { 1 => FirstFinishPoints, 2 => SecondFinishPoints, 3 => ThirdFinishPoints, 4 => FourthFinishPoints, _ => 0 };
    public static ushort PointsFor(CourseTargetDefinition target) => target.Hits > 1 ? BonusTargetPoints : TargetPoints;
    public static bool OutsideLeash(in CharacterState leader, in CharacterState racer)
    {
        float x = leader.PX - racer.PX, z = leader.PZ - racer.PZ;
        return x * x + z * z > HorizontalLeash * HorizontalLeash || leader.PY - racer.PY > BelowLeaderLeash;
    }
}

public readonly record struct CourseSafeGround(ArcadePoint Position, float Yaw, float Progress = 0);
public readonly record struct CourseScrambleState(ulong LeaderEntityId, CourseSafeGround SafeGround);

internal sealed class CourseScramble
{
    private readonly CharacterCourseDefinition _definition;
    private sealed class Racer
    {
        public ushort Points, Leash;
        public float Progress;
        public CourseSafeGround LastGround, SafeGround;
        public readonly CourseSafeGround[] Ground = new CourseSafeGround[CourseScrambleTuning.SafeGroundLagTicks];
    }
    private readonly SortedDictionary<ulong, Racer> _racers = new();
    private readonly ulong[] _breakers;
    private int _cursor;
    private ulong _leader;
    public CourseScrambleState State => new(_leader, _leader == 0 ? default : _racers[_leader].SafeGround);
    public CourseScramble(CharacterCourseDefinition definition)
    { _definition = definition; _breakers = new ulong[definition.Targets.Count]; }
    public void Register(ulong id, CharacterState start)
    {
        var racer = new Racer { LastGround = new(new(start.PX, start.PY, start.PZ), start.FacingYaw) };
        racer.SafeGround = racer.LastGround;
        Array.Fill(racer.Ground, racer.LastGround); _racers.Add(id, racer);
    }
    public ushort Points(ulong id) => _racers[id].Points;
    public ushort Leash(ulong id) => _racers[id].Leash;
    public float Progress(ulong id) => _racers[id].Progress;
    public ulong Breaker(int index) => _breakers[index];
    public void Break(ulong id, int index)
    {
        _breakers[index] = id;
        AddPoints(id, CourseScrambleTuning.PointsFor(_definition.Targets[index]));
    }
    public void Finish(ulong id, byte place) => AddPoints(id, CourseScrambleTuning.FinishPoints(place));
    private void AddPoints(ulong id, ushort points)
    { _racers[id].Points = checked((ushort)(_racers[id].Points + points)); }

    public void Tick(IReadOnlyList<CoursePlayerProgress> players, IReadOnlyDictionary<ulong, CharacterState> states,
        IReadOnlyList<ulong> deaths, Action<ulong, CourseSafeGround> respawn)
    {
        foreach (var player in players)
        {
            var current = states[player.EntityId]; var racer = _racers[player.EntityId];
            if (_definition.RacingLine is { } line && !player.Withdrawn && !deaths.Contains(player.EntityId))
                racer.Progress = line.Project(new(current.PX, current.PY, current.PZ), racer.Progress);
            if (current.IsGrounded && !deaths.Contains(player.EntityId))
                racer.LastGround = new(new(current.PX, current.PY, current.PZ), current.FacingYaw, racer.Progress);
            racer.SafeGround = racer.Ground[_cursor];
            racer.Ground[_cursor] = racer.LastGround;
        }
        _cursor = (_cursor + 1) % CourseScrambleTuning.SafeGroundLagTicks;
        _leader = (_definition.RacingLine != null
            ? players.Where(p => !p.Withdrawn).OrderByDescending(p => _racers[p.EntityId].Progress).ThenBy(p => p.EntityId)
            : players.Where(p => !p.Withdrawn).OrderByDescending(p => p.Course.SavedCheckpoint)
                .ThenBy(p => DistanceToNext(p, states[p.EntityId])).ThenBy(p => p.EntityId)).FirstOrDefault().EntityId;
        if (_leader == 0) return;
        var state = states[_leader];
        foreach (var player in players)
        {
            if (player.Withdrawn || player.Place != 0) continue;
            ushort leash = CourseScrambleTuning.OutsideLeash(state, states[player.EntityId])
                ? (ushort)(_racers[player.EntityId].Leash + 1) : (ushort)0;
            if (deaths.Contains(player.EntityId) || leash >= CourseScrambleTuning.LeashTicks)
            {
                var ground = State.SafeGround;
                respawn(player.EntityId, ground);
                _racers[player.EntityId].Progress = ground.Progress;
                leash = 0;
            }
            _racers[player.EntityId].Leash = leash;
        }
    }
    private float DistanceToNext(CoursePlayerProgress player, CharacterState state)
    {
        int next = player.Course.SavedCheckpoint + 1;
        var target = next < _definition.Checkpoints.Count ? _definition.Checkpoints[next].Volume.Center : _definition.Finish.Center;
        float x = target.X - state.PX, y = target.Y - state.PY, z = target.Z - state.PZ;
        return x * x + y * y + z * z;
    }
    public void Write(SnapshotWriter w)
    {
        w.WriteByte((byte)CourseFormat.Scramble); w.WriteULong(_leader); w.WriteUShort((ushort)_cursor);
        foreach (var racer in _racers.Values)
        {
            w.WriteUShort(racer.Points); w.WriteUShort(racer.Leash);
            if (_definition.RacingLine != null) w.WriteFloat(racer.Progress);
            WriteGround(w, racer.LastGround); WriteGround(w, racer.SafeGround);
            foreach (var ground in racer.Ground) WriteGround(w, ground);
        }
        foreach (ulong breaker in _breakers) w.WriteULong(breaker);
    }
    private void WriteGround(SnapshotWriter w, CourseSafeGround ground)
    {
        w.WriteFloat(ground.Position.X); w.WriteFloat(ground.Position.Y); w.WriteFloat(ground.Position.Z); w.WriteFloat(ground.Yaw);
        if (_definition.RacingLine != null) w.WriteFloat(ground.Progress);
    }
    private float ReadProgress(SnapshotReader r)
    {
        if (_definition.RacingLine == null) return 0;
        float progress = r.ReadFloat();
        if (!float.IsFinite(progress) || progress < 0 || progress > _definition.RacingLine.Length)
            throw SnapshotReader.Malformed("Scramble racing line progress");
        return progress;
    }
    private CourseSafeGround ReadGround(SnapshotReader r)
    {
        var ground = new CourseSafeGround(new(r.ReadFloat(), r.ReadFloat(), r.ReadFloat()), r.ReadFloat(), ReadProgress(r));
        if (!ground.Position.IsFinite || !float.IsFinite(ground.Yaw)) throw SnapshotReader.Malformed("Scramble safe ground");
        return ground;
    }
    internal sealed record RacerSnapshot(ushort Points, ushort Leash, float Progress, CourseSafeGround LastGround,
        CourseSafeGround SafeGround, CourseSafeGround[] Ground);
    internal sealed record Snapshot(ulong Leader, int Cursor, RacerSnapshot[] Racers, ulong[] Breakers);
    public Snapshot Read(SnapshotReader r, OnlineCourseSimulation.Snapshot course)
    {
        if (r.ReadByte() != (byte)CourseFormat.Scramble) throw SnapshotReader.Malformed("Scramble format");
        ulong leader = r.ReadULong(); int cursor = r.ReadUShort();
        if (cursor >= CourseScrambleTuning.SafeGroundLagTicks || leader != 0 && !_racers.ContainsKey(leader) || course.Elapsed > 0 && leader == 0
            && course.Players.Any(p => !p.Withdrawn)) throw SnapshotReader.Malformed("Scramble leader/history");
        var racers = new RacerSnapshot[_racers.Count];
        for (int i = 0; i < racers.Length; i++)
        {
            ushort points = r.ReadUShort(), leash = r.ReadUShort();
            if (leash >= CourseScrambleTuning.LeashTicks) throw SnapshotReader.Malformed("Scramble leash");
            float progress = ReadProgress(r);
            var last = ReadGround(r); var safe = ReadGround(r);
            var ground = new CourseSafeGround[CourseScrambleTuning.SafeGroundLagTicks];
            for (int j = 0; j < ground.Length; j++) ground[j] = ReadGround(r);
            racers[i] = new(points, leash, progress, last, safe, ground);
        }
        var breakers = new ulong[_breakers.Length];
        var scores = _racers.Keys.ToDictionary(id => id, _ => 0);
        for (int i = 0; i < breakers.Length; i++)
        {
            ulong breaker = breakers[i] = r.ReadULong();
            byte hits = course.Players.Length == 0 ? _definition.Targets[i].Hits : course.Players.Min(p => p.Course.Hits[i]);
            if (breaker != 0 && !_racers.ContainsKey(breaker)
                || (breaker != 0) != (hits == 0)
                || course.Players.Any(p => !p.Withdrawn && p.Course.Hits[i] != hits))
                throw SnapshotReader.Malformed("Scramble breaker/shared target");
            if (breaker != 0) scores[breaker] += CourseScrambleTuning.PointsFor(_definition.Targets[i]);
        }
        for (int i = 0; i < racers.Length; i++)
            if (racers[i].Points != scores[course.Players[i].Id] + CourseScrambleTuning.FinishPoints(course.Players[i].Place))
                throw SnapshotReader.Malformed("Scramble points");
        return new(leader, cursor, racers, breakers);
    }
    public void Apply(Snapshot snapshot)
    {
        _leader = snapshot.Leader; _cursor = snapshot.Cursor;
        Array.Copy(snapshot.Breakers, _breakers, _breakers.Length);
        int i = 0;
        foreach (var racer in _racers.Values)
        {
            var source = snapshot.Racers[i++]; racer.Points = source.Points; racer.Leash = source.Leash;
            racer.Progress = source.Progress;
            racer.LastGround = source.LastGround; racer.SafeGround = source.SafeGround;
            Array.Copy(source.Ground, racer.Ground, racer.Ground.Length);
        }
    }
}
