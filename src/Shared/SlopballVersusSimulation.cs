using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopArena.Shared;

public enum SlopballCamp : byte { None, Teal, Clay }
public enum SlopballVersusPhase : byte { WaitingServe, HeldServe, Rally, Ended }

public static class SlopballCamps
{
    public static SlopballCamp Opponent(this SlopballCamp camp) => camp switch
    {
        SlopballCamp.Teal => SlopballCamp.Clay,
        SlopballCamp.Clay => SlopballCamp.Teal,
        _ => SlopballCamp.None,
    };
    public static string ToWire(this SlopballCamp camp) => camp switch
    {
        SlopballCamp.Teal => "teal", SlopballCamp.Clay => "clay", _ => "",
    };
    public static bool TryParse(string? value, out SlopballCamp camp)
    {
        camp = value switch { null or "" => SlopballCamp.None, "teal" => SlopballCamp.Teal,
            "clay" => SlopballCamp.Clay, _ => (SlopballCamp)byte.MaxValue };
        return camp <= SlopballCamp.Clay;
    }
    public static bool AreBalanced(IEnumerable<SlopballCamp> camps)
    {
        var list = camps.ToArray();
        return list.Length is 2 or 4 && list.All(x => x is SlopballCamp.Teal or SlopballCamp.Clay)
            && list.Count(x => x == SlopballCamp.Teal) == list.Length / 2;
    }
}

public readonly record struct SlopballVersusState(SlopballVersusPhase Phase, ushort TealScore,
    ushort ClayScore, SlopballCamp ServingCamp, ulong ServerEntityId, ushort ServeInTicks,
    SlopballCamp Winner)
{
    public bool Ended => Phase == SlopballVersusPhase.Ended;
}

/// <summary>Two balanced camps; immutable setup shared by GameHost and client prediction.</summary>
public sealed class SlopballVersusMatchRule : IMatchRule
{
    public const ushort ScoreToWin = 21;
    private readonly SortedDictionary<ulong, SlopballCamp> _camps;
    public IReadOnlyDictionary<ulong, SlopballCamp> Camps { get; }
    public bool BallOnly { get; }

    public SlopballVersusMatchRule(IReadOnlyDictionary<ulong, SlopballCamp> camps, bool ballOnly = false)
    {
        if (camps == null || camps.Count is not (2 or 4)
            || camps.Any(x => x.Key == 0 || x.Key >= ServerSimulation.ArcadeBallTargetInputId
                || x.Value is not (SlopballCamp.Teal or SlopballCamp.Clay))
            || camps.Count(x => x.Value == SlopballCamp.Teal) != camps.Count / 2)
            throw new ArgumentException("Versus Slopball needs balanced camps of one or two fighters.", nameof(camps));
        _camps = new SortedDictionary<ulong, SlopballCamp>();
        foreach (var pair in camps) _camps.Add(pair.Key, pair.Value);
        Camps = new System.Collections.ObjectModel.ReadOnlyDictionary<ulong, SlopballCamp>(_camps);
        BallOnly = ballOnly;
    }

    public SlopballCamp CampOf(ulong id) => _camps.TryGetValue(id, out var camp) ? camp : SlopballCamp.None;
    public bool IsEliminated(CharacterState state) => false;
    public MatchOutcome Evaluate(IReadOnlyDictionary<ulong, CharacterState> states) => default;
}

/// <summary>Rally and point authority around the existing ball; no fighter or physics duplication.</summary>
internal sealed class SlopballVersusSimulation
{
    public const ushort PointGapTicks = 90;
    private readonly SlopballDefinition _court;
    private readonly SlopballVersusMatchRule _rule;
    private readonly List<SlopballEvent> _events = new();
    private readonly List<ArcadeBallContact> _floors = new();
    private SlopballVersusState _state = new(SlopballVersusPhase.WaitingServe, 0, 0,
        SlopballCamp.Teal, 0, 1, SlopballCamp.None);
    private ushort _tealServes, _clayServes;
    private bool _attacked;
    public SlopballVersusState State => _state;
    public IReadOnlyList<SlopballEvent> Events => _events;

