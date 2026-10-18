using System.Collections.Generic;

namespace SlopArena.Shared.Rollback
{
    /// <summary>
    /// Composes LocalTrack (self), PredictedTrack (opponents in a Predictable ActionState),
    /// and RawTrack (opponents in a Complex ActionState — received states, no simulation)
    /// into one entity-addressable surface (ADR-0011). Shape matches ISimulationBridge
    /// deliberately — RollbackSimulationBridge (Task 7) is a thin wrapper.
    ///
    /// RawTrack presentation plays received snapshots back on a playout clock that advances
    /// one tick per local tick, a small adaptive delay behind the newest server tick, so
    /// bunched packet arrival does not stall or skip raw opponents. Logic consumers (the
    /// self track's opponent mirror, interaction barriers) use the newest snapshot. While
    /// LocalTrack presents a server-decided hit reaction, the self plays back on the same
    /// clock, so attacker and victim freeze and react in one time frame.
    /// </summary>
    public sealed class RollbackSimulator
    {
        public const uint MinimumRawPlayoutDelay = 1;
        public const uint MaximumRawPlayoutDelay = 4;
        private const uint RawPlayoutResyncTicks = 8;
        private const uint RawPlayoutHealthyTicksBeforeShrink = 300;
        private const int RawHistoryCapacity = 64;

        private readonly LocalTrack _local;
        private readonly PredictedTrack _predicted;
        private readonly Dictionary<ulong, CharacterState> _rawTrackLatest = new();
        private readonly Dictionary<ulong, List<(uint Tick, CharacterState State)>> _rawHistory = new();
        private readonly List<(uint Tick, CharacterState State)> _selfServerHistory = new();
        private readonly TickPacer _rawPacer = new();
        private uint _rawNewestTick;
        private uint _rawPlayoutTick;
        private bool _rawPlayoutSeeded;
        private uint _rawPlayoutDelay = 2;
        private uint _rawHealthyTicks;
        private readonly Dictionary<ulong, CharacterDefinition> _defs = new();
        private readonly Dictionary<ulong, BakedAnimationData?> _baked = new();
        private readonly ulong _selfId;
        private uint _localTick;
        private readonly List<TimelinePresentationEvent> _acceptedPresentationEvents = new();
        private readonly HashSet<PresentationEventKey> _seenPresentationEvents = new();
        private readonly Dictionary<ulong, uint> _latestPacketTick = new();
        private readonly HashSet<ulong> _terminalInteractions = new(); // Match-unique IDs stay ended across later snapshots.
        private readonly Dictionary<ulong, ServerEntityPacket> _pendingCompanions = new();
        private ServerEntityPacket? _futureSelf;
        private const uint CompanionWindow = 30;

        public RollbackSimulator(ArenaDefinition arena, ulong selfEntityId, IMatchRule? rule = null)
        {
            _selfId = selfEntityId;
            _local = new LocalTrack(arena, selfEntityId, rule);
            _predicted = new PredictedTrack(arena, rule);
        }

        public int CorrectionCount => _local.CorrectionCount;
        /// <summary>Self-track correction health since the previous call.</summary>
        public LocalTrackStats TakeSelfStats() => _local.TakeWindowStats();
        public uint LastFrontierTicks => _predicted.LastFrontierTicks;
        public uint LocalTick => _localTick;
        /// <summary>Ticks between the newest server snapshot and the raw playout clock.</summary>
        public uint RawPlayoutDelay => _rawPlayoutDelay;
        /// <summary>Times raw playout caught up with the newest snapshot and had to hold.</summary>
        public int RawPlayoutStarvations { get; private set; }

        public void RegisterEntity(ulong id, CharacterDefinition def, CharacterState initialState, BakedAnimationData? baked = null)
        {
            _defs[id] = def;
            _baked[id] = baked;
            if (id == _selfId)
                _local.RegisterEntity(def, initialState, baked);
            else
                _rawTrackLatest[id] = initialState; // opponents start on RawTrack until their first packet
        }

