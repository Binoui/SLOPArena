using System.Collections.Generic;
using SlopArena.Shared;

namespace SlopArena.Client.Simulation
{
    public class LocalSimulationBridge : ISimulationBridge
    {
        private ServerSimulation _server;
        private readonly ArenaDefinition _arena;
        private readonly IMatchRule? _rule;
        private readonly List<TimelinePresentationEvent> _lastTickPresentationEvents = new();

        public LocalSimulationBridge(ArenaDefinition arena, IMatchRule? rule = null)
        {
            _arena = arena;
            _rule = rule;
            _server = new ServerSimulation(arena, rule);
        }

        /// <summary>
        /// Replace the simulation with a clean one on the same arena and (stateless) rule, for an
        /// Arcade retry. Fighters, projectiles, queued effects and challenge state all start over;
        /// callers register entities again. Holders of this bridge need no rebinding.
        /// </summary>
        public void Restart()
        {
            _server = new ServerSimulation(_arena, _rule);
            _lastTickPresentationEvents.Clear();
        }
        public void RegisterEntity(ulong id, CharacterDefinition def, CharacterState initialState, BakedAnimationData? baked = null)
            => _server.RegisterEntity(id, def, initialState, baked);
        public void Tick(Dictionary<ulong, InputState> inputs)
        {
            _server.Tick(inputs);
            _lastTickPresentationEvents.Clear();
            _lastTickPresentationEvents.AddRange(_server.GetPresentationEvents(clear: true));
        }

        public ActiveMoveView? GetActiveMove(ulong id) => _server.GetActiveMove(id);
        public CharacterState GetState(ulong id) => _server.GetState(id);
        public Dictionary<ulong, CharacterState> GetAllStates() => _server.GetAllStates();
        public SpellResolver? Resolver => _server.Resolver;
        public IReadOnlyList<SpellResolver.HitResult> LastTickHits => _server.LastTickHits;
        public IReadOnlyList<TimelinePresentationEvent> LastTickPresentationEvents => _lastTickPresentationEvents;
        public ServerSimulation InternalSim => _server;
        public void SetRespawnPosition(ulong id, float x, float y, float z, float yaw = 0f)
            => _server.SetRespawnPosition(id, x, y, z, yaw);
    }
}
