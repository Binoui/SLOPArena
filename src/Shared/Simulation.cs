using System;

#nullable enable

namespace SlopArena.Shared
{
    /// <summary>
    /// Pure C# simulation of one tick of game logic.
    /// No Godot dependencies — usable by Server, Client, and AI.
    ///
    /// Architecture:
    ///   SimulateTick() processes ONE tick (1/60s) of movement + combat
    ///   for a single character. It takes the current CharacterState,
    ///   mutates it to the next tick.
    ///
    ///   Hit detection uses SpellResolver (Shared/) — pure math.
    ///
    /// Usage (client/server): SimulateTick(ref state, def, input, arena, out ordinaryActionOpportunity,
    ///   out movementActionAccepted, DownActionTuning.Default, verticalMotionOwned).
    /// </summary>
    public static class Simulation
    {
        /// <summary>Input buffer window in ticks. Inputs within this many frames of unlock are buffered.</summary>
        public const ushort InputBufferWindow = 6;
        private static int _logCounter;
        /// <summary>Hook for debug logging. Set by the client to receive sim trace messages.</summary>
        public static System.Action<string>? OnDebugLog;
        public const float TickDt = 1f / 60f;
        [ThreadStatic]
        private static int[]? _triangleCandidates;

        private static int[] TriangleCandidates(in ArenaDefinition arena)
        {
            int required = arena.CollisionTriangles?.Length ?? 0;
            if (required <= 0) return Array.Empty<int>();
            if (_triangleCandidates == null || _triangleCandidates.Length < required)
                _triangleCandidates = new int[required];
            return _triangleCandidates;
        }


        /// <summary>
        /// ── Constants ──
        /// </summary>
        /// <summary>
        /// Exponential knockback decay rate λ (per second). Applied every tick while
        /// knockback velocity is alive: KV *= exp(-λ·dt). Frontloaded, DKO-style —
        /// the launch is fastest right after the hit and smoothly slows, so most travel
        /// happens early and the victim drifts in the tail. Decaying all axes also
        /// flattens launch arcs (less vertical hang).
        /// Velocity halves every ln(2)/λ seconds (~0.39 s at λ=1.8), so a kill-level
        /// launch is roughly half speed by the end of a 22-tick hitstun.
        /// Tune this constant to adjust global knockback travel distance/shape.
        /// </summary>
        private const float KnockbackDecayRate = 1.8f;
        /// <summary>
        /// Gravity applied to vertical knockback velocity each tick (units/s²).
        /// Small so launches read as launches; the exponential decay does the braking.
        /// </summary>
        private const float KnockbackMinGravity = 2.0f;

        /// <summary>Brief upward launch grace, independent of the action lock. Hitstop
        /// does not advance AirTimeTicks; downward and flat sends receive gravity immediately.</summary>
        public const ushort KnockbackPopTicks = 4;

        // Launch gravity applies after KnockbackPopTicks, including during hitstun.
        // Flight gravity 14 m/s² + horizontal friction 6 m/s². Gentler braking retains
        // outward travel with the shorter arcs produced by gravity during hitstun.
        // Horizontal friction applies after hitstun until landing or an accepted action.
        // Hardcoded, not MovementStats — the balance pass can promote them. Public so
        // tools/MoveDataReport prints the live values.
        public const float FlightGravity = 14f;
        public const float FlightFriction = 6f;

        // ADR-0019 balance pass (2026-08-17, melee-shape adoption): velocity-only scale on
        // the damage/weight formula. Hitstun is computed from the UNSCALED magnitude so combo
        // timing (stun vs IASA, the combo matrix) is preserved while launch distance shrinks.
        // Tune with tools/MoveDataReport: scripts/move-data.sh fightguy (or --kbm model presets).
        // 1.0 = raw formula.
        // Shipped values = the melee-soft profile (issue #149, 2026-08-18): stun 0.45×mag, no
        // floor — 0% hits barely stun (no free true combos; combos emerge with damage %) — and
        // KV ×0.17 (launch reads as a pop, not a glide). A/B-validated vs melee/melee-hot: all
        // free true-combo edges removed at a slightly softer launch than the raw melee shape.
        public static float KbScaleFactor = 0.17f;

        // KB-tuning lab knobs (read by the move-data tool's --kbm presets). Hitstun ticks =
        // StunCoefficient × (raw magnitude + MagBonus), min 1 — MagBonus is the Melee-style
        // "+18 floor" lever (damage-independent stun at low %), StunCoefficient the
        // stun-per-launch ratio.
        public static float HitstunStunCoefficient = 0.45f;
        public static float HitstunMagBonus = 0f;
        private const byte MaxAirDodges = 1;

        /// <summary>
        /// Short-hop release window in ticks (issue #116 / ADR-0016): releasing the jump key
        /// within this many ticks of the press produces a reduced jump. Digital-optimal timing
        /// tech; tune 3-5 in playtest per ADR-0016.
        /// </summary>
        public const byte ShortHopWindowTicks = 5;

        /// <summary>
        /// Horizontal speed dead zone. Below this, velocity is snapped to zero
        /// to prevent residual drifting from asymptotic friction decay.
        /// </summary>
        private const float VelocityDeadZone = 0.015f;
        /// <summary>
        /// Release brake on the ground (ADR-0020): how fast a Run decelerates to zero once
        /// input is released. A Rush release stops instantly (no drift at all).
        /// </summary>
        private const float GroundStopFriction = 36f;

        /// <summary>
        /// Tolerance for snapping to platform surfaces (units).
        /// Characters must be within this window above the surface to snap.
        /// resolution on what counts as a traversable step for movement abilities.
        /// </summary>
        public const float PlatformSnapTolerance = 0.5f;
        /// <summary>
        /// How far above the surface the character can be and still land.
        /// Must be small enough that a jump (VY ≈ 10) immediately breaks it in 1-2 frames.
        /// </summary>
        private const float PlatformLandTolerance = 0.1f;

        /// <summary>
        /// Horizontal search radius for ledge grab (meters).
        /// 0.8m ≈ character width — avoids magnetic pull across small platforms.
        /// </summary>
        private const float LedgeSnapRange = 0.8f;
        /// <summary>
        /// Max Y below surface edge to grab.
        /// Prevents grab from deep below the stage.
        /// </summary>
        private const float LedgeGrabTolerance = 2.5f;
        /// <summary>Invincibility ticks granted on a ledge grab.</summary>
        private const ushort LedgeRegrabLockDurationTicks = 30;
        private const float LedgeDropSpeed = 3f;

        /// Resolve the effective AttackStage from an AbilitySpec, clamping ComboStage.
        public static AttackStage ResolveStage(AbilitySpec spec, in CharacterState state)
        {
            int stageIdx = Math.Min(state.ComboStage, spec.Stages.Length - 1);
            return spec.Stages[stageIdx];
        }

        /// <summary>
        /// IASA early-out (issue #124 / ADR-0021 §1): true when the current attack's stage
        /// has passed its <c>IasaTicks</c>. From that tick on, ability inputs AND the dash
        /// interrupt the recovery (the jab → IASA → dash → dash-attack string). 0 = none
        /// (full ADR-0014 lock — the pre-IASA behavior). Never true outside Attacking.
        /// </summary>
        internal static bool IsIasaUnlocked(CharacterState state, CharacterDefinition def)
        {
            if (state.State != ActionState.Attacking || state.AttackSlot == AbilitySlots.None)
                return false;
            var cooked = def.GetCookedSlotAbility(state.AttackSlot, !state.IsGrounded);
            if (cooked == null) return false;
            var stageIndex = Math.Min(state.ComboStage, (byte)(cooked.Timeline.Stages.Count - 1));
            var stage = cooked.Timeline.Stages[stageIndex];
            if (stage.IasaTicks == 0) return false;
            var elapsed = state.AttackElapsedTicks;
            for (var i = 0; i < stageIndex; i++) elapsed -= cooked.Timeline.Stages[i].DurationTicks;
            return elapsed >= stage.IasaTicks;
        }


        // ── MAIN ENTRY POINT ──