        /// <summary>Seed the absolute server tick before prediction begins.</summary>
        public bool SetTimeline(uint lastCompletedTick)
        {
            if (lastCompletedTick < _localTick || !_local.SetTimeline(lastCompletedTick))
                return false;
            _localTick = lastCompletedTick;
            return true;
        }
        /// <summary>Advance the self entity one tick. Mirrors every other known entity's
        /// current best-known state into LocalTrack first (target-lock crash fix, Task 3).</summary>
        public void Tick(Dictionary<ulong, InputState> inputs)
        {
            foreach (var id in _defs.Keys)
                if (id != _selfId)
                    _local.SyncOpponentMirror(id, _defs[id], GetLogicState(id));

            var input = inputs.TryGetValue(_selfId, out var i) ? i : default;
            _local.Tick(input);
            _localTick++;
            Publish(_local.DrainPresentationEvents());
            _predicted.AdvanceTo(_localTick);
            Publish(_predicted.DrainPresentationEvents());
            AdvanceRawPlayout();
            if (_futureSelf is ServerEntityPacket future && future.Tick <= _localTick)
            {
                _futureSelf = null;
                if (!_terminalInteractions.Contains(future.State.InteractionId))
                {
                    _local.ReconcileWithServer(future);
                    TrackSelfPresentation(future);
                }
                Publish(_local.DrainPresentationEvents());
            }

        }

        /// <summary>Feed one network drain's worth of opponent packets. Predictable low
        /// states go to PredictedTrack; Complex or hitstop states stay on RawTrack.</summary>
        public void IngestOpponentBatch(IReadOnlyList<ServerEntityPacket> packets)
            => IngestAuthoritativeBatch(packets);

        /// <summary>Ingest the entire network drain together. Paired snapshots never
        /// enter independent opponent replay; a missing companion may arrive in a
        /// later drain, while terminal snapshots stand on their own.</summary>
        public void IngestAuthoritativeBatch(IReadOnlyList<ServerEntityPacket> packets)
        {
            var predictable = new List<ServerEntityPacket>();
            foreach (var packet in packets)
            {
                var state = packet.State.ToState();
                bool localReconciled = false;
                if (state.LastTerminalInteractionId != 0 &&
                    state.InteractionTerminalTick != 0)
                {
                    ulong terminalId = state.LastTerminalInteractionId;
                    if (_terminalInteractions.Add(terminalId) && _pendingCompanions.Count > 0)
                    {
                        var stale = new List<ulong>();
                        foreach (var pending in _pendingCompanions)
                            if (pending.Value.State.InteractionId == terminalId)
                                stale.Add(pending.Key);
                        foreach (var id in stale) _pendingCompanions.Remove(id);
                    }

                    if (packet.EntityId == _selfId)
                    {
                        _local.ReconcileWithServer(packet);
                        TrackSelfPresentation(packet);
                        localReconciled = true;
                    }
                    else if (state.InteractionId == 0 &&
                        GetLogicState(packet.EntityId).InteractionId == terminalId)
                    {
                        _predicted.StopTracking(packet.EntityId);
                        state.EntityId = packet.EntityId;
                        // A terminal outcome supersedes newer-ticked capture snapshots.
                        SetRaw(packet.EntityId, packet.Tick, state, replaceHistory: true);
                    }
                }

                if (packet.Tick > _rawNewestTick)
                    _rawNewestTick = packet.Tick;
                if (_latestPacketTick.TryGetValue(packet.EntityId, out uint last) &&
                    packet.Tick <= last) continue;
                _latestPacketTick[packet.EntityId] = packet.Tick;

                if (state.InteractionId != 0 &&
                    _terminalInteractions.Contains(state.InteractionId))
                    continue;

                // An opponent's capture can arrive before the self packet. Do not
                // present a one-sided pair; keep the newest bounded snapshot.
                if (packet.EntityId != _selfId && state.InteractionId != 0 &&
                    state.InteractionPartnerId == _selfId &&
                    (state.State is ActionState.Grabbing or ActionState.Grabbed or ActionState.Throwing))
                {
                    var self = _local.GetState();
                    if (self.InteractionId != state.InteractionId ||
                        self.InteractionTick != state.InteractionTick)
                    {
                        _pendingCompanions[packet.EntityId] = packet;
                        continue;
                    }
                }

                if (packet.EntityId == _selfId && !localReconciled && packet.Tick > _localTick)
                {
                    _futureSelf = packet;
                    continue;
                }
                _pendingCompanions.Remove(packet.EntityId);
                if (packet.EntityId == _selfId)
                {
                    if (!localReconciled)
                    {
                        _local.ReconcileWithServer(packet);
                        TrackSelfPresentation(packet);
                    }
                    if (state.InteractionId != 0 &&
                        _pendingCompanions.TryGetValue(state.InteractionPartnerId, out var companion))
                    {
                        var companionState = companion.State.ToState();
                        if (companionState.InteractionId == state.InteractionId &&
                            companionState.InteractionTick == state.InteractionTick &&
                            companionState.InteractionPartnerId == _selfId &&
                            (!_latestPacketTick.TryGetValue(companion.EntityId, out uint newest) ||
                             newest <= companion.Tick) &&
                            !_terminalInteractions.Contains(companionState.InteractionId))
                        {
                            _pendingCompanions.Remove(state.InteractionPartnerId);
                            _predicted.StopTracking(companion.EntityId);
                            companionState.EntityId = companion.EntityId;
                            SetRaw(companion.EntityId, companion.Tick, companionState);
                        }
                    }
                    continue;
                }
                if (ActionStateClassifier.IsPredictable(state) &&
                    state.InteractionTerminalTick != packet.Tick &&
                    _defs.ContainsKey(packet.EntityId))
                {
                    predictable.Add(packet);
                    _rawTrackLatest.Remove(packet.EntityId);
                    _rawHistory.Remove(packet.EntityId);
                }
                else
                {
                    _predicted.StopTracking(packet.EntityId);
                    state.EntityId = packet.EntityId;
                    SetRaw(packet.EntityId, packet.Tick, state);
                }
            }
            if (predictable.Count > 0)
            {
                _predicted.ApplyBatch(predictable, _localTick, _defs, _baked);
                Publish(_predicted.DrainPresentationEvents());
            }
            var expired = new List<ulong>();
            foreach (var pending in _pendingCompanions)
                if (_localTick >= pending.Value.Tick &&
                    _localTick - pending.Value.Tick > CompanionWindow)
                    expired.Add(pending.Key);
            foreach (var id in expired) _pendingCompanions.Remove(id);
        }

