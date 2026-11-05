using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopArena.Shared.Mayhem;

/// <summary>What the director may do to the world. ServerSimulation implements it explicitly.</summary>
internal interface IMayhemWorld
{
    ArenaDefinition Arena { get; }
    SpellResolver Resolver { get; }
    CharacterState Fighter(ulong id);
    CharacterDefinition Definition(ulong id);
    /// <summary>Removes every host and fighter effect and pending blast; attached effects go without exploding.</summary>
    void ClearShowEffects();
    /// <summary>A fresh contestant at its start: zero percent, no ability, buffer, interaction, flight or effect.</summary>
    void PlaceAtStart(ulong id, MayhemPose start);
    /// <summary>Cancels everything the contestant runs and pins it to the anchor; it is inactive until the next Call.</summary>
    void Splat(ulong id, MayhemPose anchor);
}

public enum MayhemVerdict : byte { Pending, Passed, Failed, Withdrawn }

public sealed record MayhemContestantView(ulong EntityId, byte Seat, bool OnAir, bool Withdrawn, MayhemVerdict Verdict,
    MayhemFailure? Failure, bool ObjectiveMet, uint PassMask, ushort Ratings);

/// <summary>Everything Unity, replays and CPUs read about the show at one tick. Presentation cannot write through it.</summary>
public sealed record MayhemShowView(uint Tick, MayhemCursor Cursor, ChannelEntry? Channel, MayhemControls Controls,
    ChannelScene Scene, IReadOnlyList<MayhemContestantView> Contestants, MatchOutcome Outcome, uint Seed);

/// <summary>
/// The show director and the only writer of verdicts. It holds the roster fixed at Begin, withdrawals, one pass
/// mask per contestant, the current episode's failures, the on-air set fixed at each Call, last safe poses and
/// the current channel. The cursor is always recomputed from the seed.
/// </summary>
internal sealed class MayhemShow
{
    public const ulong HostEntityId = 1UL << 48;

    private readonly MankiMayhemDefinition _stage;
    private readonly List<MayhemEvent> _events = new();
    private readonly MayhemHazards _hazards;
    private MayhemShowPlan? _plan;
    private ulong[] _roster = Array.Empty<ulong>();
    private bool[] _withdrawn = Array.Empty<bool>();
    private uint[] _passMasks = Array.Empty<uint>();
    private MayhemFailure?[] _failures = Array.Empty<MayhemFailure?>();
    private MayhemPose[] _lastSafe = Array.Empty<MayhemPose>();
    private byte _onAir;
    private IMayhemChannel? _channel;
    private int _channelEpisode = -1;
    /// <summary>A Replay display world's recorded scene; it has no channel and is never ticked.</summary>
    private ChannelScene? _display;

    public MayhemShow(MankiMayhemDefinition stage)
    {
        _stage = stage;
        _hazards = new MayhemHazards(_events, HostEntityId, stage);
    }

    public bool Begun => _plan != null;
    public MayhemShowPlan? Plan => _plan;
    public IReadOnlyList<ulong> Roster => _roster;
    public IReadOnlyList<MayhemEvent> Events => _events;
    internal void RestoreEvents(IEnumerable<MayhemEvent> events) { _events.Clear(); _events.AddRange(events); }

    public void Begin(uint seed, IEnumerable<ulong> ids)
    {
        if (seed == 0 || _plan != null) throw new InvalidOperationException("Mayhem needs a nonzero seed and a clean Match.");
        var roster = ids.OrderBy(id => id).ToArray();
        if (roster.Length is < 2 or > 4) throw new InvalidOperationException("Mayhem needs two to four fighters.");
        _roster = roster;
        _withdrawn = new bool[roster.Length];
        _passMasks = new uint[roster.Length];
        _failures = new MayhemFailure?[roster.Length];
        _lastSafe = new MayhemPose[roster.Length];
        _plan = MayhemShowPlan.For(seed);
    }

    public int SeatOf(ulong id) => Array.IndexOf(_roster, id);
    public uint PassMask(int seat) => _passMasks[seat];
    public MayhemFailure? Failure(int seat) => _failures[seat];

    private bool OnAir(int seat) => (_onAir >> seat & 1) != 0;
    private bool IsLive(int seat) => OnAir(seat) && !_withdrawn[seat] && _failures[seat] == null;