    public SlopballVersusSimulation(SlopballDefinition court, SlopballVersusMatchRule rule, ArcadeBallSimulation ball)
    { _court = court; _rule = rule; ball.Retire(); }

    public void Begin()
    {
        if (_state.ServerEntityId != 0 || _state.TealScore != 0 || _state.ClayScore != 0)
            throw new InvalidOperationException("Versus Slopball must start from a clean match.");
        _state = _state with { ServerEntityId = NextServer(SlopballCamp.Teal) };
    }

    public void BeginTick(ArcadeBallSimulation ball, Func<ulong, ArcadePoint> servePosition)
    {
        _events.Clear(); _floors.Clear(); _attacked = false;
        if (_state.Phase != SlopballVersusPhase.WaitingServe || _state.ServerEntityId == 0) return;
        _state = _state with { ServeInTicks = (ushort)(_state.ServeInTicks - 1) };
        if (_state.ServeInTicks != 0) return;
        ball.Reset(servePosition(_state.ServerEntityId), default);
        _state = _state with { Phase = SlopballVersusPhase.HeldServe };
        Emit(SlopballEventKind.ServeReady, ball, _state.ServingCamp, 0f);
    }

    public void PlanMotion(ArcadeBallSimulation ball)
    {
        if (_state.Phase != SlopballVersusPhase.Rally) return;
        foreach (var contact in ball.Contacts)
            if (!contact.Body && contact.NormalY > .5f
                && contact.Position.Y <= _court.FloorY + ball.State.Radius + .001f)
                _floors.Add(contact);
        _floors.Sort((a, b) => a.Time.CompareTo(b.Time));
        if (_floors.Count > 0) ball.ClipCollisionPath(MathF.Max(0f, _floors[0].Time - .000001f));
    }

    public bool BeforeAttack(ArcadeBallSimulation ball, float time, ulong ownerId)
    {
        CommitThrough(ball, time);
        if (_state.Phase == SlopballVersusPhase.HeldServe)
        {
            if (ownerId != _state.ServerEntityId) return false;
            _state = _state with { Phase = SlopballVersusPhase.Rally };
            Emit(SlopballEventKind.Served, ball, _state.ServingCamp, time);
        }
        if (_state.Phase != SlopballVersusPhase.Rally) return false;
        _attacked = true;
        return true;
    }

    public void FinishTick(ArcadeBallSimulation ball)
    { if (!_attacked) CommitThrough(ball, 1f); }

    private void CommitThrough(ArcadeBallSimulation ball, float time)
    {
        if (_state.Phase != SlopballVersusPhase.Rally || _floors.Count == 0 || _floors[0].Time > time) return;
        var floor = _floors[0];
        var loser = floor.Position.Z <= _court.NetZ ? SlopballCamp.Teal : SlopballCamp.Clay;
        var scorer = loser.Opponent();
        ball.RetireAt(floor.Position);
        _state = _state with
        {
            TealScore = (ushort)(_state.TealScore + (scorer == SlopballCamp.Teal ? 1 : 0)),
            ClayScore = (ushort)(_state.ClayScore + (scorer == SlopballCamp.Clay ? 1 : 0)),
            ServingCamp = loser, Phase = SlopballVersusPhase.WaitingServe, ServeInTicks = PointGapTicks,
        };
        Emit(SlopballEventKind.PointScored, ball, scorer, floor.Time);
        if (_state.TealScore >= SlopballVersusMatchRule.ScoreToWin || _state.ClayScore >= SlopballVersusMatchRule.ScoreToWin)
        {
            _state = _state with { Phase = SlopballVersusPhase.Ended, Winner = scorer, ServeInTicks = 0, ServerEntityId = 0 };
            Emit(SlopballEventKind.Stopped, ball, scorer, floor.Time);
        }
        else _state = _state with { ServerEntityId = NextServer(loser) };
    }

