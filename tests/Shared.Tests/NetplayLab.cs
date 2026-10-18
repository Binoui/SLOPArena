using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Shared.Rollback;

namespace SlopArena.Shared.Tests;

/// <summary>
/// An in-process online match for full-world rollback (ADR-0039, plan phase 3): a GameHost loop
/// (ServerSimulation, a PeerInputTimeline per player, one WorldFramePacket per tick) and a
/// NetplayPredictor per player, joined by simulated links with delay, jitter, loss, stalls and
/// outages. Everything crosses the real codecs: InputBundle up, WorldFramePacket down. One
/// <see cref="Step"/> is one GameHost tick; each player then advances its prediction to
/// <see cref="Player.LeadTicks"/> ahead of the GameHost, as the NetplayClock paces a client,
/// with at most four catch-up ticks per update. Deterministic for a seed.
/// </summary>
internal sealed class NetplayLab
{
    public const uint DefaultLeadTicks = 3;
    private static readonly Dictionary<CharacterClass, MatchContentEntry> Content = new();

    private readonly Random _rng;
    private readonly List<Player> _players = new();
    private readonly Dictionary<ulong, InputState> _consumed = new();
    private readonly WorldFrameHistory _history = new();
    private readonly List<byte[]> _packets = new();
    private readonly InputState[] _bundle = new InputState[InputBundle.MaxInputs];
    private bool _started;

    public NetplayLab(string arenaName, CharacterClass[] roster, int seed,
        Func<ulong, CharacterState, CharacterState>? placement = null)
    {
        _rng = new Random(seed);
        var arena = WorldSnapshotTests.LoadArena(arenaName);
        Server = WorldSnapshotTests.NewMatch(arena, roster, reverseRegistration: false);
        for (int slot = 0; slot < roster.Length; slot++)
        {
            ulong id = (ulong)(slot + 1);
            var state = Server.GetState(id);
            if (placement != null)
            {
                state = placement(id, state);
                Server.SetState(id, state);
            }
            _players.Add(new Player(id, new NetplayPredictor(arena, id, new StockMatchRule(250))));
        }
        // Every client registers every fighter itself first, then the others in reverse.
        foreach (var player in _players)
            foreach (ulong id in Ids.OrderBy(x => x == player.Id ? 0 : 1).ThenByDescending(x => x))
            {
                var content = ContentFor(roster[(int)id - 1]);
                player.Predictor.RegisterEntity(id, content.Definition, Server.GetState(id), content.BakedAnimation);
            }
    }

    public ServerSimulation Server { get; }
    /// <summary>Last GameHost tick.</summary>
    public uint Tick { get; private set; }
    public IReadOnlyList<Player> Players => _players;
    public IEnumerable<ulong> Ids => _players.Select(p => p.Id);
    public Player this[ulong id] => _players[(int)id - 1];

    /// <summary>GameHost records: states after each tick, every event, every hit, input sources.</summary>
    public Dictionary<uint, Dictionary<ulong, CharacterState>> ServerStates { get; } = new();
    /// <summary>Active resolver hitboxes (attacks and projectiles) after each GameHost tick.</summary>
    public Dictionary<uint, int> ServerHitboxes { get; } = new();
    public List<TimelinePresentationEvent> ServerEvents { get; } = new();
    public List<SpellResolver.HitResult> ServerHits { get; } = new();
    public Dictionary<ulong, int[]> ServerSources { get; } = new();
    public int ServerRescued { get; private set; }

    public void Run(int ticks)
    {
        for (int i = 0; i < ticks; i++)
            Step();
    }

    public void Step()
    {
        if (Tick == 0 && !_started)
        {
            // The countdown: clients already predict and send their first inputs.
            _started = true;
            foreach (var player in _players)
                StepPlayer(player, 0);
        }
        uint now = Tick + 1;
        StepGameHost(now);
        foreach (var player in _players)
            StepPlayer(player, now);
    }

    /// <summary>Neutral inputs on perfect links until every prediction should be exact again.</summary>
    public void Quiesce(int ticks)
    {
        foreach (var player in _players)
        {
            player.Input = (_, _) => default;
            player.Up.Reset();
            player.Down.Reset();
        }
        Run(ticks);
    }

