using System;
using System.Collections.Generic;
using SlopArena.Shared.Abilities;

namespace SlopArena.Shared
{
    public partial class ServerSimulation
    {
        /// <summary>
        /// Set on every Replay shell. Tick throws, as a guard against the most likely misuse. It does not make
        /// the world read-only: presenters must still never call SetState, ActivateAbility or TickAbilities.
        /// </summary>
        internal bool ReplayDisplay { get; set; }

        // A Replay shows the pose clock it recorded; today's clip timing must not retime an old move.
        private readonly Dictionary<ulong, AttackPose> _observedPoses = new();

        /// <summary>The recorded outputs of one tick in today's Shared types, written after either track's restore.</summary>
        internal sealed record TickOutputs(
            IReadOnlyList<SpellResolver.HitResult> Hits,
            IReadOnlyList<DeathEvent> Deaths,
            IReadOnlyList<TimelinePresentationEvent> PresentationEvents,
            IReadOnlyList<SlopballEvent> SlopballEvents,
            IReadOnlyList<CourseTickEvent> CourseEvents,
            IReadOnlyList<MankiMayhemEvent> MayhemEvents);

        /// <summary>Restore clears the outputs of the tick that produced the state; this writes the recorded ones back.</summary>
        internal void ApplyTickOutputs(TickOutputs outputs)
        {
            if (!ReplayDisplay) throw new InvalidOperationException("Tick outputs are written only into a Replay display world.");
            LastTickHits.Clear();
            LastTickHits.AddRange(outputs.Hits);
            LastTickDeaths.Clear();
            LastTickDeaths.AddRange(outputs.Deaths);
            _presentationEvents.Clear();
            _presentationEvents.AddRange(outputs.PresentationEvents);
            _slopball?.RestoreEvents(outputs.SlopballEvents);
            _volley?.RestoreEvents(outputs.SlopballEvents);
            _onlineCourse?.RestoreEvents(outputs.CourseEvents);
            _mayhem?.RestoreEvents(outputs.MayhemEvents);
        }

        /// <summary>A timeline runtime wired like a restored one and positioned at a recorded point, for GetActiveMove.</summary>
        internal CookedTimelineAbility ObserveAbility(ulong entityId, CookedSlotDefinition slot, byte wireSlot,
            ulong activationId, int stageIndex, ushort stageTick, byte clipIndex, bool airborne)
        {
            var ability = new CookedTimelineAbility(slot);
            WireAbility(entityId, ability, _defs[entityId].ForForm(slot.Form));
            ability.Slot = (byte)(wireSlot - 1);
            ability.ActivationId = activationId;
            ability.Observe(stageIndex, stageTick, clipIndex, airborne);
            return ability;
        }
    }
}
