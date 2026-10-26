using System;
using System.Collections.Generic;

namespace SlopArena.Shared
{
    /// <summary>Tutorial lessons in teaching order; presentation owns their wording.</summary>
    public enum TutorialLessonId : byte
    {
        MoveAndLook, Jump, FastFall, Attacks, Aerials, Specials, Recovery, ShieldAndDodge, GrabAndThrow, Combo, Knockout,
    }

    public enum TutorialObjectiveId : byte
    {
        Run, LookAround,
        StandOnBlock, DoubleJump, ReachTallBlock,
        FastFall,
        HitNormal1, HitNormal3, HitNormal4,
        LandAerials,
        UseSpecialA, UseSpecialE, UseSpecialR, UseSpecialF,
        RecoverToStage,
        BlockAttacks, AirDodge,
        Grab, Throw,
        Combo,
        Knockout,
    }

    /// <summary>How the match prepares the tutorial dummy when a lesson starts.</summary>
    public enum TutorialDummySetup : byte { Keep, Reset, KnockoutReady }

    /// <summary>What the tutorial dummy does during a lesson.</summary>
    public enum TutorialDummyBehavior : byte { Idle, Sparring }

    /// <summary>One checklist entry. Goal is a count, metres (Run) or degrees (LookAround).</summary>
    public readonly record struct TutorialObjective(TutorialObjectiveId Id, float Goal);

    /// <summary>
    /// One lesson. DummyPercent is the damage a reset or knockout setup gives the dummy;
    /// NoCooldowns lifts the player's cooldowns while the lesson runs (practice, not a rule change).
    /// </summary>
    public sealed record TutorialLesson(TutorialLessonId Id, TutorialDummySetup DummySetup,
        TutorialDummyBehavior DummyBehavior, IReadOnlyList<TutorialObjective> Objectives)
    {
        public ushort DummyPercent { get; init; }
        public bool NoCooldowns { get; init; }
    }

    /// <summary>
    /// Stage facts the tutorial reads: surface heights for the jump lesson and where the
    /// knockout lesson places the dummy. Heights are walkable surface Y, not capsule centres.
    /// </summary>
    public sealed record TutorialStageLayout(string ArenaKey, float FloorHeight, float BlockHeight,
        float TallBlockHeight, float KnockoutX, float KnockoutZ, float KnockoutYaw);

    public static class TutorialCurriculum
    {
        public static IReadOnlyList<TutorialLesson> Default { get; } = new[]
        {
            Lesson(TutorialLessonId.MoveAndLook, TutorialDummySetup.Keep, TutorialDummyBehavior.Idle,
                (TutorialObjectiveId.Run, 15f), (TutorialObjectiveId.LookAround, 180f)),
            Lesson(TutorialLessonId.Jump, TutorialDummySetup.Keep, TutorialDummyBehavior.Idle,
                (TutorialObjectiveId.StandOnBlock, 1f), (TutorialObjectiveId.DoubleJump, 1f),
                (TutorialObjectiveId.ReachTallBlock, 1f)),
            Lesson(TutorialLessonId.FastFall, TutorialDummySetup.Keep, TutorialDummyBehavior.Idle,
                (TutorialObjectiveId.FastFall, 2f)),
            Lesson(TutorialLessonId.Attacks, TutorialDummySetup.Reset, TutorialDummyBehavior.Idle,
                (TutorialObjectiveId.HitNormal1, 1f), (TutorialObjectiveId.HitNormal3, 1f),
                (TutorialObjectiveId.HitNormal4, 1f)),
            Lesson(TutorialLessonId.Aerials, TutorialDummySetup.Reset, TutorialDummyBehavior.Idle,
                (TutorialObjectiveId.LandAerials, 2f)),
            Lesson(TutorialLessonId.Specials, TutorialDummySetup.Reset, TutorialDummyBehavior.Idle,
                (TutorialObjectiveId.UseSpecialA, 1f), (TutorialObjectiveId.UseSpecialE, 1f),
                (TutorialObjectiveId.UseSpecialR, 1f), (TutorialObjectiveId.UseSpecialF, 1f)),
            Lesson(TutorialLessonId.Recovery, TutorialDummySetup.Keep, TutorialDummyBehavior.Idle,
                (TutorialObjectiveId.RecoverToStage, 1f)),
            Lesson(TutorialLessonId.ShieldAndDodge, TutorialDummySetup.Reset, TutorialDummyBehavior.Sparring,
                (TutorialObjectiveId.BlockAttacks, 3f), (TutorialObjectiveId.AirDodge, 1f)),
            Lesson(TutorialLessonId.GrabAndThrow, TutorialDummySetup.Reset, TutorialDummyBehavior.Idle,
                (TutorialObjectiveId.Grab, 1f), (TutorialObjectiveId.Throw, 1f)),
            // No fighter has a true combo between normals today; a juggle (hit again before the
            // dummy lands) is achievable for all four, some only through a long-cooldown special.
            Lesson(TutorialLessonId.Combo, TutorialDummySetup.Reset, TutorialDummyBehavior.Idle,
                (TutorialObjectiveId.Combo, 2f)) with { DummyPercent = 30, NoCooldowns = true },
            Lesson(TutorialLessonId.Knockout, TutorialDummySetup.KnockoutReady, TutorialDummyBehavior.Idle,
                (TutorialObjectiveId.Knockout, 1f)) with { DummyPercent = 100 },
        };