        /// <summary>
        /// Process one simulation tick for a character.
        /// Mutates state in-place.
        /// </summary>
        public static void SimulateTick(
            ref CharacterState s,
            CharacterDefinition def,
            InputState input,
            ArenaDefinition arena,
            out bool ordinaryActionOpportunity,
            out bool movementActionAccepted,
            DownActionTuning tuning,
            bool verticalMotionOwned,
            float gravityMultiplier = 1f)
        {
            _lastDownAdmissionReason = null;
            ordinaryActionOpportunity = false;
            movementActionAccepted = false;
            if (s.InteractionId != 0
                && s.State is (ActionState.Grabbed or ActionState.Throwing))
                return;

            var stats = def.Movement;
            bool wasGrounded = s.IsGrounded;   // airborne→grounded detection for the Rush reset
            bool blockStunLocked = s.BlockStunTicks > 0;
            bool slotInputBlocked = s.LandingLagTicks > 0 || IsDefenseActionState(s.State);

            if (verticalMotionOwned)
                ClearMovementInterruptionFlags(ref s);
            bool canFastFallEdge = !wasGrounded
                && s.VY < 0f
                && input.DownPressed
                && !verticalMotionOwned
                && !blockStunLocked
                && s.HitstopTicks == 0
                && s.HitstunTicks == 0
                && !HasKnockback(s)
                && s.State != ActionState.LedgeHang
                && s.State != ActionState.JumpSquat
                && !IsDefenseActionState(s.State)
                && s.WarpSpeed <= 0f;


            // Preserve the last committed aim when a neutral input omits aim data.
            if (input.AimYaw != 0 || input.IsAiming || input.FaceToCamera)
            {
                float aimDeg = input.AimYaw * 0.01f;
                s.AimYaw = aimDeg * (MathF.PI / 180f);
            }
            // Store aim target distance (cm → m) for projectile abilities
            s.AimTargetDistance = input.AimDistance * 0.01f;
            s.AimPitch = input.AimPitch * 0.01f * (MathF.PI / 180f);

            // ── Hitstop (ADR-0012): per-pair freeze. While frozen, capture the defender's
            // Combo Influence input, decrement, and skip the state machine, timers, and physics.
            // The launch queued at hit connect applies at freeze expiry.
            if (s.HitstopTicks > 0)
            {
                // Block contact freezes the defender but never queues a launch,
                // directional influence, or a damaging hit reaction.
                if (s.BlockHitstopKind == (byte)DefenseBlockHitstopKind.ShieldContact)
                {
                    s.HitstopTicks--;
                    if (s.HitstopTicks == 0)
                        s.BlockHitstopKind = (byte)DefenseBlockHitstopKind.None;
                    return;
                }
                if (!s.QueuedArmorHitstop && (input.MoveX != 0f || input.MoveY != 0f))
                {
                    if (!s.SdiApplied)
                    {
                        ApplySdi(ref s, input.MoveX, input.MoveY, def, arena);
                        s.SdiApplied = true;
                    }
                    s.DIX = input.MoveX;
                    s.DIY = input.MoveY;
                }
                s.HitstopTicks--;
                if (s.HitstopTicks == 0)
                {
                    bool queuedCrouchBrace = s.QueuedCrouchBrace;
                    if (s.QueuedKVOverride)
                    {
                        // OnHitEntity rewrote the launch at connect (NetherGrasp yank):
                        // restore the exact snapshot — KV was untouched during the freeze.
                        s.KVX = s.QueuedKVX; s.KVY = s.QueuedKVY; s.KVZ = s.QueuedKVZ;
                        float kvMag = MathF.Sqrt(
                            (s.KVX * s.KVX) + (s.KVY * s.KVY) + (s.KVZ * s.KVZ));
                        if (s.QueuedKBStun > 0 && kvMag > 0f)
                        {
                            // OnHitEntity rewrote the launch (NetherGrasp yank — a fixed
                            // tool): like applyScale:false, it takes the stun coefficient
                            // but NOT the +MagBonus launch floor, so yanks don't over-pull.
                            s.HitstunTicks = (ushort)Math.Clamp(
                                (int)(HitstunStunCoefficient * kvMag), 1, ushort.MaxValue);
                            s.HitstunLevel = s.HitstunTicks <= 30 ? (byte)0 :
                                s.HitstunTicks <= 50 ? (byte)1 : (byte)2;
                            s.State = ActionState.Hitstun;
                        }
                        else
                        {
                            s.HitstunTicks = 0;
                            s.State = ActionState.Idle;
                        }
                        if (s.KVY > 0f) s.IsGrounded = false;
                        s.AirTimeTicks = 0;
                        s.DashDurationTicks = 0;
                        s.AirDodgeRecoveryTicks = 0;
                        s.StateTicks = 0;
                        s.WasAirborneDuringKnockback = !s.IsGrounded;
                        s.InPostHitstunFlight = false;
                        ApplyFixedHitstun(ref s, s.QueuedKBFixedHitstunTicks, s.QueuedKBStunGate);
                    }
                    else if (s.QueuedKBResolvedForce)
                    {
                        ApplyKnockbackForce(ref s, s.QueuedKBDirX, s.QueuedKBDirZ,
                            s.QueuedKBAngle, s.QueuedKBForce, s.QueuedKBStun);
                        ApplyFixedHitstun(ref s, s.QueuedKBFixedHitstunTicks, s.QueuedKBStunGate);
                    }
                    else if (!s.QueuedKBZero && (s.QueuedKBBase != 0f || s.QueuedKBGrowth != 0f || s.QueuedKBDamage != 0f || s.QueuedKBStun > 0))
                    {
                        ApplyKnockback(ref s, s.QueuedKBDirX, s.QueuedKBDirZ, s.QueuedKBAngle,
                            s.QueuedKBBase, s.QueuedKBGrowth, s.QueuedKBDamage,
                            s.QueuedKBStun, def.Weight);
                        ApplyFixedHitstun(ref s, s.QueuedKBFixedHitstunTicks, s.QueuedKBStunGate);
                        ApplyCrouchBrace(ref s, queuedCrouchBrace, tuning.CrouchLaunchMultiplier);
                    }
                    else if (!s.QueuedArmorHitstop)
                    {
                        // Zero-launch queue = the ATTACKER frozen by their own connecting hit.
                        // Leave state and movement untouched when an armored hit added only freeze.
                        s.KVX = 0f; s.KVY = 0f; s.KVZ = 0f;
                        s.HitstunTicks = 0;
                    }
                    if (!s.QueuedArmorHitstop)
                        ApplyDirectionalInfluence(ref s);
                    s.SdiApplied = false;
                    s.QueuedKVOverride = false;
                    s.QueuedKBZero = false;
                    s.QueuedCrouchBrace = false;
                    s.QueuedKVX = 0f; s.QueuedKVY = 0f; s.QueuedKVZ = 0f;
                    s.QueuedKBDirX = 0f; s.QueuedKBDirZ = 0f; s.QueuedKBAngle = 0;
                    s.QueuedKBFixedHitstunTicks = 0;
                    s.QueuedKBStunGate = 0;
                    s.QueuedArmorHitstop = false;
                    s.QueuedKBBase = 0f; s.QueuedKBGrowth = 0f; s.QueuedKBDamage = 0f; s.QueuedKBForce = 0f; s.QueuedKBResolvedForce = false; s.QueuedKBStun = 0;
                    s.BlockHitstopKind = (byte)DefenseBlockHitstopKind.None;
                }
                return;
            }

            // A slide-carry attack keeps its live horizontal vector, but loses a fixed
            // amount of magnitude before this tick's collision/integration pass. Hitstop
            // returned above, so frozen attack ticks never consume carry.
            if (s.SlideAttackCarryActive
                && s.AttackSlot > 0
                && (s.State == ActionState.Attacking || s.State == ActionState.Aiming))
            {
                DecayHorizontalSpeed(ref s,
                    DownActionTuning.AttackDecelerationRatio * stats.RunSpeed * TickDt);
            }


            // Short-hop hold counter (issue #116): consecutive ticks the jump key is held.
            // Reset on release. Serialized on the wire so rollback replay of a JumpSquat
            // opponent is byte-identical (ADR-0011).
            if (input.JumpHeld && s.JumpHeldTicks < 255) s.JumpHeldTicks++;
            else s.JumpHeldTicks = 0;

            // 2.5 JumpSquat: tick down, apply jump force on expiry
            if (s.State == ActionState.JumpSquat)
            {
                if (s.StateTicks > 0) s.StateTicks--;
                if (s.StateTicks == 0)
                {
                    // Short-hop decision (issue #116 / ADR-0016): releasing within
                    // ShortHopWindowTicks of the press yields a reduced jump. If the player
                    // is STILL holding inside the window at squat expiry, the decision is
                    // pending — hold the squat one tick at a time until the release (short)
                    // or the window elapses (full). The deferral is bounded by the window.
                    bool withinWindow = s.JumpHeldTicks <= ShortHopWindowTicks;
                    if (input.JumpHeld && withinWindow)
                    {
                        // decision pending — stay in squat, re-check next tick
                    }
                    else
                    {
                        float force = withinWindow
                            ? stats.ShortHopForce
                            : stats.JumpForce;
                        s.VY = force;
                        s.IsGrounded = false;
                        s.State = ActionState.Idle;
                        s.AirTimeTicks = stats.FloatWindowTicks;
                        float jumpCap = stats.RunSpeed * (s.JumpFromSlide ? DownActionTuning.JumpCapRatio : 1f);
                        ClampHorizontalSpeed(ref s, jumpCap);
                        s.JumpFromSlide = false;
                    }
                // During squat: preserve horizontal momentum, no acceleration
            }
            }
            var activeCookedSlot = def.GetCookedSlotAbility(s.AttackSlot, !s.IsGrounded);
            s.IsAiming = input.IsAiming && !(activeCookedSlot?.Behavior == AuthoringAbilityBehavior.AimedProjectile
                && activeCookedSlot.AimMode == AuthoringAimMode.None);

            // 1. Tick timers
            ushort rushBeforeMovement = s.RushTicks;
            TickTimers(ref s);
            blockStunLocked |= s.BlockStunTicks > 0;
            if (blockStunLocked)
                s.VX = s.VZ = 0f;
            AdvanceDefenseStates(ref s);

            if (s.State != ActionState.Hitstun && !HasKnockback(s))
            {
                bool movementAllowed = s.HitstunTicks == 0
                    && s.HitstopTicks == 0
                    && !blockStunLocked
                    && s.LandingLagTicks == 0;
                bool mobileAim = s.State == ActionState.Aiming
                    && s.AttackSlot > 0
                    && def.GetAimMovementMode(s.AttackSlot, !s.IsGrounded) == AimMovementMode.Mobile;
                bool ordinaryMovement = movementAllowed
                    && (s.State == ActionState.Idle || s.State == ActionState.Run
                        || s.State == ActionState.Crouching || s.State == ActionState.Sliding
                        || mobileAim);
                bool jump = movementAllowed
                    && s.JumpsLeft > 0
                    && s.AnimLockTicks == 0
                    && s.State != ActionState.JumpSquat
                    && s.State != ActionState.Aiming
                    && s.State != ActionState.LedgeHang
                    && (!IsDefenseActionState(s.State) || s.State == ActionState.Shielding);
                bool dodge = movementAllowed && !s.IsGrounded && s.AirDodgesLeft > 0
                    && CanAcceptAirDodge(in s, blockStunLocked, verticalMotionOwned);
                bool ledgeExit = s.State == ActionState.LedgeHang
                    && FindLedge(s, arena, def.CapsuleHeight * 0.5f,
                        out _, out _, out _, out _, out _);
                ordinaryActionOpportunity = ordinaryMovement || jump || dodge || ledgeExit;
            }

            // 2. Hitstun overrides everything (DI window)
            if (s.State == ActionState.Hitstun)
            {
                ProcessHitstun(ref s, input, arena, def);
                // Fall through — position update + ground collision must run during hitstun.
                // Without this, the target stands perfectly still for the entire stun duration
                // (V=KV set but PX/PZ/PY never updated), then does a single-frame hop on expiry.
            }

            // 3. Knockback overrides everything (but dash invincibility still applies)
            if (s.State != ActionState.Hitstun && HasKnockback(s))
            {
                ProcessKnockback(ref s, arena, def);
                return;
            }

            bool wasLedgeHang = s.State == ActionState.LedgeHang;
            // 4. Warp processing: velocity override during any state
            if (s.WarpSpeed > 0f)
            {
                bool warpComplete = ProcessWarp(ref s, def, arena);
                if (warpComplete)
                {
                    // Warp arrival: velocity cleared, WarpSpeed=0.
                    // Let the ability continue — lunge and hitboxes are still pending.
                    // TickAbilities (called after SimulateTick) handles the rest.
                }
            }
            // Only process state machine if not warping
            else
            {
                if (s.State is ActionState.AirDodgeMovement or ActionState.AirDodgeRecovery)
                    ProcessDefenseAirDodge(ref s, stats);
                else if (s.State == ActionState.LedgeHang)
                    ProcessLedgeHang(ref s, stats, input, arena, def);
            }
            if (wasLedgeHang && s.State == ActionState.JumpSquat)
            {
                movementActionAccepted = true;
                ClearMovementInterruptionFlags(ref s);
            }

            // NOTE: no ground friction during Attacking (issue #115) — attacks preserve
            // drift and lunge momentum; friction resumes when the ability returns to Idle.



            if (input.GrabPressed && s.IsGrounded
                && s.VX * s.VX + s.VZ * s.VZ <= 0.0025f
                && CanAcceptDefenseAction(in s, def, blockStunLocked, verticalMotionOwned))
            {
                StartGrabAttempt(ref s);
                movementActionAccepted = true;
            }

            if (!input.GrabPressed && input.Jump && s.JumpsLeft > 0 && s.AnimLockTicks == 0
                && s.HitstunTicks == 0 && !blockStunLocked && !verticalMotionOwned
                && s.LandingLagTicks == 0 && s.State != ActionState.JumpSquat
                && s.State != ActionState.Aiming && s.State != ActionState.LedgeHang
                && !HasKnockback(s) && !HasQueuedLaunch(s)
                && (!IsDefenseActionState(s.State) || s.State == ActionState.Shielding))
            {
                movementActionAccepted = true;
                if (s.IsGrounded && input.ShieldHeld
                    && s.State is (ActionState.Idle or ActionState.Run or ActionState.Crouching or ActionState.Sliding))
                {
                    // Brake before squat, without carrying a protected frame or slide momentum.
                    s.VX = s.VZ = 0f;
                    s.State = ActionState.Idle;
                    ClearMovementInterruptionFlags(ref s);
                }
                bool jumpFromSlide = s.IsGrounded && s.State == ActionState.Sliding;
                ClearMovementInterruptionFlags(ref s);
                s.ShieldDropTicks = 0;
                if (s.IsGrounded)
                {
                    s.JumpFromSlide = jumpFromSlide;
                    s.State = ActionState.JumpSquat;
                    s.StateTicks = stats.JumpSquatTicks;
                    s.JumpsLeft--;
                }
                else
                {
                    s.VY = stats.JumpForce * stats.AirJumpVMultiplier;
                    (float dirX, float dirZ) = GetInputDirection(input);
                    s.VX += dirX * stats.AirSpeedMax * stats.AirJumpHMultiplier;
                    s.VZ += dirZ * stats.AirSpeedMax * stats.AirJumpHMultiplier;
                    s.JumpsLeft--;
                    s.AirTimeTicks = stats.FloatWindowTicks;
                }
            }
            else if (input.Jump && !input.GrabPressed)
            {
                string reason = s.AnimLockTicks > 0 ? "anim_lock" :
                    s.LandingLagTicks > 0 ? "landing_lag" :
                    s.HitstunTicks > 0 || blockStunLocked ? "hitstun" :
                    s.State == ActionState.JumpSquat ? "already_squatting" :
                    s.JumpsLeft <= 0 ? "no_jumps" : "unknown";
                OnDebugLog?.Invoke($"[JumpBlocked] input.Jump=true but blocked by {reason}");
            }

            if (s.State == ActionState.Shielding && !input.ShieldHeld
                && !blockStunLocked && s.HitstopTicks == 0)
            {
                s.State = ActionState.ShieldDrop;
                s.ShieldDropTicks = DefenseConfig.ShieldDropTicks;
                s.StateTicks = DefenseConfig.ShieldDropTicks;
                s.VX = s.VZ = 0f;
            }
            else if (!input.GrabPressed && !input.Jump)
            {
                if (s.IsGrounded && input.ShieldHeld
                    && CanAcceptDefenseAction(in s, def, blockStunLocked, verticalMotionOwned))
                {
                    if (s.State != ActionState.Shielding)
                    {
                        s.State = ActionState.Shielding;
                        s.ShieldDropTicks = 0;
                        s.StateTicks = 0;
                        ClearMovementInterruptionFlags(ref s);
                        ClearDefenseAttackState(ref s);
                        movementActionAccepted = true;
                    }
                    s.VX = s.VZ = 0f;
                }
                else if (!s.IsGrounded && input.ShieldPressed && s.AirDodgesLeft > 0
                    && CanAcceptAirDodge(in s, blockStunLocked, verticalMotionOwned))
                {
                    StartAirDodge(ref s, stats);
                    movementActionAccepted = true;
                }
            }

            // Only ServerSimulation activates abilities. A press made eligible by this
            // tick's takeoff/timers stays buffered until its next pre-simulation admission.
            // Landing-lag and defense commitment presses are dropped, including expiry.
            if (!slotInputBlocked && !blockStunLocked && !movementActionAccepted
                && input.ActiveSlot > 0 && s.LandingLagTicks == 0 && s.BufferedSlot == 0)
            {
                bool readyAfterTimers = s.AnimLockTicks == 0 && s.HitstunTicks == 0
                    && s.State is (ActionState.Idle or ActionState.Run or ActionState.Crouching or ActionState.Sliding)
                    && s.GetCooldown(input.ActiveSlot) == 0;
                if (readyAfterTimers || s.State == ActionState.JumpSquat
                    || (s.AnimLockTicks > 0 && s.AnimLockTicks <= InputBufferWindow)
                    || (s.HitstunTicks > 0 && s.HitstunTicks <= InputBufferWindow))
                    s.BufferedSlot = input.ActiveSlot;
            }

            // 7. Fixed aim holds stop horizontal movement, including incoming air momentum.
            // Mobile-policy holds retain normal movement control. Attacks own their travel.
            bool fixedAim = s.State == ActionState.Aiming && s.AttackSlot > 0
                && def.GetAimMovementMode(s.AttackSlot, !s.IsGrounded) == AimMovementMode.Fixed;
            if (fixedAim)
                s.VX = s.VZ = 0f;
            // Landing lag (issue #125): "no input, no movement" — the stick cannot steer
            // during the lock, even once the aerial has ended and the state is Idle.
            if (!blockStunLocked && s.LandingLagTicks == 0
                && (s.State == ActionState.Idle || s.State == ActionState.Aiming
                    || s.State == ActionState.Run || s.State == ActionState.Crouching
                    || s.State == ActionState.Sliding))
                ProcessNormalMovement(ref s, stats, input, tuning, movementActionAccepted, processInput: !fixedAim);
            if (verticalMotionOwned && s.IsGrounded
                && (input.Down || input.DownPressed)
                && !movementActionAccepted)
                _lastDownAdmissionReason = DownActionAdmissionReason.MotionOwned;
            // Admission happens after timers: the entry tick belongs to slide too.
            if (s.State == ActionState.Sliding)
                s.RushTicks = rushBeforeMovement;

            // 6c. Facing snap (LMB, ADR-0017 / issue #126): utility input honored at the
            // input gate — instant facing to the camera azimuth (AimYaw), usable when not
            // attack-locked, not in hitstun / landing lag / jump squat / aim stance.
            // Runs AFTER normal movement so an accepted snap wins its input tick. It does
            // not break persistent target lock.
            if (input.FaceToCamera && s.LandingLagTicks == 0 && s.AnimLockTicks == 0
                && s.State != ActionState.Hitstun && s.State != ActionState.JumpSquat
                && s.State != ActionState.Aiming && !IsDefenseActionState(s.State))
            {
                s.FacingYaw = input.AimYaw * 0.01f * (MathF.PI / 180f);
                s.AimYaw = s.FacingYaw;
            }

            // 7b. Charge ticks for aimed projectile abilities (Manki Q, FightGuy Q).
            // ServerAbility subclasses read s.ChargeTicks to check max hold duration.
            if ((s.State is ActionState.Attacking or ActionState.Aiming) && s.AttackSlot > 0 && s.ChargeTicks < ushort.MaxValue)
            {
                var spec = def.GetSlotAbility(s.AttackSlot - 1, !s.IsGrounded);
                if (spec != null && spec.Behavior == AbilityBehavior.AimedProjectile)
                {
                    if (input.IsAiming && s.ChargeTicks < spec.ChargeHoldTicks)
                        s.ChargeTicks++;
                }
            }
            
            if (canFastFallEdge && !movementActionAccepted && !verticalMotionOwned
                && s.HitstopTicks == 0 && s.HitstunTicks == 0 && !blockStunLocked
                && !HasKnockback(s) && s.WarpSpeed <= 0f
                && s.State != ActionState.LedgeHang
                && s.State != ActionState.JumpSquat
                && s.State != ActionState.Warping
                && !IsDefenseActionState(s.State))
            {
                s.IsFastFalling = true;
                s.InPostHitstunFlight = false;
            }

            // 8. Gravity (skip during hitstun — ProcessHitstun handles launch gravity;
            // skip during LedgeHang — a hang is a hang, no gravity, else the character
            // slides down through the FindLedge tolerance window and falls off on its own)
            if (s.State != ActionState.Hitstun && s.State != ActionState.LedgeHang)
                ApplyGravity(ref s, stats, gravityMultiplier);
            
            // 9-10. Authoritative triangle collision, with the old heightmap path kept
            // intact for legacy and synthetic unbaked arenas.
            float capsuleHalf = def.CapsuleHeight * 0.5f;
            float surfaceY = float.MinValue;
            float groundY = float.NaN;
            if (ArenaCollision.HasTriangles(arena))
            {
                MoveThroughStage(ref s, def, arena,
                    s.VX * TickDt, s.VY * TickDt, s.VZ * TickDt);
                if (s.IsGrounded)
                {
                    surfaceY = s.PY - capsuleHalf;
                    groundY = s.PY;
                }
            }
            else
            {
                // 9. Position integration
                s.PX += s.VX * TickDt;
                s.PZ += s.VZ * TickDt;
                s.PY += s.VY * TickDt;

                // 10. Ground collision via heightmap
                surfaceY = arena.Heightmap.Data != null
                    ? arena.Heightmap.Sample(s.PX, s.PZ)
                    : arena.KillHeight + 1f;
                if (surfaceY > float.MinValue)
                {
                    groundY = surfaceY + capsuleHalf;
                    if (s.State == ActionState.Hitstun)
                    {
                        bool atSurface = s.PY <= groundY + PlatformLandTolerance && s.PY >= groundY - PlatformSnapTolerance;
                        if (atSurface && s.KVY <= 0f)
                        {
                            s.IsGrounded = true;
                            s.VY = 0f;
                            s.PY = groundY;
                            s.AirTimeTicks = 0;
                            s.KVY = 0f;
                        }
                        else if (s.PY < groundY - PlatformSnapTolerance)
                        {
                            s.IsGrounded = true;
                            s.VY = 0f;
                            s.KVY = 0f;
                            s.PY = groundY;
                            s.AirTimeTicks = 0;
                            }
                        else
                        {
                            s.IsGrounded = false;
                        }
                    }
                    else if (s.PY <= groundY + PlatformLandTolerance
                        && (s.PY >= groundY - PlatformSnapTolerance || s.PY < groundY))
                    {
                        s.IsGrounded = true;
                        s.VY = 0f;
                        s.PY = groundY;
                        s.AirTimeTicks = 0;
                    }
                    else
                    {
                        s.IsGrounded = false;
                    }
                }
                else
                {
                    s.IsGrounded = false;
                }
            }

            // Walk-off: running off a platform must start falling immediately, not ride
            // the float window (AirFloatGravity = 0 for every class → a full
            // FloatWindowTicks of zero gravity, ~0.5-0.67s of air-running off the ledge).
            // Jumps already pre-set AirTimeTicks = FloatWindowTicks on activation, so only
            // a grounded→airborne transition with no upward velocity and no launch force
            // (knockback rides KVY + its own flight gravity) needs the nudge: leap the
            // float window so full Gravity applies on the very next airborne tick.
            ApplyWalkOffTransition(ref s, wasGrounded, stats.FloatWindowTicks);

            // Landing resets to a fresh Rush window (ADR-0020): the first reversal after
            // landing is an instant dash, not a Turnaround (Melee resets to a dash on land).
            if (!wasGrounded && s.IsGrounded)
            {
                s.RushTicks = stats.RushTicks;
                ClearMovementInterruptionFlags(ref s);
            }

            if (s.State is (ActionState.AirDodgeMovement or ActionState.AirDodgeRecovery)
                && s.IsGrounded)
            {
                if (s.State == ActionState.AirDodgeMovement)
                    s.AirDodgeRecoveryTicks = (ushort)(s.StateTicks + DefenseConfig.AirDodgeRecoveryTicks);
                s.State = ActionState.AirDodgeRecovery;
                s.StateTicks = s.AirDodgeRecoveryTicks;
                if (s.InvincibilityTicks <= DefenseConfig.AirDodgeInvulnerabilityTicks)
                    s.InvincibilityTicks = 0;
                s.VX = s.VZ = 0f;
                RefreshGroundResources(ref s, stats);
            }
            if (s.State == ActionState.Shielding && !s.IsGrounded)
                s.State = ActionState.Idle;


            // DEBUG: log ground collision data (every 60 ticks = ~1/sec per entity)
            if (_logCounter++ % 60 == 0)
                OnDebugLog?.Invoke(
                    $"[SimGround] sY={surfaceY:F3} cH={capsuleHalf:F3} gY={groundY:F3} PY={s.PY:F3} gnd={s.IsGrounded} st={s.State}");
        }

