using System;
using System.Collections.Generic;

namespace SlopArena.Shared.Rollback
{
    /// <summary>
    /// One player's online match, independent of the transport (ADR-0039). It paces full-world
    /// prediction with the <see cref="NetplayClock"/>, takes the GameHost's Clock controls and
    /// World Frames, and produces the input bundles to send. The Unity bridge wraps it around
    /// NetworkClient; GameHost tests drive it over real UDP.
    ///
    /// Prediction starts from the frame that carries the Playing state: the GO frame is the
    /// tick before the match's start tick, so the first predicted tick is the first simulated
    /// one. Each update takes the clock's paced steps; catch-up steps hold the previous controls
    /// and the newest step carries the presses, including those of updates that took no step.
    /// </summary>
    public sealed class NetplaySession
    {
        private readonly NetplayPredictor _predictor;
        private readonly NetplayClock _clock = new();
        private readonly InputState[] _recent = new InputState[InputBundle.MaxInputs];
        private int _recentCount;
        private InputState _buffered;
        private bool _hasBuffered;
        private InputState _lastApplied;
        private int _updates, _skippedUpdates, _extraStepUpdates, _serverLateInputs;
        private int _minInputSlack = int.MaxValue;

        public NetplaySession(ArenaDefinition arena, ulong selfEntityId, IMatchRule rule)
        {
            _predictor = new NetplayPredictor(arena, selfEntityId, rule);
        }

        public NetplayPredictor Predictor => _predictor;
        public NetplayClock Clock => _clock;
        public ulong SelfEntityId => _predictor.SelfEntityId;
        /// <summary>Last predicted tick; the newest input in <see cref="RecentInputs"/> belongs to it.</summary>
        public uint LocalTick => _predictor.LocalTick;
        /// <summary>Newest match state any frame carried: Waiting, Countdown, Playing, Ended.</summary>
        public MatchState MatchState { get; private set; } = MatchState.Waiting;
        /// <summary>A frame arrived since the connection (re)started, so every fighter's state is known.</summary>
        public bool HasFrame { get; private set; }
        /// <summary>Newest frame tick received (for Ready controls).</summary>
        public uint NewestFrameTick { get; private set; }
        /// <summary>The inputs to send, oldest first, for consecutive ticks ending at <see cref="LocalTick"/>.</summary>
        public ReadOnlySpan<InputState> RecentInputs => _recent.AsSpan(0, _recentCount);

        public void RegisterEntity(ulong id, CharacterDefinition def, CharacterState initialState, BakedAnimationData? baked = null)
            => _predictor.RegisterEntity(id, def, initialState, baked);

        /// <summary>A new connection must receive a frame again before it reports Ready.</summary>
        public void ResetConnection() => HasFrame = false;

        /// <summary>A control from the GameHost: Clock samples and this player's input-arrival report.</summary>
        public void ObserveControl(in NetplayControlPacket control, double receivedAtSeconds, double roundTripMilliseconds)
        {
            if (control.EntityId != SelfEntityId || control.Kind != NetplayControlKind.Clock)
                return;
            _clock.ObserveInputTiming(control.InputSlack, control.LateInputs, receivedAtSeconds);
            _serverLateInputs += control.LateInputs;
            if (control.InputSlack != NetplayControlPacket.NoInputSlack && control.InputSlack < _minInputSlack)
                _minInputSlack = control.InputSlack;
            _clock.Observe(control.Tick, control.StartTick, receivedAtSeconds, roundTripMilliseconds);
        }

        /// <summary>One network drain of World Frames.</summary>
        public void IngestFrames(IReadOnlyList<WorldFramePacket> frames)
        {
            if (frames.Count == 0)
                return;
            foreach (var frame in frames)
            {
                HasFrame = true;
                if (frame.Tick > NewestFrameTick)
                    NewestFrameTick = frame.Tick;
                if (frame.MatchState > MatchState)
                    MatchState = frame.MatchState;
            }
            uint before = _predictor.LocalTick;
            _predictor.IngestFrames(frames);
            if (_predictor.LocalTick != before)
            {
                // The timeline jumped to the GameHost: older inputs no longer precede the local
                // tick, and pacing restarts from the new frontier.
                _recentCount = 0;
                _clock.ResetPacing();
            }
        }

        /// <summary>
        /// Predict this update's ticks with the player's newest input. Returns true when at least
        /// one tick was predicted: send <see cref="RecentInputs"/> ending at <see cref="LocalTick"/>.
        /// </summary>
        public bool Update(InputState input, double nowSeconds)
        {
            if (MatchState != MatchState.Playing || _predictor.AuthoritativeMatchState != MatchState.Playing ||
                !_clock.IsSeeded || _clock.EstimateTick(nowSeconds) < _clock.StartTick)
                return false;

            if (_hasBuffered)
                input = input.WithEdgesFrom(_buffered);
            _buffered = input;
            _hasBuffered = true;

            uint steps = _clock.StepsForUpdate(_predictor.LocalTick, nowSeconds);
            _updates++;
            if (steps == 0) _skippedUpdates++;
            else if (steps > 1) _extraStepUpdates++;
            bool advanced = false;
            for (uint i = 0; i < steps; i++)
            {
                bool newest = i == steps - 1;
                var tickInput = newest ? _buffered : _lastApplied.WithoutEdges();
                if (!_predictor.Advance(tickInput))
                    break; // window full: keep the presses for a later tick
                advanced = true;
                Remember(tickInput);
                if (newest)
                {
                    _lastApplied = tickInput.WithoutEdges();
                    _buffered = default;
                    _hasBuffered = false;
                }
            }
            return advanced;
        }

        /// <summary>Pacing, prediction and input-arrival health since the previous call.</summary>
        public NetplaySessionStats TakeStats(double nowSeconds)
        {
            var stats = new NetplaySessionStats
            {
                Updates = _updates,
                SkippedUpdates = _skippedUpdates,
                ExtraStepUpdates = _extraStepUpdates,
                LeadTicks = _clock.LeadTicks,
                LeadBonus = _clock.LeadBonus,
                AheadOfServerTicks = _clock.IsSeeded ? (long)_predictor.LocalTick - _clock.EstimateTick(nowSeconds) : 0,
                ServerLateInputs = _serverLateInputs,
                MinInputSlack = _minInputSlack == int.MaxValue ? (int?)null : _minInputSlack,
                Prediction = _predictor.TakeStats(),
            };
            _updates = _skippedUpdates = _extraStepUpdates = _serverLateInputs = 0;
            _minInputSlack = int.MaxValue;
            return stats;
        }

        private void Remember(InputState input)
        {
            if (_recentCount == _recent.Length)
            {
                Array.Copy(_recent, 1, _recent, 0, _recent.Length - 1);
                _recentCount--;
            }
            _recent[_recentCount++] = input;
        }
    }

    /// <summary>One window of online pacing, prediction and input-arrival health (for the netplay log).</summary>
    public struct NetplaySessionStats
    {
        public int Updates, SkippedUpdates, ExtraStepUpdates;
        public uint LeadTicks;
        public int LeadBonus;
        /// <summary>Local tick minus the estimated GameHost tick.</summary>
        public long AheadOfServerTicks;
        /// <summary>Input datagrams the GameHost reported arriving after their tick ran.</summary>
        public int ServerLateInputs;
        /// <summary>Smallest arrival slack the GameHost reported (1 = just in time), if any input arrived.</summary>
        public int? MinInputSlack;
        public NetplayPredictorStats Prediction;
    }
}