    /// <summary>Withdrawn, or failed this episode: no input, movement, hurtbox, pushbox or targeting.</summary>
    public bool Inactive(ulong id)
    {
        int seat = SeatOf(id);
        return seat >= 0 && (_withdrawn[seat] || _failures[seat] != null);
    }

    public void Withdraw(ulong id)
    {
        int seat = SeatOf(id);
        if (seat >= 0) _withdrawn[seat] = true;
    }

    public MayhemControls ControlsFor(ulong id, uint tick)
    {
        int seat = SeatOf(id);
        if (_plan == null || seat < 0 || _channel == null || !IsLive(seat)) return MayhemControls.Frozen;
        var now = _plan.At(tick);
        return now.Phase == MayhemPhase.Play && now.Episode == _channelEpisode
            ? MayhemControls.Playing(MayhemChannels.Find(_plan.ChannelAt(now.Episode)).Extra) : MayhemControls.Frozen;
    }

    public MatchOutcome Outcome(uint tick)
    {
        if (_plan == null || tick < _plan.EndTick) return default;
        int best = -1, winners = 0; ulong winner = 0;
        for (int seat = 0; seat < _roster.Length; seat++)
        {
            if (_withdrawn[seat]) continue;
            int ratings = MayhemShowPlan.Ratings(_passMasks[seat]);
            if (ratings > best) { best = ratings; winners = 1; winner = _roster[seat]; }
            else if (ratings == best) winners++;
        }
        return winners == 0 ? new(true, 0, true) : new(true, winners == 1 ? winner : 0, winners != 1);
    }

    /// <summary>
    /// Phase edges before input admission. At the deadline it judges the state after the last Play tick and
    /// commits every pending verdict once. Returns whether gameplay runs this tick (Call and Play only).
    /// </summary>
    public bool BeginTick(uint tick, IMayhemWorld world)
    {
        _events.Clear();
        if (_plan == null) return false;
        var now = _plan.At(tick);
        var before = _plan.At(tick - 1);
        if (tick == 1) Emit(tick, MayhemEvent.ShowEpisode, MayhemEventKind.Intro, HostEntityId);
        if (now.Phase != before.Phase || now.Episode != before.Episode)
            switch (now.Phase)
            {
                case MayhemPhase.Intro:
                    break;
                case MayhemPhase.Call:
                    StartEpisode(tick, now, world);
                    break;
                case MayhemPhase.Play:
                    Emit(tick, (ushort)now.Episode, MayhemEventKind.Play, HostEntityId);
                    break;
                case MayhemPhase.Verdict:
                    if (_channel != null && _channelEpisode == now.Episode) Commit(tick, now.Episode, world);
                    world.ClearShowEffects();
                    Emit(tick, (ushort)now.Episode, MayhemEventKind.Verdict, HostEntityId);
                    break;
                case MayhemPhase.SpeedUp:
                    DropChannel();
                    Emit(tick, MayhemEvent.ShowEpisode, MayhemEventKind.SpeedUp, HostEntityId, now.Act);
                    break;
                case MayhemPhase.Ended:
                    DropChannel();
                    Emit(tick, MayhemEvent.ShowEpisode, MayhemEventKind.Ended, HostEntityId);
                    break;
            }
        if (!now.Gameplay || _channel == null || _channelEpisode != now.Episode) return now.Gameplay;
        _hazards.Bind(world.Resolver, tick, now.Episode);
        _channel.Tick(new ChannelTick(ChannelMoment.BeforeActions, now.Phase, tick, now.PlayStart, now.Deadline,
            Frame(tick, world)), _hazards);
        return true;
    }

