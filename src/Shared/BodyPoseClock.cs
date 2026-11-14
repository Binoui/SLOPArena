using System;

namespace SlopArena.Shared
{
    /// <summary>The rule the body pose clock is following for the clip it samples.</summary>
    public enum BodyClipKind : byte
    {
        /// <summary>Nothing sampled yet.</summary>
        None = 0,
        /// <summary>One baked frame per tick from the clip's start, looping: idle, jump, fall, dodge, captive.</summary>
        Pose = 1,
        /// <summary>Frame zero held: crouch, slide, shell and flying.</summary>
        Held = 2,
        /// <summary>The active move's own clip clock (<see cref="AttackPose"/>).</summary>
        Attack = 3,
        /// <summary>The run loop, advanced each tick by horizontal speed over run speed with a floor.</summary>
        Run = 4,
        /// <summary>A hit reaction stretched over the hitstun it began with.</summary>
        Reaction = 5,
        /// <summary>A strong launch's reaction at clip speed, the impact snap before its tumble.</summary>
        ImpactSnap = 6,
        /// <summary>The tumble's build-up once, then its loop, until the fighter lands, jumps or acts.</summary>
        Tumble = 7,
        /// <summary>A launch that lands inside hitstun finishes its reaction's ground recovery.</summary>
        Recovery = 8,
    }

    /// <summary>
    /// The body pose clock: which baked clip a fighter's body shows and where in it, as deterministic
    /// per-fighter simulation state. Shared steps it once per tick when it builds hurtboxes, so hurtboxes
    /// take its pose, and the renderer samples the same clock instead of keeping its own rules. It lives in
    /// World Snapshots and restores with them. Frames are baked frames at 60 Hz: clip time <c>Frame / 60</c>.
    /// </summary>
    public struct BodyPoseClock
    {
        public BodyClipKind Kind;
        /// <summary>Baked animation index of the sampled clip.</summary>
        public int AnimIndex;
        /// <summary>The baked frame the last step sampled, wrapped into a loop or held at a clip's end.</summary>
        public float Frame;
        /// <summary>Baked frames per tick at the last step: the clip speed a presenter plays between ticks.</summary>
        public float Rate;
        /// <summary>Ticks since this clip segment began; hitstop does not count.</summary>
        public ushort Elapsed;
        /// <summary>Changes whenever a new clip segment begins: the restart edge presenters crossfade on.</summary>
        public ushort Segment;
        /// <summary>Reaction identity (<see cref="CharacterState.HitReactionSequence"/>) the reaction clock serves.</summary>
        public ushort ReactionSequence;
        /// <summary>Hitstun ticks remaining when the reaction began: its stretch length.</summary>
        public ushort ReactionTicks;
        /// <summary>Hitstun level the reaction began with; selects its clip.</summary>
        public byte ReactionLevel;
        /// <summary>The action state of the last step outside hitstop.</summary>
        public ActionState LastState;
        /// <summary>The previous step's jumps, ground contact and wall jump, for jump and takeoff edges.</summary>
        public byte LastJumpsLeft;
        public bool LastGrounded;
        public bool LastWallJumpUsed;

        /// <summary>Horizontal speed above which a grounded fighter shows the run loop.</summary>
        public const float RunSpeedThreshold = 0.1f;
        /// <summary>Slowest run playback, as a fraction of clip speed.</summary>
        public const float RunCadenceFloor = 0.35f;
        /// <summary>A launch tumbles only with at least this much hitstun.</summary>
        public const int TumbleMinHitstunTicks = 24;
        /// <summary>tan² of the 15° minimum launch elevation: steeper launches tumble.</summary>
        private const float TumbleMinElevationTanSquared = 0.0717967697f;
        /// <summary>Hitstun ticks of a strong launch that show its reaction before the tumble takes over.</summary>
        public const int TumbleImpactSnapTicks = 10;
        /// <summary>A tumble clip's build-up; after it plays once the clip cycles from here.</summary>
        public const int TumbleLoopStartFrame = 24;
        /// <summary>The loop starts after the build-up only when more than this much clip follows it.</summary>
        private const int TumbleLoopMinTailFrames = 6;
        private const float MotionEpsilon = 0.01f;

        public readonly bool HasSample => Kind != BodyClipKind.None;

        /// <summary>The clips this clock times by its own rules; presenters sample them from it.</summary>
        public readonly bool OwnsPresentation => Kind is BodyClipKind.Run or BodyClipKind.Reaction
            or BodyClipKind.ImpactSnap or BodyClipKind.Tumble or BodyClipKind.Recovery;

        /// <summary>The semantic animation of a clip this clock owns, for presenters' bindings.</summary>
        public readonly string? OwnedAnimationId(CharacterDefinition def) => Kind switch
        {
            BodyClipKind.Run => def.RunAnim,
            BodyClipKind.Reaction or BodyClipKind.ImpactSnap or BodyClipKind.Recovery => HitAnimation(def, ReactionLevel),
            BodyClipKind.Tumble => def.TumbleAnim,
            _ => null,
        };