    private void StepGameHost(uint now)
    {
        foreach (var player in _players)
        {
            _packets.Clear();
            player.Up.Receive(now, _packets);
            foreach (var packet in _packets)
            {
                if (!InputBundle.TryRead(packet, out ulong entityId, out uint newestTick, _bundle, out int count) ||
                    entityId != player.Id)
                    throw new InvalidOperationException("Lab uplink produced an invalid bundle.");
                var late = player.ServerInputs.Accept(Tick, newestTick, _bundle.AsSpan(0, count), NetplayClock.HistoryTicks);
                ServerRescued += late.RescuedPresses;
            }
        }

        _consumed.Clear();
        foreach (var player in _players)
        {
            if (player.ServerSeesDisconnected || Server.GetState(player.Id).Deaths >= 250)
            {
                player.ServerInputs.Reset();
                continue;
            }
            var consumed = player.ServerInputs.Consume(now);
            if (!ServerSources.TryGetValue(player.Id, out var sources))
                ServerSources[player.Id] = sources = new int[3];
            sources[(int)consumed.Source]++;
            _consumed[player.Id] = consumed.Input;
        }
        var relayed = Ids.Select(id => _consumed.TryGetValue(id, out var input)
            ? new RelayedInput(id, true, input) : new RelayedInput(id, false, default)).ToArray();

        Server.SetTick(Tick);
        Tick = now;
        Server.Tick(new Dictionary<ulong, InputState>(_consumed));
        var events = Server.GetPresentationEvents(clear: true);
        ServerEvents.AddRange(events);
        ServerHits.AddRange(Server.LastTickHits);
        ServerStates[now] = new Dictionary<ulong, CharacterState>(Server.GetAllStates());
        ServerHitboxes[now] = Server.Resolver.GetActiveHitboxes().Count;
        _history.Record(now, events, Server.LastTickHits);
        var frame = new WorldFramePacket(MatchState.Playing, Server.CaptureSnapshot(), relayed,
            _history.EventsForFrame(now).ToArray(), _history.HitsForFrame(now).ToArray()).Serialize();
        foreach (var player in _players)
            player.Down.Send(now, frame, _rng);
    }

    private void StepPlayer(Player player, uint now)
    {
        _packets.Clear();
        player.Down.Receive(now, _packets);
        var frames = new List<WorldFramePacket>(_packets.Count);
        foreach (var packet in _packets)
        {
            var frame = WorldFramePacket.Deserialize(packet);
            for (uint tick = frame.EventsFromTick; tick <= frame.Tick; tick++)
                player.CoveredEventTicks.Add(tick);
            frames.Add(frame);
        }
        if (frames.Count > 0)
            player.Predictor.IngestFrames(frames);

        uint target = now + player.LeadTicks;
        bool advanced = false;
        for (int step = 0; step < NetplayClock.MaximumCatchUpSteps && player.Predictor.LocalTick < target; step++)
        {
            uint tick = player.Predictor.LocalTick + 1;
            if (!player.Predictor.Advance(player.Input(player, tick)))
                break;
            player.Predictor.TryGetRecordedInput(tick, out var recorded);
            player.Recent.Add(recorded);
            if (player.Recent.Count > InputBundle.MaxInputs)
                player.Recent.RemoveAt(0);
            advanced = true;
        }
        if (advanced)
        {
            var buffer = new byte[InputBundle.SizeFor(player.Recent.Count)];
            InputBundle.Write(buffer, player.Id, player.Predictor.LocalTick, player.Recent.ToArray());
            player.Up.Send(now, buffer, _rng);
        }

        int startedBefore = player.Started.Count, retractedBefore = player.Retracted.Count;
        player.Predictor.DrainPresentation(player.Started, player.Retracted);
        for (int i = startedBefore; i < player.Started.Count; i++)
            player.EffectLog.Add((now, true, player.Started[i]));
        for (int i = retractedBefore; i < player.Retracted.Count; i++)
            player.EffectLog.Add((now, false, player.Retracted[i]));
        int hitsBefore = player.Hits.Count, hitsRetractedBefore = player.RetractedHits.Count;
        player.Predictor.DrainHits(player.Hits, player.RetractedHits);
        for (int i = hitsBefore; i < player.Hits.Count; i++)
            player.HitLog.Add((now, true, player.Hits[i]));
        for (int i = hitsRetractedBefore; i < player.RetractedHits.Count; i++)
            player.HitLog.Add((now, false, player.RetractedHits[i]));
        var window = player.Predictor.TakeStats();
        player.Windows.Add((now, window));
        player.Accumulate(window);
        uint local = player.Predictor.LocalTick;
        if (!player.Presented.ContainsKey(local))
        {
            player.Presented[local] = new Dictionary<ulong, CharacterState>(player.Predictor.GetAllStates());
            player.PresentedHitboxes[local] = player.Predictor.Resolver.GetActiveHitboxes().Count;
        }
    }