        /// <summary>The Training Lab: 1 m / 2 m / 3.5 m blocks on the east side, open west and south edges.</summary>
        public static TutorialStageLayout TrainingLab { get; } =
            new("training", 0f, 1f, 3.5f, -15f, 0f, MathF.PI * 0.5f);

        /// <summary>
        /// The Fighter's License rooftop deck: 1.5 m and 3 m blocks and a 5.5 m tower before the north wall,
        /// one open east edge; the knockout spot is 5 m from it, facing back across the deck.
        /// </summary>
        public static TutorialStageLayout LicenseTest { get; } =
            new("license_test", 0f, 1.5f, 3f, 15f, -2f, -MathF.PI * 0.5f);

        private static TutorialLesson Lesson(TutorialLessonId id, TutorialDummySetup setup,
            TutorialDummyBehavior behavior, params (TutorialObjectiveId Id, float Goal)[] objectives)
        {
            var list = new TutorialObjective[objectives.Length];
            for (int i = 0; i < list.Length; i++) list[i] = new(objectives[i].Id, objectives[i].Goal);
            return new(id, setup, behavior, list);
        }
    }

    /// <summary>One simulated tick as the tutorial sees it: both fighters before/after and the tick's hits.</summary>
    public readonly struct TutorialFrame
    {
        public readonly CharacterState PlayerBefore, Player, DummyBefore, Dummy;
        public readonly IReadOnlyList<SpellResolver.HitResult> Hits;
        /// <summary>Degrees the player's camera turned this tick (presentation input, never simulated).</summary>
        public readonly float CameraYawDelta;
        public TutorialFrame(in CharacterState playerBefore, in CharacterState player, in CharacterState dummyBefore,
            in CharacterState dummy, IReadOnlyList<SpellResolver.HitResult> hits, float cameraYawDelta = 0f)
        {
            PlayerBefore = playerBefore; Player = player; DummyBefore = dummyBefore; Dummy = dummy;
            Hits = hits; CameraYawDelta = cameraYawDelta;
        }
    }

    /// <summary>
    /// The tutorial checklist. It only observes local simulation results; the match owner
    /// applies lesson setup and dummy input, so gameplay authority stays in the simulation.
    /// </summary>
    public sealed class TutorialProgress
    {
        private const float SurfaceTolerance = 0.15f;
        /// <summary>Feet this far below the floor surface count as offstage for the recovery lesson.</summary>
        private const float OffstageDepth = 0.5f;
        private readonly TutorialStageLayout _layout;
        private readonly float _capsuleHalf;
        private float[] _amounts = Array.Empty<float>();
        private int _aerialSlots, _comboHits;
        private ulong _comboActivation;
        private bool _offstage, _recovered;
        private byte _offstageDeaths;

        public IReadOnlyList<TutorialLesson> Lessons { get; }
        public int LessonIndex { get; private set; }
        public TutorialLesson Lesson => Lessons[LessonIndex];
        /// <summary>Every objective of the current lesson is done; the owner decides when to start the next.</summary>
        public bool LessonComplete { get; private set; }
        public bool Finished => LessonComplete && LessonIndex == Lessons.Count - 1;
        /// <summary>Longest combo in the current lesson, for presentation.</summary>
        public int ComboHits => _comboHits;

