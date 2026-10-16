using System;
using System.Collections.Generic;
using SlopArena.Shared;
using SlopArena.Shared.Rollback;
using SlopArena.Client.Network;

namespace SlopArena.Client.Simulation
{
    /// <summary>
    /// ISimulationBridge backed by RollbackSimulator (ADR-0011): the self entity predicts
    /// continuously (LocalTrack); opponents predict while in a Predictable ActionState
    /// (PredictedTrack) and render raw from the server otherwise (RawTrack). Replaces
    /// NetworkSimulationBridge for PvPMatch — Training keeps LocalSimulationBridge.
    ///
    /// Local prediction advances one tick per fixed update (paced toward the clock's
    /// frontier) and every simulated tick's input is sent, repeated in the next datagrams.
    /// </summary>
    public class RollbackSimulationBridge : ISimulationBridge
    {
        private readonly RollbackSimulator _core;
        private readonly NetworkClient _client;
        private readonly ulong _selfId;
        private readonly HashSet<ulong> _receivedInitialStates = new();
        private uint _tick;
        private bool _timelineSeeded;
        private InputState _bufferedInput;
        private InputState _lastAppliedInput;
        private bool _hasBufferedInput;
        private readonly NetplayClock _clock = new();
        private readonly InputState[] _recentInputs = new InputState[InputBundle.MaxInputs];
        private int _recentCount;
        private readonly List<NetworkClient.ControlFrame> _lastControlFrames = new();
        private readonly List<SpellResolver.HitResult> _lastTickHits = new();
        private readonly List<TimelinePresentationEvent> _lastTickPresentationEvents = new();
        private int _windowUpdates, _windowSkipped, _windowExtra;
        public MatchResultPacket? LatestMatchResult { get; private set; }
        public MatchState AuthoritativeMatchState { get; private set; } = MatchState.Waiting;
        public IReadOnlyList<NetworkClient.ControlFrame> LastControlFrames => _lastControlFrames;
        public bool ClockSeeded => _clock.IsSeeded;


        public RollbackSimulationBridge(ArenaDefinition arena, NetworkClient client, ulong selfEntityId, IMatchRule? rule = null)
        {
            _core = new RollbackSimulator(arena, selfEntityId, rule);
            _client = client;
            _selfId = selfEntityId;
        }

        /// <summary>Debug overlay data (Task 10) — not part of ISimulationBridge.</summary>
        public int CorrectionCount => _core.CorrectionCount;
        public uint LastFrontierTicks => _core.LastFrontierTicks;

        /// <summary>Pacing and transport health since the previous call (for the periodic log line).</summary>
        public NetplayStats TakeStats(double nowSeconds)
        {
            var stats = new NetplayStats(_windowUpdates, _windowSkipped, _windowExtra,
                _clock.LeadTicks, _clock.LeadBonus,
                _clock.IsSeeded ? (long)_tick - _clock.EstimateTick(nowSeconds) : 0,
                _core.RawPlayoutDelay, _core.RawPlayoutStarvations, _core.CorrectionCount);
            _windowUpdates = _windowSkipped = _windowExtra = 0;
            return stats;
        }

        public void RegisterEntity(ulong id, CharacterDefinition def, CharacterState initialState, BakedAnimationData? baked = null)
            => _core.RegisterEntity(id, def, initialState, baked);
        public bool HasInitialState(ulong entityId) => _receivedInitialStates.Contains(entityId);
        public void ResetInitialStates() => _receivedInitialStates.Clear();

        public void PumpNetwork()
        {
            _client.ReceiveControlPackets(_lastControlFrames);
            foreach (var frame in _lastControlFrames)
            {
                var control = frame.Packet;
                if (control.EntityId != _selfId || control.Kind != NetplayControlKind.Clock)
                    continue;
                _clock.ObserveInputTiming(control.InputSlack, control.LateInputs, frame.ReceivedAtSeconds);
                if (!_clock.Observe(control.Tick, control.StartTick, frame.ReceivedAtSeconds, frame.RoundTripMilliseconds) ||
                    _timelineSeeded || control.StartTick == 0)
                    continue;

                uint baselineTick = control.Tick < control.StartTick
                    ? control.StartTick - 1
                    : control.Tick;
                if (_core.SetTimeline(baselineTick))
                {
                    _tick = baselineTick;
                    _timelineSeeded = true;
                    _clock.ResetPacing();
                }
            }

            var packets = _client.ReceiveEntityPackets();
            foreach (var entityPacket in packets)
            {
                _receivedInitialStates.Add(entityPacket.EntityId);
                if ((byte)entityPacket.State.MatchState > (byte)AuthoritativeMatchState)
                    AuthoritativeMatchState = entityPacket.State.MatchState;
            }
            _core.IngestAuthoritativeBatch(packets);
            foreach (var result in _client.ReceiveMatchResults())
                LatestMatchResult = result;
            if (LatestMatchResult != null)
                AuthoritativeMatchState = MatchState.Ended;

            _core.IngestPresentationEvents(_client.ReceivePresentationEvents());
            _lastTickPresentationEvents.Clear();
            _lastTickPresentationEvents.AddRange(_core.DrainPresentationEvents());
        }