    /// <summary>After explosions and before ordinary death processing: falls, the channel's after-combat step, then its judgement.</summary>
    public void EndTick(uint tick, IMayhemWorld world)
    {
        if (_plan == null || _channel == null) return;
        var now = _plan.At(tick);
        if (!now.Gameplay || _channelEpisode != now.Episode) return;
        bool play = now.Phase == MayhemPhase.Play;
        if (play)
            for (int seat = 0; seat < _roster.Length; seat++)
            {
                if (!IsLive(seat)) continue;
                var state = world.Fighter(_roster[seat]);
                if (Fallen(state))
                    TryFail(seat, tick, FailCause.Fell, SplatStyle.Fallen, _lastSafe[seat], world);
                else if (state.IsGrounded)
                    _lastSafe[seat] = MayhemPose.Of(state);
            }
        _hazards.Bind(world.Resolver, tick, now.Episode);
        _channel.Tick(new ChannelTick(ChannelMoment.AfterCombat, now.Phase, tick, now.PlayStart, now.Deadline,
            Frame(tick, world)), _hazards);
        if (!play) return;
        var frame = Frame(tick, world);
        for (int seat = 0; seat < _roster.Length; seat++)
        {
            if (!IsLive(seat)) continue;
            var judgement = _channel.Judge(new ContestantProbe(frame.Fighters[seat], frame, AtDeadline: false));
            if (judgement.Kind == JudgementKind.FailNow)
                TryFail(seat, tick, judgement.Cause, judgement.Style, MayhemPose.Of(frame.Fighters[seat].State), world);
        }
    }

    /// <summary>
    /// A collision fact from the engine's hit pass. Host contacts and hits on channel objects are always consumed:
    /// a hazard never damages or launches. Returns false only for a fighter-on-fighter contact.
    /// </summary>
    public bool Contact(in MayhemContact contact, uint tick, IMayhemWorld world)
    {
        if (_plan == null || _channel == null) return true;
        var now = _plan.At(tick);
        if (now.Phase != MayhemPhase.Play || _channelEpisode != now.Episode) return true;
        if (contact.Kind is MayhemContactKind.HazardBody or MayhemContactKind.HostBlast or MayhemContactKind.BombImpact)
        {
            if (!MayhemHazards.TryDecode(contact.ActivationId, out int episode, out _, out _) || episode != now.Episode
                || contact.Kind == MayhemContactKind.HazardBody && (contact.TargetSeat < 0 || !IsLive(contact.TargetSeat)))
                return true;
        }
        else
        {
            int attacker = SeatOf(contact.Attacker);
            if (attacker < 0 || !IsLive(attacker)) return true;
        }
        _hazards.Bind(world.Resolver, tick, now.Episode);
        var result = _channel.Contact(contact, _hazards);
        if (result.FailSeat >= 0)
            TryFail(result.FailSeat, tick, result.Cause, result.Style, MayhemPose.Of(world.Fighter(_roster[result.FailSeat])), world);
        return true;
    }

    public MayhemCpuIntent CpuIntent(ulong id, uint tick, IMayhemWorld world, int lookaheadTicks, int slipPercent)
    {
        int seat = SeatOf(id);
        if (_plan == null || _channel == null || seat < 0 || !IsLive(seat)) return MayhemCpuIntent.Wait;
        var now = _plan.At(tick);
        if (!now.Gameplay || now.Episode != _channelEpisode) return MayhemCpuIntent.Wait;
        var frame = Frame(tick, world);
        var controls = MayhemControls.Playing(MayhemChannels.Find(_plan.ChannelAt(now.Episode)).Extra);
        return _channel.Cpu(new CpuProbe(frame.Fighters[seat], frame, controls, now.PlayStart, now.Deadline, lookaheadTicks,
            MayhemCpu.Slips(MayhemMath.ChannelSeed(_plan.Seed, now.Episode, 0), (byte)seat, slipPercent)));
    }

    public MayhemShowView? View(uint tick, IMayhemWorld world)
    {
        if (_plan == null) return null;
        var cursor = _plan.At(tick);
        bool current = (_channel != null || _display != null) && cursor.HasEpisode && _channelEpisode == cursor.Episode;
        var entry = current ? MayhemChannels.Find(_plan.ChannelAt(cursor.Episode)) : null;
        var frame = current && _channel != null ? Frame(tick, world) : null;
        var contestants = new MayhemContestantView[_roster.Length];
        for (int seat = 0; seat < _roster.Length; seat++)
        {
            bool passed = cursor.HasEpisode && (_passMasks[seat] >> cursor.Episode & 1) != 0;
            var verdict = _withdrawn[seat] ? MayhemVerdict.Withdrawn : _failures[seat] != null ? MayhemVerdict.Failed
                : passed ? MayhemVerdict.Passed : MayhemVerdict.Pending;
            bool met = frame != null && cursor.Phase == MayhemPhase.Play && IsLive(seat)
                && _channel!.Judge(new ContestantProbe(frame!.Fighters[seat], frame, AtDeadline: false)).Kind == JudgementKind.Ready;
            contestants[seat] = new(_roster[seat], (byte)seat, OnAir(seat), _withdrawn[seat], verdict, _failures[seat], met,
                _passMasks[seat], MayhemShowPlan.Ratings(_passMasks[seat]));
        }
        var controls = cursor.Phase == MayhemPhase.Play && entry != null ? MayhemControls.Playing(entry.Extra) : MayhemControls.Frozen;
        return new MayhemShowView(tick, cursor, entry, controls, !current ? ChannelScene.Empty : _channel?.Observe() ?? _display!,
            contestants, Outcome(tick), _plan.Seed);
    }