        /// <summary>The dummy's behaviour now: a sparring dummy rests once its swings have all been blocked.</summary>
        public TutorialDummyBehavior DummyBehavior
        {
            get
            {
                var lesson = Lesson;
                if (lesson.DummyBehavior != TutorialDummyBehavior.Sparring) return lesson.DummyBehavior;
                for (int i = 0; i < lesson.Objectives.Count; i++)
                    if (lesson.Objectives[i].Id == TutorialObjectiveId.BlockAttacks && !IsComplete(i))
                        return TutorialDummyBehavior.Sparring;
                return TutorialDummyBehavior.Idle;
            }
        }

        public TutorialProgress(TutorialStageLayout layout, float playerCapsuleHeight,
            IReadOnlyList<TutorialLesson>? lessons = null)
        {
            _layout = layout ?? throw new ArgumentNullException(nameof(layout));
            Lessons = lessons ?? TutorialCurriculum.Default;
            if (Lessons.Count == 0) throw new ArgumentException("A tutorial needs at least one lesson.", nameof(lessons));
            _capsuleHalf = playerCapsuleHeight * 0.5f;
            StartLesson(0);
        }

        public float Amount(int objective) => _amounts[objective];
        public bool IsComplete(int objective) => _amounts[objective] >= Lesson.Objectives[objective].Goal;

        /// <summary>Advance the checklist; returns a bitmask of objectives this frame completed.</summary>
        public int Observe(in TutorialFrame frame)
        {
            if (LessonComplete) return 0;
            Track(in frame);
            var objectives = Lesson.Objectives;
            int completed = 0;
            bool all = true;
            for (int i = 0; i < objectives.Count; i++)
            {
                if (IsComplete(i)) continue;
                _amounts[i] = Math.Min(objectives[i].Goal, Advance(objectives[i].Id, _amounts[i], in frame));
                if (IsComplete(i)) completed |= 1 << i;
                else all = false;
            }
            LessonComplete = all;
            return completed;
        }

        /// <summary>Move to the next lesson; false once the final lesson is done.</summary>
        public bool StartNextLesson()
        {
            if (LessonIndex + 1 >= Lessons.Count) return false;
            StartLesson(LessonIndex + 1);
            return true;
        }

        /// <summary>Mark the current lesson done without its objectives (pause-menu skip).</summary>
        public void SkipLesson() => LessonComplete = true;
        public void RestartLesson() => StartLesson(LessonIndex);

        private void StartLesson(int index)
        {
            LessonIndex = index;
            LessonComplete = false;
            _amounts = new float[Lessons[index].Objectives.Count];
            _aerialSlots = _comboHits = 0;
            _comboActivation = 0;
            _offstage = _recovered = false;
        }

        private void Track(in TutorialFrame f)
        {
            ulong player = f.Player.EntityId, dummy = f.Dummy.EntityId;
            bool dummyHit = false;
            foreach (var hit in f.Hits)
            {
                if (hit.OwnerEntityId != player || hit.TargetEntityId != dummy || hit.Blocked) continue;
                dummyHit = true;
                if (hit.Airborne && IsNormal(hit.AttackSlot)) _aerialSlots |= 1 << hit.AttackSlot;
                if (_comboHits == 0 || !Reeling(in f.DummyBefore)) { _comboHits = 1; _comboActivation = hit.ActivationId; }
                else if (hit.ActivationId != _comboActivation) { _comboHits++; _comboActivation = hit.ActivationId; }
            }
            if (!dummyHit && !Reeling(in f.Dummy)) _comboHits = 0;

            var p = f.Player;
            if (!_offstage)
            {
                if (!p.IsGrounded && Feet(in p) < _layout.FloorHeight - OffstageDepth)
                { _offstage = true; _offstageDeaths = p.Deaths; }
            }
            else if (p.Deaths != _offstageDeaths) _offstage = false;
            else if (p.IsGrounded && Feet(in p) >= _layout.FloorHeight - SurfaceTolerance)
            { _offstage = false; _recovered = true; }
        }

