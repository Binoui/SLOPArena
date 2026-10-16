using System.Collections.Generic;

namespace SlopArena.Shared.Rollback
{
    /// <summary>
    /// Rebuild-and-replay for opponent entities currently in a Predictable ActionState (D9).
    /// Owns ONE shared ServerSimulation for every tracked opponent, matching how the real
    /// server also sims everyone together — so hurtbox/collision checks between two
    /// tracked opponents behave consistently. Callers (RollbackSimulator, Task 5) must
    /// route Complex-state packets elsewhere (RawTrack) — this class only ever sees
    /// Predictable-state packets and calls StopTracking when an entity leaves that partition.
    ///
    /// Each opponent keeps its latest confirmed snapshot. A batch rebuilds every tracked
    /// opponent from its own snapshot (an opponent missing from the batch is not advanced
    /// twice); between batches the frontier advances one tick per local tick, holding each
    /// opponent's relayed input without one-tick press edges.
    /// </summary>
    public sealed class PredictedTrack
    {
        private readonly ServerSimulation _sim;
        private readonly Dictionary<ulong, Confirmed> _confirmed = new();
        private readonly List<ulong> _tracked = new();
        private readonly Dictionary<ulong, InputState> _frontierInputs = new();
        private uint _frontierTick;
        private const uint WindowCap = 30;

        private struct Confirmed
        {
            public uint Tick;
            public CharacterState State;
            public InputState HeldInput;
        }

        public uint LastFrontierTicks { get; private set; }

        public PredictedTrack(ArenaDefinition arena, IMatchRule? rule = null) => _sim = new ServerSimulation(arena, rule);

        public bool IsTracking(ulong id) => _confirmed.ContainsKey(id);

        /// <summary>
        /// Apply one network drain's worth of Predictable-state packets, then rebuild every
        /// tracked opponent from its confirmed tick to <paramref name="currentLocalTick"/>
        /// (capped at WindowCap ticks as a desync guard).
        /// </summary>
        public void ApplyBatch(IReadOnlyList<ServerEntityPacket> packets, uint currentLocalTick,
            IReadOnlyDictionary<ulong, CharacterDefinition> defs,
            IReadOnlyDictionary<ulong, BakedAnimationData?> baked)
        {
            if (packets.Count == 0) { LastFrontierTicks = 0; return; }

            bool changed = false;
            foreach (var packet in packets)
            {
                // No definition — cannot simulate this entity; the caller keeps it raw.
                if (!defs.TryGetValue(packet.EntityId, out var def))
                    continue;
                if (_confirmed.TryGetValue(packet.EntityId, out var existing) && packet.Tick < existing.Tick)
                    continue;

                var confirmedState = packet.State.ToState();
                confirmedState.EntityId = packet.EntityId;
                if (!_confirmed.ContainsKey(packet.EntityId))
                {
                    _sim.RegisterEntity(packet.EntityId, def, confirmedState,
                        baked.TryGetValue(packet.EntityId, out var b) ? b : null);
                    _tracked.Add(packet.EntityId);
                    _tracked.Sort();
                }

                // The relay describes the already-consumed confirmed tick. Reuse only its
                // held controls on the prediction frontier; never replay a one-tick edge.
                // The packet itself remains untouched for exact-tick consumers.
                _confirmed[packet.EntityId] = new Confirmed
                {
                    Tick = packet.Tick,
                    State = confirmedState,
                    HeldInput = packet.HasInput ? packet.Input.WithoutEdges() : default,
                };
                changed = true;
            }
            if (changed)
                Rebuild(currentLocalTick);
        }

        /// <summary>Advance every tracked opponent to <paramref name="localTick"/> on held inputs.</summary>
        public void AdvanceTo(uint localTick)
        {
            if (_tracked.Count == 0 || localTick <= _frontierTick)
            {
                if (_tracked.Count == 0) _frontierTick = localTick;
                return;
            }
            uint steps = System.Math.Min(localTick - _frontierTick, WindowCap);
            for (uint i = 0; i < steps; i++)
                StepFrontier();
            _frontierTick = localTick;
        }

        public void StopTracking(ulong id)
        {
            if (_confirmed.Remove(id))
            {
                _tracked.Remove(id);
                _frontierInputs.Remove(id);
                _sim.RemoveEntity(id);
            }
        }

        public IReadOnlyList<TimelinePresentationEvent> DrainPresentationEvents()
            => _sim.GetPresentationEvents(clear: true);
        public CharacterState GetState(ulong id) => _sim.GetState(id);

        private void Rebuild(uint localTick)
        {
            uint oldest = uint.MaxValue;
            uint newest = 0;
            foreach (var id in _tracked)
            {
                var confirmed = _confirmed[id];
                _sim.SetState(id, confirmed.State);
                if (confirmed.Tick < oldest) oldest = confirmed.Tick;
                if (confirmed.Tick > newest) newest = confirmed.Tick;
            }

            uint end = localTick > oldest ? localTick : oldest;
            if (end - oldest > WindowCap) end = oldest + WindowCap;
            _sim.SetTick(oldest);
            for (uint tick = oldest; tick < end; tick++)
            {
                // Opponents confirmed later than the oldest start exactly at their own tick.
                if (tick != oldest)
                    foreach (var id in _tracked)
                        if (_confirmed[id].Tick == tick)
                            _sim.SetState(id, _confirmed[id].State);
                StepFrontier();
            }
            foreach (var id in _tracked)
                if (_confirmed[id].Tick >= end && _confirmed[id].Tick != oldest)
                    _sim.SetState(id, _confirmed[id].State);

            _frontierTick = end;
            LastFrontierTicks = localTick > newest ? localTick - newest : 0;
        }

        private void StepFrontier()
        {
            _frontierInputs.Clear();
            foreach (var id in _tracked)
                _frontierInputs[id] = _confirmed[id].HeldInput;
            _sim.Tick(_frontierInputs);
        }
    }
}