        // ── TIMERS ──

        private static void TickTimers(ref CharacterState s)
        {
            if (s.DashCooldownTicks > 0) s.DashCooldownTicks--;
            if (s.DashDurationTicks > 0) s.DashDurationTicks--;
            if (s.InvincibilityTicks > 0) s.InvincibilityTicks--;
            if (s.AnimLockTicks > 0) s.AnimLockTicks--;
            if (s.LandingLagTicks > 0) s.LandingLagTicks--;
            if (s.HitstunTicks > 0) s.HitstunTicks--;
            if (s.BlockStunTicks > 0) s.BlockStunTicks--;
            if (s.ShieldDropTicks > 0) s.ShieldDropTicks--;
            if (s.State == ActionState.AirDodgeRecovery && s.AirDodgeRecoveryTicks > 0)
                s.AirDodgeRecoveryTicks--;
            if (s.AttackElapsedTicks < ushort.MaxValue) s.AttackElapsedTicks++;

            // Rush window ticks: counts only while purely moving in one direction on the
            // ground. Other actions freeze it; the expired window no longer adds turn lag.
            if (s.RushTicks > 0 && s.IsGrounded && s.State == ActionState.Run)
                s.RushTicks--;
            if (s.LedgeRegrabLockTicks > 0) s.LedgeRegrabLockTicks--;

            // Only a timer that actually expires can end a state. An untimed
            // attack, jump squat, run, or hitstun must not be reset to Idle.
            if (s.StateTicks > 0 && s.State != ActionState.JumpSquat)
            {
                s.StateTicks--;
                if (s.StateTicks == 0 && s.State != ActionState.Idle &&
                    s.State is not (ActionState.Shielding or ActionState.ShieldDrop or
                        ActionState.GrabAttempt or ActionState.AirDodgeMovement or
                        ActionState.AirDodgeRecovery))
                    s.State = ActionState.Idle;
            }


            // Cooldowns (all 11 slots — issue #117; slots 6-10 got fields in #116 but the
            // decrement only covered 1-6, so their cooldowns never expired).
            for (byte slot = 1; slot <= AbilitySlots.Count; slot++)
            {
                ushort cd = s.GetCooldown(slot);
                if (cd > 0) s.SetCooldown(slot, (ushort)(cd - 1));
            }

            // Charge-stock regen (refundable ability pools, e.g. Wibou Rising Slash).
            // Only active when a charge is spent; recovers one charge per regen period.
            if (s.ChargeStockSpent > 0)
            {
                if (s.ChargeStockRegenTicks > 0) s.ChargeStockRegenTicks--;
                if (s.ChargeStockRegenTicks == 0)
                {
                    s.ChargeStockSpent--;
                    if (s.ChargeStockSpent > 0)
                        s.ChargeStockRegenTicks = s.ChargeStockRegenPeriod > 0 ? s.ChargeStockRegenPeriod : (ushort)180;
                }
            }


            // Status timer
            if (s.StatusRemainingTicks > 0)
            {
                s.StatusRemainingTicks--;
                if (s.StatusRemainingTicks == 0)
                    s.StatusFlags = 0;  // clear all statuses when timer expires
            }
        }
        private static bool IsDefenseActionState(ActionState state)
            => state is ActionState.Shielding or ActionState.ShieldDrop or ActionState.GrabAttempt
                or ActionState.Grabbed or ActionState.Throwing
                or ActionState.AirDodgeMovement or ActionState.AirDodgeRecovery;

