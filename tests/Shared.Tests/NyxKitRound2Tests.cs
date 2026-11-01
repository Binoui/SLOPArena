using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Nyx's round 2 (ADR-0051 sections 9 to 11) on her own package, from source and from the saved cook: Hex Crow's dive
/// lifecycle, Grasp's marked and unmarked branches, and Nyxxie's telegraphed Ring Light. Timing and reach come from
/// her cooked data, so they assert the rules, not her tuning.
/// </summary>
public sealed class NyxKitRound2Tests(ITestOutputHelper output)
{
    private const ulong Nyx = FormsFixture.Nyx, Dummy = FormsFixture.Dummy;

    internal sealed class Duel
    {
        public readonly ServerSimulation Sim;
        public readonly CharacterDefinition Def;
        public readonly List<SpellResolver.HitResult> Hits = new();
        public readonly List<TimelinePresentationEvent> Events = new();

        public Duel(bool saved, byte form = 0, float distance = 6f, float dummyYawDegrees = 180f)
        {
            var content = BuiltInContentResolver.Resolve(CharacterClass.Nyx);
            Def = saved ? content.Definition : TestHelpers.ResolveSourceDef("nyx");
            Sim = FormsFixture.Sim(Def, form);
            if (saved) Sim.RegisterEntity(Nyx, Def, Sim.GetState(Nyx), content.BakedAnimation);
            var dummy = Sim.GetState(Dummy);
            dummy.PZ = FormsFixture.Z + distance;
            dummy.FacingYaw = dummyYawDegrees * MathF.PI / 180f;
            dummy.TargetEntityId = (byte)Nyx;
            Sim.SetState(Dummy, dummy);
        }

        public CharacterState Her => Sim.GetState(Nyx);
        public CharacterState Victim => Sim.GetState(Dummy);

        public void Step(InputState nyx = default, InputState dummy = default)
        {
            nyx.TargetEntityId = (byte)Dummy;
            dummy.TargetEntityId = (byte)Nyx;
            Sim.Tick(new Dictionary<ulong, InputState> { [Nyx] = nyx, [Dummy] = dummy });
            Hits.AddRange(Sim.LastTickHits.Where(h => h.OwnerEntityId == Nyx));
            Events.AddRange(Sim.GetPresentationEvents(clear: true));
        }

        public void Press(byte slot) => Step(new InputState { ActiveSlot = slot });

        public void Until(Func<bool> done, int maxTicks, Func<InputState>? dummy = null)
        {
            for (int tick = 0; tick < maxTicks && !done(); tick++) Step(default, dummy?.Invoke() ?? default);
            Assert.True(done(), $"not reached within {maxTicks} ticks");
        }

        public Hitbox? Crow => Sim.Resolver.GetActiveHitboxes()
            .Where(h => h.OwnerId == Nyx && h.Perch.HasValue).Select(h => (Hitbox?)h).FirstOrDefault();

        public PerchPhase? Phase => Crow?.Perch!.Value.Phase;

        public CookedProjectile CrowData => Def.ForForm(0).GetCookedSlotAbility(AbilitySlots.A, false)!.Timeline.Stages[0]
            .Operations.OfType<CookedSpawnProjectileOperation>().Single().Projectile;

        public CookedTetherAtTarget Grasp => ((CookedTetherCapabilityParameters)Def.ForForm(0)
            .GetCookedSlotAbility(AbilitySlots.R, false)!.Timeline.Stages[0].Operations
            .OfType<CookedStartCapabilityOperation>().Single().Parameters).AtTarget!;

        /// <summary>Throws the crow and steps to the first tick of the caw.</summary>
        public void ToCaw()
        {
            Press(AbilitySlots.A);
            Until(() => Phase == PerchPhase.Marking, 120);
        }

        /// <summary>Throws the crow and steps until it perches on the dummy, which stands still.</summary>
        public void Perch()
        {
            Press(AbilitySlots.A);
            Until(() => Crow is { AttachedEntityId: Dummy }, 160);
            Until(() => Sim.GetActiveAbility(Nyx) == null, 60);
        }

        public bool Scattered => Events.Any(e => e.PresentationId == CrowData.Perch!.ScatterPresentationId);

