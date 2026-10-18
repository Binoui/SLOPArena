using System;

namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// FightGuy's E-slot upward-mobility move (issue #117) — the ADR-0015 up-B analog.
    ///
    /// A rising punch: Melee Falcon-Dive-style recovery. VY is SET to <c>rise_speed</c>
    /// (cancels fall/drift — reliable recovery, not additive) and held at that constant speed
    /// for <c>rise_ticks</c> (overrides gravity: TickAbilities runs after ApplyGravity), then
    /// gravity resumes. Optional hitboxes from the spec's stage.
    ///
    /// Grounded cast (anti-air launcher) delays the rise by <c>rise_delay</c> (default 8 —
    /// matches the animation windup), so the authored hitboxes at trigger ticks 6-10 connect
    /// while the body is still low, before the launch. Airborne cast (recovery burst) rises
    /// immediately: a windup there would eat ticks of fall before the burst.
    ///
    /// The spec carries <c>IsRecoveryMove</c>, so the engine resets AirTimeTicks (FloatWindow)
    /// when activated airborne — but the ability closes that float window once the rise ends, so
    /// gravity bounds the arc into a Melee up-B height instead of rocketing on the float.
    ///
    /// Cooked capability parameters supply rise speed, rise duration, and grounded delay.
    /// The containing cooked stage owns animation and outer completion timing.
    /// </summary>
    public class FightGuyRisingKick : ServerAbility
    {
        public override bool OwnsVerticalMotion => true;

        private readonly CookedRisingDragonCapabilityParameters _parameters;
        private ushort _ticks;
        private bool _airborne;

        public FightGuyRisingKick(CookedRisingDragonCapabilityParameters parameters)
            => _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));

        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            ClearVelocityOwnership(ref s);
            s.IsFastFalling = false;

            _ticks = 0;
            _airborne = !s.IsGrounded;
            s.FacingYaw = s.AimYaw;
            s.State = ActionState.Attacking;
            s.IsAiming = false;
            AnimIndex = 0;
            s.ComboStage = 0;
            s.AttackElapsedTicks = 0;
            if (_airborne)
            {
                s.VY = _parameters.RiseSpeed;
                s.IsGrounded = false;
            }
        }

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            ClearVelocityOwnership(ref s);
            s.IsFastFalling = false;
            _ticks++;
            if (_ticks < _parameters.RiseDelay)
                return;

            if (_ticks <= _parameters.RiseDelay + _parameters.RiseTicks)
            {
                s.VY = _parameters.RiseSpeed;
                if (s.IsGrounded)
                    s.IsGrounded = false;
            }
            else if (s.AirTimeTicks < def.Movement.FloatWindowTicks)
            {
                s.AirTimeTicks = def.Movement.FloatWindowTicks;
            }
        }

        internal override void WriteSnapshot(SnapshotWriter w)
        {
            base.WriteSnapshot(w);
            w.WriteUShort(_ticks);
            w.WriteBool(_airborne);
        }

        internal override void ReadSnapshot(SnapshotReader r)
        {
            base.ReadSnapshot(r);
            _ticks = r.ReadUShort();
            _airborne = r.ReadBool();
        }
    }
}