        /// <summary>Legacy one-packet entry point; the bridge uses the batched path.</summary>
        public void ReconcileSelf(ServerEntityPacket packet)
            => IngestAuthoritativeBatch(new[] { packet });

        /// <summary>Presentation state: self prediction, predicted opponents, or raw playout.</summary>
        public CharacterState GetState(ulong id)
        {
            if (id == _selfId)
                return _local.PresentFromServer && _selfServerHistory.Count > 0
                    ? RawDisplayState(_selfServerHistory)
                    : _local.GetState();
            if (_predicted.IsTracking(id)) return _predicted.GetState(id);
            if (_rawHistory.TryGetValue(id, out var history) && history.Count > 0)
                return RawDisplayState(history);
            return _rawTrackLatest.TryGetValue(id, out var s) ? s : default;
        }

        /// <summary>Newest authoritative or predicted state, without raw playout delay.</summary>
        public CharacterState GetLogicState(ulong id)
        {
            if (id == _selfId) return _local.GetState();
            if (_predicted.IsTracking(id)) return _predicted.GetState(id);
            return _rawTrackLatest.TryGetValue(id, out var s) ? s : default;
        }

        private void SetRaw(ulong id, uint tick, CharacterState state, bool replaceHistory = false)
        {
            _rawTrackLatest[id] = state;
            if (!_rawHistory.TryGetValue(id, out var history))
                _rawHistory[id] = history = new List<(uint Tick, CharacterState State)>();
            if (replaceHistory)
                history.Clear();
            InsertSnapshot(history, tick, state);
        }

        /// <summary>Keep the self's authoritative snapshots only while LocalTrack presents
        /// a server-decided hit reaction.</summary>
        private void TrackSelfPresentation(ServerEntityPacket packet)
        {
            if (!_local.PresentFromServer)
            {
                _selfServerHistory.Clear();
                return;
            }
            var state = packet.State.ToState();
            state.EntityId = _selfId;
            InsertSnapshot(_selfServerHistory, packet.Tick, state);
        }

