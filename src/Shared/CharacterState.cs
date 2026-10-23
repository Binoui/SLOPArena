namespace SlopArena.Shared
{
    /// <summary>
    /// Complete state of a character at a single tick.
    /// Pure C# — no Godot types. Used by both client prediction
    /// and server authoritative simulation.
    /// All durations are in ticks (1/60s at 60Hz).
    /// </summary>
    public struct CharacterState
    {
        /// <summary>
        /// ── Position & Velocity ──
        /// </summary>
        public float PX, PY, PZ;
        public float VX, VY, VZ;

        /// <summary>
        /// ── Action state machine ──
        /// </summary>
        public ActionState State;
        /// <summary>
        /// remaining ticks in current state
        /// </summary>
        public ushort StateTicks;
        /// <summary>Remaining vulnerable shield-drop recovery ticks.</summary>
        public ushort ShieldDropTicks;
        /// <summary>Remaining block stun after a successful shield contact.</summary>
        public ushort BlockStunTicks;
        /// <summary>Block-contact hitstop classification; values are DefenseBlockHitstopKind.</summary>
        public byte BlockHitstopKind;
        /// <summary>Air-dodge recovery remaining after landing retains grounded commitment.</summary>
        public ushort AirDodgeRecoveryTicks;
        /// <summary>Current paired interaction phase; values are DefenseInteractionPhase.</summary>
        public byte InteractionPhase;
        /// <summary>Stable server-owned interaction ID; zero means no active interaction.</summary>
        public ulong InteractionId;
        /// <summary>Paired fighter entity ID; zero means no partner.</summary>
        public ulong InteractionPartnerId;
        /// <summary>Authoritative start tick of the current capture or throw phase.</summary>
        public uint InteractionTick;
        /// <summary>Last terminal interaction ID; zero means no terminal result recorded.</summary>
        public ulong LastTerminalInteractionId;
        /// <summary>Authoritative tick at which the last terminal interaction was committed.</summary>
        public uint InteractionTerminalTick;
        /// <summary>Pair heading in signed degrees × 100; steered during the hold, frozen when throwing starts.</summary>
        public short CapturedYaw;
        /// <summary>Match lifecycle state (Waiting, Countdown, Playing, Ended).</summary>
        public MatchState MatchState;

        /// <summary>
        /// ── Resources ──
        /// </summary>
        public ushort DamagePercent;    // 0-999, Smash-style % (increases when hit, knockback scales with it)
        public byte JumpsLeft;
        public byte AirDodgesLeft;
        /// <summary>Airborne ticks since the last gravity-window reset; selects ordinary
        /// FloatWindow gravity or the brief launch pop window, including during hitstun.</summary>
        public ushort AirTimeTicks;
        /// <summary>
        /// Consecutive ticks the jump key has been held (issue #116 / ADR-0016). Reset when
        /// <c>InputState.JumpHeld</c> drops. Drives the short-hop decision at JumpSquat expiry:
        /// releasing within <c>Simulation.ShortHopWindowTicks</c> produces a reduced jump.
        /// Serialized so rollback replay of a JumpSquat opponent is byte-identical.
        /// </summary>
        public byte JumpHeldTicks;
        public bool IsGrounded;
        public bool WasAirborneDuringKnockback;

        /// <summary>Airborne from a launch, post-hitstun — launch gravity and horizontal
        /// friction until landing or any action (jump/ability). Replicated
        /// in World Snapshots so reconstruction preserves the flight law.</summary>
        public bool InPostHitstunFlight;
        /// <summary>Latched deliberate fast-fall action, replicated for reconstruction.</summary>
        public bool IsFastFalling;
        /// <summary>True when the current jump originated from a slide.</summary>
        public bool JumpFromSlide;
        /// <summary>True while an opted-in grounded attack brakes residual Run or Slide momentum.</summary>
        public bool SlideAttackCarryActive;
        /// <summary>True after a complete stationary crouch tick.</summary>
        public bool CrouchSettled;
        /// <summary>Captured crouch brace state for a deferred ordinary launch.</summary>
        public bool QueuedCrouchBrace;

        public byte Deaths;              // match death counter, server authority

        /// <summary>Air-dodge direction captured from the stick, or facing when neutral.</summary>
        public float DashDirX, DashDirZ;

        /// <summary>Remaining respawn, ledge, or air-dodge invulnerability ticks.</summary>
        public ushort InvincibilityTicks;

        /// <summary>
        /// ── Combo / Attack ──
        /// </summary>
        public ushort AttackElapsedTicks;  // elapsed ticks since attack start
        /// <summary>Which ability slot this attack uses (1-6). 0 = none.</summary>
        public byte AttackSlot;
        /// <summary>Buffered input slot (general buffer during any lock). 0 = none.</summary>
        public byte BufferedSlot;
        /// <summary>
        /// 0 = none, 1-3 = stage
        /// </summary>
        public byte ComboStage;
        /// <summary>Animation index (into spec's AnimationNames[]) set by server ability. Synced to client.</summary>
        public byte AnimIndex;
        /// <summary>Increments on every ability activation so clients can restart same-slot attack clips.</summary>
        public byte AttackSequence;
        /// <summary>
        /// self-lock from attack (remaining)
        /// </summary>
        public ushort AnimLockTicks;
        /// <summary>
        /// Landing lag lock from landing mid-aerial (issue #125): remaining ticks the
        /// character is planted — no ability input, no jump/dash/burst, no input movement —
        /// after touching ground while an air-started attack whose stage declared
        /// LandingLagTicks is still active (unless the landing tick fell in an auto-cancel
        /// window). Authoritative and serialized so local and remote presentation/rollback
        /// tracks render and replay the same commitment.
        /// Cleared by hitstun (ApplyKnockback) — being hit ends the commitment.
        /// </summary>
        public ushort LandingLagTicks;
        /// <summary>
        /// aimed charge progress (0 = none, >0 = charging)
        /// </summary>
        public ushort ChargeTicks;
        /// <summary>
        /// ── Charge-stock pool (regenerating ability charges, e.g. Wibou Rising Slash) ──
        /// Number of charges currently spent/unavailable (0 = full pool). The pool max
        /// and regen cadence come from the ability spec's "max_charges"/"charge_regen_ticks"
        /// params. Spent charges return only through regen. Server-sim only (not on the wire).
        /// </summary>
        public byte ChargeStockSpent;
        /// <summary>Ticks until the next spent charge regenerates. 0 when nothing is regenerating.</summary>
        public ushort ChargeStockRegenTicks;
        /// <summary>Full regen period for one charge, cached from the ability spec at spend time.</summary>
        public ushort ChargeStockRegenPeriod;

        /// <summary>
        /// ── Knockback ──
        /// </summary>
        public float KVX, KVY, KVZ;     // knockback velocity (decays separately)

        /// <summary>
        /// ── Hitstun + DI (Directional Influence) ──
        /// </summary>
        /// <summary>Remaining no-input lock ticks after hitstun begins (ADR-0012 renamed the comment — hitstop is the freeze).</summary>
        public ushort HitstunTicks;
        /// <summary>Changes when an authoritative launch starts a new hit reaction.</summary>
        public ushort HitReactionSequence;
        /// <summary>Remaining hitstop freeze ticks (ADR-0012). While > 0 the entity is frozen: no state machine, no physics, no timer decrement, no ability ticking.</summary>
        public ushort HitstopTicks;
        /// <summary>Queued launch payload, set at hit connect (ResolveHits), applied at freeze expiry (SimulateTick gate). Server + local-sim only — NOT on the wire.</summary>
        public float QueuedKBDirX, QueuedKBDirZ;
        public sbyte QueuedKBAngle;
        public float QueuedKBBase, QueuedKBGrowth, QueuedKBDamage;
        public float QueuedKBForce;
        public ushort QueuedKBStun;
        /// <summary>Fixed hitstun to apply with the queued hit launch; zero uses the formula.</summary>
        public ushort QueuedKBFixedHitstunTicks;
        /// <summary>Authored stun gate preserved separately from hook-resolved queued stun.</summary>
        public ushort QueuedKBStunGate;
        /// <summary>Local-only marker: this hit added hitstop without adding a reaction.</summary>
        public bool QueuedArmorHitstop;
        /// <summary>True when the hit's OnHitEntity supplied a complete direct launch force.</summary>
        public bool QueuedKBResolvedForce;
        /// <summary>True when the resolved hit intentionally has zero launch magnitude.</summary>
        public bool QueuedKBZero;
        /// <summary>Hitstun animation tier: 0=small, 1=medium, 2=hard. Set at hit time.</summary>
        public byte HitstunLevel;
        /// <summary>
        /// accumulated DI input during hitstun
        /// </summary>
        public float DIX, DIY;
        /// <summary>Server/local-only marker for the one-shot hitstop SDI.</summary>
        public bool SdiApplied;

        /// <summary>
        /// ── Facing ──
        /// </summary>
        public float FacingYaw;          // radians, +Z = 0
        /// <summary>Combat aim yaw in radians — sent by client, used for hitboxes/hurtboxes.</summary>
        public float AimYaw;
        /// <summary>Target distance for projectile aim (meters). Set from InputState.AimDistance each tick.</summary>
        public float AimTargetDistance;
        /// <summary>Combat aim pitch in radians — sent by client, used for projectile direction.</summary>
        public float AimPitch;

        /// <summary>Committed attack-pose pitch, independent of raw camera aim.</summary>
        public float AttackPosePitch;
        public float AttackCorrectionStartYaw;
        public ulong AttackCorrectionTargetId;
        public byte AttackCorrectionTargetDeaths;
        public bool AttackCorrectionActive;
        /// <summary>This activation owns facing even after its correction window closes.</summary>
        public bool AttackCorrectionOwned;

        /// <summary>Clears attack-owned fields without changing the successor action, reactions or velocities.</summary>
        public void ClearAttackFields()
        {
            AttackSlot = 0;
            ComboStage = 0;
            AttackElapsedTicks = 0;
            AnimLockTicks = 0;
            BufferedSlot = 0;
            ChargeTicks = 0;
            IsAiming = false;
            AnimIndex = 0;
            SlideAttackCarryActive = false;
            ClearStartupAimCorrection();
        }

        internal void ClearStartupAimCorrection()
        {
            AttackPosePitch = 0f;
            AttackCorrectionStartYaw = 0f;
            AttackCorrectionTargetId = 0;
            AttackCorrectionTargetDeaths = 0;
            AttackCorrectionActive = false;
            AttackCorrectionOwned = false;
        }

        /// <summary>True while player is holding an aim-to-fire ability (RMB charge, Q throw).</summary>
        public bool IsAiming;

        /// <summary>
        /// ── Rush / ledge ──
        /// </summary>
        /// <summary>
        /// Rush window remaining ticks. Counts down only while the fighter is purely
        /// running in one direction on the ground. Any other action freezes it; landings
        /// refill it. A reversal is always an instant full-speed flip; zero only means
        /// the Rush kick-off window has expired.
        /// </summary>
        public ushort RushTicks;
        /// <summary>Reserved ledge re-grab timer; retained in the versioned state packet
        /// while automatic ledge grabs are disabled.</summary>
        public ushort LedgeRegrabLockTicks;

        /// <summary>
        /// ── Last input direction (for tech roll, air dodge fallback) ──
        /// </summary>
        public float LastDirX, LastDirZ;

        /// <summary>
        /// ── Entity ID (0 = unassigned) ──
        /// </summary>
        public ulong EntityId;
        /// <summary>
        /// ── Targeting ──
        /// </summary>
        /// <summary>Soft-lock target entity ID. 0 = none.</summary>
        public ulong TargetEntityId;
        /// <summary>
        /// Persistent target lock state, set by explicit toggle or automatic policy.
        /// </summary>
        public bool LockOn;
        /// <summary>
        /// An explicit toggle-off suppresses automatic reacquisition until a manual
        /// toggle-on or retarget request. Rollback state packet carries this flag.
        /// </summary>
        public bool AutoLockSuppressed;

        /// <summary>
        /// <summary>
        /// Per-slot cooldown ticks (11 slots — ADR-0016; slots 6-10 have no kit data yet).
        /// </summary>
        public ushort Cooldown0, Cooldown1, Cooldown2, Cooldown3, Cooldown4, Cooldown5,
            Cooldown6, Cooldown7, Cooldown8, Cooldown9, Cooldown10;

        /// <summary>
        /// Wire slot whose returning projectile (Sir Bonk's pot lid) is still out, 0 when none. Mirrored
        /// from the hitboxes at the end of each tick for the CPU and the HUD; the press gate reads the hitboxes.
        /// </summary>
        public byte ReturningProjectileSlot;

        /// <summary>Cooldown for an ActiveSlot value (1-11), 0 for out-of-range.</summary>
        public ushort GetCooldown(byte activeSlot) => activeSlot switch
        {
            AbilitySlots.Lmb => Cooldown0,
            AbilitySlots.Rmb => Cooldown1,
            AbilitySlots.Slot1 => Cooldown2,
            AbilitySlots.E => Cooldown3,
            AbilitySlots.R => Cooldown4,
            AbilitySlots.F => Cooldown5,
            AbilitySlots.Slot2 => Cooldown6,
            AbilitySlots.Slot3 => Cooldown7,
            AbilitySlots.Slot4 => Cooldown8,
            AbilitySlots.Slot5 => Cooldown9,
            AbilitySlots.A => Cooldown10,
            _ => 0,
        };

        /// <summary>Set cooldown for an ActiveSlot value (1-11); out-of-range is ignored.</summary>
        public void SetCooldown(byte activeSlot, ushort ticks)
        {
            switch (activeSlot)
            {
                case AbilitySlots.Lmb: Cooldown0 = ticks; break;
                case AbilitySlots.Rmb: Cooldown1 = ticks; break;
                case AbilitySlots.Slot1: Cooldown2 = ticks; break;
                case AbilitySlots.E: Cooldown3 = ticks; break;
                case AbilitySlots.R: Cooldown4 = ticks; break;
                case AbilitySlots.F: Cooldown5 = ticks; break;
                case AbilitySlots.Slot2: Cooldown6 = ticks; break;
                case AbilitySlots.Slot3: Cooldown7 = ticks; break;
                case AbilitySlots.Slot4: Cooldown8 = ticks; break;
                case AbilitySlots.Slot5: Cooldown9 = ticks; break;
                case AbilitySlots.A: Cooldown10 = ticks; break;
            }
        }
    }
    /// <summary>Paired grab/capture/throw phase replicated as part of CharacterState.</summary>
    public enum DefenseInteractionPhase : byte
    {
        None = 0,
        Attempt = 1,
        Captured = 2,
        Throwing = 3,
        Terminal = 4
    }

    /// <summary>Hitstop reason for defensive block contacts.</summary>
    public enum DefenseBlockHitstopKind : byte
    {
        None = 0,
        ShieldContact = 1
    }
}