        public void Tick(Dictionary<ulong, InputState> inputs)
            => Tick(inputs, NetworkClient.MonotonicSeconds);

        public void Tick(Dictionary<ulong, InputState> inputs, double nowSeconds)
        {
            _lastTickHits.Clear();
            if (!_timelineSeeded || !_clock.IsSeeded ||
                AuthoritativeMatchState != MatchState.Playing ||
                _clock.EstimateTick(nowSeconds) < _clock.StartTick)
                return;

            InputState latest = inputs.TryGetValue(_selfId, out var value) ? value : default;
            if (_hasBufferedInput)
                latest = MergeBufferedEdges(_bufferedInput, latest);
            _bufferedInput = latest;
            _hasBufferedInput = true;

            uint steps = _clock.StepsForUpdate(_tick, nowSeconds);
            _windowUpdates++;
            if (steps == 0) _windowSkipped++;
            else if (steps > 1) _windowExtra++;
            for (uint i = 0; i < steps; i++)
            {
                // Catch-up ticks hold the previous controls; the newest tick carries any presses.
                bool newest = i == steps - 1;
                InputState input = newest ? _bufferedInput : _lastAppliedInput.WithoutEdges();
                inputs[_selfId] = input;
                _core.Tick(inputs);
                _tick++;
                _lastTickHits.AddRange(_core.LastTickHits);
                RememberSent(input);

                if (newest)
                {
                    _lastAppliedInput = input.WithoutEdges();
                    _bufferedInput = default;
                    _hasBufferedInput = false;
                }
            }
            if (steps > 0)
                _client.SendInputs(_tick, _recentInputs.AsSpan(0, _recentCount));
        }

        private void RememberSent(InputState input)
        {
            if (_recentCount == _recentInputs.Length)
            {
                Array.Copy(_recentInputs, 1, _recentInputs, 0, _recentInputs.Length - 1);
                _recentCount--;
            }
            _recentInputs[_recentCount++] = input;
        }

        private static InputState MergeBufferedEdges(InputState previous, InputState latest)
        {
            latest.Jump |= previous.Jump;
            latest.DownPressed |= previous.DownPressed;
            latest.ShieldPressed |= previous.ShieldPressed;
            latest.GrabPressed |= previous.GrabPressed;
            latest.FaceToCamera |= previous.FaceToCamera;
            latest.ToggleLock |= previous.ToggleLock;
            latest.RetargetPressed |= previous.RetargetPressed;
            if (latest.ActiveSlot == 0) latest.ActiveSlot = previous.ActiveSlot;
            return latest;
        }

        public CharacterState GetState(ulong id) => _core.GetState(id);
        public Dictionary<ulong, CharacterState> GetAllStates() => _core.GetAllStates();
        public IReadOnlyList<TimelinePresentationEvent> LastTickPresentationEvents => _lastTickPresentationEvents;
        public SpellResolver? Resolver => _core.Resolver;
        public IReadOnlyList<SpellResolver.HitResult> LastTickHits => _lastTickHits;
    }

    /// <summary>One window of online pacing and transport health.</summary>
    public readonly struct NetplayStats
    {
        public NetplayStats(int updates, int skippedUpdates, int extraStepUpdates, uint leadTicks, int leadBonus,
            long aheadOfServerTicks, uint rawPlayoutDelay, int rawStarvations, int corrections)
        {
            Updates = updates;
            SkippedUpdates = skippedUpdates;
            ExtraStepUpdates = extraStepUpdates;
            LeadTicks = leadTicks;
            LeadBonus = leadBonus;
            AheadOfServerTicks = aheadOfServerTicks;
            RawPlayoutDelay = rawPlayoutDelay;
            RawStarvations = rawStarvations;
            Corrections = corrections;
        }

        public int Updates { get; }
        public int SkippedUpdates { get; }
        public int ExtraStepUpdates { get; }
        public uint LeadTicks { get; }
        public int LeadBonus { get; }
        public long AheadOfServerTicks { get; }
        public uint RawPlayoutDelay { get; }
        public int RawStarvations { get; }
        public int Corrections { get; }
    }
}
