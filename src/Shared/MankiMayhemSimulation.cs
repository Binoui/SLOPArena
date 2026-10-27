using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopArena.Shared;

public enum MankiMayhemPhase : byte { Waiting, Call, Pattern, Settle, Score, Break, Ended }
public enum MankiMayhemEventKind : byte { Call, WindUp, Launch, Bench, Score, End, Slam, WallHit, WallBroken }
public readonly record struct MankiMayhemState(uint Seed, ushort RoundIndex, MankiMayhemCard Card,
    MankiMayhemPhase Phase, ushort PhaseTick, int HazardsLaunched)
{
    public bool Ended => Phase == MankiMayhemPhase.Ended;
    public int RoundCount => MankiMayhemTuning.DeckPasses * MankiMayhemRounds.DeckSize;
}
public readonly record struct MayhemPlayer(ulong EntityId, ushort Score, ushort RoundsSurvived,
    ushort Bonuses, bool Benched, bool Withdrawn);
public readonly record struct MayhemEventKey(uint MatchTick, ulong EntityId, ushort RoundIndex,
    MankiMayhemEventKind Kind, int OperationIndex);
public readonly record struct MankiMayhemEvent(uint MatchTick, ushort RoundIndex, MankiMayhemEventKind Kind,
    ulong EntityId, int OperationIndex, ushort Points = 0)
{
    public MayhemEventKey Key => new(MatchTick, EntityId, RoundIndex, Kind, OperationIndex);
}

/// <summary>Seeded round cursor and current round state. No random stream or future round list.</summary>
internal sealed class MankiMayhemSimulation
{
    public const ulong HostEntityId = 1UL << 48;
    private readonly MankiMayhemDefinition _stage;
    private readonly SortedDictionary<ulong, MayhemPlayer> _players = new();
    private readonly List<MankiMayhemEvent> _events = new();
    public RuntimeSolidSet Solids { get; } = new();
    private MankiMayhemState _state;
    private MayhemLandingRing[] _rings = Array.Empty<MayhemLandingRing>();
    private MayhemSlam[] _slams = Array.Empty<MayhemSlam>();
    public IReadOnlyList<MayhemSlam> Slams => Array.AsReadOnly(_slams);
    public MankiMayhemState State => _state;
    public IReadOnlyList<MayhemPlayer> Players => _players.Values.ToArray();
    public IReadOnlyList<MayhemLandingRing> Rings => Array.AsReadOnly(_rings);
    public IReadOnlyList<MankiMayhemEvent> Events => _events;
    public bool Inactive(ulong id) => _players.TryGetValue(id, out var p) && (p.Benched || p.Withdrawn);
    public MatchOutcome Outcome
    {
        get
        {
            if (!_state.Ended) return default;
            var eligible = _players.Values.Where(p => !p.Withdrawn).ToArray();
            if (eligible.Length == 0) return new(true, 0, true);
            int best = eligible.Max(p => p.Score);
            var winners = eligible.Where(p => p.Score == best).ToArray();
            return new(true, winners.Length == 1 ? winners[0].EntityId : 0, winners.Length != 1);
        }
    }

    public MankiMayhemSimulation(MankiMayhemDefinition stage) => _stage = stage;
    public void Begin(uint seed, IEnumerable<ulong> ids)
    {
        if (seed == 0 || _state.Seed != 0) throw new InvalidOperationException("Mayhem needs a nonzero seed and a clean Match.");
        var fighters = ids.OrderBy(id => id).ToArray();
        if (fighters.Length is < 2 or > 4) throw new InvalidOperationException("Mayhem needs two to four fighters.");
        foreach (ulong id in fighters) _players.Add(id, new(id, 0, 0, 0, false, false));
        _state = new(seed, 0, MankiMayhemRounds.CardAt(seed, 0), MankiMayhemPhase.Waiting, 0, 0);
    }

    public void Withdraw(ulong id)
    {
        if (_players.TryGetValue(id, out var p)) _players[id] = p with { Withdrawn = true, Benched = true };
    }