    private static MatchContentEntry ContentFor(CharacterClass character)
    {
        lock (Content)
        {
            if (!Content.TryGetValue(character, out var content))
                Content[character] = content = BuiltInContentResolver.Resolve(character);
            return content;
        }
    }

    internal sealed class Player
    {
        public Player(ulong id, NetplayPredictor predictor)
        {
            Id = id;
            Predictor = predictor;
        }

        public ulong Id { get; }
        public NetplayPredictor Predictor { get; }
        public Link Up { get; } = new();
        public Link Down { get; } = new();
        public uint LeadTicks { get; set; } = DefaultLeadTicks;
        /// <summary>The player's input for a local tick (they see <see cref="Predictor"/>'s states).</summary>
        public Func<Player, uint, InputState> Input { get; set; } = (_, _) => default;
        /// <summary>The GameHost has noticed the disconnect: it consumes and relays no input.</summary>
        public bool ServerSeesDisconnected { get; set; }
        public PeerInputTimeline ServerInputs { get; } = new();
        public List<InputState> Recent { get; } = new();
        public List<TimelinePresentationEvent> Started { get; } = new();
        public List<TimelinePresentationEvent> Retracted { get; } = new();
        public List<SpellResolver.HitResult> Hits { get; } = new();
        public List<SpellResolver.HitResult> RetractedHits { get; } = new();
        /// <summary>Hits handed out for feedback (true) and ruled out (false), in order.</summary>
        public List<(uint Wall, bool Start, SpellResolver.HitResult Hit)> HitLog { get; } = new();
        public HashSet<uint> CoveredEventTicks { get; } = new();
        /// <summary>States as first presented at each local tick.</summary>
        public Dictionary<uint, Dictionary<ulong, CharacterState>> Presented { get; } = new();
        public Dictionary<uint, int> PresentedHitboxes { get; } = new();
        /// <summary>Effects started (true) and stopped (false), in the order they were handed out.</summary>
        public List<(uint Wall, bool Start, TimelinePresentationEvent Event)> EffectLog { get; } = new();
        public NetplayPredictorStats Totals;
        /// <summary>Predictor stats of each GameHost tick's update.</summary>
        public List<(uint Wall, NetplayPredictorStats Stats)> Windows { get; } = new();

        public CharacterState State(ulong id) => Predictor.GetState(id);

