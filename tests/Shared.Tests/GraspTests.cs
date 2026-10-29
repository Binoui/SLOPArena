using System;
using System.Linq;
using System.Text.Json.Nodes;
using SlopArena.Shared.Abilities;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// The tether's placed catch (ADR-0051, Nyx's Grasp) on Bashie's live tether slot rewritten with an atTarget block:
/// no ring is thrown, the spot is fixed at the press, the catch volume rises after the warning, a catch reels through
/// the ordinary pair, and a miss is a fixed recovery with no retract.
/// </summary>
public sealed class GraspTests
{
    private const ulong Caster = RingDuel.Bashie, Dummy = RingDuel.Dummy;
    private const ushort ThrowTick = 12, WarningTicks = 12, CatchTicks = 5, MissRecovery = 43;
    private const ushort CatchTick = ThrowTick + WarningTicks;
    private static readonly InputState Press = new() { ActiveSlot = AbilitySlots.A, TargetEntityId = (byte)Dummy };
    internal static readonly (CharacterDefinition Def, BakedAnimationData Poses) Placed = RingDuel.Load(PlaceCatch);

    internal static void PlaceCatch(JsonObject character) => TetherTests.EditTether(character, p =>
    {
        foreach (var thrown in new[] { "projectile", "retractSpeed", "retractAnimationId", "retractPresentationId" })
            p.Remove(thrown);
        p["throwTick"] = ThrowTick;
        p["missRecoveryTicks"] = MissRecovery;
        p["atTarget"] = JsonNode.Parse(
            """{"warningTicks":12,"catchTicks":5,"radius":0.8,"height":2.0,"damage":4,"maxRange":9,"noTargetDistance":3}""");
    });

    private static RingDuel Duel(float dummyX, float dummyZ)
        => new RingDuel().WithBashie(def: Placed).WithFighter(Dummy, RingDuel.Standing(dummyX, dummyZ, yawDegrees: 180f));

    [Fact]
    public void TheCatchRisesAtTheTargetsFeetAfterTheWarningAndReelsThemToHerFeet()
    {
        var duel = Duel(2f, 6f);
        duel.Step(Press);
        uint pressed = duel.Sim.CurrentTick;
        Assert.False(duel.RingOut);
        Assert.Equal(MathF.Atan2(2f, 6f), duel.State(Caster).FacingYaw, 3);

        duel.RunUntil(() => duel.State(Dummy).State == ActionState.Reeled, 60, Dummy);
        var caught = Assert.Single(duel.Hits, h => h.OwnerEntityId == Caster && h.TargetEntityId == Dummy);
        // The press tick is stage tick 1, as for every timeline move.
        Assert.Equal(pressed + CatchTick - 1, caught.MatchTick);
        Assert.Equal(4f, caught.Damage);
        Assert.Equal((ushort)4, duel.State(Dummy).DamagePercent);
        Assert.Equal((byte)DefenseInteractionPhase.Reeling, duel.State(Caster).InteractionPhase);
        Assert.Equal(duel.State(Caster).InteractionId, duel.State(Dummy).InteractionId);
        Assert.False(duel.RingOut);

        duel.RunUntil(() => duel.State(Caster).State != ActionState.Attacking, 120);
        var her = duel.State(Caster);
        var victim = duel.State(Dummy);
        Assert.Equal(ActionState.Reeled, victim.State);
        Assert.Equal(0ul, victim.InteractionId);
        float distance = MathF.Sqrt((victim.PX - her.PX) * (victim.PX - her.PX) + (victim.PZ - her.PZ) * (victim.PZ - her.PZ));
        Assert.InRange(distance, 0.6f, 1.6f);
        Assert.Empty(duel.Retracts);
    }

    [Fact]
    public void AMissIsTheFixedRecoveryWithNoRetract()
    {
        // The target stands beyond the spot's range, so the catch rises short of it.
        var duel = Duel(0f, 12f);
        duel.Step(Press);
        uint pressed = duel.Sim.CurrentTick;
        while (duel.Sim.GetActiveAbility(Caster) != null) duel.Step(new InputState { TargetEntityId = (byte)Dummy });
        Assert.Equal(pressed + CatchTick + CatchTicks + MissRecovery - 1, duel.Sim.CurrentTick);
        Assert.DoesNotContain(duel.Hits, h => h.OwnerEntityId == Caster);
        Assert.Empty(duel.Retracts);
        Assert.Equal(ActionState.Idle, duel.State(Dummy).State);
    }

    [Fact]
    public void ATargetThatLeavesTheSpotDuringTheWarningIsMissed()
    {
        var duel = Duel(0f, 5f);
        var run = new InputState { MoveX = 1f };
        duel.Step(Press, (Dummy, run));
        for (int tick = 0; tick < CatchTick + CatchTicks + 2; tick++) duel.Step(default, (Dummy, run));
        Assert.DoesNotContain(duel.Hits, h => h.OwnerEntityId == Caster);
        Assert.NotEqual(ActionState.Reeled, duel.State(Dummy).State);
        Assert.Equal(0ul, duel.State(Caster).InteractionId);
    }