    public bool BeginTick(uint tick, IReadOnlyDictionary<ulong, CharacterState> states,
        Action reset, SpellResolver resolver)
    {
        _events.Clear(); Solids.Step();
        if (_state.Seed == 0 || _state.Ended) return false;
        if (_state.Phase == MankiMayhemPhase.Waiting)
            StartRound(tick, states, reset);
        else
        {
            _state = _state with { PhaseTick = checked((ushort)(_state.PhaseTick + 1)) };
            if (_state.Phase == MankiMayhemPhase.Call && _state.PhaseTick >= MankiMayhemRounds.CallDuration(_state.RoundIndex))
                Phase(MankiMayhemPhase.Pattern);
            else if (_state.Phase == MankiMayhemPhase.Score && _state.PhaseTick >= MankiMayhemTuning.ScoreTicks)
                Phase(MankiMayhemPhase.Break);
            else if (_state.Phase == MankiMayhemPhase.Break && _state.PhaseTick >= MankiMayhemTuning.BreakTicks)
            {
                if (_state.RoundIndex + 1 >= _state.RoundCount)
                { Phase(MankiMayhemPhase.Ended); Emit(tick, MankiMayhemEventKind.End); return false; }
                _state = _state with { RoundIndex = (ushort)(_state.RoundIndex + 1) };
                StartRound(tick, states, reset);
            }
        }
        if (_state.Phase is MankiMayhemPhase.Call or MankiMayhemPhase.Pattern)
        {
            foreach (var slam in _slams)
            {
                if (tick + MankiMayhemTuning.WindUpTicks == slam.Tick)
                    Emit(tick, MankiMayhemEventKind.WindUp, operation: slam.Ordinal);
                if (tick != slam.Tick) continue;
                MankiMayhemRounds.Slam(slam, _state.RoundIndex, resolver);
                _state = _state with { HazardsLaunched = _state.HazardsLaunched + 1 };
                Emit(tick, MankiMayhemEventKind.Slam, operation: slam.Ordinal);
            }
            foreach (var ring in _rings)
            {
                if (tick + MankiMayhemTuning.WindUpTicks == ring.LaunchTick)
                    Emit(tick, MankiMayhemEventKind.WindUp, operation: ring.Ordinal);
                if (tick != ring.LaunchTick) continue;
                MankiMayhemRounds.Launch(ring, _state.RoundIndex, _stage, resolver, _state.Card == MankiMayhemCard.BreakOut);
                _state = _state with { HazardsLaunched = _state.HazardsLaunched + 1 };
                Emit(tick, MankiMayhemEventKind.Launch, operation: ring.Ordinal);
            }
        }
        return _state.Phase is MankiMayhemPhase.Call or MankiMayhemPhase.Pattern or MankiMayhemPhase.Settle;
    }

    private void StartRound(uint tick, IReadOnlyDictionary<ulong, CharacterState> states, Action reset)
    {
        foreach (ulong id in _players.Keys.ToArray())
            _players[id] = _players[id] with { Benched = _players[id].Withdrawn };
        Solids.Clear();
        reset();
        Phase(MankiMayhemPhase.Call);
        _state = _state with { Card = MankiMayhemRounds.CardAt(_state.Seed, _state.RoundIndex), HazardsLaunched = 0 };
        var active = _players.Values.Where(p => !p.Withdrawn).Select(p => states[p.EntityId]).ToArray();
        _rings = active.Length == 0 ? Array.Empty<MayhemLandingRing>()
            : _state.Card == MankiMayhemCard.Scatter
                ? MankiMayhemRounds.Scatter(_state.Seed, _state.RoundIndex, tick, _stage, active)
                : _state.Card == MankiMayhemCard.BreakOut ? MankiMayhemRounds.BreakOut(tick, _stage, active)
                : Array.Empty<MayhemLandingRing>();
        if (_state.Card == MankiMayhemCard.BreakOut)
            for (int i = 0; i < active.Length; i++)
                Solids.AddCage(active[i].EntityId, new(active[i].PX, _stage.DeckTop, active[i].PZ), _stage.DeckTop, i);
        _slams = _state.Card == MankiMayhemCard.Jump ? MankiMayhemRounds.Jump(_state.RoundIndex, tick, _stage) : Array.Empty<MayhemSlam>();
        Emit(tick, MankiMayhemEventKind.Call);
    }