    private ulong NextServer(SlopballCamp camp)
    {
        var members = _rule.Camps.Where(x => x.Value == camp).Select(x => x.Key).ToArray();
        ushort sequence = camp == SlopballCamp.Teal ? _tealServes++ : _clayServes++;
        return members[sequence % members.Length];
    }

    private void Emit(SlopballEventKind kind, ArcadeBallSimulation ball, SlopballCamp camp, float time)
        => _events.Add(new SlopballEvent(kind, ball.State.EntityId, 0, time, camp));

    internal readonly record struct Snapshot(SlopballVersusState State, ushort TealServes, ushort ClayServes)
    {
        public void Write(SnapshotWriter w)
        {
            w.WriteByte((byte)State.Phase); w.WriteUShort(State.TealScore); w.WriteUShort(State.ClayScore);
            w.WriteByte((byte)State.ServingCamp); w.WriteULong(State.ServerEntityId); w.WriteUShort(State.ServeInTicks);
            w.WriteByte((byte)State.Winner); w.WriteUShort(TealServes); w.WriteUShort(ClayServes);
        }
        public static Snapshot Read(SnapshotReader r)
        {
            byte phase = r.ReadByte(); ushort teal = r.ReadUShort(), clay = r.ReadUShort();
            byte camp = r.ReadByte(); ulong server = r.ReadULong(); ushort gap = r.ReadUShort();
            byte winner = r.ReadByte(); ushort tealServes = r.ReadUShort(), clayServes = r.ReadUShort();
            if (phase > (byte)SlopballVersusPhase.Ended || camp is < 1 or > 2 || winner > 2
                || teal > SlopballVersusMatchRule.ScoreToWin || clay > SlopballVersusMatchRule.ScoreToWin
                || gap > PointGapTicks)
                throw SnapshotReader.Malformed("Versus Slopball state");
            return new(new((SlopballVersusPhase)phase, teal, clay, (SlopballCamp)camp, server, gap,
                (SlopballCamp)winner), tealServes, clayServes);
        }
    }
    public Snapshot Capture() => new(_state, _tealServes, _clayServes);
    public void Validate(Snapshot snapshot)
    {
        var state = snapshot.State;
        bool ended = state.Phase == SlopballVersusPhase.Ended;
        bool clean = state.ServerEntityId == 0 && state.Phase == SlopballVersusPhase.WaitingServe
            && state.TealScore == 0 && state.ClayScore == 0 && state.ServeInTicks == 1;
        if (ended ? state.Winner is not (SlopballCamp.Teal or SlopballCamp.Clay)
                || state.ServerEntityId != 0 || state.ServeInTicks != 0
                || (state.Winner == SlopballCamp.Teal
                    ? state.TealScore != SlopballVersusMatchRule.ScoreToWin || state.ClayScore >= state.TealScore
                    : state.ClayScore != SlopballVersusMatchRule.ScoreToWin || state.TealScore >= state.ClayScore)
            : state.Winner != SlopballCamp.None || state.TealScore >= SlopballVersusMatchRule.ScoreToWin
                || state.ClayScore >= SlopballVersusMatchRule.ScoreToWin
                || !clean && _rule.CampOf(state.ServerEntityId) != state.ServingCamp
                || (state.Phase == SlopballVersusPhase.WaitingServe ? state.ServeInTicks == 0 : state.ServeInTicks != 0))
            throw SnapshotReader.Malformed("Versus Slopball server");
    }
    public void Apply(Snapshot snapshot)
    {
        Validate(snapshot);
        _state = snapshot.State; _tealServes = snapshot.TealServes; _clayServes = snapshot.ClayServes;
        _events.Clear(); _floors.Clear(); _attacked = false;
    }
}