    /// <summary>The current channel's scene for collision: solids that block and targets that take hits.</summary>
    public ChannelScene Scene => _channel?.Observe() ?? ChannelScene.Empty;

    private void StartEpisode(uint tick, MayhemCursor now, IMayhemWorld world)
    {
        DropChannel();
        _onAir = 0;
        for (int seat = 0; seat < _roster.Length; seat++)
        {
            _failures[seat] = null;
            if (!_withdrawn[seat]) _onAir |= (byte)(1 << seat);
        }
        world.ClearShowEffects();
        var seats = new List<ChannelSeat>(_roster.Length);
        for (int seat = 0; seat < _roster.Length; seat++)
        {
            if (!OnAir(seat)) continue;
            ulong id = _roster[seat];
            var definition = world.Definition(id);
            var spawn = MatchFormat.MankiMayhem.StartFor(world.Arena, seat, _roster.Length);
            var start = new MayhemPose(spawn.X, _stage.DeckTop + definition.CapsuleHeight * .5f, spawn.Z, spawn.Yaw);
            world.PlaceAtStart(id, start);
            _lastSafe[seat] = start;
            seats.Add(new ChannelSeat((byte)seat, id, start, definition.CapsuleRadius, definition.CapsuleHeight));
        }
        var entry = MayhemChannels.Find(_plan!.ChannelAt(now.Episode));
        _channel = entry.Create(new ChannelSetup(MayhemMath.ChannelSeed(_plan.Seed, now.Episode, 0), now.Episode, 0,
            now.Tier, now.PhaseStart, now.PlayStart, now.Deadline, _stage, seats));
        _channelEpisode = now.Episode;
        Emit(tick, (ushort)now.Episode, MayhemEventKind.Call, HostEntityId);
    }

    private void Commit(uint tick, int episode, IMayhemWorld world)
    {
        var frame = Frame(tick, world);
        for (int seat = 0; seat < _roster.Length; seat++)
        {
            if (!IsLive(seat)) continue;
            var judgement = _channel!.Judge(new ContestantProbe(frame.Fighters[seat], frame, AtDeadline: true));
            switch (judgement.Kind)
            {
                case JudgementKind.Ready:
                    _passMasks[seat] |= 1u << episode;
                    Emit(tick, (ushort)episode, MayhemEventKind.Pass, _roster[seat], MayhemShowPlan.Award(episode));
                    break;
                case JudgementKind.Incomplete:
                case JudgementKind.FailNow:
                    TryFail(seat, tick, judgement.Cause, judgement.Style, MayhemPose.Of(frame.Fighters[seat].State), world);
                    break;
            }
        }
    }

    /// <summary>The only transition out of Pending into Failed. A second failure changes nothing and emits nothing.</summary>
    private bool TryFail(int seat, uint tick, FailCause cause, SplatStyle style, MayhemPose anchor, IMayhemWorld world)
    {
        if (!IsLive(seat)) return false;
        var deck = MayhemMath.Clamp(_stage, anchor.X, anchor.Z, 0f);
        anchor = new MayhemPose(deck.X, _stage.DeckTop + world.Definition(_roster[seat]).CapsuleHeight * .5f, deck.Z, anchor.Yaw);
        _failures[seat] = new MayhemFailure(cause, style, tick, anchor);
        Emit(tick, (ushort)_channelEpisode, MayhemEventKind.Fail, _roster[seat], MayhemEvent.FailValue(cause, style));
        world.Splat(_roster[seat], anchor);
        return true;
    }