        private static bool CanAcceptDefenseAction(in CharacterState state, CharacterDefinition def,
            bool blockStunLocked, bool verticalMotionOwned)
        {
            if (verticalMotionOwned || blockStunLocked || state.HitstunTicks > 0 || state.HitstopTicks > 0
                || HasKnockback(state) || HasQueuedLaunch(state)
                || state.AnimLockTicks > 0 && !IsIasaUnlocked(state, def)
                || state.LandingLagTicks > 0 || state.WarpSpeed > 0f)
                return false;

            return state.State is ActionState.Idle or ActionState.Run or ActionState.Crouching
                or ActionState.Sliding or ActionState.Attacking or ActionState.Shielding;
        }

        private static bool CanAcceptAirDodge(in CharacterState state,
            bool blockStunLocked, bool verticalMotionOwned)
            => !verticalMotionOwned && !blockStunLocked
                && state.State is (ActionState.Idle or ActionState.Run)
                && state.AttackSlot == 0 && state.AnimLockTicks == 0
                && state.LandingLagTicks == 0 && state.HitstunTicks == 0
                && state.HitstopTicks == 0 && state.WarpSpeed <= 0f
                && !HasKnockback(state) && !HasQueuedLaunch(state);

        private static void ClearDefenseAttackState(ref CharacterState state)
        {
            state.ClearStartupAimCorrection();
            state.AttackSlot = 0;
            state.ComboStage = 0;
            state.AttackElapsedTicks = 0;
            state.AnimLockTicks = 0;
            state.BufferedSlot = 0;
            state.ChargeTicks = 0;
            state.IsAiming = false;
            state.AnimIndex = 0;
            state.SlideAttackCarryActive = false;
        }

        private static void StartGrabAttempt(ref CharacterState state)
        {
            ClearMovementInterruptionFlags(ref state);
            state.State = ActionState.GrabAttempt;
            state.StateTicks = (ushort)(DefenseConfig.GrabStartupTicks
                + DefenseConfig.GrabActiveTicks + DefenseConfig.GrabWhiffRecoveryTicks);
            state.InteractionId = 0;
            state.InteractionPartnerId = 0;
            state.InteractionPhase = (byte)DefenseInteractionPhase.Attempt;
            state.InteractionTick = 0;
            state.CapturedYaw = (short)Math.Clamp(
                (int)MathF.Round(state.FacingYaw * (18000f / MathF.PI)), short.MinValue, short.MaxValue);
            state.ShieldDropTicks = 0;
            ClearDefenseAttackState(ref state);
            state.VX = state.VZ = 0f;
        }

        private static void StartAirDodge(ref CharacterState state, MovementStats stats)
        {
            ClearMovementInterruptionFlags(ref state);
            ClearDefenseAttackState(ref state);
            state.DashDirX = MathF.Sin(state.FacingYaw);
            state.DashDirZ = MathF.Cos(state.FacingYaw);
            state.State = ActionState.AirDodgeMovement;
            state.StateTicks = DefenseConfig.AirDodgeMovementTicks;
            state.AirDodgeRecoveryTicks = 0;
            state.InvincibilityTicks = Math.Max(state.InvincibilityTicks,
                DefenseConfig.AirDodgeInvulnerabilityTicks);
            state.AirDodgesLeft--;
            state.VX = state.DashDirX * stats.AirDodgeSpeed;
            state.VZ = state.DashDirZ * stats.AirDodgeSpeed;
        }

        private static void AdvanceDefenseStates(ref CharacterState state)
        {
            if (state.State == ActionState.ShieldDrop && state.ShieldDropTicks == 0)
                state.State = ActionState.Idle;
            else if (state.State == ActionState.GrabAttempt && state.StateTicks == 0)
            {
                state.State = ActionState.Idle;
                state.InteractionPhase = (byte)DefenseInteractionPhase.None;
                state.InteractionId = 0;
                state.InteractionPartnerId = 0;
                state.InteractionTick = 0;
            }
            else if (state.State == ActionState.AirDodgeMovement && state.StateTicks == 0)
            {
                state.State = ActionState.AirDodgeRecovery;
                state.StateTicks = DefenseConfig.AirDodgeRecoveryTicks;
                state.AirDodgeRecoveryTicks = DefenseConfig.AirDodgeRecoveryTicks;
                state.VX = state.VZ = 0f;
            }
            else if (state.State == ActionState.AirDodgeRecovery && state.AirDodgeRecoveryTicks == 0)
            {
                state.State = ActionState.Idle;
                state.StateTicks = 0;
            }
        }

        private static void ProcessDefenseAirDodge(ref CharacterState state, MovementStats stats)
        {
            if (state.State == ActionState.AirDodgeRecovery)
                state.VX = state.VZ = 0f;
            else
            {
                state.VX = state.DashDirX * stats.AirDodgeSpeed;
                state.VZ = state.DashDirZ * stats.AirDodgeSpeed;
            }
        }


        // ── HITSTUN + DI (Directional Influence) ──

        /// <summary>
        /// Process hitstun state: apply knockback immediately (no freeze before flight).
        /// HitstunTicks controls how long the victim can't act (animation lock).
        /// DI input is stored during hitstun and applied when it expires.
        /// </summary>
        private static void ProcessHitstun(ref CharacterState s, InputState input,
            ArenaDefinition arena, CharacterDefinition def)
        {
            // The lock preserves horizontal impulse, but never suspends gravity for its
            // entire duration. Position is integrated by the caller's collision pass.
            s.VX = s.KVX;
            s.VY = s.KVY;
            s.VZ = s.KVZ;
            if (s.VY > 0f) s.IsGrounded = false;

            // On the expiry tick the transfer below hands gravity to ApplyGravity.
            // Advance once per tick, including locks shorter than the pop window.
            if (s.HitstunTicks > 0 && !s.IsGrounded)
            {
                ApplyLaunchGravity(ref s.KVY, ref s.AirTimeTicks, def.Movement.MaxFallSpeed);
                s.VY = s.KVY;
            }

            if (input.MoveX != 0f || input.MoveY != 0f)
            {
                s.DIX = input.MoveX;
                s.DIY = input.MoveY;
            }

            if (s.HitstunTicks == 0)
            {
                if (ArenaCollision.HasTriangles(arena))
                    MoveThroughStage(ref s, def, arena, s.DIX * 0.4f, 0f, s.DIY * 0.4f);
                else
                {
                    s.PX += s.DIX * 0.4f;
                    s.PZ += s.DIY * 0.4f;
                }
                s.DIX = 0f;
                s.DIY = 0f;
                s.SdiApplied = false;
                s.VX = s.KVX;
                s.VY = s.KVY;
                s.VZ = s.KVZ;
                s.KVX = 0f;
                s.KVY = 0f;
                s.KVZ = 0f;
                s.InPostHitstunFlight = true;
                s.State = ActionState.Idle;
            }
        }

