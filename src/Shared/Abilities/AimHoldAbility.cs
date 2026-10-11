using System;

namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// Shared base for hold-to-aim, release-to-fire abilities (AbilityBehavior.AimedProjectile
    /// family: Manki Q/E/R. Owns the whole hold lifecycle so the
    /// per-ability classes are just projectile spawners:
    ///
    ///   - OnStart: Aiming state (hold = aim stance), IsAiming, AnimLockTicks,
    ///     ChargeTicks reset, and the aerial ascent-stop. ServerSimulation.ActivateAbility
    ///     only cancels DOWNWARD VY and re-opens the zero-g float window (AirTimeTicks=0),
    ///     so without zeroing VY here an aim cast mid-jump-rise would climb through the
    ///     float.
    ///     Simulation's movement gate reads the explicit aim movement policy. Fixed-policy
    ///     holds stop horizontal momentum; mobile-policy holds retain normal movement.
    ///   - Aim phase: 8-tick debounce, optional mid-hold anim swap, manual release
    ///     (!IsAiming) or auto-release at the positive hold cap. A cap of zero is unlimited.
    ///     The cap reads the SPEC's ChargeHoldTicks — the same field Simulation.cs clamps
    ///     so the auto-release can't drift from the clamp.
    ///   - Throw phase: fire once at throw_trigger_tick (IsAiming=false) in the
    ///     Attacking state, then end at throw_duration.
    ///
    /// Subclasses only implement the hooks. MankiBazooka keeps its own
    /// three-phase FSM; directional dash capabilities own their separate
    /// charge, travel, and recovery lifecycles.
    /// </summary>
    public abstract class AimHoldAbility : ServerAbility
    {
        private bool _fired;

        /// <summary>Max hold ticks before auto-release. Spec ChargeHoldTicks (see class doc).</summary>
        protected virtual ushort GetMaxHoldTicks(CharacterDefinition def)
            => def.GetSlotAbility(Slot, airborne: false)?.ChargeHoldTicks ?? 180;

        /// <summary>AnimLockTicks during the aim hold.</summary>
        protected virtual ushort GetHoldLockTicks(CharacterDefinition def) => 8;

        /// <summary>AnimIndex to swap to after the 8-tick debounce while holding, or -1 for none.</summary>
        protected virtual int GetMidHoldAnimIndex(CharacterDefinition def) => -1;

        /// <summary>AnimIndex for the throw phase (set on release).</summary>
        protected virtual byte GetReleaseAnimIndex(CharacterDefinition def) => 1;

        /// <summary>Called at the very start (after boilerplate). Reset per-cast fields here.</summary>
        protected virtual void OnAimStart(ref CharacterState s, CharacterDefinition def) { }

        /// <summary>
        /// Called each tick while the aim is held (input.IsAiming). Subclasses use
        /// this to track the live aim (cursor/camera) so the throw uses the aim at
        /// RELEASE — the release tick's input may carry zeroed aim values.
        /// </summary>
        protected virtual void OnAimTick(ref CharacterState s, CharacterDefinition def) { }

        /// <summary>Called once when the hold releases. Cache aim (yaw/pitch/distance) here.</summary>
        protected virtual void OnRelease(ref CharacterState s, CharacterDefinition def) { }

        /// <summary>Called once at throw_trigger_tick to spawn the projectile.</summary>
        protected virtual void OnFire(ref CharacterState s, CharacterDefinition def) { }

        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            _fired = false;

            // Hold = aim stance: the explicit policy controls normal movement while
            // ActionState.Aiming continues to block jump, dash, and other activations.
            s.State = ActionState.Aiming;
            s.AttackSlot = (byte)(Slot + 1);
            AnimIndex = 0;
            s.ComboStage = 0;
            s.AttackElapsedTicks = 0;
            s.IsAiming = true;
            s.AnimLockTicks = GetHoldLockTicks(def);
            s.ChargeTicks = 0;

            OnAimStart(ref s, def);
        }

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            ushort maxHoldTicks = GetMaxHoldTicks(def);

            // ── Aim phase ──
            if (s.ComboStage == 0)
            {
                if (input.IsAiming)
                    OnAimTick(ref s, def);
                if (s.AttackElapsedTicks > 8)
                {
                    int mid = GetMidHoldAnimIndex(def);
                    if (mid >= 0 && AnimIndex != mid)
                        AnimIndex = (byte)mid;
                }

                bool released = !input.IsAiming || (maxHoldTicks > 0 && s.ChargeTicks >= maxHoldTicks);
                if (s.AttackElapsedTicks > 8 && released)
                {
                    OnRelease(ref s, def);
                    // Release enters the action phase.
                    s.State = ActionState.Attacking;
                    s.ComboStage = 1;
                    AnimIndex = GetReleaseAnimIndex(def);
                    s.AttackElapsedTicks = 0;
                }
                return;
            }

            // ── Throw phase ──
            ushort throwTick = (ushort)GetParam(def, "throw_trigger_tick", 10f);
            if (!_fired && s.AttackElapsedTicks >= throwTick)
            {
                _fired = true;
                s.IsAiming = false;
                OnFire(ref s, def);
            }

            if (s.AttackElapsedTicks >= (ushort)GetParam(def, "throw_duration", 60f))
                EndAbility(ref s);
        }
    }
}
