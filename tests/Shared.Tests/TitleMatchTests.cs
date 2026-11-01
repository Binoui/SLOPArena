using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using SlopArena.Shared.Abilities;
using SlopArena.Shared.AI;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// FightGuy's Title Match (ADR-0053) through his live package, a command grab with a hold stage (ADR-0057): the lunge
/// catches through a shield, the hold stage's hits land on the held fighter as damage and hitstop only, the kick launches
/// after the release, an empty lunge ends after the whiff recovery, and every way the pair can end converges on free
/// fighters.
/// </summary>
public sealed class TitleMatchTests
{
    private const ulong Guy = 1, Victim = 2, Third = 3;
    private static readonly InputState Grab = new() { ActiveSlot = AbilitySlots.F, TargetEntityId = (byte)Victim };
    private static readonly (CharacterDefinition Def, BakedAnimationData Poses) Fighter =
        (TestHelpers.ResolveSourceDef("fightguy"), BakedAnimationData.LoadFromBin(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "../../../../../content-cooked/fightguy/poses.bin"))));
    private static CookedSlotDefinition Move => Fighter.Def.GetCookedSlotAbility(AbilitySlots.F, false)!;
    private static CookedCommandGrabCapabilityParameters P => (CookedCommandGrabCapabilityParameters)Move.Timeline.Stages[0]
        .Operations.OfType<CookedStartCapabilityOperation>().Single().Parameters;
    private static int LungeStageTicks => P.CaptureTick + P.Capture.DurationTicks;

    [Fact]
    public void ACatchThroughAShieldPairsBothFightersAndHoldsTheVictimInFront()
    {
        var duel = new Duel().With(Victim, Standing(0f, 2f, 180f));
        duel.Step(Grab, (Victim, new InputState { ShieldHeld = true, ShieldPressed = true }));
        duel.RunUntil(() => duel.State(Victim).State == ActionState.CommandGrabbed, 20, (Victim, new InputState { ShieldHeld = true }));
        var holder = duel.State(Guy);
        var victim = duel.State(Victim);
        Assert.Equal(ActionState.Attacking, holder.State);
        Assert.NotEqual(0ul, holder.InteractionId);
        Assert.Equal(holder.InteractionId, victim.InteractionId);
        Assert.Equal((byte)DefenseInteractionPhase.CommandHold, holder.InteractionPhase);
        Assert.Equal((byte)DefenseInteractionPhase.CommandHold, victim.InteractionPhase);
        Assert.DoesNotContain(duel.Hits, h => h.Blocked);

        duel.Step(default, (Victim, new InputState { ShieldHeld = true }));
        holder = duel.State(Guy);
        victim = duel.State(Victim);
        float expected = MathF.Max(P.AnchorDistance!.Value, Fighter.Def.CapsuleRadius * 2f + .05f);
        float dx = victim.PX - holder.PX, dz = victim.PZ - holder.PZ;
        Assert.Equal(expected, MathF.Sqrt(dx * dx + dz * dz), 2);
        Assert.Equal(ActionState.CommandGrabbed, victim.State);
    }

    [Fact]
    public void TheLungeTakesTheCurrentTargetsBearingAtThePress()
    {
        var duel = new Duel().With(Victim, Standing(1.5f, 1.5f, 225f));
        duel.Step(Grab);
        Assert.Equal(MathF.PI / 4f, duel.State(Guy).FacingYaw, 3);
        duel.RunUntil(() => duel.State(Victim).State == ActionState.CommandGrabbed, 20);
    }

    [Fact]
    public void AHeldVictimTakesEveryHoldHitAsHitstopThenTheKickLaunchesIt()
    {
        var duel = new Duel().With(Victim, Standing(0f, 1.5f, 180f));
        duel.Step(Grab);
        duel.RunUntil(() =>
        {
            var v = duel.State(Victim);
            if (v.State == ActionState.CommandGrabbed) Assert.Equal((ushort)0, v.HitstunTicks);
            return v.State == ActionState.Hitstun;
        }, 300);
        var hold = Move.Timeline.Stages[1].Operations.OfType<CookedSpawnHitboxOperation>()
            .OrderBy(op => op.Tick).Select(op => op.Hitbox).ToArray();
        var onVictim = duel.Hits.Where(h => h.OwnerEntityId == Guy && h.TargetEntityId == Victim).ToArray();
        Assert.Equal(1 + hold.Length, onVictim.Length);
        Assert.All(onVictim.Skip(1).SkipLast(1), h => Assert.True(h.HitstopTicks > 0, "each held hit freezes"));
        Assert.Equal(P.Capture.Damage + hold.Sum(h => h.Damage), duel.State(Victim).DamagePercent);
        var kick = duel.State(Victim);
        Assert.True(Simulation.HasKnockback(kick) || kick.HitstopTicks > 0, "the kick launches after its freeze");
        Assert.Equal(0ul, kick.InteractionId);
        duel.RunUntil(() => duel.Sim.GetActiveAbility(Guy) == null, 120);
    }

    [Theory]
    [InlineData("range")]
    [InlineData("dodge")]
    [InlineData("wall")]
    public void AnEmptyLungeEndsTheMoveAfterTheWhiffRecovery(string miss)
    {
        var duel = miss switch
        {
            "range" => new Duel().With(Victim, Standing(0f, 9f, 180f)),
            "dodge" => new Duel(airborne: 2f).With(Victim, Airborne(Standing(0f, 3.2f, 180f), 2f) with { AirDodgesLeft = 1 }),
            _ => new Duel(BashieShellTests.Floor(wallZ: 1.2f)).With(Victim, Standing(0f, 3f, 180f)),
        };
        var dodge = miss == "dodge" ? new InputState { ShieldPressed = true, ShieldHeld = true, MoveX = 1f } : default;
        duel.Step(Grab);
        duel.Step(default, (Victim, dodge));
        int ticks = 2;
        while (duel.Sim.GetActiveAbility(Guy) != null && ticks < 200) { duel.Step(); ticks++; }
        Assert.DoesNotContain(duel.Hits, h => h.OwnerEntityId == Guy);
        Assert.NotEqual(ActionState.CommandGrabbed, duel.State(Victim).State);
        Assert.InRange(ticks, P.WhiffRecoveryTicks + 1, LungeStageTicks + P.WhiffRecoveryTicks);
        Assert.DoesNotContain(duel.Events, e => e.PresentationId == "presentation.fightguy.fist-of-fury.focus");
        var guy = duel.State(Guy);
        Assert.Equal((ushort)0, guy.AnimLockTicks);
        if (miss == "wall") Assert.True(guy.PZ < 1.2f, "the wall stopped the lunge");
    }

    [Theory]
    [InlineData(Guy)]
    [InlineData(Victim)]
    public void AThirdFightersHitOnEitherFighterFreesBoth(ulong struck)
    {
        var duel = Holding();
        duel.HitFrom(Third, struck, 6f);
        duel.Step();
        duel.Step();
        var victim = duel.State(Victim);
        Assert.Equal(0ul, victim.InteractionId);
        Assert.Equal(0ul, duel.State(Guy).InteractionId);
        Assert.NotEqual(ActionState.CommandGrabbed, victim.State);
        Assert.Contains(duel.Hits, h => h.OwnerEntityId == Third && h.TargetEntityId == struck);
        // His move pauses through the freeze of his own last hit, then ends instead of punching air.
        duel.RunUntil(() => duel.Sim.GetActiveAbility(Guy) == null, 20);
    }

    [Theory]
    [InlineData("knockout")]
    [InlineData("victim removed")]
    [InlineData("holder removed")]
    public void ADeathOrARemovalClearsThePair(string end)
    {
        var duel = Holding();
        switch (end)
        {
            case "knockout": duel.Sim.SetState(Victim, duel.State(Victim) with { PY = -100f }); break;
            case "victim removed": duel.Sim.RemoveEntity(Victim); break;
            default: duel.Sim.RemoveEntity(Guy); break;
        }
        duel.Step();
        duel.Step();
        foreach (var id in new[] { Guy, Victim })
        {
            if (!duel.Sim.GetAllStates().TryGetValue(id, out var s)) continue;
            Assert.Equal(0ul, s.InteractionId);
            Assert.NotEqual(ActionState.CommandGrabbed, s.State);
        }
        if (end != "holder removed") duel.RunUntil(() => duel.Sim.GetActiveAbility(Guy) == null, 20);
    }

    [Fact]
    public void AnAirHoldKeepsBothFightersAloftUntilTheRelease()
    {
        var duel = new Duel(airborne: 3f).With(Victim, Airborne(Standing(0f, 1.8f, 180f), 3f));
        duel.Step(Grab);
        duel.RunUntil(() => duel.State(Victim).State == ActionState.CommandGrabbed, 20);
        float holderY = duel.State(Guy).PY, victimY = duel.State(Victim).PY;
        duel.RunUntil(() => duel.State(Victim).InteractionId == 0, 300);
        Assert.Equal(holderY, duel.State(Guy).PY, 3);
        Assert.Equal(victimY, duel.State(Victim).PY, 1);
        Assert.False(duel.State(Guy).IsGrounded);
        var hold = Move.Timeline.Stages[1].Operations.OfType<CookedSpawnHitboxOperation>().Count();
        duel.RunUntil(() => duel.State(Victim).State == ActionState.Hitstun, 30);
        Assert.Equal(1 + hold, duel.Hits.Count(h => h.OwnerEntityId == Guy && h.TargetEntityId == Victim));
    }

    [Fact]
    public void AFighterAlreadyHeldRefusesTheCatchAndTheLungeWhiffs()
    {
        var duel = new Duel().With(Victim, Standing(0f, 1.5f, 180f) with { State = ActionState.CutHeld, HitstopTicks = 200 });
        duel.Step(Grab);
        duel.RunUntil(() => duel.Sim.GetActiveAbility(Guy) == null, 60);
        Assert.Equal(ActionState.CutHeld, duel.State(Victim).State);
        Assert.Equal(0ul, duel.State(Victim).InteractionId);
    }

    [Fact]
    public void ReciprocalCatchesInOneBatchClash()
    {
        var duel = new Duel().With(Victim, Standing(0f, 1.2f, 180f), Fighter.Def, Fighter.Poses);
        duel.Step(Grab, (Victim, Grab with { TargetEntityId = (byte)Guy }));
        Assert.DoesNotContain(duel.Hits, h => h.OwnerEntityId is Guy or Victim);
        Assert.Equal(0ul, duel.State(Guy).InteractionId);
        Assert.Equal(0ul, duel.State(Victim).InteractionId);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(8)]
    [InlineData(20)]
    [InlineData(60)]
    [InlineData(120)]
    [InlineData(136)]
    [InlineData(140)]
    [InlineData(150)]
    public void RestoringAWorldSnapshotMidHoldMatchesTheContinuousRun(int restoreAt)
    {
        var continuous = Holdable();
        Duel? restored = null;
        for (int tick = 0; tick < 200; tick++)
        {
            if (tick == restoreAt)
            {
                restored = Holdable();
                restored.Sim.RestoreSnapshot(continuous.Sim.CaptureSnapshot());
            }
            var input = tick == 0 ? Grab : default;
            continuous.Step(input);
            if (restored == null) continue;
            restored.Step(input);
            Assert.True(continuous.Sim.CaptureSnapshot().ContentEquals(restored.Sim.CaptureSnapshot()), $"diverged at tick {tick}");
        }
        Assert.Contains(continuous.Hits, h => h.OwnerEntityId == Guy && h.HitstopTicks > 0);
    }

    [Theory]
    [InlineData(ActionState.Shielding, 0)]
    [InlineData(ActionState.Idle, 90)]
    public void TheCpuGrabsAShieldOrAKillPercentTargetInReach(ActionState targetState, int percent)
    {
        int grabs = Cpu(Standing(0f, 2.5f, 180f) with { State = targetState, DamagePercent = (ushort)percent });
        Assert.True(grabs >= 24, $"{grabs} of 32 seeds pressed F");
        Assert.Equal(0, Cpu(Standing(0f, 6f, 180f) with { State = targetState, DamagePercent = (ushort)percent }));
    }

    /// <summary>How many of 32 seeded Hard CPU decisions, idle with F ready, press F at this target.</summary>
    private static int Cpu(CharacterState target)
    {
        var self = Standing(0f, 0f, 0f) with { EntityId = Guy };
        target.EntityId = Victim;
        var policy = new HeuristicBotPolicy();
        int grabs = 0;
        for (int seed = 0; seed < 32; seed++)
        {
            var memory = new BotMemory { Difficulty = CpuDifficulty.Hard };
            for (int i = 0; i <= BotDifficultyProfile.ForDifficulty(memory.Difficulty).ReactionDelayTicks; i++)
                memory.ObserveOpponent(target);
            var input = policy.Decide(self, target, Fighter.Def, new Random(seed), memory, BashieShellTests.Floor(), Fighter.Poses);
            if (input.ActiveSlot == AbilitySlots.F) grabs++;
        }
        return grabs;
    }

    [Fact]
    public void ThePackageOpensItsFWithAGrabThatReleasesOneTickBeforeTheKick()
    {
        foreach (bool air in new[] { false, true })
        {
            var slot = Fighter.Def.GetCookedSlotAbility(AbilitySlots.F, air)!;
            Assert.Equal(2, slot.Timeline.Stages.Count);
            Assert.True(P.HasHoldStage);
            Assert.Null(P.Launch);
            Assert.Equal(LungeStageTicks, slot.Timeline.Stages[0].DurationTicks);
            var kick = slot.Timeline.Stages[1].Operations.OfType<CookedSpawnHitboxOperation>().MaxBy(op => op.Hitbox.Damage)!;
            Assert.Equal(LungeStageTicks + kick.Tick - 1, P.ReleaseTick);
            Assert.DoesNotContain(slot.Timeline.Stages[1].Operations.OfType<CookedSpawnHitboxOperation>(),
                op => op.Hitbox.KnockbackDirection == AuthoringKnockbackDirection.TowardOwner || op.Hitbox.FixedHitstunTicks > 0);
        }
    }

    public static IEnumerable<object[]> InvalidHoldStageGrabs() => new[]
    {
        new object[] { "lunge stage length", (Action<JsonObject>)(ch => Stage(ch, 0)["durationTicks"] = 30) },
        new object[] { "one stage", (Action<JsonObject>)(ch => FSlot(ch)["timeline"]!["stages"]!.AsArray().RemoveAt(1)) },
        new object[] { "no whiff recovery", (Action<JsonObject>)(ch => Parameters(ch).Remove("whiffRecoveryTicks")) },
        new object[] { "a launching catch", (Action<JsonObject>)(ch => Capture(ch)["baseKnockback"] = 20) },
        new object[] { "a release outside the hold", (Action<JsonObject>)(ch => Parameters(ch)["releaseTick"] = 7) },
        new object[] { "no launch and no lock", (Action<JsonObject>)(ch => Parameters(ch).Remove("releaseLockTicks")) },
        new object[] { "the retired clinch", (Action<JsonObject>)(ch => FSlot(ch)["timeline"]!["stages"]![0]!["operations"]!
            .AsArray().Single(op => op!["kind"]!.GetValue<string>() == "startCapability")!["capabilityId"] = "slop.ability.clinch.v1") },
        new object[] { "a hitbox beside the lunge", (Action<JsonObject>)(ch => Stage(ch, 0)["operations"]!.AsArray()
            .Add(Stage(ch, 1)["operations"]!.AsArray().First(op => op!["kind"]!.GetValue<string>() == "spawnHitbox")!.DeepClone())) },
    };

    [Theory]
    [MemberData(nameof(InvalidHoldStageGrabs))]
    public void TheCompilerRejectsInvalidHoldStageGrabs(string name, Action<JsonObject> edit)
    {
        var json = JsonNode.Parse(System.IO.File.ReadAllText(SourcePath("character.json")))!.AsObject();
        edit(json);
        var compiled = CharacterPackageCompiler.Compile(System.IO.File.ReadAllText(SourcePath("package.json")),
            json.ToJsonString(), CharacterCookProfile.TrustedBuiltIn);
        Assert.True(compiled.HasErrors, name);
    }

    private static string SourcePath(string file)
        => System.IO.Path.Combine(TestPaths.Root, "client/Unity/Assets/CharacterPackages/fightguy", file);
    private static JsonObject FSlot(JsonObject ch)
        => ch["slots"]!.AsArray().Single(s => s!["id"]!.GetValue<string>() == "ground.F")!.AsObject();
    private static JsonObject Stage(JsonObject ch, int index) => FSlot(ch)["timeline"]!["stages"]![index]!.AsObject();
    private static JsonObject Parameters(JsonObject ch) => Stage(ch, 0)["operations"]!.AsArray()
        .Single(op => op!["kind"]!.GetValue<string>() == "startCapability")!["parameters"]!.AsObject();
    private static JsonObject Capture(JsonObject ch) => Parameters(ch)["capture"]!.AsObject();

    /// <summary>Title Match in its hold stage, with a third fighter standing by.</summary>
    private static Duel Holding()
    {
        var duel = new Duel().With(Victim, Standing(0f, 1.5f, 180f)).With(Third, Standing(4f, 0f, 270f));
        duel.Step(Grab);
        duel.RunUntil(() => duel.State(Victim).State == ActionState.CommandGrabbed, 20);
        duel.RunUntil(() => duel.Hits.Count(h => h.OwnerEntityId == Guy) >= 2, 40);
        return duel;
    }

    private static Duel Holdable() => new Duel().With(Victim, Standing(0f, 2.5f, 180f));

    private static CharacterState Standing(float x, float z, float yawDegrees)
    {
        var state = TestHelpers.PlayerState(x, z);
        state.PY = TestHelpers.GroundPY(Fighter.Def);
        state.FacingYaw = yawDegrees * MathF.PI / 180f;
        return state;
    }

    private static CharacterState Airborne(CharacterState state, float height)
        => state with { PY = state.PY + height, IsGrounded = false, AirTimeTicks = 100 };

    private sealed class Duel
    {
        internal readonly ServerSimulation Sim;
        internal readonly List<SpellResolver.HitResult> Hits = new();
        internal readonly List<TimelinePresentationEvent> Events = new();

        internal Duel(ArenaDefinition? arena = null, float airborne = 0f)
        {
            Sim = new ServerSimulation(arena ?? BashieShellTests.Floor());
            var guy = Standing(0f, 0f, 0f);
            if (airborne > 0f) guy = Airborne(guy, airborne);
            guy.EntityId = Guy;
            Sim.RegisterEntity(Guy, Fighter.Def, guy, Fighter.Poses);
        }

        internal Duel With(ulong id, CharacterState state, CharacterDefinition? def = null, BakedAnimationData? poses = null)
        {
            state.EntityId = id;
            Sim.RegisterEntity(id, def ?? Fighter.Def, state, poses ?? Fighter.Poses);
            return this;
        }

        internal CharacterState State(ulong id) => Sim.GetState(id);

        internal void Step(InputState guy = default, params (ulong Id, InputState Input)[] others)
        {
            var inputs = new Dictionary<ulong, InputState> { [Guy] = guy };
            foreach (var (id, input) in others) inputs[id] = input;
            Sim.Tick(inputs);
            Hits.AddRange(Sim.LastTickHits);
            Events.AddRange(Sim.GetPresentationEvents(clear: true));
        }

        internal void RunUntil(Func<bool> done, int maxTicks, params (ulong Id, InputState Input)[] others)
        {
            for (int tick = 0; tick < maxTicks; tick++)
            {
                if (done()) return;
                Step(default, others);
            }
            Assert.True(done(), $"not reached within {maxTicks} ticks");
        }

        internal void HitFrom(ulong owner, ulong target, float damage)
        {
            var at = State(target);
            Sim.Resolver.Spawn(new Hitbox
            {
                X = at.PX, Y = at.PY, Z = at.PZ, Radius = .4f, Shape = HitboxShape.Sphere,
                Damage = damage, BaseKnockback = 6f, KnockbackGrowth = 0f, KnockbackAngle = 45, StunTicks = 10,
                DurationTicks = 1, OwnerId = owner,
            });
        }
    }
}