        /// <summary>
        /// Moves one character through triangle geometry. The remaining displacement is
        /// projected against each contact, so walls slide and corners settle without
        /// tunnelling. Both ordinary and knockback velocities receive the same projection.
        /// </summary>
        internal static bool MoveThroughStage(ref CharacterState s, CharacterDefinition def,
            in ArenaDefinition arena, float dx, float dy, float dz)
        {
            if (!ArenaCollision.HasTriangles(arena))
            {
                s.PX += dx; s.PY += dy; s.PZ += dz;
                return false;
            }

            bool wasGrounded = s.IsGrounded;
            bool followingGround = wasGrounded && MathF.Abs(dy) <= 0.000001f;
            bool followedSlope = false;

            int[] candidates = TriangleCandidates(in arena);
            ArenaCollision.RecoverCapsule(ref s.PX, ref s.PY, ref s.PZ,
                def.CapsuleRadius, def.CapsuleHeight, in arena, candidates);
            // A grounded capsule follows an upward support plane for horizontal travel.
            // The support query is the gate: edge normals from a nearby triangle do not
            // invent a slope, while an interior ramp normal gives continuous uphill and
            // downhill movement without weakening wall contacts.
            if (followingGround
                && dx * dx + dz * dz > 0.000001f
                && ArenaCollision.TryFindSupport(s.PX, s.PY, s.PZ,
                    def.CapsuleRadius, def.CapsuleHeight, in arena, candidates, out var support)
                && support.NormalY > 0.5f
                && ArenaCollision.IsTriangleInteriorContact(s.PX, s.PY, s.PZ,
                    def.CapsuleRadius, def.CapsuleHeight, in arena, in support)
                && ArenaCollision.TryGetTriangleNormal(support.TriangleIndex, in arena,
                    out float faceX, out float faceY, out float faceZ))
            {
                float normalAlignment = support.NormalX * faceX
                    + support.NormalY * faceY + support.NormalZ * faceZ;
                float horizontalNormalSq = faceX * faceX + faceZ * faceZ;
                // A closest-point edge normal is not a walkable support plane. Requiring
                // alignment with the triangle face keeps platform perimeters blocking.
                if (normalAlignment > 0.99999f && horizontalNormalSq > 0.000001f)
                {
                    float normalMotion = dx * faceX + dy * faceY + dz * faceZ;
                    dx -= faceX * normalMotion;
                    dy -= faceY * normalMotion;
                    dz -= faceZ * normalMotion;
                    followedSlope = true;
                }
            }

            float remainingX = dx, remainingY = dy, remainingZ = dz;
            for (int iteration = 0; iteration < 4; iteration++)
            {
                float length = MathF.Sqrt(remainingX * remainingX + remainingY * remainingY + remainingZ * remainingZ);
                if (length <= 0.000001f) break;
                int count = ArenaCollision.GetCandidateTrianglesForSweep(
                    s.PX, s.PY, s.PZ,
                    s.PX + remainingX, s.PY + remainingY, s.PZ + remainingZ,
                    def.CapsuleRadius, def.CapsuleHeight, in arena, candidates);
                if (!ArenaCollision.SweepCapsule(
                    s.PX, s.PY, s.PZ,
                    s.PX + remainingX, s.PY + remainingY, s.PZ + remainingZ,
                    def.CapsuleRadius, def.CapsuleHeight, in arena,
                    candidates, count, out var contact))
                {
                    s.PX += remainingX; s.PY += remainingY; s.PZ += remainingZ;
                    remainingX = remainingY = remainingZ = 0f;
                    break;
                }
                bool upwardFace = ArenaCollision.IsUpwardFacingTriangle(contact.TriangleIndex, in arena);
                if (contact.NormalY > 0.5f
                    && !upwardFace
                    && remainingY <= 0f)
                {
                    s.PX += remainingX;
                    s.PY += remainingY;
                    s.PZ += remainingZ;
                    remainingX = remainingY = remainingZ = 0f;
                    break;
                }


                float contactTime = Math.Clamp(contact.Time, 0f, 1f);
                s.PX += remainingX * contactTime;
                s.PY += remainingY * contactTime;
                s.PZ += remainingZ * contactTime;
                float after = 1f - contactTime;
                remainingX *= after;
                remainingY *= after;
                remainingZ *= after;

                // Keep the actual contact normal: grounded feet can roll over a
                // walkable face's crest, but walls and airborne impacts cannot climb.
                bool enteringSupportFace = wasGrounded && contact.NormalY > 0.5f && upwardFace;
                followedSlope |= enteringSupportFace && contact.NormalY < 0.99999f;

                float inward = remainingX * contact.NormalX
                    + remainingY * contact.NormalY + remainingZ * contact.NormalZ;
                float verticalDisplacementBeforeProjection = remainingY;
                if (inward < 0f)
                {
                    remainingX -= contact.NormalX * inward;
                    remainingY -= contact.NormalY * inward;
                    remainingZ -= contact.NormalZ * inward;
                    if (!enteringSupportFace && verticalDisplacementBeforeProjection <= 0f
                        && remainingY > verticalDisplacementBeforeProjection)
                        remainingY = verticalDisplacementBeforeProjection;
                }

                float velocityYBeforeProjection = s.VY;
                float knockbackVelocityYBeforeProjection = s.KVY;
                ProjectVelocity(ref s.VX, ref s.VY, ref s.VZ, contact.NormalX, contact.NormalY, contact.NormalZ);
                ProjectVelocity(ref s.KVX, ref s.KVY, ref s.KVZ, contact.NormalX, contact.NormalY, contact.NormalZ);
                if (s.VY > velocityYBeforeProjection)
                    s.VY = velocityYBeforeProjection;
                if (s.KVY > knockbackVelocityYBeforeProjection)
                    s.KVY = knockbackVelocityYBeforeProjection;
                if (remainingX * remainingX + remainingY * remainingY + remainingZ * remainingZ <= 0.000001f)
                    break;
            }

            // Rising contact with a lip is not a landing and must not refresh float.
            bool supported = s.VY <= 0f && s.KVY <= 0f
                && ArenaCollision.TryFindSupport(s.PX, s.PY, s.PZ, def.CapsuleRadius,
                def.CapsuleHeight, in arena, candidates, out _);
            if (!supported && followingGround)
            {
                // Follow a descending ramp or rounded crest without manufacturing an
                // airborne frame. Flat ledges still use the ordinary walk-off path.
                int count = ArenaCollision.GetCandidateTrianglesForSweep(
                    s.PX, s.PY, s.PZ, s.PX, s.PY - PlatformSnapTolerance, s.PZ,
                    def.CapsuleRadius, def.CapsuleHeight, in arena, candidates);
                if (ArenaCollision.SweepCapsule(
                        s.PX, s.PY, s.PZ, s.PX, s.PY - PlatformSnapTolerance, s.PZ,
                        def.CapsuleRadius, def.CapsuleHeight, in arena, candidates, count, out var ground)
                    && ground.NormalY > 0.5f
                    && ArenaCollision.TryGetTriangleNormal(ground.TriangleIndex, in arena,
                        out _, out float groundFaceY, out _)
                    && groundFaceY > 0.5f)
                {
                    bool slopeConnection = followedSlope || groundFaceY < 0.99999f;
                    if (!slopeConnection)
                    {
                        // At a flat-to-ramp crest the closest hit can be the flat
                        // perimeter. Require a real ramp under the same capsule path;
                        // a plain ledge over a lower flat floor must not snap down.
                        int slopeCount = 0;
                        for (int i = 0; i < count; i++)
                        {
                            int triangle = candidates[i];
                            if (ArenaCollision.TryGetTriangleNormal(triangle, in arena,
                                    out _, out float normalY, out _)
                                && normalY > 0.5f && normalY < 0.99999f)
                                candidates[slopeCount++] = triangle;
                        }
                        slopeConnection = ArenaCollision.SweepCapsule(
                            s.PX, s.PY, s.PZ, s.PX, s.PY - PlatformSnapTolerance, s.PZ,
                            def.CapsuleRadius, def.CapsuleHeight, in arena,
                            candidates, slopeCount, out var ramp) && ramp.NormalY > 0.5f;
                    }
                    if (slopeConnection)
                    {
                        s.PY -= PlatformSnapTolerance * ground.Time;
                        supported = true;
                    }
                }
            }
            s.IsGrounded = supported;
            if (supported)
            {
                if (s.VY < 0f) s.VY = 0f;
                if (s.KVY < 0f) s.KVY = 0f;
                s.AirTimeTicks = 0;
            }
            ApplyWalkOffTransition(ref s, wasGrounded, def.Movement.FloatWindowTicks);
            return supported;
        }

        internal static void RecoverStageOverlap(ref CharacterState s, CharacterDefinition def,
            in ArenaDefinition arena)
        {
            if (!ArenaCollision.HasTriangles(arena)) return;
            bool wasGrounded = s.IsGrounded;
            int[] candidates = TriangleCandidates(in arena);
            ArenaCollision.RecoverCapsule(ref s.PX, ref s.PY, ref s.PZ,
                def.CapsuleRadius, def.CapsuleHeight, in arena, candidates);
            s.IsGrounded = ArenaCollision.TryFindSupport(s.PX, s.PY, s.PZ,
                def.CapsuleRadius, def.CapsuleHeight, in arena, candidates, out _);
            ApplyWalkOffTransition(ref s, wasGrounded, def.Movement.FloatWindowTicks);
        }
        private static void ApplyWalkOffTransition(
            ref CharacterState s, bool wasGrounded, ushort floatWindowTicks)
        {
            if (wasGrounded && !s.IsGrounded && s.VY <= 0f && !HasKnockback(s))
            {
                if (IsGroundLowState(s.State))
                    s.State = ActionState.Idle;
                s.CrouchSettled = false;
                s.AirTimeTicks = floatWindowTicks;
            }
        }

        private static void ProjectVelocity(ref float vx, ref float vy, ref float vz,
            float nx, float ny, float nz)
        {
            float into = vx * nx + vy * ny + vz * nz;
            if (into < 0f)
            {
                vx -= nx * into;
                vy -= ny * into;
                vz -= nz * into;
            }
        }

        public static void ApplySdi(ref CharacterState s, float dx, float dz,
            CharacterDefinition def, in ArenaDefinition arena)
        {
            if (ArenaCollision.HasTriangles(arena))
                MoveThroughStage(ref s, def, arena, dx * 0.4f, 0f, dz * 0.4f);
            else
            {
                s.PX += dx * 0.4f;
                s.PZ += dz * 0.4f;
            }
        }

        public static void ApplyDirectionalInfluence(ref CharacterState s)
        {
            float mag = MathF.Sqrt(s.KVX * s.KVX + s.KVY * s.KVY + s.KVZ * s.KVZ);
            float inputMag = MathF.Sqrt(s.DIX * s.DIX + s.DIY * s.DIY);
            if (mag <= 0.0001f || inputMag <= 0.0001f) return;
            float tx = s.DIX / inputMag;
            float tz = s.DIY / inputMag;
            float horizontal = MathF.Sqrt(s.KVX * s.KVX + s.KVZ * s.KVZ);
            if (horizontal <= 0.0001f)
            {
                float elevation = 18f * MathF.PI / 180f;
                float signY = s.KVY >= 0f ? 1f : -1f;
                s.KVX = tx * mag * MathF.Sin(elevation);
                s.KVZ = tz * mag * MathF.Sin(elevation);
                s.KVY = signY * mag * MathF.Cos(elevation);
                return;
            }
            float hx = s.KVX / horizontal;
            float hz = s.KVZ / horizontal;
            float dot = Math.Clamp(hx * tx + hz * tz, -1f, 1f);
            float angle = MathF.Acos(dot);
            float turn = MathF.Min(angle, 18f * MathF.PI / 180f * MathF.Sin(angle) * MathF.Sin(angle));
            float cross = hx * tz - hz * tx;
            float sign = cross >= 0f ? 1f : -1f;
            float c = MathF.Cos(turn * sign);
            float sn = MathF.Sin(turn * sign);
            s.KVX = (hx * c - hz * sn) * horizontal;
            s.KVZ = (hx * sn + hz * c) * horizontal;
        }

        // ── KNOCKBACK ──

        internal static bool HasKnockback(CharacterState s)
        {
            return ((s.KVX * s.KVX) + (s.KVY * s.KVY) + (s.KVZ * s.KVZ)) > 0.0001f;
        }

        /// <summary>Find a grabbable ledge for an off-grid state. Mirrors the old TryLedgeSnap
        /// geometry: entity off-grid, a cardinal neighbour ±LedgeSnapRange has surface, and PY is
        /// within [ledgeY - LedgeGrabTolerance, ledgeY + 0.5]. Returns the ledge surface world Y
        /// (surfaceY), the unit inward direction (inwardX/Z), and the ledge surface sample point
        /// (edgeX/Z).</summary>
        internal static bool FindLedge(CharacterState s, ArenaDefinition arena, float capsuleHalf,
            out float surfaceY, out float inwardX, out float inwardZ, out float edgeX, out float edgeZ)
        {
            surfaceY = 0f; inwardX = 0f; inwardZ = 0f; edgeX = 0f; edgeZ = 0f;
            if (s.IsGrounded) return false;

            float centerSurface = arena.Heightmap.Data != null
                ? arena.Heightmap.Sample(s.PX, s.PZ)
                : float.MinValue;
            if (centerSurface > float.MinValue)
                return false; // over a platform — normal ground collision handles it

            // Four cardinal neighbours, X axis then Z axis. The inward direction is the
            // sign of the offset: the stage is on the side that has surface.
            float n = arena.Heightmap.Data != null ? arena.Heightmap.Sample(s.PX + LedgeSnapRange, s.PZ) : float.MinValue;
            if (n > float.MinValue && s.PY >= (n + capsuleHalf) - LedgeGrabTolerance && s.PY <= (n + capsuleHalf) + 0.5f)
            {
                surfaceY = n; inwardX = 1f; edgeX = s.PX + LedgeSnapRange; edgeZ = s.PZ; return true;
            }
            n = arena.Heightmap.Data != null ? arena.Heightmap.Sample(s.PX - LedgeSnapRange, s.PZ) : float.MinValue;
            if (n > float.MinValue && s.PY >= (n + capsuleHalf) - LedgeGrabTolerance && s.PY <= (n + capsuleHalf) + 0.5f)
            {
                surfaceY = n; inwardX = -1f; edgeX = s.PX - LedgeSnapRange; edgeZ = s.PZ; return true;
            }
            n = arena.Heightmap.Data != null ? arena.Heightmap.Sample(s.PX, s.PZ + LedgeSnapRange) : float.MinValue;
            if (n > float.MinValue && s.PY >= (n + capsuleHalf) - LedgeGrabTolerance && s.PY <= (n + capsuleHalf) + 0.5f)
            {
                surfaceY = n; inwardZ = 1f; edgeX = s.PX; edgeZ = s.PZ + LedgeSnapRange; return true;
            }
            n = arena.Heightmap.Data != null ? arena.Heightmap.Sample(s.PX, s.PZ - LedgeSnapRange) : float.MinValue;
            if (n > float.MinValue && s.PY >= (n + capsuleHalf) - LedgeGrabTolerance && s.PY <= (n + capsuleHalf) + 0.5f)
            {
                surfaceY = n; inwardZ = -1f; edgeX = s.PX; edgeZ = s.PZ - LedgeSnapRange; return true;
            }
            return false;
        }