    [Fact]
    public void TheSpotStaysWhereTheTargetStoodAtThePress()
    {
        // The target runs off and a third fighter steps onto the spot: the catch takes whoever stands there.
        var duel = Duel(0f, 5f).WithFighter(RingDuel.Third, RingDuel.Standing(6f, 5f));
        var run = new InputState { MoveX = 1f };
        duel.Step(Press, (Dummy, run));
        var third = duel.State(RingDuel.Third);
        third.PX = 0.2f;
        duel.Sim.SetState(RingDuel.Third, third);
        duel.RunUntil(() => duel.State(Caster).InteractionId != 0, CatchTick + 2, Dummy, (Dummy, run));
        Assert.Equal(RingDuel.Third, duel.State(Caster).InteractionPartnerId);
        Assert.Equal(ActionState.Reeled, duel.State(RingDuel.Third).State);
        Assert.NotEqual(ActionState.Reeled, duel.State(Dummy).State);
    }

    [Fact]
    public void ARestoreMidReelMatchesTheContinuousRunEveryTick()
    {
        var continuous = Duel(0f, 6f);
        continuous.Step(Press);
        continuous.RunUntil(() => continuous.State(Dummy).State == ActionState.Reeled, 60, Dummy);
        continuous.Step();
        continuous.Step();
        Assert.NotEqual(0ul, continuous.State(Caster).InteractionId);

        var restored = Duel(0f, 6f);
        var snapshot = continuous.Sim.CaptureSnapshot();
        restored.Sim.RestoreSnapshot(snapshot);
        Assert.True(snapshot.ContentEquals(restored.Sim.CaptureSnapshot()));
        for (int tick = 0; tick < 80; tick++)
        {
            continuous.Step();
            restored.Step();
            Assert.True(continuous.Sim.CaptureSnapshot().ContentEquals(restored.Sim.CaptureSnapshot()),
                $"diverged {tick + 1} ticks after the restore");
        }
        Assert.Null(restored.Sim.GetActiveAbility(Caster));
        Assert.Equal(0ul, restored.State(Dummy).InteractionId);
    }

    [Fact]
    public void ThePlacedCatchCooksAtRuntimeApi118AndRoundTripsThroughTheLoader()
    {
        var compiled = RingDuel.Compile(PlaceCatch);
        Assert.Equal("1.18.0", compiled.Metadata.RuntimeApiMin);
        var tether = RingDuel.TetherOf(Placed.Def);
        Assert.Null(tether.Projectile);
        Assert.Equal(new CookedTetherAtTarget(WarningTicks, CatchTicks, .8f, 2f, 4f, 9f, 3f), tether.AtTarget);
        var admitted = CookedCharacterPackageLoader.LoadAssembly(
            CharacterPackageAssembler.Assemble(ArmorAuthoringTests.BuildInput(compiled)));
        Assert.True(admitted.IsValid, string.Join("; ", admitted.Diagnostics));
        Assert.Equal(compiled.CanonicalBytes, admitted.Package!.CanonicalBytes);
        Assert.Equal(tether, RingDuel.TetherOf(CookedCharacterRuntimeAdapter.ToCharacterDefinition(admitted.Package!,
            CharacterClass.Bashie)));
    }

    public static TheoryData<string, Action<JsonObject>> InvalidCatches() => new()
    {
        { "a ring beside it", c => TetherTests.EditTether(c, p => p["projectile"] = JsonNode.Parse(
            """{"launchOffsetX":0,"launchOffsetY":1,"launchOffsetZ":0.5,"speed":20,"gravity":0,"radius":0.5,"damage":0,"angle":0,"baseKnockback":0,"knockbackGrowth":0,"stunTicks":0,"maxFlightTicks":30}""")) },
        { "a retract speed", c => TetherTests.EditTether(c, p => p["retractSpeed"] = 20) },
        { "no warning", c => TetherTests.EditTether(c, p => p["atTarget"]!["warningTicks"] = 0) },
        { "a flat catch", c => TetherTests.EditTether(c, p => p["atTarget"]!["height"] = 1) },
        { "a fallback past the range", c => TetherTests.EditTether(c, p => p["atTarget"]!["noTargetDistance"] = 10) },
    };

    [Theory]
    [MemberData(nameof(InvalidCatches))]
    public void TheCompilerRejectsInvalidPlacedCatches(string name, Action<JsonObject> edit)
    {
        var rejected = RingDuel.TryCompile(c => { PlaceCatch(c); edit(c); });
        Assert.True(rejected.CookedPackage == null, $"{name}: the compiler admitted it");
    }
}
