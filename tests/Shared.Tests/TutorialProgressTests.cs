using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>Tutorial checklist detection: synthetic frames for each rule, real simulation for the wiring.</summary>
public sealed class TutorialProgressTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;
    public TutorialProgressTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    private const ulong Player = 1, Dummy = 2;
    private const float Capsule = 1.7f;
    private static readonly TutorialStageLayout Layout = TutorialCurriculum.TrainingLab;
    private static readonly SpellResolver.HitResult[] NoHits = Array.Empty<SpellResolver.HitResult>();

    private static TutorialProgress At(TutorialLessonId lesson)
    {
        var progress = new TutorialProgress(Layout, Capsule);
        while (progress.Lesson.Id != lesson) Assert.True(progress.StartNextLesson());
        return progress;
    }

    private static CharacterState Fighter(ulong id, float feet = 0f, bool grounded = true) => new()
    {
        EntityId = id, PY = feet + Capsule * 0.5f, IsGrounded = grounded, JumpsLeft = 2, State = ActionState.Idle,
    };

    private static int Observe(TutorialProgress progress, CharacterState playerBefore, CharacterState player,
        IReadOnlyList<SpellResolver.HitResult>? hits = null, CharacterState? dummyBefore = null,
        CharacterState? dummy = null, float cameraYaw = 0f)
    {
        var idleDummy = Fighter(Dummy);
        return progress.Observe(new TutorialFrame(playerBefore, player, dummyBefore ?? idleDummy,
            dummy ?? dummyBefore ?? idleDummy, hits ?? NoHits, cameraYaw));
    }

    private static SpellResolver.HitResult Hit(byte slot, bool airborne = false, ulong activation = 1,
        bool blocked = false, ulong owner = Player, ulong target = Dummy) => new()
    {
        OwnerEntityId = owner, TargetEntityId = target, AttackSlot = slot, Airborne = airborne,
        ActivationId = activation, Blocked = blocked,
    };

    [Fact]
    public void CurriculumFollowsTheAgreedTeachingOrder()
    {
        Assert.Equal(new[]
        {
            TutorialLessonId.MoveAndLook, TutorialLessonId.Jump, TutorialLessonId.FastFall, TutorialLessonId.Attacks,
            TutorialLessonId.Aerials, TutorialLessonId.Beeline, TutorialLessonId.Specials, TutorialLessonId.Recovery,
            TutorialLessonId.ShieldAndDodge, TutorialLessonId.GrabAndThrow, TutorialLessonId.Combo,
            TutorialLessonId.BeelineTricks, TutorialLessonId.Knockout,
        }, TutorialCurriculum.Default.Select(lesson => lesson.Id));
        Assert.All(TutorialCurriculum.Default, lesson => Assert.InRange(lesson.Objectives.Count, 1, 8));
        var combo = TutorialCurriculum.Default.Single(lesson => lesson.Id == TutorialLessonId.Combo);
        Assert.True(combo.NoCooldowns);
        Assert.Equal(30, combo.DummyPercent);
        Assert.Equal(100, TutorialCurriculum.Default.Single(lesson => lesson.Id == TutorialLessonId.Knockout).DummyPercent);
        var tricks = TutorialCurriculum.Default.Single(lesson => lesson.Id == TutorialLessonId.BeelineTricks);
        Assert.True(tricks.NoCooldowns);
        Assert.Equal(30, tricks.DummyPercent);
        Assert.Equal(TutorialDummySetup.Reset, tricks.DummySetup);
        Assert.Equal(TutorialDummyBehavior.Sparring, tricks.DummyBehavior);
        Assert.DoesNotContain(TutorialCurriculum.Default, lesson => lesson.NoCooldowns
            && lesson.Id is not (TutorialLessonId.Combo or TutorialLessonId.BeelineTricks));
    }

    [Fact]
    public void RunCountsOnlyGroundedRunDistanceAndLookCountsCameraTurning()
    {
        var progress = At(TutorialLessonId.MoveAndLook);
        var before = Fighter(Player);
        var running = before with { State = ActionState.Run, PZ = 0.5f };
        var airborne = before with { IsGrounded = false, PZ = 0.5f };
        Observe(progress, before, airborne);
        Assert.Equal(0f, progress.Amount(0));
        for (int i = 0; i < 29; i++) Observe(progress, before, running);
        Assert.False(progress.IsComplete(0));
        Assert.Equal(1 << 0, Observe(progress, before, running));

        Observe(progress, before, before, cameraYaw: -120f);
        Assert.False(progress.LessonComplete);
        Assert.Equal(1 << 1, Observe(progress, before, before, cameraYaw: 70f));
        Assert.True(progress.LessonComplete);
    }

    [Fact]
    public void BlockObjectivesUseFeetHeightAndAirJumpsOnly()
    {
        var progress = At(TutorialLessonId.Jump);
        var floor = Fighter(Player);
        Observe(progress, floor, floor with { PY = 1f + Capsule * 0.5f, IsGrounded = false });
        Assert.Equal(0f, progress.Amount(0));
        Observe(progress, floor, Fighter(Player, feet: 1f));
        Assert.True(progress.IsComplete(0));

        var squat = floor with { State = ActionState.JumpSquat };
        Observe(progress, squat, squat with { JumpsLeft = 1 });
        Assert.Equal(0f, progress.Amount(1));
        var rising = Fighter(Player, feet: 1.5f, grounded: false) with { JumpsLeft = 1 };
        Observe(progress, rising, rising with { JumpsLeft = 0 });
        Assert.True(progress.IsComplete(1));

        Observe(progress, floor, Fighter(Player, feet: 3.3f, grounded: false));
        Assert.False(progress.IsComplete(2));
        Observe(progress, floor, Fighter(Player, feet: 3.5f));
        Assert.True(progress.LessonComplete);
    }

    [Fact]
    public void FastFallCountsEachFreshLatch()
    {
        var progress = At(TutorialLessonId.FastFall);
        var falling = Fighter(Player, feet: 3f, grounded: false);
        var latched = falling with { IsFastFalling = true };
        Observe(progress, falling, latched);
        Observe(progress, latched, latched);
        Assert.Equal(1f, progress.Amount(0));
        Observe(progress, falling, latched);
        Assert.True(progress.LessonComplete);
    }

    [Fact]
    public void GroundNormalsNeedUnblockedGroundHitsOnTheDummy()
    {
        var progress = At(TutorialLessonId.Attacks);
        var idle = Fighter(Player);
        Observe(progress, idle, idle, new[]
        {
            Hit(AbilitySlots.Slot1, airborne: true), Hit(AbilitySlots.Slot3, blocked: true),
            Hit(AbilitySlots.Slot4, target: 99), Hit(AbilitySlots.A),
        });
        Assert.All(Enumerable.Range(0, 3), i => Assert.Equal(0f, progress.Amount(i)));
        Assert.Equal(0b111, Observe(progress, idle, idle, new[]
        {
            Hit(AbilitySlots.Slot1), Hit(AbilitySlots.Slot3), Hit(AbilitySlots.Slot4),
        }));
        Assert.True(progress.LessonComplete);
    }

    [Fact]
    public void AerialsNeedTwoDifferentAirNormals()
    {
        var progress = At(TutorialLessonId.Aerials);
        var air = Fighter(Player, feet: 1f, grounded: false);
        Observe(progress, air, air, new[] { Hit(AbilitySlots.Slot3, airborne: true) });
        Observe(progress, air, air, new[] { Hit(AbilitySlots.Slot3, airborne: true, activation: 2) });
        Observe(progress, air, air, new[] { Hit(AbilitySlots.E, airborne: true, activation: 3) });
        Assert.Equal(1f, progress.Amount(0));
        Observe(progress, air, air, new[] { Hit(AbilitySlots.Slot4, airborne: true, activation: 4) });
        Assert.True(progress.LessonComplete);
    }

    [Fact]
    public void SpecialsCountActivationsBySlot()
    {
        var progress = At(TutorialLessonId.Specials);
        var idle = Fighter(Player);
        Observe(progress, idle, idle with { AttackSlot = AbilitySlots.E });
        Assert.Equal(0f, progress.Amount(1));
        foreach (byte slot in new[] { AbilitySlots.A, AbilitySlots.E, AbilitySlots.R, AbilitySlots.F, AbilitySlots.Slot1 })
            Observe(progress, idle, idle with { State = ActionState.Attacking, AttackSlot = slot, AttackSequence = 1 });
        Assert.True(progress.LessonComplete);
    }

    [Fact]
    public void RecoveryNeedsAReturnToTheFloorWithoutDying()
    {
        var progress = At(TutorialLessonId.Recovery);
        var floor = Fighter(Player);
        var below = Fighter(Player, feet: -2f, grounded: false);
        Observe(progress, floor, below);
        Observe(progress, below, below with { Deaths = 1 });
        Observe(progress, below, Fighter(Player) with { Deaths = 1 });
        Assert.False(progress.LessonComplete);

        var again = below with { Deaths = 1 };
        Observe(progress, floor, again);
        Observe(progress, again, Fighter(Player, feet: 1f, grounded: false) with { Deaths = 1 });
        Assert.False(progress.LessonComplete);
        Observe(progress, again, Fighter(Player) with { Deaths = 1 });
        Assert.True(progress.LessonComplete);
    }

    [Fact]
    public void ShieldCountsTheDummysBlockedHitsAndAirDodgeEntry()
    {
        var progress = At(TutorialLessonId.ShieldAndDodge);
        Assert.Equal(TutorialDummyBehavior.Sparring, progress.DummyBehavior);
        var shielding = Fighter(Player) with { State = ActionState.Shielding };
        Observe(progress, shielding, shielding, new[]
        {
            Hit(AbilitySlots.Slot1, owner: Dummy, target: Player, blocked: true),
            Hit(AbilitySlots.Slot1, owner: Dummy, target: Player, blocked: false),
            Hit(AbilitySlots.Slot1, owner: Player, target: Dummy, blocked: true),
        });
        Assert.Equal(1f, progress.Amount(0));
        Observe(progress, shielding, shielding, new[]
        {
            Hit(AbilitySlots.Slot1, owner: Dummy, target: Player, blocked: true, activation: 2),
            Hit(AbilitySlots.Slot1, owner: Dummy, target: Player, blocked: true, activation: 3),
        });
        Assert.True(progress.IsComplete(0));
        Assert.Equal(TutorialDummyBehavior.Idle, progress.DummyBehavior);
        var air = Fighter(Player, feet: 1f, grounded: false);
        Observe(progress, air, air with { State = ActionState.AirDodgeMovement });
        Assert.True(progress.LessonComplete);
    }

    [Fact]
    public void GrabNeedsTheDummyAndThrowNeedsItsRelease()
    {
        var progress = At(TutorialLessonId.GrabAndThrow);
        var idle = Fighter(Player);
        Observe(progress, idle, idle with { State = ActionState.Grabbing, InteractionPartnerId = 99 });
        Assert.Equal(0f, progress.Amount(0));
        Observe(progress, idle, idle with { State = ActionState.Grabbing, InteractionPartnerId = Dummy });
        Assert.True(progress.IsComplete(0));
        var held = Fighter(Dummy) with { State = ActionState.Grabbed };
        Observe(progress, idle, idle, dummyBefore: held, dummy: held with { State = ActionState.Thrown });
        Assert.True(progress.LessonComplete);
    }

    [Fact]
    public void ComboNeedsASecondMoveWhileTheDummyIsStillStunned()
    {
        var progress = At(TutorialLessonId.Combo);
        var idle = Fighter(Player);
        var dummy = Fighter(Dummy);
        var stunned = dummy with { State = ActionState.Hitstun, HitstunTicks = 12 };
        Observe(progress, idle, idle, new[] { Hit(AbilitySlots.Slot1, activation: 1) }, dummy, stunned);
        Observe(progress, idle, idle, new[] { Hit(AbilitySlots.Slot1, activation: 1) }, stunned, stunned);
        Assert.Equal(1f, progress.Amount(0));

        Observe(progress, idle, idle, dummyBefore: stunned, dummy: dummy);
        Observe(progress, idle, idle, new[] { Hit(AbilitySlots.Slot4, activation: 2) }, dummy, stunned);
        Assert.Equal(1, progress.ComboHits);
        Assert.False(progress.LessonComplete);

        Observe(progress, idle, idle, new[] { Hit(AbilitySlots.Slot3, activation: 3) }, stunned, stunned);
        Assert.Equal(2, progress.ComboHits);
        Assert.True(progress.LessonComplete);
    }

    [Fact]
    public void ComboCountsAJuggleAfterHitstunWhileTheDummyIsStillAirborne()
    {
        var progress = At(TutorialLessonId.Combo);
        var idle = Fighter(Player);
        var dummy = Fighter(Dummy);
        var launched = Fighter(Dummy, feet: 2f, grounded: false);
        Observe(progress, idle, idle, new[] { Hit(AbilitySlots.Slot3, activation: 1) }, dummy, launched with { HitstunTicks = 8 });
        Observe(progress, idle, idle, dummyBefore: launched with { HitstunTicks = 1 }, dummy: launched);
        Assert.Equal(1, progress.ComboHits);
        Observe(progress, idle, idle, new[] { Hit(AbilitySlots.Slot3, airborne: true, activation: 2) }, launched, launched);
        Assert.True(progress.LessonComplete);
    }

    [Fact]
    public void BeelineHitsNeedAnArrivalActivationAndAnUnblockedHitOnTheDummy()
    {
        var progress = At(TutorialLessonId.Beeline);
        var idle = Fighter(Player);
        var attack = idle with { AttackSequence = 1, AttackSlot = AbilitySlots.Slot1 };
        Observe(progress, idle, attack, new[] { Hit(AbilitySlots.Slot1) });
        Assert.False(progress.IsComplete(0));
        var arrival = idle with { State = ActionState.HomingDashArrive };
        Observe(progress, arrival, attack, new[]
        {
            Hit(AbilitySlots.Slot1, blocked: true), Hit(AbilitySlots.Slot1, target: 99),
            Hit(AbilitySlots.Slot1, owner: 99),
        });
        Assert.False(progress.IsComplete(0));
        Observe(progress, attack, attack, new[] { Hit(AbilitySlots.Slot1) });
        Assert.True(progress.IsComplete(0));
        Assert.False(progress.IsComplete(1));
        var next = attack with { AttackSequence = 2 };
        Observe(progress, attack, next, new[] { Hit(AbilitySlots.Slot1, airborne: true) });
        Assert.False(progress.IsComplete(1));
        var special = next with { AttackSequence = 3, AttackSlot = AbilitySlots.R };
        Observe(progress, arrival, special);
        Observe(progress, special, special, new[] { Hit(AbilitySlots.R, airborne: true) });
        Assert.True(progress.LessonComplete);
    }

    [Fact]
    public void BeelineOutOfShieldNeedsAFreshDashAndStopsSparring()
    {
        var progress = At(TutorialLessonId.BeelineTricks);
        var idle = Fighter(Player);
        var dash = idle with { State = ActionState.HomingDashFloat };
        Observe(progress, idle, dash);
        Observe(progress, dash, dash);
        Assert.False(progress.IsComplete(0));
        Assert.Equal(TutorialDummyBehavior.Sparring, progress.DummyBehavior);
        Observe(progress, idle with { State = ActionState.Shielding }, dash);
        Assert.True(progress.IsComplete(0));
        Assert.Equal(TutorialDummyBehavior.Idle, progress.DummyBehavior);
        Assert.False(progress.LessonComplete);
        progress.RestartLesson();
        Assert.Equal(TutorialDummyBehavior.Sparring, progress.DummyBehavior);
    }

    [Fact]
    public void BeelineRecoveryNeedsAnOffstageDashAndALandingWithoutDying()
    {
        var progress = At(TutorialLessonId.BeelineTricks);
        var floor = Fighter(Player);
        var below = Fighter(Player, feet: -0.6f, grounded: false);
        Observe(progress, floor, below);
        Observe(progress, below, floor);
        Assert.False(progress.IsComplete(1));
        var dash = below with { State = ActionState.HomingDashFloat };
        Observe(progress, floor, floor with { State = ActionState.HomingDashFloat });
        Observe(progress, floor, below);
        Observe(progress, below, floor);
        Assert.False(progress.IsComplete(1));
        Observe(progress, floor, below);
        Observe(progress, below, dash);
        Observe(progress, dash, below with { Deaths = 1 });
        Observe(progress, below, floor with { Deaths = 1 });
        Assert.False(progress.IsComplete(1));
        below.Deaths = 1; dash.Deaths = 1; floor.Deaths = 1;
        Observe(progress, floor, below);
        Observe(progress, below, floor);
        Assert.False(progress.IsComplete(1));
        Observe(progress, floor, below);
        Observe(progress, below, dash);
        Observe(progress, dash, floor);
        Assert.True(progress.IsComplete(1));
        progress.RestartLesson();
        Observe(progress, below, floor);
        Assert.False(progress.IsComplete(1));
    }

    [Fact]
    public void BeelineChaseNeedsADashStartedWhileTheDummyIsAirborne()
    {
        var progress = At(TutorialLessonId.BeelineTricks);
        var idle = Fighter(Player);
        var dashing = idle with { State = ActionState.HomingDashFloat };
        var arrival = idle with { State = ActionState.HomingDashArrive };
        var grounded = Fighter(Dummy);
        var launched = Fighter(Dummy, feet: 2f, grounded: false);

        Observe(progress, idle, dashing, dummyBefore: grounded, dummy: grounded);
        Observe(progress, arrival, idle with { AttackSequence = 1 }, new[] { Hit(AbilitySlots.Slot1) }, grounded, launched);
        Assert.False(progress.IsComplete(2), "a Beeline at a grounded dummy is not a chase");

        Observe(progress, idle, dashing, dummyBefore: launched, dummy: launched);
        Observe(progress, idle, idle with { AttackSequence = 2 }, new[] { Hit(AbilitySlots.Slot1, activation: 2) }, launched, launched);
        Assert.False(progress.IsComplete(2), "the hit must come from the arrival window");
        Observe(progress, arrival, idle with { AttackSequence = 3 },
            new[] { Hit(AbilitySlots.Slot1, activation: 3, blocked: true) }, launched, launched);
        Assert.False(progress.IsComplete(2), "a blocked hit does not count");
        Observe(progress, arrival, idle with { AttackSequence = 4 }, new[] { Hit(AbilitySlots.Slot1, activation: 4) }, launched, grounded);
        Assert.True(progress.IsComplete(2));
    }

    [Fact]
    public void RestartClearsTheChaseDash()
    {
        var progress = At(TutorialLessonId.BeelineTricks);
        var idle = Fighter(Player);
        var launched = Fighter(Dummy, feet: 2f, grounded: false);
        Observe(progress, idle, idle with { State = ActionState.HomingDashFloat }, dummyBefore: launched, dummy: launched);
        progress.RestartLesson();
        Observe(progress, idle with { State = ActionState.HomingDashArrive }, idle with { AttackSequence = 1 },
            new[] { Hit(AbilitySlots.Slot1) }, launched, launched);
        Assert.False(progress.IsComplete(2));
    }

    [Fact]
    public void KnockoutNeedsTheDummyToLoseALife()
    {
        var progress = At(TutorialLessonId.Knockout);
        var idle = Fighter(Player);
        var dummy = Fighter(Dummy);
        Observe(progress, idle, idle with { Deaths = 1 }, dummyBefore: dummy, dummy: dummy);
        Assert.False(progress.LessonComplete);
        Observe(progress, idle, idle, dummyBefore: dummy, dummy: dummy with { Deaths = 1 });
        Assert.True(progress.LessonComplete);
        Assert.True(progress.Finished);
        Assert.False(progress.StartNextLesson());
    }

    [Fact]
    public void CompletedLessonsWaitForTheOwnerAndSkipOrRestartReset()
    {
        var progress = new TutorialProgress(Layout, Capsule);
        var idle = Fighter(Player);
        progress.SkipLesson();
        Assert.True(progress.LessonComplete);
        Assert.Equal(0, Observe(progress, idle, idle, cameraYaw: 360f));
        Assert.Equal(0f, progress.Amount(1));
        Assert.True(progress.StartNextLesson());
        Assert.Equal(TutorialLessonId.Jump, progress.Lesson.Id);

        Observe(progress, idle, Fighter(Player, feet: 1f));
        Assert.True(progress.IsComplete(0));
        progress.RestartLesson();
        Assert.False(progress.IsComplete(0));
        Assert.False(progress.Finished);
    }

    // ── Real simulation: cooked fighters on the Training Lab ─────────────────────────

    private sealed class Lab
    {
        public readonly ServerSimulation Sim;
        public readonly TutorialProgress Progress;
        public readonly MatchContentEntry PlayerEntry;
        public int Completed;
        public uint Tick;
        public readonly List<string> Contacts = new();
        public readonly List<string> DashTrace = new();

        public Lab(CharacterClass player, CharacterClass dummy, TutorialLessonId lesson,
            float playerX = 0f, float playerZ = -4f, float dummyX = 0f, float dummyZ = 4f)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
            var arena = ArenaBinaryFormat.LoadFromFile(Path.Combine(root, "data", "arenas", "training.arena"))!.Value;
            Sim = new ServerSimulation(arena, NoWinMatchRule.Instance);
            PlayerEntry = BuiltInContentResolver.Resolve(player);
            var dummyEntry = BuiltInContentResolver.Resolve(dummy);
            Register(Player, PlayerEntry, playerX, playerZ, 0f);
            Register(Dummy, dummyEntry, dummyX, dummyZ, MathF.PI);
            Progress = new TutorialProgress(Layout, PlayerEntry.Definition.CapsuleHeight);
            while (Progress.Lesson.Id != lesson) Progress.StartNextLesson();
            Sim.NoCooldownsEntityId = Progress.Lesson.NoCooldowns ? Player : null;
            Sim.SetState(Dummy, Sim.GetState(Dummy) with { DamagePercent = Progress.Lesson.DummyPercent });
        }

        private void Register(ulong id, MatchContentEntry entry, float x, float z, float yaw)
        {
            var def = entry.Definition;
            Sim.RegisterEntity(id, def, new CharacterState
            {
                PX = x, PY = def.CapsuleHeight * 0.5f, PZ = z, FacingYaw = yaw, AimYaw = yaw, IsGrounded = true,
                JumpsLeft = def.Movement.MaxJumps, AirDodgesLeft = 1,
            }, entry.BakedAnimation);
            Sim.SetRespawnPosition(id, x, def.CapsuleHeight * 0.5f, z, yaw);
        }

        public void Step(InputState player = default, InputState? dummy = null)
        {
            var playerBefore = Sim.GetState(Player);
            var dummyBefore = Sim.GetState(Dummy);
            var dummyInput = dummy ?? TutorialDummy.Build(Progress.DummyBehavior, dummyBefore, playerBefore, Tick);
            Sim.Tick(new Dictionary<ulong, InputState> { [Player] = player, [Dummy] = dummyInput });
            Completed |= Progress.Observe(new TutorialFrame(playerBefore, Sim.GetState(Player), dummyBefore,
                Sim.GetState(Dummy), Sim.LastTickHits.ToArray()));
            var after = Sim.GetState(Player);
            if (after.State != playerBefore.State && after.State is ActionState.HomingDashFloat
                or ActionState.HomingDashArrive or ActionState.HomingDashMiss)
            {
                var d = Sim.GetState(Dummy);
                DashTrace.Add($"t{Tick} {after.State} p=({after.PX:F2},{after.PY:F2},{after.PZ:F2}) "
                    + $"d=({d.PX:F2},{d.PY:F2},{d.PZ:F2}) dummyGround={d.IsGrounded}");
            }
            foreach (var hit in Sim.LastTickHits)
                Contacts.Add($"t{Tick} slot{hit.AttackSlot} air={hit.Airborne} blocked={hit.Blocked} "
                    + $"owner={hit.OwnerEntityId} dummyBefore={dummyBefore.State}/ground={dummyBefore.IsGrounded} "
                    + $"playerBefore={playerBefore.State} seq={Sim.GetState(Player).AttackSequence}");
            Tick++;
        }

        public void Wait(int ticks) { for (int i = 0; i < ticks; i++) Step(); }
    }

    /// <summary>The live tutorial's dummy (TutorialMatch): Bonk, or FightGuy when the player is Bonk.</summary>
    private static CharacterClass DummyFor(CharacterClass fighter)
        => fighter == CharacterClass.Bonk ? CharacterClass.FightGuy : CharacterClass.Bonk;

    public static IEnumerable<object[]> BeelineFighters()
    {
        foreach (var fighter in new[] { CharacterClass.FightGuy, CharacterClass.Manki, CharacterClass.Wibou,
            CharacterClass.Bonk, CharacterClass.Bashie })
            yield return new object[] { fighter };
        try { BuiltInContentResolver.Resolve(CharacterClass.Salamander); }
        catch (InvalidDataException) { yield break; }
        yield return new object[] { CharacterClass.Salamander };
    }

    private static string Snapshot(Lab lab)
    {
        var p = lab.Sim.GetState(Player); var d = lab.Sim.GetState(Dummy);
        return $"t{lab.Tick} player={p.State} ({p.PX:F2},{p.PY:F2},{p.PZ:F2}) ground={p.IsGrounded} deaths={p.Deaths}; "
            + $"dummy={d.State} ({d.PX:F2},{d.PY:F2},{d.PZ:F2}) ground={d.IsGrounded}; "
            + string.Join("; ", lab.DashTrace.Concat(lab.Contacts));
    }

    [Theory]
    [MemberData(nameof(BeelineFighters))]
    public void RealBeelineGroundAndAerialHitsAreFeasible(CharacterClass fighter)
    {
        foreach (bool aerial in new[] { false, true })
        {
            bool passed = false;
            string evidence = "";
            foreach (byte slot in new[] { AbilitySlots.Slot1, AbilitySlots.Slot3, AbilitySlots.Slot4, AbilitySlots.R })
            {
                var lab = new Lab(fighter, DummyFor(fighter), TutorialLessonId.Beeline, playerZ: -3f, dummyZ: 3f);
                lab.Step();
                if (aerial)
                {
                    lab.Step(new InputState { Jump = true, JumpHeld = true });
                    for (int t = 0; t < 9; t++) lab.Step(new InputState { JumpHeld = true });
                }
                lab.Step(new InputState { DashPressed = true });
                bool pressed = false;
                for (int t = 0; t < 180 && !lab.Progress.IsComplete(aerial ? 1 : 0); t++)
                {
                    bool press = !pressed && lab.Sim.GetState(Player).State == ActionState.HomingDash;
                    lab.Step(new InputState { ActiveSlot = press ? slot : AbilitySlots.None });
                    pressed |= press;
                }
                evidence = Snapshot(lab);
                if (!lab.Progress.IsComplete(aerial ? 1 : 0)) continue;
                _output.WriteLine($"{fighter} {(aerial ? "aerial" : "ground")}: {(aerial ? "Jump held 10t, " : "")}Dash, buffered slot {slot}; {evidence}");
                passed = true; break;
            }
            Assert.True(passed, $"{fighter} aerial={aerial}: {evidence}");
        }
    }

    [Theory]
    [MemberData(nameof(BeelineFighters))]
    public void RealBeelineOutOfShieldAndRecoveryAreFeasible(CharacterClass fighter)
    {
        var shield = new Lab(fighter, CharacterClass.Bonk, TutorialLessonId.BeelineTricks,
            playerZ: -1f, dummyZ: 3f);
        bool blocked = false;
        for (int t = 0; t < 600 && !shield.Progress.IsComplete(0); t++)
        {
            bool dash = blocked && shield.Sim.GetState(Player).State == ActionState.Shielding
                && shield.Sim.GetState(Player).BlockStunTicks == 0;
            shield.Step(new InputState { ShieldHeld = true, ShieldPressed = t == 0, DashPressed = dash });
            blocked |= shield.Sim.LastTickHits.Any(h => h.Blocked && h.TargetEntityId == Player);
        }
        Assert.True(blocked && shield.Progress.IsComplete(0), $"{fighter} shield: {Snapshot(shield)}");
        Assert.Equal(TutorialDummyBehavior.Idle, shield.Progress.DummyBehavior);
        _output.WriteLine($"{fighter} shield: hold Shield, block, Dash after blockstun; {Snapshot(shield)}");

        // The plain south edge keeps this recovery clear of the new western trench.
        var recovery = new Lab(fighter, DummyFor(fighter), TutorialLessonId.BeelineTricks,
            playerX: 0f, playerZ: -15.5f, dummyX: 0f, dummyZ: -9.5f);
        recovery.Sim.SetState(Player, recovery.Sim.GetState(Player) with
        {
            IsGrounded = false, PY = recovery.PlayerEntry.Definition.CapsuleHeight * 0.5f - 0.7f,
            FacingYaw = 0f, AimYaw = 0f,
        });
        recovery.Step(dummy: default(InputState));
        recovery.Step(new InputState { DashPressed = true }, default(InputState));
        for (int t = 0; t < 180 && !recovery.Progress.IsComplete(1); t++) recovery.Step(dummy: default(InputState));
        Assert.True(recovery.Progress.IsComplete(1), $"{fighter} recovery: {Snapshot(recovery)}");
        Assert.Equal(0, recovery.Sim.GetState(Player).Deaths);
        _output.WriteLine($"{fighter} recovery: off the south edge z=-15.5, feet=-0.7, Dash toward dummy z=-9.5; {Snapshot(recovery)}");
    }

    [Theory]
    [MemberData(nameof(BeelineFighters))]
    public void RealBeelineChaseIsFeasible(CharacterClass fighter)
    {
        var lab = new Lab(fighter, DummyFor(fighter), TutorialLessonId.BeelineTricks, playerZ: 0f, dummyZ: 1.2f);
        lab.Sim.SetState(Dummy, lab.Sim.GetState(Dummy) with { DamagePercent = 30 });
        lab.Step(new InputState { ActiveSlot = AbilitySlots.Slot3 }, default(InputState));
        bool dashed = false, pressed = false;
        for (int t = 0; t < 240 && !lab.Progress.IsComplete(2); t++)
        {
            var p = lab.Sim.GetState(Player);
            bool dash = !dashed && lab.Contacts.Count > 0 && !lab.Sim.GetState(Dummy).IsGrounded;
            bool press = dashed && !pressed && p.State == ActionState.HomingDash;
            lab.Step(new InputState { DashPressed = dash, ActiveSlot = press ? AbilitySlots.Slot1 : AbilitySlots.None },
                default(InputState));
            dashed |= lab.Sim.GetState(Player).State == ActionState.HomingDashFloat;
            pressed |= press;
        }
        Assert.True(lab.Progress.IsComplete(2), $"{fighter} chase: {Snapshot(lab)}");
        _output.WriteLine($"{fighter} chase: up normal, Beeline once the dummy is airborne, normal 1 buffered; {Snapshot(lab)}");
    }

    [Fact]
    public void RealJumpsAndFastFallAreDetected()
    {
        var lab = new Lab(CharacterClass.FightGuy, CharacterClass.Bonk, TutorialLessonId.Jump);
        lab.Step(new InputState { Jump = true, JumpHeld = true });
        for (int i = 0; i < 25; i++) lab.Step(new InputState { JumpHeld = true });
        lab.Step(new InputState { Jump = true, JumpHeld = true });
        lab.Wait(5);
        Assert.True(lab.Progress.IsComplete(1), "an air jump completes the double-jump objective");

        lab.Progress.StartNextLesson();
        Assert.Equal(TutorialLessonId.FastFall, lab.Progress.Lesson.Id);
        for (int i = 0; i < 60 && lab.Sim.GetState(Player).VY >= 0f; i++) lab.Step();
        lab.Step(new InputState { Down = true, DownPressed = true });
        Assert.True(lab.Sim.GetState(Player).IsFastFalling);
        Assert.Equal(1f, lab.Progress.Amount(0));
    }

    [Theory]
    [InlineData(CharacterClass.FightGuy)]
    [InlineData(CharacterClass.Manki)]
    [InlineData(CharacterClass.Wibou)]
    [InlineData(CharacterClass.Bonk)]
    public void EverySpecialPressIsDetectedForEachFighter(CharacterClass fighter)
    {
        foreach (byte slot in new[] { AbilitySlots.A, AbilitySlots.E, AbilitySlots.R, AbilitySlots.F })
        {
            var lab = new Lab(fighter, fighter == CharacterClass.Bonk ? CharacterClass.FightGuy : CharacterClass.Bonk,
                TutorialLessonId.Specials, dummyZ: 12f);
            int index = Array.FindIndex(lab.Progress.Lesson.Objectives.ToArray(), o => o.Id == slot switch
            {
                AbilitySlots.A => TutorialObjectiveId.UseSpecialA, AbilitySlots.E => TutorialObjectiveId.UseSpecialE,
                AbilitySlots.R => TutorialObjectiveId.UseSpecialR, _ => TutorialObjectiveId.UseSpecialF,
            });
            lab.Step(new InputState { ActiveSlot = slot });
            lab.Wait(3);
            Assert.True(lab.Progress.IsComplete(index), $"{fighter} special {slot} was not detected");
        }
    }

    [Fact]
    public void SparringDummyAttacksCanBeBlocked()
    {
        var lab = new Lab(CharacterClass.FightGuy, CharacterClass.Bonk, TutorialLessonId.ShieldAndDodge,
            playerZ: -1f, dummyZ: 3f);
        for (int i = 0; i < 600 && !lab.Progress.IsComplete(0); i++)
            lab.Step(new InputState { ShieldHeld = true, ShieldPressed = i == 0 });
        Assert.True(lab.Progress.IsComplete(0), "three sparring swings should be blocked within ten seconds");
        Assert.Equal(0, lab.Sim.GetState(Player).DamagePercent);
    }

    [Fact]
    public void RealGrabAndThrowAreDetected()
    {
        var lab = new Lab(CharacterClass.FightGuy, CharacterClass.Bonk, TutorialLessonId.GrabAndThrow,
            playerZ: 0f, dummyZ: 1.1f);
        lab.Step(new InputState { GrabPressed = true });
        lab.Wait(DefenseConfig.GrabStartupTicks + 1);
        Assert.True(lab.Progress.IsComplete(0), "grab: " + lab.Sim.GetState(Player).State);
        lab.Step(new InputState { ActiveSlot = AbilitySlots.Slot1 });
        lab.Wait(DefenseConfig.ThrowReleaseTicks + 1);
        Assert.True(lab.Progress.LessonComplete, "throw: " + lab.Sim.GetState(Dummy).State);
    }
}