        /// <summary>Occupied LedgeHang state: recompute the held ledge from position each tick
        /// (no stored ledge state). Three escapes — jump, W (stand onto the stage), S (drop) —
        /// else stay hanging. Lost the ledge → fall.</summary>
        private static void ProcessLedgeHang(ref CharacterState s, MovementStats stats,
            InputState input, ArenaDefinition arena, CharacterDefinition def)
        {
            float capsuleHalf = def.CapsuleHeight * 0.5f;
            if (!FindLedge(s, arena, capsuleHalf, out float surfaceY, out float inwardX, out float inwardZ, out _, out _))
            {
                s.State = ActionState.Idle;
                s.IsGrounded = false;
                ClearMovementInterruptionFlags(ref s);
                return;
            }
            (float dirX, float dirZ) = GetInputDirection(input);
            float toward = dirX * inwardX + dirZ * inwardZ;   // >0 toward stage, <0 away
            if (input.Jump && s.JumpsLeft > 0)
            {
                s.State = ActionState.JumpSquat;
                s.StateTicks = stats.JumpSquatTicks;
                s.JumpsLeft--;
                ClearMovementInterruptionFlags(ref s);
                s.InvincibilityTicks = 0;
            }
            else if (toward > 0.5f)
            {
                // W = stand onto the stage
                s.IsGrounded = true;
                s.PY = surfaceY + capsuleHalf;
                s.PX += inwardX * (LedgeSnapRange + def.CapsuleRadius);
                s.PZ += inwardZ * (LedgeSnapRange + def.CapsuleRadius);
                s.VX = s.VY = s.VZ = 0f;
                RecoverStageOverlap(ref s, def, arena);
                ClearMovementInterruptionFlags(ref s);
                s.State = ActionState.Idle;
                s.InvincibilityTicks = 0;
            }
            else if (toward < -0.5f)
            {
                // S = drop. End the float window so full gravity applies on the way down —
                // otherwise the drop rides AirFloatGravity (0) and only falls ~1.5m inside
                // the 30-tick regrab lock, still within the 2.5m grab tolerance → auto re-grab.
                s.State = ActionState.Idle;
                s.IsGrounded = false;
                s.VY = -LedgeDropSpeed;
                s.AirTimeTicks = stats.FloatWindowTicks;
                s.InvincibilityTicks = 0;
                s.LedgeRegrabLockTicks = LedgeRegrabLockDurationTicks;
                ClearMovementInterruptionFlags(ref s);
            }
            else
            {
                // Stay hanging: hold the rim. Zero residual vertical velocity so a hang
                // entered mid-slide never drifts out of the FindLedge window.
                s.VY = 0f;
            }
        }

        private static void ProcessKnockback(ref CharacterState s, ArenaDefinition arena, CharacterDefinition def)
        {
            // Lock-free launches use the same pop/gravity law and vector braking.
            float horizontal = MathF.Sqrt(s.KVX * s.KVX + s.KVZ * s.KVZ);
            float retained = MathF.Max(0f, horizontal - FlightFriction * TickDt);
            if (horizontal > 0.0001f)
            {
                float scale = retained / horizontal;
                s.KVX *= scale;
                s.KVZ *= scale;
            }

            if (!s.IsGrounded)
                ApplyLaunchGravity(ref s.KVY, ref s.AirTimeTicks, def.Movement.MaxFallSpeed);
            s.VX = s.KVX;
            s.VY = s.KVY;
            s.VZ = s.KVZ;

            bool wasAirborne = !s.IsGrounded;
            if (ArenaCollision.HasTriangles(arena))
            {
                MoveThroughStage(ref s, def, arena,
                    s.VX * TickDt, s.VY * TickDt, s.VZ * TickDt);
            }
            else
            {
                // Position update and ground check via the legacy heightmap path.
                s.PX += s.VX * TickDt;
                s.PZ += s.VZ * TickDt;
                s.PY += s.VY * TickDt;
                float capsuleHalfKb = def.CapsuleHeight * 0.5f;
                float kbSurfaceY = arena.Heightmap.Data != null
                    ? arena.Heightmap.Sample(s.PX, s.PZ)
                    : float.MinValue;
                if (kbSurfaceY > float.MinValue)
                {
                    float groundY = kbSurfaceY + capsuleHalfKb;
                    s.IsGrounded = s.KVY <= 0f
                        && s.PY <= groundY + PlatformLandTolerance
                        && (wasAirborne || s.PY >= groundY - PlatformSnapTolerance);
                    if (s.IsGrounded)
                    {
                        s.VY = 0f;
                        s.PY = groundY;
                    }
                }
                else
                {
                    s.IsGrounded = false;
                }
            }

            if (wasAirborne && s.IsGrounded)
            {
                // Natural landing clears knockback
                ClearKnockback(ref s);
                s.AirDodgesLeft = MaxAirDodges;
            }
        }


        /// <summary>
        /// Snap horizontal velocity to zero when below the dead zone threshold.
        /// Prevents residual drift from asymptotic friction/drag decay.
        /// </summary>
        private static void ApplyVelocityDeadZone(ref CharacterState s)
        {
            if (Math.Abs(s.VX) < VelocityDeadZone && Math.Abs(s.VZ) < VelocityDeadZone)
            {
                s.VX = 0f;
                s.VZ = 0f;
            }
        }

        // ── NORMAL MOVEMENT ──

        private static bool IsGroundLowState(ActionState state)
            => state == ActionState.Crouching || state == ActionState.Sliding;

        /// <summary>
        /// Returns whether ordinary grounded low-state input may be admitted this tick.
        /// The ability owner is supplied by the server because Simulation does not own
        /// ability instances.
        /// </summary>
        internal static bool IsGroundLowEligible(in CharacterState s, bool abilityOwned = false)
        {
            if (abilityOwned || !s.IsGrounded
                || (s.State != ActionState.Idle && s.State != ActionState.Run
                    && s.State != ActionState.Crouching && s.State != ActionState.Sliding))
                return false;

            return s.HitstopTicks == 0
                && s.HitstunTicks == 0
                && s.AnimLockTicks == 0
                && s.LandingLagTicks == 0
                && s.WarpSpeed <= 0f
                && !s.IsAiming
                && !HasKnockback(s);
        }

        [ThreadStatic]
        private static DownActionAdmissionReason? _lastDownAdmissionReason;

        /// <summary>
        /// Reason produced by the grounded Down admission helper during the most recent
        /// single-character simulation call. ServerSimulation consumes this immediately
        /// to expose a tick-local diagnostic without adding replicated state.
        /// </summary>
        internal static DownActionAdmissionReason? LastDownAdmissionReason
            => _lastDownAdmissionReason;

        /// <summary>
        /// Apply the shared grounded Down admission rules. The helper owns the transition
        /// itself so diagnostics cannot drift from the behavior they describe.
        /// </summary>
        internal static bool TryApplyGroundDown(
            ref CharacterState s, MovementStats stats, InputState input,
            DownActionTuning tuning, bool movementActionAccepted,
            bool motionOwned, bool abilityOwned, bool landing,
            out DownActionAdmissionReason reason)
        {
            reason = DownActionAdmissionReason.Locked;
            bool lowState = IsGroundLowState(s.State);
            bool requested = input.Down || input.DownPressed || lowState;
            if (!requested)
                return false;

            if (movementActionAccepted)
            {
                reason = DownActionAdmissionReason.ActionAccepted;
                _lastDownAdmissionReason = reason;
                return false;
            }
            if (motionOwned)
            {
                reason = DownActionAdmissionReason.MotionOwned;
                _lastDownAdmissionReason = reason;
                return false;
            }
            if (abilityOwned)
            {
                reason = DownActionAdmissionReason.Locked;
                _lastDownAdmissionReason = reason;
                return false;
            }
            if (!IsGroundLowEligible(in s))
            {
                reason = DownActionAdmissionReason.Locked;
                _lastDownAdmissionReason = reason;
                return false;
            }

            if ((!input.Down && !input.DownPressed) || (landing && !input.Down))
            {
                if (lowState)
                    s.State = ActionState.Idle;
                s.CrouchSettled = false;
                reason = DownActionAdmissionReason.Released;
                _lastDownAdmissionReason = reason;
                return lowState;
            }

            float speed = MathF.Sqrt((s.VX * s.VX) + (s.VZ * s.VZ));
            if (s.State == ActionState.Sliding)
            {
                s.CrouchSettled = false;
                if (!landing)
                    DecayHorizontalSpeed(ref s,
                        tuning.SlideDecelerationRatio * stats.RunSpeed * TickDt);
                if (MathF.Sqrt((s.VX * s.VX) + (s.VZ * s.VZ))
                    < DownActionTuning.EndRatio * stats.RunSpeed)
                    s.State = ActionState.Crouching;
                reason = DownActionAdmissionReason.Accepted;
                _lastDownAdmissionReason = reason;
                return true;
            }

            float entryThreshold = (landing
                ? DownActionTuning.LandingEntryRatio
                : DownActionTuning.GroundEntryRatio) * stats.RunSpeed;
            if (landing || input.DownPressed)
            {
                if (speed >= entryThreshold)
                {
                    s.State = ActionState.Sliding;
                    s.CrouchSettled = false;
                    ClampHorizontalSpeed(ref s, DownActionTuning.EntryCapRatio * stats.RunSpeed);
                    if (!landing)
                        DecayHorizontalSpeed(ref s,
                            tuning.SlideDecelerationRatio * stats.RunSpeed * TickDt);
                    reason = DownActionAdmissionReason.Accepted;
                    _lastDownAdmissionReason = reason;
                    return true;
                }

                reason = DownActionAdmissionReason.BelowThreshold;
                _lastDownAdmissionReason = reason;
                // A held edge below the slide threshold still enters crouch; a sampled
                // tap with no hold does not invent a posture transition.
                if (!input.Down)
                    return false;
            }

            bool enteredCrouch = s.State != ActionState.Crouching;
            s.State = ActionState.Crouching;
            if (enteredCrouch)
                s.CrouchSettled = false;
            // Crouch has no acceleration. Any residual momentum brakes by magnitude.
            if (speed > 0f && !landing)
            {
                DecayHorizontalSpeed(ref s, GroundStopFriction * TickDt);
                s.CrouchSettled = false;
            }
            if (reason != DownActionAdmissionReason.BelowThreshold)
                reason = DownActionAdmissionReason.Accepted;
            _lastDownAdmissionReason = reason;
            return true;
        }