        private float Advance(TutorialObjectiveId id, float amount, in TutorialFrame f)
        {
            ref readonly var before = ref f.PlayerBefore;
            ref readonly var after = ref f.Player;
            switch (id)
            {
                case TutorialObjectiveId.Run:
                    if (!before.IsGrounded || !after.IsGrounded || after.State != ActionState.Run
                        || after.Deaths != before.Deaths) return amount;
                    float dx = after.PX - before.PX, dz = after.PZ - before.PZ;
                    return amount + Math.Min(1f, MathF.Sqrt(dx * dx + dz * dz));
                case TutorialObjectiveId.LookAround: return amount + Math.Abs(f.CameraYawDelta);
                case TutorialObjectiveId.StandOnBlock: return StandingAbove(in after, _layout.BlockHeight) ? 1f : amount;
                case TutorialObjectiveId.ReachTallBlock: return StandingAbove(in after, _layout.TallBlockHeight) ? 1f : amount;
                case TutorialObjectiveId.DoubleJump:
                    return !before.IsGrounded && before.State != ActionState.JumpSquat
                        && after.JumpsLeft < before.JumpsLeft ? amount + 1f : amount;
                case TutorialObjectiveId.FastFall:
                    return after.IsFastFalling && !before.IsFastFalling ? amount + 1f : amount;
                case TutorialObjectiveId.HitNormal1: return amount + GroundHits(in f, AbilitySlots.Slot1);
                case TutorialObjectiveId.HitNormal3: return amount + GroundHits(in f, AbilitySlots.Slot3);
                case TutorialObjectiveId.HitNormal4: return amount + GroundHits(in f, AbilitySlots.Slot4);
                case TutorialObjectiveId.LandAerials: return BitCount(_aerialSlots);
                case TutorialObjectiveId.UseSpecialA: return amount + Activated(in f, AbilitySlots.A);
                case TutorialObjectiveId.UseSpecialE: return amount + Activated(in f, AbilitySlots.E);
                case TutorialObjectiveId.UseSpecialR: return amount + Activated(in f, AbilitySlots.R);
                case TutorialObjectiveId.UseSpecialF: return amount + Activated(in f, AbilitySlots.F);
                case TutorialObjectiveId.RecoverToStage: return _recovered ? 1f : amount;
                case TutorialObjectiveId.BlockAttacks:
                    int blocked = 0;
                    foreach (var hit in f.Hits)
                        if (hit.Blocked && hit.TargetEntityId == after.EntityId && hit.OwnerEntityId == f.Dummy.EntityId)
                            blocked++;
                    return amount + blocked;
                case TutorialObjectiveId.AirDodge:
                    return after.State == ActionState.AirDodgeMovement
                        && before.State != ActionState.AirDodgeMovement ? amount + 1f : amount;
                case TutorialObjectiveId.Grab:
                    return after.State == ActionState.Grabbing && before.State != ActionState.Grabbing
                        && after.InteractionPartnerId == f.Dummy.EntityId ? amount + 1f : amount;
                case TutorialObjectiveId.Throw:
                    return f.Dummy.State == ActionState.Thrown
                        && f.DummyBefore.State != ActionState.Thrown ? amount + 1f : amount;
                case TutorialObjectiveId.Combo: return Math.Max(amount, _comboHits);
                case TutorialObjectiveId.Knockout:
                    return f.Dummy.Deaths > f.DummyBefore.Deaths ? amount + 1f : amount;
                default: return amount;
            }
        }

        private float Feet(in CharacterState s) => s.PY - _capsuleHalf;
        private bool StandingAbove(in CharacterState s, float surface) => s.IsGrounded && Feet(in s) >= surface - SurfaceTolerance;

        private static int GroundHits(in TutorialFrame f, byte slot)
        {
            int hits = 0;
            foreach (var hit in f.Hits)
                if (hit.OwnerEntityId == f.Player.EntityId && hit.TargetEntityId == f.Dummy.EntityId
                    && !hit.Blocked && !hit.Airborne && hit.AttackSlot == slot) hits++;
            return hits;
        }

        private static float Activated(in TutorialFrame f, byte slot)
            => f.Player.AttackSequence != f.PlayerBefore.AttackSequence && f.Player.AttackSlot == slot ? 1f : 0f;

        private static bool IsNormal(byte slot)
            => slot is AbilitySlots.Slot1 or AbilitySlots.Slot3 or AbilitySlots.Slot4;

        /// <summary>
        /// Still stunned, or still airborne from the last hit: the idle dummy only leaves the
        /// ground when launched, so a follow-up here is a juggle even after Hitstun expires.
        /// </summary>
        private static bool Reeling(in CharacterState s)
            => s.HitstunTicks > 0 || s.HitstopTicks > 0 || !s.IsGrounded
                || s.State is ActionState.Hitstun or ActionState.Thrown;

        private static int BitCount(int value)
        {
            int count = 0;
            for (; value != 0; value &= value - 1) count++;
            return count;
        }
    }
}