    public void FinishTick(uint tick, IReadOnlyDictionary<ulong, CharacterState> states,
        IEnumerable<ulong> deaths, Action<ulong> bench, SpellResolver resolver)
    {
        foreach (ulong id in deaths)
        {
            if (!_players.TryGetValue(id, out var p) || p.Benched) continue;
            _players[id] = p with { Benched = true };
            bench(id); Emit(tick, MankiMayhemEventKind.Bench, id);
        }
        var survivors = _players.Values.Where(p => !p.Benched && !p.Withdrawn).ToArray();
        if (_state.Phase is MankiMayhemPhase.Call or MankiMayhemPhase.Pattern
            && (survivors.Length <= 1 || (_rings.Length == 0 && _slams.Length == 0)
                || (_rings.Length > 0 && tick >= _rings[_rings.Length - 1].LandingTick)
                || (_slams.Length > 0 && tick >= _slams[_slams.Length - 1].Tick)))
        {
            _rings = _rings.Take(_state.HazardsLaunched).ToArray();
            Phase(MankiMayhemPhase.Settle);
        }
        if (_state.Phase != MankiMayhemPhase.Settle || resolver.HasOwnerEffects(HostEntityId)
            || survivors.Any(p => !states[p.EntityId].IsGrounded)) return;
        resolver.RemoveOwnerEffects(HostEntityId);
        foreach (var player in survivors)
        {
            ushort bonus = survivors.Length == 1 ? MankiMayhemTuning.LastStandingBonus : (ushort)0;
            ushort points = (ushort)(MankiMayhemTuning.SurvivalPoints + bonus);
            _players[player.EntityId] = player with { Score = (ushort)(player.Score + points),
                RoundsSurvived = (ushort)(player.RoundsSurvived + 1), Bonuses = (ushort)(player.Bonuses + bonus) };
            Emit(tick, MankiMayhemEventKind.Score, player.EntityId, points: points);
        }
        Solids.Sink();
        Phase(MankiMayhemPhase.Score);
    }

    public void HitWall(uint tick, ulong id, bool wreck = false)
    {
        if (!Solids.Hit(id, wreck, out var wall)) return;
        Emit(tick, wall.HitsLeft == 0 ? MankiMayhemEventKind.WallBroken : MankiMayhemEventKind.WallHit,
            id, (int)(id - RuntimeSolidSet.FirstEntityId));
    }

    private void Phase(MankiMayhemPhase phase) => _state = _state with { Phase = phase, PhaseTick = 0 };
    private void Emit(uint tick, MankiMayhemEventKind kind, ulong id = HostEntityId, int operation = 0, ushort points = 0)
        => _events.Add(new(tick, _state.RoundIndex, kind, id, operation, points));