        public void Accumulate(in NetplayPredictorStats w)
        {
            Totals.Frames += w.Frames;
            Totals.StaleFrames += w.StaleFrames;
            Totals.RejectedFrames += w.RejectedFrames;
            Totals.Anchors += w.Anchors;
            Totals.ExactAnchors += w.ExactAnchors;
            Totals.Corrections += w.Corrections;
            Totals.Mispredictions += w.Mispredictions;
            Totals.Jumps += w.Jumps;
            Totals.StarvedTicks += w.StarvedTicks;
            Totals.SelfInputMismatches += w.SelfInputMismatches;
            Totals.EffectsStarted += w.EffectsStarted;
            Totals.EffectsRetracted += w.EffectsRetracted;
            Totals.HitsStarted += w.HitsStarted;
            Totals.HitsRetracted += w.HitsRetracted;
            Totals.PredictionTicks = w.PredictionTicks;
            Totals.MaxAnchorAgeTicks = Math.Max(Totals.MaxAnchorAgeTicks, w.MaxAnchorAgeTicks);
            Totals.ResimulatedTicks += w.ResimulatedTicks;
            Totals.MaxResimulatedTicks = Math.Max(Totals.MaxResimulatedTicks, w.MaxResimulatedTicks);
            Totals.TotalResimulationMs += w.TotalResimulationMs;
            Totals.MaxResimulationMs = Math.Max(Totals.MaxResimulationMs, w.MaxResimulationMs);
            Totals.MaxSelfCorrectionMeters = Math.Max(Totals.MaxSelfCorrectionMeters, w.MaxSelfCorrectionMeters);
            Totals.MaxOpponentCorrectionMeters = Math.Max(Totals.MaxOpponentCorrectionMeters, w.MaxOpponentCorrectionMeters);
            Totals.MaxSelfSnapMeters = Math.Max(Totals.MaxSelfSnapMeters, w.MaxSelfSnapMeters);
            Totals.MaxOpponentSnapMeters = Math.Max(Totals.MaxOpponentSnapMeters, w.MaxOpponentSnapMeters);
        }

        public string Describe()
        {
            var t = Totals;
            return $"player {Id}: frames={t.Frames} stale={t.StaleFrames} anchors={t.Anchors} exact={t.ExactAnchors} " +
                $"corrections={t.Corrections} mispredicts={t.Mispredictions} jumps={t.Jumps} starved={t.StarvedTicks} " +
                $"selfInputMismatch={t.SelfInputMismatches} maxAge={t.MaxAnchorAgeTicks} resim={t.ResimulatedTicks} " +
                $"maxResim={t.MaxResimulatedTicks} resimMs={t.TotalResimulationMs:F1} maxResimMs={t.MaxResimulationMs:F3} " +
                $"selfCorr={t.MaxSelfCorrectionMeters:F3}m oppCorr={t.MaxOpponentCorrectionMeters:F3}m " +
                $"selfSnap={t.MaxSelfSnapMeters:F3}m oppSnap={t.MaxOpponentSnapMeters:F3}m " +
                $"effects +{Started.Count} -{Retracted.Count} hits +{Hits.Count} -{RetractedHits.Count} " +
                $"up={Up.Describe()} down={Down.Describe()}";
        }
    }

    /// <summary>One direction of one player's connection, in GameHost ticks.</summary>
    internal sealed class Link
    {
        private readonly List<(uint At, long Order, byte[] Payload)> _inFlight = new();
        private long _order;

        public int DelayTicks { get; set; } = 1;
        public int JitterTicks { get; set; }
        public double Loss { get; set; }
        /// <summary>Packets sent while cut are lost.</summary>
        public bool Cut { get; set; }
        /// <summary>Packets sent in [StallFrom, StallUntil) arrive together at StallUntil.</summary>
        public uint StallFrom { get; set; }
        public uint StallUntil { get; set; }
        public int Sent { get; private set; }
        public int Lost { get; private set; }

        public void Reset()
        {
            DelayTicks = 1;
            JitterTicks = 0;
            Loss = 0;
            Cut = false;
            StallFrom = StallUntil = 0;
        }

        public void Send(uint now, byte[] payload, Random rng)
        {
            Sent++;
            double roll = rng.NextDouble();
            int jitter = JitterTicks > 0 ? rng.Next(JitterTicks + 1) : 0;
            if (Cut || roll < Loss)
            {
                Lost++;
                return;
            }
            uint at = now + (uint)DelayTicks + (uint)jitter;
            if (now >= StallFrom && now < StallUntil && at < StallUntil)
                at = StallUntil;
            _inFlight.Add((at, _order++, payload));
        }

        public void Receive(uint now, List<byte[]> into)
        {
            var due = _inFlight.Where(p => p.At <= now).OrderBy(p => p.At).ThenBy(p => p.Order).ToList();
            foreach (var packet in due)
                into.Add(packet.Payload);
            _inFlight.RemoveAll(p => p.At <= now);
        }

        public string Describe() => $"{Sent - Lost}/{Sent}";
    }
}