        public static string HitAnimation(CharacterDefinition def, byte level) => level switch
        {
            1 => def.HitMediumAnim,
            2 => def.HitHardAnim,
            _ => def.HitSmallAnim,
        };

        /// <summary>
        /// Strong launches tumble: enough applied hitstun, sent upward at 15° or steeper. Spikes, flat
        /// ground kicks and small pops keep the hit reaction. Compared without trigonometry so every
        /// runtime decides alike.
        /// </summary>
        public static bool IsTumbleLaunch(int appliedHitstunTicks, float kvx, float kvy, float kvz)
            => appliedHitstunTicks >= TumbleMinHitstunTicks && kvy > 0f
               && kvy * kvy >= TumbleMinElevationTanSquared * ((kvx * kvx) + (kvz * kvz));

        /// <summary>The tumble frame after <paramref name="elapsed"/> ticks: the build-up once, then the loop.</summary>
        public static float TumbleFrame(float elapsed, int frameCount)
        {
            if (frameCount <= 0 || elapsed < frameCount) return Math.Max(0f, elapsed);
            int loopStart = frameCount > TumbleLoopStartFrame + TumbleLoopMinTailFrames ? TumbleLoopStartFrame : 0;
            return loopStart + ((elapsed - loopStart) % (frameCount - loopStart));
        }

        /// <summary>Run playback for a horizontal speed: speed over run speed, never below the floor.</summary>
        public static float RunRate(in CharacterState state, CharacterDefinition def)
        {
            float runSpeed = def.Movement.RunSpeed;
            float horizontal = MathF.Sqrt((state.VX * state.VX) + (state.VZ * state.VZ));
            return runSpeed > 0f ? Math.Max(RunCadenceFloor, horizontal / runSpeed) : 1f;
        }

        /// <summary>
        /// Advance the clock one tick for <paramref name="state"/> and return the baked clip and frame the
        /// body shows. <paramref name="attack"/> is the active move's pose while it attacks. Returns false
        /// when the bake has no usable clip. Hitstop holds the pose; a new reaction identity restarts it.
        /// </summary>
        public static bool Step(ref BodyPoseClock clock, in CharacterState state, CharacterDefinition def,
            BakedAnimationData baked, AttackPose? attack, out int animIndex, out float frame)
        {
            bool sampled = StepPose(ref clock, in state, def, baked, attack, out animIndex, out frame);
            if (state.HitstopTicks == 0) clock.LastState = state.State;
            clock.LastJumpsLeft = state.JumpsLeft;
            clock.LastGrounded = state.IsGrounded;
            clock.LastWallJumpUsed = state.WallJumpUsed;
            return sampled;
        }

