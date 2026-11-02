using System;
using System.Collections.Generic;
using SlopArena.Client.Simulation;
using SlopArena.Shared;

namespace SlopArena.Client.Replays
{
    /// <summary>The ordinary presentation seam over a Replay's display world. Presenters read it; nothing ticks it.</summary>
    public sealed class ReplayBridge : ISimulationBridge
    {
        private readonly ServerSimulation _world;

        public ReplayBridge(ServerSimulation world) => _world = world;

        public void RegisterEntity(ulong id, CharacterDefinition def, CharacterState initialState, BakedAnimationData? baked = null)
            => throw new InvalidOperationException("ReplayWorld registers the Replay cast.");

        public void Tick(Dictionary<ulong, InputState> inputs)
            => throw new InvalidOperationException("A Replay display world is never ticked.");

        public CharacterState GetState(ulong id) => _world.GetState(id);
        public ActiveMoveView? GetActiveMove(ulong id) => _world.GetActiveMove(id);
        public Dictionary<ulong, CharacterState> GetAllStates() => _world.GetAllStates();
        public SpellResolver? Resolver => _world.Resolver;
        public IReadOnlyList<SpellResolver.HitResult> LastTickHits => _world.LastTickHits;
        public IReadOnlyList<TimelinePresentationEvent> LastTickPresentationEvents => _world.GetPresentationEvents();
        public ServerSimulation? World => _world;
    }
}