    private bool Fallen(in CharacterState state)
        => !MayhemMath.OnDeck(_stage, state.PX, state.PZ) || state.PY < _stage.DeckTop - MayhemShowTuning.FallDepth;

    private void DropChannel() { _channel = null; _channelEpisode = -1; _display = null; }

    private MayhemFrame Frame(uint tick, IMayhemWorld world)
    {
        var fighters = new MayhemFighterFact[_roster.Length];
        for (int seat = 0; seat < _roster.Length; seat++)
            fighters[seat] = new((byte)seat, _roster[seat], world.Fighter(_roster[seat]), world.Definition(_roster[seat]),
                OnAir(seat), IsLive(seat));
        return new MayhemFrame(tick, _stage, fighters);
    }

    private void Emit(uint tick, ushort episode, MayhemEventKind kind, ulong subject, ushort value = 0)
        => _events.Add(new MayhemEvent(tick, episode, 0, 0, kind, subject, value));

    // ── World Snapshot ──────────────────────────────────────────────────────────────────────────────

    internal sealed record Snapshot(uint Seed, ulong[] Roster, bool[] Withdrawn, byte OnAir, uint[] PassMasks,
        MayhemFailure?[] Failures, MayhemPose[] LastSafe, int ChannelEpisode, IMayhemChannel? Channel)
    {
        /// <summary>Set only by the Replay View decoder: the recorded scene of a display world.</summary>
        internal ChannelScene? Display { get; init; }
    }

    public void WriteSnapshot(SnapshotWriter w)
    {
        w.WriteUInt(_plan?.Seed ?? 0);
        if (_plan == null) return;
        w.WriteByte((byte)_roster.Length);
        byte withdrawn = 0;
        for (int seat = 0; seat < _roster.Length; seat++)
        {
            w.WriteULong(_roster[seat]);
            if (_withdrawn[seat]) withdrawn |= (byte)(1 << seat);
        }
        w.WriteByte(withdrawn);
        w.WriteByte(_onAir);
        for (int seat = 0; seat < _roster.Length; seat++)
        {
            w.WriteUInt(_passMasks[seat]);
            w.WriteBool(_failures[seat].HasValue);
            if (_failures[seat] is { } failure)
            {
                w.WriteByte((byte)failure.Cause); w.WriteByte((byte)failure.Style); w.WriteUInt(failure.Tick);
                WritePose(w, failure.Anchor);
            }
            WritePose(w, _lastSafe[seat]);
        }
        w.WriteUShort(_channel == null ? (ushort)0 : _plan.ChannelAt(_channelEpisode).Value);
        if (_channel == null) return;
        w.WriteByte((byte)_channelEpisode);
        _channel.WriteSnapshot(w);
    }

    internal static void WritePose(SnapshotWriter w, MayhemPose pose)
    { w.WriteFloat(pose.X); w.WriteFloat(pose.Y); w.WriteFloat(pose.Z); w.WriteFloat(pose.Yaw); }

    internal static MayhemPose ReadPose(SnapshotReader r, string what)
    {
        var pose = new MayhemPose(r.ReadFloat(), r.ReadFloat(), r.ReadFloat(), r.ReadFloat());
        return pose.IsFinite ? pose : throw SnapshotReader.Malformed(what);
    }