        /// <summary>Cap the horizontal velocity by vector magnitude, preserving direction.</summary>
        internal static void ClampHorizontalSpeed(ref CharacterState s, float cap)
        {
            cap = MathF.Max(0f, cap);
            float speed = MathF.Sqrt((s.VX * s.VX) + (s.VZ * s.VZ));
            if (speed > cap && speed > 0f)
            {
                float scale = cap / speed;
                s.VX *= scale;
                s.VZ *= scale;
            }
        }

        /// <summary>Reduce horizontal velocity by vector magnitude, preserving direction.</summary>
        internal static void DecayHorizontalSpeed(ref CharacterState s, float amount)
        {
            float speed = MathF.Sqrt((s.VX * s.VX) + (s.VZ * s.VZ));
            if (speed <= 0f) return;
            float remaining = MathF.Max(0f, speed - MathF.Max(0f, amount));
            if (remaining <= VelocityDeadZone)
            {
                s.VX = 0f;
                s.VZ = 0f;
                return;
            }


            float scale = remaining / speed;
            s.VX *= scale;
            s.VZ *= scale;
        }

        /// <summary>Refresh grounded jump and air-dodge resources without touching squat state.</summary>
        internal static void RefreshGroundResources(ref CharacterState s, MovementStats stats)
        {
            s.AirDodgesLeft = MaxAirDodges;
            s.JumpsLeft = stats.MaxJumps;
        }

        private static void ProcessNormalMovement(
            ref CharacterState s, MovementStats stats, InputState input,
            DownActionTuning tuning, bool movementActionAccepted, bool processInput = true)
        {
            if (!processInput)
            {
                // Fixed aim: decay horizontal momentum, nothing else — no accel, no facing,
                // no resource resets. Ground mirrors the attacking-friction branch exactly;
                // air uses the per-character AirFriction stat with the air-drag shape from
                // ProcessAirMovement.
                if (s.IsGrounded)
                {
                    float friction = stats.GroundFriction * TickDt;
                    s.VX = MoveToward(s.VX, 0f, Math.Abs(s.VX) * friction);
                    s.VZ = MoveToward(s.VZ, 0f, Math.Abs(s.VZ) * friction);
                }
                else
                {
                    float friction = stats.AirFriction * TickDt;
                    s.VX = MoveToward(s.VX, 0f, friction);
                    s.VZ = MoveToward(s.VZ, 0f, friction);
                    ApplyVelocityDeadZone(ref s);
                }
                return;
            }

            (float dirX, float dirZ) = GetInputDirection(input);

            if (s.IsGrounded)
            {
                ProcessGroundMovement(ref s, stats, input, dirX, dirZ, tuning, movementActionAccepted);
            }
            else
            {
                ProcessAirMovement(ref s, stats, input, dirX, dirZ);
            }

            if (!s.IsGrounded || !IsGroundLowState(s.State))
            {
                // Low states preserve the previously committed direction and Rush window;
                // release exits low posture before reaching this assignment.
                s.LastDirX = dirX;
                s.LastDirZ = dirZ;
            }
        }

        private static void ProcessGroundMovement(
            ref CharacterState s, MovementStats stats,
            InputState input, float dirX, float dirZ,
            DownActionTuning tuning, bool movementActionAccepted)
        {
            // Ground resources are refreshed for all ordinary low states, but never while
            // JumpSquat is active (the caller does not route squat through this method).
            RefreshGroundResources(ref s, stats);
            s.IsGrounded = true;
            s.IsFastFalling = false;
            s.InPostHitstunFlight = false;

            bool lowState = IsGroundLowState(s.State);
            if (movementActionAccepted && lowState)
            {
                s.State = ActionState.Idle;
                s.CrouchSettled = false;
                lowState = false;
            }

            // The admission helper owns every grounded low-state transition and publishes
            // the exact reason consumed by ServerSimulation's tick-local diagnostics.
            if (TryApplyGroundDown(ref s, stats, input, tuning,
                movementActionAccepted, false, false, false, out _)
                && IsGroundLowState(s.State))
                return;

            // A low posture cannot steer or transition while an original movement lock is
            // still active. An accepted action above already removed the posture.
            if (lowState && !IsGroundLowEligible(s))
                return;

            // Run/Idle are the locomotion states this method manages. Aiming (mobile aim,
            // e.g. Wibou E) also routes through here but must keep its own state.
            bool isLocomotion = s.State == ActionState.Idle || s.State == ActionState.Run;

            bool hasInput = ((dirX * dirX) + (dirZ * dirZ)) > 1e-4f;

            if (!hasInput)
            {
                bool inRush = s.RushTicks > 0;   // capture before reset
                s.RushTicks = 0;
                if (inRush && s.State == ActionState.Run)
                {
                    // Rush release: a tap is a fixed burst, not a slide — stop dead.
                    // Gated on Run: the stop is a dash-tap property. A fighter at rest
                    // in Idle — e.g. just finished a move with lunge drift — brakes
                    // instead, so end-of-move momentum carries into Idle
                    // (MomentumPreserve). The window refresh from ability activation
                    // must not turn every attack into a dead stop on release.
                    s.VX = 0f;
                    s.VZ = 0f;
                }
                else
                {
                    // Run release: brake to a stop — fast, no semi-truck drift.
                    float friction = GroundStopFriction * TickDt;
                    s.VX = MoveToward(s.VX, 0f, friction);
                    s.VZ = MoveToward(s.VZ, 0f, friction);
                }
                if (isLocomotion) s.State = ActionState.Idle;
                ApplyVelocityDeadZone(ref s);
                s.LastDirX = s.LastDirZ = 0f;
                return; // facing unchanged
            }

            // Starting from a standstill opens the Rush window (ADR-0020): a fixed
            // dash-dance window during which velocity is set to cruise speed immediately.
            // A perpendicular redirect also restarts the window; reversals remain instant
            // after it expires.
            bool wasStopped = (s.LastDirX == 0f && s.LastDirZ == 0f);
            float dirChangeDot = (s.LastDirX * dirX) + (s.LastDirZ * dirZ);
            if (wasStopped || MathF.Abs(dirChangeDot) < 0.5f) s.RushTicks = stats.RushTicks;

            float facingX = MathF.Sin(s.FacingYaw);
            float facingZ = MathF.Cos(s.FacingYaw);
            bool turnInput = (dirX * facingX + dirZ * facingZ) < -0.5f;   // input opposes previous facing
            float runSpeed = MathF.Sqrt((s.VX * s.VX) + (s.VZ * s.VZ));

            if (turnInput && s.RushTicks > 0)
            {
                // Rush reversal (ADR-0020): instant full-speed flip, no turn lag (the
                // Melee dash-dance). Facing re-faces below; the window restarts so the
                // fighter stays in Rush as long as it keeps reversing.
                s.VX = dirX * stats.RunSpeed;
                s.VZ = dirZ * stats.RunSpeed;
                s.RushTicks = stats.RushTicks;
                if (isLocomotion) s.State = ActionState.Run;
            }
            else
            {
                bool pivot = runSpeed > VelocityDeadZone && (s.VX * dirX + s.VZ * dirZ) < 0f;   // velocity opposes input
                if (pivot)
                {
                    // Ground reversals are immediate after Rush as well: no sluggish
                    // Turnaround skid. Snap to cruise speed in the requested direction.
                    s.VX = dirX * stats.RunSpeed;
                    s.VZ = dirZ * stats.RunSpeed;
                    if (isLocomotion) s.State = ActionState.Run;
                }
                else if (runSpeed > stats.RunSpeed)
                {
                    // SA Dash → Run coast
                    float friction = stats.GroundFriction * TickDt;
                    s.VX = MoveToward(s.VX, dirX * stats.RunSpeed, friction);
                    s.VZ = MoveToward(s.VZ, dirZ * stats.RunSpeed, friction);
                    if (isLocomotion) s.State = ActionState.Run;
                }
                else if (s.RushTicks > 0)
                {
                    // Rush kick-off / hold: cruise speed immediately (no ramp).
                    s.VX = dirX * stats.RunSpeed;
                    s.VZ = dirZ * stats.RunSpeed;
                    if (isLocomotion) s.State = ActionState.Run;
                }
                else
                {
                    // Run hold. Redirects snap to the input direction, except when a
                    // held direction has already been reduced to a single tangent by
                    // stage collision. Re-adding the blocked component at full speed
                    // every tick would make wall slides lose tangent speed geometrically.
                    bool sameInput = MathF.Abs(s.LastDirX - dirX) <= 0.001f
                        && MathF.Abs(s.LastDirZ - dirZ) <= 0.001f;
                    float perp = (s.VX * dirZ) - (s.VZ * dirX);
                    bool tangentSlide = sameInput && MathF.Abs(perp) > VelocityDeadZone
                        && ((MathF.Abs(s.VX) <= VelocityDeadZone && MathF.Abs(s.VZ) > VelocityDeadZone)
                            || (MathF.Abs(s.VZ) <= VelocityDeadZone && MathF.Abs(s.VX) > VelocityDeadZone));
                    if (!tangentSlide)
                    {
                        s.VX = dirX * stats.RunSpeed;
                        s.VZ = dirZ * stats.RunSpeed;
                    }
                    else
                    {
                        float accel = (stats.RunAccelerationA + stats.RunAccelerationB) * TickDt;
                        s.VX = MoveToward(s.VX, dirX * stats.RunSpeed, accel);
                        s.VZ = MoveToward(s.VZ, dirZ * stats.RunSpeed, accel);
                    }
                    if (isLocomotion) s.State = ActionState.Run;
                }
            }

            ApplyVelocityDeadZone(ref s);
            s.LastDirX = dirX;
            s.LastDirZ = dirZ;
            // Facing follows movement direction — even under target lock, which only
            // steers facing during attacks (per-stage RotateTowardTarget, ADR-0018 /
            // issue #127). Only reached with movement input (early-return above).
            s.FacingYaw = MathF.Atan2(dirX, dirZ);
        }


        private static void ProcessAirMovement(
            ref CharacterState s, MovementStats stats,
            InputState input, float dirX, float dirZ)
        {
            s.IsGrounded = false;

            bool hasInput = ((dirX * dirX) + (dirZ * dirZ)) > 1e-4f;
            if (hasInput)
            {
                float accel = (stats.AirAccelStick + stats.AirAccelBase) * TickDt;
                s.VX = MoveToward(s.VX, dirX * stats.AirSpeedMax, accel);
                s.VZ = MoveToward(s.VZ, dirZ * stats.AirSpeedMax, accel);
            }
            else
            {
                if (s.InPostHitstunFlight)
                {
                    // Brake the vector, not each axis: diagonal sends retain the same
                    // distance and heading as axis-aligned sends of equal strength.
                    DecayHorizontalSpeed(ref s, FlightFriction * TickDt);
                }
                else
                {
                    float friction = stats.AirFriction * TickDt;
                    s.VX = MoveToward(s.VX, 0f, friction);
                    s.VZ = MoveToward(s.VZ, 0f, friction);
                }
            }
            ApplyVelocityDeadZone(ref s);


            // Air facing is sticky (ADR-0017, issue #126): it locks at takeoff (last
            // ground facing) and drift / camera rotation never re-face the fighter
            // mid-air. Air normals are deterministic — attack direction = the faced
            // direction, changed only by the LMB facing snap or a target lock
            // (ADR-0018). The old velocity-facing overwrite is what made drift re-face
            // the fighter every frame and is deliberately gone.
        }
        internal static void ClearMovementInterruptionFlags(ref CharacterState s)
        {
            s.IsFastFalling = false;
            s.JumpFromSlide = false;
            s.SlideAttackCarryActive = false;
            s.CrouchSettled = false;
            s.QueuedCrouchBrace = false;
            s.InPostHitstunFlight = false;
        }

        // ── ATTACK PROCESSING ──
        // Removed: ProcessAttack() — all ability execution now handled by ServerAbility lifecycle

        // Removed: StartAttackFromSlot() — ability activation is now handled by ServerSimulation pre-sim phase

