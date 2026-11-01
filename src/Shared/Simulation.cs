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
        /// <summary>Horizontal brake (m/s²) when the stick points straight against post-hitstun
        /// launch momentum. Air steering would brake at 12-24 m/s² and cancel half a launch;
        /// this lets the stick cancel about a fifth.</summary>
        public const float HeldFlightBrake = 8f;

        private const byte MaxAirDodges = 1;

        /// <summary>
        /// Short-hop release window in ticks (issue #116 / ADR-0016): releasing the jump key
        /// within this many ticks of the press produces a reduced jump. Digital-optimal timing
        /// tech; tune 3-5 in playtest per ADR-0016.
        /// </summary>
        public const byte ShortHopWindowTicks = 5;
        /// <summary>An ordinary jump leaves at no more than this multiple of AirSpeedMax, as Smash caps takeoff (ADR-0058).</summary>
        public const float JumpTakeoffAirSpeedRatio = 1.2f;

        /// <summary>
        /// Horizontal speed dead zone. Below this, velocity is snapped to zero
        /// to prevent residual drifting from asymptotic friction decay.
        /// </summary>
        private const float VelocityDeadZone = 0.015f;
        /// <summary>
        /// Release brake on the ground (ADR-0020): how fast a Run decelerates to zero once
        /// input is released above RunSpeed. Ordinary release uses GroundSpeedTuning.
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

        internal static bool IsIasaUnlocked(in CharacterState state, ActiveMoveView? move)
            => IsIasaUnlocked(in state, move?.Slot);

        internal static bool IsIasaUnlocked(in CharacterState state, CookedSlotDefinition? move)
        {
            if (state.State != ActionState.Attacking || state.AttackSlot == AbilitySlots.None || move == null)
                return false;
            var stages = move.Timeline.Stages;
            int stageIndex = Math.Min(state.ComboStage, stages.Count - 1);
            var stage = stages[stageIndex];
            if (stage.IasaTicks == 0) return false;
            var elapsed = state.AttackElapsedTicks;
            for (int i = 0; i < stageIndex; i++) elapsed -= stages[i].DurationTicks;
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
            float gravityMultiplier = 1f,
            CombatTuning? combatTuning = null, CookedSlotDefinition? move = null)
            => SimulateTick(ref s, def, input, arena, out ordinaryActionOpportunity,
                out movementActionAccepted, tuning, verticalMotionOwned, move == null ? null : new ActiveMoveView(move, 0, 0, move.IsAir, s.ComboStage, default), gravityMultiplier, combatTuning);

        public static void SimulateTick(
            ref CharacterState s, CharacterDefinition def, InputState input, ArenaDefinition arena,
            out bool ordinaryActionOpportunity, out bool movementActionAccepted,
            DownActionTuning tuning, bool verticalMotionOwned, ActiveMoveView? activeMove,
            float gravityMultiplier = 1f, CombatTuning? combatTuning = null)
        {
            _lastDownAdmissionReason = null;
            ordinaryActionOpportunity = false;
            movementActionAccepted = false;
            if (s.InteractionId != 0 && ActionStates.MovedByPartner(s.State))
            {
                // A command-grabbed fighter takes its grabber's hits as hitstop alone (ADR-0057); nothing else runs it down.
                if (s.State == ActionState.CommandGrabbed && s.HitstopTicks > 0) s.HitstopTicks--;
                TickTimers(ref s);
                return;
            }
            if (s.InteractionId != 0 && s.State == ActionState.Grabbing)
            {
                TickTimers(ref s);
                s.VX = s.VY = s.VZ = 0f;
                return;
            }

            var stats = def.Movement;
            bool wasGrounded = s.IsGrounded;
            bool blockStunLocked = s.BlockStunTicks > 0;
            bool slotInputBlocked = s.LandingLagTicks > 0
                || IsDefenseActionState(s.State) && !(s.State == ActionState.Throwing && s.InteractionId == 0);

            if (verticalMotionOwned)
            {
                // A hop out of the shell owns only her height: the skate-speed carry brakes on (ADR-0045).
                bool shellCarry = s.SlideAttackCarryActive && s.AttackCarryFrom == AttackCarryOrigin.Shell;
                ClearMovementInterruptionFlags(ref s);
                s.SlideAttackCarryActive = shellCarry;
            }
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
                && !IsDefenseActionState(s.State);


            // Preserve the last committed aim when a neutral input omits aim data.
            if (input.AimYaw != 0 || input.IsAiming || input.FaceToCamera)
            {
                float aimDeg = input.AimYaw * 0.01f;
                s.AimYaw = aimDeg * (MathF.PI / 180f);
            }
            // Store aim target distance (cm → m) for projectile abilities
            s.AimTargetDistance = input.AimDistance * 0.01f;
            s.AimPitch = input.AimPitch * 0.01f * (MathF.PI / 180f);

            // A held cut (ADR-0052) is only ever frozen; any path that ends its freeze without the release still frees it.
            if (s.State == ActionState.CutHeld && s.HitstopTicks == 0)
                s.State = ActionState.Idle;
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
                    // A held victim keeps its DI for the pop but cannot nudge out of the hold.
                    if (!s.SdiApplied && s.State != ActionState.CutHeld)
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
                    if (s.State == ActionState.CutHeld)
                        s.State = ActionState.Idle;
                    bool queuedCrouchBrace = s.QueuedCrouchBrace;
                    if (s.QueuedKBResolvedForce)
                    {
                        ApplyKnockbackForce(ref s, s.QueuedKBDirX, s.QueuedKBDirZ,
                            s.QueuedKBAngle, s.QueuedKBForce, s.QueuedKBStun, combatTuning);
                        ApplyFixedHitstun(ref s, s.QueuedKBFixedHitstunTicks, s.QueuedKBStunGate);
                    }
                    else if (!s.QueuedKBZero && (s.QueuedKBBase != 0f || s.QueuedKBGrowth != 0f || s.QueuedKBDamage != 0f || s.QueuedKBStun > 0))
                    {
                        ApplyKnockback(ref s, s.QueuedKBDirX, s.QueuedKBDirZ, s.QueuedKBAngle,
                            s.QueuedKBBase, s.QueuedKBGrowth, s.QueuedKBDamage,
                            s.QueuedKBStun, def.Weight, combatTuning: combatTuning);
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
                    s.QueuedKBZero = false;
                    s.QueuedCrouchBrace = false;
                    s.QueuedKBDirX = 0f; s.QueuedKBDirZ = 0f; s.QueuedKBAngle = 0;
                    s.QueuedKBFixedHitstunTicks = 0;
                    s.QueuedKBStunGate = 0;
                    s.QueuedArmorHitstop = false;
                    s.QueuedKBBase = 0f; s.QueuedKBGrowth = 0f; s.QueuedKBDamage = 0f; s.QueuedKBForce = 0f; s.QueuedKBResolvedForce = false; s.QueuedKBStun = 0;
                    s.BlockHitstopKind = (byte)DefenseBlockHitstopKind.None;
                }
                return;
            }

            // The homing dash owns its motion: ServerSimulation steers it before this tick,
            // and the body moves without gravity, friction or stick control. A slot pressed
            // during the dash is held for the arrival window (the latest press wins).
            if (s.State is ActionState.HomingDashFloat or ActionState.HomingDash)
            {
                TickTimers(ref s);
                MoveDefenseBody(ref s, def, arena);
                return;
            }

            if (s.SlideAttackCarryActive
                && s.AttackSlot > 0
                && (s.State == ActionState.Attacking || s.State == ActionState.Aiming))
            {
                float brake = s.AttackCarryFrom switch
                {
                    AttackCarryOrigin.Run => DownActionTuning.RunAttackDecelerationRatio * stats.RunSpeed,
                    AttackCarryOrigin.Shell => def.Shell?.PopOutDeceleration ?? 0f,
                    _ => DownActionTuning.AttackDecelerationRatio * stats.RunSpeed,
                };
                DecayHorizontalSpeed(ref s, brake * TickDt);
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
                        float jumpCap = s.JumpTakeoff switch
                        {
                            JumpTakeoff.Standing => MathF.Min(stats.RunSpeed, stats.AirSpeedMax * JumpTakeoffAirSpeedRatio),
                            JumpTakeoff.Slide => stats.RunSpeed * DownActionTuning.JumpCapRatio,
                            JumpTakeoff.Shell => float.PositiveInfinity,
                            _ => throw new ArgumentOutOfRangeException(nameof(s.JumpTakeoff)),
                        };
                        ClampHorizontalSpeed(ref s, jumpCap);
                        s.JumpTakeoff = JumpTakeoff.Standing;
                    }
                // During squat: preserve horizontal momentum, no acceleration
            }
            }
            var activeCookedSlot = activeMove?.Slot;
            s.IsAiming = input.IsAiming && !(activeCookedSlot?.Behavior == AuthoringAbilityBehavior.AimedProjectile
                && activeCookedSlot.AimMode == AuthoringAimMode.None);

            // 1. Tick timers
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
                    && activeCookedSlot?.AimMovement == AuthoringAimMovementMode.Mobile;
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
            if (s.State is ActionState.AirDodgeMovement or ActionState.AirDodgeRecovery)
                ProcessDefenseAirDodge(ref s, stats);
            else if (s.State == ActionState.LedgeHang)
                ProcessLedgeHang(ref s, stats, input, arena, def);
            if (wasLedgeHang && s.State == ActionState.JumpSquat)
            {
                movementActionAccepted = true;
                ClearMovementInterruptionFlags(ref s);
            }

            // NOTE: no ground friction during Attacking (issue #115) — attacks preserve
            // drift and lunge momentum; friction resumes when the ability returns to Idle.



            // Grab takes priority over grounded movement and briefly carries existing momentum.
            if (input.GrabPressed && s.IsGrounded
                && CanAcceptDefenseAction(in s, activeMove, blockStunLocked, verticalMotionOwned))
            {
                StartGrabAttempt(ref s, stats);
                movementActionAccepted = true;
            }
            if (s.State == ActionState.GrabAttempt)
                DecayHorizontalSpeed(ref s, stats.RunSpeed / DefenseConfig.GrabBrakeTicks);

            bool canJump = !input.GrabPressed && input.Jump && s.AnimLockTicks == 0
                && s.HitstunTicks == 0 && !blockStunLocked && !verticalMotionOwned
                && s.LandingLagTicks == 0 && s.State != ActionState.JumpSquat
                && s.State != ActionState.Aiming && s.State != ActionState.LedgeHang
                && !HasKnockback(s) && !HasQueuedLaunch(s)
                && (!IsDefenseActionState(s.State) || s.State == ActionState.Shielding);
            float wallJumpX = 0f, wallJumpZ = 0f;
            bool wallJump = canJump && !s.IsGrounded && !s.WallJumpUsed
                && TryFindWallJumpNormal(in s, def, in arena, input, out wallJumpX, out wallJumpZ);
            if (canJump && (wallJump || s.JumpsLeft > 0))
            {
                movementActionAccepted = true;
                if (s.IsGrounded && input.ShieldHeld
                    && ActionStates.AdmitsActions(s.State))
                {
                    // Brake before squat, without carrying a protected frame or slide momentum.
                    s.VX = s.VZ = 0f;
                    s.State = ActionState.Idle;
                    ClearMovementInterruptionFlags(ref s);
                }
                var takeoff = !s.IsGrounded ? JumpTakeoff.Standing
                    : s.State == ActionState.Sliding ? JumpTakeoff.Slide
                    : s.State == ActionState.Shell ? JumpTakeoff.Shell
                    : JumpTakeoff.Standing;
                ClearMovementInterruptionFlags(ref s);
                s.ShieldDropTicks = 0;
                if (s.IsGrounded)
                {
                    s.JumpTakeoff = takeoff;
                    s.State = ActionState.JumpSquat;
                    s.StateTicks = stats.JumpSquatTicks;
                    s.JumpsLeft--;
                }
                else
                {
                    // An airborne jump hops out of the coast.
                    if (s.State == ActionState.Shell)
                    {
                        s.State = ActionState.Idle;
                        s.StateTicks = 0;
                    }
                    s.VY = stats.JumpForce * stats.AirJumpVMultiplier;
                    if (wallJump)
                    {
                        s.VY *= WallJumpConfig.VerticalScale;
                        s.VX = wallJumpX * stats.AirSpeedMax * WallJumpConfig.AwayScale;
                        s.VZ = wallJumpZ * stats.AirSpeedMax * WallJumpConfig.AwayScale;
                        s.FacingYaw = MathF.Atan2(-wallJumpX, -wallJumpZ);
                        s.WallJumpUsed = true;
                    }
                    else
                    {
                        (float dirX, float dirZ) = GetInputDirection(input);
                        s.VX += dirX * stats.AirSpeedMax * stats.AirJumpHMultiplier;
                        s.VZ += dirZ * stats.AirSpeedMax * stats.AirJumpHMultiplier;
                        s.JumpsLeft--;
                    }
                    s.AirTimeTicks = stats.FloatWindowTicks;
                }
            }
            else if (input.Jump && !input.GrabPressed)
            {
                string reason = s.AnimLockTicks > 0 ? "anim_lock" :
                    s.LandingLagTicks > 0 ? "landing_lag" :
                    s.HitstunTicks > 0 || blockStunLocked ? "hitstun" :
                    s.State == ActionState.JumpSquat ? "already_squatting" :
                    verticalMotionOwned ? "vertical_motion_owned" :
                    s.State == ActionState.Aiming ? "aiming" :
                    s.State == ActionState.LedgeHang ? "ledge_hang" :
                    HasKnockback(s) ? "knockback" : HasQueuedLaunch(s) ? "queued_launch" :
                    IsDefenseActionState(s.State) && s.State != ActionState.Shielding ? "defense" :
                    s.JumpsLeft <= 0 ? "no_jumps_or_wall_jump" : "unknown";
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
                    && CanAcceptDefenseAction(in s, activeMove, blockStunLocked, verticalMotionOwned))
                {
                    if (s.State != ActionState.Shielding)
                    {
                        s.State = ActionState.Shielding;
                        s.ShieldDropTicks = 0;
                        s.StateTicks = 0;
                        ClearMovementInterruptionFlags(ref s);
                        s.ClearAttackFields();
                        movementActionAccepted = true;
                    }
                    s.VX = s.VZ = 0f;
                }
                else if (!s.IsGrounded && input.ShieldPressed && s.AirDodgesLeft > 0
                    && CanAcceptAirDodge(in s, blockStunLocked, verticalMotionOwned))
                {
                    StartAirDodge(ref s, stats, input);
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
                    && (ActionStates.AdmitsActions(s.State) || s.State == ActionState.HomingDashArrive)
                    && s.GetCooldown(input.ActiveSlot) == 0;
                if (readyAfterTimers || s.State == ActionState.JumpSquat
                    || (s.AnimLockTicks > 0 && s.AnimLockTicks <= InputBufferWindow)
                    || (s.HitstunTicks > 0 && s.HitstunTicks <= InputBufferWindow))
                    s.BufferedSlot = input.ActiveSlot;
            }

            // 7. Fixed aim holds stop grounded movement but let incoming air momentum coast.
            // Mobile-policy holds retain normal movement control at the slot's speed scale.
            // Attacks own their travel.
            bool fixedAim = s.State == ActionState.Aiming && s.AttackSlot > 0
                && activeCookedSlot?.AimMovement == AuthoringAimMovementMode.Fixed;
            if (fixedAim && s.IsGrounded)
                s.VX = s.VZ = 0f;
            var moveStats = stats;
            if (s.State == ActionState.Aiming && s.AttackSlot > 0 && !fixedAim)
            {
                float aimSpeedScale = activeCookedSlot?.AimMovementSpeedScale ?? 1f;
                moveStats.RunSpeed *= aimSpeedScale;
                moveStats.AirSpeedMax *= aimSpeedScale;
            }
            // Landing lag (issue #125): "no input, no movement" — the stick cannot steer
            // during the lock, even once the aerial has ended and the state is Idle.
            if (!blockStunLocked && s.LandingLagTicks == 0
                && (s.State == ActionState.Idle || s.State == ActionState.Aiming
                    || s.State == ActionState.Run || s.State == ActionState.Crouching
                    || s.State == ActionState.Sliding))
                ProcessNormalMovement(ref s, moveStats, input, tuning, movementActionAccepted, processInput: !fixedAim);
            if (verticalMotionOwned && s.IsGrounded
                && (input.Down || input.DownPressed)
                && !movementActionAccepted)
                _lastDownAdmissionReason = DownActionAdmissionReason.MotionOwned;
            // 6c. Facing snap (LMB, ADR-0017 / issue #126): utility input honored at the
            // input gate — instant facing to the camera azimuth (AimYaw), usable when not
            // attack-locked, not in hitstun / landing lag / jump squat / aim stance.
            // Runs AFTER normal movement so an accepted snap wins its input tick. It does
            // not break persistent target lock.
            if (input.FaceToCamera && s.LandingLagTicks == 0 && s.AnimLockTicks == 0
                && s.State != ActionState.Hitstun && s.State != ActionState.JumpSquat && s.State != ActionState.Shell
                && s.State != ActionState.Aiming && !IsDefenseActionState(s.State))
            {
                s.FacingYaw = input.AimYaw * 0.01f * (MathF.PI / 180f);
                s.AimYaw = s.FacingYaw;
            }

            if (canFastFallEdge && !movementActionAccepted && !verticalMotionOwned
                && s.HitstopTicks == 0 && s.HitstunTicks == 0 && !blockStunLocked
                && !HasKnockback(s)
                && s.State != ActionState.LedgeHang
                && s.State != ActionState.JumpSquat
                && !IsDefenseActionState(s.State))
            {
                s.IsFastFalling = true;
                s.InPostHitstunFlight = false;
            }

            // 8. Gravity (skip during hitstun — ProcessHitstun handles launch gravity;
            // skip during LedgeHang — a hang is a hang, no gravity, else the character
            // slides down through the FindLedge tolerance window and falls off on its own)
            if (s.State != ActionState.Hitstun && s.State != ActionState.LedgeHang)
                ApplyGravity(ref s, stats, gravityMultiplier, verticalMotionOwned);
            bool riding = s.State == ActionState.Shell && wasGrounded;
            float stepX = s.PX, stepY = s.PY, stepZ = s.PZ;

            // 9-10. Authoritative triangle collision, with the old heightmap path kept
            // intact for legacy and synthetic unbaked arenas.
            float capsuleHalf = def.CapsuleHeight * 0.5f;
            float surfaceY = float.MinValue;
            float groundY = float.NaN;
            if (ArenaCollision.HasTriangles(arena))
            {
                MoveThroughStage(ref s, def, arena,
                    s.VX * TickDt, s.VY * TickDt, s.VZ * TickDt, out float wallX, out float wallZ);
                if (riding && def.Shell != null && Shell.IsHeadOn(s.FacingYaw, wallX, wallZ))
                    Shell.Blocked(ref s, def.Shell);
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
            // The wave breaks at the lip (ADR-0050): a grounded ride never steps off its support.
            if (riding && s.State == ActionState.Shell && !s.IsGrounded)
                Shell.BreakAtLip(ref s, stepX, stepY, stepZ);
            ApplyWalkOffTransition(ref s, wasGrounded, stats.FloatWindowTicks);

            if (!wasGrounded && s.IsGrounded)
                ClearMovementInterruptionFlags(ref s);
            // A coast that lands rides on: its clock starts and its speed is laid back along the heading.
            if (s.State == ActionState.Shell && !wasGrounded && s.IsGrounded && def.Shell?.Ride is { } ride)
                Shell.LandRide(ref s, ride);

            if (s.State is (ActionState.AirDodgeMovement or ActionState.AirDodgeRecovery)
                && s.IsGrounded)
            {
                if (s.State == ActionState.AirDodgeMovement)
                    s.AirDodgeRecoveryTicks = (ushort)(s.StateTicks + DefenseConfig.AirDodgeRecoveryTicks);
                s.AirDodgeRecoveryTicks = Math.Min(s.AirDodgeRecoveryTicks, DefenseConfig.AirDodgeLandingTicks);
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
            if (s.InvincibilityTicks > 0) s.InvincibilityTicks--;
            if (s.AnimLockTicks > 0) s.AnimLockTicks--;
            if (s.LandingLagTicks > 0) s.LandingLagTicks--;
            if (s.HitstunTicks > 0) s.HitstunTicks--;
            if (s.BlockStunTicks > 0) s.BlockStunTicks--;
            if (s.ShieldDropTicks > 0) s.ShieldDropTicks--;
            if (s.State == ActionState.AirDodgeRecovery && s.AirDodgeRecoveryTicks > 0)
                s.AirDodgeRecoveryTicks--;
            if (s.AttackElapsedTicks < ushort.MaxValue) s.AttackElapsedTicks++;

            if (s.LedgeRegrabLockTicks > 0) s.LedgeRegrabLockTicks--;

            // Only a timer that actually expires can end a state. An untimed
            // attack, jump squat, run, or hitstun must not be reset to Idle.
            if (s.StateTicks > 0 && s.State != ActionState.JumpSquat)
            {
                s.StateTicks--;
                if (s.StateTicks == 0 && s.State != ActionState.Idle &&
                    s.State is not (ActionState.Shielding or ActionState.ShieldDrop or
                        ActionState.GrabAttempt or ActionState.Grabbing or ActionState.Grabbed or
                        ActionState.Throwing or ActionState.AirDodgeMovement or
                        ActionState.AirDodgeRecovery or ActionState.HomingDashFloat or ActionState.HomingDash))
                    s.State = ActionState.Idle;
            }
            if (s.HomingDashCooldownTicks > 0) s.HomingDashCooldownTicks--;


            // Cooldowns (all 11 slots — issue #117; slots 6-10 got fields in #116 but the
            // decrement only covered 1-6, so their cooldowns never expired).
            for (byte slot = 1; slot <= AbilitySlots.Count; slot++)
            {
                ushort cd = s.GetCooldown(slot);
                if (cd > 0) s.SetCooldown(slot, (ushort)(cd - 1));
            }
            Forms.TickBank(ref s.Form);

            s.RegenerateCharges();


        }
        private static bool IsDefenseActionState(ActionState state)
            => state is ActionState.Shielding or ActionState.ShieldDrop or ActionState.GrabAttempt
                or ActionState.Grabbing or ActionState.Grabbed or ActionState.Throwing
                or ActionState.AirDodgeMovement or ActionState.AirDodgeRecovery || ActionStates.Captive(state);

        private static bool CanAcceptDefenseAction(in CharacterState state, ActiveMoveView? activeMove,
            bool blockStunLocked, bool verticalMotionOwned)
        {
            if (verticalMotionOwned || blockStunLocked || state.HitstunTicks > 0 || state.HitstopTicks > 0
                || HasKnockback(state) || HasQueuedLaunch(state)
                || state.AnimLockTicks > 0 && !IsIasaUnlocked(in state, activeMove)
                || state.LandingLagTicks > 0)
                return false;

            return ActionStates.AdmitsActions(state.State) || state.State is ActionState.Attacking
                or ActionState.Shielding or ActionState.HomingDashArrive;
        }

        private static bool CanAcceptAirDodge(in CharacterState state,
            bool blockStunLocked, bool verticalMotionOwned)
            => !verticalMotionOwned && !blockStunLocked
                && state.State is (ActionState.Idle or ActionState.Run or ActionState.Shell or ActionState.HomingDashArrive)
                && state.AttackSlot == 0 && state.AnimLockTicks == 0
                && state.LandingLagTicks == 0 && state.HitstunTicks == 0
                && state.HitstopTicks == 0
                && !HasKnockback(state) && !HasQueuedLaunch(state);

        private static void StartGrabAttempt(ref CharacterState state, MovementStats stats)
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
            state.ClearAttackFields();
            ClampHorizontalSpeed(ref state, stats.RunSpeed);
        }

        private static void StartAirDodge(ref CharacterState state, MovementStats stats, InputState input)
        {
            ClearMovementInterruptionFlags(ref state);
            state.ClearAttackFields();
            // The stick aims the dodge and a neutral stick dodges forward; facing stays put.
            (float dirX, float dirZ) = GetInputDirection(input);
            if ((dirX * dirX) + (dirZ * dirZ) <= 1e-4f)
            {
                dirX = MathF.Sin(state.FacingYaw);
                dirZ = MathF.Cos(state.FacingYaw);
            }
            state.DashDirX = dirX;
            state.DashDirY = 0f;
            state.DashDirZ = dirZ;
            state.State = ActionState.AirDodgeMovement;
            state.StateTicks = DefenseConfig.AirDodgeMovementTicks;
            state.AirDodgeRecoveryTicks = 0;
            state.InvincibilityTicks = Math.Max(state.InvincibilityTicks,
                DefenseConfig.AirDodgeInvulnerabilityTicks);
            state.AirDodgesLeft--;
            float speed = AirDodgeBurstSpeed(stats, state.StateTicks);
            state.VX = state.DashDirX * speed;
            state.VZ = state.DashDirZ * speed;
        }

        /// <summary>
        /// Start the homing dash float toward <paramref name="dx"/>, <paramref name="dy"/>,
        /// <paramref name="dz"/> (the offset to the target), or along facing without a target.
        /// The caller owns admission and retires any interrupted ability first.
        /// </summary>
        internal static void StartHomingDash(ref CharacterState state, ulong targetId,
            float dx, float dy, float dz)
        {
            if (state.State == ActionState.AirDodgeMovement
                && state.InvincibilityTicks <= DefenseConfig.AirDodgeInvulnerabilityTicks)
                state.InvincibilityTicks = 0;
            ClearMovementInterruptionFlags(ref state);
            state.ClearAttackFields();
            state.ShieldDropTicks = 0;
            state.AirDodgeRecoveryTicks = 0;
            state.State = ActionState.HomingDashFloat;
            state.StateTicks = HomingDashConfig.FloatTicks;
            state.VX = state.VY = state.VZ = 0f;
            state.HomingDashTargetId = targetId;
            state.HomingDashCooldownTicks = HomingDashConfig.CooldownTicks;
            if (!state.IsGrounded) state.HomingDashAirUsed = true;
            state.DashDirX = MathF.Sin(state.FacingYaw);
            state.DashDirY = 0f;
            state.DashDirZ = MathF.Cos(state.FacingYaw);
            if (targetId != 0) AimHomingDash(ref state, dx, dy, dz, snap: true);
        }

        /// <summary>
        /// Advance the homing dash one tick before movement. <paramref name="hasTarget"/> with
        /// the offset to the target and the arrival distance between capsule centres; without
        /// a target the dash keeps its direction and ends as a miss.
        /// </summary>
        internal static void StepHomingDash(ref CharacterState state, bool hasTarget,
            float dx, float dy, float dz, float arrivalDistance, ushort floatWindowTicks)
        {
            if (state.State == ActionState.HomingDashFloat)
            {
                state.VX = state.VY = state.VZ = 0f;
                if (hasTarget) AimHomingDash(ref state, dx, dy, dz, snap: true);
                if (state.StateTicks > 0) return;
                state.State = ActionState.HomingDash;
                state.StateTicks = HomingDashConfig.MaxTravelTicks;
            }
            else if (state.State != ActionState.HomingDash)
                return;
            float speed = HomingDashConfig.Speed;
            if (hasTarget)
            {
                float distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                if (distance <= arrivalDistance + ArrivalTolerance)
                {
                    EndHomingDash(ref state, ActionState.HomingDashArrive, HomingDashConfig.ArriveWindowTicks);
                    // An airborne arrival hangs in the float window (AirFloatGravity) for the hover
                    // and glides on along the dash instead of stopping dead.
                    state.AirTimeTicks = (ushort)Math.Max(0, floatWindowTicks - HomingDashConfig.ArriveHoverTicks);
                    if (!state.IsGrounded)
                    {
                        state.VX = state.DashDirX * HomingDashConfig.ArriveCarrySpeed;
                        state.VZ = state.DashDirZ * HomingDashConfig.ArriveCarrySpeed;
                    }
                    return;
                }
                AimHomingDash(ref state, dx, dy, dz, snap: false);
                // The last step shortens to land on the arrival gap, so every distance arrives at
                // the same spacing instead of anywhere within one step of it.
                speed = MathF.Min(speed, (distance - arrivalDistance) / TickDt);
            }
            if (state.StateTicks == 0)
            {
                EndHomingDash(ref state, ActionState.HomingDashMiss, HomingDashConfig.MissRecoveryTicks);
                state.AnimLockTicks = HomingDashConfig.MissRecoveryTicks;
                state.BufferedSlot = 0;
                state.AirTimeTicks = floatWindowTicks;
                return;
            }
            state.VX = state.DashDirX * speed;
            state.VY = state.DashDirY * speed;
            state.VZ = state.DashDirZ * speed;
        }

        /// <summary>A shortened last step can stop just short of the gap after float rounding or a turning target.</summary>
        private const float ArrivalTolerance = 0.05f;

        private static void EndHomingDash(ref CharacterState state, ActionState end, ushort ticks)
        {
            state.State = end;
            state.StateTicks = ticks;
            state.VX = state.VY = state.VZ = 0f;
            state.HomingDashTargetId = 0;
        }

        /// <summary>Turn the dash direction toward the target offset, snapping during the float.</summary>
        private static void AimHomingDash(ref CharacterState state, float dx, float dy, float dz, bool snap)
        {
            // Grounded travel slides along the floor instead of pushing into it.
            if (state.IsGrounded && dy < 0f) dy = 0f;
            float length = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            if (length < 1e-4f) return;
            dx /= length; dy /= length; dz /= length;
            float x = state.DashDirX, y = state.DashDirY, z = state.DashDirZ;
            float dot = x * dx + y * dy + z * dz;
            if (snap || dot >= HomingDashConfig.TurnCos)
            {
                x = dx; y = dy; z = dz;
            }
            else
            {
                // Rotate by the turn limit inside the plane of the two directions.
                float px = dx - dot * x, py = dy - dot * y, pz = dz - dot * z;
                float perp = MathF.Sqrt(px * px + py * py + pz * pz);
                if (perp < 1e-4f)
                {
                    // Directly behind: turn around the vertical axis.
                    px = z; py = 0f; pz = -x;
                    perp = MathF.Sqrt(px * px + pz * pz);
                    if (perp < 1e-4f) return;
                }
                px /= perp; py /= perp; pz /= perp;
                x = x * HomingDashConfig.TurnCos + px * HomingDashConfig.TurnSin;
                y = y * HomingDashConfig.TurnCos + py * HomingDashConfig.TurnSin;
                z = z * HomingDashConfig.TurnCos + pz * HomingDashConfig.TurnSin;
                float norm = MathF.Sqrt(x * x + y * y + z * z);
                x /= norm; y /= norm; z /= norm;
            }
            state.DashDirX = x; state.DashDirY = y; state.DashDirZ = z;
            if (x * x + z * z > 1e-6f)
                state.FacingYaw = state.AimYaw = MathF.Atan2(x, z);
        }

        /// <summary>Front-loaded burst speed, derived from the remaining movement ticks.</summary>
        private static float AirDodgeBurstSpeed(MovementStats stats, ushort remainingTicks)
        {
            // Repeated multiplication avoids MathF.Pow, whose result can differ between runtimes.
            float speed = stats.AirDodgeSpeed * DefenseConfig.AirDodgeLaunchScale;
            for (int tick = remainingTicks; tick < DefenseConfig.AirDodgeMovementTicks; tick++)
                speed *= DefenseConfig.AirDodgeSpeedRetention;
            return speed;
        }

        private static void AdvanceDefenseStates(ref CharacterState state)
        {
            if (state.State == ActionState.ShieldDrop && state.ShieldDropTicks == 0)
                state.State = ActionState.Idle;
            else if (state.State == ActionState.GrabAttempt && state.StateTicks == 0)
            {
                state.State = ActionState.Idle;
                state.ClearStartupAimCorrection();
                state.InteractionPhase = (byte)DefenseInteractionPhase.None;
                state.InteractionId = 0;
                state.InteractionPartnerId = 0;
                state.InteractionTick = 0;
            }
            else if (state.State == ActionState.AirDodgeMovement && state.StateTicks == 0)
            {
                // Recovery keeps the burst's exit velocity and glides it out.
                state.State = ActionState.AirDodgeRecovery;
                state.StateTicks = DefenseConfig.AirDodgeRecoveryTicks;
                state.AirDodgeRecoveryTicks = DefenseConfig.AirDodgeRecoveryTicks;
            }
            else if (state.State == ActionState.AirDodgeRecovery && state.AirDodgeRecoveryTicks == 0)
            {
                state.State = ActionState.Idle;
                state.StateTicks = 0;
            }
            else if (state.State == ActionState.Throwing && state.InteractionId == 0 && state.StateTicks == 0)
                state.State = ActionState.Idle;
        }

        internal static void MoveDefenseBody(ref CharacterState state, CharacterDefinition def,
            in ArenaDefinition arena)
        {
            MoveThroughStage(ref state, def, arena,
                state.VX * TickDt, state.VY * TickDt, state.VZ * TickDt);
            if (ArenaCollision.HasTriangles(arena)) return;

            float surface = arena.Heightmap.Data != null
                ? arena.Heightmap.Sample(state.PX, state.PZ) : arena.KillHeight + 1f;
            float floor = surface + def.CapsuleHeight * 0.5f;
            state.IsGrounded = surface > float.MinValue && state.PY <= floor + 0.0001f && state.VY <= 0f;
            if (state.IsGrounded)
            {
                state.PY = floor;
                state.VY = 0f;
            }
        }

        private static void ProcessDefenseAirDodge(ref CharacterState state, MovementStats stats)
        {
            if (state.State == ActionState.AirDodgeRecovery)
            {
                // Glide on air friction without steering; landing has already zeroed it.
                DecayHorizontalSpeed(ref state, stats.AirFriction * TickDt);
                return;
            }
            float speed = AirDodgeBurstSpeed(stats, state.StateTicks);
            state.VX = state.DashDirX * speed;
            state.VZ = state.DashDirZ * speed;
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

        private static bool TryFindWallJumpNormal(in CharacterState s, CharacterDefinition def,
            in ArenaDefinition arena, InputState input, out float wallX, out float wallZ)
        {
            wallX = wallZ = 0f;
            (float dirX, float dirZ) = GetInputDirection(input);
            if (dirX * dirX + dirZ * dirZ <= 0.000001f || !ArenaCollision.HasTriangles(arena))
                return false;
            float endX = s.PX + dirX * WallJumpConfig.ProbeDistance;
            float endZ = s.PZ + dirZ * WallJumpConfig.ProbeDistance;
            int[] candidates = TriangleCandidates(in arena);
            int count = ArenaCollision.GetCandidateTrianglesForSweep(s.PX, s.PY, s.PZ,
                endX, s.PY, endZ, def.CapsuleRadius, def.CapsuleHeight, in arena, candidates);
            if (!ArenaCollision.SweepCapsule(s.PX, s.PY, s.PZ, endX, s.PY, endZ,
                def.CapsuleRadius, def.CapsuleHeight, in arena, candidates, count, out var contact)
                || MathF.Abs(contact.NormalY) > 0.5f)
                return false;
            float sideNormal = MathF.Sqrt(contact.NormalX * contact.NormalX + contact.NormalZ * contact.NormalZ);
            if (sideNormal <= 0.000001f) return false;
            wallX = contact.NormalX / sideNormal;
            wallZ = contact.NormalZ / sideNormal;
            return -(dirX * wallX + dirZ * wallZ) >= WallJumpConfig.PressedThreshold;
        }

        /// <summary>
        /// Moves one character through triangle geometry. The remaining displacement is
        /// projected against each contact, so walls slide and corners settle without
        /// tunnelling. Both ordinary and knockback velocities receive the same projection.
        /// </summary>
        internal static bool MoveThroughStage(ref CharacterState s, CharacterDefinition def,
            in ArenaDefinition arena, float dx, float dy, float dz)
            => MoveThroughStage(ref s, def, arena, dx, dy, dz, out _, out _);

        /// <param name="wallX">Horizontal normal of the side contact (a face too steep to stand on) that most
        /// opposed this move, with <paramref name="wallZ"/>; zero when there was none.</param>
        internal static bool MoveThroughStage(ref CharacterState s, CharacterDefinition def,
            in ArenaDefinition arena, float dx, float dy, float dz, out float wallX, out float wallZ)
        {
            wallX = wallZ = 0f;
            if (!ArenaCollision.HasTriangles(arena))
            {
                s.PX += dx; s.PY += dy; s.PZ += dz;
                return false;
            }
            float travel = MathF.Sqrt(dx * dx + dz * dz);
            float mostInto = 0f;

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
                float sideNormal = MathF.Sqrt(contact.NormalX * contact.NormalX + contact.NormalZ * contact.NormalZ);
                if (travel > 0.000001f && MathF.Abs(contact.NormalY) <= 0.5f && sideNormal > 0.000001f)
                {
                    float into = (dx * contact.NormalX + dz * contact.NormalZ) / (travel * sideNormal);
                    if (into < mostInto)
                    {
                        mostInto = into;
                        wallX = contact.NormalX / sideNormal;
                        wallZ = contact.NormalZ / sideNormal;
                    }
                }
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
                s.HomingDashAirUsed = false;
                s.WallJumpUsed = false;
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
            s.HomingDashAirUsed = false;
            s.WallJumpUsed = false;
            s.JumpsLeft = stats.MaxJumps;
        }

        private static void ProcessNormalMovement(
            ref CharacterState s, MovementStats stats, InputState input,
            DownActionTuning tuning, bool movementActionAccepted,
            bool processInput = true)
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
                // Low states preserve the previously committed direction;
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

            float speed = MathF.Sqrt(s.VX * s.VX + s.VZ * s.VZ);
            if (!hasInput)
            {
                if (speed > stats.RunSpeed * 1.000001f)
                {
                    float brake = GroundStopFriction * TickDt;
                    s.VX = MoveToward(s.VX, 0f, brake);
                    s.VZ = MoveToward(s.VZ, 0f, brake);
                }
                else DecayHorizontalSpeed(ref s, stats.RunSpeed / GroundSpeedTuning.StopTicks);
                ApplyVelocityDeadZone(ref s);
                s.LastDirX = s.LastDirZ = 0f;
                if (isLocomotion) s.State = ActionState.Idle;
                return;
            }

            bool reverse = s.VX * dirX + s.VZ * dirZ < 0f;
            if (speed > stats.RunSpeed * 1.000001f)
            {
                // Preserve the shipped dash-to-run coast and overspeed pivot.
                if (reverse)
                {
                    s.VX = dirX * stats.RunSpeed;
                    s.VZ = dirZ * stats.RunSpeed;
                }
                else
                {
                    float friction = stats.GroundFriction * TickDt;
                    s.VX = MoveToward(s.VX, dirX * stats.RunSpeed, friction);
                    s.VZ = MoveToward(s.VZ, dirZ * stats.RunSpeed, friction);
                }
            }
            else
            {
                float start = stats.RunSpeed * GroundSpeedTuning.StartRatio;
                float span = stats.RunSpeed - start;
                float accel = span / GroundSpeedTuning.RampTicks;
                bool belowStart = speed < start * 0.999999f;
                float next = input.Walk
                    ? MathF.Max(stats.RunSpeed * GroundSpeedTuning.WalkRatio, speed - stats.RunSpeed / GroundSpeedTuning.StopTicks)
                    : belowStart ? start : MathF.Min(stats.RunSpeed, MathF.Max(start, speed) + accel);
                if (next >= stats.RunSpeed * 0.999999f) next = stats.RunSpeed;
                float perp = s.VX * dirZ - s.VZ * dirX;
                bool sameInput = MathF.Abs(s.LastDirX - dirX) <= 0.001f
                    && MathF.Abs(s.LastDirZ - dirZ) <= 0.001f;
                bool tangentSlide = sameInput && !reverse && MathF.Abs(perp) > VelocityDeadZone
                    && ((MathF.Abs(s.VX) <= VelocityDeadZone && MathF.Abs(s.VZ) > VelocityDeadZone)
                        || (MathF.Abs(s.VZ) <= VelocityDeadZone && MathF.Abs(s.VX) > VelocityDeadZone));
                if (tangentSlide && !belowStart && !input.Walk)
                {
                    s.VX = MoveToward(s.VX, dirX * stats.RunSpeed, accel);
                    s.VZ = MoveToward(s.VZ, dirZ * stats.RunSpeed, accel);
                }
                else
                {
                    s.VX = dirX * next;
                    s.VZ = dirZ * next;
                }
            }
            ApplyVelocityDeadZone(ref s);
            s.LastDirX = dirX;
            s.LastDirZ = dirZ;
            s.FacingYaw = MathF.Atan2(dirX, dirZ);
            if (isLocomotion) s.State = ActionState.Run;
        }

        internal static void ProcessAirMovement(
            ref CharacterState s, MovementStats stats,
            InputState input, float dirX, float dirZ)
        {
            s.IsGrounded = false;

            bool hasInput = ((dirX * dirX) + (dirZ * dirZ)) > 1e-4f;
            float speed = MathF.Sqrt((s.VX * s.VX) + (s.VZ * s.VZ));
            float opposing = speed > 0f ? -((dirX * s.VX) + (dirZ * s.VZ)) / speed : 0f;
            if (hasInput && s.InPostHitstunFlight && (opposing > 0f || speed > stats.AirSpeedMax))
            {
                // Launch momentum outranks steering: it only brakes, harder the more the
                // stick opposes it, and ordinary drift resumes once it is spent.
                float brake = FlightFriction
                    + ((HeldFlightBrake - FlightFriction) * Math.Clamp(opposing, 0f, 1f));
                DecayHorizontalSpeed(ref s, brake * TickDt);
            }
            else if (hasInput)
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
            s.JumpTakeoff = JumpTakeoff.Standing;
            s.SlideAttackCarryActive = false;
            s.CrouchSettled = false;
            s.QueuedCrouchBrace = false;
            s.InPostHitstunFlight = false;
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
            if (fixedTicks == 0 || stunGate == 0) return;
            if (s.HitstunTicks == 0)
            {
                s.State = ActionState.Hitstun;
                s.HitReactionSequence = unchecked((ushort)(s.HitReactionSequence + 1));
            }
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
            ushort stunTicks, float weight, bool applyScale = true, CombatTuning? combatTuning = null)
        {
            var tuning = combatTuning ?? CombatTuning.Default;
            // A press held for the homing dash arrival must not fire after the hit.
            if (s.State is ActionState.HomingDashFloat or ActionState.HomingDash)
                s.BufferedSlot = 0;
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

            // Hitstun uses the unscaled magnitude; tuning scales velocity separately below.
            float kbMagnitude = MathF.Sqrt(
                (s.KVX * s.KVX) + (s.KVY * s.KVY) + (s.KVZ * s.KVZ));
            if (stunTicks > 0 && kbMagnitude > 0f)
            {
                // applyScale:false = fixed tools (grabs/yanks — Melee's weight_set_knockback
                // analog): they take the stun coefficient but NOT the +MagBonus launch floor,
                // so yanks don't over-pull (drag outlasting the reel). Damage launches (scaled)
                // carry the floor — that's the Melee "+18" combo lever.
                float stunMag = applyScale ? kbMagnitude + tuning.HitstunMagnitudeBonus : kbMagnitude;
                s.HitstunTicks = (ushort)Math.Clamp(
                    (int)(tuning.HitstunCoefficient * stunMag), 1, ushort.MaxValue);
                s.HitstunLevel = s.HitstunTicks <= 30 ? (byte)0 :
                    s.HitstunTicks <= 50 ? (byte)1 : (byte)2;
                s.State = ActionState.Hitstun;
                s.HitReactionSequence = unchecked((ushort)(s.HitReactionSequence + 1));
            }
            else
            {
                s.HitstunTicks = 0;
                s.HitstunLevel = 0;
                s.State = ActionState.Idle;
            }

            // Velocity-only scale affects launch distance after hitstun is calculated.
            // applyScale:false is for fixed tools (grabs) that opt out of the hit-KB balance.
            if (applyScale)
            {
                s.KVX *= tuning.KnockbackScale;
                s.KVY *= tuning.KnockbackScale;
                s.KVZ *= tuning.KnockbackScale;
            }

            s.AirTimeTicks = 0;
            s.AirDodgeRecoveryTicks = 0;
            s.StateTicks = 0;
            s.WasAirborneDuringKnockback = !s.IsGrounded;
        }

        /// <summary>Apply a fully resolved launch force supplied by an ability hook.</summary>
        public static void ApplyKnockbackForce(ref CharacterState s, float dirX, float dirZ,
            sbyte angleDeg, float force, ushort stunTicks, CombatTuning? combatTuning = null)
        {
            var tuning = combatTuning ?? CombatTuning.Default;
            if (s.State is ActionState.HomingDashFloat or ActionState.HomingDash)
                s.BufferedSlot = 0;
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
                    (int)(tuning.HitstunCoefficient * magnitude), 1, ushort.MaxValue);
                s.HitstunLevel = s.HitstunTicks <= 30 ? (byte)0 : s.HitstunTicks <= 50 ? (byte)1 : (byte)2;
                s.State = ActionState.Hitstun;
                s.HitReactionSequence = unchecked((ushort)(s.HitReactionSequence + 1));
            }
            else
            {
                s.HitstunTicks = 0;
                s.HitstunLevel = 0;
                s.State = ActionState.Idle;
            }
            s.AirTimeTicks = 0;
            s.AirDodgeRecoveryTicks = 0;
            s.StateTicks = 0;
            s.WasAirborneDuringKnockback = !s.IsGrounded;
        }

        private static bool HasQueuedLaunch(CharacterState s)
            => s.QueuedKBZero || s.QueuedKBBase != 0f || s.QueuedKBGrowth != 0f || s.QueuedKBStun > 0;


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

        internal static void ApplyGravity(ref CharacterState s, MovementStats stats, float gravityMultiplier,
            bool verticalMotionOwned = false)
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

                // With gravity off, a move that owns her vertical motion (a dive) keeps its own speed.
                if (s.VY < -stats.MaxFallSpeed && !(verticalMotionOwned && gravityMultiplier == 0f))
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