    internal sealed record Snapshot(MankiMayhemState State, MayhemPlayer[] Players, MayhemLandingRing[] Rings, MayhemSlam[] Slams, RuntimeSolid[] Walls);
    public Snapshot Capture() => new(_state, _players.Values.ToArray(), (MayhemLandingRing[])_rings.Clone(), (MayhemSlam[])_slams.Clone(), Solids.Walls.ToArray());
    public void WriteSnapshot(SnapshotWriter w)
    {
        w.WriteUInt(_state.Seed); w.WriteUShort(_state.RoundIndex); w.WriteByte((byte)_state.Card);
        w.WriteByte((byte)_state.Phase); w.WriteUShort(_state.PhaseTick); w.WriteInt(_state.HazardsLaunched);
        w.WriteInt(_players.Count);
        foreach (var p in _players.Values)
        { w.WriteULong(p.EntityId); w.WriteUShort(p.Score); w.WriteUShort(p.RoundsSurvived); w.WriteUShort(p.Bonuses);
            w.WriteBool(p.Benched); w.WriteBool(p.Withdrawn); }
        w.WriteInt(_rings.Length);
        foreach (var ring in _rings)
        { w.WriteInt(ring.Ordinal); w.WriteFloat(ring.Position.X); w.WriteFloat(ring.Position.Y); w.WriteFloat(ring.Position.Z);
            w.WriteUInt(ring.LaunchTick); w.WriteUInt(ring.LandingTick); w.WriteUShort(ring.FuseTicks); }
        WriteSlams(w); Solids.WriteSnapshot(w);
    }
    private void WriteSlams(SnapshotWriter w)
    {
        w.WriteInt(_slams.Length);
        foreach (var slam in _slams)
        { w.WriteInt(slam.Ordinal); w.WriteFloat(slam.Position.X); w.WriteFloat(slam.Position.Y); w.WriteFloat(slam.Position.Z); w.WriteUInt(slam.Tick); }
    }
    public Snapshot ReadSnapshot(SnapshotReader r, IReadOnlyList<ulong> ids)
    {
        var state = new MankiMayhemState(r.ReadUInt(), r.ReadUShort(), (MankiMayhemCard)r.ReadByte(),
            (MankiMayhemPhase)r.ReadByte(), r.ReadUShort(), r.ReadInt());
        if (state.Card > MankiMayhemCard.BreakOut || state.Phase > MankiMayhemPhase.Ended
            || state.RoundIndex >= state.RoundCount || state.HazardsLaunched < 0 || state.HazardsLaunched > MankiMayhemTuning.MaxBombs)
            throw SnapshotReader.Malformed("Mayhem cursor");
        int count = r.ReadCount(1);
        if ((count != 0 && count != ids.Count) || count > 4 || (count == 0 && state.Seed != 0))
            throw SnapshotReader.Malformed("Mayhem fighters");
        var players = new MayhemPlayer[count];
        for (int i = 0; i < count; i++)
        {
            players[i] = new(r.ReadULong(), r.ReadUShort(), r.ReadUShort(), r.ReadUShort(), r.ReadBool(), r.ReadBool());
            if (players[i].EntityId != ids[i] || players[i].Withdrawn && !players[i].Benched
                || players[i].RoundsSurvived > state.RoundCount || players[i].Bonuses > players[i].RoundsSurvived)
                throw SnapshotReader.Malformed("Mayhem player");
        }
        int hazards = r.ReadCount(1);
        if (hazards > MankiMayhemTuning.MaxBombs || state.Card != MankiMayhemCard.Jump && state.HazardsLaunched > hazards) throw SnapshotReader.Malformed("Mayhem rings");
        var rings = new MayhemLandingRing[hazards];
        for (int i = 0; i < hazards; i++)
        {
            rings[i] = new(r.ReadInt(), new(r.ReadFloat(), r.ReadFloat(), r.ReadFloat()), r.ReadUInt(), r.ReadUInt(), r.ReadUShort());
            var ring = rings[i];
            if (ring.Ordinal != i || !ring.Position.IsFinite || ring.Position.X < _stage.MinX || ring.Position.X > _stage.MaxX
                || ring.Position.Z < _stage.MinZ || ring.Position.Z > _stage.MaxZ || ring.Position.Y != _stage.DeckTop
                || ring.LandingTick - ring.LaunchTick != (state.Card == MankiMayhemCard.BreakOut ? MankiMayhemTuning.CageFlightTicks : MankiMayhemTuning.FlightTicks) - 1 || ring.FuseTicks == 0)
                throw SnapshotReader.Malformed("Mayhem landing ring");
        }
        int slamCount = r.ReadCount(20);
        if (slamCount > 4 || state.Card == MankiMayhemCard.Jump && state.HazardsLaunched > slamCount)
            throw SnapshotReader.Malformed("Mayhem slams");
        var slams = new MayhemSlam[slamCount];
        for (int i = 0; i < slamCount; i++)
        {
            slams[i] = new(r.ReadInt(), new(r.ReadFloat(), r.ReadFloat(), r.ReadFloat()), r.ReadUInt());
            if (slams[i].Ordinal != i || !slams[i].Position.IsFinite || slams[i].Position.Y != _stage.DeckTop + MankiMayhemTuning.RingHalfHeight)
                throw SnapshotReader.Malformed("Mayhem slam");
        }
        return new(state, players, rings, slams, RuntimeSolidSet.ReadSnapshot(r, ids));
    }
    public void Apply(Snapshot snapshot)
    {
        _state = snapshot.State; _players.Clear();
        foreach (var player in snapshot.Players) _players.Add(player.EntityId, player);
        _rings = (MayhemLandingRing[])snapshot.Rings.Clone();
        _slams = (MayhemSlam[])snapshot.Slams.Clone(); Solids.Apply(snapshot.Walls); _events.Clear();
    }
}
