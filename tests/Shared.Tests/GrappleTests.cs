using System;
using System.Linq;
using System.Collections.Generic;
using SlopArena.Shared.Abilities;
using Xunit;
namespace SlopArena.Shared.Tests;
public sealed class GrappleTests
{
    internal static CookedGrappleCapabilityParameters Parameters => new(8, 6, 5, .2f, 14, .6f, .6f, 3, 20, 24, 24, 4, 12, 20, .3f,
        new CookedProjectile(0, .3f, .1f, 40, 0, .12f, 1, 0, 0, 0, 0, 24, 0),
        new CookedHitbox(AuthoringHitboxShape.Sphere, 1f, 0, 0, .65f, 0, 0, 0, null, null, 10, 35, 8, 20, 16, 1, true, 0));
    internal static CharacterDefinition Definition()
    {
        var def = TestHelpers.EngineDef;
        var slots = def.CookedSlots!.ToArray();
        foreach (int i in new[] { 4, 11 }) slots[i] = new CookedSlotDefinition(i, i == 4 ? "ground.E" : "air.E", i == 11,
            "Grapple", "", "", AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, 210, true, false,
            new CookedTimeline(new[] { new CookedStage(240, 0, 0, 0, 0, Array.Empty<string>(), new CookedTimelineOperation[] {
                new CookedStartCapabilityOperation(0, AuthoringUnit.Ticks, CharacterPackageCompiler.GrappleCapabilityId, "1", Parameters) }) }));
        return def with { CookedSlots = slots };
    }
    internal static ArenaDefinition Box() => CourseShellArena.FromBoxes(new[] { (-5f,-10f,0f,5f,0f,10f) }, new SpawnPoint());
    internal static CharacterState Air() => TestHelpers.PlayerState(0, -2) with { PY = -.2f, IsGrounded = false, JumpsLeft = 0 };
    private static ServerSimulation New(bool stage)
    {
        var sim = new ServerSimulation(stage ? Box() : BashieShellTests.Floor());
        sim.RegisterEntity(1, Definition(), stage ? Air() : RingDuel.Standing(0, 0));
        sim.RegisterEntity(2, TestHelpers.EngineDef, RingDuel.Standing(0, stage ? 8 : 5));
        return sim;
    }
    private static void Step(ServerSimulation sim, byte slot = 0, InputState victim = default)
        => sim.Tick(new Dictionary<ulong, InputState> { [1] = new() { ActiveSlot = slot }, [2] = victim });
    [Fact]
    public void BoxLipIsAboveFeetAndBiasRejectsTheOtherSide()
    {
        var arena = Box(); var s = Air(); var scratch = new int[arena.CollisionTriangles.Length];
        Assert.True(GrappleAnchors.TryFind(s, Definition(), arena, Parameters, 0, 1, scratch, out var a));
        Assert.InRange(a.Z, -.02f, .02f); Assert.Equal(0, a.Y, 2); Assert.True(a.PopZ > .5f);
        Assert.False(GrappleAnchors.TryFind(s, Definition(), arena, Parameters, 0, -1, scratch, out _));
    }
    [Fact]
    public void StageCatchHaulsAndPopsOntoTop()
    {
        var sim = New(true); Step(sim, AbilitySlots.E);
        for (int i = 0; i < 100; i++) Step(sim);
        var s = sim.GetState(1);
        Assert.Equal(0, s.Deaths); Assert.True(s.IsGrounded, $"{s.PX},{s.PY},{s.PZ} {s.State}");
        Assert.True(s.PZ > 0); Assert.Null(sim.GetActiveMove(1));
    }
    [Fact]
    public void FighterCatchHoldsVictimAndKickDealsOrdinaryDamage()
    {
        var sim = New(false); Step(sim, AbilitySlots.E);
        bool held = false;
        for (int i = 0; i < 90; i++) { Step(sim); held |= sim.GetState(2).State == ActionState.Reeled; }
        Assert.True(held); Assert.Equal((ushort)11, sim.GetState(2).DamagePercent);
        Assert.Equal(0ul, sim.GetState(1).InteractionId); Assert.Equal(0ul, sim.GetState(2).InteractionId);
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void MidHaulAndMidZipSnapshotsReplayExactly(bool stage)
    {
        var sim = New(stage); Step(sim, AbilitySlots.E);
        int ticks = stage ? 16 : 19;
        for (int i = 0; i < ticks; i++) Step(sim);
        if (stage) Assert.NotNull(sim.GetActiveMove(1)?.Spot);
        else Assert.NotEqual(0ul, sim.GetState(1).InteractionId);
        var copy = New(stage); copy.RestoreSnapshot(sim.CaptureSnapshot());
        for (int i = 0; i < 100; i++) { Step(sim); Step(copy); Assert.True(sim.CaptureSnapshot().ContentEquals(copy.CaptureSnapshot()), $"tick {i}"); }
    }
    [Fact]
    public void ShieldStopsLoopAndFlopIsEmittedOnce()
    {
        var sim = New(false); Step(sim, AbilitySlots.E, new InputState { ShieldHeld = true });
        int misses = 0;
        for (int i = 0; i < 90; i++)
        {
            Step(sim, victim: new InputState { ShieldHeld = true });
            misses += sim.GetPresentationEvents(true).Count(e => e.Source == PresentationEventSource.GrappleMiss);
        }
        Assert.Equal(1, misses); Assert.Equal((ushort)0, sim.GetState(2).DamagePercent);
    }
    [Fact]
    public void HeightmapSearchAndHeadingTiesAreRepeatable()
    {
        var arena = Box(); arena.CollisionTriangles = Array.Empty<CollisionTriangle>();
        var s = Air();
        Assert.True(GrappleAnchors.TryFind(s, Definition(), arena, Parameters, 0, 0, Array.Empty<int>(), out var first));
        for (int i = 0; i < 50; i++)
        {
            Assert.True(GrappleAnchors.TryFind(s, Definition(), arena, Parameters, 0, 0, Array.Empty<int>(), out var again));
            Assert.Equal(first, again);
        }
    }
    [Fact]
    public void UnderStageAndLowOverhangHaveNoStandableAnchor()
    {
        var arena = Box(); var s = Air() with { PZ = 2, PY = -2 };
        Assert.False(GrappleAnchors.TryFind(s, Definition(), arena, Parameters, 0, 0, new int[arena.CollisionTriangles.Length], out _));
        arena = CourseShellArena.FromBoxes(new[] { (-5f,-10f,0f,5f,0f,10f), (-5f,1f,0f,5f,2f,10f) }, new SpawnPoint());
        // The first lip cannot fit a standing capsule under the overhang.
        Assert.False(GrappleAnchors.TryFind(Air(), Definition(), arena, Parameters with { MaxRise = 1.5f }, 0, 1, new int[arena.CollisionTriangles.Length], out _));
    }
    [Fact]
    public void ReciprocalLoopsClashWithoutHoldingEitherFighter()
    {
        var sim = new ServerSimulation(BashieShellTests.Floor()); var def = Definition();
        sim.RegisterEntity(1, def, RingDuel.Standing(0,0)); sim.RegisterEntity(2, def, RingDuel.Standing(0,5,180));
        for (int i = 0; i < 90; i++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { [1] = new() { ActiveSlot = i == 0 ? AbilitySlots.E : (byte)0, TargetEntityId = 2 },
                [2] = new() { ActiveSlot = i == 0 ? AbilitySlots.E : (byte)0, TargetEntityId = 1 } });
            Assert.Equal(0ul, sim.GetState(1).InteractionId); Assert.Equal(0ul, sim.GetState(2).InteractionId);
        }
    }
    [Fact]
    public void ThirdPartyHitBreaksZipAndAHitBreaksHaul()
    {
        foreach (bool stage in new[] { false, true })
        {
            var sim = New(stage); sim.RegisterEntity(3, TestHelpers.EngineDef, RingDuel.Standing(4,4)); Step(sim, AbilitySlots.E);
            for (int i = 0; i < 16; i++) Step(sim);
            var s = sim.GetState(1);
            sim.Resolver.Spawn(new Hitbox { OwnerId = 3, OnlyTargetId = 1, X = s.PX, Y = s.PY, Z = s.PZ,
                Shape = HitboxShape.Sphere, Radius = 1, Damage = 3, BaseKnockback = 8, StunTicks = 12, DurationTicks = 1 });
            for (int i = 0; i < 30; i++) Step(sim);
            Assert.Equal((ushort)3, sim.GetState(1).DamagePercent);
            Assert.Null(sim.GetActiveMove(1)); Assert.Equal(0ul, sim.GetState(1).InteractionId);
            Assert.Equal(0ul, sim.GetState(2).InteractionId);
        }
    }