        private static void InsertSnapshot(List<(uint Tick, CharacterState State)> history, uint tick, CharacterState state)
        {
            int index = history.Count;
            while (index > 0 && history[index - 1].Tick >= tick)
                index--;
            if (index < history.Count && history[index].Tick == tick)
                history[index] = (tick, state);
            else
                history.Insert(index, (tick, state));
            if (history.Count > RawHistoryCapacity)
                history.RemoveAt(0);
        }

        private CharacterState RawDisplayState(List<(uint Tick, CharacterState State)> history)
        {
            var newest = history[history.Count - 1].State;
            // Coupled interactions stay paired with the self track's newest authoritative state.
            if (newest.InteractionId != 0 || !_rawPlayoutSeeded)
                return newest;
            for (int i = history.Count - 1; i >= 0; i--)
                if (history[i].Tick <= _rawPlayoutTick)
                    return history[i].State;
            return history[0].State;
        }

        /// <summary>Advance raw playout one tick per local tick toward newest-minus-delay.</summary>
        private void AdvanceRawPlayout()
        {
            if (_rawNewestTick == 0)
                return;
            uint target = _rawNewestTick > _rawPlayoutDelay ? _rawNewestTick - _rawPlayoutDelay : 0;
            if (!_rawPlayoutSeeded)
            {
                _rawPlayoutTick = target;
                _rawPlayoutSeeded = true;
                return;
            }
            long error = (long)target - ((long)_rawPlayoutTick + 1);
            if (error >= RawPlayoutResyncTicks || error <= -(long)RawPlayoutResyncTicks)
            {
                _rawPlayoutTick = target;
                _rawPacer.Reset();
            }
            else
                _rawPlayoutTick += _rawPacer.Steps(_rawPlayoutTick, target, 2);

            if (_rawPlayoutTick > _rawNewestTick)
            {
                _rawPlayoutTick = _rawNewestTick;
                RawPlayoutStarvations++;
                _rawHealthyTicks = 0;
                if (_rawPlayoutDelay < MaximumRawPlayoutDelay)
                    _rawPlayoutDelay++;
            }
            else if (++_rawHealthyTicks >= RawPlayoutHealthyTicksBeforeShrink)
            {
                _rawHealthyTicks = 0;
                if (_rawPlayoutDelay > MinimumRawPlayoutDelay)
                    _rawPlayoutDelay--;
            }

            foreach (var history in _rawHistory.Values)
                TrimPlayedSnapshots(history);
            TrimPlayedSnapshots(_selfServerHistory);
        }

        private void TrimPlayedSnapshots(List<(uint Tick, CharacterState State)> history)
        {
            int keep = 0;
            while (keep + 1 < history.Count && history[keep + 1].Tick <= _rawPlayoutTick)
                keep++;
            if (keep > 0)
                history.RemoveRange(0, keep);
        }

        public Dictionary<ulong, CharacterState> GetAllStates()
        {
            var result = new Dictionary<ulong, CharacterState> { [_selfId] = GetState(_selfId) };
            foreach (var id in _defs.Keys)
                if (id != _selfId) result[id] = GetState(id);
            return result;
        }

        public void IngestPresentationEvent(TimelinePresentationEvent value)
        {
            if (_seenPresentationEvents.Add(value.Key))
                _acceptedPresentationEvents.Add(value);
        }

        public void IngestPresentationEvents(IReadOnlyList<TimelinePresentationEvent> values)
        {
            foreach (var value in values)
                IngestPresentationEvent(value);
        }

        public List<TimelinePresentationEvent> DrainPresentationEvents()
        {
            var result = new List<TimelinePresentationEvent>(_acceptedPresentationEvents);
            _acceptedPresentationEvents.Clear();
            return result;
        }

        private void Publish(IReadOnlyList<TimelinePresentationEvent> values)
        {
            foreach (var value in values)
                if (value.Source != PresentationEventSource.HitContact)
                    IngestPresentationEvent(value);
        }

        public SpellResolver? Resolver => _local.Resolver;
        public IReadOnlyList<SpellResolver.HitResult> LastTickHits => _local.LastTickHits;
    }
}