    /// <summary>Validates the whole section against the cursor at <paramref name="tick"/> before anything is applied.</summary>
    public Snapshot ReadSnapshot(SnapshotReader r, IReadOnlyList<ulong> ids, uint tick)
    {
        uint seed = r.ReadUInt();
        if (seed == 0)
            return new Snapshot(0, Array.Empty<ulong>(), Array.Empty<bool>(), 0, Array.Empty<uint>(),
                Array.Empty<MayhemFailure?>(), Array.Empty<MayhemPose>(), -1, null);
        var plan = MayhemShowPlan.For(seed);
        int count = r.ReadByte();
        if (count is < 2 or > 4 || count != ids.Count) throw SnapshotReader.Malformed("Mayhem roster");
        var roster = new ulong[count];
        for (int seat = 0; seat < count; seat++)
        {
            roster[seat] = r.ReadULong();
            if (roster[seat] != ids[seat] || seat > 0 && roster[seat] <= roster[seat - 1]) throw SnapshotReader.Malformed("Mayhem roster");
        }
        int all = (1 << count) - 1;
        byte withdrawnMask = r.ReadByte(), onAir = r.ReadByte();
        if ((withdrawnMask & ~all) != 0 || (onAir & ~all) != 0) throw SnapshotReader.Malformed("Mayhem seat masks");
        var cursor = plan.At(tick);
        int committed = cursor.Phase switch
        {
            MayhemPhase.Intro => 0,
            MayhemPhase.Call or MayhemPhase.Play => cursor.Episode,
            MayhemPhase.Verdict => cursor.Episode + 1,
            MayhemPhase.SpeedUp => cursor.Act * MayhemShowTuning.EpisodesPerAct,
            MayhemPhase.Ended => MayhemShowTuning.Episodes,
        };
        bool failuresAllowed = cursor.Phase is MayhemPhase.Play or MayhemPhase.Verdict or MayhemPhase.SpeedUp or MayhemPhase.Ended;
        var withdrawn = new bool[count];
        var passMasks = new uint[count];
        var failures = new MayhemFailure?[count];
        var lastSafe = new MayhemPose[count];
        for (int seat = 0; seat < count; seat++)
        {
            withdrawn[seat] = (withdrawnMask >> seat & 1) != 0;
            passMasks[seat] = r.ReadUInt();
            if ((passMasks[seat] & ~((1u << committed) - 1)) != 0) throw SnapshotReader.Malformed("Mayhem pass mask");
            if (r.ReadBool())
            {
                var cause = (FailCause)r.ReadByte();
                var style = (SplatStyle)r.ReadByte();
                uint failTick = r.ReadUInt();
                var anchor = ReadPose(r, "Mayhem splat anchor");
                bool lastPassed = cursor.Phase == MayhemPhase.Verdict && (passMasks[seat] >> cursor.Episode & 1) != 0;
                if (!failuresAllowed || cause > FailCause.BoardsRemain || style > SplatStyle.Fallen || failTick > tick
                    || (onAir >> seat & 1) == 0 || lastPassed)
                    throw SnapshotReader.Malformed("Mayhem failure");
                failures[seat] = new MayhemFailure(cause, style, failTick, anchor);
            }
            lastSafe[seat] = ReadPose(r, "Mayhem safe pose");
        }
        var channelId = new ChannelId(r.ReadUShort());
        bool channelExpected = cursor.Phase is MayhemPhase.Call or MayhemPhase.Play or MayhemPhase.Verdict;
        IMayhemChannel? channel = null;
        int channelEpisode = -1;
        if (channelId != ChannelId.None)
        {
            channelEpisode = r.ReadByte();
            if (!channelExpected || channelEpisode != cursor.Episode || plan.ChannelAt(channelEpisode) != channelId
                || !MayhemChannels.TryFind(channelId, out var entry))
                throw SnapshotReader.Malformed("Mayhem channel");
            channel = entry.Read(r, new ChannelRestore(channelId, channelEpisode, 0, _stage, roster,
                plan.PlayStart(channelEpisode), plan.Deadline(channelEpisode), onAir));
        }
        else if (channelExpected)
            throw SnapshotReader.Malformed("Mayhem channel");
        return new Snapshot(seed, roster, withdrawn, onAir, passMasks, failures, lastSafe, channelEpisode, channel);
    }

    public void Apply(Snapshot snapshot)
    {
        _plan = snapshot.Seed == 0 ? null : MayhemShowPlan.For(snapshot.Seed);
        _roster = (ulong[])snapshot.Roster.Clone();
        _withdrawn = (bool[])snapshot.Withdrawn.Clone();
        _onAir = snapshot.OnAir;
        _passMasks = (uint[])snapshot.PassMasks.Clone();
        _failures = (MayhemFailure?[])snapshot.Failures.Clone();
        _lastSafe = (MayhemPose[])snapshot.LastSafe.Clone();
        _channel = snapshot.Channel;
        _channelEpisode = snapshot.ChannelEpisode;
        _display = snapshot.Channel == null ? snapshot.Display : null;
        _events.Clear();
    }
}