/// <summary>
/// FightGuy's source without Title Match's lunge stage (runtime API 1.20.0, ADR-0057) and ADR-0053's runtime API 1.17.0
/// features (the Dragon's own hit presentation and conditioned finger), for tests that use him as a reference package for
/// other features.
/// </summary>
internal static class FightGuyFixture
{
    internal static void WithoutTitleMatch(JsonObject character)
    {
        foreach (var slot in character["slots"]!.AsArray())
        {
            var stages = slot!["timeline"]!["stages"]!.AsArray();
            if (stages.Count == 2 && stages[0]!["operations"]!.AsArray()
                    .Any(op => (string?)op!["capabilityId"] == CharacterPackageCompiler.CommandGrabCapabilityId))
                stages.RemoveAt(0);
            foreach (var stage in stages)
                foreach (var op in stage!["operations"]!.AsArray())
                {
                    op!.AsObject().Remove("condition");
                    (op["hitbox"] as JsonObject)?.Remove("hitPresentationId");
                }
        }
        var requirements = character["capabilityRequirements"]!.AsArray();
        for (int i = requirements.Count - 1; i >= 0; i--)
            if ((string?)requirements[i]!["capabilityId"] == CharacterPackageCompiler.CommandGrabCapabilityId)
                requirements.RemoveAt(i);
        var ids = character["presentationIds"]!.AsArray();
        for (int i = ids.Count - 1; i >= 0; i--)
            if ((string?)ids[i] == "presentation.fightguy.rising-dragon.sweet-hit") ids.RemoveAt(i);
    }
}