    [Fact]
    public void BotChoosesGrappleWhenTheSharedSearchFindsALip()
    {
        var s = Air() with { EntityId = 1, VY = -1 };
        var input = new SlopArena.Shared.AI.HeuristicBotPolicy().Decide(s, Definition(), new Random(1),
            new SlopArena.Shared.AI.BotMemory(), Box());
        Assert.Equal(AbilitySlots.E, input.ActiveSlot);
        Assert.True(input.MoveY > 0);
    }

    [Fact]
    public void AnEdgeguarderInterceptsTheLoopAimedAtALip()
    {
        var sim = New(true); Step(sim, AbilitySlots.E);
        for (int i=0;i<7;i++) Step(sim);
        sim.RegisterEntity(3, TestHelpers.EngineDef, RingDuel.Standing(0,-.8f) with { IsGrounded=false });
        bool caught=false;
        for (int i=0;i<12;i++) { Step(sim); caught |= sim.GetState(1).InteractionPartnerId==3; }
        Assert.True(caught);
    }
    [Fact]
    public void ABodyThatCannotMakeProgressSnapsRatherThanTeleportingThroughTheWall()
    {
        var sim = new ServerSimulation(Box()); var def=Definition();
        var s=Air() with { PY=-2 };
        Assert.False(sim.MoveGrappleBody(ref s, def, 0,-2,5));
        Assert.True(s.PZ<0);
    }