        public bool Free => Sim.GetActiveAbility(Nyx) == null
            || Simulation.IsIasaUnlocked(Sim.GetState(Nyx), Sim.GetActiveMove(Nyx));
    }

    public static TheoryData<bool> Saved => new() { false, true };

    private static string RepoRoot
    {
        get
        {
            var directory = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !System.IO.Directory.Exists(System.IO.Path.Combine(directory.FullName, "client", "Unity")))
                directory = directory.Parent;
            Assert.NotNull(directory);
            return directory!.FullName;
        }
    }

    [Theory]
    [MemberData(nameof(Saved))]
    public void TheCrowRisesOverItsTargetCawsThenDivesOntoTheSpot(bool saved)
    {
        var duel = new Duel(saved);
        var dive = duel.CrowData.Perch!.Dive!;
        duel.Press(AbilitySlots.A);
        duel.Until(() => duel.Crow != null, 40);
        var phases = new List<PerchPhase>();
        var hover = default(Hitbox);
        int markTicks = 0;
        for (int tick = 0; tick < 160 && duel.Phase is { } phase && phase != PerchPhase.Perched; tick++)
        {
            if (phase == PerchPhase.Marking && markTicks++ == 0) hover = duel.Crow!.Value;
            if (phases.Count == 0 || phases[^1] != phase) phases.Add(phase);
            Assert.Equal((ushort)0, duel.Victim.DamagePercent);
            duel.Step();
        }
        Assert.Equal(new[] { PerchPhase.Rising, PerchPhase.Marking, PerchPhase.Diving }, phases);
        Assert.Equal(dive.MarkTicks + 1, markTicks);
        var victim = duel.Victim;
        float feet = victim.PY - TestHelpers.EngineDef.CapsuleHeight * 0.5f;
        Assert.Equal(victim.PX, hover.X, 1);
        Assert.Equal(victim.PZ, hover.Z, 1);
        Assert.Equal(feet + dive.HoverHeight, hover.Y, 1);
        Assert.Equal(Dummy, duel.Crow!.Value.AttachedEntityId);
        Assert.Equal((ushort)0, duel.Victim.DamagePercent);
        Assert.Equal(ActionState.Idle, duel.Victim.State);
    }

    public static TheoryData<bool, string, bool> Escapes() => new()
    {
        { false, "stand", true }, { false, "jump", true }, { false, "beeline", false }, { false, "air dodge", false },
        { true, "stand", true }, { true, "jump", true }, { true, "beeline", false }, { true, "air dodge", false },
    };

    [Theory]
    [MemberData(nameof(Escapes))]
    public void ADashOrAnAirDodgeAtTheCawAvoidsTheDive(bool saved, string escape, bool perches)
    {
        var duel = new Duel(saved);
        duel.ToCaw();
        int tick = 0;
        InputState Dodge()
        {
            tick++;
            return escape switch
            {
                "beeline" => new InputState { DashPressed = tick == 1 },
                "jump" => new InputState { Jump = tick == 1, JumpHeld = true },
                "air dodge" => new InputState { Jump = tick == 1, JumpHeld = true, MoveX = 1f,
                    ShieldPressed = !duel.Victim.IsGrounded && duel.Victim.State != ActionState.AirDodgeMovement
                        && duel.Victim.AirDodgesLeft > 0 },
                _ => default,
            };
        }
        for (int i = 0; i < 80 && duel.Crow is { Perch.Phase: not PerchPhase.Perched }; i++) duel.Step(default, Dodge());
        output.WriteLine($"{escape}: crow {(duel.Crow is { } c ? c.Perch!.Value.Phase.ToString() : "gone")}");
        Assert.Equal(perches, duel.Crow is { AttachedEntityId: Dummy });
        if (!perches) Assert.True(duel.Scattered, "a missed dive scatters the crow");
    }

    [Theory]
    [InlineData(false, PerchPhase.Rising)]
    [InlineData(false, PerchPhase.Marking)]
    [InlineData(false, PerchPhase.Diving)]
    [InlineData(true, PerchPhase.Rising)]
    [InlineData(true, PerchPhase.Marking)]
    [InlineData(true, PerchPhase.Diving)]
    public void AnAttackBreaksTheCrowInEveryFlightPhase(bool saved, PerchPhase phase)
    {
        var duel = new Duel(saved, distance: 9f);
        duel.Press(AbilitySlots.A);
        duel.Until(() => duel.Phase == phase, 160);
        var crow = duel.Crow!.Value;
        duel.Sim.Resolver.Spawn(new Hitbox
        {
            X = crow.X, Y = crow.Y, Z = crow.Z, Radius = 0.6f, Shape = HitboxShape.Sphere, Damage = 4f,
            BaseKnockback = 4f, KnockbackAngle = 30, StunTicks = 8, DurationTicks = 2, OwnerId = Dummy,
            CancelWithAbility = true, HitsMultipleOpponents = true, HitEntities = new HashSet<ulong>(),
        });
        duel.Step();
        Assert.Null(duel.Crow);
        Assert.True(duel.Scattered);
        Assert.Equal((ushort)0, duel.Victim.DamagePercent);
    }

    [Theory]
    [MemberData(nameof(Saved))]
    public void APerchedCrowPecksItsCarrierOnItsCadenceWithoutAReaction(bool saved)
    {
        var duel = new Duel(saved, distance: 9f);
        var peck = duel.CrowData.Perch!.Peck!;
        duel.Perch();
        int perchedAt = duel.Crow!.Value.Perch!.Value.PhaseTicks;
        var damage = new List<(int Tick, ushort Percent)>();
        ushort last = duel.Victim.DamagePercent;
        for (int tick = 1; tick <= 3 * peck.IntervalTicks; tick++)
        {
            duel.Step();
            var victim = duel.Victim;
            Assert.Equal(ActionState.Idle, victim.State);
            Assert.Equal((ushort)0, victim.HitstunTicks);
            Assert.Equal((ushort)0, victim.HitstopTicks);
            if (victim.DamagePercent != last) damage.Add((perchedAt + tick, victim.DamagePercent));
            last = victim.DamagePercent;
        }
        Assert.Equal(new[] { 1, 2, 3 }.Select(n => (n * (int)peck.IntervalTicks, (ushort)(n * peck.Damage))), damage);
        Assert.DoesNotContain(duel.Hits, h => h.TargetEntityId == Dummy);
        Assert.Equal(3, duel.Events.Count(e => e.PresentationId == peck.PresentationId));
    }

    [Theory]
    [InlineData(false, "shield")]
    [InlineData(false, "air dodge")]
    [InlineData(true, "shield")]
    [InlineData(true, "air dodge")]
    public void ShieldingOrAnAirDodgeSwatsThePerchedCrowOff(bool saved, string defense)
    {
        var duel = new Duel(saved, distance: 9f);
        duel.Perch();
        int tick = 0;
        InputState Defend()
        {
            tick++;
            return defense == "shield" ? new InputState { ShieldHeld = true }
                : new InputState { Jump = tick == 1, JumpHeld = true, ShieldPressed = !duel.Victim.IsGrounded };
        }
        duel.Until(() => duel.Crow == null, 20, Defend);
        Assert.True(duel.Scattered, "the swat scatters the crow");
        Assert.Equal(defense == "shield" ? ActionState.Shielding : ActionState.AirDodgeMovement, duel.Victim.State);
        Assert.Equal((ushort)0, duel.Victim.DamagePercent);
    }

    [Theory]
    [MemberData(nameof(Saved))]
    public void NyxxiesNextHitStillPopsADivedCrow(bool saved)
    {
        var duel = new Duel(saved, distance: 3f);
        var pop = duel.CrowData.Perch!.Pop;
        duel.Perch();
        duel.Until(() => duel.Free, 60);
        duel.Press(AbilitySlots.F);
        duel.Until(() => duel.Free && duel.Her.Form.Active == 1, 80);
        Assert.Equal(Dummy, duel.Crow!.Value.AttachedEntityId);
        var dummy = duel.Victim;
        dummy.PZ = duel.Her.PZ + 1.2f;
        duel.Sim.SetState(Dummy, dummy);
        duel.Press(AbilitySlots.Slot1);
        duel.Until(() => duel.Hits.Any(h => h.AttackSlot == AbilitySlots.A), 80);
        var slap = duel.Hits.First(h => h.Form == 1);
        var popped = duel.Hits.Single(h => h.AttackSlot == AbilitySlots.A);
        Assert.Equal(0, popped.Form);
        Assert.Equal(pop.Damage, popped.Damage);
        Assert.True(popped.MatchTick >= slap.MatchTick + slap.HitstopTicks, "the pop waits out the slap's hitstop");
        Assert.Null(duel.Crow);
        Assert.False(duel.Scattered, "a pop is the crow's payoff, not a scatter");
    }

    [Theory]
    [InlineData(false, PerchPhase.Rising)]
    [InlineData(false, PerchPhase.Marking)]
    [InlineData(false, PerchPhase.Diving)]
    [InlineData(false, PerchPhase.Perched)]
    [InlineData(true, PerchPhase.Rising)]
    [InlineData(true, PerchPhase.Marking)]
    [InlineData(true, PerchPhase.Diving)]
    [InlineData(true, PerchPhase.Perched)]
    public void ARestoreInEveryCrowPhaseMatchesTheContinuousRun(bool saved, PerchPhase phase)
    {
        var continuous = new Duel(saved);
        continuous.Press(AbilitySlots.A);
        continuous.Until(() => continuous.Phase == phase, 160);
        continuous.Step();
        var restored = new Duel(saved);
        var snapshot = continuous.Sim.CaptureSnapshot();
        restored.Sim.RestoreSnapshot(snapshot);
        Assert.True(snapshot.ContentEquals(restored.Sim.CaptureSnapshot()));
        var peck = continuous.CrowData.Perch!.Peck!.IntervalTicks;
        for (int tick = 0; tick < 60 + peck; tick++)
        {
            continuous.Step();
            restored.Step();
            Assert.True(continuous.Sim.CaptureSnapshot().ContentEquals(restored.Sim.CaptureSnapshot()),
                $"diverged {tick + 1} ticks after a restore in {phase}");
        }
        Assert.Equal(continuous.Victim.DamagePercent, restored.Victim.DamagePercent);
    }

    [Theory]
    [MemberData(nameof(Saved))]
    public void GraspDragsACrowedTargetToHerFeet(bool saved)
    {
        var duel = new Duel(saved, distance: 6f);
        duel.Perch();
        duel.Until(() => duel.Free && duel.Sim.GetActiveAbility(Nyx) == null, 60);
        duel.Press(AbilitySlots.R);
        duel.Until(() => duel.Victim.State == ActionState.Reeled, 80);
        duel.Until(() => duel.Sim.GetActiveAbility(Nyx) == null, 160);
        var her = duel.Her;
        var victim = duel.Victim;
        float distance = MathF.Sqrt((victim.PX - her.PX) * (victim.PX - her.PX) + (victim.PZ - her.PZ) * (victim.PZ - her.PZ));
        Assert.InRange(distance, 0.6f, 1.6f);
        Assert.Equal(Dummy, duel.Crow!.Value.AttachedEntityId);
    }

    [Theory]
    [MemberData(nameof(Saved))]
    public void WithoutACrowGraspPopsUpATargetInFrontWithNoDrag(bool saved)
    {
        var duel = new Duel(saved, distance: 1.6f);
        var hand = duel.Grasp.Unmarked!;
        duel.Press(AbilitySlots.R);
        uint pressed = duel.Sim.CurrentTick;
        duel.Until(() => duel.Hits.Count > 0, 60);
        var pop = Assert.Single(duel.Hits);
        Assert.Equal(pressed + hand.RiseTick - 1, pop.MatchTick);
        Assert.Equal(hand.Damage, pop.Damage);
        Assert.Equal(0ul, duel.Her.InteractionId);
        duel.Until(() => duel.Victim.HitstopTicks == 0, 30);
        duel.Step();
        Assert.NotEqual(ActionState.Reeled, duel.Victim.State);
        Assert.True(duel.Victim.KVY > 0f || duel.Victim.VY > 0f, "the hand pops the target up");
        duel.Until(() => duel.Sim.GetActiveAbility(Nyx) == null, 120);
        Assert.InRange((int)(duel.Sim.CurrentTick - pressed), hand.RiseTick + hand.ActiveTicks + hand.RecoveryTicks - 3,
            hand.RiseTick + hand.ActiveTicks + hand.RecoveryTicks + 12);
    }

    [Theory]
    [MemberData(nameof(Saved))]
    public void WithoutACrowGraspCannotReachAFarTarget(bool saved)
    {
        var duel = new Duel(saved, distance: 6f);
        duel.Press(AbilitySlots.R);
        duel.Until(() => duel.Sim.GetActiveAbility(Nyx) == null, 160);
        Assert.Empty(duel.Hits);
        Assert.Equal(ActionState.Idle, duel.Victim.State);
        Assert.Equal(0ul, duel.Victim.InteractionId);
    }

    [Theory]
    [MemberData(nameof(Saved))]
    public void ARestoreDuringTheUnmarkedHandMatchesTheContinuousRun(bool saved)
    {
        var continuous = new Duel(saved, distance: 1.6f);
        continuous.Press(AbilitySlots.R);
        continuous.Until(() => continuous.Sim.Resolver.GetActiveHitboxes().Any(h => h.OwnerId == Nyx), 40);
        continuous.Hits.Clear();
        var restored = new Duel(saved, distance: 1.6f);
        var snapshot = continuous.Sim.CaptureSnapshot();
        restored.Sim.RestoreSnapshot(snapshot);
        Assert.True(snapshot.ContentEquals(restored.Sim.CaptureSnapshot()));
        for (int tick = 0; tick < 70; tick++)
        {
            continuous.Step();
            restored.Step();
            Assert.True(continuous.Sim.CaptureSnapshot().ContentEquals(restored.Sim.CaptureSnapshot()),
                $"diverged {tick + 1} ticks after the restore");
        }
        Assert.Equal(continuous.Hits.Select(h => (h.MatchTick, h.Damage)), restored.Hits.Select(h => (h.MatchTick, h.Damage)));
    }

    private static int RingLightFlashTick(CharacterDefinition def) => def.ForForm(1).GetCookedSlotAbility(AbilitySlots.A, false)!
        .Timeline.Stages[0].Operations.OfType<CookedSpawnHitboxOperation>().Min(op => op.Tick);

    private static float RingLightReach(CharacterDefinition def) => def.ForForm(1).GetCookedSlotAbility(AbilitySlots.A, false)!
        .Timeline.Stages[0].Operations.OfType<CookedSpawnHitboxOperation>()
        .Max(op => MathF.Sqrt(op.Hitbox.OffsetX * op.Hitbox.OffsetX + op.Hitbox.OffsetZ * op.Hitbox.OffsetZ) + op.Hitbox.Radius);

    [Theory]
    [InlineData(false, 180f, true)]
    [InlineData(false, 0f, false)]
    [InlineData(true, 180f, true)]
    [InlineData(true, 0f, false)]
    public void RingLightWindsUpInPlaceThenDazzlesOnlyAFacingTargetAtMidRange(bool saved, float dummyYaw, bool dazzled)
    {
        var duel = new Duel(saved, form: 1, distance: 4.4f, dummyYawDegrees: dummyYaw);
        int flash = RingLightFlashTick(duel.Def);
        Assert.InRange(flash, 18, 40);
        var start = duel.Her;
        duel.Press(AbilitySlots.A);
        uint pressed = duel.Sim.CurrentTick;
        for (int tick = 0; tick < flash + 8; tick++) duel.Step();
        Assert.Equal(start.PX, duel.Her.PX, 3);
        Assert.Equal(start.PZ, duel.Her.PZ, 3);
        Assert.Equal(dazzled, duel.Hits.Count == 1);
        if (!dazzled)
        {
            Assert.Equal((ushort)0, duel.Victim.HitstunTicks);
            return;
        }
        var hit = duel.Hits.Single();
        Assert.Equal(pressed + (uint)flash - 1, hit.MatchTick);
        Assert.Equal(0f, hit.Damage);
        Assert.True(duel.Victim.HitstunTicks > 0);
        Assert.Equal(0f, duel.Victim.KVX, 3);
        Assert.Equal(0f, duel.Victim.KVZ, 3);
    }

    [Theory]
    [MemberData(nameof(Saved))]
    public void RingLightReachesMidRangeAndNoFurther(bool saved)
    {
        var def = new Duel(saved).Def;
        float reach = RingLightReach(def);
        Assert.InRange(reach, 4f, 6f);
        var far = new Duel(saved, form: 1, distance: reach + TestHelpers.EngineDef.CapsuleRadius + 0.6f);
        far.Press(AbilitySlots.A);
        for (int tick = 0; tick < RingLightFlashTick(def) + 8; tick++) far.Step();
        Assert.Empty(far.Hits);
    }

    [Theory]
    [MemberData(nameof(Saved))]
    public void ALandedFlashGivesNyxxieTimeToRushInWithPhoneSlaps(bool saved)
    {
        // Near the flash's far edge, and with eight ticks to spare for a human's late Beeline.
        const int Slack = 8;
        var duel = new Duel(saved, form: 1, distance: 4.6f);
        duel.Press(AbilitySlots.A);
        duel.Until(() => duel.Hits.Count == 1, 60);
        uint dazzledAt = duel.Hits[0].MatchTick;
        void Check()
        {
            var victim = duel.Victim;
            Assert.True(victim.HitstunTicks > 0 || victim.HitstopTicks > 0 || duel.Hits.Count > 1,
                $"the target recovered {duel.Sim.CurrentTick - dazzledAt} ticks after the flash, before Nyxxie's slap");
        }
        while (!duel.Free) { duel.Step(); Check(); }
        duel.Step(new InputState { DashPressed = true });
        for (int tick = 0; tick < 60 && duel.Her.State != ActionState.HomingDashArrive; tick++) { duel.Step(); Check(); }
        Assert.Equal(ActionState.HomingDashArrive, duel.Her.State);
        duel.Press(AbilitySlots.Slot1);
        int left = duel.Victim.HitstunTicks;
        for (int tick = 0; tick < 30 && duel.Hits.Count == 1; tick++) { Check(); left = duel.Victim.HitstunTicks; duel.Step(); }
        var slap = duel.Hits[1];
        output.WriteLine($"slap {slap.MatchTick - dazzledAt} ticks after the flash, {left} dazzle ticks left");
        Assert.True(left >= Slack, $"only {left} dazzle ticks were left for the slap");
        Assert.Equal(AbilitySlots.Slot1, slap.AttackSlot);
        Assert.Equal(1, slap.Form);
    }

    [Fact]
    public void HerRound2BlocksCookAtRuntimeApi121AndRoundTripThroughTheSourceCodec()
    {
        var root = RepoRoot;
        var package = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "client/Unity/Assets/CharacterPackages/nyx/package.json"));
        var character = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "client/Unity/Assets/CharacterPackages/nyx/character.json"));
        var parsed = CharacterPackageSourceCodec.Load(package, character);
        Assert.True(parsed.IsValid, string.Join("; ", parsed.Diagnostics));
        var again = CharacterPackageSourceCodec.Load(CharacterPackageSourceCodec.SerializeManifest(parsed.Source!.Manifest),
            CharacterPackageSourceCodec.SerializeCharacter(parsed.Source.Character));
        Assert.True(again.IsValid, string.Join("; ", again.Diagnostics));
        var compiled = CharacterPackageCompiler.Compile(parsed.Source).CookedPackage;
        var recompiled = CharacterPackageCompiler.Compile(again.Source!).CookedPackage;
        Assert.NotNull(compiled);
        Assert.Equal(compiled!.CanonicalBytes, recompiled!.CanonicalBytes);
        Assert.Equal("1.21.0", compiled.Metadata.RuntimeApiMin);
        var real = CookedCharacterRuntimeAdapter.ToCharacterDefinition(compiled).ForForm(0);
        var crow = real.GetCookedSlotAbility(AbilitySlots.A, false)!.Timeline.Stages[0].Operations
            .OfType<CookedSpawnProjectileOperation>().Single().Projectile;
        Assert.Null(crow.Homing);
        Assert.NotNull(crow.Perch!.Dive);
        Assert.NotNull(crow.Perch.Peck);
        var grasp = (CookedTetherCapabilityParameters)real.GetCookedSlotAbility(AbilitySlots.R, false)!.Timeline.Stages[0]
            .Operations.OfType<CookedStartCapabilityOperation>().Single().Parameters;
        Assert.NotNull(grasp.AtTarget!.Unmarked);
    }

    private static void AddUnmarkedHand(System.Text.Json.Nodes.JsonObject character)
    {
        GraspTests.PlaceCatch(character);
        TetherTests.EditTether(character, p => p["atTarget"]!["unmarked"] = System.Text.Json.Nodes.JsonNode.Parse(
            """{"riseTick":10,"activeTicks":5,"distance":1.2,"radius":0.6,"height":2,"damage":8,"angle":80,"baseKnockback":6,"knockbackGrowth":20,"stunTicks":16,"recoveryTicks":20}"""));
    }

    [Fact]
    public void TheUnmarkedHandNeedsRuntimeApi121AtTheLoader()
    {
        var compiled = RingDuel.Compile(AddUnmarkedHand);
        Assert.Equal("1.21.0", compiled.Metadata.RuntimeApiMin);
        var admitted = CookedCharacterPackageLoader.LoadAssembly(CharacterPackageAssembler.Assemble(ArmorAuthoringTests.BuildInput(compiled)));
        Assert.True(admitted.IsValid, string.Join("; ", admitted.Diagnostics));
        Assert.Equal(compiled.CanonicalBytes, admitted.Package!.CanonicalBytes);
        Assert.Equal(new CookedTetherUnmarked(10, 5, 1.2f, .6f, 2f, 8f, 80f, 6f, 20f, 16, 20),
            RingDuel.TetherOf(admitted.ToCharacterDefinition(CharacterClass.Bashie)).AtTarget!.Unmarked);
        var older = new CookedCharacterPackage(compiled.Metadata with { RuntimeApiMin = "1.20.0" }, compiled.Definition,
            compiled.Budget, compiled.Diagnostics,
            System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(compiled.CanonicalBytes).Replace("1.21.0", "1.20.0")));
        var rejected = CookedCharacterPackageLoader.LoadAssembly(CharacterPackageAssembler.Assemble(ArmorAuthoringTests.BuildInput(older)));
        Assert.False(rejected.IsValid);
        Assert.Contains(rejected.Diagnostics, d => d.Code == "package.compatibility.unsupported");
    }

    public static TheoryData<string, string> InvalidBlocks() => new()
    {
        { "homing on a diving perch", """{"turnDegreesPerTick":3}""" },
        { "no mark", "markTicks:0" },
        { "a fractional peck", "peckDamage:1.5" },
        { "a peck slower than the perch", "peckInterval:2000" },
        { "a hand with no reach", "handRadius:0" },
        { "a flat hand", "handHeight:0.5" },
    };

    [Theory]
    [MemberData(nameof(InvalidBlocks))]
    public void TheCompilerRejectsInvalidDivesPecksAndHands(string name, string edit)
    {
        var root = RepoRoot;
        var package = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "client/Unity/Assets/CharacterPackages/nyx/package.json"));
        var character = System.Text.Json.Nodes.JsonNode.Parse(System.IO.File.ReadAllText(
            System.IO.Path.Combine(root, "client/Unity/Assets/CharacterPackages/nyx/character.json")))!;
        var slots = character["slots"]!.AsArray();
        var crow = slots.First(s => (string)s!["id"]! == "ground.A")!["timeline"]!["stages"]![0]!["operations"]!.AsArray()
            .First(op => (string)op!["kind"]! == "spawnProjectile")!["projectile"]!;
        var hand = slots.First(s => (string)s!["id"]! == "ground.R")!["timeline"]!["stages"]![0]!["operations"]!.AsArray()
            .First(op => (string)op!["kind"]! == "startCapability")!["parameters"]!["atTarget"]!["unmarked"]!;
        if (edit.StartsWith("{")) crow["homing"] = System.Text.Json.Nodes.JsonNode.Parse(edit);
        else
        {
            var (field, value) = (edit.Split(':')[0], double.Parse(edit.Split(':')[1], System.Globalization.CultureInfo.InvariantCulture));
            switch (field)
            {
                case "markTicks": crow["perch"]!["dive"]!["markTicks"] = (int)value; break;
                case "peckDamage": crow["perch"]!["peck"]!["damage"] = value; break;
                case "peckInterval": crow["perch"]!["peck"]!["intervalTicks"] = (int)value; break;
                case "handRadius": hand["radius"] = value; break;
                case "handHeight": hand["height"] = value; break;
            }
        }
        var compiled = CharacterPackageCompiler.Compile(package, character.ToJsonString());
        Assert.True(compiled.CookedPackage == null, $"{name}: the compiler admitted it");
    }
}
