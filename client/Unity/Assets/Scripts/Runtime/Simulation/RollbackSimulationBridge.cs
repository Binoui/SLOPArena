using System.Collections.Generic;
using SlopArena.Shared;
using SlopArena.Shared.Rollback;
using SlopArena.Client.Network;

namespace SlopArena.Client.Simulation
{
    /// <summary>
    /// ISimulationBridge for online matches: full-world rollback (ADR-0039). A transport-free
    /// <see cref="NetplaySession"/> predicts every fighter from the newest World Frame and paces
    /// local ticks with the NetplayClock; this bridge feeds it NetworkClient's controls and
    /// frames and sends the input bundles it produces. Each update also collects what the
    /// predictor's ledgers hand out: presentation events and hits to start, and those a
    /// rollback ruled out. Training keeps LocalSimulationBridge.
    /// </summary>
    public class RollbackSimulationBridge : ISimulationBridge
    {
        private readonly NetplaySession _session;
        private readonly NetworkClient _client;
        private readonly ulong _selfId;
        private readonly List<WorldFramePacket> _frames = new();
        private readonly List<NetworkClient.ControlFrame> _lastControlFrames = new();
        private readonly List<SpellResolver.HitResult> _hits = new();
        private readonly List<SpellResolver.HitResult> _retractedHits = new();
        private readonly List<TimelinePresentationEvent> _events = new();
        private readonly List<TimelinePresentationEvent> _retractedEvents = new();
        private readonly List<CourseTickEvent> _courseEvents = new();
        private readonly List<CourseTickEvent> _retractedCourseEvents = new();
        private readonly List<SlopballTickEvent> _slopballEvents = new();
        private readonly List<SlopballTickEvent> _retractedSlopballEvents = new();

        public RollbackSimulationBridge(ArenaDefinition arena, NetworkClient client, ulong selfEntityId, IMatchRule rule)
        {
            _session = new NetplaySession(arena, selfEntityId, rule);
            _client = client;
            _selfId = selfEntityId;
        }

        public MatchResultPacket? LatestMatchResult { get; private set; }
        /// <summary>Newest match state the GameHost reported; Ended once its result arrived.</summary>
        public MatchState AuthoritativeMatchState => LatestMatchResult != null ? MatchState.Ended : _session.MatchState;
        public IReadOnlyList<NetworkClient.ControlFrame> LastControlFrames => _lastControlFrames;
        public bool ClockSeeded => _session.Clock.IsSeeded;
        /// <summary>A World Frame arrived on this connection, so every fighter's state is known.</summary>
        public bool HasWorldFrame => _session.HasFrame;
        /// <summary>Ticks currently predicted beyond the newest World Frame.</summary>
        public uint PredictionTicks => _session.Predictor.PredictionTicks;
        /// <summary>The predicted world, for presentation reads (projectile visuals); never mutate it.</summary>
        public ServerSimulation Simulation => _session.Predictor.Simulation;
        /// <summary>Presentation events a rollback ruled out during the latest update.</summary>
        public IReadOnlyList<TimelinePresentationEvent> RetractedPresentationEvents => _retractedEvents;
        /// <summary>Predicted hits the GameHost ruled out during the latest update.</summary>
        public IReadOnlyList<SpellResolver.HitResult> RetractedHits => _retractedHits;
        /// <summary>Slopball events to present from the latest update, each once (ADR-0040).</summary>
        public IReadOnlyList<CourseTickEvent> CourseEvents => _courseEvents;
        public IReadOnlyList<CourseTickEvent> RetractedCourseEvents => _retractedCourseEvents;
        public IReadOnlyList<SlopballTickEvent> SlopballEvents => _slopballEvents;
        /// <summary>Predicted Slopball events a rollback ruled out during the latest update.</summary>
        public IReadOnlyList<SlopballTickEvent> RetractedSlopballEvents => _retractedSlopballEvents;

        /// <summary>A new connection must receive a World Frame before it reports Ready.</summary>
        public void ResetConnection() => _session.ResetConnection();

        /// <summary>Pacing, prediction and input-arrival health since the previous call (for the periodic log line).</summary>
        public NetplaySessionStats TakeStats(double nowSeconds) => _session.TakeStats(nowSeconds);

        public void RegisterEntity(ulong id, CharacterDefinition def, CharacterState initialState, BakedAnimationData? baked = null)
            => _session.RegisterEntity(id, def, initialState, baked);

        /// <summary>Take the controls, World Frames and match result received since the previous update.</summary>
        public void PumpNetwork()
        {
            _client.ReceiveControlPackets(_lastControlFrames);
            foreach (var frame in _lastControlFrames)
                _session.ObserveControl(frame.Packet, frame.ReceivedAtSeconds, frame.RoundTripMilliseconds);
            _client.ReceiveWorldFrames(_frames);
            _session.IngestFrames(_frames);
            foreach (var result in _client.ReceiveMatchResults())
                LatestMatchResult = result;
        }

        public void Tick(Dictionary<ulong, InputState> inputs)
            => Tick(inputs, NetworkClient.MonotonicSeconds);

        /// <summary>Predict this update's ticks, send their inputs, and collect what to present.</summary>
        public void Tick(Dictionary<ulong, InputState> inputs, double nowSeconds)
        {
            InputState input = inputs.TryGetValue(_selfId, out var value) ? value : default;
            if (_session.Update(input, nowSeconds))
                _client.SendInputs(_session.LocalTick, _session.RecentInputs);
            CollectPresentation();
        }

        /// <summary>An update that predicts nothing (not yet admitted or connected): still present what frames brought.</summary>
        public void Idle() => CollectPresentation();

        private void CollectPresentation()
        {
            _events.Clear();
            _retractedEvents.Clear();
            _session.Predictor.DrainPresentation(_events, _retractedEvents);
            _hits.Clear();
            _retractedHits.Clear();
            _session.Predictor.DrainHits(_hits, _retractedHits);
            _slopballEvents.Clear();
            _retractedSlopballEvents.Clear();
            _session.Predictor.DrainSlopballEvents(_slopballEvents, _retractedSlopballEvents);
            _courseEvents.Clear(); _retractedCourseEvents.Clear();
            _session.Predictor.DrainCourseEvents(_courseEvents, _retractedCourseEvents);
        }

        public CharacterState GetState(ulong id) => _session.Predictor.GetState(id);
        public Dictionary<ulong, CharacterState> GetAllStates() => Simulation.GetAllStates();
        public IReadOnlyList<TimelinePresentationEvent> LastTickPresentationEvents => _events;
        public SpellResolver? Resolver => _session.Predictor.Resolver;
        public IReadOnlyList<SpellResolver.HitResult> LastTickHits => _hits;
    }
}
