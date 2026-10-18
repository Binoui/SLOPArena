using System;
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
    /// Hits on the self are server decisions this track cannot predict (opponent mirrors
    /// carry no hitboxes) and cannot rebuild mid-freeze: the queued launch is not on the wire.
    /// A victim freeze therefore pauses the local simulation on the authoritative frozen
    /// snapshot (Hold). The first snapshot after the freeze carries the applied launch; the
    /// track re-seeds from it, restores the directional influence captured from the player's
    /// own inputs during the freeze, and replays to the local tick (Shadow). While either
    /// lasts, <see cref="PresentFromServer"/> asks the caller to present authoritative
    /// snapshots, so the freeze and the full hit reaction share the attacker's time frame.
    ///
    /// Also mirrors other entities' current best-known states in as read-only lookup
    /// targets: ServerSimulation.ProcessTargetLock indexes _states[targetId] directly for
    /// any entity whose input carries a nonzero TargetEntityId (screen-center soft-lock,
    /// set every frame an opponent is near screen center — not attack-only). Without a
    /// mirror, that throws KeyNotFoundException the moment an opponent is on screen.
    /// </summary>
    public sealed class LocalTrack
    {
        private enum ReactionMode : byte { None, Hold, Shadow }

        private const float MispredictionMeters = 0.01f;
        private readonly ServerSimulation _sim;
        private readonly ulong _entityId;
        private readonly List<(uint Tick, CharacterState State, InputState Input)> _history = new();
        private readonly List<(uint Tick, InputState Input)> _replay = new();
        private readonly HashSet<ulong> _mirrored = new();
        private const int WindowCap = 30;
        private uint _localTick;
        private uint _lastAuthoritativeTick;
        private ulong _lastTerminalInteractionId;
        private uint _lastTerminalInteractionTick;
        private bool _coupledBarrier;
        private ReactionMode _reaction;
        private bool _heldFrozen;
        private uint _reactionStartTick;
        private uint _lastSeenTick;
        private ushort _lastSeenDamage;
        private ushort _lastSeenReactionSequence;
        private int _windowReconciles, _windowMispredictions, _windowHitReactions, _windowMaxHitLateTicks;
        private float _windowMaxSnapMeters;

        public int CorrectionCount { get; private set; }

        /// <summary>True while a server-decided hit reaction should be presented from
        /// authoritative snapshots instead of this track's local timeline.</summary>
        public bool PresentFromServer => _reaction != ReactionMode.None;

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
            _reaction = ReactionMode.None;
            _heldFrozen = false;
            _reactionStartTick = 0;
            _lastSeenTick = 0;
            _lastSeenDamage = initialState.DamagePercent;
            _lastSeenReactionSequence = initialState.HitReactionSequence;
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
            if (_reaction == ReactionMode.Hold)
                _sim.SetTick(_localTick + 1); // paused on the server's freeze; keep the clock aligned
            else if (!_coupledBarrier)
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
            // The self track never hits itself, so new damage or a new reaction identity is a
            // server decision it could not predict.
            bool newDamage = false, newReaction = false;
            if (packet.Tick >= _lastSeenTick)
            {
                newDamage = authoritative.DamagePercent > _lastSeenDamage;
                newReaction = authoritative.HitReactionSequence != _lastSeenReactionSequence;
                _lastSeenTick = packet.Tick;
                _lastSeenDamage = authoritative.DamagePercent;
                _lastSeenReactionSequence = authoritative.HitReactionSequence;
            }
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
                _reaction = ReactionMode.None;
                _history.Clear();
                _history.Add((_localTick, authoritative, default));
                CorrectionCount++;
                RecordCorrection(current, authoritative);
                return;
            }

            if (TryHandleHitReaction(packet, authoritative, newDamage, newReaction))
                return;

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

            // Replay from the corrected tick so replayed and later ticks keep the server's
            // tick numbers (presentation event keys dedupe against the authoritative copies).
            _sim.SetTick(_history[idx].Tick);
            for (int i = idx + 1; i < _history.Count; i++)
            {
                _sim.Tick(new Dictionary<ulong, InputState> { { _entityId, _history[i].Input } });
                _history[i] = (_history[i].Tick, _sim.GetState(_entityId), _history[i].Input);
            }
            RecordCorrection(current, _sim.GetState(_entityId));
        }

        /// <summary>Correction health since the previous call (for the periodic netplay log).</summary>
        public LocalTrackStats TakeWindowStats()
        {
            var stats = new LocalTrackStats(_windowReconciles, _windowMispredictions,
                _windowMaxSnapMeters, _windowHitReactions, _windowMaxHitLateTicks);
            _windowReconciles = _windowMispredictions = _windowHitReactions = _windowMaxHitLateTicks = 0;
            _windowMaxSnapMeters = 0f;
            return stats;
        }

        public CharacterState GetState() => _sim.GetState(_entityId);
        public SpellResolver? Resolver => _sim.Resolver;
        public IReadOnlyList<SpellResolver.HitResult> LastTickHits => _sim.LastTickHits;

        public IReadOnlyList<TimelinePresentationEvent> DrainPresentationEvents()
            => _sim.GetPresentationEvents(clear: true);

        /// <summary>Route snapshots of a server-decided hit reaction. Returns true when the
        /// packet was consumed; false hands it to ordinary reconciliation.</summary>
        private bool TryHandleHitReaction(ServerEntityPacket packet, CharacterState authoritative,
            bool newDamage, bool newReaction)
        {
            bool frozen = packet.State.HitstopTicks > 0;
            if (frozen && newDamage)
            {
                EnterHold(packet, authoritative);
                return true;
            }
            bool freshLaunch = authoritative.State == ActionState.Hitstun && newReaction && !frozen;
            switch (_reaction)
            {
                case ReactionMode.Hold:
                    if (frozen)
                        UpdateHold(packet, authoritative, frozen: true);
                    else if (authoritative.State == ActionState.Hitstun)
                        Reseed(packet, authoritative, _heldFrozen, ReactionMode.Shadow);
                    else if (ActionStateClassifier.IsSnapSafe(authoritative))
                        Reseed(packet, authoritative, diFromFreeze: false, ReactionMode.None);
                    else
                        UpdateHold(packet, authoritative, frozen: false); // e.g. an armored attack continues
                    return true;
                case ReactionMode.Shadow:
                    if (freshLaunch)
                    {
                        CountHitReaction(packet.Tick);
                        Reseed(packet, authoritative, diFromFreeze: false, ReactionMode.Shadow);
                        return true;
                    }
                    if (frozen || authoritative.State == ActionState.Hitstun)
                        return true; // the re-seeded local sim runs the reaction itself
                    _reaction = ReactionMode.None; // the server left the reaction: present locally again
                    _reactionStartTick = 0;
                    return false;
                default:
                    if (!freshLaunch)
                        return false;
                    // A launch without a freeze applies directional influence at contact.
                    _reactionStartTick = packet.Tick;
                    CountHitReaction(packet.Tick);
                    Reseed(packet, authoritative, diFromFreeze: false, ReactionMode.Shadow);
                    return true;
            }
        }

        /// <summary>Pause on the server's victim freeze and cancel the local attack runtime the
        /// hit interrupted.</summary>
        private void EnterHold(ServerEntityPacket packet, CharacterState authoritative)
        {
            var before = _sim.GetState(_entityId);
            var held = HeldState(before, packet, authoritative);
            _sim.ApplyAuthoritativeState(_entityId, held);
            if (_reaction == ReactionMode.None)
                _reactionStartTick = packet.Tick;
            _reaction = ReactionMode.Hold;
            _heldFrozen = true;
            if (packet.Tick > _lastAuthoritativeTick)
                _lastAuthoritativeTick = packet.Tick;
            CorrectionCount++;
            _windowReconciles++;
            CountHitReaction(packet.Tick);
        }

        private void UpdateHold(ServerEntityPacket packet, CharacterState authoritative, bool frozen)
        {
            _sim.SetState(_entityId, HeldState(_sim.GetState(_entityId), packet, authoritative));
            _heldFrozen = frozen;
            if (packet.Tick > _lastAuthoritativeTick)
                _lastAuthoritativeTick = packet.Tick;
            CorrectionCount++;
            _windowReconciles++;
        }

        private static CharacterState HeldState(CharacterState local, ServerEntityPacket packet,
            CharacterState authoritative)
        {
            var held = local;
            packet.State.ApplyTo(ref held);
            ClearReactionLocals(ref held);
            // No live ability owns these timers while the server drives the self.
            held.AttackElapsedTicks = authoritative.AttackElapsedTicks;
            held.ChargeTicks = authoritative.ChargeTicks;
            return held;
        }

        /// <summary>Restart local prediction from an authoritative snapshot of the reaction and
        /// replay the inputs recorded after it, keeping the server's tick numbers.</summary>
        private void Reseed(ServerEntityPacket packet, CharacterState authoritative, bool diFromFreeze,
            ReactionMode next)
        {
            var before = _sim.GetState(_entityId);
            var seed = before;
            packet.State.ApplyTo(ref seed);
            ClearReactionLocals(ref seed);
            seed.AnimLockTicks = 0;
            if (seed.State == ActionState.Hitstun)
                (seed.DIX, seed.DIY) = diFromFreeze
                    ? LastMoveInput(_reactionStartTick, packet.Tick)
                    : (0f, 0f);

            InputState seedInput = default;
            _replay.Clear();
            foreach (var entry in _history)
            {
                if (entry.Tick == packet.Tick) seedInput = entry.Input;
                else if (entry.Tick > packet.Tick) _replay.Add((entry.Tick, entry.Input));
            }

            _sim.ApplyAuthoritativeState(_entityId, seed);
            _sim.SetTick(packet.Tick);
            _history.Clear();
            _history.Add((packet.Tick, seed, seedInput));
            uint tick = packet.Tick;
            int replayIndex = 0;
            while (tick < _localTick)
            {
                tick++;
                // A tick missing from the window replays the previous controls without presses.
                InputState input = replayIndex < _replay.Count && _replay[replayIndex].Tick == tick
                    ? _replay[replayIndex++].Input
                    : _history[_history.Count - 1].Input.WithoutEdges();
                _sim.Tick(new Dictionary<ulong, InputState> { { _entityId, input } });
                _history.Add((tick, _sim.GetState(_entityId), input));
            }
            while (_history.Count > WindowCap) _history.RemoveAt(0);

            _reaction = next;
            _heldFrozen = false;
            if (next == ReactionMode.None)
                _reactionStartTick = 0;
            if (packet.Tick > _lastAuthoritativeTick)
                _lastAuthoritativeTick = packet.Tick;
            CorrectionCount++;
            _windowReconciles++;
        }

        /// <summary>Directional influence held since the reaction began: the newest nonzero
        /// stick the server consumed during the freeze (ADR-0012 Combo Influence).</summary>
        private (float X, float Y) LastMoveInput(uint afterTick, uint throughTick)
        {
            (float X, float Y) influence = (0f, 0f);
            foreach (var entry in _history)
                if (entry.Tick > afterTick && entry.Tick <= throughTick &&
                    (entry.Input.MoveX != 0f || entry.Input.MoveY != 0f))
                    influence = (entry.Input.MoveX, entry.Input.MoveY);
            return influence;
        }

        private static void ClearReactionLocals(ref CharacterState s)
        {
            s.QueuedKBDirX = s.QueuedKBDirZ = 0f;
            s.QueuedKBAngle = 0;
            s.QueuedKBBase = s.QueuedKBGrowth = s.QueuedKBDamage = s.QueuedKBForce = 0f;
            s.QueuedKBStun = s.QueuedKBFixedHitstunTicks = s.QueuedKBStunGate = 0;
            s.QueuedArmorHitstop = s.QueuedKBResolvedForce = s.QueuedKBZero = false;
            s.DIX = s.DIY = 0f;
            s.SdiApplied = false;
            s.BufferedSlot = 0;
        }

        private void CountHitReaction(uint packetTick)
        {
            _windowHitReactions++;
            int late = _localTick > packetTick ? (int)(_localTick - packetTick) : 0;
            if (late > _windowMaxHitLateTicks) _windowMaxHitLateTicks = late;
        }

        private void RecordCorrection(CharacterState before, CharacterState after)
        {
            _windowReconciles++;
            float dx = after.PX - before.PX, dy = after.PY - before.PY, dz = after.PZ - before.PZ;
            float snap = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            if (snap < MispredictionMeters && before.State == after.State)
                return;
            _windowMispredictions++;
            if (snap > _windowMaxSnapMeters) _windowMaxSnapMeters = snap;
        }
    }

    /// <summary>Self-track correction health over one logging window.</summary>
    public readonly struct LocalTrackStats
    {
        public LocalTrackStats(int reconciles, int mispredictions, float maxSnapMeters,
            int hitReactions, int maxHitLateTicks)
        {
            Reconciles = reconciles;
            Mispredictions = mispredictions;
            MaxSnapMeters = maxSnapMeters;
            HitReactions = hitReactions;
            MaxHitLateTicks = maxHitLateTicks;
        }

        /// <summary>Authoritative snapshots applied to the self track.</summary>
        public int Reconciles { get; }
        /// <summary>Applied snapshots that moved the predicted self by a centimetre or more,
        /// or changed its action state.</summary>
        public int Mispredictions { get; }
        public float MaxSnapMeters { get; }
        /// <summary>Hits on the self that were presented from server snapshots.</summary>
        public int HitReactions { get; }
        /// <summary>How many ticks the local timeline had run past the newest such hit.</summary>
        public int MaxHitLateTicks { get; }
    }
}
