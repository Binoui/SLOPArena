namespace SlopArena.Shared.Rollback
{
    /// <summary>
    /// The Predictable/Complex ActionState partition (ADR-0011, D9). Predictable states
    /// depend only on fields the wire carries (see CharacterStatePacket's D10 fields) —
    /// PredictedTrack and LocalTrack's correction path may safely re-simulate through them.
    /// Complex states depend on the ServerAbility instance layer and/or SpellResolver's
    /// hitbox/projectile list, neither of which is ever serialized — entities in these
    /// states must never be rebuilt from a snapshot (RawTrack for opponents; LocalTrack
    /// skips its own correction replay through them, see LocalTrack.ReconcileWithServer).
    /// </summary>
    public static class ActionStateClassifier
    {
        public static bool IsPredictable(ActionState state) => state is
            ActionState.Idle or ActionState.JumpSquat or ActionState.Run or
            ActionState.Sliding or ActionState.Crouching or ActionState.Shielding or
            ActionState.ShieldDrop or ActionState.GrabAttempt or ActionState.Throwing or ActionState.Thrown or
            ActionState.AirDodgeMovement or ActionState.AirDodgeRecovery;

        /// <summary>State-aware opponent classification. Hitstop is excluded because
        /// the snapshot omits the live ability and queued-launch payloads that must
        /// remain frozen in the authoritative simulation.</summary>
        public static bool IsPredictable(CharacterState state)
            => state.HitstopTicks == 0 && state.BlockHitstopKind == 0 &&
               state.InteractionId == 0 && IsPredictable(state.State);

        /// <summary>True when the self entity's continuous sim may snap wire fields and replay
        /// through this state (LocalTrack correction). LedgeHang has no ServerAbility instance and
        /// recomputes its ledge from the wire position, so it is snap-safe in both directions even
        /// though it is NOT Predictable for opponents (occupancy is a multi-entity server decision).</summary>
        public static bool IsSnapSafe(ActionState state)
            => IsPredictable(state) || state == ActionState.LedgeHang;

        /// <summary>State-aware local correction classification. Hitstop suffixes are
        /// never rebuilt from an incomplete packet.</summary>
        public static bool IsSnapSafe(CharacterState state)
            => state.HitstopTicks == 0 && state.BlockHitstopKind == 0 &&
               state.InteractionId == 0 && IsSnapSafe(state.State);
    }
}