        private static bool StepPose(ref BodyPoseClock clock, in CharacterState state, CharacterDefinition def,
            BakedAnimationData baked, AttackPose? attack, out int animIndex, out float frame)
        {
            frame = 0f;
            // Flight, shell and low poses are static frame-zero samples. Low tracks are selected only when
            // grounded and the bake has the exact binding; absent content falls through upright.
            if (state.State == ActionState.Flying && Hold(ref clock, baked, def.FlyingAnim, out animIndex))
                return true;
            if (state.State == ActionState.Shell && Hold(ref clock, baked, def.ShellAnim, out animIndex))
                return true;
            if (state.IsGrounded && state.State is ActionState.Crouching or ActionState.Sliding
                && Hold(ref clock, baked, state.State == ActionState.Crouching ? def.CrouchAnim : def.SlideAnim, out animIndex))
                return true;

            if (state.State is ActionState.Attacking or ActionState.Aiming && state.AttackSlot > 0 && attack is { } pose)
            {
                // Attacks run on their clip clock, the stage's elapsed ticks mapped onto the clip exactly
                // like hitboxes, so body and weapon share one pose.
                animIndex = Resolve(baked, pose.AnimationId);
                if (animIndex < 0) return false;
                frame = pose.FrameAt(baked.Animations[animIndex].FrameCount);
                Continue(ref clock, BodyClipKind.Attack, animIndex);
                clock.Frame = frame;
                clock.Rate = 0f;
                return true;
            }

            // Hitstop freezes the body where it is; nothing changes during the freeze.
            if (state.HitstopTicks > 0 && clock.HasSample && Valid(baked, clock.AnimIndex))
                return Sample(in clock, baked, out animIndex, out frame);

            if (state.State == ActionState.Hitstun)
            {
                if (HitstunPresentation.ShouldRestart(clock.LastState, clock.ReactionSequence, in state)
                    || !InReaction(clock.Kind) || !Valid(baked, clock.AnimIndex))
                    BeginReaction(ref clock, in state, def, baked);
                else if (clock.Kind == BodyClipKind.ImpactSnap
                    && clock.ReactionTicks - state.HitstunTicks >= TumbleImpactSnapTicks)
                {
                    if (state.IsGrounded) BeginRecovery(ref clock, in state, baked, clock.AnimIndex);
                    else Begin(ref clock, BodyClipKind.Tumble, baked.FindAnimIndex(def.TumbleAnim), 0f, 1f);
                }
                else if (clock.Kind == BodyClipKind.Tumble && state.IsGrounded)
                    // Landed inside hitstun: the reaction's ground recovery fills the rest.
                    BeginRecovery(ref clock, in state, baked, Resolve(baked, HitAnimation(def, clock.ReactionLevel)));
                else
                    Advance(ref clock, in state, def, baked);
                return Sample(in clock, baked, out animIndex, out frame);
            }

            bool wallJumped = !state.IsGrounded && state.WallJumpUsed && !clock.LastWallJumpUsed;
            bool jumpStarted = state.JumpsLeft < clock.LastJumpsLeft || wallJumped
                || (clock.LastGrounded && !state.IsGrounded);
            bool acted = state.State != ActionState.Idle && state.State != ActionState.Run;
            // An ordinary reaction plays out through an airborne launch's flight before blending.
            bool reactionPlaysOut = clock.Kind == BodyClipKind.Reaction && clock.Elapsed + 1 < clock.ReactionTicks
                && !acted && !jumpStarted && IsStrongAirborneLaunch(in state);
            // The tumble carries the fighter after hitstun until it lands, jumps or acts. A launch that
            // leaves hitstun before its impact snap ends drops the pending tumble.
            bool tumbleCarries = clock.Kind == BodyClipKind.Tumble && !state.IsGrounded && !jumpStarted && !acted;
            if ((reactionPlaysOut || tumbleCarries) && Valid(baked, clock.AnimIndex))
            {
                Advance(ref clock, in state, def, baked);
                return Sample(in clock, baked, out animIndex, out frame);
            }

            string? target;
            var kind = BodyClipKind.Pose;
            if (state.State is ActionState.AirDodgeMovement or ActionState.AirDodgeRecovery
                or ActionState.HomingDashFloat or ActionState.HomingDash)
                target = string.IsNullOrEmpty(def.AirDodgeAnim) ? def.DashAnim : def.AirDodgeAnim;
            else if (state.State is ActionState.Attacking or ActionState.Aiming && state.AttackSlot > 0)
                target = null; // an attack without an active move has no clip of its own
            // A captive fighter wears the small-hit pose, as its client presentation does.
            else if (ActionStates.Captive(state.State)) target = def.HitSmallAnim;
            else if (!state.IsGrounded) target = state.VY > 0 ? def.JumpAnim : def.FallAnim;
            else if ((state.VX * state.VX) + (state.VZ * state.VZ) > RunSpeedThreshold * RunSpeedThreshold)
            {
                target = def.RunAnim;
                kind = BodyClipKind.Run;
            }
            else target = def.IdleAnim;

            int index = Resolve(baked, target, fallback: false);
            if (index < 0)
            {
                kind = BodyClipKind.Pose;
                index = Resolve(baked, target);
            }
            if (!Valid(baked, index))
            {
                animIndex = -1;
                return false;
            }
            if (clock.Kind != kind || clock.AnimIndex != index || !Valid(baked, clock.AnimIndex))
                Begin(ref clock, kind, index, 0f, kind == BodyClipKind.Run ? RunRate(in state, def) : 1f);
            else
                Advance(ref clock, in state, def, baked);
            return Sample(in clock, baked, out animIndex, out frame);
        }

        private static bool InReaction(BodyClipKind kind)
            => kind is BodyClipKind.Reaction or BodyClipKind.ImpactSnap or BodyClipKind.Tumble or BodyClipKind.Recovery;

        private static bool IsStrongAirborneLaunch(in CharacterState state)
            => !state.IsGrounded && state.WasAirborneDuringKnockback
               && (state.InPostHitstunFlight || MathF.Abs(state.VY) > MotionEpsilon
                   || (state.VX * state.VX) + (state.VZ * state.VZ) > MotionEpsilon * MotionEpsilon);

        private static bool Hold(ref BodyPoseClock clock, BakedAnimationData baked, string animationId, out int animIndex)
        {
            animIndex = string.IsNullOrEmpty(animationId) ? -1 : baked.FindAnimIndex(animationId);
            if (!Valid(baked, animIndex)) return false;
            Continue(ref clock, BodyClipKind.Held, animIndex);
            clock.Frame = 0f;
            clock.Rate = 0f;
            return true;
        }