        /// <summary>
        /// Process warping state: sets velocity toward warp target each tick.
        /// Position update and collision are handled by main SimulateTick loop.
        /// Returns true if warp completed (arrived at target), false if still warping.
        /// </summary>
        private static bool ProcessWarp(ref CharacterState s, CharacterDefinition def, ArenaDefinition arena)
        {
            float dx = s.WarpTargetX - s.PX;
            float dz = s.WarpTargetZ - s.PZ;
            float distSq = dx * dx + dz * dz;
            float attackRangeSq = s.WarpAttackRange * s.WarpAttackRange;

            // Close enough → warp complete
            if (distSq <= attackRangeSq)
            {
                s.WarpSpeed = 0f;
                s.VX = 0f;
                s.VZ = 0f;
                return true;
            }

            // Set velocity toward target: constant speed at RunSpeed
            // (auto-run feel — matched to character movement speed)
            float dist = MathF.Sqrt(distSq);
            s.VX = (dx / dist) * def.Movement.RunSpeed;
            s.VZ = (dz / dist) * def.Movement.RunSpeed;
            s.FacingYaw = MathF.Atan2(dx, dz);

            // Position update and collision handled by main SimulateTick loop (steps 5-7)
            // Gravity is applied by ApplyGravity() (step 5)

            return false; // still warping
        }


        /// <summary>
        /// Apply jump force. Consumes one jump if available.
        /// </summary>
        public static void ApplyJump(ref CharacterState s, float jumpForce)
        {
            if (s.JumpsLeft <= 0) return;
            ClearMovementInterruptionFlags(ref s);
            s.VY = jumpForce;
            s.JumpsLeft--;
            s.IsGrounded = false;
        }

        /// <summary>
        /// Apply the settled-crouch brace multiplier to a formula-based launch.
        /// This helper intentionally touches only the final knockback velocity; damage,
        /// hitstop, hitstun, and DI remain authored/resolved values.
        /// </summary>
        internal static void ApplyCrouchBrace(ref CharacterState s, bool eligible, float multiplier)
        {
            if (!eligible) return;
            s.KVX *= multiplier;
            s.KVY *= multiplier;
            s.KVZ *= multiplier;
        }

        internal static void ApplyFixedHitstun(ref CharacterState s, ushort fixedTicks, ushort stunGate)
        {
            if (fixedTicks == 0 || stunGate == 0 || s.HitstunTicks == 0) return;
            s.HitstunTicks = Math.Min(fixedTicks, (ushort)240);
            s.HitstunLevel = s.HitstunTicks <= 30 ? (byte)0 :
                s.HitstunTicks <= 50 ? (byte)1 : (byte)2;
        }


        /// <summary>
        /// Apply knockback using the ADR-0019 damage/weight formula.
        /// Magnitude = (base + growth * (damage% / 100 + 1) + damage * 0.1)
        /// * 200 / (weight + 100). StunTicks is a zero/nonzero gate only.
        /// </summary>
        public static void ApplyKnockback(ref CharacterState s, float dirX, float dirZ,
            sbyte angleDeg, float baseKB, float growthKB, float damage,
            ushort stunTicks, float weight, bool applyScale = true)
        {
            ClearMovementInterruptionFlags(ref s);
            s.ClearStartupAimCorrection();
            s.LandingLagTicks = 0;
            float mass = MathF.Max(0.01f, weight + 100f);
            float magnitude = (baseKB + growthKB * (s.DamagePercent * 0.01f + 1f)
                + damage * 0.1f) * 200f / mass;
            float rad = angleDeg * MathF.PI / 180f;
            float cosA = MathF.Cos(rad);
            float sinA = MathF.Sin(rad);

            s.KVX = dirX * magnitude * cosA;
            s.KVY = magnitude * sinA;
            s.KVZ = dirZ * magnitude * cosA;
            if (s.KVY > 0f)
                s.IsGrounded = false;

            // Hitstun from the UNSCALED magnitude (KbScaleFactor below scales velocity only).
            float kbMagnitude = MathF.Sqrt(
                (s.KVX * s.KVX) + (s.KVY * s.KVY) + (s.KVZ * s.KVZ));
            if (stunTicks > 0 && kbMagnitude > 0f)
            {
                // applyScale:false = fixed tools (grabs/yanks — Melee's weight_set_knockback
                // analog): they take the stun coefficient but NOT the +MagBonus launch floor,
                // so yanks don't over-pull (drag outlasting the reel). Damage launches (scaled)
                // carry the floor — that's the Melee "+18" combo lever.
                float stunMag = applyScale ? kbMagnitude + HitstunMagBonus : kbMagnitude;
                s.HitstunTicks = (ushort)Math.Clamp(
                    (int)(HitstunStunCoefficient * stunMag), 1, ushort.MaxValue);
                s.HitstunLevel = s.HitstunTicks <= 30 ? (byte)0 :
                    s.HitstunTicks <= 50 ? (byte)1 : (byte)2;
                s.State = ActionState.Hitstun;
            }
            else
            {
                s.HitstunTicks = 0;
                s.HitstunLevel = 0;
                s.State = ActionState.Idle;
            }

            // Velocity-only scale (KbScaleFactor) — launch distance, not hitstun.
            // applyScale:false is for fixed tools (grabs) that opt out of the hit-KB balance.
            if (applyScale)
            {
                s.KVX *= KbScaleFactor;
                s.KVY *= KbScaleFactor;
                s.KVZ *= KbScaleFactor;
            }

            s.AirTimeTicks = 0;
            s.DashDurationTicks = 0;
            s.AirDodgeRecoveryTicks = 0;
            s.StateTicks = 0;
            s.WasAirborneDuringKnockback = !s.IsGrounded;
        }

        /// <summary>Apply a fully resolved launch force supplied by an ability hook.</summary>
        public static void ApplyKnockbackForce(ref CharacterState s, float dirX, float dirZ,
            sbyte angleDeg, float force, ushort stunTicks)
        {
            ClearMovementInterruptionFlags(ref s);
            s.ClearStartupAimCorrection();
            s.LandingLagTicks = 0;
            float rad = angleDeg * MathF.PI / 180f;
            float cosA = MathF.Cos(rad);
            float sinA = MathF.Sin(rad);
            s.KVX = dirX * force * cosA;
            s.KVY = force * sinA;
            s.KVZ = dirZ * force * cosA;
            if (s.KVY > 0f) s.IsGrounded = false;
            float magnitude = MathF.Sqrt(s.KVX * s.KVX + s.KVY * s.KVY + s.KVZ * s.KVZ);
            if (stunTicks > 0 && magnitude > 0f)
            {
                // Fixed-force tools (grabs/yanks — Melee's weight_set_knockback analog) take the
                // stun coefficient but NOT the +MagBonus launch floor: the floor is the Melee
                // "+18" damage-launch lever, and applying it to pulls made yanks over-pull
                // (drag outlasting the reel and carrying the victim through the caster).
                s.HitstunTicks = (ushort)Math.Clamp(
                    (int)(HitstunStunCoefficient * magnitude), 1, ushort.MaxValue);
                s.HitstunLevel = s.HitstunTicks <= 30 ? (byte)0 : s.HitstunTicks <= 50 ? (byte)1 : (byte)2;
                s.State = ActionState.Hitstun;
            }
            else
            {
                s.HitstunTicks = 0;
                s.HitstunLevel = 0;
                s.State = ActionState.Idle;
            }
            s.AirTimeTicks = 0;
            s.DashDurationTicks = 0;
            s.AirDodgeRecoveryTicks = 0;
            s.StateTicks = 0;
            s.WasAirborneDuringKnockback = !s.IsGrounded;
        }

        private static bool HasQueuedLaunch(CharacterState s)
            => s.QueuedKBZero || s.QueuedKBBase != 0f || s.QueuedKBGrowth != 0f || s.QueuedKVOverride || s.QueuedKBStun > 0;


        /// <summary>
        /// Apply damage and increase damage percentage.
        /// </summary>
        public static void ApplyDamage(ref CharacterState s, float damage)
        {
            int newPercent = s.DamagePercent + (int)Math.Round(damage);
            s.DamagePercent = (ushort)Math.Clamp(newPercent, 0, 999);
        }

        /// <summary>
        /// Tech roll: clears knockback, small burst in last input direction.
        /// </summary>
        public static void DoTechRoll(ref CharacterState s)
        {
            ClearKnockback(ref s);

            float dirX = s.LastDirX;
            float dirZ = s.LastDirZ;
            float len = MathF.Sqrt((dirX * dirX) + (dirZ * dirZ));
            if (len < 0.01f)
            {
                // No input: forward
                dirX = MathF.Sin(s.FacingYaw);
                dirZ = MathF.Cos(s.FacingYaw);
            }
            else
            {
                dirX /= len;
                dirZ /= len;
            }

            s.VX = dirX * 10f;
            s.VZ = dirZ * 10f;
            s.VY = 0f;
            s.State = ActionState.Idle;
        }
        private static void ClearKnockback(ref CharacterState s)
        {
            s.KVX = s.KVY = s.KVZ = 0f;
        }

        // ── GRAVITY ──
        
        private static void ApplyLaunchGravity(ref float verticalVelocity, ref ushort airTimeTicks,
            float maxFallSpeed)
        {
            if (airTimeTicks < ushort.MaxValue) airTimeTicks++;
            if (airTimeTicks <= KnockbackPopTicks && verticalVelocity > 0f) return;

            // Gravity accelerates a fall; it must not slow an already stronger spike.
            float fallLimit = MathF.Min(verticalVelocity, -maxFallSpeed);
            verticalVelocity = MathF.Max(verticalVelocity - FlightGravity * TickDt, fallLimit);
        }

        private static void ApplyGravity(ref CharacterState s, MovementStats stats, float gravityMultiplier)
        {
            if (!s.IsGrounded)
            {
                if (s.InPostHitstunFlight && !s.IsFastFalling)
                {
                    ApplyLaunchGravity(ref s.VY, ref s.AirTimeTicks, stats.MaxFallSpeed);
                    return;
                }

                if (s.AirTimeTicks < ushort.MaxValue)
                    s.AirTimeTicks++;

                if (s.IsFastFalling)
                {
                    s.VY = -stats.FastFallSpeed;
                    return;
                }

                float gravity = (s.AirTimeTicks < stats.FloatWindowTicks)
                    ? stats.AirFloatGravity
                    : stats.Gravity;

                s.VY -= gravity * gravityMultiplier * TickDt;

                if (s.VY < -stats.MaxFallSpeed)
                    s.VY = -stats.MaxFallSpeed;
            }
        }
        // ── INPUT HELPERS ──

        private static (float dirX, float dirZ) GetInputDirection(InputState input)
        {
            // Use camera-relative MoveX/MoveY
            float dx = input.MoveX;
            float dz = input.MoveY;

            float len = MathF.Sqrt((dx * dx) + (dz * dz));
            if (len > 0.001f)
            {
                dx /= len;
                dz /= len;
            }

            return (dx, dz);
        }

        // ── MATH HELPERS ──

        // (removed GetGroundSurfaceY — replaced by ArenaHeightmap.Sample)

        /// <summary>
        /// Get candidate triangle indices near a sphere at (px, py, pz).
        /// Uses the arena's spatial grid for broadphase culling.
        /// </summary>
        public static int GetCandidateTriangles(
            float px, float py, float pz, float radius,
            in ArenaDefinition arena,
            int[] outIndices)
        {
            return ArenaCollision.GetCandidateTrianglesForAabb(
                px - radius, py - radius, pz - radius,
                px + radius, py + radius, pz + radius,
                in arena, outIndices);
        }

        private static float MoveToward(float from, float to, float delta)
        {
            if (Math.Abs(to - from) <= delta)
                return to;
            return from + (Math.Sign(to - from) * delta);
        }
    }
}
