using System.Collections.Generic;
using SlopArena.Shared;

namespace SlopArena.Client.Simulation
{
    public interface ISimulationBridge
    {
        void RegisterEntity(ulong id, CharacterDefinition def, CharacterState initialState,
            BakedAnimationData? baked = null);
        void Tick(Dictionary<ulong, InputState> inputs);
        CharacterState GetState(ulong id);
        ActiveMoveView? GetActiveMove(ulong id);
        Dictionary<ulong, CharacterState> GetAllStates();
        SpellResolver? Resolver { get; }
        IReadOnlyList<SpellResolver.HitResult> LastTickHits { get; }
        IReadOnlyList<TimelinePresentationEvent> LastTickPresentationEvents { get; }
        /// <summary>The simulation presenters read (local, predicted or a Replay's display world); never tick it here.</summary>
        ServerSimulation? World { get; }
    }
}