    [Fact]
    public void ALoopMeetingAnAlreadyHeldFighterMisses()
    {
        var sim = new ServerSimulation(BashieShellTests.Floor()); var def=Definition();
        sim.RegisterEntity(1,def,RingDuel.Standing(0,0));
        sim.RegisterEntity(2,TestHelpers.EngineDef,RingDuel.Standing(0,8));
        sim.RegisterEntity(3,def,RingDuel.Standing(-4,8));
        int misses=0;
        for (int i=0;i<100;i++)
        {
            sim.Tick(new Dictionary<ulong,InputState> {
                [1]=new() { ActiveSlot=i==0?AbilitySlots.E:(byte)0, TargetEntityId=2 },
                [2]=default, [3]=new() { ActiveSlot=i==8?AbilitySlots.E:(byte)0, TargetEntityId=2 } });
            misses+=sim.GetPresentationEvents(true).Count(e=>e.EntityId==3 && e.Source==PresentationEventSource.GrappleMiss);
        }
        Assert.Equal(1,misses); Assert.Equal((ushort)11,sim.GetState(2).DamagePercent);
    }
    [Fact]
    public void AThirdPartyHitOnTheVictimBreaksTheZipAndPreventsTheKick()
    {
        var sim=New(false); sim.RegisterEntity(3,TestHelpers.EngineDef,RingDuel.Standing(4,4));
        Step(sim,AbilitySlots.E);
        for(int i=0;i<19;i++) Step(sim);
        Assert.NotEqual(0ul,sim.GetState(2).InteractionId);
        var victim=sim.GetState(2);
        sim.Resolver.Spawn(new Hitbox { OwnerId=3, OnlyTargetId=2, X=victim.PX,Y=victim.PY,Z=victim.PZ,
            Shape=HitboxShape.Sphere, Radius=.5f, Damage=3,BaseKnockback=6,KnockbackAngle=45,StunTicks=10,DurationTicks=1 });
        for(int i=0;i<60;i++) Step(sim);
        Assert.Equal(0ul,sim.GetState(1).InteractionId);
        Assert.Equal((ushort)4,sim.GetState(2).DamagePercent);
    }

}