        private static void BeginReaction(ref BodyPoseClock clock, in CharacterState state, CharacterDefinition def,
            BakedAnimationData baked)
        {
            byte level = state.HitstunLevel;
            int index = Resolve(baked, HitAnimation(def, level));
            int count = Valid(baked, index) ? baked.Animations[index].FrameCount : 0;
            bool launch = IsTumbleLaunch(state.HitstunTicks, state.KVX, state.KVY, state.KVZ)
                && !string.IsNullOrEmpty(def.TumbleAnim) && Valid(baked, baked.FindAnimIndex(def.TumbleAnim));
            // A strong launch shows only the reaction's impact snap at clip speed; other hits stretch the
            // whole reaction over the hitstun they began with.
            float rate = launch || state.HitstunTicks == 0 || count == 0 ? 1f : (float)count / state.HitstunTicks;
            Begin(ref clock, launch ? BodyClipKind.ImpactSnap : BodyClipKind.Reaction, index, 0f, rate);
            clock.ReactionSequence = state.HitReactionSequence;
            clock.ReactionTicks = state.HitstunTicks;
            clock.ReactionLevel = level;
        }

        private static void BeginRecovery(ref BodyPoseClock clock, in CharacterState state, BakedAnimationData baked,
            int index)
        {
            float length = Valid(baked, index) ? baked.Animations[index].FrameCount : 0f;
            int applied = Math.Max(1, (int)clock.ReactionTicks);
            float progress = Math.Clamp((float)(applied - state.HitstunTicks) / applied, 0f, 1f);
            // The rest of the reaction fills the hitstun that remains.
            Begin(ref clock, BodyClipKind.Recovery, index, progress * length,
                length * (1f - progress) / Math.Max(1, (int)state.HitstunTicks));
        }

        /// <summary>Keep a frame-driven segment running, or begin it when the clip or rule changed.</summary>
        private static void Continue(ref BodyPoseClock clock, BodyClipKind kind, int animIndex)
        {
            if (clock.Kind == kind && clock.AnimIndex == animIndex)
                clock.Elapsed = (ushort)Math.Min(ushort.MaxValue, clock.Elapsed + 1);
            else
                Begin(ref clock, kind, animIndex, 0f, 0f);
        }

        private static void Begin(ref BodyPoseClock clock, BodyClipKind kind, int animIndex, float frame, float rate)
        {
            clock.Kind = kind;
            clock.AnimIndex = animIndex;
            clock.Frame = frame;
            clock.Rate = rate;
            clock.Elapsed = 0;
            clock.Segment = unchecked((ushort)(clock.Segment + 1));
        }

        private static void Advance(ref BodyPoseClock clock, in CharacterState state, CharacterDefinition def,
            BakedAnimationData baked)
        {
            int count = baked.Animations[clock.AnimIndex].FrameCount;
            clock.Elapsed = (ushort)Math.Min(ushort.MaxValue, clock.Elapsed + 1);
            switch (clock.Kind)
            {
                case BodyClipKind.Run:
                    clock.Rate = RunRate(in state, def);
                    clock.Frame = Wrap(clock.Frame + clock.Rate, count);
                    break;
                case BodyClipKind.Tumble:
                    clock.Frame = TumbleFrame(clock.Elapsed, count);
                    break;
                case BodyClipKind.Reaction:
                case BodyClipKind.ImpactSnap:
                case BodyClipKind.Recovery:
                    // Reactions do not loop: the last frame holds.
                    clock.Frame = Math.Min(clock.Frame + clock.Rate, count - 1);
                    break;
                default:
                    clock.Frame = Wrap(clock.Frame + 1f, count);
                    break;
            }
        }

        private static float Wrap(float frame, int count)
        {
            while (frame >= count) frame -= count;
            return frame;
        }

        private static bool Sample(in BodyPoseClock clock, BakedAnimationData baked, out int animIndex, out float frame)
        {
            animIndex = clock.AnimIndex;
            frame = 0f;
            if (!Valid(baked, animIndex)) return false;
            // A loop's phase runs up to its frame count, where it meets frame zero again; clips that end
            // hold their last frame.
            int last = baked.Animations[animIndex].FrameCount - 1;
            frame = Math.Max(0f, clock.Frame);
            if (clock.Kind != BodyClipKind.Run && frame > last) frame = last;
            return true;
        }

        private static bool Valid(BakedAnimationData baked, int index)
            => index >= 0 && index < baked.Animations.Count && baked.Animations[index].FrameCount > 0;

        /// <summary>The bake's clip for <paramref name="animationId"/>, else (with fallback) its "idle" stand-in.</summary>
        private static int Resolve(BakedAnimationData baked, string? animationId, bool fallback = true)
        {
            int index = string.IsNullOrEmpty(animationId) ? -1 : baked.FindAnimIndex(animationId!);
            if (index < 0 && fallback) index = baked.FindAnimIndex("idle");
            return index;
        }
    }
}
