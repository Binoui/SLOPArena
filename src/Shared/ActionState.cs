namespace SlopArena.Shared
{
    public enum ActionState : byte
    {
        Idle = 0,
        // Wire code 1 is reserved for the retired universal dash.
        Hitstun = 2,
        Sliding = 3,
        Attacking = 4,
        // Wire code 5 is reserved.
        JumpSquat = 6,
        Warping = 7, // Reserved retired state; keep the numeric wire value.
        /// <summary>Hold-to-aim phase (Wibou E): ability active, movement unlocked, jump/dash blocked.</summary>
        Aiming = 8,
        Run = 9,
        LedgeHang = 10,
        Crouching = 11,
        // Defense states; appended to preserve all existing wire values.
        Shielding = 12,
        ShieldDrop = 13,
        GrabAttempt = 14,
        Grabbed = 15,
        Throwing = 16,
        // Wire code 17 is reserved to preserve the phase wire codes.
        AirDodgeMovement = 18,
        AirDodgeRecovery = 19,
        /// <summary>Grounded capture hold; the grabber can walk until requesting a throw.</summary>
        Grabbing = 20,
        /// <summary>Released victim's throw flight and landing recovery.</summary>
        Thrown = 21,
        /// <summary>Homing dash startup: the fighter hangs in place facing the target.</summary>
        HomingDashFloat = 22,
        /// <summary>Homing dash travel, turning toward the target at a limited rate.</summary>
        HomingDash = 23,
        /// <summary>Arrived next to the target; any action cancels this window.</summary>
        HomingDashArrive = 24,
        /// <summary>The dash ran out without arriving; a locked, punishable end.</summary>
        HomingDashMiss = 25,
        /// <summary>
        /// Tucked in the shell with no move running (ADR-0045): she skates straight on the ground and coasts in
        /// the air. StateTicks is the skate clock (0 is an untimed coast); its expiry pops her up into Idle.
        /// </summary>
        Shell = 26,
        /// <summary>
        /// Caught on a tether (ADR-0048). Paired (phase Reeling), the thrower's line is the only writer of its
        /// position. Delivered, it stays here unpaired for a StateTicks countdown with ordinary gravity, then Idle.
        /// </summary>
        Reeled = 27
    }

    /// <summary>Where a jump that is taking off started; selects its horizontal speed cap.</summary>
    public enum JumpTakeoff : byte
    {
        /// <summary>Capped at RunSpeed.</summary>
        Standing = 0,
        /// <summary>Capped at RunSpeed × <see cref="DownActionTuning.JumpCapRatio"/>.</summary>
        Slide = 1,
        /// <summary>Uncapped: she hops out of the shell keeping her speed.</summary>
        Shell = 2,
    }

    /// <summary>Where a grounded attack's carried momentum came from; selects its entry cap and braking.</summary>
    public enum AttackCarryOrigin : byte
    {
        /// <summary>Capped at the slide entry ratio, brakes at <see cref="DownActionTuning.AttackDecelerationRatio"/>.</summary>
        Slide = 0,
        /// <summary>Capped at the run entry ratio, brakes at <see cref="DownActionTuning.RunAttackDecelerationRatio"/>.</summary>
        Run = 1,
        /// <summary>Uncapped skate speed, brakes at <see cref="CookedShellDefinition.PopOutDeceleration"/>.</summary>
        Shell = 2,
    }

    public static class ActionStates
    {
        /// <summary>
        /// States that admit a new action the way Idle does: a slot press, a jump, a shield, a grab.
        /// Movement processing keeps its own list.
        /// </summary>
        public static bool AdmitsActions(ActionState state)
            => state is ActionState.Idle or ActionState.Run or ActionState.Crouching or ActionState.Sliding
                or ActionState.Shell;

        /// <summary>States that only exist inside a paired interaction; ending the pair frees them.</summary>
        public static bool InPairedInteraction(ActionState state)
            => state is ActionState.Grabbing or ActionState.Grabbed or ActionState.Throwing or ActionState.Reeled;

        /// <summary>Paired states whose fighter skips its own movement step: the partner's pass places it.</summary>
        public static bool MovedByPartner(ActionState state)
            => state is ActionState.Grabbed or ActionState.Throwing or ActionState.Reeled;
    }
}
