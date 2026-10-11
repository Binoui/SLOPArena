using System.Collections.Generic;

namespace SlopArena.Shared.Rollback
{
    /// <summary>
    /// The self entity's continuously-running ServerSimulation (ADR-0011). Never rebuilt
    /// from a received snapshot — fed the player's true InputState every tick. Corrected by
    /// patching wire-serialized fields onto its own full-fidelity history when the server
    /// packet disagrees, replayed forward only across a Predictable-state suffix (D9) —
    /// a Complex tick anywhere in the replay range means "trust the live sim", never rebuilt.
    ///
    /// Also mirrors other entities' current best-known states in as read-only lookup
    /// targets: ServerSimulation.ProcessTargetLock indexes _states[targetId] directly for
    /// any entity whose input carries a nonzero TargetEntityId (screen-center soft-lock,
    /// set every frame an opponent is near screen center — not attack-only). Without a
    /// mirror, that throws KeyNotFoundException the moment an opponent is on screen.
    /// </summary>
    public sealed class LocalTrack
    {
        private readonly ServerSimulation _sim;
        private readonly ulong _entityId;
        private readonly List<(uint Tick, CharacterState State, InputState Input)> _history = new();
        private readonly HashSet<ulong> _mirrored = new();
        private const int WindowCap = 30;
        private uint _localTick;
        private uint _lastAuthoritativeTick;
        private ulong _lastTerminalInteractionId;
        private uint _lastTerminalInteractionTick;
        private bool _coupledBarrier;

        public int CorrectionCount { get; private set; }

        public LocalTrack(ArenaDefinition arena, ulong entityId, IMatchRule? rule = null)
        {
            _sim = new ServerSimulation(arena, rule);
            _entityId = entityId;
            _sim.PredictCoupledInteractions = false;
        }

        public void RegisterEntity(CharacterDefinition def, CharacterState initialState, BakedAnimationData? baked = null)
        {
            _sim.RegisterEntity(_entityId, def, initialState, baked);
            _history.Clear();
            _localTick = 0;
            _lastAuthoritativeTick = 0;
            _lastTerminalInteractionId = 0;
            _lastTerminalInteractionTick = 0;
            _coupledBarrier = false;
            _history.Add((0, _sim.GetState(_entityId), default));
        }

        /// <summary>Seed the absolute server timeline before local simulation advances.</summary>
        public bool SetTimeline(uint lastCompletedTick)
        {
            if (_history.Count != 1 || lastCompletedTick < _localTick)
                return false;
            _localTick = lastCompletedTick;
            _lastAuthoritativeTick = lastCompletedTick;
            _sim.SetTick(lastCompletedTick);
            _history[0] = (lastCompletedTick, _sim.GetState(_entityId), default);
            return true;
        }
        /// <summary>Register-or-update a read-only mirror of another entity, purely so
        /// ServerSimulation's target-lock lookups resolve. Never rendered from this track.</summary>
        public void SyncOpponentMirror(ulong id, CharacterDefinition def, CharacterState state)
        {
            if (_mirrored.Add(id))
                _sim.RegisterEntity(id, def, state);
            else
                _sim.SetState(id, state);
        }

        public CharacterState Tick(InputState input)
        {
            if (!_coupledBarrier)
                _sim.Tick(new Dictionary<ulong, InputState> { { _entityId, input } });
            var state = _sim.GetState(_entityId);
            _localTick++;
            _history.Add((_localTick, state, input));
            if (_history.Count > WindowCap) _history.RemoveAt(0);
            return state;
        }

        /// <summary>Correct safe history normally. Block contact and coupled outcomes
        /// establish a barrier even when the local history contains an active ability.</summary>
        public void ReconcileWithServer(ServerEntityPacket packet)
        {
            var authoritative = packet.State.ToState();
            authoritative.EntityId = _entityId;
            bool hasTerminal = authoritative.LastTerminalInteractionId != 0 &&
                authoritative.InteractionTerminalTick != 0;
            bool newTerminal = hasTerminal &&
                authoritative.InteractionTerminalTick >= _lastTerminalInteractionTick &&
                authoritative.LastTerminalInteractionId != _lastTerminalInteractionId;
            if (newTerminal)
            {
                _lastTerminalInteractionId = authoritative.LastTerminalInteractionId;
                _lastTerminalInteractionTick = authoritative.InteractionTerminalTick;
            }
            if (hasTerminal && !newTerminal && packet.Tick <= _lastAuthoritativeTick)
                return;

            var current = _sim.GetState(_entityId);
            bool terminal = newTerminal &&
                (current.InteractionId == 0 ||
                 current.InteractionId == authoritative.LastTerminalInteractionId) &&
                (authoritative.InteractionId == 0 ||
                 authoritative.InteractionId == authoritative.LastTerminalInteractionId);
            if (newTerminal && !terminal)
                return;

            bool staleInteraction = authoritative.InteractionId != 0 &&
                authoritative.InteractionId == _lastTerminalInteractionId;
            if (staleInteraction) return;

            bool coupled = authoritative.InteractionId != 0 &&
                (authoritative.State == ActionState.Grabbing ||
                 authoritative.State == ActionState.Grabbed ||
                 authoritative.State == ActionState.Throwing);
            bool contact = authoritative.BlockStunTicks != 0 ||
                authoritative.BlockHitstopKind != 0;
            if (coupled || terminal || contact || _coupledBarrier)
            {
                if (packet.Tick > _lastAuthoritativeTick)
                    _lastAuthoritativeTick = packet.Tick;
                _sim.ApplyAuthoritativeState(_entityId, authoritative);
                _coupledBarrier = coupled;
                _history.Clear();
                _history.Add((_localTick, authoritative, default));
                CorrectionCount++;
                return;
            }

            // Ordinary reconciliation cannot rebuild an attack's live ability from
            // wire fields after local recovery. Wait for a subsequent authority state
            // rather than install an attack or aim state with no matching runtime.
            // Coupled/contact barriers above still own their authoritative outcomes.
            if (authoritative.State is ActionState.Attacking or ActionState.Aiming)
                return;

            // Hitstop carries live ability/queued-launch state not represented in the
            // packet. Never reconstruct from an authoritative frozen snapshot.
            if (packet.State.HitstopTicks > 0) return;

            int idx = _history.FindIndex(h => h.Tick == packet.Tick);
            if (idx < 0) return; // outside the window — trust the continuous sim, self-heals next packet

            for (int i = idx; i < _history.Count; i++)
                if (!ActionStateClassifier.IsSnapSafe(_history[i].State))
                    return;
            _lastAuthoritativeTick = packet.Tick;
            CorrectionCount++;

            var corrected = _history[idx].State;
            packet.State.ApplyTo(ref corrected);
            _sim.SetState(_entityId, corrected);
            _history[idx] = (_history[idx].Tick, corrected, _history[idx].Input);

            for (int i = idx + 1; i < _history.Count; i++)
            {
                _sim.Tick(new Dictionary<ulong, InputState> { { _entityId, _history[i].Input } });
                _history[i] = (_history[i].Tick, _sim.GetState(_entityId), _history[i].Input);
            }
        }

        public CharacterState GetState() => _sim.GetState(_entityId);
        public SpellResolver? Resolver => _sim.Resolver;
        public IReadOnlyList<SpellResolver.HitResult> LastTickHits => _sim.LastTickHits;

        public IReadOnlyList<TimelinePresentationEvent> DrainPresentationEvents()
            => _sim.GetPresentationEvents(clear: true);
    }
}
